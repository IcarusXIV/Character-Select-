using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Party;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using LuminaSeStringBuilder = Lumina.Text.SeStringBuilder;
using static CharacterSelectPlugin.Configuration;

namespace CharacterSelectPlugin
{
    public class PlayerNameProcessor : IDisposable
    {
        private readonly Plugin plugin;
        private readonly INamePlateGui namePlateGui;
        private readonly IChatGui chatGui;
        private readonly IClientState clientState;
        private readonly IAddonLifecycle addonLifecycle;
        private readonly IPluginLog log;
        private readonly IPartyList partyList;
        private readonly ICondition condition;

        // Wave animation
        private static readonly Stopwatch AnimationTimer = Stopwatch.StartNew();
        private const int GradientSteps = 64;
        private const int AnimationSpeed = 15;

        // Periodic redraw for smooth animation on other players' nameplates
        private DateTime lastRedrawRequest = DateTime.MinValue;
        private static readonly TimeSpan RedrawInterval = TimeSpan.FromMilliseconds(50);

        // Cached level prefix for local player (e.g. "Lv100 ")
        private byte[]? cachedLevelPrefixBytes = null;

        // Track party slot -> physical name for other players (to update when they switch CS+ characters)
        private readonly Dictionary<int, string> partySlotToPhysicalName = new();

        // Track party slot -> level prefix bytes for other players (each player has their own level)
        private readonly Dictionary<int, byte[]> partySlotPrefixBytes = new();

        // Track party composition by ObjectId to detect when party changes
        private HashSet<uint> trackedPartyObjectIds = new();

        // Target bar addons
        private static readonly string[] TargetAddonNames = { "_TargetInfo", "_TargetInfoMainTarget", "_TargetInfoCastBar" };

        // Track which physical names we've replaced on nameplates (to properly reset them)
        private readonly HashSet<string> replacedNameplateNames = new();

        public PlayerNameProcessor(
            Plugin plugin,
            INamePlateGui namePlateGui,
            IChatGui chatGui,
            IClientState clientState,
            IAddonLifecycle addonLifecycle,
            IPluginLog log,
            IPartyList partyList,
            ICondition condition)
        {
            this.plugin = plugin;
            this.namePlateGui = namePlateGui;
            this.chatGui = chatGui;
            this.clientState = clientState;
            this.addonLifecycle = addonLifecycle;
            this.log = log;
            this.partyList = partyList;
            this.condition = condition;

            namePlateGui.OnNamePlateUpdate += OnNamePlateUpdate;
            chatGui.ChatMessage += OnChatMessage;

            // Target bar updates
            addonLifecycle.RegisterListener(AddonEvent.PreDraw, TargetAddonNames, OnTargetAddonUpdate);

            // Party list updates
            addonLifecycle.RegisterListener(AddonEvent.PreDraw, "_PartyList", OnPartyListUpdate);
        }

        /// <summary>Checks if name is a full match (not substring of another word).</summary>
        private bool ContainsFullName(string text, string name)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(name))
                return false;

            var index = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;

            // Check char before
            if (index > 0)
            {
                var charBefore = text[index - 1];
                if (char.IsLetter(charBefore))
                    return false;
            }

            // Check char after
            var endIndex = index + name.Length;
            if (endIndex < text.Length)
            {
                var charAfter = text[endIndex];
                if (char.IsLetter(charAfter))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Request nameplate redraw for smooth animation on other players' names.
        /// Call this periodically from framework update when shared name replacement is enabled.
        /// </summary>
        public void RequestRedrawIfNeeded()
        {
            // Only request redraws when shared name replacement is enabled and there are replaced names
            // Skip if using simple glow for others (no animation needed)
            if (!plugin.Configuration.EnableSharedNameReplacement ||
                !plugin.Configuration.NameReplacementNameplate ||
                plugin.Configuration.UseSimpleGlowForOthers ||
                replacedNameplateNames.Count == 0)
                return;

            var now = DateTime.UtcNow;
            if (now - lastRedrawRequest < RedrawInterval)
                return;

            lastRedrawRequest = now;
            namePlateGui.RequestRedraw();
        }

        /// <summary>Get wave colour at character position.</summary>
        private Vector3 GetWaveColor(Vector3 targetColor, int charIndex)
        {
            var animationOffset = AnimationTimer.ElapsedMilliseconds / AnimationSpeed;

            // Wave pattern: position varies by char index + time
            var gradientPosition = (animationOffset + charIndex * 4) % (GradientSteps * 2);

            // Bounce: 0->64->0
            if (gradientPosition >= GradientSteps)
                gradientPosition = (GradientSteps * 2) - gradientPosition;

            var intensity = (float)gradientPosition / GradientSteps;

            return targetColor * intensity;
        }

        // Virtual key codes for modifier keys
        private const int VK_MENU = 0x12;    // Alt
        private const int VK_CONTROL = 0x11; // Ctrl
        private const int VK_SHIFT = 0x10;   // Shift

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        /// <summary>Check if reveal actual names keybind is being held.</summary>
        private bool IsRevealKeybindHeld()
        {
            if (!plugin.Configuration.EnableRevealActualNamesKeybind)
                return false;

            int vKey;

            // Use custom key if set, otherwise fall back to enum
            if (plugin.Configuration.RevealActualNamesCustomKey > 0)
            {
                vKey = plugin.Configuration.RevealActualNamesCustomKey;
            }
            else
            {
                vKey = plugin.Configuration.RevealActualNamesKey switch
                {
                    RevealNamesKeyOption.Alt => VK_MENU,
                    RevealNamesKeyOption.Ctrl => VK_CONTROL,
                    RevealNamesKeyOption.Shift => VK_SHIFT,
                    _ => 0
                };
            }

            if (vKey == 0)
                return false;

            // Check if main key is pressed
            bool mainKeyPressed = (GetAsyncKeyState(vKey) & 0x8000) != 0;
            if (!mainKeyPressed)
                return false;

            // Check modifier if one is set
            int modifier = plugin.Configuration.RevealActualNamesModifier;
            if (modifier > 0)
            {
                return (GetAsyncKeyState(modifier) & 0x8000) != 0;
            }

            return true;
        }

        /// <summary>
        /// Checks if we're in instanced content (dungeon, raid, trial, etc.)
        /// Party list uses a fixed gold glow in instances so CS+ names are always recognisable.
        /// </summary>
        private bool IsInInstance()
        {
            return condition[ConditionFlag.BoundByDuty] || condition[ConditionFlag.BoundByDuty56];
        }

        // Note: Previously had MaxWaveTextElements = 25 to switch to pair grouping for long names.
        // Now always use pair grouping to avoid conflicts with Honorific (both using per-char causes crash).

        /// <summary>Get text elements (grapheme clusters) from a string. Handles emojis correctly.</summary>
        private static List<string> GetTextElements(string name)
        {
            var elements = new List<string>();
            if (string.IsNullOrEmpty(name))
                return elements;

            var enumerator = StringInfo.GetTextElementEnumerator(name);
            while (enumerator.MoveNext())
            {
                elements.Add(enumerator.GetTextElement());
            }
            return elements;
        }

        /// <summary>Create coloured name with wave glow effect and italics.</summary>
        /// <param name="forPartyList">If true, uses gold glow in instances for visibility.</param>
        private SeString CreateColoredName(string name, Vector3 glowColor, bool forPartyList = false)
        {
            // Get text elements (grapheme clusters) - handles emojis as single units
            var textElements = GetTextElements(name);

            // Use simple glow only if explicitly configured
            if (plugin.Configuration.UseSimpleNameplateGlow)
            {
                return CreateSimpleColoredName(name, glowColor, forPartyList);
            }

            // In instances, party list uses a fixed gold glow so CS+ names are always recognisable
            if (forPartyList && IsInInstance())
                glowColor = InstancePartyListGlowColor;

            var payloads = new List<Payload>();

            payloads.Add(EmphasisItalicPayload.ItalicsOn);

            // Build coloured part with wave glow
            var builder = new LuminaSeStringBuilder();
            builder.PushColorRgba(new Vector4(1f, 1f, 1f, 1f));

            // Always use pair grouping for wave glow to avoid conflicts with Honorific
            // (both plugins using per-char wave on same nameplate causes crash)
            for (int i = 0; i < textElements.Count; i += 2)
            {
                var waveColor = GetWaveColor(glowColor, i);
                builder.PushEdgeColorRgba(new Vector4(waveColor, 1f));

                // Append current element
                builder.Append(textElements[i]);

                // Append next element if exists (pair grouping)
                if (i + 1 < textElements.Count)
                {
                    builder.Append(textElements[i + 1]);
                }

                builder.PopEdgeColor();
            }

            builder.PopColor();

            var coloredPart = SeString.Parse(builder.GetViewAsSpan());
            payloads.AddRange(coloredPart.Payloads);

            payloads.Add(EmphasisItalicPayload.ItalicsOff);

            return new SeString(payloads);
        }

        // Fixed gold glow for CS+ names in party list while in instances
        // Distinct from the default blue so Name Sync names are immediately recognisable
        private static readonly Vector3 InstancePartyListGlowColor = new(1.0f, 0.8f, 0.2f);

        /// <summary>Create coloured name with simple solid glow (no wave animation) and italics.</summary>
        /// <param name="forPartyList">If true, uses gold glow in instances for visibility.</param>
        private SeString CreateSimpleColoredName(string name, Vector3 glowColor, bool forPartyList = false)
        {
            // In instances, party list uses a fixed gold glow so CS+ names are always recognisable
            if (forPartyList && IsInInstance())
                glowColor = InstancePartyListGlowColor;

            var payloads = new List<Payload>();

            payloads.Add(EmphasisItalicPayload.ItalicsOn);

            var builder = new LuminaSeStringBuilder();
            builder.PushColorRgba(new Vector4(1f, 1f, 1f, 1f)); // White text color
            builder.PushEdgeColorRgba(new Vector4(glowColor, 1f));
            builder.Append(name);
            builder.PopEdgeColor();
            builder.PopColor();

            var coloredPart = SeString.Parse(builder.GetViewAsSpan());
            payloads.AddRange(coloredPart.Payloads);

            payloads.Add(EmphasisItalicPayload.ItalicsOff);

            return new SeString(payloads);
        }

        /// <summary>Replace name in text, preserving context (level, FC tag, etc.) with italics.</summary>
        /// <param name="useWave">If false, always use simple glow (for target bar which doesn't animate).</param>
        private SeString? CreateColoredNameWithContext(string fullText, string nameToReplace, string newName, Vector3 glowColor, bool useWave = true)
        {
            var nameIndex = fullText.IndexOf(nameToReplace, StringComparison.OrdinalIgnoreCase);
            if (nameIndex < 0)
                return null;

            var beforeName = fullText.Substring(0, nameIndex);
            var afterName = fullText.Substring(nameIndex + nameToReplace.Length);

            // If beforeName is only whitespace, discard it (but keep icons/other chars)
            if (string.IsNullOrWhiteSpace(beforeName))
                beforeName = "";

            // Get text elements for the new name
            var textElements = GetTextElements(newName);

            var payloads = new List<Payload>();

            // Text before name (not italicized)
            if (!string.IsNullOrEmpty(beforeName))
            {
                payloads.Add(new TextPayload(beforeName));
            }

            // Italics on for the name
            payloads.Add(EmphasisItalicPayload.ItalicsOn);

            var builder = new LuminaSeStringBuilder();
            builder.PushColorRgba(new Vector4(1f, 1f, 1f, 1f));

            // Use simple glow when wave is disabled (target bar) or explicitly configured
            if (!useWave || plugin.Configuration.UseSimpleNameplateGlow)
            {
                builder.PushEdgeColorRgba(new Vector4(glowColor, 1f));
                builder.Append(newName);
                builder.PopEdgeColor();
            }
            else
            {
                // Always use pair grouping for wave glow to avoid conflicts with Honorific
                // (both plugins using per-char wave on same nameplate causes crash)
                for (int i = 0; i < textElements.Count; i += 2)
                {
                    var waveColor = GetWaveColor(glowColor, i);
                    builder.PushEdgeColorRgba(new Vector4(waveColor, 1f));

                    // Append current element
                    builder.Append(textElements[i]);

                    // Append next element if exists (pair grouping)
                    if (i + 1 < textElements.Count)
                    {
                        builder.Append(textElements[i + 1]);
                    }

                    builder.PopEdgeColor();
                }
            }
            builder.PopColor();

            var coloredPart = SeString.Parse(builder.GetViewAsSpan());
            payloads.AddRange(coloredPart.Payloads);

            // Italics off
            payloads.Add(EmphasisItalicPayload.ItalicsOff);

            // Text after name (not italicized)
            if (!string.IsNullOrEmpty(afterName))
            {
                payloads.Add(new TextPayload(afterName));
            }

            return new SeString(payloads);
        }

        /// <summary>Create chat name with italics and solid glow.</summary>
        private SeString CreateChatName(string name, Vector3 glowColor)
        {
            // Chat always uses simple glow, no per-char wave - just pass name through
            var payloads = new List<Payload>();

            // Italics on
            payloads.Add(EmphasisItalicPayload.ItalicsOn);

            // Coloured name with solid glow
            var coloredBuilder = new LuminaSeStringBuilder();
            coloredBuilder.PushColorRgba(new Vector4(1f, 1f, 1f, 1f));
            coloredBuilder.PushEdgeColorRgba(new Vector4(glowColor, 1f));
            coloredBuilder.Append(name);
            coloredBuilder.PopEdgeColor();
            coloredBuilder.PopColor();

            var coloredPart = SeString.Parse(coloredBuilder.GetViewAsSpan());
            payloads.AddRange(coloredPart.Payloads);

            // Italics off
            payloads.Add(EmphasisItalicPayload.ItalicsOff);

            return new SeString(payloads);
        }

        private void OnNamePlateUpdate(
            INamePlateUpdateContext context,
            IReadOnlyList<INamePlateUpdateHandler> handlers)
        {
            var localPlayer = clientState.LocalPlayer;
            if (localPlayer == null)
                return;

            var selfReplacementEnabled = plugin.Configuration.EnableNameReplacement &&
                                         plugin.Configuration.NameReplacementNameplate;
            var sharedReplacementEnabled = plugin.Configuration.EnableSharedNameReplacement &&
                                           plugin.Configuration.NameReplacementNameplate;
            var rpProfileLookupEnabled = plugin.Configuration.ShowViewRPContextMenu;

            // Continue if any feature needs nameplate processing
            if (!selfReplacementEnabled && !sharedReplacementEnabled && !rpProfileLookupEnabled)
                return;

            // Check if reveal keybind is held - we still need to process nameplates
            // to ensure they show original names (can't just return early as modifications persist)
            var revealActualNames = IsRevealKeybindHeld();

            var activeChar = selfReplacementEnabled ? plugin.GetActiveCharacter() : null;
            if (activeChar?.ExcludeFromNameSync == true) activeChar = null; // Per-character opt-out

            foreach (var handler in handlers)
            {
                // Player nameplates only
                if (handler.NamePlateKind != NamePlateKind.PlayerCharacter)
                    continue;

                var gameObject = handler.GameObject;
                if (gameObject == null)
                    continue;

                // Local player
                if (gameObject.GameObjectId == localPlayer.GameObjectId)
                {
                    // Use Alias if set, otherwise fall back to Name
                    var displayName = !string.IsNullOrWhiteSpace(activeChar?.Alias) ? activeChar.Alias : activeChar?.Name;
                    if (selfReplacementEnabled && activeChar != null && !string.IsNullOrEmpty(displayName) && !revealActualNames)
                    {
                        // Apply CS+ name (using alias if set)
                        handler.NameParts.Text = CreateColoredName(displayName, activeChar.NameplateColor);

                        // Hide FC tag
                        if (plugin.Configuration.HideFCTagInNameplate)
                        {
                            handler.RemoveFreeCompanyTag();
                        }
                    }
                    else if (selfReplacementEnabled)
                    {
                        // Reset to original name when: reveal keybind held, excluded from sync, or no active character
                        // This ensures nameplate reverts when switching to excluded character
                        handler.NameParts.Text = new SeString(new TextPayload(localPlayer.Name.TextValue));
                    }
                }
                // Other players - process for shared name replacement or RP profile lookup
                else if (sharedReplacementEnabled || rpProfileLookupEnabled)
                {
                    var playerChar = handler.PlayerCharacter;
                    if (playerChar == null)
                        continue;

                    var playerName = playerChar.Name.TextValue;
                    var worldName = playerChar.HomeWorld.ValueNullable?.Name.ToString();

                    if (string.IsNullOrEmpty(playerName) || string.IsNullOrEmpty(worldName))
                        continue;

                    var physicalName = $"{playerName}@{worldName}";

                    // Queue lookups for both systems (they have internal checks)
                    if (sharedReplacementEnabled)
                        plugin.SharedNameManager?.QueueLookup(physicalName);
                    if (rpProfileLookupEnabled)
                        plugin.RPProfileLookupManager?.QueueLookup(physicalName);

                    // Name replacement only when shared replacement is enabled
                    if (sharedReplacementEnabled)
                    {
                        var sharedEntry = plugin.SharedNameManager?.GetCachedName(physicalName);

                        if (sharedEntry != null && !revealActualNames)
                        {
                            // Replace with their CS+ name
                            // Use simple glow if configured (disables wave animation for others)
                            if (plugin.Configuration.UseSimpleGlowForOthers)
                                handler.NameParts.Text = CreateSimpleColoredName(sharedEntry.CSName, sharedEntry.NameplateColor);
                            else
                                handler.NameParts.Text = CreateColoredName(sharedEntry.CSName, sharedEntry.NameplateColor);
                            replacedNameplateNames.Add(physicalName);
                        }
                        else if (replacedNameplateNames.Contains(physicalName))
                        {
                            // Reset to original name: either reveal is held, or they no longer have a CS+ name
                            handler.NameParts.Text = new SeString(new TextPayload(playerName));

                            // Only remove from tracking if they truly don't have a CS+ name anymore
                            // (not just reveal being held)
                            if (!revealActualNames)
                            {
                                replacedNameplateNames.Remove(physicalName);
                            }
                        }
                    }
                }
            }
        }

        private void OnChatMessage(
            XivChatType type,
            int timestamp,
            ref SeString sender,
            ref SeString message,
            ref bool isHandled)
        {
            var localPlayer = clientState.LocalPlayer;
            if (localPlayer == null)
                return;

            var selfReplacementEnabled = plugin.Configuration.EnableNameReplacement &&
                                         plugin.Configuration.NameReplacementChat;
            var sharedReplacementEnabled = plugin.Configuration.EnableSharedNameReplacement &&
                                           plugin.Configuration.NameReplacementChat;

            if (!selfReplacementEnabled && !sharedReplacementEnabled)
                return;

            // Reveal actual names while keybind held
            if (IsRevealKeybindHeld())
                return;

            var localName = localPlayer.Name.TextValue;
            var senderText = sender.TextValue;

            // Self replacement
            if (selfReplacementEnabled && senderText.Contains(localName))
            {
                var activeChar = plugin.GetActiveCharacter();
                // Use Alias if set, otherwise fall back to Name
                var chatDisplayName = !string.IsNullOrWhiteSpace(activeChar?.Alias) ? activeChar.Alias : activeChar?.Name;
                if (activeChar != null && !activeChar.ExcludeFromNameSync && !string.IsNullOrEmpty(chatDisplayName))
                {
                    sender = ReplaceSenderName(sender, localName, chatDisplayName, activeChar.NameplateColor);
                    return;
                }
            }

            // Check for shared name replacement (other players)
            if (sharedReplacementEnabled && plugin.SharedNameManager != null)
            {
                // Try to find a matching cached shared name for the sender (exclude local player's name)
                var match = plugin.SharedNameManager.FindCachedNameInText(senderText, localName);
                if (match.HasValue)
                {
                    var (sharedEntry, originalName) = match.Value;
                    sender = ReplaceSenderName(sender, originalName, sharedEntry.CSName, sharedEntry.NameplateColor);
                }
            }
        }

        /// <summary>
        /// Replaces a name in the sender SeString with a colored version
        /// </summary>
        private SeString ReplaceSenderName(SeString sender, string originalName, string newName, Vector3 glowColor)
        {
            var newPayloads = new List<Payload>();
            bool nameReplaced = false;

            foreach (var payload in sender.Payloads)
            {
                if (payload is TextPayload textPayload && textPayload.Text != null)
                {
                    if (textPayload.Text.Contains(originalName, StringComparison.OrdinalIgnoreCase) && !nameReplaced)
                    {
                        // Replace the name with colored version
                        var nameIndex = textPayload.Text.IndexOf(originalName, StringComparison.OrdinalIgnoreCase);
                        var beforeName = nameIndex > 0 ? textPayload.Text.Substring(0, nameIndex) : "";
                        var afterName = nameIndex + originalName.Length < textPayload.Text.Length
                            ? textPayload.Text.Substring(nameIndex + originalName.Length)
                            : "";

                        if (!string.IsNullOrEmpty(beforeName))
                            newPayloads.Add(new TextPayload(beforeName));

                        // Add italicized name with solid glow for chat (more visible indicator)
                        var chatName = CreateChatName(newName, glowColor);
                        newPayloads.AddRange(chatName.Payloads);

                        if (!string.IsNullOrEmpty(afterName))
                            newPayloads.Add(new TextPayload(afterName));

                        nameReplaced = true;
                    }
                    else
                    {
                        newPayloads.Add(payload);
                    }
                }
                else
                {
                    newPayloads.Add(payload);
                }
            }

            return new SeString(newPayloads);
        }

        /// <summary>
        /// Handles target bar addon updates to replace the name when it appears
        /// (covers both targeting self and being targeted by someone else - target of target)
        /// </summary>
        private unsafe void OnTargetAddonUpdate(AddonEvent type, AddonArgs args)
        {
            var localPlayer = clientState.LocalPlayer;
            if (localPlayer == null)
                return;

            var selfReplacementEnabled = plugin.Configuration.EnableNameReplacement &&
                                         plugin.Configuration.NameReplacementNameplate;
            var sharedReplacementEnabled = plugin.Configuration.EnableSharedNameReplacement &&
                                           plugin.Configuration.NameReplacementNameplate;

            if (!selfReplacementEnabled && !sharedReplacementEnabled)
                return;

            // Check reveal keybind - if held, don't apply name replacements
            // The game will naturally update target bar with original names
            var revealActualNames = IsRevealKeybindHeld();
            if (revealActualNames)
                return;

            try
            {
                if (args.Addon.IsNull)
                    return;

                var addon = (AtkUnitBase*)args.Addon.Address;
                if (!addon->IsVisible)
                    return;

                // Replace local player's name if self replacement is enabled
                if (selfReplacementEnabled)
                {
                    var activeChar = plugin.GetActiveCharacter();
                    // Use Alias if set, otherwise fall back to Name
                    var targetDisplayName = !string.IsNullOrWhiteSpace(activeChar?.Alias) ? activeChar.Alias : activeChar?.Name;
                    if (activeChar != null && !activeChar.ExcludeFromNameSync && !string.IsNullOrEmpty(targetDisplayName))
                    {
                        var localName = localPlayer.Name.TextValue;
                        ReplaceNameInAllTextNodes(addon, localName, targetDisplayName, activeChar.NameplateColor);
                    }
                }

                // Replace other players' names if shared replacement is enabled
                if (sharedReplacementEnabled)
                {
                    ReplaceSharedNamesInTextNodes(addon);
                }
            }
            catch (Exception ex)
            {
                // Silently fail - addon structure may have changed
                log.Debug($"Failed to update target addon: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles party list addon updates to replace the local player's name
        /// </summary>
        private unsafe void OnPartyListUpdate(AddonEvent type, AddonArgs args)
        {
            try
            {
                if (args.Addon.IsNull)
                    return;

                var addon = (AddonPartyList*)args.Addon.Address;

                // Early capture removed - we capture prefix inline when we find the name

                var selfReplacementEnabled = plugin.Configuration.EnableNameReplacement &&
                                             plugin.Configuration.NameReplacementPartyList;
                var sharedReplacementEnabled = plugin.Configuration.EnableSharedNameReplacement &&
                                               plugin.Configuration.NameReplacementPartyList;

                if (!selfReplacementEnabled && !sharedReplacementEnabled)
                    return;

                // Reveal actual names while keybind held
                if (IsRevealKeybindHeld())
                    return;

                UpdatePartyListNames(addon, forceUpdate: false, selfReplacementEnabled, sharedReplacementEnabled);
            }
            catch (Exception ex)
            {
                log.Debug($"Failed to update party list addon: {ex.Message}");
            }
        }

        /// <summary>
        /// Manually refreshes the party list name replacement.
        /// Call this after character switching to force an update.
        /// </summary>
        public unsafe void RefreshPartyList()
        {
            var selfReplacementEnabled = plugin.Configuration.EnableNameReplacement &&
                                         plugin.Configuration.NameReplacementPartyList;
            var sharedReplacementEnabled = plugin.Configuration.EnableSharedNameReplacement &&
                                           plugin.Configuration.NameReplacementPartyList;

            if (!selfReplacementEnabled && !sharedReplacementEnabled)
                return;

            try
            {
                var atkStage = AtkStage.Instance();
                if (atkStage == null)
                    return;

                var unitManager = atkStage->RaptureAtkUnitManager;
                if (unitManager == null)
                    return;

                var addon = (AddonPartyList*)unitManager->GetAddonByName("_PartyList");
                if (addon == null)
                    return;

                UpdatePartyListNames(addon, forceUpdate: true, selfReplacementEnabled, sharedReplacementEnabled);
            }
            catch (Exception ex)
            {
                log.Warning($"Failed to refresh party list: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if party composition has changed and clears tracking if so.
        /// This prevents stale CS+ names from being applied to new party members.
        /// </summary>
        private void CheckForPartyChange()
        {
            // Skip if we have no tracking to validate - avoid unnecessary work
            if (partySlotToPhysicalName.Count == 0 && trackedPartyObjectIds.Count == 0)
                return;

            var currentObjectIds = new HashSet<uint>();

            for (int i = 0; i < partyList.Length; i++)
            {
                var member = partyList[i];
                if (member != null && member.ObjectId != 0)
                {
                    currentObjectIds.Add(member.ObjectId);
                }
            }

            // Check if party composition changed
            if (!currentObjectIds.SetEquals(trackedPartyObjectIds))
            {
                // Clear tracking if we had any
                if (partySlotToPhysicalName.Count > 0 || partySlotPrefixBytes.Count > 0)
                {
                    log.Debug($"[PartyList] Party composition changed (was {trackedPartyObjectIds.Count} members, now {currentObjectIds.Count}) - clearing all tracking");
                    partySlotToPhysicalName.Clear();
                    partySlotPrefixBytes.Clear();
                }

                trackedPartyObjectIds = currentObjectIds;
            }
        }

        /// <summary>
        /// Core logic for updating party list names
        /// </summary>
        private unsafe void UpdatePartyListNames(AddonPartyList* addon, bool forceUpdate, bool selfReplacementEnabled, bool sharedReplacementEnabled)
        {
            if (addon == null || !addon->AtkUnitBase.IsVisible)
                return;

            var localPlayer = clientState.LocalPlayer;
            if (localPlayer == null)
                return;

            var localName = localPlayer.Name.TextValue;

            var activeChar = selfReplacementEnabled ? plugin.GetActiveCharacter() : null;
            if (activeChar?.ExcludeFromNameSync == true) activeChar = null; // Per-character opt-out

            // Check for party composition changes - clear all tracking if party changed
            CheckForPartyChange();

            // Process all slots - local player is ALWAYS slot 0
            // Note: We rely on CheckForPartyChange() (ObjectId-based) to detect party composition changes.
            // Don't validate against IPartyList indices here - they don't match addon slot indices reliably.
            for (int i = 0; i < addon->MemberCount && i < 8; i++)
            {
                var member = addon->PartyMembers[i];
                var nameNode = member.Name;

                if (nameNode == null)
                    continue;

                // Parse SeString to get plain text
                string plainText;
                byte[] rawBytes;
                try
                {
                    rawBytes = nameNode->NodeText.AsSpan().ToArray();
                    var seString = SeString.Parse(rawBytes);
                    plainText = seString.TextValue;
                }
                catch
                {
                    plainText = nameNode->NodeText.ToString();
                    rawBytes = null;
                }

                if (string.IsNullOrEmpty(plainText))
                    continue;

                // Slot 0 is always the local player
                bool isOurSlot = (i == 0);

                if (isOurSlot && selfReplacementEnabled && activeChar != null && !string.IsNullOrEmpty(activeChar.Name))
                {
                    // Always construct level prefix from player's level to avoid malformed SeString issues
                    // (raw bytes from party list may contain incomplete SeString sequences)
                    try
                    {
                        if (localPlayer != null && localPlayer.Level > 0)
                        {
                            var prefixBytes = ConstructLevelPrefix(localPlayer.Level);
                            cachedLevelPrefixBytes = prefixBytes;

                            // Use Alias if set, otherwise fall back to Name
                            var selfDisplayName = !string.IsNullOrWhiteSpace(activeChar.Alias) ? activeChar.Alias : activeChar.Name;
                            var coloredName = CreateSimpleColoredName(selfDisplayName, activeChar.NameplateColor, forPartyList: true);
                            var coloredBytes = coloredName.Encode();

                            // +1 for null terminator - SetText expects null-terminated C string
                            var finalBytes = new byte[prefixBytes.Length + coloredBytes.Length + 1];
                            Array.Copy(prefixBytes, 0, finalBytes, 0, prefixBytes.Length);
                            Array.Copy(coloredBytes, 0, finalBytes, prefixBytes.Length, coloredBytes.Length);
                            // finalBytes[last] is already 0 from array initialization

                            nameNode->SetText(finalBytes);
                        }
                        else
                        {
                            // Fallback: no prefix available at all - Encode() doesn't include null terminator
                            // Use Alias if set, otherwise fall back to Name
                            var selfDisplayNameFallback = !string.IsNullOrWhiteSpace(activeChar.Alias) ? activeChar.Alias : activeChar.Name;
                            var fallbackName = CreateSimpleColoredName(selfDisplayNameFallback, activeChar.NameplateColor, forPartyList: true);
                            var fallbackBytes = fallbackName.Encode();
                            var nullTerminated = new byte[fallbackBytes.Length + 1];
                            Array.Copy(fallbackBytes, 0, nullTerminated, 0, fallbackBytes.Length);
                            nameNode->SetText(nullTerminated);
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Debug($"Failed to set party list name (self): {ex.Message}");
                    }
                }
                // Check for shared name replacement (other players - slots 1+)
                else if (!isOurSlot && sharedReplacementEnabled && plugin.SharedNameManager != null)
                {
                    // Party list shows just "FirstName LastName" without world
                    // Try to find a matching cached shared name by physical name in text
                    var match = plugin.SharedNameManager.FindCachedNameInText(plainText, localName);

                    if (match.HasValue)
                    {
                        var (sharedEntry, originalName) = match.Value;
                        // Store this slot's physical name for future lookups (when they switch CS+ characters)
                        partySlotToPhysicalName[i] = originalName;

                        // Use pattern detection to get ONLY the level icon bytes (safe, no SeString issues)
                        byte[]? slotPrefix = null;
                        if (rawBytes != null && rawBytes.Length > 0)
                        {
                            slotPrefix = FindLevelPrefixByPattern(rawBytes);
                            if (slotPrefix != null)
                            {
                                partySlotPrefixBytes[i] = slotPrefix;
                            }
                        }

                        // Replace with their CS+ name using the captured prefix
                        try
                        {
                            var coloredName = CreateSimpleColoredName(sharedEntry.CSName, sharedEntry.NameplateColor, forPartyList: true);
                            var coloredBytes = coloredName.Encode();
                            if (slotPrefix != null)
                            {
                                // +1 for null terminator - SetText expects null-terminated C string
                                var finalBytes = new byte[slotPrefix.Length + coloredBytes.Length + 1];
                                Array.Copy(slotPrefix, 0, finalBytes, 0, slotPrefix.Length);
                                Array.Copy(coloredBytes, 0, finalBytes, slotPrefix.Length, coloredBytes.Length);
                                nameNode->SetText(finalBytes);
                            }
                            else
                            {
                                // Fallback if no prefix captured - add null terminator
                                var nullTerminated = new byte[coloredBytes.Length + 1];
                                Array.Copy(coloredBytes, 0, nullTerminated, 0, coloredBytes.Length);
                                nameNode->SetText(nullTerminated);
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Debug($"Failed to set party list name (shared): {ex.Message}");
                        }
                    }
                    // If no match by physical name in text, check if we have a stored physical name for this slot
                    else if (partySlotToPhysicalName.TryGetValue(i, out var storedPhysicalName))
                    {
                        // First check: if the text already contains their in-game name, they switched to no-profile
                        if (plainText.Contains(storedPhysicalName, StringComparison.OrdinalIgnoreCase))
                        {
                            // Their in-game name is showing - clear tracking
                            partySlotToPhysicalName.Remove(i);
                            partySlotPrefixBytes.Remove(i);
                        }
                        else
                        {
                            // Their in-game name is NOT in the text (we previously replaced it)
                            var entry = plugin.SharedNameManager.GetCachedNameByCharacterName(storedPhysicalName);
                            if (entry != null && !string.IsNullOrEmpty(entry.CSName))
                            {
                                // Use pattern detection or stored prefix (safe level icon bytes only)
                                byte[]? slotPrefix = null;
                                if (rawBytes != null && rawBytes.Length > 0)
                                {
                                    slotPrefix = FindLevelPrefixByPattern(rawBytes);
                                    if (slotPrefix != null)
                                    {
                                        partySlotPrefixBytes[i] = slotPrefix;
                                    }
                                }

                                // Fall back to stored prefix if pattern detection failed
                                if (slotPrefix == null && partySlotPrefixBytes.TryGetValue(i, out var storedPrefix))
                                {
                                    slotPrefix = storedPrefix;
                                }

                                try
                                {
                                    var coloredName = CreateSimpleColoredName(entry.CSName, entry.NameplateColor, forPartyList: true);
                                    var coloredBytes = coloredName.Encode();
                                    if (slotPrefix != null)
                                    {
                                        // +1 for null terminator - SetText expects null-terminated C string
                                        var finalBytes = new byte[slotPrefix.Length + coloredBytes.Length + 1];
                                        Array.Copy(slotPrefix, 0, finalBytes, 0, slotPrefix.Length);
                                        Array.Copy(coloredBytes, 0, finalBytes, slotPrefix.Length, coloredBytes.Length);
                                        nameNode->SetText(finalBytes);
                                    }
                                    else
                                    {
                                        // Fallback - add null terminator
                                        var nullTerminated = new byte[coloredBytes.Length + 1];
                                        Array.Copy(coloredBytes, 0, nullTerminated, 0, coloredBytes.Length);
                                        nameNode->SetText(nullTerminated);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    log.Debug($"Failed to set party list name (stored shared): {ex.Message}");
                                }
                            }
                            else
                            {
                                // No CS+ name found - revert to in-game name (just use plain text, no prefix concat)
                                try
                                {
                                    nameNode->SetText(storedPhysicalName);
                                }
                                catch (Exception ex)
                                {
                                    log.Debug($"Failed to revert party list name: {ex.Message}");
                                }
                                partySlotToPhysicalName.Remove(i);
                                partySlotPrefixBytes.Remove(i);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Finds a byte sequence within a larger byte array
        /// </summary>
        private int FindByteSequence(byte[] source, byte[] pattern)
        {
            if (pattern.Length == 0 || source.Length < pattern.Length)
                return -1;

            for (int i = 0; i <= source.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Constructs level prefix bytes for a given level.
        /// FFXIV uses Private Use Area characters (EE 81 XX) for level display.
        /// Format: [Lv icon] [digit icons...] [space]
        /// </summary>
        private byte[] ConstructLevelPrefix(int level)
        {
            // Based on observed pattern: EE-81-AA is "Lv" icon, EE-81-A0 is "0", EE-81-A1 is "1", etc.
            var bytes = new List<byte>();

            // Add "Lv" indicator icon
            bytes.AddRange(new byte[] { 0xEE, 0x81, 0xAA });

            // Add each digit
            var levelStr = level.ToString();
            foreach (char c in levelStr)
            {
                int digit = c - '0';
                bytes.AddRange(new byte[] { 0xEE, 0x81, (byte)(0xA0 + digit) });
            }

            // Add space
            bytes.Add(0x20);

            return bytes.ToArray();
        }

        /// <summary>
        /// Finds the level prefix in party list bytes by detecting FFXIV level icon patterns.
        /// Level icons are encoded as EE-81-XX (Private Use Area characters).
        /// Format: [level icons] [space] [name]
        /// Returns the prefix bytes including the space, or null if not found.
        /// </summary>
        private byte[]? FindLevelPrefixByPattern(byte[] rawBytes)
        {
            if (rawBytes == null || rawBytes.Length < 4)
                return null;

            // Look for FFXIV Private Use Area characters (EE 81 XX) which are level icons
            // These appear before the player name in the party list
            int lastLevelIconEnd = -1;

            for (int i = 0; i <= rawBytes.Length - 3; i++)
            {
                // Check for EE 81 XX pattern (level icon character)
                if (rawBytes[i] == 0xEE && rawBytes[i + 1] == 0x81)
                {
                    // Found a level icon character, mark end position (after the 3-byte sequence)
                    lastLevelIconEnd = i + 3;
                }
            }

            if (lastLevelIconEnd > 0)
            {
                // Check if there's a space (0x20) after the last level icon
                if (lastLevelIconEnd < rawBytes.Length && rawBytes[lastLevelIconEnd] == 0x20)
                {
                    // Include the space in the prefix
                    lastLevelIconEnd++;
                }

                // Return everything from start to end of level icons (including space)
                return rawBytes.Take(lastLevelIconEnd).ToArray();
            }

            return null;
        }

        /// <summary>
        /// Replaces the player's name in all text nodes of an addon
        /// </summary>
        private unsafe void ReplaceNameInAllTextNodes(AtkUnitBase* addon, string originalName, string newName, Vector3 glowColor)
        {
            if (addon == null)
                return;

            var nodeList = addon->UldManager.NodeList;
            var nodeCount = addon->UldManager.NodeListCount;

            for (var i = 0; i < nodeCount; i++)
            {
                var node = nodeList[i];
                if (node == null || node->Type != NodeType.Text)
                    continue;

                var textNode = (AtkTextNode*)node;

                // Parse to get plain text
                string plainText;
                try
                {
                    var rawBytes = textNode->NodeText.AsSpan().ToArray();
                    var seString = SeString.Parse(rawBytes);
                    plainText = seString.TextValue;
                }
                catch
                {
                    plainText = textNode->NodeText.ToString();
                }

                if (!string.IsNullOrEmpty(plainText) && plainText.Contains(originalName, StringComparison.OrdinalIgnoreCase))
                {
                    // Target bar doesn't animate, so use simple glow (useWave: false)
                    var replacedSeString = CreateColoredNameWithContext(plainText, originalName, newName, glowColor, useWave: false);
                    if (replacedSeString != null)
                    {
                        try
                        {
                            textNode->SetText(replacedSeString.Encode());
                        }
                        catch
                        {
                            // Silently fail - prevents crash from propagating
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Replaces other players' names in text nodes using cached shared names
        /// </summary>
        private unsafe void ReplaceSharedNamesInTextNodes(AtkUnitBase* addon)
        {
            if (addon == null || plugin.SharedNameManager == null)
                return;

            // Get local player name to exclude from shared replacement
            var localPlayer = clientState.LocalPlayer;
            var localName = localPlayer?.Name.TextValue;

            var nodeList = addon->UldManager.NodeList;
            var nodeCount = addon->UldManager.NodeListCount;

            for (var i = 0; i < nodeCount; i++)
            {
                var node = nodeList[i];
                if (node == null || node->Type != NodeType.Text)
                    continue;

                var textNode = (AtkTextNode*)node;

                // Parse to get plain text
                string plainText;
                try
                {
                    var rawBytes = textNode->NodeText.AsSpan().ToArray();
                    var seString = SeString.Parse(rawBytes);
                    plainText = seString.TextValue;
                }
                catch
                {
                    plainText = textNode->NodeText.ToString();
                }

                if (string.IsNullOrEmpty(plainText))
                    continue;

                // Try to find a cached name that appears anywhere in this text (exclude local player)
                var match = plugin.SharedNameManager.FindCachedNameInText(plainText, localName);
                if (match.HasValue)
                {
                    var (sharedEntry, originalName) = match.Value;
                    // Target bar doesn't animate, so use simple glow (useWave: false)
                    var replacedSeString = CreateColoredNameWithContext(plainText, originalName, sharedEntry.CSName, sharedEntry.NameplateColor, useWave: false);
                    if (replacedSeString != null)
                    {
                        try
                        {
                            textNode->SetText(replacedSeString.Encode());
                        }
                        catch
                        {
                            // Silently fail - prevents crash from propagating
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            namePlateGui.OnNamePlateUpdate -= OnNamePlateUpdate;
            chatGui.ChatMessage -= OnChatMessage;
            addonLifecycle.UnregisterListener(OnTargetAddonUpdate);
            addonLifecycle.UnregisterListener(OnPartyListUpdate);
        }
    }
}

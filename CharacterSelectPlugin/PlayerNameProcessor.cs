using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
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

        // Wave animation
        private static readonly Stopwatch AnimationTimer = Stopwatch.StartNew();
        private const int GradientSteps = 64;
        private const int AnimationSpeed = 15;

        // Cached level prefix for local player (e.g. "Lv100 ")
        private byte[]? cachedLevelPrefixBytes = null;

        // Track party slot -> physical name for other players (to update when they switch CS+ characters)
        private readonly Dictionary<int, string> partySlotToPhysicalName = new();

        // Track party slot -> level prefix bytes for other players (each player has their own level)
        private readonly Dictionary<int, byte[]> partySlotPrefixBytes = new();

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
            IPluginLog log)
        {
            this.plugin = plugin;
            this.namePlateGui = namePlateGui;
            this.chatGui = chatGui;
            this.clientState = clientState;
            this.addonLifecycle = addonLifecycle;
            this.log = log;

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

        // Max text elements for wave animation (prevents SeString payload overflow)
        // Each text element gets a color push/pop, so limit to prevent overflow
        private const int MaxWaveTextElements = 25;

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
        private SeString CreateColoredName(string name, Vector3 glowColor)
        {
            // Get text elements (grapheme clusters) - handles emojis as single units
            var textElements = GetTextElements(name);

            // Use simple glow only if explicitly configured
            if (plugin.Configuration.UseSimpleNameplateGlow)
            {
                return CreateSimpleColoredName(name, glowColor);
            }

            var payloads = new List<Payload>();

            // Italics on
            payloads.Add(EmphasisItalicPayload.ItalicsOn);

            // Build coloured part with wave glow
            var builder = new LuminaSeStringBuilder();
            builder.PushColorRgba(new Vector4(1f, 1f, 1f, 1f));

            // For long names (>25 chars), group by pairs to reduce payloads (like Honorific does)
            if (textElements.Count > MaxWaveTextElements)
            {
                for (int i = 0; i < textElements.Count; i += 2)
                {
                    var waveColor = GetWaveColor(glowColor, i);
                    builder.PushEdgeColorRgba(new Vector4(waveColor, 1f));

                    // Append current element
                    if (textElements[i].Length == 1)
                        builder.AppendChar(textElements[i][0]);
                    else
                        builder.Append(textElements[i]);

                    // Append next element if exists (pair grouping)
                    if (i + 1 < textElements.Count)
                    {
                        if (textElements[i + 1].Length == 1)
                            builder.AppendChar(textElements[i + 1][0]);
                        else
                            builder.Append(textElements[i + 1]);
                    }

                    builder.PopEdgeColor();
                }
            }
            else
            {
                // Per-element wave glow for short names
                for (int i = 0; i < textElements.Count; i++)
                {
                    var waveColor = GetWaveColor(glowColor, i);
                    builder.PushEdgeColorRgba(new Vector4(waveColor, 1f));

                    // Use AppendChar for single chars (preserves original behavior), Append for multi-char (emojis)
                    if (textElements[i].Length == 1)
                        builder.AppendChar(textElements[i][0]);
                    else
                        builder.Append(textElements[i]);

                    builder.PopEdgeColor();
                }
            }

            builder.PopColor();

            var coloredPart = SeString.Parse(builder.GetViewAsSpan());
            payloads.AddRange(coloredPart.Payloads);

            // Italics off
            payloads.Add(EmphasisItalicPayload.ItalicsOff);

            return new SeString(payloads);
        }

        /// <summary>Create coloured name with simple solid glow (no wave animation).</summary>
        private SeString CreateSimpleColoredName(string name, Vector3 glowColor)
        {
            // Simple glow with italics
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
            // For long names (>25 chars), group by pairs to reduce payloads (like Honorific does)
            else if (textElements.Count > MaxWaveTextElements)
            {
                for (int i = 0; i < textElements.Count; i += 2)
                {
                    var waveColor = GetWaveColor(glowColor, i);
                    builder.PushEdgeColorRgba(new Vector4(waveColor, 1f));

                    // Append current element
                    if (textElements[i].Length == 1)
                        builder.AppendChar(textElements[i][0]);
                    else
                        builder.Append(textElements[i]);

                    // Append next element if exists (pair grouping)
                    if (i + 1 < textElements.Count)
                    {
                        if (textElements[i + 1].Length == 1)
                            builder.AppendChar(textElements[i + 1][0]);
                        else
                            builder.Append(textElements[i + 1]);
                    }

                    builder.PopEdgeColor();
                }
            }
            else
            {
                // Per-element wave glow for short names
                for (int i = 0; i < textElements.Count; i++)
                {
                    var waveColor = GetWaveColor(glowColor, i);
                    builder.PushEdgeColorRgba(new Vector4(waveColor, 1f));

                    // Use AppendChar for single chars (preserves original behavior), Append for multi-char (emojis)
                    if (textElements[i].Length == 1)
                        builder.AppendChar(textElements[i][0]);
                    else
                        builder.Append(textElements[i]);

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
                    if (selfReplacementEnabled && activeChar != null && !string.IsNullOrEmpty(activeChar.Name) && !revealActualNames)
                    {
                        // Apply CS+ name
                        handler.NameParts.Text = CreateColoredName(activeChar.Name, activeChar.NameplateColor);

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
                if (activeChar != null && !activeChar.ExcludeFromNameSync && !string.IsNullOrEmpty(activeChar.Name))
                {
                    sender = ReplaceSenderName(sender, localName, activeChar.Name, activeChar.NameplateColor);
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
                    if (activeChar != null && !activeChar.ExcludeFromNameSync && !string.IsNullOrEmpty(activeChar.Name))
                    {
                        var localName = localPlayer.Name.TextValue;
                        ReplaceNameInAllTextNodes(addon, localName, activeChar.Name, activeChar.NameplateColor);
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

            // Process all slots - local player is ALWAYS slot 0
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
                    if (rawBytes != null && rawBytes.Length > 0)
                    {
                        // Find where the name starts - could be original name OR our CS+ name if already replaced
                        int nameStartIndex = -1;

                        // First try: find the original in-game name
                        var realNameBytes = System.Text.Encoding.UTF8.GetBytes(localName);
                        nameStartIndex = FindByteSequence(rawBytes, realNameBytes);

                        // Second try: if not found, search for the CS+ name (we may have already replaced it)
                        if (nameStartIndex < 0)
                        {
                            // The CS+ name might be in the SeString - search for it in plain text form
                            var csNameBytes = System.Text.Encoding.UTF8.GetBytes(activeChar.Name);
                            nameStartIndex = FindByteSequence(rawBytes, csNameBytes);
                        }

                        // Third try: use pattern detection to find where level icons end
                        if (nameStartIndex < 0)
                        {
                            var patternPrefix = FindLevelPrefixByPattern(rawBytes);
                            if (patternPrefix != null)
                            {
                                nameStartIndex = patternPrefix.Length;
                            }
                        }

                        if (nameStartIndex > 0)
                        {
                            // Found where name starts - everything before it is the level prefix
                            var prefixBytes = rawBytes.Take(nameStartIndex).ToArray();
                            cachedLevelPrefixBytes = prefixBytes; // Cache for future use

                            // Build: [level prefix] + [colored CS+ name]
                            var coloredName = CreateColoredName(activeChar.Name, activeChar.NameplateColor);
                            var coloredBytes = coloredName.Encode();

                            var finalBytes = new byte[prefixBytes.Length + coloredBytes.Length];
                            Array.Copy(prefixBytes, 0, finalBytes, 0, prefixBytes.Length);
                            Array.Copy(coloredBytes, 0, finalBytes, prefixBytes.Length, coloredBytes.Length);

                            nameNode->SetText(finalBytes);
                            continue;
                        }

                        // Last resort: construct prefix from player's level
                        if (localPlayer != null && localPlayer.Level > 0)
                        {
                            var prefixBytes = ConstructLevelPrefix(localPlayer.Level);
                            cachedLevelPrefixBytes = prefixBytes;

                            var coloredName = CreateColoredName(activeChar.Name, activeChar.NameplateColor);
                            var coloredBytes = coloredName.Encode();

                            var finalBytes = new byte[prefixBytes.Length + coloredBytes.Length];
                            Array.Copy(prefixBytes, 0, finalBytes, 0, prefixBytes.Length);
                            Array.Copy(coloredBytes, 0, finalBytes, prefixBytes.Length, coloredBytes.Length);

                            nameNode->SetText(finalBytes);
                            continue;
                        }
                    }

                    // Fallback: no prefix available at all
                    var fallbackName = CreateColoredName(activeChar.Name, activeChar.NameplateColor);
                    nameNode->SetText(fallbackName.Encode());
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

                        // Capture this slot's level prefix from raw bytes (each player has their own level)
                        byte[]? slotPrefix = null;
                        if (rawBytes != null && rawBytes.Length > 0)
                        {
                            // First try: find the original in-game name
                            var nameBytes = System.Text.Encoding.UTF8.GetBytes(originalName);
                            int nameIndex = FindByteSequence(rawBytes, nameBytes);

                            // Second try: find the CS+ name (we may have already replaced it)
                            if (nameIndex < 0)
                            {
                                var csNameBytes = System.Text.Encoding.UTF8.GetBytes(sharedEntry.CSName);
                                nameIndex = FindByteSequence(rawBytes, csNameBytes);
                            }

                            // Third try: pattern detection
                            if (nameIndex < 0)
                            {
                                var patternPrefix = FindLevelPrefixByPattern(rawBytes);
                                if (patternPrefix != null)
                                {
                                    nameIndex = patternPrefix.Length;
                                }
                            }

                            if (nameIndex > 0)
                            {
                                slotPrefix = rawBytes.Take(nameIndex).ToArray();
                                partySlotPrefixBytes[i] = slotPrefix;
                            }
                        }

                        // Replace with their CS+ name using the captured prefix
                        var coloredName = CreateColoredName(sharedEntry.CSName, sharedEntry.NameplateColor);
                        if (slotPrefix != null)
                        {
                            var coloredBytes = coloredName.Encode();
                            var finalBytes = new byte[slotPrefix.Length + coloredBytes.Length];
                            Array.Copy(slotPrefix, 0, finalBytes, 0, slotPrefix.Length);
                            Array.Copy(coloredBytes, 0, finalBytes, slotPrefix.Length, coloredBytes.Length);
                            nameNode->SetText(finalBytes);
                        }
                        else
                        {
                            // Fallback if no prefix captured
                            nameNode->SetText(coloredName.Encode());
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
                                // Try to capture/update prefix from current raw bytes
                                byte[]? slotPrefix = null;
                                if (rawBytes != null && rawBytes.Length > 0)
                                {
                                    // Try to find the CS+ name in raw bytes
                                    var csNameBytes = System.Text.Encoding.UTF8.GetBytes(entry.CSName);
                                    int nameIndex = FindByteSequence(rawBytes, csNameBytes);

                                    // Try pattern detection
                                    if (nameIndex < 0)
                                    {
                                        var patternPrefix = FindLevelPrefixByPattern(rawBytes);
                                        if (patternPrefix != null)
                                        {
                                            nameIndex = patternPrefix.Length;
                                        }
                                    }

                                    if (nameIndex > 0)
                                    {
                                        slotPrefix = rawBytes.Take(nameIndex).ToArray();
                                        partySlotPrefixBytes[i] = slotPrefix;
                                    }
                                    else if (partySlotPrefixBytes.TryGetValue(i, out var storedPrefix))
                                    {
                                        slotPrefix = storedPrefix;
                                    }
                                }

                                var coloredName = CreateColoredName(entry.CSName, entry.NameplateColor);
                                if (slotPrefix != null)
                                {
                                    var coloredBytes = coloredName.Encode();
                                    var finalBytes = new byte[slotPrefix.Length + coloredBytes.Length];
                                    Array.Copy(slotPrefix, 0, finalBytes, 0, slotPrefix.Length);
                                    Array.Copy(coloredBytes, 0, finalBytes, slotPrefix.Length, coloredBytes.Length);
                                    nameNode->SetText(finalBytes);
                                }
                                else
                                {
                                    nameNode->SetText(coloredName.Encode());
                                }
                            }
                            else
                            {
                                // No CS+ name found - revert to in-game name
                                if (partySlotPrefixBytes.TryGetValue(i, out var slotPrefix) && slotPrefix != null)
                                {
                                    var inGameNameBytes = System.Text.Encoding.UTF8.GetBytes(storedPhysicalName);
                                    var finalBytes = new byte[slotPrefix.Length + inGameNameBytes.Length];
                                    Array.Copy(slotPrefix, 0, finalBytes, 0, slotPrefix.Length);
                                    Array.Copy(inGameNameBytes, 0, finalBytes, slotPrefix.Length, inGameNameBytes.Length);
                                    nameNode->SetText(finalBytes);
                                }
                                else
                                {
                                    nameNode->SetText(storedPhysicalName);
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
                        textNode->SetText(replacedSeString.Encode());
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
                        textNode->SetText(replacedSeString.Encode());
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

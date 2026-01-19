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

        // Local player's party list slot
        private int lastKnownLocalPlayerSlot = -1;

        // Cached level prefix (e.g. "Lv100 ")
        private byte[]? cachedLevelPrefixBytes = null;

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

        /// <summary>Create coloured name with wave glow effect and italics.</summary>
        private SeString CreateColoredName(string name, Vector3 glowColor)
        {
            var payloads = new List<Payload>();

            // Italics on
            payloads.Add(EmphasisItalicPayload.ItalicsOn);

            // Build coloured part with wave glow
            var builder = new LuminaSeStringBuilder();
            builder.PushColorRgba(new Vector4(1f, 1f, 1f, 1f));

            // Per-char wave glow
            for (int i = 0; i < name.Length; i++)
            {
                var waveColor = GetWaveColor(glowColor, i);
                builder.PushEdgeColorRgba(new Vector4(waveColor, 1f));
                builder.AppendChar(name[i]);
                builder.PopEdgeColor();
            }

            builder.PopColor();

            var coloredPart = SeString.Parse(builder.GetViewAsSpan());
            payloads.AddRange(coloredPart.Payloads);

            // Italics off
            payloads.Add(EmphasisItalicPayload.ItalicsOff);

            return new SeString(payloads);
        }

        /// <summary>Replace name in text, preserving context (level, FC tag, etc.) with italics.</summary>
        private SeString? CreateColoredNameWithContext(string fullText, string nameToReplace, string newName, Vector3 glowColor)
        {
            var nameIndex = fullText.IndexOf(nameToReplace, StringComparison.OrdinalIgnoreCase);
            if (nameIndex < 0)
                return null;

            var beforeName = fullText.Substring(0, nameIndex);
            var afterName = fullText.Substring(nameIndex + nameToReplace.Length);

            var payloads = new List<Payload>();

            // Text before name (not italicized)
            if (!string.IsNullOrEmpty(beforeName))
            {
                payloads.Add(new TextPayload(beforeName));
            }

            // Italics on for the name
            payloads.Add(EmphasisItalicPayload.ItalicsOn);

            // Name with wave glow
            var builder = new LuminaSeStringBuilder();
            builder.PushColorRgba(new Vector4(1f, 1f, 1f, 1f));
            for (int i = 0; i < newName.Length; i++)
            {
                var waveColor = GetWaveColor(glowColor, i);
                builder.PushEdgeColorRgba(new Vector4(waveColor, 1f));
                builder.AppendChar(newName[i]);
                builder.PopEdgeColor();
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

            if (!selfReplacementEnabled && !sharedReplacementEnabled)
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
                    if (selfReplacementEnabled && activeChar != null && !string.IsNullOrEmpty(activeChar.Name))
                    {
                        if (revealActualNames)
                        {
                            // Explicitly set back to original name (nameplate might have cached our modification)
                            handler.NameParts.Text = new SeString(new TextPayload(localPlayer.Name.TextValue));
                        }
                        else
                        {
                            handler.NameParts.Text = CreateColoredName(activeChar.Name, activeChar.NameplateColor);

                            // Hide FC tag
                            if (plugin.Configuration.HideFCTagInNameplate)
                            {
                                handler.RemoveFreeCompanyTag();
                            }
                        }
                    }
                }
                // Other players
                else if (sharedReplacementEnabled)
                {
                    var playerChar = handler.PlayerCharacter;
                    if (playerChar == null)
                        continue;

                    var playerName = playerChar.Name.TextValue;
                    var worldName = playerChar.HomeWorld.ValueNullable?.Name.ToString();

                    if (string.IsNullOrEmpty(playerName) || string.IsNullOrEmpty(worldName))
                        continue;

                    var physicalName = $"{playerName}@{worldName}";

                    // Always queue lookup so cache stays fresh
                    plugin.SharedNameManager?.QueueLookup(physicalName);

                    // Check if this player has a CS+ name
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

                // Try to capture level prefix from original data before any replacement
                // This runs even with feature disabled to ensure we capture the prefix
                if (cachedLevelPrefixBytes == null && addon->MemberCount > 0)
                {
                    var localPlayer = clientState.LocalPlayer;
                    var localName = localPlayer?.Name.TextValue;

                    if (!string.IsNullOrEmpty(localName))
                    {
                        for (int slotIdx = 0; slotIdx < addon->MemberCount && slotIdx < 8; slotIdx++)
                        {
                            var memberDebug = addon->PartyMembers[slotIdx];
                            if (memberDebug.Name != null)
                            {
                                try
                                {
                                    var rawBytes = memberDebug.Name->NodeText.AsSpan().ToArray();
                                    var realNameBytes = System.Text.Encoding.UTF8.GetBytes(localName);
                                    int realNameIdx = FindByteSequence(rawBytes, realNameBytes);
                                    if (realNameIdx > 0)
                                    {
                                        cachedLevelPrefixBytes = rawBytes.Take(realNameIdx).ToArray();
                                        break; // Found it, stop searching
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }

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

            // Build set of all OUR CS+ character names (for identifying our slot)
            var ourCSNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var character in plugin.Configuration.Characters)
            {
                if (!string.IsNullOrEmpty(character.Name))
                    ourCSNames.Add(character.Name);
            }

            // Track if we found our slot
            bool foundOurSlot = false;

            // Scan addon slots
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

                // Check if this slot is OURS: contains our real name OR one of our CS+ character names
                // Use ContainsFullName to avoid substring matches (e.g., "Kit" matching "Kitten")
                bool isOurSlot = false;
                string? foundName = null;

                if (ContainsFullName(plainText, localName))
                {
                    isOurSlot = true;
                    foundName = localName;
                }
                else
                {
                    // Check if any of our CS+ names are in the text (full match only)
                    foreach (var csName in ourCSNames)
                    {
                        if (ContainsFullName(plainText, csName))
                        {
                            isOurSlot = true;
                            foundName = csName;
                            break;
                        }
                    }
                }

                if (isOurSlot && selfReplacementEnabled && activeChar != null && !string.IsNullOrEmpty(activeChar.Name))
                {
                    lastKnownLocalPlayerSlot = i;
                    foundOurSlot = true;

                    if (rawBytes != null && rawBytes.Length > 0)
                    {
                        // ALWAYS try to find the REAL player name first to capture/update the level prefix
                        var realNameBytes = System.Text.Encoding.UTF8.GetBytes(localName);
                        int realNameIndex = FindByteSequence(rawBytes, realNameBytes);

                        if (realNameIndex > 0)
                        {
                            // Found real name - capture the level prefix for future use
                            cachedLevelPrefixBytes = rawBytes.Take(realNameIndex).ToArray();
                        }

                        // Now apply the replacement
                        byte[]? prefixToUse = null;

                        if (realNameIndex > 0)
                        {
                            // Use freshly captured prefix
                            prefixToUse = cachedLevelPrefixBytes;
                        }
                        else if (cachedLevelPrefixBytes != null)
                        {
                            // Use cached prefix from before
                            prefixToUse = cachedLevelPrefixBytes;
                        }

                        if (prefixToUse != null)
                        {
                            // Build new SeString: prefix + colored name
                            var coloredName = CreateColoredName(activeChar.Name, activeChar.NameplateColor);
                            var coloredBytes = coloredName.Encode();

                            // Combine prefix + colored name
                            var finalBytes = new byte[prefixToUse.Length + coloredBytes.Length];
                            Array.Copy(prefixToUse, 0, finalBytes, 0, prefixToUse.Length);
                            Array.Copy(coloredBytes, 0, finalBytes, prefixToUse.Length, coloredBytes.Length);

                            nameNode->SetText(finalBytes);
                            continue; // Process next slot
                        }
                    }

                    // Fallback: simple replacement if no prefix available
                    var fallbackName = CreateColoredName(activeChar.Name, activeChar.NameplateColor);
                    nameNode->SetText(fallbackName.Encode());
                }
                // Check for shared name replacement (other players)
                else if (!isOurSlot && sharedReplacementEnabled && plugin.SharedNameManager != null)
                {
                    // Party list shows just "FirstName LastName" without world
                    // Try to find a matching cached shared name (exclude local player's name)
                    var match = plugin.SharedNameManager.FindCachedNameInText(plainText, localName);
                    if (match.HasValue)
                    {
                        var (sharedEntry, originalName) = match.Value;
                        // Replace with their CS+ name (preserving any level prefix)
                        var replacedSeString = CreateColoredNameWithContext(plainText, originalName, sharedEntry.CSName, sharedEntry.NameplateColor);
                        if (replacedSeString != null)
                        {
                            nameNode->SetText(replacedSeString.Encode());
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
                    var replacedSeString = CreateColoredNameWithContext(plainText, originalName, newName, glowColor);
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
                    var replacedSeString = CreateColoredNameWithContext(plainText, originalName, sharedEntry.CSName, sharedEntry.NameplateColor);
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

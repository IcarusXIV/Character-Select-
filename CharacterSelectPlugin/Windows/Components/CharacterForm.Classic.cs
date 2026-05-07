using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.ImGuiSeStringRenderer;
using SeString = Dalamud.Game.Text.SeStringHandling.SeString;
using SeStringBuilder = Lumina.Text.SeStringBuilder;
using DalamudSeStringBuilder = Dalamud.Game.Text.SeStringHandling.SeStringBuilder;
using CharacterSelectPlugin.Windows.Styles;
using CharacterSelectPlugin.Windows.Utils;
using CharacterSelectPlugin.Effects;

namespace CharacterSelectPlugin.Windows.Components
{
    public partial class CharacterForm
    {
        private void DrawClassicLayout()
{
            if (!plugin.IsAddCharacterWindowOpen && !IsEditWindowOpen)
                return;

            var totalScale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);

            // Check if Conflict Resolution is enabled and determine secret mode
            if (plugin.Configuration.EnableConflictResolution)
            {
                // For editing existing characters, check if they already have secret mode data
                if (IsEditWindowOpen && selectedCharacterIndex >= 0 && selectedCharacterIndex < plugin.Characters.Count)
                {
                    var character = plugin.Characters[selectedCharacterIndex];
                    bool hasSecretModeData = character.SecretModState != null ||
                                           (character.Designs?.Any(d => d.SecretModState != null) == true);

                    if (hasSecretModeData && !isSecretMode)
                    {
                        isSecretMode = true;
                    }
                }

                if (!IsEditWindowOpen && isSecretMode)
                {
                    plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                }
            }

            uiStyles.PushFormStyle();

            try
            {
                float baseLines = 26f;
                if (isAdvancedModeCharacter)
                    baseLines += 6f;

                float maxContentHeight = ImGui.GetTextLineHeightWithSpacing() * baseLines;
                float availableHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() * 2.5f;
                float scrollHeight = Math.Min(maxContentHeight, availableHeight);

                ImGui.BeginChild("CharacterFormScrollable", new Vector2(0, scrollHeight), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);
                DrawClassicCharacterFormContent(totalScale);
                ImGui.EndChild();
            }
            finally
            {
                uiStyles.PopFormStyle();
            }
        }

        private void DrawClassicSecretModeModsField(float labelWidth, float inputWidth, float inputOffset, float scale)
        {
            DrawClassicFormField("Mod Manager", labelWidth, inputWidth, inputOffset, () =>
            {
                var selectedCount = IsEditWindowOpen && plugin.Characters[selectedCharacterIndex].SecretModState != null
                    ? plugin.Characters[selectedCharacterIndex].SecretModState.Count
                    : (plugin.NewSecretModState?.Count ?? 0);

                var buttonText = selectedCount > 0
                    ? $"Configure Mods ({selectedCount} selected)###SecretMods"
                    : "Configure Mods###SecretMods";

                string characterName = IsEditWindowOpen ? editedCharacterName : plugin.NewCharacterName;
                bool hasValidName = !string.IsNullOrWhiteSpace(characterName);

                float refreshButtonWidth = 30f * scale;
                float buttonGap = 4f * scale;
                float configureButtonWidth = inputWidth - refreshButtonWidth - buttonGap;

                if (!hasValidName)
                    ImGui.BeginDisabled();

                if (ImGui.Button(buttonText, new Vector2(configureButtonWidth, 0)))
                {
                    if (hasValidName)
                    {
                        if (plugin.SecretModeModWindow == null)
                        {
                            plugin.SecretModeModWindow = new SecretModeModWindow(plugin);
                            plugin.WindowSystem.AddWindow(plugin.SecretModeModWindow);
                        }

                        Dictionary<string, bool>? currentSelection = null;
                        HashSet<string>? currentPins = null;
                        if (IsEditWindowOpen)
                        {
                            currentSelection = plugin.Characters[selectedCharacterIndex].SecretModState;
                            currentPins = plugin.Characters[selectedCharacterIndex].SecretModPins != null ? new HashSet<string>(plugin.Characters[selectedCharacterIndex].SecretModPins) : null;
                        }
                        else
                        {
                            currentSelection = plugin.NewSecretModState;
                            currentPins = plugin.NewSecretModPins != null ? new HashSet<string>(plugin.NewSecretModPins) : null;
                        }

                        plugin.SecretModeModWindow.Open(
                            IsEditWindowOpen ? selectedCharacterIndex : null,
                            currentSelection,
                            currentPins,
                            (selection) =>
                            {
                                if (IsEditWindowOpen)
                                {
                                    plugin.Characters[selectedCharacterIndex].SecretModState = selection;
                                    plugin.SaveConfiguration();
                                }
                                else
                                {
                                    plugin.NewSecretModState = selection;
                                }
                            },
                            (pins) =>
                            {
                                if (IsEditWindowOpen)
                                {
                                    plugin.Characters[selectedCharacterIndex].SecretModPins = pins?.ToList();
                                    plugin.SaveConfiguration();
                                }
                                else
                                {
                                    plugin.NewSecretModPins = pins?.ToList();
                                }
                            },
                            null,
                            characterName,
                            (inheritMods) =>
                            {
                                if (inheritMods != null && inheritMods.Count > 0)
                                {
                                    _ = plugin.RestoreModInheritance(inheritMods);
                                }
                            }
                        );
                    }
                }

                if (!hasValidName)
                {
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        ImGui.BeginTooltip();
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.7f, 0.7f, 1.0f));
                        ImGui.Text("Please enter a Character Name before configuring mods.");
                        ImGui.PopStyleColor();
                        ImGui.EndTooltip();
                    }
                }

                // Quick-update refresh button on the same row
                ImGui.SameLine(0, buttonGap);
                ImGui.PushFont(UiBuilder.IconFont);

                bool canQuickUpdate = hasValidName && plugin.Configuration.EnableConflictResolution;

                if (!canQuickUpdate)
                    ImGui.BeginDisabled();

                if (ImGui.Button("##CharacterQuickUpdate", new Vector2(refreshButtonWidth, 0)))
                {
                    if (canQuickUpdate)
                    {
                        PerformQuickCharacterGearHairUpdate();
                    }
                }

                if (!canQuickUpdate)
                    ImGui.EndDisabled();

                ImGui.PopFont();

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.BeginTooltip();
                    if (canQuickUpdate)
                    {
                        ImGui.Text("Update gear/hair changes");
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f),
                            "Pulls currently-affecting gear/hair mods into this character's mod state.");
                    }
                    else
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.7f, 0.7f, 1.0f));
                        if (!hasValidName)
                            ImGui.Text("Enter a Character Name first");
                        else if (!plugin.Configuration.EnableConflictResolution)
                            ImGui.Text("Conflict Resolution must be enabled");
                        ImGui.PopStyleColor();
                    }
                    ImGui.EndTooltip();
                }
            }, "Select which mods to enable and configure their options for this character.\nAllows different characters to use different mod combinations and settings.", scale);
        }

        private void ClassicCloseForm()
{
            IsEditWindowOpen = false;
            plugin.CloseAddCharacterWindow();

            // Close Mod Manager window if it's open
            if (plugin.SecretModeModWindow?.IsOpen ?? false)
            {
                plugin.SecretModeModWindow.IsOpen = false;
            }

            isSecretMode = false;
            isAdvancedModeCharacter = false;
            ResetFields();
        }

        private void DrawClassicCharacterFormContent(float scale)
{
            float labelWidth = 130 * scale;
            float inputWidth = 250 * scale;
            float inputOffset = 10 * scale;

            string tempName = IsEditWindowOpen ? editedCharacterName : plugin.NewCharacterName;
            string tempMacros = IsEditWindowOpen ? editedCharacterMacros : plugin.NewCharacterMacros;
            string? imagePath = IsEditWindowOpen ? editedCharacterImagePath : plugin.NewCharacterImagePath;
            string tempPenumbra = IsEditWindowOpen ? editedCharacterPenumbra : plugin.NewPenumbraCollection;
            string tempGlamourer = IsEditWindowOpen ? editedCharacterGlamourer : plugin.NewGlamourerDesign;
            string tempCustomize = IsEditWindowOpen ? editedCharacterCustomize : plugin.NewCustomizeProfile;
            Vector3 tempColor = IsEditWindowOpen ? editedCharacterColor : plugin.NewCharacterColor;
            string tempTag = IsEditWindowOpen ? editedCharacterTag : plugin.NewCharacterTag;

            // Character Name
            DrawClassicFormField("Character Name*", labelWidth, inputWidth, inputOffset, () =>
            {
                // Show red border if there's a validation error
                if (!string.IsNullOrEmpty(nameValidationError))
                {
                    ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 2.0f);
                }

                ImGui.InputText("##CharacterName", ref tempName, 50);
                plugin.CharacterNameFieldPos = ImGui.GetItemRectMin();
                plugin.CharacterNameFieldSize = ImGui.GetItemRectSize();

                if (!string.IsNullOrEmpty(nameValidationError))
                {
                    ImGui.PopStyleColor();
                    ImGui.PopStyleVar();

                    // Show error message
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.3f, 0.3f, 1.0f));
                    ImGui.TextWrapped(nameValidationError);
                    ImGui.PopStyleColor();
                }

                if (IsEditWindowOpen) editedCharacterName = tempName;
                else plugin.NewCharacterName = tempName;

                // Validate name on change
                ValidateCharacterName(tempName);
            }, "Enter your OC's name or nickname for profile here.", scale,
            // Name Sync exclusion checkbox - after tooltip, only show if Name Sync sharing is enabled
            plugin.Configuration.AllowOthersToSeeMyCSName ? () =>
            {
                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 4 * scale); // Small gap
                bool tempExclude = IsEditWindowOpen ? editedCharacterExcludeFromNameSync : plugin.NewCharacterExcludeFromNameSync;
                if (ImGui.Checkbox("Exclude from Name Sync", ref tempExclude))
                {
                    if (IsEditWindowOpen) editedCharacterExcludeFromNameSync = tempExclude;
                    else plugin.NewCharacterExcludeFromNameSync = tempExclude;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("When checked, Name Sync won't apply to this character.");
                }
            } : null);

            // Character Alias field - only show when Name Sync is enabled
            if (plugin.Configuration.EnableNameReplacement || plugin.Configuration.EnableSharedNameReplacement)
            {
                string tempAlias = IsEditWindowOpen ? editedCharacterAlias : plugin.NewCharacterAlias;
                DrawClassicFormField("Character Alias", labelWidth, inputWidth, inputOffset, () =>
                {
                    ImGui.InputTextWithHint("##CharacterAlias", "Leave empty to use Character Name", ref tempAlias, 100);

                    if (IsEditWindowOpen) editedCharacterAlias = tempAlias;
                    else plugin.NewCharacterAlias = tempAlias;
                }, "Optional alias used for Name Sync.\nIf set, this name is displayed instead of Character Name.\nLeave empty to use the Character Name above.", scale);
            }

            ImGui.Separator();

            // Character Tags
            DrawClassicFormField("Character Tags", labelWidth, inputWidth, inputOffset, () =>
            {
                ImGui.InputTextWithHint("##Tags", "e.g. Casual, Battle, Beach", ref tempTag, 100);

                if (IsEditWindowOpen) editedCharacterTag = tempTag;
                else plugin.NewCharacterTag = tempTag;
            }, "You can assign multiple tags by separating them with commas.\nExamples: Casual, Favourites, Seasonal", scale);

            ImGui.Separator();

            // Nameplate Colour
            DrawClassicFormField("Nameplate Color", labelWidth, inputWidth, inputOffset, () =>
            {
                ImGui.ColorEdit3("##NameplateColor", ref tempColor);

                if (IsEditWindowOpen) editedCharacterColor = tempColor;
                else plugin.NewCharacterColor = tempColor;
            }, "Affects your character's nameplate under their profile picture in Character Select+.", scale);

            ImGui.Separator();

            // Penumbra Collection
            DrawClassicFormField("Penumbra Collection*", labelWidth, inputWidth, inputOffset, () =>
            {
                var penumbraOptions = plugin.IntegrationListProvider?.GetPenumbraCollections() ?? Array.Empty<string>();
                var currentPenumbra = plugin.IntegrationListProvider?.GetCurrentPenumbraCollection();
                string oldValue = tempPenumbra;

                if (AutocompleteCombo.Draw("##PenumbraCollection", ref tempPenumbra, penumbraOptions, inputWidth, "Select collection...", currentActive: currentPenumbra))
                {
                    plugin.PenumbraFieldPos = ImGui.GetItemRectMin();
                    plugin.PenumbraFieldSize = ImGui.GetItemRectSize();

                    if (IsEditWindowOpen)
                    {
                        editedCharacterPenumbra = tempPenumbra;
                        if (isAdvancedModeCharacter)
                        {
                            UpdateAdvancedMacroPenumbra(tempPenumbra);
                        }
                        else
                        {
                            editedCharacterMacros = GenerateMacro();
                        }
                    }
                    else
                    {
                        plugin.NewPenumbraCollection = tempPenumbra;
                        if (isAdvancedModeCharacter)
                        {
                            UpdateAdvancedMacroPenumbra(tempPenumbra);
                            plugin.NewCharacterMacros = advancedCharacterMacroText;
                        }
                        else
                        {
                            plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                        }
                    }
                }
                else
                {
                    // Still track position even when not changed
                    plugin.PenumbraFieldPos = ImGui.GetItemRectMin();
                    plugin.PenumbraFieldSize = ImGui.GetItemRectSize();
                }
            }, "Select the Penumbra collection for this character. Right-click to clear.", scale);

            ImGui.Separator();

            // Glamourer Design
            DrawClassicFormField("Glamourer Design*", labelWidth, inputWidth, inputOffset, () =>
            {
                var glamourerOptions = plugin.IntegrationListProvider?.GetGlamourerDesigns() ?? Array.Empty<string>();
                string oldValue = tempGlamourer;

                if (AutocompleteCombo.Draw("##GlamourerDesign", ref tempGlamourer, glamourerOptions, inputWidth, "Select design..."))
                {
                    plugin.GlamourerFieldPos = ImGui.GetItemRectMin();
                    plugin.GlamourerFieldSize = ImGui.GetItemRectSize();

                    if (IsEditWindowOpen)
                    {
                        editedCharacterGlamourer = tempGlamourer;
                        if (isAdvancedModeCharacter)
                        {
                            UpdateAdvancedMacroGlamourer(oldValue, tempGlamourer);
                        }
                        else
                        {
                            editedCharacterMacros = GenerateMacro();
                        }
                    }
                    else
                    {
                        plugin.NewGlamourerDesign = tempGlamourer;
                        if (isAdvancedModeCharacter)
                        {
                            UpdateAdvancedMacroGlamourer(oldValue, tempGlamourer);
                            plugin.NewCharacterMacros = advancedCharacterMacroText;
                        }
                        else
                        {
                            plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                        }
                    }
                }
                else
                {
                    // Still track position even when not changed
                    plugin.GlamourerFieldPos = ImGui.GetItemRectMin();
                    plugin.GlamourerFieldSize = ImGui.GetItemRectSize();
                }
            }, "Select the Glamourer design for this character. Right-click to clear.\nYou can add additional designs later.", scale);

            ImGui.Separator();

            // Automation (if enabled)
            if (plugin.Configuration.EnableAutomations)
            {
                DrawClassicAutomationField(labelWidth, inputWidth, inputOffset, scale);
                ImGui.Separator();
            }

            // Customize+ Profile
            DrawClassicCustomizeField(labelWidth, inputWidth, inputOffset, scale);
            ImGui.Separator();

            // Honorific Section
            DrawClassicHonorificSection(labelWidth, inputWidth, inputOffset, scale);
            ImGui.Separator();

            // Moodle Preset
            DrawClassicMoodleField(labelWidth, inputWidth, inputOffset, scale);
            ImGui.Separator();

            // Idle Pose
            DrawClassicIdlePoseField(labelWidth, inputWidth, inputOffset, scale);
            ImGui.Separator();

            // Assigned Gearset (only if enabled)
            if (plugin.Configuration.EnableGearsetAssignments)
            {
                DrawClassicGearsetField(labelWidth, inputWidth, inputOffset, scale);
                ImGui.Separator();
            }

            // Mod Manager (Conflict Resolution)
            if (plugin.Configuration.EnableConflictResolution)
            {
                if (ImGui.Checkbox("Use Conflict Resolution", ref isSecretMode))
                {
                    if (!IsEditWindowOpen && !isAdvancedModeCharacter)
                    {
                        plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                    }
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Manual mod state for this character.");
                }

                if (isSecretMode)
                {
                    DrawClassicSecretModeModsField(labelWidth, inputWidth, inputOffset, scale);
                }
                ImGui.Separator();
            }

            // Image Selection
            DrawClassicImageSelection(scale);
            ImGui.Separator();

            // Advanced Mode Toggle
            DrawClassicAdvancedModeSection(scale);
            ImGui.Separator();

            // Buttons!
            DrawClassicActionButtons(scale);
        }

        private void DrawClassicFormField(string label, float labelWidth, float inputWidth, float inputOffset,
                                 System.Action drawInput, string tooltip, float scale, System.Action? afterTooltip = null)
{
            ImGui.SetCursorPosX(10 * scale);
            ImGui.Text(label);
            ImGui.SameLine(labelWidth);
            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);

            drawInput();

            // Tooltip
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300 * scale);
                ImGui.TextUnformatted(tooltip);
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            // Optional content after tooltip
            afterTooltip?.Invoke();
        }

        private void DrawClassicAdvancedModeSection(float scale)
{
            if (ImGui.Button(isAdvancedModeCharacter ? "Exit Advanced Mode" : "Advanced Mode", new Vector2(0, 25 * scale)))
            {
                isAdvancedModeCharacter = !isAdvancedModeCharacter;

                // Update the character's advanced mode flag
                if (IsEditWindowOpen && selectedCharacterIndex >= 0 && selectedCharacterIndex < plugin.Characters.Count)
                {
                    plugin.Characters[selectedCharacterIndex].IsAdvancedMode = isAdvancedModeCharacter;
                    plugin.SaveConfiguration();
                }

                if (isAdvancedModeCharacter)
                {
                    // When entering advanced mode, use existing macro if available, otherwise generate
                    if (IsEditWindowOpen)
                    {
                        advancedCharacterMacroText = !string.IsNullOrWhiteSpace(editedCharacterMacros)
                            ? editedCharacterMacros
                            : GenerateMacro();
                    }
                    else
                    {
                        advancedCharacterMacroText = !string.IsNullOrWhiteSpace(plugin.NewCharacterMacros)
                            ? plugin.NewCharacterMacros
                            : ((isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro());
                        plugin.NewCharacterMacros = advancedCharacterMacroText;
                    }
                }
                else
                {
                    // When exiting advanced mode, preserve the current macro state
                    if (IsEditWindowOpen)
                    {
                        editedCharacterMacros = advancedCharacterMacroText;
                    }
                    else
                    {
                        plugin.NewCharacterMacros = advancedCharacterMacroText;
                    }
                }
            }

            // Tooltip
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (5 * scale));
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300 * scale);
                ImGui.TextUnformatted("⚠️ Do not touch this unless you know what you're doing.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            // Advanced mode editor
            if (isAdvancedModeCharacter)
            {
                ImGui.Text("Edit Macro Manually:");

                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.1f, 0.1f, 0.1f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));

                ImGui.InputTextMultiline("##AdvancedCharacterMacro", ref advancedCharacterMacroText, 2000,
                    new Vector2(500 * scale, 150 * scale), ImGuiInputTextFlags.AllowTabInput);

                ImGui.PopStyleColor(2);

                // Real-time sync when user types in advanced mode
                if (!IsEditWindowOpen)
                {
                    plugin.NewCharacterMacros = advancedCharacterMacroText;
                }
                else
                {
                    editedCharacterMacros = advancedCharacterMacroText;
                }
            }
        }

        private void DrawClassicHonorificSection(float labelWidth, float inputWidth, float inputOffset, float scale)
{
            ImGui.SetCursorPosX(10 * scale);
            ImGui.Text("Honorific Title");
            ImGui.SameLine();
            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);

            bool changed = false;

            // Title input
            changed |= ImGui.InputText("##HonorificTitle", ref tempHonorificTitle, 50);

            ImGui.SameLine();
            ImGui.SetNextItemWidth(80 * scale);
            if (ImGui.BeginCombo("##HonorificPlacement", tempHonorificPrefix))
            {
                foreach (var opt in new[] { "Prefix", "Suffix" })
                {
                    if (ImGui.Selectable(opt, tempHonorificPrefix == opt))
                    {
                        tempHonorificPrefix = opt;
                        tempHonorificSuffix = opt;
                        changed = true;
                    }
                }
                ImGui.EndCombo();
            }

            // Text colour picker
            ImGui.SameLine();
            ImGui.SetNextItemWidth(40 * scale);
            changed |= ImGui.ColorEdit3("##HonorificColor", ref tempHonorificColor, ImGuiColorEditFlags.NoInputs);

            // Glow picker with gradient options (Honorific-style)
            ImGui.SameLine();
            changed |= DrawClassicGlowPicker(scale);

            // Tooltip
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300 * scale);
                ImGui.TextUnformatted("This will set a forced title when you switch to this character.\nThe dropdown selects if the title appears above (prefix) or below (suffix) your name in-game.\nClick the glow color box to access gradient presets.\nUse the Honorific plug-in's 'Clear' button if you need to remove it.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            // Live preview to the right of tooltip
            if (!string.IsNullOrWhiteSpace(tempHonorificTitle))
            {
                ImGui.SameLine(0, 4 * scale);
                DrawClassicHonorificPreview(scale);
            }

            if (changed)
            {
                UpdateHonorificData();

                // Always update advanced macro when in advanced mode
                if (isAdvancedModeCharacter)
                {
                    UpdateAdvancedMacroHonorific();
                    if (!IsEditWindowOpen)
                    {
                        plugin.NewCharacterMacros = advancedCharacterMacroText;
                    }
                }
                else
                {
                    if (IsEditWindowOpen)
                    {
                        editedCharacterMacros = GenerateMacro();
                    }
                    else
                    {
                        plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                    }
                }
            }
        }

        private void DrawClassicImageSelection(float scale)
{
            if (ImGui.Button("Choose Image", new Vector2(0, 25 * scale)))
            {
                plugin.OpenFilePicker(
                    "Select Character Image",
                    "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|PNG files (*.png)|*.png",
                    (selectedPath) =>
                    {
                        lock (this)
                        {
                            pendingImagePath = selectedPath;
                        }
                    }
                );
            }

            // Apply pending image
            if (pendingImagePath != null)
            {
                lock (this)
                {
                    if (IsEditWindowOpen)
                        editedCharacterImagePath = pendingImagePath;
                    else
                        plugin.NewCharacterImagePath = pendingImagePath;

                    pendingImagePath = null;
                }
            }

            // Show image preview
            DrawClassicImagePreview(scale);

            DrawClassicAnimatedImageSelection(scale);
        }

        private void DrawClassicAnimatedImageSelection(float scale)
        {
            string currentGif = IsEditWindowOpen
                ? (editedAnimatedImagePath ?? "")
                : (plugin.NewCharacterAnimatedImagePath ?? "");
            bool hasGif = !string.IsNullOrWhiteSpace(currentGif);

            ImGui.Spacing();
            if (ImGui.Button(hasGif ? "Change Animated Image" : "Choose Animated Image (GIF)", new Vector2(0, 25 * scale)))
            {
                plugin.OpenFilePicker(
                    "Select Animated Image",
                    "Animated images (*.gif;*.webp)|*.gif;*.webp|GIF files (*.gif)|*.gif|WebP files (*.webp)|*.webp",
                    (selectedPath) => { lock (this) { pendingAnimatedImagePath = selectedPath; } }
                );
            }

            if (pendingAnimatedImagePath != null)
            {
                lock (this)
                {
                    if (IsEditWindowOpen) editedAnimatedImagePath = pendingAnimatedImagePath;
                    else plugin.NewCharacterAnimatedImagePath = pendingAnimatedImagePath;
                    pendingAnimatedImagePath = null;
                }
            }

            currentGif = IsEditWindowOpen
                ? (editedAnimatedImagePath ?? "")
                : (plugin.NewCharacterAnimatedImagePath ?? "");
            hasGif = !string.IsNullOrWhiteSpace(currentGif);

            if (hasGif)
            {
                ImGui.SameLine();
                if (ImGui.Button("Clear##gif", new Vector2(60 * scale, 25 * scale)))
                {
                    if (IsEditWindowOpen) editedAnimatedImagePath = null;
                    else plugin.NewCharacterAnimatedImagePath = null;
                    return;
                }

                DrawClassicAnimatedImagePreview(currentGif, scale);
            }
        }

        private void DrawClassicAnimatedImagePreview(string gifPath, float scale)
        {
            float side = 100f * scale;
            var origin = ImGui.GetCursorScreenPos();
            DrawAnimatedPreviewBox(origin, side, scale);
            ImGui.Dummy(new Vector2(side, side));

            ImGui.SameLine();
            ImGui.BeginGroup();
            DrawFramingSliders(scale, 200f * scale,
                () => editedAnimatedOffsetX, v => editedAnimatedOffsetX = v,
                () => editedAnimatedOffsetY, v => editedAnimatedOffsetY = v,
                () => editedAnimatedZoom,    v => editedAnimatedZoom    = v,
                "classicGif", showResetButtons: true);
            ImGui.EndGroup();
        }

        private void DrawClassicActionButtons(float scale)
{
            string tempName = IsEditWindowOpen ? editedCharacterName : plugin.NewCharacterName;
            string tempPenumbra = IsEditWindowOpen ? editedCharacterPenumbra : plugin.NewPenumbraCollection;
            string tempGlamourer = IsEditWindowOpen ? editedCharacterGlamourer : plugin.NewGlamourerDesign;

            bool canSaveCharacter = !string.IsNullOrWhiteSpace(tempName) &&
                                   !string.IsNullOrWhiteSpace(tempPenumbra) &&
                                   !string.IsNullOrWhiteSpace(tempGlamourer) &&
                                   string.IsNullOrEmpty(nameValidationError);

            uiStyles.PushDarkButtonStyle(scale);

            if (!canSaveCharacter)
                ImGui.BeginDisabled();

            if (ImGui.Button(IsEditWindowOpen ? "Save Changes" : "Save Character", new Vector2(0, 30 * scale)))
            {
                if (IsEditWindowOpen)
                {
                    SaveEditedCharacter();
                }
                else
                {
                    string finalMacro;
                    if (isAdvancedModeCharacter)
                    {
                        finalMacro = advancedCharacterMacroText;
                    }
                    else
                    {
                        finalMacro = plugin.NewCharacterMacros;
                    }

                    var created = plugin.SaveNewCharacter(finalMacro);
                    if (created != null)
                    {
                        created.PortraitOffsetX = editedPortraitOffsetX;
                        created.PortraitOffsetY = editedPortraitOffsetY;
                        created.PortraitZoom = editedPortraitZoom;
                        if (!string.IsNullOrWhiteSpace(created.AnimatedImagePath))
                        {
                            created.AnimatedOffsetX = editedAnimatedOffsetX;
                            created.AnimatedOffsetY = editedAnimatedOffsetY;
                            created.AnimatedZoom = editedAnimatedZoom;
                        }
                        plugin.SaveConfiguration();
                    }
                }

                ClassicCloseForm();
            }

            plugin.SaveButtonPos = ImGui.GetItemRectMin();
            plugin.SaveButtonSize = ImGui.GetItemRectSize();

            if (!canSaveCharacter)
                ImGui.EndDisabled();

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(0, 30 * scale)))
            {
                ClassicCloseForm();
            }

            uiStyles.PopDarkButtonStyle();
        }

        private void DrawClassicAutomationField(float labelWidth, float inputWidth, float inputOffset, float scale)
{
            string tempCharacterAutomation = IsEditWindowOpen ? editedCharacterAutomation : plugin.NewCharacterAutomation;

            DrawClassicFormField("Glam. Automation", labelWidth, inputWidth, inputOffset, () =>
            {
                // Glamourer doesn't expose an IPC to get automation names, so use plain text input
                ImGui.SetNextItemWidth(inputWidth);
                if (ImGui.InputText("##Glam.Automation", ref tempCharacterAutomation, 100))
                {
                    if (IsEditWindowOpen)
                    {
                        editedCharacterAutomation = tempCharacterAutomation;
                        if (isAdvancedModeCharacter)
                        {
                            UpdateAdvancedMacroAutomation(tempCharacterAutomation);
                        }
                        else
                        {
                            editedCharacterMacros = GenerateMacro();
                        }
                    }
                    else
                    {
                        plugin.NewCharacterAutomation = tempCharacterAutomation;
                        if (isAdvancedModeCharacter)
                        {
                            UpdateAdvancedMacroAutomation(tempCharacterAutomation);
                            plugin.NewCharacterMacros = advancedCharacterMacroText;
                        }
                        else
                        {
                            plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                        }
                    }
                }
            }, "Enter the name of a Glamourer Automation for this character.\nMust match the automation name EXACTLY as shown in Glamourer.\nDesign-level automations override this if both are set.", scale);
        }

        private void DrawClassicCustomizeField(float labelWidth, float inputWidth, float inputOffset, float scale)
{
            string tempCustomize = IsEditWindowOpen ? editedCharacterCustomize : plugin.NewCustomizeProfile;

            DrawClassicFormField("Customize+ Profile", labelWidth, inputWidth, inputOffset, () =>
            {
                var customizeOptions = plugin.IntegrationListProvider?.GetCustomizePlusProfiles() ?? Array.Empty<string>();
                var currentCustomize = plugin.IntegrationListProvider?.GetCurrentCustomizePlusProfile();

                if (AutocompleteCombo.Draw("##CustomizeProfile", ref tempCustomize, customizeOptions, inputWidth, "Select profile...", currentActive: currentCustomize))
                {
                    if (IsEditWindowOpen)
                    {
                        editedCharacterCustomize = tempCustomize;
                        if (isAdvancedModeCharacter)
                        {
                            UpdateAdvancedMacroCustomize(tempCustomize);
                        }
                        else
                        {
                            editedCharacterMacros = GenerateMacro();
                        }
                    }
                    else
                    {
                        plugin.NewCustomizeProfile = tempCustomize;
                        if (isAdvancedModeCharacter)
                        {
                            UpdateAdvancedMacroCustomize(tempCustomize);
                            plugin.NewCharacterMacros = advancedCharacterMacroText;
                        }
                        else
                        {
                            plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                        }
                    }
                }
            }, "Select the Customize+ profile for this character. Right-click to clear.", scale);
        }

        private void DrawClassicGearsetField(float labelWidth, float inputWidth, float inputOffset, float scale)
{
            ImGui.SetCursorPosX(10 * scale);
            ImGui.Text("Assigned Gearset");
            ImGui.SameLine();
            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);

            // Get available gearsets
            var gearsets = plugin.GetPlayerGearsets();

            // Get current value
            int? currentGearset = IsEditWindowOpen ? editedCharacterGearset : plugin.NewCharacterGearset;

            // Build display text for current selection
            string currentDisplay = "None";
            if (currentGearset.HasValue)
            {
                var matchingGearset = gearsets.FirstOrDefault(g => g.Number == currentGearset.Value);
                if (matchingGearset.Number > 0)
                {
                    currentDisplay = plugin.GetGearsetDisplayName(matchingGearset.Number, matchingGearset.JobId, matchingGearset.Name);
                }
                else
                {
                    currentDisplay = $"Gearset {currentGearset.Value}";
                }
            }

            if (ImGui.BeginCombo("##AssignedGearset", currentDisplay))
            {
                // "None" option
                if (ImGui.Selectable("None", !currentGearset.HasValue))
                {
                    if (IsEditWindowOpen)
                        editedCharacterGearset = null;
                    else
                        plugin.NewCharacterGearset = null;
                }
                if (!currentGearset.HasValue)
                    ImGui.SetItemDefaultFocus();

                // Gearset options
                foreach (var gearset in gearsets)
                {
                    string displayName = plugin.GetGearsetDisplayName(gearset.Number, gearset.JobId, gearset.Name);
                    bool isSelected = currentGearset.HasValue && currentGearset.Value == gearset.Number;

                    if (ImGui.Selectable(displayName, isSelected))
                    {
                        if (IsEditWindowOpen)
                            editedCharacterGearset = gearset.Number;
                        else
                            plugin.NewCharacterGearset = gearset.Number;
                    }
                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            // Tooltip
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300 * scale);
                ImGui.TextUnformatted("Automatically switch to this gearset when applying this character.\nChoose 'None' to not change gearsets.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
        }

        /// <summary>Glow colour picker with gradient options (Honorific-style).</summary>
        private bool DrawClassicGlowPicker(float scale)
{
            bool modified = false;
            long animOffset = AnimationTimer.ElapsedMilliseconds;

            Vector3 displayColor;
            if (tempHonorificGradientSet.HasValue)
            {
                if (tempHonorificGradientSet.Value == -1)
                {
                    // Two-colour gradient: alternate between the two colours
                    displayColor = GetTwoColourPreviewColor(tempHonorificGlow, tempHonorificColor3, animOffset);
                }
                else
                {
                    displayColor = GetGradientPreviewColor(tempHonorificGradientSet.Value, animOffset);
                }
            }
            else
            {
                displayColor = tempHonorificGlow;
            }

            if (ImGui.ColorButton("##GlowPickerBtn", new Vector4(displayColor, 1f), ImGuiColorEditFlags.NoTooltip))
            {
                ImGui.OpenPopup("##GlowPickerPopup");
            }

            // Tooltip
            if (ImGui.IsItemHovered())
            {
                if (tempHonorificGradientSet.HasValue)
                {
                    if (tempHonorificGradientSet.Value == -1)
                        ImGui.SetTooltip($"Two Colour Gradient ({tempHonorificAnimationStyle ?? "Wave"})");
                    else
                        ImGui.SetTooltip($"{GradientPresetNames[tempHonorificGradientSet.Value]} ({tempHonorificAnimationStyle ?? "Wave"})");
                }
                else
                    ImGui.SetTooltip("Glow (click for gradients)");
            }

            // The popup with gradient options
            if (ImGui.BeginPopup("##GlowPickerPopup"))
            {
                float popupWidth = 220 * scale;

                ImGui.Text("Solid Glow:");
                ImGui.SameLine();
                if (ImGui.ColorEdit3("##GlowColorPicker", ref tempHonorificGlow, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                {
                    tempHonorificGradientSet = null;
                    tempHonorificAnimationStyle = null;
                    modified = true;
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("Use##UseGlow"))
                {
                    tempHonorificGradientSet = null;
                    tempHonorificAnimationStyle = null;
                    modified = true;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.Separator();

                // Gate animated gradients behind supporter acknowledgment
                if (plugin.Configuration.HasAcknowledgedHonorificSupport)
                {
                    // Nested combo for gradient selection (like Honorific)
                    string gradientLabel = tempHonorificGradientSet.HasValue
                        ? (tempHonorificGradientSet.Value == -1 ? "Two Colour Gradient" : GradientPresetNames[tempHonorificGradientSet.Value])
                        : "Select Gradient...";

                    ImGui.SetNextItemWidth(popupWidth);
                    if (ImGui.BeginCombo("##GradientSelect", gradientLabel, ImGuiComboFlags.HeightLargest))
                    {
                        // Tab bar for animation styles
                        if (ImGui.BeginTabBar("##GradAnimTabs"))
                        {
                            foreach (var animStyle in new[] { "Wave", "Pulse", "Static" })
                            {
                                if (ImGui.BeginTabItem(animStyle))
                                {
                                    // Child region for scrolling
                                    float childHeight = Math.Min(180 * scale, (GradientPresetNames.Length + 1) * ImGui.GetTextLineHeightWithSpacing());
                                    if (ImGui.BeginChild($"##Presets{animStyle}", new Vector2(popupWidth - 16 * scale, childHeight)))
                                    {
                                        var drawList = ImGui.GetWindowDrawList();

                                        // Two Colour Gradient option at top
                                        bool isTwoColourSelected = tempHonorificGradientSet == -1 && tempHonorificAnimationStyle == animStyle;
                                        if (ImGui.Selectable("Two Colour Gradient", isTwoColourSelected, ImGuiSelectableFlags.DontClosePopups))
                                        {
                                            tempHonorificGradientSet = -1;
                                            tempHonorificAnimationStyle = animStyle;
                                            modified = true;
                                            ImGui.CloseCurrentPopup();  // Close inner combo only
                                        }

                                        // Preset gradients
                                        for (int i = 0; i < GradientPresetNames.Length; i++)
                                        {
                                            bool isSelected = tempHonorificGradientSet == i && tempHonorificAnimationStyle == animStyle;

                                            var selectableSize = new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeightWithSpacing());
                                            var cursorPos = ImGui.GetCursorScreenPos();

                                            if (ImGui.Selectable($"##Preset{animStyle}{i}", isSelected, ImGuiSelectableFlags.DontClosePopups, selectableSize))
                                            {
                                                tempHonorificGradientSet = i;
                                                tempHonorificAnimationStyle = animStyle;
                                                modified = true;
                                                ImGui.CloseCurrentPopup();
                                            }

                                            // Draw the preset name with animated gradient effect
                                            var textPos = cursorPos + ImGui.GetStyle().FramePadding;
                                            DrawGradientTextForPicker(drawList, textPos, GradientPresetNames[i], i, animStyle);
                                        }
                                    }
                                    ImGui.EndChild();
                                    ImGui.EndTabItem();
                                }
                            }
                            ImGui.EndTabBar();
                        }
                        ImGui.EndCombo();
                    }

                    // Show animated preview of selected gradient (below the combo, still in popup)
                    if (tempHonorificGradientSet.HasValue)
                    {
                        var previewText = tempHonorificGradientSet.Value == -1
                            ? "Two Colour Gradient"
                            : GradientPresetNames[tempHonorificGradientSet.Value];

                        var previewPos = ImGui.GetCursorScreenPos();
                        var drawList = ImGui.GetWindowDrawList();

                        // Reserve space and draw preview
                        ImGui.Dummy(new Vector2(popupWidth, ImGui.GetTextLineHeightWithSpacing()));
                        DrawGradientTextForPicker(drawList, previewPos, previewText,
                            tempHonorificGradientSet.Value, tempHonorificAnimationStyle ?? "Wave");
                    }

                    // Two colour pickers (shown below combo when two-colour is selected)
                    if (tempHonorificGradientSet == -1)
                    {
                        if (ImGui.ColorEdit3("##TwoColour1", ref tempHonorificGlow, ImGuiColorEditFlags.NoInputs))
                        {
                            modified = true;
                        }
                        ImGui.SameLine();
                        if (ImGui.ColorEdit3("Colours##TwoColour2", ref tempHonorificColor3, ImGuiColorEditFlags.NoInputs))
                        {
                            modified = true;
                        }
                    }
                }
                else
                {
                    // Show message when supporter acknowledgment not enabled
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.65f, 1.0f));
                    ImGui.TextWrapped("Enable in Settings > Visual Settings to use animated gradients.");
                    ImGui.PopStyleColor();
                }

                ImGui.EndPopup();
            }

            return modified;
        }

        /// <summary>Animated preview of the Honorific title with current settings in a dark container.</summary>
        private void DrawClassicHonorificPreview(float scale)
{
            if (string.IsNullOrWhiteSpace(tempHonorificTitle))
                return;

            var textSize = ImGui.CalcTextSize(tempHonorificTitle);
            var padding = new Vector2(8 * scale, 4 * scale);
            var boxSize = textSize + padding * 2;

            // Draw dark background box
            var drawList = ImGui.GetWindowDrawList();
            var boxStart = ImGui.GetCursorScreenPos();
            var boxEnd = boxStart + boxSize;

            // Dark background with slight border
            drawList.AddRectFilled(boxStart, boxEnd, ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 1f)), 4f);
            drawList.AddRect(boxStart, boxEnd, ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.3f, 1f)), 4f);

            // Text position inside the box
            var textPos = boxStart + padding;

            SeString seString;
            if (tempHonorificGradientSet.HasValue)
            {
                // For gradients, build per-character SeString with animated colors
                // For two-colour gradient (-1), pass both colours
                seString = BuildGradientSeString(tempHonorificTitle, tempHonorificGradientSet.Value,
                    tempHonorificAnimationStyle ?? "Wave", tempHonorificColor,
                    tempHonorificGradientSet.Value == -1 ? tempHonorificGlow : null,
                    tempHonorificGradientSet.Value == -1 ? tempHonorificColor3 : null);
            }
            else
            {
                seString = BuildColoredSeString(tempHonorificTitle, tempHonorificColor, tempHonorificGlow);
            }

            // Render using Dalamud's SeString renderer for smooth text
            ImGuiHelpers.SeStringWrapped(seString.Encode(), new SeStringDrawParams
            {
                Color = 0xFFFFFFFF,
                WrapWidth = float.MaxValue,
                TargetDrawList = drawList,
                Font = UiBuilder.DefaultFont,
                FontSize = UiBuilder.DefaultFontSizePx,
                ScreenOffset = textPos
            });

            // Reserve space for the box
            ImGui.Dummy(boxSize);
        }

        private void DrawClassicIdlePoseField(float labelWidth, float inputWidth, float inputOffset, float scale)
{
            ImGui.SetCursorPosX(10 * scale);
            ImGui.Text("Idle Pose");
            ImGui.SameLine();
            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);

            string[] poseOptions = { "None", "0", "1", "2", "3", "4", "5", "6" };
            byte storedIndex = IsEditWindowOpen
                ? plugin.Characters[selectedCharacterIndex].IdlePoseIndex
                : plugin.NewCharacterIdlePoseIndex;

            int dropdownIndex = storedIndex == 7 ? 0 : storedIndex + 1;

            if (ImGui.BeginCombo("##IdlePose", poseOptions[dropdownIndex]))
            {
                for (int i = 0; i < poseOptions.Length; i++)
                {
                    bool selected = i == dropdownIndex;
                    if (ImGui.Selectable(poseOptions[i], selected))
                    {
                        byte newIndex = (byte)(i == 0 ? 7 : i - 1);
                        byte currentIndex = IsEditWindowOpen
                            ? plugin.Characters[selectedCharacterIndex].IdlePoseIndex
                            : plugin.NewCharacterIdlePoseIndex;

                        if (currentIndex != newIndex)
                        {
                            if (IsEditWindowOpen)
                                plugin.Characters[selectedCharacterIndex].IdlePoseIndex = newIndex;
                            else
                                plugin.NewCharacterIdlePoseIndex = newIndex;

                            if (isAdvancedModeCharacter)
                            {
                                UpdateAdvancedMacroIdlePose(newIndex);
                                if (!IsEditWindowOpen)
                                {
                                    plugin.NewCharacterMacros = advancedCharacterMacroText;
                                }
                            }
                            else
                            {
                                if (IsEditWindowOpen)
                                {
                                    editedCharacterMacros = GenerateMacro();
                                }
                                else
                                {
                                    plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                                }
                            }
                        }
                    }
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            // Tooltip
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300 * scale);
                ImGui.TextUnformatted("Sets your character's idle pose (0-6).\nChoose 'None' if you don't want Character Select+ to change your idle.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
        }

        private void DrawClassicImagePreview(float scale)
        {
            float side = 100f * scale;
            var origin = ImGui.GetCursorScreenPos();
            DrawPortraitPreviewBox(origin, side, scale);
            ImGui.Dummy(new Vector2(side, side));

            ImGui.SameLine();
            ImGui.BeginGroup();
            DrawFramingSliders(scale, 200f * scale,
                () => editedPortraitOffsetX, v => editedPortraitOffsetX = v,
                () => editedPortraitOffsetY, v => editedPortraitOffsetY = v,
                () => editedPortraitZoom,    v => editedPortraitZoom    = v,
                "classicPortrait", showResetButtons: true);
            ImGui.EndGroup();
        }

        private void DrawClassicMoodleField(float labelWidth, float inputWidth, float inputOffset, float scale)
{
            DrawClassicFormField("Moodle Preset", labelWidth, inputWidth, inputOffset, () =>
            {
                var moodleOptions = plugin.IntegrationListProvider?.GetMoodlesPresets() ?? Array.Empty<string>();

                if (AutocompleteCombo.Draw("##MoodlePreset", ref tempMoodlePreset, moodleOptions, inputWidth, "Select preset..."))
                {
                    if (IsEditWindowOpen)
                        editedCharacterMoodlePreset = tempMoodlePreset;
                    else
                        plugin.NewCharacterMoodlePreset = tempMoodlePreset;

                    if (isAdvancedModeCharacter)
                    {
                        UpdateAdvancedMacroMoodle(tempMoodlePreset);
                        if (!IsEditWindowOpen)
                        {
                            plugin.NewCharacterMacros = advancedCharacterMacroText;
                        }
                    }
                    else
                    {
                        if (IsEditWindowOpen)
                        {
                            editedCharacterMacros = GenerateMacro();
                        }
                        else
                        {
                            plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                        }
                    }
                }
            }, "Select the Moodle preset for this character. Right-click to clear.", scale);
        }

    }
}

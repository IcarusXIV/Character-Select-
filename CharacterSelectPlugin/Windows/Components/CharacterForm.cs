using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Windows.Forms;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin.Windows.Components
{
    public class CharacterForm : IDisposable
    {
        private Plugin plugin;
        private UIStyles uiStyles;

        // Form state
        public bool IsEditWindowOpen { get; private set; } = false;
        private int selectedCharacterIndex = -1;
        private bool isSecretMode = false;
        private bool isAdvancedModeCharacter = false;
        private string? pendingImagePath = null;

        // Edit fields
        private string editedCharacterName = "";
        private string editedCharacterMacros = "";
        private string? editedCharacterImagePath = null;
        private string nameValidationError = "";
        private Vector3 editedCharacterColor = new Vector3(1.0f, 1.0f, 1.0f);
        private string editedCharacterPenumbra = "";
        private string editedCharacterGlamourer = "";
        private string editedCharacterCustomize = "";
        private string editedCharacterTag = "";
        private string editedCharacterAutomation = "";
        private string editedCharacterMoodlePreset = "";

        // Honorific fields
        private string editedCharacterHonorificTitle = "";
        private string editedCharacterHonorificPrefix = "Prefix";
        private string editedCharacterHonorificSuffix = "Suffix";
        private Vector3 editedCharacterHonorificColor = new Vector3(1.0f, 1.0f, 1.0f);
        private Vector3 editedCharacterHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f);

        // Temp fields for live updates
        private string tempHonorificTitle = "";
        private string tempHonorificPrefix = "Prefix";
        private string tempHonorificSuffix = "Suffix";
        private Vector3 tempHonorificColor = new Vector3(1.0f, 1.0f, 1.0f);
        private Vector3 tempHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f);
        private string tempMoodlePreset = "";
        private string advancedCharacterMacroText = "";

        public CharacterForm(Plugin plugin, UIStyles uiStyles)
        {
            this.plugin = plugin;
            this.uiStyles = uiStyles;
        }

        public void Dispose()
        {
        }

        public void Draw()
        {
            if (!plugin.IsAddCharacterWindowOpen && !IsEditWindowOpen)
                return;

            var dpiScale = ImGui.GetIO().DisplayFramebufferScale.X;
            var uiScale = plugin.Configuration.UIScaleMultiplier;
            var totalScale = GetSafeScale(dpiScale * uiScale);

            // Check if Conflict Resolution is enabled and determine secret mode
            if (plugin.Configuration.EnableConflictResolution)
            {
                // Check if plugin.IsSecretMode is set (from Ctrl+Shift+Edit or Add)
                if (plugin.IsSecretMode && !isSecretMode)
                {
                    isSecretMode = true;
                    plugin.IsSecretMode = false; // Reset the flag
                }
                
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
                DrawCharacterFormContent(totalScale);
                ImGui.EndChild();
            }
            finally
            {
                uiStyles.PopFormStyle();
            }
        }

        private void DrawCharacterFormContent(float scale)
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
            DrawFormField("Character Name*", labelWidth, inputWidth, inputOffset, () =>
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
            }, "Enter your OC's name or nickname for profile here.", scale);

            ImGui.Separator();

            // Character Tags
            DrawFormField("Character Tags", labelWidth, inputWidth, inputOffset, () =>
            {
                ImGui.InputTextWithHint("##Tags", "e.g. Casual, Battle, Beach", ref tempTag, 100);

                if (IsEditWindowOpen) editedCharacterTag = tempTag;
                else plugin.NewCharacterTag = tempTag;
            }, "You can assign multiple tags by separating them with commas.\nExamples: Casual, Favourites, Seasonal", scale);

            ImGui.Separator();

            // Nameplate Colour
            DrawFormField("Nameplate Color", labelWidth, inputWidth, inputOffset, () =>
            {
                ImGui.ColorEdit3("##NameplateColor", ref tempColor);

                if (IsEditWindowOpen) editedCharacterColor = tempColor;
                else plugin.NewCharacterColor = tempColor;
            }, "Affects your character's nameplate under their profile picture in Character Select+.", scale);

            ImGui.Separator();

            // Penumbra Collection 
            DrawFormField("Penumbra Collection*", labelWidth, inputWidth, inputOffset, () =>
            {
                ImGui.InputText("##PenumbraCollection", ref tempPenumbra, 50);
                plugin.PenumbraFieldPos = ImGui.GetItemRectMin();
                plugin.PenumbraFieldSize = ImGui.GetItemRectSize();

                string oldValue = IsEditWindowOpen ? editedCharacterPenumbra : plugin.NewPenumbraCollection;
                if (oldValue != tempPenumbra)
                {
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
            }, "Enter the name of the Penumbra collection to apply to this character.\nMust be entered EXACTLY as it is named in Penumbra!", scale);

            ImGui.Separator();

            // Glamourer Design
            DrawFormField("Glamourer Design*", labelWidth, inputWidth, inputOffset, () =>
            {
                ImGui.InputText("##GlamourerDesign", ref tempGlamourer, 50);
                plugin.GlamourerFieldPos = ImGui.GetItemRectMin();
                plugin.GlamourerFieldSize = ImGui.GetItemRectSize();

                string oldValue = IsEditWindowOpen ? editedCharacterGlamourer : plugin.NewGlamourerDesign;
                if (oldValue != tempGlamourer)
                {
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
            }, "Enter the name of the Glamourer design to apply to this character.\nMust be entered EXACTLY as it is named in Glamourer!\nNote: You can add additional designs later.", scale);

            ImGui.Separator();

            // Automation (if enabled)
            if (plugin.Configuration.EnableAutomations)
            {
                DrawAutomationField(labelWidth, inputWidth, inputOffset, scale);
                ImGui.Separator();
            }

            // Customize+ Profile
            DrawCustomizeField(labelWidth, inputWidth, inputOffset, scale);
            ImGui.Separator();

            // Honorific Section
            DrawHonorificSection(labelWidth, inputWidth, inputOffset, scale);
            ImGui.Separator();

            // Moodle Preset
            DrawMoodleField(labelWidth, inputWidth, inputOffset, scale);
            ImGui.Separator();

            // Idle Pose
            DrawIdlePoseField(labelWidth, inputWidth, inputOffset, scale);
            ImGui.Separator();
            
            // Mod Manager (Conflict Resolution)
            if (isSecretMode)
            {
                DrawSecretModeModsField(labelWidth, inputWidth, inputOffset, scale);
                ImGui.Separator();
            }

            // Image Selection
            DrawImageSelection(scale);
            ImGui.Separator();

            // Advanced Mode Toggle
            DrawAdvancedModeSection(scale);
            ImGui.Separator();

            // Buttons!
            DrawActionButtons(scale);
        }

        private void DrawFormField(string label, float labelWidth, float inputWidth, float inputOffset,
                                 System.Action drawInput, string tooltip, float scale)
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
        }

        private void DrawSecretModeModsField(float labelWidth, float inputWidth, float inputOffset, float scale)
        {
            DrawFormField("Mod Manager", labelWidth, inputWidth, inputOffset, () =>
            {
                var selectedCount = IsEditWindowOpen && plugin.Characters[selectedCharacterIndex].SecretModState != null
                    ? plugin.Characters[selectedCharacterIndex].SecretModState.Count
                    : (plugin.NewSecretModState?.Count ?? 0);
                
                var buttonText = selectedCount > 0 
                    ? $"Configure Mods ({selectedCount} selected)###SecretMods"
                    : "Configure Mods###SecretMods";
                
                // Validate that character name is filled before opening mod manager
                string characterName = IsEditWindowOpen ? editedCharacterName : plugin.NewCharacterName;
                bool hasValidName = !string.IsNullOrWhiteSpace(characterName);
                
                if (!hasValidName)
                    ImGui.BeginDisabled();
                
                if (ImGui.Button(buttonText, new Vector2(inputWidth, 0)))
                {
                    if (hasValidName)
                    {
                        // Open the Secret Mode mod selection window
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
                            Plugin.Log.Information($"[PIN DEBUG] Character form loading pins for character {selectedCharacterIndex}: {currentPins?.Count ?? 0} pins - {string.Join(", ", currentPins ?? new HashSet<string>())}");
                        }
                        else
                        {
                            currentSelection = plugin.NewSecretModState;
                            currentPins = plugin.NewSecretModPins != null ? new HashSet<string>(plugin.NewSecretModPins) : null;
                            Plugin.Log.Information($"[PIN DEBUG] Character form loading pins for new character: {currentPins?.Count ?? 0} pins - {string.Join(", ", currentPins ?? new HashSet<string>())}");
                        }
                        
                        Plugin.Log.Information($"[PIN DEBUG] About to pass pins to mod manager: {currentPins?.Count ?? 0} pins - {string.Join(", ", currentPins ?? new HashSet<string>())}");
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
                                    Plugin.Log.Information($"[PIN DEBUG] Character save callback: saving {pins?.Count ?? 0} pins to character {selectedCharacterIndex}");
                                    plugin.Characters[selectedCharacterIndex].SecretModPins = pins?.ToList();
                                    plugin.SaveConfiguration();
                                }
                                else
                                {
                                    Plugin.Log.Information($"[PIN DEBUG] New character save callback: saving {pins?.Count ?? 0} pins to NewSecretModPins");
                                    plugin.NewSecretModPins = pins?.ToList();
                                }
                            },
                            null,  // No design context for character-level operations
                            characterName  // Pass the character name for context
                        );
                    }
                }
                
                if (!hasValidName)
                {
                    ImGui.EndDisabled();
                    
                    // Show tooltip explaining why the button is disabled
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        ImGui.BeginTooltip();
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.7f, 0.7f, 1.0f));
                        ImGui.Text("Please enter a Character Name before configuring mods.");
                        ImGui.PopStyleColor();
                        ImGui.EndTooltip();
                    }
                }
            }, "Select which mods to enable and configure their options for this character.\nAllows different characters to use different mod combinations and settings.", scale);
        }
        
        private void DrawAutomationField(float labelWidth, float inputWidth, float inputOffset, float scale)
        {
            string tempCharacterAutomation = IsEditWindowOpen ? editedCharacterAutomation : plugin.NewCharacterAutomation;

            DrawFormField("Glam. Automation", labelWidth, inputWidth, inputOffset, () =>
            {
                if (ImGui.InputText("##Glam.Automation", ref tempCharacterAutomation, 50))
                {
                    string oldValue = IsEditWindowOpen ? editedCharacterAutomation : plugin.NewCharacterAutomation;
                    if (oldValue != tempCharacterAutomation)
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
                }
            }, "Enter the name of a Glamourer Automation profile to apply when this character is activated.\nDesign-level automations override this if both are set.\nLeave blank to default to a fallback profile named 'None'.", scale);
        }

        private void DrawCustomizeField(float labelWidth, float inputWidth, float inputOffset, float scale)
        {
            string tempCustomize = IsEditWindowOpen ? editedCharacterCustomize : plugin.NewCustomizeProfile;

            DrawFormField("Customize+ Profile", labelWidth, inputWidth, inputOffset, () =>
            {
                if (ImGui.InputText("##CustomizeProfile", ref tempCustomize, 50))
                {
                    string oldValue = IsEditWindowOpen ? editedCharacterCustomize : plugin.NewCustomizeProfile;
                    if (oldValue != tempCustomize)
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
                }
            }, "Enter the name of the Customize+ profile to apply to this character.\nMust be entered EXACTLY as it is named in Customize+!", scale);
        }

        private void DrawHonorificSection(float labelWidth, float inputWidth, float inputOffset, float scale)
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

            // Colour pickers
            ImGui.SameLine();
            ImGui.SetNextItemWidth(40 * scale);
            changed |= ImGui.ColorEdit3("##HonorificColor", ref tempHonorificColor, ImGuiColorEditFlags.NoInputs);

            ImGui.SameLine();
            ImGui.SetNextItemWidth(40 * scale);
            changed |= ImGui.ColorEdit3("##HonorificGlow", ref tempHonorificGlow, ImGuiColorEditFlags.NoInputs);

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

            // Tooltip
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300 * scale);
                ImGui.TextUnformatted("This will set a forced title when you switch to this character.\nThe dropdown selects if the title appears above (prefix) or below (suffix) your name in-game.\nUse the Honorific plug-in's 'Clear' button if you need to remove it.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
        }

        private void DrawMoodleField(float labelWidth, float inputWidth, float inputOffset, float scale)
        {
            DrawFormField("Moodle Preset", labelWidth, inputWidth, inputOffset, () =>
            {
                if (ImGui.InputText("##MoodlePreset", ref tempMoodlePreset, 50))
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
            }, "Enter the Moodle preset name exactly as saved in the Moodle plugin.\nExample: 'HappyFawn' will apply the preset named 'HappyFawn'.", scale);
        }

        private void DrawIdlePoseField(float labelWidth, float inputWidth, float inputOffset, float scale)
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
                ImGui.TextUnformatted("Sets your character's idle pose (0–6).\nChoose 'None' if you don't want Character Select+ to change your idle.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
        }

        private void DrawImageSelection(float scale)
        {
            if (ImGui.Button("Choose Image", new Vector2(0, 25 * scale)))
            {
                try
                {
                    Thread thread = new Thread(() =>
                    {
                        try
                        {
                            using (OpenFileDialog openFileDialog = new OpenFileDialog())
                            {
                                openFileDialog.Filter = "PNG files (*.png)|*.png";
                                openFileDialog.Title = "Select Character Image";

                                if (openFileDialog.ShowDialog() == DialogResult.OK)
                                {
                                    lock (this)
                                    {
                                        pendingImagePath = openFileDialog.FileName;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.Error($"Error opening file picker: {ex.Message}");
                        }
                    });

                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"Critical file picker error: {ex.Message}");
                }
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
            DrawImagePreview(scale);
        }

        private void DrawImagePreview(float scale)
        {
            string pluginDirectory = plugin.PluginDirectory;
            string defaultImagePath = Path.Combine(pluginDirectory, "Assets", "Default.png");

            string? imagePath = IsEditWindowOpen ? editedCharacterImagePath : plugin.NewCharacterImagePath;
            string finalImagePath = !string.IsNullOrEmpty(imagePath) && File.Exists(imagePath)
                ? imagePath
                : defaultImagePath;

            if (!string.IsNullOrEmpty(finalImagePath) && File.Exists(finalImagePath))
            {
                var texture = Plugin.TextureProvider.GetFromFile(finalImagePath).GetWrapOrDefault();
                if (texture != null)
                {
                    float originalWidth = texture.Width;
                    float originalHeight = texture.Height;
                    float maxSize = 100f * scale;

                    float aspectRatio = originalWidth / originalHeight;
                    float displayWidth, displayHeight;

                    if (aspectRatio > 1)
                    {
                        displayWidth = maxSize;
                        displayHeight = maxSize / aspectRatio;
                    }
                    else
                    {
                        displayHeight = maxSize;
                        displayWidth = maxSize * aspectRatio;
                    }

                    var cursorPos = ImGui.GetCursorScreenPos();
                    var imageEnd = cursorPos + new Vector2(displayWidth, displayHeight);

                    uiStyles.DrawGlowingBorder(
                        cursorPos - new Vector2(2 * scale, 2 * scale),
                        imageEnd + new Vector2(2 * scale, 2 * scale),
                        new Vector3(0.5f, 0.5f, 0.5f),
                        0.3f,
                        false,
                        scale
                    );

                    ImGui.Image((ImTextureID)texture.Handle, new Vector2(displayWidth, displayHeight));
                }
                else
                {
                    ImGui.Text($"Failed to load image: {Path.GetFileName(finalImagePath)}");
                }
            }
            else
            {
                ImGui.Text("No Image Available");
            }
        }

        private void DrawAdvancedModeSection(float scale)
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
        private float GetSafeScale(float baseScale)
        {
            return Math.Clamp(baseScale, 0.3f, 5.0f); // Prevent extreme scaling
        }

        private void DrawActionButtons(float scale)
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

                    plugin.SaveNewCharacter(finalMacro);
                }

                CloseForm();
            }

            plugin.SaveButtonPos = ImGui.GetItemRectMin();
            plugin.SaveButtonSize = ImGui.GetItemRectSize();

            if (!canSaveCharacter)
                ImGui.EndDisabled();

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(0, 30 * scale)))
            {
                CloseForm();
            }

            uiStyles.PopDarkButtonStyle();
        }

        // Advanced mode update methods
        private void UpdateAdvancedMacroPenumbra(string collection)
        {
            advancedCharacterMacroText = PatchMacroLine(
                advancedCharacterMacroText,
                "/penumbra collection",
                $"/penumbra collection individual | {collection} | self"
            );

            advancedCharacterMacroText = UpdateCollectionInLines(
                advancedCharacterMacroText,
                "/penumbra bulktag disable",
                collection
            );

            advancedCharacterMacroText = UpdateCollectionInLines(
                advancedCharacterMacroText,
                "/penumbra bulktag enable",
                collection
            );
        }

        private void UpdateAdvancedMacroGlamourer(string oldGlamourer, string newGlamourer)
        {
            var lines = advancedCharacterMacroText.Split('\n').ToList();

            // Find and replace the main glamour apply line (not "no clothes")
            bool foundExistingLine = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("/glamour apply", StringComparison.OrdinalIgnoreCase) &&
                    !line.Contains("no clothes", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"/glamour apply {newGlamourer} | self";
                    foundExistingLine = true;
                    break;
                }
            }

            // Update bulktag enable line if it exists (for secret mode....shhh! how can it stay a secret if I keep mentioning it??)
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("/penumbra bulktag enable", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 2)
                    {
                        var collection = parts[0].Replace("/penumbra bulktag enable", "").Trim();
                        lines[i] = $"/penumbra bulktag enable {collection} | {newGlamourer}";
                    }
                    break;
                }
            }

            if (!foundExistingLine && !string.IsNullOrWhiteSpace(newGlamourer))
            {
                var insertPos = GetProperInsertPosition(lines, "/glamour apply");
                lines.Insert(insertPos, $"/glamour apply {newGlamourer} | self");
            }

            advancedCharacterMacroText = string.Join("\n", lines);
        }

        private void UpdateAdvancedMacroAutomation(string automation)
        {
            var line = string.IsNullOrWhiteSpace(automation)
                ? "/glamour automation enable None"
                : $"/glamour automation enable {automation}";

            advancedCharacterMacroText = PatchMacroLine(
                advancedCharacterMacroText,
                "/glamour automation enable",
                line
            );
        }

        private void UpdateAdvancedMacroCustomize(string customize)
        {
            advancedCharacterMacroText = PatchMacroLine(
                advancedCharacterMacroText,
                "/customize profile disable",
                "/customize profile disable <me>"
            );

            if (!string.IsNullOrWhiteSpace(customize))
            {
                advancedCharacterMacroText = PatchMacroLine(
                    advancedCharacterMacroText,
                    "/customize profile enable",
                    $"/customize profile enable <me>, {customize}"
                );
            }
            else
            {
                advancedCharacterMacroText = string.Join("\n",
                    advancedCharacterMacroText
                        .Split('\n')
                        .Where(l => !l.TrimStart().StartsWith("/customize profile enable"))
                );
            }
        }

        private void UpdateAdvancedMacroHonorific()
        {
            var lines = advancedCharacterMacroText.Split('\n').ToList();

            var clearIdx = lines.FindIndex(l =>
                l.TrimStart().StartsWith("/honorific force clear", StringComparison.OrdinalIgnoreCase));

            if (clearIdx < 0)
            {
                var insertPos = GetProperInsertPosition(lines, "/honorific force clear");
                lines.Insert(insertPos, "/honorific force clear");
                clearIdx = insertPos;
            }

            if (!string.IsNullOrWhiteSpace(tempHonorificTitle))
            {
                var c = tempHonorificColor;
                var g = tempHonorificGlow;
                string colorHex = $"#{(int)(c.X * 255):X2}{(int)(c.Y * 255):X2}{(int)(c.Z * 255):X2}";
                string glowHex = $"#{(int)(g.X * 255):X2}{(int)(g.Y * 255):X2}{(int)(g.Z * 255):X2}";
                string setLine = $"/honorific force set {tempHonorificTitle} | {tempHonorificPrefix} | {colorHex} | {glowHex}";

                var setIdx = lines.FindIndex(l =>
                    l.TrimStart().StartsWith("/honorific force set", StringComparison.OrdinalIgnoreCase));

                if (setIdx >= 0)
                {
                    lines[setIdx] = setLine;
                }
                else
                {
                    lines.Insert(clearIdx + 1, setLine);
                }
            }
            else
            {
                lines.RemoveAll(l => l.TrimStart().StartsWith("/honorific force set", StringComparison.OrdinalIgnoreCase));
            }

            advancedCharacterMacroText = string.Join("\n", lines);
        }


        private void UpdateAdvancedMacroMoodle(string preset)
        {
            var lines = advancedCharacterMacroText.Split('\n').ToList();

            var removeIdx = lines.FindIndex(l =>
                l.TrimStart().StartsWith("/moodle remove self preset all", StringComparison.OrdinalIgnoreCase));

            if (removeIdx < 0)
            {
                var insertPos = GetProperInsertPosition(lines, "/moodle remove");
                lines.Insert(insertPos, "/moodle remove self preset all");
                removeIdx = insertPos;
            }

            if (!string.IsNullOrWhiteSpace(preset))
            {
                string applyLine = $"/moodle apply self preset \"{preset}\"";
                var applyIdx = lines.FindIndex(l =>
                    l.TrimStart().StartsWith("/moodle apply self preset", StringComparison.OrdinalIgnoreCase));

                if (applyIdx >= 0)
                {
                    lines[applyIdx] = applyLine;
                }
                else
                {
                    lines.Insert(removeIdx + 1, applyLine);
                }
            }
            else
            {
                lines.RemoveAll(l => l.TrimStart().StartsWith("/moodle apply self preset", StringComparison.OrdinalIgnoreCase));
            }

            advancedCharacterMacroText = string.Join("\n", lines);
        }

        private void UpdateAdvancedMacroIdlePose(byte poseIndex)
        {
            var lines = advancedCharacterMacroText.Split('\n').ToList();

            if (poseIndex != 7)
            {
                string sidleLine = $"/sidle {poseIndex}";
                var sidleIdx = lines.FindIndex(l =>
                    l.TrimStart().StartsWith("/sidle", StringComparison.OrdinalIgnoreCase));

                if (sidleIdx >= 0)
                {
                    lines[sidleIdx] = sidleLine;
                }
                else
                {
                    var insertPos = GetProperInsertPosition(lines, "/sidle");
                    lines.Insert(insertPos, sidleLine);
                }
            }
            else
            {
                // Remove any existing sidle line when pose is "None"
                lines.RemoveAll(l => l.TrimStart().StartsWith("/sidle", StringComparison.OrdinalIgnoreCase));
            }

            advancedCharacterMacroText = string.Join("\n", lines);
        }

        private void UpdateHonorificData()
        {
            if (IsEditWindowOpen)
            {
                editedCharacterHonorificTitle = tempHonorificTitle;
                editedCharacterHonorificPrefix = tempHonorificPrefix;
                editedCharacterHonorificSuffix = tempHonorificSuffix;
                editedCharacterHonorificColor = tempHonorificColor;
                editedCharacterHonorificGlow = tempHonorificGlow;
            }
            else
            {
                plugin.NewCharacterHonorificTitle = tempHonorificTitle;
                plugin.NewCharacterHonorificPrefix = tempHonorificPrefix;
                plugin.NewCharacterHonorificSuffix = tempHonorificSuffix;
                plugin.NewCharacterHonorificColor = tempHonorificColor;
                plugin.NewCharacterHonorificGlow = tempHonorificGlow;
            }
        }
        private string PatchMacroLine(string existing, string prefix, string replacement)
        {
            var lines = existing.Split('\n').ToList();
            var idx = lines.FindIndex(l => l.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

            if (idx >= 0)
            {
                lines[idx] = replacement;
            }
            else
            {
                int insertPosition = GetProperInsertPosition(lines, prefix);
                lines.Insert(insertPosition, replacement);
            }

            return string.Join("\n", lines);
        }

        private int GetProperInsertPosition(List<string> lines, string prefix)
        {
            var order = new[]
            {
                "/penumbra collection",
                "/penumbra bulktag disable",
                "/penumbra bulktag enable",
                "/glamour apply no clothes",
                "/glamour apply",
                "/glamour automation enable",
                "/customize profile disable",
                "/customize profile enable",
                "/honorific force clear",
                "/honorific force set",
                "/moodle remove",
                "/moodle apply",
                "/sidle",
                "/penumbra redraw"
            };

            int targetOrder = Array.FindIndex(order, o => prefix.StartsWith(o, StringComparison.OrdinalIgnoreCase));
            if (targetOrder == -1) return lines.Count;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].TrimStart();
                int lineOrder = Array.FindIndex(order, o => line.StartsWith(o, StringComparison.OrdinalIgnoreCase));

                if (lineOrder > targetOrder || lineOrder == -1)
                {
                    return i;
                }
            }

            return lines.Count;
        }

        private string UpdateCollectionInLines(string existing, string prefix, string newCollection)
        {
            var lines = existing.Split('\n').Select(line =>
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var rest = trimmed.Substring(prefix.Length).TrimStart();
                    var afterCollection = rest.IndexOf('|') >= 0
                        ? rest.Substring(rest.IndexOf('|'))
                        : rest.Substring(rest.IndexOf(' '));
                    return $"{prefix} {newCollection} {afterCollection}";
                }
                return line;
            });
            return string.Join("\n", lines);
        }

        private string GenerateMacro()
        {
            string penumbra = IsEditWindowOpen ? editedCharacterPenumbra : plugin.NewPenumbraCollection;
            string glamourer = IsEditWindowOpen ? editedCharacterGlamourer : plugin.NewGlamourerDesign;
            string customize = IsEditWindowOpen ? editedCharacterCustomize : plugin.NewCustomizeProfile;
            string honorificTitle = IsEditWindowOpen ? editedCharacterHonorificTitle : plugin.NewCharacterHonorificTitle;
            string honorificPrefix = IsEditWindowOpen ? editedCharacterHonorificPrefix : plugin.NewCharacterHonorificPrefix;
            Vector3 honorificColor = IsEditWindowOpen ? editedCharacterHonorificColor : plugin.NewCharacterHonorificColor;
            Vector3 honorificGlow = IsEditWindowOpen ? editedCharacterHonorificGlow : plugin.NewCharacterHonorificGlow;
            string automation = IsEditWindowOpen ? editedCharacterAutomation : plugin.NewCharacterAutomation;
            string moodlePreset = IsEditWindowOpen ? editedCharacterMoodlePreset : plugin.NewCharacterMoodlePreset;
            int idlePose = IsEditWindowOpen ? plugin.Characters[selectedCharacterIndex].IdlePoseIndex : plugin.NewCharacterIdlePoseIndex;

            if (string.IsNullOrWhiteSpace(penumbra) || string.IsNullOrWhiteSpace(glamourer))
                return "/penumbra redraw self";

            string macro = $"/penumbra collection individual | {penumbra} | self\n";
            macro += $"/glamour apply {glamourer} | self\n";

            if (plugin.Configuration.EnableAutomations)
            {
                if (string.IsNullOrWhiteSpace(automation))
                    macro += "/glamour automation enable None\n";
                else
                    macro += $"/glamour automation enable {automation}\n";
            }

            macro += "/customize profile disable <me>\n";
            if (!string.IsNullOrWhiteSpace(customize))
                macro += $"/customize profile enable <me>, {customize}\n";

            macro += "/honorific force clear\n";
            if (!string.IsNullOrWhiteSpace(honorificTitle))
            {
                string colorHex = $"#{(int)(honorificColor.X * 255):X2}{(int)(honorificColor.Y * 255):X2}{(int)(honorificColor.Z * 255):X2}";
                string glowHex = $"#{(int)(honorificGlow.X * 255):X2}{(int)(honorificGlow.Y * 255):X2}{(int)(honorificGlow.Z * 255):X2}";
                macro += $"/honorific force set {honorificTitle} | {honorificPrefix} | {colorHex} | {glowHex}\n";
            }

            macro += "/moodle remove self preset all\n";
            if (!string.IsNullOrWhiteSpace(moodlePreset))
                macro += $"/moodle apply self preset \"{moodlePreset}\"\n";

            if (idlePose != 7)
                macro += $"/sidle {idlePose}\n";

            macro += "/penumbra redraw self";

            return macro;
        }

        private string GenerateSecretMacro()
        {
            string penumbra = IsEditWindowOpen ? editedCharacterPenumbra : plugin.NewPenumbraCollection;
            string glamourer = IsEditWindowOpen ? editedCharacterGlamourer : plugin.NewGlamourerDesign;
            string customize = IsEditWindowOpen ? editedCharacterCustomize : plugin.NewCustomizeProfile;
            string honorTitle = IsEditWindowOpen ? editedCharacterHonorificTitle : plugin.NewCharacterHonorificTitle;
            string honorPref = IsEditWindowOpen ? editedCharacterHonorificPrefix : plugin.NewCharacterHonorificPrefix;
            Vector3 honorColor = IsEditWindowOpen ? editedCharacterHonorificColor : plugin.NewCharacterHonorificColor;
            Vector3 honorGlow = IsEditWindowOpen ? editedCharacterHonorificGlow : plugin.NewCharacterHonorificGlow;
            string moodlePreset = IsEditWindowOpen ? editedCharacterMoodlePreset : plugin.NewCharacterMoodlePreset;
            int idlePose = IsEditWindowOpen
                                    ? plugin.Characters[selectedCharacterIndex].IdlePoseIndex
                                    : plugin.NewCharacterIdlePoseIndex;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"/penumbra collection individual | {penumbra} | self");
            sb.AppendLine($"/penumbra bulktag disable {penumbra} | gear");
            sb.AppendLine($"/penumbra bulktag disable {penumbra} | hair");
            sb.AppendLine($"/penumbra bulktag enable {penumbra} | {glamourer}");
            sb.AppendLine("/glamour apply no clothes | self");
            sb.AppendLine($"/glamour apply {glamourer} | self");

            if (plugin.Configuration.EnableAutomations)
            {
                string automation = IsEditWindowOpen ? editedCharacterAutomation : plugin.NewCharacterAutomation;
                if (string.IsNullOrWhiteSpace(automation))
                    sb.AppendLine("/glamour automation enable None");
                else
                    sb.AppendLine($"/glamour automation enable {automation}");
            }

            sb.AppendLine("/customize profile disable <me>");
            if (!string.IsNullOrWhiteSpace(customize))
                sb.AppendLine($"/customize profile enable <me>, {customize}");

            sb.AppendLine("/honorific force clear");
            if (!string.IsNullOrWhiteSpace(honorTitle))
            {
                var colorHex = $"#{(int)(honorColor.X * 255):X2}{(int)(honorColor.Y * 255):X2}{(int)(honorColor.Z * 255):X2}";
                var glowHex = $"#{(int)(honorGlow.X * 255):X2}{(int)(honorGlow.Y * 255):X2}{(int)(honorGlow.Z * 255):X2}";
                sb.AppendLine($"/honorific force set {honorTitle} | {honorPref} | {colorHex} | {glowHex}");
            }

            sb.AppendLine("/moodle remove self preset all");
            if (!string.IsNullOrWhiteSpace(moodlePreset))
                sb.AppendLine($"/moodle apply self preset \"{moodlePreset}\"");

            if (idlePose != 7)
                sb.AppendLine($"/sidle {idlePose}");

            sb.Append("/penumbra redraw self");
            return sb.ToString();
        }

        public void SetSecretMode(bool secretMode)
        {
            isSecretMode = secretMode;
            if (secretMode && !IsEditWindowOpen)
            {
                plugin.NewCharacterMacros = (secretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
            }
        }

        private void CloseForm()
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

        public void ResetFields()
        {
            plugin.NewCharacterName = "";
            plugin.NewCharacterColor = new Vector3(1.0f, 1.0f, 1.0f);
            plugin.NewPenumbraCollection = "";
            plugin.NewGlamourerDesign = "";
            plugin.NewCharacterAutomation = "";
            plugin.NewCustomizeProfile = "";
            plugin.NewCharacterImagePath = null;
            plugin.NewCharacterDesigns.Clear();
            plugin.NewCharacterHonorificTitle = "";
            plugin.NewCharacterHonorificPrefix = "Prefix";
            plugin.NewCharacterHonorificSuffix = "Suffix";
            plugin.NewCharacterHonorificColor = new Vector3(1.0f, 1.0f, 1.0f);
            plugin.NewCharacterHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f);
            plugin.NewCharacterMoodlePreset = "";
            plugin.NewCharacterIdlePoseIndex = 7;
            plugin.NewCharacterIsAdvancedMode = false;
            // Reset local temp fields
            tempHonorificTitle = "";
            tempHonorificPrefix = "Prefix";
            tempHonorificSuffix = "Suffix";
            tempHonorificColor = new Vector3(1.0f, 1.0f, 1.0f);
            tempHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f);
            tempMoodlePreset = "";

            // Reset edit fields
            editedCharacterName = "";
            editedCharacterMacros = "";
            editedCharacterImagePath = null;
            editedCharacterColor = new Vector3(1.0f, 1.0f, 1.0f);
            editedCharacterPenumbra = "";
            editedCharacterGlamourer = "";
            editedCharacterCustomize = "";
            editedCharacterTag = "";
            editedCharacterAutomation = "";
            editedCharacterMoodlePreset = "";
            editedCharacterHonorificTitle = "";
            editedCharacterHonorificPrefix = "Prefix";
            editedCharacterHonorificSuffix = "Suffix";
            editedCharacterHonorificColor = new Vector3(1.0f, 1.0f, 1.0f);
            editedCharacterHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f);

            advancedCharacterMacroText = "";

            // Only regenerate macro if not in advanced mode
            if (!isAdvancedModeCharacter)
            {
                plugin.NewCharacterMacros = GenerateMacro();
            }
        }

        private void SaveEditedCharacter()
        {
            if (selectedCharacterIndex < 0 || selectedCharacterIndex >= plugin.Characters.Count)
                return;

            var character = plugin.Characters[selectedCharacterIndex];

            character.Name = editedCharacterName;
            character.Tags = string.IsNullOrWhiteSpace(editedCharacterTag)
                ? new List<string>()
                : editedCharacterTag.Split(',').Select(f => f.Trim()).ToList();
            character.PenumbraCollection = editedCharacterPenumbra;
            character.GlamourerDesign = editedCharacterGlamourer;
            character.CustomizeProfile = editedCharacterCustomize;
            character.NameplateColor = editedCharacterColor;
            character.CharacterAutomation = editedCharacterAutomation;
            character.HonorificTitle = editedCharacterHonorificTitle;
            character.HonorificPrefix = editedCharacterHonorificPrefix;
            character.HonorificSuffix = editedCharacterHonorificSuffix;
            character.HonorificColor = editedCharacterHonorificColor;
            character.HonorificGlow = editedCharacterHonorificGlow;
            character.MoodlePreset = editedCharacterMoodlePreset;

            character.Macros = isAdvancedModeCharacter ? advancedCharacterMacroText : editedCharacterMacros;

            if (!string.IsNullOrEmpty(editedCharacterImagePath))
            {
                character.ImagePath = editedCharacterImagePath;
            }

            // Note: SecretModState is handled directly in the SecretModeModWindow callback
            // and doesn't need to be copied here since it's already persisted to the character object

            plugin.SaveConfiguration();
        }

        public void OpenEditCharacterWindow(int index)
        {
            if (index < 0 || index >= plugin.Characters.Count)
                return;

            selectedCharacterIndex = index;
            var character = plugin.Characters[index];

            string pluginDirectory = plugin.PluginDirectory;
            string defaultImagePath = Path.Combine(pluginDirectory, "Assets", "Default.png");

            editedCharacterName = character.Name;
            editedCharacterPenumbra = character.PenumbraCollection;
            editedCharacterGlamourer = character.GlamourerDesign;
            editedCharacterCustomize = character.CustomizeProfile ?? "";
            editedCharacterColor = character.NameplateColor;

            editedCharacterMacros = character.Macros;

            editedCharacterImagePath = !string.IsNullOrEmpty(character.ImagePath) ? character.ImagePath : defaultImagePath;
            editedCharacterTag = character.Tags != null && character.Tags.Count > 0
                ? string.Join(", ", character.Tags)
                : "";

            editedCharacterHonorificTitle = character.HonorificTitle ?? "";
            editedCharacterHonorificPrefix = character.HonorificPrefix ?? "Prefix";
            editedCharacterHonorificSuffix = character.HonorificSuffix ?? "Suffix";
            editedCharacterHonorificColor = character.HonorificColor;
            editedCharacterHonorificGlow = character.HonorificGlow;
            editedCharacterMoodlePreset = character.MoodlePreset ?? "";

            string safeAutomation = character.CharacterAutomation == "None" ? "" : character.CharacterAutomation ?? "";
            editedCharacterAutomation = safeAutomation;

            // Copy to temp fields
            tempHonorificTitle = editedCharacterHonorificTitle;
            tempHonorificPrefix = editedCharacterHonorificPrefix;
            tempHonorificSuffix = editedCharacterHonorificSuffix;
            tempHonorificColor = editedCharacterHonorificColor;
            tempHonorificGlow = editedCharacterHonorificGlow;
            tempMoodlePreset = editedCharacterMoodlePreset;

            if (isAdvancedModeCharacter)
            {
                advancedCharacterMacroText = character.Macros;
            }
            // Restore advanced mode state
            isAdvancedModeCharacter = character.IsAdvancedMode;

            if (isAdvancedModeCharacter)
            {
                advancedCharacterMacroText = character.Macros;
            }
            IsEditWindowOpen = true;
        }

        private void ValidateCharacterName(string name)
        {
            nameValidationError = "";
            
            if (string.IsNullOrWhiteSpace(name))
                return;

            // Check if name already exists
            bool nameExists;
            if (IsEditWindowOpen && selectedCharacterIndex >= 0 && selectedCharacterIndex < plugin.Characters.Count)
            {
                // When editing, exclude the current character from the check
                var currentCharName = plugin.Characters[selectedCharacterIndex].Name;
                nameExists = plugin.Characters.Any(c => 
                    !c.Name.Equals(currentCharName, StringComparison.OrdinalIgnoreCase) && 
                    c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                // When creating new, check all characters
                nameExists = plugin.Characters.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }

            if (nameExists)
            {
                nameValidationError = "You already have a character with this name. Please choose a different name. " +
                                    "Try adding a number or variation (e.g., Name 2, Name Alt, etc.)";
            }
        }
    }
}

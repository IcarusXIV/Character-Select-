using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using CharacterSelectPlugin.Windows.Styles;
using CharacterSelectPlugin.Windows.Utils;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Dalamud.Interface.Textures.TextureWraps;
using CharacterSelectPlugin.Effects;

namespace CharacterSelectPlugin.Windows.Components
{
    public partial class DesignPanel
    {
        private List<CharacterDesign> cachedFilteredDesigns = new();
        private bool filterCacheDirty = true;
        private string lastSearchQuery = "";
        private string lastSelectedTag = "All";
        private int lastDesignCount = -1;
        private string selectedTag = "All";
        private bool showTagFilter = false;

        public void DrawClassicLayout()
{
            if (!IsOpen) return;

            // Calculate responsive sizing
            var totalScale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);

            // Scale the panel dimensions
            float scaledPanelWidth = PanelWidth * GetSafeScale(totalScale);
            float scaledMinWidth = MinPanelWidth * totalScale;
            float scaledMaxWidth = MaxPanelWidth * totalScale;
            float scaledHandleWidth = resizeHandleWidth * totalScale;

            DrawClassicDesignPanelContent(totalScale, scaledPanelWidth);
            DrawResizeHandle(totalScale, scaledPanelWidth, scaledMinWidth, scaledMaxWidth, scaledHandleWidth);

            if (IsOpen)
            {
                ClassicUpdateEffects();
            }

            DrawClassicImportWindow(totalScale);
            DrawClassicAdvancedModeWindow(totalScale);
            DrawClassicSnapshotDialog(totalScale);
        }

        private void DrawClassicDesignPanelContent(float totalScale, float scaledPanelWidth)
{
            if (activeCharacterIndex < 0 || activeCharacterIndex >= plugin.Characters.Count)
                return;

            var character = plugin.Characters[activeCharacterIndex];

            ApplyScaledStyles(totalScale);

            try
            {
                DrawClassicHeader(character, totalScale);

                if (isEditDesignWindowOpen)
                {
                    DrawClassicDesignForm(character, totalScale);
                    ImGui.Separator();
                }

                DrawClassicSortingControls(character, totalScale);
                ImGui.Separator();

                DrawClassicDesignList(character, totalScale);
            }
            finally
            {
                PopScaledStyles();
            }
        }

        private void DrawClassicHeader(Character character, float scale)
{
            float buttonSize = 25f * scale;
            float spacing = 2f * scale;

            
            ImGui.BeginGroup();

            // Add and Folder buttons
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.27f, 1.07f, 0.27f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.25f, 0.25f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.35f, 0.35f, 0.35f, 1f));

            if (ImGui.Button("+##AddDesign", new Vector2(buttonSize, buttonSize)))
            {
                isSecretDesignMode = false;
                AddNewDesign();
                editedDesignMacro = GenerateDesignMacro(character);
                if (isAdvancedModeDesign)
                    advancedDesignMacroText = editedDesignMacro;
            }

            plugin.DesignPanelAddButtonPos = ImGui.GetItemRectMin();
            plugin.DesignPanelAddButtonSize = ImGui.GetItemRectSize();
            uiStyles.ApplyHoverSheenToLastItem("design_add_btn");

            ImGui.PopStyleColor(4);

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("Add a new design");
                ImGui.EndTooltip();
            }

            ImGui.SameLine(0, spacing);

            // Folder Button
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.7f, 0.3f, 1.0f)); // Yellow
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.25f, 0.25f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.35f, 0.35f, 0.35f, 1f));

            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button("\uf07b##AddFolder"))
                ImGui.OpenPopup("CreateFolderPopup");
            ImGui.PopFont();
            uiStyles.ApplyHoverSheenToLastItem("design_folder_btn");

            ImGui.PopStyleColor(4);

            DrawClassicFolderCreationPopup(character, scale);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Add Folder");
            }

            // Search button
            ImGui.SameLine(0, spacing);
            if (uiStyles.IconButton("\uf002", "Search designs"))
            {
                showSearchBar = !showSearchBar;
                if (!showSearchBar)
                {
                    searchQuery = "";
                    ClassicInvalidateFilterCache();
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Search designs");

            // Wardrobe button (visual design browser)
            ImGui.SameLine(0, spacing);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.83f, 0.69f, 0.22f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.22f, 0.15f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.35f, 0.30f, 0.18f, 1f));
            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button("\uf00a##OpenWardrobe"))
            {
                if (plugin.WardrobeWindow != null)
                {
                    // Design Panel's wardrobe button clears any Shift+Click target, uses active character
                    plugin.WardrobeWindow.TargetCharacter = null;
                    plugin.WardrobeWindow.IsOpen = !plugin.WardrobeWindow.IsOpen;
                    if (plugin.WardrobeWindow.IsOpen) plugin.AchievementTracker?.OnWardrobeOpened();
                }
            }
            ImGui.PopFont();
            uiStyles.ApplyHoverSheenToLastItem("design_wardrobe_btn");
            ImGui.PopStyleColor(4);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Open Wardrobe (visual design browser)");

            // Snapshot button (right-aligned)
            ImGui.SameLine();
            float availableWidth = ImGui.GetContentRegionAvail().X;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availableWidth - (buttonSize * 2) - (5 * scale));

            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));        // Dark grey
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.4f, 0.4f, 0.9f)); // Medium grey
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));  // Light grey
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 1.0f, 1.0f));          // White text
            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));        // Center icon

            if (ImGui.Button($"\uf030##CreateSnapshot"))
            {
                if (activeCharacterIndex >= 0 && activeCharacterIndex < plugin.Characters.Count)
                {
                    var selectedCharacter = plugin.Characters[activeCharacterIndex];
                    CreateSmartSnapshot(selectedCharacter, plugin.Configuration.EnableConflictResolution);
                }
            }

            ImGui.PopStyleVar(1);
            ImGui.PopStyleColor(4);
            ImGui.PopFont();
            uiStyles.ApplyHoverSheenToLastItem("design_snapshot_btn");

            if (ImGui.IsItemHovered())
            {
                string tooltip = plugin.Configuration.EnableConflictResolution
                    ? "Snapshot current look as a new Design (with Conflict Resolution)"
                    : "Snapshot current look as a new Design";
                ImGui.SetTooltip(tooltip);
            }

            // Close button
            ImGui.SameLine(0, spacing);

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.27f, 0.27f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.2f, 0.2f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.3f, 0.3f, 1f));

            if (ImGui.Button("×##CloseDesignPanel"))
            {
                Close();
            }
            uiStyles.ApplyHoverSheenToLastItem("design_close_btn");

            ImGui.PopStyleColor(4);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Close Design Panel");
            }

            ImGui.EndGroup();

            ImGui.Spacing();

            // Character name
            string name = $"Designs for {character.Name}";
            ImGui.TextUnformatted(name);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(name);

            ImGui.Spacing();
        }

        private void DrawClassicFolderCreationPopup(Character character, float scale)
{
            if (ImGui.BeginPopup("CreateFolderPopup"))
            {
                ImGui.Text("New Folder Name:");
                ImGui.SetNextItemWidth(200 * scale);
                ImGui.InputText("##NewFolder", ref newFolderName, 100);

                ImGui.Spacing();
                ImGui.Text("Folder Color:");

                // Colour selection
                var quickColors = new[]
                {
                    (Vector3?)null, // Auto
                    new Vector3(0.8f, 0.2f, 0.2f), // Red
                    new Vector3(0.3f, 0.8f, 0.3f), // Green
                    new Vector3(0.3f, 0.5f, 0.9f), // Blue
                    new Vector3(0.7f, 0.3f, 0.9f)  // Purple
                };

                float colorButtonSize = 30f * scale;
                for (int i = 0; i < quickColors.Length; i++)
                {
                    var color = quickColors[i];
                    bool isSelected = (newFolderSelectedColor == null && color == null) ||
                                     (newFolderSelectedColor != null && color != null &&
                                      Vector3.Distance(newFolderSelectedColor.Value, color.Value) < 0.1f);

                    if (i > 0) ImGui.SameLine();

                    Vector4 buttonColor = color.HasValue
                        ? new Vector4(color.Value.X, color.Value.Y, color.Value.Z, 1.0f)
                        : new Vector4(0.5f, 0.5f, 0.5f, 1.0f);

                    ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(buttonColor.X * 1.2f, buttonColor.Y * 1.2f, buttonColor.Z * 1.2f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(buttonColor.X * 0.8f, buttonColor.Y * 0.8f, buttonColor.Z * 0.8f, 1.0f));

                    if (isSelected)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1, 1, 1, 1));
                        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 3f * scale);
                    }

                    if (ImGui.Button($"##Color{i}", new Vector2(colorButtonSize, colorButtonSize)))
                    {
                        newFolderSelectedColor = color;
                    }

                    if (isSelected)
                    {
                        ImGui.PopStyleVar();
                        ImGui.PopStyleColor();
                    }

                    ImGui.PopStyleColor(3);
                }

                ImGui.Separator();

                float buttonWidth = 60f * scale;
                if (ImGui.Button("Create", new Vector2(buttonWidth, 0)))
                {
                    var folder = new DesignFolder(newFolderName, Guid.NewGuid())
                    {
                        ParentFolderId = null,
                        SortOrder = character.DesignFolders.Count,
                        CustomColor = newFolderSelectedColor
                    };
                    character.DesignFolders.Add(folder);
                    plugin.AchievementTracker?.OnDesignFolderCreated();
                    plugin.SaveConfiguration();
                    plugin.RefreshTreeItems(character);
                    newFolderName = "";
                    newFolderSelectedColor = null;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
                {
                    newFolderName = "";
                    newFolderSelectedColor = null;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }

        private void DrawClassicDesignForm(Character character, float scale)
{
            float formHeight = 320f * scale;
            ImGui.BeginChild("EditDesignForm", new Vector2(0, formHeight), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize);

            bool isNewDesignForm = string.IsNullOrEmpty(editedDesignName);
            ImGui.Text(isNewDesignForm ? "Add Design" : "Edit Design");

            float inputWidth = Math.Max(150f * scale, ImGui.GetContentRegionAvail().X - (50f * scale));

            // Design Name
            ImGui.Text("Design Name*");
            ImGui.SetCursorPosX(10 * scale);
            ImGui.SetNextItemWidth(inputWidth);
            if (ImGui.InputText("##DesignName", ref editedDesignName, 100))
            {
                plugin.EditedDesignName = editedDesignName;
            }
            plugin.DesignNameFieldPos = ImGui.GetItemRectMin();
            plugin.DesignNameFieldSize = ImGui.GetItemRectSize();

            ImGui.Separator();

            DrawClassicGlamourerField(character, inputWidth, scale);

            if (plugin.Configuration.EnableAutomations)
            {
                DrawClassicAutomationField(inputWidth, scale);
            }

            DrawClassicCustomizeField(inputWidth, scale);

            if (plugin.Configuration.EnableGearsetAssignments)
            {
                DrawClassicGearsetField(inputWidth, scale);
            }

            DrawClassicPreviewImageField(scale);

            // Mod Manager (Conflict Resolution)
            if (plugin.Configuration.EnableConflictResolution)
            {
                bool crDesignNameValid = !string.IsNullOrWhiteSpace(editedDesignName);

                if (!crDesignNameValid)
                    ImGui.BeginDisabled();
                bool crCheckboxClicked = ImGui.Checkbox("Use Conflict Resolution", ref isSecretDesignMode);
                if (!crDesignNameValid)
                    ImGui.EndDisabled();

                if (crCheckboxClicked)
                {
                    if (!isAdvancedModeDesign)
                    {
                        editedDesignMacro = (isSecretDesignMode && !plugin.Configuration.EnableConflictResolution)
                            ? GenerateSecretDesignMacro(character)
                            : GenerateDesignMacro(character);
                    }

                    if (isSecretDesignMode && crDesignNameValid)
                        PerformQuickGearHairUpdate(character);
                }
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.SetTooltip(crDesignNameValid
                        ? "Manual mod state for this design."
                        : "Enter a Design Name first.");
                }

                if (isSecretDesignMode)
                {
                    DrawClassicSecretModeDesignField(character, scale);
                }
                ImGui.Separator();
            }

            ImGui.Separator();

            DrawClassicAdvancedModeToggle(scale);

            ImGui.Separator();

            DrawClassicFormActionButtons(character, scale);

            ImGui.EndChild();
        }

        private void DrawClassicGlamourerField(Character character, float inputWidth, float scale)
{
            ImGui.Text(plugin.Configuration.EnableAutomations ? "Glamourer Design" : "Glamourer Design*");

            // Tooltip
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300 * scale);
                ImGui.TextUnformatted("Select the Glamourer design for this outfit. Right-click to clear.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            ImGui.SetCursorPosX(10 * scale);
            var glamourerOptions = plugin.IntegrationListProvider?.GetGlamourerDesigns() ?? Array.Empty<string>();
            var currentGlamourer = plugin.IntegrationListProvider?.GetCurrentGlamourerDesign();

            if (AutocompleteCombo.Draw("##GlamourerDesign", ref editedGlamourerDesign, glamourerOptions, inputWidth, "Select design...", currentActive: currentGlamourer))
            {
                plugin.EditedGlamourerDesign = editedGlamourerDesign;

                if (!isAdvancedModeDesign)
                {
                    // If Conflict Resolution is ON, always use regular macro
                    // If Conflict Resolution is OFF, use bulktag macro only if user has configured mods
                    editedDesignMacro = (!plugin.Configuration.EnableConflictResolution && isSecretDesignMode)
                        ? GenerateSecretDesignMacro(character)
                        : GenerateDesignMacro(character);
                }
                else
                {
                    UpdateAdvancedMacroGlamourerFixed(editedGlamourerDesign);
                }
            }
            plugin.DesignGlamourerFieldPos = ImGui.GetItemRectMin();
            plugin.DesignGlamourerFieldSize = ImGui.GetItemRectSize();
        }

        private void DrawClassicAutomationField(float inputWidth, float scale)
{
            ImGui.Text("Glamourer Automation");

            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300 * scale);
                ImGui.TextUnformatted("Optional: Enter the name of a Glamourer automation for this design.\n⚠️ Must match the automation name EXACTLY as shown in Glamourer.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            ImGui.SetCursorPosX(10 * scale);
            // Glamourer doesn't expose an IPC to get automation names, so use plain text input
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.InputText("##GlamourerAutomation", ref editedAutomation, 100);
        }

        private void DrawClassicCustomizeField(float inputWidth, float scale)
{
            ImGui.Text("Customize+ Profile");

            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300 * scale);
                ImGui.TextUnformatted("Optional: Select a Customize+ profile for this design. Right-click to clear.\nIf left blank, uses the character's profile or disables all profiles.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            ImGui.SetCursorPosX(10 * scale);
            var customizeOptions = plugin.IntegrationListProvider?.GetCustomizePlusProfiles() ?? Array.Empty<string>();
            var currentCustomize = plugin.IntegrationListProvider?.GetCurrentCustomizePlusProfile();

            if (AutocompleteCombo.Draw("##CustomizePlus", ref editedCustomizeProfile, customizeOptions, inputWidth, "Select profile...", currentActive: currentCustomize))
            {
                // Update macro
                if (!isAdvancedModeDesign)
                {
                    editedDesignMacro = (isSecretDesignMode && !plugin.Configuration.EnableConflictResolution)
                        ? GenerateSecretDesignMacro(plugin.Characters[activeCharacterIndex])
                        : GenerateDesignMacro(plugin.Characters[activeCharacterIndex]);
                }
                else
                {
                    UpdateAdvancedMacroCustomize();
                }
            }
        }

        private void DrawClassicGearsetField(float inputWidth, float scale)
{
            ImGui.Text("Assigned Gearset");

            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300 * scale);
                ImGui.TextUnformatted("Optional: Automatically switch to this gearset when applying this design.\nChoose 'None' to use the character's setting or not change gearsets.\nDesign setting overrides character setting.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            ImGui.SetCursorPosX(10 * scale);
            ImGui.SetNextItemWidth(inputWidth);

            // Get available gearsets
            var gearsets = plugin.GetPlayerGearsets();

            // Build display text for current selection
            string currentDisplay = "None (use character setting)";
            if (editedGearset.HasValue)
            {
                var matchingGearset = gearsets.FirstOrDefault(g => g.Number == editedGearset.Value);
                if (matchingGearset.Number > 0)
                {
                    currentDisplay = plugin.GetGearsetDisplayName(matchingGearset.Number, matchingGearset.JobId, matchingGearset.Name);
                }
                else
                {
                    currentDisplay = $"Gearset {editedGearset.Value}";
                }
            }

            if (ImGui.BeginCombo("##AssignedGearset", currentDisplay))
            {
                // "None" option
                if (ImGui.Selectable("None (use character setting)", !editedGearset.HasValue))
                {
                    editedGearset = null;
                }
                if (!editedGearset.HasValue)
                    ImGui.SetItemDefaultFocus();

                // Gearset options
                foreach (var gearset in gearsets)
                {
                    string displayName = plugin.GetGearsetDisplayName(gearset.Number, gearset.JobId, gearset.Name);
                    bool isSelected = editedGearset.HasValue && editedGearset.Value == gearset.Number;

                    if (ImGui.Selectable(displayName, isSelected))
                    {
                        editedGearset = gearset.Number;
                    }
                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }
        }

        private void DrawClassicPreviewImageField(float scale)
{
            ImGui.Text("Preview Image (Optional)");

            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300 * scale);
                ImGui.TextUnformatted("Optional: Choose an image to show when hovering over this design.\nThis helps you quickly identify designs at a glance.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            ImGui.SetCursorPosX(10 * scale);
            if (ImGui.Button("Browse..."))
            {
                SelectPreviewImage();
            }

            // Add Paste button
            ImGui.SameLine();
            bool clipboardHasImage = IsClipboardImageAvailable();
            
            if (!clipboardHasImage)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
            }
            
            if (ImGui.Button("Paste"))
            {
                if (clipboardHasImage)
                {
                    PasteImageFromClipboard();
                }
            }
            
            if (!clipboardHasImage)
            {
                ImGui.PopStyleVar();
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                if (clipboardHasImage)
                {
                    ImGui.Text("Paste image from clipboard");
                }
                else
                {
                    ImGui.Text("No image in clipboard\nCopy a screenshot first (Win+Shift+S)");
                }
                ImGui.EndTooltip();
            }

            // Add Clear button
            ImGui.SameLine();
            if (ImGui.Button("Clear") && !string.IsNullOrEmpty(editedDesignPreviewPath))
            {
                editedDesignPreviewPath = "";
            }

            // Apply pending image path from file picker
            if (pendingDesignImagePath != null)
            {
                lock (this)
                {
                    editedDesignPreviewPath = pendingDesignImagePath;
                    pendingDesignImagePath = null;
                }
            }

            // Apply pending pasted image path
            if (pendingPastedImagePath != null)
            {
                lock (this)
                {
                    editedDesignPreviewPath = pendingPastedImagePath;
                    pendingPastedImagePath = null;
                }
            }

            // Show current preview
            if (!string.IsNullOrEmpty(editedDesignPreviewPath) && File.Exists(editedDesignPreviewPath))
            {
                var texture = Plugin.TextureProvider.GetFromFile(editedDesignPreviewPath).GetWrapOrDefault();
                if (texture != null)
                {
                    float maxSize = 100f * scale;
                    var (width, height) = CalculateImageDimensions(texture, maxSize);
                    ImGui.Image((ImTextureID)texture.Handle, new Vector2(width, height));
                }
            }
            else if (!string.IsNullOrEmpty(editedDesignPreviewPath))
            {
                ImGui.Text("Preview: " + Path.GetFileName(editedDesignPreviewPath));
            }
        }

        private void DrawClassicSecretModeDesignField(Character character, float scale)
{
            ImGui.Text("Mod Manager");
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300 * scale);
                ImGui.TextUnformatted("Select which mods to enable and configure their options for this design.\nAllows different designs to use different mod combinations and settings.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            ImGui.SetCursorPosX(10 * scale);

            // Get mod count for button text
            int selectedModCount = 0;
            Dictionary<string, bool> modState = null;
            HashSet<string> pinOverrides = null;

            if (isNewDesign)
            {
                // For new designs, use temporary state
                modState = temporaryDesignSecretModState ?? new Dictionary<string, bool>();
                pinOverrides = temporaryDesignSecretModPinOverrides ?? new HashSet<string>();
                selectedModCount = modState.Count(kvp => kvp.Value);
            }
            else if (!string.IsNullOrEmpty(originalDesignName))
            {
                // For existing designs, use the design's state
                var currentDesign = character.Designs.FirstOrDefault(d => d.Name == originalDesignName);
                if (currentDesign != null)
                {
                    modState = currentDesign.SecretModState ?? new Dictionary<string, bool>();
                    pinOverrides = currentDesign.SecretModPinOverrides ?? new HashSet<string>();
                    selectedModCount = modState.Count(kvp => kvp.Value);
                }
            }

            string buttonText = selectedModCount > 0 
                ? $"Configure Mods ({selectedModCount} selected)"
                : "Configure Mods";

            // Validate that design name is filled before opening mod manager
            bool hasValidDesignName = !string.IsNullOrWhiteSpace(editedDesignName);
            
            if (!hasValidDesignName)
                ImGui.BeginDisabled();
            
            if (ImGui.Button(buttonText))
            {
                if (hasValidDesignName)
                {
                    // Open Secret Mode mod window for this design
                    var currentDesignForWindow = isNewDesign ? null : character.Designs.FirstOrDefault(d => d.Name == originalDesignName);
                    plugin.SecretModeModWindow.Open(
                        activeCharacterIndex,
                        modState,
                        LogAndReturnPins(character),
                        (newModState) =>
                        {
                            // Save callback for design-level mod state
                            if (isNewDesign)
                            {
                                // For new designs, store temporarily
                                temporaryDesignSecretModState = newModState;
                            }
                            else if (!string.IsNullOrEmpty(originalDesignName))
                            {
                                // For existing designs, save directly AND update temporary state
                                var design = character.Designs.FirstOrDefault(d => d.Name == originalDesignName);
                                if (design != null)
                                {
                                    design.SecretModState = newModState;
                                    temporaryDesignSecretModState = newModState; // Keep temp state in sync
                                    plugin.SaveConfiguration();
                                }
                            }
                        },
                        (pins) =>
                        {
                            // Character pin callback
                            Plugin.Log.Information($"[PIN DEBUG] Design save callback: saving {pins?.Count ?? 0} pins to character");
                            character.SecretModPins = pins?.ToList();
                            plugin.SaveConfiguration();
                        },
                        currentDesignForWindow,  // Pass the design context
                        character.Name,  // Pass the character name for context
                        (inheritMods) =>
                        {
                            // Inherit callback - restore Penumbra inheritance for these mods
                            if (inheritMods != null && inheritMods.Count > 0)
                            {
                                _ = plugin.RestoreModInheritance(inheritMods);
                            }
                        }
                    );
                }
            }
            
            // Quick update button for gear/hair changes
            ImGui.SameLine();
            
            ImGui.PushFont(UiBuilder.IconFont);
            
            bool canQuickUpdate = hasValidDesignName && plugin.Configuration.EnableConflictResolution;
            
            if (!canQuickUpdate)
                ImGui.BeginDisabled();
            
            if (ImGui.Button("\uf2f1")) // Import icon - suggests pulling in current state
            {
                if (canQuickUpdate)
                {
                    PerformQuickGearHairUpdate(character);
                }
            }
            
            if (!canQuickUpdate)
                ImGui.EndDisabled();
            
            ImGui.PopFont();
            
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                if (canQuickUpdate)
                {
                    ImGui.Text("Update gear/hair changes");
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.7f, 0.7f, 1.0f));
                    if (!hasValidDesignName)
                        ImGui.Text("Enter a Design Name first");
                    else if (!plugin.Configuration.EnableConflictResolution)
                        ImGui.Text("Conflict Resolution must be enabled");
                    ImGui.PopStyleColor();
                }
                ImGui.EndTooltip();
            }
            
            if (!hasValidDesignName)
            {
                ImGui.EndDisabled();
                
                // Show tooltip explaining why the button is disabled
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.BeginTooltip();
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.7f, 0.7f, 1.0f));
                    ImGui.Text("Please enter a Design Name before configuring mods.");
                    ImGui.PopStyleColor();
                    ImGui.EndTooltip();
                }
            }
        }

        private void DrawClassicAdvancedModeToggle(float scale)
{
            if (ImGui.Button(isAdvancedModeDesign ? "Exit Advanced Mode" : "Advanced Mode"))
            {
                isAdvancedModeDesign = !isAdvancedModeDesign;
                isAdvancedModeWindowOpen = isAdvancedModeDesign;

                if (isAdvancedModeDesign)
                {
                    // Load existing advanced macro if available, otherwise generate one
                    if (activeCharacterIndex >= 0 && activeCharacterIndex < plugin.Characters.Count && !isNewDesign)
                    {
                        var character = plugin.Characters[activeCharacterIndex];
                        var existingDesign = character.Designs.FirstOrDefault(d => d.Name == originalDesignName);
                        if (existingDesign != null && !string.IsNullOrEmpty(existingDesign.AdvancedMacro))
                        {
                            advancedDesignMacroText = existingDesign.AdvancedMacro;
                        }
                        else
                        {
                            advancedDesignMacroText = EnsureProperDesignMacroStructure();
                        }
                    }
                    else
                    {
                        advancedDesignMacroText = EnsureProperDesignMacroStructure();
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
                ImGui.TextUnformatted("⚠️ Do not touch this unless you know what you're doing.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
        }

        private void DrawClassicFormActionButtons(Character character, float scale)
{
            float buttonWidth = 85 * scale;
            float buttonHeight = 20 * scale;
            float buttonSpacing = 8 * scale;
            float totalButtonWidth = (buttonWidth * 2 + buttonSpacing);
            float availableWidth = ImGui.GetContentRegionAvail().X;
            float buttonPosX = (availableWidth > totalButtonWidth) ? (availableWidth - totalButtonWidth) / 2f : 0;

            ImGui.SetCursorPosX(buttonPosX);

            bool canSave = !string.IsNullOrWhiteSpace(editedDesignName)
                && (!string.IsNullOrWhiteSpace(editedGlamourerDesign)
                    || (plugin.Configuration.EnableAutomations && !string.IsNullOrWhiteSpace(editedAutomation)));

            // Center text in buttons
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4 * scale, 4 * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));

            // Save button styling
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.5f, 0.3f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.6f, 0.4f, 1.0f));

            if (!canSave)
                ImGui.BeginDisabled();

            if (ImGui.Button("Save Design", new Vector2(buttonWidth, 0)))
            {
                SaveDesign(character);
                CloseDesignEditor();
            }
            plugin.SaveDesignButtonPos = ImGui.GetItemRectMin();
            plugin.SaveDesignButtonSize = ImGui.GetItemRectSize();

            if (!canSave)
                ImGui.EndDisabled();

            ImGui.PopStyleColor(3);

            ImGui.SameLine();

            // Cancel button styling
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.2f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.3f, 0.3f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.4f, 0.4f, 1.0f));

            if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
            {
                CloseDesignEditor();
            }

            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar(2);
        }

        private void DrawClassicSortingControls(Character character, float scale)
{
            ImGui.Text("Sort Designs By:");
            ImGui.SameLine();

            float comboWidth = Math.Max(120f * scale, ImGui.GetContentRegionAvail().X - (20f * scale));
            ImGui.SetNextItemWidth(comboWidth);

            if (ImGui.BeginCombo("##DesignSortDropdown", currentDesignSort.ToString()))
            {
                if (ImGui.Selectable("Favourites", currentDesignSort == DesignSortType.Favorites))
                {
                    SetDesignSort(0); // Favorites
                    SortDesigns(character);
                }
                if (ImGui.Selectable("Alphabetical", currentDesignSort == DesignSortType.Alphabetical))
                {
                    SetDesignSort(1); // Alphabetical
                    SortDesigns(character);
                }
                if (ImGui.Selectable("Newest", currentDesignSort == DesignSortType.Recent))
                {
                    SetDesignSort(2); // Recent
                    SortDesigns(character);
                }
                if (ImGui.Selectable("Oldest", currentDesignSort == DesignSortType.Oldest))
                {
                    SetDesignSort(3); // Oldest
                    SortDesigns(character);
                }
                if (ImGui.Selectable("Manual", currentDesignSort == DesignSortType.Manual))
                {
                    SetDesignSort(4); // Manual
                }
                ImGui.EndCombo();
            }
            
            // Search input field
            if (showSearchBar)
            {
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.InputTextWithHint("##SearchDesigns", "Search designs...", ref searchQuery, 100))
                {
                    ClassicInvalidateFilterCache();
                }
            }
        }

        private void DrawClassicDesignList(Character character, float scale)
{
            float remainingHeight = ImGui.GetContentRegionAvail().Y;

            // Minimum height
            remainingHeight = Math.Max(remainingHeight, 100f * scale);

            ImGui.BeginChild("DesignListBackground", new Vector2(0, remainingHeight), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);

            // Build unified list of folders and designs
            var renderItems = BuildRenderItems(character);

            // Render each item
            bool anyRowHovered = false;
            bool anyHeaderHovered = false;

            foreach (var entry in renderItems)
            {
                if (entry.isFolder)
                {
                    var folder = (DesignFolder)entry.item;
                    bool folderWasHovered = false;
                    DrawClassicFolderItem(character, folder, ref folderWasHovered, scale);
                    if (folderWasHovered) anyHeaderHovered = true;
                }
                else
                {
                    var design = (CharacterDesign)entry.item;
                    DrawClassicDesignRow(character, design, false, scale);
                    if (ImGui.IsItemHovered()) anyRowHovered = true;
                }
            }

            // Handle dropping outside any header
            ClassicHandleDropToRoot(anyHeaderHovered, anyRowHovered, character);

            ImGui.EndChild();
        }

        private void DrawClassicFolderItem(Character character, DesignFolder folder, ref bool wasHovered, float scale)
{
            bool isRenaming = isRenamingFolder && folder.Id == renameFolderId;
            bool open = false;

            // Get folder colour
            var folderColor = GetFolderColor(character, folder);

            if (isRenaming)
            {
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.2f, 0.2f, 0.2f, 1f));
                ImGui.SetNextItemWidth(200 * scale);
                if (ImGui.InputText("##InlineRename", ref renameFolderBuf, 128, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    folder.Name = renameFolderBuf;
                    isRenamingFolder = false;
                    plugin.SaveConfiguration();
                    plugin.RefreshTreeItems(character);
                }
                ImGui.PopStyleColor();
            }
            else
            {
                // Style the folder header with custom colour
                ImGui.PushStyleColor(ImGuiCol.Header, folderColor);
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(folderColor.X * 1.2f, folderColor.Y * 1.2f, folderColor.Z * 1.2f, folderColor.W));
                ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(folderColor.X * 1.4f, folderColor.Y * 1.4f, folderColor.Z * 1.4f, folderColor.W));

                open = ImGui.CollapsingHeader($"{folder.Name}##F{folder.Id}", ImGuiTreeNodeFlags.SpanFullWidth);

                ImGui.PopStyleColor(3);

                // Drag source
                if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceAllowNullId))
                {
                    draggedFolder = folder;
                    ImGui.SetDragDropPayload("FOLDER_MOVE", ReadOnlySpan<byte>.Empty, ImGuiCond.None);
                    ImGui.TextUnformatted($"Moving Folder: {folder.Name}");
                    ImGui.EndDragDropSource();
                }

                // Context menu
                DrawClassicFolderContextMenu(character, folder, scale);
            }

            // Handle hover and drop logic
            var hdrMin = ImGui.GetItemRectMin();
            var hdrMax = ImGui.GetItemRectMax();
            bool overHeader = ImGui.IsMouseHoveringRect(hdrMin, hdrMax, true);
            wasHovered = overHeader;

            if ((draggedDesign != null || draggedFolder != null) && overHeader)
            {
                var dl = ImGui.GetWindowDrawList();
                uint col = ImGui.GetColorU32(new Vector4(0.3f, 0.5f, 1f, 1f));
                dl.AddRect(hdrMin, hdrMax, col, 0, ImDrawFlags.None, 2 * scale);
            }

            // Drop handling
            if (overHeader && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                if (draggedDesign != null)
                {
                    draggedDesign.FolderId = folder.Id;
                    plugin.SaveConfiguration();
                    plugin.RefreshTreeItems(character);
                    draggedDesign = null;
                }
                else if (draggedFolder != null && draggedFolder != folder)
                {
                    draggedFolder.ParentFolderId = folder.Id;
                    plugin.SaveConfiguration();
                    plugin.RefreshTreeItems(character);
                    draggedFolder = null;
                }
            }

            // Draw folder content
            if (open)
            {
                DrawClassicFolderContents(character, folder, scale);
            }
        }

        private void DrawClassicFolderContextMenu(Character character, DesignFolder folder, float scale)
{
            if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                ImGui.OpenPopup($"FolderCtx{folder.Id}");

            if (ImGui.BeginPopup($"FolderCtx{folder.Id}"))
            {
                if (ImGui.MenuItem("Rename Folder"))
                {
                    renameFolderId = folder.Id;
                    renameFolderBuf = folder.Name;
                    isRenamingFolder = true;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.Separator();

                // Folder colour menu
                if (ImGui.BeginMenu("Folder Colour"))
                {
                    // Auto colour option
                    if (ImGui.MenuItem("Auto Colour", "", folder.CustomColor == null))
                    {
                        folder.CustomColor = null;
                        plugin.SaveConfiguration();
                    }

                    ImGui.Separator();

                    // Preset colours
                    var presetColors = new[]
                    {
                        ("Red", new Vector3(0.8f, 0.2f, 0.2f)),
                        ("Green", new Vector3(0.3f, 0.8f, 0.3f)),
                        ("Blue", new Vector3(0.3f, 0.5f, 0.9f)),
                        ("Yellow", new Vector3(0.9f, 0.8f, 0.2f)),
                        ("Purple", new Vector3(0.7f, 0.3f, 0.9f)),
                        ("Orange", new Vector3(1.0f, 0.6f, 0.2f)),
                        ("Pink", new Vector3(0.9f, 0.4f, 0.7f)),
                        ("Cyan", new Vector3(0.3f, 0.8f, 0.8f))
                    };

                    foreach (var (colorName, color) in presetColors)
                    {
                        bool isSelected = folder.CustomColor.HasValue &&
                            Vector3.Distance(folder.CustomColor.Value, color) < 0.1f;

                        if (ImGui.MenuItem(colorName, "", isSelected))
                        {
                            folder.CustomColor = color;
                            plugin.SaveConfiguration();
                        }
                    }

                    ImGui.Separator();

                    // Custom colour picker
                    ImGui.Text("Custom Colour:");
                    Vector3 tempColor = folder.CustomColor ?? GetAutoGeneratedColor(character, folder);

                    if (ImGui.ColorEdit3("##CustomFolderColour", ref tempColor, ImGuiColorEditFlags.NoInputs))
                    {
                        folder.CustomColor = tempColor;
                        plugin.SaveConfiguration();
                    }

                    ImGui.EndMenu();
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Delete Folder"))
                {
                    DeleteFolder(character, folder);
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
        }

        private void DrawClassicFolderContents(Character character, DesignFolder folder, float scale)
{
            float indentAmount = 15f * scale;

            // Apply search filter
            var foldersToShow = character.DesignFolders
                     .Where(f => f.ParentFolderId == folder.Id);
            var designsToShow = character.Designs
                     .Where(d => d.FolderId == folder.Id);
                     
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                foldersToShow = foldersToShow.Where(f => FolderContainsMatchingDesigns(character, f));
                designsToShow = designsToShow.Where(d => MatchesSearchQuery(d));
            }

            // Child folders
            foreach (var child in foldersToShow.OrderBy(f => f.SortOrder))
            {
                ImGui.Indent(indentAmount);
                bool childWasHovered = false;
                DrawClassicFolderItem(character, child, ref childWasHovered, scale);
                ImGui.Unindent(indentAmount);
            }

            foreach (var design in designsToShow.OrderBy(d => d.SortOrder))
            {
                ImGui.Indent(indentAmount);
                DrawClassicDesignRow(character, design, true, scale);
                ImGui.Unindent(indentAmount);
            }

            // Visual separation
            ImGui.Spacing();
            ImGui.Separator();
        }

        private void DrawClassicDesignRow(Character character, CharacterDesign design, bool isInsideFolder, float scale)
{
            ImGui.PushID(design.Name);

            var rowMin = ImGui.GetCursorScreenPos();
            float rowW = ImGui.GetContentRegionAvail().X;
            float rowH = 32f * scale;
            ImGui.Dummy(new Vector2(rowW, rowH));
            var rowMax = rowMin + new Vector2(rowW, rowH);

            bool hovered = ImGui.IsMouseHoveringRect(rowMin, rowMax, true);

            // Dark row background
            if (hovered)
            {
                var hoverColor = ImGui.GetColorU32(new Vector4(0.25f, 0.25f, 0.25f, 0.8f));
                ImGui.GetWindowDrawList().AddRectFilled(rowMin, rowMax, hoverColor, 4f * scale);
            }

            // Draw design row content with compact styling, america's next top model has nothing on me now!
            DrawClassicDesignRowContent(character, design, rowMin, rowMax, rowH, hovered, rowW, scale);

            // Handle drag and drop
            ClassicHandleDesignDragDrop(character, design, rowMin, rowMax, hovered, scale);

            ImGui.PopID();
            ImGui.SetCursorScreenPos(new Vector2(rowMin.X, rowMin.Y + rowH));

            // Subtle separator
            if (!isInsideFolder)
            {
                var separatorColor = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 0.5f));
                ImGui.GetWindowDrawList().AddLine(
                    new Vector2(rowMin.X + (10 * scale), rowMax.Y),
                    new Vector2(rowMax.X - (10 * scale), rowMax.Y),
                    separatorColor, 1f * scale
                );
            }
        }

        private void DrawClassicDesignRowContent(Character character, CharacterDesign design, Vector2 rowMin, Vector2 rowMax, float rowH, bool hovered, float rowW, float scale)
{
            float pad = 8f * scale;
            float spacing = 4f * scale;
            float btnSize = 24f * scale;
            float x = rowMin.X + (2f * scale);

            // Drag handle
            if (hovered)
            {
                float handleWidth = 12f * scale;
                float handleHeight = rowH * 0.6f;
                float yOff = (rowH - handleHeight) / 2;

                ImGui.SetCursorScreenPos(new Vector2(x + pad, rowMin.Y + yOff));

                var handleColor = new Vector4(character.NameplateColor.X, character.NameplateColor.Y, character.NameplateColor.Z, 0.8f);

                ImGui.PushStyleColor(ImGuiCol.Button, handleColor);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, handleColor);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, handleColor);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2f * scale);

                ImGui.Button($"##handle_{design.Name}", new Vector2(handleWidth, handleHeight));

                ImGui.PopStyleVar();
                ImGui.PopStyleColor(3);

                // Enable drag and drop
                if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left) &&
                    ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceAllowNullId))
                {
                    draggedDesign = design;
                    ImGui.SetDragDropPayload("DESIGN_MOVE", ReadOnlySpan<byte>.Empty, ImGuiCond.None);

                    // Ghost image
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.1f, 0.1f, 0.1f, 0.9f));
                    ImGui.BeginGroup();
                    ImGui.Text("📄");
                    ImGui.SameLine();
                    ImGui.Text(design.Name);
                    ImGui.EndGroup();
                    ImGui.PopStyleColor(2);
                    ImGui.EndDragDropSource();
                }

                x += handleWidth + spacing;
            }

            // Favourite star/ghost
            ImGui.SetCursorScreenPos(new Vector2(x, rowMin.Y + (rowH - btnSize) / 2));
            
            // Check for seasonal themes
            var effectiveTheme = SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration)
                ? SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration)
                : SeasonalTheme.Default;

            string star;
            bool usesFontAwesome = false;

            if (effectiveTheme == SeasonalTheme.Halloween)
            {
                star = "\uf6e2"; // Ghost icon for Halloween
                usesFontAwesome = true;
            }
            else if (effectiveTheme == SeasonalTheme.Winter || effectiveTheme == SeasonalTheme.Christmas)
            {
                star = "\uf2dc"; // Snowflake icon for Winter/Christmas
                usesFontAwesome = true;
            }
            else if (effectiveTheme == SeasonalTheme.Valentines)
            {
                star = "\uf004"; // Heart icon for Valentine's
                usesFontAwesome = true;
            }
            else
            {
                star = design.IsFavorite ? "★" : "☆"; // Normal stars
                usesFontAwesome = false;
            }

            Vector4 starColor;
            if (effectiveTheme == SeasonalTheme.Halloween)
            {
                var themeColors = SeasonalThemeManager.GetCurrentThemeColors(plugin.Configuration);
                starColor = design.IsFavorite
                    ? new Vector4(themeColors.PrimaryAccent.X, themeColors.PrimaryAccent.Y, themeColors.PrimaryAccent.Z, hovered ? 1f : 0.7f) // Orange
                    : new Vector4(1.0f, 1.0f, 1.0f, hovered ? 0.8f : 0.6f); // White
            }
            else if (effectiveTheme == SeasonalTheme.Winter || effectiveTheme == SeasonalTheme.Christmas)
            {
                starColor = design.IsFavorite
                    ? new Vector4(1.0f, 1.0f, 1.0f, hovered ? 1f : 0.8f) // Pure white for favourited snowflake
                    : new Vector4(0.7f, 0.7f, 0.8f, hovered ? 0.8f : 0.5f); // Light grey for unfavourited
            }
            else if (effectiveTheme == SeasonalTheme.Valentines)
            {
                starColor = design.IsFavorite
                    ? new Vector4(1.0f, 1.0f, 1.0f, hovered ? 1f : 0.9f) // Solid white for favourited heart
                    : new Vector4(0.7f, 0.5f, 0.55f, hovered ? 0.7f : 0.4f); // Muted for unfavourited
            }
            else
            {
                starColor = design.IsFavorite
                    ? new Vector4(1f, 0.8f, 0.2f, hovered ? 1f : 0.7f) // Gold for normal favourites
                    : new Vector4(0.5f, 0.5f, 0.5f, hovered ? 0.8f : 0.4f); // Grey for normal unfavourited
            }

            // Ensure proper icon centering with explicit alignment
            bool scaleDownIcon = effectiveTheme == SeasonalTheme.Valentines; // Heart needs to be smaller
            if (scaleDownIcon)
            {
                ImGui.SetWindowFontScale(0.85f);
            }
            if (usesFontAwesome)
            {
                ImGui.PushFont(UiBuilder.IconFont);
            }

            ImGui.PushStyleColor(ImGuiCol.Text, starColor);
            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f)); // CENTER ICON

            bool buttonClicked = ImGui.Button($"{star}##{design.Name}", new Vector2(btnSize, btnSize));

            ImGui.PopStyleVar();
            ImGui.PopStyleColor();

            if (usesFontAwesome)
            {
                ImGui.PopFont();
            }
            if (scaleDownIcon)
            {
                ImGui.SetWindowFontScale(1.0f);
            }
            
            if (buttonClicked)
            {
                bool wasFavorite = design.IsFavorite;
                design.IsFavorite = !design.IsFavorite;

                // Trigger particle effect
                Vector2 effectPos = ImGui.GetItemRectMin() + ImGui.GetItemRectSize() / 2;
                string effectKey = $"{character.Name}_{design.Name}";
                if (!designFavoriteEffects.ContainsKey(effectKey))
                    designFavoriteEffects[effectKey] = new FavoriteSparkEffect();
                designFavoriteEffects[effectKey].Trigger(effectPos, design.IsFavorite, plugin.Configuration);

                plugin.SaveConfiguration();
                SortDesigns(character);
            }
            
            // Add tooltip for all favourite buttons
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(design.IsFavorite ? "Remove from favourites" : "Add to favourites");
            }

            x += btnSize + spacing;

            // Design name styling
            float rightZone = hovered ? (3 * btnSize + 2 * spacing + pad) : 0; // Only show buttons on hover
            float availW = rowW - (x - rowMin.X) - rightZone - pad;

            ImGui.SetCursorScreenPos(new Vector2(x, rowMin.Y + (rowH - ImGui.GetTextLineHeight()) / 2));

            var name = design.Name;
            if (ImGui.CalcTextSize(name).X > availW)
                name = TruncateWithEllipsis(name, availW);

            // Design name
            bool isActive = IsDesignCurrentlyActive(character, design);
            var textColor = isActive ? new Vector4(0.2f, 0.9f, 0.2f, 1f) : new Vector4(0.9f, 0.9f, 0.9f, 1f); // Green for active, light grey for inactive
            ImGui.PushStyleColor(ImGuiCol.Text, textColor);
            ImGui.TextUnformatted(name);
            ImGui.PopStyleColor();

            // Action buttons (only when hovered, compact)
            if (hovered)
            {
                DrawClassicCompactDesignActionButtons(character, design, rowMin, rowW, rowH, btnSize, spacing, pad, scale);
            }
        }

        private void DrawClassicCompactDesignActionButtons(Character character, CharacterDesign design, Vector2 rowMin, float rowW, float rowH, float btnSize, float spacing, float pad, float scale)
{
            // Position buttons
            float startX = rowMin.X + rowW - (3 * btnSize + 2 * spacing + pad);
            float buttonY = rowMin.Y + (rowH - btnSize) / 2;

            // Dark button styling
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.25f, 0.25f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.35f, 0.35f, 0.35f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f * scale);

            // Apply button
            ImGui.SetCursorScreenPos(new Vector2(startX, buttonY));
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 0.8f, 0.3f, 1f)); // Green
            if (ImGui.Button("\uf00c", new Vector2(btnSize, btnSize)))
            {
                // Switch gearset if assigned (design overrides character)
                if (plugin.Configuration.EnableGearsetAssignments)
                {
                    var effectiveGearset = design.AssignedGearset ?? character.AssignedGearset;
                    if (effectiveGearset.HasValue)
                    {
                        plugin.SwitchToGearset(effectiveGearset.Value);
                    }
                }

                // Check if this is a Secret Mode (Conflict Resolution) design
                if (design.SecretModState != null && design.SecretModState.Any())
                {
                    // Ensure the correct Penumbra collection is assigned before CR modifies it
                    if (!string.IsNullOrWhiteSpace(character.PenumbraCollection))
                    {
                        plugin.EnsurePenumbraCollectionAssignment(character.PenumbraCollection);
                    }

                    // Apply mod state asynchronously first, then execute macro with proper threading
                    _ = Task.Run(async () =>
                    {
                        await plugin.ApplyDesignModState(character, design);
                        Plugin.Framework.RunOnFrameworkThread(() => {
                            plugin.ExecuteMacro(design.Macro, character, design.Name);
                            // Track last used design and character for auto-reapplication and UI feedback
                            plugin.Configuration.LastUsedDesignByCharacter[character.Name] = design.Name;
                            plugin.Configuration.LastUsedDesignCharacterKey = character.Name;
                            plugin.Configuration.LastUsedCharacterKey = character.Name;
                            
                            // Update player-specific character tracking for green highlighting
                            if (Plugin.ObjectTable.LocalPlayer is { } player && player.HomeWorld.IsValid)
                            {
                                string localName = player.Name.TextValue;
                                string worldName = player.HomeWorld.Value.Name.ToString();
                                string fullKey = $"{localName}@{worldName}";
                                string pluginCharacterKey = $"{character.Name}@{worldName}";
                                plugin.Configuration.LastUsedCharacterByPlayer[fullKey] = pluginCharacterKey;
                            }
                            
                            plugin.Configuration.Save();
                        });
                    });
                }
                else
                {
                    // Regular design - just execute the macro
                    plugin.ExecuteMacro(design.Macro, character, design.Name);
                    // Track last used design and character for auto-reapplication and UI feedback
                    plugin.Configuration.LastUsedDesignByCharacter[character.Name] = design.Name;
                    plugin.Configuration.LastUsedDesignCharacterKey = character.Name;
                    plugin.Configuration.LastUsedCharacterKey = character.Name;
                    
                    // Update player-specific character tracking for green highlighting
                    if (Plugin.ObjectTable.LocalPlayer is { } player && player.HomeWorld.IsValid)
                    {
                        string localName = player.Name.TextValue;
                        string worldName = player.HomeWorld.Value.Name.ToString();
                        string fullKey = $"{localName}@{worldName}";
                        string pluginCharacterKey = $"{character.Name}@{worldName}";
                        plugin.Configuration.LastUsedCharacterByPlayer[fullKey] = pluginCharacterKey;
                    }
                    
                    plugin.Configuration.Save();
                }

                plugin.AchievementTracker?.OnDesignApplied();
            }
            ImGui.PopStyleColor();
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("Apply Design");

                // Preview image in tooltip
                if (!string.IsNullOrEmpty(design.PreviewImagePath) && File.Exists(design.PreviewImagePath))
                {
                    var texture = Plugin.TextureProvider.GetFromFile(design.PreviewImagePath).GetWrapOrDefault();
                    if (texture != null)
                    {
                        float maxSize = 300f * scale;
                        var (displayWidth, displayHeight) = CalculateImageDimensions(texture, maxSize);
                        ImGui.Image((ImTextureID)texture.Handle, new Vector2(displayWidth, displayHeight));
                    }
                }
                ImGui.EndTooltip();
            }

            // Edit button
            ImGui.SetCursorScreenPos(new Vector2(startX + btnSize + spacing, buttonY));
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 0.7f, 1f, 1f)); // Blue
            if (ImGui.Button("\uf044", new Vector2(btnSize, btnSize)))
            {
                OpenEditDesignWindow(character, design);
            }
            ImGui.PopStyleColor();
            ImGui.PopFont();

            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Edit Design");

            // Delete button
            ImGui.SetCursorScreenPos(new Vector2(startX + 2 * (btnSize + spacing), buttonY));
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f)); // Red
            var io = ImGui.GetIO();
            if (ImGui.Button("\uf2ed", new Vector2(btnSize, btnSize)) && io.KeyCtrl && io.KeyShift)
            {
                character.Designs.Remove(design);
                plugin.SaveConfiguration();
            }
            ImGui.PopStyleColor();
            ImGui.PopFont();

            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold Ctrl+Shift to delete");

            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
        }

        private void ClassicHandleDesignDragDrop(Character character, CharacterDesign design, Vector2 rowMin, Vector2 rowMax, bool hovered, float scale)
{
            // Manual drop target
            if (draggedDesign != null && ImGui.IsMouseReleased(ImGuiMouseButton.Left) &&
                ImGui.IsMouseHoveringRect(rowMin, rowMax, true) && draggedDesign != design)
            {
                var list = character.Designs;
                list.Remove(draggedDesign);
                int idx = list.IndexOf(design);
                draggedDesign.FolderId = design.FolderId;
                list.Insert(idx, draggedDesign);
                draggedDesign = null;
                plugin.SaveConfiguration();
                plugin.RefreshTreeItems(character);
            }

            // Blue outline while dragging over
            if (draggedDesign != null && hovered)
            {
                var dl = ImGui.GetWindowDrawList();
                uint col = ImGui.GetColorU32(new Vector4(0.27f, 0.53f, 0.90f, 1f));
                dl.AddRect(rowMin, rowMax, col, 0, ImDrawFlags.None, 2 * scale);
            }
        }

        private void ClassicHandleDropToRoot(bool anyHeaderHovered, bool anyRowHovered, Character character)
{
            if (draggedDesign != null && ImGui.IsMouseReleased(ImGuiMouseButton.Left) &&
                !anyHeaderHovered && !anyRowHovered)
            {
                draggedDesign.FolderId = null;
                plugin.SaveConfiguration();
                plugin.RefreshTreeItems(character);
                draggedDesign = null;
            }

            if (draggedFolder != null && ImGui.IsMouseReleased(ImGuiMouseButton.Left) &&
                !anyHeaderHovered && !anyRowHovered)
            {
                draggedFolder.ParentFolderId = null;
                plugin.SaveConfiguration();
                plugin.RefreshTreeItems(character);
                draggedFolder = null;
            }
        }

        private void DrawClassicImportWindow(float scale)
{
            if (!isImportWindowOpen || targetForDesignImport == null)
                return;
            int frame = ImGui.GetFrameCount();
            if (_importDrawnFrame == frame) return;
            _importDrawnFrame = frame;

            var windowSize = new Vector2(400 * scale, 450 * scale);
            ImGui.SetNextWindowSize(windowSize, ImGuiCond.FirstUseEver);

            if (ImGui.Begin("Import Designs", ref isImportWindowOpen, ImGuiWindowFlags.NoCollapse))
            {
                ApplyScaledStyles(scale);

                ImGui.Text($"Import designs to: {targetForDesignImport.Name}");
                ImGui.Separator();

                if (ImGui.BeginTabBar("##ClassicImportTabs"))
                {
                    if (ImGui.BeginTabItem("From Characters"))
                    {
                        DrawClassicImportCharactersTab(scale);
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("From Glamourer"))
                    {
                        DrawClassicImportGlamourerTab(scale);
                        ImGui.EndTabItem();
                    }
                    ImGui.EndTabBar();
                }

                ImGui.Separator();
                if (ImGui.Button("Close"))
                {
                    isImportWindowOpen = false;
                }

                DrawGlamImportConfirmPopup();

                PopScaledStyles();
            }
            ImGui.End();
        }

        private void DrawClassicImportCharactersTab(float scale)
        {
            if (targetForDesignImport == null)
                return;

            ImGui.BeginChild("ImportScrollArea", new Vector2(0, -40 * scale), false);

            var charactersWithDesigns = plugin.Characters
                .Where(c => c != targetForDesignImport && c.Designs.Count > 0)
                .OrderBy(c => c.Name)
                .ToList();

            foreach (var character in charactersWithDesigns)
            {
                if (ImGui.CollapsingHeader($"{character.Name} ({character.Designs.Count} designs)"))
                {
                    float indentAmount = 15f * scale;
                    ImGui.Indent(indentAmount);

                    foreach (var design in character.Designs)
                    {
                        float buttonSize = 18f * scale;

                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
                        ImGui.PushFont(UiBuilder.IconFont);

                        if (ImGui.Selectable($"##import_{design.Id}", false, ImGuiSelectableFlags.None, new Vector2(buttonSize, buttonSize)))
                        {
                            var json = JsonConvert.SerializeObject(design);
                            var clone = JsonConvert.DeserializeObject<CharacterDesign>(json);
                            clone.Name = design.Name + " (Copy)";
                            clone.Id = Guid.NewGuid();
                            clone.DateAdded = DateTime.UtcNow;
                            clone.FolderId = null;

                            targetForDesignImport.Designs.Add(clone);
                            plugin.SaveConfiguration();
                            plugin.AchievementTracker?.OnDesignImported();
                        }

                        ImGui.PopFont();
                        ImGui.PopStyleColor();

                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip($"Import '{design.Name}'");

                        ImGui.SameLine();
                        ImGui.Text(design.Name);
                    }

                    ImGui.Unindent(indentAmount);
                }
            }

            ImGui.EndChild();
        }

        private void DrawClassicImportGlamourerTab(float scale)
        {
            if (targetForDesignImport == null)
                return;

            RebuildGlamImportState(targetForDesignImport);

            ImGui.BeginChild("ImportGlamScrollArea", new Vector2(0, -40 * scale), false);

            var grouped = plugin.IntegrationListProvider?.GetGlamourerDesignsGrouped();
            if (grouped == null || grouped.Count == 0)
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "No Glamourer designs available.");
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Make sure Glamourer is installed and running.");
            }
            else
            {
                foreach (var (folder, designs) in grouped)
                {
                    string label = folder.Length == 0 ? "(No Folder)" : folder;
                    if (ImGui.CollapsingHeader($"{label} ({designs.Count})"))
                    {
                        float indentAmount = 15f * scale;
                        ImGui.Indent(indentAmount);

                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.84f, 0.2f, 1f));
                        if (ImGui.SmallButton($"Import all##importglamall_{label}"))
                        {
                            _glamConfirmFolder = folder;
                            _glamConfirmDesigns = designs;
                            _glamConfirmRequested = true;
                        }
                        ImGui.PopStyleColor();

                        foreach (var entry in designs)
                        {
                            float buttonSize = 18f * scale;
                            string entryName = entry.Name ?? "";
                            bool importedAlready = _glamExistingNames.Contains(entryName);

                            if (importedAlready)
                            {
                                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1f));
                                ImGui.PushFont(UiBuilder.IconFont);
                                ImGui.Text(FontAwesomeIcon.Check.ToIconString());
                                ImGui.PopFont();
                                if (ImGui.IsItemHovered())
                                    ImGui.SetTooltip("Already imported into this character");
                                ImGui.SameLine();
                                ImGui.Text(entryName);
                                ImGui.PopStyleColor();
                                continue;
                            }

                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
                            ImGui.PushFont(UiBuilder.IconFont);

                            if (ImGui.Selectable($"##importglam_{label}_{entry.Name}", false, ImGuiSelectableFlags.None, new Vector2(buttonSize, buttonSize)))
                                ImportGlamourerEntry(folder, entry);

                            ImGui.PopFont();
                            ImGui.PopStyleColor();

                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip($"Import '{entry.Name}'");

                            ImGui.SameLine();
                            ImGui.Text(entry.Name);
                        }

                        ImGui.Unindent(indentAmount);
                    }
                }
            }

            ImGui.EndChild();
        }

        private void DrawClassicAdvancedModeWindow(float scale)
{
            if (!isAdvancedModeWindowOpen)
                return;
                
            // Store original text on first open (for cancel functionality)
            if (string.IsNullOrEmpty(originalAdvancedMacroText))
                originalAdvancedMacroText = advancedDesignMacroText;

            var windowSize = new Vector2(600 * scale, 400 * scale); // Larger window for more text space
            ImGui.SetNextWindowSize(windowSize, ImGuiCond.FirstUseEver);

            if (ImGui.Begin("Advanced Macro Editor", ref isAdvancedModeWindowOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize))
            {
                ApplyScaledStyles(scale);

                ImGui.Text("Edit Design Macro Manually:");

                // Dark styling for the text editor
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.08f, 0.08f, 0.08f, 0.95f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));

                // Reserve space for smaller buttons at the bottom
                float buttonHeight = 25 * scale; // Smaller buttons
                float availableHeight = ImGui.GetContentRegionAvail().Y - buttonHeight - (10 * scale); // 10px spacing
                
                ImGui.InputTextMultiline("##AdvancedDesignMacroPopup", ref advancedDesignMacroText, 2000,
                    new Vector2(-1, availableHeight), ImGuiInputTextFlags.AllowTabInput);

                ImGui.PopStyleColor(2);

                // Button section
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 60 * scale; // Smaller buttons
                float totalButtonWidth = buttonWidth * 2 + (10 * scale); // 2 buttons + spacing
                float windowWidth = ImGui.GetWindowWidth();
                ImGui.SetCursorPosX((windowWidth - totalButtonWidth) / 2); // Center buttons

                // Center text in buttons
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4 * scale, 4 * scale));
                ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));

                // Save button (green) - just saves advanced mode changes
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.6f, 0.2f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.7f, 0.3f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.1f, 0.5f, 0.1f, 1.0f));

                if (ImGui.Button("Save", new Vector2(buttonWidth, 0)))
                {
                    // Save the advanced macro changes to the current design
                    if (activeCharacterIndex >= 0 && activeCharacterIndex < plugin.Characters.Count && !isNewDesign)
                    {
                        var character = plugin.Characters[activeCharacterIndex];
                        var existingDesign = character.Designs.FirstOrDefault(d => d.Name == originalDesignName);
                        if (existingDesign != null)
                        {
                            // Update the design's advanced macro with the edited text
                            existingDesign.AdvancedMacro = advancedDesignMacroText;
                            existingDesign.IsAdvancedMode = true;
                            // Save configuration to persist changes
                            plugin.Configuration.Save();
                        }
                    }
                    // Clear the original text since changes were saved
                    originalAdvancedMacroText = "";
                    // Close the advanced mode window
                    isAdvancedModeWindowOpen = false;
                }
                ImGui.PopStyleColor(3);

                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (10 * scale)); // Add spacing

                // Cancel button (red)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.3f, 0.3f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.1f, 0.1f, 1.0f));

                if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
                {
                    // Restore original text
                    advancedDesignMacroText = originalAdvancedMacroText;
                    originalAdvancedMacroText = "";
                    isAdvancedModeWindowOpen = false;
                    isAdvancedModeDesign = false;
                    // Don't save changes - return to normal editing
                }
                ImGui.PopStyleColor(3);
                ImGui.PopStyleVar(2);

                PopScaledStyles();
            }
            ImGui.End();

            if (!isAdvancedModeWindowOpen)
                isAdvancedModeDesign = false;
        }

        private void DrawClassicSnapshotDialog(float scale)
{
            if (!isSnapshotDialogOpen)
                return;

            // Force window size to fit content without scrolling
            ImGui.SetNextWindowSize(new Vector2(500 * scale, 400 * scale), ImGuiCond.Always);
            ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f));

            bool isOpen = true;
            if (ImGui.Begin("Create Design from Current Look", ref isOpen, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse))
            {
                if (snapshotTargetCharacter == null)
                {
                    ImGui.Text("Error: No character selected");
                    ImGui.End();
                    isSnapshotDialogOpen = false;
                    return;
                }

                // Apply simple dialog styling

                // Header with icon and styling
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 1.0f, 1.0f), "\uf030");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1.0f), "Snapshot Current Character State");
                
                // Subtle styled separator
                ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.4f, 0.6f, 0.8f, 0.5f));
                ImGui.Separator();
                ImGui.PopStyleColor();
                ImGui.Spacing();

                // Design name input with improved styling
                ImGui.TextColored(new Vector4(0.8f, 0.9f, 1.0f, 1.0f), "Design Name:");
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.1f, 0.15f, 0.2f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.15f, 0.2f, 0.25f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.2f, 0.25f, 0.3f, 1.0f));
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##SnapshotName", ref snapshotDesignName, 256);
                ImGui.PopStyleColor(3);
                ImGui.Spacing();

                // Conflict Resolution checkbox (only if enabled in settings)
                if (plugin.Configuration.EnableConflictResolution)
                {
                    ImGui.Checkbox("Use Conflict Resolution", ref snapshotUseConflictResolution);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Create design with conflict resolution features enabled");
                    ImGui.Spacing();
                }

                // Styled section header
                ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.4f, 0.6f, 0.8f, 0.5f));
                ImGui.Separator();
                ImGui.PopStyleColor();
                ImGui.Spacing();

                // Auto-detection status with improved layout
                ImGui.TextColored(new Vector4(0.8f, 0.9f, 1.0f, 1.0f), "Auto-Detection Status:");
                ImGui.Spacing();

                // Create a child region for detection status to control layout better
                ImGui.BeginChild("DetectionStatus", new Vector2(0, 90 * scale), false);

                // Glamourer detection with icon
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "\uf013");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.Text("Glamourer State:");
                ImGui.SameLine();
                
                float statusPosX = ImGui.GetContentRegionAvail().X - 80 * scale;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + statusPosX);
                
                if (snapshotDetectedMods.Count > 0)
                {
                    ImGui.TextColored(new Vector4(0.3f, 0.8f, 0.3f, 1.0f), "Detected");
                }
                else if (snapshotIsProcessing)
                {
                    ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.3f, 1.0f), "Detecting...");
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.8f, 0.3f, 0.3f, 1.0f), "None");
                }

                // Customize+ detection with icon
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "\uf007");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.Text("Customize+ Profile:");
                ImGui.SameLine();
                
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + statusPosX);
                
                if (!string.IsNullOrEmpty(snapshotDetectedCustomizePlusProfile))
                {
                    ImGui.TextColored(new Vector4(0.3f, 0.8f, 0.3f, 1.0f), "Found");
                }
                else if (snapshotIsProcessing)
                {
                    ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.3f, 1.0f), "Detecting...");
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.8f, 0.3f, 0.3f, 1.0f), "None");
                }

                // Clipboard image detection with icon
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "\uf03e");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.Text("Clipboard Image:");
                ImGui.SameLine();
                
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + statusPosX);
                
                if (snapshotHasClipboardImage)
                {
                    ImGui.TextColored(new Vector4(0.3f, 0.8f, 0.3f, 1.0f), "Available");
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.8f, 0.3f, 0.3f, 1.0f), "None");
                }

                ImGui.EndChild();

                // Status message
                if (!string.IsNullOrEmpty(snapshotStatusMessage))
                {
                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.8f, 0.6f, 0.3f, 1.0f), snapshotStatusMessage);
                }

                // Bottom section with buttons
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.4f, 0.6f, 0.8f, 0.5f));
                ImGui.Separator();
                ImGui.PopStyleColor();
                ImGui.Spacing();

                // Buttons with improved styling
                float buttonWidth = 120 * scale;
                float spacing = 10 * scale;
                float totalButtonWidth = (buttonWidth * 2) + spacing;
                float offsetX = (ImGui.GetContentRegionAvail().X - totalButtonWidth) * 0.5f;
                
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);

                // Create button with plugin-style colors
                bool canCreate = !string.IsNullOrWhiteSpace(snapshotDesignName) && !snapshotIsProcessing;
                if (!canCreate)
                    ImGui.BeginDisabled();

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.6f, 0.9f, 0.7f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.7f, 1.0f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.8f, 1.0f, 1.0f));

                if (ImGui.Button("Create Design", new Vector2(buttonWidth, 0)))
                {
                    CreateSnapshotDesign();
                }

                ImGui.PopStyleColor(3);

                if (!canCreate)
                    ImGui.EndDisabled();

                // Cancel button
                ImGui.SameLine(0, spacing);
                if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
                {
                    isSnapshotDialogOpen = false;
                }
                ImGui.End();
            }

            if (!isOpen)
                isSnapshotDialogOpen = false;
        }

        private void ClassicUpdateEffects()
{
            float deltaTime = ImGui.GetIO().DeltaTime;
            foreach (var effect in designFavoriteEffects.Values)
            {
                effect.Update(deltaTime);
            }

            foreach (var kvp in designFavoriteEffects.ToList())
            {
                kvp.Value.Draw();

                if (!kvp.Value.IsActive)
                {
                    designFavoriteEffects.Remove(kvp.Key);
                }
            }
        }

        private void ClassicInvalidateFilterCache()
{
            filterCacheDirty = true;
        }

    }
}

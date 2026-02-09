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
    public class DesignPanel : IDisposable
    {
        private Plugin plugin;
        private UIStyles uiStyles;

        public bool IsOpen { get; private set; } = false;
        private int activeCharacterIndex = -1;
        private Dictionary<string, FavoriteSparkEffect> designFavoriteEffects = new();

        // Resizable panel
        public float PanelWidth { get; private set; } = 300f; // Default width
        private const float MinPanelWidth = 250f;
        private const float MaxPanelWidth = 600f;
        private bool isResizing = false;
        private float resizeHandleWidth = 8f;

        // Search functionality
        private bool showSearchBar = false;
        private string searchQuery = "";
        private string selectedTag = "All";
        private bool showTagFilter = false;
        
        // Search cache for performance
        private List<CharacterDesign> cachedFilteredDesigns = new();
        private bool filterCacheDirty = true;
        private string lastSearchQuery = "";
        private string lastSelectedTag = "All";
        private int lastDesignCount = -1;
        
        // Design editing state
        private bool isEditDesignWindowOpen = false;
        private bool isAdvancedModeDesign = false;
        private bool isAdvancedModeWindowOpen = false;
        private bool isNewDesign = false;
        private bool isSecretDesignMode = false;

        // Edit fields
        private string editedDesignName = "";
        private string editedDesignMacro = "";
        private string editedGlamourerDesign = "";
        private string editedAutomation = "";
        private string editedCustomizeProfile = "";
        private int? editedGearset = null;
        private string editedDesignPreviewPath = "";
        private string advancedDesignMacroText = "";
        private string originalAdvancedMacroText = "";
        private string originalDesignName = "";
        private string? pendingDesignImagePath = null;
        private string? pendingPastedImagePath = null;
        
        // Temporary Secret Mode state for new designs
        private Dictionary<string, bool>? temporaryDesignSecretModState = null;
        private HashSet<string>? temporaryDesignSecretModPinOverrides = null;

        // Design sorting
        private enum DesignSortType { Favorites, Alphabetical, Recent, Oldest, Manual }
        private DesignSortType currentDesignSort => GetDesignSortFromConfig();

        // Folder management
        private string newFolderName = "";
        private bool isRenamingFolder = false;
        private Guid renameFolderId;
        private string renameFolderBuf = "";
        private DesignFolder? draggedFolder = null;
        private CharacterDesign? draggedDesign = null;
        private Vector3? newFolderSelectedColor = null;

        // Import window
        private bool isImportWindowOpen = false;
        private Character? targetForDesignImport = null;

        // Snapshot dialog
        private bool isSnapshotDialogOpen = false;
        private string snapshotDesignName = "";
        private bool snapshotUseConflictResolution = true;
        private Character? snapshotTargetCharacter = null;
        private HashSet<string> snapshotDetectedMods = new();
        private string? snapshotDetectedCustomizePlusProfile = null;
        private bool snapshotHasClipboardImage = false;
        private bool snapshotIsProcessing = false;
        private string snapshotStatusMessage = "";

        public DesignPanel(Plugin plugin, UIStyles uiStyles)
        {
            this.plugin = plugin;
            this.uiStyles = uiStyles;

            // Load saved panel width or use default
            PanelWidth = plugin.Configuration.DesignPanelWidth;
        }

        public void Dispose()
        {
            // Save panel width on dispose
            plugin.Configuration.DesignPanelWidth = PanelWidth;
            plugin.Configuration.Save();
        }

        public void Draw()
        {
            if (!IsOpen) return;

            // Calculate responsive sizing
            var totalScale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);

            // Scale the panel dimensions
            float scaledPanelWidth = PanelWidth * GetSafeScale(totalScale);
            float scaledMinWidth = MinPanelWidth * totalScale;
            float scaledMaxWidth = MaxPanelWidth * totalScale;
            float scaledHandleWidth = resizeHandleWidth * totalScale;

            DrawDesignPanelContent(totalScale, scaledPanelWidth);
            DrawResizeHandle(totalScale, scaledPanelWidth, scaledMinWidth, scaledMaxWidth, scaledHandleWidth);

            if (IsOpen)
            {
                UpdateEffects();
            }

            DrawImportWindow(totalScale);
            DrawAdvancedModeWindow(totalScale);
            DrawSnapshotDialog(totalScale);
        }

        private void DrawResizeHandle(float totalScale, float scaledPanelWidth, float scaledMinWidth, float scaledMaxWidth, float scaledHandleWidth)
        {
            // Current window position and size
            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();

            // Position handle at the very left edge of the design panel window
            var handleMin = new Vector2(windowPos.X, windowPos.Y);
            var handleMax = new Vector2(windowPos.X + scaledHandleWidth, windowPos.Y + windowSize.Y);

            // Check if mouse is over the handle area
            bool hovered = ImGui.IsMouseHoveringRect(handleMin, handleMax);

            // Capture mouse input when over resize handle to prevent window dragging
            if (hovered || isResizing)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);

                if (hovered && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseDown(ImGuiMouseButton.Left)))
                {
                    ImGui.SetItemAllowOverlap();

                    // Create an invisible button over the resize area to capture input
                    ImGui.SetCursorScreenPos(handleMin);
                    ImGui.InvisibleButton("##resize_handle", new Vector2(scaledHandleWidth, windowSize.Y));

                    if (ImGui.IsItemActive() || isResizing)
                    {
                        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                        {
                            isResizing = true;
                        }
                    }
                }
            }

            // Handle resizing
            if (isResizing)
            {
                if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    // Current mouse position
                    float currentMouseX = ImGui.GetMousePos().X;
                    // Calculate new width based on mouse position relative to the window's right edge
                    float windowRightEdge = ImGui.GetWindowPos().X + ImGui.GetWindowSize().X;
                    float newScaledWidth = windowRightEdge - currentMouseX;
                    // Convert to base units and clamp
                    float newWidth = newScaledWidth / totalScale;
                    PanelWidth = Math.Clamp(newWidth, MinPanelWidth, MaxPanelWidth);
                    // Save the new width immediately for responsiveness
                    plugin.Configuration.DesignPanelWidth = PanelWidth;
                    // Force main window to recalculate layout
                    if (plugin.MainWindow != null)
                    {
                        plugin.MainWindow.InvalidateLayout();
                    }
                }
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    isResizing = false;
                    // Save configuration
                    plugin.Configuration.Save();
                }
            }

            // Draw visual resize handle
            var drawList = ImGui.GetWindowDrawList();
            uint handleColor = hovered || isResizing
                ? ImGui.GetColorU32(new Vector4(0.6f, 0.6f, 0.8f, 0.8f))
                : ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.6f, 0.3f));

            // Subtle line at left edge
            drawList.AddLine(
                new Vector2(handleMin.X + 2 * totalScale, handleMin.Y + 10 * totalScale),
                new Vector2(handleMin.X + 2 * totalScale, handleMax.Y - 10 * totalScale),
                handleColor,
                2f * totalScale
            );

            // Draw resize grip dots when hovered
            if (hovered || isResizing)
            {
                float dotSize = 2f * totalScale;
                float dotSpacing = 6f * totalScale;
                var centerX = handleMin.X + scaledHandleWidth / 2;
                var centerY = handleMin.Y + windowSize.Y / 2;
                for (int i = -2; i <= 2; i++)
                {
                    drawList.AddCircleFilled(
                        new Vector2(centerX, centerY + i * dotSpacing),
                        dotSize,
                        handleColor
                    );
                }
            }
        }
        private float GetSafeScale(float baseScale)
        {
            return Math.Clamp(baseScale, 0.3f, 5.0f);
        }

        private void UpdateEffects()
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

        public void Open(int characterIndex)
        {
            activeCharacterIndex = characterIndex;
            IsOpen = true;
            plugin.IsDesignPanelOpen = true;
        }

        public void Close()
        {
            IsOpen = false;
            activeCharacterIndex = -1;
            plugin.IsDesignPanelOpen = false;
            
            // Close Mod Manager window if it's open
            if (plugin.SecretModeModWindow?.IsOpen ?? false)
            {
                plugin.SecretModeModWindow.IsOpen = false;
            }
            
            CloseDesignEditor();
        }

        private void DrawDesignPanelContent(float totalScale, float scaledPanelWidth)
        {
            if (activeCharacterIndex < 0 || activeCharacterIndex >= plugin.Characters.Count)
                return;

            var character = plugin.Characters[activeCharacterIndex];

            ApplyScaledStyles(totalScale);

            try
            {
                DrawHeader(character, totalScale);

                if (isEditDesignWindowOpen)
                {
                    DrawDesignForm(character, totalScale);
                    ImGui.Separator();
                }

                DrawSortingControls(character, totalScale);
                ImGui.Separator();

                DrawDesignList(character, totalScale);
            }
            finally
            {
                PopScaledStyles();
            }
        }

        private void ApplyScaledStyles(float scale)
        {
            // Check for custom Design Panel background colour
            var designPanelBg = new Vector4(0.08f, 0.08f, 0.1f, 0.98f);
            var designPanelChildBg = new Vector4(0.1f, 0.1f, 0.12f, 0.95f);

            if (plugin.Configuration.SelectedTheme == ThemeSelection.Custom)
            {
                var customTheme = plugin.Configuration.CustomTheme;
                if (customTheme.ColorOverrides.TryGetValue("custom.designPanelBg", out var packed) && packed.HasValue)
                {
                    var customColor = CustomThemeDefinitions.UnpackColor(packed.Value);
                    designPanelBg = customColor;
                    designPanelChildBg = customColor;
                }
            }

            ImGui.PushStyleColor(ImGuiCol.WindowBg, designPanelBg);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, designPanelChildBg);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.95f, 0.95f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.16f, 0.16f, 0.2f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.22f, 0.22f, 0.28f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.28f, 0.28f, 0.35f, 1.0f));

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5.0f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8.0f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8 * scale, 5 * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6 * scale, 3 * scale));
        }

        private void PopScaledStyles()
        {
            ImGui.PopStyleVar(4);
            ImGui.PopStyleColor(6);
        }

        private void DrawHeader(Character character, float scale)
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
                var io = ImGui.GetIO();
                bool ctrlHeld = io.KeyCtrl;
                bool shiftHeld = io.KeyShift;

                if (ctrlHeld && shiftHeld && plugin.Configuration.EnableConflictResolution)
                {
                    isSecretDesignMode = true;
                    AddNewDesign();
                    editedDesignMacro = (!plugin.Configuration.EnableConflictResolution && isSecretDesignMode) ? GenerateSecretDesignMacro(character) : GenerateDesignMacro(character);
                    if (isAdvancedModeDesign)
                        advancedDesignMacroText = editedDesignMacro;
                }
                else if (shiftHeld)
                {
                    isSecretDesignMode = false;
                    isImportWindowOpen = true;
                    targetForDesignImport = character;
                }
                else
                {
                    isSecretDesignMode = false;
                    AddNewDesign();
                    editedDesignMacro = GenerateDesignMacro(character);
                    if (isAdvancedModeDesign)
                        advancedDesignMacroText = editedDesignMacro;
                }
            }

            plugin.DesignPanelAddButtonPos = ImGui.GetItemRectMin();
            plugin.DesignPanelAddButtonSize = ImGui.GetItemRectSize();

            ImGui.PopStyleColor(4);

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("Click to add a new design\nHold Shift to import from another character");
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

            ImGui.PopStyleColor(4);

            DrawFolderCreationPopup(character, scale);

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
                    InvalidateFilterCache();
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Search designs");

            // Snapshot button
            ImGui.SameLine();
            float availableWidth = ImGui.GetContentRegionAvail().X;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availableWidth - (buttonSize * 2) - (5 * scale));

            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));        // Dark gray
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.4f, 0.4f, 0.9f)); // Medium gray  
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));  // Light gray
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 1.0f, 1.0f));          // White text
            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));        // Center icon

            if (ImGui.Button($"\uf030##CreateSnapshot"))
            {
                if (activeCharacterIndex >= 0 && activeCharacterIndex < plugin.Characters.Count)
                {
                    var io = ImGui.GetIO();
                    var selectedCharacter = plugin.Characters[activeCharacterIndex];
                    
                    if (io.KeyCtrl && io.KeyShift)
                    {
                        // Ctrl+Shift: Smart snapshot with CR
                        CreateSmartSnapshot(selectedCharacter, useConflictResolution: true);
                    }
                    else
                    {
                        // Regular click: Smart snapshot without CR
                        CreateSmartSnapshot(selectedCharacter, useConflictResolution: false);
                    }
                }
            }

            ImGui.PopStyleVar(1);
            ImGui.PopStyleColor(4);
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                string tooltip = "Create Design from Current Look\n• Click: Smart snapshot";
                if (plugin.Configuration.EnableConflictResolution)
                    tooltip += "\n• Ctrl+Shift+Click: Smart snapshot with Conflict Resolution";
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

        private void DrawFolderCreationPopup(Character character, float scale)
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

        private void DrawDesignForm(Character character, float scale)
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

            DrawGlamourerField(character, inputWidth, scale);

            if (plugin.Configuration.EnableAutomations)
            {
                DrawAutomationField(inputWidth, scale);
            }

            DrawCustomizeField(inputWidth, scale);

            if (plugin.Configuration.EnableGearsetAssignments)
            {
                DrawGearsetField(inputWidth, scale);
            }

            DrawPreviewImageField(scale);

            // Secret Mode Mod Selection (only for secret mode designs)
            if (isSecretDesignMode)
            {
                DrawSecretModeDesignField(character, scale);
                ImGui.Separator();
            }

            ImGui.Separator();

            DrawAdvancedModeToggle(scale);

            ImGui.Separator();

            DrawFormActionButtons(character, scale);

            ImGui.EndChild();
        }

        private void DrawGlamourerField(Character character, float inputWidth, float scale)
        {
            ImGui.Text("Glamourer Design*");

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

            if (AutocompleteCombo.Draw("##GlamourerDesign", ref editedGlamourerDesign, glamourerOptions, inputWidth, "Select design..."))
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

        private void DrawAutomationField(float inputWidth, float scale)
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

        private void DrawCustomizeField(float inputWidth, float scale)
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

        private void DrawGearsetField(float inputWidth, float scale)
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

        private void DrawPreviewImageField(float scale)
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

        private void DrawSecretModeDesignField(Character character, float scale)
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

        private void DrawAdvancedModeToggle(float scale)
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

        private void DrawFormActionButtons(Character character, float scale)
        {
            float buttonWidth = 85 * scale;
            float buttonHeight = 20 * scale;
            float buttonSpacing = 8 * scale;
            float totalButtonWidth = (buttonWidth * 2 + buttonSpacing);
            float availableWidth = ImGui.GetContentRegionAvail().X;
            float buttonPosX = (availableWidth > totalButtonWidth) ? (availableWidth - totalButtonWidth) / 2f : 0;

            ImGui.SetCursorPosX(buttonPosX);

            bool canSave = !string.IsNullOrWhiteSpace(editedDesignName) && !string.IsNullOrWhiteSpace(editedGlamourerDesign);

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

        private void DrawSortingControls(Character character, float scale)
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
                    InvalidateFilterCache();
                }
            }
        }
        
        private void InvalidateFilterCache()
        {
            filterCacheDirty = true;
        }

        private void DrawDesignList(Character character, float scale)
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
                    DrawFolderItem(character, folder, ref folderWasHovered, scale);
                    if (folderWasHovered) anyHeaderHovered = true;
                }
                else
                {
                    var design = (CharacterDesign)entry.item;
                    DrawDesignRow(character, design, false, scale);
                    if (ImGui.IsItemHovered()) anyRowHovered = true;
                }
            }

            // Handle dropping outside any header
            HandleDropToRoot(anyHeaderHovered, anyRowHovered, character);

            ImGui.EndChild();
        }

        private void DrawFolderItem(Character character, DesignFolder folder, ref bool wasHovered, float scale)
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
                DrawFolderContextMenu(character, folder, scale);
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
                DrawFolderContents(character, folder, scale);
            }
        }

        private void DrawFolderContextMenu(Character character, DesignFolder folder, float scale)
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

        private void DrawFolderContents(Character character, DesignFolder folder, float scale)
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
                DrawFolderItem(character, child, ref childWasHovered, scale);
                ImGui.Unindent(indentAmount);
            }

            foreach (var design in designsToShow.OrderBy(d => d.SortOrder))
            {
                ImGui.Indent(indentAmount);
                DrawDesignRow(character, design, true, scale);
                ImGui.Unindent(indentAmount);
            }

            // Visual separation
            ImGui.Spacing();
            ImGui.Separator();
        }

        private void DrawDesignRow(Character character, CharacterDesign design, bool isInsideFolder, float scale)
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
            DrawDesignRowContent(character, design, rowMin, rowMax, rowH, hovered, rowW, scale);

            // Handle drag and drop
            HandleDesignDragDrop(character, design, rowMin, rowMax, hovered, scale);

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

        private void DrawDesignRowContent(Character character, CharacterDesign design, Vector2 rowMin, Vector2 rowMax, float rowH, bool hovered, float rowW, float scale)
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
            var textColor = isActive ? new Vector4(0.2f, 0.9f, 0.2f, 1f) : new Vector4(0.9f, 0.9f, 0.9f, 1f); // Green for active, light gray for inactive
            ImGui.PushStyleColor(ImGuiCol.Text, textColor);
            ImGui.TextUnformatted(name);
            ImGui.PopStyleColor();

            // Action buttons (only when hovered, compact)
            if (hovered)
            {
                DrawCompactDesignActionButtons(character, design, rowMin, rowW, rowH, btnSize, spacing, pad, scale);
            }
        }

        private void DrawCompactDesignActionButtons(Character character, CharacterDesign design, Vector2 rowMin, float rowW, float rowH, float btnSize, float spacing, float pad, float scale)
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
                            if (Plugin.ClientState.LocalPlayer is { } player && player.HomeWorld.IsValid)
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
                    if (Plugin.ClientState.LocalPlayer is { } player && player.HomeWorld.IsValid)
                    {
                        string localName = player.Name.TextValue;
                        string worldName = player.HomeWorld.Value.Name.ToString();
                        string fullKey = $"{localName}@{worldName}";
                        string pluginCharacterKey = $"{character.Name}@{worldName}";
                        plugin.Configuration.LastUsedCharacterByPlayer[fullKey] = pluginCharacterKey;
                    }
                    
                    plugin.Configuration.Save();
                }
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
                bool isCtrlShift = ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift;
                
                // Open edit window first
                OpenEditDesignWindow(character, design);
                
                // Then convert to secret mode if Ctrl+Shift was held and Conflict Resolution is enabled
                if (isCtrlShift && plugin.Configuration.EnableConflictResolution)
                {
                    // Set secret mode flag
                    isSecretDesignMode = true;
                    
                    // Generate and set the appropriate macro in the edit fields
                    editedDesignMacro = (!plugin.Configuration.EnableConflictResolution && isSecretDesignMode) ? GenerateSecretDesignMacro(character) : GenerateDesignMacro(character);
                    if (isAdvancedModeDesign)
                    {
                        advancedDesignMacroText = editedDesignMacro;
                    }
                }
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

        private void HandleDesignDragDrop(Character character, CharacterDesign design, Vector2 rowMin, Vector2 rowMax, bool hovered, float scale)
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

        private void HandleDropToRoot(bool anyHeaderHovered, bool anyRowHovered, Character character)
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

        private void DrawImportWindow(float scale)
        {
            if (!isImportWindowOpen || targetForDesignImport == null)
                return;

            var windowSize = new Vector2(400 * scale, 450 * scale);
            ImGui.SetNextWindowSize(windowSize, ImGuiCond.FirstUseEver);

            if (ImGui.Begin("Import Designs", ref isImportWindowOpen, ImGuiWindowFlags.NoCollapse))
            {
                ApplyScaledStyles(scale);

                ImGui.Text($"Import designs to: {targetForDesignImport.Name}");
                ImGui.Separator();

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

                            // Green plus symbol
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
                            ImGui.PushFont(UiBuilder.IconFont);

                            if (ImGui.Selectable($"\uf067##import_{character.Name}_{design.Name}", false, ImGuiSelectableFlags.None, new Vector2(buttonSize, buttonSize)))
                            {
                                // Clone the entire design using JSON serialization (exact copy like copy-paste in config)
                                var json = JsonConvert.SerializeObject(design);
                                var clone = JsonConvert.DeserializeObject<CharacterDesign>(json);
                                clone.Name = design.Name + " (Copy)";
                                clone.Id = Guid.NewGuid();
                                clone.DateAdded = DateTime.UtcNow;

                                targetForDesignImport.Designs.Add(clone);
                                plugin.SaveConfiguration();
                            }

                            ImGui.PopFont();
                            ImGui.PopStyleColor();

                            if (ImGui.IsItemHovered())
                            {
                                ImGui.SetTooltip($"Import '{design.Name}'");
                            }

                            ImGui.SameLine();
                            ImGui.Text(design.Name);
                        }

                        ImGui.Unindent(indentAmount);
                    }
                }

                ImGui.EndChild();

                ImGui.Separator();
                if (ImGui.Button("Close"))
                {
                    isImportWindowOpen = false;
                }

                PopScaledStyles();
            }
            ImGui.End();
        }

        private void DrawAdvancedModeWindow(float scale)
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

        // Utility methods
        private void SelectPreviewImage()
        {
            plugin.OpenFilePicker(
                "Select Design Preview Image",
                "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|PNG files (*.png)|*.png",
                (selectedPath) =>
                {
                    lock (this)
                    {
                        pendingDesignImagePath = selectedPath;
                    }
                }
            );
        }

        private void PasteImageFromClipboard()
        {
            try
            {
                Thread thread = new Thread(() =>
                {
                    try
                    {
                        // Check if clipboard contains image data
                        if (!Clipboard.ContainsImage())
                        {
                            Plugin.Log.Warning("No image found in clipboard");
                            return;
                        }

                        // Get image from clipboard
                        using (var clipboardImage = Clipboard.GetImage())
                        {
                            if (clipboardImage == null)
                            {
                                Plugin.Log.Warning("Failed to get image from clipboard");
                                return;
                            }

                            // Create directory if it doesn't exist
                            string configDir = plugin.PluginPath;
                            string imagesDir = Path.Combine(configDir, "Images");
                            string previewsDir = Path.Combine(imagesDir, "DesignPreviews");
                            
                            Directory.CreateDirectory(previewsDir);

                            // Generate unique filename with timestamp
                            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                            string fileName = $"design_preview_{timestamp}.png";
                            string fullPath = Path.Combine(previewsDir, fileName);

                            // Save image as PNG
                            clipboardImage.Save(fullPath, ImageFormat.Png);

                            // Set the path for UI update
                            lock (this)
                            {
                                pendingPastedImagePath = fullPath;
                            }

                            Plugin.Log.Info($"Pasted image saved to: {fullPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error($"Error pasting image from clipboard: {ex.Message}");
                    }
                });

                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Critical clipboard paste error: {ex.Message}");
            }
        }

        private bool IsClipboardImageAvailable()
        {
            try
            {
                return Clipboard.ContainsImage();
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void CleanupOrphanedPreviewImages(Plugin plugin)
        {
            try
            {
                string configDir = plugin.PluginPath;
                string previewsDir = Path.Combine(configDir, "Images", "DesignPreviews");
                
                if (!Directory.Exists(previewsDir))
                    return;

                // Get all images in the previews directory
                var imageFiles = Directory.GetFiles(previewsDir, "*.png")
                    .Concat(Directory.GetFiles(previewsDir, "*.jpg"))
                    .Concat(Directory.GetFiles(previewsDir, "*.jpeg"))
                    .ToList();

                if (!imageFiles.Any())
                    return;

                // Collect all preview image paths currently in use
                var referencedImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                foreach (var character in plugin.Characters)
                {
                    foreach (var design in character.Designs)
                    {
                        if (!string.IsNullOrEmpty(design.PreviewImagePath) && 
                            File.Exists(design.PreviewImagePath))
                        {
                            referencedImages.Add(Path.GetFullPath(design.PreviewImagePath));
                        }
                    }
                }

                // Delete orphaned images
                int deletedCount = 0;
                foreach (var imageFile in imageFiles)
                {
                    string fullImagePath = Path.GetFullPath(imageFile);
                    
                    if (!referencedImages.Contains(fullImagePath))
                    {
                        try
                        {
                            File.Delete(imageFile);
                            deletedCount++;
                            Plugin.Log.Info($"Deleted orphaned preview image: {Path.GetFileName(imageFile)}");
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.Warning($"Failed to delete orphaned image {imageFile}: {ex.Message}");
                        }
                    }
                }

                if (deletedCount > 0)
                {
                    Plugin.Log.Info($"Cleanup completed: {deletedCount} orphaned preview images deleted");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error during preview image cleanup: {ex.Message}");
            }
        }

        private (float width, float height) CalculateImageDimensions(IDalamudTextureWrap texture, float maxSize)
        {
            float originalWidth = texture.Width;
            float originalHeight = texture.Height;
            float aspectRatio = originalWidth / originalHeight;

            if (aspectRatio > 1) // Landscape
            {
                return (maxSize, maxSize / aspectRatio);
            }
            else // Portrait or Square
            {
                return (maxSize * aspectRatio, maxSize);
            }
        }

        private void AddNewDesign()
        {
            isNewDesign = true;
            isEditDesignWindowOpen = true;
            plugin.IsEditDesignWindowOpen = true;
            editedDesignName = "";
            editedGlamourerDesign = "";
            editedDesignMacro = "";
            isAdvancedModeDesign = false;
            editedAutomation = "";
            editedCustomizeProfile = "";
            editedGearset = null;
            editedDesignPreviewPath = "";
            plugin.EditedDesignName = editedDesignName;
            plugin.EditedGlamourerDesign = editedGlamourerDesign;
        }

        private void OpenEditDesignWindow(Character character, CharacterDesign design)
        {
            isNewDesign = false;
            isEditDesignWindowOpen = true;
            plugin.IsEditDesignWindowOpen = true;
            originalDesignName = design.Name;
            editedDesignName = design.Name;
            editedDesignMacro = design.IsAdvancedMode ? design.AdvancedMacro ?? "" : design.Macro ?? "";
            editedGlamourerDesign = !string.IsNullOrWhiteSpace(design.GlamourerDesign)
                ? design.GlamourerDesign
                : ExtractGlamourerDesignFromMacro(design.Macro ?? "");

            editedAutomation = design.Automation ?? "";
            editedCustomizeProfile = design.CustomizePlusProfile ?? "";
            editedGearset = design.AssignedGearset;
            editedDesignPreviewPath = design.PreviewImagePath ?? "";
            isAdvancedModeDesign = design.IsAdvancedMode;
            isAdvancedModeWindowOpen = design.IsAdvancedMode;
            advancedDesignMacroText = design.AdvancedMacro ?? "";
            
            // Check if this is a Secret Mode (Conflict Resolution) design
            if ((design.SecretModState != null && design.SecretModState.Any()) ||
                (design.ModOptionSettings != null && design.ModOptionSettings.Any()) ||
                (design.SecretModPinOverrides != null && design.SecretModPinOverrides.Any()))
            {
                isSecretDesignMode = true;
                // Load the existing mod state into temporary storage for editing
                if (design.SecretModState != null)
                {
                    temporaryDesignSecretModState = new Dictionary<string, bool>(design.SecretModState);
                }
                if (design.SecretModPinOverrides != null)
                {
                    temporaryDesignSecretModPinOverrides = new HashSet<string>(design.SecretModPinOverrides);
                }
            }
        }

        private void CloseDesignEditor()
        {
            isEditDesignWindowOpen = false;
            plugin.IsEditDesignWindowOpen = false;
            isAdvancedModeWindowOpen = false;
            isNewDesign = false;
            isSecretDesignMode = false;
            
            // Close Mod Manager window if it's open
            if (plugin.SecretModeModWindow?.IsOpen ?? false)
            {
                plugin.SecretModeModWindow.IsOpen = false;
            }
            
            ResetEditFields();
        }

        private void ResetEditFields()
        {
            editedDesignName = "";
            editedDesignMacro = "";
            editedGlamourerDesign = "";
            editedAutomation = "";
            editedCustomizeProfile = "";
            editedDesignPreviewPath = "";
            advancedDesignMacroText = "";
            originalDesignName = "";
            temporaryDesignSecretModState = null;
            temporaryDesignSecretModPinOverrides = null;
        }

        private void SaveDesign(Character character)
        {
            if (string.IsNullOrWhiteSpace(editedDesignName) || string.IsNullOrWhiteSpace(editedGlamourerDesign))
                return;

            var existingDesign = !isNewDesign
                ? character.Designs.FirstOrDefault(d => d.Name == originalDesignName)
                : null;

            if (existingDesign != null)
            {
                // Update existing design
                existingDesign.Name = editedDesignName;
                bool wasPreviouslyAdvanced = existingDesign.IsAdvancedMode;
                bool keepAdvanced = wasPreviouslyAdvanced && !isAdvancedModeDesign;

                // For advanced mode with empty macro, generate from form fields
                string advancedMacroToUse = advancedDesignMacroText;
                if ((isAdvancedModeDesign || keepAdvanced) && string.IsNullOrWhiteSpace(advancedMacroToUse))
                {
                    advancedMacroToUse = GenerateDesignMacro(character);
                }

                existingDesign.Macro = keepAdvanced
                    ? advancedMacroToUse
                    : (isAdvancedModeDesign ? advancedMacroToUse : GenerateDesignMacro(character));

                existingDesign.AdvancedMacro = isAdvancedModeDesign || keepAdvanced
                    ? advancedMacroToUse
                    : "";

                existingDesign.IsAdvancedMode = isAdvancedModeDesign || keepAdvanced;
                existingDesign.Automation = editedAutomation;
                existingDesign.GlamourerDesign = editedGlamourerDesign;
                existingDesign.CustomizePlusProfile = editedCustomizeProfile;
                existingDesign.AssignedGearset = editedGearset;
                existingDesign.PreviewImagePath = editedDesignPreviewPath;

                // Apply any Secret Mode state that was configured during editing
                if (temporaryDesignSecretModState != null)
                {
                    existingDesign.SecretModState = temporaryDesignSecretModState;
                }
                if (temporaryDesignSecretModPinOverrides != null)
                {
                    existingDesign.SecretModPinOverrides = temporaryDesignSecretModPinOverrides;
                }
            }
            else
            {
                // Add new design - generate macro from fields if advanced mode has empty macro
                string macroForNewDesign = isAdvancedModeDesign
                    ? (string.IsNullOrWhiteSpace(advancedDesignMacroText) ? GenerateDesignMacro(character) : advancedDesignMacroText)
                    : GenerateDesignMacro(character);

                var newDesign = new CharacterDesign(
                    editedDesignName,
                    macroForNewDesign,
                    isAdvancedModeDesign,
                    isAdvancedModeDesign ? macroForNewDesign : "",
                    editedGlamourerDesign,
                    editedAutomation,
                    editedCustomizeProfile,
                    editedDesignPreviewPath
                )
                {
                    DateAdded = DateTime.UtcNow,
                    AssignedGearset = editedGearset
                };

                // Apply any Secret Mode state that was configured during editing
                if (temporaryDesignSecretModState != null)
                {
                    newDesign.SecretModState = temporaryDesignSecretModState;
                }
                if (temporaryDesignSecretModPinOverrides != null)
                {
                    newDesign.SecretModPinOverrides = temporaryDesignSecretModPinOverrides;
                }

                character.Designs.Add(newDesign);
            }

            plugin.SaveConfiguration();
        }

        private void DeleteFolder(Character character, DesignFolder folder)
        {
            foreach (var d in character.Designs.Where(d => d.FolderId == folder.Id))
                d.FolderId = null;

            foreach (var sub in character.DesignFolders.Where(f => f.ParentFolderId == folder.Id))
                sub.ParentFolderId = null;

            character.DesignFolders.RemoveAll(f => f.Id == folder.Id);

            plugin.SaveConfiguration();
            plugin.RefreshTreeItems(character);
        }

        private DesignSortType GetDesignSortFromConfig()
        {
            return plugin.Configuration.CurrentDesignSortIndex switch
            {
                0 => DesignSortType.Favorites,
                1 => DesignSortType.Alphabetical,
                2 => DesignSortType.Recent,
                3 => DesignSortType.Oldest,
                4 => DesignSortType.Manual,
                _ => DesignSortType.Alphabetical // Default fallback
            };
        }
        
        private void SetDesignSort(int sortIndex)
        {
            plugin.Configuration.CurrentDesignSortIndex = sortIndex;
            plugin.Configuration.Save();
        }

        private void SortDesigns(Character character)
        {
            var sortType = currentDesignSort;
            if (sortType == DesignSortType.Manual)
                return;

            // Sort all designs - both root level and within folders
            SortDesignList(character.Designs, sortType);
        }
        
        private void SortDesignList(List<CharacterDesign> designs, DesignSortType sortType)
        {
            if (sortType == DesignSortType.Favorites)
            {
                designs.Sort((a, b) =>
                {
                    int favCompare = b.IsFavorite.CompareTo(a.IsFavorite);
                    if (favCompare != 0) return favCompare;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            }
            else if (sortType == DesignSortType.Alphabetical)
            {
                designs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            else if (sortType == DesignSortType.Recent)
            {
                designs.Sort((a, b) => b.DateAdded.CompareTo(a.DateAdded));
            }
            else if (sortType == DesignSortType.Oldest)
            {
                designs.Sort((a, b) => a.DateAdded.CompareTo(b.DateAdded));
            }
        }

        private Vector4 GetFolderColor(Character character, DesignFolder folder)
        {
            Vector3 baseColor;

            if (folder.CustomColor.HasValue)
            {
                baseColor = folder.CustomColor.Value;
            }
            else
            {
                baseColor = GetAutoGeneratedColor(character, folder);
            }

            return new Vector4(baseColor.X, baseColor.Y, baseColor.Z, 0.6f);
        }

        private Vector3 GetAutoGeneratedColor(Character character, DesignFolder folder)
        {
            return character.NameplateColor;
        }

        private List<(string name, bool isFolder, object item, DateTime dateAdded, int manual)> BuildRenderItems(Character character)
        {
            var renderItems = new List<(string name, bool isFolder, object item, DateTime dateAdded, int manual)>();

            // Apply search filtering if active
            var designsToShow = character.Designs.AsEnumerable();
            var foldersToShow = character.DesignFolders.AsEnumerable();
            
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                designsToShow = designsToShow.Where(d => MatchesSearchQuery(d));
                foldersToShow = foldersToShow.Where(f => FolderContainsMatchingDesigns(character, f));
            }

            foreach (var f in foldersToShow.Where(f => f.ParentFolderId == null))
            {
                renderItems.Add((f.Name, true, f as object, DateTime.MinValue, f.SortOrder));
            }

            foreach (var d in designsToShow.Where(d => d.FolderId == null))
            {
                renderItems.Add((d.Name, false, d as object, d.DateAdded, d.SortOrder));
            }

            switch (currentDesignSort)
            {
                case DesignSortType.Favorites:
                    renderItems = renderItems
                        .OrderByDescending(x => x.isFolder ? false : ((CharacterDesign)x.item).IsFavorite)
                        .ThenBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    break;
                case DesignSortType.Alphabetical:
                    renderItems = renderItems
                        .OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    break;
                case DesignSortType.Recent:
                    renderItems = renderItems
                        .OrderByDescending(x => x.dateAdded)
                        .ToList();
                    break;
                case DesignSortType.Oldest:
                    renderItems = renderItems
                        .OrderBy(x => x.dateAdded)
                        .ToList();
                    break;
                case DesignSortType.Manual:
                    renderItems = renderItems
                        .OrderBy(x => x.manual)
                        .ToList();
                    break;
            }

            return renderItems;
        }

        private string GenerateDesignMacro(Character character)
        {
            if (string.IsNullOrWhiteSpace(editedGlamourerDesign))
                return "";

            string macro = $"/glamour apply {editedGlamourerDesign} | self";

            // Conditionally include automation line
            if (plugin.Configuration.EnableAutomations)
            {
                string automationToUse = !string.IsNullOrWhiteSpace(editedAutomation)
                    ? editedAutomation
                    : (!string.IsNullOrWhiteSpace(character.CharacterAutomation)
                        ? character.CharacterAutomation
                        : "None");

                macro += $"\n/glamour automation enable {automationToUse}";
            }

            // Always disable Customize+ first
            macro += "\n/customize profile disable <me>";

            // Determine Customize+ profile
            string customizeProfileToUse = !string.IsNullOrWhiteSpace(editedCustomizeProfile)
                ? editedCustomizeProfile
                : !string.IsNullOrWhiteSpace(character.CustomizeProfile)
                    ? character.CustomizeProfile
                    : string.Empty;

            // Enable only if needed
            if (!string.IsNullOrWhiteSpace(customizeProfileToUse))
                macro += $"\n/customize profile enable <me>, {customizeProfileToUse}";

            // Redraw line
            macro += "\n/penumbra redraw self";

            return macro;
        }

        private string GenerateSecretDesignMacro(Character character)
        {
            // Which Penumbra collection to target (taken from the character)
            var collection = character.PenumbraCollection;

            // What the form is currently set to
            var design = editedGlamourerDesign;
            var custom = !string.IsNullOrWhiteSpace(editedCustomizeProfile)
                             ? editedCustomizeProfile
                             : character.CustomizeProfile;

            var sb = new System.Text.StringBuilder();

            // Only add bulk-tag lines if Conflict Resolution is disabled
            if (!plugin.Configuration.EnableConflictResolution)
            {
                sb.AppendLine($"/penumbra bulktag disable {collection} | gear");
                sb.AppendLine($"/penumbra bulktag disable {collection} | hair");
                sb.AppendLine($"/penumbra bulktag enable  {collection} | {design}");
                // Glamourer "no clothes" for secret mode
                sb.AppendLine("/glamour apply no clothes | self");
            }

            // Glamourer design
            sb.AppendLine($"/glamour apply {design} | self");

            // Automation (if enabled)
            if (plugin.Configuration.EnableAutomations)
            {
                string automationToUse = !string.IsNullOrWhiteSpace(editedAutomation)
                    ? editedAutomation
                    : (!string.IsNullOrWhiteSpace(character.CharacterAutomation)
                        ? character.CharacterAutomation
                        : "None");
                sb.AppendLine($"/glamour automation enable {automationToUse}");
            }

            // Customize+
            sb.AppendLine("/customize profile disable <me>");
            if (!string.IsNullOrWhiteSpace(custom))
                sb.AppendLine($"/customize profile enable <me>, {custom}");

            // Final redraw
            sb.Append("/penumbra redraw self");

            return sb.ToString();
        }

        private string EnsureProperDesignMacroStructure()
        {
            var character = plugin.Characters[activeCharacterIndex];
            string glamourer = !string.IsNullOrWhiteSpace(editedGlamourerDesign) ? editedGlamourerDesign : "[Glamourer Design]";

            var sb = new System.Text.StringBuilder();

            if (isSecretDesignMode)
            {
                string collection = character.PenumbraCollection;

                // Only add bulk-tag lines if Conflict Resolution is disabled
                if (!plugin.Configuration.EnableConflictResolution)
                {
                    sb.AppendLine($"/penumbra bulktag disable {collection} | gear");
                    sb.AppendLine($"/penumbra bulktag disable {collection} | hair");
                    sb.AppendLine($"/penumbra bulktag enable {collection} | {glamourer}");
                    sb.AppendLine("/glamour apply no clothes | self");
                }

                sb.AppendLine($"/glamour apply {glamourer} | self");
            }
            else
            {
                sb.AppendLine($"/glamour apply {glamourer} | self");
            }

            // Conditionally include automation line
            if (plugin.Configuration.EnableAutomations)
            {
                string automationToUse = !string.IsNullOrWhiteSpace(editedAutomation)
                    ? editedAutomation
                    : (!string.IsNullOrWhiteSpace(character.CharacterAutomation)
                        ? character.CharacterAutomation
                        : "None");
                sb.AppendLine($"/glamour automation enable {automationToUse}");
            }

            // Always disable Customize+ first
            sb.AppendLine("/customize profile disable <me>");

            // Determine Customize+ profile
            string customizeProfileToUse = !string.IsNullOrWhiteSpace(editedCustomizeProfile)
                ? editedCustomizeProfile
                : !string.IsNullOrWhiteSpace(character.CustomizeProfile)
                    ? character.CustomizeProfile
                    : string.Empty;

            // Enable only if needed
            if (!string.IsNullOrWhiteSpace(customizeProfileToUse))
                sb.AppendLine($"/customize profile enable <me>, {customizeProfileToUse}");

            // Redraw line
            sb.Append("/penumbra redraw self");

            return sb.ToString();
        }

        private void UpdateAdvancedMacroGlamourerFixed(string newGlamourer)
        {
            var lines = advancedDesignMacroText.Split('\n').ToList();

            // Find and replace the main glamour apply line (not "no clothes")
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("/glamour apply", StringComparison.OrdinalIgnoreCase) &&
                    !line.Contains("no clothes", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"/glamour apply {newGlamourer} | self";
                    break;
                }
            }

            // Update bulktag enable line if it exists (for secret mode)
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("/penumbra bulktag enable", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract the collection name and replace the design part
                    var parts = line.Split('|');
                    if (parts.Length >= 2)
                    {
                        var collection = parts[0].Replace("/penumbra bulktag enable", "").Trim();
                        lines[i] = $"/penumbra bulktag enable {collection} | {newGlamourer}";
                    }
                    break;
                }
            }

            advancedDesignMacroText = string.Join("\n", lines);
        }

        private void UpdateAdvancedMacroCustomize()
        {
            advancedDesignMacroText = PatchMacroLine(
                advancedDesignMacroText,
                "/customize profile disable",
                "/customize profile disable <me>"
            );

            if (!string.IsNullOrWhiteSpace(editedCustomizeProfile))
            {
                advancedDesignMacroText = PatchMacroLine(
                    advancedDesignMacroText,
                    "/customize profile enable",
                    $"/customize profile enable <me>, {editedCustomizeProfile}"
                );
            }
            else
            {
                advancedDesignMacroText = string.Join("\n",
                    advancedDesignMacroText
                        .Split('\n')
                        .Where(l => !l.TrimStart().StartsWith("/customize profile enable"))
                );
            }
        }

        private string PatchMacroLine(string existing, string prefix, string replacement)
        {
            var lines = existing.Split('\n').ToList();
            var idx = lines.FindIndex(l => l.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

            if (idx >= 0)
            {
                // Replace existing line
                lines[idx] = replacement;
            }
            else
            {
                int insertPosition = GetProperDesignInsertPosition(lines, prefix);
                lines.Insert(insertPosition, replacement);
            }

            return string.Join("\n", lines);
        }

        private int GetProperDesignInsertPosition(List<string> lines, string prefix)
        {
            // Order for design macro commands
            var order = new[]
            {
                "/penumbra bulktag disable",
                "/penumbra bulktag enable",
                "/glamour apply no clothes",
                "/glamour apply",
                "/glamour automation enable",
                "/customize profile disable",
                "/customize profile enable",
                "/penumbra redraw"
            };

            int targetOrder = Array.FindIndex(order, o => prefix.StartsWith(o, StringComparison.OrdinalIgnoreCase));
            if (targetOrder == -1) return lines.Count; // Unknown command goes at end

            // Find the position where this command should be inserted
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

        private string ExtractGlamourerDesignFromMacro(string macro)
        {
            string[] lines = macro.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("/glamour apply ", StringComparison.OrdinalIgnoreCase))
                {
                    return line.Replace("/glamour apply ", "").Replace(" | self", "").Trim();
                }
            }
            return "";
        }

        private static string TruncateWithEllipsis(string text, float maxWidth)
        {
            while (ImGui.CalcTextSize(text + "...").X > maxWidth && text.Length > 0)
                text = text[..^1];
            return text + "...";
        }
        
        // Search helper methods
        private bool MatchesSearchQuery(CharacterDesign design)
        {
            if (string.IsNullOrWhiteSpace(searchQuery))
                return true;
                
            var query = searchQuery.ToLowerInvariant();
            
            // Search in design name
            if (design.Name.ToLowerInvariant().Contains(query))
                return true;
                
            // Search in glamourer design name
            if (!string.IsNullOrWhiteSpace(design.GlamourerDesign) && 
                design.GlamourerDesign.ToLowerInvariant().Contains(query))
                return true;
                
            // Search in automation
            if (!string.IsNullOrWhiteSpace(design.Automation) && 
                design.Automation.ToLowerInvariant().Contains(query))
                return true;
                
            // Search in tags
            if (design.Tag?.ToLowerInvariant().Contains(query) == true)
                return true;
                
            return false;
        }
        
        private bool FolderContainsMatchingDesigns(Character character, DesignFolder folder)
        {
            if (string.IsNullOrWhiteSpace(searchQuery))
                return true;
                
            // Check if folder name matches
            if (folder.Name.ToLowerInvariant().Contains(searchQuery.ToLowerInvariant()))
                return true;
                
            // Check if any design in this folder matches
            if (character.Designs.Any(d => d.FolderId == folder.Id && MatchesSearchQuery(d)))
                return true;
                
            // Check if any subfolder contains matching designs
            var subfolders = character.DesignFolders.Where(f => f.ParentFolderId == folder.Id);
            foreach (var subfolder in subfolders)
            {
                if (FolderContainsMatchingDesigns(character, subfolder))
                    return true;
            }
                
            return false;
        }

        private HashSet<string> LogAndReturnPins(Character character)
        {
            var pins = new HashSet<string>(character.SecretModPins ?? new List<string>());
            Plugin.Log.Information($"[PIN DEBUG] Design panel loading pins for character '{character.Name}': {pins.Count} pins - {string.Join(", ", pins)}");
            Plugin.Log.Information($"[PIN DEBUG] Design panel character object hash: {character.GetHashCode()}");
            return pins;
        }

        private void DrawSnapshotDialog(float scale)
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

        private void OpenSnapshotDialog(Character character)
        {
            snapshotTargetCharacter = character;
            snapshotDesignName = $"Design {DateTime.Now:yyyy-MM-dd HH:mm}";
            snapshotUseConflictResolution = plugin.Configuration.EnableConflictResolution;
            snapshotDetectedMods.Clear();
            snapshotDetectedCustomizePlusProfile = null;
            snapshotHasClipboardImage = false;
            snapshotIsProcessing = false;
            snapshotStatusMessage = "";
            
            // Start background detection tasks
            Task.Run(async () =>
            {
                try
                {
                    snapshotIsProcessing = true;
                    snapshotStatusMessage = "Detecting Glamourer state...";
                    
                    // Detect Glamourer state
                    await DetectGlamourerState();
                    
                    snapshotStatusMessage = "Detecting Customize+ profile...";
                    
                    // Detect Customize+ profile
                    await DetectCustomizePlusProfile();
                    
                    snapshotStatusMessage = "Checking clipboard for images...";
                    
                    // Check clipboard for images
                    CheckClipboardForImage();
                    
                    snapshotStatusMessage = "Detection complete";
                    snapshotIsProcessing = false;
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"Error during snapshot detection: {ex}");
                    snapshotStatusMessage = "Error during auto-detection";
                    snapshotIsProcessing = false;
                }
            });
            
            isSnapshotDialogOpen = true;
        }

        private void CreateSnapshotDesign()
        {
            if (snapshotTargetCharacter == null)
                return;

            snapshotIsProcessing = true;
            snapshotStatusMessage = "Creating design...";

            Task.Run(async () =>
            {
                try
                {
                    // Generate the appropriate macro based on CR mode
                    var snapshotMacro = GenerateSnapshotMacro(snapshotUseConflictResolution);
                    
                    // For CR mode, generate different macros
                    var regularMacro = GenerateSnapshotMacro(false); // Regular macro without CR
                    var advancedMacro = snapshotUseConflictResolution ? GenerateSnapshotMacro(true) : ""; // CR macro if enabled
                    
                    var newDesign = new CharacterDesign(
                        snapshotDesignName,
                        regularMacro, // Always use regular macro for base
                        snapshotUseConflictResolution, // Enable Advanced Mode if CR is checked
                        advancedMacro, // Advanced/CR macro
                        "", // GlamourerDesign - will be set later
                        "", // Automation
                        "", // CustomizePlusProfile - will be set later
                        null // PreviewImagePath - will be set later
                    );

                    // Create Glamourer design from current state if detected
                    if (snapshotDetectedMods.Count > 0)
                    {
                        var glamourerDesignName = $"{snapshotDesignName}";
                        var glamourerDesignId = await CreateGlamourerDesignFromCurrentState(glamourerDesignName);
                        if (glamourerDesignId != Guid.Empty)
                        {
                            // Store the design name, not the GUID, for CS+ compatibility
                            newDesign.GlamourerDesign = glamourerDesignName;
                            Plugin.Log.Information($"Created Glamourer design: {glamourerDesignName} (ID: {glamourerDesignId})");
                        }
                    }

                    // Set Customize+ profile if detected (only if it's not the Character default)
                    if (!string.IsNullOrEmpty(snapshotDetectedCustomizePlusProfile) && 
                        snapshotDetectedCustomizePlusProfile != "Character")
                    {
                        newDesign.CustomizePlusProfile = snapshotDetectedCustomizePlusProfile;
                    }

                    // Set up Secret Mode state for CR mode
                    if (snapshotUseConflictResolution)
                    {
                        // Get only gear/hair mods from Currently Affecting You tab (prevents body/sculpt/eye mods from being managed)
                        var allAffectingMods = plugin.PenumbraIntegration?.GetOnScreenTabMods();
                        var currentlyAffectingMods = new HashSet<string>();
                        
                        if (allAffectingMods != null)
                        {
                            foreach (var modDir in allAffectingMods)
                            {
                                try
                                {
                                    // Get mod type from cache or determine it
                                    ModType modType;
                                    if (plugin.modCategorizationCache.ContainsKey(modDir))
                                    {
                                        modType = plugin.modCategorizationCache[modDir];
                                    }
                                    else
                                    {
                                        // Use the static method to determine mod type
                                        modType = SecretModeModWindow.DetermineModType(modDir, "", plugin);
                                        plugin.modCategorizationCache[modDir] = modType;
                                    }

                                    // Only include gear and hair mods (safe to toggle, won't break body/sculpt/eyes)
                                    if (modType == ModType.Gear || modType == ModType.Hair)
                                    {
                                        currentlyAffectingMods.Add(modDir);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Plugin.Log.Warning($"Failed to determine mod type for {modDir}: {ex.Message}");
                                }
                            }
                        }
                        if (currentlyAffectingMods != null && currentlyAffectingMods.Count > 0)
                        {
                            // Create mod state dictionary with all currently affecting mods enabled
                            newDesign.SecretModState = new Dictionary<string, bool>();
                            foreach (var modName in currentlyAffectingMods)
                            {
                                newDesign.SecretModState[modName] = true;
                            }
                            Plugin.Log.Information($"Detected {newDesign.SecretModState.Count} currently affecting mods for CR design");
                        }
                        else
                        {
                            Plugin.Log.Information("No currently affecting mods detected for CR design");
                        }
                    }

                    // Save clipboard image if available
                    if (snapshotHasClipboardImage)
                    {
                        var imagePath = await SaveClipboardImageForDesign(newDesign.Id);
                        if (!string.IsNullOrEmpty(imagePath))
                        {
                            newDesign.PreviewImagePath = imagePath;
                        }
                    }

                    // The macro was already set during construction, no need to regenerate

                    // Add the design to the character
                    snapshotTargetCharacter.Designs.Add(newDesign);
                    
                    // Save configuration
                    plugin.Configuration.Save();

                    snapshotStatusMessage = "Design created successfully!";
                    
                    // Close dialog after a brief delay
                    await Task.Delay(1000);
                    isSnapshotDialogOpen = false;
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"Error creating snapshot design: {ex}");
                    snapshotStatusMessage = $"Error: {ex.Message}";
                }
                finally
                {
                    snapshotIsProcessing = false;
                }
            });
        }

        private string GenerateSnapshotMacro(bool useConflictResolution)
        {
            var macroLines = new List<string>();

            if (useConflictResolution)
            {
                // CR Mode: Generate macro that works with Secret Mode CR system
                // No bulktag commands - CR system handles mod management automatically
                
                // Add Glamourer apply if we have a design
                if (snapshotDetectedMods.Count > 0)
                {
                    macroLines.Add($"/glamour apply {snapshotDesignName} | self");
                }

                // Add Customize+ profile commands if we have a non-Character profile
                if (!string.IsNullOrEmpty(snapshotDetectedCustomizePlusProfile) && snapshotDetectedCustomizePlusProfile != "Character")
                {
                    macroLines.Add("/customize profile disable <me>");
                    macroLines.Add($"/customize profile enable <me>, {snapshotDetectedCustomizePlusProfile}");
                }

                // Add penumbra redraw at the end
                macroLines.Add("/penumbra redraw self");
            }
            else
            {
                // Regular Mode: Generate bulktag macros for non-CR designs
                // Add Glamourer apply if we have a design
                if (snapshotDetectedMods.Count > 0)
                {
                    macroLines.Add($"/glamour apply {snapshotDesignName} | self");
                }

                // Add Customize+ profile commands if we have a non-Character profile
                if (!string.IsNullOrEmpty(snapshotDetectedCustomizePlusProfile) && snapshotDetectedCustomizePlusProfile != "Character")
                {
                    macroLines.Add("/customize profile disable <me>");
                    macroLines.Add($"/customize profile enable <me>, {snapshotDetectedCustomizePlusProfile}");
                }

                // Always add penumbra redraw at the end
                macroLines.Add("/penumbra redraw self");
            }

            return string.Join("\n", macroLines);
        }

        private async Task<Guid> CreateGlamourerDesignFromCurrentState(string designName)
        {
            try
            {
                // Get current player's object index (usually 0 for local player)
                var playerIndex = 0;
                
                // First, get the current state data from Glamourer
                var glamourerStateIpc = Plugin.PluginInterface.GetIpcSubscriber<int, uint, (int, string?)>("Glamourer.GetStateBase64");
                var (stateError, stateData) = await Task.Run(() => glamourerStateIpc.InvokeFunc(playerIndex, 0));
                
                if (stateError != 0 || string.IsNullOrEmpty(stateData))
                {
                    Plugin.Log.Warning($"Failed to get Glamourer state for design creation (error: {stateError})");
                    return Guid.Empty;
                }
                
                // Create design from the state data
                var glamourerAddDesignIpc = Plugin.PluginInterface.GetIpcSubscriber<string, string, (int, Guid)>("Glamourer.AddDesign");
                var (addError, designId) = await Task.Run(() => glamourerAddDesignIpc.InvokeFunc(stateData, designName));
                
                if (addError == 0 && designId != Guid.Empty) // Success
                {
                    Plugin.Log.Information($"Created Glamourer design '{designName}' with ID {designId}");
                    return designId;
                }
                else
                {
                    Plugin.Log.Warning($"Failed to create Glamourer design (error: {addError})");
                    return Guid.Empty;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to create Glamourer design: {ex.Message}");
                return Guid.Empty;
            }
        }

        private async Task DetectGlamourerState()
        {
            try
            {
                snapshotDetectedMods.Clear();
                
                // Get current player's object index (usually 0 for local player)
                var playerIndex = 0;
                
                // Use real Glamourer IPC to get current state
                var glamourerStateIpc = Plugin.PluginInterface.GetIpcSubscriber<int, uint, (int, string?)>("Glamourer.GetStateBase64");
                var (errorCode, stateData) = await Task.Run(() => glamourerStateIpc.InvokeFunc(playerIndex, 0));
                
                if (errorCode == 0 && !string.IsNullOrEmpty(stateData)) // Success
                {
                    // We have a valid state, which means there are modifications
                    snapshotDetectedMods.Add("Current Glamourer State");
                    Plugin.Log.Information($"Glamourer detection completed: Active state detected");
                }
                else
                {
                    Plugin.Log.Information($"Glamourer detection completed: No modifications detected (error: {errorCode})");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"Failed to detect Glamourer state: {ex.Message}");
                snapshotDetectedMods.Clear();
            }
        }

        private async Task DetectCustomizePlusProfile()
        {
            try
            {
                // Get current player's object index (usually 0 for local player)
                var playerIndex = (ushort)0;
                
                // Use real Customize+ IPC to get active profile
                var customizePlusIpc = Plugin.PluginInterface.GetIpcSubscriber<ushort, (int, Guid?)>("CustomizePlus.Profile.GetActiveProfileIdOnCharacter");
                var (errorCode, profileId) = await Task.Run(() => customizePlusIpc.InvokeFunc(playerIndex));
                
                if (errorCode == 0 && profileId.HasValue && profileId.Value != Guid.Empty) // Success with profile
                {
                    // Get profile list to find the profile name
                    var profileListIpc = Plugin.PluginInterface.GetIpcSubscriber<(Guid, string, string, List<(string, ushort, byte, ushort)>, int, bool)[]>("CustomizePlus.Profile.GetList");
                    var profileList = await Task.Run(() => profileListIpc.InvokeFunc());
                    
                    // Find the active profile in the list
                    var activeProfile = profileList.FirstOrDefault(p => p.Item1 == profileId.Value);
                    
                    if (activeProfile.Item1 != Guid.Empty) // Found the profile
                    {
                        var profileName = activeProfile.Item2; // The Name field from IPCProfileDataTuple
                        
                        // If it's an empty name or default, treat as Character
                        if (string.IsNullOrWhiteSpace(profileName) || profileName == "Default")
                        {
                            profileName = "Character";
                        }
                        
                        snapshotDetectedCustomizePlusProfile = profileName;
                        Plugin.Log.Information($"Customize+ detection completed: Profile '{profileName}' active");
                    }
                    else
                    {
                        snapshotDetectedCustomizePlusProfile = "Character";
                        Plugin.Log.Information("Customize+ detection completed: Active profile not found in profile list");
                    }
                }
                else
                {
                    // No profile or error - assume Character default
                    snapshotDetectedCustomizePlusProfile = "Character";
                    Plugin.Log.Information($"Customize+ detection completed: Character profile active (error: {errorCode})");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"Failed to detect Customize+ profile: {ex.Message}");
                snapshotDetectedCustomizePlusProfile = "Character";
            }
        }

        private void CheckClipboardForImage()
        {
            try
            {
                // Clipboard operations need to be on STA thread
                var thread = new Thread(() =>
                {
                    try
                    {
                        // Check if clipboard contains image data
                        snapshotHasClipboardImage = System.Windows.Forms.Clipboard.ContainsImage();
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Warning($"Failed to check clipboard for image: {ex.Message}");
                        snapshotHasClipboardImage = false;
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"Failed to check clipboard for image: {ex.Message}");
                snapshotHasClipboardImage = false;
            }
        }

        private async Task<string> GetGlamourerDesignData()
        {
            try
            {
                // In real implementation, this would use Glamourer IPC to export current state
                await Task.Delay(200);
                
                // Example IPC call:
                // return await plugin.DalamudPluginInterface.GetIpcSubscriber<string>("Glamourer.ExportCurrentDesign").InvokeAsync();
                
                // Mock data for testing
                return "MockGlamourerDesignData_" + DateTime.Now.Ticks;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to get Glamourer design data: {ex}");
                return string.Empty;
            }
        }

        private async Task<string> GetCustomizePlusProfileData(string profileName)
        {
            try
            {
                // In real implementation, this would use Customize+ IPC to export profile
                await Task.Delay(200);
                
                // Example IPC call:
                // return await plugin.DalamudPluginInterface.GetIpcSubscriber<string>("CustomizePlus.ExportProfile").InvokeAsync(profileName);
                
                // Mock data for testing
                return $"MockCustomizePlusProfile_{profileName}_{DateTime.Now.Ticks}";
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to get Customize+ profile data: {ex}");
                return string.Empty;
            }
        }

        private async Task<string> SaveClipboardImageForDesign(Guid designId)
        {
            try
            {
                string imagePath = "";
                
                // Clipboard operations need to be on STA thread
                var thread = new Thread(() =>
                {
                    try
                    {
                        if (!System.Windows.Forms.Clipboard.ContainsImage())
                            return;

                        var image = System.Windows.Forms.Clipboard.GetImage();
                        if (image == null)
                            return;

                        // Create designs directory if it doesn't exist
                        var designsDir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "Designs");
                        Directory.CreateDirectory(designsDir);

                        // Save image with design ID as filename
                        imagePath = Path.Combine(designsDir, $"{designId}.png");
                        
                        using (var bitmap = new System.Drawing.Bitmap(image))
                        {
                            bitmap.Save(imagePath, ImageFormat.Png);
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error($"Failed to save clipboard image: {ex}");
                        imagePath = "";
                    }
                });
                
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                return imagePath;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to save clipboard image: {ex}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Sets up the snapshot state and creates a design from a chat command, using the same logic as the UI button
        /// </summary>
        public void SetupSnapshotFromCommand(Character character, string designName, bool useConflictResolution)
        {
            // Set up the snapshot state variables (same as OpenSnapshotDialog)
            snapshotTargetCharacter = character;
            snapshotDesignName = designName;
            snapshotUseConflictResolution = useConflictResolution;
            snapshotDetectedMods = new HashSet<string>();
            snapshotDetectedCustomizePlusProfile = "";
            snapshotHasClipboardImage = Clipboard.ContainsImage();
            snapshotIsProcessing = false;
            snapshotStatusMessage = "";

            // Start the detection and creation process (same as the UI button logic)
            Task.Run(async () =>
            {
                try
                {
                    // Run detection in parallel (same as UI)
                    var detectionTasks = new Task[]
                    {
                        DetectGlamourerState(),
                        DetectCustomizePlusProfile()
                    };

                    await Task.WhenAll(detectionTasks);
                    
                    // Create the design (same as clicking "Create Design" button)
                    CreateSnapshotDesign();
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"Error in snapshot creation from command: {ex}");
                    Plugin.ChatGui.PrintError($"[Character Select+] Failed to create snapshot design: {ex.Message}");
                }
            });
        }

        public void CreateSmartSnapshotFromCommand(Character character, bool useConflictResolution)
        {
            CreateSmartSnapshot(character, useConflictResolution);
        }

        private void CreateSmartSnapshot(Character character, bool useConflictResolution)
        {
            Task.Run(async () =>
            {
                try
                {
                    Plugin.Log.Information($"Starting smart snapshot for character '{character.Name}' with CR: {useConflictResolution}");

                    // Get the most recently created Glamourer design
                    var recentDesign = await GetMostRecentGlamourerDesign();
                    if (recentDesign == null)
                    {
                        Plugin.ChatGui.PrintError("[Character Select+] No recent Glamourer design found. Please create a design in Glamourer first or use the regular snapshot dialog.");
                        return;
                    }

                    Plugin.Log.Information($"Found recent Glamourer design: '{recentDesign.Value.Name}' created on {recentDesign.Value.CreationDate}");

                    // Set snapshot data using the recent design
                    snapshotTargetCharacter = character;
                    snapshotDesignName = recentDesign.Value.Name;
                    snapshotUseConflictResolution = useConflictResolution;
                    snapshotIsProcessing = true;

                    // Auto-detect current state
                    var detectionTasks = new Task[]
                    {
                        DetectGlamourerState(),
                        DetectCustomizePlusProfile(),
                        Task.Run(() => CheckClipboardForImage())
                    };

                    await Task.WhenAll(detectionTasks);

                    // Create the CS+ design with the Glamourer design field populated
                    CreateSmartSnapshotDesign(recentDesign.Value);

                    Plugin.ChatGui.Print($"[Character Select+] Smart snapshot created: '{recentDesign.Value.Name}' {(useConflictResolution ? "with" : "without")} CR");
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"Error in smart snapshot creation: {ex}");
                    Plugin.ChatGui.PrintError($"[Character Select+] Failed to create smart snapshot: {ex.Message}");
                }
            });
        }

        private async Task<(string Name, DateTimeOffset CreationDate, Guid Id)?> GetMostRecentGlamourerDesign()
        {
            try
            {
                // Get Glamourer API with correct IPC method names
                var glamourerApi = Plugin.PluginInterface.GetIpcSubscriber<Dictionary<Guid, string>>("Glamourer.GetDesignList.V2");
                var designsDict = await Task.Run(() => glamourerApi.InvokeFunc());

                if (designsDict == null || designsDict.Count == 0)
                    return null;

                var glamourerJObjectApi = Plugin.PluginInterface.GetIpcSubscriber<Guid, Newtonsoft.Json.Linq.JObject?>("Glamourer.GetDesignJObject");

                // Get design data with timestamps
                var designsWithTimestamps = new List<(string Name, DateTimeOffset CreationDate, Guid Id)>();

                foreach (var kvp in designsDict)
                {
                    try
                    {
                        var designJson = await Task.Run(() => glamourerJObjectApi.InvokeFunc(kvp.Key));
                        if (designJson != null)
                        {
                            var name = designJson["Name"]?.Value<string>() ?? kvp.Value;
                            var creationDate = designJson["CreationDate"]?.Value<DateTimeOffset>() ?? DateTimeOffset.MinValue;
                            
                            designsWithTimestamps.Add((name, creationDate, kvp.Key));
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Warning($"Failed to get timestamp for design {kvp.Key}: {ex.Message}");
                    }
                }

                // Return the most recently created design
                return designsWithTimestamps
                    .Where(d => d.CreationDate > DateTimeOffset.MinValue)
                    .OrderByDescending(d => d.CreationDate)
                    .FirstOrDefault();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to get recent Glamourer designs: {ex}");
                return null;
            }
        }

        private string GenerateSnapshotMacro(Character character, string glamourerDesign, string customizePlusProfile)
        {
            if (string.IsNullOrWhiteSpace(glamourerDesign))
                return "";

            string macro = $"/glamour apply {glamourerDesign} | self";

            // Conditionally include automation line
            if (plugin.Configuration.EnableAutomations)
            {
                string automationToUse = !string.IsNullOrWhiteSpace(character.CharacterAutomation)
                    ? character.CharacterAutomation
                    : "None";

                macro += $"\n/glamour automation enable {automationToUse}";
            }

            // Always disable Customize+ first
            macro += "\n/customize profile disable <me>";

            // Determine Customize+ profile
            string customizeProfileToUse = !string.IsNullOrWhiteSpace(customizePlusProfile)
                ? customizePlusProfile
                : !string.IsNullOrWhiteSpace(character.CustomizeProfile)
                    ? character.CustomizeProfile
                    : string.Empty;

            // Enable only if needed
            if (!string.IsNullOrWhiteSpace(customizeProfileToUse))
                macro += $"\n/customize profile enable <me>, {customizeProfileToUse}";

            // Redraw line
            macro += "\n/penumbra redraw self";

            return macro;
        }

        private void CreateSmartSnapshotDesign((string Name, DateTimeOffset CreationDate, Guid Id) recentDesign)
        {
            try
            {
                if (snapshotTargetCharacter == null)
                {
                    Plugin.Log.Error("No target character set for smart snapshot");
                    return;
                }

                Plugin.Log.Information($"Creating smart snapshot design for character '{snapshotTargetCharacter.Name}' using Glamourer design '{recentDesign.Name}'");

                // Generate the proper macro for the snapshot design
                string snapshotMacro = GenerateSnapshotMacro(snapshotTargetCharacter, recentDesign.Name, 
                    !string.IsNullOrEmpty(snapshotDetectedCustomizePlusProfile) && snapshotDetectedCustomizePlusProfile != "Character" 
                        ? snapshotDetectedCustomizePlusProfile 
                        : "");

                // Create new design based on detected character state
                var newDesign = new CharacterDesign(
                    name: recentDesign.Name,
                    macro: snapshotMacro,
                    isAdvancedMode: false,
                    advancedMacro: "",
                    glamourerDesign: recentDesign.Name, // Use the Glamourer design name
                    automation: "",
                    customizePlusProfile: !string.IsNullOrEmpty(snapshotDetectedCustomizePlusProfile) && snapshotDetectedCustomizePlusProfile != "Character" 
                        ? snapshotDetectedCustomizePlusProfile 
                        : ""
                );

                // Set CR mode if requested
                if (snapshotUseConflictResolution)
                {
                    // Get only gear/hair mods from Currently Affecting You tab (prevents body/sculpt/eye mods from being managed)
                    var allAffectingMods = plugin.PenumbraIntegration?.GetOnScreenTabMods();
                    var currentlyAffectingMods = new HashSet<string>();
                    
                    if (allAffectingMods != null)
                    {
                        foreach (var modDir in allAffectingMods)
                        {
                            try
                            {
                                // Get mod type from cache or determine it
                                ModType modType;
                                if (plugin.modCategorizationCache.ContainsKey(modDir))
                                {
                                    modType = plugin.modCategorizationCache[modDir];
                                }
                                else
                                {
                                    // Use the static method to determine mod type
                                    modType = SecretModeModWindow.DetermineModType(modDir, "", plugin);
                                    plugin.modCategorizationCache[modDir] = modType;
                                }

                                // Only include gear and hair mods (safe to toggle, won't break body/sculpt/eyes)
                                if (modType == ModType.Gear || modType == ModType.Hair)
                                {
                                    currentlyAffectingMods.Add(modDir);
                                }
                            }
                            catch (Exception ex)
                            {
                                Plugin.Log.Warning($"Failed to determine mod type for {modDir}: {ex.Message}");
                            }
                        }
                    }
                    if (currentlyAffectingMods != null && currentlyAffectingMods.Count > 0)
                    {
                        // Create mod state dictionary with all currently affecting mods enabled
                        newDesign.SecretModState = new Dictionary<string, bool>();
                        foreach (var modName in currentlyAffectingMods)
                        {
                            newDesign.SecretModState[modName] = true;
                        }
                        Plugin.Log.Information($"Smart snapshot detected {newDesign.SecretModState.Count} currently affecting mods for CR design");
                    }
                    else
                    {
                        Plugin.Log.Information("Smart snapshot: No currently affecting mods detected for CR design");
                    }
                }

                // Handle clipboard image if available
                if (snapshotHasClipboardImage)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            var imagePath = await SaveClipboardImageForDesign(Guid.NewGuid());
                            if (!string.IsNullOrEmpty(imagePath))
                            {
                                newDesign.PreviewImagePath = imagePath;
                                Plugin.Log.Information($"Saved clipboard image for smart snapshot: {imagePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.Warning($"Failed to save clipboard image for smart snapshot: {ex}");
                        }
                    });
                }

                // Add to character's designs
                snapshotTargetCharacter.Designs.Add(newDesign);

                // Save configuration
                plugin.Configuration.Save();

                Plugin.Log.Information($"Smart snapshot design '{newDesign.Name}' created successfully for character '{snapshotTargetCharacter.Name}'");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error creating smart snapshot design: {ex}");
                Plugin.ChatGui.PrintError($"[Character Select+] Failed to create smart snapshot design: {ex.Message}");
            }
            finally
            {
                snapshotIsProcessing = false;
            }
        }



        private void CloseSnapshotDialog()
        {
            isSnapshotDialogOpen = false;
            snapshotDesignName = "";
            snapshotUseConflictResolution = true;
            snapshotTargetCharacter = null;
            snapshotDetectedMods.Clear();
            snapshotDetectedCustomizePlusProfile = null;
            snapshotHasClipboardImage = false;
            snapshotIsProcessing = false;
            snapshotStatusMessage = "";
        }

        private bool IsDesignCurrentlyActive(Character character, CharacterDesign design)
        {
            // Only show active design for the currently active CS+ character
            var currentActiveCharacter = GetCurrentActiveCharacter();
            if (currentActiveCharacter == null || currentActiveCharacter.Name != character.Name)
                return false;

            if (plugin?.Configuration?.LastUsedDesignByCharacter == null)
                return false;

            if (!plugin.Configuration.LastUsedDesignByCharacter.TryGetValue(character.Name, out var lastUsedDesignName))
                return false;

            return design.Name.Equals(lastUsedDesignName, StringComparison.OrdinalIgnoreCase);
        }

        private Character? GetCurrentActiveCharacter()
        {
            // Use the same logic as the plugin uses to determine current character
            Character? currentCharacter = null;

            // Try player-specific mapping first
            if (Plugin.ClientState.LocalPlayer is { } player && player.HomeWorld.IsValid)
            {
                string localName = player.Name.TextValue;
                string worldName = player.HomeWorld.Value.Name.ToString();
                string fullKey = $"{localName}@{worldName}";
                
                if (plugin.Configuration.LastUsedCharacterByPlayer.TryGetValue(fullKey, out var lastUsedCharacterName))
                {
                    // lastUsedCharacterName is in format "CharacterName@WorldName", extract just the character name
                    var characterName = lastUsedCharacterName.Contains("@") ? lastUsedCharacterName.Split('@')[0] : lastUsedCharacterName;
                    currentCharacter = plugin.Characters.FirstOrDefault(c => c.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));
                }
            }

            // Fallback to global last used
            if (currentCharacter == null && !string.IsNullOrEmpty(plugin.Configuration.LastUsedCharacterKey))
            {
                currentCharacter = plugin.Characters.FirstOrDefault(c => c.Name.Equals(plugin.Configuration.LastUsedCharacterKey, StringComparison.OrdinalIgnoreCase));
            }

            return currentCharacter;
        }

        /// <summary>
        /// Performs quick update of gear and hair mods for the current design
        /// </summary>
        private void PerformQuickGearHairUpdate(Character character)
        {
            try
            {
                Plugin.Log.Information("Starting quick gear/hair update...");
                
                // Get all currently affecting mods using the existing method
                var allAffectingMods = plugin.PenumbraIntegration.GetCurrentlyAffectingMods();
                Plugin.Log.Information($"Found {allAffectingMods.Count} total affecting mods");
                
                if (!allAffectingMods.Any())
                {
                    Plugin.Log.Warning("No affecting mods detected for quick update");
                    return;
                }
                
                // Filter for gear and hair mods only
                var gearHairMods = new HashSet<string>();
                var modList = plugin.PenumbraIntegration.GetModList();
                
                foreach (var modDir in allAffectingMods)
                {
                    // Check if mod is in categorization cache
                    if (plugin.modCategorizationCache?.TryGetValue(modDir, out var modType) == true)
                    {
                        if (modType == CharacterSelectPlugin.Windows.ModType.Gear || 
                            modType == CharacterSelectPlugin.Windows.ModType.Hair)
                        {
                            gearHairMods.Add(modDir);
                            Plugin.Log.Debug($"✓ Included {modType} mod: {modDir}");
                        }
                        else
                        {
                            Plugin.Log.Debug($"✗ Excluded {modType} mod: {modDir}");
                        }
                    }
                    else if (modList.TryGetValue(modDir, out var modName))
                    {
                        // Not in cache, check by changed items
                        var changedItems = plugin.PenumbraIntegration.GetModChangedItems(modDir, modName);
                        if (IsGearMod(changedItems.Keys) || IsHairMod(changedItems.Keys))
                        {
                            gearHairMods.Add(modDir);
                            Plugin.Log.Debug($"✓ Included gear/hair mod by analysis: {modDir}");
                        }
                    }
                }
                
                Plugin.Log.Information($"Filtered to {gearHairMods.Count} gear/hair mods");
                
                if (!gearHairMods.Any())
                {
                    Plugin.Log.Information("No gear/hair mods currently affecting - nothing to update");
                    return;
                }
                
                // Create new mod state with only gear/hair mods enabled
                var newModState = new Dictionary<string, bool>();
                
                // Get existing mod state to preserve non-gear/hair selections
                Dictionary<string, bool> existingState = null;
                if (isNewDesign)
                {
                    existingState = temporaryDesignSecretModState ?? new Dictionary<string, bool>();
                }
                else if (!string.IsNullOrEmpty(originalDesignName))
                {
                    var currentDesign = character.Designs.FirstOrDefault(d => d.Name == originalDesignName);
                    existingState = currentDesign?.SecretModState ?? new Dictionary<string, bool>();
                }
                
                // Preserve existing non-gear/hair mod selections
                if (existingState != null)
                {
                    foreach (var (modDir, enabled) in existingState)
                    {
                        if (!gearHairMods.Contains(modDir))
                        {
                            newModState[modDir] = enabled; // Keep existing state for non-gear/hair mods
                        }
                    }
                }
                
                // Add the new gear/hair mods as enabled
                foreach (var modDir in gearHairMods)
                {
                    newModState[modDir] = true;
                }
                
                // Update the design's mod state
                if (isNewDesign)
                {
                    temporaryDesignSecretModState = newModState;
                    Plugin.Log.Information($"Updated temporary design state with {gearHairMods.Count} gear/hair mods");
                }
                else if (!string.IsNullOrEmpty(originalDesignName))
                {
                    var design = character.Designs.FirstOrDefault(d => d.Name == originalDesignName);
                    if (design != null)
                    {
                        design.SecretModState = newModState;
                        temporaryDesignSecretModState = newModState; // Keep temp state in sync
                        plugin.SaveConfiguration();
                        Plugin.Log.Information($"Updated design '{design.Name}' with {gearHairMods.Count} gear/hair mods");
                    }
                }
                
                Plugin.Log.Information("Quick gear/hair update completed successfully");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error during quick gear/hair update: {ex}");
            }
        }
        
        /// <summary>
        /// Check if a mod is a gear mod based on its changed items
        /// </summary>
        private bool IsGearMod(IEnumerable<string> changedItems)
        {
            foreach (var item in changedItems)
            {
                // Check for equipment-related items
                if (item.Contains("Equipment:", StringComparison.OrdinalIgnoreCase) ||
                    item.Contains("/equipment/", StringComparison.OrdinalIgnoreCase) ||
                    item.Contains("gear", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        
        /// <summary>
        /// Check if a mod is a hair mod based on its changed items
        /// </summary>
        private bool IsHairMod(IEnumerable<string> changedItems)
        {
            foreach (var item in changedItems)
            {
                // Check for hair-related customization items
                if (item.Contains("Hair", StringComparison.OrdinalIgnoreCase) && 
                    item.Contains("Customization:", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                
                // Check for hair file paths
                if (item.Contains("/hair/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

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
        private string editedDesignPreviewPath = "";
        private string advancedDesignMacroText = "";
        private string originalDesignName = "";
        private string? pendingDesignImagePath = null;

        // Design sorting
        private enum DesignSortType { Favorites, Alphabetical, Recent, Oldest, Manual }
        private DesignSortType currentDesignSort = DesignSortType.Alphabetical;

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
            var dpiScale = ImGui.GetIO().DisplayFramebufferScale.X;
            var uiScale = plugin.Configuration.UIScaleMultiplier;
            var totalScale = GetSafeScale(dpiScale * uiScale);

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

                    // Check if this invisible button is real...
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
                    // Force the main window to recalculate layout, consensually
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

            // Draw a subtle line at the left edge, so subtle you might not see it!
            drawList.AddLine(
                new Vector2(handleMin.X + 2 * totalScale, handleMin.Y + 10 * totalScale),
                new Vector2(handleMin.X + 2 * totalScale, handleMax.Y - 10 * totalScale),
                handleColor,
                2f * totalScale
            );

            // Draw resize grip dots when hovered, really grip them
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
            // Style, do you have it?
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.1f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.1f, 0.1f, 0.12f, 0.95f));
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
            float spacing = 5f * scale;

            
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

                if (ctrlHeld && shiftHeld)
                {
                    isSecretDesignMode = true;
                    AddNewDesign();
                    editedDesignMacro = GenerateSecretDesignMacro(character);
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
            if (ImGui.Button("\uf07b##AddFolder", new Vector2(buttonSize, buttonSize)))
                ImGui.OpenPopup("CreateFolderPopup");
            ImGui.PopFont();

            ImGui.PopStyleColor(4);

            DrawFolderCreationPopup(character, scale);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Add Folder");
            }

            // Close button
            ImGui.SameLine();
            float availableWidth = ImGui.GetContentRegionAvail().X;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availableWidth - buttonSize);

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.27f, 0.27f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.2f, 0.2f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.3f, 0.3f, 1f));

            if (ImGui.Button("×##CloseDesignPanel", new Vector2(buttonSize, buttonSize)))
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

                    // Style, I think I'm getting it!
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

            DrawPreviewImageField(scale);

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
                ImGui.TextUnformatted("Enter the name of the Glamourer design to apply to this character.\nMust be entered EXACTLY as it is named in Glamourer!\nNote: You can add additional designs later.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            ImGui.SetCursorPosX(10 * scale);
            ImGui.SetNextItemWidth(inputWidth);
            var oldGlam = editedGlamourerDesign;
            if (ImGui.InputText("##GlamourerDesign", ref editedGlamourerDesign, 100))
            {
                plugin.EditedGlamourerDesign = editedGlamourerDesign;

                if (!isAdvancedModeDesign)
                {
                    editedDesignMacro = isSecretDesignMode
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
                ImGui.TextUnformatted("Optional: Enter the name of a Glamourer automation to use with this design.\n⚠️ This must match the name of the automation EXACTLY.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            ImGui.SetCursorPosX(10 * scale);
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
                ImGui.TextUnformatted("Optional: Enter the name of a Customize+ profile to apply with this design.\nIf left blank, uses the character's profile or disables all profiles.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            ImGui.SetCursorPosX(10 * scale);
            ImGui.SetNextItemWidth(inputWidth);
            if (ImGui.InputText("##CustomizePlus", ref editedCustomizeProfile, 100))
            {
                // Update macro
                if (!isAdvancedModeDesign)
                {
                    editedDesignMacro = isSecretDesignMode
                        ? GenerateSecretDesignMacro(plugin.Characters[activeCharacterIndex])
                        : GenerateDesignMacro(plugin.Characters[activeCharacterIndex]);
                }
                else
                {
                    UpdateAdvancedMacroCustomize();
                }
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
            if (ImGui.Button("Choose Preview Image"))
            {
                SelectPreviewImage();
            }

            // Apply pending image path
            if (pendingDesignImagePath != null)
            {
                lock (this)
                {
                    editedDesignPreviewPath = pendingDesignImagePath;
                    pendingDesignImagePath = null;
                }
            }

            // Show current preview
            if (!string.IsNullOrEmpty(editedDesignPreviewPath) && File.Exists(editedDesignPreviewPath))
            {
                var texture = Plugin.TextureProvider.GetFromFile(editedDesignPreviewPath).GetWrapOrDefault();
                if (texture != null)
                {
                    float previewSize = 100f * scale;
                    ImGui.Image((ImTextureID)texture.Handle, new Vector2(previewSize, previewSize));
                }
            }
            else if (!string.IsNullOrEmpty(editedDesignPreviewPath))
            {
                ImGui.Text("Preview: " + Path.GetFileName(editedDesignPreviewPath));
            }

            ImGui.SameLine();
            if (ImGui.Button("Clear") && !string.IsNullOrEmpty(editedDesignPreviewPath))
            {
                editedDesignPreviewPath = "";
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
                    advancedDesignMacroText = EnsureProperDesignMacroStructure();
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

            // Save button stylist here, how can i help you today?
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.5f, 0.3f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.6f, 0.4f, 1.0f));

            if (!canSave)
                ImGui.BeginDisabled();

            if (ImGui.Button("Save Design", new Vector2(buttonWidth, buttonHeight)))
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

            // Cancel button styling - #stopcancebutton
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.2f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.3f, 0.3f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.4f, 0.4f, 1.0f));

            if (ImGui.Button("Cancel", new Vector2(buttonWidth, buttonHeight)))
            {
                CloseDesignEditor();
            }

            ImGui.PopStyleColor(3);
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
                    currentDesignSort = DesignSortType.Favorites;
                    SortDesigns(character);
                }
                if (ImGui.Selectable("Alphabetical", currentDesignSort == DesignSortType.Alphabetical))
                {
                    currentDesignSort = DesignSortType.Alphabetical;
                    SortDesigns(character);
                }
                if (ImGui.Selectable("Newest", currentDesignSort == DesignSortType.Recent))
                {
                    currentDesignSort = DesignSortType.Recent;
                    SortDesigns(character);
                }
                if (ImGui.Selectable("Oldest", currentDesignSort == DesignSortType.Oldest))
                {
                    currentDesignSort = DesignSortType.Oldest;
                    SortDesigns(character);
                }
                if (ImGui.Selectable("Manual", currentDesignSort == DesignSortType.Manual))
                {
                    currentDesignSort = DesignSortType.Manual;
                }
                ImGui.EndCombo();
            }
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

            // Child folders
            foreach (var child in character.DesignFolders
                     .Where(f => f.ParentFolderId == folder.Id)
                     .OrderBy(f => f.SortOrder))
            {
                ImGui.Indent(indentAmount);
                bool childWasHovered = false;
                DrawFolderItem(character, child, ref childWasHovered, scale);
                ImGui.Unindent(indentAmount);
            }

            foreach (var design in character.Designs
                     .Where(d => d.FolderId == folder.Id)
                     .OrderBy(d => d.SortOrder))
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

            // Favourite star
            ImGui.SetCursorScreenPos(new Vector2(x, rowMin.Y + (rowH - btnSize) / 2));
            string star = design.IsFavorite ? "★" : "☆";

            var starColor = design.IsFavorite
                ? new Vector4(1f, 0.8f, 0.2f, hovered ? 1f : 0.7f)
                : new Vector4(0.5f, 0.5f, 0.5f, hovered ? 0.8f : 0.4f);

            ImGui.PushStyleColor(ImGuiCol.Text, starColor);
            if (ImGui.Button($"{star}##{design.Name}", new Vector2(btnSize, btnSize)))
            {
                bool wasFavorite = design.IsFavorite;
                design.IsFavorite = !design.IsFavorite;

                // Trigger particle effect
                Vector2 effectPos = ImGui.GetItemRectMin() + ImGui.GetItemRectSize() / 2;
                string effectKey = $"{character.Name}_{design.Name}";
                if (!designFavoriteEffects.ContainsKey(effectKey))
                    designFavoriteEffects[effectKey] = new FavoriteSparkEffect();
                designFavoriteEffects[effectKey].Trigger(effectPos, design.IsFavorite);

                plugin.SaveConfiguration();
                SortDesigns(character);
            }
            ImGui.PopStyleColor();

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(design.IsFavorite ? "Remove from favourites" : "Add to favourites");

            x += btnSize + spacing;

            // Design name styling, can't be, won't be, stopped!
            float rightZone = hovered ? (3 * btnSize + 2 * spacing + pad) : 0; // Only show buttons on hover
            float availW = rowW - (x - rowMin.X) - rightZone - pad;

            ImGui.SetCursorScreenPos(new Vector2(x, rowMin.Y + (rowH - ImGui.GetTextLineHeight()) / 2));

            var name = design.Name;
            if (ImGui.CalcTextSize(name).X > availW)
                name = TruncateWithEllipsis(name, availW);

            // Design name
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1f));
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
                plugin.ExecuteMacro(design.Macro, character, design.Name);
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
                OpenEditDesignWindow(character, design);
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
                                var clone = new CharacterDesign(
                                    name: $"{design.Name} (Copy)",
                                    macro: design.Macro,
                                    isAdvancedMode: design.IsAdvancedMode,
                                    advancedMacro: design.AdvancedMacro,
                                    glamourerDesign: design.GlamourerDesign ?? "",
                                    automation: design.Automation ?? "",
                                    customizePlusProfile: design.CustomizePlusProfile ?? "",
                                    previewImagePath: design.PreviewImagePath ?? ""
                                );

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

            var windowSize = new Vector2(500 * scale, 200 * scale);
            ImGui.SetNextWindowSize(windowSize, ImGuiCond.FirstUseEver);

            if (ImGui.Begin("Advanced Macro Editor", ref isAdvancedModeWindowOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize))
            {
                ApplyScaledStyles(scale);

                ImGui.Text("Edit Design Macro Manually:");

                // Dark styling for the text editor
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.08f, 0.08f, 0.08f, 0.95f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));

                ImGui.InputTextMultiline("##AdvancedDesignMacroPopup", ref advancedDesignMacroText, 2000,
                    new Vector2(-1, -1), ImGuiInputTextFlags.AllowTabInput);

                ImGui.PopStyleColor(2);
                PopScaledStyles();
            }
            ImGui.End();

            if (!isAdvancedModeWindowOpen)
                isAdvancedModeDesign = false;
        }

        // Utility methods
        private void SelectPreviewImage()
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
                            openFileDialog.Title = "Select Design Preview Image";

                            if (openFileDialog.ShowDialog() == DialogResult.OK)
                            {
                                lock (this)
                                {
                                    pendingDesignImagePath = openFileDialog.FileName;
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
            editedDesignPreviewPath = design.PreviewImagePath ?? "";
            isAdvancedModeDesign = design.IsAdvancedMode;
            isAdvancedModeWindowOpen = design.IsAdvancedMode;
            advancedDesignMacroText = design.AdvancedMacro ?? "";
        }

        private void CloseDesignEditor()
        {
            isEditDesignWindowOpen = false;
            plugin.IsEditDesignWindowOpen = false;
            isAdvancedModeWindowOpen = false;
            isNewDesign = false;
            isSecretDesignMode = false;
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

                existingDesign.Macro = keepAdvanced
                    ? existingDesign.AdvancedMacro
                    : (isAdvancedModeDesign ? advancedDesignMacroText : GenerateDesignMacro(character));

                existingDesign.AdvancedMacro = isAdvancedModeDesign || keepAdvanced
                    ? advancedDesignMacroText
                    : "";

                existingDesign.IsAdvancedMode = isAdvancedModeDesign || keepAdvanced;
                existingDesign.Automation = editedAutomation;
                existingDesign.GlamourerDesign = editedGlamourerDesign;
                existingDesign.CustomizePlusProfile = editedCustomizeProfile;
                existingDesign.PreviewImagePath = editedDesignPreviewPath;
            }
            else
            {
                // Add new design
                character.Designs.Add(new CharacterDesign(
                    editedDesignName,
                    isAdvancedModeDesign ? advancedDesignMacroText : GenerateDesignMacro(character),
                    isAdvancedModeDesign,
                    isAdvancedModeDesign ? advancedDesignMacroText : "",
                    editedGlamourerDesign,
                    editedAutomation,
                    editedCustomizeProfile,
                    editedDesignPreviewPath
                )
                {
                    DateAdded = DateTime.UtcNow
                });
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

        private void SortDesigns(Character character)
        {
            if (currentDesignSort == DesignSortType.Manual)
                return;

            if (currentDesignSort == DesignSortType.Favorites)
            {
                character.Designs.Sort((a, b) =>
                {
                    int favCompare = b.IsFavorite.CompareTo(a.IsFavorite);
                    if (favCompare != 0) return favCompare;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            }
            else if (currentDesignSort == DesignSortType.Alphabetical)
            {
                character.Designs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            else if (currentDesignSort == DesignSortType.Recent)
            {
                character.Designs.Sort((a, b) => b.DateAdded.CompareTo(a.DateAdded));
            }
            else if (currentDesignSort == DesignSortType.Oldest)
            {
                character.Designs.Sort((a, b) => a.DateAdded.CompareTo(b.DateAdded));
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

            foreach (var f in character.DesignFolders.Where(f => f.ParentFolderId == null))
            {
                renderItems.Add((f.Name, true, f as object, DateTime.MinValue, f.SortOrder));
            }

            foreach (var d in character.Designs.Where(d => d.FolderId == null))
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

            // Bulk-tag lines
            sb.AppendLine($"/penumbra bulktag disable {collection} | gear");
            sb.AppendLine($"/penumbra bulktag disable {collection} | hair");
            sb.AppendLine($"/penumbra bulktag enable  {collection} | {design}");

            // Glamourer "no clothes" + design
            sb.AppendLine("/glamour apply no clothes | self");
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
                sb.AppendLine($"/penumbra bulktag disable {collection} | gear");
                sb.AppendLine($"/penumbra bulktag disable {collection} | hair");
                sb.AppendLine($"/penumbra bulktag enable {collection} | {glamourer}");
                sb.AppendLine("/glamour apply no clothes | self");
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
    }
}

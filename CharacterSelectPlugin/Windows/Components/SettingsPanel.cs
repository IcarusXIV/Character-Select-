using System;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using CharacterSelectPlugin.Windows.Styles;
using System.Collections.Generic;
using CharacterSelectPlugin.Managers;
using System.IO;
using System.Windows.Forms;
using System.Threading;

namespace CharacterSelectPlugin.Windows.Components
{
    public class SettingsPanel : IDisposable
    {
        private Plugin plugin;
        private UIStyles uiStyles;
        private MainWindow mainWindow;

        // Dynamic sizing
        private bool visualSettingsOpen = true;  // Default
        private bool automationSettingsOpen = false;
        private bool behaviorSettingsOpen = false;
        private bool honorificSettingsOpen = false;
        private bool mainCharacterSettingsOpen = false;
        private bool dialogueSettingsOpen = false;
        private bool nameSyncSettingsOpen = false;
        private bool characterAssignmentSettingsOpen = false;
        private bool jobAssignmentSettingsOpen = false;
        private bool conflictResolutionSettingsOpen = false;
        private bool backupSettingsOpen = false;
        private bool communitySettingsOpen = false;
        private string? pendingExpandSection = null; // Section to force-expand on next draw
        private int selectedBlockedUserIndex = -1;
        private string newRealCharacterBuffer = "";
        private string newCSCharacterBuffer = "";
        private bool newAssignmentUseDesign = false;
        private string newAssignmentDesignBuffer = "";
        private string editingAssignmentKey = "";
        private string editingAssignmentValue = "";
        private bool editingAssignmentUseDesign = false;
        private string editingAssignmentDesignBuffer = "";
        private string backupNameBuffer = "";
        private List<BackupFileInfo> availableBackups = new();
        private string lastBackupStatusMessage = "";
        private DateTime lastBackupStatusTime = DateTime.MinValue;
        private string? pendingImportPath = null;
        private bool isCapturingRevealKey = false;

        // Key capture for reveal names
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        // Common key codes and their display names
        private static readonly Dictionary<int, string> KeyNames = new()
        {
            { 0x08, "Backspace" }, { 0x09, "Tab" }, { 0x0D, "Enter" }, { 0x10, "Shift" },
            { 0x11, "Ctrl" }, { 0x12, "Alt" }, { 0x13, "Pause" }, { 0x14, "Caps Lock" },
            { 0x1B, "Escape" }, { 0x20, "Space" }, { 0x21, "Page Up" }, { 0x22, "Page Down" },
            { 0x23, "End" }, { 0x24, "Home" }, { 0x25, "Left" }, { 0x26, "Up" },
            { 0x27, "Right" }, { 0x28, "Down" }, { 0x2D, "Insert" }, { 0x2E, "Delete" },
            { 0x30, "0" }, { 0x31, "1" }, { 0x32, "2" }, { 0x33, "3" }, { 0x34, "4" },
            { 0x35, "5" }, { 0x36, "6" }, { 0x37, "7" }, { 0x38, "8" }, { 0x39, "9" },
            { 0x41, "A" }, { 0x42, "B" }, { 0x43, "C" }, { 0x44, "D" }, { 0x45, "E" },
            { 0x46, "F" }, { 0x47, "G" }, { 0x48, "H" }, { 0x49, "I" }, { 0x4A, "J" },
            { 0x4B, "K" }, { 0x4C, "L" }, { 0x4D, "M" }, { 0x4E, "N" }, { 0x4F, "O" },
            { 0x50, "P" }, { 0x51, "Q" }, { 0x52, "R" }, { 0x53, "S" }, { 0x54, "T" },
            { 0x55, "U" }, { 0x56, "V" }, { 0x57, "W" }, { 0x58, "X" }, { 0x59, "Y" },
            { 0x5A, "Z" }, { 0x60, "Numpad 0" }, { 0x61, "Numpad 1" }, { 0x62, "Numpad 2" },
            { 0x63, "Numpad 3" }, { 0x64, "Numpad 4" }, { 0x65, "Numpad 5" }, { 0x66, "Numpad 6" },
            { 0x67, "Numpad 7" }, { 0x68, "Numpad 8" }, { 0x69, "Numpad 9" },
            { 0x6A, "Numpad *" }, { 0x6B, "Numpad +" }, { 0x6D, "Numpad -" },
            { 0x6E, "Numpad ." }, { 0x6F, "Numpad /" },
            { 0x70, "F1" }, { 0x71, "F2" }, { 0x72, "F3" }, { 0x73, "F4" },
            { 0x74, "F5" }, { 0x75, "F6" }, { 0x76, "F7" }, { 0x77, "F8" },
            { 0x78, "F9" }, { 0x79, "F10" }, { 0x7A, "F11" }, { 0x7B, "F12" },
            { 0x90, "Num Lock" }, { 0x91, "Scroll Lock" },
            { 0xA0, "Left Shift" }, { 0xA1, "Right Shift" },
            { 0xA2, "Left Ctrl" }, { 0xA3, "Right Ctrl" },
            { 0xA4, "Left Alt" }, { 0xA5, "Right Alt" },
            { 0xBA, ";" }, { 0xBB, "=" }, { 0xBC, "," }, { 0xBD, "-" },
            { 0xBE, "." }, { 0xBF, "/" }, { 0xC0, "`" },
            { 0xDB, "[" }, { 0xDC, "\\" }, { 0xDD, "]" }, { 0xDE, "'" }
        };

        public SettingsPanel(Plugin plugin, UIStyles uiStyles, MainWindow mainWindow)
        {
            this.plugin = plugin;
            this.uiStyles = uiStyles;
            this.mainWindow = mainWindow;
        }

        public void Dispose()
        {
        }

        public void Draw()
        {
            if (!plugin.IsSettingsOpen)
                return;

            // Calculate dynamic height based on expanded sections
            var totalScale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);

            var windowWidth = 480f * totalScale;

            // Calculate height based on actual content
            float baseHeight = 120f * totalScale; // Header + padding
            float sectionHeaderHeight = 30f * totalScale; // Each collapsed section header
            float totalContentHeight = 0f;

            // Add heights for expanded sections
            if (visualSettingsOpen)
                totalContentHeight += 220f * totalScale;
            else
                totalContentHeight += sectionHeaderHeight;

            if (automationSettingsOpen)
                totalContentHeight += 80f * totalScale; // Warning + checkbox
            else
                totalContentHeight += sectionHeaderHeight;

            if (behaviorSettingsOpen)
                totalContentHeight += 150f * totalScale;
            else
                totalContentHeight += sectionHeaderHeight;

            if (mainCharacterSettingsOpen)
                totalContentHeight += 120f * totalScale;
            else
                totalContentHeight += sectionHeaderHeight;

            if (dialogueSettingsOpen)
                totalContentHeight += 200f * totalScale;
            else
                totalContentHeight += sectionHeaderHeight;

            if (characterAssignmentSettingsOpen)
                totalContentHeight += 250f * totalScale;
            else
                totalContentHeight += sectionHeaderHeight;

            if (conflictResolutionSettingsOpen)
                totalContentHeight += 180f * totalScale; // Warnings + checkbox + description
            else
                totalContentHeight += sectionHeaderHeight;

            if (backupSettingsOpen)
                totalContentHeight += 300f * totalScale; // Backup controls + status + file list
            else
                totalContentHeight += sectionHeaderHeight;

            var windowHeight = Math.Min(baseHeight + totalContentHeight, 700f * totalScale); // Cap at reasonable max
            var minHeight = 200f * totalScale; // Minimum height
            windowHeight = Math.Max(windowHeight, minHeight);

            // Center window
            var viewport = ImGui.GetMainViewport();
            var centerPos = new Vector2(
                viewport.Pos.X + (viewport.Size.X - windowWidth) * 0.5f,
                viewport.Pos.Y + (viewport.Size.Y - windowHeight) * 0.5f
            );

            ImGui.SetNextWindowPos(centerPos, ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(windowWidth, windowHeight), ImGuiCond.Always);

            bool isSettingsOpen = plugin.IsSettingsOpen;
            var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar;

            if (ImGui.Begin("Character Select+ Settings", ref isSettingsOpen, windowFlags))
            {
                if (!isSettingsOpen)
                    plugin.IsSettingsOpen = false;

                ApplyFixedStyles(totalScale);

                try
                {
                    DrawFixedSettingsContent();
                }
                finally
                {
                    ImGui.PopStyleVar(4);
                    ImGui.PopStyleColor(6);
                }
            }
            ImGui.End();
        }

        private void ApplyFixedStyles(float totalScale)
        {
            // Styling
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.1f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.1f, 0.1f, 0.12f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.95f, 0.95f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.16f, 0.16f, 0.2f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.22f, 0.22f, 0.28f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.28f, 0.28f, 0.35f, 1.0f));

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5.0f * totalScale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8.0f * totalScale);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8 * totalScale, 5 * totalScale));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6 * totalScale, 3 * totalScale));
        }

        private void DrawFixedSettingsContent()
        {
            var totalScale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);
            var contentWidth = ImGui.GetContentRegionAvail().X;
            var labelWidth = 140f * totalScale;
            var inputWidth = contentWidth - labelWidth - (20f * totalScale);

            // Header
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.85f, 0.95f, 1.0f));
            ImGui.Text("Customize your Character Select+ experience");
            ImGui.PopStyleColor();
            ImGui.Separator();
            ImGui.Spacing();

            // Scrollable content area for all settings
            if (ImGui.BeginChild("AllSettings", new Vector2(0, 0), false))
            {
                // Rainbow colour order: Red -> Orange -> Yellow -> Lime -> Green -> Cyan -> Blue -> Indigo -> Purple -> Pink

                // Visual Settings Section (Red)
                visualSettingsOpen = DrawModernCollapsingHeader("Visual Settings", new Vector4(1.0f, 0.35f, 0.35f, 1.0f), visualSettingsOpen, FeatureKeys.CustomTheme);
                if (visualSettingsOpen)
                {
                    DrawVisualSettings(labelWidth, inputWidth);
                }

                // Glamourer Automations Section (Orange)
                automationSettingsOpen = DrawModernCollapsingHeader("Glamourer Automations", new Vector4(1.0f, 0.6f, 0.2f, 1.0f), automationSettingsOpen);
                if (automationSettingsOpen)
                {
                    DrawAutomationSettings();
                }

                // Behavior Settings Section (Yellow)
                behaviorSettingsOpen = DrawModernCollapsingHeader("Behavior Settings", new Vector4(1.0f, 0.9f, 0.3f, 1.0f), behaviorSettingsOpen);
                if (behaviorSettingsOpen)
                {
                    DrawBehaviorSettings();
                }

                // Honorific Section (Lime/Yellow-Green)
                honorificSettingsOpen = DrawModernCollapsingHeader("Honorific", new Vector4(0.7f, 1.0f, 0.3f, 1.0f), honorificSettingsOpen, FeatureKeys.Honorific);
                if (honorificSettingsOpen)
                {
                    DrawHonorificSettings();
                }

                // Main Character Section (Green)
                mainCharacterSettingsOpen = DrawModernCollapsingHeader("Main Character", new Vector4(0.3f, 0.9f, 0.4f, 1.0f), mainCharacterSettingsOpen);
                if (mainCharacterSettingsOpen)
                {
                    DrawMainCharacterSettings(labelWidth, inputWidth);
                }

                // Character Assignments (Cyan)
                characterAssignmentSettingsOpen = DrawModernCollapsingHeader("Character Assignments", new Vector4(0.3f, 0.9f, 0.9f, 1.0f), characterAssignmentSettingsOpen);
                if (characterAssignmentSettingsOpen)
                {
                    DrawCharacterAssignmentSettings();
                }

                // Job Assignments (Teal)
                jobAssignmentSettingsOpen = DrawModernCollapsingHeader("Job Assignments", new Vector4(0.2f, 0.8f, 0.85f, 1.0f), jobAssignmentSettingsOpen, FeatureKeys.JobAssignments);
                if (jobAssignmentSettingsOpen)
                {
                    DrawJobAssignmentSettings();
                }

                // Immersive Dialogue (Blue)
                dialogueSettingsOpen = DrawModernCollapsingHeader("Immersive Dialogue", new Vector4(0.4f, 0.6f, 1.0f, 1.0f), dialogueSettingsOpen);
                if (dialogueSettingsOpen)
                {
                    DrawDialogueSettings();
                }

                // Name Sync (Indigo)
                nameSyncSettingsOpen = DrawModernCollapsingHeader("Name Sync", new Vector4(0.55f, 0.4f, 1.0f, 1.0f), nameSyncSettingsOpen, FeatureKeys.NameSync);
                if (nameSyncSettingsOpen)
                {
                    DrawNameSyncSettings();
                }

                // Conflict Resolution (Purple/Violet)
                conflictResolutionSettingsOpen = DrawModernCollapsingHeader("Conflict Resolution", new Vector4(0.8f, 0.4f, 1.0f, 1.0f), conflictResolutionSettingsOpen);
                if (conflictResolutionSettingsOpen)
                {
                    DrawConflictResolutionSettings();
                }

                // Backup & Restore (Pink/Magenta)
                backupSettingsOpen = DrawModernCollapsingHeader("Backup & Restore", new Vector4(1.0f, 0.45f, 0.7f, 1.0f), backupSettingsOpen);
                if (backupSettingsOpen)
                {
                    DrawBackupSettings();
                }
            }
            ImGui.EndChild();
        }

        private bool DrawModernCollapsingHeader(string title, Vector4 titleColor, bool currentState)
        {
            return DrawModernCollapsingHeader(title, titleColor, currentState, null);
        }

        private bool DrawModernCollapsingHeader(string title, Vector4 titleColor, bool currentState, string? featureKey)
        {
            var flags = currentState ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
            flags |= ImGuiTreeNodeFlags.SpanFullWidth;

            // Check if this section should be force-expanded (from ExpandSection call)
            if (pendingExpandSection == title)
            {
                ImGui.SetNextItemOpen(true);
                pendingExpandSection = null;
            }

            // Check if this feature has a NEW badge
            bool showBadge = !string.IsNullOrEmpty(featureKey) &&
                             !plugin.Configuration.SeenFeatures.Contains(featureKey);

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 1.0f, 1.0f)); // White text
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(titleColor.X * 0.6f, titleColor.Y * 0.6f, titleColor.Z * 0.6f, 0.7f)); // More vibrant
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(titleColor.X * 0.7f, titleColor.Y * 0.7f, titleColor.Z * 0.7f, 0.8f)); // More vibrant
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(titleColor.X * 0.8f, titleColor.Y * 0.8f, titleColor.Z * 0.8f, 0.9f)); // More vibrant

            bool isOpen = ImGui.CollapsingHeader(title, flags);

            // Draw "NEW" text if badge should show
            if (showBadge)
            {
                var headerMin = ImGui.GetItemRectMin();
                var headerMax = ImGui.GetItemRectMax();
                var drawList = ImGui.GetWindowDrawList();

                var newText = "NEW";
                var textSize = ImGui.CalcTextSize(newText);
                var textPos = new Vector2(headerMax.X - textSize.X - 10, headerMin.Y + (headerMax.Y - headerMin.Y - textSize.Y) / 2);

                // Simple bright white text
                var textColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
                drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(textColor), newText);
            }

            ImGui.PopStyleColor(4);

            // Mark feature as seen when section is expanded
            if (isOpen && !string.IsNullOrEmpty(featureKey) && !plugin.Configuration.SeenFeatures.Contains(featureKey))
            {
                plugin.Configuration.SeenFeatures.Add(featureKey);
                plugin.Configuration.Save();
            }

            if (isOpen)
            {
                ImGui.Spacing();
            }

            return isOpen;
        }

        private void DrawVisualSettings(float labelWidth, float inputWidth)
        {
            // Profile Image Scale
            DrawFixedSetting("Profile Image Scale:", labelWidth, inputWidth, () =>
            {
                float tempScale = plugin.ProfileImageScale;
                if (ImGui.SliderFloat("##ProfileImageScale", ref tempScale, 0.5f, 2.0f, "%.1f"))
                {
                    plugin.ProfileImageScale = tempScale;
                    plugin.SaveConfiguration();
                    // Force MainWindow layout invalidation
                    mainWindow.InvalidateLayout();
                    Plugin.Log.Debug($"[Settings] Profile Image Scale changed to {tempScale}");
                }
                DrawTooltip("Adjusts the size of character profile images in the grid.");
            });

            // Profiles Per Row
            DrawFixedSetting("Profiles Per Row:", labelWidth, inputWidth * 0.5f, () =>
            {
                int tempColumns = plugin.ProfileColumns;
                if (ImGui.InputInt("##ProfilesPerRow", ref tempColumns, 1, 1))
                {
                    tempColumns = Math.Clamp(tempColumns, 1, 6);
                    plugin.ProfileColumns = tempColumns;
                    plugin.SaveConfiguration();
                    // Force MainWindow layout invalidation
                    mainWindow.InvalidateLayout();
                    Plugin.Log.Debug($"[Settings] Profile Columns changed to {tempColumns}");
                }
                DrawTooltip("Number of character profiles to display per row.");
            });

            // Profile Spacing
            DrawFixedSetting("Profile Spacing:", labelWidth, inputWidth, () =>
            {
                float tempSpacing = plugin.ProfileSpacing;
                if (ImGui.SliderFloat("##ProfileSpacing", ref tempSpacing, 0.0f, 50.0f, "%.1f"))
                {
                    plugin.ProfileSpacing = tempSpacing;
                    plugin.SaveConfiguration();
                    // Force MainWindow layout invalidation
                    mainWindow.InvalidateLayout();
                    Plugin.Log.Debug($"[Settings] Profile Spacing changed to {tempSpacing}");
                }
                DrawTooltip("Spacing between character profile cards.");
            });


            // Sort Characters By
            DrawFixedSetting("Sort Characters By:", labelWidth, inputWidth, () =>
            {
                var currentSort = (Plugin.SortType)plugin.Configuration.CurrentSortIndex;
                if (ImGui.BeginCombo("##SortDropdown", currentSort.ToString()))
                {
                    if (ImGui.Selectable("Favourites", currentSort == Plugin.SortType.Favorites))
                    {
                        plugin.Configuration.CurrentSortIndex = (int)Plugin.SortType.Favorites;
                        plugin.Configuration.Save();
                        mainWindow.UpdateSortType();
                    }
                    if (ImGui.Selectable("Alphabetical", currentSort == Plugin.SortType.Alphabetical))
                    {
                        plugin.Configuration.CurrentSortIndex = (int)Plugin.SortType.Alphabetical;
                        plugin.Configuration.Save();
                        mainWindow.UpdateSortType();
                    }
                    if (ImGui.Selectable("Most Recent", currentSort == Plugin.SortType.Recent))
                    {
                        plugin.Configuration.CurrentSortIndex = (int)Plugin.SortType.Recent;
                        plugin.Configuration.Save();
                        mainWindow.UpdateSortType();
                    }
                    if (ImGui.Selectable("Oldest", currentSort == Plugin.SortType.Oldest))
                    {
                        plugin.Configuration.CurrentSortIndex = (int)Plugin.SortType.Oldest;
                        plugin.Configuration.Save();
                        mainWindow.UpdateSortType();
                    }
                    if (ImGui.Selectable("Manual", currentSort == Plugin.SortType.Manual))
                    {
                        plugin.Configuration.CurrentSortIndex = (int)Plugin.SortType.Manual;
                        plugin.Configuration.Save();
                        mainWindow.UpdateSortType();
                    }
                    ImGui.EndCombo();
                }
                DrawTooltip("Choose how characters are sorted in the main grid.");
            });

            // Character Hover Effects
            bool enableHoverEffects = plugin.Configuration.EnableCharacterHoverEffects;
            if (ImGui.Checkbox("Character Hover Effects", ref enableHoverEffects))
            {
                plugin.Configuration.EnableCharacterHoverEffects = enableHoverEffects;
                plugin.SaveConfiguration();
            }
            DrawTooltip("Characters grow slightly when hovered over for visual feedback.");

            // Custom layout for Theme
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Theme:");
            ImGui.SameLine(labelWidth);
            ImGui.SetNextItemWidth(inputWidth);

            // Theme dropdown content
            {
                var currentSelection = plugin.Configuration.SelectedTheme;
                var activePresetName = plugin.Configuration.ActivePresetName;
                var presets = plugin.Configuration.ThemePresets;

                // Build display name - show preset name if Custom + preset is active
                string displayName;
                if (currentSelection == ThemeSelection.Custom && !string.IsNullOrEmpty(activePresetName))
                {
                    displayName = $"Custom: {activePresetName}";
                }
                else
                {
                    displayName = SeasonalThemeManager.GetThemeSelectionDisplayName(currentSelection);
                }

                if (ImGui.BeginCombo("##ThemeDropdown", displayName))
                {
                    // Built-in themes
                    foreach (ThemeSelection theme in Enum.GetValues<ThemeSelection>())
                    {
                        var themeDisplayName = SeasonalThemeManager.GetThemeSelectionDisplayName(theme);
                        var description = SeasonalThemeManager.GetThemeSelectionDescription(theme);

                        // For Custom, show as "Custom (New)" if no preset is active
                        if (theme == ThemeSelection.Custom)
                        {
                            themeDisplayName = "Custom (New)";
                        }

                        bool isSelected = currentSelection == theme &&
                            (theme != ThemeSelection.Custom || string.IsNullOrEmpty(activePresetName));

                        if (ImGui.Selectable(themeDisplayName, isSelected))
                        {
                            plugin.Configuration.SelectedTheme = theme;
                            if (theme == ThemeSelection.Custom)
                            {
                                // Reset CustomTheme to clean defaults when selecting "Custom (New)"
                                plugin.Configuration.ActivePresetName = null;
                                var customTheme = plugin.Configuration.CustomTheme;
                                customTheme.ColorOverrides.Clear();
                                customTheme.BackgroundImagePath = null;
                                customTheme.BackgroundImageOpacity = 0.3f;
                                customTheme.BackgroundImageZoom = 1.0f;
                                customTheme.BackgroundImageOffsetX = 0f;
                                customTheme.BackgroundImageOffsetY = 0f;
                                customTheme.FavoriteIconId = 0;
                                customTheme.UseNameplateColorForCardGlow = true;
                            }
                            plugin.Configuration.Save();

                            // Legacy migration: sync with old setting for compatibility
                            plugin.Configuration.UseSeasonalTheme = (theme == ThemeSelection.Current);
                        }

                        // Only show tooltip for Current Season option
                        if (ImGui.IsItemHovered() && theme == ThemeSelection.Current)
                        {
                            ImGui.SetTooltip(description);
                        }
                    }

                    // Separator before presets
                    if (presets.Count > 0)
                    {
                        ImGui.Separator();
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.7f, 1.0f));
                        ImGui.Text("Saved Presets:");
                        ImGui.PopStyleColor();

                        // Saved presets
                        foreach (var preset in presets)
                        {
                            bool isPresetSelected = currentSelection == ThemeSelection.Custom &&
                                preset.Name == activePresetName;

                            if (ImGui.Selectable($"  {preset.Name}", isPresetSelected))
                            {
                                plugin.Configuration.SelectedTheme = ThemeSelection.Custom;
                                plugin.Configuration.CustomTheme.CopyFrom(preset.Config);
                                plugin.Configuration.ActivePresetName = preset.Name;
                                plugin.Configuration.Save();
                            }

                            if (ImGui.IsItemHovered())
                            {
                                ImGui.SetTooltip($"Load preset: {preset.Name}");
                            }
                        }
                    }
                    ImGui.EndCombo();
                }

                // Only show tooltip for the Theme dropdown if Current Season is selected
                if (currentSelection == ThemeSelection.Current)
                {
                    var currentTheme = SeasonalThemeManager.GetCurrentSeasonalTheme();
                    var seasonDescription = $"Currently auto-applying: {SeasonalThemeManager.GetThemeDisplayNameSafe(currentTheme)}";
                    DrawTooltip(seasonDescription);
                }

            }
            ImGui.Spacing();

            // Show custom theme editor when Custom theme is selected
            if (plugin.Configuration.SelectedTheme == ThemeSelection.Custom)
            {
                ImGui.Spacing();
                DrawCustomThemeEditor();
            }

            ImGui.Spacing();
        }

        private void DrawHonorificSettings()
        {
            // Important setup info
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.85f, 0.4f, 1.0f));
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf071"); // Warning icon
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.Text("Note");
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.7f, 1.0f));
            ImGui.TextWrapped("Animated title glows (Wave, Pulse, Static) require the corresponding option to be enabled in Honorific's plugin settings as well.");
            ImGui.PopStyleColor();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Animated Gradients - supporter acknowledgment
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
            ImGui.Text("Animated Gradients");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.8f, 1.0f));
            ImGui.TextWrapped("The animated gradient feature (Wave, Pulse, Static) in Honorific titles was created by Caraxi. If you'd like to use these features, please consider supporting their work.");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            // Ko-Fi support button
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.98f, 0.38f, 0.38f, 1.0f)); // Ko-Fi red
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1.0f, 0.5f, 0.5f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.85f, 0.3f, 0.3f, 1.0f));
            if (ImGui.Button("Support Caraxi on Ko-Fi"))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://ko-fi.com/Caraxi",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
            ImGui.PopStyleColor(3);
            DrawTooltip("Opens Caraxi's Ko-Fi page in your browser.");

            ImGui.Spacing();

            // Supporter acknowledgment checkbox
            bool hasAcknowledged = plugin.Configuration.HasAcknowledgedHonorificSupport;
            if (ImGui.Checkbox("I have supported Caraxi (enable animated gradients)", ref hasAcknowledged))
            {
                plugin.Configuration.HasAcknowledgedHonorificSupport = hasAcknowledged;
                plugin.Configuration.Save();
            }
            DrawTooltip("Check this box to enable animated gradient features (Wave, Pulse, Static) in character Honorific titles.\nThis is an honor-based system - please support the developer if you use these features.");

            if (!hasAcknowledged)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.65f, 1.0f));
                ImGui.TextWrapped("Animated gradients are currently disabled. Enable the checkbox above to use Wave, Pulse, and Static title animations.");
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.9f, 0.5f, 1.0f));
                ImGui.Text("Animated gradients enabled. Thank you for supporting Caraxi!");
                ImGui.PopStyleColor();
            }

            ImGui.Spacing();
        }

        private void DrawAutomationSettings()
        {
            // Warning
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.7f, 0.3f, 1f));
            ImGui.TextWrapped("Warning: Requires 'None' automation in Glamourer");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            bool automationToggle = plugin.Configuration.EnableAutomations;
            if (ImGui.Checkbox("Enable Automations", ref automationToggle))
            {
                plugin.Configuration.EnableAutomations = automationToggle;
                UpdateAutomationSettings(automationToggle);
            }
            DrawTooltip("Enable support for Glamourer Automations in Characters & Designs.\n\nWhen enabled, you'll be able to assign an Automation to each character & design.\nCharacters & Designs without automations will require a fallback Automation in Glamourer named: \"None\"\nYou also must enter your in-game character name in Glamourer next to \"Any World\" and Set to Character.");

            ImGui.Spacing();
        }

        private void DrawBehaviorSettings()
        {
            bool rememberMainWindow = plugin.Configuration.RememberMainWindowState;
            if (ImGui.Checkbox("Remember Main Window state on startup", ref rememberMainWindow))
            {
                plugin.Configuration.RememberMainWindowState = rememberMainWindow;
                plugin.Configuration.Save();
            }
            DrawTooltip("When enabled, the Main Window will automatically open on startup if it was open when you last closed the game or disabled the plugin.");

            bool enableCompactQuickSwitch = plugin.Configuration.QuickSwitchCompact;
            if (ImGui.Checkbox("Compact Quick Switch Bar", ref enableCompactQuickSwitch))
            {
                plugin.Configuration.QuickSwitchCompact = enableCompactQuickSwitch;
                plugin.Configuration.Save();
            }
            DrawTooltip("When enabled, the Quick Switch window will hide its title bar and frame, showing only the dropdowns and apply button.");

            bool enableAutoload = plugin.Configuration.EnableLastUsedCharacterAutoload;
            if (ImGui.Checkbox("Auto-Apply Last Used Character on Login", ref enableAutoload))
            {
                plugin.Configuration.EnableLastUsedCharacterAutoload = enableAutoload;
                plugin.Configuration.Save();
            }
            DrawTooltip("Automatically applies the last character you used when logging into the game.");

            // Design auto-reapplication setting - only show if character auto-reapplication is enabled
            if (enableAutoload)
            {
                ImGui.Indent(20f);
                bool enableDesignAutoload = plugin.Configuration.EnableLastUsedDesignAutoload;
                if (ImGui.Checkbox("Also Apply Last Used Design", ref enableDesignAutoload))
                {
                    plugin.Configuration.EnableLastUsedDesignAutoload = enableDesignAutoload;
                    plugin.Configuration.Save();
                }
                DrawTooltip("When enabled, also automatically applies the last design you used for that character when logging in.\nRequires 'Auto-Apply Last Used Character on Login' to be enabled.");
                ImGui.Unindent(20f);
            }

            bool applyIdle = plugin.Configuration.ApplyIdleOnLogin;
            if (ImGui.Checkbox("Apply idle pose on login", ref applyIdle))
            {
                plugin.Configuration.ApplyIdleOnLogin = applyIdle;
                plugin.Configuration.Save();
            }
            DrawTooltip("Automatically applies your idle pose after logging in. Disable if you're seeing pose bugs.");

            bool reapplyDesign = plugin.Configuration.ReapplyDesignOnJobChange;
            if (ImGui.Checkbox("Reapply last design on job change", ref reapplyDesign))
            {
                plugin.Configuration.ReapplyDesignOnJobChange = reapplyDesign;
                plugin.Configuration.Save();
            }
            DrawTooltip("If checked, Character Select+ will reapply the last used design when you switch jobs.");

            bool randomFavoritesOnly = plugin.Configuration.RandomSelectionFavoritesOnly;
            if (ImGui.Checkbox("Random Selection: Favourites Only", ref randomFavoritesOnly))
            {
                plugin.Configuration.RandomSelectionFavoritesOnly = randomFavoritesOnly;
                plugin.Configuration.Save();
            }
            DrawTooltip("When enabled, random selection will only choose from favourited characters and designs.\nRequires at least one favourited character to work.");

            bool showRandomChatMessages = plugin.Configuration.ShowRandomSelectionChatMessages;
            if (ImGui.Checkbox("Show Random Selection Chat Messages", ref showRandomChatMessages))
            {
                plugin.Configuration.ShowRandomSelectionChatMessages = showRandomChatMessages;
                plugin.Configuration.Save();
            }
            DrawTooltip("When enabled, displays themed chat messages when using random selection.\nMessages become spooky during Halloween season!");

            // Community & Moderation section (merged)
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var totalScale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);

            // Context Menu Options
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
            ImGui.Text("Context Menu Options");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            // View RP Profile toggle
            bool showViewRP = plugin.Configuration.ShowViewRPContextMenu;
            if (ImGui.Checkbox("Show 'View RP Profile' in context menu", ref showViewRP))
            {
                plugin.Configuration.ShowViewRPContextMenu = showViewRP;
                plugin.Configuration.Save();
            }
            DrawTooltip("When enabled, right-clicking players shows a 'View RP Profile' option.\nThis allows you to view other CS+ users' RP profiles.");

            // Block User toggle
            bool showBlock = plugin.Configuration.ShowBlockUserContextMenu;
            if (ImGui.Checkbox("Show 'Block CS+ User' in context menu", ref showBlock))
            {
                plugin.Configuration.ShowBlockUserContextMenu = showBlock;
                plugin.Configuration.Save();
            }
            DrawTooltip("When enabled, right-clicking CS+ users shows a 'Block CS+ User' option.\nBlocked users' CS+ names won't be displayed to you.");

            // Report User toggle
            bool showReport = plugin.Configuration.ShowReportUserContextMenu;
            if (ImGui.Checkbox("Show 'Report CS+ Name' in context menu", ref showReport))
            {
                plugin.Configuration.ShowReportUserContextMenu = showReport;
                plugin.Configuration.Save();
            }
            DrawTooltip("When enabled, right-clicking CS+ users shows a 'Report CS+ Name' option.\nUse this to report offensive CS+ names to moderators.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Blocked Users List
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
            ImGui.Text($"Blocked Users ({plugin.Configuration.BlockedCSUsers.Count})");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            if (plugin.Configuration.BlockedCSUsers.Count > 0)
            {
                var listHeight = Math.Min(150f, plugin.Configuration.BlockedCSUsers.Count * 25f + 10f) * totalScale;
                if (ImGui.BeginChild("##BlockedUsersList", new Vector2(-1, listHeight), true))
                {
                    var blockedList = plugin.Configuration.BlockedCSUsers.ToList();
                    for (int i = 0; i < blockedList.Count; i++)
                    {
                        var blockedUser = blockedList[i];
                        bool selected = selectedBlockedUserIndex == i;

                        if (ImGui.Selectable($"{blockedUser}##blocked_{i}", ref selected))
                        {
                            selectedBlockedUserIndex = selected ? i : -1;
                        }

                        // Double-click to unblock
                        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(0))
                        {
                            plugin.Configuration.BlockedCSUsers.Remove(blockedUser);
                            plugin.Configuration.Save();
                            selectedBlockedUserIndex = -1;
                        }
                    }
                }
                ImGui.EndChild();

                // Unblock button
                if (selectedBlockedUserIndex >= 0 && selectedBlockedUserIndex < plugin.Configuration.BlockedCSUsers.Count)
                {
                    var blockedList = plugin.Configuration.BlockedCSUsers.ToList();
                    if (ImGui.Button("Unblock Selected", new Vector2(150f * totalScale, 0)))
                    {
                        plugin.Configuration.BlockedCSUsers.Remove(blockedList[selectedBlockedUserIndex]);
                        plugin.Configuration.Save();
                        selectedBlockedUserIndex = -1;
                    }
                    DrawTooltip("Remove the selected user from your block list.\nYou can also double-click a user to unblock them.");
                }
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1f));
                ImGui.Text("No blocked users");
                ImGui.PopStyleColor();
            }

            ImGui.Spacing();
        }

        private void DrawMainCharacterSettings(float labelWidth, float inputWidth)
        {
            bool enableMainCharacterOnly = plugin.Configuration.EnableMainCharacterOnly;
            if (ImGui.Checkbox("Enable Main Character Only Mode", ref enableMainCharacterOnly))
            {
                plugin.Configuration.EnableMainCharacterOnly = enableMainCharacterOnly;
                plugin.Configuration.Save();
            }
            DrawTooltip("When enabled, only your designated main character will auto-apply on login.\nIf no main character is set, the normal auto-apply behavior will be used.");

            bool showCrown = plugin.Configuration.ShowMainCharacterCrown;
            if (ImGui.Checkbox("Show Crown Icon on Main Character", ref showCrown))
            {
                plugin.Configuration.ShowMainCharacterCrown = showCrown;
                plugin.Configuration.Save();
            }
            DrawTooltip("When enabled, the main character will display a crown icon in the top corner of their image.");

            DrawFixedSetting("Select Main Character:", labelWidth, inputWidth, () =>
            {
                string currentMainChar = plugin.Configuration.MainCharacterName ?? "None";

                if (ImGui.BeginCombo("##MainCharacterDropdown", currentMainChar))
                {
                    if (ImGui.Selectable("None", currentMainChar == "None"))
                    {
                        plugin.Configuration.MainCharacterName = null;
                        plugin.Configuration.Save();
                    }

                    foreach (var character in plugin.Characters)
                    {
                        bool isSelected = character.Name == currentMainChar;
                        if (ImGui.Selectable(character.Name, isSelected))
                        {
                            plugin.Configuration.MainCharacterName = character.Name;
                            plugin.Configuration.Save();
                        }

                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
                DrawTooltip("Select which character should be designated as your main character.\nThe main character will be marked with a crown icon and can be set to auto-apply exclusively on login.");
            });

            // Status display
            if (!string.IsNullOrEmpty(plugin.Configuration.MainCharacterName))
            {
                var mainCharacter = plugin.Characters.FirstOrDefault(c => c.Name == plugin.Configuration.MainCharacterName);
                if (mainCharacter != null)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.9f, 0.7f, 1f));
                    ImGui.Text($"Current Main: {mainCharacter.Name}");
                    ImGui.PopStyleColor();
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.6f, 0.6f, 1f));
                    ImGui.Text("Main character not found");
                    ImGui.PopStyleColor();
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Clear"))
                    {
                        plugin.Configuration.MainCharacterName = null;
                        plugin.Configuration.Save();
                    }
                }
            }

            ImGui.Spacing();
        }

        private void DrawDialogueSettings()
        {
            // Warning
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.8f, 0.4f, 1f));
            ImGui.TextWrapped("Uses your CS+ Character's name and pronouns in NPC dialogue");
            ImGui.PopStyleColor();

            // Requirements
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.8f, 1f));
            ImGui.TextWrapped("Requires: Completed RP Profile (name & pronouns)");
            ImGui.PopStyleColor();

            // They/Them pronoun chat display warning
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.7f, 0.5f, 1f));
            ImGui.TextWrapped("Note: Users with They/Them pronouns may occasionally see garbled text in chat. Simply switch between chat tabs to refresh the display if this occurs.");
            ImGui.PopStyleColor();

            ImGui.Spacing();

            bool enableDialogue = plugin.Configuration.EnableDialogueIntegration;
            if (ImGui.Checkbox("Enable Immersive Dialogue", ref enableDialogue))
            {
                plugin.Configuration.EnableDialogueIntegration = enableDialogue;

                // Reset all dialogue sub-settings when disabled
                if (!enableDialogue)
                {
                    plugin.Configuration.EnableLuaHookDialogue = false;
                    plugin.Configuration.ReplaceNameInDialogue = false;
                    plugin.Configuration.ReplacePronounsInDialogue = false;
                    plugin.Configuration.ReplaceGenderedTerms = false;
                    plugin.Configuration.EnableAdvancedTitleReplacement = false;
                    plugin.Configuration.EnableSmartGrammarInDialogue = false;
                    plugin.Configuration.EnableRaceReplacement = false;
                    plugin.Configuration.ShowDialogueReplacementPreview = false; // Off by default
                }
                else
                {
                    // Set good defaults when enabling
                    plugin.Configuration.EnableLuaHookDialogue = true;
                    plugin.Configuration.ReplaceNameInDialogue = true;
                    plugin.Configuration.ReplacePronounsInDialogue = true;
                    plugin.Configuration.ReplaceGenderedTerms = true;
                    plugin.Configuration.EnableAdvancedTitleReplacement = true;
                    plugin.Configuration.EnableSmartGrammarInDialogue = true;
                    plugin.Configuration.EnableRaceReplacement = true;
                    plugin.Configuration.ShowDialogueReplacementPreview = false; // Keep off by default

                    // Initialize the processor on-demand (deferred from startup for performance)
                    plugin.EnsureDialogueProcessorInitialized();
                }

                plugin.Configuration.Save();
            }
            DrawTooltip("Replaces NPC dialogue text to use your CS+ Character's name and pronouns instead of your game character.\nRequires an active CS+ character with RP Profile data.");

            if (plugin.Configuration.EnableDialogueIntegration)
            {
                ImGui.Indent();

                // Simplified user-facing options
                bool replaceName = plugin.Configuration.ReplaceNameInDialogue;
                if (ImGui.Checkbox("Use CS+ Character Name", ref replaceName))
                {
                    plugin.Configuration.ReplaceNameInDialogue = replaceName;
                    plugin.Configuration.Save();
                }
                DrawTooltip("Replace your real character name with your CS+ character name in dialogue.");

                bool replacePronouns = plugin.Configuration.ReplacePronounsInDialogue;
                if (ImGui.Checkbox("Use CS+ Character Pronouns", ref replacePronouns))
                {
                    plugin.Configuration.ReplacePronounsInDialogue = replacePronouns;
                    plugin.Configuration.Save();
                }
                DrawTooltip("Replace pronouns in dialogue with your character's pronouns from their RP Profile.");

                bool replaceGenderedTerms = plugin.Configuration.ReplaceGenderedTerms;
                if (ImGui.Checkbox("Use Gender-Neutral Terms", ref replaceGenderedTerms))
                {
                    plugin.Configuration.ReplaceGenderedTerms = replaceGenderedTerms;
                    plugin.Configuration.Save();
                }
                DrawTooltip("Replace gendered terms like 'sir/lady', 'man/woman' with appropriate alternatives based on your character's pronouns.");

                //bool replaceRace = plugin.Configuration.EnableRaceReplacement;
                //if (ImGui.Checkbox("Use CS+ Character Race", ref replaceRace))
                //{
                //    plugin.Configuration.EnableRaceReplacement = replaceRace;
                //    plugin.Configuration.Save();
                //}
                //DrawTooltip("Replace your race with your CS+ character's race from their RP Profile.");

                // They/Them settings section
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
                ImGui.Text("They/Them Pronoun Settings");
                ImGui.PopStyleColor();
                ImGui.Spacing();

                // Use proper fixed layout like other settings
                var contentWidth = ImGui.GetContentRegionAvail().X;
                var labelWidth = 140f;
                var inputWidth = contentWidth - labelWidth - 20f;

                DrawFixedSetting("Neutral Title Style:", labelWidth, inputWidth, () =>
                {
                    var currentStyle = (int)plugin.Configuration.TheyThemStyle;
                    string[] styleOptions = { "Friend", "Mx.", "Traveler", "Adventurer", "Custom" };

                    if (ImGui.Combo("##TheyThemStyle", ref currentStyle, styleOptions, styleOptions.Length))
                    {
                        plugin.Configuration.TheyThemStyle = (Configuration.GenderNeutralStyle)currentStyle;
                        plugin.Configuration.Save();
                    }
                    DrawTooltip("Friend: \"honored sir\" → \"honored friend\"\nMx.: \"honored sir\" → \"honored Mx.\"\nTraveler: \"honored sir\" → \"honored traveler\"\nAdventurer: \"honored sir\" → \"honored adventurer\"");
                });

                if (plugin.Configuration.TheyThemStyle == Configuration.GenderNeutralStyle.Custom)
                {
                    DrawFixedSetting("Custom Title:", labelWidth, inputWidth, () =>
                    {
                        var customTitle = plugin.Configuration.CustomGenderNeutralTitle;
                        if (ImGui.InputText("##CustomGenderNeutral", ref customTitle, 50))
                        {
                            plugin.Configuration.CustomGenderNeutralTitle = customTitle;
                            plugin.Configuration.Save();
                        }
                        DrawTooltip("Enter your preferred gender-neutral title (e.g., \"Warrior\", \"Dhampion\", \"Canadian\")");
                    });
                }

                // Preview with proper styling
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
                var characterName = plugin.Characters.FirstOrDefault()?.Name ?? "Warrior of Light";
                ImGui.Text($"Preview: \"Sir {characterName}\" -> \"{plugin.Configuration.GetGenderNeutralFormalTitle()} {characterName}\"");
                ImGui.PopStyleColor();

                ImGui.Unindent();
            }

            ImGui.Spacing();
        }

        private void DrawNameSyncSettings()
        {
            // Mark feature as seen when this section is opened
            if (!plugin.Configuration.SeenFeatures.Contains(FeatureKeys.NameSync))
            {
                plugin.Configuration.SeenFeatures.Add(FeatureKeys.NameSync);
                plugin.Configuration.Save();
            }

            // Your Name section
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
            ImGui.Text("Your Name");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            // Simple glow option - always visible so users can enable it before the main toggle
            bool simpleGlow = plugin.Configuration.UseSimpleNameplateGlow;
            if (ImGui.Checkbox("Use simple glow (compatibility)", ref simpleGlow))
            {
                plugin.Configuration.UseSimpleNameplateGlow = simpleGlow;
                plugin.Configuration.Save();
            }
            DrawTooltip("Use a simple solid glow instead of the animated wave effect.\nEnable this FIRST if Name Sync causes crashes.");

            ImGui.Spacing();

            // Main toggle
            bool enableNameReplacement = plugin.Configuration.EnableNameReplacement;
            if (ImGui.Checkbox("Show my CS+ name to myself", ref enableNameReplacement))
            {
                plugin.Configuration.EnableNameReplacement = enableNameReplacement;

                // Set defaults when enabling
                if (enableNameReplacement)
                {
                    plugin.Configuration.NameReplacementNameplate = true;
                    plugin.Configuration.NameReplacementChat = true;
                    plugin.Configuration.NameReplacementPartyList = true;
                    // HideFCTagInNameplate stays at its current value (default false)

                    // Initialize the processor on-demand (deferred from startup for performance)
                    plugin.EnsurePlayerNameProcessorInitialized();
                }

                plugin.Configuration.Save();
            }
            DrawTooltip("Replace your in-game name with your CS+ character name in various UI elements.\nThis is client-side only - other players will not see this unless they also have CS+ and you've opted in.");

            // Sub-options (only show when main toggle enabled)
            if (plugin.Configuration.EnableNameReplacement)
            {
                ImGui.Indent(20f);

                // Nameplate sub-option
                bool nameplateEnabled = plugin.Configuration.NameReplacementNameplate;
                if (ImGui.Checkbox("Nameplate", ref nameplateEnabled))
                {
                    plugin.Configuration.NameReplacementNameplate = nameplateEnabled;
                    plugin.Configuration.Save();
                }
                DrawTooltip("Replace your nameplate above your character with your CS+ name.");

                // Chat sub-option
                bool chatEnabled = plugin.Configuration.NameReplacementChat;
                if (ImGui.Checkbox("Chat messages", ref chatEnabled))
                {
                    plugin.Configuration.NameReplacementChat = chatEnabled;
                    plugin.Configuration.Save();
                }
                DrawTooltip("Replace your name in chat message sender display.");

                // Party list sub-option
                bool partyListEnabled = plugin.Configuration.NameReplacementPartyList;
                if (ImGui.Checkbox("Party list", ref partyListEnabled))
                {
                    plugin.Configuration.NameReplacementPartyList = partyListEnabled;
                    plugin.Configuration.Save();
                }
                DrawTooltip("Replace your name in the party list.");

                // FC tag hiding (only relevant for nameplate)
                ImGui.Spacing();
                bool hideFCTag = plugin.Configuration.HideFCTagInNameplate;
                if (ImGui.Checkbox("Hide FC tag", ref hideFCTag))
                {
                    plugin.Configuration.HideFCTagInNameplate = hideFCTag;
                    plugin.Configuration.Save();
                }
                DrawTooltip("Hide your Free Company tag from your nameplate.\nOnly affects nameplate, not other UI elements.");

                ImGui.Unindent(20f);
            }

            // Sharing section
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
            ImGui.Text("Sharing");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            // Opt-in for others seeing your name
            bool allowOthers = plugin.Configuration.AllowOthersToSeeMyCSName;
            if (ImGui.Checkbox("Allow others to see my CS+ name", ref allowOthers))
            {
                plugin.Configuration.AllowOthersToSeeMyCSName = allowOthers;
                plugin.Configuration.Save();
            }
            DrawTooltip("When enabled, other CS+ users who have 'Show other CS+ users' names' turned on\nwill see your CS+ character name instead of your in-game name.\nRequires your profile to be set to 'Direct Sharing' or 'Public'.");

            // Show requirement note
            ImGui.Indent(24);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.7f, 1.0f));
            ImGui.TextWrapped("Requires RP Profile sharing set to 'Direct Sharing' or 'Public' (not Private).");
            ImGui.PopStyleColor();
            ImGui.Unindent(24);

            // Other users section
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
            ImGui.Text("Other CS+ Users");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            bool enableShared = plugin.Configuration.EnableSharedNameReplacement;
            if (ImGui.Checkbox("Show other CS+ users' names", ref enableShared))
            {
                plugin.Configuration.EnableSharedNameReplacement = enableShared;

                // Initialize the processor on-demand (deferred from startup for performance)
                if (enableShared)
                {
                    plugin.EnsurePlayerNameProcessorInitialized();
                }

                plugin.Configuration.Save();
            }
            DrawTooltip("See other CS+ users' character names instead of their in-game names.\nOnly shows for users who have opted in to share their name.\nThis is independent of self name replacement - you can use one without the other.");

            // Quick Reveal section
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
            ImGui.Text("Quick Reveal");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            bool enableRevealKeybind = plugin.Configuration.EnableRevealActualNamesKeybind;
            if (ImGui.Checkbox("Hold key to reveal actual names", ref enableRevealKeybind))
            {
                plugin.Configuration.EnableRevealActualNamesKeybind = enableRevealKeybind;
                plugin.Configuration.Save();
            }
            DrawTooltip("When enabled, hold the selected key to temporarily see actual in-game names\ninstead of CS+ names. Useful for checking who someone really is.");

            if (plugin.Configuration.EnableRevealActualNamesKeybind)
            {
                ImGui.Indent(20f);

                // Get current key display name
                string currentKeyName;
                if (plugin.Configuration.RevealActualNamesCustomKey > 0)
                {
                    currentKeyName = !string.IsNullOrEmpty(plugin.Configuration.RevealActualNamesCustomKeyName)
                        ? plugin.Configuration.RevealActualNamesCustomKeyName
                        : $"Key {plugin.Configuration.RevealActualNamesCustomKey}";
                }
                else
                {
                    currentKeyName = plugin.Configuration.RevealActualNamesKey switch
                    {
                        Configuration.RevealNamesKeyOption.Alt => "Alt",
                        Configuration.RevealNamesKeyOption.Ctrl => "Ctrl",
                        Configuration.RevealNamesKeyOption.Shift => "Shift",
                        _ => "Alt"
                    };
                }

                // Get current modifier display name
                string currentModifierName = plugin.Configuration.RevealActualNamesModifier switch
                {
                    0x11 => "Ctrl",
                    0x10 => "Shift",
                    0x12 => "Alt",
                    _ => "None"
                };

                // Modifier dropdown
                ImGui.Text("Keybind:");
                ImGui.SameLine();

                var modifierOptions = new[] { "None", "Ctrl", "Shift", "Alt" };
                int currentModifierIndex = plugin.Configuration.RevealActualNamesModifier switch
                {
                    0x11 => 1, // Ctrl
                    0x10 => 2, // Shift
                    0x12 => 3, // Alt
                    _ => 0     // None
                };

                ImGui.SetNextItemWidth(70f);
                if (ImGui.Combo("##RevealModifier", ref currentModifierIndex, modifierOptions, modifierOptions.Length))
                {
                    plugin.Configuration.RevealActualNamesModifier = currentModifierIndex switch
                    {
                        1 => 0x11, // Ctrl
                        2 => 0x10, // Shift
                        3 => 0x12, // Alt
                        _ => 0     // None
                    };
                    plugin.Configuration.RevealActualNamesModifierName = currentModifierIndex > 0 ? modifierOptions[currentModifierIndex] : "";
                    plugin.Configuration.Save();
                }

                // Plus sign between modifier and key
                if (currentModifierIndex > 0)
                {
                    ImGui.SameLine();
                    ImGui.Text("+");
                }

                ImGui.SameLine();

                // Key capture button
                if (isCapturingRevealKey)
                {
                    // Check for key press (skip modifier keys when capturing)
                    int? capturedKey = null;
                    foreach (var kvp in KeyNames)
                    {
                        // Skip modifier keys - they should be set via dropdown
                        if (kvp.Key == 0x10 || kvp.Key == 0x11 || kvp.Key == 0x12 ||
                            kvp.Key == 0xA0 || kvp.Key == 0xA1 || kvp.Key == 0xA2 ||
                            kvp.Key == 0xA3 || kvp.Key == 0xA4 || kvp.Key == 0xA5)
                            continue;

                        if ((GetAsyncKeyState(kvp.Key) & 0x8000) != 0)
                        {
                            capturedKey = kvp.Key;
                            break;
                        }
                    }

                    if (capturedKey.HasValue)
                    {
                        plugin.Configuration.RevealActualNamesCustomKey = capturedKey.Value;
                        plugin.Configuration.RevealActualNamesCustomKeyName = KeyNames.TryGetValue(capturedKey.Value, out var name) ? name : $"Key {capturedKey.Value}";
                        plugin.Configuration.Save();
                        isCapturingRevealKey = false;
                    }

                    // Flashing "Press a key..." button
                    float pulse = (float)(Math.Sin(ImGui.GetTime() * 6) * 0.5 + 0.5);
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f + pulse * 0.3f, 0.5f + pulse * 0.2f, 0.8f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f + pulse * 0.3f, 0.6f + pulse * 0.2f, 0.9f, 1.0f));
                    if (ImGui.Button("Press a key...", new Vector2(100f, 0f)))
                    {
                        isCapturingRevealKey = false;
                    }
                    ImGui.PopStyleColor(2);

                    ImGui.SameLine();
                    if (ImGui.SmallButton("Cancel"))
                    {
                        isCapturingRevealKey = false;
                    }
                }
                else
                {
                    // Normal button showing current key
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.25f, 0.3f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.35f, 0.35f, 0.45f, 1.0f));
                    if (ImGui.Button($"{currentKeyName}##RevealKey", new Vector2(100f, 0f)))
                    {
                        isCapturingRevealKey = true;
                    }
                    ImGui.PopStyleColor(2);
                }
                DrawTooltip("Set your keybind. Use the dropdown for a modifier (Ctrl/Shift/Alt) and click the button to set the main key.");

                ImGui.Unindent(20f);
            }

            ImGui.Spacing();
        }

        private void DrawCharacterAssignmentSettings()
        {
            // Warning if Auto-Apply Last Used Character is disabled
            if (!plugin.Configuration.EnableLastUsedCharacterAutoload)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.7f, 0.3f, 1f));

                // Warning icon
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf071"); // FontAwesome warning triangle
                ImGui.PopFont();

                ImGui.SameLine();
                ImGui.TextWrapped("Auto-Apply Last Used Character on Login is disabled - Character Assignments require this feature.");
                ImGui.PopStyleColor();

                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.8f, 1f));
                ImGui.TextWrapped("Enable Auto-Apply Last Used Character on Login in the Automation Settings section to use assignments.");
                ImGui.PopStyleColor();

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }

            // Warning if Main Character Only Mode is enabled
            if (plugin.Configuration.EnableMainCharacterOnly)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.7f, 0.3f, 1f));

                // Warning icon
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf071"); // FontAwesome warning triangle
                ImGui.PopFont();

                ImGui.SameLine();
                ImGui.TextWrapped("Main Character Only Mode is enabled - Character Assignments will be ignored.");
                ImGui.PopStyleColor();

                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.8f, 1f));
                ImGui.TextWrapped("Disable Main Character Only Mode in the Main Character section to use assignments.");
                ImGui.PopStyleColor();

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.9f, 1.0f, 1f));
            ImGui.TextWrapped("Assign specific CS+ Characters to auto-apply when logging into specific in-game characters.");
            ImGui.PopStyleColor();

            ImGui.Spacing();

            // Display current assignments
            if (plugin.Configuration.CharacterAssignments.Any())
            {
                ImGui.Text("Current Assignments:");
                ImGui.Spacing();

                var toRemove = new List<string>();

                foreach (var assignment in plugin.Configuration.CharacterAssignments.ToList())
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.9f, 0.7f, 1f));
                    ImGui.Text($"{assignment.Key}");
                    ImGui.PopStyleColor();

                    ImGui.SameLine();
                    ImGui.Text("→");
                    ImGui.SameLine();

                    // Parse and display the assignment value
                    if (assignment.Value == "None")
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.6f, 0.6f, 1f)); // Reddish for None
                        ImGui.Text("None (No Auto-Apply)");
                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        var (charName, designName) = ParseCharacterAssignmentValue(assignment.Value);
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.8f, 0.6f, 1f));
                        if (!string.IsNullOrEmpty(designName))
                        {
                            ImGui.Text($"{charName}");
                            ImGui.PopStyleColor();
                            ImGui.SameLine();
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.7f, 0.9f, 1f));
                            ImGui.Text($"({designName})");
                        }
                        else
                        {
                            ImGui.Text(charName);
                        }
                        ImGui.PopStyleColor();
                    }

                    // Add edit and remove buttons
                    ImGui.SameLine();
                    
                    // Edit button
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.6f, 0.8f, 0.6f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.7f, 0.9f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.8f, 1.0f, 1.0f));
                    
                    if (ImGui.SmallButton($"Edit##{assignment.Key}"))
                    {
                        editingAssignmentKey = assignment.Key;
                        var (charName, designName) = ParseCharacterAssignmentValue(assignment.Value);
                        editingAssignmentValue = charName;
                        editingAssignmentUseDesign = !string.IsNullOrEmpty(designName);
                        editingAssignmentDesignBuffer = designName ?? "";
                    }
                    ImGui.PopStyleColor(3);
                    
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Edit assignment for {assignment.Key}");
                    }
                    
                    ImGui.SameLine();
                    
                    // Remove button
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.3f, 0.3f, 0.6f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.4f, 0.4f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1.0f, 0.5f, 0.5f, 1.0f));
                    
                    if (ImGui.SmallButton($"Remove##{assignment.Key}"))
                    {
                        toRemove.Add(assignment.Key);
                    }
                    ImGui.PopStyleColor(3);
                    
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Remove assignment for {assignment.Key}");
                    }
                }

                foreach (var key in toRemove)
                {
                    plugin.Configuration.CharacterAssignments.Remove(key);
                    plugin.Configuration.Save();
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }

            // Edit assignment section
            if (!string.IsNullOrEmpty(editingAssignmentKey))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.8f, 0.6f, 1f));
                ImGui.Text($"Editing Assignment: {editingAssignmentKey}");
                ImGui.PopStyleColor();
                ImGui.Spacing();

                ImGui.Text("New CS+ Character:");
                ImGui.SetNextItemWidth(300f);
                if (ImGui.BeginCombo("##EditCSChar", string.IsNullOrEmpty(editingAssignmentValue) ? "Select CS+ Character" : editingAssignmentValue))
                {
                    // Add "None" option first
                    if (ImGui.Selectable("None", editingAssignmentValue == "None"))
                    {
                        editingAssignmentValue = "None";
                        editingAssignmentUseDesign = false;
                        editingAssignmentDesignBuffer = "";
                    }

                    // Add separator
                    ImGui.Separator();

                    // Add CS+ characters
                    foreach (var character in plugin.Configuration.Characters.OrderBy(c => c.Name))
                    {
                        bool isSelected = character.Name == editingAssignmentValue;
                        if (ImGui.Selectable(character.Name, isSelected))
                        {
                            if (editingAssignmentValue != character.Name)
                            {
                                editingAssignmentValue = character.Name;
                                editingAssignmentUseDesign = false;
                                editingAssignmentDesignBuffer = "";
                            }
                        }
                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }

                // Design selection (only show if a valid character is selected)
                var editSelectedChar = plugin.Configuration.Characters.FirstOrDefault(c => c.Name == editingAssignmentValue);
                if (editSelectedChar != null && editSelectedChar.Designs.Any())
                {
                    ImGui.Spacing();
                    if (ImGui.Checkbox("Use specific design##Edit", ref editingAssignmentUseDesign))
                    {
                        if (!editingAssignmentUseDesign)
                            editingAssignmentDesignBuffer = "";
                    }

                    if (editingAssignmentUseDesign)
                    {
                        ImGui.SetNextItemWidth(300f);
                        if (ImGui.BeginCombo("##EditDesign", string.IsNullOrEmpty(editingAssignmentDesignBuffer) ? "Select Design" : editingAssignmentDesignBuffer))
                        {
                            foreach (var design in editSelectedChar.Designs.OrderBy(d => d.Name))
                            {
                                bool isSelected = design.Name == editingAssignmentDesignBuffer;
                                if (ImGui.Selectable(design.Name, isSelected))
                                {
                                    editingAssignmentDesignBuffer = design.Name;
                                }
                                if (isSelected)
                                    ImGui.SetItemDefaultFocus();
                            }
                            ImGui.EndCombo();
                        }
                    }
                }

                ImGui.Spacing();

                // Save and Cancel buttons
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.8f, 0.3f, 0.6f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.9f, 0.4f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 1.0f, 0.5f, 1.0f));
                
                if (ImGui.Button("Save Changes"))
                {
                    string designToSave = editingAssignmentUseDesign ? editingAssignmentDesignBuffer : null;
                    plugin.Configuration.CharacterAssignments[editingAssignmentKey] = BuildCharacterAssignmentValue(editingAssignmentValue, designToSave);
                    plugin.Configuration.Save();
                    editingAssignmentKey = "";
                    editingAssignmentValue = "";
                    editingAssignmentUseDesign = false;
                    editingAssignmentDesignBuffer = "";
                }
                ImGui.PopStyleColor(3);

                ImGui.SameLine();

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.6f, 0.6f, 0.6f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.7f, 0.7f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
                
                if (ImGui.Button("Cancel"))
                {
                    editingAssignmentKey = "";
                    editingAssignmentValue = "";
                    editingAssignmentUseDesign = false;
                    editingAssignmentDesignBuffer = "";
                }
                ImGui.PopStyleColor(3);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }

            // Add new assignment section
            ImGui.Text("Add New Assignment:");
            ImGui.Spacing();

            // Get list of known real characters from existing tracking
            var knownRealCharacters = plugin.Configuration.LastUsedCharacterByPlayer.Keys.ToList();

            // Add current character if logged in and not already in list
            if (Plugin.ClientState.IsLoggedIn && Plugin.ClientState.LocalPlayer != null)
            {
                var player = Plugin.ClientState.LocalPlayer;
                if (player.HomeWorld.IsValid)
                {
                    string currentFormat = $"{player.Name.TextValue}@{player.HomeWorld.Value.Name}";
                    if (!knownRealCharacters.Contains(currentFormat))
                    {
                        knownRealCharacters.Insert(0, currentFormat);
                    }
                }
            }

            string newRealCharacter = newRealCharacterBuffer;
            string newCSCharacter = newCSCharacterBuffer;

            ImGui.Text("In-Game Character:");
            ImGui.SetNextItemWidth(300f);

            if (knownRealCharacters.Any())
            {
                // Dropdown of known characters
                if (ImGui.BeginCombo("##RealCharSelect", string.IsNullOrEmpty(newRealCharacter) ? "Select In-Game Character" : newRealCharacter))
                {
                    foreach (var realChar in knownRealCharacters.OrderBy(x => x))
                    {
                        bool isSelected = realChar == newRealCharacter;
                        if (ImGui.Selectable(realChar, isSelected))
                        {
                            newRealCharacterBuffer = realChar;
                        }
                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
                DrawTooltip("Select from characters the plugin has seen before, or type manually below.");

                // Manual input as backup
                ImGui.Text("Or enter manually:");
                ImGui.SetNextItemWidth(300f);
                if (ImGui.InputTextWithHint("##RealCharManual", "First Last@WorldName", ref newRealCharacter, 100))
                {
                    newRealCharacterBuffer = newRealCharacter;
                }
            }
            else
            {
                // Fallback to manual input if no known characters
                if (ImGui.InputTextWithHint("##RealChar", "First Last@WorldName", ref newRealCharacter, 100))
                {
                    newRealCharacterBuffer = newRealCharacter;
                }
                DrawTooltip("Enter the exact character name and world as it appears in-game.\nExample: James Stone@Hyperion");
            }

            ImGui.Spacing();

            ImGui.Text("CS+ Character:");
            ImGui.SetNextItemWidth(300f);
            if (ImGui.BeginCombo("##CSChar", string.IsNullOrEmpty(newCSCharacter) ? "Select CS+ Character" : newCSCharacter))
            {
                // Add "None" option first
                if (ImGui.Selectable("None", newCSCharacter == "None"))
                {
                    newCSCharacterBuffer = "None";
                    newAssignmentUseDesign = false;
                    newAssignmentDesignBuffer = "";
                }

                // Add separator
                ImGui.Separator();

                // Add all CS+ characters
                foreach (var character in plugin.Characters)
                {
                    if (ImGui.Selectable(character.Name, character.Name == newCSCharacter))
                    {
                        if (newCSCharacterBuffer != character.Name)
                        {
                            newCSCharacterBuffer = character.Name;
                            newAssignmentUseDesign = false;
                            newAssignmentDesignBuffer = "";
                        }
                    }
                }
                ImGui.EndCombo();
            }
            DrawTooltip("Choose which CS+ character should auto-apply for this in-game character.\nSelect 'None' to prevent any auto-application for this character.");

            // Design selection (only show if a valid character is selected)
            var newSelectedChar = plugin.Characters.FirstOrDefault(c => c.Name == newCSCharacterBuffer);
            if (newSelectedChar != null && newSelectedChar.Designs.Any())
            {
                ImGui.Spacing();
                if (ImGui.Checkbox("Use specific design##New", ref newAssignmentUseDesign))
                {
                    if (!newAssignmentUseDesign)
                        newAssignmentDesignBuffer = "";
                }

                if (newAssignmentUseDesign)
                {
                    ImGui.SetNextItemWidth(300f);
                    if (ImGui.BeginCombo("##NewDesign", string.IsNullOrEmpty(newAssignmentDesignBuffer) ? "Select Design" : newAssignmentDesignBuffer))
                    {
                        foreach (var design in newSelectedChar.Designs.OrderBy(d => d.Name))
                        {
                            bool isSelected = design.Name == newAssignmentDesignBuffer;
                            if (ImGui.Selectable(design.Name, isSelected))
                            {
                                newAssignmentDesignBuffer = design.Name;
                            }
                            if (isSelected)
                                ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }
                }
            }

            ImGui.Spacing();

            bool canAdd = !string.IsNullOrWhiteSpace(newRealCharacterBuffer) &&
                          !string.IsNullOrWhiteSpace(newCSCharacterBuffer) &&
                          !plugin.Configuration.CharacterAssignments.ContainsKey(newRealCharacterBuffer);

            if (!canAdd)
                ImGui.BeginDisabled();

            if (ImGui.Button("Add Assignment"))
            {
                string designToSave = newAssignmentUseDesign ? newAssignmentDesignBuffer : null;
                plugin.Configuration.CharacterAssignments[newRealCharacterBuffer] = BuildCharacterAssignmentValue(newCSCharacterBuffer, designToSave);
                plugin.Configuration.Save();
                Plugin.Log.Debug($"[CharacterAssignment] Added: {newRealCharacterBuffer} → {BuildCharacterAssignmentValue(newCSCharacterBuffer, designToSave)}");
                newRealCharacterBuffer = "";
                newCSCharacterBuffer = "";
                newAssignmentUseDesign = false;
                newAssignmentDesignBuffer = "";
            }

            if (!canAdd)
                ImGui.EndDisabled();

            if (!canAdd && !string.IsNullOrWhiteSpace(newRealCharacterBuffer) && plugin.Configuration.CharacterAssignments.ContainsKey(newRealCharacterBuffer))
            {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.6f, 0.6f, 1f));
                ImGui.Text("Assignment already exists");
                ImGui.PopStyleColor();
            }

            // Show helpful info if no known characters
            if (!knownRealCharacters.Any())
            {
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.6f, 1f));
                ImGui.TextWrapped("Tip: The plugin will remember character names after you log into them and use a CS+ character at least once.");
                ImGui.PopStyleColor();
            }

            ImGui.Spacing();
        }

        // Job assignment UI state
        private int newJobAssignmentType = 0; // 0 = Specific Job, 1 = Role
        private int newJobAssignmentJobIndex = 0;
        private int newJobAssignmentRoleIndex = 0;
        private int newJobAssignmentCharacterIndex = 0;
        private bool newJobAssignmentUseDesign = false;
        private int newJobAssignmentDesignIndex = 0;

        // Job data for UI
        private static readonly (uint Id, string Name, string Role)[] JobData = new[]
        {
            // Tanks
            (19u, "Paladin", "Tank"), (21u, "Warrior", "Tank"), (32u, "Dark Knight", "Tank"), (37u, "Gunbreaker", "Tank"),
            // Healers
            (24u, "White Mage", "Healer"), (28u, "Scholar", "Healer"), (33u, "Astrologian", "Healer"), (40u, "Sage", "Healer"),
            // Melee DPS
            (20u, "Monk", "Melee"), (22u, "Dragoon", "Melee"), (30u, "Ninja", "Melee"), (34u, "Samurai", "Melee"), (39u, "Reaper", "Melee"), (41u, "Viper", "Melee"),
            // Ranged Physical DPS
            (23u, "Bard", "Ranged"), (31u, "Machinist", "Ranged"), (38u, "Dancer", "Ranged"),
            // Caster DPS
            (25u, "Black Mage", "Caster"), (27u, "Summoner", "Caster"), (35u, "Red Mage", "Caster"), (42u, "Pictomancer", "Caster"),
            // Crafters
            (8u, "Carpenter", "Crafter"), (9u, "Blacksmith", "Crafter"), (10u, "Armorer", "Crafter"), (11u, "Goldsmith", "Crafter"),
            (12u, "Leatherworker", "Crafter"), (13u, "Weaver", "Crafter"), (14u, "Alchemist", "Crafter"), (15u, "Culinarian", "Crafter"),
            // Gatherers
            (16u, "Miner", "Gatherer"), (17u, "Botanist", "Gatherer"), (18u, "Fisher", "Gatherer")
        };

        private static readonly string[] RoleNames = new[] { "Tank", "Healer", "Melee", "Ranged", "Caster", "Crafter", "Gatherer" };

        private void DrawJobAssignmentSettings()
        {
            // Enable toggle for Job-based switching
            bool enableJobAssignments = plugin.Configuration.EnableJobAssignments;
            if (ImGui.Checkbox("Enable Job-Based Character Switching", ref enableJobAssignments))
            {
                plugin.Configuration.EnableJobAssignments = enableJobAssignments;
                plugin.Configuration.Save();
            }
            DrawTooltip("Automatically switch CS+ character/design when you change jobs in-game.\nJob-specific assignments take priority over role assignments.");

            // Warning about Glamourer Automations conflict
            if (enableJobAssignments)
            {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.7f, 0.3f, 1.0f));
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text(FontAwesomeIcon.ExclamationTriangle.ToIconString());
                ImGui.PopFont();
                ImGui.PopStyleColor();
                DrawTooltip("WARNING: This feature will conflict with Glamourer's Automations!\n\nBoth features trigger on job change and will fight each other.\nDisable Glamourer Automations if using this feature, or vice versa.");
            }

            // Enable toggle for Gearset assignments
            bool enableGearsetAssignments = plugin.Configuration.EnableGearsetAssignments;
            if (ImGui.Checkbox("Enable Gearset Assignments", ref enableGearsetAssignments))
            {
                plugin.Configuration.EnableGearsetAssignments = enableGearsetAssignments;
                plugin.Configuration.Save();
            }
            DrawTooltip("Allow assigning a gearset to each character/design.\nWhen applied, it will automatically switch to that gearset.\nConfigure gearsets in the Add/Edit Character or Design forms.");

            if (!enableJobAssignments && !enableGearsetAssignments)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.65f, 1.0f));
                ImGui.TextWrapped("Enable a feature above to configure job/gearset assignments.");
                ImGui.PopStyleColor();
                ImGui.Spacing();
                return;
            }

            // Only show job assignment UI if that feature is enabled
            if (!enableJobAssignments)
            {
                ImGui.Spacing();
                return;
            }

            ImGui.Spacing();

            // Info text
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.8f, 0.9f, 1.0f));
            ImGui.TextWrapped("Assign CS+ characters or designs to specific jobs or roles. When you switch to that job, CS+ will automatically apply the assigned character/design.");
            ImGui.PopStyleColor();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Current assignments list
            if (plugin.Configuration.JobAssignments.Count > 0)
            {
                ImGui.Text("Current Assignments:");
                ImGui.Spacing();

                string? keyToRemove = null;

                foreach (var kvp in plugin.Configuration.JobAssignments)
                {
                    // Parse the key
                    string displayKey;
                    if (kvp.Key.StartsWith("Job_"))
                    {
                        var jobIdStr = kvp.Key.Substring(4);
                        if (uint.TryParse(jobIdStr, out var jobId))
                        {
                            var jobInfo = JobData.FirstOrDefault(j => j.Id == jobId);
                            displayKey = jobInfo.Name ?? $"Job {jobId}";
                        }
                        else
                        {
                            displayKey = kvp.Key;
                        }
                    }
                    else if (kvp.Key.StartsWith("Role_"))
                    {
                        displayKey = $"Role: {kvp.Key.Substring(5)}";
                    }
                    else
                    {
                        displayKey = kvp.Key;
                    }

                    // Parse the value
                    var (charName, designName) = plugin.ParseJobAssignment(kvp.Value);
                    string displayValue = !string.IsNullOrEmpty(designName)
                        ? $"{charName} : {designName}"
                        : charName ?? "(Invalid)";

                    // Display row
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.7f, 1.0f));
                    ImGui.Text(displayKey);
                    ImGui.PopStyleColor();
                    ImGui.SameLine();
                    ImGui.Text("→");
                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.9f, 0.7f, 1.0f));
                    ImGui.Text(displayValue);
                    ImGui.PopStyleColor();

                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.3f, 0.3f, 0.6f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.4f, 0.4f, 0.8f));
                    if (ImGui.SmallButton($"Remove##{kvp.Key}"))
                    {
                        keyToRemove = kvp.Key;
                    }
                    ImGui.PopStyleColor(2);
                }

                if (keyToRemove != null)
                {
                    plugin.Configuration.JobAssignments.Remove(keyToRemove);
                    plugin.Configuration.Save();
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }

            // Add new assignment section
            ImGui.Text("Add New Assignment:");
            ImGui.Spacing();

            // Assignment type (Job or Role)
            ImGui.Text("Type:");
            ImGui.SameLine();
            ImGui.RadioButton("Specific Job", ref newJobAssignmentType, 0);
            ImGui.SameLine();
            ImGui.RadioButton("Job Role", ref newJobAssignmentType, 1);
            DrawTooltip("Specific Job: Triggers only for that exact job.\nJob Role: Triggers for all jobs in that role (e.g., all tanks).");

            ImGui.Spacing();

            // Job/Role selection
            if (newJobAssignmentType == 0)
            {
                // Job dropdown - grouped by role
                ImGui.Text("Job:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(200f);

                var jobNames = JobData.Select(j => $"{j.Name} ({j.Role})").ToArray();
                if (ImGui.Combo("##JobSelect", ref newJobAssignmentJobIndex, jobNames, jobNames.Length))
                {
                    // Selection changed
                }
            }
            else
            {
                // Role dropdown
                ImGui.Text("Role:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(200f);
                ImGui.Combo("##RoleSelect", ref newJobAssignmentRoleIndex, RoleNames, RoleNames.Length);
            }

            ImGui.Spacing();

            // Character selection
            var characterNames = plugin.Characters.Select(c => c.Name).ToArray();
            if (characterNames.Length == 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.7f, 0.4f, 1.0f));
                ImGui.TextWrapped("No CS+ characters found. Create a character first.");
                ImGui.PopStyleColor();
                ImGui.Spacing();
                return;
            }

            ImGui.Text("Character:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200f);
            if (ImGui.Combo("##CharacterSelect", ref newJobAssignmentCharacterIndex, characterNames, characterNames.Length))
            {
                // Reset design selection when character changes
                newJobAssignmentDesignIndex = 0;
                newJobAssignmentUseDesign = false;
            }

            // Ensure valid index
            if (newJobAssignmentCharacterIndex >= characterNames.Length)
                newJobAssignmentCharacterIndex = 0;

            ImGui.Spacing();

            // Design selection (optional)
            var selectedCharacter = plugin.Characters.ElementAtOrDefault(newJobAssignmentCharacterIndex);
            if (selectedCharacter != null && selectedCharacter.Designs.Count > 0)
            {
                ImGui.Checkbox("Use specific design", ref newJobAssignmentUseDesign);
                DrawTooltip("If checked, apply a specific design. Otherwise, just apply the character.");

                if (newJobAssignmentUseDesign)
                {
                    var designNames = selectedCharacter.Designs.Select(d => d.Name).ToArray();
                    ImGui.Text("Design:");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(200f);
                    ImGui.Combo("##DesignSelect", ref newJobAssignmentDesignIndex, designNames, designNames.Length);

                    if (newJobAssignmentDesignIndex >= designNames.Length)
                        newJobAssignmentDesignIndex = 0;
                }
            }

            ImGui.Spacing();

            // Add button
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.7f, 0.4f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.8f, 0.5f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.9f, 0.6f, 1.0f));

            if (ImGui.Button("+ Add Assignment"))
            {
                // Build the key
                string key;
                if (newJobAssignmentType == 0)
                {
                    // Specific job
                    var selectedJob = JobData.ElementAtOrDefault(newJobAssignmentJobIndex);
                    key = $"Job_{selectedJob.Id}";
                }
                else
                {
                    // Role
                    key = $"Role_{RoleNames[newJobAssignmentRoleIndex]}";
                }

                // Build the value
                string value;
                if (newJobAssignmentUseDesign && selectedCharacter != null)
                {
                    var design = selectedCharacter.Designs.ElementAtOrDefault(newJobAssignmentDesignIndex);
                    if (design != null)
                    {
                        value = $"Design:{selectedCharacter.Name}:{design.Name}";
                    }
                    else
                    {
                        value = $"Character:{selectedCharacter.Name}";
                    }
                }
                else if (selectedCharacter != null)
                {
                    value = $"Character:{selectedCharacter.Name}";
                }
                else
                {
                    value = "";
                }

                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                {
                    plugin.Configuration.JobAssignments[key] = value;
                    plugin.Configuration.Save();
                }
            }
            ImGui.PopStyleColor(3);

            ImGui.Spacing();

            // Info note
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.7f, 1.0f));
            ImGui.TextWrapped("Note: Job-specific assignments take priority over role assignments. If both 'Reapply Last Design on Job Change' and Job Assignments are enabled, Job Assignments are checked first.");
            ImGui.PopStyleColor();

            ImGui.Spacing();
        }

        private void DrawFixedSetting(string label, float labelWidth, float inputWidth, Action drawControl)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text(label);
            ImGui.SameLine(labelWidth);
            ImGui.SetNextItemWidth(inputWidth);
            drawControl();
            ImGui.Spacing();
        }

        private void UpdateAutomationSettings(bool enableAutomations)
        {
            bool changed = false;

            // Character-level Automation Handling
            foreach (var character in plugin.Characters)
            {
                if (!enableAutomations)
                {
                    character.CharacterAutomation = string.Empty;
                }
                else if (string.IsNullOrWhiteSpace(character.CharacterAutomation))
                {
                    character.CharacterAutomation = "None";
                }
            }

            if (!enableAutomations)
            {
                // Remove automation lines from all macros
                foreach (var character in plugin.Characters)
                {
                    foreach (var design in character.Designs)
                    {
                        string macro = design.IsAdvancedMode ? design.AdvancedMacro : design.Macro;
                        if (string.IsNullOrWhiteSpace(macro))
                            continue;

                        var cleaned = string.Join("\n", macro
                            .Split('\n')
                            .Where(line => !line.TrimStart().StartsWith("/glamour automation enable", StringComparison.OrdinalIgnoreCase))
                            .Select(line => line.TrimEnd()));

                        if (design.IsAdvancedMode && cleaned != design.AdvancedMacro)
                        {
                            design.AdvancedMacro = cleaned;
                            changed = true;
                        }
                        else if (!design.IsAdvancedMode && cleaned != design.Macro)
                        {
                            design.Macro = cleaned;
                            changed = true;
                        }
                    }
                }

                foreach (var character in plugin.Characters)
                {
                    if (string.IsNullOrWhiteSpace(character.Macros))
                        continue;

                    var cleaned = string.Join("\n", character.Macros
                        .Split('\n')
                        .Where(line => !line.TrimStart().StartsWith("/glamour automation enable", StringComparison.OrdinalIgnoreCase))
                        .Select(line => line.TrimEnd()));

                    if (cleaned != character.Macros)
                    {
                        character.Macros = cleaned;
                        changed = true;
                    }
                }
            }
            else
            {
                // Re-add automation lines
                foreach (var character in plugin.Characters)
                {
                    foreach (var design in character.Designs)
                    {
                        string macro = design.IsAdvancedMode ? design.AdvancedMacro : design.Macro;
                        if (string.IsNullOrWhiteSpace(macro))
                            continue;

                        string updated = Plugin.SanitizeDesignMacro(macro, design, character, true);

                        if (design.IsAdvancedMode && updated != design.AdvancedMacro)
                        {
                            design.AdvancedMacro = updated;
                            changed = true;
                        }
                        else if (!design.IsAdvancedMode && updated != design.Macro)
                        {
                            design.Macro = updated;
                            changed = true;
                        }
                    }
                }

                foreach (var character in plugin.Characters)
                {
                    string updated = Plugin.SanitizeMacro(character.Macros, character);
                    if (updated != character.Macros)
                    {
                        character.Macros = updated;
                        changed = true;
                    }
                }
            }

            if (changed)
                plugin.SaveConfiguration();
        }
        private void DrawConflictResolutionSettings()
        {
            ImGui.Spacing();
            
            // Center the warning box
            var availableWidth = ImGui.GetContentRegionAvail().X;
            var warningBoxWidth = availableWidth * 0.9f; // Use 90% of available width
            var centerOffset = (availableWidth - warningBoxWidth) * 0.5f;
            
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + centerOffset);

            // Warning box
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1.0f, 0.6f, 0.0f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.8f, 0.0f, 1.0f));
            if (ImGui.BeginChild("ConflictWarning", new Vector2(warningBoxWidth, 80), true, ImGuiWindowFlags.NoScrollbar))
            {
                // Warning icon + text
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text($"{FontAwesomeIcon.ExclamationTriangle.ToIconString()}");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextWrapped("EXPERIMENTAL FEATURE");
                ImGui.TextWrapped("This feature automatically manages mod conflicts by controlling which mods are enabled per character. Use at your own risk.");
            }
            ImGui.EndChild();
            ImGui.PopStyleColor(2); // Pop both Border and Text colors

            ImGui.Spacing();
            ImGui.Indent();

            // Main checkbox
            var enabled = plugin.Configuration.EnableConflictResolution;
            if (ImGui.Checkbox("Enable Conflict Resolution", ref enabled))
            {
                plugin.Configuration.EnableConflictResolution = enabled;
                plugin.SaveConfiguration();
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300f);
                ImGui.TextUnformatted("When enabled, allows you to select specific mods per character/design that will automatically enable/disable when switching. This prevents mod conflicts without manual Penumbra management.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            if (enabled)
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "• Hold Ctrl+Shift while clicking Add Character/Design");
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "• Auto-categorizes mods in CS+ only (no Penumbra changes)");
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "• Right-click to move mods if categorization is wrong");
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "• Auto-manages Gear/Hair mods per character");
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "• Other categories managed manually");
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "• Configure individual mod settings per character");
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "• Pin critical mods to keep always active");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                // Penumbra inheritance toggle
                var respectInheritance = plugin.Configuration.RespectPenumbraInheritance;
                if (ImGui.Checkbox("Respect Penumbra Inheritance", ref respectInheritance))
                {
                    plugin.Configuration.RespectPenumbraInheritance = respectInheritance;
                    plugin.SaveConfiguration();
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.PushTextWrapPos(300f);
                    ImGui.TextUnformatted("When enabled, mods inherited from parent collections in Penumbra will not be disabled by Conflict Resolution unless explicitly configured. This is useful if you use Penumbra's collection inheritance feature.");
                    ImGui.PopTextWrapPos();
                    ImGui.EndTooltip();
                }
            }

            ImGui.Unindent();
            ImGui.Spacing();
        }

        private float GetSafeScale(float baseScale)
        {
            return Math.Clamp(baseScale, 0.3f, 5.0f); // Prevent extreme scaling
        }

        private void DrawBackupSettings()
        {
            // Check for pending import file
            if (pendingImportPath != null)
            {
                string importPath;
                lock (this)
                {
                    importPath = pendingImportPath;
                    pendingImportPath = null;
                }

                if (File.Exists(importPath))
                {
                    Plugin.Log.Info($"[Settings] Processing import file: {importPath}");
                    AddImportedFileToBackups(importPath);
                }
                else
                {
                    lastBackupStatusMessage = "❌ Selected file does not exist";
                    lastBackupStatusTime = DateTime.Now;
                }
            }

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.9f, 1.0f, 1f));
            ImGui.TextWrapped("Create manual backups and restore configurations from backup files.");
            ImGui.PopStyleColor();

            ImGui.Spacing();

            // Current backup status (refresh each time)
            var backupInfo = BackupManager.GetBackupInfo();
            RefreshAvailableBackups(); // Make sure we have current data
            
            ImGui.Text("Backup Status:");
            ImGui.Indent();
            
            if (backupInfo.BackupExists)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.9f, 0.7f, 1f));
                ImGui.Text($"Last automatic backup: {backupInfo.LastBackupDate?.ToString("yyyy-MM-dd HH:mm")}");
                ImGui.Text($"Total backups: {availableBackups.Count}"); // Use the current count
                if (!string.IsNullOrEmpty(backupInfo.LastBackupVersion))
                    ImGui.Text($"Version: {backupInfo.LastBackupVersion}");
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.8f, 0.6f, 1f));
                ImGui.Text($"Total backups: {availableBackups.Count}");
                if (availableBackups.Count == 0)
                    ImGui.Text("No backups found");
                ImGui.PopStyleColor();
            }
            
            ImGui.Unindent();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Manual backup section
            ImGui.Text("Create Manual Backup:");
            ImGui.Spacing();

            var totalScale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);
            var contentWidth = ImGui.GetContentRegionAvail().X;
            var labelWidth = 120f * totalScale;
            var inputWidth = contentWidth - labelWidth - (20f * totalScale);

            DrawFixedSetting("Backup Name:", labelWidth, inputWidth * 0.7f, () =>
            {
                if (ImGui.InputTextWithHint("##BackupName", "Optional custom name", ref backupNameBuffer, 50))
                {
                    // Sanitize input
                    backupNameBuffer = string.Join("_", backupNameBuffer.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
                }
                DrawTooltip("Optional custom name for the backup. If empty, a timestamp will be used.");
            });

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.8f, 0.5f, 0.7f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.9f, 0.6f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 1.0f, 0.7f, 1.0f));

            if (ImGui.Button("Create Manual Backup", new Vector2(200f * totalScale, 30f * totalScale)))
            {
                CreateManualBackup();
            }
            ImGui.PopStyleColor(3);

            DrawTooltip("Creates a backup of your current configuration in the plugin's backup folder that you can restore later.");

            // Show status message if recent
            if (!string.IsNullOrEmpty(lastBackupStatusMessage) && 
                DateTime.Now - lastBackupStatusTime < TimeSpan.FromSeconds(5))
            {
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.9f, 0.7f, 1f));
                ImGui.Text(lastBackupStatusMessage);
                ImGui.PopStyleColor();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Restore section
            ImGui.Text("Restore Configuration:");
            ImGui.Spacing();

            if (availableBackups.Any())
            {
                ImGui.Text("Available Backups:");
                ImGui.Spacing();

                // Backup list with restore buttons
                if (ImGui.BeginChild("BackupList", new Vector2(0, 120f * totalScale), true))
                {
                    foreach (var backup in availableBackups.Take(10)) // Show only first 10
                    {
                        // Calculate positions for proper alignment
                        var availableWidth = ImGui.GetContentRegionAvail().X;
                        var restoreButtonWidth = 70f * totalScale;
                        var deleteButtonWidth = 60f * totalScale;
                        var buttonSpacing = 5f * totalScale;
                        var totalButtonWidth = restoreButtonWidth + deleteButtonWidth + buttonSpacing;
                        var textWidth = availableWidth - totalButtonWidth - (10f * totalScale); // 10f for spacing
                        
                        // Display backup name with color coding
                        var displayColor = backup.IsManual ? new Vector4(0.8f, 0.9f, 1.0f, 1f) : new Vector4(0.7f, 0.7f, 0.8f, 1f);
                        
                        ImGui.PushStyleColor(ImGuiCol.Text, displayColor);
                        
                        // Truncate text if too long
                        var displayText = backup.GetDisplayName();
                        if (displayText.Length > 45) // Adjust for smaller space due to two buttons
                        {
                            displayText = displayText.Substring(0, 42) + "...";
                        }
                        
                        ImGui.Text(displayText);
                        ImGui.PopStyleColor();

                        // Position buttons on the same line
                        ImGui.SameLine();
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (textWidth - ImGui.CalcTextSize(displayText).X));

                        // Restore button
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.6f, 0.3f, 0.7f));
                        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.7f, 0.4f, 0.8f));
                        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1.0f, 0.8f, 0.5f, 1.0f));

                        if (ImGui.Button($"Restore##{backup.FileName}", new Vector2(restoreButtonWidth, 0)))
                        {
                            RestoreFromBackup(backup.FilePath);
                        }
                        ImGui.PopStyleColor(3);

                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip($"Restore configuration from:\n{backup.FileName}\nCreated: {backup.CreatedDate:yyyy-MM-dd HH:mm:ss}");
                        }

                        // Delete button
                        ImGui.SameLine();
                        
                        // Check if Ctrl+Shift is held for delete functionality
                        bool isCtrlShiftHeld = ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift;
                        
                        // Dim the button if Ctrl+Shift is not held
                        if (!isCtrlShiftHeld)
                        {
                            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.2f, 0.2f, 0.4f));
                            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.6f, 0.25f, 0.25f, 0.5f));
                            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.3f, 0.3f, 0.6f));
                        }
                        else
                        {
                            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.3f, 0.3f, 0.7f));
                            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.4f, 0.4f, 0.8f));
                            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1.0f, 0.5f, 0.5f, 1.0f));
                        }

                        bool deleteClicked = ImGui.Button($"Delete##{backup.FileName}", new Vector2(deleteButtonWidth, 0));
                        
                        if (deleteClicked && isCtrlShiftHeld)
                        {
                            DeleteBackup(backup.FilePath, backup.FileName);
                        }
                        ImGui.PopStyleColor(3);

                        if (ImGui.IsItemHovered())
                        {
                            if (isCtrlShiftHeld)
                            {
                                ImGui.SetTooltip($"Delete backup file:\n{backup.FileName}\nThis action cannot be undone!");
                            }
                            else
                            {
                                ImGui.SetTooltip($"Delete backup file:\n{backup.FileName}\n\nHold Ctrl+Shift and click to delete\n(prevents accidental deletion)");
                            }
                        }
                    }
                }
                ImGui.EndChild();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1f));
                ImGui.Text("No backup files found");
                ImGui.PopStyleColor();
            }

            ImGui.Spacing();

            // Import config file button
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.9f, 0.7f, 0.4f, 0.7f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1.0f, 0.8f, 0.5f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1.0f, 0.9f, 0.6f, 1.0f));

            if (ImGui.Button("Add Config File...", new Vector2(200f * totalScale, 30f * totalScale)))
            {
                ImportConfigurationFile();
            }
            ImGui.PopStyleColor(3);

            DrawTooltip("Opens a file browser to select and add a CharacterSelectPlus configuration file to your Available Backups list.");

            ImGui.Spacing();

            // Warning about restore
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.8f, 0.6f, 1f));
            
            // Warning icon
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text($"{FontAwesomeIcon.ExclamationTriangle.ToIconString()}");
            ImGui.PopFont();
            
            ImGui.SameLine();
            ImGui.TextWrapped("Restoring will overwrite your current configuration. A backup will be created automatically before restoring.");
            ImGui.PopStyleColor();

            ImGui.Spacing();
        }

        private void CreateManualBackup()
        {
            try
            {
                string? customName = string.IsNullOrWhiteSpace(backupNameBuffer) ? null : backupNameBuffer.Trim();
                string? backupPath = BackupManager.CreateManualBackup(plugin.Configuration, customName);
                
                if (!string.IsNullOrEmpty(backupPath))
                {
                    lastBackupStatusMessage = $"✓ Backup created: {Path.GetFileName(backupPath)}";
                    lastBackupStatusTime = DateTime.Now;
                    backupNameBuffer = ""; // Clear the input
                    RefreshAvailableBackups();
                }
                else
                {
                    lastBackupStatusMessage = "❌ Failed to create backup";
                    lastBackupStatusTime = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Settings] Error creating manual backup: {ex.Message}");
                lastBackupStatusMessage = "❌ Error creating backup";
                lastBackupStatusTime = DateTime.Now;
            }
        }


        private void ImportConfigurationFile()
        {
            Thread thread = new Thread(() =>
            {
                try
                {
                    using (OpenFileDialog openFileDialog = new OpenFileDialog())
                    {
                        openFileDialog.Filter = "JSON Configuration Files (*.json)|*.json|All Files (*.*)|*.*";
                        openFileDialog.Title = "Select Configuration File to Import";

                        if (openFileDialog.ShowDialog() == DialogResult.OK)
                        {
                            lock (this)
                            {
                                pendingImportPath = openFileDialog.FileName;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"[Settings] Error in import file dialog thread: {ex.Message}");
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private void RestoreFromBackup(string backupPath)
        {
            try
            {
                // Create emergency backup before restoring
                BackupManager.CreateEmergencyBackup(plugin.Configuration);
                
                var restoredConfig = BackupManager.ImportConfiguration(backupPath);
                if (restoredConfig != null)
                {
                    // Update the plugin interface reference using reflection
                    var pluginInterfaceField = restoredConfig.GetType()
                        .GetField("pluginInterface", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    pluginInterfaceField?.SetValue(restoredConfig, Plugin.PluginInterface);
                    
                    // Copy all the important configuration data back to the current config
                    // This preserves the plugin instance while updating the data
                    var currentConfig = plugin.Configuration;
                    
                    // Copy character data
                    currentConfig.Characters.Clear();
                    currentConfig.Characters.AddRange(restoredConfig.Characters);
                    
                    // Copy all configuration properties using reflection
                    var configType = typeof(Configuration);
                    var properties = configType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                        .Where(p => p.CanWrite && p.Name != "Characters");
                    
                    foreach (var prop in properties)
                    {
                        try
                        {
                            var value = prop.GetValue(restoredConfig);
                            prop.SetValue(currentConfig, value);
                        }
                        catch (Exception propEx)
                        {
                            Plugin.Log.Warning($"[Settings] Could not restore property {prop.Name}: {propEx.Message}");
                        }
                    }
                    
                    // Save the updated configuration
                    currentConfig.Save();
                    
                    lastBackupStatusMessage = $"✓ Configuration restored from {Path.GetFileName(backupPath)}";
                    lastBackupStatusTime = DateTime.Now;
                    
                    Plugin.Log.Info($"[Settings] Successfully restored configuration from {backupPath}");
                }
                else
                {
                    lastBackupStatusMessage = "❌ Failed to restore configuration";
                    lastBackupStatusTime = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Settings] Error restoring from backup: {ex.Message}");
                lastBackupStatusMessage = "❌ Error restoring configuration";
                lastBackupStatusTime = DateTime.Now;
            }
        }

        private void RefreshAvailableBackups()
        {
            try
            {
                availableBackups = BackupManager.GetAvailableBackups();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Settings] Error refreshing available backups: {ex.Message}");
                availableBackups.Clear();
            }
        }

        private void AddImportedFileToBackups(string importPath)
        {
            try
            {
                var backupDirectory = Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "Backups");
                Directory.CreateDirectory(backupDirectory);

                string originalFileName = Path.GetFileName(importPath);
                string destinationPath = Path.Combine(backupDirectory, originalFileName);

                // If file already exists, add timestamp to avoid overwriting
                if (File.Exists(destinationPath))
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(originalFileName);
                    string extension = Path.GetExtension(originalFileName);
                    originalFileName = $"{nameWithoutExt}_{timestamp}{extension}";
                    destinationPath = Path.Combine(backupDirectory, originalFileName);
                }

                File.Copy(importPath, destinationPath, overwrite: false);

                // Update the file's LastWriteTime to current time so it appears at top of list
                File.SetLastWriteTime(destinationPath, DateTime.Now);

                lastBackupStatusMessage = $"✓ Imported file added to backups: {originalFileName}";
                lastBackupStatusTime = DateTime.Now;
                RefreshAvailableBackups();

                Plugin.Log.Info($"[Settings] Successfully imported file to backups: {destinationPath}");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Settings] Error adding imported file to backups: {ex.Message}");
                lastBackupStatusMessage = "❌ Error importing file to backups";
                lastBackupStatusTime = DateTime.Now;
            }
        }

        private void DeleteBackup(string backupFilePath, string backupFileName)
        {
            try
            {
                if (File.Exists(backupFilePath))
                {
                    File.Delete(backupFilePath);
                    lastBackupStatusMessage = $"✓ Deleted backup: {backupFileName}";
                    lastBackupStatusTime = DateTime.Now;
                    RefreshAvailableBackups();
                    Plugin.Log.Info($"[Settings] Successfully deleted backup: {backupFilePath}");
                }
                else
                {
                    lastBackupStatusMessage = "❌ Backup file not found";
                    lastBackupStatusTime = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Settings] Error deleting backup: {ex.Message}");
                lastBackupStatusMessage = "❌ Error deleting backup";
                lastBackupStatusTime = DateTime.Now;
            }
        }

        #region Custom Theme Editor

        private string? _pendingBackgroundImagePath = null;
        private Dictionary<string, bool> _colorCategoryExpanded = new();
        private string _presetNameBuffer = "";
        private bool _showPresetSavePopup = false;
        private bool _showPresetDeleteConfirm = false;
        private FontAwesomeIconPickerWindow? _iconPickerWindow = null;

        private void DrawCustomThemeEditor()
        {
            var totalScale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);
            var customTheme = plugin.Configuration.CustomTheme;

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.8f, 0.6f, 1.0f));
            ImGui.Text("Custom Theme Settings");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            // Preset Management Section
            DrawPresetManagement(customTheme, totalScale);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Background Image Section
            DrawBackgroundImagePicker(customTheme, totalScale);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Favourite Icon Section
            DrawFavoriteIconPicker(customTheme, totalScale);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Color Customization Section
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
            ImGui.Text("Color Customization");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            // Global Reset Button
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.3f, 0.3f, 0.7f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.4f, 0.4f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.8f, 0.5f, 0.5f, 1.0f));
            if (ImGui.Button("Reset All Colors to Default", new Vector2(200f * totalScale, 0)))
            {
                customTheme.ColorOverrides.Clear();
                plugin.Configuration.Save();
            }
            ImGui.PopStyleColor(3);
            DrawTooltip("Reset all color customizations back to default values.");

            ImGui.Spacing();

            // Draw ImGui color categories
            foreach (var category in CustomThemeDefinitions.GetColorCategories())
            {
                DrawColorCategory(category, customTheme, totalScale);
            }

            // Draw custom colour categories (Accents - Favourite icon, card glow, etc.)
            foreach (var category in CustomThemeDefinitions.GetCustomColorCategories())
            {
                DrawCustomColorCategory(category, customTheme, totalScale);
            }
        }

        private void DrawPresetManagement(CustomThemeConfig customTheme, float totalScale)
        {
            var presets = plugin.Configuration.ThemePresets;
            var activePreset = plugin.Configuration.ActivePresetName;
            var isEditingPreset = !string.IsNullOrEmpty(activePreset);

            if (isEditingPreset)
            {
                // Editing a saved preset - show preset name and delete button
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.8f, 0.7f, 1.0f));
                ImGui.Text($"Editing: {activePreset}");
                ImGui.PopStyleColor();
                ImGui.SameLine();

                // Delete this preset button
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.3f, 0.3f, 0.7f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.4f, 0.4f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.8f, 0.5f, 0.5f, 1.0f));
                if (ImGui.Button("Delete", new Vector2(60f * totalScale, 0)))
                {
                    _showPresetDeleteConfirm = true;
                }
                ImGui.PopStyleColor(3);
                DrawTooltip($"Delete the '{activePreset}' preset.");

                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
                ImGui.Text("Changes are saved automatically.");
                ImGui.PopStyleColor();
            }
            else
            {
                // Custom (New) - show Save As button
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.5f, 0.3f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.6f, 0.4f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.7f, 0.5f, 1.0f));
                if (ImGui.Button("Save As Preset...", new Vector2(120f * totalScale, 0)))
                {
                    _showPresetSavePopup = true;
                    _presetNameBuffer = "My Theme";
                }
                ImGui.PopStyleColor(3);
                DrawTooltip("Save current settings as a new preset. Saved presets appear in the Theme dropdown.");
            }

            ImGui.Spacing();

            // Save preset popup
            if (_showPresetSavePopup)
            {
                ImGui.OpenPopup("Save Theme Preset##SavePresetPopup");
            }

            var savePopupOpen = true;
            if (ImGui.BeginPopupModal("Save Theme Preset##SavePresetPopup", ref savePopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("Enter a name for this preset:");
                ImGui.Spacing();

                ImGui.SetNextItemWidth(250f * totalScale);
                ImGui.InputText("##PresetName", ref _presetNameBuffer, 50);

                ImGui.Spacing();

                // Check if name already exists
                var existingPreset = presets.FirstOrDefault(p => p.Name.Equals(_presetNameBuffer.Trim(), StringComparison.OrdinalIgnoreCase));

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.5f, 0.3f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.6f, 0.4f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.7f, 0.5f, 1.0f));
                var saveButtonText = existingPreset != null ? "Update" : "Save";
                if (ImGui.Button(saveButtonText, new Vector2(80f * totalScale, 0)))
                {
                    if (!string.IsNullOrWhiteSpace(_presetNameBuffer))
                    {
                        var trimmedName = _presetNameBuffer.Trim();
                        if (existingPreset != null)
                        {
                            // Update existing preset
                            existingPreset.Config = customTheme.Clone();
                        }
                        else
                        {
                            // Create new preset
                            presets.Add(new ThemePreset
                            {
                                Name = trimmedName,
                                Config = customTheme.Clone()
                            });
                        }
                        plugin.Configuration.ActivePresetName = trimmedName;
                        plugin.Configuration.Save();
                        _showPresetSavePopup = false;
                        ImGui.CloseCurrentPopup();
                    }
                }
                ImGui.PopStyleColor(3);

                ImGui.SameLine();

                if (ImGui.Button("Cancel", new Vector2(80f * totalScale, 0)))
                {
                    _showPresetSavePopup = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }

            if (!savePopupOpen)
            {
                _showPresetSavePopup = false;
            }

            // Delete preset confirmation popup
            if (_showPresetDeleteConfirm)
            {
                ImGui.OpenPopup("Delete Preset?##DeletePresetConfirm");
            }

            var deletePopupOpen = true;
            if (ImGui.BeginPopupModal("Delete Preset?##DeletePresetConfirm", ref deletePopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text($"Are you sure you want to delete '{activePreset}'?");
                ImGui.Spacing();

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.3f, 0.3f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.4f, 0.4f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.8f, 0.5f, 0.5f, 1.0f));
                if (ImGui.Button("Delete", new Vector2(80f * totalScale, 0)))
                {
                    var presetToDelete = presets.FirstOrDefault(p => p.Name == activePreset);
                    if (presetToDelete != null)
                    {
                        presets.Remove(presetToDelete);
                    }
                    // Reset to Custom (New) with clean defaults
                    plugin.Configuration.ActivePresetName = null;
                    customTheme.ColorOverrides.Clear();
                    customTheme.BackgroundImagePath = null;
                    customTheme.BackgroundImageOpacity = 0.3f;
                    customTheme.BackgroundImageZoom = 1.0f;
                    customTheme.BackgroundImageOffsetX = 0f;
                    customTheme.BackgroundImageOffsetY = 0f;
                    customTheme.FavoriteIconId = 0;
                    customTheme.UseNameplateColorForCardGlow = true;
                    plugin.Configuration.Save();
                    _showPresetDeleteConfirm = false;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.PopStyleColor(3);

                ImGui.SameLine();

                if (ImGui.Button("Cancel", new Vector2(80f * totalScale, 0)))
                {
                    _showPresetDeleteConfirm = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }

            if (!deletePopupOpen)
            {
                _showPresetDeleteConfirm = false;
            }
        }

        private void DrawBackgroundImagePicker(CustomThemeConfig customTheme, float totalScale)
        {
            // Check for pending file from file browser
            if (_pendingBackgroundImagePath != null)
            {
                string path;
                lock (this)
                {
                    path = _pendingBackgroundImagePath;
                    _pendingBackgroundImagePath = null;
                }

                if (File.Exists(path))
                {
                    customTheme.BackgroundImagePath = path;
                    plugin.Configuration.Save();
                }
            }

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
            ImGui.Text("Background Image");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            // Current image path display
            var currentPath = customTheme.BackgroundImagePath ?? "None";
            if (currentPath.Length > 40)
            {
                currentPath = "..." + currentPath.Substring(currentPath.Length - 37);
            }

            ImGui.Text($"Current: {currentPath}");

            // Browse button
            if (ImGui.Button("Browse...", new Vector2(100f * totalScale, 0)))
            {
                OpenBackgroundImageBrowser();
            }
            DrawTooltip("Select an image file to use as the main window background.");

            ImGui.SameLine();

            // Clear button
            if (!string.IsNullOrEmpty(customTheme.BackgroundImagePath))
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.3f, 0.3f, 0.7f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.4f, 0.4f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.8f, 0.5f, 0.5f, 1.0f));
                if (ImGui.Button("Clear", new Vector2(60f * totalScale, 0)))
                {
                    customTheme.BackgroundImagePath = null;
                    plugin.Configuration.Save();
                }
                ImGui.PopStyleColor(3);
            }

            // Opacity slider (only show if image is set)
            if (!string.IsNullOrEmpty(customTheme.BackgroundImagePath))
            {
                ImGui.Spacing();
                var opacity = customTheme.BackgroundImageOpacity;
                ImGui.SetNextItemWidth(200f * totalScale);
                if (ImGui.SliderFloat("Opacity", ref opacity, 0.0f, 1.0f, "%.2f"))
                {
                    customTheme.BackgroundImageOpacity = opacity;
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    plugin.Configuration.Save();
                }
                DrawTooltip("Adjust the opacity of the background image (0 = invisible, 1 = fully visible).");

                // Zoom slider
                ImGui.Spacing();
                var zoom = customTheme.BackgroundImageZoom;
                ImGui.SetNextItemWidth(200f * totalScale);
                if (ImGui.SliderFloat("Zoom", ref zoom, 0.5f, 3.0f, "%.2fx"))
                {
                    customTheme.BackgroundImageZoom = zoom;
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    plugin.Configuration.Save();
                }
                DrawTooltip("Zoom level for the background image (1.0 = fit to window, larger = zoomed in).");

                // Position X slider
                ImGui.Spacing();
                var posX = customTheme.BackgroundImageOffsetX;
                ImGui.SetNextItemWidth(200f * totalScale);
                if (ImGui.SliderFloat("Position X", ref posX, -1.0f, 1.0f, "%.2f"))
                {
                    customTheme.BackgroundImageOffsetX = posX;
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    plugin.Configuration.Save();
                }
                DrawTooltip("Horizontal position offset (-1 = left, 0 = center, 1 = right). Only affects zoomed-in images.");

                // Position Y slider
                ImGui.Spacing();
                var posY = customTheme.BackgroundImageOffsetY;
                ImGui.SetNextItemWidth(200f * totalScale);
                if (ImGui.SliderFloat("Position Y", ref posY, -1.0f, 1.0f, "%.2f"))
                {
                    customTheme.BackgroundImageOffsetY = posY;
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    plugin.Configuration.Save();
                }
                DrawTooltip("Vertical position offset (-1 = top, 0 = center, 1 = bottom). Only affects zoomed-in images.");

                // Reset button for position/zoom
                ImGui.Spacing();
                if (ImGui.Button("Reset Position & Zoom", new Vector2(150f * totalScale, 0)))
                {
                    customTheme.BackgroundImageZoom = 1.0f;
                    customTheme.BackgroundImageOffsetX = 0f;
                    customTheme.BackgroundImageOffsetY = 0f;
                    plugin.Configuration.Save();
                }
            }
        }

        private void OpenBackgroundImageBrowser()
        {
            Thread thread = new Thread(() =>
            {
                try
                {
                    using (OpenFileDialog openFileDialog = new OpenFileDialog())
                    {
                        openFileDialog.Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*";
                        openFileDialog.Title = "Select Background Image";

                        if (openFileDialog.ShowDialog() == DialogResult.OK)
                        {
                            lock (this)
                            {
                                _pendingBackgroundImagePath = openFileDialog.FileName;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"[CustomTheme] Error in background image browser: {ex.Message}");
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private void DrawFavoriteIconPicker(CustomThemeConfig customTheme, float totalScale)
        {
            // Check if icon picker window has confirmed a selection or was closed
            if (_iconPickerWindow != null)
            {
                if (_iconPickerWindow.Confirmed || !_iconPickerWindow.IsOpen)
                {
                    // Window was confirmed or closed - cleanup
                    // (Icon is already saved in real-time via OnIconChanged callback)
                    plugin.WindowSystem.RemoveWindow(_iconPickerWindow);
                    _iconPickerWindow = null;
                }
            }

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
            ImGui.Text("Favorite Icon");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            // Show current icon
            var currentIconId = customTheme.FavoriteIconId;
            var currentIcon = currentIconId == 0 ? FontAwesomeIcon.Star : (FontAwesomeIcon)currentIconId;

            // Get custom favourite icon colour if set
            Vector4 favoriteIconColor = new Vector4(1.0f, 0.85f, 0.0f, 1.0f); // Default gold
            if (customTheme.ColorOverrides.TryGetValue("custom.favoriteIcon", out var packedFavColor) && packedFavColor.HasValue)
            {
                favoriteIconColor = CustomThemeDefinitions.UnpackColor(packedFavColor.Value);
            }

            ImGui.Text("Current Icon: ");
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.Text, favoriteIconColor);
            ImGui.Text(currentIcon.ToIconString());
            ImGui.PopStyleColor();
            ImGui.PopFont();

            ImGui.SameLine();

            // Open icon picker window button
            if (ImGui.Button("Choose Icon...", new Vector2(100f * totalScale, 0)))
            {
                _iconPickerWindow = new FontAwesomeIconPickerWindow(currentIcon, plugin.Configuration);

                // Set up real-time preview callback
                _iconPickerWindow.OnIconChanged = (newIcon) =>
                {
                    customTheme.FavoriteIconId = newIcon == FontAwesomeIcon.Star ? 0 : (int)newIcon;
                    plugin.Configuration.Save();
                };

                plugin.WindowSystem.AddWindow(_iconPickerWindow);
                _iconPickerWindow.IsOpen = true;
            }

            // Reset to default button
            if (currentIconId != 0)
            {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.35f, 0.35f, 0.7f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.6f, 0.4f, 0.4f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.5f, 0.5f, 0.9f));
                if (ImGui.SmallButton("Reset##FavoriteIcon"))
                {
                    customTheme.FavoriteIconId = 0;
                    plugin.Configuration.Save();
                }
                ImGui.PopStyleColor(3);
            }
        }

        private void DrawColorCategory(string category, CustomThemeConfig customTheme, float totalScale)
        {
            // Initialize category expansion state if needed
            if (!_colorCategoryExpanded.ContainsKey(category))
            {
                _colorCategoryExpanded[category] = false;
            }

            var isExpanded = _colorCategoryExpanded[category];

            // Category header
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.2f, 0.2f, 0.25f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.25f, 0.25f, 0.3f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.3f, 0.3f, 0.35f, 1.0f));

            if (ImGui.CollapsingHeader($"{category}##ColorCategory", isExpanded ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None))
            {
                _colorCategoryExpanded[category] = true;

                ImGui.Indent(10f);

                // Reset category button
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.3f, 0.3f, 0.6f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.6f, 0.4f, 0.4f, 0.7f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.5f, 0.5f, 0.8f));
                if (ImGui.SmallButton($"Reset {category}##Reset{category}"))
                {
                    // Remove all overrides for this category
                    foreach (var option in CustomThemeDefinitions.GetColorOptionsForCategory(category))
                    {
                        customTheme.ColorOverrides.Remove(option.Key);
                    }
                    plugin.Configuration.Save();
                }
                ImGui.PopStyleColor(3);
                DrawTooltip($"Reset all {category} colors to default.");

                ImGui.Spacing();

                // Draw color options for this category
                foreach (var option in CustomThemeDefinitions.GetColorOptionsForCategory(category))
                {
                    DrawColorOption(option, customTheme, totalScale);
                }

                ImGui.Unindent(10f);
            }
            else
            {
                _colorCategoryExpanded[category] = false;
            }

            ImGui.PopStyleColor(3);
        }

        private void DrawColorOption(CustomThemeDefinitions.ColorOption option, CustomThemeConfig customTheme, float totalScale)
        {
            // Get current value (override or default)
            Vector4 currentColor;
            bool hasOverride = customTheme.ColorOverrides.TryGetValue(option.Key, out var packedColor) && packedColor.HasValue;

            if (hasOverride)
            {
                currentColor = CustomThemeDefinitions.UnpackColor(packedColor!.Value);
            }
            else
            {
                currentColor = option.DefaultValue;
            }

            // Label
            ImGui.AlignTextToFramePadding();
            ImGui.Text(option.Label);

            if (!string.IsNullOrEmpty(option.Description))
            {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
                ImGui.Text("(?)");
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(option.Description);
                }
            }

            ImGui.SameLine(200f * totalScale);

            // Color picker
            ImGui.SetNextItemWidth(150f * totalScale);
            if (ImGui.ColorEdit4($"##{option.Key}", ref currentColor, ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
            {
                // Save the color override
                customTheme.ColorOverrides[option.Key] = CustomThemeDefinitions.PackColor(currentColor);
                plugin.Configuration.Save();
            }

            // Reset button - always visible, disabled when no override
            ImGui.SameLine();
            if (hasOverride)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.35f, 0.35f, 0.7f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.6f, 0.4f, 0.4f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.5f, 0.5f, 0.9f));
                if (ImGui.SmallButton($"Reset##{option.Key}"))
                {
                    customTheme.ColorOverrides.Remove(option.Key);
                    plugin.Configuration.Save();
                }
                ImGui.PopStyleColor(3);
            }
            else
            {
                // Disabled state
                ImGui.BeginDisabled();
                ImGui.SmallButton($"Reset##{option.Key}");
                ImGui.EndDisabled();
            }
        }

        private void DrawCustomColorCategory(string category, CustomThemeConfig customTheme, float totalScale)
        {
            // Initialize category expansion state if needed
            if (!_colorCategoryExpanded.ContainsKey(category))
            {
                _colorCategoryExpanded[category] = false;
            }

            var isExpanded = _colorCategoryExpanded[category];

            // Category header
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.25f, 0.2f, 0.3f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.3f, 0.25f, 0.35f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.35f, 0.3f, 0.4f, 1.0f));

            if (ImGui.CollapsingHeader($"{category}##CustomColorCategory", isExpanded ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None))
            {
                _colorCategoryExpanded[category] = true;

                ImGui.Indent(10f);

                // Reset category button
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.3f, 0.3f, 0.6f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.6f, 0.4f, 0.4f, 0.7f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.5f, 0.5f, 0.8f));
                if (ImGui.SmallButton($"Reset {category}##ResetCustom{category}"))
                {
                    // Remove all custom overrides for this category
                    foreach (var option in CustomThemeDefinitions.GetCustomColorOptionsForCategory(category))
                    {
                        customTheme.ColorOverrides.Remove(option.Key);
                    }
                    plugin.Configuration.Save();
                }
                ImGui.PopStyleColor(3);
                DrawTooltip($"Reset all {category} colors to default.");

                ImGui.Spacing();

                // Special handling for Accents category - add card glow toggle
                if (category == "Accents")
                {
                    // Card Glow source toggle
                    var useNameplateColor = customTheme.UseNameplateColorForCardGlow;
                    if (ImGui.Checkbox("Use Nameplate Color for Card Glow", ref useNameplateColor))
                    {
                        customTheme.UseNameplateColorForCardGlow = useNameplateColor;
                        plugin.Configuration.Save();
                    }
                    DrawTooltip("When enabled, character cards use each character's individual nameplate color.\nWhen disabled, all cards use the custom color below.");
                    ImGui.Spacing();
                }

                // Draw custom color options for this category
                foreach (var option in CustomThemeDefinitions.GetCustomColorOptionsForCategory(category))
                {
                    // For card glow, disable the color picker if using nameplate colors
                    if (option.Key == "custom.cardGlow" && customTheme.UseNameplateColorForCardGlow)
                    {
                        ImGui.BeginDisabled();
                        DrawCustomColorOption(option, customTheme, totalScale);
                        ImGui.EndDisabled();
                    }
                    else
                    {
                        DrawCustomColorOption(option, customTheme, totalScale);
                    }
                }

                ImGui.Unindent(10f);
            }
            else
            {
                _colorCategoryExpanded[category] = false;
            }

            ImGui.PopStyleColor(3);
        }

        private void DrawCustomColorOption(CustomThemeDefinitions.CustomColorOption option, CustomThemeConfig customTheme, float totalScale)
        {
            // Get current value (override or default)
            Vector4 currentColor;
            bool hasOverride = customTheme.ColorOverrides.TryGetValue(option.Key, out var packedColor) && packedColor.HasValue;

            if (hasOverride)
            {
                currentColor = CustomThemeDefinitions.UnpackColor(packedColor!.Value);
            }
            else
            {
                currentColor = option.DefaultValue;
            }

            // Label
            ImGui.AlignTextToFramePadding();
            ImGui.Text(option.Label);

            if (!string.IsNullOrEmpty(option.Description))
            {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
                ImGui.Text("(?)");
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(option.Description);
                }
            }

            ImGui.SameLine(200f * totalScale);

            // Color picker
            ImGui.SetNextItemWidth(150f * totalScale);
            if (ImGui.ColorEdit4($"##{option.Key}", ref currentColor, ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
            {
                // Save the color override
                customTheme.ColorOverrides[option.Key] = CustomThemeDefinitions.PackColor(currentColor);
                plugin.Configuration.Save();
            }

            // Reset button - always visible, disabled when no override
            ImGui.SameLine();
            if (hasOverride)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.35f, 0.35f, 0.7f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.6f, 0.4f, 0.4f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.5f, 0.5f, 0.9f));
                if (ImGui.SmallButton($"Reset##{option.Key}"))
                {
                    customTheme.ColorOverrides.Remove(option.Key);
                    plugin.Configuration.Save();
                }
                ImGui.PopStyleColor(3);
            }
            else
            {
                // Disabled state
                ImGui.BeginDisabled();
                ImGui.SmallButton($"Reset##{option.Key}");
                ImGui.EndDisabled();
            }
        }

        #endregion

        private void DrawTooltip(string text)
        {
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300f);
                ImGui.TextUnformatted(text);
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
        }

        /// <summary>
        /// Expands a specific settings section by name.
        /// Used by feature spotlight cards to navigate directly to relevant settings.
        /// </summary>
        public void ExpandSection(string sectionName)
        {
            // First collapse all sections
            visualSettingsOpen = false;
            automationSettingsOpen = false;
            behaviorSettingsOpen = false;
            honorificSettingsOpen = false;
            mainCharacterSettingsOpen = false;
            dialogueSettingsOpen = false;
            nameSyncSettingsOpen = false;
            characterAssignmentSettingsOpen = false;
            jobAssignmentSettingsOpen = false;
            conflictResolutionSettingsOpen = false;
            backupSettingsOpen = false;
            communitySettingsOpen = false;

            // Then expand the requested section
            switch (sectionName)
            {
                case "Visual Settings":
                    visualSettingsOpen = true;
                    break;
                case "Glamourer Automations":
                    automationSettingsOpen = true;
                    break;
                case "Behavior Settings":
                    behaviorSettingsOpen = true;
                    break;
                case "Honorific":
                    honorificSettingsOpen = true;
                    break;
                case "Main Character":
                    mainCharacterSettingsOpen = true;
                    break;
                case "Immersive Dialogue":
                    dialogueSettingsOpen = true;
                    break;
                case "Name Sync":
                    nameSyncSettingsOpen = true;
                    break;
                case "Character Assignments":
                    characterAssignmentSettingsOpen = true;
                    break;
                case "Job Assignments":
                    jobAssignmentSettingsOpen = true;
                    break;
                case "Conflict Resolution":
                    conflictResolutionSettingsOpen = true;
                    break;
                case "Backup & Restore":
                    backupSettingsOpen = true;
                    break;
                case "Community & Moderation":
                    // Redirect to Behavior Settings where this content now lives
                    behaviorSettingsOpen = true;
                    break;
                default:
                    Plugin.Log.Warning($"[SettingsPanel] Unknown section name: {sectionName}");
                    return; // Don't set pending if unknown section
            }

            // Set pending section to force ImGui to open it on next draw
            pendingExpandSection = sectionName;
        }

        /// <summary>
        /// Parses a character assignment value into character name and optional design name.
        /// Supports formats: "CharName" (legacy), "Character:CharName", "Design:CharName:DesignName"
        /// </summary>
        private (string CharacterName, string? DesignName) ParseCharacterAssignmentValue(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "None")
                return (value ?? "", null);

            if (value.StartsWith("Design:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = value.Substring("Design:".Length).Split(':', 2);
                return parts.Length >= 2 ? (parts[0], parts[1]) : (parts[0], null);
            }

            if (value.StartsWith("Character:", StringComparison.OrdinalIgnoreCase))
            {
                return (value.Substring("Character:".Length), null);
            }

            // Legacy format - just the character name
            return (value, null);
        }

        /// <summary>
        /// Builds a character assignment value string from character name and optional design name.
        /// </summary>
        private string BuildCharacterAssignmentValue(string characterName, string? designName)
        {
            if (characterName == "None")
                return "None";

            if (!string.IsNullOrEmpty(designName))
                return $"Design:{characterName}:{designName}";

            return $"Character:{characterName}";
        }
    }
}

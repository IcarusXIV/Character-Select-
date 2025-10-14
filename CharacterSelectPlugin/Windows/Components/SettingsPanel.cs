using System;
using System.Linq;
using System.Numerics;
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
        private bool mainCharacterSettingsOpen = false;
        private bool dialogueSettingsOpen = false;
        private bool characterAssignmentSettingsOpen = false;
        private bool conflictResolutionSettingsOpen = false;
        private bool backupSettingsOpen = false;
        private string newRealCharacterBuffer = "";
        private string newCSCharacterBuffer = "";
        private string editingAssignmentKey = "";
        private string editingAssignmentValue = "";
        private string backupNameBuffer = "";
        private List<BackupFileInfo> availableBackups = new();
        private string lastBackupStatusMessage = "";
        private DateTime lastBackupStatusTime = DateTime.MinValue;
        private string? pendingImportPath = null;

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
                // Visual Settings Section (Red)
                visualSettingsOpen = DrawModernCollapsingHeader("Visual Settings", new Vector4(1.0f, 0.3f, 0.3f, 1.0f), visualSettingsOpen);
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

                // Behavior Settings Section (Green)
                behaviorSettingsOpen = DrawModernCollapsingHeader("Behavior Settings", new Vector4(0.3f, 0.8f, 0.3f, 1.0f), behaviorSettingsOpen);
                if (behaviorSettingsOpen)
                {
                    DrawBehaviorSettings();
                }

                // Character Assignments (Cyan)
                characterAssignmentSettingsOpen = DrawModernCollapsingHeader("Character Assignments", new Vector4(0.2f, 0.8f, 0.9f, 1.0f), characterAssignmentSettingsOpen);
                if (characterAssignmentSettingsOpen)
                {
                    DrawCharacterAssignmentSettings();
                }

                // Main Character Section (Blue)
                mainCharacterSettingsOpen = DrawModernCollapsingHeader("Main Character", new Vector4(0.3f, 0.6f, 1.0f, 1.0f), mainCharacterSettingsOpen);
                if (mainCharacterSettingsOpen)
                {
                    DrawMainCharacterSettings(labelWidth, inputWidth);
                }

                // Roleplay Integration (Purple)
                dialogueSettingsOpen = DrawModernCollapsingHeader("Immersive Dialogue", new Vector4(0.7f, 0.4f, 1.0f, 1.0f), dialogueSettingsOpen);
                if (dialogueSettingsOpen)
                {
                    DrawDialogueSettings();
                }

                // Conflict Resolution (Amber/Gold)
                conflictResolutionSettingsOpen = DrawModernCollapsingHeader("Conflict Resolution", new Vector4(1.0f, 0.8f, 0.2f, 1.0f), conflictResolutionSettingsOpen);
                if (conflictResolutionSettingsOpen)
                {
                    DrawConflictResolutionSettings();
                }

                // Backup & Restore (Mint Green)
                backupSettingsOpen = DrawModernCollapsingHeader("Backup & Restore", new Vector4(0.4f, 1.0f, 0.6f, 1.0f), backupSettingsOpen);
                if (backupSettingsOpen)
                {
                    DrawBackupSettings();
                }
            }
            ImGui.EndChild();
        }

        private bool DrawModernCollapsingHeader(string title, Vector4 titleColor, bool currentState)
        {
            var flags = currentState ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
            flags |= ImGuiTreeNodeFlags.SpanFullWidth;

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 1.0f, 1.0f)); // White text
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(titleColor.X * 0.6f, titleColor.Y * 0.6f, titleColor.Z * 0.6f, 0.7f)); // More vibrant
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(titleColor.X * 0.7f, titleColor.Y * 0.7f, titleColor.Z * 0.7f, 0.8f)); // More vibrant
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(titleColor.X * 0.8f, titleColor.Y * 0.8f, titleColor.Z * 0.8f, 0.9f)); // More vibrant

            bool isOpen = ImGui.CollapsingHeader(title, flags);

            ImGui.PopStyleColor(4);

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

                    // Different color for "None" assignments
                    if (assignment.Value == "None")
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.6f, 0.6f, 1f)); // Reddish for None
                        ImGui.Text("None (No Auto-Apply)");
                    }
                    else
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.8f, 0.6f, 1f));
                        ImGui.Text(assignment.Value);
                    }
                    ImGui.PopStyleColor();

                    // Add edit and remove buttons
                    ImGui.SameLine();
                    
                    // Edit button
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.6f, 0.8f, 0.6f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.7f, 0.9f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.8f, 1.0f, 1.0f));
                    
                    if (ImGui.SmallButton($"Edit##{assignment.Key}"))
                    {
                        editingAssignmentKey = assignment.Key;
                        editingAssignmentValue = assignment.Value;
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
                    }

                    // Add separator
                    ImGui.Separator();

                    // Add CS+ characters
                    foreach (var character in plugin.Configuration.Characters.OrderBy(c => c.Name))
                    {
                        bool isSelected = character.Name == editingAssignmentValue;
                        if (ImGui.Selectable(character.Name, isSelected))
                        {
                            editingAssignmentValue = character.Name;
                        }
                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }

                ImGui.Spacing();

                // Save and Cancel buttons
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.8f, 0.3f, 0.6f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.9f, 0.4f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 1.0f, 0.5f, 1.0f));
                
                if (ImGui.Button("Save Changes"))
                {
                    plugin.Configuration.CharacterAssignments[editingAssignmentKey] = editingAssignmentValue;
                    plugin.Configuration.Save();
                    editingAssignmentKey = "";
                    editingAssignmentValue = "";
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
                }

                // Add separator
                ImGui.Separator();

                // Add all CS+ characters
                foreach (var character in plugin.Characters)
                {
                    if (ImGui.Selectable(character.Name, character.Name == newCSCharacter))
                    {
                        newCSCharacterBuffer = character.Name;
                    }
                }
                ImGui.EndCombo();
            }
            DrawTooltip("Choose which CS+ character should auto-apply for this in-game character.\nSelect 'None' to prevent any auto-application for this character.");

            ImGui.Spacing();

            bool canAdd = !string.IsNullOrWhiteSpace(newRealCharacterBuffer) &&
                          !string.IsNullOrWhiteSpace(newCSCharacterBuffer) &&
                          !plugin.Configuration.CharacterAssignments.ContainsKey(newRealCharacterBuffer);

            if (!canAdd)
                ImGui.BeginDisabled();

            if (ImGui.Button("Add Assignment"))
            {
                plugin.Configuration.CharacterAssignments[newRealCharacterBuffer] = newCSCharacterBuffer;
                plugin.Configuration.Save();
                Plugin.Log.Debug($"[CharacterAssignment] Added: {newRealCharacterBuffer} → {newCSCharacterBuffer}");
                newRealCharacterBuffer = "";
                newCSCharacterBuffer = "";
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
                ImGui.PopStyleColor(); // Pop warning text color
                ImGui.TextWrapped("This feature automatically manages mod conflicts by controlling which mods are enabled per character. Use at your own risk.");
            }
            ImGui.EndChild();
            ImGui.PopStyleColor(); // Pop border color

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
    }
}

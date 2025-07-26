using System;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Dalamud.Interface;
using CharacterSelectPlugin.Windows.Styles;

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
            var dpiScale = ImGui.GetIO().DisplayFramebufferScale.X;
            var uiScale = plugin.Configuration.UIScaleMultiplier;
            var totalScale = GetSafeScale(dpiScale * uiScale);

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
            var contentWidth = ImGui.GetContentRegionAvail().X;
            var labelWidth = 140f;
            var inputWidth = contentWidth - labelWidth - 20f;

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

            // UI Scale
            DrawFixedSetting("UI Scale:", labelWidth, inputWidth, () =>
            {
                float scaleSetting = plugin.Configuration.UIScaleMultiplier;
                if (ImGui.SliderFloat("##UIScale", ref scaleSetting, 0.5f, 2.0f, "%.2fx"))
                {
                    plugin.Configuration.UIScaleMultiplier = scaleSetting;
                    plugin.Configuration.Save();
                }
                DrawTooltip("Scales the entire Character Select+ UI manually.\nUseful for high-DPI monitors (2K / 3K / 4K).");
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
        private float GetSafeScale(float baseScale)
        {
            return Math.Clamp(baseScale, 0.3f, 5.0f); // Prevent extreme scaling
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

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;
using CharacterSelectPlugin.Windows.Components;
using CharacterSelectPlugin.Windows.Styles;
using CharacterSelectPlugin.Windows.Utils;
using CharacterSelectPlugin.Effects;
using Dalamud.Interface.Textures.TextureWraps;

namespace CharacterSelectPlugin.Windows
{
    public partial class MainWindow
    {
        private void DrawClassicLayout()
{
            plugin.MainWindowPos = ImGui.GetWindowPos();
            plugin.MainWindowSize = ImGui.GetWindowSize();

            float deltaTime = ImGui.GetIO().DeltaTime;

            if (giftBoxShakeTimer > 0f)
            {
                giftBoxShakeTimer -= deltaTime;
                if (giftBoxShakeTimer < 0f) giftBoxShakeTimer = 0f;
            }

            uiStyles.PushMainWindowStyle();

            try
            {
                DrawClassicHeader();
                DrawClassicMainContent(deltaTime);
                DrawClassicBottomBar();
                DrawClassicSupportButton();

                settingsPanel.Draw();
                reorderWindow.Draw();
            }

            finally
            {
                uiStyles.PopMainWindowStyle();
            }

            diceEffect.Update(deltaTime);
            diceEffect.Draw();
            DrawSeasonalBackgroundEffects(deltaTime);
        }

        private void DrawClassicHeader()
{
            int totalCharacters = plugin.Characters.Count;
            string headerText = $"Choose your character";
            ImGui.Text(headerText);

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
            ImGui.Text($"({totalCharacters} total)");
            ImGui.PopStyleColor();

            // Idle pose indicator
            if (Plugin.ObjectTable.LocalPlayer != null)
            {
                unsafe
                {
                    var charPtr = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)Plugin.ObjectTable.LocalPlayer.Address;
                    var currentIdle = charPtr->EmoteController.CPoseState;
                    
                    var scale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;
                    
                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
                    ImGui.Text($"Current Idle: {currentIdle}");
                    ImGui.PopStyleColor();
                    
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Current idle pose: {currentIdle}");
                    }
                }
            }

            ImGui.SameLine();

            var totalScale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;

            float buttonWidth = 70 * totalScale;
            float iconButtonSize = ImGui.GetTextLineHeight() + ImGui.GetStyle().FramePadding.Y * 2;
            float buttonHeight = iconButtonSize;
            float availableWidth = ImGui.GetContentRegionAvail().X;
            float spacing = ImGui.GetStyle().ItemSpacing.X;

            bool showTrophy = plugin.Configuration.EnableAchievementSystem;

            // Right-align the icon trio (Trophy + Revert + Discord).
            int iconButtonCount = showTrophy ? 2 : 1; // Trophy + Revert  /  Revert only
            int gapCount = iconButtonCount; // gaps between icons + gap before Discord
            ImGui.SetCursorPosX(ImGui.GetCursorPosX()
                + availableWidth - buttonWidth - iconButtonSize * iconButtonCount - spacing * gapCount);

            // Trophy button , fixed-width icon button, points shown in tooltip
            if (showTrophy)
            {
                var achievementData = plugin.Configuration.AchievementData;
                bool hasUnseen = achievementData.HasUnseenAchievements;

                if (uiStyles.IconButtonWithColor("\uf091", "", new Vector2(iconButtonSize, iconButtonSize), 1.0f,
                    hasUnseen ? new Vector4(1.0f, 0.84f, 0.0f, 1.0f) : null))
                {
                    if (plugin.AchievementWindow != null)
                        plugin.AchievementWindow.IsOpen = !plugin.AchievementWindow.IsOpen;
                }

                if (ImGui.IsItemHovered())
                {
                    int totalPts = achievementData.TotalPointsEarned;
                    ImGui.SetTooltip($"Achievements: {achievementData.UnlockedCount}/{Achievements.AchievementRegistry.All.Length}\nPoints: {totalPts}\n\nClick to view achievements.");
                }

                // Steady gold particle flow when unseen achievements exist
                if (hasUnseen)
                {
                    var btnMin = ImGui.GetItemRectMin();
                    var btnMax = ImGui.GetItemRectMax();
                    var btnCentre = (btnMin + btnMax) * 0.5f;
                    float dt = ImGui.GetIO().DeltaTime;
                    var rng = Random.Shared;

                    // Spawn 1-2 particles per frame at a steady rate
                    trophySpawnTimer += dt;
                    while (trophySpawnTimer > 0.04f) // ~25 spawns/sec
                    {
                        trophySpawnTimer -= 0.04f;
                        if (trophyParticles.Count > 60) break; // cap

                        float angle = (float)(rng.NextDouble() * Math.PI * 2);
                        float speed = 18f + (float)(rng.NextDouble() * 35f);
                        float life = 0.6f + (float)(rng.NextDouble() * 0.5f);

                        // Gold/amber colour range
                        var col = new Vector4(
                            0.95f + (float)(rng.NextDouble() * 0.05f),
                            0.65f + (float)(rng.NextDouble() * 0.25f),
                            0.05f + (float)(rng.NextDouble() * 0.15f),
                            1f);

                        // Spawn at a small radius outward so they skip the dense centre
                        float spawnR = 6f + (float)(rng.NextDouble() * 4f);
                        trophyParticles.Add(new Particle
                        {
                            Position = btnCentre + new Vector2(
                                (float)Math.Cos(angle) * spawnR,
                                (float)Math.Sin(angle) * spawnR),
                            Velocity = new Vector2(
                                (float)Math.Cos(angle) * speed,
                                (float)Math.Sin(angle) * speed),
                            Color = col,
                            Life = life,
                            MaxLife = life,
                            Size = 1.2f + (float)(rng.NextDouble() * 1.5f)
                        });
                    }

                    // Update
                    for (int pi = trophyParticles.Count - 1; pi >= 0; pi--)
                    {
                        trophyParticles[pi].Update(dt);
                        if (!trophyParticles[pi].IsAlive)
                            trophyParticles.RemoveAt(pi);
                    }

                    // Draw behind everything else on this line
                    var drawList = ImGui.GetWindowDrawList();
                    foreach (var p in trophyParticles)
                    {
                        uint col32 = ImGui.GetColorU32(p.Color);
                        drawList.AddCircleFilled(p.Position, p.Size, col32, 6);

                        // Soft glow on brighter particles
                        if (p.Color.W > 0.4f)
                        {
                            var glow = new Vector4(p.Color.X, p.Color.Y, p.Color.Z, p.Color.W * 0.25f);
                            drawList.AddCircleFilled(p.Position, p.Size * 2f, ImGui.GetColorU32(glow), 8);
                        }
                    }
                }
                else if (trophyParticles.Count > 0)
                {
                    // Fade out remaining particles after clicking
                    float dt = ImGui.GetIO().DeltaTime;
                    for (int pi = trophyParticles.Count - 1; pi >= 0; pi--)
                    {
                        trophyParticles[pi].Update(dt);
                        if (!trophyParticles[pi].IsAlive)
                            trophyParticles.RemoveAt(pi);
                    }

                    var drawList = ImGui.GetWindowDrawList();
                    foreach (var p in trophyParticles)
                    {
                        drawList.AddCircleFilled(p.Position, p.Size, ImGui.GetColorU32(p.Color), 6);
                    }

                    trophySpawnTimer = 0f;
                }

                ImGui.SameLine();
            }

            // Revert button , requires Ctrl+Shift held to prevent accidental clicks
            var io = ImGui.GetIO();
            bool isRevertKeysHeld = io.KeyCtrl && io.KeyShift;
            if (uiStyles.IconButton("\uf0e2", "Revert All CS+ Changes\n\nReverts:\n• Glamourer → Game state\n• Honorific → Cleared\n• Moodles → All removed\n• Customize+ → Disabled\n• Penumbra → Your Character collection\n• CS+ → No active character\n\nHold Ctrl + Shift and click to revert.", new Vector2(iconButtonSize, iconButtonSize)))
            {
                if (isRevertKeysHeld)
                    plugin.RevertAllChanges();
            }

            ImGui.SameLine();

            // Discord button
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.39f, 0.96f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.35f, 0.39f, 0.96f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.25f, 0.29f, 0.86f, 1.0f));

            if (ImGui.Button("Discord", new Vector2(buttonWidth, buttonHeight)))
            {
                Dalamud.Utility.Util.OpenLink("https://discord.gg/8JykGErcX4");
            }

            ImGui.PopStyleColor(3);

            bool discordHovered = ImGui.IsItemHovered();

            // One-shot glossy sheen on hover-enter , drawn over the button fill.
            // Low maxAlpha (0.18f) so the Discord blue still reads clearly through the sweep.
            float discordSheen = uiStyles.UpdateAndGetHoverSweepProgress("discord_btn", discordHovered);
            if (discordSheen >= 0f)
            {
                var btnMin = ImGui.GetItemRectMin();
                var btnMax = ImGui.GetItemRectMax();
                Windows.Styles.UIStyles.DrawHoverSheen(ImGui.GetWindowDrawList(), btnMin, btnMax, discordSheen, maxAlpha: 0.18f);
            }

            if (discordHovered)
            {
                ImGui.SetTooltip("Join our Discord community!");
            }

            ImGui.Separator();
        }

        private void DrawClassicMainContent(float deltaTime)
{
            var totalScale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;

            if (plugin.IsAddCharacterWindowOpen || characterForm.IsEditWindowOpen)
            {
                characterForm.Draw();
            }

            float characterGridWidth = 0;
            if (designPanel.IsOpen)
            {
                float scaledPanelWidth = designPanel.PanelWidth * totalScale;
                characterGridWidth = -(scaledPanelWidth + 10);
            }

            // Main content area
            float bottomBarHeight = ImGui.GetFrameHeight() + (10 * totalScale);
            ImGui.BeginChild("CharacterGrid", new Vector2(characterGridWidth, -bottomBarHeight), true);

            if (plugin.Configuration.SelectedTheme == ThemeSelection.Custom)
            {
                DrawCustomBackgroundInChild();
            }

            // Valentine's background image behind character grid
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration) &&
                SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration) == SeasonalTheme.Valentines)
            {
                DrawValentinesBackgroundInChild();
            }

            // Snow behind character grid
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration))
            {
                var effectiveTheme = SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration);
                if (effectiveTheme == SeasonalTheme.Winter || effectiveTheme == SeasonalTheme.Christmas)
                {
                    winterBackgroundSnowUI.ConfigureSnowEffect(alpha: 0.5f, size: 0.7f, spawnRate: 0.8f);
                    var childWindowPos = ImGui.GetCursorScreenPos();
                    var childWindowSize = ImGui.GetContentRegionAvail();
                    winterBackgroundSnowUI.SetEffectAreaAbsolute(childWindowPos, childWindowSize);
                    winterBackgroundSnowUI.Update(deltaTime);
                    winterBackgroundSnowUI.DrawAbsolute();
                }
            }

            characterGrid.Draw();
            ImGui.EndChild();

            if (designPanel.IsOpen)
            {
                ImGui.SameLine();
                float characterGridHeight = ImGui.GetItemRectSize().Y;
                float scaledPanelWidth = designPanel.PanelWidth * totalScale;

                ImGui.BeginChild("DesignPanel", new Vector2(scaledPanelWidth, characterGridHeight), true);
                designPanel.Draw();
                ImGui.EndChild();
            }
        }

        private void DrawClassicBottomBar()
{
            var totalScale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;
            float bottomPadding = 10 * totalScale;
            ImGui.SetCursorPos(new Vector2(10 * totalScale, ImGui.GetWindowHeight() - ImGui.GetFrameHeight() - bottomPadding));

            if (uiStyles.IconButton("\uf013", "Settings"))
            {
                plugin.IsSettingsOpen = !plugin.IsSettingsOpen;
            }
            plugin.SettingsButtonPos = ImGui.GetItemRectMin();
            plugin.SettingsButtonSize = ImGui.GetItemRectSize();

            bool hasUnseenSettingsFeatures = !plugin.Configuration.SeenFeatures.Contains(FeatureKeys.CustomTheme) ||
                                              !plugin.Configuration.SeenFeatures.Contains(FeatureKeys.NameSync) ||
                                              !plugin.Configuration.SeenFeatures.Contains(FeatureKeys.JobAssignments) ||
                                              !plugin.Configuration.SeenFeatures.Contains(FeatureKeys.Honorific);
            if (hasUnseenSettingsFeatures)
            {
                var buttonMin = ImGui.GetItemRectMin();
                var buttonMax = ImGui.GetItemRectMax();
                var drawList = ImGui.GetWindowDrawList();

                // Pulsing glow effect
                float pulse = (float)(Math.Sin(ImGui.GetTime() * 3.0) * 0.5 + 0.5); // 0 to 1 pulsing
                var glowColor = new Vector4(0.2f, 1.0f, 0.4f, 0.3f + pulse * 0.5f); // Green glow
                var padding = 2 * totalScale;

                // Draw multiple layers for glow effect
                for (int i = 3; i >= 1; i--)
                {
                    var layerPadding = padding + (i * 2 * totalScale);
                    var layerAlpha = glowColor.W * (1.0f - (i * 0.25f));
                    drawList.AddRect(
                        buttonMin - new Vector2(layerPadding, layerPadding),
                        buttonMax + new Vector2(layerPadding, layerPadding),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(glowColor.X, glowColor.Y, glowColor.Z, layerAlpha)),
                        4f * totalScale,
                        ImDrawFlags.None,
                        2f * totalScale
                    );
                }
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                ImGui.BeginTooltip();
                ImGui.Text("Open Settings Menu.");
                ImGui.Text("You can find options for adjusting your Character Grid.");
                ImGui.Text("As well as the Opt-In for Glamourer Automations.");
                if (hasUnseenSettingsFeatures)
                {
                    ImGui.Spacing();
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 1.0f, 0.4f, 1.0f));
                    ImGui.Text("New features available!");
                    ImGui.PopStyleColor();
                }
                ImGui.EndTooltip();
            }

            ImGui.SameLine();

            if (ImGui.Button("Reorder Characters"))
                reorderWindow.Open();
            uiStyles.ApplyHoverSheenToLastItem("reorder_chars_btn");

            ImGui.SameLine();

            if (ImGui.Button("Quick Switch"))
                plugin.QuickSwitchWindow.IsOpen = !plugin.QuickSwitchWindow.IsOpen;
            plugin.QuickSwitchButtonPos = ImGui.GetItemRectMin();
            plugin.QuickSwitchButtonSize = ImGui.GetItemRectSize();
            uiStyles.ApplyHoverSheenToLastItem("quickswitch_btn");

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                ImGui.BeginTooltip();
                ImGui.Text("Opens a more compact UI to swap between Characters & Designs.");
                ImGui.EndTooltip();
            }

            ImGui.SameLine();

            // Gallery is under construction, button intentionally inert
            ImGui.Button("Gallery");
            plugin.GalleryButtonPos = ImGui.GetItemRectMin();
            plugin.GalleryButtonSize = ImGui.GetItemRectSize();
            uiStyles.ApplyHoverSheenToLastItem("gallery_btn");

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                ImGui.BeginTooltip();
                ImGui.Text("Gallery is under construction.");
                ImGui.Text("Check back in a future update!");
                ImGui.EndTooltip();
            }

            ImGui.SameLine();

            if (ImGui.Button("Tutorial"))
                plugin.TutorialManager.StartTutorial();
            uiStyles.ApplyHoverSheenToLastItem("tutorial_btn");
            ImGui.SameLine();

            // Features button with notification badge
            bool hasUnseenFeaturesGuide = !plugin.Configuration.SeenFeatures.Contains(FeatureKeys.FeaturesGuide);
            if (hasUnseenFeaturesGuide)
            {
                var btnPos = ImGui.GetCursorScreenPos();
                var drawList = ImGui.GetWindowDrawList();

                if (ImGui.Button("Features"))
                {
                    if (plugin.FeaturesWindow != null)
                    {
                        plugin.FeaturesWindow.IsOpen = !plugin.FeaturesWindow.IsOpen;
                        plugin.Configuration.SeenFeatures.Add(FeatureKeys.FeaturesGuide);
                        plugin.Configuration.Save();
                        plugin.AchievementTracker?.OnFeaturesGuideOpened();
                    }
                }
                uiStyles.ApplyHoverSheenToLastItem("features_btn");

                // Draw notification glow
                var btnSize = ImGui.GetItemRectSize();
                float pulse = (float)(Math.Sin(ImGui.GetTime() * 3.0) * 0.5 + 0.5);
                var glowColor = new Vector4(0.2f, 1.0f, 0.4f, 0.3f + pulse * 0.4f);
                for (int i = 3; i >= 1; i--)
                {
                    var expand = i * 2f;
                    drawList.AddRect(
                        btnPos - new Vector2(expand, expand),
                        btnPos + btnSize + new Vector2(expand, expand),
                        ImGui.ColorConvertFloat4ToU32(glowColor * (1f - i * 0.2f)),
                        4f, ImDrawFlags.None, 2f);
                }
            }
            else
            {
                if (ImGui.Button("Features"))
                {
                    if (plugin.FeaturesWindow != null)
                        plugin.FeaturesWindow.IsOpen = !plugin.FeaturesWindow.IsOpen;
                }
                uiStyles.ApplyHoverSheenToLastItem("features_btn");
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                ImGui.BeginTooltip();
                ImGui.Text("Discover all the features CS+ has to offer!");
                ImGui.Text("Tips, tricks, and hidden gems.");
                ImGui.EndTooltip();
            }
            ImGui.SameLine();

            if (ImGui.Button("Patch Notes"))
            {
                plugin.PatchNotesWindow.OpenMainMenuOnClose = false;
                plugin.PatchNotesWindow.IsOpen = !plugin.PatchNotesWindow.IsOpen;
            }
            uiStyles.ApplyHoverSheenToLastItem("patchnotes_btn");

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                ImGui.BeginTooltip();
                ImGui.Text("View what's new in Character Select+");
                ImGui.Text("See the latest features and updates!");
                ImGui.EndTooltip();
            }

            ImGui.SameLine();

            // Random button with seasonal icons
            var effectiveTheme = SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration)
                ? SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration)
                : SeasonalTheme.Default;
            bool isHalloween = effectiveTheme == SeasonalTheme.Halloween;
            bool isWinterChristmas = effectiveTheme == SeasonalTheme.Winter || effectiveTheme == SeasonalTheme.Christmas;
            bool isValentines = effectiveTheme == SeasonalTheme.Valentines;

            string randomIcon;
            Vector4? iconColor = null;

            if (isHalloween)
            {
                randomIcon = "\uf492"; // Skull
                iconColor = new Vector4(0.2f, 0.8f, 0.3f, 1.0f);
            }
            else if (isWinterChristmas)
            {
                randomIcon = "\uf06b"; // Gift
                iconColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
            }
            else if (isValentines)
            {
                randomIcon = "\uf564"; // Cookie-bite
                iconColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f); // White
            }
            else
            {
                randomIcon = "\uf522"; // Dice
            }

            string randomTooltip = plugin.Configuration.RandomSelectionFavoritesOnly
                ? "Randomly selects from favourited characters and designs only"
                : "Randomly selects from all characters and designs";

            // Shake effect for gift box
            Vector2 shakeOffset = Vector2.Zero;
            if (isWinterChristmas && giftBoxShakeTimer > 0f)
            {
                float shakeIntensity = 2.0f;
                float shakeProgress = 1.0f - (giftBoxShakeTimer / GIFT_BOX_SHAKE_DURATION);
                float shakeAmount = shakeIntensity * (1.0f - shakeProgress);

                float time = giftBoxShakeTimer * 20f;
                shakeOffset.X = MathF.Sin(time * 1.7f) * shakeAmount;
                shakeOffset.Y = MathF.Cos(time * 2.3f) * shakeAmount;

                ImGui.SetCursorPos(ImGui.GetCursorPos() + shakeOffset);
            }

            if (uiStyles.IconButtonWithColor(randomIcon, randomTooltip, null, 1.0f, iconColor))
            {
                if (isWinterChristmas)
                    giftBoxShakeTimer = GIFT_BOX_SHAKE_DURATION;

                Vector2 effectPos = ImGui.GetItemRectMin() + ImGui.GetItemRectSize() / 2;
                diceEffect.Trigger(effectPos, true, plugin.Configuration);
                plugin.SelectRandomCharacterAndDesign();
            }

        }

        private void DrawClassicSupportButton()
{
            var totalScale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;

            Vector2 windowPos = ImGui.GetWindowPos();
            Vector2 windowSize = ImGui.GetWindowSize();
            float buttonWidth = 105 * totalScale;
            float buttonHeight = ImGui.GetFrameHeight(); // Use same height as other buttons
            float padding = 10 * totalScale; // Match the bottom bar padding
            

            ImGui.SetCursorScreenPos(new Vector2(
                windowPos.X + windowSize.X - buttonWidth - padding,
                windowPos.Y + windowSize.Y - buttonHeight - padding
            ));

            if (ImGui.Button("##SupportDev", new Vector2(buttonWidth, buttonHeight)))
                Dalamud.Utility.Util.OpenLink("https://ko-fi.com/icarusxiv");
            uiStyles.ApplyHoverSheenToLastItem("supportdev_btn", maxAlpha: 0.16f);

            // Draw coloured border glow (like character cards)
            var drawList = ImGui.GetWindowDrawList();
            Vector2 rectMin = ImGui.GetItemRectMin();
            Vector2 rectMax = ImGui.GetItemRectMax();
            bool isHovered = ImGui.IsItemHovered();

            // Pulsing glow intensity when hovered
            float pulse = isHovered ? 0.7f + 0.3f * (float)Math.Sin(ImGui.GetTime() * 4.0) : 0.5f;
            float thickness = isHovered ? 2.0f : 1.5f;

            // Ko-fi brand colour (coral/salmon pink)
            var glowColor = new Vector4(1.0f, 0.45f, 0.52f, pulse);
            uint borderColor = ImGui.ColorConvertFloat4ToU32(glowColor);

            drawList.AddRect(rectMin, rectMax, borderColor, 4.0f, ImDrawFlags.None, thickness);

            // Heart icon + text (centered vertically)
            float textHeight = ImGui.GetFontSize();
            float buttonHeight2 = rectMax.Y - rectMin.Y;
            float yOffset = (buttonHeight2 - textHeight) * 0.5f - 1f; // -1 to nudge up slightly
            Vector2 textPos = rectMin + new Vector2(6 * totalScale, yOffset);

            // Heart colour - white for Valentine's theme (to stand out), otherwise match border
            bool isValentinesTheme = SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration) &&
                                     SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration) == SeasonalTheme.Valentines;
            uint heartColor = isValentinesTheme
                ? ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 1.0f, 1.0f, 1.0f)) // White for Valentine's
                : ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 0.45f, 0.52f, 1.0f)); // Match border
            drawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize(), textPos, heartColor, "\uf004");
            drawList.AddText(textPos + new Vector2(22 * totalScale, 0), ImGui.GetColorU32(ImGuiCol.Text), "Support Dev");

            if (isHovered)
            {
                ImGui.SetTooltip("Enjoy Character Select+? Consider supporting development on Ko-fi!");
            }
        }

    }
}

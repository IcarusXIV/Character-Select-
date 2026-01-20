using System;
using System.IO;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using CharacterSelectPlugin.Windows.Components;
using CharacterSelectPlugin.Windows.Styles;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using CharacterSelectPlugin.Effects;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace CharacterSelectPlugin.Windows
{
    public class MainWindow : Window, IDisposable
    {
        private Plugin plugin;
        private CharacterGrid characterGrid;
        private CharacterForm characterForm;
        private DesignPanel designPanel;
        private SettingsPanel settingsPanel;
        private ReorderWindow reorderWindow;
        private UIStyles uiStyles;
        private FavoriteSparkEffect diceEffect = new();
        private WinterBackgroundSnow winterBackgroundSnow = new();
        private WinterBackgroundSnow winterBackgroundSnowUI = new(); // Second snow effect for character grid area
        private float giftBoxShakeTimer = 0f;
        private const float GIFT_BOX_SHAKE_DURATION = 0.3f;

        // Custom theme background image path (texture fetched fresh each frame)
        private string? _lastLoggedBackgroundPath;
        public bool IsDesignPanelOpen => designPanel?.IsOpen ?? false;
        public bool IsEditCharacterWindowOpen => characterForm?.IsEditWindowOpen ?? false;
        public bool IsReorderWindowOpen => reorderWindow?.IsOpen ?? false;
        
        public DesignPanel? GetDesignPanel() => designPanel;

        public MainWindow(Plugin plugin)
            : base("Character Select+", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoDocking)
        {
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(850, 700),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };

            this.plugin = plugin;
            this.uiStyles = new UIStyles(plugin);

            this.characterGrid = new CharacterGrid(plugin, uiStyles);
            this.characterForm = new CharacterForm(plugin, uiStyles);
            this.designPanel = new DesignPanel(plugin, uiStyles);
            this.settingsPanel = new SettingsPanel(plugin, uiStyles, this);
            this.reorderWindow = new ReorderWindow(plugin, uiStyles);

            // Pre-warm the file cache on a background thread to prevent UI freezing
            // when opening the window for the first time (especially for network paths)
            characterGrid.PreWarmCacheAsync();
        }

        public override void PreDraw()
        {
            uiStyles.PushCustomWindowBgIfNeeded();
        }

        public override void PostDraw()
        {
            uiStyles.PopCustomWindowBgIfNeeded();
        }

        public void InvalidateLayout()
        {
            characterGrid?.InvalidateCache();
        }

        public void Dispose()
        {
            characterGrid?.Dispose();
            characterForm?.Dispose();
            designPanel?.Dispose();
            settingsPanel?.Dispose();
            reorderWindow?.Dispose();
        }
        
        private void DrawSeasonalBackgroundEffects(float deltaTime)
        {
            if (!SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration))
                return;

            var effectiveTheme = SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration);

            if (effectiveTheme == SeasonalTheme.Winter || effectiveTheme == SeasonalTheme.Christmas)
            {
                var windowSize = ImGui.GetWindowSize();
                winterBackgroundSnow.SetEffectArea(windowSize);
                winterBackgroundSnow.Update(deltaTime);
                winterBackgroundSnow.Draw();
            }
        }

        /// <summary>Draws custom background image in current child window.</summary>
        private void DrawCustomBackgroundInChild()
        {
            var config = plugin.Configuration.CustomTheme;
            if (string.IsNullOrEmpty(config.BackgroundImagePath))
                return;

            if (!File.Exists(config.BackgroundImagePath))
                return;

            var texture = Plugin.TextureProvider
                .GetFromFile(config.BackgroundImagePath)
                .GetWrapOrDefault();

            var childPos = ImGui.GetWindowPos();
            var childSize = ImGui.GetWindowSize();
            var drawList = ImGui.GetWindowDrawList();

            if (texture == null)
                return;

            if (_lastLoggedBackgroundPath != config.BackgroundImagePath)
            {
                Plugin.Log.Info($"[CustomBG] Loaded! Size: {texture.Width}x{texture.Height}");
                _lastLoggedBackgroundPath = config.BackgroundImagePath;
            }

            // Calculate base image size (cover, maintain aspect ratio)
            var imageAspect = (float)texture.Width / texture.Height;
            var windowAspect = childSize.X / childSize.Y;

            Vector2 baseImageSize;

            if (imageAspect > windowAspect)
            {
                baseImageSize.Y = childSize.Y;
                baseImageSize.X = childSize.Y * imageAspect;
            }
            else
            {
                baseImageSize.X = childSize.X;
                baseImageSize.Y = childSize.X / imageAspect;
            }

            // Zoom
            var zoom = Math.Clamp(config.BackgroundImageZoom, 0.5f, 3.0f);
            var imageSize = baseImageSize * zoom;

            var centeredOffset = (childSize - imageSize) / 2;

            // User offset
            var userOffsetX = config.BackgroundImageOffsetX * (imageSize.X - childSize.X) * 0.5f;
            var userOffsetY = config.BackgroundImageOffsetY * (imageSize.Y - childSize.Y) * 0.5f;
            var finalOffset = centeredOffset + new Vector2(userOffsetX, userOffsetY);

            var tintColor = new Vector4(1, 1, 1, config.BackgroundImageOpacity);

            drawList.PushClipRect(childPos, childPos + childSize, true);

            drawList.AddImage(
                texture.Handle,
                childPos + finalOffset,
                childPos + finalOffset + imageSize,
                Vector2.Zero,
                Vector2.One,
                ImGui.ColorConvertFloat4ToU32(tintColor)
            );

            drawList.PopClipRect();
        }

        public override void Draw()
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
                DrawHeader();
                DrawMainContent(deltaTime);
                DrawBottomBar();
                DrawSupportButton();

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

        private void DrawHeader()
        {
            int totalCharacters = plugin.Characters.Count;
            string headerText = $"Choose your character";
            ImGui.Text(headerText);

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
            ImGui.Text($"({totalCharacters} total)");
            ImGui.PopStyleColor();

            // Idle pose indicator
            if (Plugin.ClientState.LocalPlayer != null)
            {
                unsafe
                {
                    var charPtr = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)Plugin.ClientState.LocalPlayer.Address;
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
            float buttonHeight = ImGui.GetTextLineHeight() + ImGui.GetStyle().FramePadding.Y * 2;
            float availableWidth = ImGui.GetContentRegionAvail().X;

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availableWidth - buttonWidth);

            // Discord button
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.39f, 0.96f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.35f, 0.39f, 0.96f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.25f, 0.29f, 0.86f, 1.0f));

            if (ImGui.Button("Discord", new Vector2(buttonWidth, buttonHeight)))
            {
                Dalamud.Utility.Util.OpenLink("https://discord.gg/8JykGErcX4");
            }

            ImGui.PopStyleColor(3);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Join our Discord community!");
            }

            ImGui.Separator();
        }


        public void UpdateSortType()
        {
            characterGrid.SetSortType((Plugin.SortType)plugin.Configuration.CurrentSortIndex);
        }

        private void DrawMainContent(float deltaTime)
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
        public void OpenAddCharacterWindow(bool secretMode = false)
        {
            characterForm.ResetFields();
            if (secretMode)
            {
                characterForm.SetSecretMode(true);
            }
            plugin.IsAddCharacterWindowOpen = true;
        }

        public void CloseAddCharacterWindow()
        {
            plugin.IsAddCharacterWindowOpen = false;
            characterForm.SetSecretMode(false);
        }

        private void DrawBottomBar()
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

            ImGui.SameLine();

            if (ImGui.Button("Quick Switch"))
                plugin.QuickSwitchWindow.IsOpen = !plugin.QuickSwitchWindow.IsOpen;
            plugin.QuickSwitchButtonPos = ImGui.GetItemRectMin();
            plugin.QuickSwitchButtonSize = ImGui.GetItemRectSize();

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                ImGui.BeginTooltip();
                ImGui.Text("Opens a more compact UI to swap between Characters & Designs.");
                ImGui.EndTooltip();
            }

            ImGui.SameLine();

            if (ImGui.Button("Gallery"))
                plugin.GalleryWindow.IsOpen = !plugin.GalleryWindow.IsOpen;
            plugin.GalleryButtonPos = ImGui.GetItemRectMin();
            plugin.GalleryButtonSize = ImGui.GetItemRectSize();

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                ImGui.BeginTooltip();
                ImGui.Text("Browse the Character Showcase Gallery");
                ImGui.Text("See other players' characters and share your own!");
                ImGui.EndTooltip();
            }

            ImGui.SameLine();

            if (ImGui.Button("Tutorial"))
                plugin.TutorialManager.StartTutorial();
            ImGui.SameLine();

            if (ImGui.Button("Patch Notes"))
            {
                plugin.PatchNotesWindow.OpenMainMenuOnClose = false;
                plugin.PatchNotesWindow.IsOpen = !plugin.PatchNotesWindow.IsOpen;
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                ImGui.BeginTooltip();
                ImGui.Text("View what's new in Character Select+");
                ImGui.Text("See the latest features and updates!");
                ImGui.EndTooltip();
            }

            ImGui.SameLine();

            // Random button with seasonal icons
            bool isHalloween = SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration) &&
                              SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration) == SeasonalTheme.Halloween;
            bool isWinterChristmas = SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration) &&
                                    (SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration) == SeasonalTheme.Winter ||
                                     SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration) == SeasonalTheme.Christmas);

            string randomIcon;
            Vector4? iconColor = null;

            if (isHalloween)
            {
                randomIcon = "\uf492";
                iconColor = new Vector4(0.2f, 0.8f, 0.3f, 1.0f);
            }
            else if (isWinterChristmas)
            {
                randomIcon = "\uf06b";
                iconColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
            }
            else
            {
                randomIcon = "\uf522";
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

        private void DrawSupportButton()
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
            uint heartColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 0.45f, 0.52f, 1.0f)); // Match border
            drawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize(), textPos, heartColor, "\uf004");
            drawList.AddText(textPos + new Vector2(22 * totalScale, 0), ImGui.GetColorU32(ImGuiCol.Text), "Support Dev");

            if (isHovered)
            {
                ImGui.SetTooltip("Enjoy Character Select+? Consider supporting development on Ko-fi!");
            }
        }

        public void OpenEditCharacterWindow(int index) => characterForm.OpenEditCharacterWindow(index);
        public void OpenDesignPanel(int characterIndex) => designPanel.Open(characterIndex);
        public void CloseDesignPanel() => designPanel.Close();
        public void SortCharacters() => characterGrid.SortCharacters();

        /// <summary>Opens the settings panel and navigates to a specific section.</summary>
        public void SwitchToSettingsSection(string sectionName)
        {
            plugin.IsSettingsOpen = true;
            settingsPanel.ExpandSection(sectionName);
        }
    }
}

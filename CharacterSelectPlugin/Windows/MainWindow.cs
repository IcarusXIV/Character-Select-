using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public void PlayNewCharacterAnimation(Character newChar)
            => characterGrid?.PlayNewCharacterAnimation(newChar);

        private CharacterForm characterForm;
        private DesignPanel designPanel;
        private SettingsPanel settingsPanel;
        private ReorderWindow reorderWindow;
        private UIStyles uiStyles;
        private FavoriteSparkEffect diceEffect = new();
        private List<Particle> trophyParticles = new();
        private float trophySpawnTimer = 0f;
        private WinterBackgroundSnow winterBackgroundSnow = new();
        private WinterBackgroundSnow winterBackgroundSnowUI = new(); // Second snow effect for character grid area
        private ValentinesHeartsEffect valentinesHeartsEffect = new(); // Floating hearts for Valentine's Day
        private float giftBoxShakeTimer = 0f;
        private const float GIFT_BOX_SHAKE_DURATION = 0.3f;

        // Fly text - small one-shot floating labels (e.g. "nice!" at 69 chars).
        // Each entry rises + fades out over ~1.6s, then is evicted.
        // Custom theme background image path (texture fetched fresh each frame)
        private string? _lastLoggedBackgroundPath;

        // Search input buffer (grid reads via characterGrid.SearchQuery)
        private string searchInput = "";
        private bool searchFieldHasFocus = false;
        private bool tagDropdownPopupOpen = false;
        // Hover-time tracking for sheen-on-enter animations
        private readonly System.Collections.Generic.Dictionary<string, bool> hoverPrev = new();
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
            // WindowBg must be pushed BEFORE Begin (committed at Begin time).
            // Use the same theme-aware colour the chassis paints with so any
            // ImGui-bg sliver around the content matches.
            ImGui.PushStyleColor(ImGuiCol.WindowBg, GetEffectiveChassisBg());
        }

        public override void PostDraw()
        {
            ImGui.PopStyleColor();
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
            else if (effectiveTheme == SeasonalTheme.Valentines)
            {
                var windowSize = ImGui.GetWindowSize();
                valentinesHeartsEffect.SetEffectArea(windowSize);
                valentinesHeartsEffect.Update(deltaTime);
                valentinesHeartsEffect.Draw();
            }
        }

        /// <summary>Theme-aware chassis fill so seasonal tints aren't blanked out.</summary>
        private Vector4 GetEffectiveChassisBg()
        {
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration))
            {
                var seasonal = SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration);
                switch (seasonal)
                {
                    case SeasonalTheme.Halloween: return new Vector4(0.08f, 0.04f, 0.02f, 0.98f);
                    case SeasonalTheme.Winter:    return new Vector4(0.12f, 0.16f, 0.22f, 0.98f);
                    case SeasonalTheme.Christmas: return new Vector4(0.25f, 0.05f, 0.05f, 0.98f);
                    case SeasonalTheme.Valentines: return new Vector4(0.38f, 0.10f, 0.25f, 0.98f);
                }
            }
            return Boutique.Surface0;
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

        /// <summary>Draws hearts.jpg background in current child window for Valentine's theme.</summary>
        private void DrawValentinesBackgroundInChild()
        {
            var heartsPath = Path.Combine(plugin.PluginDirectory, "Assets", "hearts.jpg");
            if (!File.Exists(heartsPath))
                return;

            var texture = Plugin.TextureProvider
                .GetFromFile(heartsPath)
                .GetWrapOrDefault();

            if (texture == null)
                return;

            var childPos = ImGui.GetWindowPos();
            var childSize = ImGui.GetWindowSize();
            var drawList = ImGui.GetWindowDrawList();

            // Cover the child window, maintaining aspect ratio
            var imageAspect = (float)texture.Width / texture.Height;
            var windowAspect = childSize.X / childSize.Y;

            Vector2 imageSize;
            if (imageAspect > windowAspect)
            {
                imageSize.Y = childSize.Y;
                imageSize.X = childSize.Y * imageAspect;
            }
            else
            {
                imageSize.X = childSize.X;
                imageSize.Y = childSize.X / imageAspect;
            }

            var offset = (childSize - imageSize) / 2;
            var tintColor = new Vector4(1, 1, 1, 0.5f);

            drawList.PushClipRect(childPos, childPos + childSize, true);
            drawList.AddImage(
                texture.Handle,
                childPos + offset,
                childPos + offset + imageSize,
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
            // Encore-pattern: zero window padding so the chrome bleeds edge-to-edge.
            // (WindowBorderSize is pushed in PreDraw, must be set before Begin.)
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

            try
            {
                DrawBoutiqueChassis(deltaTime);

                settingsPanel.Draw();
                reorderWindow.Draw();
            }

            finally
            {
                ImGui.PopStyleVar(2);
                uiStyles.PopMainWindowStyle();
            }

            diceEffect.Update(deltaTime);
            diceEffect.Draw();
            DrawSeasonalBackgroundEffects(deltaTime);
        }

        // BOUTIQUE CHASSIS, translation of design-mockups/final/main.html
        // Layout (top → bottom):
        //   Meta ribbon (30px) → Action bar (56px) → Sort subbar (38px)
        //   → Content (grid + dp-edge + dp-panel) → Footer (38px)
        private void DrawBoutiqueChassis(float deltaTime)
        {
            var totalScale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;
            var dl = ImGui.GetWindowDrawList();
            // Use cursor-based positioning so chrome sits BELOW Dalamud's title bar.
            // GetWindowPos returns the outer top-left (under the title bar);
            // GetCursorScreenPos returns the content-area top.
            var winPos = ImGui.GetCursorScreenPos();
            var winSize = ImGui.GetContentRegionAvail();
            var winMax = winPos + winSize;
            double time = ImGui.GetTime();

            float ribbonH = 30f * totalScale;
            float actionH = 56f * totalScale;
            float subbarH = 38f * totalScale;
            float footerH = 40f * totalScale;

            var ribbonMin = winPos;
            var ribbonMax = new Vector2(winMax.X, winPos.Y + ribbonH);
            var actionMin = new Vector2(winPos.X, ribbonMax.Y);
            var actionMax = new Vector2(winMax.X, actionMin.Y + actionH);
            var subbarMin = new Vector2(winPos.X, actionMax.Y);
            var subbarMax = new Vector2(winMax.X, subbarMin.Y + subbarH);
            var footerMin = new Vector2(winPos.X, winMax.Y - footerH);
            var footerMax = winMax;
            var contentMin = new Vector2(winPos.X, subbarMax.Y);
            // BUG fix: previously this was `footerMin` which has X=winPos.X,
            // collapsing the content rect width to zero and pushing dp-edge +
            // dp-panel off-screen to the left. Use winMax.X for the right edge.
            var contentMax = new Vector2(winMax.X, footerMin.Y);

            // Background fill: seasonal themes get their tinted bg; Custom
            // theme defers to the user's color.windowBg via Boutique.Surface0;
            // default falls back to Boutique.Surface0.
            Vector4 chassisBg = GetEffectiveChassisBg();
            dl.AddRectFilled(winPos, winMax, Boutique.U32(chassisBg));

            // Chrome
            DrawMetaRibbon(dl, ribbonMin, ribbonMax, totalScale, time);
            DrawActionBar(dl, actionMin, actionMax, totalScale, time);

            // When the form is open the sort tabs / TAGS / REORDER strip is
            // confusing, those controls don't apply to a form. Skip the sort
            // subbar and extend content upward to use that space.
            bool formIsOpen = plugin.IsAddCharacterWindowOpen || characterForm.IsEditWindowOpen;
            if (!formIsOpen)
            {
                DrawSortSubbar(dl, subbarMin, subbarMax, totalScale, time);
            }
            else
            {
                contentMin = new Vector2(winPos.X, actionMax.Y);
            }

            // Content (grid + dp-edge + dp-panel).  Grid depth shadows are
            // drawn INSIDE the child window (on its own draw list) so they
            // sit on top of card content, see DrawContentArea.
            DrawContentArea(dl, contentMin, contentMax, totalScale, deltaTime, time);

            // Footer
            DrawFooter(dl, footerMin, footerMax, totalScale);

            // Window brackets, drawn LAST so they sit atop everything
            Boutique.DrawWindowBrackets(dl, winPos, winMax, totalScale);
        }

        // ── Meta ribbon (30px) ─────────────────────────────────────────
        private void DrawMetaRibbon(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale, double time)
        {
            Boutique.DrawRibbonBackground(dl, min, max, scale);

            float padX = 14f * scale;
            float midY = (min.Y + max.Y) * 0.5f;

            // Pulsing gold pip
            var pipCentre = new Vector2(min.X + padX + 3f * scale, midY);
            Boutique.DrawGoldPip(dl, pipCentre, scale, time);

            // Meta text, tracked-caps. Numbers in gold (HTML: .ribbon-meta .soft b { color: gold; }).
            int total = plugin.Characters?.Count ?? 0;
            int favs = plugin.Characters?.Count(c => c.IsFavorite) ?? 0;
            var activeChar = plugin.GetActiveCharacter() ?? plugin.activeCharacter;
            string activeName = activeChar?.Name ?? "";
            float trackPx = 2f * scale;
            string totalNum = total.ToString();
            string favNum = favs.ToString();

            using (Plugin.Instance?.OswaldMed11?.Push())
            {
                float yText = midY - ImGui.GetFontSize() * 0.5f;
                float x = pipCentre.X + 14f * scale;

                // "CHARACTERS"
                x += Boutique.DrawTrackedText(dl, new Vector2(x, yText), "CHARACTERS",
                    Boutique.U32(Boutique.Text), trackPx);
                x += 10f * scale;
                // sep
                dl.AddText(new Vector2(x, yText), Boutique.U32(Boutique.TextGhost), "·");
                x += ImGui.CalcTextSize("·").X + 10f * scale;
                // "<gold>X</gold> TOTAL"
                x += Boutique.DrawTrackedText(dl, new Vector2(x, yText), totalNum,
                    Boutique.U32(Boutique.Gold), trackPx);
                x += 4f * scale;
                x += Boutique.DrawTrackedText(dl, new Vector2(x, yText), "TOTAL",
                    Boutique.U32(Boutique.TextDim), trackPx);
                x += 10f * scale;
                // sep
                dl.AddText(new Vector2(x, yText), Boutique.U32(Boutique.TextGhost), "·");
                x += ImGui.CalcTextSize("·").X + 10f * scale;
                // "<gold>Y</gold> FAVOURITES"
                x += Boutique.DrawTrackedText(dl, new Vector2(x, yText), favNum,
                    Boutique.U32(Boutique.Gold), trackPx);
                x += 4f * scale;
                x += Boutique.DrawTrackedText(dl, new Vector2(x, yText), "FAVOURITES",
                    Boutique.U32(Boutique.TextDim), trackPx);

                // Active applied (if any), to the right of the meta text
                if (!string.IsNullOrWhiteSpace(activeName))
                {
                    x += 14f * scale;
                    dl.AddText(new Vector2(x, yText), Boutique.U32(Boutique.TextGhost), "·");
                    x += ImGui.CalcTextSize("·").X + 10f * scale;
                    // np-cyan SQUARE pip (matches patch notes / achievements / wardrobe)
                    Boutique.DrawSquarePip(dl, new Vector2(x + 3f * scale, midY), 3f * scale, Boutique.NpCyan);
                    x += 14f * scale;
                    string appliedSeg = $"{activeName.ToUpperInvariant()} APPLIED";
                    Boutique.DrawTrackedText(dl, new Vector2(x, yText), appliedSeg,
                        Boutique.U32(Boutique.Text), trackPx);
                }

                // Right side: page indicator with gold numbers
                int totalPages = characterGrid?.TotalPageCount ?? 1;
                int curPage = (characterGrid?.CurrentPage ?? 0) + 1;
                // Compose: "PAGE <gold>X</gold> OF <gold>Y</gold>", measure all parts to right-align.
                string pPage = "PAGE";
                string pCur = curPage.ToString();
                string pOf = "OF";
                string pTot = totalPages.ToString();
                float wPage = Boutique.MeasureTrackedText(pPage, trackPx);
                float wCur  = Boutique.MeasureTrackedText(pCur, trackPx);
                float wOf   = Boutique.MeasureTrackedText(pOf, trackPx);
                float wTot  = Boutique.MeasureTrackedText(pTot, trackPx);
                float gap = 5f * scale;
                float totalW = wPage + gap + wCur + gap + wOf + gap + wTot;
                float rx = max.X - padX - totalW;
                rx += Boutique.DrawTrackedText(dl, new Vector2(rx, yText), pPage, Boutique.U32(Boutique.TextFaint), trackPx);
                rx += gap;
                rx += Boutique.DrawTrackedText(dl, new Vector2(rx, yText), pCur, Boutique.U32(Boutique.Gold), trackPx);
                rx += gap;
                rx += Boutique.DrawTrackedText(dl, new Vector2(rx, yText), pOf, Boutique.U32(Boutique.TextFaint), trackPx);
                rx += gap;
                Boutique.DrawTrackedText(dl, new Vector2(rx, yText), pTot, Boutique.U32(Boutique.Gold), trackPx);
            }
        }

        // ── Action bar (56px): + Add Character | spacer | Search | icon bar ──
        private void DrawActionBar(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale, double time)
        {
            // Background: gold radial wash bottom-left + dark vertical gradient
            uint topCol = Boutique.U32(new Vector4(0x0C / 255f, 0x0E / 255f, 0x14 / 255f, 1f));
            uint botCol = Boutique.U32(Boutique.Bg);
            dl.AddRectFilledMultiColor(min, max, topCol, topCol, botCol, botCol);
            Boutique.DrawAuroraSpot(dl,
                new Vector2(min.X + 60f * scale, max.Y + 10f * scale),
                250f * scale, 50f * scale,
                Boutique.WithAlpha(Boutique.Gold, 0.045f), layers: 8);
            // Bottom border
            dl.AddLine(new Vector2(min.X, max.Y - 1f * scale),
                       new Vector2(max.X, max.Y - 1f * scale),
                       Boutique.U32(Boutique.BorderSoft), 1f * scale);

            float padX = 16f * scale;
            float midY = (min.Y + max.Y) * 0.5f;

            // ── + Add Character gold pill (left) ──
            // Uses the DEFAULT ImGui font (Dalamud rasterises it at the right DPI).
            // Custom Oswald handles are baked at fixed pixel sizes and get bilinear-
            // scaled at runtime when GlobalScale > 1, which read as "blurry".
            float trackPx = 2.0f * scale;
            Vector2 pillSize = Boutique.DrawGoldPillSize("ADD CHARACTER", trackPx, scale);
            pillSize.Y += 4f * scale;  // a bit more vertical breathing room

            var pillMin = new Vector2(min.X + padX, midY - pillSize.Y * 0.5f);
            var pillMax = pillMin + pillSize;

            ImGui.SetCursorScreenPos(pillMin);
            bool addClicked = ImGui.InvisibleButton("##addchar_btn", pillSize);
            bool addHovered = ImGui.IsItemHovered();
            plugin.AddCharacterButtonPos = pillMin;
            plugin.AddCharacterButtonSize = pillSize;

            Boutique.DrawGoldPill(dl, pillMin, pillMax, "ADD CHARACTER", trackPx, scale, addHovered);
            float sheen = uiStyles.UpdateAndGetHoverSweepProgress("addchar_pill", addHovered);
            if (sheen >= 0f) Windows.Styles.UIStyles.DrawHoverSheen(dl, pillMin, pillMax, sheen, maxAlpha: 0.30f);

            if (addClicked)
            {
                var io = ImGui.GetIO();
                bool isSecretMode = io.KeyCtrl && io.KeyShift;
                plugin.OpenAddCharacterWindow();
                if (isSecretMode) plugin.IsSecretMode = isSecretMode;
                characterGrid.InvalidateCache();
            }

            // ── Icon bar (right, anchored to the right edge), slightly compact ──
            // Icons (left → right): Random, Quick Switch, Features (with new dot), Trophy, Gallery
            // | divider | Settings, Revert, Discord
            float iconSize = 26f * scale;
            float iconGap = 3f * scale;
            float dividerW = 1f * scale;
            float dividerMargin = 6f * scale;

            string[] leftIconGlyphs  = { "", "", "", "", "" };
            string[] leftIconKeys    = { "random",  "qswitch", "features", "trophy",  "gallery" };
            Vector4[] leftIconHovers = { Boutique.GoldWarm, Boutique.CyanSoft, Boutique.NpAmber, Boutique.Gold, Boutique.CyanSoft };

            // Seasonal theme: swap the Random icon glyph + hover tint to match
            // the active decoration. Other icons keep their default look.
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration))
            {
                switch (SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration))
                {
                    case SeasonalTheme.Halloween:
                        // Vial / potion icon
                        leftIconGlyphs[0] = "\uf492"; leftIconHovers[0] = new Vector4(0.65f, 0.95f, 0.45f, 1f); break;
                    case SeasonalTheme.Winter:
                        // Gift icon (matches Christmas - both seasonal gift theme)
                        leftIconGlyphs[0] = "\uf06b"; leftIconHovers[0] = new Vector4(0.55f, 0.78f, 1.00f, 1f); break;
                    case SeasonalTheme.Christmas:
                        leftIconGlyphs[0] = "\uf06b"; leftIconHovers[0] = new Vector4(1f, 0.45f, 0.40f, 1f); break;
                    case SeasonalTheme.Valentines:
                        leftIconGlyphs[0] = "\uf004"; leftIconHovers[0] = new Vector4(1f, 0.35f, 0.55f, 1f); break;
                }
            }
            string[] leftIconTooltips = {
                plugin.Configuration.RandomSelectionFavoritesOnly
                    ? "Randomly selects from favourited characters and designs only"
                    : "Randomly selects from all characters and designs",
                "Opens a more compact UI to swap between Characters & Designs.",
                "Discover all the features CS+ has to offer!\nTips, tricks, and hidden gems.",
                $"Achievements: {plugin.Configuration.AchievementData?.UnlockedCount ?? 0}/{Achievements.AchievementRegistry.All.Length}\nPoints: {plugin.Configuration.AchievementData?.TotalPointsEarned ?? 0}\n\nClick to view achievements.",
                "Gallery is under construction.\nCheck back in a future update!"
            };

            string[] rightIconGlyphs  = { "", "", "" };
            string[] rightIconKeys    = { "settings", "revert", "discord" };
            Vector4[] rightIconHovers = { Boutique.Text, Boutique.Magenta, Boutique.Cyan };
            string[] rightIconTooltips = {
                "Open Settings Menu.\nYou can find options for adjusting your Character Grid.\nAs well as the Opt-In for Glamourer Automations.",
                "Revert All CS+ Changes\n\nReverts:\n• Glamourer → Game state\n• Honorific → Cleared\n• Moodles → All removed\n• Customize+ → Disabled\n• Penumbra → Your Character collection\n• CS+ → No active character\n\nHold Ctrl + Shift and click to revert.",
                "Join our Discord community!"
            };

            int leftCount = leftIconGlyphs.Length;
            int rightCount = rightIconGlyphs.Length;
            float iconBarW = leftCount * iconSize + (leftCount - 1) * iconGap
                + dividerMargin * 2 + dividerW
                + rightCount * iconSize + (rightCount - 1) * iconGap;

            float iconY = midY - iconSize * 0.5f;
            float iconStartX = max.X - padX - iconBarW;
            float ix = iconStartX;

            for (int i = 0; i < leftCount; i++)
            {
                DrawActionIconButton(dl, scale, time, new Vector2(ix, iconY),
                    leftIconGlyphs[i], leftIconKeys[i], leftIconHovers[i], leftIconTooltips[i]);
                ix += iconSize + iconGap;
            }

            // Divider
            ix += dividerMargin - iconGap;
            dl.AddLine(new Vector2(ix, iconY + 5f * scale),
                       new Vector2(ix, iconY + iconSize - 5f * scale),
                       Boutique.U32(Boutique.BorderSoft), 1f * scale);
            ix += dividerW + dividerMargin;

            for (int i = 0; i < rightCount; i++)
            {
                DrawActionIconButton(dl, scale, time, new Vector2(ix, iconY),
                    rightIconGlyphs[i], rightIconKeys[i], rightIconHovers[i], rightIconTooltips[i]);
                ix += iconSize + iconGap;
            }

            // ── Search pill (left of icon bar), slightly compact ──
            float searchW = 220f * scale;
            float searchH = 28f * scale;
            float searchY = midY - searchH * 0.5f;
            float searchX = iconStartX - 12f * scale - searchW;
            var searchMin = new Vector2(searchX, searchY);
            var searchMax = new Vector2(searchX + searchW, searchY + searchH);

            Boutique.DrawSearchPillBackground(dl, searchMin, searchMax, scale, searchFieldHasFocus);

            // Magnifier glyph
            ImGui.PushFont(UiBuilder.IconFont);
            var magSize = ImGui.CalcTextSize("");
            ImGui.PopFont();
            var magPos = new Vector2(searchMin.X + 12f * scale, midY - magSize.Y * 0.5f);
            dl.AddText(UiBuilder.IconFont, UiBuilder.IconFont.FontSize, magPos,
                Boutique.U32(Boutique.TextFaint), "");

            // Native ImGui input on top. Push FrameBorderSize=0 + transparent
            // Border colour so the input doesn't overdraw my pill border.
            float padTextY = MathF.Max(0f, (searchH - ImGui.GetTextLineHeight()) * 0.5f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0f, padTextY));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
            ImGui.SetCursorScreenPos(new Vector2(searchMin.X + 12f * scale + magSize.X + 8f * scale,
                                                 searchMin.Y));
            ImGui.PushItemWidth(searchW - 12f * scale - magSize.X - 16f * scale);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.Border, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.BorderShadow, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.Text, Boutique.Text);
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, Boutique.TextFaint);
            if (ImGui.InputTextWithHint("##boutique_search", "Search characters...", ref searchInput, 128))
            {
                characterGrid.SearchQuery = searchInput;
            }
            searchFieldHasFocus = ImGui.IsItemActive();
            ImGui.PopStyleColor(7);
            ImGui.PopItemWidth();
            ImGui.PopStyleVar(2);
        }

        // 4-point sparkle star, drawn as 4 thin triangles meeting at the
        // centre. Outer tip distance = size (matches the circle radius the
        // particles used to draw at). ImGui's AddConvexPolyFilled can't do
        // concave shapes, so triangles are the cheapest path.
        private static void DrawSparkleStar(ImDrawListPtr dl, Vector2 c, float size, uint col)
        {
            float r = size;
            float w = size * 0.28f;
            // Vertical ray (north + south)
            dl.AddTriangleFilled(
                c + new Vector2(-w, 0), c + new Vector2(0, -r), c + new Vector2(w, 0), col);
            dl.AddTriangleFilled(
                c + new Vector2(-w, 0), c + new Vector2(0,  r), c + new Vector2(w, 0), col);
            // Horizontal ray (east + west)
            dl.AddTriangleFilled(
                c + new Vector2(0, -w), c + new Vector2(-r, 0), c + new Vector2(0, w), col);
            dl.AddTriangleFilled(
                c + new Vector2(0, -w), c + new Vector2( r, 0), c + new Vector2(0, w), col);
        }

        private void DrawActionIconButton(ImDrawListPtr dl, float scale, double time, Vector2 min,
            string glyph, string key, Vector4 hoverInk, string tooltip)
        {
            float side = 26f * scale;
            ImGui.SetCursorScreenPos(min);
            bool clicked = ImGui.InvisibleButton($"##icbtn_{key}", new Vector2(side, side));
            // Use rect check for hover so the tooltip is bulletproof against
            // any later-submitted item shadowing the InvisibleButton's hover
            // state. IsItemHovered() was apparently not firing for these
            // icons even though the source had the new tooltip strings.
            bool hovered = ImGui.IsMouseHoveringRect(min, min + new Vector2(side, side));
            if (hovered && !string.IsNullOrEmpty(tooltip)) CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(tooltip);

            Boutique.DrawIconButtonSized(dl, min, side, scale, UiBuilder.IconFont,
                UiBuilder.IconFont.FontSize, glyph, hovered, hoverInk);

            // "New feature" dot for Features Guide button
            if (key == "features")
            {
                bool hasUnseen = !plugin.Configuration.SeenFeatures.Contains(FeatureKeys.FeaturesGuide);
                if (hasUnseen)
                    Boutique.DrawNewDot(dl, min + new Vector2(side, side), scale, time);
            }

            // Trophy with unseen unlocks: override the rest-state icon ink
            // from grey to gold so the button itself signals "something
            // waiting" even before the particles read. Hover state is already
            // gold via hoverInk, so only repaint when not hovered.
            if (key == "trophy" && plugin.Configuration.EnableAchievementSystem)
            {
                bool hasUnseen = plugin.Configuration.AchievementData?.HasUnseenAchievements == true;
                if (hasUnseen && !hovered)
                {
                    ImGui.PushFont(UiBuilder.IconFont);
                    var iconSize = ImGui.CalcTextSize(glyph);
                    ImGui.PopFont();
                    var iconPos = new Vector2(
                        min.X + (side - iconSize.X) * 0.5f,
                        min.Y + (side - iconSize.Y) * 0.5f);
                    dl.AddText(UiBuilder.IconFont, UiBuilder.IconFont.FontSize, iconPos,
                        Boutique.U32(Boutique.Gold), glyph);
                }
                if (hasUnseen)
                {
                    var btnCentre = min + new Vector2(side, side) * 0.5f;
                    float dt = ImGui.GetIO().DeltaTime;
                    var rng = Random.Shared;

                    trophySpawnTimer += dt;
                    while (trophySpawnTimer > 0.04f) // ~25 spawns/sec
                    {
                        trophySpawnTimer -= 0.04f;
                        if (trophyParticles.Count > 60) break;

                        float angle = (float)(rng.NextDouble() * Math.PI * 2);
                        float speed = 18f + (float)(rng.NextDouble() * 35f);
                        float life = 0.6f + (float)(rng.NextDouble() * 0.5f);
                        var col = new Vector4(
                            0.95f + (float)(rng.NextDouble() * 0.05f),
                            0.65f + (float)(rng.NextDouble() * 0.25f),
                            0.05f + (float)(rng.NextDouble() * 0.15f),
                            1f);
                        float spawnR = (6f + (float)(rng.NextDouble() * 4f)) * scale;
                        trophyParticles.Add(new Particle
                        {
                            Position = btnCentre + new Vector2(
                                (float)Math.Cos(angle) * spawnR,
                                (float)Math.Sin(angle) * spawnR),
                            Velocity = new Vector2(
                                (float)Math.Cos(angle) * speed * scale,
                                (float)Math.Sin(angle) * speed * scale),
                            Color = col,
                            Life = life,
                            MaxLife = life,
                            Size = (1.2f + (float)(rng.NextDouble() * 1.5f)) * scale
                        });
                    }

                    for (int pi = trophyParticles.Count - 1; pi >= 0; pi--)
                    {
                        trophyParticles[pi].Update(dt);
                        if (!trophyParticles[pi].IsAlive)
                            trophyParticles.RemoveAt(pi);
                    }

                    foreach (var p in trophyParticles)
                    {
                        uint col32 = ImGui.GetColorU32(p.Color);
                        if (p.Color.W > 0.4f)
                        {
                            // Soft circular halo behind the sparkle - kept as a
                            // circle (not a star) so the glow stays diffuse.
                            var glow = new Vector4(p.Color.X, p.Color.Y, p.Color.Z, p.Color.W * 0.25f);
                            dl.AddCircleFilled(p.Position, p.Size * 2f, ImGui.GetColorU32(glow), 8);
                        }
                        DrawSparkleStar(dl, p.Position, p.Size, col32);
                    }
                }
                else if (trophyParticles.Count > 0)
                {
                    // Fade out remaining particles after the user clears the badge
                    float dt = ImGui.GetIO().DeltaTime;
                    for (int pi = trophyParticles.Count - 1; pi >= 0; pi--)
                    {
                        trophyParticles[pi].Update(dt);
                        if (!trophyParticles[pi].IsAlive)
                            trophyParticles.RemoveAt(pi);
                    }
                    foreach (var p in trophyParticles)
                        DrawSparkleStar(dl, p.Position, p.Size, ImGui.GetColorU32(p.Color));
                    trophySpawnTimer = 0f;
                }
            }

            float sheen = uiStyles.UpdateAndGetHoverSweepProgress($"icbtn_{key}", hovered);
            if (sheen >= 0f)
                Windows.Styles.UIStyles.DrawHoverSheen(dl, min, min + new Vector2(side, side), sheen, maxAlpha: 0.18f);

            if (clicked)
            {
                switch (key)
                {
                    case "random":
                        var effectiveTheme = SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration)
                            ? SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration)
                            : SeasonalTheme.Default;
                        if (effectiveTheme == SeasonalTheme.Winter || effectiveTheme == SeasonalTheme.Christmas)
                            giftBoxShakeTimer = GIFT_BOX_SHAKE_DURATION;
                        Vector2 effectPos = min + new Vector2(side, side) * 0.5f;
                        diceEffect.Trigger(effectPos, true, plugin.Configuration);
                        plugin.SelectRandomCharacterAndDesign();
                        break;
                    case "qswitch":
                        plugin.QuickSwitchWindow.IsOpen = !plugin.QuickSwitchWindow.IsOpen;
                        plugin.QuickSwitchButtonPos = min;
                        plugin.QuickSwitchButtonSize = new Vector2(side, side);
                        break;
                    case "features":
                        if (plugin.FeaturesWindow != null)
                        {
                            bool wasOpen = plugin.FeaturesWindow.IsOpen;
                            plugin.FeaturesWindow.IsOpen = !wasOpen;
                            plugin.Configuration.SeenFeatures.Add(FeatureKeys.FeaturesGuide);
                            plugin.Configuration.Save();
                            if (!wasOpen) plugin.AchievementTracker?.OnFeaturesGuideOpened();
                        }
                        break;
                    case "trophy":
                        if (plugin.AchievementWindow != null)
                            plugin.AchievementWindow.IsOpen = !plugin.AchievementWindow.IsOpen;
                        break;
                    case "gallery":
                        // Gallery is under construction, button intentionally inert
                        plugin.GalleryButtonPos = min;
                        plugin.GalleryButtonSize = new Vector2(side, side);
                        break;
                    case "settings":
                        plugin.IsSettingsOpen = !plugin.IsSettingsOpen;
                        plugin.SettingsButtonPos = min;
                        plugin.SettingsButtonSize = new Vector2(side, side);
                        break;
                    case "revert":
                        var io = ImGui.GetIO();
                        if (io.KeyCtrl && io.KeyShift)
                            plugin.RevertAllChanges();
                        break;
                    case "discord":
                        Dalamud.Utility.Util.OpenLink("https://discord.gg/8JykGErcX4");
                        break;
                }
            }
        }

        // ── Sort subbar (38px): sort tabs + roster count + Tags filter + Reorder ──
        private void DrawSortSubbar(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale, double time)
        {
            // Background: surface-0 + bottom border-soft hairline
            dl.AddRectFilled(min, max, Boutique.U32(Boutique.Surface0));
            dl.AddLine(new Vector2(min.X, max.Y - 1f * scale),
                       new Vector2(max.X, max.Y - 1f * scale),
                       Boutique.U32(Boutique.BorderSoft), 1f * scale);

            float padX = 16f * scale;
            float trackPx = 2.4f * scale;

            // Sort tabs (left)
            (string label, Plugin.SortType type)[] tabs =
            {
                ("MANUAL",     Plugin.SortType.Manual),
                ("FAVOURITES", Plugin.SortType.Favorites),
                ("A-Z",        Plugin.SortType.Alphabetical),
                ("RECENT",     Plugin.SortType.Recent),
                ("OLDEST",     Plugin.SortType.Oldest),
            };

            float tabY = min.Y + (max.Y - min.Y - 24f * scale) * 0.5f;
            float tx = min.X + padX;

            using (Plugin.Instance?.OswaldMed11?.Push())
            {
                for (int i = 0; i < tabs.Length; i++)
                {
                    var t = tabs[i];
                    bool isActive = characterGrid.CurrentSort == t.type;
                    var tabMin = new Vector2(tx, tabY);
                    float tabW = Boutique.MeasureTrackedText(t.label, trackPx) + 24f * scale;
                    float tabH = ImGui.GetFontSize() + 12f * scale;
                    var tabMax = tabMin + new Vector2(tabW, tabH);
                    ImGui.SetCursorScreenPos(tabMin);
                    bool clicked = ImGui.InvisibleButton($"##sorttab_{i}", new Vector2(tabW, tabH));
                    bool hovered = ImGui.IsItemHovered();
                    Boutique.DrawSortTab(dl, tabMin, t.label, trackPx, scale, isActive, hovered);
                    if (clicked)
                    {
                        plugin.Configuration.CurrentSortIndex = (int)t.type;
                        plugin.Configuration.Save();
                        characterGrid.SetSortType(t.type);
                        characterGrid.SortCharacters();
                    }
                    tx += tabW;
                    if (i < tabs.Length - 1)
                    {
                        // Separator hairline
                        dl.AddLine(new Vector2(tx, tabY + 5f * scale),
                                   new Vector2(tx, tabY + tabH - 5f * scale),
                                   Boutique.U32(Boutique.BorderSoft), 1f * scale);
                        tx += 4f * scale;
                    }
                }
            }

            // Right: Tags filter pill + Reorder icon
            float rightX = max.X - padX;

            // Reorder icon (28×28)
            float iconSize = 28f * scale;
            float iconY = min.Y + (max.Y - min.Y - iconSize) * 0.5f;
            float reorderX = rightX - iconSize;
            var reorderMin = new Vector2(reorderX, iconY);
            ImGui.SetCursorScreenPos(reorderMin);
            bool reorderClicked = ImGui.InvisibleButton("##reorder_btn", new Vector2(iconSize, iconSize));
            bool reorderHovered = ImGui.IsItemHovered();
            if (reorderHovered) CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Reorder characters - manage display order in a list view");
            DrawSquareIcon28(dl, reorderMin, scale, "\uf0b2", reorderHovered, Boutique.NpAmber);
            if (reorderClicked) reorderWindow.Open();

            // Tags pill
            string tagLbl = "TAGS";
            string tagVal = string.IsNullOrEmpty(characterGrid.SelectedTag) || characterGrid.SelectedTag == "All"
                ? "ALL"
                : characterGrid.SelectedTag.ToUpperInvariant();
            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                float tagTrack = ImGui.GetFontSize() * 0.18f;
                var pillSize = Boutique.MeasureFilterPill(tagLbl, tagVal, tagTrack, scale);
                float pillX = reorderX - 6f * scale - pillSize.X;
                float pillY = min.Y + (max.Y - min.Y - pillSize.Y) * 0.5f;
                var pillMin = new Vector2(pillX, pillY);
                ImGui.SetCursorScreenPos(pillMin);
                bool pillClicked = ImGui.InvisibleButton("##tags_pill", pillSize);
                bool pillHovered = ImGui.IsItemHovered();
                Boutique.DrawFilterPill(dl, pillMin, tagLbl, tagVal, tagTrack, scale, pillHovered);
                if (pillClicked)
                {
                    tagDropdownPopupOpen = true;
                    ImGui.OpenPopup("##tags_popup");
                }

                // Tag dropdown popup
                if (tagDropdownPopupOpen)
                {
                    ImGui.SetNextWindowPos(new Vector2(pillMin.X, pillMin.Y + pillSize.Y + 2f * scale));
                    ImGui.PushStyleColor(ImGuiCol.PopupBg,        Boutique.Surface1);
                    ImGui.PushStyleColor(ImGuiCol.Border,         Boutique.WithAlpha(Boutique.Gold, 0.45f));
                    ImGui.PushStyleColor(ImGuiCol.Text,           Boutique.Text);
                    ImGui.PushStyleColor(ImGuiCol.HeaderHovered,  Boutique.WithAlpha(Boutique.Gold, 0.18f));
                    ImGui.PushStyleColor(ImGuiCol.HeaderActive,   Boutique.WithAlpha(Boutique.Gold, 0.30f));
                    ImGui.PushStyleColor(ImGuiCol.Header,         Boutique.WithAlpha(Boutique.Gold, 0.10f));
                    // Bigger popup. Was 8/8 padding, 8/4 frame, 160 item width.
                    // Items rendered at OswaldMed9 (11.7px) which the user
                    // called "extremely tiny". Bumped to readable proportions.
                    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f * scale, 10f * scale));
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,  new Vector2(12f * scale, 8f * scale));
                    ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,   new Vector2(4f * scale, 4f * scale));
                    ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f * scale);
                    if (ImGui.BeginPopup("##tags_popup"))
                    {
                        var tags = characterGrid.GetAvailableTags();
                        using (Plugin.Instance?.OswaldSemi13?.Push())
                        {
                            foreach (var tag in tags)
                            {
                                bool sel = tag == characterGrid.SelectedTag;
                                string display = tag.ToUpperInvariant();
                                if (ImGui.Selectable(display, sel, ImGuiSelectableFlags.None,
                                    new Vector2(220f * scale, 0)))
                                {
                                    characterGrid.SelectedTag = tag;
                                    tagDropdownPopupOpen = false;
                                }
                                if (sel) ImGui.SetItemDefaultFocus();
                            }
                        }
                        ImGui.EndPopup();
                    }
                    else
                    {
                        tagDropdownPopupOpen = false;
                    }
                    ImGui.PopStyleVar(4);
                    ImGui.PopStyleColor(6);
                }
            }
        }

        private void DrawSquareIcon28(ImDrawListPtr dl, Vector2 min, float scale,
            string glyph, bool hovered, Vector4 hoverInk)
        {
            float side = 28f * scale;
            var max = min + new Vector2(side, side);
            var bgCol = hovered
                ? Boutique.U32(Boutique.Surface1)
                : Boutique.U32(Boutique.PillBg);
            dl.AddRectFilled(min, max, bgCol);
            var borderCol = hovered
                ? Boutique.U32(Boutique.WithAlpha(hoverInk, 0.85f))
                : Boutique.U32(Boutique.BorderSoft);
            dl.AddRect(min, max, borderCol, 0f, ImDrawFlags.None, 1f * scale);

            ImGui.PushFont(UiBuilder.IconFont);
            var ink = hovered ? hoverInk : Boutique.TextDim;
            var iconSize = ImGui.CalcTextSize(glyph);
            ImGui.PopFont();
            float fSz = UiBuilder.IconFont.FontSize;
            var iconPos = new Vector2(min.X + (side - iconSize.X * 0.78f) * 0.5f,
                                      min.Y + (side - iconSize.Y * 0.78f) * 0.5f);
            dl.AddText(UiBuilder.IconFont, fSz, iconPos, Boutique.U32(ink), glyph);
        }

        // ── Content area: grid + dp-edge + (optional) dp-panel ──
        private void DrawContentArea(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale,
            float deltaTime, double time)
        {
            // Tick the design panel's open/close animation every frame so
            // its width interpolates smoothly even before/after rendering.
            // Without this the animation deadlocks: panelW=0 → render gate
            // fails → DrawIntoRect not called → animation never advances.
            designPanel.TickAnimation();

            // Reserve dp-edge (28px) + optional dp-panel.
            // Use the panel's ANIMATED width so the grid reflows smoothly
            // as the panel slides in/out (was: snap to full width on Open).
            float edgeW = 28f * scale;
            float panelW = designPanel.GetAnimatedPanelWidth() * scale;

            float gridRight = max.X - edgeW - panelW;
            var gridMin = min;
            var gridMax = new Vector2(gridRight, max.Y);
            var edgeMin = new Vector2(gridRight, min.Y);
            var edgeMax = new Vector2(gridRight + edgeW, max.Y);
            var panelMin = new Vector2(edgeMax.X, min.Y);
            var panelMax = new Vector2(panelMin.X + panelW, max.Y);

            // Grid child window. Make it transparent so ambient effects drawn
            // INSIDE remain visible (default ChildBg covers our parent draw).
            ImGui.SetCursorScreenPos(gridMin);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20f * scale, 14f * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6f * scale, 6f * scale));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
            ImGui.BeginChild("##boutique_grid", new Vector2(gridMax.X - gridMin.X, gridMax.Y - gridMin.Y),
                false, ImGuiWindowFlags.None);

            // Ambient (background layer), radial spots + hum lines + breathe wash + dust motes.
            // ALL drawn to the CHILD's draw list BEFORE cards so they sit purely behind.
            // SKIPPED when the form is open: the form has no opaque chassis, so the
            // hum lines + dust motes show THROUGH it as visual noise/horizontal bands.
            var childDl = ImGui.GetWindowDrawList();
            var childMin = ImGui.GetCursorScreenPos() - new Vector2(20f * scale, 14f * scale);
            var childMax = childMin + new Vector2(gridMax.X - gridMin.X, gridMax.Y - gridMin.Y);
            bool ambientFormOpen = plugin.IsAddCharacterWindowOpen || characterForm.IsEditWindowOpen;
            if (!ambientFormOpen)
            {
                childDl.PushClipRect(childMin, childMax, true);
                Boutique.DrawAmbientSpots(childDl, childMin, childMax, time, scale);
                Boutique.DrawHumLines(childDl, childMin, childMax, time, scale);
                Boutique.DrawWindowBreathe(childDl, childMin, childMax, time);
                Boutique.DrawDustMotes(childDl, childMin, childMax, time, scale);
                Boutique.DrawCenterAuroraUnderHero(childDl, childMin, childMax, time, scale);
                childDl.PopClipRect();
            }

            // ── Backgrounds (custom image + seasonal layers) ──
            // ALL drawn BEFORE the hero prompt so "CHOOSE YOUR CHARACTER" and
            // its flanking gold wing lines stay legible on top of every
            // background variant.
            if (plugin.Configuration.SelectedTheme == ThemeSelection.Custom)
                DrawCustomBackgroundInChild();

            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration))
            {
                var effectiveTheme = SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration);
                if (effectiveTheme == SeasonalTheme.Valentines)
                {
                    DrawValentinesBackgroundInChild();
                }
                else if (effectiveTheme == SeasonalTheme.Winter || effectiveTheme == SeasonalTheme.Christmas)
                {
                    winterBackgroundSnowUI.ConfigureSnowEffect(alpha: 0.5f, size: 0.7f, spawnRate: 0.8f);
                    var childWindowPos = ImGui.GetCursorScreenPos();
                    var childWindowSize = ImGui.GetContentRegionAvail();
                    winterBackgroundSnowUI.SetEffectAreaAbsolute(childWindowPos, childWindowSize);
                    winterBackgroundSnowUI.Update(deltaTime);
                    winterBackgroundSnowUI.DrawAbsolute();
                }
            }

            // Hero prompt: "CHOOSE YOUR CHARACTER", display font, breathing room top + bottom.
            // Skipped entirely when the character form is open, otherwise its Dummy
            // advance pushes the form down by ~50px and steals available height.
            bool formIsOpen = plugin.IsAddCharacterWindowOpen || characterForm.IsEditWindowOpen;
            float gridChildW = ImGui.GetContentRegionAvail().X;
            if (!formIsOpen)
            {
                using (Plugin.Instance?.OswaldSemiMidSmall?.Push())
                {
                    // Top breathing space so text + flair lines aren't crammed against
                    // the sort-subbar above. Centre the rule line through the text middle.
                    float topPad = 18f * scale;
                    float yCentre = ImGui.GetCursorScreenPos().Y + topPad + ImGui.GetFontSize() * 0.5f;
                    var centre = new Vector2(ImGui.GetCursorScreenPos().X + gridChildW * 0.5f, yCentre);
                    Boutique.DrawHeroPrompt(childDl, centre, gridChildW, 7f * scale, scale, "CHOOSE YOUR CHARACTER");
                    ImGui.Dummy(new Vector2(0, topPad + ImGui.GetFontSize() + 16f * scale));
                }
            }

            // Form takes over the entire content area when open; grid is hidden behind it.
            if (plugin.IsAddCharacterWindowOpen || characterForm.IsEditWindowOpen)
            {
                characterForm.Draw();
            }
            else
            {
                characterGrid.Draw();
            }

            // Depth shadows pinned to the child WINDOW (viewport) rather than
            // the child's content cursor, using gridMin/gridMax (absolute
            // screen coords from the outer scope, scroll-invariant) instead
            // of childMin/childMax (which are GetCursorScreenPos-derived and
            // move with the child's scroll position).
            if (!plugin.IsAddCharacterWindowOpen && !characterForm.IsEditWindowOpen)
            {
                var depthDl = ImGui.GetWindowDrawList();
                float depthShadowH = 10f * scale;
                uint depthSolid = Boutique.U32(new Vector4(0f, 0f, 0f, 0.30f));
                uint depthClear = Boutique.U32(new Vector4(0f, 0f, 0f, 0f));
                depthDl.AddRectFilledMultiColor(
                    new Vector2(gridMin.X, gridMin.Y),
                    new Vector2(gridMax.X, gridMin.Y + depthShadowH),
                    depthSolid, depthSolid, depthClear, depthClear);
                depthDl.AddRectFilledMultiColor(
                    new Vector2(gridMin.X, gridMax.Y - depthShadowH),
                    new Vector2(gridMax.X, gridMax.Y),
                    depthClear, depthClear, depthSolid, depthSolid);
            }

            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);

            // ── DP edge (always visible, 28px wide, click to toggle expanded) ──
            ImGui.SetCursorScreenPos(edgeMin);
            bool edgeClicked = ImGui.InvisibleButton("##dpedge_btn", new Vector2(edgeW, edgeMax.Y - edgeMin.Y));
            bool edgeHovered = ImGui.IsItemHovered();
            if (edgeHovered) CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(designPanel.IsOpen ? "Hide the design panel" : "Show designs for the active character");

            int dpCount = 0;
            var activeChar = plugin.GetActiveCharacter() ?? plugin.activeCharacter;
            if (activeChar != null) dpCount = activeChar.Designs?.Count ?? 0;
            string dpCountText = dpCount > 0 ? dpCount.ToString("00") : "";

            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                // chevron-right () when open (click to collapse to right),
                // chevron-left () when closed (click to expand to left).
                string chevGlyph = designPanel.IsOpen ? "\uf054" : "\uf053";
                Boutique.DrawDpEdge(dl, edgeMin, edgeMax, scale,
                    UiBuilder.IconFont, UiBuilder.IconFont.FontSize,
                    chevGlyph, dpCountText, edgeHovered);
            }

            if (edgeClicked)
            {
                if (designPanel.IsOpen)
                    designPanel.Close();
                else
                {
                    var ac = plugin.GetActiveCharacter() ?? plugin.activeCharacter;
                    int idx = ac != null ? plugin.Characters.IndexOf(ac) : 0;
                    if (idx < 0) idx = 0;
                    if (plugin.Characters.Count > 0)
                        designPanel.Open(idx);
                }
            }

            // DP panel chrome on the main draw list, panel opens its own scroll child
            if (designPanel.IsVisible && panelW > 0f)
            {
                designPanel.DrawIntoRect(panelMin, panelMax, scale);
            }
        }

        // ── Footer (40px): links | pagination | count ──
        private void DrawFooter(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
        {
            // Background: surface-0 + top border-soft hairline
            dl.AddRectFilled(min, max, Boutique.U32(Boutique.Surface0));
            dl.AddLine(new Vector2(min.X, min.Y),
                       new Vector2(max.X, min.Y),
                       Boutique.U32(Boutique.BorderSoft), 1f * scale);

            float padX = 16f * scale;
            float midY = (min.Y + max.Y) * 0.5f;
            float trackPx = 2f * scale;
            double time = ImGui.GetTime();

            // Left zone: Tutorial / Patch Notes / Support Dev links
            using (Plugin.Instance?.OswaldMed11?.Push())
            {
                float lx = min.X + padX;
                lx += DrawFooterLinkButton(dl, scale, time, new Vector2(lx, midY),
                    "TUTORIAL", "tutorial", "\uf19d", false,
                    () => plugin.TutorialManager.StartTutorial(), trackPx);
                lx += 16f * scale;
                lx += DrawFooterLinkButton(dl, scale, time, new Vector2(lx, midY),
                    "PATCH NOTES", "patchnotes", "\uf70e", false,
                    () => {
                        plugin.PatchNotesWindow.OpenMainMenuOnClose = false;
                        plugin.PatchNotesWindow.IsOpen = !plugin.PatchNotesWindow.IsOpen;
                    }, trackPx);
                lx += 16f * scale;
                lx += DrawFooterLinkButton(dl, scale, time, new Vector2(lx, midY),
                    "SUPPORT DEV", "supportdev", "\uf004", true,
                    () => Dalamud.Utility.Util.OpenLink("https://ko-fi.com/icarusxiv"), trackPx);
            }

            // Right zone: count (Oswald Med 11, slightly larger so it reads at a glance)
            using (Plugin.Instance?.OswaldMed11?.Push())
            {
                int total = characterGrid.FilteredCount;
                int start = total == 0 ? 0 : characterGrid.VisibleStartIndex;
                int end = characterGrid.VisibleEndIndex;
                string countText = total == 0
                    ? "NO CHARACTERS"
                    : $"SHOWING {start}-{end} OF {total}";
                float countW = Boutique.MeasureTrackedText(countText, 1.8f * scale);
                Boutique.DrawTrackedText(dl,
                    new Vector2(max.X - padX - countW, midY - ImGui.GetFontSize() * 0.5f),
                    countText, Boutique.U32(Boutique.TextDim), 1.8f * scale);
            }

            // Centre zone: pagination, direct port of WardrobeWindow.DrawPagerRow
            int totalPages = characterGrid.TotalPageCount;
            int curPage = characterGrid.CurrentPage;
            int prevPage = characterGrid.PagePrevIdx;
            float pageT = characterGrid.PageTransitionT;
            bool isTrans = characterGrid.IsPageTransitioning;
            int fromIdx = isTrans ? prevPage : curPage;
            // Page row accent follows custom.pageButtonActive so the editor's
            // "Active Page Button" entry drives the active dot, halo, and
            // arrow tint instead of leaking off the global Accent token.
            Vector4 pageActive = Boutique.SlotOrDefault("custom.pageButtonActive",
                new Vector4(1f, 214f / 255f, 0f, 1f));
            Vector4 pageActiveWarm = Boutique.Lerp(pageActive,
                new Vector4(1f, 1f, 1f, pageActive.W), 0.20f);
            Boutique.DrawWardrobePagerRow(dl, min, max, midY, totalPages,
                curPage, fromIdx, pageT, isTrans, scale,
                pageActive, pageActiveWarm, Plugin.Instance?.OswaldMed13,
                idx => characterGrid.CurrentPage = idx);
        }

        private float DrawFooterLinkButton(ImDrawListPtr dl, float scale, double time, Vector2 midPos,
            string label, string key, string glyph, bool isHeart, Action onClick, float trackPx)
        {
            var size = Boutique.MeasureFooterLink(label, trackPx, scale,
                UiBuilder.IconFont, UiBuilder.IconFont.FontSize, glyph);
            var btnMin = new Vector2(midPos.X, midPos.Y - size.Y * 0.5f);
            ImGui.SetCursorScreenPos(btnMin);
            bool clicked = ImGui.InvisibleButton($"##flink_{key}", size);
            bool hovered = ImGui.IsItemHovered();
            Boutique.DrawFooterLink(dl, btnMin, label, trackPx, scale,
                UiBuilder.IconFont, UiBuilder.IconFont.FontSize, glyph,
                hovered, isHeart, time);
            if (hovered)
            {
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(key switch
                {
                    "tutorial" => "Start the in-game CS+ tutorial",
                    "patchnotes" => "View the latest patch notes and features",
                    "supportdev" => "Support CS+ development on Ko-fi",
                    _ => label,
                });
            }
            if (clicked) onClick?.Invoke();
            return size.X;
        }

        public void UpdateSortType()
        {
            characterGrid.SetSortType((Plugin.SortType)plugin.Configuration.CurrentSortIndex);
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

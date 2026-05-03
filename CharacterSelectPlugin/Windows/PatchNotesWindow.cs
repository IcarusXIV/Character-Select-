using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;
using System;
using System.IO;
using System.Numerics;
using System.Collections.Generic;
using CharacterSelectPlugin.Windows.Styles;
using CharacterSelectPlugin.Effects;

namespace CharacterSelectPlugin.Windows
{
    public class PatchNotesWindow : Window
    {
        private readonly Plugin plugin;
        private bool hasScrolledToEnd = false;
        // Peak scroll fraction so the footer progress bar sticks at its high-water
        // mark (e.g. 100%) rather than dropping back down if the user scrolls up.
        private float peakScrollFraction = 0f;
        private bool hasAcknowledgedNSFW = false;
        private bool wasOpen = false;

        // Mark-as-Read button: previous-frame edge detection + transition timestamps
        private bool markBtnWasHovered = false;
        private float markBtnHoverStart = -1f;
        private bool markBtnWasActive = false;
        private float markBtnReleaseTime = -1f;
        public bool OpenMainMenuOnClose = false;

        // Continuous scroll fraction (0..1) updated from DrawPatchNotes, read by the Codex footer's progress bar.
        private float scrollFraction = 0f;

        // Firework spark (same as the character favourite-button effect) that fires when the
        // "Mark as Read" button is clicked. Close is deferred briefly so the spark is visible
        // before the window dismisses.
        private readonly FavoriteSparkEffect markAsReadSpark = new();
        private bool pendingMarkAsReadClose = false;
        private DateTime pendingMarkAsReadAt = DateTime.MinValue;

        private struct Particle
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float Life;
            public float MaxLife;
            public float Size;
            public Vector4 Color;
        }

        private List<Particle> particles = new List<Particle>();
        private float particleTimer = 0f;
        private Random particleRandom = new Random();

        // Feature spotlight (set false for minor updates)
        // Feature spotlight is a carousel of "big new features" cards at the top of the patch notes
        // window. We only turn it on for releases that have 2-3 showcase-worthy features with matching
        // image assets in Assets/. Disabled for 2.1.1.0 since this release is mostly rename migration,
        // privacy fixes, and QoL rather than big highlight items. Re-enable when the next major release
        // has visual features to show off.
        private static readonly bool ShowFeatureSpotlight = false;

        private struct FeatureCard
        {
            public FontAwesomeIcon Icon;
            public string Title;
            public string Description;
            public string ActionLabel;
            public Action OnClick;
            public string ImagePath;
        }

        public PatchNotesWindow(Plugin plugin) : base("Character Select+ - What's New?",
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            this.plugin = plugin;
            IsOpen = false;

            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(800, 650),
                MaximumSize = new Vector2(800, 650)
            };
        }

        private int _chromeColorCount = 0;
        public override void PreDraw()
        {
            _chromeColorCount = CharacterSelectPlugin.Windows.Styles.ThemeHelper.PushWindowChromeColors(plugin.Configuration);
        }
        public override void PostDraw()
        {
            CharacterSelectPlugin.Windows.Styles.ThemeHelper.PopWindowChromeColors(_chromeColorCount);
            _chromeColorCount = 0;
        }

        public override void Draw()
        {
            var totalScale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;

            if (IsOpen && !wasOpen)
            {
                hasScrolledToEnd = false;
                peakScrollFraction = 0f;
                hasAcknowledgedNSFW = false;
            }
            wasOpen = IsOpen;

            // Advance the firework simulation each frame, and fire the deferred close once the
            // spark has had ~0.55s to bloom (matches the spark's natural duration).
            float deltaTime = ImGui.GetIO().DeltaTime;
            markAsReadSpark.Update(deltaTime);
            if (pendingMarkAsReadClose && (DateTime.Now - pendingMarkAsReadAt).TotalSeconds > 0.55)
            {
                plugin.Configuration.LastSeenVersion = Plugin.CurrentPluginVersion;
                plugin.Configuration.Save();
                IsOpen = false;
                if (OpenMainMenuOnClose) plugin.ToggleMainUI();
                OpenMainMenuOnClose = false;
                pendingMarkAsReadClose = false;
            }

            ImGui.SetNextWindowSize(new Vector2(800 * totalScale, 650 * totalScale), ImGuiCond.Always);

            // Always use default theme for Patch Notes - consistent first impression
            int themeColorCount = ThemeHelper.PushDefaultThemeColors();
            int themeStyleVarCount = ThemeHelper.PushThemeStyleVars(plugin.Configuration.UIScaleMultiplier);

            // Encore chassis pattern: zero window padding + item spacing so the chrome
            // (ribbon, banner, footer) can bleed edge-to-edge flush with the window
            // border. The scroll child below re-pushes its own horizontal gutter
            // before BeginChild so cards stay inset.
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0f, 0f));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,   new Vector2(0f, 0f));

            try
            {
                DrawModernHeader(totalScale);
                DrawPatchNotes(totalScale);
                DrawBottomButton(totalScale);

                // Draw the firework spark last so it appears over the button + footer. Uses the
                // current window's draw list, which is the patch-notes window itself here.
                markAsReadSpark.Draw();

                // Window corner brackets (HTML .win-bracket.bl / .br): 14×14 gold L-shapes
                // at 5px inset from window BL/BR. 1px stroke at 38% alpha. Drawn LAST so
                // they sit on top of all content.
                {
                    var wPos = ImGui.GetWindowPos();
                    var wSize = ImGui.GetWindowSize();
                    var dl = ImGui.GetWindowDrawList();
                    float bsize = 14f * totalScale;
                    float binset = 5f * totalScale;
                    uint bcol = ImGui.GetColorU32(Boutique.WithAlpha(Boutique.Gold, 0.38f));
                    // Bottom-left: L opens up-right
                    var bl = new Vector2(wPos.X + binset, wPos.Y + wSize.Y - binset);
                    dl.AddLine(new Vector2(bl.X, bl.Y - bsize), bl, bcol, 1f);
                    dl.AddLine(bl, new Vector2(bl.X + bsize, bl.Y), bcol, 1f);
                    // Bottom-right: L opens up-left
                    var br = new Vector2(wPos.X + wSize.X - binset, wPos.Y + wSize.Y - binset);
                    dl.AddLine(new Vector2(br.X, br.Y - bsize), br, bcol, 1f);
                    dl.AddLine(br, new Vector2(br.X - bsize, br.Y), bcol, 1f);
                }
            }
            finally
            {
                ImGui.PopStyleVar(2); // WindowPadding + ItemSpacing (Encore pattern)
                ThemeHelper.PopThemeStyleVars(themeStyleVarCount);
                ThemeHelper.PopThemeColors(themeColorCount);
            }
        }

        // Codex header (matches 04-codex-revised.html mockup):
        //   Row 1 (28px): slim dark metadata ribbon ABOVE the banner with gold pip + date + BUILD tag.
        //   Row 2 (168px): clean unobstructed banner key-art.
        //   1px gold hairline at bottom of banner to separate from content.
        // Total header stack = 28 + 168 = 196px.
        private void DrawModernHeader(float totalScale)
        {
            // HTML: .ribbon and .header span the full inner window width edge-to-edge
            // (they're children of .window, no horizontal margin). ImGui's window
            // padding inset was making them narrower than the window, leaving the
            // window bg showing at the left/right edges - the "not fully coloured"
            // header the user reported.
            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            var windowPadding = ImGui.GetStyle().WindowPadding;

            float headerWidth = windowSize.X;
            float metaH = 30f * totalScale; // HTML .ribbon: height 30px

            // Pre-resolve the banner texture so we can compute bannerH from the image's actual
            // scaled aspect. Hard-coding bannerH=168 left ~36px of dead air below the image for
            // NewBanner.png (3732x616, scales to ~132px at window width). Content below was then
            // technically flush with the region but visually floated 36px under the banner image.
            IDalamudTextureWrap? bannerTex = null;
            float imageAspect = 800f / 132f;
            try
            {
                string pluginDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "";
                string imagePath = Path.Combine(pluginDirectory, "Assets", "NewBanner.png");
                if (File.Exists(imagePath))
                {
                    bannerTex = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
                    if (bannerTex != null)
                        imageAspect = (float)bannerTex.Width / bannerTex.Height;
                }
            }
            catch (Exception ex) { Plugin.Log.Error($"Failed to pre-load banner: {ex.Message}"); }

            float bannerH = headerWidth / imageAspect;
            float totalHeaderH = metaH + bannerH;

            // Draw header chrome at the window's top-left corner, full width - no padding inset.
            var stackStart = windowPos;
            var metaEnd = stackStart + new Vector2(headerWidth, metaH);
            var bannerStart = stackStart + new Vector2(0, metaH);
            var bannerEnd = bannerStart + new Vector2(headerWidth, bannerH);

            var drawList = ImGui.GetWindowDrawList();
            uint goldFull = ImGui.GetColorU32(new Vector4(1.0f, 0.84f, 0.0f, 1.0f));
            uint goldClear = ImGui.GetColorU32(new Vector4(1.0f, 0.84f, 0.0f, 0.0f));

            // ── Row 1: metadata ribbon (dark gradient bg, chassis tokens) ──
            // HTML .ribbon: linear-gradient(180deg, var(--ribbon-top) #0C0E12, var(--ribbon-bot) #080A0D)
            uint ribbonTop = ImGui.GetColorU32(Boutique.RibbonTop);
            uint ribbonBot = ImGui.GetColorU32(Boutique.RibbonBot);
            drawList.AddRectFilledMultiColor(stackStart, metaEnd, ribbonTop, ribbonTop, ribbonBot, ribbonBot);

            // Top hairline: HTML ::before at 50% alpha, solid at outer ends, fading to transparent at middle.
            // linear-gradient(90deg, gold 0%, transparent 42%, transparent 58%, gold 100%); opacity 0.50
            float ruleH = 1f * totalScale;
            uint goldRuleStrong = ImGui.GetColorU32(Boutique.WithAlpha(Boutique.Gold, 0.50f));
            drawList.AddRectFilledMultiColor(
                stackStart,
                stackStart + new Vector2(headerWidth * 0.45f, ruleH),
                goldRuleStrong, goldClear, goldClear, goldRuleStrong);
            drawList.AddRectFilledMultiColor(
                stackStart + new Vector2(headerWidth * 0.55f, 0),
                stackStart + new Vector2(headerWidth, ruleH),
                goldClear, goldRuleStrong, goldRuleStrong, goldClear);

            // Bottom hairline: HTML ::after at 26% alpha, opposite pattern - transparent at ends,
            // gold at middle (25-75%). linear-gradient(90deg, transparent, gold 25%, gold 75%, transparent)
            uint goldBotMid = ImGui.GetColorU32(Boutique.WithAlpha(Boutique.Gold, 0.26f));
            drawList.AddRectFilledMultiColor(
                new Vector2(stackStart.X, metaEnd.Y - ruleH),
                new Vector2(stackStart.X + headerWidth * 0.5f, metaEnd.Y),
                goldClear, goldBotMid, goldBotMid, goldClear);
            drawList.AddRectFilledMultiColor(
                new Vector2(stackStart.X + headerWidth * 0.5f, metaEnd.Y - ruleH),
                metaEnd,
                goldBotMid, goldClear, goldClear, goldBotMid);

            // LEFT side of ribbon: pulsing gold pip + PATCH NOTES · date
            float stripCenterY = stackStart.Y + metaH * 0.5f;
            float padX = 18f * totalScale;

            double t = ImGui.GetTime();
            float pulse = 0.55f + 0.45f * (float)Math.Sin(t * 2.2);
            var pipCenter = new Vector2(stackStart.X + padX + 3 * totalScale, stripCenterY);
            uint pipGlow = ImGui.GetColorU32(new Vector4(1.0f, 0.84f, 0.0f, 0.35f * pulse));
            uint pipCore = ImGui.GetColorU32(new Vector4(1.0f, 0.88f, 0.15f, 1.0f));
            drawList.AddCircleFilled(pipCenter, 6f * totalScale, pipGlow);
            drawList.AddCircleFilled(pipCenter, 3f * totalScale, pipCore);

            string metaText = $"PATCH NOTES    \u00B7    {DateTime.Today:d MMMM yyyy}";
            float metaFontH = ImGui.GetFontSize();
            var metaTextPos = new Vector2(pipCenter.X + 12f * totalScale, stripCenterY - metaFontH * 0.5f);
            uint metaColor = ImGui.GetColorU32(new Vector4(0.91f, 0.92f, 0.94f, 0.90f));
            drawList.AddText(metaTextPos, metaColor, metaText);

            // RIGHT side: BUILD <version> tag - small enough to sit comfortably inside the ribbon
            // without brushing against the top gold hairline.
            string buildText = $"BUILD {Plugin.CurrentPluginVersion}";
            var fontPtr = ImGui.GetFont();
            float buildFontScale = 0.78f;
            float buildFontSize = fontPtr.FontSize * buildFontScale;
            var nat = ImGui.CalcTextSize(buildText);
            var buildSize = new Vector2(nat.X * buildFontScale, nat.Y * buildFontScale);
            float tagPadX = 7f * totalScale;
            float tagPadY = 2f * totalScale;
            var tagMax = new Vector2(metaEnd.X - padX, stripCenterY + buildSize.Y * 0.5f + tagPadY);
            var tagMin = new Vector2(tagMax.X - buildSize.X - tagPadX * 2, stripCenterY - buildSize.Y * 0.5f - tagPadY);
            drawList.AddRectFilled(tagMin, tagMax, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.45f)));
            drawList.AddRect(tagMin, tagMax, goldFull, 0f, ImDrawFlags.None, 1f * totalScale);
            drawList.AddText(fontPtr, buildFontSize, new Vector2(tagMin.X + tagPadX, tagMin.Y + tagPadY), goldFull, buildText);

            // ── Row 2: clean banner (image sized to exactly fill bannerH - no dead air) ──
            if (bannerTex != null)
            {
                drawList.AddImage((ImTextureID)bannerTex.Handle, bannerStart, bannerEnd);
                DrawParticleEffects(drawList, bannerStart, new Vector2(headerWidth, bannerH));
            }
            else
            {
                DrawGradientBackground(bannerStart, bannerEnd);
                DrawParticleEffects(drawList, bannerStart, new Vector2(headerWidth, bannerH));
            }

            // Gold hairline at bottom of banner. Solid in the middle, fading out to the outer ends
            // (opposite direction to the ribbon hairline). Two halves: left fades IN, right fades OUT.
            float bannerRuleH = 1f * totalScale;
            drawList.AddRectFilledMultiColor(
                new Vector2(bannerStart.X, bannerEnd.Y - bannerRuleH),
                new Vector2(bannerStart.X + headerWidth * 0.5f, bannerEnd.Y),
                goldClear, goldFull, goldFull, goldClear);
            drawList.AddRectFilledMultiColor(
                new Vector2(bannerStart.X + headerWidth * 0.5f, bannerEnd.Y - bannerRuleH),
                bannerEnd,
                goldFull, goldClear, goldClear, goldFull);

            // Leave ~12px of breathing room between the banner's gold hairline
            // and the first content element (release tab). Lets the hairline
            // read as a clean divider instead of being sandwiched against the
            // top of the release tab.
            ImGui.SetCursorPosY(bannerEnd.Y - windowPos.Y + 12f * totalScale);
        }

        private void DrawGradientBackground(Vector2 headerStart, Vector2 headerEnd)
        {
            var drawList = ImGui.GetWindowDrawList();
            uint gradientTop = ImGui.GetColorU32(new Vector4(0.2f, 0.4f, 0.8f, 0.15f));
            uint gradientBottom = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.2f, 0.05f));
            drawList.AddRectFilledMultiColor(headerStart, headerEnd, gradientTop, gradientTop, gradientBottom, gradientBottom);

            ImGui.SetCursorPos(new Vector2(20, 15));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.95f, 0.95f, 1.0f));
            ImGui.Text("Character Select+ - What's New?");
            ImGui.PopStyleColor();

            ImGui.SetCursorPos(new Vector2(20, 35));
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1.0f), "\uf005");
            ImGui.PopFont();
            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.9f, 0.4f, 1.0f));
            ImGui.Text($"New in v{Plugin.CurrentPluginVersion}");
            ImGui.PopStyleColor();

            ImGui.SetCursorPos(new Vector2(20, 55));
            ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.85f, 1.0f), "Achievements, Wardrobe, QoL & Optimizations");
        }

        private void DrawFeatureSpotlight(float totalScale)
        {
            string headerText = "══════════════ FEATURE SPOTLIGHT ══════════════";
            float headerWidth = ImGui.CalcTextSize(headerText).X;
            float windowWidth = ImGui.GetContentRegionAvail().X;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (windowWidth - headerWidth) * 0.5f);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.85f, 0.6f, 1.0f));
            ImGui.Text(headerText);
            ImGui.PopStyleColor();
            ImGui.Spacing();

            float cardWidth = (ImGui.GetContentRegionAvail().X - 20) / 3;
            float cardHeight = 200 * totalScale;

            var features = new FeatureCard[]
            {
                new FeatureCard
                {
                    Icon = FontAwesomeIcon.User,
                    Title = "Name Sync",
                    Description = "Show your CS+ name in chat, nameplates, and party list",
                    ActionLabel = "Open Settings",
                    OnClick = () => plugin.OpenSettingsToSection("Name Sync"),
                    ImagePath = "NameSync.png"
                },
                new FeatureCard
                {
                    Icon = FontAwesomeIcon.IdCard,
                    Title = "Expanded RP Profiles",
                    Description = "Organize your profile with custom sections and galleries",
                    ActionLabel = "View Profile",
                    OnClick = () => plugin.OpenRPProfileForFeatureSpotlight(),
                    ImagePath = "ERP.png"
                },
                new FeatureCard
                {
                    Icon = FontAwesomeIcon.Palette,
                    Title = "Custom Themes",
                    Description = "Personalize CS+ with colours, images, and icons",
                    ActionLabel = "Open Settings",
                    OnClick = () => plugin.OpenSettingsToSection("Visual Settings"),
                    ImagePath = "MainWindow.png"
                }
            };

            float totalCardsWidth = (cardWidth * 3) + 10;
            float startX = (windowWidth - totalCardsWidth) * 0.5f - 1;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + startX);

            for (int i = 0; i < features.Length; i++)
            {
                DrawFeatureCard(features[i], cardWidth, cardHeight, totalScale);
                if (i < features.Length - 1)
                    ImGui.SameLine(0, 5);
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

        private void DrawFeatureCard(FeatureCard card, float width, float height, float totalScale)
        {
            var startPos = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();

            float padding = 8 * totalScale;
            float imageHeight = 100 * totalScale;
            float buttonHeight = 24 * totalScale;

            drawList.AddRectFilled(
                startPos,
                startPos + new Vector2(width, height),
                ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.15f, 0.9f)),
                8f
            );

            drawList.AddRect(
                startPos,
                startPos + new Vector2(width, height),
                ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.4f, 0.6f)),
                8f
            );

            var imageBoxPos = startPos + new Vector2(padding, padding);
            var imageBoxSize = new Vector2(width - (padding * 2), imageHeight);

            drawList.AddRectFilled(
                imageBoxPos,
                imageBoxPos + imageBoxSize,
                ImGui.GetColorU32(new Vector4(0.08f, 0.08f, 0.1f, 1.0f)),
                6f
            );

            bool imageLoaded = false;
            if (!string.IsNullOrEmpty(card.ImagePath))
            {
                try
                {
                    string pluginDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "";
                    string assetsPath = Path.Combine(pluginDirectory, "Assets");
                    string imagePath = Path.Combine(assetsPath, card.ImagePath);

                    if (File.Exists(imagePath))
                    {
                        var texture = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
                        if (texture != null)
                        {
                            float imageAspect = (float)texture.Width / texture.Height;
                            float boxAspect = imageBoxSize.X / imageBoxSize.Y;

                            Vector2 imageSize;
                            Vector2 imageOffset = Vector2.Zero;

                            if (imageAspect > boxAspect)
                            {
                                imageSize = new Vector2(imageBoxSize.X, imageBoxSize.X / imageAspect);
                                imageOffset.Y = (imageBoxSize.Y - imageSize.Y) * 0.5f;
                            }
                            else
                            {
                                imageSize = new Vector2(imageBoxSize.Y * imageAspect, imageBoxSize.Y);
                                imageOffset.X = (imageBoxSize.X - imageSize.X) * 0.5f;
                            }

                            drawList.AddImage(
                                (ImTextureID)texture.Handle,
                                imageBoxPos + imageOffset,
                                imageBoxPos + imageOffset + imageSize
                            );
                            imageLoaded = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.Debug($"Failed to load feature image {card.ImagePath}: {ex.Message}");
                }
            }

            if (!imageLoaded)
            {
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.SetWindowFontScale(2.5f);
                string iconStr = card.Icon.ToIconString();
                var iconSize = ImGui.CalcTextSize(iconStr);
                var iconPos = imageBoxPos + (imageBoxSize - iconSize) * 0.5f;
                drawList.AddText(iconPos, ImGui.GetColorU32(new Vector4(0.4f, 0.5f, 0.7f, 0.6f)), iconStr);
                ImGui.SetWindowFontScale(1.0f);
                ImGui.PopFont();
            }

            drawList.AddRect(
                imageBoxPos,
                imageBoxPos + imageBoxSize,
                ImGui.GetColorU32(new Vector4(0.25f, 0.25f, 0.35f, 0.8f)),
                6f
            );

            float textAreaY = padding + imageHeight + (padding * 0.5f);
            float textAreaWidth = width - (padding * 2);
            float textAreaHeight = height - textAreaY - buttonHeight - (padding * 1.5f);

            ImGui.SetCursorScreenPos(startPos + new Vector2(padding, textAreaY));
            ImGui.BeginChild($"##CardText{card.Title}", new Vector2(textAreaWidth, textAreaHeight), false, ImGuiWindowFlags.NoScrollbar);

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
            var titleSize = ImGui.CalcTextSize(card.Title);
            float titleOffsetX = (textAreaWidth - titleSize.X) * 0.5f;
            if (titleOffsetX > 0)
                ImGui.SetCursorPosX(titleOffsetX);
            ImGui.Text(card.Title);
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.7f, 1.0f));
            var descSize = ImGui.CalcTextSize(card.Description, false, textAreaWidth);
            float descOffsetX = (textAreaWidth - Math.Min(descSize.X, textAreaWidth)) * 0.5f;
            if (descOffsetX > 0)
                ImGui.SetCursorPosX(descOffsetX);

            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textAreaWidth);
            ImGui.TextWrapped(card.Description);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();

            ImGui.EndChild();

            float buttonY = height - buttonHeight - padding;
            float buttonWidth = width - (padding * 2);
            ImGui.SetCursorScreenPos(startPos + new Vector2(padding, buttonY));

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.6f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.5f, 0.7f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.2f, 0.3f, 0.5f, 1.0f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);

            if (ImGui.Button($"{card.ActionLabel}##{card.Title}", new Vector2(buttonWidth, buttonHeight)))
            {
                card.OnClick?.Invoke();
            }

            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);

            ImGui.SetCursorScreenPos(startPos + new Vector2(width + 5, 0));
            ImGui.Dummy(new Vector2(0, height + 5));
        }

        private void DrawPatchNotes(float totalScale)
        {
            // Dynamic footer reserve: the NSFW checkbox only appears after the user scrolls to the
            // end, so we only make room for it in that state. Before then, reserve only enough for
            // the footer strip so patch-note content can use the extra vertical space.
            // Consistent 56px footer reserve (HTML footer height). The NSFW checkbox
            // lives inline at the end of scroll content so it doesn't shift the
            // footer strip.
            float footerReserve = 56f;
            // HTML .content: padding 6px 20px 18px 20px. 20px horizontal keeps cards
            // off the window edges. Zero top pad so the release tab butts right
            // against the banner bottom.
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20f * totalScale, 0f));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6f * totalScale, 8f * totalScale));
            ImGui.BeginChild("PatchNotesScroll", new Vector2(0, -footerReserve * totalScale), false, ImGuiWindowFlags.AlwaysVerticalScrollbar);
            ImGui.PopStyleVar(2);

            float currentScrollY = ImGui.GetScrollY();
            float maxScrollY = ImGui.GetScrollMaxY();

            if (maxScrollY > 0 && currentScrollY >= (maxScrollY * 0.85f))
                hasScrolledToEnd = true;

            // Continuous scroll fraction for the footer progress bar. 0 when the content fits without scroll.
            // Progress bar is sticky: once the user has read far enough that
            // fraction reached a given level, it stays there even if they
            // scroll back up. Tracks the high-water mark of the session.
            float currentFrac = maxScrollY > 0 ? Math.Clamp(currentScrollY / maxScrollY, 0f, 1f) : 1f;
            peakScrollFraction = Math.Max(peakScrollFraction, currentFrac);
            scrollFraction = peakScrollFraction;

            if (ShowFeatureSpotlight)
            {
                DrawFeatureSpotlight(totalScale);
            }

            ImGui.PushTextWrapPos();

            // Latest release - new Codex release tab + hero CategoryCard body.
            DrawReleaseTab("v2.1.1.0", "Achievements, Wardrobe, QoL & Optimizations", totalScale);
            Draw211Notes();

            if (!hasScrolledToEnd)
            {
                ImGui.Spacing();
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.8f, 0.8f));
                ImGui.Text("↓ Scroll down to see all features before continuing ↓");
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }

            // Previous releases - plain collapsing headers, bodies render with
            // their original DrawFeatureSection + bullet list formatting. The
            // fancy per-tier archive/pre-codex bar chrome was dropped - it
            // made the previous notes look like stacked text blocks compared
            // to the familiar collapsing-header layout they had before.
            if (DrawModernCollapsingHeader("v2.1.0.0 - Name Sync, Expanded RP Profiles, Custom Themes, and more", new Vector4(0.75f, 0.75f, 0.85f, 1.0f), false))
            {
                Draw210Notes();
            }

            if (DrawModernCollapsingHeader("v2.0.1.4 - 7.4 Compatibility, Mod Manager, Character Assignments", new Vector4(0.75f, 0.75f, 0.85f, 1.0f), false))
            {
                Draw214Notes();
            }

            if (DrawModernCollapsingHeader("v2.0.1.0 - Conflict Resolution, IPC, Apply to Target (GPose)", new Vector4(0.75f, 0.75f, 0.85f, 1.0f), false))
            {
                Draw201Notes();
            }

            if (DrawModernCollapsingHeader("v2.0.0.0 - Character Gallery & Visual Overhaul", new Vector4(0.75f, 0.75f, 0.85f, 1.0f), false))
            {
                Draw120Notes();
            }

            if (DrawModernCollapsingHeader("v1.1.0.8 - v1.1.1.2 - April 18 2025", new Vector4(0.75f, 0.75f, 0.85f, 1.0f), false))
            {
                Draw110Notes();
            }

            if (DrawModernCollapsingHeader("v1.1.0.(0-7) - April 09 2025", new Vector4(0.75f, 0.75f, 0.85f, 1.0f), false))
            {
                Draw1100Notes();
            }

            ImGui.PopTextWrapPos();
            ImGui.EndChild();
        }

        private bool DrawModernCollapsingHeader(string title, Vector4 titleColor, bool defaultOpen)
        {
            var flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;

            ImGui.PushStyleColor(ImGuiCol.Text, titleColor);
            bool isOpen = ImGui.CollapsingHeader(title, flags);
            ImGui.PopStyleColor();

            return isOpen;
        }

        // Codex release-tab header for the current/latest version (always expanded, not collapsible).
        // Gold tinted gradient, 3px left accent, chevron + version + tagline + gold LATEST pill.
        private void DrawReleaseTab(string version, string tagline, float totalScale)
        {
            // Plain rectangle (no chamfers) - HTML's slip silhouette on this bar was
            // making the LATEST pill feel cramped (the TR chamfer ate into it) and
            // left visible "dark strip" artefacts where the chamfer was clipped.
            // Keeps: gold gradient bg, 3px gold left rail, 1px inner rail.
            var drawList = ImGui.GetWindowDrawList();
            var startPos = ImGui.GetCursorScreenPos();
            float availWidth = ImGui.GetContentRegionAvail().X;

            float fontH = ImGui.GetFontSize();
            float padY = 9f * totalScale;
            float tabH = fontH + padY * 2f;
            var endPos = startPos + new Vector2(availWidth, tabH);

            // ── BG: flat midpoint of the horizontal gold gradient ──
            uint bgU = ImGui.GetColorU32(new Vector4(1.0f, 0.84f, 0.0f, 0.06f));
            drawList.AddRectFilled(startPos, endPos, bgU);

            // Horizontal gradient overlay: gold-left → transparent-right.
            uint bgL = ImGui.GetColorU32(new Vector4(1.0f, 0.84f, 0.0f, 0.08f));
            uint bgR = ImGui.GetColorU32(new Vector4(1.0f, 0.84f, 0.0f, 0.0f));
            drawList.AddRectFilledMultiColor(startPos, endPos, bgL, bgR, bgR, bgL);

            // ── 1px INSET RAIL (rectangle border) ──
            uint innerRailU = ImGui.GetColorU32(new Vector4(1.0f, 0.84f, 0.0f, 0.22f));
            drawList.AddRect(startPos, endPos, innerRailU, 0f, ImDrawFlags.None, 1f);

            // ── 3PX GOLD LEFT RAIL (full height, plain rect) ──
            uint goldFull = ImGui.GetColorU32(Boutique.Gold);
            drawList.AddRectFilled(
                new Vector2(startPos.X,                   startPos.Y),
                new Vector2(startPos.X + 3f * totalScale, endPos.Y),
                goldFull);

            // Content row
            float textY = startPos.Y + padY;
            float cursorX = startPos.X + 14f * totalScale;

            // Chevron
            string chev = "\u25BC";
            drawList.AddText(new Vector2(cursorX, textY), goldFull, chev);
            cursorX += ImGui.CalcTextSize(chev).X + 10f * totalScale;

            // Version
            drawList.AddText(new Vector2(cursorX, textY), goldFull, version);
            cursorX += ImGui.CalcTextSize(version).X + 12f * totalScale;

            // Separator pipe
            uint dimLine = ImGui.GetColorU32(Boutique.BorderSoft);
            drawList.AddText(new Vector2(cursorX, textY), dimLine, "|");
            cursorX += ImGui.CalcTextSize("|").X + 12f * totalScale;

            // Tagline -truncate with ellipsis if it would collide with the pill.
            uint textCol = ImGui.GetColorU32(new Vector4(0.91f, 0.92f, 0.94f, 1.0f));
            string pillText = "LATEST";
            var pillSize = ImGui.CalcTextSize(pillText);
            float pillPadX = 8f * totalScale, pillPadY = 3f * totalScale;
            float pillW = pillSize.X + pillPadX * 2f;
            float pillRightPad = 14f * totalScale;
            float taglineMaxW = endPos.X - pillRightPad - pillW - 10f * totalScale - cursorX;
            string taglineClipped = tagline;
            if (ImGui.CalcTextSize(taglineClipped).X > taglineMaxW)
            {
                while (taglineClipped.Length > 1 && ImGui.CalcTextSize(taglineClipped + "...").X > taglineMaxW)
                    taglineClipped = taglineClipped.Substring(0, taglineClipped.Length - 1);
                taglineClipped += "...";
            }
            drawList.AddText(new Vector2(cursorX, textY), textCol, taglineClipped);

            // Gold LATEST pill, right-aligned
            var pillMax = new Vector2(endPos.X - pillRightPad, textY + pillSize.Y + pillPadY);
            var pillMin = new Vector2(pillMax.X - pillW, textY - pillPadY);
            drawList.AddRectFilled(pillMin, pillMax, goldFull);
            uint pillTextCol = ImGui.GetColorU32(new Vector4(0.10f, 0.08f, 0.0f, 1.0f));
            drawList.AddText(new Vector2(pillMin.X + pillPadX, pillMin.Y + pillPadY), pillTextCol, pillText);

            // Advance layout cursor
            ImGui.Dummy(new Vector2(availWidth, tabH + 10f * totalScale));
        }

        private void DrawFeatureSection(string icon, string title, Vector4 accentColor)
        {
            // Breathing room above each feature header so consecutive sections
            // don't visually clip into each other when a version has many
            // categories back-to-back. The bg rect extends 5px above cursor
            // (via bgMin.Y -5), so the actual visible gap above the bar is
            // Spacing - 5; without this Dummy, neighbouring bars are ~3px
            // apart which reads as "crammed".
            float scale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;
            ImGui.Dummy(new Vector2(0, 10f * scale));

            var drawList = ImGui.GetWindowDrawList();
            var startPos = ImGui.GetCursorScreenPos();

            var bgMin = startPos + new Vector2(-10, -5);
            var bgMax = startPos + new Vector2(ImGui.GetContentRegionAvail().X + 10, 25);
            drawList.AddRectFilled(bgMin, bgMax, ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.15f, 0.6f)), 4f);

            drawList.AddRectFilled(bgMin, bgMin + new Vector2(3, bgMax.Y - bgMin.Y), ImGui.GetColorU32(accentColor), 2f);

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 1);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text(icon);
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.TextColored(accentColor, title);
            ImGui.Spacing();
        }

        // Bullet alias - all Draw*Notes methods call Bullet(text) for forward
        // compatibility (in case we ever introduce a tier-specific bullet
        // style). Currently delegates straight to ImGui.BulletText.
        private void Bullet(string text) => ImGui.BulletText(text);


        // Codex patch-note card: icon, title, tag, bullets.
        // Each bullet can optionally lead with a coloured phrase (Lead) rendered in the card's accent,
        // followed by the neutral Main body, and an optional muted Sub line beneath.
        // If PreviewImage is set, the filename is loaded from the Assets folder and shown in any
        // vertical slack at the bottom of the card (useful when the card is force-stretched taller
        // than its natural content by the hero-row layout).
        private class CategoryCard
        {
            public string Icon = "";
            public string Title = "";
            public Vector4 Accent;
            public string Tag = "";
            public List<(string Lead, string Main, string Sub, bool IsDim)> Bullets = new();
            public string? PreviewImage = null;
        }

        // Card layout constants (unscaled -callers multiply by totalScale)
        private const float CardPadX = 14f;
        private const float CardPadY = 12f;
        private const float CardIconSize = 20f;    // matches 04-codex-revised mockup (was 22f)
        private const float CardHeaderGap = 10f;   // gap between header row and first bullet
        private const float CardTickOffset = 14f;  // left inset for bullet text (past left border + tick)
        private const float CardBulletGap = 6f;    // vertical gap between bullets
        private const float CardSubGap = 2f;       // gap between main and sub text within a bullet
        private const float CardRowGap = 10f;      // vertical gap between rows of cards

        private float MeasureCardHeight(CategoryCard card, float width, float totalScale)
        {
            float padX = CardPadX * totalScale;
            float padY = CardPadY * totalScale;
            float iconSize = CardIconSize * totalScale;
            float headerGap = CardHeaderGap * totalScale;
            float tickOffset = CardTickOffset * totalScale;
            float bulletGap = CardBulletGap * totalScale;
            float subGap = CardSubGap * totalScale;
            float wrapW = Math.Max(40f, width - padX * 2f - tickOffset);

            float h = padY + iconSize + headerGap;
            for (int i = 0; i < card.Bullets.Count; i++)
            {
                var b = card.Bullets[i];
                string combined = string.IsNullOrEmpty(b.Lead) ? b.Main : (b.Lead + " " + b.Main);
                h += ImGui.CalcTextSize(combined, false, wrapW).Y;
                if (!string.IsNullOrEmpty(b.Sub))
                    h += subGap + ImGui.CalcTextSize(b.Sub, false, wrapW).Y;
                if (i < card.Bullets.Count - 1) h += bulletGap;
            }
            h += padY;
            return h;
        }

        private void DrawCategoryCard(CategoryCard card, float width, float height, float totalScale)
        {
            var drawList = ImGui.GetWindowDrawList();
            var startPos = ImGui.GetCursorScreenPos();
            var endPos = startPos + new Vector2(width, height);

            float padX = CardPadX * totalScale;
            float padY = CardPadY * totalScale;
            float iconSize = CardIconSize * totalScale;
            float tickOffset = CardTickOffset * totalScale;
            float bulletGap = CardBulletGap * totalScale;
            float subGap = CardSubGap * totalScale;
            float chamfer = 12f * totalScale; // HTML --cham-md

            // ── SLIP SILHOUETTE BODY ──
            // HTML: clip-path polygon with 12px chamfers TR + BL; flat fill with
            // subtle linear-gradient(180deg, surface-1, surface-0). We use the
            // midpoint of the gradient for the flat fill (gradient is very subtle).
            var bodyCol = Boutique.Lerp(Boutique.Surface1, Boutique.Surface0, 0.5f);
            Boutique.FillSlip(drawList, startPos, endPos, chamfer,
                ImGui.GetColorU32(bodyCol));

            // ── 1px INSET RAIL (border) ──
            // HTML: inset 0 0 0 1px color-mix(accent 28%, border-soft)
            var railCol = Boutique.Lerp(Boutique.BorderSoft, card.Accent, 0.28f);
            Boutique.StrokeSlip(drawList, startPos, endPos, chamfer,
                ImGui.GetColorU32(railCol), 1f);

            uint accentU = ImGui.GetColorU32(card.Accent);

            // ── 3PX TOP BAND (category accent, clipped to polygon's top edge) ──
            // HTML: inset 0 3px 0 0 var(--c). The chamfer cut on TR requires the
            // band to follow the slope. Rendered as a 4-point polygon that runs
            // along the top edge then mitres down into the slope.
            float bandH = 3f * totalScale;
            Span<Vector2> bandPts = stackalloc Vector2[4]
            {
                new Vector2(startPos.X,               startPos.Y),
                new Vector2(endPos.X - chamfer,       startPos.Y),
                new Vector2(endPos.X - chamfer + bandH, startPos.Y + bandH),
                new Vector2(startPos.X,               startPos.Y + bandH),
            };
            unsafe
            {
                fixed (Vector2* p = bandPts)
                    drawList.AddConvexPolyFilled(p, 4, accentU);
            }

            // (HTML's 30×30 TR ::after corner gradient omitted - ImGui can't render the
            // soft fall-off, and any finite-triangle approximation reads as hard shapes
            // in the corner rather than the subtle radial tint the mockup intends.)

            // Reference for legacy positioning math below (icon/header row)
            uint lineCol = ImGui.GetColorU32(Boutique.BorderSoft);

            // Header row: 22x22 accent icon tile with 4px TR micro-chamfer + title + right-aligned tag
            var iconMin = startPos + new Vector2(padX, padY);
            var iconMax = iconMin + new Vector2(iconSize, iconSize);
            // Icon tile silhouette: 5-point poly with 4px TR chamfer (HTML .cat-icon)
            float iconChamfer = Math.Min(4f * totalScale, iconSize * 0.5f);
            Span<Vector2> iconPts = stackalloc Vector2[5]
            {
                new Vector2(iconMin.X,                iconMin.Y),
                new Vector2(iconMax.X - iconChamfer, iconMin.Y),
                new Vector2(iconMax.X,                iconMin.Y + iconChamfer),
                new Vector2(iconMax.X,                iconMax.Y),
                new Vector2(iconMin.X,                iconMax.Y),
            };
            unsafe
            {
                fixed (Vector2* p = iconPts)
                    drawList.AddConvexPolyFilled(p, 5, accentU);
            }

            // Icon glyph -rendered at ~0.72x default so it feels like a small mark inside the
            // coloured square, with generous padding all around (matches 04-codex-revised mockup).
            ImGui.PushFont(UiBuilder.IconFont);
            var iconFontPtr = ImGui.GetFont();
            float iconGlyphScale = 0.72f;
            float iconPxSize = iconFontPtr.FontSize * iconGlyphScale;
            var iconNaturalSize = ImGui.CalcTextSize(card.Icon);
            var iconScaledSize = new Vector2(iconNaturalSize.X * iconGlyphScale, iconNaturalSize.Y * iconGlyphScale);
            var iconGlyphPos = iconMin + new Vector2(
                (iconSize - iconScaledSize.X) * 0.5f,
                (iconSize - iconScaledSize.Y) * 0.5f);
            drawList.AddText(iconFontPtr, iconPxSize, iconGlyphPos,
                ImGui.GetColorU32(new Vector4(0.04f, 0.045f, 0.06f, 1.0f)), card.Icon);
            ImGui.PopFont();

            // Title -HeaderFont gives the bigger, distinctive heading look from the mockup.
            string titleCaps = card.Title.ToUpperInvariant();
            uint titleCol = ImGui.GetColorU32(new Vector4(0.93f, 0.94f, 0.96f, 1.0f));
            Vector2 titleSize;
            Vector2 titlePos;
            using (plugin.HeaderFont?.Push())
            {
                titleSize = ImGui.CalcTextSize(titleCaps);
                titlePos = new Vector2(iconMax.X + 12f * totalScale, iconMin.Y + (iconSize - titleSize.Y) * 0.5f);
                drawList.AddText(titlePos, titleCol, titleCaps);
            }

            // Right-aligned tag in default font / accent colour.
            if (!string.IsNullOrEmpty(card.Tag))
            {
                string tagCaps = card.Tag.ToUpperInvariant();
                var tagSize = ImGui.CalcTextSize(tagCaps);
                var tagPos = new Vector2(endPos.X - padX - tagSize.X, iconMin.Y + (iconSize - tagSize.Y) * 0.5f);
                drawList.AddText(tagPos, accentU, tagCaps);
            }
            float fontH = ImGui.GetFontSize();

            // Bullets -use ImGui for wrapping; draw list for the left border line and accent tick.
            float bulletBorderX = startPos.X + padX + 4f * totalScale;
            float bulletTextX = startPos.X + padX + tickOffset;
            float wrapW = Math.Max(40f, width - padX * 2f - tickOffset);

            // PushTextWrapPos takes a WINDOW-LOCAL X. Our cursor/position math is in screen coords,
            // so convert: localWrapX = screenWrapX - windowPos.X. Without this the text never wraps
            // and just clips at the card edge.
            float windowOriginX = ImGui.GetWindowPos().X;
            float localWrapX = bulletTextX + wrapW - windowOriginX;

            float bulletY = iconMax.Y + CardHeaderGap * totalScale;

            var mainVec = new Vector4(0.91f, 0.92f, 0.94f, 1.0f);
            var subVec = new Vector4(0.48f, 0.50f, 0.56f, 1.0f);

            for (int i = 0; i < card.Bullets.Count; i++)
            {
                var b = card.Bullets[i];
                float itemStartY = bulletY;
                bool hasLead = !string.IsNullOrEmpty(b.Lead) && !b.IsDim;

                // Render the full combined text as a single wrapped block in the neutral colour.
                string combined = hasLead ? (b.Lead + " " + b.Main) : b.Main;
                var mainSize = ImGui.CalcTextSize(combined, false, wrapW);

                ImGui.SetCursorScreenPos(new Vector2(bulletTextX, itemStartY));
                ImGui.PushStyleColor(ImGuiCol.Text, b.IsDim ? subVec : mainVec);
                ImGui.PushTextWrapPos(localWrapX);
                ImGui.TextUnformatted(combined);
                ImGui.PopTextWrapPos();
                ImGui.PopStyleColor();

                // Overpaint just the Lead portion in the accent colour on top of the first line.
                // Assumes short leads (1-3 words) that always fit on line 1, which is how the data is authored.
                if (hasLead)
                {
                    drawList.AddText(new Vector2(bulletTextX, itemStartY), accentU, b.Lead);
                }

                bulletY = itemStartY + mainSize.Y;

                if (!string.IsNullOrEmpty(b.Sub))
                {
                    bulletY += subGap;
                    var subSize = ImGui.CalcTextSize(b.Sub, false, wrapW);
                    ImGui.SetCursorScreenPos(new Vector2(bulletTextX, bulletY));
                    ImGui.PushStyleColor(ImGuiCol.Text, subVec);
                    ImGui.PushTextWrapPos(localWrapX);
                    ImGui.TextUnformatted(b.Sub);
                    ImGui.PopTextWrapPos();
                    ImGui.PopStyleColor();
                    bulletY += subSize.Y;
                }

                // Left guide line (full item height) + short accent tick at the first-line centre.
                // Dim bullets skip the tick so they visually de-emphasise.
                drawList.AddLine(
                    new Vector2(bulletBorderX, itemStartY),
                    new Vector2(bulletBorderX, bulletY),
                    lineCol, 1f);

                if (!b.IsDim)
                {
                    float tickY = itemStartY + fontH * 0.5f;
                    drawList.AddLine(
                        new Vector2(bulletBorderX, tickY),
                        new Vector2(bulletBorderX + 8f * totalScale, tickY),
                        accentU, 1f);
                }

                if (i < card.Bullets.Count - 1) bulletY += bulletGap;
            }

            // Preview image in any remaining vertical slack (used for Achievements hero card
            // when it's force-stretched taller than its natural content by the hero-row layout).
            if (!string.IsNullOrEmpty(card.PreviewImage))
            {
                float imgTopMargin = 10f * totalScale;
                float imgBottomMargin = 10f * totalScale;
                float imgSideMargin = 4f * totalScale;
                float availH = (endPos.Y - padY) - bulletY - imgTopMargin;
                float availW = width - padX * 2f - imgSideMargin * 2f;

                if (availH > 50f * totalScale && availW > 50f * totalScale)
                {
                    try
                    {
                        string pluginDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "";
                        string imagePath = Path.Combine(pluginDirectory, "Assets", card.PreviewImage);
                        if (File.Exists(imagePath))
                        {
                            var tex = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
                            if (tex != null)
                            {
                                float imgAspect = (float)tex.Width / tex.Height;
                                float boxAspect = availW / (availH - imgBottomMargin);

                                Vector2 drawSize;
                                if (imgAspect > boxAspect)
                                {
                                    // image wider than box: fit by width
                                    drawSize = new Vector2(availW, availW / imgAspect);
                                }
                                else
                                {
                                    // image taller than box: fit by height
                                    float usableH = availH - imgBottomMargin;
                                    drawSize = new Vector2(usableH * imgAspect, usableH);
                                }

                                // Centre horizontally; anchor to the top of the slack region so it
                                // feels attached to the content above, not floating at the bottom.
                                float imgX = startPos.X + padX + (availW + imgSideMargin * 2f - drawSize.X) * 0.5f;
                                float imgY = bulletY + imgTopMargin;
                                var imgMin = new Vector2(imgX, imgY);
                                var imgMax = imgMin + drawSize;

                                drawList.AddImage((ImTextureID)tex.Handle, imgMin, imgMax);
                                drawList.AddRect(imgMin, imgMax, lineCol, 0f, ImDrawFlags.None, 1f);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Debug($"Failed to load preview image {card.PreviewImage}: {ex.Message}");
                    }
                }
            }

            // Park the cursor at the bottom-left of the card so the next row starts correctly.
            ImGui.SetCursorScreenPos(new Vector2(startPos.X, endPos.Y));
        }

        private void DrawCardFull(CategoryCard card, float totalScale)
        {
            float width = ImGui.GetContentRegionAvail().X;
            float h = MeasureCardHeight(card, width, totalScale);
            DrawCategoryCard(card, width, h, totalScale);
            ImGui.Dummy(new Vector2(0, CardRowGap * totalScale));
        }

        private void DrawCardRow2(CategoryCard left, CategoryCard right, float totalScale)
        {
            float avail = ImGui.GetContentRegionAvail().X;
            float gap = 10f * totalScale;
            float colW = (avail - gap) * 0.5f;

            float hL = MeasureCardHeight(left, colW, totalScale);
            float hR = MeasureCardHeight(right, colW, totalScale);
            float h = Math.Max(hL, hR);

            var rowStart = ImGui.GetCursorScreenPos();

            DrawCategoryCard(left, colW, h, totalScale);
            // Reset to the start of the row, advance past the left card's width for the right one.
            ImGui.SetCursorScreenPos(new Vector2(rowStart.X + colW + gap, rowStart.Y));
            DrawCategoryCard(right, colW, h, totalScale);

            // Park cursor below the row.
            ImGui.SetCursorScreenPos(new Vector2(rowStart.X, rowStart.Y + h));
            ImGui.Dummy(new Vector2(0, CardRowGap * totalScale));
        }

        // Asymmetric "L" layout: hero card on the left spans the full row height;
        // the right column stacks topRight over bottomRight.
        // Columns follow the codex mockup's 1.4fr / 1fr split.
        private void DrawCardHeroRow(CategoryCard hero, CategoryCard topRight, CategoryCard bottomRight, float totalScale)
        {
            float avail = ImGui.GetContentRegionAvail().X;
            float gap = 10f * totalScale;
            float totalUnits = 1.4f + 1.0f;
            float heroW = (avail - gap) * (1.4f / totalUnits);
            float rightW = (avail - gap) * (1.0f / totalUnits);

            float heroNaturalH = MeasureCardHeight(hero, heroW, totalScale);
            float topRightH = MeasureCardHeight(topRight, rightW, totalScale);
            float bottomRightNaturalH = MeasureCardHeight(bottomRight, rightW, totalScale);
            float rightColNaturalH = topRightH + gap + bottomRightNaturalH;

            // Both columns should finish at the same Y.
            // If the right column is taller, the hero stretches; if the hero is taller,
            // the bottom-right card stretches to swallow the slack. No dead air at the bottom
            // of either column either way.
            float rowH = Math.Max(heroNaturalH, rightColNaturalH);
            float drawHeroH = rowH;
            float drawTopRightH = topRightH;
            float drawBottomRightH = Math.Max(bottomRightNaturalH, rowH - topRightH - gap);

            var rowStart = ImGui.GetCursorScreenPos();

            DrawCategoryCard(hero, heroW, drawHeroH, totalScale);

            ImGui.SetCursorScreenPos(new Vector2(rowStart.X + heroW + gap, rowStart.Y));
            DrawCategoryCard(topRight, rightW, drawTopRightH, totalScale);

            ImGui.SetCursorScreenPos(new Vector2(rowStart.X + heroW + gap, rowStart.Y + topRightH + gap));
            DrawCategoryCard(bottomRight, rightW, drawBottomRightH, totalScale);

            ImGui.SetCursorScreenPos(new Vector2(rowStart.X, rowStart.Y + rowH));
            ImGui.Dummy(new Vector2(0, CardRowGap * totalScale));
        }

        private void Draw211Notes()
        {
            float totalScale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;

            // Banner-aligned accent palette (matches 04-codex-revised.html mockup)
            var gold         = new Vector4(1.00f, 0.84f, 0.00f, 1.0f); // #ffd600 -Achievements (Featured)
            var cyan         = new Vector4(0.16f, 0.71f, 0.96f, 1.0f); // #29b6f6 -Wardrobe
            var magenta      = new Vector4(0.95f, 0.17f, 0.49f, 1.0f); // #f12b7c -QoL (was sky, too similar to cyan)
            var violet       = new Vector4(0.49f, 0.34f, 0.76f, 1.0f); // #7e57c2 -Rename Migration
            var magentaSoft  = new Vector4(1.00f, 0.37f, 0.54f, 1.0f); // #ff5e8a -Privacy Fixes
            var slate        = new Vector4(0.42f, 0.48f, 0.56f, 1.0f); // #6a7b8f -Behind the Scenes

            var achievements = new CategoryCard
            {
                Icon = "\uf091", // trophy
                Title = "Achievements",
                Tag = "Featured",
                Accent = gold,
                PreviewImage = "Achievements.png",
                Bullets = new()
                {
                    ("80+ achievements", "across 8 categories.", "", false),
                    ("Trophy button", "on the main window glows gold for new completions.", "", false),
                    ("Slide-in toasts", "stack up to 3, one after another.", "", false),
                    ("Click any toast", "to dismiss it early.", "", false),
                    ("6 positions:", "corners or top/bottom centre.", "", false),
                    ("Retroactive credit", "for character/design counts, saved profile fields, and more.", "", false),
                    ("Built to encourage exploration,", "not pressure completionists.", "", false),
                    ("Points system:", "save them up for an upcoming rewards shop.", "", false),
                    ("", "Not for you? Settings → Achievements has a master toggle to hide it all. Progress is preserved if you change your mind.", "", true),
                }
            };

            var wardrobe = new CategoryCard
            {
                Icon = "\uf00a", // grid
                Title = "Wardrobe",
                Tag = "New",
                Accent = cyan,
                Bullets = new()
                {
                    ("Boutique coverflow:", "your designs sit on a lit stage; drag, flick, scroll, or arrow-key through them.", "", false),
                    ("Editorial info panel", "shows the focused design's name, mods, last applied, and edition.", "", false),
                    ("Click the focus card", "to apply; click any side card to bring it forward.", "", false),
                    ("Right-click the focus card", "to set its preview from your clipboard or toggle favourite.", "", false),
                    ("/wardrobe,", "or the hanger button in the Design Panel, or Shift+Click a Designs button.", "", false),
                    ("Per-character accent:", "optionally use the character's nameplate colour for the chassis.", "", false),
                    ("Fully themeable:", "colours and background image in the Custom Theme editor.", "", false),
                }
            };

            var rename = new CategoryCard
            {
                Icon = "\uf021", // sync
                Title = "Rename Migration",
                Tag = "New",
                Accent = violet,
                Bullets = new()
                {
                    ("Your likes carry over", "when you rename a CS+ character.", "", false),
                    ("In-game renames", "detected automatically on next apply.", "", false),
                    ("Manual migration", "tool in Settings covers renames from before this update.", "", false),
                    ("Clean delete:", "removing a CS+ character now cleans up the server copy too.", "", false),
                }
            };

            var privacy = new CategoryCard
            {
                Icon = "\uf023", // lock
                Title = "Privacy Fixes",
                Tag = "Fixed",
                Accent = magentaSoft,
                Bullets = new()
                {
                    ("Private profiles", "are now properly hidden from /viewrp.", "", false),
                    ("Exclude from Name Sync", "no longer hides your RP profile as well.", "", false),
                    ("Shared-name collisions:", "fixed mixed data when characters shared a display name.", "", false),
                }
            };

            var qol = new CategoryCard
            {
                Icon = "\uf013", // cog
                Title = "QoL & Improvements",
                Tag = "Polish",
                Accent = magenta,
                Bullets = new()
                {
                    ("Top pagination:", "page buttons now appear at the top of the character grid.", "", false),
                    ("Select All Gear/Hair", "button in Mod Manager → Currently Affecting You.", "", false),
                    ("Quick refresh", "for Mod Manager on the Add/Edit form.", "", false),
                    ("Manage known in-game characters", "in Settings → Character Assignments.", "", false),
                    ("Update notifications", "now appear in chat when a new CS+ release is available.", "", false),
                    ("Fixed", "Alias and Name Sync exclusion not saving on new characters.", "", false),
                }
            };

            var behind = new CategoryCard
            {
                Icon = "\uf0e7", // bolt
                Title = "Behind the Scenes",
                Tag = "Under the Hood",
                Accent = slate,
                Bullets = new()
                {
                    ("Optimised image storage", "to help keep CS+ running sustainably.", "", false),
                    ("Gallery cache:", "better caching on Gallery requests to reduce server traffic.", "", false),
                    ("Name Sync:", "new CS+ users may take ~60s to first appear nearby.", "", false),
                    ("Gallery:", "currently disabled and under construction.", "", false),
                }
            };

            // Featured row (codex asymmetric L): Achievements left (tall) | Wardrobe top-right, QoL bottom-right
            DrawCardHeroRow(achievements, wardrobe, qol, totalScale);
            // Remaining categories in a normal 2-col row, plus Behind the Scenes full-width
            DrawCardRow2(rename, privacy, totalScale);
            DrawCardFull(behind, totalScale);
        }

        private void Draw210Notes()
        {
            // Name Sync
            DrawFeatureSection("\uf007", "Name Sync", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("Show your CS+ character's name instead of your in-game name across the UI");
            Bullet("Your name appears in nameplates with an animated wave glow effect in your chosen colour");
            Bullet("Works in chat messages -- your CS+ name shows as the sender for tells, party, FC, and more");
            Bullet("The party list displays your CS+ name");
            Bullet("Target bar shows your CS+ name, including when you're someone's target-of-target");
            Bullet("Optional: Hide your Free Company tag from your nameplate");
            Bullet("Glow colour is based on your CS+ Character's nameplate colour to make your name stand out");
            Bullet("Shared Name Sync: See other CS+ users' custom names");
            Bullet("Privacy-first: Both you AND other users must opt-in to see each other's names");
            ImGui.Spacing();

            // Expanded RP Profiles
            DrawFeatureSection("\uf2c2", "Expanded RP Profiles", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            Bullet("Expand your RP profile with custom sections tailored to your character");
            Bullet("Add personality traits, backstory snippets, RP hooks, likes/dislikes, and more");
            Bullet("10 different section layouts: lists, quotes, timelines, pros/cons, key-value pairs, and grids");
            Bullet("Drag and drop to rearrange your sections exactly how you want them");
            Bullet("Connections system: Link to your own alt characters or other players you RP with");
            Bullet("Image galleries: Add multiple images to your profile with a built-in viewer");
            Bullet("Add a title and status under your name with icon support (e.g., 'The Wandering Bard')");
            ImGui.Spacing();

            // Custom Themes
            DrawFeatureSection("\uf53f", "Custom Themes", new Vector4(0.9f, 0.7f, 0.2f, 1.0f));
            Bullet("Personalize every part of your CS+ window - make it truly yours");
            Bullet("Customize colours for backgrounds, buttons, headers, tabs, text, scrollbars, and more");
            Bullet("Add a custom background image to your main window with opacity and positioning controls");
            Bullet("Zoom and pan your background image to frame it perfectly");
            Bullet("Choose a custom icon from 200+ options across 10 categories");
            Bullet("Card glow: Use each character's nameplate colour or set a single theme colour");
            Bullet("Save your favourite looks as presets and switch between them instantly");
            Bullet("Right-click icons to add them to your Favourites tab for quick access");
            ImGui.Spacing();

            // Job Assignments (Job → Character)
            DrawFeatureSection("\uf0ec", "Job Assignments (Job → Character)", new Vector4(0.5f, 0.8f, 0.9f, 1.0f));
            Bullet("Assign a character or design to each job or role");
            Bullet("Automatically switches character/design when you change jobs");
            Bullet("Supports both individual jobs and role-based assignments");
            Bullet("Enable in Settings → Job Assignments");
            ImGui.Spacing();

            // Gearset Assignments (Character → Job)
            DrawFeatureSection("\uf553", "Job Assignments (Character → Job)", new Vector4(0.9f, 0.7f, 0.4f, 1.0f));
            Bullet("Assign a job to any character or design");
            Bullet("Automatically switches to the assigned job when applying");
            Bullet("Design-level job overrides character-level setting");
            Bullet("Enable in Settings → Job Assignments → Enable Gearset Assignments");
            ImGui.Spacing();

            // Improved Immersive Dialogue
            DrawFeatureSection("\uf075", "Improved Immersive Dialogue", new Vector4(0.6f, 0.9f, 0.8f, 1.0f));
            Bullet("Better detection and replacement of player pronouns in NPC dialogue");
            Bullet("Improved handling of gendered titles (sir/madam, lord/lady, etc.)");
            Bullet("Fixed edge cases where replacements would accidentally affect the chat window");
            Bullet("Properly uses your Character's First and Last names.");
            ImGui.Spacing();

            // Honorific Glow Support
            DrawFeatureSection("\uf521", "Honorific Support", new Vector4(0.9f, 0.8f, 0.4f, 1.0f));
            Bullet("Added glow colour support for Honorific plugin titles");
            Bullet("Configure both title colour AND glow colour per-character");
            Bullet("Works with Honorific's existing gradient and animation systems");
            Bullet("Enable in Settings → Honorific");
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.75f, 1.0f));
            ImGui.TextWrapped("Honorific is maintained by Caraxi - consider supporting their work!");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            // Idle Pose Validation
            DrawFeatureSection("\uf21e", "Idle Pose Indicator", new Vector4(0.6f, 0.8f, 1.0f, 1.0f));
            Bullet("See your current idle pose number in the header, or use /select idle to check via command");
            Bullet("Added validation to prevent invalid pose numbers from causing issues");
            Bullet("More reliable pose restoration when logging in");
            Bullet("Better error handling when poses fail to apply");
            ImGui.Spacing();

            // Mod Deletion Warning
            DrawFeatureSection("\uf071", "Mod Deletion Warning", new Vector4(1.0f, 0.7f, 0.3f, 1.0f));
            Bullet("CS+ now warns you when a mod is deleted that was used in a Character or Design");
            Bullet("Shows which characters and designs are affected so you can update them");
            Bullet("Helps prevent broken Conflict Resolution configurations from deleted mods");
            ImGui.Spacing();

            // Bug Fixes
            DrawFeatureSection("\uf188", "QoL & Bug Fixes", new Vector4(0.9f, 0.4f, 0.4f, 1.0f));
            Bullet("Added option to use In-game file browser (found in Behavior Settings)");
            Bullet("Added Random Group Presets - create groups of Characters for random selection");
            Bullet("Added Quick Switch transparency slider to Custom Theme options");
            Bullet("Fixed duplicate chat messages appearing when using certain features");
            Bullet("Fixed Advanced Mode macro settings resetting unexpectedly");
            Bullet("Added an option to remember open/close state of the Main Window in Settings  → Behavior");
            Bullet("Toggles for: View RP Profile, Report CS+ Name, Block CS+ User to appear in Context Menus. Found in Settings  → Behavior");
            ImGui.Spacing();
        }

        private void Draw214Notes()
        {
            // 7.4 Compatibility Update
            DrawFeatureSection("\uf021", "7.4 Compatibility Update", new Vector4(0.6f, 0.8f, 1.0f, 1.0f));
            Bullet("Updated for Final Fantasy XIV patch 7.4");
            ImGui.Spacing();

            // Design Panel Enhancements
            DrawFeatureSection("\uf1fc", "Design Panel Enhancements", new Vector4(0.9f, 0.7f, 0.9f, 1.0f));
            Bullet("Design Previews now show in Quick Character Switch for easier design selection");
            Bullet("Active design is now highlighted in green in the design list");
            Bullet("Added save button to Design's Advanced Mode window for easier workflow");
            Bullet("Update CR for Existing Designs feature for hair/gear changes (other changes still need manual editing)");
            ImGui.Spacing();

            // Mod Manager Improvements
            DrawFeatureSection("\uf085", "Mod Manager Improvements", new Vector4(0.9f, 0.7f, 0.2f, 1.0f));
            Bullet("Standalone Mod Manager window for better organization (use '/select mods' command)");
            Bullet("Global Search functionality to search across all mod categories simultaneously");
            Bullet("'Currently Affecting You' section now shows: Tattoos, Eyes, Ears/Tail/Horns, Makeup/Face Paint");
            ImGui.Spacing();

            // Auto-Apply Last Used Design on Login
            DrawFeatureSection("\uf4fc", "Auto-Apply Last Used Design on Login", new Vector4(0.6f, 0.9f, 0.8f, 1.0f));
            Bullet("New setting that works with 'Auto-Apply Last Used Character on Login'");
            Bullet("When enabled, also automatically applies the last design you used for that character");
            Bullet("Perfect for maintaining your complete look when logging back in");
            Bullet("Appears as a sub-option when character auto-apply is enabled");
            ImGui.Spacing();

            // Winter/Christmas Theme
            DrawFeatureSection("\uf2dc", "Winter/Christmas Theme & Holiday Update", new Vector4(0.9f, 0.95f, 1.0f, 1.0f));
            Bullet("Winter and Christmas themes added");
            Bullet("Users can now freely choose which theme from the available list");
            ImGui.Spacing();

            // Apply to Target - GPose Support
            DrawFeatureSection("\uf140", "Apply to Target QoL", new Vector4(0.6f, 1.0f, 0.8f, 1.0f));
            Bullet("You can now use the Quick Character Switch window to Apply to Target by Right Clicking the Apply button");
            Bullet("You can now also CTRL+Right Click Apply to restore dropdowns back to your current Character + Design");
            ImGui.Spacing();

            // Bug Fixes
            DrawFeatureSection("\uf188", "Bug Fixes", new Vector4(0.9f, 0.4f, 0.4f, 1.0f));
            Bullet("Fixed Character Assignments not working properly (for real this time)");
            Bullet("Fixed Reapply on Job Change not working when using Character Assignments");
            ImGui.Spacing();
        }

        private void Draw201Notes()
        {
            // Conflict Resolution System
            DrawFeatureSection("\uf071", "Mod Conflict Resolution System", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            Bullet("Eliminates mod conflicts between Character designs automatically when switching between them");
            Bullet("Save complete mod configurations per design including enabled mods, mod settings, and option selections");
            Bullet("Intelligent Mod Manager with 21+ categories (Hair, Gear, Bodies, VFX, Animations, etc.) for easy organization");
            Bullet("Automatically categorizes and tracks mod additions, deletions, and changes -- no manual upkeep required");
            Bullet("Optional opt-in feature available in CS+ settings when you're ready to explore advanced mod management");
            ImGui.Spacing();

            // Enhanced IPC API
            DrawFeatureSection("\uf0c1", "API / IPC", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("API endpoints for other plugins to integrate with CS+");
            Bullet("Character switching, design management, and event notifications");
            Bullet("Used internally for Conflict Resolution, improved Apply to Target functionality, and the Snapshot feature");
            Bullet("Real-time character change events for plugin synchronization");
            ImGui.Spacing();

            // Apply to Target - GPose Support
            DrawFeatureSection("\uf140", "Apply to Target - GPose Support", new Vector4(0.6f, 1.0f, 0.8f, 1.0f));
            Bullet("Fixed Apply to Target functionality to work properly in GPose");
            Bullet("Converted from previous macro-based to new IPC-based system");
            Bullet("More reliable character application to targeted players");
            ImGui.Spacing();

            // Snapshot
            DrawFeatureSection("\uf030", "Snapshot Feature", new Vector4(0.9f, 0.7f, 1.0f, 1.0f));
            Bullet("New Snapshot feature - one-click add Design to Character Select+");
            Bullet("Use after saving a Design in Glamourer and setting up your Customize+ Profile");
            Bullet("Instantly adds your current look as a Design to the active Character in CS+");
            Bullet("Includes your current Customize+ Profile automatically");
            Bullet("CR mode: Auto-configures mods for your current outfit when using Conflict Resolution");
            Bullet("Simple workflow: Click camera button in Design Panel or use chat command");
            Bullet("Chat command: /select save - optionally add CR for Conflict Resolution mode");
            ImGui.Spacing();

            // UI Scaling
            DrawFeatureSection("\uf00e", "UI Scaling Done Right", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("Character Select+ is now properly responsive to the user's resolution and Dalamud's Global Font Scaling.");
            Bullet("Removed UI scaling options in Settings Panel.");
            Bullet("Let me know if there are any issues using this.");
            ImGui.Spacing();

            // Penumbra Collection UI Sync
            DrawFeatureSection("\uf021", "Penumbra Collection Synchronization", new Vector4(0.8f, 0.9f, 0.6f, 1.0f));
            Bullet("Switching characters now updates Penumbra's UI to show the correct collection");
            Bullet("Seamless integration between CS+ character switching and Penumbra interface");
            Bullet("Eliminates confusion about which collection is currently active");
            ImGui.Spacing();

            // Character Management Improvements
            DrawFeatureSection("\uf007", "Character Management Improvements", new Vector4(0.9f, 0.8f, 0.6f, 1.0f));
            Bullet("Fixed Character Assignments -- can now edit and remove character assignments");
            Bullet("Fixed Reorder Characters window -- changes now properly apply on save");
            Bullet("Added duplicate character name prevention for your own characters");
            ImGui.Spacing();

            // Backup & Restore System
            DrawFeatureSection("\uf0c7", "Backup & Restore System", new Vector4(0.4f, 0.8f, 1.0f, 1.0f));
            Bullet("Manual backup creation with optional custom naming");
            Bullet("Configuration file import -- appears at top of backup list");
            Bullet("Available Backups list with real-time backup count display");
            Bullet("Individual restore functionality for any backup file");
            Bullet("Automatic emergency backup creation before any restore operation");
            ImGui.Spacing();

            // Design Panel Enhancements
            DrawFeatureSection("\uf002", "Design Panel Enhancements", new Vector4(0.7f, 0.6f, 1.0f, 1.0f));
            Bullet("Added search functionality to quickly find specific designs");
            Bullet("New clipboard image pasting for Design Preview images");
            ImGui.Spacing();
        }

        private void Draw120Notes()
        {
            // Character Gallery (NEW!)
            DrawFeatureSection("\uf302", "Character Gallery", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            Bullet("View and share your CS+ Characters with everyone else!");
            Bullet("Opt-in feature - choose your main physical character to represent you");
            Bullet("Shows recent activity status with green globe indicators");
            Bullet("Like,favourite,add or even block other players' characters");
            Bullet("Click any profile to view their full RP Profile with backgrounds & effects");
            ImGui.Spacing();

            // NSFW Content Management (NEW!)
            DrawFeatureSection("\uf06e", "NSFW Content Management", new Vector4(1.0f, 0.7f, 0.4f, 1.0f));
            Bullet("RP Profile Editor now prompts you to mark profiles as NSFW if appropriate");
            Bullet("Gallery setting to opt-in to viewing NSFW profiles (disabled by default)");
            Bullet("Users must acknowledge they are 18+ to view NSFW content in the gallery");
            ImGui.Spacing();

            // Revamped RP Profiles
            DrawFeatureSection("\uf2c2", "Revamped RP Profiles", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("Complete visual redesign with new layout and styling");
            Bullet("80+ FFXIV location backgrounds to choose from");
            Bullet("Animated visual effects: butterflies, fireflies, falling leaves, and more");
            Bullet("Real-time preview - see changes instantly in the editor");
            Bullet("Right-click any player name to view their RP Profile directly");
            ImGui.Spacing();

            // Immersive Dialogue (NEW!)
            DrawFeatureSection("\uf075", "Immersive Dialogue System", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            Bullet("NPCs now use your CS+ Character's name, pronouns, and desired titles in dialogue!");
            Bullet("Integration with he/him, she/her, and they/them pronouns");
            Bullet("Granular settings: enable names, pronouns, gendered terms, or race separately");
            Bullet("Customizable they/them neutral titles: friend, Mx., traveler, adventurer, or choose your own!");
            Bullet("Only affects dialogue referring to your character - NPCs keep their own pronouns");
            Bullet("Requires an active CS+ character with RP Profile pronouns set");
            Bullet("If you find any instances in which it doesn't seem to be working please report them in the discord!");
            ImGui.Spacing();

            // Main Window UI Update
            DrawFeatureSection("\uf53f", "Main Window Visual Overhaul", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("Complete redesign with compact layout and enhanced visuals");
            Bullet("Character cards with integrated nameplates and action buttons");
            Bullet("Glowing borders and enhanced hover effects");
            Bullet("Optional setting for profiles to grow slightly on hover");
            Bullet("Crown indicator for your designated Main Character");
            Bullet("Resize Design Panel freely");
            Bullet("Drag & Drop character reordering added to Main Window (leftward movement only due to ImGui limitations)");
            ImGui.Spacing();

            // Tutorial System (NEW!)
            DrawFeatureSection("\uf19d", "Interactive Tutorial System", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            Bullet("Brand new guided tutorial for first-time users");
            Bullet("Highlights and points to buttons and fields you need to interact with");
            Bullet("Step-by-step guidance through Characters, Designs, and RP Profiles");
            Bullet("Can be ended at any time if you prefer to explore on your own");
            ImGui.Spacing();

            // Design Preview Images (NEW!)
            DrawFeatureSection("\uf03e", "Design Preview Images", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("Add custom preview images to your Designs");
            Bullet("Preview images by hovering over the Apply (✓) button");
            Bullet("Helps you quickly identify Designs at a glance");
            ImGui.Spacing();

            // Main Game Commands (NEW!)
            DrawFeatureSection("\uf120", "Base Game Command Support", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            Bullet("Add base game commands through Advanced Mode");
            Bullet("Example: Add '/gearset change 1' to switch jobs when applying Designs");
            Bullet("Perfect combo with 'Reapply Last Design on Job Change' setting");
            ImGui.Spacing();

            // Random Character + Outfit (NEW!)
            DrawFeatureSection("\uf074", "Random Character & Outfit", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("New 'Random' button for spontaneous character switching");
            Bullet("Randomly picks from your CS+ Characters and their Designs");
            Bullet("Setting to limit random selection to only favourited items");
            ImGui.Spacing();

            // Main CS+ Character (NEW!)
            DrawFeatureSection("\uf521", "Main CS+ Character", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            Bullet("Designate your main CS+ Character with a crown indicator");
            Bullet("Crown display is optional - toggle in settings");
            Bullet("'Reapply on Login' can be set to only apply your Main Character");
            ImGui.Spacing();

            // Character Assignments (NEW!)
            DrawFeatureSection("\uf0c1", "Character Assignments", new Vector4(0.6f, 1.0f, 0.8f, 1.0f));
            Bullet("Assign specific CS+ Characters to specific in-game characters");
            Bullet("Auto-apply designated CS+ characters when logging into assigned real characters");
            Bullet("Dropdown selection from characters the plugin has seen before");
            Bullet("Multiple real characters can share the same CS+ character");
            Bullet("Takes priority over 'last used' system but respects Main Character Only Mode");
            Bullet("Perfect for players with multiple alts who want consistent character setups");
            ImGui.Spacing();

            // Quick Character Switch Improvements
            DrawFeatureSection("\uf0e7", "Quick Character Switch Updates", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("Now remembers your last used character like Apply on Login");
            Bullet("Ready to go when you log in as that character");
            Bullet("Will also switch to be on your current CS+ Character if applied through other methods");
            ImGui.Spacing();

            // Bug Fixes & QoL
            DrawFeatureSection("\uf085", "Bug Fixes & Quality of Life", new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
            Bullet("Fixed Quick Switch window scroll issues");
            Bullet("Disabled window docking to prevent UI conflicts");
            Bullet("Added ghost images for drag and drop operations");
            Bullet("Automatic character config backup on updates or every 7 days");
            Bullet("Various performance improvements and optimizations");
        }

        private void Draw110Notes()
        {
            // Apply Character on Login
            DrawFeatureSection("\uf4fc", "Apply Character on Login", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("New opt-in setting in the plugin options.");
            Bullet("Character Select+ will remember the last applied character.");
            Bullet("Next time you log in, it will automatically apply that character.");
            Bullet("⚠️ May conflict if you are using Glamourer Automations.");
            ImGui.Spacing();

            // Apply Appearance on Job Change
            DrawFeatureSection("\uf4fc", "Apply Appearance on Job Change", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("New opt-in setting in the plugin options.");
            Bullet("Character Select+ will remember the last applied character and/or design.");
            Bullet("When you switch between jobs, it will automatically apply that character/design.");
            Bullet("⚠️ WILL 100 percent conflict if you are using Glamourer Automations.");
            ImGui.Spacing();

            // Designs
            DrawFeatureSection("\uf07b", "Design Panel Rework", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("Buttons now only appear on hover, keeping the panel clean and focused.");
            Bullet("Reorder designs by dragging the coloured handle‐bar on the left -click and drag to move.");
            Bullet("Create new folders inline via the folder icon next to the + button, no extra windows needed.");
            Bullet("Drag-and-drop designs into, out of, and between folders directly within the panel.");
            Bullet("Right-click folders for inline Rename/Delete context menu, with instant application.");
            ImGui.Spacing();

            // Compact Quick Switch
            DrawFeatureSection("\uf0a0", "Compact Quick Character Switch", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("Toggleable setting to hide the title bar and window frame for a slim bar.");
            Bullet("Keeps dropdowns and apply button only, preserving full switch functionality.");
            ImGui.Spacing();

            // UI Scaling Option
            DrawFeatureSection("\uf00e", "UI Scale Setting", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("You can now adjust the plugin UI scale from the settings menu.");
            Bullet("Great for users on high-resolution monitors or 4K displays.");
            Bullet("Let me know if there are any issues using this.");
            Bullet("⚠️ If your UI is fine as-is, best to leave this be.");
            ImGui.Spacing();
        }

        private void Draw1100Notes()
        {
            // RP Profile Panel
            DrawFeatureSection("\uf2c2", "RolePlay Profile Panel", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("Add bios, pronouns, orientation, and more for each character.");
            Bullet("Choose a unique image or reuse the character image.");
            Bullet("Use pan and zoom controls to fine-tune the RP portrait.");
            Bullet("Control visibility: keep private or share with others.");
            Bullet("Once applied, that character's RP profile is active.");
            Bullet("You can view others' profiles (if shared) and vice versa.");
            Bullet("Use /viewrp self | /t | First Last@World to view.");
            Bullet("Right-click in the party list, friends list, or chat to access shared RP cards.");
            ImGui.Spacing();

            // Glamourer Automations
            DrawFeatureSection("\uf5c3", "Glamourer Automations for Characters & Designs", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("Characters & Designs can now trigger specific Glamourer Automation profiles.");
            Bullet("This is *opt-in* -toggle it in plugin settings.");
            Bullet("If no automation is assigned, the design defaults to 'None'.");
            ImGui.Spacing();
            ImGui.Text("To avoid errors, set up a 'None' automation:");
            Bullet("1. Open Glamourer > Automations.");
            Bullet("2. Create an Automation named 'None'.");
            Bullet("3. Add your in-game character name beside 'Any World' then Set to Character.");
            Bullet("4. That's it. Don't touch anything else, you're done!");
            ImGui.Spacing();

            // Customize+
            DrawFeatureSection("\uf234", "Customize+ Profiles for Designs", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("Each design can now assign its own Customize+ profile.");
            Bullet("This gives you finer control over visual changes per design.");
            ImGui.Spacing();

            // Manual Reordering
            DrawFeatureSection("\uf0b0", "Manual Character Reordering", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("Use the 'Reorder Characters' button at the bottom-left.");
            Bullet("Drag and drop profiles, then press Save to lock it in.");
            ImGui.Spacing();

            // Search
            DrawFeatureSection("\uf002", "Character Search Bar", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("Click the magnifying glass to search by name instantly.");
            ImGui.Spacing();

            // Tagging
            DrawFeatureSection("\uf07b", "Tagging System", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("Add comma-separated 'tags' to organize characters.");
            Bullet("Click the filter icon to filter -characters can appear in multiple tags!");
            ImGui.Spacing();

            // Apply to Target
            DrawFeatureSection("\uf140", "Right-click → Apply to Target", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("Right-click a character in Character Select+ with a target selected.");
            Bullet("Apply their setup -or even one of their individual designs -to the target.");
            ImGui.Spacing();

            // Copy Designs
            DrawFeatureSection("\uf0c5", "Copy Designs Between Characters", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            Bullet("Hold Shift and click the '+' button in Designs to open the Design Importer.");
            Bullet("Click the + beside a design to copy it. Repeat as needed!");
            ImGui.Spacing();

            // Other changes
            DrawFeatureSection("\uf085", "Other Changes", new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
            Bullet("Older Design macros were automatically upgraded.");
            Bullet("Various UI tweaks, bugfixes, and behind-the-scenes improvements.");
        }

        // Codex footer: tri-gradient progress bar on the left, gold arrow-capped "MARK AS READ" on the right.
        // Keeps the existing scroll-to-end + NSFW-ack gate.
        private void DrawBottomButton(float totalScale)
        {
            // HTML .footer: 56px tall, surface-0 bg, 1px gold-deep top hairline (faded at
            // outer ends, solid at middle, 35% opacity). Left cluster = "Read" + 140×4
            // bar + percentage. Right cluster = slip-silhouette acknowledge button.
            bool buttonEnabled = hasScrolledToEnd && hasAcknowledgedNSFW;
            var drawList = ImGui.GetWindowDrawList();

            // Position the footer off the cursor that sits right after the scroll
            // child's EndChild (flush, no gap). X snaps to the window's left edge
            // so we get edge-to-edge width regardless of any pending cursor X.
            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            float stripH = 56f * totalScale;
            float padX = 20f * totalScale;
            var cursorNow = ImGui.GetCursorScreenPos();
            var stripStart = new Vector2(windowPos.X, cursorNow.Y);
            float availW = windowSize.X;
            float stripRight = stripStart.X + availW;
            float stripCenterY = stripStart.Y + stripH * 0.5f;
            float fontH = ImGui.GetFontSize();

            // Footer surface bg (surface-0) so the rainbow bar and the gold button sit
            // on their own panel rather than floating over the content bg.
            drawList.AddRectFilled(
                stripStart,
                new Vector2(stripRight, stripStart.Y + stripH),
                ImGui.GetColorU32(Boutique.Surface0));

            // ── TOP HAIRLINE ──
            // HTML ::before: 1px, 35% opacity, linear-gradient(90deg, transparent,
            // gold-deep 25%, gold-deep 75%, transparent). Faded at ends, solid mid.
            uint ruleMid  = ImGui.GetColorU32(Boutique.WithAlpha(Boutique.GoldDeep, 0.35f));
            uint ruleEdge = ImGui.GetColorU32(Boutique.WithAlpha(Boutique.GoldDeep, 0.0f));
            drawList.AddRectFilledMultiColor(
                new Vector2(stripStart.X, stripStart.Y),
                new Vector2(stripStart.X + availW * 0.5f, stripStart.Y + 1f * totalScale),
                ruleEdge, ruleMid, ruleMid, ruleEdge);
            drawList.AddRectFilledMultiColor(
                new Vector2(stripStart.X + availW * 0.5f, stripStart.Y),
                new Vector2(stripRight, stripStart.Y + 1f * totalScale),
                ruleMid, ruleEdge, ruleEdge, ruleMid);

            // ── LEFT: "Read" + 140×4 gradient bar + percentage ──
            string readLabel = "Read";
            var readSize = ImGui.CalcTextSize(readLabel);
            uint dimText = ImGui.GetColorU32(Boutique.TextFaint);
            drawList.AddText(new Vector2(stripStart.X + padX, stripCenterY - fontH * 0.5f), dimText, readLabel);

            float barX = stripStart.X + padX + readSize.X + 10f * totalScale;
            float barH = 4f * totalScale;
            float barY = stripCenterY - barH * 0.5f;
            float barW = 140f * totalScale;

            // Track
            uint bgLine = ImGui.GetColorU32(Boutique.BorderSoft);
            drawList.AddRectFilled(
                new Vector2(barX, barY),
                new Vector2(barX + barW, barY + barH),
                bgLine);

            // Fill - original magenta → gold → cyan rainbow (user preference).
            float fill = Math.Clamp(scrollFraction, 0f, 1f);
            if (fill > 0.001f)
            {
                float fillW = barW * fill;
                uint magentaU = ImGui.GetColorU32(new Vector4(0.95f, 0.17f, 0.49f, 1.0f));
                uint goldU    = ImGui.GetColorU32(new Vector4(1.00f, 0.84f, 0.00f, 1.0f));
                uint cyanU    = ImGui.GetColorU32(new Vector4(0.16f, 0.71f, 0.96f, 1.0f));

                float halfFillW = Math.Min(fillW, barW * 0.5f);
                drawList.AddRectFilledMultiColor(
                    new Vector2(barX, barY),
                    new Vector2(barX + halfFillW, barY + barH),
                    magentaU, goldU, goldU, magentaU);
                if (fill > 0.5f)
                {
                    float secondHalfFillW = fillW - barW * 0.5f;
                    drawList.AddRectFilledMultiColor(
                        new Vector2(barX + barW * 0.5f, barY),
                        new Vector2(barX + barW * 0.5f + secondHalfFillW, barY + barH),
                        goldU, cyanU, cyanU, goldU);
                }
            }

            string pct = $"{(int)Math.Round(fill * 100)}%";
            uint pctCol = ImGui.GetColorU32(Boutique.Gold);
            drawList.AddText(
                new Vector2(barX + barW + 10f * totalScale, stripCenterY - fontH * 0.5f),
                pctCol, pct);

            // ── RIGHT: slip-silhouette acknowledge button (Variant C · dominant CTA) ──
            // Slightly bigger than the 22×9 baseline so the button has presence, but
            // not so large that it overwhelms the footer. Computed FIRST so the
            // middle checkbox can be centred between the progress cluster and button.
            string btnText = "Mark as Read";
            var btnTextSize = ImGui.CalcTextSize(btnText);
            float btnPadX = 26f * totalScale;
            float btnPadY = 10f * totalScale;
            float btnH = btnTextSize.Y + btnPadY * 2f;
            float btnW = btnTextSize.X + btnPadX * 2f;
            float btnChamfer = 10f * totalScale;

            var btnRectMax = new Vector2(stripRight - padX, stripCenterY + btnH * 0.5f);
            var btnRectMin = new Vector2(btnRectMax.X - btnW, stripCenterY - btnH * 0.5f);

            // ── MIDDLE: NSFW acknowledgement (Variant C · compressed copy) ──
            // Short warning so the checkbox fits comfortably in the middle column
            // of the 3-col footer without crowding the button. Centred horizontally
            // in the space between the progress cluster (left) and the button (right).
            if (hasScrolledToEnd)
            {
                string checkboxText = "Mature content. I acknowledge.";
                var fontPtr = ImGui.GetFont();
                float labelFontSize = fontH; // native default font - crisp, no scaling
                var cbTextSize = ImGui.CalcTextSize(checkboxText);
                float cbBoxSize = 16f * totalScale;
                float cbTextPad = 7f * totalScale;
                float cbRowW = cbBoxSize + cbTextPad + cbTextSize.X;

                // Middle-column centring: from the end of the progress cluster
                // (pctEndX + gap) to the start of the button (btnRectMin.X - gap).
                float midGap = 24f * totalScale;
                float pctW = ImGui.CalcTextSize("100%").X;
                float pctEndX = barX + barW + 10f * totalScale + pctW;
                float midStart = pctEndX + midGap;
                float midEnd = btnRectMin.X - midGap;
                float midSpan = Math.Max(0f, midEnd - midStart);
                float cbStartX = midStart + (midSpan - cbRowW) * 0.5f;

                var boxMin = new Vector2(cbStartX, stripCenterY - cbBoxSize * 0.5f);
                var boxMax = boxMin + new Vector2(cbBoxSize, cbBoxSize);

                ImGui.SetCursorScreenPos(boxMin);
                if (ImGui.InvisibleButton("##nsfwCb", new Vector2(cbRowW, cbBoxSize)))
                    hasAcknowledgedNSFW = !hasAcknowledgedNSFW;
                bool cbHovered = ImGui.IsItemHovered();

                uint boxBg = ImGui.GetColorU32(new Vector4(0.08f, 0.08f, 0.10f, 0.95f));
                uint boxBorder = ImGui.GetColorU32(new Vector4(cbHovered ? 0.90f : 0.55f,
                                                                cbHovered ? 0.70f : 0.45f,
                                                                cbHovered ? 0.40f : 0.25f,
                                                                1.0f));
                drawList.AddRectFilled(boxMin, boxMax, boxBg);
                drawList.AddRect(boxMin, boxMax, boxBorder, 0f, ImDrawFlags.None, 1f);

                if (hasAcknowledgedNSFW)
                {
                    uint checkCol = ImGui.GetColorU32(new Vector4(0.95f, 0.78f, 0.45f, 1f));
                    float cx = boxMin.X;
                    float cy = boxMin.Y;
                    drawList.AddLine(
                        new Vector2(cx + cbBoxSize * 0.22f, cy + cbBoxSize * 0.52f),
                        new Vector2(cx + cbBoxSize * 0.44f, cy + cbBoxSize * 0.74f),
                        checkCol, 1.8f * totalScale);
                    drawList.AddLine(
                        new Vector2(cx + cbBoxSize * 0.44f, cy + cbBoxSize * 0.74f),
                        new Vector2(cx + cbBoxSize * 0.80f, cy + cbBoxSize * 0.28f),
                        checkCol, 1.8f * totalScale);
                }

                uint labelCol = ImGui.GetColorU32(new Vector4(0.9f, 0.75f, 0.5f, 1.0f));
                float labelYCorr = labelFontSize * 0.06f;
                drawList.AddText(fontPtr, labelFontSize,
                    new Vector2(boxMax.X + cbTextPad, stripCenterY - labelFontSize * 0.5f + labelYCorr),
                    labelCol, checkboxText);
            }

            ImGui.SetCursorScreenPos(btnRectMin);
            bool clicked = ImGui.InvisibleButton("##codexMarkAsRead", new Vector2(btnW, btnH));
            bool hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
            bool active = buttonEnabled && ImGui.IsItemActive();

            // ── Hover-enter detection (drives the 700ms sheen sweep) ──
            if (buttonEnabled && hovered && !markBtnWasHovered)
                markBtnHoverStart = (float)ImGui.GetTime();
            markBtnWasHovered = hovered;
            if (!hovered) markBtnHoverStart = -1f;

            // ── Release detection (drives the smooth press→idle bounce) ──
            if (!active && markBtnWasActive)
                markBtnReleaseTime = (float)ImGui.GetTime();
            markBtnWasActive = active;

            // pressAmount is clamped ease-out for colours/alphas. pressAmountGeom
            // is back-out so the geometry overshoots (button lifts past rest).
            const float releaseDuration = 0.42f;
            float pressAmount;
            float pressAmountGeom;
            if (active)
            {
                pressAmount = 1f;
                pressAmountGeom = 1f;
            }
            else if (markBtnReleaseTime >= 0f)
            {
                float e = (float)ImGui.GetTime() - markBtnReleaseTime;
                if (e >= releaseDuration)
                {
                    pressAmount = 0f;
                    pressAmountGeom = 0f;
                    markBtnReleaseTime = -1f;
                }
                else
                {
                    float t = e / releaseDuration;
                    // Clean ease-out cubic for colour/alpha
                    float easeOut = 1f - MathF.Pow(1f - t, 3f);
                    pressAmount = 1f - easeOut;

                    // Back-out (overshoot) ease for geometry. Standard
                    // cubic-bezier back-out with c1=2.2 gives ~12% overshoot
                    // at the midpoint - button lifts noticeably past rest
                    // before settling. Classic "spring out" feel.
                    const float c1 = 2.2f;
                    const float c3 = c1 + 1f;
                    float tm = t - 1f;
                    float backOut = 1f + c3 * tm * tm * tm + c1 * tm * tm;
                    pressAmountGeom = 1f - backOut;
                }
            }
            else
            {
                pressAmount = 0f;
                pressAmountGeom = 0f;
            }

            // ── Target parameter pack (hover if hovered, else idle) ──
            // These define the resting state the button blends back to.
            float tgtTranslateY   = hovered ? -2f : 0f;
            Vector4 tgtTopCol     = hovered ? new Vector4(1.0f, 0.89f, 0.40f, 1f) : Boutique.GoldWarm;
            Vector4 tgtBotCol     = hovered ? Boutique.GoldWarm : Boutique.Gold;
            // Soft halo, single peak offset/alpha drives a multi-layer stack
            // (see haloLayers loop below).  Replaces the old hard inner+outer
            // 2-layer pair which read as "two stacked shapes" rather than a
            // diffuse glow.
            float tgtPeakOff      = hovered ? 28f : 12f;
            float tgtPeakAlpha    = hovered ? 0.34f : 0.18f;
            // Inner/outer kept around for press-state shape collapse only
            float tgtInnerOff     = hovered ? 5f : 3f;
            float tgtInnerAlpha   = hovered ? 0.32f : 0.22f;
            float tgtOuterOff     = hovered ? 14f : 9f;
            float tgtOuterAlpha   = hovered ? 0.14f : 0.09f;
            float tgtBracketAlpha = hovered ? 0.70f : 0.55f;
            float tgtTopHighlight = hovered ? 1f : 0f;

            // ── Press parameter pack ──
            const float prsTranslateY   = +1f;
            Vector4    prsTopCol        = Boutique.Gold;
            Vector4    prsBotCol        = Boutique.GoldDeep;
            const float prsPeakOff      = 0f;
            const float prsPeakAlpha    = 0f;
            const float prsInnerOff     = 1f;
            const float prsInnerAlpha   = 0.10f;
            const float prsOuterOff     = 0f;
            const float prsOuterAlpha   = 0f;
            const float prsBracketAlpha = 0.32f;

            // ── Interpolate ──
            // Geometry uses pressAmountGeom (back-out ease → overshoot past
            // the target on rebound). Colour + alpha use pressAmount (clean
            // ease-out, clamped 0..1, so they don't extrapolate).
            float translateY       = (tgtTranslateY   + (prsTranslateY   - tgtTranslateY  ) * pressAmountGeom) * totalScale;
            float innerOff         = (tgtInnerOff     + (prsInnerOff     - tgtInnerOff    ) * pressAmountGeom) * totalScale;
            float outerOff         = (tgtOuterOff     + (prsOuterOff     - tgtOuterOff    ) * pressAmountGeom) * totalScale;
            float peakOff          = (tgtPeakOff      + (prsPeakOff      - tgtPeakOff     ) * pressAmountGeom) * totalScale;
            float peakAlpha        = (tgtPeakAlpha    + (prsPeakAlpha    - tgtPeakAlpha   ) * pressAmount);

            Vector4 topCol         = Boutique.Lerp(tgtTopCol,     prsTopCol,     pressAmount);
            Vector4 botCol         = Boutique.Lerp(tgtBotCol,     prsBotCol,     pressAmount);
            float innerAlpha       = (tgtInnerAlpha   + (prsInnerAlpha   - tgtInnerAlpha  ) * pressAmount);
            float outerAlpha       = (tgtOuterAlpha   + (prsOuterAlpha   - tgtOuterAlpha  ) * pressAmount);
            float bracketAlpha     = (tgtBracketAlpha + (prsBracketAlpha - tgtBracketAlpha) * pressAmount);
            float topHighlightA    = (tgtTopHighlight * (1f - pressAmount)) * 0.55f;
            float pressRingAlpha   = pressAmount * 0.45f;   // 0 at rest, 0.45 at full press
            float pressShadowAlpha = pressAmount * 0.45f;

            // Safety clamp: offsets can't go negative (would flip the rect
            // inside-out). If the overshoot pushes an offset past 0 toward
            // "negative inflate", clamp to 0 so the halo just momentarily
            // vanishes rather than inverting.
            if (innerOff < 0f) innerOff = 0f;
            if (outerOff < 0f) outerOff = 0f;

            var bMin = new Vector2(btnRectMin.X, btnRectMin.Y + translateY);
            var bMax = new Vector2(btnRectMax.X, btnRectMax.Y + translateY);

            // ── Halo (soft multi-layer stack, replaces the old 2-layer
            //    inner/outer pair).  6 concentric layers at progressive
            //    offsets and quadratic alpha falloff = no hard outer edge,
            //    reads as a cloud of light rather than two stacked shapes. ──
            if (buttonEnabled && peakAlpha > 0.001f && peakOff > 0.001f)
            {
                const int haloLayers = 6;
                for (int li = 0; li < haloLayers; li++)
                {
                    float ti = li / (float)(haloLayers - 1);          // 0 inner → 1 outer
                    float off = peakOff * MathF.Pow(ti, 0.85f);       // slight ease so inner layers cluster
                    float a   = peakAlpha * MathF.Pow(1f - ti, 1.5f); // smooth falloff to 0
                    if (a < 0.003f) continue;
                    Boutique.FillSlip(drawList,
                        new Vector2(bMin.X - off, bMin.Y - off),
                        new Vector2(bMax.X + off, bMax.Y + off),
                        btnChamfer + off,
                        ImGui.GetColorU32(Boutique.WithAlpha(Boutique.Gold, a)));
                }
            }

            // Negative outer ring (scales with pressAmount)
            if (buttonEnabled && pressRingAlpha > 0.001f)
            {
                float ringOff = 1f * totalScale;
                Boutique.StrokeSlip(drawList,
                    new Vector2(bMin.X - ringOff, bMin.Y - ringOff),
                    new Vector2(bMax.X + ringOff, bMax.Y + ringOff),
                    btnChamfer + ringOff,
                    ImGui.GetColorU32(Boutique.WithAlpha(Boutique.Shell, pressRingAlpha)),
                    1f);
            }

            // ── Body (midpoint of blended top/bot gradient colours) ──
            float bodyAlpha = buttonEnabled ? 1.0f : 0.38f;
            Vector4 bodyMid = Boutique.Lerp(topCol, botCol, 0.5f);
            Boutique.FillSlip(drawList, bMin, bMax, btnChamfer,
                ImGui.GetColorU32(Boutique.WithAlpha(bodyMid, bodyAlpha)));

            // Top-inside white highlight (modulated by topHighlightA - fades
            // away during press, comes back during release bounce)
            if (topHighlightA > 0.001f)
            {
                float hlW = bMax.X - bMin.X;
                float hlInset = btnChamfer * 0.3f;
                uint hlEdge = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f));
                uint hlMid  = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, topHighlightA));
                drawList.AddRectFilledMultiColor(
                    new Vector2(bMin.X + hlInset,            bMin.Y),
                    new Vector2(bMin.X + hlW * 0.5f,         bMin.Y + 1f * totalScale),
                    hlEdge, hlMid, hlMid, hlEdge);
                drawList.AddRectFilledMultiColor(
                    new Vector2(bMin.X + hlW * 0.5f,         bMin.Y),
                    new Vector2(bMax.X - hlInset,            bMin.Y + 1f * totalScale),
                    hlMid, hlEdge, hlEdge, hlMid);
            }

            // Press inner-top shadow (scales with pressAmount)
            if (pressShadowAlpha > 0.001f)
            {
                float shadowH = 4f * totalScale;
                uint shBlack = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, pressShadowAlpha));
                uint shClear = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0f));
                drawList.PushClipRect(bMin, bMax, true);
                drawList.AddRectFilledMultiColor(
                    new Vector2(bMin.X + btnChamfer * 0.3f, bMin.Y),
                    new Vector2(bMax.X - btnChamfer * 0.3f, bMin.Y + shadowH),
                    shBlack, shBlack, shClear, shClear);
                drawList.PopClipRect();
            }

            // ── Button label ──
            float labelAlpha = buttonEnabled ? 1.0f : 0.55f;
            uint btnTextCol = ImGui.GetColorU32(new Vector4(0.10f, 0.08f, 0.0f, labelAlpha));
            drawList.AddText(
                new Vector2(bMin.X + (btnW - btnTextSize.X) * 0.5f,
                            (bMin.Y + bMax.Y) * 0.5f - fontH * 0.5f),
                btnTextCol, btnText);

            // ── Sheen sweep (HOVER only, 700ms one-shot on hover-enter). ──
            // Suppressed during press/release so it doesn't overlap the
            // physical-feel effects.
            if (hovered && !active && pressAmount < 0.1f && markBtnHoverStart >= 0f)
            {
                const float sheenDuration = 0.70f;
                float sheenElapsed = (float)ImGui.GetTime() - markBtnHoverStart;
                if (sheenElapsed < sheenDuration)
                {
                    float sp = sheenElapsed / sheenDuration;
                    sp = 1f - MathF.Pow(1f - sp, 3f); // ease-out cubic
                    float bandW = btnW * 0.45f;
                    float sx = bMin.X - bandW + sp * (btnW + bandW * 2f);
                    float aEnv = sp < 0.15f ? sp / 0.15f
                               : sp > 0.85f ? (1f - sp) / 0.15f
                               : 1f;
                    aEnv = Math.Clamp(aEnv, 0f, 1f) * 0.38f;

                    drawList.PushClipRect(bMin, bMax, true);
                    uint edgeU = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f));
                    uint midU  = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, aEnv));
                    float midX = sx + bandW * 0.5f;
                    drawList.AddRectFilledMultiColor(
                        new Vector2(sx, bMin.Y), new Vector2(midX, bMax.Y),
                        edgeU, midU, midU, edgeU);
                    drawList.AddRectFilledMultiColor(
                        new Vector2(midX, bMin.Y), new Vector2(sx + bandW, bMax.Y),
                        midU, edgeU, edgeU, midU);
                    drawList.PopClipRect();
                }
            }

            // ── Corner brackets (blended alpha) ──
            if (buttonEnabled)
            {
                float bracketSize = 5f * totalScale;
                float bracketInset = 5f * totalScale;
                float fromChamfer = 14f * totalScale;
                uint bracketCol = ImGui.GetColorU32(new Vector4(0.10f, 0.08f, 0.0f, bracketAlpha));

                var trAnchor = new Vector2(bMax.X - fromChamfer, bMin.Y + bracketInset);
                drawList.AddLine(new Vector2(trAnchor.X - bracketSize, trAnchor.Y), trAnchor, bracketCol, 1f);
                drawList.AddLine(trAnchor, new Vector2(trAnchor.X, trAnchor.Y + bracketSize), bracketCol, 1f);

                var blAnchor = new Vector2(bMin.X + fromChamfer, bMax.Y - bracketInset);
                drawList.AddLine(blAnchor, new Vector2(blAnchor.X + bracketSize, blAnchor.Y), bracketCol, 1f);
                drawList.AddLine(blAnchor, new Vector2(blAnchor.X, blAnchor.Y - bracketSize), bracketCol, 1f);
            }

            // Tooltip on disabled hover (matches existing wording)
            if (!buttonEnabled && hovered)
            {
                if (!hasScrolledToEnd)
                    ImGui.SetTooltip("Read through the new features first! There's a lot!");
                else if (!hasAcknowledgedNSFW)
                    ImGui.SetTooltip("Please acknowledge the content warning above");
            }

            // Handle click: fire the firework spark, then defer the actual close by ~0.55s
            // so the particle burst has time to be seen before the window dismisses.
            if (clicked && buttonEnabled && !pendingMarkAsReadClose)
            {
                var sparkCentre = new Vector2(
                    (btnRectMin.X + btnRectMax.X) * 0.5f,
                    stripCenterY);
                markAsReadSpark.Trigger(sparkCentre, isFavorited: true, plugin.Configuration);
                pendingMarkAsReadClose = true;
                pendingMarkAsReadAt = DateTime.Now;
            }

            // Advance the ImGui cursor past the strip
            ImGui.SetCursorScreenPos(new Vector2(stripStart.X, stripStart.Y + stripH));
        }

        private void DrawDebugInfo()
        {
            ImGui.Spacing();
            ImGui.Text($"Scroll Debug Info:");

            // Get the scroll values from the child window
            if (ImGui.BeginChild("PatchNotesScroll", Vector2.Zero, false))
            {
                float currentScrollY = ImGui.GetScrollY();
                float maxScrollY = ImGui.GetScrollMaxY();
                ImGui.EndChild();

                ImGui.Text($"Current: {currentScrollY:F1}, Max: {maxScrollY:F1}");
                ImGui.Text($"Progress: {(maxScrollY > 0 ? (currentScrollY / maxScrollY * 100) : 0):F1}%");
                ImGui.Text($"hasScrolledToEnd: {hasScrolledToEnd}");
                ImGui.Text($"85% threshold: {maxScrollY * 0.85f:F1}");
            }
        }

        private void DrawParticleEffects(ImDrawListPtr drawList, Vector2 bannerStart, Vector2 bannerSize)
        {
            float deltaTime = ImGui.GetIO().DeltaTime;
            particleTimer += deltaTime;

            if (particleTimer > 0.15f && particles.Count < 40)
            {
                SpawnParticle(bannerStart, bannerSize);
                particleTimer = 0f;
            }

            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var particle = particles[i];

                particle.Position += particle.Velocity * deltaTime;
                particle.Life -= deltaTime;

                if (particle.Life <= 0 ||
                    particle.Position.X > bannerStart.X + bannerSize.X + 50 ||
                    particle.Position.Y < bannerStart.Y - 50 ||
                    particle.Position.Y > bannerStart.Y + bannerSize.Y + 50)
                {
                    particles.RemoveAt(i);
                    continue;
                }

                float alpha = Math.Min(1f, particle.Life / particle.MaxLife);
                var color = new Vector4(particle.Color.X, particle.Color.Y, particle.Color.Z, particle.Color.W * alpha);

                drawList.AddCircleFilled(
                    particle.Position,
                    particle.Size,
                    ImGui.GetColorU32(color)
                );

                if (alpha > 0.3f)
                {
                    var glowColor = new Vector4(color.X, color.Y, color.Z, color.W * 0.15f);
                    drawList.AddCircleFilled(
                        particle.Position,
                        particle.Size * 2.5f,
                        ImGui.GetColorU32(glowColor)
                    );
                }

                particles[i] = particle;
            }
        }

        /// <summary>
        /// Streak around the button's hex silhouette + trailing particles.
        /// `hoverElapsed` is per-element so each fresh hover starts at TL.
        /// </summary>
        private static void DrawButtonSilhouetteStreak(
            ImDrawListPtr dl, Vector2 rectMin, Vector2 rectMax, float capW, float centerY,
            Vector3 accent, float scale, float hoverElapsed)
        {
            const float streakPeriod = 3.2f;
            const float streakLengthFraction = 0.28f;
            const int streakSegments = 40;
            float streakHead = (hoverElapsed / streakPeriod) % 1f;
            float stepFraction = streakLengthFraction / streakSegments;

            // Streak body: fading tail segments behind the leading head
            for (int seg = 0; seg < streakSegments; seg++)
            {
                float pos1 = (streakHead - seg * stepFraction + 1f) % 1f;
                float pos2 = (streakHead - (seg + 1) * stepFraction + 1f) % 1f;
                var p1 = WalkButtonSilhouette(pos1, rectMin, rectMax, capW, centerY);
                var p2 = WalkButtonSilhouette(pos2, rectMin, rectMax, capW, centerY);

                float t = seg / (float)streakSegments;
                float segAlpha = (1f - t) * (1f - t) * 0.85f;
                float thickness = (1f - t * 0.45f) * 2.2f * scale;
                dl.AddLine(p1, p2,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, segAlpha)), thickness);
            }

            // Bright head dot + soft halo
            var headPt = WalkButtonSilhouette(streakHead, rectMin, rectMax, capW, centerY);
            var headCore = new Vector4(
                accent.X + (1f - accent.X) * 0.5f,
                accent.Y + (1f - accent.Y) * 0.5f,
                accent.Z + (1f - accent.Z) * 0.5f,
                0.95f);
            dl.AddCircleFilled(headPt, 5f * scale,
                ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, 0.22f)));
            dl.AddCircleFilled(headPt, 2.5f * scale,
                ImGui.ColorConvertFloat4ToU32(headCore));

            // Trailing particles from the head, phase-staggered to keep the trail continuous
            var anchor = new Vector2((rectMin.X + rectMax.X) * 0.5f, centerY);
            int particleCount = 6;
            float maxDrift = 10f;
            float lifetime = 0.45f;

            for (int i = 0; i < particleCount; i++)
            {
                float phaseOffset = i / (float)particleCount;
                float lifeProgress = ((hoverElapsed / lifetime) + phaseOffset) % 1f;
                float age = lifeProgress * lifetime;
                if (hoverElapsed - age < 0f) continue; // suppress phantom trail at hover-start

                float spawnFrac = (float)((((hoverElapsed - age) / streakPeriod) % 1.0 + 1.0) % 1.0);
                var spawnPt = WalkButtonSilhouette(spawnFrac, rectMin, rectMax, capW, centerY);

                var outward = spawnPt - anchor;
                float outLen = outward.Length();
                if (outLen > 0.01f) outward /= outLen;
                else outward = new Vector2(1, 0);

                float angleVar = (i * 137.5f * MathF.PI / 180f + hoverElapsed * 0.5f) % MathF.Tau;
                var perp = new Vector2(-outward.Y, outward.X);
                var driftDir = outward + perp * MathF.Sin(angleVar) * 0.5f;
                float dLen = driftDir.Length();
                if (dLen > 0.001f) driftDir /= dLen;

                float eased = 1f - (1f - lifeProgress) * (1f - lifeProgress);
                var pos = spawnPt + driftDir * eased * maxDrift * scale;

                float pAlpha = (1f - lifeProgress) * (1f - lifeProgress) * 0.85f;
                float pR = (1.8f - lifeProgress * 0.6f) * scale;
                dl.AddCircleFilled(pos, pR,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, pAlpha)));
            }
        }

        /// <summary>
        /// Maps 0..1 progress to a point walking clockwise around the button's hex silhouette:
        /// A (top-left body) -> B (top-right body) -> C (right cap tip) -> D (bottom-right body)
        /// -> E (bottom-left body) -> F (left cap tip) -> back to A. Uses perimeter-length-weighted
        /// segments so the streak moves at a visually constant speed regardless of cap size.
        /// </summary>
        private static Vector2 WalkButtonSilhouette(float progress, Vector2 rectMin, Vector2 rectMax, float capW, float centerY)
        {
            float btnW = rectMax.X - rectMin.X;
            float btnH = rectMax.Y - rectMin.Y;
            float capDiag = MathF.Sqrt(capW * capW + (btnH * 0.5f) * (btnH * 0.5f));

            // Segment lengths, clockwise from A
            float l1 = btnW;       // A -> B (top of body)
            float l2 = capDiag;    // B -> C (upper slope of right cap)
            float l3 = capDiag;    // C -> D (lower slope of right cap)
            float l4 = btnW;       // D -> E (bottom of body, right-to-left)
            float l5 = capDiag;    // E -> F (lower slope of left cap)
            float l6 = capDiag;    // F -> A (upper slope of left cap)
            float total = l1 + l2 + l3 + l4 + l5 + l6;

            var A = new Vector2(rectMin.X, rectMin.Y);
            var B = new Vector2(rectMax.X, rectMin.Y);
            var C = new Vector2(rectMax.X + capW, centerY);
            var D = new Vector2(rectMax.X, rectMax.Y);
            var E = new Vector2(rectMin.X, rectMax.Y);
            var F = new Vector2(rectMin.X - capW, centerY);

            float d = progress * total;
            if (d < l1) return Vector2.Lerp(A, B, d / l1);
            d -= l1;
            if (d < l2) return Vector2.Lerp(B, C, d / l2);
            d -= l2;
            if (d < l3) return Vector2.Lerp(C, D, d / l3);
            d -= l3;
            if (d < l4) return Vector2.Lerp(D, E, d / l4);
            d -= l4;
            if (d < l5) return Vector2.Lerp(E, F, d / l5);
            d -= l5;
            return Vector2.Lerp(F, A, d / l6);
        }

        private void SpawnParticle(Vector2 bannerStart, Vector2 bannerSize)
        {
            var particle = new Particle
            {
                Position = new Vector2(
                    bannerStart.X + (float)particleRandom.NextDouble() * bannerSize.X,
                    bannerStart.Y + (float)particleRandom.NextDouble() * bannerSize.Y
                ),

                Velocity = new Vector2(
                    -10f + (float)particleRandom.NextDouble() * 20f,
                    -15f + (float)particleRandom.NextDouble() * -10f
                ),

                MaxLife = 6f + (float)particleRandom.NextDouble() * 4f,
                Size = 1.5f + (float)particleRandom.NextDouble() * 2.5f,

                Color = particleRandom.Next(5) switch
                {
                    0 => new Vector4(1.0f, 1.0f, 1.0f, 0.8f),
                    1 => new Vector4(0.9f, 0.95f, 1.0f, 0.7f),
                    2 => new Vector4(0.8f, 0.9f, 1.0f, 0.6f),
                    3 => new Vector4(0.95f, 0.95f, 0.95f, 0.7f),
                    _ => new Vector4(0.85f, 0.92f, 1.0f, 0.6f)
                }
            };

            particle.Life = particle.MaxLife;
            particles.Add(particle);
        }


    }
}

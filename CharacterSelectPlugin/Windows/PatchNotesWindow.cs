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

namespace CharacterSelectPlugin.Windows
{
    public class PatchNotesWindow : Window
    {
        private readonly Plugin plugin;
        private bool hasScrolledToEnd = false;
        private bool hasAcknowledgedNSFW = false;
        private bool wasOpen = false;
        public bool OpenMainMenuOnClose = false;

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
        private static readonly bool ShowFeatureSpotlight = true;

        private struct FeatureCard
        {
            public FontAwesomeIcon Icon;
            public string Title;
            public string Description;
            public string ActionLabel;
            public Action OnClick;
            public string ImagePath;
        }

        public PatchNotesWindow(Plugin plugin) : base("Character Select+ – What's New?",
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar)
        {
            this.plugin = plugin;
            IsOpen = false;

            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(800, 650),
                MaximumSize = new Vector2(800, 650)
            };
        }

        public override void Draw()
        {
            var totalScale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;

            if (IsOpen && !wasOpen)
            {
                hasScrolledToEnd = false;
                hasAcknowledgedNSFW = false;
            }
            wasOpen = IsOpen;

            ImGui.SetNextWindowSize(new Vector2(800 * totalScale, 650 * totalScale), ImGuiCond.Always);

            // Always use default theme for Patch Notes - consistent first impression
            int themeColorCount = ThemeHelper.PushDefaultThemeColors();
            int themeStyleVarCount = ThemeHelper.PushThemeStyleVars(plugin.Configuration.UIScaleMultiplier);

            try
            {
                DrawModernHeader(totalScale);
                DrawPatchNotes(totalScale);
                DrawBottomButton(totalScale);
            }
            finally
            {
                ThemeHelper.PopThemeStyleVars(themeStyleVarCount);
                ThemeHelper.PopThemeColors(themeColorCount);
            }
        }

        private void DrawModernHeader(float totalScale)
        {
            var windowPos = ImGui.GetWindowPos();
            var windowPadding = ImGui.GetStyle().WindowPadding;

            var headerWidth = (800f * totalScale) - (windowPadding.X * 2);
            var headerHeight = 180f * totalScale;

            var headerStart = windowPos + new Vector2(windowPadding.X, windowPadding.Y * 1.1f);
            var headerEnd = headerStart + new Vector2(headerWidth, headerHeight);

            var drawList = ImGui.GetWindowDrawList();

            try
            {
                string pluginDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "";
                string assetsPath = Path.Combine(pluginDirectory, "Assets");
                string imagePath = Path.Combine(assetsPath, "NewBanner.png");

                if (File.Exists(imagePath))
                {
                    var texture = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
                    if (texture != null)
                    {
                        var imageAspect = (float)texture.Width / texture.Height;
                        var scaledWidth = headerWidth;
                        var scaledHeight = scaledWidth / imageAspect;

                        var imagePos = headerStart;
                        drawList.AddImage((ImTextureID)texture.Handle, imagePos, imagePos + new Vector2(scaledWidth, scaledHeight));
                        DrawParticleEffects(drawList, headerStart, new Vector2(scaledWidth, scaledHeight));
                    }
                    else
                    {
                        DrawGradientBackground(headerStart, headerEnd);
                        DrawParticleEffects(drawList, headerStart, new Vector2(headerWidth, headerHeight));
                    }
                }
                else
                {
                    DrawGradientBackground(headerStart, headerEnd);
                    DrawParticleEffects(drawList, headerStart, new Vector2(headerWidth, headerHeight));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to load banner image: {ex.Message}");
                DrawGradientBackground(headerStart, headerEnd);
                DrawParticleEffects(drawList, headerStart, new Vector2(headerWidth, headerHeight));
            }

            ImGui.SetCursorPosY((windowPadding.Y * 0.5f) + headerHeight - 10);

            ImGui.SetCursorPosX(9);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1.0f), "\uf005"); // Green star
            ImGui.PopFont();
            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.9f, 0.4f, 1.0f)); // Green text
            ImGui.Text($"New in v{Plugin.CurrentPluginVersion}");
            ImGui.PopStyleColor();

            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 10);
            ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.85f, 1.0f), "Name Sync, Expanded RP Profiles, Custom Themes, and more");

            ImGui.Separator();
            ImGui.Spacing();
        }

        private void DrawGradientBackground(Vector2 headerStart, Vector2 headerEnd)
        {
            var drawList = ImGui.GetWindowDrawList();
            uint gradientTop = ImGui.GetColorU32(new Vector4(0.2f, 0.4f, 0.8f, 0.15f));
            uint gradientBottom = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.2f, 0.05f));
            drawList.AddRectFilledMultiColor(headerStart, headerEnd, gradientTop, gradientTop, gradientBottom, gradientBottom);

            ImGui.SetCursorPos(new Vector2(20, 15));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.95f, 0.95f, 1.0f));
            ImGui.Text("Character Select+ – What's New?");
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
            ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.85f, 1.0f), "Name Sync, Expanded RP Profiles, Custom Themes, and more");
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
            ImGui.BeginChild("PatchNotesScroll", new Vector2(0, -70 * totalScale), false, ImGuiWindowFlags.AlwaysVerticalScrollbar);

            float currentScrollY = ImGui.GetScrollY();
            float maxScrollY = ImGui.GetScrollMaxY();

            if (maxScrollY > 0 && currentScrollY >= (maxScrollY * 0.85f))
                hasScrolledToEnd = true;

            if (ShowFeatureSpotlight)
            {
                DrawFeatureSpotlight(totalScale);
            }

            ImGui.PushTextWrapPos();

            // Latest Patch Notes - v2.1.0.0
            if (DrawModernCollapsingHeader("v2.1.0.0 – Name Sync, Expanded RP Profiles, Custom Themes, and more", new Vector4(0.4f, 0.9f, 0.4f, 1.0f), true))
            {
                Draw210Notes();

                // Show scroll indicator if haven't scrolled enough
                if (!hasScrolledToEnd)
                {
                    ImGui.Spacing();
                    ImGui.Spacing();
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.8f, 0.8f));
                    ImGui.Text("↓ Scroll down to see all features before continuing ↓");
                    ImGui.PopStyleColor();
                    ImGui.Spacing();
                }
            }

            // Previous Patch Notes - v2.0.1.4
            if (DrawModernCollapsingHeader("v2.0.1.4 – 7.4 Compatibility, Mod Manager, Character Assignments", new Vector4(0.75f, 0.75f, 0.85f, 1.0f), false))
            {
                Draw214Notes();
            }

            // Previous Patch Notes - v2.0.1.0
            if (DrawModernCollapsingHeader("v2.0.1.0 – Conflict Resolution, IPC, Apply to Target (GPose)", new Vector4(0.75f, 0.75f, 0.85f, 1.0f), false))
            {
                Draw201Notes();
            }

            // Previous Patch Notes - v2.0.0.0
            if (DrawModernCollapsingHeader("v2.0.0.0 – Character Gallery & Visual Overhaul", new Vector4(0.75f, 0.75f, 0.85f, 1.0f), false))
            {
                Draw120Notes();
            }

            // Previous Patch Notes - v1.1
            if (DrawModernCollapsingHeader("v1.1.0.8 - v1.1.1.2 – April 18 2025", new Vector4(0.75f, 0.75f, 0.85f, 1.0f), false))
            {
                Draw110Notes();
            }

            // Previous Patch Notes - v1.1.0.0-7
            if (DrawModernCollapsingHeader("v1.1.0.(0-7) – April 09 2025", new Vector4(0.75f, 0.75f, 0.85f, 1.0f), false))
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

        private void DrawFeatureSection(string icon, string title, Vector4 accentColor)
        {
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

        private void Draw210Notes()
        {
            // Name Sync
            DrawFeatureSection("\uf007", "Name Sync", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Show your CS+ character's name instead of your in-game name across the UI");
            ImGui.BulletText("Your name appears in nameplates with an animated wave glow effect in your chosen colour");
            ImGui.BulletText("Works in chat messages -- your CS+ name shows as the sender for tells, party, FC, and more");
            ImGui.BulletText("The party list displays your CS+ name");
            ImGui.BulletText("Target bar shows your CS+ name, including when you're someone's target-of-target");
            ImGui.BulletText("Optional: Hide your Free Company tag from your nameplate");
            ImGui.BulletText("Glow colour is based on your CS+ Character's nameplate colour to make your name stand out");
            ImGui.BulletText("Shared Name Sync: See other CS+ users' custom names");
            ImGui.BulletText("Privacy-first: Both you AND other users must opt-in to see each other's names");
            ImGui.Spacing();

            // Expanded RP Profiles
            DrawFeatureSection("\uf2c2", "Expanded RP Profiles", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            ImGui.BulletText("Expand your RP profile with custom sections tailored to your character");
            ImGui.BulletText("Add personality traits, backstory snippets, RP hooks, likes/dislikes, and more");
            ImGui.BulletText("10 different section layouts: lists, quotes, timelines, pros/cons, key-value pairs, and grids");
            ImGui.BulletText("Drag and drop to rearrange your sections exactly how you want them");
            ImGui.BulletText("Connections system: Link to your own alt characters or other players you RP with");
            ImGui.BulletText("Image galleries: Add multiple images to your profile with a built-in viewer");
            ImGui.BulletText("Add a title and status under your name with icon support (e.g., 'The Wandering Bard')");
            ImGui.Spacing();

            // Custom Themes
            DrawFeatureSection("\uf53f", "Custom Themes", new Vector4(0.9f, 0.7f, 0.2f, 1.0f));
            ImGui.BulletText("Personalize every part of your CS+ window - make it truly yours");
            ImGui.BulletText("Customize colours for backgrounds, buttons, headers, tabs, text, scrollbars, and more");
            ImGui.BulletText("Add a custom background image to your main window with opacity and positioning controls");
            ImGui.BulletText("Zoom and pan your background image to frame it perfectly");
            ImGui.BulletText("Choose a custom icon from 200+ options across 10 categories");
            ImGui.BulletText("Card glow: Use each character's nameplate colour or set a single theme colour");
            ImGui.BulletText("Save your favourite looks as presets and switch between them instantly");
            ImGui.BulletText("Right-click icons to add them to your Favourites tab for quick access");
            ImGui.Spacing();

            // Job Assignments (Job → Character)
            DrawFeatureSection("\uf0ec", "Job Assignments (Job → Character)", new Vector4(0.5f, 0.8f, 0.9f, 1.0f));
            ImGui.BulletText("Assign a character or design to each job or role");
            ImGui.BulletText("Automatically switches character/design when you change jobs");
            ImGui.BulletText("Supports both individual jobs and role-based assignments");
            ImGui.BulletText("Enable in Settings → Job Assignments");
            ImGui.Spacing();

            // Gearset Assignments (Character → Job)
            DrawFeatureSection("\uf553", "Job Assignments (Character → Job)", new Vector4(0.9f, 0.7f, 0.4f, 1.0f));
            ImGui.BulletText("Assign a job to any character or design");
            ImGui.BulletText("Automatically switches to the assigned job when applying");
            ImGui.BulletText("Design-level job overrides character-level setting");
            ImGui.BulletText("Enable in Settings → Job Assignments → Enable Gearset Assignments");
            ImGui.Spacing();

            // Improved Immersive Dialogue
            DrawFeatureSection("\uf075", "Improved Immersive Dialogue", new Vector4(0.6f, 0.9f, 0.8f, 1.0f));
            ImGui.BulletText("Better detection and replacement of player pronouns in NPC dialogue");
            ImGui.BulletText("Improved handling of gendered titles (sir/madam, lord/lady, etc.)");
            ImGui.BulletText("Fixed edge cases where replacements would accidentally affect the chat window");
            ImGui.BulletText("Properly uses your Character's First and Last names.");
            ImGui.Spacing();

            // Honorific Glow Support
            DrawFeatureSection("\uf521", "Honorific Support", new Vector4(0.9f, 0.8f, 0.4f, 1.0f));
            ImGui.BulletText("Added glow colour support for Honorific plugin titles");
            ImGui.BulletText("Configure both title colour AND glow colour per-character");
            ImGui.BulletText("Works with Honorific's existing gradient and animation systems");
            ImGui.BulletText("Enable in Settings → Honorific");
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.75f, 1.0f));
            ImGui.TextWrapped("Honorific is maintained by Caraxi - consider supporting their work!");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            // Idle Pose Validation
            DrawFeatureSection("\uf21e", "Idle Pose Indicator", new Vector4(0.6f, 0.8f, 1.0f, 1.0f));
            ImGui.BulletText("See your current idle pose number in the header, or use /select idle to check via command");
            ImGui.BulletText("Added validation to prevent invalid pose numbers from causing issues");
            ImGui.BulletText("More reliable pose restoration when logging in");
            ImGui.BulletText("Better error handling when poses fail to apply");
            ImGui.Spacing();

            // Mod Deletion Warning
            DrawFeatureSection("\uf071", "Mod Deletion Warning", new Vector4(1.0f, 0.7f, 0.3f, 1.0f));
            ImGui.BulletText("CS+ now warns you when a mod is deleted that was used in a Character or Design");
            ImGui.BulletText("Shows which characters and designs are affected so you can update them");
            ImGui.BulletText("Helps prevent broken Conflict Resolution configurations from deleted mods");
            ImGui.Spacing();

            // Bug Fixes
            DrawFeatureSection("\uf188", "QoL & Bug Fixes", new Vector4(0.9f, 0.4f, 0.4f, 1.0f));
            ImGui.BulletText("Fixed duplicate chat messages appearing when using certain features");
            ImGui.BulletText("Fixed Advanced Mode macro settings resetting unexpectedly");
            ImGui.BulletText("Added an option to remember open/close state of the Main Window in Settings  → Behavior");
            ImGui.BulletText("Toggles for: View RP Profile, Report CS+ Name, Block CS+ User to appear in Context Menus. Found in Settings  → Behavior");
            ImGui.Spacing();
        }

        private void Draw214Notes()
        {
            // 7.4 Compatibility Update
            DrawFeatureSection("\uf021", "7.4 Compatibility Update", new Vector4(0.6f, 0.8f, 1.0f, 1.0f));
            ImGui.BulletText("Updated for Final Fantasy XIV patch 7.4");
            ImGui.Spacing();

            // Design Panel Enhancements
            DrawFeatureSection("\uf1fc", "Design Panel Enhancements", new Vector4(0.9f, 0.7f, 0.9f, 1.0f));
            ImGui.BulletText("Design Previews now show in Quick Character Switch for easier design selection");
            ImGui.BulletText("Active design is now highlighted in green in the design list");
            ImGui.BulletText("Added save button to Design's Advanced Mode window for easier workflow");
            ImGui.BulletText("Update CR for Existing Designs feature for hair/gear changes (other changes still need manual editing)");
            ImGui.Spacing();

            // Mod Manager Improvements
            DrawFeatureSection("\uf085", "Mod Manager Improvements", new Vector4(0.9f, 0.7f, 0.2f, 1.0f));
            ImGui.BulletText("Standalone Mod Manager window for better organization (use '/select mods' command)");
            ImGui.BulletText("Global Search functionality to search across all mod categories simultaneously");
            ImGui.BulletText("'Currently Affecting You' section now shows: Tattoos, Eyes, Ears/Tail/Horns, Makeup/Face Paint");
            ImGui.Spacing();

            // Auto-Apply Last Used Design on Login
            DrawFeatureSection("\uf4fc", "Auto-Apply Last Used Design on Login", new Vector4(0.6f, 0.9f, 0.8f, 1.0f));
            ImGui.BulletText("New setting that works with 'Auto-Apply Last Used Character on Login'");
            ImGui.BulletText("When enabled, also automatically applies the last design you used for that character");
            ImGui.BulletText("Perfect for maintaining your complete look when logging back in");
            ImGui.BulletText("Appears as a sub-option when character auto-apply is enabled");
            ImGui.Spacing();

            // Winter/Christmas Theme
            DrawFeatureSection("\uf2dc", "Winter/Christmas Theme & Holiday Update", new Vector4(0.9f, 0.95f, 1.0f, 1.0f));
            ImGui.BulletText("Winter and Christmas themes added");
            ImGui.BulletText("Users can now freely choose which theme from the available list");
            ImGui.Spacing();

            // Apply to Target - GPose Support
            DrawFeatureSection("\uf140", "Apply to Target QoL", new Vector4(0.6f, 1.0f, 0.8f, 1.0f));
            ImGui.BulletText("You can now use the Quick Character Switch window to Apply to Target by Right Clicking the Apply button");
            ImGui.BulletText("You can now also CTRL+Right Click Apply to restore dropdowns back to your current Character + Design");
            ImGui.Spacing();

            // Bug Fixes
            DrawFeatureSection("\uf188", "Bug Fixes", new Vector4(0.9f, 0.4f, 0.4f, 1.0f));
            ImGui.BulletText("Fixed Character Assignments not working properly (for real this time)");
            ImGui.BulletText("Fixed Reapply on Job Change not working when using Character Assignments");
            ImGui.Spacing();
        }

        private void Draw201Notes()
        {
            // Conflict Resolution System
            DrawFeatureSection("\uf071", "Mod Conflict Resolution System", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            ImGui.BulletText("Eliminates mod conflicts between Character designs automatically when switching between them");
            ImGui.BulletText("Save complete mod configurations per design including enabled mods, mod settings, and option selections");
            ImGui.BulletText("Intelligent Mod Manager with 21+ categories (Hair, Gear, Bodies, VFX, Animations, etc.) for easy organization");
            ImGui.BulletText("Automatically categorizes and tracks mod additions, deletions, and changes -- no manual upkeep required");
            ImGui.BulletText("Optional opt-in feature available in CS+ settings when you're ready to explore advanced mod management");
            ImGui.Spacing();

            // Enhanced IPC API
            DrawFeatureSection("\uf0c1", "API / IPC", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("API endpoints for other plugins to integrate with CS+");
            ImGui.BulletText("Character switching, design management, and event notifications");
            ImGui.BulletText("Used internally for Conflict Resolution, improved Apply to Target functionality, and the Snapshot feature");
            ImGui.BulletText("Real-time character change events for plugin synchronization");
            ImGui.Spacing();

            // Apply to Target - GPose Support
            DrawFeatureSection("\uf140", "Apply to Target - GPose Support", new Vector4(0.6f, 1.0f, 0.8f, 1.0f));
            ImGui.BulletText("Fixed Apply to Target functionality to work properly in GPose");
            ImGui.BulletText("Converted from previous macro-based to new IPC-based system");
            ImGui.BulletText("More reliable character application to targeted players");
            ImGui.Spacing();

            // Snapshot
            DrawFeatureSection("\uf030", "Snapshot Feature", new Vector4(0.9f, 0.7f, 1.0f, 1.0f));
            ImGui.BulletText("New Snapshot feature - one-click add Design to Character Select+");
            ImGui.BulletText("Use after saving a Design in Glamourer and setting up your Customize+ Profile");
            ImGui.BulletText("Instantly adds your current look as a Design to the active Character in CS+");
            ImGui.BulletText("Includes your current Customize+ Profile automatically");
            ImGui.BulletText("CR mode: Auto-configures mods for your current outfit when using Conflict Resolution");
            ImGui.BulletText("Simple workflow: Click camera button in Design Panel or use chat command");
            ImGui.BulletText("Chat command: /select save - optionally add CR for Conflict Resolution mode");
            ImGui.Spacing();

            // UI Scaling
            DrawFeatureSection("\uf00e", "UI Scaling Done Right", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Character Select+ is now properly responsive to the user's resolution and Dalamud's Global Font Scaling.");
            ImGui.BulletText("Removed UI scaling options in Settings Panel.");
            ImGui.BulletText("Let me know if there are any issues using this.");
            ImGui.Spacing();

            // Penumbra Collection UI Sync
            DrawFeatureSection("\uf021", "Penumbra Collection Synchronization", new Vector4(0.8f, 0.9f, 0.6f, 1.0f));
            ImGui.BulletText("Switching characters now updates Penumbra's UI to show the correct collection");
            ImGui.BulletText("Seamless integration between CS+ character switching and Penumbra interface");
            ImGui.BulletText("Eliminates confusion about which collection is currently active");
            ImGui.Spacing();

            // Character Management Improvements
            DrawFeatureSection("\uf007", "Character Management Improvements", new Vector4(0.9f, 0.8f, 0.6f, 1.0f));
            ImGui.BulletText("Fixed Character Assignments -- can now edit and remove character assignments");
            ImGui.BulletText("Fixed Reorder Characters window -- changes now properly apply on save");
            ImGui.BulletText("Added duplicate character name prevention for your own characters");
            ImGui.Spacing();

            // Backup & Restore System
            DrawFeatureSection("\uf0c7", "Backup & Restore System", new Vector4(0.4f, 0.8f, 1.0f, 1.0f));
            ImGui.BulletText("Manual backup creation with optional custom naming");
            ImGui.BulletText("Configuration file import -- appears at top of backup list");
            ImGui.BulletText("Available Backups list with real-time backup count display");
            ImGui.BulletText("Individual restore functionality for any backup file");
            ImGui.BulletText("Automatic emergency backup creation before any restore operation");
            ImGui.Spacing();

            // Design Panel Enhancements
            DrawFeatureSection("\uf002", "Design Panel Enhancements", new Vector4(0.7f, 0.6f, 1.0f, 1.0f));
            ImGui.BulletText("Added search functionality to quickly find specific designs");
            ImGui.BulletText("New clipboard image pasting for Design Preview images");
            ImGui.Spacing();
        }

        private void Draw120Notes()
        {
            // Character Gallery (NEW!)
            DrawFeatureSection("\uf302", "Character Gallery", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            ImGui.BulletText("View and share your CS+ Characters with everyone else!");
            ImGui.BulletText("Opt-in feature - choose your main physical character to represent you");
            ImGui.BulletText("Shows recent activity status with green globe indicators");
            ImGui.BulletText("Like,favourite,add or even block other players' characters");
            ImGui.BulletText("Click any profile to view their full RP Profile with backgrounds & effects");
            ImGui.Spacing();

            // NSFW Content Management (NEW!)
            DrawFeatureSection("\uf06e", "NSFW Content Management", new Vector4(1.0f, 0.7f, 0.4f, 1.0f));
            ImGui.BulletText("RP Profile Editor now prompts you to mark profiles as NSFW if appropriate");
            ImGui.BulletText("Gallery setting to opt-in to viewing NSFW profiles (disabled by default)");
            ImGui.BulletText("Users must acknowledge they are 18+ to view NSFW content in the gallery");
            ImGui.Spacing();

            // Revamped RP Profiles
            DrawFeatureSection("\uf2c2", "Revamped RP Profiles", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Complete visual redesign with new layout and styling");
            ImGui.BulletText("80+ FFXIV location backgrounds to choose from");
            ImGui.BulletText("Animated visual effects: butterflies, fireflies, falling leaves, and more");
            ImGui.BulletText("Real-time preview - see changes instantly in the editor");
            ImGui.BulletText("Right-click any player name to view their RP Profile directly");
            ImGui.Spacing();

            // Immersive Dialogue (NEW!)
            DrawFeatureSection("\uf075", "Immersive Dialogue System", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            ImGui.BulletText("NPCs now use your CS+ Character's name, pronouns, and desired titles in dialogue!");
            ImGui.BulletText("Integration with he/him, she/her, and they/them pronouns");
            ImGui.BulletText("Granular settings: enable names, pronouns, gendered terms, or race separately");
            ImGui.BulletText("Customizable they/them neutral titles: friend, Mx., traveler, adventurer, or choose your own!");
            ImGui.BulletText("Only affects dialogue referring to your character - NPCs keep their own pronouns");
            ImGui.BulletText("Requires an active CS+ character with RP Profile pronouns set");
            ImGui.BulletText("If you find any instances in which it doesn't seem to be working please report them in the discord!");
            ImGui.Spacing();

            // Main Window UI Update
            DrawFeatureSection("\uf53f", "Main Window Visual Overhaul", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Complete redesign with compact layout and enhanced visuals");
            ImGui.BulletText("Character cards with integrated nameplates and action buttons");
            ImGui.BulletText("Glowing borders and enhanced hover effects");
            ImGui.BulletText("Optional setting for profiles to grow slightly on hover");
            ImGui.BulletText("Crown indicator for your designated Main Character");
            ImGui.BulletText("Resize Design Panel freely");
            ImGui.BulletText("Drag & Drop character reordering added to Main Window (leftward movement only due to ImGui limitations)");
            ImGui.Spacing();

            // Tutorial System (NEW!)
            DrawFeatureSection("\uf19d", "Interactive Tutorial System", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            ImGui.BulletText("Brand new guided tutorial for first-time users");
            ImGui.BulletText("Highlights and points to buttons and fields you need to interact with");
            ImGui.BulletText("Step-by-step guidance through Characters, Designs, and RP Profiles");
            ImGui.BulletText("Can be ended at any time if you prefer to explore on your own");
            ImGui.Spacing();

            // Design Preview Images (NEW!)
            DrawFeatureSection("\uf03e", "Design Preview Images", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Add custom preview images to your Designs");
            ImGui.BulletText("Preview images by hovering over the Apply (✓) button");
            ImGui.BulletText("Helps you quickly identify Designs at a glance");
            ImGui.Spacing();

            // Main Game Commands (NEW!)
            DrawFeatureSection("\uf120", "Base Game Command Support", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            ImGui.BulletText("Add base game commands through Advanced Mode");
            ImGui.BulletText("Example: Add '/gearset change 1' to switch jobs when applying Designs");
            ImGui.BulletText("Perfect combo with 'Reapply Last Design on Job Change' setting");
            ImGui.Spacing();

            // Random Character + Outfit (NEW!)
            DrawFeatureSection("\uf074", "Random Character & Outfit", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("New 'Random' button for spontaneous character switching");
            ImGui.BulletText("Randomly picks from your CS+ Characters and their Designs");
            ImGui.BulletText("Setting to limit random selection to only favourited items");
            ImGui.Spacing();

            // Main CS+ Character (NEW!)
            DrawFeatureSection("\uf521", "Main CS+ Character", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            ImGui.BulletText("Designate your main CS+ Character with a crown indicator");
            ImGui.BulletText("Crown display is optional - toggle in settings");
            ImGui.BulletText("'Reapply on Login' can be set to only apply your Main Character");
            ImGui.Spacing();

            // Character Assignments (NEW!)
            DrawFeatureSection("\uf0c1", "Character Assignments", new Vector4(0.6f, 1.0f, 0.8f, 1.0f));
            ImGui.BulletText("Assign specific CS+ Characters to specific in-game characters");
            ImGui.BulletText("Auto-apply designated CS+ characters when logging into assigned real characters");
            ImGui.BulletText("Dropdown selection from characters the plugin has seen before");
            ImGui.BulletText("Multiple real characters can share the same CS+ character");
            ImGui.BulletText("Takes priority over 'last used' system but respects Main Character Only Mode");
            ImGui.BulletText("Perfect for players with multiple alts who want consistent character setups");
            ImGui.Spacing();

            // Quick Character Switch Improvements
            DrawFeatureSection("\uf0e7", "Quick Character Switch Updates", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Now remembers your last used character like Apply on Login");
            ImGui.BulletText("Ready to go when you log in as that character");
            ImGui.BulletText("Will also switch to be on your current CS+ Character if applied through other methods");
            ImGui.Spacing();

            // Bug Fixes & QoL
            DrawFeatureSection("\uf085", "Bug Fixes & Quality of Life", new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
            ImGui.BulletText("Fixed Quick Switch window scroll issues");
            ImGui.BulletText("Disabled window docking to prevent UI conflicts");
            ImGui.BulletText("Added ghost images for drag and drop operations");
            ImGui.BulletText("Automatic character config backup on updates or every 7 days");
            ImGui.BulletText("Various performance improvements and optimizations");
        }

        private void Draw110Notes()
        {
            // Apply Character on Login
            DrawFeatureSection("\uf4fc", "Apply Character on Login", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("New opt-in setting in the plugin options.");
            ImGui.BulletText("Character Select+ will remember the last applied character.");
            ImGui.BulletText("Next time you log in, it will automatically apply that character.");
            ImGui.BulletText("⚠️ May conflict if you are using Glamourer Automations.");
            ImGui.Spacing();

            // Apply Appearance on Job Change
            DrawFeatureSection("\uf4fc", "Apply Appearance on Job Change", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("New opt-in setting in the plugin options.");
            ImGui.BulletText("Character Select+ will remember the last applied character and/or design.");
            ImGui.BulletText("When you switch between jobs, it will automatically apply that character/design.");
            ImGui.BulletText("⚠️ WILL 100 percent conflict if you are using Glamourer Automations.");
            ImGui.Spacing();

            // Designs
            DrawFeatureSection("\uf07b", "Design Panel Rework", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Buttons now only appear on hover, keeping the panel clean and focused.");
            ImGui.BulletText("Reorder designs by dragging the coloured handle‐bar on the left — click and drag to move.");
            ImGui.BulletText("Create new folders inline via the folder icon next to the + button, no extra windows needed.");
            ImGui.BulletText("Drag-and-drop designs into, out of, and between folders directly within the panel.");
            ImGui.BulletText("Right-click folders for inline Rename/Delete context menu, with instant application.");
            ImGui.Spacing();

            // Compact Quick Switch
            DrawFeatureSection("\uf0a0", "Compact Quick Character Switch", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Toggleable setting to hide the title bar and window frame for a slim bar.");
            ImGui.BulletText("Keeps dropdowns and apply button only, preserving full switch functionality.");
            ImGui.Spacing();

            // UI Scaling Option
            DrawFeatureSection("\uf00e", "UI Scale Setting", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("You can now adjust the plugin UI scale from the settings menu.");
            ImGui.BulletText("Great for users on high-resolution monitors or 4K displays.");
            ImGui.BulletText("Let me know if there are any issues using this.");
            ImGui.BulletText("⚠️ If your UI is fine as-is, best to leave this be.");
            ImGui.Spacing();
        }

        private void Draw1100Notes()
        {
            // RP Profile Panel
            DrawFeatureSection("\uf2c2", "RolePlay Profile Panel", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Add bios, pronouns, orientation, and more for each character.");
            ImGui.BulletText("Choose a unique image or reuse the character image.");
            ImGui.BulletText("Use pan and zoom controls to fine-tune the RP portrait.");
            ImGui.BulletText("Control visibility: keep private or share with others.");
            ImGui.BulletText("Once applied, that character's RP profile is active.");
            ImGui.BulletText("You can view others' profiles (if shared) and vice versa.");
            ImGui.BulletText("Use /viewrp self | /t | First Last@World to view.");
            ImGui.BulletText("Right-click in the party list, friends list, or chat to access shared RP cards.");
            ImGui.Spacing();

            // Glamourer Automations
            DrawFeatureSection("\uf5c3", "Glamourer Automations for Characters & Designs", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Characters & Designs can now trigger specific Glamourer Automation profiles.");
            ImGui.BulletText("This is *opt-in* — toggle it in plugin settings.");
            ImGui.BulletText("If no automation is assigned, the design defaults to 'None'.");
            ImGui.Spacing();
            ImGui.Text("To avoid errors, set up a 'None' automation:");
            ImGui.BulletText("1. Open Glamourer > Automations.");
            ImGui.BulletText("2. Create an Automation named 'None'.");
            ImGui.BulletText("3. Add your in-game character name beside 'Any World' then Set to Character.");
            ImGui.BulletText("4. That's it. Don't touch anything else, you're done!");
            ImGui.Spacing();

            // Customize+
            DrawFeatureSection("\uf234", "Customize+ Profiles for Designs", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Each design can now assign its own Customize+ profile.");
            ImGui.BulletText("This gives you finer control over visual changes per design.");
            ImGui.Spacing();

            // Manual Reordering
            DrawFeatureSection("\uf0b0", "Manual Character Reordering", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Use the 'Reorder Characters' button at the bottom-left.");
            ImGui.BulletText("Drag and drop profiles, then press Save to lock it in.");
            ImGui.Spacing();

            // Search
            DrawFeatureSection("\uf002", "Character Search Bar", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Click the magnifying glass to search by name instantly.");
            ImGui.Spacing();

            // Tagging
            DrawFeatureSection("\uf07b", "Tagging System", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Add comma-separated 'tags' to organize characters.");
            ImGui.BulletText("Click the filter icon to filter — characters can appear in multiple tags!");
            ImGui.Spacing();

            // Apply to Target
            DrawFeatureSection("\uf140", "Right-click → Apply to Target", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Right-click a character in Character Select+ with a target selected.");
            ImGui.BulletText("Apply their setup — or even one of their individual designs — to the target.");
            ImGui.Spacing();

            // Copy Designs
            DrawFeatureSection("\uf0c5", "Copy Designs Between Characters", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Hold Shift and click the '+' button in Designs to open the Design Importer.");
            ImGui.BulletText("Click the + beside a design to copy it. Repeat as needed!");
            ImGui.Spacing();

            // Other changes
            DrawFeatureSection("\uf085", "Other Changes", new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
            ImGui.BulletText("Older Design macros were automatically upgraded.");
            ImGui.BulletText("Various UI tweaks, bugfixes, and behind-the-scenes improvements.");
        }

        private void DrawBottomButton(float totalScale)
        {
            ImGui.Spacing();

            float windowWidth = ImGui.GetWindowSize().X;

            // NSFW acknowledgment checkbox (only shows after scrolling)
            if (hasScrolledToEnd)
            {
                ImGui.Spacing();

                // Center the checkbox area
                string checkboxText = "I understand that RP Profiles and CS+ Names may contain mature content";
                float checkboxWidth = ImGui.CalcTextSize(checkboxText).X + 30 * totalScale; // checkbox + text
                ImGui.SetCursorPosX((windowWidth - checkboxWidth) * 0.5f);

                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.75f, 0.5f, 1.0f)); // Warm warning colour
                ImGui.Checkbox(checkboxText, ref hasAcknowledgedNSFW);
                ImGui.PopStyleColor();

                ImGui.Spacing();
            }

            ImGui.Spacing();

            float buttonWidth = 90f * totalScale;
            ImGui.SetCursorPosX((windowWidth - buttonWidth) * 0.5f);

            bool buttonEnabled = hasScrolledToEnd && hasAcknowledgedNSFW;

            if (!buttonEnabled)
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);

            if (buttonEnabled)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.2f, 0.4f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.3f, 0.5f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.25f, 0.15f, 0.35f, 1f));
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
            }

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f * totalScale);

            bool buttonClicked = ImGui.Button("Got it!", new Vector2(buttonWidth, 30 * totalScale));

            if (!buttonEnabled && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                if (!hasScrolledToEnd)
                    ImGui.SetTooltip("Read through the new features first! There's a lot!");
                else if (!hasAcknowledgedNSFW)
                    ImGui.SetTooltip("Please acknowledge the content warning above");
            }

            if (buttonClicked && buttonEnabled)
            {
                plugin.Configuration.LastSeenVersion = Plugin.CurrentPluginVersion;
                plugin.Configuration.Save();
                IsOpen = false;
                if (OpenMainMenuOnClose)
                {
                    plugin.ToggleMainUI();
                }
                OpenMainMenuOnClose = false;
            }

            ImGui.PopStyleVar(!buttonEnabled ? 2 : 1);
            ImGui.PopStyleColor(3);
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

        // private void DrawWinterAnnouncementBox(float totalScale)
        // {
        //     // Winter/Christmas announcement as a child window/scrollable area
        //     ImGui.BeginChild("WinterAnnouncement", new Vector2(0, 120 * totalScale), true, ImGuiWindowFlags.None);
        //
        //     // Title with snowflake icons and date
        //     ImGui.PushFont(UiBuilder.IconFont);
        //     ImGui.TextColored(new Vector4(0.9f, 0.95f, 1.0f, 1.0f), "\uf2dc"); // FontAwesome snowflake
        //     ImGui.PopFont();
        //     ImGui.SameLine();
        //     ImGui.TextColored(new Vector4(0.2f, 0.8f, 1.0f, 1.0f), " Happy Holidays from Character Select+ ");
        //     ImGui.SameLine();
        //     ImGui.PushFont(UiBuilder.IconFont);
        //     ImGui.TextColored(new Vector4(0.9f, 0.95f, 1.0f, 1.0f), "\uf2dc"); // FontAwesome snowflake
        //     ImGui.PopFont();
        //
        //     ImGui.Spacing();
        //
        //     // Holiday message and thank you - conversational style
        //     ImGui.PushTextWrapPos();
        //     ImGui.TextColored(new Vector4(0.9f, 0.95f, 1.0f, 1.0f),
        //         "Season's greetings, adventurers! As we wrap up an amazing year, I wanted to take a moment to say thank you to everyone who has been enjoying Character Select+. " +
        //         "Your feedback, suggestions, and support have made this plugin what it is today. Whether you're creating new characters, perfecting your designs, or exploring the latest features, " +
        //         "you're the reason I love working on this project. Wishing you all a wonderful holiday season filled with joy, creativity, and fantastic adventures in FFXIV!");
        //     ImGui.PopTextWrapPos();
        //
        //     ImGui.EndChild();
        //
        //     ImGui.Spacing();
        // }

    }
}

using Dalamud.Interface.Windowing;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Bindings.ImGui;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Collections.Generic;
using Dalamud.Interface;

namespace CharacterSelectPlugin.Windows
{
    public class RPProfileViewWindow : Window
    {
        private readonly Plugin plugin;
        private Character? character;
        private RPProfile? externalProfile = null;
        private bool showingExternal = false;
        private bool imageDownloadStarted = false;
        private bool imageDownloadComplete = false;
        private string? downloadedImagePath = null;
        private IDalamudTextureWrap? cachedTexture;
        private string? cachedTexturePath;
        private bool bringToFront = false;
        private bool firstOpen = true;
        private float windowWidth = 420f;
        private float bioScrollY = 0f;
        private string? imagePreviewUrl = null;
        private bool showImagePreview = false;

        // Animation variables
        private float animationTime = 0f;
        private float[] circuitPacketPositions = new float[6];
        private float[] circuitPacketSpeeds = new float[6];
        private float[] circuitNodeGlowPhases = new float[12];
        private float[] particlePositions = new float[20];
        private float[] particleVelocitiesX = new float[20];
        private float[] particleVelocitiesY = new float[20];
        private float[] particleLifetimes = new float[20];
        private float[] gothicWispPositions = new float[8];
        private float[] gothicWispPhases = new float[8];
        private bool stylesPushed = false;

        public RPProfile? CurrentProfile => showingExternal ? externalProfile : character?.RPProfile;

        public RPProfileViewWindow(Plugin plugin)
            : base("RPProfileWindow",
                ImGuiWindowFlags.NoDecoration |
                ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoCollapse)
        {
            this.plugin = plugin;
            IsOpen = false;

            var dpiScale = ImGui.GetIO().DisplayFramebufferScale.X;
            var uiScale = plugin.Configuration.UIScaleMultiplier;
            var totalScale = GetSafeScale(dpiScale * uiScale);

            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(420 * totalScale, 550 * totalScale),
                MaximumSize = new Vector2(9999, 9999)
            };

            InitializeAnimationData();
        }

        private void InitializeAnimationData()
        {
            var random = new Random();

            // Circuit board animation
            for (int i = 0; i < circuitPacketPositions.Length; i++)
            {
                circuitPacketPositions[i] = random.NextSingle() * 400f;
                circuitPacketSpeeds[i] = 20f + random.NextSingle() * 40f;
            }
            for (int i = 0; i < circuitNodeGlowPhases.Length; i++)
            {
                circuitNodeGlowPhases[i] = random.NextSingle() * 6.28f;
            }

            // Nature particles
            for (int i = 0; i < particlePositions.Length; i++)
            {
                particlePositions[i] = random.NextSingle() * 400f;
                particleVelocitiesX[i] = (random.NextSingle() - 0.5f) * 20f;
                particleVelocitiesY[i] = random.NextSingle() * 30f + 10f;
                particleLifetimes[i] = random.NextSingle() * 5f;
            }

            // Wisps
            for (int i = 0; i < gothicWispPositions.Length; i++)
            {
                gothicWispPositions[i] = random.NextSingle() * 400f;
                gothicWispPhases[i] = random.NextSingle() * 6.28f;
            }
        }

        public void SetCharacter(Character character)
        {
            this.character = character;
            showingExternal = false;
            bringToFront = true;
            ResetImageCache();
        }

        public void SetExternalProfile(RPProfile profile)
        {
            externalProfile = profile;
            showingExternal = true;
            ResetImageCache();
            bringToFront = true;
        }

        private void ResetImageCache()
        {
            imageDownloadStarted = false;
            imageDownloadComplete = false;
            downloadedImagePath = null;
            cachedTexture?.Dispose();
            cachedTexture = null;
            cachedTexturePath = null;
        }

        public override void PreDraw()
        {
            stylesPushed = false;

            if (IsOpen && CurrentProfile != null)
            {
                ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
                stylesPushed = true;
            }

            if (IsOpen && bringToFront)
            {
                ImGui.SetNextWindowFocus();

                if (firstOpen)
                {
                    var center = ImGui.GetMainViewport().GetCenter();
                    ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
                    firstOpen = false;
                }

                bringToFront = false;
            }
        }

        public override void Draw()
        {
            var dpiScale = ImGui.GetIO().DisplayFramebufferScale.X;
            var uiScale = plugin.Configuration.UIScaleMultiplier;
            var totalScale = GetSafeScale(dpiScale * uiScale);

            var rp = CurrentProfile;
            if (rp == null)
            {
                if (stylesPushed)
                {
                    ImGui.PopStyleVar(2);
                    ImGui.PopStyleColor(2);
                    stylesPushed = false;
                }
                return;
            }

            var currentSize = ImGui.GetWindowSize();

            if (ImGui.IsWindowHovered() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var mousePos = ImGui.GetMousePos();
                var windowPos = ImGui.GetWindowPos();
                var windowSize = ImGui.GetWindowSize();

                var resizeAreaMin = windowPos + windowSize - new Vector2(20 * totalScale, 20 * totalScale);
                var resizeAreaMax = windowPos + windowSize;

                if (mousePos.X >= resizeAreaMin.X && mousePos.Y >= resizeAreaMin.Y &&
                    mousePos.X <= resizeAreaMax.X && mousePos.Y <= resizeAreaMax.Y)
                {
                    var baseAspect = 420f / 580f;
                    var newWidth = currentSize.X;
                    var newHeight = currentSize.Y;

                    if (newWidth / newHeight > baseAspect)
                    {
                        newWidth = newHeight * baseAspect;
                    }
                    else
                    {
                        newHeight = newWidth / baseAspect;
                    }

                    ImGui.SetWindowSize(new Vector2(newWidth, newHeight));
                }
            }

            if (!ImGui.Begin("RPProfileWindow",
                ImGuiWindowFlags.NoDecoration |
                ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoScrollbar))
            {
                ImGui.End();
                if (stylesPushed)
                {
                    ImGui.PopStyleVar(2);
                    ImGui.PopStyleColor(2);
                    stylesPushed = false;
                }
                return;
            }

            plugin.RPProfileViewWindowPos = ImGui.GetWindowPos();
            plugin.RPProfileViewWindowSize = ImGui.GetWindowSize();
            DrawProfileContent(rp, totalScale);
            DrawImagePreview(totalScale);
            ImGui.End();

            if (stylesPushed)
            {
                ImGui.PopStyleVar(2);
                ImGui.PopStyleColor(2);
                stylesPushed = false;
            }
        }
        private void DrawImagePreview(float scale)
        {
            if (!showImagePreview || string.IsNullOrEmpty(imagePreviewUrl))
                return;

            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.Pos);
            ImGui.SetNextWindowSize(viewport.Size);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0.9f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);

            if (ImGui.Begin("ImagePreview", ref showImagePreview,
                    ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
            {
                IDalamudTextureWrap? texture = null;

                if (File.Exists(imagePreviewUrl!))
                {
                    texture = Plugin.TextureProvider.GetFromFile(imagePreviewUrl!).GetWrapOrDefault();
                }

                if (texture != null)
                {
                    var windowSize = ImGui.GetWindowSize();
                    var imageSize = new Vector2(texture.Width, texture.Height);

                    float scaleX = windowSize.X * 0.9f / imageSize.X;
                    float scaleY = windowSize.Y * 0.9f / imageSize.Y;
                    float imageScale = Math.Min(scaleX, scaleY);

                    var displaySize = imageSize * imageScale;
                    var startPos = (windowSize - displaySize) * 0.5f;

                    ImGui.SetCursorPos(startPos);
                    ImGui.Image((ImTextureID)texture.Handle, displaySize);
                }
                else
                {
                    // Show loading or error message
                    var windowSize = ImGui.GetWindowSize();
                    var textSize = ImGui.CalcTextSize("Loading image...");
                    var textPos = (windowSize - textSize) * 0.5f;
                    ImGui.SetCursorPos(textPos);
                    ImGui.Text("Loading image...");
                }

                // Click anywhere to close
                if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    showImagePreview = false;
            }
            ImGui.End();

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor();
        }
        private string? GetCurrentImagePath()
        {
            var rp = CurrentProfile;
            if (rp == null) return null;

            // For external profiles, check if we have a downloaded image
            if (showingExternal && !string.IsNullOrEmpty(rp.ProfileImageUrl))
            {
                if (imageDownloadComplete && File.Exists(downloadedImagePath))
                    return downloadedImagePath;
                else
                    return null; // Still downloading or failed
            }

            // For local profiles, prefer custom image path
            if (!string.IsNullOrEmpty(rp.CustomImagePath) && File.Exists(rp.CustomImagePath))
            {
                return rp.CustomImagePath;
            }

            // Fall back to character image path
            if (!showingExternal && character?.ImagePath is { Length: > 0 } ip && File.Exists(ip))
            {
                return ip;
            }

            // Don't preview the default image
            return null;
        }


        private void DrawProfileContent(RPProfile rp, float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            var wndPos = ImGui.GetWindowPos();
            var wndSize = ImGui.GetWindowSize();

            var contentStartY = 65f * scale;
            var bioStartY = Math.Max(280f * scale, wndSize.Y * 0.48f);
            var animationStartY = Math.Max(bioStartY + (120f * scale), wndSize.Y * 0.85f);

            bool hasCustomBackground = !string.IsNullOrEmpty(rp.BackgroundImage);

            if (hasCustomBackground)
            {
                DrawCustomBackground(dl, wndPos, wndSize, rp.BackgroundImage!, scale);
            }
            else
            {
                var theme = rp.AnimationTheme ?? ProfileAnimationTheme.CircuitBoard;

                if (theme == ProfileAnimationTheme.Nature)
                {
                    DrawFullWindowForestBackground(dl, wndPos, wndSize);
                }
                else if (theme == ProfileAnimationTheme.DarkGothic)
                {
                    DrawFullWindowGothicBackground(dl, wndPos, wndSize);
                }
                else if (theme == ProfileAnimationTheme.MagicalParticles)
                {
                    DrawFullWindowMagicalBackground(dl, wndPos, wndSize);
                }
                else
                {
                    var animationHeight = wndSize.Y - animationStartY - (10f * scale);
                    DrawNeonGlow(dl, wndPos, wndSize, scale);
                    DrawMainBackground(dl, wndPos, wndSize, scale);
                    DrawAnimationTheme(dl, wndPos, wndSize, animationStartY, animationHeight, theme, scale);
                    DrawAnimationFadeIn(dl, wndPos, wndSize, bioStartY, animationStartY, scale);
                }
            }

            DrawEnhancedBorders(dl, wndPos, wndSize, scale);
            DrawEnhancedHeader(scale);
            ImGui.SetCursorPos(new Vector2(20 * scale, contentStartY));

            bool needsContentBackground = hasCustomBackground;
            if (!hasCustomBackground)
            {
                var theme = rp.AnimationTheme ?? ProfileAnimationTheme.CircuitBoard;
                needsContentBackground = (theme == ProfileAnimationTheme.Nature ||
                                        theme == ProfileAnimationTheme.DarkGothic ||
                                        theme == ProfileAnimationTheme.MagicalParticles);
            }

            if (needsContentBackground)
            {
                var headerHeight = 45f * scale;
                var borderThickness = 2f * scale;
                var contentBg = new Vector4(0.02f, 0.05f, 0.1f, 0.85f);

                dl.AddRectFilled(
                    wndPos + new Vector2(borderThickness, headerHeight + (1 * scale)),
                    wndPos + new Vector2(wndSize.X - borderThickness, bioStartY + (120f * scale)),
                    ImGui.ColorConvertFloat4ToU32(contentBg),
                    0f
                );
            }

            var contentHeight = bioStartY + (120f * scale) - contentStartY;
            ImGui.BeginChild("##Content", new Vector2(wndSize.X - (40 * scale), contentHeight), false, ImGuiWindowFlags.NoScrollbar);

            ImGui.BeginGroup();
            DrawPortraitSection(rp, scale);
            DrawTagsSection(rp, scale);
            ImGui.EndGroup();

            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (15f * scale));

            ImGui.BeginGroup();
            DrawNameSection(rp, scale);
            ImGui.Spacing();
            DrawExtendedFieldsAligned(rp, scale);
            ImGui.EndGroup();

            ImGui.Spacing();
            ImGui.Spacing();

            DrawResponsiveBioSection(rp, wndSize, scale);

            ImGui.EndChild();

            if (hasCustomBackground)
            {
                DrawModularEffects(dl, wndPos, wndSize, rp, scale);
            }
            else
            {
                var theme = rp.AnimationTheme ?? ProfileAnimationTheme.CircuitBoard;

                if (theme == ProfileAnimationTheme.Nature)
                {
                    DrawAnimatedPNGLeaves(dl, wndPos, wndSize.X, wndSize.Y, scale);
                    DrawAnimatedFireflies(dl, wndPos, wndSize.X, wndSize.Y, scale);
                }

                // Dark Gothic effects
                if (theme == ProfileAnimationTheme.DarkGothic)
                {
                    var deltaTime = ImGui.GetIO().DeltaTime;
                    animationTime += deltaTime;

                    DrawPulsingWindows(dl, wndPos, wndSize.X, wndSize.Y, ResolveNameplateColor(), scale);
                    DrawDriftingSmoke(dl, wndPos, wndSize.X, wndSize.Y, ResolveNameplateColor(), scale);
                    DrawAnimatedPixelBats(dl, wndPos, wndSize.X, wndSize.Y, scale);
                    DrawAnimatedFireSprites(dl, wndPos, wndSize.X, wndSize.Y, scale);
                    DrawGothicParticles(dl, wndPos, wndSize.X, wndSize.Y, ResolveNameplateColor(), scale);
                }
                if (theme == ProfileAnimationTheme.MagicalParticles)
                {
                    DrawMagicalSparkles(dl, wndPos, wndSize.X, wndSize.Y, scale);
                    DrawMagicalDustMotes(dl, wndPos, wndSize.X, wndSize.Y, scale);
                    DrawEnhancedLanternGlow(dl, wndPos, wndSize.X, wndSize.Y, scale);
                    DrawAnimatedMagicalButterflies(dl, wndPos, wndSize.X, wndSize.Y, scale);
                }
            }
            DrawFloatingActionButton(wndPos, wndSize, animationStartY, scale);
            DrawResizeHandle(wndPos, wndSize, scale);
        }

        private void DrawResponsiveBioSection(RPProfile rp, Vector2 windowSize, float scale)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.7f, 1f));
            ImGui.Text("Biography:");
            ImGui.PopStyleColor();

            var bioText = rp.Bio ?? "No biography available.";

            var remainingHeight = ImGui.GetContentRegionAvail().Y - (20f * scale);
            var baseHeight = 120f * scale; // Minimum bio height
            var scaledHeight = Math.Max(baseHeight, remainingHeight * 0.8f);

            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.05f, 0.05f, 0.1f, 0.6f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.3f, 0.3f, 0.4f, 0.5f));

            ImGui.BeginChild("##RPBio", new Vector2(-1, scaledHeight), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);

            ImGui.SetCursorPos(new Vector2(8f * scale, 8f * scale));

            var availableWidth = ImGui.GetContentRegionAvail().X - (16f * scale);

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4 * scale, 6 * scale));

            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + availableWidth);
            ImGui.TextWrapped(bioText);
            ImGui.PopTextWrapPos();

            ImGui.PopStyleVar();
            ImGui.EndChild();

            ImGui.PopStyleColor(2);
        }
        private void OpenUrl(string url)
        {
            try
            {
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                {
                    url = "https://" + url;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to open URL: {ex.Message}");
            }
        }

        private void DrawResizeHandle(Vector2 wndPos, Vector2 wndSize, float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            var color = ResolveNameplateColor();

            var handleSize = 20f * scale;
            var handlePos = wndPos + wndSize - new Vector2(handleSize, handleSize);

            ImGui.SetCursorScreenPos(handlePos);

            // Invisible button to capture mouse input
            ImGui.InvisibleButton("##ResizeHandle", new Vector2(handleSize, handleSize));

            // Change cursor when hovering
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNwse);
            }

            // Handle dragging
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var mouseDelta = ImGui.GetIO().MouseDelta;
                var currentSize = ImGui.GetWindowSize();
                var newSize = currentSize + mouseDelta;

                // Maintain aspect ratio (420:580 = 0.724) more math!!!
                var aspectRatio = 420f / 580f;

                if (Math.Abs(mouseDelta.X) > Math.Abs(mouseDelta.Y))
                {
                    newSize.Y = newSize.X / aspectRatio;
                }
                else
                {  
                    newSize.X = newSize.Y * aspectRatio;
                }

                newSize = Vector2.Max(newSize, new Vector2(420 * scale, 580 * scale));

                ImGui.SetWindowSize(newSize);
            }

            var lineColor = new Vector4(color.X, color.Y, color.Z, 0.6f);
            uint lineU32 = ImGui.ColorConvertFloat4ToU32(lineColor);

            var visualPos = handlePos + new Vector2(5f * scale, 5f * scale);
            for (int i = 0; i < 3; i++)
            {
                var offset = i * (3f * scale);
                dl.AddLine(
                    visualPos + new Vector2(offset, 10f * scale),
                    visualPos + new Vector2(10f * scale, offset),
                    lineU32, 1.5f * scale
                );
            }
        }

        // Custom background drawing
        private void DrawCustomBackground(ImDrawListPtr dl, Vector2 wndPos, Vector2 wndSize, string backgroundImage, float scale)
        {
            try
            {
                string pluginDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "";
                string assetsPath = Path.Combine(pluginDirectory, "Assets", "Backgrounds");

                string imagePath = Path.Combine(assetsPath, backgroundImage);

                if (!File.Exists(imagePath) && !backgroundImage.EndsWith(".jpg"))
                {
                    imagePath = Path.Combine(assetsPath, $"{backgroundImage}.jpg");
                }

                if (!File.Exists(imagePath))
                {
                    if (Directory.Exists(assetsPath))
                    {
                        var files = Directory.GetFiles(assetsPath);
                    }
                    else
                    {
                        Plugin.Log.Info($"Directory does not exist: {assetsPath}");
                    }
                    DrawNeonGlow(dl, wndPos, wndSize, scale);
                    DrawMainBackground(dl, wndPos, wndSize, scale);
                    return;
                }

                var texture = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
                if (texture != null)
                {
                    Vector2 imageSize = new Vector2(texture.Width, texture.Height);
                    float scaleX = wndSize.X / imageSize.X;
                    float scaleY = wndSize.Y / imageSize.Y;
                    float imageScale = Math.Max(scaleX, scaleY) * 1.1f;

                    Vector2 scaledSize = imageSize * imageScale;
                    Vector2 offset = (wndSize - scaledSize) * 0.5f;

                    dl.PushClipRect(wndPos, wndPos + wndSize, true);
                    dl.AddImage((ImTextureID)texture.Handle,
                        wndPos + offset,
                        wndPos + offset + scaledSize,
                        Vector2.Zero,
                        Vector2.One,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1.0f))
                    );
                    dl.PopClipRect();
                }
                else
                {
                    DrawNeonGlow(dl, wndPos, wndSize, scale);
                    DrawMainBackground(dl, wndPos, wndSize, scale);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error in DrawCustomBackground: {ex.Message}");
                DrawNeonGlow(dl, wndPos, wndSize, scale);
                DrawMainBackground(dl, wndPos, wndSize, scale);
            }
        }

        private void DrawModularEffects(ImDrawListPtr dl, Vector2 wndPos, Vector2 wndSize, RPProfile rp, float scale)
        {
            // Update animation time once
            var deltaTime = ImGui.GetIO().DeltaTime;
            animationTime += deltaTime;

            if (rp.Effects?.Fireflies == true)
                DrawAnimatedFireflies(dl, wndPos, wndSize.X, wndSize.Y, scale);

            if (rp.Effects?.FallingLeaves == true)
                DrawAnimatedPNGLeaves(dl, wndPos, wndSize.X, wndSize.Y, scale);

            if (rp.Effects?.Butterflies == true)
                DrawAnimatedMagicalButterflies(dl, wndPos, wndSize.X, wndSize.Y, scale);

            if (rp.Effects?.Bats == true)
                DrawAnimatedPixelBats(dl, wndPos, wndSize.X, wndSize.Y, scale);

            if (rp.Effects?.Fire == true)
                DrawAnimatedFireSprites(dl, wndPos, wndSize.X, wndSize.Y, scale);

            if (rp.Effects?.Smoke == true)
                DrawDriftingSmoke(dl, wndPos, wndSize.X, wndSize.Y, ResolveNameplateColor(), scale);

            if (rp.Effects?.CircuitBoard == true)
            {
                var animationStartY = (350f + 15f) * scale;
                var animationHeight = wndSize.Y - animationStartY - (10f * scale);
                var animationArea = wndPos + new Vector2(10 * scale, animationStartY);
                var animationSize = new Vector2(wndSize.X - (20f * scale), animationHeight);
                DrawCircuitBoardAnimation(dl, animationArea, animationSize.X, animationSize.Y, scale);
            }
        }

        private void DrawAnimationTheme(ImDrawListPtr dl, Vector2 wndPos, Vector2 wndSize, float startY, float height, ProfileAnimationTheme theme, float scale)
        {
            var animationArea = wndPos + new Vector2(10 * scale, startY);
            var animationSize = new Vector2(wndSize.X - (20f * scale), height);

            switch (theme)
            {
                case ProfileAnimationTheme.CircuitBoard:
                    DrawCircuitBoardAnimation(dl, animationArea, animationSize.X, animationSize.Y, scale);
                    break;
                case ProfileAnimationTheme.Minimalist:
                    DrawMinimalistAnimation(dl, animationArea, animationSize.X, animationSize.Y, scale);
                    break;
                case ProfileAnimationTheme.MagicalParticles:
                    DrawMagicalParticlesAnimation(dl, animationArea, animationSize.X, animationSize.Y, scale);
                    break;
            }
        }

        private void DrawExtendedFieldsAligned(RPProfile rp, float scale)
        {
            var labels = new[] { "Race:", "Gender:", "Age:", "Occupation:", "Orientation:", "Relationship:" };
            float maxLabelWidth = 0f;
            foreach (var label in labels)
            {
                var width = ImGui.CalcTextSize(label).X;
                if (width > maxLabelWidth) maxLabelWidth = width;
            }
            maxLabelWidth += (8f * scale);

            DrawAlignedInfoField("Race", rp.Race, maxLabelWidth, scale);
            DrawAlignedInfoField("Gender", rp.Gender, maxLabelWidth, scale);
            DrawAlignedInfoField("Age", rp.Age, maxLabelWidth, scale);
            DrawAlignedInfoField("Occupation", rp.Occupation, maxLabelWidth, scale);
            DrawAlignedInfoField("Orientation", rp.Orientation, maxLabelWidth, scale);
            DrawAlignedInfoField("Relationship", rp.Relationship, maxLabelWidth, scale);

            if (!string.IsNullOrWhiteSpace(rp.Abilities))
            {
                ImGui.Spacing();
                DrawAlignedInfoField("Abilities", rp.Abilities, maxLabelWidth, scale);
            }
        }

        private void DrawAlignedInfoField(string label, string? value, float labelWidth, float scale)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            ImGui.BeginGroup();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.7f, 1f));
            ImGui.Text($"{label}:");
            ImGui.PopStyleColor();

            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (labelWidth - ImGui.CalcTextSize($"{label}:").X));

            var availableWidth = ImGui.GetContentRegionAvail().X;
            var textWidth = ImGui.CalcTextSize(value).X;

            if (textWidth > availableWidth)
            {
                var truncatedText = value;
                while (ImGui.CalcTextSize(truncatedText + "...").X > availableWidth && truncatedText.Length > 0)
                {
                    truncatedText = truncatedText.Substring(0, truncatedText.Length - 1);
                }
                truncatedText += "...";

                ImGui.Text(truncatedText);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(value);
                }
            }
            else
            {
                ImGui.Text(value);
            }

            ImGui.EndGroup();
        }

        private void DrawCircuitBoardAnimation(ImDrawListPtr dl, Vector2 startPos, float width, float height, float scale)
        {
            var color = ResolveNameplateColor();
            var deltaTime = ImGui.GetIO().DeltaTime;
            animationTime += deltaTime;

            for (int i = 0; i < circuitPacketPositions.Length; i++)
            {
                circuitPacketPositions[i] += circuitPacketSpeeds[i] * deltaTime;
                if (circuitPacketPositions[i] > width + (20f * scale))
                {
                    circuitPacketPositions[i] = -(20f * scale);
                }
            }

            // Draw circuit grid
            var gridColor = new Vector4(color.X, color.Y, color.Z, 0.08f);
            uint gridU32 = ImGui.ColorConvertFloat4ToU32(gridColor);

            for (float y = 0; y <= height; y += (8f * scale))
            {
                dl.AddLine(startPos + new Vector2(0, y), startPos + new Vector2(width, y), gridU32, 0.5f * scale);
            }
            for (float x = 0; x <= width; x += (12f * scale))
            {
                dl.AddLine(startPos + new Vector2(x, 0), startPos + new Vector2(x, height), gridU32, 0.5f * scale);
            }

            // Draw pulsing connection lines
            var connectionCount = Math.Max(6, (int)(height / (25f * scale)));
            for (int i = 0; i < connectionCount; i++)
            {
                var y = (height / (connectionCount + 1)) * (i + 1);
                var pulse = 0.2f + 0.5f * (float)Math.Sin(animationTime * 1.8f + i * 0.8f);
                var lineColor = new Vector4(color.X, color.Y, color.Z, pulse);
                uint lineU32 = ImGui.ColorConvertFloat4ToU32(lineColor);

                dl.AddLine(startPos + new Vector2(0, y), startPos + new Vector2(width, y), lineU32, 1.5f * scale);

                // Connection branches
                for (float x = 25f * scale; x < width - (25f * scale); x += (40f * scale))
                {
                    var branchHeight = 6f * scale;
                    var branchColor = new Vector4(color.X, color.Y, color.Z, pulse * 0.6f);
                    uint branchU32 = ImGui.ColorConvertFloat4ToU32(branchColor);

                    dl.AddLine(startPos + new Vector2(x, y - branchHeight), startPos + new Vector2(x, y + branchHeight), branchU32, 1f * scale);
                    dl.AddLine(startPos + new Vector2(x - (6f * scale), y - branchHeight), startPos + new Vector2(x + (6f * scale), y - branchHeight), branchU32, 1f * scale);
                    dl.AddLine(startPos + new Vector2(x - (6f * scale), y + branchHeight), startPos + new Vector2(x + (6f * scale), y + branchHeight), branchU32, 1f * scale);
                }
            }

            // Draw glowing nodes
            var nodesPerRow = 8;
            var rows = Math.Max(3, (int)(height / (30f * scale)));
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < nodesPerRow; col++)
                {
                    var nodeX = (width / (nodesPerRow + 1)) * (col + 1);
                    var nodeY = (height / (rows + 1)) * (row + 1);
                    var nodePos = startPos + new Vector2(nodeX, nodeY);

                    var nodeIndex = row * nodesPerRow + col;
                    var phaseOffset = nodeIndex < circuitNodeGlowPhases.Length ? circuitNodeGlowPhases[nodeIndex % circuitNodeGlowPhases.Length] : 0f;
                    var glow = 0.3f + 0.7f * (float)Math.Sin(animationTime * 1.3f + phaseOffset);

                    var glowColor = new Vector4(color.X, color.Y, color.Z, glow * 0.3f);
                    dl.AddCircleFilled(nodePos, 5f * scale, ImGui.ColorConvertFloat4ToU32(glowColor));

                    var coreColor = new Vector4(color.X, color.Y, color.Z, glow);
                    dl.AddCircleFilled(nodePos, 2.5f * scale, ImGui.ColorConvertFloat4ToU32(coreColor));
                }
            }

            var pathCount = Math.Max(3, (int)(height / (40f * scale)));
            for (int i = 0; i < circuitPacketPositions.Length; i++)
            {
                var pathIndex = i % pathCount;
                var packetY = (height / (pathCount + 1)) * (pathIndex + 1);
                var packetX = circuitPacketPositions[i];

                if (packetX >= 0 && packetX <= width)
                {
                    var packetPos = startPos + new Vector2(packetX, packetY);

                    for (int t = 0; t < 8; t++)
                    {
                        var trailX = packetX - t * (4f * scale);
                        if (trailX >= 0)
                        {
                            var trailAlpha = (1f - t / 8f) * 0.7f;
                            var trailColor = new Vector4(color.X, color.Y, color.Z, trailAlpha);
                            var trailPos = startPos + new Vector2(trailX, packetY);
                            dl.AddCircleFilled(trailPos, (2f - t * 0.15f) * scale, ImGui.ColorConvertFloat4ToU32(trailColor));
                        }
                    }
                    var packetColor = new Vector4(color.X * 1.3f, color.Y * 1.3f, color.Z * 1.3f, 0.9f);
                    dl.AddCircleFilled(packetPos, 2.5f * scale, ImGui.ColorConvertFloat4ToU32(packetColor));
                }
            }
        }

        private void DrawMinimalistAnimation(ImDrawListPtr dl, Vector2 startPos, float width, float height, float scale)
        {
            var color = ResolveNameplateColor();
            var deltaTime = ImGui.GetIO().DeltaTime;
            animationTime += deltaTime;

            var breathPulse = 0.3f + 0.4f * (float)Math.Sin(animationTime * 0.8f);
            var glowColor = new Vector4(color.X, color.Y, color.Z, breathPulse);

            var barHeight = 6f * scale;
            var barY = startPos.Y + height - barHeight - (10f * scale);

            // Glow effect
            dl.AddRectFilled(
                new Vector2(startPos.X, barY - (2f * scale)),
                new Vector2(startPos.X + width, barY + barHeight + (2f * scale)),
                ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, breathPulse * 0.3f))
            );

            dl.AddRectFilled(
                new Vector2(startPos.X, barY),
                new Vector2(startPos.X + width, barY + barHeight),
                ImGui.ColorConvertFloat4ToU32(glowColor)
            );

            for (int i = 0; i < 3; i++)
            {
                var shapeX = startPos.X + (width / 4f) * (i + 1);
                var shapeY = startPos.Y + height * 0.6f;
                var shapePulse = 0.1f + 0.3f * (float)Math.Sin(animationTime * 0.6f + i * 2f);
                var shapeColor = new Vector4(color.X, color.Y, color.Z, shapePulse);

                dl.AddCircleFilled(new Vector2(shapeX, shapeY), 8f * scale, ImGui.ColorConvertFloat4ToU32(shapeColor));
            }
        }

        private void DrawMagicalParticlesAnimation(ImDrawListPtr dl, Vector2 startPos, float width, float height, float scale)
        {
        }

        private void DrawFullWindowForestBackground(ImDrawListPtr dl, Vector2 wndPos, Vector2 wndSize)
        {
            try
            {
                string pluginDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "";
                string assetsPath = Path.Combine(pluginDirectory, "Assets");
                string imagePath = Path.Combine(assetsPath, "forest_background.png");

                if (!File.Exists(imagePath))
                {
                    Vector4 topColor = new Vector4(0.1f, 0.1f, 0.2f, 1f);
                    Vector4 bottomColor = new Vector4(0.05f, 0.15f, 0.05f, 1f);

                    for (int y = 0; y < wndSize.Y; y += 2)
                    {
                        float t = y / wndSize.Y;
                        Vector4 color = Vector4.Lerp(topColor, bottomColor, t);
                        uint colorU32 = ImGui.ColorConvertFloat4ToU32(color);

                        dl.AddRectFilled(
                            wndPos + new Vector2(0, y),
                            wndPos + new Vector2(wndSize.X, y + 2),
                            colorU32
                        );
                    }
                    return;
                }

                var texture = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
                if (texture != null)
                {
                    Vector2 imageSize = new Vector2(texture.Width, texture.Height);
                    float scaleX = wndSize.X / imageSize.X;
                    float scaleY = wndSize.Y / imageSize.Y;
                    float scale = Math.Max(scaleX, scaleY) * 1.1f;

                    Vector2 scaledSize = imageSize * scale;
                    Vector2 offset = (wndSize - scaledSize) * 0.5f;
                    float backgroundAlpha = 1.0f;
                    dl.AddImage(
                        (ImTextureID)texture.Handle,
                        wndPos + offset,
                        wndPos + offset + scaledSize,
                        Vector2.Zero,
                        Vector2.One,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, backgroundAlpha))
                    );
                }
            }
            catch (Exception)
            {
                dl.AddRectFilled(wndPos, wndPos + wndSize, ImGui.ColorConvertFloat4ToU32(new Vector4(0.05f, 0.1f, 0.15f, 1f)));
            }
        }

        private void DrawFullWindowGothicBackground(ImDrawListPtr dl, Vector2 wndPos, Vector2 wndSize)
        {
            try
            {
                string pluginDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "";
                string assetsPath = Path.Combine(pluginDirectory, "Assets");
                string imagePath = Path.Combine(assetsPath, "gothic_background.png");

                if (!File.Exists(imagePath))
                {
                    Vector4 topColor = new Vector4(0.05f, 0.08f, 0.15f, 1f);
                    Vector4 bottomColor = new Vector4(0.02f, 0.02f, 0.05f, 1f);

                    for (int y = 0; y < wndSize.Y; y += 2)
                    {
                        float t = y / wndSize.Y;
                        Vector4 color = Vector4.Lerp(topColor, bottomColor, t);
                        uint colorU32 = ImGui.ColorConvertFloat4ToU32(color);

                        dl.AddRectFilled(
                            wndPos + new Vector2(0, y),
                            wndPos + new Vector2(wndSize.X, y + 2),
                            colorU32
                        );
                    }
                    return;
                }

                var texture = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
                if (texture != null)
                {
                    Vector2 imageSize = new Vector2(texture.Width, texture.Height);
                    float scaleX = wndSize.X / imageSize.X;
                    float scaleY = wndSize.Y / imageSize.Y;
                    float scale = Math.Max(scaleX, scaleY) * 1.2f;

                    Vector2 scaledSize = imageSize * scale;
                    Vector2 offset = new Vector2((wndSize.X - scaledSize.X) * 0.5f, (wndSize.Y - scaledSize.Y) * 0.2f);

                    dl.AddImage(
                        (ImTextureID)texture.Handle,
                        wndPos + offset,
                        wndPos + offset + scaledSize,
                        Vector2.Zero,
                        Vector2.One,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1.0f))
                    );
                }
            }
            catch (Exception)
            {
                dl.AddRectFilled(wndPos, wndPos + wndSize, ImGui.ColorConvertFloat4ToU32(new Vector4(0.05f, 0.08f, 0.15f, 1f)));
            }
        }

        private void DrawFullWindowMagicalBackground(ImDrawListPtr dl, Vector2 wndPos, Vector2 wndSize)
        {
            try
            {
                string pluginDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "";
                string assetsPath = Path.Combine(pluginDirectory, "Assets");
                string imagePath = Path.Combine(assetsPath, "magical_background.png");

                if (!File.Exists(imagePath))
                {
                    Vector4 topColor = new Vector4(0.05f, 0.15f, 0.25f, 1f);      // Deep magical blue
                    Vector4 bottomColor = new Vector4(0.15f, 0.08f, 0.05f, 1f);   // Warm brown

                    for (int y = 0; y < wndSize.Y; y += 2)
                    {
                        float t = y / wndSize.Y;
                        Vector4 color = Vector4.Lerp(topColor, bottomColor, t);
                        uint colorU32 = ImGui.ColorConvertFloat4ToU32(color);

                        dl.AddRectFilled(
                            wndPos + new Vector2(0, y),
                            wndPos + new Vector2(wndSize.X, y + 2),
                            colorU32
                        );
                    }
                    return;
                }

                var texture = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
                if (texture != null)
                {
                    Vector2 imageSize = new Vector2(texture.Width, texture.Height);

                    float scaleX = wndSize.X / imageSize.X;
                    float scaleY = wndSize.Y / imageSize.Y;
                    float scale = Math.Max(scaleX, scaleY) * 1.1f;

                    Vector2 scaledSize = imageSize * scale;
                    Vector2 offset = (wndSize - scaledSize) * 0.5f;

                    dl.PushClipRect(wndPos, wndPos + wndSize, true);

                    dl.AddImage(
                        (ImTextureID)texture.Handle,
                        wndPos + offset,
                        wndPos + offset + scaledSize,
                        Vector2.Zero,
                        Vector2.One,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1.0f))
                    );

                    dl.PopClipRect();
                }
            }
            catch (Exception)
            {
                dl.AddRectFilled(wndPos, wndPos + wndSize, ImGui.ColorConvertFloat4ToU32(new Vector4(0.08f, 0.12f, 0.18f, 1f)));
            }
        }

        private void DrawAnimatedFireflies(ImDrawListPtr dl, Vector2 startPos, float width, float height, float scale)
        {
            var deltaTime = ImGui.GetIO().DeltaTime;
            animationTime += deltaTime;

            var rp = CurrentProfile;
            Vector4[] particleColors = GetParticleColors(rp);

            for (int i = 0; i < 10; i++)
            {
                float fireflyTime = animationTime * (0.4f + i * 0.04f) + i * 1.5f;

                float baseX = width * (0.1f + (i * 0.08f));
                float baseY = height * (0.2f + (i % 5) * 0.15f);
                float primaryX = (float)Math.Sin(fireflyTime * 0.3f) * (35f * scale);
                float primaryY = (float)Math.Cos(fireflyTime * 0.25f) * (20f * scale);
                float secondaryX = (float)Math.Sin(fireflyTime * 1.1f + i * 0.4f) * (10f * scale);
                float secondaryY = (float)Math.Cos(fireflyTime * 1.3f + i * 0.3f) * (6f * scale);
                float microX = (float)Math.Sin(fireflyTime * 3.2f + i * 1.1f) * (2f * scale);
                float microY = (float)Math.Cos(fireflyTime * 3.8f + i * 0.7f) * (1.5f * scale);

                Vector2 fireflyPos = startPos + new Vector2(
                    baseX + primaryX + secondaryX + microX,
                    baseY + primaryY + secondaryY + microY
                );

                Vector4 fireflyColor = particleColors[i % particleColors.Length];
                Vector4 fireflyGlow = fireflyColor * 0.45f;

                float primaryPulse = (float)Math.Sin(fireflyTime * 2.5f + i * 0.6f) * 0.35f;
                float secondaryPulse = (float)Math.Sin(fireflyTime * 4.2f + i * 0.3f) * 0.15f;
                float magicalShimmer = (float)Math.Sin(fireflyTime * 6.8f + i * 0.8f) * 0.1f;

                float pulse = Math.Clamp(0.5f + primaryPulse + secondaryPulse + magicalShimmer, 0.25f, 1f);
                Vector4 currentFirefly = fireflyColor * pulse;
                Vector4 currentGlow = fireflyGlow * (pulse * 0.8f);

                Vector2 pixelPos = new Vector2((float)Math.Floor(fireflyPos.X), (float)Math.Floor(fireflyPos.Y));

                for (int glowRing = 4; glowRing >= 1; glowRing--)
                {
                    float glowAlpha = currentGlow.W * (1f - (glowRing - 1) * 0.22f);
                    Vector4 ringGlow = new Vector4(currentGlow.X, currentGlow.Y, currentGlow.Z, glowAlpha);
                    float ringSize = glowRing * 1.8f * scale;
                    dl.AddRectFilled(
                        pixelPos + new Vector2(-ringSize, -ringSize),
                        pixelPos + new Vector2(ringSize + (2 * scale), ringSize + (2 * scale)),
                        ImGui.ColorConvertFloat4ToU32(ringGlow)
                    );
                }

                dl.AddRectFilled(pixelPos, pixelPos + new Vector2(2 * scale, 2 * scale), ImGui.ColorConvertFloat4ToU32(currentFirefly));

                float flashChance = (float)Math.Sin(fireflyTime * 3.5f + i * 1.2f);
                if (flashChance > 0.7f)
                {
                    float flashIntensity = (flashChance - 0.7f) / 0.3f;
                    Vector4 flashColor = fireflyColor * (1f + flashIntensity * 0.6f);
                    dl.AddRectFilled(pixelPos, pixelPos + new Vector2(3 * scale, 3 * scale), ImGui.ColorConvertFloat4ToU32(flashColor));
                }
            }
        }

        private Vector4[] GetParticleColors(RPProfile rp)
        {
            if (rp?.Effects == null)
            {
                var nameplateColor = ResolveNameplateColor();
                return new Vector4[] {
                    new Vector4(nameplateColor.X, nameplateColor.Y, nameplateColor.Z, 0.9f),
                    new Vector4(nameplateColor.X * 1.2f, nameplateColor.Y * 1.2f, nameplateColor.Z * 1.2f, 0.9f),
                    new Vector4(nameplateColor.X * 0.8f, nameplateColor.Y * 0.8f, nameplateColor.Z * 0.8f, 0.9f),
                    new Vector4(nameplateColor.X * 0.9f, nameplateColor.Y * 0.9f, nameplateColor.Z * 0.9f, 0.9f)
                };
            }

            switch (rp.Effects.ColorScheme)
            {
                case ParticleColorScheme.Warm:
                    return new Vector4[] {
                        new Vector4(1f, 0.8f, 0.4f, 0.9f),     // Warm yellow
                        new Vector4(1f, 0.6f, 0.2f, 0.9f),     // Orange
                        new Vector4(1f, 0.9f, 0.5f, 0.9f),     // Light yellow
                        new Vector4(0.9f, 0.7f, 0.3f, 0.9f)    // Gold
                    };

                case ParticleColorScheme.Cool:
                    return new Vector4[] {
                        new Vector4(0.4f, 0.8f, 1f, 0.9f),     // Light blue
                        new Vector4(0.3f, 0.7f, 0.9f, 0.9f),   // Blue
                        new Vector4(0.5f, 0.9f, 1f, 0.9f),     // Cyan
                        new Vector4(0.6f, 0.8f, 0.9f, 0.9f)    // Light blue-gray
                    };

                case ParticleColorScheme.Forest:
                    return new Vector4[] {
                        new Vector4(0.4f, 0.8f, 0.3f, 0.9f),   // Green
                        new Vector4(0.6f, 0.9f, 0.4f, 0.9f),   // Light green
                        new Vector4(0.3f, 0.7f, 0.2f, 0.9f),   // Dark green
                        new Vector4(0.5f, 0.8f, 0.6f, 0.9f)    // Mint green
                    };

                case ParticleColorScheme.Magical:
                    return new Vector4[] {
                        new Vector4(0.8f, 0.4f, 1f, 0.9f),     // Purple
                        new Vector4(0.6f, 0.8f, 1f, 0.9f),     // Light purple-blue
                        new Vector4(0.9f, 0.5f, 0.9f, 0.9f),   // Pink
                        new Vector4(0.7f, 0.6f, 1f, 0.9f)      // Lavender
                    };

                case ParticleColorScheme.Winter:
                    return new Vector4[] {
                        new Vector4(1f, 1f, 1f, 0.9f),         // White
                        new Vector4(0.9f, 0.9f, 1f, 0.9f),     // Light blue-white
                        new Vector4(0.8f, 0.9f, 1f, 0.9f),     // Ice blue
                        new Vector4(0.95f, 0.95f, 0.95f, 0.9f) // Silver
                    };

                case ParticleColorScheme.Custom:
                    var customColor = rp.Effects.CustomParticleColor;
                    return new Vector4[] {
                        new Vector4(customColor.X, customColor.Y, customColor.Z, 0.9f),
                        new Vector4(customColor.X * 0.8f, customColor.Y * 0.8f, customColor.Z * 0.8f, 0.9f),
                        new Vector4(customColor.X * 1.2f, customColor.Y * 1.2f, customColor.Z * 1.2f, 0.9f),
                        new Vector4(customColor.X * 0.9f, customColor.Y * 0.9f, customColor.Z * 0.9f, 0.9f)
                    };

                case ParticleColorScheme.Auto:
                default:
                    var nameplateColor = ResolveNameplateColor();
                    return new Vector4[] {
                        new Vector4(nameplateColor.X, nameplateColor.Y, nameplateColor.Z, 0.9f),
                        new Vector4(nameplateColor.X * 1.2f, nameplateColor.Y * 1.2f, nameplateColor.Z * 1.2f, 0.9f),
                        new Vector4(nameplateColor.X * 0.8f, nameplateColor.Y * 0.8f, nameplateColor.Z * 0.8f, 0.9f),
                        new Vector4(nameplateColor.X * 0.9f, nameplateColor.Y * 0.9f, nameplateColor.Z * 0.9f, 0.9f)
                    };
            }
        }

        private void DrawAnimatedPNGLeaves(ImDrawListPtr dl, Vector2 startPos, float width, float height, float scale)
        {
            var deltaTime = ImGui.GetIO().DeltaTime;
            animationTime += deltaTime;

            try
            {
                string pluginDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "";
                string assetsPath = Path.Combine(pluginDirectory, "Assets");

                var rp = CurrentProfile;
                string[] leafFiles;

                switch (rp?.Effects?.ColorScheme)
                {
                    case ParticleColorScheme.Forest:
                        leafFiles = new string[] { "pixel_leaf_green.png" };
                        break;
                    case ParticleColorScheme.Cool:
                        leafFiles = new string[] { "pixel_leaf_blue.png" };
                        break;
                    case ParticleColorScheme.Magical:
                        leafFiles = new string[] { "pixel_leaf_purple.png" };
                        break;
                    case ParticleColorScheme.Winter:
                        leafFiles = new string[] { "pixel_leaf_white.png" };
                        break;
                    case ParticleColorScheme.Warm:
                        leafFiles = new string[] { "pixel_leaf_orange.png" };
                        break;
                    case ParticleColorScheme.Custom:
                        leafFiles = new string[] { "pixel_leaf.png" };
                        break;
                    case ParticleColorScheme.Auto:
                    default:
                        leafFiles = new string[]
                        {
                            "pixel_leaf.png",           // Original/default
                            "pixel_leaf_green.png",     // Forest green
                            "pixel_leaf_teal.png",      // Magical teal  
                            "pixel_leaf_blue.png",      // Mystical blue
                            "pixel_leaf_dark.png"       // Shadow/dark green
                        };
                        break;
                }

                var leafTextures = new List<IDalamudTextureWrap>();
                foreach (var leafFile in leafFiles)
                {
                    string leafPath = Path.Combine(assetsPath, leafFile);
                    if (File.Exists(leafPath))
                    {
                        var texture = Plugin.TextureProvider.GetFromFile(leafPath).GetWrapOrDefault();
                        if (texture != null)
                            leafTextures.Add(texture);
                    }
                }

                if (leafTextures.Count == 0)
                {
                    string fallbackPath = Path.Combine(assetsPath, "pixel_leaf.png");
                    if (File.Exists(fallbackPath))
                    {
                        var texture = Plugin.TextureProvider.GetFromFile(fallbackPath).GetWrapOrDefault();
                        if (texture != null)
                            leafTextures.Add(texture);
                    }
                }

                if (leafTextures.Count == 0) return;

                int maxLeaves = 10;
                for (int i = 0; i < maxLeaves; i++)
                {
                    float leafSeed = i * 7.3f;

                    float speedVariation = 0.6f + (float)Math.Sin(leafSeed) * 0.4f; 
                    float baseSpeed = (12f + (i % 4) * 5f) * scale;
                    float leafTime = animationTime * speedVariation * (baseSpeed / (20f * scale)) + leafSeed;

                    float swayFreq = 0.4f + (i % 5) * 0.2f;
                    float swayAmount = (8f + (i % 3) * 12f) * scale; 
                    float spiralEffect = (float)Math.Sin(leafTime * 1.2f + leafSeed) * (5f * scale); 

                    float fallDistance = height + (100f * scale);
                    float leafY = -(50f * scale) + (leafTime * (20f * scale)) % (fallDistance);

                    Vector2 leafPos = startPos + new Vector2(
                        width * (0.05f + (i * 11.7f) % 90f / 100f) + 
                        (float)Math.Sin(leafTime * swayFreq) * swayAmount + spiralEffect,
                        leafY
                    );
                    float sizeVariation = 0.7f + (i % 4) * 0.15f;
                    float baseScale = (0.08f + (i % 3) * 0.02f) * 0.6f * sizeVariation * scale;

                    var selectedTexture = leafTextures[i % leafTextures.Count];
                    Vector2 leafSize = new Vector2(selectedTexture.Width * baseScale, selectedTexture.Height * baseScale);
                    Vector4 colorTint = new Vector4(

                        0.9f + (float)Math.Sin(leafTime * 0.1f + leafSeed) * 0.1f,
                        0.95f + (float)Math.Cos(leafTime * 0.08f + leafSeed) * 0.05f,
                        0.9f + (float)Math.Sin(leafTime * 0.12f + leafSeed) * 0.1f,
                        0.8f
                    );

                    if (leafPos.Y >= startPos.Y - (60f * scale) && leafPos.Y <= startPos.Y + height + (60f * scale))
                    {
                        // Tiny shadow
                        Vector2 shadowOffset = new Vector2(0.3f * scale, 0.3f * scale);
                        Vector4 shadowColor = new Vector4(0f, 0f, 0f, 0.1f);

                        // Tiny leaf
                        dl.AddImage(
                            (ImTextureID)selectedTexture.Handle,
                            leafPos,
                            leafPos + leafSize,
                            Vector2.Zero,
                            Vector2.One,
                            ImGui.ColorConvertFloat4ToU32(colorTint)
                        );

                        // Very subtle shadow, don't look it's shy!
                        dl.AddImage(
                            (ImTextureID)selectedTexture.Handle,
                            leafPos + shadowOffset,
                            leafPos + leafSize + shadowOffset,
                            Vector2.Zero,
                            Vector2.One,
                            ImGui.ColorConvertFloat4ToU32(shadowColor)
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error loading leaf PNG: {ex.Message}");
            }
        }

        // Animated pixel bats
        private void DrawAnimatedPixelBats(ImDrawListPtr dl, Vector2 startPos, float width, float height, float scale)
        {
            try
            {
                string pluginDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "";
                string assetsPath = Path.Combine(pluginDirectory, "Assets");

                string batNormalPath = Path.Combine(assetsPath, "pixel_bat_normal.png");
                string batWingsUpPath = Path.Combine(assetsPath, "pixel_bat_wings_up.png");

                var batNormalTexture = File.Exists(batNormalPath) ?
                    Plugin.TextureProvider.GetFromFile(batNormalPath).GetWrapOrDefault() : null;
                var batWingsUpTexture = File.Exists(batWingsUpPath) ?
                    Plugin.TextureProvider.GetFromFile(batWingsUpPath).GetWrapOrDefault() : null;

                if (batNormalTexture == null && batWingsUpTexture == null) return;

                var fallbackTexture = batNormalTexture ?? batWingsUpTexture;

                for (int i = 0; i < 3; i++)
                {
                    float batSeed = i * 3.7f;
                    float batTime = animationTime * (0.15f + (i % 3) * 0.05f) + batSeed;

                    float flapSpeed = 1.5f + (i % 3) * 0.5f;
                    float flapTime = batTime * flapSpeed;
                    bool wingsUp = (flapTime % 1f) < 0.3f;

                    var currentTexture = (wingsUp && batWingsUpTexture != null) ? batWingsUpTexture :
                                        (batNormalTexture ?? fallbackTexture);

                    if (currentTexture == null) continue;

                    Vector2 batPos = GetBatPosition(startPos, width, height, i, batTime, batSeed, scale);

                    float batScale = (0.1f + (i % 3) * 0.03f) * scale;
                    Vector2 batSize = new Vector2(currentTexture.Width * batScale, currentTexture.Height * batScale);

                    float bobbing = (float)Math.Sin(batTime * 2f + batSeed) * (1.5f * scale);
                    batPos.Y += bobbing;

                    Vector4 batTint = GetBatTint(i, batTime);

                    if (batPos.X >= startPos.X - batSize.X && batPos.X <= startPos.X + width + batSize.X &&
                        batPos.Y >= startPos.Y - batSize.Y && batPos.Y <= startPos.Y + height + batSize.Y)
                    {
                        Vector2 shadowOffset = new Vector2(1f * scale, 1f * scale);
                        Vector4 shadowColor = new Vector4(0f, 0f, 0f, 0.3f);
                        dl.AddImage(
                            (ImTextureID)currentTexture.Handle,
                            batPos + shadowOffset,
                            batPos + batSize + shadowOffset,
                            Vector2.Zero,
                            Vector2.One,
                            ImGui.ColorConvertFloat4ToU32(shadowColor)
                        );

                        dl.AddImage(
                            (ImTextureID)currentTexture.Handle,
                            batPos,
                            batPos + batSize,
                            Vector2.Zero,
                            Vector2.One,
                            ImGui.ColorConvertFloat4ToU32(batTint)
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error loading bat sprites: {ex.Message}");
            }
        }

        private Vector2 GetBatPosition(Vector2 startPos, float width, float height, int batIndex, float batTime, float batSeed, float scale)
        {
            switch (batIndex)
            {
                case 0:
                    {
                        float centerX = width * 0.12f;
                        float centerY = height * 0.25f; 

                        return startPos + new Vector2(
                            centerX + (float)Math.Sin(batTime * 0.3f + batSeed) * (8f * scale),
                            centerY + (float)Math.Cos(batTime * 0.25f + batSeed) * (5f * scale)
                        );
                    }

                case 1:
                    {
                        float centerX = width * 0.18f;
                        float centerY = height * 0.75f;

                        return startPos + new Vector2(
                            centerX + (float)Math.Sin(batTime * 0.2f + batSeed) * (10f * scale),
                            centerY + (float)Math.Sin(batTime * 0.4f + batSeed) * (6f * scale)
                        );
                    }

                default:
                    {
                        float centerX = width * 0.75f;
                        float centerY = height * 0.3f;

                        return startPos + new Vector2(
                            centerX + (float)Math.Sin(batTime * 0.15f + batSeed) * (12f * scale),
                            centerY + (float)Math.Cos(batTime * 0.12f + batSeed) * (4f * scale)
                        );
                    }
            }
        }

        private Vector4 GetBatTint(int batIndex, float batTime)
        {
            Vector4[] batColors = new Vector4[]
            {
                new Vector4(0.9f, 0.9f, 0.9f, 0.95f),
                new Vector4(0.7f, 0.7f, 0.8f, 0.9f),
                new Vector4(0.8f, 0.7f, 0.7f, 0.9f),
                new Vector4(0.6f, 0.6f, 0.7f, 0.85f),
            };

            Vector4 baseColor = batColors[batIndex % batColors.Length];
            float brightness = 0.9f + (float)Math.Sin(batTime * 0.5f + batIndex) * 0.1f;

            return new Vector4(
                baseColor.X * brightness,
                baseColor.Y * brightness,
                baseColor.Z * brightness,
                baseColor.W
            );
        }
        private void DrawAnimatedFireSprites(ImDrawListPtr dl, Vector2 startPos, float width, float height, float scale)
        {
            try
            {
                string pluginDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "";
                string assetsPath = Path.Combine(pluginDirectory, "Assets");

                var fireTextures = new List<IDalamudTextureWrap>();
                for (int i = 1; i <= 7; i++)
                {
                    string firePath = Path.Combine(assetsPath, $"fire_{i}.png");
                    if (File.Exists(firePath))
                    {
                        var texture = Plugin.TextureProvider.GetFromFile(firePath).GetWrapOrDefault();
                        if (texture != null)
                        {
                            fireTextures.Add(texture);
                        }
                    }
                }

                if (fireTextures.Count == 0) return;

                float fireTime = animationTime * 1.5f;

                int cycleNum = (int)(fireTime * 0.3f);
                Random rng = new Random(cycleNum * 777);

                float randomValue = (float)rng.NextDouble();
                int maxFrame;

                if (randomValue < 0.4f) 
                {
                    maxFrame = rng.Next(2, 4);
                }
                else if (randomValue < 0.7f) 
                {
                    maxFrame = 4; 
                }
                else if (randomValue < 0.85f)
                {
                    maxFrame = 5; 
                }
                else if (randomValue < 0.95f)
                {
                    maxFrame = 6; 
                }
                else 
                {
                    maxFrame = 7; 
                }

                float cycleProgress = (fireTime * 0.4f) % 1f;
                float totalSteps = (maxFrame - 1) * 2f;
                float currentStep = cycleProgress * totalSteps;

                int frameIndex;
                if (currentStep <= (maxFrame - 1))
                {
                    frameIndex = 1 + (int)currentStep;
                }
                else
                {
                    float downStep = currentStep - (maxFrame - 1);
                    frameIndex = maxFrame - (int)downStep;
                }

                frameIndex = Math.Max(1, Math.Min(maxFrame, frameIndex));
                int textureIndex = frameIndex - 1; 

                var fireTexture = fireTextures[textureIndex];

                float fireX = startPos.X + (width - (fireTexture.Width * scale)) * 0.5f;
                float fireY = startPos.Y + height - (fireTexture.Height * scale) - (5f * scale);

                Vector2 firePosition = new Vector2(fireX, fireY);
                Vector2 spriteSize = new Vector2(fireTexture.Width * scale, fireTexture.Height * scale);

                Vector2 finalPosition = firePosition;

                // Purple/red colour mix
                Vector4 fireColor = new Vector4(1.3f, 0.6f, 1.2f, 1f);
                float brightness = 0.9f + (float)Math.Sin(fireTime * 2f) * 0.1f;
                fireColor = fireColor * brightness;
                fireColor.W = 1f;

                dl.AddImage(
                    (ImTextureID)fireTexture.Handle,
                    finalPosition,
                    finalPosition + spriteSize,
                    Vector2.Zero,
                    Vector2.One,
                    ImGui.ColorConvertFloat4ToU32(fireColor)
                );
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error in fire animation: {ex.Message}");
            }
        }
        private void DrawPulsingWindows(ImDrawListPtr dl, Vector2 startPos, float width, float height, Vector3 color, float scale)
        {
            Vector2[] windowPositions = new Vector2[]
            {
                new Vector2(width * 0.45f, height * 0.35f),
                new Vector2(width * 0.52f, height * 0.45f),
                new Vector2(width * 0.38f, height * 0.55f),
                new Vector2(width * 0.48f, height * 0.65f),
                new Vector2(width * 0.42f, height * 0.75f),
                new Vector2(width * 0.35f, height * 0.4f),
                new Vector2(width * 0.55f, height * 0.5f),
                new Vector2(width * 0.46f, height * 0.85f),
            };

            for (int i = 0; i < windowPositions.Length; i++)
            {
                Vector2 windowPos = startPos + windowPositions[i];
                float windowTime = animationTime * (0.15f + i * 0.05f) + i * 4f;

                float pulse = (i % 3) switch
                {
                    0 => 0.3f + (float)Math.Sin(windowTime * 0.2f) * 0.2f,
                    1 => 0.25f + (float)Math.Sin(windowTime * 0.3f) * 0.25f,
                    _ => 0.4f + (float)Math.Sin(windowTime * 0.15f) * 0.15f
                };

                Vector4 windowColor = (i % 4) switch
                {
                    0 => new Vector4(1f, 0.4f, 0.1f, pulse * 0.7f),
                    1 => new Vector4(1f, 0.6f, 0.2f, pulse * 0.7f),
                    2 => new Vector4(1f, 0.3f, 0.1f, pulse * 0.7f),
                    _ => new Vector4(1f, 0.7f, 0.3f, pulse * 0.7f)
                };
                float glowSize = (5f + pulse * 3f) * scale;
                dl.AddCircleFilled(windowPos, glowSize, ImGui.ColorConvertFloat4ToU32(windowColor * 0.4f));

                dl.AddCircleFilled(windowPos, (2f + pulse * 1f) * scale, ImGui.ColorConvertFloat4ToU32(windowColor));

                float randomFlare = (float)Math.Sin(windowTime * 0.4f + i * 3.1f) * (float)Math.Cos(windowTime * 0.2f + i * 1.7f);
                if (randomFlare > 0.9f) 
                {
                    Vector4 flareColor = windowColor * 1.3f;
                    dl.AddCircleFilled(windowPos, glowSize * 1.2f, ImGui.ColorConvertFloat4ToU32(flareColor * 0.2f));
                }
            }
        }
        private void DrawDriftingSmoke(ImDrawListPtr dl, Vector2 startPos, float width, float height, Vector3 color, float scale)
        {
            for (int i = 0; i < 5; i++)
            {
                float smokeTime = animationTime * (0.05f + i * 0.01f) + i * 4f;

                float smokeX = startPos.X + (width * -0.3f + (smokeTime * (12f * scale)) % (width * 1.6f));
                float smokeY = startPos.Y + height * (0.45f + (i % 3) * 0.15f) +
                               (float)Math.Sin(smokeTime * 0.3f + i) * (12f * scale); 

                Vector2 smokePos = new Vector2(smokeX, smokeY);

                Vector4 smokeColor = new Vector4(
                    0.2f + color.X * 0.1f, 
                    0.15f + color.Y * 0.05f,  
                    0.25f + color.Z * 0.15f, 
                    0.25f + (float)Math.Sin(smokeTime * 0.6f) * 0.1f
                );

                for (int layer = 0; layer < 3; layer++)
                {
                    float layerOffset = layer * (6f * scale);
                    float layerSize = ((20f + layer * 12f) * (0.9f + (float)Math.Sin(smokeTime * 0.4f + layer) * 0.3f)) * scale;
                    float layerAlpha = smokeColor.W * (1f - layer * 0.25f);

                    Vector4 layerColor = new Vector4(smokeColor.X, smokeColor.Y, smokeColor.Z, layerAlpha);

                    dl.AddCircleFilled(
                        smokePos + new Vector2(layerOffset, layer * (2f * scale)),
                        layerSize,
                        ImGui.ColorConvertFloat4ToU32(layerColor)
                    );
                }
            }
        }
        private void DrawGothicParticles(ImDrawListPtr dl, Vector2 startPos, float width, float height, Vector3 color, float scale)
        {
            for (int i = 0; i < 15; i++)
            {
                float particleTime = animationTime * (0.15f + i * 0.03f) + i * 1.2f;
                bool isEmber = i % 6 == 0;

                Vector2 particlePos = startPos + new Vector2(
                    (width / 15f) * i + (float)Math.Sin(particleTime * 0.8f) * (30f * scale),
                    height * (0.1f + (particleTime * 0.12f) % 0.9f) + (float)Math.Cos(particleTime * 0.6f) * (20f * scale)
                );

                if (isEmber)
                {
                    float glow = 0.6f + (float)Math.Sin(particleTime * 3f) * 0.4f;
                    Vector4 emberColor = new Vector4(1f, 0.5f, 0.1f, glow * 0.7f);

                    dl.AddCircleFilled(particlePos, 4f * scale, ImGui.ColorConvertFloat4ToU32(emberColor * 0.4f));
                    dl.AddCircleFilled(particlePos, 1.5f * scale, ImGui.ColorConvertFloat4ToU32(emberColor));
                }
                else
                {
                    Vector4 ashColor = new Vector4(0.3f, 0.3f, 0.4f, 0.5f);
                    float size = (0.8f + (float)Math.Sin(particleTime * 2f) * 0.4f) * scale;
                    dl.AddCircleFilled(particlePos, size, ImGui.ColorConvertFloat4ToU32(ashColor));
                }
            }
        }
        private void DrawMagicalSparkles(ImDrawListPtr dl, Vector2 startPos, float width, float height, float scale)
        {
            var deltaTime = ImGui.GetIO().DeltaTime;
            animationTime += deltaTime;
            for (int i = 0; i < 12; i++) 
            {
                float sparkleTime = animationTime * (0.3f + i * 0.05f) + i * 1.8f;

                float baseX = width * (0.08f + (i * 0.07f));
                float baseY = height * (0.15f + (i % 6) * 0.12f);
                float primaryX = (float)Math.Sin(sparkleTime * 0.25f) * (40f * scale);
                float primaryY = (float)Math.Cos(sparkleTime * 0.2f) * (25f * scale);

                float secondaryX = (float)Math.Sin(sparkleTime * 0.9f + i * 0.5f) * (12f * scale);
                float secondaryY = (float)Math.Cos(sparkleTime * 1.1f + i * 0.4f) * (8f * scale);

                float microX = (float)Math.Sin(sparkleTime * 2.8f + i * 1.3f) * (3f * scale);
                float microY = (float)Math.Cos(sparkleTime * 3.2f + i * 0.9f) * (2f * scale);

                Vector2 sparklePos = startPos + new Vector2(
                    baseX + primaryX + secondaryX + microX,
                    baseY + primaryY + secondaryY + microY
                );

                Vector4 sparkleColor = (i % 4) switch
                {
                    0 => new Vector4(1f, 0.8f, 0.4f, 0.9f), 
                    1 => new Vector4(0.9f, 0.7f, 0.3f, 0.9f),
                    2 => new Vector4(0.4f, 0.8f, 0.9f, 0.9f),
                    _ => new Vector4(0.8f, 0.9f, 1f, 0.9f) 
                };

                Vector4 sparkleGlow = sparkleColor * 0.5f;
                float primaryPulse = (float)Math.Sin(sparkleTime * 2.2f + i * 0.7f) * 0.4f;
                float secondaryPulse = (float)Math.Sin(sparkleTime * 3.8f + i * 0.4f) * 0.2f;
                float magicalShimmer = (float)Math.Sin(sparkleTime * 6.5f + i * 1.1f) * 0.15f;

                float pulse = 0.45f + primaryPulse + secondaryPulse + magicalShimmer;
                pulse = Math.Clamp(pulse, 0.2f, 1f);

                Vector4 currentSparkle = sparkleColor * pulse;
                Vector4 currentGlow = sparkleGlow * (pulse * 0.9f);

                Vector2 pixelPos = new Vector2((float)Math.Floor(sparklePos.X), (float)Math.Floor(sparklePos.Y));

                for (int glowRing = 5; glowRing >= 1; glowRing--)
                {
                    float glowAlpha = currentGlow.W * (1f - (glowRing - 1) * 0.18f);
                    Vector4 ringGlow = new Vector4(currentGlow.X, currentGlow.Y, currentGlow.Z, glowAlpha);

                    float ringSize = glowRing * (2.2f * scale);
                    dl.AddRectFilled(
                        pixelPos + new Vector2(-ringSize, -ringSize),
                        pixelPos + new Vector2(ringSize + (2 * scale), ringSize + (2 * scale)),
                        ImGui.ColorConvertFloat4ToU32(ringGlow)
                    );
                }

                dl.AddRectFilled(pixelPos, pixelPos + new Vector2(3 * scale, 3 * scale), ImGui.ColorConvertFloat4ToU32(currentSparkle));

                float flashChance = (float)Math.Sin(sparkleTime * 3.2f + i * 1.4f);
                if (flashChance > 0.75f)
                {
                    float flashIntensity = (flashChance - 0.75f) / 0.25f;
                    Vector4 flashColor = sparkleColor * (1f + flashIntensity * 0.5f);

                    if (flashIntensity > 0.7f)
                    {
                        flashColor = (i % 2 == 0)
                            ? new Vector4(0.9f, 0.6f, 1f, flashColor.W)   // Magical purple
                            : new Vector4(0.6f, 1f, 0.8f, flashColor.W);  // Mint green
                    }

                    dl.AddRectFilled(pixelPos, pixelPos + new Vector2(4 * scale, 4 * scale), ImGui.ColorConvertFloat4ToU32(flashColor));
                }
            }
        }

        // Floating magical dust motes (like dust in sunbeams, but magical)
        private void DrawMagicalDustMotes(ImDrawListPtr dl, Vector2 startPos, float width, float height, float scale)
        {
            for (int i = 0; i < 20; i++)
            {
                float dustTime = animationTime * (0.1f + i * 0.02f) + i * 2.5f;

                Vector2 dustPos = startPos + new Vector2(
                    width * (0.05f + (i * 13.7f) % 90f / 100f) + (float)Math.Sin(dustTime * 0.3f) * (15f * scale),
                    height * (0.1f + (dustTime * 0.08f) % 0.8f) + (float)Math.Cos(dustTime * 0.25f) * (8f * scale)
                );

                bool isMagical = i % 5 == 0;

                if (isMagical)
                {
                    Vector4 magicalColor = new Vector4(0.8f, 0.7f, 0.3f, 0.6f);
                    float twinkle = 0.4f + (float)Math.Sin(dustTime * 4f) * 0.3f;
                    magicalColor.W *= twinkle;

                    dl.AddCircleFilled(dustPos, 1.5f * scale, ImGui.ColorConvertFloat4ToU32(magicalColor));
                }
                else
                {
                    Vector4 dustColor = new Vector4(0.4f, 0.35f, 0.3f, 0.3f);
                    dl.AddCircleFilled(dustPos, 0.8f * scale, ImGui.ColorConvertFloat4ToU32(dustColor));
                }
            }
        }
        private void DrawEnhancedLanternGlow(ImDrawListPtr dl, Vector2 startPos, float width, float height, float scale)
        {
            Vector2[] lanternPositions = new Vector2[]
            {
                new Vector2(width * 0.15f, height * 0.3f), 
                new Vector2(width * 0.25f, height * 0.25f),
                new Vector2(width * 0.35f, height * 0.35f),
                new Vector2(width * 0.45f, height * 0.2f),
                new Vector2(width * 0.55f, height * 0.3f),
                new Vector2(width * 0.65f, height * 0.25f),
                new Vector2(width * 0.75f, height * 0.35f),
                new Vector2(width * 0.85f, height * 0.4f),
            };

            for (int i = 0; i < lanternPositions.Length; i++)
            {
                Vector2 lanternPos = startPos + lanternPositions[i];
                float lanternTime = animationTime * (0.4f + i * 0.1f) + i * 3f;

                float flicker = 0.6f + (float)Math.Sin(lanternTime * 1.5f) * 0.2f +
                               (float)Math.Sin(lanternTime * 3.2f) * 0.1f;

                Vector4 lanternGlow = new Vector4(1f, 0.7f, 0.3f, flicker * 0.4f);

                float glowSize = (12f + flicker * 6f) * scale;
                dl.AddCircleFilled(lanternPos, glowSize, ImGui.ColorConvertFloat4ToU32(lanternGlow * 0.3f));
                dl.AddCircleFilled(lanternPos, glowSize * 0.7f, ImGui.ColorConvertFloat4ToU32(lanternGlow * 0.5f));

                dl.AddCircleFilled(lanternPos, 3f * scale, ImGui.ColorConvertFloat4ToU32(lanternGlow * 1.2f));
            }
        }

        // Animated magical butterflies with 14-frame wing flapping, almost lifelike!
        private void DrawAnimatedMagicalButterflies(ImDrawListPtr dl, Vector2 startPos, float width, float height, float scale)
        {
            try
            {
                string pluginDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "";
                string assetsPath = Path.Combine(pluginDirectory, "Assets");

                var butterflyTextures = new List<IDalamudTextureWrap>();
                for (int i = 1; i <= 14; i++)
                {
                    string butterflyPath = Path.Combine(assetsPath, $"butterfly_{i}.png");
                    if (File.Exists(butterflyPath))
                    {
                        var texture = Plugin.TextureProvider.GetFromFile(butterflyPath).GetWrapOrDefault();
                        if (texture != null)
                        {
                            butterflyTextures.Add(texture);
                        }
                    }
                }

                if (butterflyTextures.Count == 0) return;

                for (int butterflyIndex = 0; butterflyIndex < 5; butterflyIndex++)
                {
                    float butterflySeed = butterflyIndex * 5.2f;
                    float butterflyTime = animationTime * (0.6f + butterflyIndex * 0.1f) + butterflySeed;

                    float cycleProgress = (butterflyTime * 0.6f) % 1f;
                    float totalSteps = 13f * 2f;
                    float currentStep = cycleProgress * totalSteps;

                    int frameIndex;
                    if (currentStep <= 13f)
                    {
                        frameIndex = 1 + (int)currentStep;
                    }
                    else
                    {
                        float downStep = currentStep - 13f;
                        frameIndex = 14 - (int)downStep;
                    }
                    frameIndex = Math.Max(1, Math.Min(14, frameIndex));
                    int textureIndex = frameIndex - 1;

                    var butterflyTexture = butterflyTextures[Math.Min(textureIndex, butterflyTextures.Count - 1)];

                    Vector2 butterflyBasePos = GetButterflyPosition(startPos, width, height, butterflyIndex, butterflyTime, butterflySeed, scale);

                    float butterflyScale = (0.15f + (butterflyIndex % 2) * 0.05f) * scale;
                    Vector2 butterflySize = new Vector2(butterflyTexture.Width * butterflyScale, butterflyTexture.Height * butterflyScale);

                    float bobbing = (float)Math.Sin(butterflyTime * 1.5f + butterflySeed) * (4f * scale);
                    Vector2 finalPos = butterflyBasePos + new Vector2(0, bobbing);

                    bool flipHorizontal = butterflyIndex % 2 == 1;
                    Vector2 uvStart = flipHorizontal ? new Vector2(1, 0) : Vector2.Zero;
                    Vector2 uvEnd = flipHorizontal ? new Vector2(0, 1) : Vector2.One;

                    Vector4 butterflyTint = GetButterflyTint(butterflyIndex, butterflyTime);

                    if (finalPos.X >= startPos.X - butterflySize.X && finalPos.X <= startPos.X + width + butterflySize.X &&
                        finalPos.Y >= startPos.Y - butterflySize.Y && finalPos.Y <= startPos.Y + height + butterflySize.Y)
                    {
                        Vector2 shadowOffset = new Vector2(2f * scale, 2f * scale);
                        Vector4 shadowColor = new Vector4(0f, 0f, 0f, 0.2f);
                        dl.AddImage(
                            (ImTextureID)butterflyTexture.Handle,
                            finalPos + shadowOffset,
                            finalPos + butterflySize + shadowOffset,
                            uvStart,
                            uvEnd,
                            ImGui.ColorConvertFloat4ToU32(shadowColor)
                        );

                        dl.AddImage(
                            (ImTextureID)butterflyTexture.Handle,
                            finalPos,
                            finalPos + butterflySize,
                            uvStart,
                            uvEnd,
                            ImGui.ColorConvertFloat4ToU32(butterflyTint)
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error in butterfly animation: {ex.Message}");
            }
        }

        private Vector2 GetButterflyPosition(Vector2 startPos, float width, float height, int butterflyIndex, float butterflyTime, float butterflySeed, float scale)
        {
            switch (butterflyIndex)
            {
                case 0:
                    {
                        float centerX = width * 0.25f;
                        float centerY = height * 0.3f;

                        return startPos + new Vector2(
                            centerX + (float)Math.Sin(butterflyTime * 0.3f) * (35f * scale),
                            centerY + (float)Math.Sin(butterflyTime * 0.6f) * (20f * scale)
                        );
                    }

                case 1: 
                    {
                        float centerX = width * 0.5f;
                        float centerY = height * 0.45f;

                        return startPos + new Vector2(
                            centerX + (float)Math.Cos(butterflyTime * 0.25f + butterflySeed) * (40f * scale),
                            centerY + (float)Math.Sin(butterflyTime * 0.25f + butterflySeed) * (25f * scale)
                        );
                    }

                case 2: 
                    {
                        float centerX = width * 0.75f;
                        float centerY = height * 0.35f;

                        return startPos + new Vector2(
                            centerX + (float)Math.Sin(butterflyTime * 0.2f + butterflySeed) * (30f * scale) +
                                      (float)Math.Sin(butterflyTime * 0.8f) * (10f * scale),
                            centerY + (float)Math.Cos(butterflyTime * 0.15f + butterflySeed) * (15f * scale)
                        );
                    }

                case 3:
                    {
                        float centerX = width * 0.2f;
                        float centerY = height * 0.75f;

                        return startPos + new Vector2(
                            centerX + (float)Math.Sin(butterflyTime * 0.35f + butterflySeed) * (25f * scale),
                            centerY + (float)Math.Cos(butterflyTime * 0.4f + butterflySeed) * (12f * scale)
                        );
                    }

                default:
                    {
                        float centerX = width * 0.8f;
                        float centerY = height * 0.7f;

                        return startPos + new Vector2(
                            centerX + (float)Math.Cos(butterflyTime * 0.28f + butterflySeed) * (30f * scale),
                            centerY + (float)Math.Sin(butterflyTime * 0.22f + butterflySeed) * (18f * scale) +
                                      (float)Math.Sin(butterflyTime * 0.6f) * (6f * scale)
                        );
                    }
            }
        }

        private Vector4 GetButterflyTint(int butterflyIndex, float butterflyTime)
        {
            Vector4[] butterflyColors = new Vector4[]
            {
                new Vector4(0.3f, 0.6f, 1f, 0.95f),      // Bright blue
                new Vector4(0.5f, 0.8f, 1f, 0.9f),       // Light blue/cyan
                new Vector4(0.2f, 0.4f, 0.9f, 0.95f),    // Deep blue
                new Vector4(0.6f, 0.9f, 1f, 0.9f),       // Pale blue
            };

            Vector4 baseColor = butterflyColors[butterflyIndex % butterflyColors.Length];

            float shimmer = 0.95f + (float)Math.Sin(butterflyTime * 2f + butterflyIndex) * 0.05f;

            return new Vector4(
                baseColor.X * shimmer,
                baseColor.Y * shimmer,
                baseColor.Z * shimmer,
                baseColor.W
            );
        }

        private void DrawNeonGlow(ImDrawListPtr dl, Vector2 wndPos, Vector2 wndSize, float scale)
        {
            var color = ResolveNameplateColor();
            var glowColor1 = new Vector4(color.X, color.Y, color.Z, 0.5f);
            var glowColor2 = new Vector4(color.X, color.Y, color.Z, 0.3f);
            var glowColor3 = new Vector4(color.X, color.Y, color.Z, 0.15f);
            var glowColor4 = new Vector4(color.X, color.Y, color.Z, 0.08f);

            uint glow1 = ImGui.ColorConvertFloat4ToU32(glowColor1);
            uint glow2 = ImGui.ColorConvertFloat4ToU32(glowColor2);
            uint glow3 = ImGui.ColorConvertFloat4ToU32(glowColor3);
            uint glow4 = ImGui.ColorConvertFloat4ToU32(glowColor4);

            var max = wndPos + wndSize;

            dl.AddRect(wndPos - new Vector2(12 * scale, 12 * scale), max + new Vector2(12 * scale, 12 * scale), glow4, 0f, ImDrawFlags.None, 12f * scale);
            dl.AddRect(wndPos - new Vector2(8 * scale, 8 * scale), max + new Vector2(8 * scale, 8 * scale), glow3, 0f, ImDrawFlags.None, 8f * scale);
            dl.AddRect(wndPos - new Vector2(5 * scale, 5 * scale), max + new Vector2(5 * scale, 5 * scale), glow2, 0f, ImDrawFlags.None, 5f * scale);
            dl.AddRect(wndPos - new Vector2(2 * scale, 2 * scale), max + new Vector2(2 * scale, 2 * scale), glow1, 0f, ImDrawFlags.None, 3f * scale);

            var edgeColor = new Vector4(color.X, color.Y, color.Z, 0.8f);
            dl.AddRect(wndPos, max, ImGui.ColorConvertFloat4ToU32(edgeColor), 0f, ImDrawFlags.None, 1f * scale);
        }

        private void DrawMainBackground(ImDrawListPtr dl, Vector2 wndPos, Vector2 wndSize, float scale)
        {
            var color = ResolveNameplateColor();
            var bgColor1 = new Vector4(0.03f, 0.03f, 0.12f, 1f);
            var bgColor2 = new Vector4(color.X * 0.12f, color.Y * 0.06f, color.Z * 0.22f, 1f);
            var bgColor3 = new Vector4(0.02f, 0.02f, 0.08f, 1f);

            uint bg1 = ImGui.ColorConvertFloat4ToU32(bgColor1);
            uint bg2 = ImGui.ColorConvertFloat4ToU32(bgColor2);
            uint bg3 = ImGui.ColorConvertFloat4ToU32(bgColor3);

            // Gradient background
            dl.AddRectFilledMultiColor(wndPos, wndPos + new Vector2(wndSize.X, wndSize.Y * 0.6f), bg1, bg1, bg2, bg2);
            dl.AddRectFilledMultiColor(wndPos + new Vector2(0, wndSize.Y * 0.6f), wndPos + wndSize, bg2, bg2, bg3, bg3);
        }

        private void DrawAnimationFadeIn(ImDrawListPtr dl, Vector2 wndPos, Vector2 wndSize, float bioEndY, float animationStartY, float scale)
        {
            var rp = CurrentProfile;
            var theme = rp?.AnimationTheme ?? ProfileAnimationTheme.CircuitBoard;

            if (theme == ProfileAnimationTheme.Nature)
                return;

            var color = ResolveNameplateColor();
            var fadeHeight = 25f * scale;
            var fadeStart = animationStartY;
            var fadeEnd = animationStartY + fadeHeight;

            var solidColor = new Vector4(color.X * 0.12f, color.Y * 0.06f, color.Z * 0.22f, 1f);
            var transparentColor = new Vector4(color.X * 0.12f, color.Y * 0.06f, color.Z * 0.22f, 0f);

            uint solidU32 = ImGui.ColorConvertFloat4ToU32(solidColor);
            uint transparentU32 = ImGui.ColorConvertFloat4ToU32(transparentColor);

            dl.AddRectFilledMultiColor(
                wndPos + new Vector2(3 * scale, fadeStart),
                wndPos + new Vector2(wndSize.X - (3 * scale), fadeEnd),
                solidU32, solidU32,
                transparentU32, transparentU32
            );
        }

        private void DrawEnhancedBorders(ImDrawListPtr dl, Vector2 wndPos, Vector2 wndSize, float scale)
        {
            var color = ResolveNameplateColor();
            var borderColor = new Vector4(color.X, color.Y, color.Z, 0.6f);
            var borderGlow = new Vector4(color.X, color.Y, color.Z, 0.3f);

            dl.AddRect(wndPos + new Vector2(2 * scale, 2 * scale), wndPos + wndSize - new Vector2(2 * scale, 2 * scale),
                      ImGui.ColorConvertFloat4ToU32(borderGlow), 0f, ImDrawFlags.None, 3f * scale);

            dl.AddRect(wndPos + new Vector2(1 * scale, 1 * scale), wndPos + wndSize - new Vector2(1 * scale, 1 * scale),
                      ImGui.ColorConvertFloat4ToU32(borderColor), 0f, ImDrawFlags.None, 1f * scale);
        }

        private void DrawEnhancedHeader(float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            var wndPos = ImGui.GetWindowPos();
            var wndSize = ImGui.GetWindowSize();
            var color = ResolveNameplateColor();

            var headerHeight = 45f * scale;
            var headerColor1 = new Vector4(color.X * 0.4f, color.Y * 0.3f, color.Z * 0.5f, 0.9f);
            var headerColor2 = new Vector4(color.X * 0.15f, color.Y * 0.08f, color.Z * 0.25f, 0.9f);

            uint hc1 = ImGui.ColorConvertFloat4ToU32(headerColor1);
            uint hc2 = ImGui.ColorConvertFloat4ToU32(headerColor2);

            dl.AddRectFilledMultiColor(wndPos, wndPos + new Vector2(wndSize.X, headerHeight), hc1, hc1, hc2, hc2);

            var topLineColor = new Vector4(color.X, color.Y, color.Z, 0.8f);
            var topGlowColor = new Vector4(color.X, color.Y, color.Z, 0.4f);

            dl.AddLine(wndPos + new Vector2(0, 1 * scale), wndPos + new Vector2(wndSize.X, 1 * scale),
                      ImGui.ColorConvertFloat4ToU32(topGlowColor), 4f * scale);
            dl.AddLine(wndPos, wndPos + new Vector2(wndSize.X, 0),
                      ImGui.ColorConvertFloat4ToU32(topLineColor), 2f * scale);

            var lineColor = new Vector4(color.X, color.Y, color.Z, 0.8f);
            var glowLineColor = new Vector4(color.X, color.Y, color.Z, 0.4f);

            dl.AddLine(wndPos + new Vector2(0, headerHeight + (1 * scale)), wndPos + new Vector2(wndSize.X, headerHeight + (1 * scale)),
                      ImGui.ColorConvertFloat4ToU32(glowLineColor), 4f * scale);
            dl.AddLine(wndPos + new Vector2(0, headerHeight), wndPos + new Vector2(wndSize.X, headerHeight),
                      ImGui.ColorConvertFloat4ToU32(lineColor), 2f * scale);

            DrawEnhancedCloseButton(scale);

            ImGui.SetCursorPos(new Vector2(20 * scale, 15 * scale));
            var headerColor = new Vector4(1f, 0.95f, 0.85f, 1f);
            var headerGlow = new Vector4(color.X, color.Y, color.Z, 0.6f);

            var textPos = ImGui.GetCursorScreenPos();
            dl.AddText(textPos - new Vector2(1 * scale, 1 * scale), ImGui.ColorConvertFloat4ToU32(headerGlow), "ROLEPLAY PROFILE");

            ImGui.PushStyleColor(ImGuiCol.Text, headerColor);
            ImGui.Text("ROLEPLAY PROFILE");
            ImGui.PopStyleColor();
        }

        private void DrawEnhancedCloseButton(float scale)
        {
            var wndSize = ImGui.GetWindowSize();
            var buttonSize = 20f * scale;
            var margin = 12f * scale;

            ImGui.SetCursorPos(new Vector2(wndSize.X - buttonSize - margin, 12f * scale));

            var buttonPos = ImGui.GetCursorScreenPos();
            var dl = ImGui.GetWindowDrawList();
            var glowColor = new Vector4(1f, 0.3f, 0.3f, 0.4f);

            dl.AddRectFilled(buttonPos - new Vector2(3 * scale, 3 * scale), buttonPos + new Vector2(buttonSize + (3 * scale), buttonSize + (3 * scale)),
                           ImGui.ColorConvertFloat4ToU32(glowColor), 0f);

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.9f, 0.1f, 0.1f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 0.2f, 0.2f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.8f, 0.05f, 0.05f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);

            if (ImGui.Button("X", new Vector2(buttonSize, buttonSize)))
            {
                IsOpen = false;
            }

            ImGui.PopStyleVar();
            ImGui.PopStyleColor(4);
        }

        private void DrawNameSection(RPProfile rp, float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            var namePos = ImGui.GetCursorScreenPos();
            var color = ResolveNameplateColor();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.95f, 0.8f, 1f));
            ImGui.Text(rp.CharacterName ?? "Unknown");
            ImGui.PopStyleColor();

            var nameSize = ImGui.CalcTextSize(rp.CharacterName ?? "Unknown");
            var glowColor = new Vector4(color.X, color.Y, color.Z, 0.3f);
            dl.AddText(namePos - new Vector2(1 * scale, 1 * scale), ImGui.ColorConvertFloat4ToU32(glowColor), rp.CharacterName ?? "Unknown");

            if (!string.IsNullOrEmpty(rp.Pronouns))
            {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.85f, 0.85f, 0.85f, 0.9f));
                ImGui.Text($"({rp.Pronouns})");
                ImGui.PopStyleColor();

                if (!string.IsNullOrEmpty(rp.Links))
                {
                    ImGui.SameLine();

                    var iconPos = ImGui.GetCursorScreenPos();
                    ImGui.SetCursorScreenPos(iconPos + new Vector2(0, 2 * scale));
                    var iconSize = new Vector2(12 * scale, 12 * scale);
                    var iconMin = iconPos - new Vector2(2 * scale, 2 * scale);
                    var iconMax = iconPos + iconSize + new Vector2(2 * scale, 2 * scale);

                    bool isHovering = ImGui.IsMouseHoveringRect(iconMin, iconMax);

                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.SetWindowFontScale(0.8f);

                    if (isHovering)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
                        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    }
                    else
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.8f, 1f, 0.9f));
                    }

                    ImGui.Text("\uf0c1");

                    ImGui.PopStyleColor();
                    ImGui.SetWindowFontScale(1.0f);
                    ImGui.PopFont();

                    if (isHovering && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        if (!string.IsNullOrEmpty(rp.Links))
                        {
                            OpenUrl(rp.Links.Trim());
                        }
                    }

                    if (isHovering)
                    {
                        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.06f, 0.06f, 0.06f, 0.98f));
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.92f, 0.92f, 0.92f, 1.0f));
                        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.25f, 0.25f, 0.35f, 0.6f));

                        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12 * scale, 10 * scale));
                        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8 * scale, 6 * scale));
                        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6.0f * scale);

                        ImGui.BeginTooltip();

                        float minTooltipWidth = 320 * scale; 
                        ImGui.Dummy(new Vector2(minTooltipWidth, 0));

                        // Link icon
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.8f, 1f, 1.0f)); // Link blue
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.Text("\uf0c1");
                        ImGui.PopFont();
                        ImGui.PopStyleColor();
                        ImGui.SameLine(0, 8 * scale);
                        ImGui.Text("External Link");

                        ImGui.Dummy(new Vector2(0, 4 * scale));

                        // URL display
                        ImGui.Separator();
                        ImGui.Dummy(new Vector2(0, 2 * scale));

                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
                        ImGui.Text("URL:");
                        ImGui.SameLine(0, 4 * scale);

                        string displayUrl = rp.Links.Length > 80 ? rp.Links.Substring(0, 77) + "..." : rp.Links;
                        ImGui.Text(displayUrl);
                        ImGui.PopStyleColor();

                        ImGui.Dummy(new Vector2(0, 4 * scale));

                        // Warning section
                        ImGui.Separator();
                        ImGui.Dummy(new Vector2(0, 2 * scale));

                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.8f, 0.4f, 1.0f)); // Warning yellow
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.Text("\uf071"); // Warning icon
                        ImGui.PopFont();
                        ImGui.PopStyleColor();
                        ImGui.SameLine(0, 8 * scale);

                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.8f, 0.4f, 1.0f));
                        ImGui.Text("Caution: External Link");
                        ImGui.PopStyleColor();

                        // Warning text
                        ImGui.Dummy(new Vector2(0, 2 * scale));
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
                        ImGui.Text("Only click links from trusted sources.");
                        ImGui.Text("Links will open in your default browser.");
                        ImGui.PopStyleColor();

                        ImGui.Dummy(new Vector2(0, 2 * scale));

                        ImGui.EndTooltip();

                        ImGui.PopStyleVar(3);
                        ImGui.PopStyleColor(3);
                    }
                }
            }
        }

        private void DrawPortraitSection(RPProfile rp, float scale)
        {
            ImGui.BeginGroup();

            var texture = GetProfileTexture(rp);
            if (texture != null)
            {
                DrawPortraitImage(texture, rp, scale);
            }
            else
            {
                ImGui.Dummy(new Vector2(140 * scale, 140 * scale));
                var dl = ImGui.GetWindowDrawList();
                var pos = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();

                var color = ResolveNameplateColor();
                var frameColor = new Vector4(color.X, color.Y, color.Z, 0.3f);
                var glowColor = new Vector4(color.X, color.Y, color.Z, 0.1f);

                dl.AddRectFilled(pos - new Vector2(6 * scale, 6 * scale), max + new Vector2(6 * scale, 6 * scale), ImGui.ColorConvertFloat4ToU32(glowColor), 8f * scale);
                dl.AddRectFilled(pos - new Vector2(3 * scale, 3 * scale), max + new Vector2(3 * scale, 3 * scale), ImGui.ColorConvertFloat4ToU32(frameColor), 6f * scale);

                dl.AddText(pos + new Vector2(45 * scale, 65 * scale), ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.8f, 0.8f, 1f)), "Loading...");
            }

            ImGui.EndGroup();
        }

        private void DrawTagsSection(RPProfile rp, float scale)
        {
            // Tags display - tag icon with tooltip on hover
            if (!string.IsNullOrWhiteSpace(rp.Tags))
            {
                ImGui.Spacing();
                var tags = rp.Tags.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToArray();
                if (tags.Length > 0)
                {
                    var color = ResolveNameplateColor();

                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(color.X * 0.9f, color.Y * 0.9f, color.Z * 0.9f, 0.8f));
                    ImGui.Text(FontAwesomeIcon.Tag.ToIconString());
                    ImGui.PopStyleColor();
                    ImGui.PopFont();

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.06f, 0.06f, 0.06f, 0.98f));
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.92f, 0.92f, 0.92f, 1.0f));
                        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.25f, 0.25f, 0.35f, 0.6f));

                        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10 * scale, 8 * scale));
                        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6 * scale, 4 * scale));
                        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6.0f * scale);

                        ImGui.BeginTooltip();

                        float minTooltipWidth = 220 * scale;
                        ImGui.Dummy(new Vector2(minTooltipWidth, 0));

                        // Tag header with icon
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.6f, 1.0f, 1.0f));
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.Text(FontAwesomeIcon.Tag.ToIconString());
                        ImGui.PopFont();
                        ImGui.PopStyleColor();
                        ImGui.SameLine(0, 6 * scale);
                        ImGui.Text("Tags");

                        ImGui.Dummy(new Vector2(0, 3 * scale));
                        ImGui.Separator();
                        ImGui.Dummy(new Vector2(0, 2 * scale));

                        string tagText = string.Join(", ", tags);

                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(color.X * 0.9f, color.Y * 0.9f, color.Z * 0.9f, 0.9f));

                        if (ImGui.CalcTextSize(tagText).X > minTooltipWidth - (20 * scale))
                        {
                            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + minTooltipWidth - (20 * scale));
                            ImGui.TextWrapped(tagText);
                            ImGui.PopTextWrapPos();
                        }
                        else
                        {
                            ImGui.Text(tagText);
                        }

                        ImGui.PopStyleColor();

                        ImGui.Dummy(new Vector2(0, 2 * scale));

                        ImGui.EndTooltip();

                        ImGui.PopStyleVar(3);
                        ImGui.PopStyleColor(3);
                    }
                }
            }
        }

        private void DrawPortraitImage(IDalamudTextureWrap texture, RPProfile rp, float scale)
        {
            float portraitSize = 140f * scale;
            float zoom = Math.Clamp(rp.ImageZoom, 0.5f, 3.0f);
            Vector2 offset = rp.ImageOffset * scale;

            float texAspect = (float)texture.Width / texture.Height;
            float drawWidth, drawHeight;

            if (texAspect >= 1f)
            {
                drawHeight = portraitSize * zoom;
                drawWidth = drawHeight * texAspect;
            }
            else
            {
                drawWidth = portraitSize * zoom;
                drawHeight = drawWidth / texAspect;
            }

            Vector2 drawSize = new(drawWidth, drawHeight);
            Vector2 cursor = ImGui.GetCursorScreenPos();
            var dl = ImGui.GetWindowDrawList();

            var color = ResolveNameplateColor();
            var frameColor = new Vector4(color.X, color.Y, color.Z, 0.9f);
            var glowColor = new Vector4(color.X, color.Y, color.Z, 0.4f);

            dl.AddRectFilled(
                cursor - new Vector2(6 * scale, 6 * scale),
                cursor + new Vector2(portraitSize + (6 * scale), portraitSize + (6 * scale)),
                ImGui.ColorConvertFloat4ToU32(glowColor),
                8f * scale
            );

            dl.AddRectFilled(
                cursor - new Vector2(3 * scale, 3 * scale),
                cursor + new Vector2(portraitSize + (3 * scale), portraitSize + (3 * scale)),
                ImGui.ColorConvertFloat4ToU32(frameColor),
                6f * scale
            );

            ImGui.BeginChild("ImageView", new Vector2(portraitSize), false,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

            ImGui.SetCursorScreenPos(cursor + offset);
            ImGui.Image((ImTextureID)texture.Handle, drawSize);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) || ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                string? imagePath = GetCurrentImagePath();
                if (!string.IsNullOrEmpty(imagePath))
                {
                    imagePreviewUrl = imagePath;
                    showImagePreview = true;
                }
            }
            if (ImGui.IsItemHovered())
            {
                string? imagePath = GetCurrentImagePath();
                if (!string.IsNullOrEmpty(imagePath))
                {
                    ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.06f, 0.06f, 0.06f, 0.98f));
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.92f, 0.92f, 0.92f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.25f, 0.25f, 0.35f, 0.6f));

                    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12 * scale, 10 * scale));
                    ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6.0f * scale);

                    ImGui.BeginTooltip();

                    // Image icon
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.8f, 1.0f, 1.0f));
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.Text("\uf03e"); // Image icon
                    ImGui.PopFont();
                    ImGui.PopStyleColor();
                    ImGui.SameLine(0, 8 * scale);
                    ImGui.Text("Image Preview");

                    ImGui.Dummy(new Vector2(0, 2 * scale));
                    ImGui.Separator();
                    ImGui.Dummy(new Vector2(0, 2 * scale));

                    ImGui.Text("Click to view full image");

                    ImGui.EndTooltip();

                    ImGui.PopStyleVar(2);
                    ImGui.PopStyleColor(3);
                }
            }
            ImGui.EndChild();
        }

        private void DrawBioSection(RPProfile rp, float scale)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.7f, 1f));
            ImGui.Text("Biography:");
            ImGui.PopStyleColor();

            var bioText = rp.Bio ?? "No biography available.";
            var textSize = ImGui.CalcTextSize(bioText, false, ImGui.GetContentRegionAvail().X - (20f * scale));
            var bioHeight = Math.Min(Math.Max(textSize.Y + (20f * scale), 80f * scale), 150f * scale);

            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.05f, 0.05f, 0.1f, 0.6f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.3f, 0.3f, 0.4f, 0.5f));

            ImGui.BeginChild("##RPBio", new Vector2(0, bioHeight), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);

            ImGui.SetCursorPos(new Vector2(8f * scale, 8f * scale));

            var availableWidth = ImGui.GetContentRegionAvail().X - (16f * scale);

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4 * scale, 6 * scale));

            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + availableWidth);
            ImGui.TextWrapped(bioText);
            ImGui.PopTextWrapPos();

            ImGui.PopStyleVar();
            ImGui.EndChild();

            ImGui.PopStyleColor(2);
        }

        private void DrawFloatingActionButton(Vector2 wndPos, Vector2 wndSize, float animationStartY, float scale)
        {
            if (showingExternal || character == null) return;

            var buttonWidth = 120f * scale;
            var buttonHeight = 28f * scale;
            var buttonX = (wndSize.X - buttonWidth) * 0.5f;
            var buttonY = animationStartY + (30f * scale);

            ImGui.SetCursorPos(new Vector2(buttonX, buttonY));

            var color = ResolveNameplateColor();
            var dl = ImGui.GetWindowDrawList();
            var buttonPos = ImGui.GetCursorScreenPos();

            var bgPadding = 8f * scale;
            var bgColor = new Vector4(0.02f, 0.02f, 0.08f, 0.9f);
            dl.AddRectFilled(
                buttonPos - new Vector2(bgPadding, bgPadding),
                buttonPos + new Vector2(buttonWidth + bgPadding, buttonHeight + bgPadding),
                ImGui.ColorConvertFloat4ToU32(bgColor), 6f * scale);

            var glowColor = new Vector4(color.X, color.Y, color.Z, 0.3f);
            dl.AddRectFilled(buttonPos - new Vector2(3 * scale, 3 * scale), buttonPos + new Vector2(buttonWidth + (3 * scale), buttonHeight + (3 * scale)),
                           ImGui.ColorConvertFloat4ToU32(glowColor), 4f * scale);

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(color.X * 0.5f, color.Y * 0.5f, color.Z * 0.5f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(color.X * 0.8f, color.Y * 0.8f, color.Z * 0.8f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(color.X, color.Y, color.Z, 1f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f * scale);

            if (ImGui.Button("Edit Profile", new Vector2(buttonWidth, buttonHeight)))
            {
                plugin.RPProfileEditor.SetCharacter(character);
                plugin.RPProfileEditor.IsOpen = true;
            }
            // Capture Edit Profile button position for tutorial
            plugin.EditProfileButtonPos = ImGui.GetItemRectMin();
            plugin.EditProfileButtonSize = ImGui.GetItemRectSize();
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(4);
        }

        private IDalamudTextureWrap? GetProfileTexture(RPProfile rp)
        {
            string? imagePath = null;

            if (showingExternal && !string.IsNullOrEmpty(rp.ProfileImageUrl))
            {
                HandleExternalImageDownload(rp.ProfileImageUrl);
                if (imageDownloadComplete && File.Exists(downloadedImagePath))
                    imagePath = downloadedImagePath;
                else
                    return null;
            }
            else if (!string.IsNullOrEmpty(rp.CustomImagePath) && File.Exists(rp.CustomImagePath))
            {
                imagePath = rp.CustomImagePath;
            }
            else if (!showingExternal && character?.ImagePath is { Length: > 0 } ip && File.Exists(ip))
            {
                imagePath = ip;
            }

            if (string.IsNullOrEmpty(imagePath) && !showingExternal)
            {
                string fallback = Path.Combine(plugin.PluginDirectory, "Assets", "Default.png");
                imagePath = fallback;
            }

            return !string.IsNullOrEmpty(imagePath) && File.Exists(imagePath)
                ? Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault()
                : null;
        }

        private void HandleExternalImageDownload(string imageUrl)
        {
            if (imageDownloadStarted) return;

            imageDownloadStarted = true;
            Task.Run(() =>
            {
                try
                {
                    using var client = new System.Net.Http.HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var data = client.GetByteArrayAsync(imageUrl).GetAwaiter().GetResult();

                    var hash = Convert.ToBase64String(
                        System.Security.Cryptography.MD5.HashData(
                            System.Text.Encoding.UTF8.GetBytes(imageUrl)
                        )
                    ).Replace("/", "_").Replace("+", "-");

                    string fileName = $"RPImage_{hash}.png";
                    string path = Path.Combine(
                        Plugin.PluginInterface.GetPluginConfigDirectory(),
                        fileName
                    );

                    File.WriteAllBytes(path, data);
                    downloadedImagePath = path;
                    imageDownloadComplete = true;
                    bringToFront = true;
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"[RPProfileViewWindow] Failed to download profile image: {ex.Message}");
                    imageDownloadComplete = true;
                }
            });
        }

        private Vector3 ResolveNameplateColor()
        {
            Vector3 fallback = new(0.4f, 0.7f, 1.0f);

            if (showingExternal && externalProfile != null)
            {
                if (externalProfile.ProfileColor.HasValue)
                {
                    var c = (Vector3)externalProfile.ProfileColor.Value;
                    if (c.X > 0.01f || c.Y > 0.01f || c.Z > 0.01f)
                        return c;
                }

                var nc = (Vector3)externalProfile.NameplateColor;
                if (nc.X < 0.01f && nc.Y < 0.01f && nc.Z < 0.01f)
                    return fallback;
                return nc;
            }
            if (character?.RPProfile?.ProfileColor.HasValue == true)
            {
                var c = (Vector3)character.RPProfile.ProfileColor.Value;
                if (c.X > 0.01f || c.Y > 0.01f || c.Z > 0.01f)
                    return c;
            }

            return character?.NameplateColor ?? fallback;
        }
        private float GetSafeScale(float baseScale)
        {
            return Math.Clamp(baseScale, 0.3f, 5.0f); // Prevent extreme scaling
        }
    }
}

using Dalamud.Interface.Windowing;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Bindings.ImGui;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.ManagedFontAtlas;
using CharacterSelectPlugin.Windows.Components;

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
        private int currentPreviewIndex = 0;
        private List<string> availableImagePaths = new List<string>();

        // Tab system
        private int activeTab = 0; // 0 = Overview, 1 = Gallery

        // Gallery image caching - use the same approach as profile images
        private Dictionary<string, string> galleryImagePaths = new Dictionary<string, string>(); // URL to local file path
        private Dictionary<string, bool> galleryImageComplete = new Dictionary<string, bool>(); // URL to download status
        private HashSet<string> downloadingImages = new HashSet<string>();
        private Dictionary<string, string> imageStatus = new Dictionary<string, string>(); // For debugging

        // Banner URL caching
        private HashSet<string> downloadingBanners = new HashSet<string>();

        // Background URL caching
        private HashSet<string> downloadingBackgrounds = new HashSet<string>();

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
        
        // Expansion state
        private bool isExpanded = false;
        private bool isAnimating = false;
        private bool pendingClose = false;
        private float animationProgress = 0f;
        private Vector2 collapsedSize = new Vector2(420f, 580f);
        private Vector2 expandedSize = new Vector2(1200f, 700f);
        private Vector2 currentWindowSize = new Vector2(420f, 580f);
        
        // Track actual biography position for handlebar alignment
        private float actualBioPositionY = 0f;

        public RPProfile? CurrentProfile => showingExternal ? externalProfile : character?.RPProfile;

        public RPProfileViewWindow(Plugin plugin)
            : this(plugin, "RPProfileWindow")
        {
        }

        public RPProfileViewWindow(Plugin plugin, string windowName)
            : base(windowName,
                ImGuiWindowFlags.NoDecoration |
                ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoCollapse)
        {
            this.plugin = plugin;
            IsOpen = false;

            UpdateSizeConstraints();

            InitializeAnimationData();
        }
        
        private void UpdateSizeConstraints()
        {
            if (isExpanded)
            {
                SizeConstraints = new WindowSizeConstraints
                {
                    MinimumSize = expandedSize,
                    MaximumSize = expandedSize
                };
            }
            else
            {
                SizeConstraints = new WindowSizeConstraints
                {
                    MinimumSize = collapsedSize,
                    MaximumSize = new Vector2(600, 800)
                };
            }
        }
        
        private void UpdateAnimation()
        {
            if (!isAnimating) return;
            
            var deltaTime = ImGui.GetIO().DeltaTime;
            animationProgress += deltaTime * 2f; // Slower, smoother animation
            
            if (animationProgress >= 1f)
            {
                animationProgress = 1f;
                isAnimating = false;
            }
            
            // Smoother cubic easing function (ease-in-out-cubic)
            float t = Math.Clamp(animationProgress, 0f, 1f);
            float easedProgress = t < 0.5f 
                ? 4f * t * t * t
                : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f;
            
            // Interpolate window size
            Vector2 targetSize = isExpanded ? expandedSize : collapsedSize;
            Vector2 startSize = isExpanded ? collapsedSize : expandedSize;
            currentWindowSize = Vector2.Lerp(startSize, targetSize, easedProgress);
            
            // Update size constraints during animation to prevent hitching
            if (isAnimating)
            {
                SizeConstraints = new WindowSizeConstraints
                {
                    MinimumSize = currentWindowSize,
                    MaximumSize = currentWindowSize
                };
            }
            else
            {
                // Only update final constraints when animation is complete
                UpdateSizeConstraints();
            }
            
            ImGui.SetWindowSize(currentWindowSize);
        }
        
        private void ToggleExpansion()
        {
            isExpanded = !isExpanded;
            isAnimating = true;
            animationProgress = 0f;
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

        private Vector2? pendingPositionOffset = null;
        private Vector2? pendingAbsolutePosition = null;

        public void SetCharacter(Character character)
        {
            this.character = character;
            showingExternal = false;
            bringToFront = true;
            ResetImageCache();

            // Always reset to compact view when setting character
            isExpanded = false;
            isAnimating = false;
            animationProgress = 0f;
            currentWindowSize = collapsedSize;
            UpdateSizeConstraints();
        }

        public void OffsetPosition(float x, float y)
        {
            pendingPositionOffset = new Vector2(x, y);
            pendingAbsolutePosition = null; // Clear absolute if offset is set
        }

        public void SetAbsolutePosition(float x, float y)
        {
            pendingAbsolutePosition = new Vector2(x, y);
            pendingPositionOffset = null; // Clear offset if absolute is set
        }

        public void RefreshCharacterData(Character character)
        {
            // Update character data without affecting the expanded state
            this.character = character;
            showingExternal = false;
            ResetImageCache(); // Ensure UI refreshes properly
            // Don't reset the expanded state or bring to front
            // This is for real-time updates from the editor
        }

        public void SetExternalProfile(RPProfile profile)
        {
            externalProfile = profile;
            showingExternal = true;
            ResetImageCache();
            bringToFront = true;
            
            // Always reset to compact view when setting external profile
            isExpanded = false;
            isAnimating = false;
            animationProgress = 0f;
            currentWindowSize = collapsedSize;
            UpdateSizeConstraints();
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

        public override void OnClose()
        {
            // Reset state when window closes
            stylesPushed = false;
            pendingClose = false;
            isExpanded = false;
            isAnimating = false;
            animationProgress = 0f;
            currentWindowSize = collapsedSize;
            ResetImageCache();
            base.OnClose();
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
                    // Use absolute position if set, otherwise use offset from center
                    if (pendingAbsolutePosition.HasValue)
                    {
                        ImGui.SetNextWindowPos(pendingAbsolutePosition.Value, ImGuiCond.Always);
                        pendingAbsolutePosition = null;
                    }
                    else
                    {
                        var center = ImGui.GetMainViewport().GetCenter();
                        var offset = pendingPositionOffset ?? Vector2.Zero;
                        ImGui.SetNextWindowPos(center + offset, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
                        pendingPositionOffset = null;
                    }
                    firstOpen = false;
                }

                // Force window size to match current state when bringing to front
                ImGui.SetNextWindowSize(currentWindowSize, ImGuiCond.Always);

                bringToFront = false;
            }
        }

        public override void Draw()
        {
            // Handle deferred close to avoid mid-draw state issues
            if (pendingClose)
            {
                pendingClose = false;
                IsOpen = false;
                if (stylesPushed)
                {
                    ImGui.PopStyleVar(2);
                    ImGui.PopStyleColor(2);
                    stylesPushed = false;
                }
                return;
            }

            var totalScale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);

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

            // Update animation state
            UpdateAnimation();

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

            if (!ImGui.Begin(WindowName,
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

            if (isExpanded)
            {
                DrawExpandedProfile(rp, totalScale);
            }
            else
            {
                DrawProfileContent(rp, totalScale);
                DrawExpandButton(totalScale);
            }

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
                    
                    // Navigation controls - only show if there are multiple images
                    if (availableImagePaths.Count > 1)
                    {
                        var buttonSize = new Vector2(60f, 40f);
                        var buttonPadding = 20f;
                        
                        // Left arrow button
                        ImGui.SetCursorPos(new Vector2(buttonPadding, (windowSize.Y - buttonSize.Y) * 0.5f));
                        if (ImGui.Button("◀", buttonSize) && currentPreviewIndex > 0)
                        {
                            currentPreviewIndex--;
                            imagePreviewUrl = availableImagePaths[currentPreviewIndex];
                        }
                        
                        // Right arrow button  
                        ImGui.SetCursorPos(new Vector2(windowSize.X - buttonSize.X - buttonPadding, (windowSize.Y - buttonSize.Y) * 0.5f));
                        if (ImGui.Button("▶", buttonSize) && currentPreviewIndex < availableImagePaths.Count - 1)
                        {
                            currentPreviewIndex++;
                            imagePreviewUrl = availableImagePaths[currentPreviewIndex];
                        }
                        
                        // Image counter
                        var counterText = $"{currentPreviewIndex + 1} / {availableImagePaths.Count}";
                        var counterSize = ImGui.CalcTextSize(counterText);
                        ImGui.SetCursorPos(new Vector2((windowSize.X - counterSize.X) * 0.5f, windowSize.Y - counterSize.Y - 30f));
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 0.8f));
                        ImGui.Text(counterText);
                        ImGui.PopStyleColor();
                    }
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

                // Keyboard navigation
                if (availableImagePaths.Count > 1)
                {
                    if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow) && currentPreviewIndex > 0)
                    {
                        currentPreviewIndex--;
                        imagePreviewUrl = availableImagePaths[currentPreviewIndex];
                    }
                    if (ImGui.IsKeyPressed(ImGuiKey.RightArrow) && currentPreviewIndex < availableImagePaths.Count - 1)
                    {
                        currentPreviewIndex++;
                        imagePreviewUrl = availableImagePaths[currentPreviewIndex];
                    }
                }
                
                // Escape to close
                if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                    showImagePreview = false;
                
                // Click anywhere to close (but not on navigation buttons)
                if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsAnyItemHovered())
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

            // For local profiles, check if a gallery image is selected for preview
            var galleryPreviewPath = GetSelectedGalleryPreviewPath();
            if (!string.IsNullOrEmpty(galleryPreviewPath))
            {
                return galleryPreviewPath;
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
        
        private string? GetSelectedGalleryPreviewPath()
        {
            var rp = CurrentProfile;
            // Only use gallery preview if explicitly enabled AND a valid image is selected
            if (rp?.UseGalleryPreview == true && rp.GalleryImages?.Count > 0 && rp.SelectedGalleryPreviewIndex >= 0)
            {
                var galleryIndex = Math.Clamp(rp.SelectedGalleryPreviewIndex, 0, rp.GalleryImages.Count - 1);
                var galleryImage = rp.GalleryImages[galleryIndex];
                return GetGalleryImagePath(galleryImage.Url);
            }
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

            // Check for URL-based RP background first, then preset backgrounds
            string? backgroundSource = null;
            bool isUrlBackground = false;

            if (!string.IsNullOrEmpty(rp.RPBackgroundImageUrl))
            {
                var cachedPath = GetRPBackgroundImageCachePath(rp.RPBackgroundImageUrl);
                if (File.Exists(cachedPath))
                {
                    backgroundSource = cachedPath;
                    isUrlBackground = true;
                }
                else if (!downloadingBackgrounds.Contains(rp.RPBackgroundImageUrl))
                {
                    Task.Run(async () => await DownloadRPBackgroundImageAsync(rp.RPBackgroundImageUrl));
                }
            }
            else if (!string.IsNullOrEmpty(rp.BackgroundImage))
            {
                // Fallback to preset backgrounds
                backgroundSource = rp.BackgroundImage;
            }

            bool hasCustomBackground = backgroundSource != null;

            if (hasCustomBackground)
            {
                if (isUrlBackground)
                {
                    DrawRPUrlBackground(dl, wndPos, wndSize, backgroundSource!, rp, scale);
                }
                else
                {
                    DrawCustomBackground(dl, wndPos, wndSize, backgroundSource!, scale);
                }
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

            // Banner is only shown in expanded view mode
            // In collapsed mode, we focus on the character portrait and basic info

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

            // Capture the actual biography Y position for the expand button
            var actualBioStartY = ImGui.GetCursorScreenPos().Y;
            DrawResponsiveBioSection(rp, wndSize, scale);
            var actualBioEndY = ImGui.GetCursorScreenPos().Y;
            
            // Calculate middle of bio section for handlebar alignment
            actualBioPositionY = actualBioStartY + ((actualBioEndY - actualBioStartY) / 2);

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

        private void DrawUrlBackground(ImDrawListPtr dl, Vector2 wndPos, Vector2 wndSize, string imagePath, RPProfile rp, float scale)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    DrawNeonGlow(dl, wndPos, wndSize, scale);
                    DrawMainBackground(dl, wndPos, wndSize, scale);
                    return;
                }

                var texture = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
                if (texture != null)
                {
                    Vector2 imageSize = new Vector2(texture.Width, texture.Height);

                    // Apply user zoom
                    float zoom = Math.Clamp(rp.BackgroundImageZoom, 0.5f, 3.0f);
                    float scaleX = wndSize.X / imageSize.X;
                    float scaleY = wndSize.Y / imageSize.Y;
                    float imageScale = Math.Max(scaleX, scaleY) * zoom;

                    Vector2 scaledSize = imageSize * imageScale;

                    // Apply user offset as percentage of window size (independent of image size)
                    // Range -1 to 1 maps to -50% to +50% of window dimension
                    var userOffsetX = rp.BackgroundImageOffsetX * wndSize.X * 0.5f;
                    var userOffsetY = rp.BackgroundImageOffsetY * wndSize.Y * 0.5f;
                    Vector2 offset = ((wndSize - scaledSize) * 0.5f) + new Vector2(userOffsetX, userOffsetY);

                    // Apply opacity
                    float opacity = Math.Clamp(rp.BackgroundImageOpacity, 0.1f, 1.0f);

                    dl.PushClipRect(wndPos, wndPos + wndSize, true);
                    dl.AddImage((ImTextureID)texture.Handle,
                        wndPos + offset,
                        wndPos + offset + scaledSize,
                        Vector2.Zero,
                        Vector2.One,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, opacity))
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
                Plugin.Log.Error($"Error in DrawUrlBackground: {ex.Message}");
                DrawNeonGlow(dl, wndPos, wndSize, scale);
                DrawMainBackground(dl, wndPos, wndSize, scale);
            }
        }

        private void DrawUrlBackgroundInRegion(ImDrawListPtr dl, Vector2 regionPos, Vector2 regionSize, string imagePath, RPProfile rp, float scale)
        {
            try
            {
                if (!File.Exists(imagePath))
                    return;

                var texture = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
                if (texture != null)
                {
                    Vector2 imageSize = new Vector2(texture.Width, texture.Height);

                    // Apply user zoom
                    float zoom = Math.Clamp(rp.BackgroundImageZoom, 0.5f, 3.0f);
                    float scaleX = regionSize.X / imageSize.X;
                    float scaleY = regionSize.Y / imageSize.Y;
                    float imageScale = Math.Max(scaleX, scaleY) * zoom;

                    Vector2 scaledSize = imageSize * imageScale;

                    // Apply user offset as percentage of region size (independent of image size)
                    // Range -1 to 1 maps to -50% to +50% of region dimension
                    var userOffsetX = rp.BackgroundImageOffsetX * regionSize.X * 0.5f;
                    var userOffsetY = rp.BackgroundImageOffsetY * regionSize.Y * 0.5f;
                    Vector2 offset = ((regionSize - scaledSize) * 0.5f) + new Vector2(userOffsetX, userOffsetY);

                    // Apply opacity
                    float opacity = Math.Clamp(rp.BackgroundImageOpacity, 0.1f, 1.0f);

                    dl.PushClipRect(regionPos, regionPos + regionSize, true);
                    dl.AddImage((ImTextureID)texture.Handle,
                        regionPos + offset,
                        regionPos + offset + scaledSize,
                        Vector2.Zero,
                        Vector2.One,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, opacity))
                    );
                    dl.PopClipRect();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error in DrawUrlBackgroundInRegion: {ex.Message}");
            }
        }

        private string GetBackgroundImageCachePath(string imageUrl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imageUrl))
                    return "";

                var fileName = $"erpbg_{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(imageUrl)).Replace('/', '_').Replace('+', '-')}";
                return Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), fileName);
            }
            catch
            {
                return "";
            }
        }

        private async Task DownloadBackgroundImageAsync(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return;

            var imagePath = GetBackgroundImageCachePath(imageUrl);

            if (File.Exists(imagePath))
                return;

            lock (downloadingBackgrounds)
            {
                if (downloadingBackgrounds.Contains(imageUrl))
                    return;
                downloadingBackgrounds.Add(imageUrl);
            }

            try
            {
                if (File.Exists(imagePath))
                    return;

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                var response = await client.GetAsync(imageUrl);

                if (response.IsSuccessStatusCode)
                {
                    var imageBytes = await response.Content.ReadAsByteArrayAsync();
                    var tempPath = imagePath + ".tmp";
                    await File.WriteAllBytesAsync(tempPath, imageBytes);
                    File.Move(tempPath, imagePath);
                    Plugin.Log.Info($"Downloaded ERP background image: {imageUrl}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to download ERP background image {imageUrl}: {ex.Message}");
            }
            finally
            {
                lock (downloadingBackgrounds)
                {
                    downloadingBackgrounds.Remove(imageUrl);
                }
            }
        }

        private void DrawRPUrlBackground(ImDrawListPtr dl, Vector2 wndPos, Vector2 wndSize, string imagePath, RPProfile rp, float scale)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    DrawNeonGlow(dl, wndPos, wndSize, scale);
                    DrawMainBackground(dl, wndPos, wndSize, scale);
                    return;
                }

                var texture = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
                if (texture != null)
                {
                    Vector2 imageSize = new Vector2(texture.Width, texture.Height);

                    // Apply user zoom (RP-specific property)
                    float zoom = Math.Clamp(rp.RPBackgroundImageZoom, 0.5f, 3.0f);
                    float scaleX = wndSize.X / imageSize.X;
                    float scaleY = wndSize.Y / imageSize.Y;
                    float imageScale = Math.Max(scaleX, scaleY) * zoom;

                    Vector2 scaledSize = imageSize * imageScale;

                    // Apply user offset as percentage of window size (independent of image size)
                    // Range -1 to 1 maps to -50% to +50% of window dimension
                    var userOffsetX = rp.RPBackgroundImageOffsetX * wndSize.X * 0.5f;
                    var userOffsetY = rp.RPBackgroundImageOffsetY * wndSize.Y * 0.5f;
                    Vector2 offset = ((wndSize - scaledSize) * 0.5f) + new Vector2(userOffsetX, userOffsetY);

                    // Apply opacity (RP-specific property)
                    float opacity = Math.Clamp(rp.RPBackgroundImageOpacity, 0.1f, 1.0f);

                    dl.PushClipRect(wndPos, wndPos + wndSize, true);
                    dl.AddImage((ImTextureID)texture.Handle,
                        wndPos + offset,
                        wndPos + offset + scaledSize,
                        Vector2.Zero,
                        Vector2.One,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, opacity))
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
                Plugin.Log.Error($"Error in DrawRPUrlBackground: {ex.Message}");
                DrawNeonGlow(dl, wndPos, wndSize, scale);
                DrawMainBackground(dl, wndPos, wndSize, scale);
            }
        }

        private string GetRPBackgroundImageCachePath(string imageUrl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imageUrl))
                    return "";

                var fileName = $"rpbg_{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(imageUrl)).Replace('/', '_').Replace('+', '-')}";
                return Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), fileName);
            }
            catch
            {
                return "";
            }
        }

        private async Task DownloadRPBackgroundImageAsync(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return;

            var imagePath = GetRPBackgroundImageCachePath(imageUrl);

            if (File.Exists(imagePath))
                return;

            lock (downloadingBackgrounds)
            {
                if (downloadingBackgrounds.Contains(imageUrl))
                    return;
                downloadingBackgrounds.Add(imageUrl);
            }

            try
            {
                if (File.Exists(imagePath))
                    return;

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                var response = await client.GetAsync(imageUrl);

                if (response.IsSuccessStatusCode)
                {
                    var imageBytes = await response.Content.ReadAsByteArrayAsync();
                    var tempPath = imagePath + ".tmp";
                    await File.WriteAllBytesAsync(tempPath, imageBytes);
                    File.Move(tempPath, imagePath);
                    Plugin.Log.Info($"Downloaded RP background image: {imageUrl}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to download RP background image {imageUrl}: {ex.Message}");
            }
            finally
            {
                lock (downloadingBackgrounds)
                {
                    downloadingBackgrounds.Remove(imageUrl);
                }
            }
        }

        private void DrawBanner(ImDrawListPtr dl, Vector2 wndPos, Vector2 wndSize, string bannerImagePath, float bannerZoom, Vector2 bannerOffset, float scale)
        {
            try
            {
                if (!File.Exists(bannerImagePath))
                {
                    return;
                }

                var bannerTexture = Plugin.TextureProvider.GetFromFile(bannerImagePath).GetWrapOrDefault();
                if (bannerTexture == null)
                {
                    return;
                }

                // Banner positioning - top portion of the window
                float bannerHeight = wndSize.Y * 0.25f; // Banner takes up top 25% of window
                Vector2 bannerRegionPos = wndPos;
                Vector2 bannerRegionSize = new Vector2(wndSize.X, bannerHeight);

                // Apply zoom and offset
                float zoom = Math.Clamp(bannerZoom, 0.1f, 8.0f);
                Vector2 offset = bannerOffset * scale;
                Plugin.Log.Info($"[Profile Banner Debug] bannerOffset: {bannerOffset}, scale: {scale}, final offset: {offset}");

                // Calculate banner size maintaining aspect ratio
                float bannerAspect = (float)bannerTexture.Width / bannerTexture.Height;
                float bannerWidth = bannerRegionSize.X * zoom;
                float bannerDrawHeight = bannerWidth / bannerAspect;
                
                Vector2 bannerDrawSize = new Vector2(bannerWidth, bannerDrawHeight);
                
                // Center the banner in the region and apply offset
                Vector2 centerOffset = (bannerRegionSize - bannerDrawSize) * 0.5f;
                Vector2 bannerDrawPos = bannerRegionPos + centerOffset + offset;

                // Clip to banner region
                dl.PushClipRect(bannerRegionPos, bannerRegionPos + bannerRegionSize, true);
                
                // Draw the banner with some transparency to blend with background
                dl.AddImage((ImTextureID)bannerTexture.Handle,
                    bannerDrawPos,
                    bannerDrawPos + bannerDrawSize,
                    Vector2.Zero,
                    Vector2.One,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.85f)) // Slightly transparent
                );

                dl.PopClipRect();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error in DrawBanner: {ex.Message}");
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
                pendingClose = true;
            }

            ImGui.PopStyleVar();
            ImGui.PopStyleColor(4);
        }

        private void DrawNameSection(RPProfile rp, float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            var namePos = ImGui.GetCursorScreenPos();
            var color = ResolveNameplateColor();

            // Use Alias if set, otherwise fall back to Name, finally to rp.CharacterName
            var displayName = showingExternal
                ? (rp.CharacterName ?? "Unknown")
                : (!string.IsNullOrWhiteSpace(character?.Alias) ? character.Alias : character?.Name ?? rp.CharacterName ?? "Unknown");

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.95f, 0.8f, 1f));
            ImGui.Text(displayName);
            ImGui.PopStyleColor();

            var nameSize = ImGui.CalcTextSize(displayName);
            var glowColor = new Vector4(color.X, color.Y, color.Z, 0.3f);
            dl.AddText(namePos - new Vector2(1 * scale, 1 * scale), ImGui.ColorConvertFloat4ToU32(glowColor), displayName);

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

        private void DrawTitleAndStatus(RPProfile rp, float scale)
        {
            var hasTitle = !string.IsNullOrEmpty(rp.Title);
            var hasStatus = !string.IsNullOrEmpty(rp.Status);

            if (!hasTitle && !hasStatus) return;

            var profileColor = ResolveNameplateColor();
            var drawList = ImGui.GetWindowDrawList();

            // Lift up closer to name/pronouns
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 2 * scale);

            // Title line (regular text, not italic)
            if (hasTitle)
            {
                // Draw icon if set (smaller size)
                if (rp.TitleIcon > 0)
                {
                    var iconPos = ImGui.GetCursorScreenPos();
                    ImGui.PushFont(UiBuilder.IconFont);
                    var iconText = ((FontAwesomeIcon)rp.TitleIcon).ToIconString();
                    var iconColor = ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X, profileColor.Y, profileColor.Z, 0.9f));
                    // Draw smaller by using drawlist with slight offset
                    ImGui.SetWindowFontScale(0.85f);
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(profileColor.X, profileColor.Y, profileColor.Z, 0.9f));
                    ImGui.Text(iconText);
                    ImGui.PopStyleColor();
                    ImGui.SetWindowFontScale(1.0f);
                    ImGui.PopFont();
                    ImGui.SameLine(0, 4 * scale);
                }

                // Draw title text (regular, not italic)
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.85f, 0.95f));
                ImGui.Text(rp.Title!);
                ImGui.PopStyleColor();
            }

            // Status line (italic)
            if (hasStatus)
            {
                // Draw icon if set (smaller size)
                if (rp.StatusIcon > 0)
                {
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.SetWindowFontScale(0.85f);
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.9f, 0.6f, 0.9f)); // Greenish for status
                    ImGui.Text(((FontAwesomeIcon)rp.StatusIcon).ToIconString());
                    ImGui.PopStyleColor();
                    ImGui.SetWindowFontScale(1.0f);
                    ImGui.PopFont();
                    ImGui.SameLine(0, 4 * scale);
                }

                // Draw status text in italics
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.85f, 0.7f, 0.95f)); // Greenish tint
                DrawItalicText(rp.Status!, scale);
                ImGui.PopStyleColor();
            }
        }

        private void DrawItalicText(string text, float scale)
        {
            // ImGui doesn't have native italic support, so we use a skew transform via drawlist
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var textSize = ImGui.CalcTextSize(text);
            var color = ImGui.GetColorU32(ImGuiCol.Text);

            // Draw each character with a slight horizontal offset based on vertical position
            float skewFactor = 0.15f; // How much to skew (italicize)
            float charX = pos.X;

            foreach (char c in text)
            {
                var charStr = c.ToString();
                var charSize = ImGui.CalcTextSize(charStr);

                // Calculate skew offset (top of character shifts right)
                float skewOffset = charSize.Y * skewFactor;

                // Draw character slightly skewed by offsetting based on Y
                drawList.AddText(new Vector2(charX + skewOffset * 0.5f, pos.Y), color, charStr);

                charX += charSize.X;
            }

            // Advance cursor past the text
            ImGui.Dummy(new Vector2(textSize.X + textSize.Y * skewFactor, textSize.Y));
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
            float zoom = Math.Clamp(rp.ImageZoom, 0.1f, 10.0f);
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
        
        private void DrawExpandButton(float scale)
        {
            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            var drawList = ImGui.GetWindowDrawList();
            
            // Inset handle on the right edge
            float handleWidth = 14f * scale;
            float handleHeight = 100f * scale;
            float handleInset = 3f * scale; // How far it's inset from edge
            
            // Use the actual biography middle position and center the handlebar on it
            float buttonX = windowPos.X + windowSize.X - handleWidth - handleInset;
            float buttonY = actualBioPositionY - (handleHeight / 2); // Center handlebar vertically
            
            var handleMin = new Vector2(buttonX, buttonY);
            var handleMax = new Vector2(buttonX + handleWidth, buttonY + handleHeight);
            
            // Check hover
            var mousePos = ImGui.GetIO().MousePos;
            bool isHovered = mousePos.X >= handleMin.X && mousePos.X <= windowPos.X + windowSize.X &&
                            mousePos.Y >= handleMin.Y && mousePos.Y <= handleMax.Y;
            
            // Get theme colors
            var profileColor = ResolveNameplateColor();
            
            // Draw inset groove effect with rounded corners
            drawList.AddRectFilled(
                handleMin,
                handleMax,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.3f)),
                6f * scale // Rounded corners
            );
            
            // Inner recessed area
            var innerMin = handleMin + new Vector2(2f * scale, 2f * scale);
            var innerMax = handleMax - new Vector2(2f * scale, 2f * scale);
            
            var bgColor = isHovered 
                ? new Vector4(profileColor.X * 0.15f, profileColor.Y * 0.15f, profileColor.Z * 0.15f, 0.8f)
                : new Vector4(0.05f, 0.05f, 0.05f, 0.7f);
                
            drawList.AddRectFilled(
                innerMin,
                innerMax,
                ImGui.ColorConvertFloat4ToU32(bgColor),
                4f * scale
            );
            
            // Vertical grip lines for texture
            float lineSpacing = 8f * scale;
            float lineY = innerMin.Y + 20f * scale;
            var gripColor = isHovered
                ? new Vector4(profileColor.X * 0.6f, profileColor.Y * 0.6f, profileColor.Z * 0.6f, 0.5f)
                : new Vector4(0.3f, 0.3f, 0.3f, 0.3f);
                
            while (lineY < innerMax.Y - 20f * scale)
            {
                drawList.AddLine(
                    new Vector2(innerMin.X + 4f * scale, lineY),
                    new Vector2(innerMax.X - 4f * scale, lineY),
                    ImGui.ColorConvertFloat4ToU32(gripColor),
                    1f * scale
                );
                lineY += lineSpacing;
            }
            
            // Small arrow in the center
            var arrowColor = isHovered 
                ? new Vector4(profileColor.X * 0.8f, profileColor.Y * 0.8f, profileColor.Z * 0.8f, 0.9f)
                : new Vector4(0.5f, 0.5f, 0.5f, 0.7f);
                
            var arrowCenter = new Vector2(buttonX + handleWidth * 0.5f, buttonY + handleHeight * 0.5f);
            var arrowSize = 3.5f * scale; // Much smaller arrow
            
            // Draw simple > arrow
            drawList.PathClear();
            drawList.PathLineTo(arrowCenter + new Vector2(-arrowSize, -arrowSize));
            drawList.PathLineTo(arrowCenter + new Vector2(arrowSize * 0.5f, 0));
            drawList.PathLineTo(arrowCenter + new Vector2(-arrowSize, arrowSize));
            drawList.PathStroke(
                ImGui.ColorConvertFloat4ToU32(arrowColor), 
                ImDrawFlags.None, 
                1.5f * scale // Thinner stroke
            );
            
            // Highlight edge when hovered
            if (isHovered)
            {
                drawList.AddRect(
                    handleMin,
                    handleMax,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X * 0.5f, profileColor.Y * 0.5f, profileColor.Z * 0.5f, 0.6f)),
                    4f * scale,
                    ImDrawFlags.None,
                    1f * scale
                );
            }

            // Draw NEW badge for Expanded RP Profile feature
            bool showExpandBadge = !plugin.Configuration.SeenFeatures.Contains(FeatureKeys.ExpandedRPProfile);
            if (showExpandBadge)
            {
                // Pulsing glow effect around the expand handle
                float pulse = (float)(Math.Sin(ImGui.GetTime() * 3.0) * 0.5 + 0.5);
                var glowColor = new Vector4(0.2f, 1.0f, 0.4f, 0.3f + pulse * 0.5f); // Green glow

                for (int i = 3; i >= 1; i--)
                {
                    var layerPadding = i * 2 * scale;
                    var layerAlpha = glowColor.W * (1.0f - (i * 0.25f));
                    drawList.AddRect(
                        handleMin - new Vector2(layerPadding, layerPadding),
                        handleMax + new Vector2(layerPadding, layerPadding),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(glowColor.X, glowColor.Y, glowColor.Z, layerAlpha)),
                        8f * scale,
                        ImDrawFlags.None,
                        2f * scale
                    );
                }
            }

            // Handle click
            if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                ToggleExpansion();

                // Mark Expanded RP Profile as seen when user expands
                if (!plugin.Configuration.SeenFeatures.Contains(FeatureKeys.ExpandedRPProfile))
                {
                    plugin.Configuration.SeenFeatures.Add(FeatureKeys.ExpandedRPProfile);
                    plugin.Configuration.Save();
                }
            }

            // Tooltip
            if (isHovered)
            {
                string tooltip = "View full profile";
                if (showExpandBadge)
                {
                    tooltip += "\n\nNEW: Expanded profiles with content boxes!";
                }
                ImGui.SetTooltip(tooltip);
            }
        }
        
        private void DrawExpandedProfile(RPProfile rp, float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            var wndPos = ImGui.GetWindowPos();
            var wndSize = ImGui.GetWindowSize();

            // Calculate header section heights
            var navHeight = 50f * scale;
            var bannerHeight = 200f * scale;
            var profileAreaHeight = 100f * scale;
            var contentStartY = navHeight + bannerHeight + profileAreaHeight;

            // Always draw base dark background for entire window first
            dl.AddRectFilled(
                wndPos,
                wndPos + wndSize,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.039f, 0.039f, 0.039f, 1.0f))
            );

            // Check for URL-based ERP background - draw only in content area (below nav+banner+profile)
            if (!string.IsNullOrEmpty(rp.BackgroundImageUrl))
            {
                var cachedPath = GetBackgroundImageCachePath(rp.BackgroundImageUrl);
                if (File.Exists(cachedPath))
                {
                    // Draw URL background only in content area
                    var contentPos = wndPos + new Vector2(0, contentStartY);
                    var contentSize = new Vector2(wndSize.X, wndSize.Y - contentStartY);
                    DrawUrlBackgroundInRegion(dl, contentPos, contentSize, cachedPath, rp, scale);
                }
                else if (!downloadingBackgrounds.Contains(rp.BackgroundImageUrl))
                {
                    Task.Run(async () => await DownloadBackgroundImageAsync(rp.BackgroundImageUrl));
                }
            }

            // Draw navigation bar
            DrawExpandedNavBar(scale);
            
            // Draw hero section with banner
            DrawHeroSection(rp, scale);
            
            // Draw content grid
            DrawContentGrid(rp, scale);
            
            // Draw collapse button in top-right
            DrawCollapseButton(scale);
            
            // Draw collapse handle on left side
            DrawLeftCollapseHandle(scale);
        }
        
        private void DrawExpandedNavBar(float scale)
        {
            var windowSize = ImGui.GetWindowSize();
            var navHeight = 50f * scale;
            var profileColor = ResolveNameplateColor();
            
            // Nav background
            var dl = ImGui.GetWindowDrawList();
            var wndPos = ImGui.GetWindowPos();
            
            dl.AddRectFilled(
                wndPos,
                wndPos + new Vector2(windowSize.X, navHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.039f, 0.039f, 0.039f, 0.95f))
            );
            
            // Bottom border
            dl.AddLine(
                wndPos + new Vector2(0, navHeight),
                wndPos + new Vector2(windowSize.X, navHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f)),
                1f * scale
            );
            
            // Title
            ImGui.SetCursorPos(new Vector2(24 * scale, 16 * scale));
            ImGui.TextColored(Vector4.One, "Character Profile");
            
            // Navigation tabs - Overview and Gallery
            var tabWidth = 100f * scale;
            var tabSpacing = 32f * scale;
            var totalTabWidth = tabWidth * 2 + tabSpacing; // Only 2 tabs now
            var tabStartX = (windowSize.X - totalTabWidth) * 0.5f;
            
            ImGui.SetCursorPos(new Vector2(tabStartX, 14 * scale));
            
            // Overview tab
            if (activeTab == 0)
            {
                // Active tab styling
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.1f, 0.1f, 0.1f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(profileColor.X, profileColor.Y, profileColor.Z, 1.0f));
            }
            else
            {
                // Inactive tab styling
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.1f, 0.1f, 0.1f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.631f, 0.631f, 0.667f, 1.0f));
            }
            
            if (ImGui.Button("Overview", new Vector2(tabWidth, 24 * scale)))
            {
                activeTab = 0;
            }
            ImGui.PopStyleColor(3);
            
            ImGui.SameLine(0, tabSpacing);
            
            // Gallery tab
            if (activeTab == 1)
            {
                // Active tab styling
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.1f, 0.1f, 0.1f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(profileColor.X, profileColor.Y, profileColor.Z, 1.0f));
            }
            else
            {
                // Inactive tab styling
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.1f, 0.1f, 0.1f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.631f, 0.631f, 0.667f, 1.0f));
            }
            
            if (ImGui.Button("Gallery", new Vector2(tabWidth, 24 * scale)))
            {
                activeTab = 1;
            }
            ImGui.PopStyleColor(3);
        }
        
        private void DrawHeroSection(RPProfile rp, float scale)
        {
            var windowSize = ImGui.GetWindowSize();
            var navHeight = 50f * scale;
            var bannerHeight = 200f * scale;
            var profileAreaHeight = 100f * scale;
            var profileColor = ResolveNameplateColor();
            
            var dl = ImGui.GetWindowDrawList();
            var wndPos = ImGui.GetWindowPos();
            
            // Banner background with custom image if available
            // Check for URL-based banner first, then local path
            string? bannerSource = null;

            if (!string.IsNullOrEmpty(rp.BannerImageUrl))
            {
                // URL-based banner
                var cachedPath = GetBannerImagePath(rp.BannerImageUrl);
                if (File.Exists(cachedPath))
                {
                    bannerSource = cachedPath;
                }
                else if (!downloadingBanners.Contains(rp.BannerImageUrl))
                {
                    // Start download
                    Task.Run(async () => await DownloadBannerImageAsync(rp.BannerImageUrl));
                }
            }
            else if (!string.IsNullOrEmpty(rp.BannerImagePath) && File.Exists(rp.BannerImagePath))
            {
                // Local file banner
                bannerSource = rp.BannerImagePath;
            }

            if (!string.IsNullOrEmpty(bannerSource))
            {
                DrawBanner(dl, wndPos, windowSize, bannerSource, rp.BannerZoom, rp.BannerOffset, scale, navHeight, bannerHeight);
            }
            else
            {
                // Default banner background (dark gray)
                dl.AddRectFilled(
                    wndPos + new Vector2(0, navHeight),
                    wndPos + new Vector2(windowSize.X, navHeight + bannerHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.15f, 1.0f))
                );
            }
            
            // Profile area below banner - gradient from profile color to black
            var profileAreaTop = wndPos + new Vector2(0, navHeight + bannerHeight);
            var profileAreaBottom = wndPos + new Vector2(windowSize.X, navHeight + bannerHeight + profileAreaHeight);
            
            // Create vertical gradient from profile-tinted color at top to black at bottom
            var topColor = new Vector4(
                profileColor.X * 0.2f,  // Use 20% of profile color intensity
                profileColor.Y * 0.2f,
                profileColor.Z * 0.2f,
                1.0f
            );
            var bottomColor = new Vector4(0f, 0f, 0f, 1.0f); // Pure black
            
            // Draw gradient rectangle (vertical gradient)
            dl.AddRectFilledMultiColor(
                profileAreaTop,
                profileAreaBottom,
                ImGui.ColorConvertFloat4ToU32(topColor),            // Top-left
                ImGui.ColorConvertFloat4ToU32(topColor),            // Top-right
                ImGui.ColorConvertFloat4ToU32(bottomColor),         // Bottom-right
                ImGui.ColorConvertFloat4ToU32(bottomColor)         // Bottom-left
            );
            
            // Profile image - positioned to overlap banner and profile area
            float imageSize = 160f * scale;
            float imageX = 40f * scale;
            
            var imagePath = GetCurrentImagePath();
            if (!string.IsNullOrEmpty(imagePath))
            {
                var texture = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
                if (texture != null)
                {
                    // Image overlaps both sections
                    var imagePos = wndPos + new Vector2(imageX, navHeight + bannerHeight - imageSize / 2);
                    
                    // White border around image
                    dl.AddRect(
                        imagePos - new Vector2(3 * scale),
                        imagePos + new Vector2(imageSize + 3 * scale),
                        ImGui.ColorConvertFloat4ToU32(Vector4.One),
                        8f * scale,
                        ImDrawFlags.None,
                        3f * scale
                    );
                    
                    // Dark border inside
                    dl.AddRect(
                        imagePos - new Vector2(1 * scale),
                        imagePos + new Vector2(imageSize + 1 * scale),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f)),
                        8f * scale,
                        ImDrawFlags.None,
                        1f * scale
                    );
                    
                    // Set up clipping for the image area
                    dl.PushClipRect(imagePos, imagePos + new Vector2(imageSize), true);
                    
                    // Check if we're displaying a gallery image and use gallery positioning
                    bool isGalleryImage = !string.IsNullOrEmpty(GetSelectedGalleryPreviewPath()) && 
                                        GetSelectedGalleryPreviewPath() == imagePath;
                    
                    // Apply zoom and offset - use gallery settings if displaying gallery image
                    // Scale offset proportionally since expanded uses 160px frame vs 140px in editor/collapsed
                    float zoom = Math.Clamp(rp.ImageZoom, 0.1f, 10.0f);
                    Vector2 offset = rp.ImageOffset * scale * (imageSize / (140f * scale));
                    
                    // Override with gallery image settings if displaying a gallery image
                    if (isGalleryImage && rp.SelectedGalleryPreviewIndex >= 0 && rp.SelectedGalleryPreviewIndex < rp.GalleryImages.Count)
                    {
                        var selectedImage = rp.GalleryImages[rp.SelectedGalleryPreviewIndex];
                        zoom = Math.Clamp(selectedImage.Zoom, 0.1f, 10.0f);
                        offset = selectedImage.Offset * scale * (imageSize / (140f * scale));
                    }
                    
                    // Calculate the actual draw size with zoom
                    float texAspect = (float)texture.Width / texture.Height;
                    float drawWidth, drawHeight;
                    
                    if (texAspect >= 1f)
                    {
                        drawHeight = imageSize * zoom;
                        drawWidth = drawHeight * texAspect;
                    }
                    else
                    {
                        drawWidth = imageSize * zoom;
                        drawHeight = drawWidth / texAspect;
                    }
                    
                    Vector2 drawSize = new(drawWidth, drawHeight);
                    
                    // Draw image with zoom and offset
                    dl.AddImageRounded(
                        (ImTextureID)texture.Handle,
                        imagePos + offset,
                        imagePos + offset + drawSize,
                        Vector2.Zero,
                        Vector2.One,
                        ImGui.ColorConvertFloat4ToU32(Vector4.One),
                        6f * scale
                    );
                    
                    // Pop the clipping rectangle
                    dl.PopClipRect();
                    
                    // Add invisible button for click detection
                    ImGui.SetCursorScreenPos(imagePos);
                    ImGui.InvisibleButton("##ExpandedProfileImage", new Vector2(imageSize));
                    
                    // Handle clicks
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Left) || ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        imagePreviewUrl = imagePath;
                        showImagePreview = true;
                    }
                    
                    // Handle hover tooltip
                    if (ImGui.IsItemHovered())
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
            }
            
            // Character name and pronouns - positioned closer to banner/image overlap
            ImGui.SetCursorPos(new Vector2(imageX + imageSize + 22 * scale, navHeight + bannerHeight + 5 * scale));
            
            // Name with pronouns on same line
            // Use Alias if set, otherwise fall back to Name
            var characterName = showingExternal
                ? (rp.CharacterName ?? "Unknown")
                : (!string.IsNullOrWhiteSpace(character?.Alias) ? character.Alias : character?.Name ?? "Unknown");
            
            // Draw name with larger font and pronouns with regular font
            using (Plugin.Instance?.NameFont?.Push())
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Vector4.One);
                ImGui.Text(characterName);
                ImGui.PopStyleColor();
            }
            
            if (!string.IsNullOrEmpty(rp.Pronouns))
            {
                // Draw pronouns using drawlist for precise positioning
                var drawList = ImGui.GetWindowDrawList();
                var nameSize = ImGui.GetItemRectSize();
                var namePos = ImGui.GetItemRectMin();
                
                // Position pronouns to the right of the name with vertical offset
                var pronounsText = $"({rp.Pronouns})";
                var pronounsPos = new Vector2(namePos.X + nameSize.X + 5 * scale, namePos.Y + 8 * scale);
                
                drawList.AddText(pronounsPos, ImGui.ColorConvertFloat4ToU32(new Vector4(0.631f, 0.631f, 0.667f, 1.0f)), pronounsText);
            }
            
            // Move cursor past name (reduced spacing)
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2 * scale);

            // Draw Title and Status for expanded view
            DrawTitleAndStatusExpanded(rp, scale, imageX + imageSize + 22 * scale);
        }

        private void DrawTitleAndStatusExpanded(RPProfile rp, float scale, float startX)
        {
            var hasTitle = !string.IsNullOrEmpty(rp.Title);
            var hasStatus = !string.IsNullOrEmpty(rp.Status);

            if (!hasTitle && !hasStatus) return;

            var profileColor = ResolveNameplateColor();
            var drawList = ImGui.GetWindowDrawList();

            // Title line (regular text, not italic)
            if (hasTitle)
            {
                ImGui.SetCursorPosX(startX);

                // Draw icon if set (smaller size)
                if (rp.TitleIcon > 0)
                {
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.SetWindowFontScale(0.85f);
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(profileColor.X, profileColor.Y, profileColor.Z, 0.9f));
                    ImGui.Text(((FontAwesomeIcon)rp.TitleIcon).ToIconString());
                    ImGui.PopStyleColor();
                    ImGui.SetWindowFontScale(1.0f);
                    ImGui.PopFont();
                    ImGui.SameLine(0, 4 * scale);
                }

                // Draw title text (regular, not italic)
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.85f, 0.95f));
                ImGui.Text(rp.Title!);
                ImGui.PopStyleColor();
            }

            // Status line (italic)
            if (hasStatus)
            {
                ImGui.SetCursorPosX(startX);

                // Draw icon if set (smaller size)
                if (rp.StatusIcon > 0)
                {
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.SetWindowFontScale(0.85f);
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.9f, 0.6f, 0.9f)); // Greenish for status
                    ImGui.Text(((FontAwesomeIcon)rp.StatusIcon).ToIconString());
                    ImGui.PopStyleColor();
                    ImGui.SetWindowFontScale(1.0f);
                    ImGui.PopFont();
                    ImGui.SameLine(0, 4 * scale);
                }

                // Draw status text in italics
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.85f, 0.7f, 0.95f)); // Greenish tint
                DrawItalicText(rp.Status!, scale);
                ImGui.PopStyleColor();
            }
        }

        private void DrawContentGrid(RPProfile rp, float scale)
        {
            var windowSize = ImGui.GetWindowSize();
            var navHeight = 50f * scale;
            var bannerHeight = 200f * scale;
            var profileAreaHeight = 100f * scale;
            var contentStartY = navHeight + bannerHeight + profileAreaHeight + 20 * scale;  // Content starts after profile area
            
            var contentMargin = 20f * scale;  // Reduced outer margin to give more space
            var contentWidth = windowSize.X - contentMargin * 2;
            var columnSpacing = 20f * scale;  // Fixed spacing between columns
            var mainColumnWidth = (contentWidth - columnSpacing) * 0.69f;
            var sidebarWidth = (contentWidth - columnSpacing) * 0.31f;
            
            var dl = ImGui.GetWindowDrawList();
            var wndPos = ImGui.GetWindowPos();
            
            // Main content section background
            var mainSectionPos = wndPos + new Vector2(contentMargin, contentStartY);
            var mainSectionSize = new Vector2(mainColumnWidth, windowSize.Y - contentStartY - contentMargin);
            var sidebarSectionPos = wndPos + new Vector2(contentMargin + mainColumnWidth + columnSpacing, contentStartY);
            var sidebarSectionSize = new Vector2(sidebarWidth, windowSize.Y - contentStartY - contentMargin);
            
            // Create single scroll container for unified scrolling
            var scrollHeight = windowSize.Y - contentStartY - contentMargin;
            ImGui.SetCursorPos(new Vector2(0, contentStartY));

            // Make child window background transparent when URL background is set
            bool hasUrlBg = !string.IsNullOrEmpty(rp.BackgroundImageUrl);
            if (hasUrlBg)
            {
                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0));
            }
            ImGui.BeginChild("##UnifiedScroll", new Vector2(windowSize.X, scrollHeight), false);
            if (hasUrlBg)
            {
                ImGui.PopStyleColor();
            }
            
            // Calculate content heights for virtual canvas
            float totalContentHeight;
            if (activeTab == 1) // Gallery
            {
                // For gallery tab, calculate height based on actual gallery content
                // Max 20 images = 5 rows, plus header, spacing and breathing room
                var maxRows = 5; // 20 images / 4 columns
                var headerHeight = 60f * scale; // Approximate header height
                var gallerySpacing = 16f * scale;
                var galleryPadding = 20f * scale;
                var breathingRoom = 100f * scale; // Extra space at bottom
                
                // Calculate approximate thumbnail size for height estimation
                var approximateAvailableWidth = windowSize.X - (contentMargin * 2) - (40f * scale); // Account for gallery container margins
                var approximateThumbnailSize = (approximateAvailableWidth - (gallerySpacing * 3)) / 4f;
                
                var galleryContentHeight = headerHeight + 
                                          (maxRows * approximateThumbnailSize) + 
                                          ((maxRows - 1) * gallerySpacing) + 
                                          (galleryPadding * 2) + 
                                          breathingRoom;
                
                totalContentHeight = galleryContentHeight;
            }
            else
            {
                // For Overview tab, use existing calculation
                var estimatedMainHeight = 3500f * scale;
                var estimatedSidebarHeight = 1500f * scale;
                totalContentHeight = Math.Max(estimatedMainHeight, estimatedSidebarHeight) + 40 * scale;
            }
            
            // Reserve space for the entire virtual canvas
            ImGui.Dummy(new Vector2(0, totalContentHeight));
            
            // Get draw list for the scroll container
            var childDl = ImGui.GetWindowDrawList();
            var childPos = ImGui.GetWindowPos();
            
            // Only draw column backgrounds for Overview tab
            if (activeTab == 0) // Overview
            {
                // Check if there's a URL background - if so, use semi-transparent panel backgrounds
                bool hasUrlBackground = !string.IsNullOrEmpty(rp.BackgroundImageUrl);
                float panelBgAlpha = hasUrlBackground ? 0.75f : 1.0f;
                float shadowAlpha = hasUrlBackground ? 0.3f : 0.5f;
                float edgeAlpha = hasUrlBackground ? 0.5f : 1.0f;

                // Draw backgrounds at fixed positions within scroll area
                // Main content background
                var mainPanelPos = childPos + new Vector2(contentMargin, 0);
                var mainPanelSize = new Vector2(mainColumnWidth, totalContentHeight);

            // Dark inner shadow
            childDl.AddRect(
                mainPanelPos + new Vector2(1, 1),
                mainPanelPos + new Vector2(mainPanelSize.X - 1, Math.Min(mainPanelSize.Y, scrollHeight) - 1),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, shadowAlpha)),
                6f * scale
            );

            // Main background
            childDl.AddRectFilled(
                mainPanelPos + new Vector2(2, 2),
                mainPanelPos + new Vector2(mainPanelSize.X - 2, Math.Min(mainPanelSize.Y, scrollHeight) - 2),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.035f, 0.035f, 0.035f, panelBgAlpha)),
                5f * scale
            );

            // Bright edge highlight
            childDl.AddRect(
                mainPanelPos,
                mainPanelPos + new Vector2(mainPanelSize.X, Math.Min(mainPanelSize.Y, scrollHeight)),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.08f, 0.08f, 0.08f, edgeAlpha)),
                6f * scale
            );

            // Sidebar background
            var sidebarPanelPos = childPos + new Vector2(contentMargin + mainColumnWidth + columnSpacing, 0);
            var sidebarPanelSize = new Vector2(sidebarWidth, totalContentHeight);

            // Dark inner shadow
            childDl.AddRect(
                sidebarPanelPos + new Vector2(1, 1),
                sidebarPanelPos + new Vector2(sidebarPanelSize.X - 1, Math.Min(sidebarPanelSize.Y, scrollHeight) - 1),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, shadowAlpha)),
                6f * scale
            );

            // Main background
            childDl.AddRectFilled(
                sidebarPanelPos + new Vector2(2, 2),
                sidebarPanelPos + new Vector2(sidebarPanelSize.X - 2, Math.Min(sidebarPanelSize.Y, scrollHeight) - 2),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.035f, 0.035f, 0.035f, panelBgAlpha)),
                5f * scale
            );

            // Bright edge highlight
            childDl.AddRect(
                sidebarPanelPos,
                sidebarPanelPos + new Vector2(sidebarPanelSize.X, Math.Min(sidebarPanelSize.Y, scrollHeight)),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.08f, 0.08f, 0.08f, edgeAlpha)),
                6f * scale
            );
            } // End background drawing for Overview tab
            
            // Now draw content directly on the virtual canvas
            // Content based on active tab
            if (activeTab == 0) // Overview
            {
                // Main content - with proper padding from edges
                var mainContentX = contentMargin + 12 * scale;
                var mainContentMaxX = contentMargin + mainColumnWidth - 24 * scale;
                ImGui.SetCursorPos(new Vector2(mainContentX, 18 * scale));
                ImGui.BeginChild("##MainContentInner", new Vector2(mainColumnWidth - 24 * scale, totalContentHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(32 * scale, 24 * scale));
                DrawMainContentCards(rp, scale);
                ImGui.PopStyleVar();
                ImGui.EndChild();
                
                // Sidebar content - positioned properly within its area
                var sidebarContentX = contentMargin + mainColumnWidth + columnSpacing + 12 * scale;
                ImGui.SetCursorPos(new Vector2(sidebarContentX, 18 * scale));
                ImGui.BeginChild("##SidebarInner", new Vector2(sidebarWidth - 24 * scale, totalContentHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(24 * scale, 24 * scale));
                DrawSidebarCards(rp, scale);
                ImGui.PopStyleVar();
                ImGui.EndChild();
            }
            else if (activeTab == 1) // Gallery
            {
                // Gallery container that nearly fills available width with breathing room
                var availableWidth = windowSize.X - (contentMargin * 2);
                var breathingRoom = 40f * scale; // Breathing room on each side
                var galleryWidth = Math.Max(800f * scale, availableWidth - (breathingRoom * 2)); // Nearly fill width, min 800px
                var galleryContentX = contentMargin + (availableWidth - galleryWidth) / 2f; // Center it
                
                ImGui.SetCursorPos(new Vector2(galleryContentX, 18 * scale));
                ImGui.BeginChild("##GalleryContent", new Vector2(galleryWidth, totalContentHeight), false, ImGuiWindowFlags.NoScrollbar);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(40 * scale, 32 * scale));
                DrawGalleryGrid(rp, scale);
                ImGui.PopStyleVar();
                ImGui.EndChild();
            }
            
            ImGui.EndChild(); // End UnifiedScroll
        }
        
        private void DrawMainContentCards(RPProfile rp, float scale)
        {
            // Use Alias if set, otherwise fall back to Name
            var characterName = showingExternal
                ? rp.CharacterName
                : (!string.IsNullOrWhiteSpace(character?.Alias) ? character.Alias : character?.Name);
            
            var drawList = ImGui.GetWindowDrawList();
            var headerPos = ImGui.GetCursorScreenPos();
            
            // Draw content cards from LeftContentBoxes if available, otherwise use defaults
            if (rp.LeftContentBoxes?.Count > 0)
            {
                // Show all content boxes on left side
                for (int i = 0; i < rp.LeftContentBoxes.Count; i++)
                {
                    var box = rp.LeftContentBoxes[i];
                    
                    DrawCard(box.Title, box.Subtitle, () =>
                    {
                        // Render content based on layout type
                        DrawContentBoxLayout(box, scale);
                    }, scale, true);
                    
                    // Spacing between cards
                    if (i < rp.LeftContentBoxes.Count - 1)
                    {
                        ImGui.Dummy(new Vector2(0, 16 * scale));
                    }
                }
            }
        }
        
        private void DrawSidebarCards(RPProfile rp, float scale)
        {
            // Draw content cards from RightContentBoxes if available, otherwise use defaults
            if (rp.RightContentBoxes?.Count > 0)
            {
                for (int i = 0; i < rp.RightContentBoxes.Count; i++)
                {
                    var box = rp.RightContentBoxes[i];
                    
                    DrawCard(box.Title, null, () =>
                    {
                        if (box.Title == "Quick Info")
                        {
                            DrawQuickInfo(rp, scale);
                        }
                        else if (box.Title == "Additional Details")
                        {
                            DrawAdditionalInfo(rp, scale);
                        }
                        else if (box.Title == "Key Traits")
                        {
                            if (!string.IsNullOrEmpty(rp.Tags))
                            {
                                var tags = rp.Tags.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();
                                foreach (var tag in tags)
                                {
                                    DrawTraitItem(tag, scale);
                                }
                            }
                            else
                            {
                                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.431f, 0.431f, 0.451f, 1.0f));
                                ImGui.TextWrapped("No traits defined yet.");
                                ImGui.PopStyleColor();
                            }
                        }
                        else if (box.Title == "Likes & Dislikes" || box.Type == ContentBoxType.LikesAndDislikes)
                        {
                            if (!string.IsNullOrEmpty(box.Likes) || !string.IsNullOrEmpty(box.Dislikes))
                            {
                                DrawContentBoxLayout(box, scale);
                            }
                            else
                            {
                                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.431f, 0.431f, 0.451f, 1.0f));
                                ImGui.TextWrapped("No likes or dislikes defined yet.");
                                ImGui.PopStyleColor();
                            }
                        }
                        else if (box.Title == "External Links")
                        {
                            if (!string.IsNullOrEmpty(rp.Links))
                            {
                                DrawExternalLink(rp.Links, scale);
                            }
                            else
                            {
                                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.431f, 0.431f, 0.451f, 1.0f));
                                ImGui.TextWrapped("No external links provided yet.");
                                ImGui.PopStyleColor();
                            }
                        }
                        else
                        {
                            // Use the proper content box layout system
                            DrawContentBoxLayout(box, scale);
                        }
                    }, scale);
                    
                    // Spacing between cards
                    if (i < rp.RightContentBoxes.Count - 1)
                    {
                        ImGui.Dummy(new Vector2(0, 24 * scale));
                    }
                }
            }
            else
            {
                // Fallback to default cards
                // Quick Info - only first 4 items
                DrawCard("Quick Info", null, () =>
                {
                    DrawQuickInfo(rp, scale);
                }, scale);
                
                // Spacing between cards
                ImGui.Dummy(new Vector2(0, 24 * scale));
                
                // Additional Details - only show if data exists
                if (!string.IsNullOrEmpty(rp.Relationship) || !string.IsNullOrEmpty(rp.Occupation) || !string.IsNullOrEmpty(rp.AdditionalDetailsCustom))
                {
                    DrawCard("Additional Details", null, () =>
                    {
                        DrawAdditionalInfo(rp, scale);
                    }, scale);

                    // Spacing between cards
                    ImGui.Dummy(new Vector2(0, 24 * scale));
                }
                
                // Character Traits (Tags) - only show if data exists
                if (!string.IsNullOrEmpty(rp.Tags))
                {
                    DrawCard("Key Traits", null, () =>
                    {
                        var tags = rp.Tags.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();
                        foreach (var tag in tags)
                        {
                            DrawTraitItem(tag, scale);
                        }
                    }, scale);

                    // Proper spacing between cards
                    ImGui.Dummy(new Vector2(0, 24 * scale));
                }
                
                // Likes & Dislikes - only show if data exists
                var likesDislikesBox = rp.LeftContentBoxes?.FirstOrDefault(cb => cb.Type == ContentBoxType.LikesAndDislikes)
                                      ?? rp.RightContentBoxes?.FirstOrDefault(cb => cb.Type == ContentBoxType.LikesAndDislikes);
                var likesData = likesDislikesBox?.Likes ?? "";
                var dislikesData = likesDislikesBox?.Dislikes ?? "";

                if (!string.IsNullOrEmpty(likesData) || !string.IsNullOrEmpty(dislikesData))
                {
                    DrawCard("Likes & Dislikes", null, () =>
                    {
                        // Likes section with thumbs up icon
                        if (!string.IsNullOrEmpty(likesData))
                        {
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.9f, 0.5f, 1.0f));
                            ImGui.Text("👍 Likes");
                            ImGui.PopStyleColor();

                            var likesItems = likesData.Split(',', '\n').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                            foreach (var like in likesItems)
                            {
                                DrawLikesDislikesTraitItem(like, scale, true);
                            }

                            if (!string.IsNullOrEmpty(dislikesData))
                            {
                                ImGui.Dummy(new Vector2(0, 8 * scale));
                            }
                        }

                        // Dislikes section with thumbs down icon
                        if (!string.IsNullOrEmpty(dislikesData))
                        {
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.5f, 0.5f, 1.0f));
                            ImGui.Text("👎 Dislikes");
                            ImGui.PopStyleColor();

                            var dislikesItems = dislikesData.Split(',', '\n').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                            foreach (var dislike in dislikesItems)
                            {
                                DrawLikesDislikesTraitItem(dislike, scale, false);
                            }
                        }
                    }, scale);

                    // Proper spacing between cards
                    ImGui.Dummy(new Vector2(0, 24 * scale));
                }

                // External Links - only show if data exists
                if (!string.IsNullOrEmpty(rp.Links))
                {
                    DrawCard("External Links", null, () =>
                    {
                        DrawExternalLink(rp.Links, scale);
                    }, scale);
                }
            }
        }
        
        private void DrawCard(string title, string? subtitle, Action content, float scale, bool isMainContent = false)
        {
            var profileColor = ResolveNameplateColor();
            var cardId = $"##{title}Card";

            // Calculate base card height dynamically
            float headerHeight = 60 * scale;
            float minContentHeight = isMainContent ? 120 * scale : 80 * scale;
            float padding = 20 * scale;

            float baseCardHeight;
            if (isMainContent)
            {
                baseCardHeight = 200 * scale;
            }
            else
            {
                if (title == "Quick Info")
                    baseCardHeight = 250 * scale;
                else if (title == "Additional Details")
                    baseCardHeight = 180 * scale;
                else if (title == "Key Traits")
                    baseCardHeight = 200 * scale;
                else if (title == "Likes & Dislikes")
                    baseCardHeight = 200 * scale;
                else if (title == "External Links")
                    baseCardHeight = 120 * scale;
                else
                    baseCardHeight = 150 * scale;
            }

            // Measure content height using a temporary child window that matches the card's
            // exact layout (same padding, border) so GetContentRegionAvail() returns the
            // correct width and pills/text wrap identically to the real card
            float actualContentHeight = 0f;
            var tempCursor = ImGui.GetCursorPos();

            // Invisible styles
            ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.Border, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Vector4.Zero);

            // Match the card's padding and border so content width is identical
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(24 * scale, 20 * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1.0f);

            ImGui.BeginChild("##measure" + cardId, new Vector2(0, 5000 * scale), true,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoMouseInputs | ImGuiWindowFlags.NoNav);

            // Clip drawList inside measurement child to hide any drawList rendering
            var measureDl = ImGui.GetWindowDrawList();
            measureDl.PushClipRect(Vector2.Zero, Vector2.Zero);

            var startY = ImGui.GetCursorPosY();
            content();
            actualContentHeight = ImGui.GetCursorPosY() - startY;

            measureDl.PopClipRect();
            ImGui.EndChild();

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(8);
            ImGui.SetCursorPos(tempCursor);

            float breathingRoom = 20 * scale;
            float requiredHeight = headerHeight + actualContentHeight + padding + breathingRoom;

            float growthMultiplier = 1.5f;
            if (title == "Character Timeline" || title == "Timeline" ||
                (isMainContent && title.Contains("Timeline")))
            {
                growthMultiplier = 2.5f;
            }

            float maxCardHeight = baseCardHeight * growthMultiplier;

            float finalCardHeight;
            var flags = ImGuiWindowFlags.None;

            if (requiredHeight <= maxCardHeight)
            {
                finalCardHeight = Math.Max(baseCardHeight, requiredHeight);
                flags = ImGuiWindowFlags.NoScrollbar;
            }
            else
            {
                finalCardHeight = maxCardHeight;
                flags = ImGuiWindowFlags.AlwaysVerticalScrollbar;
            }

            // Card styling - clean like HTML
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.067f, 0.067f, 0.067f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.102f, 0.102f, 0.102f, 1.0f));
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8.0f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(24 * scale, 20 * scale));
            
            ImGui.BeginChild(cardId, new Vector2(0, finalCardHeight), true, flags);
            
            // Now draw the glow effect on the child window's draw list after background is rendered
            var drawList = ImGui.GetWindowDrawList();
            var cardStartPos = ImGui.GetWindowPos(); // Child window position
            var lineHeight = 1.5f * scale;
            var glowRadius = 12f * scale;
            var cardWidth = ImGui.GetWindowSize().X;
            var glowInset = 2f * scale; // Distance from top edge
            
            // Create a proper glow effect that fades from edges to center
            var glowFadeWidth = cardWidth * 0.25f; // 25% of width for fade zones
            var glowCoreWidth = cardWidth * 0.5f; // 50% of width for bright core
            var glowCoreStart = (cardWidth - glowCoreWidth) * 0.5f;
            
            // Vertical glow properties
            var glowFadeHeight = 8f * scale;
            var coreLineHeight = 1f * scale; // Thinner core line
            
            // Left fade zone (transparent to bright)
            drawList.AddRectFilledMultiColor(
                cardStartPos + new Vector2(0, glowInset),
                cardStartPos + new Vector2(glowCoreStart, glowInset + coreLineHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X, profileColor.Y, profileColor.Z, 0.0f)), // Transparent left
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X, profileColor.Y, profileColor.Z, 0.9f)), // Bright right
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X, profileColor.Y, profileColor.Z, 0.9f)), // Bright right
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X, profileColor.Y, profileColor.Z, 0.0f))  // Transparent left
            );
            
            // Central core (full brightness)
            drawList.AddRectFilled(
                cardStartPos + new Vector2(glowCoreStart, glowInset),
                cardStartPos + new Vector2(glowCoreStart + glowCoreWidth, glowInset + coreLineHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X, profileColor.Y, profileColor.Z, 1.0f))
            );
            
            // Right fade zone (bright to transparent)
            drawList.AddRectFilledMultiColor(
                cardStartPos + new Vector2(glowCoreStart + glowCoreWidth, glowInset),
                cardStartPos + new Vector2(cardWidth, glowInset + coreLineHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X, profileColor.Y, profileColor.Z, 0.9f)), // Bright left
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X, profileColor.Y, profileColor.Z, 0.0f)), // Transparent right
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X, profileColor.Y, profileColor.Z, 0.0f)), // Transparent right
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X, profileColor.Y, profileColor.Z, 0.9f))  // Bright left
            );
            
            // Soft downward glow - left fade
            drawList.AddRectFilledMultiColor(
                cardStartPos + new Vector2(0, glowInset + coreLineHeight),
                cardStartPos + new Vector2(glowCoreStart, glowInset + coreLineHeight + glowFadeHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X * 0.6f, profileColor.Y * 0.6f, profileColor.Z * 0.6f, 0.0f)), // Transparent
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X * 0.6f, profileColor.Y * 0.6f, profileColor.Z * 0.6f, 0.4f)), // Soft glow
                ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0)), // Transparent bottom
                ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0))  // Transparent bottom
            );
            
            // Soft downward glow - center
            drawList.AddRectFilledMultiColor(
                cardStartPos + new Vector2(glowCoreStart, glowInset + coreLineHeight),
                cardStartPos + new Vector2(glowCoreStart + glowCoreWidth, glowInset + coreLineHeight + glowFadeHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X * 0.7f, profileColor.Y * 0.7f, profileColor.Z * 0.7f, 0.5f)), // Bright top
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X * 0.7f, profileColor.Y * 0.7f, profileColor.Z * 0.7f, 0.5f)), // Bright top
                ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0)), // Transparent bottom
                ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0))  // Transparent bottom
            );
            
            // Soft downward glow - right fade
            drawList.AddRectFilledMultiColor(
                cardStartPos + new Vector2(glowCoreStart + glowCoreWidth, glowInset + coreLineHeight),
                cardStartPos + new Vector2(cardWidth, glowInset + coreLineHeight + glowFadeHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X * 0.6f, profileColor.Y * 0.6f, profileColor.Z * 0.6f, 0.4f)), // Soft glow
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X * 0.6f, profileColor.Y * 0.6f, profileColor.Z * 0.6f, 0.0f)), // Transparent
                ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0)), // Transparent bottom
                ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0))  // Transparent bottom
            );
            
            // Title with proper sizing  
            // Card title - positioned to account for glow line
            ImGui.SetCursorPos(new Vector2(24 * scale, 24 * scale));
            ImGui.BeginGroup();
            
            // Title text with header font
            using (Plugin.Instance?.HeaderFont?.Push())
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.95f, 0.97f, 1.0f));
                ImGui.Text(title);
                ImGui.PopStyleColor();
            }
            
            if (!string.IsNullOrEmpty(subtitle))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.502f, 0.518f, 0.549f, 1.0f));
                ImGui.PushTextWrapPos(ImGui.GetWindowWidth() - 24 * scale);
                ImGui.TextWrapped(subtitle);
                ImGui.PopTextWrapPos();
                ImGui.PopStyleColor();
            }
            ImGui.EndGroup();
            
            // Content area with spacing adjusted for left side overlap
            var contentY = isMainContent ? 85 * scale : 64 * scale; // More spacing for main content
            ImGui.SetCursorPos(new Vector2(24 * scale, contentY));
            ImGui.BeginGroup();
            ImGui.PushTextWrapPos(ImGui.GetWindowWidth() - 24 * scale);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.824f, 0.835f, 0.863f, 1.0f));
            
            content();
            
            ImGui.PopStyleColor();
            ImGui.PopTextWrapPos();
            ImGui.EndGroup();
            
            ImGui.EndChild();
            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor(2);
        }
        
        private void DrawGalleryGrid(RPProfile rp, float scale)
        {
            // Gallery grid layout with thumbnails
            if (rp.GalleryImages?.Count > 0)
            {
                // Use Alias if set, otherwise fall back to Name
                var characterName = showingExternal
                    ? rp.CharacterName
                    : (!string.IsNullOrWhiteSpace(character?.Alias) ? character.Alias : character?.Name);
                var headerText = !string.IsNullOrEmpty(characterName) ? $"{characterName}'s Gallery" : "Character Gallery";
                
                // Use exact header styling matching Overview tab cards
                using (Plugin.Instance?.HeaderFont?.Push())
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.95f, 0.97f, 1.0f));
                    ImGui.Text(headerText);
                    ImGui.PopStyleColor();
                }
                ImGui.Spacing();
                
                // Calculate grid dimensions first
                var availableWidth = ImGui.GetContentRegionAvail().X;
                var containerPadding = 20f * scale;
                var spacing = 16f * scale; // Spacing between images
                var columns = 4; // Fixed 4 columns
                
                // Calculate usable width inside container (subtract padding from both sides)
                var usableWidth = availableWidth - (containerPadding * 2);
                var thumbnailSize = (usableWidth - (spacing * (columns - 1))) / columns; // Fill usable width
                
                // Calculate total grid width and centering offset (push right slightly)
                var totalGridWidth = (thumbnailSize * columns) + (spacing * (columns - 1));
                var centeringOffset = (availableWidth - totalGridWidth) / 2f + (8f * scale);
                
                // Calculate container height needed for all images
                var rows = (int)Math.Ceiling((double)rp.GalleryImages.Count / columns);
                var containerHeight = (rows * thumbnailSize) + ((rows - 1) * spacing) + (containerPadding * 2);
                
                // Draw container background with proper glow effect like Overview tab boxes
                var dl = ImGui.GetWindowDrawList();
                var containerPos = ImGui.GetCursorScreenPos();
                var containerSize = new Vector2(availableWidth, containerHeight);
                
                // Container background (dark)
                dl.AddRectFilled(
                    containerPos,
                    containerPos + containerSize,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.067f, 0.067f, 0.067f, 1.0f)),
                    8.0f * scale
                );
                
                // Container border
                dl.AddRect(
                    containerPos,
                    containerPos + containerSize,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.102f, 0.102f, 0.102f, 1.0f)),
                    8.0f * scale,
                    ImDrawFlags.None,
                    1.0f
                );
                
                // Add proper glow effect on top of container like Overview tab boxes
                var accentColor = ResolveNameplateColor();
                var glowInset = 2f * scale;
                var cardWidth = availableWidth;
                
                // Create a proper glow effect that fades from edges to center
                var glowFadeWidth = cardWidth * 0.25f; // 25% of width for fade zones
                var glowCoreWidth = cardWidth * 0.5f; // 50% of width for bright core
                var glowCoreStart = (cardWidth - glowCoreWidth) * 0.5f;
                
                // Vertical glow properties
                var glowFadeHeight = 8f * scale;
                var coreLineHeight = 1f * scale; // Thinner core line
                
                // Left fade zone (transparent to bright)
                dl.AddRectFilledMultiColor(
                    containerPos + new Vector2(0, glowInset),
                    containerPos + new Vector2(glowCoreStart, glowInset + coreLineHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.0f)), // Transparent left
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.9f)), // Bright right
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.9f)), // Bright right
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.0f))  // Transparent left
                );
                
                // Central core (full brightness)
                dl.AddRectFilled(
                    containerPos + new Vector2(glowCoreStart, glowInset),
                    containerPos + new Vector2(glowCoreStart + glowCoreWidth, glowInset + coreLineHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 1.0f))
                );
                
                // Right fade zone (bright to transparent)
                dl.AddRectFilledMultiColor(
                    containerPos + new Vector2(glowCoreStart + glowCoreWidth, glowInset),
                    containerPos + new Vector2(cardWidth, glowInset + coreLineHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.9f)), // Bright left
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.0f)), // Transparent right
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.0f)), // Transparent right
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.9f))  // Bright left
                );
                
                // Soft downward glow - left fade
                dl.AddRectFilledMultiColor(
                    containerPos + new Vector2(0, glowInset + coreLineHeight),
                    containerPos + new Vector2(glowCoreStart, glowInset + coreLineHeight + glowFadeHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X * 0.6f, accentColor.Y * 0.6f, accentColor.Z * 0.6f, 0.0f)), // Transparent
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X * 0.6f, accentColor.Y * 0.6f, accentColor.Z * 0.6f, 0.4f)), // Soft glow
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0)), // Transparent bottom
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0))  // Transparent bottom
                );
                
                // Soft downward glow - center
                dl.AddRectFilledMultiColor(
                    containerPos + new Vector2(glowCoreStart, glowInset + coreLineHeight),
                    containerPos + new Vector2(glowCoreStart + glowCoreWidth, glowInset + coreLineHeight + glowFadeHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X * 0.7f, accentColor.Y * 0.7f, accentColor.Z * 0.7f, 0.5f)), // Bright top
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X * 0.7f, accentColor.Y * 0.7f, accentColor.Z * 0.7f, 0.5f)), // Bright top
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0)), // Transparent bottom
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0))  // Transparent bottom
                );
                
                // Soft downward glow - right fade
                dl.AddRectFilledMultiColor(
                    containerPos + new Vector2(glowCoreStart + glowCoreWidth, glowInset + coreLineHeight),
                    containerPos + new Vector2(cardWidth, glowInset + coreLineHeight + glowFadeHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X * 0.6f, accentColor.Y * 0.6f, accentColor.Z * 0.6f, 0.4f)), // Soft glow
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X * 0.6f, accentColor.Y * 0.6f, accentColor.Z * 0.6f, 0.0f)), // Transparent
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0)), // Transparent bottom
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0))  // Transparent bottom
                );
                
                // Reserve space for the container and position cursor properly
                ImGui.Dummy(new Vector2(availableWidth, containerPadding)); // Top padding
                
                for (int i = 0; i < rp.GalleryImages.Count; i++)
                {
                    if (i % columns == 0)
                    {
                        // Start of new row - use calculated centering offset
                        ImGui.SetCursorPosX(centeringOffset);
                    }
                    else
                    {
                        ImGui.SameLine();
                    }
                    
                    var galleryImage = rp.GalleryImages[i];
                    
                    // Use the exact same approach as profile images
                    var imagePath = GetGalleryImagePath(galleryImage.Url);
                    IDalamudTextureWrap? texture = null;
                    
                    if (!string.IsNullOrEmpty(imagePath))
                    {
                        if (File.Exists(imagePath))
                        {
                            imageStatus[galleryImage.Url] = $"File exists: {Path.GetFileName(imagePath)}";
                            texture = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
                            if (texture != null)
                            {
                                imageStatus[galleryImage.Url] = "Loaded successfully!";
                            }
                            else
                            {
                                imageStatus[galleryImage.Url] = $"GetWrapOrDefault null: {Path.GetFileName(imagePath)}";
                            }
                        }
                        else
                        {
                            imageStatus[galleryImage.Url] = $"File missing: {Path.GetFileName(imagePath)}";
                        }
                    }
                    else
                    {
                        imageStatus[galleryImage.Url] = "No image path returned";
                    }
                    
                    bool isLoading = downloadingImages.Contains(galleryImage.Url);
                    
                    if (texture != null)
                    {
                        // Draw actual image
                        var cursorPos = ImGui.GetCursorScreenPos();
                        
                        // Apply individual gallery image zoom and offset settings
                        var aspectRatio = (float)texture.Width / texture.Height;
                        
                        // Use the individual image's zoom and offset settings
                        float zoom = Math.Clamp(galleryImage.Zoom, 0.1f, 10.0f);
                        Vector2 userOffset = galleryImage.Offset * scale;
                        
                        // Calculate image size with zoom applied
                        Vector2 imageSize;
                        if (aspectRatio > 1f)
                        {
                            // Image is wider - fit height, scale width
                            imageSize.Y = thumbnailSize * zoom;
                            imageSize.X = imageSize.Y * aspectRatio;
                        }
                        else
                        {
                            // Image is taller or square - fit width, scale height
                            imageSize.X = thumbnailSize * zoom;
                            imageSize.Y = imageSize.X / aspectRatio;
                        }
                        
                        // Center the zoomed image and apply user offset
                        Vector2 imageOffset = new Vector2(
                            -(imageSize.X - thumbnailSize) * 0.5f + userOffset.X,
                            -(imageSize.Y - thumbnailSize) * 0.5f + userOffset.Y
                        );
                        
                        var imageDl = ImGui.GetWindowDrawList();
                        
                        // Draw subtle drop shadow
                        imageDl.AddRectFilled(
                            cursorPos + new Vector2(3 * scale, 3 * scale), 
                            cursorPos + new Vector2(thumbnailSize + 3 * scale, thumbnailSize + 3 * scale),
                            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.3f)), 
                            6f * scale
                        );
                        
                        // Draw main background (dark)
                        imageDl.AddRectFilled(
                            cursorPos, 
                            cursorPos + new Vector2(thumbnailSize, thumbnailSize),
                            ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f)), 
                            6f * scale
                        );
                        
                        // Clip image to thumbnail bounds with slight inset for border
                        var imageInset = 3f * scale;
                        var imagePos = cursorPos + new Vector2(imageInset);
                        var imageDisplaySize = thumbnailSize - (imageInset * 2);
                        imageDl.PushClipRect(imagePos, imagePos + new Vector2(imageDisplaySize), true);
                        
                        // Adjust image position for the inset
                        var adjustedImageOffset = imageOffset + new Vector2(imageInset);
                        var adjustedImageSize = imageSize * (imageDisplaySize / thumbnailSize); // Scale image size proportionally
                        
                        // Draw image
                        imageDl.AddImage((ImTextureID)texture.Handle, 
                                  imagePos + imageOffset * (imageDisplaySize / thumbnailSize), 
                                  imagePos + imageOffset * (imageDisplaySize / thumbnailSize) + adjustedImageSize);
                        
                        imageDl.PopClipRect();
                        
                        // Draw elegant border frame
                        imageDl.AddRect(
                            cursorPos, 
                            cursorPos + new Vector2(thumbnailSize, thumbnailSize),
                            ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.8f, 0.9f, 0.9f)), 
                            6f * scale,
                            ImDrawFlags.None,
                            2f * scale
                        );
                        
                        // Add inner highlight
                        imageDl.AddRect(
                            cursorPos + new Vector2(1f * scale), 
                            cursorPos + new Vector2(thumbnailSize - 1f * scale, thumbnailSize - 1f * scale),
                            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.1f)), 
                            5f * scale,
                            ImDrawFlags.None,
                            1f * scale
                        );
                        
                        // Invisible button for click detection
                        ImGui.InvisibleButton($"##gallery_{i}", new Vector2(thumbnailSize, thumbnailSize));
                        
                        // Add hover effect
                        bool isHovered = ImGui.IsItemHovered();
                        if (isHovered)
                        {
                            // Hover glow effect - brighter version of the accent color
                            var hoverAccentColor = ResolveNameplateColor();
                            var hoverGlowColor = new Vector4(hoverAccentColor.X, hoverAccentColor.Y, hoverAccentColor.Z, 0.4f);
                            imageDl.AddRect(
                                cursorPos - new Vector2(2f * scale), 
                                cursorPos + new Vector2(thumbnailSize + 2f * scale, thumbnailSize + 2f * scale),
                                ImGui.ColorConvertFloat4ToU32(hoverGlowColor), 
                                6f * scale,
                                ImDrawFlags.None,
                                3f * scale
                            );
                            
                            // Subtle highlight overlay
                            imageDl.AddRectFilled(
                                cursorPos, 
                                cursorPos + new Vector2(thumbnailSize, thumbnailSize),
                                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.1f)), 
                                6f * scale
                            );
                            
                            // Image name overlay - thin bar at bottom
                            if (!string.IsNullOrWhiteSpace(galleryImage.Name))
                            {
                                var nameBarHeight = 20f * scale;
                                var nameBarPos = cursorPos + new Vector2(0, thumbnailSize - nameBarHeight);
                                var nameBarSize = new Vector2(thumbnailSize, nameBarHeight);
                                
                                // Semi-transparent dark background
                                imageDl.AddRectFilled(
                                    nameBarPos,
                                    nameBarPos + nameBarSize,
                                    ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.7f))
                                );
                                
                                // Draw the name text using draw list to avoid layout issues
                                var textPos = nameBarPos + new Vector2(4f * scale, 2f * scale);
                                imageDl.AddText(textPos, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f)), galleryImage.Name);
                            }
                        }
                        
                        if (ImGui.IsItemClicked())
                        {
                            // Build list of available images and set current index
                            availableImagePaths.Clear();
                            currentPreviewIndex = 0;
                            
                            for (int j = 0; j < rp.GalleryImages.Count; j++)
                            {
                                var availableImagePath = GetGalleryImagePath(rp.GalleryImages[j].Url);
                                if (!string.IsNullOrEmpty(availableImagePath) && File.Exists(availableImagePath))
                                {
                                    availableImagePaths.Add(availableImagePath);
                                    if (j == i) // Current clicked image
                                    {
                                        currentPreviewIndex = availableImagePaths.Count - 1;
                                    }
                                }
                            }
                            
                            imagePreviewUrl = imagePath; // Use the local file path, not the URL
                            showImagePreview = true;
                        }
                    }
                    else
                    {
                        // Show button with status info
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
                        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
                        
                        var status = imageStatus.GetValueOrDefault(galleryImage.Url, "Load Image");
                        bool clicked = ImGui.Button($"{status}##gallery_{i}", new Vector2(thumbnailSize, thumbnailSize));
                        
                        ImGui.PopStyleColor(2);
                        
                        // Start download when button is clicked OR automatically on first render
                        if (clicked || (!galleryImageComplete.ContainsKey(galleryImage.Url) && !downloadingImages.Contains(galleryImage.Url)))
                        {
                            _ = Task.Run(() => StartImageDownload(galleryImage.Url));
                        }
                    }
                    
                }
                
                // Add bottom padding and advance cursor
                ImGui.Dummy(new Vector2(availableWidth, containerPadding + 10f * scale));
                
            }
            else
            {
                // Empty state - also with header and container
                // Use Alias if set, otherwise fall back to Name
                var characterName = showingExternal
                    ? rp.CharacterName
                    : (!string.IsNullOrWhiteSpace(character?.Alias) ? character.Alias : character?.Name);
                var headerText = !string.IsNullOrEmpty(characterName) ? $"{characterName}'s Gallery" : "Character Gallery";
                
                // Use exact header styling matching Overview tab cards
                using (Plugin.Instance?.HeaderFont?.Push())
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.95f, 0.97f, 1.0f));
                    ImGui.Text(headerText);
                    ImGui.PopStyleColor();
                }
                ImGui.Spacing();
                
                // Empty state with same container style as main gallery
                var availableWidth = ImGui.GetContentRegionAvail().X;
                var containerPadding = 20f * scale;
                var containerHeight = 200f * scale; // Smaller height for empty state
                
                // Draw container background with proper glow effect like Overview tab boxes
                var dl = ImGui.GetWindowDrawList();
                var containerPos = ImGui.GetCursorScreenPos();
                var containerSize = new Vector2(availableWidth, containerHeight);
                
                // Container background (dark)
                dl.AddRectFilled(
                    containerPos,
                    containerPos + containerSize,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.067f, 0.067f, 0.067f, 1.0f)),
                    8.0f * scale
                );
                
                // Container border
                dl.AddRect(
                    containerPos,
                    containerPos + containerSize,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.102f, 0.102f, 0.102f, 1.0f)),
                    8.0f * scale,
                    ImDrawFlags.None,
                    1.0f
                );
                
                // Add proper glow effect on top of container like Overview tab boxes
                var accentColor = ResolveNameplateColor();
                var glowInset = 2f * scale;
                var cardWidth = availableWidth;
                
                // Create a proper glow effect that fades from edges to center
                var glowFadeWidth = cardWidth * 0.25f; // 25% of width for fade zones
                var glowCoreWidth = cardWidth * 0.5f; // 50% of width for bright core
                var glowCoreStart = (cardWidth - glowCoreWidth) * 0.5f;
                
                // Vertical glow properties
                var glowFadeHeight = 8f * scale;
                var coreLineHeight = 1f * scale; // Thinner core line
                
                // Left fade zone (transparent to bright)
                dl.AddRectFilledMultiColor(
                    containerPos + new Vector2(0, glowInset),
                    containerPos + new Vector2(glowCoreStart, glowInset + coreLineHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.0f)), // Transparent left
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.9f)), // Bright right
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.9f)), // Bright right
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.0f))  // Transparent left
                );
                
                // Central core (full brightness)
                dl.AddRectFilled(
                    containerPos + new Vector2(glowCoreStart, glowInset),
                    containerPos + new Vector2(glowCoreStart + glowCoreWidth, glowInset + coreLineHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 1.0f))
                );
                
                // Right fade zone (bright to transparent)
                dl.AddRectFilledMultiColor(
                    containerPos + new Vector2(glowCoreStart + glowCoreWidth, glowInset),
                    containerPos + new Vector2(cardWidth, glowInset + coreLineHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.9f)), // Bright left
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.0f)), // Transparent right
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.0f)), // Transparent right
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.9f))  // Bright left
                );
                
                // Soft downward glow - left fade
                dl.AddRectFilledMultiColor(
                    containerPos + new Vector2(0, glowInset + coreLineHeight),
                    containerPos + new Vector2(glowCoreStart, glowInset + coreLineHeight + glowFadeHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X * 0.6f, accentColor.Y * 0.6f, accentColor.Z * 0.6f, 0.0f)), // Transparent
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X * 0.6f, accentColor.Y * 0.6f, accentColor.Z * 0.6f, 0.4f)), // Soft glow
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0)), // Transparent bottom
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0))  // Transparent bottom
                );
                
                // Soft downward glow - center
                dl.AddRectFilledMultiColor(
                    containerPos + new Vector2(glowCoreStart, glowInset + coreLineHeight),
                    containerPos + new Vector2(glowCoreStart + glowCoreWidth, glowInset + coreLineHeight + glowFadeHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X * 0.7f, accentColor.Y * 0.7f, accentColor.Z * 0.7f, 0.5f)), // Bright top
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X * 0.7f, accentColor.Y * 0.7f, accentColor.Z * 0.7f, 0.5f)), // Bright top
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0)), // Transparent bottom
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0))  // Transparent bottom
                );
                
                // Soft downward glow - right fade
                dl.AddRectFilledMultiColor(
                    containerPos + new Vector2(glowCoreStart + glowCoreWidth, glowInset + coreLineHeight),
                    containerPos + new Vector2(cardWidth, glowInset + coreLineHeight + glowFadeHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X * 0.6f, accentColor.Y * 0.6f, accentColor.Z * 0.6f, 0.4f)), // Soft glow
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X * 0.6f, accentColor.Y * 0.6f, accentColor.Z * 0.6f, 0.0f)), // Transparent
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0)), // Transparent bottom
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0))  // Transparent bottom
                );
                
                // Center empty state text in container using relative positioning
                ImGui.Dummy(new Vector2(availableWidth, containerHeight / 2 - 15f * scale)); // Top padding to center vertically
                
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.631f, 0.631f, 0.667f, 1.0f));
                var text = "No gallery images added yet. Use the profile editor to add image URLs to showcase your character!";
                var availableTextWidth = availableWidth - (containerPadding * 2);
                var textSize = ImGui.CalcTextSize(text, true, availableTextWidth);
                var centerX = containerPadding + (availableTextWidth - textSize.X) * 0.5f;
                
                ImGui.SetCursorPosX(centerX);
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + availableTextWidth);
                ImGui.TextWrapped(text);
                ImGui.PopTextWrapPos();
                ImGui.PopStyleColor();
                
                // Add remaining bottom padding
                ImGui.Dummy(new Vector2(availableWidth, containerHeight / 2 - textSize.Y / 2 + 10f * scale));
            }
        }
        
        private void DrawQuickInfo(RPProfile rp, float scale)
        {
            var availWidth = ImGui.GetContentRegionAvail().X;
            var cardWidth = (availWidth - 16 * scale) / 2;
            var cardHeight = 75f * scale;
            var spacing = 20f * scale;
            
            // Helper to draw a clean info card
            void DrawInfoCard(string label, string value, float xPos, float yPos)
            {
                var drawList = ImGui.GetWindowDrawList();
                var cardPos = ImGui.GetCursorScreenPos() + new Vector2(xPos, yPos);
                
                // Card background with gradient
                drawList.AddRectFilledMultiColor(
                    cardPos,
                    cardPos + new Vector2(cardWidth, cardHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.055f, 0.055f, 0.055f, 1.0f)),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.055f, 0.055f, 0.055f, 1.0f)),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.039f, 0.039f, 0.039f, 1.0f)),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.039f, 0.039f, 0.039f, 1.0f))
                );
                
                // Card border with subtle glow
                drawList.AddRect(
                    cardPos,
                    cardPos + new Vector2(cardWidth, cardHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.11f, 0.11f, 0.11f, 1.0f)),
                    8f * scale,
                    ImDrawFlags.None,
                    1f * scale
                );
                
                // Inner highlight
                drawList.AddRect(
                    cardPos + new Vector2(1, 1),
                    cardPos + new Vector2(cardWidth - 1, cardHeight - 1),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 0.02f)),
                    7f * scale,
                    ImDrawFlags.None,
                    1f * scale
                );
                
                // Label - uppercase, slightly larger
                var labelUpper = label.ToUpper();
                var labelSize = ImGui.CalcTextSize(labelUpper);
                var scaledLabelSize = labelSize * 0.85f;
                
                // Label - smaller, uppercase
                drawList.AddText(
                    cardPos + new Vector2((cardWidth - scaledLabelSize.X) * 0.5f, 20 * scale),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.455f, 0.467f, 0.490f, 1.0f)),
                    labelUpper
                );
                
                // Value - normal size white text
                var valueSize = ImGui.CalcTextSize(value);
                drawList.AddText(
                    cardPos + new Vector2((cardWidth - valueSize.X) * 0.5f, 40 * scale),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.941f, 0.941f, 0.949f, 1.0f)),
                    value
                );
            }
            
            // Draw only first 4 items in 2x2 grid
            DrawInfoCard("AGE", rp.Age ?? "Unknown", 0, 0);
            DrawInfoCard("RACE", rp.Race ?? "Unknown", cardWidth + spacing, 0);
            
            DrawInfoCard("GENDER", rp.Gender ?? "Unknown", 0, cardHeight + spacing);
            DrawInfoCard("ORIENTATION", rp.Orientation ?? "Unknown", cardWidth + spacing, cardHeight + spacing);
        }
        
        private void DrawAdditionalInfo(RPProfile rp, float scale)
        {
            var availWidth = ImGui.GetContentRegionAvail().X;
            var cardWidth = (availWidth - 12 * scale) / 2;
            var cardHeight = 70f * scale;
            var spacing = 12f * scale;
            
            void DrawInfoCard(string label, string value, float xPos, float yPos)
            {
                var drawList = ImGui.GetWindowDrawList();
                var cardPos = ImGui.GetCursorScreenPos() + new Vector2(xPos, yPos);
                
                // Same card style as Quick Info
                drawList.AddRectFilledMultiColor(
                    cardPos,
                    cardPos + new Vector2(cardWidth, cardHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.055f, 0.055f, 0.055f, 1.0f)),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.055f, 0.055f, 0.055f, 1.0f)),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.039f, 0.039f, 0.039f, 1.0f)),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.039f, 0.039f, 0.039f, 1.0f))
                );
                
                drawList.AddRect(
                    cardPos,
                    cardPos + new Vector2(cardWidth, cardHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.11f, 0.11f, 0.11f, 1.0f)),
                    8f * scale,
                    ImDrawFlags.None,
                    1f * scale
                );
                
                drawList.AddRect(
                    cardPos + new Vector2(1, 1),
                    cardPos + new Vector2(cardWidth - 1, cardHeight - 1),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 0.02f)),
                    7f * scale,
                    ImDrawFlags.None,
                    1f * scale
                );
                
                var labelUpper = label.ToUpper();
                var labelSize = ImGui.CalcTextSize(labelUpper);
                var scaledLabelSize = labelSize * 0.85f;
                
                // Label - normal size
                drawList.AddText(
                    cardPos + new Vector2((cardWidth - scaledLabelSize.X) * 0.5f, 20 * scale),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.455f, 0.467f, 0.490f, 1.0f)),
                    labelUpper
                );
                
                // Value - normal size
                var valueSize = ImGui.CalcTextSize(value);
                drawList.AddText(
                    cardPos + new Vector2((cardWidth - valueSize.X) * 0.5f, 40 * scale),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.941f, 0.941f, 0.949f, 1.0f)),
                    value
                );
            }
            
            // Collect all items to display
            var items = new List<(string label, string value)>();

            if (!string.IsNullOrEmpty(rp.Relationship))
                items.Add(("RELATIONSHIP", rp.Relationship));

            if (!string.IsNullOrEmpty(rp.Occupation))
                items.Add(("OCCUPATION", rp.Occupation));

            // Add custom key-value pairs
            if (!string.IsNullOrEmpty(rp.AdditionalDetailsCustom))
            {
                foreach (var line in rp.AdditionalDetailsCustom.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;

                    var colonIndex = trimmed.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        var key = trimmed.Substring(0, colonIndex).Trim().ToUpper();
                        var value = trimmed.Substring(colonIndex + 1).Trim();
                        if (!string.IsNullOrWhiteSpace(value))
                            items.Add((key, value));
                    }
                }
            }

            // Draw all items in a grid (2 columns)
            for (int i = 0; i < items.Count; i++)
            {
                var col = i % 2;
                var row = i / 2;
                var xPos = col == 0 ? 0 : cardWidth + spacing;
                var yPos = row * (cardHeight + spacing);
                DrawInfoCard(items[i].label, items[i].value, xPos, yPos);
            }

            // Reserve space for all rows
            var rowCount = (items.Count + 1) / 2;
            ImGui.Dummy(new Vector2(availWidth, rowCount * (cardHeight + spacing)));
        }
        
        private void DrawExternalLink(string link, float scale)
        {
            var drawList = ImGui.GetWindowDrawList();
            var linkPos = ImGui.GetCursorScreenPos();
            
            // Link styling - clickable area with icon
            var linkSize = new Vector2(ImGui.GetContentRegionAvail().X, 40 * scale);
            
            // Hover detection
            var mousePos = ImGui.GetIO().MousePos;
            var isHovered = mousePos.X >= linkPos.X && mousePos.X <= linkPos.X + linkSize.X &&
                           mousePos.Y >= linkPos.Y && mousePos.Y <= linkPos.Y + linkSize.Y;
            
            // Background on hover
            if (isHovered)
            {
                drawList.AddRectFilled(
                    linkPos,
                    linkPos + linkSize,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 0.5f)),
                    6f * scale
                );
            }
            
            // External link icon
            var iconPos = linkPos + new Vector2(14 * scale, linkSize.Y * 0.5f);
            var iconSize = 16f * scale;
            var profileColor = ResolveNameplateColor();
            
            // Arrow icon
            drawList.AddLine(
                iconPos + new Vector2(0, -iconSize * 0.3f),
                iconPos + new Vector2(iconSize * 0.6f, -iconSize * 0.3f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X, profileColor.Y, profileColor.Z, 1.0f)),
                2f * scale
            );
            drawList.AddLine(
                iconPos + new Vector2(iconSize * 0.6f, -iconSize * 0.3f),
                iconPos + new Vector2(iconSize * 0.6f, iconSize * 0.3f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X, profileColor.Y, profileColor.Z, 1.0f)),
                2f * scale
            );
            drawList.AddLine(
                iconPos + new Vector2(iconSize * 0.2f, 0),
                iconPos + new Vector2(iconSize * 0.6f, -iconSize * 0.3f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X, profileColor.Y, profileColor.Z, 1.0f)),
                2f * scale
            );
            
            // Link text
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 40 * scale);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (linkSize.Y - ImGui.GetTextLineHeight()) * 0.5f);
            ImGui.TextColored(new Vector4(0.784f, 0.812f, 0.878f, 1.0f), link);
            
            // Click handling
            if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                OpenUrl(link);
            }
            
            if (isHovered)
            {
                ImGui.SetTooltip("Click to open in browser");
            }
            
            // Move cursor past the link
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + linkSize.Y * 0.5f);
        }
        
        private void DrawAbilityItem(string ability, float scale)
        {
            var profileColor = ResolveNameplateColor();
            var drawList = ImGui.GetWindowDrawList();
            
            // Ability box background
            var startPos = ImGui.GetCursorScreenPos();
            var boxSize = new Vector2(ImGui.GetContentRegionAvail().X, 60 * scale);
            
            // Hover detection
            var mousePos = ImGui.GetIO().MousePos;
            bool isHovered = mousePos.X >= startPos.X && mousePos.X <= startPos.X + boxSize.X &&
                           mousePos.Y >= startPos.Y && mousePos.Y <= startPos.Y + boxSize.Y;
            
            // Draw background with hover effect
            var bgColor = isHovered
                ? new Vector4(0.08f, 0.08f, 0.08f, 1.0f)
                : new Vector4(0.05f, 0.05f, 0.05f, 0.8f);
                
            drawList.AddRectFilled(
                startPos,
                startPos + boxSize,
                ImGui.ColorConvertFloat4ToU32(bgColor),
                6f * scale
            );
            
            // Border
            drawList.AddRect(
                startPos,
                startPos + boxSize,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.15f, isHovered ? 1.0f : 0.6f)),
                6f * scale,
                ImDrawFlags.None,
                1f * scale
            );
            
            // Left accent bar
            drawList.AddRectFilled(
                startPos,
                startPos + new Vector2(4 * scale, boxSize.Y),
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X * 0.8f, profileColor.Y * 0.8f, profileColor.Z * 0.8f, 1.0f))
            );
            
            // Content
            ImGui.SetCursorScreenPos(startPos + new Vector2(20 * scale, 15 * scale));
            ImGui.BeginGroup();
            
            // Ability name
            ImGui.TextColored(Vector4.One, ability);
            
            // Placeholder description
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.62f, 1.0f), "Mastered technique");
            
            ImGui.EndGroup();
            
            // Advance cursor
            ImGui.SetCursorScreenPos(startPos + new Vector2(0, boxSize.Y + 8 * scale));
        }
        
        private void DrawTraitItem(string trait, bool isPositive, float scale)
        {
            var profileColor = ResolveNameplateColor();
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            
            // Colored line indicator
            drawList.AddRectFilled(
                pos,
                pos + new Vector2(3 * scale, 20 * scale),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.067f, 0.714f, 0.506f, 1.0f))
            );
            
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12 * scale);
            ImGui.Text(trait);
            ImGui.Spacing();
        }
        
        private void DrawTraitItem(string trait, float scale)
        {
            var profileColor = ResolveNameplateColor();
            var itemWidth = ImGui.GetContentRegionAvail().X;
            var itemHeight = 32f * scale;

            // InvisibleButton handles layout and cursor advancement properly with scroll
            ImGui.InvisibleButton($"##trait_{trait}", new Vector2(itemWidth, itemHeight));
            var itemPos = ImGui.GetItemRectMin();

            var drawList = ImGui.GetWindowDrawList();

            // Item background
            drawList.AddRectFilled(
                itemPos,
                itemPos + new Vector2(itemWidth, itemHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.039f, 0.039f, 0.039f, 1.0f)),
                6f * scale
            );

            // Item border
            drawList.AddRect(
                itemPos,
                itemPos + new Vector2(itemWidth, itemHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.122f, 0.122f, 0.122f, 1.0f)),
                6f * scale,
                ImDrawFlags.None,
                1f * scale
            );

            // Left accent
            drawList.AddRectFilled(
                itemPos,
                itemPos + new Vector2(3 * scale, itemHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(profileColor.X, profileColor.Y, profileColor.Z, 1.0f))
            );

            // Text overlay
            var textPos = itemPos + new Vector2(12 * scale, (itemHeight - ImGui.GetTextLineHeight()) * 0.5f);
            drawList.AddText(textPos,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.847f, 0.847f, 0.863f, 1.0f)), trait);

            // Spacing between items
            ImGui.Dummy(new Vector2(0, 8 * scale));
        }
        
        private void DrawLikesDislikesTraitItem(string trait, float scale, bool isLike = true)
        {
            var itemWidth = ImGui.GetContentRegionAvail().X;
            var itemHeight = 32f * scale;

            // InvisibleButton handles layout and cursor advancement properly with scroll
            ImGui.InvisibleButton($"##ld_{trait}_{isLike}", new Vector2(itemWidth, itemHeight));
            var itemPos = ImGui.GetItemRectMin();

            var drawList = ImGui.GetWindowDrawList();

            // Item background
            drawList.AddRectFilled(
                itemPos,
                itemPos + new Vector2(itemWidth, itemHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.039f, 0.039f, 0.039f, 1.0f)),
                6f * scale
            );

            // Item border
            drawList.AddRect(
                itemPos,
                itemPos + new Vector2(itemWidth, itemHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.122f, 0.122f, 0.122f, 1.0f)),
                6f * scale,
                ImDrawFlags.None,
                1f * scale
            );

            // Left accent (green for likes, red for dislikes)
            var accentColor = isLike
                ? new Vector4(0.067f, 0.714f, 0.506f, 1.0f)
                : new Vector4(0.8f, 0.2f, 0.2f, 1.0f);
            drawList.AddRectFilled(
                itemPos,
                itemPos + new Vector2(3 * scale, itemHeight),
                ImGui.ColorConvertFloat4ToU32(accentColor),
                6f * scale,
                ImDrawFlags.RoundCornersLeft
            );

            // Text overlay with emoji
            string emoji = isLike ? "\U0001f44d" : "\U0001f44e";
            var textPos = itemPos + new Vector2(12 * scale, (itemHeight - ImGui.GetTextLineHeight()) * 0.5f);
            drawList.AddText(textPos,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.847f, 0.847f, 0.863f, 1.0f)), $"{emoji} {trait}");

            // Spacing between items
            ImGui.Dummy(new Vector2(0, 8 * scale));
        }
        
        private void DrawCollapseButton(float scale)
        {
            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            var drawList = ImGui.GetWindowDrawList();
            
            // Standard Dalamud-style buttons in top-right corner
            float buttonSize = 26f * scale;
            float buttonSpacing = 4f * scale;
            
            // Edit button (left of close button)
            float editButtonX = windowPos.X + windowSize.X - (buttonSize * 2) - buttonSpacing - 12f * scale;
            float editButtonY = windowPos.Y + 12f * scale;
            
            // Close button (rightmost)
            float closeButtonX = windowPos.X + windowSize.X - buttonSize - 12f * scale;
            float closeButtonY = windowPos.Y + 12f * scale;
            
            // Draw edit button (only if not showing external profile)
            if (!showingExternal)
            {
                DrawEditButton(editButtonX, editButtonY, buttonSize, scale, drawList);
            }
            
            // Draw close button
            DrawCloseButton(closeButtonX, closeButtonY, buttonSize, scale, drawList);
        }
        
        private void DrawEditButton(float buttonX, float buttonY, float buttonSize, float scale, ImDrawListPtr drawList)
        {
            var buttonMin = new Vector2(buttonX, buttonY);
            var buttonMax = new Vector2(buttonX + buttonSize, buttonY + buttonSize);
            
            // Check hover
            var mousePos = ImGui.GetIO().MousePos;
            bool isHovered = mousePos.X >= buttonMin.X && mousePos.X <= buttonMax.X &&
                            mousePos.Y >= buttonMin.Y && mousePos.Y <= buttonMax.Y;
            
            // Button styling
            if (isHovered)
            {
                // Blue hover background for edit
                drawList.AddRectFilled(
                    buttonMin,
                    buttonMax,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.4f, 0.8f, 0.8f)),
                    4f * scale
                );
            }
            else
            {
                // Subtle background when not hovered
                drawList.AddRectFilled(
                    buttonMin,
                    buttonMax,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 0.5f)),
                    4f * scale
                );
            }
            
            // Edit icon (pencil)
            var iconColor = isHovered
                ? new Vector4(1.0f, 1.0f, 1.0f, 1.0f)
                : new Vector4(0.7f, 0.7f, 0.7f, 1.0f);

            var iconFont = UiBuilder.IconFont;
            if (iconFont.IsLoaded())
            {
                var center = new Vector2(buttonX + buttonSize * 0.5f, buttonY + buttonSize * 0.5f);
                drawList.AddText(iconFont,
                    16f * scale,
                    center - new Vector2(8f * scale, 8f * scale),
                    ImGui.ColorConvertFloat4ToU32(iconColor),
                    "\uf044" // Edit/pencil icon
                );
            }

            // Draw NEW badge on Edit button
            bool showEditBadge = !plugin.Configuration.SeenFeatures.Contains(FeatureKeys.ExpandedRPProfile);
            if (showEditBadge)
            {
                // Pulsing glow effect around the button
                float pulse = (float)(Math.Sin(ImGui.GetTime() * 3.0) * 0.5 + 0.5);
                var glowColor = new Vector4(0.2f, 1.0f, 0.4f, 0.3f + pulse * 0.5f); // Green glow

                for (int i = 3; i >= 1; i--)
                {
                    var layerPadding = i * 2 * scale;
                    var layerAlpha = glowColor.W * (1.0f - (i * 0.25f));
                    drawList.AddRect(
                        buttonMin - new Vector2(layerPadding, layerPadding),
                        buttonMax + new Vector2(layerPadding, layerPadding),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(glowColor.X, glowColor.Y, glowColor.Z, layerAlpha)),
                        6f * scale,
                        ImDrawFlags.None,
                        2f * scale
                    );
                }
            }

            // Handle click - open edit window
            if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                if (character != null)
                {
                    plugin.RPProfileEditWindow.SetCharacter(character);
                    
                    // Set window to be focused on next frame
                    ImGui.SetNextWindowFocus();
                    plugin.RPProfileEditWindow.IsOpen = true;
                    
                    // Also use Dalamud's BringToFront for good measure
                    plugin.RPProfileEditWindow.BringToFront();
                }
            }
            
            // Tooltip
            if (isHovered)
            {
                ImGui.SetTooltip("Edit Profile");
            }
        }
        
        private void DrawCloseButton(float buttonX, float buttonY, float buttonSize, float scale, ImDrawListPtr drawList)
        {
            
            var buttonMin = new Vector2(buttonX, buttonY);
            var buttonMax = new Vector2(buttonX + buttonSize, buttonY + buttonSize);
            
            // Check hover
            var mousePos = ImGui.GetIO().MousePos;
            bool isHovered = mousePos.X >= buttonMin.X && mousePos.X <= buttonMax.X &&
                            mousePos.Y >= buttonMin.Y && mousePos.Y <= buttonMax.Y;
            
            // Standard window close button styling
            if (isHovered)
            {
                // Red hover background
                drawList.AddRectFilled(
                    buttonMin,
                    buttonMax,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.2f, 0.2f, 0.8f)),
                    4f * scale
                );
            }
            else
            {
                // Subtle background when not hovered
                drawList.AddRectFilled(
                    buttonMin,
                    buttonMax,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 0.5f)),
                    4f * scale
                );
            }
            
            // X icon - thinner and more standard
            var iconColor = isHovered 
                ? new Vector4(1.0f, 1.0f, 1.0f, 1.0f)
                : new Vector4(0.7f, 0.7f, 0.7f, 1.0f);
            var center = new Vector2(buttonX + buttonSize * 0.5f, buttonY + buttonSize * 0.5f);
            var iconSize = 6f * scale;
            
            // Draw X with consistent line thickness
            drawList.AddLine(
                center + new Vector2(-iconSize, -iconSize),
                center + new Vector2(iconSize, iconSize),
                ImGui.ColorConvertFloat4ToU32(iconColor),
                1.5f * scale
            );
            
            drawList.AddLine(
                center + new Vector2(-iconSize, iconSize),
                center + new Vector2(iconSize, -iconSize),
                ImGui.ColorConvertFloat4ToU32(iconColor),
                1.5f * scale
            );
            
            // Handle click - close the window
            if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                pendingClose = true;
            }
            
            // Tooltip
            if (isHovered)
            {
                ImGui.SetTooltip("Close");
            }
        }
        
        private void DrawLeftCollapseHandle(float scale)
        {
            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            var drawList = ImGui.GetWindowDrawList();
            
            // Inset handle on the left edge
            float handleWidth = 14f * scale;
            float handleHeight = 100f * scale;
            float handleInset = 3f * scale; // How far it's inset from edge
            
            // Calculate position to align with content area
            var navHeight = 50f * scale;
            var bannerHeight = 200f * scale;
            var profileAreaHeight = 100f * scale;
            var contentStartY = navHeight + bannerHeight + profileAreaHeight + 20 * scale;
            
            // Position aligned with main content section
            float buttonX = windowPos.X + handleInset;
            float buttonY = windowPos.Y + contentStartY;
            
            var handleMin = new Vector2(buttonX, buttonY);
            var handleMax = new Vector2(buttonX + handleWidth, buttonY + handleHeight);
            
            // Check hover
            var mousePos = ImGui.GetIO().MousePos;
            bool isHovered = mousePos.X >= windowPos.X && mousePos.X <= handleMax.X &&
                            mousePos.Y >= handleMin.Y && mousePos.Y <= handleMax.Y;
            
            // Get theme colors
            var profileColor = ResolveNameplateColor();
            
            // Draw inset groove effect with rounded corners
            drawList.AddRectFilled(
                handleMin,
                handleMax,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.3f)),
                6f * scale // Rounded corners
            );
            
            // Inner recessed area
            var innerMin = handleMin + new Vector2(2f * scale, 2f * scale);
            var innerMax = handleMax - new Vector2(2f * scale, 2f * scale);
            
            var bgColor = isHovered 
                ? new Vector4(profileColor.X * 0.15f, profileColor.Y * 0.15f, profileColor.Z * 0.15f, 0.8f)
                : new Vector4(0.05f, 0.05f, 0.05f, 0.7f);
                
            drawList.AddRectFilled(
                innerMin,
                innerMax,
                ImGui.ColorConvertFloat4ToU32(bgColor),
                4f * scale
            );
            
            // Vertical grip lines for texture
            float lineSpacing = 8f * scale;
            float lineY = innerMin.Y + 20f * scale;
            var gripColor = isHovered
                ? new Vector4(profileColor.X * 0.6f, profileColor.Y * 0.6f, profileColor.Z * 0.6f, 0.5f)
                : new Vector4(0.3f, 0.3f, 0.3f, 0.3f);
                
            while (lineY < innerMax.Y - 20f * scale)
            {
                drawList.AddLine(
                    new Vector2(innerMin.X + 4f * scale, lineY),
                    new Vector2(innerMax.X - 4f * scale, lineY),
                    ImGui.ColorConvertFloat4ToU32(gripColor),
                    1f * scale
                );
                lineY += lineSpacing;
            }
            
            // Small arrow in the center (pointing left)
            var arrowColor = isHovered 
                ? new Vector4(profileColor.X * 0.8f, profileColor.Y * 0.8f, profileColor.Z * 0.8f, 0.9f)
                : new Vector4(0.5f, 0.5f, 0.5f, 0.7f);
                
            var handleCenter = new Vector2(
                handleMin.X + handleWidth * 0.5f,
                handleMin.Y + handleHeight * 0.5f
            );
            
            // Draw < arrow
            var arrowSize = 3.5f * scale;
            drawList.AddLine(
                handleCenter + new Vector2(arrowSize * 0.5f, -arrowSize),
                handleCenter + new Vector2(-arrowSize * 0.5f, 0),
                ImGui.ColorConvertFloat4ToU32(arrowColor),
                2f * scale
            );
            
            drawList.AddLine(
                handleCenter + new Vector2(-arrowSize * 0.5f, 0),
                handleCenter + new Vector2(arrowSize * 0.5f, arrowSize),
                ImGui.ColorConvertFloat4ToU32(arrowColor),
                2f * scale
            );
            
            // Handle click
            if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                ToggleExpansion();
            }
            
            // Tooltip
            if (isHovered)
            {
                ImGui.SetTooltip("Back to compact view");
            }
        }

        private void DrawBanner(ImDrawListPtr dl, Vector2 wndPos, Vector2 wndSize, string bannerImagePath, float bannerZoom, Vector2 bannerOffset, float scale, float navHeight, float bannerHeight)
        {
            var bannerTexture = Plugin.TextureProvider.GetFromFile(bannerImagePath).GetWrapOrDefault();
            if (bannerTexture == null) return;

            // Banner region
            var bannerRegionPos = wndPos + new Vector2(0, navHeight);
            var bannerRegionSize = new Vector2(wndSize.X, bannerHeight);

            // Set up clipping region for banner area
            dl.PushClipRect(bannerRegionPos, bannerRegionPos + bannerRegionSize, true);

            // Calculate banner drawing parameters
            float zoom = Math.Clamp(bannerZoom, 0.5f, 3.0f);
            Vector2 offset = bannerOffset * scale;

            float bannerAspect = (float)bannerTexture.Width / bannerTexture.Height;
            float bannerDrawWidth = bannerRegionSize.X * zoom;
            float bannerDrawHeight = bannerDrawWidth / bannerAspect;

            Vector2 bannerDrawSize = new(bannerDrawWidth, bannerDrawHeight);
            Vector2 bannerDrawPos = bannerRegionPos + offset + new Vector2(
                (bannerRegionSize.X - bannerDrawWidth) * 0.5f,
                (bannerRegionSize.Y - bannerDrawHeight) * 0.5f
            );

            // Draw banner image
            dl.AddImage((ImTextureID)bannerTexture.Handle, bannerDrawPos, bannerDrawPos + bannerDrawSize,
                Vector2.Zero, Vector2.One, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.85f)));

            dl.PopClipRect();
        }

        private async Task StartImageDownload(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || downloadingImages.Contains(url))
                return;

            try
            {
                downloadingImages.Add(url);
                imageStatus[url] = "Downloading...";

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10); // 10 second timeout
                
                var imageData = await httpClient.GetByteArrayAsync(url);
                imageStatus[url] = $"Downloaded {imageData.Length} bytes";
                
                // Create file in plugin directory like the profile image system
                var hash = Convert.ToBase64String(
                    System.Security.Cryptography.MD5.HashData(
                        System.Text.Encoding.UTF8.GetBytes(url)
                    )
                ).Replace("/", "_").Replace("+", "-");

                string fileName = $"GalleryImage_{hash}.png";
                string tempPath = Path.Combine(
                    Plugin.PluginInterface.GetPluginConfigDirectory(),
                    fileName
                );
                
                // Write image data to file
                await File.WriteAllBytesAsync(tempPath, imageData);
                imageStatus[url] = "File saved, ready to load";
                galleryImagePaths[url] = tempPath;
                galleryImageComplete[url] = true;
            }
            catch (Exception ex)
            {
                imageStatus[url] = $"Error: {ex.Message}";
                galleryImageComplete[url] = true; // Mark as failed
            }
            finally
            {
                downloadingImages.Remove(url);
            }
        }

        private string? GetGalleryImagePath(string url)
        {
            // Use the exact same logic as GetCurrentImagePath() for profile images
            if (galleryImageComplete.GetValueOrDefault(url) && galleryImagePaths.TryGetValue(url, out var path))
            {
                if (File.Exists(path))
                    return path;
            }
            return null;
        }

        private void DrawContentBoxLayout(ContentBox box, float scale)
        {
            // Get available width for the content box
            var availableWidth = ImGui.GetContentRegionAvail().X;

            // Determine if the viewer is the owner of the profile
            bool isOwner = !showingExternal && character != null;

            // Set the external profile flag for connection click handling
            ContentBoxRenderer.IsViewingExternalProfile = showingExternal;

            // Set the profile accent color for styled elements (quotes, etc.)
            var profileColor = ResolveNameplateColor();
            ContentBoxRenderer.ProfileAccentColor = profileColor;

            // Use ContentBoxRenderer to render the content box
            ContentBoxRenderer.RenderContentBox(box, availableWidth, scale, isOwner, (modifiedBox) => {
                // When a checkbox is toggled, save the character immediately
                if (character != null)
                {
                    plugin.Configuration.Save();
                }
            });
        }

        private void DrawStandardLayout(ContentBox box, float scale)
        {
            if (!string.IsNullOrEmpty(box.Content))
            {
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 8 * scale));
                ImGui.TextWrapped(box.Content);
                ImGui.PopStyleVar();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.431f, 0.431f, 0.451f, 1.0f));
                ImGui.TextWrapped("No content written yet.");
                ImGui.PopStyleColor();
            }
        }

        private void DrawLikesDislikesLayout(ContentBox box, float scale)
        {
            if (!string.IsNullOrEmpty(box.Likes) || !string.IsNullOrEmpty(box.Dislikes))
            {
                // Parse likes into individual items
                if (!string.IsNullOrEmpty(box.Likes))
                {
                    var likesItems = box.Likes.Split(new char[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var item in likesItems)
                    {
                        if (!string.IsNullOrWhiteSpace(item))
                        {
                            DrawLikesDislikesTraitItem(item.Trim(), scale, true); // true = likes (green)
                        }
                    }
                }

                // Parse dislikes into individual items  
                if (!string.IsNullOrEmpty(box.Dislikes))
                {
                    var dislikesItems = box.Dislikes.Split(new char[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var item in dislikesItems)
                    {
                        if (!string.IsNullOrWhiteSpace(item))
                        {
                            DrawLikesDislikesTraitItem(item.Trim(), scale, false); // false = dislikes (red)
                        }
                    }
                }
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.431f, 0.431f, 0.451f, 1.0f));
                ImGui.TextWrapped("No preferences listed yet.");
                ImGui.PopStyleColor();
            }
        }

        private void DrawListLayout(ContentBox box, float scale)
        {
            if (!string.IsNullOrEmpty(box.Content))
            {
                var lines = box.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 4 * scale));
                
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimmedLine)) continue;
                    
                    // Auto-format as bullet points if not already formatted
                    if (!trimmedLine.StartsWith("•") && !trimmedLine.StartsWith("-") && 
                        !trimmedLine.StartsWith("*") && !char.IsDigit(trimmedLine[0]))
                    {
                        ImGui.Text($"• {trimmedLine}");
                    }
                    else
                    {
                        ImGui.Text(trimmedLine);
                    }
                }
                ImGui.PopStyleVar();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.431f, 0.431f, 0.451f, 1.0f));
                ImGui.TextWrapped("No list items added yet.");
                ImGui.PopStyleColor();
            }
        }

        private void DrawKeyValueLayout(ContentBox box, float scale)
        {
            if (!string.IsNullOrEmpty(box.Content))
            {
                var lines = box.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var availableWidth = ImGui.GetContentRegionAvail().X;
                var labelWidth = availableWidth * 0.35f; // 35% for labels, 65% for values
                
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 6 * scale));
                
                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { ':', '=' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        var label = parts[0].Trim();
                        var value = parts[1].Trim();
                        
                        // Label (aligned)
                        ImGui.SetNextItemWidth(labelWidth);
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.9f, 1.0f)); // Slightly brighter
                        ImGui.Text($"{label}:");
                        ImGui.PopStyleColor();
                        
                        ImGui.SameLine();
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (10 * scale)); // Padding
                        ImGui.TextWrapped(value);
                    }
                    else
                    {
                        // Regular line without key-value format
                        ImGui.TextWrapped(line.Trim());
                    }
                }
                ImGui.PopStyleVar();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.431f, 0.431f, 0.451f, 1.0f));
                ImGui.TextWrapped("No key-value pairs added yet. Format: Key: Value");
                ImGui.PopStyleColor();
            }
        }

        private void DrawQuoteLayout(ContentBox box, float scale)
        {
            if (!string.IsNullOrEmpty(box.QuoteText))
            {
                var availableWidth = ImGui.GetContentRegionAvail().X;
                var quoteIndent = 20 * scale;
                
                // Quote mark and indented text
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + quoteIndent);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.9f, 1.0f)); // Subtle purple tint
                ImGui.PushFont(ImGui.GetIO().Fonts.Fonts[0]); // Could use italic font if available
                
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + availableWidth - (quoteIndent * 2));
                ImGui.Text($"\"{box.QuoteText}\"");
                ImGui.PopTextWrapPos();
                
                ImGui.PopFont();
                ImGui.PopStyleColor();
                
                // Attribution
                if (!string.IsNullOrEmpty(box.QuoteAuthor))
                {
                    ImGui.Spacing();
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availableWidth - (100 * scale));
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.7f, 1.0f));
                    ImGui.Text($"— {box.QuoteAuthor}");
                    ImGui.PopStyleColor();
                }
            }
            else if (!string.IsNullOrEmpty(box.Content))
            {
                // Fallback to content if no specific quote fields
                DrawQuoteFromContent(box.Content, scale);
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.431f, 0.431f, 0.451f, 1.0f));
                ImGui.TextWrapped("No quote added yet.");
                ImGui.PopStyleColor();
            }
        }

        private void DrawQuoteFromContent(string content, float scale)
        {
            var availableWidth = ImGui.GetContentRegionAvail().X;
            var quoteIndent = 20 * scale;
            
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + quoteIndent);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.9f, 1.0f));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + availableWidth - (quoteIndent * 2));
            ImGui.TextWrapped($"\"{content}\"");
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
        }

        private void DrawTimelineLayout(ContentBox box, float scale)
        {
            if (!string.IsNullOrEmpty(box.Content))
            {
                var lines = box.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var availableWidth = ImGui.GetContentRegionAvail().X;
                var dateWidth = 80 * scale;
                
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 8 * scale));
                
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimmedLine)) continue;
                    
                    // Try to parse date/event format
                    var parts = trimmedLine.Split(new[] { '-', '—', '–' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        var date = parts[0].Trim();
                        var eventText = parts[1].Trim();
                        
                        // Date
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.8f, 0.4f, 1.0f)); // Gold
                        ImGui.SetNextItemWidth(dateWidth);
                        ImGui.Text(date);
                        ImGui.PopStyleColor();
                        
                        ImGui.SameLine();
                        ImGui.Text("────");
                        ImGui.SameLine();
                        
                        // Event
                        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + availableWidth - dateWidth - (40 * scale));
                        ImGui.TextWrapped(eventText);
                        ImGui.PopTextWrapPos();
                    }
                    else
                    {
                        // Regular line without timeline format
                        ImGui.TextWrapped(trimmedLine);
                    }
                }
                ImGui.PopStyleVar();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.431f, 0.431f, 0.451f, 1.0f));
                ImGui.TextWrapped("No timeline events added yet. Format: Date — Event");
                ImGui.PopStyleColor();
            }
        }

        private void DrawGridLayout(ContentBox box, float scale)
        {
            if (!string.IsNullOrEmpty(box.Content))
            {
                var lines = box.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var availableWidth = ImGui.GetContentRegionAvail().X;
                var columns = Math.Min(3, Math.Max(1, (int)(availableWidth / (120 * scale)))); // Auto columns
                
                if (lines.Length > 0)
                {
                    ImGui.Columns(columns, "GridColumns", false);
                    var columnWidth = availableWidth / columns;
                    
                    for (int i = 0; i < columns; i++)
                    {
                        ImGui.SetColumnWidth(i, columnWidth);
                    }
                    
                    ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 4 * scale));
                    
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i].Trim();
                        if (!string.IsNullOrEmpty(line))
                        {
                            ImGui.TextWrapped(line);
                        }
                        
                        if (i < lines.Length - 1)
                        {
                            ImGui.NextColumn();
                        }
                    }
                    
                    ImGui.PopStyleVar();
                    ImGui.Columns(1);
                }
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.431f, 0.431f, 0.451f, 1.0f));
                ImGui.TextWrapped("No grid items added yet.");
                ImGui.PopStyleColor();
            }
        }

        private void DrawProsConsLayout(ContentBox box, float scale)
        {
            var availableWidth = ImGui.GetContentRegionAvail().X;
            var columnWidth = (availableWidth - (10 * scale)) * 0.5f;

            if (!string.IsNullOrEmpty(box.LeftColumn) || !string.IsNullOrEmpty(box.RightColumn))
            {
                ImGui.Columns(2, "ProsConsColumns", false);
                ImGui.SetColumnWidth(0, columnWidth);
                ImGui.SetColumnWidth(1, columnWidth);

                // Left column (Pros/Strengths)
                if (!string.IsNullOrEmpty(box.LeftColumn))
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.8f, 0.4f, 1.0f)); // Green
                    ImGui.Text("✓ Pros / Strengths");
                    ImGui.PopStyleColor();
                    ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 4 * scale));
                    
                    var lines = box.LeftColumn.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            if (!trimmed.StartsWith("•") && !trimmed.StartsWith("✓"))
                                ImGui.Text($"• {trimmed}");
                            else
                                ImGui.Text(trimmed);
                        }
                    }
                    ImGui.PopStyleVar();
                }

                ImGui.NextColumn();

                // Right column (Cons/Weaknesses)
                if (!string.IsNullOrEmpty(box.RightColumn))
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.4f, 0.4f, 1.0f)); // Red
                    ImGui.Text("✗ Cons / Weaknesses");
                    ImGui.PopStyleColor();
                    ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 4 * scale));
                    
                    var lines = box.RightColumn.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            if (!trimmed.StartsWith("•") && !trimmed.StartsWith("✗"))
                                ImGui.Text($"• {trimmed}");
                            else
                                ImGui.Text(trimmed);
                        }
                    }
                    ImGui.PopStyleVar();
                }

                ImGui.Columns(1);
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.431f, 0.431f, 0.451f, 1.0f));
                ImGui.TextWrapped("No pros/cons listed yet.");
                ImGui.PopStyleColor();
            }
        }

        private void DrawTaggedLayout(ContentBox box, float scale)
        {
            if (!string.IsNullOrEmpty(box.Content))
            {
                var sections = box.Content.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var section in sections)
                {
                    var lines = section.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length == 0) continue;
                    
                    var tagLine = lines[0].Trim();
                    
                    // Draw tag/category header
                    if (tagLine.StartsWith("#") || tagLine.StartsWith("[") || tagLine.EndsWith(":"))
                    {
                        var tagText = tagLine.TrimStart('#').TrimStart('[').TrimEnd(']').TrimEnd(':');
                        
                        // Tag bubble background
                        var tagSize = ImGui.CalcTextSize(tagText);
                        var padding = new Vector2(8 * scale, 4 * scale);
                        var tagBgSize = tagSize + padding * 2;
                        var cursor = ImGui.GetCursorScreenPos();
                        
                        var dl = ImGui.GetWindowDrawList();
                        dl.AddRectFilled(cursor, cursor + tagBgSize, 
                            ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.5f, 0.8f, 0.3f)), 4 * scale);
                        dl.AddRect(cursor, cursor + tagBgSize, 
                            ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.5f, 0.8f, 0.8f)), 4 * scale);
                        
                        ImGui.SetCursorScreenPos(cursor + padding);
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 1.0f, 1.0f));
                        ImGui.Text(tagText);
                        ImGui.PopStyleColor();
                        ImGui.SetCursorScreenPos(cursor + new Vector2(0, tagBgSize.Y + (6 * scale)));
                        
                        // Content under the tag
                        if (lines.Length > 1)
                        {
                            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 4 * scale));
                            for (int i = 1; i < lines.Length; i++)
                            {
                                var contentLine = lines[i].Trim();
                                if (!string.IsNullOrEmpty(contentLine))
                                {
                                    ImGui.TextWrapped(contentLine);
                                }
                            }
                            ImGui.PopStyleVar();
                        }
                    }
                    else
                    {
                        // Regular content without tag formatting
                        foreach (var line in lines)
                        {
                            if (!string.IsNullOrEmpty(line.Trim()))
                                ImGui.TextWrapped(line.Trim());
                        }
                    }
                    
                    ImGui.Spacing();
                }
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.431f, 0.431f, 0.451f, 1.0f));
                ImGui.TextWrapped("No tagged content yet. Format: #Tag or [Category]: Content");
                ImGui.PopStyleColor();
            }
        }

        // Banner URL support methods
        private string GetBannerImagePath(string imageUrl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imageUrl))
                    return "";

                var fileName = $"banner_{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(imageUrl)).Replace('/', '_').Replace('+', '-')}";
                return Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), fileName);
            }
            catch
            {
                return "";
            }
        }

        private async Task DownloadBannerImageAsync(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return;

            var imagePath = GetBannerImagePath(imageUrl);

            if (File.Exists(imagePath))
                return;

            lock (downloadingBanners)
            {
                if (downloadingBanners.Contains(imageUrl))
                    return;
                downloadingBanners.Add(imageUrl);
            }

            try
            {
                if (File.Exists(imagePath))
                    return;

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                var response = await client.GetAsync(imageUrl);

                if (response.IsSuccessStatusCode)
                {
                    var imageBytes = await response.Content.ReadAsByteArrayAsync();
                    var tempPath = imagePath + ".tmp";
                    await File.WriteAllBytesAsync(tempPath, imageBytes);
                    File.Move(tempPath, imagePath);
                    Plugin.Log.Info($"[ProfileView] Downloaded banner image: {imageUrl}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[ProfileView] Failed to download banner image {imageUrl}: {ex.Message}");
            }
            finally
            {
                lock (downloadingBanners)
                {
                    downloadingBanners.Remove(imageUrl);
                }
            }
        }
    }
}

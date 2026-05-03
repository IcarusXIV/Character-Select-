using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using CharacterSelectPlugin.Windows.Styles;
using CharacterSelectPlugin.Windows.Utils;
using CharacterSelectPlugin.Effects;

namespace CharacterSelectPlugin.Windows.Components
{
    public class CharacterGrid : IDisposable
    {
        private Plugin plugin;
        private UIStyles uiStyles;
        private Dictionary<int, float> hoverAnimations = new();
        private bool showSearchBar = false;
        private string searchQuery = "";
        private string selectedTag = "All";
        private bool showTagFilter = false;
        private Dictionary<int, FavoriteSparkEffect> characterFavoriteEffects = new();
        private Dictionary<int, WinterSnowEffect> characterSnowEffects = new();
        private FogSequenceEffect fogEffect;

        // Drag and drop state
        private int? draggedCharacterIndex = null;
        private bool isDragging = false;
        private Vector2 dragStartPos = Vector2.Zero;
        private const float DragThreshold = 5f;
        public bool ShouldPreventWindowDrag => isDragging;

        // Pagination
        private int currentPage = 0;
        private int charactersPerPage = 40;
        private List<(int characterIndex, Vector2 min, Vector2 max)> cardRects = new();
        private int? currentDropTargetIndex = null;
        private bool cardRectsDirty = true;
        private bool scrollToTopOnNextFrame = false;
        private bool hasNavigatedToActiveOnStartup = false;
        private bool anyNonActiveCardHovered = false;

        // New-character reveal animation. Phases (materialise / hold / fly / land)
        // are gated by the *End constants below.
        private float _newAnimStart = -1f;
        private string? _newAnimCharName = null;
        private Vector2 _newAnimSlotMin = Vector2.Zero;
        private Vector2 _newAnimSlotMax = Vector2.Zero;
        private float _newAnimCardW = 0f;
        private float _newAnimCardH = 0f;
        private float _newAnimScale = 1f;
        private Vector2 _newAnimViewportMin = Vector2.Zero;
        private Vector2 _newAnimViewportMax = Vector2.Zero;
        private bool _newAnimSlotCaptured = false;
        private bool _newAnimScrolledToSlot = false;
        private const float NewAnimDur            = 3.05f;
        private const float NewAnimMaterializeEnd = 0.50f;
        private const float NewAnimHoldEnd        = 2.20f;
        private const float NewAnimFlyEnd         = 2.65f;

        // Cutouts are queued and drained after all cards render so they paint
        // over neighbouring cards instead of getting clipped into adjacent slots.
        private struct PendingCutout
        {
            public Vector2 ImgMin;     // portrait area (used for anchor positioning)
            public Vector2 ImgMax;
            public Vector2 SlipMin;    // full card incl. nameplate (used for sizing)
            public Vector2 SlipMax;
            public string CutoutPath;
            public float HoverAmount;
            public float Scale;        // per-character cutout size multiplier
            public float UiScale;      // ImGui scale (for any future scale-aware draws)
            public float AnchorX, AnchorY;
            public float PoseAx, PoseAy;
        }
        private readonly List<PendingCutout> pendingCutouts = new();

        // Performance optimizations
        private List<Character> cachedFilteredCharacters = new();
        private List<Character> cachedPagedCharacters = new();
        private string lastSearchQuery = "";
        private string lastSelectedTag = "All";
        private int lastCharacterCount = 0;
        private bool filterCacheDirty = true;

        // Cache UI calculations
        private float cachedCardWidth = 0f;
        private int cachedColumnCount = 0;
        private float cachedColumnWidth = 0f;
        private float cachedAvailableWidth = 0f;
        private float cachedScale = 0f;
        private bool layoutCacheDirty = true;

        // Cache expensive string operations
        private readonly Dictionary<string, bool> fileExistsCache = new();
        private readonly Dictionary<string, Vector2> textSizeCache = new();
        private volatile bool isCacheWarming = false;

        // Frame limiting for animations
        private float lastAnimationUpdate = 0f;
        private const float AnimationUpdateInterval = 1f / 60f; // 60 FPS max
        
        // Halloween wiggle animation state
        private readonly Dictionary<int, float> wiggleStartTimes = new();
        private readonly Dictionary<int, Vector2> wiggleOffsets = new();
        private float lastWiggleCheck = 0f;
        private const float WiggleCheckInterval = 2f; // Check every 2 seconds for new wiggles
        private const float WiggleDuration = 0.8f; // Each wiggle lasts 0.8 seconds
        private const float WiggleIntensity = 3f; // Maximum wiggle offset in pixels

        // Ghost image state
        private Character? draggedCharacter = null;
        private Vector2 ghostImageSize = new Vector2(120f, 120f);
        private float ghostImageAlpha = 0.8f;

        public Plugin.SortType CurrentSort { get; private set; }

        // ── Chassis bridge ─────────────────────────────────────────────
        // When the boutique chassis (MainWindow) owns the toolbar + pagination,
        // we skip drawing them inside the grid's child window. The chassis still
        // drives state through these accessors.
        public bool ChassisOwnsToolbar { get; set; } = true;
        public bool ChassisOwnsPagination { get; set; } = true;

        public string SearchQuery
        {
            get => searchQuery;
            set
            {
                if (searchQuery != value)
                {
                    searchQuery = value ?? "";
                    InvalidateFilterCache();
                }
            }
        }

        public string SelectedTag
        {
            get => selectedTag;
            set
            {
                if (selectedTag != value)
                {
                    selectedTag = string.IsNullOrEmpty(value) ? "All" : value;
                    InvalidateFilterCache();
                }
            }
        }

        public int CurrentPage
        {
            get => currentPage;
            set
            {
                int clamped = Math.Max(0, Math.Min(value, MaxPageIndex));
                if (clamped != currentPage)
                {
                    pagePrevIdx = currentPage;
                    currentPage = clamped;
                    pageTransitionStart = ImGui.GetTime();
                    scrollToTopOnNextFrame = true;
                    InvalidateCache();
                }
            }
        }

        // Page-transition state (wardrobe-style ease-out lerp on the active dot)
        private int pagePrevIdx = 0;
        private double pageTransitionStart = -1;
        private const double PageTransitionDur = 0.28;
        public bool IsPageTransitioning
        {
            get
            {
                if (pageTransitionStart < 0) return false;
                if (ImGui.GetTime() - pageTransitionStart >= PageTransitionDur)
                {
                    pageTransitionStart = -1;
                    return false;
                }
                return true;
            }
        }
        /// <summary>0..1 ease-out-cubic progress of the in-flight page transition.</summary>
        public float PageTransitionT
        {
            get
            {
                if (!IsPageTransitioning) return 1f;
                float u = (float)((ImGui.GetTime() - pageTransitionStart) / PageTransitionDur);
                u = Math.Clamp(u, 0f, 1f);
                return 1f - MathF.Pow(1f - u, 3f);
            }
        }
        public int PagePrevIdx => pagePrevIdx;

        public int CharactersPerPage => charactersPerPage;
        public int FilteredCount => GetFilteredCharacters().Count;
        public int TotalPageCount => Math.Max(1, (FilteredCount + charactersPerPage - 1) / charactersPerPage);
        public int MaxPageIndex => Math.Max(0, TotalPageCount - 1);
        public int VisibleStartIndex => FilteredCount == 0 ? 0 : currentPage * charactersPerPage + 1;
        public int VisibleEndIndex => Math.Min(FilteredCount, (currentPage + 1) * charactersPerPage);

        public IReadOnlyList<string> GetAvailableTags()
        {
            var tags = plugin.Characters
                .SelectMany(c => c.Tags ?? new List<string>())
                .Distinct()
                .OrderBy(t => t)
                .ToList();
            tags.Insert(0, "All");
            return tags;
        }

        public CharacterGrid(Plugin plugin, UIStyles uiStyles)
        {
            this.plugin = plugin;
            this.uiStyles = uiStyles;
            CurrentSort = (Plugin.SortType)plugin.Configuration.CurrentSortIndex;
            fogEffect = new FogSequenceEffect(plugin);
        }

        public void Dispose()
        {
            // Clear caches
            fileExistsCache.Clear();
            textSizeCache.Clear();
            characterFavoriteEffects.Clear();
            fogEffect?.Dispose();
        }

        public void Draw()
        {
            // Calculate responsive scaling using Dalamud's GlobalScale
            var totalScale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);

            ImGuiWindowFlags windowFlags = ImGuiWindowFlags.None;

            // Disable window moving while dragging a character
            if (isDragging && draggedCharacterIndex.HasValue)
            {
                windowFlags |= ImGuiWindowFlags.NoMove;
            }

            // Apply the flags to window
            
            // Draw seasonal background effects behind everything (before toolbar)
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration))
            {
                var effectiveTheme = SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration);
                Vector2 windowSize = ImGui.GetWindowSize();
                
                if (effectiveTheme == SeasonalTheme.Halloween)
                {
                    DrawHalloweenSpiderWebs();
                    
                    // Set fog area right before drawing
                    fogEffect?.SetEffectArea(windowSize);
                    fogEffect?.Draw(); // Draw fog on same layer as spider webs
                }
                else if (effectiveTheme == SeasonalTheme.Winter || effectiveTheme == SeasonalTheme.Christmas)
                {
                    // Corner line decorations removed per user request
                }
            }
            
            if (!ChassisOwnsToolbar)
                DrawToolbar(totalScale);
            DrawCharacterGridContent(totalScale);

            // Throttle animation updates
            float currentTime = (float)ImGui.GetTime();
            if (currentTime - lastAnimationUpdate >= AnimationUpdateInterval)
            {
                UpdateEffects(ImGui.GetIO().DeltaTime);
                lastAnimationUpdate = currentTime;
            }

            DrawEffects();
            if (!ChassisOwnsPagination)
                DrawPagination(totalScale);

            // Draw the ghost image last so it appears on top of everything
            DrawDragGhostImage(totalScale);
        }

        private void UpdateEffects(float deltaTime)
        {
            foreach (var effect in characterFavoriteEffects.Values)
            {
                effect.Update(deltaTime);
            }
            
            // Update seasonal background effects
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration))
            {
                var effectiveTheme = SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration);
                Vector2 contentSize = ImGui.GetContentRegionAvail();
                
                if (effectiveTheme == SeasonalTheme.Halloween)
                {
                    // Update fog effect for Halloween theme
                    if (contentSize.X > 0 && contentSize.Y > 0)
                    {
                        fogEffect.SetEffectArea(contentSize);
                    }
                    fogEffect.Update(deltaTime);
                }
            }
        }

        private void DrawHalloweenSpiderWebs()
        {
            var drawList = ImGui.GetWindowDrawList();
            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            
            // Use white color for spider webs
            var webColor = new Vector4(1.0f, 1.0f, 1.0f, 0.6f); // White with higher opacity
            uint color = ImGui.GetColorU32(webColor);
            
            // Draw simple corner webs
            float webSize = 50f;
            
            // Top-left corner web - positioned exactly at corner
            Vector2 cornerTL = windowPos;
            DrawSimpleWeb(drawList, cornerTL, webSize, color, 0); // Top-left
            
            // Top-right corner web - positioned exactly at corner
            Vector2 cornerTR = windowPos + new Vector2(windowSize.X, 0);
            DrawSimpleWeb(drawList, cornerTR, webSize, color, 1); // Top-right
            
            // Bottom-right corner web - positioned exactly at corner (skip bottom-left to avoid hiding behind character cards)
            Vector2 cornerBR = windowPos + new Vector2(windowSize.X, windowSize.Y);
            DrawSimpleWeb(drawList, cornerBR, webSize, color, 3); // Bottom-right
        }

        private void DrawSimpleWeb(ImDrawListPtr drawList, Vector2 corner, float size, uint color, int cornerType)
        {
            // Vary pattern based on corner for uniqueness
            int strands = cornerType switch
            {
                0 => 5, // Top-left: 5 strands
                1 => 4, // Top-right: 4 strands  
                2 => 6, // Bottom-left: 6 strands
                3 => 4, // Bottom-right: 4 strands
                _ => 4
            };
            
            // Draw radial strands from corner
            for (int i = 0; i < strands; i++)
            {
                float angle = 0f;
                Vector2 direction = Vector2.Zero;
                
                switch (cornerType)
                {
                    case 0: // Top-left
                        angle = (float)(Math.PI * 0.5f * i / (strands - 1));
                        direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                        break;
                    case 1: // Top-right
                        angle = (float)(Math.PI * 0.5f + Math.PI * 0.5f * i / (strands - 1));
                        direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                        break;
                    case 2: // Bottom-left
                        angle = (float)(Math.PI * 1.5f + Math.PI * 0.5f * i / (strands - 1));
                        direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                        break;
                    case 3: // Bottom-right
                        angle = (float)(Math.PI + Math.PI * 0.5f * i / (strands - 1));
                        direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                        break;
                }
                
                // Vary strand length for more organic look
                float strandLength = size * (0.8f + (i % 2) * 0.2f);
                Vector2 endPoint = corner + direction * strandLength;
                
                // Vary line thickness
                float thickness = i == 0 || i == strands - 1 ? 1.2f : 0.8f;
                drawList.AddLine(corner, endPoint, color, thickness);
            }
            
            // Draw connecting rings with varied complexity
            int rings = cornerType == 2 ? 4 : 3; // Bottom-left gets extra ring
            for (int ring = 1; ring <= rings; ring++)
            {
                float ringSize = size * ring / rings * 0.9f;
                
                // Add some irregularity to ring connections
                for (int i = 0; i < strands - 1; i++)
                {
                    float angle1 = 0f, angle2 = 0f;
                    
                    switch (cornerType)
                    {
                        case 0: // Top-left
                            angle1 = (float)(Math.PI * 0.5f * i / (strands - 1));
                            angle2 = (float)(Math.PI * 0.5f * (i + 1) / (strands - 1));
                            break;
                        case 1: // Top-right
                            angle1 = (float)(Math.PI * 0.5f + Math.PI * 0.5f * i / (strands - 1));
                            angle2 = (float)(Math.PI * 0.5f + Math.PI * 0.5f * (i + 1) / (strands - 1));
                            break;
                        case 2: // Bottom-left
                            angle1 = (float)(Math.PI * 1.5f + Math.PI * 0.5f * i / (strands - 1));
                            angle2 = (float)(Math.PI * 1.5f + Math.PI * 0.5f * (i + 1) / (strands - 1));
                            break;
                        case 3: // Bottom-right
                            angle1 = (float)(Math.PI + Math.PI * 0.5f * i / (strands - 1));
                            angle2 = (float)(Math.PI + Math.PI * 0.5f * (i + 1) / (strands - 1));
                            break;
                    }
                    
                    // Add slight curve to connections for more organic look
                    Vector2 point1 = corner + new Vector2((float)Math.Cos(angle1), (float)Math.Sin(angle1)) * ringSize;
                    Vector2 point2 = corner + new Vector2((float)Math.Cos(angle2), (float)Math.Sin(angle2)) * ringSize;
                    
                    // Draw all connections for complete web
                    drawList.AddLine(point1, point2, color, 0.6f);
                }
            }
            
            // Add small spider at corner for some webs
            if (cornerType == 0 || cornerType == 3) // Top-left and bottom-right
            {
                Vector2 spiderPos = corner + new Vector2(
                    cornerType == 0 ? 8 : -8,
                    cornerType == 0 ? 8 : -8
                );
                drawList.AddCircleFilled(spiderPos, 2f, color, 6);
            }
        }

        private void DrawCharacterCardSpiderWebs(ImDrawListPtr drawList, Vector2 cardMin, float cardWidth, float imageHeight, float scale, float hoverAmount)
        {
            // Smaller, more subtle webs for character cards
            float baseAlpha = 0.4f;
            float hoverAlpha = baseAlpha + (hoverAmount * 0.3f); // Increase visibility on hover
            var webColor = new Vector4(1.0f, 1.0f, 1.0f, hoverAlpha);
            uint color = ImGui.GetColorU32(webColor);
            
            float baseWebSize = 25f * scale;
            float webSize = baseWebSize * (1.0f + hoverAmount * 0.2f); // Grow on hover
            
            // Only draw on top corners to not obstruct character image too much
            Vector2 topLeft = cardMin;
            Vector2 topRight = cardMin + new Vector2(cardWidth, 0);
            
            // Draw small spider webs in top corners
            DrawCardWeb(drawList, topLeft, webSize, color, 0); // Top-left
            DrawCardWeb(drawList, topRight, webSize, color, 1); // Top-right
        }

        private void DrawCardWeb(ImDrawListPtr drawList, Vector2 corner, float size, uint color, int cornerType)
        {
            // Simpler web pattern for character cards
            int strands = 3; // Fewer strands for subtlety
            
            for (int i = 0; i < strands; i++)
            {
                float angle = 0f;
                
                switch (cornerType)
                {
                    case 0: // Top-left
                        angle = (float)(Math.PI * 0.5f * i / (strands - 1));
                        break;
                    case 1: // Top-right
                        angle = (float)(Math.PI * 0.5f + Math.PI * 0.5f * i / (strands - 1));
                        break;
                }
                
                Vector2 direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                Vector2 endPoint = corner + direction * size;
                
                drawList.AddLine(corner, endPoint, color, 0.8f);
            }
            
            // Add simple connecting rings
            for (int ring = 1; ring <= 2; ring++)
            {
                float ringSize = size * ring / 2f * 0.8f;
                
                for (int i = 0; i < strands - 1; i++)
                {
                    float angle1 = 0f, angle2 = 0f;
                    
                    switch (cornerType)
                    {
                        case 0: // Top-left
                            angle1 = (float)(Math.PI * 0.5f * i / (strands - 1));
                            angle2 = (float)(Math.PI * 0.5f * (i + 1) / (strands - 1));
                            break;
                        case 1: // Top-right
                            angle1 = (float)(Math.PI * 0.5f + Math.PI * 0.5f * i / (strands - 1));
                            angle2 = (float)(Math.PI * 0.5f + Math.PI * 0.5f * (i + 1) / (strands - 1));
                            break;
                    }
                    
                    Vector2 point1 = corner + new Vector2((float)Math.Cos(angle1), (float)Math.Sin(angle1)) * ringSize;
                    Vector2 point2 = corner + new Vector2((float)Math.Cos(angle2), (float)Math.Sin(angle2)) * ringSize;
                    
                    drawList.AddLine(point1, point2, color, 0.6f);
                }
            }
        }

        private void DrawWinterSnowDecorations()
        {
            // Simple window corner decorations - just like Halloween spider webs
            var drawList = ImGui.GetWindowDrawList();
            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            
            var snowColor = new Vector4(0.95f, 0.98f, 1.0f, 0.6f);
            uint color = ImGui.GetColorU32(snowColor);
            
            float snowSize = 50f;
            
            // Draw simple snow decorations at window corners - same as Halloween
            Vector2 cornerTL = windowPos;
            Vector2 cornerTR = windowPos + new Vector2(windowSize.X, 0);
            Vector2 cornerBR = windowPos + new Vector2(windowSize.X, windowSize.Y);
            
            // Simple icicle lines at corners
            for (int i = 0; i < 3; i++)
            {
                float offset = (i + 1) * snowSize / 4;
                
                // Top-left icicles
                drawList.AddLine(
                    cornerTL + new Vector2(offset, 0),
                    cornerTL + new Vector2(offset, snowSize * 0.6f + i * 3f),
                    color, 1.5f);
                    
                // Top-right icicles  
                drawList.AddLine(
                    cornerTR + new Vector2(-offset, 0),
                    cornerTR + new Vector2(-offset, snowSize * 0.6f + i * 3f),
                    color, 1.5f);
            }
        }


        private void DrawCharacterCardIcicles(ImDrawListPtr drawList, Vector2 cardMin, float cardWidth, float imageHeight, float scale)
        {
            // Draw icicle triangles hanging from bottom of character card
            var iceColor = new Vector4(0.85f, 0.95f, 1.0f, 1.0f); // More opaque for visibility
            uint iceColorU32 = ImGui.GetColorU32(iceColor);
            
            var random = new Random(42); // Fixed seed for consistent icicles per card
            
            // Generate 4-6 icicles distributed along the bottom edge 
            int icicleCount = 4 + random.Next(3);
            for (int i = 0; i < icicleCount; i++)
            {
                // Position icicles across the bottom edge - keep them away from edges
                float edgeMargin = cardWidth * 0.1f; // 10% margin from each edge
                float availableWidth = cardWidth - (2 * edgeMargin);
                float x = cardMin.X + edgeMargin + (availableWidth * ((float)i / (icicleCount - 1)));
                float length = 15f + random.NextSingle() * 10f; // Longer icicles
                float width = 3f + random.NextSingle() * 2f; // Wider icicles
                
                // Create icicle triangle hanging down from bottom border of the card.
                // Per user feedback: icicles moved up by 3px total (65 → 62).
                float cardBottom = cardMin.Y + imageHeight + (62f * scale);
                Vector2 topLeft = new Vector2(x - width, cardBottom);
                Vector2 topRight = new Vector2(x + width, cardBottom);
                Vector2 bottom = new Vector2(x, cardBottom + length);
                
                // Draw icicle triangle with bright color for testing
                drawList.AddTriangleFilled(topLeft, topRight, bottom, iceColorU32);
                
                // Add highlight line
                Vector2 highlight1 = topLeft + new Vector2(0.3f, 0);
                Vector2 highlight2 = bottom + new Vector2(-0.3f, 0);
                drawList.AddLine(highlight1, highlight2, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.8f)), 1.5f);
            }
            
            // Add gentle snow particles falling from character card edges
            DrawCharacterCardSnowParticles(drawList, cardMin, cardWidth, imageHeight, scale);
        }

        private void DrawCharacterCardSnowParticles(ImDrawListPtr drawList, Vector2 cardMin, float cardWidth, float imageHeight, float scale)
        {
            var snowColor = new Vector4(0.95f, 0.98f, 1.0f, 0.6f);
            uint snowColorU32 = ImGui.GetColorU32(snowColor);
            
            var random = new Random(123); // Different seed for particles
            
            // Snow particles falling from bottom edge. Spawn point matches
            // the icicle base above (+62 px) so particles fall from the right place.
            int bottomParticles = 8 + random.Next(5); // 8-12 particles
            float cardBottom = cardMin.Y + imageHeight + (62f * scale);
            for (int i = 0; i < bottomParticles; i++)
            {
                float x = cardMin.X + (cardWidth * random.NextSingle());
                float fallDistance = 20f + (random.NextSingle() * 30f);
                float particleSize = 0.8f + (random.NextSingle() * 1.2f);
                
                Vector2 particlePos = new Vector2(x, cardBottom + fallDistance);
                drawList.AddCircleFilled(particlePos, particleSize, snowColorU32);
            }
            
            // Snow particles falling from left side - more particles
            int leftParticles = 8 + random.Next(5); // 8-12 particles
            for (int i = 0; i < leftParticles; i++)
            {
                float y = cardMin.Y + (imageHeight * random.NextSingle());
                float fallDistance = 8f + (random.NextSingle() * 25f); // Slightly wider spread
                float particleSize = 0.6f + (random.NextSingle() * 1.0f);
                
                Vector2 particlePos = new Vector2(cardMin.X - fallDistance, y);
                drawList.AddCircleFilled(particlePos, particleSize, snowColorU32);
            }
            
            // Snow particles falling from right side - more particles
            int rightParticles = 8 + random.Next(5); // 8-12 particles
            for (int i = 0; i < rightParticles; i++)
            {
                float y = cardMin.Y + (imageHeight * random.NextSingle());
                float fallDistance = 8f + (random.NextSingle() * 25f); // Slightly wider spread
                float particleSize = 0.6f + (random.NextSingle() * 1.0f);
                
                Vector2 particlePos = new Vector2(cardMin.X + cardWidth + fallDistance, y);
                drawList.AddCircleFilled(particlePos, particleSize, snowColorU32);
            }
        }

        private void DrawCharacterCardSnowOverlay(ImDrawListPtr drawList, Vector2 cardMin, float cardWidth, float imageHeight, float scale, float hoverAmount)
        {
            // Load snow.png from Assets folder
            string pluginDirectory = plugin.PluginDirectory;
            string snowImagePath = Path.Combine(pluginDirectory, "Assets", "snow.png");
            
            if (File.Exists(snowImagePath))
            {
                var snowTexture = Plugin.TextureProvider.GetFromFile(snowImagePath).GetWrapOrDefault();
                
                if (snowTexture != null)
                {
                    // Calculate snow overlay size and position for top left corner
                    float snowSize = 50f * scale; // Slightly smaller size
                    // Position over the glowing border at top-left corner, with extra offset
                    var borderMargin = (4f + (hoverAmount * 2f)) * scale;
                    // Per user feedback: snow pile nudged 3px down total + 2px right.
                    // Reducing each offset moves the snow toward the card by that amount.
                    float extraOffsetUp = 16f * scale; // 3px lower than the original 19px
                    float extraOffsetLeft = 2f * scale; // 2px further right than the original 4px
                    Vector2 snowPos = cardMin - new Vector2(borderMargin + extraOffsetLeft, borderMargin + extraOffsetUp); // Position over the border
                    Vector2 snowPosMax = snowPos + new Vector2(snowSize, snowSize);
                    
                    // Draw snow overlay with no transparency
                    drawList.AddImageRounded(
                        (ImTextureID)snowTexture.Handle,
                        snowPos,
                        snowPosMax,
                        new Vector2(0, 0),
                        new Vector2(1, 1),
                        ImGui.GetColorU32(new Vector4(1, 1, 1, 1.0f)), // No transparency
                        4f * scale, // Small rounded corners
                        ImDrawFlags.RoundCornersAll
                    );
                }
            }
        }

        /// <summary>
        /// Hover overlay for cards with UseGlitchNameEffect on: continuous
        /// scanlines + periodic chromatic burst. Frame FX uses cyan/magenta
        /// (chassis); name FX uses nameplate+white+black (design rule).
        /// </summary>
        private void DrawCharacterCardGlitchOverlay(
            ImDrawListPtr drawList,
            Vector2 cardMin,
            Vector2 frameMax,
            float cardWidth,
            float imageHeight,
            float scale,
            float hoverAmount,
            Vector3 nameplateColor,
            int seedHash)
        {
            if (hoverAmount < 0.05f) return;

            float t = (float)ImGui.GetTime();
            float left = cardMin.X;
            float right = cardMin.X + cardWidth;
            float top = cardMin.Y;
            float imgBottom = cardMin.Y + imageHeight;

            // ── Layer 1: CRT scanlines on the image area (always on while hovered) ──
            float scanStep = 3f * scale;
            float scanDrift = (t * 8f) % scanStep;
            uint scanU = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.22f * hoverAmount));
            for (float y = top - scanStep + scanDrift; y < imgBottom; y += scanStep)
            {
                if (y < top || y > imgBottom) continue;
                drawList.AddRectFilled(
                    new Vector2(left, y),
                    new Vector2(right, y + 1f * scale),
                    scanU);
            }
            // Faint nameplate-colour phosphor tint on every other line.
            float tintStep = scanStep * 2f;
            uint tintU = ImGui.ColorConvertFloat4ToU32(new Vector4(
                nameplateColor.X, nameplateColor.Y, nameplateColor.Z, 0.08f * hoverAmount));
            for (float y = top - tintStep + (scanDrift * 2f) % tintStep; y < imgBottom; y += tintStep)
            {
                if (y < top || y > imgBottom) continue;
                drawList.AddRectFilled(
                    new Vector2(left, y + 1f * scale),
                    new Vector2(right, y + 2f * scale),
                    tintU);
            }

            // ── Layer 2: Periodic frame-glitch burst ──
            const float burstPeriod = 4.0f;
            const float burstWindow = 0.60f;
            float phaseOffset = (seedHash & 0xFF) / 255f * burstPeriod;
            float cyclePos = (t + phaseOffset) % burstPeriod;
            float burstE = cyclePos < burstWindow
                ? MathF.Sin(cyclePos / burstWindow * MathF.PI)
                : 0f;
            burstE *= hoverAmount;

            if (burstE > 0.05f)
            {
                int bucket = (int)(t * 16) ^ seedHash;
                var rng = new Random(bucket);

                // Edge-only glitch slivers. Small, tightly clustered to the perimeter,
                // filled in the page shell colour so the card edges look TORN. Each
                // sliver gets a chromatic fringe (magenta inward-side / cyan outward-
                // side) for the chassis-corruption feel - never crosses the body.
                Vector4 shellV = Boutique.Shell;
                uint shellU    = ImGui.ColorConvertFloat4ToU32(new Vector4(shellV.X, shellV.Y, shellV.Z, 1f));
                float fringeA  = 0.92f * burstE;
                uint magentaU  = ImGui.ColorConvertFloat4ToU32(new Vector4(
                    Boutique.GlitchMagenta.X, Boutique.GlitchMagenta.Y, Boutique.GlitchMagenta.Z, fringeA));
                uint cyanU     = ImGui.ColorConvertFloat4ToU32(new Vector4(
                    Boutique.GlitchCyan.X,    Boutique.GlitchCyan.Y,    Boutique.GlitchCyan.Z,    fringeA));

                float frameTop    = cardMin.Y;
                float frameBottom = frameMax.Y;
                float frameLeft   = cardMin.X;
                float frameRight  = frameMax.X;
                float frameH      = frameBottom - frameTop;
                // Edge band is the inward depth slivers can eat into the card.
                // 4-7px scaled keeps the corruption hugging the perimeter.
                float maxBite     = 9f * scale;

                int slivers = 8 + (int)(burstE * 10f);   // 8-18 slivers per burst peak
                for (int i = 0; i < slivers; i++)
                {
                    int edge = rng.Next(4);   // 0=top 1=bottom 2=left 3=right
                    float bite = (2f + (float)rng.NextDouble() * (maxBite - 2f));

                    if (edge == 0)
                    {
                        // Top edge: small horizontal sliver biting downward
                        float sw = (6f + (float)rng.NextDouble() * 30f) * scale;
                        float sx = frameLeft + (float)rng.NextDouble() * (cardWidth - sw);
                        var mn = new Vector2(sx, frameTop);
                        var mx = new Vector2(sx + sw, frameTop + bite);
                        drawList.AddRectFilled(mn, mx, shellU);
                        // Magenta fringe on the inward (bottom) edge of the sliver
                        drawList.AddRectFilled(
                            new Vector2(mn.X, mx.Y),
                            new Vector2(mx.X, mx.Y + 1f * scale),
                            magentaU);
                    }
                    else if (edge == 1)
                    {
                        // Bottom edge: small horizontal sliver biting upward
                        float sw = (6f + (float)rng.NextDouble() * 30f) * scale;
                        float sx = frameLeft + (float)rng.NextDouble() * (cardWidth - sw);
                        var mn = new Vector2(sx, frameBottom - bite);
                        var mx = new Vector2(sx + sw, frameBottom);
                        drawList.AddRectFilled(mn, mx, shellU);
                        // Cyan fringe on the inward (top) edge of the sliver
                        drawList.AddRectFilled(
                            new Vector2(mn.X, mn.Y - 1f * scale),
                            new Vector2(mx.X, mn.Y),
                            cyanU);
                    }
                    else if (edge == 2)
                    {
                        // Left edge: small vertical sliver biting rightward
                        float sh = (6f + (float)rng.NextDouble() * 26f) * scale;
                        float sy = frameTop + (float)rng.NextDouble() * (frameH - sh);
                        var mn = new Vector2(frameLeft, sy);
                        var mx = new Vector2(frameLeft + bite, sy + sh);
                        drawList.AddRectFilled(mn, mx, shellU);
                        // Magenta fringe on the inward (right) edge
                        drawList.AddRectFilled(
                            new Vector2(mx.X, mn.Y),
                            new Vector2(mx.X + 1f * scale, mx.Y),
                            magentaU);
                    }
                    else
                    {
                        // Right edge: small vertical sliver biting leftward
                        float sh = (6f + (float)rng.NextDouble() * 26f) * scale;
                        float sy = frameTop + (float)rng.NextDouble() * (frameH - sh);
                        var mn = new Vector2(frameRight - bite, sy);
                        var mx = new Vector2(frameRight, sy + sh);
                        drawList.AddRectFilled(mn, mx, shellU);
                        // Cyan fringe on the inward (left) edge
                        drawList.AddRectFilled(
                            new Vector2(mn.X - 1f * scale, mn.Y),
                            new Vector2(mn.X, mx.Y),
                            cyanU);
                    }
                }

                // Tiny chromatic spill specks just OUTSIDE the frame perimeter -
                // the corruption bleeding outward. Pixel-sized, clustered to edges.
                int specks = 28;
                for (int sp = 0; sp < specks; sp++)
                {
                    int side = rng.Next(4);
                    float sx, sy;
                    switch (side)
                    {
                        case 0: // above top
                            sx = frameLeft + (float)rng.NextDouble() * cardWidth;
                            sy = frameTop  - (float)rng.NextDouble() * 6f * scale;
                            break;
                        case 1: // below bottom
                            sx = frameLeft + (float)rng.NextDouble() * cardWidth;
                            sy = frameBottom + (float)rng.NextDouble() * 6f * scale;
                            break;
                        case 2: // left of left
                            sx = frameLeft - (float)rng.NextDouble() * 6f * scale;
                            sy = frameTop  + (float)rng.NextDouble() * frameH;
                            break;
                        default: // right of right
                            sx = frameRight + (float)rng.NextDouble() * 6f * scale;
                            sy = frameTop   + (float)rng.NextDouble() * frameH;
                            break;
                    }
                    int variant = rng.Next(3);
                    uint specU = variant switch
                    {
                        0 => magentaU,
                        1 => cyanU,
                        _ => ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.85f * burstE)),
                    };
                    drawList.AddRectFilled(
                        new Vector2(sx, sy),
                        new Vector2(sx + 1f * scale, sy + 1f * scale),
                        specU);
                }
            }
        }

        private void DrawCharacterCardChocolateOverlay(ImDrawListPtr drawList, Vector2 cardMin, float cardWidth, float imageHeight, float scale, float hoverAmount)
        {
            // Load chocolate.png from Assets folder
            string pluginDirectory = plugin.PluginDirectory;
            string chocolateImagePath = Path.Combine(pluginDirectory, "Assets", "chocolate.png");

            if (File.Exists(chocolateImagePath))
            {
                var chocolateTexture = Plugin.TextureProvider.GetFromFile(chocolateImagePath).GetWrapOrDefault();

                if (chocolateTexture != null)
                {
                    // Calculate chocolate overlay size and position for top left corner
                    float chocolateSize = 100f * scale; // About double the snow size
                    // Position so top of chocolate is just below the glow frame
                    var borderMargin = (4f + (hoverAmount * 2f)) * scale;
                    float extraOffsetUp = -1f * scale; // Just inside the glow frame
                    float extraOffsetLeft = -10f * scale; // Moved right into the card
                    Vector2 chocolatePos = cardMin - new Vector2(borderMargin + extraOffsetLeft, borderMargin + extraOffsetUp);
                    Vector2 chocolatePosMax = chocolatePos + new Vector2(chocolateSize, chocolateSize);

                    // Draw chocolate overlay
                    drawList.AddImageRounded(
                        (ImTextureID)chocolateTexture.Handle,
                        chocolatePos,
                        chocolatePosMax,
                        new Vector2(0, 0),
                        new Vector2(1, 1),
                        ImGui.GetColorU32(new Vector4(1, 1, 1, 1.0f)),
                        4f * scale,
                        ImDrawFlags.RoundCornersAll
                    );
                }
            }
        }

        private void DrawEffects()
        {
            foreach (var kvp in characterFavoriteEffects.ToList())
            {
                kvp.Value.Draw();

                if (!kvp.Value.IsActive)
                {
                    characterFavoriteEffects.Remove(kvp.Key);
                }
            }
        }

        private void DrawToolbar(float scale)
        {
            if (!plugin.IsAddCharacterWindowOpen)
            {
                float buttonHeight = 25f * scale;

                if (ImGui.Button("Add Character", new Vector2(0, buttonHeight)))
                {
                    var io = ImGui.GetIO();
                    bool isSecretMode = io.KeyCtrl && io.KeyShift;

                    plugin.OpenAddCharacterWindow();

                    if (isSecretMode)
                    {
                        plugin.IsSecretMode = isSecretMode;
                    }
                    InvalidateCache();
                }

                plugin.AddCharacterButtonPos = ImGui.GetItemRectMin();
                plugin.AddCharacterButtonSize = ImGui.GetItemRectSize();
                uiStyles.ApplyHoverSheenToLastItem("addcharbtn");

                // Inline pagination: placed on the same row as Add Character / filter / search,
                // centered horizontally in the window. Lives inside the toolbar row so it doesn't
                // add any vertical footprint of its own.
                DrawInlinePagination(scale);

                DrawSearchAndFilters(scale);
            }
        }

        private void DrawSearchAndFilters(float scale)
        {
            float tagDropdownWidth = 200f * scale;
            float tagIconOffset = 70f * scale;
            float tagDropdownOffset = tagDropdownWidth + tagIconOffset + (10f * scale);
            float buttonSize = 25f * scale;

            // Tag Filter Toggle (hidden when search bar is open)
            if (!showSearchBar)
            {
                ImGui.SameLine(ImGui.GetWindowWidth() - tagIconOffset - (20f * scale));
                if (uiStyles.IconButton("\uf0b0", "Filter by Tags"))
                {
                    showTagFilter = !showTagFilter;
                    InvalidateCache();
                }

                // Tag Filter Dropdown
                if (showTagFilter)
                {
                    ImGui.SameLine(ImGui.GetWindowWidth() - tagDropdownOffset - (20f * scale));
                    ImGui.SetNextItemWidth(tagDropdownWidth);
                    if (ImGui.BeginCombo("##TagFilter", selectedTag))
                    {
                        var allTags = plugin.Characters
                            .SelectMany(c => c.Tags ?? new List<string>())
                            .Distinct()
                            .OrderBy(f => f)
                            .Prepend("All")
                            .ToList();

                        foreach (var tag in allTags)
                        {
                            bool isSelected = tag == selectedTag;
                            if (ImGui.Selectable(tag, isSelected))
                            {
                                selectedTag = tag;
                                InvalidateFilterCache();
                            }

                            if (isSelected)
                                ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }
                }
            }

            // Search Button
            ImGui.SameLine(ImGui.GetWindowWidth() - (55f * scale));
            if (uiStyles.IconButton("\uf002", "Search for a Character"))
            {
                showSearchBar = !showSearchBar;
                if (!showSearchBar)
                {
                    searchQuery = "";
                    InvalidateFilterCache();
                }
                else
                {
                    // Close tag filter when opening search
                    showTagFilter = false;
                }
            }

            // Search Input Field
            if (showSearchBar)
            {
                ImGui.SameLine(ImGui.GetWindowWidth() - (265f * scale));
                ImGui.SetNextItemWidth(210f * scale);
                if (ImGui.InputTextWithHint("##SearchCharacters", "Search characters...", ref searchQuery, 100))
                    InvalidateFilterCache();
            }
        }

        private void DrawCharacterGridContent(float scale)
        {
            // On startup, navigate to the page containing the last-applied
            // character. Retries each frame until resolved because the player
            // might not be logged in on the very first draw.
            if (!hasNavigatedToActiveOnStartup)
            {
                Character? active = plugin.GetActiveCharacter() ?? plugin.activeCharacter;

                // If nothing is applied yet, look up the persisted last-used character
                if (active == null)
                {
                    if (Plugin.ObjectTable?.LocalPlayer is { } lp && lp.HomeWorld.IsValid)
                    {
                        string fullKey = $"{lp.Name.TextValue}@{lp.HomeWorld.Value.Name}";
                        if (plugin.Configuration.LastUsedCharacterByPlayer.TryGetValue(fullKey, out var lastKey))
                        {
                            var charName = lastKey.Contains('@') ? lastKey.Split('@')[0] : lastKey;
                            active = plugin.Characters.FirstOrDefault(c =>
                                c.Name.Equals(charName, StringComparison.OrdinalIgnoreCase));
                        }
                    }
                    // Fallback: global last-used key
                    if (active == null && !string.IsNullOrEmpty(plugin.Configuration.LastUsedCharacterKey))
                    {
                        active = plugin.Characters.FirstOrDefault(c =>
                            c.Name.Equals(plugin.Configuration.LastUsedCharacterKey, StringComparison.OrdinalIgnoreCase));
                    }
                }

                if (active != null)
                {
                    hasNavigatedToActiveOnStartup = true;
                    var filtered = GetFilteredCharacters();
                    int idx = filtered.IndexOf(active);
                    if (idx >= 0 && charactersPerPage > 0)
                    {
                        currentPage = idx / charactersPerPage;
                        scrollToTopOnNextFrame = true;
                        InvalidateCache();
                    }
                }
            }

            // Reset scroll to top when page changes
            if (scrollToTopOnNextFrame)
            {
                ImGui.SetScrollY(0);
                scrollToTopOnNextFrame = false;
            }

            // Capture viewport bounds for the new-character reveal overlay
            // (in absolute screen coords so the foreground draw list can use
            // them directly).  Updated each frame in case the window moves.
            if (_newAnimStart >= 0f)
            {
                _newAnimViewportMin = ImGui.GetWindowPos();
                _newAnimViewportMax = _newAnimViewportMin + ImGui.GetWindowSize();
            }

            var filteredCharacters = GetFilteredCharacters();
            var pagedCharacters = GetPagedCharacters(filteredCharacters);

            float availableWidth = ImGui.GetContentRegionAvail().X;
            if (Math.Abs(availableWidth - cachedAvailableWidth) > 1f ||
                Math.Abs(scale - cachedScale) > 0.01f ||
                layoutCacheDirty)
            {
                RecalculateLayout(availableWidth, scale);
            }

            float cardWidth = cachedCardWidth;
            int columnCount = cachedColumnCount;

            // Centre the grid horizontally
            float columnWidth = cardWidth + (plugin.ProfileSpacing * scale) + (24f * scale);
            float totalGridWidth = columnCount > 1
                ? columnCount * columnWidth
                : cardWidth;
            // Per user feedback: there's noticeably more empty space on the
            // left than the right, so nudge the grid 10px leftward by lowering
            // both the minimum indent and the centred indent by the same amount.
            float horizontalIndent = Math.Max(7f * scale, (availableWidth - totalGridWidth) / 2f - 10f * scale);
            float verticalMargin = 17f * scale;

            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + verticalMargin);
            ImGui.Indent(horizontalIndent);

            if (columnCount > 1)
            {
                // Per-count column-set ID so ImGui doesn't reuse cached widths
                // across resizes (caused the grid to get stuck at 2 columns)
                ImGui.Columns(columnCount, $"CharacterGrid_{columnCount}", false);
                for (int i = 0; i < columnCount; i++)
                {
                    ImGui.SetColumnWidth(i, columnWidth);
                }
            }

            bool shouldRebuildRects = cardRectsDirty || isDragging || pagedCharacters.Count != cardRects.Count;

            if (shouldRebuildRects)
            {
                RebuildCardRects(pagedCharacters, cardWidth, scale);
            }

            // Check if any non-active card is hovered - if so, the active
            // card's always-on streak yields so only one card glows at a time.
            var gridActiveChar = plugin.GetActiveCharacter() ?? plugin.activeCharacter;
            anyNonActiveCardHovered = false;
            foreach (var pc in pagedCharacters)
            {
                if (pc == gridActiveChar) continue;
                int ri = plugin.Characters.IndexOf(pc);
                if (ri >= 0 && hoverAnimations.TryGetValue(ri, out var ha) && ha > 0.15f)
                {
                    anyNonActiveCardHovered = true;
                    break;
                }
            }

            // Draw character cards
            for (int i = 0; i < pagedCharacters.Count; i++)
            {
                var character = pagedCharacters[i];
                int realCharacterIndex = plugin.Characters.IndexOf(character);
                if (realCharacterIndex == -1) continue;

                DrawCharacterCard(character, realCharacterIndex, cardWidth, scale);

                if (columnCount > 1)
                    ImGui.NextColumn();
            }

            // Reset columns
            if (columnCount > 1)
            {
                ImGui.Columns(1);
            }

            // Drain any pop-out cutout draws, done AFTER all cards render so
            // the cutout paints over neighbouring cards rather than being
            // clipped at their boundary.
            DrainPendingCutouts();

            // New-character reveal overlay, drawn last so it sits above
            // everything, including pop-out cutouts.
            DrawNewCharacterAnimationOverlay(scale);

            ImGui.Unindent(horizontalIndent);
        }

        private void DrainPendingCutouts()
        {
            if (pendingCutouts.Count == 0) return;
            var dl = ImGui.GetWindowDrawList();
            foreach (var pc in pendingCutouts)
            {
                var tex = Plugin.TextureProvider.GetFromFile(pc.CutoutPath).GetWrapOrDefault();
                if (tex == null) continue;

                // Width-driven sizing, cutout width = card width × CutoutScale
                // (per-character, default 3.25). Height follows native aspect.
                var slipSize = pc.SlipMax - pc.SlipMin;
                var portraitSize = pc.ImgMax - pc.ImgMin;
                float scaleAmt = 0.85f + 0.15f * pc.HoverAmount;

                float dispW = slipSize.X * pc.Scale * scaleAmt;
                float imgAR = tex.Width / (float)tex.Height;
                float dispH = dispW / imgAR;
                var poseSize = new Vector2(dispW, dispH);

                // Anchor: per-character card-side (X, Y), HARDCODED pose-side
                // at bottom-center of the image (matches the form preview).
                // pc.PoseAx/Ay are ignored at render time so characters saved
                // before the defaults changed don't render mispositioned.
                var anchorWorld = pc.ImgMin + new Vector2(portraitSize.X * pc.AnchorX, portraitSize.Y * pc.AnchorY);
                var poseMin = anchorWorld - new Vector2(poseSize.X * 0.5f, poseSize.Y * 1.0f);
                var poseMax = poseMin + poseSize;

                // Smooth alpha fade, quick ramp to full opacity (fully
                // opaque by ~25% hover) so the cutout doesn't look ghosty
                // mid-hover.  Curve crosses zero exactly at the queue gate
                // threshold (0.05) so the off transition reaches alpha=0
                // before the queue cuts off, eliminating the snap.
                float a = MathF.Max(0f, MathF.Min(1f, (pc.HoverAmount - 0.05f) * 5f));
                uint tint = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, a));
                dl.AddImage(tex.Handle, poseMin, poseMax, Vector2.Zero, Vector2.One, tint);
            }
            pendingCutouts.Clear();
        }

        // ── New-character reveal animation ─────────────────────────────────

        public void PlayNewCharacterAnimation(Character newChar)
        {
            if (newChar == null) return;
            _newAnimCharName = newChar.Name;
            _newAnimStart = (float)ImGui.GetTime();
            _newAnimSlotCaptured = false;
            _newAnimScrolledToSlot = false;

            // Force-navigate to the page where the new character will live
            // so the LAND target is on-screen.
            InvalidateCache();
            cardRectsDirty = true;
            var filtered = GetFilteredCharacters();
            int idx = filtered.IndexOf(newChar);
            if (idx >= 0 && charactersPerPage > 0)
                currentPage = idx / charactersPerPage;
        }

        private static float NewAnimEaseOutCubic(float t) => 1f - MathF.Pow(1f - t, 3f);
        private static float NewAnimEaseInCubic(float t)  => t * t * t;

        // Anisotropic squash bounce on landing: separate X and Y scale waves
        // so the card visibly compresses vertically + stretches horizontally
        // on impact, then rebounds.  u in [0,1] across the LAND phase.
        //   0.00 → 0.20  IMPACT  scaleY 1.00 → 0.78,  scaleX 1.00 → 1.10
        //   0.20 → 0.55  REBOUND scaleY 0.78 → 1.06,  scaleX 1.10 → 0.96
        //   0.55 → 1.00  SETTLE  → 1.00 / 1.00
        private static (float sx, float sy) NewAnimSquash(float u)
        {
            if (u < 0.20f)
            {
                float p = u / 0.20f;
                float eased = NewAnimEaseOutCubic(p);
                return (1.00f + 0.10f * eased, 1.00f - 0.22f * eased);
            }
            if (u < 0.55f)
            {
                float p = (u - 0.20f) / 0.35f;
                float eased = NewAnimEaseOutCubic(p);
                float sx = 1.10f + (0.96f - 1.10f) * eased;
                float sy = 0.78f + (1.06f - 0.78f) * eased;
                return (sx, sy);
            }
            // SETTLE
            float q = (u - 0.55f) / 0.45f;
            float qe = NewAnimEaseOutCubic(q);
            return (0.96f + 0.04f * qe, 1.06f + (1.00f - 1.06f) * qe);
        }

        private void DrawNewCharacterAnimationOverlay(float scale)
        {
            if (_newAnimStart < 0f || string.IsNullOrEmpty(_newAnimCharName)) return;

            float t = (float)ImGui.GetTime() - _newAnimStart;
            if (t > NewAnimDur)
            {
                _newAnimStart = -1f;
                _newAnimCharName = null;
                _newAnimSlotCaptured = false;
                _newAnimScrolledToSlot = false;
                return;
            }

            // Wait until the placeholder render has captured the slot rect on
            // its first frame; otherwise we don't yet know where to fly to.
            if (!_newAnimSlotCaptured) return;

            var character = plugin.Characters.FirstOrDefault(c => c.Name == _newAnimCharName);
            if (character == null) { _newAnimStart = -1f; _newAnimCharName = null; return; }

            var dl = ImGui.GetForegroundDrawList();

            // ── Phase: position, scale, alpha ──
            Vector2 viewportCentre = (_newAnimViewportMin + _newAnimViewportMax) * 0.5f;
            Vector2 slotCentre     = (_newAnimSlotMin + _newAnimSlotMax) * 0.5f;

            float scaleX, scaleY;
            float alpha;
            Vector2 centre;
            const float HoldScale = 1.15f;

            if (t < NewAnimMaterializeEnd)
            {
                float u = t / NewAnimMaterializeEnd;
                // Quintic ease-out: starts moving immediately but trails off
                // very gently into HOLD, feels softer than cubic.
                float eased = 1f - MathF.Pow(1f - u, 5f);
                // Larger starting scale so the materialise reads as a smooth
                // bloom rather than a sudden pop.
                float s = 0.70f + (HoldScale - 0.70f) * eased;
                scaleX = scaleY = s;
                alpha = eased;
                centre = viewportCentre;
            }
            else if (t < NewAnimHoldEnd)
            {
                scaleX = scaleY = HoldScale;
                alpha  = 1f;
                centre = viewportCentre;
            }
            else if (t < NewAnimFlyEnd)
            {
                float u = (t - NewAnimHoldEnd) / (NewAnimFlyEnd - NewAnimHoldEnd);
                float eased = NewAnimEaseInCubic(u);
                // Hold large for the first 60% of the flight, shrink rapidly
                // in the final 40% so the card "crashes down to size" near
                // the slot rather than fading away.
                float shrinkU = MathF.Max(0f, (u - 0.60f) / 0.40f);
                float shrinkE = NewAnimEaseInCubic(shrinkU);
                float s = HoldScale + (1.00f - HoldScale) * shrinkE;
                scaleX = scaleY = s;
                alpha = 1f;
                centre = new Vector2(
                    viewportCentre.X + (slotCentre.X - viewportCentre.X) * eased,
                    viewportCentre.Y + (slotCentre.Y - viewportCentre.Y) * eased);
                // Slight upward arc over the path so it doesn't read as a flat slide
                float arcLift = MathF.Sin(eased * MathF.PI) * 18f * scale;
                centre.Y -= arcLift;
            }
            else
            {
                float u = (t - NewAnimFlyEnd) / (NewAnimDur - NewAnimFlyEnd);
                var sq = NewAnimSquash(u);
                scaleX = sq.sx;
                scaleY = sq.sy;
                alpha  = 1f;
                centre = slotCentre;
            }

            // ── Backdrop dim ── (fades during fly, gone by impact)
            float dimEnv;
            if      (t < NewAnimMaterializeEnd)  dimEnv = t / NewAnimMaterializeEnd;
            else if (t < NewAnimHoldEnd)         dimEnv = 1f;
            else if (t < NewAnimFlyEnd)          dimEnv = 1f - (t - NewAnimHoldEnd) / (NewAnimFlyEnd - NewAnimHoldEnd);
            else                                 dimEnv = 0f;
            dimEnv = MathF.Min(1f, MathF.Max(0f, dimEnv)) * 0.50f;
            if (dimEnv > 0f)
            {
                uint dimCol = ImGui.GetColorU32(new Vector4(0.02f, 0.025f, 0.04f, dimEnv));
                dl.AddRectFilled(_newAnimViewportMin, _newAnimViewportMax, dimCol);
            }

            // ── Aurora halo (boutique signature glow with peak/N alpha falloff) ──
            float haloEnv;
            if      (t < NewAnimMaterializeEnd)  haloEnv = t / NewAnimMaterializeEnd;
            else if (t < NewAnimHoldEnd)         haloEnv = 1f;
            else if (t < NewAnimFlyEnd)          haloEnv = 1f - (t - NewAnimHoldEnd) / (NewAnimFlyEnd - NewAnimHoldEnd);
            else                                 haloEnv = 0f;
            if (haloEnv > 0.02f)
            {
                float pulse = 0.85f + 0.15f * MathF.Sin((float)ImGui.GetTime() * 2.6f);
                float rx = _newAnimCardW * 0.95f * scaleX * pulse;
                float ry = _newAnimCardH * 0.85f * scaleY * pulse;
                var haloCol = new Vector4(Boutique.Gold.X, Boutique.Gold.Y, Boutique.Gold.Z, 0.55f * haloEnv);
                Boutique.DrawAuroraSpot(dl, centre, rx, ry, haloCol, layers: 14);
            }


            // ── Hero card (slip + portrait + nameplate + name) ──
            float heroW = _newAnimCardW * scaleX;
            float heroH = _newAnimCardH * scaleY;
            float heroScale = scale * MathF.Max(scaleX, scaleY);
            var heroMin = centre - new Vector2(heroW * 0.5f, heroH * 0.5f);
            var heroMax = centre + new Vector2(heroW * 0.5f, heroH * 0.5f);

            var npCol = new Vector4(character.NameplateColor.X, character.NameplateColor.Y, character.NameplateColor.Z, alpha);
            Boutique.DrawCardSlip(dl, heroMin, heroMax, npCol, isHovered: false, isApplied: false, heroScale);

            // Portrait
            float portraitInset = 4f * heroScale;
            var imgMin = heroMin + new Vector2(portraitInset, portraitInset);
            // Use scaleY for the image height so the portrait squashes vertically with the card
            float heroImageH = _newAnimCardW * scaleX;     // square portrait scales with X
            var imgMax = new Vector2(heroMax.X - portraitInset, heroMin.Y + heroImageH - portraitInset);
            // But never let it overflow past the nameplate strip
            float baseImageH = _newAnimCardW;
            float baseNameH  = _newAnimCardH - baseImageH;
            float heroNpH    = baseNameH * scaleY;
            var imgMax_y     = heroMax.Y - heroNpH;
            imgMax = new Vector2(heroMax.X - portraitInset, imgMax_y - portraitInset);

            string defaultImagePath = Path.Combine(plugin.PluginDirectory, "Assets", "Default.png");
            string heroImagePath = GetCachedImagePath(character.ImagePath, defaultImagePath);
            if (!string.IsNullOrEmpty(heroImagePath))
            {
                var tex = Plugin.TextureProvider.GetFromFile(heroImagePath).GetWrapOrDefault();
                if (tex != null && tex.Width > 0 && tex.Height > 0)
                {
                    float ar = (float)tex.Width / tex.Height;
                    float boxW = imgMax.X - imgMin.X;
                    float boxH = imgMax.Y - imgMin.Y;
                    float dispW, dispH;
                    if (ar > boxW / boxH) { dispW = boxW; dispH = boxW / ar; }
                    else                  { dispH = boxH; dispW = boxH * ar; }
                    // Apply per-character zoom + offset so the hero matches
                    // what the live grid card will render (otherwise the
                    // pop-up shows the unzoomed/centred image).
                    dispW *= character.PortraitZoom;
                    dispH *= character.PortraitZoom;
                    var portraitOffset = new Vector2(
                        boxW * character.PortraitOffsetX,
                        boxH * character.PortraitOffsetY);
                    var imgC = (imgMin + imgMax) * 0.5f;
                    var dispMin = imgC - new Vector2(dispW * 0.5f, dispH * 0.5f) + portraitOffset;
                    var dispMax = imgC + new Vector2(dispW * 0.5f, dispH * 0.5f) + portraitOffset;
                    uint imgTint = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha));
                    dl.PushClipRect(imgMin, imgMax, true);
                    dl.AddImage(tex.Handle, dispMin, dispMax, Vector2.Zero, Vector2.One, imgTint);
                    dl.PopClipRect();
                }
            }

            // Nameplate strip
            var npMin = new Vector2(heroMin.X + portraitInset, imgMax_y);
            var npMax = new Vector2(heroMax.X - portraitInset, heroMax.Y - portraitInset);
            uint npBg = ImGui.GetColorU32(new Vector4(Boutique.Surface0.X, Boutique.Surface0.Y, Boutique.Surface0.Z, alpha));
            dl.AddRectFilled(npMin, npMax, npBg);
            uint npHair = ImGui.GetColorU32(new Vector4(npCol.X, npCol.Y, npCol.Z, 0.7f * alpha));
            dl.AddLine(new Vector2(npMin.X + 4f * heroScale, npMin.Y),
                       new Vector2(npMax.X - 4f * heroScale, npMin.Y), npHair, 2f * heroScale);

            // Name text centred in the nameplate
            ImFontPtr nameFont = ImGui.GetFont();
            float nameFontSize = MathF.Min(18f * heroScale, ImGui.GetFontSize() * MathF.Max(scaleX, scaleY));
            var nameMeasure = ImGui.CalcTextSize(character.Name);
            float drawScale = nameFontSize / ImGui.GetFontSize();
            var displaySize = nameMeasure * drawScale;
            var npRectCentre = (npMin + npMax) * 0.5f;
            var namePos = new Vector2(npRectCentre.X - displaySize.X * 0.5f,
                                      npRectCentre.Y - displaySize.Y * 0.5f);
            uint nameCol = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha));
            if (character.UseGlitchNameEffect && plugin.GlitchFont != null && plugin.GlitchFont.Available)
            {
                NameStylizer.Draw(dl, namePos, NameStylizer.Render(character.Name), character.NameplateColor, alpha,
                    useGlitch: true, glitchFont: plugin.GlitchFont, seedHash: NameStylizer.Hash(character.Name));
            }
            else
            {
                dl.AddText(nameFont, nameFontSize, namePos, nameCol, character.Name);
            }

            // ── Sparkle burst (radial diamonds drifting outward during MATERIALISE + early HOLD) ──
            float sparkleEnv;
            if      (t < NewAnimMaterializeEnd)  sparkleEnv = t / NewAnimMaterializeEnd;
            else if (t < NewAnimMaterializeEnd + 0.30f)
                sparkleEnv = 1f - (t - NewAnimMaterializeEnd) / 0.30f;
            else sparkleEnv = 0f;
            sparkleEnv = MathF.Min(1f, MathF.Max(0f, sparkleEnv));
            if (sparkleEnv > 0.05f)
            {
                int sparkleCount = 10;
                float baseDist = 60f * scale;
                float driftDist = 40f * scale * (1f - sparkleEnv);   // travels further as anim progresses
                float sparkSize = 4f * scale * sparkleEnv;
                uint sparkCore = ImGui.GetColorU32(new Vector4(Boutique.GoldBright.X, Boutique.GoldBright.Y, Boutique.GoldBright.Z, sparkleEnv));
                uint sparkHalo = ImGui.GetColorU32(new Vector4(Boutique.Gold.X, Boutique.Gold.Y, Boutique.Gold.Z, sparkleEnv * 0.55f));
                for (int i = 0; i < sparkleCount; i++)
                {
                    float a = i * (MathF.PI * 2f / sparkleCount) + (i % 2) * 0.30f;
                    float dist = baseDist + driftDist;
                    var sp = centre + new Vector2(MathF.Cos(a) * dist, MathF.Sin(a) * dist * 0.85f);
                    // Diamond: 4 small triangles around the centre
                    Span<Vector2> dia = stackalloc Vector2[4]
                    {
                        new Vector2(sp.X, sp.Y - sparkSize * 1.4f),
                        new Vector2(sp.X + sparkSize, sp.Y),
                        new Vector2(sp.X, sp.Y + sparkSize * 1.4f),
                        new Vector2(sp.X - sparkSize, sp.Y),
                    };
                    unsafe { fixed (Vector2* p = dia) dl.AddConvexPolyFilled(p, 4, sparkHalo); }
                    unsafe { fixed (Vector2* p = dia) dl.AddConvexPolyFilled(p, 4, sparkCore); }
                }
            }

            // ── "NEW" tag during HOLD ──
            if (t > NewAnimMaterializeEnd && t < NewAnimHoldEnd)
            {
                float tagU = (t - NewAnimMaterializeEnd) / (NewAnimHoldEnd - NewAnimMaterializeEnd);
                float tagAlpha = MathF.Sin(tagU * MathF.PI) * alpha;
                if (tagAlpha > 0.02f)
                {
                    string newLabel = "NEW";
                    ImFontPtr tagFont = nameFont;
                    using (Plugin.Instance?.OswaldSemi13?.Push()) { tagFont = ImGui.GetFont(); }
                    float tagFontSize = 26f * heroScale;
                    var tagMeasure = ImGui.CalcTextSize(newLabel);
                    float tagDS = tagFontSize / ImGui.GetFontSize();
                    var tagDisplay = tagMeasure * tagDS;
                    var tagPos = new Vector2(centre.X - tagDisplay.X * 0.5f,
                                             heroMin.Y - tagDisplay.Y - 10f * heroScale);
                    uint tagShadow = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f * tagAlpha));
                    uint tagCol = ImGui.GetColorU32(new Vector4(Boutique.GoldBright.X, Boutique.GoldBright.Y, Boutique.GoldBright.Z, tagAlpha));
                    dl.AddText(tagFont, tagFontSize, tagPos + new Vector2(2f * heroScale, 2f * heroScale), tagShadow, newLabel);
                    dl.AddText(tagFont, tagFontSize, tagPos, tagCol, newLabel);
                }
            }

            // ── IMPACT FLASH at the slot the moment LAND begins ──
            if (t >= NewAnimFlyEnd && t < NewAnimFlyEnd + 0.18f)
            {
                float fu = (t - NewAnimFlyEnd) / 0.18f;
                float fAlpha = (1f - fu) * 0.75f;
                float fRadius = (24f + 80f * fu) * scale;
                var flashCol = new Vector4(Boutique.GoldBright.X, Boutique.GoldBright.Y, Boutique.GoldBright.Z, fAlpha);
                Boutique.DrawAuroraSpot(dl, slotCentre, fRadius, fRadius * 0.85f, flashCol, layers: 16);
            }

            // ── Shockwave rings expanding from the slot during LAND ──
            if (t >= NewAnimFlyEnd)
            {
                float landU = (t - NewAnimFlyEnd) / (NewAnimDur - NewAnimFlyEnd);
                // Two rings, second offset for trailing wave feel
                for (int r = 0; r < 2; r++)
                {
                    float offsetU = MathF.Max(0f, landU - r * 0.12f);
                    if (offsetU <= 0f || offsetU >= 1f) continue;
                    float radius = (20f + 180f * offsetU) * scale;
                    float ringAlpha = (1f - offsetU) * 0.85f * (r == 0 ? 1f : 0.55f);
                    uint ringCol = ImGui.GetColorU32(new Vector4(Boutique.Gold.X, Boutique.Gold.Y, Boutique.Gold.Z, ringAlpha));
                    dl.AddCircle(slotCentre, radius, ringCol, 32, (2f - r * 0.6f) * scale);
                }
            }
        }

        private void RecalculateLayout(float availableWidth, float scale)
        {
            float profileSpacing = plugin.ProfileSpacing * scale;
            int columnCount = plugin.ProfileColumns;

            if (plugin.IsDesignPanelOpen)
                columnCount = Math.Max(1, columnCount - 1);

            float cardWidth = 250 * plugin.ProfileImageScale * scale;
            float borderMargin = 12f * scale;
            float totalCardWidth = cardWidth + (borderMargin * 2);
            float columnWidth = totalCardWidth + profileSpacing;

            columnCount = Math.Max(1, Math.Min(columnCount, (int)(availableWidth / columnWidth)));

            cachedCardWidth = cardWidth;
            cachedColumnCount = columnCount;
            cachedColumnWidth = columnWidth;
            cachedAvailableWidth = availableWidth;
            cachedScale = scale;
            layoutCacheDirty = false;
        }

        private void RebuildCardRects(List<Character> pagedCharacters, float cardWidth, float scale)
        {
            cardRects.Clear();
            for (int i = 0; i < pagedCharacters.Count; i++)
            {
                var character = pagedCharacters[i];
                int realCharacterIndex = plugin.Characters.IndexOf(character);
                if (realCharacterIndex == -1) continue;

                var cardStartPos = ImGui.GetCursorScreenPos();
                float nameplateHeight = 70 * scale;
                float imageHeight = cardWidth;
                float totalCardHeight = imageHeight + nameplateHeight;
                var cardMin = cardStartPos;
                var cardMax = cardStartPos + new Vector2(cardWidth, totalCardHeight);

                cardRects.Add((realCharacterIndex, cardMin, cardMax));
            }
            cardRectsDirty = false;
        }

        private void DrawCharacterCard(Character character, int index, float cardWidth, float scale)
        {
            DrawBoutiqueCharacterCard(character, index, cardWidth, scale);
        }

        // BOUTIQUE CARD, translation of design-mockups/final/main.html .card
        private void DrawBoutiqueCharacterCard(Character character, int index, float cardWidth, float scale)
        {
            cardWidth = Math.Clamp(cardWidth, 64 * scale, 512 * scale);
            float nameplateHeight = 60f * scale;
            float imageHeight = cardWidth;
            float totalCardHeight = imageHeight + nameplateHeight;
            float spacing = 12f * scale;

            // Reveal animation: this character is in flight from centre → slot.
            // Render an empty placeholder of the same size so the grid layout
            // stays intact, capture the slot's screen rect for the LAND target,
            // scroll the grid to bring the slot into view, then bail.
            if (_newAnimStart >= 0f && character.Name == _newAnimCharName)
            {
                ImGui.BeginGroup();
                var slotPos = ImGui.GetCursorScreenPos();
                _newAnimSlotMin  = slotPos;
                _newAnimSlotMax  = slotPos + new Vector2(cardWidth, totalCardHeight);
                _newAnimCardW    = cardWidth;
                _newAnimCardH    = totalCardHeight;
                _newAnimScale    = scale;
                _newAnimSlotCaptured = true;

                // Centre the slot vertically in the grid scroll viewport on
                // the first frame after capture.  This runs in the grid child
                // window's context so SetScrollY targets the right window.
                if (!_newAnimScrolledToSlot)
                {
                    float windowTopY = ImGui.GetWindowPos().Y;
                    float windowH    = ImGui.GetWindowSize().Y;
                    float currentScroll = ImGui.GetScrollY();
                    float slotYInContent = (slotPos.Y - windowTopY) + currentScroll;
                    float targetScrollY = slotYInContent - windowH * 0.5f + totalCardHeight * 0.5f;
                    ImGui.SetScrollY(MathF.Max(0f, targetScrollY));
                    _newAnimScrolledToSlot = true;
                }

                ImGui.Dummy(new Vector2(cardWidth, totalCardHeight));
                ImGui.EndGroup();
                return;
            }

            string defaultImagePath = Path.Combine(plugin.PluginDirectory, "Assets", "Default.png");
            string finalImagePath = GetCachedImagePath(character.ImagePath, defaultImagePath);

            bool isMainCharacter = !string.IsNullOrEmpty(plugin.Configuration.MainCharacterName) &&
                                   character.Name == plugin.Configuration.MainCharacterName;
            var activeChar = plugin.GetActiveCharacter() ?? plugin.activeCharacter;
            bool isApplied = activeChar != null && activeChar == character;

            var dl = ImGui.GetWindowDrawList();
            double time = ImGui.GetTime();

            ImGui.BeginGroup();
            var cardStartPos = ImGui.GetCursorScreenPos();
            var cardMin = cardStartPos;
            var cardMax = cardStartPos + new Vector2(cardWidth, totalCardHeight);
            ImGui.Dummy(new Vector2(cardWidth, totalCardHeight));

            // Click + drag region (whole image area)
            ImGui.SetCursorScreenPos(cardMin);
            ImGui.InvisibleButton($"##CharCard{index}", new Vector2(cardWidth, imageHeight));
            bool isHovered = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !isDragging)
                HandleCharacterClick(character, index);
            if (ImGui.BeginPopupContextItem($"##ContextMenu_{character.Name}"))
            {
                DrawContextMenu(character, scale);
                ImGui.EndPopup();
            }

            float hoverAmount = UpdateHoverAnimation(index, isHovered);
            Vector2 wiggleOffset = UpdateHalloweenWiggle(index, plugin.Characters.Count);

            var slipMin = cardMin + wiggleOffset;
            var slipMax = cardMax + wiggleOffset;

            // Boutique nameplate colour: prefer character.NameplateColor, fall back to palette by index
            Vector4 npCol;
            if (character.NameplateColor.LengthSquared() > 0.001f)
                npCol = new Vector4(character.NameplateColor.X, character.NameplateColor.Y, character.NameplateColor.Z, 1f);
            else
                npCol = Boutique.NpColorByIndex(index);

            // Custom theme override
            if (plugin.Configuration.SelectedTheme == ThemeSelection.Custom)
            {
                var customTheme = plugin.Configuration.CustomTheme;
                if (!customTheme.UseNameplateColorForCardGlow &&
                    customTheme.ColorOverrides.TryGetValue("custom.cardGlow", out var packedGlow) && packedGlow.HasValue)
                {
                    var g = CustomThemeDefinitions.UnpackColor(packedGlow.Value);
                    npCol = new Vector4(g.X, g.Y, g.Z, 1f);
                }
            }
            // Seasonal theme override (alternating per index)
            else if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration))
            {
                var effectiveTheme = SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration);
                var themeColors = SeasonalThemeManager.GetCurrentThemeColors(plugin.Configuration);
                if (effectiveTheme == SeasonalTheme.Halloween ||
                    effectiveTheme == SeasonalTheme.Winter ||
                    effectiveTheme == SeasonalTheme.Christmas)
                {
                    var p = themeColors.PrimaryAccent;
                    var s = themeColors.SecondaryAccent;
                    npCol = index % 2 == 0
                        ? new Vector4(p.X, p.Y, p.Z, 1f)
                        : new Vector4(s.X, s.Y, s.Z, 1f);
                }
                else if (effectiveTheme == SeasonalTheme.Valentines)
                {
                    npCol = new Vector4(1f, 1f, 1f, 1f);
                }
            }

            // Card slip silhouette with coloured edge
            Boutique.DrawCardSlip(dl, slipMin, slipMax, npCol,
                isHovered || hoverAmount > 0.1f, isApplied, scale);

            // Seasonal decorations behind image (preserved)
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration))
            {
                var effectiveTheme = SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration);
                if (effectiveTheme == SeasonalTheme.Winter || effectiveTheme == SeasonalTheme.Christmas)
                    DrawCharacterCardIcicles(dl, slipMin, cardWidth, imageHeight, scale);
            }

            // Image area (fills the chamfered top region)
            float portraitInset = 4f * scale;
            var imgMin = slipMin + new Vector2(portraitInset, portraitInset);
            var imgMax = new Vector2(slipMax.X - portraitInset, slipMin.Y + imageHeight - portraitInset);

            if (!string.IsNullOrEmpty(finalImagePath))
            {
                var tex = Plugin.TextureProvider.GetFromFile(finalImagePath).GetWrapOrDefault();
                if (tex != null)
                {
                    float ar = (float)tex.Width / tex.Height;
                    float boxW = imgMax.X - imgMin.X;
                    float boxH = imgMax.Y - imgMin.Y;
                    float dispW, dispH;
                    if (ar > boxW / boxH) { dispW = boxW; dispH = boxW / ar; }
                    else { dispH = boxH; dispW = boxH * ar; }

                    // Per-character zoom multiplier (applies to base portrait
                    // and GIF, for GIF we'll override with AnimatedZoom).
                    float baseZoom = character.PortraitZoom;
                    dispW *= baseZoom;
                    dispH *= baseZoom;

                    float hoverScaleAmt = plugin.Configuration.EnableCharacterHoverEffects
                        ? 1f + (0.05f * hoverAmount) : 1f;
                    dispW *= hoverScaleAmt;
                    dispH *= hoverScaleAmt;

                    // Per-character pixel offset (offsetX/Y in [-1, 1] are
                    // fractions of the portrait area).
                    var portraitOffset = new Vector2(
                        boxW * character.PortraitOffsetX,
                        boxH * character.PortraitOffsetY);

                    var imgCentre = (imgMin + imgMax) * 0.5f;
                    float liftOffset = -2f * hoverAmount * scale;
                    var dispMin = new Vector2(imgCentre.X - dispW * 0.5f, imgCentre.Y - dispH * 0.5f + liftOffset) + portraitOffset;
                    var dispMax = new Vector2(imgCentre.X + dispW * 0.5f, imgCentre.Y + dispH * 0.5f + liftOffset) + portraitOffset;

                    // Hover swap, animated GIF or pop-out cutout backdrop swap.
                    // Mutually exclusive at the form level.  Backdrop crossfades
                    // on top of the portrait using the same curve as the cutout
                    // so they appear / disappear together.  GIF still hard-swaps
                    // since it doesn't fade with the cutout system.
                    var renderHandle = tex.Handle;
                    var renderMin = dispMin;
                    var renderMax = dispMax;
                    bool cutoutActive = !string.IsNullOrWhiteSpace(character.CutoutImagePath);
                    Dalamud.Bindings.ImGui.ImTextureID backdropHandle = default;
                    bool hasBackdrop = false;
                    float backdropAlpha = 0f;
                    if (cutoutActive)
                    {
                        if ((isHovered || hoverAmount > 0.05f) && !string.IsNullOrWhiteSpace(character.CutoutBackdropPath))
                        {
                            var bgTex = Plugin.TextureProvider.GetFromFile(character.CutoutBackdropPath).GetWrapOrDefault();
                            if (bgTex != null)
                            {
                                backdropHandle = bgTex.Handle;
                                hasBackdrop = true;
                                // Match the cutout's alpha curve so the backdrop fades in/out
                                // in lockstep with the pop-out, no lingering snap-back.
                                backdropAlpha = MathF.Min(1f, MathF.Max(0f, (hoverAmount - 0.05f) * 5f));
                            }
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(character.AnimatedImagePath))
                    {
                        var animWrap = plugin.AnimatedTextureCache.GetOrLoad(character);
                        if (animWrap != null)
                        {
                            animWrap.IsHovered = isHovered;
                            if (isHovered)
                            {
                                renderHandle = animWrap.Handle;
                                // Use the GIF's aspect + per-character offset/zoom
                                if (animWrap.Width > 0 && animWrap.Height > 0)
                                {
                                    float gifAR = (float)animWrap.Width / animWrap.Height;
                                    float gW, gH;
                                    if (gifAR > boxW / boxH) { gW = boxW; gH = boxW / gifAR; }
                                    else { gH = boxH; gW = boxH * gifAR; }
                                    gW *= character.AnimatedZoom * hoverScaleAmt;
                                    gH *= character.AnimatedZoom * hoverScaleAmt;
                                    var gifOff = new Vector2(boxW * character.AnimatedOffsetX, boxH * character.AnimatedOffsetY);
                                    renderMin = new Vector2(imgCentre.X - gW * 0.5f, imgCentre.Y - gH * 0.5f + liftOffset) + gifOff;
                                    renderMax = new Vector2(imgCentre.X + gW * 0.5f, imgCentre.Y + gH * 0.5f + liftOffset) + gifOff;
                                }
                            }
                        }
                    }

                    dl.PushClipRect(imgMin, imgMax, true);
                    dl.AddImage(renderHandle, renderMin, renderMax);
                    // Backdrop fades on top of the portrait, in sync with the cutout
                    if (hasBackdrop && backdropAlpha > 0f)
                    {
                        uint backdropTint = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, backdropAlpha));
                        dl.AddImage(backdropHandle, dispMin, dispMax, Vector2.Zero, Vector2.One, backdropTint);
                    }
                    // Bottom gradient overlay tinted by npCol (subtle)
                    dl.AddRectFilledMultiColor(
                        new Vector2(imgMin.X, imgMax.Y - boxH * 0.45f),
                        imgMax,
                        Boutique.U32(Boutique.WithAlpha(npCol, 0f)),
                        Boutique.U32(Boutique.WithAlpha(npCol, 0f)),
                        Boutique.U32(Boutique.WithAlpha(npCol, 0.22f)),
                        Boutique.U32(Boutique.WithAlpha(npCol, 0.22f)));
                    dl.PopClipRect();

                    // Queue cutout draw if active + hovered.  Drained after
                    // all cards finish so the cutout paints over neighbours.
                    // Threshold above UpdateHoverAnimation's freeze zone (0.01)
                    // so cutouts actually disappear when not hovered.
                    if (cutoutActive && (isHovered || hoverAmount > 0.05f))
                    {
                        pendingCutouts.Add(new PendingCutout
                        {
                            ImgMin = imgMin,
                            ImgMax = imgMax,
                            SlipMin = slipMin,
                            SlipMax = slipMax,
                            CutoutPath = character.CutoutImagePath!,
                            HoverAmount = hoverAmount,
                            Scale = character.CutoutScale,
                            UiScale = scale,
                            AnchorX = character.CutoutAnchorX,
                            AnchorY = character.CutoutAnchorY,
                            PoseAx = character.CutoutPoseAx,
                            PoseAy = character.CutoutPoseAy,
                        });
                    }

                    // GIF capability badge, small play-triangle + "GIF" pill at the
                    // bottom-right of the portrait when the character has an animated
                    // image equipped.  Brightens on hover.
                    if (!string.IsNullOrWhiteSpace(character.AnimatedImagePath))
                    {
                        float badgeMargin = 5f * scale;
                        float badgeW = 38f * scale;
                        float badgeH = 14f * scale;
                        var badgeMin = new Vector2(imgMax.X - badgeW - badgeMargin, imgMax.Y - badgeH - badgeMargin);
                        var badgeMax = new Vector2(imgMax.X - badgeMargin, imgMax.Y - badgeMargin);

                        var bgCol = Boutique.U32(new Vector4(0.04f, 0.045f, 0.06f, 0.78f));
                        var borderCol = isHovered ? Boutique.Gold : Boutique.GoldDeep;
                        dl.AddRectFilled(badgeMin, badgeMax, bgCol);
                        dl.AddRect(badgeMin, badgeMax, Boutique.U32(borderCol), 0f, ImDrawFlags.None, 1f * scale);

                        // play triangle on the left
                        float triCx = badgeMin.X + 4f * scale;
                        float triCy = (badgeMin.Y + badgeMax.Y) * 0.5f;
                        float triS  = 4f * scale;
                        dl.AddTriangleFilled(
                            new Vector2(triCx, triCy - triS * 0.5f),
                            new Vector2(triCx, triCy + triS * 0.5f),
                            new Vector2(triCx + triS * 0.75f, triCy),
                            Boutique.U32(borderCol));

                        // "GIF" tracked-caps tag
                        var fontSize = ImGui.GetFontSize();
                        var textPos = new Vector2(triCx + triS * 0.75f + 4f * scale,
                                                  triCy - fontSize * 0.5f);
                        dl.AddText(textPos, Boutique.U32(borderCol), "GIF");
                    }
                }
                else
                {
                    var textPos = imgMin + (imgMax - imgMin) * 0.5f - new Vector2(30 * scale, 10 * scale);
                    dl.AddText(textPos, ImGui.GetColorU32(new Vector4(0.7f, 0.7f, 0.7f, 1f)), "No Image");
                }
            }

            // Seasonal overlays on top of image (preserved)
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration))
            {
                var effectiveTheme = SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration);
                if (effectiveTheme == SeasonalTheme.Halloween)
                    DrawCharacterCardSpiderWebs(dl, slipMin, cardWidth, imageHeight, scale, hoverAmount);
                else if (effectiveTheme == SeasonalTheme.Winter || effectiveTheme == SeasonalTheme.Christmas)
                    DrawCharacterCardSnowOverlay(dl, slipMin, cardWidth, imageHeight, scale, hoverAmount);
                else if (effectiveTheme == SeasonalTheme.Valentines)
                    DrawCharacterCardChocolateOverlay(dl, slipMin, cardWidth, imageHeight, scale, hoverAmount);
            }

            // Per-character glitch hover overlay - only when the character opted in.
            if (character.UseGlitchNameEffect)
            {
                DrawCharacterCardGlitchOverlay(dl, slipMin, slipMax, cardWidth, imageHeight, scale, hoverAmount,
                    character.NameplateColor, NameStylizer.Hash(character.Name));
            }

            // Applied flair, Corner Brackets (Study II): four gold L-shapes at the
            // card corners with breathing colour pulse. Replaces the previous fat
            // gold border + inset band combo. Seal pips dropped, brackets carry
            // the entire applied signature now.
            if (isApplied)
            {
                // Mirror DrawPerimeterStreak's showStreak gate so we share its
                // start-time key. Passing unconditional true here would re-seed
                // the start every frame and peg sparkT at 0.
                bool isActiveCardForBrackets = activeChar != null && activeChar == character;
                bool showStreakForBrackets = isHovered || (isActiveCardForBrackets && !anyNonActiveCardHovered);
                const float streakPeriodForBrackets = 4.5f;
                float streakElapsedForBrackets = UIStyles.GetHoverElapsedTime($"charstreak_{index}", showStreakForBrackets);
                float sparkT = streakElapsedForBrackets >= 0f
                    ? (streakElapsedForBrackets / streakPeriodForBrackets) % 1f
                    : -1f;
                Boutique.DrawAppliedCornerBrackets(dl, slipMin, slipMax, scale, time, npCol, sparkT, hoverAmount);
            }

            // APPLIED chip, bottom-LEFT of the portrait, mirroring the GIF
            // badge's bottom-RIGHT.  Bigger font (OswaldSemi11) for legibility
            // since OswaldSemi9 was reading pixel-y at the chip's small size.
            if (isApplied)
            {
                using (Plugin.Instance?.OswaldSemi11?.Push())
                {
                    var chipSize = Boutique.MeasureAppliedChip(1.8f * scale, scale);
                    var chipPos = new Vector2(
                        imgMin.X + 5f * scale,
                        imgMax.Y - chipSize.Y - 6f * scale);
                    Boutique.DrawAppliedChip(dl, chipPos, 1.8f * scale, scale, npCol);
                }
            }
            if (isMainCharacter && plugin.Configuration.ShowMainCharacterCrown)
            {
                // Bookmark ribbon hanging from the top edge near the TR corner.
                // Drawn from primitives, slim gold rectangle with a V-notched
                // tail, top-edge highlight, drop shadow.  No PNG asset.
                float ribbonW   = 11f * scale;
                float ribbonH   = 24f * scale;
                float notchD    = 6f * scale;
                float ribbonCx  = slipMax.X - 16f * scale;       // 16 px in from right edge
                float ribbonTop = slipMin.Y;                     // ribbon top flush with the slip's top frame edge

                var rTL    = new Vector2(ribbonCx - ribbonW * 0.5f, ribbonTop);
                var rTR    = new Vector2(ribbonCx + ribbonW * 0.5f, ribbonTop);
                var rBR    = new Vector2(ribbonCx + ribbonW * 0.5f, ribbonTop + ribbonH);
                var rNotch = new Vector2(ribbonCx,                   ribbonTop + ribbonH - notchD);
                var rBL    = new Vector2(ribbonCx - ribbonW * 0.5f, ribbonTop + ribbonH);

                var shOff = new Vector2(1f * scale, 1.5f * scale);
                uint shCol   = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f));
                uint goldCol = Boutique.U32(Boutique.GoldWarm);
                uint goldDeepCol = Boutique.U32(Boutique.GoldDeep);
                uint highCol = Boutique.U32(Boutique.GoldBright);

                // Wider clip so the top protrusion + shadow render past the slip,
                // but intersect with the parent (scroll) clip so the ribbon also
                // gets cut off when the card scrolls out of view.
                dl.PushClipRect(
                    slipMin - new Vector2(8f * scale, 8f * scale),
                    slipMax + new Vector2(8f * scale, 8f * scale),
                    true);

                // Drop shadow: 3 triangles forming the same V-notched silhouette
                dl.AddTriangleFilled(rNotch + shOff, rBL + shOff, rTL + shOff, shCol);
                dl.AddTriangleFilled(rNotch + shOff, rTL + shOff, rTR + shOff, shCol);
                dl.AddTriangleFilled(rNotch + shOff, rTR + shOff, rBR + shOff, shCol);

                // Body: solid gold-warm fill, ear-clipped from the concave notch
                dl.AddTriangleFilled(rNotch, rBL, rTL, goldCol);
                dl.AddTriangleFilled(rNotch, rTL, rTR, goldCol);
                dl.AddTriangleFilled(rNotch, rTR, rBR, goldCol);

                // Edge tones for depth, lighter top, deeper at the tail tips
                dl.AddLine(rTL, rTR, highCol, 1f * scale);                  // top sheen
                dl.AddLine(rBL, rNotch, goldDeepCol, 1f * scale);           // left tail edge
                dl.AddLine(rNotch, rBR, goldDeepCol, 1f * scale);           // right tail edge

                dl.PopClipRect();
            }

            // ── Boutique nameplate (bottom strip) ──
            var npMin = new Vector2(slipMin.X + 4f * scale, slipMin.Y + imageHeight);
            var npMax = new Vector2(slipMax.X - 4f * scale, slipMax.Y - 4f * scale);

            // Opaque background so any pose-reveal layer tucks behind
            dl.AddRectFilled(npMin, npMax, Boutique.U32(Boutique.Surface0));

            // Top hairline (npCol with side fades)
            DrawBoutiqueHairline(dl, npMin.X + 4f * scale, npMin.Y, npMax.X - 4f * scale, 2.5f * scale, npCol);

            // Top row: favourite | name | RP icon
            float npTop = npMin.Y + 6f * scale;
            // Favourite icon: custom-theme override, then seasonal swap, else default star
            string starGlyph = "\uf005";
            if (plugin.Configuration.SelectedTheme == ThemeSelection.Custom)
            {
                int customIconId = plugin.Configuration.CustomTheme.FavoriteIconId;
                if (customIconId != 0)
                {
                    var customIcon = (FontAwesomeIcon)customIconId;
                    starGlyph = customIcon.ToIconString();
                }
            }
            else if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration))
            {
                switch (SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration))
                {
                    case SeasonalTheme.Halloween:  starGlyph = "\uf6e2"; break; // ghost
                    case SeasonalTheme.Winter:
                    case SeasonalTheme.Christmas:  starGlyph = "\uf2dc"; break; // snowflake
                    case SeasonalTheme.Valentines: starGlyph = "\uf004"; break; // heart
                }
            }
            const string bookGlyph = "\uf02d";
            ImGui.PushFont(UiBuilder.IconFont);
            var starSizeMeasure = ImGui.CalcTextSize(starGlyph);
            var bookSizeMeasure = ImGui.CalcTextSize(bookGlyph);
            ImGui.PopFont();
            // Compact 14px hit area. Star renders at 11px (right size visually).
            // Book glyph is intrinsically narrower so it gets a 13px render size
            // to read at the same visual weight as the star.
            float favSize = 14f * scale;
            float starRenderSize = 11f * scale;
            float bookRenderSize = 13f * scale;
            float starScaleFactor = starRenderSize / UiBuilder.IconFont.FontSize;
            float bookScaleFactor = bookRenderSize / UiBuilder.IconFont.FontSize;
            var starSizeRendered = starSizeMeasure * starScaleFactor;
            var bookSizeRendered = bookSizeMeasure * bookScaleFactor;

            // Favourite star (inset further from card edge per user feedback)
            var favMin = new Vector2(npMin.X + 10f * scale, npTop);
            ImGui.SetCursorScreenPos(favMin);
            bool favClicked = ImGui.InvisibleButton($"##fav_{index}", new Vector2(favSize, favSize));
            bool favHovered = ImGui.IsItemHovered();
            if (favHovered)
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(character.IsFavorite
                    ? $"Remove {character.Name} from favourites"
                    : $"Add {character.Name} to favourites");
            // Favourite-active colour: Custom theme honours the user's
            // custom.favoriteIcon override; seasonal themes pick a tinted
            // colour matching their decoration; otherwise gold.
            var favActiveCol = Boutique.Gold;
            if (plugin.Configuration.SelectedTheme == ThemeSelection.Custom &&
                plugin.Configuration.CustomTheme.ColorOverrides.TryGetValue("custom.favoriteIcon", out var packedFavCol)
                && packedFavCol.HasValue)
            {
                favActiveCol = CustomThemeDefinitions.UnpackColor(packedFavCol.Value);
            }
            else if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration))
            {
                switch (SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration))
                {
                    case SeasonalTheme.Halloween:  favActiveCol = new Vector4(1f, 0.55f, 0.10f, 1f); break; // pumpkin
                    case SeasonalTheme.Winter:     favActiveCol = new Vector4(0.85f, 0.95f, 1.0f, 1f); break; // icy white
                    case SeasonalTheme.Christmas:  favActiveCol = new Vector4(1f, 1f, 1f, 1f); break; // snow white
                    case SeasonalTheme.Valentines: favActiveCol = new Vector4(1f, 0.35f, 0.55f, 1f); break; // pink
                }
            }
            var favCol = (character.IsFavorite || favHovered) ? favActiveCol : Boutique.TextGhost;
            var starPos = new Vector2(
                favMin.X + (favSize - starSizeRendered.X) * 0.5f,
                favMin.Y + (favSize - starSizeRendered.Y) * 0.5f);
            if (character.IsFavorite)
            {
                for (int i = 0; i < 4; i++)
                {
                    var off = new Vector2(MathF.Cos(i * MathF.PI / 2f), MathF.Sin(i * MathF.PI / 2f)) * 1f * scale;
                    dl.AddText(UiBuilder.IconFont, starRenderSize, starPos + off,
                        Boutique.U32(Boutique.WithAlpha(favActiveCol, 0.4f)), starGlyph);
                }
            }
            dl.AddText(UiBuilder.IconFont, starRenderSize, starPos, Boutique.U32(favCol), starGlyph);
            if (favClicked)
            {
                character.IsFavorite = !character.IsFavorite;
                plugin.Configuration.Save();
                InvalidateFilterCache();

                // Fire the spark/firework effect at the star centre.
                int favEffectKey = plugin.Characters.IndexOf(character);
                if (favEffectKey >= 0)
                {
                    if (!characterFavoriteEffects.ContainsKey(favEffectKey))
                        characterFavoriteEffects[favEffectKey] = new FavoriteSparkEffect();
                    var burstPos = new Vector2(starPos.X + starSizeRendered.X * 0.5f,
                                               starPos.Y + starSizeRendered.Y * 0.5f);
                    characterFavoriteEffects[favEffectKey].Trigger(burstPos, character.IsFavorite, plugin.Configuration);
                }
            }

            // RP icon (right edge, inset further per user feedback)
            var rpMin = new Vector2(npMax.X - favSize - 10f * scale, npTop);
            ImGui.SetCursorScreenPos(rpMin);
            bool rpClicked = ImGui.InvisibleButton($"##rp_{index}", new Vector2(favSize, favSize));
            bool rpHovered = ImGui.IsItemHovered();
            if (rpHovered) CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip($"View RolePlay Profile for {character.Name}");
            var rpInk = rpHovered ? Boutique.CyanSoft : Boutique.WithAlpha(Boutique.CyanSoft, 0.75f);
            dl.AddText(UiBuilder.IconFont, bookRenderSize,
                new Vector2(rpMin.X + (favSize - bookSizeRendered.X) * 0.5f,
                            rpMin.Y + (favSize - bookSizeRendered.Y) * 0.5f),
                Boutique.U32(rpInk), bookGlyph);
            if (rpClicked) plugin.OpenRPProfileViewWindow(character);

            // Name (centred between fav and rp), Outfit semi 15.5
            string displayName = !string.IsNullOrWhiteSpace(character.Alias) ? character.Alias! : character.Name;
            using (Plugin.Instance?.OutfitSemi15?.Push())
            {
                var nameSize = ImGui.CalcTextSize(displayName);
                float nameLeft = favMin.X + favSize + 6f * scale;
                float nameRight = rpMin.X - 6f * scale;
                float nameAvailW = nameRight - nameLeft;
                float nameX = nameLeft + (nameAvailW - nameSize.X) * 0.5f;
                if (nameX < nameLeft) nameX = nameLeft;
                var namePos = new Vector2(nameX, npTop + (favSize - nameSize.Y) * 0.5f);
                dl.PushClipRect(new Vector2(nameLeft, npTop - 4f * scale),
                                new Vector2(nameRight, npTop + favSize + 4f * scale), true);
                // Plain text for both applied and non-applied, the active
                // signature comes from the corner brackets, APPLIED chip and
                // accent halo, not from a wave/shimmer on the name itself.
                if (character.UseGlitchNameEffect && plugin.GlitchFont != null && plugin.GlitchFont.Available)
                {
                    // Glitch text is rendered uppercase; measure + position from the same
                    // string we will draw so centering matches the rendered glyph metrics.
                    // SD Glitch's reported line height includes empty space above caps, so
                    // pure (favSize - lineH)/2 centering pulls the visible glyphs upward.
                    // Add a small downward bias so caps sit at the optical centre.
                    string glitchText = NameStylizer.Render(displayName);
                    plugin.GlitchFont.Push();
                    var stylSize = ImGui.CalcTextSize(glitchText);
                    plugin.GlitchFont.Pop();
                    float gNameX = nameLeft + (nameAvailW - stylSize.X) * 0.5f;
                    if (gNameX < nameLeft) gNameX = nameLeft;
                    float yBias = stylSize.Y * 0.12f;   // empirical optical-centre nudge
                    var gNamePos = new Vector2(gNameX, npTop + (favSize - stylSize.Y) * 0.5f + yBias);
                    NameStylizer.Draw(dl, gNamePos, glitchText, character.NameplateColor, 1f,
                        useGlitch: true, glitchFont: plugin.GlitchFont,
                        seedHash: NameStylizer.Hash(character.Name));
                }
                else
                {
                    dl.AddText(namePos, Boutique.U32(Boutique.Text), displayName);
                }
                dl.PopClipRect();
            }

            // ── 3 buttons row: Designs | Edit | Delete ──
            // Narrower (10px inset each side), taller (22px), anchored just below
            // the fav row instead of bottom, sits up higher in the nameplate.
            float btnRowSidePad = 10f * scale;
            float btnGap = 5f * scale;
            float btnH = 22f * scale;
            float btnRowY = npTop + favSize + 6f * scale;
            float btnRowW = (npMax.X - btnRowSidePad) - (npMin.X + btnRowSidePad);
            float btnW = (btnRowW - btnGap * 2) / 3f;
            float btnRowX = npMin.X + btnRowSidePad;

            // Fall back to FontAwesome icons when the tracked-caps label can't fit
            bool useIcons;
            using (Plugin.Instance?.OswaldMed11?.Push())
            {
                float trackPx = 1.6f * scale;
                float designsW = Boutique.MeasureTrackedText("DESIGNS", trackPx);
                useIcons = btnW < designsW + 8f * scale;
            }
            const string designsIcon = ""; // folder-open
            const string editIcon = "";    // pencil/edit
            const string deleteIcon = "";  // trash-alt

            DrawBoutiqueCardButton(dl, scale, new Vector2(btnRowX, btnRowY), new Vector2(btnW, btnH),
                "DESIGNS", $"##cardbtn_d_{index}", Boutique.CyanSoft,
                hovered =>
                {
                    if (hovered) CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Open design panel (Shift+Click: Wardrobe)");
                },
                () =>
                {
                    int realIndex = plugin.Characters.IndexOf(character);
                    if (realIndex >= 0)
                    {
                        if (ImGui.GetIO().KeyShift)
                            HandleShiftClickWardrobe(character, realIndex);
                        else
                            plugin.OpenDesignPanel(realIndex);
                    }
                },
                designsIcon, useIcons);
            DrawBoutiqueCardButton(dl, scale, new Vector2(btnRowX + btnW + btnGap, btnRowY), new Vector2(btnW, btnH),
                "EDIT", $"##cardbtn_e_{index}", Boutique.Text,
                hovered => { if (hovered) CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Edit character details"); },
                () =>
                {
                    int realIndex = plugin.Characters.IndexOf(character);
                    if (realIndex >= 0)
                        plugin.MainWindow?.OpenEditCharacterWindow(realIndex);
                },
                editIcon, useIcons);
            DrawBoutiqueCardButton(dl, scale, new Vector2(btnRowX + (btnW + btnGap) * 2, btnRowY), new Vector2(btnW, btnH),
                "DELETE", $"##cardbtn_x_{index}", Boutique.Red,
                hovered => { if (hovered) CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Delete character - hold Ctrl+Shift and click"); },
                () =>
                {
                    var io = ImGui.GetIO();
                    if (!(io.KeyCtrl && io.KeyShift)) return; // Safety gate, required to delete
                    int realIndex = plugin.Characters.IndexOf(character);
                    if (realIndex >= 0)
                    {
                        var deleteTarget = plugin.Characters[realIndex];
                        // Best-effort server cleanup for any uploaded profiles
                        // tied to this character (matches legacy delete path).
                        var keysToDelete = new List<string>();
                        if (!string.IsNullOrWhiteSpace(deleteTarget.LastInGameName))
                        {
                            string csDisplay = !string.IsNullOrWhiteSpace(deleteTarget.Alias)
                                ? deleteTarget.Alias!
                                : deleteTarget.Name;
                            keysToDelete.Add($"{csDisplay}_{deleteTarget.LastInGameName}");
                        }
                        if (deleteTarget.PreviousProfileKeys is { Count: > 0 })
                            keysToDelete.AddRange(deleteTarget.PreviousProfileKeys);
                        if (keysToDelete.Count > 0 && !string.IsNullOrWhiteSpace(deleteTarget.LastInGameName))
                            _ = Plugin.DeleteProfilesAsync(keysToDelete, deleteTarget.LastInGameName);

                        plugin.AnimatedTextureCache?.Forget(deleteTarget);
                        plugin.Characters.RemoveAt(realIndex);
                        plugin.Configuration.Save();
                        InvalidateCache();
                        InvalidateFilterCache();
                    }
                },
                deleteIcon, useIcons);

            // Perimeter streak/trail effect, gold-coloured trace around the card
            // perimeter on hover, persistent on the active card unless another is
            // hovered (so only one card glows at a time). Reuses the existing helper.
            if (plugin.Configuration.EnableCharacterHoverEffects)
            {
                bool isActiveCard = activeChar != null && activeChar == character;
                bool showStreak = isHovered || (isActiveCard && !anyNonActiveCardHovered);
                float streakElapsed = UIStyles.GetHoverElapsedTime($"charstreak_{index}", showStreak);
                if (streakElapsed >= 0f)
                {
                    var bColor = new Vector3(npCol.X, npCol.Y, npCol.Z);
                    // Streak now traces the slip border itself instead of an
                    // outset rect, the trail rides on the actual card edge.
                    DrawPerimeterStreak(dl, slipMin, slipMax,
                        isActiveCard ? 1f : hoverAmount, scale, bColor, streakElapsed);
                }

                // One-shot glossy sheen sweep across the card on hover-enter
                float cardSheen = uiStyles.UpdateAndGetHoverSweepProgress($"charcard_{index}", isHovered);
                if (cardSheen >= 0f)
                    UIStyles.DrawHoverSheen(dl, slipMin, slipMax, cardSheen, maxAlpha: 0.14f);
            }

            ImGui.EndGroup();
            ImGui.Dummy(new Vector2(0, spacing));

            // Drag-drop scoped to the NAME ROW only (top of nameplate, between
            // the favorite star and rp profile icon, above the button row).
            // Geometry: fav/RP buttons sit at slipMin.X + 4 (npMin offset) +
            // 10 (button inset) + favSize. Drag area must start past the
            // fav button's right edge plus a 4px safety gap, otherwise the
            // drag tooltip stacks on top of the fav/RP tooltips.
            const float favSizePx = 14f;
            float dragNpTop = slipMin.Y + imageHeight;
            var dragAreaMin = new Vector2(
                slipMin.X + favSizePx * scale + 18f * scale,
                dragNpTop + 1f * scale);
            var dragAreaMax = new Vector2(
                slipMax.X - favSizePx * scale - 18f * scale,
                dragNpTop + favSizePx * scale + 4f * scale);

            // HandleCharacterDragAndDrop sets cursor to areaMin and creates an
            // InvisibleButton, which advances ImGui's cursor relative to that
            // jump. Save + restore so the next card in the column starts where
            // the BeginGroup/Dummy above left it, not partway up inside this
            // card (which caused overlap when the drag area shrank).
            var cursorAfterDummy = ImGui.GetCursorScreenPos();
            HandleCharacterDragAndDrop(index, dragAreaMin, dragAreaMax, character, scale);
            ImGui.SetCursorScreenPos(cursorAfterDummy);
        }

        private void DrawBoutiqueHairline(ImDrawListPtr dl, float x1, float y, float x2, float h, Vector4 col)
        {
            float w = x2 - x1;
            float fadeW = MathF.Min(20f, w * 0.18f);
            var clear = Boutique.U32(Boutique.WithAlpha(col, 0f));
            // Bumped solid alpha 0.55 → 0.85 since the bar is now thicker, saves
            // it from looking "weighty but washed out".
            var solid = Boutique.U32(Boutique.WithAlpha(col, 0.85f));
            dl.AddRectFilledMultiColor(
                new Vector2(x1, y), new Vector2(x1 + fadeW, y + h),
                clear, solid, solid, clear);
            dl.AddRectFilled(new Vector2(x1 + fadeW, y), new Vector2(x2 - fadeW, y + h), solid);
            dl.AddRectFilledMultiColor(
                new Vector2(x2 - fadeW, y), new Vector2(x2, y + h),
                solid, clear, clear, solid);
        }

        private void DrawBoutiqueCardButton(ImDrawListPtr dl, float scale, Vector2 min, Vector2 size,
            string label, string id, Vector4 hoverInk, Action<bool>? onHover, Action onClick,
            string? compactIcon = null, bool useIcon = false)
        {
            var max = min + size;
            ImGui.SetCursorScreenPos(min);
            bool clicked = ImGui.InvisibleButton(id, size);
            bool hovered = ImGui.IsItemHovered();
            onHover?.Invoke(hovered);

            // Seasonal themes substitute their own button palette so Designs /
            // Edit / Delete match the active decoration. Hover ink stays as
            // the per-button accent (cyan for designs, etc.).
            Vector4 idleBg = Boutique.Surface3;
            Vector4 idleInk = Boutique.TextDim;
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration))
            {
                switch (SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration))
                {
                    case SeasonalTheme.Halloween:
                        idleBg = new Vector4(0.20f, 0.10f, 0.05f, 0.9f);
                        idleInk = new Vector4(0.95f, 0.87f, 0.70f, 1.0f);
                        break;
                    case SeasonalTheme.Winter:
                        idleBg = new Vector4(0.20f, 0.30f, 0.45f, 0.9f);
                        idleInk = new Vector4(0.95f, 0.98f, 1.0f, 1.0f);
                        break;
                    case SeasonalTheme.Christmas:
                        idleBg = new Vector4(0.45f, 0.10f, 0.07f, 0.9f);
                        idleInk = new Vector4(1.0f, 0.97f, 0.93f, 1.0f);
                        break;
                    case SeasonalTheme.Valentines:
                        idleBg = new Vector4(0.45f, 0.10f, 0.25f, 0.9f);
                        idleInk = new Vector4(1.0f, 0.95f, 0.97f, 1.0f);
                        break;
                }
            }

            var bgCol = hovered
                ? Boutique.U32(Boutique.WithAlpha(hoverInk, 0.12f))
                : Boutique.U32(idleBg);
            dl.AddRectFilled(min, max, bgCol);
            if (hovered)
                dl.AddRect(min, max, Boutique.U32(Boutique.WithAlpha(hoverInk, 0.45f)),
                    0f, ImDrawFlags.None, 1f * scale);

            var ink = hovered ? hoverInk : idleInk;

            if (useIcon && !string.IsNullOrEmpty(compactIcon))
            {
                // Render FontAwesome icon centred when buttons are too narrow for tracked text
                float glyphSize = MathF.Min(size.Y - 6f * scale, 13f * scale);
                ImGui.PushFont(UiBuilder.IconFont);
                var rawSize = ImGui.CalcTextSize(compactIcon);
                ImGui.PopFont();
                float ratio = glyphSize / UiBuilder.IconFont.FontSize;
                var drawn = rawSize * ratio;
                var iconPos = new Vector2(
                    (min.X + max.X) * 0.5f - drawn.X * 0.5f,
                    (min.Y + max.Y) * 0.5f - drawn.Y * 0.5f);
                dl.AddText(UiBuilder.IconFont, glyphSize, iconPos, Boutique.U32(ink), compactIcon);
            }
            else
            {
                using (Plugin.Instance?.OswaldMed11?.Push())
                {
                    float trackPx = 1.6f * scale;
                    float labelW = Boutique.MeasureTrackedText(label, trackPx);
                    float labelH = ImGui.GetFontSize();
                    Boutique.DrawTrackedText(dl,
                        new Vector2((min.X + max.X) * 0.5f - labelW * 0.5f,
                                    (min.Y + max.Y) * 0.5f - labelH * 0.5f),
                        label, Boutique.U32(ink), trackPx);
                }
            }
            if (clicked) onClick?.Invoke();
        }

        // LEGACY METHOD (no longer called; kept for fallback / cleanup later)
        private void DrawCharacterCardLegacy(Character character, int index, float cardWidth, float scale)
        {
            cardWidth = Math.Clamp(cardWidth, 64 * scale, 512 * scale);
            float nameplateHeight = 70 * scale;
            float imageHeight = cardWidth;
            float totalCardHeight = imageHeight + nameplateHeight;
            float spacing = 12f * scale;

            string pluginDirectory = plugin.PluginDirectory;
            string defaultImagePath = Path.Combine(pluginDirectory, "Assets", "Default.png");

            string finalImagePath = GetCachedImagePath(character.ImagePath, defaultImagePath);

            // Check if this character is the main character
            bool isMainCharacter = !string.IsNullOrEmpty(plugin.Configuration.MainCharacterName) &&
                                   character.Name == plugin.Configuration.MainCharacterName;

            ImGui.BeginGroup();

            var cardStartPos = ImGui.GetCursorScreenPos();
            var cardMin = cardStartPos;
            var cardMax = cardStartPos + new Vector2(cardWidth, totalCardHeight);

            ImGui.Dummy(new Vector2(cardWidth, totalCardHeight));
            var cardArea = ImGui.GetItemRectMin();

            ImGui.SetCursorScreenPos(cardArea);
            ImGui.InvisibleButton($"##CharCard{index}", new Vector2(cardWidth, imageHeight));
            bool isHovered = ImGui.IsItemHovered();

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !isDragging)
            {
                HandleCharacterClick(character, index);
            }

            if (ImGui.BeginPopupContextItem($"##ContextMenu_{character.Name}"))
            {
                DrawContextMenu(character, scale);
                ImGui.EndPopup();
            }

            float hoverAmount = UpdateHoverAnimation(index, isHovered);
            
            // Get Halloween wiggle offset (use plugin.Characters.Count for total character count)
            Vector2 wiggleOffset = UpdateHalloweenWiggle(index, plugin.Characters.Count);

            Vector3 borderColor = character.NameplateColor;

            // Check for Custom theme card glow override first
            if (plugin.Configuration.SelectedTheme == ThemeSelection.Custom)
            {
                var customTheme = plugin.Configuration.CustomTheme;
                // Only use custom color if toggle is OFF (UseNameplateColorForCardGlow = false)
                if (!customTheme.UseNameplateColorForCardGlow &&
                    customTheme.ColorOverrides.TryGetValue("custom.cardGlow", out var packedGlowColor) && packedGlowColor.HasValue)
                {
                    var glowColor = CustomThemeDefinitions.UnpackColor(packedGlowColor.Value);
                    borderColor = new Vector3(glowColor.X, glowColor.Y, glowColor.Z);
                }
                // Otherwise, keep the character's nameplate color (already set above)
            }
            // Override border color for seasonal themes with alternating patterns
            else if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration))
            {
                var effectiveTheme = SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration);
                var themeColors = SeasonalThemeManager.GetCurrentThemeColors(plugin.Configuration);
                
                switch (effectiveTheme)
                {
                    case SeasonalTheme.Halloween:
                        // Alternate between orange and purple based on character index
                        borderColor = index % 2 == 0 
                            ? new Vector3(themeColors.PrimaryAccent.X, themeColors.PrimaryAccent.Y, themeColors.PrimaryAccent.Z)    // Orange
                            : new Vector3(themeColors.SecondaryAccent.X, themeColors.SecondaryAccent.Y, themeColors.SecondaryAccent.Z); // Purple
                        break;
                        
                    case SeasonalTheme.Winter:
                        // Alternate between icy blue and pale white based on character index
                        borderColor = index % 2 == 0 
                            ? new Vector3(themeColors.PrimaryAccent.X, themeColors.PrimaryAccent.Y, themeColors.PrimaryAccent.Z)     // Icy blue
                            : new Vector3(themeColors.SecondaryAccent.X, themeColors.SecondaryAccent.Y, themeColors.SecondaryAccent.Z); // Pale white
                        break;
                        
                    case SeasonalTheme.Christmas:
                        // Alternate between red and green based on character index
                        borderColor = index % 2 == 0
                            ? new Vector3(themeColors.PrimaryAccent.X, themeColors.PrimaryAccent.Y, themeColors.PrimaryAccent.Z)     // Red
                            : new Vector3(themeColors.SecondaryAccent.X, themeColors.SecondaryAccent.Y, themeColors.SecondaryAccent.Z); // Green
                        break;

                    case SeasonalTheme.Valentines:
                        // White glow for Valentine's
                        borderColor = new Vector3(1.0f, 1.0f, 1.0f);
                        break;
                }
            }
            
            float borderIntensity = 0.6f + hoverAmount * 0.4f;

            if (draggedCharacterIndex == index)
            {
                borderIntensity = 1.0f;
            }

            // Apply wiggle offset to card positions
            var wiggleCardMin = cardMin + wiggleOffset;
            var wiggleCardMax = cardMax + wiggleOffset;

            var borderMargin = (4f + (hoverAmount * 2f)) * scale;
            uiStyles.DrawGlowingBorder(
                wiggleCardMin - new Vector2(borderMargin, borderMargin),
                wiggleCardMax + new Vector2(borderMargin, borderMargin),
                borderColor,
                borderIntensity,
                isHovered || draggedCharacterIndex == index
            );

            var drawList = ImGui.GetWindowDrawList();
            
            // Draw seasonal decorations BEHIND character cards - before background is drawn
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration))
            {
                var effectiveTheme = SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration);
                if (effectiveTheme == SeasonalTheme.Winter || effectiveTheme == SeasonalTheme.Christmas)
                {
                    // Draw icicles behind the character card
                    DrawCharacterCardIcicles(drawList, wiggleCardMin, cardWidth, imageHeight, scale);
                }
                // Valentine's - no card decorations, just falling hearts background
            }
            
            uint cardBgColor = ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.12f, 0.95f));
            drawList.AddRectFilled(wiggleCardMin, wiggleCardMax, cardBgColor, 12f * scale);

            var imageArea = wiggleCardMin;
            var imageAreaSize = new Vector2(cardWidth, imageHeight);

            if (!string.IsNullOrEmpty(finalImagePath))
            {
                var texture = Plugin.TextureProvider.GetFromFile(finalImagePath).GetWrapOrDefault();

                if (texture != null)
                {
                    float originalWidth = texture.Width;
                    float originalHeight = texture.Height;
                    float aspectRatio = originalWidth / originalHeight;

                    float imageAreaWidth = imageAreaSize.X - (8 * scale);
                    float imageAreaHeight = imageAreaSize.Y - (8 * scale);

                    float displayWidth, displayHeight;
                    if (aspectRatio > 1)
                    {
                        displayWidth = imageAreaWidth;
                        displayHeight = imageAreaWidth / aspectRatio;
                        if (displayHeight > imageAreaHeight)
                        {
                            displayHeight = imageAreaHeight;
                            displayWidth = imageAreaHeight * aspectRatio;
                        }
                    }
                    else
                    {
                        displayHeight = imageAreaHeight;
                        displayWidth = imageAreaHeight * aspectRatio;
                        if (displayWidth > imageAreaWidth)
                        {
                            displayWidth = imageAreaWidth;
                            displayHeight = imageAreaWidth / aspectRatio;
                        }
                    }

                    float hoverScale = plugin.Configuration.EnableCharacterHoverEffects
                        ? 1f + (0.05f * hoverAmount)
                        : 1f;

                    float finalWidth = displayWidth * hoverScale;
                    float finalHeight = displayHeight * hoverScale;

                    float paddingX = (imageAreaSize.X - finalWidth) / 2;
                    float paddingY = (imageAreaSize.Y - finalHeight) / 2;
                    float liftOffset = -2f * hoverAmount * scale; 

                    var imagePos = imageArea + new Vector2(paddingX, paddingY + liftOffset);
                    var imagePosMax = imagePos + new Vector2(finalWidth, finalHeight);

                    // For high-resolution images, use slightly inset UVs to improve sampling quality
                    Vector2 uvMin = new Vector2(0, 0);
                    Vector2 uvMax = new Vector2(1, 1);
                    
                    // Detect very large textures that might look crunchy when downscaled
                    bool isHighRes = originalWidth > 1920 || originalHeight > 1080;
                    if (isHighRes)
                    {
                        // Use slightly inset UV coordinates to avoid edge artifacts and improve sampling
                        float uvInset = 0.001f; // Very small inset to avoid sampling edge pixels
                        uvMin = new Vector2(uvInset, uvInset);
                        uvMax = new Vector2(1.0f - uvInset, 1.0f - uvInset);
                    }

                    drawList.AddImageRounded(
                        (ImTextureID)texture.Handle,
                        imagePos,
                        imagePosMax,
                        uvMin,
                        uvMax,
                        ImGui.GetColorU32(new Vector4(1, 1, 1, 1)),
                        8f * scale,
                        ImDrawFlags.RoundCornersTop
                    );

                    if (isMainCharacter && plugin.Configuration.ShowMainCharacterCrown)
                    {
                        DrawMainCharacterCrown(drawList, imagePosMax, imagePos, hoverAmount, scale);
                    }
                }
            }
            else
            {
                var textPos = imageArea + imageAreaSize / 2 - new Vector2(30 * scale, 10 * scale); // Scale text position
                drawList.AddText(textPos, ImGui.GetColorU32(new Vector4(0.7f, 0.7f, 0.7f, 1f)), "No Image");
            }

            // Draw Halloween spider webs on character cards
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration) && 
                SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration) == SeasonalTheme.Halloween)
            {
                // Add spider webs to all character cards with hover animation
                DrawCharacterCardSpiderWebs(drawList, wiggleCardMin, cardWidth, imageHeight, scale, hoverAmount);
            }
            
            // Winter icicles now drawn behind cards earlier in the draw order
            
            // Draw snow.png overlay in top left corner for Winter/Christmas themes
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration) &&
                (SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration) == SeasonalTheme.Winter ||
                 SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration) == SeasonalTheme.Christmas))
            {
                DrawCharacterCardSnowOverlay(drawList, wiggleCardMin, cardWidth, imageHeight, scale, hoverAmount);
            }

            // Draw chocolate.png overlay in top left corner for Valentine's theme
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration) &&
                SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration) == SeasonalTheme.Valentines)
            {
                DrawCharacterCardChocolateOverlay(drawList, wiggleCardMin, cardWidth, imageHeight, scale, hoverAmount);
            }

            // Per-character glitch hover overlay - only when the character opted in.
            if (character.UseGlitchNameEffect)
            {
                DrawCharacterCardGlitchOverlay(drawList, wiggleCardMin, wiggleCardMax, cardWidth, imageHeight, scale, hoverAmount,
                    character.NameplateColor, NameStylizer.Hash(character.Name));
            }

            DrawIntegratedNameplate(character, wiggleCardMin, cardWidth, imageHeight, nameplateHeight, index, hoverAmount, scale);

            // Perimeter streak effect. Active character gets it permanently,
            // but yields when hovering a different card so only one glows.
            if (plugin.Configuration.EnableCharacterHoverEffects)
            {
                var activeChar = plugin.GetActiveCharacter() ?? plugin.activeCharacter;
                bool isActiveCard = activeChar != null && activeChar == character;
                bool showStreak = isHovered || (isActiveCard && !anyNonActiveCardHovered);

                var glowMin = wiggleCardMin - new Vector2(borderMargin, borderMargin);
                var glowMax = wiggleCardMax + new Vector2(borderMargin, borderMargin);
                float streakElapsed = UIStyles.GetHoverElapsedTime($"charstreak_{index}", showStreak);
                if (streakElapsed >= 0f)
                    DrawPerimeterStreak(drawList, glowMin, glowMax, isActiveCard ? 1f : hoverAmount, scale, borderColor, streakElapsed);

                // One-shot glossy sheen sweep across the card on hover-enter
                float cardSheen = uiStyles.UpdateAndGetHoverSweepProgress($"charcard_{index}", isHovered);
                if (cardSheen >= 0f)
                    UIStyles.DrawHoverSheen(drawList, wiggleCardMin, wiggleCardMax, cardSheen, maxAlpha: 0.14f);
            }

            ImGui.EndGroup();
            ImGui.Dummy(new Vector2(0, spacing));
        }

        private string GetCachedImagePath(string? characterImagePath, string defaultImagePath)
        {
            if (!string.IsNullOrEmpty(characterImagePath))
            {
                bool exists;
                lock (fileExistsCache)
                {
                    if (!fileExistsCache.TryGetValue(characterImagePath, out exists))
                    {
                        // Not in cache yet - check synchronously (should be rare if pre-warm ran)
                        exists = File.Exists(characterImagePath);
                        fileExistsCache[characterImagePath] = exists;
                    }
                }

                if (exists)
                    return characterImagePath;
            }

            bool defaultExists;
            lock (fileExistsCache)
            {
                if (!fileExistsCache.TryGetValue(defaultImagePath, out defaultExists))
                {
                    defaultExists = File.Exists(defaultImagePath);
                    fileExistsCache[defaultImagePath] = defaultExists;
                }
            }

            return defaultExists ? defaultImagePath : "";
        }

        private Vector2 GetCachedTextSize(string text)
        {
            if (!textSizeCache.TryGetValue(text, out Vector2 size))
            {
                size = ImGui.CalcTextSize(text);
                textSizeCache[text] = size;
            }
            return size;
        }

        private void DrawMainCharacterCrown(ImDrawListPtr drawList, Vector2 imagePosMax, Vector2 imagePos, float hoverAmount, float scale)
        {
            float crownBadgeSize = 32f * scale;
            var badgePos = new Vector2(
                imagePosMax.X - crownBadgeSize - (4 * scale),
                imagePos.Y + (4 * scale)
            );
            var badgeCenter = badgePos + new Vector2(crownBadgeSize / 2, crownBadgeSize / 2);

            uint badgeBg = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.7f));
            drawList.PathClear();
            drawList.PathArcTo(badgeCenter, crownBadgeSize / 2 + (2 * scale), 0, MathF.PI * 2);
            drawList.PathFillConvex(badgeBg);

            uint badgeRing = ImGui.GetColorU32(new Vector4(1f, 0.8f, 0.2f, 0.9f + hoverAmount * 0.1f));
            drawList.PathClear();
            drawList.PathArcTo(badgeCenter, crownBadgeSize / 2, 0, MathF.PI * 2);
            drawList.PathStroke(badgeRing, ImDrawFlags.Closed, 3f * scale);

            // Crown PNG fits inside the badge ring with a small inset; preserves
            // the badge background + gold ring drawn above for any-bg legibility.
            var crownTex = Plugin.TextureProvider.GetFromFile(plugin.CrownIconPath).GetWrapOrDefault();
            if (crownTex != null)
            {
                float crownInset = 6f * scale;
                var crownMin = badgePos + new Vector2(crownInset, crownInset);
                var crownMax = badgePos + new Vector2(crownBadgeSize - crownInset, crownBadgeSize - crownInset);
                drawList.AddImage(crownTex.Handle, crownMin, crownMax);
            }
        }

        /// <summary>
        /// Bright streak around a card's perimeter on hover, with fading tail
        /// and trailing particles. `hoverElapsed` is per-card so each starts
        /// fresh at the TL corner instead of mid-loop.
        /// </summary>
        private static void DrawPerimeterStreak(ImDrawListPtr dl, Vector2 mn, Vector2 mx, float hoverAmount, float scale, Vector3 accent, float hoverElapsed)
        {
            if (hoverAmount < 0.2f) return;
            float alpha = hoverAmount;
            float rounding = 6f;

            const float streakPeriod = 4.5f;
            const float streakLengthFraction = 0.30f;
            const int streakSegments = 40;
            float streakHead = (hoverElapsed / streakPeriod) % 1f;
            float stepFraction = streakLengthFraction / streakSegments;

            for (int seg = 0; seg < streakSegments; seg++)
            {
                float pos1 = (streakHead - seg * stepFraction + 1f) % 1f;
                float pos2 = (streakHead - (seg + 1) * stepFraction + 1f) % 1f;
                var p1 = WalkPerimeter(pos1, mn, mx, rounding);
                var p2 = WalkPerimeter(pos2, mn, mx, rounding);

                float t = seg / (float)streakSegments;
                float segAlpha = (1f - t) * (1f - t) * 0.85f * alpha;
                float thickness = (1f - t * 0.45f) * 3f * scale;
                dl.AddLine(p1, p2,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, segAlpha)), thickness);
            }

            // Bright head dot + glow halo
            // Core is a brighter tint of the accent colour (mixed ~50% toward white) so
            // the head reads as a bright leading point while still carrying the card's
            // accent hue. Pure accent would wash out on dark nameplate colours; pure
            // white loses the per-card colour identity.
            var headPt = WalkPerimeter(streakHead, mn, mx, rounding);
            var headCore = new Vector4(
                accent.X + (1f - accent.X) * 0.5f,
                accent.Y + (1f - accent.Y) * 0.5f,
                accent.Z + (1f - accent.Z) * 0.5f,
                0.95f * alpha);
            dl.AddCircleFilled(headPt, 6f * scale,
                ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, 0.22f * alpha)));
            dl.AddCircleFilled(headPt, 3f * scale,
                ImGui.ColorConvertFloat4ToU32(headCore));

            // Trailing particles from the head. Use hoverElapsed as the time source
            // so particle spawn positions align with the locally-started streak rather
            // than the global clock. Skip any particle whose "age" would point before
            // hover started - otherwise on a fresh hover you'd see phantom particles
            // at perimeter positions the streak hasn't actually visited yet.
            var anchor = (mn + mx) * 0.5f;
            float now = hoverElapsed;
            int particleCount = 6;
            float maxDrift = 12f;
            float lifetime = 0.45f;

            for (int i = 0; i < particleCount; i++)
            {
                float phaseOffset = i / (float)particleCount;
                float lifeProgress = ((now / lifetime) + phaseOffset) % 1f;
                float age = lifeProgress * lifetime;

                // Skip particles that would have spawned before hover began
                if (now - age < 0f) continue;

                float spawnFrac = (float)((((now - age) / streakPeriod) % 1.0 + 1.0) % 1.0);
                var spawnPt = WalkPerimeter(spawnFrac, mn, mx, rounding);

                var outward = spawnPt - anchor;
                float outLen = outward.Length();
                if (outLen > 0.01f) outward /= outLen;
                else outward = new Vector2(1, 0);

                float angleVar = (i * 137.5f * MathF.PI / 180f + now * 0.5f) % MathF.Tau;
                var perp = new Vector2(-outward.Y, outward.X);
                var driftDir = outward + perp * MathF.Sin(angleVar) * 0.5f;
                float dLen = driftDir.Length();
                if (dLen > 0.001f) driftDir /= dLen;

                float eased = 1f - (1f - lifeProgress) * (1f - lifeProgress);
                var pos = spawnPt + driftDir * eased * maxDrift * scale;

                float pAlpha = (1f - lifeProgress) * (1f - lifeProgress) * 0.85f * alpha;
                float pR = (2.1f - lifeProgress * 0.7f) * scale;
                uint pCol = ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, pAlpha));

                // Seasonal trail variants:
                //   Halloween → wispy smoke (stacked translucent grey circles)
                //   Winter / Christmas → snowflake glyph
                //   Valentine's → heart glyph
                //   Default → plain circle
                if (IsSmokeyTrail())
                {
                    // 3 stacked soft circles, each larger + lower alpha, gives
                    // a puffy wisp rather than a hard particle.
                    float smokeBaseR = pR * 2.6f;
                    var smokeRgb = new Vector3(0.22f, 0.20f, 0.18f);
                    for (int s = 0; s < 3; s++)
                    {
                        float layerR = smokeBaseR * (1f + s * 0.45f);
                        float layerA = pAlpha * (0.45f - s * 0.13f);
                        if (layerA <= 0f) continue;
                        dl.AddCircleFilled(pos, layerR,
                            ImGui.ColorConvertFloat4ToU32(new Vector4(smokeRgb.X, smokeRgb.Y, smokeRgb.Z, layerA)));
                    }
                    continue;
                }

                string? glyph = SeasonalTrailGlyph(i);
                if (!string.IsNullOrEmpty(glyph))
                {
                    var iconFont = UiBuilder.IconFont;
                    float glyphPx = MathF.Max(10f, pR * 6f);
                    var glyphSz = ImGui.CalcTextSize(glyph);
                    float glyphScale = glyphPx / iconFont.FontSize;
                    var glyphPos = new Vector2(
                        pos.X - glyphSz.X * glyphScale * 0.5f,
                        pos.Y - glyphSz.Y * glyphScale * 0.5f);
                    dl.AddText(iconFont, glyphPx, glyphPos, pCol, glyph);
                }
                else
                {
                    dl.AddCircleFilled(pos, pR, pCol);
                }
            }
        }

        /// <summary>True when the active theme should render the perimeter
        /// trail as wispy smoke instead of a single particle / glyph.</summary>
        private static bool IsSmokeyTrail()
        {
            var p = Plugin.Instance;
            if (p?.Configuration == null) return false;
            if (!SeasonalThemeManager.IsSeasonalThemeEnabled(p.Configuration)) return false;
            return SeasonalThemeManager.GetEffectiveTheme(p.Configuration) == SeasonalTheme.Halloween;
        }

        /// <summary>
        /// Returns the FontAwesome glyph used by the perimeter trail
        /// particles for the active seasonal theme, or null when the
        /// default circle particles should be used.
        /// </summary>
        private static string? SeasonalTrailGlyph(int particleIndex)
        {
            var p = Plugin.Instance;
            if (p?.Configuration == null) return null;
            if (!SeasonalThemeManager.IsSeasonalThemeEnabled(p.Configuration)) return null;
            switch (SeasonalThemeManager.GetEffectiveTheme(p.Configuration))
            {
                case SeasonalTheme.Halloween:  return (particleIndex % 2 == 0) ? "" : ""; // ghost / spider
                case SeasonalTheme.Winter:
                case SeasonalTheme.Christmas:  return ""; // snowflake
                case SeasonalTheme.Valentines: return ""; // heart
                default: return null;
            }
        }

        /// <summary>Maps a 0-1 progress value to a point travelling clockwise around the card's perimeter.</summary>
        private static Vector2 WalkPerimeter(float progress, Vector2 mn, Vector2 mx, float rounding)
        {
            float left = mn.X + rounding, right = mx.X - rounding;
            float top = mn.Y + rounding, bottom = mx.Y - rounding;
            float w = right - left, h = bottom - top;
            float total = 2f * (w + h);
            float d = progress * total;

            if (d < w) return new Vector2(left + d, mn.Y);
            d -= w;
            if (d < h) return new Vector2(mx.X, top + d);
            d -= h;
            if (d < w) return new Vector2(right - d, mx.Y);
            d -= w;
            return new Vector2(mn.X, bottom - d);
        }

        private void DrawIntegratedNameplate(Character character, Vector2 cardMin, float cardWidth, float imageHeight, float nameplateHeight, int characterIndex, float hoverAmount, float scale)
        {
            var drawList = ImGui.GetWindowDrawList();

            var nameplateMin = new Vector2(cardMin.X, cardMin.Y + imageHeight);
            var nameplateMax = new Vector2(cardMin.X + cardWidth, cardMin.Y + imageHeight + nameplateHeight);

            uint nameplateColor = ImGui.GetColorU32(new Vector4(0.08f, 0.08f, 0.08f, 0.95f));
            drawList.AddRectFilled(nameplateMin, nameplateMax, nameplateColor, 12f * scale, ImDrawFlags.RoundCornersBottom);

            var accentMin = new Vector2(nameplateMin.X + (6 * scale), nameplateMin.Y + (2 * scale));
            var accentMax = new Vector2(nameplateMax.X - (6 * scale), nameplateMin.Y + (6 * scale));
            uint accentColor = ImGui.GetColorU32(new Vector4(character.NameplateColor.X, character.NameplateColor.Y, character.NameplateColor.Z, 0.9f + hoverAmount * 0.3f));
            drawList.AddRectFilled(accentMin, accentMax, accentColor, 3f * scale);

            float topRowY = nameplateMin.Y + (12 * scale);

            // Favourite Star/Ghost/Snowflake
            string starSymbol;
            bool usesFontAwesome = false;

            // Check for Custom theme first - it uses user-selected icon
            if (plugin.Configuration.SelectedTheme == ThemeSelection.Custom)
            {
                var customIconId = plugin.Configuration.CustomTheme.FavoriteIconId;
                if (customIconId == 0)
                {
                    // Default star
                    starSymbol = character.IsFavorite ? "★" : "☆";
                    usesFontAwesome = false;
                }
                else
                {
                    // Custom FontAwesome icon
                    var customIcon = (FontAwesomeIcon)customIconId;
                    starSymbol = customIcon.ToIconString();
                    usesFontAwesome = true;
                }
            }
            else if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration))
            {
                var effectiveTheme = SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration);
                if (effectiveTheme == SeasonalTheme.Halloween)
                {
                    starSymbol = "\uf6e2"; // Ghost icon (different colours for favourite/unfavourite)
                    usesFontAwesome = true;
                }
                else if (effectiveTheme == SeasonalTheme.Winter || effectiveTheme == SeasonalTheme.Christmas)
                {
                    starSymbol = "\uf2dc"; // Snowflake icon (different colours for favourite/unfavourite)
                    usesFontAwesome = true;
                }
                else if (effectiveTheme == SeasonalTheme.Valentines)
                {
                    starSymbol = "\uf004"; // Heart icon for Valentine's Day
                    usesFontAwesome = true;
                }
                else
                {
                    starSymbol = character.IsFavorite ? "★" : "☆"; // Default stars
                    usesFontAwesome = false;
                }
            }
            else
            {
                starSymbol = character.IsFavorite ? "★" : "☆"; // Default stars
                usesFontAwesome = false;
            }
            
            // Push FontAwesome font if needed
            if (usesFontAwesome)
            {
                ImGui.PushFont(UiBuilder.IconFont);
            }
            
            var starPos = new Vector2(nameplateMin.X + (8 * scale), topRowY);
            var starSize = GetCachedTextSize(starSymbol);

            // Get star colors based on seasonal theme
            Vector4 starMainColor, starGlowColor;

            // Check for Custom theme first
            if (plugin.Configuration.SelectedTheme == ThemeSelection.Custom)
            {
                // Custom theme reads from custom colour overrides
                var customTheme = plugin.Configuration.CustomTheme;
                Vector4 customFavoriteColor = new Vector4(1f, 0.85f, 0f, 1f); // Default gold

                // Check if user has a custom favourite icon colour
                if (customTheme.ColorOverrides.TryGetValue("custom.favoriteIcon", out var packedFavColor) && packedFavColor.HasValue)
                {
                    customFavoriteColor = CustomThemeDefinitions.UnpackColor(packedFavColor.Value);
                }

                if (character.IsFavorite)
                {
                    starMainColor = customFavoriteColor;
                    starGlowColor = new Vector4(customFavoriteColor.X, customFavoriteColor.Y, customFavoriteColor.Z, 0.5f + hoverAmount * 0.3f);
                }
                else
                {
                    starMainColor = new Vector4(0.5f, 0.5f, 0.5f, 0.7f + hoverAmount * 0.3f); // Gray
                    starGlowColor = starMainColor;
                }
            }
            else if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration))
            {
                var effectiveTheme = SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration);
                if (effectiveTheme == SeasonalTheme.Halloween)
                {
                    var themeColors = SeasonalThemeManager.GetCurrentThemeColors(plugin.Configuration);
                    if (character.IsFavorite)
                    {
                        starMainColor = themeColors.PrimaryAccent; // Orange
                        starGlowColor = new Vector4(themeColors.GlowColor.X, themeColors.GlowColor.Y, themeColors.GlowColor.Z, 0.5f + hoverAmount * 0.3f);
                    }
                    else
                    {
                        starMainColor = new Vector4(1.0f, 1.0f, 1.0f, 0.7f + hoverAmount * 0.3f); // White
                        starGlowColor = starMainColor;
                    }
                }
                else if (effectiveTheme == SeasonalTheme.Winter || effectiveTheme == SeasonalTheme.Christmas)
                {
                    if (character.IsFavorite)
                    {
                        starMainColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f); // Pure white for favourited snowflake
                        starGlowColor = new Vector4(0.8f, 0.9f, 1.0f, 0.6f + hoverAmount * 0.4f); // Icy blue glow
                    }
                    else
                    {
                        starMainColor = new Vector4(0.7f, 0.7f, 0.8f, 0.6f + hoverAmount * 0.3f); // Light grey for unfavourited
                        starGlowColor = starMainColor;
                    }
                }
                else if (effectiveTheme == SeasonalTheme.Valentines)
                {
                    if (character.IsFavorite)
                    {
                        starMainColor = new Vector4(1.0f, 0.0f, 0.5f, 1.0f); // Vivid magenta-pink for favourited heart
                        starGlowColor = new Vector4(1.0f, 0.1f, 0.45f, 0.7f + hoverAmount * 0.3f); // Vibrant pink glow
                    }
                    else
                    {
                        starMainColor = new Vector4(0.85f, 0.4f, 0.55f, 0.65f + hoverAmount * 0.35f); // Brighter muted pink for unfavourited
                        starGlowColor = starMainColor;
                    }
                }
                else
                {
                    // Default colours for other seasonal themes
                    if (character.IsFavorite)
                    {
                        starMainColor = new Vector4(1f, 0.9f, 0.2f, 1f); // Gold
                        starGlowColor = new Vector4(1f, 0.8f, 0f, 0.5f + hoverAmount * 0.3f);
                    }
                    else
                    {
                        starMainColor = new Vector4(0.5f, 0.5f, 0.5f, 0.7f + hoverAmount * 0.3f); // Gray
                        starGlowColor = starMainColor;
                    }
                }
            }
            else
            {
                // Default colours
                if (character.IsFavorite)
                {
                    starMainColor = new Vector4(1f, 0.9f, 0.2f, 1f); // Gold
                    starGlowColor = new Vector4(1f, 0.8f, 0f, 0.5f + hoverAmount * 0.3f);
                }
                else
                {
                    starMainColor = new Vector4(0.5f, 0.5f, 0.5f, 0.7f + hoverAmount * 0.3f); // Grey
                    starGlowColor = starMainColor;
                }
            }

            if (character.IsFavorite)
            {
                uint starGlow = ImGui.GetColorU32(starGlowColor);
                drawList.AddText(starPos + new Vector2(1 * scale, 1 * scale), starGlow, starSymbol);
            }

            uint starColor = ImGui.GetColorU32(starMainColor);
            drawList.AddText(starPos, starColor, starSymbol);
            
            // Pop FontAwesome font if it was used
            if (usesFontAwesome)
            {
                ImGui.PopFont();
            }

            var starHitMin = starPos - new Vector2(2 * scale, 2 * scale);
            var starHitMax = starPos + starSize + new Vector2(2 * scale, 2 * scale);
            if (ImGui.IsMouseHoveringRect(starHitMin, starHitMax))
            {
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip($"{(character.IsFavorite ? "Remove" : "Add")} {character.Name} as a Favourite");

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    var actualCharacter = plugin.Characters[characterIndex];
                    actualCharacter.IsFavorite = !actualCharacter.IsFavorite;
                    if (actualCharacter.IsFavorite) plugin.AchievementTracker?.OnFavouriteSet();

                    Vector2 effectPos = starPos + starSize / 2;
                    if (!characterFavoriteEffects.ContainsKey(characterIndex))
                        characterFavoriteEffects[characterIndex] = new FavoriteSparkEffect();
                    characterFavoriteEffects[characterIndex].Trigger(effectPos, actualCharacter.IsFavorite, plugin.Configuration);

                    plugin.SaveConfiguration();
                    SortCharacters();
                }
            }

            // Character Name - with truncation for narrow cards
            float availableNameWidth = cardWidth - (70 * scale); // Space between star and RP icon
            string displayName = LayoutHelper.ClampText(character.Name, availableNameWidth, "...");
            bool isNameTruncated = displayName != character.Name;

            var textSize = GetCachedTextSize(displayName);
            var nameAreaMin = new Vector2(nameplateMin.X + (35 * scale), topRowY - (4 * scale));
            var nameAreaMax = new Vector2(nameplateMax.X - (35 * scale), topRowY + textSize.Y + (4 * scale));
            var textPos = new Vector2(
                nameplateMin.X + (cardWidth - textSize.X) / 2,
                topRowY
            );

            bool canDrag = CurrentSort == Plugin.SortType.Manual;

            if (canDrag)
            {
                HandleCharacterDragAndDrop(characterIndex, nameAreaMin, nameAreaMax, character, scale);
            }

            if (draggedCharacterIndex == characterIndex)
            {
                var highlightColor = ImGui.GetColorU32(new Vector4(character.NameplateColor.X, character.NameplateColor.Y, character.NameplateColor.Z, 0.4f));
                drawList.AddRectFilled(nameAreaMin, nameAreaMax, highlightColor, 4f * scale);
            }

            bool hoveringNameArea = ImGui.IsMouseHoveringRect(nameAreaMin, nameAreaMax);
            if (hoveringNameArea)
            {
                if (isNameTruncated)
                {
                    // Show full name tooltip when truncated
                    CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(character.Name);
                }
                else if (canDrag)
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Drag to reorder characters (manual sort mode only)");
                }
            }

            if (character.UseGlitchNameEffect && plugin.GlitchFont != null && plugin.GlitchFont.Available)
            {
                NameStylizer.Draw(drawList, textPos, NameStylizer.Render(displayName), character.NameplateColor, 1f,
                    useGlitch: true, glitchFont: plugin.GlitchFont, seedHash: NameStylizer.Hash(character.Name));
            }
            else
            {
                drawList.AddText(textPos + new Vector2(1 * scale, 1 * scale), ImGui.GetColorU32(new Vector4(0, 0, 0, 0.8f)), displayName);
                drawList.AddText(textPos, ImGui.GetColorU32(new Vector4(0.95f, 0.95f, 0.95f, 1f)), displayName);
            }

            // RP Profile Button
            ImGui.PushFont(UiBuilder.IconFont);
            string icon = "\uf2c2";
            var iconSize = GetCachedTextSize(icon);
            var iconPos = new Vector2(nameplateMax.X - iconSize.X - (8 * scale), topRowY);

            if (hoverAmount > 0.1f)
            {
                uint iconGlow = ImGui.GetColorU32(new Vector4(0.3f, 0.7f, 1f, 0.4f + hoverAmount * 0.4f));
                drawList.AddText(iconPos + new Vector2(1 * scale, 1 * scale), iconGlow, icon);
            }

            uint iconColor = ImGui.GetColorU32(new Vector4(0.7f, 0.8f, 1f, 0.8f + hoverAmount * 0.2f));
            drawList.AddText(iconPos, iconColor, icon);
            ImGui.PopFont();

            // Draw NEW badge on RP Profile icon if user hasn't seen Expanded RP Profiles feature (only on first character)
            bool showRPBadge = characterIndex == 0 && !plugin.Configuration.SeenFeatures.Contains(FeatureKeys.ExpandedRPProfile);

            var iconHitMin = iconPos - new Vector2(2 * scale, 2 * scale);
            var iconHitMax = iconPos + iconSize + new Vector2(2 * scale, 2 * scale);

            if (showRPBadge)
            {
                // Pulsing glow effect around the icon
                float pulse = (float)(Math.Sin(ImGui.GetTime() * 3.0) * 0.5 + 0.5);
                var glowColor = new Vector4(0.2f, 1.0f, 0.4f, 0.3f + pulse * 0.5f); // Green glow

                for (int i = 3; i >= 1; i--)
                {
                    var layerPadding = i * 2 * scale;
                    var layerAlpha = glowColor.W * (1.0f - (i * 0.25f));
                    drawList.AddRect(
                        iconHitMin - new Vector2(layerPadding, layerPadding),
                        iconHitMax + new Vector2(layerPadding, layerPadding),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(glowColor.X, glowColor.Y, glowColor.Z, layerAlpha)),
                        4f * scale,
                        ImDrawFlags.None,
                        2f * scale
                    );
                }
            }

            if (ImGui.IsMouseHoveringRect(iconHitMin, iconHitMax))
            {
                string tooltip = $"View RolePlay Profile for {character.Name}";
                if (showRPBadge)
                {
                    tooltip += "\n\nNEW: Expanded RP Profiles with content boxes!";
                }
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(tooltip);

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    plugin.OpenRPProfileViewWindow(character);
                }
            }

            if (characterIndex == 0)
            {
                plugin.RPProfileButtonPos = iconHitMin;
                plugin.RPProfileButtonSize = iconHitMax - iconHitMin;
            }

            // Buttons!!
            float bottomRowY = nameplateMin.Y + (35 * scale);
            float btnWidth = (cardWidth - (32 * scale)) / 3;
            float btnHeight = 22 * scale;
            float btnSpacing = 8 * scale;

            // Responsive button labels - switch to icons when buttons are too narrow
            float buttonPadding = 12 * scale;
            float designsTextWidth = ImGui.CalcTextSize("Designs").X + buttonPadding;
            bool useIcons = btnWidth < designsTextWidth;

            // FontAwesome icons for compact mode
            string designsIcon = "\uf07c";  // folder-open
            string editIcon = "\uf044";     // edit/pencil
            string deleteIcon = "\uf2ed";   // trash-alt

            ImGui.SetCursorScreenPos(new Vector2(nameplateMin.X + (8 * scale), bottomRowY));

            // Button styling - Custom theme uses main window colours, seasonal themes have specific colours
            bool isCustomTheme = plugin.Configuration.SelectedTheme == ThemeSelection.Custom;
            int buttonColorCount = 0;

            if (!isCustomTheme)
            {
                // Seasonal themed button styling or default
                if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration))
                {
                    var effectiveTheme = SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration);

                    switch (effectiveTheme)
                    {
                        case SeasonalTheme.Halloween:
                            // Halloween button styling - dark orange theme
                            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.10f, 0.05f, 0.9f));
                            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.15f, 0.08f, 0.9f));
                            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.40f, 0.20f, 0.10f, 0.9f));
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.87f, 0.70f, 1.0f)); // Warm white text
                            buttonColorCount = 4;
                            break;

                        case SeasonalTheme.Winter:
                            // Winter button styling - bright blue theme
                            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.30f, 0.45f, 0.9f));
                            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.40f, 0.60f, 0.9f));
                            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.40f, 0.55f, 0.75f, 0.9f));
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.98f, 1.0f, 1.0f)); // Bright white text
                            buttonColorCount = 4;
                            break;

                        case SeasonalTheme.Christmas:
                            // Christmas button styling - vibrant saturated red theme
                            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.65f, 0.15f, 0.10f, 0.9f));
                            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.80f, 0.22f, 0.15f, 0.9f));
                            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.95f, 0.28f, 0.20f, 0.9f));
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.98f, 0.95f, 1.0f)); // Bright warm white text
                            buttonColorCount = 4;
                            break;

                        case SeasonalTheme.Valentines:
                            // Valentine's button styling - pink/rose theme
                            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.12f, 0.30f, 0.9f));
                            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.70f, 0.18f, 0.40f, 0.9f));
                            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.85f, 0.25f, 0.50f, 0.9f));
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.95f, 0.97f, 1.0f)); // Soft pink-white text
                            buttonColorCount = 4;
                            break;

                        default:
                            // Default button styling
                            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));
                            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.25f, 0.25f, 1.0f));
                            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.35f, 0.35f, 0.35f, 1.0f));
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
                            buttonColorCount = 4;
                            break;
                    }
                }
                else
                {
                    // Default button styling
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.25f, 0.25f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.35f, 0.35f, 0.35f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
                    buttonColorCount = 4;
                }
            }
            // Custom theme: don't push any button colours - use the main window style colours
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4 * scale, 2 * scale)); // Symmetric padding for centered text
            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));

            var buttonPos = ImGui.GetCursorScreenPos();
            var buttonSize = new Vector2(btnWidth, btnHeight);

            // Scale down icons to be smaller
            float iconScale = 0.85f;

            // Designs button
            if (useIcons)
            {
                ImGui.SetWindowFontScale(iconScale);
                ImGui.PushFont(UiBuilder.IconFont);
            }
            if (ImGui.Button(useIcons ? $"{designsIcon}##{character.Name}" : $"Designs##{character.Name}", new Vector2(btnWidth, btnHeight)))
            {
                if (ImGui.GetIO().KeyShift)
                {
                    // Shift+Click opens the Wardrobe
                    int realIndex = plugin.Characters.IndexOf(character);
                    if (realIndex >= 0)
                        HandleShiftClickWardrobe(character, realIndex);
                }
                else
                {
                    int realIndex = plugin.Characters.IndexOf(character);
                    if (realIndex >= 0)
                        plugin.OpenDesignPanel(realIndex);
                }
            }
            if (useIcons)
            {
                ImGui.PopFont();
                ImGui.SetWindowFontScale(1.0f);
            }
            uiStyles.ApplyHoverSheenToLastItem($"charbtn_designs_{character.Name}");
            if (ImGui.IsItemHovered())
            {
                if (useIcons)
                    CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(ImGui.GetIO().KeyShift ? "Open Wardrobe" : "Designs (Shift+Click: Wardrobe)");
                else
                    CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Shift+Click: Open Wardrobe");
            }

            // Store for tutorial
            if (plugin.Characters.IndexOf(character) == 0)
            {
                plugin.FirstCharacterDesignsButtonPos = buttonPos;
                plugin.FirstCharacterDesignsButtonSize = buttonSize;
            }

            ImGui.SameLine(0, btnSpacing);

            // Declare once for both Edit and Delete buttons
            bool isCtrlShiftPressed = ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift;

            // Edit button
            if (useIcons)
            {
                ImGui.SetWindowFontScale(iconScale);
                ImGui.PushFont(UiBuilder.IconFont);
            }
            if (ImGui.Button(useIcons ? $"{editIcon}##{character.Name}" : $"Edit##{character.Name}", new Vector2(btnWidth, btnHeight)))
            {
                int realIndex = plugin.Characters.IndexOf(character);
                if (realIndex >= 0)
                {
                    if (isCtrlShiftPressed && plugin.Configuration.EnableConflictResolution)
                    {
                        // Enable secret mode for this character conversion
                        plugin.IsSecretMode = true;

                        // Ensure the character has secret mode data structure initialized
                        var targetChar = plugin.Characters[realIndex];
                        if (targetChar.SecretModState == null)
                        {
                            targetChar.SecretModState = new Dictionary<string, bool>();
                        }

                        Plugin.ChatGui.Print("[Character Select+] Character conversion to Secret Mode enabled. Configure mods in the Edit window.");
                    }
                    // Always open edit window (either with converted or original macro)
                    plugin.OpenEditCharacterWindow(realIndex);
                }
            }
            if (useIcons)
            {
                ImGui.PopFont();
                ImGui.SetWindowFontScale(1.0f);
            }
            uiStyles.ApplyHoverSheenToLastItem($"charbtn_edit_{character.Name}");
            if (useIcons && ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("Edit");
                ImGui.EndTooltip();
            }

            ImGui.SameLine(0, btnSpacing);

            // Delete button
            if (useIcons)
            {
                ImGui.SetWindowFontScale(iconScale);
                ImGui.PushFont(UiBuilder.IconFont);
            }
            if (ImGui.Button(useIcons ? $"{deleteIcon}##{character.Name}" : $"Delete##{character.Name}", new Vector2(btnWidth, btnHeight)))
            {
                if (isCtrlShiftPressed)
                {
                    // Collect every server fileKey this character has ever been known by so the
                    // server can clean up the JSON, image, likes, and cache entries. Best-effort:
                    // failures don't block local deletion.
                    var keysToDelete = new List<string>();
                    if (!string.IsNullOrWhiteSpace(character.LastInGameName))
                    {
                        string csDisplay = !string.IsNullOrWhiteSpace(character.Alias) ? character.Alias : character.Name;
                        keysToDelete.Add($"{csDisplay}_{character.LastInGameName}");
                    }
                    if (character.PreviousProfileKeys is { Count: > 0 })
                        keysToDelete.AddRange(character.PreviousProfileKeys);

                    if (keysToDelete.Count > 0 && !string.IsNullOrWhiteSpace(character.LastInGameName))
                    {
                        _ = Plugin.DeleteProfilesAsync(keysToDelete, character.LastInGameName);
                    }

                    plugin.AnimatedTextureCache?.Forget(character);
                    plugin.Characters.Remove(character);
                    plugin.Configuration.Save();
                    InvalidateCache();
                }
            }
            if (useIcons)
            {
                ImGui.PopFont();
                ImGui.SetWindowFontScale(1.0f);
            }
            uiStyles.ApplyHoverSheenToLastItem($"charbtn_delete_{character.Name}");

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                if (useIcons)
                    ImGui.Text("Delete - Hold Ctrl + Shift and click");
                else
                    ImGui.Text("Hold Ctrl + Shift and click to delete.");
                ImGui.EndTooltip();
            }

            ImGui.PopStyleVar(3);
            if (buttonColorCount > 0)
            {
                ImGui.PopStyleColor(buttonColorCount);
            }
        }


        private void HandleCharacterDragAndDrop(int characterIndex, Vector2 areaMin, Vector2 areaMax, Character character, float scale)
        {
            bool hoveringArea = ImGui.IsMouseHoveringRect(areaMin, areaMax);
            bool canDrag = CurrentSort == Plugin.SortType.Manual;

            if (canDrag)
            {
                // Create invisible button
                ImGui.SetCursorScreenPos(areaMin);
                ImGui.InvisibleButton($"##drag_handle_{characterIndex}", areaMax - areaMin);

                if (ImGui.IsItemActive() && draggedCharacterIndex == null)
                {
                    dragStartPos = ImGui.GetMousePos();
                    draggedCharacterIndex = characterIndex;
                    draggedCharacter = character;
                    isDragging = false;
                }

                if (draggedCharacterIndex == characterIndex && ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    Vector2 currentPos = ImGui.GetMousePos();
                    float distance = Vector2.Distance(dragStartPos, currentPos);

                    if (distance > DragThreshold * scale)
                    {
                        isDragging = true;
                    }
                }

                // During dragging, find which card the mouse is over
                if (isDragging && draggedCharacterIndex != null)
                {
                    Vector2 mousePos = ImGui.GetMousePos();
                    if (hoveringArea && characterIndex != draggedCharacterIndex)
                    {
                        currentDropTargetIndex = characterIndex;

                        var drawList = ImGui.GetWindowDrawList();
                        uint dropZoneColor = ImGui.GetColorU32(new Vector4(character.NameplateColor.X, character.NameplateColor.Y, character.NameplateColor.Z, 0.8f));
                        drawList.AddRect(areaMin - new Vector2(2 * scale, 2 * scale), areaMax + new Vector2(2 * scale, 2 * scale), dropZoneColor, 8f * scale, ImDrawFlags.None, 3f * scale);
                    }
                    else if (currentDropTargetIndex == characterIndex)
                    {
                        currentDropTargetIndex = null;
                    }
                }

                // End dragging
                if (draggedCharacterIndex == characterIndex && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    if (isDragging && currentDropTargetIndex.HasValue)
                    {
                        ReorderCharacters(draggedCharacterIndex.Value, currentDropTargetIndex.Value);
                        InvalidateCache();
                    }
                    draggedCharacterIndex = null;
                    draggedCharacter = null;
                    isDragging = false;
                    currentDropTargetIndex = null;
                }

                // Set cursor + tooltip when hovering the name area only.
                // IsItemHovered() should already be scoped to the drag_handle
                // InvisibleButton, but the user reported the tooltip leaking
                // over the image, using IsMouseHoveringRect on the explicit
                // areaMin/areaMax rect is bulletproof against any quirk in
                // last-item resolution between the surrounding card items.
                if (ImGui.IsMouseHoveringRect(areaMin, areaMax))
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Drag to reorder characters (manual sort mode only)");
                }
            }
        }
        private void DrawDragGhostImage(float scale)
        {
            if (!isDragging || draggedCharacter == null)
                return;

            Vector2 mousePos = ImGui.GetMousePos();

            Vector2 scaledGhostSize = ghostImageSize * scale;

            Vector2 ghostOffset = new Vector2(-scaledGhostSize.X / 2, -scaledGhostSize.Y / 2 - (20 * scale));
            Vector2 ghostPos = mousePos + ghostOffset;

            var drawList = ImGui.GetWindowDrawList();

            // Draw a semi-transparent background for the ghost, and maybe it won't haunt us
            uint ghostBgColor = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, ghostImageAlpha * 0.8f));
            drawList.AddRectFilled(
                ghostPos,
                ghostPos + scaledGhostSize,
                ghostBgColor,
                8f * scale 
            );

            // Glowing border using the character's nameplate colour
            uint borderColor = ImGui.GetColorU32(new Vector4(
                draggedCharacter.NameplateColor.X,
                draggedCharacter.NameplateColor.Y,
                draggedCharacter.NameplateColor.Z,
                ghostImageAlpha
            ));
            drawList.AddRect(
                ghostPos - new Vector2(2 * scale, 2 * scale),
                ghostPos + scaledGhostSize + new Vector2(2 * scale, 2 * scale),
                borderColor,
                8f * scale,
                ImDrawFlags.None,
                2f * scale
            );

            // Draw the character's image
            string pluginDirectory = plugin.PluginDirectory;
            string defaultImagePath = Path.Combine(pluginDirectory, "Assets", "Default.png");
            string finalImagePath = GetCachedImagePath(draggedCharacter.ImagePath, defaultImagePath);

            if (!string.IsNullOrEmpty(finalImagePath))
            {
                var texture = Plugin.TextureProvider.GetFromFile(finalImagePath).GetWrapOrDefault();

                if (texture != null)
                {
                    float imageMargin = 8f * scale;
                    Vector2 availableSize = scaledGhostSize - new Vector2(imageMargin * 2, imageMargin + (25 * scale));

                    float originalWidth = texture.Width;
                    float originalHeight = texture.Height;
                    float aspectRatio = originalWidth / originalHeight;

                    Vector2 imageSize;
                    if (aspectRatio > 1) // Landscape
                    {
                        imageSize.X = availableSize.X;
                        imageSize.Y = availableSize.X / aspectRatio;
                        if (imageSize.Y > availableSize.Y)
                        {
                            imageSize.Y = availableSize.Y;
                            imageSize.X = availableSize.Y * aspectRatio;
                        }
                    }
                    else // Portrait or square
                    {
                        imageSize.Y = availableSize.Y;
                        imageSize.X = availableSize.Y * aspectRatio;
                        if (imageSize.X > availableSize.X)
                        {
                            imageSize.X = availableSize.X;
                            imageSize.Y = availableSize.X / aspectRatio;
                        }
                    }

                    // Center the image
                    Vector2 imagePos = ghostPos + new Vector2(
                        (scaledGhostSize.X - imageSize.X) / 2,
                        imageMargin
                    );

                    // Draw image with transparency
                    uint imageColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, ghostImageAlpha));
                    
                    // For high-resolution images, use slightly inset UVs to improve sampling quality
                    Vector2 uvMin = new Vector2(0, 0);
                    Vector2 uvMax = new Vector2(1, 1);
                    
                    // Detect very large textures that might look crunchy when downscaled
                    bool isHighRes = originalWidth > 1920 || originalHeight > 1080;
                    if (isHighRes)
                    {
                        // Use slightly inset UV coordinates to avoid edge artifacts and improve sampling
                        float uvInset = 0.001f; // Very small inset to avoid sampling edge pixels
                        uvMin = new Vector2(uvInset, uvInset);
                        uvMax = new Vector2(1.0f - uvInset, 1.0f - uvInset);
                    }
                    
                    drawList.AddImageRounded(
                        (ImTextureID)texture.Handle,
                        imagePos,
                        imagePos + imageSize,
                        uvMin,
                        uvMax,
                        imageColor,
                        6f * scale,
                        ImDrawFlags.RoundCornersTop
                    );
                }
            }

            // Character name
            var nameSize = GetCachedTextSize(draggedCharacter.Name);
            Vector2 namePos = new Vector2(
                ghostPos.X + (scaledGhostSize.X - nameSize.X) / 2,
                ghostPos.Y + scaledGhostSize.Y - (20 * scale) 
            );

            // Text shadow
            uint shadowColor = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, ghostImageAlpha * 0.8f));
            drawList.AddText(namePos + new Vector2(1 * scale, 1 * scale), shadowColor, draggedCharacter.Name);

            // Main text
            uint textColor = ImGui.GetColorU32(new Vector4(0.95f, 0.95f, 0.95f, ghostImageAlpha));
            drawList.AddText(namePos, textColor, draggedCharacter.Name);
        }

        private void DrawContextMenu(Character character, float scale)
        {
            if (ImGui.Selectable("Apply to Target"))
            {
                // Get target on main thread, then apply in background
                var target = plugin.GetCurrentTarget();
                if (target == null)
                {
                    Plugin.ChatGui.PrintError("[Character Select+] No target selected.");
                }
                else
                {
                    var targetInfo = new { ObjectIndex = target.ObjectIndex, ObjectKind = target.ObjectKind, Name = target.Name?.ToString() ?? "Unknown" };
                    
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            await plugin.ApplyToTarget(character, -1);
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.Error($"Error applying character to target: {ex}");
                        }
                    });
                }
            }

            bool isMainCharacter = !string.IsNullOrEmpty(plugin.Configuration.MainCharacterName) &&
                                   character.Name == plugin.Configuration.MainCharacterName;

            ImGui.Separator();
            if (isMainCharacter)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.8f, 0.2f, 1f));

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf02e");
                ImGui.PopFont();

                ImGui.SameLine(0, 4 * scale);
                if (ImGui.Selectable("Remove as Main Character"))
                {
                    plugin.Configuration.MainCharacterName = null;
                    plugin.Configuration.Save();
                    InvalidateCache();
                }

                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.8f, 0.2f, 1f));

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf02e");
                ImGui.PopFont();

                ImGui.SameLine(0, 4 * scale);
                if (ImGui.Selectable("Set as Main Character"))
                {
                    plugin.Configuration.MainCharacterName = character.Name;
                    plugin.Configuration.Save();
                    InvalidateCache();
                }

                ImGui.PopStyleColor();
            }

            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(character.NameplateColor, 1.0f));
            ImGui.BeginChild($"##Separator_{character.Name}", new Vector2(ImGui.GetContentRegionAvail().X, 3 * scale), false);
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.Spacing();

            if (character.Designs.Count > 0)
            {
                float itemHeight = ImGui.GetTextLineHeightWithSpacing();
                float maxVisible = 10;
                float scrollHeight = Math.Min(character.Designs.Count, maxVisible) * itemHeight + (8 * scale);

                if (ImGui.BeginChild($"##DesignScroll_{character.Name}", new Vector2(300 * scale, scrollHeight)))
                {
                    foreach (var design in character.Designs)
                    {
                        if (ImGui.Selectable($"Apply Design: {design.Name}"))
                        {
                            // Get target on main thread, then apply design in background
                            var target = plugin.GetCurrentTarget();
                            if (target == null)
                            {
                                Plugin.ChatGui.PrintError("[Character Select+] No target selected.");
                            }
                            else
                            {
                                var designIndex = character.Designs.IndexOf(design);
                                var targetInfo = new { ObjectIndex = target.ObjectIndex, ObjectKind = target.ObjectKind, Name = target.Name?.ToString() ?? "Unknown" };
                                
                                _ = System.Threading.Tasks.Task.Run(async () =>
                                {
                                    try
                                    {
                                        await plugin.ApplyToTarget(character, designIndex);
                                    }
                                    catch (Exception ex)
                                    {
                                        Plugin.Log.Error($"Error applying design to target: {ex}");
                                    }
                                });
                            }
                        }
                    }
                    ImGui.EndChild();
                }
            }
        }
        // Pagination button with pixel-perfect icon/text centering. Uses InvisibleButton + manual
        // drawList rendering so the label is centered via (buttonSize - textSize) * 0.5, matching
        // the approach in UIStyles.IconButton. Callers push their own style colours before calling.
        // Uses ImGui.GetColorU32() so PushStyleVar(Alpha) for disabled state works correctly.
        private bool DrawCenteredButton(string label, string id, Vector2 size, bool isIcon)
        {
            var drawList = ImGui.GetWindowDrawList();
            var buttonPos = ImGui.GetCursorScreenPos();

            bool result = ImGui.InvisibleButton(id, size);
            bool isHovered = ImGui.IsItemHovered();
            bool isActive = ImGui.IsItemActive();

            // Background - reads from current ImGui style so pushed colours are respected
            Vector4 bgColor;
            if (isActive) bgColor = ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive];
            else if (isHovered) bgColor = ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonHovered];
            else bgColor = ImGui.GetStyle().Colors[(int)ImGuiCol.Button];

            drawList.AddRectFilled(buttonPos, buttonPos + size,
                ImGui.GetColorU32(bgColor), ImGui.GetStyle().FrameRounding);

            // Measure label with the correct font
            if (isIcon) ImGui.PushFont(UiBuilder.IconFont);
            var textSize = ImGui.CalcTextSize(label);
            if (isIcon) ImGui.PopFont();

            // Centre the label within the button bounds
            var textPos = buttonPos + (size - textSize) * 0.5f;
            var textColor = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];

            if (isIcon) ImGui.PushFont(UiBuilder.IconFont);
            drawList.AddText(textPos, ImGui.GetColorU32(textColor), label);
            if (isIcon) ImGui.PopFont();

            // Hover sheen sweep on hover-enter (keyed on the button's unique id so each
            // page button tracks its own hover state)
            float sheen = uiStyles.UpdateAndGetHoverSweepProgress($"pagebtn_{id}", isHovered);
            if (sheen >= 0f)
                Windows.Styles.UIStyles.DrawHoverSheen(drawList, buttonPos, buttonPos + size, sheen, maxAlpha: 0.22f);

            return result;
        }

        // Inline top pagination - placed on the same row as Add Character / filter / search via
        // SameLine at a computed X so it sits centered in the window. Adds zero vertical footprint
        // to the toolbar row. Only shown when there's more than one page.
        private void DrawInlinePagination(float scale)
        {
            var filteredCharacters = GetFilteredCharacters();
            if (filteredCharacters.Count <= charactersPerPage) return;

            int totalPages = (int)Math.Ceiling((double)filteredCharacters.Count / charactersPerPage);
            if (totalPages <= 1) return;

            // Same dimensions as the bottom pagination so icons render cleanly inside the button bounds
            float buttonWidth = 30f * scale;
            float buttonHeight = 25f * scale;
            float buttonSpacing = 8f * scale;
            float arrowButtonWidth = 25f * scale;

            const int maxPageButtons = 10;
            int startPage = Math.Max(0, currentPage - maxPageButtons / 2);
            int endPage = Math.Min(totalPages - 1, startPage + maxPageButtons - 1);
            if (endPage - startPage + 1 < maxPageButtons)
                startPage = Math.Max(0, endPage - maxPageButtons + 1);

            int visiblePageCount = endPage - startPage + 1;
            float totalWidth = arrowButtonWidth + buttonSpacing
                             + visiblePageCount * (buttonWidth + buttonSpacing)
                             + arrowButtonWidth;

            // Place the pagination on the same row as Add Character, horizontally centered in the
            // window. SameLine(x) keeps us on the current row and positions the next item at x.
            float windowWidth = ImGui.GetWindowWidth();
            float centerX = Math.Max(10f * scale, (windowWidth - totalWidth) / 2);
            ImGui.SameLine(centerX);

            bool isCustomTheme = plugin.Configuration.SelectedTheme == ThemeSelection.Custom;
            int arrowColorCount = 0;
            if (!isCustomTheme)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
                arrowColorCount = 3;
            }

            // Previous
            bool canGoPrev = currentPage > 0;
            if (!canGoPrev) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
            if (DrawCenteredButton("\uf053", "##topPrev", new Vector2(arrowButtonWidth, buttonHeight), true) && canGoPrev)
            {
                currentPage--;
                InvalidateCache();
                scrollToTopOnNextFrame = true;
            }
            if (!canGoPrev) ImGui.PopStyleVar();
            if (ImGui.IsItemHovered() && canGoPrev) CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Previous page");

            ImGui.SameLine(0, buttonSpacing);

            for (int i = startPage; i <= endPage; i++)
            {
                bool isCurrentPage = i == currentPage;
                int pageColorCount = 0;
                if (isCurrentPage)
                {
                    // Active page - customisable, defaults to blue
                    Vector4 activeCol;
                    if (isCustomTheme)
                    {
                        var config = plugin.Configuration.CustomTheme;
                        if (config.ColorOverrides.TryGetValue("custom.pageButtonActive", out var packed) && packed.HasValue)
                            activeCol = Styles.CustomThemeDefinitions.UnpackColor(packed.Value);
                        else
                            activeCol = new Vector4(0.4f, 0.6f, 1.0f, 0.8f);
                    }
                    else
                    {
                        activeCol = new Vector4(0.4f, 0.6f, 1.0f, 0.8f);
                    }
                    ImGui.PushStyleColor(ImGuiCol.Button, activeCol);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(
                        Math.Min(1f, activeCol.X + 0.1f), Math.Min(1f, activeCol.Y + 0.1f),
                        Math.Min(1f, activeCol.Z + 0.1f), Math.Min(1f, activeCol.W + 0.2f)));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(
                        Math.Max(0f, activeCol.X - 0.1f), Math.Max(0f, activeCol.Y - 0.1f),
                        Math.Max(0f, activeCol.Z - 0.1f), activeCol.W));
                    pageColorCount = 3;
                }
                else if (!isCustomTheme)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.25f, 0.25f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.35f, 0.35f, 0.35f, 1.0f));
                    pageColorCount = 3;
                }

                if (DrawCenteredButton((i + 1).ToString(), $"##top{i}", new Vector2(buttonWidth, buttonHeight), false))
                {
                    currentPage = i;
                    InvalidateCache();
                    scrollToTopOnNextFrame = true;
                }
                if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (pageColorCount > 0) ImGui.PopStyleColor(pageColorCount);

                if (i < endPage) ImGui.SameLine(0, buttonSpacing);
            }

            ImGui.SameLine(0, buttonSpacing);

            // Next
            bool canGoNext = currentPage < totalPages - 1;
            if (!canGoNext) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
            if (DrawCenteredButton("\uf054", "##topNext", new Vector2(arrowButtonWidth, buttonHeight), true) && canGoNext)
            {
                currentPage++;
                InvalidateCache();
                scrollToTopOnNextFrame = true;
            }
            if (!canGoNext) ImGui.PopStyleVar();
            if (ImGui.IsItemHovered() && canGoNext) CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Next page");

            if (arrowColorCount > 0) ImGui.PopStyleColor(arrowColorCount);
        }

        private void DrawPagination(float scale)
        {
            var filteredCharacters = GetFilteredCharacters();

            if (filteredCharacters.Count <= charactersPerPage)
            {
                currentPage = 0;
                return;
            }

            int totalPages = (int)Math.Ceiling((double)filteredCharacters.Count / charactersPerPage);
            if (totalPages <= 1) return;

            var pagedCharacters = GetPagedCharacters(filteredCharacters);

            // For sparse pages, add extra spacing to push pagination down
            if (pagedCharacters.Count <= 4)
            {
                float availableHeight = ImGui.GetContentRegionAvail().Y;
                float minSpacingForPagination = availableHeight * 0.4f; // Push to bottom 40% of remaining space

                ImGui.Dummy(new Vector2(0, Math.Max(50f * scale, minSpacingForPagination)));
            }
            else
            {
                // Normal spacing for full pages
                ImGui.Spacing();
                ImGui.Spacing();
                ImGui.Spacing();
            }

            // Rest of pagination code stays the same...
            float windowWidth = ImGui.GetContentRegionAvail().X;
            float buttonWidth = 30f * scale;
            float buttonHeight = 25f * scale;
            float buttonSpacing = 8f * scale;
            float arrowButtonWidth = 25f * scale;

            int maxPageButtons = 10;
            int startPage = Math.Max(0, currentPage - maxPageButtons / 2);
            int endPage = Math.Min(totalPages - 1, startPage + maxPageButtons - 1);
            if (endPage - startPage + 1 < maxPageButtons)
            {
                startPage = Math.Max(0, endPage - maxPageButtons + 1);
            }

            int visiblePageCount = endPage - startPage + 1;
            float totalWidth = arrowButtonWidth + buttonSpacing + (visiblePageCount * (buttonWidth + buttonSpacing)) + arrowButtonWidth;
            float startX = Math.Max(10f * scale, (windowWidth - totalWidth) / 2);

            ImGui.SetCursorPosX(startX);

            // Check if Custom theme is active - if so, use main window colours instead of pushing overrides
            bool isPaginationCustomTheme = plugin.Configuration.SelectedTheme == ThemeSelection.Custom;
            int paginationArrowColorCount = 0;

            // Previous button
            if (!isPaginationCustomTheme)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
                paginationArrowColorCount = 3;
            }

            bool canGoPrev = currentPage > 0;
            if (!canGoPrev)
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);

            if (DrawCenteredButton("\uf053", "##btmPrev", new Vector2(arrowButtonWidth, buttonHeight), true) && canGoPrev)
            {
                currentPage--;
                InvalidateCache();
                scrollToTopOnNextFrame = true;
            }

            if (!canGoPrev)
                ImGui.PopStyleVar();

            if (ImGui.IsItemHovered() && canGoPrev)
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Previous page");

            ImGui.SameLine(0, buttonSpacing);

            // Page number buttons
            for (int i = startPage; i <= endPage; i++)
            {
                bool isCurrentPage = i == currentPage;
                int pageButtonColorCount = 0;

                if (isCurrentPage)
                {
                    // Active page highlight - customisable via Custom Theme > Accents > Active Page Button
                    Vector4 activeCol;
                    if (isPaginationCustomTheme)
                    {
                        var config = plugin.Configuration.CustomTheme;
                        if (config.ColorOverrides.TryGetValue("custom.pageButtonActive", out var packed) && packed.HasValue)
                            activeCol = Styles.CustomThemeDefinitions.UnpackColor(packed.Value);
                        else
                            activeCol = new Vector4(0.4f, 0.6f, 1.0f, 0.8f);
                    }
                    else
                    {
                        activeCol = new Vector4(0.4f, 0.6f, 1.0f, 0.8f);
                    }

                    ImGui.PushStyleColor(ImGuiCol.Button, activeCol);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(
                        Math.Min(1f, activeCol.X + 0.1f), Math.Min(1f, activeCol.Y + 0.1f),
                        Math.Min(1f, activeCol.Z + 0.1f), Math.Min(1f, activeCol.W + 0.2f)));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(
                        Math.Max(0f, activeCol.X - 0.1f), Math.Max(0f, activeCol.Y - 0.1f),
                        Math.Max(0f, activeCol.Z - 0.1f), activeCol.W));
                    pageButtonColorCount = 3;
                }
                else if (!isPaginationCustomTheme)
                {
                    // Non-active pages: only push in non-Custom theme (Custom theme provides its own)
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.25f, 0.25f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.35f, 0.35f, 0.35f, 1.0f));
                    pageButtonColorCount = 3;
                }

                if (DrawCenteredButton((i + 1).ToString(), $"##btm{i}", new Vector2(buttonWidth, buttonHeight), false))
                {
                    currentPage = i;
                    InvalidateCache();
                    scrollToTopOnNextFrame = true;
                }

                if (ImGui.IsItemHovered())
                {
                    CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip($"Go to page {i + 1}");
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                }

                if (pageButtonColorCount > 0)
                    ImGui.PopStyleColor(pageButtonColorCount);

                if (i < endPage)
                    ImGui.SameLine(0, buttonSpacing);
            }

            ImGui.SameLine(0, buttonSpacing);

            // Next button
            bool canGoNext = currentPage < totalPages - 1;
            if (!canGoNext)
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);

            if (DrawCenteredButton("\uf054", "##btmNext", new Vector2(arrowButtonWidth, buttonHeight), true) && canGoNext)
            {
                currentPage++;
                InvalidateCache();
                scrollToTopOnNextFrame = true;
            }

            if (!canGoNext)
                ImGui.PopStyleVar();

            if (ImGui.IsItemHovered() && canGoNext)
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Next page");

            if (paginationArrowColorCount > 0)
                ImGui.PopStyleColor(paginationArrowColorCount);

            // Page info text
            ImGui.Spacing();
            string pageInfo = $"Page {currentPage + 1} of {totalPages} ({filteredCharacters.Count} characters)";
            var textSize = ImGui.CalcTextSize(pageInfo);
            ImGui.SetCursorPosX(Math.Max(10f * scale, (windowWidth - textSize.X) / 2));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
            ImGui.Text(pageInfo);
            ImGui.PopStyleColor();

            ImGui.Spacing();
            ImGui.Spacing();
        }

        private void ReorderCharacters(int fromIndex, int toIndex)
        {
            if (fromIndex == toIndex || fromIndex < 0 || toIndex < 0 ||
                fromIndex >= plugin.Characters.Count || toIndex >= plugin.Characters.Count)
                return;

            var character = plugin.Characters[fromIndex];

            plugin.Characters.RemoveAt(fromIndex);

            int insertIndex;
            if (fromIndex < toIndex)
            {
                insertIndex = toIndex - 1;
            }
            else
            {
                insertIndex = toIndex;
            }

            insertIndex = Math.Clamp(insertIndex, 0, plugin.Characters.Count);
            plugin.Characters.Insert(insertIndex, character);

            for (int i = 0; i < plugin.Characters.Count; i++)
            {
                plugin.Characters[i].SortOrder = i;
            }

            plugin.Configuration.CurrentSortIndex = (int)Plugin.SortType.Manual;
            plugin.SaveConfiguration();

            Plugin.Log.Debug($"[DragDrop] Moved character '{character.Name}' from position {fromIndex} to {insertIndex} (target was {toIndex})");
        }

        private void HandleCharacterClick(Character character, int index)
        {
            if (isDragging || draggedCharacterIndex != null)
                return;

            if (plugin.IsDesignPanelOpen)
            {
                plugin.IsDesignPanelOpen = false;
            }

            // Switch Penumbra collection if specified
            if (!string.IsNullOrEmpty(character.PenumbraCollection))
            {
                plugin.SwitchPenumbraCollection(character.PenumbraCollection);
            }
            
            // Apply Secret Mode mod states if configured
            if (character.SecretModState != null && character.SecretModState.Any())
            {
                _ = plugin.ApplySecretModState(character);
            }

            // Apply character-level mod option settings
            if (character.ModOptionSettings != null && character.ModOptionSettings.Any())
            {
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var (success, collectionId, collectionName) = plugin.PenumbraIntegration.GetCurrentCollection();
                        if (success)
                        {
                            Plugin.Log.Info($"[CharacterGrid] Applying character-level mod options for '{character.Name}' - {character.ModOptionSettings.Count} mods");
                            await plugin.PenumbraIntegration.ApplyModOptionsForDesign(collectionId, character.ModOptionSettings);
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error($"[CharacterGrid] Error applying character mod options: {ex}");
                    }
                });
            }

            plugin.ExecuteMacro(character.Macros, character, null);
            plugin.AchievementTracker?.OnSwitchFromMainWindow();
            plugin.AchievementTracker?.CheckSwitchMethodsAll();

            // Switch gearset if assigned at character level
            if (plugin.Configuration.EnableGearsetAssignments && character.AssignedGearset.HasValue)
            {
                plugin.SwitchToGearset(character.AssignedGearset.Value);
            }

            plugin.SetActiveCharacter(character);

            // Check if we should upload to server
            if (Plugin.ObjectTable.LocalPlayer is { } player && player.HomeWorld.IsValid)
            {
                string localName = player.Name.TextValue;
                string worldName = player.HomeWorld.Value.Name.ToString();
                string fullKey = $"{localName}@{worldName}";

                if (ShouldUploadToServer(character))
                {
                    var effectiveSharing = GetEffectiveSharingForUpload(character, fullKey);
                    var excludeFromSync = character.ExcludeFromNameSync; // Capture for closure
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        var profileToSend = plugin.BuildProfileForUpload(character);
                        _ = Plugin.UploadProfileAsync(profileToSend, character.LastInGameName ?? character.Name,
                            sharingOverride: effectiveSharing, excludeFromNameSync: excludeFromSync);
                    });
                    Plugin.Log.Info($"[CharacterGrid] ✓ Uploading profile for {character.Name} (effective sharing: {effectiveSharing}, excluded: {excludeFromSync})");
                }
                else
                {
                    Plugin.Log.Info($"[CharacterGrid] ⚠ Skipped upload for {character.Name} (NeverShare)");
                }
            }
            plugin.QuickSwitchWindow.UpdateSelectionFromCharacter(character);
        }

        /// <summary>Shift+Click on a character card opens the Wardrobe for that character without changing plugin active state.</summary>
        private void HandleShiftClickWardrobe(Character character, int index)
        {
            if (isDragging || draggedCharacterIndex != null)
                return;

            // Set the Wardrobe's target character WITHOUT changing plugin active state
            if (plugin.WardrobeWindow != null)
            {
                plugin.WardrobeWindow.TargetCharacter = character;
                plugin.WardrobeWindow.IsOpen = true;
                plugin.AchievementTracker?.OnWardrobeOpened();
            }
        }

        private bool ShouldUploadToServer(Character character)
        {
            var sharing = character.RPProfile?.Sharing ?? ProfileSharing.AlwaysShare;

            // NeverShare = never upload to server
            if (sharing == ProfileSharing.NeverShare)
            {
                Plugin.Log.Debug($"[CharacterGrid-ShouldUpload] NeverShare - not uploading {character.Name}");
                return false;
            }

            // AlwaysShare and ShowcasePublic both upload to server
            Plugin.Log.Debug($"[CharacterGrid-ShouldUpload] ✓ {sharing} - uploading {character.Name}");
            return true;
        }

        private ProfileSharing GetEffectiveSharingForUpload(Character character, string currentPhysicalCharacter)
        {
            // ExcludeFromNameSync = upload as NeverShare so server cache excludes this character
            if (character.ExcludeFromNameSync)
            {
                Plugin.Log.Debug($"[CharacterGrid-Sharing] ExcludeFromNameSync - sending as NeverShare");
                return ProfileSharing.NeverShare;
            }

            var sharing = character.RPProfile?.Sharing ?? ProfileSharing.AlwaysShare;

            // NeverShare and AlwaysShare are sent as-is
            if (sharing != ProfileSharing.ShowcasePublic)
                return sharing;

            // ShowcasePublic: Only send as ShowcasePublic (gallery listing) if on Main Character
            var userMain = plugin.Configuration.GalleryMainCharacter;
            bool onMainCharacter = !string.IsNullOrEmpty(userMain) && currentPhysicalCharacter == userMain;

            if (onMainCharacter)
            {
                Plugin.Log.Debug($"[CharacterGrid-Sharing] ShowcasePublic on Main Character - will appear in Gallery");
                return ProfileSharing.ShowcasePublic;
            }
            else
            {
                Plugin.Log.Debug($"[CharacterGrid-Sharing] ShowcasePublic but not on Main Character - sending as AlwaysShare");
                return ProfileSharing.AlwaysShare;
            }
        }

        private List<Character> GetFilteredCharacters()
        {
            if (filterCacheDirty ||
                searchQuery != lastSearchQuery ||
                selectedTag != lastSelectedTag ||
                plugin.Characters.Count != lastCharacterCount)
            {
                RecalculateFilteredCharacters();
            }

            return cachedFilteredCharacters;
        }
        private float GetSafeScale(float baseScale)
        {
            return Math.Clamp(baseScale, 0.3f, 5.0f);
        }

        private void RecalculateFilteredCharacters()
        {
            var characters = plugin.Characters.AsEnumerable();

            // Apply tag filter
            if (selectedTag != "All")
            {
                characters = characters.Where(c => c.Tags?.Contains(selectedTag) ?? false);
            }

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                characters = characters.Where(c =>
                    c.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));
            }

            cachedFilteredCharacters = characters.ToList();

            lastSearchQuery = searchQuery;
            lastSelectedTag = selectedTag;
            lastCharacterCount = plugin.Characters.Count;
            filterCacheDirty = false;
        }

        private List<Character> GetPagedCharacters(List<Character> filteredCharacters)
        {
            int startIndex = currentPage * charactersPerPage;
            var pagedResult = filteredCharacters.Skip(startIndex).Take(charactersPerPage).ToList();

            if (cachedPagedCharacters == null || !cachedPagedCharacters.SequenceEqual(pagedResult))
            {
                cachedPagedCharacters = pagedResult;
            }

            return cachedPagedCharacters;
        }

        private float UpdateHoverAnimation(int characterIndex, bool isHovered)
        {
            if (!hoverAnimations.ContainsKey(characterIndex))
                hoverAnimations[characterIndex] = 0f;

            float target = isHovered ? 1f : 0f;
            float current = hoverAnimations[characterIndex];

            // Only update if there's a significant change
            if (Math.Abs(target - current) > 0.01f)
            {
                float speed = 8f;
                current = current + (target - current) * ImGui.GetIO().DeltaTime * speed;
                current = Math.Clamp(current, 0f, 1f);
                hoverAnimations[characterIndex] = current;
            }

            return current;
        }

        private Vector2 UpdateHalloweenWiggle(int characterIndex, int totalCharacters)
        {
            if (!SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration) || 
                SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration) != SeasonalTheme.Halloween)
            {
                return Vector2.Zero;
            }

            float currentTime = (float)ImGui.GetTime();

            // Check if it's time to trigger new wiggles
            if (currentTime - lastWiggleCheck >= WiggleCheckInterval)
            {
                lastWiggleCheck = currentTime;
                
                // Random chance to start wiggles on 1-3 random characters
                Random rand = new Random();
                int numWiggles = rand.Next(1, 4); // 1-3 wiggles
                
                for (int i = 0; i < numWiggles; i++)
                {
                    int randomIndex = rand.Next(0, totalCharacters);
                    
                    // Don't start a new wiggle if one is already active
                    if (!wiggleStartTimes.ContainsKey(randomIndex) || 
                        currentTime - wiggleStartTimes[randomIndex] >= WiggleDuration)
                    {
                        wiggleStartTimes[randomIndex] = currentTime;
                    }
                }
                
                // Clean up expired wiggles
                var expiredWiggles = wiggleStartTimes.Where(kvp => currentTime - kvp.Value >= WiggleDuration).ToList();
                foreach (var expired in expiredWiggles)
                {
                    wiggleStartTimes.Remove(expired.Key);
                    wiggleOffsets.Remove(expired.Key);
                }
            }

            // Calculate wiggle offset for this character
            if (wiggleStartTimes.ContainsKey(characterIndex))
            {
                float wiggleElapsed = currentTime - wiggleStartTimes[characterIndex];
                
                if (wiggleElapsed < WiggleDuration)
                {
                    // Sine wave wiggle with decay
                    float progress = wiggleElapsed / WiggleDuration;
                    float intensity = (1f - progress) * WiggleIntensity; // Decay over time
                    float wiggleFreq = 15f; // Fast wiggle
                    
                    float offsetX = (float)(Math.Sin(wiggleElapsed * wiggleFreq) * intensity);
                    float offsetY = (float)(Math.Sin(wiggleElapsed * wiggleFreq * 1.3f) * intensity * 0.5f); // Less Y movement
                    
                    return new Vector2(offsetX, offsetY);
                }
            }

            return Vector2.Zero;
        }

        public void SortCharacters()
        {
            if (CurrentSort == Plugin.SortType.Favorites)
            {
                plugin.Characters.Sort((a, b) =>
                {
                    int favCompare = b.IsFavorite.CompareTo(a.IsFavorite);
                    if (favCompare != 0) return favCompare;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            }
            else if (CurrentSort == Plugin.SortType.Manual)
            {
                plugin.Characters.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            }
            else if (CurrentSort == Plugin.SortType.Alphabetical)
            {
                plugin.Characters.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            else if (CurrentSort == Plugin.SortType.Recent)
            {
                plugin.Characters.Sort((a, b) => b.DateAdded.CompareTo(a.DateAdded));
            }
            else if (CurrentSort == Plugin.SortType.Oldest)
            {
                plugin.Characters.Sort((a, b) => a.DateAdded.CompareTo(b.DateAdded));
            }

            InvalidateCache();
        }


        public void SetSortType(Plugin.SortType sortType)
        {
            CurrentSort = sortType;
            SortCharacters();
        }

        public void InvalidateCache()
        {
            cardRectsDirty = true;
            layoutCacheDirty = true;
            InvalidateFilterCache();
        }

        private void InvalidateFilterCache()
        {
            filterCacheDirty = true;
        }

        // Method to clear file cache when needed
        public void ClearFileCache()
        {
            fileExistsCache.Clear();
        }

        /// <summary>
        /// Pre-warms the file exists cache on a background thread.
        /// This prevents UI freezing when opening the window for the first time,
        /// especially for images on network paths.
        /// </summary>
        public void PreWarmCacheAsync()
        {
            if (isCacheWarming) return;
            isCacheWarming = true;

            Task.Run(() =>
            {
                try
                {
                    var characters = plugin.Configuration.Characters;
                    string pluginDirectory = plugin.PluginDirectory;
                    string defaultImagePath = Path.Combine(pluginDirectory, "Assets", "Default.png");

                    // Pre-check default image
                    var defaultExists = File.Exists(defaultImagePath);
                    lock (fileExistsCache)
                    {
                        fileExistsCache[defaultImagePath] = defaultExists;
                    }

                    // Pre-check all character images
                    foreach (var character in characters.ToList())
                    {
                        if (!string.IsNullOrEmpty(character.ImagePath))
                        {
                            var exists = File.Exists(character.ImagePath);
                            lock (fileExistsCache)
                            {
                                fileExistsCache[character.ImagePath] = exists;
                            }
                        }

                        // Also check design preview images
                        foreach (var design in character.Designs ?? Enumerable.Empty<CharacterDesign>())
                        {
                            if (!string.IsNullOrEmpty(design.PreviewImagePath))
                            {
                                var exists = File.Exists(design.PreviewImagePath);
                                lock (fileExistsCache)
                                {
                                    fileExistsCache[design.PreviewImagePath] = exists;
                                }
                            }
                        }
                    }

                    Plugin.Log.Info($"[CharacterGrid] Pre-warmed file cache for {fileExistsCache.Count} paths");
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"[CharacterGrid] Error pre-warming cache: {ex.Message}");
                }
                finally
                {
                    isCacheWarming = false;
                }
            });
        }

        // Method to clear text cache when font changes
        public void ClearTextCache()
        {
            textSizeCache.Clear();
        }

        /// <summary>Returns currently visible characters (respects search and tag filters).</summary>
        public List<Character> GetVisibleCharacters()
        {
            return GetFilteredCharacters();
        }

    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using CharacterSelectPlugin.Windows.Styles;
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
        private float cachedAvailableWidth = 0f;
        private float cachedScale = 0f;
        private bool layoutCacheDirty = true;

        // Cache expensive string operations
        private readonly Dictionary<string, bool> fileExistsCache = new();
        private readonly Dictionary<string, Vector2> textSizeCache = new();

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
            
            // Draw Halloween spider webs and fog behind everything (before toolbar)
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration) && 
                SeasonalThemeManager.GetCurrentSeasonalTheme() == SeasonalTheme.Halloween)
            {
                DrawHalloweenSpiderWebs();
                
                // Set fog area right before drawing
                Vector2 windowSize = ImGui.GetWindowSize();
                fogEffect?.SetEffectArea(windowSize);
                
                fogEffect?.Draw(); // Draw fog on same layer as spider webs
            }
            
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
            
            // Update fog effect for Halloween theme
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration) && 
                SeasonalThemeManager.GetCurrentSeasonalTheme() == SeasonalTheme.Halloween)
            {
                // Set fog effect area to current window content region
                Vector2 contentSize = ImGui.GetContentRegionAvail();
                if (contentSize.X > 0 && contentSize.Y > 0)
                {
                    fogEffect.SetEffectArea(contentSize);
                }
                fogEffect.Update(deltaTime);
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

                DrawSearchAndFilters(scale);
            }
        }

        private void DrawSearchAndFilters(float scale)
        {
            float tagDropdownWidth = 200f * scale;
            float tagIconOffset = 70f * scale;
            float tagDropdownOffset = tagDropdownWidth + tagIconOffset + (10f * scale);
            float buttonSize = 25f * scale;

            // Tag Filter Toggle
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

            float containerMargin = 17f * scale; // Scale the margin
            ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(containerMargin, containerMargin));

            if (columnCount > 1)
            {
                ImGui.Columns(columnCount, "CharacterGrid", false);
                float columnWidth = cardWidth + (plugin.ProfileSpacing * scale) + (24f * scale); // Scale spacing
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
        }

        private void RecalculateLayout(float availableWidth, float scale)
        {
            float profileSpacing = plugin.ProfileSpacing * scale;
            int columnCount = plugin.ProfileColumns;

            if (plugin.IsDesignPanelOpen)
            {
                columnCount = Math.Max(1, columnCount - 1);
            }

            float cardWidth = 250 * plugin.ProfileImageScale * scale;
            float borderMargin = 12f * scale;
            float totalCardWidth = cardWidth + (borderMargin * 2);
            float columnWidth = totalCardWidth + profileSpacing;

            // Ensure column count fits within available space
            columnCount = Math.Max(1, Math.Min(columnCount, (int)(availableWidth / columnWidth)));

            // Cache the results
            cachedCardWidth = cardWidth;
            cachedColumnCount = columnCount;
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
            
            // Override border color for Halloween theme with alternating pattern
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration) && 
                SeasonalThemeManager.GetCurrentSeasonalTheme() == SeasonalTheme.Halloween)
            {
                var themeColors = SeasonalThemeManager.GetCurrentThemeColors(plugin.Configuration);
                // Alternate between orange and purple based on character index
                borderColor = index % 2 == 0 
                    ? new Vector3(themeColors.PrimaryAccent.X, themeColors.PrimaryAccent.Y, themeColors.PrimaryAccent.Z)    // Orange
                    : new Vector3(themeColors.SecondaryAccent.X, themeColors.SecondaryAccent.Y, themeColors.SecondaryAccent.Z); // Purple
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
                SeasonalThemeManager.GetCurrentSeasonalTheme() == SeasonalTheme.Halloween)
            {
                // Add spider webs to all character cards with hover animation
                DrawCharacterCardSpiderWebs(drawList, wiggleCardMin, cardWidth, imageHeight, scale, hoverAmount);
            }

            DrawIntegratedNameplate(character, wiggleCardMin, cardWidth, imageHeight, nameplateHeight, index, hoverAmount, scale);

            ImGui.EndGroup();
            ImGui.Dummy(new Vector2(0, spacing));
        }

        private string GetCachedImagePath(string? characterImagePath, string defaultImagePath)
        {
            if (!string.IsNullOrEmpty(characterImagePath))
            {
                if (!fileExistsCache.TryGetValue(characterImagePath, out bool exists))
                {
                    exists = File.Exists(characterImagePath);
                    fileExistsCache[characterImagePath] = exists;
                }

                if (exists)
                    return characterImagePath;
            }

            if (!fileExistsCache.TryGetValue(defaultImagePath, out bool defaultExists))
            {
                defaultExists = File.Exists(defaultImagePath);
                fileExistsCache[defaultImagePath] = defaultExists;
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

            ImGui.PushFont(UiBuilder.IconFont);
            string crownSymbol = "\uf521";
            var crownSize = GetCachedTextSize(crownSymbol);

            var crownPos = new Vector2(
                badgeCenter.X - crownSize.X / 2 + (1f * scale),
                badgeCenter.Y - crownSize.Y / 2 - (1f * scale)
            );

            uint crownGlow = ImGui.GetColorU32(new Vector4(1f, 0.8f, 0.2f, 0.6f + hoverAmount * 0.4f));
            drawList.AddText(crownPos + new Vector2(1 * scale, 1 * scale), crownGlow, crownSymbol);

            uint crownColor = ImGui.GetColorU32(new Vector4(1f, 0.9f, 0.3f, 1f));
            drawList.AddText(crownPos, crownColor, crownSymbol);

            ImGui.PopFont();
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

            // Favourite Star/Ghost
            string starSymbol;
            bool usesFontAwesome = false;
            
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration) && 
                SeasonalThemeManager.GetCurrentSeasonalTheme() == SeasonalTheme.Halloween)
            {
                starSymbol = "\uf6e2"; // Ghost icon (same icon, different colors for favorite/unfavorite)
                usesFontAwesome = true;
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
            
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration) && 
                SeasonalThemeManager.GetCurrentSeasonalTheme() == SeasonalTheme.Halloween)
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
            else
            {
                // Default colors
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
                ImGui.SetTooltip($"{(character.IsFavorite ? "Remove" : "Add")} {character.Name} as a Favourite");

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    var actualCharacter = plugin.Characters[characterIndex];
                    actualCharacter.IsFavorite = !actualCharacter.IsFavorite;

                    Vector2 effectPos = starPos + starSize / 2;
                    if (!characterFavoriteEffects.ContainsKey(characterIndex))
                        characterFavoriteEffects[characterIndex] = new FavoriteSparkEffect();
                    characterFavoriteEffects[characterIndex].Trigger(effectPos, actualCharacter.IsFavorite);

                    plugin.SaveConfiguration();
                    SortCharacters();
                }
            }

            // Character Name
            var textSize = GetCachedTextSize(character.Name);
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
            if (canDrag && hoveringNameArea)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                ImGui.SetTooltip("Drag to reorder characters\n(Manual sort mode only)");
            }

            drawList.AddText(textPos + new Vector2(1 * scale, 1 * scale), ImGui.GetColorU32(new Vector4(0, 0, 0, 0.8f)), character.Name);
            drawList.AddText(textPos, ImGui.GetColorU32(new Vector4(0.95f, 0.95f, 0.95f, 1f)), character.Name);

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

            var iconHitMin = iconPos - new Vector2(2 * scale, 2 * scale);
            var iconHitMax = iconPos + iconSize + new Vector2(2 * scale, 2 * scale);

            if (ImGui.IsMouseHoveringRect(iconHitMin, iconHitMax))
            {
                ImGui.SetTooltip($"View RolePlay Profile for {character.Name}");

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

            ImGui.SetCursorScreenPos(new Vector2(nameplateMin.X + (8 * scale), bottomRowY));

            // Halloween themed button styling or default
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration) && 
                SeasonalThemeManager.GetCurrentSeasonalTheme() == SeasonalTheme.Halloween)
            {
                // Halloween button styling - dark orange theme
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.10f, 0.05f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.15f, 0.08f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.40f, 0.20f, 0.10f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.87f, 0.70f, 1.0f)); // Warm white text
            }
            else
            {
                // Default button styling
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.25f, 0.25f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.35f, 0.35f, 0.35f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
            }
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));

            var buttonPos = ImGui.GetCursorScreenPos();
            var buttonSize = new Vector2(btnWidth, btnHeight);

            if (ImGui.Button($"Designs##{character.Name}", new Vector2(btnWidth, btnHeight)))
            {
                int realIndex = plugin.Characters.IndexOf(character);
                if (realIndex >= 0)
                    plugin.OpenDesignPanel(realIndex);
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
            
            if (ImGui.Button($"Edit##{character.Name}", new Vector2(btnWidth, btnHeight)))
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

            ImGui.SameLine(0, btnSpacing);
            if (ImGui.Button($"Delete##{character.Name}", new Vector2(btnWidth, btnHeight)))
            {
                if (isCtrlShiftPressed)
                {
                    plugin.Characters.Remove(character);
                    plugin.Configuration.Save();
                    InvalidateCache();
                }
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("Hold Ctrl + Shift and click to delete.");
                ImGui.EndTooltip();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(4);
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

                // Set cursor when hovering over draggable area
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    ImGui.SetTooltip("Drag to reorder characters\n(Manual sort mode only)");
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
                ImGui.Text("\uf521");
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
                ImGui.Text("\uf521");
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

            // Previous button
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));

            bool canGoPrev = currentPage > 0;
            if (!canGoPrev)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
            }

            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button("\uf053", new Vector2(arrowButtonWidth, buttonHeight)) && canGoPrev)
            {
                currentPage--;
                InvalidateCache();
            }
            ImGui.PopFont();

            if (!canGoPrev)
            {
                ImGui.PopStyleVar();
            }

            if (ImGui.IsItemHovered() && canGoPrev)
            {
                ImGui.SetTooltip("Previous page");
            }

            ImGui.SameLine(0, buttonSpacing);

            // Page number buttons
            for (int i = startPage; i <= endPage; i++)
            {
                bool isCurrentPage = i == currentPage;

                if (isCurrentPage)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.6f, 1.0f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.7f, 1.0f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.3f, 0.5f, 0.9f, 1.0f));
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.25f, 0.25f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.35f, 0.35f, 0.35f, 1.0f));
                }

                string pageLabel = (i + 1).ToString();
                if (ImGui.Button(pageLabel, new Vector2(buttonWidth, buttonHeight)))
                {
                    currentPage = i;
                    InvalidateCache();
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Go to page {i + 1}");
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                }

                ImGui.PopStyleColor(3);

                if (i < endPage)
                {
                    ImGui.SameLine(0, buttonSpacing);
                }
            }

            ImGui.SameLine(0, buttonSpacing);

            // Next button
            bool canGoNext = currentPage < totalPages - 1;
            if (!canGoNext)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
            }

            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button("\uf054", new Vector2(arrowButtonWidth, buttonHeight)) && canGoNext)
            {
                currentPage++;
                InvalidateCache();
            }
            ImGui.PopFont();

            if (!canGoNext)
            {
                ImGui.PopStyleVar();
            }

            if (ImGui.IsItemHovered() && canGoNext)
            {
                ImGui.SetTooltip("Next page");
            }

            ImGui.PopStyleColor(3);

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

            plugin.ExecuteMacro(character.Macros, character, null);
            plugin.SetActiveCharacter(character);

            // Check if we should upload to gallery
            if (Plugin.ClientState.LocalPlayer is { } player && player.HomeWorld.IsValid)
            {
                string localName = player.Name.TextValue;
                string worldName = player.HomeWorld.Value.Name.ToString();
                string fullKey = $"{localName}@{worldName}";

                bool shouldUploadToGallery = ShouldUploadToGallery(character, fullKey);

                if (shouldUploadToGallery)
                {
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        var profileToSend = new RPProfile
                        {
                            Pronouns = character.RPProfile?.Pronouns,
                            Gender = character.RPProfile?.Gender,
                            Age = character.RPProfile?.Age,
                            Race = character.RPProfile?.Race,
                            Orientation = character.RPProfile?.Orientation,
                            Relationship = character.RPProfile?.Relationship,
                            Occupation = character.RPProfile?.Occupation,
                            Abilities = character.RPProfile?.Abilities,
                            Bio = character.RPProfile?.Bio,
                            Tags = character.RPProfile?.Tags,
                            CustomImagePath = !string.IsNullOrEmpty(character.RPProfile?.CustomImagePath)
                                ? character.RPProfile.CustomImagePath
                                : character.ImagePath,
                            ImageZoom = character.RPProfile?.ImageZoom ?? 1.0f,
                            ImageOffset = character.RPProfile?.ImageOffset ?? Vector2.Zero,
                            Sharing = character.RPProfile?.Sharing ?? ProfileSharing.AlwaysShare,
                            ProfileImageUrl = character.RPProfile?.ProfileImageUrl,
                            CharacterName = character.Name,
                            NameplateColor = character.RPProfile?.ProfileColor ?? character.NameplateColor,
                            BackgroundImage = character.BackgroundImage,
                            Effects = character.Effects ?? new ProfileEffects(),
                            GalleryStatus = character.GalleryStatus,
                            Links = character.RPProfile?.Links,
                            LastActiveTime = plugin.Configuration.ShowRecentlyActiveStatus ? DateTime.UtcNow : null
                        };

                        _ = Plugin.UploadProfileAsync(profileToSend, character.LastInGameName ?? character.Name);
                    });
                    Plugin.Log.Info($"[CharacterGrid] ✓ Uploading profile for {character.Name}");
                }
                else
                {
                    Plugin.Log.Info($"[CharacterGrid] ⚠ Skipped gallery upload for {character.Name} (not on main character or not public)");
                }
            }
            plugin.QuickSwitchWindow.UpdateSelectionFromCharacter(character);
        }
        private bool ShouldUploadToGallery(Character character, string currentPhysicalCharacter)
        {
            // Is there a main character set?
            var userMain = plugin.Configuration.GalleryMainCharacter;
            if (string.IsNullOrEmpty(userMain))
            {
                Plugin.Log.Debug($"[CharacterGrid-ShouldUpload] No main character set - not uploading {character.Name}");
                return false;
            }

            // Are we currently on the main character?
            if (currentPhysicalCharacter != userMain)
            {
                Plugin.Log.Debug($"[CharacterGrid-ShouldUpload] Current character '{currentPhysicalCharacter}' != main '{userMain}' - not uploading {character.Name}");
                return false;
            }

            // Is this CS+ character set to public sharing?
            var sharing = character.RPProfile?.Sharing ?? ProfileSharing.AlwaysShare;
            if (sharing != ProfileSharing.ShowcasePublic)
            {
                Plugin.Log.Debug($"[CharacterGrid-ShouldUpload] Character '{character.Name}' sharing is '{sharing}' (not public) - not uploading");
                return false;
            }

            Plugin.Log.Debug($"[CharacterGrid-ShouldUpload] ✓ All checks passed - will upload {character.Name} as {currentPhysicalCharacter}");
            return true;
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
                SeasonalThemeManager.GetCurrentSeasonalTheme() != SeasonalTheme.Halloween)
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

        // Method to clear text cache when font changes
        public void ClearTextCache()
        {
            textSizeCache.Clear();
        }

    }
}

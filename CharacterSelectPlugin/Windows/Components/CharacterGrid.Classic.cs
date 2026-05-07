using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using CharacterSelectPlugin.Windows.Styles;
using CharacterSelectPlugin.Windows.Utils;
using CharacterSelectPlugin.Effects;

namespace CharacterSelectPlugin.Windows.Components
{
    public partial class CharacterGrid
    {
        private void DrawClassicToolbar(float scale)
        {
            if (!plugin.IsAddCharacterWindowOpen)
            {
                float buttonHeight = 25f * scale;

                if (ImGui.Button("Add Character", new Vector2(0, buttonHeight)))
                {
                    plugin.OpenAddCharacterWindow();
                    InvalidateCache();
                }

                plugin.AddCharacterButtonPos = ImGui.GetItemRectMin();
                plugin.AddCharacterButtonSize = ImGui.GetItemRectSize();
                uiStyles.ApplyHoverSheenToLastItem("addcharbtn");

                DrawClassicInlinePagination(scale);

                DrawSearchAndFilters(scale);
            }
        }

        private void DrawClassicLayout()
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
            
            DrawClassicToolbar(totalScale);
            DrawClassicCharacterGridContent(totalScale);

            // Throttle animation updates
            float currentTime = (float)ImGui.GetTime();
            if (currentTime - lastAnimationUpdate >= AnimationUpdateInterval)
            {
                UpdateEffects(ImGui.GetIO().DeltaTime);
                lastAnimationUpdate = currentTime;
            }

            DrawEffects();
            DrawClassicPagination(totalScale);

            // Draw the ghost image last so it appears on top of everything
            DrawDragGhostImage(totalScale);
        }

        private void DrawClassicCharacterGridContent(float scale)
{
            // Reset scroll to top when page changes
            if (scrollToTopOnNextFrame)
            {
                ImGui.SetScrollY(0);
                scrollToTopOnNextFrame = false;
            }

            var filteredCharacters = GetFilteredCharacters();
            var pagedCharacters = GetPagedCharacters(filteredCharacters);

            float availableWidth = ImGui.GetContentRegionAvail().X;
            if (Math.Abs(availableWidth - cachedAvailableWidth) > 1f || 
                Math.Abs(scale - cachedScale) > 0.01f || 
                layoutCacheDirty)
            {
                ClassicRecalculateLayout(availableWidth, scale);
            }

            float cardWidth = cachedCardWidth;
            int columnCount = cachedColumnCount;

            // Centre the grid horizontally
            float columnWidth = cardWidth + (plugin.ProfileSpacing * scale) + (24f * scale);
            float totalGridWidth = columnCount > 1
                ? columnCount * columnWidth
                : cardWidth;
            float horizontalIndent = Math.Max(17f * scale, (availableWidth - totalGridWidth) / 2f);
            float verticalMargin = 17f * scale;

            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + verticalMargin);
            ImGui.Indent(horizontalIndent);

            if (columnCount > 1)
            {
                ImGui.Columns(columnCount, "CharacterGrid", false);
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

                DrawClassicCharacterCard(character, realCharacterIndex, cardWidth, scale);

                if (columnCount > 1)
                    ImGui.NextColumn();
            }

            // Reset columns
            if (columnCount > 1)
            {
                ImGui.Columns(1);
            }

            ImGui.Unindent(horizontalIndent);
        }

        private void DrawClassicCharacterCard(Character character, int index, float cardWidth, float scale)
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
                ClassicHandleCharacterClick(character, index);
            }

            if (ImGui.BeginPopupContextItem($"##ContextMenu_{character.Name}"))
            {
                DrawClassicContextMenu(character, scale);
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
                if (!customTheme.UseNameplateColorForCardGlow &&
                    customTheme.ColorOverrides.TryGetValue("custom.cardGlow", out var packedGlowColor) && packedGlowColor.HasValue)
                {
                    var glowColor = CustomThemeDefinitions.UnpackColor(packedGlowColor.Value);
                    borderColor = new Vector3(glowColor.X, glowColor.Y, glowColor.Z);
                }
            }
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
                    DrawClassicCharacterCardIcicles(drawList, wiggleCardMin, cardWidth, imageHeight, scale);
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
                    // GIF swap-in when hovering
                    var renderHandle = texture.Handle;
                    float originalWidth = texture.Width;
                    float originalHeight = texture.Height;
                    bool isGifActive = false;

                    if (!string.IsNullOrWhiteSpace(character.AnimatedImagePath))
                    {
                        var animWrap = plugin.AnimatedTextureCache?.GetOrLoad(character);
                        if (animWrap != null)
                        {
                            animWrap.IsHovered = isHovered;
                            if (isHovered && animWrap.Width > 0 && animWrap.Height > 0)
                            {
                                renderHandle = animWrap.Handle;
                                originalWidth = animWrap.Width;
                                originalHeight = animWrap.Height;
                                isGifActive = true;
                            }
                        }
                    }

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

                    if (isGifActive)
                    {
                        finalWidth *= character.AnimatedZoom;
                        finalHeight *= character.AnimatedZoom;
                    }
                    else
                    {
                        finalWidth *= character.PortraitZoom;
                        finalHeight *= character.PortraitZoom;
                    }

                    float paddingX = (imageAreaSize.X - finalWidth) / 2;
                    float paddingY = (imageAreaSize.Y - finalHeight) / 2;
                    float liftOffset = -2f * hoverAmount * scale;

                    var imagePos = imageArea + new Vector2(paddingX, paddingY + liftOffset);
                    if (isGifActive)
                    {
                        imagePos += new Vector2(imageAreaSize.X * character.AnimatedOffsetX, imageAreaSize.Y * character.AnimatedOffsetY);
                    }
                    else
                    {
                        imagePos += new Vector2(imageAreaSize.X * character.PortraitOffsetX, imageAreaSize.Y * character.PortraitOffsetY);
                    }
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

                    drawList.PushClipRect(imageArea, imageArea + imageAreaSize, true);
                    drawList.AddImageRounded(
                        renderHandle,
                        imagePos,
                        imagePosMax,
                        uvMin,
                        uvMax,
                        ImGui.GetColorU32(new Vector4(1, 1, 1, 1)),
                        8f * scale,
                        ImDrawFlags.RoundCornersTop
                    );
                    drawList.PopClipRect();

                    if (isMainCharacter && plugin.Configuration.ShowMainCharacterCrown)
                    {
                        DrawClassicMainCharacterCrown(drawList, imagePosMax, imagePos, hoverAmount, scale);
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
                DrawClassicCharacterCardSnowOverlay(drawList, wiggleCardMin, cardWidth, imageHeight, scale, hoverAmount);
            }

            // Draw chocolate.png overlay in top left corner for Valentine's theme
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration) &&
                SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration) == SeasonalTheme.Valentines)
            {
                DrawCharacterCardChocolateOverlay(drawList, wiggleCardMin, cardWidth, imageHeight, scale, hoverAmount);
            }

            DrawClassicIntegratedNameplate(character, wiggleCardMin, cardWidth, imageHeight, nameplateHeight, index, hoverAmount, scale);

            // Perimeter streak on hover, gated by EnableCharacterHoverEffects, traces the expanded glow rect.
            if (plugin.Configuration.EnableCharacterHoverEffects)
            {
                var glowMin = wiggleCardMin - new Vector2(borderMargin, borderMargin);
                var glowMax = wiggleCardMax + new Vector2(borderMargin, borderMargin);
                // Per-element hover-elapsed time keeps the streak origin stable across cards.
                float streakElapsed = UIStyles.GetHoverElapsedTime($"charstreak_{index}", isHovered);
                if (streakElapsed >= 0f)
                    DrawClassicPerimeterStreak(drawList, glowMin, glowMax, hoverAmount, scale, borderColor, streakElapsed);

                // One-shot glossy sheen sweep across the card on hover-enter
                float cardSheen = uiStyles.UpdateAndGetHoverSweepProgress($"charcard_{index}", isHovered);
                if (cardSheen >= 0f)
                    UIStyles.DrawHoverSheen(drawList, wiggleCardMin, wiggleCardMax, cardSheen, maxAlpha: 0.14f);
            }

            ImGui.EndGroup();
            ImGui.Dummy(new Vector2(0, spacing));
        }

        private void DrawClassicIntegratedNameplate(Character character, Vector2 cardMin, float cardWidth, float imageHeight, float nameplateHeight, int characterIndex, float hoverAmount, float scale)
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
                ImGui.SetTooltip($"{(character.IsFavorite ? "Remove" : "Add")} {character.Name} as a Favourite");

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
                ClassicHandleCharacterDragAndDrop(characterIndex, nameAreaMin, nameAreaMax, character, scale);
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
                    ImGui.SetTooltip(character.Name);
                }
                else if (canDrag)
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    ImGui.SetTooltip("Drag to reorder characters\n(Manual sort mode only)");
                }
            }

            drawList.AddText(textPos + new Vector2(1 * scale, 1 * scale), ImGui.GetColorU32(new Vector4(0, 0, 0, 0.8f)), displayName);
            drawList.AddText(textPos, ImGui.GetColorU32(new Vector4(0.95f, 0.95f, 0.95f, 1f)), displayName);

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
                ImGui.SetTooltip(tooltip);

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
                    ImGui.SetTooltip(ImGui.GetIO().KeyShift ? "Open Wardrobe" : "Designs (Shift+Click: Wardrobe)");
                else
                    ImGui.SetTooltip("Shift+Click: Open Wardrobe");
            }

            // Store for tutorial
            if (plugin.Characters.IndexOf(character) == 0)
            {
                plugin.FirstCharacterDesignsButtonPos = buttonPos;
                plugin.FirstCharacterDesignsButtonSize = buttonSize;
            }

            ImGui.SameLine(0, btnSpacing);

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
                if (ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift)
                {
                    // Best-effort server cleanup, never blocks local deletion.
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

        private void ClassicHandleCharacterClick(Character character, int index)
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

        private void ClassicHandleCharacterDragAndDrop(int characterIndex, Vector2 areaMin, Vector2 areaMax, Character character, float scale)
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

        /// <summary>Hover streak around a card's perimeter, accented to the card colour.</summary>
        private static void DrawClassicPerimeterStreak(ImDrawListPtr dl, Vector2 mn, Vector2 mx, float hoverAmount, float scale, Vector3 accent, float hoverElapsed)
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
                float thickness = (1f - t * 0.45f) * 2.5f * scale;
                dl.AddLine(p1, p2,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, segAlpha)), thickness);
            }

            // Head dot, brighter tint of the accent for visibility on dark nameplates.
            var headPt = WalkPerimeter(streakHead, mn, mx, rounding);
            var headCore = new Vector4(
                accent.X + (1f - accent.X) * 0.5f,
                accent.Y + (1f - accent.Y) * 0.5f,
                accent.Z + (1f - accent.Z) * 0.5f,
                0.95f * alpha);
            dl.AddCircleFilled(headPt, 5f * scale,
                ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, 0.20f * alpha)));
            dl.AddCircleFilled(headPt, 2.5f * scale,
                ImGui.ColorConvertFloat4ToU32(headCore));

            // Trailing particles aligned to hoverElapsed; skip ones spawned before this hover began.
            var anchor = (mn + mx) * 0.5f;
            float now = hoverElapsed;
            int particleCount = 6;
            float maxDrift = 10f;
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
                float pR = (1.8f - lifeProgress * 0.6f) * scale;
                dl.AddCircleFilled(pos, pR,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, pAlpha)));
            }
        }

        private void DrawClassicPagination(float scale)
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

            if (DrawClassicCenteredButton("\uf053", "##btmPrev", new Vector2(arrowButtonWidth, buttonHeight), true) && canGoPrev)
            {
                currentPage--;
                InvalidateCache();
                scrollToTopOnNextFrame = true;
            }

            if (!canGoPrev)
                ImGui.PopStyleVar();

            if (ImGui.IsItemHovered() && canGoPrev)
                ImGui.SetTooltip("Previous page");

            ImGui.SameLine(0, buttonSpacing);

            // Page number buttons
            for (int i = startPage; i <= endPage; i++)
            {
                bool isCurrentPage = i == currentPage;
                int pageButtonColorCount = 0;

                if (isCurrentPage)
                {
                    // Active page highlight , customisable via Custom Theme > Accents > Active Page Button
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

                if (DrawClassicCenteredButton((i + 1).ToString(), $"##btm{i}", new Vector2(buttonWidth, buttonHeight), false))
                {
                    currentPage = i;
                    InvalidateCache();
                    scrollToTopOnNextFrame = true;
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Go to page {i + 1}");
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

            if (DrawClassicCenteredButton("\uf054", "##btmNext", new Vector2(arrowButtonWidth, buttonHeight), true) && canGoNext)
            {
                currentPage++;
                InvalidateCache();
                scrollToTopOnNextFrame = true;
            }

            if (!canGoNext)
                ImGui.PopStyleVar();

            if (ImGui.IsItemHovered() && canGoNext)
                ImGui.SetTooltip("Next page");

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

        // Inline top pagination, sits on the toolbar row at a computed X.
        private void DrawClassicInlinePagination(float scale)
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

            // Place the pagination on the same row as Add Character, horizontally centred in the window.
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
            if (DrawClassicCenteredButton("\uf053", "##topPrev", new Vector2(arrowButtonWidth, buttonHeight), true) && canGoPrev)
            {
                currentPage--;
                InvalidateCache();
                scrollToTopOnNextFrame = true;
            }
            if (!canGoPrev) ImGui.PopStyleVar();
            if (ImGui.IsItemHovered() && canGoPrev) ImGui.SetTooltip("Previous page");

            ImGui.SameLine(0, buttonSpacing);

            for (int i = startPage; i <= endPage; i++)
            {
                bool isCurrentPage = i == currentPage;
                int pageColorCount = 0;
                if (isCurrentPage)
                {
                    // Active page , customisable, defaults to blue
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

                if (DrawClassicCenteredButton((i + 1).ToString(), $"##top{i}", new Vector2(buttonWidth, buttonHeight), false))
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
            if (DrawClassicCenteredButton("\uf054", "##topNext", new Vector2(arrowButtonWidth, buttonHeight), true) && canGoNext)
            {
                currentPage++;
                InvalidateCache();
                scrollToTopOnNextFrame = true;
            }
            if (!canGoNext) ImGui.PopStyleVar();
            if (ImGui.IsItemHovered() && canGoNext) ImGui.SetTooltip("Next page");

            if (arrowColorCount > 0) ImGui.PopStyleColor(arrowColorCount);
        }

        private void DrawClassicMainCharacterCrown(ImDrawListPtr drawList, Vector2 imagePosMax, Vector2 imagePos, float hoverAmount, float scale)
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

        private void DrawClassicContextMenu(Character character, float scale)
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

        // Pagination button, InvisibleButton + manual draw for pixel-perfect centring.
        private bool DrawClassicCenteredButton(string label, string id, Vector2 size, bool isIcon)
{
            var drawList = ImGui.GetWindowDrawList();
            var buttonPos = ImGui.GetCursorScreenPos();

            bool result = ImGui.InvisibleButton(id, size);
            bool isHovered = ImGui.IsItemHovered();
            bool isActive = ImGui.IsItemActive();

            // Background , reads from current ImGui style so pushed colours are respected
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

        private void DrawClassicCharacterCardIcicles(ImDrawListPtr drawList, Vector2 cardMin, float cardWidth, float imageHeight, float scale)
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
                
                // Create icicle triangle hanging down from bottom border of the card
                float cardBottom = cardMin.Y + imageHeight + (65f * scale); // Move up slightly
                Vector2 topLeft = new Vector2(x - width, cardBottom);
                Vector2 topRight = new Vector2(x + width, cardBottom);
                Vector2 bottom = new Vector2(x, cardBottom + length);
                
                drawList.AddTriangleFilled(topLeft, topRight, bottom, iceColorU32);
                
                // Add highlight line
                Vector2 highlight1 = topLeft + new Vector2(0.3f, 0);
                Vector2 highlight2 = bottom + new Vector2(-0.3f, 0);
                drawList.AddLine(highlight1, highlight2, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.8f)), 1.5f);
            }
            
            // Add gentle snow particles falling from character card edges
            DrawClassicCharacterCardSnowParticles(drawList, cardMin, cardWidth, imageHeight, scale);
        }

        private void DrawClassicCharacterCardSnowOverlay(ImDrawListPtr drawList, Vector2 cardMin, float cardWidth, float imageHeight, float scale, float hoverAmount)
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
                    float extraOffsetUp = 19f * scale; // Additional offset to move further up (reduced by 1px)
                    float extraOffsetLeft = 4f * scale; // Even less offset to the left to move more right (reduced by 1px)
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

        private void DrawClassicCharacterCardSnowParticles(ImDrawListPtr drawList, Vector2 cardMin, float cardWidth, float imageHeight, float scale)
{
            var snowColor = new Vector4(0.95f, 0.98f, 1.0f, 0.6f);
            uint snowColorU32 = ImGui.GetColorU32(snowColor);
            
            var random = new Random(123); // Different seed for particles
            
            // Snow particles falling from bottom edge
            int bottomParticles = 8 + random.Next(5); // 8-12 particles
            float cardBottom = cardMin.Y + imageHeight + (65f * scale);
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

        private void ClassicRecalculateLayout(float availableWidth, float scale)
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
            cachedColumnWidth = columnWidth;
            cachedAvailableWidth = availableWidth;
            cachedScale = scale;
            layoutCacheDirty = false;
        }

    }
}

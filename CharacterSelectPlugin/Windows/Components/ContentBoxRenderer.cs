using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Textures.TextureWraps;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace CharacterSelectPlugin.Windows.Components
{
    public static class ContentBoxRenderer
    {
        // Callback for opening linked character profiles (local - uses CS+ character name)
        public static Action<string>? OnOpenLinkedProfile { get; set; }

        // Callback for opening linked profiles from external/server profiles (uses in-game name for server lookup)
        public static Action<string>? OnOpenLinkedProfileExternal { get; set; }

        // Whether we're currently viewing an external profile (affects how connection clicks work)
        public static bool IsViewingExternalProfile { get; set; } = false;

        // Profile accent color for styling elements like quote borders
        public static Vector3 ProfileAccentColor { get; set; } = new Vector3(0.3f, 0.7f, 1.0f);

        // Default method for backward compatibility
        public static void RenderContentBox(ContentBox box, float availableWidth, float scale = 1.0f)
        {
            RenderContentBox(box, availableWidth, scale, false, null);
        }
        
        // Enhanced method with owner editing capability
        public static void RenderContentBox(ContentBox box, float availableWidth, float scale, bool isOwner, Action<ContentBox>? onModified)
        {
            // Don't add extra container since we're already inside a card
            // Just render the content directly
            
            // Render content based on layout type
            ImGui.BeginGroup();
            switch (box.LayoutType)
            {
                case ContentBoxLayoutType.Timeline:
                    RenderTimelineLayout(box, availableWidth, scale);
                    break;
                    
                case ContentBoxLayoutType.Grid:
                    RenderGridLayout(box, availableWidth, scale);
                    break;
                    
                case ContentBoxLayoutType.List:
                    RenderListLayout(box, availableWidth, scale, isOwner, onModified);
                    break;
                    
                case ContentBoxLayoutType.KeyValue:
                    RenderKeyValueLayout(box, availableWidth, scale);
                    break;
                    
                case ContentBoxLayoutType.Quote:
                    RenderQuoteLayout(box, availableWidth, scale);
                    break;
                    
                case ContentBoxLayoutType.ProsCons:
                    RenderProsConsLayout(box, availableWidth, scale);
                    break;
                    
                case ContentBoxLayoutType.Tagged:
                    RenderTaggedLayout(box, availableWidth, scale);
                    break;
                    
                case ContentBoxLayoutType.LikesDislikes:
                    RenderLikesDislikesLayout(box, availableWidth, scale);
                    break;

                case ContentBoxLayoutType.Connections:
                    RenderConnectionsLayout(box, availableWidth, scale);
                    break;

                default:
                    RenderStandardLayout(box, availableWidth, scale);
                    break;
            }
            ImGui.EndGroup();
        }
        
        private static void RenderTimelineLayout(ContentBox box, float width, float scale)
        {
            var drawList = ImGui.GetWindowDrawList();
            var startPos = ImGui.GetCursorScreenPos();
            
            // Parse timeline data
            var entries = ParseTimelineEntries(box.TimelineData);
            if (entries.Count == 0) return;
            
            var lineX = startPos.X + 15 * scale;
            var currentY = startPos.Y;
            
            // Draw gradient timeline line
            var lineEndY = currentY + (entries.Count * 100 * scale);
            DrawGradientLine(drawList, new Vector2(lineX, currentY), new Vector2(lineX, lineEndY), 
                new Vector4(0.3f, 0.7f, 0.3f, 0.8f), new Vector4(0.3f, 0.5f, 0.9f, 0.8f), 3f * scale);
            
            // Render each timeline entry
            foreach (var entry in entries)
            {
                // Save current position
                var cursorPos = ImGui.GetCursorPos();
                var nodePos = ImGui.GetCursorScreenPos();
                
                // Calculate card dimensions first
                var cardStart = nodePos + new Vector2(30 * scale, 0); // Space for timeline line
                var cardWidth = width - 50 * scale; // Account for margins and timeline
                
                // Estimate card height based on content
                var dateSize = ImGui.CalcTextSize(entry.Date);
                var eventLines = (int)Math.Ceiling(ImGui.CalcTextSize(entry.Event).X / (cardWidth - 24 * scale));
                var eventHeight = ImGui.GetTextLineHeightWithSpacing() * Math.Max(1, eventLines);
                
                var cardHeight = dateSize.Y + eventHeight + 24 * scale;
                var cardSize = new Vector2(cardWidth, cardHeight);
                
                // Draw node with glow effect
                DrawGlowingCircle(drawList, new Vector2(lineX, nodePos.Y + cardHeight / 2), 6 * scale,
                    new Vector4(0.3f, 0.7f, 0.3f, 1.0f));
                
                // Draw shadow first
                drawList.AddRectFilled(
                    cardStart + new Vector2(2 * scale, 2 * scale),
                    cardStart + cardSize + new Vector2(2 * scale, 2 * scale),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.3f)),
                    6 * scale
                );
                
                // Draw card background
                drawList.AddRectFilled(
                    cardStart,
                    cardStart + cardSize,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.15f, 0.95f)),
                    6 * scale
                );
                
                // Draw left accent border
                drawList.AddRectFilled(
                    cardStart,
                    cardStart + new Vector2(3 * scale, cardHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.7f, 0.3f, 1.0f)),
                    6 * scale,
                    ImDrawFlags.RoundCornersLeft
                );
                
                // Draw card border
                drawList.AddRect(
                    cardStart,
                    cardStart + cardSize,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.3f, 0.5f)),
                    6 * scale
                );
                
                // Reserve space for the card and position text
                ImGui.SetCursorPos(cursorPos);
                ImGui.InvisibleButton($"##timeline_space_{entry.Id}", new Vector2(width, cardHeight));
                
                // Save text position for both date and event
                var textStartPos = cursorPos + new Vector2(42 * scale, 8 * scale); // 30 for timeline + 12 for padding
                
                // Date
                ImGui.SetCursorPos(textStartPos);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 0.7f, 0.3f, 1.0f));
                ImGui.Text(entry.Date);
                ImGui.PopStyleColor();
                
                // Event - position it below the date
                ImGui.SetCursorPos(textStartPos + new Vector2(0, ImGui.GetTextLineHeightWithSpacing()));
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + cardWidth - 54 * scale);
                ImGui.TextWrapped(entry.Event);
                ImGui.PopTextWrapPos();
                
                // Move cursor past this card
                ImGui.SetCursorPos(cursorPos + new Vector2(0, cardHeight + 20 * scale));
            }
            // No unindent needed since we didn't indent
        }
        
        private static void RenderGridLayout(ContentBox box, float width, float scale)
        {
            var items = ParseGridItems(box.Content);
            if (items.Count == 0) return;
            
            var drawList = ImGui.GetWindowDrawList();
            var columns = Math.Max(2, Math.Min(6, (int)(width / (140 * scale))));
            var cellWidth = (width - ((columns - 1) * 10 * scale)) / columns;
            var cellHeight = 90 * scale;
            
            var startPos = ImGui.GetCursorScreenPos();
            
            for (int i = 0; i < items.Count; i++)
            {
                int col = i % columns;
                int row = i / columns;
                
                var cellPos = startPos + new Vector2(
                    col * (cellWidth + 10 * scale),
                    row * (cellHeight + 10 * scale)
                );
                
                var cellMin = cellPos;
                var cellMax = cellPos + new Vector2(cellWidth, cellHeight);
                
                bool isHovered = ImGui.IsMouseHoveringRect(cellMin, cellMax);
                
                // Draw shadow (elevated on hover)
                if (isHovered)
                {
                    for (int s = 3; s > 0; s--)
                    {
                        drawList.AddRectFilled(
                            cellMin + new Vector2(s * scale, s * scale),
                            cellMax + new Vector2(s * scale, s * scale),
                            ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.1f * (4 - s))),
                            8 * scale
                        );
                    }
                }
                else
                {
                    drawList.AddRectFilled(
                        cellMin + new Vector2(2 * scale, 2 * scale),
                        cellMax + new Vector2(2 * scale, 2 * scale),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.2f)),
                        8 * scale
                    );
                }
                
                // Draw gradient background
                var topColor = isHovered 
                    ? new Vector4(0.25f, 0.25f, 0.3f, 1.0f)
                    : new Vector4(0.18f, 0.18f, 0.18f, 1.0f);
                var bottomColor = isHovered
                    ? new Vector4(0.2f, 0.2f, 0.25f, 1.0f)  
                    : new Vector4(0.12f, 0.12f, 0.12f, 1.0f);
                    
                drawList.AddRectFilledMultiColor(
                    cellMin,
                    cellMax,
                    ImGui.ColorConvertFloat4ToU32(topColor),
                    ImGui.ColorConvertFloat4ToU32(topColor),
                    ImGui.ColorConvertFloat4ToU32(bottomColor),
                    ImGui.ColorConvertFloat4ToU32(bottomColor)
                );
                
                // Draw border
                if (isHovered)
                {
                    // Glow effect
                    drawList.AddRect(
                        cellMin - new Vector2(1 * scale, 1 * scale),
                        cellMax + new Vector2(1 * scale, 1 * scale),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.7f, 0.3f, 0.3f)),
                        8 * scale,
                        ImDrawFlags.None,
                        2 * scale
                    );
                }
                
                drawList.AddRect(
                    cellMin,
                    cellMax,
                    ImGui.ColorConvertFloat4ToU32(
                        isHovered 
                            ? new Vector4(0.3f, 0.7f, 0.3f, 1.0f)
                            : new Vector4(0.3f, 0.3f, 0.3f, 0.5f)
                    ),
                    8 * scale,
                    ImDrawFlags.None,
                    1 * scale
                );
                
                // Draw content directly without child window to avoid grey box
                var contentPos = cellMin + new Vector2(8 * scale, 8 * scale);
                
                if (!string.IsNullOrEmpty(items[i].Icon))
                {
                    // Try to render as game icon first
                    if (int.TryParse(items[i].Icon, out int iconId))
                    {
                        var gameIconTexture = GetGameIcon(iconId);
                        if (gameIconTexture != null)
                        {
                            var iconSize = 48 * scale; // Increased from 32 to 48
                            var iconPos = new Vector2(
                                cellMin.X + (cellWidth - iconSize) / 2,
                                cellMin.Y + 12 * scale
                            );
                            drawList.AddImage(gameIconTexture.Handle, iconPos, iconPos + new Vector2(iconSize, iconSize));
                        }
                        else
                        {
                            // Fallback to text display for invalid icon IDs
                            var iconText = $"Icon {iconId}";
                            var iconTextSize = ImGui.CalcTextSize(iconText);
                            var iconTextPos = new Vector2(
                                cellMin.X + (cellWidth - iconTextSize.X) / 2,
                                cellMin.Y + 20 * scale
                            );
                            drawList.AddText(iconTextPos, ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.6f, 0.6f, 1.0f)), iconText);
                        }
                    }
                    else
                    {
                        // Render as text/Unicode character
                        var iconTextSize = ImGui.CalcTextSize(items[i].Icon) * 1.5f;
                        var iconTextPos = new Vector2(
                            cellMin.X + (cellWidth - iconTextSize.X) / 2,
                            cellMin.Y + 20 * scale
                        );
                        drawList.AddText(iconTextPos, ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.8f, 0.4f, 1.0f)), items[i].Icon);
                    }
                }
                
                // Draw text label below icon using drawList to avoid child window
                var textStartY = string.IsNullOrEmpty(items[i].Icon) ? cellMin.Y + cellHeight / 2 - 10 * scale : cellMin.Y + 65 * scale;
                var textPos = new Vector2(cellMin.X + 8 * scale, textStartY);
                var textWidth = cellWidth - 16 * scale;
                var textSize = ImGui.CalcTextSize(items[i].Text);
                
                // Center text if it fits, otherwise left-align
                if (textSize.X < textWidth)
                {
                    textPos.X = cellMin.X + (cellWidth - textSize.X) / 2;
                }
                
                // Draw text using drawList
                drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.9f, 1.0f)), items[i].Text);
            }
            
            // Update cursor position
            int totalRows = (items.Count + columns - 1) / columns;
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + totalRows * (cellHeight + 10 * scale));
        }
        
        private static void RenderQuoteLayout(ContentBox box, float width, float scale)
        {
            var drawList = ImGui.GetWindowDrawList();
            var startPos = ImGui.GetCursorScreenPos();
            var accentColor = ProfileAccentColor;

            // Calculate height based on wrapped text
            var wrapWidth = width - 60 * scale;
            var textSize = ImGui.CalcTextSize($"\"{box.QuoteText}\"", false, wrapWidth);
            var cardHeight = Math.Max(100 * scale, textSize.Y + 80 * scale);

            // Shadow
            drawList.AddRectFilled(
                startPos + new Vector2(3 * scale, 3 * scale),
                startPos + new Vector2(width, cardHeight) + new Vector2(3 * scale, 3 * scale),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.3f)),
                12 * scale
            );

            // Gradient background
            drawList.AddRectFilledMultiColor(
                startPos,
                startPos + new Vector2(width, cardHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.15f, 0.95f)),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.18f, 0.18f, 0.18f, 0.95f)),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.12f, 0.12f, 0.12f, 0.95f)),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 0.95f))
            );

            // Left accent border (uses character's color)
            drawList.AddRectFilled(
                startPos,
                startPos + new Vector2(4 * scale, cardHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 1.0f))
            );

            // Decorative quote icon using FontAwesome
            ImGui.SetCursorScreenPos(startPos + new Vector2(14 * scale, 14 * scale));
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.3f), FontAwesomeIcon.QuoteLeft.ToIconString());
            ImGui.PopFont();

            // Quote text
            ImGui.SetCursorScreenPos(startPos + new Vector2(24 * scale, 32 * scale));
            ImGui.PushTextWrapPos(startPos.X + width - 24 * scale);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
            ImGui.TextWrapped($"\"{box.QuoteText}\"");
            ImGui.PopStyleColor();
            ImGui.PopTextWrapPos();

            // Author
            if (!string.IsNullOrEmpty(box.QuoteAuthor))
            {
                var authorText = $"— {box.QuoteAuthor}";
                var authorSize = ImGui.CalcTextSize(authorText);
                ImGui.SetCursorScreenPos(startPos + new Vector2(width - authorSize.X - 30 * scale, cardHeight - 30 * scale));
                ImGui.TextColored(new Vector4(accentColor.X * 0.8f, accentColor.Y * 0.9f, accentColor.Z, 1.0f), authorText);
            }

            ImGui.SetCursorScreenPos(startPos + new Vector2(0, cardHeight + 10 * scale));
        }

        private static void DrawItalicTextWrapped(ImDrawListPtr drawList, string text, float wrapWidth, uint color)
        {
            var startPos = ImGui.GetCursorScreenPos();

            // Skew factor controls how italic the text appears
            float skewFactor = 0.2f;
            float lineHeight = ImGui.GetTextLineHeight();
            float spaceWidth = ImGui.CalcTextSize(" ").X;

            var words = text.Split(' ');
            float currentX = startPos.X;
            float currentY = startPos.Y;
            float lineStartX = startPos.X;
            float maxY = currentY;

            foreach (var word in words)
            {
                var wordSize = ImGui.CalcTextSize(word);

                // Wrap to next line if needed
                if (currentX + wordSize.X > lineStartX + wrapWidth && currentX > lineStartX)
                {
                    currentX = lineStartX;
                    currentY += lineHeight;
                }

                // Draw each character with horizontal skew for italic effect
                foreach (char c in word)
                {
                    var charStr = c.ToString();
                    var charSize = ImGui.CalcTextSize(charStr);

                    // Skew: shift character right based on its height (top shifts more than bottom)
                    float skewOffset = charSize.Y * skewFactor;

                    drawList.AddText(new Vector2(currentX + skewOffset, currentY), color, charStr);
                    currentX += charSize.X;
                }

                currentX += spaceWidth;
                maxY = Math.Max(maxY, currentY);
            }

            // Move cursor past the rendered text
            ImGui.SetCursorScreenPos(new Vector2(startPos.X, maxY + lineHeight));
        }
        
        private static void RenderTaggedLayout(ContentBox box, float width, float scale)
        {
            var categories = ParseTagCategories(box.TaggedData);
            if (categories.Count == 0) return;
            
            var drawList = ImGui.GetWindowDrawList();
            
            foreach (var category in categories)
            {
                var categoryStartPos = ImGui.GetCursorScreenPos();
                
                // Reserve space for background - we'll draw it after measuring content
                ImGui.BeginGroup();
                
                // Category name
                ImGui.Dummy(new Vector2(10 * scale, 0));
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.5f, 0.7f, 1.0f, 1.0f), category.Name.ToUpper());
                ImGui.Dummy(new Vector2(0, 5 * scale));
                
                // Render tags as pills
                var startX = ImGui.GetCursorPosX();
                var maxX = startX + width;
                var lineY = ImGui.GetCursorPosY();
                
                foreach (var tag in category.Tags)
                {
                    var tagSize = ImGui.CalcTextSize(tag);
                    var pillWidth = tagSize.X + 24 * scale;
                    var pillHeight = 26 * scale;
                    
                    // Check if we need to wrap to next line
                    if (ImGui.GetCursorPosX() + pillWidth > maxX && ImGui.GetCursorPosX() > startX)
                    {
                        ImGui.SetCursorPosX(startX);
                        ImGui.SetCursorPosY(lineY + pillHeight + 8 * scale);
                        lineY = ImGui.GetCursorPosY();
                    }
                    
                    var pillPos = ImGui.GetCursorScreenPos();
                    bool isHovered = ImGui.IsMouseHoveringRect(
                        pillPos,
                        pillPos + new Vector2(pillWidth, pillHeight)
                    );
                    
                    // Shadow
                    if (isHovered)
                    {
                        drawList.AddRectFilled(
                            pillPos + new Vector2(1 * scale, 1 * scale),
                            pillPos + new Vector2(pillWidth, pillHeight) + new Vector2(1 * scale, 1 * scale),
                            ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.4f)),
                            pillHeight / 2
                        );
                    }
                    
                    // Pill background
                    drawList.AddRectFilled(
                        pillPos,
                        pillPos + new Vector2(pillWidth, pillHeight),
                        ImGui.ColorConvertFloat4ToU32(
                            isHovered 
                                ? new Vector4(0.4f, 0.4f, 0.5f, 1.0f)
                                : new Vector4(0.3f, 0.3f, 0.3f, 1.0f)
                        ),
                        pillHeight / 2
                    );
                    
                    // Border on hover
                    if (isHovered)
                    {
                        drawList.AddRect(
                            pillPos,
                            pillPos + new Vector2(pillWidth, pillHeight),
                            ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.8f, 0.4f, 1.0f)),
                            pillHeight / 2,
                            ImDrawFlags.None,
                            1 * scale
                        );
                    }
                    
                    // Tag text
                    drawList.AddText(
                        pillPos + new Vector2(12 * scale, (pillHeight - tagSize.Y) / 2),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.9f, 1.0f)),
                        tag
                    );
                    
                    // Move cursor for next tag
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + pillWidth + 8 * scale);
                }
                
                ImGui.EndGroup();
                
                // Now draw the background that wraps both category and tags
                var categoryEndPos = ImGui.GetItemRectMax();
                drawList.AddRectFilled(
                    categoryStartPos - new Vector2(5 * scale, 5 * scale),
                    categoryEndPos + new Vector2(5 * scale, 10 * scale),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.15f, 0.3f)),
                    6 * scale
                );
                drawList.AddRect(
                    categoryStartPos - new Vector2(5 * scale, 5 * scale),
                    categoryEndPos + new Vector2(5 * scale, 10 * scale),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.15f, 0.8f)),
                    6 * scale
                );
                
                ImGui.SetCursorPosY(categoryEndPos.Y - ImGui.GetWindowPos().Y + 20 * scale);
            }
        }
        
        // Helper methods
        private static void DrawGradientLine(ImDrawListPtr drawList, Vector2 start, Vector2 end, 
            Vector4 startColor, Vector4 endColor, float thickness)
        {
            int segments = 20;
            for (int i = 0; i < segments; i++)
            {
                float t1 = (float)i / segments;
                float t2 = (float)(i + 1) / segments;
                
                var color1 = Vector4.Lerp(startColor, endColor, t1);
                var color2 = Vector4.Lerp(startColor, endColor, t2);
                
                var pos1 = Vector2.Lerp(start, end, t1);
                var pos2 = Vector2.Lerp(start, end, t2);
                
                drawList.AddLine(pos1, pos2, ImGui.ColorConvertFloat4ToU32(color1), thickness);
            }
        }
        
        private static void DrawGlowingCircle(ImDrawListPtr drawList, Vector2 center, float radius, Vector4 color)
        {
            // Glow layers
            for (int i = 3; i > 0; i--)
            {
                var glowAlpha = 0.1f * (4 - i);
                drawList.AddCircleFilled(
                    center,
                    radius + i * 2,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, glowAlpha)),
                    16
                );
            }
            
            // Main circle
            drawList.AddCircleFilled(center, radius, ImGui.ColorConvertFloat4ToU32(color), 16);
            
            // White center highlight
            drawList.AddCircleFilled(
                center,
                radius * 0.3f,
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.8f)),
                12
            );
        }
        
        private static float GetEstimatedHeight(ContentBox box, float width, float scale)
        {
            // Estimate height based on content type and amount
            return box.LayoutType switch
            {
                ContentBoxLayoutType.Timeline => 150 * scale * Math.Max(1, ParseTimelineEntries(box.TimelineData).Count),
                ContentBoxLayoutType.Grid => 100 * scale * ((ParseGridItems(box.Content).Count + 2) / 3),
                ContentBoxLayoutType.Quote => 150 * scale,
                _ => 200 * scale
            };
        }
        
        // Add remaining layout renderers...
        private static void RenderListLayout(ContentBox box, float width, float scale, bool isOwner = false, Action<ContentBox>? onModified = null)
        {
            var items = ParseListItems(box.Content);
            if (items.Count == 0) return;
            
            var drawList = ImGui.GetWindowDrawList();
            var listType = box.Subtitle?.ToLower() ?? "bullet";
            
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6 * scale, 10 * scale));
            
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var itemPos = ImGui.GetCursorScreenPos();
                
                // Indent for nested items
                if (item.IndentLevel > 0)
                {
                    ImGui.Indent(item.IndentLevel * 20 * scale);
                }
                
                switch (listType)
                {
                    case "numbered":
                        ImGui.TextColored(new Vector4(0.5f, 0.7f, 1.0f, 1.0f), $"{i + 1}.");
                        ImGui.SameLine();
                        break;
                        
                    case "checkbox":
                        var checkPos = itemPos + new Vector2(0, 2 * scale);
                        
                        // Custom checkbox drawing
                        var checkSize = 18 * scale;
                        
                        // Check if mouse is hovering over checkbox (only if owner)
                        bool isHovered = false;
                        if (isOwner)
                        {
                            var mousePos = ImGui.GetMousePos();
                            isHovered = mousePos.X >= checkPos.X && mousePos.X <= checkPos.X + checkSize &&
                                       mousePos.Y >= checkPos.Y && mousePos.Y <= checkPos.Y + checkSize;
                            
                            if (isHovered)
                            {
                                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                            }
                        }
                        
                        // Background color changes on hover for owner
                        var bgColor = isHovered 
                            ? new Vector4(0.3f, 0.3f, 0.3f, 1.0f) 
                            : new Vector4(0.2f, 0.2f, 0.2f, 1.0f);
                        
                        drawList.AddRectFilled(
                            checkPos,
                            checkPos + new Vector2(checkSize, checkSize),
                            ImGui.ColorConvertFloat4ToU32(bgColor),
                            3 * scale
                        );
                        
                        // Border color changes on hover for owner
                        var borderColor = isHovered
                            ? new Vector4(0.5f, 0.9f, 0.5f, 1.0f)
                            : new Vector4(0.4f, 0.8f, 0.4f, 1.0f);
                        
                        drawList.AddRect(
                            checkPos,
                            checkPos + new Vector2(checkSize, checkSize),
                            ImGui.ColorConvertFloat4ToU32(borderColor),
                            3 * scale,
                            ImDrawFlags.None,
                            1.5f * scale
                        );
                        
                        if (item.IsChecked)
                        {
                            // Draw checkmark
                            var p1 = checkPos + new Vector2(4 * scale, 9 * scale);
                            var p2 = checkPos + new Vector2(7 * scale, 12 * scale);
                            var p3 = checkPos + new Vector2(14 * scale, 5 * scale);
                            
                            drawList.AddLine(p1, p2, ImGui.ColorConvertFloat4ToU32(borderColor), 2 * scale);
                            drawList.AddLine(p2, p3, ImGui.ColorConvertFloat4ToU32(borderColor), 2 * scale);
                        }
                        
                        // Handle click for owner
                        if (isOwner && isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                        {
                            item.IsChecked = !item.IsChecked;
                            
                            // Update the content box with the modified items
                            box.Content = JsonConvert.SerializeObject(items);
                            
                            // Notify the parent window that content has been modified
                            onModified?.Invoke(box);
                        }
                        
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + checkSize + 8 * scale);
                        break;
                        
                    default: // bullet
                        // Custom bullet point
                        var bulletCenter = itemPos + new Vector2(8 * scale, ImGui.GetTextLineHeight() / 2);
                        drawList.AddCircleFilled(
                            bulletCenter,
                            3 * scale,
                            ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.8f, 0.4f, 1.0f)),
                            8
                        );
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 20 * scale);
                        break;
                }
                
                // Item text
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - 40 * scale);
                ImGui.Text(item.Text);
                ImGui.PopTextWrapPos();
                
                if (item.IndentLevel > 0)
                {
                    ImGui.Unindent(item.IndentLevel * 20 * scale);
                }
            }
            
            ImGui.PopStyleVar();
        }
        
        private static void RenderKeyValueLayout(ContentBox box, float width, float scale)
        {
            var pairs = ParseKeyValuePairs(box.LeftColumn, box.RightColumn);
            if (pairs.Count == 0) return;
            
            var drawList = ImGui.GetWindowDrawList();
            
            if (ImGui.BeginTable("##keyvalue", 2, ImGuiTableFlags.None))
            {
                ImGui.TableSetupColumn("Key", ImGuiTableColumnFlags.WidthFixed, 200 * scale);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
                
                foreach (var pair in pairs)
                {
                    ImGui.TableNextRow();
                    
                    // Key column with background
                    ImGui.TableNextColumn();
                    var keyPos = ImGui.GetCursorScreenPos();
                    var keySize = new Vector2(195 * scale, ImGui.GetTextLineHeight() + 12 * scale);
                    
                    // Key background with gradient
                    drawList.AddRectFilledMultiColor(
                        keyPos,
                        keyPos + keySize,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.25f, 1.0f)),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.2f, 1.0f)),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.2f, 1.0f)),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.25f, 1.0f))
                    );
                    
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8 * scale);
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 6 * scale);
                    ImGui.TextColored(new Vector4(0.5f, 0.7f, 1.0f, 1.0f), pair.Key);
                    
                    // Value column
                    ImGui.TableNextColumn();
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 6 * scale);
                    ImGui.TextWrapped(pair.Value);
                }
                
                ImGui.EndTable();
            }
        }
        
        private static void RenderProsConsLayout(ContentBox box, float width, float scale)
        {
            var pros = box.LeftColumn.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var cons = box.RightColumn.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            var drawList = ImGui.GetWindowDrawList();
            
            if (ImGui.BeginTable("##proscons", 2, ImGuiTableFlags.None))
            {
                ImGui.TableSetupColumn("Pros", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Cons", ImGuiTableColumnFlags.WidthStretch);
                
                ImGui.TableNextRow();
                
                // Pros column
                ImGui.TableNextColumn();
                var prosPos = ImGui.GetCursorScreenPos();
                var columnWidth = (width - 10 * scale) / 2;
                
                // Pros background
                drawList.AddRectFilled(
                    prosPos,
                    prosPos + new Vector2(columnWidth, (pros.Length + 1) * 30 * scale + 20 * scale),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.2f, 0.1f, 0.3f)),
                    6 * scale
                );
                
                // Pros header
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.8f, 0.4f, 1.0f));
                ImGui.Text("✓ STRENGTHS");
                ImGui.PopStyleColor();
                ImGui.Separator();
                ImGui.Spacing();
                
                foreach (var pro in pros)
                {
                    ImGui.Dummy(new Vector2(10 * scale, 0)); // Left padding
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.4f, 0.8f, 0.4f, 0.9f), "✓");
                    ImGui.SameLine();
                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + columnWidth - 40 * scale);
                    ImGui.Text(pro);
                    ImGui.PopTextWrapPos();
                }
                
                // Cons column
                ImGui.TableNextColumn();
                var consPos = ImGui.GetCursorScreenPos() - new Vector2(0, ImGui.GetScrollY());
                
                // Cons background
                drawList.AddRectFilled(
                    consPos,
                    consPos + new Vector2(columnWidth, (cons.Length + 1) * 30 * scale + 20 * scale),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.1f, 0.1f, 0.3f)),
                    6 * scale
                );
                
                // Cons header
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.3f, 0.3f, 1.0f));
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text(FontAwesomeIcon.Times.ToIconString());
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.Text("WEAKNESSES");
                ImGui.PopStyleColor();
                ImGui.Separator();
                ImGui.Spacing();
                
                foreach (var con in cons)
                {
                    ImGui.Dummy(new Vector2(10 * scale, 0)); // Left padding
                    ImGui.SameLine();
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.TextColored(new Vector4(0.9f, 0.3f, 0.3f, 0.9f), FontAwesomeIcon.Times.ToIconString());
                    ImGui.PopFont();
                    ImGui.SameLine();
                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + columnWidth - 40 * scale);
                    ImGui.Text(con);
                    ImGui.PopTextWrapPos();
                }
                
                ImGui.EndTable();
            }
        }
        
        private static void RenderLikesDislikesLayout(ContentBox box, float width, float scale)
        {
            var drawList = ImGui.GetWindowDrawList();
            
            // Likes section with styled items (no label)
            if (!string.IsNullOrEmpty(box.Likes))
            {
                var likes = box.Likes.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var like in likes)
                {
                    DrawLikesDislikesTraitItem(like.Trim(), width, scale, true);
                }
                
                if (!string.IsNullOrEmpty(box.Dislikes))
                    ImGui.Dummy(new Vector2(0, 10 * scale));
            }
            
            // Dislikes section with styled items (no label)
            if (!string.IsNullOrEmpty(box.Dislikes))
            {
                var dislikes = box.Dislikes.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var dislike in dislikes)
                {
                    DrawLikesDislikesTraitItem(dislike.Trim(), width, scale, false);
                }
            }
        }
        
        private static void DrawLikesDislikesTraitItem(string trait, float width, float scale, bool isLike)
        {
            var drawList = ImGui.GetWindowDrawList();
            var itemPos = ImGui.GetCursorScreenPos();
            var itemHeight = 32f * scale;
            
            // Item background
            drawList.AddRectFilled(
                itemPos,
                itemPos + new Vector2(width, itemHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.039f, 0.039f, 0.039f, 1.0f)),
                6f * scale
            );
            
            // Item border
            drawList.AddRect(
                itemPos,
                itemPos + new Vector2(width, itemHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.122f, 0.122f, 0.122f, 1.0f)),
                6f * scale,
                ImDrawFlags.None,
                1f * scale
            );
            
            // Left accent border (green for likes, red for dislikes)
            var accentColor = isLike 
                ? new Vector4(0.067f, 0.714f, 0.506f, 1.0f) // Green
                : new Vector4(0.8f, 0.2f, 0.2f, 1.0f);      // Red
            drawList.AddRectFilled(
                itemPos,
                itemPos + new Vector2(3 * scale, itemHeight),
                ImGui.ColorConvertFloat4ToU32(accentColor),
                6f * scale,
                ImDrawFlags.RoundCornersLeft
            );
            
            // Text with FontAwesome thumbs icons
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (itemHeight - ImGui.GetTextLineHeight()) * 0.5f);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12 * scale);
            
            // Draw icon first
            ImGui.PushFont(UiBuilder.IconFont);
            string icon = isLike ? FontAwesomeIcon.ThumbsUp.ToIconString() : FontAwesomeIcon.ThumbsDown.ToIconString();
            ImGui.TextColored(new Vector4(0.847f, 0.847f, 0.863f, 1.0f), icon);
            ImGui.PopFont();
            
            // Draw text next to icon
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.847f, 0.847f, 0.863f, 1.0f), trait);
            
            // Move cursor past the item
            ImGui.SetCursorPosY(itemPos.Y - ImGui.GetWindowPos().Y + itemHeight + 8 * scale);
        }
        
        private static void RenderStandardLayout(ContentBox box, float width, float scale)
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextWrapped(box.Content);
            ImGui.PopTextWrapPos();
        }
        
        // Parsing helper methods
        private static List<TimelineEntry> ParseTimelineEntries(string data)
        {
            if (string.IsNullOrEmpty(data)) return new List<TimelineEntry>();
            
            try
            {
                return JsonConvert.DeserializeObject<List<TimelineEntry>>(data) ?? new List<TimelineEntry>();
            }
            catch
            {
                // Legacy format fallback
                var entries = new List<TimelineEntry>();
                foreach (var line in data.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('|', 2);
                    if (parts.Length == 2)
                    {
                        entries.Add(new TimelineEntry { Date = parts[0].Trim(), Event = parts[1].Trim() });
                    }
                }
                return entries;
            }
        }
        
        private static List<GridItem> ParseGridItems(string content)
        {
            if (string.IsNullOrEmpty(content)) return new List<GridItem>();
            
            try
            {
                // Try to deserialize as JSON first (new format)
                return JsonConvert.DeserializeObject<List<GridItem>>(content) ?? new List<GridItem>();
            }
            catch
            {
                // Legacy format fallback
                var items = new List<GridItem>();
                foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split(' ', 2);
                    if (parts.Length == 2)
                    {
                        items.Add(new GridItem { Icon = parts[0], Text = parts[1] });
                    }
                    else
                    {
                        items.Add(new GridItem { Icon = "", Text = line });
                    }
                }
                return items;
            }
        }
        
        private static List<ListItem> ParseListItems(string content)
        {
            if (string.IsNullOrEmpty(content)) return new List<ListItem>();
            
            try
            {
                return JsonConvert.DeserializeObject<List<ListItem>>(content) ?? new List<ListItem>();
            }
            catch
            {
                // Simple text fallback
                var items = new List<ListItem>();
                foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var text = line;
                    bool isChecked = false;
                    
                    if (line.StartsWith("[x]") || line.StartsWith("[X]"))
                    {
                        isChecked = true;
                        text = line.Substring(3).Trim();
                    }
                    else if (line.StartsWith("[ ]"))
                    {
                        text = line.Substring(3).Trim();
                    }
                    
                    items.Add(new ListItem { Text = text, IsChecked = isChecked });
                }
                return items;
            }
        }
        
        private static List<KeyValuePairData> ParseKeyValuePairs(string keys, string values)
        {
            var pairs = new List<KeyValuePairData>();
            var keyArray = keys.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var valueArray = values.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            for (int i = 0; i < Math.Min(keyArray.Length, valueArray.Length); i++)
            {
                pairs.Add(new KeyValuePairData { Key = keyArray[i], Value = valueArray[i] });
            }
            
            return pairs;
        }
        
        private static List<TagCategory> ParseTagCategories(string data)
        {
            if (string.IsNullOrEmpty(data)) return new List<TagCategory>();
            
            try
            {
                return JsonConvert.DeserializeObject<List<TagCategory>>(data) ?? new List<TagCategory>();
            }
            catch
            {
                // Legacy format fallback
                var categories = new List<TagCategory>();
                var lines = data.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                TagCategory? currentCategory = null;
                
                foreach (var line in lines)
                {
                    if (line.EndsWith(":"))
                    {
                        if (currentCategory != null) categories.Add(currentCategory);
                        currentCategory = new TagCategory { Name = line.TrimEnd(':') };
                    }
                    else if (currentCategory != null)
                    {
                        currentCategory.Tags.AddRange(line.Split(',').Select(t => t.Trim()));
                    }
                }
                
                if (currentCategory != null) categories.Add(currentCategory);
                return categories;
            }
        }
        
        private static IDalamudTextureWrap? GetGameIcon(int iconId)
        {
            try
            {
                var texture = Plugin.TextureProvider.GetFromGameIcon((uint)iconId);
                return texture?.GetWrapOrEmpty();
            }
            catch
            {
                return null;
            }
        }

        private static void RenderConnectionsLayout(ContentBox box, float width, float scale)
        {
            var drawList = ImGui.GetWindowDrawList();
            var connections = ParseConnections(box.Content);

            if (connections.Count == 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.431f, 0.431f, 0.451f, 1.0f));
                ImGui.TextWrapped("No connections added yet.");
                ImGui.PopStyleColor();
                return;
            }

            foreach (var connection in connections)
            {
                var itemPos = ImGui.GetCursorScreenPos();
                var itemHeight = 44f * scale;
                var itemWidth = width;

                // Background card for each connection
                drawList.AddRectFilled(
                    itemPos,
                    itemPos + new Vector2(itemWidth, itemHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.055f, 0.055f, 0.055f, 1.0f)),
                    6f * scale
                );

                // Left accent bar - different color based on relationship type
                var displayType = connection.DisplayRelationshipType;
                var accentColor = GetRelationshipColor(displayType);
                drawList.AddRectFilled(
                    itemPos,
                    itemPos + new Vector2(4f * scale, itemHeight),
                    ImGui.ColorConvertFloat4ToU32(accentColor),
                    3f * scale,
                    ImDrawFlags.RoundCornersLeft
                );

                // Border
                drawList.AddRect(
                    itemPos,
                    itemPos + new Vector2(itemWidth, itemHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.11f, 0.11f, 0.11f, 1.0f)),
                    6f * scale,
                    ImDrawFlags.None,
                    1f * scale
                );

                // Relationship type label (top left, small)
                var labelPos = itemPos + new Vector2(12f * scale, 6f * scale);
                drawList.AddText(
                    labelPos,
                    ImGui.ColorConvertFloat4ToU32(accentColor),
                    displayType.ToUpper()
                );

                // Character name (larger, below label)
                var namePos = itemPos + new Vector2(12f * scale, 22f * scale);
                var nameColor = connection.IsOwnCharacter
                    ? new Vector4(0.4f, 0.7f, 1.0f, 1.0f)  // Blue for linked characters
                    : new Vector4(0.847f, 0.847f, 0.863f, 1.0f);  // White for others

                drawList.AddText(
                    namePos,
                    ImGui.ColorConvertFloat4ToU32(nameColor),
                    connection.Name
                );

                // Link icon for own characters (clickable if local, or if external with in-game name)
                bool canClick = connection.IsOwnCharacter && (
                    (!IsViewingExternalProfile && !string.IsNullOrEmpty(connection.LinkedCharacterName)) ||
                    (IsViewingExternalProfile && !string.IsNullOrEmpty(connection.LinkedCharacterInGameName))
                );

                if (canClick)
                {
                    var iconPos = itemPos + new Vector2(itemWidth - 30f * scale, (itemHeight - 16f * scale) / 2);

                    // Make clickable area
                    ImGui.SetCursorScreenPos(itemPos);
                    if (ImGui.InvisibleButton($"##conn_{connection.Id}", new Vector2(itemWidth, itemHeight)))
                    {
                        if (IsViewingExternalProfile)
                        {
                            // External profile - use in-game name to fetch from server
                            OnOpenLinkedProfileExternal?.Invoke(connection.LinkedCharacterInGameName!);
                        }
                        else
                        {
                            // Local profile - use CS+ character name
                            OnOpenLinkedProfile?.Invoke(connection.LinkedCharacterName!);
                        }
                    }

                    var displayName = IsViewingExternalProfile
                        ? connection.LinkedCharacterInGameName
                        : connection.LinkedCharacterName;

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                        // Hover highlight
                        drawList.AddRectFilled(
                            itemPos,
                            itemPos + new Vector2(itemWidth, itemHeight),
                            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.05f)),
                            6f * scale
                        );
                        ImGui.BeginTooltip();
                        ImGui.Text($"View {displayName}'s profile");
                        ImGui.EndTooltip();
                    }

                    // Draw link icon
                    ImGui.PushFont(UiBuilder.IconFont);
                    drawList.AddText(
                        iconPos,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.7f, 1.0f, 0.8f)),
                        FontAwesomeIcon.Link.ToIconString()
                    );
                    ImGui.PopFont();
                }
                else
                {
                    // Non-clickable spacer
                    ImGui.Dummy(new Vector2(itemWidth, itemHeight));
                }

                ImGui.Dummy(new Vector2(0, 6f * scale));
            }
        }

        private static Vector4 GetRelationshipColor(string relationshipType)
        {
            return relationshipType.ToLower() switch
            {
                "family" or "sibling" or "parent" or "child" => new Vector4(0.9f, 0.6f, 0.3f, 1.0f),  // Orange for family
                "partner" or "spouse" => new Vector4(0.9f, 0.4f, 0.5f, 1.0f),  // Pink for romantic
                "friend" => new Vector4(0.4f, 0.8f, 0.4f, 1.0f),  // Green for friends
                "rival" or "enemy" => new Vector4(0.9f, 0.3f, 0.3f, 1.0f),  // Red for adversaries
                "mentor" or "student" => new Vector4(0.6f, 0.5f, 0.9f, 1.0f),  // Purple for teaching
                "colleague" or "acquaintance" => new Vector4(0.5f, 0.7f, 0.8f, 1.0f),  // Light blue for casual
                "alt" or "past self" => new Vector4(0.7f, 0.7f, 0.7f, 1.0f),  // Gray for self-references
                _ => new Vector4(0.6f, 0.6f, 0.6f, 1.0f)  // Default gray
            };
        }

        private static List<Connection> ParseConnections(string content)
        {
            if (string.IsNullOrEmpty(content)) return new List<Connection>();

            try
            {
                return JsonConvert.DeserializeObject<List<Connection>>(content) ?? new List<Connection>();
            }
            catch
            {
                return new List<Connection>();
            }
        }
    }
}
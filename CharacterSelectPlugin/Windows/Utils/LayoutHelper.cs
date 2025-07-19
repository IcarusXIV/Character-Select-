using System;
using System.Numerics;
using ImGuiNET;

namespace CharacterSelectPlugin.Windows.Utils
{
    public static class LayoutHelper
    {
        // Calculate responsive column count based on available width
        public static int CalculateColumnCount(float itemWidth, float spacing, float availableWidth, int maxColumns = 6)
        {
            if (availableWidth <= 0) return 1;

            float totalItemWidth = itemWidth + spacing;
            int columns = (int)Math.Floor(availableWidth / totalItemWidth);

            return Math.Clamp(columns, 1, maxColumns);
        }

        // Center content horizontally within available space
        public static void CenterHorizontally(float contentWidth)
        {
            float availableWidth = ImGui.GetContentRegionAvail().X;
            float offset = Math.Max(0, (availableWidth - contentWidth) / 2);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
        }

        // Center content vertically within available space
        public static void CenterVertically(float contentHeight)
        {
            float availableHeight = ImGui.GetContentRegionAvail().Y;
            float offset = Math.Max(0, (availableHeight - contentHeight) / 2);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offset);
        }

        // Center content both horizontally and vertically
        public static void CenterContent(Vector2 contentSize)
        {
            CenterHorizontally(contentSize.X);
            CenterVertically(contentSize.Y);
        }

        // Create a flexible row layout with evenly spaced items
        public static void FlexRow(float[] itemWidths, float spacing = 8f)
        {
            if (itemWidths.Length == 0) return;

            float totalItemWidth = 0;
            foreach (float width in itemWidths)
                totalItemWidth += width;

            float availableWidth = ImGui.GetContentRegionAvail().X;
            float totalSpacing = (itemWidths.Length - 1) * spacing;
            float extraSpace = Math.Max(0, availableWidth - totalItemWidth - totalSpacing);
            float extraSpacePerItem = extraSpace / itemWidths.Length;

            for (int i = 0; i < itemWidths.Length; i++)
            {
                if (i > 0)
                {
                    ImGui.SameLine(0, spacing);
                }

                float finalWidth = itemWidths[i] + extraSpacePerItem;
                ImGui.SetNextItemWidth(finalWidth);
            }
        }

        // Create a grid layout with automatic wrapping
        public static bool BeginGrid(string id, float itemWidth, float itemHeight, float spacing = 8f)
        {
            int columns = CalculateColumnCount(itemWidth, spacing, ImGui.GetContentRegionAvail().X);

            if (columns > 1)
            {
                ImGui.Columns(columns, id, false);

                // Set column widths
                for (int i = 0; i < columns; i++)
                {
                    ImGui.SetColumnWidth(i, itemWidth + spacing);
                }

                return true;
            }

            return false;
        }

        // End grid layout
        public static void EndGrid()
        {
            ImGui.Columns(1);
        }

        // Calculate image dimensions while maintaining aspect ratio
        public static Vector2 CalculateImageSize(Vector2 originalSize, float maxWidth, float maxHeight)
        {
            if (originalSize.X <= 0 || originalSize.Y <= 0)
                return new Vector2(maxWidth, maxHeight);

            float aspectRatio = originalSize.X / originalSize.Y;

            if (aspectRatio > 1) // Landscape
            {
                float width = Math.Min(maxWidth, originalSize.X);
                float height = width / aspectRatio;

                if (height > maxHeight)
                {
                    height = maxHeight;
                    width = height * aspectRatio;
                }

                return new Vector2(width, height);
            }
            else // Portrait or square
            {
                float height = Math.Min(maxHeight, originalSize.Y);
                float width = height * aspectRatio;

                if (width > maxWidth)
                {
                    width = maxWidth;
                    height = width / aspectRatio;
                }

                return new Vector2(width, height);
            }
        }

        // Create a responsive button layout
        public static void ResponsiveButtons(string[] buttonLabels, float minButtonWidth = 80f, float spacing = 8f)
        {
            if (buttonLabels.Length == 0) return;

            float availableWidth = ImGui.GetContentRegionAvail().X;
            float totalSpacing = (buttonLabels.Length - 1) * spacing;
            float buttonWidth = Math.Max(minButtonWidth, (availableWidth - totalSpacing) / buttonLabels.Length);

            for (int i = 0; i < buttonLabels.Length; i++)
            {
                if (i > 0)
                    ImGui.SameLine(0, spacing);

                ImGui.Button(buttonLabels[i], new Vector2(buttonWidth, 0));
            }
        }

        // Create a tooltip with proper positioning
        public static void Tooltip(string text, float maxWidth = 300f)
        {
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(maxWidth);
                ImGui.TextUnformatted(text);
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
        }

        // Create a separator with custom styling
        public static void StyledSeparator(Vector4 color, float thickness = 1f)
        {
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;

            drawList.AddLine(
                pos,
                pos + new Vector2(width, 0),
                ImGui.GetColorU32(color),
                thickness
            );

            ImGui.Dummy(new Vector2(0, thickness + 4f));
        }

        // Create a loading indicator
        public static void LoadingSpinner(float size = 20f, float thickness = 3f)
        {
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var center = pos + new Vector2(size / 2, size / 2);

            float angle = (float)(ImGui.GetTime() * 4f) % (2f * (float)Math.PI);
            uint color = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.8f));

            // Draw spinning arc
            drawList.PathArcTo(center, size / 2 - thickness / 2, angle, angle + (float)Math.PI * 1.5f, 16);
            drawList.PathStroke(color, ImDrawFlags.None, thickness);

            ImGui.Dummy(new Vector2(size, size));
        }

        // Get screen position for centering a popup
        public static Vector2 GetCenterScreenPos(Vector2 popupSize)
        {
            var viewport = ImGui.GetMainViewport();
            return viewport.Pos + (viewport.Size - popupSize) / 2f;
        }

        // Clamp text to fit within a specific width
        public static string ClampText(string text, float maxWidth, string ellipsis = "...")
        {
            if (ImGui.CalcTextSize(text).X <= maxWidth)
                return text;

            while (text.Length > 0 && ImGui.CalcTextSize(text + ellipsis).X > maxWidth)
            {
                text = text[..^1];
            }

            return text + ellipsis;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;

namespace CharacterSelectPlugin.Windows
{
    public partial class FontAwesomeIconPickerWindow
    {
        private void DrawClassicLayout()
{
            var windowSize = ImGui.GetWindowSize();
            var buttonHeight = 30f;
            var sidebarWidth = 120f;
            var padding = 8f;

            if (ImGui.BeginChild("Categories", new Vector2(sidebarWidth, -buttonHeight - padding * 2), true))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
                ImGui.Text("Categories");
                ImGui.PopStyleColor();
                ImGui.Separator();

                foreach (var category in CategoryOrder)
                {
                    if (category == "Favorites" && GetFavoriteIcons().Length == 0 && _selectedCategory != "Favorites")
                        continue;

                    var isSelected = category == _selectedCategory;
                    if (isSelected)
                        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));

                    var displayName = category;
                    if (category == "Favorites")
                    {
                        ImGui.PushFont(UiBuilder.IconFont);
                        var starIcon = FontAwesomeIcon.Star.ToIconString();
                        ImGui.PopFont();
                        displayName = $"{starIcon} Favorites";
                    }

                    if (ImGui.Button(category == "Favorites" ? "Favorites" : category, new Vector2(-1, 0)))
                        _selectedCategory = category;

                    if (category == "Favorites")
                    {
                        var buttonMin = ImGui.GetItemRectMin();
                        var drawList = ImGui.GetWindowDrawList();
                        ImGui.PushFont(UiBuilder.IconFont);
                        var starStr = FontAwesomeIcon.Star.ToIconString();
                        var starSize = ImGui.CalcTextSize(starStr);
                        ImGui.PopFont();
                        drawList.AddText(UiBuilder.IconFont, 12f,
                            buttonMin + new Vector2(4, (ImGui.GetItemRectSize().Y - starSize.Y) / 2 + 1),
                            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.85f, 0f, 1f)),
                            starStr);
                    }

                    if (isSelected)
                        ImGui.PopStyleColor();
                }
            }
            ImGui.EndChild();

            ImGui.SameLine();

            ImGui.BeginGroup();

            ImGui.Text("Search:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##search", "Type to filter icons...", ref _searchFilter, 50);

            ImGui.Separator();
            ImGui.Spacing();

            var remainingHeight = ImGui.GetContentRegionAvail().Y - buttonHeight - padding * 2;
            if (ImGui.BeginChild("IconGrid", new Vector2(-1, remainingHeight), true))
            {
                DrawClassicIconGrid();
            }
            ImGui.EndChild();

            ImGui.EndGroup();

            ImGui.Separator();

            ImGui.Text("Selected:");
            ImGui.SameLine();
            if (SelectedIcon.HasValue)
            {
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.85f, 0.0f, 1.0f));
                ImGui.Text(SelectedIcon.Value.ToIconString());
                ImGui.PopStyleColor();
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.Text($"({SelectedIcon.Value})");
            }
            else
            {
                ImGui.Text("None");
            }

            ImGui.SameLine(windowSize.X - 160);

            if (ImGui.Button("Cancel", new Vector2(70, 0)))
            {
                SelectedIcon = _initialIcon;
                if (_initialIcon.HasValue)
                    OnIconChanged?.Invoke(_initialIcon.Value);
                Confirmed = false;
                IsOpen = false;
            }

            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.5f, 0.3f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.6f, 0.4f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.7f, 0.5f, 1.0f));
            if (ImGui.Button("Confirm", new Vector2(70, 0)))
            {
                Confirmed = true;
                IsOpen = false;
            }
            ImGui.PopStyleColor(3);
        }

        private void DrawClassicIconGrid()
{
            var searchLower = _searchFilter.ToLowerInvariant();

            FontAwesomeIcon[] icons;
            if (_selectedCategory == "Favorites")
            {
                icons = GetFavoriteIcons();
                if (icons.Length == 0)
                {
                    ImGui.TextDisabled("No favorite icons yet.");
                    ImGui.TextDisabled("Right-click any icon to add it to favorites.");
                    return;
                }
            }
            else if (!IconCategories.TryGetValue(_selectedCategory, out icons!))
            {
                return;
            }

            var filteredIcons = icons.Where(icon =>
                string.IsNullOrEmpty(searchLower) ||
                icon.ToString().ToLowerInvariant().Contains(searchLower)
            ).ToList();

            if (filteredIcons.Count == 0)
            {
                ImGui.TextDisabled("No icons match your search.");
                return;
            }

            var availableWidth = ImGui.GetContentRegionAvail().X;
            var iconSize = 36f;
            var spacing = 6f;
            var cellSize = iconSize + spacing;
            var iconsPerRow = Math.Max(1, (int)Math.Floor(availableWidth / cellSize));

            var drawList = ImGui.GetWindowDrawList();
            var startPos = ImGui.GetCursorScreenPos();

            for (int i = 0; i < filteredIcons.Count; i++)
            {
                int col = i % iconsPerRow;
                int row = i / iconsPerRow;

                var cellPos = startPos + new Vector2(col * cellSize, row * cellSize);
                var cellMin = cellPos;
                var cellMax = cellPos + new Vector2(iconSize, iconSize);

                var icon = filteredIcons[i];
                bool isHovered = ImGui.IsMouseHoveringRect(cellMin, cellMax);
                bool isSelected = SelectedIcon == icon;
                bool isFavorite = IsIconFavorite(icon);
                bool leftClicked = isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
                bool rightClicked = isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right);

                var bgColor = isSelected
                    ? new Vector4(0.3f, 0.5f, 0.7f, 0.8f)
                    : isHovered
                        ? new Vector4(0.3f, 0.3f, 0.4f, 0.6f)
                        : new Vector4(0.15f, 0.15f, 0.2f, 0.4f);

                drawList.AddRectFilled(cellMin, cellMax, ImGui.ColorConvertFloat4ToU32(bgColor), 4f);

                if (isSelected || isHovered)
                {
                    var borderColor = isSelected
                        ? new Vector4(0.5f, 0.7f, 1.0f, 1.0f)
                        : new Vector4(0.5f, 0.5f, 0.6f, 0.8f);
                    drawList.AddRect(cellMin, cellMax, ImGui.ColorConvertFloat4ToU32(borderColor), 4f, ImDrawFlags.None, 1.5f);
                }

                if (isFavorite)
                {
                    var starPos = cellMin + new Vector2(iconSize - 10, 2);
                    drawList.AddText(UiBuilder.IconFont, 10f, starPos,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.85f, 0f, 0.9f)),
                        FontAwesomeIcon.Star.ToIconString());
                }

                ImGui.PushFont(UiBuilder.IconFont);
                var iconStr = icon.ToIconString();
                var textSize = ImGui.CalcTextSize(iconStr);
                var textPos = cellMin + new Vector2((iconSize - textSize.X) / 2, (iconSize - textSize.Y) / 2);

                var iconColor = isSelected
                    ? new Vector4(1.0f, 0.9f, 0.5f, 1.0f)
                    : new Vector4(0.9f, 0.9f, 0.9f, 1.0f);
                drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(iconColor), iconStr);
                ImGui.PopFont();

                if (leftClicked)
                {
                    SelectedIcon = icon;
                    OnIconChanged?.Invoke(icon);

                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (_lastClickTime > 0 && now - _lastClickTime < 300)
                    {
                        Confirmed = true;
                        IsOpen = false;
                    }
                    _lastClickTime = now;
                }

                if (rightClicked)
                    ToggleFavorite(icon);

                if (isHovered)
                {
                    ImGui.BeginTooltip();
                    ImGui.Text(icon.ToString());
                    ImGui.TextDisabled("Left-click to select, double-click to confirm");
                    if (isFavorite)
                        ImGui.TextColored(new Vector4(1f, 0.85f, 0f, 1f), "Right-click to remove from favorites");
                    else
                        ImGui.TextDisabled("Right-click to add to favorites");
                    ImGui.EndTooltip();
                }
            }

            int totalRows = (filteredIcons.Count + iconsPerRow - 1) / iconsPerRow;
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + totalRows * cellSize);
        }

    }
}

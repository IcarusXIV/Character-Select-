using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace CharacterSelectPlugin.Windows.Utils
{
    /// <summary>
    /// Searchable combo box using standard ImGui patterns.
    /// </summary>
    public static class AutocompleteCombo
    {
        private static readonly Dictionary<string, string> _filterTexts = new();

        public static bool Draw(
            string id,
            ref string value,
            IReadOnlyList<string> options,
            float width,
            string placeholder = "Select...",
            int maxVisibleItems = 8,
            string? currentActive = null)
        {
            bool valueChanged = false;
            var scale = ImGuiHelpers.GlobalScale;

            // Ensure filter text exists for this id
            if (!_filterTexts.ContainsKey(id))
                _filterTexts[id] = "";

            ImGui.SetNextItemWidth(width);

            // Show current value or placeholder in the combo preview
            var previewValue = string.IsNullOrEmpty(value) ? placeholder : value;

            // Constrain popup width to match combo button
            ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, 400 * scale));

            var comboOpen = ImGui.BeginCombo($"##{id}", previewValue);

            // Right-click to clear
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && !string.IsNullOrEmpty(value))
            {
                value = "";
                valueChanged = true;
            }

            if (comboOpen)
            {
                // Style the popup
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8 * scale, 6 * scale));
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8 * scale, 4 * scale));
                ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.26f, 0.59f, 0.98f, 0.4f));
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.26f, 0.59f, 0.98f, 0.6f));

                // Filter input at top of dropdown
                ImGui.SetNextItemWidth(-1);
                var filterText = _filterTexts[id];
                if (ImGui.InputTextWithHint($"##{id}_filter", "Search...", ref filterText, 256))
                {
                    _filterTexts[id] = filterText;
                }

                // Keep focus on filter input when combo opens
                if (ImGui.IsWindowAppearing())
                {
                    ImGui.SetKeyboardFocusHere(-1);
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                // "None" option to clear selection
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.55f, 1.0f));
                if (ImGui.Selectable("(None)", string.IsNullOrEmpty(value)))
                {
                    value = "";
                    valueChanged = true;
                    _filterTexts[id] = "";
                    ImGui.CloseCurrentPopup();
                }
                ImGui.PopStyleColor();

                ImGui.Separator();

                // Filter options
                var searchTerm = filterText.ToLowerInvariant();
                var filteredOptions = string.IsNullOrEmpty(searchTerm)
                    ? options.ToList()
                    : options.Where(o => o.ToLowerInvariant().Contains(searchTerm)).ToList();

                // Move current active to top if present
                int currentActiveIndex = -1;
                if (!string.IsNullOrEmpty(currentActive))
                {
                    var activeIdx = filteredOptions.FindIndex(o => o.Equals(currentActive, StringComparison.OrdinalIgnoreCase));
                    if (activeIdx > 0)
                    {
                        var activeItem = filteredOptions[activeIdx];
                        filteredOptions.RemoveAt(activeIdx);
                        filteredOptions.Insert(0, activeItem);
                        currentActiveIndex = 0;
                    }
                    else if (activeIdx == 0)
                    {
                        currentActiveIndex = 0;
                    }
                }

                // List items - combo handles scrolling natively
                for (int i = 0; i < filteredOptions.Count; i++)
                {
                    var option = filteredOptions[i];
                    var isCurrentActive = i == currentActiveIndex;
                    var isSelected = option.Equals(value, StringComparison.OrdinalIgnoreCase);

                    // Green color for currently active
                    if (isCurrentActive)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 1.0f, 0.5f, 1.0f));
                    }

                    var displayText = isCurrentActive ? $"● {option}" : option;

                    if (ImGui.Selectable(displayText, isSelected))
                    {
                        value = option;
                        valueChanged = true;
                        _filterTexts[id] = "";
                        ImGui.CloseCurrentPopup();
                    }

                    if (isCurrentActive)
                    {
                        ImGui.PopStyleColor();
                    }
                }

                if (filteredOptions.Count == 0)
                {
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.55f, 1.0f), "No matches");
                }

                ImGui.PopStyleColor(2); // Header, HeaderHovered
                ImGui.PopStyleVar(2);   // ItemSpacing, FramePadding
                ImGui.EndCombo();
            }
            else
            {
                // Combo is closed, clear filter for next open
                _filterTexts[id] = "";
            }

            return valueChanged;
        }

        public static void ClearState(string id) => _filterTexts.Remove(id);
        public static void ClearAllStates() => _filterTexts.Clear();
    }
}

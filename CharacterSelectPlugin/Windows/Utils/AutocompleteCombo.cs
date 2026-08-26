using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin.Windows.Utils
{
    /// <summary>
    /// Hybrid input + chevron-button combo with type-to-filter dropdown.
    /// Boutique-styled: 30px input pill + 28px chevron, popup with 2px gold
    /// top binding, items with hover gold-at-10% bg and selected-item gold
    /// left bar in gold-warm ink.
    /// </summary>
    public static class AutocompleteCombo
    {
        private static readonly Dictionary<string, string> _filterTexts = new();
        private static readonly Dictionary<string, bool> _focusFilterOnNext = new();

        // Plain ImGui combo for classic mode. allowCustomInput picks between an
        // editable InputText plus arrow popup and a plain BeginCombo.
        private static bool DrawClassicCombo(string id, ref string value, IReadOnlyList<string> options,
            float width, string placeholder, bool allowCustomInput, string? currentActive = null)
        {
            bool changed = false;
            if (!string.IsNullOrEmpty(currentActive))
                options = options.OrderByDescending(o => o.Equals(currentActive, StringComparison.OrdinalIgnoreCase)).ToList();

            if (allowCustomInput)
            {
                float arrowW = ImGui.GetFrameHeight();
                float spacing = ImGui.GetStyle().ItemInnerSpacing.X;
                float inputW = width - arrowW - spacing;
                if (inputW < 40f) inputW = 40f;

                ImGui.SetNextItemWidth(inputW);
                if (ImGui.InputTextWithHint($"{id}_input", placeholder, ref value, 200))
                    changed = true;

                ImGui.SameLine(0, spacing);
                string popupId = $"{id}_popup";
                if (ImGui.ArrowButton($"{id}_btn", ImGuiDir.Down))
                {
                    ImGui.OpenPopup(popupId);
                    _focusFilterOnNext[id] = true;
                    _filterTexts[id] = "";
                }

                ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, 280));

                if (ImGui.BeginPopup(popupId))
                {
                    string filter = _filterTexts.GetValueOrDefault(id, "");
                    ImGui.SetNextItemWidth(width - 16);
                    if (ImGui.InputTextWithHint($"##classic_filter_{id}", "Search...", ref filter, 256))
                        _filterTexts[id] = filter;

                    if (_focusFilterOnNext.GetValueOrDefault(id, false))
                    {
                        ImGui.SetKeyboardFocusHere(-1);
                        _focusFilterOnNext[id] = false;
                    }

                    ImGui.Separator();

                    string lower = filter.ToLowerInvariant().Trim();
                    if (ImGui.BeginChild($"##classic_list_{id}", new Vector2(width - 16, 200), false))
                    {
                        if (ImGui.Selectable("(None)", string.IsNullOrEmpty(value)))
                        {
                            value = "";
                            changed = true;
                            _filterTexts[id] = "";
                            ImGui.CloseCurrentPopup();
                        }

                        foreach (var opt in options)
                        {
                            if (!string.IsNullOrEmpty(lower) && !opt.ToLowerInvariant().Contains(lower)) continue;
                            bool isSel = opt.Equals(value, StringComparison.OrdinalIgnoreCase);
                            bool isActive = opt.Equals(currentActive, StringComparison.OrdinalIgnoreCase);
                            if (isActive) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.9f, 0.2f, 1f));
                            if (ImGui.Selectable(isActive ? $"{opt} (active)##{opt}" : opt, isSel))
                            {
                                value = opt;
                                changed = true;
                                _filterTexts[id] = "";
                                ImGui.CloseCurrentPopup();
                            }
                            if (isActive) ImGui.PopStyleColor();
                        }
                    }
                    ImGui.EndChild();
                    ImGui.EndPopup();
                }
            }
            else
            {
                ImGui.SetNextItemWidth(width);
                string display = string.IsNullOrEmpty(value) ? placeholder : value;
                if (ImGui.BeginCombo(id, display))
                {
                    if (ImGui.Selectable("(None)", string.IsNullOrEmpty(value)))
                    {
                        value = "";
                        changed = true;
                    }
                    foreach (var opt in options)
                    {
                        bool isSel = opt.Equals(value, StringComparison.OrdinalIgnoreCase);
                        bool isActive = opt.Equals(currentActive, StringComparison.OrdinalIgnoreCase);
                        if (isActive) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.9f, 0.2f, 1f));
                        if (ImGui.Selectable(isActive ? $"{opt} (active)##{opt}" : opt, isSel))
                        {
                            value = opt;
                            changed = true;
                        }
                        if (isActive) ImGui.PopStyleColor();
                        if (isSel) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
            }
            return changed;
        }

        public static bool Draw(
            string id,
            ref string value,
            IReadOnlyList<string> options,
            float width,
            string placeholder = "Select...",
            int maxVisibleItems = 8,
            string? currentActive = null,
            bool allowCustomInput = true)
        {
            if (Plugin.UseClassicLayout)
                return DrawClassicCombo(id, ref value, options, width, placeholder, allowCustomInput, currentActive);

            bool valueChanged = false;
            float fs = Boutique.FormScale;

            if (!_filterTexts.ContainsKey(id))
                _filterTexts[id] = "";

            // Layout: combo trigger pill (input area + chev). Height follows
            // PushFormStyle's FramePadding so combos sit at the SAME height as
            // text inputs side-by-side. Chev is 22*fs wide, square-ish, smaller
            // than before (was 28*fs which felt clunky).
            float h = ImGui.GetFrameHeight();
            float chevW = 22f * fs;
            if (width <= chevW + 24f) width = chevW + 24f;
            float inputW = width - chevW;

            var pos = ImGui.GetCursorScreenPos();
            var inputMin = pos;
            var inputMax = inputMin + new Vector2(inputW, h);
            var chevMin = new Vector2(inputMax.X, inputMin.Y);
            var chevMax = chevMin + new Vector2(chevW, h);

            string popupId = $"##bcombo_pop_{id}";
            bool wasOpen = ImGui.IsPopupOpen(popupId);

            // Single InvisibleButton for the whole combo trigger; chev is detected
            // via mouse X. Cursor advances naturally.
            bool clicked = ImGui.InvisibleButton($"##bcombo_trig_{id}", new Vector2(width, h));
            bool hovered = ImGui.IsItemHovered();
            bool rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);

            bool overChev = hovered && ImGui.GetMousePos().X >= chevMin.X;
            bool overInput = hovered && !overChev;

            if (clicked && !wasOpen)
            {
                ImGui.OpenPopup(popupId);
                _focusFilterOnNext[id] = true;
            }
            if (rightClicked && !string.IsNullOrEmpty(value))
            {
                value = "";
                valueChanged = true;
            }

            // ── Paint the trigger pill ──
            // Read FrameBg / FrameBgHovered / FrameBgActive from the active style
            // so callers can lift the trigger off its host bg by pushing those
            // colours, without forking the primitive. PushFormStyle pushes the
            // dark velvet default; the design panel form layers a lighter
            // Surface2 family on top.
            var dl = ImGui.GetWindowDrawList();
            var styleColors = ImGui.GetStyle().Colors;
            Vector4 bg     = styleColors[(int)ImGuiCol.FrameBg];
            Vector4 hoverC = styleColors[(int)ImGuiCol.FrameBgHovered];
            Vector4 focusC = styleColors[(int)ImGuiCol.FrameBgActive];

            Vector4 inputBg = wasOpen ? focusC : (overInput ? hoverC : bg);
            Vector4 chevBg  = wasOpen
                ? Boutique.WithAlpha(Boutique.Gold, 0.10f)
                : (overChev ? hoverC : bg);
            Vector4 borderC = wasOpen
                ? Boutique.GoldDeep
                : (hovered ? Boutique.Border : Boutique.BorderSoft);

            // Single unified pill, same bg across the whole trigger, no internal
            // divider hairline. The chev gets a subtle hover/open state via the
            // arrow colour, not a separate background segment, so the trigger
            // reads as ONE widget rather than "input + button" stuck together.
            Vector4 unifiedBg = wasOpen ? focusC : (hovered ? hoverC : bg);
            dl.AddRectFilled(inputMin, chevMax, Boutique.U32(unifiedBg));
            dl.AddRect(inputMin, chevMax,
                Boutique.U32(borderC), 0f, ImDrawFlags.None, 1f * fs);

            // Inset gold focus glow
            if (wasOpen)
            {
                dl.AddRect(inputMin + new Vector2(1f * fs, 1f * fs),
                           chevMax  - new Vector2(1f * fs, 1f * fs),
                           Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.18f)),
                           0f, ImDrawFlags.None, 1f * fs);
            }

            // Display text
            string display = string.IsNullOrEmpty(value) ? placeholder : value;
            Vector4 textCol = string.IsNullOrEmpty(value) ? Boutique.TextFaint : Boutique.Text;
            float fontH = ImGui.GetFontSize();
            dl.PushClipRect(new Vector2(inputMin.X + 4f * fs, inputMin.Y),
                            new Vector2(inputMax.X - 4f * fs, inputMax.Y), true);
            dl.AddText(new Vector2(inputMin.X + 10f * fs, inputMin.Y + (h - fontH) * 0.5f),
                       Boutique.U32(textCol), display);
            dl.PopClipRect();

            // Chev arrow
            var arrowMid = new Vector2(chevMin.X + chevW * 0.5f, chevMin.Y + h * 0.5f);
            Vector4 arrowCol = wasOpen ? Boutique.Gold
                              : (overChev ? Boutique.GoldWarm : Boutique.TextDim);
            float arr = 4f * fs;
            if (wasOpen)
            {
                dl.AddTriangleFilled(
                    arrowMid + new Vector2(-arr, 1.5f * fs),
                    arrowMid + new Vector2( arr, 1.5f * fs),
                    arrowMid + new Vector2(0f, -2.5f * fs),
                    Boutique.U32(arrowCol));
            }
            else
            {
                dl.AddTriangleFilled(
                    arrowMid + new Vector2(-arr, -1.5f * fs),
                    arrowMid + new Vector2( arr, -1.5f * fs),
                    arrowMid + new Vector2(0f,  2.5f * fs),
                    Boutique.U32(arrowCol));
            }

            // ── Popup ──
            ImGui.SetNextWindowPos(new Vector2(inputMin.X, inputMax.Y + 2f * fs));
            ImGui.SetNextWindowSize(new Vector2(width, 0));
            ImGui.SetNextWindowSizeConstraints(
                new Vector2(width, 0),
                new Vector2(width, 240f * fs));

            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.07f, 0.08f, 0.11f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.Border,  Boutique.WithAlpha(Boutique.Gold, 0.55f));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(20f / 255f, 24f / 255f, 32f / 255f, 0.65f));
            ImGui.PushStyleColor(ImGuiCol.Text,    Boutique.Text);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0f, 0f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f * fs);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 0f));

            if (ImGui.BeginPopup(popupId, ImGuiWindowFlags.NoMove))
            {
                var pdl = ImGui.GetWindowDrawList();
                var pPos = ImGui.GetWindowPos();
                var pSize = ImGui.GetWindowSize();

                // Stripped the previous gold top binding + halo, they were
                // loud for what is just a list dropdown. Now: clean dark popup
                // with the existing gold-tinted border (set via PushStyleColor
                // ImGuiCol.Border above) and minimal top padding.
                ImGui.Dummy(new Vector2(0, 4f * fs));

                // Filter input
                ImGui.SetCursorPosX(8f * fs);
                ImGui.SetNextItemWidth(width - 16f * fs);
                string filterText = _filterTexts[id];
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f * fs);
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f * fs, 5f * fs));
                ImGui.PushStyleColor(ImGuiCol.Border, Boutique.WithAlpha(Boutique.GoldDeep, 0.45f));
                ImGui.PushStyleColor(ImGuiCol.Text, Boutique.InputText);
                ImGui.PushStyleColor(ImGuiCol.TextDisabled, Boutique.InputPlaceholder);
                bool filterEnter = ImGui.InputTextWithHint($"##bcombo_filt_{id}",
                    allowCustomInput ? "Search or type custom..." : "Search...",
                    ref filterText, 256,
                    allowCustomInput
                        ? ImGuiInputTextFlags.EnterReturnsTrue
                        : ImGuiInputTextFlags.None);
                ImGui.PopStyleColor(3);
                ImGui.PopStyleVar(2);

                if (_focusFilterOnNext.TryGetValue(id, out bool wantsFocus) && wantsFocus)
                {
                    ImGui.SetKeyboardFocusHere(-1);
                    _focusFilterOnNext[id] = false;
                }

                if (filterEnter && allowCustomInput && !string.IsNullOrWhiteSpace(filterText))
                {
                    value = filterText;
                    valueChanged = true;
                    _filterTexts[id] = "";
                    ImGui.CloseCurrentPopup();
                }
                else if (filterText != _filterTexts[id])
                {
                    _filterTexts[id] = filterText;
                }

                ImGui.Dummy(new Vector2(0, 6f * fs));

                // Hairline separator
                {
                    var sepY = ImGui.GetCursorScreenPos().Y;
                    pdl.AddLine(new Vector2(pPos.X + 6f * fs, sepY),
                                new Vector2(pPos.X + pSize.X - 6f * fs, sepY),
                                Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.45f)),
                                1f);
                }
                ImGui.Dummy(new Vector2(0, 3f * fs));

                // Filtered options
                string searchTerm = filterText.ToLowerInvariant().Trim();
                List<string> filteredOptions = string.IsNullOrEmpty(searchTerm)
                    ? options.ToList()
                    : options.Where(o => o.ToLowerInvariant().Contains(searchTerm)).ToList();

                bool hasExactMatch = filteredOptions.Any(o =>
                    o.Equals(filterText, StringComparison.OrdinalIgnoreCase));

                // Move current-active option to the top
                if (!string.IsNullOrEmpty(currentActive))
                {
                    int idx = filteredOptions.FindIndex(o =>
                        o.Equals(currentActive, StringComparison.OrdinalIgnoreCase));
                    if (idx > 0)
                    {
                        var item = filteredOptions[idx];
                        filteredOptions.RemoveAt(idx);
                        filteredOptions.Insert(0, item);
                    }
                }

                // Scrollable item list
                float maxListH = 200f * fs;
                ImGui.BeginChild($"##bcombo_list_{id}",
                    new Vector2(0, MathF.Min(maxListH, (filteredOptions.Count + 2) * 28f * fs + 12f * fs)),
                    false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysUseWindowPadding);

                // "Use: ..." custom-input hint
                if (allowCustomInput && !string.IsNullOrEmpty(filterText) && !hasExactMatch)
                {
                    if (DrawComboItem($"Use: \"{filterText}\" (Enter)", null,
                                       false, isCustomHint: true, isCurrent: false, fs))
                    {
                        value = filterText;
                        valueChanged = true;
                        _filterTexts[id] = "";
                        ImGui.CloseCurrentPopup();
                    }
                }

                // (None) clear option
                if (DrawComboItem("(None)", null,
                                   selected: string.IsNullOrEmpty(value),
                                   isCustomHint: false,
                                   isCurrent: false, fs))
                {
                    value = "";
                    valueChanged = true;
                    _filterTexts[id] = "";
                    ImGui.CloseCurrentPopup();
                }

                if (filteredOptions.Count == 0)
                {
                    ImGui.SetCursorPosX(12f * fs);
                    ImGui.PushStyleColor(ImGuiCol.Text, Boutique.TextFaint);
                    ImGui.TextUnformatted("No matches");
                    ImGui.PopStyleColor();
                    ImGui.Dummy(new Vector2(0, 4f * fs));
                }
                else
                {
                    foreach (var opt in filteredOptions)
                    {
                        bool isSel = opt.Equals(value, StringComparison.OrdinalIgnoreCase);
                        bool isCur = !string.IsNullOrEmpty(currentActive)
                                     && opt.Equals(currentActive, StringComparison.OrdinalIgnoreCase);

                        if (DrawComboItem(opt, isCur ? "ACTIVE" : null,
                                           selected: isSel,
                                           isCustomHint: false,
                                           isCurrent: isCur, fs))
                        {
                            value = opt;
                            valueChanged = true;
                            _filterTexts[id] = "";
                            ImGui.CloseCurrentPopup();
                        }
                    }
                }

                ImGui.EndChild();
                ImGui.EndPopup();
            }
            else
            {
                // Popup not rendering, clear filter so next open starts clean
                _filterTexts[id] = "";
                _focusFilterOnNext[id] = false;
            }

            ImGui.PopStyleVar(4);
            ImGui.PopStyleColor(4);

            return valueChanged;
        }

        /// <summary>
        /// One row in the boutique combo popup. Handles the hover gold-at-10%
        /// background, selected-item gold left bar + halo, and "current-active"
        /// green pip. Returns true on click.
        /// </summary>
        private static bool DrawComboItem(string label, string? meta,
            bool selected, bool isCustomHint, bool isCurrent, float fs)
        {
            // Tighter row height, was 28*fs, now matches the body font's height
            // plus 6px each side. Less wasted vertical space in the popup.
            float h = ImGui.GetFontSize() + 8f * fs;
            var pos = ImGui.GetCursorScreenPos();
            float availW = ImGui.GetContentRegionAvail().X;
            var max = pos + new Vector2(availW, h);

            // Stable per-row id (label + cursor Y)
            string rid = $"##bcombo_row_{label}_{(int)pos.Y}";
            bool clicked = ImGui.InvisibleButton(rid, new Vector2(availW, h));
            bool hovered = ImGui.IsItemHovered();

            var dl = ImGui.GetWindowDrawList();

            if (selected)
            {
                // Subtle gold-tinted bg + 2px gold left bar. No multi-layer halo
                // (it was overdone for a list item), just a clean accent.
                dl.AddRectFilled(pos, max,
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.08f)));
                dl.AddRectFilled(
                    new Vector2(pos.X, pos.Y),
                    new Vector2(pos.X + 2f * fs, max.Y),
                    Boutique.U32(Boutique.Gold));
            }
            else if (hovered)
            {
                dl.AddRectFilled(pos, max,
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.10f)));
            }

            // Active-pip (currently active in plugin), small green dot at the left
            float textX = pos.X + 12f * fs;
            if (isCurrent && !selected)
            {
                dl.AddCircleFilled(
                    new Vector2(pos.X + 7f * fs, pos.Y + h * 0.5f),
                    2.5f * fs,
                    Boutique.U32(Boutique.Green));
                textX = pos.X + 16f * fs;
            }
            else if (isCurrent && selected)
            {
                textX = pos.X + 14f * fs;
            }

            // Label
            Vector4 textCol = isCustomHint ? Boutique.GreenSoft
                            : selected     ? Boutique.GoldWarm
                            : hovered      ? Boutique.Text
                            : Boutique.Text;
            float fontH = ImGui.GetFontSize();

            float metaPad = 0f;
            if (!string.IsNullOrEmpty(meta))
            {
                var ms = ImGui.CalcTextSize(meta);
                metaPad = ms.X + 16f * fs;
            }

            dl.PushClipRect(new Vector2(textX, pos.Y),
                            new Vector2(max.X - 8f * fs - metaPad, max.Y), true);
            dl.AddText(new Vector2(textX, pos.Y + (h - fontH) * 0.5f),
                       Boutique.U32(textCol), label);
            dl.PopClipRect();

            // Meta on the right
            if (!string.IsNullOrEmpty(meta))
            {
                var ms = ImGui.CalcTextSize(meta);
                dl.AddText(new Vector2(max.X - 12f * fs - ms.X, pos.Y + (h - fontH) * 0.5f),
                           Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.85f)),
                           meta);
            }

            return clicked;
        }

        public static void ClearState(string id)
        {
            _filterTexts.Remove(id);
            _focusFilterOnNext.Remove(id);
        }

        public static void ClearAllStates()
        {
            _filterTexts.Clear();
            _focusFilterOnNext.Clear();
        }
    }
}

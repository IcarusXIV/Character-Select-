using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace CharacterSelectPlugin.Windows.Styles;

/// <summary>Boutique settings primitives: toggles, sliders, dropdowns, callouts, rows. Each helper is keyed off a stable id.</summary>
public static partial class Boutique
{
    // Row state
    public enum SettingRowState
    {
        Default,
        Dirty,
        Favourite,
    }

    public enum CalloutKind
    {
        Info,
        Warning,
        Danger,
    }

    public enum ListMarker
    {
        DiamondGold,
        DiamondLatest,
        ColourSquare,
    }

    // Per-id animation state for toggle knob slide and reset-glyph hover fade
    private static readonly Dictionary<string, float> _toggleAnimT = new();
    private static readonly Dictionary<string, bool> _toggleAnimLastTarget = new();
    private static readonly Dictionary<string, float> _rowHoverT = new();
    private static readonly Dictionary<string, float> _markerPulseSeed = new();

    /// <summary>
    /// Settings row: pip + label + desc on the left, caller's widget on the
    /// right. drawCtrl runs with cursor positioned at the widget anchor.
    /// </summary>
    public static void SettingRow(string id, string label, string desc,
        float widgetWidth, float scale, Action drawCtrl,
        SettingRowState state = SettingRowState.Default,
        Action? onResetClick = null,
        bool subOption = false)
    {
        // Tighter padding and DYNAMIC row height (grows to fit a wrapped desc
        // instead of clipping to a fixed 50 px).
        float padX = 10f * scale;
        float padY = 9f * scale;
        float pipW = 5f * scale;
        float pipGap = 10f * scale;
        float resetW = 18f * scale;
        float resetGap = 4f * scale;
        float labelToDescGap = 8f * scale;

        var origin = ImGui.GetCursorScreenPos();
        float availW = ImGui.GetContentRegionAvail().X;

        // Pre-measure label + desc heights so the row sizes itself.
        // Label wraps just like the description so long labels don't overlap
        // the widget on narrow chassis or large UI scale.
        float labelLineH;
        using (Plugin.Instance?.OutfitMed13?.Push())
            labelLineH = ImGui.GetFontSize();

        float infoX = origin.X + padX + pipW + pipGap;
        float widgetAnchorX = origin.X + availW - padX - widgetWidth;
        if (onResetClick != null) widgetAnchorX -= resetW + resetGap;
        float infoMaxX = widgetAnchorX - 12f * scale;
        float infoW = MathF.Max(0f, infoMaxX - infoX);

        float labelH = labelLineH;
        if (!string.IsNullOrEmpty(label) && infoW > 0f)
        {
            using (Plugin.Instance?.OutfitMed13?.Push())
            {
                var sz = ImGui.CalcTextSize(label, false, infoW);
                labelH = MathF.Max(labelLineH, sz.Y);
            }
        }

        float descH = 0f;
        if (!string.IsNullOrEmpty(desc))
        {
            using (Plugin.Instance?.OutfitBody13?.Push())
            {
                var sz = ImGui.CalcTextSize(desc, false, infoW);
                descH = sz.Y;
            }
        }
        float rowH = MathF.Max(46f * scale, padY * 2 + labelH + labelToDescGap + descH);

        var rowMin = origin;
        var rowMax = origin + new Vector2(availW, rowH);
        var dl = ImGui.GetWindowDrawList();

        bool rowHovered = ImGui.IsMouseHoveringRect(rowMin, rowMax, true)
                          && !ImGui.IsAnyItemActive();
        float dt = ImGui.GetIO().DeltaTime;
        _rowHoverT.TryGetValue(id, out float hoverT);
        hoverT = rowHovered
            ? MathF.Min(1f, hoverT + dt / 0.15f)
            : MathF.Max(0f, hoverT - dt / 0.15f);
        _rowHoverT[id] = hoverT;

        if (hoverT > 0.01f)
            dl.AddRectFilled(rowMin, rowMax, U32(WithAlpha(Gold, 0.025f * hoverT)));

        dl.AddLine(new Vector2(rowMin.X, rowMin.Y),
                   new Vector2(rowMax.X, rowMin.Y),
                   U32(new Vector4(1f, 1f, 1f, 0.025f)), 1f);

        // Status pip with a slow breathing pulse so the rows feel alive
        // instead of staring back at the user. Aligned with the FIRST line
        // of the label (not the centre of the wrapped block).
        float pipY = rowMin.Y + padY + (labelLineH - pipW) * 0.5f;
        var pipMin = new Vector2(rowMin.X + padX, pipY);
        var pipMax = pipMin + new Vector2(pipW, pipW);

        Vector4 pipBaseCol = state switch
        {
            SettingRowState.Dirty => Green,
            SettingRowState.Favourite => GoldWarm,
            _ => GoldDeep,        // soft warm gold instead of dead grey
        };

        // Gentle 2.4s breathe cycle, offset per id so adjacent rows aren't
        // perfectly in sync.
        float t = (float)ImGui.GetTime();
        float idPhase = (id.GetHashCode() & 0x7FFF) / 32768f;
        float pulse = 0.55f + 0.45f * MathF.Sin((t + idPhase * 6.28f) * 2.6f);
        Vector4 pipCol = WithAlpha(pipBaseCol, MathF.Min(1f, pipBaseCol.W * (0.55f + 0.45f * pulse)));

        // Halo padding scales with the pulse so dirty / favourite rows breathe more.
        float gPadBase = (state != SettingRowState.Default) ? 4f * scale : 2.5f * scale;
        float gPad = gPadBase * (0.85f + 0.30f * pulse);
        dl.AddRectFilled(pipMin - new Vector2(gPad, gPad), pipMax + new Vector2(gPad, gPad),
            U32(WithAlpha(pipBaseCol, 0.20f * pulse + (state != SettingRowState.Default ? 0.15f : 0f))));
        dl.AddRectFilled(pipMin, pipMax, U32(pipCol));

        // Sub-option connector: when this row is rendered indented under a
        // parent toggle, draw an L-shaped tether from the parent row's pip
        // column down to this row's pip so the relationship is visible.
        if (subOption)
        {
            float connectorX = rowMin.X + padX - 10f * scale;
            uint connectorCol = U32(WithAlpha(GoldDeep, 0.45f));
            // Vertical leg from row top to pip centre
            dl.AddLine(new Vector2(connectorX, rowMin.Y),
                       new Vector2(connectorX, pipY + pipW * 0.5f),
                       connectorCol, 1f * scale);
            // Horizontal leg into the pip
            dl.AddLine(new Vector2(connectorX, pipY + pipW * 0.5f),
                       new Vector2(pipMin.X - 1f * scale, pipY + pipW * 0.5f),
                       connectorCol, 1f * scale);
        }

        // Label uses ImGui.TextUnformatted with PushTextWrapPos so long labels
        // wrap into the available width instead of overflowing onto the widget.
        // PushTextWrapPos takes a WINDOW-LOCAL X, not a screen X - convert.
        ImGui.SetCursorScreenPos(new Vector2(infoX, rowMin.Y + padY));
        ImGui.PushStyleColor(ImGuiCol.Text, Text);
        using (Plugin.Instance?.OutfitMed13?.Push())
        {
            float wrapLocal = (infoX + infoW) - ImGui.GetWindowPos().X;
            ImGui.PushTextWrapPos(wrapLocal);
            ImGui.TextUnformatted(label);
            ImGui.PopTextWrapPos();
        }
        ImGui.PopStyleColor();

        // Description WRAPS (preserves the full tooltip text the user wrote).
        // Bumped to OutfitBody13 - 12px under the label was too small for the
        // information density the boutique chassis can support.
        // PushTextWrapPos takes a WINDOW-LOCAL X, not a screen X - convert.
        if (!string.IsNullOrEmpty(desc))
        {
            ImGui.SetCursorScreenPos(new Vector2(infoX, rowMin.Y + padY + labelH + labelToDescGap));
            ImGui.PushStyleColor(ImGuiCol.Text, TextDim);
            using (Plugin.Instance?.OutfitBody13?.Push())
            {
                float wrapLocal = (infoX + infoW) - ImGui.GetWindowPos().X;
                ImGui.PushTextWrapPos(wrapLocal);
                ImGui.TextUnformatted(desc);
                ImGui.PopTextWrapPos();
            }
            ImGui.PopStyleColor();
        }

        // Widget aligns with the FIRST line of the label so long wrapped
        // labels don't push the toggle / dropdown down into the description.
        float widgetH = 26f * scale;
        float widgetY = rowMin.Y + padY + (labelLineH - widgetH) * 0.5f;
        if (widgetY < rowMin.Y + padY * 0.5f) widgetY = rowMin.Y + padY * 0.5f;
        ImGui.SetCursorScreenPos(new Vector2(widgetAnchorX, widgetY));
        drawCtrl();

        // Reset glyph (only when hovered + onResetClick provided)
        if (onResetClick != null && hoverT > 0.05f)
        {
            float resetY = rowMin.Y + (rowH - resetW) * 0.5f;
            float resetX = rowMax.X - padX - resetW;
            var rMin = new Vector2(resetX, resetY);
            var rMax = rMin + new Vector2(resetW, resetW);
            ImGui.SetCursorScreenPos(rMin);
            bool rClicked = ImGui.InvisibleButton($"##reset_{id}", new Vector2(resetW, resetW));
            bool rHover = ImGui.IsItemHovered();
            if (rHover) Tooltip("Reset to default");

            string resetGlyph = ""; // FontAwesome sync
            ImGui.PushFont(UiBuilder.IconFont);
            var rNat = ImGui.CalcTextSize(resetGlyph);
            ImGui.PopFont();
            float rRender = 11f * scale;
            float rRatio = rRender / UiBuilder.IconFont.FontSize;
            var rDrawn = rNat * rRatio;
            uint rCol = rHover
                ? U32(GoldWarm)
                : U32(WithAlpha(TextGhost, hoverT));
            dl.AddText(UiBuilder.IconFont, rRender,
                rMin + new Vector2((resetW - rDrawn.X) * 0.5f, (resetW - rDrawn.Y) * 0.5f),
                rCol, resetGlyph);
            if (rClicked) onResetClick();
        }

        // Advance cursor to the bottom of the row
        ImGui.SetCursorScreenPos(new Vector2(origin.X, rowMax.Y));
        ImGui.Dummy(new Vector2(0, 0));
    }

    private static string ClampToWidth(string text, float maxW, float fontH)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var size = ImGui.CalcTextSize(text);
        if (size.X <= maxW) return text;
        // Binary-ish truncate
        string ellipsis = "...";
        var ellSize = ImGui.CalcTextSize(ellipsis);
        float budget = maxW - ellSize.X;
        if (budget < 0) return ellipsis;
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (ImGui.CalcTextSize(text.Substring(0, mid)).X <= budget) lo = mid;
            else hi = mid - 1;
        }
        return text.Substring(0, lo) + ellipsis;
    }

    // ── Toggle pill (replaces ImGui.Checkbox) ───────────────────────────
    /// <summary>
    /// 42x22 px toggle pill. Renders at the current ImGui cursor.
    /// Returns true when the value changed this frame.
    /// </summary>
    public static bool TogglePill(string id, ref bool value, float scale)
    {
        // Slightly larger pill (46x24) with a thin gold inner stroke so the
        // toggle reads with more presence than a flat 42x22 rect.
        float w = 46f * scale;
        float h = 24f * scale;
        float knobS = 18f * scale;
        float knobInset = 2f * scale;

        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(w, h);

        bool clicked = ImGui.InvisibleButton($"##toggle_{id}", new Vector2(w, h));
        bool hovered = ImGui.IsItemHovered();
        bool changed = false;
        if (clicked)
        {
            value = !value;
            changed = true;
        }

        // Animation: knob slides between left/right with cubic-bezier overshoot
        float dt = ImGui.GetIO().DeltaTime;
        _toggleAnimLastTarget.TryGetValue(id, out bool lastTarget);
        if (lastTarget != value)
        {
            // Reset progress on flip
            _toggleAnimT[id] = 0f;
            _toggleAnimLastTarget[id] = value;
        }
        _toggleAnimT.TryGetValue(id, out float animT);
        animT = MathF.Min(1f, animT + dt / 0.22f);
        _toggleAnimT[id] = animT;
        // Overshoot easing (close to cubic-bezier(0.34,1.2,0.42,1))
        float easeOut = 1f - MathF.Pow(1f - animT, 3f);
        float oneMinusT = 1f - animT;
        float overshoot = 0.10f * MathF.Sin(animT * MathF.PI * 2f) * oneMinusT * oneMinusT;
        float eased = MathF.Min(1f, easeOut + overshoot);

        // Background + border
        Vector4 bgCol = value
            ? WithAlpha(Gold, 0.12f)
            : Surface2;
        Vector4 borderCol = value ? GoldDeep : BorderSoft;
        if (hovered) borderCol = value ? Gold : GoldDeep;
        dl.AddRectFilled(min, max, U32(bgCol));
        dl.AddRect(min, max, U32(borderCol), 0f, ImDrawFlags.None, 1f);

        // Knob
        float knobLeftX = min.X + knobInset;
        float knobRightX = max.X - knobS - knobInset;
        float startX = value ? knobLeftX : knobRightX;
        float endX = value ? knobRightX : knobLeftX;
        float knobX = startX + (endX - startX) * eased;
        float knobY = min.Y + (h - knobS) * 0.5f;
        var knobMin = new Vector2(knobX, knobY);
        var knobMax = knobMin + new Vector2(knobS, knobS);

        Vector4 knobCol = value ? Gold : TextFaint;
        if (value)
        {
            // Halo: 3 concentric squares
            for (int g = 3; g >= 1; g--)
            {
                float pad = g * 1.6f * scale;
                uint glowCol = U32(WithAlpha(Gold, 0.20f / g));
                dl.AddRectFilled(knobMin - new Vector2(pad, pad), knobMax + new Vector2(pad, pad), glowCol);
            }
        }
        dl.AddRectFilled(knobMin, knobMax, U32(knobCol));
        // Inner detail: a 2px deeper-gold inset square on the knob when on,
        // off-grey ring when off, gives the toggle a bit more character.
        if (value)
        {
            float inset = 4f * scale;
            dl.AddRect(knobMin + new Vector2(inset, inset),
                       knobMax - new Vector2(inset, inset),
                       U32(GoldDeep), 0f, ImDrawFlags.None, 1f);
        }
        else
        {
            dl.AddRect(knobMin, knobMax, U32(BorderSoft), 0f, ImDrawFlags.None, 1f);
        }

        return changed;
    }

    // ── Slider track (replaces ImGui.SliderFloat) ───────────────────────
    // Per-id state: when user ctrl+clicks the slider we switch into typing
    // mode and show an InputText with the current value. Enter / blur exits
    // typing mode and clamps the parsed value to [min,max].
    private static readonly HashSet<string> _sliderInputMode = new();
    private static readonly Dictionary<string, string> _sliderInputBuf = new();
    private static readonly HashSet<string> _sliderJustEnteredInput = new();

    /// <summary>
    /// Custom-painted slider with gold gradient fill, value readout, and
    /// ctrl+click-to-input parity with ImGui.SliderFloat. Caller passes total
    /// width including the right-side value readout column.
    /// </summary>
    public static bool SliderTrack(string id, ref float value, float min, float max,
        string format, float totalWidth, float scale)
    {
        float h = 22f * scale;
        float valW = 56f * scale;
        float gap = 10f * scale;
        float trackW = totalWidth - valW - gap;
        float trackH = 3f * scale;

        var origin = ImGui.GetCursorScreenPos();

        // ── Input mode (ctrl+click typed value) ─────────────────────────
        if (_sliderInputMode.Contains(id))
        {
            if (!_sliderInputBuf.TryGetValue(id, out string? buf) || buf == null)
            {
                buf = FormatSliderValue(value, format);
                _sliderInputBuf[id] = buf;
            }

            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0x08 / 255f, 0x0A / 255f, 0x0E / 255f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Border, Gold);
            ImGui.PushStyleColor(ImGuiCol.Text, Text);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f * scale, (h - ImGui.GetFontSize()) * 0.5f));

            ImGui.SetNextItemWidth(totalWidth);
            if (_sliderJustEnteredInput.Contains(id))
            {
                ImGui.SetKeyboardFocusHere();
                _sliderJustEnteredInput.Remove(id);
            }
            bool entered = ImGui.InputText($"##slider_input_{id}", ref buf, 16,
                ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
            bool deactivated = ImGui.IsItemDeactivated();
            _sliderInputBuf[id] = buf;

            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor(3);

            bool changedFromInput = false;
            if (entered || deactivated)
            {
                if (float.TryParse(buf, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                {
                    parsed = MathF.Max(min, MathF.Min(max, parsed));
                    if (parsed != value) { value = parsed; changedFromInput = true; }
                }
                _sliderInputMode.Remove(id);
                _sliderInputBuf.Remove(id);
            }
            // Cursor is already past the input by ImGui
            return changedFromInput;
        }

        // ── Normal slider mode ──────────────────────────────────────────
        var dl = ImGui.GetWindowDrawList();
        // Thicker, more visible track (5px instead of 3px so the bar reads
        // immediately rather than disappearing into the background).
        float trackThick = 5f * scale;
        var trackMin = new Vector2(origin.X, origin.Y + (h - trackThick) * 0.5f);
        var trackMax = trackMin + new Vector2(trackW, trackThick);

        var hitMin = new Vector2(origin.X, origin.Y);
        ImGui.SetCursorScreenPos(hitMin);
        ImGui.InvisibleButton($"##slider_{id}", new Vector2(trackW, h));
        bool active = ImGui.IsItemActive();
        bool hovered = ImGui.IsItemHovered();
        bool ctrlClicked = hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                                   && ImGui.GetIO().KeyCtrl;
        if (hovered) Tooltip("Drag to change. Ctrl+click to type a value.");

        bool changed = false;
        if (ctrlClicked)
        {
            _sliderInputMode.Add(id);
            _sliderJustEnteredInput.Add(id);
        }
        else if (active && !ImGui.GetIO().KeyCtrl)
        {
            float mouseX = ImGui.GetIO().MousePos.X;
            float t = MathF.Max(0f, MathF.Min(1f, (mouseX - trackMin.X) / trackW));
            float newVal = min + (max - min) * t;
            // Snap to the format's precision so a slider showing "0.9" stores
            // 0.9 (not 0.91313). Matches the legacy ImGui.SliderFloat
            // behaviour the user referenced. Uses double-precision Round to
            // avoid float-multiplication drift from the dropped earlier
            // attempt.
            newVal = RoundToFormatPrecision(newVal, format);
            newVal = MathF.Max(min, MathF.Min(max, newVal));
            if (newVal != value) { value = newVal; changed = true; }
        }

        // Track background: solid dark with a subtle border for contrast
        dl.AddRectFilled(trackMin, trackMax, U32(new Vector4(0f, 0f, 0f, 0.75f)));
        dl.AddRect(trackMin, trackMax, U32(WithAlpha(BorderSoft, 0.85f)),
            0f, ImDrawFlags.None, 1f);

        // Fill (gold gradient with stronger glow)
        float fillT = (max > min) ? (value - min) / (max - min) : 0f;
        fillT = MathF.Max(0f, MathF.Min(1f, fillT));
        float fillX = trackMin.X + trackW * fillT;
        if (fillT > 0.001f)
        {
            dl.AddRectFilledMultiColor(trackMin,
                new Vector2(fillX, trackMax.Y),
                U32(GoldDeep), U32(GoldWarm), U32(GoldWarm), U32(GoldDeep));
            // Outer glow on the fill so it pops
            for (int g = 2; g >= 1; g--)
            {
                float pad = g * 1.5f * scale;
                dl.AddRectFilled(
                    new Vector2(trackMin.X, trackMin.Y - pad),
                    new Vector2(fillX, trackMax.Y + pad),
                    U32(WithAlpha(Gold, 0.18f / g)));
            }
        }

        // Thumb (slightly bigger, brighter ring on hover)
        float thumbS = 12f * scale;
        var thumbMin = new Vector2(fillX - thumbS * 0.5f,
            trackMin.Y + (trackThick - thumbS) * 0.5f);
        var thumbMax = thumbMin + new Vector2(thumbS, thumbS);
        for (int g = 3; g >= 1; g--)
        {
            float pad = g * 1.6f * scale;
            dl.AddRectFilled(thumbMin - new Vector2(pad, pad), thumbMax + new Vector2(pad, pad),
                U32(WithAlpha(Gold, 0.28f / g)));
        }
        dl.AddRectFilled(thumbMin, thumbMax, U32(Gold));
        dl.AddRect(thumbMin, thumbMax, U32(hovered || active ? GoldBright : GoldDeep),
            0f, ImDrawFlags.None, 1f);

        // Value readout (bumped to OswaldSemi11 for slightly more presence)
        using (Plugin.Instance?.OswaldSemi11?.Push())
        {
            float trackPx = 1.8f * scale;
            string valStr = FormatSliderValue(value, format);
            float valTextW = MeasureTrackedText(valStr, trackPx);
            float fontH = ImGui.GetFontSize();
            float vx = origin.X + trackW + gap + (valW - valTextW);
            float vy = origin.Y + (h - fontH) * 0.5f;
            DrawTrackedText(dl, new Vector2(vx, vy), valStr, U32(GoldWarm), trackPx);
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X + totalWidth, origin.Y));
        return changed;
    }

    /// <summary>Int variant of SliderTrack. Round to int for storage + display.</summary>
    public static bool SliderTrackInt(string id, ref int value, int min, int max,
        float totalWidth, float scale)
    {
        float fval = value;
        bool changed = SliderTrack(id, ref fval, min, max, "%d", totalWidth, scale);
        if (changed)
        {
            int newInt = (int)MathF.Round(fval);
            newInt = Math.Clamp(newInt, min, max);
            if (newInt != value) { value = newInt; return true; }
            return false;
        }
        return false;
    }

    /// <summary>Round a float to the decimal precision implied by a printf-style format ("%.1f", "%d", etc).</summary>
    private static float RoundToFormatPrecision(float value, string format)
    {
        if (string.IsNullOrEmpty(format)) return value;
        int decimals = -1;
        switch (format)
        {
            case "%d": case "%i": case "%.0f": decimals = 0; break;
            case "%.1f": decimals = 1; break;
            case "%.2f": decimals = 2; break;
            case "%.3f": decimals = 3; break;
        }
        if (decimals < 0)
        {
            var m = System.Text.RegularExpressions.Regex.Match(format, "%\\.(\\d+)f");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int parsed)) decimals = parsed;
        }
        if (decimals < 0) return value;
        return (float)Math.Round((double)value, decimals, MidpointRounding.AwayFromZero);
    }

    private static string FormatSliderValue(float value, string format)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (string.IsNullOrEmpty(format)) return value.ToString("0.0", inv);
        // Normalise common C printf patterns
        switch (format)
        {
            case "%d": return ((int)MathF.Round(value)).ToString(inv);
            case "%i": return ((int)MathF.Round(value)).ToString(inv);
            case "%.0f": return value.ToString("0", inv);
            case "%.1f": return value.ToString("0.0", inv);
            case "%.2f": return value.ToString("0.00", inv);
            case "%.3f": return value.ToString("0.000", inv);
            case "%f": return value.ToString("0.000", inv);
        }
        // Fallback: try a regex on "%.<n>f" patterns
        var m = System.Text.RegularExpressions.Regex.Match(format, "%\\.(\\d+)f");
        if (m.Success && int.TryParse(m.Groups[1].Value, out int decimals))
        {
            string spec = "0" + (decimals > 0 ? "." + new string('0', decimals) : "");
            return value.ToString(spec, inv);
        }
        // Generic fallback
        return value.ToString("0.0", inv);
    }

    // ── Boutique-styled CollapsingHeader with a left gold accent stripe ──
    /// <summary>
    /// Wraps ImGui.CollapsingHeader with boutique-tinted Header/HeaderHovered/
    /// HeaderActive colours, GoldWarm tracked-caps text, and (when expanded) a
    /// 2px gold-deep accent stripe on the left edge plus a thin gold hairline
    /// just below the header bar. Click-to-expand behaviour preserved.
    /// </summary>
    public static bool BoutiqueCollapsingHeader(string label, string id, bool defaultOpen, float scale)
    {
        ImGui.PushStyleColor(ImGuiCol.Header, WithAlpha(Gold, 0.08f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, WithAlpha(Gold, 0.14f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, WithAlpha(Gold, 0.22f));
        ImGui.PushStyleColor(ImGuiCol.Text, GoldWarm);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);

        var flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        bool open = ImGui.CollapsingHeader($"{label.ToUpperInvariant()}##{id}", flags);

        // Decorate the header rect: 2px gold-deep stripe on the left edge,
        // gold-fade hairline at the bottom. Drawn after the header to overlay.
        var hMin = ImGui.GetItemRectMin();
        var hMax = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();
        // Left accent stripe (always visible, brighter when expanded)
        dl.AddRectFilled(
            new Vector2(hMin.X, hMin.Y + 2f * scale),
            new Vector2(hMin.X + 2f * scale, hMax.Y - 2f * scale),
            U32(open ? Gold : GoldDeep));
        // Bottom gold-fading hairline
        uint goldMid = U32(WithAlpha(Gold, open ? 0.30f : 0.12f));
        uint goldClear = U32(WithAlpha(Gold, 0f));
        float hairW = (hMax.X - hMin.X) * 0.40f;
        dl.AddRectFilledMultiColor(
            new Vector2(hMin.X, hMax.Y - 1f),
            new Vector2(hMin.X + hairW, hMax.Y),
            goldMid, goldClear, goldClear, goldMid);

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(4);
        return open;
    }

    // ── Sub-section header (mockup .group-head pattern, no card wrap) ──
    /// <summary>
    /// Renders a boutique sub-section header at the current cursor: small
    /// gold-deep diamond glyph + tracked-caps label + optional right-aligned
    /// hint + thin gold hairline below. Used to delineate sub-sections
    /// within a section's content. Advances cursor past the header + hairline.
    /// </summary>
    public static void SubSectionHeader(string label, string? hint, float scale)
    {
        ImGui.Dummy(new Vector2(0, 6f * scale));
        var origin = ImGui.GetCursorScreenPos();
        float availW = ImGui.GetContentRegionAvail().X;
        float headH = 22f * scale;
        float padX = 4f * scale;
        var dl = ImGui.GetWindowDrawList();
        var min = origin;
        var max = origin + new Vector2(availW, headH);
        float midY = (min.Y + max.Y) * 0.5f;

        // Diamond glyph
        float gSize = 6f * scale;
        var gCentre = new Vector2(min.X + padX + gSize * 0.5f, midY);
        DrawDiamondAt(dl, gCentre, gSize * 0.5f, U32(GoldDeep));

        // Cap label
        float labelX = gCentre.X + gSize + 8f * scale;
        if (Plugin.Instance?.OswaldSemi11 != null)
        {
            using (Plugin.Instance.OswaldSemi11.Push())
            {
                float trackPx = 2.6f * scale;
                float fontH = ImGui.GetFontSize();
                DrawTrackedText(dl, new Vector2(labelX, midY - fontH * 0.5f),
                    label.ToUpperInvariant(), U32(GoldWarm), trackPx);
            }
        }

        // Right-aligned hint
        if (!string.IsNullOrEmpty(hint) && Plugin.Instance?.OswaldMed9 != null)
        {
            using (Plugin.Instance.OswaldMed9.Push())
            {
                float trackPx = 2.0f * scale;
                float fontH = ImGui.GetFontSize();
                float hintW = MeasureTrackedText(hint, trackPx);
                DrawTrackedText(dl,
                    new Vector2(max.X - padX - hintW, midY - fontH * 0.5f),
                    hint.ToUpperInvariant(), U32(TextGhost), trackPx);
            }
        }

        // Bottom hairline
        dl.AddLine(new Vector2(min.X + padX, max.Y),
                   new Vector2(max.X - padX, max.Y),
                   U32(WithAlpha(Gold, 0.18f)), 1f);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, max.Y + 4f * scale));
        ImGui.Dummy(new Vector2(0, 0));
    }

    // ── Wardrobe-style sort pill (the proven boutique dropdown pattern) ──
    // Modeled on WardrobeWindow.DrawSortPill: pill with KICKER + value +
    // chevron, themed popup with selected accent stripe + tracked-caps items.
    // No typing, no AutocompleteCombo: just click to open, click to pick.
    private static readonly Dictionary<string, bool> _sortPillOpen = new();
    private static readonly Dictionary<string, Vector2> _sortPillAnchor = new();

    /// <summary>
    /// Renders a sort-style pill at the current cursor and (if open) its
    /// themed popup. Returns the index of the selected option if the user
    /// just picked one this frame, otherwise -1.
    /// </summary>
    public static int SortPill(string id, string kicker, int currentIndex,
        IReadOnlyList<string> options, float width, float scale)
    {
        float h = 26f * scale;
        var origin = ImGui.GetCursorScreenPos();
        var min = origin;
        var max = min + new Vector2(width, h);

        ImGui.SetCursorScreenPos(min);
        ImGui.InvisibleButton($"##sortpill_{id}", new Vector2(width, h));
        bool hovered = ImGui.IsItemHovered();
        bool clicked = ImGui.IsItemClicked();
        bool isOpen = _sortPillOpen.TryGetValue(id, out bool o) && o;

        if (clicked)
        {
            isOpen = !isOpen;
            _sortPillOpen[id] = isOpen;
            if (isOpen) _sortPillAnchor[id] = new Vector2(min.X, max.Y + 4f * scale);
        }

        var dl = ImGui.GetWindowDrawList();
        // Pill bg + border
        dl.AddRectFilled(min, max,
            U32(new Vector4(8f / 255f, 10f / 255f, 14f / 255f, 0.85f)));
        Vector4 borderCol = isOpen ? Gold : (hovered ? GoldDeep : BorderSoft);
        dl.AddRect(min, max, U32(borderCol), 0f, ImDrawFlags.None, 1f * scale);

        // KICKER (left) + value (right-leaning toward chevron)
        float padX = 12f * scale;
        string value = (currentIndex >= 0 && currentIndex < options.Count)
            ? options[currentIndex].ToUpperInvariant() : "";

        if (Plugin.Instance?.OswaldMed9 != null && Plugin.Instance.OswaldMed11 != null)
        {
            using (Plugin.Instance.OswaldMed9.Push())
            {
                float kY = (min.Y + max.Y) * 0.5f - ImGui.GetFontSize() * 0.5f;
                DrawTrackedText(dl, new Vector2(min.X + padX, kY),
                    kicker.ToUpperInvariant(), U32(TextGhost), 2.5f * scale);
            }
            using (Plugin.Instance.OswaldMed11.Push())
            {
                float vY = (min.Y + max.Y) * 0.5f - ImGui.GetFontSize() * 0.5f;
                float trackPx = 1.8f * scale;
                float vW = MeasureTrackedText(value, trackPx);
                float chevronW = 14f * scale;
                float vX = max.X - padX - chevronW - vW;
                DrawTrackedText(dl, new Vector2(vX, vY), value, U32(GoldWarm), trackPx);
            }
        }

        // Chevron at far right
        var chC = (min + max) * 0.5f;
        float chR = 4f * scale;
        dl.AddTriangleFilled(
            new Vector2(max.X - padX - 2f * scale, chC.Y - chR * 0.5f),
            new Vector2(max.X - padX - 2f * scale - chR * 2f, chC.Y - chR * 0.5f),
            new Vector2(max.X - padX - 2f * scale - chR, chC.Y + chR * 0.7f),
            U32(GoldDeep));

        int picked = -1;
        if (isOpen)
        {
            string popupId = $"##sortpop_{id}";
            if (clicked) ImGui.OpenPopup(popupId);
            ImGui.SetNextWindowPos(_sortPillAnchor.TryGetValue(id, out var a) ? a
                : new Vector2(min.X, max.Y + 4f * scale));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(6f / 255f, 7f / 255f, 9f / 255f, 0.97f));
            ImGui.PushStyleColor(ImGuiCol.Border, WithAlpha(GoldDeep, 0.85f));
            ImGui.PushStyleColor(ImGuiCol.Text, Text);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 4 * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 1 * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 0f);

            if (ImGui.BeginPopup(popupId))
            {
                float itemH = 24f * scale;
                float itemPadX = 14f * scale;
                float itemW = width;
                var popupFont = Plugin.Instance?.OswaldMed11;

                // Clamp popup height: hard cap (so even on tall screens we
                // don't render a 30-item wall) plus viewport clamp (so it
                // never falls off the bottom).
                var anchor = _sortPillAnchor.TryGetValue(id, out var aPos)
                    ? aPos : new Vector2(min.X, max.Y + 4f * scale);
                float screenH = ImGui.GetIO().DisplaySize.Y;
                float marginBottom = 16f * scale;
                float maxPopupH = 240f * scale; // ≈10 rows
                float viewportH = MathF.Max(80f * scale, screenH - anchor.Y - marginBottom);
                float availableH = MathF.Min(maxPopupH, viewportH);
                float desiredH = options.Count * itemH + 8f * scale;
                bool needsScroll = desiredH > availableH;
                float listH = needsScroll ? availableH : desiredH;

                if (needsScroll)
                {
                    ImGui.BeginChild($"##sortpop_scroll_{id}",
                        new Vector2(itemW, listH), false,
                        ImGuiWindowFlags.AlwaysVerticalScrollbar);
                }

                for (int i = 0; i < options.Count; i++)
                {
                    bool isSel = i == currentIndex;
                    var rowMn = ImGui.GetCursorScreenPos();
                    var rowMx = new Vector2(rowMn.X + itemW, rowMn.Y + itemH);
                    ImGui.InvisibleButton($"##sortitem_{id}_{i}", new Vector2(itemW, itemH));
                    bool hov = ImGui.IsItemHovered();
                    bool itemClicked = ImGui.IsItemClicked();
                    if (itemClicked)
                    {
                        picked = i;
                        _sortPillOpen[id] = false;
                        ImGui.CloseCurrentPopup();
                    }

                    var pdl = ImGui.GetWindowDrawList();
                    if (isSel)
                    {
                        pdl.AddRectFilled(rowMn, rowMx,
                            U32(WithAlpha(Gold, 0.18f)));
                        pdl.AddRectFilled(rowMn,
                            new Vector2(rowMn.X + 2f * scale, rowMx.Y),
                            U32(Gold));
                    }
                    else if (hov)
                    {
                        pdl.AddRectFilled(rowMn, rowMx,
                            U32(WithAlpha(Gold, 0.10f)));
                    }

                    if (popupFont != null)
                    {
                        using (popupFont.Push())
                        {
                            float fontH = ImGui.GetFontSize();
                            float trackPx = fontH * 0.18f;
                            string label = options[i].ToUpperInvariant();
                            Vector4 col = isSel ? GoldWarm : (hov ? Text : TextDim);
                            DrawTrackedText(pdl,
                                new Vector2(rowMn.X + itemPadX, rowMn.Y + (itemH - fontH) * 0.5f),
                                label, U32(col), trackPx);
                        }
                    }
                }
                if (needsScroll) ImGui.EndChild();
                ImGui.EndPopup();
            }
            else
            {
                _sortPillOpen[id] = false;
            }
            ImGui.PopStyleVar(4);
            ImGui.PopStyleColor(3);
        }

        ImGui.SetCursorScreenPos(new Vector2(min.X + width, min.Y));
        return picked;
    }

    // ── Dropdown pill button (caller draws popup) ───────────────────────
    /// <summary>
    /// Boutique pill chrome: [KICKER]  value  ▾. Renders at current cursor.
    /// Returns true if clicked (caller should ImGui.OpenPopup at that point).
    /// </summary>
    public static bool DropdownPillButton(string id, string kicker, string value,
        float minWidth, float scale)
    {
        float h = 26f * scale;
        float padX = 12f * scale;
        float chevW = 12f * scale;
        float gap = 10f * scale;

        // Measure
        float kickerTrack = 2.0f * scale;
        float valTrack = 1.8f * scale;
        float kickerW;
        float valTextW;
        using (Plugin.Instance?.OswaldMed9?.Push())
        {
            kickerW = string.IsNullOrEmpty(kicker) ? 0f : MeasureTrackedText(kicker, kickerTrack);
        }
        using (Plugin.Instance?.OswaldMed11?.Push())
        {
            valTextW = MeasureTrackedText(value ?? "", valTrack);
        }
        float natW = padX + kickerW + (kickerW > 0 ? gap : 0)
                     + valTextW + gap + chevW + padX;
        float w = MathF.Max(minWidth, natW);

        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var min = origin;
        var max = min + new Vector2(w, h);

        bool clicked = ImGui.InvisibleButton($"##dd_{id}", new Vector2(w, h));
        bool hovered = ImGui.IsItemHovered();

        Vector4 bg = new Vector4(0x08 / 255f, 0x0A / 255f, 0x0E / 255f, 0.85f);
        Vector4 border = hovered ? GoldDeep : BorderSoft;
        dl.AddRectFilled(min, max, U32(bg));
        dl.AddRect(min, max, U32(border), 0f, ImDrawFlags.None, 1f);

        float midY = (min.Y + max.Y) * 0.5f;
        float cursor = min.X + padX;
        if (kickerW > 0)
        {
            using (Plugin.Instance?.OswaldMed9?.Push())
            {
                float fontH = ImGui.GetFontSize();
                DrawTrackedText(dl, new Vector2(cursor, midY - fontH * 0.5f),
                    kicker, U32(TextGhost), kickerTrack);
            }
            cursor += kickerW + gap;
        }
        using (Plugin.Instance?.OswaldMed11?.Push())
        {
            float fontH = ImGui.GetFontSize();
            DrawTrackedText(dl, new Vector2(cursor, midY - fontH * 0.5f),
                value ?? "", U32(GoldWarm), valTrack);
        }

        // Chevron (FontAwesome chevron-down )
        string chev = "";
        ImGui.PushFont(UiBuilder.IconFont);
        var chevNat = ImGui.CalcTextSize(chev);
        ImGui.PopFont();
        float chevSize = 9f * scale;
        float chevRatio = chevSize / UiBuilder.IconFont.FontSize;
        var chevDrawn = chevNat * chevRatio;
        var chevPos = new Vector2(max.X - padX - chevDrawn.X, midY - chevDrawn.Y * 0.5f);
        dl.AddText(UiBuilder.IconFont, chevSize, chevPos, U32(GoldDeep), chev);

        ImGui.SetCursorScreenPos(new Vector2(min.X + w, min.Y));
        return clicked;
    }

    // ── Text field (lightly skinned ImGui.InputText) ────────────────────
    /// <summary>
    /// Wraps ImGui.InputText with boutique frame styling + focus glow.
    /// Caller-supplied width. Returns true if changed.
    /// </summary>
    public static bool TextField(string id, ref string value, int maxLength,
        float width, float scale, ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
    {
        float h = 26f * scale;

        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0x08 / 255f, 0x0A / 255f, 0x0E / 255f, 0.85f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0x08 / 255f, 0x0A / 255f, 0x0E / 255f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0x08 / 255f, 0x0A / 255f, 0x0E / 255f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, Text);
        ImGui.PushStyleColor(ImGuiCol.Border, BorderSoft);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f * scale, (h - ImGui.GetFontSize()) * 0.5f));

        ImGui.SetNextItemWidth(width);
        bool changed = ImGui.InputText($"##tf_{id}", ref value, maxLength, flags);

        // Focus glow on the just-rendered item
        if (ImGui.IsItemActive())
        {
            var dl = ImGui.GetWindowDrawList();
            var iMin = ImGui.GetItemRectMin();
            var iMax = ImGui.GetItemRectMax();
            for (int g = 2; g >= 1; g--)
            {
                float pad = g * 1.5f * scale;
                uint glow = U32(WithAlpha(Gold, 0.18f / g));
                dl.AddRect(iMin - new Vector2(pad, pad), iMax + new Vector2(pad, pad), glow,
                    0f, ImDrawFlags.None, 1f);
            }
            dl.AddRect(iMin, iMax, U32(Gold), 0f, ImDrawFlags.None, 1f);
        }

        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(5);
        return changed;
    }

    // ── Buttons (three flavours, all chamfered slip) ────────────────────
    /// <summary>Ghost button: dark bg, BorderSoft, TextFaint label.</summary>
    public static bool GhostButton(string id, string label, float scale)
    {
        return DrawCharmedButton(id, label, scale,
            bg: new Vector4(0f, 0f, 0f, 0.45f),
            border: BorderSoft,
            label_: TextFaint,
            hoverBorder: GoldDeep,
            hoverLabel: GoldWarm,
            hoverBg: new Vector4(0f, 0f, 0f, 0.55f));
    }

    /// <summary>Outline button: transparent bg, GoldDeep border, GoldWarm label.</summary>
    public static bool OutlineButton(string id, string label, float scale)
    {
        return DrawCharmedButton(id, label, scale,
            bg: new Vector4(0f, 0f, 0f, 0.0f),
            border: GoldDeep,
            label_: GoldWarm,
            hoverBorder: Gold,
            hoverLabel: Gold,
            hoverBg: WithAlpha(Gold, 0.08f));
    }

    /// <summary>Primary button: gold gradient, dark text, big halo.</summary>
    public static bool PrimaryButton(string id, string label, float scale)
    {
        // Reuse the existing gold pill primitive (chamfered, gradient, halo).
        // DrawGoldPill already provides the canonical primary look used by
        // wardrobe / patch notes / etc.
        var size = BoutiqueChassis.DrawGoldPillSize(label, 1.6f * scale, scale);
        var origin = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(origin);
        bool clicked = ImGui.InvisibleButton($"##pbtn_{id}", size);
        bool hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var min = origin;
        var max = origin + size;
        // Gradient fill
        uint gTop = U32(GoldWarm);
        uint gBot = U32(Gold);
        dl.AddRectFilledMultiColor(min, max, gTop, gTop, gBot, gBot);
        dl.AddRect(min, max, U32(WithAlpha(Gold, hovered ? 0.85f : 0.55f)), 0f, ImDrawFlags.None, 1f);
        // Halo
        for (int g = 3; g >= 1; g--)
        {
            float pad = g * 2f * scale;
            uint glow = U32(WithAlpha(Gold, (hovered ? 0.20f : 0.12f) / g));
            dl.AddRectFilled(min - new Vector2(pad, pad), max + new Vector2(pad, pad), glow);
        }
        // Label
        using (Plugin.Instance?.OswaldSemi11?.Push())
        {
            float trackPx = 1.6f * scale;
            float labelW = MeasureTrackedText(label, trackPx);
            float fontH = ImGui.GetFontSize();
            DrawTrackedText(dl,
                new Vector2((min.X + max.X) * 0.5f - labelW * 0.5f,
                            (min.Y + max.Y) * 0.5f - fontH * 0.5f),
                label, U32(new Vector4(0.10f, 0.08f, 0.03f, 1f)), trackPx);
        }
        return clicked;
    }

    private static bool DrawCharmedButton(string id, string label, float scale,
        Vector4 bg, Vector4 border, Vector4 label_,
        Vector4 hoverBorder, Vector4 hoverLabel, Vector4 hoverBg)
    {
        float h = 30f * scale;
        float padX = 16f * scale;
        float trackPx = 1.6f * scale;

        // Measure label width with tracking
        float labelW;
        using (Plugin.Instance?.OswaldSemi10?.Push())
        {
            labelW = MeasureTrackedText(label, trackPx);
        }
        float w = labelW + padX * 2;

        var origin = ImGui.GetCursorScreenPos();
        var min = origin;
        var max = origin + new Vector2(w, h);
        bool clicked = ImGui.InvisibleButton($"##gbtn_{id}", new Vector2(w, h));
        bool hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        // Slip-polygon fill + outline (5px chamfer at TR + BL)
        float chamfer = 5f * scale;
        Vector4 fill = hovered ? hoverBg : bg;
        Vector4 brd = hovered ? hoverBorder : border;
        Vector4 lbl = hovered ? hoverLabel : label_;
        CodexChassis.FillSlip(dl, min, max, chamfer, U32(fill));
        CodexChassis.StrokeSlip(dl, min, max, chamfer, U32(brd), 1f);

        using (Plugin.Instance?.OswaldSemi10?.Push())
        {
            float fontH = ImGui.GetFontSize();
            DrawTrackedText(dl,
                new Vector2((min.X + max.X) * 0.5f - labelW * 0.5f,
                            (min.Y + max.Y) * 0.5f - fontH * 0.5f),
                label, U32(lbl), trackPx);
        }
        return clicked;
    }

    // ── Sub-group header (mockup .group-head) ───────────────────────────
    /// <summary>
    /// Renders the chamfered slip card body + header row at the current
    /// cursor. Caller continues to render rows inside the card. End the
    /// card with EndSubGroup which closes the visual rect and advances.
    /// </summary>
    public static void BeginSubGroup(string label, string? hintRight, float scale,
        out Vector2 cardMin, out Vector2 cardMax_unused)
    {
        float headH = 36f * scale;
        float padX = 16f * scale;
        float marginTop = 10f * scale;

        ImGui.Dummy(new Vector2(0, marginTop));

        var origin = ImGui.GetCursorScreenPos();
        float availW = ImGui.GetContentRegionAvail().X;
        cardMin = origin;
        cardMax_unused = origin; // filled at EndSubGroup

        // Defer the card body rendering until EndSubGroup since we don't know
        // the height yet. Stash the cardMin in a per-thread stack for End.
        _subGroupStack.Push((cardMin, label, hintRight ?? "", scale, availW));

        var dl = ImGui.GetWindowDrawList();
        var headMin = cardMin;
        var headMax = cardMin + new Vector2(availW, headH);

        // Header bg is part of the card: slightly darker than the card body
        // would be drawn in EndSubGroup. Draw a temporary header tint here so
        // the head reads even if EndSubGroup is somehow not called yet.
        // (The full card body render happens in EndSubGroup.)

        // Glyph (small diamond)
        float glyphSize = 7f * scale;
        var glyphCentre = new Vector2(headMin.X + padX + glyphSize * 0.5f, (headMin.Y + headMax.Y) * 0.5f);
        DrawDiamondAt(dl, glyphCentre, glyphSize * 0.5f, U32(GoldDeep));

        // Cap label
        float labelX = glyphCentre.X + glyphSize + 10f * scale;
        using (Plugin.Instance?.OswaldSemi11?.Push())
        {
            float trackPx = 3.0f * scale;
            float fontH = ImGui.GetFontSize();
            DrawTrackedText(dl, new Vector2(labelX, (headMin.Y + headMax.Y) * 0.5f - fontH * 0.5f),
                label.ToUpperInvariant(), U32(GoldWarm), trackPx);
        }

        // Right hint
        if (!string.IsNullOrEmpty(hintRight))
        {
            using (Plugin.Instance?.OswaldMed9?.Push())
            {
                float trackPx = 2.4f * scale;
                float fontH = ImGui.GetFontSize();
                float hintW = MeasureTrackedText(hintRight, trackPx);
                DrawTrackedText(dl,
                    new Vector2(headMax.X - padX - hintW, (headMin.Y + headMax.Y) * 0.5f - fontH * 0.5f),
                    hintRight, U32(TextGhost), trackPx);
            }
        }

        // Bottom hairline of header
        dl.AddLine(new Vector2(headMin.X + padX, headMax.Y),
                   new Vector2(headMax.X - padX, headMax.Y),
                   U32(WithAlpha(Gold, 0.12f)), 1f);

        // Position cursor below the header for row content
        ImGui.SetCursorScreenPos(new Vector2(headMin.X, headMax.Y + 2f * scale));
    }

    public static void EndSubGroup()
    {
        if (_subGroupStack.Count == 0) return;
        var (cardMin, label, hintRight, scale, availW) = _subGroupStack.Pop();

        var endCursor = ImGui.GetCursorScreenPos();
        float cardBottom = endCursor.Y + 4f * scale;
        var cardMax = new Vector2(cardMin.X + availW, cardBottom);
        float chamfer = 10f * scale;

        var dl = ImGui.GetWindowDrawList();
        // Card fill is drawn AT END so it's beneath the rows. To do that we
        // need to use the channel feature of the draw list. Simpler: just
        // draw the OUTLINE here at the end, and rely on the parent group's
        // velvet bg being visible through the (transparent) card interior.
        // Since the parent panel uses the DrawSectionGroupCard wrapping
        // already, sub-groups should be visually distinct: draw a 1px
        // BorderSoft outline + 2px gold-deep top stripe.
        dl.AddRect(cardMin, cardMax, U32(BorderSoft), 0f, ImDrawFlags.None, 1f);
        dl.AddRectFilled(
            new Vector2(cardMin.X, cardMin.Y),
            new Vector2(cardMax.X - chamfer, cardMin.Y + 2f * scale),
            U32(WithAlpha(GoldDeep, 0.55f)));

        // Bottom margin
        ImGui.Dummy(new Vector2(0, 8f * scale));
    }

    private static readonly Stack<(Vector2 min, string label, string hint, float scale, float availW)> _subGroupStack = new();

    private static void DrawDiamondAt(ImDrawListPtr dl, Vector2 centre, float halfW, uint colour)
    {
        var top = new Vector2(centre.X, centre.Y - halfW);
        var right = new Vector2(centre.X + halfW, centre.Y);
        var bot = new Vector2(centre.X, centre.Y + halfW);
        var left = new Vector2(centre.X - halfW, centre.Y);
        dl.AddTriangleFilled(top, right, bot, colour);
        dl.AddTriangleFilled(top, bot, left, colour);
    }

    // ── Callout (warning / info / danger) ───────────────────────────────
    public static void Callout(CalloutKind kind, FontAwesomeIcon icon, string title, string body, float scale)
    {
        Vector4 borderCol;
        Vector4 titleCol;
        switch (kind)
        {
            case CalloutKind.Warning:
                borderCol = new Vector4(1f, 0.58f, 0.30f, 1f);
                titleCol  = new Vector4(1f, 0.80f, 0.30f, 1f);
                break;
            case CalloutKind.Danger:
                borderCol = Red;
                titleCol  = Red;
                break;
            default:
                borderCol = CyanSoft;
                titleCol  = CyanSoft;
                break;
        }

        ImGui.Dummy(new Vector2(0, 4f * scale));
        var origin = ImGui.GetCursorScreenPos();
        float availW = ImGui.GetContentRegionAvail().X;
        // Bigger box per UX feedback: more padding + roomier icon gap.
        // Body sits BELOW the title row at the same left padding so it spans
        // the full inner width rather than being indented past the icon
        // column - reads more like a magazine pull-quote, less like a
        // hanging-indent bullet.
        float padX = 16f * scale;
        float padY = 12f * scale;
        float iconGap = 10f * scale;            // gap between icon and title text
        float titleBodyGap = 8f * scale;
        float leftBarW = 2f * scale;

        // Title in OswaldSemi13 (bumped per UX feedback, was 11).
        float titleH = 0f;
        using (Plugin.Instance?.OswaldSemi13?.Push())
        {
            titleH = ImGui.GetFontSize();
        }

        // Icon glyph natural size (so we know how wide to leave for it next
        // to the title).
        string iconStr = icon.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        var iconNat = ImGui.CalcTextSize(iconStr);
        ImGui.PopFont();
        float iconRender = 18f * scale;
        float iconRatio = iconRender / UiBuilder.IconFont.FontSize;
        var iconDrawn = iconNat * iconRatio;

        // Body wraps to the full inner width (minus left bar + padX*2). Sits
        // at the SAME X as the icon, so the title row hangs centred above
        // and the body fills the box left-to-right.
        float innerLeft = leftBarW + padX;
        float innerRight = availW - padX;
        float bodyW = innerRight - innerLeft;

        // Body in OutfitBody15 (15.5px) - more readable inside the callout.
        // Same family the design panel uses for descriptive copy.
        float bodyH = 0f;
        using (Plugin.Instance?.OutfitBody15?.Push())
        {
            var textSize = ImGui.CalcTextSize(body, false, bodyW);
            bodyH = textSize.Y;
        }
        float totalH = padY * 2 + titleH + titleBodyGap + bodyH;

        var min = origin;
        var max = origin + new Vector2(availW, totalH);

        var dl = ImGui.GetWindowDrawList();
        // Translucent dark bg + coloured 2px left bar
        dl.AddRectFilled(min, max, U32(new Vector4(0f, 0f, 0f, 0.45f)));
        dl.AddRect(min, max, U32(WithAlpha(borderCol, 0.55f)), 0f, ImDrawFlags.None, 1f);
        dl.AddRectFilled(min, new Vector2(min.X + leftBarW, max.Y), U32(borderCol));

        // Icon (sits at the start of the title row).
        dl.AddText(UiBuilder.IconFont, iconRender,
            new Vector2(min.X + innerLeft, min.Y + padY + (titleH - iconDrawn.Y) * 0.5f),
            U32(titleCol), iconStr);

        // Title - OswaldSemi13, sits to the right of the icon.
        float titleX = min.X + innerLeft + iconDrawn.X * iconRatio + iconGap;
        using (Plugin.Instance?.OswaldSemi13?.Push())
        {
            float trackPx = 2.6f * scale;
            DrawTrackedText(dl,
                new Vector2(titleX, min.Y + padY),
                title.ToUpperInvariant(), U32(titleCol), trackPx);
        }

        // Body. OutfitBody15 (15.5px) for legibility. Sits at the same left
        // padding as the icon so the box reads as a unit instead of a
        // hanging-indent bullet. PushTextWrapPos takes a WINDOW-LOCAL X.
        ImGui.SetCursorScreenPos(new Vector2(min.X + innerLeft, min.Y + padY + titleH + titleBodyGap));
        ImGui.PushStyleColor(ImGuiCol.Text, Text);
        using (Plugin.Instance?.OutfitBody15?.Push())
        {
            float wrapLocal = (min.X + innerRight) - ImGui.GetWindowPos().X;
            ImGui.PushTextWrapPos(wrapLocal);
            ImGui.TextUnformatted(body);
            ImGui.PopTextWrapPos();
        }
        ImGui.PopStyleColor();

        // Advance to below callout
        ImGui.SetCursorScreenPos(new Vector2(origin.X, max.Y + 4f * scale));
        ImGui.Dummy(new Vector2(0, 0));
    }

    // ── List row (Backup, Assignments, Random Groups) ───────────────────
    /// <summary>
    /// Generic list row: marker + primary text + meta + right-aligned
    /// actions. drawActions runs at the right edge with width budget = 80px
    /// (caller adjusts if their action button is wider).
    /// Returns the y-extent consumed.
    /// </summary>
    public static void ListRow(string id, ListMarker marker, Vector4 markerColor,
        string primary, string meta, float scale, Action drawActions,
        float actionsWidth = 80f)
    {
        float rowH = 36f * scale;
        float padX = 14f * scale;
        float markerW = 9f * scale;

        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        float availW = ImGui.GetContentRegionAvail().X;
        var min = origin;
        var max = origin + new Vector2(availW, rowH);
        float midY = (min.Y + max.Y) * 0.5f;

        // Top divider
        dl.AddLine(new Vector2(min.X + padX, min.Y),
                   new Vector2(max.X - padX, min.Y),
                   U32(new Vector4(1f, 1f, 1f, 0.025f)), 1f);

        // Marker
        var markerCentre = new Vector2(min.X + padX + markerW * 0.5f, midY);
        switch (marker)
        {
            case ListMarker.DiamondGold:
                DrawDiamondAt(dl, markerCentre, markerW * 0.5f, U32(GoldDeep));
                break;
            case ListMarker.DiamondLatest:
                // Pulsing gold halo + bright gold core
                {
                    if (!_markerPulseSeed.TryGetValue(id, out float seed))
                    {
                        seed = (float)ImGui.GetTime();
                        _markerPulseSeed[id] = seed;
                    }
                    float pulse = 0.55f + 0.45f * MathF.Sin((float)(ImGui.GetTime() - seed) * 2.4f);
                    for (int g = 3; g >= 1; g--)
                    {
                        float pad = g * 1.5f * scale;
                        uint glow = U32(WithAlpha(Gold, 0.30f * pulse / g));
                        dl.AddRectFilled(markerCentre - new Vector2(markerW * 0.5f + pad, markerW * 0.5f + pad),
                                         markerCentre + new Vector2(markerW * 0.5f + pad, markerW * 0.5f + pad),
                                         glow);
                    }
                    DrawDiamondAt(dl, markerCentre, markerW * 0.5f, U32(Gold));
                }
                break;
            case ListMarker.ColourSquare:
                {
                    var sqMin = markerCentre - new Vector2(markerW * 0.5f, markerW * 0.5f);
                    var sqMax = markerCentre + new Vector2(markerW * 0.5f, markerW * 0.5f);
                    dl.AddRectFilled(sqMin, sqMax, U32(markerColor));
                }
                break;
        }

        // Primary text
        float primaryX = min.X + padX + markerW + 12f * scale;
        float primaryY = midY - 14f * scale;
        float metaY = midY + 1f * scale;
        float infoMaxX = max.X - padX - actionsWidth - 12f * scale;
        using (Plugin.Instance?.OswaldMed11?.Push())
        {
            float trackPx = 2.0f * scale;
            float fontH = ImGui.GetFontSize();
            string truncP = ClampToWidth(primary, infoMaxX - primaryX, fontH);
            DrawTrackedText(dl, new Vector2(primaryX, midY - fontH - 1f),
                truncP, U32(Text), trackPx);
            if (truncP != primary && ImGui.IsMouseHoveringRect(min, max, true))
            {
                Tooltip(primary);
            }
        }

        // Meta line below
        if (!string.IsNullOrEmpty(meta))
        {
            using (Plugin.Instance?.OutfitBody12?.Push())
            {
                float fontH = ImGui.GetFontSize();
                string truncM = ClampToWidth(meta, infoMaxX - primaryX, fontH);
                dl.AddText(new Vector2(primaryX, midY + 2f * scale),
                    U32(TextDim), truncM);
            }
        }

        // Position cursor for action buttons (right-aligned)
        ImGui.SetCursorScreenPos(new Vector2(max.X - padX - actionsWidth, midY - 14f * scale));
        ImGui.BeginGroup();
        drawActions();
        ImGui.EndGroup();

        // Advance cursor to next row
        ImGui.SetCursorScreenPos(new Vector2(min.X, max.Y));
        ImGui.Dummy(new Vector2(0, 0));
    }
}

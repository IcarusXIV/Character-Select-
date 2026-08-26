using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace CharacterSelectPlugin.Windows.Styles;

public static partial class Boutique
{
    // ── Search pill ─────────────────────────────────────────────────────
    // Boutique search input: 30px tall, magnifier on the left, optional
    // tracked-caps kicker before the divider, lifts to gold-deep border on
    // focus and adds a faint inset gold halo.

    /// <summary>
    /// Draw a full-width search pill (background + magnifier + optional kicker
    /// + InputText). Returns true if the value was edited this frame.
    /// </summary>
    public static bool DrawSearchPill(string id, ref string value, string placeholder,
        string? kicker, float scale, int maxLength = 200)
    {
        var dl = ImGui.GetWindowDrawList();
        float pillH = SearchPillHeight * scale;
        var pos = ImGui.GetCursorScreenPos();
        float w = ImGui.GetContentRegionAvail().X;
        var min = pos;
        var max = new Vector2(min.X + w, min.Y + pillH);

        dl.AddRectFilled(min, max, U32(PillBg));

        float padX = 12f * scale;
        float xCursor = min.X + padX;
        float midY = (min.Y + max.Y) * 0.5f;

        // Optional kicker + divider
        if (!string.IsNullOrEmpty(kicker))
        {
            using (Kicker9?.Push())
            {
                float fs = ImGui.GetFontSize();
                float trackPx = Track30(fs);
                DrawTrackedText(dl, new Vector2(xCursor, midY - fs * 0.5f),
                    kicker.ToUpperInvariant(), U32(TextFaint), trackPx);
                xCursor += MeasureTrackedText(kicker.ToUpperInvariant(), trackPx) + 8f * scale;
            }
            // Divider hairline
            dl.AddLine(new Vector2(xCursor, midY - 7f * scale),
                       new Vector2(xCursor, midY + 7f * scale),
                       U32(BorderSoft), 1f * scale);
            xCursor += 8f * scale;
        }

        // Magnifier glyph
        var iconFont = UiBuilder.IconFont;
        string searchGlyph = FontAwesomeIcon.Search.ToIconString();
        ImGui.PushFont(iconFont);
        var sgSz = ImGui.CalcTextSize(searchGlyph);
        ImGui.PopFont();
        float sgPx = iconFont.FontSize * 0.65f;
        float sgScaleR = sgPx / iconFont.FontSize;
        dl.AddText(iconFont, sgPx,
            new Vector2(xCursor, midY - sgSz.Y * sgScaleR * 0.5f),
            U32(TextFaint), searchGlyph);

        float inputX = xCursor + sgSz.X * sgScaleR + 8f * scale;
        float inputW = max.X - inputX - padX;
        float inputPadY = MathF.Max(0f, (pillH - ImGui.GetTextLineHeight()) * 0.5f);

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0f, inputPadY));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Text, InputText);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, InputPlaceholder);

        ImGui.SetCursorScreenPos(new Vector2(inputX, min.Y));
        ImGui.SetNextItemWidth(inputW);
        bool changed = ImGui.InputTextWithHint($"##{id}", placeholder, ref value, maxLength);
        bool focused = ImGui.IsItemActive();

        ImGui.PopStyleColor(5);
        ImGui.PopStyleVar(2);

        // Border (focus → gold-deep)
        uint border = U32(focused ? GoldDeep : BorderSoft);
        dl.AddRect(min, max, border, 0f, ImDrawFlags.None, 1f * scale);

        if (focused)
        {
            // Faint inset gold halo on focus
            dl.AddRect(
                new Vector2(min.X + 1f * scale, min.Y + 1f * scale),
                new Vector2(max.X - 1f * scale, max.Y - 1f * scale),
                U32(WithAlpha(Gold, 0.12f)), 0f, ImDrawFlags.None, 1f * scale);
        }

        if (HoveredTokenKey != null)
        {
            DrawTokenHighlight(dl, min, max, "color.frameBg");
            DrawTokenHighlight(dl, min, max, "custom.input.text");
            DrawTokenHighlight(dl, min, max, "custom.input.placeholder");
        }

        ImGui.SetCursorScreenPos(new Vector2(min.X, max.Y));
        return changed;
    }

    // ── Boutique checkbox ───────────────────────────────────────────────

    /// <summary>
    /// Draw a 14x14 chamfer-less checkbox at the supplied min position. Returns
    /// true on click. The caller is responsible for syncing state with the
    /// returned click. `wrapperWidth` controls the invisible-button width so
    /// the checkbox label slot can match a 78px control wrapper.
    /// </summary>
    public static bool DrawBoutiqueCheckbox(ImDrawListPtr dl, Vector2 min, float scale,
        bool isOn, string id,
        string? label = null, float wrapperWidth = 0f)
    {
        float chk = CheckboxSide * scale;
        float h = StateCtrlH * scale;
        bool hasWrapper = wrapperWidth > 0f;

        // Padding only exists when there's a wrapper (padding-left for the
        // box inside a wider slot). Without a wrapper, the box sits at min
        // directly so the InvisibleButton hit-rect coincides with the box,
        // previously the button was 14 px wide at min while the box was
        // drawn 8 px to the right, leaving the LEFT 8 px hot but the BOX'S
        // RIGHT 8 px unclickable.
        float padX = hasWrapper ? 8f * scale : 0f;
        float wrapW = hasWrapper ? wrapperWidth * scale : chk;

        ImGui.SetCursorScreenPos(min);
        bool clicked = ImGui.InvisibleButton($"##bchk_{id}", new Vector2(wrapW, h));
        bool hovered = ImGui.IsItemHovered();

        var boxMin = new Vector2(min.X + padX, min.Y + (h - chk) * 0.5f);
        var boxMax = boxMin + new Vector2(chk, chk);

        // Box bg + border
        dl.AddRectFilled(boxMin, boxMax, U32(PillBgDeep));
        Vector4 borderC = isOn ? GoldDeep : (hovered ? GoldDeep : BorderSoft);
        dl.AddRect(boxMin, boxMax, U32(borderC), 0f, ImDrawFlags.None, 1f * scale);

        // Check glyph if on
        if (isOn)
        {
            var iconFont = UiBuilder.IconFont;
            string g = FontAwesomeIcon.Check.ToIconString();
            float gPx = chk * 0.7f;
            ImGui.PushFont(iconFont);
            var sz = ImGui.CalcTextSize(g);
            ImGui.PopFont();
            float scaleR = gPx / iconFont.FontSize;
            var p = new Vector2(boxMin.X + (chk - sz.X * scaleR) * 0.5f,
                                boxMin.Y + (chk - sz.Y * scaleR) * 0.5f);
            dl.AddText(iconFont, gPx, p, U32(Gold), g);
        }

        // Optional small tracked-caps label after the box
        if (!string.IsNullOrEmpty(label))
        {
            using (Kicker9?.Push())
            {
                float fs = ImGui.GetFontSize();
                float trackPx = Track26(fs);
                Vector4 lblCol = isOn ? Text : TextFaint;
                DrawTrackedText(dl, new Vector2(boxMax.X + 8f * scale, min.Y + (h - fs) * 0.5f),
                    label.ToUpperInvariant(), U32(lblCol), trackPx);
            }
        }

        return clicked;
    }

    // ── Field label (tracked-caps Oswald above an input) ───────────────

    /// <summary>
    /// Tracked-caps Oswald label above an input. Caller must push the label
    /// font (typically Kicker9) before calling.
    /// </summary>
    public static void DrawFieldLabel(string label, bool required, string? tooltip = null)
    {
        var dl = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        float fontH = ImGui.GetFontSize();
        float trackPx = Track18(fontH);

        float labelW = MeasureTrackedText(label, trackPx);
        var pos = ImGui.GetCursorScreenPos();
        DrawTrackedText(dl, pos, label, U32(Text), trackPx);

        float trailingX = labelW + 5f;

        if (required)
        {
            float starX = pos.X + trailingX;
            float starSize = font.FontSize * 1.15f;
            ImGui.PushFont(font);
            float starW = ImGui.CalcTextSize("*").X * 1.15f;
            ImGui.PopFont();
            dl.AddText(font, starSize,
                new Vector2(starX, pos.Y - 1f),
                U32(GoldWarm), "*");
            trailingX += starW + 4f;
        }

        if (!string.IsNullOrEmpty(tooltip))
        {
            float scale = FormScale;
            float iconSize = 14f * scale;
            float iconLeft = pos.X + trailingX + 4f;
            float iconY = pos.Y + (fontH - iconSize) * 0.5f;
            var iconMin = new Vector2(iconLeft, iconY);

            ImGui.SetCursorScreenPos(iconMin);
            ImGui.InvisibleButton("##bfield_info_" + label, new Vector2(iconSize, iconSize));
            bool hovered = ImGui.IsItemHovered();
            Vector4 inkC = hovered ? Gold : GoldDeep;

            var iconFont = UiBuilder.IconFont;
            float glyphScale = iconSize / iconFont.FontSize;
            ImGui.PushFont(iconFont);
            var glyphSz = ImGui.CalcTextSize("");
            ImGui.PopFont();
            dl.AddText(iconFont, iconSize,
                new Vector2(iconMin.X + (iconSize - glyphSz.X * glyphScale) * 0.5f,
                            iconMin.Y + (iconSize - glyphSz.Y * glyphScale) * 0.5f),
                U32(inkC), "");

            if (hovered) Tooltip(tooltip);

            ImGui.SetCursorScreenPos(pos);
        }

        ImGui.Dummy(new Vector2(labelW, fontH));
    }
}

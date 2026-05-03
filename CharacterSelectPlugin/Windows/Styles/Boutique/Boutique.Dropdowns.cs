using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CharacterSelectPlugin.Windows.Styles;

public static partial class Boutique
{
    // ── Sort pill (the Wardrobe-style kicker + value + chevron picker) ──
    // Used for Collection picker on the mod manager and the sort selector on
    // wardrobe / gallery surfaces. Click toggles a popup with options.

    /// <summary>
    /// Draw a sort-pill chrome (background + border + kicker + tracked-caps
    /// value + chevron). Returns true on click. Caller is responsible for
    /// opening the popup and handling option selection.
    /// </summary>
    public static bool DrawSortPill(ImDrawListPtr dl, Vector2 pos, Vector2 size,
        string kicker, string value, float scale, bool open, string id)
    {
        var min = pos;
        var max = pos + size;

        ImGui.SetCursorScreenPos(min);
        bool clicked = ImGui.InvisibleButton($"##bsort_{id}", size);
        bool hovered = ImGui.IsItemHovered();

        dl.AddRectFilled(min, max, U32(PillBgDeep));
        Vector4 borderC = open ? Gold : (hovered ? GoldDeep : BorderSoft);
        dl.AddRect(min, max, U32(borderC), 0f, ImDrawFlags.None, 1f * scale);

        float padX = 12f * scale;
        float midY = (min.Y + max.Y) * 0.5f;

        // Kicker on the left
        using (Kicker9?.Push())
        {
            float fs = ImGui.GetFontSize();
            float trackPx = Track34(fs);
            DrawTrackedText(dl, new Vector2(min.X + padX, midY - fs * 0.5f),
                kicker.ToUpperInvariant(), U32(TextFaint), trackPx);
        }

        // Chevron on the right
        float chR = 4f * scale;
        var chC = new Vector2(max.X - padX - chR, midY);
        Vector4 chevColour = open ? Gold : GoldDeep;
        dl.AddTriangleFilled(
            chC + new Vector2(-chR, -chR * 0.5f),
            chC + new Vector2( chR, -chR * 0.5f),
            chC + new Vector2(0f, chR * 0.7f),
            U32(chevColour));

        // Value tracked-caps, right-aligned (leave room for kicker + chevron)
        using (OswaldMed11?.Push())
        {
            float fs = ImGui.GetFontSize();
            float trackPx = Track18(fs);
            float maxValW = (size.X - padX) - 90f * scale - chR * 4;
            string display = TruncateTrackedToWidth(value.ToUpperInvariant(), trackPx, maxValW);
            float vW = MeasureTrackedText(display, trackPx);
            DrawTrackedText(dl,
                new Vector2(max.X - padX - chR * 4 - vW, midY - fs * 0.5f),
                display, U32(GoldWarm), trackPx);
        }

        return clicked;
    }

    // ── Filter pill (compact KICKER : VALUE pill with triangle chevron) ─
    // Replaces the legacy "v"-letter chevron from BoutiqueChassis.DrawFilterPill
    // with a proper filled triangle to match the boutique sort-pill chevron.
    // Caller pushes the font (typically Kicker11 / OswaldSemi11) before
    // measuring/drawing, typography is caller-controlled so the same pill
    // can host different sizes across surfaces.

    public static Vector2 MeasureFilterPill(string lbl, string val, float trackPx, float scale)
    {
        float padX = 11f * scale;
        float gap = 8f * scale;
        float chevSlot = 14f * scale;
        float lblW = MeasureTrackedText(lbl, trackPx);
        float valW = MeasureTrackedText(val, trackPx);
        float w = padX * 2 + lblW + gap + valW + gap + chevSlot;
        float fontSize = ImGui.GetFontSize();
        float h = MathF.Max(28f * scale, fontSize + 12f * scale);
        return new Vector2(w, h);
    }

    public static void DrawFilterPill(ImDrawListPtr dl, Vector2 pos, string lbl, string val,
        float trackPx, float scale, bool hovered)
    {
        var size = MeasureFilterPill(lbl, val, trackPx, scale);
        var min = pos;
        var max = pos + size;

        var bg = U32(hovered ? Surface1 : PillBg);
        dl.AddRectFilled(min, max, bg);
        var borderCol = U32(hovered ? GoldDeep : BorderSoft);
        dl.AddRect(min, max, borderCol, 0f, ImDrawFlags.None, 1f * scale);

        float padX = 11f * scale;
        float gap = 8f * scale;
        float fontSize = ImGui.GetFontSize();
        float yText = min.Y + (size.Y - fontSize) * 0.5f;
        var x = min.X + padX;

        DrawTrackedText(dl, new Vector2(x, yText), lbl,
            U32(hovered ? Text : TextFaint), trackPx);
        x += MeasureTrackedText(lbl, trackPx) + gap;

        DrawTrackedText(dl, new Vector2(x, yText), val,
            U32(hovered ? GoldWarm : Text), trackPx);
        x += MeasureTrackedText(val, trackPx) + gap;

        // Triangle chevron (matches DrawSortPill / state combo style)
        float chR = 4f * scale;
        var chC = new Vector2(max.X - padX - chR, (min.Y + max.Y) * 0.5f);
        Vector4 chevColour = hovered ? Gold : GoldDeep;
        dl.AddTriangleFilled(
            chC + new Vector2(-chR, -chR * 0.5f),
            chC + new Vector2( chR, -chR * 0.5f),
            chC + new Vector2(0f, chR * 0.7f),
            U32(chevColour));
    }

    /// <summary>
    /// Render a popup row: tracked-caps Oswald label, optional left bar +
    /// gold tint when selected, hover wash. Returns true on click.
    /// </summary>
    public static bool DrawPopupRow(ImDrawListPtr dl, float width, float scale,
        string label, bool selected, string id)
    {
        float h = 26f * scale;
        var pos = ImGui.GetCursorScreenPos();
        var min = pos;
        var max = pos + new Vector2(width, h);

        ImGui.SetCursorScreenPos(min);
        bool clicked = ImGui.InvisibleButton($"##bpop_{id}", new Vector2(width, h));
        bool hovered = ImGui.IsItemHovered();

        if (selected)
        {
            dl.AddRectFilled(min, max, U32(WithAlpha(Gold, 0.18f)));
            dl.AddRectFilled(min, new Vector2(min.X + 2f * scale, max.Y), U32(Gold));
        }
        else if (hovered)
        {
            dl.AddRectFilled(min, max, U32(WithAlpha(Gold, 0.10f)));
        }

        using (OswaldMed11?.Push())
        {
            float fs = ImGui.GetFontSize();
            float trackPx = Track18(fs);
            Vector4 col = selected ? GoldWarm : (hovered ? GoldBright : Text);
            DrawTrackedText(dl, new Vector2(min.X + 14f * scale, min.Y + (h - fs) * 0.5f),
                label.ToUpperInvariant(), U32(col), trackPx);
        }

        return clicked;
    }

    // 78x20 state combo (Enable / Disable / Inherit) with a gold-bordered popup.
    /// <summary>State value for the boutique state combo.</summary>
    public enum StateValue { Enable = 0, Disable = 1, Inherit = 2 }

    /// <summary>
    /// Draw the 78x20 state combo body. Returns true if the combo was clicked
    /// (caller should then open a popup using the supplied id and render
    /// option items via DrawStatePopupItem).
    /// </summary>
    public static bool DrawStateComboBody(ImDrawListPtr dl, Vector2 min, Vector2 max,
        StateValue state, bool open, float scale, string id)
    {
        ImGui.SetCursorScreenPos(min);
        bool clicked = ImGui.InvisibleButton($"##bsc_{id}", max - min);
        bool hovered = ImGui.IsItemHovered();

        dl.AddRectFilled(min, max, U32(PillBgDeep));
        Vector4 borderC = (open || hovered) ? GoldDeep : BorderSoft;
        dl.AddRect(min, max, U32(borderC), 0f, ImDrawFlags.None, 1f * scale);

        string label = state switch
        {
            StateValue.Enable  => "ENABLE",
            StateValue.Disable => "DISABLE",
            _                  => "INHERIT",
        };

        Vector4 inkColor = state switch
        {
            StateValue.Enable  => Text,
            StateValue.Disable => TextFaint,
            _                  => TextFaint,
        };
        if (open) inkColor = GoldWarm;
        else if (hovered) inkColor = Text;

        using (Kicker9?.Push())
        {
            float fs = ImGui.GetFontSize();
            float trackPx = Track22(fs);
            float midY = (min.Y + max.Y) * 0.5f;
            float lblW = MeasureTrackedText(label, trackPx);
            // Truncate if needed (e.g. very small scales)
            if (lblW > (max.X - min.X) - 22f * scale)
            {
                label = TruncateTrackedToWidth(label, trackPx, (max.X - min.X) - 22f * scale);
            }
            DrawTrackedText(dl, new Vector2(min.X + 8f * scale, midY - fs * 0.5f),
                label, U32(inkColor), trackPx);
        }

        // Chevron
        float chR = 3f * scale;
        var chC = new Vector2(max.X - 7f * scale - chR, (min.Y + max.Y) * 0.5f);
        Vector4 chevColour = WithAlpha(inkColor, 0.6f);
        dl.AddTriangleFilled(
            chC + new Vector2(-chR, -chR * 0.5f),
            chC + new Vector2( chR, -chR * 0.5f),
            chC + new Vector2(0f, chR * 0.7f),
            U32(chevColour));

        return clicked;
    }

    /// <summary>
    /// One row of the state-popup. Renders a coloured dot, the option name,
    /// and a small descriptor. Returns true on click.
    /// </summary>
    public static bool DrawStatePopupItem(ImDrawListPtr dl, float width, float scale,
        StateValue value, bool selected, string id)
    {
        float h = 26f * scale;
        var pos = ImGui.GetCursorScreenPos();
        var min = pos;
        var max = pos + new Vector2(width, h);

        ImGui.SetCursorScreenPos(min);
        bool clicked = ImGui.InvisibleButton($"##bsp_{id}", new Vector2(width, h));
        bool hovered = ImGui.IsItemHovered();

        if (selected)
        {
            dl.AddRectFilled(min, max, U32(WithAlpha(Gold, 0.06f)));
            dl.AddRectFilled(min, new Vector2(min.X + 2f * scale, max.Y), U32(Gold));
        }
        else if (hovered)
        {
            dl.AddRectFilled(min, max, U32(WithAlpha(Gold, 0.10f)));
        }

        // Colour dot
        float dotR = 4f * scale;
        Vector4 dotCol = value switch
        {
            StateValue.Enable  => Gold,
            StateValue.Disable => Red,
            _                  => TextFaint,
        };
        var dotC = new Vector2(min.X + 12f * scale + dotR, (min.Y + max.Y) * 0.5f);
        dl.AddCircleFilled(dotC, dotR, U32(dotCol), 12);
        if (value != StateValue.Inherit)
            dl.AddCircleFilled(dotC, dotR + 1.5f * scale, U32(WithAlpha(dotCol, 0.30f)), 16);

        // Option label
        string label = value switch
        {
            StateValue.Enable  => "ENABLE",
            StateValue.Disable => "DISABLE",
            _                  => "INHERIT",
        };
        string desc = value switch
        {
            StateValue.Enable  => "force on",
            StateValue.Disable => "force off",
            _                  => "parent col.",
        };

        using (Kicker9?.Push())
        {
            float fs = ImGui.GetFontSize();
            float trackPx = Track26(fs);
            Vector4 col = selected ? GoldWarm : Text;
            DrawTrackedText(dl,
                new Vector2(min.X + 12f * scale + dotR * 2 + 8f * scale, min.Y + (h - fs) * 0.5f),
                label, U32(col), trackPx);
        }

        // Right-aligned descriptor in body font
        using (Body12?.Push())
        {
            float fs = ImGui.GetFontSize();
            float dW = ImGui.CalcTextSize(desc).X;
            dl.AddText(new Vector2(max.X - 10f * scale - dW, min.Y + (h - fs) * 0.5f),
                U32(TextFaint), desc);
        }

        return clicked;
    }

    /// <summary>
    /// Push the popup window styles for a boutique gold-bordered popup body.
    /// Pop with PopBoutiquePopupStyles after rendering.
    /// </summary>
    public static void PushBoutiquePopupStyles(float scale, Vector4? borderTint = null)
    {
        ImGui.PushStyleColor(ImGuiCol.PopupBg, PopupBg);
        ImGui.PushStyleColor(ImGuiCol.Border, WithAlpha(borderTint ?? Gold, 0.85f));
        ImGui.PushStyleColor(ImGuiCol.Text, Text);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0f, 4f * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 1f * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
    }

    public static void PopBoutiquePopupStyles()
    {
        ImGui.PopStyleVar(4);
        ImGui.PopStyleColor(3);
    }
}

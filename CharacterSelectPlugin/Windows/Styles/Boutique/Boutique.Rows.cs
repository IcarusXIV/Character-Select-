using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace CharacterSelectPlugin.Windows.Styles;

public static partial class Boutique
{
    // ── Sidebar column header ──────────────────────────────────────────
    /// <summary>
    /// Sidebar header strip with a tracked-caps gold-warm label and a soft
    /// bottom hairline. Used at the top of every boutique sidebar.
    /// </summary>
    public static void DrawSidebarColumnHead(string label, float scale)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        float w = ImGui.GetContentRegionAvail().X;
        float h = CategoryRowH * scale;
        var max = pos + new Vector2(w, h);

        dl.AddRectFilled(pos, max, U32(new Vector4(0f, 0f, 0f, 0.30f)));
        dl.AddLine(new Vector2(pos.X, max.Y - 1f * scale),
                   new Vector2(max.X, max.Y - 1f * scale),
                   U32(BorderSoft), 1f * scale);

        using (Kicker11?.Push())
        {
            float fs = ImGui.GetFontSize();
            float trackPx = Track32(fs);
            float labelY = pos.Y + (h - fs) * 0.5f;
            DrawTrackedText(dl, new Vector2(pos.X + 12f * scale, labelY),
                label, U32(GoldWarm), trackPx);
        }
        ImGui.Dummy(new Vector2(w, h));
    }

    // ── Sidebar category row ───────────────────────────────────────────
    /// <summary>
    /// Sidebar entry row with optional icon, name, count, and active marker.
    /// Active state: gold-at-12% to gold-at-2% horizontal gradient + 2px gold
    /// left bar (with 6px halo).
    /// </summary>
    public static bool DrawSidebarCategoryRow(string id, string name, int count, bool isActive,
        float scale, ImFontPtr iconFont, float iconFontSize, string? iconGlyph = null,
        Vector4? iconColor = null)
    {
        var dl = ImGui.GetWindowDrawList();
        float w = ImGui.GetContentRegionAvail().X;
        float h = (CategoryRowH - 2f) * scale;
        var pos = ImGui.GetCursorScreenPos();
        var max = pos + new Vector2(w, h);

        ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGui.InvisibleButton($"##bcat_{id}", new Vector2(w, h));
        bool hovered = ImGui.IsItemHovered();

        if (isActive)
        {
            uint l = U32(WithAlpha(Gold, AlphaActiveStart));
            uint r = U32(WithAlpha(Gold, 0f));
            dl.AddRectFilledMultiColor(pos, max, l, r, r, l);
            // Gold left bar with halo
            dl.AddRectFilled(new Vector2(pos.X, pos.Y + 4f * scale),
                             new Vector2(pos.X + 2f * scale, max.Y - 4f * scale),
                             U32(Gold));
            // Soft halo
            for (int i = 1; i <= 3; i++)
            {
                float pad = i * 1.5f * scale;
                dl.AddRectFilled(
                    new Vector2(pos.X - pad, pos.Y + 4f * scale - pad),
                    new Vector2(pos.X + 2f * scale + pad, max.Y - 4f * scale + pad),
                    U32(WithAlpha(Gold, 0.12f / i)));
            }
        }
        else if (hovered)
        {
            dl.AddRectFilled(pos, max, U32(WithAlpha(Gold, AlphaRowHoverTint)));
        }

        float xCursor = pos.X + 14f * scale;

        // Icon glyph
        if (!string.IsNullOrEmpty(iconGlyph))
        {
            ImGui.PushFont(iconFont);
            var sz = ImGui.CalcTextSize(iconGlyph);
            ImGui.PopFont();
            float scaleR = iconFontSize / iconFont.FontSize;
            Vector4 iconCol = isActive ? Gold : (iconColor ?? TextFaint);
            var iconPos = new Vector2(xCursor, pos.Y + (h - sz.Y * scaleR) * 0.5f);
            dl.AddText(iconFont, iconFontSize, iconPos, U32(iconCol), iconGlyph);
            xCursor += sz.X * scaleR + 10f * scale;
        }

        // Name in body font
        using (Body12?.Push())
        {
            float fs = ImGui.GetFontSize();
            Vector4 nameCol = isActive ? GoldWarm : Text;
            float maxNameW = max.X - xCursor - 50f * scale; // reserve for count
            string nameDisplay = TruncateToWidth(name, maxNameW);
            dl.AddText(new Vector2(xCursor, pos.Y + (h - fs) * 0.5f),
                U32(nameCol), nameDisplay);
        }

        // Count tracked-caps right-aligned
        string countStr = count.ToString();
        using (Kicker9?.Push())
        {
            float fs = ImGui.GetFontSize();
            float trackPx = Track18(fs);
            float cw = MeasureTrackedText(countStr, trackPx);
            float cy = pos.Y + (h - fs) * 0.5f;
            DrawTrackedText(dl, new Vector2(max.X - 14f * scale - cw, cy),
                countStr, U32(isActive ? GoldWarm : TextDim), trackPx);
        }

        return clicked;
    }

    /// <summary>
    /// Mod row chassis: hover wash + optional green left tick. Caller advances
    /// the cursor to (rowMin.X, rowMax.Y) after drawing the row body.
    /// </summary>
    public static (Vector2 rowMin, Vector2 rowMax, bool rowHovered) DrawTableRowChassis(
        ImDrawListPtr dl, float width, float scale, bool isLive)
    {
        float h = ModRowHeight * scale;
        var pos = ImGui.GetCursorScreenPos();
        var min = pos;
        var max = new Vector2(min.X + width, min.Y + h);

        bool hovered = ImGui.IsMouseHoveringRect(min, max);
        if (hovered)
        {
            dl.AddRectFilled(min, max, U32(new Vector4(1f, 1f, 1f, AlphaRowHover)));
        }
        if (isLive)
        {
            dl.AddRectFilled(
                new Vector2(min.X, min.Y + 6f * scale),
                new Vector2(min.X + 1f * scale, max.Y - 6f * scale),
                U32(WithAlpha(Green, 0.55f)));
        }

        return (min, max, hovered);
    }

    // ── Restricted divider (amber dashed top + tracked-caps body) ──────
    /// <summary>
    /// Lightened restricted-section divider used to introduce design-scoped
    /// (Ctrl-click) mods inside the "Currently Affecting You" view. Thin
    /// amber dashed top border, tracked-caps Oswald body copy, optional
    /// right-side counter.
    /// </summary>
    public static void DrawRestrictedDivider(string body, string? rightLabel, float scale)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        float w = ImGui.GetContentRegionAvail().X;
        float padY = 5f * scale;
        float fs = 11f * scale;
        float h = padY * 2 + fs + 4f * scale;
        var min = pos;
        var max = pos + new Vector2(w, h);

        // Amber dashed top border
        DrawDashedHairline(dl, new Vector2(min.X + 14f * scale, min.Y),
            w - 28f * scale, scale, WithAlpha(NpAmber, 0.30f));

        float midY = min.Y + h * 0.5f;

        // Warning glyph (triangle-exclamation)
        var iconFont = UiBuilder.IconFont;
        string warnGlyph = FontAwesomeIcon.ExclamationTriangle.ToIconString();
        ImGui.PushFont(iconFont);
        var iconSz = ImGui.CalcTextSize(warnGlyph);
        ImGui.PopFont();
        float iconPx = 11f * scale;
        float scaleR = iconPx / iconFont.FontSize;
        var iconPos = new Vector2(min.X + 14f * scale, midY - iconSz.Y * scaleR * 0.5f);
        dl.AddText(iconFont, iconPx, iconPos, U32(WithAlpha(NpAmber, 0.85f)), warnGlyph);

        float xCursor = iconPos.X + iconSz.X * scaleR + 8f * scale;

        // Body copy in tracked-caps Oswald
        using (Kicker9?.Push())
        {
            float fontH = ImGui.GetFontSize();
            float trackPx = Track28(fontH);
            DrawTrackedText(dl, new Vector2(xCursor, midY - fontH * 0.5f),
                body.ToUpperInvariant(), U32(TextFaint), trackPx);
        }

        // Right-side counter
        if (!string.IsNullOrEmpty(rightLabel))
        {
            using (Kicker9?.Push())
            {
                float fontH = ImGui.GetFontSize();
                float trackPx = Track30(fontH);
                float labelW = MeasureTrackedText(rightLabel.ToUpperInvariant(), trackPx);
                DrawTrackedText(dl,
                    new Vector2(max.X - 14f * scale - labelW, midY - fontH * 0.5f),
                    rightLabel.ToUpperInvariant(), U32(TextGhost), trackPx);
            }
        }

        ImGui.Dummy(new Vector2(w, h));
    }

    // ── Found-N caption (small tracked-caps line above a list) ──────────
    /// <summary>
    /// Tracked-caps Oswald caption: "FOUND <N> MATCHES IN <CATEGORY>". Uses
    /// green-soft for the count to read as a positive result indicator.
    /// </summary>
    public static void DrawFoundNCaption(int count, string suffix, float scale)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        float padX = 14f * scale;
        float padY = 4f * scale;

        using (Kicker9?.Push())
        {
            float fs = ImGui.GetFontSize();
            float trackPx = Track28(fs);
            float y = pos.Y + padY;
            float xCursor = pos.X + padX;

            string prefix = "FOUND ";
            DrawTrackedText(dl, new Vector2(xCursor, y), prefix, U32(TextFaint), trackPx);
            xCursor += MeasureTrackedText(prefix, trackPx);

            string countStr = count.ToString();
            DrawTrackedText(dl, new Vector2(xCursor, y), countStr, U32(GreenSoft), trackPx);
            xCursor += MeasureTrackedText(countStr, trackPx);

            string tail = $" {suffix.ToUpperInvariant()}";
            DrawTrackedText(dl, new Vector2(xCursor, y), tail, U32(TextFaint), trackPx);

            ImGui.Dummy(new Vector2(0f, fs + padY * 2));
        }
    }
}

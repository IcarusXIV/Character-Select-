using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CharacterSelectPlugin.Windows.Styles;

public static partial class Boutique
{
    // ── Gold pill (the canonical save/apply button) ─────────────────────
    // Chamfered TR + BL (slip silhouette), gold gradient body, top-edge
    // gold-bright highlight, drop-shadow halo via aurora-spot stack on
    // hover, optional sheen sweep driven by a sheen tracker. The "one big
    // gold moment" of any window the mockup law calls for.

    /// <summary>Measure the natural width of a gold pill for the given label and tracking.</summary>
    public static Vector2 MeasureGoldPill(string label, float trackPx, float scale)
    {
        float padX = 22f * scale;
        float h = ApplyPillHeight * scale;
        float w = MeasureTrackedText(label, trackPx) + padX * 2;
        return new Vector2(w, h);
    }

    /// <summary>Draw a gold pill background (no click handling). Used for the Apply button.</summary>
    public static void DrawGoldPillBody(ImDrawListPtr dl, Vector2 min, Vector2 max,
        string label, float trackPx, float scale, bool hovered)
    {
        float chamfer = ChamSm * scale;
        Span<Vector2> pts = stackalloc Vector2[6];
        BuildSlipPolygon(min, max, chamfer, pts);

        // Pill body. Buttons category drives the fill / highlight / halo.
        // Default gold gradient; seasonal themes substitute their primary
        // palette so Save / Apply pills match the active decoration.
        Vector4 goldDefault     = new(1f, 214f / 255f,   0f, 1f);
        Vector4 goldWarmDefault = new(1f, 200f / 255f, 61f / 255f, 1f);
        Vector4 goldHoverTop    = new(1f, 0.89f, 0.40f, 1f);
        Vector4 goldHighlight   = new(1f, 241f / 255f, 168f / 255f, 1f);
        var pInst = Plugin.Instance;
        if (pInst?.Configuration != null && SeasonalThemeManager.IsSeasonalThemeEnabled(pInst.Configuration))
        {
            switch (SeasonalThemeManager.GetEffectiveTheme(pInst.Configuration))
            {
                case SeasonalTheme.Halloween:
                    goldDefault     = new Vector4(0.95f, 0.45f, 0.10f, 1f);
                    goldWarmDefault = new Vector4(0.95f, 0.50f, 0.15f, 1f);
                    goldHoverTop    = new Vector4(1.00f, 0.55f, 0.20f, 1f);
                    goldHighlight   = new Vector4(1.00f, 0.70f, 0.30f, 1f);
                    break;
                case SeasonalTheme.Winter:
                    goldDefault     = new Vector4(0.40f, 0.65f, 0.95f, 1f);
                    goldWarmDefault = new Vector4(0.50f, 0.72f, 0.98f, 1f);
                    goldHoverTop    = new Vector4(0.55f, 0.78f, 1.00f, 1f);
                    goldHighlight   = new Vector4(0.85f, 0.95f, 1.00f, 1f);
                    break;
                case SeasonalTheme.Christmas:
                    goldDefault     = new Vector4(0.85f, 0.20f, 0.18f, 1f);
                    goldWarmDefault = new Vector4(0.95f, 0.25f, 0.22f, 1f);
                    goldHoverTop    = new Vector4(1.00f, 0.30f, 0.25f, 1f);
                    goldHighlight   = new Vector4(1.00f, 0.55f, 0.45f, 1f);
                    break;
                case SeasonalTheme.Valentines:
                    goldDefault     = new Vector4(0.95f, 0.30f, 0.55f, 1f);
                    goldWarmDefault = new Vector4(1.00f, 0.40f, 0.60f, 1f);
                    goldHoverTop    = new Vector4(1.00f, 0.45f, 0.65f, 1f);
                    goldHighlight   = new Vector4(1.00f, 0.65f, 0.78f, 1f);
                    break;
            }
        }
        Vector4 topCol = hovered
            ? SlotOrDefault("color.buttonHovered", goldHoverTop)
            : SlotOrDefault("color.button",        goldWarmDefault);
        Vector4 botCol = SlotOrDefault("color.button", goldDefault);
        var midCol = Lerp(topCol, botCol, 0.5f);
        unsafe
        {
            fixed (Vector2* p = pts)
                dl.AddConvexPolyFilled(p, 6, U32(midCol));
        }

        // Top-edge highlight (1px line just below the chamfer). Lifts toward
        // white from the resolved button colour so it follows the user's
        // override instead of always reading as bright gold.
        Vector4 highlightCol = SlotOrDefault("color.button", goldHighlight);
        Vector4 brightHighlight = Lerp(highlightCol, new Vector4(1f, 1f, 1f, 1f), 0.55f);
        float chamPx = MathF.Min(chamfer, (max.Y - min.Y) * 0.5f);
        var hlMin = new Vector2(min.X + chamPx + 2f * scale, min.Y);
        var hlMax = new Vector2(max.X - chamPx - 2f * scale, min.Y + 1f * scale);
        dl.AddRectFilled(hlMin, hlMax, U32(WithAlpha(brightHighlight, 0.85f)));

        // Drop-shadow halo on hover (concentric chamfered rings, low alpha).
        // Halo follows the button colour so the Buttons override drives the
        // hover glow as well.
        if (hovered)
        {
            Vector4 haloCol = SlotOrDefault("color.button", goldDefault);
            for (int i = 1; i <= 3; i++)
            {
                float pad = i * 2.5f * scale;
                var hMin = new Vector2(min.X - pad, min.Y - pad);
                var hMax = new Vector2(max.X + pad, max.Y + pad);
                FillSlip(dl, hMin, hMax, chamfer + pad * 0.5f,
                    U32(WithAlpha(haloCol, 0.10f / i)));
            }
        }

        // Label centred. Default ink is very dark chocolate (mockup #1a1500)
        // so it reads as printed-on-gold; the editor's "Button Label" entry
        // can retint it independently of the button fill.
        Vector4 inkDefault = new(26f / 255f, 21f / 255f, 0f, 1f);
        Vector4 inkCol = SlotOrDefault("custom.button.text", inkDefault);
        float labelW = MeasureTrackedText(label, trackPx);
        var fontH = ImGui.GetFontSize();
        var labelPos = new Vector2(
            min.X + ((max.X - min.X) - labelW) * 0.5f,
            min.Y + ((max.Y - min.Y) - fontH) * 0.5f);
        DrawTrackedText(dl, labelPos, label, U32(inkCol), trackPx);
    }

    /// <summary>Sheen sweep overlay on a gold pill (caller supplies progress 0..1, -1 = no sheen).</summary>
    public static void DrawGoldPillSheen(ImDrawListPtr dl, Vector2 min, Vector2 max, float progress)
    {
        if (progress < 0f || progress > 1f) return;
        float w = max.X - min.X;
        float h = max.Y - min.Y;
        float bandW = w * 0.30f;
        float startX = min.X - bandW + (w + bandW) * progress;

        // 12px tall horizontal slices to approximate the parallelogram sheen
        // sweep (tilt is small so a vertical strip is close enough at this size).
        int slices = 8;
        for (int s = 0; s < slices; s++)
        {
            float t = s / (float)(slices - 1);
            float bandX = startX + t * bandW;
            float sliceY1 = min.Y + h * (s / (float)slices);
            float sliceY2 = min.Y + h * ((s + 1) / (float)slices);
            float alpha = 0.28f * MathF.Sin(MathF.PI * (s / (float)(slices - 1)));
            uint clear = U32(WithAlpha(Vector4.One, 0f));
            uint warm = U32(WithAlpha(Vector4.One, alpha));
            dl.AddRectFilledMultiColor(
                new Vector2(bandX - 14f, sliceY1),
                new Vector2(bandX + 14f, sliceY2),
                clear, warm, warm, clear);
        }
    }

    /// <summary>Click + drawing wrapper for the canonical Apply pill. Returns true on click.</summary>
    public static bool DrawApplyPill(ImDrawListPtr dl, Vector2 min, Vector2 max,
        string label, float trackPx, float scale, string id,
        Func<string, bool, float>? sheenProvider = null)
    {
        ImGui.SetCursorScreenPos(min);
        bool clicked = ImGui.InvisibleButton($"##bapply_{id}", max - min);
        bool hovered = ImGui.IsItemHovered();

        DrawGoldPillBody(dl, min, max, label, trackPx, scale, hovered);

        if (sheenProvider != null)
        {
            float t = sheenProvider($"bapply_{id}", hovered);
            if (t >= 0f) DrawGoldPillSheen(dl, min, max, t);
        }
        return clicked;
    }

    // ── Cancel button (chamfered, neutral) ──────────────────────────────

    /// <summary>Draw a chamfered cancel button. Returns true on click.</summary>
    public static bool DrawCancelBtn(ImDrawListPtr dl, Vector2 min, Vector2 max,
        string label, float trackPx, float scale, string id)
    {
        ImGui.SetCursorScreenPos(min);
        bool clicked = ImGui.InvisibleButton($"##bcancel_{id}", max - min);
        bool hovered = ImGui.IsItemHovered();

        float chamfer = ChamCancel * scale;
        Span<Vector2> pts = stackalloc Vector2[6];
        BuildSlipPolygon(min, max, chamfer, pts);

        Vector4 bgCol = hovered
            ? new Vector4(0.11f, 0.13f, 0.17f, 0.92f)
            : new Vector4(0.08f, 0.10f, 0.13f, 0.85f);
        unsafe
        {
            fixed (Vector2* p = pts)
                dl.AddConvexPolyFilled(p, 6, U32(bgCol));
        }
        for (int i = 0; i < 6; i++) dl.PathLineTo(pts[i]);
        dl.PathStroke(U32(hovered ? TextDim : BorderSoft), ImDrawFlags.Closed, 1f * scale);

        Vector4 inkCol = hovered ? GoldBright : Text;
        float labelW = MeasureTrackedText(label, trackPx);
        float fh = ImGui.GetFontSize();
        var labelPos = new Vector2(
            min.X + ((max.X - min.X) - labelW) * 0.5f,
            min.Y + ((max.Y - min.Y) - fh) * 0.5f);
        DrawTrackedText(dl, labelPos, label, U32(inkCol), trackPx);

        return clicked;
    }

    // ── Mini button (chamfered, 26px tall, used in main-head action row) ─

    /// <summary>Measure the natural size of a mini button.</summary>
    public static Vector2 MeasureMiniBtn(string label, float trackPx, float scale, bool hasIcon = false)
    {
        float padX = 12f * scale;
        float gap = hasIcon ? 6f * scale : 0f;
        float iconW = hasIcon ? ImGui.GetFontSize() * 0.8f : 0f;
        float labelW = MeasureTrackedText(label, trackPx);
        float h = MiniBtnHeight * scale;
        return new Vector2(padX * 2 + iconW + gap + labelW, h);
    }

    /// <summary>
    /// Compact secondary action button: chamfered (5px), tracked-caps Oswald
    /// label, optional icon glyph drawn at gold-deep before the label.
    /// Returns true on click.
    /// </summary>
    public static bool DrawMiniBtn(ImDrawListPtr dl, Vector2 pos, Vector2 size,
        string label, float trackPx, float scale, string id,
        ImFontPtr iconFont, float iconFontSize, string? iconGlyph = null)
    {
        ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGui.InvisibleButton($"##bmini_{id}", size);
        bool hovered = ImGui.IsItemHovered();

        var min = pos;
        var max = pos + size;
        float chamfer = ChamMini * scale;

        Span<Vector2> pts = stackalloc Vector2[6];
        BuildSlipPolygon(min, max, chamfer, pts);
        Vector4 bg = hovered
            ? SlotOrDefault("color.buttonHovered", new Vector4(28f / 255f, 32f / 255f, 42f / 255f, 0.85f))
            : SlotOrDefault("color.button",        new Vector4(20f / 255f, 24f / 255f, 32f / 255f, 0.6f));
        unsafe
        {
            fixed (Vector2* p = pts)
                dl.AddConvexPolyFilled(p, 6, U32(bg));
        }
        for (int i = 0; i < 6; i++) dl.PathLineTo(pts[i]);
        dl.PathStroke(U32(hovered ? GoldDeep : BorderSoft), ImDrawFlags.Closed, 1f * scale);

        float padX = 12f * scale;
        float gap = 6f * scale;
        float fh = ImGui.GetFontSize();
        float midY = (min.Y + max.Y) * 0.5f;

        float xCursor = min.X + padX;
        if (!string.IsNullOrEmpty(iconGlyph))
        {
            ImGui.PushFont(iconFont);
            var iconSz = ImGui.CalcTextSize(iconGlyph);
            ImGui.PopFont();
            float scaledIconH = iconFontSize / iconFont.FontSize * iconSz.Y;
            var iconPos = new Vector2(xCursor, midY - scaledIconH * 0.5f);
            Vector4 iconInk = hovered ? Gold : GoldDeep;
            dl.AddText(iconFont, iconFontSize, iconPos, U32(iconInk), iconGlyph);
            xCursor += iconFontSize / iconFont.FontSize * iconSz.X + gap;
        }

        Vector4 inkCol = hovered ? GoldWarm : TextDim;
        DrawTrackedText(dl, new Vector2(xCursor, midY - fh * 0.5f),
            label, U32(inkCol), trackPx);

        return clicked;
    }

    // ── Icon button (square, optional sized variant) ────────────────────

    /// <summary>30x30 icon-only button. Returns true on click.</summary>
    public static bool DrawIconButton30(ImDrawListPtr dl, Vector2 min, float scale, string id,
        ImFontPtr iconFont, float iconFontSize, string glyph,
        Vector4 hoverInk, string? tooltip = null)
    {
        return DrawIconButtonSized(dl, min, IconBtnSide30, scale, id,
            iconFont, iconFontSize, glyph, hoverInk, tooltip);
    }

    /// <summary>Square icon-only button at the supplied side (30 or 26 typically). Returns true on click.</summary>
    public static bool DrawIconButtonSized(ImDrawListPtr dl, Vector2 min, float side, float scale, string id,
        ImFontPtr iconFont, float iconFontSize, string glyph,
        Vector4 hoverInk, string? tooltip = null)
    {
        float sidePx = side * scale;
        var max = min + new Vector2(sidePx, sidePx);

        ImGui.SetCursorScreenPos(min);
        bool clicked = ImGui.InvisibleButton($"##bicon_{id}", new Vector2(sidePx, sidePx));
        bool hovered = ImGui.IsItemHovered();

        Vector4 bgFill = hovered
            ? SlotOrDefault("color.buttonHovered", Surface1)
            : SlotOrDefault("color.button",        PillBg);
        dl.AddRectFilled(min, max, U32(bgFill));
        var borderCol = U32(hovered ? WithAlpha(hoverInk, 0.85f) : BorderSoft);
        dl.AddRect(min, max, borderCol, 0f, ImDrawFlags.None, 1f * scale);

        var inkCol = hovered ? hoverInk : TextDim;
        ImGui.PushFont(iconFont);
        Vector2 iconSize = ImGui.CalcTextSize(glyph);
        ImGui.PopFont();
        float scaleR = iconFontSize / iconFont.FontSize;
        var iconPos = new Vector2(
            min.X + (sidePx - iconSize.X * scaleR) * 0.5f,
            min.Y + (sidePx - iconSize.Y * scaleR) * 0.5f);
        dl.AddText(iconFont, iconFontSize, iconPos, U32(inkCol), glyph);

        if (hovered && !string.IsNullOrEmpty(tooltip)) Tooltip(tooltip);
        return clicked;
    }

    // ── Cluster icon (18x18, used in mod row state pin/gear cluster) ────

    /// <summary>
    /// Tiny 18x18 invisible-button + glyph rendered at the supplied colour.
    /// Active state lifts the colour to "active". Hover lifts the colour to
    /// "hover" and translates the glyph 1px upward. Returns true on click.
    /// </summary>
    public static bool DrawClusterIcon(ImDrawListPtr dl, Vector2 min, float scale, string id,
        ImFontPtr iconFont, float iconFontSize, string glyph,
        Vector4 idleColour, Vector4 hoverColour, string? tooltip = null)
    {
        float side = ClusterIconSide * scale;
        ImGui.SetCursorScreenPos(min);
        bool clicked = ImGui.InvisibleButton($"##bclu_{id}", new Vector2(side, side));
        bool hovered = ImGui.IsItemHovered();

        Vector4 ink = hovered ? hoverColour : idleColour;
        ImGui.PushFont(iconFont);
        Vector2 iconSz = ImGui.CalcTextSize(glyph);
        ImGui.PopFont();
        float scaleR = iconFontSize / iconFont.FontSize;
        var iconPos = new Vector2(
            min.X + (side - iconSz.X * scaleR) * 0.5f,
            min.Y + (side - iconSz.Y * scaleR) * 0.5f);
        if (hovered) iconPos.Y -= 1f * scale;

        dl.AddText(iconFont, iconFontSize, iconPos, U32(ink), glyph);

        if (hovered && !string.IsNullOrEmpty(tooltip)) Tooltip(tooltip);
        return clicked;
    }

    // Page button (plain rect, supports number / icon / current state)

    /// <summary>
    /// 24x24 page button. `current` lifts the border to gold and tints the bg.
    /// `currentT` (0..1) drives a transition flair when a page becomes current;
    /// pass `outgoingT` on the previous page so its gold fades out in sync.
    /// Pass currentT=1 for the settled state.
    /// </summary>
    public static bool DrawPageBtn(ImDrawListPtr dl, Vector2 pos, float scale, string id,
        string label, bool current, bool disabled, float currentT = 1f, float outgoingT = 0f)
    {
        return DrawPageBtnInternal(dl, pos, scale, id, label, current, disabled,
            useIcon: false, default, default!, 0f, currentT, outgoingT);
    }

    /// <summary>Page button variant rendering a FontAwesome icon glyph instead of a numeric label.</summary>
    public static bool DrawPageBtnIcon(ImDrawListPtr dl, Vector2 pos, float scale, string id,
        ImFontPtr iconFont, float iconFontSize, string iconGlyph,
        bool current, bool disabled, float currentT = 1f, float outgoingT = 0f)
    {
        return DrawPageBtnInternal(dl, pos, scale, id, label: "", current, disabled,
            useIcon: true, iconFont, iconGlyph, iconFontSize, currentT, outgoingT);
    }

    private static bool DrawPageBtnInternal(ImDrawListPtr dl, Vector2 pos, float scale, string id,
        string label, bool current, bool disabled,
        bool useIcon, ImFontPtr iconFont, string iconGlyph, float iconFontSize,
        float currentT, float outgoingT)
    {
        float side = PageBtnSide * scale;
        var min = pos;
        var max = pos + new Vector2(side, side);

        ImGui.SetCursorScreenPos(min);
        bool clicked = ImGui.InvisibleButton($"##bpg_{id}", new Vector2(side, side));
        if (disabled) clicked = false;
        bool hovered = !disabled && ImGui.IsItemHovered();

        // Outgoing alpha takes precedence when the previous-current page is
        // animating out, its gold fades from full down to zero across the
        // transition. New current animates in via currentT.
        float goldT = current ? currentT : (outgoingT > 0f ? outgoingT : 0f);

        // Active-page accent: respects custom.pageButtonActive when set,
        // otherwise falls back to the master-driven Gold token.
        Vector4 activeAccent = Gold;
        var pInst = Plugin.Instance;
        if (pInst?.Configuration?.SelectedTheme == ThemeSelection.Custom &&
            pInst.Configuration.CustomTheme != null &&
            pInst.Configuration.CustomTheme.ColorOverrides.TryGetValue("custom.pageButtonActive", out var pageActivePacked) &&
            pageActivePacked.HasValue)
        {
            activeAccent = CustomThemeDefinitions.UnpackColor(pageActivePacked.Value);
        }

        Vector4 baseBg = hovered
            ? new Vector4(28f / 255f, 32f / 255f, 42f / 255f, 0.85f)
            : PillBg;
        Vector4 bg = Lerp(baseBg, WithAlpha(activeAccent, 0.10f), goldT);
        if (disabled) bg = ScaleAlpha(bg, 0.55f);
        dl.AddRectFilled(min, max, U32(bg));

        Vector4 baseBorder = hovered ? GoldDeep : BorderSoft;
        Vector4 borderC = Lerp(baseBorder, WithAlpha(activeAccent, 0.50f), goldT);
        if (disabled) borderC = WithAlpha(borderC, 0.30f);
        dl.AddRect(min, max, U32(borderC), 0f, ImDrawFlags.None, 1f * scale);

        // Gold halo glow around the active button. Continuously breathes on
        // a ~2s sin cycle (matches the wardrobe pager dot's pulse) while the
        // button is current; new currents ride in via the currentT lerp so
        // the halo pulses into place as the previous active fades. Plus a
        // brief ripple expanding outward when the button just became current.
        if (goldT > 0.01f)
        {
            float t = (float)ImGui.GetTime();
            float breath = 0.55f + 0.45f * MathF.Sin(t * MathF.PI);
            float pad = 3f * scale;
            for (int i = 1; i <= 2; i++)
            {
                float r = i * pad;
                dl.AddRect(
                    new Vector2(min.X - r, min.Y - r),
                    new Vector2(max.X + r, max.Y + r),
                    U32(WithAlpha(activeAccent, (0.20f / i) * goldT * (0.55f + 0.45f * breath))),
                    0f, ImDrawFlags.None, 1f * scale);
            }
            // Transition ripple: while currentT is still climbing toward 1
            // (i.e. the button just became current), draw an expanding rect
            // outline that fades as it grows. Wardrobe-style feedback flair.
            if (current && currentT < 0.99f)
            {
                float rip = (1f - currentT) * 12f * scale;
                float ra = MathF.Pow(currentT, 1.5f);
                dl.AddRect(
                    new Vector2(min.X - rip, min.Y - rip),
                    new Vector2(max.X + rip, max.Y + rip),
                    U32(WithAlpha(activeAccent, 0.65f * (1f - ra))),
                    0f, ImDrawFlags.None, 1.5f * scale);
            }
        }

        Vector4 baseInk = hovered ? GoldWarm : TextDim;
        Vector4 inkC = Lerp(baseInk, Gold, goldT);
        if (disabled) inkC = ScaleAlpha(inkC, 0.45f);

        if (useIcon)
        {
            ImGui.PushFont(iconFont);
            var sz = ImGui.CalcTextSize(iconGlyph);
            ImGui.PopFont();
            float scaleR = iconFontSize / iconFont.FontSize;
            var p = new Vector2(min.X + (side - sz.X * scaleR) * 0.5f,
                                min.Y + (side - sz.Y * scaleR) * 0.5f);
            dl.AddText(iconFont, iconFontSize, p, U32(inkC), iconGlyph);
        }
        else if (!string.IsNullOrEmpty(label))
        {
            var sz = ImGui.CalcTextSize(label);
            var p = new Vector2(min.X + (side - sz.X) * 0.5f,
                                min.Y + (side - sz.Y) * 0.5f);
            dl.AddText(p, U32(inkC), label);
        }

        return clicked;
    }
}

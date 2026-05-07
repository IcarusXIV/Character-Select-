using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;

namespace CharacterSelectPlugin.Windows.Styles;

/// <summary>
/// Boutique primitives for the main roster + design panel. Callers push their
/// own font; helpers that need a secondary font take an explicit ImFontPtr.
/// </summary>
public static class BoutiqueChassis
{
    /// <summary>
    /// Boutique progress ring with aura bloom, gold arc, chromatic ghosts,
    /// leading-edge tip, and glitch flair. `displayedRatio` is the arc length
    /// (0..1); `fillProgress` is the animation state (1 = settled, less =
    /// glitch); `fillSeed` keeps glitch positions stable per cycle.
    /// </summary>
    public static void DrawProgressRing(ImDrawListPtr dl,
        Vector2 centre, float scale,
        float ringRadius, float ringThickness,
        float displayedRatio, float fillProgress,
        int fillSeed, float time)
    {
        float s = scale;
        float t = time;
        float ringInnerEdge = ringRadius - ringThickness * 0.5f;

        // ── Aura bloom ──
        {
            float pulseSin = 0.5f + 0.5f * MathF.Sin(t * MathF.Tau / 5f);
            float bloomPulse = 1f + 0.04f * pulseSin;
            float bloomAlpha = 0.90f + 0.10f * pulseSin;
            float maxR = 200f * s * bloomPulse;
            const int bloomLayers = 64;
            const float peakTarget = 0.10f;
            uint bloomCol = ImGui.ColorConvertFloat4ToU32(
                CodexChassis.WithAlpha(CodexChassis.Gold,
                    (peakTarget / bloomLayers) * bloomAlpha));
            for (int i = 0; i < bloomLayers; i++)
            {
                float r = maxR * ((i + 1) / (float)bloomLayers);
                dl.AddCircleFilled(centre, r, bloomCol, 72);
            }
        }

        // ── Ring drop-shadow halo ──
        {
            float ringOuterEdge = ringRadius + ringThickness * 0.5f;
            const int haloLayers = 50;
            const float haloPeak = 0.22f;
            float haloSpread = 30f * s;
            uint haloCol = ImGui.ColorConvertFloat4ToU32(
                CodexChassis.WithAlpha(CodexChassis.Gold, haloPeak / haloLayers));
            for (int i = 0; i < haloLayers; i++)
            {
                float u = (i + 1) / (float)haloLayers;
                float r = ringOuterEdge + haloSpread * u;
                dl.AddCircleFilled(centre, r, haloCol, 84);
            }
        }

        // ── Progress ring (gold/gold-warm arc + chromatic ghosts) ──
        float startAng = -MathF.PI * 0.5f;
        float earnedAng = MathF.Tau * displayedRatio;
        float endAng = startAng + earnedAng;

        if (displayedRatio > 0f)
        {
            if (fillProgress < 1f)
            {
                float chromaA = 1f - fillProgress;
                Vector2 magOff = new Vector2(-3f * s, -2f * s);
                Vector2 cyanOff = new Vector2(3f * s, 2f * s);
                dl.PathClear();
                dl.PathArcTo(centre + magOff, ringRadius, startAng, endAng, 96);
                dl.PathStroke(
                    ImGui.ColorConvertFloat4ToU32(
                        CodexChassis.WithAlpha(CodexChassis.GlitchMagenta, 0.85f * chromaA)),
                    ImDrawFlags.None, ringThickness);
                dl.PathClear();
                dl.PathArcTo(centre + cyanOff, ringRadius, startAng, endAng, 96);
                dl.PathStroke(
                    ImGui.ColorConvertFloat4ToU32(
                        CodexChassis.WithAlpha(CodexChassis.GlitchCyan, 0.85f * chromaA)),
                    ImDrawFlags.None, ringThickness);
            }

            float midAng = startAng + earnedAng * 0.5f;
            dl.PathClear();
            dl.PathArcTo(centre, ringRadius, startAng, midAng, 64);
            dl.PathStroke(
                ImGui.ColorConvertFloat4ToU32(CodexChassis.Gold),
                ImDrawFlags.None, ringThickness);
            dl.PathClear();
            dl.PathArcTo(centre, ringRadius, midAng, endAng, 64);
            dl.PathStroke(
                ImGui.ColorConvertFloat4ToU32(CodexChassis.GoldWarm),
                ImDrawFlags.None, ringThickness);

        }
        if (displayedRatio < 1f)
        {
            dl.PathClear();
            dl.PathArcTo(centre, ringRadius, endAng, startAng + MathF.Tau, 96);
            dl.PathStroke(
                ImGui.ColorConvertFloat4ToU32(CodexChassis.Border),
                ImDrawFlags.None, ringThickness);
        }

        float innerR = ringInnerEdge + 2f * s;

        // ── Inner disc ──
        {
            dl.AddCircleFilled(centre, innerR,
                ImGui.ColorConvertFloat4ToU32(CodexChassis.Surface0), 80);
            float tintMaxR = innerR * 0.70f;
            const int tintLayers = 32;
            const float tintPeak = 0.08f;
            uint tintCol = ImGui.ColorConvertFloat4ToU32(
                CodexChassis.WithAlpha(CodexChassis.Gold, tintPeak / tintLayers));
            for (int i = 0; i < tintLayers; i++)
            {
                float r = tintMaxR * ((i + 1) / (float)tintLayers);
                dl.AddCircleFilled(centre, r, tintCol, 56);
            }
            dl.AddCircle(centre, innerR,
                ImGui.ColorConvertFloat4ToU32(CodexChassis.GoldDark), 80, 1f);
        }

        // ── Glitch dropout bars + specks ──
        if (fillProgress < 1f)
        {
            float fillFlairA = 1f - fillProgress;

            float[] barAt = { 0.25f, 0.65f };
            for (int b = 0; b < barAt.Length; b++)
            {
                float bt = fillProgress - barAt[b];
                if (bt < 0f || bt > 0.18f) continue;
                float bp = bt / 0.18f;
                float a = bp < 0.30f ? bp / 0.30f : 1f - (bp - 0.30f) / 0.70f;
                a = MathF.Max(0f, a) * fillFlairA;
                if (a < 0.02f) continue;
                int hY = RingHashCombine(fillSeed, b * 7919);
                float by = centre.Y + (((hY & 0xFFFF) / 65535f) - 0.5f) * (innerR * 1.2f);
                float bh = 6f * s + (((hY >> 16) & 0xFF) / 255f) * 5f * s;
                float bxLeft = centre.X - innerR;
                float bxRight = centre.X + innerR;
                dl.AddRectFilled(
                    new Vector2(bxLeft, by - 1f * s),
                    new Vector2(bxRight, by),
                    ImGui.ColorConvertFloat4ToU32(
                        CodexChassis.WithAlpha(CodexChassis.GlitchMagenta, 0.80f * a)));
                dl.AddRectFilled(
                    new Vector2(bxLeft, by),
                    new Vector2(bxRight, by + bh),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f * a)));
                dl.AddRectFilled(
                    new Vector2(bxLeft, by + bh),
                    new Vector2(bxRight, by + bh + 1f * s),
                    ImGui.ColorConvertFloat4ToU32(
                        CodexChassis.WithAlpha(CodexChassis.GlitchCyan, 0.80f * a)));
            }

            int speckCount = 18;
            for (int sp = 0; sp < speckCount; sp++)
            {
                int hX = RingHashCombine(fillSeed, sp * 9613 + 3);
                int hY = RingHashCombine(fillSeed, sp * 7283 + 7);
                int hC = RingHashCombine(fillSeed, sp * 5527 + 11);
                int hP = RingHashCombine(fillSeed, sp * 3271 + 17);
                float phase = (hP & 0xFF) / 255f;
                float blink = (t * 12f + phase * 6.28f) % 1f;
                if (blink > 0.40f) continue;
                float sxN = ((hX & 0xFFFF) / 65535f) * 2f - 1f;
                float syN = ((hY & 0xFFFF) / 65535f) * 2f - 1f;
                if (sxN * sxN + syN * syN > 0.85f) continue;
                Vector2 spPos = new Vector2(
                    centre.X + sxN * innerR * 0.85f,
                    centre.Y + syN * innerR * 0.85f);
                uint sc = (hC & 0x3) switch
                {
                    0 => ImGui.ColorConvertFloat4ToU32(
                        CodexChassis.WithAlpha(CodexChassis.GlitchMagenta, 0.92f * fillFlairA)),
                    1 => ImGui.ColorConvertFloat4ToU32(
                        CodexChassis.WithAlpha(CodexChassis.GlitchCyan, 0.92f * fillFlairA)),
                    _ => ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.92f * fillFlairA)),
                };
                float ss = ((hC >> 4) & 1) == 0 ? 1f * s : 2f * s;
                dl.AddRectFilled(spPos, spPos + new Vector2(ss, ss), sc);
            }
        }
    }

    private static int RingHashCombine(int a, int b)
    {
        unchecked { return (a * 397) ^ b; }
    }

    /// <summary>
    /// Plugin-wide tooltip primitive. Forces the same font, padding, bg,
    /// border, and rounding regardless of which window or context the call
    /// came from. Replaces ImGui.SetTooltip everywhere so tooltips no longer
    /// vary in size based on whatever font the caller had pushed.
    /// </summary>
    public static void Tooltip(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 7f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.04f, 0.05f, 0.08f, 0.96f));
        ImGui.PushStyleColor(ImGuiCol.Border, CodexChassis.WithAlpha(CodexChassis.Gold, 0.55f));
        ImGui.PushStyleColor(ImGuiCol.Text, CodexChassis.Text);

        ImGui.BeginTooltip();
        var fontHandle = CharacterSelectPlugin.Plugin.Instance?.OutfitMed13;
        using (fontHandle?.Push())
        {
            ImGui.PushTextWrapPos(380f);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
        }
        ImGui.EndTooltip();

        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(3);
    }

    // ── Nameplate accents (np-* tokens from main.html) ──────────────────
    public static readonly Vector4 NpCyan   = Rgb(0x5A, 0xC7, 0xFF);
    public static readonly Vector4 NpRose   = Rgb(0xFF, 0x73, 0xD1);
    public static readonly Vector4 NpGreen  = Rgb(0x4D, 0xF2, 0x80);
    public static readonly Vector4 NpAmber  = Rgb(0xFF, 0xB8, 0x40);
    public static readonly Vector4 NpViolet = Rgb(0xA3, 0x8F, 0xFF);
    public static readonly Vector4 NpCoral  = Rgb(0xFF, 0x88, 0x70);
    public static readonly Vector4 NpOcean  = Rgb(0x6F, 0xBF, 0xE3);
    public static readonly Vector4 NpSand   = Rgb(0xD9, 0xC9, 0x8B);

    private static readonly Vector4[] NpPalette =
    {
        NpCyan, NpRose, NpGreen, NpAmber, NpViolet, NpCoral, NpOcean, NpSand
    };

    /// <summary>Stable nameplate colour per character index.</summary>
    public static Vector4 NpColorByIndex(int i) => NpPalette[((i % NpPalette.Length) + NpPalette.Length) % NpPalette.Length];

    /// <summary>Translucent dark fill used for icon-button + filter pill backgrounds. Delegates to Boutique.PillBg so the editor's "Input Fields" override drives the search-bar / filter-pill backgrounds in the main roster too.</summary>
    public static Vector4 PillBg => Boutique.PillBg;

    private static Vector4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);

    // ── Window chrome ───────────────────────────────────────────────────

    /// <summary>BL+BR gold L-bracket pair, drawn last so it sits atop content.</summary>
    public static void DrawWindowBrackets(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
    {
        float size = 14f * scale;
        float inset = 6f * scale;
        uint c = CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.Gold, 0.38f));
        var bl = new Vector2(min.X + inset, max.Y - inset);
        dl.AddLine(new Vector2(bl.X, bl.Y - size), bl, c, 1f * scale);
        dl.AddLine(bl, new Vector2(bl.X + size, bl.Y), c, 1f * scale);
        var br = new Vector2(max.X - inset, max.Y - inset);
        dl.AddLine(new Vector2(br.X, br.Y - size), br, c, 1f * scale);
        dl.AddLine(br, new Vector2(br.X - size, br.Y), c, 1f * scale);
    }

    // ── Meta ribbon (30px tall, sits above action bar) ──────────────────

    /// <summary>Draws the dark gradient ribbon background with gold hairlines top + bottom.</summary>
    public static void DrawRibbonBackground(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
    {
        uint top = CodexChassis.U32(CodexChassis.RibbonTop);
        uint bot = CodexChassis.U32(CodexChassis.RibbonBot);
        dl.AddRectFilledMultiColor(min, max, top, top, bot, bot);

        float ruleH = 1f * scale;
        uint goldStrong = CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.Gold, 0.50f));
        uint goldClear  = CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.Gold, 0.0f));
        float w = max.X - min.X;
        dl.AddRectFilledMultiColor(
            min,
            new Vector2(min.X + w * 0.42f, min.Y + ruleH),
            goldStrong, goldClear, goldClear, goldStrong);
        dl.AddRectFilledMultiColor(
            new Vector2(min.X + w * 0.58f, min.Y),
            new Vector2(max.X, min.Y + ruleH),
            goldClear, goldStrong, goldStrong, goldClear);

        uint goldMid = CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.Gold, 0.26f));
        dl.AddRectFilledMultiColor(
            new Vector2(min.X, max.Y - ruleH),
            new Vector2(min.X + w * 0.5f, max.Y),
            goldClear, goldMid, goldMid, goldClear);
        dl.AddRectFilledMultiColor(
            new Vector2(min.X + w * 0.5f, max.Y - ruleH),
            max,
            goldMid, goldClear, goldClear, goldMid);
    }

    /// <summary>Pulsing gold pip, square (matches patch notes / achievements / wardrobe convention).</summary>
    public static void DrawGoldPip(ImDrawListPtr dl, Vector2 centre, float scale, double time)
    {
        float pulse = 0.55f + 0.45f * (float)Math.Sin(time * 2.4);
        var glow = CodexChassis.WithAlpha(CodexChassis.GoldWarm, 0.55f * pulse);
        // Outer glow square (soft), then solid gold core square
        float glowR = 6f * scale;
        float coreR = 3f * scale;
        dl.AddRectFilled(centre - new Vector2(glowR, glowR), centre + new Vector2(glowR, glowR),
            CodexChassis.U32(glow));
        dl.AddRectFilled(centre - new Vector2(coreR, coreR), centre + new Vector2(coreR, coreR),
            CodexChassis.U32(CodexChassis.Gold));
    }

    /// <summary>Small coloured square pip used for the active-name marker.</summary>
    public static void DrawSquarePip(ImDrawListPtr dl, Vector2 centre, float halfSize, Vector4 colour)
    {
        float r = halfSize;
        dl.AddRectFilled(centre - new Vector2(r * 1.6f, r * 1.6f), centre + new Vector2(r * 1.6f, r * 1.6f),
            CodexChassis.U32(CodexChassis.WithAlpha(colour, 0.35f)));
        dl.AddRectFilled(centre - new Vector2(r, r), centre + new Vector2(r, r),
            CodexChassis.U32(colour));
    }

    // ── Gold pill button ────────────────────────────────────────────────

    /// <summary>
    /// Renders a chamfered gold-pill button (TR + BL chamfer 8px), gold-warm → gold
    /// vertical gradient, dark text, "+ LABEL" centred. Caller must push the desired
    /// font before calling. Returns the rect used for hit-testing (anchor → anchor + size).
    /// </summary>
    public static Vector2 DrawGoldPillSize(string label, float trackPx, float scale)
    {
        float padX = 14f * scale;
        float padY = 9f * scale;
        float fontSize = ImGui.GetFontSize();
        float plusW = ImGui.CalcTextSize("+").X;
        float gap = 8f * scale;
        float labelW = MeasureTrackedText(label, trackPx);
        float btnW = padX * 2 + plusW + gap + labelW;
        float btnH = fontSize + padY * 1.2f;
        return new Vector2(btnW, btnH);
    }

    public static void DrawGoldPill(ImDrawListPtr dl, Vector2 min, Vector2 max,
        string label, float trackPx, float scale, bool hovered, bool showPlus = true)
    {
        float cham = 8f * scale;

        // Snap to integer pixels, sub-pixel text rendering blurs.
        min = new Vector2(MathF.Round(min.X), MathF.Round(min.Y));
        max = new Vector2(MathF.Round(max.X), MathF.Round(max.Y));

        // Lift on hover
        if (hovered) { min.Y -= 1f * scale; max.Y -= 1f * scale; }

        // Pill fill follows the Buttons category. Defaults are gold by
        // default; seasonal themes substitute their own primary palette so
        // the Add Character / New Design / Save / Apply pills match.
        Vector4 goldDefault      = Rgb(0xFF, 0xD6, 0x00);
        Vector4 goldHoverDefault = Rgb(0xFF, 0xDB, 0x3A);
        Vector4 goldWarmDefault  = Rgb(0xFF, 0xC8, 0x3D);
        var seasonPlugin = Plugin.Instance;
        if (seasonPlugin?.Configuration != null && SeasonalThemeManager.IsSeasonalThemeEnabled(seasonPlugin.Configuration))
        {
            switch (SeasonalThemeManager.GetEffectiveTheme(seasonPlugin.Configuration))
            {
                case SeasonalTheme.Halloween:
                    goldDefault      = new Vector4(0.95f, 0.45f, 0.10f, 1f);
                    goldHoverDefault = new Vector4(1.00f, 0.55f, 0.15f, 1f);
                    goldWarmDefault  = new Vector4(0.95f, 0.50f, 0.15f, 1f);
                    break;
                case SeasonalTheme.Winter:
                    goldDefault      = new Vector4(0.40f, 0.65f, 0.95f, 1f);
                    goldHoverDefault = new Vector4(0.55f, 0.78f, 1.00f, 1f);
                    goldWarmDefault  = new Vector4(0.50f, 0.72f, 0.98f, 1f);
                    break;
                case SeasonalTheme.Christmas:
                    goldDefault      = new Vector4(0.85f, 0.20f, 0.18f, 1f);
                    goldHoverDefault = new Vector4(1.00f, 0.30f, 0.25f, 1f);
                    goldWarmDefault  = new Vector4(0.95f, 0.25f, 0.22f, 1f);
                    break;
                case SeasonalTheme.Valentines:
                    goldDefault      = new Vector4(0.95f, 0.30f, 0.55f, 1f);
                    goldHoverDefault = new Vector4(1.00f, 0.45f, 0.65f, 1f);
                    goldWarmDefault  = new Vector4(1.00f, 0.40f, 0.60f, 1f);
                    break;
            }
        }
        // Gold pill follows the Primary Accent slot; hover lerps the resolved primary 18% toward white.
        Vector4 primary = Boutique.SlotOrDefault("custom.accent.primary", goldDefault);
        Vector4 hoverFromPrimary = CodexChassis.Lerp(primary, new Vector4(1f, 1f, 1f, 1f), 0.18f);
        Vector4 fillCol = hovered ? hoverFromPrimary : primary;
        Span<Vector2> pts = stackalloc Vector2[6];
        CodexChassis.BuildSlipPolygon(min, max, cham, pts);
        unsafe
        {
            fixed (Vector2* p = pts)
                dl.AddConvexPolyFilled(p, 6, CodexChassis.U32(fillCol));
        }

        // 1px top-edge highlight, lerped from the resolved primary toward white.
        Vector4 highlightCol = primary;
        Vector4 brightHighlight = CodexChassis.Lerp(highlightCol, new Vector4(1f, 1f, 1f, 1f), 0.30f);
        var hlMin = new Vector2(min.X + cham, min.Y);
        var hlMax = new Vector2(max.X - cham, min.Y + 1f * scale);
        dl.AddRectFilled(hlMin, hlMax, CodexChassis.U32(CodexChassis.WithAlpha(brightHighlight, 0.85f)));

        // Centre the (optional "+") + LABEL group inside the pill. Icon and
        // label colours are independently overridable via the editor's
        // "Button Icon" and "Button Label" entries; both default to the dark
        // chocolate ink the mockup specifies.
        float fontSize = ImGui.GetFontSize();
        Vector4 inkDefault = Rgb(0x1A, 0x15, 0x00);
        uint iconCol  = CodexChassis.U32(Boutique.SlotOrDefault("custom.button.icon", inkDefault));
        uint labelCol = CodexChassis.U32(Boutique.SlotOrDefault("custom.button.text", inkDefault));
        float labelW = MeasureTrackedText(label, trackPx);
        float btnW = max.X - min.X;
        float btnH = max.Y - min.Y;
        float yText = MathF.Round(min.Y + (btnH - fontSize) * 0.5f);
        if (showPlus)
        {
            float plusW = ImGui.CalcTextSize("+").X;
            float gap = 8f * scale;
            float totalContentW = plusW + gap + labelW;
            float xText = MathF.Round(min.X + (btnW - totalContentW) * 0.5f);
            dl.AddText(new Vector2(xText, yText), iconCol, "+");
            DrawTrackedText(dl, new Vector2(xText + plusW + gap, yText), label, labelCol, trackPx);
        }
        else
        {
            float xText = MathF.Round(min.X + (btnW - labelW) * 0.5f);
            DrawTrackedText(dl, new Vector2(xText, yText), label, labelCol, trackPx);
        }
    }

    // ── 30×30 icon button ───────────────────────────────────────────────

    /// <summary>
    /// Draws a 30×30 axis-aligned icon button with the standard rgba(20,24,32,0.6) fill,
    /// border-soft 1px stroke, hover-tinted ink. Renders the FontAwesome glyph centred.
    /// `iconFont` is the FontAwesome font handle (UiBuilder.IconFont) at its size.
    /// </summary>
    /// <summary>Sized variant of DrawIconButton30, caller supplies the side length.</summary>
    public static void DrawIconButtonSized(ImDrawListPtr dl, Vector2 min, float side, float scale,
        ImFontPtr iconFont, float iconFontSize, string glyph, bool hovered, Vector4 hoverInk)
    {
        var max = min + new Vector2(side, side);
        // Icon buttons (Random / QuickSwitch / Features / Trophy / Gallery /
        // Discord / Revert) follow the Buttons category, NOT Input Fields.
        Vector4 bgFill = hovered
            ? Boutique.SlotOrDefault("color.buttonHovered", CodexChassis.Surface1)
            : Boutique.SlotOrDefault("color.button",        new Vector4(20f / 255f, 24f / 255f, 32f / 255f, 0.6f));
        dl.AddRectFilled(min, max, CodexChassis.U32(bgFill));
        var borderCol = hovered
            ? CodexChassis.U32(CodexChassis.WithAlpha(hoverInk, 0.85f))
            : CodexChassis.U32(CodexChassis.BorderSoft);
        dl.AddRect(min, max, borderCol, 0f, ImDrawFlags.None, 1f * scale);

        var inkCol = hovered ? hoverInk : CodexChassis.TextDim;
        ImGui.PushFont(iconFont);
        Vector2 iconSize = ImGui.CalcTextSize(glyph);
        ImGui.PopFont();
        var iconPos = new Vector2(
            min.X + (side - iconSize.X) * 0.5f,
            min.Y + (side - iconSize.Y) * 0.5f);
        dl.AddText(iconFont, iconFontSize, iconPos, CodexChassis.U32(inkCol), glyph);
    }

    public static void DrawIconButton30(ImDrawListPtr dl, Vector2 min, float scale,
        ImFontPtr iconFont, float iconFontSize, string glyph, bool hovered, Vector4 hoverInk)
    {
        float side = 30f * scale;
        var max = min + new Vector2(side, side);

        Vector4 bgFill = hovered
            ? Boutique.SlotOrDefault("color.buttonHovered", CodexChassis.Surface1)
            : Boutique.SlotOrDefault("color.button",        new Vector4(20f / 255f, 24f / 255f, 32f / 255f, 0.6f));
        dl.AddRectFilled(min, max, CodexChassis.U32(bgFill));

        var borderCol = hovered
            ? CodexChassis.U32(CodexChassis.WithAlpha(hoverInk, 0.85f))
            : CodexChassis.U32(CodexChassis.BorderSoft);
        dl.AddRect(min, max, borderCol, 0f, ImDrawFlags.None, 1f * scale);

        // Icon centred, measure with explicit font via push-pop
        var inkCol = hovered ? hoverInk : CodexChassis.TextDim;
        ImGui.PushFont(iconFont);
        Vector2 iconSize = ImGui.CalcTextSize(glyph);
        ImGui.PopFont();
        var iconPos = new Vector2(
            min.X + (side - iconSize.X) * 0.5f,
            min.Y + (side - iconSize.Y) * 0.5f);
        dl.AddText(iconFont, iconFontSize, iconPos, CodexChassis.U32(inkCol), glyph);
    }

    /// <summary>Small notification dot (top-right of an icon button), 6px green pulsing.</summary>
    public static void DrawNewDot(ImDrawListPtr dl, Vector2 buttonMax, float scale, double time)
    {
        float dotSize = 6f * scale;
        var pos = new Vector2(buttonMax.X - 4f * scale - dotSize * 0.5f,
                              buttonMax.Y - 26f * scale);
        float blink = 0.7f + 0.3f * (float)Math.Sin(time * 4.0);
        var col = CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.Green, blink));
        dl.AddCircleFilled(pos, dotSize * 0.5f, col, 12);
        var glow = CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.Green, 0.35f * blink));
        dl.AddCircleFilled(pos, dotSize, glow, 16);
    }

    // ── Sort tab ────────────────────────────────────────────────────────

    /// <summary>
    /// Draws a sort tab. `pos` is the top-left of the tab's bounding rect.
    /// Returns the actual width used.
    /// </summary>
    public static float DrawSortTab(ImDrawListPtr dl, Vector2 pos, string label,
        float trackPx, float scale, bool isActive, bool isHovered)
    {
        float padX = 12f * scale;
        float padY = 6f * scale;
        float fontSize = ImGui.GetFontSize();
        float labelW = MeasureTrackedText(label, trackPx);
        float w = labelW + padX * 2;
        float h = fontSize + padY * 2;

        Vector4 ink = isActive ? CodexChassis.Text
                    : isHovered ? CodexChassis.TextDim
                                : CodexChassis.TextFaint;
        var textPos = new Vector2(pos.X + padX, pos.Y + padY);
        DrawTrackedText(dl, textPos, label, CodexChassis.U32(ink), trackPx);

        if (isActive)
        {
            // Active sort tab underline + glow follows the Primary Accent slot.
            Vector4 tabActive = Boutique.SlotOrDefault("custom.accent.primary",
                new Vector4(1f, 214f / 255f, 0f, 1f));

            float underY = pos.Y + h - 1f * scale;
            var ulMin = new Vector2(pos.X + 10f * scale, underY);
            var ulMax = new Vector2(pos.X + w - 10f * scale, underY + 2f * scale);
            for (int i = 3; i > 0; i--)
            {
                float r = i * 2f * scale;
                var gMin = ulMin - new Vector2(r, r);
                var gMax = ulMax + new Vector2(r, r);
                dl.AddRectFilled(gMin, gMax,
                    CodexChassis.U32(CodexChassis.WithAlpha(tabActive, 0.12f / i)));
            }
            dl.AddRectFilled(ulMin, ulMax, CodexChassis.U32(tabActive));
        }

        return w;
    }

    // ── Count badge (gold-at-8% bg, gold-at-28% border, gold ink) ───────

    public static Vector2 MeasureCountBadge(string text, float trackPx, float scale)
    {
        float padX = 7f * scale;
        float padTop = 2f * scale;
        float padBot = 3f * scale;
        float fontSize = ImGui.GetFontSize();
        float textW = MeasureTrackedText(text, trackPx);
        return new Vector2(textW + padX * 2, fontSize + padTop + padBot);
    }

    public static void DrawCountBadge(ImDrawListPtr dl, Vector2 pos, string text,
        float trackPx, float scale)
    {
        float padX = 7f * scale;
        float padTop = 2f * scale;
        var size = MeasureCountBadge(text, trackPx, scale);
        var min = pos;
        var max = pos + size;
        dl.AddRectFilled(min, max, CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.Gold, 0.08f)));
        dl.AddRect(min, max, CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.Gold, 0.28f)),
            0f, ImDrawFlags.None, 1f * scale);
        DrawTrackedText(dl, new Vector2(min.X + padX, min.Y + padTop), text,
            CodexChassis.U32(CodexChassis.Gold), trackPx);
    }

    // ── Filter pill ─────────────────────────────────────────────────────

    public static Vector2 MeasureFilterPill(string lbl, string val, float trackPx, float scale)
    {
        float padX = 11f * scale;
        float gap = 8f * scale;
        float lblW = MeasureTrackedText(lbl, trackPx);
        float valW = MeasureTrackedText(val, trackPx);
        float chevW = ImGui.CalcTextSize("v").X;
        float w = padX * 2 + lblW + gap + valW + gap + chevW;
        float h = 28f * scale;
        return new Vector2(w, h);
    }

    public static void DrawFilterPill(ImDrawListPtr dl, Vector2 pos, string lbl, string val,
        float trackPx, float scale, bool hovered)
    {
        var size = MeasureFilterPill(lbl, val, trackPx, scale);
        var min = pos;
        var max = pos + size;
        var bg = hovered
            ? CodexChassis.U32(CodexChassis.Surface1)
            : CodexChassis.U32(PillBg);
        dl.AddRectFilled(min, max, bg);
        dl.AddRect(min, max,
            CodexChassis.U32(hovered ? CodexChassis.Border : CodexChassis.BorderSoft),
            0f, ImDrawFlags.None, 1f * scale);

        float padX = 11f * scale;
        float gap = 8f * scale;
        float fontSize = ImGui.GetFontSize();
        float yText = min.Y + (size.Y - fontSize) * 0.5f;
        var x = min.X + padX;
        DrawTrackedText(dl, new Vector2(x, yText), lbl,
            CodexChassis.U32(CodexChassis.TextFaint), trackPx);
        x += MeasureTrackedText(lbl, trackPx) + gap;
        DrawTrackedText(dl, new Vector2(x, yText), val,
            CodexChassis.U32(CodexChassis.Text), trackPx);
        x += MeasureTrackedText(val, trackPx) + gap;
        dl.AddText(new Vector2(x, yText + 1f * scale),
            CodexChassis.U32(CodexChassis.TextFaint), "v");
    }

    // ── Search pill ─────────────────────────────────────────────────────

    public static void DrawSearchPillBackground(ImDrawListPtr dl, Vector2 min, Vector2 max,
        float scale, bool focused)
    {
        var bg = CodexChassis.U32(PillBg);
        dl.AddRectFilled(min, max, bg);
        var borderCol = focused
            ? CodexChassis.U32(CodexChassis.GoldDeep)
            : CodexChassis.U32(CodexChassis.BorderSoft);
        dl.AddRect(min, max, borderCol, 0f, ImDrawFlags.None, 1f * scale);
    }

    // ── Hero prompt: "CHOOSE YOUR CHARACTER" with gold wing lines ───────

    public static void DrawHeroPrompt(ImDrawListPtr dl, Vector2 centre, float availWidth,
        float trackPx, float scale, string text)
    {
        float fontSize = ImGui.GetFontSize();
        float textW = MeasureTrackedText(text, trackPx);
        float gapBetween = 20f * scale;
        float wingMax = 180f * scale;
        float wingW = MathF.Min(wingMax, (availWidth - textW - gapBetween * 2) * 0.5f);
        if (wingW < 30f * scale) wingW = 30f * scale;

        float y = centre.Y;
        var goldDeep = CodexChassis.WithAlpha(CodexChassis.GoldDeep, 0.55f);
        var goldClear = CodexChassis.WithAlpha(CodexChassis.GoldDeep, 0f);

        // Left wing, gradient: clear → goldDeep at 40% → goldDeep at 60% → clear
        var lStart = new Vector2(centre.X - textW * 0.5f - gapBetween - wingW, y);
        var lMidL  = new Vector2(centre.X - textW * 0.5f - gapBetween - wingW * 0.6f, y);
        var lMidR  = new Vector2(centre.X - textW * 0.5f - gapBetween - wingW * 0.4f, y);
        var lEnd   = new Vector2(centre.X - textW * 0.5f - gapBetween, y);
        dl.AddRectFilledMultiColor(lStart, new Vector2(lMidL.X, y + 1f * scale),
            CodexChassis.U32(goldClear), CodexChassis.U32(goldDeep),
            CodexChassis.U32(goldDeep), CodexChassis.U32(goldClear));
        dl.AddRectFilled(new Vector2(lMidL.X, y), new Vector2(lMidR.X, y + 1f * scale),
            CodexChassis.U32(goldDeep));
        dl.AddRectFilledMultiColor(new Vector2(lMidR.X, y), new Vector2(lEnd.X, y + 1f * scale),
            CodexChassis.U32(goldDeep), CodexChassis.U32(goldClear),
            CodexChassis.U32(goldClear), CodexChassis.U32(goldDeep));

        // Right wing
        var rStart = new Vector2(centre.X + textW * 0.5f + gapBetween, y);
        var rMidL  = new Vector2(centre.X + textW * 0.5f + gapBetween + wingW * 0.4f, y);
        var rMidR  = new Vector2(centre.X + textW * 0.5f + gapBetween + wingW * 0.6f, y);
        var rEnd   = new Vector2(centre.X + textW * 0.5f + gapBetween + wingW, y);
        dl.AddRectFilledMultiColor(rStart, new Vector2(rMidL.X, y + 1f * scale),
            CodexChassis.U32(goldClear), CodexChassis.U32(goldDeep),
            CodexChassis.U32(goldDeep), CodexChassis.U32(goldClear));
        dl.AddRectFilled(new Vector2(rMidL.X, y), new Vector2(rMidR.X, y + 1f * scale),
            CodexChassis.U32(goldDeep));
        dl.AddRectFilledMultiColor(new Vector2(rMidR.X, y), new Vector2(rEnd.X, y + 1f * scale),
            CodexChassis.U32(goldDeep), CodexChassis.U32(goldClear),
            CodexChassis.U32(goldClear), CodexChassis.U32(goldDeep));

        // Text, vertically centred on the wing-line Y. Soft drop shadow + main.
        var textPos = new Vector2(centre.X - textW * 0.5f, y - fontSize * 0.5f);
        DrawTrackedText(dl, textPos + new Vector2(0, 1.5f * scale), text,
            CodexChassis.U32(new Vector4(0, 0, 0, 0.65f)), trackPx);
        DrawTrackedText(dl, textPos, text,
            CodexChassis.U32(CodexChassis.Text), trackPx);
    }

    // ── Ambient: radial spots, hum lines, dust motes, breathe ──────────

    /// <summary>
    /// Ambient radial-spot stack, direct port of AchievementWindow's aurora pattern.
    /// 3 spots (gold, violet, cyan) drifting on per-spot periods, each rendered as
    /// 24 nested ellipse polygons stacked at low alpha to approximate a CSS blur.
    /// </summary>
    public static void DrawAmbientSpots(ImDrawListPtr dl, Vector2 mn, Vector2 mx, double time, float scale)
    {
        float t = (float)time;
        float w = mx.X - mn.X;
        float h = mx.Y - mn.Y;

        var spotPts = new Vector2[48];
        void Spot(float periodSec, Vector2 startAnchor, Vector2 driftDelta,
                  float rx, float ry, Vector4 colour, float peakA)
        {
            float phase = (t % (periodSec * 2f)) / periodSec;
            float p = phase <= 1f ? phase : 2f - phase;
            p = 0.5f - 0.5f * MathF.Cos(p * MathF.PI);
            var centre = mn + startAnchor + driftDelta * p;
            float scalePulse = 1f + 0.12f * p;
            float rxEff = rx * scalePulse;
            float ryEff = ry * scalePulse;

            const int layers = 24;
            uint col = ImGui.ColorConvertFloat4ToU32(
                CodexChassis.WithAlpha(colour, peakA / layers));
            for (int i = 1; i <= layers; i++)
            {
                float u = i / (float)layers;
                float lx = rxEff * u;
                float ly = ryEff * u;
                for (int j = 0; j < spotPts.Length; j++)
                {
                    float theta = (float)(j * Math.PI * 2.0 / spotPts.Length);
                    spotPts[j] = centre + new Vector2(
                        lx * (float)Math.Cos(theta),
                        ly * (float)Math.Sin(theta));
                }
                dl.AddConvexPolyFilled(ref spotPts[0], spotPts.Length, col);
            }
        }
        Spot(26f, new Vector2(w * 0.18f, h * 0.22f), new Vector2(200f * scale, 90f * scale),
             230f * scale, 140f * scale, CodexChassis.Gold, 0.028f);
        Spot(32f, new Vector2(w * 0.75f, h * 0.55f), new Vector2(-160f * scale, -70f * scale),
             210f * scale, 130f * scale, CodexChassis.Violet, 0.020f);
        Spot(38f, new Vector2(w * 0.45f, h * 0.65f), new Vector2(-120f * scale, 60f * scale),
             190f * scale, 120f * scale, CodexChassis.Cyan, 0.014f);
    }

    /// <summary>2-arg overload kept for backwards compat (defaults scale=1).</summary>
    public static void DrawAmbientSpots(ImDrawListPtr dl, Vector2 mn, Vector2 mx, double time)
        => DrawAmbientSpots(dl, mn, mx, time, 1f);

    /// <summary>
    /// 4th aurora, a soft warm gold wash anchored under the hero prompt area
    /// (top-centre, ~20% from top). Lower alpha than the main 3 since it
    /// underlays the hero text directly.
    /// </summary>
    public static void DrawCenterAuroraUnderHero(ImDrawListPtr dl, Vector2 mn, Vector2 mx, double time, float scale)
    {
        float t = (float)time;
        float w = mx.X - mn.X;
        float h = mx.Y - mn.Y;
        var spotPts = new Vector2[48];

        const float periodSec = 22f;
        float phase = (t % (periodSec * 2f)) / periodSec;
        float p = phase <= 1f ? phase : 2f - phase;
        p = 0.5f - 0.5f * MathF.Cos(p * MathF.PI);
        var centre = mn + new Vector2(w * 0.50f, h * 0.20f) + new Vector2(0f, 30f * scale * p);
        float scalePulse = 1f + 0.10f * p;
        float rxEff = 280f * scale * scalePulse;
        float ryEff = 100f * scale * scalePulse;

        const int layers = 24;
        const float peakA = 0.024f;
        uint col = ImGui.ColorConvertFloat4ToU32(CodexChassis.WithAlpha(CodexChassis.GoldWarm, peakA / layers));
        for (int i = 1; i <= layers; i++)
        {
            float u = i / (float)layers;
            float lx = rxEff * u;
            float ly = ryEff * u;
            for (int j = 0; j < spotPts.Length; j++)
            {
                float theta = (float)(j * Math.PI * 2.0 / spotPts.Length);
                spotPts[j] = centre + new Vector2(lx * (float)Math.Cos(theta), ly * (float)Math.Sin(theta));
            }
            dl.AddConvexPolyFilled(ref spotPts[0], spotPts.Length, col);
        }
    }

    private static Vector2 AmbientDrift(double t, double period, float dx, float dy)
    {
        float u = (float)((Math.Sin(t * Math.PI * 2 / period) + 1) * 0.5);
        return new Vector2(dx * u, dy * u);
    }

    private static void DrawAmbientSpot(ImDrawListPtr dl, Vector2 centre, float rx, float ry,
        Vector4 colour, float peakA)
    {
        const int layers = 8;       // fewer layers → each is brighter, more visible
        const int segments = 24;
        Span<Vector2> pts = stackalloc Vector2[segments];
        for (int i = 1; i <= layers; i++)
        {
            float u = i / (float)layers;
            for (int s = 0; s < segments; s++)
            {
                float a = s * MathF.PI * 2 / segments;
                pts[s] = centre + new Vector2(MathF.Cos(a) * rx * u, MathF.Sin(a) * ry * u);
            }
            unsafe
            {
                fixed (Vector2* p = pts)
                    dl.AddConvexPolyFilled(p, segments,
                        CodexChassis.U32(CodexChassis.WithAlpha(colour, peakA / layers)));
            }
        }
    }

    public static void DrawHumLines(ImDrawListPtr dl, Vector2 mn, Vector2 mx, double time, float scale)
    {
        float t = (float)time;
        float w = mx.X - mn.X;
        float h = mx.Y - mn.Y;
        void HumLine(float topFrac, float periodSec, bool reverse, Vector4 col, float peakA)
        {
            float phase = (t % periodSec) / periodSec;
            if (reverse) phase = 1f - phase;
            float xOff = -0.30f * w + phase * 0.60f * w;
            float yBand = mn.Y + topFrac * h;
            float lineW = w * 1.5f;
            var start = new Vector2(mn.X + xOff - lineW * 0.5f, yBand);
            var midL  = new Vector2(start.X + lineW * 0.40f, yBand);
            var midR  = new Vector2(start.X + lineW * 0.60f, yBand);
            var end   = new Vector2(start.X + lineW, yBand);
            uint cEdge = ImGui.ColorConvertFloat4ToU32(CodexChassis.WithAlpha(col, 0f));
            uint cMid  = ImGui.ColorConvertFloat4ToU32(CodexChassis.WithAlpha(col, peakA));
            float lineH = 1f * scale;
            dl.AddRectFilledMultiColor(
                start, new Vector2(midL.X, yBand + lineH),
                cEdge, cMid, cMid, cEdge);
            dl.AddRectFilled(midL, new Vector2(midR.X, yBand + lineH), cMid);
            dl.AddRectFilledMultiColor(
                midR, new Vector2(end.X, yBand + lineH),
                cMid, cEdge, cEdge, cMid);
        }
        HumLine(0.28f, 16f, reverse: false, CodexChassis.Gold,       0.18f);
        HumLine(0.54f, 22f, reverse: true,  CodexChassis.MagentaSft, 0.12f);
        HumLine(0.78f, 26f, reverse: false, CodexChassis.CyanSoft,   0.09f);
    }

    public static void DrawHumLines(ImDrawListPtr dl, Vector2 mn, Vector2 mx, double time)
        => DrawHumLines(dl, mn, mx, time, 1f);

    public static void DrawDustMotes(ImDrawListPtr dl, Vector2 mn, Vector2 mx, double time, float scale)
    {
        // Direct port of AchievementWindow's mote pattern.
        float t = (float)time;
        float w = mx.X - mn.X;
        float h = mx.Y - mn.Y;
        var motes = new (float leftPct, float delay, float period, Vector4 col)[]
        {
            (0.14f,  0f,   10f, CodexChassis.GoldWarm),
            (0.32f,  2f,   12f, CodexChassis.MagentaSft),
            (0.48f,  4f,   11f, CodexChassis.GoldWarm),
            (0.66f,  1.5f, 13f, CodexChassis.CyanSoft),
            (0.82f,  6f,   10f, CodexChassis.GoldWarm),
            (0.24f,  5f,   14f, CodexChassis.Violet),
        };
        foreach (var (leftPct, delay, period, col) in motes)
        {
            float localT = ((t - delay) % period + period) % period;
            float p = localT / period;
            float a = p < 0.15f ? (p / 0.15f) * 0.7f
                    : p > 0.85f ? ((1f - p) / 0.15f) * 0.7f
                    : 0.7f;
            float yRise = p * (h + 150f * scale);
            float xDrift = -p * 30f * scale;
            var pt = new Vector2(mn.X + leftPct * w + xDrift, mx.Y - 10f * scale - yRise);
            dl.AddCircleFilled(pt, 1.2f * scale,
                ImGui.ColorConvertFloat4ToU32(CodexChassis.WithAlpha(col, a)), 8);
            dl.AddCircleFilled(pt, 2.5f * scale,
                ImGui.ColorConvertFloat4ToU32(CodexChassis.WithAlpha(col, a * 0.4f)), 10);
        }
    }

    public static void DrawWindowBreathe(ImDrawListPtr dl, Vector2 min, Vector2 max, double time)
    {
        Vector2 size = max - min;
        float pulse = 0.45f + 0.55f * (0.5f + 0.5f * MathF.Sin((float)time * MathF.PI * 2 / 8f));
        var goldCol = CodexChassis.WithAlpha(CodexChassis.Gold, 0.05f * pulse);
        var magCol  = CodexChassis.WithAlpha(CodexChassis.Magenta, 0.035f * pulse);
        DrawAmbientSpot(dl, min + size * new Vector2(0.22f, 0.28f),
            size.X * 0.35f, size.Y * 0.275f, goldCol, goldCol.W);
        DrawAmbientSpot(dl, min + size * new Vector2(0.78f, 0.72f),
            size.X * 0.30f, size.Y * 0.225f, magCol,  magCol.W);
    }

    // ── Card silhouette ─────────────────────────────────────────────────

    public static void DrawCardSlip(ImDrawListPtr dl, Vector2 min, Vector2 max,
        Vector4 npCol, bool isHovered, bool isApplied, float scale)
    {
        float cham = (isApplied ? CodexChassis.ChamMd : CodexChassis.ChamSm) * scale;

        // Fill first, then stroked edge. Border is 2px when hovered/applied
        // for prominence, 1.5px otherwise, was too subtle at 1px.
        var fillCol = CodexChassis.Lerp(CodexChassis.Surface1, CodexChassis.Surface0, 0.5f);
        CodexChassis.FillSlip(dl, min, max, cham, CodexChassis.U32(fillCol));

        // Applied state KEEPS the nameplate-coloured edge (no fat gold border).
        // The applied "signature" comes from the corner brackets drawn separately
        // by DrawAppliedCornerBrackets, so the card silhouette stays consistent.
        Vector4 edgeCol;
        float thickness;
        if (isHovered)
        {
            edgeCol = npCol;
            thickness = 2.5f * scale;
        }
        else
        {
            edgeCol = CodexChassis.Lerp(CodexChassis.BorderSoft, npCol, 0.55f);
            thickness = 1.5f * scale;
        }
        CodexChassis.StrokeSlip(dl, min, max, cham, CodexChassis.U32(edgeCol), thickness);
    }

    /// <summary>
    /// Applied-card "Corner Brackets" treatment (Study II).
    /// Two L-shapes at the SHARP corners only (TL + BR). The chamfered TR + BL
    /// stay clean. Brackets are tinted with the character's nameplate colour and
    /// breathe between a dimmed and full-strength version on a 4.4s cycle.
    /// </summary>
    public static void DrawAppliedCornerBrackets(ImDrawListPtr dl, Vector2 min, Vector2 max,
        float scale, double time, Vector4 npCol, float sparkT = -1f, float hoverAmount = 0f)
    {
        float arm = 16f * scale;                   // sharp-corner arm length
        float outset = 2f * scale;                 // bracket sits just outside the slip
        float thickness = 1.5f * scale;

        // Breathe between a dimmed npCol and full-strength on a 4.4s cycle
        float pulse = 0.5f + 0.5f * MathF.Sin((float)time * MathF.PI * 2f / 4.4f);
        var dim = CodexChassis.WithAlpha(npCol, 0.55f);
        var col = CodexChassis.Lerp(dim, npCol, pulse);
        uint cu = CodexChassis.U32(col);

        // Hover baseline + spark spike, max'd so spike isn't swamped. Hover
        // ramp gates at 0.5 so frozen-after-hover residue (~0.01) doesn't
        // leave a sub-pixel TL shift on fractional UI scales.
        float maxPx = 4f * scale;
        float hoverDisplPx = MathF.Max(0f, MathF.Min(1f, (hoverAmount - 0.5f) * 2f)) * maxPx;
        Vector2 tlOff = Vector2.Zero;
        Vector2 brOff = Vector2.Zero;
        float tlPx = hoverDisplPx;
        float brPx = hoverDisplPx;
        if (sparkT >= 0f)
        {
            tlPx = MathF.Max(tlPx, SparkBracketProximity(sparkT, 0.0f) * maxPx);
            brPx = MathF.Max(brPx, SparkBracketProximity(sparkT, 0.5f) * maxPx);
        }
        if (tlPx > 0f) tlOff = new Vector2(-1f, -1f) * tlPx;
        if (brPx > 0f) brOff = new Vector2(+1f, +1f) * brPx;

        // ── TL (sharp): top-left corner L ──
        var tl = new Vector2(min.X - outset + tlOff.X, min.Y - outset + tlOff.Y);
        dl.AddLine(tl, new Vector2(tl.X + arm, tl.Y), cu, thickness);          // top
        dl.AddLine(tl, new Vector2(tl.X, tl.Y + arm), cu, thickness);          // left

        // ── BR (sharp): bottom-right corner L ──
        var br = new Vector2(max.X + outset + brOff.X, max.Y + outset + brOff.Y);
        dl.AddLine(br, new Vector2(br.X - arm, br.Y), cu, thickness);          // bottom
        dl.AddLine(br, new Vector2(br.X, br.Y - arm), cu, thickness);          // right

        // (TR + BL chamfered brackets dropped per user, the two sharp brackets
        // alone read more cleanly without doubling up on the chamfer cuts.)

        // Soft 6px aurora glow at each remaining bracket at peak pulse
        if (pulse > 0.4f)
        {
            float glowA = (pulse - 0.4f) / 0.6f * 0.50f;
            var glowCol = CodexChassis.WithAlpha(npCol, glowA);
            DrawAuroraSpot(dl, tl, 8f * scale, 8f * scale, glowCol, 4);
            DrawAuroraSpot(dl, br, 8f * scale, 8f * scale, glowCol, 4);
        }
    }

    // Asymmetric proximity curve for spark-pushes-bracket.  Returns 0..1.
    // Quick quadratic ramp as the spark approaches the corner (over 6% of
    // the perimeter), peaks at the corner, then decays quadratically over
    // a longer 15% window so the bracket lingers shoved-out before settling.
    private static float SparkBracketProximity(float sparkT, float cornerT)
    {
        float delta = sparkT - cornerT;
        if (delta > 0.5f)  delta -= 1f;
        if (delta < -0.5f) delta += 1f;

        if (delta < 0f)
        {
            // Tighter window + cubic ramp so the bracket stays still until
            // the spark is really close, then snaps out as it arrives.
            const float approachWin = 0.04f;
            if (delta < -approachWin) return 0f;
            float t = 1f + delta / approachWin;        // 0 far → 1 at corner
            return t * t * t;
        }
        else
        {
            const float decayWin = 0.15f;
            if (delta > decayWin) return 0f;
            float t = 1f - delta / decayWin;           // 1 at corner → 0 far
            return t * t;
        }
    }

    // ── Top chips ───────────────────────────────────────────────────────

    public static Vector2 MeasureAppliedChip(float trackPx, float scale)
    {
        float dotSize = 5f * scale;
        float gap = 6f * scale;
        float padX = 10f * scale;
        float padTop = 3f * scale;
        float padBot = 4f * scale;
        float fontSize = ImGui.GetFontSize();
        float labelW = MeasureTrackedText("APPLIED", trackPx);
        return new Vector2(padX * 2 + dotSize + gap + labelW, fontSize + padTop + padBot);
    }

    /// <summary>Applied chip tinted with the character's nameplate colour.</summary>
    public static void DrawAppliedChip(ImDrawListPtr dl, Vector2 pos, float trackPx, float scale, Vector4 npCol)
    {
        float dotSize = 5f * scale;
        float gap = 6f * scale;
        float padX = 10f * scale;
        float padTop = 3f * scale;
        var size = MeasureAppliedChip(trackPx, scale);
        var min = pos;
        var max = pos + size;
        // Solid dark slip, no double-layer alpha bleed.
        dl.AddRectFilled(min, max, CodexChassis.U32(new Vector4(0.04f, 0.04f, 0.05f, 0.92f)));
        // 1px npCol underline (was gold)
        dl.AddLine(new Vector2(min.X, max.Y - 1f * scale),
                   new Vector2(max.X, max.Y - 1f * scale),
                   CodexChassis.U32(npCol), 1f * scale);

        // Square pip in nameplate colour
        var pipC = new Vector2(min.X + padX + dotSize * 0.5f, min.Y + size.Y * 0.5f);
        DrawSquarePip(dl, pipC, dotSize * 0.5f, npCol);

        DrawTrackedText(dl, new Vector2(min.X + padX + dotSize + gap, min.Y + padTop),
            "APPLIED", CodexChassis.U32(npCol), trackPx);
    }

    /// <summary>Backwards-compat overload, defaults to Gold.</summary>
    public static void DrawAppliedChip(ImDrawListPtr dl, Vector2 pos, float trackPx, float scale)
        => DrawAppliedChip(dl, pos, trackPx, scale, CodexChassis.Gold);

    /// <summary>Crown-only badge, minimal, just the gold crown icon with a soft halo.</summary>
    public static Vector2 MeasureMainChip(float trackPx, float scale, ImFontPtr iconFont, float iconFontSize, string crownGlyph)
    {
        // Just the icon at its render size + small padding.
        float side = 18f * scale;
        return new Vector2(side, side);
    }

    /// <summary>
    /// Diamond Sigil (Study II), a small rotated square stacked over a darker
    /// inner sister with a gold-pip centre. `pos` is the diamond CENTRE.
    /// `iconFont`/`iconFontSize`/`crownGlyph` kept for signature compat (unused).
    /// </summary>
    public static void DrawMainChip(ImDrawListPtr dl, Vector2 pos, float trackPx, float scale,
        ImFontPtr iconFont, float iconFontSize, string crownGlyph)
    {
        // 18×18 unrotated square → diamond after 45° rotation. Compact size
        // so it pins to the TL corner without crowding the APPLIED chip.
        const float Sqrt2 = 1.4142136f;
        float outerHalf = 9f * scale;
        float outerR    = outerHalf * Sqrt2;
        float innerHalf = 5.5f * scale;   // 3.5px inset → 11×11 inner square
        float innerR    = innerHalf * Sqrt2;

        // Outer diamond vertices (CW from top)
        var outTop   = pos + new Vector2(0,        -outerR);
        var outRight = pos + new Vector2( outerR,   0);
        var outBot   = pos + new Vector2(0,         outerR);
        var outLeft  = pos + new Vector2(-outerR,   0);

        // Inner diamond vertices
        var inTop    = pos + new Vector2(0,        -innerR);
        var inRight  = pos + new Vector2( innerR,   0);
        var inBot    = pos + new Vector2(0,         innerR);
        var inLeft   = pos + new Vector2(-innerR,   0);

        // ── Drop shadow (1.5px down-right) for any-bg legibility ──
        var shOff = new Vector2(1.5f * scale, 1.5f * scale);
        uint shCol = CodexChassis.U32(new Vector4(0, 0, 0, 0.55f));
        Span<Vector2> shadowPts = stackalloc Vector2[4] {
            outTop + shOff, outRight + shOff, outBot + shOff, outLeft + shOff
        };
        unsafe { fixed (Vector2* p = shadowPts) dl.AddConvexPolyFilled(p, 4, shCol); }

        // ── Outer diamond fill (Gold) ──
        Span<Vector2> outerPts = stackalloc Vector2[4] { outTop, outRight, outBot, outLeft };
        unsafe { fixed (Vector2* p = outerPts) dl.AddConvexPolyFilled(p, 4, CodexChassis.U32(CodexChassis.Gold)); }

        // ── Top-edge highlights for subtle 3D depth (gold-warm on the two upper
        //    edges, gold-deep would be on lower edges but we skip for simplicity). ──
        uint hlCol = CodexChassis.U32(CodexChassis.GoldWarm);
        dl.AddLine(outLeft, outTop, hlCol, 1f * scale);
        dl.AddLine(outTop, outRight, hlCol, 1f * scale);
        // Bottom edges in gold-deep, completes the depth cue
        uint shadeCol = CodexChassis.U32(CodexChassis.GoldDeep);
        dl.AddLine(outRight, outBot, shadeCol, 1f * scale);
        dl.AddLine(outBot, outLeft, shadeCol, 1f * scale);

        // ── Inner dark diamond (Surface0 fill) ──
        Span<Vector2> innerPts = stackalloc Vector2[4] { inTop, inRight, inBot, inLeft };
        unsafe { fixed (Vector2* p = innerPts) dl.AddConvexPolyFilled(p, 4, CodexChassis.U32(CodexChassis.Surface0)); }

        // ── Inner diamond gold outline (1px) ──
        uint inOutlineCol = CodexChassis.U32(CodexChassis.Gold);
        dl.AddLine(inTop, inRight, inOutlineCol, 1f * scale);
        dl.AddLine(inRight, inBot, inOutlineCol, 1f * scale);
        dl.AddLine(inBot, inLeft, inOutlineCol, 1f * scale);
        dl.AddLine(inLeft, inTop, inOutlineCol, 1f * scale);

        // ── Centre gold pip + soft glow ──
        float pipR = 2f * scale;
        dl.AddCircleFilled(pos, pipR * 1.8f,
            CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.Gold, 0.45f)), 12);
        dl.AddCircleFilled(pos, pipR, CodexChassis.U32(CodexChassis.Gold), 12);
    }

    // ── Applied state visual flair ──────────────────────────────────────

    public static void DrawAppliedHaloRings(ImDrawListPtr dl, Vector2 imgMin, Vector2 imgMax,
        float scale, double time)
    {
        float pulse = 0.5f + 0.5f * MathF.Sin((float)time * MathF.PI * 2 / 4f);
        var rings = new (float offset, float alphaIdle, float alphaPeak)[]
        {
            (1f, 0.42f, 0.60f),
            (4f, 0.12f, 0.20f),
            (8f, 0.05f, 0.10f),
        };
        foreach (var r in rings)
        {
            float a = r.alphaIdle + (r.alphaPeak - r.alphaIdle) * pulse;
            var col = CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.Gold, a));
            dl.AddRect(imgMin - new Vector2(r.offset, r.offset) * scale,
                       imgMax + new Vector2(r.offset, r.offset) * scale,
                       col, 0f, ImDrawFlags.None, 1f * scale);
        }
        // (No portrait-centred glow, was muddying the image.)
    }

    public static void DrawAppliedBeatRipple(ImDrawListPtr dl, Vector2 centre, float baseRadius,
        float scale, double time)
    {
        for (int phase = 0; phase < 2; phase++)
        {
            float t = (float)((time + phase * 2.2) % 4.4 / 4.4);
            float r = baseRadius * (0.45f + (2.4f - 0.45f) * t);
            float a = (1f - t) * 0.65f;
            dl.AddCircle(centre, r * scale,
                CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.Gold, a)),
                32, 1f * scale);
        }
    }

    public static void DrawAppliedSealPips(ImDrawListPtr dl, Vector2 cardMin, Vector2 cardMax, float scale)
    {
        float chamMd = CodexChassis.ChamMd * scale;
        var trMin = new Vector2(cardMax.X - chamMd, cardMin.Y - 2f * scale);
        var trMax = trMin + new Vector2(4f * scale, 4f * scale);
        dl.AddRectFilled(trMin, trMax, CodexChassis.U32(CodexChassis.Gold));
        var blMin = new Vector2(cardMin.X + chamMd - 4f * scale, cardMax.Y - 2f * scale);
        var blMax = blMin + new Vector2(4f * scale, 4f * scale);
        dl.AddRectFilled(blMin, blMax, CodexChassis.U32(CodexChassis.GoldDeep));
    }

    public static void DrawAppliedNameShimmer(ImDrawListPtr dl, Vector2 textPos, string text,
        float scale, double time)
    {
        float fontSize = ImGui.GetFontSize();
        float w = ImGui.CalcTextSize(text).X;
        float sweepX = (float)(((time / 4.2) % 1.0) - 0.2) * (w + 80f * scale);

        dl.AddText(textPos, CodexChassis.U32(CodexChassis.Text), text);

        float x = textPos.X;
        for (int i = 0; i < text.Length; i++)
        {
            string g = text.Substring(i, 1);
            float gW = ImGui.CalcTextSize(g).X;
            float dist = MathF.Abs((x + gW * 0.5f) - (textPos.X + sweepX));
            float falloff = MathF.Max(0f, 1f - dist / (60f * scale));
            if (falloff > 0.02f)
            {
                var brightCol = CodexChassis.Lerp(CodexChassis.GoldWarm,
                    new Vector4(1, 1, 1, 1), 0.15f);
                dl.AddText(new Vector2(x, textPos.Y),
                    CodexChassis.U32(CodexChassis.WithAlpha(brightCol, falloff * 0.85f)), g);
            }
            x += gW;
        }
    }

    // ── Tracked-caps text helpers (current font) ────────────────────────

    public static float MeasureTrackedText(string text, float trackPx)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        float w = 0f;
        for (int i = 0; i < text.Length; i++)
        {
            w += ImGui.CalcTextSize(text.Substring(i, 1)).X;
            if (i < text.Length - 1) w += trackPx;
        }
        return w;
    }

    public static float DrawTrackedText(ImDrawListPtr dl, Vector2 pos, string text, uint colour, float trackPx)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        // Snap base position to integer pixels, sub-pixel text gets bilinear-smeared.
        float x = MathF.Round(pos.X);
        float y = MathF.Round(pos.Y);
        for (int i = 0; i < text.Length; i++)
        {
            string g = text.Substring(i, 1);
            dl.AddText(new Vector2(x, y), colour, g);
            x = MathF.Round(x + ImGui.CalcTextSize(g).X);
            if (i < text.Length - 1) x = MathF.Round(x + trackPx);
        }
        return x - MathF.Round(pos.X);
    }

    // ── Aurora-spot (small concentric ellipse glow stack) ───────────────

    public static void DrawAuroraSpot(ImDrawListPtr dl, Vector2 centre, float rx, float ry,
        Vector4 colour, int layers = 12)
    {
        const int segments = 24;
        for (int i = 1; i <= layers; i++)
        {
            float u = i / (float)layers;
            Span<Vector2> pts = stackalloc Vector2[segments];
            for (int s = 0; s < segments; s++)
            {
                float a = s * MathF.PI * 2 / segments;
                pts[s] = centre + new Vector2(MathF.Cos(a) * rx * u, MathF.Sin(a) * ry * u);
            }
            unsafe
            {
                fixed (Vector2* p = pts)
                    dl.AddConvexPolyFilled(p, segments,
                        CodexChassis.U32(CodexChassis.WithAlpha(colour, colour.W / layers)));
            }
        }
    }

    // ── Pagination dot ──────────────────────────────────────────────────

    /// <summary>
    /// Wardrobe-style page dot. Inactive: 8px hollow square. Active: 16×6
    /// gold horizontal bar with breathing halo. Hover lifts inactive to a
    /// brighter border.
    /// </summary>
    public static void DrawPageDot(ImDrawListPtr dl, Vector2 centre, float scale, bool isActive, bool isHovered, double time)
    {
        // Active page indicator pulls from custom.pageButtonActive when set,
        // hardcoded gold default otherwise. Decoupled from CodexChassis.Gold
        // so the editor's Accent override stops driving the active page dot.
        Vector4 pageActive = Boutique.SlotOrDefault("custom.pageButtonActive",
            new Vector4(1f, 214f / 255f, 0f, 1f));
        Vector4 pageActiveDeep = CodexChassis.Lerp(pageActive,
            new Vector4(0f, 0f, 0f, pageActive.W), 0.40f);

        if (isActive)
        {
            float w = 16f * scale;
            float h = 5f * scale;
            var min = centre - new Vector2(w * 0.5f, h * 0.5f);
            var max = centre + new Vector2(w * 0.5f, h * 0.5f);

            // Breathing halo
            float breath = 0.5f + 0.5f * MathF.Sin((float)time * MathF.PI);
            float haloA = 0.20f + 0.15f * breath;
            float pad = 3f * scale;
            dl.AddRectFilled(
                new Vector2(min.X - pad, min.Y - pad),
                new Vector2(max.X + pad, max.Y + pad),
                CodexChassis.U32(CodexChassis.WithAlpha(pageActive, haloA * 0.5f)));
            dl.AddRectFilled(min, max, CodexChassis.U32(pageActive));
        }
        else
        {
            float side = 8f * scale;
            var min = centre - new Vector2(side * 0.5f, side * 0.5f);
            var max = centre + new Vector2(side * 0.5f, side * 0.5f);
            var borderCol = isHovered ? pageActiveDeep : CodexChassis.Border;
            dl.AddRect(min, max, CodexChassis.U32(borderCol), 0f, ImDrawFlags.None, 1.5f * scale);
            if (isHovered)
                dl.AddRectFilled(min, max,
                    CodexChassis.U32(CodexChassis.WithAlpha(pageActiveDeep, 0.20f)));
        }
    }

    /// <summary>2-arg backwards-compat overload.</summary>
    public static void DrawPageDot(ImDrawListPtr dl, Vector2 centre, float scale, bool isActive, bool isHovered)
        => DrawPageDot(dl, centre, scale, isActive, isHovered, ImGui.GetTime());

    // ─── Wardrobe-pager port ───
    // Direct 1:1 of WardrobeWindow.DrawPagerRow + DrawNavArrow, parameterised
    // for the main-window footer (accent = Gold). Caller supplies prevIdx/curr/t
    // so this helper stays stateless.
    private const float PagerTransitionSec = 0.28f;

    /// <summary>
    /// Wardrobe-style page row: sharp-rect arrows with "<" / ">" glyphs +
    /// lerping dots + breathing halo + bloom ring on transition. Returns the
    /// full row width so caller can centre it.
    /// </summary>
    public static void DrawWardrobePagerRow(ImDrawListPtr dl, Vector2 stMn, Vector2 stMx,
        float yCenter, int total, int curr, int fromIdx, float t, bool isTransitioning,
        float scale, Vector4 accent, Vector4 accentWarm, IFontHandle? glyphFont,
        Action<int> onJumpToPage)
    {
        if (total <= 0) return;
        float arrSize = 26f * scale;
        float dotSz = 6f * scale;
        float activeDotW = 14f * scale;
        float dotGap = 6f * scale;

        float dotsW = (total - 1) * dotSz + activeDotW + (total - 1) * dotGap;
        float gap = 10f * scale;
        float rowW = arrSize + gap + dotsW + gap + arrSize;
        float xStart = (stMn.X + stMx.X) * 0.5f - rowW * 0.5f;

        var prevPos = new Vector2(xStart, yCenter - arrSize * 0.5f);
        bool prevEnabled = curr > 0;
        DrawWardrobeNavArrow(dl, "##boutPagerPrev", prevPos, new Vector2(arrSize, arrSize),
            "<", !prevEnabled, accent, accentWarm, glyphFont,
            () => { if (prevEnabled) onJumpToPage(curr - 1); });

        float dotsLeft = prevPos.X + arrSize + gap;
        float dotY = yCenter - dotSz * 0.5f;

        for (int i = 0; i < total; i++)
        {
            float xFrom = dotsLeft;
            float xCur = dotsLeft;
            for (int k = 0; k < i; k++)
            {
                xFrom += (k == fromIdx ? activeDotW : dotSz) + dotGap;
                xCur  += (k == curr     ? activeDotW : dotSz) + dotGap;
            }
            float xi = xFrom + (xCur - xFrom) * t;

            float wFrom = (i == fromIdx) ? activeDotW : dotSz;
            float wCur  = (i == curr)    ? activeDotW : dotSz;
            float wi = wFrom + (wCur - wFrom) * t;

            var dotMin = new Vector2(xi, dotY);
            var dotMax = new Vector2(xi + wi, dotY + dotSz);

            ImGui.SetCursorScreenPos(dotMin);
            if (ImGui.InvisibleButton($"##boutPagerDot_{i}", new Vector2(wi, dotSz)))
                onJumpToPage(i);
            bool hov = ImGui.IsItemHovered();

            float fillAFrom, borderAFrom;
            if (i == fromIdx)      { fillAFrom = 1f;    borderAFrom = 0f;    }
            else if (i < fromIdx)  { fillAFrom = 0.35f; borderAFrom = 0f;    }
            else                   { fillAFrom = 0f;    borderAFrom = 0.35f; }

            float fillACur, borderACur;
            if (i == curr)         { fillACur = 1f;    borderACur = 0f;    }
            else if (i < curr)     { fillACur = 0.35f; borderACur = 0f;    }
            else                   { fillACur = 0f;    borderACur = 0.35f; }

            float fillA = fillAFrom + (fillACur - fillAFrom) * t;
            float borderA = borderAFrom + (borderACur - borderAFrom) * t;

            if (hov && i != curr && !isTransitioning)
            {
                fillA = MathF.Max(fillA, 0.25f);
                borderA = MathF.Max(borderA, 0.60f);
            }

            if (i == curr && !isTransitioning)
            {
                float breath = 0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * MathF.PI);
                float halo = (0.10f + 0.10f * breath) * 0.4f;
                float pad = 3f * scale;
                dl.AddRectFilled(
                    new Vector2(dotMin.X - pad, dotMin.Y - pad),
                    new Vector2(dotMax.X + pad, dotMax.Y + pad),
                    CodexChassis.U32(CodexChassis.WithAlpha(accent, halo)));
            }

            if (fillA > 0.01f)
                dl.AddRectFilled(dotMin, dotMax,
                    CodexChassis.U32(CodexChassis.WithAlpha(accent, fillA)));
            if (borderA > 0.01f)
                dl.AddRect(dotMin, dotMax,
                    CodexChassis.U32(CodexChassis.WithAlpha(accent, borderA)),
                    0f, ImDrawFlags.None, 1f);

            if (isTransitioning && i == curr)
            {
                float rippleR = wi * 0.5f + t * dotSz * 2.5f;
                float rippleA = (1f - t) * 0.50f;
                if (rippleA > 0.01f)
                {
                    var ctr = new Vector2(
                        (dotMin.X + dotMax.X) * 0.5f,
                        (dotMin.Y + dotMax.Y) * 0.5f);
                    dl.AddCircle(ctr, rippleR,
                        CodexChassis.U32(CodexChassis.WithAlpha(accent, rippleA)),
                        24, 1.5f * scale);
                }
            }
        }

        float nextX = dotsLeft + dotsW + gap;
        var nextPos = new Vector2(nextX, yCenter - arrSize * 0.5f);
        bool nextEnabled = curr < total - 1;
        DrawWardrobeNavArrow(dl, "##boutPagerNext", nextPos, new Vector2(arrSize, arrSize),
            ">", !nextEnabled, accent, accentWarm, glyphFont,
            () => { if (nextEnabled) onJumpToPage(curr + 1); });
    }

    /// <summary>Wardrobe nav-arrow: sharp rect, accent border alpha by state.</summary>
    private static void DrawWardrobeNavArrow(ImDrawListPtr dl, string id, Vector2 pos, Vector2 size,
        string glyph, bool disabled, Vector4 accent, Vector4 accentWarm, IFontHandle? glyphFont, Action onClick)
    {
        ImGui.SetCursorScreenPos(pos);
        if (!disabled && ImGui.InvisibleButton(id, size)) onClick();
        bool hovered = !disabled && ImGui.IsItemHovered();

        var min = pos;
        var max = new Vector2(pos.X + size.X, pos.Y + size.Y);
        Vector4 borderCol = disabled
            ? CodexChassis.WithAlpha(accent, 0.10f)
            : (hovered ? accent : CodexChassis.WithAlpha(accent, 0.24f));
        dl.AddRect(min, max, CodexChassis.U32(borderCol), 0f, ImDrawFlags.None, 1f);
        if (hovered)
            dl.AddRectFilled(min, max, CodexChassis.U32(CodexChassis.WithAlpha(accent, 0.08f)));

        Vector4 textCol = disabled
            ? CodexChassis.TextGhost
            : (hovered ? accentWarm : CodexChassis.TextDim);
        if (glyphFont != null)
        {
            using (glyphFont.Push())
            {
                var sz = ImGui.CalcTextSize(glyph);
                dl.AddText(
                    new Vector2(pos.X + (size.X - sz.X) * 0.5f,
                                pos.Y + (size.Y - sz.Y) * 0.5f),
                    CodexChassis.U32(textCol), glyph);
            }
        }
        else
        {
            var sz = ImGui.CalcTextSize(glyph);
            dl.AddText(
                new Vector2(pos.X + (size.X - sz.X) * 0.5f,
                            pos.Y + (size.Y - sz.Y) * 0.5f),
                CodexChassis.U32(textCol), glyph);
        }
    }

    // ── Page-arrow button (26×22 with chevron glyph) ────────────────────

    public static void DrawPageArrow(ImDrawListPtr dl, Vector2 min, float scale,
        ImFontPtr iconFont, float iconFontSize, string glyph, bool hovered, bool disabled)
    {
        float w = 26f * scale;
        float h = 22f * scale;
        var max = min + new Vector2(w, h);
        Vector4 arrowBg = hovered && !disabled
            ? Boutique.SlotOrDefault("color.buttonHovered", new Vector4(20f / 255f, 24f / 255f, 32f / 255f, 0.6f))
            : Boutique.SlotOrDefault("color.button",        new Vector4(20f / 255f, 24f / 255f, 32f / 255f, 0.6f));
        dl.AddRectFilled(min, max, CodexChassis.U32(arrowBg));
        var borderCol = hovered && !disabled
            ? CodexChassis.U32(CodexChassis.Border)
            : CodexChassis.U32(CodexChassis.BorderSoft);
        dl.AddRect(min, max, borderCol, 0f, ImDrawFlags.None, 1f * scale);

        Vector4 ink = disabled
            ? CodexChassis.WithAlpha(CodexChassis.TextDim, 0.30f)
            : (hovered ? CodexChassis.Text : CodexChassis.TextDim);

        ImGui.PushFont(iconFont);
        Vector2 iconSize = ImGui.CalcTextSize(glyph);
        ImGui.PopFont();
        var iconPos = new Vector2(min.X + (w - iconSize.X) * 0.5f,
                                  min.Y + (h - iconSize.Y) * 0.5f);
        dl.AddText(iconFont, iconFontSize, iconPos, CodexChassis.U32(ink), glyph);
    }

    // ── Footer link (icon + tracked-caps label) ────────────────────────

    public static Vector2 MeasureFooterLink(string label, float trackPx, float scale,
        ImFontPtr iconFont, float iconFontSize, string glyph)
    {
        float gap = 7f * scale;
        ImGui.PushFont(iconFont);
        Vector2 iconSize = ImGui.CalcTextSize(glyph);
        ImGui.PopFont();
        float labelW = MeasureTrackedText(label, trackPx);
        float fontSize = ImGui.GetFontSize();
        return new Vector2(iconSize.X + gap + labelW, MathF.Max(fontSize, iconSize.Y));
    }

    public static void DrawFooterLink(ImDrawListPtr dl, Vector2 pos, string label,
        float trackPx, float scale, ImFontPtr iconFont, float iconFontSize, string glyph,
        bool hovered, bool isHeart, double time)
    {
        float gap = 7f * scale;
        var size = MeasureFooterLink(label, trackPx, scale, iconFont, iconFontSize, glyph);

        Vector4 textInk = hovered ? CodexChassis.Text : CodexChassis.TextFaint;
        Vector4 iconInk;
        ImGui.PushFont(iconFont);
        Vector2 iconSize = ImGui.CalcTextSize(glyph);
        ImGui.PopFont();
        var iconPos = new Vector2(pos.X, pos.Y + (size.Y - iconSize.Y) * 0.5f);

        if (isHeart)
        {
            iconInk = hovered ? Rgb(0xFF, 0x6B, 0x6B) : CodexChassis.Red;
            // Heart pulses ONLY on hover (was always-on which read as nervous noise).
            float beat = hovered ? HeartBeatScale(time) : 1f;
            float scaled = iconFontSize * beat;
            if (beat > 1.05f)
            {
                var glowCol = CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.Red, 0.35f * (beat - 1f) / 0.18f));
                dl.AddText(iconFont, scaled, iconPos - new Vector2(1, 1) * scale, glowCol, glyph);
                dl.AddText(iconFont, scaled, iconPos + new Vector2(1, 1) * scale, glowCol, glyph);
            }
            dl.AddText(iconFont, scaled, iconPos, CodexChassis.U32(iconInk), glyph);
        }
        else
        {
            iconInk = hovered ? CodexChassis.Gold : CodexChassis.TextGhost;
            dl.AddText(iconFont, iconFontSize, iconPos, CodexChassis.U32(iconInk), glyph);
        }

        float fontSize = ImGui.GetFontSize();
        var textPos = new Vector2(pos.X + iconSize.X + gap, pos.Y + (size.Y - fontSize) * 0.5f);
        DrawTrackedText(dl, textPos, label, CodexChassis.U32(textInk), trackPx);
    }

    /// <summary>Heartbeat scale curve (1.0 idle → 1.18 peak twice per ~1.8s cycle).</summary>
    public static float HeartBeatScale(double time)
    {
        float t = (float)((time / 1.8) % 1.0);
        // Two beats: 0-0.15 → 1.18, 0.30 → 1.10, 0.45 → 1.0, rest idle
        return t switch
        {
            < 0.15f => 1.0f + 0.18f * MathF.Sin(t / 0.15f * MathF.PI),
            < 0.30f => 1.0f,
            < 0.45f => 1.0f + 0.10f * MathF.Sin((t - 0.30f) / 0.15f * MathF.PI),
            _       => 1.0f,
        };
    }

    // ── Design panel: collapsed dp-edge (28px wide vertical strip) ──────

    public static void DrawDpEdge(ImDrawListPtr dl, Vector2 min, Vector2 max,
        float scale, ImFontPtr iconFont, float iconFontSize,
        string chevronLeftGlyph, string countText, bool hovered)
    {
        // Background: lighter than the main window so the strip reads as distinct.
        uint top = CodexChassis.U32(hovered ? CodexChassis.Surface2 : CodexChassis.Surface1);
        uint bot = CodexChassis.U32(hovered ? CodexChassis.Surface2 : CodexChassis.Surface0);
        dl.AddRectFilledMultiColor(min, max, top, top, bot, bot);
        dl.AddLine(min, new Vector2(min.X, max.Y),
            CodexChassis.U32(CodexChassis.Border), 1f * scale);

        // Two corner brackets (top-right of strip + bottom-right), 6×6 gold L-shapes
        float bSize = 8f * scale;
        float bInset = 5f * scale;
        uint bCol = CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.Gold, 0.65f));
        var topR = new Vector2(max.X - bInset, min.Y + bInset);
        dl.AddLine(topR, new Vector2(topR.X, topR.Y + bSize), bCol, 1f * scale);
        dl.AddLine(topR, new Vector2(topR.X - bSize, topR.Y), bCol, 1f * scale);
        var botR = new Vector2(max.X - bInset, max.Y - bInset);
        dl.AddLine(botR, new Vector2(botR.X, botR.Y - bSize), bCol, 1f * scale);
        dl.AddLine(botR, new Vector2(botR.X - bSize, botR.Y), bCol, 1f * scale);

        // Chevron + label + count, vertically stacked. Layout TOP-DOWN with
        // explicit gaps so the chevron doesn't clip into the first letter.
        Vector2 size = max - min;
        float cx = min.X + size.X * 0.5f;
        float cy = min.Y + size.Y * 0.5f;

        ImGui.PushFont(iconFont);
        Vector2 chevSz = ImGui.CalcTextSize(chevronLeftGlyph);
        ImGui.PopFont();

        const string label = "DESIGNS";
        float fontSize = ImGui.GetFontSize();
        float charH = fontSize + 2f * scale;
        float labelTotalH = label.Length * charH;
        float countTotalH = string.IsNullOrEmpty(countText) ? 0f : countText.Length * charH;
        float chevGap = 10f * scale;
        float countGap = string.IsNullOrEmpty(countText) ? 0f : 10f * scale;
        float stackTotalH = chevSz.Y + chevGap + labelTotalH + countGap + countTotalH;
        float stackTopY = cy - stackTotalH * 0.5f;

        // Chevron at top of stack
        var chevInk = hovered ? CodexChassis.GoldWarm : CodexChassis.Gold;
        dl.AddText(iconFont, iconFontSize,
            new Vector2(cx - chevSz.X * 0.5f, stackTopY),
            CodexChassis.U32(chevInk), chevronLeftGlyph);

        // "DESIGNS" label below chevron, char-by-char downward
        var labelInk = hovered ? CodexChassis.Text : CodexChassis.TextDim;
        float labelStartY = stackTopY + chevSz.Y + chevGap;
        for (int i = 0; i < label.Length; i++)
        {
            string g = label.Substring(i, 1);
            float gW = ImGui.CalcTextSize(g).X;
            dl.AddText(new Vector2(cx - gW * 0.5f, labelStartY + i * charH),
                CodexChassis.U32(labelInk), g);
        }

        // Count below label
        if (!string.IsNullOrEmpty(countText))
        {
            float countStartY = labelStartY + labelTotalH + countGap;
            for (int i = 0; i < countText.Length; i++)
            {
                string g = countText.Substring(i, 1);
                float gW = ImGui.CalcTextSize(g).X;
                dl.AddText(new Vector2(cx - gW * 0.5f, countStartY + i * charH),
                    CodexChassis.U32(CodexChassis.GoldDeep), g);
            }
        }
    }

    // ── Design panel: applied row gold left bar + horizontal fade ───────

    public static void DrawAppliedRowAccent(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
    {
        float w = max.X - min.X;
        // Horizontal gold fade: gold-at-10% left → gold-at-2% at 70% → transparent right
        var goldStart = CodexChassis.WithAlpha(CodexChassis.Gold, 0.10f);
        var goldFade  = CodexChassis.WithAlpha(CodexChassis.Gold, 0.02f);
        var goldClear = CodexChassis.WithAlpha(CodexChassis.Gold, 0f);
        var midX = min.X + w * 0.7f;
        dl.AddRectFilledMultiColor(min, new Vector2(midX, max.Y),
            CodexChassis.U32(goldStart), CodexChassis.U32(goldFade),
            CodexChassis.U32(goldFade),  CodexChassis.U32(goldStart));
        dl.AddRectFilledMultiColor(new Vector2(midX, min.Y), max,
            CodexChassis.U32(goldFade),  CodexChassis.U32(goldClear),
            CodexChassis.U32(goldClear), CodexChassis.U32(goldFade));

        // 2px gold left bar, 4px inset top/bottom, with 8px glow
        float barX = min.X;
        var barMin = new Vector2(barX, min.Y + 4f * scale);
        var barMax = new Vector2(barX + 2f * scale, max.Y - 4f * scale);
        // Glow
        for (int i = 3; i > 0; i--)
        {
            float r = i * 2.5f * scale;
            dl.AddRectFilled(barMin - new Vector2(r, 0), barMax + new Vector2(r, 0),
                CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.Gold, 0.18f / i)));
        }
        dl.AddRectFilled(barMin, barMax, CodexChassis.U32(CodexChassis.Gold));
    }

    // ── Design panel: faint gold accent line on left edge of expanded panel ─

    public static void DrawDpAccentLine(ImDrawListPtr dl, Vector2 panelMin, Vector2 panelMax, float scale)
    {
        float topInset = 14f * scale;
        float botInset = 14f * scale;
        var topCol  = CodexChassis.WithAlpha(CodexChassis.Gold, 0.0f);
        var midCol1 = CodexChassis.WithAlpha(CodexChassis.Gold, 0.12f);
        var midCol2 = CodexChassis.WithAlpha(CodexChassis.Gold, 0.04f);
        // Approximate vertical gradient using 4 stacked rectangles
        float h = panelMax.Y - panelMin.Y - topInset - botInset;
        float segH = h / 3f;
        float x = panelMin.X;
        for (int i = 0; i < 3; i++)
        {
            float y0 = panelMin.Y + topInset + i * segH;
            float y1 = y0 + segH;
            Vector4 c0, c1;
            if (i == 0) { c0 = topCol;  c1 = midCol1; }
            else if (i == 1) { c0 = midCol1; c1 = midCol2; }
            else { c0 = midCol2; c1 = topCol; }
            // Re-scale c1 to gold for the bottom of segment 0/1, top of segment 2
            dl.AddRectFilledMultiColor(
                new Vector2(x, y0), new Vector2(x + 1f * scale, y1),
                CodexChassis.U32(c0), CodexChassis.U32(c0),
                CodexChassis.U32(c1), CodexChassis.U32(c1));
        }
    }

    // v6 DESIGN PANEL LIST PRIMITIVES
    // - Slip-polygon row body with vertical Surface1->Surface0 gradient
    // - Folder tab heads with TR-only chamfer
    // - Coloured spine + per-row spine ticks

    /// <summary>
    /// Row body: vertical gradient (top->bottom) inside a slip-polygon shape.
    /// Renders as a rect-with-gradient with two parent-coloured corner triangles
    /// to simulate the TR+BL chamfer cuts. parentBg should match the surface the
    /// row sits on (velvet for the design list).
    /// </summary>
    public static void DrawRowBodyGradient(ImDrawListPtr dl, Vector2 min, Vector2 max,
        Vector4 topCol, Vector4 botCol, Vector4 parentBg, float chamfer)
    {
        uint top = CodexChassis.U32(topCol);
        uint bot = CodexChassis.U32(botCol);
        dl.AddRectFilledMultiColor(min, max, top, top, bot, bot);

        // Cut TR + BL corners with parent-coloured triangles
        uint pc = CodexChassis.U32(parentBg);
        dl.AddTriangleFilled(
            new Vector2(max.X - chamfer, min.Y),
            new Vector2(max.X, min.Y),
            new Vector2(max.X, min.Y + chamfer),
            pc);
        dl.AddTriangleFilled(
            new Vector2(min.X, max.Y - chamfer),
            new Vector2(min.X, max.Y),
            new Vector2(min.X + chamfer, max.Y),
            pc);
    }

    /// <summary>1px inset stroke along the slip-polygon edges at low alpha.</summary>
    public static void DrawRowInsetHairline(ImDrawListPtr dl, Vector2 min, Vector2 max,
        float chamfer, Vector4 col)
    {
        CodexChassis.StrokeSlip(dl, min, max, chamfer, CodexChassis.U32(col), 1f);
    }

    /// <summary>
    /// TR diagonal corner glow, soft directional gradient via a single
    /// AddRectFilledMultiColor: bright at the TR vertex, transparent on the
    /// other three corners. True linear falloff, no polygon-overlap halo.
    /// Caller passes max with X = row right edge and Y = row top.
    /// </summary>
    public static void DrawRowCornerGlow(ImDrawListPtr dl, Vector2 max, float scale,
        Vector4 glowCol, float strength)
    {
        if (strength <= 0f) return;
        float reach = 36f * scale;
        var pMin = new Vector2(max.X - reach, max.Y);
        var pMax = new Vector2(max.X, max.Y + reach);
        uint bright = CodexChassis.U32(CodexChassis.WithAlpha(glowCol, strength));
        uint clear  = CodexChassis.U32(CodexChassis.WithAlpha(glowCol, 0f));
        // p_min, p_max, col_upr_left, col_upr_right, col_bot_right, col_bot_left
        dl.AddRectFilledMultiColor(pMin, pMax, clear, bright, clear, clear);
    }


    /// <summary>
    /// 2px-wide left rail with smooth aurora glow (stacked ellipses for a soft,
    /// non-stair-stepped halo). X is the rail's centre line; midY is the row centre.
    /// </summary>
    public static void DrawRowRail(ImDrawListPtr dl, float x, float midY, float halfH,
        float scale, Vector4 col, float glowAlpha = 0f, float glowRadius = 0f)
    {
        // Smooth glow halo via multi-layer ellipses, same primitive as DrawAuroraSpot
        if (glowAlpha > 0f && glowRadius > 0f)
        {
            var glowCol = CodexChassis.WithAlpha(col, glowAlpha);
            // Tall narrow ellipse centred on the rail
            DrawAuroraSpot(dl, new Vector2(x + 1f * scale, midY),
                glowRadius, halfH + glowRadius * 0.4f, glowCol, 10);
        }
        // Crisp 2px rail on top
        dl.AddRectFilled(
            new Vector2(x, midY - halfH),
            new Vector2(x + 2f * scale, midY + halfH),
            CodexChassis.U32(col));
    }

    /// <summary>
    /// Black-on-gold APPLIED chip, patch-notes LATEST DNA. Renders at the given
    /// position. Caller is responsible for sizing/placement; returns the chip's
    /// width so the caller can offset other elements.
    /// </summary>
    public static float DrawAppliedChip(ImDrawListPtr dl, Vector2 pos, float scale, string label = "APPLIED")
    {
        Vector2 ts = ImGui.CalcTextSize(label);
        float padX = 7f * scale;
        float padY = 3f * scale;
        float w = ts.X + padX * 2;
        float h = ts.Y + padY * 2;
        var min = pos;
        var max = new Vector2(pos.X + w, pos.Y + h);

        // Soft 3-layer halo behind the chip
        for (int i = 3; i > 0; i--)
        {
            float r = i * 2f * scale;
            dl.AddRectFilled(min - new Vector2(r, r * 0.4f), max + new Vector2(r, r * 0.4f),
                CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.Gold, 0.14f / i)));
        }
        dl.AddRectFilled(min, max, CodexChassis.U32(CodexChassis.Gold));
        dl.AddText(new Vector2(min.X + padX, min.Y + padY),
            CodexChassis.U32(new Vector4(0x1A / 255f, 0x15 / 255f, 0x00 / 255f, 1f)), label);
        return w;
    }

    /// <summary>
    /// Folder tab head shape: top-left flat, top-right chamfered, bottom flat.
    /// Returns the polygon points so the caller can draw fills/borders.
    /// </summary>
    public static void DrawFolderTabBody(ImDrawListPtr dl, Vector2 min, Vector2 max,
        Vector4 topCol, Vector4 botCol, Vector4 parentBg, float chamfer)
    {
        uint top = CodexChassis.U32(topCol);
        uint bot = CodexChassis.U32(botCol);
        dl.AddRectFilledMultiColor(min, max, top, top, bot, bot);
        // Cut TR corner only
        uint pc = CodexChassis.U32(parentBg);
        dl.AddTriangleFilled(
            new Vector2(max.X - chamfer, min.Y),
            new Vector2(max.X, min.Y),
            new Vector2(max.X, min.Y + chamfer),
            pc);
    }

    /// <summary>
    /// Folder top binding: 2px coloured stripe along the top edge of the folder
    /// head with a smooth aurora glow above (centred on the binding) instead of
    /// stacked rect-borders.
    /// </summary>
    public static void DrawFolderTopBinding(ImDrawListPtr dl, Vector2 headMin, Vector2 headMax,
        float scale, Vector4 col, float chamfer)
    {
        float bindingW = (headMax.X - chamfer) - headMin.X;
        var bindingCentre = new Vector2(headMin.X + bindingW * 0.5f, headMin.Y + 1f * scale);

        // Smooth aurora halo above the binding
        DrawAuroraSpot(dl, bindingCentre,
            bindingW * 0.5f, 6f * scale,
            CodexChassis.WithAlpha(col, 0.40f), 8);

        // Solid 2px top binding
        dl.AddRectFilled(
            new Vector2(headMin.X, headMin.Y),
            new Vector2(headMax.X - chamfer, headMin.Y + 2f * scale),
            CodexChassis.U32(col));
    }

    /// <summary>
    /// Folder spine: 1.5px vertical line in folder colour with smooth alpha
    /// fade (8 stacked segments, no banding) from head-bottom to body-bottom.
    /// Caps with a small rotated diamond rather than an awkward right-angle hook.
    /// </summary>
    public static void DrawFolderSpine(ImDrawListPtr dl, float x, float topY, float botY,
        float scale, Vector4 col)
    {
        if (botY <= topY) return;
        float h = botY - topY;
        float w = 1.5f * scale;
        const int seg = 8;
        for (int i = 0; i < seg; i++)
        {
            float t0 = i / (float)seg;
            float t1 = (i + 1) / (float)seg;
            float a0 = MathF.Max(0f, 0.55f * (1f - t0 * 0.85f));
            float a1 = MathF.Max(0f, 0.55f * (1f - t1 * 0.85f));
            uint c0 = CodexChassis.U32(CodexChassis.WithAlpha(col, a0));
            uint c1 = CodexChassis.U32(CodexChassis.WithAlpha(col, a1));
            dl.AddRectFilledMultiColor(
                new Vector2(x, topY + t0 * h), new Vector2(x + w, topY + t1 * h),
                c0, c0, c1, c1);
        }
        // Diamond cap at the spine bottom (rotated 3px square)
        float capR = 2.5f * scale;
        var capC = new Vector2(x + w * 0.5f, botY + 1f * scale);
        uint capCol = CodexChassis.U32(CodexChassis.WithAlpha(col, 0.40f));
        dl.AddTriangleFilled(
            capC + new Vector2(0, -capR),
            capC + new Vector2(capR, 0),
            capC + new Vector2(0, capR), capCol);
        dl.AddTriangleFilled(
            capC + new Vector2(0, -capR),
            capC + new Vector2(-capR, 0),
            capC + new Vector2(0, capR), capCol);
    }

    /// <summary>
    /// Spine tick: thin horizontal line that connects the spine to the row's
    /// left edge so the row visibly "branches off" the spine. Multi-colour
    /// fade from solid at the spine to ~50% alpha at the row edge.
    /// </summary>
    public static void DrawSpineTick(ImDrawListPtr dl, float spineX, float rowLeftX, float midY,
        float scale, Vector4 col, bool isApplied)
    {
        float w = MathF.Max(4f * scale, rowLeftX - spineX);
        float thick = isApplied ? 1.5f * scale : 1f * scale;
        float a = isApplied ? 0.85f : 0.45f;

        if (isApplied)
        {
            for (int i = 2; i > 0; i--)
            {
                float r = i * 2f * scale;
                dl.AddRectFilled(
                    new Vector2(spineX - r * 0.5f, midY - thick * 0.5f - r * 0.5f),
                    new Vector2(spineX + w + r, midY + thick * 0.5f + r * 0.5f),
                    CodexChassis.U32(CodexChassis.WithAlpha(col, 0.22f / i)));
            }
        }
        uint cStart = CodexChassis.U32(CodexChassis.WithAlpha(col, a));
        uint cEnd   = CodexChassis.U32(CodexChassis.WithAlpha(col, a * 0.5f));
        dl.AddRectFilledMultiColor(
            new Vector2(spineX, midY - thick * 0.5f),
            new Vector2(spineX + w, midY + thick * 0.5f),
            cStart, cEnd, cEnd, cStart);
    }

    /// <summary>Backwards-compat overload (5-arg). Uses a default tick width.</summary>
    public static void DrawSpineTick(ImDrawListPtr dl, float spineX, float midY, float scale,
        Vector4 col, bool isApplied)
    {
        float defaultW = isApplied ? 8f * scale : 6f * scale;
        DrawSpineTick(dl, spineX, spineX + defaultW, midY, scale, col, isApplied);
    }

    /// <summary>
    /// Per-row hover lift offset eased over a fixed duration. Returns the Y
    /// offset to translate the row by (0 .. -maxLift). Use a per-row state
    /// dictionary keyed by stable row id to track the eased value frame-to-frame.
    /// </summary>
    public static float EasedLift(float t, float maxLift)
    {
        // ease-out-back: cubic-bezier(0.34, 1.2, 0.42, 1)
        if (t <= 0f) return 0f;
        if (t >= 1f) return -maxLift;
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        float v = 1f + c3 * MathF.Pow(t - 1f, 3) + c1 * MathF.Pow(t - 1f, 2);
        return -v * maxLift;
    }

    // FORM PRIMITIVES, header, sections, inputs, footer (gold pill + cancel)

    /// <summary>
    /// Pushes a coherent boutique style stack for form inputs. Apply BEFORE drawing
    /// inputs (text fields, combos, sliders, color pickers). Pop with PopFormStyle.
    /// Pushes 14 colours and 4 vars.
    /// </summary>
    public static void PushFormStyle()
    {
        // Each editor-exposed slot consults the user's override first via
        // Boutique.SlotOrDefault and only falls back to the boutique form
        // default when unset. Every entry in the Custom Theme editor (Input
        // Fields, Buttons, Text, Popup/Tooltip, Separators) actually drives
        // the Add/Edit Character/Design forms now.
        ImGui.PushStyleColor(ImGuiCol.FrameBg,        Boutique.SlotOrDefault("color.frameBg",         new Vector4(0.058f, 0.067f, 0.094f, 1.0f)));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Boutique.SlotOrDefault("color.frameBgHovered",  new Vector4(0.078f, 0.090f, 0.118f, 1.0f)));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive,  Boutique.SlotOrDefault("color.frameBgActive",   new Vector4(0.094f, 0.110f, 0.141f, 1.0f)));
        ImGui.PushStyleColor(ImGuiCol.Border,         CodexChassis.WithAlpha(CodexChassis.Gold, 0.18f));
        ImGui.PushStyleColor(ImGuiCol.Button,         Boutique.SlotOrDefault("color.button",          new Vector4(20f / 255f, 24f / 255f, 32f / 255f, 0.7f)));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered,  Boutique.SlotOrDefault("color.buttonHovered",   CodexChassis.WithAlpha(CodexChassis.Gold, 0.18f)));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive,   Boutique.SlotOrDefault("color.buttonActive",    CodexChassis.WithAlpha(CodexChassis.Gold, 0.28f)));
        ImGui.PushStyleColor(ImGuiCol.Header,         CodexChassis.WithAlpha(CodexChassis.Gold, 0.10f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered,  CodexChassis.WithAlpha(CodexChassis.Gold, 0.20f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive,   CodexChassis.WithAlpha(CodexChassis.Gold, 0.30f));
        ImGui.PushStyleColor(ImGuiCol.Text,           Boutique.SlotOrDefault("color.text",           CodexChassis.Text));
        ImGui.PushStyleColor(ImGuiCol.TextDisabled,   Boutique.SlotOrDefault("color.textDisabled",   CodexChassis.TextFaint));
        ImGui.PushStyleColor(ImGuiCol.PopupBg,        Boutique.SlotOrDefault("color.popupBg",        new Vector4(0.04f, 0.05f, 0.08f, 0.97f)));
        ImGui.PushStyleColor(ImGuiCol.Separator,      Boutique.SlotOrDefault("color.separator",      CodexChassis.WithAlpha(CodexChassis.Gold, 0.20f)));

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
        // Tighter input row so the label (now bigger than the input value) dominates
        // visually. OutfitBody12 = 15.6px + FramePadding.y 4*fs (= 5.2px each side)
        // gives input row of ~26px. The label above (Oswald Semi 13 = 16.9px) ends
        // up roughly 65% of the input row's height, flips the old proportion where
        // the input dwarfed the label.
        float s = CodexChassis.FormScale;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,  new Vector2(8f * s, 4f * s));
        // Mockup .field { gap: 5px }. ItemSpacing.y carries label-to-input AND
        // field-to-field gaps; field rows no longer add an explicit Dummy on top.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,   new Vector2(6f * s, 4f * s));
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 0f);
    }

    public static void PopFormStyle()
    {
        ImGui.PopStyleColor(14);
        ImGui.PopStyleVar(4);
    }

    /// <summary>
    /// Renders a 40px form header bar with: 2px gold top binding, optional
    /// nameplate-coloured pip, tracked-caps title (kicker · pip · NAME), X close
    /// button on the right. Returns true if the close button was clicked.
    /// </summary>
    public static bool DrawFormHeader(ImDrawListPtr dl, Vector2 min, Vector2 max,
        float scale, string kicker, string title, Vector4? npCol,
        ImFontPtr labelFont, ImFontPtr titleFont, ImFontPtr iconFont, string xGlyph)
    {
        // Background gradient
        uint top = CodexChassis.U32(CodexChassis.Surface2);
        uint bot = CodexChassis.U32(CodexChassis.Surface1);
        dl.AddRectFilledMultiColor(min, max, top, top, bot, bot);

        // Bottom hairline
        dl.AddLine(new Vector2(min.X, max.Y - 1f * scale),
                   new Vector2(max.X, max.Y - 1f * scale),
                   CodexChassis.U32(CodexChassis.BorderSoft), 1f * scale);

        // 2px gold top binding with halo
        var bindingC = new Vector2((min.X + max.X) * 0.5f, min.Y + 1f * scale);
        DrawAuroraSpot(dl, bindingC, (max.X - min.X) * 0.45f, 5f * scale,
            CodexChassis.WithAlpha(CodexChassis.Gold, 0.35f), 6);
        dl.AddRectFilled(min, new Vector2(max.X, min.Y + 2f * scale),
            CodexChassis.U32(CodexChassis.Gold));

        float padX = 12f * scale;
        float midY = (min.Y + max.Y) * 0.5f;
        float cursorX = min.X + padX;

        // Kicker (e.g. "EDIT CHARACTER" / "NEW CHARACTER"), Oswald Med 11 tracked 0.32em
        if (!string.IsNullOrEmpty(kicker))
        {
            ImGui.PushFont(labelFont);
            float kickerH = ImGui.GetFontSize();
            float kickerTrack = labelFont.FontSize * 0.32f;
            float kickerW = MeasureTrackedText(kicker, kickerTrack);
            DrawTrackedText(dl,
                new Vector2(cursorX, midY - kickerH * 0.5f),
                kicker, CodexChassis.U32(CodexChassis.TextDim), kickerTrack);
            ImGui.PopFont();
            cursorX += kickerW + 10f * scale;

            // Diamond separator
            var sepC = new Vector2(cursorX + 2.5f * scale, midY);
            dl.AddTriangleFilled(
                sepC + new Vector2(0, -2.5f * scale),
                sepC + new Vector2(2.5f * scale, 0),
                sepC + new Vector2(0, 2.5f * scale),
                CodexChassis.U32(CodexChassis.GoldDeep));
            dl.AddTriangleFilled(
                sepC + new Vector2(0, -2.5f * scale),
                sepC + new Vector2(0, 2.5f * scale),
                sepC + new Vector2(-2.5f * scale, 0),
                CodexChassis.U32(CodexChassis.GoldDeep));
            cursorX += 12f * scale;
        }

        // Nameplate pip (if editing existing)
        if (npCol.HasValue)
        {
            DrawSquarePip(dl, new Vector2(cursorX + 3.5f * scale, midY), 3.5f * scale, npCol.Value);
            cursorX += 14f * scale;
        }

        // X close button on the right
        float xSize = 24f * scale;
        var xMin = new Vector2(max.X - padX - xSize, midY - xSize * 0.5f);
        ImGui.SetCursorScreenPos(xMin);
        bool xClicked = ImGui.InvisibleButton("##bform_close", new Vector2(xSize, xSize));
        bool xHovered = ImGui.IsItemHovered();
        uint xBg = xHovered
            ? CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.Red, 0.20f))
            : CodexChassis.U32(Boutique.SlotOrDefault("color.button", new Vector4(20f / 255f, 24f / 255f, 32f / 255f, 0.6f)));
        dl.AddRectFilled(xMin, xMin + new Vector2(xSize, xSize), xBg);
        dl.AddRect(xMin, xMin + new Vector2(xSize, xSize),
            CodexChassis.U32(xHovered ? CodexChassis.Red : CodexChassis.BorderSoft),
            0f, ImDrawFlags.None, 1f * scale);
        ImGui.PushFont(iconFont);
        var xs = ImGui.CalcTextSize(xGlyph);
        ImGui.PopFont();
        float xIconSize = 12f * scale;
        float xScale = xIconSize / iconFont.FontSize;
        dl.AddText(iconFont, xIconSize,
            xMin + new Vector2((xSize - xs.X * xScale) * 0.5f, (xSize - xs.Y * xScale) * 0.5f),
            CodexChassis.U32(xHovered ? CodexChassis.Red : CodexChassis.TextDim), xGlyph);

        // Title (between kicker/pip and X), Oswald Semi 14 tracked 0.20em
        float titleMaxX = xMin.X - 10f * scale;
        if (!string.IsNullOrEmpty(title))
        {
            ImGui.PushFont(titleFont);
            float titleH = ImGui.GetFontSize();
            float titleTrack = titleFont.FontSize * 0.20f;
            float titleY = midY - titleH * 0.5f;
            dl.PushClipRect(new Vector2(cursorX, min.Y), new Vector2(titleMaxX, max.Y), true);
            DrawTrackedText(dl,
                new Vector2(cursorX, titleY),
                title, CodexChassis.U32(CodexChassis.Text), titleTrack);
            dl.PopClipRect();
            ImGui.PopFont();
        }

        return xClicked;
    }

    /// <summary>
    /// Section divider: tracked-caps label with a gold-deep hairline trailing
    /// to the right. `maxWidth` optionally caps the hairline length.
    /// </summary>
    public static void DrawSimpleSectionLabel(string label, float scale, float maxWidth = 0f)
    {
        if (string.IsNullOrEmpty(label)) return;

        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        float availW = maxWidth > 0f ? maxWidth : ImGui.GetContentRegionAvail().X;

        var font = ImGui.GetFont();
        float fontH = font.FontSize;
        float track = font.FontSize * 0.30f;
        float labelW = MeasureTrackedText(label, track);
        // Lifted to Text (was TextDim) so section labels read clearly against
        // the form bg. The hairline rule still does the visual break job.
        DrawTrackedText(dl, pos, label, CodexChassis.U32(CodexChassis.Text), track);

        // Hairline at gold-deep low alpha, visibly marks the section without
        // shouting. Sits centred against the label baseline.
        float hairY = pos.Y + fontH * 0.55f;
        float hairStartX = pos.X + labelW + 12f * scale;
        float hairEndX = pos.X + availW;
        if (hairEndX > hairStartX)
        {
            dl.AddLine(new Vector2(hairStartX, hairY), new Vector2(hairEndX, hairY),
                CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.GoldDeep, 0.45f)),
                1f * scale);
        }

        // Advance cursor past the label row
        ImGui.Dummy(new Vector2(availW, fontH));
    }

    public static void DrawSectionHead(string roman, string title, string meta,
        ImFontPtr smallFont, ImFontPtr titleFont, float scale)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        float availW = ImGui.GetContentRegionAvail().X;
        float baselineY = pos.Y;

        // Roman numeral, Oswald Med 9 tracked 0.28em, gold-deep
        float cursorX = pos.X;
        if (!string.IsNullOrEmpty(roman))
        {
            ImGui.PushFont(smallFont);
            float romanTrack = smallFont.FontSize * 0.28f;
            float romanW = MeasureTrackedText(roman, romanTrack);
            DrawTrackedText(dl,
                new Vector2(cursorX, baselineY),
                roman, CodexChassis.U32(CodexChassis.GoldDeep), romanTrack);
            ImGui.PopFont();
            cursorX += romanW + 10f * scale;
        }

        // Title, Oswald Semi 11 tracked 0.40em, Text
        Vector2 tsz = default;
        if (!string.IsNullOrEmpty(title))
        {
            ImGui.PushFont(titleFont);
            float titleH = ImGui.GetFontSize();
            float titleTrack = titleFont.FontSize * 0.40f;
            float titleW = MeasureTrackedText(title, titleTrack);
            DrawTrackedText(dl,
                new Vector2(cursorX, baselineY),
                title, CodexChassis.U32(CodexChassis.Text), titleTrack);
            ImGui.PopFont();
            tsz = new Vector2(titleW, titleH);
        }

        // Meta on the right, small font tracked 0.30em, TextFaint
        if (!string.IsNullOrEmpty(meta))
        {
            ImGui.PushFont(smallFont);
            float metaTrack = smallFont.FontSize * 0.30f;
            float metaW = MeasureTrackedText(meta, metaTrack);
            DrawTrackedText(dl,
                new Vector2(pos.X + availW - metaW, baselineY + 2f * scale),
                meta, CodexChassis.U32(CodexChassis.TextFaint), metaTrack);
            ImGui.PopFont();
        }

        float fontH = MathF.Max(tsz.Y, 12f * scale);
        // Gold-fade rule below
        float ruleY = baselineY + fontH + 3f * scale;
        uint goldStart = CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.GoldDeep, 0.65f));
        uint goldFade  = CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.Gold, 0.15f));
        uint goldClear = CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.Gold, 0f));
        // 2 segments to approximate the smooth fade
        dl.AddRectFilledMultiColor(
            new Vector2(pos.X, ruleY),
            new Vector2(pos.X + availW * 0.6f, ruleY + 1f),
            goldStart, goldFade, goldFade, goldStart);
        dl.AddRectFilledMultiColor(
            new Vector2(pos.X + availW * 0.6f, ruleY),
            new Vector2(pos.X + availW, ruleY + 1f),
            goldFade, goldClear, goldClear, goldFade);

        // Advance cursor past the heading + rule. Tight 4*fs trailing, the
        // parent uses ItemSpacing.y for the gap to the first field below.
        ImGui.Dummy(new Vector2(0, fontH + 4f * CodexChassis.FormScale));
    }

    /// <summary>
    /// Gold-pill SAVE button (chamfered TR+BL, halo, sheen on hover, top-edge
    /// gold-bright highlight). Returns true if clicked. Disabled state dims the
    /// gradient and ignores clicks.
    /// </summary>
    public static bool DrawSavePill(ImDrawListPtr dl, Vector2 min, Vector2 max,
        string label, float trackPx, float scale, string id, bool disabled,
        Func<string, bool, float> sheenProvider, string disabledReason = null)
    {
        ImGui.SetCursorScreenPos(min);
        bool clicked = ImGui.InvisibleButton($"##bsave_{id}", max - min);
        if (disabled) clicked = false;
        bool rawHovered = ImGui.IsItemHovered();
        bool hovered = !disabled && rawHovered;
        // When disabled, show a boutique tooltip explaining why
        if (disabled && rawHovered && !string.IsNullOrEmpty(disabledReason))
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 7f));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.04f, 0.05f, 0.08f, 0.96f));
            ImGui.PushStyleColor(ImGuiCol.Border, CodexChassis.WithAlpha(CodexChassis.Red, 0.55f));
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(280f);
            ImGui.TextColored(CodexChassis.Red, "CAN'T SAVE");
            ImGui.TextColored(CodexChassis.Text, disabledReason);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar();
        }

        // Match the ADD CHARACTER button's style exactly: just the gold pill
        // body (which already paints its own top highlight) + sheen on hover.
        // The previous aurora-spot halo + extra top-edge highlight was over-doing
        // the glow and reading as bad/blurry.
        DrawGoldPill(dl, min, max, label, trackPx, scale, hovered, showPlus: false);

        if (disabled)
        {
            // Dim veil over the disabled pill so it reads as inactive.
            dl.AddRectFilled(min, max,
                CodexChassis.U32(new Vector4(0.05f, 0.05f, 0.08f, 0.55f)));
        }
        else
        {
            float sheen = sheenProvider($"bsave_{id}", hovered);
            if (sheen >= 0f)
                Windows.Styles.UIStyles.DrawHoverSheen(dl, min, max, sheen, maxAlpha: 0.30f);
        }

        return clicked;
    }

    /// <summary>
    /// Neutral chamfered cancel button. Returns true if clicked.
    /// </summary>
    public static bool DrawCancelBtn(ImDrawListPtr dl, Vector2 min, Vector2 max,
        string label, float trackPx, float scale, string id,
        ImFontPtr font)
    {
        ImGui.SetCursorScreenPos(min);
        bool clicked = ImGui.InvisibleButton($"##bcancel_{id}", max - min);
        bool hovered = ImGui.IsItemHovered();

        Span<Vector2> pts = stackalloc Vector2[6];
        CodexChassis.BuildSlipPolygon(min, max, 6f * scale, pts);
        Vector4 bgCol = hovered
            ? new Vector4(0.11f, 0.13f, 0.17f, 0.92f)
            : new Vector4(0.08f, 0.10f, 0.13f, 0.85f);
        unsafe
        {
            fixed (Vector2* p = pts)
                dl.AddConvexPolyFilled(p, 6, CodexChassis.U32(bgCol));
        }
        // Stroke
        for (int i = 0; i < 6; i++)
            dl.PathLineTo(pts[i]);
        dl.PathStroke(CodexChassis.U32(hovered ? CodexChassis.TextDim : CodexChassis.BorderSoft),
            ImDrawFlags.Closed, 1f * scale);

        // Label, bright Text by default (was TextDim, which read as washed
        // out against the dark cancel fill).
        ImGui.PushFont(font);
        Vector2 ls = ImGui.CalcTextSize(label);
        ImGui.PopFont();
        Vector4 inkCol = hovered ? CodexChassis.GoldBright : CodexChassis.Text;
        dl.AddText(font, font.FontSize,
            min + (max - min - ls) * 0.5f,
            CodexChassis.U32(inkCol), label);

        return clicked;
    }

    /// <summary>
    /// Tracked-caps Oswald label above an input. Optional required-asterisk and
    /// info tooltip glyph. Caller must push the label font (OswaldSemi9) before
    /// calling this and pop after.
    /// </summary>
    public static void DrawFieldLabel(string label, bool required, string tooltip = null)
    {
        // v2-simple spec: tracked 0.20em (was 0.32em, that read as editorial chrome).
        // Smaller tracking + slightly brighter colour so labels function as readable
        // content, not as decorative tracked-caps. Caller pushes the font (typically
        // OswaldMed10).
        var dl = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        float fontH = ImGui.GetFontSize();
        float track = font.FontSize * 0.20f;

        // Caller is responsible for pushing the right font (OswaldSemi9). We measure
        // and render via DrawTrackedText to give the label proper letter-spacing.
        float labelW = MeasureTrackedText(label, track);

        var pos = ImGui.GetCursorScreenPos();
        // Brighter label colour (was TextDim, too low-contrast against the form
        // bg). Matches the dropdown value text colour so labels and inputs read
        // at similar weight.
        DrawTrackedText(dl, pos, label, CodexChassis.U32(CodexChassis.Text), track);

        // Reserve a row of fontH height for the label + advance cursor naturally.
        // Use ImGui.Dummy with the label's full row width so subsequent SameLine
        // / SetCursor positions are correct, and ItemSpacing.y kicks in afterward.
        // Dummy advances the cursor past this row.
        float trailingX = labelW + 5f;

        // Required marker, slightly bigger draw size so the asterisk reads
        // even at small label sizes. Bumped down 1px so it doesn't visually
        // merge into the next character's cap line, Oswald's `*` floats high
        // above the baseline at the cap height.
        if (required)
        {
            float starX = pos.X + trailingX;
            float starSize = font.FontSize * 1.15f;
            ImGui.PushFont(font);
            float starW = ImGui.CalcTextSize("*").X * 1.15f;
            ImGui.PopFont();
            dl.AddText(font, starSize,
                new Vector2(starX, pos.Y - 1f),
                CodexChassis.U32(CodexChassis.GoldWarm), "*");
            trailingX += starW + 4f;
        }

        // Info icon, single FontAwesome `info-circle` glyph (). Replaces
        // the previous hand-drawn circle + dot/stem combo where the manual `i`
        // looked lost inside the ring. The glyph is one cohesive shape, scales
        // crisply at any size, and reads as a recognisable affordance.
        if (!string.IsNullOrEmpty(tooltip))
        {
            float scale = CodexChassis.FormScale;
            float iconSize = 14f * scale;
            float iconLeft = pos.X + trailingX + 4f;
            float iconY = pos.Y + (fontH - iconSize) * 0.5f;
            var iconMin = new Vector2(iconLeft, iconY);

            ImGui.SetCursorScreenPos(iconMin);
            ImGui.InvisibleButton("##field_info_" + label, new Vector2(iconSize, iconSize));
            bool hovered = ImGui.IsItemHovered();
            Vector4 inkC = hovered ? CodexChassis.Gold : CodexChassis.GoldDeep;

            var iconFont = Dalamud.Interface.UiBuilder.IconFont;
            float glyphScale = iconSize / iconFont.FontSize;
            ImGui.PushFont(iconFont);
            var glyphSz = ImGui.CalcTextSize("");
            ImGui.PopFont();
            dl.AddText(iconFont, iconSize,
                new Vector2(iconMin.X + (iconSize - glyphSz.X * glyphScale) * 0.5f,
                            iconMin.Y + (iconSize - glyphSz.Y * glyphScale) * 0.5f),
                CodexChassis.U32(inkC), "");

            if (hovered && !string.IsNullOrEmpty(tooltip))
            {
                // Tooltip uses OutfitMed13 (16.9px Medium), heavier stroke than
                // Regular at the same size = easier to read at first glance, no
                // squinting. Wider wrap and breathing space.
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 9f));
                ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.04f, 0.05f, 0.08f, 0.97f));
                ImGui.PushStyleColor(ImGuiCol.Border, CodexChassis.WithAlpha(CodexChassis.Gold, 0.55f));
                ImGui.BeginTooltip();
                using (CharacterSelectPlugin.Plugin.Instance?.OutfitMed13?.Push())
                {
                    ImGui.PushTextWrapPos(320f);
                    ImGui.TextColored(CodexChassis.Text, tooltip);
                    ImGui.PopTextWrapPos();
                }
                ImGui.EndTooltip();
                ImGui.PopStyleColor(2);
                ImGui.PopStyleVar();
            }

            // Restore cursor to row-start so the next Dummy advance is correct
            ImGui.SetCursorScreenPos(pos);
        }

        // Advance cursor past the label row. ItemSpacing.y will add the 5px gap to
        // the input that follows.
        ImGui.Dummy(new Vector2(labelW, fontH));
    }

    /// <summary>
    /// 28x28 chamfered colour swatch. Click opens ImGui's standard colour
    /// picker popup (which inherits the boutique form style stack). Returns
    /// true if the colour was changed this frame.
    /// </summary>
    public static bool DrawBoutiqueColorSwatch(string id, ref Vector3 colour, float scale)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        float side = 28f * scale;
        ImGui.SetCursorScreenPos(pos);
        ImGui.InvisibleButton($"##bswatch_{id}", new Vector2(side, side));
        bool hovered = ImGui.IsItemHovered();
        bool clicked = ImGui.IsItemClicked();

        // Slip polygon fill
        var swatchCol = new Vector4(colour.X, colour.Y, colour.Z, 1f);
        Span<Vector2> pts = stackalloc Vector2[6];
        CodexChassis.BuildSlipPolygon(pos, pos + new Vector2(side, side), 5f * scale, pts);
        unsafe
        {
            fixed (Vector2* p = pts)
                dl.AddConvexPolyFilled(p, 6, CodexChassis.U32(swatchCol));
        }
        // Border
        for (int i = 0; i < 6; i++) dl.PathLineTo(pts[i]);
        dl.PathStroke(CodexChassis.U32(hovered ? CodexChassis.Gold : CodexChassis.BorderSoft),
            ImDrawFlags.Closed, 1f * scale);

        // Tiny corner tick (TL)
        dl.AddLine(pos + new Vector2(2f, 2f), pos + new Vector2(6f, 2f),
            CodexChassis.U32(new Vector4(1f, 1f, 1f, 0.20f)), 1f);
        dl.AddLine(pos + new Vector2(2f, 2f), pos + new Vector2(2f, 6f),
            CodexChassis.U32(new Vector4(1f, 1f, 1f, 0.20f)), 1f);

        if (clicked) ImGui.OpenPopup($"##bcolorpicker_{id}");

        bool changed = false;
        if (ImGui.BeginPopup($"##bcolorpicker_{id}"))
        {
            changed = ImGui.ColorPicker3($"##picker_{id}", ref colour,
                ImGuiColorEditFlags.NoLabel);
            ImGui.EndPopup();
        }
        return changed;
    }

    /// <summary>
    /// 16x16 chamfered checkbox + label + optional description on the right.
    /// Whole row is clickable. Returns true if state changed.
    /// </summary>
    public static bool DrawBoutiqueCheckbox(string id, ref bool value, string label,
        string description, float scale, ImFontPtr labelFont, ImFontPtr descFont)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        float availW = ImGui.GetContentRegionAvail().X;

        ImGui.PushFont(labelFont);
        Vector2 ls = ImGui.CalcTextSize(label);
        ImGui.PopFont();
        ImGui.PushFont(descFont);
        Vector2 ds = string.IsNullOrEmpty(description) ? Vector2.Zero : ImGui.CalcTextSize(description);
        ImGui.PopFont();

        float boxSize = 16f * scale;
        float rowH = MathF.Max(boxSize + 4f, ls.Y + ds.Y + 6f);
        // Hit zone width: box + gap (10*scale) + the wider of label/desc + a
        // small comfort pad.  Was using availW which made the whole row width
        // hit-clickable from anywhere across the form.
        float textW = MathF.Max(ls.X, ds.X);
        float gap = 10f * scale;
        float hitW = MathF.Min(availW, boxSize + gap + textW + 6f * scale);
        ImGui.SetCursorScreenPos(pos);
        ImGui.InvisibleButton($"##bcb_{id}", new Vector2(hitW, rowH));
        bool hovered = ImGui.IsItemHovered();
        bool clicked = ImGui.IsItemClicked();

        // Box (slip polygon)
        var boxMin = new Vector2(pos.X, pos.Y + (rowH - boxSize) * 0.5f);
        Span<Vector2> bpts = stackalloc Vector2[6];
        CodexChassis.BuildSlipPolygon(boxMin, boxMin + new Vector2(boxSize, boxSize), 3f * scale, bpts);
        if (value)
        {
            unsafe
            {
                fixed (Vector2* p = bpts)
                    dl.AddConvexPolyFilled(p, 6, CodexChassis.U32(CodexChassis.Gold));
            }
            DrawAuroraSpot(dl, boxMin + new Vector2(boxSize * 0.5f, boxSize * 0.5f),
                boxSize * 0.7f, boxSize * 0.7f,
                CodexChassis.WithAlpha(CodexChassis.Gold, 0.40f), 6);
            // Check glyph, FontAwesome `check` rendered properly centred in the
            // box. The previous two-line manual draw had visible joints and didn't
            // align, which made the check look amateur.
            var iconFont = Dalamud.Interface.UiBuilder.IconFont;
            float iconSize = boxSize * 0.78f;
            float iconScale = iconSize / iconFont.FontSize;
            ImGui.PushFont(iconFont);
            var checkSz = ImGui.CalcTextSize("");
            ImGui.PopFont();
            var checkPos = new Vector2(
                MathF.Round(boxMin.X + (boxSize - checkSz.X * iconScale) * 0.5f),
                MathF.Round(boxMin.Y + (boxSize - checkSz.Y * iconScale) * 0.5f));
            dl.AddText(iconFont, iconSize, checkPos,
                CodexChassis.U32(new Vector4(0x1A / 255f, 0x15 / 255f, 0f, 1f)),
                "");
        }
        else
        {
            // Off state: dark fill + ALWAYS-visible gold-deep border so the box
            // looks intentional/clickable even when not hovered. Hover lifts the
            // border to bright Gold + a subtle gold-at-8% bg fill as feedback.
            Vector4 boxBg = hovered
                ? CodexChassis.WithAlpha(CodexChassis.Gold, 0.08f)
                : new Vector4(20f / 255f, 24f / 255f, 32f / 255f, 0.7f);
            unsafe
            {
                fixed (Vector2* p = bpts)
                    dl.AddConvexPolyFilled(p, 6, CodexChassis.U32(boxBg));
            }
            for (int i = 0; i < 6; i++) dl.PathLineTo(bpts[i]);
            dl.PathStroke(CodexChassis.U32(hovered ? CodexChassis.Gold : CodexChassis.GoldDeep),
                ImDrawFlags.Closed, 1f * scale);
        }

        // Label + description. Label always at full Text colour (was dimming when
        // unchecked, which made unchecked rows look disabled). Description in TextDim
        // (was TextFaint, almost invisible).
        float textX = boxMin.X + boxSize + 10f * scale;
        float textY = pos.Y + (rowH - ls.Y - ds.Y - (string.IsNullOrEmpty(description) ? 0f : 4f)) * 0.5f;
        dl.AddText(labelFont, labelFont.FontSize,
            new Vector2(textX, textY),
            CodexChassis.U32(CodexChassis.Text), label);
        if (!string.IsNullOrEmpty(description))
        {
            dl.AddText(descFont, descFont.FontSize,
                new Vector2(textX, textY + ls.Y + 4f),
                CodexChassis.U32(CodexChassis.TextDim), description);
        }

        if (clicked)
        {
            value = !value;
            return true;
        }
        return false;
    }

    /// <summary>
    /// CR opt-in toggle row: 2px gold-deep left bar + chamfered checkbox toggle
    /// + tracked-caps title + small description. Returns true if state changed.
    /// </summary>
    public static bool DrawCRToggleRow(ImDrawListPtr dl, Vector2 min, Vector2 max,
        float scale, ref bool isChecked, string title, string description,
        ImFontPtr titleFont, ImFontPtr descFont)
    {
        ImGui.SetCursorScreenPos(min);
        bool clicked = ImGui.InvisibleButton("##cr_toggle_row", max - min);
        bool hovered = ImGui.IsItemHovered();
        bool changed = false;

        // Background
        Vector4 bgCol = hovered
            ? new Vector4(28f / 255f, 32f / 255f, 42f / 255f, 0.65f)
            : new Vector4(20f / 255f, 24f / 255f, 32f / 255f, 0.50f);
        dl.AddRectFilled(min, max, CodexChassis.U32(bgCol));

        // 2px gold-deep left bar (lifts to gold on hover)
        Vector4 barCol = hovered ? CodexChassis.Gold : CodexChassis.GoldDeep;
        dl.AddRectFilled(min, new Vector2(min.X + 2f * scale, max.Y),
            CodexChassis.U32(barCol));

        float padX = 12f * scale;
        float padY = 8f * scale;
        float toggleSz = 16f * scale;
        var togMin = new Vector2(min.X + padX, min.Y + padY + 1f * scale);

        // Toggle box
        Span<Vector2> tpts = stackalloc Vector2[6];
        CodexChassis.BuildSlipPolygon(togMin, togMin + new Vector2(toggleSz, toggleSz), 3f * scale, tpts);
        if (isChecked)
        {
            unsafe
            {
                fixed (Vector2* p = tpts)
                    dl.AddConvexPolyFilled(p, 6, CodexChassis.U32(CodexChassis.Gold));
            }
            // Glow
            DrawAuroraSpot(dl, togMin + new Vector2(toggleSz * 0.5f, toggleSz * 0.5f),
                toggleSz * 0.8f, toggleSz * 0.8f,
                CodexChassis.WithAlpha(CodexChassis.Gold, 0.40f), 6);
        }
        else
        {
            unsafe
            {
                fixed (Vector2* p = tpts)
                    dl.AddConvexPolyFilled(p, 6, CodexChassis.U32(new Vector4(20f/255f, 24f/255f, 32f/255f, 0.7f)));
            }
            for (int i = 0; i < 6; i++) dl.PathLineTo(tpts[i]);
            dl.PathStroke(CodexChassis.U32(hovered ? CodexChassis.GoldDeep : CodexChassis.BorderSoft),
                ImDrawFlags.Closed, 1f * scale);
        }

        if (clicked)
        {
            isChecked = !isChecked;
            changed = true;
        }

        // Text block on the right of toggle
        float textX = togMin.X + toggleSz + 10f * scale;
        ImGui.PushFont(titleFont);
        Vector2 ts = ImGui.CalcTextSize(title);
        ImGui.PopFont();
        dl.AddText(titleFont, titleFont.FontSize,
            new Vector2(textX, min.Y + padY),
            CodexChassis.U32(CodexChassis.GoldWarm), title);

        if (!string.IsNullOrEmpty(description))
        {
            ImGui.PushFont(descFont);
            ImGui.PopFont();
            dl.AddText(descFont, descFont.FontSize,
                new Vector2(textX, min.Y + padY + ts.Y + 4f * scale),
                CodexChassis.U32(CodexChassis.TextFaint), description);
        }

        return changed;
    }

    // BOUTIQUE TEXT INPUT (custom frame paint + ImGui InputText overlay)

    /// <summary>Boutique-styled text input: hand-painted velvet frame + transparent ImGui.InputText overlay. Returns true if the value changed.</summary>
    public static bool DrawBoutiqueTextInput(string id, ref string value, int maxLen,
        float width, string placeholder = "", ImGuiInputTextFlags flags = 0)
    {
        float fs = CodexChassis.FormScale;
        // Use GetFrameHeight so text inputs and combos track the SAME FramePadding
        // (the one set in PushFormStyle). Result: combos and text inputs are the
        // same height side-by-side, no visual mismatch.
        float h = ImGui.GetFrameHeight();

        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var max = pos + new Vector2(width, h);

        // Read the active FrameBg style colour so callers can override the
        // input bg via PushStyleColor(FrameBg, ...) without forking the
        // primitive. PushFormStyle pushes the dark velvet default; the design
        // panel form layers a lighter Surface2 on top to lift inputs off the
        // form ground.
        Vector4 inputBg = ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg];
        dl.AddRectFilled(pos, max, CodexChassis.U32(inputBg));

        // Overlay an InputText with transparent FrameBg + no border. Pad
        // horizontally 10px per the mockup; vertical centring via FramePadding.y
        // matched to (h - fontH) / 2.
        float fontH = ImGui.GetFontSize();
        float padY = MathF.Max(0f, (h - fontH) * 0.5f);

        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f * fs, padY));

        ImGui.SetCursorScreenPos(pos);
        ImGui.SetNextItemWidth(width);
        bool changed = string.IsNullOrEmpty(placeholder)
            ? ImGui.InputText(id, ref value, maxLen, flags)
            : ImGui.InputTextWithHint(id, placeholder, ref value, maxLen, flags);

        bool isFocused = ImGui.IsItemActive() || ImGui.IsItemFocused();
        bool isHovered = ImGui.IsItemHovered();

        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(3);

        // Frame border, 1px BorderSoft, lifts to gold-deep on focus and a
        // slightly brighter Border on hover.
        Vector4 borderC = isFocused
            ? CodexChassis.GoldDeep
            : (isHovered ? CodexChassis.Border : CodexChassis.BorderSoft);
        dl.AddRect(pos, max, CodexChassis.U32(borderC), 0f, ImDrawFlags.None, 1f * fs);

        // Inset gold-at-12% glow on focus
        if (isFocused)
        {
            dl.AddRect(
                pos + new Vector2(1f * fs, 1f * fs),
                max - new Vector2(1f * fs, 1f * fs),
                CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.Gold, 0.12f)),
                0f, ImDrawFlags.None, 1f * fs);
        }

        return changed;
    }

    // CHAMFERED TEXT BUTTON + MACRO EDITOR (shared between forms)

    /// <summary>
    /// Small chamfered text-only button used by the portrait-section actions
    /// (BROWSE / PASTE / CLEAR) and macro toolbar (REGENERATE / PASTE / RESET).
    /// Caller must push the label font (typically OswaldSemi9) before calling.
    /// </summary>
    public static bool DrawChamferedTextButton(string label, float w, float h, float scale, string id)
    {
        float fs = CodexChassis.FormScale;
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var max = pos + new Vector2(w, h);
        float chamfer = 5f * fs;

        ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGui.InvisibleButton($"##bcham_{id}", new Vector2(w, h));
        bool hovered = ImGui.IsItemHovered();

        Span<Vector2> pts = stackalloc Vector2[6];
        CodexChassis.BuildSlipPolygon(pos, max, chamfer, pts);

        Vector4 bg = hovered
            ? new Vector4(28f / 255f, 32f / 255f, 42f / 255f, 0.92f)
            : new Vector4(20f / 255f, 24f / 255f, 32f / 255f, 0.78f);
        unsafe
        {
            fixed (Vector2* p = pts)
                dl.AddConvexPolyFilled(p, 6, CodexChassis.U32(bg));
        }
        for (int i = 0; i < 6; i++) dl.PathLineTo(pts[i]);
        dl.PathStroke(CodexChassis.U32(hovered ? CodexChassis.GoldDeep : CodexChassis.BorderSoft),
            ImDrawFlags.Closed, 1f * fs);

        var labelFont = ImGui.GetFont();
        var labelSz = ImGui.CalcTextSize(label);
        float startX = pos.X + (w - labelSz.X) * 0.5f;
        Vector4 inkCol = hovered ? CodexChassis.GoldWarm : CodexChassis.TextDim;
        dl.AddText(labelFont, labelFont.FontSize,
            new Vector2(startX, pos.Y + (h - labelFont.FontSize) * 0.5f),
            CodexChassis.U32(inkCol), label);

        return clicked;
    }

    /// <summary>
    /// Boutique macro editor: toolbar (line/char count + REGENERATE/PASTE/RESET buttons)
    /// + line-number gutter + monospaced textarea. `regenerate` provides the generated
    /// macro text on REGENERATE/RESET click. `paste` is invoked when the PASTE button is
    /// clicked; the callback should put text into macroText (either via clipboard read
    /// or some other source). `smallFont` is the tracked-caps Oswald font to use for
    /// labels and gutter numbers.
    /// </summary>
    public static void DrawMacroEditor(ref string macroText, string id, float scale,
        Func<string> regenerate, Action paste, ImFontPtr smallFont, float editorH = 170f)
    {
        float fs = CodexChassis.FormScale;
        var dl = ImGui.GetWindowDrawList();
        var toolbarStart = ImGui.GetCursorScreenPos();
        float availW = ImGui.GetContentRegionAvail().X;

        // Compact toolbar: three right-aligned chamfered text buttons sized
        // to fit OswaldSemi13 (16.9px) labels, readable size that matches
        // the rest of the form's typography.
        float toolbarH = 26f * fs;
        float btnH = 24f * fs;
        float btnGap = 5f * fs;
        float regenBtnW = 96f * fs;
        float pasteBtnW = 70f * fs;
        float resetBtnW = 70f * fs;
        float btnY = toolbarStart.Y + (toolbarH - btnH) * 0.5f;
        // Right-align with a 16-scale margin matching the editor frame so the
        // buttons don't kiss the window edge.
        float btnRowEnd = toolbarStart.X + availW - 16f * scale;
        float resetX = btnRowEnd - resetBtnW;
        float pasteX = resetX - btnGap - pasteBtnW;
        float regenX = pasteX - btnGap - regenBtnW;

        using (CharacterSelectPlugin.Plugin.Instance?.OswaldSemi13?.Push())
        {
            ImGui.SetCursorScreenPos(new Vector2(resetX, btnY));
            if (DrawChamferedTextButton("RESET", resetBtnW, btnH, scale, $"{id}_reset"))
                macroText = regenerate();

            ImGui.SetCursorScreenPos(new Vector2(pasteX, btnY));
            if (DrawChamferedTextButton("PASTE", pasteBtnW, btnH, scale, $"{id}_paste"))
                paste?.Invoke();

            ImGui.SetCursorScreenPos(new Vector2(regenX, btnY));
            if (DrawChamferedTextButton("REGENERATE", regenBtnW, btnH, scale, $"{id}_regen"))
                macroText = regenerate();
        }

        ImGui.SetCursorScreenPos(new Vector2(toolbarStart.X, toolbarStart.Y + toolbarH + 4f * fs));

        // ── Macro textarea + line-number gutter ──
        // editorH is the FULL caller-supplied space; subtract the toolbar so
        // the editor's bottom doesn't run past the body box. Reserve a real
        // right margin (16) so the frame doesn't kiss the window edge.
        float editorPxH = (editorH * fs) - (toolbarH + 4f * fs);
        var editorStart = ImGui.GetCursorScreenPos();
        float editorPxW = availW - 16f * scale;
        var editorMax = new Vector2(editorStart.X + editorPxW, editorStart.Y + editorPxH);

        dl.AddRectFilled(editorStart, editorMax,
            CodexChassis.U32(new Vector4(20f / 255f, 24f / 255f, 32f / 255f, 0.88f)));
        dl.AddRect(editorStart, editorMax,
            CodexChassis.U32(CodexChassis.GoldDeep), 0f, ImDrawFlags.None, 1f * fs);

        // Gutter: just a divider hairline + line numbers. No tinted bg fill,
        // the previous gold-at-4% wash read as a "murky yellow" stripe against
        // the dark editor body. Letting the gutter share the editor body's
        // colour keeps it quiet; the GoldDeep divider does the visual split.
        float gutterW = 32f * fs;
        dl.AddLine(new Vector2(editorStart.X + gutterW, editorStart.Y + 4f * fs),
                   new Vector2(editorStart.X + gutterW, editorMax.Y - 4f * fs),
                   CodexChassis.U32(CodexChassis.WithAlpha(CodexChassis.GoldDeep, 0.50f)),
                   1f * fs);

        // Line numbers, TextFaint for actual lines, TextGhost for the trail
        // past the macro's end so the gutter doesn't go suddenly empty.
        int lineCount = string.IsNullOrEmpty(macroText) ? 0 : macroText.Count(c => c == '\n') + 1;
        float lineH = ImGui.GetTextLineHeight();
        int displayLines = Math.Max(lineCount, (int)((editorPxH - 8f * fs) / lineH));
        var lineFont = ImGui.GetFont();
        for (int i = 1; i <= displayLines && (i - 1) * lineH < editorPxH - 8f * fs; i++)
        {
            string ln = i.ToString();
            var ls = ImGui.CalcTextSize(ln);
            uint inkCol = i <= lineCount
                ? CodexChassis.U32(CodexChassis.TextFaint)
                : CodexChassis.U32(CodexChassis.TextGhost);
            dl.AddText(lineFont, lineFont.FontSize,
                new Vector2(editorStart.X + gutterW - 6f * fs - ls.X,
                            editorStart.Y + 4f * fs + (i - 1) * lineH),
                inkCol, ln);
        }

        // Textarea overlay, sits in the area to the right of the gutter.
        ImGui.SetCursorScreenPos(new Vector2(editorStart.X + gutterW + 4f * fs,
                                             editorStart.Y + 4f * fs));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.Text, CodexChassis.GoldWarm);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2f * fs, 0f));

        ImGui.InputTextMultiline($"##{id}", ref macroText, 4000,
            new Vector2(editorPxW - gutterW - 8f * fs, editorPxH - 8f * fs),
            ImGuiInputTextFlags.AllowTabInput);

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(3);

        ImGui.SetCursorScreenPos(new Vector2(editorStart.X, editorMax.Y + 6f * fs));
    }
}

using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;

namespace CharacterSelectPlugin.Windows.Styles;

public static partial class Boutique
{
    // Font handle aliases. Null until Plugin.Instance is ready, callers null-check.
    private static Plugin? P => Plugin.Instance;

    /// <summary>Tracked-caps kicker, 9px Oswald SemiBold (search/sort kickers, sidebar count, restricted-div copy).</summary>
    public static IFontHandle? Kicker9 => P?.OswaldSemi9;
    /// <summary>Tracked-caps small label, 10px Oswald SemiBold (ribbon-right, found-N caption).</summary>
    public static IFontHandle? Kicker10 => P?.OswaldSemi10;
    /// <summary>Tracked-caps mid label, 11px Oswald SemiBold (sidebar head, ribbon meta, sort-pill value).</summary>
    public static IFontHandle? Kicker11 => P?.OswaldSemi11;
    /// <summary>Tracked-caps stat, 12px Oswald SemiBold (main-head title).</summary>
    public static IFontHandle? Kicker12 => P?.OswaldSemi12;
    /// <summary>Tracked-caps stat heavier, 13px Oswald SemiBold.</summary>
    public static IFontHandle? Kicker13 => P?.OswaldSemi13;
    /// <summary>Tracked-caps subtitle, 14px Oswald SemiBold.</summary>
    public static IFontHandle? Kicker14 => P?.OswaldSemi14;

    /// <summary>Body 12px Outfit Medium (mod row name, sidebar row name).</summary>
    public static IFontHandle? Body12 => P?.OutfitMed12;
    /// <summary>Body 13px Outfit Medium (paragraph copy, tooltips).</summary>
    public static IFontHandle? Body13 => P?.OutfitMed13;

    /// <summary>Oswald Medium 11px (sort-pill value tracked-caps).</summary>
    public static IFontHandle? OswaldMed11 => P?.OswaldMed11;
    /// <summary>Oswald Medium 13px.</summary>
    public static IFontHandle? OswaldMed13 => P?.OswaldMed13;
    /// <summary>Oswald SemiBold "Big" used for the loading ring's percent display.</summary>
    public static IFontHandle? OswaldSemiBig => P?.OswaldSemiBig;
    /// <summary>Oswald SemiBold "Small" (16px ach-scaled), loading ring caption.</summary>
    public static IFontHandle? OswaldSemiSmall => P?.OswaldSemiSmall;

    // ── Tracked text ─────────────────────────────────────────────────────
    // ImGui doesn't ship CSS letter-spacing, so we render glyph-by-glyph
    // with a fixed pixel gap. trackPx is the EXTRA pixels between glyphs.
    // Use the Track18/22/26/28/30/32/34/40 helpers from Boutique.Tokens
    // to compute trackPx from the current font size.

    /// <summary>Render text with per-glyph tracked-caps spacing. Returns the total rendered width.</summary>
    public static float DrawTrackedText(ImDrawListPtr dl, Vector2 pos, string text, uint colour, float trackPx)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        float x = pos.X;
        for (int i = 0; i < text.Length; i++)
        {
            string g = text.Substring(i, 1);
            dl.AddText(new Vector2(x, pos.Y), colour, g);
            x += ImGui.CalcTextSize(g).X;
            if (i < text.Length - 1) x += trackPx;
        }
        return x - pos.X;
    }

    /// <summary>Measure rendered width of tracked text without drawing.</summary>
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

    /// <summary>Truncate text to fit a max width (in plain ImGui::CalcTextSize units), suffixed with "...".</summary>
    public static string TruncateToWidth(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (ImGui.CalcTextSize(text).X <= maxWidth) return text;
        const string ell = "...";
        float ellW = ImGui.CalcTextSize(ell).X;
        for (int k = text.Length - 1; k > 0; k--)
        {
            var trunc = text.Substring(0, k);
            if (ImGui.CalcTextSize(trunc).X + ellW <= maxWidth)
                return trunc + ell;
        }
        return ell;
    }

    /// <summary>Truncate tracked-caps text to fit a max width using MeasureTrackedText.</summary>
    public static string TruncateTrackedToWidth(string text, float trackPx, float maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (MeasureTrackedText(text, trackPx) <= maxWidth) return text;
        const string ell = "...";
        float ellW = MeasureTrackedText(ell, trackPx);
        for (int k = text.Length - 1; k > 0; k--)
        {
            var trunc = text.Substring(0, k);
            if (MeasureTrackedText(trunc, trackPx) + ellW <= maxWidth)
                return trunc + ell;
        }
        return ell;
    }
}

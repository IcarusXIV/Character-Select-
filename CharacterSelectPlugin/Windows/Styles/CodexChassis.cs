using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using CharacterSelectPlugin.Achievements;

namespace CharacterSelectPlugin.Windows.Styles;

/// <summary>
/// Shared visual chassis for the Codex UI surfaces (Achievements, Patch Notes,
/// Achievement Toasts). Central home for the palette, category accents, chamfer
/// sizes, the slip polygon helper, and chamfered perimeter walker.
/// </summary>
public static class CodexChassis
{
    // Form layout dimensions are sized in mockup CSS pixels; multiply by this to
    // match the achUi (1.30) font scaling used throughout the plugin's custom fonts.
    public const float FormScale = 1.30f;

    // ── Core palette ─────────────────────────────────────────────────────
    // Design-system tokens delegate to Boutique so user custom-theme master
    // overrides (custom.boutique.accent / .surface / .border / .text) cascade
    // through every chassis surface that reads these. Defaults are kept in
    // Boutique so changing them here is forbidden, this file just forwards.
    public static Vector4 Shell      => Boutique.Shell;
    public static Vector4 Velvet     => Boutique.Velvet;
    public static Vector4 Bg         => Boutique.Bg;
    public static Vector4 Surface0   => Boutique.Surface0;
    public static Vector4 Surface1   => Boutique.Surface1;
    public static Vector4 Surface2   => Boutique.Surface2;
    public static Vector4 Surface3   => Boutique.Surface3;
    public static Vector4 RibbonTop  => Boutique.RibbonTop;
    public static Vector4 RibbonBot  => Boutique.RibbonBot;
    public static Vector4 Border     => Boutique.Border;
    public static Vector4 BorderSoft => Boutique.BorderSoft;

    public static Vector4 Text       => Boutique.Text;
    public static Vector4 TextDim    => Boutique.TextDim;
    public static Vector4 TextFaint  => Boutique.TextFaint;
    public static Vector4 TextGhost  => Boutique.TextGhost;

    public static Vector4 Gold       => Boutique.Gold;
    public static Vector4 GoldWarm   => Boutique.GoldWarm;
    public static Vector4 GoldBright => Boutique.GoldBright;
    public static Vector4 GoldDeep   => Boutique.GoldDeep;
    public static Vector4 GoldDark   => Boutique.GoldDark;

    public static readonly Vector4 Magenta    = Rgb(0xF1, 0x2B, 0x7C);
    public static readonly Vector4 MagentaSft = Rgb(0xFF, 0x5E, 0x8A);
    public static readonly Vector4 Cyan       = Rgb(0x29, 0xB6, 0xF6);
    public static readonly Vector4 CyanSoft   = Rgb(0x4D, 0xD0, 0xE1);
    public static readonly Vector4 Violet     = Rgb(0x7E, 0x57, 0xC2);
    public static readonly Vector4 Slate      = Rgb(0x6A, 0x7B, 0x8F);

    public static readonly Vector4 Green      = Rgb(0x4A, 0xDE, 0x80);
    public static readonly Vector4 GreenSoft  = Rgb(0x6D, 0xEA, 0x9A);
    public static readonly Vector4 Red        = Rgb(0xEF, 0x44, 0x44);

    // Glitch palette (toast shatter exit)
    public static readonly Vector4 GlitchMagenta = Rgb(0xFF, 0x2B, 0xB8);
    public static readonly Vector4 GlitchCyan    = Rgb(0x00, 0xEA, 0xFF);

    // Arcane palette (magical toast theme)
    public static readonly Vector4 ArcaneInk    = Rgb(0x3A, 0x2B, 0x78);
    public static readonly Vector4 ArcaneGlow   = Rgb(0xC9, 0xB7, 0xFF);
    public static readonly Vector4 ArcaneWarm   = Rgb(0xFF, 0xD6, 0x8A);
    public static readonly Vector4 ArcaneEmber  = Rgb(0xFF, 0xB3, 0x47);
    public static readonly Vector4 ArcaneAether = Rgb(0x6B, 0xE3, 0xFF);

    // ── Chamfer sizes (design token single source of truth) ─────────────
    public const float ChamSm = 8f;
    public const float ChamMd = 12f;
    public const float ChamLg = 18f;
    public const float ChamToast = 20f; // toast silhouette
    public const float ChamHero  = 20f; // patch-notes release tab hero
    public const float ChamPill  = 6f;  // active tab, active category pill

    // ── Category accents (shared across all Codex surfaces) ─────────────
    public static readonly Vector4 CatCharacters    = Rgb(0x5A, 0xC7, 0xFF);
    public static readonly Vector4 CatDesigns       = Rgb(0xFF, 0x73, 0xD1);
    public static readonly Vector4 CatProfiles      = Rgb(0x4D, 0xF2, 0x80);
    public static readonly Vector4 CatSwitching     = Rgb(0xFF, 0xB8, 0x40);
    public static readonly Vector4 CatAutomation    = Rgb(0xA3, 0x8F, 0xFF);
    public static readonly Vector4 CatSocial        = Rgb(0x40, 0xE6, 0xFF);
    public static readonly Vector4 CatCustomization = Rgb(0xFF, 0x94, 0x4D);
    public static readonly Vector4 CatDiscovery     = Rgb(0xAD, 0xFF, 0x61);

    // Patch-notes-only accent additions
    public static readonly Vector4 CatFeatured = Gold;        // hero tier
    public static readonly Vector4 CatBehind   = Rgb(0x6A, 0x7B, 0x8F);
    public static readonly Vector4 CatRandom   = Rgb(0xFF, 0x88, 0x70);

    // FontAwesome glyphs per category (matches AchievementWindow.AllCatMeta)
    public static string CategoryIcon(AchievementCategory cat) => cat switch
    {
        AchievementCategory.Characters    => "",
        AchievementCategory.Designs       => "",
        AchievementCategory.Profiles      => "",
        AchievementCategory.Switching     => "",
        AchievementCategory.Automation    => "",
        AchievementCategory.Social        => "",
        AchievementCategory.Customization => "",
        AchievementCategory.Discovery     => "",
        _                                 => "",
    };

    public static Vector4 CategoryColor(AchievementCategory cat) => cat switch
    {
        AchievementCategory.Characters    => CatCharacters,
        AchievementCategory.Designs       => CatDesigns,
        AchievementCategory.Profiles      => CatProfiles,
        AchievementCategory.Switching     => CatSwitching,
        AchievementCategory.Automation    => CatAutomation,
        AchievementCategory.Social        => CatSocial,
        AchievementCategory.Customization => CatCustomization,
        AchievementCategory.Discovery     => CatDiscovery,
        _                                 => Slate,
    };

    // ── Colour helpers ───────────────────────────────────────────────────

    /// <summary>Apply a new alpha to a colour (keeps RGB intact).</summary>
    public static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    /// <summary>Multiply the alpha of a colour.</summary>
    public static Vector4 ScaleAlpha(Vector4 c, float scale) => new(c.X, c.Y, c.Z, c.W * scale);

    /// <summary>Linear interpolation between two colours, all 4 channels.</summary>
    public static Vector4 Lerp(Vector4 a, Vector4 b, float t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, a.W + (b.W - a.W) * t);

    /// <summary>
    /// CSS color-mix(in srgb, A t%, B) analogue. t=0 returns full b, t=1 returns full a.
    /// We operate in linear space (not true sRGB gamma) for performance - close enough
    /// visually to CSS srgb mixing for these UI accents.
    /// </summary>
    public static Vector4 Mix(Vector4 a, float t, Vector4 b) => Lerp(b, a, t);

    /// <summary>Apply a tint to black (t percent of category colour mixed with surface).</summary>
    public static Vector4 TintSurface(Vector4 surface, Vector4 tint, float t) => Lerp(surface, tint, t);

    public static uint U32(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);
    public static uint U32(Vector4 c, float alpha) => ImGui.ColorConvertFloat4ToU32(new Vector4(c.X, c.Y, c.Z, c.W * alpha));

    // Slip polygon: 6 points, chamfered TR + BL, sharp TL + BR. Returned clockwise from TL.

    /// <summary>
    /// Fills the passed array (length 6) with the 6-point slip polygon for the
    /// given rect. Chamfer is clamped so it never exceeds half the shorter side.
    /// </summary>
    public static void BuildSlipPolygon(Vector2 min, Vector2 max, float chamfer, Span<Vector2> pts)
    {
        float c = Math.Min(chamfer, Math.Min(max.X - min.X, max.Y - min.Y) * 0.5f);
        pts[0] = new Vector2(min.X,     min.Y);        // TL (sharp)
        pts[1] = new Vector2(max.X - c, min.Y);        // TR start
        pts[2] = new Vector2(max.X,     min.Y + c);    // TR end
        pts[3] = new Vector2(max.X,     max.Y);        // BR (sharp)
        pts[4] = new Vector2(min.X + c, max.Y);        // BL start
        pts[5] = new Vector2(min.X,     max.Y - c);    // BL end
    }

    /// <summary>Allocates a fresh 6-point polygon array.</summary>
    public static Vector2[] SlipPolygon(Vector2 min, Vector2 max, float chamfer)
    {
        var pts = new Vector2[6];
        BuildSlipPolygon(min, max, chamfer, pts);
        return pts;
    }

    /// <summary>Fill a slip-shaped region with a solid colour.</summary>
    public static void FillSlip(ImDrawListPtr dl, Vector2 min, Vector2 max, float chamfer, uint colour)
    {
        Span<Vector2> pts = stackalloc Vector2[6];
        BuildSlipPolygon(min, max, chamfer, pts);
        unsafe
        {
            fixed (Vector2* p = pts)
                dl.AddConvexPolyFilled(p, 6, colour);
        }
    }

    /// <summary>Stroke the slip silhouette at 1px (or supplied thickness).</summary>
    public static void StrokeSlip(ImDrawListPtr dl, Vector2 min, Vector2 max, float chamfer, uint colour, float thickness = 1f)
    {
        Span<Vector2> pts = stackalloc Vector2[6];
        BuildSlipPolygon(min, max, chamfer, pts);
        for (int i = 0; i < 6; i++)
            dl.PathLineTo(pts[i]);
        dl.PathStroke(colour, ImDrawFlags.Closed, thickness);
    }

    /// <summary>Fill a slip silhouette with a flat midpoint of two gradient colours. Caller can layer a tinted accent for stronger gradient.</summary>
    public static void FillSlipGradient(ImDrawListPtr dl, Vector2 min, Vector2 max, float chamfer,
        Vector4 topLeftCol, Vector4 bottomRightCol)
    {
        var midCol = Lerp(topLeftCol, bottomRightCol, 0.5f);
        FillSlip(dl, min, max, chamfer, U32(midCol));
    }

    /// <summary>
    /// As FillSlipGradient, but adds a category-tinted "corner glow" overlay
    /// near the BR sharp corner to approximate the 145deg tint fall-off. The
    /// overlay is a convex triangle clipped to the polygon via a bounded rect
    /// of translucent fills along the anti-diagonal of the slip.
    /// </summary>
    public static void FillSlipWithCornerTint(ImDrawListPtr dl, Vector2 min, Vector2 max, float chamfer,
        Vector4 baseCol, Vector4 tintCol, float tintStrength = 0.16f)
    {
        FillSlip(dl, min, max, chamfer, U32(baseCol));

        // Soft tint glow anchored at the BR corner. Draw 3 nested triangles
        // with decaying alpha and decreasing size: gives a subtle radial fall-off
        // toward the top-left without requiring per-pixel gradient support.
        float c = Math.Min(chamfer, Math.Min(max.X - min.X, max.Y - min.Y) * 0.5f);
        float diag = Vector2.Distance(min, max);
        for (int i = 0; i < 4; i++)
        {
            float reach = diag * (0.28f + i * 0.18f);
            float a = tintStrength * (1f - i * 0.25f);
            var br = new Vector2(max.X, max.Y);
            var p1 = new Vector2(max.X - reach, max.Y);
            var p2 = new Vector2(max.X, max.Y - reach);
            dl.AddTriangleFilled(br, p1, p2, U32(WithAlpha(tintCol, a)));
        }
        _ = c;
    }

    // ── Perimeter walker (6-point slip) ─────────────────────────────────

    /// <summary>
    /// Map a 0..1 progress to a point on the slip polygon's perimeter, walking
    /// clockwise from the TL corner. Useful for orbit/scan head positions.
    /// Replaces the older rectangular WalkPerimeter in CSAchievementToast.cs.
    /// </summary>
    public static Vector2 WalkSlipPerimeter(float progress, Vector2 min, Vector2 max, float chamfer)
    {
        Span<Vector2> pts = stackalloc Vector2[6];
        BuildSlipPolygon(min, max, chamfer, pts);

        // Compute segment lengths in clockwise order.
        Span<float> lens = stackalloc float[6];
        float total = 0f;
        for (int i = 0; i < 6; i++)
        {
            lens[i] = Vector2.Distance(pts[i], pts[(i + 1) % 6]);
            total += lens[i];
        }
        if (total < 0.001f) return pts[0];

        float d = (progress % 1f + 1f) % 1f * total;
        for (int i = 0; i < 6; i++)
        {
            if (d <= lens[i])
            {
                float t = lens[i] < 0.001f ? 0f : d / lens[i];
                return Vector2.Lerp(pts[i], pts[(i + 1) % 6], t);
            }
            d -= lens[i];
        }
        return pts[0];
    }

    // Tracked-caps text. ImGui has no letter-spacing, so we draw glyph-by-glyph
    // with `trackPx` extra pixels between each. Returns total rendered width.
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

    /// <summary>Measure rendered width of tracked text (no draw).</summary>
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

    // ── Draw helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Window frame corner brackets (gold, 40% alpha, L-shaped). TL+TR+BL+BR
    /// positions are drawn at the supplied inset from the content rect corners.
    /// </summary>
    public static void DrawCornerBrackets(ImDrawListPtr dl, Vector2 min, Vector2 max,
        float size = 14f, float inset = 6f, float thickness = 1f, float alpha = 0.40f)
    {
        uint c = U32(WithAlpha(Gold, alpha));

        // Bottom-left
        var bl = new Vector2(min.X + inset, max.Y - inset);
        dl.AddLine(new Vector2(bl.X, bl.Y - size), bl, c, thickness);
        dl.AddLine(bl, new Vector2(bl.X + size, bl.Y), c, thickness);

        // Bottom-right
        var br = new Vector2(max.X - inset, max.Y - inset);
        dl.AddLine(new Vector2(br.X, br.Y - size), br, c, thickness);
        dl.AddLine(br, new Vector2(br.X - size, br.Y), c, thickness);
    }

    // ── Deterministic PRNG (Mulberry32) ─────────────────────────────────
    // The mockups seed all their procedural content (shatter slabs, sigil ring
    // angles, ember paths) from a per-toast PRNG so each achievement gets a
    // unique but deterministic pattern. Mirrored here so the plugin can feed
    // the achievement Id as the seed.

    public struct Mulberry32
    {
        private uint _s;
        public Mulberry32(int seed) { _s = (uint)(seed == 0 ? 1 : seed); }

        public float Next()
        {
            _s += 0x6D2B79F5u;
            uint t = _s;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + (t ^ (t >> 7)) * (t | 61u);
            return ((t ^ (t >> 14)) & 0xFFFFFFFFu) / 4294967296f;
        }

        public float Range(float lo, float hi) => lo + Next() * (hi - lo);
        public int RangeI(int lo, int hi) => (int)(lo + Next() * (hi - lo + 1));
    }

    /// <summary>Compose a stable seed from a string identifier (FNV-1a 32-bit).</summary>
    public static int HashSeed(string s)
    {
        if (string.IsNullOrEmpty(s)) return 1;
        unchecked
        {
            uint h = 2166136261u;
            foreach (char c in s) { h ^= c; h *= 16777619u; }
            return (int)h;
        }
    }

    // ── Rgb helper ──────────────────────────────────────────────────────
    private static Vector4 Rgb(int r, int g, int b, float a = 1f) =>
        new(r / 255f, g / 255f, b / 255f, a);
}

using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CharacterSelectPlugin.Windows.Styles;

/// <summary>Boutique design system tokens partial: palette, dimensions, kerning, alphas.</summary>
public static partial class Boutique
{
    // Palette tokens, each is a property that consults the
    // user's custom theme for a "master override" (accent / surface / border /
    // text) and derives variants from it via lerp. When no override is set,
    // the hardcoded defaults below are used. Defaults match the original
    // boutique palette exactly so non-Custom themes are visually unchanged.

    private static readonly Vector4 _ShellDefault      = Rgb(0x04, 0x05, 0x0A);
    private static readonly Vector4 _VelvetDefault     = Rgb(0x06, 0x07, 0x09);
    private static readonly Vector4 _BgDefault         = Rgb(0x0A, 0x0B, 0x10);
    private static readonly Vector4 _Surface0Default   = Rgb(0x0E, 0x10, 0x14);
    private static readonly Vector4 _Surface1Default   = Rgb(0x13, 0x15, 0x1C);
    private static readonly Vector4 _Surface2Default   = Rgb(0x1A, 0x1D, 0x27);
    private static readonly Vector4 _Surface3Default   = Rgb(0x20, 0x24, 0x2E);
    private static readonly Vector4 _RibbonTopDefault  = Rgb(0x0C, 0x0E, 0x12);
    private static readonly Vector4 _RibbonBotDefault  = Rgb(0x08, 0x0A, 0x0D);
    private static readonly Vector4 _BorderDefault     = Rgb(0x2F, 0x35, 0x42);
    private static readonly Vector4 _BorderSoftDefault = Rgb(0x25, 0x28, 0x34);

    private static readonly Vector4 _TextDefault       = Rgb(0xE8, 0xEA, 0xF0);
    private static readonly Vector4 _TextDimDefault    = Rgb(0x8D, 0x93, 0xA2);
    private static readonly Vector4 _TextFaintDefault  = Rgb(0x5B, 0x61, 0x74);
    private static readonly Vector4 _TextGhostDefault  = Rgb(0x3C, 0x41, 0x50);

    private static readonly Vector4 _GoldDefault       = Rgb(0xFF, 0xD6, 0x00);
    private static readonly Vector4 _GoldWarmDefault   = Rgb(0xFF, 0xC8, 0x3D);
    private static readonly Vector4 _GoldBrightDefault = Rgb(0xFF, 0xF1, 0xA8);
    private static readonly Vector4 _GoldDeepDefault   = Rgb(0xB8, 0x90, 0x1C);
    private static readonly Vector4 _GoldDarkDefault   = Rgb(0x5A, 0x45, 0x11);

    private static readonly Vector4 _White             = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 _Black             = new(0f, 0f, 0f, 1f);

    // True when the token has a user override
    public static bool HasTokenOverride(string key)
    {
        var p = Plugin.Instance;
        return p?.Configuration?.SelectedTheme == ThemeSelection.Custom
            && p.Configuration.CustomTheme != null
            && p.Configuration.CustomTheme.ColorOverrides.TryGetValue(key, out var v)
            && v.HasValue;
    }

    // Token resolver: specific key > slot key > default
    private static Vector4 ResolveColor(string? specificKey, string? slotKey,
        Vector4 fallback)
    {
        var p = Plugin.Instance;
        if (p?.Configuration?.SelectedTheme != ThemeSelection.Custom ||
            p.Configuration.CustomTheme == null)
            return fallback;

        var theme = p.Configuration.CustomTheme;
        if (specificKey != null
            && theme.ColorOverrides.TryGetValue(specificKey, out var sp)
            && sp.HasValue)
            return CustomThemeDefinitions.UnpackColor(sp.Value);

        if (slotKey != null
            && theme.ColorOverrides.TryGetValue(slotKey, out var slp)
            && slp.HasValue)
            return CustomThemeDefinitions.UnpackColor(slp.Value);

        return fallback;
    }

    private static Vector4 ResolveDerivedFromPrimary(Vector4 primary,
        Vector4 fallbackPrimary, Vector4 fallbackVariant, Func<Vector4, Vector4> derive)
    {
        // If the primary equals its hardcoded default (no override), keep the
        // variant's hardcoded default. If the user has tinted the primary,
        // derive the variant from the new primary so the family stays
        // visually coherent.
        if (primary == fallbackPrimary)
            return fallbackVariant;
        return derive(primary);
    }

    // Surface family. Surface0 routes through color.windowBg; Surface1/2/3 and
    // Ribbon Top/Bot derive from it so they shift together when it changes.
    public static Vector4 Surface0   => ResolveColor(null,            "color.windowBg", _Surface0Default);
    public static Vector4 Surface1   => ResolveDerivedFromPrimary(Surface0, _Surface0Default, _Surface1Default, m => Lerp(m, _White, 0.06f));
    public static Vector4 Surface2   => ResolveDerivedFromPrimary(Surface0, _Surface0Default, _Surface2Default, m => Lerp(m, _White, 0.12f));
    public static Vector4 Surface3   => ResolveDerivedFromPrimary(Surface0, _Surface0Default, _Surface3Default, m => Lerp(m, _White, 0.18f));
    public static Vector4 Velvet     => ResolveColor("custom.list.bg",  null,             _VelvetDefault);
    public static Vector4 Bg         => ResolveDerivedFromPrimary(Surface0, _Surface0Default, _BgDefault,       m => Lerp(m, _Black, 0.20f));
    public static Vector4 Shell      => ResolveDerivedFromPrimary(Surface0, _Surface0Default, _ShellDefault,    m => Lerp(m, _Black, 0.55f));
    public static Vector4 RibbonTop  => ResolveDerivedFromPrimary(Surface0, _Surface0Default, _RibbonTopDefault, m => Lerp(m, _White, 0.03f));
    public static Vector4 RibbonBot  => ResolveDerivedFromPrimary(Surface0, _Surface0Default, _RibbonBotDefault, m => Lerp(m, _Black, 0.18f));

    // Border family. Routes through color.separator / color.separatorActive so
    // the Separators category in the editor also drives boutique hairlines.
    public static Vector4 BorderSoft => ResolveColor(null, "color.separator",       _BorderSoftDefault);
    public static Vector4 Border     => ResolveColor(null, "color.separatorActive", _BorderDefault);

    // Text tiers each have a dedicated editor key so they don't bleed into each other
    public static Vector4 Text       => ResolveColor(null, "color.text", _TextDefault);
    public static Vector4 TextDim    => ResolveColor("custom.text.subtle", null, _TextDimDefault);
    public static Vector4 TextFaint  => ResolveColor("custom.text.faint",  null, _TextFaintDefault);
    public static Vector4 TextGhost  => _TextGhostDefault;
    public static Vector4 InputText        => ResolveColor("custom.input.text", "color.text", _TextDefault);
    public static Vector4 InputPlaceholder => ResolveColor("custom.input.placeholder", null, _TextFaintDefault);
    public static Vector4 MenuIcon         => ResolveColor("custom.button.menu.icon", null, _TextDimDefault);

    // Character card button labels
    public static Vector4 CardDesignsInk => ResolveColor("custom.card.designsText", null, CyanSoft);
    public static Vector4 CardEditInk    => ResolveColor("custom.card.editText",    null, _TextDefault);
    public static Vector4 CardDeleteInk  => ResolveColor("custom.card.deleteText",  null, Red);
    public static Vector4 CardButtonBg   => ResolveColor("custom.card.buttonBg",    null, _Surface3Default);
    public static Vector4 CardNameInk    => ResolveColor("custom.card.nameText",    "color.text", _TextDefault);

    // Applied design row accent
    public static Vector4 ActiveDesignAccent => AccentSyncActive ? Gold : ResolveColor("custom.designPanel.activeAccent", null, Gold);

    // Hovered (?) token key, expires after 2 frames
    private static string? _hoveredTokenKey;
    private static int _hoveredTokenFrame;
    public static string? HoveredTokenKey
    {
        get => _hoveredTokenKey != null && ImGui.GetFrameCount() - _hoveredTokenFrame <= 2 ? _hoveredTokenKey : null;
        set { _hoveredTokenKey = value; _hoveredTokenFrame = ImGui.GetFrameCount(); }
    }
    public static bool IsTokenHovered(string key) => HoveredTokenKey == key;

    // Pulsing outline while the token's (?) is hovered
    public static void DrawTokenHighlight(ImDrawListPtr dl, Vector2 min, Vector2 max, string key)
    {
        if (!IsTokenHovered(key))
            return;
        float pulse = 0.55f + 0.35f * MathF.Sin((float)ImGui.GetTime() * 6f);
        var col = new Vector4(1f, 0.95f, 0.55f, pulse);
        dl.AddRect(min - new Vector2(2f, 2f), max + new Vector2(2f, 2f), U32(col), 0f, ImDrawFlags.None, 2f);
    }

    // User atmosphere intensity multiplier, Custom theme only
    public static float AtmosphereIntensity
    {
        get
        {
            var p = Plugin.Instance;
            if (p?.Configuration?.SelectedTheme != ThemeSelection.Custom || p.Configuration.CustomTheme == null)
                return 1f;
            return Math.Clamp(p.Configuration.CustomTheme.AtmosphereIntensity, 0f, 12f);
        }
    }

    // Hover spotlight wins, otherwise user intensity
    public static float AtmosphereAlpha(string? key, float baseAlpha, float cap)
        => key != null && IsTokenHovered(key)
            ? MathF.Min(baseAlpha * 12f, cap)
            : MathF.Min(baseAlpha * AtmosphereIntensity, cap);

    // While set, the gold family resolves the settings accent instead of the main accent
    public static bool StaticChrome;

    // Master character-colour sync
    public static bool AccentSyncActive
    {
        get
        {
            var p = Plugin.Instance;
            return !StaticChrome
                && p?.Configuration?.SelectedTheme == ThemeSelection.Custom
                && p.Configuration.CustomTheme?.AccentFollowsNameplate == true;
        }
    }

    // Gold accent family
    public static Vector4 Gold
    {
        get
        {
            if (StaticChrome)
                return ResolveColor("custom.settings.accent", null, _GoldDefault);
            var p = Plugin.Instance;
            if (p?.Configuration?.SelectedTheme == ThemeSelection.Custom
                && p.Configuration.CustomTheme?.AccentFollowsNameplate == true)
            {
                var np = p.ActiveCharacterNameplate;
                if (np != null && np.Value.LengthSquared() > 0.001f)
                    return new Vector4(np.Value.X, np.Value.Y, np.Value.Z, 1f);
            }
            return ResolveColor("custom.accent.primary", null, _GoldDefault);
        }
    }

    // Decorative animation gate
    public static bool ReduceMotion => Plugin.Instance?.Configuration?.ReduceMotion == true;
    public static double AnimTime(double t) => ReduceMotion ? 0.0 : t;

    // True when text needs a CJK-capable font
    public static bool NeedsCjkFont(string text)
    {
        foreach (var c in text)
            if (c >= 0x3000)
                return true;
        return false;
    }
    // Action pill fill
    public static Vector4 ActionFill     => AccentSyncActive ? Gold : ResolveColor("custom.button.bg", null, Gold);
    public static Vector4 ActionFillWarm => ResolveDerivedFromPrimary(ActionFill, _GoldDefault, _GoldWarmDefault, m => Lerp(m, _White, 0.20f));
    public static Vector4 GoldWarm   => ResolveDerivedFromPrimary(Gold, _GoldDefault, _GoldWarmDefault,   m => Lerp(m, _White, 0.20f));
    public static Vector4 GoldBright => ResolveDerivedFromPrimary(Gold, _GoldDefault, _GoldBrightDefault, m => Lerp(m, _White, 0.55f));
    public static Vector4 GoldDeep   => ResolveDerivedFromPrimary(Gold, _GoldDefault, _GoldDeepDefault,   m => Lerp(m, _Black, 0.40f));
    public static Vector4 GoldDark   => ResolveDerivedFromPrimary(Gold, _GoldDefault, _GoldDarkDefault,   m => Lerp(m, _Black, 0.70f));

    public static readonly Vector4 Magenta    = Rgb(0xF1, 0x2B, 0x7C);
    public static readonly Vector4 MagentaSft = Rgb(0xFF, 0x5E, 0x8A);
    public static readonly Vector4 Cyan       = Rgb(0x29, 0xB6, 0xF6);
    public static readonly Vector4 CyanSoft   = Rgb(0x4D, 0xD0, 0xE1);

    private static readonly Vector4 _HeaderTopDefault = Rgb(0x0C, 0x0E, 0x14);
    // Header and action-bar gradient top stop
    public static Vector4 HeaderTop => ResolveColor("custom.header.top", null, _HeaderTopDefault);

    // Ambient atmosphere layers, soft variants derive from their primary
    public static Vector4 AmbientMagenta => ResolveColor("custom.ambient.magenta", null, Magenta);
    public static Vector4 AmbientCyan    => ResolveColor("custom.ambient.cyan",    null, Cyan);
    public static Vector4 AmbientViolet  => ResolveColor("custom.ambient.violet",  null, Violet);
    public static Vector4 AmbientMagentaSoft => ResolveDerivedFromPrimary(AmbientMagenta, Magenta, MagentaSft, m => Lerp(m, _White, 0.25f));
    public static Vector4 AmbientCyanSoft    => ResolveDerivedFromPrimary(AmbientCyan,    Cyan,    CyanSoft,   m => Lerp(m, _White, 0.25f));
    public static readonly Vector4 NpCyan     = Rgb(0x5A, 0xC7, 0xFF);
    public static readonly Vector4 NpAmber    = Rgb(0xFF, 0xB8, 0x40);
    public static readonly Vector4 Violet     = Rgb(0x7E, 0x57, 0xC2);
    public static readonly Vector4 Slate      = Rgb(0x6A, 0x7B, 0x8F);

    public static readonly Vector4 Green      = Rgb(0x4A, 0xDE, 0x80);
    public static readonly Vector4 GreenSoft  = Rgb(0x6D, 0xEA, 0x9A);
    public static readonly Vector4 Red        = Rgb(0xEF, 0x44, 0x44);

    private static readonly Vector4 _PillBgDefault = new(20f / 255f, 24f / 255f, 32f / 255f, 0.6f);
    /// <summary>Standard pill background for boutique search bars and closed dropdowns. Routes through color.frameBg so the editor's "Input Fields" override drives boutique input-like pills.</summary>
    public static Vector4 PillBg => ResolveColor(null, "color.frameBg", _PillBgDefault);
    private static readonly Vector4 _PillBgDeepDefault = new(14f / 255f, 16f / 255f, 20f / 255f, 0.70f);
    /// <summary>Lighter pill background for boutique input fields and closed dropdowns. Routes through color.frameBg so the editor's "Input Fields" override drives boutique inputs.</summary>
    public static Vector4 PillBgDeep => ResolveColor(null, "color.frameBg", _PillBgDeepDefault);
    /// <summary>Boutique custom-painted popup contents background. Routes through color.popupBg so the editor's "Popup/Tooltip" override drives boutique popups.</summary>
    public static Vector4 PopupBg => ResolveColor(null, "color.popupBg", new Vector4(6f / 255f, 7f / 255f, 9f / 255f, 0.97f));

    /// <summary>Public slot-key resolver for callers outside this file. Consults the user's custom-theme override for the given ImGui slot key, returns the fallback if unset.</summary>
    public static Vector4 SlotOrDefault(string slotKey, Vector4 fallback)
        => ResolveColor(null, slotKey, fallback);
    /// <summary>Window child bg used inside Begin/Child for boutique surfaces.</summary>
    public static readonly Vector4 ChildBg    = new(0.04f, 0.05f, 0.08f, 0.40f);
    /// <summary>Sidebar child bg, slightly more opaque than ChildBg.</summary>
    public static readonly Vector4 SidebarBg  = new(0.04f, 0.05f, 0.08f, 0.55f);

    // ── Chamfer sizes (px before scale multiplier) ──────────────────────
    public const float ChamMini  = 5f;   // mini-btn, page-btn
    public const float ChamPill  = 6f;   // active tab, active category pill, cancel
    public const float ChamCancel = 6f;  // cancel pill (alias for ChamPill, kept for clarity)
    public const float ChamSm    = 8f;   // apply pill, gold pill
    public const float ChamMd    = 12f;
    public const float ChamLg    = 18f;
    public const float ChamHero  = 20f;  // patch-notes release tab hero
    public const float ChamToast = 20f;  // toast silhouette

    // ── Standard dimensions (px before scale multiplier) ────────────────
    public const float RibbonHeight     = 30f;
    public const float ToolbarRowHeight = 30f;
    public const float SidebarWidth     = 220f;
    public const float SearchPillHeight = 30f;
    public const float SortPillHeight   = 30f;
    public const float CategoryRowH     = 30f;
    public const float ModRowHeight     = 32f; // up from 28 to accommodate Body13 names
    // Reduced from the mockup's 78px because at user UI-scale > 1 the wrapper
    // ended up with 30+ scaled pixels of empty space between the "ENABLE"
    // label and the cluster, making the pin/gear feel disconnected from the
    // state. 64px keeps room for the "DISABLE" label + chevron at 1× scale and
    // tightens the cluster against the state at any scale.
    public const float StateCtrlW       = 64f;
    public const float StateCtrlH       = 20f;
    public const float ClusterIconSide  = 18f;
    public const float ClusterIconGap   = 4f;
    public const float CheckboxSide     = 14f;
    public const float MiniBtnHeight    = 26f;
    public const float IconBtnSide30    = 30f;
    public const float IconBtnSide26    = 26f;
    public const float FooterHeight     = 50f;
    public const float ApplyPillHeight  = 32f;
    public const float CancelBtnHeight  = 32f;
    public const float PageBtnSide      = 24f;

    // ── Standard alphas (CSS letter-spacing maps roughly per em, but the
    // mockup uses fixed track values, scale with font size when calling). ─
    /// <summary>Hover wash on table rows (2.5% white).</summary>
    public const float AlphaRowHover    = 0.025f;
    /// <summary>Selected/live row gold tint.</summary>
    public const float AlphaRowSelected = 0.10f;
    /// <summary>Active category gradient start alpha (gold-at-12%).</summary>
    public const float AlphaActiveStart = 0.12f;
    /// <summary>Hover row gold-tint (4-5%).</summary>
    public const float AlphaRowHoverTint = 0.05f;

    // ── Kerning helpers ─────────────────────────────────────────────────
    // CSS letter-spacing values used in the mockups, mapped to the per-glyph
    // pixel gap our tracked-text helper uses. Pass the current font height to
    // get an accurate result (em is relative to font size).
    /// <summary>0.18em → kicker subhead.</summary>
    public static float Track18(float fontSize) => fontSize * 0.18f;
    /// <summary>0.22em → ribbon meta.</summary>
    public static float Track22(float fontSize) => fontSize * 0.22f;
    /// <summary>0.26em → state-combo, opt label.</summary>
    public static float Track26(float fontSize) => fontSize * 0.26f;
    /// <summary>0.28em → ribbon-right, sidebar header.</summary>
    public static float Track28(float fontSize) => fontSize * 0.28f;
    /// <summary>0.30em → page label, search kicker.</summary>
    public static float Track30(float fontSize) => fontSize * 0.30f;
    /// <summary>0.32em → main-head title, found-N caption, sidebar column head.</summary>
    public static float Track32(float fontSize) => fontSize * 0.32f;
    /// <summary>0.34em → sort-pill kicker, restricted-div top border copy.</summary>
    public static float Track34(float fontSize) => fontSize * 0.34f;
    /// <summary>0.40em → window title, sidebar primary head.</summary>
    public static float Track40(float fontSize) => fontSize * 0.40f;

    // ── Colour helpers ───────────────────────────────────────────────────
    public static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);
    public static Vector4 ScaleAlpha(Vector4 c, float scale) => new(c.X, c.Y, c.Z, c.W * scale);
    public static Vector4 Lerp(Vector4 a, Vector4 b, float t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, a.W + (b.W - a.W) * t);
    /// <summary>CSS color-mix(in srgb, A t%, B) analogue. t=0 returns full b, t=1 returns full a.</summary>
    public static Vector4 Mix(Vector4 a, float t, Vector4 b) => Lerp(b, a, t);
    public static Vector4 TintSurface(Vector4 surface, Vector4 tint, float t) => Lerp(surface, tint, t);

    public static uint U32(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);
    public static uint U32(Vector4 c, float alpha) => ImGui.ColorConvertFloat4ToU32(new Vector4(c.X, c.Y, c.Z, c.W * alpha));

    /// <summary>Resolve a #RRGGBB byte triplet to a normalised Vector4 colour.</summary>
    private static Vector4 Rgb(int r, int g, int b, float a = 1f) =>
        new(r / 255f, g / 255f, b / 255f, a);

    // ── Form scale ──────────────────────────────────────────────────────
    /// <summary>
    /// Scale multiplier applied to mockup CSS pixel values when drawing into
    /// the achUi (1.30) font scale used across the plugin's custom fonts.
    /// Multiply mockup pixel constants by this in form-style surfaces.
    /// </summary>
    public const float FormScale = 1.30f;

    /// <summary>Resolve the user's UI scale multiplier, clamped to a sane range.</summary>
    public static float Scale =>
        Math.Clamp(Plugin.Instance?.Configuration?.UIScaleMultiplier ?? 1f, 0.85f, 2.0f);
}

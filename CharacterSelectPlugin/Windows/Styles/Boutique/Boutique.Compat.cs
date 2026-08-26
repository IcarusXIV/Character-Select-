using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using CharacterSelectPlugin.Achievements;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin.Windows.Styles;

/// <summary>Boutique compatibility partial: re-exposes BoutiqueChassis / CodexChassis helpers under Boutique.</summary>
public static partial class Boutique
{
    // Nameplate palette (8-colour cycle used by character cards)
    public static readonly Vector4 NpRose   = new(0xFF / 255f, 0x73 / 255f, 0xD1 / 255f, 1f);
    public static readonly Vector4 NpGreen  = new(0x4D / 255f, 0xF2 / 255f, 0x80 / 255f, 1f);
    public static readonly Vector4 NpViolet = new(0xA3 / 255f, 0x8F / 255f, 0xFF / 255f, 1f);
    public static readonly Vector4 NpCoral  = new(0xFF / 255f, 0x88 / 255f, 0x70 / 255f, 1f);
    public static readonly Vector4 NpOcean  = new(0x6F / 255f, 0xBF / 255f, 0xE3 / 255f, 1f);
    public static readonly Vector4 NpSand   = new(0xD9 / 255f, 0xC9 / 255f, 0x8B / 255f, 1f);

    // Lazy so NpCyan/NpAmber (declared in Boutique.Tokens.cs) are initialised first
    private static Vector4[]? _npPalette;
    public static Vector4[] NpPalette =>
        _npPalette ??= new[] { NpCyan, NpRose, NpGreen, NpAmber, NpViolet, NpCoral, NpOcean, NpSand };

    public static Vector4 NpColorByIndex(int i)
        => NpPalette[((i % NpPalette.Length) + NpPalette.Length) % NpPalette.Length];

    // Achievement category palette + glyph
    public static readonly Vector4 CatCharacters    = CodexChassis.CatCharacters;
    public static readonly Vector4 CatDesigns       = CodexChassis.CatDesigns;
    public static readonly Vector4 CatProfiles      = CodexChassis.CatProfiles;
    public static readonly Vector4 CatSwitching     = CodexChassis.CatSwitching;
    public static readonly Vector4 CatAutomation    = CodexChassis.CatAutomation;
    public static readonly Vector4 CatSocial        = CodexChassis.CatSocial;
    public static readonly Vector4 CatCustomization = CodexChassis.CatCustomization;
    public static readonly Vector4 CatDiscovery     = CodexChassis.CatDiscovery;
    public static Vector4 CatFeatured      => CodexChassis.CatFeatured;
    public static readonly Vector4 CatBehind        = CodexChassis.CatBehind;
    public static readonly Vector4 CatRandom        = CodexChassis.CatRandom;

    public static string CategoryIcon(AchievementCategory cat) => CodexChassis.CategoryIcon(cat);
    public static Vector4 CategoryColor(AchievementCategory cat) => CodexChassis.CategoryColor(cat);

    // ── Glitch palette (toast shatter exit) ─────────────────────────────
    public static readonly Vector4 GlitchMagenta = CodexChassis.GlitchMagenta;
    public static readonly Vector4 GlitchCyan    = CodexChassis.GlitchCyan;

    // ── Arcane palette (magical toast theme) ────────────────────────────
    public static readonly Vector4 ArcaneInk    = CodexChassis.ArcaneInk;
    public static readonly Vector4 ArcaneGlow   = CodexChassis.ArcaneGlow;
    public static readonly Vector4 ArcaneWarm   = CodexChassis.ArcaneWarm;
    public static readonly Vector4 ArcaneEmber  = CodexChassis.ArcaneEmber;
    public static readonly Vector4 ArcaneAether = CodexChassis.ArcaneAether;

    // ── Slip polygon extras ─────────────────────────────────────────────
    public static void FillSlipGradient(ImDrawListPtr dl, Vector2 min, Vector2 max, float chamfer,
        Vector4 topLeftCol, Vector4 bottomRightCol)
        => CodexChassis.FillSlipGradient(dl, min, max, chamfer, topLeftCol, bottomRightCol);

    public static void FillSlipWithCornerTint(ImDrawListPtr dl, Vector2 min, Vector2 max, float chamfer,
        Vector4 baseCol, Vector4 tintCol, float tintStrength = 0.16f)
        => CodexChassis.FillSlipWithCornerTint(dl, min, max, chamfer, baseCol, tintCol, tintStrength);

    public static Vector2 WalkSlipPerimeter(float progress, Vector2 min, Vector2 max, float chamfer)
        => CodexChassis.WalkSlipPerimeter(progress, min, max, chamfer);

    /// <summary>Legacy 4-corner bracket draw (TL+TR+BL+BR variant from CodexChassis).</summary>
    public static void DrawCornerBrackets(ImDrawListPtr dl, Vector2 min, Vector2 max,
        float size = 14f, float inset = 6f, float thickness = 1f, float alpha = 0.40f)
        => CodexChassis.DrawCornerBrackets(dl, min, max, size, inset, thickness, alpha);

    /// <summary>Pulsing gold pip (animated square variant, used in the meta ribbon).</summary>
    public static void DrawGoldPip(ImDrawListPtr dl, Vector2 centre, float scale, double time)
        => BoutiqueChassis.DrawGoldPip(dl, centre, scale, time);

    // ── PRNG (Mulberry32) + hash seed ───────────────────────────────────
    public static int HashSeed(string s) => CodexChassis.HashSeed(s);
    /// <summary>Compose a Mulberry32 PRNG. Use the seeds produced by HashSeed for stable per-id procedural patterns.</summary>
    public static Mulberry32 NewRandom(int seed) => new Mulberry32(seed);

    /// <summary>
    /// Deterministic 32-bit PRNG (Mulberry32). Seeded from a stable id (use
    /// HashSeed) so a given id produces the same sequence each session.
    /// Mirrors CodexChassis.Mulberry32 for consumers migrating to Boutique.
    /// </summary>
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

    // ── Loading: progress ring (achievements vault style) ───────────────
    public static void DrawProgressRing(ImDrawListPtr dl, Vector2 centre, float scale,
        float ringRadius, float ringThickness, float displayedRatio, float fillProgress,
        int fillSeed, float time)
        => BoutiqueChassis.DrawProgressRing(dl, centre, scale, ringRadius, ringThickness,
            displayedRatio, fillProgress, fillSeed, time);

    // ── Gold pill (legacy 3-arg variant; keep alongside new DrawApplyPill) ─
    public static Vector2 DrawGoldPillSize(string label, float trackPx, float scale)
        => BoutiqueChassis.DrawGoldPillSize(label, trackPx, scale);

    public static void DrawGoldPill(ImDrawListPtr dl, Vector2 min, Vector2 max,
        string label, float trackPx, float scale, bool hovered, bool showPlus = false)
        => BoutiqueChassis.DrawGoldPill(dl, min, max, label, trackPx, scale, hovered, showPlus);

    // ── Save pill (legacy variant, disabled support) ───────────────────
    public static bool DrawSavePill(ImDrawListPtr dl, Vector2 min, Vector2 max,
        string label, float trackPx, float scale, string id, bool disabled,
        Func<string, bool, float> sheenProvider, string? disabledReason = null)
        => BoutiqueChassis.DrawSavePill(dl, min, max, label, trackPx, scale, id,
            disabled, sheenProvider, disabledReason!);

    // ── Cancel button (legacy with explicit font) ───────────────────────
    public static bool DrawCancelBtn(ImDrawListPtr dl, Vector2 min, Vector2 max,
        string label, float trackPx, float scale, string id, ImFontPtr font)
        => BoutiqueChassis.DrawCancelBtn(dl, min, max, label, trackPx, scale, id, font);

    // ── Icon button legacy overloads (no id, void return) ───────────────
    public static void DrawIconButton30(ImDrawListPtr dl, Vector2 min, float scale,
        ImFontPtr iconFont, float iconFontSize, string glyph, bool hovered, Vector4 hoverInk)
        => BoutiqueChassis.DrawIconButton30(dl, min, scale, iconFont, iconFontSize, glyph, hovered, hoverInk);

    public static void DrawIconButtonSized(ImDrawListPtr dl, Vector2 min, float side, float scale,
        ImFontPtr iconFont, float iconFontSize, string glyph, bool hovered, Vector4 hoverInk)
        => BoutiqueChassis.DrawIconButtonSized(dl, min, side, scale, iconFont, iconFontSize, glyph, hovered, hoverInk);

    // ── New dot (notification dot on icon button) ───────────────────────
    public static void DrawNewDot(ImDrawListPtr dl, Vector2 buttonMax, float scale, double time)
        => BoutiqueChassis.DrawNewDot(dl, buttonMax, scale, time);

    // ── Sort tab (gold underline + glow on active) ──────────────────────
    public static float DrawSortTab(ImDrawListPtr dl, Vector2 pos, string label,
        float trackPx, float scale, bool isActive, bool isHovered)
        => BoutiqueChassis.DrawSortTab(dl, pos, label, trackPx, scale, isActive, isHovered);

    // ── Count badge (gold-at-8% bg, gold-at-28% border, gold ink) ───────
    public static Vector2 MeasureCountBadge(string text, float trackPx, float scale)
        => BoutiqueChassis.MeasureCountBadge(text, trackPx, scale);

    public static void DrawCountBadge(ImDrawListPtr dl, Vector2 pos, string text,
        float trackPx, float scale)
        => BoutiqueChassis.DrawCountBadge(dl, pos, text, trackPx, scale);

    // ── Search pill background only (caller draws input themselves) ─────
    public static void DrawSearchPillBackground(ImDrawListPtr dl, Vector2 min, Vector2 max,
        float scale, bool focused)
        => BoutiqueChassis.DrawSearchPillBackground(dl, min, max, scale, focused);

    // ── Hero prompt (CHOOSE YOUR CHARACTER with gold wings) ─────────────
    public static void DrawHeroPrompt(ImDrawListPtr dl, Vector2 centre, float availWidth,
        float trackPx, float scale, string text)
        => BoutiqueChassis.DrawHeroPrompt(dl, centre, availWidth, trackPx, scale, text);

    // ── Ambient atmosphere effects ──────────────────────────────────────
    public static void DrawAmbientSpots(ImDrawListPtr dl, Vector2 mn, Vector2 mx, double time, float scale)
        => BoutiqueChassis.DrawAmbientSpots(dl, mn, mx, time, scale);

    public static void DrawAmbientSpots(ImDrawListPtr dl, Vector2 mn, Vector2 mx, double time)
        => BoutiqueChassis.DrawAmbientSpots(dl, mn, mx, time);

    public static void DrawCenterAuroraUnderHero(ImDrawListPtr dl, Vector2 mn, Vector2 mx, double time, float scale)
        => BoutiqueChassis.DrawCenterAuroraUnderHero(dl, mn, mx, time, scale);

    public static void DrawHumLines(ImDrawListPtr dl, Vector2 mn, Vector2 mx, double time, float scale)
        => BoutiqueChassis.DrawHumLines(dl, mn, mx, time, scale);

    public static void DrawHumLines(ImDrawListPtr dl, Vector2 mn, Vector2 mx, double time)
        => BoutiqueChassis.DrawHumLines(dl, mn, mx, time);

    public static void DrawDustMotes(ImDrawListPtr dl, Vector2 mn, Vector2 mx, double time, float scale)
        => BoutiqueChassis.DrawDustMotes(dl, mn, mx, time, scale);

    public static void DrawWindowBreathe(ImDrawListPtr dl, Vector2 min, Vector2 max, double time)
        => BoutiqueChassis.DrawWindowBreathe(dl, min, max, time);

    public static void DrawAuroraSpot(ImDrawListPtr dl, Vector2 centre, float rx, float ry,
        Vector4 colour, int layers = 12)
        => BoutiqueChassis.DrawAuroraSpot(dl, centre, rx, ry, colour, layers);

    // ── Card slip (chamfered card silhouette with optional applied glow) ─
    public static void DrawCardSlip(ImDrawListPtr dl, Vector2 min, Vector2 max,
        Vector4 npCol, bool isHovered, bool isApplied, float scale)
        => BoutiqueChassis.DrawCardSlip(dl, min, max, npCol, isHovered, isApplied, scale);

    // ── Applied chip primitives ─────────────────────────────────────────
    public static void DrawAppliedCornerBrackets(ImDrawListPtr dl, Vector2 min, Vector2 max,
        float scale, double time, Vector4 npCol, float sparkT = -1f, float hoverAmount = 0f)
        => BoutiqueChassis.DrawAppliedCornerBrackets(dl, min, max, scale, time, npCol, sparkT, hoverAmount);

    public static Vector2 MeasureAppliedChip(float trackPx, float scale)
        => BoutiqueChassis.MeasureAppliedChip(trackPx, scale);

    public static void DrawAppliedChip(ImDrawListPtr dl, Vector2 pos, float trackPx, float scale, Vector4 npCol)
        => BoutiqueChassis.DrawAppliedChip(dl, pos, trackPx, scale, npCol);

    public static void DrawAppliedChip(ImDrawListPtr dl, Vector2 pos, float trackPx, float scale)
        => BoutiqueChassis.DrawAppliedChip(dl, pos, trackPx, scale);

    /// <summary>Compact 4-arg applied chip variant returning the rendered width.</summary>
    public static float DrawAppliedChip(ImDrawListPtr dl, Vector2 pos, float scale, string label = "APPLIED")
        => BoutiqueChassis.DrawAppliedChip(dl, pos, scale, label);

    // ── Main chip (CURRENT crown chip) ──────────────────────────────────
    public static Vector2 MeasureMainChip(string label, float trackPx, float scale,
        ImFontPtr iconFont, float iconFontSize, string crownGlyph)
        => BoutiqueChassis.MeasureMainChip(trackPx, scale, iconFont, iconFontSize, crownGlyph);

    public static void DrawMainChip(ImDrawListPtr dl, Vector2 pos, float trackPx, float scale,
        ImFontPtr iconFont, float iconFontSize, string crownGlyph)
        => BoutiqueChassis.DrawMainChip(dl, pos, trackPx, scale, iconFont, iconFontSize, crownGlyph);

    // ── Applied card halo / pulse / seal pips / shimmer ─────────────────
    public static void DrawAppliedHaloRings(ImDrawListPtr dl, Vector2 imgMin, Vector2 imgMax,
        float scale, double time)
        => BoutiqueChassis.DrawAppliedHaloRings(dl, imgMin, imgMax, scale, time);

    public static void DrawAppliedBeatRipple(ImDrawListPtr dl, Vector2 centre, float baseRadius,
        float scale, double time)
        => BoutiqueChassis.DrawAppliedBeatRipple(dl, centre, baseRadius, scale, time);

    public static void DrawAppliedSealPips(ImDrawListPtr dl, Vector2 cardMin, Vector2 cardMax, float scale)
        => BoutiqueChassis.DrawAppliedSealPips(dl, cardMin, cardMax, scale);

    public static void DrawAppliedNameShimmer(ImDrawListPtr dl, Vector2 textPos, string text,
        float scale, double time)
        => BoutiqueChassis.DrawAppliedNameShimmer(dl, textPos, text, scale, time);

    // Page-dot pager
    public static void DrawPageDot(ImDrawListPtr dl, Vector2 centre, float scale, bool isActive, bool isHovered, double time)
        => BoutiqueChassis.DrawPageDot(dl, centre, scale, isActive, isHovered, time);

    public static void DrawPageDot(ImDrawListPtr dl, Vector2 centre, float scale, bool isActive, bool isHovered)
        => BoutiqueChassis.DrawPageDot(dl, centre, scale, isActive, isHovered);

    public static void DrawWardrobePagerRow(ImDrawListPtr dl, Vector2 stMn, Vector2 stMx,
        float yCenter, int total, int curr, int fromIdx, float t, bool isTransitioning,
        float scale, Vector4 accent, Vector4 accentWarm,
        Dalamud.Interface.ManagedFontAtlas.IFontHandle? glyphFont,
        Action<int> onJumpToPage, string? highlightTokenKey = null)
        => BoutiqueChassis.DrawWardrobePagerRow(dl, stMn, stMx, yCenter, total, curr, fromIdx, t,
            isTransitioning, scale, accent, accentWarm, glyphFont, onJumpToPage, highlightTokenKey);

    public static void DrawPageArrow(ImDrawListPtr dl, Vector2 min, float scale,
        ImFontPtr iconFont, float iconFontSize, string glyph, bool hovered, bool disabled)
        => BoutiqueChassis.DrawPageArrow(dl, min, scale, iconFont, iconFontSize, glyph, hovered, disabled);

    // ── Footer link (icon + tracked-caps label, optional heart pulse) ───
    public static Vector2 MeasureFooterLink(string label, float trackPx, float scale,
        ImFontPtr iconFont, float iconFontSize, string glyph)
        => BoutiqueChassis.MeasureFooterLink(label, trackPx, scale, iconFont, iconFontSize, glyph);

    public static void DrawFooterLink(ImDrawListPtr dl, Vector2 pos, string label,
        float trackPx, float scale, ImFontPtr iconFont, float iconFontSize, string glyph,
        bool hovered, bool isHeart, double time)
        => BoutiqueChassis.DrawFooterLink(dl, pos, label, trackPx, scale, iconFont, iconFontSize, glyph, hovered, isHeart, time);

    // ── Design panel / row body / row corner glow / row rail ────────────
    public static void DrawDpEdge(ImDrawListPtr dl, Vector2 min, Vector2 max,
        float scale, ImFontPtr iconFont, float iconFontSize,
        string chevronLeftGlyph, string countText, bool hovered)
        => BoutiqueChassis.DrawDpEdge(dl, min, max, scale, iconFont, iconFontSize,
            chevronLeftGlyph, countText, hovered);

    public static void DrawAppliedRowAccent(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
        => BoutiqueChassis.DrawAppliedRowAccent(dl, min, max, scale);

    public static void DrawDpAccentLine(ImDrawListPtr dl, Vector2 panelMin, Vector2 panelMax, float scale)
        => BoutiqueChassis.DrawDpAccentLine(dl, panelMin, panelMax, scale);

    public static void DrawRowBodyGradient(ImDrawListPtr dl, Vector2 min, Vector2 max,
        Vector4 topCol, Vector4 botCol, Vector4 parentBg, float chamfer)
        => BoutiqueChassis.DrawRowBodyGradient(dl, min, max, topCol, botCol, parentBg, chamfer);

    public static void DrawRowInsetHairline(ImDrawListPtr dl, Vector2 min, Vector2 max,
        float chamfer, Vector4 col)
        => BoutiqueChassis.DrawRowInsetHairline(dl, min, max, chamfer, col);

    public static void DrawRowCornerGlow(ImDrawListPtr dl, Vector2 max, float scale,
        Vector4 glowCol, float strength)
        => BoutiqueChassis.DrawRowCornerGlow(dl, max, scale, glowCol, strength);

    public static void DrawRowRail(ImDrawListPtr dl, float x, float midY, float halfH,
        float scale, Vector4 col, float glowAlpha = 0f, float glowRadius = 0f)
        => BoutiqueChassis.DrawRowRail(dl, x, midY, halfH, scale, col, glowAlpha, glowRadius);

    // ── Folder primitives (Design panel folder spine + tabs) ────────────
    public static void DrawFolderTabBody(ImDrawListPtr dl, Vector2 min, Vector2 max,
        Vector4 topCol, Vector4 botCol, Vector4 parentBg, float chamfer)
        => BoutiqueChassis.DrawFolderTabBody(dl, min, max, topCol, botCol, parentBg, chamfer);

    public static void DrawFolderTopBinding(ImDrawListPtr dl, Vector2 headMin, Vector2 headMax,
        float scale, Vector4 col, float chamfer)
        => BoutiqueChassis.DrawFolderTopBinding(dl, headMin, headMax, scale, col, chamfer);

    public static void DrawFolderSpine(ImDrawListPtr dl, float x, float topY, float botY,
        float scale, Vector4 col)
        => BoutiqueChassis.DrawFolderSpine(dl, x, topY, botY, scale, col);

    public static void DrawSpineTick(ImDrawListPtr dl, float spineX, float rowLeftX, float midY,
        float scale, Vector4 col, bool isApplied)
        => BoutiqueChassis.DrawSpineTick(dl, spineX, rowLeftX, midY, scale, col, isApplied);

    public static void DrawSpineTick(ImDrawListPtr dl, float spineX, float midY, float scale,
        Vector4 col, bool isApplied)
        => BoutiqueChassis.DrawSpineTick(dl, spineX, midY, scale, col, isApplied);

    // ── Form style stack (push/pop common form colors + spacing) ────────
    public static void PushFormStyle()  => BoutiqueChassis.PushFormStyle();
    public static void PopFormStyle()   => BoutiqueChassis.PopFormStyle();

    public static bool DrawFormHeader(ImDrawListPtr dl, Vector2 min, Vector2 max,
        float scale, string kicker, string title, Vector4? npCol,
        ImFontPtr labelFont, ImFontPtr titleFont, ImFontPtr iconFont, string xGlyph)
        => BoutiqueChassis.DrawFormHeader(dl, min, max, scale, kicker, title, npCol,
            labelFont, titleFont, iconFont, xGlyph);

    public static void DrawSimpleSectionLabel(string label, float scale, float maxWidth = 0f)
        => BoutiqueChassis.DrawSimpleSectionLabel(label, scale, maxWidth);

    public static void DrawSectionHead(string roman, string title, string meta,
        ImFontPtr smallFont, ImFontPtr titleFont, float scale)
        => BoutiqueChassis.DrawSectionHead(roman, title, meta, smallFont, titleFont, scale);

    // ── Boutique colour swatch (28x28 chamfered, opens colour picker) ───
    public static bool DrawBoutiqueColorSwatch(string id, ref Vector3 colour, float scale)
        => BoutiqueChassis.DrawBoutiqueColorSwatch(id, ref colour, scale);

    /// <summary>Legacy boutique checkbox: id, ref bool, label, description, font + descFont.</summary>
    public static bool DrawBoutiqueCheckbox(string id, ref bool value, string label,
        string description, float scale, ImFontPtr labelFont, ImFontPtr descFont)
        => BoutiqueChassis.DrawBoutiqueCheckbox(id, ref value, label, description, scale, labelFont, descFont);

    public static bool DrawCRToggleRow(ImDrawListPtr dl, Vector2 min, Vector2 max,
        float scale, ref bool isChecked, string title, string description,
        ImFontPtr titleFont, ImFontPtr descFont)
        => BoutiqueChassis.DrawCRToggleRow(dl, min, max, scale, ref isChecked, title, description,
            titleFont, descFont);

    public static bool DrawBoutiqueTextInput(string id, ref string value, int maxLen,
        float width, string placeholder = "", ImGuiInputTextFlags flags = 0)
        => BoutiqueChassis.DrawBoutiqueTextInput(id, ref value, maxLen, width, placeholder, flags);

    /// <summary>Chamfered text button (tracked-caps Oswald label, 5px chamfer). Same as DrawMiniBtn under a different name.</summary>
    public static bool DrawChamferedTextButton(string label, float w, float h, float scale, string id)
        => BoutiqueChassis.DrawChamferedTextButton(label, w, h, scale, id);

    public static void DrawMacroEditor(ref string macroText, string id, float scale,
        Func<string> regenerate, Action paste, ImFontPtr smallFont, float editorH = 170f)
        => BoutiqueChassis.DrawMacroEditor(ref macroText, id, scale, regenerate, paste, smallFont, editorH);

    // ── Hover sheen (UIStyles statics) ──────────────────────────────────
    /// <summary>
    /// Per-id hover-sweep elapsed time tracker. Returns 0 when not hovered, or
    /// the elapsed seconds since hover began. Pair with DrawHoverSheen for
    /// the canonical sheen sweep over a button rect.
    /// </summary>
    public static float GetHoverElapsedTime(string id, bool isHovered)
        => UIStyles.GetHoverElapsedTime(id, isHovered);

    /// <summary>Apply hover sheen overlay to the most recently drawn item.</summary>
    public static void ApplyHoverSheenToLastItem(string id, float maxAlpha = 0.18f)
        => UIStyles.ApplyHoverSheenToLastItemStatic(id, maxAlpha);

    /// <summary>Draw a sheen sweep band over the supplied rect with the given progress.</summary>
    public static void DrawHoverSheen(ImDrawListPtr dl, Vector2 mn, Vector2 mx,
        float progress, float maxAlpha = 0.20f)
        => UIStyles.DrawHoverSheen(dl, mn, mx, progress, maxAlpha);
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using CharacterSelectPlugin.Windows.Styles;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace CharacterSelectPlugin.Windows;

/// <summary>
/// Boutique coverflow lookbook for the active character's designs. 1:1 of
/// design-mockups/wardrobe/16-themed.html: gold-on-velvet stage, single fractional
/// scrollPos drives every card's height/width/position/brightness, editorial info
/// panel + footer scrubber + pager dots. Opened via /wardrobe or the Design Panel.
/// </summary>
public class WardrobeWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    // ── Layout constants ────────────────────────────────────────────────
    // Card row enlarged to fill more of the stage. Floor band + pool pushed
    // down to sit just under the new card bottom so the previews dominate the
    // upper two-thirds of the stage and the editorial sits flush below them.
    private const float WindowW       = 660f;
    private const float WindowH       = 820f;
    private const float RibbonH       = 28f;
    private const float HeaderPadTop  = 14f;
    private const float HeaderPadBot  = 12f;
    private const float TitleSpacing  = 6f;
    private const float HeaderRuleTop = 10f;
    private const float ToolbarPadTop = 10f;
    private const float ToolbarPadBot = 12f;
    private const float ToolbarPadX   = 20f;
    private const float InputH        = 28f;
    private const float CornerBtn     = 26f;
    private const float StageRowTop   = 18f;   // row-frame top within stage
    private const float StageRowH     = 380f;  // row-frame / focus tier height
    private const float FloorBandY    = 404f;  // hairline directly under cards (the design-row baseline)
    private const float FloorPoolY    = 404f;  // floor pool anchored to band
    private const float FloorPoolH    = 26f;   // tight pool - sits at the cards' feet
    private const float ApplyPillTopOffset = 6f;  // below row-frame bottom
    private const float EditorialBottom    = 96f; // from stage bottom (clear of footer hairline)
    private const float EditorialH         = 168f;
    private const float FooterBottom       = 14f; // from stage bottom
    private const float FooterH            = 78f; // bumped - more room between scrubber + pager
    private const float CardChamfer        = 12f; // TR + BL chamfer for cards
    private const float UnitW              = 170f; // legacy (now computed dynamically)

    // Tier breakpoints
    private const float SideThreshold = 0.5f;
    private const float EdgeThreshold = 1.5f;
    private const float OffstageStart = 2.5f;

    // Animation periods
    private const float SpotBreathPeriod  = 6.0f;
    private const float FloorShimmerPeriod= 4.5f;
    private const float HexBreathPeriod   = 2.8f;
    private const float BeadPeriod        = 3.0f;
    private const float PulseDotPeriod    = 1.6f;
    private const float PagerPulsePeriod  = 2.2f;
    private const float RibbonPipPeriod   = 2.4f;
    private const float FocusSheenPeriod  = 7.0f;
    private const float FocusSheenSweep   = 0.56f; // ~8% of period
    private const float HoverSheenSide    = 0.7f;
    private const float HoverSheenFocus   = 0.9f;

    // Apply pill / click VFX
    private const float ApplyBurstDur   = 0.6f;
    private const float ClickRingDur    = 0.9f;
    private const float ClickFlashDur   = 0.5f;
    private const float FloorBoostDur   = 0.4f;

    // Snap / momentum
    private const float SnapDurDefault  = 0.22f;
    private const float SnapDurClick    = 0.28f;
    private const float MomentumExpBase = 0.05f;     // exp decay base per second

    // ── Default palette (overridden by Custom theme + np-toggle) ─────────
    private static readonly Vector4 DefaultGold     = Boutique.Gold;
    private static readonly Vector4 DefaultGoldWarm = Boutique.GoldWarm;
    private static readonly Vector4 DefaultGoldDeep = Boutique.GoldDeep;
    // gold-bright #FFF1A8 - used for hex tag inner fill + warm wash highlights
    private static readonly Vector4 DefaultGoldBright = new(1f, 241f / 255f, 168f / 255f, 1f);
    private static readonly Vector4 DefaultWinBg    = Boutique.Surface0;

    // Resolved each frame
    private Vector4 accent     = DefaultGold;
    private Vector4 accentWarm = DefaultGoldWarm;
    private Vector4 accentDeep = DefaultGoldDeep;
    private Vector4 accentAura = new(1f, 214f / 255f, 0f, 0.10f);
    private Vector4 winBg      = DefaultWinBg;
    private Vector4 cardBg     = Boutique.Surface1;
    private Vector4 cardBgBot  = Boutique.Surface0;
    // Custom-theme card border + name text colours (resolved from
    // custom.wardrobeCardBorder / custom.wardrobeNameText each frame).
    private Vector4 cardBorderCol = new(80f / 255f, 90f / 255f, 100f / 255f, 0.55f);
    private Vector4 nameTextCol   = Boutique.Text;

    private bool useNameplateAccent = false;

    // ── Image aspect cache ──────────────────────────────────────────────
    private readonly Dictionary<Guid, float> imageAspectCache = new();

    // ── Public target override ──────────────────────────────────────────
    /// <summary>When set, the Wardrobe shows this character's designs instead of the active character.</summary>
    public Character? TargetCharacter { get; set; } = null;

    // ── Filter state ────────────────────────────────────────────────────
    private string searchQuery = "";
    private int localSortOverride = -1;
    private static readonly string[] SortLabels =
        { "Design Panel", "Favourites", "Alphabetical", "Newest", "Oldest", "Manual" };
    private string lastFilterKey = "";

    // ── Carousel state (single fractional scrollPos drives everything) ──
    private float scrollPos = 0f;
    private float velocity  = 0f;
    private bool  isDragging = false;
    private float dragStartMouseX = 0f;
    private float dragStartScrollPos = 0f;
    private float lastDragX = 0f;
    private double lastDragT = 0;
    private float lastVelPx = 0f;
    private bool  momentumActive = false;
    private bool  snapActive = false;
    private float snapStart = 0f, snapTarget = 0f;
    private double snapStartT = 0;
    private float snapDur = SnapDurDefault;
    private bool  justDragged = false;
    private double dragEndT = 0;

    // Per-card hover progress (lerps 0↔1)
    private readonly Dictionary<Guid, float> hoverProgress = new();
    /// <summary>0 = card sits at its shrunk-to-fit size, 1 = card is fully
    /// expanded to its natural aspect-preserved size on hover. Lerped each
    /// frame so the wide-preview hover-expand has a smooth ease.</summary>
    private readonly Dictionary<Guid, float> expandProgress = new();

    // Hover sheen (one-shot on hover-enter)
    private readonly Dictionary<Guid, double> hoverSheenStart = new();

    // Apply VFX state per design
    private readonly Dictionary<Guid, double> applyStart = new();
    private double floorBoostStart = -10;

    // Focus-sheen reset moment (on focus-owner change)
    private double focusSheenAnchor = 0;
    private int    lastFocusOwner = -1;

    // Apply pill state
    private bool focusHovered = false;
    private double focusHoverStart = -10; // for slide-in

    // Filter-change soft snap
    private double filterSnapAnchor = -10;

    // Stage bounds captured each frame so the chamfer mask sampler can
    // reproduce the stage atmosphere colour at any point.
    private Vector2 stageMinF;
    private Vector2 stageMaxF;

    // Encore HelpWindow pager-dot transition state
    private int pagerFromIndex = -1;
    private int pagerDisplayedIndex = -1;
    private double pagerTransitionStartT = -1;
    private const float PagerTransitionSec = 0.32f;

    // Tracks open transitions so we can scroll to the active design on open
    private bool wasOpenLastFrame = false;

    // Window
    private bool pendingClose = false;

    public WardrobeWindow(Plugin plugin) : base("Wardrobe###CSPlusWardrobeBoutique",
        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking |
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize)
    {
        this.plugin = plugin;
        // Coverflow is built around a fixed 720×880 portrait chassis. Resizing
        // would break the layout, so the size is locked every frame and the
        // resize handle is hidden via NoResize. New ###id forces ImGui to drop
        // any saved size from the previous grid-style wardrobe.
        Size = new Vector2(WindowW, WindowH);
        SizeCondition = ImGuiCond.Always;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(WindowW, WindowH),
            MaximumSize = new Vector2(WindowW, WindowH)
        };
    }

    public void Dispose() { }

    private int _chromeColorCount = 0;
    public override void PreDraw()
    {
        ResolveThemeColors();
        _chromeColorCount = Styles.ThemeHelper.PushWindowChromeColors(plugin.Configuration);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, winBg);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
        Styles.ThemeHelper.PopWindowChromeColors(_chromeColorCount);
        _chromeColorCount = 0;
    }

    public override void Draw()
    {
        if (pendingClose) { pendingClose = false; IsOpen = false; return; }

        ResolveThemeColors();

        var s = Math.Clamp(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier, 0.5f, 3f);
        var dl = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();

        var character = TargetCharacter ?? plugin.GetActiveCharacter() ?? plugin.activeCharacter;
        var designs = character != null ? GetFilteredSortedDesigns(character) : new List<CharacterDesign>();

        // ── Open-transition hook ────────────────────────────────────────
        // When the wardrobe just opened, scroll directly to the design that
        // was last applied for this character (or the most-recently-applied
        // one if the wardrobe is showing a TargetCharacter that isn't the
        // currently active one). Falls back to index 0 if nothing matches.
        bool justOpened = IsOpen && !wasOpenLastFrame;
        wasOpenLastFrame = IsOpen;
        if (justOpened && character != null && designs.Count > 0)
        {
            int idx = FindLastAppliedDesignIndex(character, designs);
            scrollPos = idx;
            velocity = 0;
            snapActive = false;
            momentumActive = false;
            lastFilterKey = designs.Count + "_" + string.Join(",", designs.Select(d => d.Id.ToString()).OrderBy(x => x));
        }

        // Reset scrollPos when filter result set changes (search / sort)
        string filterKey = designs.Count + "_" + string.Join(",", designs.Select(d => d.Id.ToString()).OrderBy(x => x));
        if (!justOpened && filterKey != lastFilterKey)
        {
            lastFilterKey = filterKey;
            // Soft-snap to 0 instead of hard jump
            if (designs.Count > 0)
            {
                StartSnap(0, SnapDurClick);
                filterSnapAnchor = ImGui.GetTime();
            }
            else
            {
                scrollPos = 0;
                velocity = 0;
                snapActive = false;
                momentumActive = false;
            }
        }

        // ── Window chassis: 1px BorderSoft outer border + faint gold inner ──
        DrawWindowChassis(dl, winPos, winSize, s);

        // ── Vertical layout: ribbon → header → toolbar → stage ──
        var cursor = winPos;

        DrawRibbon(dl, cursor, winSize.X, s, character, designs.Count);
        cursor.Y += RibbonH * s;

        float headerH = DrawHeader(dl, cursor, winSize.X, s, character, designs.Count);
        cursor.Y += headerH;

        float toolbarH = DrawToolbar(dl, cursor, winSize.X, s);
        cursor.Y += toolbarH;

        var stageMin = cursor;
        var stageMax = new Vector2(winPos.X + winSize.X, winPos.Y + winSize.Y);
        DrawStage(dl, stageMin, stageMax, s, character, designs);

        // ── Window corner brackets (BL + BR, gold @ 40% per mockup) ──
        DrawCornerBracketsBLBR(dl, winPos, winSize, s);
    }

    // ═══════════════ THEME RESOLUTION ═══════════════

    private void ResolveThemeColors()
    {
        bool isCustom = plugin.Configuration.SelectedTheme == ThemeSelection.Custom;
        var config = isCustom ? plugin.Configuration.CustomTheme : null;

        // Window bg + card surfaces (Custom theme can override accent / bg / card colours)
        winBg = GetCustomColor(config, "custom.wardrobeBg", DefaultWinBg);
        cardBg = GetCustomColor(config, "custom.wardrobeCardBg", Boutique.Surface1);
        cardBgBot = Boutique.Surface0;
        cardBorderCol = GetCustomColor(config, "custom.wardrobeCardBorder",
            new Vector4(80f / 255f, 90f / 255f, 100f / 255f, 0.55f));
        nameTextCol = GetCustomColor(config, "custom.wardrobeNameText", Boutique.Text);

        // Default accent track: gold tokens from CodexChassis
        accent     = GetCustomColor(config, "custom.wardrobeAccent", DefaultGold);
        accentWarm = DefaultGoldWarm;
        accentDeep = DefaultGoldDeep;

        // Per-character override: nameplate colour as accent
        var character = TargetCharacter ?? plugin.GetActiveCharacter() ?? plugin.activeCharacter;
        useNameplateAccent = character != null && character.UseNameplateColorInWardrobe;
        if (useNameplateAccent)
        {
            var np = character!.NameplateColor;
            accent     = new Vector4(np.X, np.Y, np.Z, 1f);
            accentWarm = Boutique.Lerp(accent, new Vector4(1, 1, 1, 1), 0.30f);
            accentDeep = new Vector4(accent.X * 0.55f, accent.Y * 0.55f, accent.Z * 0.55f, 1f);
        }
        accentAura = new Vector4(accent.X, accent.Y, accent.Z, 0.10f);
    }

    private static Vector4 GetCustomColor(CustomThemeConfig? config, string key, Vector4 fallback)
    {
        if (config != null && config.ColorOverrides.TryGetValue(key, out var packed) && packed.HasValue)
            return CustomThemeDefinitions.UnpackColor(packed.Value);
        return fallback;
    }

    // ═══════════════ WINDOW CHASSIS + BACKGROUND IMAGE ═══════════════

    private void DrawWindowChassis(ImDrawListPtr dl, Vector2 winPos, Vector2 winSize, float s)
    {
        // Custom theme background image (drawn first, at the very back)
        if (plugin.Configuration.SelectedTheme == ThemeSelection.Custom)
        {
            var config = plugin.Configuration.CustomTheme;
            if (!string.IsNullOrEmpty(config.WardrobeBackgroundImagePath) &&
                File.Exists(config.WardrobeBackgroundImagePath))
            {
                var bgTex = Plugin.TextureProvider.GetFromFile(config.WardrobeBackgroundImagePath).GetWrapOrDefault();
                if (bgTex != null)
                {
                    float zoom = config.WardrobeBackgroundImageZoom;
                    float imgW = winSize.X * zoom;
                    float imgH = winSize.Y * zoom;
                    float offX = config.WardrobeBackgroundImageOffsetX * winSize.X * 0.5f;
                    float offY = config.WardrobeBackgroundImageOffsetY * winSize.Y * 0.5f;
                    float imgX = winPos.X + (winSize.X - imgW) * 0.5f + offX;
                    float imgY = winPos.Y + (winSize.Y - imgH) * 0.5f + offY;

                    dl.PushClipRect(winPos, winPos + winSize, true);
                    dl.AddImage((ImTextureID)bgTex.Handle,
                        new Vector2(imgX, imgY), new Vector2(imgX + imgW, imgY + imgH),
                        Vector2.Zero, Vector2.One,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, config.WardrobeBackgroundImageOpacity)));
                    dl.PopClipRect();
                }
            }
        }

        // Outer border: 1px BorderSoft
        dl.AddRect(winPos, winPos + winSize,
            Boutique.U32(Boutique.WithAlpha(Boutique.Border, 0.85f)),
            0f, ImDrawFlags.None, 1f * s);

        // Inner faint gold accent line (matches mockup `inset 0 0 0 1px gold@2.5%`)
        dl.AddRect(winPos + new Vector2(1, 1), winPos + winSize - new Vector2(1, 1),
            Boutique.U32(Boutique.WithAlpha(accent, 0.025f)),
            0f, ImDrawFlags.None, 1f);
    }

    private void DrawCornerBracketsBLBR(ImDrawListPtr dl, Vector2 winPos, Vector2 winSize, float s)
    {
        // Mockup: BL+BR brackets only, 14×14, 1px gold @ 40%, inset 5px
        float size = 14f * s;
        float inset = 5f * s;
        uint c = Boutique.U32(Boutique.WithAlpha(accent, 0.40f));

        var bl = new Vector2(winPos.X + inset, winPos.Y + winSize.Y - inset);
        dl.AddLine(new Vector2(bl.X, bl.Y - size), bl, c, 1f * s);
        dl.AddLine(bl, new Vector2(bl.X + size, bl.Y), c, 1f * s);

        var br = new Vector2(winPos.X + winSize.X - inset, winPos.Y + winSize.Y - inset);
        dl.AddLine(new Vector2(br.X, br.Y - size), br, c, 1f * s);
        dl.AddLine(br, new Vector2(br.X - size, br.Y), c, 1f * s);
    }

    // ═══════════════ RIBBON ═══════════════

    private void DrawRibbon(ImDrawListPtr dl, Vector2 origin, float winW, float s,
        Character? character, int designCount)
    {
        float h = RibbonH * s;
        var min = origin;
        var max = new Vector2(origin.X + winW, origin.Y + h);

        // Vertical gradient: ribbonTop → ribbonBot
        uint top = Boutique.U32(Boutique.RibbonTop);
        uint bot = Boutique.U32(Boutique.RibbonBot);
        dl.AddRectFilledMultiColor(min, max, top, top, bot, bot);

        // Top hairline (gold @ 50%, solid at outer edges → transparent at middle)
        float ruleH = 1f * s;
        uint goldStrong = Boutique.U32(Boutique.WithAlpha(accent, 0.50f));
        uint goldClear  = Boutique.U32(Boutique.WithAlpha(accent, 0.0f));
        dl.AddRectFilledMultiColor(
            min, new Vector2(min.X + winW * 0.45f, min.Y + ruleH),
            goldStrong, goldClear, goldClear, goldStrong);
        dl.AddRectFilledMultiColor(
            new Vector2(min.X + winW * 0.55f, min.Y),
            new Vector2(max.X, min.Y + ruleH),
            goldClear, goldStrong, goldStrong, goldClear);

        // Bottom hairline (gold @ 26%, opposite pattern: transparent at edges → solid at middle)
        uint goldMid = Boutique.U32(Boutique.WithAlpha(accent, 0.26f));
        dl.AddRectFilledMultiColor(
            new Vector2(min.X, max.Y - ruleH),
            new Vector2(min.X + winW * 0.5f, max.Y),
            goldClear, goldMid, goldMid, goldClear);
        dl.AddRectFilledMultiColor(
            new Vector2(min.X + winW * 0.5f, max.Y - ruleH),
            max, goldMid, goldClear, goldClear, goldMid);

        // Pulsing pip (left side) - axis-aligned square. Pulse formula and
        // halo/core proportions match the PatchNotesWindow ribbon pip:
        // halo size constant, alpha modulates with the pulse; core stays
        // bright at full alpha. Brighter gold-warm core matches PN's
        // (1, 0.88, 0.15) bright gold versus base accent.
        double t = ImGui.GetTime();
        float pulse = 0.55f + 0.45f * (float)Math.Sin(t * 2.2);
        float padX = 14f * s;
        float coreR = 3f * s;
        float haloR = 6f * s;
        var pipCenter = new Vector2(min.X + padX + haloR, min.Y + h * 0.5f);
        // Halo - pulsed alpha
        dl.AddRectFilled(
            new Vector2(pipCenter.X - haloR, pipCenter.Y - haloR),
            new Vector2(pipCenter.X + haloR, pipCenter.Y + haloR),
            Boutique.U32(Boutique.WithAlpha(accent, 0.35f * pulse)));
        // Core - bright accent-warm at full alpha
        dl.AddRectFilled(
            new Vector2(pipCenter.X - coreR, pipCenter.Y - coreR),
            new Vector2(pipCenter.X + coreR, pipCenter.Y + coreR),
            Boutique.U32(accentWarm));

        // Ribbon tracked-caps: WARDROBE · CHARACTER NAME - bumped to Med11
        // (was Med10), brightened character name from TextDim → Text-warm so
        // both segments are clearly readable against the dark gradient.
        var ribbonFontHandle = plugin.OswaldMed11 ?? plugin.OswaldMed10;
        if (ribbonFontHandle != null)
        {
            using (ribbonFontHandle.Push())
            {
                float fontH = ImGui.GetFontSize();
                float trackPx = fontH * 0.22f;
                float baseY = min.Y + h * 0.5f - fontH * 0.5f;
                float textX = pipCenter.X + 10f * s;
                uint textCol = Boutique.U32(Boutique.Text);
                uint sepCol  = Boutique.U32(Boutique.WithAlpha(accentDeep, 0.85f));
                uint softCol = Boutique.U32(Boutique.WithAlpha(Boutique.Text, 0.92f));

                float w1 = Boutique.DrawTrackedText(dl, new Vector2(textX, baseY),
                    "WARDROBE", textCol, trackPx);
                textX += w1 + 10f * s;

                dl.AddText(new Vector2(textX, baseY), sepCol, "·");
                textX += ImGui.CalcTextSize("·").X + 10f * s;

                string charName = character != null
                    ? (!string.IsNullOrWhiteSpace(character.Alias) ? character.Alias : character.Name)
                    : "NO CHARACTER";
                charName = (charName ?? "").ToUpperInvariant();
                Boutique.DrawTrackedText(dl, new Vector2(textX, baseY),
                    charName, softCol, fontH * 0.18f);
            }
        }

        // Count tag (right side): "{N} DESIGNS" - Oswald 600, 0.20em
        var countFontHandle = plugin.OswaldSemi9;
        if (countFontHandle != null)
        {
            using (countFontHandle.Push())
            {
                string countText = $"{designCount} DESIGNS";
                float fontH = ImGui.GetFontSize();
                float trackPx = fontH * 0.20f;
                float textW = Boutique.MeasureTrackedText(countText, trackPx);
                float padTagX = 7f * s;
                float padTagY = 2f * s;
                var tagMax = new Vector2(max.X - padX, min.Y + h * 0.5f + fontH * 0.5f + padTagY);
                var tagMin = new Vector2(tagMax.X - textW - padTagX * 2,
                                         min.Y + h * 0.5f - fontH * 0.5f - padTagY);
                dl.AddRectFilled(tagMin, tagMax,
                    Boutique.U32(new Vector4(0, 0, 0, 0.55f)));
                dl.AddRect(tagMin, tagMax,
                    Boutique.U32(accentDeep), 0f, ImDrawFlags.None, 1f);
                Boutique.DrawTrackedText(dl,
                    new Vector2(tagMin.X + padTagX, tagMin.Y + padTagY),
                    countText, Boutique.U32(accentWarm), trackPx);
            }
        }
    }

    // ═══════════════ HEADER ═══════════════

    private float DrawHeader(ImDrawListPtr dl, Vector2 origin, float winW, float s,
        Character? character, int designCount)
    {
        float padTop = HeaderPadTop * s;
        float padBot = HeaderPadBot * s;
        float btn    = CornerBtn * s;

        // Compute total header height for layout
        // Title (24px) + 7px + subtitle (~13px) + 12px rule margin + 2px rule + padBot
        float titleH = 24f * s;
        float subH   = 13f * s;
        float ruleH  = 2f * s;
        float headerH = padTop + titleH + TitleSpacing * s + subH + HeaderRuleTop * s + ruleH + padBot;

        var min = origin;
        var max = new Vector2(origin.X + winW, origin.Y + headerH);

        // Background: simple top→bg vertical gradient. No radial circles -
        // those read as 4 distinct discs, not a soft wash. The single bottom-
        // anchored aurora spot below provides the warm fade the mockup wants.
        uint cTop = Boutique.U32(new Vector4(0x0c / 255f, 0x0e / 255f, 0x14 / 255f, 1f));
        uint cBot = Boutique.U32(Boutique.Bg);
        dl.AddRectFilledMultiColor(min, max, cTop, cTop, cBot, cBot);

        // Faint warm wash anchored at the bottom of the header. Centre is
        // pulled UP into the header by ry so the aurora's falloff hits zero
        // exactly at max.Y - no hard cutoff line where the stage clips it.
        float washRy = (max.Y - min.Y) * 0.55f;
        DrawAuroraSpot(dl,
            centre: new Vector2((min.X + max.X) * 0.5f, max.Y - washRy),
            rx: (max.X - min.X) * 0.42f,
            ry: washRy,
            colour: accent,
            peakAlpha: 0.07f);

        // ── Corner button: nameplate-accent toggle (left) ──
        var npChar = TargetCharacter ?? plugin.GetActiveCharacter() ?? plugin.activeCharacter;
        var npBtnMin = new Vector2(min.X + 8f * s, min.Y + padTop);
        var npBtnMax = npBtnMin + new Vector2(btn, btn);
        if (npChar != null)
        {
            DrawNameplateToggleButton(dl, npBtnMin, npBtnMax, s, npChar);
        }

        // ── Corner button: close (right) ──
        var closeMin = new Vector2(max.X - 8f * s - btn, min.Y + padTop);
        var closeMax = closeMin + new Vector2(btn, btn);
        DrawCloseButton(dl, closeMin, closeMax, s);

        // ── Title (centred): tracked-caps "WARDROBE" ──
        // HeaderFont (26 px Noto Sans JP Medium) - same handle PatchNotesWindow
        // and CSAchievementToast use for big display titles. Native size,
        // sharp, properly proportioned for the in-game UI (not the browser
        // mockup's 24 px desktop spec).
        if (plugin.HeaderFont != null)
        {
            using (plugin.HeaderFont.Push())
            {
                string title = "WARDROBE";
                float fontH = ImGui.GetFontSize();
                float trackPx = fontH * 0.32f;
                float titleW = Boutique.MeasureTrackedText(title, trackPx);
                var titleY = min.Y + padTop + (btn - fontH) * 0.5f;
                var titleX = (min.X + max.X) * 0.5f - titleW * 0.5f;
                Boutique.DrawTrackedText(dl,
                    new Vector2(titleX + 1.5f * s, titleY + 1.5f * s),
                    title, Boutique.U32(new Vector4(0, 0, 0, 0.55f)), trackPx);
                Boutique.DrawTrackedText(dl,
                    new Vector2(titleX, titleY),
                    title, Boutique.U32(Boutique.Text), trackPx);
            }
        }

        // ── Subtitle: "{Display} ◆ {N} LOOKS" - Oswald 500 with primitive
        // diamond separator (the U+25C6 glyph isn't in the loaded fonts and
        // rendered as a "?" placeholder before). ──
        var subHandle = plugin.OswaldMed11 ?? plugin.OswaldBody11;
        if (subHandle != null)
        {
            using (subHandle.Push())
            {
                string display = character != null
                    ? (!string.IsNullOrWhiteSpace(character.Alias) ? character.Alias : character.Name) ?? ""
                    : "";
                string subA = (display ?? "").ToUpperInvariant();
                string subB = $"{designCount} LOOKS";
                float fontH = ImGui.GetFontSize();
                float trackPx = fontH * 0.40f;
                float wA = Boutique.MeasureTrackedText(subA, trackPx);
                float diamondGap = 14f * s;            // padding either side of the diamond
                float diamondHalf = fontH * 0.30f;     // diamond half-size proportional to font
                float wSep = diamondGap * 2 + diamondHalf * 2;
                float wB = Boutique.MeasureTrackedText(subB, trackPx);
                float total = wA + wSep + wB;
                float subY = min.Y + padTop + btn + TitleSpacing * s;
                float subX = (min.X + max.X) * 0.5f - total * 0.5f;

                uint goldFade = Boutique.U32(Boutique.WithAlpha(accent, 0.55f));
                uint goldDeepU = Boutique.U32(Boutique.WithAlpha(accentDeep, 0.85f));

                Boutique.DrawTrackedText(dl, new Vector2(subX, subY), subA, goldFade, trackPx);
                subX += wA + diamondGap;

                // Primitive diamond (rotated quad) instead of ◆ glyph
                float diamondCY = subY + fontH * 0.50f;
                float diamondCX = subX + diamondHalf;
                dl.AddQuadFilled(
                    new Vector2(diamondCX, diamondCY - diamondHalf),
                    new Vector2(diamondCX + diamondHalf, diamondCY),
                    new Vector2(diamondCX, diamondCY + diamondHalf),
                    new Vector2(diamondCX - diamondHalf, diamondCY),
                    goldDeepU);
                subX += diamondHalf * 2 + diamondGap;

                Boutique.DrawTrackedText(dl, new Vector2(subX, subY), subB, goldFade, trackPx);
            }
        }

        // ── Header rule: 50% width, 1px gold gradient transparent → 50% → transparent ──
        float lineW = winW * 0.50f;
        float lineX = min.X + (winW - lineW) * 0.5f;
        float lineY = max.Y - padBot - ruleH - 2f * s;
        uint goldRule = Boutique.U32(Boutique.WithAlpha(accent, 0.50f));
        uint trans    = Boutique.U32(Boutique.WithAlpha(accent, 0f));
        dl.AddRectFilledMultiColor(
            new Vector2(lineX, lineY),
            new Vector2(lineX + lineW * 0.5f, lineY + ruleH),
            trans, goldRule, goldRule, trans);
        dl.AddRectFilledMultiColor(
            new Vector2(lineX + lineW * 0.5f, lineY),
            new Vector2(lineX + lineW, lineY + ruleH),
            goldRule, trans, trans, goldRule);

        return headerH;
    }

    private void DrawNameplateToggleButton(ImDrawListPtr dl, Vector2 min, Vector2 max, float s, Character npChar)
    {
        bool active = npChar.UseNameplateColorInWardrobe;
        var np = npChar.NameplateColor;

        // InvisibleButton at native cursor pos for click handling
        ImGui.SetCursorScreenPos(min);
        ImGui.InvisibleButton("##wardNpToggle", max - min);
        bool hovered = ImGui.IsItemHovered();
        bool clicked = ImGui.IsItemClicked();
        if (clicked)
        {
            npChar.UseNameplateColorInWardrobe = !npChar.UseNameplateColorInWardrobe;
            plugin.SaveConfiguration();
        }
        if (hovered)
        {
            DrawBoutiqueTooltip(active
                ? $"Using {npChar.Name}'s nameplate colour as accent.\nClick to use the theme accent instead."
                : $"Click to use {npChar.Name}'s nameplate colour as the Wardrobe accent.",
                accent);
        }

        // Background
        Vector4 bg, border, glyph;
        if (active)
        {
            bg = new Vector4(np.X, np.Y, np.Z, 0.10f);
            border = new Vector4(np.X, np.Y, np.Z, 1f);
            glyph = new Vector4(np.X, np.Y, np.Z, 1f);
        }
        else
        {
            bg = hovered ? Boutique.WithAlpha(accent, 0.06f) : new Vector4(0, 0, 0, 0.40f);
            border = hovered ? accentDeep : Boutique.BorderSoft;
            glyph = hovered ? accentWarm : Boutique.WithAlpha(Boutique.TextFaint, 1f);
        }
        dl.AddRectFilled(min, max, Boutique.U32(bg), 1f * s);
        dl.AddRect(min, max, Boutique.U32(border), 1f * s, ImDrawFlags.None, 1f * s);

        // Hand-drawn hexagon (matches mockup's ⬢)
        var center = (min + max) * 0.5f;
        float r = (max.X - min.X) * 0.32f;
        Span<Vector2> hex = stackalloc Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            float a = i * MathF.PI / 3f - MathF.PI / 2f; // flat-top hex
            hex[i] = new Vector2(center.X + MathF.Cos(a) * r, center.Y + MathF.Sin(a) * r);
        }
        unsafe
        {
            fixed (Vector2* p = hex)
                dl.AddConvexPolyFilled(p, 6, Boutique.U32(glyph));
        }
    }

    private void DrawCloseButton(ImDrawListPtr dl, Vector2 min, Vector2 max, float s)
    {
        ImGui.SetCursorScreenPos(min);
        ImGui.InvisibleButton("##wardClose", max - min);
        bool hovered = ImGui.IsItemHovered();
        bool clicked = ImGui.IsItemClicked();
        if (clicked) pendingClose = true;
        if (hovered)
            DrawBoutiqueTooltip("Close",
                new Vector4(239f / 255f, 68f / 255f, 68f / 255f, 1f));

        Vector4 bg = hovered
            ? new Vector4(239f / 255f, 68f / 255f, 68f / 255f, 0.10f)
            : new Vector4(0, 0, 0, 0.40f);
        Vector4 border = hovered
            ? new Vector4(239f / 255f, 68f / 255f, 68f / 255f, 0.55f)
            : Boutique.BorderSoft;
        Vector4 glyph = hovered
            ? new Vector4(239f / 255f, 68f / 255f, 68f / 255f, 1f)
            : Boutique.WithAlpha(Boutique.TextFaint, 1f);
        dl.AddRectFilled(min, max, Boutique.U32(bg), 1f * s);
        dl.AddRect(min, max, Boutique.U32(border), 1f * s, ImDrawFlags.None, 1f * s);

        // Drawn × glyph (two crossing lines, no font dependency)
        var c = (min + max) * 0.5f;
        float r = (max.X - min.X) * 0.22f;
        uint glyphC = Boutique.U32(glyph);
        dl.AddLine(new Vector2(c.X - r, c.Y - r), new Vector2(c.X + r, c.Y + r), glyphC, 1.5f * s);
        dl.AddLine(new Vector2(c.X + r, c.Y - r), new Vector2(c.X - r, c.Y + r), glyphC, 1.5f * s);
    }

    // ═══════════════ TOOLBAR ═══════════════

    private float DrawToolbar(ImDrawListPtr dl, Vector2 origin, float winW, float s)
    {
        float padTop = ToolbarPadTop * s;
        float padBot = ToolbarPadBot * s;
        float padX   = ToolbarPadX * s;
        float h = padTop + InputH * s + padBot;

        var min = origin;
        var max = new Vector2(origin.X + winW, origin.Y + h);

        // Surface0 background
        dl.AddRectFilled(min, max, Boutique.U32(Boutique.Surface0));

        // Bottom border: 1px BorderSoft
        dl.AddLine(new Vector2(min.X, max.Y - 1f * s), new Vector2(max.X, max.Y - 1f * s),
            Boutique.U32(Boutique.BorderSoft), 1f * s);
        // Plus a fading gold accent under the border (mockup ::after)
        uint goldStrong = Boutique.U32(Boutique.WithAlpha(accent, 0.35f));
        uint goldClear  = Boutique.U32(Boutique.WithAlpha(accent, 0f));
        float accStartX = min.X + padX;
        float accEndX   = max.X - padX;
        float aw = accEndX - accStartX;
        dl.AddRectFilledMultiColor(
            new Vector2(accStartX, max.Y),
            new Vector2(accStartX + aw * 0.5f, max.Y + 1f * s),
            goldClear, goldStrong, goldStrong, goldClear);
        dl.AddRectFilledMultiColor(
            new Vector2(accStartX + aw * 0.5f, max.Y),
            new Vector2(accEndX, max.Y + 1f * s),
            goldStrong, goldClear, goldClear, goldStrong);

        // Layout: search (flex) + sort pill (168px), 10px gap
        float gap = 10f * s;
        float sortW = 168f * s;
        float searchW = (winW - padX * 2f) - sortW - gap;

        var searchMin = new Vector2(min.X + padX, min.Y + padTop);
        var searchMax = searchMin + new Vector2(searchW, InputH * s);
        DrawSearchInput(dl, searchMin, searchMax, s);

        var sortMin = new Vector2(searchMax.X + gap, min.Y + padTop);
        var sortMax = sortMin + new Vector2(sortW, InputH * s);
        DrawSortPill(dl, sortMin, sortMax, s);

        return h;
    }

    private void DrawSearchInput(ImDrawListPtr dl, Vector2 min, Vector2 max, float s)
    {
        // Backing rect drawn by us; ImGui InputText overlaid for actual input
        bool focused = ImGui.IsAnyItemActive(); // approximated below

        // Need to know hover/focus AFTER InputText, so render the border at the end.
        // Order: bg first → InputText → border.

        // Background
        dl.AddRectFilled(min, max,
            Boutique.U32(new Vector4(8f / 255f, 10f / 255f, 14f / 255f, 0.85f)));

        // Search icon - FontAwesome magnifier glyph (), GoldDeep tint.
        // Replaces the previous hand-drawn circle+stick which read crudely.
        ImGui.PushFont(UiBuilder.IconFont);
        string searchGlyph = "";
        float iconFontSz = 13f * s;
        var iconNatural = ImGui.CalcTextSize(searchGlyph);
        float iconScale = iconFontSz / UiBuilder.IconFont.FontSize;
        var iconDrawSz = new Vector2(iconNatural.X * iconScale, iconNatural.Y * iconScale);
        var iconPos = new Vector2(min.X + 10f * s,
            (min.Y + max.Y) * 0.5f - iconDrawSz.Y * 0.5f);
        dl.AddText(UiBuilder.IconFont, iconFontSz, iconPos,
            Boutique.U32(accentDeep), searchGlyph);
        ImGui.PopFont();

        // ImGui InputText, transparent
        ImGui.SetCursorScreenPos(new Vector2(min.X + 28f * s, min.Y + (max.Y - min.Y - ImGui.GetTextLineHeight()) * 0.5f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.Text, Boutique.Text);
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, Boutique.WithAlpha(accent, 0.30f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
        ImGui.SetNextItemWidth((max.X - min.X) - 36f * s);
        if (ImGui.InputTextWithHint("##wardSearch", "Search the wardrobe...", ref searchQuery, 100))
        {
            scrollPos = 0; velocity = 0;
        }
        bool hovered = ImGui.IsItemHovered();
        focused = ImGui.IsItemActive() || ImGui.IsItemFocused();
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(6);

        // Border on top of the input
        Vector4 borderCol;
        if (focused) borderCol = accent;
        else if (hovered) borderCol = accentDeep;
        else borderCol = Boutique.BorderSoft;
        dl.AddRect(min, max, Boutique.U32(borderCol), 0f, ImDrawFlags.None, 1f * s);

        // Focus glow
        if (focused)
        {
            uint glow = Boutique.U32(Boutique.WithAlpha(accent, 0.30f));
            dl.AddRect(min - new Vector2(2f * s, 2f * s),
                       max + new Vector2(2f * s, 2f * s),
                       glow, 0f, ImDrawFlags.None, 2f * s);
        }
    }

    private bool sortPopupOpen = false;
    private Vector2 sortPopupAnchor;
    private void DrawSortPill(ImDrawListPtr dl, Vector2 min, Vector2 max, float s)
    {
        ImGui.SetCursorScreenPos(min);
        ImGui.InvisibleButton("##wardSortBtn", max - min);
        bool hovered = ImGui.IsItemHovered();
        bool clicked = ImGui.IsItemClicked();
        if (clicked)
        {
            sortPopupOpen = true;
            sortPopupAnchor = new Vector2(min.X, max.Y + 4f * s);
            ImGui.OpenPopup("##wardSortPopup");
        }

        // Backing
        dl.AddRectFilled(min, max,
            Boutique.U32(new Vector4(8f / 255f, 10f / 255f, 14f / 255f, 0.85f)));
        Vector4 borderCol = sortPopupOpen ? accent : (hovered ? accentDeep : Boutique.BorderSoft);
        dl.AddRect(min, max, Boutique.U32(borderCol), 0f, ImDrawFlags.None, 1f * s);

        // Three text pieces: kicker SORT (left), value (centre-leaning), chevron (right)
        // Heights: kicker 9px, value 11px, chevron 9px
        float padX = 12f * s;
        var kHandle = plugin.OswaldMed9;
        var vHandle = plugin.OswaldMed11;
        if (kHandle != null && vHandle != null)
        {
            int displaySort = localSortOverride + 1;
            string kicker = "SORT";
            string value = SortLabels[Math.Clamp(displaySort, 0, SortLabels.Length - 1)].ToUpperInvariant();

            using (kHandle.Push())
            {
                float kY = (min.Y + max.Y) * 0.5f - ImGui.GetFontSize() * 0.5f;
                Boutique.DrawTrackedText(dl,
                    new Vector2(min.X + padX, kY),
                    kicker, Boutique.U32(Boutique.TextGhost), 2.5f * s);
            }

            using (vHandle.Push())
            {
                float vY = (min.Y + max.Y) * 0.5f - ImGui.GetFontSize() * 0.5f;
                float trackPx = 1.8f * s;
                float vW = Boutique.MeasureTrackedText(value, trackPx);
                // Right-align toward chevron, with chevron in 14px space at far right
                float chevronW = 14f * s;
                float vX = max.X - padX - chevronW - vW;
                Boutique.DrawTrackedText(dl,
                    new Vector2(vX, vY),
                    value, Boutique.U32(accentWarm), trackPx);
            }
        }

        // Chevron ▾ - drawn as a small filled triangle
        var chC = (min + max) * 0.5f;
        float chR = 4f * s;
        dl.AddTriangleFilled(
            new Vector2(max.X - padX - 2f * s, chC.Y - chR * 0.5f),
            new Vector2(max.X - padX - 2f * s - chR * 2f, chC.Y - chR * 0.5f),
            new Vector2(max.X - padX - 2f * s - chR, chC.Y + chR * 0.7f),
            Boutique.U32(accentDeep));

        // Popup - themed to match the boutique chassis. Dark velvet bg,
        // gold-deep border, gold-warm hover/active items, all-caps tracked
        // labels in Oswald to match the rest of the wardrobe surfaces.
        ImGui.SetNextWindowPos(sortPopupAnchor);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(6f / 255f, 7f / 255f, 9f / 255f, 0.97f));
        ImGui.PushStyleColor(ImGuiCol.Border, Boutique.WithAlpha(accentDeep, 0.85f));
        ImGui.PushStyleColor(ImGuiCol.Header, Boutique.WithAlpha(accent, 0.20f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Boutique.WithAlpha(accent, 0.16f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Boutique.WithAlpha(accent, 0.28f));
        ImGui.PushStyleColor(ImGuiCol.Text, Boutique.Text);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 4 * s));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 1 * s));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f * s);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        if (ImGui.BeginPopup("##wardSortPopup"))
        {
            int displaySort = localSortOverride + 1;
            float itemH = 24f * s;
            float itemPadX = 14f * s;
            float itemW = 168f * s; // match the sort pill width
            var popupFont = plugin.OswaldMed11 ?? plugin.OswaldMed10;

            for (int i = 0; i < SortLabels.Length; i++)
            {
                bool isSel = displaySort == i;
                var rowMn = ImGui.GetCursorScreenPos();
                var rowMx = new Vector2(rowMn.X + itemW, rowMn.Y + itemH);
                ImGui.InvisibleButton($"##wardSortItem_{i}", new Vector2(itemW, itemH));
                bool hov = ImGui.IsItemHovered();
                bool itemClicked = ImGui.IsItemClicked();
                if (itemClicked)
                {
                    localSortOverride = i - 1;
                    scrollPos = 0; velocity = 0;
                    sortPopupOpen = false;
                    ImGui.CloseCurrentPopup();
                }

                var pdl = ImGui.GetWindowDrawList();
                if (isSel)
                {
                    pdl.AddRectFilled(rowMn, rowMx,
                        Boutique.U32(Boutique.WithAlpha(accent, 0.18f)));
                    // 2 px accent stripe on the left
                    pdl.AddRectFilled(rowMn,
                        new Vector2(rowMn.X + 2f * s, rowMx.Y),
                        Boutique.U32(accent));
                }
                else if (hov)
                {
                    pdl.AddRectFilled(rowMn, rowMx,
                        Boutique.U32(Boutique.WithAlpha(accent, 0.10f)));
                }

                if (popupFont != null)
                {
                    using (popupFont.Push())
                    {
                        float fontH = ImGui.GetFontSize();
                        float trackPx = fontH * 0.18f;
                        string label = SortLabels[i].ToUpperInvariant();
                        Vector4 col = isSel ? accentWarm : (hov ? Boutique.Text : Boutique.TextDim);
                        Boutique.DrawTrackedText(pdl,
                            new Vector2(rowMn.X + itemPadX, rowMn.Y + (itemH - fontH) * 0.5f),
                            label, Boutique.U32(col), trackPx);
                    }
                }
            }
            ImGui.EndPopup();
        }
        else if (sortPopupOpen)
        {
            sortPopupOpen = false;
        }
        ImGui.PopStyleVar(4);
        ImGui.PopStyleColor(6);
    }

    // ═══════════════ STAGE ═══════════════

    private void DrawStage(ImDrawListPtr dl, Vector2 stageMin, Vector2 stageMax, float s,
        Character? character, List<CharacterDesign> designs)
    {
        // Cache stage bounds for SampleStageColour (chamfer mask sampler)
        stageMinF = stageMin;
        stageMaxF = stageMax;

        // Stage atmosphere is clipped to stage bounds so the focus glow's
        // ellipse can't bleed UP into the toolbar (which used to create an
        // awkward warm halo straddling the toolbar/stage boundary).
        dl.PushClipRect(stageMin, stageMax, true);

        // Stage background: warm top wash → side vignettes → velvet base
        DrawStageBackground(dl, stageMin, stageMax, s);

        // Spotlight glow + cat aura
        DrawFocusGlow(dl, stageMin, stageMax, s);

        // Atmospheric motes (gold particles)
        DrawMotes(dl, stageMin, stageMax, s);

        // Floor band hairline + floor pool shimmer (drawn below cards)
        float rowFrameTop = stageMin.Y + StageRowTop * s;
        float rowFrameBot = rowFrameTop + StageRowH * s;
        float floorBandY  = stageMin.Y + FloorBandY * s;
        DrawFloorPool(dl, stageMin, stageMax, s);
        DrawFloorBand(dl, stageMin, stageMax, floorBandY, s);

        dl.PopClipRect();

        // Decide if any UI exists. Empty / no-character branches still draw atmosphere
        // but skip the carousel/editorial.
        if (character == null)
        {
            DrawEmptyState(dl, stageMin, stageMax, "Select a character to view their wardrobe.", s);
            return;
        }
        if (designs.Count == 0)
        {
            string msg = !string.IsNullOrWhiteSpace(searchQuery)
                ? $"No designs match \"{searchQuery}\"."
                : "This character doesn't have any designs yet.";
            DrawEmptyState(dl, stageMin, stageMax, msg, s);
            return;
        }

        // Animation tick (drives velocity / snap / momentum)
        TickAnimation(designs.Count, ImGui.GetIO().DeltaTime);

        // ── Stage interaction zone (covers card row, above editorial/footer) ──
        // One unified InvisibleButton handles drag AND click. Card-specific
        // hit-testing happens at release time against the cached slot rects.
        float editorialTopY = stageMax.Y - (EditorialBottom + EditorialH) * s;
        float interactTop = rowFrameTop;
        float interactH = editorialTopY - interactTop;
        var interactMin = new Vector2(stageMin.X, interactTop);
        var interactSize = new Vector2(stageMax.X - stageMin.X, interactH);
        ImGui.SetCursorScreenPos(interactMin);
        ImGui.InvisibleButton("##wardStageGesture", interactSize);
        bool stageHovered = ImGui.IsItemHovered();
        bool stageActive  = ImGui.IsItemActive();

        // Wheel
        if (stageHovered && Math.Abs(ImGui.GetIO().MouseWheel) > 0.01f)
        {
            float wheel = ImGui.GetIO().MouseWheel;
            int delta = wheel > 0.3f ? -1 : wheel < -0.3f ? 1 : 0;
            if (delta != 0)
            {
                int target = Math.Clamp((int)Math.Round(scrollPos) + delta, 0, designs.Count - 1);
                if (target != (int)Math.Round(scrollPos)) AnimateScrollTo(target);
            }
        }

        // Keyboard
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
        {
            if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow))
                AnimateScrollTo(Math.Max(0, (int)Math.Round(scrollPos) - 1));
            if (ImGui.IsKeyPressed(ImGuiKey.RightArrow))
                AnimateScrollTo(Math.Min(designs.Count - 1, (int)Math.Round(scrollPos) + 1));
        }

        // ── Render carousel cards + collect focus card rect for editorial/apply pill ──
        float stageW = stageMax.X - stageMin.X;
        float stageCenterX = (stageMin.X + stageMax.X) * 0.5f;

        // Track the focus owner (card nearest to scrollPos)
        int owner = Math.Clamp((int)Math.Round(scrollPos), 0, designs.Count - 1);
        if (owner != lastFocusOwner)
        {
            lastFocusOwner = owner;
            focusSheenAnchor = ImGui.GetTime();
            focusHovered = false;
            focusHoverStart = -10;
        }

        // Derive UNIT_W from the widest card in this design set so the visual
        // gap stays consistent regardless of aspect ratio mix
        float dynamicUnitW = ComputeUnitW(designs, s, stageW);

        // Walk all designs, collect render data, draw back-to-front (edge → side → focus)
        var slots = new List<CardSlot>();
        for (int i = 0; i < designs.Count; i++)
        {
            float offset = i - scrollPos;
            float abs = MathF.Abs(offset);
            if (abs > 2.6f) continue;

            float hF = HeightFactor(abs);
            float fullH = StageRowH * s * hF;
            float h = fullH;
            float aspect = GetImageAspect(designs[i]);
            float fullW = h * aspect;
            float w = fullW;

            // Wide previews (landscape screenshots) shrink uniformly to fit
            // within ~95% of the carousel unit slot. Aspect preserved (no
            // crop/letterbox). Visual feedback that portrait is the
            // preferred format, wide cards visibly demote next to portraits.
            float maxCardW = dynamicUnitW * 0.95f;
            bool wasShrunk = false;
            if (w > maxCardW)
            {
                float shrink = maxCardW / w;
                w *= shrink;
                h *= shrink;
                wasShrunk = true;
            }

            float cx = stageCenterX + offset * dynamicUnitW;
            float bottomY = rowFrameBot;
            float topY = bottomY - h;
            float leftX = cx - w * 0.5f;

            // Hover-expand: when a shrunk landscape card is hovered it eases
            // back toward its natural full size so the preview is readable.
            // The expand progress is lerped per frame for a smooth transition
            //, instant snap looked too abrupt.
            if (wasShrunk)
            {
                // Detect hover against the union of shrunk + expanded rects so
                // the cursor doesn't "fall off" mid-expansion.
                var mp = ImGui.GetIO().MousePos;
                float fullLeftX = cx - fullW * 0.5f;
                float fullTopY = rowFrameBot - fullH;
                bool cardHovered =
                    mp.X >= MathF.Min(leftX, fullLeftX) &&
                    mp.X <= MathF.Max(leftX + w, fullLeftX + fullW) &&
                    mp.Y >= MathF.Min(topY, fullTopY) &&
                    mp.Y <= rowFrameBot;

                float target = cardHovered ? 1f : 0f;
                if (!expandProgress.TryGetValue(designs[i].Id, out float ep)) ep = 0f;
                float speed = target > ep ? 10f : 6f;
                ep += (target - ep) * MathF.Min(1f, speed * ImGui.GetIO().DeltaTime);
                expandProgress[designs[i].Id] = ep;

                // Ease-out cubic on the progress, then lerp size + position.
                float t = 1f - MathF.Pow(1f - ep, 3f);
                w = w + (fullW - w) * t;
                h = h + (fullH - h) * t;
                bottomY = rowFrameBot;
                topY = bottomY - h;
                leftX = cx - w * 0.5f;
            }

            CardTier tier = abs < SideThreshold ? CardTier.Focus
                          : abs < EdgeThreshold ? CardTier.Side
                          : CardTier.Edge;

            slots.Add(new CardSlot
            {
                Index = i,
                Design = designs[i],
                Min = new Vector2(leftX, topY),
                Max = new Vector2(leftX + w, bottomY),
                Tier = tier,
                Abs = abs,
                Brightness = BrightnessFactor(abs),
                Saturation = SaturationFactor(abs),
                RawAspect = aspect,
                FrameAspect = aspect,
            });
        }

        // ── Gesture state machine (drag vs click) ──
        // Runs BEFORE drawing the cards so the new scrollPos is reflected this
        // frame. Cards have no InvisibleButtons of their own - release-position
        // hit-tests against the slot rects we just built.
        HandleStageGesture(stageHovered, stageActive, slots, character, designs.Count, dynamicUnitW, s);

        // Sort by abs descending so focus draws last (on top)
        slots.Sort((a, b) => b.Abs.CompareTo(a.Abs));

        foreach (var slot in slots)
        {
            DrawCard(dl, slot, character, s);
        }

        // Edge fades over the cards (mask at left/right)
        DrawEdgeFades(dl, stageMin, rowFrameTop, rowFrameBot, s);

        // Fav glyph + apply pill on the focus card
        var focusSlot = slots.FirstOrDefault(x => x.Index == owner);
        if (focusSlot != null)
        {
            if (focusSlot.Design.IsFavorite)
                DrawFavGlyph(dl, focusSlot, s);

            // Hover detection on focus card region (just the card rect)
            var io = ImGui.GetIO();
            var mp = io.MousePos;
            bool hover = mp.X >= focusSlot.Min.X && mp.X <= focusSlot.Max.X &&
                         mp.Y >= focusSlot.Min.Y && mp.Y <= focusSlot.Max.Y &&
                         !isDragging;
            if (hover && !focusHovered) focusHoverStart = ImGui.GetTime();
            focusHovered = hover;

            DrawApplyPill(dl, focusSlot, rowFrameBot, s);

            // Right-click: open clipboard-set-preview popup
            HandleFocusCardContextMenu(focusSlot, character);
        }

        // Floor pool brightness boost during apply
        // (handled inside DrawFloorPool via floorBoostStart timestamp)

        // ── Editorial info panel (below cards) ──
        if (focusSlot != null)
        {
            var edMin = new Vector2(stageMin.X, stageMax.Y - (EditorialBottom + EditorialH) * s);
            var edMax = new Vector2(stageMax.X, stageMax.Y - EditorialBottom * s);
            DrawEditorial(dl, edMin, edMax, focusSlot.Design, character, owner, designs.Count, s);
        }

        // ── Footer (scrollbar + pager) ──
        var ftMin = new Vector2(stageMin.X, stageMax.Y - (FooterBottom + FooterH) * s);
        var ftMax = new Vector2(stageMax.X, stageMax.Y - FooterBottom * s);
        DrawFooter(dl, ftMin, ftMax, designs.Count, s);
    }

    private class CardSlot
    {
        public int Index;
        public CharacterDesign Design = null!;
        public Vector2 Min;
        public Vector2 Max;
        public CardTier Tier;
        public float Abs;
        public float Brightness;
        public float Saturation;
        public float RawAspect;     // image's actual w/h
        public float FrameAspect;   // clamped aspect used by the card frame
    }

    private enum CardTier { Focus, Side, Edge }

    // ═══════════════ STAGE BACKGROUND LAYERS ═══════════════

    private void DrawStageBackground(ImDrawListPtr dl, Vector2 mn, Vector2 mx, float s)
    {
        // Velvet base. Top sits at the user's wardrobe bg; bottom is darkened
        // toward black to keep the velvet-fade feel of the mockup while
        // honouring the wardrobeBg override.
        Vector4 velvetTopV = winBg;
        Vector4 velvetBotV = Boutique.Lerp(velvetTopV, new Vector4(0f, 0f, 0f, velvetTopV.W), 0.50f);
        uint velvetTop = Boutique.U32(velvetTopV);
        uint velvetBot = Boutique.U32(velvetBotV);
        dl.AddRectFilledMultiColor(mn, mx, velvetTop, velvetTop, velvetBot, velvetBot);

        // Side vignettes: black @ 45% fading to transparent at 35% width
        uint vigStart = Boutique.U32(new Vector4(0, 0, 0, 0.45f));
        uint vigEnd   = Boutique.U32(new Vector4(0, 0, 0, 0f));
        float w = mx.X - mn.X;
        dl.AddRectFilledMultiColor(
            mn, new Vector2(mn.X + w * 0.35f, mx.Y),
            vigStart, vigEnd, vigEnd, vigStart);
        dl.AddRectFilledMultiColor(
            new Vector2(mn.X + w * 0.65f, mn.Y), mx,
            vigEnd, vigStart, vigStart, vigEnd);

        // Top warm wash: aurora ellipse anchored above the stage top so only
        // its bottom half shows, feathering in from the toolbar boundary.
        float h = mx.Y - mn.Y;
        var warmWhite = new Vector4(1f, 240f / 255f, 168f / 255f, 1f);
        // Centre ABOVE stage top so peak intensity sits in the toolbar area
        // (which is invisible thanks to the stage clip rect) - the visible
        // portion is the soft falloff into the stage.
        float washRy = h * 0.40f;
        float washCentreY = mn.Y - washRy * 0.55f; // peak is above stage; ~45% of ellipse visible
        DrawAuroraSpot(dl,
            centre: new Vector2(mn.X + w * 0.50f, washCentreY),
            rx: w * 0.85f,
            ry: washRy,
            colour: warmWhite,
            peakAlpha: 0.22f);
        // Subtle accent layer for the deeper gold mid-band the mockup wants
        DrawAuroraSpot(dl,
            centre: new Vector2(mn.X + w * 0.50f, washCentreY),
            rx: w * 0.95f,
            ry: washRy * 1.10f,
            colour: accent,
            peakAlpha: 0.10f);
    }

    private void DrawFocusGlow(ImDrawListPtr dl, Vector2 mn, Vector2 mx, float s)
    {
        double t = ImGui.GetTime();
        float breath = 0.85f + 0.30f * (float)Math.Sin(t * Math.Tau / SpotBreathPeriod);
        float w = mx.X - mn.X;
        float h = mx.Y - mn.Y;

        // Warm spotlight from above, anchored at top-centre.
        DrawAuroraSpot(dl,
            new Vector2(mn.X + w * 0.50f, mn.Y + h * 0.18f),
            rx: w * 0.45f, ry: h * 0.30f,
            colour: new Vector4(1f, 240f / 255f, 168f / 255f, 1f),
            peakAlpha: 0.10f * breath);

        // Accent aura behind the cards.
        DrawAuroraSpot(dl,
            new Vector2(mn.X + w * 0.50f, mn.Y + h * 0.32f),
            rx: w * 0.55f, ry: h * 0.32f,
            colour: accent,
            peakAlpha: 0.07f);
    }

    /// <summary>
    /// Soft radial light pool - port of AchievementWindow.DrawAmbientSpots.
    /// Stacks 24 ellipse polygons from outer radius down to centre, each at
    /// peakAlpha/24, producing a smooth linear falloff with no visible discs.
    /// This is the canonical CS+ glow primitive - every wardrobe glow uses it.
    /// </summary>
    private static void DrawAuroraSpot(ImDrawListPtr dl, Vector2 centre,
        float rx, float ry, Vector4 colour, float peakAlpha)
    {
        if (rx <= 0.5f || ry <= 0.5f || peakAlpha <= 0.001f) return;
        const int Layers = 24;
        const int PolyPts = 48;
        Span<Vector2> pts = stackalloc Vector2[PolyPts];
        uint col = Boutique.U32(new Vector4(colour.X, colour.Y, colour.Z, peakAlpha / Layers));
        for (int i = 1; i <= Layers; i++)
        {
            float u = i / (float)Layers;
            float lx = rx * u;
            float ly = ry * u;
            for (int j = 0; j < PolyPts; j++)
            {
                float theta = (float)(j * Math.PI * 2.0 / PolyPts);
                pts[j] = centre + new Vector2(lx * (float)Math.Cos(theta), ly * (float)Math.Sin(theta));
            }
            unsafe
            {
                fixed (Vector2* p = pts)
                    dl.AddConvexPolyFilled(p, PolyPts, col);
            }
        }
    }

    // 11 motes - 7 cluster (c1-c7) + 4 ambient (a1-a4)
    private static readonly (float topPct, float leftPct, float period, float dx, float dy, float aHi, float aLo, float phase, bool ambient)[] MoteSpecs =
    {
        // Cluster motes - shorter periods + bigger drift than the original
        // mockup spec so the cluster feels alive rather than near-static.
        (26f,  0.51f, 5.5f, +6,  -18, 0.85f, 0.30f,  0f, false), // c1
        (56f,  0.49f, 6.8f, -8,  -14, 0.75f, 0.25f,  0f, false), // c2
        (92f,  0.53f, 7.5f, +5,  -22, 0.65f, 0.20f, -3f, false), // c3
        (132f, 0.47f, 6.0f, +7,  -18, 0.85f, 0.30f, -2f, false), // c4
        (175f, 0.52f, 7.2f, -8,  -14, 0.75f, 0.25f, -5f, false), // c5
        (220f, 0.48f, 8.0f, +5,  -22, 0.65f, 0.20f, -1f, false), // c6
        (270f, 0.51f, 6.5f, +7,  -18, 0.85f, 0.30f, -4f, false), // c7
        // Ambient corner motes - drift more freely
        (0f,   0.14f, 6.0f, -8,  -16, 0.55f, 0.18f,  0f, true ), // a1
        (0f,   0.86f, 7.0f, -8,  -16, 0.55f, 0.18f, -3f, true ), // a2
        (0f,   0.08f, 6.5f, -8,  -16, 0.55f, 0.18f, -1f, true ), // a3
        (0f,   0.92f, 7.5f, -8,  -16, 0.55f, 0.18f, -2f, true ), // a4
    };
    private static readonly float[] AmbientTops = { 0.14f, 0.22f, 0.38f, 0.30f };

    private void DrawMotes(ImDrawListPtr dl, Vector2 mn, Vector2 mx, float s)
    {
        double t = ImGui.GetTime();
        float w = mx.X - mn.X;
        float h = mx.Y - mn.Y;

        for (int i = 0; i < MoteSpecs.Length; i++)
        {
            var spec = MoteSpecs[i];
            float baseX, baseY;
            if (spec.ambient)
            {
                int aIdx = i - 7;
                baseX = mn.X + spec.leftPct * w;
                baseY = mn.Y + AmbientTops[aIdx] * h;
            }
            else
            {
                baseX = mn.X + spec.leftPct * w;
                baseY = mn.Y + spec.topPct * s;
            }

            float u = (float)((t - spec.phase) / spec.period % 1.0);
            if (u < 0) u += 1f;
            float eased = 0.5f + 0.5f * MathF.Sin(u * MathF.Tau);
            float px = baseX + spec.dx * s * eased;
            float py = baseY + spec.dy * s * eased;
            float a = spec.aLo + (spec.aHi - spec.aLo) * eased;

            // Halo (slightly bigger + brighter for visibility)
            dl.AddCircleFilled(new Vector2(px, py), 3.5f * s,
                Boutique.U32(Boutique.WithAlpha(accentWarm, a * 0.50f)));
            // Core (GoldBright when in default gold mode, otherwise the np-warm)
            var coreCol = useNameplateAccent ? accentWarm : DefaultGoldBright;
            dl.AddCircleFilled(new Vector2(px, py), 1.8f * s,
                Boutique.U32(Boutique.WithAlpha(coreCol, a)));
        }
    }

    private void DrawFloorPool(ImDrawListPtr dl, Vector2 mn, Vector2 mx, float s)
    {
        // STATIC pool - no shimmer. The breath was reading as a rough,
        // uneven pulse against the bigger editorial. Apply burst still
        // boosts intensity briefly when a design is applied.
        float boost = 1f;
        double sinceApply = ImGui.GetTime() - floorBoostStart;
        if (sinceApply >= 0 && sinceApply < FloorBoostDur)
        {
            float k = (float)(sinceApply / FloorBoostDur);
            boost = 1f + (1f - k) * 0.6f;
        }

        float w = mx.X - mn.X;
        var centre = new Vector2((mn.X + mx.X) * 0.5f, mn.Y + FloorPoolY * s + FloorPoolH * 0.20f * s);
        DrawAuroraSpot(dl, centre,
            rx: w * 0.45f,
            ry: FloorPoolH * 0.50f * s,
            colour: accent,
            peakAlpha: 0.16f * boost);
    }

    private void DrawFloorBand(ImDrawListPtr dl, Vector2 mn, Vector2 mx, float bandY, float s)
    {
        // 1px hairline: transparent → gold@10% (12%) → 45% (35%) → 55% (50%) → fade out symmetrically
        float w = mx.X - mn.X;
        uint t0 = Boutique.U32(Boutique.WithAlpha(accent, 0f));
        uint t10 = Boutique.U32(Boutique.WithAlpha(accent, 0.10f));
        uint t45 = Boutique.U32(Boutique.WithAlpha(accent, 0.45f));
        uint t55 = Boutique.U32(Boutique.WithAlpha(accent, 0.55f));

        float[] stops = { 0f, 0.12f, 0.35f, 0.50f, 0.65f, 0.88f, 1.0f };
        uint[] cols = { t0, t10, t45, t55, t45, t10, t0 };
        for (int i = 0; i < stops.Length - 1; i++)
        {
            dl.AddRectFilledMultiColor(
                new Vector2(mn.X + w * stops[i],   bandY),
                new Vector2(mn.X + w * stops[i + 1], bandY + 1f * s),
                cols[i], cols[i + 1], cols[i + 1], cols[i]);
        }
    }

    private void DrawEdgeFades(ImDrawListPtr dl, Vector2 stageMin, float rowTop, float rowBot, float s)
    {
        float fadeW = 90f * s;
        var velvet = new Vector4(6f / 255f, 7f / 255f, 9f / 255f, 1f);
        uint cFull = Boutique.U32(velvet);
        uint cClear = Boutique.U32(new Vector4(velvet.X, velvet.Y, velvet.Z, 0f));

        // Left fade
        dl.AddRectFilledMultiColor(
            new Vector2(stageMin.X, rowTop),
            new Vector2(stageMin.X + fadeW, rowBot),
            cFull, cClear, cClear, cFull);
        // Right fade
        var stageMaxX = ImGui.GetWindowPos().X + ImGui.GetWindowSize().X;
        dl.AddRectFilledMultiColor(
            new Vector2(stageMaxX - fadeW, rowTop),
            new Vector2(stageMaxX, rowBot),
            cClear, cFull, cFull, cClear);
    }

    /// <summary>UNIT_W derived from the design set's widest card so adjacent cards never overlap regardless of source aspect.</summary>
    private float ComputeUnitW(List<CharacterDesign> designs, float s, float stageW)
    {
        float maxAspect = 0.55f; // sensible portrait default
        foreach (var d in designs)
            maxAspect = Math.Max(maxAspect, GetImageAspect(d));

        float focusW = StageRowH * s * maxAspect;
        float sideW = StageRowH * s * 0.75f * maxAspect;
        float unit = (focusW + sideW) * 0.5f + 16f * s;

        // Hard cap so side cards always sit within the stage. Without this,
        // a single ultrawide preview (aspect > 1) blows the unit up to ~1000+
        // px and pushes the side cards entirely off-screen, leaving only the
        // focus card visible. The wide card itself gets uniformly shrunk in
        // DrawCard so it visibly demotes vs portrait neighbours, encouraging
        // portrait previews without forbidding landscape.
        float maxUnit = stageW * 0.42f;
        return MathF.Min(unit, maxUnit);
    }

    private static float HeightFactor(float abs)
    {
        if (abs >= OffstageStart) return 0f;
        if (abs <= 1f) return 1.00f - abs * 0.25f;
        if (abs <= 2f) return 0.75f - (abs - 1f) * 0.20f;
        return 0.55f - (abs - 2f) * 1.10f;
    }
    private static float BrightnessFactor(float abs)
    {
        if (abs >= OffstageStart) return 0f;
        if (abs <= 1f) return 1.00f - abs * 0.20f;
        if (abs <= 2f) return 0.80f - (abs - 1f) * 0.25f;
        return 0.55f - (abs - 2f) * 1.10f;
    }
    private static float SaturationFactor(float abs)
    {
        if (abs >= OffstageStart) return 0.85f;
        if (abs <= 1f) return 1.00f - abs * 0.08f;
        if (abs <= 2f) return 0.92f - (abs - 1f) * 0.07f;
        return 0.85f;
    }

    // ═══════════════ CARD DRAW ═══════════════

    private void DrawCard(ImDrawListPtr dl, CardSlot slot, Character character, float s)
    {
        var d = slot.Design;
        var mn = slot.Min;
        var mx = slot.Max;
        float chamfer = CardChamfer * s;
        bool isFocus = slot.Tier == CardTier.Focus;

        // Hover progress lerp
        float hoverTarget = 0f;
        if (isFocus && focusHovered) hoverTarget = 1f;
        if (!hoverProgress.TryGetValue(d.Id, out float hp)) hp = 0f;
        float hoverSpeed = hoverTarget > hp ? 10f : 6f;
        hp += (hoverTarget - hp) * Math.Min(1f, hoverSpeed * ImGui.GetIO().DeltaTime);
        hoverProgress[d.Id] = hp;

        // Apply burst scale (applies to focus card during apply)
        float scaleMul = 1f;
        if (applyStart.TryGetValue(d.Id, out double aStart))
        {
            double age = ImGui.GetTime() - aStart;
            if (age >= 0 && age < ApplyBurstDur)
            {
                float k = (float)(age / ApplyBurstDur);
                // 0.95 → 1.02 → 1.00 (mockup keyframes)
                if (k < 0.20f) scaleMul = 0.95f + (1.02f - 0.95f) * (k / 0.20f);
                else scaleMul = 1.02f - (1.02f - 1.00f) * ((k - 0.20f) / 0.80f);
            }
            else if (age >= ApplyBurstDur)
            {
                applyStart.Remove(d.Id);
            }
        }

        // Hover lift transform: side/edge cards lift -4px on hover; focus shifts -3px scale 1.015
        float liftY = 0f;
        if (slot.Tier != CardTier.Focus)
        {
            // Side/edge hover detection - quick inline test
            var io = ImGui.GetIO();
            bool sideHover = io.MousePos.X >= mn.X && io.MousePos.X <= mx.X &&
                             io.MousePos.Y >= mn.Y && io.MousePos.Y <= mx.Y && !isDragging;
            if (sideHover) liftY = -4f * s;
        }
        else
        {
            liftY = -3f * s * hp;
            scaleMul *= (1f + 0.015f * hp);
        }

        if (Math.Abs(scaleMul - 1f) > 0.001f)
        {
            var center = (mn + mx) * 0.5f;
            mn = center + (mn - center) * scaleMul;
            mx = center + (mx - center) * scaleMul;
        }
        if (liftY != 0f)
        {
            mn.Y += liftY;
            mx.Y += liftY;
        }

        // ── Layer 0: focus halo (aurora spot behind the card) ──
        if (isFocus)
        {
            var centre = (mn + mx) * 0.5f;
            float halfW = (mx.X - mn.X) * 0.5f;
            float halfH = (mx.Y - mn.Y) * 0.5f;
            float hoverBoost = 1f + hp * 0.30f;
            DrawAuroraSpot(dl, centre,
                rx: halfW * 1.55f * hoverBoost,
                ry: halfH * 1.30f * hoverBoost,
                colour: accent,
                peakAlpha: (0.18f + hp * 0.10f));
        }

        // ── Layer 1: card body fill (Surface1 → Surface0 mid-tone, slip polygon) ──
        var bodyMid = Boutique.Lerp(cardBg, cardBgBot, 0.5f);
        Boutique.FillSlip(dl, mn, mx, chamfer, Boutique.U32(bodyMid));

        // ── Layer 2: image (single rect, clipped to card bounds). The chamfer
        // cut areas get covered by the chamfer triangle masks at Layer 6 below
        // (positioned AFTER the focus spotlight so the warm overlay doesn't
        // leak into the chamfer cuts and create the "lighter triangle" look). ──
        bool hasImage = !string.IsNullOrEmpty(d.PreviewImagePath) && File.Exists(d.PreviewImagePath);
        dl.PushClipRect(mn, mx, true);
        if (hasImage)
        {
            var tex = Plugin.TextureProvider.GetFromFile(d.PreviewImagePath!).GetWrapOrDefault();
            if (tex != null)
            {
                Vector2 uvMin = Vector2.Zero;
                Vector2 uvMax = Vector2.One;
                if (tex.Width > 1920 || tex.Height > 1080)
                {
                    uvMin = new Vector2(0.001f, 0.001f);
                    uvMax = new Vector2(0.999f, 0.999f);
                }
                float b = slot.Brightness;
                uint tint = Boutique.U32(new Vector4(b, b, b, 1f));
                dl.AddImage((ImTextureID)tex.Handle, mn, mx, uvMin, uvMax, tint);
            }
            else
            {
                DrawCardPlaceholder(dl, mn, mx, slot.Brightness, s);
            }
        }
        else
        {
            DrawCardPlaceholder(dl, mn, mx, slot.Brightness, s);
        }
        dl.PopClipRect();

        // ── Layer 4: bottom vignette (image-anchored, transparent → black @ 25% bottom 20%) ──
        dl.PushClipRect(mn, mx, true);
        float vignetteTop = mx.Y - (mx.Y - mn.Y) * 0.20f;
        uint vTop = Boutique.U32(new Vector4(0, 0, 0, 0f));
        uint vBot = Boutique.U32(new Vector4(0, 0, 0, 0.25f));
        dl.AddRectFilledMultiColor(
            new Vector2(mn.X, vignetteTop), mx,
            vTop, vTop, vBot, vBot);

        // ── Layer 5: focus-only top spotlight gradient ──
        if (isFocus)
        {
            float spotH = (mx.Y - mn.Y) * 0.60f;
            float k = 0.20f + 0.10f * hp; // brighter on hover
            var sw = new Vector4(1f, 240f / 255f, 168f / 255f, k);
            var sw2 = new Vector4(1f, 230f / 255f, 130f / 255f, 0.08f + 0.04f * hp);
            uint cA = Boutique.U32(sw);
            uint cB = Boutique.U32(sw2);
            uint cC = Boutique.U32(new Vector4(sw.X, sw.Y, sw.Z, 0f));
            dl.AddRectFilledMultiColor(
                mn, new Vector2(mx.X, mn.Y + spotH * 0.3f),
                cA, cA, cB, cB);
            dl.AddRectFilledMultiColor(
                new Vector2(mn.X, mn.Y + spotH * 0.3f),
                new Vector2(mx.X, mn.Y + spotH),
                cB, cB, cC, cC);
        }

        // ── Layer 6: focus sheen sweep (periodic) + hover sheen (one-shot) ──
        if (isFocus)
        {
            DrawFocusSheen(dl, mn, mx, s);
        }
        DrawHoverSheen(dl, slot, mn, mx, s);

        // ── Layer 6.5: TR corner flare (aurora glow inside the slip area).
        // Drawn BEFORE the chamfer masks below so the chamfer paint cancels
        // any flare bleed past the chamfer cut line. ──
        if (slot.Tier != CardTier.Edge)
        {
            float flareR = isFocus ? 60f * s : 38f * s;
            float flareA = isFocus ? 0.55f : 0.32f;
            DrawAuroraSpot(dl,
                centre: new Vector2(mx.X, mn.Y),
                rx: flareR,
                ry: flareR,
                colour: accent,
                peakAlpha: flareA);
        }

        dl.PopClipRect();

        // ── Layer 6.6: chamfer corner masks ──
        // Each mask is painted in the EXACT colour the stage atmosphere has
        // at that corner position (sampled analytically from velvet base +
        // side vignette + top warm wash + focus glow + cat aura). The mask
        // becomes invisible against the surroundings - the cut reads as a
        // clean see-through to the stage instead of a tinted patch.
        // Vertices extend 1 px outward to absorb any AA hairline at the cut.
        float ov = 1f * s;
        // TR - sample at the corner (where the cut meets the stage)
        Vector4 trCol = SampleStageColour(new Vector2(mx.X, mn.Y));
        uint trU = Boutique.U32(trCol);
        dl.AddTriangleFilled(
            new Vector2(mx.X - chamfer - ov, mn.Y - ov),
            new Vector2(mx.X + ov, mn.Y - ov),
            new Vector2(mx.X + ov, mn.Y + chamfer + ov),
            trU);
        // BL - sample at the corner
        Vector4 blCol = SampleStageColour(new Vector2(mn.X, mx.Y));
        uint blU = Boutique.U32(blCol);
        dl.AddTriangleFilled(
            new Vector2(mn.X - ov, mx.Y - chamfer - ov),
            new Vector2(mn.X + chamfer + ov, mx.Y + ov),
            new Vector2(mn.X - ov, mx.Y + ov),
            blU);

        // ── Layer 7: inset 1px stroke (slip stroke) ──
        // Non-focus border honours custom.wardrobeCardBorder; focus uses the
        // resolved accent (which already honours custom.wardrobeAccent).
        float borderThick = isFocus ? 1.5f * s : 1f * s;
        Vector4 borderCol = isFocus ? accent : cardBorderCol;
        Boutique.StrokeSlip(dl, mn, mx, chamfer, Boutique.U32(borderCol), borderThick);

        // ── Layer 8: top accent band (4 px tall, stops at TR chamfer) ──
        // Mockup spec is 3 px (`inset 0 3px 0 0 var(--c)`) but at in-game scale
        // 3 px reads as a hairline. 4 px gives a clear gold strip. Drawn AFTER
        // the chamfer triangle masks (Layer 3) and the inset stroke (Layer 7)
        // so it sits visibly on top of both.
        dl.AddRectFilled(
            new Vector2(mn.X, mn.Y),
            new Vector2(mx.X - chamfer, mn.Y + 4f * s),
            Boutique.U32(accent));

        // (Layer 9 corner flare moved up to Layer 6.5 so the chamfer mask
        //  cancels its bleed past the cut line - was protruding visibly into
        //  the cut area when drawn after the mask.)

        // (Layer 10 was a 3-stroke outset halo - replaced by the aurora-spot
        // glow drawn BEFORE the card body so it reads as a soft pool of light
        // behind the focus card, not three sharp outlines.)

        // ── Layer 11: apply VFX (click ring + flash), green flash and ring on apply ──
        if (applyStart.TryGetValue(d.Id, out double a2))
        {
            double age = ImGui.GetTime() - a2;
            // Click flash (gold @ 45% fading 500ms)
            if (age < ClickFlashDur)
            {
                float k = (float)(age / ClickFlashDur);
                float a = (1f - k) * 0.45f;
                Boutique.FillSlip(dl, mn, mx, chamfer,
                    Boutique.U32(Boutique.WithAlpha(accentWarm, a)));
            }
            // Click ring (1.5px stroke expanding 1.0 → 1.55, 900ms)
            if (age < ClickRingDur)
            {
                float k = (float)(age / ClickRingDur);
                float scale = 1f + k * 0.55f;
                float a = (1f - k) * 0.85f;
                var c = (mn + mx) * 0.5f;
                var rmn = c + (mn - c) * scale;
                var rmx = c + (mx - c) * scale;
                Boutique.StrokeSlip(dl, rmn, rmx, chamfer * scale,
                    Boutique.U32(Boutique.WithAlpha(accent, a)),
                    1.5f * s);
            }
        }

        // Click handling moved to the stage-wide gesture handler - per-card
        // InvisibleButtons used to swallow mousedown so the drag area never
        // saw it. Card hit-testing now happens at gesture-release time against
        // the cached slot rects.
    }

    private void DrawCardPlaceholder(ImDrawListPtr dl, Vector2 mn, Vector2 mx, float brightness, float s)
    {
        // Empty card body. Honours the user's wardrobe Card Background
        // override; brightness still scales for selection-aware darkening.
        Vector4 cardBgScaled = new(cardBg.X * brightness, cardBg.Y * brightness, cardBg.Z * brightness, cardBg.W);
        Vector4 cardBgLit = new(
            Math.Min(1f, cardBg.X * brightness * 1.18f),
            Math.Min(1f, cardBg.Y * brightness * 1.18f),
            Math.Min(1f, cardBg.Z * brightness * 1.18f), cardBg.W);
        uint a  = Boutique.U32(cardBgScaled);
        uint b2 = Boutique.U32(cardBgLit);
        dl.AddRectFilledMultiColor(mn, mx, a, b2, a, b2);

        // "?" rendered at the font's NATIVE size (sharp - no atlas upscaling).
        // OswaldSemiBig is 44 px logical so it renders bright and crisp.
        var qHandle = plugin.OswaldSemiBig ?? plugin.OswaldSemiTitle ?? plugin.OswaldSemiSmall;
        if (qHandle == null) return;

        using (qHandle.Push())
        {
            string glyph = "?";
            var sz = ImGui.CalcTextSize(glyph);
            var center = (mn + mx) * 0.5f;
            var pos = new Vector2(center.X - sz.X * 0.5f, center.Y - sz.Y * 0.5f);
            // Subtle drop shadow for legibility on dark backgrounds
            dl.AddText(pos + new Vector2(1.5f * s, 1.5f * s),
                Boutique.U32(new Vector4(0, 0, 0, 0.50f * brightness)), glyph);
            dl.AddText(pos,
                Boutique.U32(new Vector4(0.55f * brightness, 0.55f * brightness, 0.62f * brightness, 0.80f)),
                glyph);
        }
    }

    // ═══════════════ FOCUS SHEEN ═══════════════

    private void DrawFocusSheen(ImDrawListPtr dl, Vector2 mn, Vector2 mx, float s)
    {
        double now = ImGui.GetTime();
        double elapsed = now - focusSheenAnchor;
        float phase = (float)((elapsed % FocusSheenPeriod) / FocusSheenPeriod);
        float sweepFrac = FocusSheenSweep / FocusSheenPeriod;
        if (phase >= sweepFrac) return;

        float k = phase / sweepFrac; // 0..1 across the sweep
        float w = mx.X - mn.X;
        float bandW = w * 0.60f;
        // Sweep range bumped so the wider band fully traverses the card
        float bandX = mn.X + (-bandW / w + 2.40f * k) * w - bandW * 0.5f;

        // Alpha envelope - peaks at k≈0.18, fades at start and end of sweep
        float a;
        if (k < 0.08f) a = (k / 0.08f) * 0.22f;
        else if (k > 0.35f) a = MathF.Max(0f, (1f - (k - 0.35f) / 0.65f) * 0.22f);
        else a = 0.22f;

        DrawDiagonalSheenBand(dl, mn, mx, bandX, bandW, a);
    }

    private void DrawHoverSheen(ImDrawListPtr dl, CardSlot slot, Vector2 mn, Vector2 mx, float s)
    {
        var io = ImGui.GetIO();
        bool sideHover = (slot.Tier != CardTier.Focus) &&
                         io.MousePos.X >= mn.X && io.MousePos.X <= mx.X &&
                         io.MousePos.Y >= mn.Y && io.MousePos.Y <= mx.Y && !isDragging;
        bool focusHover = slot.Tier == CardTier.Focus && focusHovered;
        bool hover = sideHover || focusHover;

        if (hover)
        {
            if (!hoverSheenStart.ContainsKey(slot.Design.Id))
                hoverSheenStart[slot.Design.Id] = ImGui.GetTime();
        }
        else
        {
            hoverSheenStart.Remove(slot.Design.Id);
            return;
        }

        double startT = hoverSheenStart[slot.Design.Id];
        float dur = focusHover ? HoverSheenFocus : HoverSheenSide;
        float k = (float)((ImGui.GetTime() - startT) / dur);
        if (k > 1f) return;

        float w = mx.X - mn.X;
        float bandW = w * 0.60f;
        float bandX = mn.X + (-bandW / w + 2.40f * k) * w - bandW * 0.5f;
        float peak = focusHover ? 0.32f : 0.18f;
        float a;
        if (k < 0.20f) a = (k / 0.20f) * peak;
        else a = peak * (1f - (k - 0.20f) / 0.80f);

        DrawDiagonalSheenBand(dl, mn, mx, bandX, bandW, a);
    }

    /// <summary>
    /// Diagonal warm-white sheen band, ~20° off vertical. Built from stacked
    /// 1 px slices with per-row X-offset for the slant.
    /// </summary>
    private static void DrawDiagonalSheenBand(ImDrawListPtr dl, Vector2 mn, Vector2 mx,
        float bandX, float bandW, float peakAlpha)
    {
        if (peakAlpha <= 0.001f) return;
        float h = mx.Y - mn.Y;
        if (h < 1f) return;
        float slopeOffset = h * 0.364f; // tan(20°)

        var warmCol = new Vector4(1f, 240f / 255f, 168f / 255f, 1f);
        uint cTrans = Boutique.U32(Boutique.WithAlpha(warmCol, 0f));
        uint cPeak  = Boutique.U32(Boutique.WithAlpha(warmCol, peakAlpha));

        // Pixel-tall slices for sub-pixel slope steps. AddRectFilledMultiColor
        // handles the trans→peak→trans alpha interpolation natively per-strip.
        int N = Math.Max(8, (int)MathF.Ceiling(h));
        for (int i = 0; i < N; i++)
        {
            float yT = mn.Y + (i / (float)N) * h;
            float yB = mn.Y + ((i + 1) / (float)N) * h;
            float vMid = (i + 0.5f) / N;          // 0..1, top to bottom
            float xShift = (1f - vMid) * slopeOffset; // peak shift at top, none at bottom
            float xL = bandX + xShift;
            float xM = xL + bandW * 0.5f;
            float xR = xL + bandW;

            // Left half: transparent → peak
            dl.AddRectFilledMultiColor(
                new Vector2(xL, yT), new Vector2(xM, yB),
                cTrans, cPeak, cPeak, cTrans);
            // Right half: peak → transparent
            dl.AddRectFilledMultiColor(
                new Vector2(xM, yT), new Vector2(xR, yB),
                cPeak, cTrans, cTrans, cPeak);
        }
    }

    // ═══════════════ FAV GLYPH / APPLY PILL ═══════════════
    // (Hex tag removed at the user's request - it didn't add value visually.)

    private void DrawFavGlyph(ImDrawListPtr dl, CardSlot focus, float s)
    {
        // FontAwesome solid star at top-right of card, -22px above, 14px font
        var pos = new Vector2(focus.Max.X - 16f * s, focus.Min.Y - 22f * s);
        ImGui.PushFont(UiBuilder.IconFont);
        float fontSz = 14f * s;
        // Drop shadow (low-alpha black)
        dl.AddText(ImGui.GetFont(), fontSz,
            new Vector2(pos.X + 1f * s, pos.Y + 1f * s),
            Boutique.U32(new Vector4(0, 0, 0, 0.85f)),
            "");
        // Star
        dl.AddText(ImGui.GetFont(), fontSz,
            pos, Boutique.U32(accentWarm), "");
        ImGui.PopFont();
    }

    private void DrawApplyPill(ImDrawListPtr dl, CardSlot focus, float rowFrameBot, float s)
    {
        if (!focusHovered) return;
        float k = (float)((ImGui.GetTime() - focusHoverStart) / 0.18);
        float vis = Math.Clamp(k, 0f, 1f);
        if (vis <= 0f) return;

        var pillFont = plugin.OswaldSemi9;
        if (pillFont == null) return;

        using (pillFont.Push())
        {
            string text = "CLICK TO APPLY";
            float trackPx = 3.2f * s;
            float textW = Boutique.MeasureTrackedText(text, trackPx);
            float padX = 14f * s;
            float padY = 5f * s;
            float pillW = textW + padX * 2;
            float pillH = ImGui.GetFontSize() + padY + 6f * s;
            float pillX = (focus.Min.X + focus.Max.X) * 0.5f - pillW * 0.5f;
            float yOffset = (1f - vis) * 6f * s;
            float pillY = rowFrameBot + ApplyPillTopOffset * s + yOffset;
            var pillMin = new Vector2(pillX, pillY);
            var pillMax = new Vector2(pillX + pillW, pillY + pillH);
            float pillChamfer = 6f * s;

            // Soft aurora glow behind the pill (replaces the harsh
            // AddRectFilled glow rect that read as a flat outline).
            var pillCentre = (pillMin + pillMax) * 0.5f;
            float pillHalfW = (pillMax.X - pillMin.X) * 0.5f;
            float pillHalfH = (pillMax.Y - pillMin.Y) * 0.5f;
            DrawAuroraSpot(dl, pillCentre,
                rx: pillHalfW * 1.95f,
                ry: pillHalfH * 2.30f,
                colour: accent,
                peakAlpha: 0.45f * vis);

            // Pill body (vertical gradient warm → gold) - use mid colour for slip fill
            var pillMid = Boutique.Lerp(accentWarm, accent, 0.5f);
            Boutique.FillSlip(dl, pillMin, pillMax, pillChamfer,
                Boutique.U32(Boutique.WithAlpha(pillMid, vis)));

            // Tab pointer (5px triangle pointing UP from pill top centre)
            float tabSz = 5f * s;
            var pTip = new Vector2((pillMin.X + pillMax.X) * 0.5f, pillMin.Y - tabSz);
            var pL = new Vector2((pillMin.X + pillMax.X) * 0.5f - tabSz, pillMin.Y);
            var pR = new Vector2((pillMin.X + pillMax.X) * 0.5f + tabSz, pillMin.Y);
            dl.AddTriangleFilled(pTip, pL, pR,
                Boutique.U32(Boutique.WithAlpha(accentWarm, vis)));

            // Text
            float textY = pillMin.Y + (pillH - ImGui.GetFontSize()) * 0.5f;
            float textX = (pillMin.X + pillMax.X) * 0.5f - textW * 0.5f;
            uint inkC = Boutique.U32(Boutique.WithAlpha(
                new Vector4(0x1A / 255f, 0x14 / 255f, 0x08 / 255f, 1f), vis));
            Boutique.DrawTrackedText(dl, new Vector2(textX, textY), text, inkC, trackPx);
        }
    }

    // ═══════════════ EDITORIAL ═══════════════

    private void DrawEditorial(ImDrawListPtr dl, Vector2 mn, Vector2 mx,
        CharacterDesign design, Character? owner, int idx, int total, float s)
    {
        float padL = 32f * s;
        float padR = 32f * s;
        float padBot = 14f * s;

        // Vertical gold spine: 1px wide, 124px tall, gradient gold → warm 30% → deep 70% → trans
        float spineX = mn.X + padL;
        float spineBot = mx.Y - padBot;
        float spineTop = spineBot - 124f * s;
        uint cTop = Boutique.U32(accent);
        uint cWarm = Boutique.U32(accentWarm);
        uint cDeep = Boutique.U32(accentDeep);
        uint cTrans = Boutique.U32(Boutique.WithAlpha(accent, 0f));
        // Gradient via 3 stacked rect segments
        float h = spineBot - spineTop;
        dl.AddRectFilledMultiColor(
            new Vector2(spineX, spineTop),
            new Vector2(spineX + 1f * s, spineTop + h * 0.30f),
            cTop, cTop, cWarm, cWarm);
        dl.AddRectFilledMultiColor(
            new Vector2(spineX, spineTop + h * 0.30f),
            new Vector2(spineX + 1f * s, spineTop + h * 0.70f),
            cWarm, cWarm, cDeep, cDeep);
        dl.AddRectFilledMultiColor(
            new Vector2(spineX, spineTop + h * 0.70f),
            new Vector2(spineX + 1f * s, spineBot),
            cDeep, cDeep, cTrans, cTrans);

        // Diamond bead at top of editorial spine - soft aurora glow + crisp
        // diamond core. Subtle alpha pulse, no size pulse.
        double t = ImGui.GetTime();
        float beadPulse = (float)(0.5 + 0.5 * Math.Sin(t * Math.Tau / BeadPeriod));
        float beadSize = 5f * s;
        var beadCenter = new Vector2(spineX + 0.5f * s, spineTop - 10f * s);
        // Soft aurora halo (replaces the hard AddCircleFilled blob)
        DrawAuroraSpot(dl, beadCenter,
            rx: beadSize * 3.0f,
            ry: beadSize * 3.0f,
            colour: accent,
            peakAlpha: 0.22f + 0.10f * beadPulse);
        // Diamond core - solid accent
        dl.AddQuadFilled(
            new Vector2(beadCenter.X, beadCenter.Y - beadSize),
            new Vector2(beadCenter.X + beadSize, beadCenter.Y),
            new Vector2(beadCenter.X, beadCenter.Y + beadSize),
            new Vector2(beadCenter.X - beadSize, beadCenter.Y),
            Boutique.U32(accent));

        // Right block (edition num + decorative blades) - wider to fit the
        // bumped 24 px edition num font without clipping its 100/100 width.
        float rightBlockW = 170f * s;
        float rightBlockX = mx.X - padR - rightBlockW;
        DrawEditorialRightBlock(dl, new Vector2(rightBlockX, mn.Y),
            new Vector2(mx.X - padR, mx.Y - padBot), idx, total, s);

        // Name block (left of right block)
        var nameMin = new Vector2(spineX + 24f * s, mn.Y);
        var nameMax = new Vector2(rightBlockX - 14f * s, mx.Y - padBot);
        DrawEditorialNameBlock(dl, nameMin, nameMax, design, owner, s);
    }

    private void DrawEditorialNameBlock(ImDrawListPtr dl, Vector2 mn, Vector2 mx,
        CharacterDesign d, Character? owner, float s)
    {
        // Anchored to bottom (flex-end). Build from bottom up, then position.
        float bottomY = mx.Y;

        // Meta row (lowest) - Oswald 400, 0.32em, uppercase. Bumped tracking
        // and replaced the inline "·" with a generous gold-deep diamond so
        // the two segments don't run into one another as a single sentence.
        var metaFont = plugin.OswaldBody11 ?? plugin.OswaldBody9;
        if (metaFont != null)
        {
            using (metaFont.Push())
            {
                string applied = FormatLastApplied(d.LastApplied).ToUpperInvariant();
                string created = $"CREATED {d.DateAdded.ToLocalTime():MMM dd}";
                float fontH = ImGui.GetFontSize();
                float trackPx = fontH * 0.32f;
                float metaY = bottomY - fontH;
                uint dimC = Boutique.U32(Boutique.TextDim);
                uint sepC = Boutique.U32(Boutique.WithAlpha(accentDeep, 0.85f));
                float x = mn.X;
                Boutique.DrawTrackedText(dl, new Vector2(x, metaY), applied, dimC, trackPx);
                x += Boutique.MeasureTrackedText(applied, trackPx) + 14f * s;

                // Diamond separator (matches the mods row style)
                float dSz = 2.5f * s;
                dl.AddQuadFilled(
                    new Vector2(x, metaY + fontH * 0.5f - dSz),
                    new Vector2(x + dSz, metaY + fontH * 0.5f),
                    new Vector2(x, metaY + fontH * 0.5f + dSz),
                    new Vector2(x - dSz, metaY + fontH * 0.5f),
                    sepC);
                x += dSz * 2 + 12f * s;

                Boutique.DrawTrackedText(dl, new Vector2(x, metaY), created, dimC, trackPx);
                bottomY = metaY - 9f * s; // bumped gap so meta doesn't kiss mods row
            }
        }

        // Mods row - Oswald 500, label tracked 0.30em uppercase, value plain
        // mixed-case. Bumped tracking + gaps + diamond size for clear segment
        // separation (was reading as one run-on phrase).
        var modsFont = plugin.OswaldMed13 ?? plugin.OswaldMed10;
        if (modsFont != null)
        {
            using (modsFont.Push())
            {
                float fontH = ImGui.GetFontSize();
                float trackPx = fontH * 0.32f;
                float y = bottomY - fontH;
                uint deepC = Boutique.U32(accentDeep);
                uint whiteC = Boutique.U32(Boutique.Text);
                uint sepC = Boutique.U32(accent);
                float x = mn.X;

                string lblG = "GLAMOURER";
                string valG = string.IsNullOrWhiteSpace(d.GlamourerDesign) ? "-" : d.GlamourerDesign;
                Boutique.DrawTrackedText(dl, new Vector2(x, y), lblG, deepC, trackPx);
                x += Boutique.MeasureTrackedText(lblG, trackPx) + 8f * s;
                dl.AddText(new Vector2(x, y), whiteC, valG);
                x += ImGui.CalcTextSize(valG).X + 16f * s;

                // Diamond separator - slightly larger to read as a real divider
                float dSz = 3f * s;
                dl.AddQuadFilled(
                    new Vector2(x, y + fontH * 0.5f - dSz),
                    new Vector2(x + dSz, y + fontH * 0.5f),
                    new Vector2(x, y + fontH * 0.5f + dSz),
                    new Vector2(x - dSz, y + fontH * 0.5f),
                    sepC);
                x += dSz * 2 + 14f * s;

                string lblC = "C+";
                string valC = string.IsNullOrWhiteSpace(d.CustomizePlusProfile) ? "-" : d.CustomizePlusProfile;
                Boutique.DrawTrackedText(dl, new Vector2(x, y), lblC, deepC, trackPx);
                x += Boutique.MeasureTrackedText(lblC, trackPx) + 8f * s;
                float maxX = mx.X;
                dl.PushClipRect(new Vector2(x, y - 2f * s), new Vector2(maxX, y + fontH + 2f * s), true);
                dl.AddText(new Vector2(x, y), whiteC, valC);
                dl.PopClipRect();
                bottomY = y - 9f * s;
            }
        }

        // Underline (above mods row)
        float ulY = bottomY;
        dl.AddRectFilledMultiColor(
            new Vector2(mn.X, ulY - 2f * s),
            new Vector2(mn.X + 56f * s, ulY),
            Boutique.U32(accent), Boutique.U32(accentDeep),
            Boutique.U32(accentDeep), Boutique.U32(accent));
        bottomY -= 8f * s;

        // Display name, boutique HEADLINE. Character name in the "name text"
        // theme colour (white default), design name in the wardrobe accent
        // (gold default / nameplate colour / custom.wardrobeAccent). Splits
        // by ENTITY (character vs design), not by word, so multi-word names
        // inside either entity stay one colour. Long combined headlines
        // auto-marquee with a 60 px loop gap.
        var nameFont = plugin.OswaldSemiBig ?? plugin.OswaldSemiMid;
        if (nameFont != null)
        {
            using (nameFont.Push())
            {
                string charPart = owner == null
                    ? ""
                    : (!string.IsNullOrWhiteSpace(owner.Alias) ? owner.Alias! : owner.Name ?? "");
                string designPart = string.IsNullOrWhiteSpace(d.Name) ? "Untitled" : d.Name;
                string upperChar = charPart.ToUpperInvariant();
                string upperDesign = designPart.ToUpperInvariant();

                float fontH = ImGui.GetFontSize();
                float trackPx = fontH * 0.16f;
                float nameY = bottomY - fontH * 0.95f;
                float spaceW = string.IsNullOrEmpty(upperChar) ? 0 : ImGui.CalcTextSize(" ").X;

                uint shadowC = Boutique.U32(new Vector4(0, 0, 0, 0.60f));
                uint whiteC = Boutique.U32(nameTextCol);
                uint warmC = Boutique.U32(accentWarm);

                float charW = string.IsNullOrEmpty(upperChar) ? 0 : Boutique.MeasureTrackedText(upperChar, trackPx);
                float designW = Boutique.MeasureTrackedText(upperDesign, trackPx);
                float fullW = charW + (charW > 0 ? spaceW : 0) + designW;
                float availW = mx.X - mn.X;

                var clipMn = new Vector2(mn.X - 4 * s, nameY - 4 * s);
                var clipMx = new Vector2(mx.X + 4 * s, nameY + fontH + 4 * s);
                dl.PushClipRect(clipMn, clipMx, true);

                void DrawHeadline(float baseX)
                {
                    float x = baseX;
                    if (!string.IsNullOrEmpty(upperChar))
                    {
                        Boutique.DrawTrackedText(dl,
                            new Vector2(x + 2 * s, nameY + 2 * s), upperChar, shadowC, trackPx);
                        Boutique.DrawTrackedText(dl,
                            new Vector2(x, nameY), upperChar, whiteC, trackPx);
                        x += charW + spaceW;
                    }
                    Boutique.DrawTrackedText(dl,
                        new Vector2(x + 2 * s, nameY + 2 * s), upperDesign, shadowC, trackPx);
                    Boutique.DrawTrackedText(dl,
                        new Vector2(x, nameY), upperDesign, warmC, trackPx);
                }

                if (fullW <= availW)
                {
                    DrawHeadline(mn.X);
                }
                else
                {
                    float gap = 60f * s;
                    float loopW = fullW + gap;
                    float speedPxPerSec = 30f * s;
                    float scrollX = (float)((ImGui.GetTime() * speedPxPerSec) % loopW);
                    DrawHeadline(mn.X - scrollX);
                    DrawHeadline(mn.X - scrollX + loopW);
                }

                dl.PopClipRect();
                bottomY = nameY - 3f * s;
            }
        }

        // Now-cap (above name): ▪ NOW IN FOCUS - static axis-aligned square.
        // No pulsing animation - keeps the editorial calm.
        var capFont = plugin.OswaldMed11 ?? plugin.OswaldMed9;
        if (capFont != null)
        {
            using (capFont.Push())
            {
                float fontH = ImGui.GetFontSize();
                float trackPx = fontH * 0.50f;
                float capY = bottomY - fontH;
                float dotR = 3.5f * s;
                var dotC = new Vector2(mn.X + dotR, capY + fontH * 0.5f);
                dl.AddRectFilled(
                    new Vector2(dotC.X - dotR, dotC.Y - dotR),
                    new Vector2(dotC.X + dotR, dotC.Y + dotR),
                    Boutique.U32(accent));

                Boutique.DrawTrackedText(dl,
                    new Vector2(dotC.X + dotR * 2 + 6 * s, capY),
                    "NOW IN FOCUS", Boutique.U32(Boutique.TextDim), trackPx);
            }
        }
    }

    private void DrawEditorialRightBlock(ImDrawListPtr dl, Vector2 mn, Vector2 mx,
        int idx, int total, float s)
    {
        float bottomY = mx.Y;

        // Edition num "{nn} / {total}" - Oswald 500, 0.10em. Bumped to
        // OswaldSemiSmall (24 px native) so the edition number reads as a
        // proper headline counterpart to the display name.
        var numFont = plugin.OswaldSemiSmall ?? plugin.OswaldMed13;
        if (numFont != null)
        {
            using (numFont.Push())
            {
                string nn = (idx + 1).ToString("D2");
                string of = "/";
                string totalS = total.ToString("D2");
                float fontH = ImGui.GetFontSize();
                float trackPx = fontH * 0.10f;
                float wA = Boutique.MeasureTrackedText(nn, trackPx);
                float wSep = ImGui.CalcTextSize(of).X;
                float wB = Boutique.MeasureTrackedText(totalS, trackPx);
                float w = wA + wSep + wB + 8 * s;
                float y = bottomY - fontH;
                float x = mx.X - w;
                uint warmC = Boutique.U32(accentWarm);
                uint ghostC = Boutique.U32(Boutique.TextGhost);
                Boutique.DrawTrackedText(dl, new Vector2(x, y), nn, warmC, trackPx);
                x += wA + 4 * s;
                dl.AddText(new Vector2(x, y), ghostC, of);
                x += wSep + 4 * s;
                Boutique.DrawTrackedText(dl, new Vector2(x, y), totalS, ghostC, trackPx);
                bottomY = y - 3f * s;
            }
        }

        // Edition cap "EDITION" - Oswald 500, 0.34em. Bumped to Med11.
        var capFont = plugin.OswaldMed11 ?? plugin.OswaldMed9;
        if (capFont != null)
        {
            using (capFont.Push())
            {
                string text = "EDITION";
                float fontH = ImGui.GetFontSize();
                float trackPx = fontH * 0.34f;
                float w = Boutique.MeasureTrackedText(text, trackPx);
                float y = bottomY - fontH;
                float x = mx.X - w;
                Boutique.DrawTrackedText(dl, new Vector2(x, y), text, Boutique.U32(Boutique.TextGhost), trackPx);
                bottomY = y - 6f * s;
            }
        }

        // Decorative blade strip: 3 blades (deep / accent / deep) + diamond
        // Right-aligned, ascending size to right
        float bladeY = bottomY;
        float dSz = 3f * s;
        float bx = mx.X;
        // Diamond (rightmost)
        bx -= dSz * 2;
        dl.AddQuadFilled(
            new Vector2(bx + dSz, bladeY - dSz),
            new Vector2(bx + dSz * 2, bladeY),
            new Vector2(bx + dSz, bladeY + dSz),
            new Vector2(bx, bladeY),
            Boutique.U32(accent));
        bx -= 4f * s;
        // Blade 3 (deep)
        DrawBlade(dl, bx, bladeY, accentDeep, s);
        bx -= 8f * s;
        // Blade 2 (accent)
        DrawBlade(dl, bx, bladeY, accent, s);
        bx -= 8f * s;
        // Blade 1 (deep)
        DrawBlade(dl, bx, bladeY, accentDeep, s);
    }

    /// <summary>
    /// Right-pointing chevron blade for the editorial decorative strip.
    /// Mockup uses the CSS border-triangle technique with `border-left:
    /// 8px solid` which produces a right-pointing arrowhead. Filled triangle
    /// here matches that - wide base on the LEFT, sharp tip on the RIGHT.
    /// `xRight` is the position of the tip.
    /// </summary>
    private static void DrawBlade(ImDrawListPtr dl, float xRight, float y, Vector4 col, float s)
    {
        float w = 9f * s;
        float h = 7f * s;
        dl.AddTriangleFilled(
            new Vector2(xRight - w, y - h * 0.5f),  // top-left (base)
            new Vector2(xRight,     y),              // tip (right)
            new Vector2(xRight - w, y + h * 0.5f),  // bottom-left (base)
            Boutique.U32(col));
    }

    // ═══════════════ FOOTER ═══════════════

    private void DrawFooter(ImDrawListPtr dl, Vector2 mn, Vector2 mx, int total, float s)
    {
        // Top hairline - drawn INSIDE the footer (just above the scrubber)
        // instead of hovering above the footer top edge (which used to put
        // it 16 px above and clip into the editorial meta row text).
        float padX = 30f * s;
        uint goldFade = Boutique.U32(Boutique.WithAlpha(accent, 0.30f));
        uint goldClear = Boutique.U32(Boutique.WithAlpha(accent, 0f));
        float hlY = mn.Y + 1f * s;
        dl.AddRectFilledMultiColor(
            new Vector2(mn.X + padX, hlY),
            new Vector2(mn.X + (mx.X - mn.X) * 0.5f, hlY + 1f * s),
            goldClear, goldFade, goldFade, goldClear);
        dl.AddRectFilledMultiColor(
            new Vector2(mn.X + (mx.X - mn.X) * 0.5f, hlY),
            new Vector2(mx.X - padX, hlY + 1f * s),
            goldFade, goldClear, goldClear, goldFade);

        // Row 1: scrubber (anchored toward top of footer, just below hairline)
        float row1Y = mn.Y + 10f * s;
        float row1H = 18f * s;
        DrawScrubberRow(dl,
            new Vector2(mn.X + padX, row1Y),
            new Vector2(mx.X - padX, row1Y + row1H),
            total, s);

        // Row 2: pager (anchored further from scrubber so they don't crowd)
        float row2Y = mx.Y - 22f * s;
        DrawPagerRow(dl, mn, mx, row2Y, total, s);
    }

    private void DrawScrubberRow(ImDrawListPtr dl, Vector2 mn, Vector2 mx, int total, float s)
    {
        // (DRAG ↔ caption removed - visual scrubber speaks for itself.)
        // Page count "{Roman}/{Roman}" (right) - Oswald 500, 0.20em
        var pgFont = plugin.OswaldMed11;
        float pgW = 0;
        if (pgFont != null)
        {
            using (pgFont.Push())
            {
                string nn = ToRoman(Math.Clamp((int)Math.Round(scrollPos) + 1, 1, total));
                string of = "/";
                string totalS = ToRoman(total);
                float fontH = ImGui.GetFontSize();
                float trackPx = fontH * 0.20f;
                float wA = Boutique.MeasureTrackedText(nn, trackPx);
                float wOf = ImGui.CalcTextSize(of).X;
                float wSep = wOf + 10 * s;
                float wB = Boutique.MeasureTrackedText(totalS, trackPx);
                pgW = wA + wSep + wB;
                float y = (mn.Y + mx.Y) * 0.5f - fontH * 0.5f;
                float x = mx.X - pgW;
                uint warmC = Boutique.U32(accentWarm);
                uint ghostC = Boutique.U32(Boutique.TextGhost);
                Boutique.DrawTrackedText(dl, new Vector2(x, y), nn, warmC, trackPx);
                x += wA + 5 * s;
                dl.AddText(new Vector2(x, y), ghostC, of);
                x += wOf + 5 * s;
                Boutique.DrawTrackedText(dl, new Vector2(x, y), totalS, ghostC, trackPx);
            }
        }

        // Track spans full width minus the page-count column on the right
        float trackXMin = mn.X;
        float trackXMax = mx.X - pgW - 14 * s;
        float trackY = (mn.Y + mx.Y) * 0.5f;
        float trackH = 5f * s;  // bumped from 3 → 5 px for visibility
        var tMin = new Vector2(trackXMin, trackY - trackH * 0.5f);
        var tMax = new Vector2(trackXMax, trackY + trackH * 0.5f);

        // End diamonds (rotated quads - match the boutique diamond language)
        float dSz = 3f * s;
        dl.AddQuadFilled(
            new Vector2(trackXMin - 9 * s, trackY - dSz),
            new Vector2(trackXMin - 9 * s + dSz, trackY),
            new Vector2(trackXMin - 9 * s, trackY + dSz),
            new Vector2(trackXMin - 9 * s - dSz, trackY),
            Boutique.U32(Boutique.WithAlpha(accentDeep, 0.85f)));
        dl.AddQuadFilled(
            new Vector2(trackXMax + 9 * s, trackY - dSz),
            new Vector2(trackXMax + 9 * s + dSz, trackY),
            new Vector2(trackXMax + 9 * s, trackY + dSz),
            new Vector2(trackXMax + 9 * s - dSz, trackY),
            Boutique.U32(Boutique.WithAlpha(accentDeep, 0.85f)));

        // Track bg - darker for contrast against the bold thumb
        dl.AddRectFilled(tMin, tMax,
            Boutique.U32(new Vector4(0, 0, 0, 0.70f)));
        // 1 px gold-deep border for definition
        dl.AddRect(tMin, tMax,
            Boutique.U32(Boutique.WithAlpha(accentDeep, 0.40f)),
            0f, ImDrawFlags.None, 1f);

        // Thumb - bolder, with a brighter centre band and a wider halo so
        // it reads as a primary control, not a hairline.
        if (total > 1)
        {
            float thumbWFrac = 1f / total;
            float t = scrollPos / Math.Max(1, total - 1);
            float thumbW = Math.Max(18f * s, (tMax.X - tMin.X) * thumbWFrac);
            float thumbX = tMin.X + t * ((tMax.X - tMin.X) - thumbW);
            var thumbMin = new Vector2(thumbX, tMin.Y);
            var thumbMax = new Vector2(thumbX + thumbW, tMax.Y);
            // Gradient deep → warm → deep with full-saturation centre
            uint cd = Boutique.U32(accentDeep);
            uint cw = Boutique.U32(accentWarm);
            uint ca = Boutique.U32(accent);
            float midX = thumbMin.X + thumbW * 0.5f;
            dl.AddRectFilledMultiColor(
                thumbMin, new Vector2(midX, thumbMax.Y),
                cd, ca, cw, cd);
            dl.AddRectFilledMultiColor(
                new Vector2(midX, thumbMin.Y), thumbMax,
                ca, cd, cd, cw);
            // Bright top hairline for highlight
            dl.AddRectFilled(
                new Vector2(thumbMin.X, thumbMin.Y),
                new Vector2(thumbMax.X, thumbMin.Y + 1f * s),
                Boutique.U32(Boutique.WithAlpha(accentWarm, 0.95f)));
            // Outer halo
            dl.AddRect(thumbMin - new Vector2(1.5f * s, 1.5f * s),
                       thumbMax + new Vector2(1.5f * s, 1.5f * s),
                       Boutique.U32(Boutique.WithAlpha(accent, 0.55f)),
                       0f, ImDrawFlags.None, 2f * s);
        }

        // Click-AND-drag on the track. Pressing snaps to that position;
        // holding drags scrollPos directly (no snap until release).
        var trackHitMin = new Vector2(tMin.X, tMin.Y - 4 * s);
        var trackHitSize = new Vector2(tMax.X - tMin.X, trackH + 8 * s);
        ImGui.SetCursorScreenPos(trackHitMin);
        ImGui.InvisibleButton("##wardScrubTrack", trackHitSize);
        if (total > 1 && ImGui.IsItemActive())
        {
            float u = (ImGui.GetIO().MousePos.X - tMin.X) / Math.Max(1f, tMax.X - tMin.X);
            u = Math.Clamp(u, 0f, 1f);
            // Drag: live update without snap; cancel any in-flight motion
            momentumActive = false;
            snapActive = false;
            velocity = 0;
            scrollPos = u * (total - 1);
        }
        else if (total > 1 && ImGui.IsItemDeactivated())
        {
            // Release: settle on the nearest integer
            StartSnap((int)Math.Round(scrollPos), SnapDurDefault);
        }
    }

    private void DrawPagerRow(ImDrawListPtr dl, Vector2 stMn, Vector2 stMx, float yCenter, int total, float s)
    {
        // ── Encore HelpWindow pager pattern ──────────────────────────────
        // Sharp-rect arrows (DrawNavArrow), animated lerp dots between them
        // with breathing halo on stable active and a bloom ring during the
        // chapter-change transition. Lifted directly from Encore so the
        // wardrobe pager feels native to the rest of the plugin family.
        int total_ = total;
        int curr = Math.Clamp((int)Math.Round(scrollPos), 0, total_ - 1);

        // Detect focus change → kick off transition
        if (pagerDisplayedIndex != curr)
        {
            if (pagerDisplayedIndex >= 0)
            {
                pagerFromIndex = pagerDisplayedIndex;
                pagerTransitionStartT = ImGui.GetTime();
            }
            pagerDisplayedIndex = curr;
        }
        bool isTransitioning = pagerTransitionStartT >= 0;
        float t = 1f;
        if (isTransitioning)
        {
            float tRaw = MathF.Min(1f,
                (float)((ImGui.GetTime() - pagerTransitionStartT) / PagerTransitionSec));
            t = 1f - MathF.Pow(1f - tRaw, 3f); // easeOutCubic
            if (tRaw >= 1f)
            {
                pagerTransitionStartT = -1;
                pagerFromIndex = -1;
                isTransitioning = false;
            }
        }
        int fromIdx = isTransitioning ? pagerFromIndex : curr;

        float arrSize = 26f * s;
        float dotSz = 6f * s;
        float activeDotW = 14f * s;
        float dotGap = 6f * s;

        // Lay out: prev arrow + (gap) + dots + (gap) + next arrow, centred
        float dotsW = (total_ - 1) * dotSz + activeDotW + (total_ - 1) * dotGap;
        float gap = 10f * s;
        float rowW = arrSize + gap + dotsW + gap + arrSize;
        float xStart = (stMn.X + stMx.X) * 0.5f - rowW * 0.5f;

        // Prev arrow
        var prevPos = new Vector2(xStart, yCenter - arrSize * 0.5f);
        bool prevEnabled = curr > 0;
        DrawNavArrow(dl, "##wardPagerPrev", prevPos, new Vector2(arrSize, arrSize),
            "<", !prevEnabled, () => { if (prevEnabled) AnimateScrollTo(curr - 1); });

        // Dots between arrows
        float dotsLeft = prevPos.X + arrSize + gap;
        float dotY = yCenter - dotSz * 0.5f;

        for (int i = 0; i < total_; i++)
        {
            // Walk twice to compute xFrom and xCur, then lerp
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
            if (ImGui.InvisibleButton($"##wardPagerDot_{i}", new Vector2(wi, dotSz)))
                AnimateScrollTo(i);
            bool hov = ImGui.IsItemHovered();

            // Compute fill / border state at fromIdx and curr, then lerp.
            // Done = filled @ 35%, upcoming = border @ 35%, active = full accent.
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

            // Hover lift for non-active dots between transitions
            if (hov && i != curr && !isTransitioning)
            {
                fillA = MathF.Max(fillA, 0.25f);
                borderA = MathF.Max(borderA, 0.60f);
            }

            // Breathing halo on stable active dot
            if (i == curr && !isTransitioning)
            {
                float breath = 0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * MathF.PI);
                float halo = (0.10f + 0.10f * breath) * 0.4f;
                float pad = 3f * s;
                dl.AddRectFilled(
                    new Vector2(dotMin.X - pad, dotMin.Y - pad),
                    new Vector2(dotMax.X + pad, dotMax.Y + pad),
                    Boutique.U32(Boutique.WithAlpha(accent, halo)));
            }

            if (fillA > 0.01f)
                dl.AddRectFilled(dotMin, dotMax,
                    Boutique.U32(Boutique.WithAlpha(accent, fillA)));
            if (borderA > 0.01f)
                dl.AddRect(dotMin, dotMax,
                    Boutique.U32(Boutique.WithAlpha(accent, borderA)),
                    0f, ImDrawFlags.None, 1f);

            // Bloom ring during transition - emanates from the new active dot
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
                        Boutique.U32(Boutique.WithAlpha(accent, rippleA)),
                        24, 1.5f * s);
                }
            }
        }

        // Next arrow
        float nextX = dotsLeft + dotsW + gap;
        var nextPos = new Vector2(nextX, yCenter - arrSize * 0.5f);
        bool nextEnabled = curr < total_ - 1;
        DrawNavArrow(dl, "##wardPagerNext", nextPos, new Vector2(arrSize, arrSize),
            ">", !nextEnabled, () => { if (nextEnabled) AnimateScrollTo(curr + 1); });
    }

    /// <summary>
    /// Encore HelpWindow nav-arrow pattern. Sharp rect, accent border alpha
    /// changes by state (default 24% / hovered full / disabled 10%), faint
    /// hover fill, glyph drawn at the font handle's native size.
    /// </summary>
    private void DrawNavArrow(ImDrawListPtr dl, string id, Vector2 pos, Vector2 size,
        string glyph, bool disabled, Action onClick)
    {
        ImGui.SetCursorScreenPos(pos);
        if (!disabled && ImGui.InvisibleButton(id, size)) onClick();
        bool hovered = !disabled && ImGui.IsItemHovered();

        var min = pos;
        var max = new Vector2(pos.X + size.X, pos.Y + size.Y);
        Vector4 borderCol = disabled
            ? Boutique.WithAlpha(accent, 0.10f)
            : (hovered ? accent : Boutique.WithAlpha(accent, 0.24f));
        dl.AddRect(min, max, Boutique.U32(borderCol), 0f, ImDrawFlags.None, 1f);
        if (hovered)
        {
            dl.AddRectFilled(min, max,
                Boutique.U32(Boutique.WithAlpha(accent, 0.08f)));
        }

        Vector4 textCol = disabled
            ? Boutique.TextGhost
            : (hovered ? accentWarm : Boutique.TextDim);
        var glyphFont = plugin.OswaldMed13 ?? plugin.OswaldBody13;
        if (glyphFont != null && glyphFont.Available)
        {
            using (glyphFont.Push())
            {
                var sz = ImGui.CalcTextSize(glyph);
                dl.AddText(
                    new Vector2(pos.X + (size.X - sz.X) * 0.5f,
                                pos.Y + (size.Y - sz.Y) * 0.5f),
                    Boutique.U32(textCol), glyph);
            }
        }
        else
        {
            var sz = ImGui.CalcTextSize(glyph);
            dl.AddText(
                new Vector2(pos.X + (size.X - sz.X) * 0.5f,
                            pos.Y + (size.Y - sz.Y) * 0.5f),
                Boutique.U32(textCol), glyph);
        }
    }

    // ═══════════════ STAGE GESTURE / SNAP / MOMENTUM ═══════════════

    /// <summary>
    /// Stage-wide press → drag-or-click gesture handler. The single InvisibleButton
    /// covers the whole row area, so press is captured even when starting on a card.
    /// If the pointer moves &gt; 4 px the gesture upgrades to drag; otherwise on
    /// release we hit-test against the slot rects and either apply (focus card)
    /// or snap-focus (side/edge card).
    /// </summary>
    private void HandleStageGesture(bool hovered, bool active, List<CardSlot> slots,
        Character character, int total, float unitW, float s)
    {
        var io = ImGui.GetIO();
        double now = ImGui.GetTime();

        // Press-start: captured the moment the InvisibleButton becomes active
        if (active && !isDragging)
        {
            isDragging = true;
            momentumActive = false;
            snapActive = false;
            velocity = 0;
            dragStartMouseX = io.MousePos.X;
            dragStartScrollPos = scrollPos;
            lastDragX = io.MousePos.X;
            lastDragT = now;
            lastVelPx = 0;
            justDragged = false;
        }

        if (isDragging && active)
        {
            float dx = io.MousePos.X - dragStartMouseX;
            if (Math.Abs(dx) > 4f * s) justDragged = true;

            // Track velocity for release-time momentum
            double dt = now - lastDragT;
            if (dt > 0)
                lastVelPx = (io.MousePos.X - lastDragX) / (float)dt;
            lastDragX = io.MousePos.X;
            lastDragT = now;

            // Only update scrollPos once we've crossed the click-vs-drag threshold,
            // so a stationary press doesn't nudge the carousel.
            if (justDragged && unitW > 0)
            {
                scrollPos = dragStartScrollPos - dx / unitW;
                scrollPos = Math.Clamp(scrollPos, 0, total - 1);
            }
        }

        // Release
        if (isDragging && !active)
        {
            isDragging = false;
            dragEndT = now;

            if (justDragged)
            {
                // Drag release: hand off to momentum / snap
                velocity = unitW > 0 ? -lastVelPx / unitW : 0f;
                velocity = Math.Clamp(velocity, -12f, 12f);
                if (Math.Abs(velocity) < 1.0f)
                {
                    momentumActive = false;
                    StartSnap((int)Math.Round(scrollPos), SnapDurDefault);
                }
                else
                {
                    momentumActive = true;
                }
            }
            else if (hovered)
            {
                // Click: hit-test against slot rects (focus tier draws on top so
                // its rect wins ties when overlapping with a side card).
                CardSlot? hit = null;
                CardTier hitTier = CardTier.Edge;
                foreach (var slot in slots)
                {
                    if (io.MousePos.X >= slot.Min.X && io.MousePos.X <= slot.Max.X &&
                        io.MousePos.Y >= slot.Min.Y && io.MousePos.Y <= slot.Max.Y)
                    {
                        // Prefer the focus tier if multiple rects contain the point
                        if (hit == null || slot.Tier < hitTier)
                        {
                            hit = slot;
                            hitTier = slot.Tier;
                        }
                    }
                }
                if (hit != null)
                {
                    if (hit.Tier == CardTier.Focus)
                    {
                        int designIndex = character.Designs.IndexOf(hit.Design);
                        if (designIndex >= 0) TriggerApply(hit.Design, designIndex, character);
                    }
                    else
                    {
                        AnimateScrollTo(hit.Index);
                    }
                }
            }
        }
    }

    private void TickAnimation(int total, float dt)
    {
        if (snapActive)
        {
            double elapsed = ImGui.GetTime() - snapStartT;
            float k = Math.Clamp((float)(elapsed / snapDur), 0f, 1f);
            float eased = 1f - MathF.Pow(1f - k, 3f);
            scrollPos = snapStart + (snapTarget - snapStart) * eased;
            if (k >= 1f)
            {
                scrollPos = snapTarget;
                snapActive = false;
                velocity = 0;
            }
        }
        else if (momentumActive)
        {
            scrollPos += velocity * dt;
            if (scrollPos < 0) { scrollPos = 0; velocity = 0; }
            if (scrollPos > total - 1) { scrollPos = total - 1; velocity = 0; }
            velocity *= MathF.Pow(MomentumExpBase, dt);
            if (Math.Abs(velocity) < 0.5f)
            {
                momentumActive = false;
                StartSnap((int)Math.Round(scrollPos), SnapDurDefault);
            }
        }
    }

    private void StartSnap(int target, float dur)
    {
        snapActive = true;
        snapStart = scrollPos;
        snapTarget = Math.Clamp(target, 0, int.MaxValue);
        snapStartT = ImGui.GetTime();
        snapDur = Math.Max(0.05f, dur);
    }

    private void AnimateScrollTo(int idx)
    {
        momentumActive = false;
        velocity = 0;
        StartSnap(idx, SnapDurClick);
    }

    // ═══════════════ APPLY ═══════════════

    private void TriggerApply(CharacterDesign d, int designIndex, Character character)
    {
        applyStart[d.Id] = ImGui.GetTime();
        floorBoostStart = ImGui.GetTime();
        plugin.ApplyProfile(character, designIndex);
        Plugin.Log.Info($"[Wardrobe] Applied design '{d.Name}' on character '{character.Name}'");
    }

    // ═══════════════ FOCUS CARD CONTEXT MENU (set preview from clipboard) ═══════════════

    private void HandleFocusCardContextMenu(CardSlot focus, Character character)
    {
        // No InvisibleButton (would steal mousedown from the stage gesture handler).
        // Right-click is detected globally and hit-tested against the focus rect.
        var io = ImGui.GetIO();
        bool overFocus = io.MousePos.X >= focus.Min.X && io.MousePos.X <= focus.Max.X &&
                         io.MousePos.Y >= focus.Min.Y && io.MousePos.Y <= focus.Max.Y;
        if (overFocus && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup($"##wardCtx_{focus.Design.Id}");

        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(6f / 255f, 6f / 255f, 12f / 255f, 0.97f));
        ImGui.PushStyleColor(ImGuiCol.Border, accentDeep);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        if (ImGui.BeginPopup($"##wardCtx_{focus.Design.Id}"))
        {
            ImGui.TextColored(Boutique.Text, focus.Design.Name ?? "Untitled");
            ImGui.Separator();
            if (ImGui.MenuItem("Set Preview from Clipboard"))
            {
                var captureDesign = focus.Design;
                var captureId = focus.Design.Id;
                imageAspectCache.Remove(captureId);
                _ = Task.Run(async () =>
                {
                    var path = await plugin.SaveClipboardImageForDesign(captureId);
                    if (!string.IsNullOrEmpty(path))
                    {
                        captureDesign.PreviewImagePath = path;
                        plugin.SaveConfiguration();
                        Plugin.Framework.RunOnTick(() => imageAspectCache.Remove(captureId));
                        Plugin.Log.Info($"[Wardrobe] Set preview from clipboard for '{captureDesign.Name}': {path}");
                        plugin.AchievementTracker?.OnDesignPreviewSet();
                    }
                });
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.05f, 0.05f, 0.10f, 0.97f));
                ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8));
                ImGui.BeginTooltip();
                ImGui.TextColored(Boutique.Text, "Set Preview from Clipboard");
                ImGui.Spacing();
                ImGui.TextColored(Boutique.TextDim, "1. Take a screenshot");
                ImGui.TextColored(Boutique.TextDim, "2. Copy the image to your clipboard");
                ImGui.TextColored(Boutique.TextDim, "3. Click this option");
                ImGui.EndTooltip();
                ImGui.PopStyleVar(2);
                ImGui.PopStyleColor();
            }

            // Toggle favourite
            bool fav = focus.Design.IsFavorite;
            if (ImGui.MenuItem(fav ? "Unmark favourite" : "Mark as favourite"))
            {
                focus.Design.IsFavorite = !fav;
                plugin.SaveConfiguration();
            }
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    // ═══════════════ EMPTY STATE ═══════════════

    private void DrawEmptyState(ImDrawListPtr dl, Vector2 mn, Vector2 mx, string message, float s)
    {
        // Centred icon + message, gold @ 25% icon, TextDim message
        var center = (mn + mx) * 0.5f;
        ImGui.PushFont(UiBuilder.IconFont);
        float iconSz = 56f * s;
        var iconC = Boutique.U32(Boutique.WithAlpha(accent, 0.25f));
        var icSz = ImGui.CalcTextSize(""); // shirt icon (people-clothes)
        dl.AddText(ImGui.GetFont(), iconSz,
            new Vector2(center.X - icSz.X * 0.5f * (iconSz / ImGui.GetFontSize()), center.Y - iconSz - 8 * s),
            iconC, "");
        ImGui.PopFont();

        var msgFont = plugin.OutfitMed12 ?? plugin.OutfitBody13;
        if (msgFont != null)
        {
            using (msgFont.Push())
            {
                var sz = ImGui.CalcTextSize(message);
                dl.AddText(new Vector2(center.X - sz.X * 0.5f, center.Y),
                    Boutique.U32(Boutique.TextDim), message);
            }
        }
    }

    // ═══════════════ HELPERS ═══════════════

    // Text rendering follows the PatchNotesWindow pattern - push the font
    // handle, then use Boutique.DrawTrackedText / ImGui.CalcTextSize at
    // the handle's native rasterised size. No explicit size overrides (those
    // upscale the atlas glyphs and produce blurry text).

    // ── Stage-atmosphere sampler ─────────────────────────────────────────
    // Reproduces the colour of the stage atmosphere at any position on the
    // stage, by analytically compositing every layer DrawStageBackground +
    // DrawFocusGlow paints. Used by the chamfer mask code so the mask paints
    // in EXACTLY the colour the stage would be at that point - making the
    // chamfer cut visually invisible against the surroundings.

    private Vector4 SampleStageColour(Vector2 pos)
    {
        float w = stageMaxF.X - stageMinF.X;
        float h = stageMaxF.Y - stageMinF.Y;
        if (w <= 0f || h <= 0f) return new Vector4(0.024f, 0.027f, 0.035f, 1f);

        float sx = pos.X - stageMinF.X;
        float sy = pos.Y - stageMinF.Y;
        float yPct = Math.Clamp(sy / h, 0f, 1f);

        // Velvet base - vertical gradient #060709 → #03040A
        var velvetTop = new Vector4(6f / 255f, 7f / 255f, 9f / 255f, 1f);
        var velvetBot = new Vector4(3f / 255f, 4f / 255f, 10f / 255f, 1f);
        Vector4 col = LerpV4(velvetTop, velvetBot, yPct);

        // Side vignettes - black @ 45% fading to transparent at 35% width
        float xFrac = Math.Clamp(sx / w, 0f, 1f);
        float vigA = 0f;
        if (xFrac < 0.35f) vigA = (1f - xFrac / 0.35f) * 0.45f;
        else if (xFrac > 0.65f) vigA = ((xFrac - 0.65f) / 0.35f) * 0.45f;
        if (vigA > 0f) col = CompositeV4(col, new Vector4(0, 0, 0, 1f), vigA);

        // Top warm wash - three stacked AddRectFilledMultiColor layers
        var warmWhite = new Vector4(1f, 240f / 255f, 168f / 255f, 1f);
        if (yPct < 0.08f)
        {
            float v = yPct / 0.08f;
            float a = 0.16f * (1f - v) + 0.06f * v;
            var c = LerpV4(warmWhite, accent, v);
            col = CompositeV4(col, c, a);
        }
        else if (yPct < 0.22f)
        {
            float v = (yPct - 0.08f) / 0.14f;
            float a = 0.06f * (1f - v) + 0.02f * v;
            col = CompositeV4(col, accent, a);
        }
        else if (yPct < 0.38f)
        {
            float v = (yPct - 0.22f) / 0.16f;
            float a = 0.02f * (1f - v);
            if (a > 0f) col = CompositeV4(col, accent, a);
        }

        // Focus glow - warm-white aurora at top centre
        var fgCentre = new Vector2(stageMinF.X + w * 0.50f, stageMinF.Y + h * 0.18f);
        double t = ImGui.GetTime();
        float breath = 0.85f + 0.30f * (float)Math.Sin(t * Math.Tau / SpotBreathPeriod);
        float fgIntensity = SampleAuroraIntensity(pos, fgCentre, w * 0.45f, h * 0.30f, 0.10f * breath);
        if (fgIntensity > 0f) col = CompositeV4(col, warmWhite, fgIntensity);

        // Cat aura - accent-coloured aurora behind cards
        var caCentre = new Vector2(stageMinF.X + w * 0.50f, stageMinF.Y + h * 0.32f);
        float caIntensity = SampleAuroraIntensity(pos, caCentre, w * 0.55f, h * 0.32f, 0.07f);
        if (caIntensity > 0f) col = CompositeV4(col, accent, caIntensity);

        return col;
    }

    /// <summary>
    /// Approximate intensity of an aurora spot at a given point.
    /// Aurora draws 24 nested ellipse layers, each at peakAlpha/24 - at point
    /// P the layers covering P are those with u ≥ d(P), giving composite
    /// alpha ≈ peakAlpha * (1 - d) for d ∈ [0, 1].
    /// </summary>
    private static float SampleAuroraIntensity(Vector2 pos, Vector2 centre, float rx, float ry, float peakAlpha)
    {
        if (rx <= 0.001f || ry <= 0.001f || peakAlpha <= 0f) return 0f;
        float dx = (pos.X - centre.X) / rx;
        float dy = (pos.Y - centre.Y) / ry;
        float d = MathF.Sqrt(dx * dx + dy * dy);
        if (d >= 1f) return 0f;
        return (1f - d) * peakAlpha;
    }

    private static Vector4 LerpV4(Vector4 a, Vector4 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Vector4(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t,
            a.W + (b.W - a.W) * t);
    }

    private static Vector4 CompositeV4(Vector4 baseCol, Vector4 overlay, float alpha)
    {
        if (alpha <= 0f) return baseCol;
        if (alpha >= 1f) return new Vector4(overlay.X, overlay.Y, overlay.Z, 1f);
        return new Vector4(
            baseCol.X * (1f - alpha) + overlay.X * alpha,
            baseCol.Y * (1f - alpha) + overlay.Y * alpha,
            baseCol.Z * (1f - alpha) + overlay.Z * alpha,
            1f);
    }

    /// <summary>
    /// Boutique-themed tooltip - dark velvet bg, accent-coloured 1 px border,
    /// 3 px accent stripe down the left edge, white text. Matches the rest of
    /// the wardrobe chassis instead of ImGui's default grey tooltip.
    /// </summary>
    private static void DrawBoutiqueTooltip(string text, Vector4 stripeAccent)
    {
        if (string.IsNullOrEmpty(text)) return;
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(6f / 255f, 7f / 255f, 9f / 255f, 0.96f));
        ImGui.PushStyleColor(ImGuiCol.Border, Boutique.WithAlpha(stripeAccent, 0.85f));
        ImGui.PushStyleColor(ImGuiCol.Text, Boutique.Text);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 8f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.BeginTooltip();
        var dl = ImGui.GetWindowDrawList();
        var wMn = ImGui.GetWindowPos();
        var wMx = wMn + ImGui.GetWindowSize();
        // 3 px accent stripe on the left edge of the tooltip
        dl.AddRectFilled(wMn, new Vector2(wMn.X + 3f, wMx.Y),
            Boutique.U32(stripeAccent));
        ImGui.PushTextWrapPos(360f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(3);
    }

    /// <summary>
    /// Find the index in `designs` that corresponds to the design CS+ most
    /// recently applied for this character. Falls back to:
    ///   1. Configuration.LastUsedDesignByCharacter[character.Name] (last applied via /select or wardrobe)
    ///   2. The design with the most recent LastApplied timestamp
    ///   3. Index 0
    /// </summary>
    private int FindLastAppliedDesignIndex(Character character, List<CharacterDesign> designs)
    {
        if (designs.Count == 0) return 0;

        // 1. Configured last-used name
        if (plugin.Configuration.LastUsedDesignByCharacter.TryGetValue(character.Name, out var lastName)
            && !string.IsNullOrWhiteSpace(lastName))
        {
            for (int i = 0; i < designs.Count; i++)
            {
                if (string.Equals(designs[i].Name, lastName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        // 2. Most recent LastApplied timestamp
        int bestIdx = -1;
        DateTime bestT = DateTime.MinValue;
        for (int i = 0; i < designs.Count; i++)
        {
            if (designs[i].LastApplied is DateTime la && la > bestT)
            {
                bestT = la;
                bestIdx = i;
            }
        }
        return bestIdx >= 0 ? bestIdx : 0;
    }

    // Designs with no preview image render a placeholder card at this aspect.
    // 0.62 reads as a slightly-portrait card - proportions feel like a polaroid
    // / lookbook page rather than the squat 16:9 a screenshot would produce.
    private const float PlaceholderAspect = 0.62f;

    private float GetImageAspect(CharacterDesign design)
    {
        if (imageAspectCache.TryGetValue(design.Id, out float cached))
            return cached;
        if (string.IsNullOrEmpty(design.PreviewImagePath) || !File.Exists(design.PreviewImagePath))
        {
            imageAspectCache[design.Id] = PlaceholderAspect;
            return PlaceholderAspect;
        }
        var tex = Plugin.TextureProvider.GetFromFile(design.PreviewImagePath).GetWrapOrDefault();
        if (tex != null && tex.Height > 0)
        {
            float a = (float)tex.Width / tex.Height;
            imageAspectCache[design.Id] = a;
            return a;
        }
        return PlaceholderAspect;
    }

    private static string FormatLastApplied(DateTime? la)
    {
        if (la == null) return "Never applied";
        var span = DateTime.Now - la.Value;
        if (span.TotalSeconds < 60) return "Last applied just now";
        if (span.TotalMinutes < 60) return $"Last applied {(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24)   return $"Last applied {(int)span.TotalHours}h ago";
        if (span.TotalDays < 1.5)   return "Last applied yesterday";
        if (span.TotalDays < 7)     return $"Last applied {(int)span.TotalDays}d ago";
        return $"Last applied {la.Value.ToLocalTime():MMM dd}";
    }

    private static readonly string[] RomanThousands = { "", "M", "MM", "MMM" };
    private static readonly string[] RomanHundreds = { "", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM" };
    private static readonly string[] RomanTens     = { "", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC" };
    private static readonly string[] RomanOnes     = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
    private static string ToRoman(int n)
    {
        if (n <= 0) return "0";
        if (n > 3999) return n.ToString();
        return RomanThousands[n / 1000]
             + RomanHundreds[(n % 1000) / 100]
             + RomanTens[(n % 100) / 10]
             + RomanOnes[n % 10];
    }

    // ═══════════════ SORTING & FILTERING ═══════════════

    private List<CharacterDesign> GetFilteredSortedDesigns(Character character)
    {
        var list = character.Designs?.ToList() ?? new List<CharacterDesign>();

        int sortIndex = localSortOverride >= 0 ? localSortOverride : plugin.Configuration.CurrentDesignSortIndex;

        switch (sortIndex)
        {
            case 0: // Favourites
                list.Sort((a, b) =>
                {
                    int fav = b.IsFavorite.CompareTo(a.IsFavorite);
                    return fav != 0 ? fav : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
                break;
            case 1: // Alphabetical
                list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                break;
            case 2: // Newest
                list.Sort((a, b) => b.DateAdded.CompareTo(a.DateAdded));
                break;
            case 3: // Oldest
                list.Sort((a, b) => a.DateAdded.CompareTo(b.DateAdded));
                break;
            case 4: // Manual
                list.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
                break;
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var q = searchQuery.Trim().ToLowerInvariant();
            list = list.Where(d =>
                (d.Name?.ToLowerInvariant().Contains(q) == true) ||
                (d.Tag?.ToLowerInvariant().Contains(q) == true) ||
                (d.DesignTags?.Any(t => t.ToLowerInvariant().Contains(q)) == true)
            ).ToList();
        }

        return list;
    }
}

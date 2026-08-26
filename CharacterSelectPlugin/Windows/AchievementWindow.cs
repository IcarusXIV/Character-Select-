using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using CharacterSelectPlugin.Achievements;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin.Windows;

public class AchievementWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private int selectedCategoryIndex = 0;
    private int selectedTab = 0; // 0 = Achievements, 1 = Shop
    private string searchQuery = "";
    private bool searchFocused = false; // tracks last-frame focus for pill border glow
    private int sortMode = 0; // 0 = Progress, 1 = Points, 2 = Recent, 3 = Name
    private int rewardFilter = 0; // 0 = All, 1 = Completed, 2 = Uncompleted
    private bool pendingClose = false;
    private int currentPage = 0;
    private const int ItemsPerPage = 8;
    private const float CardMargin = 6f; // side margin so cards aren't edge-to-edge

    // Default true so the natural sorts (highest progress, highest points, most recent) come first.
    // Reset to ascending automatically when the user picks "Name" since A→Z is the conventional default.
    private bool sortDescending = true;

    private static readonly string[] SortLabels = { "Progress", "Points", "Recent", "Name" };
    private static readonly string[] RewardLabels = { "All", "Completed", "Uncompleted", "Core only", "Bonus only" };

    // Colour palette
    private static readonly Vector4 WinBg     = new(0.055f, 0.063f, 0.078f, 1f);
    private static Vector4 Gold => Boutique.Gold;
    private static readonly Vector4 GreenOk   = new(0.25f, 0.95f, 0.45f, 1f);
    private static readonly Vector4 TxBright  = new(0.97f, 0.97f, 1.00f, 1f);
    private static readonly Vector4 TxMid     = new(0.68f, 0.68f, 0.76f, 0.90f);
    private static readonly Vector4 TxDim     = new(0.42f, 0.42f, 0.52f, 0.65f);
    private static readonly Vector4 CardBg    = new(0.07f, 0.08f, 0.14f, 0.96f);
    private static readonly Vector4 BarTrack  = new(0.10f, 0.10f, 0.16f, 1f);
    private static readonly Vector4 InputBg   = new(0.06f, 0.06f, 0.12f, 0.90f);

    private static Vector4 A(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    // Category tabs
    private static readonly (string Label, AchievementCategory? Cat, string Icon, Vector4 Color)[] CatTabs =
    {
        ("All",           null,                               "\uf0ac", new Vector4(0.40f, 0.72f, 1.00f, 1f)),
        ("Characters",    AchievementCategory.Characters,     "\uf007", new Vector4(0.35f, 0.78f, 1.00f, 1f)),
        ("Designs",       AchievementCategory.Designs,        "\uf553", new Vector4(1.00f, 0.45f, 0.82f, 1f)),
        ("Profiles",      AchievementCategory.Profiles,       "\uf2c2", new Vector4(0.30f, 0.95f, 0.50f, 1f)),
        ("Switching",     AchievementCategory.Switching,      "\uf0ec", new Vector4(1.00f, 0.72f, 0.25f, 1f)),
        ("Automation",    AchievementCategory.Automation,     "\uf085", new Vector4(0.64f, 0.56f, 1.00f, 1f)),
        ("Social",        AchievementCategory.Social,         "\uf0c0", new Vector4(0.25f, 0.90f, 1.00f, 1f)),
        ("Customization", AchievementCategory.Customization,  "\uf53f", new Vector4(1.00f, 0.58f, 0.30f, 1f)),
        ("Discovery",     AchievementCategory.Discovery,      "\uf002", new Vector4(0.68f, 1.00f, 0.38f, 1f)),
    };

    // Full category metadata for card styling
    private static readonly (AchievementCategory Cat, string Icon, Vector4 Color)[] AllCatMeta =
    {
        (AchievementCategory.Characters,    "\uf007", new Vector4(0.35f, 0.78f, 1.00f, 1f)),
        (AchievementCategory.Designs,       "\uf553", new Vector4(1.00f, 0.45f, 0.82f, 1f)),
        (AchievementCategory.Profiles,      "\uf2c2", new Vector4(0.30f, 0.95f, 0.50f, 1f)),
        (AchievementCategory.Switching,     "\uf0ec", new Vector4(1.00f, 0.72f, 0.25f, 1f)),
        (AchievementCategory.Automation,    "\uf085", new Vector4(0.64f, 0.56f, 1.00f, 1f)),
        (AchievementCategory.Social,        "\uf0c0", new Vector4(0.25f, 0.90f, 1.00f, 1f)),
        (AchievementCategory.Customization, "\uf53f", new Vector4(1.00f, 0.58f, 0.30f, 1f)),
        (AchievementCategory.Discovery,     "\uf002", new Vector4(0.68f, 1.00f, 0.38f, 1f)),
    };

    private static readonly Dictionary<string, int> MilestoneTargets = new()
    {
        { "char_1", 1 }, { "char_5", 5 }, { "char_10", 10 }, { "char_25", 25 },
        { "char_41", 41 }, { "char_50", 50 }, { "char_100", 100 },
        { "design_1", 1 }, { "design_10", 10 }, { "design_25", 25 }, { "design_50", 50 }, { "design_100", 100 },
        { "social_likes_1", 1 }, { "social_likes_10", 10 }, { "social_likes_50", 50 },
    };

    // Milestone chains - ordered tiers so we can show progress toward the NEXT one
    private static readonly string[][] MilestoneChains =
    {
        new[] { "char_1", "char_5", "char_10", "char_25", "char_41", "char_50", "char_100" },
        new[] { "design_1", "design_10", "design_25", "design_50", "design_100" },
        new[] { "social_likes_1", "social_likes_10", "social_likes_50" },
    };

    // Just-unlocked celebration state
    private string? celebId = null;
    private float celebStart = -1f;

    // Hero celebration cinematic
    // The card NEVER scales. Hero motion + framing FX cascade carry the impact.
    //   0   → 400  ms  AWAKEN   : card sits in slot, backdrop dim fades in
    //   400 → 1100 ms  ASCENT   : arc rise from slot to viewport centre (700ms)
    //   1100→ 2200 ms  HOLD     : held at centre, FX cascade plays
    //   2200→ 3000 ms  DESCENT  : ease-in drop to slot 0 + slide-over + shockwave
    private const float CelebDur        = 3.0f;
    private const float CelebAwakenEnd  = 0.4f;   // 13% - ASCENT begins
    private const float CelebAscentEnd  = 1.1f;   // 37% - HOLD begins
    private const float CelebHoldEnd    = 2.2f;   // 73% - DESCENT begins
    // Snapshot of the hero's slot K position at celebration start. Captured
    // lazily on the first render frame from tileLastFramePos so we have the
    // pre-unlock screen position to RISE from.
    private Vector2 celebOriginPos = Vector2.Zero;
    private bool celebOriginCaptured = false;
    // Refreshed each frame inside DrawAchievementContent - viewport bounds
    // for the backdrop dim, centre target for HOLD, slot 0 target for LAND.
    private Vector2 celebViewportMin = Vector2.Zero;
    private Vector2 celebViewportMax = Vector2.Zero;
    private Vector2 celebSlot0Pos    = Vector2.Zero;
    // Base regular-grid card dimensions (set when the FLIP path computes
    // them) - needed by the cinematic block so it can centre the scaled hero.
    private float celebGridCardW = 0f;
    private float celebGridCardH = 0f;
    // Test-trigger flag set by /select testunlock. Picked up at the top of
    // Draw on the next frame so we can call ImGui.GetTime safely.
    private bool celebTestRequested = false;
    // Slide-over snapshot - positions of all currently-visible tiles at the
    // moment the celebration triggered. During LAND, other tiles lerp from
    // their snapshot position to their post-unlock natural position so they
    // visibly "make room" for the hero arriving at slot 0 (1:1 with HTML
    // .shift-right / .shift-wrap behaviour).
    private Dictionary<string, Vector2> celebOtherTilesOldPos = new();

    // ── Hover sheen system ──────────────────────────────────────────────
    // Tracks per-element hover sweeps. When an element starts being hovered, we record
    // the start time; the sweep plays for HoverSweepDuration and then stops drawing.
    // On un-hover the entry is removed so the next hover-enter starts a fresh sweep.
    private readonly Dictionary<string, DateTime> hoverSweepStarts = new();
    private const float HoverSweepDuration = 0.65f;

    /// <summary>
    /// Updates the per-element hover sweep state and returns the current sweep progress.
    /// Returns 0..1 while a sweep is active for this element, or -1 if nothing should draw.
    /// </summary>
    private float UpdateAndGetHoverSweepProgress(string id, bool isHovered)
    {
        if (!isHovered)
        {
            hoverSweepStarts.Remove(id);
            return -1f;
        }
        if (!hoverSweepStarts.ContainsKey(id))
            hoverSweepStarts[id] = DateTime.UtcNow;

        float elapsed = (float)(DateTime.UtcNow - hoverSweepStarts[id]).TotalSeconds;
        if (elapsed >= HoverSweepDuration) return -1f;
        return elapsed / HoverSweepDuration;
    }

    /// <summary>
    /// Draws a glossy left-to-right sheen sweep across the (mn, mx) rect, same shape as
    /// the CSAchievementToast spawn sheen but driven by hover progress instead of spawn time.
    /// Two halves of AddRectFilledMultiColor build the transparent → bright → transparent
    /// gradient. Clipped to the rect so the off-screen halves don't render.
    /// </summary>
    private static void DrawHoverSheen(ImDrawListPtr dl, Vector2 mn, Vector2 mx, float progress, float maxAlpha = 0.20f)
    {
        if (progress < 0f || progress > 1f) return;

        float w = mx.X - mn.X;
        // Linear sweep from off-left (-w) to off-right (+w)
        float bandLeftX  = mn.X - w + progress * (2f * w);
        float bandRightX = bandLeftX + w;
        float bandMidX   = (bandLeftX + bandRightX) * 0.5f;

        dl.PushClipRect(mn, mx, true);

        uint transparentU = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0f));
        uint brightU      = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, maxAlpha));

        // Left half: transparent on the left, bright at the band centre
        dl.AddRectFilledMultiColor(
            new Vector2(bandLeftX, mn.Y),
            new Vector2(bandMidX,  mx.Y),
            transparentU, brightU,
            brightU,      transparentU);

        // Right half: bright at the band centre, transparent on the right
        dl.AddRectFilledMultiColor(
            new Vector2(bandMidX,   mn.Y),
            new Vector2(bandRightX, mx.Y),
            brightU,      transparentU,
            transparentU, brightU);

        dl.PopClipRect();
    }

    public AchievementWindow(Plugin plugin) : base("Achievements###CSPlusAchievements",
        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking |
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        // Codex mockup note: "no custom title bar. Dalamud owns the window
        // header (title + close button); plugin content begins with the meta
        // ribbon below." Removed NoTitleBar so Dalamud's chrome shows.
        this.plugin = plugin;
        Size = new Vector2(1024, 680);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(900, 560), MaximumSize = new Vector2(1600, 1200) };
    }

    public void Dispose() { }

    private int _chromeColorCount = 0;
    public override void PreDraw()
    {
        _chromeColorCount = ThemeHelper.PushWindowChromeColors(plugin.Configuration);
    }

    public override void PostDraw()
    {
        ThemeHelper.PopWindowChromeColors(_chromeColorCount);
        _chromeColorCount = 0;
    }

    public override void Draw()
    {
        if (pendingClose)
        {
            pendingClose = false;
            IsOpen = false;
            return;
        }

        int themeColorCount = ThemeHelper.PushThemeColors(plugin.Configuration);
        int themeStyleVarCount = ThemeHelper.PushThemeStyleVars(plugin.Configuration.UIScaleMultiplier);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, WinBg);

        // Encore chassis pattern: zero outer padding so the meta ribbon, hero
        // stats band, and subbar can bleed edge-to-edge with the window border.
        // Inner scroll regions re-push their own horizontal gutter (Phase 3).
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 0f));

        try
        {
            var s = Math.Clamp(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier, 0.5f, 3f);
            var data = plugin.Configuration.AchievementData;

            // Test-trigger pickup. Set by /select testunlock - picked up here
            // so ImGui.GetTime is always called inside a frame.
            if (celebTestRequested)
            {
                celebTestRequested = false;
                if (celebId == null)
                {
                    string? testId;
                    if (!string.IsNullOrEmpty(celebTestRequestedId)
                        && AchievementRegistry.All.Any(a => a.Id == celebTestRequestedId))
                    {
                        testId = celebTestRequestedId;
                    }
                    else
                    {
                        testId = data.UnlockedAchievements.Keys.FirstOrDefault()
                              ?? AchievementRegistry.All.FirstOrDefault()?.Id;
                    }
                    celebTestRequestedId = null;
                    if (testId != null)
                    {
                        celebId = testId;
                        celebStart = (float)ImGui.GetTime();
                        data.CelebratedAchievements.Add(testId);
                        plugin.SaveConfiguration();
                        // Snapshot pre-unlock positions for slide-over AND
                        // pin the hero's origin BEFORE this frame's render
                        // overwrites tileLastFramePos with post-sort slots.
                        celebOtherTilesOldPos = new Dictionary<string, Vector2>(tileLastFramePos);
                        if (tileLastFramePos.TryGetValue(testId, out var origP))
                        {
                            celebOriginPos = origP;
                            celebOriginCaptured = true;
                        }
                        else
                        {
                            celebOriginCaptured = false;
                        }
                    }
                }
            }

            // Multi-test queue (/select testunlock N) - one per free celebId slot.
            // Each pop fires a test celebration with the same fakery flag the single-
            // test path uses so the slide-over reads correctly even though no real
            // unlock happened.
            if (celebId == null && celebTestQueue.Count > 0)
            {
                var qid = celebTestQueue.Dequeue();
                celebId = qid;
                celebStart = (float)ImGui.GetTime();
                data.CelebratedAchievements.Add(qid);
                plugin.SaveConfiguration();
                celebOtherTilesOldPos = new Dictionary<string, Vector2>(tileLastFramePos);
                if (tileLastFramePos.TryGetValue(qid, out var origP))
                {
                    celebOriginPos = origP;
                    celebOriginCaptured = true;
                }
                else
                {
                    celebOriginCaptured = false;
                }
            }

            // Legacy/upgrade seed: on the first launch after this persistence change shipped,
            // mark every currently-unlocked achievement as already-celebrated so existing
            // users don't get flooded with celebrations for everything they unlocked before
            // the feature existed. Persisted on AchievementData so this only runs once ever.
            if (!data.HasInitializedCelebrations)
            {
                foreach (var id in data.UnlockedAchievements.Keys)
                    data.CelebratedAchievements.Add(id);
                data.HasInitializedCelebrations = true;
                plugin.SaveConfiguration();
            }
            else if (celebId == null)
            {
                // Pick up any unlocks not yet celebrated (including ones unlocked while
                // CS+ wasn't running). One per frame; the previous celebration's expiry
                // below frees celebId so the next missed unlock triggers on the next frame,
                // which gives natural staggering across multiple missed unlocks.
                foreach (var id in data.UnlockedAchievements.Keys)
                {
                    if (!data.CelebratedAchievements.Contains(id))
                    {
                        celebId = id;
                        celebStart = (float)ImGui.GetTime();
                        data.CelebratedAchievements.Add(id);
                        plugin.SaveConfiguration();
                        // Snapshot pre-unlock positions for slide-over AND
                        // pin the hero's origin BEFORE this frame's render
                        // overwrites tileLastFramePos with the post-unlock
                        // slot 0 position. Without this, the hero appears to
                        // teleport to slot 0 before the ASCENT begins.
                        celebOtherTilesOldPos = new Dictionary<string, Vector2>(tileLastFramePos);
                        if (tileLastFramePos.TryGetValue(id, out var origP))
                        {
                            celebOriginPos = origP;
                            celebOriginCaptured = true;
                        }
                        else
                        {
                            celebOriginCaptured = false;
                        }
                        break;
                    }
                }
            }
            // Expire the celebration once its full timeline has run
            if (celebId != null && ImGui.GetTime() - celebStart > CelebDur)
            {
                celebId = null;
                celebStart = -1f;
            }

            foreach (var id in data.UnlockedAchievements.Keys)
                data.SeenAchievements.Add(id);

            // ═══ CHROME ═══
            DrawMetaRibbon(data, s);
            DrawHeroStatsBand(data, s);
            DrawSubbar(s);

            // Body: category pills + content. Re-enables inner ItemSpacing
            // so the per-element layout math inside DrawCategoryRow /
            // DrawAchievementContent behaves correctly.
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f * s, 4f * s));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f * s, 3f * s));

            // Track tab switches so animations fire on tab-enter:
            //   Shop tab → Shop ring fill (counts up from 0 to ratio)
            //   Achievements tab → re-trigger card shuffle slide-in
            if (selectedTab != lastTabIndex)
            {
                float now = (float)ImGui.GetTime();
                if (selectedTab == 1)
                    shopFirstShownTime = now;
                else if (selectedTab == 0)
                    categoryChangeTime = now;
                // Glitch flash on the subbar tab underline
                subbarChangeTime = now;
                lastTabIndex = selectedTab;
            }

            // Category filters only make sense for the Achievements tab.
            if (selectedTab == 0)
            {
                DrawCategoryRow(s);
                DrawAchievementContent(data, s);
            }
            else
            {
                DrawShop(s);
            }

            ImGui.PopStyleVar(2);

            // Window corner brackets - drawn LAST so they sit on top of all
            // content. 16×16 gold L-shapes at bottom-left and bottom-right,
            // 6px inset, 1px stroke at 40% alpha.
            DrawWindowCornerBrackets(s);
        }
        finally
        {
            ImGui.PopStyleVar(2); // outer WindowPadding + ItemSpacing
            ImGui.PopStyleColor();
            ThemeHelper.PopThemeStyleVars(themeStyleVarCount);
            ThemeHelper.PopThemeColors(themeColorCount);
        }
    }

    private void DrawWindowCornerBrackets(float s)
    {
        var wPos = ImGui.GetWindowPos();
        var wSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        float bSize = 16f * s;
        float bInset = 6f * s;
        uint bCol = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0.40f));

        // Bottom-left: L opens up-right
        var bl = new Vector2(wPos.X + bInset, wPos.Y + wSize.Y - bInset);
        dl.AddLine(new Vector2(bl.X, bl.Y - bSize), bl, bCol, 1f);
        dl.AddLine(bl, new Vector2(bl.X + bSize, bl.Y), bCol, 1f);

        // Bottom-right: L opens up-left
        var br = new Vector2(wPos.X + wSize.X - bInset, wPos.Y + wSize.Y - bInset);
        dl.AddLine(new Vector2(br.X, br.Y - bSize), br, bCol, 1f);
        dl.AddLine(br, new Vector2(br.X - bSize, br.Y), bCol, 1f);
    }

    // ═══════════════ META RIBBON (30px) ═══════════════
    // HTML .ribbon - trophy pip (pulsing core + expanding rings), tracked-caps
    // meta text, right-aligned "tracking" state tag. Gold hairlines top and
    // bottom (bright at edges/fading mid ↔ transparent at edges/solid mid).
    private void DrawMetaRibbon(AchievementData data, float s)
    {
        var dl = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();
        float availW = ImGui.GetContentRegionAvail().X;
        // HTML 30px × 1.3 scale (achUi). Ribbon keeps chrome proportional
        // when ribbon-meta font bumps up to OswaldMed13.
        float ribbonH = 38f * s;
        var mn = cursor;
        var mx = new Vector2(cursor.X + availW, cursor.Y + ribbonH);

        // Bg: vertical gradient ribbon-top → ribbon-bot
        uint bgTop = ImGui.ColorConvertFloat4ToU32(Boutique.RibbonTop);
        uint bgBot = ImGui.ColorConvertFloat4ToU32(Boutique.RibbonBot);
        dl.AddRectFilledMultiColor(mn, mx, bgTop, bgTop, bgBot, bgBot);

        // Top hairline: gold solid at edges, fading to transparent 42-58%.
        uint goldStrong = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0.50f));
        uint goldClear = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0f));
        float ruleH = 1f * s;
        dl.AddRectFilledMultiColor(
            mn, new Vector2(mn.X + availW * 0.42f, mn.Y + ruleH),
            goldStrong, goldClear, goldClear, goldStrong);
        dl.AddRectFilledMultiColor(
            new Vector2(mn.X + availW * 0.58f, mn.Y),
            new Vector2(mx.X, mn.Y + ruleH),
            goldClear, goldStrong, goldStrong, goldClear);

        // Bottom hairline: opposite - transparent at edges, gold mid (25-75%).
        uint goldMid = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0.26f));
        dl.AddRectFilledMultiColor(
            new Vector2(mn.X, mx.Y - ruleH),
            new Vector2(mn.X + availW * 0.5f, mx.Y),
            goldClear, goldMid, goldMid, goldClear);
        dl.AddRectFilledMultiColor(
            new Vector2(mn.X + availW * 0.5f, mx.Y - ruleH),
            mx,
            goldMid, goldClear, goldClear, goldMid);

        float centreY = (mn.Y + mx.Y) * 0.5f;
        float padX = 14f * s;
        float cursorX = mn.X + padX;

        // Trophy pip: pulsing core + two expanding rings on a 1.8s loop
        {
            float pipSize = 18f * s;
            var pipCentre = new Vector2(cursorX + pipSize * 0.5f, centreY);
            float t = (float)Boutique.AnimTime(ImGui.GetTime());

            // Core - 5×5 gold-warm SQUARE (not a circle - HTML says
            // `background: var(--gold-warm);` with explicit width/height 5px,
            // no border-radius). Pulsing scale 1.0 ↔ 1.2 on 1.8s ease-in-out.
            float coreCycle = (t % 1.8f) / 1.8f;
            float corePulse = 0.5f + 0.5f * MathF.Sin(coreCycle * MathF.Tau - MathF.PI * 0.5f);
            float coreHalf = 2.5f * s * (1f + 0.2f * corePulse);
            uint coreColU = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(
                Boutique.GoldWarm, 0.85f + 0.15f * corePulse));
            dl.AddRectFilled(
                pipCentre - new Vector2(coreHalf, coreHalf),
                pipCentre + new Vector2(coreHalf, coreHalf),
                coreColU);

            // Expanding SQUARE rings - 5px → 18px over 1.8s, opacity 0.85 → 0
            void Ring(float phaseOff)
            {
                float p = ((t + phaseOff) % 1.8f) / 1.8f;
                float half = 2.5f * s + p * (9f * s - 2.5f * s);
                float ra = (1f - p) * 0.85f;
                if (ra > 0.02f)
                    dl.AddRect(
                        pipCentre - new Vector2(half, half),
                        pipCentre + new Vector2(half, half),
                        ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, ra)),
                        0f, ImDrawFlags.None, 1f);
            }
            Ring(0f);
            Ring(0.9f);

            cursorX += pipSize + 12f * s;
        }

        // ── Meta text: "ACHIEVEMENTS · 47 of 62 · 3,240 POINTS" ──
        // Tracked-caps. Uses default font at native size for crispness.
        int uCore = data.UnlockedCoreCount;
        int tCore = AchievementRegistry.CoreAchievements.Count();
        int p = data.TotalPointsEarned;
        var dim = ImGui.ColorConvertFloat4ToU32(Boutique.TextDim);
        var bright = ImGui.ColorConvertFloat4ToU32(Boutique.Text);
        var gold = ImGui.ColorConvertFloat4ToU32(Boutique.Gold);
        var faint = ImGui.ColorConvertFloat4ToU32(Boutique.TextFaint);

        // HTML .ribbon-meta: 11px Oswald 500, letter-spacing 0.20em, uppercase.
        // Bumped to OswaldMed13 (Medium weight static face) so the 500 weight
        // actually renders bold - the variable font was defaulting to 400.
        using (Plugin.Instance?.OswaldMed13?.Push())
        {
            float fH = ImGui.GetFontSize();
            float textY = centreY - fH * 0.5f;
            float trk20 = fH * 0.20f; // 0.20em tracking

            // "ACHIEVEMENTS"
            cursorX += Boutique.DrawTrackedText(dl, new Vector2(cursorX, textY),
                "ACHIEVEMENTS", bright, trk20) + 10f * s;
            dl.AddText(new Vector2(cursorX, textY), faint, "·");
            cursorX += ImGui.CalcTextSize("·").X + 10f * s;

            // Counts: bold gold numbers + tracked caps separators
            string uStr = uCore.ToString();
            dl.AddText(new Vector2(cursorX, textY), gold, uStr);
            cursorX += ImGui.CalcTextSize(uStr).X + 6f * s;
            cursorX += Boutique.DrawTrackedText(dl, new Vector2(cursorX, textY),
                "OF", dim, trk20 * 0.8f) + 6f * s;
            string tStr = tCore.ToString();
            dl.AddText(new Vector2(cursorX, textY), gold, tStr);
            cursorX += ImGui.CalcTextSize(tStr).X + 10f * s;
            dl.AddText(new Vector2(cursorX, textY), faint, "·");
            cursorX += ImGui.CalcTextSize("·").X + 10f * s;
            string pStr = p.ToString("N0");
            dl.AddText(new Vector2(cursorX, textY), gold, pStr);
            cursorX += ImGui.CalcTextSize(pStr).X + 6f * s;
            Boutique.DrawTrackedText(dl, new Vector2(cursorX, textY),
                "POINTS", dim, trk20 * 0.8f);
        }

        // ── Right-aligned state tag: "TRACKING" ──
        // HTML .state-tag: 10px Oswald 600, tracked 0.18em. Gold border,
        // gold@6% bg, pulsing 5×5 dot (circular per border-radius: 50%).
        // OswaldSemi11 = SemiBold (600) static face, matches the HTML weight.
        using (Plugin.Instance?.OswaldSemi11?.Push())
        {
            string tag = "TRACKING";
            float tagFH = ImGui.GetFontSize();
            float trkTag = tagFH * 0.18f;
            float tagW = Boutique.MeasureTrackedText(tag, trkTag);
            var tagSize = new Vector2(tagW, tagFH);
            float tagPadX = 9f * s;
            float dotSize = 5f * s;
            float dotGap = 6f * s;
            float tagBoxW = tagPadX * 2f + dotSize + dotGap + tagSize.X;
            float tagBoxH = tagSize.Y + 7f * s;
            float tagRightPad = 14f * s;
            var tagMax = new Vector2(mx.X - tagRightPad, centreY + tagBoxH * 0.5f);
            var tagMin = new Vector2(tagMax.X - tagBoxW, centreY - tagBoxH * 0.5f);

            dl.AddRectFilled(tagMin, tagMax,
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0.06f)));
            dl.AddRect(tagMin, tagMax,
                ImGui.ColorConvertFloat4ToU32(Boutique.GoldDeep), 0f, ImDrawFlags.None, 1f);

            // Pulsing dot (scale + alpha, 1.4s ease-in-out)
            float t = (float)Boutique.AnimTime(ImGui.GetTime());
            float dotCycle = (t % 1.4f) / 1.4f;
            float dotPulse = 0.5f + 0.5f * MathF.Sin(dotCycle * MathF.Tau - MathF.PI * 0.5f);
            float dotA = 0.5f + 0.5f * dotPulse;
            float dotScale = 1f + 0.4f * dotPulse;
            var dotC = new Vector2(tagMin.X + tagPadX + dotSize * 0.5f, centreY);
            dl.AddCircleFilled(dotC, (dotSize * 0.5f) * dotScale,
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, dotA)), 16);
            dl.AddCircleFilled(dotC, (dotSize * 0.5f + 1.5f * s) * dotScale,
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0.25f * dotA)), 18);

            // Label - tracked caps
            Boutique.DrawTrackedText(dl,
                new Vector2(dotC.X + dotSize * 0.5f + dotGap, centreY - tagSize.Y * 0.5f),
                tag, gold, trkTag);
        }

        ImGui.Dummy(new Vector2(availW, ribbonH));
    }

    // ═══════════════ STATS BAR ═══════════════

    private void DrawStatsBar(AchievementData data, float s)
    {
        int uCore = data.UnlockedCoreCount;
        int tCore = AchievementRegistry.CoreAchievements.Count();
        int uBonus = data.UnlockedBonusCount;
        int tBonus = AchievementRegistry.BonusAchievements.Count();
        int p = data.TotalPointsEarned, mp = AchievementRegistry.TotalPoints;

        // Trophy + CORE unlocked count (the primary number - only counts non-bonus achievements)
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(A(Gold, 0.75f), "\uf091");
        ImGui.PopFont();
        ImGui.SameLine(0, 6 * s);
        ImGui.TextColored(TxBright, $"{uCore} / {tCore}");
        ImGui.SameLine(0, 6 * s);
        ImGui.TextColored(TxDim, "Unlocked");

        // Bonus secondary stat: smaller, dimmer, sits right next to the core count
        if (tBonus > 0)
        {
            ImGui.SameLine(0, 14 * s);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(A(new Vector4(1.00f, 0.55f, 0.85f, 1f), 0.75f), "\uf005"); // star
            ImGui.PopFont();
            ImGui.SameLine(0, 5 * s);
            ImGui.TextColored(A(TxBright, 0.85f), $"{uBonus} / {tBonus}");
            ImGui.SameLine(0, 5 * s);
            ImGui.TextColored(TxDim, "Bonus");
            if (ImGui.IsItemHovered() || ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Bonus achievements are extra/optional.\nThey award points but don't count toward 100%.");
        }

        // Points: plain text, no star icon
        ImGui.SameLine(0, 22 * s);
        ImGui.TextColored(Gold, $"{p}");
        ImGui.SameLine(0, 4 * s);
        ImGui.TextColored(TxDim, $"/ {mp} pts");

        // Filter + Sort By: right-aligned, both combos on the stats row
        float rightEdge = ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X;
        float comboW = 105 * s;
        float filterComboW = 90 * s;
        float sortLabelW = ImGui.CalcTextSize("Sort:").X;
        float filterLabelW = ImGui.CalcTextSize("Show:").X;
        float comboGap = 10 * s;
        float dirBtnW = ImGui.GetFrameHeight(); // square icon button matching combo height
        float dirBtnGap = 4 * s;
        float totalW = filterLabelW + 6 * s + filterComboW + comboGap + sortLabelW + 6 * s + comboW + dirBtnGap + dirBtnW;

        ImGui.SameLine(rightEdge - totalW);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(TxDim, "Show:");
        ImGui.SameLine(0, 6 * s);

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8 * s, 3 * s));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, InputBg);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.10f, 0.10f, 0.20f, 0.90f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.06f, 0.06f, 0.12f, 0.97f));
        ImGui.PushStyleColor(ImGuiCol.Text, TxBright);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.12f, 0.12f, 0.22f, 0.80f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.18f, 0.18f, 0.30f, 0.90f));
        ImGui.SetNextItemWidth(filterComboW);
        if (ImGui.BeginCombo("##achReward", RewardLabels[rewardFilter]))
        {
            for (int i = 0; i < RewardLabels.Length; i++)
            {
                if (ImGui.Selectable(RewardLabels[i], rewardFilter == i))
                {
                    rewardFilter = i;
                    currentPage = 0;
                }
                if (rewardFilter == i) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine(0, comboGap);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(TxDim, "Sort:");
        ImGui.SameLine(0, 6 * s);
        ImGui.SetNextItemWidth(comboW);
        if (ImGui.BeginCombo("##achSort", SortLabels[sortMode]))
        {
            for (int i = 0; i < SortLabels.Length; i++)
            {
                if (ImGui.Selectable(SortLabels[i], sortMode == i))
                {
                    if (sortMode != i)
                    {
                        sortMode = i;
                        // Reset direction to the natural default for this sort mode.
                        // Name → ascending (A→Z), everything else → descending (highest first).
                        sortDescending = (i != 3);
                    }
                }
                if (sortMode == i) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        // Direction toggle: small icon button next to the sort combo.
        // InvisibleButton + manual draw so we can geometrically centre the arrow icon.
        // ImGui.Button auto-alignment uses font baseline math which doesn't land right
        // for FontAwesome glyphs (same root cause as the achievement icon centring fix).
        ImGui.SameLine(0, dirBtnGap);
        // \uf063 = arrow-down (descending), \uf062 = arrow-up (ascending)
        string dirIcon = sortDescending ? "\uf063" : "\uf062";
        var dirBtnPos = ImGui.GetCursorScreenPos();
        var dirBtnSize = new Vector2(dirBtnW, dirBtnW);
        if (ImGui.InvisibleButton("##sortDir", dirBtnSize))
            sortDescending = !sortDescending;
        bool dirHovered = ImGui.IsItemHovered();

        var dirDl = ImGui.GetWindowDrawList();
        var dirBgCol = dirHovered
            ? new Vector4(0.18f, 0.18f, 0.30f, 0.90f)
            : new Vector4(0.12f, 0.12f, 0.22f, 0.80f);
        dirDl.AddRectFilled(dirBtnPos, dirBtnPos + dirBtnSize,
            ImGui.ColorConvertFloat4ToU32(dirBgCol), 4f);

        // Centred arrow glyph: simple cell centring like MainWindow IconButton
        ImGui.PushFont(UiBuilder.IconFont);
        var dirIconSz = ImGui.CalcTextSize(dirIcon);
        var dirIconPos = dirBtnPos + (dirBtnSize - dirIconSz) * 0.5f;
        dirDl.AddText(dirIconPos,
            ImGui.ColorConvertFloat4ToU32(TxBright), dirIcon);
        ImGui.PopFont();

        if (dirHovered)
            CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(sortDescending ? "Sort descending (highest → lowest)\nClick to flip" : "Sort ascending (lowest → highest)\nClick to flip");

        ImGui.PopStyleColor(6);
        ImGui.PopStyleVar(2);

        // Progress bar - uses CORE progress only, since the bonus tier is extra and
        // the bar represents "are you on track for 100%?". Mixing bonus in here would
        // make it impossible to ever fill without grinding the high-count milestones.
        float prog = tCore > 0 ? (float)uCore / tCore : 0;
        var pos = ImGui.GetCursorScreenPos();
        float w = ImGui.GetContentRegionAvail().X;
        float h = 3 * s;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(w, h), ImGui.ColorConvertFloat4ToU32(BarTrack), 2f);
        if (prog > 0)
            dl.AddRectFilled(pos, pos + new Vector2(w * prog, h), ImGui.ColorConvertFloat4ToU32(A(GreenOk, 0.85f)), 2f);

        // Firework spark at the leading edge. Intensity ramps with prog so the bar
        // builds excitement as the user approaches 100%. Skipped at empty/full endpoints.
        if (prog > 0.01f && prog < 0.99f)
        {
            var sparkPos = new Vector2(pos.X + w * prog, pos.Y + h * 0.5f);
            DrawProgressSpark(dl, sparkPos, GreenOk, s, large: true, intensity: prog);
        }

        ImGui.Dummy(new Vector2(0, h + 4 * s));

        ImGui.TextColored(A(TxDim, 0.45f), "Keep exploring to unlock more rewards.");
    }

    // ═══════════════ HERO STATS BAND ═══════════════
    // HTML .hero-stats - three stat columns (Core / Bonus / Points) with
    // vertical dividers, right-aligned Show/Sort/Direction Codex pills, and a
    // 3px progress bar with firework head. Caption row: hint left, pct right.
    // Bg: gold radial glow at 20% 100% + vertical dark gradient.
    private void DrawHeroStatsBand(AchievementData data, float s)
    {
        int uCore  = data.UnlockedCoreCount;
        int tCore  = AchievementRegistry.CoreAchievements.Count();
        int uBonus = data.UnlockedBonusCount;
        int tBonus = AchievementRegistry.BonusAchievements.Count();
        int p      = data.TotalPointsEarned;
        int mp     = AchievementRegistry.TotalPoints;

        // GLITCH FLAIR - detect counter changes since last frame so the next
        // render can draw chromatic-split ghosts on the changed value(s).
        // Skip first-frame init (lastXValue == -1) so opening the window
        // doesn't fire all three flairs at once.
        float bandNow = (float)ImGui.GetTime();
        if (lastCoreValue   >= 0 && uCore  != lastCoreValue)   coreChangeTime   = bandNow;
        if (lastBonusValue  >= 0 && uBonus != lastBonusValue)  bonusChangeTime  = bandNow;
        if (lastPointsValue >= 0 && p      != lastPointsValue) pointsChangeTime = bandNow;
        lastCoreValue   = uCore;
        lastBonusValue  = uBonus;
        lastPointsValue = p;

        var dl = ImGui.GetWindowDrawList();
        var bandStart = ImGui.GetCursorScreenPos();
        float availW = ImGui.GetContentRegionAvail().X;
        float padX = 20f * s;
        float padTop = 16f * s;
        float padBot = 15f * s;

        // Band background: vertical dark gradient + soft gold radial
        float fontH  = ImGui.GetFontSize();
        float bandKickerH = fontH, capH = fontH;
        using (Plugin.Instance?.OswaldMed10?.Push())
            bandKickerH = ImGui.GetFontSize();
        using (Plugin.Instance?.OswaldBody10?.Push())
            capH = ImGui.GetFontSize();
        float valueH = fontH;
        using (Plugin.Instance?.StatLargeFont?.Push())
            valueH = ImGui.GetFontSize();
        float row1H  = bandKickerH + 2f * s + valueH;
        float progH  = 3f * s;
        float progTopGap = 11f * s;
        float capTopGap  = 8f * s;
        float bandH = padTop + row1H + progTopGap + progH + capTopGap + capH + padBot;
        var bandMx   = new Vector2(bandStart.X + availW, bandStart.Y + bandH);

        // Dark vertical gradient #0c0e14 → #0a0b10
        var gTop = new Vector4(0.047f, 0.055f, 0.078f, 1f);
        var gBot = new Vector4(0.039f, 0.043f, 0.063f, 1f);
        dl.AddRectFilledMultiColor(bandStart, bandMx,
            ImGui.ColorConvertFloat4ToU32(gTop),
            ImGui.ColorConvertFloat4ToU32(gTop),
            ImGui.ColorConvertFloat4ToU32(gBot),
            ImGui.ColorConvertFloat4ToU32(gBot));

        // Gold radial ELLIPSE anchored at (20% width, 100% height) - HTML spec
        // `radial-gradient(ellipse 500px 180px at 20% 100%, rgba(gold, 0.045),
        // transparent 70%)`. The prior concentric-circle approach showed as
        // visible rings AND read as a circular cloud; HTML spec is a wide,
        // flat horizontal glow. Implement as N nested ellipse polygons, each
        // adding a small uniform alpha - stack approximates linear falloff
        // from peak at centre → transparent at 70% radius.
        {
            var anchor = new Vector2(bandStart.X + availW * 0.20f, bandMx.Y);
            float rx = 500f * s * 0.7f;   // 70% stop → this is where alpha hits 0
            float ry = 180f * s * 0.7f;
            const float peakA = 0.045f;   // HTML exact
            const int layers = 28;
            const int segs = 48;
            var pts = new Vector2[segs];
            uint col = ImGui.ColorConvertFloat4ToU32(
                Boutique.WithAlpha(Boutique.Gold, peakA / layers));
            for (int i = 1; i <= layers; i++)
            {
                float u = i / (float)layers;
                float lx = rx * u;
                float ly = ry * u;
                for (int j = 0; j < segs; j++)
                {
                    float theta = (float)(j * Math.PI * 2.0 / segs);
                    pts[j] = anchor + new Vector2(
                        lx * (float)Math.Cos(theta),
                        ly * (float)Math.Sin(theta));
                }
                dl.AddConvexPolyFilled(ref pts[0], segs, col);
            }
        }

        // Bottom hairline: gold mid-bright, fading at edges (opacity 0.35)
        uint goldMid  = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0.35f));
        uint goldEdge = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0f));
        {
            var hMn = new Vector2(bandStart.X + padX, bandMx.Y - 1f * s);
            var hMx = new Vector2(bandStart.X + availW - padX, bandMx.Y);
            float hMidX = (hMn.X + hMx.X) * 0.5f;
            dl.AddRectFilledMultiColor(
                hMn, new Vector2(hMidX, hMx.Y),
                goldEdge, goldMid, goldMid, goldEdge);
            dl.AddRectFilledMultiColor(
                new Vector2(hMidX, hMn.Y), hMx,
                goldMid, goldEdge, goldEdge, goldMid);
        }

        // Colour shorthand
        var dim       = ImGui.ColorConvertFloat4ToU32(Boutique.TextDim);
        var faint     = ImGui.ColorConvertFloat4ToU32(Boutique.TextFaint);
        var ghost     = ImGui.ColorConvertFloat4ToU32(Boutique.TextGhost);
        var txt       = ImGui.ColorConvertFloat4ToU32(Boutique.Text);
        var gold      = ImGui.ColorConvertFloat4ToU32(Boutique.Gold);
        var goldWarm  = ImGui.ColorConvertFloat4ToU32(Boutique.GoldWarm);
        var goldDeep  = ImGui.ColorConvertFloat4ToU32(Boutique.GoldDeep);
        var magSoft   = ImGui.ColorConvertFloat4ToU32(Boutique.MagentaSft);

        // ── Stat column helper ──
        // Renders: kicker (small tracked caps + dot) + value (N / M with
        // distinct colours/sizes). Returns the right edge X so the next
        // column can be placed with a divider in between.
        float DrawStatColumn(float startX, float topY,
            Vector4 dotCol, string kicker, uint kickerCol,
            int unlocked, int total,
            IFontHandle? valueFont, uint valueCol,
            IFontHandle? totalFont, uint slashCol, uint totalCol,
            bool pointsGlow,
            float flairTime = -100f)
        {
            // Kicker row - use ACTUAL Oswald 10 height (not default fontH) so the
            // 4px gap to the value row is measured from the real kicker bottom.
            float kickerY = topY;
            float kickerH;
            using (Plugin.Instance?.OswaldMed10?.Push())
                kickerH = ImGui.GetFontSize();

            var dotC = new Vector2(startX + 2.5f * s, kickerY + kickerH * 0.5f);
            dl.AddRectFilled(
                dotC - new Vector2(2.5f * s, 2.5f * s),
                dotC + new Vector2(2.5f * s, 2.5f * s),
                ImGui.ColorConvertFloat4ToU32(dotCol));
            if (pointsGlow)
            {
                dl.AddRectFilled(
                    dotC - new Vector2(3.8f * s, 3.8f * s),
                    dotC + new Vector2(3.8f * s, 3.8f * s),
                    ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0.35f)));
                dl.AddRectFilled(
                    dotC - new Vector2(2.5f * s, 2.5f * s),
                    dotC + new Vector2(2.5f * s, 2.5f * s),
                    ImGui.ColorConvertFloat4ToU32(dotCol));
            }
            // HTML .stat .kicker: 9.5px Oswald 500, letter-spacing 0.28em, uppercase
            float kickerRight;
            using (Plugin.Instance?.OswaldMed10?.Push())
            {
                float trk = ImGui.GetFontSize() * 0.28f;
                float w = Boutique.DrawTrackedText(dl,
                    new Vector2(startX + 11f * s, kickerY), kicker, kickerCol, trk);
                kickerRight = startX + 11f * s + w;
            }

            // Value row - HTML .hero-row `align-items: flex-end`, so all columns
            // share a BASELINE. Bonus (22px) must sit on the same baseline as
            // Core (28px), not share the same top-Y. Baseline = top-of-value +
            // valueH × 0.78 (sans-serif ascender ratio). Using outer `valueH`
            // (core/points size) as the reference for the shared row baseline.
            // Gap 4px → 2px: HTML spec reads too loose once the value font is
            // scaled up - tighter stack makes the numbers sit right under caps.
            const float Ascender = 0.78f;
            float baselineY = kickerY + kickerH + 2f * s + valueH * Ascender;
            string uStr = unlocked.ToString();
            string slashStr = "/";
            string tStr = total.ToString();

            float uW = 0f, slashW = 0f, tW = 0f, vSize = 0f, tSize = 0f;
            float vTrk = 0f, tTrk = 0f;
            using (valueFont?.Push())
            {
                vSize = ImGui.GetFontSize();
                vTrk = vSize * 0.04f;
                uW = Boutique.MeasureTrackedText(uStr, vTrk);
                slashW = ImGui.CalcTextSize(slashStr).X;
            }
            using (totalFont?.Push())
            {
                tSize = ImGui.GetFontSize();
                tTrk = tSize * 0.04f;
                tW = Boutique.MeasureTrackedText(tStr, tTrk);
            }
            // This column's value sits so its baseline = shared row baselineY.
            float valueY = baselineY - vSize * Ascender;
            // Total sits so its baseline = value baseline (same Y).
            float totalYOff = (vSize - tSize) * Ascender;
            // HTML .slash { margin: 0 2px } - tight 2px on each side, no spaces.
            float slashPad = 2f * s;

            // Value (N)
            using (valueFont?.Push())
            {
                var valuePos = new Vector2(startX, valueY);
                if (pointsGlow)
                {
                    // HTML: text-shadow: 0 0 18px rgba(gold, 0.35). The 18px is
                    // the gaussian BLUR RADIUS - most of the glow's energy sits
                    // within 1-1.5× that radius of the glyph edges. Sizing the
                    // halo to hug the glyphs tightly (text bounds + 10px pad)
                    // avoids the "spotlight" look of a halo much larger than
                    // the text.
                    var glyphWidth = ImGui.CalcTextSize(uStr).X;
                    var anchor = new Vector2(valuePos.X + glyphWidth * 0.5f,
                                             valuePos.Y + vSize * 0.55f);
                    float rx = glyphWidth * 0.55f + 10f * s;
                    float ry = vSize       * 0.55f + 10f * s;
                    const float peakA = 0.22f;   // dial down from 0.35 HTML exact
                    const int layers = 18;
                    const int segs = 32;
                    var haloPts = new Vector2[segs];
                    uint haloCol = ImGui.ColorConvertFloat4ToU32(
                        Boutique.WithAlpha(Boutique.Gold, peakA / layers));
                    for (int i = 1; i <= layers; i++)
                    {
                        float u = i / (float)layers;
                        float lx = rx * u;
                        float ly = ry * u;
                        for (int k = 0; k < segs; k++)
                        {
                            float a = (float)(k * Math.PI * 2.0 / segs);
                            haloPts[k] = anchor + new Vector2(
                                lx * MathF.Cos(a),
                                ly * MathF.Sin(a));
                        }
                        dl.AddConvexPolyFilled(ref haloPts[0], segs, haloCol);
                    }
                }
                // GLITCH FLAIR - chromatic-split ghosts under the value
                // text when the value just changed. Fades to 0 over the
                // flair window so the resting state is the clean coloured
                // value.
                float colFlairElapsed = (float)ImGui.GetTime() - flairTime;
                if (colFlairElapsed >= 0f && colFlairElapsed < GlitchFlashWindow)
                {
                    float chromaA = 1f - (colFlairElapsed / GlitchFlashWindow);
                    float chrOff = 3f * s;
                    Boutique.DrawTrackedText(dl, valuePos + new Vector2(-chrOff, 0f), uStr,
                        ImGui.ColorConvertFloat4ToU32(
                            Boutique.WithAlpha(Boutique.GlitchMagenta, 0.85f * chromaA)),
                        vTrk);
                    Boutique.DrawTrackedText(dl, valuePos + new Vector2(chrOff, 0f), uStr,
                        ImGui.ColorConvertFloat4ToU32(
                            Boutique.WithAlpha(Boutique.GlitchCyan, 0.85f * chromaA)),
                        vTrk);
                }
                Boutique.DrawTrackedText(dl, valuePos, uStr, valueCol, vTrk);

                // "/" - HTML .slash { margin: 0 2px }, tight against both sides.
                dl.AddText(ImGui.GetFont(), vSize,
                    new Vector2(startX + uW + slashPad, valueY),
                    slashCol, slashStr);
            }
            // Total (M) at smaller size, baseline-aligned to value.
            using (totalFont?.Push())
            {
                Boutique.DrawTrackedText(dl,
                    new Vector2(startX + uW + slashPad + slashW + slashPad,
                                valueY + totalYOff),
                    tStr, totalCol, tTrk);
            }

            float valueRight = startX + uW + slashPad + slashW + slashPad + tW;
            return Math.Max(kickerRight, valueRight);
        }

        // ── Column 1: CORE ──
        float rowY = bandStart.Y + padTop;
        float col1X = bandStart.X + padX;
        float col1End = DrawStatColumn(col1X, rowY,
            Boutique.Gold, "CORE", faint,
            uCore, tCore,
            Plugin.Instance?.StatLargeFont, txt,
            Plugin.Instance?.StatMidSmallFont, ghost, dim,
            pointsGlow: false,
            flairTime: coreChangeTime);

        // Vertical divider
        var divCol = ImGui.ColorConvertFloat4ToU32(Boutique.BorderSoft);
        float divX1 = col1End + 18f * s;
        dl.AddRectFilled(
            new Vector2(divX1, rowY + 4f * s),
            new Vector2(divX1 + 1f * s, rowY + row1H - 4f * s),
            divCol);

        // ── Column 2: BONUS ──
        float col2End = divX1;
        if (tBonus > 0)
        {
            float col2X = divX1 + 18f * s;
            col2End = DrawStatColumn(col2X, rowY,
                Boutique.MagentaSft, "BONUS", ghost,
                uBonus, tBonus,
                Plugin.Instance?.StatMidFont, dim,
                Plugin.Instance?.StatSmallFont, ghost, ghost,
                pointsGlow: false,
                flairTime: bonusChangeTime);
        }

        // Vertical divider
        float divX2 = (tBonus > 0 ? col2End + 18f * s : col1End + 18f * s);
        dl.AddRectFilled(
            new Vector2(divX2, rowY + 4f * s),
            new Vector2(divX2 + 1f * s, rowY + row1H - 4f * s),
            divCol);

        // ── Column 3: POINTS ──
        float col3X = divX2 + 18f * s;
        DrawStatColumn(col3X, rowY,
            Boutique.Gold, "POINTS", faint,
            p, mp,
            Plugin.Instance?.StatLargeFont, gold,
            Plugin.Instance?.StatMidSmallFont, goldDeep, goldDeep,
            pointsGlow: true,
            flairTime: pointsChangeTime);

        // ── Right-aligned controls: Show / Sort / Dir as Codex pills ──
        float controlsY = rowY + (row1H - 34f * s) * 0.5f;
        float rightEdge = bandStart.X + availW - padX;
        DrawStatControlsPills(dl, controlsY, rightEdge, s);

        // ── Progress bar (3px) ──
        float progY = rowY + row1H + progTopGap;
        float progX = bandStart.X + padX;
        float progW = availW - padX * 2f;

        // Track
        dl.AddRectFilled(
            new Vector2(progX, progY),
            new Vector2(progX + progW, progY + progH),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f)));
        dl.AddRect(
            new Vector2(progX, progY),
            new Vector2(progX + progW, progY + progH),
            ImGui.ColorConvertFloat4ToU32(Boutique.BorderSoft), 0f, ImDrawFlags.None, 1f);

        float prog = tCore > 0 ? (float)uCore / tCore : 0f;
        if (prog > 0.001f)
        {
            float fillW = progW * prog;
            // Gradient gold-deep → gold → gold-warm
            float halfFill = Math.Min(fillW, progW * 0.55f);
            dl.AddRectFilledMultiColor(
                new Vector2(progX, progY),
                new Vector2(progX + halfFill, progY + progH),
                goldDeep, gold, gold, goldDeep);
            if (fillW > progW * 0.55f)
            {
                dl.AddRectFilledMultiColor(
                    new Vector2(progX + progW * 0.55f, progY),
                    new Vector2(progX + fillW, progY + progH),
                    gold, goldWarm, goldWarm, gold);
            }
            // Soft halo beneath
            dl.AddRectFilled(
                new Vector2(progX - 1f, progY - 1f),
                new Vector2(progX + fillW + 1f, progY + progH + 1f),
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0.22f)));

            // Firework head at leading edge
            DrawStatsFirework(dl,
                new Vector2(progX + fillW, progY + progH * 0.5f), s);
        }

        // ── Caption row ──
        // HTML .hint: Oswald 10px 400 @ 0.24em  ·  .pct: Oswald 10px 600 @ 0.18em
        float capY = progY + progH + capTopGap;
        using (Plugin.Instance?.OswaldBody10?.Push())
        {
            float capFH = ImGui.GetFontSize();
            Boutique.DrawTrackedText(dl, new Vector2(progX, capY),
                "KEEP EXPLORING TO UNLOCK MORE REWARDS", ghost, capFH * 0.24f);
        }
        using (Plugin.Instance?.OswaldSemi10?.Push())
        {
            float pctFH = ImGui.GetFontSize();
            string pctStr = $"{(int)MathF.Round(prog * 100)}% COMPLETE";
            float pctW = Boutique.MeasureTrackedText(pctStr, pctFH * 0.18f);
            Boutique.DrawTrackedText(dl,
                new Vector2(progX + progW - pctW, capY),
                pctStr, goldWarm, pctFH * 0.18f);
        }

        // Advance cursor past the band - SET position absolutely so the
        // intermediate ImGui widget calls (from the pills) don't leave the
        // cursor dangling somewhere inside the band.
        ImGui.SetCursorScreenPos(new Vector2(bandStart.X, bandMx.Y));
    }

    // ── Codex pills (Show / Sort / Dir) ──
    // Custom-drawn to match HTML .pill: 28px in HTML, bumped to 34px in-game
    // so the pill tracks the beefier stat-row visual weight.
    private void DrawStatControlsPills(ImDrawListPtr dl, float topY, float rightEdge, float s)
    {
        float pillH = 34f * s;

        // Build right-to-left so we land exactly at rightEdge on the right side.
        float cursorR = rightEdge;
        cursorR = DrawPillDir(dl, cursorR, topY, pillH, s);
        cursorR -= 6f * s;
        cursorR = DrawPillCombo(dl, cursorR, topY, pillH, s,
            "SORT", SortLabels[sortMode], "##achSortPopup",
            SortLabels, sortMode,
            idx => {
                if (sortMode != idx) { sortMode = idx; sortDescending = (idx != 3); }
            });
        cursorR -= 6f * s;
        cursorR = DrawPillCombo(dl, cursorR, topY, pillH, s,
            "SHOW", RewardLabels[rewardFilter], "##achShowPopup",
            RewardLabels, rewardFilter,
            idx => {
                rewardFilter = idx;
                currentPage = 0;
            });
    }

    // Draws a Codex pill [LABEL VALUE ▾] right-anchored at `rightX`, returns the
    // new right-X (i.e. the pill's left edge) so the caller can continue leftward.
    // Popup body is rendered via custom chassis-styled rows (icons + tracked-caps)
    // instead of default ImGui.Selectable so the dropdown actually looks designed.
    private float DrawPillCombo(ImDrawListPtr dl, float rightX, float topY, float pillH, float s,
        string label, string value, string popupId, string[] items, int selectedIdx, Action<int> onSelect)
    {
        float padX = 11f * s;
        float gap = 9f * s;
        // HTML .pill: Oswald 10px @ 0.16em uppercase for BOTH label and value.
        //   .pill-label = weight 500 (Medium)
        //   .pill-value = weight 600 (SemiBold)
        string labelCaps = label.ToUpperInvariant();
        string valueCaps = value.ToUpperInvariant();
        float trk = 10f * s * 0.16f;
        Vector2 lblSz, valSz;
        using (Plugin.Instance?.OswaldMed10?.Push())
        {
            float fh = ImGui.GetFontSize();
            lblSz = new Vector2(Boutique.MeasureTrackedText(labelCaps, trk), fh);
        }
        using (Plugin.Instance?.OswaldSemi10?.Push())
        {
            float fh = ImGui.GetFontSize();
            valSz = new Vector2(Boutique.MeasureTrackedText(valueCaps, trk), fh);
        }
        float caretBaseW = 6f * s; // pill caret (8px in HTML → triangle base ~6px)
        float w = padX * 2f + lblSz.X + gap + valSz.X + gap + caretBaseW;

        var mn = new Vector2(rightX - w, topY);
        var mx = new Vector2(rightX,     topY + pillH);

        ImGui.SetCursorScreenPos(mn);
        if (ImGui.InvisibleButton($"##pill_{popupId}", new Vector2(w, pillH)))
            ImGui.OpenPopup(popupId);
        bool hovered = ImGui.IsItemHovered();

        // Body
        var bgCol = hovered
            ? Boutique.Surface1
            : new Vector4(0.078f, 0.094f, 0.125f, 0.6f);
        var borderCol = hovered
            ? Boutique.Border
            : Boutique.BorderSoft;
        dl.AddRectFilled(mn, mx, ImGui.ColorConvertFloat4ToU32(bgCol));
        dl.AddRect(mn, mx, ImGui.ColorConvertFloat4ToU32(borderCol), 0f, ImDrawFlags.None, 1f);

        // Both label and value are the same Oswald 10 size; baseline shared.
        float textY = mn.Y + (pillH - lblSz.Y) * 0.5f;
        float cursorX = mn.X + padX;
        using (Plugin.Instance?.OswaldMed10?.Push())
            Boutique.DrawTrackedText(dl,
                new Vector2(cursorX, textY),
                labelCaps, ImGui.ColorConvertFloat4ToU32(Boutique.TextFaint), trk);
        cursorX += lblSz.X + gap;
        using (Plugin.Instance?.OswaldSemi10?.Push())
            Boutique.DrawTrackedText(dl,
                new Vector2(cursorX, textY),
                valueCaps, ImGui.ColorConvertFloat4ToU32(hovered ? Boutique.Text : Boutique.TextDim), trk);
        cursorX += valSz.X + gap;
        // Caret - small triangle, text-faint (HTML .caret font-size: 8px).
        float caretHalf = caretBaseW * 0.5f;
        float caretH = caretBaseW * 0.6f;
        float caretCY = mn.Y + pillH * 0.5f;
        uint caretCol = ImGui.ColorConvertFloat4ToU32(
            hovered ? Boutique.TextDim : Boutique.TextFaint);
        dl.AddTriangleFilled(
            new Vector2(cursorX,                 caretCY - caretH * 0.5f),
            new Vector2(cursorX + caretBaseW,    caretCY - caretH * 0.5f),
            new Vector2(cursorX + caretHalf,     caretCY + caretH * 0.5f),
            caretCol);

        // Popup - custom chassis-styled rows. Each row: left gold accent bar
        // when selected + Oswald tracked-caps label, hover = gold-14% wash,
        // click = onSelect(idx) + close. Width matches the pill so it visually
        // "drops down" from the pill itself.
        ImGui.SetNextWindowPos(new Vector2(mn.X, mx.Y + 2f * s));
        ImGui.SetNextWindowSizeConstraints(new Vector2(w, 0), new Vector2(w, float.MaxValue));
        ImGui.PushStyleColor(ImGuiCol.PopupBg,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.040f, 0.046f, 0.058f, 0.98f)));
        ImGui.PushStyleColor(ImGuiCol.Border,
            ImGui.ColorConvertFloat4ToU32(Boutique.Border));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0f, 4f * s));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        if (ImGui.BeginPopup(popupId))
        {
            var popMn = ImGui.GetWindowPos();
            var popSz = ImGui.GetWindowSize();
            float rowH = 28f * s;
            float rowTrk = 10f * s * 0.14f;
            var popDl = ImGui.GetWindowDrawList();

            for (int idx = 0; idx < items.Length; idx++)
            {
                bool active = idx == selectedIdx;
                string rowLabel = items[idx].ToUpperInvariant();

                // Row hit target - full width
                ImGui.SetCursorScreenPos(new Vector2(popMn.X, ImGui.GetCursorScreenPos().Y));
                if (ImGui.InvisibleButton($"##row_{popupId}_{idx}",
                        new Vector2(popSz.X, rowH)))
                {
                    onSelect(idx);
                    ImGui.CloseCurrentPopup();
                }
                bool rowHov = ImGui.IsItemHovered();
                var rowMn = ImGui.GetItemRectMin();
                var rowMx = ImGui.GetItemRectMax();

                // Hover/active wash
                if (active)
                    popDl.AddRectFilled(rowMn, rowMx,
                        ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0.10f)));
                else if (rowHov)
                    popDl.AddRectFilled(rowMn, rowMx,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.03f)));

                // Left accent bar - 2px gold if selected
                if (active)
                    popDl.AddRectFilled(
                        rowMn,
                        new Vector2(rowMn.X + 2f * s, rowMx.Y),
                        ImGui.ColorConvertFloat4ToU32(Boutique.Gold));

                // Label - tracked Oswald caps, baseline centred
                Vector4 lblCol = active
                    ? Boutique.GoldWarm
                    : (rowHov ? Boutique.Text : Boutique.TextDim);
                using (Plugin.Instance?.OswaldMed10?.Push())
                {
                    float rowFh = ImGui.GetFontSize();
                    Boutique.DrawTrackedText(popDl,
                        new Vector2(rowMn.X + 14f * s, rowMn.Y + (rowH - rowFh) * 0.5f),
                        rowLabel, ImGui.ColorConvertFloat4ToU32(lblCol), rowTrk);
                }

                // Hairline divider between rows (skip the last)
                if (idx < items.Length - 1)
                    popDl.AddRectFilled(
                        new Vector2(rowMn.X + 8f * s, rowMx.Y - 1f * s),
                        new Vector2(rowMx.X - 8f * s, rowMx.Y),
                        ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.BorderSoft, 0.60f)));
            }
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar(4);
        ImGui.PopStyleColor(2);

        return mn.X;
    }

    // Square 28×28 direction toggle pill. Returns its left edge for leftward layout.
    private float DrawPillDir(ImDrawListPtr dl, float rightX, float topY, float pillH, float s)
    {
        float w = pillH; // square
        var mn = new Vector2(rightX - w, topY);
        var mx = new Vector2(rightX,     topY + pillH);

        ImGui.SetCursorScreenPos(mn);
        if (ImGui.InvisibleButton("##pill_sortDir", new Vector2(w, pillH)))
            sortDescending = !sortDescending;
        bool hovered = ImGui.IsItemHovered();

        var bgCol = hovered ? Boutique.Surface1 : new Vector4(0.078f, 0.094f, 0.125f, 0.6f);
        var borderCol = hovered ? Boutique.Border : Boutique.BorderSoft;
        dl.AddRectFilled(mn, mx, ImGui.ColorConvertFloat4ToU32(bgCol));
        dl.AddRect(mn, mx, ImGui.ColorConvertFloat4ToU32(borderCol), 0f, ImDrawFlags.None, 1f);

        // FontAwesome - HTML uses fa-arrow-down-wide-short (), the
        // ascending inverse is fa-arrow-up-wide-short (). These are
        // the "stacked lines with arrow" sort icons, NOT a plain arrow.
        string iconGlyph = sortDescending ? "" : "";
        ImGui.PushFont(UiBuilder.IconFont);
        var iconSz = ImGui.CalcTextSize(iconGlyph);
        ImGui.PopFont();
        dl.AddText(UiBuilder.IconFont, UiBuilder.IconFont.FontSize,
            mn + (new Vector2(w, pillH) - iconSz) * 0.5f,
            ImGui.ColorConvertFloat4ToU32(hovered ? Boutique.Text : Boutique.TextDim),
            iconGlyph);

        if (hovered)
            CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(sortDescending
                ? "Sort descending (highest → lowest)\nClick to flip"
                : "Sort ascending (lowest → highest)\nClick to flip");

        return mn.X;
    }

    // Firework spark at the progress bar's leading edge: pulsing core + drifting dots
    private void DrawStatsFirework(ImDrawListPtr dl, Vector2 centre, float s)
    {
        float t = (float)Boutique.AnimTime(ImGui.GetTime());

        // Core - 10×10 radial, pulsing scale 1.0 ↔ 1.4 on 1.3s ease-in-out
        float cycle = (t % 1.3f) / 1.3f;
        float pulse = 0.5f + 0.5f * MathF.Sin(cycle * MathF.Tau - MathF.PI * 0.5f);
        float coreScale = 1f + 0.4f * pulse;
        float coreR = 5f * s * coreScale;

        // White centre → gold-warm → gold → transparent
        dl.AddCircleFilled(centre, coreR * 0.25f,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f)), 14);
        dl.AddCircleFilled(centre, coreR * 0.55f,
            ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.GoldWarm, 0.85f)), 18);
        dl.AddCircleFilled(centre, coreR,
            ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0.45f)), 20);
        dl.AddCircleFilled(centre, coreR * 1.5f,
            ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0.15f)), 22);

        if (Boutique.ReduceMotion) return;

        // 4 drifting spark particles, each with its own offset and delay
        (float dx, float dy, float delay)[] particles = {
            (  8f, -6f, 0.0f),
            ( 10f,  4f, 0.3f),
            (  6f,  8f, 0.7f),
            ( 12f, -2f, 1.1f),
        };
        foreach (var (dx, dy, delay) in particles)
        {
            // 2s loop per mockup .spark-dots
            float localT = ((t - delay) % 2f + 2f) % 2f;
            float lp = localT / 2f;
            if (lp < 0.01f) continue;
            float a = lp < 0.2f ? lp / 0.2f : (1f - (lp - 0.2f) / 0.8f);
            a = Math.Clamp(a, 0f, 1f);
            // Scale: 0.6 → 0.3
            float sc = 0.6f + (0.3f - 0.6f) * lp;
            // Translate lerps by lp
            var pos = centre + new Vector2(dx * s * lp, dy * s * lp);
            float pR = 1f * s * sc;
            dl.AddCircleFilled(pos, pR,
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.GoldWarm, a)), 8);
            dl.AddCircleFilled(pos, pR * 2.2f,
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.GoldWarm, a * 0.4f)), 10);
        }
    }

    // ═══════════════ SUBBAR (Achievements / Shop tabs + search) ═══════════════
    // HTML .subbar - 42px tall. Active tab = slip silhouette (6px chamfers)
    // with inset gold fill + gold underline glow. Inactive tabs = plain text.
    // Right-aligned search pill (210px wide, plain rect).
    private void DrawSubbar(float s)
    {
        var dl = ImGui.GetWindowDrawList();
        var barStart = ImGui.GetCursorScreenPos();
        float availW = ImGui.GetContentRegionAvail().X;
        float barH = 42f * s;
        float padX = 14f * s;
        var mn = barStart;
        var mx = new Vector2(barStart.X + availW, barStart.Y + barH);

        // Bg: surface-0 with bottom border
        dl.AddRectFilled(mn, mx,
            ImGui.ColorConvertFloat4ToU32(Boutique.Surface0));
        dl.AddRectFilled(
            new Vector2(mn.X, mx.Y - 1f * s), mx,
            ImGui.ColorConvertFloat4ToU32(Boutique.BorderSoft));

        // Tabs
        float cursorX = mn.X + padX;
        cursorX += DrawSubbarTab("ACHIEVEMENTS", 0, cursorX, mn.Y, barH, s, dl);
        cursorX += 4f * s;
        cursorX += DrawSubbarTab("SHOP", 1, cursorX, mn.Y, barH, s, dl);

        // Right-aligned search pill
        float pillW = 210f * s;
        float pillH = 28f * s;
        float pillRightPad = 14f * s;
        var pillMax = new Vector2(mx.X - pillRightPad, mn.Y + (barH + pillH) * 0.5f);
        var pillMin = new Vector2(pillMax.X - pillW, mn.Y + (barH - pillH) * 0.5f);

        // Background rect + border. Focus state (cached from last frame)
        // drives a gold border + outer glow ring so the pill reads as
        // keyboard-active when the user is typing into it.
        dl.AddRectFilled(pillMin, pillMax,
            ImGui.ColorConvertFloat4ToU32(searchFocused
                ? Boutique.WithAlpha(Boutique.Gold, 0.05f)
                : new Vector4(0.078f, 0.094f, 0.125f, 0.6f)));
        if (searchFocused)
        {
            for (int g = 1; g <= 2; g++)
            {
                float off = g * 2f * s;
                float ga = 0.22f / g;
                dl.AddRect(
                    new Vector2(pillMin.X - off, pillMin.Y - off),
                    new Vector2(pillMax.X + off, pillMax.Y + off),
                    ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.GoldWarm, ga)),
                    0f, ImDrawFlags.None, 1f);
            }
        }
        dl.AddRect(pillMin, pillMax,
            ImGui.ColorConvertFloat4ToU32(searchFocused
                ? Boutique.Gold
                : Boutique.BorderSoft), 0f, ImDrawFlags.None, 1f);

        // ── GLITCH FLAIR on focus-enter - one-shot scanline wipe across
        //    the pill (~300ms after the unfocused→focused transition),
        //    fading to 0. Tells the user the field is "live"; once focused
        //    the gold border is the resting cue. ──
        float pillFocusT = (float)ImGui.GetTime() - searchFocusTransitionTime;
        if (pillFocusT >= 0f && pillFocusT < GlitchFlashWindow)
        {
            float wipeA = 1f - (pillFocusT / GlitchFlashWindow);
            // Cyan scanlines across the pill interior
            float pitch = 3f * s;
            float stripeH = 1f * s;
            uint stripe = ImGui.ColorConvertFloat4ToU32(
                Boutique.WithAlpha(Boutique.GlitchCyan, 0.20f * wipeA));
            for (float y = pillMin.Y; y < pillMax.Y; y += pitch)
                dl.AddRectFilled(new Vector2(pillMin.X, y),
                                 new Vector2(pillMax.X, y + stripeH), stripe);
            // Magenta top + cyan bottom 1px fringes (toast vocabulary)
            dl.AddRectFilled(
                new Vector2(pillMin.X, pillMin.Y),
                new Vector2(pillMax.X, pillMin.Y + 1f * s),
                ImGui.ColorConvertFloat4ToU32(
                    Boutique.WithAlpha(Boutique.GlitchMagenta, 0.85f * wipeA)));
            dl.AddRectFilled(
                new Vector2(pillMin.X, pillMax.Y - 1f * s),
                new Vector2(pillMax.X, pillMax.Y),
                ImGui.ColorConvertFloat4ToU32(
                    Boutique.WithAlpha(Boutique.GlitchCyan, 0.85f * wipeA)));
        }

        // Magnifying glass icon
        float iconSize = 10f * s;
        var iconPos = new Vector2(pillMin.X + 10f * s, (pillMin.Y + pillMax.Y) * 0.5f - iconSize * 0.5f);
        ImGui.PushFont(UiBuilder.IconFont);
        float iconFontSize = 10f * s;
        float iconFontSizeNat = UiBuilder.IconFont.FontSize;
        dl.AddText(UiBuilder.IconFont, iconFontSize,
            iconPos,
            ImGui.ColorConvertFloat4ToU32(searchFocused ? Boutique.GoldWarm : Boutique.TextFaint), "");
        ImGui.PopFont();

        // Input field sits inside the pill. Use InputTextWithHint so the
        // placeholder shows when empty. Position cursor inside pill.
        float inputX = pillMin.X + 10f * s + iconSize + 8f * s;
        float inputY = pillMin.Y + (pillH - ImGui.GetFrameHeight()) * 0.5f;
        ImGui.SetCursorScreenPos(new Vector2(inputX, inputY));
        ImGui.SetNextItemWidth(pillMax.X - inputX - 8f * s);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.Text, Boutique.Text);
        ImGui.InputTextWithHint("##achSearch", "Search achievements...", ref searchQuery, 50);
        // Cache focus for NEXT frame's pill border. 1-frame lag is invisible
        // at 60fps and avoids drawing the border twice. Also detect the
        // unfocused → focused TRANSITION so the GLITCH FLAIR scanline wipe
        // fires once on focus-enter (not every frame while focused).
        bool focusedNow = ImGui.IsItemActive() || ImGui.IsItemFocused();
        if (focusedNow && !searchFocusedPrev)
            searchFocusTransitionTime = (float)ImGui.GetTime();
        searchFocusedPrev = focusedNow;
        searchFocused = focusedNow;
        ImGui.PopStyleColor(4);

        ImGui.SetCursorScreenPos(new Vector2(mn.X, mx.Y));
    }

    // Returns the width consumed (for cursor advance).
    private float DrawSubbarTab(string label, int tab, float startX, float topY, float barH, float s, ImDrawListPtr dl)
    {
        bool active = selectedTab == tab;

        // HTML .tab: 11px Oswald 600, letter-spacing 0.24em, uppercase
        Vector2 labelSz;
        float trk;
        using (Plugin.Instance?.OswaldSemi11?.Push())
        {
            trk = ImGui.GetFontSize() * 0.24f;
            labelSz = new Vector2(Boutique.MeasureTrackedText(label, trk), ImGui.GetFontSize());
        }
        float padX = 20f * s;
        float tabW = labelSz.X + padX * 2f;

        ImGui.SetCursorScreenPos(new Vector2(startX, topY));
        if (ImGui.InvisibleButton($"##subbarTab{tab}", new Vector2(tabW, barH)))
            selectedTab = tab;
        bool hovered = ImGui.IsItemHovered();

        var tabMn = new Vector2(startX, topY);
        var tabMx = new Vector2(startX + tabW, topY + barH);

        if (active)
        {
            // Slip silhouette with vertical gold gradient (inset 6px from top,
            // 8px from sides, flush to bottom). 6px TR+BL chamfers.
            float inset = 6f * s;
            float insetSide = 8f * s;
            var slipMn = new Vector2(tabMn.X + insetSide, tabMn.Y + inset);
            var slipMx = new Vector2(tabMx.X - insetSide, tabMx.Y);
            float chamfer = 6f * s;

            // Filled slip: vertical gold gradient 24% top → 6% bottom
            Vector4 topCol = Boutique.WithAlpha(Boutique.Gold, 0.24f);
            Vector4 botCol = Boutique.WithAlpha(Boutique.Gold, 0.06f);
            Boutique.FillSlip(dl, slipMn, slipMx, chamfer,
                ImGui.ColorConvertFloat4ToU32(Boutique.Lerp(topCol, botCol, 0.5f)));

            // Inset stroke border (gold @ 35% alpha)
            Boutique.StrokeSlip(dl, slipMn, slipMx, chamfer,
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0.35f)), 1f);

            // Gold underline with glow (2px tall, 14px inset from sides)
            float underlineY = tabMx.Y - 1f * s;
            dl.AddRectFilled(
                new Vector2(tabMn.X + 14f * s, underlineY - 1f * s),
                new Vector2(tabMx.X - 14f * s, underlineY + 1f * s),
                ImGui.ColorConvertFloat4ToU32(Boutique.Gold));
            // Soft glow above the underline
            dl.AddRectFilledMultiColor(
                new Vector2(tabMn.X + 14f * s, underlineY - 6f * s),
                new Vector2(tabMx.X - 14f * s, underlineY - 1f * s),
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0f)),
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0f)),
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0.45f)),
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0.45f)));

            // ── GLITCH FLAIR on tab switch - chromatic-split fringes ride
            //    the gold underline for ~300ms after the click, then fade.
            //    Gold underline above is the resting state. ──
            float subT = (float)ImGui.GetTime() - subbarChangeTime;
            if (subT >= 0f && subT < GlitchFlashWindow)
            {
                float chromaA = 1f - (subT / GlitchFlashWindow);
                float ulLeft = tabMn.X + 14f * s;
                float ulRight = tabMx.X - 14f * s;
                dl.AddRectFilled(
                    new Vector2(ulLeft, underlineY - 2f * s),
                    new Vector2(ulRight, underlineY - 1f * s),
                    ImGui.ColorConvertFloat4ToU32(
                        Boutique.WithAlpha(Boutique.GlitchMagenta, 0.85f * chromaA)));
                dl.AddRectFilled(
                    new Vector2(ulLeft, underlineY + 1f * s),
                    new Vector2(ulRight, underlineY + 2f * s),
                    ImGui.ColorConvertFloat4ToU32(
                        Boutique.WithAlpha(Boutique.GlitchCyan, 0.85f * chromaA)));
            }
        }

        // Label: active = gold-warm, hover = text-dim, else text-faint
        Vector4 labelCol = active ? Boutique.GoldWarm
                          : (hovered ? Boutique.TextDim : Boutique.TextFaint);
        var labelPos = new Vector2(
            tabMn.X + (tabW - labelSz.X) * 0.5f,
            tabMn.Y + (barH - labelSz.Y) * 0.5f);
        using (Plugin.Instance?.OswaldSemi11?.Push())
        {
            Boutique.DrawTrackedText(dl, labelPos, label,
                ImGui.ColorConvertFloat4ToU32(labelCol), trk);
        }

        return tabW;
    }

    // ═══════════════ CATEGORY ROW ═══════════════

    private void DrawCategoryRow(float s)
    {
        // HTML .categories - surface-0 bg, 12px top / 14px bot padding, 5px
        // gap between pills, wraps onto multiple rows. Active pill becomes a
        // slip silhouette tinted by category colour; inactive pills are plain
        // rects with border-soft strokes.
        var dl = ImGui.GetWindowDrawList();
        var rowStart = ImGui.GetCursorScreenPos();
        float availW = ImGui.GetContentRegionAvail().X;
        float padX = 14f * s;
        float padTop = 12f * s;
        float padBot = 14f * s;
        float gap = 5f * s;

        // Underline-stroke variant: no body at rest, hover paints surface-1,
        // active paints a coloured underline + corner tick, count is unlocked/total
        float cellH = 38f * s;
        float rowY = rowStart.Y + padTop;
        float cursorX = rowStart.X + padX;
        float rightEdge = rowStart.X + availW - padX;
        float cellGap = 6f * s;
        float iconRenderSize = 16f * s;

        // Reset per-frame cell rect cache. Used by the animated active
        // underline drawn after this loop.
        categoryCellRects.Clear();

        for (int i = 0; i < CatTabs.Length; i++)
        {
            bool sel = selectedCategoryIndex == i;
            var col = CatTabs[i].Color;
            string icon = CatTabs[i].Icon;
            string label = CatTabs[i].Label;

            // Measure icon at explicit render size
            ImGui.PushFont(UiBuilder.IconFont);
            float iconNatSize = UiBuilder.IconFont.FontSize;
            var iconNat = ImGui.CalcTextSize(icon);
            ImGui.PopFont();
            float iconScale = iconRenderSize / iconNatSize;
            var iconSz = iconNat * iconScale;

            // Count: "unlocked/total" - overall progress per category
            int totalInCat, unlockedInCat;
            if (CatTabs[i].Cat == null)
            {
                totalInCat = AchievementRegistry.All.Count();
                unlockedInCat = plugin.Configuration.AchievementData.UnlockedAchievements.Count;
            }
            else
            {
                var cat = CatTabs[i].Cat!.Value;
                totalInCat = AchievementRegistry.All.Count(a => a.Category == cat);
                unlockedInCat = AchievementRegistry.All.Count(a =>
                    a.Category == cat &&
                    plugin.Configuration.AchievementData.IsUnlocked(a.Id));
            }
            string countStr = $"{unlockedInCat}/{totalInCat}";
            Vector2 countSz;
            float cntTrk;
            using (Plugin.Instance?.OswaldMed10?.Push())
            {
                cntTrk = ImGui.GetFontSize() * 0.10f;
                countSz = new Vector2(
                    Boutique.MeasureTrackedText(countStr, cntTrk),
                    ImGui.GetFontSize());
            }

            float iconCountGap = 7f * s;
            float cellPadX = 13f * s;
            float cellW = cellPadX * 2f + iconSz.X + iconCountGap + countSz.X;

            // Wrap defensively if overflowing
            if (cursorX + cellW > rightEdge && cursorX > rowStart.X + padX)
            {
                cursorX = rowStart.X + padX;
                rowY += cellH + gap;
            }

            var mn = new Vector2(cursorX, rowY);
            var mx = new Vector2(cursorX + cellW, rowY + cellH);
            // Cache for the animated underline overlay (drawn after loop).
            categoryCellRects.Add(new CatCellRect { Min = mn, Max = mx, Color = col });

            ImGui.SetCursorScreenPos(mn);
            if (ImGui.InvisibleButton($"##cat_{i}", new Vector2(cellW, cellH)))
            {
                if (selectedCategoryIndex != i)
                {
                    // Capture FROM index BEFORE updating selection so the
                    // animated underline can lerp from the previous cell's
                    // position+colour to the new one (Encore HelpWindow
                    // dot transition pattern).
                    categoryFromIndex = selectedCategoryIndex;
                    categoryChangeTime = (float)ImGui.GetTime();
                    // FLIP snapshot - capture last-rendered positions and
                    // list so survivors can lerp old → new and exiters can
                    // fade out at where they USED to be.
                    if (lastFrameRegularList != null)
                    {
                        prevTileList = lastFrameRegularList.ToList();
                        tileOldPos = new Dictionary<string, Vector2>(tileLastFramePos);
                    }
                }
                selectedCategoryIndex = i;
                currentPage = 0;
            }
            bool hov = ImGui.IsItemHovered();
            if (hov)
            {
                // Chassis-styled tooltip: dark surface, 1px border-soft, tight
                // padding, no rounded corners. Label in Oswald tracked caps on
                // top, count in Outfit body below - matches chassis vocabulary.
                ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.040f, 0.046f, 0.058f, 0.98f));
                ImGui.PushStyleColor(ImGuiCol.Border, Boutique.Border);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(11f * s, 8f * s));
                ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
                ImGui.BeginTooltip();
                using (Plugin.Instance?.OswaldMed10?.Push())
                {
                    float trk = ImGui.GetFontSize() * 0.18f;
                    var dl2 = ImGui.GetWindowDrawList();
                    var pos = ImGui.GetCursorScreenPos();
                    float w = Boutique.DrawTrackedText(dl2, pos,
                        label.ToUpperInvariant(),
                        ImGui.ColorConvertFloat4ToU32(col), trk);
                    ImGui.Dummy(new Vector2(w, ImGui.GetFontSize()));
                }
                using (Plugin.Instance?.OutfitBody12?.Push())
                    ImGui.TextColored(Boutique.TextDim,
                        $"{unlockedInCat}/{totalInCat} unlocked");
                ImGui.EndTooltip();
                ImGui.PopStyleVar(3);
                ImGui.PopStyleColor(2);
            }

            // ── Surface + border - tab bar behaviour ──
            // The entire active state (body fill, top accent, underline,
            // bloom ring, breathing halo) is a single animated overlay drawn
            // AFTER the cell loop so it can lerp position/width/colour
            // between cells on category change.
            uint iconU, countU;
            if (sel)
            {
                // Active: text/icon recolour only. The body chrome is the
                // animated overlay below.
                iconU  = ImGui.ColorConvertFloat4ToU32(col);
                countU = ImGui.ColorConvertFloat4ToU32(col);
            }
            else if (hov)
            {
                // Hover: faint surface lift + thin border-soft box
                dl.AddRectFilled(mn, mx,
                    ImGui.ColorConvertFloat4ToU32(Boutique.Surface1));
                dl.AddRect(mn, mx,
                    ImGui.ColorConvertFloat4ToU32(Boutique.BorderSoft),
                    0f, ImDrawFlags.None, 1f);
                iconU  = ImGui.ColorConvertFloat4ToU32(Boutique.Text);
                countU = ImGui.ColorConvertFloat4ToU32(Boutique.TextDim);
            }
            else
            {
                // Rest: transparent - no body chrome at all
                iconU  = ImGui.ColorConvertFloat4ToU32(Boutique.TextDim);
                countU = ImGui.ColorConvertFloat4ToU32(Boutique.TextFaint);
            }

            // Content - icon + count inline, vertically centred
            float contentY = rowY + (cellH - iconSz.Y) * 0.5f;
            float drawX = cursorX + cellPadX;
            dl.AddText(UiBuilder.IconFont, iconRenderSize,
                new Vector2(drawX, contentY),
                iconU, icon);
            drawX += iconSz.X + iconCountGap;
            // Count baseline aligned to icon (centred vertically)
            float countY = rowY + (cellH - countSz.Y) * 0.5f;
            using (Plugin.Instance?.OswaldMed10?.Push())
                Boutique.DrawTrackedText(dl,
                    new Vector2(drawX, countY),
                    countStr, countU, cntTrk);

            cursorX += cellW + cellGap;
        }

        // Animated active indicator: cell-body fill + top/bottom accents
        // lerps between cells via easeOutCubic. Bloom ring on transition,
        // breathing halo at rest.
        if (categoryCellRects.Count > 0
            && selectedCategoryIndex < categoryCellRects.Count)
        {
            float now = (float)ImGui.GetTime();
            float elapsed = now - categoryChangeTime;
            float tRaw = MathF.Min(1f, MathF.Max(0f, elapsed / CategoryTransitionSec));
            float tEase = 1f - MathF.Pow(1f - tRaw, 3f); // easeOutCubic
            bool transitioning = elapsed < CategoryTransitionSec
                              && categoryFromIndex >= 0
                              && categoryFromIndex < categoryCellRects.Count
                              && categoryFromIndex != selectedCategoryIndex;

            int fromIdx = transitioning ? categoryFromIndex : selectedCategoryIndex;
            var rFrom = categoryCellRects[fromIdx];
            var rTo   = categoryCellRects[selectedCategoryIndex];

            float fromX = rFrom.Min.X, fromYTop = rFrom.Min.Y, fromYBot = rFrom.Max.Y;
            float fromW = rFrom.Max.X - rFrom.Min.X;
            float toX = rTo.Min.X, toYTop = rTo.Min.Y, toYBot = rTo.Max.Y;
            float toW = rTo.Max.X - rTo.Min.X;

            float ux = fromX + (toX - fromX) * tEase;
            float uyTop = fromYTop + (toYTop - fromYTop) * tEase;
            float uyBot = fromYBot + (toYBot - fromYBot) * tEase;
            float uw = fromW + (toW - fromW) * tEase;
            Vector4 ucol = Boutique.Lerp(rFrom.Color, rTo.Color, tEase);
            float thickness = 2f * s;
            float topAccentH = 1f * s;

            // ── Body fill: 3-stop horizontal gradient giving the active
            //    indicator visual mass. Centre brighter, edges softer, so
            //    the moving shape reads as a "thing" not a flat slab. ──
            uint cBodyEdge = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(ucol, 0.025f));
            uint cBodyMid  = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(ucol, 0.12f));
            float midX = ux + uw * 0.5f;
            // Body sits BETWEEN the top accent and the underline.
            var bodyMn = new Vector2(ux, uyTop + topAccentH);
            var bodyMx = new Vector2(ux + uw, uyBot - thickness);
            dl.AddRectFilledMultiColor(
                bodyMn, new Vector2(midX, bodyMx.Y),
                cBodyEdge, cBodyMid, cBodyMid, cBodyEdge);
            dl.AddRectFilledMultiColor(
                new Vector2(midX, bodyMn.Y), bodyMx,
                cBodyMid, cBodyEdge, cBodyEdge, cBodyMid);

            // ── Top accent: 1px coloured edge at ~30% alpha. Bracket-style
            //    framing so the active state isn't bottom-heavy. ──
            dl.AddRectFilled(
                new Vector2(ux, uyTop),
                new Vector2(ux + uw, uyTop + topAccentH),
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(ucol, 0.30f)));

            // ── Soft glow above the underline (3 stacked low-alpha bands) ──
            for (int g = 1; g <= 3; g++)
            {
                float pad = g * 1.5f * s;
                float ga = 0.18f / g;
                dl.AddRectFilled(
                    new Vector2(ux, uyBot - thickness - pad),
                    new Vector2(ux + uw, uyBot - thickness),
                    ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(ucol, ga)));
            }

            // ── Underline (2px bottom edge, full alpha) ──
            dl.AddRectFilled(
                new Vector2(ux, uyBot - thickness),
                new Vector2(ux + uw, uyBot),
                ImGui.ColorConvertFloat4ToU32(ucol));

            // ── GLITCH FLAIR on click - chromatic-split fringes ride the
            //    underline during the transition window, fading to 0 by the
            //    time the slide completes. Cat-coloured underline below is
            //    the resting state; this is just the texture of the click. ──
            if (transitioning)
            {
                float chromaA = 1f - tEase;
                dl.AddRectFilled(
                    new Vector2(ux, uyBot - thickness - 2f * s),
                    new Vector2(ux + uw, uyBot - thickness - 1f * s),
                    ImGui.ColorConvertFloat4ToU32(
                        Boutique.WithAlpha(Boutique.GlitchMagenta, 0.85f * chromaA)));
                dl.AddRectFilled(
                    new Vector2(ux, uyBot),
                    new Vector2(ux + uw, uyBot + 1f * s),
                    ImGui.ColorConvertFloat4ToU32(
                        Boutique.WithAlpha(Boutique.GlitchCyan, 0.85f * chromaA)));

                // Speck burst at the destination cell (8 specks scatter outward)
                Vector2 burstCtr = new Vector2(
                    (rTo.Min.X + rTo.Max.X) * 0.5f,
                    rTo.Max.Y - thickness * 0.5f);
                float burstA = (1f - tEase) * (1f - tEase);  // quick decay
                if (burstA > 0.02f)
                {
                    int burstSeed = HashCombine(selectedCategoryIndex, 0xCAFE);
                    for (int sp = 0; sp < 8; sp++)
                    {
                        int hA = HashCombine(burstSeed, sp * 3329 + 7);
                        int hC = HashCombine(burstSeed, sp * 5273 + 13);
                        float angle = ((hA & 0xFFFF) / 65535f) * MathF.Tau;
                        float dist = (10f * s) + tEase * 22f * s;
                        var pos = burstCtr + new Vector2(
                            MathF.Cos(angle) * dist,
                            MathF.Sin(angle) * dist * 0.5f);
                        uint sc = (hC & 1) == 0
                            ? ImGui.ColorConvertFloat4ToU32(
                                Boutique.WithAlpha(Boutique.GlitchMagenta, burstA))
                            : ImGui.ColorConvertFloat4ToU32(
                                Boutique.WithAlpha(Boutique.GlitchCyan, burstA));
                        dl.AddRectFilled(pos, pos + new Vector2(2f * s, 2f * s), sc);
                    }
                }
            }

            // ── Bloom ring centred on the NEW active cell's MIDPOINT
            //    (not the underline). Radius scales with cell width so it
            //    reads proportional. Encore's wi*0.5 + t*dotSize*2.5 →
            //    ours: starts at ~30% of cell width, ends at ~120%. ──
            if (transitioning)
            {
                var ctr = new Vector2(
                    (rTo.Min.X + rTo.Max.X) * 0.5f,
                    (rTo.Min.Y + rTo.Max.Y) * 0.5f);
                float r0 = toW * 0.30f;
                float r1 = toW * 1.20f;
                float rippleR = r0 + (r1 - r0) * tEase;
                float rippleA = (1f - tEase) * 0.55f;
                if (rippleA > 0.01f)
                {
                    // Two concentric rings for depth - outer fades faster
                    dl.AddCircle(ctr, rippleR,
                        ImGui.ColorConvertFloat4ToU32(
                            Boutique.WithAlpha(rTo.Color, rippleA)),
                        40, 1.5f);
                    dl.AddCircle(ctr, rippleR * 0.72f,
                        ImGui.ColorConvertFloat4ToU32(
                            Boutique.WithAlpha(rTo.Color, rippleA * 0.55f)),
                        40, 1.0f);
                }
            }
            else
            {
                // ── Stable active: breathing halo over the FULL active rect.
                //    Subtle alpha sine (~0.6 Hz) - felt, not seen. ──
                float breath = 0.5f + 0.5f * MathF.Sin(now * MathF.PI * 1.2f);
                float halo = 0.04f + 0.04f * breath;
                uint cHalo = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(ucol, halo));
                dl.AddRectFilled(bodyMn, bodyMx, cHalo);
            }
        }

        // Total row height: last rowY + cellH + padBot
        float totalH = (rowY - rowStart.Y) + cellH + padBot;
        var rowMx = new Vector2(rowStart.X + availW, rowStart.Y + totalH);

        // Backfill the surface-0 background behind all pills (drawn after so
        // it sits BEHIND them - use channel split? simpler: just rely on the
        // pill fills sitting on top of the window bg which is already surface-0).
        // The HTML has `background: var(--surface-0)` which matches our WinBg.

        // Bottom separator hairline (border-soft)
        dl.AddRectFilled(
            new Vector2(rowMx.X - availW, rowMx.Y - 1f * s),
            rowMx,
            ImGui.ColorConvertFloat4ToU32(Boutique.BorderSoft));

        // Advance cursor to the end of the row
        ImGui.SetCursorScreenPos(new Vector2(rowStart.X, rowMx.Y));
    }

    // ═══════════════ ACHIEVEMENT CONTENT ═══════════════

    private void DrawAchievementContent(AchievementData data, float s)
    {
        // Reset per-frame card index - drives the staggered shuffle slide-in
        // on category change / tab-enter. Each card increments + reads this.
        cardDrawCounter = 0;

        // HTML .content: padding 14px 18px 18px - keeps cards inset from the
        // window edges so the bottom corner brackets (BL/BR) remain visible
        // as a footer detail. Scroll child is also reserved 22px at the
        // bottom so it doesn't run over the brackets.
        float gutterX = 18f * s;
        float footerReserve = 22f * s;

        // Encore pattern: push the horizontal gutter via WindowPadding BEFORE
        // BeginChild. ItemSpacing restores inside the child so intra-card
        // spacing behaves normally.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(gutterX, 8f * s));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6f * s, 4f * s));
        ImGui.BeginChild("AchScroll", new Vector2(-1f, -footerReserve), false);
        ImGui.PopStyleVar(2);

        // Capture viewport bounds for the celebration cinematic - backdrop
        // dim covers this rect, viewport centre is the HOLD target.
        celebViewportMin = ImGui.GetWindowPos();
        celebViewportMax = celebViewportMin + ImGui.GetWindowSize();

        float indentedAvailW = ImGui.GetContentRegionAvail().X;

        var all = GetFilteredAchievements(data);
        var sorted = SortAchievements(all, data);

        // "Almost There" - closest to completion, NOT already completed
        // Searches ALL achievements (including hidden) since in-progress hidden ones should be revealed here.
        // If there aren't enough in-progress achievements, fill the slots with recommendations
        // so the section never feels empty.
        var almostThere = new List<AchievementDefinition>();
        bool anyInProgress = false;
        if (selectedCategoryIndex == 0 && string.IsNullOrWhiteSpace(searchQuery) && rewardFilter == 0)
        {
            almostThere = AchievementRegistry.All
                .Where(a => !data.IsUnlocked(a.Id) && GetProgress(a) > 0)
                .OrderByDescending(a => GetProgress(a))
                .Take(2)
                .ToList();
            anyInProgress = almostThere.Count > 0;

            // Fallback: fill empty slots with recommendations (easiest non-hidden uncompleted achievements).
            // Stable order so the suggestion doesn't churn between frames.
            if (almostThere.Count < 2)
            {
                var recommended = AchievementRegistry.All
                    .Where(a => !data.IsUnlocked(a.Id) && !a.IsHidden && GetProgress(a) <= 0)
                    .Where(a => !almostThere.Contains(a))
                    .OrderBy(a => (int)a.Tier)   // Bronze first
                    .ThenBy(a => a.Points)        // cheaper first
                    .ThenBy(a => a.Id)            // stable tiebreaker
                    .Take(2 - almostThere.Count)
                    .ToList();
                almostThere.AddRange(recommended);
            }
        }

        // Ambient layer - drifting radial spots + scrolling hum lines + rising
        // motes. Drawn once per Draw under the section content so it reads
        // like depth behind the cards without competing for attention.
        // Drifting aurora spots only - the hum-line layer reads as random
        // horizontal streaks behind the cards, which the user found ugly.
        // Spots-only matches the Shop tab's atmosphere and the HTML mockup.
        DrawAmbientSpots(s);

        if (almostThere.Count > 0)
        {
            // Featured section rule - gold bar + gradient line + "N of Total" tail
            int totalCount = AchievementRegistry.All.Count();
            int unlockedCount = data.UnlockedCoreCount + data.UnlockedBonusCount;
            DrawSectionRule(
                anyInProgress ? "Almost There" : "Try These",
                $"{almostThere.Count} spotlighted · {unlockedCount} / {totalCount} overall",
                featured: true, s);
            float gap = 10 * s;
            float largeW = (indentedAvailW - gap) / 2;
            // Tightened from 150 to match the regular grid pass; spotlight
            // cards keep a few extra px over the all-grid (122) for a bit
            // more presence, but the old 150 left a chunk of dead space.
            float largeH = 128 * s;

            for (int i = 0; i < almostThere.Count; i++)
            {
                if (i == 1) ImGui.SameLine(0, gap);
                DrawCard(almostThere[i], data, s, largeW, largeH, true);
            }
            ImGui.Spacing();
            ImGui.Spacing();
        }

        // Main grid - all remaining, scrollable
        var regular = sorted.Where(a => !almostThere.Contains(a)).ToList();

        if (regular.Count > 0)
        {
            // Section header above the grid. ALWAYS present so the grid
            // never lands flush against the scroll-area top edge:
            //   · All + spotlight   → "All Achievements"
            //   · All without spot. → "All Achievements" (filter / search)
            //   · Any other tab      → category name (e.g., "Characters")
            if (selectedCategoryIndex == 0)
                DrawSectionRule("All Achievements", tail: null, featured: false, s);
            else
                DrawSectionRule(CatTabs[selectedCategoryIndex].Label,
                    tail: null, featured: false, s);

            float gap = 8 * s;
            int cols = indentedAvailW > 950 * s ? 4 : 3;
            float cardW = (indentedAvailW - gap * (cols - 1)) / cols;
            // Tightened from 138 (mockup's grid-auto-rows value, only needed
            // there to align CSS-grid translate offsets). Real content needs
            // ~102px; 122 gives a comfy breath above the progress foot.
            float cardH = 122 * s;

            // Capture grid metrics for the celebration cinematic - slot 0
            // (top-left of regular grid) is the LAND target, card dims drive
            // the centring math during HOLD.
            celebSlot0Pos  = ImGui.GetCursorScreenPos();
            celebGridCardW = cardW;
            celebGridCardH = cardH;

            // ── FLIP filter cascade transition ──
            // While transitioning, bypass the SameLine flow and position each
            // tile manually so survivors can lerp from old→new positions and
            // exiters/enterers can fade in/out at their respective places.
            float flipNow = (float)ImGui.GetTime();
            float flipElapsed = flipNow - categoryChangeTime;
            bool flipActive = flipElapsed >= 0f
                           && flipElapsed < TileTransitionDur
                           && prevTileList != null;

            // Hero celebration is in-flight - skip drawing the hero in the
            // grid; it's rendered cinematically as a final overlay so it
            // can scale + reposition above all other cards.
            bool celebActive = celebId != null
                            && celebStart >= 0f
                            && flipNow - celebStart < CelebDur;

            // Match the AchScroll child's vertical ItemSpacing so manual
            // positioning matches what the SameLine flow would produce.
            float rowGapY = ImGui.GetStyle().ItemSpacing.Y;

            // Compute new (post-unlock natural) positions - used by slide-over
            // and FLIP paths. Cheap to compute either way.
            Vector2 gridStart_ = ImGui.GetCursorScreenPos();
            var naturalPositions_ = new Dictionary<string, Vector2>(regular.Count);
            for (int i = 0; i < regular.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                naturalPositions_[regular[i].Id] = new Vector2(
                    gridStart_.X + col * (cardW + gap),
                    gridStart_.Y + row * (cardH + rowGapY));
            }

            if (celebActive)
            {
                // ── Hero celebration slide-over render ──
                // Other tiles lerp from their PRE-unlock snapshot positions
                // to their natural post-unlock positions over the LAND phase
                // (1800-2800ms after celebStart). Hero is skipped (drawn
                // cinematically as a final overlay).
                float celebElapsed = flipNow - celebStart;
                float slideOverEased;
                if (celebElapsed < CelebHoldEnd) slideOverEased = 0f;
                else
                {
                    float u = (celebElapsed - CelebHoldEnd) / (CelebDur - CelebHoldEnd);
                    slideOverEased = EaseOutCubic(MathF.Min(1f, u));
                }

                foreach (var tile in regular)
                {
                    Vector2 naturalPos = naturalPositions_[tile.Id];
                    tileLastFramePos[tile.Id] = naturalPos;
                    if (tile.Id == celebId) continue;

                    Vector2 oldPos = celebOtherTilesOldPos.TryGetValue(tile.Id, out var op)
                        ? op : naturalPos;

                    // Test mode: snapshot positions match natural positions
                    // (no real sort change happened). Slide-over is only
                    // visually demonstrable on REAL unlocks where the hero's
                    // pre-unlock slot K differs from its post-unlock slot 0.
                    Vector2 pos = new Vector2(
                        oldPos.X + (naturalPos.X - oldPos.X) * slideOverEased,
                        oldPos.Y + (naturalPos.Y - oldPos.Y) * slideOverEased);
                    ImGui.SetCursorScreenPos(pos);
                    DrawCard(tile, data, s, cardW, cardH, false);
                }

                // Advance cursor past the grid for downstream layout.
                int totalRows = (regular.Count + cols - 1) / cols;
                if (totalRows > 0)
                {
                    float gridBottomY = gridStart_.Y
                        + totalRows * cardH
                        + (totalRows - 1) * rowGapY;
                    ImGui.SetCursorScreenPos(new Vector2(gridStart_.X, gridBottomY));
                }
            }
            else if (flipActive)
            {
                var dl = ImGui.GetWindowDrawList();
                float t = MathF.Min(1f, flipElapsed / TileTransitionDur);
                float tEased = 1f - MathF.Pow(1f - t, 3f); // easeOutCubic

                // Veil alphas for fade out (exiters) and fade in (enterers).
                float exitAlpha = MathF.Min(1f, flipElapsed / TileExitDur);
                float enterRaw = (flipElapsed - TileEnterDelay) / TileEnterDur;
                float enterT = MathF.Min(1f, MathF.Max(0f, enterRaw));
                float enterVeil = 1f - enterT;

                Vector2 gridStart = ImGui.GetCursorScreenPos();

                // New positions for the current filtered list - derived
                // from grid layout so they match what the SameLine flow
                // would produce in the non-transition path.
                var newPositions = new Dictionary<string, Vector2>(regular.Count);
                for (int i = 0; i < regular.Count; i++)
                {
                    int col = i % cols;
                    int row = i / cols;
                    newPositions[regular[i].Id] = new Vector2(
                        gridStart.X + col * (cardW + gap),
                        gridStart.Y + row * (cardH + rowGapY));
                }

                var newIds  = new HashSet<string>(regular.Select(a => a.Id));
                var prevIds = new HashSet<string>(prevTileList!.Select(a => a.Id));

                // Pass 1: exiters at their OLD positions, fading out.
                // GLITCH FLAIR: scanline wipe + chromatic top/bottom fringes
                // overlay the bg-coloured veil so exiters dissolve like a
                // dropping signal instead of just fading to black.
                foreach (var tile in prevTileList!)
                {
                    if (newIds.Contains(tile.Id)) continue;
                    if (celebActive && tile.Id == celebId) continue; // hero rendered last
                    if (!tileOldPos.TryGetValue(tile.Id, out var pos)) continue;
                    ImGui.SetCursorScreenPos(pos);
                    DrawCard(tile, data, s, cardW, cardH, false, veilAlpha: exitAlpha);
                    // Scanline wipe across the card area (alpha rises with exit)
                    if (exitAlpha > 0.05f)
                    {
                        Vector2 cMn = pos;
                        Vector2 cMx = pos + new Vector2(cardW, cardH);
                        float pitch = 3f * s;
                        float stripeH = 1f * s;
                        uint stripe = ImGui.ColorConvertFloat4ToU32(
                            Boutique.WithAlpha(Boutique.GlitchCyan, 0.18f * exitAlpha));
                        for (float y = cMn.Y; y < cMx.Y; y += pitch)
                            dl.AddRectFilled(new Vector2(cMn.X, y),
                                             new Vector2(cMx.X, y + stripeH), stripe);
                        // Top + bottom magenta/cyan fringes (toast vocabulary)
                        dl.AddRectFilled(
                            new Vector2(cMn.X, cMn.Y),
                            new Vector2(cMx.X, cMn.Y + 1f * s),
                            ImGui.ColorConvertFloat4ToU32(
                                Boutique.WithAlpha(Boutique.GlitchMagenta, 0.85f * exitAlpha)));
                        dl.AddRectFilled(
                            new Vector2(cMn.X, cMx.Y - 1f * s),
                            new Vector2(cMx.X, cMx.Y),
                            ImGui.ColorConvertFloat4ToU32(
                                Boutique.WithAlpha(Boutique.GlitchCyan, 0.85f * exitAlpha)));
                    }
                }

                // Pass 2: survivors lerp old→new; enterers fade in at new.
                // GLITCH FLAIR for enterers: chromatic offset ghosts behind
                // the card resolve into the solid card as the veil fades -
                // reads as a digital "decode" rather than a plain fade-in.
                foreach (var tile in regular)
                {
                    Vector2 targetPos = newPositions[tile.Id];
                    // Track target position even for the celebrating hero so
                    // the NEXT celebration has a fresh slot K to RISE from.
                    tileLastFramePos[tile.Id] = targetPos;
                    if (celebActive && tile.Id == celebId) continue; // hero rendered last
                    bool isEntering = !prevIds.Contains(tile.Id);
                    Vector2 pos;
                    if (isEntering)
                    {
                        pos = targetPos;
                    }
                    else
                    {
                        Vector2 oldPos = tileOldPos.TryGetValue(tile.Id, out var op) ? op : targetPos;
                        pos = new Vector2(
                            oldPos.X + (targetPos.X - oldPos.X) * tEased,
                            oldPos.Y + (targetPos.Y - oldPos.Y) * tEased);
                    }
                    // For enterers: draw chromatic ghosts BEFORE the card so
                    // the card's own draw composites over them as the decode
                    // resolves. Alpha rides enterVeil (1 → 0 over the window).
                    if (isEntering && enterVeil > 0.05f)
                    {
                        Vector2 cMn = pos;
                        Vector2 cMx = pos + new Vector2(cardW, cardH);
                        float chrOff = 3f * s;
                        dl.AddRectFilled(
                            new Vector2(cMn.X - chrOff, cMn.Y),
                            new Vector2(cMx.X - chrOff, cMx.Y),
                            ImGui.ColorConvertFloat4ToU32(
                                Boutique.WithAlpha(Boutique.GlitchMagenta, 0.55f * enterVeil)));
                        dl.AddRectFilled(
                            new Vector2(cMn.X + chrOff, cMn.Y),
                            new Vector2(cMx.X + chrOff, cMx.Y),
                            ImGui.ColorConvertFloat4ToU32(
                                Boutique.WithAlpha(Boutique.GlitchCyan, 0.55f * enterVeil)));
                    }
                    ImGui.SetCursorScreenPos(pos);
                    DrawCard(tile, data, s, cardW, cardH, false,
                        veilAlpha: isEntering ? enterVeil : 0f);
                }

                // Advance the layout cursor past the grid so everything
                // below (footer dummy, edge shadows) lays out correctly.
                int totalRows = (regular.Count + cols - 1) / cols;
                if (totalRows > 0)
                {
                    float gridBottomY = gridStart.Y
                        + totalRows * cardH
                        + (totalRows - 1) * rowGapY;
                    ImGui.SetCursorScreenPos(new Vector2(gridStart.X, gridBottomY));
                }
            }
            else
            {
                // Normal flow - capture each tile's screen position so the
                // next category click has fresh "from" positions to FLIP from.
                for (int i = 0; i < regular.Count; i++)
                {
                    if (i % cols != 0) ImGui.SameLine(0, gap);
                    var pos = ImGui.GetCursorScreenPos();
                    tileLastFramePos[regular[i].Id] = pos;
                    if (celebActive && regular[i].Id == celebId)
                    {
                        // Hero is drawn cinematically as a final overlay -
                        // emit a Dummy of the same size so SameLine targets
                        // the correct next-card position.
                        ImGui.Dummy(new Vector2(cardW, cardH));
                    }
                    else
                    {
                        DrawCard(regular[i], data, s, cardW, cardH, false);
                    }
                }
            }

            // Snapshot the current regular list so the NEXT category click
            // has a "from" list to identify survivors / exiters against.
            lastFrameRegularList = regular;
        }

        if (all.Count == 0)
        {
            ImGui.Spacing(); ImGui.Spacing(); ImGui.Spacing();
            CenterText("No achievements found.", TxDim);
        }

        ImGui.Dummy(new Vector2(0, 12 * s));

        // ── Hero celebration cinematic ──
        // 1:1 with HTML mockup .card.just-unlocked timeline. Drawn LAST
        // (after all grid content, before edge shadows) so the hero card
        // and backdrop sit above other tiles while still letting the
        // scroll-affordance gradients ride on top.
        DrawCelebrationCinematic(data, s);

        // ── Edge shadows - faux drop-shadow on top + bottom of scroll area ──
        // Draw LAST inside the child so the gradient overlays sit on top of any
        // content that would scroll under the edges. Screen coords from
        // GetWindowPos/Size (child-relative, not scrolled). Thin, dark, fading
        // - subtle but felt; gives the scroll region a discernible envelope.
        {
            var childMn = ImGui.GetWindowPos();
            var childSz = ImGui.GetWindowSize();
            var childMx = childMn + childSz;
            var edgeDl = ImGui.GetWindowDrawList();
            float edgeH = 14f * s;
            uint shadow = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f));
            uint clear  = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0f));
            // Top edge: opaque shadow at top → transparent at edgeH down
            edgeDl.AddRectFilledMultiColor(
                new Vector2(childMn.X, childMn.Y),
                new Vector2(childMx.X, childMn.Y + edgeH),
                shadow, shadow, clear, clear);
            // Bottom edge: transparent at edgeH from bottom → opaque shadow at bottom
            edgeDl.AddRectFilledMultiColor(
                new Vector2(childMn.X, childMx.Y - edgeH),
                new Vector2(childMx.X, childMx.Y),
                clear, clear, shadow, shadow);
        }

        ImGui.EndChild();
    }

    private void DrawSectionHeader(string text, float s)
    {
        DrawSectionRule(text, tail: null, featured: false, s);
    }

    // \u2500\u2500 Section rule (HTML .section-rule) \u2500\u2500
    // Label (tracked caps) + horizontal gradient line + optional tail text.
    // `featured` variant prepends a 4px gold bar and uses gold-warm for the
    // label + gold gradient for the line (used for "Almost There"-style hero
    // band above the spotlight grid).
    private void DrawSectionRule(string label, string? tail, bool featured, float s)
    {
        var dl = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();
        float availW = ImGui.GetContentRegionAvail().X;
        float fontH = ImGui.GetFontSize();
        float rowH = 20f * s;
        float cursorX = cursor.X;
        float midY = cursor.Y + rowH * 0.5f;

        // Featured gold bar (4\u00d714)
        if (featured)
        {
            float barW = 4f * s;
            float barH = 14f * s;
            var bMn = new Vector2(cursorX, midY - barH * 0.5f);
            var bMx = bMn + new Vector2(barW, barH);
            // Soft gold halo (approximate box-shadow)
            dl.AddRectFilled(
                new Vector2(bMn.X - 2f * s, bMn.Y - 2f * s),
                new Vector2(bMx.X + 2f * s, bMx.Y + 2f * s),
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0.25f)));
            dl.AddRectFilled(bMn, bMx,
                ImGui.ColorConvertFloat4ToU32(Boutique.Gold));
            cursorX += barW + 12f * s;
        }

        // Label - 10px Oswald 600 @ 0.30em (HTML .section-rule .label) SemiBold face
        var labelCol = featured ? Boutique.GoldWarm : Boutique.TextDim;
        var labelCaps = label.ToUpperInvariant();
        float lblTrk = 10f * s * 0.30f;
        float lblW;
        using (Plugin.Instance?.OswaldSemi10?.Push())
        {
            float lblFontH = ImGui.GetFontSize();
            lblW = Boutique.MeasureTrackedText(labelCaps, lblTrk);
            Boutique.DrawTrackedText(dl,
                new Vector2(cursorX, midY - lblFontH * 0.5f),
                labelCaps, ImGui.ColorConvertFloat4ToU32(labelCol), lblTrk);
        }
        cursorX += lblW + 12f * s;

        // Gradient line
        float lineY = midY;
        float lineEndX = cursor.X + availW;
        // Tail uses Med11 - section-rule metadata ("X spotlighted · Y/Z
        // overall") needs to be readable, not squinty.
        float tailTrk = 11f * s * 0.18f;
        float tailWidth = 0f;
        if (!string.IsNullOrEmpty(tail))
        {
            using (Plugin.Instance?.OswaldMed11?.Push())
                tailWidth = Boutique.MeasureTrackedText(tail, tailTrk);
            lineEndX -= tailWidth + 12f * s;
        }
        uint lineStart, lineMid, lineEnd;
        if (featured)
        {
            lineStart = ImGui.ColorConvertFloat4ToU32(Boutique.GoldDeep);
            lineMid   = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0.15f));
            lineEnd   = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0f));
        }
        else
        {
            lineStart = ImGui.ColorConvertFloat4ToU32(Boutique.Border);
            lineMid   = ImGui.ColorConvertFloat4ToU32(Boutique.BorderSoft);
            lineEnd   = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.BorderSoft, 0f));
        }
        if (lineEndX > cursorX)
        {
            float midX = cursorX + (lineEndX - cursorX) * 0.6f;
            dl.AddRectFilledMultiColor(
                new Vector2(cursorX, lineY),
                new Vector2(midX, lineY + 1f * s),
                lineStart, lineMid, lineMid, lineStart);
            dl.AddRectFilledMultiColor(
                new Vector2(midX, lineY),
                new Vector2(lineEndX, lineY + 1f * s),
                lineMid, lineEnd, lineEnd, lineMid);
        }

        // Tail text (right-aligned) - OswaldMed11 @ 0.18em
        if (!string.IsNullOrEmpty(tail))
        {
            using (Plugin.Instance?.OswaldMed11?.Push())
            {
                float tailH = ImGui.GetFontSize();
                Boutique.DrawTrackedText(dl,
                    new Vector2(lineEndX + 12f * s, midY - tailH * 0.5f),
                    tail, ImGui.ColorConvertFloat4ToU32(Boutique.TextGhost), tailTrk);
            }
        }

        ImGui.Dummy(new Vector2(availW, rowH + 4f * s));
    }

    private void DrawPagination(int totalPages, float s)
    {
        float dotSize = 8 * s;
        float dotGap = 6 * s;
        float totalW = totalPages * dotSize + (totalPages - 1) * dotGap;
        float startX = (ImGui.GetWindowWidth() - totalW) / 2;

        var dl = ImGui.GetWindowDrawList();
        var basePos = ImGui.GetCursorScreenPos();

        for (int i = 0; i < totalPages; i++)
        {
            float x = startX + i * (dotSize + dotGap);
            var centre = new Vector2(basePos.X + x + dotSize / 2, basePos.Y + dotSize / 2 + 2 * s);

            bool active = i == currentPage;
            var col = active ? Gold : new Vector4(0.30f, 0.30f, 0.38f, 0.50f);
            dl.AddCircleFilled(centre, dotSize / 2, ImGui.ColorConvertFloat4ToU32(col));

            ImGui.SetCursorScreenPos(centre - new Vector2(dotSize, dotSize));
            if (ImGui.InvisibleButton($"##page{i}", new Vector2(dotSize * 2, dotSize * 2)))
                currentPage = i;
        }

        ImGui.Dummy(new Vector2(0, dotSize + 8 * s));
    }

    // ═══════════════ ACHIEVEMENT CARD (slip silhouette) ═══════════════
    // HTML .card - 8px chamfered slip (18px for spotlight `isLarge`).
    //   Layers: border polygon → 1px-inset fill polygon (vertical gradient)
    //   → 4px left stripe (stops before BL chamfer) → 2px top bar (stops
    //   before TR chamfer) → 42×42 icon tile w/ corner ticks → meta row
    //   (cat-tag + new-pip if unseen + points top-right) → name (+ bonus
    //   star) → description → state chip (Unlocked·date / N%·In Progress
    //   / Locked / Hidden).
    //   Hover: perimeter streak rotates around silhouette + gilded sheen
    //   sweeps across once (700ms) + border colour deepens.
    private void DrawCard(AchievementDefinition ach, AchievementData data, float s, float w, float h, bool isLarge, float veilAlpha = 0f)
    {
        bool unlocked = data.IsUnlocked(ach.Id);
        bool hidden = ach.IsHidden && !unlocked;
        bool isNew = unlocked && !data.SeenAchievements.Contains(ach.Id);
        float prog = GetProgress(ach);
        if (unlocked) prog = 1f;

        var catMeta = AllCatMeta.FirstOrDefault(c => c.Cat == ach.Category);
        var catCol = catMeta.Color != default ? catMeta.Color : new Vector4(0.5f, 0.5f, 0.5f, 1f);
        string catIcon = catMeta.Icon ?? "";

        float chamfer = (isLarge ? 18f : 8f) * s;
        float padL = 18f * s;
        float padR = 13f * s;
        float padTop = 11f * s;
        float padBot = 12f * s;

        // Hit target. InvisibleButton advances the cursor; CAPTURE where it
        // lands so we can restore it at the end (later ImGui.TextWrapped /
        // SetCursorScreenPos calls inside this method would otherwise leave
        // the cursor far from where the layout expects, which is the bug
        // that caused cards to drift / overlap in the grid).
        var origin = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(origin);
        bool clicked = ImGui.InvisibleButton($"##card_{ach.Id}", new Vector2(w, h));
        var cursorAfterButton = ImGui.GetCursorScreenPos();
        bool hovered = ImGui.IsItemHovered();
        if (isNew && clicked)
            data.SeenAchievements.Add(ach.Id);

        // Smooth scale lerp toward hover target (1.015 hovered, 1.0 idle).
        // Step rate ~0.18 per frame ≈ 100ms approach at 60fps. Avoids the
        // hard pop the previous "step" implementation produced.
        float curScale = cardScales.GetValueOrDefault(ach.Id, 1f);
        float targetScale = hovered ? 1.015f : 1f;
        curScale += (targetScale - curScale) * 0.18f;
        cardScales[ach.Id] = curScale;
        float scale = curScale;
        // Hover-enter detection - drives sheen trigger so it fires on every
        // fresh hover instead of being gated by a cooldown. Suppressed for
        // a brief window after a category/tab change: cards whose Id wasn't
        // drawn last frame have wasHovered defaulting to false, so without
        // this freeze the card under the cursor would falsely fire sheen
        // on the first frame back ("random sheen without cause").
        bool wasHovered = cardWasHovered.GetValueOrDefault(ach.Id, false);
        bool inCatChangeFreeze = (float)ImGui.GetTime() - categoryChangeTime < CategorySheenFreezeSec;
        if (hovered && !wasHovered && !inCatChangeFreeze)
            cardSheenStart[ach.Id] = (float)ImGui.GetTime();
        cardWasHovered[ach.Id] = hovered;

        // Category / tab transitions are now handled by the FLIP cascade
        // in DrawAchievementContent - that path SetCursorScreenPos's each
        // tile to its lerped position before calling DrawCard, and passes
        // veilAlpha for fade-in/out. No per-card transition state needed
        // here; the scatter/scale-settle/stagger logic was removed because
        // it duplicated motion the FLIP cascade now owns.
        cardDrawCounter++;
        float catScaleMul = 1f;
        float catXOffset = 0f;
        float catYOffset = 0f;

        float effectiveScale = scale * catScaleMul;
        float effectiveW = w * effectiveScale;
        float effectiveH = h * effectiveScale;
        var cardMin = new Vector2(
            origin.X - (effectiveW - w) * 0.5f + catXOffset,
            origin.Y - (effectiveH - h) * 0.5f + catYOffset);
        var cardMax = new Vector2(cardMin.X + effectiveW, cardMin.Y + effectiveH);
        float sCham = chamfer * effectiveScale;

        var dl = ImGui.GetWindowDrawList();

        // ── Border polygon (outer layer) ──
        // HTML ::before: color-mix(c 24%, border-soft) normal, c 42% unlocked,
        // c 60% hover. Drawn as a slightly-inflated slip polygon behind the
        // 1px-inset fill polygon.
        float borderMix = hovered ? 0.60f : (unlocked ? 0.42f : (isLarge ? 0.38f : 0.24f));
        var borderCol = Boutique.Lerp(Boutique.BorderSoft, catCol, borderMix);
        Boutique.FillSlip(dl, cardMin, cardMax, sCham,
            ImGui.ColorConvertFloat4ToU32(borderCol));

        // Drop-shadow glow on hover (approximate filter: drop-shadow 14px)
        if (hovered)
        {
            for (int g = 1; g <= 3; g++)
            {
                float off = g * 2.5f * s;
                float ga = 0.18f * (1f - g * 0.28f);
                Boutique.StrokeSlip(dl,
                    new Vector2(cardMin.X - off, cardMin.Y - off),
                    new Vector2(cardMax.X + off, cardMax.Y + off),
                    sCham + off,
                    ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(catCol, ga)), 1f);
            }
        }

        // ── Fill polygon (1px inset inside the border) ──
        // HTML ::after: linear-gradient(180deg, surface-1, surface-0) +
        // radial-gradient at TR (c 10%, transparent 72%). We approximate
        // with a single flat fill at the gradient midpoint (surface-1 and
        // surface-0 differ by only a few percent - visually indistinguishable).
        float inset = 1f * s;
        var fillMin = new Vector2(cardMin.X + inset, cardMin.Y + inset);
        var fillMax = new Vector2(cardMax.X - inset, cardMax.Y - inset);
        float fillCham = Math.Max(0.5f, sCham - inset);
        var bodyMid = Boutique.Lerp(Boutique.Surface1, Boutique.Surface0, 0.5f);
        Boutique.FillSlip(dl, fillMin, fillMax, fillCham,
            ImGui.ColorConvertFloat4ToU32(bodyMid));

        // HTML has a radial gradient at 160px 80px from TR corner (c 10%
        // alpha). Cannot translate to ImGui without a texture - any
        // triangle/polygon approximation reads as a hard shape, which the
        // user explicitly called out as wrong. Omitted in favour of the
        // solid body fill. The slip silhouette + left stripe + top bar
        // carry the visual identity; the corner tint was never critical.

        // ── Left stripe (4px wide, stops before BL chamfer) ──
        // Unlocked = bright cat-colour gradient; locked = dim border grey
        // with just a hint of cat tint. Major visual cue for state.
        {
            float stripeW = 4f * s;
            float stripeBotY = cardMax.Y - sCham;
            Vector4 sTop, sMid, sBot;
            if (unlocked)
            {
                sTop = Boutique.WithAlpha(catCol, 0.70f);
                sMid = catCol;
                sBot = Boutique.WithAlpha(catCol, 0.55f);
            }
            else
            {
                // Locked: mostly border grey with ~15% cat tint, much lower alpha
                var dim = Boutique.Lerp(Boutique.Border, catCol, 0.15f);
                sTop = Boutique.WithAlpha(dim, 0.40f);
                sMid = Boutique.WithAlpha(dim, 0.55f);
                sBot = Boutique.WithAlpha(dim, 0.30f);
            }
            uint stripeTop = ImGui.ColorConvertFloat4ToU32(sTop);
            uint stripeMid = ImGui.ColorConvertFloat4ToU32(sMid);
            uint stripeBot = ImGui.ColorConvertFloat4ToU32(sBot);
            // Vertical gradient via 2 stacked rects
            float stripeMidY = (cardMin.Y + stripeBotY) * 0.5f;
            dl.AddRectFilledMultiColor(
                new Vector2(cardMin.X, cardMin.Y),
                new Vector2(cardMin.X + stripeW, stripeMidY),
                stripeTop, stripeTop, stripeMid, stripeMid);
            dl.AddRectFilledMultiColor(
                new Vector2(cardMin.X, stripeMidY),
                new Vector2(cardMin.X + stripeW, stripeBotY),
                stripeMid, stripeMid, stripeBot, stripeBot);
        }

        // ── Top bar (2px tall, stops before TR chamfer) ──
        // Unlocked = bright cat-colour edge; locked = soft border grey.
        {
            float tbRightX = cardMax.X - sCham;
            uint tbCol = unlocked
                ? ImGui.ColorConvertFloat4ToU32(catCol)
                : ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Border, 0.85f));
            dl.AddRectFilled(
                new Vector2(cardMin.X, cardMin.Y),
                new Vector2(tbRightX, cardMin.Y + 2f * s),
                tbCol);
        }

        // ── Icon tile (42×42 square, corner ticks at TL+BR) ──
        float iconSize = (isLarge ? 48f : 42f) * s;
        var iconMin = new Vector2(cardMin.X + padL, cardMin.Y + padTop);
        var iconMax = iconMin + new Vector2(iconSize, iconSize);
        {
            // Gradient bg: unlocked tints with cat colour; locked is pure
            // surface (no cat tint) so the icon tile reads as inert/dimmed.
            var iconTopMix = unlocked
                ? Boutique.Lerp(Boutique.Surface3, catCol, 0.45f)
                : Boutique.Surface2;
            var iconBotMix = unlocked ? Boutique.Surface2 : Boutique.Surface1;
            uint iconTopU = ImGui.ColorConvertFloat4ToU32(iconTopMix);
            uint iconBotU = ImGui.ColorConvertFloat4ToU32(iconBotMix);
            dl.AddRectFilledMultiColor(iconMin, iconMax,
                iconTopU, iconTopU, iconBotU, iconBotU);

            // Border: cat colour when unlocked, plain border-soft when locked
            var iconBorder = unlocked ? catCol : Boutique.BorderSoft;
            dl.AddRect(iconMin, iconMax,
                ImGui.ColorConvertFloat4ToU32(iconBorder), 0f, ImDrawFlags.None, 1f);

            // Corner ticks - cat colour when unlocked, dim border-soft when
            // locked, so the small L's don't keep advertising the category.
            float tickSize = 5f * s;
            uint tickCol = ImGui.ColorConvertFloat4ToU32(unlocked
                ? Boutique.WithAlpha(catCol, 0.55f)
                : Boutique.WithAlpha(Boutique.BorderSoft, 0.70f));
            // TL: top + left edges
            dl.AddLine(
                new Vector2(iconMin.X - 1, iconMin.Y - 1),
                new Vector2(iconMin.X + tickSize, iconMin.Y - 1), tickCol, 1f);
            dl.AddLine(
                new Vector2(iconMin.X - 1, iconMin.Y - 1),
                new Vector2(iconMin.X - 1, iconMin.Y + tickSize), tickCol, 1f);
            // BR: bottom + right edges
            dl.AddLine(
                new Vector2(iconMax.X - tickSize, iconMax.Y + 1),
                new Vector2(iconMax.X + 1, iconMax.Y + 1), tickCol, 1f);
            dl.AddLine(
                new Vector2(iconMax.X + 1, iconMax.Y - tickSize),
                new Vector2(iconMax.X + 1, iconMax.Y + 1), tickCol, 1f);

            // Icon glyph
            float glyphSize = (isLarge ? 0.82f : 0.72f) * UiBuilder.IconFont.FontSize;
            ImGui.PushFont(UiBuilder.IconFont);
            var glyphNat = ImGui.CalcTextSize(catIcon);
            ImGui.PopFont();
            float glyphScale = glyphSize / UiBuilder.IconFont.FontSize;
            var glyphDrawSz = glyphNat * glyphScale;
            var iconCentre = (iconMin + iconMax) * 0.5f;
            // Unlocked = vibrant cat colour; locked = greyscale (text-faint)
            // so it reads as "not yet earned" rather than dimly tinted.
            var glyphColFinal = unlocked ? catCol : Boutique.TextFaint;
            dl.AddText(UiBuilder.IconFont, glyphSize,
                iconCentre - glyphDrawSz * 0.5f,
                ImGui.ColorConvertFloat4ToU32(glyphColFinal), catIcon);
        }

        // ── Card body (right of icon) ──
        float bodyX = iconMax.X + 13f * s;
        float bodyY = cardMin.Y + padTop;
        float bodyR = cardMax.X - padR;
        float bodyW = bodyR - bodyX;

        // Meta row: cat-tag + new-pip + points (right-aligned)
        // cat-tag: 9px Oswald 500 tracked 0.24em (HTML .cat-tag) Medium face
        string catTag = ach.Category.ToString().ToUpperInvariant();
        float catTrk = 9f * s * 0.24f;
        Vector2 catTagSz;
        float metaY = bodyY;
        using (Plugin.Instance?.OswaldMed9?.Push())
        {
            float catFH = ImGui.GetFontSize();
            float catW = Boutique.MeasureTrackedText(catTag, catTrk);
            catTagSz = new Vector2(catW, catFH);
            // Cat-tag: bright cat-colour when unlocked, dim grey when locked
            // - keeps the category-tag colour from being a constant strong cue
            // that washes out the unlocked-vs-locked distinction.
            var catTagCol = unlocked
                ? Boutique.WithAlpha(catCol, 0.85f)
                : Boutique.WithAlpha(Boutique.TextFaint, 0.85f);
            Boutique.DrawTrackedText(dl,
                new Vector2(bodyX, metaY),
                catTag, ImGui.ColorConvertFloat4ToU32(catTagCol), catTrk);
        }

        float metaCursorX = bodyX + catTagSz.X + 8f * s;

        // Blinking green pip for unseen unlocks
        if (isNew)
        {
            float pipSize = 6f * s;
            float tPip = (float)Boutique.AnimTime(ImGui.GetTime());
            float pipCycle = (tPip % 1.6f) / 1.6f;
            float pipPulse = 0.5f + 0.5f * MathF.Sin(pipCycle * MathF.Tau - MathF.PI * 0.5f);
            float pipA = 0.45f + 0.55f * pipPulse;
            var pipC = new Vector2(metaCursorX + pipSize * 0.5f, metaY + catTagSz.Y * 0.5f);
            dl.AddRectFilled(
                pipC - new Vector2(pipSize * 0.5f, pipSize * 0.5f),
                pipC + new Vector2(pipSize * 0.5f, pipSize * 0.5f),
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Green, pipA)));
            // Soft outer halo (approximates box-shadow: 0 0 5px green)
            dl.AddRectFilled(
                pipC - new Vector2(pipSize * 0.75f, pipSize * 0.75f),
                pipC + new Vector2(pipSize * 0.75f, pipSize * 0.75f),
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Green, 0.30f * pipA)));
        }

        // Points (right-aligned): "+N PTS"
        // HTML .card-pts: Oswald 14px 600 @ 0.06em ; .unit: 8.5px 500 @ 0.22em
        string ptsStr = $"+{ach.Points}";
        string ptsUnit = "PTS";
        float ptsValTrk, ptsUnitTrk;
        Vector2 ptsSz, ptsUnitSz;
        using (Plugin.Instance?.OswaldSemi14?.Push())
        {
            float fh = ImGui.GetFontSize();
            ptsValTrk = fh * 0.06f;
            ptsSz = new Vector2(
                Boutique.MeasureTrackedText(ptsStr, ptsValTrk),
                fh);
        }
        using (Plugin.Instance?.OswaldMed9?.Push())
        {
            float unitFH = ImGui.GetFontSize();
            ptsUnitTrk = unitFH * 0.22f;
            float unitW = Boutique.MeasureTrackedText(ptsUnit, ptsUnitTrk);
            ptsUnitSz = new Vector2(unitW, unitFH);
        }
        float ptsBlockW = ptsSz.X + 3f * s + ptsUnitSz.X;
        float ptsX = bodyR - ptsBlockW;
        // Unlocked = bright Gold (the reward POPS); locked = TextFaint
        // (clearly inactive - points haven't been earned yet).
        uint ptsCol = unlocked
            ? ImGui.ColorConvertFloat4ToU32(Boutique.Gold)
            : ImGui.ColorConvertFloat4ToU32(Boutique.TextFaint);
        uint ptsUnitCol = unlocked
            ? ImGui.ColorConvertFloat4ToU32(Boutique.GoldDeep)
            : ImGui.ColorConvertFloat4ToU32(Boutique.TextGhost);
        // Baseline-align unit to value (unit is smaller, anchor bottoms)
        float unitY = metaY + (ptsSz.Y - ptsUnitSz.Y);
        using (Plugin.Instance?.OswaldSemi14?.Push())
            Boutique.DrawTrackedText(dl, new Vector2(ptsX, metaY),
                ptsStr, ptsCol, ptsValTrk);
        using (Plugin.Instance?.OswaldMed9?.Push())
            Boutique.DrawTrackedText(dl,
                new Vector2(ptsX + ptsSz.X + 3f * s, unitY),
                ptsUnit, ptsUnitCol, ptsUnitTrk);

        // ── Name row ──
        // 15.5px Outfit 600 (HTML .name) - uses OutfitBody15
        float nameY = metaY + catTagSz.Y + 3f * s;
        string nameText = hidden ? "???" : ach.Name;
        float bonusStarReserve = 0f;
        if (ach.IsBonus)
        {
            ImGui.PushFont(UiBuilder.IconFont);
            bonusStarReserve = ImGui.CalcTextSize("").X * 0.85f + 6f * s;
            ImGui.PopFont();
        }
        float nameAvail = bodyR - bodyX - bonusStarReserve;

        // Unlocked = bright text; locked = clearly muted grey (not just
        // slightly dimmed). isLarge spotlight cards stay bright so the
        // "Almost There" section reads as a feature row.
        var nameCol = hidden
            ? Boutique.TextFaint
            : (unlocked || isLarge ? Boutique.Text : Boutique.TextFaint);
        Vector2 nameSz;
        string drawName;
        using (Plugin.Instance?.OutfitSemi15?.Push())
        {
            nameSz = ImGui.CalcTextSize(nameText);
            drawName = nameText;
            if (nameSz.X > nameAvail)
            {
                while (drawName.Length > 1 && ImGui.CalcTextSize(drawName + "...").X > nameAvail)
                    drawName = drawName.Substring(0, drawName.Length - 1);
                drawName += "...";
                nameSz = ImGui.CalcTextSize(drawName);
            }
            dl.AddText(new Vector2(bodyX, nameY),
                ImGui.ColorConvertFloat4ToU32(nameCol), drawName);
        }

        // Bonus star
        if (ach.IsBonus)
        {
            float starFontSz = ImGui.GetFontSize() * 0.95f;
            float starX = bodyX + nameSz.X + 5f * s;
            float starY = nameY + (nameSz.Y - starFontSz) * 0.5f;
            ImGui.PushFont(UiBuilder.IconFont);
            var starNat = ImGui.CalcTextSize("");
            ImGui.PopFont();
            float starScale = starFontSz / UiBuilder.IconFont.FontSize;
            var starDrawSz = starNat * starScale;
            dl.AddText(UiBuilder.IconFont, starFontSz,
                new Vector2(starX, starY + (starFontSz - starDrawSz.Y) * 0.5f),
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.MagentaSft, unlocked ? 1f : 0.85f)),
                "");
        }

        // ── Description (2-line clamp via PushClipRect) ──
        float descY = nameY + nameSz.Y + 3f * s;
        // Reserve room for the progress foot. Spotlight (isLarge) cards use
        // a bigger 13px caption so they need extra vertical reserve; regular
        // cards stay at the original 28px reserve.
        float descMaxY = cardMax.Y - padBot - (isLarge ? 36f : 28f) * s;
        var descText = hidden ? "???" : ach.GetDescriptionFor(unlocked);
        // Unlocked desc = readable TextDim; locked desc = much dimmer
        // TextGhost so it reads as a "preview hint" not active content.
        var descCol = hidden
            ? Boutique.TextGhost
            : (unlocked ? Boutique.TextDim : Boutique.TextGhost);

        // HTML .card-desc is 12px Outfit 400, but reads thin in-game - routing
        // through OutfitMed12 (Medium / 500 weight) restores the legibility the
        // browser gets from its own text rasteriser.
        dl.PushClipRect(new Vector2(bodyX, descY), new Vector2(bodyR, descMaxY), true);
        ImGui.SetCursorScreenPos(new Vector2(bodyX, descY));
        // PushTextWrapPos takes window-LOCAL X, not screen X. Subtract the
        // child's window pos or the wrap point lands off-screen and the text
        // never wraps (just clips at the card edge).
        ImGui.PushTextWrapPos(bodyR - ImGui.GetWindowPos().X);
        ImGui.PushStyleColor(ImGuiCol.Text, descCol);
        using (Plugin.Instance?.OutfitMed12?.Push())
            ImGui.TextUnformatted(descText);
        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();
        dl.PopClipRect();

        // ── Bottom progress bar (HTML design-mockups/06-card-progress-variants Variant A) ──
        // Caption row (Oswald 9px): "X / Total" ratio on the left, state tag on the
        // right. Progress bar (4px) below, track + fill + leading-edge spark. Every
        // card gets the same treatment across all 4 states so the grid rhythm holds.
        {
            bool inProgress = !unlocked && !hidden && prog > 0;

            float fillRatio;
            string? ratioNumStr = null;
            string? ratioTotalStr = null;
            string tagStr;
            Vector4 trackCol, fillStart, fillMid, fillEnd, barBorderCol, numCol, tagCol;

            if (unlocked && data.UnlockedAchievements.TryGetValue(ach.Id, out var unlockTime))
            {
                fillRatio = 1f;
                tagStr = "UNLOCKED · " + unlockTime.ToLocalTime().ToString("MMM. d").ToUpperInvariant();
                tagCol = Boutique.GreenSoft;
                trackCol = Boutique.WithAlpha(Boutique.Green, 0.14f);
                barBorderCol = Boutique.WithAlpha(Boutique.Green, 0.22f);
                fillStart = Boutique.WithAlpha(Boutique.Green, 0.70f);
                fillMid = Boutique.Green;
                fillEnd = Boutique.Lerp(Boutique.Green, new Vector4(1f, 1f, 1f, 1f), 0.15f);
                numCol = Boutique.GreenSoft;
                if (MilestoneTargets.TryGetValue(ach.Id, out int tgt))
                {
                    ratioNumStr = tgt.ToString();
                    ratioTotalStr = tgt.ToString();
                }
            }
            else if (hidden)
            {
                fillRatio = 0f;
                tagStr = "HIDDEN";
                tagCol = Boutique.Violet;
                trackCol = Boutique.WithAlpha(Boutique.Violet, 0.10f);
                barBorderCol = Boutique.WithAlpha(Boutique.Violet, 0.25f);
                fillStart = fillMid = fillEnd = Boutique.Violet;
                numCol = Boutique.Violet;
            }
            else if (inProgress)
            {
                fillRatio = Math.Clamp(prog, 0f, 1f);
                tagStr = "IN PROGRESS";
                tagCol = Boutique.TextFaint;
                trackCol = Boutique.WithAlpha(catCol, 0.14f);
                barBorderCol = Boutique.WithAlpha(catCol, 0.22f);
                fillStart = Boutique.WithAlpha(catCol, 0.70f);
                fillMid = catCol;
                fillEnd = Boutique.Lerp(catCol, new Vector4(1f, 1f, 1f, 1f), 0.15f);
                numCol = catCol;
                if (MilestoneTargets.TryGetValue(ach.Id, out int tgt))
                {
                    int curr = 0;
                    if (ach.Id.StartsWith("char_")) curr = plugin.Characters.Count;
                    else if (ach.Id.StartsWith("design_")) curr = plugin.Characters.Sum(c => c.Designs?.Count ?? 0);
                    ratioNumStr = curr.ToString();
                    ratioTotalStr = tgt.ToString();
                }
                else
                {
                    ratioNumStr = ((int)(prog * 100)).ToString();
                    ratioTotalStr = "100";
                }
            }
            else
            {
                fillRatio = 0f;
                tagStr = "LOCKED";
                tagCol = Boutique.TextFaint;
                trackCol = Boutique.WithAlpha(Boutique.Border, 0.22f);
                barBorderCol = Boutique.WithAlpha(Boutique.Border, 0.35f);
                fillStart = fillMid = fillEnd = Boutique.Slate;
                numCol = Boutique.TextFaint;
            }

            // Layout: caption row + 5px gap + 4px bar
            // BAR spans from the card's left padding (under the icon) all the
            // way across to the right padding - a proper card-wide status bar
            // anchored to the visible left edge of the content. The CAPTION
            // stays inside the body column so the ratio starts where the name
            // starts.
            // Spotlight (isLarge) cards bump the caption fonts 9 → 13 so
            // the ratio "N / Total" and state tag actually read at glance.
            // Regular cards stay at 9px.
            var capSemiFont = isLarge ? Plugin.Instance?.OswaldSemi13 : Plugin.Instance?.OswaldSemi9;
            var capBodyFont = isLarge ? Plugin.Instance?.OswaldBody13 : Plugin.Instance?.OswaldBody9;
            var capMedFont  = isLarge ? Plugin.Instance?.OswaldMed13  : Plugin.Instance?.OswaldMed9;

            float captionH;
            using (capSemiFont?.Push())
                captionH = ImGui.GetFontSize();
            float barH = 4f * s;
            float captionGap = 5f * s;
            float capX = bodyX;                      // caption: body column
            float barX = cardMin.X + padL;           // bar: full content width
            float footR = cardMax.X - padR;
            float footH = captionH + captionGap + barH;
            float footY = cardMax.Y - padBot - footH;
            float captionY = footY;

            // ── Caption: left ratio "N / Total", right state tag ──
            float ratioTrk = 0f, tagTrk = 0f;
            using (capSemiFont?.Push())
            {
                float fh = ImGui.GetFontSize();
                ratioTrk = fh * 0.22f;
                tagTrk = fh * 0.22f;
            }

            if (ratioNumStr != null && ratioTotalStr != null)
            {
                float cursorX = capX;
                using (capSemiFont?.Push())
                {
                    float numW = Boutique.DrawTrackedText(dl, new Vector2(cursorX, captionY),
                        ratioNumStr, ImGui.ColorConvertFloat4ToU32(numCol), ratioTrk);
                    cursorX += numW + 2f * s;
                }
                using (capBodyFont?.Push())
                {
                    float slashW = ImGui.CalcTextSize("/").X;
                    dl.AddText(new Vector2(cursorX, captionY),
                        ImGui.ColorConvertFloat4ToU32(Boutique.TextGhost), "/");
                    cursorX += slashW + 2f * s;
                }
                using (capMedFont?.Push())
                    Boutique.DrawTrackedText(dl, new Vector2(cursorX, captionY),
                        ratioTotalStr, ImGui.ColorConvertFloat4ToU32(Boutique.TextDim), ratioTrk);
            }

            // Right-aligned tag
            using (capSemiFont?.Push())
            {
                float tagW = Boutique.MeasureTrackedText(tagStr, tagTrk);
                Boutique.DrawTrackedText(dl,
                    new Vector2(footR - tagW, captionY),
                    tagStr, ImGui.ColorConvertFloat4ToU32(tagCol), tagTrk);
            }

            // ── Progress bar (spans card-wide from barX to footR) ──
            float barY = captionY + captionH + captionGap;
            var bMn = new Vector2(barX, barY);
            var bMx = new Vector2(footR, barY + barH);
            dl.AddRectFilled(bMn, bMx, ImGui.ColorConvertFloat4ToU32(trackCol));
            dl.AddRect(bMn, bMx, ImGui.ColorConvertFloat4ToU32(barBorderCol),
                0f, ImDrawFlags.None, 1f);

            if (fillRatio > 0f)
            {
                float trackW = footR - barX;
                float fillW = trackW * fillRatio;
                float fillMidX = barX + fillW * 0.6f;
                uint uStart = ImGui.ColorConvertFloat4ToU32(fillStart);
                uint uMid   = ImGui.ColorConvertFloat4ToU32(fillMid);
                uint uEnd   = ImGui.ColorConvertFloat4ToU32(fillEnd);
                // Left half: start → mid
                dl.AddRectFilledMultiColor(
                    new Vector2(barX, bMn.Y),
                    new Vector2(fillMidX, bMx.Y),
                    uStart, uMid, uMid, uStart);
                // Right half: mid → end (brighter peak at leading edge)
                dl.AddRectFilledMultiColor(
                    new Vector2(fillMidX, bMn.Y),
                    new Vector2(barX + fillW, bMx.Y),
                    uMid, uEnd, uEnd, uMid);

                // Spark at the leading edge, pulsing on a 1.6s loop
                if (inProgress)
                {
                    float t = (float)Boutique.AnimTime(ImGui.GetTime());
                    float cycle = (t % 1.6f) / 1.6f;
                    float pulse = 0.4f + 0.55f * (0.5f + 0.5f * MathF.Sin(cycle * MathF.Tau - MathF.PI * 0.5f));
                    float sparkX = barX + fillW;
                    // Glow halo (wider, lower alpha, cat-tinted)
                    dl.AddRectFilled(
                        new Vector2(sparkX - 3f * s, bMn.Y - 2f * s),
                        new Vector2(sparkX + 3f * s, bMx.Y + 2f * s),
                        ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(catCol, 0.35f * pulse)));
                    // Bright core (white 2×8)
                    dl.AddRectFilled(
                        new Vector2(sparkX - 1f * s, bMn.Y - 2f * s),
                        new Vector2(sparkX + 1f * s, bMx.Y + 2f * s),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, pulse)));
                }
            }
        }

        // ── Perimeter streak - only while actively hovered ──
        if (hovered)
        {
            DrawCardPerimeterStreak(dl, cardMin, cardMax, sCham, catCol, ach.Id, s);
        }
        else
        {
            // Drop stale particles so re-hovering a card starts fresh
            // instead of briefly re-rendering ghost sparkles.
            cardSparkles.Remove(ach.Id);
        }

        // ── Sheen - gated on `hovered` so it stops the moment the cursor
        // leaves. Previously it played out for its full 1.4s duration after
        // hover-enter, which read as "random shimmer while idle" if the
        // cursor merely grazed a card during scrolling. Now: cause persists
        // → effect persists. Drop the entry on un-hover so a re-enter starts
        // a fresh sweep instead of resuming mid-animation. ──
        if (hovered && cardSheenStart.TryGetValue(ach.Id, out var sheenStart))
        {
            float elapsed = (float)ImGui.GetTime() - sheenStart;
            if (elapsed >= 0f && elapsed <= 1.4f)
                DrawCardGildedSheen(dl, cardMin, cardMax, sCham, catCol, ach.Id, s);
        }
        else if (!hovered)
        {
            cardSheenStart.Remove(ach.Id);
        }

        // ── Just-unlocked celebration flair ──
        // The FX cascade is now drawn by DrawCelebrationCinematic anchored to
        // the hero's CURRENT cinematic position (not its grid slot), since the
        // hero moves rise → hold → land. Per-card slot-anchored FX would be
        // misaligned. Nothing to draw here.

        // ── FLIP transition veil ──
        // Drawn LAST so it covers all card chrome. Used by the filter
        // cascade in DrawAchievementContent to fade exiters out and
        // enterers in at their respective positions. Bg-coloured rect
        // matched to the card's chamfered slip silhouette so the fade
        // doesn't show square corners against the surrounding chrome.
        if (veilAlpha > 0f)
        {
            float aClamped = MathF.Min(1f, veilAlpha);
            uint veilU = ImGui.ColorConvertFloat4ToU32(
                Boutique.WithAlpha(Boutique.Bg, aClamped));
            Boutique.FillSlip(dl, cardMin, cardMax, sCham, veilU);
        }

        // Reset the "last item" rect to the card so the parent grid's SameLine
        // positions the next card relative to this card and not the description text
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(w, h));

        // ── Tooltip ──
        if (hovered && !hidden)
        {
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.05f, 0.05f, 0.10f, 0.97f));
            ImGui.PushStyleColor(ImGuiCol.Border, A(catCol, 0.35f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8));
            ImGui.BeginTooltip();
            ImGui.TextColored(TxBright, ach.Name);
            if (!string.IsNullOrWhiteSpace(ach.FlavourText))
                ImGui.TextColored(A(catCol, 0.65f), $"\"{ach.FlavourText}\"");
            ImGui.Spacing();
            ImGui.TextColored(TxMid, ach.GetDescriptionFor(unlocked));
            if (!unlocked && !string.IsNullOrWhiteSpace(ach.Hint))
            {
                ImGui.Spacing();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextColored(A(catCol, 0.85f), "");
                ImGui.PopFont();
                ImGui.SameLine(0, 6);
                ImGui.TextColored(A(catCol, 0.85f), ach.Hint);
            }
            ImGui.Spacing();
            ImGui.TextColored(Gold, $"+{ach.Points} pts");
            ImGui.SameLine(0, 10);
            ImGui.TextColored(A(catCol, 0.65f), $"{ach.Category}");
            if (ach.IsBonus)
            {
                ImGui.Spacing();
                var bonusCol = new Vector4(1.00f, 0.55f, 0.85f, 1f);
                ImGui.TextColored(bonusCol, "Bonus achievement");
                ImGui.TextColored(A(TxDim, 0.85f), "Not required for 100%.");
            }
            ImGui.EndTooltip();
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
        }
    }

    // Rotating perimeter streak around the card's slip silhouette.
    // Beefed up: longer trail, brighter peak with white-hot core, fading
    // sparkle drops behind the head, larger halo.
    private void DrawCardPerimeterStreak(ImDrawListPtr dl, Vector2 mn, Vector2 mx, float chamfer, Vector4 catCol, string id, float s)
    {
        if (Boutique.ReduceMotion) return;
        float t = (float)ImGui.GetTime();
        float headFrac = (t / 3.0f) % 1f;        // slightly faster lap (3.0s)
        const int segments = 80;
        float segStep = 0.40f / segments;        // longer 40% trail (was 28%)

        for (int i = 0; i < segments; i++)
        {
            float p1 = (headFrac - i * segStep + 1f) % 1f;
            float p2 = (headFrac - (i + 1) * segStep + 1f) % 1f;
            var a = Boutique.WalkSlipPerimeter(p1, mn, mx, chamfer);
            var b = Boutique.WalkSlipPerimeter(p2, mn, mx, chamfer);
            float tailT = i / (float)segments;

            // Brighten the leading 12% of the trail with a white-hot blend.
            Vector4 segCol;
            float alpha;
            if (tailT < 0.12f)
            {
                float bri = 1f - (tailT / 0.12f);                // 1 at head, 0 at 12%
                segCol = Boutique.Lerp(catCol, new Vector4(1f, 1f, 1f, 1f), bri * 0.7f);
                alpha = 0.95f - tailT * 1.0f;
            }
            else
            {
                segCol = catCol;
                float td = (tailT - 0.12f) / 0.88f;
                alpha = (1f - td) * (1f - td) * 0.70f;
            }
            float thickness = (1f - tailT * 0.55f) * 2.2f * s;
            uint col = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(segCol, alpha));
            dl.AddLine(a, b, col, thickness);
        }

        // Bright head: layered halo from outer category-tint to white-hot core
        var headPt = Boutique.WalkSlipPerimeter(headFrac, mn, mx, chamfer);
        dl.AddCircleFilled(headPt, 6f * s,
            ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(catCol, 0.18f)), 16);
        dl.AddCircleFilled(headPt, 4f * s,
            ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(catCol, 0.55f)), 14);
        dl.AddCircleFilled(headPt, 2.4f * s,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.85f)), 12);
        dl.AddCircleFilled(headPt, 1.2f * s,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1.0f)), 10);

        // Sparkle drops - every ~70ms shed a small fading particle behind the head.
        if (!cardSparkles.TryGetValue(id, out var sparkles))
        {
            sparkles = new List<(Vector2, float)>();
            cardSparkles[id] = sparkles;
        }
        // Add new sparkle if enough time has passed since last one
        float lastBorn = sparkles.Count > 0 ? sparkles[^1].born : 0f;
        if (t - lastBorn > 0.07f)
        {
            // Position along the trail, slightly behind the head (~3% back)
            var sparkPt = Boutique.WalkSlipPerimeter(
                (headFrac - 0.03f + 1f) % 1f, mn, mx, chamfer);
            // Random outward jitter for sparkle vibes
            float jitter = ((id.GetHashCode() ^ (int)(t * 1000)) & 0xFF) / 255f;
            float jx = (jitter - 0.5f) * 1.6f * s;
            float jy = ((jitter * 17.31f) % 1f - 0.5f) * 1.6f * s;
            sparkles.Add((sparkPt + new Vector2(jx, jy), t));
        }
        // Render + cull (lifetime 0.6s)
        const float sparkLife = 0.6f;
        for (int i = sparkles.Count - 1; i >= 0; i--)
        {
            float age = t - sparkles[i].born;
            if (age > sparkLife) { sparkles.RemoveAt(i); continue; }
            float u = age / sparkLife;
            float a = (1f - u) * 0.9f;
            float r = (1f - u * 0.5f) * 1.2f * s;
            dl.AddCircleFilled(sparkles[i].pos, r,
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(catCol, a)), 8);
            dl.AddCircleFilled(sparkles[i].pos, r * 0.5f,
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, a * 0.6f)), 8);
        }
    }

    // Gilded sheen sweep on hover - warm gold approach, white peak, category
    // bleed tail. Fires once per hover-enter (700ms), repeats if cursor leaves
    // and re-enters. Single AddRectFilledMultiColor pass clipped to silhouette.
    private readonly Dictionary<string, float> cardSheenStart = new();
    // Per-card hover state - drives smooth scale lerp + sheen hover-enter trigger.
    private readonly Dictionary<string, float> cardScales = new();
    private readonly Dictionary<string, bool>  cardWasHovered = new();
    // Per-card sparkle trail particles for the perimeter streak.
    private readonly Dictionary<string, List<(Vector2 pos, float born)>> cardSparkles = new();
    // Category filter transition timestamp. Far in the past so first-open
    // is fully settled (no slide-in on window first show).
    private float categoryChangeTime = -100f;
    // Index of the category we're transitioning FROM. -1 = no transition,
    // first-open settled state. Set on click before selectedCategoryIndex
    // changes so the underline can lerp old → new positions.
    private int   categoryFromIndex  = -1;
    // Per-frame cache of category cell rects, used by the animated active
    // underline so it can lerp between any two cells without re-measuring.
    private struct CatCellRect { public Vector2 Min; public Vector2 Max; public Vector4 Color; }
    private readonly List<CatCellRect> categoryCellRects = new();
    // Encore-style page-dot transition duration (HelpWindow.TransitionSec).
    private const float CategoryTransitionSec = 0.34f;
    // Sheen-trigger freeze window after a category/tab change. Cards whose
    // Id wasn't drawn last frame default cardWasHovered=false, so on the
    // first frame after a switch the card under the cursor would falsely
    // register a fresh hover-enter and play sheen "without cause".
    private const float CategorySheenFreezeSec = 0.5f;

    // ── Glitch-flair event timestamps ──
    // Each is set on a discrete user event; the corresponding FX block
    // checks (now - timestamp) < window and renders chromatic flash /
    // scanline wipe / speck burst layered on top of the resting visuals.
    // Once the window passes, the visuals settle clean (gold/cat-coloured).
    private float subbarChangeTime         = -100f;   // tab switch (Achievements ↔ Shop)
    private float searchFocusTransitionTime = -100f;  // search pill focus-enter
    private bool  searchFocusedPrev         = false;
    // Hero-stats-band counter changes (Core / Bonus / Points). Each value
    // is tracked across frames; on change, the matching timestamp updates
    // and the next render draws chromatic-split ghost text for the flair
    // window. -1 = uninitialised (don't fire on first frame).
    private int   lastCoreValue       = -1;
    private int   lastBonusValue      = -1;
    private int   lastPointsValue     = -1;
    private float coreChangeTime      = -100f;
    private float bonusChangeTime     = -100f;
    private float pointsChangeTime    = -100f;
    // Window each event holds the chromatic flair for
    private const float GlitchFlashWindow = 0.30f;

    // ── FLIP-style filter cascade transition state ──
    // When the user switches categories, surviving tiles animate from their
    // OLD screen position to their NEW screen position (slide), exiters
    // fade out at their old position, enterers fade in at their new position.
    // Updated every frame for every visible tile so we always have the
    // most-recent-frame positions to snapshot when a click happens.
    private readonly Dictionary<string, Vector2> tileLastFramePos = new();
    // Snapshot of tileLastFramePos taken at the moment of category click.
    private Dictionary<string, Vector2> tileOldPos = new();
    // Snapshot of the regular-grid list visible at the moment of click.
    // Drives identification of survivors (in both) vs exiters (prev only).
    private List<AchievementDefinition>? prevTileList;
    // Latest regular-grid list. Captured every frame so the click handler
    // can snapshot whatever was visible *just before* the click.
    private List<AchievementDefinition>? lastFrameRegularList;
    // Total cascade duration. Survivors slide for this whole window.
    private const float TileTransitionDur = 0.28f;
    // Exiters fade out over the FIRST portion (alpha 0→1 veil).
    private const float TileExitDur       = 0.15f;
    // Enterers fade in over the LAST portion, delayed so exiters clear
    // first (veil 1→0 starting at TileEnterDelay).
    private const float TileEnterDelay    = 0.13f;
    private const float TileEnterDur      = 0.15f;
    // Per-frame card draw index (reset at top of DrawAchievementContent).
    private int cardDrawCounter = 0;
    // Shop ring fill animation start time (tab-open).
    private float shopFirstShownTime = -100f;
    // Initialized to default tab so first-frame doesn't trigger transition.
    private int   lastTabIndex       = 0;

    /// <summary>
    /// Public hook for /select testunlock - sets a flag picked up at the
    /// top of Draw on the next frame so ImGui.GetTime is always called
    /// inside a frame. No-op if a celebration is already running.
    /// </summary>
    public void TriggerTestCelebration() { celebTestRequested = true; celebTestRequestedId = null; }

    /// <summary>
    /// Variant of TriggerTestCelebration that targets a specific achievement
    /// id (e.g. /select testnice → "char_69"). Falls back to the default
    /// "first unlocked / first registered" picker if the id isn't found.
    /// </summary>
    public void TriggerTestCelebrationFor(string achievementId)
    {
        celebTestRequested = true;
        celebTestRequestedId = achievementId;
    }
    private string? celebTestRequestedId = null;

    // Queue for /select testunlock N - picked up one at a time at the top of
    // Draw whenever celebId is free, so multiple test celebrations stagger
    // naturally just like real missed unlocks.
    private readonly Queue<string> celebTestQueue = new();

    /// <summary>
    /// Public hook for /select testunlock N - enqueues up to <paramref name="count"/>
    /// already-unlocked achievements to fire celebrations sequentially.
    /// Returns the number actually queued (capped by available unlocked achievements).
    /// </summary>
    public int TriggerTestCelebrations(int count)
    {
        if (count <= 0) return 0;
        var data = plugin.Configuration.AchievementData;
        var ids = data.UnlockedAchievements.Keys
            .Where(id => AchievementRegistry.Get(id) != null)
            .Take(count)
            .ToList();
        // Fall back to registry order if the user has nothing unlocked yet
        if (ids.Count == 0)
        {
            ids = AchievementRegistry.All.Select(a => a.Id).Take(count).ToList();
        }
        foreach (var id in ids)
            celebTestQueue.Enqueue(id);
        return ids.Count;
    }

    // ── Easing helpers ──
    // cubic-bezier(0.34, 1.35, 0.42, 1) - springy, overshoots ~10% then settles.
    // Standard easeOutBack approximation (visually indistinguishable for our use).
    private static float SpringEaseOut(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * MathF.Pow(t - 1f, 3f) + c1 * MathF.Pow(t - 1f, 2f);
    }
    // cubic-bezier(0.32, 0.05, 0.2, 1) - smooth ease-out used by HTML's LAND.
    private static float EaseOutCubic(float t) => 1f - MathF.Pow(1f - t, 3f);

    // Deterministic hash combiner for procedural glitch FX layout (chunks,
    // specks, dropout-bar Y positions, etc). Same inputs → same outputs, so
    // each celebration ID gets a stable but unique FX arrangement.
    private static int HashCombine(int a, int b)
    {
        unchecked
        {
            uint h = (uint)a;
            h ^= (uint)b + 0x9E3779B9u + (h << 6) + (h >> 2);
            return (int)h;
        }
    }

    /// <summary>
    /// Hero celebration "ASCENSION" cinematic (1:1 with 02-celebration-alt
    /// mockup). Card stays at NATURAL SIZE throughout - no font/glyph scaling,
    /// no blurry text. Hero motion + framing FX cascade carry the impact.
    /// </summary>
    private void DrawCelebrationCinematic(AchievementData data, float s)
    {
        if (celebId == null || celebStart < 0f) return;
        float t = (float)ImGui.GetTime() - celebStart;
        if (t < 0f || t > CelebDur) return;

        var heroDef = AchievementRegistry.All.FirstOrDefault(a => a.Id == celebId);
        if (heroDef == null) return;
        if (celebGridCardW <= 0f || celebGridCardH <= 0f) return;

        // Snapshot origin (slot K) on the first cinematic frame. If we
        // never tracked the hero (e.g. it was on a category not currently
        // shown when /select testunlock fired), fall back to slot 0.
        if (!celebOriginCaptured)
        {
            celebOriginPos = tileLastFramePos.TryGetValue(celebId, out var p)
                ? p : celebSlot0Pos;
            celebOriginCaptured = true;
        }

        var dl = ImGui.GetWindowDrawList();

        // ── Backdrop dim envelope (mockup .backdrop keyframes) ──
        //   0%-13%  fade in 0 → 1
        //   13%-73% hold at 1
        //   73%-100% fade out 1 → 0
        float dimEnv;
        if (t < CelebAwakenEnd)        dimEnv = t / CelebAwakenEnd;
        else if (t > CelebHoldEnd)     dimEnv = 1f - (t - CelebHoldEnd) / (CelebDur - CelebHoldEnd);
        else                           dimEnv = 1f;
        dimEnv = MathF.Min(1f, MathF.Max(0f, dimEnv));
        if (dimEnv > 0f)
        {
            // SPOTLIGHT vignette - much stronger than before so the hero
            // genuinely "pops" out of the obscured grid. Base 60% alpha
            // black, corner gradients push to 85%. Surrounding cards become
            // ghostly silhouettes, focusing all attention on the centre.
            uint baseCol = ImGui.ColorConvertFloat4ToU32(
                new Vector4(4f / 255f, 5f / 255f, 10f / 255f, 0.60f * dimEnv));
            uint cornerCol = ImGui.ColorConvertFloat4ToU32(
                new Vector4(4f / 255f, 5f / 255f, 10f / 255f, 0.85f * dimEnv));
            uint clearCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0f));
            dl.AddRectFilled(celebViewportMin, celebViewportMax, baseCol);
            var ctr = (celebViewportMin + celebViewportMax) * 0.5f;
            dl.AddRectFilledMultiColor(celebViewportMin, ctr,
                cornerCol, clearCol, clearCol, clearCol);
            dl.AddRectFilledMultiColor(
                new Vector2(ctr.X, celebViewportMin.Y), new Vector2(celebViewportMax.X, ctr.Y),
                clearCol, cornerCol, clearCol, clearCol);
            dl.AddRectFilledMultiColor(
                new Vector2(celebViewportMin.X, ctr.Y), new Vector2(ctr.X, celebViewportMax.Y),
                clearCol, clearCol, clearCol, cornerCol);
            dl.AddRectFilledMultiColor(ctr, celebViewportMax,
                clearCol, clearCol, cornerCol, clearCol);
        }

        // ── Hero motion: AWAKEN → ASCENT (arc) → HOLD → DESCENT (ease-in) ──
        // Track the card's CENTRE position; derive top-left from natural size.
        Vector2 originCentre = celebOriginPos + new Vector2(celebGridCardW * 0.5f, celebGridCardH * 0.5f);
        Vector2 holdCentre   = (celebViewportMin + celebViewportMax) * 0.5f;
        Vector2 landCentre   = celebSlot0Pos + new Vector2(celebGridCardW * 0.5f, celebGridCardH * 0.5f);

        Vector2 currentCentre;
        if (t < CelebAwakenEnd)
        {
            // AWAKEN: hold at origin (slot K)
            currentCentre = originCentre;
        }
        else if (t < CelebAscentEnd)
        {
            // ASCENT: arc rise from origin to viewport centre. Arc shape
            // = linear lerp + a sin-weighted upward bow at the apex.
            float u = (t - CelebAwakenEnd) / (CelebAscentEnd - CelebAwakenEnd);
            float eased = EaseOutCubic(u);
            Vector2 lerped = new Vector2(
                originCentre.X + (holdCentre.X - originCentre.X) * eased,
                originCentre.Y + (holdCentre.Y - originCentre.Y) * eased);
            float arcLift = MathF.Sin(eased * MathF.PI) * 40f * s;  // 40px apex bow
            currentCentre = new Vector2(lerped.X, lerped.Y - arcLift);
        }
        else if (t < CelebHoldEnd)
        {
            // HOLD: park at viewport centre for the FX cascade
            currentCentre = holdCentre;
        }
        else
        {
            // DESCENT: ease-IN from centre to slot 0 (accelerating drop)
            float u = (t - CelebHoldEnd) / (CelebDur - CelebHoldEnd);
            float eased = u * u * u;  // cubic ease-in
            currentCentre = new Vector2(
                holdCentre.X + (landCentre.X - holdCentre.X) * eased,
                holdCentre.Y + (landCentre.Y - holdCentre.Y) * eased);
        }

        Vector2 topLeft = new Vector2(
            currentCentre.X - celebGridCardW * 0.5f,
            currentCentre.Y - celebGridCardH * 0.5f);

        // ── FX cascade (anchored at hero's current centre) ──
        // Drawn BEFORE the hero so the card sits on top of rays/rings/etc,
        // with the hero card's chrome remaining the focal point.
        var heroCatMeta = AllCatMeta.FirstOrDefault(c => c.Cat == heroDef.Category);
        var heroCatCol = heroCatMeta.Color != default ? heroCatMeta.Color : Boutique.Gold;
        DrawCelebrationFlair(dl, t, currentCentre, heroCatCol, s);

        // ── Chromatic RGB-split ghost behind the hero card ──
        // Magenta ghost offset left/up, cyan ghost offset right/down. The
        // real card draws on top a moment later. With small per-frame
        // X-jitter it reads as a TV signal split / digital glitch instead
        // of a gold halo. Replaces the gold drop-shadow + 4-layer halo.
        float chromaEnv;
        if (t < CelebAwakenEnd)        chromaEnv = 0f;
        else if (t < CelebAscentEnd)   chromaEnv = (t - CelebAwakenEnd) / (CelebAscentEnd - CelebAwakenEnd);
        else if (t < CelebHoldEnd)     chromaEnv = 1f;
        else                           chromaEnv = MathF.Max(0f, 1f - (t - CelebHoldEnd) / (CelebDur - CelebHoldEnd));
        if (chromaEnv > 0.02f)
        {
            // Per-frame jitter: small pseudo-random offset on top of the base
            // split distance so the ghosts shimmer rather than sit static.
            float jit = MathF.Sin(t * 47.3f) * 1.5f * s;
            float baseOff = 4f * s + jit;
            uint magCol = ImGui.ColorConvertFloat4ToU32(
                Boutique.WithAlpha(Boutique.GlitchMagenta, 0.55f * chromaEnv));
            uint cyanCol = ImGui.ColorConvertFloat4ToU32(
                Boutique.WithAlpha(Boutique.GlitchCyan, 0.55f * chromaEnv));
            // Magenta ghost offset left + slightly up
            dl.AddRectFilled(
                new Vector2(topLeft.X - baseOff, topLeft.Y - 1f * s),
                new Vector2(topLeft.X + celebGridCardW - baseOff, topLeft.Y + celebGridCardH - 1f * s),
                magCol);
            // Cyan ghost offset right + slightly down
            dl.AddRectFilled(
                new Vector2(topLeft.X + baseOff, topLeft.Y + 1f * s),
                new Vector2(topLeft.X + celebGridCardW + baseOff, topLeft.Y + celebGridCardH + 1f * s),
                cyanCol);
            // Subtle dark backdrop directly behind the card so the chromatic
            // ghosts don't bleed through and confuse the silhouette
            dl.AddRectFilled(topLeft,
                new Vector2(topLeft.X + celebGridCardW, topLeft.Y + celebGridCardH),
                ImGui.ColorConvertFloat4ToU32(
                    new Vector4(0f, 0f, 0f, 0.85f * chromaEnv)));
        }

        // ── Chromatic chunk burst at HOLD start (1100ms, 350ms) ──
        // Small mag/cyan rectangles materialise around the hero - looks like
        // a digital signal corrupting in. Replaces the gold spotlight halo.
        if (t >= 1.10f && t < 1.45f)
        {
            float bp = (t - 1.10f) / 0.35f;
            float burstA = bp < 0.25f ? bp / 0.25f : 1f - (bp - 0.25f) / 0.75f;
            burstA = MathF.Max(0f, burstA);
            if (burstA > 0.02f)
            {
                int chunkCount = 22;
                int seed = celebId?.GetHashCode() ?? 0;
                for (int i = 0; i < chunkCount; i++)
                {
                    // Hash-based deterministic chunk layout
                    int h1 = HashCombine(seed, i * 7919);
                    int h2 = HashCombine(seed, i * 6151 + 17);
                    int h3 = HashCombine(seed, i * 4093 + 31);
                    int h4 = HashCombine(seed, i * 2099 + 53);
                    // Position around the card area (extends ~120px outside)
                    float angle = (h1 & 0xFFFF) / 65535f * MathF.Tau;
                    float dist  = ((h2 & 0xFFFF) / 65535f) * 100f * s + 30f * s;
                    float cx = currentCentre.X + MathF.Cos(angle) * dist;
                    float cy = currentCentre.Y + MathF.Sin(angle) * dist;
                    float cw = ((h3 & 0xFF) / 255f) * 18f * s + 6f * s;
                    float ch = ((h4 & 0xFF) / 255f) * 4f * s + 2f * s;
                    bool isMag = (h1 & 1) == 0;
                    var col = isMag ? Boutique.GlitchMagenta : Boutique.GlitchCyan;
                    dl.AddRectFilled(
                        new Vector2(cx - cw * 0.5f, cy - ch * 0.5f),
                        new Vector2(cx + cw * 0.5f, cy + ch * 0.5f),
                        ImGui.ColorConvertFloat4ToU32(
                            Boutique.WithAlpha(col, 0.85f * burstA)));
                }
            }
        }

        // ── Big points-value display above the card (1500ms, 1400ms) ──
        // Replaces the trophy/crown glyph (which didn't render - Dalamud's
        // IconFont atlas doesn't include those FA glyphs). Uses native-size
        // OswaldSemiLarge (28px) + OswaldSemiMid (22px) so text stays crisp.
        // Tells the player what they EARNED, with a 4-layer halo behind it
        // for spotlight presence.
        if (t >= 1.50f && t < 2.90f)
        {
            float ct = t - 1.50f;
            float cp = ct / 1.40f;
            float pointsOpacity, pointsYOff;
            if (cp < 0.25f)
            {
                float pp = cp / 0.25f;
                pointsOpacity = pp; pointsYOff = 20f * (1f - pp);
            }
            else if (cp < 0.75f)
            {
                pointsOpacity = 1f; pointsYOff = -(cp - 0.25f) / 0.50f * 8f;
            }
            else
            {
                float pp = (cp - 0.75f) / 0.25f;
                pointsOpacity = 1f - pp; pointsYOff = -8f - pp * 12f;
            }
            pointsOpacity = MathF.Max(0f, pointsOpacity);
            if (pointsOpacity > 0.02f)
            {
                string ptsStr  = "+" + heroDef.Points;
                string unitStr = "PTS";
                Vector2 ptsSz, unitSz;
                float ptsTrk = 0f, unitTrk = 0f;
                using (Plugin.Instance?.OswaldSemiLarge?.Push())
                {
                    float fh = ImGui.GetFontSize();
                    ptsTrk = fh * 0.06f;
                    ptsSz = new Vector2(Boutique.MeasureTrackedText(ptsStr, ptsTrk), fh);
                }
                using (Plugin.Instance?.OswaldSemiMid?.Push())
                {
                    float fh = ImGui.GetFontSize();
                    unitTrk = fh * 0.22f;
                    unitSz = new Vector2(Boutique.MeasureTrackedText(unitStr, unitTrk), fh);
                }
                float gap = 8f * s;
                float totalW = ptsSz.X + gap + unitSz.X;
                float blockX = currentCentre.X - totalW * 0.5f;
                float blockY = currentCentre.Y - 105f * s + pointsYOff * s - ptsSz.Y * 0.5f;
                // Baseline-align "PTS" to the bottom of the bigger number
                float unitY = blockY + (ptsSz.Y - unitSz.Y);
                Vector2 blockCentre = new Vector2(currentCentre.X, blockY + ptsSz.Y * 0.5f);

                // CHROMATIC RGB-SPLIT TEXT - magenta ghost left, cyan ghost
                // right, white centre. With per-frame jitter it reads as a
                // glitched broadcast signal, matching the toast aesthetic.
                // No gold halo (drops the "regal" feel completely).
                float chrJit = MathF.Sin(t * 53.7f) * 0.8f * s;
                float chrOff = 3f * s + chrJit;
                uint magU = ImGui.ColorConvertFloat4ToU32(
                    Boutique.WithAlpha(Boutique.GlitchMagenta, 0.85f * pointsOpacity));
                uint cyanU = ImGui.ColorConvertFloat4ToU32(
                    Boutique.WithAlpha(Boutique.GlitchCyan, 0.85f * pointsOpacity));
                uint whiteU = ImGui.ColorConvertFloat4ToU32(
                    Boutique.WithAlpha(new Vector4(1f, 1f, 1f, 1f), pointsOpacity));

                // Draw "+N" 3 times (mag offset, cyan offset, white centre)
                using (Plugin.Instance?.OswaldSemiLarge?.Push())
                {
                    Boutique.DrawTrackedText(dl,
                        new Vector2(blockX - chrOff, blockY), ptsStr, magU, ptsTrk);
                    Boutique.DrawTrackedText(dl,
                        new Vector2(blockX + chrOff, blockY), ptsStr, cyanU, ptsTrk);
                    Boutique.DrawTrackedText(dl,
                        new Vector2(blockX, blockY), ptsStr, whiteU, ptsTrk);
                }
                // "PTS" gets the same treatment, baseline-aligned to the "+N"
                using (Plugin.Instance?.OswaldSemiMid?.Push())
                {
                    float ux = blockX + ptsSz.X + gap;
                    Boutique.DrawTrackedText(dl,
                        new Vector2(ux - chrOff, unitY), unitStr, magU, unitTrk);
                    Boutique.DrawTrackedText(dl,
                        new Vector2(ux + chrOff, unitY), unitStr, cyanU, unitTrk);
                    Boutique.DrawTrackedText(dl,
                        new Vector2(ux, unitY), unitStr, whiteU, unitTrk);
                }
            }
        }

        // ── Render hero at natural size at the cinematic position ──
        ImGui.SetCursorScreenPos(topLeft);
        DrawCard(heroDef, data, s, celebGridCardW, celebGridCardH, false);

        // ── "ACHIEVEMENT UNLOCKED" tracked-caps banner below the card ──
        // Appears during HOLD with a fade in/out. Tells the user what's
        // happening since we can't scale the card text up to dominate.
        if (t >= CelebAscentEnd && t < CelebHoldEnd + 0.3f)
        {
            float bannerLocal = t - CelebAscentEnd;
            float bannerWindow = (CelebHoldEnd + 0.3f) - CelebAscentEnd;
            float bp = bannerLocal / bannerWindow;
            float bannerA = bp < 0.15f ? bp / 0.15f : (bp > 0.85f ? (1f - bp) / 0.15f : 1f);
            bannerA = MathF.Min(1f, MathF.Max(0f, bannerA));
            if (bannerA > 0.02f)
            {
                // Chromatic-split system label below the card. Magenta + cyan
                // ghosts under white centre text. Tight dark backdrop pill
                // for legibility, magenta + cyan border lines top/bottom
                // (mirrors the toast's dropout-bar fringe vocabulary).
                string label = "SYSTEM // ACHIEVEMENT GRANTED";
                using (Plugin.Instance?.OswaldSemi14?.Push())
                {
                    float trk = ImGui.GetFontSize() * 0.32f;
                    float labelW = Boutique.MeasureTrackedText(label, trk);
                    float labelH = ImGui.GetFontSize();
                    float bannerY = topLeft.Y + celebGridCardH + 22f * s;
                    Vector2 labelPos = new Vector2(currentCentre.X - labelW * 0.5f, bannerY);
                    float padX = 14f * s;
                    float padY = 6f * s;
                    // Hard black backdrop (no warmth - system-overlay feel)
                    uint pillBg = ImGui.ColorConvertFloat4ToU32(
                        new Vector4(0f, 0f, 0f, 0.78f * bannerA));
                    dl.AddRectFilled(
                        new Vector2(labelPos.X - padX, labelPos.Y - padY),
                        new Vector2(labelPos.X + labelW + padX, labelPos.Y + labelH + padY),
                        pillBg);
                    // Magenta top edge + cyan bottom edge (toast vocabulary)
                    uint magU = ImGui.ColorConvertFloat4ToU32(
                        Boutique.WithAlpha(Boutique.GlitchMagenta, 0.92f * bannerA));
                    uint cyanU = ImGui.ColorConvertFloat4ToU32(
                        Boutique.WithAlpha(Boutique.GlitchCyan, 0.92f * bannerA));
                    dl.AddRectFilled(
                        new Vector2(labelPos.X - padX, labelPos.Y - padY - 2f * s),
                        new Vector2(labelPos.X + labelW + padX, labelPos.Y - padY),
                        magU);
                    dl.AddRectFilled(
                        new Vector2(labelPos.X - padX, labelPos.Y + labelH + padY),
                        new Vector2(labelPos.X + labelW + padX, labelPos.Y + labelH + padY + 2f * s),
                        cyanU);
                    // RGB-split text
                    float bnrJit = MathF.Sin(t * 41.1f) * 0.6f * s;
                    float bnrOff = 2f * s + bnrJit;
                    uint magTextU = ImGui.ColorConvertFloat4ToU32(
                        Boutique.WithAlpha(Boutique.GlitchMagenta, 0.85f * bannerA));
                    uint cyanTextU = ImGui.ColorConvertFloat4ToU32(
                        Boutique.WithAlpha(Boutique.GlitchCyan, 0.85f * bannerA));
                    uint whiteTextU = ImGui.ColorConvertFloat4ToU32(
                        new Vector4(1f, 1f, 1f, bannerA));
                    Boutique.DrawTrackedText(dl,
                        new Vector2(labelPos.X - bnrOff, labelPos.Y), label, magTextU, trk);
                    Boutique.DrawTrackedText(dl,
                        new Vector2(labelPos.X + bnrOff, labelPos.Y), label, cyanTextU, trk);
                    Boutique.DrawTrackedText(dl, labelPos, label, whiteTextU, trk);
                }
            }
        }

        // ── Landing impact (slot-0 flash + shockwave rings) ──
        DrawCelebrationLanding(dl, t, s);
    }

    private void DrawCardGildedSheen(ImDrawListPtr dl, Vector2 mn, Vector2 mx, float chamfer, Vector4 catCol, string id, float s)
    {
        if (Boutique.ReduceMotion) return;
        float t = (float)ImGui.GetTime();
        // Sheen start is set on hover-enter from DrawCard. Slightly longer
        // (1.4s) and softer alphas - feels like a wash of light moving across
        // polished gold leaf rather than a punchy sweep.
        const float duration = 1.4f;
        if (!cardSheenStart.TryGetValue(id, out var sheenStart)) return;
        float elapsed = t - sheenStart;
        if (elapsed < 0f || elapsed > duration) return;

        float p = elapsed / duration;
        // ease-out cubic-bezier(0.33, 0.12, 0.55, 0.95) approximation
        float eased = p < 0.5f ? 4f * p * p * p : 1f - MathF.Pow(-2f * p + 2f, 3f) / 2f;

        float w = mx.X - mn.X;
        float bandW = w * 0.9f;
        float sx = mn.X - bandW + eased * (w + bandW * 2f);

        // Alpha envelope: ramp 0→22%, hold 22→78%, fade 78→100%
        float envA = p < 0.22f ? p / 0.22f
                   : p > 0.78f ? (1f - p) / 0.22f
                   : 1f;
        envA = Math.Clamp(envA, 0f, 1f);

        dl.PushClipRect(mn, mx, true);

        // 4 adjacent rects: approach → gold band → white peak → category tint → fade
        // Alphas reduced ~40% across the board for a softer wash effect.
        uint c0 = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0f));
        uint c1 = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.GoldWarm, 0.06f * envA));
        uint c2 = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0.10f * envA));
        uint cPeak = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.18f * envA));
        uint c3 = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0.10f * envA));
        uint c4 = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(catCol, 0.16f * envA));
        uint c5 = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0f));

        float q = bandW / 5f;
        dl.AddRectFilledMultiColor(
            new Vector2(sx, mn.Y), new Vector2(sx + q, mx.Y),
            c0, c1, c1, c0);
        dl.AddRectFilledMultiColor(
            new Vector2(sx + q, mn.Y), new Vector2(sx + 2f * q, mx.Y),
            c1, c2, c2, c1);
        dl.AddRectFilledMultiColor(
            new Vector2(sx + 2f * q, mn.Y), new Vector2(sx + 3f * q, mx.Y),
            c2, cPeak, cPeak, c2);
        dl.AddRectFilledMultiColor(
            new Vector2(sx + 3f * q, mn.Y), new Vector2(sx + 4f * q, mx.Y),
            cPeak, c4, c4, cPeak);
        dl.AddRectFilledMultiColor(
            new Vector2(sx + 4f * q, mn.Y), new Vector2(sx + bandW, mx.Y),
            c4, c5, c5, c4);

        dl.PopClipRect();
    }

    // Ascension celebration FX cascade. FX are anchored to the hero's centre
    // and gated to the HOLD window so positions stay stable.
    private void DrawCelebrationFlair(ImDrawListPtr dl, float t, Vector2 centre, Vector4 catCol, float s)
    {
        if (t < 0f || t > CelebDur) return;

        int seed = celebId?.GetHashCode() ?? 0;
        uint magFringeU  = ImGui.ColorConvertFloat4ToU32(
            Boutique.WithAlpha(Boutique.GlitchMagenta, 0.92f));
        uint cyanFringeU = ImGui.ColorConvertFloat4ToU32(
            Boutique.WithAlpha(Boutique.GlitchCyan, 0.92f));

        // Clip the cascade to a generous box around the hero so off-screen
        // FX (dropout overshoots, vshear extensions) clip cleanly without
        // bleeding into other UI surfaces.
        float clipR = 460f * s;
        Vector2 clipMn = new Vector2(centre.X - clipR, centre.Y - clipR * 0.7f);
        Vector2 clipMx = new Vector2(centre.X + clipR, centre.Y + clipR * 0.7f);
        clipMn.X = MathF.Max(clipMn.X, celebViewportMin.X);
        clipMn.Y = MathF.Max(clipMn.Y, celebViewportMin.Y);
        clipMx.X = MathF.Min(clipMx.X, celebViewportMax.X);
        clipMx.Y = MathF.Min(clipMx.Y, celebViewportMax.Y);
        dl.PushClipRect(clipMn, clipMx, true);

        // ── CRT scanlines across the FX area during HOLD (1100-2200ms) ──
        if (t >= CelebAscentEnd && t < CelebHoldEnd + 0.10f)
        {
            float scanA;
            if (t < CelebAscentEnd + 0.15f)      scanA = (t - CelebAscentEnd) / 0.15f;
            else if (t > CelebHoldEnd)           scanA = MathF.Max(0f, 1f - (t - CelebHoldEnd) / 0.10f);
            else                                 scanA = 1f;
            scanA = MathF.Min(1f, scanA);
            float pitch = 3f * s;
            float stripeH = 1f * s;
            uint stripeU = ImGui.ColorConvertFloat4ToU32(
                Boutique.WithAlpha(Boutique.GlitchCyan, 0.10f * scanA));
            for (float y = clipMn.Y; y < clipMx.Y; y += pitch)
                dl.AddRectFilled(new Vector2(clipMn.X, y),
                                 new Vector2(clipMx.X, y + stripeH), stripeU);
        }

        // ── Dropout bars: 4 horizontal pulses with patterned chunks +
        //    magenta top + cyan bottom fringes (toast vocabulary) ──
        float[] dropoutAt = { 1.18f, 1.42f, 1.65f, 1.95f };
        for (int b = 0; b < dropoutAt.Length; b++)
        {
            float dt = t - dropoutAt[b];
            if (dt < 0f || dt > 0.18f) continue;
            float dp = dt / 0.18f;
            float a = dp < 0.30f ? dp / 0.30f : 1f - (dp - 0.30f) / 0.70f;
            a = MathF.Max(0f, a);
            if (a < 0.02f) continue;
            int hY = HashCombine(seed, b * 6151);
            int hX = HashCombine(seed, b * 4093 + 17);
            float by = centre.Y + (((hY & 0xFFFF) / 65535f) - 0.5f) * 320f * s;
            float bh = 8f * s + ((hX & 0xFF) / 255f) * 6f * s;
            float bxOff = (((hX >> 8) & 0xFF) / 255f - 0.5f) * 8f * s;
            float bxLeft = clipMn.X + bxOff;
            float bxRight = clipMx.X + bxOff;
            uint blackU = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.85f * a));
            uint whiteU = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.18f * a));
            uint cyanCU = ImGui.ColorConvertFloat4ToU32(
                Boutique.WithAlpha(Boutique.GlitchCyan, 0.65f * a));
            uint magCU = ImGui.ColorConvertFloat4ToU32(
                Boutique.WithAlpha(Boutique.GlitchMagenta, 0.65f * a));
            float chunkW = 18f * s;
            for (float x = bxLeft; x < bxRight; x += chunkW)
            {
                int hC = HashCombine(seed ^ b, (int)(x * 0.13f));
                uint chunkCol = (hC & 0x3) switch
                {
                    0 => cyanCU,
                    1 => magCU,
                    2 => whiteU,
                    _ => blackU,
                };
                dl.AddRectFilled(new Vector2(x, by),
                                 new Vector2(MathF.Min(x + chunkW * 0.7f, bxRight), by + bh),
                                 chunkCol);
            }
            dl.AddRectFilled(
                new Vector2(bxLeft, by - 2f * s),
                new Vector2(bxRight, by),
                ImGui.ColorConvertFloat4ToU32(
                    Boutique.WithAlpha(Boutique.GlitchMagenta, 0.85f * a)));
            dl.AddRectFilled(
                new Vector2(bxLeft, by + bh),
                new Vector2(bxRight, by + bh + 2f * s),
                ImGui.ColorConvertFloat4ToU32(
                    Boutique.WithAlpha(Boutique.GlitchCyan, 0.85f * a)));
        }

        // ── Vertical shears: 5 narrow strips with cyan top + magenta bot fringe ──
        for (int v = 0; v < 5; v++)
        {
            int hO = HashCombine(seed, v * 7919 + 41);
            int hX = HashCombine(seed, v * 5039 + 13);
            int hW = HashCombine(seed, v * 3637 + 23);
            float onset = 1.20f + ((hO & 0xFFFF) / 65535f) * 0.85f;
            float dur = 0.20f;
            float vt = t - onset;
            if (vt < 0f || vt > dur) continue;
            float vp = vt / dur;
            float a = vp < 0.20f ? vp / 0.20f : 1f - (vp - 0.20f) / 0.80f;
            a = MathF.Max(0f, a);
            if (a < 0.02f) continue;
            float vx = centre.X + (((hX & 0xFFFF) / 65535f) - 0.5f) * 400f * s;
            float vw = 6f * s + ((hW & 0xFF) / 255f) * 8f * s;
            float vtop = clipMn.Y;
            float vbot = clipMx.Y;
            dl.AddRectFilled(
                new Vector2(vx, vtop),
                new Vector2(vx + vw, vbot),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.85f * a)));
            dl.AddRectFilled(
                new Vector2(vx, vtop - 3f * s),
                new Vector2(vx + vw, vtop),
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.GlitchCyan, 0.92f * a)));
            dl.AddRectFilled(
                new Vector2(vx, vbot),
                new Vector2(vx + vw, vbot + 3f * s),
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.GlitchMagenta, 0.92f * a)));
        }

        // ── Glitch specks: 40 single-pixel points flickering on/off ──
        if (t >= CelebAscentEnd && t < CelebHoldEnd + 0.05f)
        {
            float spA;
            if (t < CelebAscentEnd + 0.15f) spA = (t - CelebAscentEnd) / 0.15f;
            else if (t > CelebHoldEnd)      spA = MathF.Max(0f, 1f - (t - CelebHoldEnd) / 0.05f);
            else                            spA = 1f;
            spA = MathF.Min(1f, spA);

            for (int sp = 0; sp < 40; sp++)
            {
                int hX = HashCombine(seed, sp * 9613 + 3);
                int hY = HashCombine(seed, sp * 7283 + 7);
                int hC = HashCombine(seed, sp * 5527 + 11);
                int hP = HashCombine(seed, sp * 3271 + 17);
                float phase = (hP & 0xFF) / 255f;
                float blink = (t * 8f + phase * 6.28f) % 1f;
                if (blink > 0.45f) continue;
                float sx = centre.X + (((hX & 0xFFFF) / 65535f) - 0.5f) * 380f * s;
                float sy = centre.Y + (((hY & 0xFFFF) / 65535f) - 0.5f) * 220f * s;
                uint sc = (hC & 0x3) switch
                {
                    0 => magFringeU,
                    1 => cyanFringeU,
                    _ => ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.92f * spA)),
                };
                float ss = ((hC >> 4) & 1) == 0 ? 1f * s : 2f * s;
                dl.AddRectFilled(
                    new Vector2(sx, sy),
                    new Vector2(sx + ss, sy + ss),
                    sc);
            }
        }

        dl.PopClipRect();
    }

    /// <summary>
    /// Cross-hair beam: 4-segment gradient (transparent → soft → bright peak →
    /// soft → transparent) along the long axis. Used by the cross-hair flash
    /// during ENTHRONE.
    /// </summary>
    private void DrawCelebrationBeam(ImDrawListPtr dl, Vector2 bMn, Vector2 bMx, float opacity, bool horizontal)
    {
        Vector4 cEdge = new Vector4(1f, 1f, 1f, 0f);
        Vector4 cSoft = new Vector4(1f, 0.95f, 0.4f, 0.55f * opacity);
        Vector4 cPeak = new Vector4(1f, 1f,    1f,  0.95f * opacity);
        uint uEdge = ImGui.ColorConvertFloat4ToU32(cEdge);
        uint uSoft = ImGui.ColorConvertFloat4ToU32(cSoft);
        uint uPeak = ImGui.ColorConvertFloat4ToU32(cPeak);

        if (horizontal)
        {
            float w = bMx.X - bMn.X;
            float x1 = bMn.X + w * 0.38f;
            float x2 = bMn.X + w * 0.50f;
            float x3 = bMn.X + w * 0.62f;
            dl.AddRectFilledMultiColor(bMn,
                new Vector2(x1, bMx.Y),       uEdge, uSoft, uSoft, uEdge);
            dl.AddRectFilledMultiColor(new Vector2(x1, bMn.Y),
                new Vector2(x2, bMx.Y),       uSoft, uPeak, uPeak, uSoft);
            dl.AddRectFilledMultiColor(new Vector2(x2, bMn.Y),
                new Vector2(x3, bMx.Y),       uPeak, uSoft, uSoft, uPeak);
            dl.AddRectFilledMultiColor(new Vector2(x3, bMn.Y),
                bMx,                          uSoft, uEdge, uEdge, uSoft);
        }
        else
        {
            float h = bMx.Y - bMn.Y;
            float y1 = bMn.Y + h * 0.38f;
            float y2 = bMn.Y + h * 0.50f;
            float y3 = bMn.Y + h * 0.62f;
            dl.AddRectFilledMultiColor(bMn,
                new Vector2(bMx.X, y1),       uEdge, uEdge, uSoft, uSoft);
            dl.AddRectFilledMultiColor(new Vector2(bMn.X, y1),
                new Vector2(bMx.X, y2),       uSoft, uSoft, uPeak, uPeak);
            dl.AddRectFilledMultiColor(new Vector2(bMn.X, y2),
                new Vector2(bMx.X, y3),       uPeak, uPeak, uSoft, uSoft);
            dl.AddRectFilledMultiColor(new Vector2(bMn.X, y3),
                bMx,                          uSoft, uSoft, uEdge, uEdge);
        }
    }

    /// <summary>
    /// Landing impact at slot 0 - chromatic version. Hard black slot flash
    /// with magenta + cyan fringes (matches toast vshear vocabulary), and
    /// two offset shockwave rings (one magenta, one cyan, slightly offset)
    /// expanding outward instead of the gold double-ring.
    /// </summary>
    private void DrawCelebrationLanding(ImDrawListPtr dl, float t, float s)
    {
        if (celebGridCardW <= 0f || celebGridCardH <= 0f) return;
        Vector2 slot0Mn = celebSlot0Pos;
        Vector2 slot0Mx = slot0Mn + new Vector2(celebGridCardW, celebGridCardH);
        Vector2 slot0Centre = (slot0Mn + slot0Mx) * 0.5f;

        // Slot-0 impact flash with chromatic fringes (2680ms, 380ms)
        if (t >= 2.68f && t < 3.06f)
        {
            float fp = (t - 2.68f) / 0.38f;
            float opacity = fp < 0.20f ? fp / 0.20f : 1f - (fp - 0.20f) / 0.80f;
            opacity = MathF.Max(0f, opacity);
            if (opacity > 0.02f)
            {
                // Hard white core flash inside the slot silhouette (system feel)
                float chamfer = 8f * s;
                Boutique.FillSlip(dl, slot0Mn, slot0Mx, chamfer,
                    ImGui.ColorConvertFloat4ToU32(
                        new Vector4(1f, 1f, 1f, opacity * 0.45f)));
                // Magenta top edge + cyan bottom edge (toast vocabulary)
                dl.AddRectFilled(
                    new Vector2(slot0Mn.X, slot0Mn.Y - 2f * s),
                    new Vector2(slot0Mx.X, slot0Mn.Y),
                    ImGui.ColorConvertFloat4ToU32(
                        Boutique.WithAlpha(Boutique.GlitchMagenta, 0.95f * opacity)));
                dl.AddRectFilled(
                    new Vector2(slot0Mn.X, slot0Mx.Y),
                    new Vector2(slot0Mx.X, slot0Mx.Y + 2f * s),
                    ImGui.ColorConvertFloat4ToU32(
                        Boutique.WithAlpha(Boutique.GlitchCyan, 0.95f * opacity)));
            }
        }

        // Chromatic shockwave: one magenta ring offset left/up, one cyan
        // ring offset right/down - both expand together, fading. Replaces
        // the gold double-ring shockwave.
        float[] shockStarts = { 2.70f, 2.74f };
        Vector4[] shockColors = { Boutique.GlitchMagenta, Boutique.GlitchCyan };
        Vector2[] shockOffsets = { new Vector2(-3f * s, -2f * s), new Vector2(3f * s, 2f * s) };
        for (int i = 0; i < 2; i++)
        {
            float rt = t - shockStarts[i];
            if (rt < 0f || rt > 0.60f) continue;
            float p = rt / 0.60f;
            float pe = EaseOutCubic(p);
            float baseRadius = 30f * s;
            float radius = baseRadius * (0.3f + (7f - 0.3f) * pe);
            float opacity = p < 0.10f
                ? (p / 0.10f) * 0.90f
                : 0.90f * (1f - (p - 0.10f) / 0.90f);
            opacity = MathF.Max(0f, opacity);
            if (opacity < 0.02f) continue;
            float borderW = (3f - 2f * pe) * s;
            dl.AddCircle(slot0Centre + shockOffsets[i], radius,
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(shockColors[i], opacity)),
                48, borderW);
        }
    }

    // Drifting aurora spots only, no hum lines
    private void DrawAmbientSpots(float s)
    {
        var dl = ImGui.GetWindowDrawList();
        var mn = ImGui.GetWindowPos();
        var mx = mn + ImGui.GetWindowSize();
        float t = (float)Boutique.AnimTime(ImGui.GetTime());
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
                Boutique.WithAlpha(colour, peakA / layers));
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
        // Higher peak alphas than the window ambient, since the shop shell is darker
        Spot(26f, new Vector2(w * 0.18f, h * 0.22f), new Vector2(200f * s, 90f * s),
             260f * s, 160f * s, Boutique.Gold,          Boutique.AtmosphereAlpha(null, 0.085f, 0.30f));
        Spot(32f, new Vector2(w * 0.78f, h * 0.55f), new Vector2(-160f * s, -70f * s),
             240f * s, 150f * s, Boutique.AmbientViolet, Boutique.AtmosphereAlpha("custom.ambient.violet", 0.060f, 0.30f));
        Spot(38f, new Vector2(w * 0.45f, h * 0.70f), new Vector2(-120f * s, 60f * s),
             220f * s, 140f * s, Boutique.AmbientCyan,   Boutique.AtmosphereAlpha("custom.ambient.cyan", 0.045f, 0.30f));
    }

    private void DrawAmbientLayer(float s)
    {
        var dl = ImGui.GetWindowDrawList();
        var mn = ImGui.GetWindowPos();
        var mx = mn + ImGui.GetWindowSize();
        float t = (float)Boutique.AnimTime(ImGui.GetTime());
        float w = mx.X - mn.X;
        float h = mx.Y - mn.Y;

        // ── Drifting aurora spots (HTML .spot.s1/s2/s3) ──
        // HTML specifies ellipse dimensions (460×280, 420×260, 380×240) +
        // `filter: blur(80px)` + radial-gradient(circle, colour, transparent 65%).
        // We can't blur in ImGui, so we approximate with nested ellipse polygons
        // each adding a uniform small alpha - stack produces a smooth linear
        // falloff from peak at centre → transparent at the outer edge.
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
                Boutique.WithAlpha(colour, peakA / layers));
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
        // Low peak alphas so the spots read as atmosphere, not coloured clouds
        Spot(26f, new Vector2(w * 0.18f, h * 0.22f), new Vector2(200f * s, 90f * s),
             230f * s, 140f * s, Boutique.Gold, Boutique.AtmosphereAlpha(null, 0.028f, 0.30f));
        Spot(32f, new Vector2(w * 0.75f, h * 0.55f), new Vector2(-160f * s, -70f * s),
             210f * s, 130f * s, Boutique.AmbientViolet, Boutique.AtmosphereAlpha("custom.ambient.violet", 0.020f, 0.30f));
        Spot(38f, new Vector2(w * 0.45f, h * 0.65f), new Vector2(-120f * s, 60f * s),
             190f * s, 120f * s, Boutique.AmbientCyan, Boutique.AtmosphereAlpha("custom.ambient.cyan", 0.014f, 0.30f));

        // ── 3 horizontal hum lines (1px, scrolling at different speeds) ──
        void HumLine(float topFrac, float periodSec, bool reverse, Vector4 col, float peakA)
        {
            float phase = (t % periodSec) / periodSec;
            if (reverse) phase = 1f - phase;
            // Line is wider than window - scrolls from -30% to +30% of w
            float xOff = -0.30f * w + phase * 0.60f * w;
            float yBand = mn.Y + topFrac * h;
            // Build a 3-segment gradient: transparent → colour (40-60%) → transparent
            float lineW = w * 1.5f;
            var start = new Vector2(mn.X + xOff - lineW * 0.5f, yBand);
            var midL  = new Vector2(start.X + lineW * 0.40f, yBand);
            var midR  = new Vector2(start.X + lineW * 0.60f, yBand);
            var end   = new Vector2(start.X + lineW, yBand);
            uint cEdge = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(col, 0f));
            uint cMid  = ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(col, peakA));
            float lineH = 1f * s;
            dl.AddRectFilledMultiColor(
                start, new Vector2(midL.X, yBand + lineH),
                cEdge, cMid, cMid, cEdge);
            dl.AddRectFilled(midL, new Vector2(midR.X, yBand + lineH), cMid);
            dl.AddRectFilledMultiColor(
                midR, new Vector2(end.X, yBand + lineH),
                cMid, cEdge, cEdge, cMid);
        }
        if (!Boutique.ReduceMotion)
        {
            HumLine(0.28f, 16f, reverse: false, Boutique.Gold, Boutique.AtmosphereAlpha(null, 0.18f, 0.85f));
            HumLine(0.54f, 22f, reverse: true,  Boutique.AmbientMagentaSoft, Boutique.AtmosphereAlpha("custom.ambient.magenta", 0.12f, 0.85f));
            HumLine(0.78f, 26f, reverse: false, Boutique.AmbientCyanSoft, Boutique.AtmosphereAlpha("custom.ambient.cyan", 0.09f, 0.85f));
        }

        if (Boutique.ReduceMotion) return;

        // Each mote: left%, base delay, period, colour
        (float leftPct, float delay, float period, Vector4 col)[] motes = {
            (0.14f,  0f, 10f, Boutique.GoldWarm),
            (0.32f,  2f, 12f, Boutique.AmbientMagentaSoft),
            (0.48f,  4f, 11f, Boutique.GoldWarm),
            (0.66f,  1.5f, 13f, Boutique.AmbientCyanSoft),
            (0.82f,  6f, 10f, Boutique.GoldWarm),
            (0.24f,  5f, 14f, Boutique.AmbientViolet),
        };
        foreach (var (leftPct, delay, period, col) in motes)
        {
            float localT = ((t - delay) % period + period) % period;
            float p = localT / period;
            // 0..1 across the full flight; opacity ramps up 0-15%, down 85-100%
            float a = p < 0.15f ? (p / 0.15f) * 0.7f
                    : p > 0.85f ? ((1f - p) / 0.15f) * 0.7f
                    : 0.7f;
            string? moteKey = col == Boutique.AmbientMagentaSoft ? "custom.ambient.magenta"
                            : col == Boutique.AmbientCyanSoft    ? "custom.ambient.cyan"
                            : col == Boutique.AmbientViolet      ? "custom.ambient.violet"
                            : null;
            a = Boutique.AtmosphereAlpha(moteKey, a, 1f);
            // Rises past the top over the period, drifting slightly leftward
            float yRise = p * (h + 150f * s);
            float xDrift = -p * 30f * s;
            var pt = new Vector2(mn.X + leftPct * w + xDrift, mx.Y - 10f * s - yRise);
            dl.AddCircleFilled(pt, 1.2f * s,
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(col, a)), 8);
            dl.AddCircleFilled(pt, 2.5f * s,
                ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(col, a * 0.4f)), 10);
        }
    }

    // Firework spark at the leading edge of an in-progress bar. `intensity`
    // (0..1) ramps the core, halo and particle count.
    private void DrawProgressSpark(ImDrawListPtr dl, Vector2 center, Vector4 color, float scale, bool large, float intensity = 1f)
    {
        intensity = Math.Clamp(intensity, 0f, 1f);

        double t = Boutique.AnimTime(ImGui.GetTime());
        // Pulse cadence ramps from 4.5Hz to 7Hz as intensity rises
        float pulseSpeed = 4.5f + intensity * 2.5f;
        float pulse = 0.65f + 0.35f * MathF.Sin((float)t * pulseSpeed);

        float coreR = (large ? 2.8f : 2.0f) * scale;
        float glowR = (large ? 6.5f : 4.5f) * scale;

        // Outer halo: soft coloured glow
        float haloMult = 0.40f + 0.60f * intensity;
        dl.AddCircleFilled(center, glowR,         ImGui.ColorConvertFloat4ToU32(A(color, 0.18f * pulse * haloMult)));
        dl.AddCircleFilled(center, glowR * 0.65f, ImGui.ColorConvertFloat4ToU32(A(color, 0.30f * pulse * haloMult)));

        // Bright white core
        var white = new Vector4(1f, 1f, 1f, 1f);
        float coreMult = 0.50f + 0.50f * intensity;
        dl.AddCircleFilled(center, coreR,         ImGui.ColorConvertFloat4ToU32(A(white, 0.95f * pulse * coreMult)));
        dl.AddCircleFilled(center, coreR * 0.55f, ImGui.ColorConvertFloat4ToU32(A(white, coreMult)));

        if (Boutique.ReduceMotion) return;

        // Radiating particles on a golden-angle distribution, looping outward
        int baseCount = large ? 5 : 4;
        int sparkCount = Math.Max(1, (int)Math.Round(baseCount * (0.45f + 0.55f * intensity)));
        const float period = 1.35f;
        float maxDist = (large ? 9f : 6.5f) * scale * (0.70f + 0.30f * intensity);
        float sparkBaseR = (large ? 1.5f : 1.1f) * scale;
        float sparkAlphaMult = 0.40f + 0.60f * intensity;

        for (int i = 0; i < sparkCount; i++)
        {
            float phase = ((float)t / period + i / (float)sparkCount) % 1f;
            float eased = 1f - (1f - phase) * (1f - phase); // ease-out
            float dist  = eased * maxDist;
            float angle = (i * 137.5f + 25f) * MathF.PI / 180f;

            var pos = new Vector2(
                center.X + MathF.Cos(angle) * dist,
                center.Y + MathF.Sin(angle) * dist * 0.55f); // squash vertically, bar is narrow

            float sparkAlpha = (1f - phase) * 0.85f * sparkAlphaMult;
            float sparkR     = sparkBaseR * (1f - phase * 0.4f);
            dl.AddCircleFilled(pos, sparkR, ImGui.ColorConvertFloat4ToU32(A(color, sparkAlpha)));
        }
    }

    // ═══════════════ SHOP ═══════════════

    // \u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550 SHOP \u2014 CINEMATIC VAULT \u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550
    // design-mockups/08-shop-variants Variant 3. Pulsing aura bloom, centred
    // progress ring (earned/total points), and a large "VAULT SEALED" title
    // block with description. Category pips + flanking rules removed per user
    // feedback \u2014 they read as random chrome rather than part of the comp.
    private void DrawShop(float s)
    {
        ImGui.BeginChild("ShopArea", Vector2.Zero, false);
        float availW = ImGui.GetContentRegionAvail().X;
        float availH = ImGui.GetContentRegionAvail().Y;
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        float t = (float)ImGui.GetTime();

        // Shop tab backdrop - matches HTML `.strip { background: var(--shell) }`
        // (#04050a) so the vault sits on the deepest surface in the palette
        // rather than inheriting the main window's surface-0 (#0e1014).
        var fullMn = new Vector2(origin.X - 4f * s, origin.Y);
        var fullMx = new Vector2(origin.X + availW + 4f * s, origin.Y + availH + 4f * s);
        dl.AddRectFilled(fullMn, fullMx,
            ImGui.ColorConvertFloat4ToU32(Boutique.Shell));

        // Soft drifting aurora spots (no hum lines - those read as random
        // horizontal streaks in the vault comp). Matches the atmosphere on
        // the Achievements tab's content-wrap.
        DrawAmbientSpots(s);

        int earned = plugin.Configuration.AchievementData.TotalPointsEarned;
        int total  = Math.Max(1, AchievementRegistry.TotalPoints);
        float ratio = Math.Clamp(earned / (float)total, 0f, 1f);
        int pct = (int)MathF.Round(ratio * 100f);

        // Ring fill animation - start at 0 on Shop tab-enter, ease-out cubic
        // to the actual ratio over 1500ms. shopFirstShownTime is captured at
        // the dispatch site in Draw() when the tab actually transitions.
        const float fillDuration = 1.5f;
        float fillElapsed = t - shopFirstShownTime;
        float fillProgress = Math.Clamp(fillElapsed / fillDuration, 0f, 1f);
        // Ease-out cubic
        float fillEased = 1f - MathF.Pow(1f - fillProgress, 3f);
        float displayedRatio = ratio * fillEased;
        int displayedPct = (int)MathF.Round(pct * fillEased);
        // Animate displayed earned number too - feels like the dial is winding up
        int displayedEarned = (int)MathF.Round(earned * fillEased);

        // HTML ring is 210\u00d7210 (radius 105, 20px thick = 19% of radius).
        // User consistently felt previous sizes too big for the in-game area.
        // 100\u00d7s matches HTML-native more closely and leaves room for the
        // title + description below.
        float ringRadius = 100f * s;
        float ringThickness = 19f * s;
        float ringInnerEdge = ringRadius - ringThickness * 0.5f;
        // Position ring centre so its TOP edge sits ~56px below the shop
        // tab's top. This leaves the full title stack + description room to
        // the bottom without clipping at the window footer.
        var centre = new Vector2(
            origin.X + availW * 0.5f,
            origin.Y + 56f * s + ringRadius);

        // \u2500\u2500 Aura bloom \u2500\u2500
        // HTML: radial gradient gold@18% \u2192 5% at 25% \u2192 transparent at 55%
        // with filter:blur(20px). Blur softens the 18% peak, so effective
        // centre alpha reads around 0.10. Pulse is subtle (85-100% opacity,
        // 1.00-1.08 scale) on a 5s cycle.
        {
            float pulseSin = 0.5f + 0.5f * MathF.Sin(t * MathF.Tau / 5f);
            float bloomPulse = 1f + 0.04f * pulseSin;
            float bloomAlpha = 0.90f + 0.10f * pulseSin;
            float maxR = 200f * s * bloomPulse;
            const int bloomLayers = 64;
            const float peakTarget = 0.10f;
            uint bloomCol = ImGui.ColorConvertFloat4ToU32(
                Boutique.WithAlpha(Boutique.Gold,
                    (peakTarget / bloomLayers) * bloomAlpha));
            for (int i = 0; i < bloomLayers; i++)
            {
                float r = maxR * ((i + 1) / (float)bloomLayers);
                dl.AddCircleFilled(centre, r, bloomCol, 72);
            }
        }

        // \u2500\u2500 Ring drop-shadow halo \u2500\u2500
        // Soft FILLED-disc layers with uniform alpha \u2192 gaussian-like bleed.
        {
            float ringOuterEdge = ringRadius + ringThickness * 0.5f;
            const int haloLayers = 50;
            const float haloPeak = 0.22f;
            float haloSpread = 30f * s;
            uint haloCol = ImGui.ColorConvertFloat4ToU32(
                Boutique.WithAlpha(Boutique.Gold, haloPeak / haloLayers));
            for (int i = 0; i < haloLayers; i++)
            {
                float u = (i + 1) / (float)haloLayers;
                float r = ringOuterEdge + haloSpread * u;
                dl.AddCircleFilled(centre, r, haloCol, 84);
            }
        }

        // \u2500\u2500 Progress ring \u2500\u2500
        // HTML: conic-gradient from -90deg: gold 0\u00b0 \u2192 gold-warm at ratio\u00b0 \u2192
        // border remainder. Draw earned arc as TWO PathStroke passes \u2014 one
        // for each half of the arc with interpolated colour.
        //
        // GLITCH FLAIR (during fill animation only): chromatic fringes on
        // the leading edge + dropout flickers. Once fillProgress hits 1.0
        // the ring sits as a clean gold dial.
        {
            float startAng = -MathF.PI * 0.5f;
            float earnedAng = MathF.Tau * displayedRatio;
            float endAng = startAng + earnedAng;

            if (displayedRatio > 0f)
            {
                // GLITCH FLAIR \u2014 chromatic ghost arcs UNDER the gold ring
                // while the fill is animating. Fades to nothing as fillProgress
                // hits 1.0 so the settled state is clean gold-on-gold.
                if (fillProgress < 1f)
                {
                    float chromaA = 1f - fillProgress;
                    Vector2 magOff = new Vector2(-3f * s, -2f * s);
                    Vector2 cyanOff = new Vector2(3f * s, 2f * s);
                    dl.PathClear();
                    dl.PathArcTo(centre + magOff, ringRadius, startAng, endAng, 96);
                    dl.PathStroke(
                        ImGui.ColorConvertFloat4ToU32(
                            Boutique.WithAlpha(Boutique.GlitchMagenta, 0.85f * chromaA)),
                        ImDrawFlags.None, ringThickness);
                    dl.PathClear();
                    dl.PathArcTo(centre + cyanOff, ringRadius, startAng, endAng, 96);
                    dl.PathStroke(
                        ImGui.ColorConvertFloat4ToU32(
                            Boutique.WithAlpha(Boutique.GlitchCyan, 0.85f * chromaA)),
                        ImDrawFlags.None, ringThickness);
                }

                // Gold + gold-warm arc on top (the actual ring identity)
                float midAng = startAng + earnedAng * 0.5f;
                dl.PathClear();
                dl.PathArcTo(centre, ringRadius, startAng, midAng, 64);
                dl.PathStroke(
                    ImGui.ColorConvertFloat4ToU32(Boutique.Gold),
                    ImDrawFlags.None, ringThickness);

                dl.PathClear();
                dl.PathArcTo(centre, ringRadius, midAng, endAng, 64);
                dl.PathStroke(
                    ImGui.ColorConvertFloat4ToU32(Boutique.GoldWarm),
                    ImDrawFlags.None, ringThickness);

                // GLITCH FLAIR \u2014 bright white-hot leading edge with magenta
                // and cyan fringes flanking it (where the fill is currently
                // tracing). Strongest at fill start, fades out as it settles.
                if (fillProgress < 1f && displayedRatio > 0.005f)
                {
                    float leadingA = 1f - fillProgress;
                    Vector2 leadPos = centre + new Vector2(
                        MathF.Cos(endAng) * ringRadius,
                        MathF.Sin(endAng) * ringRadius);
                    // Hot white core at the tip
                    dl.AddCircleFilled(leadPos, ringThickness * 0.55f,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.95f * leadingA)),
                        16);
                    dl.AddCircleFilled(leadPos, ringThickness * 0.95f,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.95f, 0.7f, 0.45f * leadingA)),
                        16);
                    // Magenta + cyan halo specks just behind the tip
                    Vector2 perpDir = new Vector2(-MathF.Sin(endAng), MathF.Cos(endAng));
                    dl.AddCircleFilled(leadPos + perpDir * ringThickness * 0.5f,
                        ringThickness * 0.30f,
                        ImGui.ColorConvertFloat4ToU32(
                            Boutique.WithAlpha(Boutique.GlitchMagenta, 0.85f * leadingA)),
                        12);
                    dl.AddCircleFilled(leadPos - perpDir * ringThickness * 0.5f,
                        ringThickness * 0.30f,
                        ImGui.ColorConvertFloat4ToU32(
                            Boutique.WithAlpha(Boutique.GlitchCyan, 0.85f * leadingA)),
                        12);
                }
            }
            // Remainder arc \u2014 single stroke border
            if (displayedRatio < 1f)
            {
                dl.PathClear();
                dl.PathArcTo(centre, ringRadius, endAng, startAng + MathF.Tau, 96);
                dl.PathStroke(
                    ImGui.ColorConvertFloat4ToU32(Boutique.Border),
                    ImDrawFlags.None, ringThickness);
            }
        }

        // Hoisted so both the inner disc and the GLITCH FLAIR (drawn AFTER
        // the disc, on top, so dropout bars + specks aren't covered) can use it.
        float innerR = ringInnerEdge + 2f * s;

        // \u2500\u2500 Inner disc \u2500\u2500
        // HTML inset: 22px from 210px wrap \u2192 disc radius 83, which is 3px
        // OUTSIDE the ring's inner edge (ring starts at r=80). The disc
        // overlaps into the ring area, which hides the 1px gold-dark border
        // under the ring's gold fill. Previous code had a 4\u00d7s GAP between
        // disc and ring \u2014 that made the gold-dark border visible as a
        // "second inner circle". Correcting to overlap.
        // Inner disc \u2014 innerR was hoisted above the GLITCH FLAIR block
        {
            // Base opaque surface-0 fill
            dl.AddCircleFilled(centre, innerR,
                ImGui.ColorConvertFloat4ToU32(Boutique.Surface0), 80);

            // Soft gold centre tint \u2014 32 uniform-alpha layers
            float tintMaxR = innerR * 0.70f;
            const int tintLayers = 32;
            const float tintPeak = 0.08f;
            uint tintCol = ImGui.ColorConvertFloat4ToU32(
                Boutique.WithAlpha(Boutique.Gold, tintPeak / tintLayers));
            for (int i = 0; i < tintLayers; i++)
            {
                float r = tintMaxR * ((i + 1) / (float)tintLayers);
                dl.AddCircleFilled(centre, r, tintCol, 56);
            }

            // 1px gold-dark border
            dl.AddCircle(centre, innerR,
                ImGui.ColorConvertFloat4ToU32(Boutique.GoldDark),
                80, 1f);
        }

        // ── GLITCH FLAIR overlay (only during the 1.5s fill animation) ──
        // Dropout bars + glitch specks layered on TOP of the inner disc
        // while the ring is winding up. Faded to 0 once fillProgress hits
        // 1.0 so the settled state is the clean gold dial - the gold
        // identity is preserved, the glitch is just on the animation.
        if (fillProgress < 1f)
        {
            float fillFlairA = 1f - fillProgress;
            int fillSeed = HashCombine((int)(shopFirstShownTime * 1000f), 0xBEEF);

            // Two dropout bars across the disc at staggered fill-progress
            // times (25% and 65% into the fill). Magenta top + cyan bottom
            // fringes match the toast's exit-shatter vocabulary.
            float[] barAt = { 0.25f, 0.65f };
            for (int b = 0; b < barAt.Length; b++)
            {
                float bt = fillProgress - barAt[b];
                if (bt < 0f || bt > 0.18f) continue;
                float bp = bt / 0.18f;
                float a = bp < 0.30f ? bp / 0.30f : 1f - (bp - 0.30f) / 0.70f;
                a = MathF.Max(0f, a) * fillFlairA;
                if (a < 0.02f) continue;
                int hY = HashCombine(fillSeed, b * 7919);
                float by = centre.Y + (((hY & 0xFFFF) / 65535f) - 0.5f) * (innerR * 1.2f);
                float bh = 6f * s + (((hY >> 16) & 0xFF) / 255f) * 5f * s;
                float bxLeft = centre.X - innerR;
                float bxRight = centre.X + innerR;
                dl.AddRectFilled(
                    new Vector2(bxLeft, by - 1f * s),
                    new Vector2(bxRight, by),
                    ImGui.ColorConvertFloat4ToU32(
                        Boutique.WithAlpha(Boutique.GlitchMagenta, 0.80f * a)));
                dl.AddRectFilled(
                    new Vector2(bxLeft, by),
                    new Vector2(bxRight, by + bh),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f * a)));
                dl.AddRectFilled(
                    new Vector2(bxLeft, by + bh),
                    new Vector2(bxRight, by + bh + 1f * s),
                    ImGui.ColorConvertFloat4ToU32(
                        Boutique.WithAlpha(Boutique.GlitchCyan, 0.80f * a)));
            }

            // Glitch specks scattered across the disc, flickering on/off.
            // Each has its own blink phase so they don't pulse in unison.
            int speckCount = 18;
            for (int sp = 0; sp < speckCount; sp++)
            {
                int hX = HashCombine(fillSeed, sp * 9613 + 3);
                int hY = HashCombine(fillSeed, sp * 7283 + 7);
                int hC = HashCombine(fillSeed, sp * 5527 + 11);
                int hP = HashCombine(fillSeed, sp * 3271 + 17);
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
                        Boutique.WithAlpha(Boutique.GlitchMagenta, 0.92f * fillFlairA)),
                    1 => ImGui.ColorConvertFloat4ToU32(
                        Boutique.WithAlpha(Boutique.GlitchCyan, 0.92f * fillFlairA)),
                    _ => ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.92f * fillFlairA)),
                };
                float ss = ((hC >> 4) & 1) == 0 ? 1f * s : 2f * s;
                dl.AddRectFilled(spPos, spPos + new Vector2(ss, ss), sc);
            }
        }

        // \u2500\u2500 Centre readout: earned (big) + "/ total earned" + pct \u2500\u2500
        // HTML weights: .e = 600 SemiBold, .t = 400 Regular, .p = 500 Medium.
        // Previous code used Med13/Semi13 for t/p \u2014 one weight class too bold.
        {
            string earnedStr = displayedEarned.ToString();
            string totalStr  = $"/ {total} EARNED";
            string pctStr    = $"{displayedPct}%";

            Vector2 earnedSz;
            using (Plugin.Instance?.OswaldSemiBig?.Push())
                earnedSz = ImGui.CalcTextSize(earnedStr);
            float totalTrk, pctTrk;
            Vector2 totalSz, pctSz;
            using (Plugin.Instance?.OswaldBody13?.Push())   // HTML .t weight 400
            {
                totalTrk = ImGui.GetFontSize() * 0.22f;
                totalSz = new Vector2(
                    Boutique.MeasureTrackedText(totalStr, totalTrk),
                    ImGui.GetFontSize());
            }
            using (Plugin.Instance?.OswaldMed13?.Push())    // HTML .p weight 500
            {
                pctTrk = ImGui.GetFontSize() * 0.20f;
                pctSz = new Vector2(
                    Boutique.MeasureTrackedText(pctStr, pctTrk),
                    ImGui.GetFontSize());
            }

            float stackGap = 4f * s;
            float pctGap   = 8f * s;
            float totalStackH = earnedSz.Y + stackGap + totalSz.Y + pctGap + pctSz.Y;
            float topY = centre.Y - totalStackH * 0.5f;

            // Big earned number - gold (settled identity). During the fill
            // animation, magenta + cyan ghost copies underlay the gold for
            // a brief chromatic-split flair, fading out as fill completes.
            using (Plugin.Instance?.OswaldSemiBig?.Push())
            {
                Vector2 ePos = new Vector2(centre.X - earnedSz.X * 0.5f, topY);
                if (fillProgress < 1f)
                {
                    float chrA = 1f - fillProgress;
                    float chrOff = 3f * s;
                    dl.AddText(ImGui.GetFont(), earnedSz.Y,
                        ePos + new Vector2(-chrOff, 0f),
                        ImGui.ColorConvertFloat4ToU32(
                            Boutique.WithAlpha(Boutique.GlitchMagenta, 0.85f * chrA)),
                        earnedStr);
                    dl.AddText(ImGui.GetFont(), earnedSz.Y,
                        ePos + new Vector2(chrOff, 0f),
                        ImGui.ColorConvertFloat4ToU32(
                            Boutique.WithAlpha(Boutique.GlitchCyan, 0.85f * chrA)),
                        earnedStr);
                }
                dl.AddText(ImGui.GetFont(), earnedSz.Y, ePos,
                    ImGui.ColorConvertFloat4ToU32(Boutique.Gold),
                    earnedStr);
            }
            using (Plugin.Instance?.OswaldBody13?.Push())
                Boutique.DrawTrackedText(dl,
                    new Vector2(centre.X - totalSz.X * 0.5f, topY + earnedSz.Y + stackGap),
                    totalStr,
                    ImGui.ColorConvertFloat4ToU32(Boutique.TextFaint),
                    totalTrk);
            using (Plugin.Instance?.OswaldMed13?.Push())
                Boutique.DrawTrackedText(dl,
                    new Vector2(centre.X - pctSz.X * 0.5f,
                                topY + earnedSz.Y + stackGap + totalSz.Y + pctGap),
                    pctStr,
                    ImGui.ColorConvertFloat4ToU32(Boutique.GoldWarm),
                    pctTrk);
        }

        // \u2500\u2500 Title block: kicker + "VAULT SEALED" + description \u2500\u2500
        {
            string kicker = "THE SHOP";
            string title  = "VAULT SEALED";
            string desc   = "Keep unlocking achievements to earn points. When the vault opens, your balance will be waiting to spend on icons, badges, frames, and profile effects.";

            float titleTopY = centre.Y + ringRadius + 32f * s;

            // Kicker "THE SHOP" \u2014 OswaldSemi13 tracked 0.36em gold-deep
            float kickerTrk;
            Vector2 kickerSz;
            using (Plugin.Instance?.OswaldSemi13?.Push())
            {
                kickerTrk = ImGui.GetFontSize() * 0.36f;
                kickerSz = new Vector2(
                    Boutique.MeasureTrackedText(kicker, kickerTrk),
                    ImGui.GetFontSize());
                Boutique.DrawTrackedText(dl,
                    new Vector2(centre.X - kickerSz.X * 0.5f, titleTopY),
                    kicker,
                    ImGui.ColorConvertFloat4ToU32(Boutique.GoldDeep),
                    kickerTrk);
            }

            // "VAULT SEALED" \u2014 OswaldSemiTitle (~52px) tracked 0.14em gold.
            // Softer than Hero but still hero-scale. Single low-alpha underlay
            // at a tiny Y offset for a gentle emissive bloom.
            float vaultTrk;
            Vector2 vaultSz;
            using (Plugin.Instance?.OswaldSemiTitle?.Push())
            {
                vaultTrk = ImGui.GetFontSize() * 0.14f;
                vaultSz = new Vector2(
                    Boutique.MeasureTrackedText(title, vaultTrk),
                    ImGui.GetFontSize());
            }
            float vaultY = titleTopY + kickerSz.Y + 10f * s;
            var vaultPos = new Vector2(centre.X - vaultSz.X * 0.5f, vaultY);

            using (Plugin.Instance?.OswaldSemiTitle?.Push())
            {
                Boutique.DrawTrackedText(dl,
                    vaultPos + new Vector2(0f, 2f * s),
                    title,
                    ImGui.ColorConvertFloat4ToU32(Boutique.WithAlpha(Boutique.Gold, 0.28f)),
                    vaultTrk);
                Boutique.DrawTrackedText(dl, vaultPos, title,
                    ImGui.ColorConvertFloat4ToU32(Boutique.Gold),
                    vaultTrk);
            }

            // \u2500\u2500 Flanking rules \u2014 visually one full divider interrupted by
            // the title. HTML spec is 60\u00d71 each, but to read as a proper
            // full-width horizontal divider with VAULT SEALED sitting ON it,
            // extend the reach on each side: from 30px in from availW edge
            // to 18px from title, with a gradient fading toward the title.
            {
                float ruleY = vaultY + vaultSz.Y * 0.55f;
                float ruleMargin = 18f * s;       // gap between title and rule
                float ruleEdgePad = 30f * s;      // gap from rule to availW edge
                float ruleThickness = 1f * s;
                uint goldDeep  = ImGui.ColorConvertFloat4ToU32(Boutique.GoldDeep);
                uint goldClear = ImGui.ColorConvertFloat4ToU32(
                    Boutique.WithAlpha(Boutique.GoldDeep, 0f));

                float leftStart  = origin.X + ruleEdgePad;
                float leftEnd    = vaultPos.X - ruleMargin;
                float rightStart = vaultPos.X + vaultSz.X + ruleMargin;
                float rightEnd   = origin.X + availW - ruleEdgePad;

                // Left rule: transparent at far edge \u2192 gold-deep at title edge
                if (leftEnd > leftStart)
                    dl.AddRectFilledMultiColor(
                        new Vector2(leftStart, ruleY - ruleThickness * 0.5f),
                        new Vector2(leftEnd,   ruleY + ruleThickness * 0.5f),
                        goldClear, goldDeep, goldDeep, goldClear);
                // Right rule: gold-deep at title edge \u2192 transparent at far edge
                if (rightEnd > rightStart)
                    dl.AddRectFilledMultiColor(
                        new Vector2(rightStart, ruleY - ruleThickness * 0.5f),
                        new Vector2(rightEnd,   ruleY + ruleThickness * 0.5f),
                        goldDeep, goldClear, goldClear, goldDeep);
            }

            // Description \u2014 Outfit 12 text-dim, CENTRE-aligned wrapping.
            // HTML .v3-title { text-align: center } centres every wrapped
            // line. ImGui's TextUnformatted left-aligns, so manually split
            // and draw each line centred.
            {
                float descMaxW = 480f * s;
                float descY = vaultY + vaultSz.Y + 18f * s;
                using (Plugin.Instance?.OutfitBody12?.Push())
                {
                    float fh = ImGui.GetFontSize();
                    float lineGap = 4f * s;
                    uint descColU = ImGui.ColorConvertFloat4ToU32(Boutique.TextDim);

                    // Simple word-wrap: accumulate words while under maxW.
                    var words = desc.Split(' ');
                    var line = new System.Text.StringBuilder();
                    float y = descY;
                    foreach (var word in words)
                    {
                        string candidate = line.Length == 0 ? word : line + " " + word;
                        float w = ImGui.CalcTextSize(candidate).X;
                        if (w > descMaxW && line.Length > 0)
                        {
                            // Flush current line centred
                            string drawn = line.ToString();
                            float lw = ImGui.CalcTextSize(drawn).X;
                            dl.AddText(new Vector2(centre.X - lw * 0.5f, y),
                                descColU, drawn);
                            y += fh + lineGap;
                            line.Clear();
                            line.Append(word);
                        }
                        else
                        {
                            if (line.Length > 0) line.Append(' ');
                            line.Append(word);
                        }
                    }
                    if (line.Length > 0)
                    {
                        string drawn = line.ToString();
                        float lw = ImGui.CalcTextSize(drawn).X;
                        dl.AddText(new Vector2(centre.X - lw * 0.5f, y),
                            descColU, drawn);
                    }
                }
            }
        }

        ImGui.EndChild();
    }

    // ═══════════════ FILTERING & SORTING ═══════════════

    private List<AchievementDefinition> GetFilteredAchievements(AchievementData data)
    {
        var list = selectedCategoryIndex == 0
            ? AchievementRegistry.All.ToList()
            : AchievementRegistry.All.Where(a => a.Category == CatTabs[selectedCategoryIndex].Cat).ToList();

        bool isSearching = !string.IsNullOrWhiteSpace(searchQuery);
        if (isSearching)
        {
            var q = searchQuery.Trim().ToLowerInvariant();
            list = list.Where(a =>
                a.Name.ToLowerInvariant().Contains(q) ||
                a.Description.ToLowerInvariant().Contains(q) ||
                a.Category.ToString().ToLowerInvariant().Contains(q)
            ).ToList();
        }

        if (rewardFilter == 1) list = list.Where(a => data.IsUnlocked(a.Id)).ToList();
        else if (rewardFilter == 2) list = list.Where(a => !data.IsUnlocked(a.Id)).ToList();
        else if (rewardFilter == 3) list = list.Where(a => !a.IsBonus).ToList();
        else if (rewardFilter == 4) list = list.Where(a => a.IsBonus).ToList();

        // Chain collapse: ladder achievements (char_*, design_*, social_likes_*) render as
        // ONE active card per chain instead of all 7 simultaneously. The active card is the
        // first uncompleted tier in the chain - it morphs forward as the user crosses
        // thresholds. Skipped for "Completed" filter and search (users want to see history
        // and find specific tiers there).
        bool collapseChains = !isSearching && rewardFilter != 1;
        if (collapseChains)
        {
            list = CollapseChains(list, data);
        }

        return list;
    }

    /// <summary>
    /// Filters out non-active ladder tiers. For each chain, keeps only the first
    /// uncompleted achievement (the user's current goal). Fully-completed chains
    /// have all their tiers removed entirely so the chain disappears from the grid.
    /// Non-chain achievements pass through untouched.
    /// </summary>
    private List<AchievementDefinition> CollapseChains(List<AchievementDefinition> list, AchievementData data)
    {
        // Build a set of "all chain member IDs" for fast skip-lookup
        var allChainIds = new HashSet<string>();
        foreach (var chain in MilestoneChains)
            foreach (var id in chain)
                allChainIds.Add(id);

        // Build a set of "active tier IDs" - the next-uncompleted tier per chain
        var activeChainIds = new HashSet<string>();
        foreach (var chain in MilestoneChains)
        {
            string? activeId = chain.FirstOrDefault(id => !data.IsUnlocked(id));
            if (activeId != null) activeChainIds.Add(activeId);
            // If null, every tier in this chain is unlocked - chain is hidden entirely
        }

        // Keep non-chain achievements as-is, plus any chain entry that's the active one
        return list.Where(a => !allChainIds.Contains(a.Id) || activeChainIds.Contains(a.Id)).ToList();
    }

    private List<AchievementDefinition> SortAchievements(List<AchievementDefinition> list, AchievementData data)
    {
        return sortMode switch
        {
            // Progress: descending = completed first, then most-progressed in-progress
            //           ascending  = locked + lowest-progress first
            0 => sortDescending
                ? list
                    .OrderByDescending(a => data.IsUnlocked(a.Id))
                    .ThenByDescending(a => data.UnlockedAchievements.TryGetValue(a.Id, out var dt) ? dt : DateTime.MinValue)
                    .ThenByDescending(a => GetProgress(a))
                    .ThenBy(a => a.Tier)
                    .ToList()
                : list
                    .OrderBy(a => data.IsUnlocked(a.Id))
                    .ThenBy(a => GetProgress(a))
                    .ThenBy(a => a.Tier)
                    .ToList(),

            // Points: descending = highest-value first, ascending = cheapest first
            1 => sortDescending
                ? list.OrderByDescending(a => a.Points).ThenBy(a => a.Name).ToList()
                : list.OrderBy(a => a.Points).ThenBy(a => a.Name).ToList(),

            // Recent (unlock date): descending = newest first, ascending = oldest first
            2 => sortDescending
                ? list.OrderByDescending(a => data.UnlockedAchievements.TryGetValue(a.Id, out var dt) ? dt : DateTime.MinValue).ToList()
                : list.OrderBy(a => data.UnlockedAchievements.TryGetValue(a.Id, out var dt) ? dt : DateTime.MinValue).ToList(),

            // Name: descending = Z→A, ascending = A→Z
            3 => sortDescending
                ? list.OrderByDescending(a => a.Name).ToList()
                : list.OrderBy(a => a.Name).ToList(),

            _ => list
        };
    }

    private float GetProgress(AchievementDefinition ach)
    {
        var data = plugin.Configuration.AchievementData;

        // Already completed = 100%
        if (data.IsUnlocked(ach.Id)) return 1f;

        // Milestone-based (char count, design count, likes)
        if (MilestoneTargets.TryGetValue(ach.Id, out int target))
        {
            // Get current count for this chain
            int current = 0;
            if (ach.Id.StartsWith("char_"))
                current = plugin.Characters.Count;
            else if (ach.Id.StartsWith("design_"))
                current = plugin.Characters.Sum(c => c.Designs?.Count ?? 0);
            // Likes: no live count available here

            // Only show progress on the NEXT uncompleted tier in the chain
            // e.g., if user has 8 chars and char_1/char_5 are done, only char_10 shows 8/10
            var chain = MilestoneChains.FirstOrDefault(c => c.Contains(ach.Id));
            if (chain != null)
            {
                // Find the first uncompleted achievement in this chain
                string? nextInChain = chain.FirstOrDefault(id => !data.IsUnlocked(id));
                // Only show progress on the next one. Others in the chain show nothing
                if (nextInChain != ach.Id) return 0f;
            }

            return target > 0 ? Math.Min((float)current / target, 1f) : 0;
        }

        // Boolean achievements: check if the condition is currently met
        // These return 1.0 if condition is met (tracker will unlock on next trigger),
        // or 0 if not yet done. This lets the UI show which ones are achievable.
        var config = plugin.Configuration;

        return ach.Id switch
        {
            // Designs
            "design_folder" => plugin.Characters.Any(c => c.DesignFolders?.Count > 0) ? 1f : 0f,
            "design_preview" => plugin.Characters.Any(c => c.Designs?.Any(d => !string.IsNullOrWhiteSpace(d.PreviewImagePath)) == true) ? 1f : 0f,

            // Profiles: check if any character has the relevant data
            "profile_bio" => plugin.Characters.Any(c => !string.IsNullOrWhiteSpace(c.RPProfile?.Bio)) ? 1f : 0f,
            "profile_image" => plugin.Characters.Any(c => !string.IsNullOrWhiteSpace(c.ImagePath)) ? 1f : 0f,
            "profile_bg" => plugin.Characters.Any(c =>
                !string.IsNullOrWhiteSpace(c.RPProfile?.BackgroundImage) ||
                !string.IsNullOrWhiteSpace(c.RPProfile?.BackgroundImageUrl) ||
                !string.IsNullOrWhiteSpace(c.RPProfile?.RPBackgroundImageUrl)) ? 1f : 0f,
            "profile_boxes" => plugin.Characters.Any(c =>
                (c.RPProfile?.LeftContentBoxes?.Count ?? 0) + (c.RPProfile?.RightContentBoxes?.Count ?? 0) >= 3) ? 1f : 0f,
            "profile_gallery" => plugin.Characters.Any(c => c.RPProfile?.Sharing == ProfileSharing.ShowcasePublic) ? 1f : 0f,
            "profile_effect" => plugin.Characters.Any(c => c.RPProfile?.Effects != null &&
                (c.RPProfile.Effects.CircuitBoard || c.RPProfile.Effects.Fireflies ||
                 c.RPProfile.Effects.FallingLeaves || c.RPProfile.Effects.Butterflies ||
                 c.RPProfile.Effects.Bats || c.RPProfile.Effects.Fire || c.RPProfile.Effects.Smoke)) ? 1f : 0f,

            // Automation
            "auto_assignment" => config.CharacterAssignments.Any() ? 1f : 0f,
            "auto_job" => config.EnableJobAssignments && config.JobAssignments.Any() ? 1f : 0f,
            "auto_group" => config.RandomGroups?.Any() == true ? 1f : 0f,
            "auto_macro" => plugin.Characters.Any(c => c.IsAdvancedMode) ? 1f : 0f,
            "auto_gearset" => config.EnableGearsetAssignments ? 1f : 0f,
            "auto_glamauto" => plugin.Characters.Any(c => !string.IsNullOrWhiteSpace(c.CharacterAutomation)) ? 1f : 0f,
            "auto_dialogue" => config.EnableDialogueIntegration ? 1f : 0f,
            "auto_cr" => config.EnableConflictResolution ? 1f : 0f,

            // Social
            "social_namesync" => config.EnableNameReplacement ? 1f : 0f,
            "social_seen" => config.AllowOthersToSeeMyCSName ? 1f : 0f,
            "social_follow" => config.FollowedPlayers?.Count > 0 ? 1f : 0f,
            "social_fav_gallery" => config.FavoriteSnapshots?.Count > 0 ? 1f : 0f,

            // Customization
            "custom_theme" => config.SelectedTheme == ThemeSelection.Custom ? 1f : 0f,
            "custom_alias" => plugin.Characters.Any(c => !string.IsNullOrWhiteSpace(c.Alias)) ? 1f : 0f,
            "custom_bgimage" => !string.IsNullOrWhiteSpace(config.CustomTheme?.BackgroundImagePath) ? 1f : 0f,
            "custom_preset" => config.ThemePresets?.Count > 0 ? 1f : 0f,

            // Profiles (new)
            "profile_pronouns" => plugin.Characters.Any(c => !string.IsNullOrWhiteSpace(c.RPProfile?.Pronouns)) ? 1f : 0f,
            "profile_color" => plugin.Characters.Any(c => c.NameplateColor != default) ? 1f : 0f,
            "profile_title" => plugin.Characters.Any(c =>
                !string.IsNullOrWhiteSpace(c.RPProfile?.Title) || !string.IsNullOrWhiteSpace(c.RPProfile?.Status)) ? 1f : 0f,
            "profile_connection" => plugin.Characters.Any(c =>
                (c.RPProfile?.LeftContentBoxes?.Any(b => b.LayoutType == ContentBoxLayoutType.Connections) ?? false) ||
                (c.RPProfile?.RightContentBoxes?.Any(b => b.LayoutType == ContentBoxLayoutType.Connections) ?? false)) ? 1f : 0f,

            // Discovery
            "discover_fav" => plugin.Characters.Any(c => c.IsFavorite) ? 1f : 0f,
            "discover_pose" => plugin.Characters.Any(c => c.IdlePoseIndex < 7) ? 1f : 0f,
            "discover_tags" => plugin.Characters.Any(c => c.Tags?.Count > 0) ? 1f : 0f,
            "discover_main" => !string.IsNullOrWhiteSpace(config.MainCharacterName) ? 1f : 0f,

            _ => 0f
        };
    }

    private static void CenterText(string text, Vector4 colour)
    {
        float w = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (w - ImGui.CalcTextSize(text).X) / 2);
        ImGui.TextColored(colour, text);
    }
}

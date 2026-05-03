using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using CharacterSelectPlugin.Achievements;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin.Windows;

/// <summary>
/// Achievement toast, Codex chassis revamp. Renders the "Angled Slip" silhouette
/// (420x100, opposing TR+BL chamfers) with a cyberpunk Stamp theme:
///   - Drops from above on a spring curve, a gold scanner line sweeps L→R during
///     impact, CRT scanlines settle to clean, then the toast shatters into
///     deterministic seeded chromatic slabs on exit.
/// Stacks up to 3 simultaneously, click to dismiss early.
/// </summary>
public class CSAchievementToast : Window, IDisposable
{
    private readonly Plugin plugin;

    // ── Layout (mockup-matched, pre-scale) ──────────────────────────────
    private const int   MaxStack         = 3;
    private const float ToastWidthBase   = 420f;
    private const float ToastHeightBase  = 100f;
    private const float ToastChamfer     = 20f;
    private const float ToastMargin      = 16f;
    private const float ToastSpacing     = 10f;

    // ── Phase lifecycle ────────────────────────────────────────────────
    // Stretched from the mockup's 4s loop to give the animation breathing
    // room: entrance scan (~620ms), comfortable hold (~5.6s), exit shatter
    // (~780ms). Normalised 0..1 across the full lifetime.
    private const float LifetimeSeconds = 7.0f;
    private const float ExitStart       = 0.89f; // fraction at which exit VFX begin (matches mockup's 85-96%/4s ratio)
    private const float SpawnStagger    = 1.10f; // gap between successive spawns so stacks don't overlap phases

    // ── Static bits ─────────────────────────────────────────────────────
    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static Vector4 A(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);
    private static uint U32(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);

    // ── State ────────────────────────────────────────────────────────────
    private class ToastInstance
    {
        public AchievementDefinition Achievement = null!;
        public DateTime SpawnTime;
        public float CurrentY;
        public float TargetY;
        public bool DismissRequested;

        // Deterministic per-toast procedural shatter. Seeded from achievement Id
        // so the same achievement always shatters the same way.
        public int Seed;
        public ShatterLayout Shatter = null!;
    }

    private static float Phase(ToastInstance t, DateTime now) =>
        Math.Clamp((float)((now - t.SpawnTime).TotalSeconds) / LifetimeSeconds, 0f, 1f);

    private readonly List<ToastInstance> activeToasts = new();
    private readonly Queue<AchievementDefinition> pendingQueue = new();
    private DateTime lastSpawnTime = DateTime.MinValue;

    public CSAchievementToast(Plugin plugin) : base("###CSAchievementToast",
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
        ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoBackground |
        ImGuiWindowFlags.NoDocking)
    {
        this.plugin = plugin;
        IsOpen = false;
        RespectCloseHotkey = false;
    }

    public void Dispose() { }

    public void Enqueue(AchievementDefinition ach)
    {
        if (ach == null) return;
        pendingQueue.Enqueue(ach);
        IsOpen = true;
    }

    public override void PreDraw()
    {
        var viewport = ImGui.GetMainViewport();
        var s = Math.Clamp(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier, 0.5f, 3f);

        float toastW = ToastWidthBase * s;
        float toastH = ToastHeightBase * s;
        float spacing = ToastSpacing * s;
        float margin = ToastMargin * s;

        // Generous fx pad so shatter slabs that displace horizontally can
        // overdraw the toast edges without being clipped by the window.
        float fxPad = 80f * s;
        float winW = toastW + margin * 2 + fxPad * 2;
        float winH = toastH * MaxStack + spacing * (MaxStack - 1) + margin * 2 + fxPad * 2;

        var pos = plugin.Configuration.AchievementToastPosition;
        Vector2 windowPos;
        switch (pos)
        {
            case Configuration.ToastPosition.BottomLeft:
                windowPos = new Vector2(viewport.Pos.X - fxPad,
                    viewport.Pos.Y + viewport.Size.Y - winH + fxPad);
                break;
            case Configuration.ToastPosition.TopRight:
                windowPos = new Vector2(viewport.Pos.X + viewport.Size.X - winW + fxPad,
                    viewport.Pos.Y - fxPad);
                break;
            case Configuration.ToastPosition.TopLeft:
                windowPos = new Vector2(viewport.Pos.X - fxPad, viewport.Pos.Y - fxPad);
                break;
            case Configuration.ToastPosition.TopCenter:
                windowPos = new Vector2(viewport.Pos.X + (viewport.Size.X - winW) * 0.5f,
                    viewport.Pos.Y - fxPad);
                break;
            case Configuration.ToastPosition.BottomCenter:
                windowPos = new Vector2(viewport.Pos.X + (viewport.Size.X - winW) * 0.5f,
                    viewport.Pos.Y + viewport.Size.Y - winH + fxPad);
                break;
            case Configuration.ToastPosition.BottomRight:
            default:
                windowPos = new Vector2(viewport.Pos.X + viewport.Size.X - winW + fxPad,
                    viewport.Pos.Y + viewport.Size.Y - winH + fxPad);
                break;
        }

        ImGui.SetNextWindowPos(windowPos);
        ImGui.SetNextWindowSize(new Vector2(winW, winH));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar();
    }

    public override void Draw()
    {
        var s = Math.Clamp(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier, 0.5f, 3f);
        float toastW  = ToastWidthBase  * s;
        float toastH  = ToastHeightBase * s;
        float spacing = ToastSpacing    * s;
        float margin  = ToastMargin     * s;
        float fxPad   = 80f             * s;

        var now = DateTime.UtcNow;
        float dt = Math.Max(0.001f, ImGui.GetIO().DeltaTime);

        var pos = plugin.Configuration.AchievementToastPosition;
        bool isTop = pos == Configuration.ToastPosition.TopRight
                  || pos == Configuration.ToastPosition.TopLeft
                  || pos == Configuration.ToastPosition.TopCenter;

        // Drain pending queue. One spawn per frame, gated by stagger.
        if (activeToasts.Count < MaxStack && pendingQueue.Count > 0)
        {
            float sinceLastSpawn = (float)(now - lastSpawnTime).TotalSeconds;
            if (sinceLastSpawn >= SpawnStagger)
            {
                var ach = pendingQueue.Dequeue();
                int seed = Boutique.HashSeed(ach.Id);
                activeToasts.Add(new ToastInstance
                {
                    Achievement = ach,
                    SpawnTime   = now,
                    CurrentY    = 0f,
                    TargetY     = 0f,
                    Seed        = seed,
                    Shatter     = ShatterLayout.Build(seed, ToastWidthBase, ToastHeightBase),
                });
                lastSpawnTime = now;
            }
        }

        // Click dismissal: snap phase forward to the exit window.
        foreach (var t in activeToasts)
        {
            if (t.DismissRequested)
            {
                float phaseNow = Phase(t, now);
                if (phaseNow < ExitStart)
                {
                    float desired = ExitStart * LifetimeSeconds;
                    t.SpawnTime = now - TimeSpan.FromSeconds(desired);
                }
                t.DismissRequested = false;
            }
        }
        activeToasts.RemoveAll(t => Phase(t, now) >= 1f);

        for (int i = 0; i < activeToasts.Count; i++)
        {
            int stackPos = activeToasts.Count - 1 - i;
            float step = stackPos * (toastH + spacing);
            activeToasts[i].TargetY = isTop ? step : -step;
        }

        float lerp = 1f - MathF.Exp(-14f * dt);
        foreach (var t in activeToasts)
            t.CurrentY += (t.TargetY - t.CurrentY) * lerp;

        var dl = ImGui.GetForegroundDrawList();
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();

        float baselineX = winPos.X + margin + fxPad;
        float baselineY = isTop
            ? winPos.Y + margin + fxPad
            : winPos.Y + winSize.Y - margin - fxPad - toastH;

        for (int i = 0; i < activeToasts.Count; i++)
        {
            var t = activeToasts[i];
            var toastPos = new Vector2(baselineX, baselineY + t.CurrentY);
            var toastSize = new Vector2(toastW, toastH);

            DrawStamp(dl, toastPos, toastSize, t, s, now);

            ImGui.SetCursorScreenPos(toastPos);
            if (ImGui.InvisibleButton($"##cstoast{i}_{t.Achievement.Id}", toastSize))
                t.DismissRequested = true;
        }

        if (activeToasts.Count == 0 && pendingQueue.Count == 0)
            IsOpen = false;
    }

    // STAMP · cyberpunk digital scan + shatter exit
    // Mockup phase timeline (was 4s loop, here stretched over 7s lifetime):
    //   0        → 0.033   DROP        scale 1.55 → 1.0, Y from -80 to 0
    //   0.055    impact    hard hit
    //   0.055    → 0.090   BOUNCE      0.92 → 1.05 → 0.995 → 1.0
    //   0.090    → ExitStart HOLD      gentle breath
    //   ExitStart → 1.0    EXIT        shatter dissolve
    private void DrawStamp(ImDrawListPtr dl, Vector2 basePos, Vector2 size, ToastInstance t, float s, DateTime now)
    {
        float phase = Phase(t, now);

        // Container motion + pulse bloom (used by DrawToastBody to brighten
        // the border/rail so the pulse reads as a visible swell instead of
        // a sub-pixel scale change).
        float yOffset = 0f;
        float toastScale = 1f;
        float pulseBloom = 0f; // 0..1, peak at mid-hold

        if (phase < 0.055f)
        {
            float p = phase / 0.055f;
            yOffset = -80f * s * (1f - EaseInCubic(p));
            toastScale = 1.55f - (1.55f - 1.0f) * p;
        }
        else if (phase < 0.09f)
        {
            float p = (phase - 0.055f) / 0.035f;
            toastScale = BounceCurve(p);
        }
        else if (phase < ExitStart)
        {
            // Mid-hold pulse - alpha-only. Scale-based pulses in ImGui are
            // sub-pixel jitter (the toast edge snaps to integer pixels so
            // a 0.6-1.5% scale change just shifts 1-2px awkwardly). Instead
            // pulseBloom ramps the border + gold rail brightness for a
            // brief swell at mid-hold. No geometry change at all.
            float holdP = (phase - 0.09f) / (ExitStart - 0.09f);
            // Short, sharp window centred at mid-hold: full pulse is ~300ms
            // across (0.04 hold-space * 7s = 0.28s). Gaussian sigma 0.02.
            float pulseCenter = 0.50f;
            float pulseWidth  = 0.025f;
            float d = (holdP - pulseCenter) / pulseWidth;
            pulseBloom = MathF.Exp(-d * d);
            toastScale = 1f;
        }
        else
        {
            toastScale = 1f;
        }

        var center = basePos + size * 0.5f + new Vector2(0, yOffset);
        var scaledSize = size * toastScale;
        var mn = center - scaledSize * 0.5f;
        var mx = center + scaledSize * 0.5f;
        float chamfer = ToastChamfer * s * toastScale;

        // Overall alpha
        float alpha = 1f;
        if (phase < 0.02f) alpha = phase / 0.02f;
        else if (phase >= 0.995f) alpha = Math.Max(0f, 1f - (phase - 0.995f) / 0.005f);

        // Main toast body
        DrawToastBody(dl, mn, mx, chamfer, t.Achievement.Category, alpha, s, drawContent: true, toast: t, pulseBloom: pulseBloom);

        // Entrance digital scan (5.5% → 16% phase)
        if (phase >= 0.055f && phase < 0.18f)
        {
            DrawStampScanFX(dl, mn, mx, chamfer, phase, alpha, s);
        }

        // Exit shatter (from ExitStart onward)
        if (phase >= ExitStart)
        {
            DrawStampShatter(dl, mn, mx, chamfer, phase, t, s);
        }
    }

    private static float BounceCurve(float p)
    {
        // (0, 1.0), (0.2, 0.92), (0.55, 1.05), (0.8, 0.995), (1, 1)
        if (p < 0.2f) return Lerp(1.0f, 0.92f, p / 0.2f);
        if (p < 0.55f) return Lerp(0.92f, 1.05f, (p - 0.2f) / 0.35f);
        if (p < 0.80f) return Lerp(1.05f, 0.995f, (p - 0.55f) / 0.25f);
        return Lerp(0.995f, 1.0f, (p - 0.80f) / 0.20f);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
    private static float EaseInCubic(float p) => p * p * p;

    // ── Shared toast body (icon, text, points, gold rail) ─────────────
    private void DrawToastBody(ImDrawListPtr dl, Vector2 mn, Vector2 mx, float chamfer,
        AchievementCategory cat, float alpha, float s, bool drawContent, ToastInstance? toast = null,
        float pulseBloom = 0f)
    {
        if (alpha <= 0f) return;

        var catCol = Boutique.CategoryColor(cat);

        // Drop shadow: 4 filled slips offset down only, body fill covers the
        // overlapping portion so only the falloff below the toast remains.
        for (int sh = 4; sh >= 1; sh--)
        {
            float sy = sh * 2.5f * s;     // vertical offset only - never above
            float sx = sh * 0.6f * s;     // tiny lateral spread so the shadow softens at the sides
            float sa = 0.18f * alpha / sh; // decaying alpha outward
            var smn = new Vector2(mn.X - sx, mn.Y + sy);
            var smx = new Vector2(mx.X + sx, mx.Y + sy);
            Boutique.FillSlip(dl, smn, smx, chamfer, U32(new Vector4(0f, 0f, 0f, sa)));
        }

        // BODY FILL - flat slip polygon. The HTML's 145deg 10% category tint
        // is too subtle to bother approximating, and my earlier corner-tint
        // triangles extended hundreds of pixels above the toast (the pink
        // shape you circled: reach was `diag * 0.82` which for a 420×100
        // toast is ~354px, far above the 100px toast height).
        var baseCol = A(Boutique.Surface0, 0.96f * alpha);
        Boutique.FillSlip(dl, mn, mx, chamfer, U32(baseCol));

        // BORDER - 1px inset rail at mix(c 55%, transparent). Brightens at
        // pulse peak (up to +35% alpha) so the breath swell is visible even
        // when the 1.5% scale change is sub-pixel.
        float borderAlpha = (0.55f + 0.35f * pulseBloom) * alpha;
        Boutique.StrokeSlip(dl, mn, mx, chamfer, U32(A(catCol, Math.Min(1f, borderAlpha))), 1f);

        // LEFT 3PX GOLD RAIL (stops short of the BL chamfer). Also bloom-
        // brightens with the pulse, so the swell reads as "the rail flares".
        float railAlpha = Math.Min(1f, (0.90f + 0.10f * pulseBloom) * alpha);
        dl.AddRectFilled(
            new Vector2(mn.X, mn.Y),
            new Vector2(mn.X + 3f * s, mx.Y - chamfer),
            U32(A(Boutique.Gold, railAlpha)));
        // Top gold hairline, stops short of the TR chamfer
        dl.AddRectFilled(
            new Vector2(mn.X, mn.Y),
            new Vector2(mx.X - chamfer, mn.Y + 1f * s),
            U32(A(Boutique.Gold, Math.Min(1f, (0.50f + 0.40f * pulseBloom) * alpha))));

        if (!drawContent || toast == null) return;
        var ach = toast.Achievement;

        // ── ICON TILE (52x52, 18px inset) ──
        float iconInset = 18f * s;
        float iconSize = 52f * s;
        var iconMn = new Vector2(mn.X + iconInset, (mn.Y + mx.Y) * 0.5f - iconSize * 0.5f);
        var iconMx = iconMn + new Vector2(iconSize, iconSize);
        var iconBg = A(Boutique.Mix(catCol, 0.30f, Boutique.Surface0), 0.92f * alpha);
        dl.AddRectFilled(iconMn, iconMx, U32(iconBg));
        dl.AddRect(iconMn, iconMx, U32(A(catCol, 0.95f * alpha)), 0f, ImDrawFlags.None, 1f);
        dl.AddRect(
            new Vector2(iconMn.X - 2f * s, iconMn.Y - 2f * s),
            new Vector2(iconMx.X + 2f * s, iconMx.Y + 2f * s),
            U32(A(catCol, 0.28f * alpha)), 0f, ImDrawFlags.None, 2f);

        string glyph = Boutique.CategoryIcon(ach.Category);
        ImGui.PushFont(UiBuilder.IconFont);
        var glyphSz = ImGui.CalcTextSize(glyph);
        ImGui.PopFont();
        float glyphFontSz = 22f * s;
        float glyphScale = glyphFontSz / UiBuilder.IconFont.FontSize;
        var glyphDrawSz = glyphSz * glyphScale;
        var iconCenter = (iconMn + iconMx) * 0.5f;
        dl.AddText(UiBuilder.IconFont, glyphFontSz,
            new Vector2(iconCenter.X - glyphDrawSz.X * 0.5f,
                        iconCenter.Y - glyphDrawSz.Y * 0.5f),
            U32(A(catCol, alpha)), glyph);

        // ── TEXT BODY ──
        // All text rendered at the NATIVE rasterized size of its pushed font
        // (ImGui.GetFontSize() after Push). Asking ImGui to draw text at a
        // custom fontSize scales the glyph atlas, producing blur - the user
        // explicitly flagged the blurry scaling. Sharpness is worth the minor
        // size-vs-mockup mismatch (default font renders a bit larger than the
        // mockup's 10/12px kicker/desc, but stays crisp).
        float textL = mn.X + 84f * s;
        float textR = mx.X - 84f * s;
        float textTop = mn.Y + 12f * s;

        const string kicker = "ACHIEVEMENT UNLOCKED";
        var kickerCol = U32(A(Boutique.Gold, 0.95f * alpha));
        var kickerSz = ImGui.CalcTextSize(kicker);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(textL, textTop), kickerCol, kicker);
        float kickerH = kickerSz.Y + 2f * s;

        ImGui.PushClipRect(new Vector2(textL, mn.Y), new Vector2(textR, mx.Y), true);
        var nameCol = U32(A(Boutique.Text, alpha));
        float nameH = 0f;
        using (Plugin.Instance?.HeaderFont?.Push())
        {
            var nameSz = ImGui.CalcTextSize(ach.Name);
            nameH = nameSz.Y;
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                new Vector2(textL, textTop + kickerH), nameCol, ach.Name);
        }
        string desc = ach.GetDescriptionFor(unlocked: true) ?? "";
        if (!string.IsNullOrWhiteSpace(desc))
        {
            float descY = textTop + kickerH + nameH + 2f * s;
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                new Vector2(textL, descY),
                U32(A(Boutique.TextFaint, 0.90f * alpha)), desc);
        }
        ImGui.PopClipRect();

        // ── POINTS BLOCK ──
        // "+N" uses ToastPointsFont (rasterized at 36px, sharp at that size).
        // "PTS" uses the default font at native size (crisp, bigger than the
        // mockup's 9px but readable).
        string ptsNumber = $"+{ach.Points}";
        const string ptsUnit = "PTS";
        float ptsRight = mx.X - 18f * s;
        var midY = (mn.Y + mx.Y) * 0.5f;

        Vector2 ptsSz;
        float ptsFontSize;
        using (Plugin.Instance?.ToastPointsFont?.Push())
        {
            ptsSz = ImGui.CalcTextSize(ptsNumber);
            ptsFontSize = ImGui.GetFontSize();
        }
        var unitSz = ImGui.CalcTextSize(ptsUnit);

        float blockH = ptsSz.Y + 2f * s + unitSz.Y;
        float blockTop = midY - blockH * 0.5f;

        using (Plugin.Instance?.ToastPointsFont?.Push())
        {
            var ptsAnchor = new Vector2(ptsRight - ptsSz.X, blockTop);
            // Text-shadow approximation: two faint offset copies behind the
            // crisp number. Kept subtle so they don't bloom into a halo.
            for (int shadow = 0; shadow < 2; shadow++)
            {
                float off = (shadow + 1) * 1.5f * s;
                dl.AddText(ImGui.GetFont(), ptsFontSize,
                    new Vector2(ptsAnchor.X - off, ptsAnchor.Y),
                    U32(A(Boutique.GoldWarm, 0.18f * alpha)), ptsNumber);
                dl.AddText(ImGui.GetFont(), ptsFontSize,
                    new Vector2(ptsAnchor.X + off, ptsAnchor.Y),
                    U32(A(Boutique.GoldWarm, 0.18f * alpha)), ptsNumber);
            }
            dl.AddText(ImGui.GetFont(), ptsFontSize, ptsAnchor,
                U32(A(Boutique.Gold, alpha)), ptsNumber);
        }
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(ptsRight - unitSz.X, blockTop + ptsSz.Y + 2f * s),
            U32(A(Boutique.GoldDeep, 0.95f * alpha)), ptsUnit);
    }

    // ── Digital scan FX ─────────────────────────────────────────────────
    private void DrawStampScanFX(ImDrawListPtr dl, Vector2 mn, Vector2 mx, float chamfer,
        float phase, float alpha, float s)
    {
        const float scanStart = 0.055f;
        const float scanEnd = 0.16f;
        const float crtEnd = 0.18f;
        float p = (phase - scanStart) / (scanEnd - scanStart);
        p = Math.Clamp(p, 0f, 1f);

        dl.PushClipRect(mn, mx, true);

        float scannerX = mn.X + p * (mx.X - mn.X);

        // Scan band (trailing wash)
        if (phase < scanEnd)
        {
            float bandAlphaBase = 1f;
            if (phase > scanEnd - 0.01f) bandAlphaBase = 1f - (phase - (scanEnd - 0.01f)) / 0.01f;
            bandAlphaBase = Math.Clamp(bandAlphaBase, 0f, 1f) * alpha;
            if (bandAlphaBase > 0.01f && scannerX > mn.X + 1f)
            {
                uint leftU  = U32(A(Boutique.Gold, 0f));
                uint midU   = U32(A(Boutique.Gold, 0.08f * bandAlphaBase));
                uint rightU = U32(A(new Vector4(1f, 0.94f, 0.70f, 1f), 0.32f * bandAlphaBase));
                dl.AddRectFilledMultiColor(
                    new Vector2(mn.X, mn.Y), new Vector2(scannerX, mx.Y),
                    leftU, rightU, rightU, leftU);
            }
        }

        // CRT scanlines
        if (phase >= scanStart && phase < crtEnd)
        {
            float crtAlpha;
            if (phase < 0.10f) crtAlpha = Lerp(0.70f, 0.55f, (phase - scanStart) / 0.045f);
            else if (phase < scanEnd) crtAlpha = Lerp(0.55f, 0.25f, (phase - 0.10f) / (scanEnd - 0.10f));
            else crtAlpha = Lerp(0.25f, 0f, (phase - scanEnd) / (crtEnd - scanEnd));
            crtAlpha *= alpha;

            if (crtAlpha > 0.01f)
            {
                float pitch = 3f * s;
                float stripeH = 1f * s;
                uint stripeU = U32(A(Boutique.Gold, 0.14f * crtAlpha));
                for (float y = mn.Y; y < mx.Y; y += pitch)
                    dl.AddRectFilled(new Vector2(mn.X, y), new Vector2(mx.X, y + stripeH), stripeU);
            }
        }

        // Scanner line (2px vertical bar with vertical gradient)
        if (phase >= scanStart && phase <= scanEnd + 0.003f)
        {
            float hBar = mx.Y - mn.Y;
            uint topU    = U32(A(Boutique.GoldDeep, 0.30f * alpha));
            uint midTopU = U32(A(Boutique.GoldWarm, 0.85f * alpha));
            uint whiteU  = U32(A(White, alpha));
            uint midBotU = U32(A(Boutique.GoldWarm, 0.85f * alpha));
            uint botU    = U32(A(Boutique.GoldDeep, 0.30f * alpha));

            float y0 = mn.Y;
            float[] stops = new[] { 0f, hBar * 0.18f, hBar * 0.42f, hBar * 0.58f, hBar * 0.82f, hBar };
            uint[] colors = new[] { topU, midTopU, whiteU, whiteU, midBotU, botU };
            for (int seg = 0; seg < 5; seg++)
            {
                dl.AddRectFilledMultiColor(
                    new Vector2(scannerX, y0 + stops[seg]),
                    new Vector2(scannerX + 2f * s, y0 + stops[seg + 1]),
                    colors[seg], colors[seg], colors[seg + 1], colors[seg + 1]);
            }

            uint glowU = U32(A(Boutique.Gold, 0.35f * alpha));
            uint glowFade = U32(A(Boutique.Gold, 0f));
            dl.AddRectFilledMultiColor(
                new Vector2(scannerX - 10f * s, mn.Y), new Vector2(scannerX, mx.Y),
                glowFade, glowU, glowU, glowFade);
            dl.AddRectFilledMultiColor(
                new Vector2(scannerX + 2f * s, mn.Y), new Vector2(scannerX + 12f * s, mx.Y),
                glowU, glowFade, glowFade, glowU);
        }

        dl.PopClipRect();
    }

    // ── Shatter exit ────────────────────────────────────────────────────
    // Matched to mockup keyframes (scaled to our ExitStart..1.0 exit window).
    // HTML reference: 4s loop, exit 85-96% = 440ms. Our exit window (ExitStart..1.0
    // = 0.11 of 7s = 770ms) is slightly longer but keyframe ratios are preserved.
    private void DrawStampShatter(ImDrawListPtr dl, Vector2 mn, Vector2 mx, float chamfer,
        float phase, ToastInstance t, float s)
    {
        float exitT = (phase - ExitStart) / (1f - ExitStart); // 0..1 across exit window
        if (exitT <= 0f) return;

        uint shellU = U32(Boutique.Shell);
        var shatter = t.Shatter;
        uint goldSpeckU = U32(Boutique.Gold);

        float toastW = mx.X - mn.X;
        float toastH = mx.Y - mn.Y;

        // Clip slabs/vshears/dropouts to the toast bounds; their overhang must
        // not leak past. ImGui only does rect clipping so the TR/BL chamfer
        // corners may show a sliver of black, acceptable.
        dl.PushClipRect(mn, mx, true);

        // Each slab has its own [TearAt, VanishAt] window so they tear/vanish
        // staggered instead of stacking into one opaque block.
        foreach (var slab in shatter.Slabs)
        {
            if (exitT < slab.TearAt || exitT >= slab.VanishAt) continue;

            float shiftX = slab.Tx * s;
            float slabY = mn.Y + slab.Y * s;
            float slabH = slab.H * s;

            var slabMn = new Vector2(mn.X + shiftX - 60f * s, slabY);
            var slabMx = new Vector2(mx.X + shiftX + 60f * s, slabY + slabH);

            dl.AddRectFilled(slabMn, slabMx, shellU);

            // Edge fringes - magenta on leading edge, cyan on trailing. HTML
            // slab-chroma starts ~0.05 exit-space after slab-tear and tapers
            // 0.85 → 0.60 during hold, vanishes with the slab.
            float chromaLead = Math.Max(0f, exitT - slab.TearAt - 0.05f);
            float slabWindow = slab.VanishAt - slab.TearAt;
            if (chromaLead > 0f && slabWindow > 0.001f)
            {
                float holdP = Math.Clamp(chromaLead / slabWindow, 0f, 1f);
                float fringeAlpha = Lerp(0.85f, 0.60f, holdP);
                uint magU = U32(A(Boutique.GlitchMagenta, fringeAlpha));
                uint cyanU = U32(A(Boutique.GlitchCyan, fringeAlpha));
                dl.AddRectFilled(
                    new Vector2(slabMn.X - 4f * s, slabMn.Y),
                    new Vector2(slabMn.X, slabMx.Y), magU);
                dl.AddRectFilled(
                    new Vector2(slabMx.X, slabMn.Y),
                    new Vector2(slabMx.X + 4f * s, slabMx.Y), cyanU);
            }

            // Chunks - inherit the slab's stagger. HTML chunk-pop keyframe is
            // 87-94.5% of the 4s cycle, tied to the slab via shared delay, so
            // they appear ~0.18 exit-space after the slab and vanish with it.
            foreach (var chunk in slab.Chunks)
            {
                float chunkOnset = slab.TearAt + 0.18f + chunk.DelayOffset;
                if (exitT < chunkOnset || exitT >= slab.VanishAt) continue;
                float chunkX = mn.X + chunk.X * s + shiftX;
                dl.AddRectFilled(
                    new Vector2(chunkX, slabY),
                    new Vector2(chunkX + chunk.W * s, slabY + slabH), shellU);
            }
        }

        // Shared fringe colour uniforms for dust/dropout/vshear below
        uint magentaFringeU = U32(A(Boutique.GlitchMagenta, 0.92f));
        uint cyanFringeU    = U32(A(Boutique.GlitchCyan, 0.92f));

        // Each dust speck runs the 5-interval flicker pattern shifted by its
        // own DelayOffset so they all flicker out of phase
        foreach (var d in shatter.Dust)
        {
            float localT = exitT - d.DelayOffset;
            if (!IsDustOn(localT)) continue;

            var pt = new Vector2(mn.X + d.X * s, mn.Y + d.Y * s);
            uint col = d.Color switch
            {
                0 => magentaFringeU,
                1 => cyanFringeU,
                2 => goldSpeckU,
                _ => U32(White),
            };
            dl.AddRectFilled(pt, pt + new Vector2(1f * s, 1f * s), col);
        }

        // DROPOUT BARS - 3 beats with per-beat horizontal jitter
        foreach (var bar in shatter.Dropouts)
        {
            // Pulse schedule converted from HTML keyframes (85.3/87.2/89.4%
            // in 4s loop) into exit-space (0..1 across 0.85..0.96):
            //   (85.3-85)/11 = 0.027, (87.2-85)/11 = 0.200, (89.4-85)/11 = 0.400
            float a = 0f;
            int pulseIdx = -1;
            float[] pulses = { 0.027f, 0.200f, 0.400f };
            for (int k = 0; k < 3; k++)
            {
                float d = MathF.Abs(exitT - pulses[k]);
                if (d < 0.03f) { float na = 1f - d / 0.03f; if (na > a) { a = na; pulseIdx = k; } }
            }
            if (a < 0.01f || pulseIdx < 0) continue;

            float tx = pulseIdx switch
            {
                0 => bar.TxJitter[0],
                1 => bar.TxJitter[1],
                _ => bar.TxJitter[2],
            } * s;

            float by = mn.Y + bar.Y * s;
            float bh = bar.H * s;
            float bxLeft = mn.X - 20f * s + tx;
            float bxRight = mx.X + 20f * s + tx;

            float x = bxLeft;
            while (x < bxRight)
            {
                DrawDropoutPattern(dl, x, by, bh, s, a);
                x += 20f * s;
            }
            dl.AddRectFilled(
                new Vector2(bxLeft, by - 2f * s),
                new Vector2(bxRight, by),
                U32(A(Boutique.GlitchMagenta, 0.85f * a)));
            dl.AddRectFilled(
                new Vector2(bxLeft, by + bh),
                new Vector2(bxRight, by + bh + 2f * s),
                U32(A(Boutique.GlitchCyan, 0.85f * a)));
        }

        // VERTICAL SHEARS - each has per-vshear onset AND VanishAt. HTML
        // vshear-cut is visible 88-95.5% = 7.5% of 4s loop = 0.68 in exit-space
        // per vshear. With staggered onsets, this means vshears disappear
        // individually rather than all at once at exitT=0.96.
        foreach (var vs in shatter.Vshears)
        {
            if (exitT < vs.OnsetAt || exitT >= vs.VanishAt) continue;
            float vx = mn.X + vs.X * s;
            float vw = vs.W * s;
            float vtop = mn.Y - 30f * s + vs.Ty * s;
            float vbot = mx.Y + 30f * s + vs.Ty * s;
            dl.AddRectFilled(new Vector2(vx, vtop), new Vector2(vx + vw, vbot), shellU);
            dl.AddRectFilled(
                new Vector2(vx, vtop - 3f * s),
                new Vector2(vx + vw, vtop), cyanFringeU);
            dl.AddRectFilled(
                new Vector2(vx, vbot),
                new Vector2(vx + vw, vbot + 3f * s), magentaFringeU);
        }

        // SURVIVOR SPECKS - global hard-blink schedule (HTML: on at 95.5,
        // off at 96, on at 96.5, off at 97.5 → exit-space 0.955 / 0.96 / 0.965 / 0.975
        // within our window). Simplified to two pulses at the very end.
        {
            bool on = (exitT >= 0.955f && exitT < 0.970f)
                   || (exitT >= 0.980f && exitT < 0.995f);
            if (on)
            {
                foreach (var speck in shatter.Specks)
                {
                    var pt = new Vector2(mn.X + speck.X * s, mn.Y + speck.Y * s);
                    dl.AddRectFilled(pt, pt + new Vector2(2f * s, 2f * s), goldSpeckU);
                    dl.AddRectFilled(
                        pt - new Vector2(1f * s, 1f * s),
                        pt + new Vector2(3f * s, 3f * s),
                        U32(A(Boutique.Gold, 0.35f)));
                }
            }
        }

        dl.PopClipRect();
    }

    // HTML dust-flick on-intervals in exit-space [0..1]. Converted from the
    // 4s-loop keyframes (85.4/85.9, 86.7/87.1, 88.3/88.7, 90.0/90.4, 92.0/92.3)
    // via (x-85)/11. Each per-speck DelayOffset is added to shift this pattern
    // so specks flicker out of phase like the HTML.
    private static readonly (float Start, float End)[] DustOnIntervals =
    {
        (0.036f, 0.082f),
        (0.155f, 0.191f),
        (0.300f, 0.336f),
        (0.455f, 0.491f),
        (0.636f, 0.664f),
    };

    private static bool IsDustOn(float t)
    {
        foreach (var interval in DustOnIntervals)
            if (t >= interval.Start && t < interval.End) return true;
        return false;
    }

    private static void DrawDropoutPattern(ImDrawListPtr dl, float x, float y, float h, float s, float alpha)
    {
        uint black = U32(A(new Vector4(0, 0, 0, 1), alpha));
        uint white = U32(A(new Vector4(1, 1, 1, 1), 0.12f * alpha));
        uint cyan = U32(A(Boutique.GlitchCyan, 0.55f * alpha));
        uint magenta = U32(A(Boutique.GlitchMagenta, 0.55f * alpha));
        uint gold = U32(A(Boutique.Gold, alpha));

        void R(float x0, float x1, uint c) =>
            dl.AddRectFilled(new Vector2(x + x0 * s, y), new Vector2(x + x1 * s, y + h), c);

        R(0, 3, black);
        R(3, 4, white);
        R(4, 9, black);
        R(9, 10, cyan);
        R(10, 14, black);
        R(14, 15, magenta);
        R(15, 19, black);
        R(19, 20, gold);
    }

    // PROCEDURAL SHATTER LAYOUT (seeded per achievement)
    private struct ShatterSlab
    {
        public float Y;
        public float H;
        public float Tx;
        public float TearAt;
        public float VanishAt; // per-slab disappearance in exit-space
        public ShatterChunk[] Chunks;
    }
    private struct ShatterChunk { public float X; public float W; public float DelayOffset; }
    private struct ShatterDust  { public float X; public float Y; public int Color; public float DelayOffset; }
    private struct ShatterDropout
    {
        public float Y;
        public float H;
        public float[] TxJitter;
    }
    private struct ShatterVshear
    {
        public float X;
        public float W;
        public float Ty;
        public float OnsetAt;
        public float VanishAt;
    }
    private struct ShatterSpeck  { public float X; public float Y; public float PhaseOffset; }

    private class ShatterLayout
    {
        public ShatterSlab[] Slabs = Array.Empty<ShatterSlab>();
        public ShatterDust[] Dust = Array.Empty<ShatterDust>();
        public ShatterDropout[] Dropouts = Array.Empty<ShatterDropout>();
        public ShatterVshear[] Vshears = Array.Empty<ShatterVshear>();
        public ShatterSpeck[] Specks = Array.Empty<ShatterSpeck>();

        public static ShatterLayout Build(int seed, float toastW, float toastH)
        {
            var rng = new Boutique.Mulberry32(seed * 9301 + 49297);
            var L = new ShatterLayout();

            // Slabs
            var slabs = new List<ShatterSlab>();
            float y = 0f;
            while (y < toastH)
            {
                float p = rng.Next();
                float h;
                if (p < 0.40f) h = rng.RangeI(2, 5);
                else if (p < 0.72f) h = rng.RangeI(6, 14);
                else if (p < 0.92f) h = rng.RangeI(15, 28);
                else h = rng.RangeI(29, 48);
                h = Math.Min(h, toastH - y);
                slabs.Add(new ShatterSlab { Y = y, H = h });
                y += h;
            }
            int[] order = Enumerable.Range(0, slabs.Count).ToArray();
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = rng.RangeI(0, i);
                (order[i], order[j]) = (order[j], order[i]);
            }

            for (int idx = 0; idx < slabs.Count; idx++)
            {
                int slot = Array.IndexOf(order, idx);
                float phase = slot / (float)Math.Max(1, slabs.Count - 1);
                bool extreme = rng.Next() < 0.18f;
                float mag = extreme ? rng.Range(45f, 80f) : rng.Range(10f, 40f);
                int dir = rng.Next() < 0.5f ? -1 : 1;
                float tx = MathF.Round(mag * dir);
                float jitter = rng.Range(-0.006f, 0.006f);
                // TearAt spans 0..0.82 to match the HTML generator's tear
                // distribution (EXIT_START + phase * (EXIT_END - EXIT_START
                // - 0.02) = 0.85 + phase * 0.09 in loop-space → 0..0.82 in
                // exit-space).
                float tearAt = phase * 0.82f + jitter;

                // VanishAt: each slab is visible for ~50% of exit-space after
                // its TearAt. HTML's slab-tear keyframe is visible for 11%
                // of the 4s loop (85.01-96%) with per-slab animation-delay,
                // which in exit-space translates to staggered windows per
                // slab. 50% of exit-space per slab keeps enough visible
                // density during mid-exit while ensuring every slab has its
                // own disappearance time - no simultaneous snap-to-nothing.
                float vanishAt = Math.Min(1.0f, tearAt + 0.50f + rng.Range(-0.05f, 0.05f));

                int chunkCount = rng.RangeI(2, 5);
                var chunks = new ShatterChunk[chunkCount];
                for (int k = 0; k < chunkCount; k++)
                {
                    float cw = rng.RangeI(8, 70);
                    float cx = rng.RangeI(-30, (int)(toastW - cw + 30));
                    // Chunk delay in exit-space: HTML uses rand(-0.02s, +0.02s)
                    // of a 4s loop = ±0.005 loop-space = ±0.045 exit-space.
                    float cDelay = rng.Range(-0.045f, 0.045f);
                    chunks[k] = new ShatterChunk { X = cx, W = cw, DelayOffset = cDelay };
                }

                var s = slabs[idx];
                s.Tx = tx;
                s.TearAt = tearAt;
                s.VanishAt = vanishAt;
                s.Chunks = chunks;
                slabs[idx] = s;
            }
            L.Slabs = slabs.ToArray();

            // Dust - per-speck time shift on the HTML dust-flick pattern.
            // HTML uses animationDelay of rand(-0.06s, +0.04s) on a 4s loop,
            // which in exit-space is approx (-0.06/4)/0.11 to (0.04/4)/0.11
            // = [-0.136, 0.091]. Widen slightly so speck flickers are even
            // more out of phase visually.
            L.Dust = new ShatterDust[50];
            for (int k = 0; k < 50; k++)
            {
                L.Dust[k] = new ShatterDust
                {
                    X = rng.RangeI(0, (int)toastW - 1),
                    Y = rng.RangeI(0, (int)toastH - 1),
                    Color = rng.RangeI(0, 3),
                    DelayOffset = rng.Range(-0.14f, 0.10f),
                };
            }

            // Dropout bars with per-pulse jitter
            L.Dropouts = new ShatterDropout[3];
            for (int k = 0; k < 3; k++)
            {
                float dh = rng.RangeI(5, 12);
                float dy = rng.RangeI(8, (int)(toastH - dh - 8));
                // HTML keyframes: pulse 0 = -6, pulse 1 = +8, pulse 2 = -4.
                // Jitter each by ±2px so bars vary but the left/right/left
                // pattern holds.
                var jitter = new float[]
                {
                    -6f + rng.Range(-2f, 2f),
                    +8f + rng.Range(-2f, 2f),
                    -4f + rng.Range(-2f, 2f),
                };
                L.Dropouts[k] = new ShatterDropout { Y = dy, H = dh, TxJitter = jitter };
            }

            // Vertical shears - per-vshear onset AND vanish. HTML vshear-cut
            // is visible 88-95.5% of 4s loop = 7.5% = 0.68 exit-space per
            // vshear. With onsets 0.27/0.50/0.73, vanishes at 0.95/1.0/1.0.
            L.Vshears = new ShatterVshear[3];
            float[] vOnsets = { 0.27f, 0.50f, 0.73f };
            for (int k = 0; k < 3; k++)
            {
                float vw = rng.RangeI(18, 44);
                float vx = rng.RangeI(10, (int)(toastW - vw - 10));
                float vmag = rng.Range(15f, 50f);
                int vdir = rng.Next() < 0.5f ? -1 : 1;
                float ty = MathF.Round(vmag * vdir);
                float onset = vOnsets[k] + rng.Range(-0.02f, 0.02f);
                float vanish = Math.Min(1.0f, onset + 0.68f);
                L.Vshears[k] = new ShatterVshear
                {
                    X = vx, W = vw, Ty = ty, OnsetAt = onset, VanishAt = vanish,
                };
            }

            // Survivor specks
            L.Specks = new ShatterSpeck[5];
            for (int k = 0; k < 5; k++)
            {
                L.Specks[k] = new ShatterSpeck
                {
                    X = rng.RangeI(10, (int)(toastW - 10)),
                    Y = rng.RangeI(10, (int)(toastH - 10)),
                    PhaseOffset = rng.Range(0f, 1f),
                };
            }

            return L;
        }
    }
}

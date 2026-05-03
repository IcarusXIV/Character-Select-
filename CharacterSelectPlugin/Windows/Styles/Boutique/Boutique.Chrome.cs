using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CharacterSelectPlugin.Windows.Styles;

public static partial class Boutique
{
    // ── Slip polygon (boutique chamfered silhouette) ────────────────────
    // Opposing chamfers: top-right + bottom-left. Sharp corners: top-left,
    // bottom-right. This is the canonical button/pill shape used by the
    // gold pill, cancel, mini-btn, page-btn, and chamfered text buttons.
    // Returned points are clockwise from the TL sharp corner.

    /// <summary>Fill the passed 6-element span with the slip polygon points for the given rect.</summary>
    public static void BuildSlipPolygon(Vector2 min, Vector2 max, float chamfer, Span<Vector2> pts)
    {
        float c = Math.Min(chamfer, Math.Min(max.X - min.X, max.Y - min.Y) * 0.5f);
        pts[0] = new Vector2(min.X,     min.Y);
        pts[1] = new Vector2(max.X - c, min.Y);
        pts[2] = new Vector2(max.X,     min.Y + c);
        pts[3] = new Vector2(max.X,     max.Y);
        pts[4] = new Vector2(min.X + c, max.Y);
        pts[5] = new Vector2(min.X,     max.Y - c);
    }

    /// <summary>Allocate a fresh 6-point slip polygon array.</summary>
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

    /// <summary>Stroke the slip silhouette at the given thickness.</summary>
    public static void StrokeSlip(ImDrawListPtr dl, Vector2 min, Vector2 max, float chamfer, uint colour, float thickness = 1f)
    {
        Span<Vector2> pts = stackalloc Vector2[6];
        BuildSlipPolygon(min, max, chamfer, pts);
        for (int i = 0; i < 6; i++)
            dl.PathLineTo(pts[i]);
        dl.PathStroke(colour, ImDrawFlags.Closed, thickness);
    }

    // ── Window corner brackets ──────────────────────────────────────────
    // Brand asymmetry: BL + BR only, never TL or TR. Drawn at a fixed inset
    // from the content rect corners with the gold token at 40% alpha.

    public static void DrawWindowBrackets(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale,
        float size = 14f, float inset = 6f, float thickness = 1f, float alpha = 0.40f)
    {
        uint c = U32(WithAlpha(Gold, alpha));
        float s = size * scale;
        float i = inset * scale;
        float t = thickness * scale;

        // Bottom-left
        var bl = new Vector2(min.X + i, max.Y - i);
        dl.AddLine(new Vector2(bl.X, bl.Y - s), bl, c, t);
        dl.AddLine(bl, new Vector2(bl.X + s, bl.Y), c, t);

        // Bottom-right
        var br = new Vector2(max.X - i, max.Y - i);
        dl.AddLine(new Vector2(br.X, br.Y - s), br, c, t);
        dl.AddLine(br, new Vector2(br.X - s, br.Y), c, t);
    }

    // ── Ribbon background (ribbon strip with gold hairlines + pip) ─────

    /// <summary>
    /// Paint the boutique ribbon strip: vertical gradient bg + gold hairline
    /// at top (wing-fade) and bottom (centre-fade). Caller is responsible for
    /// the pip and any tracked-caps content drawn on top.
    /// </summary>
    public static void DrawRibbonBackground(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
    {
        uint top = U32(RibbonTop);
        uint bot = U32(RibbonBot);
        dl.AddRectFilledMultiColor(min, max, top, top, bot, bot);

        // Top hairline: gold at edges, transparent in middle
        uint goldEdge = U32(WithAlpha(Gold, 0.50f));
        uint goldClear = U32(WithAlpha(Gold, 0f));
        float midX = (min.X + max.X) * 0.5f;
        dl.AddRectFilledMultiColor(
            new Vector2(min.X, min.Y),
            new Vector2(midX, min.Y + 1f * scale),
            goldEdge, goldClear, goldClear, goldEdge);
        dl.AddRectFilledMultiColor(
            new Vector2(midX, min.Y),
            new Vector2(max.X, min.Y + 1f * scale),
            goldClear, goldEdge, goldEdge, goldClear);

        // Bottom hairline: transparent at edges, gold at centre
        uint goldMid = U32(WithAlpha(Gold, 0.26f));
        dl.AddRectFilledMultiColor(
            new Vector2(min.X, max.Y - 1f * scale),
            new Vector2(midX, max.Y),
            goldClear, goldMid, goldMid, goldClear);
        dl.AddRectFilledMultiColor(
            new Vector2(midX, max.Y - 1f * scale),
            new Vector2(max.X, max.Y),
            goldMid, goldClear, goldClear, goldMid);
    }

    /// <summary>Small gold pip with soft glow, used as the leftmost ribbon decoration.</summary>
    public static void DrawGoldPip(ImDrawListPtr dl, Vector2 centre, float scale)
    {
        float r = 3f * scale;
        // Glow halo
        for (int i = 3; i > 0; i--)
        {
            float rr = r + i * 1.5f * scale;
            dl.AddCircleFilled(centre, rr, U32(WithAlpha(Gold, 0.10f / i)), 16);
        }
        dl.AddCircleFilled(centre, r, U32(Gold), 16);
    }

    /// <summary>
    /// Small coloured square pip with a soft outer halo. Used as the active-name
    /// marker on cards and form headers. Renders an outer square at 1.6× halfSize
    /// in the colour at 35% alpha (the halo) plus an inner square at full alpha,
    /// matching the canonical pip silhouette.
    /// </summary>
    public static void DrawSquarePip(ImDrawListPtr dl, Vector2 centre, float halfSize, Vector4 colour)
    {
        float r = halfSize;
        dl.AddRectFilled(
            centre - new Vector2(r * 1.6f, r * 1.6f),
            centre + new Vector2(r * 1.6f, r * 1.6f),
            U32(WithAlpha(colour, 0.35f)));
        dl.AddRectFilled(
            centre - new Vector2(r, r),
            centre + new Vector2(r, r),
            U32(colour));
    }

    // ── Hairline rules ──────────────────────────────────────────────────

    /// <summary>Horizontal gold-fade rule (one-pixel high, fades from gold-deep to clear).</summary>
    public static void DrawGoldFadeRule(ImDrawListPtr dl, Vector2 start, float width, float scale,
        float startAlpha = 0.65f, float midAlpha = 0.15f)
    {
        uint goldStart = U32(WithAlpha(GoldDeep, startAlpha));
        uint goldFade  = U32(WithAlpha(Gold, midAlpha));
        uint goldClear = U32(WithAlpha(Gold, 0f));
        float h = 1f * scale;
        dl.AddRectFilledMultiColor(
            start,
            new Vector2(start.X + width * 0.6f, start.Y + h),
            goldStart, goldFade, goldFade, goldStart);
        dl.AddRectFilledMultiColor(
            new Vector2(start.X + width * 0.6f, start.Y),
            new Vector2(start.X + width, start.Y + h),
            goldFade, goldClear, goldClear, goldFade);
    }

    /// <summary>One-pixel solid border line (use for separator rules between sections).</summary>
    public static void DrawHairline(ImDrawListPtr dl, Vector2 start, float width, float scale, Vector4 colour)
    {
        dl.AddLine(start, new Vector2(start.X + width, start.Y), U32(colour), 1f * scale);
    }

    /// <summary>Dashed amber top border (used for restricted-div). Length pattern 2px on / 2px off by default.</summary>
    public static void DrawDashedHairline(ImDrawListPtr dl, Vector2 start, float width, float scale, Vector4 colour,
        float dashOn = 2f, float dashOff = 2f)
    {
        float x = start.X;
        float endX = start.X + width;
        float on = dashOn * scale;
        float off = dashOff * scale;
        uint c = U32(colour);
        float thick = 1f * scale;
        while (x < endX)
        {
            float segEnd = MathF.Min(x + on, endX);
            dl.AddRectFilled(new Vector2(x, start.Y), new Vector2(segEnd, start.Y + thick), c);
            x = segEnd + off;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CharacterSelectPlugin.Windows.Styles;

public static partial class Boutique
{
    // One-shot sheen tracker: returns 0..1 progress while in flight, -1 otherwise
    private static readonly Dictionary<string, DateTime> _sheenStarts = new();

    /// <summary>
    /// Returns the sheen progress in [0,1] for this id. -1 when no sheen
    /// should be drawn (idle, or sweep already completed). Pass `hovered` and
    /// the sheen restarts when hover begins, runs for `duration` seconds,
    /// then idles until unhover.
    /// </summary>
    public static float SheenProgress(string id, bool hovered, float duration = 0.65f)
    {
        if (!hovered)
        {
            _sheenStarts.Remove(id);
            return -1f;
        }
        if (!_sheenStarts.ContainsKey(id))
            _sheenStarts[id] = DateTime.UtcNow;
        float elapsed = (float)(DateTime.UtcNow - _sheenStarts[id]).TotalSeconds;
        if (elapsed >= duration) return -1f;
        return elapsed / duration;
    }

    // ── Easing curves ───────────────────────────────────────────────────

    /// <summary>Quadratic ease-out (1 - (1-t)^2).</summary>
    public static float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);
    /// <summary>Cubic ease-out (1 - (1-t)^3).</summary>
    public static float EaseOutCubic(float t)
    {
        float u = 1f - t;
        return 1f - u * u * u;
    }
    /// <summary>Quadratic ease-in (t^2).</summary>
    public static float EaseInQuad(float t) => t * t;
    /// <summary>Smoothstep (3t^2 - 2t^3).</summary>
    public static float Smoothstep(float t) => t * t * (3f - 2f * t);

    // ── Heart pulse ─────────────────────────────────────────────────────
    /// <summary>Heartbeat scale curve (1.0 idle, 1.18 peak twice per ~1.8s cycle).</summary>
    public static float HeartBeatScale(double time)
    {
        float t = (float)((time / 1.8) % 1.0);
        return t switch
        {
            < 0.15f => 1.0f + 0.18f * MathF.Sin(t / 0.15f * MathF.PI),
            < 0.30f => 1.0f,
            < 0.45f => 1.0f + 0.10f * MathF.Sin((t - 0.30f) / 0.15f * MathF.PI),
            _       => 1.0f,
        };
    }

    // ── Hover lift ──────────────────────────────────────────────────────
    /// <summary>Eased upward lift in pixels (0..maxLift) for hovered buttons. t is 0..1 hover progress.</summary>
    public static float EasedLift(float t, float maxLift) => maxLift * EaseOutCubic(Math.Clamp(t, 0f, 1f));
}

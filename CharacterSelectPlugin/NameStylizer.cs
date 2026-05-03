using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;

namespace CharacterSelectPlugin
{
    /// <summary>
    /// Renders a single name with the boutique "glitch" FX cascade: SD Glitch font as base,
    /// periodic chromatic burst (cyan + magenta ghost copies, letter scramble, chunk scatter).
    ///
    /// All draw operations go through ImDrawListPtr primitives, no per-pixel ops, no shaders.
    /// Translates 1:1 from in-window ImGui surfaces (character card, RP profile) to anywhere
    /// else CS+ owns the rendering surface.
    /// </summary>
    public static class NameStylizer
    {
        // Burst clock: each cycle is BurstPeriod seconds long, with a glitch window
        // of BurstWindow seconds at the start of the cycle. Outside the window the
        // name renders cleanly in the glitch font.
        private const float BurstPeriod = 5.5f;
        private const float BurstWindow = 0.55f;

        // Glyph pool the letter-scramble draws from. Mix of caps, digits, and a
        // few punctuation marks so it reads as "system corruption" not gibberish.
        private static readonly char[] ScrambleGlyphs =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%&*?<>/\\=+".ToCharArray();

        // Name FX palette: nameplate colour + white + black. NOT cyan/magenta -
        // those are reserved for chassis-level ring/slab fringes.
        private static readonly Vector3 White = new(1f, 1f, 1f);
        private static readonly Vector3 Black = new(0f, 0f, 0f);

        /// <summary>
        /// Draws a stylized name. When useGlitch is false (or font unavailable) falls back
        /// to a plain AddText so callers can use this unconditionally.
        /// </summary>
        /// <param name="dl">ImGui draw list to draw into.</param>
        /// <param name="pos">Top-left screen position of the text.</param>
        /// <param name="text">The name to render.</param>
        /// <param name="baseColor">Per-character nameplate colour, used in the colour mix.</param>
        /// <param name="alpha">Master alpha multiplier (0..1).</param>
        /// <param name="useGlitch">When true, FX cascade. When false, simple AddText.</param>
        /// <param name="glitchFont">Font handle for the glitch face. Can be null.</param>
        /// <param name="seedHash">Per-character seed so different cards don't burst in lockstep.</param>
        public static void Draw(
            ImDrawListPtr dl,
            Vector2 pos,
            string text,
            Vector3 baseColor,
            float alpha,
            bool useGlitch,
            IFontHandle? glitchFont,
            int seedHash)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (!useGlitch || glitchFont == null || !glitchFont.Available)
            {
                // Plain path - use whatever font is currently pushed
                uint c = ColU32(baseColor, alpha);
                dl.AddText(pos, c, text);
                return;
            }

            // Push the glitch font for the duration of this draw
            glitchFont.Push();
            try
            {
                DrawWithFx(dl, pos, text, baseColor, alpha, seedHash);
            }
            finally
            {
                glitchFont.Pop();
            }
        }

        private static void DrawWithFx(
            ImDrawListPtr dl,
            Vector2 pos,
            string text,
            Vector3 baseColor,
            float alpha,
            int seedHash)
        {
            float t = (float)ImGui.GetTime();
            // Per-character phase offset so cards don't all burst together
            float phaseOffset = (seedHash & 0xFF) / 255f * BurstPeriod;
            float cyclePos = ((t + phaseOffset) % BurstPeriod);

            bool inBurst = cyclePos < BurstWindow;
            float burstT = inBurst ? cyclePos / BurstWindow : 0f;
            // Envelope: sin(pi * x) gives a smooth ramp-in / ramp-out pulse.
            float burstE = inBurst ? MathF.Sin(burstT * MathF.PI) : 0f;

            ImFontPtr font = ImGui.GetFont();
            float fontSize = ImGui.GetFontSize();
            var size = ImGui.CalcTextSize(text);

            // Aggressive letter scramble during burst - frame-stable seed so glyphs
            // don't strobe at frame rate, but fast enough to feel "live"
            string display = text;
            if (burstE > 0.05f)
            {
                int frameSeed = (int)(t * 14) ^ seedHash;
                display = ScrambleString(text, frameSeed, burstE * 0.55f);
            }

            // Chunk scatter behind the text - small black bars + nameplate-colour
            // sliver bars across the text strip. Stays inside text bounds.
            if (burstE > 0.15f)
            {
                int chunkSeed = (int)(t * 12) ^ seedHash;
                DrawChunkScatter(dl, pos, size, burstE, chunkSeed, baseColor);
            }

            // Ghost copies: drawn in nameplate colour AND white at offsets, with
            // a small vertical jitter during burst for the "torn" feel. NO cyan/
            // magenta - that's the toast/chassis FX, not the name FX.
            float horizOff = 1.5f + burstE * 3.5f;
            float vertJit  = burstE * (((seedHash & 0x7) - 3.5f) * 0.5f);

            // Left ghost: tinted nameplate-colour, slightly transparent so the
            // core reads on top.
            uint leftGhostU = ColU32(baseColor, alpha * (0.45f + burstE * 0.30f));
            dl.AddText(font, fontSize, pos + new Vector2(-horizOff, vertJit), leftGhostU, display);

            // Right ghost: white, lower alpha so it reads as a hot edge rather
            // than a duplicate copy. Pulses harder during the burst peak.
            uint rightGhostU = ColU32(White, alpha * (0.20f + burstE * 0.55f));
            dl.AddText(font, fontSize, pos + new Vector2(horizOff, -vertJit), rightGhostU, display);

            // Black "shadow" pass directly behind the core for definition. Always
            // on (mild outside burst, stronger during) - keeps the text legible
            // against bright backgrounds without drawing a chromatic fringe.
            uint shadowU = ColU32(Black, alpha * (0.55f + burstE * 0.25f));
            dl.AddText(font, fontSize, pos + new Vector2(1f, 1f), shadowU, display);

            // Core letters. Outside burst, pure nameplate colour. During burst,
            // flash-mix toward white (hot peak) - never cyan/magenta.
            Vector3 coreColor = Vector3.Lerp(baseColor, White, burstE * 0.55f);
            uint coreU = ColU32(coreColor, alpha);
            dl.AddText(font, fontSize, pos, coreU, display);
        }

        private static string ScrambleString(string source, int seed, float intensity)
        {
            if (intensity <= 0f) return source;
            var rng = new Random(seed);
            var chars = source.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] == ' ') continue;
                if (rng.NextDouble() < intensity)
                {
                    chars[i] = ScrambleGlyphs[rng.Next(ScrambleGlyphs.Length)];
                }
            }
            return new string(chars);
        }

        private static void DrawChunkScatter(
            ImDrawListPtr dl,
            Vector2 pos,
            Vector2 size,
            float burstE,
            int seed,
            Vector3 baseColor)
        {
            var rng = new Random(seed);
            int chunks = 2 + (int)(burstE * 3f);   // 2-5 chunks at peak
            for (int i = 0; i < chunks; i++)
            {
                // Each chunk is a thin horizontal slab clamped to the text bounds.
                float yFrac   = (float)rng.NextDouble();
                float hFrac   = 0.08f + (float)rng.NextDouble() * 0.14f;
                float xJitter = ((float)rng.NextDouble() - 0.5f) * size.X * 0.20f * burstE;
                float wFrac   = 0.30f + (float)rng.NextDouble() * 0.50f;

                float startX = pos.X + size.X * (1f - wFrac) * (float)rng.NextDouble() + xJitter;
                startX = MathF.Max(pos.X, startX);
                float endX = MathF.Min(pos.X + size.X, startX + size.X * wFrac);

                var chunkMin = new Vector2(startX, pos.Y + size.Y * yFrac);
                var chunkMax = new Vector2(endX,   chunkMin.Y + size.Y * hFrac);

                // Alternate black slab + nameplate-colour sliver. No cyan/magenta.
                bool blackSlab = (i & 1) == 0;
                Vector3 tint = blackSlab ? Black : baseColor;
                float a = (blackSlab ? 0.55f : 0.40f) * burstE;
                dl.AddRectFilled(chunkMin, chunkMax, ColU32(tint, a));
            }
        }

        private static uint ColU32(Vector3 c, float a)
        {
            return ImGui.ColorConvertFloat4ToU32(new Vector4(c.X, c.Y, c.Z, Math.Clamp(a, 0f, 1f)));
        }

        /// <summary>Hash a string to a deterministic int for per-character phase offsets.</summary>
        public static int Hash(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            unchecked
            {
                int h = 23;
                foreach (var ch in s) h = h * 31 + ch;
                return h;
            }
        }

        /// <summary>Render-time string transform for the glitch effect: uppercase only.
        /// SD Glitch's lowercase reads as a different style; forcing caps keeps the
        /// look consistent without forcing the user to type their character name in caps.</summary>
        public static string Render(string s)
        {
            return string.IsNullOrEmpty(s) ? string.Empty : s.ToUpperInvariant();
        }
    }
}

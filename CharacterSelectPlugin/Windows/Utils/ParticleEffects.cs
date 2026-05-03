using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace CharacterSelectPlugin.Effects
{
    public class Particle
    {
        public Vector2 Position { get; set; }
        public Vector2 Velocity { get; set; }
        public Vector4 Color { get; set; }
        public float Life { get; set; }
        public float MaxLife { get; set; }
        public float Size { get; set; }
        // Optional FontAwesome glyph; when set the particle renders as the
        // glyph (snowflake / ghost / spider / heart) instead of a circle.
        public string? Glyph { get; set; }
        public float Rotation { get; set; }
        public float SpinSpeed { get; set; }

        public bool IsAlive => Life > 0;

        public void Update(float deltaTime)
        {
            Life -= deltaTime;
            Position += Velocity * deltaTime;

            // Fade out over time
            float alpha = Life / MaxLife;
            Color = new Vector4(Color.X, Color.Y, Color.Z, alpha);

            // Shrink over time
            Size *= 0.99f;

            // Spin glyph particles for a bit of motion / character
            Rotation += SpinSpeed * deltaTime;
        }
    }

    public class FavoriteSparkEffect
    {
        private List<Particle> particles = new();
        private float duration = 0.8f;
        private float elapsed = 0;
        private bool isActive = false;
        private Vector2 origin;

        public bool IsActive => isActive && elapsed < duration;

        public void Trigger(Vector2 position, bool isFavorited, Configuration? config = null)
        {
            particles.Clear();
            origin = position;
            elapsed = 0;
            isActive = true;

            var random = new Random();
            int particleCount = isFavorited ? 12 : 8; // Favouriting

            Vector4 baseColor;
            string? extraGlyph = null;

            // Check for seasonal themes - pick a palette + glyph per theme.
            if (config != null && SeasonalThemeManager.IsSeasonalThemeEnabled(config))
            {
                var effectiveTheme = SeasonalThemeManager.GetEffectiveTheme(config);
                switch (effectiveTheme)
                {
                    case SeasonalTheme.Winter:
                    case SeasonalTheme.Christmas:
                        baseColor = isFavorited
                            ? new Vector4(1f, 1f, 1f, 1f)
                            : new Vector4(0.7f, 0.7f, 0.8f, 1f);
                        extraGlyph = ""; // FontAwesome snowflake
                        break;
                    case SeasonalTheme.Halloween:
                        baseColor = isFavorited
                            ? new Vector4(1f, 0.55f, 0.10f, 1f) // Pumpkin orange
                            : new Vector4(0.7f, 0.7f, 0.7f, 1f);
                        // Mix ghost () and spider () glyphs across particles
                        // for variety; assigned per-particle in the loop below.
                        extraGlyph = "";
                        break;
                    case SeasonalTheme.Valentines:
                        baseColor = isFavorited
                            ? new Vector4(1f, 0.35f, 0.55f, 1f) // Vivid pink
                            : new Vector4(0.85f, 0.55f, 0.65f, 1f);
                        extraGlyph = ""; // FontAwesome solid heart
                        break;
                    default:
                        baseColor = isFavorited
                            ? new Vector4(1f, 0.8f, 0.2f, 1f)
                            : new Vector4(0.6f, 0.6f, 0.6f, 1f);
                        break;
                }
            }
            else
            {
                baseColor = isFavorited
                    ? new Vector4(1f, 0.8f, 0.2f, 1f) // Gold for favourited (default)
                    : new Vector4(0.6f, 0.6f, 0.6f, 1f); // Grey for unfavourited
            }

            for (int i = 0; i < particleCount; i++)
            {
                float angle = (float)(random.NextDouble() * Math.PI * 2);
                float speed = 50f + (float)(random.NextDouble() * 100f);
                float life = 0.4f + (float)(random.NextDouble() * 0.4f);

                var particle = new Particle
                {
                    Position = position + new Vector2(
                        (float)(random.NextDouble() * 10 - 5),
                        (float)(random.NextDouble() * 10 - 5)
                    ),
                    Velocity = new Vector2(
                        (float)Math.Cos(angle) * speed,
                        (float)Math.Sin(angle) * speed
                    ),
                    Color = baseColor + new Vector4(
                        (float)(random.NextDouble() * 0.2f - 0.1f),
                        (float)(random.NextDouble() * 0.2f - 0.1f),
                        (float)(random.NextDouble() * 0.2f - 0.1f),
                        0
                    ),
                    Life = life,
                    MaxLife = life,
                    Size = 2f + (float)(random.NextDouble() * 2f),
                    Glyph = null,
                    Rotation = 0f,
                    SpinSpeed = 0f
                };

                particles.Add(particle);
            }

            // Themed decoration: spawn a smaller second wave on top of the
            // firework burst so the original circle-spark feel is preserved
            // and the theme reads as a layer rather than replacing the burst.
            // Halloween renders smoke puffs (Glyph "smoke" sentinel); other
            // themes render the snowflake / heart glyph.
            if (config != null && SeasonalThemeManager.IsSeasonalThemeEnabled(config))
            {
                bool halloween = SeasonalThemeManager.GetEffectiveTheme(config) == SeasonalTheme.Halloween;
                int extras = isFavorited ? 6 : 4;
                for (int j = 0; j < extras; j++)
                {
                    float angle = (float)(random.NextDouble() * Math.PI * 2);
                    float speed = 30f + (float)(random.NextDouble() * 70f);
                    float life = 0.55f + (float)(random.NextDouble() * 0.5f);
                    var extra = new Particle
                    {
                        Position = position + new Vector2(
                            (float)(random.NextDouble() * 10 - 5),
                            (float)(random.NextDouble() * 10 - 5)
                        ),
                        Velocity = new Vector2(
                            (float)Math.Cos(angle) * speed,
                            (float)Math.Sin(angle) * speed
                        ),
                        Color = baseColor,
                        Life = life,
                        MaxLife = life,
                        Size = 2.5f + (float)(random.NextDouble() * 2f),
                        Rotation = (float)(random.NextDouble() * Math.PI * 2),
                        SpinSpeed = (float)((random.NextDouble() - 0.5) * 4.0),
                        Glyph = halloween ? "smoke" : extraGlyph
                    };
                    particles.Add(extra);
                }
            }
        }

        public void Update(float deltaTime)
        {
            if (!isActive) return;

            elapsed += deltaTime;

            for (int i = particles.Count - 1; i >= 0; i--)
            {
                particles[i].Update(deltaTime);
                if (!particles[i].IsAlive)
                {
                    particles.RemoveAt(i);
                }
            }

            if (elapsed >= duration)
            {
                isActive = false;
                particles.Clear();
            }
        }

        public void Draw() => Draw(ImGui.GetWindowDrawList());

        public void Draw(ImDrawListPtr drawList)
        {
            if (!IsActive) return;

            var iconFont = UiBuilder.IconFont;
            foreach (var particle in particles)
            {
                if (!particle.IsAlive) continue;

                uint color = ImGui.GetColorU32(particle.Color);

                if (particle.Glyph == "smoke")
                {
                    // Halloween: wispy smoke puff - stacked translucent grey
                    // circles fading outward from the particle centre.
                    float baseR = particle.Size * 2.6f;
                    var smokeRgb = new Vector3(0.22f, 0.20f, 0.18f);
                    for (int s = 0; s < 3; s++)
                    {
                        float layerR = baseR * (1f + s * 0.45f);
                        float layerA = particle.Color.W * (0.45f - s * 0.13f);
                        if (layerA <= 0f) continue;
                        drawList.AddCircleFilled(particle.Position, layerR,
                            ImGui.GetColorU32(new Vector4(smokeRgb.X, smokeRgb.Y, smokeRgb.Z, layerA)));
                    }
                }
                else if (!string.IsNullOrEmpty(particle.Glyph))
                {
                    // Glyph particles (snowflake / heart) layered on top of
                    // the circle-burst fireworks. Size scales with
                    // particle.Size; glow halo for bright glyphs.
                    float glyphPx = MathF.Max(10f, particle.Size * 6f);
                    var glyphSz = ImGui.CalcTextSize(particle.Glyph);
                    float scaleR = glyphPx / iconFont.FontSize;
                    var glyphPos = new Vector2(
                        particle.Position.X - glyphSz.X * scaleR * 0.5f,
                        particle.Position.Y - glyphSz.Y * scaleR * 0.5f);

                    if (particle.Color.W > 0.4f)
                    {
                        var glowColor = new Vector4(particle.Color.X, particle.Color.Y, particle.Color.Z, particle.Color.W * 0.30f);
                        drawList.AddText(iconFont, glyphPx * 1.20f,
                            new Vector2(glyphPos.X - 1f, glyphPos.Y - 1f),
                            ImGui.GetColorU32(glowColor), particle.Glyph);
                    }
                    drawList.AddText(iconFont, glyphPx, glyphPos, color, particle.Glyph);
                }
                else
                {
                    drawList.AddCircleFilled(
                        particle.Position,
                        particle.Size,
                        color,
                        6
                    );

                    if (particle.Color.X > 0.8f)
                    {
                        var glowColor = new Vector4(particle.Color.X, particle.Color.Y, particle.Color.Z, particle.Color.W * 0.3f);
                        drawList.AddCircleFilled(
                            particle.Position,
                            particle.Size * 1.5f,
                            ImGui.GetColorU32(glowColor),
                            8
                        );
                    }
                }
            }
        }
    }
    public class LikeSparkEffect
    {
        private List<Particle> particles = new();
        private float duration = 0.8f;
        private float elapsed = 0;
        private bool isActive = false;
        private Vector2 origin;

        public bool IsActive => isActive && elapsed < duration;

        public void Trigger(Vector2 position, bool isFavorited)
        {
            particles.Clear();
            origin = position;
            elapsed = 0;
            isActive = true;

            var random = new Random();
            int particleCount = isFavorited ? 12 : 8; // Particles when favouriting

            Vector4 baseColor = isFavorited
                ? new Vector4(1f, 0.2f, 0.4f, 1f) // Red for liking
                : new Vector4(0.6f, 0.6f, 0.6f, 1f); // Gray for unfavourited

            for (int i = 0; i < particleCount; i++)
            {
                float angle = (float)(random.NextDouble() * Math.PI * 2);
                float speed = 50f + (float)(random.NextDouble() * 100f);
                float life = 0.4f + (float)(random.NextDouble() * 0.4f);

                var particle = new Particle
                {
                    Position = position + new Vector2(
                        (float)(random.NextDouble() * 10 - 5),
                        (float)(random.NextDouble() * 10 - 5)
                    ),
                    Velocity = new Vector2(
                        (float)Math.Cos(angle) * speed,
                        (float)Math.Sin(angle) * speed
                    ),
                    Color = baseColor + new Vector4(
                        (float)(random.NextDouble() * 0.2f - 0.1f),
                        (float)(random.NextDouble() * 0.2f - 0.1f),
                        (float)(random.NextDouble() * 0.2f - 0.1f),
                        0
                    ),
                    Life = life,
                    MaxLife = life,
                    Size = 2f + (float)(random.NextDouble() * 2f)
                };

                particles.Add(particle);
            }
        }

        public void Update(float deltaTime)
        {
            if (!isActive) return;

            elapsed += deltaTime;

            for (int i = particles.Count - 1; i >= 0; i--)
            {
                particles[i].Update(deltaTime);
                if (!particles[i].IsAlive)
                {
                    particles.RemoveAt(i);
                }
            }

            if (elapsed >= duration)
            {
                isActive = false;
                particles.Clear();
            }
        }

        public void Draw()
        {
            if (!IsActive) return;

            var drawList = ImGui.GetWindowDrawList();

            foreach (var particle in particles)
            {
                if (particle.IsAlive)
                {
                    uint color = ImGui.GetColorU32(particle.Color);

                    drawList.AddCircleFilled(
                        particle.Position,
                        particle.Size,
                        color,
                        6
                    );

                    if (particle.Color.X > 0.7f && particle.Color.X > particle.Color.Y && particle.Color.X > particle.Color.Z)
                    {
                        var glowColor = new Vector4(particle.Color.X, particle.Color.Y, particle.Color.Z, particle.Color.W * 0.3f);
                        drawList.AddCircleFilled(
                            particle.Position,
                            particle.Size * 1.5f,
                            ImGui.GetColorU32(glowColor),
                            8
                        );
                    }
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

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
            
            // Check for seasonal themes
            if (config != null && SeasonalThemeManager.IsSeasonalThemeEnabled(config))
            {
                var effectiveTheme = SeasonalThemeManager.GetEffectiveTheme(config);
                if (effectiveTheme == SeasonalTheme.Winter || effectiveTheme == SeasonalTheme.Christmas)
                {
                    baseColor = isFavorited
                        ? new Vector4(1f, 1f, 1f, 1f) // Pure white for winter/Christmas favourites
                        : new Vector4(0.7f, 0.7f, 0.8f, 1f); // Light grey for unfavourited
                }
                else
                {
                    baseColor = isFavorited
                        ? new Vector4(1f, 0.8f, 0.2f, 1f) // Gold for favourited (default/other themes)
                        : new Vector4(0.6f, 0.6f, 0.6f, 1f); // Grey for unfavourited
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

                    // Draw particles
                    drawList.AddCircleFilled(
                        particle.Position,
                        particle.Size,
                        color,
                        6
                    );

                    // Subtle glow effect
                    if (particle.Color.X > 0.8f) // Gold particles
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

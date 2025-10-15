using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CharacterSelectPlugin.Effects
{
    public class FogParticle
    {
        public Vector2 Position { get; set; }
        public Vector2 Velocity { get; set; }
        public Vector4 Color { get; set; }
        public float Life { get; set; }
        public float MaxLife { get; set; }
        public float Size { get; set; }
        public float NoiseOffset { get; set; }
        public float FlowDirection { get; set; }

        public bool IsAlive => Life > 0;

        public void Update(float deltaTime)
        {
            Life -= deltaTime;
            
            // Add sinusoidal movement for more natural fog flow
            float noiseX = MathF.Sin(Life * 2.0f + NoiseOffset) * 10.0f;
            float noiseY = MathF.Cos(Life * 1.5f + NoiseOffset * 0.7f) * 5.0f;
            
            var noiseVelocity = new Vector2(noiseX, noiseY) * deltaTime;
            Position += (Velocity + noiseVelocity) * deltaTime;

            // Fade out over time with more sophisticated alpha curve
            float lifeFraction = Life / MaxLife;
            float alpha = lifeFraction; // Linear fade for better visibility
            Color = new Vector4(Color.X, Color.Y, Color.Z, alpha * 1.0f); // Maximum visibility fog

            // Very slow size change to keep fog consistent
            Size *= 0.9995f;
        }
    }

    public class HalloweenFogEffect
    {
        private List<FogParticle> fogParticles = new();
        private Random random = new();
        private float spawnTimer = 0;
        private float spawnInterval = 0.02f; // Spawn new fog particles more frequently for testing
        private Vector2 effectArea = Vector2.Zero;
        private bool isActive = false;

        public void SetEffectArea(Vector2 area)
        {
            effectArea = area;
            isActive = area.X > 0 && area.Y > 0;
        }

        public void Update(float deltaTime)
        {
            if (!isActive) return;

            spawnTimer += deltaTime;

            // Spawn new fog particles
            if (spawnTimer >= spawnInterval)
            {
                SpawnFogParticles();
                spawnTimer = 0;
            }

            // Update existing particles
            for (int i = fogParticles.Count - 1; i >= 0; i--)
            {
                fogParticles[i].Update(deltaTime);
                
                // Remove dead particles or those that have moved too far
                if (!fogParticles[i].IsAlive || 
                    fogParticles[i].Position.X < -100 || fogParticles[i].Position.X > effectArea.X + 100 ||
                    fogParticles[i].Position.Y < -100 || fogParticles[i].Position.Y > effectArea.Y + 100)
                {
                    fogParticles.RemoveAt(i);
                }
            }

            // Limit particle count for performance
            if (fogParticles.Count > 50)
            {
                fogParticles.RemoveRange(0, fogParticles.Count - 50);
            }
        }

        private void SpawnFogParticles()
        {
            // Spawn 5-10 fog particles per interval for testing
            int count = random.Next(5, 11);
            
            for (int i = 0; i < count; i++)
            {
                // Start particles from random edges (left, right, bottom)
                Vector2 startPos;
                Vector2 velocity;
                
                int spawnSide = random.Next(3); // 0=left, 1=right, 2=bottom
                
                switch (spawnSide)
                {
                    case 0: // Left side
                        startPos = new Vector2(-20, random.NextSingle() * effectArea.Y);
                        velocity = new Vector2(15 + random.NextSingle() * 10, (random.NextSingle() - 0.5f) * 5);
                        break;
                    case 1: // Right side
                        startPos = new Vector2(effectArea.X + 20, random.NextSingle() * effectArea.Y);
                        velocity = new Vector2(-15 - random.NextSingle() * 10, (random.NextSingle() - 0.5f) * 5);
                        break;
                    default: // Bottom
                        startPos = new Vector2(random.NextSingle() * effectArea.X, effectArea.Y + 20);
                        velocity = new Vector2((random.NextSingle() - 0.5f) * 20, -10 - random.NextSingle() * 10);
                        break;
                }

                var themeColors = SeasonalThemeManager.GetThemeColors(SeasonalTheme.Halloween); // Use Halloween colors directly
                
                var fogParticle = new FogParticle
                {
                    Position = startPos,
                    Velocity = velocity,
                    Color = new Vector4(
                        0.8f, // Very bright for testing
                        0.2f, 
                        0.8f,
                        0.9f + random.NextSingle() * 0.1f // Very visible alpha
                    ),
                    Life = 15.0f + random.NextSingle() * 10.0f, // Long-lived fog
                    MaxLife = 15.0f + random.NextSingle() * 10.0f,
                    Size = 30.0f + random.NextSingle() * 40.0f, // Large, soft fog particles
                    NoiseOffset = random.NextSingle() * MathF.PI * 2,
                    FlowDirection = random.NextSingle() * MathF.PI * 2
                };

                fogParticles.Add(fogParticle);
            }
        }

        public void Draw()
        {
            if (!isActive || fogParticles.Count == 0) 
            {
                // DEBUG: Draw a test message if no particles
                if (isActive)
                {
                    var debugDrawList = ImGui.GetWindowDrawList();
                    Vector2 debugPos = ImGui.GetWindowPos() + new Vector2(200, 50);
                    debugDrawList.AddCircleFilled(debugPos, 10f, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 0.0f, 1.0f)), 8);
                }
                return;
            }

            var drawList = ImGui.GetWindowDrawList();

            foreach (var particle in fogParticles)
            {
                if (particle.IsAlive && particle.Color.W > 0.01f)
                {
                    uint fogColor = ImGui.GetColorU32(particle.Color);

                    // Draw multiple overlapping circles for soft fog effect
                    float baseSize = particle.Size;
                    
                    // Outer soft layer
                    var outerColor = new Vector4(particle.Color.X, particle.Color.Y, particle.Color.Z, particle.Color.W * 0.3f);
                    drawList.AddCircleFilled(
                        particle.Position,
                        baseSize * 1.5f,
                        ImGui.GetColorU32(outerColor),
                        16
                    );

                    // Middle layer
                    var middleColor = new Vector4(particle.Color.X, particle.Color.Y, particle.Color.Z, particle.Color.W * 0.6f);
                    drawList.AddCircleFilled(
                        particle.Position,
                        baseSize,
                        ImGui.GetColorU32(middleColor),
                        12
                    );

                    // Inner core
                    var coreColor = new Vector4(particle.Color.X, particle.Color.Y, particle.Color.Z, particle.Color.W * 0.8f);
                    drawList.AddCircleFilled(
                        particle.Position,
                        baseSize * 0.5f,
                        ImGui.GetColorU32(coreColor),
                        8
                    );
                }
            }
        }

        public void Reset()
        {
            fogParticles.Clear();
            spawnTimer = 0;
        }
    }

    public class SpiderWebEffect
    {
        private List<Vector2[]> webStrands = new();
        private Random random = new();
        private bool isInitialized = false;
        private Vector2[] corners = new Vector2[4];
        private float webSize = 30f; // Size of web in each corner

        public void Initialize(Vector2[] cardCorners, float size = 30f)
        {
            if (cardCorners.Length >= 4)
            {
                corners = new Vector2[4];
                Array.Copy(cardCorners, corners, 4);
                webSize = size;
                GenerateWebs();
                isInitialized = true;
            }
        }

        public void Initialize(Vector2 corner, float size)
        {
            // Legacy method for backward compatibility
            corners = new Vector2[] { corner, corner, corner, corner };
            webSize = size;
            GenerateWeb();
            isInitialized = true;
        }

        private void GenerateWebs()
        {
            webStrands.Clear();

            // Generate webs for all four corners
            for (int cornerIndex = 0; cornerIndex < 4; cornerIndex++)
            {
                GenerateWebForCorner(corners[cornerIndex], cornerIndex);
            }
        }

        private void GenerateWeb()
        {
            webStrands.Clear();
            // Legacy method - generate for single corner
            GenerateWebForCorner(corners[0], 0);
        }

        private void GenerateWebForCorner(Vector2 cornerPos, int cornerIndex)
        {
            // Create a simple web pattern in the corner
            int strandCount = 3 + random.Next(2); // 3-4 strands per corner (keep it subtle)
            
            for (int i = 0; i < strandCount; i++)
            {
                var strand = new List<Vector2>();
                
                // Determine web direction based on corner index
                Vector2 start, end;
                Vector2 dir1, dir2;
                
                switch (cornerIndex)
                {
                    case 0: // Top-left
                        dir1 = new Vector2(webSize, 0);
                        dir2 = new Vector2(0, webSize);
                        break;
                    case 1: // Top-right
                        dir1 = new Vector2(-webSize, 0);
                        dir2 = new Vector2(0, webSize);
                        break;
                    case 2: // Bottom-left
                        dir1 = new Vector2(webSize, 0);
                        dir2 = new Vector2(0, -webSize);
                        break;
                    default: // Bottom-right
                        dir1 = new Vector2(-webSize, 0);
                        dir2 = new Vector2(0, -webSize);
                        break;
                }

                float t = (float)(i + 1) / (strandCount + 1);
                start = cornerPos + dir1 * t;
                end = cornerPos + dir2 * t;

                // Add some curve to the strand
                Vector2 mid = (start + end) * 0.5f;
                mid += new Vector2((random.NextSingle() - 0.5f) * 5, (random.NextSingle() - 0.5f) * 5);

                strand.Add(start);
                strand.Add(mid);
                strand.Add(end);
                
                webStrands.Add(strand.ToArray());
            }
        }

        public void Draw(Configuration config)
        {
            if (!isInitialized || webStrands.Count == 0) return;
            if (!SeasonalThemeManager.IsSeasonalThemeEnabled(config)) return;
            if (SeasonalThemeManager.GetCurrentSeasonalTheme() != SeasonalTheme.Halloween) return;

            var drawList = ImGui.GetWindowDrawList();
            var themeColors = SeasonalThemeManager.GetCurrentThemeColors(config);
            
            // Very subtle web color
            var webColor = new Vector4(themeColors.SecondaryAccent.X, themeColors.SecondaryAccent.Y, themeColors.SecondaryAccent.Z, 0.15f);
            uint color = ImGui.GetColorU32(webColor);

            foreach (var strand in webStrands)
            {
                if (strand.Length >= 2)
                {
                    for (int i = 0; i < strand.Length - 1; i++)
                    {
                        drawList.AddLine(strand[i], strand[i + 1], color, 1.0f);
                    }
                }
            }
        }
    }

    public class HalloweenParticleEffect
    {
        private List<Particle> particles = new();
        private float duration = 1.2f;
        private float elapsed = 0;
        private bool isActive = false;
        private Vector2 origin;

        public bool IsActive => isActive && elapsed < duration;

        public void Trigger(Vector2 position, Configuration config)
        {
            particles.Clear();
            origin = position;
            elapsed = 0;
            isActive = true;

            var random = new Random();
            int particleCount = 15; // More particles for Halloween effect
            
            var themeColors = SeasonalThemeManager.GetCurrentThemeColors(config);

            for (int i = 0; i < particleCount; i++)
            {
                float angle = (float)(random.NextDouble() * Math.PI * 2);
                float speed = 60f + (float)(random.NextDouble() * 80f);
                float life = 0.6f + (float)(random.NextDouble() * 0.6f);

                // Use Halloween colors
                Vector4 baseColor = i % 2 == 0 ? themeColors.PrimaryAccent : themeColors.SecondaryAccent;

                var particle = new Particle
                {
                    Position = position + new Vector2(
                        (float)(random.NextDouble() * 15 - 7.5),
                        (float)(random.NextDouble() * 15 - 7.5)
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
                    Size = 3f + (float)(random.NextDouble() * 3f)
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

                    // Draw with glow effect
                    drawList.AddCircleFilled(
                        particle.Position,
                        particle.Size,
                        color,
                        8
                    );

                    // Add glow
                    var glowColor = new Vector4(particle.Color.X, particle.Color.Y, particle.Color.Z, particle.Color.W * 0.4f);
                    drawList.AddCircleFilled(
                        particle.Position,
                        particle.Size * 2.0f,
                        ImGui.GetColorU32(glowColor),
                        12
                    );
                }
            }
        }
    }
}
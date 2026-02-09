using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

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
            if (SeasonalThemeManager.GetEffectiveTheme(config) != SeasonalTheme.Halloween) return;

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

    public class WinterSnowEffect
    {
        private List<Vector2[]> icicleShapes = new();
        private List<Vector2[]> snowPileShapes = new();
        private Random random = new();
        private bool isInitialized = false;
        private Vector2[] corners = new Vector2[4];
        private float effectSize = 25f;

        public void Initialize(Vector2[] cardCorners, float size = 25f)
        {
            if (cardCorners.Length >= 4)
            {
                corners = new Vector2[4];
                Array.Copy(cardCorners, corners, 4);
                effectSize = size;
                GenerateSnowEffects();
                isInitialized = true;
            }
        }

        public void Initialize(Vector2 min, Vector2 max, float size = 25f)
        {
            // Convert min/max to corner array for compatibility
            corners = new Vector2[]
            {
                new Vector2(min.X, min.Y), // Top-left
                new Vector2(max.X, min.Y), // Top-right  
                new Vector2(min.X, max.Y), // Bottom-left
                new Vector2(max.X, max.Y)  // Bottom-right
            };
            effectSize = size;
            GenerateSnowEffects();
            isInitialized = true;
        }

        private void GenerateSnowEffects()
        {
            icicleShapes.Clear();
            snowPileShapes.Clear();

            float cardWidth = corners[1].X - corners[0].X; // Top-right X - Top-left X
            float cardHeight = corners[2].Y - corners[0].Y; // Bottom-left Y - Top-left Y

            // Generate icicles hanging from the top edge
            int icicleCount = 4 + random.Next(3); // 4-6 icicles for better distribution
            for (int i = 0; i < icicleCount; i++)
            {
                float x = corners[0].X + (cardWidth * 0.15f) + (cardWidth * 0.7f * ((float)i / (icicleCount - 1)));
                float length = 8f + random.NextSingle() * 12f;
                float width = 1.5f + random.NextSingle() * 2f;

                // Create icicle as triangle shape (store final positions)
                Vector2 top = new Vector2(x, corners[0].Y); // Top of card
                Vector2 bottom = new Vector2(x, corners[0].Y + length); // Hanging down
                Vector2 left = new Vector2(x - width, corners[0].Y);
                Vector2 right = new Vector2(x + width, corners[0].Y);

                // Store icicle as triangle vertices
                var icicleTriangle = new Vector2[] { left, right, bottom };
                icicleShapes.Add(icicleTriangle);
            }

            // Generate snow piles along the bottom edge  
            int snowPileCount = 3 + random.Next(2); // 3-4 snow piles
            for (int i = 0; i < snowPileCount; i++)
            {
                float x = corners[2].X + (cardWidth * 0.1f) + (cardWidth * 0.8f * ((float)i / (snowPileCount - 1)));
                float pileWidth = 8f + random.NextSingle() * 6f;
                float pileHeight = 3f + random.NextSingle() * 3f;

                // Create snow pile as curved mound (store points for natural pile shape)
                var pilePoints = new List<Vector2>();
                for (float j = -pileWidth; j <= pileWidth; j += 1.5f)
                {
                    float normalizedPos = Math.Abs(j / pileWidth);
                    float height = pileHeight * (1f - normalizedPos * normalizedPos);
                    pilePoints.Add(new Vector2(x + j, corners[2].Y - height));
                }
                snowPileShapes.Add(pilePoints.ToArray());
            }
        }

        public void Draw(Configuration config)
        {
            if (!isInitialized) return;
            if (!SeasonalThemeManager.IsSeasonalThemeEnabled(config)) return;

            var effectiveTheme = SeasonalThemeManager.GetEffectiveTheme(config);
            if (effectiveTheme != SeasonalTheme.Winter && effectiveTheme != SeasonalTheme.Christmas) return;

            var drawList = ImGui.GetWindowDrawList();
            var themeColors = SeasonalThemeManager.GetCurrentThemeColors(config);

            // Snow color - bright white with slight blue tint
            var snowColor = new Vector4(0.95f, 0.98f, 1.0f, 0.9f);
            var iceColor = new Vector4(0.85f, 0.95f, 1.0f, 0.7f);
            uint snowColorU32 = ImGui.GetColorU32(snowColor);
            uint iceColorU32 = ImGui.GetColorU32(iceColor);

            // Draw icicles - same pattern as spider webs
            foreach (var icicleTriangle in icicleShapes)
            {
                if (icicleTriangle.Length >= 3)
                {
                    // Draw icicle triangle
                    drawList.AddTriangleFilled(icicleTriangle[0], icicleTriangle[1], icicleTriangle[2], iceColorU32);
                    
                    // Add highlight line on one side
                    Vector2 highlight1 = icicleTriangle[0] + new Vector2(0.3f, 0);
                    Vector2 highlight2 = icicleTriangle[2] + new Vector2(-0.3f, 0);
                    drawList.AddLine(highlight1, highlight2, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.6f)), 0.8f);
                }
            }

            // Draw snow piles - natural curved shapes
            foreach (var snowPile in snowPileShapes)
            {
                if (snowPile.Length >= 2)
                {
                    // Draw snow pile as connected points creating a natural mound
                    foreach (var point in snowPile)
                    {
                        drawList.AddCircleFilled(point, 1.2f, snowColorU32, 8);
                        
                        // Add subtle highlight
                        drawList.AddCircleFilled(point + new Vector2(0, -0.5f), 0.7f, 
                            ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.4f)), 6);
                    }
                }
            }
        }
    }

    public class WinterBackgroundSnow
    {
        private List<SnowFlake> snowFlakes = new();
        private Random random = new();
        private float spawnTimer = 0;
        private float spawnInterval = 0.1f; // Spawn snow flakes regularly
        private Vector2 effectArea = Vector2.Zero;
        private Vector2 absoluteOffset = Vector2.Zero;
        private bool isActive = false;
        private bool useAbsoluteCoordinates = false;
        
        // Configuration for different snow types
        private float alphaMultiplier = 1.0f;
        private float sizeMultiplier = 1.0f;
        private float spawnRateMultiplier = 1.0f;

        public void SetEffectArea(Vector2 area)
        {
            effectArea = area;
            isActive = area.X > 0 && area.Y > 0;
            useAbsoluteCoordinates = false;
        }

        public void SetEffectAreaAbsolute(Vector2 windowPos, Vector2 windowSize)
        {
            effectArea = windowSize;
            absoluteOffset = windowPos;
            isActive = windowSize.X > 0 && windowSize.Y > 0;
            useAbsoluteCoordinates = true;
        }

        public void ConfigureSnowEffect(float alpha = 1.0f, float size = 1.0f, float spawnRate = 1.0f)
        {
            alphaMultiplier = alpha;
            sizeMultiplier = size;
            spawnRateMultiplier = spawnRate;
        }

        public void Update(float deltaTime)
        {
            if (!isActive) return;

            spawnTimer += deltaTime;

            // Spawn new snow flakes
            if (spawnTimer >= spawnInterval)
            {
                SpawnSnowFlakes();
                spawnTimer = 0;
            }

            // Update existing snow flakes
            for (int i = snowFlakes.Count - 1; i >= 0; i--)
            {
                snowFlakes[i].Update(deltaTime);
                
                // Remove snow flakes that have fallen off screen
                if (useAbsoluteCoordinates)
                {
                    // For absolute coordinates, allow snow to fall much further down
                    float leftBound = absoluteOffset.X - 50;
                    float rightBound = absoluteOffset.X + effectArea.X + 50;
                    float bottomBound = absoluteOffset.Y + effectArea.Y + 500; // Allow snow to fall way past the bottom
                    
                    if (!snowFlakes[i].IsAlive || 
                        snowFlakes[i].Position.X < leftBound || snowFlakes[i].Position.X > rightBound ||
                        snowFlakes[i].Position.Y > bottomBound)
                    {
                        snowFlakes.RemoveAt(i);
                    }
                }
                else
                {
                    // Original bounds checking for relative coordinates
                    if (!snowFlakes[i].IsAlive || 
                        snowFlakes[i].Position.X < -50 || snowFlakes[i].Position.X > effectArea.X + 50 ||
                        snowFlakes[i].Position.Y > effectArea.Y + 50)
                    {
                        snowFlakes.RemoveAt(i);
                    }
                }
            }

            // Limit particle count for performance
            if (snowFlakes.Count > 100)
            {
                snowFlakes.RemoveRange(0, snowFlakes.Count - 100);
            }
        }

        private void SpawnSnowFlakes()
        {
            // Spawn snow flakes with configurable rate
            int baseMin = Math.Max(1, (int)(3 * spawnRateMultiplier));
            int baseMax = Math.Max(2, (int)(7 * spawnRateMultiplier));
            int count = random.Next(baseMin, baseMax);
            
            for (int i = 0; i < count; i++)
            {
                // Start snowflakes from top, random X position
                Vector2 startPos = new Vector2(random.NextSingle() * effectArea.X, -20);
                
                // For absolute coordinates, add the window offset to spawn position
                if (useAbsoluteCoordinates)
                {
                    startPos += absoluteOffset;
                }
                
                Vector2 velocity = new Vector2(
                    (random.NextSingle() - 0.5f) * 15f, // Gentle sideways drift
                    20f + random.NextSingle() * 15f     // Slower downward fall speed
                );

                // Much longer life for absolute coordinate snow to traverse full character grid height
                float baseLife = useAbsoluteCoordinates ? 60.0f : 12.0f;
                float extraLife = useAbsoluteCoordinates ? 30.0f : 8.0f;
                
                var snowFlake = new SnowFlake
                {
                    Position = startPos,
                    Velocity = velocity,
                    Color = new Vector4(0.95f, 0.98f, 1.0f, (0.6f + random.NextSingle() * 0.3f) * alphaMultiplier), // Configurable alpha
                    Life = baseLife + random.NextSingle() * extraLife, // Longer-lived for absolute coordinates
                    MaxLife = baseLife + random.NextSingle() * extraLife,
                    Size = (2.0f + random.NextSingle() * 2.0f) * sizeMultiplier, // Configurable size
                    NoiseOffset = random.NextSingle() * MathF.PI * 2,
                    SwayAmount = 3f + random.NextSingle() * 7f // Less aggressive sway
                };

                snowFlakes.Add(snowFlake);
            }
        }

        public void Draw()
        {
            Draw(false);
        }

        public void DrawAbsolute()
        {
            Draw(true);
        }

        private void Draw(bool useAbsolutePositions)
        {
            if (!isActive) return;
            
            var drawList = ImGui.GetWindowDrawList();
            var windowPos = ImGui.GetWindowPos();

            foreach (var snowFlake in snowFlakes)
            {
                if (snowFlake.IsAlive && snowFlake.Color.W > 0.01f)
                {
                    uint snowColor = ImGui.GetColorU32(snowFlake.Color);

                    // Draw snowflake at appropriate position
                    Vector2 pos = useAbsolutePositions 
                        ? snowFlake.Position  // Absolute position for background effects
                        : snowFlake.Position + windowPos; // Window-relative position for normal effects
                    float size = snowFlake.Size;
                    
                    // Draw a small cross/plus shape with thicker lines
                    drawList.AddLine(
                        new Vector2(pos.X - size, pos.Y), 
                        new Vector2(pos.X + size, pos.Y), 
                        snowColor, 1.5f);
                    drawList.AddLine(
                        new Vector2(pos.X, pos.Y - size), 
                        new Vector2(pos.X, pos.Y + size), 
                        snowColor, 1.5f);
                    
                    // Add diagonal lines for star effect (smaller)
                    float diagonalSize = size * 0.6f;
                    drawList.AddLine(
                        new Vector2(pos.X - diagonalSize, pos.Y - diagonalSize), 
                        new Vector2(pos.X + diagonalSize, pos.Y + diagonalSize), 
                        snowColor, 1.2f);
                    drawList.AddLine(
                        new Vector2(pos.X - diagonalSize, pos.Y + diagonalSize), 
                        new Vector2(pos.X + diagonalSize, pos.Y - diagonalSize), 
                        snowColor, 1.2f);
                }
            }
        }

        public void Reset()
        {
            snowFlakes.Clear();
            spawnTimer = 0;
        }
    }

    public class SnowFlake
    {
        public Vector2 Position { get; set; }
        public Vector2 Velocity { get; set; }
        public Vector4 Color { get; set; }
        public float Life { get; set; }
        public float MaxLife { get; set; }
        public float Size { get; set; }
        public float NoiseOffset { get; set; }
        public float SwayAmount { get; set; }

        public bool IsAlive => Life > 0;

        public void Update(float deltaTime)
        {
            Life -= deltaTime;
            
            // Add gentle swaying motion
            float swayX = MathF.Sin(Life * 1.5f + NoiseOffset) * SwayAmount * deltaTime;
            Vector2 sway = new Vector2(swayX, 0);
            
            Position += (Velocity + sway) * deltaTime;

            // Gentle fade as it ages (but keep mostly visible)
            float lifeFraction = Life / MaxLife;
            // Keep snowflakes more visible for longer - only fade in the last 20% of their life
            float alpha = lifeFraction < 0.2f ? (lifeFraction / 0.2f) * 0.9f : 0.9f;
            Color = new Vector4(Color.X, Color.Y, Color.Z, alpha);
        }
    }

    public class FloatingHeart
    {
        public Vector2 Position { get; set; }
        public Vector2 Velocity { get; set; }
        public Vector4 Color { get; set; }
        public float Life { get; set; }
        public float MaxLife { get; set; }
        public float Size { get; set; }
        public float NoiseOffset { get; set; }
        public float SwayAmount { get; set; }
        public float PulseOffset { get; set; }

        public bool IsAlive => Life > 0;

        public void Update(float deltaTime)
        {
            Life -= deltaTime;

            // Gentle falling motion with smooth sway - like snowflakes
            float swayX = MathF.Sin(Life * 1.2f + NoiseOffset) * SwayAmount * deltaTime;
            Vector2 sway = new Vector2(swayX, 0);

            Position += (Velocity * deltaTime) + sway;

            // Keep mostly visible, gentle fade at end of life
            float lifeFraction = Life / MaxLife;
            float alpha = lifeFraction < 0.15f ? (lifeFraction / 0.15f) * 0.85f : 0.85f;
            Color = new Vector4(Color.X, Color.Y, Color.Z, alpha);
        }
    }

    public class ValentinesHeartsEffect
    {
        private List<FloatingHeart> hearts = new();
        private Random random = new();
        private float spawnTimer = 0;
        private float spawnInterval = 0.12f; // Similar to snow
        private Vector2 effectArea = Vector2.Zero;
        private Vector2 absoluteOffset = Vector2.Zero;
        private bool isActive = false;
        private bool useAbsoluteCoordinates = false;

        // Vibrant, saturated heart colours
        private static readonly Vector4[] HeartColors = new[]
        {
            new Vector4(1.0f, 0.0f, 0.5f, 1.0f),   // Vivid magenta-pink
            new Vector4(1.0f, 0.1f, 0.3f, 1.0f),   // Vivid red-pink
            new Vector4(0.95f, 0.0f, 0.35f, 1.0f), // Pure rose
            new Vector4(1.0f, 0.2f, 0.6f, 1.0f),   // Bright pink
            new Vector4(0.9f, 0.0f, 0.15f, 1.0f),  // Deep red
        };

        public void SetEffectArea(Vector2 area)
        {
            effectArea = area;
            isActive = area.X > 0 && area.Y > 0;
            useAbsoluteCoordinates = false;
        }

        public void SetEffectAreaAbsolute(Vector2 windowPos, Vector2 windowSize)
        {
            effectArea = windowSize;
            absoluteOffset = windowPos;
            isActive = windowSize.X > 0 && windowSize.Y > 0;
            useAbsoluteCoordinates = true;
        }

        public void Update(float deltaTime)
        {
            if (!isActive) return;

            spawnTimer += deltaTime;

            if (spawnTimer >= spawnInterval)
            {
                SpawnHearts();
                spawnTimer = 0;
            }

            // Update existing hearts
            for (int i = hearts.Count - 1; i >= 0; i--)
            {
                hearts[i].Update(deltaTime);

                // Remove hearts that have fallen off screen or died
                if (useAbsoluteCoordinates)
                {
                    float bottomBound = absoluteOffset.Y + effectArea.Y + 100;
                    if (!hearts[i].IsAlive || hearts[i].Position.Y > bottomBound)
                    {
                        hearts.RemoveAt(i);
                    }
                }
                else
                {
                    if (!hearts[i].IsAlive || hearts[i].Position.Y > effectArea.Y + 50)
                    {
                        hearts.RemoveAt(i);
                    }
                }
            }

            // Particle limit like snow
            if (hearts.Count > 80)
            {
                hearts.RemoveRange(0, hearts.Count - 80);
            }
        }

        private void SpawnHearts()
        {
            int count = random.Next(1, 4); // Similar to snow spawn rate

            for (int i = 0; i < count; i++)
            {
                // Hearts start from TOP and fall DOWN like snow
                Vector2 startPos = new Vector2(
                    random.NextSingle() * effectArea.X,
                    -20  // Start above the window
                );

                if (useAbsoluteCoordinates)
                {
                    startPos += absoluteOffset;
                }

                // Fall downward with gentle sideways drift - like snow
                Vector2 velocity = new Vector2(
                    (random.NextSingle() - 0.5f) * 15f,  // Gentle sideways drift
                    18f + random.NextSingle() * 12f      // Downward fall (positive Y)
                );

                float baseLife = useAbsoluteCoordinates ? 30.0f : 12.0f;

                // Pick a vibrant colour
                var heartColor = HeartColors[random.Next(HeartColors.Length)];

                var heart = new FloatingHeart
                {
                    Position = startPos,
                    Velocity = velocity,
                    Color = heartColor,
                    Life = baseLife + random.NextSingle() * 8.0f,
                    MaxLife = baseLife + 8.0f,
                    Size = 5f + random.NextSingle() * 5f,  // Moderate size
                    NoiseOffset = random.NextSingle() * MathF.PI * 2,
                    SwayAmount = 12f + random.NextSingle() * 8f,  // Gentle sway
                    PulseOffset = random.NextSingle() * MathF.PI * 2
                };

                hearts.Add(heart);
            }
        }

        public void Draw()
        {
            Draw(false);
        }

        public void DrawAbsolute()
        {
            Draw(true);
        }

        private void Draw(bool useAbsolutePositions)
        {
            if (!isActive) return;

            var drawList = ImGui.GetWindowDrawList();
            var windowPos = ImGui.GetWindowPos();

            foreach (var heart in hearts)
            {
                if (heart.IsAlive && heart.Color.W > 0.01f)
                {
                    Vector2 pos = useAbsolutePositions
                        ? heart.Position
                        : heart.Position + windowPos;

                    float size = heart.Size;

                    // Draw glow layer first (larger, more transparent)
                    var glowColor = new Vector4(heart.Color.X, heart.Color.Y, heart.Color.Z, heart.Color.W * 0.3f);
                    DrawSmoothHeart(drawList, pos, size * 1.4f, ImGui.GetColorU32(glowColor));

                    // Draw main heart
                    DrawSmoothHeart(drawList, pos, size, ImGui.GetColorU32(heart.Color));
                }
            }
        }

        private void DrawSmoothHeart(ImDrawListPtr drawList, Vector2 center, float size, uint color)
        {
            // Draw a smoother heart using more circle segments and better proportions
            float scale = size / 10f;

            // Heart shape made of overlapping circles and a quad for smoother look
            float topRadius = 2.8f * scale;
            float topOffset = 2.0f * scale;
            float topY = -1.0f * scale;

            // Left lobe
            drawList.AddCircleFilled(
                new Vector2(center.X - topOffset, center.Y + topY),
                topRadius, color, 8);

            // Right lobe
            drawList.AddCircleFilled(
                new Vector2(center.X + topOffset, center.Y + topY),
                topRadius, color, 8);

            // Bottom point using a smooth triangle
            Vector2 bottomPoint = new Vector2(center.X, center.Y + 5.5f * scale);
            Vector2 leftPoint = new Vector2(center.X - 4.2f * scale, center.Y + 0.8f * scale);
            Vector2 rightPoint = new Vector2(center.X + 4.2f * scale, center.Y + 0.8f * scale);

            drawList.AddTriangleFilled(leftPoint, rightPoint, bottomPoint, color);

            // Fill the center gap with a rectangle
            drawList.AddRectFilled(
                new Vector2(center.X - topOffset, center.Y + topY - topRadius * 0.3f),
                new Vector2(center.X + topOffset, center.Y + 1.5f * scale),
                color);
        }

        public void Reset()
        {
            hearts.Clear();
            spawnTimer = 0;
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
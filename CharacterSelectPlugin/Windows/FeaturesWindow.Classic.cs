using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;

namespace CharacterSelectPlugin.Windows;

public partial class FeaturesWindow
{
    private struct Particle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Life;
        public float MaxLife;
        public float Size;
        public Vector4 Color;
    }

    private List<Particle> particles = new();
    private float particleTimer = 0f;
    private Random particleRandom = new();

    private void DrawClassicLayout()
{
        var scale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;

        // Banner area
        DrawClassicBanner();

        ImGui.Spacing();

        // Search bar
        ImGui.SetNextItemWidth(-1);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 8));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8);
        ImGui.InputTextWithHint("##FeatureSearch", "  Search... (try 'name', 'random', 'mods', 'backup')", ref searchQuery, 100);
        ImGui.PopStyleVar(2);

        ImGui.Spacing();
        ImGui.Spacing();

        // Content area
        ImGui.BeginChild("FeaturesScrollArea", Vector2.Zero, false);

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            DrawClassicSearchResults();
        }
        else
        {
            DrawClassicAllFeatures();
        }

        ImGui.EndChild();
    }

    private void DrawClassicBanner()
{
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var windowWidth = ImGui.GetWindowWidth();
        var bannerHeight = 80f * ImGuiHelpers.GlobalScale;

        // Try to load banner image
        bool imageDrawn = false;
        try
        {
            var pluginDirectory = Plugin.PluginInterface.AssemblyLocation.Directory?.FullName;
            if (pluginDirectory != null)
            {
                string imagePath = Path.Combine(pluginDirectory, "Assets", "Feature Banner.png");
                if (File.Exists(imagePath))
                {
                    var texture = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
                    if (texture != null)
                    {
                        var contentWidth = windowWidth - 16;

                        // Fill the banner area width, crop height if needed
                        float imageAspect = (float)texture.Width / texture.Height;
                        float drawWidth = contentWidth;
                        float drawHeight = drawWidth / imageAspect;

                        // Center vertically if image is taller than banner
                        float offsetY = 0;
                        if (drawHeight > bannerHeight)
                        {
                            offsetY = (bannerHeight - drawHeight) * 0.5f;
                        }

                        // Clip to banner region
                        drawList.PushClipRect(cursorPos, new Vector2(cursorPos.X + contentWidth, cursorPos.Y + bannerHeight), true);

                        // Darken overlay for text readability
                        drawList.AddRectFilled(
                            cursorPos,
                            new Vector2(cursorPos.X + contentWidth, cursorPos.Y + bannerHeight),
                            ImGui.ColorConvertFloat4ToU32(new Vector4(0.05f, 0.05f, 0.08f, 1.0f)),
                            8);

                        // Draw image filling width
                        drawList.AddImage(
                            (ImTextureID)texture.Handle,
                            new Vector2(cursorPos.X, cursorPos.Y + offsetY),
                            new Vector2(cursorPos.X + drawWidth, cursorPos.Y + offsetY + drawHeight),
                            Vector2.Zero,
                            Vector2.One,
                            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.7f)));

                        drawList.PopClipRect();
                        imageDrawn = true;
                    }
                }
            }
        }
        catch { }

        // Fallback background if no image
        if (!imageDrawn)
        {
            drawList.AddRectFilled(
                cursorPos,
                new Vector2(cursorPos.X + windowWidth - 16, cursorPos.Y + bannerHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.12f, 0.12f, 0.18f, 0.95f)),
                8);
        }

        // Title text - centered
        var titleText = "Discover CS+ Features";
        ImGui.PushFont(UiBuilder.DefaultFont);
        ImGui.SetWindowFontScale(1.6f);
        var titleSize = ImGui.CalcTextSize(titleText);
        var titleX = cursorPos.X + (windowWidth - 16 - titleSize.X) * 0.5f;
        var titleY = cursorPos.Y + (bannerHeight - titleSize.Y) * 0.35f;

        // Shadow
        drawList.AddText(new Vector2(titleX + 2, titleY + 2), ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.5f)), titleText);
        // Main text - white
        drawList.AddText(new Vector2(titleX, titleY), ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f)), titleText);
        ImGui.SetWindowFontScale(1.0f);
        ImGui.PopFont();

        // Subtitle
        var subtitleText = "Tips, tricks, and hidden gems";
        var subtitleSize = ImGui.CalcTextSize(subtitleText);
        var subtitleX = cursorPos.X + (windowWidth - 16 - subtitleSize.X) * 0.5f;
        var subtitleY = titleY + titleSize.Y + 4;
        drawList.AddText(new Vector2(subtitleX, subtitleY), ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.75f, 1f)), subtitleText);

        // Draw particles on top
        DrawClassicParticleEffects(drawList, cursorPos, new Vector2(windowWidth - 16, bannerHeight));

        // Move cursor past banner
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + bannerHeight + 8);
    }

    private void DrawClassicSearchResults()
{
        var query = searchQuery.ToLowerInvariant().Trim();
        var results = allFeatures.Where(f =>
            f.Name.ToLowerInvariant().Contains(query) ||
            f.Description.ToLowerInvariant().Contains(query) ||
            f.Keywords.Any(k => k.Contains(query))
        ).ToList();

        if (results.Count == 0)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
            ImGui.SetCursorPosX(20);
            ImGui.TextWrapped($"No features found for \"{searchQuery}\"");
            ImGui.Spacing();
            ImGui.SetCursorPosX(20);
            ImGui.Text("Try: name, random, mods, backup, theme, profile");
            ImGui.PopStyleColor();
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
        ImGui.SetCursorPosX(10);
        ImGui.Text($"{results.Count} result{(results.Count == 1 ? "" : "s")}");
        ImGui.PopStyleColor();
        ImGui.Spacing();

        foreach (var feature in results)
        {
            DrawClassicFeatureCard(feature);
        }
    }

    private void DrawClassicAllFeatures()
{
        var categories = allFeatures.GroupBy(f => f.Category).ToList();

        foreach (var category in categories)
        {
            DrawClassicCategoryHeader(category.Key);

            foreach (var feature in category)
            {
                DrawClassicFeatureCard(feature);
            }

            ImGui.Spacing();
            ImGui.Spacing();
        }

        // Footer
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
        ImGui.SetCursorPosX(20);
        ImGui.TextWrapped("Tip: Use /select <name> to quickly switch characters, or /select random for a surprise!");
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private void DrawClassicCategoryHeader(string title)
{
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var availWidth = ImGui.GetContentRegionAvail().X;

        // Category colour based on name
        var colour = title switch
        {
            "Quick Actions" => new Vector4(1.0f, 0.8f, 0.3f, 1.0f),
            "Your Identity" => new Vector4(0.3f, 0.9f, 1.0f, 1.0f),
            "Automation" => new Vector4(0.7f, 0.5f, 1.0f, 1.0f),
            "Organization" => new Vector4(0.4f, 0.7f, 1.0f, 1.0f),
            "Apply to Target" => new Vector4(0.3f, 0.85f, 0.7f, 1.0f),
            "RP Profiles" => new Vector4(1.0f, 0.5f, 0.7f, 1.0f),
            "Mod Management" => new Vector4(1.0f, 0.6f, 0.3f, 1.0f),
            "Capturing Looks" => new Vector4(0.3f, 0.9f, 0.9f, 1.0f),
            "Chat Commands" => new Vector4(0.6f, 0.7f, 0.85f, 1.0f),
            "Customize CS+" => new Vector4(0.8f, 0.5f, 1.0f, 1.0f),
            "Backup & Safety" => new Vector4(0.5f, 0.9f, 0.6f, 1.0f),
            _ => new Vector4(0.7f, 0.7f, 0.7f, 1.0f)
        };

        // Left accent bar
        drawList.AddRectFilled(
            cursorPos,
            new Vector2(cursorPos.X + 4, cursorPos.Y + 22),
            ImGui.ColorConvertFloat4ToU32(colour),
            2);

        // Title
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 14);
        ImGui.PushStyleColor(ImGuiCol.Text, colour);
        ImGui.Text(title.ToUpperInvariant());
        ImGui.PopStyleColor();

        // Horizontal line
        ImGui.SameLine();
        var lineStart = ImGui.GetCursorScreenPos();
        lineStart.X += 10;
        lineStart.Y += 8;
        drawList.AddLine(
            lineStart,
            new Vector2(cursorPos.X + availWidth - 10, lineStart.Y),
            ImGui.ColorConvertFloat4ToU32(colour * 0.3f),
            1);

        ImGui.Spacing();
        ImGui.Spacing();
    }

    private void DrawClassicFeatureCard(FeatureEntry feature)
{
        var drawList = ImGui.GetWindowDrawList();
        var availWidth = ImGui.GetContentRegionAvail().X;
        var scale = ImGuiHelpers.GlobalScale;

        ImGui.PushID(feature.Name);

        ImGui.BeginGroup();

        // Icon
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 10);
        ImGui.PushStyleColor(ImGuiCol.Text, feature.IconColor);
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.Text(feature.Icon.ToIconString());
        ImGui.PopFont();
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8);

        // Text content
        ImGui.BeginGroup();

        // Feature name
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.95f, 0.95f, 1.0f));
        ImGui.Text(feature.Name);
        ImGui.PopStyleColor();

        // Description
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.65f, 0.65f, 0.65f, 1.0f));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + availWidth - 80);
        ImGui.TextWrapped(feature.Description);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();

        // Location
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.45f, 0.5f, 0.55f, 1.0f));
        ImGui.Text(feature.Location);
        ImGui.PopStyleColor();

        ImGui.EndGroup();
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.PopID();
    }

    private void DrawClassicParticleEffects(ImDrawListPtr drawList, Vector2 bannerStart, Vector2 bannerSize)
{
        float deltaTime = 1f / 60f;
        particleTimer += deltaTime;

        if (particleTimer > 0.12f && particles.Count < 35)
        {
            SpawnClassicParticle(bannerStart, bannerSize);
            particleTimer = 0f;
        }

        for (int i = particles.Count - 1; i >= 0; i--)
        {
            var particle = particles[i];

            particle.Position += particle.Velocity * deltaTime;
            particle.Life -= deltaTime;

            if (particle.Life <= 0 ||
                particle.Position.X > bannerStart.X + bannerSize.X + 50 ||
                particle.Position.Y < bannerStart.Y - 50 ||
                particle.Position.Y > bannerStart.Y + bannerSize.Y + 50)
            {
                particles.RemoveAt(i);
                continue;
            }

            float alpha = Math.Min(1f, particle.Life / particle.MaxLife);
            var color = new Vector4(particle.Color.X, particle.Color.Y, particle.Color.Z, particle.Color.W * alpha);

            drawList.AddCircleFilled(
                particle.Position,
                particle.Size,
                ImGui.ColorConvertFloat4ToU32(color));

            // Glow effect for brighter particles
            if (particle.Color.W > 0.5f)
            {
                drawList.AddCircleFilled(
                    particle.Position,
                    particle.Size * 2.5f,
                    ImGui.ColorConvertFloat4ToU32(color with { W = color.W * 0.2f }));
            }

            particles[i] = particle;
        }
    }

    private void SpawnClassicParticle(Vector2 bannerStart, Vector2 bannerSize)
{
        var particle = new Particle
        {
            Position = new Vector2(
                bannerStart.X + (float)particleRandom.NextDouble() * bannerSize.X,
                bannerStart.Y + (float)particleRandom.NextDouble() * bannerSize.Y
            ),

            Velocity = new Vector2(
                -8f + (float)particleRandom.NextDouble() * 16f,
                -12f + (float)particleRandom.NextDouble() * -8f
            ),

            MaxLife = 5f + (float)particleRandom.NextDouble() * 3f,
            Size = 1.5f + (float)particleRandom.NextDouble() * 2f,

            Color = particleRandom.Next(5) switch
            {
                0 => new Vector4(1.0f, 1.0f, 1.0f, 0.8f),   // White
                1 => new Vector4(0.95f, 0.95f, 1.0f, 0.7f), // Soft white
                2 => new Vector4(1.0f, 0.5f, 0.7f, 0.7f),   // Pink
                3 => new Vector4(0.5f, 0.7f, 1.0f, 0.7f),   // Blue
                _ => new Vector4(0.8f, 0.9f, 1.0f, 0.6f)    // Light blue
            }
        };

        particle.Life = particle.MaxLife;
        particles.Add(particle);
    }

}

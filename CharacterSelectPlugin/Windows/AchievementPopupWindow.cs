using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using CharacterSelectPlugin.Effects;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin.Windows;

public class AchievementPopupWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private List<Particle> particles = new();
    private float particleTimer = 0f;
    private bool hasTriggeredBurst = false;

    public AchievementPopupWindow(Plugin plugin) : base("###CSPlusAchievement",
        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize |
        ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        Size = new Vector2(360, 200);
        SizeCondition = ImGuiCond.Always;
    }

    public void Dispose() { }

    public void Show()
    {
        hasTriggeredBurst = false;
        particleTimer = 0f;
        particles.Clear();
        IsOpen = true;
    }

    public override void PreDraw()
    {
        var viewport = ImGui.GetMainViewport();
        var windowSize = Size ?? new Vector2(360, 200);
        var centerPos = new Vector2(
            viewport.Pos.X + (viewport.Size.X - windowSize.X) / 2,
            viewport.Pos.Y + (viewport.Size.Y - windowSize.Y) / 2
        );
        ImGui.SetNextWindowPos(centerPos, ImGuiCond.Appearing);
    }

    public override void Draw()
    {
        var scale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;
        if (scale < 0.5f) scale = 0.5f;
        if (scale > 3f) scale = 3f;

        var windowWidth = ImGui.GetWindowWidth();
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var drawList = ImGui.GetWindowDrawList();

        int themeColorCount = ThemeHelper.PushThemeColors(plugin.Configuration);
        int themeStyleVarCount = ThemeHelper.PushThemeStyleVars(plugin.Configuration.UIScaleMultiplier);

        try
        {
            // Gold accent bar across top
            var gold = new Vector4(1.0f, 0.84f, 0.0f, 1.0f);
            drawList.AddRectFilled(
                windowPos,
                new Vector2(windowPos.X + windowWidth, windowPos.Y + 4 * scale),
                ImGui.ColorConvertFloat4ToU32(gold)
            );

            // Sparkle burst on first frame
            if (!hasTriggeredBurst)
            {
                hasTriggeredBurst = true;
                SpawnBurst(new Vector2(windowPos.X + windowWidth / 2, windowPos.Y + windowSize.Y * 0.4f));
            }

            // Update and draw particles
            float dt = 1f / 60f;
            particleTimer += dt;

            for (int i = particles.Count - 1; i >= 0; i--)
            {
                particles[i].Update(dt);
                if (!particles[i].IsAlive)
                {
                    particles.RemoveAt(i);
                    continue;
                }

                var p = particles[i];
                var col = ImGui.ColorConvertFloat4ToU32(p.Color);
                drawList.AddCircleFilled(p.Position, p.Size * scale, col);

                // Glow on brighter particles
                if (p.Color.W > 0.5f)
                {
                    var glowCol = new Vector4(p.Color.X, p.Color.Y, p.Color.Z, p.Color.W * 0.3f);
                    drawList.AddCircleFilled(p.Position, p.Size * scale * 2f, ImGui.ColorConvertFloat4ToU32(glowCol));
                }
            }

            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 16 * scale);

            // Header
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
            CenterText("\u2605 ACHIEVEMENT UNLOCKED \u2605", windowWidth, 0.9f);
            ImGui.PopStyleColor();

            ImGui.Spacing();

            // Title
            ImGui.PushStyleColor(ImGuiCol.Text, gold);
            CenterText("Who Am I Again?", windowWidth, 1.4f);
            ImGui.PopStyleColor();

            ImGui.Spacing();

            // Flavour text
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
            CenterText("One page wasn't enough.", windowWidth, 0.95f);
            ImGui.PopStyleColor();

            ImGui.Spacing();

            // Subtitle
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
            CenterText("Created your 41st character.", windowWidth, 0.85f);
            ImGui.PopStyleColor();

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Spacing();

            // Dismiss button
            float buttonWidth = 80 * scale;
            ImGui.SetCursorPosX((windowWidth - buttonWidth) / 2);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.68f, 0.0f, 0.6f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.76f, 0.0f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1.0f, 0.84f, 0.0f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            if (ImGui.Button("Nice!", new Vector2(buttonWidth, 28 * scale)))
            {
                IsOpen = false;
            }
            ImGui.PopStyleColor(4);
        }
        finally
        {
            ImGui.PopStyleColor(themeColorCount);
            ImGui.PopStyleVar(themeStyleVarCount);
        }
    }

    private void SpawnBurst(Vector2 center)
    {
        var random = new Random();
        // Multi-colour gold/warm burst
        Vector4[] colours = {
            new(1.0f, 0.84f, 0.0f, 1.0f),  // Gold
            new(1.0f, 0.65f, 0.0f, 1.0f),  // Orange-gold
            new(1.0f, 0.9f, 0.4f, 1.0f),   // Light gold
            new(1.0f, 1.0f, 0.6f, 1.0f),   // Pale yellow
            new(0.9f, 0.75f, 0.2f, 1.0f),  // Warm gold
        };

        for (int i = 0; i < 24; i++)
        {
            float angle = (float)(random.NextDouble() * Math.PI * 2);
            float speed = 40f + (float)(random.NextDouble() * 120f);
            float life = 0.5f + (float)(random.NextDouble() * 0.6f);

            particles.Add(new Particle
            {
                Position = center + new Vector2(
                    (float)(random.NextDouble() * 16 - 8),
                    (float)(random.NextDouble() * 16 - 8)
                ),
                Velocity = new Vector2(
                    (float)Math.Cos(angle) * speed,
                    (float)Math.Sin(angle) * speed
                ),
                Color = colours[random.Next(colours.Length)],
                Life = life,
                MaxLife = life,
                Size = 2f + (float)(random.NextDouble() * 2.5f)
            });
        }
    }

    private static void CenterText(string text, float windowWidth, float fontScale)
    {
        ImGui.SetWindowFontScale(fontScale);
        float textWidth = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorPosX((windowWidth - textWidth) / 2);
        ImGui.TextUnformatted(text);
        ImGui.SetWindowFontScale(1.0f);
    }
}

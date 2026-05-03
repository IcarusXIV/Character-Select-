using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace CharacterSelectPlugin.Windows.Styles
{
    public class UIStyles
    {
        private Plugin plugin;
        private int styleStackCount = 0;
        private int colorStackCount = 0;

        // Hover sheen state, keyed by per-element string ID
        private readonly System.Collections.Generic.Dictionary<string, DateTime> hoverSweepStarts = new();
        private const float HoverSweepDuration = 0.65f;

        public UIStyles(Plugin plugin)
        {
            this.plugin = plugin;
        }

        /// <summary>
        /// Per-element hover sweep state. Returns 0..1 sweep progress while an element
        /// is freshly hovered, or -1 when nothing should draw (either not hovered or the
        /// sweep has finished its one-shot animation for this hover).
        /// </summary>
        public float UpdateAndGetHoverSweepProgress(string id, bool isHovered)
        {
            if (!isHovered)
            {
                hoverSweepStarts.Remove(id);
                return -1f;
            }
            if (!hoverSweepStarts.ContainsKey(id))
                hoverSweepStarts[id] = DateTime.UtcNow;

            float elapsed = (float)(DateTime.UtcNow - hoverSweepStarts[id]).TotalSeconds;
            if (elapsed >= HoverSweepDuration) return -1f;
            return elapsed / HoverSweepDuration;
        }

        /// <summary>
        /// One-liner helper: call immediately after an ImGui.Button (or any item that
        /// supports IsItemHovered + GetItemRectMin/Max) to overlay the hover sheen sweep
        /// on the previously-drawn item. Each call site must pass a unique id so hover
        /// state can be tracked independently per button.
        /// </summary>
        public void ApplyHoverSheenToLastItem(string id, float maxAlpha = 0.18f)
        {
            bool hovered = ImGui.IsItemHovered();
            float sheen = UpdateAndGetHoverSweepProgress(id, hovered);
            if (sheen >= 0f)
            {
                var mn = ImGui.GetItemRectMin();
                var mx = ImGui.GetItemRectMax();
                DrawHoverSheen(ImGui.GetWindowDrawList(), mn, mx, sheen, maxAlpha);
            }
        }

        // Static fallbacks so windows without a UIStyles instance (e.g., QuickSwitchWindow) share state
        private static readonly System.Collections.Generic.Dictionary<string, DateTime> staticHoverSweepStarts = new();
        // Continuous hover effects (perimeter streak etc.) need elapsed-since-hover, kept separate from the one-shot sheen
        private static readonly System.Collections.Generic.Dictionary<string, double> staticHoverStartTimes = new();

        /// <summary>Seconds elapsed since the element started being hovered, or -1 if not hovered.</summary>
        public static float GetHoverElapsedTime(string id, bool isHovered)
        {
            if (!isHovered)
            {
                staticHoverStartTimes.Remove(id);
                return -1f;
            }
            double now = ImGui.GetTime();
            if (!staticHoverStartTimes.TryGetValue(id, out var startTime))
            {
                staticHoverStartTimes[id] = now;
                return 0f;
            }
            return (float)(now - startTime);
        }

        /// <summary>
        /// Static variant of <see cref="ApplyHoverSheenToLastItem"/> for windows that
        /// don't hold a UIStyles instance. Call immediately after an ImGui item.
        /// </summary>
        public static void ApplyHoverSheenToLastItemStatic(string id, float maxAlpha = 0.18f)
        {
            bool hovered = ImGui.IsItemHovered();
            if (!hovered)
            {
                staticHoverSweepStarts.Remove(id);
                return;
            }
            if (!staticHoverSweepStarts.ContainsKey(id))
                staticHoverSweepStarts[id] = DateTime.UtcNow;

            float elapsed = (float)(DateTime.UtcNow - staticHoverSweepStarts[id]).TotalSeconds;
            if (elapsed >= HoverSweepDuration) return;
            float progress = elapsed / HoverSweepDuration;

            var mn = ImGui.GetItemRectMin();
            var mx = ImGui.GetItemRectMax();
            DrawHoverSheen(ImGui.GetWindowDrawList(), mn, mx, progress, maxAlpha);
        }

        /// <summary>
        /// Draws a glossy left-to-right sheen sweep across the (mn, mx) rect. Feeds off
        /// the 0..1 progress returned by <see cref="UpdateAndGetHoverSweepProgress"/>.
        /// Two halves of AddRectFilledMultiColor build the transparent → bright →
        /// transparent gradient, clipped to the rect so the off-screen halves don't render.
        /// </summary>
        public static void DrawHoverSheen(ImDrawListPtr dl, Vector2 mn, Vector2 mx, float progress, float maxAlpha = 0.20f)
        {
            if (progress < 0f || progress > 1f) return;

            float w = mx.X - mn.X;
            float bandLeftX  = mn.X - w + progress * (2f * w);
            float bandRightX = bandLeftX + w;
            float bandMidX   = (bandLeftX + bandRightX) * 0.5f;

            dl.PushClipRect(mn, mx, true);

            uint transparentU = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0f));
            uint brightU      = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, maxAlpha));

            dl.AddRectFilledMultiColor(
                new Vector2(bandLeftX, mn.Y),
                new Vector2(bandMidX,  mx.Y),
                transparentU, brightU,
                brightU,      transparentU);

            dl.AddRectFilledMultiColor(
                new Vector2(bandMidX,   mn.Y),
                new Vector2(bandRightX, mx.Y),
                brightU,      transparentU,
                transparentU, brightU);

            dl.PopClipRect();
        }

        // PreDraw push count, paired with PopCustomWindowBgIfNeeded in PostDraw
        private int preDrawSlotsPushed = 0;

        /// <summary>
        /// PreDraw hook: pushes the chrome slots ImGui paints at Begin time
        /// (WindowBg, TitleBg, TitleBgActive, TitleBgCollapsed, MenuBarBg) so
        /// the title bar respects the Custom theme.
        /// </summary>
        public void PushCustomWindowBgIfNeeded()
        {
            preDrawSlotsPushed = 0;

            if (plugin.Configuration.SelectedTheme != ThemeSelection.Custom)
                return;

            var customTheme = plugin.Configuration.CustomTheme;
            preDrawSlotsPushed += TryPushSlot(customTheme, "color.windowBg",     ImGuiCol.WindowBg);
            preDrawSlotsPushed += TryPushSlot(customTheme, "color.titleBg",      ImGuiCol.TitleBg);
            preDrawSlotsPushed += TryPushSlot(customTheme, "color.titleBgActive", ImGuiCol.TitleBgActive);
            preDrawSlotsPushed += TryPushSlot(customTheme, "color.titleBg",      ImGuiCol.TitleBgCollapsed);
            preDrawSlotsPushed += TryPushSlot(customTheme, "color.menuBarBg",    ImGuiCol.MenuBarBg);
        }

        private static int TryPushSlot(CustomThemeConfig theme, string key, ImGuiCol target)
        {
            if (theme.ColorOverrides.TryGetValue(key, out var packed) && packed.HasValue)
            {
                ImGui.PushStyleColor(target, CustomThemeDefinitions.UnpackColor(packed.Value));
                return 1;
            }
            return 0;
        }

        /// <summary>
        /// Called in PostDraw. Pops every slot that was pushed in PreDraw.
        /// </summary>
        public void PopCustomWindowBgIfNeeded()
        {
            if (preDrawSlotsPushed > 0)
            {
                ImGui.PopStyleColor(preDrawSlotsPushed);
                preDrawSlotsPushed = 0;
            }
        }

        public void PushMainWindowStyle()
        {
            float scale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;

            // Check for Custom theme first (takes priority)
            if (plugin.Configuration.SelectedTheme == ThemeSelection.Custom)
            {
                PushCustomThemeColors();
            }
            // Check for seasonal themes
            else if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration) &&
                     SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration) == SeasonalTheme.Halloween)
            {
                // Halloween themed styling with dark gradient background
                ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.04f, 0.02f, 0.98f)); // Dark orange-brown
                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.10f, 0.05f, 0.08f, 0.95f)); // Dark purple-black
                ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.08f, 0.04f, 0.02f, 0.98f)); // Dark orange-brown
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.15f, 0.08f, 0.04f, 0.9f)); // Dark orange frames
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.20f, 0.12f, 0.06f, 0.9f)); // Lighter orange hover
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.25f, 0.15f, 0.08f, 0.9f)); // Active orange
                ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.06f, 0.03f, 0.02f, 1.0f)); // Very dark orange
                ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.08f, 0.04f, 0.02f, 1.0f)); // Dark orange active
                ImGui.PushStyleColor(ImGuiCol.MenuBarBg, new Vector4(0.08f, 0.04f, 0.02f, 0.98f)); // Dark orange menu
                ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.06f, 0.03f, 0.02f, 0.8f)); // Dark scrollbar
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.3f, 0.15f, 0.08f, 0.8f)); // Orange scrollbar grab
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.4f, 0.20f, 0.10f, 0.9f)); // Hover orange
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(0.5f, 0.25f, 0.12f, 1.0f)); // Active orange
                ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.35f, 0.18f, 0.09f, 0.6f)); // Orange separator
                ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, new Vector4(0.45f, 0.23f, 0.11f, 0.8f)); // Hover separator
                ImGui.PushStyleColor(ImGuiCol.SeparatorActive, new Vector4(0.55f, 0.28f, 0.14f, 1.0f)); // Active separator
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.87f, 0.70f, 1.0f)); // Warm white text
                ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(0.6f, 0.45f, 0.35f, 0.8f)); // Warm gray disabled
                
                // Halloween button styling
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.10f, 0.05f, 0.9f)); // Dark orange buttons
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.15f, 0.08f, 0.9f)); // Hover orange
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.40f, 0.20f, 0.10f, 0.9f)); // Active orange
            }
            else if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration) &&
                     SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration) == SeasonalTheme.Winter)
            {
                // Winter themed styling with bright icy blue/white theme
                ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.12f, 0.16f, 0.22f, 0.98f)); // Bright cool blue
                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.20f, 0.28f, 0.95f)); // Lighter cool blue
                ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.12f, 0.16f, 0.22f, 0.98f)); // Bright cool blue
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.20f, 0.25f, 0.35f, 0.9f)); // Bright blue frames
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.25f, 0.32f, 0.45f, 0.9f)); // Lighter blue hover
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.30f, 0.40f, 0.55f, 0.9f)); // Active bright blue
                ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.08f, 0.12f, 0.18f, 1.0f)); // Medium blue
                ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.12f, 0.16f, 0.22f, 1.0f)); // Bright blue active
                ImGui.PushStyleColor(ImGuiCol.MenuBarBg, new Vector4(0.12f, 0.16f, 0.22f, 0.98f)); // Bright blue menu
                ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.08f, 0.12f, 0.18f, 0.8f)); // Medium scrollbar
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.30f, 0.40f, 0.55f, 0.8f)); // Bright blue scrollbar
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.40f, 0.50f, 0.70f, 0.9f)); // Hover bright blue
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(0.50f, 0.65f, 0.85f, 1.0f)); // Active very bright blue
                ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.35f, 0.45f, 0.60f, 0.6f)); // Bright blue separator
                ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, new Vector4(0.45f, 0.55f, 0.75f, 0.8f)); // Hover separator
                ImGui.PushStyleColor(ImGuiCol.SeparatorActive, new Vector4(0.55f, 0.70f, 0.90f, 1.0f)); // Active separator
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.98f, 1.0f, 1.0f)); // Bright white text
                ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(0.60f, 0.70f, 0.85f, 0.8f)); // Cool light gray disabled
                
                // Winter button styling
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.30f, 0.45f, 0.9f)); // Bright blue buttons
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.40f, 0.60f, 0.9f)); // Hover bright blue
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.40f, 0.55f, 0.75f, 0.9f)); // Active very bright blue
            }
            else if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration) &&
                     SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration) == SeasonalTheme.Christmas)
            {
                // Christmas themed styling with vibrant saturated red/green theme
                ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.25f, 0.05f, 0.05f, 0.98f)); // Vibrant saturated red
                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.30f, 0.08f, 0.05f, 0.95f)); // Saturated red-brown
                ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.25f, 0.05f, 0.05f, 0.98f)); // Vibrant saturated red
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.40f, 0.12f, 0.08f, 0.9f)); // Vibrant red frames
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.50f, 0.18f, 0.12f, 0.9f)); // Saturated red hover
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.65f, 0.22f, 0.15f, 0.9f)); // Active saturated red
                ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.18f, 0.03f, 0.03f, 1.0f)); // Deep saturated red
                ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.25f, 0.05f, 0.05f, 1.0f)); // Saturated red active
                ImGui.PushStyleColor(ImGuiCol.MenuBarBg, new Vector4(0.25f, 0.05f, 0.05f, 0.98f)); // Saturated red menu
                ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.18f, 0.03f, 0.03f, 0.8f)); // Deep scrollbar
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.60f, 0.20f, 0.15f, 0.8f)); // Saturated red scrollbar
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.75f, 0.25f, 0.18f, 0.9f)); // Hover saturated red
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(0.90f, 0.30f, 0.22f, 1.0f)); // Active very saturated red
                ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.70f, 0.25f, 0.18f, 0.6f)); // Saturated red separator
                ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, new Vector4(0.80f, 0.30f, 0.22f, 0.8f)); // Hover separator
                ImGui.PushStyleColor(ImGuiCol.SeparatorActive, new Vector4(0.95f, 0.35f, 0.25f, 1.0f)); // Active separator
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.98f, 0.95f, 1.0f)); // Bright warm white text
                ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(0.80f, 0.70f, 0.60f, 0.8f)); // Warm light gray disabled

                // Christmas button styling
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.60f, 0.18f, 0.12f, 0.9f)); // Saturated red buttons
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.75f, 0.25f, 0.18f, 0.9f)); // Hover saturated red
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.90f, 0.32f, 0.22f, 0.9f)); // Active very saturated red
            }
            else if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration) &&
                     SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration) == SeasonalTheme.Valentines)
            {
                // Valentine's Day themed styling with vibrant pink/red theme
                ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.38f, 0.10f, 0.25f, 0.98f)); // More pink
                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.42f, 0.12f, 0.28f, 0.95f)); // Deeper pink
                ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.38f, 0.10f, 0.25f, 0.98f)); // More pink
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.38f, 0.06f, 0.18f, 0.9f)); // Vibrant pink frames
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.52f, 0.08f, 0.26f, 0.9f)); // Brighter pink hover
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.65f, 0.10f, 0.32f, 0.9f)); // Active vivid pink
                ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.18f, 0.02f, 0.09f, 1.0f)); // Deep rose
                ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.28f, 0.03f, 0.14f, 1.0f)); // Rich rose active
                ImGui.PushStyleColor(ImGuiCol.MenuBarBg, new Vector4(0.22f, 0.03f, 0.12f, 0.98f)); // Rich rose menu
                ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.18f, 0.02f, 0.09f, 0.8f)); // Deep scrollbar
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.70f, 0.10f, 0.35f, 0.85f)); // Vivid pink scrollbar
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.85f, 0.15f, 0.42f, 0.95f)); // Brighter pink hover
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(1.0f, 0.20f, 0.50f, 1.0f)); // Hot pink active
                ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.80f, 0.12f, 0.40f, 0.7f)); // Vivid pink separator
                ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, new Vector4(0.95f, 0.18f, 0.48f, 0.85f)); // Brighter separator
                ImGui.PushStyleColor(ImGuiCol.SeparatorActive, new Vector4(1.0f, 0.25f, 0.55f, 1.0f)); // Hot pink separator
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.95f, 0.97f, 1.0f)); // Soft white-pink text
                ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(0.75f, 0.50f, 0.58f, 0.8f)); // Muted pink disabled

                // Valentine's button styling - more vibrant
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.60f, 0.08f, 0.30f, 0.9f)); // Vivid pink buttons
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.78f, 0.12f, 0.40f, 0.95f)); // Bright pink hover
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.95f, 0.18f, 0.48f, 1.0f)); // Hot pink active
            }
            else
            {
                // Default matte black styling
                ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.06f, 0.06f, 0.06f, 0.98f));
                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.08f, 0.95f));
                ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.06f, 0.06f, 0.06f, 0.98f));
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.12f, 0.12f, 0.12f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.18f, 0.18f, 0.18f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.22f, 0.22f, 0.22f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.04f, 0.04f, 0.04f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.06f, 0.06f, 0.06f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.MenuBarBg, new Vector4(0.06f, 0.06f, 0.06f, 0.98f));
                ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.04f, 0.04f, 0.04f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.3f, 0.3f, 0.3f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.25f, 0.25f, 0.25f, 0.6f));
                ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, new Vector4(0.35f, 0.35f, 0.35f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.SeparatorActive, new Vector4(0.45f, 0.45f, 0.45f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.92f, 0.92f, 0.92f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(0.5f, 0.5f, 0.5f, 0.8f));
                
                // Default button styling
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.16f, 0.16f, 0.16f, 0.9f)); // Default gray buttons
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.22f, 0.22f, 0.22f, 0.9f)); // Hover gray
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.28f, 0.28f, 0.28f, 0.9f)); // Active gray
            }

            // Styling variables for polish
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8 * scale, 4 * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8 * scale, 6 * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f); // Boutique: no rounding so tooltips stay sharp-cornered
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8.0f * scale);   // Scale child rounding
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f * scale);   // Scale frame rounding
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 8.0f * scale); // Scale scrollbar rounding
            ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 6.0f * scale);    // Scale grab rounding
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1.0f * scale); // Scale borders
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0.5f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0.5f * scale);

            // ColorOptions push one slot each, plus the manual SeparatorHovered push.
            // Compute dynamically so editor option additions/removals don't drift the count.
            colorStackCount += CustomThemeDefinitions.ColorOptions.Length + 1;
            styleStackCount += 10;

        }

        public void PopMainWindowStyle()
        {
            ImGui.PopStyleVar(styleStackCount);
            ImGui.PopStyleColor(colorStackCount);
            styleStackCount = 0;
            colorStackCount = 0;
        }

        /// <summary>
        /// Pushes custom theme colors from CustomThemeDefinitions.
        /// Uses user overrides where available, otherwise falls back to defaults.
        /// </summary>
        private void PushCustomThemeColors()
        {
            var customTheme = plugin.Configuration.CustomTheme;
            int pushedColors = 0;

            // Push all ImGui colors from CustomThemeDefinitions
            foreach (var option in CustomThemeDefinitions.ColorOptions)
            {
                Vector4 color;
                if (customTheme.ColorOverrides.TryGetValue(option.Key, out var packed) && packed.HasValue)
                {
                    color = CustomThemeDefinitions.UnpackColor(packed.Value);
                }
                else
                {
                    color = option.DefaultValue;
                }

                ImGui.PushStyleColor(option.Target, color);
                pushedColors++;
            }

            // Push additional separator colors to match seasonal theme count (21 total)
            // These use defaults since they're not in the customizable options
            ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, new Vector4(0.35f, 0.35f, 0.35f, 0.8f));

            // Note: colorStackCount is incremented in PushMainWindowStyle() after the if/else block
            // to keep consistent handling across all theme types (line 156: colorStackCount += 21)
        }

        public void PushCharacterCardStyle(Vector3 glowColor, bool isHovered = false, float scale = 1.0f)
        {
            // Use GlobalScale combined with any additional scaling
            float finalScale = ImGuiHelpers.GlobalScale * scale;
            
            // Dark card background with subtle transparency
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));

            // Rounded corners
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 12.0f * finalScale);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, (isHovered ? 2.0f : 1.0f) * finalScale);

            colorStackCount++;
            styleStackCount += 2;
        }

        public void PopCharacterCardStyle()
        {
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(1);
            styleStackCount -= 2;
            colorStackCount--;
        }

        public void DrawGlowingBorder(Vector2 min, Vector2 max, Vector3 color, float intensity = 1.0f, bool isHovered = false, float scale = 1.0f)
        {
            var drawList = ImGui.GetWindowDrawList();
            float finalScale = ImGuiHelpers.GlobalScale * scale;

            // Convert colour to ImGui format
            var glowColor = new Vector4(color.X, color.Y, color.Z, intensity);
            uint glowColorU32 = ImGui.GetColorU32(glowColor);

            // Draw multiple borders for glow effect - scale thickness and radius
            float thickness = (isHovered ? 2.0f : 1.5f) * finalScale;
            float cornerRadius = 12.0f * finalScale;

            // Outer glow
            for (int i = 0; i < 5; i++)
            {
                float alpha = (0.4f - i * 0.08f) * intensity;
                if (alpha <= 0) break;

                uint outerColor = ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, alpha));
                float offset = (i + 1) * 1.5f * finalScale;

                drawList.AddRect(
                    min - new Vector2(offset, offset),
                    max + new Vector2(offset, offset),
                    outerColor,
                    cornerRadius + offset,
                    ImDrawFlags.RoundCornersAll,
                    1.0f * finalScale
                );
            }

            // Inner bright border
            if (isHovered)
            {
                uint brightColor = ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, intensity * 0.8f));
                drawList.AddRect(
                    min + new Vector2(1 * finalScale, 1 * finalScale),
                    max - new Vector2(1 * finalScale, 1 * finalScale),
                    brightColor,
                    cornerRadius - (1 * finalScale),
                    ImDrawFlags.RoundCornersAll,
                    1.0f * finalScale
                );
            }

            // Main border
            drawList.AddRect(min, max, glowColorU32, cornerRadius, ImDrawFlags.RoundCornersAll, thickness);
        }

        public void PushDarkButtonStyle(float scale = 1.0f)
        {
            float finalScale = ImGuiHelpers.GlobalScale * scale;
            
            // Dark button styling
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.3f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));

            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f * finalScale); // Scale button rounding

            colorStackCount += 4;
            styleStackCount += 2;
        }

        public void PopDarkButtonStyle()
        {
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(4);
            styleStackCount -= 2;
            colorStackCount -= 4;
        }

        public bool IconButton(string icon, string tooltip, Vector2? size = null, float scale = 1.0f)
        {
            return IconButtonWithColor(icon, tooltip, size, scale, null);
        }

        public bool IconButtonWithColor(string icon, string tooltip, Vector2? size = null, float scale = 1.0f, Vector4? iconColor = null)
        {
            float finalScale = ImGuiHelpers.GlobalScale * scale;

            // Calculate icon size and visible-glyph bounds. CalcTextSize returns the
            // advance width, which can be wider than the visible glyph for some
            // FontAwesome icons (notably the trophy \uf091), leaving extra space on
            // the right and visually pushing the glyph to the left when we centre by
            // advance width alone. Pull the per-glyph X0/X1 so we can centre on the
            // actual visible bounds instead.
            float visibleX0 = 0f;
            float visibleWidth;
            Vector2 iconSize;
            ImGui.PushFont(UiBuilder.IconFont);
            iconSize = ImGui.CalcTextSize(icon);
            if (!string.IsNullOrEmpty(icon))
            {
                visibleWidth = iconSize.X;
                try
                {
                    unsafe
                    {
                        var glyphPtr = ImGui.GetFont().FindGlyph(icon[0]);
                        if (glyphPtr != null)
                        {
                            visibleX0 = glyphPtr->X0;
                            visibleWidth = glyphPtr->X1 - glyphPtr->X0;
                        }
                    }
                }
                catch
                {
                    // Fallback if the glyph API isn't available or the glyph isn't found
                    visibleWidth = iconSize.X;
                }
            }
            else
            {
                visibleWidth = iconSize.X;
            }
            ImGui.PopFont();

            // Determine button size
            Vector2 buttonSize;
            if (size.HasValue)
            {
                buttonSize = new Vector2(size.Value.X * finalScale, size.Value.Y * finalScale);
            }
            else
            {
                // Default: icon size + padding
                var padding = ImGui.GetStyle().FramePadding;
                buttonSize = new Vector2(iconSize.X + padding.X * 2, iconSize.Y + padding.Y * 2);
            }

            // Get button position before creating it
            var buttonPos = ImGui.GetCursorScreenPos();

            // Create invisible button for interaction
            var buttonId = $"##iconbtn_{icon}_{buttonPos.X}_{buttonPos.Y}";
            bool result = ImGui.InvisibleButton(buttonId, buttonSize);
            bool isHovered = ImGui.IsItemHovered();
            bool isActive = ImGui.IsItemActive();

            // Draw button background
            var drawList = ImGui.GetWindowDrawList();
            var buttonEnd = buttonPos + buttonSize;

            Vector4 bgColor;
            if (isActive)
                bgColor = ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive];
            else if (isHovered)
                bgColor = ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonHovered];
            else
                bgColor = ImGui.GetStyle().Colors[(int)ImGuiCol.Button];

            drawList.AddRectFilled(buttonPos, buttonEnd, ImGui.ColorConvertFloat4ToU32(bgColor), ImGui.GetStyle().FrameRounding);

            // Centre horizontally on the visible glyph bounds (accounts for glyphs
            // whose advance width exceeds their visible width); Y-centre on the
            // advance-width cell (FontAwesome has no descent so cell-centring is fine).
            // AddText draws the glyph starting at (pos.X + glyph.X0), so we subtract
            // visibleX0 from the target X to line the visible glyph's left edge up
            // with where we want it.
            float targetVisibleLeftX = buttonPos.X + (buttonSize.X - visibleWidth) * 0.5f;
            var iconPos = new Vector2(
                targetVisibleLeftX - visibleX0,
                buttonPos.Y + (buttonSize.Y - iconSize.Y) * 0.5f);
            var textColor = iconColor ?? ImGui.GetStyle().Colors[(int)ImGuiCol.Text];

            ImGui.PushFont(UiBuilder.IconFont);
            drawList.AddText(iconPos, ImGui.ColorConvertFloat4ToU32(textColor), icon);
            ImGui.PopFont();

            // Hover sheen sweep on hover-enter (plays once, then waits for un-hover + re-hover)
            float sheen = UpdateAndGetHoverSweepProgress(buttonId, isHovered);
            if (sheen >= 0f)
                DrawHoverSheen(drawList, buttonPos, buttonEnd, sheen, maxAlpha: 0.22f);

            if (isHovered && !string.IsNullOrEmpty(tooltip))
            {
                ImGui.SetTooltip(tooltip);
            }

            return result;
        }

        public void DrawGradientBackground(Vector2 min, Vector2 max, Vector4 topColor, Vector4 bottomColor)
        {
            var drawList = ImGui.GetWindowDrawList();

            uint topColorU32 = ImGui.GetColorU32(topColor);
            uint bottomColorU32 = ImGui.GetColorU32(bottomColor);

            drawList.AddRectFilledMultiColor(
                min, max,
                topColorU32, topColorU32,
                bottomColorU32, bottomColorU32
            );
        }

        public void PushNameplateStyle(float scale = 1.0f)
        {
            // Nameplate styling with transparency
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0.85f));
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0.0f); // Nameplates typically don't have rounding
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0.0f);

            colorStackCount++;
            styleStackCount += 2;
        }

        public void PopNameplateStyle()
        {
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(1);
            styleStackCount -= 2;
            colorStackCount--;
        }

        public void DrawPaginationDots(int currentPage, int totalPages, Vector2 position, float scale = 1.0f)
        {
            if (totalPages <= 1) return;

            var drawList = ImGui.GetWindowDrawList();
            float finalScale = ImGuiHelpers.GlobalScale * scale;
            float dotSize = 8.0f * finalScale; 
            float spacing = 16.0f * finalScale; 

            for (int i = 0; i < totalPages; i++)
            {
                Vector2 dotPos = position + new Vector2(i * spacing, 0);
                uint color = i == currentPage
                    ? ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 1.0f))
                    : ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 0.7f));

                drawList.AddCircleFilled(dotPos, dotSize / 2, color);

                // Glow effect for active dot
                if (i == currentPage)
                {
                    drawList.AddCircle(dotPos, dotSize / 2 + (2 * finalScale),
                        ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.5f)), 0, 1.0f * finalScale);
                }
            }
        }

        public void PushFormStyle()
        {
            float scale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;

            // Form-specific styling
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.16f, 0.16f, 0.16f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.22f, 0.22f, 0.22f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.28f, 0.28f, 0.28f, 0.9f));

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f * ImGuiHelpers.GlobalScale);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6 * scale, 4 * scale));

            colorStackCount += 3;
            styleStackCount += 2;
        }

        public void PopFormStyle()
        {
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(3);
            styleStackCount -= 2;
            colorStackCount -= 3;
        }
    }

    public static class SeStringExtensions
    {
        public static SeStringBuilder AddColored(this SeStringBuilder builder, string text, ushort colorId, bool bold = false)
        {
            builder.AddUiForeground(colorId);
            if (bold) builder.Add(RawPayload.LinkTerminator);
            builder.AddText(text);
            if (bold) builder.Add(RawPayload.LinkTerminator);
            builder.AddUiForegroundOff();
            return builder;
        }

        public static SeStringBuilder AddRed(this SeStringBuilder builder, string text, bool bold = false)
            => builder.AddColored(text, 14, bold); // Red color

        public static SeStringBuilder AddBlue(this SeStringBuilder builder, string text, bool bold = false)
            => builder.AddColored(text, 37, bold); // Blue color

        public static SeStringBuilder AddYellow(this SeStringBuilder builder, string text, bool bold = false)
            => builder.AddColored(text, 31, bold); // Yellow color

        public static SeStringBuilder AddGreen(this SeStringBuilder builder, string text, bool bold = false)
            => builder.AddColored(text, 43, bold); // Green color

        public static SeStringBuilder AddPurple(this SeStringBuilder builder, string text, bool bold = false)
            => builder.AddColored(text, 541, bold); // Purple color

        public static SeStringBuilder AddOrange(this SeStringBuilder builder, string text, bool bold = false)
            => builder.AddColored(text, 500, bold); // Orange color

        public static SeStringBuilder AddWhite(this SeStringBuilder builder, string text, bool bold = false)
            => builder.AddColored(text, 1, bold); // White color
    }

    /// <summary>
    /// Static helper methods for applying theme colors to secondary windows.
    /// These methods can be called without a UIStyles instance.
    /// </summary>
    public static class ThemeHelper
    {
        /// <summary>Pushes theme colours for secondary windows. Pair with PopThemeColors.</summary>
        public static int PushThemeColors(Configuration config)
        {
            if (config.SelectedTheme == ThemeSelection.Custom)
            {
                return PushCustomThemeColorsStatic(config);
            }
            else if (SeasonalThemeManager.IsSeasonalThemeEnabled(config) &&
                     SeasonalThemeManager.GetEffectiveTheme(config) == SeasonalTheme.Halloween)
            {
                return PushHalloweenColors();
            }
            else if (SeasonalThemeManager.IsSeasonalThemeEnabled(config) &&
                     SeasonalThemeManager.GetEffectiveTheme(config) == SeasonalTheme.Winter)
            {
                return PushWinterColors();
            }
            else if (SeasonalThemeManager.IsSeasonalThemeEnabled(config) &&
                     SeasonalThemeManager.GetEffectiveTheme(config) == SeasonalTheme.Christmas)
            {
                return PushChristmasColors();
            }
            else if (SeasonalThemeManager.IsSeasonalThemeEnabled(config) &&
                     SeasonalThemeManager.GetEffectiveTheme(config) == SeasonalTheme.Valentines)
            {
                return PushValentinesColors();
            }
            else
            {
                return PushDefaultColors();
            }
        }

        /// <summary>
        /// Pops theme colors pushed by PushThemeColors.
        /// </summary>
        /// <param name="count">The count returned by PushThemeColors</param>
        public static void PopThemeColors(int count)
        {
            if (count > 0)
            {
                ImGui.PopStyleColor(count);
            }
        }

        /// <summary>
        /// Pushes default theme colors, ignoring current theme selection.
        /// Use for windows that should always have consistent appearance.
        /// </summary>
        /// <returns>Number of colors pushed (use for PopStyleColor)</returns>
        public static int PushDefaultThemeColors()
        {
            return PushDefaultColors();
        }

        /// <summary>
        /// Pushes chrome colours (WindowBg, TitleBg, TitleBgActive, MenuBarBg)
        /// for the active theme. Must be called from PreDraw, before ImGui.Begin,
        /// so the title bar picks them up. Pair with PopWindowChromeColors in PostDraw.
        /// </summary>
        public static int PushWindowChromeColors(Configuration config)
        {
            Vector4 windowBg, titleBg, titleBgActive, menuBarBg;
            ResolveChromeColors(config, out windowBg, out titleBg, out titleBgActive, out menuBarBg);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, windowBg);
            ImGui.PushStyleColor(ImGuiCol.TitleBg, titleBg);
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, titleBgActive);
            ImGui.PushStyleColor(ImGuiCol.MenuBarBg, menuBarBg);
            return 4;
        }

        /// <summary>Counterpart to PushWindowChromeColors. Call in PostDraw.</summary>
        public static void PopWindowChromeColors(int count)
        {
            if (count > 0) ImGui.PopStyleColor(count);
        }

        private static void ResolveChromeColors(Configuration config,
            out Vector4 windowBg, out Vector4 titleBg, out Vector4 titleBgActive, out Vector4 menuBarBg)
        {
            if (config.SelectedTheme == ThemeSelection.Custom)
            {
                var custom = config.CustomTheme;
                windowBg      = LookupCustom(custom, ImGuiCol.WindowBg,      new Vector4(0.05f, 0.05f, 0.05f, 0.98f));
                titleBg       = LookupCustom(custom, ImGuiCol.TitleBg,       new Vector4(0.04f, 0.04f, 0.04f, 1.0f));
                titleBgActive = LookupCustom(custom, ImGuiCol.TitleBgActive, new Vector4(0.06f, 0.06f, 0.06f, 1.0f));
                menuBarBg     = LookupCustom(custom, ImGuiCol.MenuBarBg,     new Vector4(0.05f, 0.05f, 0.05f, 0.98f));
                return;
            }
            if (SeasonalThemeManager.IsSeasonalThemeEnabled(config))
            {
                switch (SeasonalThemeManager.GetEffectiveTheme(config))
                {
                    case SeasonalTheme.Halloween:
                        windowBg      = new Vector4(0.08f, 0.04f, 0.02f, 0.98f);
                        titleBg       = new Vector4(0.06f, 0.03f, 0.02f, 1.0f);
                        titleBgActive = new Vector4(0.08f, 0.04f, 0.02f, 1.0f);
                        menuBarBg     = new Vector4(0.08f, 0.04f, 0.02f, 0.98f);
                        return;
                    case SeasonalTheme.Winter:
                        windowBg      = new Vector4(0.12f, 0.16f, 0.22f, 0.98f);
                        titleBg       = new Vector4(0.08f, 0.12f, 0.18f, 1.0f);
                        titleBgActive = new Vector4(0.12f, 0.16f, 0.22f, 1.0f);
                        menuBarBg     = new Vector4(0.12f, 0.16f, 0.22f, 0.98f);
                        return;
                    case SeasonalTheme.Christmas:
                        windowBg      = new Vector4(0.25f, 0.05f, 0.05f, 0.98f);
                        titleBg       = new Vector4(0.20f, 0.04f, 0.04f, 1.0f);
                        titleBgActive = new Vector4(0.25f, 0.05f, 0.05f, 1.0f);
                        menuBarBg     = new Vector4(0.25f, 0.05f, 0.05f, 0.98f);
                        return;
                    case SeasonalTheme.Valentines:
                        windowBg      = new Vector4(0.18f, 0.07f, 0.12f, 0.98f);
                        titleBg       = new Vector4(0.14f, 0.05f, 0.10f, 1.0f);
                        titleBgActive = new Vector4(0.20f, 0.08f, 0.14f, 1.0f);
                        menuBarBg     = new Vector4(0.18f, 0.07f, 0.12f, 0.98f);
                        return;
                }
            }
            // Default theme
            windowBg      = new Vector4(0.05f, 0.05f, 0.05f, 0.98f);
            titleBg       = new Vector4(0.04f, 0.04f, 0.04f, 1.0f);
            titleBgActive = new Vector4(0.06f, 0.06f, 0.06f, 1.0f);
            menuBarBg     = new Vector4(0.05f, 0.05f, 0.05f, 0.98f);
        }

        private static Vector4 LookupCustom(CustomThemeConfig theme, ImGuiCol target, Vector4 fallback)
        {
            foreach (var option in CustomThemeDefinitions.ColorOptions)
            {
                if (option.Target != target) continue;
                if (theme.ColorOverrides.TryGetValue(option.Key, out var packed) && packed.HasValue)
                    return CustomThemeDefinitions.UnpackColor(packed.Value);
                return option.DefaultValue;
            }
            return fallback;
        }

        /// <summary>
        /// Pushes standard style variables for secondary windows.
        /// </summary>
        /// <param name="scale">UI scale multiplier</param>
        /// <returns>Number of style vars pushed (use for PopStyleVar)</returns>
        public static int PushThemeStyleVars(float scale = 1.0f)
        {
            float finalScale = ImGuiHelpers.GlobalScale * scale;

            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8 * finalScale, 4 * finalScale));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8 * finalScale, 6 * finalScale));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8.0f * finalScale);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f * finalScale);
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 8.0f * finalScale);
            ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 6.0f * finalScale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1.0f * finalScale);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0.5f * finalScale);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0.5f * finalScale);

            return 10;
        }

        /// <summary>
        /// Pops style vars pushed by PushThemeStyleVars.
        /// </summary>
        /// <param name="count">The count returned by PushThemeStyleVars</param>
        public static void PopThemeStyleVars(int count)
        {
            if (count > 0)
            {
                ImGui.PopStyleVar(count);
            }
        }

        private static int PushCustomThemeColorsStatic(Configuration config)
        {
            var customTheme = config.CustomTheme;
            int pushedColors = 0;

            // Push all ImGui colors from CustomThemeDefinitions
            foreach (var option in CustomThemeDefinitions.ColorOptions)
            {
                Vector4 color;
                if (customTheme.ColorOverrides.TryGetValue(option.Key, out var packed) && packed.HasValue)
                {
                    color = CustomThemeDefinitions.UnpackColor(packed.Value);
                }
                else
                {
                    color = option.DefaultValue;
                }

                ImGui.PushStyleColor(option.Target, color);
                pushedColors++;
            }

            // Push additional separator colors to match seasonal theme count (21 total)
            ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, new Vector4(0.35f, 0.35f, 0.35f, 0.8f));
            pushedColors++;

            return pushedColors;
        }

        private static int PushHalloweenColors()
        {
            // Halloween themed styling with dark gradient background
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.04f, 0.02f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.10f, 0.05f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.08f, 0.04f, 0.02f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.15f, 0.08f, 0.04f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.20f, 0.12f, 0.06f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.25f, 0.15f, 0.08f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.06f, 0.03f, 0.02f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.08f, 0.04f, 0.02f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.MenuBarBg, new Vector4(0.08f, 0.04f, 0.02f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.06f, 0.03f, 0.02f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.3f, 0.15f, 0.08f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.4f, 0.20f, 0.10f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(0.5f, 0.25f, 0.12f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.35f, 0.18f, 0.09f, 0.6f));
            ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, new Vector4(0.45f, 0.23f, 0.11f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.SeparatorActive, new Vector4(0.55f, 0.28f, 0.14f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.87f, 0.70f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(0.6f, 0.45f, 0.35f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.10f, 0.05f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.15f, 0.08f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.40f, 0.20f, 0.10f, 0.9f));

            return 21;
        }

        private static int PushWinterColors()
        {
            // Winter themed styling with bright icy blue/white theme
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.12f, 0.16f, 0.22f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.20f, 0.28f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.12f, 0.16f, 0.22f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.20f, 0.25f, 0.35f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.25f, 0.32f, 0.45f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.30f, 0.40f, 0.55f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.08f, 0.12f, 0.18f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.12f, 0.16f, 0.22f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.MenuBarBg, new Vector4(0.12f, 0.16f, 0.22f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.08f, 0.12f, 0.18f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.30f, 0.40f, 0.55f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.40f, 0.50f, 0.70f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(0.50f, 0.65f, 0.85f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.35f, 0.45f, 0.60f, 0.6f));
            ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, new Vector4(0.45f, 0.55f, 0.75f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.SeparatorActive, new Vector4(0.55f, 0.70f, 0.90f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.98f, 1.0f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(0.60f, 0.70f, 0.85f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.30f, 0.45f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.40f, 0.60f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.40f, 0.55f, 0.75f, 0.9f));

            return 21;
        }

        private static int PushChristmasColors()
        {
            // Christmas themed styling with vibrant saturated red/green theme
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.25f, 0.05f, 0.05f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.30f, 0.08f, 0.05f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.25f, 0.05f, 0.05f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.40f, 0.12f, 0.08f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.50f, 0.18f, 0.12f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.65f, 0.22f, 0.15f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.18f, 0.03f, 0.03f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.25f, 0.05f, 0.05f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.MenuBarBg, new Vector4(0.25f, 0.05f, 0.05f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.18f, 0.03f, 0.03f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.60f, 0.20f, 0.15f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.75f, 0.25f, 0.18f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(0.90f, 0.30f, 0.22f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.70f, 0.25f, 0.18f, 0.6f));
            ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, new Vector4(0.80f, 0.30f, 0.22f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.SeparatorActive, new Vector4(0.95f, 0.35f, 0.25f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.98f, 0.95f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(0.80f, 0.70f, 0.60f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.60f, 0.18f, 0.12f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.75f, 0.25f, 0.18f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.90f, 0.32f, 0.22f, 0.9f));

            return 21;
        }

        private static int PushValentinesColors()
        {
            // Valentine's Day themed styling with vibrant pink/red theme
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.38f, 0.10f, 0.25f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.42f, 0.12f, 0.28f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.38f, 0.10f, 0.25f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.38f, 0.06f, 0.18f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.52f, 0.08f, 0.26f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.65f, 0.10f, 0.32f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.18f, 0.02f, 0.09f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.28f, 0.03f, 0.14f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.MenuBarBg, new Vector4(0.22f, 0.03f, 0.12f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.18f, 0.02f, 0.09f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.70f, 0.10f, 0.35f, 0.85f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.85f, 0.15f, 0.42f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(1.0f, 0.20f, 0.50f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.80f, 0.12f, 0.40f, 0.7f));
            ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, new Vector4(0.95f, 0.18f, 0.48f, 0.85f));
            ImGui.PushStyleColor(ImGuiCol.SeparatorActive, new Vector4(1.0f, 0.25f, 0.55f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.95f, 0.97f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(0.75f, 0.50f, 0.58f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.60f, 0.08f, 0.30f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.78f, 0.12f, 0.40f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.95f, 0.18f, 0.48f, 1.0f));

            return 21;
        }

        private static int PushDefaultColors()
        {
            // Default matte black styling
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.06f, 0.06f, 0.06f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.06f, 0.06f, 0.06f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.12f, 0.12f, 0.12f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.18f, 0.18f, 0.18f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.22f, 0.22f, 0.22f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.04f, 0.04f, 0.04f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.06f, 0.06f, 0.06f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.MenuBarBg, new Vector4(0.06f, 0.06f, 0.06f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.04f, 0.04f, 0.04f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.3f, 0.3f, 0.3f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.25f, 0.25f, 0.25f, 0.6f));
            ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, new Vector4(0.35f, 0.35f, 0.35f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.SeparatorActive, new Vector4(0.45f, 0.45f, 0.45f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.92f, 0.92f, 0.92f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(0.5f, 0.5f, 0.5f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.16f, 0.16f, 0.16f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.22f, 0.22f, 0.22f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.28f, 0.28f, 0.28f, 0.9f));

            return 21;
        }
    }
}

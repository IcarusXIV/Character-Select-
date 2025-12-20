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

        public UIStyles(Plugin plugin)
        {
            this.plugin = plugin;
        }

        public void PushMainWindowStyle()
        {
            float scale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;

            // Check for seasonal themes
            bool isSeasonalThemed = SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration);
            var effectiveTheme = SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration);
            
            if (isSeasonalThemed && effectiveTheme == SeasonalTheme.Halloween)
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
            else if (isSeasonalThemed && effectiveTheme == SeasonalTheme.Winter)
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
            else if (isSeasonalThemed && effectiveTheme == SeasonalTheme.Christmas)
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
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10.0f * scale); // Scale window rounding
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8.0f * scale);   // Scale child rounding
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f * scale);   // Scale frame rounding
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 8.0f * scale); // Scale scrollbar rounding
            ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 6.0f * scale);    // Scale grab rounding
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1.0f * scale); // Scale borders
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0.5f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0.5f * scale);

            colorStackCount += 21; // Updated count to include button colors
            styleStackCount += 10;

        }

        public void PopMainWindowStyle()
        {
            ImGui.PopStyleVar(styleStackCount);
            ImGui.PopStyleColor(colorStackCount);
            styleStackCount = 0;
            colorStackCount = 0;
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
            ImGui.PushFont(UiBuilder.IconFont);
            float finalScale = ImGuiHelpers.GlobalScale * scale;

            // Scale the button size if provided
            Vector2 buttonSize = size ?? Vector2.Zero;
            if (size.HasValue)
            {
                buttonSize = new Vector2(size.Value.X * finalScale, size.Value.Y * finalScale);
            }

            // Apply icon color if specified
            bool colorPushed = false;
            if (iconColor.HasValue)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, iconColor.Value);
                colorPushed = true;
            }

            bool result = ImGui.Button(icon, buttonSize);
            
            if (colorPushed)
            {
                ImGui.PopStyleColor();
            }
            
            ImGui.PopFont();

            if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(tooltip))
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
}

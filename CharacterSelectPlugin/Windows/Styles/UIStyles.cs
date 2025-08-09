using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

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
            float scale = plugin.Configuration.UIScaleMultiplier;

            // Matte black styling
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

            colorStackCount += 18;
            styleStackCount += 10;

            ImGui.SetWindowFontScale(scale);
        }

        public void PopMainWindowStyle()
        {
            ImGui.SetWindowFontScale(1f);
            ImGui.PopStyleVar(styleStackCount);
            ImGui.PopStyleColor(colorStackCount);
            styleStackCount = 0;
            colorStackCount = 0;
        }

        public void PushCharacterCardStyle(Vector3 glowColor, bool isHovered = false, float scale = 1.0f)
        {
            // Dark card background with subtle transparency
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));

            // Rounded corners
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 12.0f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, (isHovered ? 2.0f : 1.0f) * scale);

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

            // Convert colour to ImGui format
            var glowColor = new Vector4(color.X, color.Y, color.Z, intensity);
            uint glowColorU32 = ImGui.GetColorU32(glowColor);

            // Draw multiple borders for glow effect - scale thickness and radius
            float thickness = (isHovered ? 2.0f : 1.5f) * scale;
            float cornerRadius = 12.0f * scale;

            // Outer glow
            for (int i = 0; i < 5; i++)
            {
                float alpha = (0.4f - i * 0.08f) * intensity;
                if (alpha <= 0) break;

                uint outerColor = ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, alpha));
                float offset = (i + 1) * 1.5f * scale;

                drawList.AddRect(
                    min - new Vector2(offset, offset),
                    max + new Vector2(offset, offset),
                    outerColor,
                    cornerRadius + offset,
                    ImDrawFlags.RoundCornersAll,
                    1.0f * scale
                );
            }

            // Inner bright border
            if (isHovered)
            {
                uint brightColor = ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, intensity * 0.8f));
                drawList.AddRect(
                    min + new Vector2(1 * scale, 1 * scale),
                    max - new Vector2(1 * scale, 1 * scale),
                    brightColor,
                    cornerRadius - (1 * scale),
                    ImDrawFlags.RoundCornersAll,
                    1.0f * scale
                );
            }

            // Main border
            drawList.AddRect(min, max, glowColorU32, cornerRadius, ImDrawFlags.RoundCornersAll, thickness);
        }

        public void PushDarkButtonStyle(float scale = 1.0f)
        {
            // Dark button styling
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.3f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));

            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f * scale); // Scale button rounding

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
            ImGui.PushFont(UiBuilder.IconFont);

            // Scale the button size if provided
            Vector2 buttonSize = size ?? Vector2.Zero;
            if (size.HasValue)
            {
                buttonSize = new Vector2(size.Value.X * scale, size.Value.Y * scale);
            }

            bool result = ImGui.Button(icon, buttonSize);
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
            float dotSize = 8.0f * scale; 
            float spacing = 16.0f * scale; 

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
                    drawList.AddCircle(dotPos, dotSize / 2 + (2 * scale),
                        ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.5f)), 0, 1.0f * scale);
                }
            }
        }

        public void PushFormStyle()
        {
            float scale = plugin.Configuration.UIScaleMultiplier;

            // Form-specific styling
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.16f, 0.16f, 0.16f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.22f, 0.22f, 0.22f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.28f, 0.28f, 0.28f, 0.9f));

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);
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
}

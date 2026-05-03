using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CharacterSelectPlugin.Windows.Styles
{
    /// <summary>
    /// Defines all customizable color options for the Custom theme.
    /// Only includes colors that are actually pushed by the Default theme.
    /// </summary>
    public static class CustomThemeDefinitions
    {
        #region Option Records

        /// <summary>
        /// ImGui color option that maps to an ImGuiCol target.
        /// </summary>
        public readonly record struct ColorOption(
            string Key,
            string Label,
            string Category,
            Vector4 DefaultValue,
            ImGuiCol Target,
            string? Description = null
        );

        /// <summary>
        /// Custom color option for plugin-specific colors (not ImGui colors).
        /// </summary>
        public readonly record struct CustomColorOption(
            string Key,
            string Label,
            string Category,
            Vector4 DefaultValue,
            string? Description = null
        );

        #endregion

        #region Color Options

        /// <summary>
        /// ImGui colors that the Default theme pushes in UIStyles.PushMainWindowStyle().
        /// 20 colors here + 1 SeparatorHovered added manually in PushCustomThemeColors = 21 total (matches Default).
        /// </summary>
        public static readonly ColorOption[] ColorOptions = new[]
        {
            // === BACKGROUNDS ===
            new ColorOption(
                "color.windowBg",
                "Window Frame",
                "Backgrounds",
                new Vector4(0.06f, 0.06f, 0.06f, 0.98f),
                ImGuiCol.WindowBg,
                "Outer window frame/edge area behind panels"
            ),
            new ColorOption(
                "color.popupBg",
                "Popup/Tooltip",
                "Backgrounds",
                new Vector4(0.06f, 0.06f, 0.06f, 0.98f),
                ImGuiCol.PopupBg,
                "Background for popups, dropdowns, and tooltips"
            ),
            // Hidden but kept in the slot count so PushCustomThemeColors stays
            // in lock-step with the seasonal/default theme branches (each of
            // which pushes 21 colours). These slots are mostly inert in the
            // boutique UI because every visible surface paints itself, but
            // removing them entirely would unbalance the colour stack pop.
            new ColorOption(
                "color.childBg",
                "Sub-panel Background",
                "Backgrounds",
                new Vector4(0.08f, 0.08f, 0.08f, 0.95f),
                ImGuiCol.ChildBg,
                "Inner sub-panel background. Mostly inert: the boutique UI paints custom backgrounds over BeginChild surfaces."
            ),
            new ColorOption(
                "color.menuBarBg",
                "Menu Bar",
                "Title Bar",
                new Vector4(0.06f, 0.06f, 0.06f, 0.98f),
                ImGuiCol.MenuBarBg,
                "Background for menu bar areas. Mostly inert: most boutique windows have no menu bar."
            ),
            new ColorOption(
                "color.frameBg",
                "Input Fields",
                "Backgrounds",
                new Vector4(0.12f, 0.12f, 0.12f, 0.9f),
                ImGuiCol.FrameBg,
                "Background for text inputs, checkboxes, sliders"
            ),
            new ColorOption(
                "color.frameBgHovered",
                "Input Fields (Hover)",
                "Backgrounds",
                new Vector4(0.18f, 0.18f, 0.18f, 0.9f),
                ImGuiCol.FrameBgHovered
            ),
            new ColorOption(
                "color.frameBgActive",
                "Input Fields (Active)",
                "Backgrounds",
                new Vector4(0.22f, 0.22f, 0.22f, 0.9f),
                ImGuiCol.FrameBgActive
            ),

            // === TITLE BAR ===
            new ColorOption(
                "color.titleBg",
                "Title Bar (Inactive)",
                "Title Bar",
                new Vector4(0.04f, 0.04f, 0.04f, 1.0f),
                ImGuiCol.TitleBg,
                "Window title bar when not focused"
            ),
            new ColorOption(
                "color.titleBgActive",
                "Title Bar (Active)",
                "Title Bar",
                new Vector4(0.06f, 0.06f, 0.06f, 1.0f),
                ImGuiCol.TitleBgActive,
                "Window title bar when focused"
            ),

            // === TEXT ===
            new ColorOption(
                "color.text",
                "Primary Text",
                "Text",
                new Vector4(0.92f, 0.92f, 0.92f, 1.0f),
                ImGuiCol.Text,
                "Main text color throughout the UI"
            ),
            new ColorOption(
                "color.textDisabled",
                "Secondary/Disabled Text",
                "Text",
                new Vector4(0.5f, 0.5f, 0.5f, 0.8f),
                ImGuiCol.TextDisabled,
                "Muted text for hints and disabled items"
            ),

            // === BUTTONS ===
            new ColorOption(
                "color.button",
                "Button",
                "Buttons",
                new Vector4(0.16f, 0.16f, 0.16f, 0.9f),
                ImGuiCol.Button
            ),
            new ColorOption(
                "color.buttonHovered",
                "Button (Hover)",
                "Buttons",
                new Vector4(0.22f, 0.22f, 0.22f, 0.9f),
                ImGuiCol.ButtonHovered
            ),
            new ColorOption(
                "color.buttonActive",
                "Button (Pressed)",
                "Buttons",
                new Vector4(0.28f, 0.28f, 0.28f, 0.9f),
                ImGuiCol.ButtonActive
            ),

            // === SCROLLBAR ===
            new ColorOption(
                "color.scrollbarBg",
                "Scrollbar Track",
                "Scrollbar",
                new Vector4(0.04f, 0.04f, 0.04f, 0.8f),
                ImGuiCol.ScrollbarBg
            ),
            new ColorOption(
                "color.scrollbarGrab",
                "Scrollbar Handle",
                "Scrollbar",
                new Vector4(0.2f, 0.2f, 0.2f, 0.8f),
                ImGuiCol.ScrollbarGrab
            ),
            new ColorOption(
                "color.scrollbarGrabHovered",
                "Scrollbar Handle (Hover)",
                "Scrollbar",
                new Vector4(0.3f, 0.3f, 0.3f, 0.9f),
                ImGuiCol.ScrollbarGrabHovered
            ),
            new ColorOption(
                "color.scrollbarGrabActive",
                "Scrollbar Handle (Drag)",
                "Scrollbar",
                new Vector4(0.4f, 0.4f, 0.4f, 1.0f),
                ImGuiCol.ScrollbarGrabActive
            ),

            // === SEPARATORS ===
            new ColorOption(
                "color.separator",
                "Separator Lines",
                "Separators",
                new Vector4(0.25f, 0.25f, 0.25f, 0.6f),
                ImGuiCol.Separator,
                "Horizontal/vertical divider lines"
            ),
            new ColorOption(
                "color.separatorActive",
                "Separator (Active)",
                "Separators",
                new Vector4(0.45f, 0.45f, 0.45f, 1.0f),
                ImGuiCol.SeparatorActive
            ),
        };

        /// <summary>
        /// Custom plugin-specific colors (not ImGui colors). Per-element keys
        /// for things that don't map cleanly onto an ImGui slot. Slot-driven
        /// recolouring (window frame, popup, input fields, buttons, separators,
        /// scrollbar, text) is handled via the ColorOptions array above.
        /// </summary>
        public static readonly CustomColorOption[] CustomColorOptions = new[]
        {
            // === BACKGROUNDS (specifics that don't ride on an ImGui slot) ===
            new CustomColorOption(
                "custom.designPanelBg",
                "Design Panel",
                "Backgrounds",
                new Vector4(0.08f, 0.08f, 0.10f, 0.98f),
                "Background of the entire Design Panel on the right side of the main window."
            ),

            // === TEXT (third tier, richer than Primary/Secondary) ===
            new CustomColorOption(
                "custom.text.subtle",
                "Subtle Text",
                "Text",
                new Vector4(141f / 255f, 147f / 255f, 162f / 255f, 1f),
                "Smaller descriptive text under labels (kickers, captions, the second line on rows)."
            ),
            new CustomColorOption(
                "custom.text.faint",
                "Faint Text",
                "Text",
                new Vector4(91f / 255f, 97f / 255f, 116f / 255f, 1f),
                "Faint third-tier text (meta info, hints, ghosted labels)."
            ),

            // === BUTTONS (icon + label inside primary buttons) ===
            new CustomColorOption(
                "custom.button.icon",
                "Button Icon",
                "Buttons",
                new Vector4(26f / 255f, 21f / 255f, 0f, 1f),
                "Icon glyph colour inside primary buttons (e.g. the + on Add Character / New Design, the disk on Save)."
            ),
            new CustomColorOption(
                "custom.button.text",
                "Button Label",
                "Buttons",
                new Vector4(26f / 255f, 21f / 255f, 0f, 1f),
                "Label text colour inside primary buttons (e.g. ADD CHARACTER, SAVE, APPLY)."
            ),

            // === ACCENTS ===
            new CustomColorOption(
                "custom.accent.primary",
                "Accent",
                "Accents",
                new Vector4(1f, 214f / 255f, 0f, 1f),
                "The primary accent colour, used for active states, section heads, gold pills, and highlights."
            ),
            new CustomColorOption(
                "custom.favoriteIcon",
                "Favourite Star",
                "Accents",
                new Vector4(1.0f, 0.85f, 0.0f, 1.0f),
                "Colour of the favourite star icon when active."
            ),
            new CustomColorOption(
                "custom.cardGlow",
                "Character Card Glow",
                "Accents",
                new Vector4(0.4f, 0.6f, 1.0f, 0.6f),
                "Glow effect around selected/hovered character cards."
            ),
            new CustomColorOption(
                "custom.pageButtonActive",
                "Active Page Button",
                "Accents",
                new Vector4(0.4f, 0.6f, 1.0f, 0.8f),
                "Highlight colour for the currently selected page button in the main grid."
            ),

            // === WARDROBE ===
            new CustomColorOption(
                "custom.wardrobeBg",
                "Background",
                "Wardrobe",
                new Vector4(0.05f, 0.05f, 0.09f, 0.98f),
                "Wardrobe window background colour"
            ),
            new CustomColorOption(
                "custom.wardrobeCardBg",
                "Card Background",
                "Wardrobe",
                new Vector4(0.09f, 0.09f, 0.15f, 0.98f),
                "Background fill for design cards (visible behind images without preview)"
            ),
            new CustomColorOption(
                "custom.wardrobeCardBorder",
                "Card Border",
                "Wardrobe",
                new Vector4(0.35f, 0.30f, 0.20f, 0.35f),
                "Border around each design card"
            ),
            new CustomColorOption(
                "custom.wardrobeAccent",
                "Rail & Accent",
                "Wardrobe",
                new Vector4(0.83f, 0.69f, 0.22f, 1f),
                "Colour for the hanger rail, hangers, sparkle particles, perimeter streak, and page buttons"
            ),
            new CustomColorOption(
                "custom.wardrobeNameText",
                "Name Text",
                "Wardrobe",
                new Vector4(1f, 1f, 1f, 0.88f),
                "Design name text colour below cards"
            ),
        };

        #endregion

        #region Helper Methods

        /// <summary>
        /// Get all unique categories from ImGui color options.
        /// </summary>
        public static IEnumerable<string> GetColorCategories()
            => ColorOptions.Select(o => o.Category).Distinct();

        /// <summary>
        /// Get all unique categories from custom color options.
        /// </summary>
        public static IEnumerable<string> GetCustomColorCategories()
            => CustomColorOptions.Select(o => o.Category).Distinct();

        /// <summary>
        /// Get all unique categories from both ImGui and custom color options.
        /// </summary>
        public static IEnumerable<string> GetAllCategories()
            => ColorOptions.Select(o => o.Category)
                .Concat(CustomColorOptions.Select(o => o.Category))
                .Distinct();

        /// <summary>
        /// Get ImGui color options for a specific category.
        /// </summary>
        public static IEnumerable<ColorOption> GetColorOptionsForCategory(string category)
            => ColorOptions.Where(o => o.Category == category);

        /// <summary>
        /// Get custom color options for a specific category.
        /// </summary>
        public static IEnumerable<CustomColorOption> GetCustomColorOptionsForCategory(string category)
            => CustomColorOptions.Where(o => o.Category == category);

        /// <summary>
        /// Pack a Vector4 color into a uint for storage.
        /// </summary>
        public static uint PackColor(Vector4 color)
        {
            byte r = (byte)(Math.Clamp(color.X, 0f, 1f) * 255f);
            byte g = (byte)(Math.Clamp(color.Y, 0f, 1f) * 255f);
            byte b = (byte)(Math.Clamp(color.Z, 0f, 1f) * 255f);
            byte a = (byte)(Math.Clamp(color.W, 0f, 1f) * 255f);
            return (uint)(r | (g << 8) | (b << 16) | (a << 24));
        }

        /// <summary>
        /// Unpack a uint color into a Vector4.
        /// </summary>
        public static Vector4 UnpackColor(uint packed)
        {
            return new Vector4(
                (packed & 0xFF) / 255f,
                ((packed >> 8) & 0xFF) / 255f,
                ((packed >> 16) & 0xFF) / 255f,
                ((packed >> 24) & 0xFF) / 255f
            );
        }

        #endregion
    }
}

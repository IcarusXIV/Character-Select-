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
            // Window
            new ColorOption(
                "color.windowBg",
                "Window Background",
                "Window",
                new Vector4(0.06f, 0.06f, 0.06f, 0.98f),
                ImGuiCol.WindowBg,
                "Main window surface. Panel shades across the plugin are derived from it."
            ),
            new ColorOption(
                "color.popupBg",
                "Popup/Tooltip",
                "Window",
                new Vector4(0.06f, 0.06f, 0.06f, 0.98f),
                ImGuiCol.PopupBg,
                "Background of popups, dropdowns, and tooltips"
            ),
            new ColorOption(
                "color.titleBg",
                "Title Bar (Inactive)",
                "Window",
                new Vector4(0.04f, 0.04f, 0.04f, 1.0f),
                ImGuiCol.TitleBg,
                "Title bar when not focused. Only affects windows that have a title bar."
            ),
            new ColorOption(
                "color.titleBgActive",
                "Title Bar (Active)",
                "Window",
                new Vector4(0.06f, 0.06f, 0.06f, 1.0f),
                ImGuiCol.TitleBgActive,
                "Title bar when focused. Only affects windows that have a title bar."
            ),
            new ColorOption(
                "color.separator",
                "Hairlines",
                "Window",
                new Vector4(0.25f, 0.25f, 0.25f, 0.6f),
                ImGuiCol.Separator,
                "Thin divider lines between sections and rows"
            ),
            new ColorOption(
                "color.separatorActive",
                "Borders",
                "Window",
                new Vector4(0.45f, 0.45f, 0.45f, 1.0f),
                ImGuiCol.SeparatorActive,
                "Window and panel border lines"
            ),

            // Text
            new ColorOption(
                "color.text",
                "Main Text",
                "Text",
                new Vector4(0.92f, 0.92f, 0.92f, 1.0f),
                ImGuiCol.Text,
                "Body text throughout the plugin"
            ),
            new ColorOption(
                "color.textDisabled",
                "Greyed-Out Text",
                "Text",
                new Vector4(0.5f, 0.5f, 0.5f, 0.8f),
                ImGuiCol.TextDisabled,
                "Disabled controls and hints in standard windows"
            ),

            // Input fields
            new ColorOption(
                "color.frameBg",
                "Background",
                "Input Fields",
                new Vector4(0.12f, 0.12f, 0.12f, 0.9f),
                ImGuiCol.FrameBg,
                "Fill of text boxes, checkboxes, and sliders"
            ),
            new ColorOption(
                "color.frameBgHovered",
                "Background (Hover)",
                "Input Fields",
                new Vector4(0.18f, 0.18f, 0.18f, 0.9f),
                ImGuiCol.FrameBgHovered
            ),
            new ColorOption(
                "color.frameBgActive",
                "Background (Active)",
                "Input Fields",
                new Vector4(0.22f, 0.22f, 0.22f, 0.9f),
                ImGuiCol.FrameBgActive
            ),

            // Menu buttons
            new ColorOption(
                "color.button",
                "Background",
                "Menu Buttons",
                new Vector4(0.16f, 0.16f, 0.16f, 0.9f),
                ImGuiCol.Button,
                "Utility buttons like Cancel, Reset, and dropdown entries. The big gold pills are under Action Buttons."
            ),
            new ColorOption(
                "color.buttonHovered",
                "Background (Hover)",
                "Menu Buttons",
                new Vector4(0.22f, 0.22f, 0.22f, 0.9f),
                ImGuiCol.ButtonHovered
            ),
            new ColorOption(
                "color.buttonActive",
                "Background (Pressed)",
                "Menu Buttons",
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
        };

        /// <summary>
        /// Custom plugin-specific colors (not ImGui colors). Per-element keys
        /// for things that don't map cleanly onto an ImGui slot. Slot-driven
        /// recolouring (window frame, popup, input fields, buttons, separators,
        /// scrollbar, text) is handled via the ColorOptions array above.
        /// </summary>
        public static readonly CustomColorOption[] CustomColorOptions = new[]
        {
            // Accent, drawn first in the editor
            new CustomColorOption(
                "custom.accent.primary",
                "Accent",
                "Accent",
                new Vector4(1f, 214f / 255f, 0f, 1f),
                "The signature colour of the whole interface: pill fills, headers, underlines, corner brackets, badges, glows, and the shades derived from them. Set this first, then fine-tune individual sections below."
            ),

            // Text, merges into the Text section
            new CustomColorOption(
                "custom.text.subtle",
                "Labels & Captions",
                "Text",
                new Vector4(141f / 255f, 147f / 255f, 162f / 255f, 1f),
                "Section labels, captions, small headings, and small button labels"
            ),
            new CustomColorOption(
                "custom.text.faint",
                "Hint Text",
                "Text",
                new Vector4(91f / 255f, 97f / 255f, 116f / 255f, 1f),
                "Faint hints, ghosted labels, and inactive tabs"
            ),

            // Input fields, merges into the Input Fields section
            new CustomColorOption(
                "custom.input.text",
                "Text",
                "Input Fields",
                new Vector4(0.92f, 0.92f, 0.92f, 1.0f),
                "Text typed inside input fields. Follows Main Text unless set."
            ),
            new CustomColorOption(
                "custom.input.placeholder",
                "Placeholder",
                "Input Fields",
                new Vector4(91f / 255f, 97f / 255f, 116f / 255f, 1f),
                "The greyed prompt shown in empty input fields, like 'Search characters...'"
            ),

            // Menu buttons, merges into the Menu Buttons section
            new CustomColorOption(
                "custom.button.menu.icon",
                "Icon",
                "Menu Buttons",
                new Vector4(141f / 255f, 147f / 255f, 162f / 255f, 1f),
                "Icon glyphs on the small utility buttons, like the icon row at the top of the main window. Follows Labels & Captions unless set."
            ),

            // Action buttons
            new CustomColorOption(
                "custom.button.bg",
                "Fill",
                "Action Buttons",
                new Vector4(1f, 214f / 255f, 0f, 1f),
                "Fill of the big action pills (ADD CHARACTER, SAVE, APPLY) only. Follows Accent unless set."
            ),
            new CustomColorOption(
                "custom.button.text",
                "Label",
                "Action Buttons",
                new Vector4(26f / 255f, 21f / 255f, 0f, 1f),
                "Label text inside the action pills"
            ),
            new CustomColorOption(
                "custom.button.icon",
                "Icon",
                "Action Buttons",
                new Vector4(26f / 255f, 21f / 255f, 0f, 1f),
                "The + glyph inside the ADD CHARACTER and NEW DESIGN pills"
            ),

            // Character cards
            new CustomColorOption(
                "custom.card.nameText",
                "Character Name",
                "Character Cards",
                new Vector4(0.92f, 0.92f, 0.92f, 1.0f),
                "Character name on the cards. Follows Main Text unless set."
            ),
            new CustomColorOption(
                "custom.card.buttonBg",
                "Button Background",
                "Character Cards",
                new Vector4(0x20 / 255f, 0x24 / 255f, 0x2E / 255f, 1f),
                "Fill behind the DESIGNS, EDIT, and DELETE buttons"
            ),
            new CustomColorOption(
                "custom.card.designsText",
                "Designs Button",
                "Character Cards",
                new Vector4(77f / 255f, 208f / 255f, 225f / 255f, 1f),
                "Label of the DESIGNS button on character cards"
            ),
            new CustomColorOption(
                "custom.card.editText",
                "Edit Button",
                "Character Cards",
                new Vector4(232f / 255f, 234f / 255f, 240f / 255f, 1f),
                "Label of the EDIT button on character cards"
            ),
            new CustomColorOption(
                "custom.card.deleteText",
                "Delete Button",
                "Character Cards",
                new Vector4(239f / 255f, 68f / 255f, 68f / 255f, 1f),
                "Label of the DELETE button on character cards"
            ),
            new CustomColorOption(
                "custom.favoriteIcon",
                "Favourite Icon",
                "Character Cards",
                new Vector4(1.0f, 0.85f, 0.0f, 1.0f),
                "The favourite star on character cards. Independent of Accent."
            ),
            new CustomColorOption(
                "custom.cardGlow",
                "Card Glow",
                "Character Cards",
                new Vector4(0.4f, 0.6f, 1.0f, 0.6f),
                "Edge glow on all character cards. Requires 'Use nameplate colour for card glow' to be off."
            ),
            new CustomColorOption(
                "custom.pageButtonActive",
                "Active Page Button",
                "Window",
                new Vector4(0.4f, 0.6f, 1.0f, 0.8f),
                "Current page marker in the main grid and Mod Manager"
            ),
            new CustomColorOption(
                "custom.settings.accent",
                "Settings Accent",
                "Window",
                new Vector4(1f, 214f / 255f, 0f, 1f),
                "Headers, underlines, and highlights inside the Settings window only. Independent of Accent so the editor stays readable while you experiment."
            ),

            // Design panel
            new CustomColorOption(
                "custom.designPanelBg",
                "Background",
                "Design Panel",
                new Vector4(0.08f, 0.08f, 0.10f, 0.98f),
                "Background of the Design Panel on the right side of the main window"
            ),
            new CustomColorOption(
                "custom.designPanel.activeAccent",
                "Active Design",
                "Design Panel",
                new Vector4(1f, 214f / 255f, 0f, 1f),
                "Highlight on the currently applied design's row. Follows Accent unless set."
            ),

            // Panels
            new CustomColorOption(
                "custom.list.bg",
                "List Backdrop",
                "Panels",
                new Vector4(6f / 255f, 7f / 255f, 9f / 255f, 1f),
                "Dark backdrop behind the character grid and design lists"
            ),
            new CustomColorOption(
                "custom.header.top",
                "Header Gradient",
                "Panels",
                new Vector4(12f / 255f, 14f / 255f, 20f / 255f, 1f),
                "Top shade of window header and toolbar gradients"
            ),

            // Atmosphere
            new CustomColorOption(
                "custom.ambient.magenta",
                "Ambient Magenta",
                "Atmosphere",
                new Vector4(241f / 255f, 43f / 255f, 124f / 255f, 1f),
                "Very faint animated glow in the main window and achievements backgrounds: a breathing spot, scan lines, and dust. Hover the (?) to spotlight it."
            ),
            new CustomColorOption(
                "custom.ambient.cyan",
                "Ambient Cyan",
                "Atmosphere",
                new Vector4(41f / 255f, 182f / 255f, 246f / 255f, 1f),
                "Very faint animated glow in the main window and achievements backgrounds: an aurora spot, scan lines, and dust. Hover the (?) to spotlight it."
            ),
            new CustomColorOption(
                "custom.ambient.violet",
                "Ambient Violet",
                "Atmosphere",
                new Vector4(126f / 255f, 87f / 255f, 194f / 255f, 1f),
                "Very faint animated glow in the main window and achievements backgrounds: an aurora spot and dust. Hover the (?) to spotlight it."
            ),

            // Wardrobe
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
                "Background fill of design cards (visible behind images without preview)"
            ),
            new CustomColorOption(
                "custom.wardrobeCardBorder",
                "Card Border",
                "Wardrobe",
                new Vector4(0.35f, 0.30f, 0.20f, 0.35f),
                "Border around each design card. The focused card's border follows the wardrobe accent."
            ),
            new CustomColorOption(
                "custom.wardrobeAccent",
                "Rail & Accent",
                "Wardrobe",
                new Vector4(0.83f, 0.69f, 0.22f, 1f),
                "Colour of the hanger rail, hangers, sparkle particles, perimeter streak, apply pill, and page buttons"
            ),
            new CustomColorOption(
                "custom.wardrobeNameText",
                "Headline Character Name",
                "Wardrobe",
                new Vector4(1f, 1f, 1f, 0.88f),
                "Character-name half of the wardrobe headline. The design-name half follows the wardrobe accent."
            ),
        };

        #endregion

        #region Helper Methods

        private static readonly HashSet<string> ClassicDeadKeys = new()
        {
            "custom.settings.accent",
            "custom.card.buttonBg",
            "custom.card.designsText",
            "custom.card.editText",
            "custom.card.deleteText",
            "custom.card.nameText",
            "custom.designPanel.activeAccent",
            "custom.input.text",
            "custom.input.placeholder",
            "custom.button.menu.icon",
            "custom.list.bg",
            "custom.button.bg",
            "custom.button.icon",
            "custom.ambient.magenta",
        };

        public static bool IsDeadInClassic(string key) => ClassicDeadKeys.Contains(key);

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

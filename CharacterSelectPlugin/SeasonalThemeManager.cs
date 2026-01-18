using System;
using System.Numerics;

namespace CharacterSelectPlugin
{
    public enum SeasonalTheme
    {
        Default,
        Halloween,
        Christmas,
        Winter
    }

    public enum ThemeSelection
    {
        Current,      // Auto-seasonal based on date
        Default,      // Always use default theme
        Halloween,    // Always use Halloween theme
        Christmas,    // Always use Christmas theme
        Winter,       // Always use Winter theme
        Custom        // User-customized theme
    }

    public class SeasonalThemeColors
    {
        public Vector4 PrimaryAccent { get; set; }
        public Vector4 SecondaryAccent { get; set; }
        public Vector4 IconTint { get; set; }
        public Vector4 GlowColor { get; set; }
        public Vector4 ParticleColor { get; set; }
        public Vector4 BackgroundTint { get; set; }
    }

    public static class SeasonalThemeManager
    {
        public static SeasonalTheme GetCurrentSeasonalTheme()
        {
            var now = DateTime.Now;
            var month = now.Month;
            var day = now.Day;

            // Halloween: October
            if (month == 10)
                return SeasonalTheme.Halloween;
            
            // Christmas: December 24th and 25th only
            if (month == 12 && (day == 24 || day == 25))
                return SeasonalTheme.Christmas;
            
            // Winter: November through March (excluding Christmas days)
            if (month == 11 || 
                (month == 12 && day != 24 && day != 25) || 
                month == 1 || month == 2 || month == 3)
                return SeasonalTheme.Winter;

            return SeasonalTheme.Default;
        }

        public static SeasonalThemeColors GetThemeColors(SeasonalTheme theme)
        {
            return theme switch
            {
                SeasonalTheme.Halloween => new SeasonalThemeColors
                {
                    PrimaryAccent = new Vector4(1.0f, 0.42f, 0.21f, 1.0f),      // Orange #FF6B35
                    SecondaryAccent = new Vector4(0.48f, 0.17f, 0.75f, 1.0f),   // Purple #7B2CBF
                    IconTint = new Vector4(1.0f, 0.55f, 0.0f, 1.0f),            // Dark Orange #FF8C00
                    GlowColor = new Vector4(1.0f, 0.42f, 0.21f, 0.6f),          // Orange Glow
                    ParticleColor = new Vector4(0.9f, 0.41f, 0.0f, 1.0f),       // Burnt Orange
                    BackgroundTint = new Vector4(0.1f, 0.05f, 0.1f, 0.3f)       // Dark purple tint
                },
                SeasonalTheme.Christmas => new SeasonalThemeColors
                {
                    PrimaryAccent = new Vector4(0.95f, 0.15f, 0.15f, 1.0f),     // Vibrant saturated red
                    SecondaryAccent = new Vector4(0.15f, 0.85f, 0.25f, 1.0f),   // Vibrant saturated green
                    IconTint = new Vector4(1.0f, 0.85f, 0.0f, 1.0f),            // Bright gold
                    GlowColor = new Vector4(1.0f, 0.85f, 0.0f, 0.6f),           // Bright gold glow
                    ParticleColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f),        // Snow white
                    BackgroundTint = new Vector4(0.08f, 0.05f, 0.05f, 0.3f)     // Warm red tint
                },
                SeasonalTheme.Winter => new SeasonalThemeColors
                {
                    PrimaryAccent = new Vector4(0.6f, 0.8f, 1.0f, 1.0f),        // Icy blue
                    SecondaryAccent = new Vector4(0.9f, 0.95f, 1.0f, 1.0f),     // Pale white
                    IconTint = new Vector4(0.8f, 0.9f, 1.0f, 1.0f),             // Cool silver
                    GlowColor = new Vector4(0.6f, 0.8f, 1.0f, 0.6f),            // Icy blue glow
                    ParticleColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f),        // Snow white
                    BackgroundTint = new Vector4(0.05f, 0.08f, 0.15f, 0.3f)     // Cool blue-gray tint
                },
                _ => new SeasonalThemeColors
                {
                    PrimaryAccent = new Vector4(0.3f, 0.6f, 1.0f, 1.0f),        // Default Blue
                    SecondaryAccent = new Vector4(0.7f, 0.7f, 0.7f, 1.0f),      // Gray
                    IconTint = new Vector4(1.0f, 1.0f, 1.0f, 1.0f),             // White
                    GlowColor = new Vector4(0.3f, 0.6f, 1.0f, 0.6f),            // Blue Glow
                    ParticleColor = new Vector4(1.0f, 0.8f, 0.2f, 1.0f),        // Gold
                    BackgroundTint = new Vector4(0.0f, 0.0f, 0.0f, 0.0f)        // No tint
                }
            };
        }

        public static bool IsSeasonalThemeEnabled(Configuration config)
        {
            // Legacy support: if old setting was true and no new selection made, default to Current
            if (config.UseSeasonalTheme && config.SelectedTheme == ThemeSelection.Current)
                return true;

            // Custom theme is not a seasonal theme
            if (config.SelectedTheme == ThemeSelection.Custom)
                return false;

            return config.SelectedTheme != ThemeSelection.Default;
        }

        public static SeasonalTheme GetEffectiveTheme(Configuration config)
        {
            return config.SelectedTheme switch
            {
                ThemeSelection.Current => GetCurrentSeasonalTheme(),
                ThemeSelection.Default => SeasonalTheme.Default,
                ThemeSelection.Halloween => SeasonalTheme.Halloween,
                ThemeSelection.Christmas => SeasonalTheme.Christmas,
                ThemeSelection.Winter => SeasonalTheme.Winter,
                ThemeSelection.Custom => SeasonalTheme.Default, // Custom uses its own styling
                _ => SeasonalTheme.Default
            };
        }

        public static SeasonalThemeColors GetCurrentThemeColors(Configuration config)
        {
            var effectiveTheme = GetEffectiveTheme(config);
            return GetThemeColors(effectiveTheme);
        }

        public static string GetThemeDisplayName(SeasonalTheme theme)
        {
            return theme switch
            {
                SeasonalTheme.Halloween => "Halloween",
                SeasonalTheme.Christmas => "Christmas",
                SeasonalTheme.Winter => "Winter",
                _ => "Default"
            };
        }

        public static string GetThemeDisplayNameSafe(SeasonalTheme theme)
        {
            return theme switch
            {
                SeasonalTheme.Halloween => "Halloween",
                SeasonalTheme.Christmas => "Christmas",
                SeasonalTheme.Winter => "Winter",
                _ => "Default"
            };
        }

        public static string GetThemeSelectionDisplayName(ThemeSelection selection)
        {
            return selection switch
            {
                ThemeSelection.Current => "Current Season",
                ThemeSelection.Default => "Default",
                ThemeSelection.Halloween => "Halloween",
                ThemeSelection.Christmas => "Christmas",
                ThemeSelection.Winter => "Winter",
                ThemeSelection.Custom => "Custom",
                _ => "Default"
            };
        }

        public static string GetThemeSelectionDescription(ThemeSelection selection)
        {
            var currentSeason = GetThemeDisplayName(GetCurrentSeasonalTheme());
            return selection switch
            {
                ThemeSelection.Current => $"Auto-seasonal theme (currently: {currentSeason})",
                ThemeSelection.Default => "Standard blue theme",
                ThemeSelection.Halloween => "Orange and purple Halloween theme",
                ThemeSelection.Christmas => "Red, green, and gold Christmas theme",
                ThemeSelection.Winter => "Icy blue and white winter theme",
                ThemeSelection.Custom => "Your custom colors and settings",
                _ => "Standard theme"
            };
        }
    }
}
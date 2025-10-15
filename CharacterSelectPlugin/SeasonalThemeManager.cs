using System;
using System.Numerics;

namespace CharacterSelectPlugin
{
    public enum SeasonalTheme
    {
        Default,
        Halloween,
        Christmas
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
            
            // Christmas/Winter: November - December (when winter theme is ready)
            if (month == 11 || month == 12)
                return SeasonalTheme.Christmas;

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
                    PrimaryAccent = new Vector4(0.8f, 0.2f, 0.2f, 1.0f),        // Red
                    SecondaryAccent = new Vector4(0.2f, 0.7f, 0.3f, 1.0f),      // Green
                    IconTint = new Vector4(1.0f, 0.84f, 0.0f, 1.0f),            // Gold
                    GlowColor = new Vector4(1.0f, 0.84f, 0.0f, 0.6f),           // Gold Glow
                    ParticleColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f),        // Snow white
                    BackgroundTint = new Vector4(0.05f, 0.1f, 0.15f, 0.3f)      // Cool blue tint
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
            return config.UseSeasonalTheme;
        }

        public static SeasonalThemeColors GetCurrentThemeColors(Configuration config)
        {
            if (!IsSeasonalThemeEnabled(config))
                return GetThemeColors(SeasonalTheme.Default);

            var currentTheme = GetCurrentSeasonalTheme();
            return GetThemeColors(currentTheme);
        }

        public static string GetThemeDisplayName(SeasonalTheme theme)
        {
            return theme switch
            {
                SeasonalTheme.Halloween => "🎃 Halloween",
                SeasonalTheme.Christmas => "🎄 Winter/Christmas",
                _ => "Default"
            };
        }

        public static string GetThemeDisplayNameSafe(SeasonalTheme theme)
        {
            return theme switch
            {
                SeasonalTheme.Halloween => "Halloween",
                SeasonalTheme.Christmas => "Winter/Christmas",
                _ => "Default"
            };
        }
    }
}
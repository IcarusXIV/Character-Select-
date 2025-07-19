using System.Numerics;

namespace CharacterSelectPlugin.Windows.Styles
{
    public static class ColorSchemes
    {
        public static class Dark
        {
            // Updated to match the new matte black theme
            public static readonly Vector4 WindowBackground = new(0.06f, 0.06f, 0.06f, 0.98f);
            public static readonly Vector4 ChildBackground = new(0.08f, 0.08f, 0.08f, 0.95f);
            public static readonly Vector4 PopupBackground = new(0.06f, 0.06f, 0.06f, 0.98f);
            public static readonly Vector4 FrameBackground = new(0.12f, 0.12f, 0.12f, 0.9f);
            public static readonly Vector4 FrameBackgroundHovered = new(0.18f, 0.18f, 0.18f, 0.9f);
            public static readonly Vector4 FrameBackgroundActive = new(0.22f, 0.22f, 0.22f, 0.9f);

            public static readonly Vector4 ButtonNormal = new(0.15f, 0.15f, 0.15f, 0.9f);
            public static readonly Vector4 ButtonHovered = new(0.25f, 0.25f, 0.25f, 1.0f);
            public static readonly Vector4 ButtonActive = new(0.35f, 0.35f, 0.35f, 1.0f);

            public static readonly Vector4 TextPrimary = new(0.92f, 0.92f, 0.92f, 1.0f);
            public static readonly Vector4 TextSecondary = new(0.7f, 0.7f, 0.7f, 1.0f);
            public static readonly Vector4 TextMuted = new(0.5f, 0.5f, 0.5f, 0.8f);

            public static readonly Vector4 AccentBlue = new(0.3f, 0.7f, 1.0f, 1.0f);
            public static readonly Vector4 AccentGreen = new(0.27f, 1.07f, 0.27f, 1.0f);
            public static readonly Vector4 AccentRed = new(1.0f, 0.27f, 0.27f, 1.0f);
            public static readonly Vector4 AccentYellow = new(1.0f, 0.85f, 0.3f, 1.0f);

            public static readonly Vector4 NameplateBackground = new(0, 0, 0, 0.9f);
            public static readonly Vector4 CardBackground = new(0.1f, 0.1f, 0.1f, 0.95f);
        }

        public static class Gradients
        {
            public static readonly Vector4 NameplateTop = new(0, 0, 0, 0.9f);
            public static readonly Vector4 NameplateBottom = new(0, 0, 0, 0.7f);

            public static readonly Vector4 CardTop = new(0.18f, 0.18f, 0.18f, 0.9f);
            public static readonly Vector4 CardBottom = new(0.12f, 0.12f, 0.12f, 0.9f);
        }

        public static class Glow
        {
            public static readonly Vector4 Subtle = new(1.0f, 1.0f, 1.0f, 0.3f);
            public static readonly Vector4 Medium = new(1.0f, 1.0f, 1.0f, 0.6f);
            public static readonly Vector4 Strong = new(1.0f, 1.0f, 1.0f, 0.9f);
        }
    }
}

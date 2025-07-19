using Newtonsoft.Json;
using System;
using System.Numerics;

namespace CharacterSelectPlugin
{
    public class RPProfile
    {
        public string? Pronouns { get; set; }
        public string? Gender { get; set; }
        public string? Age { get; set; }
        public string? Orientation { get; set; }
        public string? Relationship { get; set; }
        public string? Occupation { get; set; }
        public string? Abilities { get; set; }
        public string? Bio { get; set; }
        public string? Tags { get; set; }

        public string? CustomImagePath { get; set; }
        public float ImageZoom { get; set; } = 1.0f;
        public Vector2 ImageOffset { get; set; } = Vector2.Zero;
        public ProfileSharing Sharing { get; set; } = ProfileSharing.AlwaysShare;
        public string? ProfileImageUrl { get; set; }
        public string? CharacterName { get; set; }
        public Vector3Serializable NameplateColor { get; set; } = new(0.3f, 0.7f, 1f);
        public string? Race { get; set; }
        public DateTime? LastActiveTime { get; set; } = null;

        public string? BackgroundImage { get; set; } = null;
        public ProfileEffects Effects { get; set; } = new ProfileEffects();
        public Vector3Serializable? ProfileColor { get; set; } = null;
        public string? GalleryStatus { get; set; }
        public string? Links { get; set; }

        [JsonIgnore]
        public ProfileAnimationTheme? AnimationTheme { get; set; } = null;

        public bool IsEmpty()
        {
            return string.IsNullOrWhiteSpace(Pronouns)
                && string.IsNullOrWhiteSpace(Gender)
                && string.IsNullOrWhiteSpace(Age)
                && string.IsNullOrWhiteSpace(Race)
                && string.IsNullOrWhiteSpace(Orientation)
                && string.IsNullOrWhiteSpace(Relationship)
                && string.IsNullOrWhiteSpace(Occupation)
                && string.IsNullOrWhiteSpace(Abilities)
                && string.IsNullOrWhiteSpace(Bio)
                && string.IsNullOrWhiteSpace(Tags)
                && string.IsNullOrWhiteSpace(GalleryStatus);
        }

        public void MigrateFromLegacyTheme()
        {
            if (AnimationTheme.HasValue && string.IsNullOrEmpty(BackgroundImage))
            {
                switch (AnimationTheme.Value)
                {
                    case ProfileAnimationTheme.Nature:
                        BackgroundImage = "forest_background.png";
                        Effects.Fireflies = true;
                        Effects.FallingLeaves = true;
                        break;
                    case ProfileAnimationTheme.DarkGothic:
                        BackgroundImage = "gothic_background.png";
                        Effects.Bats = true;
                        Effects.Fire = true;
                        Effects.Smoke = true;
                        break;
                    case ProfileAnimationTheme.MagicalParticles:
                        BackgroundImage = "magical_background.png";
                        Effects.Fireflies = true;
                        Effects.Butterflies = true;
                        break;
                    case ProfileAnimationTheme.CircuitBoard:
                    case ProfileAnimationTheme.Minimalist:
                        BackgroundImage = null;
                        break;
                }
                AnimationTheme = null;
            }
        }
    }

    public class ProfileEffects
    {
        public bool CircuitBoard { get; set; } = false;
        public bool Fireflies { get; set; } = false;
        public bool FallingLeaves { get; set; } = false;
        public bool Butterflies { get; set; } = false;
        public bool Bats { get; set; } = false;
        public bool Fire { get; set; } = false;
        public bool Smoke { get; set; } = false;

        // Colour customization for particles
        public ParticleColorScheme ColorScheme { get; set; } = ParticleColorScheme.Auto;
        public Vector3Serializable CustomParticleColor { get; set; } = new(1f, 1f, 1f);
    }

    public enum ParticleColorScheme
    {
        Auto,// Automatically match the background/theme
        Warm, // Oranges/golds - good for desert/Ul'dah
        Cool,  // Blues/teals - good for water/Limsa
        Forest,  // Greens - good for nature/Gridania  
        Magical, // Purples/blues - good for magical areas
        Winter,  // White/silver - good for snow/Ishgard
        Custom  // Use CustomParticleColour
    }

    public enum ProfileSharing
    {
        AlwaysShare,
        NeverShare,
        ShowcasePublic
    }

    public enum ProfileAnimationTheme
    {
        CircuitBoard,
        Minimalist,
        Nature,
        DarkGothic,
        MagicalParticles
    }

    public static class RPProfileJson
    {
        public static string Serialize(RPProfile profile)
        {
            return JsonConvert.SerializeObject(profile);
        }

        public static RPProfile? Deserialize(string json)
        {
            var profile = JsonConvert.DeserializeObject<RPProfile>(json);
            profile?.MigrateFromLegacyTheme();
            return profile;
        }
    }

    public struct Vector3Serializable
    {
        public float X;
        public float Y;
        public float Z;

        public Vector3Serializable(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static implicit operator Vector3(Vector3Serializable v) => new(v.X, v.Y, v.Z);
        public static implicit operator Vector3Serializable(Vector3 v) => new(v.X, v.Y, v.Z);
    }
}

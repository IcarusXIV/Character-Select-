using Newtonsoft.Json;
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

        public string? CustomImagePath { get; set; } // Optional override for profile image
        public float ImageZoom { get; set; } = 1.0f;
        public Vector2 ImageOffset { get; set; } = Vector2.Zero;
        public ProfileSharing Sharing { get; set; } = ProfileSharing.AlwaysShare;
        public string? ProfileImageUrl { get; set; }
        public string? CharacterName { get; set; }
        public Vector3Serializable NameplateColor { get; set; } = new(0.3f, 0.7f, 1f);
        public string? Race { get; set; }


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
                && string.IsNullOrWhiteSpace(Tags);
        }
    }
    public enum ProfileSharing
    {
        AlwaysShare,
        NeverShare
    }
    public static class RPProfileJson
    {
        public static string Serialize(RPProfile profile)
        {
            return JsonConvert.SerializeObject(profile); // ← FIXED
        }

        public static RPProfile? Deserialize(string json)
        {
            return JsonConvert.DeserializeObject<RPProfile>(json); // ← FIXED
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

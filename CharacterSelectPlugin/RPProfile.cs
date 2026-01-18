using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace CharacterSelectPlugin
{
    public class GalleryImage
    {
        public string Url { get; set; } = "";
        public string Name { get; set; } = "";
        public float Zoom { get; set; } = 1.0f;
        public Vector2 Offset { get; set; } = Vector2.Zero;
    }
    
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

        // Title and Status for display under name/pronouns
        public string? Title { get; set; }           // Tagline like "The Wandering Minstrel"
        public int TitleIcon { get; set; } = 0;      // FontAwesome icon ID (0 = none)
        public string? Status { get; set; }          // Current status like "Seeking RP"
        public int StatusIcon { get; set; } = 0;     // FontAwesome icon ID (0 = none)

        public string? CustomImagePath { get; set; }
        public string? BannerImagePath { get; set; }
        public string? BannerImageUrl { get; set; }
        public float ImageZoom { get; set; } = 1.0f;
        public Vector2 ImageOffset { get; set; } = Vector2.Zero;
        public float BannerZoom { get; set; } = 1.0f;
        public Vector2 BannerOffset { get; set; } = Vector2.Zero;
        public ProfileSharing Sharing { get; set; } = ProfileSharing.AlwaysShare;
        public bool AllowOthersToSeeMyCSName { get; set; } = true;
        public string? ProfileImageUrl { get; set; }
        public string? CharacterName { get; set; }
        public Vector3Serializable NameplateColor { get; set; } = new(0.3f, 0.7f, 1f);
        public string? Race { get; set; }
        public DateTime? LastActiveTime { get; set; } = null;

        public string? BackgroundImage { get; set; } = null;

        /// <summary>
        /// URL-based custom background for Expanded RP Profile. Takes priority over BackgroundImage preset.
        /// </summary>
        public string? BackgroundImageUrl { get; set; } = null;

        /// <summary>
        /// Background image opacity for URL backgrounds (0.3 - 1.0).
        /// </summary>
        public float BackgroundImageOpacity { get; set; } = 1.0f;

        /// <summary>
        /// Background image zoom for URL backgrounds (0.5 - 3.0, 1.0 = fit to window).
        /// </summary>
        public float BackgroundImageZoom { get; set; } = 1.0f;

        /// <summary>
        /// Background image X offset for URL backgrounds (-1.0 to 1.0, 0 = centered).
        /// </summary>
        public float BackgroundImageOffsetX { get; set; } = 0f;

        /// <summary>
        /// Background image Y offset for URL backgrounds (-1.0 to 1.0, 0 = centered).
        /// </summary>
        public float BackgroundImageOffsetY { get; set; } = 0f;

        /// <summary>
        /// URL-based custom background for compact RP Profile view.
        /// </summary>
        public string? RPBackgroundImageUrl { get; set; } = null;

        /// <summary>
        /// Background image opacity for RP Profile URL background (0.3 - 1.0).
        /// </summary>
        public float RPBackgroundImageOpacity { get; set; } = 0.5f;

        /// <summary>
        /// Background image zoom for RP Profile URL background (0.5 - 3.0).
        /// </summary>
        public float RPBackgroundImageZoom { get; set; } = 1.0f;

        /// <summary>
        /// Background image X offset for RP Profile URL background (-1.0 to 1.0).
        /// </summary>
        public float RPBackgroundImageOffsetX { get; set; } = 0f;

        /// <summary>
        /// Background image Y offset for RP Profile URL background (-1.0 to 1.0).
        /// </summary>
        public float RPBackgroundImageOffsetY { get; set; } = 0f;

        public ProfileEffects Effects { get; set; } = new ProfileEffects();
        public Vector3Serializable? ProfileColor { get; set; } = null;
        public string? GalleryStatus { get; set; }
        public string? Links { get; set; }
        public bool IsNSFW { get; set; } = false;

        // Content Boxes for enhanced editor
        public List<ContentBox>? LeftContentBoxes { get; set; } = new();
        public List<ContentBox>? RightContentBoxes { get; set; } = new();

        // Gallery Images for character showcase
        [JsonConverter(typeof(GalleryImageListConverter))]
        public List<GalleryImage>? GalleryImages { get; set; } = new();
        
        // Gallery image positioning (for preview)
        public bool UseGalleryPreview { get; set; } = false; // Explicitly enable gallery preview
        public int SelectedGalleryPreviewIndex { get; set; } = -1; // -1 means no gallery preview selected

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
        
        public void MigrateGalleryImages(Newtonsoft.Json.Linq.JToken? galleryImagesToken = null)
        {
            // Handle migration from List<string> to List<GalleryImage>
            if (galleryImagesToken != null && galleryImagesToken.Type == Newtonsoft.Json.Linq.JTokenType.Array)
            {
                var migratedImages = new List<GalleryImage>();
                foreach (var token in galleryImagesToken)
                {
                    if (token.Type == Newtonsoft.Json.Linq.JTokenType.String)
                    {
                        // Old format: string URL
                        migratedImages.Add(new GalleryImage
                        {
                            Url = token.ToString() ?? "",
                            Name = $"Image {migratedImages.Count + 1}",
                            Zoom = 1.0f,
                            Offset = Vector2.Zero
                        });
                    }
                    else
                    {
                        // New format: already a GalleryImage object
                        var galleryImage = token.ToObject<GalleryImage>();
                        if (galleryImage != null)
                            migratedImages.Add(galleryImage);
                    }
                }
                GalleryImages = migratedImages;
            }
            else
            {
                GalleryImages ??= new List<GalleryImage>();
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
        Auto,// Use profile accent/nameplate color
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
            try
            {
                // First parse as JObject to handle migration
                var jObject = Newtonsoft.Json.Linq.JObject.Parse(json);
                var galleryImagesToken = jObject["GalleryImages"];
                
                // Temporarily remove GalleryImages from the JSON if it's in string format
                if (galleryImagesToken?.Type == Newtonsoft.Json.Linq.JTokenType.Array && 
                    galleryImagesToken.HasValues && 
                    galleryImagesToken.First?.Type == Newtonsoft.Json.Linq.JTokenType.String)
                {
                    jObject.Remove("GalleryImages");
                }
                
                // Deserialize the rest of the profile
                var profile = jObject.ToObject<RPProfile>();
                
                if (profile != null)
                {
                    // Handle gallery migration
                    profile.MigrateGalleryImages(galleryImagesToken);
                    profile.MigrateFromLegacyTheme();
                }
                
                return profile;
            }
            catch
            {
                // Fallback to regular deserialization for backward compatibility
                var profile = JsonConvert.DeserializeObject<RPProfile>(json);
                profile?.MigrateFromLegacyTheme();
                profile?.MigrateGalleryImages();
                return profile;
            }
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

    public class ContentBox
    {
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Content { get; set; } = "";
        public string Likes { get; set; } = "";        // For Likes & Dislikes type
        public string Dislikes { get; set; } = "";     // For Likes & Dislikes type
        public ContentBoxType Type { get; set; } = ContentBoxType.CoreIdentity;
        public ContentBoxLayoutType LayoutType { get; set; } = ContentBoxLayoutType.Standard;
        
        // Additional fields for new layout types
        public string LeftColumn { get; set; } = "";   // For ProsCons, KeyValue layouts
        public string RightColumn { get; set; } = "";  // For ProsCons, KeyValue layouts
        public string TimelineData { get; set; } = ""; // For Timeline layout (JSON or structured format)
        public string TaggedData { get; set; } = "";   // For Tagged layout
        public string QuoteText { get; set; } = "";    // For Quote layout
        public string QuoteAuthor { get; set; } = "";  // For Quote layout attribution
    }

    public enum ContentBoxType
    {
        CoreIdentity = 0,
        Background = 1,
        Personality = 2,
        AdditionalDetails = 3,
        KeyTraits = 4,
        Combat = 5,
        RPHooks = 6,
        LikesAndDislikes = 7,
        ExternalLinks = 8,
        Custom = 9,
        Connections = 10
    }

    public enum ContentBoxLayoutType
    {
        Standard = 0,       // Single content field (default)
        LikesDislikes = 1,  // Two-column likes/dislikes (legacy)
        List = 2,           // Bullet points or numbered list
        KeyValue = 3,       // Label: Value pairs in columns
        Quote = 4,          // Stylized quote with attribution
        Timeline = 5,       // Date/event chronological layout
        Grid = 6,           // Multi-column grid layout
        ProsCons = 7,       // Two-column pros/cons or comparison
        Tagged = 8,         // Tags with categorized content
        Connections = 9     // Character relationships/connections
    }

    public class GalleryImageListConverter : JsonConverter<List<GalleryImage>>
    {
        public override List<GalleryImage>? ReadJson(JsonReader reader, Type objectType, List<GalleryImage>? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var list = new List<GalleryImage>();
            
            if (reader.TokenType == JsonToken.StartArray)
            {
                reader.Read();
                int index = 0;
                
                while (reader.TokenType != JsonToken.EndArray)
                {
                    if (reader.TokenType == JsonToken.String)
                    {
                        // Old format: string URL
                        var url = reader.Value?.ToString() ?? "";
                        list.Add(new GalleryImage
                        {
                            Url = url,
                            Name = $"Image {index + 1}",
                            Zoom = 1.0f,
                            Offset = Vector2.Zero
                        });
                    }
                    else if (reader.TokenType == JsonToken.StartObject)
                    {
                        // New format: GalleryImage object
                        var galleryImage = serializer.Deserialize<GalleryImage>(reader);
                        if (galleryImage != null)
                            list.Add(galleryImage);
                    }
                    
                    index++;
                    reader.Read();
                }
            }
            
            return list;
        }

        public override void WriteJson(JsonWriter writer, List<GalleryImage>? value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value);
        }
    }
    
    // Structured data models for content box layouts
    public class TimelineEntry
    {
        public string Date { get; set; } = "";
        public string Event { get; set; } = "";
        public string Id { get; set; } = Guid.NewGuid().ToString();
    }
    
    public class GridItem
    {
        public string Icon { get; set; } = "";
        public string Text { get; set; } = "";
        public string Id { get; set; } = Guid.NewGuid().ToString();
    }
    
    public class ListItem
    {
        public string Text { get; set; } = "";
        public bool IsChecked { get; set; } = false;
        public int IndentLevel { get; set; } = 0;
        public string Id { get; set; } = Guid.NewGuid().ToString();
    }
    
    public class KeyValuePairData
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
        public string Id { get; set; } = Guid.NewGuid().ToString();
    }
    
    public class TagCategory
    {
        public string Name { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public string Id { get; set; } = Guid.NewGuid().ToString();
    }
    
    public class ProsConsData
    {
        public List<string> Pros { get; set; } = new();
        public List<string> Cons { get; set; } = new();
    }

    public class Connection
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";                      // Display name
        public string RelationshipType { get; set; } = "Friend";    // Friend, Family, Rival, Partner, etc.
        public string CustomRelationshipType { get; set; } = "";    // Custom label when RelationshipType is "Other"
        public string? LinkedCharacterName { get; set; } = null;    // CS+ character name if own character
        public string? LinkedCharacterInGameName { get; set; } = null; // In-game name for server lookups (e.g., "Name Surname@Server")
        public bool IsOwnCharacter { get; set; } = false;           // Whether this links to user's own CS+ character

        // Helper to get the display relationship type
        public string DisplayRelationshipType => RelationshipType == "Other" && !string.IsNullOrEmpty(CustomRelationshipType)
            ? CustomRelationshipType
            : RelationshipType;
    }
}

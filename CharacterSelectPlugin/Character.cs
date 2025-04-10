using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json;

namespace CharacterSelectPlugin
{
    [Serializable]
    public class Character
    {
        public string Name { get; set; }
        public string Macros { get; set; } = ""; // ✅ Default to empty instead of null
        public string? ImagePath { get; set; }
        public List<CharacterDesign> Designs { get; set; }
        public Vector3 NameplateColor { get; set; } = new Vector3(1.0f, 1.0f, 1.0f); // Default to white
        public string PenumbraCollection { get; set; } = "";
        public string GlamourerDesign { get; set; } = "";
        public string CustomizeProfile { get; set; } = "";
        public bool IsFavorite { get; set; } = false; // Allows favoriting
        public DateTime DateAdded { get; set; } = DateTime.Now; // Tracks when the character was added
        public int SortOrder { get; set; } = 0; // Tracks manual drag-drop order
        public string HonorificTitle { get; set; } = "";
        public string HonorificPrefix { get; set; } = "";
        public string HonorificSuffix { get; set; } = "";
        public Vector3 HonorificColor { get; set; } = new Vector3(1.0f, 1.0f, 1.0f); // Default white
        public Vector3 HonorificGlow { get; set; } = new Vector3(1.0f, 1.0f, 1.0f); // Default white
        public string MoodlePreset { get; set; } = ""; // MOODLES
        public byte IdlePoseIndex { get; set; } = 0; // Idles!
        public byte SitPoseIndex { get; set; } = 255;
        public byte GroundSitPoseIndex { get; set; } = 255;
        public byte DozePoseIndex { get; set; } = 255;
        public string? Pronouns { get; set; }
        public string? Gender { get; set; }
        public string? Age { get; set; }
        public string? SexualOrientation { get; set; }
        public string? RelationshipStatus { get; set; }
        public string? Occupation { get; set; }
        public string? Abilities { get; set; }
        public string? Bio { get; set; }
        public string? RpTags { get; set; }
        public string? RpImagePath { get; set; } // Optional override image
        public RPProfile RPProfile { get; set; } = new();
        public string? LastInGameName { get; set; }
        public List<string> Tags { get; set; } = new();
        [JsonIgnore]
        public string Tag
        {
            get => Tags.FirstOrDefault() ?? "";
            set
            {
                Tags.Clear();
                if (!string.IsNullOrWhiteSpace(value))
                    Tags.Add(value);
            }
        }
        public List<string> KnownTags { get; set; } = new();
        public List<string> DesignTags { get; set; } = new List<string>();
        public string CharacterAutomation { get; set; } = "";






        public Character(
            string name,
            string macros,
            string? imagePath,
            List<CharacterDesign> designs,
            Vector3 nameplateColor,
            string penumbraCollection,
            string glamourerDesign,
            string customizeProfile,
            string honorificTitle,
            string honorificPrefix,
            string honorificSuffix,
            Vector3 honorificColor,
            Vector3 honorificGlow,
            string moodlePreset,
            string characterautomation)
        {
            Name = name;
            Macros = macros ?? ""; // ✅ Prevents null macros
            ImagePath = imagePath;
            Designs = designs ?? new List<CharacterDesign>();
            NameplateColor = nameplateColor;
            PenumbraCollection = penumbraCollection;
            GlamourerDesign = glamourerDesign;
            CustomizeProfile = customizeProfile;
            HonorificTitle = honorificTitle;
            HonorificPrefix = honorificPrefix;
            HonorificSuffix = honorificSuffix;
            HonorificColor = honorificColor;
            HonorificGlow = honorificGlow;
            MoodlePreset = moodlePreset;
            CharacterAutomation = characterautomation;
        }
    }

}

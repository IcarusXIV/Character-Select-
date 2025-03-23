using System;
using System.Collections.Generic;
using System.Numerics;

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
            string moodlePreset)
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
        }
    }

}

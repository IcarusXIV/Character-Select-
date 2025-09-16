using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

namespace CharacterSelectPlugin
{
    public class CharacterDesign
    {
        private static int CurrentVersion = 1;

        public string Name { get; set; }
        public string Macro { get; set; }
        public bool IsAdvancedMode { get; set; } // Tracks if Advanced Mode was used
        public string AdvancedMacro { get; set; } // Stores Advanced Mode macro separately
        public string GlamourerDesign { get; set; } // Store Glamourer Design separately
        public DateTime DateAdded { get; set; } = DateTime.UtcNow; // Automatically set when created
        public bool IsFavorite { get; set; }     // Sorting by Favourites
        public string Automation { get; set; } = "";
        public string CustomizePlusProfile { get; set; } = "";
        public string? PreviewImagePath { get; set; } = null;
        public string Tag { get; set; } = "Unsorted";
        public List<string> KnownTags { get; set; } = new();
        public List<string> DesignTags { get; set; } = new List<string>();
        public Guid? FolderId { get; set; } = null; 
        public Guid Id { get; set; } = Guid.NewGuid();
        public int SortOrder { get; set; } = 0;
        public Vector3 Color { get; set; } = new Vector3(1.0f, 1.0f, 1.0f); // Default to white
        public int Version { get; set; } = CurrentVersion; // Tracks the version of the design for future compatibility


        public CharacterDesign(string name, Vector3 color, string macro, bool isAdvancedMode = false, string advancedMacro = "", string glamourerDesign = "", string automation = "", string customizePlusProfile = "", string? previewImagePath = null, int version = 0)
        {
            Name = name;
            Color = color;
            Macro = macro;
            IsAdvancedMode = isAdvancedMode; // Tracks if this design was saved in Advanced Mode
            AdvancedMacro = advancedMacro;   // Stores the exact Advanced Mode macro if used
            GlamourerDesign = glamourerDesign; // Store Glamourer Design
            Automation = automation; // Store Glamourer Automation
            CustomizePlusProfile = customizePlusProfile; // Store Design C+ Profiles
            PreviewImagePath = previewImagePath;
            DateAdded = DateTime.UtcNow;
            IsFavorite = false;  // Default to not favourited
            
            if (version < 1) // Version 0 -> 1 Compatibility
            {
                Color = new Vector3(1.0f, 1.0f, 1.0f); // Default to white
                Version = CurrentVersion;
            }
        }
    }
}

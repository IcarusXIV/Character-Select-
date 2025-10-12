using System;
using System.Collections.Generic;

namespace CharacterSelectPlugin
{
    public class CharacterDesign
    {
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
        public Dictionary<string, bool>? SecretModState { get; set; } // Secret Mode mod selections for this design
        public HashSet<string>? SecretModPinOverrides { get; set; } // Mods that this design unpins (overrides character pins)
        
        /// <summary>
        /// Design-specific mod option configurations
        /// Only mods with explicitly saved options are stored here
        /// Format: ModDirectory -> GroupName -> List of selected option names
        /// </summary>
        public Dictionary<string, Dictionary<string, List<string>>>? ModOptionSettings { get; set; }




        public CharacterDesign(string name, string macro, bool isAdvancedMode = false, string advancedMacro = "", string glamourerDesign = "", string automation = "", string customizePlusProfile = "", string? previewImagePath = null)
        {
            Name = name;
            Macro = macro;
            IsAdvancedMode = isAdvancedMode; // Tracks if this design was saved in Advanced Mode
            AdvancedMacro = advancedMacro;   // Stores the exact Advanced Mode macro if used
            GlamourerDesign = glamourerDesign; // Store Glamourer Design
            Automation = automation; // Store Glamourer Automation
            CustomizePlusProfile = customizePlusProfile; // Store Design C+ Profiles
            PreviewImagePath = previewImagePath;
            DateAdded = DateTime.UtcNow;
            IsFavorite = false;  // Default to not favourited
        }
    }
}

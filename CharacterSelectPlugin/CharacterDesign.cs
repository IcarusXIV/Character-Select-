using System;
using System.Collections.Generic;

namespace CharacterSelectPlugin
{
    public class CharacterDesign
    {
        public string Name { get; set; }
        public string Macro { get; set; }
        public bool IsAdvancedMode { get; set; } // ✅ Tracks if Advanced Mode was used
        public string AdvancedMacro { get; set; } // ✅ Stores Advanced Mode macro separately
        public string GlamourerDesign { get; set; } // ✅ New: Store Glamourer Design separately!
        public DateTime DateAdded { get; set; } = DateTime.UtcNow; // ✅ Automatically set when created
        public bool IsFavorite { get; set; }     // ✅ Used for sorting by Favorites
        public string Automation { get; set; } = "";
        public string CustomizePlusProfile { get; set; } = "";
        public string Tag { get; set; } = "Unsorted";
        public List<string> KnownTags { get; set; } = new();
        public List<string> DesignTags { get; set; } = new List<string>();
        public Guid? FolderId { get; set; } = null; // null = Unsorted




        public CharacterDesign(string name, string macro, bool isAdvancedMode = false, string advancedMacro = "", string glamourerDesign = "", string automation = "", string customizePlusProfile = "")
        {
            Name = name;
            Macro = macro;
            IsAdvancedMode = isAdvancedMode; // ✅ Tracks if this design was saved in Advanced Mode
            AdvancedMacro = advancedMacro;   // ✅ Stores the exact Advanced Mode macro if used
            GlamourerDesign = glamourerDesign; // ✅ Store Glamourer Design properly
            Automation = automation; // ✅ Store Glamourer Automation properly
            CustomizePlusProfile = customizePlusProfile; // ✅ Store Design C+ Profiles properly
            DateAdded = DateTime.UtcNow;
            IsFavorite = false;  // ✅ Default to not favorited
        }
    }
}

using System;

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

        public CharacterDesign(string name, string macro, bool isAdvancedMode = false, string advancedMacro = "", string glamourerDesign = "")
        {
            Name = name;
            Macro = macro;
            IsAdvancedMode = isAdvancedMode; // ✅ Tracks if this design was saved in Advanced Mode
            AdvancedMacro = advancedMacro;   // ✅ Stores the exact Advanced Mode macro if used
            GlamourerDesign = glamourerDesign; // ✅ Store Glamourer Design properly
            DateAdded = DateTime.UtcNow;
            IsFavorite = false;  // ✅ Default to not favorited
        }
    }
}

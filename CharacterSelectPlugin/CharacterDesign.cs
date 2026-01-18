using System;
using System.Collections.Generic;

namespace CharacterSelectPlugin
{
    public class CharacterDesign
    {
        public string Name { get; set; }
        public string Macro { get; set; }
        public bool IsAdvancedMode { get; set; }
        public string AdvancedMacro { get; set; }
        public string GlamourerDesign { get; set; }
        public DateTime DateAdded { get; set; } = DateTime.UtcNow;
        public bool IsFavorite { get; set; }
        public string Automation { get; set; } = "";
        public string CustomizePlusProfile { get; set; } = "";
        public string? PreviewImagePath { get; set; } = null;
        public string Tag { get; set; } = "Unsorted";
        public List<string> KnownTags { get; set; } = new();
        public List<string> DesignTags { get; set; } = new List<string>();
        public Guid? FolderId { get; set; } = null; 
        public Guid Id { get; set; } = Guid.NewGuid();
        public int SortOrder { get; set; } = 0;
        public Dictionary<string, bool>? SecretModState { get; set; }
        public HashSet<string>? SecretModPinOverrides { get; set; }
        
        /// <summary>
        /// Per-design mod option settings.
        /// Format: ModDirectory -> GroupName -> SelectedOptionNames
        /// </summary>
        public Dictionary<string, Dictionary<string, List<string>>>? ModOptionSettings { get; set; }

        /// <summary>Gearset to switch to when applying this design (null = use character's setting or don't switch).</summary>
        public int? AssignedGearset { get; set; } = null;

        public CharacterDesign(string name, string macro, bool isAdvancedMode = false, string advancedMacro = "", string glamourerDesign = "", string automation = "", string customizePlusProfile = "", string? previewImagePath = null)
        {
            Name = name;
            Macro = macro;
            IsAdvancedMode = isAdvancedMode;
            AdvancedMacro = advancedMacro;
            GlamourerDesign = glamourerDesign;
            Automation = automation;
            CustomizePlusProfile = customizePlusProfile;
            PreviewImagePath = previewImagePath;
            DateAdded = DateTime.UtcNow;
            IsFavorite = false;
        }
    }
}

namespace CharacterSelectPlugin
{
    public class CharacterDesign
    {
        public string Name { get; set; }
        public string Macro { get; set; }
        public bool IsAdvancedMode { get; set; } // ✅ New: Tracks if Advanced Mode was used
        public string AdvancedMacro { get; set; } // ✅ New: Stores Advanced Mode macro separately

        public CharacterDesign(string name, string macro, bool isAdvancedMode = false, string advancedMacro = "")
        {
            Name = name;
            Macro = macro;
            IsAdvancedMode = isAdvancedMode; // ✅ Tracks if this design was saved in Advanced Mode
            AdvancedMacro = advancedMacro;   // ✅ Stores the exact Advanced Mode macro if used
        }
    }

}

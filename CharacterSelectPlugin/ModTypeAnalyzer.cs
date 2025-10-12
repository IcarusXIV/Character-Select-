using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;

namespace CharacterSelectPlugin
{
    /// <summary>
    /// Analyzes mod content to determine mod types and generate appropriate tags
    /// </summary>
    public class ModTypeAnalyzer
    {
        private readonly IPluginLog log;
        
        // Equipment slot patterns for path analysis
        private readonly Dictionary<string, string[]> equipmentPatterns = new()
        {
            ["Head"] = new[] { "_hed.mdl", "_fac.mdl", "chara/equipment/e", "/face/" },
            ["Body"] = new[] { "_top.mdl", "_met.mdl", "chara/equipment/e" },
            ["Hands"] = new[] { "_glv.mdl", "chara/equipment/e" },
            ["Legs"] = new[] { "_dwn.mdl", "_leg.mdl", "chara/equipment/e" },
            ["Feet"] = new[] { "_sho.mdl", "chara/equipment/e" },
            ["Earrings"] = new[] { "_ear.mdl", "chara/accessory/a" },
            ["Necklace"] = new[] { "_nek.mdl", "chara/accessory/a" },
            ["Bracelets"] = new[] { "_wrs.mdl", "chara/accessory/a" },
            ["Rings"] = new[] { "_rir.mdl", "_ril.mdl", "chara/accessory/a" },
            ["Primary Weapon"] = new[] { "chara/weapon/w", "_a.mdl", "_b.mdl", "_c.mdl", "_d.mdl" },
            ["Secondary Weapon"] = new[] { "chara/weapon/w", "_s.mdl" }
        };
        
        // Character customization patterns
        private readonly Dictionary<string, string[]> customizationPatterns = new()
        {
            ["Hair"] = new[] { "chara/human/c", "/obj/hair/", "_hir.mdl", "_hir_" },
            ["Face"] = new[] { "chara/human/c", "/obj/face/", "_fac.mdl", "/facepainting/" },
            ["Body"] = new[] { "chara/human/c", "/obj/body/", "_bdy.mdl", "/skin/", "/tattoo/" },
            ["Tail"] = new[] { "chara/human/c", "/obj/tail/", "_til.mdl" },
            ["Ears"] = new[] { "chara/human/c", "/obj/zear/", "_zer.mdl" }
        };
        
        // Content type patterns
        private readonly Dictionary<string, string[]> contentTypePatterns = new()
        {
            ["Models"] = new[] { ".mdl", "/model/" },
            ["Textures"] = new[] { ".tex", ".dds", "/texture/" },
            ["Materials"] = new[] { ".mtrl", "/material/" },
            ["VFX"] = new[] { ".vfx", "/vfx/", "/effect/" },
            ["Animations"] = new[] { ".pap", ".tmb", "/animation/" },
            ["UI"] = new[] { "/ui/", "/icon/", "/hud/" },
            ["Housing"] = new[] { "/housing/", "/furniture/" }
        };
        
        // Race-specific patterns
        private readonly Dictionary<string, string[]> racePatterns = new()
        {
            ["Hyur"] = new[] { "/hyur/", "/midlander/", "/highlander/" },
            ["Elezen"] = new[] { "/elezen/", "/wildwood/", "/duskwight/" },
            ["Lalafell"] = new[] { "/lalafell/", "/plainsfolk/", "/dunesfolk/" },
            ["Miqo'te"] = new[] { "/miqote/", "/seekers/", "/keepers/" },
            ["Roegadyn"] = new[] { "/roegadyn/", "/seawolves/", "/hellsguard/" },
            ["Au Ra"] = new[] { "/aura/", "/raen/", "/xaela/" },
            ["Hrothgar"] = new[] { "/hrothgar/" },
            ["Viera"] = new[] { "/viera/", "/rava/", "/veena/" }
        };
        
        public ModTypeAnalyzer(IPluginLog log)
        {
            this.log = log;
        }
        
        /// <summary>
        /// Analyze a mod's changed items to determine its type and generate tags
        /// </summary>
        public ModAnalysisResult AnalyzeMod(string modDirectory, string modName, IReadOnlyDictionary<string, object?> changedItems)
        {
            var result = new ModAnalysisResult
            {
                ModDirectory = modDirectory,
                ModName = modName,
                DetectedTags = new HashSet<string>(),
                EquipmentSlots = new HashSet<string>(),
                ContentTypes = new HashSet<string>(),
                Races = new HashSet<string>(),
                IsMultiSlot = false,
                IsRaceSpecific = false,
                IsGenderSpecific = false
            };
            
            var itemIdentifiers = changedItems.Keys.ToList();
            
            // Analyze item identifiers (NOT file paths!)
            foreach (var identifier in itemIdentifiers)
            {
                // Analyze customization FIRST (more specific patterns)
                AnalyzeCustomizationFromIdentifier(identifier, result);
                
                // Analyze equipment based on item names (only if not customization)
                if (!identifier.StartsWith("Customization:", StringComparison.OrdinalIgnoreCase))
                {
                    AnalyzeEquipmentFromIdentifier(identifier, result);
                }
                
                // Analyze race and gender specificity
                AnalyzeRaceAndGenderFromIdentifier(identifier, result);
            }
            
            // Determine if multi-slot
            result.IsMultiSlot = result.EquipmentSlots.Count > 1;
            if (result.IsMultiSlot)
                result.DetectedTags.Add("Multi-Slot");
            
            // Add race-specific tag if applicable
            if (result.IsRaceSpecific)
                result.DetectedTags.Add("Race-Specific");
            
            // Add gender-specific tag if applicable
            if (result.IsGenderSpecific)
                result.DetectedTags.Add("Gender-Specific");
            
            // Add equipment slot tags
            foreach (var slot in result.EquipmentSlots)
                result.DetectedTags.Add(slot);
            
            // Add content type tags
            foreach (var contentType in result.ContentTypes)
                result.DetectedTags.Add(contentType);
            
            // Add race tags
            foreach (var race in result.Races)
                result.DetectedTags.Add(race);
            
            // Add category tags based on analysis
            DetermineCategoryTags(result);
            
            // Removed debug logging to prevent spam with thousands of mods
            
            return result;
        }
        
        
        private void AnalyzeEquipmentFromIdentifier(string identifier, ModAnalysisResult result)
        {
            var lower = identifier.ToLower();
            
            // Check for equipment items by name patterns
            if (Regex.IsMatch(lower, @"\b(helm|hat|crown|circlet|mask|hood|cap|tiara|headband|visor|glasses|head)\b"))
                result.EquipmentSlots.Add("Head");
            
            if (Regex.IsMatch(lower, @"\b(robe|dress|shirt|coat|jacket|vest|tunic|armor|mail|plate|cuirass|harness|doublet|top)\b"))
                result.EquipmentSlots.Add("Body");
            
            if (Regex.IsMatch(lower, @"\b(glove|gauntlet|mitt|bracer|hand)\b"))
                result.EquipmentSlots.Add("Hands");
            
            if (Regex.IsMatch(lower, @"\b(pant|trouser|leg|skirt|short|breeches|hose|chaps|bottom|down)\b"))
                result.EquipmentSlots.Add("Legs");
            
            if (Regex.IsMatch(lower, @"\b(boot|shoe|sandal|slipper|sabatons|footwear|feet)\b"))
                result.EquipmentSlots.Add("Feet");
                
            if (Regex.IsMatch(lower, @"\b(earring|ear)\b"))
                result.EquipmentSlots.Add("Earrings");
                
            if (Regex.IsMatch(lower, @"\b(necklace|pendant|choker|amulet|neck)\b"))
                result.EquipmentSlots.Add("Necklace");
                
            if (Regex.IsMatch(lower, @"\b(bracelet|wrist|bangle)\b"))
                result.EquipmentSlots.Add("Bracelets");
                
            if (Regex.IsMatch(lower, @"\b(ring|finger)\b"))
                result.EquipmentSlots.Add("Rings");
            
            if (Regex.IsMatch(lower, @"\b(sword|blade|axe|hammer|bow|staff|wand|gun|knife|dagger|spear|lance|katana|rapier|scythe|shield)\b"))
            {
                if (lower.Contains("shield") || lower.Contains("off"))
                    result.EquipmentSlots.Add("Secondary Weapon");
                else
                    result.EquipmentSlots.Add("Primary Weapon");
            }
        }
        
        private void AnalyzeCustomizationFromIdentifier(string identifier, ModAnalysisResult result)
        {
            // Match Penumbra's customization format: "Customization: [Race] [Gender] [Type] [Number]"
            if (Regex.IsMatch(identifier, @"Customization:.*\bHair\b", RegexOptions.IgnoreCase))
                result.DetectedTags.Add("Hair");
            
            if (Regex.IsMatch(identifier, @"Customization:.*\bFace\b", RegexOptions.IgnoreCase))
                result.DetectedTags.Add("Face");
            
            if (Regex.IsMatch(identifier, @"Customization:.*\bTail\b", RegexOptions.IgnoreCase))
                result.DetectedTags.Add("Tail");
            
            if (Regex.IsMatch(identifier, @"Customization:.*\bEar\b", RegexOptions.IgnoreCase))
                result.DetectedTags.Add("Ears");
            
            if (Regex.IsMatch(identifier, @"Customization:.*\bSkin\b", RegexOptions.IgnoreCase))
                result.DetectedTags.Add("Skin");
            
            if (Regex.IsMatch(identifier, @"Customization:.*Face Decal", RegexOptions.IgnoreCase))
                result.DetectedTags.Add("Face Paint");
            
            if (Regex.IsMatch(identifier, @"Customization:.*\bEyes\b", RegexOptions.IgnoreCase))
                result.DetectedTags.Add("Eyes");
        }
        
        private void AnalyzeRaceAndGenderFromIdentifier(string identifier, ModAnalysisResult result)
        {
            // Extract race information from customization identifiers
            var raceMatch = Regex.Match(identifier, @"Customization: (Hyur|Elezen|Lalafell|Miqote|Roegadyn|Aura|Hrothgar|Viera)", RegexOptions.IgnoreCase);
            if (raceMatch.Success)
            {
                var raceName = raceMatch.Groups[1].Value;
                if (raceName.Equals("Aura", StringComparison.OrdinalIgnoreCase))
                    raceName = "Au Ra";
                else if (raceName.Equals("Miqote", StringComparison.OrdinalIgnoreCase))
                    raceName = "Miqo'te";
                    
                result.Races.Add(raceName);
                result.IsRaceSpecific = true;
            }
            
            // Extract gender information from customization identifiers
            var genderMatch = Regex.Match(identifier, @"Customization:.*?(Male|Female)", RegexOptions.IgnoreCase);
            if (genderMatch.Success)
            {
                var gender = genderMatch.Groups[1].Value;
                var otherGender = gender.Equals("Male", StringComparison.OrdinalIgnoreCase) ? "Female" : "Male";
                
                // Check if this mod only affects one gender
                if (!result.DetectedTags.Contains($"{otherGender} Only"))
                {
                    result.IsGenderSpecific = true;
                    result.DetectedTags.Add($"{gender} Only");
                }
            }
        }
        
        private void DetermineCategoryTags(ModAnalysisResult result)
        {
            // Determine high-level category tags
            var hasEquipment = result.EquipmentSlots.Any(slot => !slot.Contains("Weapon"));
            var hasWeapons = result.EquipmentSlots.Any(slot => slot.Contains("Weapon"));
            var hasAccessories = result.EquipmentSlots.Any(slot => 
                slot == "Earrings" || slot == "Necklace" || slot == "Bracelets" || slot == "Rings");
            var hasCustomization = result.DetectedTags.Any(tag => 
                tag == "Hair" || tag == "Face" || tag == "Skin" || tag == "Tail" || tag == "Ears" || tag == "Face Paint" || tag == "Eyes");
            
            if (hasEquipment)
                result.DetectedTags.Add("Gear");
            
            if (hasWeapons)
                result.DetectedTags.Add("Weapons");
            
            if (hasAccessories)
                result.DetectedTags.Add("Accessories");
            
            if (hasCustomization)
                result.DetectedTags.Add("Character Customization");
            
            // Determine if it's a comprehensive mod (multiple categories)
            var categoryCount = new[] { hasEquipment, hasWeapons, hasAccessories, hasCustomization }.Count(x => x);
            if (categoryCount > 1)
                result.DetectedTags.Add("Comprehensive");
        }
        
        /// <summary>
        /// Batch analyze all mods and return results
        /// </summary>
        public List<ModAnalysisResult> AnalyzeAllMods(IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> allModsData)
        {
            var results = new List<ModAnalysisResult>();
            
            foreach (var (modDirectory, changedItems) in allModsData)
            {
                try
                {
                    // Extract mod name from directory (fallback to directory name)
                    var modName = modDirectory; // In practice, you'd get this from GetModList()
                    
                    var result = AnalyzeMod(modDirectory, modName, changedItems);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    log.Error($"Error analyzing mod {modDirectory}: {ex}");
                }
            }
            
            log.Information($"Analyzed {results.Count} mods");
            return results;
        }
    }
    
    /// <summary>
    /// Result of mod analysis containing detected tags and metadata
    /// </summary>
    public class ModAnalysisResult
    {
        public string ModDirectory { get; set; } = "";
        public string ModName { get; set; } = "";
        public HashSet<string> DetectedTags { get; set; } = new();
        public HashSet<string> EquipmentSlots { get; set; } = new();
        public HashSet<string> ContentTypes { get; set; } = new();
        public HashSet<string> Races { get; set; } = new();
        public bool IsMultiSlot { get; set; }
        public bool IsRaceSpecific { get; set; }
        public bool IsGenderSpecific { get; set; }
    }
}
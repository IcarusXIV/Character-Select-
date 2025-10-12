using CharacterSelectPlugin.Windows;
using Dalamud.Configuration;
using Dalamud.Plugin;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
using System.Text.Json.Serialization;

namespace CharacterSelectPlugin
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;
        public List<Character> Characters { get; set; } = new List<Character>();
        public Vector3 NewCharacterColor { get; set; } = new Vector3(1.0f, 1.0f, 1.0f);
        public bool IsConfigWindowMovable { get; set; } = true;
        public bool SomePropertyToBeSavedAndWithADefault { get; set; } = false;

        // Profile Settings
        public float ProfileImageScale { get; set; } = 1.0f; // Image scaling
        public int ProfileColumns { get; set; } = 3;        // Number of profiles per row
        public float ProfileSpacing { get; set; } = 10.0f;  // Default spacing between profiles

        private IDalamudPluginInterface pluginInterface;
        public int CurrentSortIndex { get; set; } = 0; // Default to Manual (SortType.Manual = 0)
        public PersistentPoseSet DefaultPoses { get; set; } = new();
        public bool IsQuickSwitchWindowOpen { get; set; } = false;
        public bool EnableAutomations { get; set; } = false;
        public string LastSeenVersion { get; set; } = "";
        public ProfileSharing RPSharingMode { get; set; } = ProfileSharing.AlwaysShare;
        public List<string> KnownTags { get; set; } = new();
        public byte LastIdlePoseAppliedByPlugin { get; set; } = 255;
        public byte LastSitPoseAppliedByPlugin { get; set; } = 255;
        public byte LastGroundSitPoseAppliedByPlugin { get; set; } = 255;
        public byte LastDozePoseAppliedByPlugin { get; set; } = 255;
        public Dictionary<string, string> LastUsedCharacterByPlayer { get; set; } = new();
        public Dictionary<string, string> CharacterAssignments { get; set; } = new();
        public bool EnableLastUsedCharacterAutoload { get; set; } = false;
        public string? LastSessionId { get; set; } = null;
        public string? PreviousSessionId { get; set; }
        [JsonProperty]
        public float UIScaleMultiplier { get; set; } = 1.0f;
        [DefaultValue(true)]
        public bool ApplyIdleOnLogin { get; set; } = true;
        public uint LastKnownJobId { get; set; } = 0;
        public Dictionary<string, string> LastUsedDesignByCharacter { get; set; } = new();
        public bool ReapplyDesignOnJobChange { get; set; } = false;
        
        // Pose Settings
        public bool? UseCommandBasedPoses { get; set; } = true; // null = not set yet, will default to true
        
        // Design Sorting
        public int CurrentDesignSortIndex { get; set; } = 1; // Default to Alphabetical (matches DesignSortType.Alphabetical)
        public string? LastUsedDesignCharacterKey { get; set; } = null;
        public string? LastUsedCharacterKey { get; set; } = null;
        [DefaultValue(false)]
        public bool EnableLoginDelay { get; set; } = false;
        [JsonProperty]
        public bool EnablePoseAutoSave { get; set; } = true;
        public bool EnableSafeMode { get; set; } = false;
        public bool QuickSwitchCompact { get; set; } = false;
        public bool EnableCharacterHoverEffects { get; set; } = false;
        public HashSet<string> FavoriteGalleryProfiles { get; set; } = new();
        public HashSet<string> LikedGalleryProfiles { get; set; } = new();
        public List<FavoriteSnapshot> FavoriteSnapshots { get; set; } = new();
        public bool ShowRecentlyActiveStatus { get; set; } = true;
        public bool HasSeenTutorial { get; set; } = false;
        public bool TutorialActive { get; set; } = false;
        public int CurrentTutorialStep { get; set; } = 0;
        public bool ShowTutorialOnStartup { get; set; } = true;
        public Dictionary<uint, uint> GearsetJobMapping { get; set; } = new();
        public uint? LastUsedGearset { get; set; } = null;
        public string? GalleryMainCharacter { get; set; } = null; // Format: "CharacterName@Server"
        public bool EnableGalleryAutoRefresh { get; set; } = true;
        public int GalleryAutoRefreshSeconds { get; set; } = 30;
        [DefaultValue(false)]
        public bool RandomSelectionFavoritesOnly { get; set; } = false;
        public string? MainCharacterName { get; set; } = null; 
        public bool EnableMainCharacterOnly { get; set; } = false;
        public bool ShowMainCharacterCrown { get; set; } = true;
        public HashSet<string> BlockedGalleryProfiles { get; set; } = new();
        public float DesignPanelWidth { get; set; } = 300f;
        
        // Conflict Resolution settings (formerly Secret Mode)
        public bool EnableConflictResolution { get; set; } = false;
        public HashSet<string> SecretModeBlacklistedMods { get; set; } = new(); // Keep for backwards compatibility
        public HashSet<string> FollowedPlayers { get; set; } = new();
        [JsonPropertyName("enableDialogueIntegration")]
        public bool EnableDialogueIntegration { get; set; } = false;

        [JsonPropertyName("replaceNameInDialogue")]
        public bool ReplaceNameInDialogue { get; set; } = true;

        [JsonPropertyName("replacePronounsInDialogue")]
        public bool ReplacePronounsInDialogue { get; set; } = true;

        [JsonPropertyName("enableSmartGrammarInDialogue")]
        public bool EnableSmartGrammarInDialogue { get; set; } = true;

        [JsonPropertyName("showDialogueReplacementPreview")]
        public bool ShowDialogueReplacementPreview { get; set; } = false;
        // New enhanced dialogue settings
        [JsonPropertyName("enableLuaHookDialogue")]
        public bool EnableLuaHookDialogue { get; set; } = true;

        [JsonPropertyName("replaceGenderedTerms")]
        public bool ReplaceGenderedTerms { get; set; } = true;

        [JsonPropertyName("enableAdvancedTitleReplacement")]
        public bool EnableAdvancedTitleReplacement { get; set; } = true;

        [JsonPropertyName("theyThemStyle")]
        public GenderNeutralStyle TheyThemStyle { get; set; } = GenderNeutralStyle.Friend;

        [JsonPropertyName("customGenderNeutralTitle")]
        public string CustomGenderNeutralTitle { get; set; } = "friend";
        public bool EnableRaceReplacement { get; set; } = false;
        public DateTime LastSeenAnnouncements { get; set; } = DateTime.MinValue;
        public bool ShowNSFWProfiles { get; set; } = false;
        public int LastAcceptedGalleryTOSVersion { get; set; } = 0;

        public Configuration(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        public static Configuration Load(IDalamudPluginInterface pluginInterface)
        {
            var config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration(pluginInterface);
            config.pluginInterface = pluginInterface;

            // Ensure valid sorting index
            if (config.CurrentSortIndex < 0 || config.CurrentSortIndex > 4)
                config.CurrentSortIndex = 0;

            return config;
        }
        [Serializable]
        public class PersistentPoseSet
        {
            public byte Idle { get; set; } = 255;
            public byte Sit { get; set; } = 255;
            public byte GroundSit { get; set; } = 255;
            public byte Doze { get; set; } = 255;
        }
        public enum GenderNeutralStyle
        {
            Friend, 
            HonoredOne, 
            Traveler, 
            Adventurer, 
            Custom  
        }

        public string GetGenderNeutralTitle()
        {
            return TheyThemStyle switch
            {
                GenderNeutralStyle.Friend => "friend",
                GenderNeutralStyle.HonoredOne => "Mx.",
                GenderNeutralStyle.Traveler => "traveler",
                GenderNeutralStyle.Adventurer => "adventurer",
                GenderNeutralStyle.Custom => CustomGenderNeutralTitle,
                _ => "friend"
            };
        }

        public string GetGenderNeutralFormalTitle()
        {
            return TheyThemStyle switch
            {
                GenderNeutralStyle.Friend => "friend",
                GenderNeutralStyle.HonoredOne => "Mx.",
                GenderNeutralStyle.Traveler => "traveler",
                GenderNeutralStyle.Adventurer => "adventurer",
                GenderNeutralStyle.Custom => CustomGenderNeutralTitle,
                _ => "friend"
            };
        }

        public void Save()
        {
            pluginInterface.SavePluginConfig(this);
        }
    }
}

using CharacterSelectPlugin.Windows;
using Dalamud.Configuration;
using Dalamud.Plugin;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
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
        public bool RememberMainWindowState { get; set; } = false;
        public bool IsMainWindowOpen { get; set; } = false;
        public bool EnableAutomations { get; set; } = false;
        public string LastSeenVersion { get; set; } = "";

        /// <summary>Last seen patch notes version (e.g., "2.1"). Only changes when patch notes content changes.</summary>
        public string LastSeenPatchNotesVersion { get; set; } = "";
        public ProfileSharing RPSharingMode { get; set; } = ProfileSharing.AlwaysShare;
        public List<string> KnownTags { get; set; } = new();
        public byte LastIdlePoseAppliedByPlugin { get; set; } = 255;
        public byte LastSitPoseAppliedByPlugin { get; set; } = 255;
        public byte LastGroundSitPoseAppliedByPlugin { get; set; } = 255;
        public byte LastDozePoseAppliedByPlugin { get; set; } = 255;
        public Dictionary<string, string> LastUsedCharacterByPlayer { get; set; } = new();
        public Dictionary<string, string> CharacterAssignments { get; set; } = new();

        // Job Assignments - maps job IDs or roles to CS+ characters/designs
        // Key format: "Job_{JobId}" for specific jobs, "Role_{RoleName}" for roles
        // Value format: "Character:{CharacterName}" or "Design:{CharacterName}:{DesignName}"
        public Dictionary<string, string> JobAssignments { get; set; } = new();
        public bool EnableJobAssignments { get; set; } = false;
        public bool EnableGearsetAssignments { get; set; } = false;

        public bool EnableLastUsedCharacterAutoload { get; set; } = false;
        public bool EnableLastUsedDesignAutoload { get; set; } = false;
        public string? LastSessionId { get; set; } = null;
        public string? PreviousSessionId { get; set; }
        public List<uint> FavoriteIconIds { get; set; } = new();
        [JsonProperty]
        private float _uiScaleMultiplier = 1.0f;
        
        /// <summary>
        /// UI scale multiplier (0.5-2.0). Legacy setting, no longer in UI.
        /// </summary>
        public float UIScaleMultiplier 
        { 
            get => _uiScaleMultiplier;
            set => _uiScaleMultiplier = Math.Clamp(value, 0.5f, 2.0f);
        }
        [DefaultValue(true)]
        public bool ApplyIdleOnLogin { get; set; } = true;
        public uint LastKnownJobId { get; set; } = 0;
        public Dictionary<string, string> LastUsedDesignByCharacter { get; set; } = new();
        public bool ReapplyDesignOnJobChange { get; set; } = false;
        
        // Pose Settings
        public bool? UseCommandBasedPoses { get; set; } = true;
        
        // Design Sorting
        public int CurrentDesignSortIndex { get; set; } = 1;
        public string? LastUsedDesignCharacterKey { get; set; } = null;
        public string? LastUsedCharacterKey { get; set; } = null;
        [DefaultValue(false)]
        public bool EnableLoginDelay { get; set; } = false;
        [JsonProperty]
        public bool EnablePoseAutoSave { get; set; } = true;
        public bool EnableSafeMode { get; set; } = false;
        public bool QuickSwitchCompact { get; set; } = false;
        public bool EnableCharacterHoverEffects { get; set; } = false;
        
        // Theme Settings
        public ThemeSelection SelectedTheme { get; set; } = ThemeSelection.Current;
        public CustomThemeConfig CustomTheme { get; set; } = new();
        public List<ThemePreset> ThemePresets { get; set; } = new();
        public string? ActivePresetName { get; set; } = null;

        // Legacy (use SelectedTheme)
        [Obsolete("Use SelectedTheme instead")]
        public bool UseSeasonalTheme { get; set; } = false;
        
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
        public string? GalleryMainCharacter { get; set; } = null;
        public bool EnableGalleryAutoRefresh { get; set; } = true;
        public int GalleryAutoRefreshSeconds { get; set; } = 30;
        [DefaultValue(false)]
        public bool RandomSelectionFavoritesOnly { get; set; } = false;
        [DefaultValue(true)]
        public bool ShowRandomSelectionChatMessages { get; set; } = true;
        public string? MainCharacterName { get; set; } = null; 
        public bool EnableMainCharacterOnly { get; set; } = false;
        public bool ShowMainCharacterCrown { get; set; } = true;
        public HashSet<string> BlockedGalleryProfiles { get; set; } = new();
        public float DesignPanelWidth { get; set; } = 300f;
        
        // Conflict Resolution
        public bool EnableConflictResolution { get; set; } = false;
        public bool RespectPenumbraInheritance { get; set; } = false;
        public HashSet<string> SecretModeBlacklistedMods { get; set; } = new();
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
        // Enhanced dialogue
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
        
        [JsonPropertyName("useFlagBasedDialogueOnly")]
        public bool UseFlagBasedDialogueOnly { get; set; } = true;

        // Name Replacement
        [JsonPropertyName("enableNameReplacement")]
        public bool EnableNameReplacement { get; set; } = false;

        [JsonPropertyName("nameReplacementNameplate")]
        public bool NameReplacementNameplate { get; set; } = true;

        [JsonPropertyName("nameReplacementChat")]
        public bool NameReplacementChat { get; set; } = true;

        [JsonPropertyName("nameReplacementPartyList")]
        public bool NameReplacementPartyList { get; set; } = true;

        [JsonPropertyName("hideFCTagInNameplate")]
        public bool HideFCTagInNameplate { get; set; } = false;

        /// <summary>Use simple solid glow instead of wave effect on nameplates (for compatibility).</summary>
        [JsonPropertyName("useSimpleNameplateGlow")]
        public bool UseSimpleNameplateGlow { get; set; } = false;

        // Shared Name Replacement
        [JsonPropertyName("enableSharedNameReplacement")]
        public bool EnableSharedNameReplacement { get; set; } = false;

        [JsonPropertyName("allowOthersToSeeMyCSName")]
        public bool AllowOthersToSeeMyCSName { get; set; } = false;

        // Reveal actual names keybind
        [JsonPropertyName("enableRevealActualNamesKeybind")]
        public bool EnableRevealActualNamesKeybind { get; set; } = false;

        [JsonPropertyName("revealActualNamesKey")]
        public RevealNamesKeyOption RevealActualNamesKey { get; set; } = RevealNamesKeyOption.Alt;

        /// <summary>Custom virtual key code for reveal names (0 = use RevealActualNamesKey enum instead).</summary>
        [JsonPropertyName("revealActualNamesCustomKey")]
        public int RevealActualNamesCustomKey { get; set; } = 0;

        /// <summary>Display name for the custom key.</summary>
        [JsonPropertyName("revealActualNamesCustomKeyName")]
        public string RevealActualNamesCustomKeyName { get; set; } = "";

        /// <summary>Modifier key for reveal names (0 = none, 0x11 = Ctrl, 0x10 = Shift, 0x12 = Alt).</summary>
        [JsonPropertyName("revealActualNamesModifier")]
        public int RevealActualNamesModifier { get; set; } = 0;

        /// <summary>Display name for the modifier key.</summary>
        [JsonPropertyName("revealActualNamesModifierName")]
        public string RevealActualNamesModifierName { get; set; } = "";

        public bool EnableRaceReplacement { get; set; } = false;
        public DateTime LastSeenAnnouncements { get; set; } = DateTime.MinValue;
        public bool ShowNSFWProfiles { get; set; } = false;
        public int LastAcceptedGalleryTOSVersion { get; set; } = 0;

        // Context menu
        [JsonPropertyName("showViewRPContextMenu")]
        public bool ShowViewRPContextMenu { get; set; } = true;

        [JsonPropertyName("showBlockUserContextMenu")]
        public bool ShowBlockUserContextMenu { get; set; } = true;

        [JsonPropertyName("showReportUserContextMenu")]
        public bool ShowReportUserContextMenu { get; set; } = true;

        // Blocked CS+ users
        [JsonPropertyName("blockedCSUsers")]
        public HashSet<string> BlockedCSUsers { get; set; } = new();

        // Migration
        public bool HasMigratedHonorificSilent { get; set; } = false;

        // Honorific supporter acknowledgment
        [JsonPropertyName("hasAcknowledgedHonorificSupport")]
        public bool HasAcknowledgedHonorificSupport { get; set; } = false;

        /// <summary>
        /// Features user has seen (versioned keys, e.g. "NameSync_v2.1").
        /// </summary>
        public HashSet<string> SeenFeatures { get; set; } = new();

        /// <summary>
        /// Show patch notes on startup after updates.
        /// </summary>
        [DefaultValue(true)]
        public bool ShowPatchNotesOnStartup { get; set; } = true;

        public Configuration(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        public static Configuration Load(IDalamudPluginInterface pluginInterface)
        {
            var config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration(pluginInterface);
            config.pluginInterface = pluginInterface;

            // Validate sort index
            if (config.CurrentSortIndex < 0 || config.CurrentSortIndex > 4)
                config.CurrentSortIndex = 0;

            // Ensure collections are initialized (may be null from older config deserialization)
            config.ThemePresets ??= new List<ThemePreset>();
            config.CustomTheme ??= new CustomThemeConfig();
            config.Characters ??= new List<Character>();
            config.KnownTags ??= new List<string>();
            config.FavoriteIconIds ??= new List<uint>();
            config.LastUsedCharacterByPlayer ??= new Dictionary<string, string>();
            config.CharacterAssignments ??= new Dictionary<string, string>();
            config.JobAssignments ??= new Dictionary<string, string>();
            config.LastUsedDesignByCharacter ??= new Dictionary<string, string>();
            config.FavoriteGalleryProfiles ??= new HashSet<string>();
            config.LikedGalleryProfiles ??= new HashSet<string>();
            config.FavoriteSnapshots ??= new List<FavoriteSnapshot>();
            config.BlockedGalleryProfiles ??= new HashSet<string>();
            config.SecretModeBlacklistedMods ??= new HashSet<string>();
            config.FollowedPlayers ??= new HashSet<string>();
            config.GearsetJobMapping ??= new Dictionary<uint, uint>();
            config.BlockedCSUsers ??= new HashSet<string>();
            config.SeenFeatures ??= new HashSet<string>();

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

        public enum RevealNamesKeyOption
        {
            Alt,
            Ctrl,
            Shift
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
            try
            {
                // Ensure pluginInterface is set
                if (pluginInterface == null)
                {
                    Plugin.Log.Warning("[Configuration.Save] pluginInterface is null, skipping save");
                    return;
                }

                // Auto-save to active preset (with null checks)
                if (SelectedTheme == ThemeSelection.Custom && !string.IsNullOrEmpty(ActivePresetName))
                {
                    // Ensure collections are initialized
                    ThemePresets ??= new List<ThemePreset>();
                    CustomTheme ??= new CustomThemeConfig();

                    var activePreset = ThemePresets.FirstOrDefault(p => p.Name == ActivePresetName);
                    if (activePreset != null)
                    {
                        activePreset.Config = CustomTheme.Clone();
                    }
                }

                pluginInterface.SavePluginConfig(this);
            }
            catch (Exception ex)
            {
                // Log but don't crash - file permission issues (antivirus, cloud sync, etc) shouldn't crash the UI
                Plugin.Log.Error($"[Configuration.Save] Failed to save configuration: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Custom theme colour and style overrides.
    /// </summary>
    [Serializable]
    public class CustomThemeConfig
    {
        /// <summary>
        /// Colour overrides (packed RGBA). Keys: "color.windowBg", "color.text", etc.
        /// </summary>
        public Dictionary<string, uint?> ColorOverrides { get; set; } = new();

        /// <summary>
        /// Background image path for main window.
        /// </summary>
        public string? BackgroundImagePath { get; set; }

        /// <summary>Background image opacity (0.0-1.0).</summary>
        public float BackgroundImageOpacity { get; set; } = 0.5f;

        /// <summary>Background zoom (0.5-3.0, 1.0 = fit).</summary>
        public float BackgroundImageZoom { get; set; } = 1.0f;

        /// <summary>Background X offset (-1.0 to 1.0).</summary>
        public float BackgroundImageOffsetX { get; set; } = 0f;

        /// <summary>Background Y offset (-1.0 to 1.0).</summary>
        public float BackgroundImageOffsetY { get; set; } = 0f;

        /// <summary>Favourite icon ID (0 = default Star).</summary>
        public int FavoriteIconId { get; set; } = 0;

        /// <summary>Use nameplate colour for card glow instead of custom colour.</summary>
        public bool UseNameplateColorForCardGlow { get; set; } = true;

        /// <summary>Deep copy for preset saving.</summary>
        public CustomThemeConfig Clone()
        {
            return new CustomThemeConfig
            {
                ColorOverrides = new Dictionary<string, uint?>(this.ColorOverrides),
                BackgroundImagePath = this.BackgroundImagePath,
                BackgroundImageOpacity = this.BackgroundImageOpacity,
                BackgroundImageZoom = this.BackgroundImageZoom,
                BackgroundImageOffsetX = this.BackgroundImageOffsetX,
                BackgroundImageOffsetY = this.BackgroundImageOffsetY,
                FavoriteIconId = this.FavoriteIconId,
                UseNameplateColorForCardGlow = this.UseNameplateColorForCardGlow
            };
        }

        /// <summary>Copy settings from another config.</summary>
        public void CopyFrom(CustomThemeConfig other)
        {
            this.ColorOverrides = new Dictionary<string, uint?>(other.ColorOverrides);
            this.BackgroundImagePath = other.BackgroundImagePath;
            this.BackgroundImageOpacity = other.BackgroundImageOpacity;
            this.BackgroundImageZoom = other.BackgroundImageZoom;
            this.BackgroundImageOffsetX = other.BackgroundImageOffsetX;
            this.BackgroundImageOffsetY = other.BackgroundImageOffsetY;
            this.FavoriteIconId = other.FavoriteIconId;
            this.UseNameplateColorForCardGlow = other.UseNameplateColorForCardGlow;
        }
    }

    /// <summary>Saved theme preset.</summary>
    [Serializable]
    public class ThemePreset
    {
        public string Name { get; set; } = "New Preset";
        public CustomThemeConfig Config { get; set; } = new();
    }

    /// <summary>Versioned feature keys for new-badge tracking.</summary>
    public static class FeatureKeys
    {
        public const string CustomTheme = "CustomTheme_v2.1";
        public const string NameSync = "NameSync_v2.1";
        public const string ExpandedRPProfile = "ExpandedRPProfile_v2.1";
        public const string JobAssignments = "JobAssignments_v2.1";
        public const string Honorific = "Honorific_v2.1";
    }
}

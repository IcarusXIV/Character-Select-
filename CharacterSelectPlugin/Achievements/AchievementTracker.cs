using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace CharacterSelectPlugin.Achievements
{
    /// <summary>
    /// Core achievement tracking logic. Hooks into plugin events, checks conditions, queues
    /// popups, and manages the retroactive scan for existing users. Instantiated once in Plugin.cs.
    /// </summary>
    public class AchievementTracker : IDisposable
    {
        private readonly Plugin plugin;
        private readonly Queue<string> notificationQueue = new();
        private DateTime lastNotificationTime = DateTime.MinValue;
        private const double NotificationCooldownSeconds = 1.5;

        // Dalamud notification manager (for toast notifications)
        private readonly INotificationManager? notificationManager;

        public AchievementTracker(Plugin plugin, INotificationManager? notificationManager = null)
        {
            this.plugin = plugin;
            this.notificationManager = notificationManager;
        }

        public void Dispose() { }

        // PUBLIC API - called from various code paths throughout CS+

        /// <summary>Called after a character is added to the list (new or imported).</summary>
        public void OnCharacterCreated()
        {
            int count = plugin.Characters.Count;
            TryUnlock("char_1", count >= 1);
            TryUnlock("char_5", count >= 5);
            TryUnlock("char_10", count >= 10);
            TryUnlock("char_25", count >= 25);
            TryUnlock("char_41", count >= 41);
            TryUnlock("char_50", count >= 50);
            // Hidden bonus - fires only at EXACTLY 69. Idempotent across
            // future calls; if the user goes past, no harm.
            TryUnlock("char_69", count == 69);
            TryUnlock("char_100", count >= 100);
        }

        /// <summary>Called after a design is added to any character.</summary>
        public void OnDesignCreated()
        {
            int count = plugin.Characters.Sum(c => c.Designs?.Count ?? 0);
            TryUnlock("design_1", count >= 1);
            TryUnlock("design_10", count >= 10);
            TryUnlock("design_25", count >= 25);
            TryUnlock("design_50", count >= 50);
            TryUnlock("design_100", count >= 100);
        }

        /// <summary>Called when a design folder is created.</summary>
        public void OnDesignFolderCreated()
        {
            TryUnlock("design_folder", true);
        }

        /// <summary>Called when a character is applied from the MAIN WINDOW grid.</summary>
        public void OnSwitchFromMainWindow()
        {
            TryUnlock("switch_main", true);
        }

        /// <summary>Called when a character is applied from QUICK SWITCH.</summary>
        public void OnSwitchFromQuickSwitch()
        {
            TryUnlock("switch_quick", true);
        }

        /// <summary>Called when a character is applied via the /select COMMAND.</summary>
        public void OnSwitchFromCommand()
        {
            TryUnlock("switch_command", true);
        }

        /// <summary>Called when /select random is used.</summary>
        public void OnRandomUsed()
        {
            TryUnlock("switch_random", true);
        }

        /// <summary>Called when an RP profile is saved with a bio.</summary>
        public void OnProfileUpdated(Character character)
        {
            var rp = character.RPProfile;
            if (rp == null) return;

            TryUnlock("profile_bio", !string.IsNullOrWhiteSpace(rp.Bio));
            TryUnlock("profile_bg", !string.IsNullOrWhiteSpace(rp.BackgroundImage) || !string.IsNullOrWhiteSpace(rp.BackgroundImageUrl) || !string.IsNullOrWhiteSpace(rp.RPBackgroundImageUrl));

            int leftBoxes = rp.LeftContentBoxes?.Count ?? 0;
            int rightBoxes = rp.RightContentBoxes?.Count ?? 0;
            TryUnlock("profile_boxes", leftBoxes + rightBoxes >= 3);

            bool hasEffect = rp.Effects != null && (rp.Effects.CircuitBoard || rp.Effects.Fireflies ||
                rp.Effects.FallingLeaves || rp.Effects.Butterflies || rp.Effects.Bats ||
                rp.Effects.Fire || rp.Effects.Smoke);
            TryUnlock("profile_effect", hasEffect);
        }

        /// <summary>Called when a profile image is set.</summary>
        public void OnProfileImageSet()
        {
            TryUnlock("profile_image", true);
        }

        /// <summary>Called when a profile is uploaded to the gallery (ShowcasePublic).</summary>
        public void OnGalleryUpload()
        {
            TryUnlock("profile_gallery", true);
        }

        /// <summary>Called when a character assignment is set.</summary>
        public void OnAssignmentSet()
        {
            TryUnlock("auto_assignment", true);
        }

        /// <summary>Called when a job assignment is set.</summary>
        public void OnJobAssignmentSet()
        {
            TryUnlock("auto_job", true);
        }

        /// <summary>Called when a random group is created.</summary>
        public void OnRandomGroupCreated()
        {
            TryUnlock("auto_group", true);
        }

        /// <summary>Called when advanced mode is enabled on a character.</summary>
        public void OnAdvancedModeUsed()
        {
            TryUnlock("auto_macro", true);
        }

        /// <summary>Called when Name Sync is enabled.</summary>
        public void OnNameSyncEnabled()
        {
            TryUnlock("social_namesync", true);
        }

        /// <summary>Called when shared name visibility is enabled.</summary>
        public void OnSharedNameEnabled()
        {
            TryUnlock("social_seen", true);
        }

        /// <summary>Called periodically with the user's current total like count.</summary>
        public void CheckLikeCount(int totalLikes)
        {
            TryUnlock("social_likes_1", totalLikes >= 1);
            TryUnlock("social_likes_10", totalLikes >= 10);
            TryUnlock("social_likes_50", totalLikes >= 50);
        }

        /// <summary>Called when the user views someone else's RP profile.</summary>
        public void OnViewRPProfile()
        {
            TryUnlock("social_viewrp", true);
        }

        /// <summary>Called when the user likes a gallery profile.</summary>
        public void OnGalleryLike()
        {
            TryUnlock("social_like", true);
        }

        /// <summary>Called when a custom theme is selected.</summary>
        public void OnCustomThemeSet()
        {
            TryUnlock("custom_theme", true);
        }

        /// <summary>Called when a seasonal theme is selected.</summary>
        public void OnSeasonalThemeSet()
        {
            TryUnlock("custom_seasonal", true);
        }

        /// <summary>Called when a custom favourite icon is set.</summary>
        public void OnCustomIconSet()
        {
            TryUnlock("custom_icon", true);
        }

        /// <summary>Called when a Character Alias is set.</summary>
        public void OnAliasSet()
        {
            TryUnlock("custom_alias", true);
        }

        /// <summary>Called when a character is marked as favourite.</summary>
        public void OnFavouriteSet()
        {
            TryUnlock("discover_fav", true);
        }

        /// <summary>Called when an idle pose is set.</summary>
        public void OnPoseSet()
        {
            TryUnlock("discover_pose", true);
        }

        /// <summary>Called when /select save is used.</summary>
        public void OnSnapshotUsed()
        {
            TryUnlock("discover_snapshot", true);
        }

        /// <summary>Called when a manual backup is created.</summary>
        public void OnBackupCreated()
        {
            TryUnlock("discover_backup", true);
        }

        /// <summary>Called when the Features Guide window is opened.</summary>
        public void OnFeaturesGuideOpened()
        {
            TryUnlock("discover_features", true);
        }

        // ── New achievement triggers ──

        /// <summary>Called when a design is applied.</summary>
        public void OnDesignApplied() => TryUnlock("switch_design", true);

        /// <summary>Called when a design is imported from another character.</summary>
        public void OnDesignImported() => TryUnlock("design_import", true);

        /// <summary>Called when a preview image is set on a design.</summary>
        public void OnDesignPreviewSet() => TryUnlock("design_preview", true);

        /// <summary>Called when all CS+ changes are reverted.</summary>
        public void OnRevert() => TryUnlock("switch_revert", true);

        /// <summary>Called when pronouns are set on a character.</summary>
        public void OnPronounsSet() => TryUnlock("profile_pronouns", true);

        /// <summary>Called when a nameplate colour is set.</summary>
        public void OnNameplateColorSet() => TryUnlock("profile_color", true);

        /// <summary>Called when a Connection is added to an RP profile.</summary>
        public void OnConnectionAdded() => TryUnlock("profile_connection", true);

        /// <summary>Called when a Title or Status is set on an RP profile.</summary>
        public void OnProfileTitleSet() => TryUnlock("profile_title", true);

        /// <summary>Called when the user views their own RP profile.</summary>
        public void OnViewSelfProfile() => TryUnlock("social_viewself", true);

        /// <summary>Called when the user follows someone in the gallery.</summary>
        public void OnGalleryFollow() => TryUnlock("social_follow", true);

        /// <summary>Called when the user favourites a gallery profile.</summary>
        public void OnGalleryFavourite() => TryUnlock("social_fav_gallery", true);

        /// <summary>Called when gearset assignments are enabled.</summary>
        public void OnGearsetAssignmentsEnabled() => TryUnlock("auto_gearset", true);

        /// <summary>Called when a Glamourer Automation is set.</summary>
        public void OnGlamourerAutomationSet() => TryUnlock("auto_glamauto", true);

        /// <summary>Called when Immersive Dialogue is enabled.</summary>
        public void OnImmersiveDialogueEnabled() => TryUnlock("auto_dialogue", true);

        /// <summary>Called when Conflict Resolution is enabled.</summary>
        public void OnConflictResolutionEnabled() => TryUnlock("auto_cr", true);

        /// <summary>Called when a custom background image is set.</summary>
        public void OnCustomBgImageSet() => TryUnlock("custom_bgimage", true);

        /// <summary>Called when a theme preset is saved.</summary>
        public void OnThemePresetSaved() => TryUnlock("custom_preset", true);

        /// <summary>Called when tags are added to a character.</summary>
        public void OnTagsUsed() => TryUnlock("discover_tags", true);

        /// <summary>Called when a Main Character is set.</summary>
        public void OnMainCharacterSet() => TryUnlock("discover_main", true);

        /// <summary>Called when the Mod Manager is opened.</summary>
        public void OnModManagerOpened() => TryUnlock("discover_mods", true);

        /// <summary>Called when the Gallery is opened.</summary>
        public void OnGalleryOpened() => TryUnlock("discover_gallery", true);

        /// <summary>Called when the Patch Notes are viewed.</summary>
        public void OnPatchNotesViewed() => TryUnlock("discover_patchnotes", true);

        /// <summary>Called when characters are reordered.</summary>
        public void OnCharactersReordered() => TryUnlock("discover_reorder", true);

        // ── New batch (24 additions) ──

        /// <summary>Called when a Honorific title is set on a character.</summary>
        public void OnHonorificTitleSet() => TryUnlock("integration_honorific", true);

        /// <summary>Called when a Customize+ profile is set on a character.</summary>
        public void OnCustomizePlusSet() => TryUnlock("integration_customize", true);

        /// <summary>Called when a Customize+ profile is set on a specific design.</summary>
        public void OnPerDesignCustomizePlusSet() => TryUnlock("integration_customize_design", true);

        /// <summary>Called when a two-colour Honorific gradient is configured.</summary>
        public void OnTwoColourGradientSet() => TryUnlock("integration_gradient", true);

        /// <summary>Called when a character has Glamourer + Customize+ + Honorific all set.</summary>
        public void OnTripleIntegrationSet() => TryUnlock("integration_triple", true);

        /// <summary>Called when the user completes the in-plugin tutorial.</summary>
        public void OnTutorialCompleted() => TryUnlock("discover_tutorial", true);

        /// <summary>Called when the user picks a seasonal theme. Tracks distinct themes used.</summary>
        public void OnSeasonalThemeUsed(string themeName)
        {
            var data = plugin.Configuration.AchievementData;
            if (data.SeasonalThemesUsed.Add(themeName))
                plugin.SaveConfiguration();
            TryUnlock("custom_seasonal_3", data.SeasonalThemesUsed.Count >= 3);
        }

        /// <summary>Called when an RP bio is saved with 500+ characters.</summary>
        public void OnLongBioWritten() => TryUnlock("profile_bio_long", true);

        /// <summary>Called when a character has a complete RP profile (bio, pronouns, image, background, content box).</summary>
        public void OnProfileCompleted() => TryUnlock("profile_complete", true);

        /// <summary>Called when 5+ distinct content box layout types have been used across all characters.</summary>
        public void OnLayoutTypesExplored() => TryUnlock("profile_layouts", true);

        /// <summary>Called when 6+ content boxes are present on a single character.</summary>
        public void OnSixContentBoxes() => TryUnlock("profile_boxes_6", true);

        /// <summary>Called when a banner image is set on an RP profile.</summary>
        public void OnBannerImageSet() => TryUnlock("profile_banner", true);

        /// <summary>Called when a custom URL background is set on an RP profile.</summary>
        public void OnUrlBackgroundSet() => TryUnlock("profile_url_bg", true);

        /// <summary>Called when a Timeline content box layout is used.</summary>
        public void OnTimelineLayoutUsed() => TryUnlock("profile_layout_timeline", true);

        /// <summary>Called when a Quote content box layout is used.</summary>
        public void OnQuoteLayoutUsed() => TryUnlock("profile_layout_quote", true);

        /// <summary>Called when a sit/ground sit/doze pose is set via /ssit, /sgroundsit, or /sdoze.</summary>
        public void OnPoseChatCommandUsed() => TryUnlock("discover_pose_cmd", true);

        /// <summary>Called when per-design mod option overrides are set.</summary>
        public void OnPerDesignModOptionsSet() => TryUnlock("auto_modoptions", true);

        /// <summary>Called when a CS+ character is applied to a GPose target.</summary>
        public void OnGPoseTargetApply() => TryUnlock("discover_gpose", true);

        /// <summary>Called after each character switch. Checks if all 3 switching methods have been used.</summary>
        public void CheckSwitchMethodsAll()
        {
            var data = plugin.Configuration.AchievementData;
            bool main    = data.IsUnlocked("switch_main");
            bool quick   = data.IsUnlocked("switch_quick");
            bool command = data.IsUnlocked("switch_command");
            TryUnlock("switch_all_methods", main && quick && command);
        }

        /// <summary>Called when character assignments are saved. Checks for the 5-assignments milestone.</summary>
        public void CheckAssignmentCount()
        {
            int count = plugin.Configuration.CharacterAssignments?.Count ?? 0;
            TryUnlock("auto_assignment_5", count >= 5);
        }

        /// <summary>Called when job assignments are saved. Checks for the 3-jobs milestone.</summary>
        public void CheckJobAssignmentCount()
        {
            // Only count Job_ prefixed entries (not Role_ which are role-based shortcuts)
            int distinctJobs = plugin.Configuration.JobAssignments?
                .Keys.Count(k => k.StartsWith("Job_")) ?? 0;
            TryUnlock("auto_job_3", distinctJobs >= 3);
        }

        /// <summary>Called after each new unlock. Checks meta progress (50% / 100%).</summary>
        public void CheckMetaProgress()
        {
            var data = plugin.Configuration.AchievementData;
            // Meta progress only counts CORE achievements (bonus tier is extra/optional).
            // Also exclude the two meta achievements themselves to avoid feedback loops
            // where unlocking meta_halfway pushes you over the 50% line.
            var qualifying = AchievementRegistry.All
                .Where(a => !a.IsBonus && a.Id != "meta_halfway" && a.Id != "meta_completionist")
                .ToList();
            int totalQualifying = qualifying.Count;
            int unlockedQualifying = qualifying.Count(a => data.IsUnlocked(a.Id));
            TryUnlock("meta_halfway", unlockedQualifying * 2 >= totalQualifying);
            TryUnlock("meta_completionist", unlockedQualifying >= totalQualifying);
        }

        /// <summary>Called when the Wardrobe is opened.</summary>
        public void OnWardrobeOpened() => TryUnlock("discover_wardrobe", true);

        /// <summary>Called when the in-game file browser is used to pick a file.</summary>
        public void OnFileBrowserUsed() => TryUnlock("discover_filebrowser", true);

        // RETROACTIVE SCAN - runs once on first load after the system is added

        /// <summary>
        /// Scans existing config state to award achievements the user already qualifies for.
        /// Only runs once (guarded by HasCompletedRetroactiveScan). Silently queues unlocks
        /// without individual notifications. A summary is shown later.
        /// </summary>
        // Bump this when adding new achievements so existing users get a re-scan
        private const int CurrentScanVersion = 4;

        public void RunRetroactiveScan()
        {
            var data = plugin.Configuration.AchievementData;
            // Re-run if version is outdated (new achievements added since last scan)
            if (data.HasCompletedRetroactiveScan && data.RetroactiveScanVersion >= CurrentScanVersion) return;

            Plugin.Log.Info("[Achievements] Running retroactive scan...");

            var retroUnlocks = new List<string>();

            // Characters
            int charCount = plugin.Characters.Count;
            if (charCount >= 1 && data.TryUnlock("char_1")) retroUnlocks.Add("char_1");
            if (charCount >= 5 && data.TryUnlock("char_5")) retroUnlocks.Add("char_5");
            if (charCount >= 10 && data.TryUnlock("char_10")) retroUnlocks.Add("char_10");
            if (charCount >= 25 && data.TryUnlock("char_25")) retroUnlocks.Add("char_25");
            if (charCount >= 41 && data.TryUnlock("char_41")) retroUnlocks.Add("char_41");
            if (charCount >= 50 && data.TryUnlock("char_50")) retroUnlocks.Add("char_50");
            // Exact-match Easter egg - only retro-fires if user happens to
            // be sitting at exactly 69 right now. If they've gone past, miss.
            if (charCount == 69 && data.TryUnlock("char_69")) retroUnlocks.Add("char_69");
            if (charCount >= 100 && data.TryUnlock("char_100")) retroUnlocks.Add("char_100");

            // Designs
            int designCount = plugin.Characters.Sum(c => c.Designs?.Count ?? 0);
            if (designCount >= 1 && data.TryUnlock("design_1")) retroUnlocks.Add("design_1");
            if (designCount >= 10 && data.TryUnlock("design_10")) retroUnlocks.Add("design_10");
            if (designCount >= 25 && data.TryUnlock("design_25")) retroUnlocks.Add("design_25");
            if (designCount >= 50 && data.TryUnlock("design_50")) retroUnlocks.Add("design_50");
            if (designCount >= 100 && data.TryUnlock("design_100")) retroUnlocks.Add("design_100");

            bool hasFolders = plugin.Characters.Any(c => c.DesignFolders?.Count > 0);
            if (hasFolders && data.TryUnlock("design_folder")) retroUnlocks.Add("design_folder");

            bool hasDesignPreview = plugin.Characters.Any(c => c.Designs?.Any(d => !string.IsNullOrWhiteSpace(d.PreviewImagePath)) == true);
            if (hasDesignPreview && data.TryUnlock("design_preview")) retroUnlocks.Add("design_preview");

            // Profiles - scan all characters for existing RP data
            foreach (var character in plugin.Characters)
            {
                var rp = character.RPProfile;
                if (rp == null) continue;

                if (!string.IsNullOrWhiteSpace(rp.Bio) && data.TryUnlock("profile_bio"))
                    retroUnlocks.Add("profile_bio");
                if (!string.IsNullOrWhiteSpace(character.ImagePath) && data.TryUnlock("profile_image"))
                    retroUnlocks.Add("profile_image");
                if ((!string.IsNullOrWhiteSpace(rp.BackgroundImage) || !string.IsNullOrWhiteSpace(rp.BackgroundImageUrl)) && data.TryUnlock("profile_bg"))
                    retroUnlocks.Add("profile_bg");

                int boxes = (rp.LeftContentBoxes?.Count ?? 0) + (rp.RightContentBoxes?.Count ?? 0);
                if (boxes >= 3 && data.TryUnlock("profile_boxes"))
                    retroUnlocks.Add("profile_boxes");

                bool hasEffect = rp.Effects != null && (rp.Effects.CircuitBoard || rp.Effects.Fireflies ||
                    rp.Effects.FallingLeaves || rp.Effects.Butterflies || rp.Effects.Bats ||
                    rp.Effects.Fire || rp.Effects.Smoke);
                if (hasEffect && data.TryUnlock("profile_effect"))
                    retroUnlocks.Add("profile_effect");

                if (rp.Sharing == ProfileSharing.ShowcasePublic && data.TryUnlock("profile_gallery"))
                    retroUnlocks.Add("profile_gallery");
            }

            // Config-based feature checks
            if (plugin.Characters.Any(c => c.IsFavorite) && data.TryUnlock("discover_fav"))
                retroUnlocks.Add("discover_fav");
            if (plugin.Characters.Any(c => c.IdlePoseIndex < 7) && data.TryUnlock("discover_pose"))
                retroUnlocks.Add("discover_pose");
            if (plugin.Characters.Any(c => c.IsAdvancedMode) && data.TryUnlock("auto_macro"))
                retroUnlocks.Add("auto_macro");
            if (plugin.Characters.Any(c => !string.IsNullOrWhiteSpace(c.Alias)) && data.TryUnlock("custom_alias"))
                retroUnlocks.Add("custom_alias");

            if (plugin.Configuration.CharacterAssignments.Any() && data.TryUnlock("auto_assignment"))
                retroUnlocks.Add("auto_assignment");
            if (plugin.Configuration.EnableJobAssignments && plugin.Configuration.JobAssignments.Any() && data.TryUnlock("auto_job"))
                retroUnlocks.Add("auto_job");
            if (plugin.Configuration.RandomGroups?.Any() == true && data.TryUnlock("auto_group"))
                retroUnlocks.Add("auto_group");

            if (plugin.Configuration.EnableNameReplacement && data.TryUnlock("social_namesync"))
                retroUnlocks.Add("social_namesync");
            if (plugin.Configuration.AllowOthersToSeeMyCSName && data.TryUnlock("social_seen"))
                retroUnlocks.Add("social_seen");

            if (plugin.Configuration.SelectedTheme == ThemeSelection.Custom && data.TryUnlock("custom_theme"))
                retroUnlocks.Add("custom_theme");

            // New retroactive checks
            foreach (var character in plugin.Characters)
            {
                var rp = character.RPProfile;
                if (rp != null)
                {
                    if (!string.IsNullOrWhiteSpace(rp.Pronouns) && data.TryUnlock("profile_pronouns"))
                        retroUnlocks.Add("profile_pronouns");
                    if (!string.IsNullOrWhiteSpace(rp.Title) || !string.IsNullOrWhiteSpace(rp.Status))
                        if (data.TryUnlock("profile_title")) retroUnlocks.Add("profile_title");

                    bool hasConnection = (rp.LeftContentBoxes?.Any(b => b.LayoutType == ContentBoxLayoutType.Connections) ?? false) ||
                                         (rp.RightContentBoxes?.Any(b => b.LayoutType == ContentBoxLayoutType.Connections) ?? false);
                    if (hasConnection && data.TryUnlock("profile_connection"))
                        retroUnlocks.Add("profile_connection");
                }

                if (character.NameplateColor != default && data.TryUnlock("profile_color"))
                    retroUnlocks.Add("profile_color");
                if (character.Tags?.Count > 0 && data.TryUnlock("discover_tags"))
                    retroUnlocks.Add("discover_tags");
                if (!string.IsNullOrWhiteSpace(character.CharacterAutomation) && data.TryUnlock("auto_glamauto"))
                    retroUnlocks.Add("auto_glamauto");
            }

            if (plugin.Configuration.EnableGearsetAssignments && data.TryUnlock("auto_gearset"))
                retroUnlocks.Add("auto_gearset");
            if (plugin.Configuration.EnableDialogueIntegration && data.TryUnlock("auto_dialogue"))
                retroUnlocks.Add("auto_dialogue");
            if (plugin.Configuration.EnableConflictResolution && data.TryUnlock("auto_cr"))
                retroUnlocks.Add("auto_cr");
            if (!string.IsNullOrWhiteSpace(plugin.Configuration.MainCharacterName) && data.TryUnlock("discover_main"))
                retroUnlocks.Add("discover_main");
            if (plugin.Configuration.CustomTheme?.BackgroundImagePath != null && data.TryUnlock("custom_bgimage"))
                retroUnlocks.Add("custom_bgimage");
            if (plugin.Configuration.ThemePresets?.Count > 0 && data.TryUnlock("custom_preset"))
                retroUnlocks.Add("custom_preset");
            if (plugin.Configuration.FollowedPlayers?.Count > 0 && data.TryUnlock("social_follow"))
                retroUnlocks.Add("social_follow");
            if (plugin.Configuration.FavoriteSnapshots?.Count > 0 && data.TryUnlock("social_fav_gallery"))
                retroUnlocks.Add("social_fav_gallery");

            // ── Scan v4 additions ──
            // Plugin integrations
            if (plugin.Characters.Any(c => !string.IsNullOrWhiteSpace(c.HonorificTitle)) && data.TryUnlock("integration_honorific"))
                retroUnlocks.Add("integration_honorific");
            if (plugin.Characters.Any(c => !string.IsNullOrWhiteSpace(c.CustomizeProfile)) && data.TryUnlock("integration_customize"))
                retroUnlocks.Add("integration_customize");
            if (plugin.Characters.Any(c => c.Designs?.Any(d => !string.IsNullOrWhiteSpace(d.CustomizePlusProfile)) == true) && data.TryUnlock("integration_customize_design"))
                retroUnlocks.Add("integration_customize_design");
            if (plugin.Characters.Any(c => c.HonorificGradientSet == -1) && data.TryUnlock("integration_gradient"))
                retroUnlocks.Add("integration_gradient");
            // Triple integration: any character with all three plugin fields set
            if (plugin.Characters.Any(c =>
                    !string.IsNullOrWhiteSpace(c.GlamourerDesign)
                 && !string.IsNullOrWhiteSpace(c.CustomizeProfile)
                 && !string.IsNullOrWhiteSpace(c.HonorificTitle))
                && data.TryUnlock("integration_triple"))
                retroUnlocks.Add("integration_triple");

            // Tutorial completion
            if (plugin.Configuration.HasSeenTutorial && data.TryUnlock("discover_tutorial"))
                retroUnlocks.Add("discover_tutorial");

            // ERP profile depth
            // Long bio
            if (plugin.Characters.Any(c => (c.RPProfile?.Bio?.Length ?? 0) >= 500) && data.TryUnlock("profile_bio_long"))
                retroUnlocks.Add("profile_bio_long");

            // Fully Realised - composite
            bool hasComplete = plugin.Characters.Any(c =>
            {
                var rp = c.RPProfile;
                if (rp == null) return false;
                bool hasBio = !string.IsNullOrWhiteSpace(rp.Bio);
                bool hasPronouns = !string.IsNullOrWhiteSpace(rp.Pronouns);
                bool hasImage = !string.IsNullOrWhiteSpace(c.ImagePath);
                bool hasBg = !string.IsNullOrWhiteSpace(rp.BackgroundImage)
                          || !string.IsNullOrWhiteSpace(rp.BackgroundImageUrl)
                          || !string.IsNullOrWhiteSpace(rp.RPBackgroundImageUrl);
                bool hasBox = (rp.LeftContentBoxes?.Count ?? 0) + (rp.RightContentBoxes?.Count ?? 0) > 0;
                return hasBio && hasPronouns && hasImage && hasBg && hasBox;
            });
            if (hasComplete && data.TryUnlock("profile_complete"))
                retroUnlocks.Add("profile_complete");

            // Layout types - count distinct ContentBoxLayoutType across all characters
            var distinctLayouts = new HashSet<ContentBoxLayoutType>();
            foreach (var c in plugin.Characters)
            {
                var rp = c.RPProfile;
                if (rp == null) continue;
                if (rp.LeftContentBoxes != null)
                    foreach (var b in rp.LeftContentBoxes) distinctLayouts.Add(b.LayoutType);
                if (rp.RightContentBoxes != null)
                    foreach (var b in rp.RightContentBoxes) distinctLayouts.Add(b.LayoutType);
            }
            if (distinctLayouts.Count >= 5 && data.TryUnlock("profile_layouts"))
                retroUnlocks.Add("profile_layouts");
            if (distinctLayouts.Contains(ContentBoxLayoutType.Timeline) && data.TryUnlock("profile_layout_timeline"))
                retroUnlocks.Add("profile_layout_timeline");
            if (distinctLayouts.Contains(ContentBoxLayoutType.Quote) && data.TryUnlock("profile_layout_quote"))
                retroUnlocks.Add("profile_layout_quote");

            // 6+ content boxes on a single character
            if (plugin.Characters.Any(c =>
                    ((c.RPProfile?.LeftContentBoxes?.Count ?? 0) + (c.RPProfile?.RightContentBoxes?.Count ?? 0)) >= 6)
                && data.TryUnlock("profile_boxes_6"))
                retroUnlocks.Add("profile_boxes_6");

            // Banner image
            if (plugin.Characters.Any(c => !string.IsNullOrWhiteSpace(c.RPProfile?.BannerImagePath)) && data.TryUnlock("profile_banner"))
                retroUnlocks.Add("profile_banner");

            // URL background
            if (plugin.Characters.Any(c =>
                    !string.IsNullOrWhiteSpace(c.RPProfile?.BackgroundImageUrl)
                 || !string.IsNullOrWhiteSpace(c.RPProfile?.RPBackgroundImageUrl))
                && data.TryUnlock("profile_url_bg"))
                retroUnlocks.Add("profile_url_bg");

            // Per-design mod options
            if (plugin.Characters.Any(c => c.Designs?.Any(d => d.ModOptionSettings?.Count > 0) == true) && data.TryUnlock("auto_modoptions"))
                retroUnlocks.Add("auto_modoptions");

            // Tiered milestones
            int assignmentCount = plugin.Configuration.CharacterAssignments?.Count ?? 0;
            if (assignmentCount >= 5 && data.TryUnlock("auto_assignment_5"))
                retroUnlocks.Add("auto_assignment_5");

            int distinctJobAssignments = plugin.Configuration.JobAssignments?
                .Keys.Count(k => k.StartsWith("Job_")) ?? 0;
            if (distinctJobAssignments >= 3 && data.TryUnlock("auto_job_3"))
                retroUnlocks.Add("auto_job_3");

            // Why Pick One? - composite based on the three switching achievements
            if (data.IsUnlocked("switch_main") && data.IsUnlocked("switch_quick") && data.IsUnlocked("switch_command")
                && data.TryUnlock("switch_all_methods"))
                retroUnlocks.Add("switch_all_methods");

            // Mark scan complete with version
            data.HasCompletedRetroactiveScan = true;
            data.RetroactiveScanVersion = CurrentScanVersion;
            data.RetroactiveUnlocks = retroUnlocks;
            plugin.SaveConfiguration();

            Plugin.Log.Info($"[Achievements] Retroactive scan v{CurrentScanVersion} complete: {retroUnlocks.Count} achievements awarded ({data.TotalPointsEarned} pts)");
        }

        /// <summary>
        /// Show the retroactive scan summary in chat (once). Called from FrameworkUpdate
        /// after login is complete so the chat UI is ready.
        /// </summary>
        public void TryShowRetroactiveSummary()
        {
            var data = plugin.Configuration.AchievementData;
            if (data.HasShownRetroactiveSummary) return;
            if (!plugin.Configuration.EnableAchievementSystem) return;
            if (data.RetroactiveUnlocks == null || data.RetroactiveUnlocks.Count == 0)
            {
                data.HasShownRetroactiveSummary = true;
                plugin.SaveConfiguration();
                return;
            }

            // Show summary in chat (gated by chat toggle so users who muted chat
            // notifications don't get a one-off welcome line either)
            if (plugin.Configuration.ShowAchievementChatMessages)
            {
                int count = data.RetroactiveUnlocks.Count;
                int points = data.TotalPointsEarned;

                var msg = new SeStringBuilder()
                    .AddUiForeground("[CS+] ", 35)
                    .AddUiForeground("\u2605 ", 45)
                    .AddText($"Welcome back! You've earned ")
                    .AddUiForeground($"{count} achievement{(count != 1 ? "s" : "")}", 45)
                    .AddText($" ({points} pts). Click the trophy to view them!")
                    .Build();

                Plugin.ChatGui.Print(new XivChatEntry { Message = msg, Type = XivChatType.Echo });
            }

            data.HasShownRetroactiveSummary = true;
            plugin.SaveConfiguration();
        }

        // NOTIFICATION QUEUE - processes queued achievement notifications

        /// <summary>
        /// Called from FrameworkUpdate. Processes the notification queue with a cooldown
        /// between each so they don't spam the user.
        /// </summary>
        public void ProcessNotificationQueue()
        {
            if (notificationQueue.Count == 0) return;

            // Master gate - when achievements are disabled entirely, drop everything
            // pending so re-enabling later starts clean.
            if (!plugin.Configuration.EnableAchievementSystem)
            {
                notificationQueue.Clear();
                return;
            }

            if ((DateTime.UtcNow - lastNotificationTime).TotalSeconds < NotificationCooldownSeconds) return;

            string achievementId = notificationQueue.Dequeue();
            var def = AchievementRegistry.Get(achievementId);
            if (def == null) return;

            lastNotificationTime = DateTime.UtcNow;

            // Chat notification (opt-out via settings)
            if (plugin.Configuration.ShowAchievementChatMessages)
            {
                var chatMsg = new SeStringBuilder()
                    .AddUiForeground("[CS+] ", 35)
                    .AddUiForeground("\u2605 ", 45)
                    .AddText("Achievement Unlocked: ")
                    .AddUiForeground(def.Name, 45)
                    .AddText($" (+{def.Points} pts)")
                    .Build();

                Plugin.ChatGui.Print(new XivChatEntry { Message = chatMsg, Type = XivChatType.Echo });
            }

            // CS+ toast notification (opt-out via settings)
            if (plugin.Configuration.ShowAchievementNotifications)
            {
                try
                {
                    plugin.AchievementToast?.Enqueue(def);
                }
                catch (Exception ex)
                {
                    Plugin.Log.Debug($"[Achievements] Toast enqueue failed: {ex.Message}");
                }
            }

            Plugin.Log.Info($"[Achievements] Notified: {def.Name} ({def.Id}, +{def.Points} pts)");
        }

        // INTERNAL

        /// <summary>Try to unlock an achievement if the condition is met. Queues notification on success.</summary>
        private void TryUnlock(string achievementId, bool conditionMet)
        {
            if (!conditionMet) return;

            var data = plugin.Configuration.AchievementData;
            if (!data.TryUnlock(achievementId)) return;

            plugin.SaveConfiguration();

            // Don't notify during retroactive scan, summary handles that
            if (!data.HasCompletedRetroactiveScan) return;

            notificationQueue.Enqueue(achievementId);
            Plugin.Log.Info($"[Achievements] Unlocked: {achievementId}");

            // After every successful unlock, check meta progress (Halfway / Completionist).
            // Guarded against re-entry on the meta IDs themselves.
            if (achievementId != "meta_halfway" && achievementId != "meta_completionist")
                CheckMetaProgress();
        }
    }
}

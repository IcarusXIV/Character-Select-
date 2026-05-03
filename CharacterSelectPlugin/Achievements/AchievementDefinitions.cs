using System.Collections.Generic;
using System.Linq;

namespace CharacterSelectPlugin.Achievements
{
    public enum AchievementCategory
    {
        Characters,
        Designs,
        Profiles,
        Switching,
        Automation,
        Social,
        Customization,
        Discovery
    }

    public enum AchievementTier
    {
        Bronze,
        Silver,
        Gold,
        Platinum
    }

    public class AchievementDefinition
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        /// <summary>Imperative description shown while the achievement is locked. e.g. "Enable Immersive Dialogue."</summary>
        public string Description { get; init; } = "";
        /// <summary>
        /// Optional past-tense variant shown after unlock. e.g. "Enabled Immersive Dialogue."
        /// Also used as an opportunity to shorten descriptions that would overflow narrow
        /// surfaces like the achievement toast. Falls back to <see cref="Description"/> when null.
        /// </summary>
        public string? CompletedDescription { get; init; } = null;
        public string FlavourText { get; init; } = "";
        /// <summary>
        /// Optional hint shown in the tooltip explaining where/how to complete the achievement.
        /// Use for feature discovery - settings paths, chat commands, button locations.
        /// Leave null for self-explanatory achievements (e.g. "create your first character").
        /// </summary>
        public string? Hint { get; init; } = null;
        public AchievementCategory Category { get; init; }
        public AchievementTier Tier { get; init; }
        public int Points { get; init; }
        public bool IsHidden { get; init; } = false;
        /// <summary>Optional/grind achievements that award points but don't count toward core progression or meta unlocks.</summary>
        public bool IsBonus { get; init; } = false;

        /// <summary>
        /// Returns the description appropriate for the unlock state. Uses
        /// <see cref="CompletedDescription"/> when unlocked and set, otherwise falls
        /// back to the imperative <see cref="Description"/>.
        /// </summary>
        public string GetDescriptionFor(bool unlocked) =>
            unlocked && !string.IsNullOrEmpty(CompletedDescription)
                ? CompletedDescription!
                : Description;
    }

    public static class AchievementRegistry
    {
        public static readonly AchievementDefinition[] All =
        {
            // ── Characters (7) - tiered milestone ──
            new() { Id = "char_1",   Name = "First Steps",           Description = "Create your first character.",            CompletedDescription = "Created your first character.",   FlavourText = "Everyone starts somewhere.",            Category = AchievementCategory.Characters, Tier = AchievementTier.Bronze,   Points = 2 },
            new() { Id = "char_5",   Name = "Getting Started",       Description = "Create 5 characters.",                    CompletedDescription = "Created 5 characters.",           FlavourText = "The roster grows.",                     Category = AchievementCategory.Characters, Tier = AchievementTier.Bronze,   Points = 5 },
            new() { Id = "char_10",  Name = "Growing Collection",    Description = "Create 10 characters.",                   CompletedDescription = "Created 10 characters.",          FlavourText = "Double digits.",                        Category = AchievementCategory.Characters, Tier = AchievementTier.Silver,   Points = 8 },
            new() { Id = "char_25",  Name = "Character Enthusiast",  Description = "Create 25 characters.",                   CompletedDescription = "Created 25 characters.",          FlavourText = "You might have a problem.",             Category = AchievementCategory.Characters, Tier = AchievementTier.Silver,   Points = 15, IsBonus = true },
            new() { Id = "char_41",  Name = "Who Am I Again?",       Description = "Create 41 characters.",                   CompletedDescription = "Created 41 characters.",          FlavourText = "One page wasn't enough.",               Category = AchievementCategory.Characters, Tier = AchievementTier.Gold,     Points = 20, IsHidden = false, IsBonus = true },
            new() { Id = "char_50",  Name = "Identity Crisis",       Description = "Create 50 characters.",                   CompletedDescription = "Created 50 characters.",          FlavourText = "At this point, who's counting?",        Category = AchievementCategory.Characters, Tier = AchievementTier.Gold,     Points = 25, IsBonus = true },
            new() { Id = "char_69",  Name = "Nice!",                 Description = "???",                                     CompletedDescription = "Reached exactly 69 characters.",  FlavourText = "Nice.",                                 Category = AchievementCategory.Characters, Tier = AchievementTier.Bronze,   Points = 6, IsBonus = true, IsHidden = true },
            new() { Id = "char_100", Name = "Centurion",             Description = "Create 100 characters.",                  CompletedDescription = "Created 100 characters.",         FlavourText = "A century of faces.",                   Category = AchievementCategory.Characters, Tier = AchievementTier.Platinum, Points = 50, IsHidden = false, IsBonus = true },

            // ── Designs (7) - tiered + one-offs ──
            new() { Id = "design_1",      Name = "First Outfit",       Description = "Create your first design.",             CompletedDescription = "Created your first design.",      FlavourText = "Looking good.",                         Category = AchievementCategory.Designs, Tier = AchievementTier.Bronze, Points = 2 },
            new() { Id = "design_10",     Name = "Fashion Forward",    Description = "Create 10 designs.",                    CompletedDescription = "Created 10 designs.",             FlavourText = "Choices, choices.",                     Category = AchievementCategory.Designs, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "design_25",     Name = "Walk-In Closet",     Description = "Create 25 designs.",                    CompletedDescription = "Created 25 designs.",             FlavourText = "You need a bigger wardrobe.",           Category = AchievementCategory.Designs, Tier = AchievementTier.Silver, Points = 10 },
            new() { Id = "design_50",     Name = "Fashionista",        Description = "Create 50 designs.",                    CompletedDescription = "Created 50 designs.",             FlavourText = "Fashion is your middle name.",          Category = AchievementCategory.Designs, Tier = AchievementTier.Gold,   Points = 20, IsBonus = true },
            new() { Id = "design_100",    Name = "Master Tailor",      Description = "Create 100 designs.",                   CompletedDescription = "Created 100 designs.",            FlavourText = "A wardrobe for every occasion.",        Category = AchievementCategory.Designs, Tier = AchievementTier.Platinum, Points = 30, IsBonus = true },
            new() { Id = "design_folder", Name = "Organized Dresser",  Description = "Create a design folder.",               CompletedDescription = "Created a design folder.",        FlavourText = "A place for everything.",               Hint = "Design Panel: New Folder button (or right-click empty space)", Category = AchievementCategory.Designs, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "design_import",  Name = "Borrowed Style",     Description = "Import a design from another character.", CompletedDescription = "Imported a design from another character.", FlavourText = "Sharing is caring.",                    Hint = "Design Panel: Shift+click the + Add Design button to open Import", Category = AchievementCategory.Designs, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "design_preview", Name = "Picture This",       Description = "Set a design preview image.",           FlavourText = "Imagine not being able to preview your outfits.", Hint = "Edit a design > Preview Image field", Category = AchievementCategory.Designs, Tier = AchievementTier.Bronze, Points = 3 },

            // ── Profiles (10) - one-offs for feature discovery ──
            new() { Id = "profile_bio",       Name = "Storyteller",      Description = "Fill out an RP bio.",                   CompletedDescription = "Filled out an RP bio.",           FlavourText = "Every character has a story.",          Category = AchievementCategory.Profiles, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "profile_image",     Name = "Picture Perfect",  Description = "Add a profile image.",                  CompletedDescription = "Added a profile image.",          FlavourText = "A face to the name.",                  Category = AchievementCategory.Profiles, Tier = AchievementTier.Bronze, Points = 3 },
            new() { Id = "profile_bg",        Name = "Setting the Scene",Description = "Set an RP profile background.",         FlavourText = "Atmosphere matters.",                   Hint = "RP Profile editor > Background dropdown (80+ presets)", Category = AchievementCategory.Profiles, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "profile_boxes",     Name = "Content Creator",  Description = "Add 3 or more content boxes.",          CompletedDescription = "Added 3 content boxes.",          FlavourText = "Building out the details.",             Hint = "Expanded RP Profile editor > Add Content Box (do this 3 times). Content boxes are an ERP feature only.", Category = AchievementCategory.Profiles, Tier = AchievementTier.Silver, Points = 10 },
            new() { Id = "profile_gallery",   Name = "Gallery Debut",    Description = "Upload a profile to the gallery.",      CompletedDescription = "Uploaded a profile to the gallery.", FlavourText = "Putting yourself out there.",        Hint = "RP Profile > Sharing > Direct Sharing or Public, then apply your character", Category = AchievementCategory.Profiles, Tier = AchievementTier.Silver, Points = 10 },
            new() { Id = "profile_effect",    Name = "Special Effects",  Description = "Enable a particle effect on a profile.",CompletedDescription = "Enabled a particle effect.",      FlavourText = "A little sparkle goes a long way.",     Hint = "RP Profile editor > Visual Effects section", Category = AchievementCategory.Profiles, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "profile_pronouns",  Name = "Self Expression",  Description = "Set pronouns on a character.",          CompletedDescription = "Set pronouns on a character.",    FlavourText = "Words matter.",                         Category = AchievementCategory.Profiles, Tier = AchievementTier.Bronze, Points = 3 },
            new() { Id = "profile_color",     Name = "Colour Coded",     Description = "Set a nameplate colour.",               FlavourText = "Stand out from the crowd.",             Hint = "Edit Character > Nameplate Colour picker", Category = AchievementCategory.Profiles, Tier = AchievementTier.Bronze, Points = 3 },
            new() { Id = "profile_connection",Name = "It's Complicated", Description = "Add a Connection to an RP profile.",    CompletedDescription = "Added a Connection.",             FlavourText = "No one walks alone.",                   Hint = "Expanded RP Profile editor > add a Connections content box (ERP feature)", Category = AchievementCategory.Profiles, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "profile_title",     Name = "Title Card",       Description = "Set a Title or Status on an RP profile.", CompletedDescription = "Set a Title or Status.",        FlavourText = "First impressions count.",              Hint = "RP Profile editor > Title and Status fields (with icons)", Category = AchievementCategory.Profiles, Tier = AchievementTier.Bronze, Points = 3 },

            // ── Switching (6) - one-offs for feature discovery ──
            new() { Id = "switch_main",    Name = "Point & Click",     Description = "Apply a character from the main window.",CompletedDescription = "Applied from the main window.",   FlavourText = "The classic approach.",                 Hint = "Click any character card in the main window", Category = AchievementCategory.Switching, Tier = AchievementTier.Bronze, Points = 3 },
            new() { Id = "switch_quick",   Name = "Quick Draw",        Description = "Apply a character from Quick Switch.",   CompletedDescription = "Applied from Quick Switch.",      FlavourText = "Speed is everything.",                  Hint = "/selectswitch (or Quick Switch button), then click a character", Category = AchievementCategory.Switching, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "switch_command", Name = "Command Line",      Description = "Apply a character via /select command.", CompletedDescription = "Applied via /select command.",    FlavourText = "The keyboard warrior's way.",           Hint = "/select <CharacterName> in chat", Category = AchievementCategory.Switching, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "switch_random",  Name = "Dice Roll",         Description = "Use /select random.",                    CompletedDescription = "Used /select random.",            FlavourText = "Feeling lucky?",                       Hint = "/select random in chat", Category = AchievementCategory.Switching, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "switch_design",  Name = "Outfit Change",     Description = "Apply a design.",                        CompletedDescription = "Applied a design.",               FlavourText = "New look, same you.",                   Hint = "Click a design in the Design Panel", Category = AchievementCategory.Switching, Tier = AchievementTier.Bronze, Points = 3 },
            new() { Id = "switch_revert",  Name = "Clean Slate",       Description = "Revert all CS+ changes.",                CompletedDescription = "Reverted all CS+ changes.",       FlavourText = "Back to basics.",                       Hint = "Revert button (top right of main window) or /selectrevert", Category = AchievementCategory.Switching, Tier = AchievementTier.Bronze, Points = 5 },

            // ── Automation (7) - one-offs for feature discovery ──
            new() { Id = "auto_assignment", Name = "Assigned Duty",    Description = "Set a character assignment.",            FlavourText = "One character per adventurer.",         Hint = "Settings > Character Assignments > Add Assignment", Category = AchievementCategory.Automation, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "auto_job",        Name = "Career Counselor", Description = "Set a job assignment.",                  FlavourText = "Dress for the job you want.",           Hint = "Settings > Job Assignments > enable + add a mapping", Category = AchievementCategory.Automation, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "auto_group",      Name = "Group Thinker",    Description = "Create a random group.",                 CompletedDescription = "Created a random group.",         FlavourText = "Strength in numbers.",                  Hint = "Settings > Random Groups > Create Group", Category = AchievementCategory.Automation, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "auto_macro",      Name = "Macro Master",     Description = "Use advanced mode on a character.",      CompletedDescription = "Used advanced mode.",             FlavourText = "Full control.",                         Hint = "Edit Character > Advanced Mode toggle", Category = AchievementCategory.Automation, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "auto_gearset",    Name = "Gear Up",          Description = "Enable gearset assignments.",            CompletedDescription = "Enabled gearset assignments.",    FlavourText = "Always prepared.",                      Hint = "Settings > Behaviour > Enable gearset assignments", Category = AchievementCategory.Automation, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "auto_glamauto",   Name = "Automated Style",  Description = "Set a Glamourer Automation.",            FlavourText = "Fashion on autopilot.",                 Hint = "Settings > Glamourer Automations (enable), then Edit Character > Automation field", Category = AchievementCategory.Automation, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "auto_dialogue",   Name = "They Call Me...",  Description = "Enable Immersive Dialogue.",             CompletedDescription = "Enabled Immersive Dialogue.",     FlavourText = "...whatever you want them to.",         Hint = "Settings > Immersive Dialogue", Category = AchievementCategory.Automation, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "auto_cr",         Name = "Conflict Resolver",Description = "Enable Conflict Resolution.",            CompletedDescription = "Enabled Conflict Resolution.",    FlavourText = "Taking control of your mods.",          Hint = "Settings > Conflict Resolution", Category = AchievementCategory.Automation, Tier = AchievementTier.Silver, Points = 10 },

            // ── Social (10) - one-offs + tiered ──
            new() { Id = "social_namesync",    Name = "Name's the Game",   Description = "Enable Name Sync.",                   CompletedDescription = "Enabled Name Sync.",              FlavourText = "Be who you want to be.",                Hint = "Settings > Name Sync > Enable name replacement", Category = AchievementCategory.Social, Tier = AchievementTier.Bronze,   Points = 5 },
            new() { Id = "social_seen",        Name = "Seen & Known",      Description = "Enable shared name visibility.",       CompletedDescription = "Enabled shared name visibility.", FlavourText = "Let the world see you.",                Hint = "Settings > Name Sync > Allow others to see my CS+ name", Category = AchievementCategory.Social, Tier = AchievementTier.Silver,   Points = 10 },
            new() { Id = "social_likes_1",     Name = "First Fan",         Description = "Get your first gallery like.",         CompletedDescription = "Got your first gallery like.",    FlavourText = "Someone noticed!",                      Hint = "Set RP Profile sharing to Public + a Main Character, then wait for likes", Category = AchievementCategory.Social, Tier = AchievementTier.Bronze,   Points = 5 },
            new() { Id = "social_likes_10",    Name = "Community Star",    Description = "Get 10 gallery likes total.",          CompletedDescription = "Got 10 gallery likes.",           FlavourText = "People are noticing.",                  Hint = "Set RP Profile sharing to Public + a Main Character, then wait for likes", Category = AchievementCategory.Social, Tier = AchievementTier.Silver,   Points = 10, IsBonus = true },
            new() { Id = "social_likes_50",    Name = "Gallery Celebrity", Description = "Get 50 gallery likes total.",          CompletedDescription = "Got 50 gallery likes.",           FlavourText = "Fame and fortune.",                     Hint = "Set RP Profile sharing to Public + a Main Character, then wait for likes", Category = AchievementCategory.Social, Tier = AchievementTier.Gold,     Points = 20, IsBonus = true },
            new() { Id = "social_viewrp",      Name = "People Watcher",    Description = "View someone's RP profile.",           CompletedDescription = "Viewed someone's RP profile.",    FlavourText = "Curiosity is a virtue.",                Hint = "/viewrp <Name@World> or right-click a player > View RP Profile", Category = AchievementCategory.Social, Tier = AchievementTier.Bronze,   Points = 5 },
            new() { Id = "social_viewself",    Name = "Mirror Mirror",     Description = "View your own RP profile.",            CompletedDescription = "Viewed your own RP profile.",     FlavourText = "Looking good.",                         Hint = "/viewrp self in chat", Category = AchievementCategory.Social, Tier = AchievementTier.Bronze,   Points = 3 },
            new() { Id = "social_like",        Name = "Showing Love",      Description = "Like someone's gallery profile.",      CompletedDescription = "Liked a gallery profile.",        FlavourText = "Spread the love.",                      Hint = "Click the heart on any gallery profile (not your own)", Category = AchievementCategory.Social, Tier = AchievementTier.Bronze,   Points = 3 },
            new() { Id = "social_follow",      Name = "Fan Club",          Description = "Follow someone in the gallery.",       CompletedDescription = "Followed a gallery user.",        FlavourText = "Keep up with the latest.",              Hint = "Open a gallery profile > Follow button", Category = AchievementCategory.Social, Tier = AchievementTier.Bronze,   Points = 3 },
            new() { Id = "social_fav_gallery", Name = "Bookmarked",        Description = "Favourite a gallery profile.",         CompletedDescription = "Favourited a gallery profile.",   FlavourText = "Saved for later.",                      Hint = "Click the bookmark/star button on a gallery profile", Category = AchievementCategory.Social, Tier = AchievementTier.Bronze,   Points = 3 },

            // ── Customization (6) - one-offs ──
            new() { Id = "custom_theme",    Name = "Theme Crafter",      Description = "Create a custom theme.",               CompletedDescription = "Created a custom theme.",         FlavourText = "Make it yours.",                        Hint = "Settings > Visual Settings > Theme > Custom (New)", Category = AchievementCategory.Customization, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "custom_seasonal", Name = "Seasonal Spirit",    Description = "Use a seasonal theme.",                CompletedDescription = "Used a seasonal theme.",          FlavourText = "Getting into the spirit.",              Hint = "Settings > Visual Settings > Theme > Halloween/Winter/Christmas/Valentine's", Category = AchievementCategory.Customization, Tier = AchievementTier.Bronze, Points = 3 },
            new() { Id = "custom_icon",     Name = "Icon Connoisseur",   Description = "Set a custom favourite icon.",         FlavourText = "It's the little things.",               Hint = "Custom Theme editor > Favourite Icon picker", Category = AchievementCategory.Customization, Tier = AchievementTier.Bronze, Points = 3 },
            new() { Id = "custom_alias",    Name = "Also Known As",      Description = "Set a Character Alias.",               FlavourText = "What's in a name?",                     Hint = "Edit Character > Character Alias field (visible when Name Sync is enabled)", Category = AchievementCategory.Customization, Tier = AchievementTier.Bronze, Points = 3 },
            new() { Id = "custom_bgimage",  Name = "Interior Decorator", Description = "Set a custom background image.",       FlavourText = "Setting the mood.",                     Hint = "Custom Theme editor > Background Image", Category = AchievementCategory.Customization, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "custom_preset",   Name = "Saved Look",         Description = "Save a theme preset.",                 CompletedDescription = "Saved a theme preset.",           FlavourText = "For next time.",                        Hint = "Custom Theme editor > Save as Preset", Category = AchievementCategory.Customization, Tier = AchievementTier.Silver, Points = 8 },

            // ── Discovery (11) - one-offs for meta features ──
            new() { Id = "discover_fav",        Name = "Picking Favourites",   Description = "Mark a character as favourite.",     CompletedDescription = "Marked a character as favourite.", FlavourText = "Everyone has a favourite.",              Category = AchievementCategory.Discovery, Tier = AchievementTier.Bronze, Points = 3 },
            new() { Id = "discover_pose",       Name = "Strike a Pose",        Description = "Set an idle pose on a character.",   CompletedDescription = "Set an idle pose.",               FlavourText = "Express yourself.",                     Hint = "Edit Character > Poses section", Category = AchievementCategory.Discovery, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "discover_snapshot",   Name = "Captured Moment",      Description = "Use /select save to snapshot a look.", CompletedDescription = "Used /select save.",            FlavourText = "Freeze frame.",                        Hint = "/select save in chat (or /select save CR for full mod snapshot)", Category = AchievementCategory.Discovery, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "discover_backup",     Name = "Safety First",         Description = "Create a manual backup.",             CompletedDescription = "Created a manual backup.",      FlavourText = "Better safe than sorry.",               Hint = "Settings > Backup & Restore > Manual Backup", Category = AchievementCategory.Discovery, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "discover_features",   Name = "Explorer",             Description = "Open the Features Guide.",            CompletedDescription = "Opened the Features Guide.",    FlavourText = "Knowledge is power.",                   Hint = "Features button in the bottom tray of the main window", Category = AchievementCategory.Discovery, Tier = AchievementTier.Bronze, Points = 3 },
            new() { Id = "discover_tags",       Name = "Label Maker",          Description = "Use tags to organise characters.",    CompletedDescription = "Used tags to organise characters.", FlavourText = "A system for everything.",          Hint = "Edit Character > Tags field", Category = AchievementCategory.Discovery, Tier = AchievementTier.Bronze, Points = 3 },
            new() { Id = "discover_main",       Name = "Leading Role",         Description = "Set a Main Character.",               FlavourText = "The star of the show.",                 Hint = "Settings > Main Character > pick one of your characters", Category = AchievementCategory.Discovery, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "discover_mods",       Name = "Under the Hood",       Description = "Open the Mod Manager.",               CompletedDescription = "Opened the Mod Manager.",       FlavourText = "Tinker time.",                          Hint = "/select mods (or click the Mod Manager button)", Category = AchievementCategory.Discovery, Tier = AchievementTier.Bronze, Points = 3 },
            new() { Id = "discover_gallery",    Name = "Window Shopping",      Description = "Open the Gallery.",                   CompletedDescription = "Opened the Gallery.",           FlavourText = "So many faces.",                        Hint = "/gallery (or click the Gallery button)", Category = AchievementCategory.Discovery, Tier = AchievementTier.Bronze, Points = 3 },
            new() { Id = "discover_patchnotes", Name = "What's New?",          Description = "View the Patch Notes.",               CompletedDescription = "Viewed the Patch Notes.",       FlavourText = "Staying informed.",                     Hint = "Patch Notes button in the bottom tray of the main window (beside Features), or /select whatsnew in chat", Category = AchievementCategory.Discovery, Tier = AchievementTier.Bronze, Points = 3 },
            new() { Id = "discover_reorder",    Name = "Rearranged",           Description = "Reorder your characters.",            CompletedDescription = "Reordered your characters.",    FlavourText = "Everything in its place.",               Hint = "Set sort to Manual, then drag characters in the main window", Category = AchievementCategory.Discovery, Tier = AchievementTier.Bronze, Points = 3 },

            // ── Plugin integration achievements ──
            new() { Id = "integration_honorific",        Name = "Quote Unquote",       Description = "Set a Honorific title on a character.",        CompletedDescription = "Set a Honorific title.",            FlavourText = "Song lyrics, hearts, edgy quotes... your call.",     Hint = "Edit Character > Honorific section > set a title", Category = AchievementCategory.Automation, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "integration_customize",        Name = "Custom Fit",          Description = "Set a Customize+ profile on a character.",     CompletedDescription = "Set a Customize+ profile.",         FlavourText = "Made to measure.",                                  Hint = "Edit Character > Customize+ Profile field", Category = AchievementCategory.Automation, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "integration_customize_design", Name = "Tailor Made",         Description = "Set a Customize+ profile on a specific design.",CompletedDescription = "Set a per-design Customize+ profile.", FlavourText = "Different outfit, different fit.",               Hint = "Edit a Design > Customize+ Profile field", Category = AchievementCategory.Designs,    Tier = AchievementTier.Silver, Points = 8 },
            new() { Id = "integration_triple",           Name = "Mirror Check",        Description = "Set Glamourer, Customize+, AND Honorific on the same character.", CompletedDescription = "Set all three integrations on one character.", FlavourText = "Looking good. Time to head out.",  Hint = "Edit one character: set Glamourer Design + Customize+ Profile + Honorific Title together", Category = AchievementCategory.Customization, Tier = AchievementTier.Gold,   Points = 15 },
            new() { Id = "integration_gradient",         Name = "Tie-Dye",             Description = "Use a two-colour Honorific gradient title.",   CompletedDescription = "Used a two-colour gradient title.", FlavourText = "Why pick one shade.",                              Hint = "Settings > Honorific > enable Two-Colour Gradients, then Edit Character > Honorific > Gradient style > Two-Colour", Category = AchievementCategory.Automation, Tier = AchievementTier.Silver, Points = 8 },

            // ── Tutorial / discovery extensions ──
            new() { Id = "discover_tutorial",   Name = "Class Dismissed",      Description = "Complete the in-plugin tutorial.",    CompletedDescription = "Completed the tutorial.",         FlavourText = "Graduated with honours.",                 Hint = "Settings > Behaviour > Show Tutorial (or it appears on first launch)", Category = AchievementCategory.Discovery, Tier = AchievementTier.Silver, Points = 10 },
            new() { Id = "custom_seasonal_3",   Name = "Seasoned",             Description = "Use 3 different seasonal themes.",    CompletedDescription = "Used 3 different seasonal themes.", FlavourText = "Year-round mood.",                       Hint = "Try Halloween, Winter, Christmas, AND Valentine's themes (Settings > Visual)", Category = AchievementCategory.Customization, Tier = AchievementTier.Bronze, Points = 5 },

            // ── Expanded RP profile achievements ──
            new() { Id = "profile_bio_long",    Name = "Novelist",             Description = "Write an RP bio over 500 characters.",CompletedDescription = "Wrote an RP bio over 500 characters.", FlavourText = "Tell me everything.",                Category = AchievementCategory.Profiles, Tier = AchievementTier.Silver, Points = 10 },
            new() { Id = "profile_complete",    Name = "Fully Realised",       Description = "Have a character with bio, pronouns, image, background, and a content box all set.", CompletedDescription = "Filled out a complete RP profile.", FlavourText = "Every detail accounted for.",       Hint = "Fill bio + pronouns + image + background, then add a content box via the Expanded RP Profile editor (ERP)", Category = AchievementCategory.Profiles, Tier = AchievementTier.Gold,   Points = 15 },
            new() { Id = "profile_layouts",     Name = "Layout Connoisseur",   Description = "Use 5 different content box layout types.", CompletedDescription = "Used 5 different content box layouts.", FlavourText = "Fluent in every format.",          Hint = "Expanded RP Profile editor > content boxes: try Standard, List, Quote, Timeline, Grid, etc. (ERP feature)", Category = AchievementCategory.Profiles, Tier = AchievementTier.Gold,   Points = 15 },
            new() { Id = "profile_boxes_6",     Name = "Encyclopedia",         Description = "Add 6 or more content boxes to a character.", CompletedDescription = "Added 6+ content boxes.",     FlavourText = "Cover to cover.",                          Category = AchievementCategory.Profiles, Tier = AchievementTier.Silver, Points = 10 },
            new() { Id = "profile_banner",      Name = "Headlining",           Description = "Set a banner image on an RP profile.", CompletedDescription = "Set a banner image.",            FlavourText = "Above the fold.",                          Hint = "Expanded RP Profile editor > Banner Image", Category = AchievementCategory.Profiles, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "profile_url_bg",      Name = "Wallpapered",          Description = "Set a custom URL background on an RP profile.", CompletedDescription = "Set a custom URL background.", FlavourText = "Brought your own backdrop.",          Hint = "Expanded RP Profile editor > Custom URL Background", Category = AchievementCategory.Profiles, Tier = AchievementTier.Silver, Points = 8 },
            new() { Id = "profile_layout_timeline", Name = "Time Capsule",     Description = "Use a Timeline content box layout.",  CompletedDescription = "Used a Timeline content box.",   FlavourText = "Chronologically yours.",                  Hint = "Expanded RP Profile editor > add a content box and pick the Timeline layout (ERP feature)", Category = AchievementCategory.Profiles, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "profile_layout_quote",    Name = "On the Record",    Description = "Use a Quote content box layout.",     CompletedDescription = "Used a Quote content box.",      FlavourText = "And I quote.",                            Hint = "Expanded RP Profile editor > add a content box and pick the Quote layout (ERP feature)", Category = AchievementCategory.Profiles, Tier = AchievementTier.Bronze, Points = 5 },

            // ── Pose / mod / GPose ──
            new() { Id = "discover_pose_cmd",   Name = "Pose Library",         Description = "Set a sit, ground sit, or doze pose via /ssit, /sgroundsit, or /sdoze.", CompletedDescription = "Set a pose via chat command.", FlavourText = "There's more than one way to stand still.", Hint = "/ssit 0-6, /sgroundsit 0-6, or /sdoze 0-6 in chat", Category = AchievementCategory.Discovery, Tier = AchievementTier.Bronze, Points = 5 },
            new() { Id = "auto_modoptions",     Name = "Tinkerer",             Description = "Set per-design mod option overrides.",CompletedDescription = "Set per-design mod options.",    FlavourText = "Surgical precision.",                     Hint = "Edit a design > Configure Mods button > click the edit icon on a configurable mod and set its options", Category = AchievementCategory.Automation, Tier = AchievementTier.Silver, Points = 10 },
            new() { Id = "discover_gpose",      Name = "Casting Director",     Description = "Apply a CS+ character to a GPose target.", CompletedDescription = "Applied a CS+ character to a GPose target.", FlavourText = "And... action.",                Hint = "Enter GPose, target a Brio/Ktisis actor, then apply a CS+ character", Category = AchievementCategory.Discovery, Tier = AchievementTier.Bronze, Points = 5 },

            // ── Composite / tiered ──
            new() { Id = "switch_all_methods",  Name = "Why Pick One?",        Description = "Apply via main window, Quick Switch, and /select.", CompletedDescription = "Used all three switching methods.", FlavourText = "Multiple choice.",                Hint = "Apply a character three different ways: main window click, Quick Switch click, AND /select <name>", Category = AchievementCategory.Switching, Tier = AchievementTier.Silver, Points = 10 },
            new() { Id = "auto_assignment_5",   Name = "Ensemble",             Description = "Set character assignments on 5 different in-game characters.", CompletedDescription = "Set 5 character assignments.",  FlavourText = "Quite the lineup.",                Hint = "Settings > Character Assignments > add 5 entries (different in-game characters)", Category = AchievementCategory.Automation, Tier = AchievementTier.Silver, Points = 10 },
            new() { Id = "auto_job_3",          Name = "Type Cast",            Description = "Set job assignments for 3 different jobs.", CompletedDescription = "Set 3 job assignments.",     FlavourText = "Right character for the right role.",     Hint = "Settings > Job Assignments > add 3 different jobs", Category = AchievementCategory.Automation, Tier = AchievementTier.Silver, Points = 10 },

            // ── Meta progress ──
            new() { Id = "meta_halfway",        Name = "Halfway There",        Description = "Unlock half of all achievements.",     CompletedDescription = "Unlocked half of all achievements.", FlavourText = "Coasting on momentum.",                Category = AchievementCategory.Discovery, Tier = AchievementTier.Silver, Points = 15 },
            new() { Id = "meta_completionist",  Name = "Completionist",        Description = "Unlock every achievement.",            CompletedDescription = "Unlocked every achievement.",  FlavourText = "There is no further.",                       Category = AchievementCategory.Discovery, Tier = AchievementTier.Platinum, Points = 50, IsHidden = true },

            // ── Easter egg ──
            new() { Id = "discover_wardrobe",     Name = "Dress Rehearsal",      Description = "Open the Wardrobe.",                   CompletedDescription = "Opened the Wardrobe.",           FlavourText = "Now you're browsing in style.",            Hint = "/wardrobe or click the Wardrobe button in the Design Panel", Category = AchievementCategory.Discovery, Tier = AchievementTier.Bronze, Points = 3 },
            new() { Id = "discover_filebrowser", Name = "Personal Preference", Description = "Use the in-game file browser.",        CompletedDescription = "Used the in-game file browser.", FlavourText = "Different strokes for different folks.",   Hint = "Settings > Behaviour > Use in-game file browser, then pick any image", Category = AchievementCategory.Discovery, Tier = AchievementTier.Bronze, Points = 3 },
        };

        private static readonly Dictionary<string, AchievementDefinition> ById =
            All.ToDictionary(a => a.Id, a => a);

        /// <summary>Look up an achievement by its unique ID. Returns null if not found.</summary>
        public static AchievementDefinition? Get(string id) =>
            ById.TryGetValue(id, out var def) ? def : null;

        /// <summary>Get all achievements in a specific category.</summary>
        public static IEnumerable<AchievementDefinition> GetByCategory(AchievementCategory category) =>
            All.Where(a => a.Category == category);

        /// <summary>Total achievable points across all achievements (core + bonus).</summary>
        public static int TotalPoints => All.Sum(a => a.Points);

        /// <summary>All non-bonus achievements (these count toward Halfway / Completionist).</summary>
        public static IEnumerable<AchievementDefinition> CoreAchievements =>
            All.Where(a => !a.IsBonus);

        /// <summary>All bonus achievements (extra/optional, not required for 100%).</summary>
        public static IEnumerable<AchievementDefinition> BonusAchievements =>
            All.Where(a => a.IsBonus);

        /// <summary>Total achievable points across just the core achievements.</summary>
        public static int CorePoints => CoreAchievements.Sum(a => a.Points);

        /// <summary>Total achievable points across just the bonus achievements.</summary>
        public static int BonusPoints => BonusAchievements.Sum(a => a.Points);
    }
}

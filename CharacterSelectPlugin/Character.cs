using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json;

namespace CharacterSelectPlugin
{
    [Serializable]
    public class Character
    {
        public string Name { get; set; }

        /// <summary>Optional alias used for Name Sync. If empty, uses Name.</summary>
        public string? Alias { get; set; } = null;

        /// <summary>When true, this character's name won't be shared via Name Sync regardless of global setting.</summary>
        public bool ExcludeFromNameSync { get; set; } = false;
        // nullable backing tolerates the nulls left by the pre-release nullable version
        [JsonProperty("AccentFollowsNameplate")]
        private bool? accentFollowsNameplateBacking { get; set; }

        [JsonIgnore]
        public bool AccentFollowsNameplate
        {
            get => accentFollowsNameplateBacking ?? false;
            set => accentFollowsNameplateBacking = value;
        }

        /// <summary>When true, this character's name in the character card and RP profile renders
        /// in the SD Glitch font with a periodic chromatic-burst FX cascade.</summary>
        public bool UseGlitchNameEffect { get; set; } = false;

        /// <summary>
        /// Previous server file keys (format: "{sanitizedCsName}_{physicalName}") recorded whenever this
        /// character is renamed, its alias is changed, or the linked in-game character is renamed. Sent
        /// with every upload so the server can migrate likes + delete orphan files from prior identities.
        /// Capped at <see cref="MaxPreviousProfileKeys"/> entries (oldest trimmed).
        /// </summary>
        public List<string> PreviousProfileKeys { get; set; } = new();

        /// <summary>
        /// Dalamud content ID of the in-game character this CS+ character was last applied to. Used to
        /// detect FFXIV character renames - if ContentId matches but Name differs, it's a rename and we
        /// should migrate the old server data. If ContentId differs, it's a different alt.
        /// </summary>
        public ulong? LastInGameContentId { get; set; } = null;

        [JsonIgnore]
        public const int MaxPreviousProfileKeys = 20;

        /// <summary>
        /// Appends a previous fileKey to the rename history, deduping and trimming oldest if over cap.
        /// Safe to call with null/empty values (no-op).
        /// </summary>
        public void AddPreviousProfileKey(string? fileKey)
        {
            if (string.IsNullOrWhiteSpace(fileKey)) return;
            PreviousProfileKeys ??= new List<string>();
            // Dedup: move existing entry to end so it's the most recent
            PreviousProfileKeys.Remove(fileKey);
            PreviousProfileKeys.Add(fileKey);
            while (PreviousProfileKeys.Count > MaxPreviousProfileKeys)
                PreviousProfileKeys.RemoveAt(0);
        }

        /// <summary>
        /// Records that this CS+ character was applied to a specific in-game character. Detects an
        /// FFXIV character rename (same ContentId + different Name) and adds the old server fileKey
        /// to the rename history so the next upload migrates likes/files. Always updates both
        /// <see cref="LastInGameName"/> and <see cref="LastInGameContentId"/>.
        /// </summary>
        public void OnInGameApply(string newLastInGameName, ulong newContentId)
        {
            if (string.IsNullOrWhiteSpace(newLastInGameName)) return;

            if (LastInGameContentId.HasValue
                && LastInGameContentId.Value == newContentId
                && newContentId != 0
                && !string.IsNullOrWhiteSpace(LastInGameName)
                && !string.Equals(LastInGameName, newLastInGameName, StringComparison.Ordinal))
            {
                // Same physical character (ContentId), new name → rename. Record the old fileKey.
                string displayName = !string.IsNullOrWhiteSpace(Alias) ? Alias : Name;
                AddPreviousProfileKey($"{displayName}_{LastInGameName}");
            }

            LastInGameContentId = newContentId;
            LastInGameName = newLastInGameName;
        }

        public string Macros { get; set; } = "";
        public string? ImagePath { get; set; }

        /// <summary>Static portrait fine-tune controls.
        /// Offset values are fractions of the portrait area (-1 to 1), zoom is a
        /// multiplier on the aspect-fit base size.</summary>
        public float PortraitOffsetX { get; set; } = 0f;
        public float PortraitOffsetY { get; set; } = 0f;
        public float PortraitZoom { get; set; } = 1f;

        /// <summary>Optional animated image (.gif / .webp) shown on hover in the main roster.
        /// Falls back to ImagePath when null/missing.</summary>
        public string? AnimatedImagePath { get; set; }

        /// <summary>Animated image fine-tune controls, separate from the static portrait
        /// since the GIF may have different framing.</summary>
        public float AnimatedOffsetX { get; set; } = 0f;
        public float AnimatedOffsetY { get; set; } = 0f;
        public float AnimatedZoom { get; set; } = 1f;

        /// <summary>Optional transparent PNG cutout for the hover pop-out effect.
        /// On hover: cutout grows + extends above the card. Mutually exclusive with
        /// AnimatedImagePath at the form level, only one hover mode active per character.</summary>
        public string? CutoutImagePath { get; set; }

        /// <summary>Optional alternate backdrop swapped in during a cutout hover (in place
        /// of the static portrait inside the card area). Only meaningful when
        /// CutoutImagePath is set.</summary>
        public string? CutoutBackdropPath { get; set; }

        /// <summary>WHERE on the card the cutout's reference point lands (% of card rect, 0..1).
        /// Default: 65% across (slightly right of center), bottom of portrait area (1.0).</summary>
        public float CutoutAnchorX { get; set; } = 0.65f;
        public float CutoutAnchorY { get; set; } = 1.00f;

        /// <summary>WHERE in the cutout PNG that reference point lives (% of image rect, 0..1).
        /// Default: image bottom-center, assumes the character's feet are at the PNG bottom.</summary>
        public float CutoutPoseAx { get; set; } = 0.50f;
        public float CutoutPoseAy { get; set; } = 1.00f;

        /// <summary>Cutout size multiplier (× full card width). Default 3.25 gives a
        /// substantially-larger-than-card render. Per-character so different cutouts
        /// can be scaled to fit.</summary>
        public float CutoutScale { get; set; } = 3.25f;
        public List<CharacterDesign> Designs { get; set; }
        public Vector3 NameplateColor { get; set; } = new Vector3(1.0f, 1.0f, 1.0f);
        /// <summary>Per-character toggle: when true, the Wardrobe uses this character's
        /// NameplateColor as the accent (rail, hangers, sparkles, streak) instead of
        /// the theme's default gold/custom accent colour.</summary>
        public bool UseNameplateColorInWardrobe { get; set; } = false;
        public string PenumbraCollection { get; set; } = "";
        public string GlamourerDesign { get; set; } = "";
        public string CustomizeProfile { get; set; } = "";
        public bool IsFavorite { get; set; } = false; 
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public int SortOrder { get; set; } = 0;
        public string HonorificTitle { get; set; } = "";
        public string HonorificPrefix { get; set; } = "";
        public string HonorificSuffix { get; set; } = "";
        public Vector3 HonorificColor { get; set; } = new Vector3(1.0f, 1.0f, 1.0f);
        public Vector3 HonorificGlow { get; set; } = new Vector3(1.0f, 1.0f, 1.0f);
        public Vector3? HonorificColor3 { get; set; } = null;  // Second colour for two-colour gradient
        public int? HonorificGradientSet { get; set; } = null;  // -1 = Two Colour Gradient
        public string? HonorificAnimationStyle { get; set; } = null;
        public string MoodlePreset { get; set; } = "";
        public byte IdlePoseIndex { get; set; } = 7;
        public byte SitPoseIndex { get; set; } = 255;
        public byte GroundSitPoseIndex { get; set; } = 255;
        public byte DozePoseIndex { get; set; } = 255;
        public Dictionary<string, bool>? SecretModState { get; set; }
        public List<string>? SecretModPins { get; set; }

        /// <summary>Per-character mod option settings. Format: ModDirectory -> GroupName -> SelectedOptionNames</summary>
        public Dictionary<string, Dictionary<string, List<string>>>? ModOptionSettings { get; set; }

        /// <summary>
        /// Baseline mod options captured at session start for restoration when switching designs.
        /// Format: ModDirectory -> GroupName -> SelectedOptionNames
        /// </summary>
        [JsonIgnore]
        public Dictionary<string, Dictionary<string, List<string>>>? OriginalCollectionState { get; set; }

        // Mod directories the currently-applied design overrides this session.
        [JsonIgnore]
        public HashSet<string>? ActiveDesignModOverrides { get; set; }
        public string? Pronouns { get; set; }
        public string? Gender { get; set; }
        public string? Age { get; set; }
        public string? Race { get; set; }
        public string? SexualOrientation { get; set; }
        public string? RelationshipStatus { get; set; }
        public string? Occupation { get; set; }
        public string? Abilities { get; set; }
        public string? Bio { get; set; }
        public string? RpTags { get; set; }
        public string? RpImagePath { get; set; } 
        public RPProfile RPProfile { get; set; } = new();
        public string? LastInGameName { get; set; }
        public List<string> Tags { get; set; } = new();
        [JsonIgnore]
        public string Tag
        {
            get => Tags.FirstOrDefault() ?? "";
            set
            {
                Tags.Clear();
                if (!string.IsNullOrWhiteSpace(value))
                    Tags.Add(value);
            }
        }
        public List<string> KnownTags { get; set; } = new();
        public List<string> DesignTags { get; set; } = new List<string>();
        public string CharacterAutomation { get; set; } = "";

        /// <summary>Gearset to switch to when applying this character (null = don't switch).</summary>
        public int? AssignedGearset { get; set; } = null;

        public List<DesignFolder> DesignFolders { get; set; } = new();
        public Vector3? OverrideAccentColor { get; set; } 
        public string? BackgroundImage { get; set; }
        public ProfileEffects? Effects { get; set; }
        public string GalleryStatus { get; set; } = "";
        public bool IsAdvancedMode { get; set; } = false;
        
        // Extended profile fields
        public string? ExtendedBio { get; set; } = "";
        public string? PersonalityTraits { get; set; } = "";
        public string? BackstorySnippet { get; set; } = "";
        public string? Hooks { get; set; } = "";
        public string? Connections { get; set; } = "";
        public string? Goals { get; set; } = "";
        public string? Secrets { get; set; } = "";
        public string? Quotes { get; set; } = "";
        public List<string> GalleryImages { get; set; } = new();
        public string? BannerImagePath { get; set; } = null;





        public Character(
            string name,
            string macros,
            string? imagePath,
            List<CharacterDesign> designs,
            Vector3 nameplateColor,
            string penumbraCollection,
            string glamourerDesign,
            string customizeProfile,
            string honorificTitle,
            string honorificPrefix,
            string honorificSuffix,
            Vector3 honorificColor,
            Vector3 honorificGlow,
            string moodlePreset,
            string characterautomation,
            string galleryStatus = "")
        {
            Name = name;
            Macros = macros ?? ""; 
            ImagePath = imagePath;
            Designs = designs ?? new List<CharacterDesign>();
            NameplateColor = nameplateColor;
            PenumbraCollection = penumbraCollection;
            GlamourerDesign = glamourerDesign;
            CustomizeProfile = customizeProfile;
            HonorificTitle = honorificTitle;
            HonorificPrefix = honorificPrefix;
            HonorificSuffix = honorificSuffix;
            HonorificColor = honorificColor;
            HonorificGlow = honorificGlow;
            MoodlePreset = moodlePreset;
            CharacterAutomation = characterautomation;
            BackgroundImage = null;
            Effects = new ProfileEffects();
            GalleryStatus = galleryStatus;
        }
    }
    public class DesignFolder
    {
        public string Name { get; set; }
        public Guid Id { get; set; }
        public Vector3? CustomColor { get; set; } = null;

        public Guid? ParentFolderId { get; set; } = null;
        public int SortOrder { get; set; } = 0;

        
        public DesignFolder()
        {
            Name = "";
            Id = Guid.NewGuid();
            ParentFolderId = null;
            SortOrder = 0;
        }

        public DesignFolder(string name)
        {
            Name = name;
            Id = Guid.NewGuid();
        }

        public DesignFolder(string name, Guid id)
        {
            Name = name;
            Id = id;
        }

        public DesignFolder(DesignFolder other)
        {
            Name = other.Name;
            Id = other.Id;
        }
    }

}

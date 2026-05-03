using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using CharacterSelectPlugin.Windows.Styles;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace CharacterSelectPlugin.Windows;

public class FeaturesWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    private string searchQuery = "";
    private List<FeatureEntry> allFeatures = new();
    private string[] categoryNames = Array.Empty<string>();   // ordered category list, no "All" prefix
    private CategoryMeta[] categoryMeta = Array.Empty<CategoryMeta>();
    private int activeCategoryIndex = 0;                       // 0 = All, 1..n = category index + 1
    private double categoryChangeT = 0;

    private record FeatureEntry(
        string Name,
        string Description,
        string Location,
        string Category,
        FontAwesomeIcon Icon,
        Vector4 IconColor,
        string[] Keywords,
        bool IsNew = false);

    private record struct CategoryMeta(string Name, string ShortLabel, FontAwesomeIcon Icon, Vector4 Tint);

    public FeaturesWindow(Plugin plugin) : base(
        "CS+ Features Guide",
        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;

        Size = new Vector2(720, 880);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(620, 560),
            MaximumSize = new Vector2(1100, 1300)
        };

        BuildFeatureList();
        BuildCategoryMeta();
    }

    public void Dispose() { }

    // ── Feature data ───────────────────────────────────────────────────
    private void BuildFeatureList()
    {
        var cyan   = new Vector4(0.3f,  0.85f, 1.0f,  1.0f);
        var green  = new Vector4(0.4f,  0.9f,  0.5f,  1.0f);
        var orange = new Vector4(1.0f,  0.7f,  0.3f,  1.0f);
        var pink   = new Vector4(1.0f,  0.5f,  0.7f,  1.0f);
        var purple = new Vector4(0.7f,  0.5f,  1.0f,  1.0f);
        var yellow = new Vector4(1.0f,  0.9f,  0.4f,  1.0f);
        var red    = new Vector4(1.0f,  0.4f,  0.4f,  1.0f);
        var blue   = new Vector4(0.4f,  0.6f,  1.0f,  1.0f);
        var slate  = new Vector4(0.6f,  0.7f,  0.85f, 1.0f);

        allFeatures = new List<FeatureEntry>
        {
            // Quick Actions
            new("Quick Switch Overlay",
                "A floating window for rapid character switching. Keep it open while you play.",
                "/selectswitch",
                "Quick Actions",
                FontAwesomeIcon.Bolt, yellow,
                new[] { "quick", "fast", "switch", "overlay" }),

            new("Compact Quick Switch",
                "A minimal version of Quick Switch, just a row of character icons. Toggle compact mode in the settings or right-click the Quick Switch window.",
                "Settings > Behavior",
                "Quick Actions",
                FontAwesomeIcon.CompressArrowsAlt, yellow,
                new[] { "quick", "compact", "small", "minimal", "bar" }),

            new("Random Selection",
                "Can't decide? Let CS+ pick a random character and design for you.",
                "/select random",
                "Quick Actions",
                FontAwesomeIcon.Dice, orange,
                new[] { "random", "surprise", "pick" }),

            new("Random Groups",
                "Create groups like 'Tanks' or 'Casual Looks' for themed random picks.",
                "Settings > Random Groups",
                "Quick Actions",
                FontAwesomeIcon.LayerGroup, orange,
                new[] { "random", "group", "themed" }),

            new("Revert All Changes",
                "One click to undo all CS+ changes and return to your base look.",
                "/selectrevert",
                "Quick Actions",
                FontAwesomeIcon.Undo, red,
                new[] { "revert", "undo", "reset" }),

            new("Achievements",
                "Encourages you to explore CS+ features you might not know exist. Try something new and earn an achievement for it, with a slide-in toast and points reward. The Achievements window tracks your progress with categories, sorting, and an Almost There spotlight pointing you at what to try next. Fully optional: toggle toasts, chat messages, or the whole system off in Settings.",
                "Trophy button on Main Window",
                "Quick Actions",
                FontAwesomeIcon.Trophy, yellow,
                new[] { "achievement", "achievements", "trophy", "unlock", "reward", "points", "progress", "toast", "explore", "discover" },
                IsNew: true),

            // Your Identity
            new("See Your CS+ Name Everywhere",
                "Your nameplate, chat messages, target bar, and party list all show your character's name instead of your FFXIV name.",
                "Settings > Name Sync",
                "Your Identity",
                FontAwesomeIcon.IdCard, cyan,
                new[] { "name", "nameplate", "chat", "identity" }),

            new("See Other Players' CS+ Names",
                "See other CS+ users' character names instead of their FFXIV names. Anyone who opts in becomes visible to you.",
                "Settings > Name Sync",
                "Your Identity",
                FontAwesomeIcon.Users, cyan,
                new[] { "name", "other", "shared", "rp" }),

            new("NPCs Use Your Name",
                "Quest dialogue says 'Hello, [YourCharacter]!' instead of your FFXIV name. Full immersion.",
                "Settings > Immersive Dialogue",
                "Your Identity",
                FontAwesomeIcon.Comment, green,
                new[] { "dialogue", "npc", "name", "immersion" }),

            new("NPCs Use Your Pronouns",
                "She/her, he/him, they/them. NPCs will use whatever pronouns you set in your RP Profile.",
                "Settings > Immersive Dialogue",
                "Your Identity",
                FontAwesomeIcon.Comments, green,
                new[] { "pronoun", "they", "she", "he", "gender" }),

            new("Honorific Titles",
                "Set a title that appears above your character's name using the Honorific plugin. Customise the colour and glow. Supporters of Honorific can use animated gradients, and you can configure a two-colour gradient title for that ombre look.",
                "Character Form > Honorific",
                "Your Identity",
                FontAwesomeIcon.Star, yellow,
                new[] { "honorific", "title", "glow", "name", "gradient", "two-colour", "tie-dye" }),

            new("Customize+ Profiles",
                "Set a Customize+ profile on a character to control body proportions and scale. Set a per-design Customize+ profile too, so different outfits can have different body shapes for the same character.",
                "Character Form / Design Form > Customize+",
                "Your Identity",
                FontAwesomeIcon.UserCog, yellow,
                new[] { "customize", "scale", "body", "proportions", "shape" }),

            new("Expanded RP Profile Editor",
                "Open the bigger profile editor to add multi-section content boxes (Timeline, Quote, Connections, Lists, Pros & Cons, and more), set a banner image, and use URL-based custom backgrounds. Most users don't realise how deep this goes.",
                "Character Form > RP Profile > Open Editor",
                "RP Profiles",
                FontAwesomeIcon.BookOpen, pink,
                new[] { "rp", "profile", "expanded", "editor", "banner", "url", "background", "content", "box", "layout", "timeline", "quote" }),

            new("In-Plugin Tutorial",
                "Walks you through the basics step-by-step. Re-runnable from Settings if you skipped it or want a refresher.",
                "Settings > Behavior > Tutorial",
                "Customize CS+",
                FontAwesomeIcon.GraduationCap, purple,
                new[] { "tutorial", "guide", "intro", "first time", "walkthrough" }),

            // Automation
            new("Auto-Apply on Login",
                "Log in and your last character + design applies automatically. No clicks needed.",
                "Settings > Behavior",
                "Automation",
                FontAwesomeIcon.SignInAlt, purple,
                new[] { "login", "auto", "remember" }),

            new("Character Assignments",
                "Different FFXIV alts, different CS+ characters. Automatically.",
                "Settings > Character Assignments",
                "Automation",
                FontAwesomeIcon.Link, purple,
                new[] { "assignment", "alt", "auto" }),

            new("Job Assignments",
                "Switch to Tank? Your tank character applies. Switch to Healer? Healer character. Automatic job-based looks.",
                "Settings > Job Assignments",
                "Automation",
                FontAwesomeIcon.Briefcase, purple,
                new[] { "job", "class", "tank", "healer", "auto" }),

            new("Gearset Sync",
                "When you apply a character, also switch to a matching gearset automatically.",
                "Settings > Job Assignments",
                "Automation",
                FontAwesomeIcon.Tshirt, purple,
                new[] { "gearset", "gear", "equipment" }),

            new("Reapply Design on Job Change",
                "Changed jobs in-game? CS+ reapplies your current design to refresh your look. Handy when job-specific mods are involved.",
                "Settings > Behavior",
                "Automation",
                FontAwesomeIcon.Sync, purple,
                new[] { "job", "change", "reapply", "refresh" }),

            new("Glamourer Automations",
                "Trigger Glamourer Automations when applying characters or designs. Create a 'None' automation in Glamourer for characters that shouldn't run any automation, this prevents one character's automation from carrying over to another.",
                "Settings > Glamourer Automations",
                "Automation",
                FontAwesomeIcon.Magic, purple,
                new[] { "glamourer", "automation", "none", "trigger" }),

            new("Advanced Mode & Macros",
                "Enable Advanced Mode on a character or design to run custom macro commands on apply. Use this for anything CS+ doesn't directly support: trigger other plugins, run game commands, or execute complex sequences.",
                "Character Form / Design Panel",
                "Automation",
                FontAwesomeIcon.Code, purple,
                new[] { "advanced", "macro", "command", "script" }),

            // Organization
            new("Drag & Drop Everything",
                "Drag characters by their name to reorder. Drag the coloured bar on designs. Drag designs into folders.",
                "Main Window / Design Panel",
                "Organization",
                FontAwesomeIcon.GripVertical, blue,
                new[] { "drag", "drop", "reorder", "organize" }),

            new("Design Folders",
                "Group your designs into folders. Right-click to rename or delete folders.",
                "Design Panel",
                "Organization",
                FontAwesomeIcon.FolderOpen, blue,
                new[] { "folder", "organize", "group" }),

            new("Import Designs",
                "Hold Shift + click the '+' button to copy designs from another character.",
                "Design Panel",
                "Organization",
                FontAwesomeIcon.FileImport, blue,
                new[] { "import", "copy", "share" }),

            new("Tags & Favorites",
                "Tag characters, mark favourites, and filter to find exactly what you need.",
                "Character Form / Main Window",
                "Organization",
                FontAwesomeIcon.Tags, blue,
                new[] { "tag", "favorite", "filter" }),

            new("Wardrobe",
                "A boutique coverflow lookbook for the active character's designs. The focused design sits on a lit stage with the others receding to either side. Drag, flick, scroll, or use the arrow keys to pan; click the focus card to apply, click any side card to bring it forward. The editorial info panel below the cards shows the design's name, mods, last applied time, and edition. Right-click the focus card to set its preview from clipboard or toggle favourite. The hex button at the top-left of the header swaps the gold accent for the active character's nameplate colour. Open with /wardrobe, the hanger button in the Design Panel, or Shift+Click the Designs button on a character card.",
                "/wardrobe, Design Panel button, or Shift+Click Designs button",
                "Organization",
                FontAwesomeIcon.ThLarge, yellow,
                new[] { "wardrobe", "browse", "design", "visual", "preview", "outfit", "boutique", "coverflow", "hanger", "nameplate", "accent" }),

            // Apply to Target
            new("Apply to Target",
                "Apply CS+ characters to other targets like GPose actors. Spawn actors with Brio or Ktisis, target them, then right-click a character card and select 'Apply to Target'.",
                "Right-click character card",
                "Apply to Target",
                FontAwesomeIcon.Crosshairs, green,
                new[] { "target", "gpose", "brio", "ktisis", "actor", "apply" }),

            new("Apply to Target via Quick Switch",
                "Use the Quick Character Switch to apply to targets. Select a character and design, then right-click the Apply button.",
                "Quick Switch > Right-click Apply",
                "Apply to Target",
                FontAwesomeIcon.Bolt, green,
                new[] { "quick", "switch", "target", "apply" }),

            new("Reset Quick Switch Selection",
                "Changed the Quick Switch dropdowns to apply to a target? Ctrl+Right-click Apply to snap back to your current character.",
                "Quick Switch > Ctrl+Right-click Apply",
                "Apply to Target",
                FontAwesomeIcon.Undo, green,
                new[] { "quick", "switch", "reset", "revert" }),

            // RP Profiles
            new("Mini + Expanded Profiles",
                "Every RP Profile has two views: a compact mini view for quick-reference info and an expanded view with content boxes, a banner, and much more room to write. Open your profile and look for the small arrow handle on the right edge: click it to swap between views.",
                "RP Profile > arrow handle on right edge",
                "RP Profiles",
                FontAwesomeIcon.AngleDoubleRight, pink,
                new[] { "mini", "expanded", "expand", "arrow", "handle", "switch", "view", "compact", "full", "profile" }),

            new("Share Your Profile",
                "Private, Direct Share, or Public, you choose who sees your RP profile.",
                "RP Profile Edit",
                "RP Profiles",
                FontAwesomeIcon.Share, pink,
                new[] { "share", "profile", "privacy" }),

            new("View Others' Profiles",
                "Right-click players in party / chat / friends to peek at their character's story.",
                "Right-click menu",
                "RP Profiles",
                FontAwesomeIcon.Eye, pink,
                new[] { "view", "profile", "other" }),

            new("Profile Effects",
                "Add fireflies, butterflies, falling leaves, and custom backgrounds to your profile.",
                "RP Profile Edit",
                "RP Profiles",
                FontAwesomeIcon.Magic, pink,
                new[] { "effects", "particles", "background" }),

            new("Gallery",
                "Browse public profiles, like your favourites, follow interesting players.",
                "/gallery",
                "RP Profiles",
                FontAwesomeIcon.Images, pink,
                new[] { "gallery", "browse", "discover" }),

            // Mod Management
            new("What is Conflict Resolution?",
                "Tired of constantly toggling mods on and off in Penumbra? CR lets you save which mods should be enabled or disabled for each character, and applies them automatically when you switch.",
                "Settings > Conflict Resolution",
                "Mod Management",
                FontAwesomeIcon.QuestionCircle, orange,
                new[] { "conflict", "resolution", "what", "mods" }),

            new("Per-Character Mods",
                "Set up mod states once per character. When you switch characters, CS+ handles enabling and disabling mods for you, no more manual Penumbra toggling.",
                "/select mods",
                "Mod Management",
                FontAwesomeIcon.User, orange,
                new[] { "character", "mods", "enable", "disable" }),

            new("Per-Design Mods",
                "Different mods for different outfits on the same character. Wet skin for your swimsuit design, dry skin for your cosy PJs.",
                "Design Panel > CR section",
                "Mod Management",
                FontAwesomeIcon.Tshirt, orange,
                new[] { "design", "outfit", "mods" }),

            new("Pinned Mods",
                "Got accessories you always wear? Pin mods like your favourite earrings or necklace so they stay enabled no matter which design you switch to.",
                "Mod Manager",
                "Mod Management",
                FontAwesomeIcon.Thumbtack, orange,
                new[] { "pin", "always", "never" }),

            // Capturing Looks
            new("Snapshot",
                "One click to save your current look as a new design. Uses your most recently created Glamourer design.",
                "Design Panel camera icon",
                "Capturing Looks",
                FontAwesomeIcon.Camera, cyan,
                new[] { "snapshot", "save", "capture" }),

            new("Snapshot + Mods",
                "Ctrl+Shift+Click to also capture which mods are currently enabled.",
                "Design Panel (Ctrl+Shift)",
                "Capturing Looks",
                FontAwesomeIcon.CameraRetro, cyan,
                new[] { "snapshot", "mods", "save" }),

            // Customize CS+
            new("Animated Portraits",
                "Pick a GIF or WebP per character, plays on hover in the main roster, freezes on frame 0 otherwise. Perfect for animated character art or pose swaps.",
                "Add / Edit Character > Portrait > Animated Hover",
                "Customize CS+",
                FontAwesomeIcon.PlayCircle, purple,
                new[] { "gif", "webp", "animated", "hover", "portrait", "image" },
                IsNew: true),

            new("Custom Themes",
                "Change every colour, add background images, pick a custom favourite icon.",
                "Settings > Visual > Custom",
                "Customize CS+",
                FontAwesomeIcon.Palette, purple,
                new[] { "theme", "colour", "customize" }),

            new("Seasonal Themes",
                "Halloween, Winter, Christmas, Valentine's, with special visual effects.",
                "Settings > Visual",
                "Customize CS+",
                FontAwesomeIcon.Snowflake, purple,
                new[] { "theme", "halloween", "winter" }),

            new("In-Game File Browser",
                "Trouble with file dialogs? Use the built-in browser. Great for Linux users.",
                "Settings > Behavior",
                "Customize CS+",
                FontAwesomeIcon.FolderOpen, purple,
                new[] { "file", "browser", "linux" }),

            // Backup & Safety
            new("Auto Backups",
                "CS+ backs up your config on updates and weekly. Your characters are safe.",
                "Settings > Backup & Restore",
                "Backup & Safety",
                FontAwesomeIcon.Shield, green,
                new[] { "backup", "auto", "safe" }),

            new("Manual Backups",
                "Create named backups before big changes. Restore anytime.",
                "Settings > Backup & Restore",
                "Backup & Safety",
                FontAwesomeIcon.Save, green,
                new[] { "backup", "manual", "restore" }),

            // Chat Commands
            new("/select <name> [design]",
                "Switch to a character, optionally with a specific design.",
                "Chat",
                "Chat Commands",
                FontAwesomeIcon.Terminal, slate,
                new[] { "command", "select", "switch" }),

            new("/select random [name|group]",
                "Random character & design. Add a name for random design from that character, or a group name for random from a group.",
                "Chat",
                "Chat Commands",
                FontAwesomeIcon.Dice, slate,
                new[] { "command", "random" }),

            new("/select jobchange on|off",
                "Toggle the Reapply Design on Job Change setting.",
                "Chat",
                "Chat Commands",
                FontAwesomeIcon.Sync, slate,
                new[] { "command", "job", "toggle" }),

            new("/select idle|sit|groundsit|doze [0-6]",
                "Check current pose (no number) or set a specific pose.",
                "Chat",
                "Chat Commands",
                FontAwesomeIcon.Child, slate,
                new[] { "command", "pose" }),

            new("/select mods",
                "Open the Conflict Resolution Mod Manager window.",
                "Chat",
                "Chat Commands",
                FontAwesomeIcon.Cogs, slate,
                new[] { "command", "mods" }),

            new("/select save [CR]",
                "Snapshot your current look as a new design. Add CR to include mod states.",
                "Chat",
                "Chat Commands",
                FontAwesomeIcon.Camera, slate,
                new[] { "command", "save", "snapshot" }),

            new("/select whatsnew",
                "Open the patch notes window.",
                "Chat",
                "Chat Commands",
                FontAwesomeIcon.Newspaper, slate,
                new[] { "command", "patch", "notes" }),

            new("/selectswitch",
                "Toggle the Quick Switch overlay window.",
                "Chat",
                "Chat Commands",
                FontAwesomeIcon.Bolt, slate,
                new[] { "command", "quick", "switch" }),

            new("/selectrevert",
                "Revert all CS+ changes (Glamourer, Customize+, Honorific).",
                "Chat",
                "Chat Commands",
                FontAwesomeIcon.Undo, slate,
                new[] { "command", "revert" }),

            new("/viewrp self | t | Name@World",
                "View RP profiles. 'self' for yours, 't' for target, or specify a player.",
                "Chat",
                "Chat Commands",
                FontAwesomeIcon.Eye, slate,
                new[] { "command", "viewrp", "profile" }),

            new("/gallery",
                "Open the Character Gallery.",
                "Chat",
                "Chat Commands",
                FontAwesomeIcon.Images, slate,
                new[] { "command", "gallery" }),

            new("/sidle, /ssit, /sgroundsit, /sdoze [0-6]",
                "Direct pose commands (shorthand for /select idle, etc.).",
                "Chat",
                "Chat Commands",
                FontAwesomeIcon.Walking, slate,
                new[] { "command", "pose", "shorthand" }),
        };
    }

    // Category metadata: ordered by mockup left-rail, with rail glyph and tint.
    private void BuildCategoryMeta()
    {
        var seen = new HashSet<string>();
        var ordered = new List<string>();
        foreach (var f in allFeatures)
            if (seen.Add(f.Category)) ordered.Add(f.Category);
        categoryNames = ordered.ToArray();

        Vector4 yellow = new(1.00f, 0.84f, 0.30f, 1f);
        Vector4 cyan   = new(0.35f, 0.78f, 1.00f, 1f);
        Vector4 pink   = new(1.00f, 0.45f, 0.80f, 1f);
        Vector4 purple = new(0.65f, 0.55f, 1.00f, 1f);
        Vector4 violet = new(0.78f, 0.55f, 1.00f, 1f);
        Vector4 blue   = new(0.50f, 0.72f, 1.00f, 1f);
        Vector4 green  = new(0.42f, 0.92f, 0.60f, 1f);
        Vector4 orange = new(1.00f, 0.65f, 0.30f, 1f);
        Vector4 cyan2  = new(0.30f, 0.92f, 0.92f, 1f);
        Vector4 mint   = new(0.50f, 0.92f, 0.65f, 1f);
        Vector4 slate  = new(0.65f, 0.75f, 0.85f, 1f);

        var byName = new Dictionary<string, CategoryMeta>
        {
            { "Quick Actions",   new("Quick Actions",   "QUICK ACTIONS",  FontAwesomeIcon.Bolt,         yellow) },
            { "Your Identity",   new("Your Identity",   "YOUR IDENTITY",  FontAwesomeIcon.IdCard,       cyan)   },
            { "RP Profiles",     new("RP Profiles",     "RP PROFILES",    FontAwesomeIcon.BookOpen,     pink)   },
            { "Customize CS+",   new("Customize CS+",   "CUSTOMISE",      FontAwesomeIcon.Palette,      purple) },
            { "Automation",      new("Automation",      "AUTOMATION",     FontAwesomeIcon.Sync,         violet) },
            { "Organization",    new("Organization",    "ORGANISATION",   FontAwesomeIcon.FolderOpen,   blue)   },
            { "Apply to Target", new("Apply to Target", "APPLY TO TARGET",FontAwesomeIcon.Crosshairs,   green)  },
            { "Mod Management",  new("Mod Management",  "MOD MANAGEMENT", FontAwesomeIcon.PuzzlePiece,  orange) },
            { "Capturing Looks", new("Capturing Looks", "CAPTURING LOOKS",FontAwesomeIcon.Camera,       cyan2)  },
            { "Backup & Safety", new("Backup & Safety", "BACKUP & SAFETY",FontAwesomeIcon.ShieldAlt,    mint)   },
            { "Chat Commands",   new("Chat Commands",   "CHAT COMMANDS",  FontAwesomeIcon.Terminal,     slate)  },
        };

        categoryMeta = categoryNames
            .Select(n => byName.TryGetValue(n, out var m)
                ? m
                : new CategoryMeta(n, n.ToUpperInvariant(), FontAwesomeIcon.Asterisk, slate))
            .ToArray();
    }

    private int _chromeColorCount = 0;
    public override void PreDraw()
    {
        _chromeColorCount = CharacterSelectPlugin.Windows.Styles.ThemeHelper.PushWindowChromeColors(plugin.Configuration);
    }
    public override void PostDraw()
    {
        CharacterSelectPlugin.Windows.Styles.ThemeHelper.PopWindowChromeColors(_chromeColorCount);
        _chromeColorCount = 0;
    }

    // ── Draw entry ──────────────────────────────────────────────────────
    public override void Draw()
    {
        var scale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;

        // Encore chassis pattern: zero outer padding so the ribbon, header, and
        // body bleed flush with the window border. Inner regions re-push their
        // own gutter.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

        try
        {
            DrawChassis(scale);
            DrawWindowBrackets(scale);
        }
        finally
        {
            ImGui.PopStyleVar(2);
        }
    }

    // ── Chassis composition ────────────────────────────────────────────
    private void DrawChassis(float scale)
    {
        // Anchor the chassis to the cursor (which sits below Dalamud's title
        // bar) and the content region size, NOT the raw window rect. Using
        // GetWindowPos here would draw the ribbon under Dalamud's title bar.
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();

        // Mockup spec: ribbon 30, header 100 (with banner), toolbar 60, rail 200.
        // Bumped from the bare-mockup numbers because at 1x game scale the
        // banner/header read tight and the rail labels needed breathing room.
        float ribbonH  = 30f * scale;
        float headerH  = 100f * scale;
        float toolbarH = 60f * scale;
        float railW    = 200f * scale;

        var ribbonMin  = origin;
        var ribbonMax  = new Vector2(origin.X + avail.X, origin.Y + ribbonH);
        var headerMin  = new Vector2(origin.X, ribbonMax.Y);
        var headerMax  = new Vector2(origin.X + avail.X, headerMin.Y + headerH);
        var toolbarMin = new Vector2(origin.X, headerMax.Y);
        var toolbarMax = new Vector2(origin.X + avail.X, toolbarMin.Y + toolbarH);
        var bodyMin    = new Vector2(origin.X, toolbarMax.Y);
        var bodyMax    = new Vector2(origin.X + avail.X, origin.Y + avail.Y);
        var railMin    = bodyMin;
        var railMax    = new Vector2(bodyMin.X + railW, bodyMax.Y);
        var contentMin = new Vector2(railMax.X, bodyMin.Y);
        var contentMax = bodyMax;

        var dl = ImGui.GetWindowDrawList();
        DrawRibbon(dl, ribbonMin, ribbonMax, scale);
        DrawHeader(dl, headerMin, headerMax, scale);
        DrawToolbar(dl, toolbarMin, toolbarMax, scale);
        DrawBodyBackdrop(dl, bodyMin, bodyMax);
        DrawNavRail(dl, railMin, railMax, scale);
        DrawContent(contentMin, contentMax, scale);
    }

    // ── Ribbon ─────────────────────────────────────────────────────────
    // Sub-pixel text positions blur with bilinear-filtered fonts, so every
    // text origin in this method snaps to whole pixels via Pix(...) before
    // drawing. The pip is also sized to even values so its centre lands on
    // an integer pixel.
    private void DrawRibbon(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
    {
        BoutiqueChassis.DrawRibbonBackground(dl, min, max, scale);

        float padX = 14f * scale;
        float midY = MathF.Round((min.Y + max.Y) * 0.5f);

        // Pulsing gold pip at the left. Use 6 px size so half-extent is 3 (integer).
        double time = ImGui.GetTime();
        float pipPulse = 0.55f + 0.45f * (float)Math.Sin(time * 2.4);
        float pipHalf = MathF.Round(3f * scale);
        float pipGlowR = 8f * scale;
        var pipCentre = new Vector2(MathF.Round(min.X + padX + pipHalf), midY);
        for (int g = 3; g >= 1; g--)
        {
            float pad = pipGlowR * g / 3f;
            uint glowCol = Boutique.U32(Boutique.WithAlpha(Boutique.GoldWarm, 0.22f * pipPulse / g));
            dl.AddRectFilled(pipCentre - new Vector2(pad, pad), pipCentre + new Vector2(pad, pad), glowCol);
        }
        dl.AddRectFilled(pipCentre - new Vector2(pipHalf, pipHalf), pipCentre + new Vector2(pipHalf, pipHalf),
            Boutique.U32(Boutique.Gold));

        // Meta text: FEATURES [dot] GUIDE TO THE PLUGIN
        // The middle-dot glyph U+00B7 is OUTSIDE Oswald's atlas (display
        // variants ship Basic Latin only, no Latin-1 Supplement) so it
        // renders as `?`. Substituting a draw-list filled circle.
        float textX = pipCentre.X + pipHalf + 12f * scale;
        using (Plugin.Instance?.OswaldMed13?.Push())
        {
            float fH = ImGui.GetFontSize();
            float textY = MathF.Round(midY - fH * 0.5f);
            float trk = Boutique.Track22(fH);

            textX += DrawCrispTracked(dl, new Vector2(textX, textY),
                "FEATURES", Boutique.U32(Boutique.Text), trk) + 10f * scale;
            float dotR = MathF.Max(1.5f, MathF.Round(2f * scale));
            var dotC = new Vector2(MathF.Round(textX + dotR), MathF.Round(midY));
            dl.AddCircleFilled(dotC, dotR, Boutique.U32(Boutique.GoldDeep), 12);
            textX += dotR * 2 + 10f * scale;
            DrawCrispTracked(dl, new Vector2(textX, textY),
                "GUIDE TO THE PLUGIN", Boutique.U32(Boutique.TextDim), Boutique.Track18(fH));
        }

        // Right-aligned count tag. Solid dark backdrop (was gold@6% which let
        // the ribbon gradient bleed through and made the small text look
        // muddy/blurry over the underlying colour shifts).
        using (Plugin.Instance?.OswaldSemi11?.Push())
        {
            string tag = $"{allFeatures.Count} ENTRIES";
            float tagFH = ImGui.GetFontSize();
            float trkTag = Boutique.Track22(tagFH);
            float tagW = Boutique.MeasureTrackedText(tag, trkTag);
            float tagPadX = 10f * scale;
            float tagPadY = 4f * scale;
            float tagBoxW = MathF.Round(tagPadX * 2f + tagW);
            float tagBoxH = MathF.Round(tagFH + tagPadY * 2f);
            float tagRightPad = 14f * scale;
            float tagMaxX = MathF.Round(max.X - tagRightPad);
            float tagMaxY = MathF.Round(midY + tagBoxH * 0.5f);
            float tagMinX = tagMaxX - tagBoxW;
            float tagMinY = tagMaxY - tagBoxH;
            var tagMin = new Vector2(tagMinX, tagMinY);
            var tagMax = new Vector2(tagMaxX, tagMaxY);

            dl.AddRectFilled(tagMin, tagMax, Boutique.U32(new Vector4(0.02f, 0.03f, 0.05f, 0.92f)));
            dl.AddRect(tagMin, tagMax, Boutique.U32(Boutique.GoldDeep), 0f, ImDrawFlags.None, 1f);
            DrawCrispTracked(dl,
                new Vector2(tagMin.X + tagPadX, midY - tagFH * 0.5f),
                tag, Boutique.U32(Boutique.GoldWarm), trkTag);
        }
    }

    // Whole-pixel snap to keep tracked text crisp under bilinear font sampling.
    private static Vector2 Pix(float x, float y) => new(MathF.Round(x), MathF.Round(y));

    // Crisp tracked text: per-glyph X is rounded so the bilinear sampler
    // always lands on whole pixels. Returns un-rounded advance for layout math.
    private static float DrawCrispTracked(ImDrawListPtr dl, Vector2 pos, string text,
        uint colour, float trackPx)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        float x = pos.X;
        float y = MathF.Round(pos.Y);
        float startX = x;
        for (int i = 0; i < text.Length; i++)
        {
            string g = text.Substring(i, 1);
            dl.AddText(new Vector2(MathF.Round(x), y), colour, g);
            x += ImGui.CalcTextSize(g).X;
            if (i < text.Length - 1) x += trackPx;
        }
        return x - startX;
    }

    // ── Header (banner-backed) ─────────────────────────────────────────
    private void DrawHeader(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
    {
        // Base dark gradient first, banner sits on top of this
        uint bgTop = Boutique.U32(new Vector4(0x0C / 255f, 0x0E / 255f, 0x14 / 255f, 1f));
        uint bgBot = Boutique.U32(Boutique.Bg);
        dl.AddRectFilledMultiColor(min, max, bgTop, bgTop, bgBot, bgBot);

        // Banner image at 0.55 alpha, cropped to cover with center-35% anchoring.
        IDalamudTextureWrap? bannerTex = null;
        try
        {
            string pluginDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "";
            string imagePath = Path.Combine(pluginDirectory, "Assets", "Feature Banner.png");
            if (File.Exists(imagePath))
                bannerTex = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
        }
        catch (Exception ex) { Plugin.Log.Error($"Features banner load failed: {ex.Message}"); }

        if (bannerTex != null)
        {
            // Centre-crop the banner so the warmest cloud is high in the header.
            float headerW = max.X - min.X;
            float headerH = max.Y - min.Y;
            float texW = bannerTex.Width;
            float texH = bannerTex.Height;
            float texAspect = texW / texH;
            float headerAspect = headerW / headerH;

            Vector2 uvMin = Vector2.Zero, uvMax = Vector2.One;
            if (texAspect > headerAspect)
            {
                // Tex is wider than header: crop horizontally, centre.
                float scaleU = headerAspect / texAspect;
                uvMin.X = (1f - scaleU) * 0.5f;
                uvMax.X = uvMin.X + scaleU;
            }
            else
            {
                // Tex is taller than header: crop vertically, anchor at center 35%.
                float scaleV = texAspect / headerAspect;
                uvMin.Y = MathF.Max(0f, 0.35f - scaleV * 0.5f);
                uvMax.Y = uvMin.Y + scaleV;
            }

            uint imgTint = Boutique.U32(new Vector4(1f, 1f, 1f, 0.85f));
            dl.AddImage((ImTextureID)bannerTex.Handle, min, max, uvMin, uvMax, imgTint);
        }

        // Veil ramp tuned so the banner colour comes through clearly while
        // still giving the centred title + subtitle enough contrast.
        uint veilTop = Boutique.U32(new Vector4(0.016f, 0.020f, 0.039f, 0.28f));
        uint veilBot = Boutique.U32(new Vector4(0.016f, 0.020f, 0.039f, 0.55f));
        dl.AddRectFilledMultiColor(min, max, veilTop, veilTop, veilBot, veilBot);

        // Discreet text band: a horizontal gradient strip behind the title +
        // subtitle so the type sits on a darker bed regardless of which colour
        // happens to be behind it in the banner. Centre is darker, edges fade
        // to transparent so the band reads as atmosphere not a panel.
        float bandTop = min.Y + 14f * scale;
        float bandBot = max.Y - 18f * scale;
        float bandMid = (min.X + max.X) * 0.5f;
        float bandHalfW = MathF.Min((max.X - min.X) * 0.35f, 320f * scale);
        uint bandClear = Boutique.U32(new Vector4(0f, 0f, 0f, 0f));
        uint bandDark = Boutique.U32(new Vector4(0f, 0f, 0f, 0.45f));
        dl.AddRectFilledMultiColor(
            new Vector2(bandMid - bandHalfW, bandTop),
            new Vector2(bandMid, bandBot),
            bandClear, bandDark, bandDark, bandClear);
        dl.AddRectFilledMultiColor(
            new Vector2(bandMid, bandTop),
            new Vector2(bandMid + bandHalfW, bandBot),
            bandDark, bandClear, bandClear, bandDark);

        // Soft gold radial wash at the bottom-centre to keep the chassis warm.
        BoutiqueChassis.DrawAuroraSpot(dl,
            new Vector2((min.X + max.X) * 0.5f, max.Y),
            260f * scale, 70f * scale,
            Boutique.WithAlpha(Boutique.Gold, 0.10f), 8);

        // Drifting gold motes for atmosphere (replaces the old particle layer).
        DrawHeaderMotes(dl, min, max, scale);

        // Centred title "FEATURES" in tracked-caps Oswald with a 2 px shadow.
        float titleY = MathF.Round(min.Y + 18f * scale);
        using (Plugin.Instance?.OswaldSemiMid?.Push())
        {
            float fH = ImGui.GetFontSize();
            float trk = Boutique.Track32(fH);
            string title = "FEATURES";
            float titleW = Boutique.MeasureTrackedText(title, trk);
            float titleX = MathF.Round((min.X + max.X) * 0.5f - titleW * 0.5f);
            DrawCrispTracked(dl,
                new Vector2(titleX + MathF.Round(2f * scale), titleY + MathF.Round(2f * scale)),
                title, Boutique.U32(new Vector4(0f, 0f, 0f, 0.70f)), trk);
            DrawCrispTracked(dl, new Vector2(titleX, titleY),
                title, Boutique.U32(Boutique.Text), trk);
        }

        // Subtitle: TIPS [diamond] TRICKS [diamond] HIDDEN GEMS in gold-at-75%.
        using (Plugin.Instance?.OswaldSemi11?.Push())
        {
            float fH = ImGui.GetFontSize();
            float trk = Boutique.Track40(fH);
            string a = "TIPS";
            string b = "TRICKS";
            string c = "HIDDEN GEMS";
            float aW = Boutique.MeasureTrackedText(a, trk);
            float bW = Boutique.MeasureTrackedText(b, trk);
            float cW = Boutique.MeasureTrackedText(c, trk);
            float gap = 14f * scale;
            float dia = 5f * scale;
            float total = aW + gap + dia * 2 + gap + bW + gap + dia * 2 + gap + cW;
            float subX = MathF.Round((min.X + max.X) * 0.5f - total * 0.5f);
            float subY = MathF.Round(titleY + 38f * scale);

            uint subCol = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.75f));
            uint diaCol = Boutique.U32(Boutique.GoldWarm);
            float diaY = MathF.Round(subY + fH * 0.5f);
            float x = subX;
            x += DrawCrispTracked(dl, new Vector2(x, subY), a, subCol, trk);
            x += gap;
            DrawDiamond(dl, new Vector2(MathF.Round(x + dia), diaY), dia, diaCol);
            x += dia * 2 + gap;
            x += DrawCrispTracked(dl, new Vector2(x, subY), b, subCol, trk);
            x += gap;
            DrawDiamond(dl, new Vector2(MathF.Round(x + dia), diaY), dia, diaCol);
            x += dia * 2 + gap;
            DrawCrispTracked(dl, new Vector2(x, subY), c, subCol, trk);
        }

        // Half-width gold-fade rule along the header bottom edge.
        float ruleY = max.Y - 4f * scale;
        float ruleW = (max.X - min.X) * 0.5f;
        float ruleX = (min.X + max.X) * 0.5f - ruleW * 0.5f;
        uint goldStrong = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.50f));
        uint goldClear = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0f));
        dl.AddRectFilledMultiColor(
            new Vector2(ruleX, ruleY),
            new Vector2(ruleX + ruleW * 0.5f, ruleY + 1f),
            goldClear, goldStrong, goldStrong, goldClear);
        dl.AddRectFilledMultiColor(
            new Vector2(ruleX + ruleW * 0.5f, ruleY),
            new Vector2(ruleX + ruleW, ruleY + 1f),
            goldStrong, goldClear, goldClear, goldStrong);
    }

    // Six gold motes drifting in the header. Per-mote phase keeps motion organic.
    private void DrawHeaderMotes(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
    {
        float w = max.X - min.X;
        float h = max.Y - min.Y;
        float t = (float)ImGui.GetTime();

        ReadOnlySpan<(float bx, float by, float phase, float speed, float bright)> motes = stackalloc (float, float, float, float, float)[]
        {
            (0.18f, 0.30f, 0.0f, 0.42f, 0.65f),
            (0.30f, 0.62f, 1.6f, 0.38f, 0.55f),
            (0.50f, 0.42f, 3.1f, 0.46f, 0.50f),
            (0.68f, 0.22f, 4.8f, 0.40f, 0.60f),
            (0.78f, 0.70f, 6.4f, 0.36f, 0.45f),
            (0.14f, 0.78f, 8.0f, 0.44f, 0.50f),
        };

        for (int i = 0; i < motes.Length; i++)
        {
            var m = motes[i];
            float driftX = MathF.Sin((t + m.phase) * m.speed) * 4f * scale;
            float driftY = MathF.Cos((t + m.phase) * m.speed * 0.8f) * 6f * scale;
            var c = new Vector2(min.X + m.bx * w + driftX, min.Y + m.by * h + driftY);
            float r = 1.4f * scale;
            // Outer halo
            dl.AddCircleFilled(c, r * 2.6f, Boutique.U32(Boutique.WithAlpha(Boutique.GoldWarm, 0.15f * m.bright)), 12);
            dl.AddCircleFilled(c, r * 1.6f, Boutique.U32(Boutique.WithAlpha(Boutique.GoldBright, 0.30f * m.bright)), 12);
            // Core
            dl.AddCircleFilled(c, r, Boutique.U32(Boutique.WithAlpha(Boutique.GoldBright, 0.85f * m.bright)), 12);
        }
    }

    // ── Toolbar (search) ───────────────────────────────────────────────
    private void DrawToolbar(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
    {
        // Toolbar bg: solid Bg (matches the body's top, no banner bleed-through).
        // Two soft hairlines, gold-deep above + BorderSoft below, so the toolbar
        // reads as a discrete band between the header and the body.
        dl.AddRectFilled(min, max, Boutique.U32(Boutique.Bg));
        dl.AddLine(new Vector2(min.X, min.Y), new Vector2(max.X, min.Y),
            Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.18f)), 1f);
        dl.AddLine(new Vector2(min.X, max.Y - 1f), new Vector2(max.X, max.Y - 1f),
            Boutique.U32(Boutique.BorderSoft), 1f);

        float padX = 22f * scale;
        float pillH = 36f * scale;
        float pillY = (min.Y + max.Y) * 0.5f - pillH * 0.5f;
        var pillMin = new Vector2(min.X + padX, pillY);
        var pillMax = new Vector2(max.X - padX, pillY + pillH);

        int visibleCount = ComputeFilteredCount();

        // Pill background: solid surface so the search bar reads as a discrete
        // input, not a translucent slip floating over the toolbar gradient.
        float chamfer = 6f * scale;
        var pillFill = new Vector4(0.05f, 0.06f, 0.08f, 1f);
        Boutique.FillSlip(dl, pillMin, pillMax, chamfer, Boutique.U32(pillFill));

        // Magnifier glyph at left
        float glyphPx = 16f * scale;
        var glyphFont = UiBuilder.IconFont;
        string glyph = FontAwesomeIcon.Search.ToIconString();
        ImGui.PushFont(glyphFont);
        var glyphSz = ImGui.CalcTextSize(glyph);
        ImGui.PopFont();
        float glyphRatio = glyphPx / glyphFont.FontSize;
        var glyphPos = new Vector2(
            pillMin.X + 16f * scale,
            (pillMin.Y + pillMax.Y) * 0.5f - glyphSz.Y * glyphRatio * 0.5f);
        dl.AddText(glyphFont, glyphPx, glyphPos, Boutique.U32(Boutique.GoldWarm), glyph);

        // Right hint: "N / TOTAL ENTRIES" with a divider before it
        string hintNum = visibleCount == allFeatures.Count
            ? $"{allFeatures.Count}"
            : $"{visibleCount} / {allFeatures.Count}";
        string hintRest = " ENTRIES";
        float hintFH;
        float hintTotal;
        float hintNumW;
        using (Plugin.Instance?.OswaldSemi11?.Push())
        {
            hintFH = ImGui.GetFontSize();
            float trk = Boutique.Track30(hintFH);
            hintNumW = Boutique.MeasureTrackedText(hintNum, trk);
            hintTotal = hintNumW + Boutique.MeasureTrackedText(hintRest, trk);
        }
        float hintRightX = pillMax.X - 16f * scale;
        float hintLeftX = hintRightX - hintTotal;
        float dividerX = hintLeftX - 14f * scale;

        // Vertical divider
        dl.AddLine(
            new Vector2(dividerX, pillMin.Y + 8f * scale),
            new Vector2(dividerX, pillMax.Y - 8f * scale),
            Boutique.U32(Boutique.BorderSoft), 1f);

        using (Plugin.Instance?.OswaldSemi11?.Push())
        {
            float trk = Boutique.Track30(hintFH);
            float yMid = (pillMin.Y + pillMax.Y) * 0.5f - hintFH * 0.5f;
            float x = hintLeftX;
            Boutique.DrawTrackedText(dl, new Vector2(x, yMid), hintNum, Boutique.U32(Boutique.GoldWarm), trk);
            Boutique.DrawTrackedText(dl, new Vector2(x + hintNumW, yMid), hintRest,
                Boutique.U32(Boutique.TextFaint), trk);
        }

        // InputText with a body font pushed for legibility (defaults are too
        // small in this pill). Transparent frame so the slip ground shows.
        float inputX = glyphPos.X + glyphSz.X * glyphRatio + 12f * scale;
        float inputW = dividerX - inputX - 10f * scale;

        bool focused;
        using (Plugin.Instance?.OutfitBody13?.Push())
        {
            float inputPadY = MathF.Max(0f, (pillH - ImGui.GetTextLineHeight()) * 0.5f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0f, inputPadY));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.Text, Boutique.Text);
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, Boutique.TextFaint);

            ImGui.SetCursorScreenPos(new Vector2(inputX, pillMin.Y));
            ImGui.SetNextItemWidth(inputW);
            ImGui.InputTextWithHint("##features_search",
                "Search features, locations, keywords...", ref searchQuery, 100);
            focused = ImGui.IsItemActive();

            ImGui.PopStyleColor(5);
            ImGui.PopStyleVar(2);
        }

        // Pill border (gold-deep on focus)
        Boutique.StrokeSlip(dl, pillMin, pillMax, chamfer,
            Boutique.U32(focused ? Boutique.GoldDeep : Boutique.BorderSoft), 1f);
        if (focused)
        {
            // Faint gold halo inset
            Boutique.StrokeSlip(dl,
                new Vector2(pillMin.X + 1f * scale, pillMin.Y + 1f * scale),
                new Vector2(pillMax.X - 1f * scale, pillMax.Y - 1f * scale),
                chamfer - 1f * scale,
                Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.12f)), 1f);
        }
    }

    private int ComputeFilteredCount()
    {
        return allFeatures.Count(f => MatchesFilter(f));
    }

    private bool MatchesFilter(FeatureEntry f)
    {
        if (activeCategoryIndex > 0)
        {
            int catIdx = activeCategoryIndex - 1;
            if (catIdx >= categoryNames.Length || f.Category != categoryNames[catIdx])
                return false;
        }
        if (string.IsNullOrWhiteSpace(searchQuery)) return true;

        string q = searchQuery.Trim().ToLowerInvariant();
        if (f.Name.ToLowerInvariant().Contains(q)) return true;
        if (f.Description.ToLowerInvariant().Contains(q)) return true;
        if (f.Location.ToLowerInvariant().Contains(q)) return true;
        if (f.Category.ToLowerInvariant().Contains(q)) return true;
        foreach (var k in f.Keywords) if (k.Contains(q)) return true;
        return false;
    }

    // ── Body backdrop ──────────────────────────────────────────────────
    private void DrawBodyBackdrop(ImDrawListPtr dl, Vector2 min, Vector2 max)
    {
        // Velvet vertical gradient like Settings
        Vector4 top = Boutique.Velvet;
        Vector4 bot = Boutique.Lerp(top, new Vector4(0f, 0f, 0f, top.W), 0.55f);
        dl.AddRectFilledMultiColor(min, max, Boutique.U32(top), Boutique.U32(top),
            Boutique.U32(bot), Boutique.U32(bot));
    }

    // ── Nav rail ───────────────────────────────────────────────────────
    private void DrawNavRail(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
    {
        // Rail backdrop
        dl.AddRectFilled(min, max,
            Boutique.U32(new Vector4(0x08 / 255f, 0x0A / 255f, 0x0E / 255f, 0.55f)));

        // Right-edge gold accent hairline (fade top + bottom)
        uint goldFade = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0f));
        uint goldMid = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.25f));
        float fadeH = (max.Y - min.Y) * 0.18f;
        dl.AddRectFilledMultiColor(
            new Vector2(max.X - 1f, min.Y),
            new Vector2(max.X, min.Y + fadeH),
            goldFade, goldFade, goldMid, goldMid);
        dl.AddRectFilled(
            new Vector2(max.X - 1f, min.Y + fadeH),
            new Vector2(max.X, max.Y - fadeH),
            goldMid);
        dl.AddRectFilledMultiColor(
            new Vector2(max.X - 1f, max.Y - fadeH),
            new Vector2(max.X, max.Y),
            goldMid, goldMid, goldFade, goldFade);

        // BorderSoft underneath the gold (settings mockup parity)
        dl.AddLine(new Vector2(max.X - 1f, min.Y), new Vector2(max.X - 1f, max.Y),
            Boutique.U32(Boutique.BorderSoft), 1f);

        // Layout. Items are 40 px so the bumped Oswald label has comfortable
        // top + bottom padding; the ALL row + dividers + 11 categories all
        // fit in the visible rail at the default window height without
        // forcing a scrollbar.
        float padTop = 18f * scale;
        float capPadX = 18f * scale;
        float itemH = 40f * scale;

        // CATEGORIES cap with gold-fade underline
        using (Plugin.Instance?.OswaldMed13?.Push())
        {
            float trk = 4.6f * scale;
            Boutique.DrawTrackedText(dl,
                new Vector2(min.X + capPadX, min.Y + padTop),
                "CATEGORIES", Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.65f)), trk);
        }
        float capUlY = min.Y + padTop + 22f * scale;
        uint goldDeepCol = Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.55f));
        uint goldDeepClear = Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0f));
        dl.AddRectFilledMultiColor(
            new Vector2(min.X + capPadX, capUlY),
            new Vector2(max.X - capPadX, capUlY + 1f),
            goldDeepCol, goldDeepClear, goldDeepClear, goldDeepCol);

        // Item list inside a child so it can scroll if the window is short.
        float listTop = capUlY + 14f * scale;
        float listH = max.Y - listTop - 8f * scale;
        ImGui.SetCursorScreenPos(new Vector2(min.X, listTop));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginChild("##features_rail_items", new Vector2(max.X - min.X, listH),
            false, ImGuiWindowFlags.NoScrollbar);

        var railDl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        float rowW = max.X - min.X;

        // Row 0 = ALL pseudo-category, then categoryMeta items. A single
        // divider hairline separates ALL from the rest; another sits just
        // before Chat Commands so commands read as a footer block. Each
        // InvisibleButton inside DrawNavRailItem already advances ImGui's
        // cursor by itemH, so a final Dummy here would double the child's
        // scroll height (Settings has the same comment).
        int totalItems = 1 + categoryMeta.Length;
        float y = origin.Y;
        for (int i = 0; i < totalItems; i++)
        {
            // Divider after ALL
            if (i == 1)
                DrawRailDivider(railDl, new Vector2(origin.X, y), rowW, scale);
            // Divider before Chat Commands
            if (i > 0 && i - 1 < categoryMeta.Length
                && categoryMeta[i - 1].Name == "Chat Commands")
                DrawRailDivider(railDl, new Vector2(origin.X, y), rowW, scale);

            DrawNavRailItem(railDl, i,
                new Vector2(origin.X, y),
                new Vector2(origin.X + rowW, y + itemH),
                scale);
            y += itemH;
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private void DrawRailDivider(ImDrawListPtr dl, Vector2 pos, float rowW, float scale)
    {
        float pad = 18f * scale;
        float y = pos.Y + 4f * scale;
        uint mid = Boutique.U32(Boutique.WithAlpha(Boutique.BorderSoft, 1f));
        uint clr = Boutique.U32(Boutique.WithAlpha(Boutique.BorderSoft, 0f));
        dl.AddRectFilledMultiColor(
            new Vector2(pos.X + pad, y),
            new Vector2(pos.X + pad + (rowW - pad * 2) * 0.5f, y + 1f),
            clr, mid, mid, clr);
        dl.AddRectFilledMultiColor(
            new Vector2(pos.X + pad + (rowW - pad * 2) * 0.5f, y),
            new Vector2(pos.X + rowW - pad, y + 1f),
            mid, clr, clr, mid);
    }

    private void DrawNavRailItem(ImDrawListPtr dl, int index, Vector2 min, Vector2 max, float scale)
    {
        bool isAll = index == 0;
        bool isActive = index == activeCategoryIndex;
        float midY = (min.Y + max.Y) * 0.5f;

        // Hit region
        ImGui.SetCursorScreenPos(min);
        bool clicked = ImGui.InvisibleButton($"##fwnav_{index}", max - min);
        bool hovered = ImGui.IsItemHovered();

        // Background: hover gold@4%, active gold gradient fade
        if (isActive)
        {
            uint a1 = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.10f));
            uint a2 = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.02f));
            uint a3 = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0f));
            float w = max.X - min.X;
            float seam = min.X + w * 0.80f;
            dl.AddRectFilledMultiColor(min, new Vector2(seam, max.Y), a1, a2, a2, a1);
            dl.AddRectFilledMultiColor(new Vector2(seam, min.Y), max, a2, a3, a3, a2);
        }
        else if (hovered)
        {
            dl.AddRectFilled(min, max, Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.04f)));
        }

        // Active accent: 3px gold left bar with halo
        if (isActive)
        {
            var barMin = new Vector2(min.X, min.Y + 7f * scale);
            var barMax = new Vector2(min.X + 3f * scale, max.Y - 7f * scale);
            for (int g = 3; g >= 1; g--)
            {
                float pad = g * 1.5f * scale;
                var gMin = new Vector2(barMin.X - pad, barMin.Y - pad);
                var gMax = new Vector2(barMax.X + pad, barMax.Y + pad);
                uint gCol = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.10f / g));
                dl.AddRectFilled(gMin, gMax, gCol);
            }
            dl.AddRectFilled(barMin, barMax, Boutique.U32(Boutique.GoldWarm));
        }

        // Icon, label, count
        FontAwesomeIcon icon;
        Vector4 tint;
        string label;
        int count;
        if (isAll)
        {
            icon = FontAwesomeIcon.Asterisk;
            tint = Boutique.GoldWarm;
            label = "ALL";
            count = allFeatures.Count;
        }
        else
        {
            var meta = categoryMeta[index - 1];
            icon = meta.Icon;
            tint = meta.Tint;
            label = meta.ShortLabel;
            count = allFeatures.Count(f => f.Category == meta.Name);
        }

        // Icon (18px, anchored at fixed gutter)
        float iconGutter = min.X + 26f * scale;
        float iconSize = 18f * scale;
        ImGui.PushFont(UiBuilder.IconFont);
        var iconNat = ImGui.CalcTextSize(icon.ToIconString());
        ImGui.PopFont();
        float iconRatio = iconSize / UiBuilder.IconFont.FontSize;
        var iconDrawn = iconNat * iconRatio;
        var iconPos = new Vector2(iconGutter - iconDrawn.X * 0.5f, midY - iconDrawn.Y * 0.5f);

        Vector4 iconCol;
        if (isActive)      iconCol = tint;
        else if (hovered)  iconCol = Boutique.WithAlpha(tint, 0.90f);
        else               iconCol = Boutique.WithAlpha(tint, 0.62f);
        dl.AddText(UiBuilder.IconFont, iconSize, iconPos, Boutique.U32(iconCol), icon.ToIconString());

        // Reserve count column on the right (tabular numerals, slightly bumped)
        float labelX = iconGutter + 20f * scale;
        float countW;
        using (Plugin.Instance?.OswaldMed11?.Push())
        {
            float fH = ImGui.GetFontSize();
            float trk = 1.8f * scale;
            countW = Boutique.MeasureTrackedText(count.ToString(), trk);
            Vector4 countCol;
            if (isActive)     countCol = Boutique.GoldWarm;
            else if (hovered) countCol = Boutique.GoldDeep;
            else              countCol = Boutique.WithAlpha(Boutique.TextFaint, 0.95f);
            Boutique.DrawTrackedText(dl,
                new Vector2(max.X - 16f * scale - countW, midY - fH * 0.5f),
                count.ToString(), Boutique.U32(countCol), trk);
        }

        // Label: bumped to OswaldSemi13 so the rail is comfortable to scan
        // without leaning into the screen.
        using (Plugin.Instance?.OswaldSemi13?.Push())
        {
            float fH = ImGui.GetFontSize();
            float trk = 2.2f * scale;
            float labelMaxW = (max.X - 16f * scale - countW - 12f * scale) - labelX;
            string trimmed = Boutique.TruncateTrackedToWidth(label, trk, labelMaxW);
            Vector4 labelCol;
            if (isActive)      labelCol = Boutique.Text;
            else if (hovered)  labelCol = Boutique.GoldWarm;
            else               labelCol = Boutique.WithAlpha(Boutique.Text, 0.82f);
            Boutique.DrawTrackedText(dl,
                new Vector2(labelX, midY - fH * 0.5f),
                trimmed, Boutique.U32(labelCol), trk);
        }

        // Active right-edge chevron tab
        if (isActive)
        {
            double now = ImGui.GetTime();
            float t = (float)Math.Clamp(now - categoryChangeT, 0, 1);
            float eased = 1f - MathF.Pow(1f - t, 3f);
            float cs = 3.5f * scale;
            float chevOffset = 6f * (1f - eased) * scale;
            var cTip = new Vector2(max.X - cs * 1.6f - chevOffset, midY);
            var cTop = new Vector2(cTip.X + cs, cTip.Y - cs);
            var cBot = new Vector2(cTip.X + cs, cTip.Y + cs);
            dl.AddTriangleFilled(cTop, cBot, cTip,
                Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.55f * eased)));
        }

        if (clicked && index != activeCategoryIndex)
        {
            activeCategoryIndex = index;
            categoryChangeT = ImGui.GetTime();
        }
    }

    // Content body. Scrollbar sits flush with the window border; content's
    // own WindowPadding keeps section heads off the scrollbar.
    private void DrawContent(Vector2 min, Vector2 max, float scale)
    {
        float padLeft = 18f * scale;
        float padTop  = 14f * scale;
        float padBot  = 14f * scale;
        float padRightInside = 18f * scale;
        float padLeftInside  = 0f;

        ImGui.SetCursorScreenPos(new Vector2(min.X + padLeft, min.Y + padTop));

        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Boutique.WithAlpha(Boutique.Gold, 0.30f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Boutique.WithAlpha(Boutique.Gold, 0.55f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Boutique.WithAlpha(Boutique.Gold, 0.75f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padLeftInside, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 0f));

        try
        {
            float childW = max.X - min.X - padLeft;
            float childH = max.Y - min.Y - padTop - padBot;
            if (childW < 100f) childW = 100f;
            if (childH < 100f) childH = 100f;

            ImGui.BeginChild("##features_content", new Vector2(childW, childH),
                false, ImGuiWindowFlags.None);

            // Inner avail already excludes the scrollbar, so subtracting the
            // inside-right padding gives the actual usable content width.
            float innerW = ImGui.GetContentRegionAvail().X - padRightInside;
            if (innerW < 80f) innerW = 80f;

            DrawContentInner(scale, innerW);

            ImGui.EndChild();
        }
        finally
        {
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(5);
        }
    }

    private void DrawContentInner(float scale, float contentW)
    {
        // Determine which categories are visible after filtering.
        var visibleByCat = new Dictionary<string, List<FeatureEntry>>();
        foreach (var f in allFeatures)
        {
            if (!MatchesFilter(f)) continue;
            if (!visibleByCat.TryGetValue(f.Category, out var list))
            {
                list = new List<FeatureEntry>();
                visibleByCat[f.Category] = list;
            }
            list.Add(f);
        }

        if (visibleByCat.Count == 0)
        {
            DrawEmptyState(scale, contentW);
            return;
        }

        int sectionIdx = 0;
        for (int i = 0; i < categoryNames.Length; i++)
        {
            var cat = categoryNames[i];
            if (!visibleByCat.TryGetValue(cat, out var entries)) continue;

            DrawSectionHead(scale, contentW, i + 1, cat, entries.Count);
            DrawGroupCard(scale, contentW, entries);
            sectionIdx++;
        }

        // Bottom breathing room
        ImGui.Dummy(new Vector2(contentW, 8f * scale));
    }

    private void DrawSectionHead(float scale, float contentW, int oneBasedIdx, string title, int entryCount)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        float padTop = 18f * scale;
        var headTop = pos.Y + padTop;

        // Find the meta for the icon + tint
        var meta = categoryMeta.FirstOrDefault(m => m.Name == title);

        // Title font height up-front so the glyph + numeral can centre on its
        // midline. OswaldSemiSmall (~21 px baked) is a step down from
        // OswaldSemiMidSmall, sized so the title and the bumped numeral feel
        // balanced rather than the title dominating.
        float titleH;
        using (Plugin.Instance?.OswaldSemiSmall?.Push()) { titleH = ImGui.GetFontSize(); }

        float headBaseY = headTop;
        float cursorX = pos.X;
        float midY = headBaseY + titleH * 0.5f;

        // Tinted glyph in the category's accent colour. Centred on the
        // title's midline.
        float iconSize = 17f * scale;
        ImGui.PushFont(UiBuilder.IconFont);
        var iconNat = ImGui.CalcTextSize(meta.Icon.ToIconString());
        ImGui.PopFont();
        float iconRatio = iconSize / UiBuilder.IconFont.FontSize;
        var iconDrawn = iconNat * iconRatio;
        var iconPos = Pix(cursorX, midY - iconDrawn.Y * 0.5f);
        dl.AddText(UiBuilder.IconFont, iconSize, iconPos,
            Boutique.U32(Boutique.WithAlpha(meta.Tint, 0.95f)), meta.Icon.ToIconString());
        cursorX += iconDrawn.X + 14f * scale;

        // Numeral "01 //" bumped to OswaldSemi13 (~17 px baked) so it reads
        // alongside the title rather than fading into the rule beneath it.
        string numeral = $"{oneBasedIdx:00} //";
        using (Plugin.Instance?.OswaldSemi13?.Push())
        {
            float numFH = ImGui.GetFontSize();
            float trk = Boutique.Track32(numFH);
            float numW = Boutique.MeasureTrackedText(numeral, trk);
            DrawCrispTracked(dl,
                new Vector2(cursorX, midY - numFH * 0.5f),
                numeral, Boutique.U32(Boutique.GoldWarm), trk);
            cursorX += numW + 14f * scale;
        }

        // Title (OswaldSemiSmall, ~21 px baked). Step down from the previous
        // ~26 px size; reads at a glance without overshadowing the numeral.
        using (Plugin.Instance?.OswaldSemiSmall?.Push())
        {
            float trk = Boutique.Track28(titleH);
            DrawCrispTracked(dl,
                new Vector2(cursorX + MathF.Round(1.5f * scale), headBaseY + MathF.Round(1.5f * scale)),
                title.ToUpperInvariant(),
                Boutique.U32(new Vector4(0, 0, 0, 0.55f)), trk);
            DrawCrispTracked(dl,
                new Vector2(cursorX, headBaseY),
                title.ToUpperInvariant(), Boutique.U32(Boutique.Text), trk);
        }

        // Right-aligned crumb, centred on the title's midline.
        using (Plugin.Instance?.OswaldSemi11?.Push())
        {
            float fH = ImGui.GetFontSize();
            float trk = Boutique.Track30(fH);
            string crumb = $"{entryCount} ENTR{(entryCount == 1 ? "Y" : "IES")}";
            float crumbW = Boutique.MeasureTrackedText(crumb, trk);
            DrawCrispTracked(dl,
                new Vector2(pos.X + contentW - crumbW, midY - fH * 0.5f),
                crumb, Boutique.U32(Boutique.TextFaint), trk);
        }

        // Underline: full BorderSoft hairline + 80px gold-fade overlay at the left.
        float ruleY = headBaseY + titleH + 8f * scale;
        dl.AddLine(
            new Vector2(pos.X, ruleY),
            new Vector2(pos.X + contentW, ruleY),
            Boutique.U32(Boutique.BorderSoft), 1f);
        float goldRuleW = 96f * scale;
        uint goldStart = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.70f));
        uint goldClear = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0f));
        dl.AddRectFilledMultiColor(
            new Vector2(pos.X, ruleY),
            new Vector2(pos.X + goldRuleW, ruleY + 1f),
            goldStart, goldClear, goldClear, goldStart);

        ImGui.Dummy(new Vector2(contentW, padTop + titleH + 18f * scale));
    }

    private void DrawGroupCard(float scale, float contentW, List<FeatureEntry> entries)
    {
        var dl = ImGui.GetWindowDrawList();
        var cardMin = ImGui.GetCursorScreenPos();
        float chamfer = 10f * scale;
        float rowGap = 0f;
        float rowVPad = 16f * scale;
        float tileSide = 34f * scale;

        // Pre-measure rows so we know the card height before drawing the card
        // body. Tile is 34, info column starts past it with comfortable gap.
        float infoX = cardMin.X + 18f * scale + tileSide + 16f * scale;
        float infoW = (cardMin.X + contentW) - infoX - 18f * scale;

        var rowMetrics = new (float h, float nameH, float descH, float whereH)[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            var f = entries[i];
            float nameH;
            using (Plugin.Instance?.OutfitMed13?.Push())
            {
                nameH = ImGui.CalcTextSize(f.Name).Y;
            }
            float descH;
            using (Plugin.Instance?.OutfitBody13?.Push())
            {
                descH = ImGui.CalcTextSize(f.Description, false, infoW).Y;
            }
            float whereH = 0f;
            if (!string.IsNullOrEmpty(f.Location))
            {
                // Stacked WHERE layout: kicker line + 2 px gap + path line.
                float labelFontH;
                using (Plugin.Instance?.OswaldSemi10?.Push()) { labelFontH = ImGui.GetFontSize(); }
                float pathFontH;
                using (Plugin.Instance?.OutfitBody13?.Push()) { pathFontH = ImGui.GetFontSize(); }
                whereH = labelFontH + 2f * scale + pathFontH;
            }
            float h = rowVPad * 2 + nameH + 6f * scale + descH;
            if (whereH > 0f) h += whereH + 10f * scale;
            // Minimum row height so the icon tile always fits with breathing room.
            float minH = rowVPad * 2 + tileSide;
            if (h < minH) h = minH;
            rowMetrics[i] = (h, nameH, descH, whereH);
        }
        float cardH = 0f;
        for (int i = 0; i < entries.Count; i++) cardH += rowMetrics[i].h + rowGap;

        var cardMax = new Vector2(cardMin.X + contentW, cardMin.Y + cardH);

        // Card body fill (slip-polygon) and border
        Boutique.FillSlip(dl, cardMin, cardMax, chamfer,
            Boutique.U32(new Vector4(14f / 255f, 16f / 255f, 20f / 255f, 0.55f)));
        Boutique.StrokeSlip(dl, cardMin, cardMax, chamfer,
            Boutique.U32(Boutique.BorderSoft), 1f);

        // Gold-deep top stripe (2px) from left edge to start of TR chamfer
        dl.AddRectFilled(
            new Vector2(cardMin.X, cardMin.Y),
            new Vector2(cardMax.X - chamfer, cardMin.Y + 2f * scale),
            Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.55f)));

        // Draw rows. The per-row InvisibleButton advances ImGui's cursor by
        // the row height, but the description's TextUnformatted advances
        // further inside each row. Snap the cursor back to the card's top
        // before reserving the card's full footprint as a single Dummy so
        // the next section head starts at exactly cardMax.Y + spacing,
        // independent of where the inner draw calls left ImGui's cursor.
        float y = cardMin.Y;
        for (int i = 0; i < entries.Count; i++)
        {
            var f = entries[i];
            var rm = rowMetrics[i];
            var rowMin = new Vector2(cardMin.X, y);
            var rowMax = new Vector2(cardMax.X, y + rm.h);
            DrawFeatureRow(dl, f, rowMin, rowMax, rm.nameH, rm.descH, rm.whereH, infoX, infoW, scale, i == 0);
            y += rm.h + rowGap;
        }

        ImGui.SetCursorScreenPos(cardMin);
        ImGui.Dummy(new Vector2(contentW, cardH + 4f * scale));
    }

    private void DrawFeatureRow(ImDrawListPtr dl, FeatureEntry f,
        Vector2 min, Vector2 max,
        float nameH, float descH, float whereH,
        float infoX, float infoW,
        float scale, bool isFirst)
    {
        // Hit region for hover
        ImGui.SetCursorScreenPos(min);
        ImGui.InvisibleButton($"##fwrow_{f.Name}", max - min);
        bool hovered = ImGui.IsItemHovered();

        // Top hairline (skip on first row in card)
        if (!isFirst)
        {
            dl.AddLine(
                new Vector2(min.X + 12f * scale, min.Y),
                new Vector2(max.X - 12f * scale, min.Y),
                Boutique.U32(new Vector4(1f, 1f, 1f, 0.025f)), 1f);
        }

        // Hover wash + gold-deep left edge bar
        if (hovered)
        {
            dl.AddRectFilled(min, max, Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.03f)));
            dl.AddRectFilled(
                new Vector2(min.X, min.Y + 8f * scale),
                new Vector2(min.X + 2f * scale, max.Y - 8f * scale),
                Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.85f)));
        }

        // Icon tile (34x34, slip-polygon, accent colour wash + accent glyph).
        // Tile sits aligned with the top of the name text rather than centred
        // on the row, so the visual mass reads against the title not the
        // bottom of the description.
        float padL = 18f * scale;
        float tileSide = 34f * scale;
        float tileChamfer = 5f * scale;
        var tileMin = new Vector2(min.X + padL, min.Y + 14f * scale);
        var tileMax = tileMin + new Vector2(tileSide, tileSide);
        Boutique.FillSlip(dl, tileMin, tileMax, tileChamfer,
            Boutique.U32(new Vector4(8f / 255f, 10f / 255f, 14f / 255f, 0.85f)));
        // Accent wash
        Boutique.FillSlip(dl, tileMin, tileMax, tileChamfer,
            Boutique.U32(Boutique.WithAlpha(f.IconColor, hovered ? 0.10f : 0.05f)));
        // Border tints to accent on hover
        Boutique.StrokeSlip(dl, tileMin, tileMax, tileChamfer,
            Boutique.U32(hovered ? Boutique.WithAlpha(f.IconColor, 0.55f) : Boutique.BorderSoft), 1f);

        // Glyph
        float glyphPx = 16f * scale;
        ImGui.PushFont(UiBuilder.IconFont);
        var gNat = ImGui.CalcTextSize(f.Icon.ToIconString());
        ImGui.PopFont();
        float gRatio = glyphPx / UiBuilder.IconFont.FontSize;
        var gPos = new Vector2(
            (tileMin.X + tileMax.X) * 0.5f - gNat.X * gRatio * 0.5f,
            (tileMin.Y + tileMax.Y) * 0.5f - gNat.Y * gRatio * 0.5f);
        dl.AddText(UiBuilder.IconFont, glyphPx, gPos, Boutique.U32(f.IconColor), f.Icon.ToIconString());

        // Info column. Top-aligned with the icon tile.
        float infoTop = min.Y + 16f * scale;
        var infoCursor = new Vector2(infoX, infoTop);

        // Name
        using (Plugin.Instance?.OutfitMed13?.Push())
        {
            float fH = ImGui.GetFontSize();
            // Render name; if there's a NEW pill, lay out name then pill on the same baseline.
            float nameW = ImGui.CalcTextSize(f.Name).X;
            dl.AddText(infoCursor, Boutique.U32(Boutique.Text), f.Name);

            if (f.IsNew)
            {
                float pillX = infoCursor.X + nameW + 8f * scale;
                using (Plugin.Instance?.OswaldSemi9?.Push())
                {
                    float pH = ImGui.GetFontSize();
                    float pTrk = Boutique.Track32(pH);
                    string nlbl = "NEW";
                    float pillTextW = Boutique.MeasureTrackedText(nlbl, pTrk);
                    float pillPadX = 6f * scale;
                    float pillPadY = 2f * scale;
                    var pillMin = new Vector2(pillX, infoCursor.Y + (fH - pH) * 0.5f - pillPadY);
                    var pillMax = new Vector2(pillX + pillTextW + pillPadX * 2f, pillMin.Y + pH + pillPadY * 2f);
                    dl.AddRectFilled(pillMin, pillMax, Boutique.U32(Boutique.Gold));
                    Boutique.DrawTrackedText(dl,
                        new Vector2(pillMin.X + pillPadX, pillMin.Y + pillPadY),
                        nlbl, Boutique.U32(new Vector4(0.10f, 0.08f, 0f, 1f)), pTrk);
                }
            }
        }

        // Description (wrapped). PushTextWrapPos expects a window-local X, so
        // convert the absolute infoCursor + infoW to local coordinates first.
        infoCursor.Y += nameH + 6f * scale;
        using (Plugin.Instance?.OutfitBody13?.Push())
        {
            var winPos = ImGui.GetWindowPos();
            ImGui.SetCursorScreenPos(infoCursor);
            ImGui.PushStyleColor(ImGuiCol.Text, Boutique.TextDim);
            ImGui.PushTextWrapPos((infoCursor.X + infoW) - winPos.X);
            ImGui.TextUnformatted(f.Description);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
        }

        // WHERE block. Two-line layout: tracked-caps kicker on its own thin
        // line, then path on the next line at infoX so the path aligns
        // vertically with the description text above it. The previous
        // single-line layout indented the path past the WHERE label, which
        // broke the column alignment with the description.
        if (!string.IsNullOrEmpty(f.Location))
        {
            float whereY = infoCursor.Y + descH + 10f * scale;

            float labelFontH;
            using (Plugin.Instance?.OswaldSemi10?.Push()) { labelFontH = ImGui.GetFontSize(); }
            using (Plugin.Instance?.OswaldSemi10?.Push())
            {
                float trk = Boutique.Track32(labelFontH);
                DrawCrispTracked(dl,
                    new Vector2(infoX, MathF.Round(whereY)),
                    "WHERE", Boutique.U32(Boutique.GoldDeep), trk);
            }

            // Path sits one line below the kicker, at the same X. Use the
            // path's own font height so the kicker-to-path gap stays tight.
            float pathTopY = whereY + labelFontH + 2f * scale;
            using (Plugin.Instance?.OutfitBody13?.Push())
            {
                float pathX = infoX;
                float yT = MathF.Round(pathTopY);
                var path = f.Location;

                // Split on " > " and render with gold-deep separators.
                var parts = path.Split(new[] { " > " }, StringSplitOptions.None);
                float xCursor = pathX;
                float remaining = (infoX + infoW) - pathX;
                for (int p = 0; p < parts.Length; p++)
                {
                    string seg = parts[p];
                    bool isFirst2 = p == 0;
                    Vector4 col;
                    if (isFirst2 && parts.Length > 1) col = Boutique.GoldWarm;
                    else col = Boutique.TextDim;

                    string trimmed = Boutique.TruncateToWidth(seg, MathF.Max(20f, remaining));
                    dl.AddText(new Vector2(xCursor, yT), Boutique.U32(col), trimmed);
                    float segW = ImGui.CalcTextSize(trimmed).X;
                    xCursor += segW;
                    remaining -= segW;

                    if (p < parts.Length - 1 && remaining > 30f * scale)
                    {
                        string sep = " › ";
                        dl.AddText(new Vector2(xCursor, yT), Boutique.U32(Boutique.GoldDeep), sep);
                        float sepW = ImGui.CalcTextSize(sep).X;
                        xCursor += sepW;
                        remaining -= sepW;
                    }
                }
            }
        }
    }

    private void DrawEmptyState(float scale, float contentW)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        float top = pos.Y + 80f * scale;
        float centreX = pos.X + contentW * 0.5f;

        // Diamond glyph
        float dSize = 6f * scale;
        var dCentre = new Vector2(centreX, top);
        var top1 = new Vector2(dCentre.X, dCentre.Y - dSize);
        var right = new Vector2(dCentre.X + dSize, dCentre.Y);
        var bot   = new Vector2(dCentre.X, dCentre.Y + dSize);
        var left  = new Vector2(dCentre.X - dSize, dCentre.Y);
        dl.AddTriangleFilled(top1, right, bot, Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.65f)));
        dl.AddTriangleFilled(top1, bot, left, Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.65f)));

        // "NO ENTRIES MATCH"
        using (Plugin.Instance?.OswaldSemi13?.Push())
        {
            float fH = ImGui.GetFontSize();
            float trk = Boutique.Track40(fH);
            string msg = "NO ENTRIES MATCH";
            float msgW = Boutique.MeasureTrackedText(msg, trk);
            Boutique.DrawTrackedText(dl,
                new Vector2(centreX - msgW * 0.5f, top + 22f * scale),
                msg, Boutique.U32(Boutique.GoldWarm), trk);
        }

        // Sub line
        using (Plugin.Instance?.OutfitBody13?.Push())
        {
            string sub = "Clear the search or pick a different category.";
            float subW = ImGui.CalcTextSize(sub).X;
            dl.AddText(new Vector2(centreX - subW * 0.5f, top + 50f * scale),
                Boutique.U32(Boutique.TextFaint), sub);
        }

        ImGui.Dummy(new Vector2(contentW, 200f * scale));
    }

    // ── Drawn diamond (substitute for the U+25C6 glyph, which is not in
    //    the Oswald atlas and otherwise renders as `?`). ─────────────────
    private static void DrawDiamond(ImDrawListPtr dl, Vector2 centre, float halfSize, uint colour)
    {
        var top   = new Vector2(centre.X, centre.Y - halfSize);
        var right = new Vector2(centre.X + halfSize, centre.Y);
        var bot   = new Vector2(centre.X, centre.Y + halfSize);
        var left  = new Vector2(centre.X - halfSize, centre.Y);
        dl.AddTriangleFilled(top, right, bot, colour);
        dl.AddTriangleFilled(top, bot, left, colour);
    }

    // ── Window brackets ───────────────────────────────────────────────
    private void DrawWindowBrackets(float scale)
    {
        var wPos = ImGui.GetWindowPos();
        var wSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        float bsize = 14f * scale;
        float binset = 5f * scale;
        uint bcol = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.40f));
        // Bottom-left
        var bl = new Vector2(wPos.X + binset, wPos.Y + wSize.Y - binset);
        dl.AddLine(new Vector2(bl.X, bl.Y - bsize), bl, bcol, 1f);
        dl.AddLine(bl, new Vector2(bl.X + bsize, bl.Y), bcol, 1f);
        // Bottom-right
        var br = new Vector2(wPos.X + wSize.X - binset, wPos.Y + wSize.Y - binset);
        dl.AddLine(new Vector2(br.X, br.Y - bsize), br, bcol, 1f);
        dl.AddLine(br, new Vector2(br.X - bsize, br.Y), bcol, 1f);
    }
}

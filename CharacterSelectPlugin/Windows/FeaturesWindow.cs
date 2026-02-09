using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace CharacterSelectPlugin.Windows;

public class FeaturesWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    private string searchQuery = "";
    private List<FeatureEntry> allFeatures = new();

    // Particle system
    private struct Particle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Life;
        public float MaxLife;
        public float Size;
        public Vector4 Color;
    }

    private List<Particle> particles = new();
    private float particleTimer = 0f;
    private Random particleRandom = new();

    private record FeatureEntry(
        string Name,
        string Description,
        string Location,
        string Category,
        FontAwesomeIcon Icon,
        Vector4 IconColor,
        string[] Keywords);

    public FeaturesWindow(Plugin plugin) : base(
        "CS+ Features Guide",
        ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;

        Size = new Vector2(650, 750);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500, 450),
            MaximumSize = new Vector2(950, 1100)
        };

        BuildFeatureList();
    }

    public void Dispose() { }

    private void BuildFeatureList()
    {
        // Colours for icons
        var cyan = new Vector4(0.3f, 0.85f, 1.0f, 1.0f);
        var green = new Vector4(0.4f, 0.9f, 0.5f, 1.0f);
        var orange = new Vector4(1.0f, 0.7f, 0.3f, 1.0f);
        var pink = new Vector4(1.0f, 0.5f, 0.7f, 1.0f);
        var purple = new Vector4(0.7f, 0.5f, 1.0f, 1.0f);
        var yellow = new Vector4(1.0f, 0.9f, 0.4f, 1.0f);
        var red = new Vector4(1.0f, 0.4f, 0.4f, 1.0f);
        var blue = new Vector4(0.4f, 0.6f, 1.0f, 1.0f);
        var slate = new Vector4(0.6f, 0.7f, 0.85f, 1.0f); // For chat commands

        allFeatures = new List<FeatureEntry>
        {
            // === Quick Actions ===
            new("Quick Switch Overlay",
                "A floating window for rapid character switching. Keep it open while you play!",
                "/selectswitch",
                "Quick Actions",
                FontAwesomeIcon.Bolt, yellow,
                new[] { "quick", "fast", "switch", "overlay" }),

            new("Compact Quick Switch",
                "A minimal version of Quick Switch - just a row of character icons. Toggle compact mode from the settings or right-click the Quick Switch window.",
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

            // === Your Identity ===
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
                "Quest dialogue says 'Hello, [YourCharacter]!' instead of your FFXIV name. Full immersion!",
                "Settings > Immersive Dialogue",
                "Your Identity",
                FontAwesomeIcon.Comment, green,
                new[] { "dialogue", "npc", "name", "immersion" }),

            new("NPCs Use Your Pronouns",
                "She/her, he/him, they/them - NPCs will use whatever pronouns you set in your RP Profile.",
                "Settings > Immersive Dialogue",
                "Your Identity",
                FontAwesomeIcon.Comments, green,
                new[] { "pronoun", "they", "she", "he", "gender" }),

            new("Honorific Titles",
                "Set a title that appears above your character's name using the Honorific plugin. Customize the colour and glow - supporters of Honorific can use animated gradients.",
                "Character Form > Honorific",
                "Your Identity",
                FontAwesomeIcon.Star, yellow,
                new[] { "honorific", "title", "glow", "name" }),

            // === Automation ===
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
                "Trigger Glamourer Automations when applying characters or designs. Create a 'None' automation in Glamourer for characters that shouldn't run any automation - this prevents one character's automation from carrying over to another.",
                "Settings > Glamourer Automations",
                "Automation",
                FontAwesomeIcon.Magic, purple,
                new[] { "glamourer", "automation", "none", "trigger" }),

            new("Advanced Mode & Macros",
                "Enable Advanced Mode on a character or design to run custom macro commands on apply. Use this for anything CS+ doesn't directly support - trigger other plugins, run game commands, or execute complex sequences.",
                "Character Form / Design Panel",
                "Automation",
                FontAwesomeIcon.Code, purple,
                new[] { "advanced", "macro", "command", "script" }),

            // === Organization ===
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
                "Tag characters, mark favorites, and filter to find exactly what you need.",
                "Character Form / Main Window",
                "Organization",
                FontAwesomeIcon.Tags, blue,
                new[] { "tag", "favorite", "filter" }),

            // === Apply to Target ===
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

            // === RP Profiles ===
            new("Share Your Profile",
                "Private, Direct Share, or Public - you choose who sees your RP profile.",
                "RP Profile Edit",
                "RP Profiles",
                FontAwesomeIcon.Share, pink,
                new[] { "share", "profile", "privacy" }),

            new("View Others' Profiles",
                "Right-click players in party/chat/friends to peek at their character's story.",
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
                "Browse public profiles, like your favorites, follow interesting players.",
                "/gallery",
                "RP Profiles",
                FontAwesomeIcon.Images, pink,
                new[] { "gallery", "browse", "discover" }),

            // === Mod Management ===
            new("What is Conflict Resolution?",
                "Tired of constantly toggling mods on and off in Penumbra? CR lets you save which mods should be enabled or disabled for each character, and applies them automatically when you switch.",
                "Settings > Conflict Resolution",
                "Mod Management",
                FontAwesomeIcon.QuestionCircle, orange,
                new[] { "conflict", "resolution", "what", "mods" }),

            new("Per-Character Mods",
                "Set up mod states once per character. When you switch characters, CS+ handles enabling and disabling mods for you - no more manual Penumbra toggling.",
                "/select mods",
                "Mod Management",
                FontAwesomeIcon.User, orange,
                new[] { "character", "mods", "enable", "disable" }),

            new("Per-Design Mods",
                "Different mods for different outfits on the same character. Wet skin for your swimsuit design, dry skin for your cozy PJs.",
                "Design Panel > CR section",
                "Mod Management",
                FontAwesomeIcon.Tshirt, orange,
                new[] { "design", "outfit", "mods" }),

            new("Pinned Mods",
                "Got accessories you always wear? Pin mods like your favorite earrings or necklace so they stay enabled no matter which design you switch to.",
                "Mod Manager",
                "Mod Management",
                FontAwesomeIcon.Thumbtack, orange,
                new[] { "pin", "always", "never" }),

            // === Capturing Looks ===
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

            // === Customization ===
            new("Custom Themes",
                "Change every colour, add background images, pick a custom favorite icon.",
                "Settings > Visual > Custom",
                "Customize CS+",
                FontAwesomeIcon.Palette, purple,
                new[] { "theme", "colour", "customize" }),

            new("Seasonal Themes",
                "Halloween, Winter, Christmas - with special visual effects!",
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

            // === Safety ===
            new("Auto Backups",
                "CS+ backs up your config on updates and weekly. Your characters are safe!",
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

            // === Chat Commands (at bottom) ===
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

    public override void Draw()
    {
        var scale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;

        // Banner area
        DrawBanner();

        ImGui.Spacing();

        // Search bar
        ImGui.SetNextItemWidth(-1);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 8));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8);
        ImGui.InputTextWithHint("##FeatureSearch", "  Search... (try 'name', 'random', 'mods', 'backup')", ref searchQuery, 100);
        ImGui.PopStyleVar(2);

        ImGui.Spacing();
        ImGui.Spacing();

        // Content area
        ImGui.BeginChild("FeaturesScrollArea", Vector2.Zero, false);

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            DrawSearchResults();
        }
        else
        {
            DrawAllFeatures();
        }

        ImGui.EndChild();
    }

    private void DrawBanner()
    {
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var windowWidth = ImGui.GetWindowWidth();
        var bannerHeight = 80f * ImGuiHelpers.GlobalScale;

        // Try to load banner image
        bool imageDrawn = false;
        try
        {
            var pluginDirectory = Plugin.PluginInterface.AssemblyLocation.Directory?.FullName;
            if (pluginDirectory != null)
            {
                string imagePath = Path.Combine(pluginDirectory, "Assets", "Feature Banner.png");
                if (File.Exists(imagePath))
                {
                    var texture = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
                    if (texture != null)
                    {
                        var contentWidth = windowWidth - 16;

                        // Fill the banner area width, crop height if needed
                        float imageAspect = (float)texture.Width / texture.Height;
                        float drawWidth = contentWidth;
                        float drawHeight = drawWidth / imageAspect;

                        // Center vertically if image is taller than banner
                        float offsetY = 0;
                        if (drawHeight > bannerHeight)
                        {
                            offsetY = (bannerHeight - drawHeight) * 0.5f;
                        }

                        // Clip to banner region
                        drawList.PushClipRect(cursorPos, new Vector2(cursorPos.X + contentWidth, cursorPos.Y + bannerHeight), true);

                        // Darken overlay for text readability
                        drawList.AddRectFilled(
                            cursorPos,
                            new Vector2(cursorPos.X + contentWidth, cursorPos.Y + bannerHeight),
                            ImGui.ColorConvertFloat4ToU32(new Vector4(0.05f, 0.05f, 0.08f, 1.0f)),
                            8);

                        // Draw image filling width
                        drawList.AddImage(
                            (ImTextureID)texture.Handle,
                            new Vector2(cursorPos.X, cursorPos.Y + offsetY),
                            new Vector2(cursorPos.X + drawWidth, cursorPos.Y + offsetY + drawHeight),
                            Vector2.Zero,
                            Vector2.One,
                            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.7f)));

                        drawList.PopClipRect();
                        imageDrawn = true;
                    }
                }
            }
        }
        catch { }

        // Fallback background if no image
        if (!imageDrawn)
        {
            drawList.AddRectFilled(
                cursorPos,
                new Vector2(cursorPos.X + windowWidth - 16, cursorPos.Y + bannerHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.12f, 0.12f, 0.18f, 0.95f)),
                8);
        }

        // Title text - centered
        var titleText = "Discover CS+ Features";
        ImGui.PushFont(UiBuilder.DefaultFont);
        ImGui.SetWindowFontScale(1.6f);
        var titleSize = ImGui.CalcTextSize(titleText);
        var titleX = cursorPos.X + (windowWidth - 16 - titleSize.X) * 0.5f;
        var titleY = cursorPos.Y + (bannerHeight - titleSize.Y) * 0.35f;

        // Shadow
        drawList.AddText(new Vector2(titleX + 2, titleY + 2), ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.5f)), titleText);
        // Main text - white
        drawList.AddText(new Vector2(titleX, titleY), ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f)), titleText);
        ImGui.SetWindowFontScale(1.0f);
        ImGui.PopFont();

        // Subtitle
        var subtitleText = "Tips, tricks, and hidden gems";
        var subtitleSize = ImGui.CalcTextSize(subtitleText);
        var subtitleX = cursorPos.X + (windowWidth - 16 - subtitleSize.X) * 0.5f;
        var subtitleY = titleY + titleSize.Y + 4;
        drawList.AddText(new Vector2(subtitleX, subtitleY), ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.75f, 1f)), subtitleText);

        // Draw particles on top
        DrawParticleEffects(drawList, cursorPos, new Vector2(windowWidth - 16, bannerHeight));

        // Move cursor past banner
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + bannerHeight + 8);
    }

    private void DrawParticleEffects(ImDrawListPtr drawList, Vector2 bannerStart, Vector2 bannerSize)
    {
        float deltaTime = 1f / 60f;
        particleTimer += deltaTime;

        if (particleTimer > 0.12f && particles.Count < 35)
        {
            SpawnParticle(bannerStart, bannerSize);
            particleTimer = 0f;
        }

        for (int i = particles.Count - 1; i >= 0; i--)
        {
            var particle = particles[i];

            particle.Position += particle.Velocity * deltaTime;
            particle.Life -= deltaTime;

            if (particle.Life <= 0 ||
                particle.Position.X > bannerStart.X + bannerSize.X + 50 ||
                particle.Position.Y < bannerStart.Y - 50 ||
                particle.Position.Y > bannerStart.Y + bannerSize.Y + 50)
            {
                particles.RemoveAt(i);
                continue;
            }

            float alpha = Math.Min(1f, particle.Life / particle.MaxLife);
            var color = new Vector4(particle.Color.X, particle.Color.Y, particle.Color.Z, particle.Color.W * alpha);

            drawList.AddCircleFilled(
                particle.Position,
                particle.Size,
                ImGui.ColorConvertFloat4ToU32(color));

            // Glow effect for brighter particles
            if (particle.Color.W > 0.5f)
            {
                drawList.AddCircleFilled(
                    particle.Position,
                    particle.Size * 2.5f,
                    ImGui.ColorConvertFloat4ToU32(color with { W = color.W * 0.2f }));
            }

            particles[i] = particle;
        }
    }

    private void SpawnParticle(Vector2 bannerStart, Vector2 bannerSize)
    {
        var particle = new Particle
        {
            Position = new Vector2(
                bannerStart.X + (float)particleRandom.NextDouble() * bannerSize.X,
                bannerStart.Y + (float)particleRandom.NextDouble() * bannerSize.Y
            ),

            Velocity = new Vector2(
                -8f + (float)particleRandom.NextDouble() * 16f,
                -12f + (float)particleRandom.NextDouble() * -8f
            ),

            MaxLife = 5f + (float)particleRandom.NextDouble() * 3f,
            Size = 1.5f + (float)particleRandom.NextDouble() * 2f,

            Color = particleRandom.Next(5) switch
            {
                0 => new Vector4(1.0f, 1.0f, 1.0f, 0.8f),   // White
                1 => new Vector4(0.95f, 0.95f, 1.0f, 0.7f), // Soft white
                2 => new Vector4(1.0f, 0.5f, 0.7f, 0.7f),   // Pink
                3 => new Vector4(0.5f, 0.7f, 1.0f, 0.7f),   // Blue
                _ => new Vector4(0.8f, 0.9f, 1.0f, 0.6f)    // Light blue
            }
        };

        particle.Life = particle.MaxLife;
        particles.Add(particle);
    }

    private void DrawSearchResults()
    {
        var query = searchQuery.ToLowerInvariant().Trim();
        var results = allFeatures.Where(f =>
            f.Name.ToLowerInvariant().Contains(query) ||
            f.Description.ToLowerInvariant().Contains(query) ||
            f.Keywords.Any(k => k.Contains(query))
        ).ToList();

        if (results.Count == 0)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
            ImGui.SetCursorPosX(20);
            ImGui.TextWrapped($"No features found for \"{searchQuery}\"");
            ImGui.Spacing();
            ImGui.SetCursorPosX(20);
            ImGui.Text("Try: name, random, mods, backup, theme, profile");
            ImGui.PopStyleColor();
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
        ImGui.SetCursorPosX(10);
        ImGui.Text($"{results.Count} result{(results.Count == 1 ? "" : "s")}");
        ImGui.PopStyleColor();
        ImGui.Spacing();

        foreach (var feature in results)
        {
            DrawFeatureCard(feature);
        }
    }

    private void DrawAllFeatures()
    {
        var categories = allFeatures.GroupBy(f => f.Category).ToList();

        foreach (var category in categories)
        {
            DrawCategoryHeader(category.Key);

            foreach (var feature in category)
            {
                DrawFeatureCard(feature);
            }

            ImGui.Spacing();
            ImGui.Spacing();
        }

        // Footer
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
        ImGui.SetCursorPosX(20);
        ImGui.TextWrapped("Tip: Use /select <name> to quickly switch characters, or /select random for a surprise!");
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private void DrawCategoryHeader(string title)
    {
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var availWidth = ImGui.GetContentRegionAvail().X;

        // Category colour based on name
        var colour = title switch
        {
            "Quick Actions" => new Vector4(1.0f, 0.8f, 0.3f, 1.0f),
            "Your Identity" => new Vector4(0.3f, 0.9f, 1.0f, 1.0f),
            "Automation" => new Vector4(0.7f, 0.5f, 1.0f, 1.0f),
            "Organization" => new Vector4(0.4f, 0.7f, 1.0f, 1.0f),
            "Apply to Target" => new Vector4(0.3f, 0.85f, 0.7f, 1.0f),
            "RP Profiles" => new Vector4(1.0f, 0.5f, 0.7f, 1.0f),
            "Mod Management" => new Vector4(1.0f, 0.6f, 0.3f, 1.0f),
            "Capturing Looks" => new Vector4(0.3f, 0.9f, 0.9f, 1.0f),
            "Chat Commands" => new Vector4(0.6f, 0.7f, 0.85f, 1.0f),
            "Customize CS+" => new Vector4(0.8f, 0.5f, 1.0f, 1.0f),
            "Backup & Safety" => new Vector4(0.5f, 0.9f, 0.6f, 1.0f),
            _ => new Vector4(0.7f, 0.7f, 0.7f, 1.0f)
        };

        // Left accent bar
        drawList.AddRectFilled(
            cursorPos,
            new Vector2(cursorPos.X + 4, cursorPos.Y + 22),
            ImGui.ColorConvertFloat4ToU32(colour),
            2);

        // Title
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 14);
        ImGui.PushStyleColor(ImGuiCol.Text, colour);
        ImGui.Text(title.ToUpperInvariant());
        ImGui.PopStyleColor();

        // Horizontal line
        ImGui.SameLine();
        var lineStart = ImGui.GetCursorScreenPos();
        lineStart.X += 10;
        lineStart.Y += 8;
        drawList.AddLine(
            lineStart,
            new Vector2(cursorPos.X + availWidth - 10, lineStart.Y),
            ImGui.ColorConvertFloat4ToU32(colour * 0.3f),
            1);

        ImGui.Spacing();
        ImGui.Spacing();
    }

    private void DrawFeatureCard(FeatureEntry feature)
    {
        var drawList = ImGui.GetWindowDrawList();
        var availWidth = ImGui.GetContentRegionAvail().X;
        var scale = ImGuiHelpers.GlobalScale;

        ImGui.PushID(feature.Name);

        ImGui.BeginGroup();

        // Icon
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 10);
        ImGui.PushStyleColor(ImGuiCol.Text, feature.IconColor);
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.Text(feature.Icon.ToIconString());
        ImGui.PopFont();
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8);

        // Text content
        ImGui.BeginGroup();

        // Feature name
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.95f, 0.95f, 1.0f));
        ImGui.Text(feature.Name);
        ImGui.PopStyleColor();

        // Description
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.65f, 0.65f, 0.65f, 1.0f));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + availWidth - 80);
        ImGui.TextWrapped(feature.Description);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();

        // Location
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.45f, 0.5f, 0.55f, 1.0f));
        ImGui.Text(feature.Location);
        ImGui.PopStyleColor();

        ImGui.EndGroup();
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.PopID();
    }
}

using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;
using System;
using System.IO;
using System.Numerics;
using System.Collections.Generic;

namespace CharacterSelectPlugin.Windows
{
    public class PatchNotesWindow : Window
    {
        private readonly Plugin plugin;
        private bool hasScrolledToEnd = false;
        private bool wasOpen = false; // Track if window was open last frame
        public bool OpenMainMenuOnClose = false;

        // Particle system for banner effects
        private struct Particle
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float Life;
            public float MaxLife;
            public float Size;
            public Vector4 Color;
        }

        private List<Particle> particles = new List<Particle>();
        private float particleTimer = 0f;
        private Random particleRandom = new Random();

        public PatchNotesWindow(Plugin plugin) : base("Character Select+ – What's New?",
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar)
        {
            this.plugin = plugin;
            IsOpen = false;

            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(800, 650),
                MaximumSize = new Vector2(800, 650)
            };
        }

        public override void Draw()
        {
            var totalScale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;
            
            // Reset scroll tracking if window was just opened
            if (IsOpen && !wasOpen)
            {
                hasScrolledToEnd = false;
            }
            wasOpen = IsOpen;

            ImGui.SetNextWindowSize(new Vector2(800 * totalScale, 650 * totalScale), ImGuiCond.Always);

            // UI Stylin'
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.06f, 0.06f, 0.06f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.92f, 0.92f, 0.92f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.15f, 0.15f, 0.18f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.2f, 0.2f, 0.25f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.25f, 0.25f, 0.3f, 1.0f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f * totalScale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10.0f * totalScale);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8 * totalScale, 8 * totalScale));

            try
            {
                DrawModernHeader(totalScale);
                DrawPatchNotes(totalScale);
                DrawBottomButton(totalScale);
            }
            finally
            {
                ImGui.PopStyleVar(3);
                ImGui.PopStyleColor(6);
            }
        }

        private void DrawModernHeader(float totalScale)
        {
            // Window position
            var windowPos = ImGui.GetWindowPos();
            var windowPadding = ImGui.GetStyle().WindowPadding;

            // Header area dimensions - let it start higher
            var headerWidth = (800f * totalScale) - (windowPadding.X * 2);
            var headerHeight = 180f * totalScale;

            // Start the header
            var headerStart = windowPos + new Vector2(windowPadding.X, windowPadding.Y * 1.1f);
            var headerEnd = headerStart + new Vector2(headerWidth, headerHeight);

            var drawList = ImGui.GetWindowDrawList();

            try
            {
                string pluginDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "";
                string assetsPath = Path.Combine(pluginDirectory, "Assets");
                string imagePath = Path.Combine(assetsPath, "winterbanner.png");

                if (File.Exists(imagePath))
                {
                    var texture = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
                    if (texture != null)
                    {
                        // Calculate scaling to fill width and maintain aspect ratio
                        var imageAspect = (float)texture.Width / texture.Height;
                        var scaledWidth = headerWidth;
                        var scaledHeight = scaledWidth / imageAspect;

                        // Draw the banner image starting from the header position
                        var imagePos = headerStart;
                        drawList.AddImage((ImTextureID)texture.Handle, imagePos, imagePos + new Vector2(scaledWidth, scaledHeight));

                        // Draw particles
                        DrawParticleEffects(drawList, headerStart, new Vector2(scaledWidth, scaledHeight));
                    }
                    else
                    {
                        DrawGradientBackground(headerStart, headerEnd);
                        // Add particle effects over the gradient too
                        DrawParticleEffects(drawList, headerStart, new Vector2(headerWidth, headerHeight));
                    }
                }
                else
                {
                    DrawGradientBackground(headerStart, headerEnd);
                    // Add particle effects over the gradient
                    DrawParticleEffects(drawList, headerStart, new Vector2(headerWidth, headerHeight));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to load banner image: {ex.Message}");
                DrawGradientBackground(headerStart, headerEnd);
                // Add particle effects over the gradient
                DrawParticleEffects(drawList, headerStart, new Vector2(headerWidth, headerHeight));
            }

            ImGui.SetCursorPosY((windowPadding.Y * 0.5f) + headerHeight - 10);

            // Winter/Christmas Special Announcement Box - positioned in header above "New in v..." text
            DrawWinterAnnouncementBox(totalScale);


            // Version badge
            ImGui.SetCursorPosX(9);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1.0f), "\uf005"); // Green star
            ImGui.PopFont();
            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.9f, 0.4f, 1.0f)); // Green text
            ImGui.Text($"New in v{Plugin.CurrentPluginVersion}");
            ImGui.PopStyleColor();

            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 10);
            ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.85f, 1.0f), "7.4 Compatibility, Mod Manager, Character Assignments");

            ImGui.Separator();
            ImGui.Spacing();
        }

        private void DrawGradientBackground(Vector2 headerStart, Vector2 headerEnd)
        {
            // Gradient background
            var drawList = ImGui.GetWindowDrawList();
            uint gradientTop = ImGui.GetColorU32(new Vector4(0.2f, 0.4f, 0.8f, 0.15f));
            uint gradientBottom = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.2f, 0.05f));
            drawList.AddRectFilledMultiColor(headerStart, headerEnd, gradientTop, gradientTop, gradientBottom, gradientBottom);

            // Add fallback text for gradient version
            ImGui.SetCursorPos(new Vector2(20, 15));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.95f, 0.95f, 1.0f));
            ImGui.Text("Character Select+ – What's New?");
            ImGui.PopStyleColor();

            ImGui.SetCursorPos(new Vector2(20, 35));
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1.0f), "\uf005");
            ImGui.PopFont();
            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.9f, 0.4f, 1.0f));
            ImGui.Text($"New in v{Plugin.CurrentPluginVersion}");
            ImGui.PopStyleColor();

            ImGui.SetCursorPos(new Vector2(20, 55));
            ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.85f, 1.0f), "7.4 Compatibility, Mod Manager, Character Assignments");
        }

        private void DrawPatchNotes(float totalScale)
        {
            // Scrollable content area
            ImGui.BeginChild("PatchNotesScroll", new Vector2(0, -70 * totalScale), false, ImGuiWindowFlags.AlwaysVerticalScrollbar);

            // Track scroll position for enabling button
            float currentScrollY = ImGui.GetScrollY();
            float maxScrollY = ImGui.GetScrollMaxY();

            // Check if user has scrolled at least 85% through the content
            if (maxScrollY > 0 && currentScrollY >= (maxScrollY * 0.85f))
            {
                hasScrolledToEnd = true;
            }

            ImGui.PushTextWrapPos();

            // Latest Patch Notes - v2.0.1.4
            if (DrawModernCollapsingHeader("v2.0.1.4 – 7.4 Compatibility, Mod Manager, Character Assignments", new Vector4(0.4f, 0.9f, 0.4f, 1.0f), true))
            {
                Draw214Notes();

                // Show scroll indicator if haven't scrolled enough
                if (!hasScrolledToEnd)
                {
                    ImGui.Spacing();
                    ImGui.Spacing();
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.8f, 0.8f));
                    ImGui.Text("↓ Scroll down to see all features before continuing ↓");
                    ImGui.PopStyleColor();
                    ImGui.Spacing();
                }
            }

            // Previous Patch Notes - v2.0.1.0  
            if (DrawModernCollapsingHeader("v2.0.1.0 – Conflict Resolution, IPC, Apply to Target (GPose)", new Vector4(0.75f, 0.75f, 0.85f, 1.0f), false))
            {
                Draw201Notes();
            }

            // Previous Patch Notes - v2.0.0.0
            if (DrawModernCollapsingHeader("v2.0.0.0 – Character Gallery & Visual Overhaul", new Vector4(0.75f, 0.75f, 0.85f, 1.0f), false))
            {
                Draw120Notes();
            }

            // Previous Patch Notes - v1.1
            if (DrawModernCollapsingHeader("v1.1.0.8 - v1.1.1.2 – April 18 2025", new Vector4(0.75f, 0.75f, 0.85f, 1.0f), false))
            {
                Draw110Notes();
            }

            // Previous Patch Notes - v1.1.0.0-7
            if (DrawModernCollapsingHeader("v1.1.0.(0-7) – April 09 2025", new Vector4(0.75f, 0.75f, 0.85f, 1.0f), false))
            {
                Draw1100Notes();
            }

            ImGui.PopTextWrapPos();
            ImGui.EndChild();
        }

        private bool DrawModernCollapsingHeader(string title, Vector4 titleColor, bool defaultOpen)
        {
            var flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;

            ImGui.PushStyleColor(ImGuiCol.Text, titleColor);
            bool isOpen = ImGui.CollapsingHeader(title, flags);
            ImGui.PopStyleColor();

            return isOpen;
        }

        private void DrawFeatureSection(string icon, string title, Vector4 accentColor)
        {
            var drawList = ImGui.GetWindowDrawList();
            var startPos = ImGui.GetCursorScreenPos();

            // Feature section background
            var bgMin = startPos + new Vector2(-10, -5);
            var bgMax = startPos + new Vector2(ImGui.GetContentRegionAvail().X + 10, 25);
            drawList.AddRectFilled(bgMin, bgMax, ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.15f, 0.6f)), 4f);

            drawList.AddRectFilled(bgMin, bgMin + new Vector2(3, bgMax.Y - bgMin.Y), ImGui.GetColorU32(accentColor), 2f);

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 1);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text(icon);
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.TextColored(accentColor, title);
            ImGui.Spacing();
        }

        private void Draw214Notes()
        {
            // 7.4 Compatibility Update
            DrawFeatureSection("\uf021", "7.4 Compatibility Update", new Vector4(0.6f, 0.8f, 1.0f, 1.0f));
            ImGui.BulletText("Updated for Final Fantasy XIV patch 7.4");
            ImGui.Spacing();

            // Design Panel Enhancements
            DrawFeatureSection("\uf1fc", "Design Panel Enhancements", new Vector4(0.9f, 0.7f, 0.9f, 1.0f));
            ImGui.BulletText("Design Previews now show in Quick Character Switch for easier design selection");
            ImGui.BulletText("Active design is now highlighted in green in the design list");
            ImGui.BulletText("Added save button to Design's Advanced Mode window for easier workflow");
            ImGui.BulletText("Update CR for Existing Designs feature for hair/gear changes (other changes still need manual editing)");
            ImGui.Spacing();

            // Mod Manager Improvements
            DrawFeatureSection("\uf085", "Mod Manager Improvements", new Vector4(0.9f, 0.7f, 0.2f, 1.0f));
            ImGui.BulletText("Standalone Mod Manager window for better organization (use '/select mods' command)");
            ImGui.BulletText("Global Search functionality to search across all mod categories simultaneously");
            ImGui.BulletText("'Currently Affecting You' section now shows: Tattoos, Eyes, Ears/Tail/Horns, Makeup/Face Paint");
            ImGui.Spacing();

            // Auto-Apply Last Used Design on Login
            DrawFeatureSection("\uf4fc", "Auto-Apply Last Used Design on Login", new Vector4(0.6f, 0.9f, 0.8f, 1.0f));
            ImGui.BulletText("New setting that works with 'Auto-Apply Last Used Character on Login'");
            ImGui.BulletText("When enabled, also automatically applies the last design you used for that character");
            ImGui.BulletText("Perfect for maintaining your complete look when logging back in");
            ImGui.BulletText("Appears as a sub-option when character auto-apply is enabled");
            ImGui.Spacing();

            // Winter/Christmas Theme
            DrawFeatureSection("\uf2dc", "Winter/Christmas Theme & Holiday Update", new Vector4(0.9f, 0.95f, 1.0f, 1.0f));
            ImGui.BulletText("Winter and Christmas themes added");
            ImGui.BulletText("Users can now freely choose which theme from the available list");
            ImGui.Spacing();

            // Bug Fixes
            DrawFeatureSection("\uf188", "Bug Fixes", new Vector4(0.9f, 0.4f, 0.4f, 1.0f));
            ImGui.BulletText("Fixed Character Assignments not working properly (hopefully)");
            ImGui.Spacing();
        }

        private void Draw201Notes()
        {
            // Conflict Resolution System
            DrawFeatureSection("\uf071", "Mod Conflict Resolution System", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            ImGui.BulletText("Eliminates mod conflicts between Character designs automatically when switching between them");
            ImGui.BulletText("Save complete mod configurations per design including enabled mods, mod settings, and option selections");
            ImGui.BulletText("Intelligent Mod Manager with 21+ categories (Hair, Gear, Bodies, VFX, Animations, etc.) for easy organization");
            ImGui.BulletText("Automatically categorizes and tracks mod additions, deletions, and changes -- no manual upkeep required");
            ImGui.BulletText("Optional opt-in feature available in CS+ settings when you're ready to explore advanced mod management");
            ImGui.Spacing();

            // Enhanced IPC API
            DrawFeatureSection("\uf0c1", "API / IPC", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("API endpoints for other plugins to integrate with CS+");
            ImGui.BulletText("Character switching, design management, and event notifications");
            ImGui.BulletText("Used internally for Conflict Resolution, improved Apply to Target functionality, and the Snapshot feature");
            ImGui.BulletText("Real-time character change events for plugin synchronization");
            ImGui.Spacing();

            // Apply to Target - GPose Support
            DrawFeatureSection("\uf140", "Apply to Target - GPose Support", new Vector4(0.6f, 1.0f, 0.8f, 1.0f));
            ImGui.BulletText("Fixed Apply to Target functionality to work properly in GPose");
            ImGui.BulletText("Converted from previous macro-based to new IPC-based system");
            ImGui.BulletText("More reliable character application to targeted players");
            ImGui.Spacing();

            // Snapshot
            DrawFeatureSection("\uf030", "Snapshot Feature", new Vector4(0.9f, 0.7f, 1.0f, 1.0f));
            ImGui.BulletText("New Snapshot feature - one-click add Design to Character Select+");
            ImGui.BulletText("Use after saving a Design in Glamourer and setting up your Customize+ Profile");
            ImGui.BulletText("Instantly adds your current look as a Design to the active Character in CS+");
            ImGui.BulletText("Includes your current Customize+ Profile automatically");
            ImGui.BulletText("CR mode: Auto-configures mods for your current outfit when using Conflict Resolution");
            ImGui.BulletText("Simple workflow: Click camera button in Design Panel or use chat command");
            ImGui.BulletText("Chat command: /select save - optionally add CR for Conflict Resolution mode");
            ImGui.Spacing();

            // UI Scaling
            DrawFeatureSection("\uf00e", "UI Scaling Done Right", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Character Select+ is now properly responsive to the user's resolution and Dalamud's Global Font Scaling.");
            ImGui.BulletText("Removed UI scaling options in Settings Panel.");
            ImGui.BulletText("Let me know if there are any issues using this.");
            ImGui.Spacing();

            // Penumbra Collection UI Sync
            DrawFeatureSection("\uf021", "Penumbra Collection Synchronization", new Vector4(0.8f, 0.9f, 0.6f, 1.0f));
            ImGui.BulletText("Switching characters now updates Penumbra's UI to show the correct collection");
            ImGui.BulletText("Seamless integration between CS+ character switching and Penumbra interface");
            ImGui.BulletText("Eliminates confusion about which collection is currently active");
            ImGui.Spacing();

            // Character Management Improvements
            DrawFeatureSection("\uf007", "Character Management Improvements", new Vector4(0.9f, 0.8f, 0.6f, 1.0f));
            ImGui.BulletText("Fixed Character Assignments -- can now edit and remove character assignments");
            ImGui.BulletText("Fixed Reorder Characters window -- changes now properly apply on save");
            ImGui.BulletText("Added duplicate character name prevention for your own characters");
            ImGui.Spacing();

            // Backup & Restore System
            DrawFeatureSection("\uf0c7", "Backup & Restore System", new Vector4(0.4f, 0.8f, 1.0f, 1.0f));
            ImGui.BulletText("Manual backup creation with optional custom naming");
            ImGui.BulletText("Configuration file import -- appears at top of backup list");
            ImGui.BulletText("Available Backups list with real-time backup count display");
            ImGui.BulletText("Individual restore functionality for any backup file");
            ImGui.BulletText("Automatic emergency backup creation before any restore operation");
            ImGui.Spacing();

            // Design Panel Enhancements
            DrawFeatureSection("\uf002", "Design Panel Enhancements", new Vector4(0.7f, 0.6f, 1.0f, 1.0f));
            ImGui.BulletText("Added search functionality to quickly find specific designs");
            ImGui.BulletText("New clipboard image pasting for Design Preview images");
            ImGui.Spacing();
        }

        private void Draw120Notes()
        {
            // Character Gallery (NEW!)
            DrawFeatureSection("\uf302", "Character Gallery", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            ImGui.BulletText("View and share your CS+ Characters with everyone else!");
            ImGui.BulletText("Opt-in feature - choose your main physical character to represent you");
            ImGui.BulletText("Shows recent activity status with green globe indicators");
            ImGui.BulletText("Like,favourite,add or even block other players' characters");
            ImGui.BulletText("Click any profile to view their full RP Profile with backgrounds & effects");
            ImGui.Spacing();

            // NSFW Content Management (NEW!)
            DrawFeatureSection("\uf06e", "NSFW Content Management", new Vector4(1.0f, 0.7f, 0.4f, 1.0f));
            ImGui.BulletText("RP Profile Editor now prompts you to mark profiles as NSFW if appropriate");
            ImGui.BulletText("Gallery setting to opt-in to viewing NSFW profiles (disabled by default)");
            ImGui.BulletText("Users must acknowledge they are 18+ to view NSFW content in the gallery");
            ImGui.Spacing();

            // Revamped RP Profiles
            DrawFeatureSection("\uf2c2", "Revamped RP Profiles", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Complete visual redesign with new layout and styling");
            ImGui.BulletText("80+ FFXIV location backgrounds to choose from");
            ImGui.BulletText("Animated visual effects: butterflies, fireflies, falling leaves, and more");
            ImGui.BulletText("Real-time preview - see changes instantly in the editor");
            ImGui.BulletText("Right-click any player name to view their RP Profile directly");
            ImGui.Spacing();

            // Immersive Dialogue (NEW!)
            DrawFeatureSection("\uf075", "Immersive Dialogue System", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            ImGui.BulletText("NPCs now use your CS+ Character's name, pronouns, and desired titles in dialogue!");
            ImGui.BulletText("Integration with he/him, she/her, and they/them pronouns");
            ImGui.BulletText("Granular settings: enable names, pronouns, gendered terms, or race separately");
            ImGui.BulletText("Customizable they/them neutral titles: friend, Mx., traveler, adventurer, or choose your own!");
            ImGui.BulletText("Only affects dialogue referring to your character - NPCs keep their own pronouns");
            ImGui.BulletText("Requires an active CS+ character with RP Profile pronouns set");
            ImGui.BulletText("If you find any instances in which it doesn't seem to be working please report them in the discord!");
            ImGui.Spacing();

            // Main Window UI Update
            DrawFeatureSection("\uf53f", "Main Window Visual Overhaul", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Complete redesign with compact layout and enhanced visuals");
            ImGui.BulletText("Character cards with integrated nameplates and action buttons");
            ImGui.BulletText("Glowing borders and enhanced hover effects");
            ImGui.BulletText("Optional setting for profiles to grow slightly on hover");
            ImGui.BulletText("Crown indicator for your designated Main Character");
            ImGui.BulletText("Resize Design Panel freely");
            ImGui.BulletText("Drag & Drop character reordering added to Main Window (leftward movement only due to ImGui limitations)");
            ImGui.Spacing();

            // Tutorial System (NEW!)
            DrawFeatureSection("\uf19d", "Interactive Tutorial System", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            ImGui.BulletText("Brand new guided tutorial for first-time users");
            ImGui.BulletText("Highlights and points to buttons and fields you need to interact with");
            ImGui.BulletText("Step-by-step guidance through Characters, Designs, and RP Profiles");
            ImGui.BulletText("Can be ended at any time if you prefer to explore on your own");
            ImGui.Spacing();

            // Design Preview Images (NEW!)
            DrawFeatureSection("\uf03e", "Design Preview Images", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Add custom preview images to your Designs");
            ImGui.BulletText("Preview images by hovering over the Apply (✓) button");
            ImGui.BulletText("Helps you quickly identify Designs at a glance");
            ImGui.Spacing();

            // Main Game Commands (NEW!)
            DrawFeatureSection("\uf120", "Base Game Command Support", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            ImGui.BulletText("Add base game commands through Advanced Mode");
            ImGui.BulletText("Example: Add '/gearset change 1' to switch jobs when applying Designs");
            ImGui.BulletText("Perfect combo with 'Reapply Last Design on Job Change' setting");
            ImGui.Spacing();

            // Random Character + Outfit (NEW!)
            DrawFeatureSection("\uf074", "Random Character & Outfit", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("New 'Random' button for spontaneous character switching");
            ImGui.BulletText("Randomly picks from your CS+ Characters and their Designs");
            ImGui.BulletText("Setting to limit random selection to only favourited items");
            ImGui.Spacing();

            // Main CS+ Character (NEW!)
            DrawFeatureSection("\uf521", "Main CS+ Character", new Vector4(0.9f, 0.6f, 0.9f, 1.0f));
            ImGui.BulletText("Designate your main CS+ Character with a crown indicator");
            ImGui.BulletText("Crown display is optional - toggle in settings");
            ImGui.BulletText("'Reapply on Login' can be set to only apply your Main Character");
            ImGui.Spacing();

            // Character Assignments (NEW!)
            DrawFeatureSection("\uf0c1", "Character Assignments", new Vector4(0.6f, 1.0f, 0.8f, 1.0f));
            ImGui.BulletText("Assign specific CS+ Characters to specific in-game characters");
            ImGui.BulletText("Auto-apply designated CS+ characters when logging into assigned real characters");
            ImGui.BulletText("Dropdown selection from characters the plugin has seen before");
            ImGui.BulletText("Multiple real characters can share the same CS+ character");
            ImGui.BulletText("Takes priority over 'last used' system but respects Main Character Only Mode");
            ImGui.BulletText("Perfect for players with multiple alts who want consistent character setups");
            ImGui.Spacing();

            // Quick Character Switch Improvements
            DrawFeatureSection("\uf0e7", "Quick Character Switch Updates", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Now remembers your last used character like Apply on Login");
            ImGui.BulletText("Ready to go when you log in as that character");
            ImGui.BulletText("Will also switch to be on your current CS+ Character if applied through other methods");
            ImGui.Spacing();

            // Bug Fixes & QoL
            DrawFeatureSection("\uf085", "Bug Fixes & Quality of Life", new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
            ImGui.BulletText("Fixed Quick Switch window scroll issues");
            ImGui.BulletText("Disabled window docking to prevent UI conflicts");
            ImGui.BulletText("Added ghost images for drag and drop operations");
            ImGui.BulletText("Automatic character config backup on updates or every 7 days");
            ImGui.BulletText("Various performance improvements and optimizations");
        }

        private void Draw110Notes()
        {
            // Apply Character on Login
            DrawFeatureSection("\uf4fc", "Apply Character on Login", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("New opt-in setting in the plugin options.");
            ImGui.BulletText("Character Select+ will remember the last applied character.");
            ImGui.BulletText("Next time you log in, it will automatically apply that character.");
            ImGui.BulletText("⚠️ May conflict if you are using Glamourer Automations.");
            ImGui.Spacing();

            // Apply Appearance on Job Change
            DrawFeatureSection("\uf4fc", "Apply Appearance on Job Change", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("New opt-in setting in the plugin options.");
            ImGui.BulletText("Character Select+ will remember the last applied character and/or design.");
            ImGui.BulletText("When you switch between jobs, it will automatically apply that character/design.");
            ImGui.BulletText("⚠️ WILL 100 percent conflict if you are using Glamourer Automations.");
            ImGui.Spacing();

            // Designs
            DrawFeatureSection("\uf07b", "Design Panel Rework", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Buttons now only appear on hover, keeping the panel clean and focused.");
            ImGui.BulletText("Reorder designs by dragging the coloured handle‐bar on the left — click and drag to move.");
            ImGui.BulletText("Create new folders inline via the folder icon next to the + button, no extra windows needed.");
            ImGui.BulletText("Drag-and-drop designs into, out of, and between folders directly within the panel.");
            ImGui.BulletText("Right-click folders for inline Rename/Delete context menu, with instant application.");
            ImGui.Spacing();

            // Compact Quick Switch
            DrawFeatureSection("\uf0a0", "Compact Quick Character Switch", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Toggleable setting to hide the title bar and window frame for a slim bar.");
            ImGui.BulletText("Keeps dropdowns and apply button only, preserving full switch functionality.");
            ImGui.Spacing();

            // UI Scaling Option
            DrawFeatureSection("\uf00e", "UI Scale Setting", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("You can now adjust the plugin UI scale from the settings menu.");
            ImGui.BulletText("Great for users on high-resolution monitors or 4K displays.");
            ImGui.BulletText("Let me know if there are any issues using this.");
            ImGui.BulletText("⚠️ If your UI is fine as-is, best to leave this be.");
            ImGui.Spacing();
        }

        private void Draw1100Notes()
        {
            // RP Profile Panel
            DrawFeatureSection("\uf2c2", "RolePlay Profile Panel", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Add bios, pronouns, orientation, and more for each character.");
            ImGui.BulletText("Choose a unique image or reuse the character image.");
            ImGui.BulletText("Use pan and zoom controls to fine-tune the RP portrait.");
            ImGui.BulletText("Control visibility: keep private or share with others.");
            ImGui.BulletText("Once applied, that character's RP profile is active.");
            ImGui.BulletText("You can view others' profiles (if shared) and vice versa.");
            ImGui.BulletText("Use /viewrp self | /t | First Last@World to view.");
            ImGui.BulletText("Right-click in the party list, friends list, or chat to access shared RP cards.");
            ImGui.Spacing();

            // Glamourer Automations
            DrawFeatureSection("\uf5c3", "Glamourer Automations for Characters & Designs", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Characters & Designs can now trigger specific Glamourer Automation profiles.");
            ImGui.BulletText("This is *opt-in* — toggle it in plugin settings.");
            ImGui.BulletText("If no automation is assigned, the design defaults to 'None'.");
            ImGui.Spacing();
            ImGui.Text("To avoid errors, set up a 'None' automation:");
            ImGui.BulletText("1. Open Glamourer > Automations.");
            ImGui.BulletText("2. Create an Automation named 'None'.");
            ImGui.BulletText("3. Add your in-game character name beside 'Any World' then Set to Character.");
            ImGui.BulletText("4. That's it. Don't touch anything else, you're done!");
            ImGui.Spacing();

            // Customize+
            DrawFeatureSection("\uf234", "Customize+ Profiles for Designs", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Each design can now assign its own Customize+ profile.");
            ImGui.BulletText("This gives you finer control over visual changes per design.");
            ImGui.Spacing();

            // Manual Reordering
            DrawFeatureSection("\uf0b0", "Manual Character Reordering", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Use the 'Reorder Characters' button at the bottom-left.");
            ImGui.BulletText("Drag and drop profiles, then press Save to lock it in.");
            ImGui.Spacing();

            // Search
            DrawFeatureSection("\uf002", "Character Search Bar", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Click the magnifying glass to search by name instantly.");
            ImGui.Spacing();

            // Tagging
            DrawFeatureSection("\uf07b", "Tagging System", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Add comma-separated 'tags' to organize characters.");
            ImGui.BulletText("Click the filter icon to filter — characters can appear in multiple tags!");
            ImGui.Spacing();

            // Apply to Target
            DrawFeatureSection("\uf140", "Right-click → Apply to Target", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Right-click a character in Character Select+ with a target selected.");
            ImGui.BulletText("Apply their setup — or even one of their individual designs — to the target.");
            ImGui.Spacing();

            // Copy Designs
            DrawFeatureSection("\uf0c5", "Copy Designs Between Characters", new Vector4(0.6f, 0.9f, 1.0f, 1.0f));
            ImGui.BulletText("Hold Shift and click the '+' button in Designs to open the Design Importer.");
            ImGui.BulletText("Click the + beside a design to copy it. Repeat as needed!");
            ImGui.Spacing();

            // Other changes
            DrawFeatureSection("\uf085", "Other Changes", new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
            ImGui.BulletText("Older Design macros were automatically upgraded.");
            ImGui.BulletText("Various UI tweaks, bugfixes, and behind-the-scenes improvements.");
        }

        private void DrawBottomButton(float totalScale)
        {
            ImGui.Spacing();
            ImGui.Spacing();

            // Center the button
            float windowWidth = ImGui.GetWindowSize().X;
            float buttonWidth = 90f * totalScale;
            ImGui.SetCursorPosX((windowWidth - buttonWidth) * 0.5f);

            // Button is only enabled if user has scrolled enough
            bool buttonEnabled = hasScrolledToEnd;

            if (!buttonEnabled)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f); // Make it look disabled
            }

            // Styled button - purple when enabled, gray when disabled
            if (buttonEnabled)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.2f, 0.4f, 0.8f)); // Purple base
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.3f, 0.5f, 1f)); // Purple hover
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.25f, 0.15f, 0.35f, 1f)); // Purple active
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.8f)); // Gray base
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
            }

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f * totalScale);

            bool buttonClicked = ImGui.Button("Got it!", new Vector2(buttonWidth, 30 * totalScale));

            // Show tooltip when disabled
            if (!buttonEnabled && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip("Read through the new features first! There's a lot!");
            }

            if (buttonClicked && buttonEnabled)
            {
                plugin.Configuration.LastSeenVersion = Plugin.CurrentPluginVersion;
                plugin.Configuration.Save();
                IsOpen = false;
                if (OpenMainMenuOnClose)
                {
                    plugin.ToggleMainUI();
                }
                OpenMainMenuOnClose = false;
            }

            ImGui.PopStyleVar(!buttonEnabled ? 2 : 1);
            ImGui.PopStyleColor(3);
        }

        // Optional debug method - temporarily call this in Draw() to see scroll values
        private void DrawDebugInfo()
        {
            ImGui.Spacing();
            ImGui.Text($"Scroll Debug Info:");

            // Get the scroll values from the child window
            if (ImGui.BeginChild("PatchNotesScroll", Vector2.Zero, false))
            {
                float currentScrollY = ImGui.GetScrollY();
                float maxScrollY = ImGui.GetScrollMaxY();
                ImGui.EndChild();

                ImGui.Text($"Current: {currentScrollY:F1}, Max: {maxScrollY:F1}");
                ImGui.Text($"Progress: {(maxScrollY > 0 ? (currentScrollY / maxScrollY * 100) : 0):F1}%");
                ImGui.Text($"hasScrolledToEnd: {hasScrolledToEnd}");
                ImGui.Text($"85% threshold: {maxScrollY * 0.85f:F1}");
            }
        }

        private void DrawParticleEffects(ImDrawListPtr drawList, Vector2 bannerStart, Vector2 bannerSize)
        {
            float deltaTime = ImGui.GetIO().DeltaTime;
            particleTimer += deltaTime;

            // Spawn new particles more frequently and across the entire banner
            if (particleTimer > 0.15f && particles.Count < 40) // More particles, spawn faster
            {
                SpawnParticle(bannerStart, bannerSize);
                particleTimer = 0f;
            }

            // Update and draw existing particles
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var particle = particles[i];

                // Update particle
                particle.Position += particle.Velocity * deltaTime;
                particle.Life -= deltaTime;

                // Remove dead particles or ones that left the banner area
                if (particle.Life <= 0 ||
                    particle.Position.X > bannerStart.X + bannerSize.X + 50 ||
                    particle.Position.Y < bannerStart.Y - 50 ||
                    particle.Position.Y > bannerStart.Y + bannerSize.Y + 50)
                {
                    particles.RemoveAt(i);
                    continue;
                }

                // Calculate alpha based on life remaining
                float alpha = Math.Min(1f, particle.Life / particle.MaxLife);
                var color = new Vector4(particle.Color.X, particle.Color.Y, particle.Color.Z, particle.Color.W * alpha);

                // Draw particle as a small circle
                drawList.AddCircleFilled(
                    particle.Position,
                    particle.Size,
                    ImGui.GetColorU32(color)
                );

                // Subtle glow effect
                if (alpha > 0.3f)
                {
                    var glowColor = new Vector4(color.X, color.Y, color.Z, color.W * 0.15f);
                    drawList.AddCircleFilled(
                        particle.Position,
                        particle.Size * 2.5f,
                        ImGui.GetColorU32(glowColor)
                    );
                }

                particles[i] = particle;
            }
        }

        private void SpawnParticle(Vector2 bannerStart, Vector2 bannerSize)
        {
            var particle = new Particle
            {
                // Spawn randomly across the ENTIRE banner area
                Position = new Vector2(
                    bannerStart.X + (float)particleRandom.NextDouble() * bannerSize.X,
                    bannerStart.Y + (float)particleRandom.NextDouble() * bannerSize.Y
                ),

                Velocity = new Vector2(
                    -10f + (float)particleRandom.NextDouble() * 20f,
                    -15f + (float)particleRandom.NextDouble() * -10f
                ),

                MaxLife = 6f + (float)particleRandom.NextDouble() * 4f,
                Size = 1.5f + (float)particleRandom.NextDouble() * 2.5f,

                // Winter/Christmas themed colors - whites, blues, silvers
                Color = particleRandom.Next(5) switch
                {
                    0 => new Vector4(1.0f, 1.0f, 1.0f, 0.8f),  // Pure white (snowflake)
                    1 => new Vector4(0.9f, 0.95f, 1.0f, 0.7f), // Soft white-blue
                    2 => new Vector4(0.8f, 0.9f, 1.0f, 0.6f),  // Light blue
                    3 => new Vector4(0.95f, 0.95f, 0.95f, 0.7f), // Silver-white
                    _ => new Vector4(0.85f, 0.92f, 1.0f, 0.6f)   // Icy blue
                }
            };

            particle.Life = particle.MaxLife;
            particles.Add(particle);
        }

        private void DrawWinterAnnouncementBox(float totalScale)
        {
            // Winter/Christmas announcement as a child window/scrollable area
            ImGui.BeginChild("WinterAnnouncement", new Vector2(0, 120 * totalScale), true, ImGuiWindowFlags.None);
            
            // Title with snowflake icons and date
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(new Vector4(0.9f, 0.95f, 1.0f, 1.0f), "\uf2dc"); // FontAwesome snowflake
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.2f, 0.8f, 1.0f, 1.0f), " Happy Holidays from Character Select+ ");
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(new Vector4(0.9f, 0.95f, 1.0f, 1.0f), "\uf2dc"); // FontAwesome snowflake
            ImGui.PopFont();
            
            ImGui.Spacing();
            
            // Holiday message and thank you - conversational style
            ImGui.PushTextWrapPos();
            ImGui.TextColored(new Vector4(0.9f, 0.95f, 1.0f, 1.0f), 
                "Season's greetings, adventurers! As we wrap up an amazing year, I wanted to take a moment to say thank you to everyone who has been enjoying Character Select+. " +
                "Your feedback, suggestions, and support have made this plugin what it is today. Whether you're creating new characters, perfecting your designs, or exploring the latest features, " +
                "you're the reason I love working on this project. Wishing you all a wonderful holiday season filled with joy, creativity, and fantastic adventures in FFXIV!");
            ImGui.PopTextWrapPos();
            
            ImGui.EndChild();
            
            ImGui.Spacing();
        }

    }
}

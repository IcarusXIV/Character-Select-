using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using System.Numerics;

namespace CharacterSelectPlugin.Windows
{
    public class PatchNotesWindow : Window
    {
        private readonly Plugin plugin;

        public PatchNotesWindow(Plugin plugin) : base("Character Select+ – What's New?", ImGuiWindowFlags.AlwaysAutoResize)
        {
            this.plugin = plugin;
            IsOpen = false;
        }

        public override void Draw()
        {
            ImGui.SetNextWindowSizeConstraints(new Vector2(480, 1051), new Vector2(float.MaxValue, float.MaxValue));
            ImGui.PushTextWrapPos();

            ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.2f, 1.0f), $"★ New in v{Plugin.CurrentPluginVersion}");
            ImGui.Separator();
            ImGui.Spacing();

            // Latest Patch Notes
            if (ImGui.CollapsingHeader("v1.1.0.8 - v1.1.1.2 – April 18 2025", ImGuiTreeNodeFlags.DefaultOpen))
            {
                // Apply Character on Login
                ImGui.PushFont(UiBuilder.IconFont); ImGui.Text("\uf4fc"); ImGui.PopFont(); ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.8f, 0.95f, 1.0f, 1.0f), "Apply Character on Login");
                ImGui.BulletText("New opt-in setting in the plugin options.");
                ImGui.BulletText("Character Select+ will remember the last applied character.");
                ImGui.BulletText("Next time you log in, it will automatically apply that character.");
                ImGui.BulletText("⚠️ May conflict if you are using Glamourer Automations.");
                ImGui.Separator();

                // Apply Appearance on Job Change
                ImGui.PushFont(UiBuilder.IconFont); ImGui.Text("\uf4fc"); ImGui.PopFont(); ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.8f, 0.95f, 1.0f, 1.0f), "Apply Appearance on Job Change");
                ImGui.BulletText("New opt-in setting in the plugin options.");
                ImGui.BulletText("Character Select+ will remember the last applied character and/or design.");
                ImGui.BulletText("When you switch between jobs, it will automatically apply that character/design.");
                ImGui.BulletText("⚠️ WILL 100 percent conflict if you are using Glamourer Automations.");
                ImGui.Separator();

                // Designs
                ImGui.PushFont(UiBuilder.IconFont); ImGui.Text("\uf07b"); ImGui.PopFont(); ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.8f, 0.95f, 1.0f, 1.0f), "Design Panel Rework");
                ImGui.BulletText("Buttons now only appear on hover, keeping the panel clean and focused.");
                ImGui.BulletText("Reorder designs by dragging the colored handle‐bar on the left — click and drag to move.");
                ImGui.BulletText("Create new folders inline via the folder icon next to the + button, no extra windows needed.");
                ImGui.BulletText("Drag-and-drop designs into, out of, and between folders directly within the panel.");
                ImGui.BulletText("Right-click folders for inline Rename/Delete context menu, with instant application.");
                ImGui.Separator();

                // Compact Quick Switch
                ImGui.PushFont(UiBuilder.IconFont); ImGui.Text("\uf0a0"); ImGui.PopFont(); ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.8f, 0.95f, 1.0f, 1.0f), "Compact Quick Character Switch");
                ImGui.BulletText("Toggleable setting to hide the title bar and window frame for a slim bar.");
                ImGui.BulletText("Keeps dropdowns and apply button only, preserving full switch functionality.");
                ImGui.Separator();

                // UI Scaling Option
                ImGui.PushFont(UiBuilder.IconFont); ImGui.Text("\uf00e"); ImGui.PopFont(); ImGui.SameLine(); // <i class="fas fa-search-plus"></i>
                ImGui.TextColored(new Vector4(0.8f, 0.95f, 1.0f, 1.0f), "UI Scale Setting");
                ImGui.BulletText("You can now adjust the plugin UI scale from the settings menu.");
                ImGui.BulletText("Great for users on high-resolution monitors or 4K displays.");
                ImGui.BulletText("Let me know if there are any issues using this.");
                ImGui.BulletText("⚠️ If your UI is fine as-is, best to leave this be.");
                ImGui.Separator();
            }

            // Previous Patch Notes
            if (ImGui.CollapsingHeader("v1.1.0.(0-7) – April 09 2025", ImGuiTreeNodeFlags.None))
            {
                Draw1100Notes();
            }

            ImGui.Spacing(); ImGui.Spacing();
            float windowWidth = ImGui.GetWindowSize().X;
            float buttonWidth = 90f;
            ImGui.SetCursorPosX((windowWidth - buttonWidth) * 0.5f);

            if (ImGui.Button("Got it!", new Vector2(buttonWidth, 0)))
            {
                plugin.Configuration.LastSeenVersion = Plugin.CurrentPluginVersion;
                plugin.Configuration.Save();
                IsOpen = false;
                plugin.ToggleMainUI();
            }

            ImGui.PopTextWrapPos();
        }
        private void Draw1100Notes()
        {
            // RP Profile Panel
            ImGui.PushFont(UiBuilder.IconFont); ImGui.Text("\uf2c2"); ImGui.PopFont(); ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.8f, 0.95f, 1.0f, 1.0f), "RolePlay Profile Panel");
            ImGui.BulletText("Add bios, pronouns, orientation, and more for each character.");
            ImGui.BulletText("Choose a unique image or reuse the character image.");
            ImGui.BulletText("Use pan and zoom controls to fine-tune the RP portrait.");
            ImGui.BulletText("Control visibility: keep private or share with others.");
            ImGui.BulletText("Once applied, that character’s RP profile is active.");
            ImGui.BulletText("You can view others’ profiles (if shared) and vice versa.");
            ImGui.BulletText("Use /viewrp self | /t | First Last@World to view.");
            ImGui.BulletText("Right-click in the party list, friends list, or chat to access shared RP cards.");
            ImGui.Separator();

            // Glamourer Automations
            ImGui.PushFont(UiBuilder.IconFont); ImGui.Text("\uf5c3"); ImGui.PopFont(); ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.8f, 0.95f, 1.0f, 1.0f), "Glamourer Automations for Characters & Designs");
            ImGui.BulletText("Characters & Designs can now trigger specific Glamourer Automation profiles.");
            ImGui.BulletText("This is *opt-in* — toggle it in plugin settings.");
            ImGui.BulletText("If no automation is assigned, the design defaults to 'None'.");
            ImGui.Spacing();
            ImGui.Text("To avoid errors, set up a 'None' automation:");
            ImGui.BulletText("1. Open Glamourer > Automations.");
            ImGui.BulletText("2. Create an Automation named 'None'.");
            ImGui.BulletText("3. Add your in-game character name beside 'Any World' then Set to Character.");
            ImGui.BulletText("4. That’s it. Don’t touch anything else, you’re done!");
            ImGui.Separator();

            // Customize+
            ImGui.PushFont(UiBuilder.IconFont); ImGui.Text("\uf234"); ImGui.PopFont(); ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.8f, 0.95f, 1.0f, 1.0f), "Customize+ Profiles for Designs");
            ImGui.BulletText("Each design can now assign its own Customize+ profile.");
            ImGui.BulletText("This gives you finer control over visual changes per design.");
            ImGui.Separator();

            // Manual Reordering
            ImGui.PushFont(UiBuilder.IconFont); ImGui.Text("\uf0b0"); ImGui.PopFont(); ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.8f, 0.95f, 1.0f, 1.0f), "Manual Character Reordering");
            ImGui.BulletText("Use the 'Reorder Characters' button at the bottom-left.");
            ImGui.BulletText("Drag and drop profiles, then press Save to lock it in.");
            ImGui.Separator();

            // Search
            ImGui.PushFont(UiBuilder.IconFont); ImGui.Text("\uf002"); ImGui.PopFont(); ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.8f, 0.95f, 1.0f, 1.0f), "Character Search Bar");
            ImGui.BulletText("Click the magnifying glass to search by name instantly.");
            ImGui.Separator();

            // Tagging
            ImGui.PushFont(UiBuilder.IconFont); ImGui.Text("\uf07b"); ImGui.PopFont(); ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.8f, 0.95f, 1.0f, 1.0f), "Tagging System");
            ImGui.BulletText("Add comma-separated 'tags' to organize characters.");
            ImGui.BulletText("Click the filter icon to filter — characters can appear in multiple tags!");
            ImGui.Separator();

            // Apply to Target
            ImGui.PushFont(UiBuilder.IconFont); ImGui.Text("\uf140"); ImGui.PopFont(); ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.8f, 0.95f, 1.0f, 1.0f), "Right-click → Apply to Target");
            ImGui.BulletText("Right-click a character in Character Select+ with a target selected.");
            ImGui.BulletText("Apply their setup — or even one of their individual designs — to the target.");
            ImGui.Separator();

            // Copy Designs
            ImGui.PushFont(UiBuilder.IconFont); ImGui.Text("\uf0c5"); ImGui.PopFont(); ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.8f, 0.95f, 1.0f, 1.0f), "Copy Designs Between Characters");
            ImGui.BulletText("Hold Shift and click the '+' button in Designs to open the Design Importer.");
            ImGui.BulletText("Click the + beside a design to copy it. Repeat as needed!");
            ImGui.Separator();

            // Other changes
            ImGui.PushFont(UiBuilder.IconFont); ImGui.Text("\uf085"); ImGui.PopFont(); ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.9f, 1.0f), "Other Changes");
            ImGui.BulletText("Older Design macros were automatically upgraded.");
            ImGui.BulletText("Various UI tweaks, bugfixes, and behind-the-scenes improvements.");
        }

    }
}

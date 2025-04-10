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
            ImGui.PushTextWrapPos();

            ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.2f, 1.0f), $"★ New in v{Plugin.CurrentPluginVersion}");
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.PushFont(UiBuilder.IconFont); ImGui.Text("\uf055"); ImGui.PopFont(); ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.9f, 1.0f), "New Features");
            ImGui.Separator();

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
            ImGui.TextColored(new Vector4(0.8f, 0.95f, 1.0f, 1.0f), "Glamourer Automations for Designs");
            ImGui.BulletText("Designs can now trigger specific Glamourer Automation profiles.");
            ImGui.BulletText("This is *opt-in* — toggle it in plugin settings.");
            ImGui.BulletText("If no automation is assigned, the design defaults to 'None'.");
            ImGui.Spacing();
            ImGui.Text("To avoid errors, set up a 'None' automation:");
            ImGui.BulletText("1. Open Glamourer > Automations.");
            ImGui.BulletText("2. Create an Automation named 'None'.");
            ImGui.BulletText("3. Add your in-game character name beside 'Any World'.");
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

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.PushFont(UiBuilder.IconFont); ImGui.Text("\uf085"); ImGui.PopFont(); ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.9f, 1.0f), "Other Changes");
            ImGui.Separator();
            ImGui.BulletText("Older Design macros were automatically upgraded.");
            ImGui.BulletText("Various UI tweaks, bugfixes, and behind-the-scenes improvements.");

            ImGui.Spacing();
            ImGui.Spacing();

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
    }
}

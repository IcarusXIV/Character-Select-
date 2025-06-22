using Dalamud.Interface.Windowing;
using ImGuiNET;
using System.IO;
using System;
using System.Numerics;
using System.Threading;
using System.Windows.Forms;

namespace CharacterSelectPlugin.Windows
{
    public class RPProfileWindow : Window
    {
        private readonly Plugin plugin;
        private Character? character;
        private RPProfile profile = new();

        private string pronouns = "";
        private string gender = "";
        private string age = "";
        private string orientation = "";
        private string relationship = "";
        private string occupation = "";
        private string abilities = "";
        private string bio = "";
        private string tags = "";
        private string race = "";

        public RPProfileWindow(Plugin plugin) : base("Roleplay Profile", ImGuiWindowFlags.AlwaysAutoResize)
        {
            this.plugin = plugin;
            IsOpen = false;
        }

        public void SetCharacter(Character character)
        {
            this.character = character;
            profile = character.RPProfile ??= new RPProfile();

            var rp = character.RPProfile ??= new RPProfile();
            pronouns = rp.Pronouns ?? "";
            gender = rp.Gender ?? "";
            age = rp.Age ?? "";
            race = rp.Race ?? "";
            orientation = rp.Orientation ?? "";
            relationship = rp.Relationship ?? "";
            occupation = rp.Occupation ?? "";
            abilities = rp.Abilities ?? "";
            bio = rp.Bio ?? "";
            tags = rp.Tags ?? "";
        }

        public override void Draw()
        {
            if (character == null)
            {
                ImGui.Text("No character selected.");
                return;
            }
            var rp = character.RPProfile ??= new RPProfile();

            // Image Override Section (Top of Window)
            ImGui.Text("Profile Image:");

            // Choose Image Button
            if (ImGui.Button("Choose Image"))
            {
                try
                {
                    Thread thread = new Thread(() =>
                    {
                        try
                        {
                            using (OpenFileDialog openFileDialog = new OpenFileDialog())
                            {
                                openFileDialog.Filter = "PNG files (*.png)|*.png";
                                openFileDialog.Title = "Select Profile Image";

                                if (openFileDialog.ShowDialog() == DialogResult.OK)
                                {
                                    lock (this)
                                    {
                                        rp.CustomImagePath = openFileDialog.FileName;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.Error($"Error opening file picker: {ex.Message}");
                        }
                    });

                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"Critical file picker error: {ex.Message}");
                }
            }

            ImGui.SameLine();
            if (!string.IsNullOrEmpty(rp.CustomImagePath))
            {
                if (ImGui.Button("Clear"))
                    rp.CustomImagePath = "";
            }

            // Fixed-Frame Image Preview with Zoom & Pan
            string pluginDir = plugin.PluginDirectory;
            string fallback = Path.Combine(pluginDir, "Assets", "Default.png");
            string finalImagePath = !string.IsNullOrEmpty(rp.CustomImagePath) && File.Exists(rp.CustomImagePath)
                ? rp.CustomImagePath
                : (!string.IsNullOrEmpty(character.ImagePath) && File.Exists(character.ImagePath)
                    ? character.ImagePath
                    : fallback);

            if (File.Exists(finalImagePath))
            {
                var texture = Plugin.TextureProvider.GetFromFile(finalImagePath).GetWrapOrDefault();
                if (texture != null)
                {
                    float frameSize = 150f;
                    float zoom = Math.Clamp(rp.ImageZoom, 0.5f, 5.0f);
                    Vector2 offset = rp.ImageOffset;

                    float imgAspect = (float)texture.Width / texture.Height;
                    float drawWidth, drawHeight;

                    if (imgAspect >= 1f)
                    {
                        drawHeight = frameSize * zoom;
                        drawWidth = drawHeight * imgAspect;
                    }
                    else
                    {
                        drawWidth = frameSize * zoom;
                        drawHeight = drawWidth / imgAspect;
                    }

                    Vector2 drawSize = new(drawWidth, drawHeight);
                    Vector2 drawPos = ImGui.GetCursorScreenPos() + offset;

                    // Background border
                    var cursor = ImGui.GetCursorScreenPos();
                    var drawList = ImGui.GetWindowDrawList();
                    drawList.AddRectFilled(cursor - new Vector2(2, 2), cursor + new Vector2(frameSize + 2), ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.4f, 0.4f, 1f)), 4);

                    // Crop region
                    ImGui.BeginChild("ImageCropFrame", new Vector2(frameSize), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                    ImGui.SetCursorScreenPos(drawPos);
                    ImGui.Image(texture.ImGuiHandle, drawSize);
                    ImGui.EndChild();
                }
                else
                {
                    ImGui.Text($"⚠ Failed to load: {Path.GetFileName(finalImagePath)}");
                }
            }
            else
            {
                ImGui.TextDisabled("No Image Available");
            }

            // Image Offset Sliders
            ImGui.Spacing();
            ImGui.Text("Image Position Offset:");
            ImGui.SameLine();
            ImGui.TextDisabled("ⓘ");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Use these sliders to reposition your image inside the fixed frame.");

            Vector2 newOffset = rp.ImageOffset;
            ImGui.PushItemWidth(200);
            ImGui.SliderFloat("X##offset", ref newOffset.X, -300f, 300f, "%.0f");
            ImGui.SliderFloat("Y##offset", ref newOffset.Y, -300f, 300f, "%.0f");
            ImGui.PopItemWidth();
            rp.ImageOffset = newOffset;

            // Zoom
            ImGui.Spacing();
            ImGui.Text("Zoom:");
            ImGui.SameLine();
            ImGui.TextDisabled("ⓘ");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Zoom in or out on your image.");

            float newZoom = rp.ImageZoom;
            ImGui.PushItemWidth(200);
            ImGui.SliderFloat("##zoom", ref newZoom, 0.5f, 5.0f, "%.1fx");
            ImGui.PopItemWidth();
            rp.ImageZoom = newZoom;


            ImGui.Spacing();

            ImGui.PushTextWrapPos();

            ImGui.TextColored(new Vector4(1f, 0.75f, 0.4f, 1f), $"{character.Name} – Roleplay Profile");
            ImGui.Separator();

            DrawEditableField("Pronouns", ref pronouns);
            DrawEditableField("Gender", ref gender);
            DrawEditableField("Age", ref age);
            DrawEditableField("Race", ref race);
            DrawEditableField("Sexual Orientation", ref orientation);
            DrawEditableField("Relationship", ref relationship);
            DrawEditableField("Occupation", ref occupation);
            ImGui.Spacing();

            // Abilities (as tag-like input)
            ImGui.Text("Abilities:");
            ImGui.SameLine();
            ImGui.TextDisabled("ⓘ");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Separate abilities using commas: e.g. 'alchemy, bardic magic, swordplay'");

            ImGui.InputTextMultiline("##abilities", ref abilities, 1000, new Vector2(-1, 40));

            ImGui.Spacing();
            ImGui.Text("Bio:");
            ImGui.InputTextMultiline("##bio", ref bio, 1000, new Vector2(-1, 100));

            ImGui.Spacing();
            ImGui.Text("RP Tags:");
            ImGui.SameLine();
            ImGui.TextDisabled("ⓘ");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Separate tags using commas: e.g. 'casual, paragraph, lore-heavy'");

            ImGui.InputTextMultiline("##tags", ref tags, 1000, new Vector2(-1, 40));

            ImGui.Text("Profile Sharing:");
            ImGui.SameLine();

            var sharing = profile.Sharing;
            string GetSharingDisplayName(ProfileSharing option) => option switch
            {
                ProfileSharing.AlwaysShare => "Always Share",
                ProfileSharing.NeverShare => "Never Share",
                _ => option.ToString(),
            };

            if (ImGui.BeginCombo("##SharingSetting", GetSharingDisplayName(sharing)))
            {
                foreach (ProfileSharing option in Enum.GetValues(typeof(ProfileSharing)))
                {
                    bool isSelected = (sharing == option);
                    if (ImGui.Selectable(GetSharingDisplayName(option), isSelected))
                        profile.Sharing = option;
                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            ImGui.Spacing();
            if (ImGui.Button("Save"))
            {
                // Save all edits into profile
                profile.Pronouns = pronouns;
                profile.Gender = gender;
                profile.Age = age;
                profile.Race = race;
                profile.Orientation = orientation;
                profile.Relationship = relationship;
                profile.Occupation = occupation;
                profile.Abilities = abilities;
                profile.Bio = bio;
                profile.Tags = tags;
                profile.ImageZoom = rp.ImageZoom;
                profile.ImageOffset = rp.ImageOffset;

                // Save reference back to character and config
                character.RPProfile = profile;
                plugin.SaveConfiguration();
                // Upload profile just like ApplyProfile() does
                if (!string.IsNullOrWhiteSpace(character.LastInGameName))
                {
                    character.RPProfile.CharacterName = character.Name;
                    character.RPProfile.NameplateColor = character.NameplateColor;

                    _ = Plugin.UploadProfileAsync(character.RPProfile, character.LastInGameName);
                }


                IsOpen = false;

                // Auto-open view window with new profile
                plugin.RPProfileViewer.SetCharacter(character);
                plugin.RPProfileViewer.IsOpen = true;
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                IsOpen = false;
                // Immediately open the RP profile viewer
                plugin.RPProfileViewer.SetCharacter(character);
                plugin.RPProfileViewer.IsOpen = true;
            }

            ImGui.PopTextWrapPos();
        }

        private void DrawEditableField(string label, ref string value)
        {
            ImGui.Text(label + ":");
            ImGui.SameLine(130); // Adjust for better alignment
            ImGui.SetNextItemWidth(200);
            ImGui.InputText("##" + label, ref value, 100);
        }
    }
}

using Dalamud.Interface.Windowing;
using ImGuiNET;
using ImGuiScene;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace CharacterSelectPlugin.Windows
{
    public class RPProfileViewWindow : Window
    {
        private readonly Plugin plugin;
        private Character? character;
        private RPProfile? externalProfile = null;
        private bool showingExternal = false;
        private bool imageDownloadStarted = false;
        private bool imageDownloadComplete = false;
        private string? downloadedImagePath = null;
        private TextureWrap? cachedTexture;
        private string? cachedTexturePath;
        private bool bringToFront = false;
        private bool firstOpen = true;
        public RPProfile? CurrentProfile => showingExternal ? externalProfile : character?.RPProfile;

        public RPProfileViewWindow(Plugin plugin)
            : base("RP Profile", ImGuiWindowFlags.AlwaysAutoResize)
        {
            this.plugin = plugin;
            IsOpen = false;
        }
        public void SetCharacter(Character character)
        {
            this.character = character;
            showingExternal = false;
            bringToFront = true;

            // Clear old cached texture so image reloads properly
            cachedTexture?.Dispose();
            cachedTexture = null;
            cachedTexturePath = null;
        }
        public override void PreDraw()
        {
            if (IsOpen && bringToFront)
            {
                ImGui.SetNextWindowFocus();

                if (firstOpen)
                {
                    ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
                    firstOpen = false;
                }

                bringToFront = false;
            }
        }

        public override void Draw()
        {
            RPProfile? rp = null;
            Vector3 nameplateColor;
            string displayName;
            // Case: Viewing an external profile
            if (showingExternal && externalProfile != null)
            {
                rp = externalProfile;
                nameplateColor = (Vector3)externalProfile.NameplateColor;
                displayName = externalProfile.CharacterName ?? "External Profile";
            }
            // Case: Viewing a local character's profile
            else if (character != null && character.RPProfile != null)
            {
                rp = character.RPProfile;
                nameplateColor = character.NameplateColor;
                displayName = character.Name;
            }
            else
            {
                ImGui.Text("No profile available.");
                return;
            }
            ImGui.PushTextWrapPos();

            // Top Bar
            var color = ResolveNameplateColor();
            var topColor = new Vector4(color.X, color.Y, color.Z, 1f);
            var drawList = ImGui.GetWindowDrawList();
            var topLeft = ImGui.GetCursorScreenPos();
            drawList.AddRectFilled(topLeft, topLeft + new Vector2(600, 6), ImGui.ColorConvertFloat4ToU32(topColor));

            ImGui.Dummy(new Vector2(1, 10)); // spacer

            ImGui.BeginChild("ProfileCard", new Vector2(600, 210), false);

            // Left (Image + Tags)
            ImGui.BeginGroup();

            string fallback = Path.Combine(plugin.PluginDirectory, "Assets", "Default.png");
            string? imagePath = null;

            // If viewing external profile and has ProfileImageUrl
            if (showingExternal && !string.IsNullOrEmpty(rp.ProfileImageUrl))
            {
                if (!imageDownloadStarted)
                {
                    imageDownloadStarted = true;
                    Task.Run(() =>
                    {
                        try
                        {
                            using var client = new System.Net.Http.HttpClient();
                            var data = client.GetByteArrayAsync(rp.ProfileImageUrl).GetAwaiter().GetResult();

                            var hash = Convert.ToBase64String(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(rp.ProfileImageUrl)))
            .Replace("/", "_").Replace("+", "-");
                            string fileName = $"RPImage_{hash}.png";
                            string path = Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), fileName);

                            // Save once only if not exists
                            File.WriteAllBytes(path, data);
                            Plugin.Log.Debug($"[RPProfileView] Downloaded image to: {path}");


                            downloadedImagePath = path;
                            imageDownloadComplete = true;

                            // Force window to update and focus
                            bringToFront = true;

                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.Error($"[RPProfileViewWindow] Failed to download profile image: {ex.Message}");
                            imageDownloadComplete = true;
                        }
                    });
                }


                if (imageDownloadComplete && File.Exists(downloadedImagePath))
                {
                    imagePath = downloadedImagePath;
                }
            }
            // Local fallback options
            else if (!string.IsNullOrEmpty(rp.CustomImagePath) && File.Exists(rp.CustomImagePath))
            {
                imagePath = rp.CustomImagePath;
            }
            else if (!showingExternal && character?.ImagePath is { Length: > 0 } && File.Exists(character.ImagePath))
            {
                imagePath = character.ImagePath;
            }

            // Final fallback
            string finalImagePath = !string.IsNullOrEmpty(imagePath) && File.Exists(imagePath) ? imagePath : fallback;


            var texture = Plugin.TextureProvider.GetFromFile(finalImagePath).GetWrapOrDefault();
            if (texture != null)
            {
                float previewSize = 150f;
                float zoom = Math.Clamp(rp.ImageZoom, 0.5f, 5.0f);
                Vector2 offset = rp.ImageOffset;

                float texAspect = (float)texture.Width / texture.Height;
                float drawWidth, drawHeight;

                if (texAspect >= 1f)
                {
                    drawHeight = previewSize * zoom;
                    drawWidth = drawHeight * texAspect;
                }
                else
                {
                    drawWidth = previewSize * zoom;
                    drawHeight = drawWidth / texAspect;
                }

                Vector2 drawSize = new(drawWidth, drawHeight);
                Vector2 cursor = ImGui.GetCursorScreenPos();

                // Outer border frame
                drawList.AddRectFilled(cursor - new Vector2(2, 2), cursor + new Vector2(previewSize + 2), ImGui.ColorConvertFloat4ToU32(topColor), 4);

                // Crop area
                ImGui.BeginChild("ImageView", new Vector2(previewSize), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                ImGui.SetCursorScreenPos(cursor + offset);
                ImGui.Image(texture.ImGuiHandle, drawSize);
                ImGui.EndChild();
            }

            else
            {
                ImGui.Dummy(new Vector2(150, 150));
            }

            if (!string.IsNullOrWhiteSpace(rp.Tags))
            {
                ImGui.Spacing();
                var tagColor = new Vector4((color.X + 1f) / 2f, (color.Y + 1f) / 2f, (color.Z + 1f) / 2f, 0.65f);
                var tags = rp.Tags.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t));
                foreach (var tag in tags)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, tagColor);
                    ImGui.Button(tag);
                    ImGui.PopStyleColor();
                    ImGui.SameLine();
                }
                ImGui.NewLine();
            }

            ImGui.EndGroup();

            // Name + Fields
            ImGui.SameLine();
            ImGui.SetCursorPosX(180); // ensure it never touches image

            ImGui.BeginGroup();
            // Header: Name – Pronouns   Roleplay Profile
            ImGui.TextColored(new Vector4(1f, 0.75f, 0.4f, 1f), displayName);
            if (!string.IsNullOrWhiteSpace(rp.Pronouns))
            {
                ImGui.SameLine();
                ImGui.Text($"– {rp.Pronouns}");
            }
            ImGui.SameLine();
            ImGui.TextDisabled("Roleplay Profile");

            ImGui.Spacing();

            // New field layout
            float colSplit = 200f;
            DrawFieldRow("▪ Gender", rp.Gender, "▪ Age", rp.Age, colSplit);
            DrawFieldRow("▪ Race", rp.Race, "▪ Orientation", rp.Orientation, colSplit);
            DrawFieldRow("▪ Relationship", rp.Relationship, "▪ Occupation", rp.Occupation, colSplit);
            if (!string.IsNullOrWhiteSpace(rp.Abilities))
            {
                ImGui.Spacing();
                ImGui.Text("Abilities:");

                var abilities = rp.Abilities.Split(',')
                    .Select(a => a.Trim())
                    .Where(a => !string.IsNullOrEmpty(a));

                ImGui.PushTextWrapPos();
                ImGui.Text(string.Join("   •   ", abilities));
                ImGui.PopTextWrapPos();
            }

            ImGui.EndGroup();
            ImGui.EndChild();

            // Divider Bar Below Card
            // Diamond Divider
            var diamond = "◆";
            var diamondWidth = ImGui.CalcTextSize(diamond).X;

            float barPadding = 6f;
            float totalWidth = 600f;
            float barLength = (totalWidth - diamondWidth - (barPadding * 2)) / 2f;
            float dividerY = ImGui.GetCursorScreenPos().Y;
            float dividerX = ImGui.GetCursorScreenPos().X;

            drawList.AddLine(
                new Vector2(dividerX, dividerY),
                new Vector2(dividerX + barLength, dividerY),
                ImGui.ColorConvertFloat4ToU32(topColor), 1f
            );

            ImGui.SetCursorScreenPos(new Vector2(dividerX + barLength + barPadding, dividerY - ImGui.GetTextLineHeight() / 2));
            ImGui.TextColored(topColor, diamond);

            drawList.AddLine(
                new Vector2(dividerX + barLength + barPadding + diamondWidth + barPadding, dividerY),
                new Vector2(dividerX + totalWidth, dividerY),
                ImGui.ColorConvertFloat4ToU32(topColor), 1f
            );

            ImGui.Dummy(new Vector2(1, 8));


            // Bio Section
            ImGui.Text("Bio:");
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.15f, 0.7f));
            ImGui.BeginChild("BioScroll", new Vector2(600, 120), true);
            ImGui.TextWrapped(string.IsNullOrWhiteSpace(rp.Bio) ? "—" : rp.Bio);
            ImGui.EndChild();
            ImGui.PopStyleColor();

            // Centered Edit Button
            ImGui.Spacing();
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - 120) * 0.5f);

            if (!showingExternal && character != null && ImGui.Button("Edit Profile", new Vector2(120, 0)))
            {
                plugin.RPProfileEditor.SetCharacter(character);
                plugin.RPProfileEditor.IsOpen = true;
                IsOpen = false;
            }


            ImGui.PopTextWrapPos();
            if (!IsOpen)
            {
                showingExternal = false;
                externalProfile = null;
            }
        }

        private void DrawFieldRow(string label1, string? val1, string label2, string? val2, float columnSplit)
        {
            float rowStartX = ImGui.GetCursorPosX();
            var labelColor = new Vector4(1f, 1f, 0.85f, 1f);

            // First column
            ImGui.SetCursorPosX(rowStartX);
            ImGui.TextColored(labelColor, label1 + ":");
            ImGui.SameLine();
            ImGui.Text(string.IsNullOrWhiteSpace(val1) ? "—" : val1);

            // Second column
            ImGui.SameLine();
            ImGui.SetCursorPosX(rowStartX + columnSplit);
            ImGui.TextColored(labelColor, label2 + ":");
            ImGui.SameLine();
            ImGui.Text(string.IsNullOrWhiteSpace(val2) ? "—" : val2);
        }
        public void SetExternalProfile(RPProfile profile)
        {
            externalProfile = profile;
            showingExternal = true;

            imageDownloadStarted = false;
            imageDownloadComplete = false;
            downloadedImagePath = null;

            cachedTexture?.Dispose();
            cachedTexture = null;
            cachedTexturePath = null;

            bringToFront = true; // Bring the window to front
        }
        private Vector3 ResolveNameplateColor()
        {
            Vector3 fallback = new(0.3f, 0.6f, 1.0f); // soft blue

            if (showingExternal && externalProfile != null)
            {
                var c = (Vector3)externalProfile.NameplateColor;
                // If the colour is effectively black or unset, fallback
                if (c.X < 0.01f && c.Y < 0.01f && c.Z < 0.01f)
                    return fallback;

                return c;
            }

            return character?.NameplateColor ?? fallback;
        }
    }
}

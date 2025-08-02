using Dalamud.Interface.Windowing;
using ImGuiNET;
using System.IO;
using System;
using System.Numerics;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;

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
        private Vector3? originalProfileColor;
        private string links = "";

        private string originalPronouns = "";
        private string originalGender = "";
        private string originalAge = "";
        private string originalOrientation = "";
        private string originalRelationship = "";
        private string originalOccupation = "";
        private string originalAbilities = "";
        private string originalBio = "";
        private string originalTags = "";
        private string originalRace = "";
        private string? originalBackgroundImage = null;
        private ProfileEffects originalEffects = new();
        private float originalImageZoom = 1.0f;
        private Vector2 originalImageOffset = Vector2.Zero;
        private string? originalCustomImagePath = null;
        private string originalLinks = "";

        private List<string> availableBackgrounds = new();
        private string[] backgroundDisplayNames = Array.Empty<string>();
        private int selectedBackgroundIndex = 0;
        private bool isNSFW = false;
        private bool originalIsNSFW = false;

        public RPProfileWindow(Plugin plugin) : base("Roleplay Profile", ImGuiWindowFlags.None)
        {
            this.plugin = plugin;
            IsOpen = false;
            LoadAvailableBackgrounds();

            var dpiScale = ImGui.GetIO().DisplayFramebufferScale.X;
            var uiScale = plugin.Configuration.UIScaleMultiplier;
            var totalScale = GetSafeScale(dpiScale * uiScale);

            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(700 * totalScale, 500 * totalScale),
                MaximumSize = new Vector2(900 * totalScale, 700 * totalScale)
            };
        }

        private void LoadAvailableBackgrounds()
        {
            try
            {
                string pluginDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "";
                string assetsPath = Path.Combine(pluginDirectory, "Assets", "Backgrounds");

                availableBackgrounds.Clear();
                availableBackgrounds.Add("");

                if (Directory.Exists(assetsPath))
                {
                    for (int i = 1; i <= 80; i++)
                    {
                        string numberedFile = $"{i}.jpg";
                        string fullPath = Path.Combine(assetsPath, numberedFile);

                        if (File.Exists(fullPath))
                        {
                            availableBackgrounds.Add(numberedFile);
                        }
                    }
                }

                backgroundDisplayNames = availableBackgrounds.Select(bg =>
                    string.IsNullOrEmpty(bg) ? "None (Procedural Theme)" : GetCustomBackgroundName(bg)
                ).ToArray();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error loading backgrounds: {ex.Message}");
                availableBackgrounds = new List<string> { "" };
                backgroundDisplayNames = new string[] { "None (Procedural Theme)" };
            }
        }

        private string GetCustomBackgroundName(string filename)
        {
            string numberStr = Path.GetFileNameWithoutExtension(filename);

            return numberStr switch
            {
                "1" => "Gloomy Occult - @KateFF14",
                "2" => "East Shroud - @Rishido_Cuore",
                "3" => "Coerthas - @_a222xiv",
                "4" => "Tuliyollal - @anoSviX",
                "5" => "Puppets' Bunker - @Ar_FF14_",
                "6" => "Crystal Tower - @Cielbn_FF14",
                "7" => "The Dead Ends Third Zone - @FF14__Ciel",
                "8" => "Radz-at-Han Market - @futsukatsuki",
                "9" => "Yak T'el - @gm_am03",
                "10" => "Delubrum Reginae - @h_x_ff14",
                "11" => "Aglaia - @haku_XIV",
                "12" => "The Aitiascope - @hoka_mit",
                "13" => "The Dead Ends First Zone - @i_id_tea",
                "14" => "the Lunar Subterrane - @igaigako",
                "15" => "The Black Shroud Bridge - @Kyrie_FF14",
                "16" => "Little Solace - @Kyrie_FF14",
                "17" => "Tuliyollal Beach - @Kyrie_FF14",
                "18" => "The Qitana Ravel - @Kyrie_FF14",
                "19" => "The Black Shroud Bridge 2 - @Kyrie_FF14",
                "20" => "The Grand Cosmos - @lemi_ff14",
                "21" => "Crystal Tower - @lemi_ff14",
                "22" => "Sinus Ardorum - @lemi_ff14",
                "23" => "Alexandria - @len_xiv",
                "24" => "Crystal Tower 2 - @len_xiv",
                "25" => "Eulmore - @len_xiv",
                "26" => "Ishgard - @Lina_ff14_",
                "27" => "Ishgard 2 - @Lina_ff14_",
                "28" => "Amourot - @Lina_ff14_",
                "29" => "Alexandria 2 - @Lina_ff14_",
                "30" => "Limsa Lominsa - @m_salyu63436",
                "31" => "Castrum Fluminis - @mario_ops",
                "32" => "Mamook - @mario_ops",
                "33" => "Mamook 2 - @mario_ops",
                "34" => "The Fringes - @MorgenRei",
                "35" => "Ul'dah - @opapi_ff14",
                "36" => "Solution Nine - @ratototo1209",
                "37" => "Kozama'uka - @reirxiv",
                "38" => "Occult Crescent - @reirxiv",
                "39" => "Occult Crescent 2 - @reirxiv",
                "40" => "Somewhere in La Noscea - @reirxiv",
                "41" => "Worlar's Echo - @Rishido_Cuore",
                "42" => "Kozama'uka 2 - @Rishido_Cuore",
                "43" => "Mor Dhona - @Rishido_Cuore",
                "44" => "Old Gridania - @Rishido_Cuore",
                "45" => "Soution Nine 2 - @Rishido_Cuore",
                "46" => "The Black Shroud - @Rishido_Cuore",
                "47" => "Kozama'uka 3 - @Rishido_Cuore",
                "48" => "The Great Gubal Library - @sheri_shi_",
                "49" => "Xelphatol - @sheri_shi_",
                "50" => "The Qitana Ravel 2 - @ST_261",
                "51" => "Living Memory - @anoSviX",
                "52" => "A Cave - @Ar_FF14_",
                "53" => "Uh..Nier Raid Place? - @BabeUnico",
                "54" => "Il Mheg 1 - @FF14__Ciel",
                "55" => "Garden - @FFAru7714",
                "56" => "The Aetherial Slough - @hoka_mit",
                "57" => "Garlemald - @igaigako",
                "58" => "Night Flowers - @KyoBlack_xiv",
                "59" => "Solution Nine 3 - @KyoBlack_xiv",
                "60" => "The Black Shroud 2 - @Kyrie_FF14",
                "61" => "Gears! - @Kyrie_FF14",
                "62" => "Crystarium - @len_xiv",
                "63" => "Sil'dihn Subterrane - @len_xiv",
                "64" => "Thaleia - @len_xiv",
                "65" => "Il Mheg 2 - @len_xiv",
                "66" => "Ishgard 3 - @Lina_ff14_",
                "67" => "Il Mheg 3 - @Lina_ff14_",
                "68" => "Urqopacha - @Lina_ff14_",
                "69" => "Mount Rokkon - @Lina_ff14_",
                "70" => "Great Gubal Library - @Lina_ff14_",
                "71" => "Clouds - @M_Cieux_FF14",
                "72" => "Forest Clearing - @natsuchrome",
                "73" => "Occult Crescent 3 - @NtatuA",
                "74" => "Crypt in a Cave - @opheli_msb10",
                "75" => "Mamook 3 - @Rishido_Cuore",
                "76" => "The Azim Steppe - @Rishido_Cuore",
                "77" => "Il Mheg 4 - @sakusaku121625",
                "78" => "Living Memory 2 - @sheri_shi_",
                "79" => "Puppet's Bunker - @YoshiFahrenheit",
                "80" => "Sil'dihn Subterrane 2 - @YoshiFahrenheit",

                _ => $"Background {numberStr}"
            };
        }

        public void SetCharacter(Character character)
        {
            this.character = character;
            plugin.IsRPProfileEditorOpen = true;
            profile = character.RPProfile ??= new RPProfile();

            var rp = character.RPProfile ??= new RPProfile();

            // Store current values
            pronouns = rp.Pronouns ?? "";
            race = rp.Race ?? "";
            gender = rp.Gender ?? "";
            age = rp.Age ?? "";
            occupation = rp.Occupation ?? "";
            orientation = rp.Orientation ?? "";
            relationship = rp.Relationship ?? "";
            abilities = rp.Abilities ?? "";
            tags = rp.Tags ?? "";
            bio = rp.Bio ?? "";
            links = rp.Links ?? "";
            isNSFW = rp.IsNSFW;

            // Store original values for cancel functionality
            originalPronouns = pronouns;
            originalRace = race;
            originalGender = gender;
            originalAge = age;
            originalOccupation = occupation;
            originalOrientation = orientation;
            originalRelationship = relationship;
            originalAbilities = abilities;
            originalTags = tags;
            originalBio = bio;
            originalTags = tags;
            originalBackgroundImage = rp.BackgroundImage;
            originalEffects = new ProfileEffects
            {
                CircuitBoard = rp.Effects.CircuitBoard,
                Fireflies = rp.Effects.Fireflies,
                FallingLeaves = rp.Effects.FallingLeaves,
                Butterflies = rp.Effects.Butterflies,
                Bats = rp.Effects.Bats,
                Fire = rp.Effects.Fire,
                Smoke = rp.Effects.Smoke,
                ColorScheme = rp.Effects.ColorScheme,
                CustomParticleColor = rp.Effects.CustomParticleColor
            };
            originalImageZoom = rp.ImageZoom;
            originalImageOffset = rp.ImageOffset;
            originalCustomImagePath = rp.CustomImagePath;
            originalLinks = links;
            originalIsNSFW = isNSFW;
            originalProfileColor = rp.ProfileColor;

            if (rp.ProfileColor == null)
            {
            }

            // Set background selection
            selectedBackgroundIndex = 0;
            if (!string.IsNullOrEmpty(rp.BackgroundImage))
            {
                int index = availableBackgrounds.IndexOf(rp.BackgroundImage);
                if (index > 0) selectedBackgroundIndex = index;
            }
        }

        public override void Draw()
        {
            var dpiScale = ImGui.GetIO().DisplayFramebufferScale.X;
            var uiScale = plugin.Configuration.UIScaleMultiplier;
            var totalScale = GetSafeScale(dpiScale * uiScale);

            if (character == null)
            {
                ImGui.Text("No character selected.");
                return;
            }

            // Dark stylin' on 'em
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.06f, 0.06f, 0.06f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.06f, 0.06f, 0.06f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.04f, 0.04f, 0.04f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.3f, 0.3f, 0.3f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.25f, 0.25f, 0.25f, 0.6f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.92f, 0.92f, 0.92f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(0.5f, 0.5f, 0.5f, 0.8f));

            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8 * totalScale, 4 * totalScale));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8 * totalScale, 6 * totalScale));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10.0f * totalScale);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8.0f * totalScale);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f * totalScale);
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 8.0f * totalScale);

            try
            {
                ImGui.SetNextWindowSize(new Vector2(750 * totalScale, 620 * totalScale), ImGuiCond.FirstUseEver);
                plugin.RPProfileEditorWindowPos = ImGui.GetWindowPos();
                plugin.RPProfileEditorWindowSize = ImGui.GetWindowSize();
                var rp = character.RPProfile ??= new RPProfile();

                var contentHeight = ImGui.GetContentRegionAvail().Y - (80 * totalScale);
                var availableWidth = ImGui.GetContentRegionAvail().X;
                var leftColumnWidth = availableWidth * 0.40f;
                var rightColumnWidth = availableWidth * 0.58f;

                // Image and appearance
                ImGui.BeginChild("##LeftColumn", new Vector2(leftColumnWidth, contentHeight), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);

                ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), "Profile Image");
                ImGui.Separator();

                if (ImGui.Button("Choose Image", new Vector2(120 * totalScale, 0)))
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
                if (!string.IsNullOrEmpty(rp.CustomImagePath) && ImGui.Button("Clear", new Vector2(60 * totalScale, 0)))
                    rp.CustomImagePath = "";

                ImGui.Spacing();

                // Image preview
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
                        float frameSize = 140f * totalScale;
                        float zoom = Math.Clamp(rp.ImageZoom, 0.5f, 5.0f);
                        Vector2 offset = rp.ImageOffset * totalScale;

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
                        drawList.AddRectFilled(cursor - new Vector2(2 * totalScale, 2 * totalScale), cursor + new Vector2(frameSize + (2 * totalScale)), ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.4f, 0.4f, 1f)), 4 * totalScale);

                        // Crop region
                        ImGui.BeginChild("ImageCropFrame", new Vector2(frameSize), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                        ImGui.SetCursorScreenPos(drawPos);
                        ImGui.Image(texture.ImGuiHandle, drawSize);
                        ImGui.EndChild();
                    }
                    else
                    {
                        ImGui.Text($"⚠ Failed to load image");
                    }
                }
                else
                {
                    ImGui.TextDisabled("No Image Available");
                }

                ImGui.Spacing();

                // Image controls
                ImGui.Text("Image Position:");
                Vector2 newOffset = rp.ImageOffset;
                ImGui.PushItemWidth(160 * totalScale);
                ImGui.SliderFloat("X Offset", ref newOffset.X, -300f, 300f, "%.0f");
                ImGui.SliderFloat("Y Offset", ref newOffset.Y, -300f, 300f, "%.0f");
                float newZoom = rp.ImageZoom;
                ImGui.SliderFloat("Zoom Level", ref newZoom, 0.5f, 5.0f, "%.1fx");
                ImGui.PopItemWidth();
                rp.ImageOffset = newOffset;
                rp.ImageZoom = newZoom;

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), "Profile Appearance");
                ImGui.Separator();

                ImGui.Text("Profile Colour:");
                ImGui.SameLine();
                ImGui.TextDisabled("ⓘ");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Choose your profile's accent colour. Used for borders, glows, and nameplate display.");

                Vector3 profileColor = rp.ProfileColor ?? character.NameplateColor;

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.25f, 0.25f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.35f, 0.35f, 0.35f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.12f, 0.12f, 0.12f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.18f, 0.18f, 0.18f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.22f, 0.22f, 0.22f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(character.NameplateColor.X, character.NameplateColor.Y, character.NameplateColor.Z, 1.0f));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f * totalScale);

                bool useNameplateColor = rp.ProfileColor == null;
                if (ImGui.Checkbox("Use Nameplate Colour", ref useNameplateColor))
                {
                    if (useNameplateColor)
                    {
                        rp.ProfileColor = null; // Use nameplate colour
                    }
                    else
                    {
                        rp.ProfileColor = character.NameplateColor;
                    }
                }

                ImGui.PopStyleVar(1);
                ImGui.PopStyleColor(8);

                // Color picker (ONLY show if custom colour is selected)
                if (!useNameplateColor)
                {
                    ImGui.Spacing();

                    ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.12f, 0.12f, 0.12f, 0.9f));
                    ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.18f, 0.18f, 0.18f, 0.9f));
                    ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.22f, 0.22f, 0.22f, 0.9f));

                    if (ImGui.ColorPicker3("##ProfileColourPicker", ref profileColor, ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoAlpha))
                    {
                        rp.ProfileColor = profileColor;
                    }

                    ImGui.PopStyleColor(3);
                }

                ImGui.Spacing();

                // Background section
                ImGui.Text("Background:");
                ImGui.SameLine();
                ImGui.TextDisabled("ⓘ");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Choose a background image from various FFXIV locations, or use procedural themes");

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.25f, 0.25f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.35f, 0.35f, 0.35f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f * totalScale);
                ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));

                // Previous/Next buttons
                if (ImGui.Button("◀", new Vector2(25 * totalScale, 0)))
                {
                    selectedBackgroundIndex = selectedBackgroundIndex > 0 ? selectedBackgroundIndex - 1 : backgroundDisplayNames.Length - 1;
                    rp.BackgroundImage = selectedBackgroundIndex > 0 ? availableBackgrounds[selectedBackgroundIndex] : null;
                }
                ImGui.SameLine();

                ImGui.PopStyleVar(2);
                ImGui.PopStyleColor(4);

                // Apply
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.12f, 0.12f, 0.12f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.18f, 0.18f, 0.18f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.22f, 0.22f, 0.22f, 0.9f));

                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - (30 * totalScale)); // Leave space for next button - scaled
                if (ImGui.Combo("##Background", ref selectedBackgroundIndex, backgroundDisplayNames, backgroundDisplayNames.Length))
                {
                    rp.BackgroundImage = selectedBackgroundIndex > 0 ? availableBackgrounds[selectedBackgroundIndex] : null;
                }
                plugin.RPBackgroundDropdownPos = ImGui.GetItemRectMin();
                plugin.RPBackgroundDropdownSize = ImGui.GetItemRectSize();
                ImGui.PopItemWidth();

                ImGui.PopStyleColor(3);

                ImGui.SameLine();

                // Apply button styling
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.25f, 0.25f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.35f, 0.35f, 0.35f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f * totalScale);
                ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));

                if (ImGui.Button("▶", new Vector2(25 * totalScale, 0)))
                {
                    selectedBackgroundIndex = (selectedBackgroundIndex + 1) % backgroundDisplayNames.Length;
                    rp.BackgroundImage = selectedBackgroundIndex > 0 ? availableBackgrounds[selectedBackgroundIndex] : null;
                }

                ImGui.PopStyleVar(2);
                ImGui.PopStyleColor(4);

                ImGui.Spacing();

                // Effects
                ImGui.Text("Visual Effects:");
                ImGui.SameLine();
                ImGui.TextDisabled("ⓘ");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Choose which animated effects to layer over your background. Mix and match as desired!");

                bool circuitBoard = rp.Effects.CircuitBoard;
                bool fireflies = rp.Effects.Fireflies;
                bool leaves = rp.Effects.FallingLeaves;
                bool butterflies = rp.Effects.Butterflies;
                bool bats = rp.Effects.Bats;
                bool fire = rp.Effects.Fire;
                bool smoke = rp.Effects.Smoke;

                // Checkbox styling
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.12f, 0.12f, 0.12f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.18f, 0.18f, 0.18f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.22f, 0.22f, 0.22f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(character.NameplateColor.X, character.NameplateColor.Y, character.NameplateColor.Z, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f * totalScale);

                // Effects
                var effectsStartPos = ImGui.GetCursorScreenPos();
                plugin.RPVisualEffectsPos = effectsStartPos;

                ImGui.Checkbox("Circuit Board", ref circuitBoard);
                ImGui.Checkbox("Fireflies/Sparkles", ref fireflies);
                ImGui.Checkbox("Falling Leaves", ref leaves);
                ImGui.Checkbox("Butterflies", ref butterflies);
                ImGui.Checkbox("Flying Bats", ref bats);
                ImGui.Checkbox("Fire Effects", ref fire);
                ImGui.Checkbox("Smoke/Mist", ref smoke);

                ImGui.PopStyleVar(1);
                ImGui.PopStyleColor(5);

                var effectsEndPos = ImGui.GetCursorScreenPos();
                plugin.RPVisualEffectsSize = new Vector2(ImGui.GetContentRegionAvail().X, effectsEndPos.Y - effectsStartPos.Y);

                // Save 
                rp.Effects.CircuitBoard = circuitBoard;
                rp.Effects.Fireflies = fireflies;
                rp.Effects.FallingLeaves = leaves;
                rp.Effects.Butterflies = butterflies;
                rp.Effects.Bats = bats;
                rp.Effects.Fire = fire;
                rp.Effects.Smoke = smoke;

                ImGui.Spacing();

                // Colour scheme with styling
                ImGui.Text("Particle Colours:");
                ImGui.SameLine();
                ImGui.TextDisabled("ⓘ");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Choose a colour scheme for particles to match your chosen background");

                var colorScheme = rp.Effects.ColorScheme;

                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.12f, 0.12f, 0.12f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.18f, 0.18f, 0.18f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.22f, 0.22f, 0.22f, 0.9f));

                ImGui.PushItemWidth(-1);
                if (ImGui.BeginCombo("##ColorScheme", GetColorSchemeDisplayName(colorScheme)))
                {
                    foreach (ParticleColorScheme scheme in Enum.GetValues(typeof(ParticleColorScheme)))
                    {
                        bool isSelected = (colorScheme == scheme);
                        if (ImGui.Selectable(GetColorSchemeDisplayName(scheme), isSelected))
                            rp.Effects.ColorScheme = scheme;
                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
                ImGui.PopItemWidth();

                ImGui.PopStyleColor(3);

                if (rp.Effects.ColorScheme == ParticleColorScheme.Custom)
                {
                    ImGui.Spacing();
                    Vector3 customColor = rp.Effects.CustomParticleColor;

                    ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.12f, 0.12f, 0.12f, 0.9f));
                    ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.18f, 0.18f, 0.18f, 0.9f));
                    ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.22f, 0.22f, 0.22f, 0.9f));

                    if (ImGui.ColorEdit3("Custom Colour", ref customColor))
                    {
                        rp.Effects.CustomParticleColor = customColor;
                    }

                    ImGui.PopStyleColor(3);
                }

                ImGui.EndChild();

                // Profile info
                ImGui.SameLine();
                ImGui.BeginChild("##RightColumn", new Vector2(rightColumnWidth, contentHeight), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);

                ImGui.TextColored(new Vector4(1f, 0.75f, 0.4f, 1f), $"{character.Name} – Profile Info");
                ImGui.Separator();

                // Keep stylin on 'em 
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.16f, 0.16f, 0.16f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.22f, 0.22f, 0.22f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.28f, 0.28f, 0.28f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f * totalScale);
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6 * totalScale, 4 * totalScale));

                // Profile fields
                ImGui.Text("Pronouns:");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##editPronouns", ref pronouns, 100);
                plugin.RPPronounsFieldPos = ImGui.GetItemRectMin();
                plugin.RPPronounsFieldSize = ImGui.GetItemRectSize();

                ImGui.Text("Race:");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##editRace", ref race, 100);

                ImGui.Text("Gender:");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##editGender", ref gender, 100);

                ImGui.Text("Age:");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##editAge", ref age, 100);

                ImGui.Text("Occupation:");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##editOccupation", ref occupation, 100);

                ImGui.Text("Orientation:");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##editOrientation", ref orientation, 100);

                ImGui.Text("Relationship:");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##editRelationship", ref relationship, 100);

                ImGui.Text("Abilities:");
                ImGui.SameLine();
                ImGui.TextDisabled("ⓘ");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Separate abilities using commas: e.g. 'alchemy, bardic magic, swordplay'");
                ImGui.InputTextMultiline("##abilities", ref abilities, 1000, new Vector2(-1, 35 * totalScale), ImGuiInputTextFlags.CtrlEnterForNewLine);

                ImGui.Text("Tags:");
                ImGui.SameLine();
                ImGui.TextDisabled("ⓘ");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Separate tags using commas: e.g. 'casual, paragraph, lore-heavy'");
                ImGui.InputTextMultiline("##tags", ref tags, 1000, new Vector2(-1, 35 * totalScale));

                ImGui.Spacing();
                ImGui.Text("Bio:");
                ImGui.InputTextMultiline("##bio", ref bio, 1000, new Vector2(-1, 90 * totalScale), ImGuiInputTextFlags.CtrlEnterForNewLine);
                plugin.RPBioFieldPos = ImGui.GetItemRectMin();
                plugin.RPBioFieldSize = ImGui.GetItemRectSize();

                ImGui.Text("Links:");
                ImGui.SameLine();
                ImGui.TextDisabled("ⓘ");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Add a social media link, website, etc.");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##editLinks", ref links, 1000);

                ImGui.PopStyleVar(2);
                ImGui.PopStyleColor(4);

                ImGui.EndChild();

                ImGui.Separator();
                ImGui.Spacing();

                // Profile sharing
                ImGui.Text("Profile Sharing:");
                var sharing = profile.Sharing;

                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.16f, 0.16f, 0.16f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.22f, 0.22f, 0.22f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.28f, 0.28f, 0.28f, 0.9f));

                ImGui.PushItemWidth(200 * totalScale); // Fixed width for sharing dropdown
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
                    plugin.RPSharingDropdownPos = ImGui.GetItemRectMin();
                    plugin.RPSharingDropdownSize = ImGui.GetItemRectSize();
                }
                ImGui.PopItemWidth();

                ImGui.PopStyleColor(3);

                // NSFW Checkbox
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.16f, 0.16f, 0.16f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.22f, 0.22f, 0.22f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.28f, 0.28f, 0.28f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(1.0f, 0.6f, 0.2f, 1.0f));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f * totalScale);

                if (ImGui.Checkbox("NSFW", ref isNSFW))
                {
                    // Checkbox changed
                }

                ImGui.PopStyleVar(1);
                ImGui.PopStyleColor(4);

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Mark this profile as NSFW (18+ content)");
                }

                ImGui.SameLine();

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.3f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f * totalScale);
                ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));

                float buttonWidth = 80f * totalScale;
                float totalButtonWidth = buttonWidth * 2 + (20f * totalScale); 
                float availableSpace = ImGui.GetContentRegionAvail().X;
                float startPos = availableSpace - totalButtonWidth; 

                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + startPos);
                if (ImGui.Button("Save", new Vector2(buttonWidth, 35 * totalScale))) 
                {
                    plugin.SaveRPProfileButtonPos = ImGui.GetItemRectMin();
                    plugin.SaveRPProfileButtonSize = ImGui.GetItemRectSize();

                    profile.Pronouns = pronouns;
                    profile.Race = race;
                    profile.Gender = gender;
                    profile.Age = age;
                    profile.Occupation = occupation;
                    profile.Orientation = orientation;
                    profile.Relationship = relationship;
                    profile.Abilities = abilities;
                    profile.Tags = tags;
                    profile.Bio = bio;
                    profile.Links = links;
                    profile.IsNSFW = isNSFW;
                    profile.BackgroundImage = rp.BackgroundImage;
                    profile.Effects = rp.Effects;
                    profile.ImageZoom = rp.ImageZoom;
                    profile.ImageOffset = rp.ImageOffset;
                    profile.CustomImagePath = rp.CustomImagePath;

                    character.BackgroundImage = rp.BackgroundImage;
                    character.Effects = rp.Effects;
                    profile.ProfileColor = rp.ProfileColor;
                    character.RPProfile = profile;

                    profile.GalleryStatus = character.GalleryStatus;
                    profile.AnimationTheme = character.RPProfile?.AnimationTheme ?? ProfileAnimationTheme.CircuitBoard;
                    profile.LastActiveTime = plugin.Configuration.ShowRecentlyActiveStatus ? DateTime.UtcNow : null;

                    string imagePathToUse = !string.IsNullOrEmpty(profile.CustomImagePath)
                        ? profile.CustomImagePath
                        : character.ImagePath;
                    profile.CustomImagePath = imagePathToUse;

                    profile.CharacterName = character.Name;
                    profile.NameplateColor = character.NameplateColor;

                    plugin.SaveConfiguration();

                    if (!string.IsNullOrWhiteSpace(character.LastInGameName))
                    {
                        // Check if we should upload to gallery based on main character setting
                        if (Plugin.ClientState.LocalPlayer is { } player && player.HomeWorld.IsValid)
                        {
                            string localName = player.Name.TextValue;
                            string worldName = player.HomeWorld.Value.Name.ToString();
                            string fullKey = $"{localName}@{worldName}";

                            // Use the same ShouldUploadToGallery logic
                            var userMain = plugin.Configuration.GalleryMainCharacter;
                            bool shouldUpload = !string.IsNullOrEmpty(userMain) &&
                                               fullKey == userMain &&
                                               profile.Sharing == ProfileSharing.ShowcasePublic;

                            if (shouldUpload)
                            {
                                _ = Plugin.UploadProfileAsync(profile, character.LastInGameName);
                                plugin.GalleryWindow.RefreshLikeStatesAfterProfileUpdate(character.Name);
                                Plugin.Log.Info($"[RPProfile] ✅ Uploaded profile for {character.Name} from RP editor");
                            }
                            else
                            {
                                Plugin.Log.Info($"[RPProfile] ⚠ Skipped gallery upload from RP editor - main character check failed");
                            }
                        }
                    }

                    IsOpen = false;
                    plugin.IsRPProfileEditorOpen = false;

                    // Auto-open view window with new profile
                    plugin.RPProfileViewer.SetCharacter(character);
                    plugin.RPProfileViewer.IsOpen = true;
                }
                else
                {
                    // Capture position for Tutorial
                    plugin.SaveRPProfileButtonPos = ImGui.GetItemRectMin();
                    plugin.SaveRPProfileButtonSize = ImGui.GetItemRectSize();
                }

                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(buttonWidth, 35 * totalScale)))
                {
                    rp.Pronouns = originalPronouns;
                    rp.Gender = originalGender;
                    rp.Age = originalAge;
                    rp.Race = originalRace;
                    rp.Orientation = originalOrientation;
                    rp.Relationship = originalRelationship;
                    rp.Occupation = originalOccupation;
                    rp.Abilities = originalAbilities;
                    rp.Bio = originalBio;
                    profile.Tags = originalTags;
                    rp.BackgroundImage = originalBackgroundImage;
                    rp.Effects = originalEffects;
                    rp.ImageZoom = originalImageZoom;
                    rp.ImageOffset = originalImageOffset;
                    rp.CustomImagePath = originalCustomImagePath;
                    rp.Links = originalLinks;
                    rp.IsNSFW = originalIsNSFW;
                    rp.ProfileColor = originalProfileColor;

                    IsOpen = false;
                    plugin.IsRPProfileEditorOpen = false;
                }

                ImGui.PopStyleVar(2);
                ImGui.PopStyleColor(4);
            }
            finally
            {
                ImGui.PopStyleVar(6);
                ImGui.PopStyleColor(10);
            }
        }

        private void DrawCompactField(string label1, ref string value1, string label2, ref string value2, float scale)
        {
            if (!string.IsNullOrEmpty(label1))
            {
                ImGui.Text($"{label1}:");
                ImGui.SameLine(100 * scale);
                ImGui.SetNextItemWidth(140 * scale);
                ImGui.InputText($"##edit{label1}", ref value1, 100);
            }

            if (!string.IsNullOrEmpty(label2))
            {
                ImGui.SameLine(260 * scale);
                ImGui.Text($"{label2}:");
                ImGui.SameLine(340 * scale);
                ImGui.SetNextItemWidth(140 * scale);
                ImGui.InputText($"##edit{label2}", ref value2, 100);
            }
        }

        private string GetColorSchemeDisplayName(ParticleColorScheme scheme) => scheme switch
        {
            ParticleColorScheme.Auto => "Auto (Match Background)",
            ParticleColorScheme.Warm => "Warm (Orange/Gold)",
            ParticleColorScheme.Cool => "Cool (Blue/Teal)",
            ParticleColorScheme.Forest => "Forest (Green)",
            ParticleColorScheme.Magical => "Magical (Purple)",
            ParticleColorScheme.Winter => "Winter (White/Silver)",
            ParticleColorScheme.Custom => "Custom Colour",
            _ => scheme.ToString()
        };

        private string GetSharingDisplayName(ProfileSharing option) => option switch
        {
            ProfileSharing.NeverShare => "Private",
            ProfileSharing.AlwaysShare => "Direct Sharing",
            ProfileSharing.ShowcasePublic => "Public",
            _ => option.ToString(),
        };
        private float GetSafeScale(float baseScale)
        {
            return Math.Clamp(baseScale, 0.3f, 5.0f); // Prevent extreme scaling
        }
    }
}

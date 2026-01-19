using Dalamud.Interface.Windowing;
using Dalamud.Interface;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using System.IO;
using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Newtonsoft.Json;
using CharacterSelectPlugin.Windows.Components;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin.Windows
{
    public class RPProfileEditWindow : Window
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
        private string title = "";
        private string status = "";
        private int titleIcon = 0;
        private int statusIcon = 0;

        private DateTime lastUpdateTime = DateTime.MinValue;
        private const double UpdateDebounceMs = 50;

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
        private string? originalBannerImagePath = null;
        private string? originalBannerImageUrl = null;
        private float originalBannerZoom = 1.0f;
        private Vector2 originalBannerOffset = Vector2.Zero;
        private string originalLinks = "";
        private string originalTitle = "";
        private string originalStatus = "";
        private int originalTitleIcon = 0;
        private int originalStatusIcon = 0;

        private List<ContentBox> originalLeftContentBoxes = new();
        private List<ContentBox> originalRightContentBoxes = new();

        private List<string> availableBackgrounds = new();
        private string[] backgroundDisplayNames = Array.Empty<string>();
        private int selectedBackgroundIndex = 0;
        private bool isNSFW = false;
        private bool originalIsNSFW = false;

        private string erpBackgroundUrlInput = "";
        private string? originalBackgroundImageUrl = null;
        private float originalBackgroundImageOpacity = 1.0f;
        private float originalBackgroundImageZoom = 1.0f;
        private float originalBackgroundImageOffsetX = 0f;
        private float originalBackgroundImageOffsetY = 0f;
        private readonly HashSet<string> downloadingBackgrounds = new();

        private List<ContentBox> leftContentBoxes = new();
        private List<ContentBox> rightContentBoxes = new();

        private int selectedLeftLayout = 0;
        private int selectedRightLayout = 0;

        private int draggedItemIndex = -1;
        private string draggedItemSide = "";

        private string newImageUrl = "";
        private string newImageName = "";
        private int editingGalleryItem = -1;
        private string tempEditName = "";
        private string tempEditUrl = "";
        private float tempEditZoom = 1.0f;
        private Vector2 tempEditOffset = Vector2.Zero;
        private readonly HashSet<string> downloadingImages = new();

        private readonly HashSet<string> downloadingBanners = new();
        private string bannerUrlInput = "";

        private string originalEditName = "";
        private string originalEditUrl = "";
        private float originalEditZoom = 1.0f;
        private Vector2 originalEditOffset = Vector2.Zero;

        private bool shouldScrollToEdit = false;
        private bool shouldScrollToTop = false;

        private string lastEditUrl = "";

        public RPProfileEditWindow(Plugin plugin) : base("Enhanced Profile Editor", ImGuiWindowFlags.None)
        {
            this.plugin = plugin;
            IsOpen = false;
            LoadAvailableBackgrounds();

            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(1000, 700),
                MaximumSize = new Vector2(1400, 900)
            };

            InitializeContentBoxes();
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
                Plugin.Log.Error($"Failed to load backgrounds: {ex.Message}");
            }
        }

        private string GetCustomBackgroundName(string filename)
        {
            if (string.IsNullOrEmpty(filename)) return "None";
            return Path.GetFileNameWithoutExtension(filename);
        }

        private void InitializeContentBoxes()
        {
            if (character?.RPProfile?.LeftContentBoxes?.Count > 0)
            {
                // Deep clone to avoid modifying original data
                leftContentBoxes = character.RPProfile.LeftContentBoxes.Select(b => b.Clone()).ToList();
                foreach (var box in leftContentBoxes)
                {
                    if (box.LayoutType == ContentBoxLayoutType.Standard)
                    {
                        if (box.Type == ContentBoxType.LikesAndDislikes)
                        {
                            box.LayoutType = ContentBoxLayoutType.LikesDislikes;
                        }
                    }
                }
            }
            else
            {
                leftContentBoxes = new List<ContentBox>
                {
                    new ContentBox { Title = "Core Identity", Subtitle = "The foundation of who this character is", Content = "", Type = ContentBoxType.CoreIdentity },
                    new ContentBox { Title = "Combat Prowess", Subtitle = "Skills and techniques honed through experience", Content = "", Type = ContentBoxType.Combat },
                    new ContentBox { Title = "Background & Lore", Subtitle = "Where this character came from and what shaped them", Content = "", Type = ContentBoxType.Background },
                    new ContentBox { Title = "RP Hooks", Subtitle = "Ways to start a story with this character", Content = "", Type = ContentBoxType.RPHooks }
                };
            }

            if (character?.RPProfile?.RightContentBoxes?.Count > 0)
            {
                // Deep clone to avoid modifying original data
                rightContentBoxes = character.RPProfile.RightContentBoxes.Select(b => b.Clone()).ToList();
                foreach (var box in rightContentBoxes)
                {
                    if (box.LayoutType == ContentBoxLayoutType.Standard)
                    {
                        if (box.Type == ContentBoxType.LikesAndDislikes)
                        {
                            box.LayoutType = ContentBoxLayoutType.LikesDislikes;
                        }
                    }
                }
            }
            else
            {
                rightContentBoxes = new List<ContentBox>
                {
                    new ContentBox { Title = "Quick Info", Subtitle = "Basic character information", Content = "", Type = ContentBoxType.AdditionalDetails },
                    new ContentBox { Title = "Additional Details", Subtitle = "Extra information about this character", Content = "", Type = ContentBoxType.AdditionalDetails },
                    new ContentBox { Title = "Key Traits", Subtitle = "Notable characteristics and qualities", Content = "", Type = ContentBoxType.KeyTraits },
                    new ContentBox { Title = "Likes & Dislikes", Subtitle = "Preferences that define this character", Content = "", Likes = "", Dislikes = "", Type = ContentBoxType.LikesAndDislikes, LayoutType = ContentBoxLayoutType.LikesDislikes },
                    new ContentBox { Title = "External Links", Subtitle = "Social media, websites, and related content", Content = "", Type = ContentBoxType.ExternalLinks }
                };
            }
            
            MigrateLikesDislikesToRightSide();
        }

        private void MigrateLikesDislikesToRightSide()
        {
            var likesDislikesBoxes = leftContentBoxes.Where(box => 
                box.Type == ContentBoxType.LikesAndDislikes || 
                (box.LayoutType == ContentBoxLayoutType.LikesDislikes)).ToList();

            if (likesDislikesBoxes.Count > 0)
            {
                foreach (var box in likesDislikesBoxes)
                    leftContentBoxes.Remove(box);

                var existingRightLikesBox = rightContentBoxes.FirstOrDefault(box => 
                    box.Type == ContentBoxType.LikesAndDislikes || 
                    (box.LayoutType == ContentBoxLayoutType.LikesDislikes));

                if (existingRightLikesBox != null)
                {
                    var migratedBox = likesDislikesBoxes.First();
                    if (string.IsNullOrWhiteSpace(existingRightLikesBox.Likes) && !string.IsNullOrWhiteSpace(migratedBox.Likes))
                        existingRightLikesBox.Likes = migratedBox.Likes;
                    if (string.IsNullOrWhiteSpace(existingRightLikesBox.Dislikes) && !string.IsNullOrWhiteSpace(migratedBox.Dislikes))
                        existingRightLikesBox.Dislikes = migratedBox.Dislikes;
                    existingRightLikesBox.Type = ContentBoxType.LikesAndDislikes;
                    existingRightLikesBox.LayoutType = ContentBoxLayoutType.LikesDislikes;
                }
                else
                {
                    var migratedBox = likesDislikesBoxes.First();
                    migratedBox.Type = ContentBoxType.LikesAndDislikes;
                    migratedBox.LayoutType = ContentBoxLayoutType.LikesDislikes;
                    if (rightContentBoxes.Count > 2)
                        rightContentBoxes.Insert(2, migratedBox);
                    else
                        rightContentBoxes.Add(migratedBox);
                }

                if (character?.RPProfile != null)
                {
                    character.RPProfile.LeftContentBoxes = new List<ContentBox>(leftContentBoxes);
                    character.RPProfile.RightContentBoxes = new List<ContentBox>(rightContentBoxes);
                }
            }
        }

        public void SetCharacter(Character character)
        {
            this.character = character;
            profile = character.RPProfile ??= new RPProfile();

            var rp = character.RPProfile ??= new RPProfile();

            ContentBoxEditor.AvailableCharacterNames = plugin.Characters
                .Where(c => c.Name != character.Name)
                .Select(c => c.Name)
                .ToList();
            ContentBoxEditor.CharacterInGameNames = plugin.Characters
                .Where(c => c.Name != character.Name && !string.IsNullOrEmpty(c.LastInGameName))
                .ToDictionary(c => c.Name, c => c.LastInGameName!);

            InitializeContentBoxes();

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
            title = rp.Title ?? "";
            status = rp.Status ?? "";
            titleIcon = rp.TitleIcon;
            statusIcon = rp.StatusIcon;
            isNSFW = rp.IsNSFW;

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
            originalBackgroundImage = rp.BackgroundImage;
            originalBackgroundImageUrl = rp.BackgroundImageUrl;
            originalBackgroundImageOpacity = rp.BackgroundImageOpacity;
            originalBackgroundImageZoom = rp.BackgroundImageZoom;
            originalBackgroundImageOffsetX = rp.BackgroundImageOffsetX;
            originalBackgroundImageOffsetY = rp.BackgroundImageOffsetY;
            erpBackgroundUrlInput = rp.BackgroundImageUrl ?? "";
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
            originalBannerImagePath = rp.BannerImagePath;
            originalBannerImageUrl = rp.BannerImageUrl;
            bannerUrlInput = rp.BannerImageUrl ?? "";
            originalBannerZoom = rp.BannerZoom;
            originalBannerOffset = rp.BannerOffset;
            originalLinks = links;
            originalTitle = title;
            originalStatus = status;
            originalTitleIcon = titleIcon;
            originalStatusIcon = statusIcon;
            originalIsNSFW = isNSFW;
            originalProfileColor = rp.ProfileColor;

            selectedBackgroundIndex = 0;
            if (!string.IsNullOrEmpty(rp.BackgroundImage))
            {
                int index = availableBackgrounds.IndexOf(rp.BackgroundImage);
                if (index > 0) selectedBackgroundIndex = index;
            }

            LoadContentIntoBoxes(rp);

            originalLeftContentBoxes = leftContentBoxes.Select(box => new ContentBox
            {
                Title = box.Title,
                Subtitle = box.Subtitle,
                Content = box.Content,
                Likes = box.Likes,
                Dislikes = box.Dislikes,
                Type = box.Type,
                LayoutType = box.LayoutType,
                LeftColumn = box.LeftColumn ?? "",
                RightColumn = box.RightColumn ?? "",
                QuoteText = box.QuoteText ?? "",
                QuoteAuthor = box.QuoteAuthor ?? "",
                TimelineData = box.TimelineData ?? "",
                TaggedData = box.TaggedData ?? ""
            }).ToList();

            originalRightContentBoxes = rightContentBoxes.Select(box => new ContentBox
            {
                Title = box.Title,
                Subtitle = box.Subtitle,
                Content = box.Content,
                Likes = box.Likes,
                Dislikes = box.Dislikes,
                Type = box.Type,
                LayoutType = box.LayoutType,
                LeftColumn = box.LeftColumn ?? "",
                RightColumn = box.RightColumn ?? "",
                QuoteText = box.QuoteText ?? "",
                QuoteAuthor = box.QuoteAuthor ?? "",
                TimelineData = box.TimelineData ?? "",
                TaggedData = box.TaggedData ?? ""
            }).ToList();
        }

        private void LoadContentIntoBoxes(RPProfile rp)
        {
            var coreIdentityBox = leftContentBoxes.FirstOrDefault(b => b.Title == "Core Identity");
            if (coreIdentityBox != null)
            {
                coreIdentityBox.Content = rp.Bio ?? "";
            }

            var combatBox = leftContentBoxes.FirstOrDefault(b => b.Title == "Combat Prowess");
            if (combatBox != null)
            {
                combatBox.Content = rp.Abilities ?? "";
            }

            var backgroundBox = leftContentBoxes.FirstOrDefault(b => b.Title == "Background & Lore");
            if (backgroundBox != null)
            {
                backgroundBox.Content = rp.GalleryStatus ?? "";
            }

            var rpHooksBox = leftContentBoxes.FirstOrDefault(b => b.Title == "RP Hooks");
            if (rpHooksBox != null)
            {
                rpHooksBox.Content = rp.Tags ?? "";
            }

            var linksBox = rightContentBoxes.FirstOrDefault(b => b.Title == "External Links");
            if (linksBox != null)
            {
                linksBox.Content = rp.Links ?? "";
            }
        }

        public override void Draw()
        {
            var totalScale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);

            if (character == null)
            {
                ImGui.Text("No character selected for editing.");
                return;
            }

            int themeColorCount = ThemeHelper.PushThemeColors(plugin.Configuration);
            int themeStyleVarCount = ThemeHelper.PushThemeStyleVars(plugin.Configuration.UIScaleMultiplier);

            try
            {
                var rp = character.RPProfile ??= new RPProfile();

                var contentHeight = ImGui.GetContentRegionAvail().Y - (80 * totalScale);
                var availableWidth = ImGui.GetContentRegionAvail().X;
                var leftColumnWidth = availableWidth * 0.40f;
                var rightColumnWidth = availableWidth * 0.58f;

                ImGui.BeginChild("##LeftColumn", new Vector2(leftColumnWidth, contentHeight), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);
                DrawImageAndEffectsSection(rp, totalScale);
                ImGui.EndChild();

                ImGui.SameLine();

                ImGui.BeginChild("##RightColumn", new Vector2(rightColumnWidth, contentHeight), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);
                DrawContentEditingSection(rp, totalScale);
                ImGui.EndChild();

                ImGui.Separator();
                ImGui.Spacing();
                DrawSaveCancelButtons(totalScale);
            }
            finally
            {
                ThemeHelper.PopThemeStyleVars(themeStyleVarCount);
                ThemeHelper.PopThemeColors(themeColorCount);
            }
        }

        private void DrawImageAndEffectsSection(RPProfile rp, float totalScale)
        {
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

            string pluginDir = plugin.PluginDirectory;
            string fallback = Path.Combine(pluginDir, "Assets", "Default.png");
            string finalImagePath = !string.IsNullOrEmpty(rp.CustomImagePath) && File.Exists(rp.CustomImagePath)
                ? rp.CustomImagePath
                : (!string.IsNullOrEmpty(character.ImagePath) && File.Exists(character.ImagePath)
                    ? character.ImagePath
                    : fallback);

            Plugin.Log.Info($"[Profile Debug] FinalImagePath: '{finalImagePath}'");
            Plugin.Log.Info($"[Profile Debug] File exists: {File.Exists(finalImagePath)}");
            
            if (File.Exists(finalImagePath))
            {
                var texture = Plugin.TextureProvider.GetFromFile(finalImagePath).GetWrapOrDefault();
                Plugin.Log.Info($"[Profile Debug] Texture loaded: {texture != null}");
                if (texture != null)
                {
                    float frameSize = 140f * totalScale;
                    float zoom = Math.Clamp(rp.ImageZoom, 0.1f, 10.0f);
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

                    var cursor = ImGui.GetCursorScreenPos();
                    var drawList = ImGui.GetWindowDrawList();
                    drawList.AddRectFilled(cursor - new Vector2(2 * totalScale, 2 * totalScale), cursor + new Vector2(frameSize + (2 * totalScale)), ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.4f, 0.4f, 1f)), 4 * totalScale);

                    ImGui.BeginChild("ImageCropFrame", new Vector2(frameSize), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                    ImGui.SetCursorScreenPos(drawPos);
                    ImGui.Image((ImTextureID)texture.Handle, drawSize);
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

            ImGui.Text("Image Position:");
            Vector2 newOffset = rp.ImageOffset;
            ImGui.PushItemWidth(160 * totalScale);
            
            bool xOffsetChanged = ImGui.SliderFloat("X Offset", ref newOffset.X, -800f, 800f, "%.0f");
            bool xOffsetActive = ImGui.IsItemActive();
            if (xOffsetChanged)
            {
                rp.ImageOffset = newOffset;
                UpdateExpandedViewIfOpen();
            }
            if (!xOffsetActive && ImGui.IsItemDeactivatedAfterEdit())
                ForceUpdateExpandedView();
            
            bool yOffsetChanged = ImGui.SliderFloat("Y Offset", ref newOffset.Y, -800f, 800f, "%.0f");
            bool yOffsetActive = ImGui.IsItemActive();
            if (yOffsetChanged)
            {
                rp.ImageOffset = newOffset;
                UpdateExpandedViewIfOpen();
            }
            if (!yOffsetActive && ImGui.IsItemDeactivatedAfterEdit())
                ForceUpdateExpandedView();
            
            float newZoom = rp.ImageZoom;
            bool zoomChanged = ImGui.SliderFloat("Zoom Level", ref newZoom, 0.1f, 10.0f, "%.1fx");
            bool zoomActive = ImGui.IsItemActive();
            if (zoomChanged)
            {
                rp.ImageZoom = newZoom;
                UpdateExpandedViewIfOpen();
            }
            if (!zoomActive && ImGui.IsItemDeactivatedAfterEdit())
                ForceUpdateExpandedView();
            ImGui.PopItemWidth();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), "Banner Image");
            ImGui.Separator();

            ImGui.Text("Enter image URL:");
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "URL must point directly to the image file (e.g. for Imgur: right-click → 'Open image in new tab' → copy that URL).");
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 160 * totalScale);
            if (string.IsNullOrEmpty(bannerUrlInput) && !string.IsNullOrEmpty(rp.BannerImageUrl))
                bannerUrlInput = rp.BannerImageUrl;
            if (ImGui.InputTextWithHint("##BannerUrl", "https://example.com/banner.png", ref bannerUrlInput, 500))
            {
            }
            ImGui.SameLine();
            if (ImGui.Button("Apply", new Vector2(70 * totalScale, 0)) && !string.IsNullOrWhiteSpace(bannerUrlInput))
            {
                rp.BannerImageUrl = bannerUrlInput.Trim();
                rp.BannerImagePath = "";
                if (!downloadingBanners.Contains(rp.BannerImageUrl))
                {
                    Task.Run(async () => await DownloadBannerImageAsync(rp.BannerImageUrl));
                }
            }
            ImGui.SameLine();
            if (!string.IsNullOrEmpty(rp.BannerImageUrl) && ImGui.Button("Clear", new Vector2(70 * totalScale, 0)))
            {
                rp.BannerImageUrl = "";
                rp.BannerImagePath = "";
                bannerUrlInput = "";
            }

            string? bannerSource = null;

            if (!string.IsNullOrEmpty(rp.BannerImageUrl))
            {
                var cachedPath = GetBannerImagePath(rp.BannerImageUrl);
                if (File.Exists(cachedPath))
                    bannerSource = cachedPath;
                else if (!downloadingBanners.Contains(rp.BannerImageUrl))
                    Task.Run(async () => await DownloadBannerImageAsync(rp.BannerImageUrl));
            }
            else if (!string.IsNullOrEmpty(rp.BannerImagePath) && File.Exists(rp.BannerImagePath))
            {
                bannerSource = rp.BannerImagePath;
            }

            if (!string.IsNullOrEmpty(rp.BannerImageUrl))
            {
                if (downloadingBanners.Contains(rp.BannerImageUrl))
                    ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Downloading...");
                else if (!string.IsNullOrEmpty(bannerSource))
                    ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), "Banner loaded");
            }

            if (!string.IsNullOrEmpty(bannerSource))
            {
                try
                {
                    var bannerTexture = Plugin.TextureProvider.GetFromFile(bannerSource).GetWrapOrDefault();

                    if (bannerTexture != null)
                    {
                        float frameWidth = 300f * totalScale;
                        float frameHeight = 50f * totalScale;

                        float zoom = Math.Clamp(rp.BannerZoom, 0.1f, 8.0f);

                        float actualBannerWidth = 1200f * totalScale;
                        float offsetScaleRatio = frameWidth / actualBannerWidth;
                        Vector2 offset = rp.BannerOffset * totalScale * offsetScaleRatio;

                        float bannerAspect = (float)bannerTexture.Width / bannerTexture.Height;

                        float bannerDrawWidth = frameWidth * zoom;
                        float bannerDrawHeight = bannerDrawWidth / bannerAspect;

                        Vector2 bannerDrawSize = new Vector2(bannerDrawWidth, bannerDrawHeight);

                        Vector2 bannerRegionSize = new Vector2(frameWidth, frameHeight);
                        Vector2 centerOffset = (bannerRegionSize - bannerDrawSize) * 0.5f;

                        Vector2 drawPos = ImGui.GetCursorScreenPos() + centerOffset + offset;

                        var cursor = ImGui.GetCursorScreenPos();
                        var drawList = ImGui.GetWindowDrawList();
                        drawList.AddRectFilled(
                            cursor - new Vector2(2 * totalScale, 2 * totalScale),
                            cursor + new Vector2(frameWidth + (2 * totalScale), frameHeight + (2 * totalScale)),
                            ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.4f, 0.4f, 1f)),
                            4 * totalScale
                        );

                        ImGui.BeginChild("BannerCropFrame", new Vector2(frameWidth, frameHeight), false,
                                        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                        ImGui.SetCursorScreenPos(drawPos);
                        ImGui.Image((ImTextureID)bannerTexture.Handle, bannerDrawSize);
                        ImGui.EndChild();
                    }
                    else
                    {
                        ImGui.TextDisabled("⚠ Failed to load banner texture");
                    }
                }
                catch (Exception ex)
                {
                    ImGui.Text($"⚠ Banner error: {ex.Message}");
                }
            }
            else if (string.IsNullOrEmpty(rp.BannerImagePath) && string.IsNullOrEmpty(rp.BannerImageUrl))
            {
                ImGui.TextDisabled("No banner set");
            }

            ImGui.Spacing();

            if (!string.IsNullOrEmpty(bannerSource) || !string.IsNullOrEmpty(rp.BannerImageUrl) || !string.IsNullOrEmpty(rp.BannerImagePath))
            {
                ImGui.Text("Banner Position:");
                Vector2 newBannerOffset = rp.BannerOffset;
                ImGui.PushItemWidth(160 * totalScale);
                
                bool bannerXChanged = ImGui.SliderFloat("Banner X Offset", ref newBannerOffset.X, -800f, 800f, "%.0f");
                bool bannerXActive = ImGui.IsItemActive();
                if (bannerXChanged)
                {
                    rp.BannerOffset = newBannerOffset;
                    UpdateExpandedViewIfOpen();
                }
                if (!bannerXActive && ImGui.IsItemDeactivatedAfterEdit())
                    ForceUpdateExpandedView();
                
                bool bannerYChanged = ImGui.SliderFloat("Banner Y Offset", ref newBannerOffset.Y, -400f, 400f, "%.0f");
                bool bannerYActive = ImGui.IsItemActive();
                if (bannerYChanged)
                {
                    rp.BannerOffset = newBannerOffset;
                    UpdateExpandedViewIfOpen();
                }
                if (!bannerYActive && ImGui.IsItemDeactivatedAfterEdit())
                    ForceUpdateExpandedView();
                
                float newBannerZoom = rp.BannerZoom;
                bool bannerZoomChanged = ImGui.SliderFloat("Banner Zoom", ref newBannerZoom, 0.1f, 8.0f, "%.1fx");
                bool bannerZoomActive = ImGui.IsItemActive();
                if (bannerZoomChanged)
                {
                    rp.BannerZoom = newBannerZoom;
                    UpdateExpandedViewIfOpen();
                }
                if (!bannerZoomActive && ImGui.IsItemDeactivatedAfterEdit())
                    ForceUpdateExpandedView();

                ImGui.PopItemWidth();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), "Custom Background Image");
            ImGui.Separator();

            ImGui.Text("Enter image URL:");
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "URL must point directly to the image file. This overrides the preset backgrounds below.");

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 160 * totalScale);
            if (string.IsNullOrEmpty(erpBackgroundUrlInput) && !string.IsNullOrEmpty(rp.BackgroundImageUrl))
                erpBackgroundUrlInput = rp.BackgroundImageUrl;
            if (ImGui.InputTextWithHint("##ERPBackgroundUrl", "https://example.com/background.png", ref erpBackgroundUrlInput, 500))
            {
            }
            ImGui.SameLine();
            if (ImGui.Button("Apply##ERPBgUrl", new Vector2(70 * totalScale, 0)) && !string.IsNullOrWhiteSpace(erpBackgroundUrlInput))
            {
                rp.BackgroundImageUrl = erpBackgroundUrlInput.Trim();
                if (!downloadingBackgrounds.Contains(rp.BackgroundImageUrl))
                {
                    Task.Run(async () => await DownloadBackgroundImageAsync(rp.BackgroundImageUrl));
                }
                UpdateExpandedViewIfOpen();
            }
            ImGui.SameLine();
            if (!string.IsNullOrEmpty(rp.BackgroundImageUrl) && ImGui.Button("Clear##ERPBgUrl", new Vector2(70 * totalScale, 0)))
            {
                rp.BackgroundImageUrl = null;
                erpBackgroundUrlInput = "";
                UpdateExpandedViewIfOpen();
            }

            if (!string.IsNullOrEmpty(rp.BackgroundImageUrl))
            {
                if (downloadingBackgrounds.Contains(rp.BackgroundImageUrl))
                    ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Downloading...");
                else if (File.Exists(GetBackgroundImageCachePath(rp.BackgroundImageUrl)))
                    ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), "Background loaded");
                else
                    ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1f), "Failed to load");
            }

            if (!string.IsNullOrEmpty(rp.BackgroundImageUrl))
            {
                ImGui.Spacing();
                ImGui.Text("Background Adjustments:");
                ImGui.PushItemWidth(160 * totalScale);

                var opacity = rp.BackgroundImageOpacity;
                bool opacityChanged = ImGui.SliderFloat("Opacity##ERPBg", ref opacity, 0.3f, 1.0f, "%.1f");
                bool opacityActive = ImGui.IsItemActive();
                if (opacityChanged)
                {
                    rp.BackgroundImageOpacity = opacity;
                    UpdateExpandedViewIfOpen();
                }
                if (!opacityActive && ImGui.IsItemDeactivatedAfterEdit())
                    ForceUpdateExpandedView();

                var bgZoom = rp.BackgroundImageZoom;
                bool bgZoomChanged = ImGui.SliderFloat("Zoom##ERPBg", ref bgZoom, 0.5f, 3.0f, "%.1fx");
                bool bgZoomActive = ImGui.IsItemActive();
                if (bgZoomChanged)
                {
                    rp.BackgroundImageZoom = bgZoom;
                    UpdateExpandedViewIfOpen();
                }
                if (!bgZoomActive && ImGui.IsItemDeactivatedAfterEdit())
                    ForceUpdateExpandedView();

                var posX = rp.BackgroundImageOffsetX;
                bool posXChanged = ImGui.SliderFloat("X Offset##ERPBg", ref posX, -1.0f, 1.0f, "%.2f");
                bool posXActive = ImGui.IsItemActive();
                if (posXChanged)
                {
                    rp.BackgroundImageOffsetX = posX;
                    UpdateExpandedViewIfOpen();
                }
                if (!posXActive && ImGui.IsItemDeactivatedAfterEdit())
                    ForceUpdateExpandedView();

                var posY = rp.BackgroundImageOffsetY;
                bool posYChanged = ImGui.SliderFloat("Y Offset##ERPBg", ref posY, -1.0f, 1.0f, "%.2f");
                bool posYActive = ImGui.IsItemActive();
                if (posYChanged)
                {
                    rp.BackgroundImageOffsetY = posY;
                    UpdateExpandedViewIfOpen();
                }
                if (!posYActive && ImGui.IsItemDeactivatedAfterEdit())
                    ForceUpdateExpandedView();

                ImGui.PopItemWidth();

                if (ImGui.Button("Reset Position##ERPBg"))
                {
                    rp.BackgroundImageZoom = 1.0f;
                    rp.BackgroundImageOffsetX = 0f;
                    rp.BackgroundImageOffsetY = 0f;
                    UpdateExpandedViewIfOpen();
                }
            }

        }

        private void DrawPreviewSection(RPProfile rp, float totalScale)
        {
            ImGui.TextColored(new Vector4(1f, 0.75f, 0.4f, 1f), "Live Preview");
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextWrapped("Live preview of the profile will be shown here.");
        }

        private void DrawContentEditingSection(RPProfile rp, float totalScale)
        {
            ImGui.TextColored(new Vector4(0.7f, 1f, 0.4f, 1f), "Content Editing");
            ImGui.Separator();
            ImGui.Spacing();

            DrawBasicProfileFields(totalScale);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawContentBoxesEditor(totalScale);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawGalleryEditingSection(rp, totalScale);
        }

        private void DrawBasicProfileFields(float totalScale)
        {
            if (character?.RPProfile == null) return;
            var rp = character.RPProfile;

            ImGui.Text("Basic Information:");
            ImGui.Spacing();

            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.16f, 0.16f, 0.16f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.22f, 0.22f, 0.22f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.28f, 0.28f, 0.28f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f * totalScale);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6 * totalScale, 4 * totalScale));

            ImGui.Text("Pronouns:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##editPronouns", ref pronouns, 100))
            {
                rp.Pronouns = pronouns;
                UpdateExpandedViewIfOpen();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.7f, 0.8f, 0.9f, 1.0f), "Title & Status (displayed under name)");
            ImGui.Spacing();

            ImGui.Text("Title/Tagline:");
            ImGui.SetNextItemWidth(-50 * totalScale);
            if (ImGui.InputText("##editTitle", ref title, 200))
            {
                rp.Title = title;
                UpdateExpandedViewIfOpen();
            }
            ImGui.SameLine();
            DrawIconButton("##titleIconBtn", ref titleIcon, rp, true, totalScale);

            ImGui.Text("Status:");
            ImGui.SetNextItemWidth(-50 * totalScale);
            if (ImGui.InputText("##editStatus", ref status, 200))
            {
                rp.Status = status;
                UpdateExpandedViewIfOpen();
            }
            ImGui.SameLine();
            DrawIconButton("##statusIconBtn", ref statusIcon, rp, false, totalScale);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Race:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##editRace", ref race, 100))
            {
                rp.Race = race;
                UpdateExpandedViewIfOpen();
            }

            ImGui.Text("Gender:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##editGender", ref gender, 100))
            {
                rp.Gender = gender;
                UpdateExpandedViewIfOpen();
            }

            ImGui.Text("Age:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##editAge", ref age, 100))
            {
                rp.Age = age;
                UpdateExpandedViewIfOpen();
            }

            ImGui.Text("Occupation:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##editOccupation", ref occupation, 100))
            {
                rp.Occupation = occupation;
                UpdateExpandedViewIfOpen();
            }

            ImGui.Text("Orientation:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##editOrientation", ref orientation, 100))
            {
                rp.Orientation = orientation;
                UpdateExpandedViewIfOpen();
            }

            ImGui.Text("Relationship:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##editRelationship", ref relationship, 100))
            {
                rp.Relationship = relationship;
                UpdateExpandedViewIfOpen();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(4);
        }

        private void DrawIconButton(string id, ref int iconValue, RPProfile rp, bool isTitle, float totalScale)
        {
            var buttonSize = new Vector2(40 * totalScale, 22 * totalScale);

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f * totalScale);

            if (iconValue > 0)
            {
                ImGui.PushFont(UiBuilder.IconFont);
                if (ImGui.Button(((FontAwesomeIcon)iconValue).ToIconString() + id, buttonSize))
                {
                    ImGui.OpenPopup("IconPicker" + id);
                }
                ImGui.PopFont();
            }
            else
            {
                if (ImGui.Button("..." + id, buttonSize))
                {
                    ImGui.OpenPopup("IconPicker" + id);
                }
            }

            ImGui.PopStyleVar();

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Select icon (click to open picker)");
            }

            if (ImGui.BeginPopup("IconPicker" + id))
            {
                ImGui.Text("Select an icon:");
                ImGui.Separator();

                var suggestedIcons = new FontAwesomeIcon[]
                {
                    FontAwesomeIcon.None,
                    FontAwesomeIcon.Star, FontAwesomeIcon.Heart, FontAwesomeIcon.Crown,
                    FontAwesomeIcon.Gem, FontAwesomeIcon.Feather, FontAwesomeIcon.Fire,
                    FontAwesomeIcon.Bolt, FontAwesomeIcon.Moon, FontAwesomeIcon.Sun,
                    FontAwesomeIcon.Leaf, FontAwesomeIcon.Snowflake, FontAwesomeIcon.Music,
                    FontAwesomeIcon.Shield, FontAwesomeIcon.Skull, FontAwesomeIcon.Cross,
                    FontAwesomeIcon.Book, FontAwesomeIcon.Scroll, FontAwesomeIcon.Magic,
                    FontAwesomeIcon.Flask, FontAwesomeIcon.Dice, FontAwesomeIcon.Paw,
                    FontAwesomeIcon.Eye, FontAwesomeIcon.HandPaper, FontAwesomeIcon.Thumbtack,
                    FontAwesomeIcon.Check, FontAwesomeIcon.InfoCircle, FontAwesomeIcon.ExclamationTriangle,
                    FontAwesomeIcon.Comment, FontAwesomeIcon.Comments, FontAwesomeIcon.Envelope,
                    FontAwesomeIcon.User, FontAwesomeIcon.Users, FontAwesomeIcon.Male,
                    FontAwesomeIcon.Palette, FontAwesomeIcon.PaintBrush, FontAwesomeIcon.Pen,
                    FontAwesomeIcon.Camera, FontAwesomeIcon.Video, FontAwesomeIcon.Microphone,
                    FontAwesomeIcon.Guitar, FontAwesomeIcon.Headphones, FontAwesomeIcon.Mask,
                    FontAwesomeIcon.Dragon, FontAwesomeIcon.Ghost, FontAwesomeIcon.Cat,
                    FontAwesomeIcon.Coffee, FontAwesomeIcon.GlassMartini, FontAwesomeIcon.Utensils,
                    FontAwesomeIcon.Clock, FontAwesomeIcon.Hourglass, FontAwesomeIcon.Bell,
                    FontAwesomeIcon.Flag, FontAwesomeIcon.Map, FontAwesomeIcon.Compass,
                    FontAwesomeIcon.Gamepad, FontAwesomeIcon.Chess, FontAwesomeIcon.PuzzlePiece
                };

                ImGui.PushFont(UiBuilder.IconFont);
                int iconsPerRow = 10;
                for (int i = 0; i < suggestedIcons.Length; i++)
                {
                    var icon = suggestedIcons[i];
                    var isSelected = (int)icon == iconValue;

                    if (isSelected)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.5f, 0.7f, 1.0f));
                    }

                    string buttonLabel = icon == FontAwesomeIcon.None ? "X" : icon.ToIconString();
                    if (ImGui.Button(buttonLabel + "##icon" + i, new Vector2(28 * totalScale, 28 * totalScale)))
                    {
                        iconValue = (int)icon;
                        if (isTitle)
                        {
                            rp.TitleIcon = iconValue;
                        }
                        else
                        {
                            rp.StatusIcon = iconValue;
                        }
                        UpdateExpandedViewIfOpen();
                        ImGui.CloseCurrentPopup();
                    }

                    if (isSelected)
                    {
                        ImGui.PopStyleColor();
                    }

                    if (ImGui.IsItemHovered() && icon != FontAwesomeIcon.None)
                    {
                        ImGui.PopFont();
                        ImGui.SetTooltip(icon.ToString());
                        ImGui.PushFont(UiBuilder.IconFont);
                    }

                    if ((i + 1) % iconsPerRow != 0 && i < suggestedIcons.Length - 1)
                    {
                        ImGui.SameLine();
                    }
                }
                ImGui.PopFont();

                ImGui.EndPopup();
            }
        }

        private void DrawContentBoxesEditor(float totalScale)
        {
            ImGui.Text("Content Sections:");
            ImGui.Spacing();

            if (ImGui.BeginTabBar("ContentSides"))
            {
                if (ImGui.BeginTabItem("Main Content"))
                {
                    ImGui.TextDisabled("Left side of the expanded profile view");
                    DrawContentBoxList(leftContentBoxes, "Main", totalScale);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Sidebar"))
                {
                    ImGui.TextDisabled("Right sidebar of the expanded profile view");
                    DrawContentBoxList(rightContentBoxes, "Sidebar", totalScale);
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        private void DrawContentBoxList(List<ContentBox> boxes, string side, float totalScale)
        {
            float availableWidth = ImGui.GetContentRegionAvail().X;
            DrawAddNewSection(boxes, side, totalScale);
            
            ImGui.Separator();
            ImGui.Spacing();
            
            for (int i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                ImGui.PushID($"dragdrop_{side}_{i}");

                bool headerOpen = ImGui.CollapsingHeader($"{box.Title}###{side}_{i}");

                if (ImGui.BeginDragDropSource())
                {
                    draggedItemIndex = i;
                    draggedItemSide = side;
                    ImGui.SetDragDropPayload($"CONTENTBOX_{side.ToUpper()}", new byte[] { 1 });
                    ImGui.Text($"Moving: {box.Title}");
                    ImGui.EndDragDropSource();
                }

                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload($"CONTENTBOX_{side.ToUpper()}");
                    if (!payload.IsNull && draggedItemSide == side && draggedItemIndex >= 0)
                    {
                        int sourceIndex = draggedItemIndex;
                        if (sourceIndex != i && sourceIndex < boxes.Count)
                        {
                            var sourceBox = boxes[sourceIndex];
                            boxes.RemoveAt(sourceIndex);
                            int targetIndex = sourceIndex < i ? i - 1 : i;
                            boxes.Insert(targetIndex, sourceBox);
                            UpdateProfileViewRealTime();
                            draggedItemIndex = -1;
                            draggedItemSide = "";
                        }
                    }
                    ImGui.EndDragDropTarget();
                }

                if (headerOpen)
                {
                    ImGui.Indent();

                    if (ContentBoxEditor.DrawContentBoxEditor(box, availableWidth - 20 * totalScale, totalScale))
                        UpdateProfileViewRealTime();

                    ImGui.Spacing();
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.3f, 0.3f, 0.8f));
                    if (ImGui.Button($"Remove This Section##remove_{side}_{i}", new Vector2(150 * totalScale, 0)))
                    {
                        boxes.RemoveAt(i);
                        ImGui.PopStyleColor();
                        ImGui.Unindent();
                        break; // Exit the loop since we modified the list
                    }
                    ImGui.PopStyleColor();
                    
                    ImGui.Unindent();
                }
                
                ImGui.PopID(); // Close the PushID for drag and drop
                ImGui.Spacing();
            }
        }

        private void DrawAddNewSection(List<ContentBox> boxes, string side, float totalScale)
        {
            ImGui.Text("Add New Section:");

            string[] layoutDisplayNames;
            string[] layoutNames;
            ContentBoxLayoutType[] layoutTypes;
            int selectedLayoutIndex;
            
            if (side == "Main")
            {
                layoutDisplayNames = new string[] {
                    "Core Identity - Essential character information and defining characteristics",
                    "Combat Prowess - Fighting style, abilities, and battle expertise",
                    "Background & Lore - Character history, origins, and past events",
                    "RP Hooks - Conversation starters and plot hooks for roleplay",
                    "Timeline - Chronological events with dates",
                    "Key-Value Pairs - Structured information (Height: 5'10\")",
                    "Quote Style - Memorable sayings with attribution",
                    "Icon/Inventory - Visual grid of items and abilities",
                    "Strengths & Weaknesses - Character's positive and negative traits",
                    "Tagged Categories - Organized tags grouped by category"
                };
                layoutNames = new string[] {
                    "Core Identity",
                    "Combat Prowess",
                    "Background & Lore",
                    "RP Hooks",
                    "Timeline",
                    "Key-Value Pairs",
                    "Quote Style",
                    "Icon/Inventory",
                    "Strengths & Weaknesses",
                    "Tagged Categories"
                };
                layoutTypes = new ContentBoxLayoutType[] {
                    ContentBoxLayoutType.Standard,  // Core Identity
                    ContentBoxLayoutType.Standard,  // Combat Prowess
                    ContentBoxLayoutType.Standard,  // Background & Lore
                    ContentBoxLayoutType.Standard,  // RP Hooks
                    ContentBoxLayoutType.Timeline,
                    ContentBoxLayoutType.KeyValue,
                    ContentBoxLayoutType.Quote,
                    ContentBoxLayoutType.Grid,
                    ContentBoxLayoutType.ProsCons,
                    ContentBoxLayoutType.Tagged
                };
                selectedLayoutIndex = selectedLeftLayout;
            }
            else
            {
                layoutDisplayNames = new string[] {
                    "Quick Info - At-a-glance character facts and statistics",
                    "Additional Details - Extra information that doesn't fit elsewhere",
                    "Key Traits - Defining personality traits and characteristics",
                    "Likes & Dislikes - Things your character enjoys or avoids",
                    "External Links - Links to wikis, playlists, or character resources",
                    "Quick List - Bullet points for skills and quick facts",
                    "Connections - Relationships to your other characters"
                };
                layoutNames = new string[] {
                    "Quick Info",
                    "Additional Details",
                    "Key Traits",
                    "Likes & Dislikes",
                    "External Links",
                    "Quick List",
                    "Connections"
                };
                layoutTypes = new ContentBoxLayoutType[] {
                    ContentBoxLayoutType.Standard,  // Quick Info
                    ContentBoxLayoutType.Standard,  // Additional Details
                    ContentBoxLayoutType.Standard,  // Key Traits
                    ContentBoxLayoutType.LikesDislikes,
                    ContentBoxLayoutType.Standard,  // External Links
                    ContentBoxLayoutType.List,
                    ContentBoxLayoutType.Connections
                };
                selectedLayoutIndex = selectedRightLayout;
            }
            
            if (selectedLayoutIndex >= layoutDisplayNames.Length)
                selectedLayoutIndex = 0;
            
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo($"##newLayout{side}", ref selectedLayoutIndex, layoutDisplayNames, layoutDisplayNames.Length))
            {
                if (side == "Main")
                    selectedLeftLayout = selectedLayoutIndex;
                else
                    selectedRightLayout = selectedLayoutIndex;
            }
            
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.6f, 0.2f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.7f, 0.3f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.1f, 0.5f, 0.1f, 1.0f));
            
            if (ImGui.Button($"Add '{layoutNames[selectedLayoutIndex]}' Section##{side}"))
            {
                var selectedLayout = layoutTypes[selectedLayoutIndex];
                var selectedName = layoutNames[selectedLayoutIndex];
                
                var newBox = new ContentBox
                {
                    Title = selectedName,  // Use the display name as title
                    Subtitle = GetDefaultSubtitleForLayoutAndName(selectedLayout, selectedName),
                    Content = "",
                    Type = GetContentTypeForLayoutAndName(selectedLayout, selectedName),
                    LayoutType = selectedLayout,
                    Likes = "",
                    Dislikes = ""
                };
                
                boxes.Add(newBox);
                UpdateProfileViewRealTime();
            }
            
            ImGui.PopStyleColor(3);
        }

        private void DrawSaveCancelButtons(float totalScale)
        {
            var buttonWidth = 100 * totalScale;
            var spacing = 10 * totalScale;
            var totalButtonWidth = (buttonWidth * 2) + spacing;
            var availableSpace = ImGui.GetContentRegionAvail().X;
            var startPos = availableSpace - totalButtonWidth;

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + startPos);

            if (ImGui.Button("Save", new Vector2(buttonWidth, 35 * totalScale)))
            {
                SaveChanges();
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(buttonWidth, 35 * totalScale)))
            {
                CancelChanges();
            }
        }

        private void SaveChanges()
        {
            if (character?.RPProfile == null) return;

            var rp = character.RPProfile;

            rp.Pronouns = pronouns;
            rp.Race = race;
            rp.Gender = gender;
            rp.Age = age;
            rp.Occupation = occupation;
            rp.Orientation = orientation;
            rp.Relationship = relationship;
            rp.Title = title;
            rp.Status = status;
            rp.TitleIcon = titleIcon;
            rp.StatusIcon = statusIcon;
            rp.IsNSFW = isNSFW;

            var coreIdentityBox = leftContentBoxes.FirstOrDefault(b => b.Title == "Core Identity");
            if (coreIdentityBox != null)
            {
                rp.Bio = coreIdentityBox.Content;
            }

            var combatBox = leftContentBoxes.FirstOrDefault(b => b.Title == "Combat Prowess");
            if (combatBox != null)
            {
                rp.Abilities = combatBox.Content;
            }

            var backgroundBox = leftContentBoxes.FirstOrDefault(b => b.Title == "Background & Lore");
            if (backgroundBox != null)
            {
                rp.GalleryStatus = backgroundBox.Content; // Use GalleryStatus for background content
            }

            var rpHooksBox = leftContentBoxes.FirstOrDefault(b => b.Title == "RP Hooks");
            if (rpHooksBox != null)
            {
                rp.Tags = rpHooksBox.Content; // Use Tags for RP hooks
            }

            var linksBox = rightContentBoxes.FirstOrDefault(b => b.Title == "External Links");
            if (linksBox != null)
            {
                rp.Links = linksBox.Content;
            }

            rp.LeftContentBoxes = new List<ContentBox>(leftContentBoxes);
            rp.RightContentBoxes = new List<ContentBox>(rightContentBoxes);

            plugin.SaveConfiguration();

            if (Plugin.ClientState.LocalPlayer is { } player && player.HomeWorld.IsValid && character != null)
            {
                string localName = player.Name.TextValue;
                string worldName = player.HomeWorld.Value.Name.ToString();
                string fullKey = $"{localName}@{worldName}";

                var sharing = rp.Sharing;

                if (sharing != ProfileSharing.NeverShare && !string.IsNullOrWhiteSpace(character.LastInGameName))
                {
                    ProfileSharing effectiveSharing = sharing;
                    if (sharing == ProfileSharing.ShowcasePublic)
                    {
                        var userMain = plugin.Configuration.GalleryMainCharacter;
                        bool onMainCharacter = !string.IsNullOrEmpty(userMain) && fullKey == userMain;
                        effectiveSharing = onMainCharacter ? ProfileSharing.ShowcasePublic : ProfileSharing.AlwaysShare;
                    }

                    _ = Plugin.UploadProfileAsync(rp, character.LastInGameName, isCharacterApplication: false, sharingOverride: effectiveSharing);
                    Plugin.Log.Info($"[ExpandedProfile] ✅ Uploaded expanded profile for {character.Name} (effective sharing: {effectiveSharing})");
                }
                else
                {
                    Plugin.Log.Debug($"[ExpandedProfile] ⚠ Skipped upload for {character.Name} (NeverShare)");
                }
            }

            IsOpen = false;
            if (plugin.RPProfileViewer.IsOpen && character != null)
                plugin.RPProfileViewer.RefreshCharacterData(character);
        }

        private void CancelChanges()
        {
            if (character?.RPProfile != null)
            {
                var rp = character.RPProfile;
                rp.Pronouns = originalPronouns;
                rp.Gender = originalGender;
                rp.Age = originalAge;
                rp.Race = originalRace;
                rp.Orientation = originalOrientation;
                rp.Relationship = originalRelationship;
                rp.Occupation = originalOccupation;
                rp.Abilities = originalAbilities;
                rp.Bio = originalBio;
                rp.BackgroundImage = originalBackgroundImage;
                rp.BackgroundImageUrl = originalBackgroundImageUrl;
                rp.BackgroundImageOpacity = originalBackgroundImageOpacity;
                rp.BackgroundImageZoom = originalBackgroundImageZoom;
                rp.BackgroundImageOffsetX = originalBackgroundImageOffsetX;
                rp.BackgroundImageOffsetY = originalBackgroundImageOffsetY;
                rp.ImageZoom = originalImageZoom;
                rp.ImageOffset = originalImageOffset;
                rp.CustomImagePath = originalCustomImagePath;
                rp.BannerImagePath = originalBannerImagePath;
                rp.BannerImageUrl = originalBannerImageUrl;
                rp.BannerZoom = originalBannerZoom;
                rp.BannerOffset = originalBannerOffset;
                rp.Tags = originalTags;
                rp.Links = originalLinks;
                rp.Title = originalTitle;
                rp.Status = originalStatus;
                rp.TitleIcon = originalTitleIcon;
                rp.StatusIcon = originalStatusIcon;
                rp.IsNSFW = originalIsNSFW;
                rp.ProfileColor = originalProfileColor;
                rp.Effects.CircuitBoard = originalEffects.CircuitBoard;
                rp.Effects.Fireflies = originalEffects.Fireflies;
                rp.Effects.FallingLeaves = originalEffects.FallingLeaves;
                rp.Effects.Butterflies = originalEffects.Butterflies;
                rp.Effects.Bats = originalEffects.Bats;
                rp.Effects.Fire = originalEffects.Fire;
                rp.Effects.Smoke = originalEffects.Smoke;
                rp.Effects.ColorScheme = originalEffects.ColorScheme;
                rp.Effects.CustomParticleColor = originalEffects.CustomParticleColor;

                leftContentBoxes.Clear();
                leftContentBoxes.AddRange(originalLeftContentBoxes.Select(box => new ContentBox
                {
                    Title = box.Title,
                    Subtitle = box.Subtitle,
                    Content = box.Content,
                    Likes = box.Likes,
                    Dislikes = box.Dislikes,
                    Type = box.Type,
                    LayoutType = box.LayoutType,
                    LeftColumn = box.LeftColumn,
                    RightColumn = box.RightColumn,
                    QuoteText = box.QuoteText,
                    QuoteAuthor = box.QuoteAuthor,
                    TimelineData = box.TimelineData,
                    TaggedData = box.TaggedData
                }));
                
                rightContentBoxes.Clear();
                rightContentBoxes.AddRange(originalRightContentBoxes.Select(box => new ContentBox
                {
                    Title = box.Title,
                    Subtitle = box.Subtitle,
                    Content = box.Content,
                    Likes = box.Likes,
                    Dislikes = box.Dislikes,
                    Type = box.Type,
                    LayoutType = box.LayoutType,
                    LeftColumn = box.LeftColumn,
                    RightColumn = box.RightColumn,
                    QuoteText = box.QuoteText,
                    QuoteAuthor = box.QuoteAuthor,
                    TimelineData = box.TimelineData,
                    TaggedData = box.TaggedData
                }));

                rp.LeftContentBoxes = new List<ContentBox>(leftContentBoxes);
                rp.RightContentBoxes = new List<ContentBox>(rightContentBoxes);
            }

            IsOpen = false;
            if (plugin.RPProfileViewer.IsOpen && character != null)
            {
                plugin.RPProfileViewer.RefreshCharacterData(character);
            }
        }

        private void DrawGalleryEditingSection(RPProfile rp, float totalScale)
        {
            if (shouldScrollToTop)
            {
                ImGui.SetScrollY(0f);
                shouldScrollToTop = false;
            }

            ImGui.TextColored(new Vector4(0.9f, 0.7f, 1f, 1f), "Gallery Images");
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextWrapped("Add image URLs to display in your profile gallery. Maximum 20 images. URLs must point directly to the image file (e.g. for Imgur: right-click → 'Open image in new tab' → copy that URL).");
            ImGui.Spacing();

            rp.GalleryImages ??= new List<GalleryImage>();

            ImGui.Text("Add New Gallery Image:");

            ImGui.Text("Name:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200 * totalScale);
            ImGui.InputText("##newImageName", ref newImageName, 100);

            ImGui.Text("URL:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-50 * totalScale);
            ImGui.InputText("##newImageUrl", ref newImageUrl, 500);

            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.6f, 0.2f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.7f, 0.3f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.1f, 0.5f, 0.1f, 1.0f));
            
            if (ImGui.Button("Add##addGalleryImage"))
            {
                if (!string.IsNullOrWhiteSpace(newImageUrl) && rp.GalleryImages.Count < 20)
                {
                    var galleryImage = new GalleryImage 
                    { 
                        Url = newImageUrl.Trim(),
                        Name = string.IsNullOrWhiteSpace(newImageName) ? $"Image {rp.GalleryImages.Count + 1}" : newImageName.Trim()
                    };
                    rp.GalleryImages.Add(galleryImage);
                    newImageUrl = "";
                    newImageName = "";
                    UpdateExpandedViewIfOpen();
                }
            }

            ImGui.PopStyleColor(3);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text($"Gallery Management ({rp.GalleryImages.Count}/20):");
            ImGui.Spacing();

            if (rp.GalleryImages.Count == 0)
            {
                ImGui.TextDisabled("No gallery images added yet. Add some URLs above to get started!");
            }
            else
            {
                DrawGalleryManagementGrid(rp, totalScale);
            }
        }

        private void DrawGalleryManagementGrid(RPProfile rp, float totalScale)
        {
            DrawThumbnailGrid(rp, totalScale);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (editingGalleryItem >= 0 && editingGalleryItem < rp.GalleryImages.Count)
            {
                DrawEditPanel(rp, totalScale);
            }
        }
        
        private void DrawThumbnailGrid(RPProfile rp, float totalScale)
        {
            const int itemsPerRow = 4;
            float thumbnailSize = 100 * totalScale;
            float itemSpacing = 10 * totalScale;
            float itemWidth = thumbnailSize + itemSpacing;
            
            for (int i = 0; i < rp.GalleryImages.Count; i++)
            {
                var galleryImage = rp.GalleryImages[i];
                
                ImGui.PushID($"thumb_{i}");
                
                // Start new row if needed
                if (i > 0 && i % itemsPerRow != 0)
                    ImGui.SameLine(0, itemSpacing);
                
                // Thumbnail column with consistent height
                var startCursor = ImGui.GetCursorPos();
                ImGui.BeginGroup();
                
                // Image number - ensure consistent text alignment
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(new Vector4(0.7f, 0.8f, 1.0f, 1.0f), $"#{i + 1}");
                
                // Actual thumbnail with real image
                bool isEditing = editingGalleryItem == i;
                var imagePath = GetGalleryImagePath(galleryImage.Url);
                IDalamudTextureWrap? texture = null;
                
                if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                {
                    texture = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
                }
                else if (!string.IsNullOrEmpty(galleryImage.Url) && !downloadingImages.Contains(galleryImage.Url))
                {
                    // Start downloading the image if not already downloading
                    Task.Run(async () => await DownloadGalleryImageAsync(galleryImage.Url));
                }
                
                if (texture != null)
                {
                    // Draw actual image thumbnail
                    var cursorPos = ImGui.GetCursorScreenPos();
                    var drawList = ImGui.GetWindowDrawList();
                    
                    // Show thumbnails at default zoom/position for consistency
                    float aspectRatio = (float)texture.Width / texture.Height;
                    Vector2 imageSize;
                    if (aspectRatio > 1f)
                    {
                        // Image is wider - fit to height
                        imageSize.Y = thumbnailSize;
                        imageSize.X = imageSize.Y * aspectRatio;
                    }
                    else
                    {
                        // Image is taller or square - fit to width
                        imageSize.X = thumbnailSize;
                        imageSize.Y = imageSize.X / aspectRatio;
                    }
                    
                    // Center the image in the thumbnail
                    Vector2 imageOffset = new Vector2(
                        -(imageSize.X - thumbnailSize) * 0.5f,
                        -(imageSize.Y - thumbnailSize) * 0.5f
                    );
                    
                    // Background
                    uint bgColor = isEditing ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.5f, 0.8f, 1.0f)) :
                                              ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
                    drawList.AddRectFilled(cursorPos, cursorPos + new Vector2(thumbnailSize), bgColor);
                    
                    // Clip to thumbnail bounds
                    drawList.PushClipRect(cursorPos, cursorPos + new Vector2(thumbnailSize), true);
                    
                    // Draw image with zoom/offset applied
                    drawList.AddImage((ImTextureID)texture.Handle,
                                    cursorPos + imageOffset,
                                    cursorPos + imageOffset + imageSize);
                    
                    drawList.PopClipRect();
                    
                    // Invisible button for clicking
                    ImGui.SetCursorScreenPos(cursorPos);
                    if (ImGui.InvisibleButton($"thumb_{i}", new Vector2(thumbnailSize, thumbnailSize)))
                    {
                        StartEditingImage(galleryImage, i);
                    }
                    
                    // Tooltip for click to edit
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Click to edit this image");
                    }
                }
                else
                {
                    // Fallback to button if no image
                    ImGui.PushStyleColor(ImGuiCol.Button, isEditing ? 
                        new Vector4(0.3f, 0.5f, 0.8f, 1.0f) : 
                        new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
                    
                    if (ImGui.Button($"IMG##thumb_{i}", new Vector2(thumbnailSize, thumbnailSize)))
                    {
                        StartEditingImage(galleryImage, i);
                    }
                    
                    // Tooltip for click to edit
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Click to edit this image");
                    }
                    
                    ImGui.PopStyleColor();
                }
                
                // Image name (truncated if too long)
                string displayName = galleryImage.Name.Length > 12 ? 
                    galleryImage.Name.Substring(0, 9) + "..." : 
                    galleryImage.Name;
                ImGui.AlignTextToFramePadding();
                ImGui.Text(displayName);
                
                // Remove button
                if (ImGui.Button($"Remove##remove_{i}", new Vector2(thumbnailSize, 0)))
                {
                    rp.GalleryImages.RemoveAt(i);
                    if (editingGalleryItem == i)
                    {
                        editingGalleryItem = -1; // Stop editing if we removed the current item
                    }
                    else if (editingGalleryItem > i)
                    {
                        editingGalleryItem--; // Adjust index if we removed an item before the current
                    }
                    UpdateExpandedViewIfOpen();
                }
                
                // Ensure consistent group height
                ImGui.Dummy(new Vector2(0, 2 * totalScale));
                
                ImGui.EndGroup();
                
                ImGui.PopID();
            }
        }
        
        private void DrawEditPanel(RPProfile rp, float totalScale)
        {
            var galleryImage = rp.GalleryImages[editingGalleryItem];
            
            ImGui.TextColored(new Vector4(0.8f, 0.9f, 1.0f, 1.0f), $"Editing Image {editingGalleryItem + 1}: {galleryImage.Name}");
            ImGui.Separator();
            ImGui.Spacing();
            
            // Two columns: preview on left, controls on right
            float previewSize = 120 * totalScale;
            float controlsWidth = ImGui.GetContentRegionAvail().X - previewSize - 20 * totalScale;
            
            // Left: Preview thumbnail
            ImGui.BeginChild("##editPreview", new Vector2(previewSize, 240 * totalScale), true);
            ImGui.Text("Preview:");
            
            // Show actual image preview
            var imagePath = GetGalleryImagePath(galleryImage.Url);
            IDalamudTextureWrap? texture = null;
            
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                texture = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
            }
            else if (!string.IsNullOrEmpty(galleryImage.Url) && !downloadingImages.Contains(galleryImage.Url))
            {
                // Start downloading the image if not already downloading
                Task.Run(async () => await DownloadGalleryImageAsync(galleryImage.Url));
            }
            
            float previewThumbSize = 100 * totalScale;
            if (texture != null)
            {
                // Draw actual image preview with current zoom/offset
                var cursorPos = ImGui.GetCursorScreenPos();
                var drawList = ImGui.GetWindowDrawList();
                
                float zoom = Math.Clamp(galleryImage.Zoom, 0.1f, 10.0f);
                Vector2 userOffset = galleryImage.Offset; // Don't scale here
                
                float aspectRatio = (float)texture.Width / texture.Height;
                Vector2 imageSize;
                if (aspectRatio > 1f)
                {
                    imageSize.Y = previewThumbSize * zoom;
                    imageSize.X = imageSize.Y * aspectRatio;
                }
                else
                {
                    imageSize.X = previewThumbSize * zoom;
                    imageSize.Y = imageSize.X / aspectRatio;
                }
                
                Vector2 imageOffset = new Vector2(
                    -(imageSize.X - previewThumbSize) * 0.5f + userOffset.X,
                    -(imageSize.Y - previewThumbSize) * 0.5f + userOffset.Y
                );
                
                // Background
                drawList.AddRectFilled(cursorPos, cursorPos + new Vector2(previewThumbSize), 
                                     ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f)));
                
                // Clip to preview bounds
                drawList.PushClipRect(cursorPos, cursorPos + new Vector2(previewThumbSize), true);
                
                // Draw image with zoom/offset applied
                drawList.AddImage((ImTextureID)texture.Handle,
                                cursorPos + imageOffset,
                                cursorPos + imageOffset + imageSize);
                
                drawList.PopClipRect();
                
                // Advance cursor
                ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, previewThumbSize));
            }
            else
            {
                ImGui.Button($"IMG##editPreview", new Vector2(previewThumbSize, previewThumbSize));
            }
            
            ImGui.EndChild();
            
            ImGui.SameLine();
            
            // Right: Controls
            ImGui.BeginChild("##editControls", new Vector2(controlsWidth, 240 * totalScale), false);
            
            // Name input
            ImGui.Text("Name:");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##editName", ref tempEditName, 100);
            
            // URL input  
            ImGui.Text("URL:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##editUrl", ref tempEditUrl, 500))
            {
                // Auto-update: detect URL changes and trigger download
                if (tempEditUrl != lastEditUrl && !string.IsNullOrWhiteSpace(tempEditUrl))
                {
                    lastEditUrl = tempEditUrl;
                    // Start download for new URL
                    if (!downloadingImages.Contains(tempEditUrl))
                    {
                        Task.Run(async () => await DownloadGalleryImageAsync(tempEditUrl));
                    }
                }
            }
            
            // Zoom control with live preview
            ImGui.Text($"Zoom: {tempEditZoom:F1}x");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderFloat("##editZoom", ref tempEditZoom, 0.1f, 3.0f, ""))
            {
                // Apply live preview to the actual gallery image
                galleryImage.Zoom = tempEditZoom;
                UpdateExpandedViewIfOpen();
            }
            
            // Position controls with live preview  
            ImGui.Text("X Offset, Y Offset:");
            ImGui.SetNextItemWidth(-1);
            var tempOffset = new System.Numerics.Vector2(tempEditOffset.X, tempEditOffset.Y);
            if (ImGui.DragFloat2("##editOffset", ref tempOffset, 1.0f, -500f, 500f, "%.0f"))
            {
                tempEditOffset = new Vector2(tempOffset.X, tempOffset.Y);
                // Apply live preview to the actual gallery image
                galleryImage.Offset = tempEditOffset;
                UpdateExpandedViewIfOpen();
            }
            
            ImGui.EndChild();
            
            ImGui.Spacing();
            
            // Center the action buttons
            float buttonWidth = 80 * totalScale;
            float totalButtonWidth = (buttonWidth * 4) + (ImGui.GetStyle().ItemSpacing.X * 3); // 4 buttons with spacing
            float centerOffset = (ImGui.GetContentRegionAvail().X - totalButtonWidth) * 0.5f;
            if (centerOffset > 0)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + centerOffset);
            
            // Action buttons
            if (ImGui.Button("Save", new Vector2(buttonWidth, 0)))
            {
                SaveEditedImage(galleryImage);
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
            {
                CancelEditing();
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Reset", new Vector2(buttonWidth, 0)))
            {
                ResetEditedImage();
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Remove", new Vector2(buttonWidth, 0)))
            {
                RemoveEditedImage(rp);
            }
            
            // Handle auto-scroll to show these buttons after drawing them
            if (shouldScrollToEdit)
            {
                ImGui.SetScrollHereY(0.9f); // Scroll to show buttons at bottom
                shouldScrollToEdit = false;
            }
        }
        
        private void StartEditingImage(GalleryImage galleryImage, int index)
        {
            editingGalleryItem = index;
            
            // Store current values as temp values
            tempEditName = galleryImage.Name;
            tempEditUrl = galleryImage.Url;
            tempEditZoom = galleryImage.Zoom;
            tempEditOffset = galleryImage.Offset;
            
            // Store original values for Cancel functionality
            originalEditName = galleryImage.Name;
            originalEditUrl = galleryImage.Url;
            originalEditZoom = galleryImage.Zoom;
            originalEditOffset = galleryImage.Offset;
            
            // Set flag to scroll to edit controls
            shouldScrollToEdit = true;
            
            // Initialize auto-update tracking
            lastEditUrl = galleryImage.Url;
        }
        
        private void SaveEditedImage(GalleryImage galleryImage)
        {
            galleryImage.Name = tempEditName;
            galleryImage.Url = tempEditUrl;
            galleryImage.Zoom = tempEditZoom;
            galleryImage.Offset = tempEditOffset;
            
            editingGalleryItem = -1; // Stop editing
            UpdateExpandedViewIfOpen();
        }
        
        private void CancelEditing()
        {
            // Restore original values to the gallery image
            if (editingGalleryItem >= 0 && character?.RPProfile?.GalleryImages != null && editingGalleryItem < character.RPProfile.GalleryImages.Count)
            {
                var galleryImage = character.RPProfile.GalleryImages[editingGalleryItem];
                galleryImage.Name = originalEditName;
                galleryImage.Url = originalEditUrl;
                galleryImage.Zoom = originalEditZoom;
                galleryImage.Offset = originalEditOffset;
                UpdateExpandedViewIfOpen();
            }
            
            editingGalleryItem = -1; // Stop editing
            shouldScrollToTop = true; // Scroll back to top after saving
        }
        
        private void ResetEditedImage()
        {
            tempEditZoom = 1.0f;
            tempEditOffset = Vector2.Zero;
        }
        
        private void RemoveEditedImage(RPProfile rp)
        {
            if (editingGalleryItem >= 0 && editingGalleryItem < rp.GalleryImages.Count)
            {
                rp.GalleryImages.RemoveAt(editingGalleryItem);
                editingGalleryItem = -1; // Stop editing
                UpdateExpandedViewIfOpen();
            }
        }
        
        private string GetGalleryImagePath(string imageUrl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imageUrl))
                    return "";

                // Use similar logic to RPProfileViewWindow
                var uri = new Uri(imageUrl);
                var fileName = $"gallery_{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(imageUrl)).Replace('/', '_').Replace('+', '-')}";
                return Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), fileName);
            }
            catch
            {
                return "";
            }
        }

        private void UpdateRPProfileFromContentBox(ContentBox box)
        {
            if (character?.RPProfile == null) return;
            var rp = character.RPProfile;

            // Map content boxes to RPProfile fields in real-time
            switch (box.Title)
            {
                case "Core Identity":
                    rp.Bio = box.Content;
                    break;
                case "Combat Prowess":
                    rp.Abilities = box.Content;
                    break;
                case "Background & Lore":
                    rp.GalleryStatus = box.Content;
                    break;
                case "RP Hooks":
                    rp.Tags = box.Content;
                    break;
                case "External Links":
                    rp.Links = box.Content;
                    break;
                case "Likes & Dislikes":
                    // TODO: Add separate Likes and Dislikes properties to RPProfile
                    // For now, Likes & Dislikes don't map to existing fields to avoid conflicts
                    break;
            }
        }

        private void UpdateExpandedViewIfOpen()
        {
            // Debounce updates for smoother slider experience
            var now = DateTime.UtcNow;
            if ((now - lastUpdateTime).TotalMilliseconds < UpdateDebounceMs)
                return;

            // Update the view window if it's open to show real-time changes
            if (plugin?.RPProfileViewer != null && plugin.RPProfileViewer.IsOpen && character != null)
            {
                plugin.RPProfileViewer.RefreshCharacterData(character);
                lastUpdateTime = now;
            }
        }

        private void ForceUpdateExpandedView()
        {
            // Force immediate update without debouncing - for when user finishes editing
            if (plugin.RPProfileViewer.IsOpen && character != null)
            {
                plugin.RPProfileViewer.RefreshCharacterData(character);
                lastUpdateTime = DateTime.UtcNow;
            }
        }

        private async Task DownloadGalleryImageAsync(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return;

            var imagePath = GetGalleryImagePath(imageUrl);
            
            // Check again if file exists (it might have been downloaded by another thread)
            if (File.Exists(imagePath))
                return;

            // Use lock to prevent multiple downloads of the same URL
            lock (downloadingImages)
            {
                if (downloadingImages.Contains(imageUrl))
                    return; // Already being downloaded
                downloadingImages.Add(imageUrl);
            }

            try
            {
                // Double-check file doesn't exist after acquiring lock
                if (File.Exists(imagePath))
                    return;

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var response = await client.GetAsync(imageUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var imageBytes = await response.Content.ReadAsByteArrayAsync();
                    
                    // Write to temp file first, then move to avoid corruption
                    var tempPath = imagePath + ".tmp";
                    await File.WriteAllBytesAsync(tempPath, imageBytes);
                    File.Move(tempPath, imagePath);
                    
                    Plugin.Log.Info($"Downloaded gallery image: {imageUrl}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to download gallery image {imageUrl}: {ex.Message}");
            }
            finally
            {
                lock (downloadingImages)
                {
                    downloadingImages.Remove(imageUrl);
                }
            }
        }

        private void DrawLayoutSpecificFields(ContentBox box, string side, int i, float totalScale)
        {
            switch (box.LayoutType)
            {
                case ContentBoxLayoutType.ProsCons:
                    ImGui.Text("Strengths:");
                    string leftColumn = box.LeftColumn ?? "";
                    if (ImGui.InputTextMultiline($"##left_{side}_{i}", ref leftColumn, 1000, new Vector2(-1, 80 * totalScale)))
                    {
                        box.LeftColumn = leftColumn;
                    }
                    
                    ImGui.Text("Weaknesses:");
                    string rightColumn = box.RightColumn ?? "";
                    if (ImGui.InputTextMultiline($"##right_{side}_{i}", ref rightColumn, 1000, new Vector2(-1, 80 * totalScale)))
                    {
                        box.RightColumn = rightColumn;
                    }
                    break;

                case ContentBoxLayoutType.Quote:
                    ImGui.Text("Quote Text:");
                    string quoteText = box.QuoteText ?? "";
                    if (ImGui.InputTextMultiline($"##quote_{side}_{i}", ref quoteText, 500, new Vector2(-1, 80 * totalScale)))
                    {
                        box.QuoteText = quoteText;
                    }
                    
                    ImGui.Text("Attribution (Optional):");
                    string quoteAuthor = box.QuoteAuthor ?? "";
                    if (ImGui.InputText($"##author_{side}_{i}", ref quoteAuthor, 100))
                    {
                        box.QuoteAuthor = quoteAuthor;
                    }
                    break;

                case ContentBoxLayoutType.KeyValue:
                    ImGui.Text("Key-Value Pairs:");
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.8f, 1.0f));
                    ImGui.TextWrapped("Format: Key: Value (one per line)");
                    ImGui.PopStyleColor();
                    break;

                case ContentBoxLayoutType.List:
                    ImGui.Text("List Items:");
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.8f, 1.0f));
                    ImGui.TextWrapped("Enter one item per line. Bullet points will be added automatically.");
                    ImGui.PopStyleColor();
                    break;

                case ContentBoxLayoutType.Timeline:
                    ImGui.Text("Timeline Events:");
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.8f, 1.0f));
                    ImGui.TextWrapped("Format: Date — Event description (one per line)");
                    ImGui.PopStyleColor();
                    break;

                case ContentBoxLayoutType.Grid:
                    ImGui.Text("Grid Items:");
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.8f, 1.0f));
                    ImGui.TextWrapped("Enter items one per line. They will be arranged in columns automatically.");
                    ImGui.PopStyleColor();
                    break;

                case ContentBoxLayoutType.Tagged:
                    ImGui.Text("Tagged Content:");
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.8f, 1.0f));
                    ImGui.TextWrapped("Format: #Tag or [Category]: followed by content. Use double line breaks to separate sections.");
                    ImGui.PopStyleColor();
                    break;

                case ContentBoxLayoutType.LikesDislikes:
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.8f, 1.0f));
                    ImGui.TextWrapped("This layout uses the Likes and Dislikes fields above.");
                    ImGui.PopStyleColor();
                    break;

                case ContentBoxLayoutType.Standard:
                default:
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.8f, 1.0f));
                    ImGui.TextWrapped("Standard text layout using the Content field above.");
                    ImGui.PopStyleColor();
                    break;
            }
        }

        private float GetSafeScale(float scale)
        {
            return Math.Max(0.1f, Math.Min(scale, 3.0f));
        }
        
        private string GetDefaultTitleForLayout(ContentBoxLayoutType layout)
        {
            return layout switch
            {
                ContentBoxLayoutType.Standard => "New Section",
                ContentBoxLayoutType.LikesDislikes => "Likes & Dislikes",
                ContentBoxLayoutType.List => "Quick List",
                ContentBoxLayoutType.KeyValue => "Key-Value Pairs",
                ContentBoxLayoutType.Quote => "Quote Style",
                ContentBoxLayoutType.Timeline => "Timeline",
                ContentBoxLayoutType.Grid => "Icon/Inventory",
                ContentBoxLayoutType.ProsCons => "Strengths & Weaknesses",
                ContentBoxLayoutType.Tagged => "Tagged Categories",
                ContentBoxLayoutType.Connections => "Connections",
                _ => "New Section"
            };
        }
        
        private string GetDefaultSubtitleForLayout(ContentBoxLayoutType layout)
        {
            return layout switch
            {
                ContentBoxLayoutType.Standard => "Description",
                ContentBoxLayoutType.LikesDislikes => "Personal preferences and aversions",
                ContentBoxLayoutType.List => "Traits, skills, or quick facts",
                ContentBoxLayoutType.KeyValue => "Key information pairs",
                ContentBoxLayoutType.Quote => "A significant quote or saying",
                ContentBoxLayoutType.Timeline => "Important events and milestones",
                ContentBoxLayoutType.Grid => "Inventory, skills, or abilities",
                ContentBoxLayoutType.ProsCons => "Strengths and weaknesses",
                ContentBoxLayoutType.Tagged => "Categorized and tagged information",
                ContentBoxLayoutType.Connections => "Character relationships and connections",
                _ => "Description"
            };
        }
        
        private string GetDefaultSubtitleForLayoutAndName(ContentBoxLayoutType layout, string name)
        {
            return name switch
            {
                "Core Identity" => "The foundation of who this character is",
                "Combat Prowess" => "Skills and techniques honed through experience",
                "Background & Lore" => "Where this character came from and what shaped them",
                "RP Hooks" => "Ways to start a story with this character",
                "Quick Info" => "Basic character information",
                "Additional Details" => "Extra information about this character",
                "Key Traits" => "Notable characteristics and qualities",
                "External Links" => "Social media, websites, and related content",
                "Connections" => "Character relationships and connections",
                _ => GetDefaultSubtitleForLayout(layout)
            };
        }
        
        private ContentBoxType GetContentTypeForLayout(ContentBoxLayoutType layout)
        {
            return layout switch
            {
                ContentBoxLayoutType.LikesDislikes => ContentBoxType.LikesAndDislikes,
                _ => ContentBoxType.Custom
            };
        }
        
        private ContentBoxType GetContentTypeForLayoutAndName(ContentBoxLayoutType layout, string name)
        {
            return name switch
            {
                "Core Identity" => ContentBoxType.CoreIdentity,
                "Combat Prowess" => ContentBoxType.Combat,
                "Background & Lore" => ContentBoxType.Background,
                "RP Hooks" => ContentBoxType.RPHooks,
                "Key Traits" => ContentBoxType.KeyTraits,
                "Additional Details" => ContentBoxType.AdditionalDetails,
                "External Links" => ContentBoxType.ExternalLinks,
                "Likes & Dislikes" => ContentBoxType.LikesAndDislikes,
                "Connections" => ContentBoxType.Connections,
                _ => ContentBoxType.Custom
            };
        }
        
        private string GetContentTypeTooltip(string contentType, bool isMainContent)
        {
            return contentType switch
            {
                // Main content types
                "Core Identity" => "Essential character information like name, race, job, and defining characteristics",
                "Combat Prowess" => "Fighting style, abilities, strengths and weaknesses in battle",
                "Background & Lore" => "Character history, origins, and important past events",
                "RP Hooks" => "Conversation starters and plot hooks for roleplay interactions",
                "Timeline" => "Chronological events in your character's life with dates",
                "Key-Value Pairs" => "Structured information in label/value format (e.g., Height: 5'10\")",
                "Quote Style" => "Memorable quotes or sayings with attribution",
                "Icon/Inventory" => "Visual grid of items, abilities, or icons your character possesses",
                "Strengths & Weaknesses" => "Two-column comparison of character pros and cons",
                "Tagged Categories" => "Organized tags grouped by category (e.g., Skills: Swordplay, Magic, Cooking)",
                
                // Sidebar content types
                "Quick Info" => "At-a-glance character facts and statistics",
                "Additional Details" => "Extra information that doesn't fit elsewhere",
                "Key Traits" => "Defining personality traits and characteristics",
                "Likes & Dislikes" => "Things your character enjoys or avoids",
                "External Links" => "Links to wikis, playlists, or other character resources",
                "Quick List" => "Bullet points for skills, abilities, or quick facts",
                "Connections" => "Relationships to your other CS+ characters",

                _ => "Custom content section"
            };
        }
        
        private void UpdateProfileViewRealTime()
        {
            if (character?.RPProfile == null) return;

            // Update the character's RPProfile with current content box data
            character.RPProfile.LeftContentBoxes = new List<ContentBox>(leftContentBoxes);
            character.RPProfile.RightContentBoxes = new List<ContentBox>(rightContentBoxes);

            // Refresh the profile view window if it's open
            if (plugin.RPProfileViewer.IsOpen)
            {
                plugin.RPProfileViewer.RefreshCharacterData(character);
            }
        }

        // Banner URL support methods
        private string GetBannerImagePath(string imageUrl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imageUrl))
                    return "";

                var fileName = $"banner_{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(imageUrl)).Replace('/', '_').Replace('+', '-')}";
                return Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), fileName);
            }
            catch
            {
                return "";
            }
        }

        private async Task DownloadBannerImageAsync(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return;

            var imagePath = GetBannerImagePath(imageUrl);

            if (File.Exists(imagePath))
                return;

            lock (downloadingBanners)
            {
                if (downloadingBanners.Contains(imageUrl))
                    return;
                downloadingBanners.Add(imageUrl);
            }

            try
            {
                if (File.Exists(imagePath))
                    return;

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                var response = await client.GetAsync(imageUrl);

                if (response.IsSuccessStatusCode)
                {
                    var imageBytes = await response.Content.ReadAsByteArrayAsync();
                    var tempPath = imagePath + ".tmp";
                    await File.WriteAllBytesAsync(tempPath, imageBytes);
                    File.Move(tempPath, imagePath);
                    Plugin.Log.Info($"Downloaded banner image: {imageUrl}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to download banner image {imageUrl}: {ex.Message}");
            }
            finally
            {
                lock (downloadingBanners)
                {
                    downloadingBanners.Remove(imageUrl);
                }
            }
        }

        private string GetBackgroundImageCachePath(string imageUrl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imageUrl))
                    return "";

                var fileName = $"erpbg_{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(imageUrl)).Replace('/', '_').Replace('+', '-')}";
                return Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), fileName);
            }
            catch
            {
                return "";
            }
        }

        private async Task DownloadBackgroundImageAsync(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return;

            var imagePath = GetBackgroundImageCachePath(imageUrl);

            if (File.Exists(imagePath))
                return;

            lock (downloadingBackgrounds)
            {
                if (downloadingBackgrounds.Contains(imageUrl))
                    return;
                downloadingBackgrounds.Add(imageUrl);
            }

            try
            {
                if (File.Exists(imagePath))
                    return;

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                var response = await client.GetAsync(imageUrl);

                if (response.IsSuccessStatusCode)
                {
                    var imageBytes = await response.Content.ReadAsByteArrayAsync();
                    var tempPath = imagePath + ".tmp";
                    await File.WriteAllBytesAsync(tempPath, imageBytes);
                    File.Move(tempPath, imagePath);
                    Plugin.Log.Info($"Downloaded ERP background image: {imageUrl}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to download ERP background image {imageUrl}: {ex.Message}");
            }
            finally
            {
                lock (downloadingBackgrounds)
                {
                    downloadingBackgrounds.Remove(imageUrl);
                }
            }
        }

    }

}
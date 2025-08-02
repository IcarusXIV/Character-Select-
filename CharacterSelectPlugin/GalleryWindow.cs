using Dalamud.Interface.Windowing;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json;
using System.IO;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using System.Threading;
using CharacterSelectPlugin.Effects;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Dalamud.Plugin.Services;
using System.Text;
using Dalamud.Game.Gui;

namespace CharacterSelectPlugin.Windows
{
    // Data classes
    public class GalleryProfile
    {
        public string CharacterId { get; set; } = "";
        public string CharacterName { get; set; } = "";
        public string Server { get; set; } = "";
        public string? ProfileImageUrl { get; set; }
        public string Tags { get; set; } = "";
        public string Bio { get; set; } = "";
        public string Race { get; set; } = "";
        public string Pronouns { get; set; } = "";
        public int LikeCount { get; set; }
        public string LastUpdated { get; set; } = "";
        public float ImageZoom { get; set; } = 1.0f;
        public Vector2 ImageOffset { get; set; } = Vector2.Zero;
        public string GalleryStatus { get; set; } = "";
        public bool IsNSFW { get; set; } = false;
    }

    public class FavoriteSnapshot
    {
        public string CharacterId { get; set; } = "";
        public string CharacterName { get; set; } = "";
        public string Server { get; set; } = "";
        public string? ProfileImageUrl { get; set; }
        public string Tags { get; set; } = "";
        public string Bio { get; set; } = "";
        public string Race { get; set; } = "";
        public string Pronouns { get; set; } = "";
        public DateTime FavoritedAt { get; set; } = DateTime.Now;
        public float ImageZoom { get; set; }
        public Vector2 ImageOffset { get; set; }
        public string OwnerCharacterKey { get; set; } = "";
        public string LocalImagePath { get; set; } = "";
    }

    public class LikeResponse
    {
        public int LikeCount { get; set; }
    }

    public enum GallerySortType
    {
        Popular,
        Recent,
        Alphabetical
    }

    public class Announcement
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string Type { get; set; } = "info"; // info, warning, update, maintenance
        public DateTime CreatedAt { get; set; }
        public bool Active { get; set; } = true;
    }

    public class ReportRequest
    {
        public string ReportedCharacterId { get; set; } = "";
        public string ReportedCharacterName { get; set; } = "";
        public string ReporterCharacter { get; set; } = "";
        public string Reason { get; set; } = "";
        public string Details { get; set; } = "";
    }
    public enum ReportReason
    {
        InappropriateContent,
        Spam,
        MaliciousLinks,
        Other
    }
    public class GalleryWindow : Window
    {
        private readonly Plugin plugin;
        private enum GalleryTab { Gallery, Friends, Favourites, Blocked, Announcements, Settings }
        private GalleryTab currentTab = GalleryTab.Gallery;

        // Gallery data
        private List<GalleryProfile> allProfiles = new();
        private List<GalleryProfile> filteredProfiles = new();
        private bool isLoading = false;
        private string searchFilter = "";
        private List<string> popularTags = new();
        private GallerySortType sortType = GallerySortType.Popular;
        private List<FavoriteSnapshot> favoriteSnapshots = new();
        private Dictionary<string, Vector3> cachedCharacterColors = new();
        private HashSet<string> profileLoadingStarted = new();
        private DateTime lastCacheCleanup = DateTime.MinValue;
        private readonly TimeSpan cacheCleanupInterval = TimeSpan.FromHours(1);
        private readonly TimeSpan maxImageAge = TimeSpan.FromDays(7);
        private readonly long maxCacheSizeBytes = 500 * 1024 * 1024;
        private static readonly Dictionary<string, DateTime> LastRequestTimes = new();
        private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromSeconds(1);
        private Dictionary<string, LikeSparkEffect> galleryLikeEffects = new();
        private Dictionary<string, FavoriteSparkEffect> galleryFavoriteEffects = new();
        private List<string> friendsInGallery = new();
        private DateTime lastFriendsUpdate = DateTime.MinValue;
        private readonly TimeSpan friendsUpdateInterval = TimeSpan.FromMinutes(1);
        private bool showAddedFriends = false;
        private GalleryTab lastActiveTab = GalleryTab.Gallery;
        private readonly Dictionary<string, IDalamudTextureWrap?> textureCache = new();
        private DateTime lastTextureCacheCleanup = DateTime.MinValue;
        private readonly Dictionary<string, string?> imagePathCache = new();
        private bool shouldResetScroll = false;
        private List<Announcement> announcements = new();
        private DateTime lastAnnouncementUpdate = DateTime.MinValue;
        private readonly TimeSpan announcementUpdateInterval = TimeSpan.FromMinutes(5);
        private DateTime lastSeenAnnouncements = DateTime.MinValue;
        private string lastErrorMessage = "";
        private DateTime lastErrorTime = DateTime.MinValue;

        // Report dialog state
        private bool showReportDialog = false;
        private string reportTargetCharacterId = "";
        private string reportTargetCharacterName = "";
        private ReportReason selectedReportReason = ReportReason.InappropriateContent;
        private string customReportReason = "";
        private string reportDetails = "";
        private bool showReportConfirmation = false;
        private string reportConfirmationMessage = "";
        private void ClearPerformanceCaches()
        {
            cachedCharacterColors.Clear();
            profileLoadingStarted.Clear();
            imageLoadStarted.Clear();
            loadingProfiles.Clear();
        }

        private const int PROFILES_PER_PAGE = 20;
        private int currentPage = 0;
        private readonly Dictionary<string, bool> imageLoadStarted = new();
        private readonly HashSet<string> loadingProfiles = new();
        private DateTime lastAutoRefresh = DateTime.MinValue;
        private readonly TimeSpan autoRefreshInterval = TimeSpan.FromMinutes(5);
        private bool wasWindowFocused = false;
        private HashSet<string> blockedProfiles = new();
        private string addFriendName = "";
        private string addFriendServer = "";
        private List<string> cachedMutualFriends = new();
        private DateTime lastMutualFriendsUpdate = DateTime.MinValue;
        private readonly TimeSpan mutualFriendsUpdateInterval = TimeSpan.FromMinutes(2);

        // UI state
        private float scrollPosition = 0f;
        private Dictionary<string, bool> likedProfiles = new();
        private Dictionary<string, RPProfile> downloadedProfiles = new();
        private Character? lastActiveCharacter = null;
        private HashSet<string> currentFavoritedProfiles = new();
        private bool pendingStateUpdate = false;
        private Dictionary<string, bool> frozenLikedProfiles = new();
        private HashSet<string> frozenFavoritedProfiles = new();
        private bool useStateFreeze = false;
        private DateTime lastSuccessfulRefresh = DateTime.MinValue;
        private bool isAutoRefreshing = false;
        private readonly Dictionary<string, IDalamudTextureWrap?> galleryTextureCache = new();
        private void EnsureFavoritesFilteredByCSCharacter()
        {
            var ownerKey = GetActiveCharacter()?.Name;
            if (string.IsNullOrEmpty(ownerKey))
            {
                currentFavoritedProfiles.Clear();
                return;
            }

            currentFavoritedProfiles.Clear();

            foreach (var favorite in favoriteSnapshots)
            {
                if (favorite.OwnerCharacterKey == ownerKey)
                {
                    var profileKey = GetProfileKey(favorite);
                    currentFavoritedProfiles.Add(profileKey);
                    Plugin.Log.Debug($"[Gallery] Restored favourites");
                }
            }

            Plugin.Log.Debug($"[Gallery] Restored {currentFavoritedProfiles.Count} favourites for {ownerKey}");
        }
        // TOS Modal state
        private const int CURRENT_TOS_VERSION = 1;
        private bool showTOSModal = false;
        private bool hasAcceptedCurrentTOS = false;

        // Image preview
        private string? imagePreviewUrl = null;
        private bool showImagePreview = false;
        private const float RpProfileFrameSize = 140f;

        private string GetProfileKey(string? id, string name, string server)
            => !string.IsNullOrWhiteSpace(id) ? id! : $"{name}@{server}";

        private string GetProfileKey(GalleryProfile p)
            => GetProfileKey(p.CharacterId, p.CharacterName, p.Server);

        private string GetProfileKey(FavoriteSnapshot f)
            => GetProfileKey(f.CharacterId, f.CharacterName, f.Server);

        private IDalamudTextureWrap? TryGetDownloadedTexture(string? imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl))
                return null;

            var local = GetDownloadedImagePath(imageUrl);
            if (!string.IsNullOrEmpty(local) && File.Exists(local))
                return Plugin.TextureProvider.GetFromFile(local).GetWrapOrDefault();

            return null;
        }

        public GalleryWindow(Plugin plugin) : base("Character Select+ Gallery", ImGuiWindowFlags.None)
        {
            this.plugin = plugin;
            IsOpen = false;

            var dpiScale = ImGui.GetIO().DisplayFramebufferScale.X;
            var uiScale = plugin.Configuration.UIScaleMultiplier;
            var totalScale = GetSafeScale(dpiScale * uiScale);

            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(735 * totalScale, 500 * totalScale),
                MaximumSize = new Vector2(1200 * totalScale, 900 * totalScale)
            };

            LoadFavorites();
            LoadBlockedProfiles();

            likedProfiles = new Dictionary<string, bool>();
            var savedLikes = plugin.Configuration.LikedGalleryProfiles ?? new HashSet<string>();
            foreach (var likeKey in savedLikes)
            {
                likedProfiles[likeKey] = true;
            }
        }

        public override void Draw()
        {
            var dpiScale = ImGui.GetIO().DisplayFramebufferScale.X;
            var uiScale = plugin.Configuration.UIScaleMultiplier;
            var totalScale = GetSafeScale(dpiScale * uiScale);

            // Main window dark styling
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.06f, 0.06f, 0.06f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.25f, 0.25f, 0.25f, 0.6f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.92f, 0.92f, 0.92f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(0.5f, 0.5f, 0.5f, 0.8f));

            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10.0f * totalScale);
            ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 6.0f * totalScale);

            try
            {
                // **NEW: Check if TOS modal should be shown**
                if (showTOSModal)
                {
                    DrawTOSModal(totalScale);
                    return; // Don't draw the rest of the gallery
                }

                // **EXISTING: All your current Draw() method content below this line**
                bool isWindowFocused = ImGui.IsWindowFocused();

                // Deadlock detection and recovery
                var timeSinceRefreshStarted = DateTime.Now - lastAutoRefresh;
                if (isAutoRefreshing && timeSinceRefreshStarted.TotalSeconds > 30)
                {
                    Plugin.Log.Warning("[Gallery] Auto-refresh appears stuck, resetting flag");
                    isAutoRefreshing = false;
                }

                // Auto-refresh logic
                if (IsOpen && !isAutoRefreshing &&
                    DateTime.Now - lastAutoRefresh > autoRefreshInterval &&
                    lastSuccessfulRefresh != DateTime.MinValue)
                {
                    if (DateTime.Now - lastSuccessfulRefresh > TimeSpan.FromSeconds(10))
                    {
                        Plugin.Log.Debug($"[Gallery] Starting auto-refresh (last attempt: {(DateTime.Now - lastAutoRefresh).TotalMinutes:F1}m ago)");

                        isAutoRefreshing = true;
                        lastAutoRefresh = DateTime.Now;

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await RefreshLikeCountsOnlyFixed();
                            }
                            catch (Exception ex)
                            {
                                Plugin.Log.Error($"[Gallery] Auto-refresh task failed: {ex.Message}");
                            }
                            finally
                            {
                                isAutoRefreshing = false;
                                Plugin.Log.Debug("[Gallery] Auto-refresh task completed, flag reset");
                            }
                        });
                    }
                }

                // Focus change handling
                if (isWindowFocused && !wasWindowFocused)
                {
                    var timeSinceLastRefresh = DateTime.Now - lastAutoRefresh;
                    if (timeSinceLastRefresh > TimeSpan.FromMinutes(1))
                    {
                        if (!isAutoRefreshing)
                        {
                            Plugin.Log.Debug("[Gallery] Focus-triggered refresh starting");
                            isAutoRefreshing = true;
                            lastAutoRefresh = DateTime.Now;

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await RefreshLikeCountsOnlyFixed();
                                }
                                catch (Exception ex)
                                {
                                    Plugin.Log.Error($"[Gallery] Focus refresh failed: {ex.Message}");
                                }
                                finally
                                {
                                    isAutoRefreshing = false;
                                    Plugin.Log.Debug("[Gallery] Focus refresh completed, flag reset");
                                }
                            });
                        }
                        Plugin.Log.Debug("[Gallery] Auto-refreshed on window focus");
                    }
                }

                wasWindowFocused = isWindowFocused;

                // Cache cleanup
                if (DateTime.Now - lastCacheCleanup > cacheCleanupInterval)
                {
                    Task.Run(() => CleanupImageCache());
                    lastCacheCleanup = DateTime.Now;
                }

                // Character change detection
                if (HasCharacterChanged())
                {
                    Plugin.Log.Info("[Gallery] Character changed, freezing current button states until refresh");

                    frozenLikedProfiles = new Dictionary<string, bool>(likedProfiles);
                    frozenFavoritedProfiles = new HashSet<string>(currentFavoritedProfiles);
                    useStateFreeze = true;

                    _ = LoadGalleryData();
                }

                if (ImGui.BeginTabBar("GalleryTabs"))
                {
                    GalleryTab newActiveTab = currentTab;

                    var tabTextColors = new Vector4[]
                    {
                new Vector4(0.4f, 0.8f, 0.8f, 1.0f),  // Cyan - Gallery
                new Vector4(0.4f, 0.8f, 0.4f, 1.0f), // Green - Friends  
                new Vector4(1.0f, 0.8f, 0.2f, 1.0f), // Yellow - Favourites
                new Vector4(1.0f, 0.4f, 0.4f, 1.0f), // Red - Blocked
                new Vector4(0.8f, 0.4f, 1.0f, 1.0f), // Purple - Announcements
                new Vector4(1.0f, 1.0f, 1.0f, 1.0f)  // White - Settings
                    };

                    // Dark tab backgrounds
                    ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.12f, 0.12f, 0.12f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(0.18f, 0.18f, 0.18f, 0.9f));
                    ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(0.22f, 0.22f, 0.22f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.TabUnfocused, new Vector4(0.08f, 0.08f, 0.08f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.TabUnfocusedActive, new Vector4(0.16f, 0.16f, 0.16f, 0.9f));

                    // Gallery Tab
                    ImGui.PushStyleColor(ImGuiCol.Text, currentTab == GalleryTab.Gallery ? tabTextColors[0] : new Vector4(0.92f, 0.92f, 0.92f, 1.0f));
                    if (ImGui.BeginTabItem("Gallery"))
                    {
                        newActiveTab = GalleryTab.Gallery;
                        ImGui.PopStyleColor(1);
                        DrawGalleryTab(totalScale);
                        ImGui.EndTabItem();
                    }
                    else ImGui.PopStyleColor(1);

                    // Friends Tab
                    ImGui.PushStyleColor(ImGuiCol.Text, currentTab == GalleryTab.Friends ? tabTextColors[1] : new Vector4(0.92f, 0.92f, 0.92f, 1.0f));
                    if (ImGui.BeginTabItem("Friends"))
                    {
                        newActiveTab = GalleryTab.Friends;
                        ImGui.PopStyleColor(1);
                        if (lastActiveTab != GalleryTab.Friends)
                        {
                            Plugin.Log.Debug("[Gallery] Switched to Friends tab, auto-refreshing mutual friends");
                            _ = RefreshMutualFriends();
                            lastMutualFriendsUpdate = DateTime.Now;
                        }
                        DrawFriendsTab(totalScale);
                        ImGui.EndTabItem();
                    }
                    else ImGui.PopStyleColor(1);

                    // Favourites Tab
                    ImGui.PushStyleColor(ImGuiCol.Text, currentTab == GalleryTab.Favourites ? tabTextColors[2] : new Vector4(0.92f, 0.92f, 0.92f, 1.0f));
                    if (ImGui.BeginTabItem("Favourites"))
                    {
                        newActiveTab = GalleryTab.Favourites;
                        ImGui.PopStyleColor(1);
                        DrawFavouritesTab(totalScale);
                        ImGui.EndTabItem();
                    }
                    else ImGui.PopStyleColor(1);

                    // Blocked Tab
                    ImGui.PushStyleColor(ImGuiCol.Text, currentTab == GalleryTab.Blocked ? tabTextColors[3] : new Vector4(0.92f, 0.92f, 0.92f, 1.0f));
                    if (ImGui.BeginTabItem("Blocked"))
                    {
                        newActiveTab = GalleryTab.Blocked;
                        ImGui.PopStyleColor(1);
                        DrawBlockedTab(totalScale);
                        ImGui.EndTabItem();
                    }
                    else ImGui.PopStyleColor(1);

                    // Announcements Tab with notification badge
                    bool hasNewAnnouncements = announcements.Any() && announcements.Any(a => a.CreatedAt > lastSeenAnnouncements.ToUniversalTime());

                    ImGui.PushStyleColor(ImGuiCol.Text, currentTab == GalleryTab.Announcements ? tabTextColors[4] : new Vector4(0.92f, 0.92f, 0.92f, 1.0f));

                    if (ImGui.BeginTabItem("Announcements"))
                    {
                        newActiveTab = GalleryTab.Announcements;
                        ImGui.PopStyleColor(1);

                        // Draw notification dot AFTER the tab is created
                        if (hasNewAnnouncements && currentTab != GalleryTab.Announcements)
                        {
                            var drawList = ImGui.GetWindowDrawList();
                            var tabMin = ImGui.GetItemRectMin();
                            var tabMax = ImGui.GetItemRectMax();
                            var dotCenter = new Vector2(tabMax.X - (6 * totalScale), tabMin.Y + (6 * totalScale));
                            drawList.AddCircleFilled(dotCenter, 3 * totalScale, ImGui.GetColorU32(new Vector4(1.0f, 0.3f, 0.3f, 1.0f)));
                        }

                        // Mark as seen when entering the tab
                        if (hasNewAnnouncements)
                        {
                            lastSeenAnnouncements = DateTime.UtcNow;
                            plugin.Configuration.LastSeenAnnouncements = DateTime.UtcNow;
                            plugin.Configuration.Save();
                        }

                        DrawAnnouncementsTab(totalScale);
                        ImGui.EndTabItem();
                    }
                    else
                    {
                        ImGui.PopStyleColor(1);

                        // Draw notification dot when NOT on the tab
                        if (hasNewAnnouncements)
                        {
                            var drawList = ImGui.GetWindowDrawList();
                            var tabMin = ImGui.GetItemRectMin();
                            var tabMax = ImGui.GetItemRectMax();
                            var dotCenter = new Vector2(tabMax.X - (6 * totalScale), tabMin.Y + (6 * totalScale));
                            drawList.AddCircleFilled(dotCenter, 3 * totalScale, ImGui.GetColorU32(new Vector4(1.0f, 0.3f, 0.3f, 1.0f)));
                        }
                    }

                    // Settings Tab
                    ImGui.PushStyleColor(ImGuiCol.Text, currentTab == GalleryTab.Settings ? tabTextColors[5] : new Vector4(0.92f, 0.92f, 0.92f, 1.0f));
                    if (ImGui.BeginTabItem("Settings"))
                    {
                        newActiveTab = GalleryTab.Settings;
                        ImGui.PopStyleColor(1);
                        DrawSettingsTab(totalScale);
                        ImGui.EndTabItem();
                    }
                    else ImGui.PopStyleColor(1);

                    ImGui.PopStyleColor(5); // Pop the 5 tab background colors

                    currentTab = newActiveTab;
                    lastActiveTab = newActiveTab;
                    ImGui.EndTabBar();
                }

                DrawImagePreview(totalScale);
                DrawReportDialog(totalScale);
                DrawReportConfirmation(totalScale);
                DrawErrorMessage();

                // Draw gallery effects
                float deltaTime = ImGui.GetIO().DeltaTime;

                foreach (var effect in galleryLikeEffects.Values)
                {
                    effect.Update(deltaTime);
                }

                foreach (var effect in galleryFavoriteEffects.Values)
                {
                    effect.Update(deltaTime);
                }

                foreach (var kvp in galleryLikeEffects.ToList())
                {
                    kvp.Value.Draw();
                    if (!kvp.Value.IsActive)
                        galleryLikeEffects.Remove(kvp.Key);
                }

                foreach (var kvp in galleryFavoriteEffects.ToList())
                {
                    kvp.Value.Draw();
                    if (!kvp.Value.IsActive)
                        galleryFavoriteEffects.Remove(kvp.Key);
                }
            }
            finally
            {
                ImGui.PopStyleVar(2);
                ImGui.PopStyleColor(4);
            }
        }
        private void DrawTOSModal(float scale)
        {
            // Center the modal
            var viewport = ImGui.GetMainViewport();
            var modalSize = new Vector2(650 * scale, 550 * scale);
            var modalPos = viewport.Pos + (viewport.Size - modalSize) * 0.5f;

            ImGui.SetNextWindowPos(modalPos, ImGuiCond.Always);
            ImGui.SetNextWindowSize(modalSize, ImGuiCond.Always);

            // Match patch notes styling
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.06f, 0.06f, 0.06f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.92f, 0.92f, 0.92f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.15f, 0.15f, 0.18f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.2f, 0.2f, 0.25f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.25f, 0.25f, 0.3f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.25f, 0.25f, 0.25f, 0.6f));

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10.0f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8 * scale, 8 * scale));

            bool modalOpen = true;
            if (ImGui.Begin("Character Select+ Gallery - Terms & Rules", ref modalOpen,
                ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking))
            {
                // Header with warning
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.6f, 0.2f, 1.0f));
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf071"); // Warning triangle
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.Text("IMPORTANT: Gallery Terms & Community Rules");
                ImGui.PopStyleColor();

                ImGui.Separator();
                ImGui.Spacing();

                // Scrollable content
                ImGui.BeginChild("TOSContent", new Vector2(0, -70 * scale), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);

                ImGui.PushTextWrapPos();

                // Age verification
                DrawTOSSection("AGE VERIFICATION REQUIRED", new Vector4(1.0f, 0.4f, 0.4f, 1.0f), scale);
                ImGui.TextWrapped("You must be 18 years or older to use the Character Select+ Gallery. This gallery may contain user-generated content marked as NSFW (Not Safe For Work).");
                ImGui.Spacing();

                // Community rules
                DrawTOSSection("COMMUNITY RULES", new Vector4(0.4f, 0.8f, 1.0f, 1.0f), scale);
                ImGui.BulletText("Respect other users and their characters");
                ImGui.BulletText("No harassment, hate speech, or discriminatory content");
                ImGui.BulletText("Report inappropriate profiles using the right-click menu");
                ImGui.BulletText("NSFW content must be properly marked in the RP Profile Editor");
                ImGui.BulletText("Set your profile sharing to 'Showcase Public' to appear in the gallery");
                ImGui.Spacing();

                // Content warning
                DrawTOSSection("CONTENT WARNING", new Vector4(1.0f, 0.8f, 0.2f, 1.0f), scale);
                ImGui.TextWrapped("The gallery contains user-generated content. While moderated, you may encounter:");
                ImGui.BulletText("Adult themes and mature content (when NSFW is enabled)");
                ImGui.BulletText("Roleplay content that may not align with your preferences");
                ImGui.BulletText("Fan-created characters and stories");
                ImGui.Spacing();

                // Privacy notice
                DrawTOSSection("PRIVACY & DATA", new Vector4(0.6f, 1.0f, 0.6f, 1.0f), scale);
                ImGui.BulletText("Your profiles are only visible based on your sharing settings");
                ImGui.BulletText("You can block users and report inappropriate content");
                ImGui.BulletText("NSFW content is hidden by default - enable in Settings if desired");
                ImGui.BulletText("Your interaction data (likes, favorites) is stored locally");
                ImGui.Spacing();

                // Moderation
                DrawTOSSection("MODERATION", new Vector4(0.8f, 0.4f, 1.0f, 1.0f), scale);
                ImGui.BulletText("Violations may result in profile removal or gallery bans");
                ImGui.BulletText("Appeals can be made through official Character Select+ channels");
                ImGui.BulletText("Moderators reserve the right to remove any content");
                ImGui.Spacing();

                // Agreement text
                ImGui.Separator();
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
                ImGui.TextWrapped("By clicking 'I Accept', you confirm that you are 18+ years old and agree to follow these community rules and terms of service.");
                ImGui.PopStyleColor();

                ImGui.PopTextWrapPos();
                ImGui.EndChild();

                // Bottom buttons
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 120 * scale;
                float totalButtonWidth = buttonWidth * 2 + (20 * scale);
                float availableWidth = ImGui.GetContentRegionAvail().X;
                float buttonStartX = (availableWidth - totalButtonWidth) * 0.5f;

                ImGui.SetCursorPosX(buttonStartX);

                // Accept button
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.6f, 0.2f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.7f, 0.3f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.8f, 0.4f, 1.0f));

                if (ImGui.Button("I Accept (18+)", new Vector2(buttonWidth, 35 * scale)))
                {
                    // User accepted TOS
                    plugin.Configuration.LastAcceptedGalleryTOSVersion = CURRENT_TOS_VERSION;
                    plugin.Configuration.Save();

                    hasAcceptedCurrentTOS = true;
                    showTOSModal = false;

                    // Now load the gallery data
                    _ = LoadGalleryData();

                    Plugin.Log.Info($"[Gallery] User accepted TOS version {CURRENT_TOS_VERSION}");
                }

                ImGui.PopStyleColor(3);

                ImGui.SameLine();

                // Decline button
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.3f, 0.3f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.8f, 0.4f, 0.4f, 1.0f));

                if (ImGui.Button("Decline", new Vector2(buttonWidth, 35 * scale)))
                {
                    // User declined - close the gallery window
                    IsOpen = false;
                    Plugin.Log.Info("[Gallery] User declined TOS - gallery closed");
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("This will close the gallery.\nYou can reopen it anytime to accept the terms.");
                }

                ImGui.PopStyleColor(3);
            }
            ImGui.End();

            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor(7);

            // If user closed modal without accepting, close gallery
            if (!modalOpen)
            {
                IsOpen = false;
            }
        }

        // Simplified section header
        private void DrawTOSSection(string title, Vector4 accentColor, float scale)
        {
            var drawList = ImGui.GetWindowDrawList();
            var startPos = ImGui.GetCursorScreenPos();

            // Simple background bar
            var bgMin = startPos + new Vector2(-8 * scale, -3 * scale);
            var bgMax = startPos + new Vector2(ImGui.GetContentRegionAvail().X + 8 * scale, 22 * scale);
            drawList.AddRectFilled(bgMin, bgMax, ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.15f, 0.6f)), 4f * scale);

            // Left accent line
            drawList.AddRectFilled(bgMin, bgMin + new Vector2(3 * scale, bgMax.Y - bgMin.Y), ImGui.GetColorU32(accentColor), 2f * scale);

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 5 * scale);
            ImGui.TextColored(accentColor, title);
            ImGui.Spacing();
        }

        private void DrawImagePreview(float scale)
        {
            if (!showImagePreview || string.IsNullOrEmpty(imagePreviewUrl))
                return;

            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.Pos);
            ImGui.SetNextWindowSize(viewport.Size);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0.9f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);

            if (ImGui.Begin("ImagePreview", ref showImagePreview,
                    ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
            {
                IDalamudTextureWrap? texture;
                if (File.Exists(imagePreviewUrl!))
                    texture = Plugin.TextureProvider.GetFromFile(imagePreviewUrl!).GetWrapOrDefault();
                else
                    texture = GetProfileTexture(imagePreviewUrl);

                if (texture != null)
                {
                    var windowSize = ImGui.GetWindowSize();
                    var imageSize = new Vector2(texture.Width, texture.Height);

                    float scaleX = windowSize.X * 0.9f / imageSize.X;
                    float scaleY = windowSize.Y * 0.9f / imageSize.Y;
                    float imageScale = Math.Min(scaleX, scaleY);

                    var displaySize = imageSize * imageScale;
                    var startPos = (windowSize - displaySize) * 0.5f;

                    ImGui.SetCursorPos(startPos);
                    ImGui.Image(texture.ImGuiHandle, displaySize);
                }

                if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    showImagePreview = false;
            }
            ImGui.End();

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor();
        }

        private void DrawGalleryTab(float scale)
        {
            ImGui.PushID("SearchSection");
            float searchAvailableWidth = ImGui.GetContentRegionAvail().X;
            float clearButtonWidth = 0f;

            if (!string.IsNullOrEmpty(searchFilter))
            {
                if (ImGui.Button("X##ClearGallerySearch"))
                {
                    searchFilter = "";
                    FilterProfiles();
                }
                clearButtonWidth = ImGui.GetItemRectSize().X + ImGui.GetStyle().ItemSpacing.X;
                ImGui.SameLine();
            }

            ImGui.SetNextItemWidth(searchAvailableWidth - clearButtonWidth);
            if (ImGui.InputTextWithHint("##SearchGallery", "Search tags, names, bios...", ref searchFilter, 100))
            {
                FilterProfiles();
            }
            ImGui.PopID();

            // Sort and Refresh controls
            ImGui.Text("Sort:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120 * scale);
            if (ImGui.BeginCombo("##SortType", GetSortDisplayName(sortType)))
            {
                foreach (GallerySortType sort in Enum.GetValues<GallerySortType>())
                {
                    bool isSelected = sortType == sort;
                    if (ImGui.Selectable(GetSortDisplayName(sort), isSelected))
                    {
                        sortType = sort;
                        SortProfiles();
                    }
                    if (isSelected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (10f * scale));
            if (ImGui.Button("Refresh"))
            {
                _ = LoadGalleryData();
            }

            // Show refresh status
            if (lastSuccessfulRefresh != DateTime.MinValue)
            {
                var timeSinceSuccess = DateTime.Now - lastSuccessfulRefresh;
                var timeSinceAttempt = DateTime.Now - lastAutoRefresh;

                string timeText;
                Vector4 statusColor;
                string statusPrefix = "";

                if (timeSinceSuccess.TotalSeconds < 30)
                {
                    timeText = "Just now";
                    statusColor = new Vector4(0.4f, 0.8f, 0.4f, 1.0f);
                }
                else if (timeSinceSuccess.TotalMinutes < 1)
                {
                    timeText = $"{(int)timeSinceSuccess.TotalSeconds}s ago";
                    statusColor = new Vector4(0.4f, 0.8f, 0.4f, 1.0f);
                }
                else if (timeSinceSuccess.TotalMinutes < 60)
                {
                    timeText = $"{(int)timeSinceSuccess.TotalMinutes}m ago";

                    if (timeSinceSuccess.TotalMinutes > 10)
                    {
                        statusColor = new Vector4(1.0f, 0.8f, 0.4f, 1.0f);
                        statusPrefix = "⚠ ";
                    }
                    else
                    {
                        statusColor = new Vector4(0.6f, 0.6f, 0.6f, 1.0f);
                    }
                }
                else
                {
                    timeText = $"{(int)timeSinceSuccess.TotalHours}h ago";
                    statusColor = new Vector4(1.0f, 0.6f, 0.4f, 1.0f);
                    statusPrefix = "⚠ ";
                }

                ImGui.SameLine();

                if (isAutoRefreshing)
                {
                    ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.4f, 1.0f), "(Updating...)");
                }
                else
                {
                    if (timeSinceSuccess.TotalMinutes > 10 && timeSinceAttempt.TotalMinutes < 1)
                    {
                        ImGui.TextColored(statusColor, $"{statusPrefix}Updated {timeText} (connection issues)");
                    }
                    else
                    {
                        ImGui.TextColored(statusColor, $"{statusPrefix}Updated {timeText}");
                    }
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    if (timeSinceSuccess.TotalMinutes > 5)
                    {
                        ImGui.Text($"Last successful update: {lastSuccessfulRefresh:HH:mm:ss}");
                        ImGui.Text($"Last attempt: {lastAutoRefresh:HH:mm:ss}");
                        ImGui.Text("Auto-refresh may be experiencing network issues");
                    }
                    else
                    {
                        ImGui.Text($"Last update: {lastSuccessfulRefresh:HH:mm:ss}");
                        ImGui.Text("Auto-refresh every 5 minutes");
                    }
                    ImGui.EndTooltip();
                }
            }
            else if (isAutoRefreshing)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.4f, 1.0f), "(Updating...)");
            }

            if (isLoading)
            {
                ImGui.Text("Loading character showcase...");
                return;
            }

            if (filteredProfiles.Count == 0)
            {
                if (allProfiles.Count == 0)
                {
                    ImGui.Text("No characters are currently showcased.");
                    ImGui.Text("Be the first to share your character!");
                }
                else
                {
                    ImGui.Text("No characters match your search.");
                }
                return;
            }

            // Popular tags with button styling
            if (popularTags.Count > 0)
            {
                var reallyPopularTags = popularTags.Take(6).ToList();
                if (reallyPopularTags.Count > 0)
                {
                    ImGui.Text("Popular Tags:");
                    ImGui.SameLine();

                    // Button styling for tags only
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.3f, 0.9f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.6f, 1.0f, 1.0f)); // Purple text

                    for (int i = 0; i < reallyPopularTags.Count; i++)
                    {
                        var tag = reallyPopularTags[i];
                        if (ImGui.SmallButton(tag))
                        {
                            searchFilter = tag;
                            FilterProfiles();
                            currentPage = 0;
                        }
                        if (i < reallyPopularTags.Count - 1) ImGui.SameLine();
                    }

                    ImGui.PopStyleColor(4); // Pop tag button styles
                }
            }

            ImGui.Separator();

            // Pagination
            int totalPages = (int)Math.Ceiling((double)filteredProfiles.Count / PROFILES_PER_PAGE);
            int startIndex = currentPage * PROFILES_PER_PAGE;
            int endIndex = Math.Min(startIndex + PROFILES_PER_PAGE, filteredProfiles.Count);

            DrawPaginationControls(totalPages, scale, "top");

            ImGui.Separator();

            ImGui.BeginChild("GalleryContent", new Vector2(0, -20 * scale), false, ImGuiWindowFlags.AlwaysVerticalScrollbar);
            if (shouldResetScroll)
            {
                ImGui.SetScrollY(0);
                shouldResetScroll = false;
            }
            float availableWidth = ImGui.GetContentRegionAvail().X;
            var style = ImGui.GetStyle();
            float scrollbarWidth = style.ScrollbarSize;
            const float extraNudge = 10f;
            float usableWidth = availableWidth - scrollbarWidth - (extraNudge * scale);

            float minCardWidth = 320f * scale;
            float maxCardWidth = 400f * scale;
            float cardPadding = 15f * scale;

            int cardsPerRow = Math.Max(1, (int)((usableWidth + cardPadding) / (minCardWidth + cardPadding)));
            float cardWidth = Math.Min(maxCardWidth, (usableWidth - (cardsPerRow - 1) * cardPadding) / cardsPerRow);
            float cardHeight = 120f * scale;

            float totalCardsWidth = cardWidth * cardsPerRow + cardPadding * (cardsPerRow - 1);
            float leftoverSpace = usableWidth - totalCardsWidth;
            float marginX = leftoverSpace > 0 ? leftoverSpace * 0.5f : style.WindowPadding.X;

            var currentPageProfiles = filteredProfiles.Skip(startIndex).Take(PROFILES_PER_PAGE).ToList();

            int idx = 0;
            foreach (var profile in currentPageProfiles)
            {
                if (idx % cardsPerRow == 0)
                {
                    if (idx > 0) ImGui.Spacing();
                    ImGui.SetCursorPosX(marginX);
                }
                else
                {
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + cardPadding);
                }

                DrawProfileCard(profile, new Vector2(cardWidth, cardHeight), scale);
                idx++;
            }

            ImGui.EndChild();
            DrawPaginationControls(totalPages, scale, "bottom");
        }

        public void RefreshLikeStatesAfterProfileUpdate(string characterName)
        {
            Plugin.Log.Info($"[Gallery] Refreshing like states after profile update for {characterName}");

            _ = LoadGalleryData();

            var activeCharacter = GetActiveCharacter();
            if (activeCharacter != null)
            {
                string csCharacterKey = activeCharacter.Name;
                var keysToRemove = likedProfiles.Keys
                    .Where(k => k.StartsWith($"{csCharacterKey}|{characterName}"))
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    likedProfiles.Remove(key);
                }
            }
        }

        private async Task RefreshLikeCountsOnlyFixed()
        {
            try
            {
                Plugin.Log.Debug($"[Gallery] Starting auto-refresh (last successful: {(DateTime.Now - lastSuccessfulRefresh).TotalMinutes:F1} minutes ago)");

                using var http = Plugin.CreateAuthenticatedHttpClient();
                http.Timeout = TimeSpan.FromSeconds(10);

                var response = await http.GetAsync("https://character-select-profile-server-production.up.railway.app/gallery");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var serverProfiles = JsonConvert.DeserializeObject<List<GalleryProfile>>(json) ?? new();

                    int updatedCount = 0;

                    foreach (var serverProfile in serverProfiles)
                    {
                        var serverKey = GetProfileKey(serverProfile);

                        var localProfile = allProfiles.FirstOrDefault(p => GetProfileKey(p) == serverKey);
                        if (localProfile != null && localProfile.LikeCount != serverProfile.LikeCount)
                        {
                            Plugin.Log.Debug($"[Gallery] Updated like count for {localProfile.CharacterName}: {localProfile.LikeCount} → {serverProfile.LikeCount}");
                            localProfile.LikeCount = serverProfile.LikeCount;
                            updatedCount++;
                        }

                        var filteredProfile = filteredProfiles.FirstOrDefault(p => GetProfileKey(p) == serverKey);
                        if (filteredProfile != null && filteredProfile.LikeCount != serverProfile.LikeCount)
                        {
                            filteredProfile.LikeCount = serverProfile.LikeCount;
                        }
                    }

                    ClearImpossibleZeroLikes();

                    lastAutoRefresh = DateTime.Now;
                    lastSuccessfulRefresh = DateTime.Now;

                    if (updatedCount > 0)
                    {
                        Plugin.Log.Debug($"[Gallery] Auto-refresh updated {updatedCount} like counts");
                    }
                    else
                    {
                        Plugin.Log.Debug("[Gallery] Auto-refresh completed, no changes");
                    }
                }
                else
                {
                    Plugin.Log.Warning($"[Gallery] Auto-refresh failed with status: {response.StatusCode}");

                    lastAutoRefresh = DateTime.Now;

                    var timeSinceSuccess = DateTime.Now - lastSuccessfulRefresh;
                    if (timeSinceSuccess.TotalMinutes > 15)
                    {
                        Plugin.Log.Warning($"[Gallery] Auto-refresh has been failing for {timeSinceSuccess.TotalMinutes:F0} minutes");
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                Plugin.Log.Warning($"[Gallery] Auto-refresh network error: {ex.Message}");
                lastAutoRefresh = DateTime.Now;
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                Plugin.Log.Warning("[Gallery] Auto-refresh timed out");
                lastAutoRefresh = DateTime.Now;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Gallery] Auto-refresh failed: {ex.Message}");
                lastAutoRefresh = DateTime.Now;
            }
        }

        private async Task LoadProfileWithImageAsync(string characterId)
        {
            try
            {
                var profile = await Plugin.DownloadProfileAsync(characterId);
                if (profile != null)
                {
                    downloadedProfiles[characterId] = profile;
                    Plugin.Log.Debug($"[Gallery] Profile loaded for {SanitizeForLogging(characterId)}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Gallery] Failed to load profile for {SanitizeForLogging(characterId)}: {ex.Message}");
            }
        }

        private void DrawFavouritesTab(float scale)
        {
            if (favoriteSnapshots.Count == 0)
            {
                ImGui.Text("You haven't favourited any characters yet.");
                ImGui.Text("Star characters in the Gallery tab to add them here!");
                return;
            }

            ImGui.Text($"Your Favourited Characters ({favoriteSnapshots.Count})");
            ImGui.Separator();

            FavoriteSnapshot? toRemove = null; // Track which item to remove

            ImGui.BeginChild("FavouritesContent", new Vector2(0, 0), false, ImGuiWindowFlags.AlwaysVerticalScrollbar);

            float availableWidth = ImGui.GetContentRegionAvail().X;
            var style = ImGui.GetStyle();
            float scrollbarWidth = style.ScrollbarSize;
            float usableWidth = availableWidth - scrollbarWidth - (10f * scale);

            float cardWidth = 300f * scale;
            float cardHeight = 120f * scale;
            float cardPadding = 15f * scale;

            int cardsPerRow = Math.Max(1, (int)((usableWidth + cardPadding) / (cardWidth + cardPadding)));
            cardWidth = Math.Min(cardWidth, (usableWidth - (cardsPerRow - 1) * cardPadding) / cardsPerRow);

            float totalCardsWidth = cardWidth * cardsPerRow + cardPadding * (cardsPerRow - 1);
            float leftoverSpace = usableWidth - totalCardsWidth;
            float marginX = leftoverSpace > 0 ? leftoverSpace * 0.5f : style.WindowPadding.X;

            int idx = 0;
            foreach (var favorite in favoriteSnapshots)
            {
                if (idx % cardsPerRow == 0)
                {
                    if (idx > 0) ImGui.Spacing();
                    ImGui.SetCursorPosX(marginX);
                }
                else
                {
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + cardPadding);
                }

                DrawCompactFavoriteCard(favorite, new Vector2(cardWidth, cardHeight), scale, ref toRemove);
                idx++;
            }

            ImGui.EndChild();

            // Remove item AFTER the loop, when it's safe
            if (toRemove != null)
            {
                favoriteSnapshots.Remove(toRemove);
                plugin.Configuration.FavoriteSnapshots = favoriteSnapshots;
                plugin.Configuration.Save();
                EnsureFavoritesFilteredByCSCharacter();
            }
        }
        private void DrawCompactFavoriteCard(FavoriteSnapshot favorite, Vector2 cardSize, float scale, ref FavoriteSnapshot? toRemove)
        {
            var drawList = ImGui.GetWindowDrawList();
            var cardMin = ImGui.GetCursorScreenPos();
            var cardMax = cardMin + cardSize;
            var snapKey = GetProfileKey(favorite);

            bool isCardHovered = ImGui.IsMouseHoveringRect(cardMin, cardMax);

            var bgColor = isCardHovered
                ? new Vector4(0.12f, 0.12f, 0.18f, 0.95f)
                : new Vector4(0.08f, 0.08f, 0.12f, 0.95f);

            var borderColor = isCardHovered
                ? new Vector4(0.35f, 0.35f, 0.45f, 0.8f)
                : new Vector4(0.25f, 0.25f, 0.35f, 0.6f);

            drawList.AddRectFilled(cardMin, cardMax, ImGui.GetColorU32(bgColor), 8f * scale);
            drawList.AddRect(cardMin, cardMax, ImGui.GetColorU32(borderColor), 8f * scale, ImDrawFlags.None, 1f * scale);

            ImGui.BeginChild($"fav_compact_{favorite.CharacterId}_{favorite.FavoritedAt.Ticks}", cardSize, false);

            var imageSize = new Vector2(60 * scale, 60 * scale);
            ImGui.SetCursorPos(new Vector2(8 * scale, 8 * scale));

            // Load image
            IDalamudTextureWrap? tex = null;
            if (!string.IsNullOrEmpty(favorite.LocalImagePath) && File.Exists(favorite.LocalImagePath))
            {
                tex = Plugin.TextureProvider.GetFromFile(favorite.LocalImagePath).GetWrapOrDefault();
            }

            if (tex != null)
            {
                const float RpProfileFrame = 140f;
                float imageScale = imageSize.X / RpProfileFrame;
                float zoom = favorite.ImageZoom;
                Vector2 offset = favorite.ImageOffset * imageScale;
                var cursor = ImGui.GetCursorScreenPos();

                ImGui.BeginChild($"FavImageClip_{favorite.CharacterId}_{favorite.FavoritedAt.Ticks}",
                                 imageSize,
                                 false,
                                 ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                ImGui.SetCursorScreenPos(cursor + offset);
                var texW = tex.Width;
                var texH = tex.Height;
                float aspect = (float)texW / texH;

                float drawW = aspect >= 1f
                    ? imageSize.Y * aspect * zoom
                    : imageSize.X * zoom;
                float drawH = aspect >= 1f
                    ? imageSize.Y * zoom
                    : imageSize.X / aspect * zoom;

                ImGui.Image(tex.ImGuiHandle, new Vector2(drawW, drawH));

                if (ImGui.IsItemClicked(ImGuiMouseButton.Left) || ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    imagePreviewUrl = favorite.LocalImagePath;
                    showImagePreview = true;
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Click to preview full image");
                }

                ImGui.EndChild();
            }
            else
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Loading…");
            }

            // Character info
            ImGui.SameLine();
            ImGui.SetCursorPos(new Vector2(imageSize.X + (12 * scale), 8 * scale));
            ImGui.BeginGroup();

            // Name and pronouns
            ImGui.PushFont(UiBuilder.DefaultFont);
            ImGui.Text(favorite.CharacterName);
            ImGui.PopFont();

            if (!string.IsNullOrEmpty(favorite.Pronouns))
            {
                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() - (4f * scale));
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 1.0f, 1.0f), $"({favorite.Pronouns})");
            }

            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), $"{favorite.Race} • {favorite.Server}");

            // Compact bio
            if (!string.IsNullOrEmpty(favorite.Bio))
            {
                var bioPreview = favorite.Bio.Length > 60 ? favorite.Bio.Substring(0, 60) + "..." : favorite.Bio;
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + (180 * scale));
                ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.85f, 1.0f), $"\"{bioPreview}\"");
                ImGui.PopTextWrapPos();
            }

            // Favourited date
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), $"★ {favorite.FavoritedAt:MMM dd}");

            ImGui.EndGroup();

            ImGui.SetCursorPos(new Vector2(cardSize.X - (25 * scale), 5 * scale));

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.7f, 0.2f, 0.2f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.9f, 0.7f, 0.2f, 0.3f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.7f, 0.2f, 1.0f));

            bool removeClicked = ImGui.Button($"★##{snapKey}_remove_{favorite.FavoritedAt.Ticks}", new Vector2(20 * scale, 20 * scale));

            ImGui.PopStyleColor(4); // Always pop styles, even if clicked!

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Remove from favourites");
            }

            // Mark for removal, but DO NOT remove inside the loop!
            if (removeClicked)
            {
                toRemove = favorite;
            }

            ImGui.EndChild();
        }

        private void DrawFavoriteCard(FavoriteSnapshot favorite, Vector2 cardSize, float scale, ref FavoriteSnapshot? toRemove)
        {
            var drawList = ImGui.GetWindowDrawList();
            var cardMin = ImGui.GetCursorScreenPos();
            var cardMax = cardMin + cardSize;
            var snapKey = GetProfileKey(favorite);

            bool isCardHovered = ImGui.IsMouseHoveringRect(cardMin, cardMax);

            var bgColor = isCardHovered
                ? new Vector4(0.12f, 0.12f, 0.18f, 0.95f)
                : new Vector4(0.08f, 0.08f, 0.12f, 0.95f);

            var borderColor = isCardHovered
                ? new Vector4(0.35f, 0.35f, 0.45f, 0.8f)
                : new Vector4(0.25f, 0.25f, 0.35f, 0.6f);

            drawList.AddRectFilled(cardMin, cardMax, ImGui.GetColorU32(bgColor), 8f * scale);
            drawList.AddRect(cardMin, cardMax, ImGui.GetColorU32(borderColor), 8f * scale, ImDrawFlags.None, 1f * scale);

            ImGui.BeginChild($"fav_card_{favorite.CharacterId}_{favorite.FavoritedAt.Ticks}", cardSize, false);

            var imageSize = new Vector2(100 * scale, 100 * scale);
            ImGui.SetCursorPos(new Vector2(10 * scale, 20 * scale));

            IDalamudTextureWrap? tex = null;
            if (!string.IsNullOrEmpty(favorite.LocalImagePath) && File.Exists(favorite.LocalImagePath))
            {
                tex = Plugin.TextureProvider.GetFromFile(favorite.LocalImagePath).GetWrapOrDefault();
            }

            if (tex != null)
            {
                const float RpProfileFrame = 140f;
                float imageScale = imageSize.X / RpProfileFrame;
                float zoom = favorite.ImageZoom;
                Vector2 offset = favorite.ImageOffset * imageScale;
                var cursor = ImGui.GetCursorScreenPos();

                ImGui.BeginChild($"FavImageClip_{favorite.CharacterId}_{favorite.FavoritedAt.Ticks}",
                                 imageSize,
                                 false,
                                 ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                ImGui.SetCursorScreenPos(cursor + offset);
                var texW = tex.Width;
                var texH = tex.Height;
                float aspect = (float)texW / texH;

                float drawW = aspect >= 1f
                    ? imageSize.Y * aspect * zoom
                    : imageSize.X * zoom;
                float drawH = aspect >= 1f
                    ? imageSize.Y * zoom
                    : imageSize.X / aspect * zoom;

                ImGui.Image(tex.ImGuiHandle, new Vector2(drawW, drawH));

                if (ImGui.IsItemClicked(ImGuiMouseButton.Left) || ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    imagePreviewUrl = favorite.LocalImagePath;
                    showImagePreview = true;
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Click to preview full image");
                }

                ImGui.EndChild();
            }
            else
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Downloading…");
            }

            // Right side - Character info
            ImGui.SameLine();
            ImGui.BeginGroup();

            // Character name from snapshot
            ImGui.PushFont(UiBuilder.DefaultFont);
            ImGui.Text(favorite.CharacterName);
            ImGui.PopFont();

            if (!string.IsNullOrEmpty(favorite.Pronouns))
            {
                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() - (4f * scale));
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 1.0f, 1.0f), $"({favorite.Pronouns})");
            }

            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), $"{favorite.Race} • {favorite.Server}");

            if (!string.IsNullOrEmpty(favorite.Bio))
            {
                ImGui.Spacing();
                var bioPreview = favorite.Bio.Length > 120 ? favorite.Bio.Substring(0, 120) + "..." : favorite.Bio;
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + (240 * scale));
                ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.85f, 1.0f), $"\"{bioPreview}\"");
                ImGui.PopTextWrapPos();
            }

            // Show when favourited
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), $"Favourited {favorite.FavoritedAt:MMM dd}");

            ImGui.EndGroup();

            ImGui.SetCursorPos(new Vector2(cardSize.X - (35 * scale), cardSize.Y - (35 * scale)));

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.7f, 0.2f, 0.2f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.9f, 0.7f, 0.2f, 0.3f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.7f, 0.2f, 1.0f));

            bool removeClicked = ImGui.Button($"★##{snapKey}_remove_{favorite.FavoritedAt.Ticks}", new Vector2(25 * scale, 25 * scale));

            ImGui.PopStyleColor(4); // Always pop styles, even if clicked!

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Remove from favourites");
            }

            // Mark for removal, but DO NOT remove inside the loop!
            if (removeClicked)
            {
                toRemove = favorite;
            }

            ImGui.EndChild();
        }

        private IDalamudTextureWrap? GetFavoriteTexture(string? imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl))
                return null;

            try
            {
                var hash = Convert.ToBase64String(
                    System.Security.Cryptography.MD5.HashData(
                        System.Text.Encoding.UTF8.GetBytes($"FAV_{imageUrl}")
                    )
                ).Replace("/", "_").Replace("+", "-");

                string fileName = $"RPImage_FAV_{hash}.png";
                string localPath = Path.Combine(
                    Plugin.PluginInterface.GetPluginConfigDirectory(),
                    fileName
                );

                if (File.Exists(localPath))
                {
                    return Plugin.TextureProvider.GetFromFile(localPath).GetWrapOrDefault();
                }

                Task.Run(async () =>
                {
                    try
                    {
                        using var client = new HttpClient();
                        client.Timeout = TimeSpan.FromSeconds(10);
                        var data = await client.GetByteArrayAsync(imageUrl);

                        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                        File.WriteAllBytes(localPath, data);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error($"[Gallery] Failed to download favourite image {imageUrl}: {ex.Message}");
                    }
                });

                return null;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Gallery] Error in GetFavoriteTexture: {ex.Message}");
                return null;
            }
        }

        private void DrawSettingsTab(float scale)
        {
            ImGui.Text("Gallery Settings");
            ImGui.Separator();

            // Apply form styling to settings controls
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.16f, 0.16f, 0.16f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.22f, 0.22f, 0.22f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.28f, 0.28f, 0.28f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.3f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));

            // Gallery Main Character
            DrawSettingsSection("Gallery Main Character", scale, () => {
                ImGui.TextWrapped("Choose which physical character represents you in the public gallery. Only this character will appear in the gallery, preventing duplicates.");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("This determines which physical character name appears in the gallery.\nYour CS+ character profiles can still be applied to any physical character,\nbut only your chosen main will be visible to others in the gallery.");
                }

                var currentMain = plugin.Configuration.GalleryMainCharacter;
                string displayText = string.IsNullOrEmpty(currentMain) ? "None Selected" : currentMain;

                ImGui.SetNextItemWidth(300 * scale);
                if (ImGui.BeginCombo("##GalleryMain", displayText))
                {
                    bool isNoneSelected = string.IsNullOrEmpty(currentMain);
                    if (ImGui.Selectable("None (Don't show in gallery)", isNoneSelected))
                    {
                        plugin.Configuration.GalleryMainCharacter = null;
                        plugin.Configuration.Save();
                        Plugin.Log.Info("[Gallery] Gallery main character cleared - will not appear in gallery");
                    }
                    if (isNoneSelected) ImGui.SetItemDefaultFocus();

                    ImGui.Separator();

                    var physicalCharacterOptions = GetPhysicalCharacterOptions();
                    foreach (var option in physicalCharacterOptions)
                    {
                        bool isSelected = currentMain == option;
                        if (ImGui.Selectable(option, isSelected))
                        {
                            plugin.Configuration.GalleryMainCharacter = option;
                            plugin.Configuration.Save();
                            Plugin.Log.Info($"[Gallery] Set gallery main character to: {option}");

                            var parts = option.Split('@');
                            if (parts.Length == 2)
                            {
                                var characterName = parts[0];
                                var character = plugin.Characters.FirstOrDefault(c => c.Name == characterName);
                                if (character?.RPProfile != null)
                                {
                                    _ = Plugin.UploadProfileAsync(character.RPProfile, option);
                                }
                            }
                        }
                        if (isSelected) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }

                if (!string.IsNullOrEmpty(currentMain))
                {
                    ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1.0f), $"✓ Gallery shows you as: {currentMain}");
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.7f, 1.0f), "⚠ You will not appear in the public gallery");
                }
            });

            // Recently Active Status
            DrawSettingsSection("Privacy Settings", scale, () => {
                bool showMyStatus = plugin.Configuration.ShowRecentlyActiveStatus;
                if (ImGui.Checkbox("Show my recently active status to others", ref showMyStatus))
                {
                    plugin.Configuration.ShowRecentlyActiveStatus = showMyStatus;
                    plugin.Configuration.Save();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("When enabled, other players can see when you've used any of your characters recently (green globe).\nWhen disabled, all of your characters always appear offline to others.\nThis setting applies to all your Character Select+ profiles.");
                }
            });

            // NSFW Content Settings
            DrawSettingsSection("Content Filtering", scale, () => {
                bool showNSFW = plugin.Configuration.ShowNSFWProfiles;
                if (ImGui.Checkbox("Show NSFW profiles in gallery", ref showNSFW))
                {
                    plugin.Configuration.ShowNSFWProfiles = showNSFW;
                    plugin.Configuration.Save();

                    // Refresh gallery to apply new NSFW setting**
                    _ = LoadGalleryData();
                    Plugin.Log.Info($"[Gallery] NSFW profiles visibility: {(showNSFW ? "enabled" : "disabled")} - gallery refreshed");
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("When disabled, profiles marked as NSFW will be hidden from the gallery.");
                }

                if (showNSFW)
                {
                    ImGui.TextColored(new Vector4(1.0f, 0.6f, 0.2f, 1.0f), "⚠ NSFW content enabled - you must be 18+");
                }
            });

            // Current CS+ Character Settings
            var activeCharacter = GetActiveCharacter();
            if (activeCharacter != null)
            {
                DrawSettingsSection($"Settings for CS+ Character: {activeCharacter.Name}", scale, () => {
                    var currentSharing = activeCharacter.RPProfile?.Sharing ?? ProfileSharing.AlwaysShare;

                    if (ImGui.RadioButton("Don't share this CS+ character", currentSharing == ProfileSharing.NeverShare))
                    {
                        SetCharacterSharing(activeCharacter, ProfileSharing.NeverShare);
                    }

                    if (ImGui.RadioButton("Share when requested (/viewrp)", currentSharing == ProfileSharing.AlwaysShare))
                    {
                        SetCharacterSharing(activeCharacter, ProfileSharing.AlwaysShare);
                    }

                    if (ImGui.RadioButton("Show in public gallery", currentSharing == ProfileSharing.ShowcasePublic))
                    {
                        SetCharacterSharing(activeCharacter, ProfileSharing.ShowcasePublic);
                    }

                    if (currentSharing == ProfileSharing.ShowcasePublic && string.IsNullOrEmpty(plugin.Configuration.GalleryMainCharacter))
                    {
                        ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.4f, 1.0f), "⚠ This CS+ character won't appear in gallery without a main character selected above");
                    }
                });

                // Gallery Status Message
                if (activeCharacter.RPProfile?.Sharing == ProfileSharing.ShowcasePublic)
                {
                    DrawSettingsSection($"Gallery Status Message for '{activeCharacter.Name}'", scale, () => {
                        ImGui.TextWrapped("Set a custom message for this CS+ character to display in the gallery instead of their bio. Think of it like a quote, lyric, or current mood.");
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip("This is specific to the CS+ character you currently have selected.\nThis is NOT your online/offline status - it's a custom message that shows in gallery cards.\nLeave empty to show your bio instead.");
                        }

                        string currentStatus = activeCharacter.GalleryStatus ?? "";
                        if (ImGui.InputTextMultiline("##GalleryStatus", ref currentStatus, 200, new Vector2(-1, 50 * scale)))
                        {
                            activeCharacter.GalleryStatus = currentStatus;
                            plugin.SaveConfiguration();

                            if (activeCharacter.RPProfile != null)
                            {
                                _ = Plugin.UploadProfileAsync(activeCharacter.RPProfile, activeCharacter.LastInGameName ?? activeCharacter.Name);
                            }
                        }

                        var statusLines = currentStatus.Split('\n');
                        int lineCount = statusLines.Length;
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), $"Characters: {currentStatus.Length}/200, Lines: {lineCount}/2");

                        if (lineCount > 2)
                        {
                            ImGui.TextColored(new Vector4(1f, 0.6f, 0.4f, 1f), "⚠ Status will be truncated to 2 lines in gallery");
                        }
                    });
                }
            }

            // Block Management
            DrawSettingsSection("Block Management", scale, () => {
                ImGui.Text($"You have blocked {blockedProfiles.Count} character(s)");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Blocked characters won't appear in your gallery view");
                }

                if (blockedProfiles.Count > 0)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("View Blocked"))
                    {
                        currentTab = GalleryTab.Blocked;
                    }

                    if (ImGui.Button("Clear All Blocks"))
                    {
                        ImGui.OpenPopup("ConfirmClearBlocks");
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Unblock all {blockedProfiles.Count} blocked characters");
                    }

                    if (ImGui.BeginPopupModal("ConfirmClearBlocks"))
                    {
                        ImGui.Text($"Are you sure you want to unblock all {blockedProfiles.Count} blocked characters?");
                        ImGui.Spacing();

                        if (ImGui.Button("Yes, Clear All", new Vector2(120 * scale, 0)))
                        {
                            blockedProfiles.Clear();
                            plugin.Configuration.BlockedGalleryProfiles.Clear();
                            plugin.Configuration.Save();
                            _ = LoadGalleryData();
                            ImGui.CloseCurrentPopup();
                        }

                        ImGui.SameLine();
                        if (ImGui.Button("Cancel", new Vector2(120 * scale, 0)))
                        {
                            ImGui.CloseCurrentPopup();
                        }

                        ImGui.EndPopup();
                    }
                }
            });

            // Favourites Management
            DrawSettingsSection("Favourites Management", scale, () => {
                ImGui.Text($"You have {favoriteSnapshots.Count} favourited character(s)");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("These are snapshots of characters you've starred in the gallery");
                }

                if (favoriteSnapshots.Count > 0)
                {
                    if (ImGui.Button("Clear All Favourites"))
                    {
                        ImGui.OpenPopup("ConfirmClearFavourites");
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Remove all {favoriteSnapshots.Count} favourited characters");
                    }

                    if (ImGui.BeginPopupModal("ConfirmClearFavourites"))
                    {
                        ImGui.Text($"Are you sure you want to remove all {favoriteSnapshots.Count} favourited characters?");
                        ImGui.Spacing();

                        if (ImGui.Button("Yes, Clear All", new Vector2(120 * scale, 0)))
                        {
                            favoriteSnapshots.Clear();
                            plugin.Configuration.FavoriteSnapshots = favoriteSnapshots;
                            plugin.Configuration.Save();
                            ImGui.CloseCurrentPopup();
                        }

                        ImGui.SameLine();
                        if (ImGui.Button("Cancel", new Vector2(120 * scale, 0)))
                        {
                            ImGui.CloseCurrentPopup();
                        }

                        ImGui.EndPopup();
                    }
                }
            });

            ImGui.PopStyleColor(6);
        }

        private void DrawSettingsSection(string title, float scale, Action content)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
            ImGui.Text(title);
            ImGui.PopStyleColor();

            ImGui.Spacing();

            ImGui.Indent(8 * scale);
            content();
            ImGui.Unindent(8 * scale);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

        private List<string> GetPhysicalCharacterOptions()
        {
            var options = new List<string>();

            foreach (var kvp in plugin.Configuration.LastUsedCharacterByPlayer)
            {
                var physicalCharacterKey = kvp.Key;
                var csCharacterKey = kvp.Value;

                var csCharacterName = csCharacterKey.Contains('@') ? csCharacterKey.Split('@')[0] : csCharacterKey;

                var csCharacter = plugin.Characters.FirstOrDefault(c => c.Name == csCharacterName);
                if (csCharacter?.RPProfile?.Sharing == ProfileSharing.ShowcasePublic)
                {
                    if (!options.Contains(physicalCharacterKey))
                    {
                        options.Add(physicalCharacterKey);
                        Plugin.Log.Debug($"[Gallery] Added physical character from login history: {physicalCharacterKey}");
                    }
                }
            }

            if (Plugin.ClientState.LocalPlayer?.HomeWorld.IsValid == true)
            {
                var currentPhysicalName = Plugin.ClientState.LocalPlayer.Name.TextValue;
                var currentServer = Plugin.ClientState.LocalPlayer.HomeWorld.Value.Name.ToString();
                var currentPhysicalKey = $"{currentPhysicalName}@{currentServer}";

                var hasPublicCharacters = plugin.Characters.Any(c => c.RPProfile?.Sharing == ProfileSharing.ShowcasePublic);

                if (hasPublicCharacters && !options.Contains(currentPhysicalKey))
                {
                    options.Add(currentPhysicalKey);
                    Plugin.Log.Debug($"[Gallery] Added current physical character: {currentPhysicalKey}");
                }
            }

            if (options.Count == 0)
            {
                Plugin.Log.Warning("[Gallery] No physical character options found. You may need to apply a CS+ profile first to populate the list.");
            }

            return options.Distinct().OrderBy(x => x).ToList();
        }
        private void DrawPaginationControls(int totalPages, float scale, string idSuffix = "")
        {
            ImGui.Text($"Page {currentPage + 1} of {totalPages} ({filteredProfiles.Count} profiles)");
            ImGui.SameLine();

            SafeStyleScope(3, 0, () => {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.3f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));

                bool pageChanged = false;

                // First page button
                if (currentPage > 0)
                {
                    if (ImGui.Button($"First##{idSuffix}"))
                    {
                        currentPage = 0;
                        shouldResetScroll = true;
                        pageChanged = true;
                    }
                    ImGui.SameLine();
                }
                else
                {
                    ImGui.BeginDisabled();
                    ImGui.Button($"First##{idSuffix}");
                    ImGui.EndDisabled();
                    ImGui.SameLine();
                }

                // Previous page button
                if (currentPage > 0)
                {
                    if (ImGui.Button($"< Previous##{idSuffix}"))
                    {
                        currentPage--;
                        shouldResetScroll = true;
                        pageChanged = true;
                    }
                    ImGui.SameLine();
                }
                else
                {
                    ImGui.BeginDisabled();
                    ImGui.Button($"< Previous##{idSuffix}");
                    ImGui.EndDisabled();
                    ImGui.SameLine();
                }

                // Next page button
                if (currentPage < totalPages - 1)
                {
                    if (ImGui.Button($"Next >##{idSuffix}"))
                    {
                        currentPage++;
                        shouldResetScroll = true;
                        pageChanged = true;
                    }
                    ImGui.SameLine();
                }
                else
                {
                    ImGui.BeginDisabled();
                    ImGui.Button($"Next >##{idSuffix}");
                    ImGui.EndDisabled();
                    ImGui.SameLine();
                }

                // Last page button
                if (currentPage < totalPages - 1)
                {
                    if (ImGui.Button($"Last##{idSuffix}"))
                    {
                        currentPage = totalPages - 1;
                        shouldResetScroll = true;
                        pageChanged = true;
                    }
                }
                else
                {
                    ImGui.BeginDisabled();
                    ImGui.Button($"Last##{idSuffix}");
                    ImGui.EndDisabled();
                }

                // Force close any open context menus when page changes
                if (pageChanged)
                {
                    ImGui.CloseCurrentPopup();
                }
            });
        }

        private void DrawProfileCard(GalleryProfile profile, Vector2 cardSize, float scale)
        {
            var characterKey = GetProfileKey(profile);

            var activeCharacter = GetActiveCharacter();
            string csCharacterKey = activeCharacter?.Name ?? "NoCharacter";

            string likeKey = GetLikeKey(csCharacterKey, characterKey);

            var isLiked = useStateFreeze ?
    frozenLikedProfiles.ContainsKey(likeKey) :
    IsProfileLikedByAnyOfMyCharacters(characterKey);

            var isFavorited = useStateFreeze ?
                frozenFavoritedProfiles.Contains(characterKey) :
                currentFavoritedProfiles.Contains(characterKey);

            // Get nameplate colour
            Vector3 nameplateColor;

            RPProfile? serverProfile = downloadedProfiles.ContainsKey(characterKey) ? downloadedProfiles[characterKey] : null;
            if (serverProfile?.NameplateColor != null)
            {
                var serverColor = serverProfile.NameplateColor;
                nameplateColor = new Vector3(serverColor.X, serverColor.Y, serverColor.Z);
                cachedCharacterColors[characterKey] = nameplateColor;
            }
            else if (!cachedCharacterColors.TryGetValue(characterKey, out nameplateColor))
            {
                nameplateColor = new Vector3(0.4f, 0.7f, 1.0f);
                var config = Plugin.PluginInterface.GetPluginConfig() as Configuration;
                var match = config?.Characters.FirstOrDefault(c => c.LastInGameName == characterKey || c.Name == profile.CharacterName);
                if (match != null)
                {
                    nameplateColor = match.NameplateColor;
                }
                cachedCharacterColors[characterKey] = nameplateColor;
            }

            var accentColor = new Vector4(nameplateColor.X, nameplateColor.Y, nameplateColor.Z, 1.0f);

            // Card background with hover effect
            var drawList = ImGui.GetWindowDrawList();
            var cardMin = ImGui.GetCursorScreenPos();
            var cardMax = cardMin + cardSize;

            bool isCardHovered = ImGui.IsMouseHoveringRect(cardMin, cardMax);

            var bgColor = isCardHovered
                ? new Vector4(0.12f, 0.12f, 0.18f, 0.95f)
                : new Vector4(0.08f, 0.08f, 0.12f, 0.95f);

            var borderColor = isCardHovered
                ? new Vector4(0.35f, 0.35f, 0.45f, 0.8f)
                : new Vector4(0.25f, 0.25f, 0.35f, 0.6f);

            float cornerRadius = 6f * scale;

            // Drop shadow
            var shadowOffset = new Vector2(2f * scale, 2f * scale);
            var shadowColor = new Vector4(0f, 0f, 0f, 0.2f);
            drawList.AddRectFilled(cardMin + shadowOffset, cardMax + shadowOffset, ImGui.GetColorU32(shadowColor), cornerRadius);

            // Main card background
            drawList.AddRectFilled(cardMin, cardMax, ImGui.GetColorU32(bgColor), cornerRadius);

            // Accent border
            drawList.AddRectFilled(cardMin, cardMin + new Vector2(4f * scale, cardSize.Y), ImGui.GetColorU32(accentColor), cornerRadius, ImDrawFlags.RoundCornersLeft);

            // Card border
            drawList.AddRect(cardMin, cardMax, ImGui.GetColorU32(borderColor), cornerRadius, ImDrawFlags.None, 1f * scale);

            // Hover glow
            if (isCardHovered)
            {
                var glowColor = new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.15f);
                drawList.AddRect(cardMin, cardMax, ImGui.GetColorU32(glowColor), cornerRadius, ImDrawFlags.None, 2f * scale);
            }

            string uniqueId = $"card_{characterKey}";
            ImGui.BeginChild(uniqueId, cardSize, false);

            // Profile loading optimization
            if (!imageLoadStarted.ContainsKey(characterKey) &&
                !loadingProfiles.Contains(characterKey) &&
                lastSuccessfulRefresh != DateTime.MinValue)
            {
                imageLoadStarted[characterKey] = true;
                loadingProfiles.Add(characterKey);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(100);
                        await LoadProfileWithImageAsync(characterKey);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error($"[Gallery] Failed to load profile for {characterKey}: {ex.Message}");
                    }
                    finally
                    {
                        loadingProfiles.Remove(characterKey);
                    }
                });
            }

            var imageSize = new Vector2(80 * scale, 80 * scale);
            ImGui.SetCursorPos(new Vector2(12 * scale, 16 * scale));

            RPProfile? fullProfile = downloadedProfiles.ContainsKey(characterKey) ? downloadedProfiles[characterKey] : null;

            var texture = GetCorrectProfileTexture(fullProfile, profile);

            if (texture != null)
            {
                DrawPortraitImageGalleryOnly(texture, profile, fullProfile, imageSize.X);

                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                    ViewProfile(characterKey);
                else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    var imagePreviewPath = GetCorrectImagePath(fullProfile, profile);
                    if (!string.IsNullOrEmpty(imagePreviewPath))
                    {
                        imagePreviewUrl = imagePreviewPath;
                        showImagePreview = true;
                    }
                    else
                    {
                        Plugin.ChatGui.Print("[Gallery] No custom image to preview - profile is using default image");
                    }
                }

                bool isUsingDefaultImage = string.IsNullOrEmpty(GetCorrectImagePath(fullProfile, profile));
                if (ImGui.IsItemHovered())
                {
                    if (isUsingDefaultImage)
                        ImGui.SetTooltip("Left click: View Profile\nRight click: Default image (no preview available)");
                    else
                        ImGui.SetTooltip("Left click: View Profile\nRight click: Preview Image");
                }
            }
            else
            {
                // Loading placeholder
                var cursor = ImGui.GetCursorScreenPos();
                ImGui.Dummy(imageSize);

                var dl = ImGui.GetWindowDrawList();
                var min = cursor;
                var max = cursor + imageSize;

                var loadingBg = new Vector4(0.15f, 0.15f, 0.2f, 0.9f);
                var loadingBorder = new Vector4(0.3f, 0.3f, 0.35f, 0.8f);

                dl.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(loadingBg), 4f * scale);
                dl.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(loadingBorder), 4f * scale, ImDrawFlags.None, 1f * scale);

                var loadingText = "Loading...";
                var textSize = ImGui.CalcTextSize(loadingText);
                var textPos = min + (imageSize - textSize) * 0.5f;
                dl.AddText(textPos, ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.7f, 1f)), loadingText);

                ImGui.SetCursorScreenPos(cursor);
                if (ImGui.InvisibleButton($"##LoadingCard{characterKey}", imageSize))
                    ViewProfile(characterKey);
            }

            // Right side - Character info
            ImGui.SameLine();
            ImGui.SetCursorPos(new Vector2(imageSize.X + (20 * scale), 16 * scale));
            ImGui.BeginGroup();

            var nameStartPos = ImGui.GetCursorScreenPos();

            // Name and pronouns
            var nameText = profile.CharacterName;
            var pronounsText = !string.IsNullOrEmpty(profile.Pronouns) ? $" ({profile.Pronouns})" : "";

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(accentColor.X * 1.2f, accentColor.Y * 1.2f, accentColor.Z * 1.2f, 1.0f));
            ImGui.PushFont(UiBuilder.DefaultFont);
            ImGui.Text(nameText);
            ImGui.PopFont();
            ImGui.PopStyleColor();

            if (!string.IsNullOrEmpty(profile.Pronouns))
            {
                var nameSize = ImGui.CalcTextSize(nameText);
                var pronounsPos = nameStartPos + new Vector2(nameSize.X + (2f * scale), 1f * scale);

                var pronounsColor = ImGui.GetColorU32(new Vector4(0.7f, 0.7f, 0.8f, 0.9f));
                var smallFont = ImGui.GetFont();
                var smallFontSize = ImGui.GetFontSize() * 0.85f;

                drawList.AddText(smallFont, smallFontSize, pronounsPos, pronounsColor, pronounsText);
            }

            // Check for name hover
            var nameEndPos = ImGui.GetCursorScreenPos();
            var nameRect = new Vector2(nameEndPos.X - nameStartPos.X, ImGui.GetTextLineHeight());
            bool nameHovered = ImGui.IsMouseHoveringRect(nameStartPos, nameStartPos + nameRect);

            if (nameHovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    ViewProfile(characterKey);
                }
            }

            // Age and Race info
            string? age = fullProfile?.Age ?? null;
            string? race = fullProfile?.Race ?? profile.Race;

            if (!string.IsNullOrEmpty(age) && !string.IsNullOrEmpty(race))
            {
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), $"{age} • {race}");
            }
            else if (!string.IsNullOrEmpty(race))
            {
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), race);
            }

            // Status/Bio preview
            if (!string.IsNullOrEmpty(profile.GalleryStatus))
            {

                float statusMaxWidth = cardSize.X - (imageSize.X + (20 * scale)) - (15f * scale);

                var lines = new List<string>();
                var statusWords = profile.GalleryStatus.Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var currentLine = "";
                int totalWordsProcessed = 0;

                foreach (var word in statusWords)
                {
                    var testLine = string.IsNullOrEmpty(currentLine) ? word : $"{currentLine} {word}";
                    var testLineSize = ImGui.CalcTextSize(testLine);

                    if (testLineSize.X <= statusMaxWidth)
                    {
                        currentLine = testLine;
                        totalWordsProcessed++;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(currentLine))
                        {
                            lines.Add(currentLine);
                            currentLine = word;
                            totalWordsProcessed++;
                        }
                        else
                        {
                            var truncatedWord = word;
                            while (ImGui.CalcTextSize(truncatedWord + "...").X > statusMaxWidth && truncatedWord.Length > 3)
                            {
                                truncatedWord = truncatedWord.Substring(0, truncatedWord.Length - 1);
                            }
                            lines.Add(truncatedWord + "...");
                            totalWordsProcessed++;
                        }

                        // Stop if we have 2 lines
                        if (lines.Count >= 2) break;
                    }
                }

                if (!string.IsNullOrEmpty(currentLine) && lines.Count < 2)
                {
                    lines.Add(currentLine);
                }

                if (totalWordsProcessed < statusWords.Length && lines.Count > 0)
                {
                    var lastLineIndex = lines.Count - 1;
                    var lastLine = lines[lastLineIndex];

                    while (ImGui.CalcTextSize(lastLine + "...").X > statusMaxWidth && lastLine.Contains(' '))
                    {
                        var lastSpaceIndex = lastLine.LastIndexOf(' ');
                        if (lastSpaceIndex > 0)
                        {
                            lastLine = lastLine.Substring(0, lastSpaceIndex);
                        }
                        else
                        {
                            break;
                        }
                    }

                    lines[lastLineIndex] = lastLine + "...";
                }

                foreach (var line in lines)
                {
                    ImGui.TextColored(new Vector4(0.9f, 0.85f, 0.9f, 1.0f), line);
                }
            }
            else if (!string.IsNullOrEmpty(profile.Bio))
            {
                // Fallback to bio if no status is set

                float bioMaxWidth = cardSize.X - (imageSize.X + (20 * scale)) - (15f * scale);

                var lines = new List<string>();
                var bioWords = profile.Bio.Replace('\n', ' ').Replace('\r', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var currentLine = "";
                int totalWordsProcessed = 0;

                foreach (var word in bioWords)
                {
                    var testLine = string.IsNullOrEmpty(currentLine) ? word : $"{currentLine} {word}";
                    var testLineSize = ImGui.CalcTextSize($"\"{testLine}\""); // Include quotes in measurement

                    if (testLineSize.X <= bioMaxWidth)
                    {
                        currentLine = testLine;
                        totalWordsProcessed++;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(currentLine))
                        {
                            lines.Add(currentLine);
                            currentLine = word;
                            totalWordsProcessed++;
                        }
                        else
                        {
                            var truncatedWord = word;
                            while (ImGui.CalcTextSize($"\"{truncatedWord}...\"").X > bioMaxWidth && truncatedWord.Length > 3)
                            {
                                truncatedWord = truncatedWord.Substring(0, truncatedWord.Length - 1);
                            }
                            lines.Add(truncatedWord + "...");
                            totalWordsProcessed++;
                        }

                        // Stop if we have 2 lines
                        if (lines.Count >= 2) break;
                    }
                }

                if (!string.IsNullOrEmpty(currentLine) && lines.Count < 2)
                {
                    lines.Add(currentLine);
                }

                if (totalWordsProcessed < bioWords.Length && lines.Count > 0)
                {
                    var lastLineIndex = lines.Count - 1;
                    var lastLine = lines[lastLineIndex];

                    while (ImGui.CalcTextSize($"\"{lastLine}...\"").X > bioMaxWidth && lastLine.Contains(' '))
                    {
                        var lastSpaceIndex = lastLine.LastIndexOf(' ');
                        if (lastSpaceIndex > 0)
                        {
                            lastLine = lastLine.Substring(0, lastSpaceIndex);
                        }
                        else
                        {
                            break;
                        }
                    }

                    lines[lastLineIndex] = lastLine + "...";
                }

                // Draw the bio lines with quotes
                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    if (i == 0)
                    {
                        line = $"\"{line}";
                    }
                    if (i == lines.Count - 1)
                    {
                        line = $"{line}\"";
                    }

                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.85f, 1.0f), line);
                }
            }

            // Tags section to Gallery cards
            if (!string.IsNullOrEmpty(profile.Tags))
            {
                ImGui.SetCursorPos(new Vector2(12 * scale, imageSize.Y + (18 * scale)));
                float tagsMaxWidth = cardSize.X - (12 * scale) - (70 * scale);

                var tags = profile.Tags.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();
                var displayTags = new List<string>();

                foreach (var tag in tags)
                {
                    var tagText = $"Tags: {string.Join(", ", displayTags.Concat(new[] { tag }))}";
                    var testWidth = ImGui.CalcTextSize(tagText).X;

                    if (testWidth <= tagsMaxWidth)
                    {
                        displayTags.Add(tag);
                    }
                    else
                    {
                        break;
                    }
                }

                if (displayTags.Count > 0)
                {
                    var finalTagText = displayTags.Count < tags.Count ?
                        $"Tags: {string.Join(", ", displayTags)}..." :
                        $"Tags: {string.Join(", ", displayTags)}";

                    ImGui.TextColored(new Vector4(0.8f, 0.6f, 1.0f, 1.0f), finalTagText);
                }
            }

            // Globe icon in top right with "online recently" indicator
            var globePos = new Vector2(cardSize.X - (25 * scale), 12 * scale);
            ImGui.SetCursorPos(globePos);

            bool isRecentlyActive = IsCharacterRecentlyActive(profile);
            var globeColor = isRecentlyActive ?
                new Vector4(0.4f, 1.0f, 0.4f, 1.0f) :
                new Vector4(0.6f, 0.6f, 0.7f, 1.0f);

            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(globeColor, "\uf0ac");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                if (isRecentlyActive)
                    ImGui.SetTooltip($"{profile.Server}\n(Recently Active)");
                else
                    ImGui.SetTooltip(profile.Server);
            }

            ImGui.SetCursorPos(new Vector2(cardSize.X - (65 * scale), cardSize.Y - (22 * scale)));

            // Like button
            SafeStyleScope(4, 0, () => {
                if (isLiked)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.85f, 0.3f, 0.4f, 0.2f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.85f, 0.3f, 0.4f, 0.3f));
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.85f, 0.3f, 0.4f, 1.0f));
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.85f, 0.3f, 0.4f, 0.1f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.85f, 0.3f, 0.4f, 0.2f));
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
                }

                if (ImGui.Button($"♥##{characterKey}_like", new Vector2(16 * scale, 18 * scale)))
                {
                    bool wasLiked = isLiked;
                    ToggleLike(characterKey);

                    Vector2 effectPos = ImGui.GetItemRectMin() + ImGui.GetItemRectSize() / 2;
                    string likeEffectKey = $"like_{characterKey}";
                    if (!galleryLikeEffects.ContainsKey(likeEffectKey))
                        galleryLikeEffects[likeEffectKey] = new LikeSparkEffect();
                    galleryLikeEffects[likeEffectKey].Trigger(effectPos, !wasLiked);

                    if (useStateFreeze)
                    {
                        var currentLikeKey = $"{csCharacterKey}|{characterKey}";
                        if (frozenLikedProfiles.ContainsKey(currentLikeKey))
                            frozenLikedProfiles.Remove(currentLikeKey);
                        else
                            frozenLikedProfiles[currentLikeKey] = true;
                    }
                }
            });

            // Like count
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() - (2f * scale));
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (1f * scale));

            var currentProfile = allProfiles.FirstOrDefault(p => GetProfileKey(p) == characterKey);
            int displayLikeCount = currentProfile?.LikeCount ?? profile.LikeCount;
            ImGui.Text($"{displayLikeCount}");

            // Favourite button
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (2f * scale));
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - (1f * scale));

            // Favourite button
            bool favoriteClicked = false;
            SafeStyleScope(4, 0, () => {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.0f, 0.0f, 0.0f, 0.0f));

                if (isFavorited)
                {
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.7f, 0.2f, 0.2f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.9f, 0.7f, 0.2f, 0.3f));
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.7f, 0.2f, 1.0f));
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.7f, 0.2f, 0.1f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.9f, 0.7f, 0.2f, 0.2f));
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
                }

                favoriteClicked = ImGui.Button($"★##{characterKey}_fav", new Vector2(16 * scale, 18 * scale));
            });

            // Handle the click after styles are safely popped
            if (favoriteClicked)
            {
                bool wasFavorited = isFavorited;
                ToggleBookmark(profile);

                Vector2 effectPos = ImGui.GetItemRectMin() + ImGui.GetItemRectSize() / 2;
                string favEffectKey = $"fav_{characterKey}";
                if (!galleryFavoriteEffects.ContainsKey(favEffectKey))
                    galleryFavoriteEffects[favEffectKey] = new FavoriteSparkEffect();
                galleryFavoriteEffects[favEffectKey].Trigger(effectPos, !wasFavorited);

                if (useStateFreeze)
                {
                    if (frozenFavoritedProfiles.Contains(characterKey))
                        frozenFavoritedProfiles.Remove(characterKey);
                    else
                        frozenFavoritedProfiles.Add(characterKey);
                }
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(isFavorited ? "Remove from favourites" : "Add to favourites");
            }

            // Right-click context menu for blocking
            if (ImGui.IsMouseHoveringRect(cardMin, cardMax) && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                ImGui.OpenPopup($"ProfileContextMenu_{characterKey}");
            }

            if (ImGui.BeginPopupContextItem($"ProfileContextMenu_{characterKey}"))
            {
                var currentActiveCharacter = GetActiveCharacter();
                bool isOwnProfile = false;

                if (currentActiveCharacter != null)
                {
                    string userMainCharacter = plugin.Configuration.GalleryMainCharacter ?? "";
                    if (!string.IsNullOrEmpty(userMainCharacter))
                    {
                        string profilePhysicalName = "";
                        if (!string.IsNullOrEmpty(profile.CharacterId) && profile.CharacterId.Contains('_'))
                        {
                            var parts = profile.CharacterId.Split('_', 2);
                            if (parts.Length == 2)
                            {
                                profilePhysicalName = parts[1];
                            }
                        }
                        else
                        {
                            profilePhysicalName = $"{profile.CharacterName}@{profile.Server}";
                        }

                        isOwnProfile = profilePhysicalName == userMainCharacter;
                    }
                }

                if (!isOwnProfile)
                {
                    var profilePlayerKey = ExtractPhysicalCharacterFromId(profile.CharacterId);

                    Vector3 separatorColor = new Vector3(0.4f, 0.7f, 1.0f); // Default
                    if (downloadedProfiles.TryGetValue(characterKey, out var rpProfile))
                    {
                        separatorColor = new Vector3(rpProfile.NameplateColor.X, rpProfile.NameplateColor.Y, rpProfile.NameplateColor.Z);
                    }
                    else if (cachedCharacterColors.TryGetValue(characterKey, out var cachedColor))
                    {
                        separatorColor = cachedColor;
                    }

                    // Friend management with icons
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.8f, 0.4f, 1.0f));
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.Text("\uf007");
                    ImGui.PopFont();
                    ImGui.PopStyleColor();
                    ImGui.SameLine(0, 4 * scale);

                    if (!plugin.Configuration.FollowedPlayers.Contains(profilePlayerKey))
                    {
                        if (ImGui.Selectable("Add this player as a friend"))
                        {
                            plugin.Configuration.FollowedPlayers.Add(profilePlayerKey);
                            plugin.Configuration.Save();
                            _ = UpdateFriendsOnServer();
                            Plugin.ChatGui.Print($"[Gallery] Added friend!");
                        }
                    }
                    else
                    {
                        if (ImGui.Selectable("Remove this player from friends"))
                        {
                            plugin.Configuration.FollowedPlayers.Remove(profilePlayerKey);
                            plugin.Configuration.Save();
                            _ = UpdateFriendsOnServer();
                            Plugin.ChatGui.Print($"[Gallery] Removed friend.");
                        }
                    }

                    ImGui.Spacing();
                    ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(separatorColor.X, separatorColor.Y, separatorColor.Z, 1.0f));
                    ImGui.BeginChild($"##Separator_{characterKey}", new Vector2(ImGui.GetContentRegionAvail().X, 3 * scale), false);
                    ImGui.EndChild();
                    ImGui.PopStyleColor();
                    ImGui.Spacing();

                    // Report option with icon
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.6f, 0.2f, 1.0f));
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.Text("\uf071"); // Warning triangle icon
                    ImGui.PopFont();
                    ImGui.PopStyleColor();
                    ImGui.SameLine(0, 4 * scale);

                    if (ImGui.Selectable("Report this profile"))
                    {
                        OpenReportDialog(GetProfileKey(profile), profile.CharacterName);
                    }

                    ImGui.Spacing();

                    // Block option with icon
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.3f, 0.3f, 1.0f));
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.Text("\uf05e");
                    ImGui.PopFont();
                    ImGui.PopStyleColor();
                    ImGui.SameLine(0, 4 * scale);

                    if (ImGui.Selectable("Block this user"))
                    {
                        BlockProfile(profile);
                    }
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
                    ImGui.Text("This is your own profile");
                    ImGui.PopStyleColor();
                }

                ImGui.EndPopup();
            }
            ImGui.EndChild();
        }


        private bool IsCharacterRecentlyActive(GalleryProfile profile)
        {
            try
            {
                if (!plugin.Configuration.ShowRecentlyActiveStatus)
                    return false;

                if (!downloadedProfiles.TryGetValue(GetProfileKey(profile), out var rpProfile))
                    return false;

                if (rpProfile.LastActiveTime == null)
                    return false;

                var timeSince = DateTime.UtcNow - rpProfile.LastActiveTime.Value;
                return timeSince.TotalMinutes <= 30;
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"Error checking recently active character: {ex.Message}");
                return false;
            }
        }

        private IDalamudTextureWrap? GetCorrectProfileTexture(RPProfile? rpProfile, GalleryProfile galleryProfile)
        {
            var characterKey = GetProfileKey(galleryProfile);

            // Cache the expensive path calculation
            if (!imagePathCache.TryGetValue(characterKey, out string? finalImagePath))
            {
                string? imagePath = null;
                string fallback = Path.Combine(plugin.PluginDirectory, "Assets", "Default.png");

                if (rpProfile != null && !string.IsNullOrEmpty(rpProfile.CustomImagePath) && File.Exists(rpProfile.CustomImagePath))
                {
                    imagePath = rpProfile.CustomImagePath;
                }
                else if (rpProfile != null && !string.IsNullOrEmpty(rpProfile.ProfileImageUrl))
                {
                    imagePath = GetDownloadedImagePath(rpProfile.ProfileImageUrl);
                    if (string.IsNullOrEmpty(imagePath))
                        return null;
                }
                else if (!string.IsNullOrEmpty(galleryProfile.ProfileImageUrl))
                {
                    imagePath = GetDownloadedImagePath(galleryProfile.ProfileImageUrl);
                    if (string.IsNullOrEmpty(imagePath))
                        return null;
                }

                finalImagePath = !string.IsNullOrEmpty(imagePath) && File.Exists(imagePath) ? imagePath : fallback;
                imagePathCache[characterKey] = finalImagePath;
            }

            if (string.IsNullOrEmpty(finalImagePath))
                return null;

            return Plugin.TextureProvider.GetFromFile(finalImagePath).GetWrapOrDefault();
        }

        private string? GetCorrectImagePath(RPProfile? rpProfile, GalleryProfile galleryProfile)
        {
            if (rpProfile != null && !string.IsNullOrEmpty(rpProfile.CustomImagePath) && File.Exists(rpProfile.CustomImagePath))
            {
                return rpProfile.CustomImagePath;
            }
            else if (rpProfile != null && !string.IsNullOrEmpty(rpProfile.ProfileImageUrl))
            {
                return GetDownloadedImagePath(rpProfile.ProfileImageUrl);
            }
            else if (!string.IsNullOrEmpty(galleryProfile.ProfileImageUrl))
            {
                return GetDownloadedImagePath(galleryProfile.ProfileImageUrl);
            }

            return null;
        }

        private void DrawPortraitImageGalleryOnly(IDalamudTextureWrap texture, GalleryProfile galleryProfile, RPProfile? rpProfile, float portraitSize)
        {
            float zoom = 1.0f;
            Vector2 offset = Vector2.Zero;

            if (rpProfile != null)
            {
                zoom = Math.Clamp(rpProfile.ImageZoom, 0.5f, 3.0f);
                offset = rpProfile.ImageOffset;
            }
            else if (galleryProfile.ImageZoom != 1.0f || galleryProfile.ImageOffset != Vector2.Zero)
            {
                zoom = Math.Clamp(galleryProfile.ImageZoom, 0.5f, 3.0f);
                offset = galleryProfile.ImageOffset;
            }
            else
            {
                zoom = 1.2f;
            }

            float scale = portraitSize / RpProfileFrameSize;
            offset = offset * scale;

            float texAspect = (float)texture.Width / texture.Height;
            float drawWidth, drawHeight;

            if (texAspect >= 1f)
            {
                drawHeight = portraitSize * zoom;
                drawWidth = drawHeight * texAspect;
            }
            else
            {
                drawWidth = portraitSize * zoom;
                drawHeight = drawWidth / texAspect;
            }

            Vector2 drawSize = new(drawWidth, drawHeight);
            Vector2 cursor = ImGui.GetCursorScreenPos();

            ImGui.BeginChild("ImageView", new Vector2(portraitSize), false,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

            ImGui.SetCursorScreenPos(cursor + offset);
            ImGui.Image(texture.ImGuiHandle, drawSize);
            ImGui.EndChild();
        }

        private IDalamudTextureWrap? GetProfileTextureForProfile(RPProfile? rpProfile, string? fallbackUrl)
        {
            string? imagePath = null;
            string fallback = Path.Combine(plugin.PluginDirectory, "Assets", "Default.png");

            if (rpProfile != null && !string.IsNullOrEmpty(rpProfile.CustomImagePath) && File.Exists(rpProfile.CustomImagePath))
            {
                imagePath = rpProfile.CustomImagePath;
            }
            else if (rpProfile != null && !string.IsNullOrEmpty(rpProfile.ProfileImageUrl))
            {
                imagePath = GetDownloadedImagePath(rpProfile.ProfileImageUrl);
            }
            else if (!string.IsNullOrEmpty(fallbackUrl))
            {
                imagePath = GetDownloadedImagePath(fallbackUrl);
            }

            string finalImagePath = !string.IsNullOrEmpty(imagePath) && File.Exists(imagePath) ? imagePath : fallback;
            return Plugin.TextureProvider.GetFromFile(finalImagePath).GetWrapOrDefault();
        }

        private string? GetDownloadedImagePath(string imageUrl)
        {
            try
            {
                var hash = Convert.ToBase64String(
                    System.Security.Cryptography.MD5.HashData(
                        System.Text.Encoding.UTF8.GetBytes(imageUrl)
                    )
                ).Replace("/", "_").Replace("+", "-");

                string fileName = $"RPImage_{hash}.png";
                string localPath = Path.Combine(
                    Plugin.PluginInterface.GetPluginConfigDirectory(),
                    fileName
                );

                if (File.Exists(localPath))
                {
                    File.SetLastAccessTime(localPath, DateTime.Now);
                    return localPath;
                }

                if (imageLoadStarted.ContainsKey(imageUrl))
                {
                    return null;
                }

                var configDir = Plugin.PluginInterface.GetPluginConfigDirectory();
                var existingImages = Directory.GetFiles(configDir, "RPImage*.png");
                long currentCacheSize = existingImages.Sum(f => new FileInfo(f).Length);

                if (currentCacheSize > maxCacheSizeBytes)
                {
                    Plugin.Log.Warning($"[Gallery] Cache size limit reached ({currentCacheSize / (1024 * 1024)}MB), skipping download");
                    return null;
                }

                imageLoadStarted[imageUrl] = true;

                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(Random.Shared.Next(100, 500));

                        using var client = new HttpClient();
                        client.Timeout = TimeSpan.FromSeconds(15);
                        var data = await client.GetByteArrayAsync(imageUrl);

                        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                        File.WriteAllBytes(localPath, data);

                        File.SetLastAccessTime(localPath, DateTime.Now);

                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error($"[Gallery] Failed to download image {imageUrl}: {ex.Message}");
                    }
                    finally
                    {
                        imageLoadStarted.Remove(imageUrl);
                    }
                });

                return null;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Gallery] Error in GetDownloadedImagePath: {ex.Message}");
                return null;
            }
        }

        private IDalamudTextureWrap? GetProfileTexture(string? imageUrl)
        {
            return GetProfileTextureForProfile(null, imageUrl);
        }

        private async void ViewProfile(string characterId)
        {
            try
            {
                var rpProfile = await Plugin.DownloadProfileAsync(characterId);
                if (rpProfile != null)
                {
                    plugin.RPProfileViewer.SetExternalProfile(rpProfile);
                    plugin.RPProfileViewer.IsOpen = true;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error viewing profile: {ex.Message}");
            }
        }

        private void ToggleLike(string characterId)
        {
            var activeCharacter = GetActiveCharacter();
            if (activeCharacter == null) return;

            string csCharacterKey = activeCharacter.Name;

            var targetProfile = allProfiles.FirstOrDefault(p => GetProfileKey(p) == characterId);
            string stableLikeTarget = targetProfile?.CharacterName ?? characterId;

            string currentCharacterLikeKey = $"{csCharacterKey}|{stableLikeTarget}";

            // Check if ANY of your characters has liked this profile, no like fraud on my watch!
            string? existingLikerCharacter = GetWhichOfMyCharactersLikedProfile(characterId);
            bool isCurrentlyLiked = existingLikerCharacter != null;

            Plugin.Log.Info($"[Like Debug] CS+ Character: {csCharacterKey}");
            Plugin.Log.Info($"[Like Debug] CharacterId passed: {SanitizeForLogging(characterId)}");
            Plugin.Log.Info($"[Like Debug] Target Profile Name: {stableLikeTarget}");
            Plugin.Log.Info($"[Like Debug] Currently Liked by: {existingLikerCharacter ?? "None"}");
            Plugin.Log.Info($"[Like Debug] Action: {(isCurrentlyLiked ? "Unlike" : "Like")}");

            if (isCurrentlyLiked)
            {
                string existingLikeKey = $"{existingLikerCharacter}|{stableLikeTarget}";
                likedProfiles.Remove(existingLikeKey);
                plugin.Configuration.LikedGalleryProfiles.Remove(existingLikeKey);
                Plugin.Log.Info($"[Like Debug] Removed like key: {existingLikeKey}");
            }
            else
            {
                likedProfiles[currentCharacterLikeKey] = true;
                plugin.Configuration.LikedGalleryProfiles.Add(currentCharacterLikeKey);
                Plugin.Log.Info($"[Like Debug] Added like key: {currentCharacterLikeKey}");
            }

            plugin.Configuration.Save();

            Task.Run(async () =>
            {
                try
                {
                    using var httpClient = new HttpClient();
                    var endpoint = $"https://character-select-profile-server-production.up.railway.app/gallery/{Uri.EscapeDataString(characterId)}/like";
                    var method = isCurrentlyLiked ? HttpMethod.Delete : HttpMethod.Post;
                    var request = new HttpRequestMessage(method, endpoint);

                    request.Headers.Add("X-Character-Key", csCharacterKey);

                    Plugin.Log.Info($"[Like Debug] HTTP {method} to gallery like endpoint");
                    Plugin.Log.Info($"[Like Debug] Using CS+ Character: {csCharacterKey}");

                    var response = await httpClient.SendAsync(request);

                    Plugin.Log.Info($"[Like Debug] Response Status: {response.StatusCode}");

                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        Plugin.Log.Info($"[Like Debug] Response Content: {responseContent}");

                        var result = JsonConvert.DeserializeObject<LikeResponse>(responseContent);

                        var profile = allProfiles.FirstOrDefault(p => GetProfileKey(p) == characterId);
                        if (profile != null)
                        {
                            Plugin.Log.Info($"[Like Debug] Old Like Count: {profile.LikeCount}");
                            profile.LikeCount = result?.LikeCount ?? profile.LikeCount;
                            Plugin.Log.Info($"[Like Debug] New Like Count: {profile.LikeCount}");
                        }

                        var filteredProfile = filteredProfiles.FirstOrDefault(p => GetProfileKey(p) == characterId);
                        if (filteredProfile != null)
                        {
                            filteredProfile.LikeCount = result?.LikeCount ?? filteredProfile.LikeCount;
                        }
                    }
                    else
                    {
                        Plugin.Log.Warning($"[Like Debug] Server request failed: {response.StatusCode}");
                        if (isCurrentlyLiked)
                        {
                            string existingLikeKey = $"{existingLikerCharacter}|{stableLikeTarget}";
                            likedProfiles[existingLikeKey] = true;
                            plugin.Configuration.LikedGalleryProfiles.Add(existingLikeKey);
                        }
                        else
                        {
                            likedProfiles.Remove(currentCharacterLikeKey);
                            plugin.Configuration.LikedGalleryProfiles.Remove(currentCharacterLikeKey);
                        }
                        plugin.Configuration.Save();
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"[Like Debug] Exception: {ex.Message}");
                    if (isCurrentlyLiked)
                    {
                        string existingLikeKey = $"{existingLikerCharacter}|{stableLikeTarget}";
                        likedProfiles[existingLikeKey] = true;
                        plugin.Configuration.LikedGalleryProfiles.Add(existingLikeKey);
                    }
                    else
                    {
                        likedProfiles.Remove(currentCharacterLikeKey);
                        plugin.Configuration.LikedGalleryProfiles.Remove(currentCharacterLikeKey);
                    }
                    plugin.Configuration.Save();
                }
            });
        }
        private string GetLikeKey(string physicalCharacterKey, string characterId)
        {
            var targetProfile = allProfiles.FirstOrDefault(p => GetProfileKey(p) == characterId);
            string stableLikeTarget = targetProfile?.CharacterName ?? characterId;
            return $"{physicalCharacterKey}|{stableLikeTarget}";
        }

        private void ToggleBookmark(GalleryProfile profile)
        {
            var ownerKey = GetActiveCharacter()!.Name;
            var galleryKey = GetProfileKey(profile);

            var existing = favoriteSnapshots
                .FirstOrDefault(f =>
                    f.OwnerCharacterKey == ownerKey &&
                    GetProfileKey(f) == galleryKey);

            if (existing != null)
            {
                favoriteSnapshots.Remove(existing);
                Plugin.Log.Debug($"[Gallery] Removed favourite: {SanitizeForLogging(galleryKey)} for {ownerKey}");
            }
            else
            {
                if (string.IsNullOrEmpty(profile.ProfileImageUrl))
                {
                    Plugin.ChatGui.PrintError("[Gallery] Cannot favorite - profile has no custom image");
                    return;
                }

                bool alreadyExists = favoriteSnapshots.Any(f =>
                    f.OwnerCharacterKey == ownerKey &&
                    GetProfileKey(f) == galleryKey);

                if (alreadyExists)
                {
                    Plugin.Log.Warning($"[Gallery] Attempted to add duplicate favourite: {SanitizeForLogging(galleryKey)} for {ownerKey}");
                    return;
                }

                var hash = Convert.ToBase64String(
                                   System.Security.Cryptography.MD5
                                     .HashData(System.Text.Encoding.UTF8.GetBytes(profile.ProfileImageUrl!))
                               )
                               .Replace("/", "_")
                               .Replace("+", "-");
                var fileName = $"fav_{ownerKey}_{SanitizeForLogging(galleryKey)}_{hash}.png";
                var localPath = Path.Combine(
                                    Plugin.PluginInterface.GetPluginConfigDirectory(),
                                    fileName
                                );

                var snap = new FavoriteSnapshot
                {
                    OwnerCharacterKey = ownerKey,
                    CharacterId = galleryKey,
                    CharacterName = profile.CharacterName,
                    Server = profile.Server,
                    ProfileImageUrl = profile.ProfileImageUrl,
                    Tags = profile.Tags,
                    Bio = profile.Bio,
                    Race = profile.Race,
                    Pronouns = profile.Pronouns,
                    ImageZoom = profile.ImageZoom,
                    ImageOffset = profile.ImageOffset,
                    FavoritedAt = DateTime.Now,
                    LocalImagePath = localPath
                };

                favoriteSnapshots.Add(snap);
                Plugin.Log.Debug($"[Gallery] Added favourite: {SanitizeForLogging(galleryKey)} for {ownerKey}");

                _ = DownloadFavoriteImageAsync(profile.ProfileImageUrl!, localPath)
                       .ContinueWith(_ => plugin.Configuration.Save());
            }

            plugin.Configuration.FavoriteSnapshots = favoriteSnapshots;
            plugin.Configuration.Save();

            EnsureFavoritesFilteredByCSCharacter();
        }

        // Blocked Tab
        private void DrawBlockedTab(float scale)
        {
            if (blockedProfiles.Count == 0)
            {
                ImGui.Text("You haven't blocked any profiles yet.");
                ImGui.TextDisabled("Right-click on a profile in the Gallery tab to block it.");
                return;
            }

            ImGui.Text($"Blocked Characters ({blockedProfiles.Count})");
            ImGui.TextDisabled("These characters' profiles are hidden from your gallery view.");
            ImGui.Separator();

            ImGui.BeginChild("BlockedContent", new Vector2(0, 0), false, ImGuiWindowFlags.AlwaysVerticalScrollbar);

            var blockedList = blockedProfiles.ToList();
            for (int i = 0; i < blockedList.Count; i++)
            {
                var blockedCharacter = blockedList[i];

                ImGui.PushID($"blocked_{i}");

                var drawList = ImGui.GetWindowDrawList();
                var cardMin = ImGui.GetCursorScreenPos();
                var cardHeight = 50f * scale;
                var cardWidth = ImGui.GetContentRegionAvail().X - (20f * scale);
                var cardMax = cardMin + new Vector2(cardWidth, cardHeight);

                var bgColor = new Vector4(0.08f, 0.08f, 0.12f, 0.95f);
                var borderColor = new Vector4(0.25f, 0.25f, 0.35f, 0.6f);

                drawList.AddRectFilled(cardMin, cardMax, ImGui.GetColorU32(bgColor), 6f * scale);
                drawList.AddRect(cardMin, cardMax, ImGui.GetColorU32(borderColor), 6f * scale, ImDrawFlags.None, 1f * scale);

                var accentColor = new Vector4(0.8f, 0.3f, 0.3f, 1.0f);
                drawList.AddRectFilled(cardMin, cardMin + new Vector2(4f * scale, cardHeight), ImGui.GetColorU32(accentColor), 6f * scale, ImDrawFlags.RoundCornersLeft);

                ImGui.BeginChild($"blocked_card_{i}", new Vector2(cardWidth, cardHeight), false);

                ImGui.SetCursorPos(new Vector2(15 * scale, 15 * scale));
                ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1.0f), $"[X] {blockedCharacter}");

                ImGui.SetCursorPos(new Vector2(cardWidth - (85 * scale), 11 * scale));

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.3f, 0.3f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.4f, 0.4f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.2f, 0.2f, 1.0f));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f * scale);

                bool buttonClicked = ImGui.Button("Unblock", new Vector2(70 * scale, 28 * scale));

                ImGui.PopStyleVar();
                ImGui.PopStyleColor(3);

                if (buttonClicked)
                {
                    UnblockProfile(blockedCharacter);
                    break;
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Allow {blockedCharacter}'s profiles to appear in the gallery again");
                }

                ImGui.EndChild();

                ImGui.PopID();

                ImGui.Dummy(new Vector2(0, 8f * scale));
            }

            ImGui.EndChild();
        }

        private void DrawFriendsTab(float scale)
        {
            ImGui.Text("Gallery Friends");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Add friends to see all their public Character Select+ profiles");
            }

            // Apply form styling to friend controls
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.16f, 0.16f, 0.16f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.22f, 0.22f, 0.22f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.28f, 0.28f, 0.28f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.3f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));

            // Add friend section
            ImGui.SetNextItemWidth(150f * scale);
            ImGui.InputTextWithHint("##FriendName", "Player Name", ref addFriendName, 50);

            ImGui.SameLine();
            ImGui.SetNextItemWidth(150f * scale);
            ImGui.InputTextWithHint("##FriendServer", "Server", ref addFriendServer, 50);

            ImGui.SameLine();
            if (ImGui.Button("Add Friend"))
            {
                if (!string.IsNullOrWhiteSpace(addFriendName) && !string.IsNullOrWhiteSpace(addFriendServer))
                {
                    string playerKey = $"{addFriendName.Trim()}@{addFriendServer.Trim()}";

                    if (!plugin.Configuration.FollowedPlayers.Contains(playerKey))
                    {
                        plugin.Configuration.FollowedPlayers.Add(playerKey);
                        plugin.Configuration.Save();
                        _ = UpdateFriendsOnServer();

                        var playerProfiles = GetPlayerProfiles(playerKey);

                        if (playerProfiles.Any())
                        {
                            Plugin.ChatGui.Print($"[Gallery] Added {playerKey} as friend! Found {playerProfiles.Count} character(s).");
                        }
                        else
                        {
                            Plugin.ChatGui.Print($"[Gallery] Added {playerKey} as friend. No public profiles found yet.");
                        }
                    }
                    else
                    {
                        Plugin.ChatGui.PrintError($"[Gallery] {playerKey} is already your friend.");
                    }

                    addFriendName = "";
                    addFriendServer = "";
                }
            }

            // Manual refresh button
            ImGui.SameLine();
            if (ImGui.Button("Refresh Friends"))
            {
                _ = RefreshMutualFriends();
                lastMutualFriendsUpdate = DateTime.Now;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Manually check for friend updates and mutual connections");
            }

            ImGui.PopStyleColor(6);

            ImGui.Separator();

            // Show friends, hold them close, you never know how long til the next server restart
            var mutualFriendProfiles = new List<GalleryProfile>();

            foreach (var mutualFriend in cachedMutualFriends)
            {
                var friendProfiles = GetPlayerProfiles(mutualFriend);
                mutualFriendProfiles.AddRange(friendProfiles);
            }

            if (mutualFriendProfiles.Count > 0)
            {
                var groupedByPlayer = mutualFriendProfiles
                    .GroupBy(p => ExtractPhysicalCharacterFromId(p.CharacterId))
                    .OrderBy(g => g.Key)
                    .ToList();

                ImGui.Text($"Friends ({mutualFriendProfiles.Count} characters from {groupedByPlayer.Count} players)");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Players who added you back as a friend");
                }

                ImGui.BeginChild("MutualFriendsContent", new Vector2(0, 0), false, ImGuiWindowFlags.AlwaysVerticalScrollbar);

                // Draw friend profiles
                float availableWidth = ImGui.GetContentRegionAvail().X;
                var style = ImGui.GetStyle();
                float scrollbarWidth = style.ScrollbarSize;
                const float extraNudge = 10f;
                float usableWidth = availableWidth - scrollbarWidth - (extraNudge * scale);

                float minCardWidth = 320f * scale;
                float maxCardWidth = 400f * scale;
                float cardPadding = 15f * scale;

                int cardsPerRow = Math.Max(1, (int)((usableWidth + cardPadding) / (minCardWidth + cardPadding)));
                float cardWidth = Math.Min(maxCardWidth, (usableWidth - (cardsPerRow - 1) * cardPadding) / cardsPerRow);
                float cardHeight = 120f * scale;

                float totalCardsWidth = cardWidth * cardsPerRow + cardPadding * (cardsPerRow - 1);
                float leftoverSpace = usableWidth - totalCardsWidth;
                float marginX = leftoverSpace > 0 ? leftoverSpace * 0.5f : style.WindowPadding.X;

                int idx = 0;
                foreach (var profile in mutualFriendProfiles.OrderByDescending(p => IsCharacterRecentlyActive(p)).ThenBy(p => p.CharacterName))
                {
                    if (idx % cardsPerRow == 0)
                    {
                        if (idx > 0) ImGui.Spacing();
                        ImGui.SetCursorPosX(marginX);
                    }
                    else
                    {
                        ImGui.SameLine();
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + cardPadding);
                    }

                    DrawFriendProfileCard(profile, new Vector2(cardWidth, cardHeight), scale);
                    idx++;
                }

                ImGui.EndChild();
            }
            else if (plugin.Configuration.FollowedPlayers.Count > 0)
            {
                ImGui.Text("No mutual friends yet.");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("None of your friends have added you back yet!");
                }
                ImGui.Spacing();
            }
            else
            {
                ImGui.Text("No friends added yet.");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Add friends above to see their characters here!");
                }
                ImGui.Spacing();
            }
        }

        private void DrawFriendProfileCard(GalleryProfile profile, Vector2 cardSize, float scale)
        {
            var characterKey = GetProfileKey(profile);

            var activeCharacter = GetActiveCharacter();
            string csCharacterKey = activeCharacter?.Name ?? "NoCharacter";

            string likeKey = GetLikeKey(csCharacterKey, characterKey);

            var isLiked = useStateFreeze ?
    frozenLikedProfiles.ContainsKey(likeKey) :
    IsProfileLikedByAnyOfMyCharacters(characterKey);

            var isFavorited = useStateFreeze ?
                frozenFavoritedProfiles.Contains(characterKey) :
                currentFavoritedProfiles.Contains(characterKey);

            Vector3 nameplateColor;

            RPProfile? serverProfile = downloadedProfiles.ContainsKey(characterKey) ? downloadedProfiles[characterKey] : null;
            if (serverProfile?.NameplateColor != null)
            {
                var serverColor = serverProfile.NameplateColor;
                nameplateColor = new Vector3(serverColor.X, serverColor.Y, serverColor.Z);
                cachedCharacterColors[characterKey] = nameplateColor; // Cache me outside, how about that?
                Plugin.Log.Debug($"[Gallery] Using server nameplate color for friend {profile.CharacterName}: {nameplateColor}");
            }
            else if (!cachedCharacterColors.TryGetValue(characterKey, out nameplateColor))
            {
                // Fallback to local config or default
                nameplateColor = new Vector3(0.4f, 0.7f, 1.0f);
                var config = Plugin.PluginInterface.GetPluginConfig() as Configuration;
                var match = config?.Characters.FirstOrDefault(c => c.LastInGameName == characterKey || c.Name == profile.CharacterName);
                if (match != null)
                {
                    nameplateColor = match.NameplateColor;
                    Plugin.Log.Debug($"[Gallery] Using local config nameplate color for friend {profile.CharacterName}: {nameplateColor}");
                }
                else
                {
                    Plugin.Log.Debug($"[Gallery] Using default nameplate color for friend {profile.CharacterName}: {nameplateColor}");
                }

                cachedCharacterColors[characterKey] = nameplateColor;
            }

            var accentColor = new Vector4(nameplateColor.X, nameplateColor.Y, nameplateColor.Z, 1.0f);

            // Card background with hover effect
            var drawList = ImGui.GetWindowDrawList();
            var cardMin = ImGui.GetCursorScreenPos();
            var cardMax = cardMin + cardSize;

            bool isCardHovered = ImGui.IsMouseHoveringRect(cardMin, cardMax);

            var bgColor = isCardHovered
                ? new Vector4(0.12f, 0.12f, 0.18f, 0.95f)
                : new Vector4(0.08f, 0.08f, 0.12f, 0.95f);

            var borderColor = isCardHovered
                ? new Vector4(0.35f, 0.35f, 0.45f, 0.8f)
                : new Vector4(0.25f, 0.25f, 0.35f, 0.6f);

            float cornerRadius = 6f * scale;

            // Drop shadow
            var shadowOffset = new Vector2(2f * scale, 2f * scale);
            var shadowColor = new Vector4(0f, 0f, 0f, 0.2f);
            drawList.AddRectFilled(cardMin + shadowOffset, cardMax + shadowOffset, ImGui.GetColorU32(shadowColor), cornerRadius);

            // Main card background
            drawList.AddRectFilled(cardMin, cardMax, ImGui.GetColorU32(bgColor), cornerRadius);

            // Accent border
            drawList.AddRectFilled(cardMin, cardMin + new Vector2(4f * scale, cardSize.Y), ImGui.GetColorU32(accentColor), cornerRadius, ImDrawFlags.RoundCornersLeft);

            // Card border
            drawList.AddRect(cardMin, cardMax, ImGui.GetColorU32(borderColor), cornerRadius, ImDrawFlags.None, 1f * scale);

            // Hover glow
            if (isCardHovered)
            {
                var glowColor = new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.15f);
                drawList.AddRect(cardMin, cardMax, ImGui.GetColorU32(glowColor), cornerRadius, ImDrawFlags.None, 2f * scale);
            }

            string uniqueId = $"friend_card_{characterKey}";
            ImGui.BeginChild(uniqueId, cardSize, false);

            // Profile loading optimization
            if (!imageLoadStarted.ContainsKey(characterKey) &&
                !loadingProfiles.Contains(characterKey) &&
                lastSuccessfulRefresh != DateTime.MinValue)
            {
                imageLoadStarted[characterKey] = true;
                loadingProfiles.Add(characterKey);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(100);
                        await LoadProfileWithImageAsync(characterKey);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error($"[Gallery] Failed to load profile for {characterKey}: {ex.Message}");
                    }
                    finally
                    {
                        loadingProfiles.Remove(characterKey);
                    }
                });
            }

            var imageSize = new Vector2(80 * scale, 80 * scale);
            ImGui.SetCursorPos(new Vector2(12 * scale, 16 * scale));

            RPProfile? fullProfile = downloadedProfiles.ContainsKey(characterKey) ? downloadedProfiles[characterKey] : null;

            var texture = GetCorrectProfileTexture(fullProfile, profile);

            if (texture != null)
            {
                DrawPortraitImageGalleryOnly(texture, profile, fullProfile, imageSize.X);

                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                    ViewProfile(characterKey);
                else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    var imagePreviewPath = GetCorrectImagePath(fullProfile, profile);
                    if (!string.IsNullOrEmpty(imagePreviewPath))
                    {
                        imagePreviewUrl = imagePreviewPath;
                        showImagePreview = true;
                    }
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Left click: View Profile\nRight click: Preview Image");
            }
            else
            {
                // Loading placeholder
                var cursor = ImGui.GetCursorScreenPos();
                ImGui.Dummy(imageSize);

                var dl = ImGui.GetWindowDrawList();
                var min = cursor;
                var max = cursor + imageSize;

                var loadingBg = new Vector4(0.15f, 0.15f, 0.2f, 0.9f);
                var loadingBorder = new Vector4(0.3f, 0.3f, 0.35f, 0.8f);

                dl.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(loadingBg), 4f * scale);
                dl.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(loadingBorder), 4f * scale, ImDrawFlags.None, 1f * scale);

                var loadingText = "Loading...";
                var textSize = ImGui.CalcTextSize(loadingText);
                var textPos = min + (imageSize - textSize) * 0.5f;
                dl.AddText(textPos, ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.7f, 1f)), loadingText);

                ImGui.SetCursorScreenPos(cursor);
                if (ImGui.InvisibleButton($"##LoadingCard{characterKey}", imageSize))
                    ViewProfile(characterKey);
            }

            // Right side - Character info
            ImGui.SameLine();
            ImGui.SetCursorPos(new Vector2(imageSize.X + (20 * scale), 16 * scale)); // Back to normal position
            ImGui.BeginGroup();

            var nameStartPos = ImGui.GetCursorScreenPos();

            // Draw name and pronouns
            var nameText = profile.CharacterName;
            var pronounsText = !string.IsNullOrEmpty(profile.Pronouns) ? $" ({profile.Pronouns})" : "";

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(accentColor.X * 1.2f, accentColor.Y * 1.2f, accentColor.Z * 1.2f, 1.0f));
            ImGui.PushFont(UiBuilder.DefaultFont);
            ImGui.Text(nameText);
            ImGui.PopFont();
            ImGui.PopStyleColor();

            if (!string.IsNullOrEmpty(profile.Pronouns))
            {
                var nameSize = ImGui.CalcTextSize(nameText);
                var pronounsPos = nameStartPos + new Vector2(nameSize.X + (2f * scale), 1f * scale);

                var pronounsColor = ImGui.GetColorU32(new Vector4(0.7f, 0.7f, 0.8f, 0.9f));
                var smallFont = ImGui.GetFont();
                var smallFontSize = ImGui.GetFontSize() * 0.85f;

                drawList.AddText(smallFont, smallFontSize, pronounsPos, pronounsColor, pronounsText);
            }

            // Check for name hover
            var nameEndPos = ImGui.GetCursorScreenPos();
            var nameRect = new Vector2(nameEndPos.X - nameStartPos.X, ImGui.GetTextLineHeight());
            bool nameHovered = ImGui.IsMouseHoveringRect(nameStartPos, nameStartPos + nameRect);

            if (nameHovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    ViewProfile(characterKey);
                }
            }

            // Age and Race info
            string? age = fullProfile?.Age ?? null;
            string? race = fullProfile?.Race ?? profile.Race;

            if (!string.IsNullOrEmpty(age) && !string.IsNullOrEmpty(race))
            {
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), $"{age} • {race}");
            }
            else if (!string.IsNullOrEmpty(race))
            {
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), race);
            }

            // Status/Bio preview
            if (!string.IsNullOrEmpty(profile.GalleryStatus))
            {
                float statusMaxWidth = cardSize.X - (imageSize.X + (20 * scale)) - (15f * scale);

                var lines = new List<string>();
                var statusWords = profile.GalleryStatus.Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var currentLine = "";
                int totalWordsProcessed = 0;

                foreach (var word in statusWords)
                {
                    var testLine = string.IsNullOrEmpty(currentLine) ? word : $"{currentLine} {word}";
                    var testLineSize = ImGui.CalcTextSize(testLine);

                    if (testLineSize.X <= statusMaxWidth)
                    {
                        currentLine = testLine;
                        totalWordsProcessed++;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(currentLine))
                        {
                            lines.Add(currentLine);
                            currentLine = word;
                            totalWordsProcessed++;
                        }
                        else
                        {
                            var truncatedWord = word;
                            while (ImGui.CalcTextSize(truncatedWord + "...").X > statusMaxWidth && truncatedWord.Length > 3)
                            {
                                truncatedWord = truncatedWord.Substring(0, truncatedWord.Length - 1);
                            }
                            lines.Add(truncatedWord + "...");
                            totalWordsProcessed++;
                        }

                        // Stop if we have 2 lines
                        if (lines.Count >= 2) break;
                    }
                }

                if (!string.IsNullOrEmpty(currentLine) && lines.Count < 2)
                {
                    lines.Add(currentLine);
                }

                if (totalWordsProcessed < statusWords.Length && lines.Count > 0)
                {
                    var lastLineIndex = lines.Count - 1;
                    var lastLine = lines[lastLineIndex];

                    while (ImGui.CalcTextSize(lastLine + "...").X > statusMaxWidth && lastLine.Contains(' '))
                    {
                        var lastSpaceIndex = lastLine.LastIndexOf(' ');
                        if (lastSpaceIndex > 0)
                        {
                            lastLine = lastLine.Substring(0, lastSpaceIndex);
                        }
                        else
                        {
                            break;
                        }
                    }

                    lines[lastLineIndex] = lastLine + "...";
                }

                foreach (var line in lines)
                {
                    ImGui.TextColored(new Vector4(0.9f, 0.85f, 0.9f, 1.0f), line);
                }
            }
            else if (!string.IsNullOrEmpty(profile.Bio))
            {
                // Fallback to bio if no status is set

                float bioMaxWidth = cardSize.X - (imageSize.X + (20 * scale)) - (15f * scale);

                var lines = new List<string>();
                var bioWords = profile.Bio.Replace('\n', ' ').Replace('\r', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var currentLine = "";
                int totalWordsProcessed = 0;

                foreach (var word in bioWords)
                {
                    var testLine = string.IsNullOrEmpty(currentLine) ? word : $"{currentLine} {word}";
                    var testLineSize = ImGui.CalcTextSize($"\"{testLine}\""); // Include quotes in measurement

                    if (testLineSize.X <= bioMaxWidth)
                    {
                        currentLine = testLine;
                        totalWordsProcessed++;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(currentLine))
                        {
                            lines.Add(currentLine);
                            currentLine = word;
                            totalWordsProcessed++;
                        }
                        else
                        {
                            var truncatedWord = word;
                            while (ImGui.CalcTextSize($"\"{truncatedWord}...\"").X > bioMaxWidth && truncatedWord.Length > 3)
                            {
                                truncatedWord = truncatedWord.Substring(0, truncatedWord.Length - 1);
                            }
                            lines.Add(truncatedWord + "...");
                            totalWordsProcessed++;
                        }

                        // Stop if we have 2 lines
                        if (lines.Count >= 2) break;
                    }
                }

                if (!string.IsNullOrEmpty(currentLine) && lines.Count < 2)
                {
                    lines.Add(currentLine);
                }

                if (totalWordsProcessed < bioWords.Length && lines.Count > 0)
                {
                    var lastLineIndex = lines.Count - 1;
                    var lastLine = lines[lastLineIndex];

                    while (ImGui.CalcTextSize($"\"{lastLine}...\"").X > bioMaxWidth && lastLine.Contains(' '))
                    {
                        var lastSpaceIndex = lastLine.LastIndexOf(' ');
                        if (lastSpaceIndex > 0)
                        {
                            lastLine = lastLine.Substring(0, lastSpaceIndex);
                        }
                        else
                        {
                            break;
                        }
                    }

                    lines[lastLineIndex] = lastLine + "...";
                }

                // Draw the bio lines with quotes
                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    if (i == 0)
                    {
                        line = $"\"{line}";
                    }
                    if (i == lines.Count - 1)
                    {
                        line = $"{line}\"";
                    }

                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.85f, 1.0f), line);
                }
            }

            // Tags section to Gallery cards
            if (!string.IsNullOrEmpty(profile.Tags))
            {
                ImGui.SetCursorPos(new Vector2(12 * scale, imageSize.Y + (18 * scale)));
                float tagsMaxWidth = cardSize.X - (12 * scale) - (70 * scale);

                var tags = profile.Tags.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();
                var displayTags = new List<string>();

                foreach (var tag in tags)
                {
                    var tagText = $"Tags: {string.Join(", ", displayTags.Concat(new[] { tag }))}";
                    var testWidth = ImGui.CalcTextSize(tagText).X;

                    if (testWidth <= tagsMaxWidth)
                    {
                        displayTags.Add(tag);
                    }
                    else
                    {
                        break;
                    }
                }

                if (displayTags.Count > 0)
                {
                    var finalTagText = displayTags.Count < tags.Count ?
                        $"Tags: {string.Join(", ", displayTags)}..." :
                        $"Tags: {string.Join(", ", displayTags)}";

                    ImGui.TextColored(new Vector4(0.8f, 0.6f, 1.0f, 1.0f), finalTagText);
                }
            }

            // Globe icon in top right with "online recently" indicator
            var globePos = new Vector2(cardSize.X - (25 * scale), 12 * scale);
            ImGui.SetCursorPos(globePos);

            bool isRecentlyActive = IsCharacterRecentlyActive(profile);
            var globeColor = isRecentlyActive ?
                new Vector4(0.4f, 1.0f, 0.4f, 1.0f) :
                new Vector4(0.6f, 0.6f, 0.7f, 1.0f);

            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(globeColor, "\uf0ac");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                if (isRecentlyActive)
                    ImGui.SetTooltip($"{profile.Server}\n(Recently Active)");
                else
                    ImGui.SetTooltip(profile.Server);
            }

            ImGui.SetCursorPos(new Vector2(cardSize.X - (65 * scale), cardSize.Y - (22 * scale)));

            // Like button
            if (isLiked)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.85f, 0.3f, 0.4f, 0.2f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.85f, 0.3f, 0.4f, 0.3f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.85f, 0.3f, 0.4f, 1.0f));
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.85f, 0.3f, 0.4f, 0.1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.85f, 0.3f, 0.4f, 0.2f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
            }

            if (ImGui.Button($"♥##{characterKey}_like", new Vector2(16 * scale, 18 * scale)))
            {
                bool wasLiked = isLiked;
                ToggleLike(characterKey);

                Vector2 effectPos = ImGui.GetItemRectMin() + ImGui.GetItemRectSize() / 2;
                string likeEffectKey = $"like_{characterKey}";
                if (!galleryLikeEffects.ContainsKey(likeEffectKey))
                    galleryLikeEffects[likeEffectKey] = new LikeSparkEffect();
                galleryLikeEffects[likeEffectKey].Trigger(effectPos, !wasLiked);

                if (useStateFreeze)
                {
                    var currentLikeKey = $"{csCharacterKey}|{characterKey}";
                    if (frozenLikedProfiles.ContainsKey(currentLikeKey))
                        frozenLikedProfiles.Remove(currentLikeKey);
                    else
                        frozenLikedProfiles[currentLikeKey] = true;
                }
            }
            ImGui.PopStyleColor(4);

            // Like count
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() - (2f * scale));
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (1f * scale));

            var currentProfile = allProfiles.FirstOrDefault(p => GetProfileKey(p) == characterKey);
            int displayLikeCount = currentProfile?.LikeCount ?? profile.LikeCount;
            ImGui.Text($"{displayLikeCount}");

            // Favourite button
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (2f * scale));
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - (1f * scale));

            // Always push exactly 4 style colors
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.0f, 0.0f, 0.0f, 0.0f));

            if (isFavorited)
            {
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.7f, 0.2f, 0.2f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.9f, 0.7f, 0.2f, 0.3f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.7f, 0.2f, 1.0f));
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.7f, 0.2f, 0.1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.9f, 0.7f, 0.2f, 0.2f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
            }

            bool favoriteClicked = ImGui.Button($"★##{characterKey}_fav", new Vector2(16 * scale, 18 * scale));

            // Always pop exactly 4 style colors
            ImGui.PopStyleColor(4);

            // Handle the click after popping colors
            if (favoriteClicked)
            {
                bool wasFavorited = isFavorited;
                ToggleBookmark(profile);

                Vector2 effectPos = ImGui.GetItemRectMin() + ImGui.GetItemRectSize() / 2;
                string favEffectKey = $"fav_{characterKey}";
                if (!galleryFavoriteEffects.ContainsKey(favEffectKey))
                    galleryFavoriteEffects[favEffectKey] = new FavoriteSparkEffect();
                galleryFavoriteEffects[favEffectKey].Trigger(effectPos, !wasFavorited);

                if (useStateFreeze)
                {
                    if (frozenFavoritedProfiles.Contains(characterKey))
                        frozenFavoritedProfiles.Remove(characterKey);
                    else
                        frozenFavoritedProfiles.Add(characterKey);
                }
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(isFavorited ? "Remove from favourites" : "Add to favourites");
            }

            // Right-click context menu for friend management
            if (ImGui.IsMouseHoveringRect(cardMin, cardMax) && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                ImGui.OpenPopup($"FriendContextMenu_{characterKey}");
            }

            if (ImGui.BeginPopupContextItem($"FriendContextMenu_{characterKey}"))
            {
                var friendPlayerKey = ExtractPhysicalCharacterFromId(profile.CharacterId);
                
                // Report option with icon  
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.6f, 0.2f, 1.0f));
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf071"); // Warning triangle icon
                ImGui.PopFont();
                ImGui.PopStyleColor();
                ImGui.SameLine(0, 4 * scale);

                if (ImGui.Selectable("Report this profile"))
                {
                    OpenReportDialog(GetProfileKey(profile), profile.CharacterName);
                }

                ImGui.Spacing();

                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.3f, 0.3f, 1.0f));
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf007");
                ImGui.PopFont();
                ImGui.PopStyleColor();
                ImGui.SameLine(0, 4 * scale);

                // LONGER BUT STILL SAFE TEXT - matches the main gallery context menu length
                if (ImGui.Selectable("Remove this player from friends"))
                {
                    plugin.Configuration.FollowedPlayers.Remove(friendPlayerKey);
                    plugin.Configuration.Save();
                    _ = UpdateFriendsOnServer();
                    _ = RefreshMutualFriends();
                    Plugin.ChatGui.Print($"[Gallery] Removed friend.");
                }

                ImGui.EndPopup();
            }

            ImGui.EndChild();
        }

        private List<GalleryProfile> GetPlayerProfiles(string physicalCharacterKey)
        {
            return allProfiles.Where(profile =>
            {
                var physicalPart = ExtractPhysicalCharacterFromId(profile.CharacterId);
                return physicalPart == physicalCharacterKey;
            }).ToList();
        }

        private string ExtractPhysicalCharacterFromId(string characterId)
        {
            var underscoreIndex = characterId.IndexOf('_');
            if (underscoreIndex > 0 && underscoreIndex < characterId.Length - 1)
            {
                return characterId.Substring(underscoreIndex + 1);
            }
            return characterId;
        }

        private void DrawPlayerCharacterGrid(List<GalleryProfile> profiles, float scale)
        {
            if (profiles.Count == 0) return;

            float availableWidth = ImGui.GetContentRegionAvail().X - (20 * scale);
            float minCardWidth = 280f * scale;
            float cardPadding = 10f * scale;

            int cardsPerRow = Math.Max(1, (int)((availableWidth + cardPadding) / (minCardWidth + cardPadding)));
            float cardWidth = (availableWidth - (cardsPerRow - 1) * cardPadding) / cardsPerRow;
            float cardHeight = 100f * scale;

            ImGui.Indent(10f * scale);

            int idx = 0;
            foreach (var profile in profiles)
            {
                if (idx % cardsPerRow == 0 && idx > 0)
                {
                    ImGui.Spacing();
                }

                if (idx % cardsPerRow != 0)
                {
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + cardPadding);
                }

                DrawFollowedCharacterCard(profile, new Vector2(cardWidth, cardHeight), scale);
                idx++;
            }

            ImGui.Unindent(10f * scale);
        }

        private void DrawFollowedCharacterCard(GalleryProfile profile, Vector2 cardSize, float scale)
        {
            bool isRecentlyActive = IsCharacterRecentlyActive(profile);

            var drawList = ImGui.GetWindowDrawList();
            var cardMin = ImGui.GetCursorScreenPos();
            var cardMax = cardMin + cardSize;

            var bgColor = new Vector4(0.10f, 0.10f, 0.15f, 0.95f);
            drawList.AddRectFilled(cardMin, cardMax, ImGui.GetColorU32(bgColor), 6f * scale);

            var borderColor = isRecentlyActive
                ? new Vector4(0.4f, 1.0f, 0.4f, 0.8f)
                : new Vector4(0.3f, 0.3f, 0.4f, 0.6f);
            drawList.AddRect(cardMin, cardMax, ImGui.GetColorU32(borderColor), 6f * scale, ImDrawFlags.None, 1.5f * scale);

            var namePos = cardMin + new Vector2(8 * scale, 8 * scale);
            var nameColor = isRecentlyActive
                ? new Vector4(0.9f, 1.0f, 0.9f, 1.0f)
                : new Vector4(0.8f, 0.8f, 0.8f, 1.0f);
            drawList.AddText(namePos, ImGui.GetColorU32(nameColor), profile.CharacterName);

            if (!string.IsNullOrEmpty(profile.GalleryStatus))
            {
                var statusPos = cardMin + new Vector2(8 * scale, 28 * scale);
                var statusText = profile.GalleryStatus.Length > 40
                    ? profile.GalleryStatus.Substring(0, 37) + "..."
                    : profile.GalleryStatus;
                drawList.AddText(statusPos, ImGui.GetColorU32(new Vector4(0.9f, 0.85f, 0.9f, 1.0f)), statusText);
            }
            else if (!string.IsNullOrEmpty(profile.Bio))
            {
                var bioPos = cardMin + new Vector2(8 * scale, 28 * scale);
                var bioText = profile.Bio.Length > 40
                    ? profile.Bio.Substring(0, 37) + "..."
                    : profile.Bio;
                drawList.AddText(bioPos, ImGui.GetColorU32(new Vector4(0.7f, 0.8f, 0.9f, 1.0f)), bioText);
            }

            ImGui.SetCursorScreenPos(cardMin);
            ImGui.InvisibleButton($"##FollowedCard_{profile.CharacterId}", cardSize);

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                ViewProfile(profile.CharacterId);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text($"{profile.CharacterName}");
                if (isRecentlyActive)
                {
                    ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), "Recently Active");
                }
                ImGui.Text("Click to view full profile");
                ImGui.EndTooltip();
            }
        }

        private async Task UpdateFriendsOnServer()
        {
            try
            {
                var currentChar = GetCurrentCharacterKey();
                if (string.IsNullOrEmpty(currentChar)) return;

                using var http = new HttpClient();
                var friendsData = new
                {
                    character = currentChar,
                    following = plugin.Configuration.FollowedPlayers.ToList()
                };

                var json = JsonConvert.SerializeObject(friendsData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await http.PostAsync(
                    "https://character-select-profile-server-production.up.railway.app/friends/update-follows",
                    content);

                if (response.IsSuccessStatusCode)
                {
                    Plugin.Log.Debug($"[Gallery] Updated friends on server for {currentChar}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"[Gallery] Failed to update friends on server: {ex.Message}");
            }
        }

        private async Task RefreshMutualFriends()
        {
            try
            {
                var currentChar = GetCurrentCharacterKey();
                if (string.IsNullOrEmpty(currentChar)) return;

                using var http = new HttpClient();

                var followsData = new
                {
                    character = currentChar,
                    following = plugin.Configuration.FollowedPlayers.ToList()
                };

                var json = JsonConvert.SerializeObject(followsData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await http.PostAsync(
                    "https://character-select-profile-server-production.up.railway.app/friends/check-mutual",
                    content);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<dynamic>(responseJson);

                    if (result?.mutualFriends != null)
                    {
                        cachedMutualFriends = ((Newtonsoft.Json.Linq.JArray)result.mutualFriends)
                            .Select(x => x.ToString()).ToList();
                    }
                    else
                    {
                        cachedMutualFriends = new List<string>();
                    }

                    Plugin.Log.Debug($"[Gallery] Found {cachedMutualFriends.Count} mutual friends");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"[Gallery] Mutual friends check failed: {ex.Message}");
            }
        }

        private string GetCurrentCharacterKey()
        {
            if (Plugin.ClientState.LocalPlayer?.HomeWorld.IsValid == true)
            {
                var name = Plugin.ClientState.LocalPlayer.Name.TextValue;
                var world = Plugin.ClientState.LocalPlayer.HomeWorld.Value.Name.ToString();
                return $"{name}@{world}";
            }
            return "";
        }

        // Data management methods
        public async Task LoadGalleryData()
        {
            if (!CanMakeRequest("gallery"))
            {
                Plugin.Log.Debug("[Gallery] Rate limited - skipping request");
                return;
            }
            isLoading = true;
            ClearPerformanceCaches();
            downloadedProfiles.Clear();
            try
            {
                using var http = Plugin.CreateAuthenticatedHttpClient();

                // Send NSFW preference to server
                string nsfwParam = plugin.Configuration.ShowNSFWProfiles ? "?nsfw=true" : "";
                string url = $"https://character-select-profile-server-production.up.railway.app/gallery{nsfwParam}";

                var response = await http.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var rawProfiles = JsonConvert.DeserializeObject<List<GalleryProfile>>(json) ?? new();

                    // Get user's main character setting
                    var userMain = plugin.Configuration.GalleryMainCharacter;
                    var userCharacterNames = plugin.Characters.Select(c => c.Name).ToHashSet();

                    Plugin.Log.Debug($"[Gallery] User main character: {userMain ?? "None"}");
                    Plugin.Log.Debug($"[Gallery] User CS+ characters: {string.Join(", ", userCharacterNames)}");
                    Plugin.Log.Debug($"[Gallery] NSFW enabled: {plugin.Configuration.ShowNSFWProfiles}");
                    Plugin.Log.Debug($"[Gallery] Loaded {rawProfiles.Count} profiles from server");

                    var processedProfiles = new List<GalleryProfile>();

                    foreach (var profile in rawProfiles)
                    {
                        bool isUserCharacter = userCharacterNames.Contains(profile.CharacterName);

                        if (isUserCharacter)
                        {
                            Plugin.Log.Debug($"[Gallery] Found user's character in gallery: {profile.CharacterName}");

                            if (string.IsNullOrEmpty(userMain))
                            {
                                Plugin.Log.Debug($"[Gallery] Filtering user's character {profile.CharacterName} - no main character set");
                                continue;
                            }

                            string? profilePhysicalName = null;

                            if (!string.IsNullOrEmpty(profile.CharacterId) && profile.CharacterId.Contains('_'))
                            {
                                var parts = profile.CharacterId.Split('_', 2);
                                if (parts.Length == 2)
                                {
                                    profilePhysicalName = parts[1]; // This should be "FirstName LastName@Server"
                                }
                            }

                            if (string.IsNullOrEmpty(profilePhysicalName))
                            {
                                profilePhysicalName = $"{profile.CharacterName}@{profile.Server}";
                                Plugin.Log.Warning($"[Gallery] No CharacterId found for {profile.CharacterName}, using fallback: {profilePhysicalName}");
                            }

                            Plugin.Log.Debug($"[Gallery] Profile physical name: '{profilePhysicalName}', User main: '{userMain}'");

                            if (profilePhysicalName != userMain)
                            {
                                Plugin.Log.Debug($"[Gallery] Filtering user's character {profile.CharacterName} - not main character ({profilePhysicalName} != {userMain})");
                                continue;
                            }

                            // Check if the CS+ character is set to public sharing
                            var userCharacter = plugin.Characters.FirstOrDefault(c => c.Name == profile.CharacterName);
                            var sharing = userCharacter?.RPProfile?.Sharing ?? ProfileSharing.AlwaysShare;
                            if (sharing != ProfileSharing.ShowcasePublic)
                            {
                                Plugin.Log.Debug($"[Gallery] Filtering user's character {profile.CharacterName} - sharing not set to public (is: {sharing})");
                                continue;
                            }

                            Plugin.Log.Debug($"[Gallery] ✓ Including user's character {profile.CharacterName} as main character");
                        }

                        processedProfiles.Add(profile);
                    }

                    allProfiles = processedProfiles;

                    ExtractPopularTags();
                    FilterProfiles(); // Additional client-side filtering
                    ClearImpossibleZeroLikes();

                    EnsureLikesFilteredByCSCharacter();
                    EnsureFavoritesFilteredByCSCharacter();

                    Plugin.Log.Info("[Gallery] Gallery refresh completed, unfreezing button states");
                    useStateFreeze = false;
                    frozenLikedProfiles.Clear();
                    frozenFavoritedProfiles.Clear();
                    lastSuccessfulRefresh = DateTime.Now;
                    lastAutoRefresh = DateTime.Now;

                    SortProfiles();
                }
                else
                {
                    Plugin.Log.Warning($"Gallery request failed: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to load gallery: {ex.Message}");
            }
            finally
            {
                isLoading = false;
            }
        }

        private void ExtractPopularTags()
        {
            popularTags = allProfiles
                .SelectMany(p => (p.Tags ?? "").Split(','))
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Take(8)
                .Select(g => g.Key)
                .ToList();
        }

        private void FilterProfiles()
        {
            if (string.IsNullOrEmpty(searchFilter))
            {
                filteredProfiles = allProfiles.Where(p =>
                    !IsProfileBlocked(p)
                // Removed NSFW filtering here since server handles it now
                ).ToList();
            }
            else
            {
                var filter = searchFilter.ToLowerInvariant();
                filteredProfiles = allProfiles.Where(p =>
                    !IsProfileBlocked(p) &&
                    ((p.CharacterName?.ToLowerInvariant().Contains(filter) ?? false) ||
                    (p.Tags?.ToLowerInvariant().Contains(filter) ?? false) ||
                    (p.Bio?.ToLowerInvariant().Contains(filter) ?? false) ||
                    (p.Race?.ToLowerInvariant().Contains(filter) ?? false) ||
                    (p.Pronouns?.ToLowerInvariant().Contains(filter) ?? false))
                ).ToList();
            }

            SortProfiles();
        }
        
        private void SortProfiles()
        {
            filteredProfiles = sortType switch
            {
                GallerySortType.Popular => filteredProfiles.OrderByDescending(p => p.LikeCount).ToList(),
                GallerySortType.Recent => filteredProfiles.OrderByDescending(p => DateTime.Parse(p.LastUpdated)).ToList(),
                GallerySortType.Alphabetical => filteredProfiles.OrderBy(p => p.CharacterName).ToList(),
                _ => filteredProfiles
            };
        }

        private string GetSortDisplayName(GallerySortType sort) => sort switch
        {
            GallerySortType.Popular => "Most Liked",
            GallerySortType.Recent => "Recent",
            GallerySortType.Alphabetical => "A-Z",
            _ => sort.ToString()
        };

        private Character? GetActiveCharacter()
        {
            Character? newCharacter = null;

            if (!string.IsNullOrEmpty(plugin.Configuration.LastUsedCharacterKey))
            {
                newCharacter = plugin.Characters.FirstOrDefault(c => c.Name == plugin.Configuration.LastUsedCharacterKey);
                if (newCharacter != null)
                {
                    if (lastActiveCharacter?.Name != newCharacter.Name)
                    {
                        lastActiveCharacter = newCharacter;
                    }
                    return newCharacter;
                }
            }

            newCharacter = plugin.Characters.FirstOrDefault();

            if (lastActiveCharacter?.Name != newCharacter?.Name)
            {
                lastActiveCharacter = newCharacter;
            }

            return newCharacter;
        }

        private void SetCharacterSharing(Character character, ProfileSharing sharing)
        {
            if (character.RPProfile == null)
                character.RPProfile = new RPProfile();

            character.RPProfile.Sharing = sharing;
            plugin.SaveConfiguration();
            _ = Plugin.UploadProfileAsync(character.RPProfile, character.LastInGameName ?? character.Name);
        }

        private void LoadFavorites()
        {
            favoriteSnapshots = plugin.Configuration.FavoriteSnapshots ?? new List<FavoriteSnapshot>();
            EnsureFavoritesFilteredByCSCharacter();
        }

        private async void LoadProfileWithImage(string characterId)
        {
            if (loadingProfiles.Contains(characterId))
            {
                Plugin.Log.Debug($"[Gallery] Already loading profile for {SanitizeForLogging(characterId)}, skipping");
                return;
            }

            loadingProfiles.Add(characterId);

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                var profile = await Plugin.DownloadProfileAsync(characterId);

                if (profile != null && !cts.Token.IsCancellationRequested)
                {
                    downloadedProfiles[characterId] = profile;
                    Plugin.Log.Debug($"[Gallery] Profile loaded for {SanitizeForLogging(characterId)}");
                }
            }
            catch (OperationCanceledException)
            {
                Plugin.Log.Warning($"[Gallery] Profile loading timed out for {SanitizeForLogging(characterId)}");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Gallery] Failed to load profile for {SanitizeForLogging(characterId)}: {ex.Message}");
            }
            finally
            {
                loadingProfiles.Remove(characterId);
            }
        }

        private string GetFavoriteLocalPath(string? imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl))
                return "";
            var hash = Convert.ToBase64String(
                System.Security.Cryptography.MD5.HashData(
                    System.Text.Encoding.UTF8.GetBytes($"FAV_{imageUrl}")
                )
            ).Replace("/", "_").Replace("+", "-");
            var fileName = $"RPImage_FAV_{hash}.png";
            return Path.Combine(
                Plugin.PluginInterface.GetPluginConfigDirectory(),
                fileName
            );
        }

        private void CleanupImageCache()
        {
            try
            {
                var configDir = Plugin.PluginInterface.GetPluginConfigDirectory();
                var galleryImages = Directory.GetFiles(configDir, "RPImage_*.png");
                var favoriteImages = Directory.GetFiles(configDir, "RPImage_FAV_*.png");
                var allCacheImages = galleryImages.Concat(favoriteImages).ToArray();

                Plugin.Log.Info($"[Gallery] Cache cleanup: Found {allCacheImages.Length} cached images");

                long totalSize = allCacheImages.Sum(file => new FileInfo(file).Length);
                Plugin.Log.Info($"[Gallery] Current cache size: {totalSize / (1024 * 1024)}MB");

                var filesToDelete = new List<string>();

                var cutoffTime = DateTime.Now - maxImageAge;
                foreach (var file in allCacheImages)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastAccessTime < cutoffTime)
                    {
                        filesToDelete.Add(file);
                    }
                }

                if (totalSize > maxCacheSizeBytes)
                {
                    var filesByAge = allCacheImages
                        .Except(filesToDelete)
                        .Select(f => new FileInfo(f))
                        .OrderBy(f => f.LastAccessTime)
                        .ToList();

                    long currentSize = totalSize - filesToDelete.Sum(f => new FileInfo(f).Length);

                    foreach (var fileInfo in filesByAge)
                    {
                        if (currentSize <= maxCacheSizeBytes) break;

                        if (!fileInfo.Name.Contains("FAV_") || currentSize > maxCacheSizeBytes * 2)
                        {
                            filesToDelete.Add(fileInfo.FullName);
                            currentSize -= fileInfo.Length;
                        }
                    }
                }

                foreach (var file in filesToDelete)
                {
                    try
                    {
                        File.Delete(file);
                        Plugin.Log.Debug($"[Gallery] Deleted cached image: {Path.GetFileName(file)}");
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Warning($"[Gallery] Failed to delete {file}: {ex.Message}");
                    }
                }

                long newTotalSize = Directory.GetFiles(configDir, "RPImage*.png")
                    .Sum(file => new FileInfo(file).Length);

                Plugin.Log.Info($"[Gallery] Cache cleanup complete: Deleted {filesToDelete.Count} files, " +
                                $"new size: {newTotalSize / (1024 * 1024)}MB");

                lastCacheCleanup = DateTime.Now;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Gallery] Cache cleanup failed: {ex.Message}");
            }
        }

        private async Task DownloadFavoriteImageAsync(string url, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var data = await client.GetByteArrayAsync(url);
            File.WriteAllBytes(path, data);
        }

        private void EnsureLikesFilteredByCSCharacter()
        {
            var activeCharacter = GetActiveCharacter();
            if (activeCharacter == null)
            {
                likedProfiles.Clear();
                return;
            }

            string csCharacterKey = activeCharacter.Name;

            var allLikedProfiles = plugin.Configuration.LikedGalleryProfiles ?? new HashSet<string>();

            likedProfiles.Clear();

            foreach (var likeKey in allLikedProfiles)
            {
                if (likeKey.StartsWith(csCharacterKey + "|"))
                {
                    likedProfiles[likeKey] = true;
                }
            }

            Plugin.Log.Debug($"[Gallery] Restored {likedProfiles.Count} likes for {csCharacterKey}");
        }

        private bool HasCharacterChanged()
        {
            var currentActiveCharacter = GetActiveCharacter();
            bool hasChanged = lastActiveCharacter?.Name != currentActiveCharacter?.Name;

            if (hasChanged)
            {
                var lastCharName = lastActiveCharacter?.Name ?? "null";
                var currentCharName = currentActiveCharacter?.Name ?? "null";
                Plugin.Log.Info($"[Gallery] Character changed from {lastCharName} to {currentCharName}");

                downloadedProfiles.Clear();
                LoadBlockedProfiles();

                lastActiveCharacter = currentActiveCharacter;
                return true;
            }

            return false;
        }

        private void ClearImpossibleZeroLikes()
        {
            var activeCharacter = GetActiveCharacter();
            if (activeCharacter == null) return;

            var impossibleLikes = new List<string>();

            var myCharacterNames = plugin.Characters.Select(c => c.Name).ToList();

            foreach (var profile in allProfiles)
            {
                var profileKey = GetProfileKey(profile);
                string stableLikeTarget = profile.CharacterName ?? profileKey;

                foreach (var myCharacterName in myCharacterNames)
                {
                    string likeKey = $"{myCharacterName}|{stableLikeTarget}";

                    if ((likedProfiles.ContainsKey(likeKey) ||
                        (plugin.Configuration.LikedGalleryProfiles?.Contains(likeKey) ?? false)) &&
                        profile.LikeCount == 0)
                    {
                        if (DateTime.Now - lastSuccessfulRefresh < TimeSpan.FromMinutes(1) ||
                            (!string.IsNullOrEmpty(profile.LastUpdated) &&
                             DateTime.Parse(profile.LastUpdated) > DateTime.Now.AddDays(-1)))
                        {
                            impossibleLikes.Add(likeKey);
                            Plugin.Log.Info($"[Gallery] Clearing impossible like: {myCharacterName} → {stableLikeTarget}");
                        }
                    }
                }
            }

            foreach (var impossibleKey in impossibleLikes)
            {
                likedProfiles.Remove(impossibleKey);
                plugin.Configuration.LikedGalleryProfiles?.Remove(impossibleKey);
            }

            if (impossibleLikes.Count > 0)
            {
                plugin.Configuration.Save();
                Plugin.Log.Info($"[Gallery] Cleared {impossibleLikes.Count} like states due to apparent server reset");
            }
        }

        private void LoadBlockedProfiles()
        {
            blockedProfiles = plugin.Configuration.BlockedGalleryProfiles ?? new HashSet<string>();
        }

        private void BlockProfile(GalleryProfile profile)
        {
            var profileKey = GetProfileKey(profile);
            string mainCharacterName = profile.CharacterName ?? SanitizeForLogging(profileKey);

            if (!blockedProfiles.Contains(mainCharacterName))
            {
                blockedProfiles.Add(mainCharacterName);
                plugin.Configuration.BlockedGalleryProfiles.Add(mainCharacterName);
                plugin.Configuration.Save();

                Plugin.Log.Info($"[Gallery] Blocked all profiles from: {mainCharacterName}");
                FilterProfiles();
            }
        }

        private void UnblockProfile(string blockedCharacterName)
        {
            if (blockedProfiles.Contains(blockedCharacterName))
            {
                blockedProfiles.Remove(blockedCharacterName);
                plugin.Configuration.BlockedGalleryProfiles.Remove(blockedCharacterName);
                plugin.Configuration.Save();

                Plugin.Log.Info($"[Gallery] Unblocked profiles from: {blockedCharacterName}");
                _ = LoadGalleryData();
            }
        }

        private bool IsProfileBlocked(GalleryProfile profile)
        {
            string mainCharacterName = profile.CharacterName ?? GetProfileKey(profile);
            return blockedProfiles.Contains(mainCharacterName);
        }

        private bool CanMakeRequest(string endpoint)
        {
            if (LastRequestTimes.TryGetValue(endpoint, out var lastTime))
            {
                if (DateTime.Now - lastTime < MinimumRequestInterval)
                {
                    return false;
                }
            }

            LastRequestTimes[endpoint] = DateTime.Now;
            return true;
        }

        private bool IsProfileLikedByAnyOfMyCharacters(string characterKey)
        {
            var targetProfile = allProfiles.FirstOrDefault(p => GetProfileKey(p) == characterKey);
            string stableLikeTarget = targetProfile?.CharacterName ?? characterKey;

            var myCharacterNames = plugin.Characters.Select(c => c.Name).ToList();

            foreach (var myCharacterName in myCharacterNames)
            {
                string likeKey = $"{myCharacterName}|{stableLikeTarget}";
                if (likedProfiles.ContainsKey(likeKey) ||
                    (plugin.Configuration.LikedGalleryProfiles?.Contains(likeKey) ?? false))
                {
                    return true;
                }
            }

            return false;
        }

        private string? GetWhichOfMyCharactersLikedProfile(string characterKey)
        {
            var targetProfile = allProfiles.FirstOrDefault(p => GetProfileKey(p) == characterKey);
            string stableLikeTarget = targetProfile?.CharacterName ?? characterKey;

            var myCharacterNames = plugin.Characters.Select(c => c.Name).ToList();

            foreach (var myCharacterName in myCharacterNames)
            {
                string likeKey = $"{myCharacterName}|{stableLikeTarget}";
                if (likedProfiles.ContainsKey(likeKey) ||
                    (plugin.Configuration.LikedGalleryProfiles?.Contains(likeKey) ?? false))
                {
                    return myCharacterName;
                }
            }

            return null;
        }
        private async Task LoadAnnouncements()
        {
            try
            {
                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(10);
                var response = await http.GetAsync("https://character-select-profile-server-production.up.railway.app/announcements");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var serverAnnouncements = JsonConvert.DeserializeObject<List<Announcement>>(json) ?? new();
                    announcements = serverAnnouncements.OrderByDescending(a => a.CreatedAt).ToList();
                    lastAnnouncementUpdate = DateTime.Now;
                    Plugin.Log.Debug($"[Gallery] Loaded {announcements.Count} announcements");
                }
                else
                {
                    Plugin.Log.Warning($"[Gallery] Failed to load announcements: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Gallery] Error loading announcements: {ex.Message}");
            }
        }

        // Submit a report to the server
        private async Task SubmitReport(string characterId, string characterName, ReportReason reason, string details, string customReason = "")
        {
            try
            {
                var activeCharacter = GetActiveCharacter();
                if (activeCharacter == null)
                {
                    Plugin.ChatGui.PrintError("[Gallery] No active character found for reporting");
                    return;
                }

                string reporterName = activeCharacter.LastInGameName ?? activeCharacter.Name;
                string reasonText = reason == ReportReason.Other ? customReason : GetReportReasonText(reason);

                var reportRequest = new ReportRequest
                {
                    ReportedCharacterId = characterId,
                    ReportedCharacterName = characterName,
                    ReporterCharacter = reporterName,
                    Reason = reasonText,
                    Details = details
                };

                using var http = new HttpClient();

                var settings = new JsonSerializerSettings
                {
                    ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
                };

                var json = JsonConvert.SerializeObject(reportRequest, settings);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await http.PostAsync(
                    "https://character-select-profile-server-production.up.railway.app/reports",
                    content);

                if (response.IsSuccessStatusCode)
                {
                    // Close the report dialog
                    showReportDialog = false;
                    reportTargetCharacterId = "";
                    reportTargetCharacterName = "";
                    reportDetails = "";
                    customReportReason = "";

                    // Show success confirmation
                    reportConfirmationMessage = $"Report submitted successfully for {characterName}.\n\nThank you for helping keep the gallery safe!";
                    showReportConfirmation = true;
                }
                else
                {
                    reportConfirmationMessage = $"Failed to submit report: {response.StatusCode}\n\nPlease try again later.";
                    showReportConfirmation = true;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Gallery] Error submitting report: {ex.Message}");
                reportConfirmationMessage = "Failed to submit report due to network error.\n\nPlease check your connection and try again.";
                showReportConfirmation = true;
            }
        }
        private void DrawReportConfirmation(float scale)
        {
            if (!showReportConfirmation || string.IsNullOrEmpty(reportConfirmationMessage))
                return;

            ImGui.SetNextWindowSize(new Vector2(400 * scale, 180 * scale), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.06f, 0.06f, 0.06f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.12f, 0.12f, 0.12f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.18f, 0.18f, 0.18f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.25f, 0.25f, 0.25f, 0.6f));

            // Success button styling
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.3f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));

            if (ImGui.Begin("Report Submitted##ReportConfirmation", ref showReportConfirmation,
                ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize))
            {
                ImGui.Spacing();

                // Subtle success indicator
                ImGui.SetCursorPosX((ImGui.GetWindowWidth() - ImGui.CalcTextSize("✓").X) * 0.5f);
                ImGui.TextColored(new Vector4(0.4f, 0.8f, 0.4f, 1.0f), "✓");

                ImGui.Spacing();

                // Message with your standard text colors
                var messageLines = reportConfirmationMessage.Split('\n');
                foreach (var line in messageLines)
                {
                    var lineWidth = ImGui.CalcTextSize(line).X;
                    ImGui.SetCursorPosX((ImGui.GetWindowWidth() - lineWidth) * 0.5f);

                    if (line.Contains("successfully"))
                    {
                        ImGui.TextColored(new Vector4(0.92f, 0.92f, 0.92f, 1.0f), line);
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), line);
                    }
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                // Center the OK button
                float buttonWidth = 80 * scale;
                ImGui.SetCursorPosX((ImGui.GetWindowWidth() - buttonWidth) * 0.5f);

                if (ImGui.Button("OK", new Vector2(buttonWidth, 0)))
                {
                    showReportConfirmation = false;
                    reportConfirmationMessage = "";
                }
            }
            ImGui.End();

            ImGui.PopStyleColor(7); // Pop style colors
        }

        // Get user-friendly text for report reasons
        private string GetReportReasonText(ReportReason reason)
        {
            return reason switch
            {
                ReportReason.InappropriateContent => "Inappropriate Content",
                ReportReason.Spam => "Spam",
                ReportReason.MaliciousLinks => "Malicious Links",
                ReportReason.Other => "Other",
                _ => "Other"
            };
        }

        // Open the report dialog for a specific character
        private void OpenReportDialog(string characterId, string characterName)
        {
            reportTargetCharacterId = characterId;
            reportTargetCharacterName = characterName;
            selectedReportReason = ReportReason.InappropriateContent;
            customReportReason = "";
            reportDetails = "";
            showReportDialog = true;
        }

        // Draw the report dialog popup
        private void DrawReportDialog(float scale)
        {
            if (!showReportDialog || string.IsNullOrEmpty(reportTargetCharacterName))
                return;

            ImGui.SetNextWindowSize(new Vector2(500 * scale, 400 * scale), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));

            // Dark styling for report dialog
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.06f, 0.06f, 0.06f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.8f, 0.3f, 0.3f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.9f, 0.4f, 0.4f, 1.0f));

            if (ImGui.Begin($"Report Profile: {reportTargetCharacterName}##ReportDialog", ref showReportDialog,
                ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoCollapse))
            {
                ImGui.Text($"You are reporting: {reportTargetCharacterName}");
                ImGui.Separator();
                ImGui.Spacing();

                // Reason selection
                ImGui.Text("Reason for report:");
                ImGui.SetNextItemWidth(-1);

                string[] reasonOptions = Enum.GetValues<ReportReason>()
                    .Select(GetReportReasonText)
                    .ToArray();

                int selectedIndex = (int)selectedReportReason;
                if (ImGui.Combo("##ReportReason", ref selectedIndex, reasonOptions, reasonOptions.Length))
                {
                    selectedReportReason = (ReportReason)selectedIndex;
                }

                // Custom reason input if "Other" is selected
                if (selectedReportReason == ReportReason.Other)
                {
                    ImGui.Spacing();
                    ImGui.Text("Please specify:");
                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputText("##CustomReason", ref customReportReason, 100);
                }

                ImGui.Spacing();
                ImGui.Text("Additional details (optional):");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextMultiline("##ReportDetails", ref reportDetails, 500, new Vector2(-1, 100 * scale));

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                // Buttons
                bool canSubmit = selectedReportReason != ReportReason.Other || !string.IsNullOrWhiteSpace(customReportReason);

                if (!canSubmit)
                {
                    ImGui.BeginDisabled();
                }

                if (ImGui.Button("Submit Report", new Vector2(120 * scale, 0)))
                {
                    _ = SubmitReport(reportTargetCharacterId, reportTargetCharacterName, selectedReportReason, reportDetails, customReportReason);
                }

                if (!canSubmit)
                {
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        ImGui.SetTooltip("Please specify a reason when selecting 'Other'");
                    }
                }

                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(120 * scale, 0)))
                {
                    showReportDialog = false;
                }

                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f),
                    "Reports are reviewed by administrators. False reports may result in restrictions.");
            }
            ImGui.End();

            ImGui.PopStyleColor(3);
        }

        // Draw the announcements tab
        private void DrawAnnouncementsTab(float scale)
        {
            // Auto-load announcements if needed
            if (DateTime.Now - lastAnnouncementUpdate > announcementUpdateInterval)
            {
                _ = LoadAnnouncements();
            }

            if (announcements.Count == 0)
            {
                ImGui.Text("No announcements at this time.");
                if (ImGui.Button("Refresh"))
                {
                    _ = LoadAnnouncements();
                }
                return;
            }

            ImGui.Text($"Announcements ({announcements.Count})");
            if (ImGui.Button("Refresh"))
            {
                _ = LoadAnnouncements();
            }

            ImGui.Separator();
            ImGui.Spacing();

            ImGui.BeginChild("AnnouncementsContent", new Vector2(0, 0), false, ImGuiWindowFlags.AlwaysVerticalScrollbar);

            foreach (var announcement in announcements)
            {
                DrawAnnouncementCard(announcement, scale);
                ImGui.Spacing();
            }

            ImGui.EndChild();
        }

        // Draw individual announcement card
        private void DrawAnnouncementCard(Announcement announcement, float scale)
        {
            var drawList = ImGui.GetWindowDrawList();
            var cardMin = ImGui.GetCursorScreenPos();
            var cardWidth = ImGui.GetContentRegionAvail().X - (20 * scale);
            var cardHeight = 120 * scale; // Dynamic height based on content

            // Determine colors based on announcement type
            Vector4 accentColor = announcement.Type switch
            {
                "warning" => new Vector4(1.0f, 0.6f, 0.2f, 1.0f), // Orange
                "maintenance" => new Vector4(0.8f, 0.3f, 0.3f, 1.0f), // Red
                "update" => new Vector4(0.3f, 0.8f, 0.3f, 1.0f), // Green
                _ => new Vector4(0.3f, 0.7f, 1.0f, 1.0f) // Blue for info
            };

            var cardMax = cardMin + new Vector2(cardWidth, cardHeight);
            var bgColor = new Vector4(0.08f, 0.08f, 0.12f, 0.95f);
            var borderColor = new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.6f);

            // Draw card background
            drawList.AddRectFilled(cardMin, cardMax, ImGui.GetColorU32(bgColor), 8f * scale);
            drawList.AddRectFilled(cardMin, cardMin + new Vector2(6f * scale, cardHeight), ImGui.GetColorU32(accentColor), 8f * scale, ImDrawFlags.RoundCornersLeft);
            drawList.AddRect(cardMin, cardMax, ImGui.GetColorU32(borderColor), 8f * scale, ImDrawFlags.None, 1f * scale);

            ImGui.BeginChild($"announcement_{announcement.Id}", new Vector2(cardWidth, cardHeight), false);

            // Header with title and type
            ImGui.SetCursorPos(new Vector2(15 * scale, 10 * scale));
            ImGui.PushStyleColor(ImGuiCol.Text, accentColor);
            ImGui.PushFont(UiBuilder.DefaultFont);
            ImGui.Text(announcement.Title);
            ImGui.PopFont();
            ImGui.PopStyleColor();

            // Type label - properly aligned to right edge
            string typeText = announcement.Type.ToUpper();
            var typeTextSize = ImGui.CalcTextSize(typeText);
            ImGui.SameLine();
            ImGui.SetCursorPosX(cardWidth - typeTextSize.X - (15 * scale)); // 15 = right padding
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), typeText);

            // Message
            ImGui.SetCursorPos(new Vector2(15 * scale, 35 * scale));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + cardWidth - (30 * scale));
            ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1.0f), announcement.Message);
            ImGui.PopTextWrapPos();

            // Date
            ImGui.SetCursorPos(new Vector2(15 * scale, cardHeight - (25 * scale)));
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
                $"Posted: {announcement.CreatedAt:MMM dd, yyyy}");

            ImGui.EndChild();
        }
        private string SanitizeForLogging(string characterId)
        {
            // Extract just the CS+ character name from characterId format: "CSName_PhysicalName@Server"
            var underscoreIndex = characterId.IndexOf('_');
            if (underscoreIndex > 0)
            {
                return characterId.Substring(0, underscoreIndex);
            }

            // If no underscore, might be just a character name already
            return characterId.Contains('@') ? characterId.Split('@')[0] : characterId;
        }
        private void SafeStyleScope(int colorCount, int varCount, Action action)
        {
            try
            {
                action();
            }
            finally
            {
                if (colorCount > 0) ImGui.PopStyleColor(colorCount);
                if (varCount > 0) ImGui.PopStyleVar(varCount);
            }
        }

        private void ShowErrorMessage(string message)
        {
            lastErrorMessage = message;
            lastErrorTime = DateTime.Now;
        }

        private void DrawErrorMessage()
        {
            if (!string.IsNullOrEmpty(lastErrorMessage) && DateTime.Now - lastErrorTime < TimeSpan.FromSeconds(5))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.4f, 0.4f, 1.0f));
                ImGui.Text($"⚠ {lastErrorMessage}");
                ImGui.PopStyleColor();
            }
        }

        public void EmergencyStop()
        {
            isAutoRefreshing = false;
            isLoading = false;
            loadingProfiles.Clear();
            imageLoadStarted.Clear();
            Plugin.Log.Warning("[Gallery] Emergency stop activated - all requests halted");
        }
        private float GetSafeScale(float baseScale)
        {
            return Math.Clamp(baseScale, 0.3f, 5.0f);
        }

        public override void OnOpen()
        {
            LoadFavorites();
            LoadBlockedProfiles();
            EnsureLikesFilteredByCSCharacter();
            EnsureFavoritesFilteredByCSCharacter();

            // Check if user needs to accept TOS
            hasAcceptedCurrentTOS = plugin.Configuration.LastAcceptedGalleryTOSVersion >= CURRENT_TOS_VERSION;
            if (!hasAcceptedCurrentTOS)
            {
                showTOSModal = true;
            }
            else
            {
                _ = LoadGalleryData();
            }

            _ = LoadAnnouncements();
            if (plugin.Configuration.LastSeenAnnouncements != DateTime.MinValue)
            {
                lastSeenAnnouncements = plugin.Configuration.LastSeenAnnouncements;
            }
        }

        public override void OnClose()
        {
            Task.Run(() => CleanupImageCache());
            base.OnClose();
        }
    }
}

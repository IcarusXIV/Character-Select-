using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin.Windows
{
    public enum ModType
    {
        Unknown,
        Gear,
        Hair,
        Face,
        Eyes,
        Tattoos,
        FacePaint,
        Body,
        EarsTails,
        Mount,
        Minion,
        Emote,
        StandingIdle,
        ChairSitting,
        GroundSitting,
        LyingDozing,
        MixedIdle,
        Movement,
        JobVFX,
        VFX,
        Skeleton,
        Other
    }

    // Simple classes for parsing mod JSON files
    public class ModOption
    {
        public string? Name { get; set; }
        public int Priority { get; set; }
        public Dictionary<string, string>? Files { get; set; }
    }

    public class ModGroup
    {
        public string? Name { get; set; }
        public List<ModOption>? Options { get; set; }
    }

    public class ModDependency
    {
        public string RequiredModName { get; set; } = "";
        public string RequiredModPath { get; set; } = "";
        public bool IsFound { get; set; } = false;
    }

    public class ModEntry
    {
        public string Directory { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsEnabled { get; set; }
        public List<string> Categories { get; set; } = new();
        public bool IsBlacklisted { get; set; }
        public int Priority { get; set; }
        public bool IsCurrentlyAffecting { get; set; }
        public ModType ModType { get; set; } = ModType.Unknown;
        public List<ModDependency> Dependencies { get; set; } = new();
        public bool HasOnlyModels { get; set; } = false; // True if mod contains only .mdl files, no textures
        public bool HasOnlyTextures { get; set; } = false; // True if mod contains only textures/materials, no models
        public ModConflictAnalysisResult? Analysis { get; set; } = null; // Contextual dependency and conflict analysis
        public bool IsInherited { get; set; } = false; // True if inherited from parent collection

        // Dependency and conflict fields from analysis
        public bool HasDependency { get; set; } = false;
        public string DependencyType { get; set; } = "";
        public bool HasConflicts { get; set; } = false;
        public List<string> ConflictingMods { get; set; } = new();
    }

    public partial class SecretModeModWindow : Window, IDisposable
    {
        private Plugin plugin;
        private UIStyles uiStyles;
        private List<ModEntry> availableMods = new();
        private Dictionary<string, bool> selectedMods = new(); // true=Enable, false=Disable
        // Snapshot of selectedMods at Open() time, used to compute the +N enabled / -N
        // disabled delta in the footer ("vs. saved").
        private Dictionary<string, bool> originalSelection = new();
        private HashSet<string> modsToInherit = new(); // Mods explicitly set to Inherit (restore Penumbra inheritance)
        private string searchFilter = ""; // Category-specific search
        private string globalSearchFilter = ""; // Global search across all categories
        private bool isLoading = true;
        private int? editingCharacterIndex = null;
        private CharacterDesign? editingDesign = null;
        private string? editingCharacterName = null;
        private Action<Dictionary<string, bool>>? onSave = null;
        private Action<HashSet<string>>? onSavePins = null;
        private Action<HashSet<string>>? onSaveInherit = null; // Callback for mods to restore inheritance

        // Pagination
        private const int ModsPerPage = 150;
        private int currentPage = 0;
        private Dictionary<int, int> categoryPageNumbers = new(); // Track page per category

        // Collection management
        private string currentCollectionName = "";
        private Guid currentCollectionId = Guid.Empty;
        private Dictionary<Guid, string> availableCollections = new();
        private int selectedCollectionIndex = 0;
        private bool userHasSelectedCollection = false;

        // Category sidebar
        private int selectedCategory = 0; // 0 = Currently Affecting, 1 = Gear, 2 = Hair, etc.
        private readonly string[] categoryNames = {
            "Currently Affecting You", "Gear", "Hair", "Bodies", "Tattoos",
            "Eyes", "Ears/Horns/Tails", "Makeup/Face Paint", "Sculpts", "Mounts/Minions", "Standing Idle", "Chair Sitting", "Ground Sitting", "Lying/Dozing", "Mixed Idle", "Emotes", "Movement", "Job VFX", "VFX", "Skeletons", "Other"
        };
        private readonly ModType[] categoryTypes = {
            ModType.Unknown, ModType.Gear, ModType.Hair, ModType.Body, ModType.Tattoos,
            ModType.Eyes, ModType.EarsTails, ModType.FacePaint, ModType.Face, ModType.Mount, ModType.StandingIdle, ModType.ChairSitting, ModType.GroundSitting, ModType.LyingDozing, ModType.MixedIdle, ModType.Emote, ModType.Movement, ModType.JobVFX, ModType.VFX, ModType.Skeleton, ModType.Other
        };

        // Pinned mods (never disabled)
        private HashSet<string> pinnedMods = new();

        // Tracks which mod's state-combo popup is currently open (only one at a
        // time across all rows). Cleared when the popup closes.
        private string? openStateComboKey = null;

        // Wardrobe-style page transition: when currentPage changes, the
        // previous page's gold border/bg fades out and the new page's fades
        // in over PageTransitionDur (ease-out-cubic). Mirrors the pattern in
        // CharacterGrid.PageTransitionT used by MainWindow's pager dots.
        private int pagePrevIdx = 0;
        private double pageTransitionStart = -1;
        private const double PageTransitionDur = 0.28;

        private bool IsPageTransitioning
        {
            get
            {
                if (pageTransitionStart < 0) return false;
                if (ImGui.GetTime() - pageTransitionStart >= PageTransitionDur)
                {
                    pageTransitionStart = -1;
                    return false;
                }
                return true;
            }
        }

        /// <summary>0..1 ease-out-cubic progress of the in-flight page transition.</summary>
        private float PageTransitionT
        {
            get
            {
                if (!IsPageTransitioning) return 1f;
                float u = (float)((ImGui.GetTime() - pageTransitionStart) / PageTransitionDur);
                u = Math.Clamp(u, 0f, 1f);
                return 1f - MathF.Pow(1f - u, 3f);
            }
        }

        private void TriggerPageChange(int newPage)
        {
            if (newPage == currentPage) return;
            pagePrevIdx = currentPage;
            pageTransitionStart = ImGui.GetTime();
            currentPage = newPage;
            categoryPageNumbers[selectedCategory] = currentPage;
        }

        // Contextual warning system
        private HashSet<string> dismissedWarnings = new();

        // Mod options panel state
        private ModEntry? optionsEditingMod = null;
        private Dictionary<string, List<string>>? availableModOptions = null;
        private Dictionary<string, List<string>>? currentModOptions = null;
        private bool shouldOpenOptionsPopup = false;
        private bool isOptionsPopupOpen = true;
        private Dictionary<string, int>? optionGroupTypes = null; // 0=Single, 1=Multi

        // Performance cache for mod options (prevents overwhelming Penumbra with 7000+ mods)
        private Dictionary<string, bool> modOptionsCache = new();

        // Progress tracking for async loading
        private float loadingProgress = 0f;
        private string loadingStatus = "";
        private int totalModsToLoad = 0;
        private int modsLoaded = 0;
        private CancellationTokenSource? loadingCancellation = null;

        // Enhanced loading UI
        private string currentLoadingMessage = "";
        private DateTime lastMessageChange = DateTime.Now;
        private int lastMessageIndex = -1;
        private float loadingPanelAlpha = 0f;
        private DateTime loadingStartTime = DateTime.Now;
        private readonly Random messageRandom = new Random();

        // Multi-stage progress tracking
        private enum LoadingStage
        {
            Initializing = 0,
            LoadingMods = 1,
            AnalyzingDependencies = 2,
            Finalizing = 3,
            Complete = 4
        }

        private LoadingStage currentLoadingStage = LoadingStage.Initializing;
        private float stageProgress = 0f;

        // Loading message pools
        private readonly string[] generalLoadingMessages = {
            "Convincing mods they want to be organized...",
            "Bribing Penumbra with digital cookies...",
            "Untangling the mod spaghetti...",
            "Asking each mod 'What do you actually do?'",
            "Counting pixels... there are many...",
            "Negotiating peace treaties between conflicting textures...",
            "Playing mod Jenga (try not to crash)...",
            "Converting chaos into organized chaos...",
            "Performing mod archaeology on your collection...",
            "Summoning the mod primals (please don't wipe)...",
            "Rolling for mod compatibility... Natural 20!",
            "Converting chaos into slightly less chaos...",
            "Herding digital cats with very strong opinions...",
            "Explaining to mods why they can't all be first...",
            "Mediating disputes between texture files...",
            "Teaching models basic conflict resolution...",
            "Organizing the digital equivalent of a sock drawer...",
            "Asking textures to share nicely...",
            "Performing digital feng shui on your mods...",
            "Convincing animations to behave themselves...",
            "Sorting through years of digital hoarding...",
            "Playing 4D chess with file dependencies...",
            "Teaching old mods new tricks...",
            "Debugging someone else's creative decisions...",
            "Calculating the meaning of mod life...",
            "Asking Penumbra very nicely to cooperate...",
            "Preventing a texture uprising...",
            "Organizing a digital fashion show...",
            "Cataloguing crimes against good taste...",
            "Teaching files the alphabet (for sorting)...",
            "Negotiating with stubborn model files...",
            "Explaining priority systems to confused mods...",
            "Untying knots in the dependency web...",
            "Convincing textures to render correctly...",
            "Debugging the debugging tools...",
            "Asking the internet why this always happens...",
            "Performing miracles with questionable file structures...",
            "Teaching computers the concept of patience...",
            "Solving mysteries that Sherlock Holmes would quit...",
            "Organizing digital chaos one file at a time...",
            "Explaining to files why they need to have manners...",
            "Teaching textures basic social skills...",
            "Mediating family disputes between related mods...",
            "Asking the file system to please just work...",
            "Converting spaghetti code into organized spaghetti...",
            "Explaining modern file etiquette to legacy mods...",
            "Performing digital archaeology on ancient downloads...",
            "Teaching priority queues to actual priority systems...",
            "Asking very nicely for everything to just work...",
            "Organizing a support group for conflicted textures...",
            "Explaining to mods that sharing is caring...",
            "Teaching file systems advanced mathematics...",
            "Negotiating ceasefires between warring animations...",
            "Asking politely for the laws of physics to apply...",
            "Converting theoretical file structures into reality...",
            "Teaching databases the concept of organization...",
            "Mediating disputes in the texture parliament...",
            "Explaining to files why alphabetical order exists...",
            "Teaching models basic conflict avoidance...",
            "Asking the computer gods for patience and wisdom...",
            "Converting digital nightmares into manageable dreams...",
            "Explaining to mods that categories aren't suggestions...",
            "Organizing a intervention for hoarding behaviors...",
            "Teaching file extensions the meaning of identity..."
        };

        private readonly string[] nearEndMessages = {
            "Just kidding, we're only 50% done...",
            "Almost there! (Narrator: They were not almost there)",
            "This progress bar is more accurate than a DPS meter...",
            "Loading bars: the ultimate trust exercise...",
            "The progress bar is having an existential crisis...",
            "We're 99% done with 90% of the work...",
            "Progress bars are more like progress suggestions...",
            "Almost finished lying about being almost finished...",
            "The progress bar is taking creative liberties...",
            "We're definitely maybe almost done..."
        };

        public SecretModeModWindow(Plugin plugin) : base(
            "Mod Manager",
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            this.plugin = plugin;
            this.uiStyles = new UIStyles(plugin);
            // Bumped minimum height so the ribbon + toolbar + main + pagination
            // + footer all fit without ImGui auto-injecting a window scrollbar
            // (which clips the footer). Mockup target is 1080×720.
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(960, 720),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };
        }

        private int _chromeColorCount = 0;
        public override void PreDraw()
        {
            if (Plugin.UseClassicLayout) return;
            // WindowBg / TitleBg commit at ImGui.Begin time, must be pushed
            // BEFORE Begin so the title bar respects the theme.
            _chromeColorCount = CharacterSelectPlugin.Windows.Styles.ThemeHelper.PushWindowChromeColors(plugin.Configuration);
        }

        public override void PostDraw()
        {
            if (_chromeColorCount == 0) return;
            CharacterSelectPlugin.Windows.Styles.ThemeHelper.PopWindowChromeColors(_chromeColorCount);
            _chromeColorCount = 0;
        }

        public void Open(int? characterIndex = null, Dictionary<string, bool>? existingSelection = null, HashSet<string>? existingPins = null, Action<Dictionary<string, bool>>? saveCallback = null, Action<HashSet<string>>? savePinsCallback = null, CharacterDesign? design = null, string? characterName = null, Action<HashSet<string>>? saveInheritCallback = null)
        {
            Plugin.Log.Information($"[PIN DEBUG] Open method received existingPins parameter: {existingPins?.Count ?? -1} pins - {string.Join(", ", existingPins ?? new HashSet<string>())} (null: {existingPins == null})");
            // Cancel any existing loading operation
            loadingCancellation?.Cancel();
            loadingCancellation?.Dispose();

            IsOpen = true;
            editingCharacterIndex = characterIndex;
            editingDesign = design;
            editingCharacterName = characterName;
            onSave = saveCallback;
            onSavePins = savePinsCallback;
            onSaveInherit = saveInheritCallback;
            userHasSelectedCollection = false; // Reset on each open to allow fresh auto-detection

            // Initialize with existing selection if provided
            selectedMods.Clear();
            originalSelection.Clear();
            modsToInherit.Clear();
            if (existingSelection != null)
            {
                foreach (var kvp in existingSelection)
                {
                    selectedMods[kvp.Key] = kvp.Value;
                    originalSelection[kvp.Key] = kvp.Value;
                }
            }

            // Initialize with existing pins if provided
            pinnedMods.Clear();
            if (existingPins != null)
            {
                Plugin.Log.Information($"[PIN DEBUG] Loading {existingPins.Count} existing pins: {string.Join(", ", existingPins)}");
                foreach (var pin in existingPins)
                {
                    pinnedMods.Add(pin);
                    // Automatically check pinned mods
                    selectedMods[pin] = true;
                }
            }
            else
            {
                Plugin.Log.Information("[PIN DEBUG] No existing pins provided");
            }

            // Initialize loading animations
            loadingStartTime = DateTime.Now;
            loadingPanelAlpha = 0f;
            currentLoadingMessage = "";
            lastMessageIndex = -1;

            // Create new cancellation token
            loadingCancellation = new CancellationTokenSource();
            _ = LoadCurrentMods();
        }

        private async Task LoadCurrentMods()
        {
            isLoading = true;
            availableMods.Clear();

            // Reset progress tracking
            loadingProgress = 0f;
            loadingStatus = "Initializing...";
            totalModsToLoad = 0;
            modsLoaded = 0;
            currentLoadingStage = LoadingStage.Initializing;
            stageProgress = 0f;

            try
            {
                // Removed debug log to reduce spam

                // Check if Penumbra integration is available
                if (plugin.PenumbraIntegration?.IsPenumbraAvailable != true)
                {
                    Plugin.Log.Warning("[SecretMode] Penumbra integration not available");
                    return;
                }

                // Get all available collections first
                availableCollections = plugin.PenumbraIntegration.GetAvailableCollections();
                // Available collections count logged when needed

                // Only auto-detect collection if user hasn't manually selected one
                if (!userHasSelectedCollection)
                {
                    var (success, detectedCollectionId, detectedCollectionName) = plugin.PenumbraIntegration.GetPlayerCollection();

                    if (success)
                    {
                        currentCollectionId = detectedCollectionId;
                        currentCollectionName = detectedCollectionName;
                        // Auto-detected player collection (log removed to prevent spam)

                        // Find the index in available collections for UI dropdown
                        var collectionsList = availableCollections.ToList();
                        selectedCollectionIndex = collectionsList.FindIndex(kvp => kvp.Key == currentCollectionId);
                        if (selectedCollectionIndex < 0) selectedCollectionIndex = 0;
                    }
                    else
                    {
                        // Could not auto-detect player collection (log removed to prevent spam)
                        if (availableCollections.Any())
                        {
                            var firstCollection = availableCollections.First();
                            currentCollectionId = firstCollection.Key;
                            currentCollectionName = firstCollection.Value;
                            selectedCollectionIndex = 0;
                            // Default collection selection (reduced logging)
                        }
                    }
                }
                else
                {
                    // Using user-selected collection (log removed to prevent spam)

                    // Ensure the dropdown index is correct for user-selected collection
                    var collectionsList = availableCollections.ToList();
                    selectedCollectionIndex = collectionsList.FindIndex(kvp => kvp.Key == currentCollectionId);
                    if (selectedCollectionIndex < 0)
                    {
                        selectedCollectionIndex = 0;
                        // User-selected collection not found, defaulting to index 0 (log removed to prevent spam)
                    }
                }

                // Get mod list for names
                var modList = plugin.PenumbraIntegration.GetModList();
                // Display mode set (reduced logging)

                // Always load ALL mods for proper categorization
                if (currentCollectionId == Guid.Empty || !availableCollections.Any())
                {
                    // No valid collection ID - showing all mods (log removed to prevent spam)
                    await LoadAllModsSimple(modList);
                }
                else
                {
                    // Load all mods and mark which ones are currently affecting
                    await LoadAllModsWithAffectingStatus(modList, currentCollectionId);
                }

                // LoadCurrentMods completed (log removed to prevent spam)

                // Detect dependencies after all mods are loaded
                DetectAllModDependencies();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[SecretMode] Error loading mods: {ex}");
                availableMods.Clear(); // Ensure empty list for proper error display
            }
            finally
            {
                // Final stage before completing
                currentLoadingStage = LoadingStage.Finalizing;
                stageProgress = 1.0f;
                UpdateOverallProgress();
                loadingStatus = "Finalizing...";

                // Small delay to show completion
                await Task.Delay(200);

                isLoading = false;

                // Ensure pinned mods remain selected after async loading
                foreach (var pin in pinnedMods)
                {
                    selectedMods[pin] = true;
                }
            }
        }

        private async Task LoadCurrentlyAffectingMods(Dictionary<string, string> modList, Guid collectionId)
        {
            // Loading currently affecting mods
            // Using On-Screen tab data

            // Mod list loaded

            // Get the mods that are ACTUALLY affecting the character right now (On-Screen tab equivalent)
            var affectingMods = plugin.PenumbraIntegration?.GetOnScreenTabMods();

            // Debug the affecting mods result
            if (affectingMods == null)
            {
                Plugin.Log.Error("[SecretMode] GetOnScreenTabMods returned null");
            }
            else
            {
                // GetOnScreenTabMods returned affecting mods (log removed to prevent spam)
                if (affectingMods.Any())
                {
                    // First 5 affecting mods (log removed to prevent spam)
                }
            }

            // Always show some mods - if we can't determine what's affecting, show all enabled
            if (affectingMods == null || !affectingMods.Any())
            {
                // No currently affecting mods found, showing all enabled mods instead (log removed to prevent spam)

                // Fallback to the original method if the new one doesn't work yet
                var allModsChangedItems = plugin.PenumbraIntegration?.GetAllModsChangedItems();
                if (allModsChangedItems == null || !allModsChangedItems.Any())
                {
                    // No changed items data available from Penumbra (log removed to prevent spam)
                    return;
                }

                // Fallback method active

                // Get mod settings to check enabled status and priorities
                var fallbackModSettings = plugin.PenumbraIntegration?.GetAllModSettingsRobust(collectionId);

                if (fallbackModSettings == null)
                {
                    // Could not get mod settings in fallback - using simple load (log removed to prevent spam)
                    await LoadAllModsSimple(modList);
                    return;
                }

                var fallbackAffectingMods = new HashSet<string>();
                foreach (var (modDir, changedItems) in allModsChangedItems)
                {
                    // Only include if the mod is enabled and has changes
                    if (fallbackModSettings.ContainsKey(modDir) && fallbackModSettings[modDir].Item1 && changedItems.Any())
                    {
                        fallbackAffectingMods.Add(modDir);
                        // Mod affecting items (reduced logging)
                    }
                }

                // Fallback affecting mods found

                // Create entries for affecting mods using fallback data
                await CreateModEntries(modList, fallbackModSettings, fallbackAffectingMods, true);
                return;
            }

            // Found currently affecting mods via On-Screen tab data (log removed to prevent spam)

            var modListKeys = modList.Keys.ToHashSet();
            var intersection = affectingMods.Intersect(modListKeys).ToList();
            // On-Screen tab found affecting mods (log removed to prevent spam)

            // Get mod settings to get priorities and other info
            var modSettings = plugin.PenumbraIntegration?.GetAllModSettingsRobust(collectionId);

            if (modSettings == null)
            {
                // Could not get mod settings - using simple load (log removed to prevent spam)
                await LoadAllModsSimple(modList);
                return;
            }

            // Create entries for the mods that are actually affecting the character
            await CreateModEntries(modList, modSettings, affectingMods, true);
        }

        private async Task LoadEnabledMods(Dictionary<string, string> modList, Guid collectionId)
        {
            try
            {
                // Get mod settings using robust method
                var modSettings = plugin.PenumbraIntegration?.GetAllModSettingsRobust(collectionId);

                if (modSettings == null)
                {
                    // Could not get mod settings for collection (log removed to prevent spam)
                    await LoadAllModsSimple(modList);
                    return;
                }

                // Mod settings retrieved

                // Only show mods that are ENABLED in this specific collection
                var enabledMods = modSettings
                    .Where(kvp => kvp.Value.Item1) // Only enabled mods
                    .Select(kvp => kvp.Key)
                    .ToHashSet();

                // Found enabled mods in collection (log removed to prevent spam)

                await CreateModEntries(modList, modSettings, enabledMods, false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[SecretMode] Error loading enabled mods: {ex}");
                // Fall back to simple loading
                await LoadAllModsSimple(modList);
            }
        }

        private async Task LoadAllModsWithAffectingStatus(Dictionary<string, string> modList, Guid collectionId)
        {
            try
            {
                // Loading all mods with affecting status

                // Update loading status
                loadingStatus = "Getting currently affecting mods...";
                await Task.Yield(); // Allow UI to update

                // Get what mods are currently affecting the character
                var affectingMods = plugin.PenumbraIntegration?.GetCurrentlyAffectingMods(collectionId) ?? new HashSet<string>();
                // Found currently affecting mods (log removed to prevent spam)

                // Debug: Log first few affecting mods
                if (affectingMods.Any())
                {
                    var firstFew = affectingMods.Take(5).ToList();
                    // First few affecting mods (log removed to prevent spam)
                }
                else
                {
                    // No affecting mods detected (log removed to prevent spam)
                }

                // Update loading status
                loadingStatus = "Getting mod settings...";
                await Task.Yield(); // Allow UI to update

                // Get mod settings using robust method
                var modSettings = plugin.PenumbraIntegration?.GetAllModSettingsRobust(collectionId);

                if (modSettings == null)
                {
                    // Could not get mod settings for all mods (log removed to prevent spam)
                    await LoadAllModsSimple(modList);
                    return;
                }

                // All mod settings retrieved

                // For the category system, we want to show ALL mods from the mod list
                var allMods = modList.Keys.ToHashSet();
                totalModsToLoad = allMods.Count;
                loadingStatus = $"Processing {totalModsToLoad} mods...";

                await CreateModEntriesWithAffectingStatus(modList, modSettings, allMods, affectingMods);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[SecretMode] Error loading all mods with affecting status: {ex}");
                // Fall back to simple loading
                await LoadAllModsSimple(modList);
            }
        }

        private async Task LoadAllMods(Dictionary<string, string> modList, Guid collectionId)
        {
            try
            {
                // Get mod settings using robust method
                var modSettings = plugin.PenumbraIntegration?.GetAllModSettingsRobust(collectionId);

                if (modSettings == null)
                {
                    // Could not get mod settings for all mods (log removed to prevent spam)
                    await LoadAllModsSimple(modList);
                    return;
                }

                // Loading all mods - got settings (log removed to prevent spam)

                // For "All Mods" mode, we want to show ALL mods from the mod list, not just ones with settings
                var allMods = modList.Keys.ToHashSet();
                // Total mods in mod list (log removed to prevent spam)

                await CreateModEntries(modList, modSettings, allMods, false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[SecretMode] Error loading all mods: {ex}");
                // Fall back to simple loading
                await LoadAllModsSimple(modList);
            }
        }

        private async Task LoadAllModsSimple(Dictionary<string, string> modList)
        {
            // Simple fallback when we can't get collection information - show ALL mods
            foreach (var (modDir, modName) in modList)
            {
                // Determine mod type using path-based detection only
                var modType = DetermineModTypeFromPaths(modDir, modName, null);

                var entry = new ModEntry
                {
                    Directory = modDir,
                    Name = modName,
                    IsEnabled = false, // We don't know the actual status
                    Categories = new List<string>(), // No longer using old categories
                    IsBlacklisted = plugin.Configuration.SecretModeBlacklistedMods.Contains(modDir),
                    Priority = 0, // We don't know the actual priority
                    IsCurrentlyAffecting = false, // We don't know this either
                    ModType = modType
                };

                availableMods.Add(entry);

                // Don't add to selectedMods - mods not in selectedMods = "Don't Change"
                // Only mods explicitly toggled by the user should be in selectedMods
            }

            await Task.CompletedTask;
        }

        private async Task CreateModEntriesWithAffectingStatus(Dictionary<string, string> modList, Dictionary<string, (bool, int, Dictionary<string, List<string>>, bool, bool)> modSettings, HashSet<string> allMods, HashSet<string> affectingMods)
        {
            // Creating mod entries with affecting status

            var modsList = allMods.ToList();
            var batchSize = 25; // Process 25 mods at a time for better performance
            var processedCount = 0;

            for (int i = 0; i < modsList.Count; i += batchSize)
            {
                // Check for cancellation
                if (loadingCancellation?.Token.IsCancellationRequested == true)
                {
                    // Mod loading cancelled (log removed to prevent spam)
                    return;
                }

                var batch = modsList.Skip(i).Take(batchSize).ToList();

                foreach (var modDir in batch)
                {
                    var modName = modList.ContainsKey(modDir) ? modList[modDir] : modDir;

                    // Use cached categorization if available, otherwise analyze
                    var modType = ModType.Unknown;
                    if (plugin.modCategorizationCache != null && plugin.modCategorizationCache.ContainsKey(modDir))
                    {
                        modType = plugin.modCategorizationCache[modDir];
                    }
                    else
                    {
                        // Fallback to expensive method only if not in cache
                        modType = DetermineModTypeFromPaths(modDir, modName, null);
                    }

                    // Debug log only first 5 mods for categorization consistency - removed to reduce spam

                    // Check if this mod has settings in the current collection
                    bool hasSettings = modSettings.ContainsKey(modDir);
                    var settings = hasSettings ? modSettings[modDir] : (false, 0, new Dictionary<string, List<string>>(), false, false);

                    // Check if this mod is currently affecting the character
                    bool isCurrentlyAffecting = affectingMods.Contains(modDir);

                    // Analyze for dependencies and conflicts
                    var conflictAnalysis = plugin.PenumbraIntegration?.AnalyzeModForDependenciesAndConflicts(
                        modDir, modName, modType, selectedMods);

                    var entry = new ModEntry
                    {
                        Directory = modDir,
                        Name = modName,
                        IsEnabled = settings.Item1,
                        Categories = new List<string>(), // No longer using old categories
                        IsBlacklisted = plugin.Configuration.SecretModeBlacklistedMods.Contains(modDir),
                        Priority = settings.Item2,
                        IsCurrentlyAffecting = isCurrentlyAffecting,
                        ModType = modType,
                        IsInherited = settings.Item4,
                        HasDependency = conflictAnalysis?.HasDependency ?? false,
                        DependencyType = conflictAnalysis?.DependencyType ?? "",
                        HasConflicts = conflictAnalysis?.HasConflicts ?? false,
                        ConflictingMods = conflictAnalysis?.ConflictingMods ?? new List<string>()
                    };

                    availableMods.Add(entry);

                    // Don't add to selectedMods - mods not in selectedMods = "Don't Change"
                    // Only mods explicitly toggled by the user should be in selectedMods

                    processedCount++;
                }

                // Update progress with multi-stage calculation
                modsLoaded = processedCount;
                currentLoadingStage = LoadingStage.LoadingMods;
                stageProgress = (float)processedCount / totalModsToLoad;
                UpdateOverallProgress();
                loadingStatus = $"Processing mods... ({processedCount}/{totalModsToLoad})";

                // Yield to allow UI updates - reduced delay for batch processing
                await Task.Delay(10, loadingCancellation.Token);
            }

            // Created mod entries (log removed to prevent spam)
        }

        private async Task CreateModEntries(Dictionary<string, string> modList, Dictionary<string, (bool, int, Dictionary<string, List<string>>, bool, bool)> modSettings, HashSet<string> modsToInclude, bool markAsAffecting)
        {
            // Creating mod entries

            var modsList = modsToInclude.ToList();
            var batchSize = 25; // Process 25 mods at a time for better performance
            var processedCount = 0;

            // Update total if not already set
            if (totalModsToLoad == 0) totalModsToLoad = modsList.Count;

            for (int i = 0; i < modsList.Count; i += batchSize)
            {
                // Check for cancellation
                if (loadingCancellation?.Token.IsCancellationRequested == true)
                {
                    // Mod loading cancelled (log removed to prevent spam)
                    return;
                }

                var batch = modsList.Skip(i).Take(batchSize).ToList();

                foreach (var modDir in batch)
                {
                    var modName = modList.ContainsKey(modDir) ? modList[modDir] : modDir;

                    // Use cached categorization if available, otherwise analyze
                    var modType = ModType.Unknown;
                    if (plugin.modCategorizationCache != null && plugin.modCategorizationCache.ContainsKey(modDir))
                    {
                        modType = plugin.modCategorizationCache[modDir];
                    }
                    else
                    {
                        // Fallback to expensive method only if not in cache
                        modType = DetermineModTypeFromPaths(modDir, modName, null);
                    }

                    // Check if this mod has settings in the current collection
                    bool hasSettings = modSettings.ContainsKey(modDir);
                    var settings = hasSettings ? modSettings[modDir] : (false, 0, new Dictionary<string, List<string>>(), false, false);

                    // Analyze for dependencies and conflicts
                    var conflictAnalysis = plugin.PenumbraIntegration?.AnalyzeModForDependenciesAndConflicts(
                        modDir, modName, modType, selectedMods);

                    var entry = new ModEntry
                    {
                        Directory = modDir,
                        Name = modName,
                        IsEnabled = settings.Item1,
                        Categories = new List<string>(), // No longer using old categories
                        IsBlacklisted = plugin.Configuration.SecretModeBlacklistedMods.Contains(modDir),
                        Priority = settings.Item2,
                        IsCurrentlyAffecting = markAsAffecting,
                        ModType = modType,
                        IsInherited = settings.Item4,
                        HasDependency = conflictAnalysis?.HasDependency ?? false,
                        DependencyType = conflictAnalysis?.DependencyType ?? "",
                        HasConflicts = conflictAnalysis?.HasConflicts ?? false,
                        ConflictingMods = conflictAnalysis?.ConflictingMods ?? new List<string>()
                    };

                    availableMods.Add(entry);

                    // Don't add to selectedMods - mods not in selectedMods = "Don't Change"
                    // Only mods explicitly toggled by the user should be in selectedMods

                    processedCount++;
                }

                // Update progress with multi-stage calculation
                modsLoaded = processedCount;
                currentLoadingStage = LoadingStage.LoadingMods;
                stageProgress = (float)processedCount / totalModsToLoad;
                UpdateOverallProgress();
                loadingStatus = $"Processing mods... ({processedCount}/{totalModsToLoad})";

                // Yield to allow UI updates - reduced delay for batch processing
                await Task.Delay(10, loadingCancellation.Token);
            }

            // Created mod entries (log removed to prevent spam)
        }

        // Helper methods for new UI
        private int GetModCountForCategory(int categoryIndex)
        {
            if (categoryIndex == 0) // Currently Affecting You
            {
                // Count must match the filtering logic in GetFilteredModsForSelectedCategory
                return availableMods.Count(m => m.IsCurrentlyAffecting &&
                    (m.ModType == ModType.Gear || m.ModType == ModType.Hair ||
                     m.ModType == ModType.Eyes || m.ModType == ModType.Tattoos ||
                     m.ModType == ModType.EarsTails || m.ModType == ModType.FacePaint));
            }

            var categoryType = categoryTypes[categoryIndex];
            return GetModsForCategory(categoryType).Count();
        }

        private List<ModEntry> GetModsForCategory(ModType categoryType)
        {
            return availableMods.Where(m =>
                categoryType == ModType.Unknown ||
                m.ModType == categoryType ||
                (categoryType == ModType.Other && (m.ModType == ModType.Unknown || m.ModType == ModType.Other))
            ).ToList();
        }

        private Vector4 GetTypeColor(ModType modType)
        {
            return modType switch
            {
                ModType.Gear => ColorSchemes.Dark.AccentYellow,
                ModType.Hair => new Vector4(0.8f, 0.4f, 0.8f, 1.0f),
                ModType.Body => ColorSchemes.Dark.AccentBlue,
                ModType.Face => new Vector4(1.0f, 0.6f, 0.8f, 1.0f),
                ModType.Eyes => new Vector4(0.4f, 0.8f, 1.0f, 1.0f),
                ModType.Tattoos => new Vector4(1.0f, 0.4f, 0.6f, 1.0f),
                ModType.FacePaint => new Vector4(0.9f, 0.7f, 0.3f, 1.0f),
                ModType.EarsTails => new Vector4(0.6f, 0.8f, 0.6f, 1.0f),
                ModType.Mount => new Vector4(0.8f, 0.6f, 0.4f, 1.0f),
                ModType.Minion => new Vector4(0.6f, 0.4f, 0.8f, 1.0f),
                ModType.Emote => new Vector4(0.5f, 1.0f, 0.5f, 1.0f),
                ModType.StandingIdle => new Vector4(0.7f, 0.9f, 0.7f, 1.0f),
                ModType.ChairSitting => new Vector4(0.6f, 0.8f, 0.9f, 1.0f),
                ModType.GroundSitting => new Vector4(0.8f, 0.7f, 0.6f, 1.0f),
                ModType.LyingDozing => new Vector4(0.9f, 0.6f, 0.9f, 1.0f),
                ModType.MixedIdle => new Vector4(0.8f, 0.8f, 0.8f, 1.0f),
                ModType.Movement => new Vector4(0.4f, 0.8f, 0.4f, 1.0f),
                ModType.JobVFX => new Vector4(1.0f, 0.7f, 0.3f, 1.0f),
                ModType.VFX => new Vector4(1.0f, 0.5f, 0.5f, 1.0f),
                ModType.Skeleton => new Vector4(0.7f, 0.7f, 1.0f, 1.0f),
                _ => ColorSchemes.Dark.TextMuted
            };
        }

        private string GetTypeIcon(ModType modType)
        {
            return modType switch
            {
                ModType.Gear => FontAwesomeIcon.Tshirt.ToIconString(),
                ModType.Hair => FontAwesomeIcon.Cut.ToIconString(),
                ModType.Face => FontAwesomeIcon.Smile.ToIconString(),
                ModType.Eyes => FontAwesomeIcon.Eye.ToIconString(),
                ModType.Tattoos => FontAwesomeIcon.Palette.ToIconString(),
                ModType.FacePaint => FontAwesomeIcon.PaintBrush.ToIconString(),
                ModType.Body => FontAwesomeIcon.User.ToIconString(),
                ModType.EarsTails => FontAwesomeIcon.Cat.ToIconString(),
                ModType.Mount => FontAwesomeIcon.Horse.ToIconString(),
                ModType.Minion => FontAwesomeIcon.Dragon.ToIconString(),
                ModType.Emote => FontAwesomeIcon.HandPaper.ToIconString(),
                ModType.StandingIdle => FontAwesomeIcon.Male.ToIconString(),
                ModType.ChairSitting => FontAwesomeIcon.Chair.ToIconString(),
                ModType.GroundSitting => FontAwesomeIcon.Mountain.ToIconString(),
                ModType.LyingDozing => FontAwesomeIcon.Bed.ToIconString(),
                ModType.MixedIdle => FontAwesomeIcon.LayerGroup.ToIconString(),
                ModType.Movement => FontAwesomeIcon.Running.ToIconString(),
                ModType.JobVFX => FontAwesomeIcon.Star.ToIconString(),
                ModType.VFX => FontAwesomeIcon.Magic.ToIconString(),
                ModType.Skeleton => FontAwesomeIcon.Bone.ToIconString(),
                _ => FontAwesomeIcon.PuzzlePiece.ToIconString()
            };
        }

        public override void Draw()
        {
            if (Plugin.UseClassicLayout) { DrawClassicLayout(); return; }
            // Update window title based on current context
            var contextTitle = GetContextualWindowTitle();
            if (WindowName != contextTitle)
            {
                WindowName = contextTitle;
            }

            // Boutique form style for inputs/buttons inside. WindowBg itself
            // is pushed in PreDraw (must commit before Begin).
            CharacterSelectPlugin.Windows.Styles.Boutique.PushFormStyle();
            // Outer ChildBg transparent so the window's Surface0 bg shows
            // through, matches every other boutique surface in the plugin.
            ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);

            try
            {
                if (isLoading)
                {
                    DrawLoadingState();
                    return;
                }

                DrawHeader();
                DrawMainContent();
                DrawBottomButtons();

                DrawModOptionsPopup();
            }
            finally
            {
                ImGui.PopStyleColor();
                CharacterSelectPlugin.Windows.Styles.Boutique.PopFormStyle();
            }
        }

        private string GetContextualWindowTitle()
        {
            return "Mod Manager";
        }

        private void DrawLoadingState()
        {
            UpdateLoadingAnimations();

            float scale = (Plugin.Instance?.Configuration?.UIScaleMultiplier ?? 1f);
            scale = MathF.Max(0.85f, MathF.Min(scale, 2.0f));

            var dl = ImGui.GetWindowDrawList();
            var origin = ImGui.GetCursorScreenPos();
            var availSize = ImGui.GetContentRegionAvail();
            float t = (float)ImGui.GetTime();

            // Pre-measure the percent + count text block so we can centre the
            // readout inside the ring properly. The percent uses OswaldSemiTitle
            // (bigger than SemiBig, fills the disc more dominantly) and the
            // count uses OswaldMed13 (small secondary line below the ring).
            string pctStr = $"{(int)(Math.Clamp(loadingProgress, 0f, 1f) * 100f)}%";
            string countStr = $"{modsLoaded}/{totalModsToLoad}";
            Vector2 pctSz, cSz;
            float pctFontSize;
            using (Plugin.Instance?.OswaldSemiTitle?.Push())
            {
                pctSz = ImGui.CalcTextSize(pctStr);
                pctFontSize = ImGui.GetFontSize();
            }
            using (Plugin.Instance?.OswaldMed13?.Push()) cSz = ImGui.CalcTextSize(countStr);

            // Pre-measure the title + message block sizes.
            Vector2 titleSz, msgSz;
            using (Plugin.Instance?.OswaldSemiSmall?.Push())
                titleSz = ImGui.CalcTextSize("LOADING MOD INFORMATION");
            using (Plugin.Instance?.OutfitMed13?.Push())
                msgSz = string.IsNullOrEmpty(currentLoadingMessage)
                    ? Vector2.Zero
                    : ImGui.CalcTextSize(currentLoadingMessage);

            // Total content height = ring diameter + below-ring stack
            float ringRadius = 100f * scale;
            float ringThickness = 19f * scale;
            float ringDiameter = ringRadius * 2f;
            float ringToTitle = 44f * scale;
            float titleToMsg = string.IsNullOrEmpty(currentLoadingMessage) ? 0f : 16f * scale;
            float msgToBtn = 36f * scale;
            float btnH = 30f * scale;
            float totalH = ringDiameter + ringToTitle + titleSz.Y
                         + (string.IsNullOrEmpty(currentLoadingMessage) ? 0f : titleToMsg + msgSz.Y)
                         + msgToBtn + btnH;

            // Vertically centre the entire block in the available space.
            float startY = origin.Y + MathF.Max(0f, (availSize.Y - totalH) * 0.5f);
            var centre = new Vector2(
                origin.X + availSize.X * 0.5f,
                startY + ringRadius);

            // Glitch flair plays for the entire load, not just a 1.5s wind-up.
            // fillProgress is tied to loadingProgress so chromatic ghost arcs
            // + dropout bars + glitch specks keep playing while data is
            // streaming, and only settle to clean gold once the load reaches
            // 100%.
            float displayedRatio = Math.Clamp(loadingProgress, 0f, 1f);
            float fillProgress = displayedRatio; // < 1 while loading, 1 at completion
            int fillSeed = unchecked(((int)(loadingStartTime.Ticks & 0x7FFFFFFF)) ^ 0xBEEF);

            // Use the shared helper, same call path the achievement vault
            // uses for its ring (bloom + halo + chromatic flair + inner disc
            // are all painted inside DrawProgressRing).
            CharacterSelectPlugin.Windows.Styles.Boutique.DrawProgressRing(
                dl, centre, scale, ringRadius, ringThickness,
                displayedRatio: displayedRatio,
                fillProgress: fillProgress,
                fillSeed: fillSeed,
                time: t);

            // Centred readout inside the disc. Tiny right bias because "%"'s
            // wide arms shift the visible mass off geometric centre.
            using (Plugin.Instance?.OswaldSemiTitle?.Push())
            {
                float pctY = centre.Y - pctSz.Y * 0.5f;
                float pctX = centre.X - pctSz.X * 0.5f + pctSz.X * 0.02f;
                dl.AddText(
                    new Vector2(pctX, pctY),
                    ImGui.ColorConvertFloat4ToU32(Boutique.GoldWarm),
                    pctStr);
            }

            // ── Below the ring: tracked-caps title + witty message ──
            float belowY = centre.Y + ringRadius + ringToTitle;
            using (Plugin.Instance?.OswaldSemiSmall?.Push())
            {
                string title = "LOADING MOD INFORMATION";
                float trackPx = ImGui.GetFontSize() * 0.32f;
                float titleW = CharacterSelectPlugin.Windows.Styles.Boutique
                    .MeasureTrackedText(title, trackPx);
                CharacterSelectPlugin.Windows.Styles.Boutique.DrawTrackedText(
                    dl, new Vector2(centre.X - titleW * 0.5f, belowY),
                    title, ImGui.ColorConvertFloat4ToU32(Boutique.Text), trackPx);
            }
            // Mods-loaded count (X / Y) sits just under the title, was inside
            // the ring next to the %, but that competed with the % for the
            // ring centre. Now the % owns the disc by itself.
            using (Plugin.Instance?.OswaldMed13?.Push())
            {
                float cY = belowY + titleSz.Y + 4f * scale;
                dl.AddText(
                    new Vector2(centre.X - cSz.X * 0.5f, cY),
                    ImGui.ColorConvertFloat4ToU32(Boutique.TextDim),
                    countStr);
            }
            float msgY = belowY + titleSz.Y + 4f * scale + cSz.Y + titleToMsg;
            if (!string.IsNullOrEmpty(currentLoadingMessage))
            {
                using (Plugin.Instance?.OutfitMed13?.Push())
                {
                    dl.AddText(
                        new Vector2(centre.X - msgSz.X * 0.5f, msgY),
                        ImGui.ColorConvertFloat4ToU32(Boutique.TextDim),
                        currentLoadingMessage);
                }
            }

            // ── Cancel button ──
            float btnW = 100f * scale;
            float btnY = (string.IsNullOrEmpty(currentLoadingMessage)
                ? belowY + titleSz.Y
                : msgY + msgSz.Y) + msgToBtn;
            ImGui.SetCursorScreenPos(new Vector2(centre.X - btnW * 0.5f, btnY));
            if (CharacterSelectPlugin.Windows.Styles.Boutique.DrawCancelBtn(
                    dl,
                    new Vector2(centre.X - btnW * 0.5f, btnY),
                    new Vector2(centre.X + btnW * 0.5f, btnY + btnH),
                    "CANCEL", 1.6f * scale, scale, "modmgr_loading_cancel"))
            {
                loadingCancellation?.Cancel();
                IsOpen = false;
            }

            // No Dummy(availSize), that was forcing the content to span the
            // full region and triggering a phantom scrollbar.
        }

        private void UpdateLoadingAnimations()
        {
            var timeSinceStart = (float)(DateTime.Now - loadingStartTime).TotalSeconds;

            // Fade in panel
            loadingPanelAlpha = Math.Min(1.0f, timeSinceStart * 3.0f); // Fade in over ~0.33 seconds

            // Update loading message every 3 seconds
            var timeSinceMessage = (DateTime.Now - lastMessageChange).TotalSeconds;
            if (timeSinceMessage >= 3.0 || string.IsNullOrEmpty(currentLoadingMessage))
            {
                UpdateLoadingMessage();
                lastMessageChange = DateTime.Now;
            }
        }

        private void UpdateLoadingMessage()
        {
            string[] messagePool;

            // Use near-end messages when progress is high
            if (loadingProgress >= 0.95f)
            {
                messagePool = nearEndMessages;
            }
            else
            {
                messagePool = generalLoadingMessages;
            }

            // Get a random message different from the last one
            int newIndex;
            do
            {
                newIndex = messageRandom.Next(messagePool.Length);
            } while (newIndex == lastMessageIndex && messagePool.Length > 1);

            lastMessageIndex = newIndex;
            currentLoadingMessage = messagePool[newIndex];
        }

        private void UpdateOverallProgress()
        {
            // Multi-stage progress calculation
            // Stage weights: Initializing (5%), LoadingMods (80%), AnalyzingDependencies (10%), Finalizing (5%)
            float overallProgress = 0f;

            switch (currentLoadingStage)
            {
                case LoadingStage.Initializing:
                    overallProgress = 0.05f * stageProgress;
                    break;
                case LoadingStage.LoadingMods:
                    overallProgress = 0.05f + (0.80f * stageProgress);
                    break;
                case LoadingStage.AnalyzingDependencies:
                    overallProgress = 0.85f + (0.10f * stageProgress);
                    break;
                case LoadingStage.Finalizing:
                    overallProgress = 0.95f + (0.05f * stageProgress);
                    break;
                case LoadingStage.Complete:
                    overallProgress = 1.0f;
                    break;
            }

            loadingProgress = Math.Min(1.0f, overallProgress);
        }

        private void DrawHeader()
        {
            float scale = (Plugin.Instance?.Configuration?.UIScaleMultiplier ?? 1f);
            scale = MathF.Max(0.85f, MathF.Min(scale, 2.0f));
            var dl = ImGui.GetWindowDrawList();

            // ── Meta ribbon (30px): pulsing pip + breadcrumb + right-side version ──
            // No explicit Dummy gaps between header rows, PushFormStyle's
            // ItemSpacing.y already provides the natural ~5px gap. Adding
            // Dummy(0, 4*scale) on top of that doubled the visible gap and
            // made the toolbar feel awkwardly spaced from the ribbon/search.
            DrawMetaRibbon(dl, scale);

            // ── Toolbar row 1: collection pill + refresh + context info ──
            if (availableCollections.Any())
            {
                DrawCollectionPill(scale);
            }
            else
            {
                using (Plugin.Instance?.OutfitMed12?.Push())
                {
                    ImGui.TextColored(Boutique.Red, "No Penumbra collections found");
                }
            }

            // ── Global search pill (full width, boutique style) ──
            float searchPillH = 30f * scale;
            var searchPillStart = ImGui.GetCursorScreenPos();
            float searchPillW = ImGui.GetContentRegionAvail().X;
            var spMin = searchPillStart;
            var spMax = new Vector2(spMin.X + searchPillW, spMin.Y + searchPillH);

            dl.AddRectFilled(spMin, spMax,
                Boutique.U32(new Vector4(0.078f, 0.094f, 0.125f, 0.6f)));

            // Magnifier glyph
            ImGui.PushFont(UiBuilder.IconFont);
            string searchGlyph = FontAwesomeIcon.Search.ToIconString();
            var sgSz = ImGui.CalcTextSize(searchGlyph);
            ImGui.PopFont();
            float sgPx = UiBuilder.IconFont.FontSize * 0.65f;
            float sgScaleR = sgPx / UiBuilder.IconFont.FontSize;
            dl.AddText(UiBuilder.IconFont, sgPx,
                new Vector2(spMin.X + 12f * scale,
                            spMin.Y + (searchPillH - sgSz.Y * sgScaleR) * 0.5f),
                Boutique.U32(Boutique.TextFaint), searchGlyph);

            float inputX = spMin.X + 12f * scale + sgSz.X * sgScaleR + 8f * scale;
            float inputW = searchPillW - (inputX - spMin.X) - 12f * scale;
            float inputPadY = MathF.Max(0f, (searchPillH - ImGui.GetTextLineHeight()) * 0.5f);

            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0f, inputPadY));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.Text, Boutique.Text);
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, Boutique.TextFaint);

            ImGui.SetCursorScreenPos(new Vector2(inputX, spMin.Y));
            ImGui.SetNextItemWidth(inputW);
            if (ImGui.InputTextWithHint("##modmgr_global_search",
                "Global search across all mods...", ref globalSearchFilter, 200))
            {
                if (!string.IsNullOrEmpty(globalSearchFilter))
                {
                    searchFilter = "";
                    currentPage = 0;
                }
            }
            bool searchFocused = ImGui.IsItemActive();

            ImGui.PopStyleColor(5);
            ImGui.PopStyleVar(2);

            // Border (lifts to GoldDeep on focus)
            uint searchBorder = Boutique.U32(searchFocused
                ? Boutique.GoldDeep
                : Boutique.BorderSoft);
            dl.AddRect(spMin, spMax, searchBorder, 0f, ImDrawFlags.None, 1f * scale);

            ImGui.SetCursorScreenPos(new Vector2(spMin.X, spMax.Y + 4f * scale));
        }

        // Meta ribbon (30px): pulsing gold pip + "CS+ • MOD MANAGER" + breadcrumb
        // for character/design context, with "CONFLICT RESOLUTION v2.x" on the right.
        // Uses the canonical ribbon background (gradient + gold hairlines) from
        // Boutique.DrawRibbonBackground to match every other window in the plugin.
        private void DrawMetaRibbon(ImDrawListPtr dl, float scale)
        {
            float ribbonH = Boutique.RibbonHeight * scale;
            var rPos = ImGui.GetCursorScreenPos();
            float rW = ImGui.GetContentRegionAvail().X;
            var rMin = rPos;
            var rMax = new Vector2(rPos.X + rW, rPos.Y + ribbonH);

            Boutique.DrawRibbonBackground(dl, rMin, rMax, scale);

            float padX = 14f * scale;
            float midY = (rMin.Y + rMax.Y) * 0.5f;
            double time = ImGui.GetTime();

            // Pulsing gold pip (4-arg Boutique variant, square pip with sin-based pulse)
            var pipCentre = new Vector2(rMin.X + padX + 3f * scale, midY);
            Boutique.DrawGoldPip(dl, pipCentre, scale, time);

            float xCursor = rMin.X + padX + 12f * scale;

            // ── Left breadcrumb in tracked-caps Oswald ──
            using (Boutique.Kicker11?.Push())
            {
                float fs = ImGui.GetFontSize();
                float trackPx = Boutique.Track22(fs);
                float textY = midY - fs * 0.5f;

                // "CS+ • MOD MANAGER" header (Text)
                string title = "CS+ · MOD MANAGER";
                Boutique.DrawTrackedText(dl, new Vector2(xCursor, textY),
                    title, Boutique.U32(Boutique.Text), trackPx);
                xCursor += Boutique.MeasureTrackedText(title, trackPx);

                if (!string.IsNullOrEmpty(editingCharacterName))
                {
                    string sep = "  /  ";
                    Boutique.DrawTrackedText(dl, new Vector2(xCursor, textY),
                        sep, Boutique.U32(Boutique.TextGhost), trackPx);
                    xCursor += Boutique.MeasureTrackedText(sep, trackPx);

                    string config = "CONFIGURING ";
                    Boutique.DrawTrackedText(dl, new Vector2(xCursor, textY),
                        config, Boutique.U32(Boutique.TextDim), trackPx);
                    xCursor += Boutique.MeasureTrackedText(config, trackPx);

                    string charName = editingCharacterName.ToUpperInvariant();
                    Boutique.DrawTrackedText(dl, new Vector2(xCursor, textY),
                        charName, Boutique.U32(Boutique.GoldWarm), trackPx);
                    xCursor += Boutique.MeasureTrackedText(charName, trackPx);
                }

                if (editingDesign != null)
                {
                    string sep = "  /  ";
                    Boutique.DrawTrackedText(dl, new Vector2(xCursor, textY),
                        sep, Boutique.U32(Boutique.TextGhost), trackPx);
                    xCursor += Boutique.MeasureTrackedText(sep, trackPx);

                    // Square pip for the design context
                    var ctxPipC = new Vector2(xCursor + 3f * scale, midY);
                    Boutique.DrawSquarePip(dl, ctxPipC, 3f * scale, Boutique.Gold);
                    xCursor += 11f * scale;

                    string designName = string.IsNullOrEmpty(editingDesign.Name)
                        ? "NEW DESIGN" : editingDesign.Name.ToUpperInvariant();
                    Boutique.DrawTrackedText(dl, new Vector2(xCursor, textY),
                        designName, Boutique.U32(Boutique.Gold), trackPx);
                }
            }

            // ── Right-side: "CONFLICT RESOLUTION vX.X.X" ──
            using (Boutique.Kicker10?.Push())
            {
                float fs = ImGui.GetFontSize();
                float trackPx = Boutique.Track22(fs);
                float textY = midY - fs * 0.5f;

                string version = Plugin.PatchNotesVersion ?? "";
                string verLabel = string.IsNullOrEmpty(version) ? "" : $"V{version}";
                string label = "CONFLICT RESOLUTION";

                float verW = Boutique.MeasureTrackedText(verLabel, trackPx);
                float labelW = Boutique.MeasureTrackedText(label, trackPx);
                float gap = string.IsNullOrEmpty(verLabel) ? 0f : 6f * scale;
                float rightPad = padX;

                float rxStart = rMax.X - rightPad - verW;
                if (!string.IsNullOrEmpty(verLabel))
                {
                    Boutique.DrawTrackedText(dl, new Vector2(rxStart, textY),
                        verLabel, Boutique.U32(Boutique.GoldWarm), trackPx);
                }
                Boutique.DrawTrackedText(dl,
                    new Vector2(rxStart - gap - labelW, textY),
                    label, Boutique.U32(Boutique.TextFaint), trackPx);
            }

            ImGui.Dummy(new Vector2(rW, ribbonH));
        }

        // Wardrobe-style sort pill for the Penumbra collection picker.
        // [COLLECTION] kicker + value + chevron, opens a popup with all
        // available collections. Refresh icon button sits to the right.
        private bool collectionPopupOpen = false;
        private Vector2 collectionPopupAnchor;
        private void DrawCollectionPill(float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            float pillH = 30f * scale;
            // Capped narrower so the right-side context info ("X mods affecting
            // | Y total") has room and the pill doesn't dominate the toolbar.
            float pillW = MathF.Min(240f * scale, ImGui.GetContentRegionAvail().X - 40f * scale);
            float btnSide = 28f * scale;
            float gap = 6f * scale;

            var pillStart = ImGui.GetCursorScreenPos();
            var pillMin = pillStart;
            var pillMax = pillMin + new Vector2(pillW, pillH);

            ImGui.SetCursorScreenPos(pillMin);
            bool pillClicked = ImGui.InvisibleButton("##modmgr_coll_pill", new Vector2(pillW, pillH));
            bool pillHovered = ImGui.IsItemHovered();
            if (pillClicked)
            {
                collectionPopupOpen = true;
                collectionPopupAnchor = new Vector2(pillMin.X, pillMax.Y + 4f * scale);
                ImGui.OpenPopup("##modmgr_coll_popup");
            }

            dl.AddRectFilled(pillMin, pillMax,
                Boutique.U32(new Vector4(8f / 255f, 10f / 255f, 14f / 255f, 0.85f)));
            Vector4 pillBorderC = collectionPopupOpen
                ? Boutique.Gold
                : (pillHovered ? Boutique.GoldDeep : Boutique.BorderSoft);
            dl.AddRect(pillMin, pillMax, Boutique.U32(pillBorderC), 0f, ImDrawFlags.None, 1f * scale);

            float padX = 12f * scale;
            // Kicker COLLECTION
            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                float kY = (pillMin.Y + pillMax.Y) * 0.5f - ImGui.GetFontSize() * 0.5f;
                Boutique.DrawTrackedText(dl,
                    new Vector2(pillMin.X + padX, kY),
                    "COLLECTION", Boutique.U32(Boutique.TextDim), 2.5f * scale);
            }
            // Value (right-aligned before chevron)
            string val = (currentCollectionName ?? availableCollections.FirstOrDefault().Value ?? "")
                .ToUpperInvariant();
            using (Plugin.Instance?.OswaldMed11?.Push())
            {
                float vY = (pillMin.Y + pillMax.Y) * 0.5f - ImGui.GetFontSize() * 0.5f;
                float trackPx = 1.8f * scale;
                float chevW = 14f * scale;
                float maxValW = pillW - padX - 90f * scale - chevW; // room for kicker + chev
                string display = val;
                float vW = Boutique.MeasureTrackedText(display, trackPx);
                if (vW > maxValW && display.Length > 1)
                {
                    const string ell = "...";
                    float ellW = Boutique.MeasureTrackedText(ell, trackPx);
                    for (int k = display.Length - 1; k > 0; k--)
                    {
                        var trunc = display.Substring(0, k);
                        if (Boutique.MeasureTrackedText(trunc, trackPx) + ellW <= maxValW)
                        {
                            display = trunc + ell;
                            vW = Boutique.MeasureTrackedText(display, trackPx);
                            break;
                        }
                    }
                }
                Boutique.DrawTrackedText(dl,
                    new Vector2(pillMax.X - padX - chevW - vW, vY),
                    display, Boutique.U32(Boutique.GoldWarm), trackPx);
            }
            // Chevron
            float chR = 4f * scale;
            var chC = new Vector2(pillMax.X - padX - chR, (pillMin.Y + pillMax.Y) * 0.5f);
            dl.AddTriangleFilled(
                chC + new Vector2(-chR, -chR * 0.5f),
                chC + new Vector2( chR, -chR * 0.5f),
                chC + new Vector2(0f, chR),
                Boutique.U32(Boutique.GoldDeep));

            // Refresh icon button (right of pill)
            float refreshX = pillMax.X + gap;
            float refreshY = pillMin.Y + (pillH - btnSide) * 0.5f;
            ImGui.SetCursorScreenPos(new Vector2(refreshX, refreshY));
            bool refreshClicked = ImGui.InvisibleButton("##modmgr_refresh", new Vector2(btnSide, btnSide));
            bool refreshHovered = ImGui.IsItemHovered();
            uint refreshBg = Boutique.U32(refreshHovered ? Boutique.Surface2
                : new Vector4(0.078f, 0.094f, 0.125f, 0.6f));
            uint refreshBorder = Boutique.U32(refreshHovered ? Boutique.GoldDeep : Boutique.BorderSoft);
            var rfMin = new Vector2(refreshX, refreshY);
            var rfMax = rfMin + new Vector2(btnSide, btnSide);
            dl.AddRectFilled(rfMin, rfMax, refreshBg);
            dl.AddRect(rfMin, rfMax, refreshBorder, 0f, ImDrawFlags.None, 1f * scale);
            string rfGlyph = FontAwesomeIcon.SyncAlt.ToIconString();
            var rfFont = UiBuilder.IconFont;
            float rfPx = rfFont.FontSize * 0.60f;
            float rfScaleR = rfPx / rfFont.FontSize;
            ImGui.PushFont(rfFont);
            var rfSz = ImGui.CalcTextSize(rfGlyph);
            ImGui.PopFont();
            dl.AddText(rfFont, rfPx,
                new Vector2(refreshX + (btnSide - rfSz.X * rfScaleR) * 0.5f,
                            refreshY + (btnSide - rfSz.Y * rfScaleR) * 0.5f),
                Boutique.U32(refreshHovered ? Boutique.GoldWarm : Boutique.TextDim),
                rfGlyph);
            if (refreshHovered)
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Refresh mods");
            if (refreshClicked) _ = LoadCurrentMods();

            // ── Popup (Wardrobe-style) ──
            ImGui.SetNextWindowPos(collectionPopupAnchor);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(6f / 255f, 7f / 255f, 9f / 255f, 0.97f));
            ImGui.PushStyleColor(ImGuiCol.Border, Boutique.WithAlpha(Boutique.GoldDeep, 0.85f));
            ImGui.PushStyleColor(ImGuiCol.Text, Boutique.Text);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 4f * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 1f * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
            if (ImGui.BeginPopup("##modmgr_coll_popup"))
            {
                float itemH = 26f * scale;
                float itemPadX = 14f * scale;
                float itemW = pillW;
                var popupFont = Plugin.Instance?.OswaldMed11;

                var collectionsList = availableCollections.ToList();
                // Cap the popup to ~10 rows tall so it never grows off-screen
                // when the user has many collections; anything beyond that
                // scrolls inside the child.
                float maxPopupH = 10f * itemH;
                float childH = MathF.Min(collectionsList.Count * itemH, maxPopupH);
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
                ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Vector4.Zero);
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Boutique.WithAlpha(Boutique.GoldDeep, 0.55f));
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Boutique.GoldDeep);
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Boutique.Gold);
                ImGui.BeginChild("##modmgr_coll_popup_scroll", new Vector2(itemW, childH), false,
                    ImGuiWindowFlags.AlwaysUseWindowPadding);
                for (int i = 0; i < collectionsList.Count; i++)
                {
                    var kvp = collectionsList[i];
                    bool isSel = i == selectedCollectionIndex;
                    var rowMn = ImGui.GetCursorScreenPos();
                    var rowMx = new Vector2(rowMn.X + itemW, rowMn.Y + itemH);
                    ImGui.InvisibleButton($"##modmgr_coll_item_{i}", new Vector2(itemW, itemH));
                    bool itemHov = ImGui.IsItemHovered();
                    bool itemClk = ImGui.IsItemClicked();
                    if (itemClk)
                    {
                        selectedCollectionIndex = i;
                        currentCollectionId = kvp.Key;
                        currentCollectionName = kvp.Value;
                        userHasSelectedCollection = true;
                        _ = LoadCurrentMods();
                        collectionPopupOpen = false;
                        ImGui.CloseCurrentPopup();
                    }

                    var pdl = ImGui.GetWindowDrawList();
                    if (isSel)
                    {
                        pdl.AddRectFilled(rowMn, rowMx,
                            Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.18f)));
                        pdl.AddRectFilled(rowMn,
                            new Vector2(rowMn.X + 2f * scale, rowMx.Y),
                            Boutique.U32(Boutique.Gold));
                    }
                    else if (itemHov)
                    {
                        pdl.AddRectFilled(rowMn, rowMx,
                            Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.10f)));
                    }

                    if (popupFont != null)
                    {
                        using (popupFont.Push())
                        {
                            float fontH = ImGui.GetFontSize();
                            float trackPx = fontH * 0.18f;
                            string itemLabel = kvp.Value.ToUpperInvariant();
                            Vector4 col = isSel ? Boutique.GoldWarm
                                : (itemHov ? Boutique.GoldBright : Boutique.Text);
                            Boutique.DrawTrackedText(pdl,
                                new Vector2(rowMn.X + itemPadX, rowMn.Y + (itemH - fontH) * 0.5f),
                                itemLabel, Boutique.U32(col), trackPx);
                        }
                    }
                }
                ImGui.EndChild();
                ImGui.PopStyleColor(4);
                ImGui.PopStyleVar();
                ImGui.EndPopup();
            }
            else if (collectionPopupOpen)
            {
                collectionPopupOpen = false;
            }
            ImGui.PopStyleVar(4);
            ImGui.PopStyleColor(3);

            // ── Toolbar context info on the right: "X MODS AFFECTING YOU NOW | Y MODS TOTAL" ──
            int affectingCount = availableMods.Count(m => m.IsCurrentlyAffecting);
            int totalCount = availableMods.Count;
            float ctxRowRight = pillStart.X + ImGui.GetContentRegionAvail().X;
            float ctxMidY = (pillMin.Y + pillMax.Y) * 0.5f;

            using (Boutique.Kicker11?.Push())
            {
                float fs = ImGui.GetFontSize();
                float trackPx = Boutique.Track22(fs);
                float ctxY = ctxMidY - fs * 0.5f;

                string totalNum = totalCount.ToString();
                string totalTail = " MODS TOTAL";
                string pipe = "  |  ";
                string affNum = affectingCount.ToString();
                string affTail = " MODS AFFECTING YOU NOW";

                float wTotalNum = Boutique.MeasureTrackedText(totalNum, trackPx);
                float wTotalTail = Boutique.MeasureTrackedText(totalTail, trackPx);
                float wPipe = Boutique.MeasureTrackedText(pipe, trackPx);
                float wAffNum = Boutique.MeasureTrackedText(affNum, trackPx);
                float wAffTail = Boutique.MeasureTrackedText(affTail, trackPx);

                // Pulsing green dot for the "affecting now" caption
                float dotR = 3f * scale;
                float dotW = dotR * 2f + 6f * scale;
                float clusterW = dotW + wAffNum + wAffTail + wPipe + wTotalNum + wTotalTail;
                float ctxX = ctxRowRight - clusterW;

                // Green dot (live indicator)
                double t = ImGui.GetTime();
                float pulse = 0.65f + 0.35f * (float)Math.Sin(t * 2.4);
                var dotC = new Vector2(ctxX + dotR, ctxMidY);
                dl.AddCircleFilled(dotC, dotR + 2f * scale,
                    Boutique.U32(Boutique.WithAlpha(Boutique.Green, 0.30f * pulse)), 16);
                dl.AddCircleFilled(dotC, dotR, Boutique.U32(Boutique.Green), 12);
                ctxX += dotW;

                // "<N>" affecting count in gold-warm. Tail text bumped to
                // TextDim (was TextFaint, barely readable on the dark bg).
                Boutique.DrawTrackedText(dl, new Vector2(ctxX, ctxY), affNum,
                    Boutique.U32(Boutique.GoldWarm), trackPx);
                ctxX += wAffNum;
                Boutique.DrawTrackedText(dl, new Vector2(ctxX, ctxY), affTail,
                    Boutique.U32(Boutique.TextDim), trackPx);
                ctxX += wAffTail;
                Boutique.DrawTrackedText(dl, new Vector2(ctxX, ctxY), pipe,
                    Boutique.U32(Boutique.TextFaint), trackPx);
                ctxX += wPipe;
                Boutique.DrawTrackedText(dl, new Vector2(ctxX, ctxY), totalNum,
                    Boutique.U32(Boutique.GoldWarm), trackPx);
                ctxX += wTotalNum;
                Boutique.DrawTrackedText(dl, new Vector2(ctxX, ctxY), totalTail,
                    Boutique.U32(Boutique.TextDim), trackPx);
            }

            // Reserve the pill row's vertical space from its TOP, not its BOTTOM
            ImGui.SetCursorScreenPos(pillStart);
            ImGui.Dummy(new Vector2(pillW + gap + btnSide, pillH));
        }

        // Sidebar column header, tracked-caps GoldWarm + bottom hairline
        private void DrawSidebarColumnHead(string label, float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            float w = ImGui.GetContentRegionAvail().X;
            float h = 30f * scale;
            var max = pos + new Vector2(w, h);

            dl.AddRectFilled(pos, max, Boutique.U32(new Vector4(0f, 0f, 0f, 0.30f)));
            dl.AddLine(new Vector2(pos.X, max.Y - 1f * scale),
                       new Vector2(max.X, max.Y - 1f * scale),
                       Boutique.U32(Boutique.BorderSoft), 1f * scale);

            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                float trackPx = ImGui.GetFontSize() * 0.32f;
                float labelY = pos.Y + (h - ImGui.GetFontSize()) * 0.5f;
                CharacterSelectPlugin.Windows.Styles.Boutique.DrawTrackedText(
                    dl, new Vector2(pos.X + 12f * scale, labelY),
                    label, Boutique.U32(Boutique.GoldWarm), trackPx);
            }
            ImGui.Dummy(new Vector2(w, h));
        }

        // Single category row, icon + name + count, gold gradient + 2px gold
        // left bar on selected. Returns true on click.
        private bool DrawSidebarCategoryRow(string name, int count, bool isActive,
            string id, float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            float w = ImGui.GetContentRegionAvail().X;
            float h = 28f * scale;
            var pos = ImGui.GetCursorScreenPos();
            var max = pos + new Vector2(w, h);

            ImGui.SetCursorScreenPos(pos);
            bool clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
            bool hovered = ImGui.IsItemHovered();

            if (isActive)
            {
                uint l = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.10f));
                uint r = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0f));
                dl.AddRectFilledMultiColor(pos, max, l, r, r, l);
                dl.AddRectFilled(new Vector2(pos.X, pos.Y + 4f * scale),
                                 new Vector2(pos.X + 2f * scale, max.Y - 4f * scale),
                                 Boutique.U32(Boutique.Gold));
            }
            else if (hovered)
            {
                dl.AddRectFilled(pos, max,
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.05f)));
            }

            // Name (Outfit Med 12)
            using (Plugin.Instance?.OutfitMed12?.Push())
            {
                float labelY = pos.Y + (h - ImGui.GetFontSize()) * 0.5f;
                Vector4 nameCol = isActive ? Boutique.GoldWarm : Boutique.Text;
                dl.AddText(new Vector2(pos.X + 14f * scale, labelY),
                    Boutique.U32(nameCol), name);
            }

            // Count (right-aligned, OswaldSemi11 tracked-caps in TextDim)
            string countStr = count.ToString();
            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                float trackPx = ImGui.GetFontSize() * 0.20f;
                float countW = CharacterSelectPlugin.Windows.Styles.Boutique
                    .MeasureTrackedText(countStr, trackPx);
                float countY = pos.Y + (h - ImGui.GetFontSize()) * 0.5f;
                CharacterSelectPlugin.Windows.Styles.Boutique.DrawTrackedText(
                    dl, new Vector2(max.X - 14f * scale - countW, countY),
                    countStr, Boutique.U32(isActive ? Boutique.GoldWarm : Boutique.TextDim),
                    trackPx);
            }

            return clicked;
        }

        private void DrawMainContent()
        {
            // Check if no mods available
            if (!availableMods.Any())
            {
                var center = ImGui.GetContentRegionAvail() / 2;
                ImGui.SetCursorPos(center - new Vector2(100, 30));
                ImGui.TextColored(ColorSchemes.Dark.TextMuted, "No mods found. This could mean:");
                ImGui.BulletText("Penumbra is not installed or running");
                ImGui.BulletText("Penumbra has no mods in the current collection");
                ImGui.BulletText("No mods are currently affecting your character");

                ImGui.Separator();
                if (ImGui.Button("Retry Loading Mods"))
                {
                    _ = LoadCurrentMods();
                }
                return;
            }

            float scaleSb = (Plugin.Instance?.Configuration?.UIScaleMultiplier ?? 1f);
            scaleSb = MathF.Max(0.85f, MathF.Min(scaleSb, 2.0f));

            // ── Boutique sidebar (220px), quick-access entry style ──
            float sidebarW = 220f * scaleSb;
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.04f, 0.05f, 0.08f, 0.55f));
            ImGui.BeginChild("CategorySidebar", new Vector2(sidebarW, -72 * scaleSb), false);

            // Column header strip (CATEGORIES, tracked-caps, GoldWarm)
            DrawSidebarColumnHead("CATEGORIES", scaleSb);
            ImGui.Dummy(new Vector2(0, 6f * scaleSb));

            for (int i = 0; i < categoryNames.Length; i++)
            {
                var modCount = GetModCountForCategory(i);
                bool isSelected = selectedCategory == i;
                bool clickedCat = DrawSidebarCategoryRow(categoryNames[i], modCount, isSelected,
                    $"##modmgr_cat_{i}", scaleSb);
                if (clickedCat)
                {
                    selectedCategory = i;
                    // Reset to first page when switching categories
                    categoryPageNumbers[i] = 0;
                    currentPage = 0;
                    // Clear search when switching categories
                    searchFilter = "";
                    // Clear global search when switching categories
                    globalSearchFilter = "";
                }
            }

            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);

            // Main mod list area, transparent so the window's Surface0
            // bg shows through; matches the other boutique surfaces.
            ImGui.SameLine();
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f * scaleSb, 8f * scaleSb));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
            ImGui.BeginChild("ModListArea", new Vector2(-1, -72 * scaleSb), false);

            // Sticky boutique search pill at the top of the list
            DrawCategorySearchPill(scaleSb);
            ImGui.Dummy(new Vector2(0, 4f * scaleSb));


            // Scrollable mod list with pagination. Reserve enough vertical
            // space at the bottom for the boutique pagination row (32px) plus
            // a small gap, so the page-btns aren't clipped.
            ImGui.BeginChild("ModList", new Vector2(-1, -36 * scaleSb), false);

            // Get filtered mods and handle pagination
            var categoryMods = GetFilteredModsForSelectedCategory();
            var totalMods = categoryMods.Count;
            var totalPages = (int)Math.Ceiling((double)totalMods / ModsPerPage);

            // Ensure current page is valid for this category
            if (!categoryPageNumbers.ContainsKey(selectedCategory))
                categoryPageNumbers[selectedCategory] = 0;

            currentPage = categoryPageNumbers[selectedCategory];
            if (currentPage >= totalPages && totalPages > 0)
                currentPage = totalPages - 1;

            // Get mods for current page
            var pagedMods = categoryMods
                .Skip(currentPage * ModsPerPage)
                .Take(ModsPerPage)
                .ToList();

            // Boutique tracked-caps caption when searching.
            if (!string.IsNullOrWhiteSpace(searchFilter))
            {
                Boutique.DrawFoundNCaption(totalMods, "matches", scaleSb);
            }

            // "Select All" for Currently Affecting You tab, chamfered mini-btn
            // that toggles every Gear/Hair mod currently affecting the character.
            // Does not touch the Ctrl+click-restricted section (Eyes/Tattoos/etc).
            if (selectedCategory == 0)
            {
                var gearHairMods = categoryMods
                    .Where(m => m.ModType == ModType.Gear || m.ModType == ModType.Hair)
                    .ToList();

                if (gearHairMods.Count > 0)
                {
                    int alreadySelected = gearHairMods.Count(m => selectedMods.TryGetValue(m.Directory, out var v) && v);
                    bool allSelected = alreadySelected == gearHairMods.Count;

                    string mlabel = allSelected
                        ? $"DESELECT GEAR/HAIR ({gearHairMods.Count})"
                        : $"SELECT GEAR/HAIR ({gearHairMods.Count})";

                    var dlMini = ImGui.GetWindowDrawList();
                    using (Boutique.Kicker9?.Push())
                    {
                        float fs = ImGui.GetFontSize();
                        float trackPx = Boutique.Track26(fs);
                        var miniSize = Boutique.MeasureMiniBtn(mlabel, trackPx, scaleSb, hasIcon: true);
                        ImGui.SetCursorPosX(14f * scaleSb);
                        var miniPos = ImGui.GetCursorScreenPos();
                        if (Boutique.DrawMiniBtn(dlMini, miniPos, miniSize, mlabel,
                            trackPx, scaleSb, "modmgr_selectall_gh",
                            UiBuilder.IconFont, UiBuilder.IconFont.FontSize * 0.55f,
                            FontAwesomeIcon.CheckSquare.ToIconString()))
                        {
                            foreach (var mod in gearHairMods)
                                selectedMods[mod.Directory] = !allSelected;
                        }
                        if (ImGui.IsItemHovered())
                        {
                            Boutique.TitledTooltip(
                                allSelected ? "Deselect All" : "Select All",
                                allSelected
                                    ? "Deselect every Gear/Hair mod currently affecting you. Does not touch the Ctrl+click-restricted section."
                                    : "Select every Gear/Hair mod currently affecting you. Does not touch the Ctrl+click-restricted section.");
                        }
                        ImGui.Dummy(new Vector2(miniSize.X, miniSize.Y + 4f * scaleSb));
                    }
                }
            }

            // Draw mod entries with divider between Gear/Hair and other types for "Currently Affecting You"
            bool hasDrawnDivider = false;
            bool hasPreviousGearHair = false;

            foreach (var mod in pagedMods)
            {
                // Check if we need to draw a divider (only for "Currently Affecting You" tab)
                if (selectedCategory == 0 && !hasDrawnDivider && hasPreviousGearHair)
                {
                    bool isCurrentGearHair = mod.ModType == ModType.Gear || mod.ModType == ModType.Hair;

                    // If we transition from Gear/Hair to other types, draw the
                    // boutique restricted divider (amber dashed top + tracked-caps copy).
                    if (!isCurrentGearHair)
                    {
                        int restrictedRowCount = pagedMods
                            .Count(m => m.ModType != ModType.Gear && m.ModType != ModType.Hair);
                        Boutique.DrawRestrictedDivider(
                            "Hold Ctrl to toggle these design-scoped mods",
                            restrictedRowCount > 0 ? $"{restrictedRowCount} below" : null,
                            scaleSb);
                        if (ImGui.IsItemHovered())
                        {
                            Boutique.TitledTooltip(
                                "Design Selection Warning",
                                "Selecting these mods ties them to this specific design, they get DISABLED when switching to other designs and ENABLED when switching back. Leave general customisation mods (ears/tails, tattoos) unselected so they stay active across designs.",
                                360f);
                        }
                        hasDrawnDivider = true;
                    }
                }

                // Check if this mod requires Ctrl+click (other mods after divider in Currently Affecting You tab)
                bool requiresCtrlClick = selectedCategory == 0 && hasDrawnDivider &&
                                        mod.ModType != ModType.Gear && mod.ModType != ModType.Hair;
                DrawModEntry(mod, requiresCtrlClick);

                // Track if this mod is Gear/Hair for next iteration
                if (selectedCategory == 0)
                {
                    bool isGearHair = mod.ModType == ModType.Gear || mod.ModType == ModType.Hair;
                    if (isGearHair)
                        hasPreviousGearHair = true;
                }
            }

            ImGui.EndChild();

            // Pagination controls
            DrawPaginationControls(totalPages, totalMods);

            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
        }

        // Boutique search pill matching the global search style at the top of
        // the mod list area. 30px tall, magnifier icon, dark velvet bg, gold
        // border on focus.
        private void DrawCategorySearchPill(float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            float pillH = 30f * scale;
            var pos = ImGui.GetCursorScreenPos();
            float w = ImGui.GetContentRegionAvail().X;
            var min = pos;
            var max = new Vector2(min.X + w, min.Y + pillH);

            dl.AddRectFilled(min, max,
                Boutique.U32(new Vector4(0.078f, 0.094f, 0.125f, 0.6f)));

            ImGui.PushFont(UiBuilder.IconFont);
            string sg = FontAwesomeIcon.Search.ToIconString();
            var sgSz = ImGui.CalcTextSize(sg);
            ImGui.PopFont();
            float sgPx = UiBuilder.IconFont.FontSize * 0.65f;
            float sgScaleR = sgPx / UiBuilder.IconFont.FontSize;
            dl.AddText(UiBuilder.IconFont, sgPx,
                new Vector2(min.X + 12f * scale,
                            min.Y + (pillH - sgSz.Y * sgScaleR) * 0.5f),
                Boutique.U32(Boutique.TextFaint), sg);

            float inputX = min.X + 12f * scale + sgSz.X * sgScaleR + 8f * scale;
            float inputW = w - (inputX - min.X) - 12f * scale;
            float inputPadY = MathF.Max(0f, (pillH - ImGui.GetTextLineHeight()) * 0.5f);

            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0f, inputPadY));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.Text, Boutique.Text);
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, Boutique.TextFaint);

            ImGui.SetCursorScreenPos(new Vector2(inputX, min.Y));
            ImGui.SetNextItemWidth(inputW);
            if (ImGui.InputTextWithHint("##modmgr_cat_search",
                "Search mods...", ref searchFilter, 100))
            {
                if (!string.IsNullOrEmpty(searchFilter))
                {
                    globalSearchFilter = "";
                    currentPage = 0;
                }
            }
            bool focused = ImGui.IsItemActive();

            ImGui.PopStyleColor(5);
            ImGui.PopStyleVar(2);

            uint borderC = Boutique.U32(focused
                ? Boutique.GoldDeep
                : Boutique.BorderSoft);
            dl.AddRect(min, max, borderC, 0f, ImDrawFlags.None, 1f * scale);

            ImGui.SetCursorScreenPos(new Vector2(min.X, max.Y));
        }

        private void DrawBottomButtons()
        {
            float scale = (Plugin.Instance?.Configuration?.UIScaleMultiplier ?? 1f);
            scale = MathF.Max(0.85f, MathF.Min(scale, 2.0f));
            var dl = ImGui.GetWindowDrawList();

            // Top hairline separator + extra breathing room above and below
            // the buttons. Without enough vertical pad the cancel/apply pills
            // sat right against the window's bottom edge.
            ImGui.Dummy(new Vector2(0, 4f * scale));
            var sepStart = ImGui.GetCursorScreenPos();
            float sepW = ImGui.GetContentRegionAvail().X;
            dl.AddLine(sepStart, new Vector2(sepStart.X + sepW, sepStart.Y),
                Boutique.U32(Boutique.BorderSoft), 1f * scale);
            ImGui.Dummy(new Vector2(0, 10f * scale));

            // Selection counts and delta vs the snapshot taken at Open() time.
            int selectedCount = selectedMods.Count(kvp => kvp.Value);
            int enabledDelta = 0;  // Newly enabled (was off/missing, now on)
            int disabledDelta = 0; // Newly disabled (was on, now off/missing)
            foreach (var kvp in selectedMods)
            {
                bool wasOn = originalSelection.TryGetValue(kvp.Key, out var prev) && prev;
                if (kvp.Value && !wasOn) enabledDelta++;
                else if (!kvp.Value && wasOn) disabledDelta++;
            }
            // Also count mods that were ON in the original but are missing entirely now
            foreach (var kvp in originalSelection)
            {
                if (kvp.Value && !selectedMods.ContainsKey(kvp.Key))
                    disabledDelta++;
            }

            float btnH = 30f * scale;
            float cancelW = 96f * scale;
            float applyW = 130f * scale;
            float gap = 8f * scale;
            float footPadX = 14f * scale;

            var rowStart = ImGui.GetCursorScreenPos();
            float rowRight = rowStart.X + sepW;
            float midY = rowStart.Y + btnH * 0.5f;

            // ── Big gold numeral + "SELECTED" label + pipe + delta ──
            float xCursor = rowStart.X + footPadX;
            using (Plugin.Instance?.OswaldSemiMidSmall?.Push())
            {
                string numStr = selectedCount.ToString();
                var numSz = ImGui.CalcTextSize(numStr);
                float numY = midY - numSz.Y * 0.5f;
                Vector4 numCol = selectedCount > 0 ? Boutique.Gold : Boutique.TextFaint;
                dl.AddText(new Vector2(xCursor, numY), Boutique.U32(numCol), numStr);
                xCursor += numSz.X + 8f * scale;
            }
            // Footer text bumped from TextFaint to TextDim, TextFaint was
            // unreadable against the dark bg in the bottom-right cluster.
            using (Boutique.Kicker11?.Push())
            {
                float fs = ImGui.GetFontSize();
                float trackPx = Boutique.Track32(fs);
                float lblY = midY - fs * 0.5f;
                Boutique.DrawTrackedText(dl, new Vector2(xCursor, lblY),
                    "SELECTED", Boutique.U32(Boutique.TextDim), trackPx);
                xCursor += Boutique.MeasureTrackedText("SELECTED", trackPx) + 12f * scale;
            }
            // Pipe
            dl.AddRectFilled(
                new Vector2(xCursor, midY - 9f * scale),
                new Vector2(xCursor + 1f * scale, midY + 9f * scale),
                Boutique.U32(Boutique.BorderSoft));
            xCursor += 12f * scale;

            // Delta: "+5 enabled, -2 disabled vs. saved"
            using (Boutique.Kicker10?.Push())
            {
                float fs = ImGui.GetFontSize();
                float trackPx = Boutique.Track26(fs);
                float deltaY = midY - fs * 0.5f;

                string posStr = $"+{enabledDelta}";
                Boutique.DrawTrackedText(dl, new Vector2(xCursor, deltaY), posStr,
                    Boutique.U32(enabledDelta > 0 ? Boutique.GreenSoft : Boutique.TextDim),
                    trackPx);
                xCursor += Boutique.MeasureTrackedText(posStr, trackPx);

                string mid = " ENABLED, ";
                Boutique.DrawTrackedText(dl, new Vector2(xCursor, deltaY), mid,
                    Boutique.U32(Boutique.TextDim), trackPx);
                xCursor += Boutique.MeasureTrackedText(mid, trackPx);

                string negStr = $"-{disabledDelta}";
                Boutique.DrawTrackedText(dl, new Vector2(xCursor, deltaY), negStr,
                    Boutique.U32(disabledDelta > 0
                        ? new Vector4(1f, 0.54f, 0.54f, 1f)
                        : Boutique.TextDim),
                    trackPx);
                xCursor += Boutique.MeasureTrackedText(negStr, trackPx);

                string tail = " DISABLED VS. SAVED";
                Boutique.DrawTrackedText(dl, new Vector2(xCursor, deltaY), tail,
                    Boutique.U32(Boutique.TextDim), trackPx);
            }

            // CANCEL + APPLY pill on the right edge
            var applyMin = new Vector2(rowRight - footPadX - applyW, midY - btnH * 0.5f);
            var applyMax = applyMin + new Vector2(applyW, btnH);
            var cancelMin = new Vector2(applyMin.X - gap - cancelW, midY - btnH * 0.5f);
            var cancelMax = cancelMin + new Vector2(cancelW, btnH);

            if (CharacterSelectPlugin.Windows.Styles.Boutique.DrawCancelBtn(
                    dl, cancelMin, cancelMax, "CANCEL", 1.6f * scale, scale, "modmgr_cancel"))
            {
                IsOpen = false;
            }

            if (CharacterSelectPlugin.Windows.Styles.Boutique.DrawSavePill(
                    dl, applyMin, applyMax, "APPLY", 1.8f * scale, scale, "modmgr_apply",
                    false, _modMgrSheen))
            {
                SaveSelection();
                IsOpen = false;
            }

            ImGui.Dummy(new Vector2(0, btnH + 8f * scale));
        }

        // Static sheen tracker for mod manager Apply pill.
        private static readonly Dictionary<string, DateTime> _modMgrSheenStarts = new();
        private const float ModMgrSheenDuration = 0.65f;
        private static float _modMgrSheen(string id, bool hovered)
        {
            if (!hovered)
            {
                _modMgrSheenStarts.Remove(id);
                return -1f;
            }
            if (!_modMgrSheenStarts.ContainsKey(id))
                _modMgrSheenStarts[id] = DateTime.UtcNow;
            float elapsed = (float)(DateTime.UtcNow - _modMgrSheenStarts[id]).TotalSeconds;
            if (elapsed >= ModMgrSheenDuration) return -1f;
            return elapsed / ModMgrSheenDuration;
        }

        // Path-based mod type analysis implemented below

        private void SaveSelection()
        {
            // Build selection: include both Enable (true) AND Disable (false)
            // Exclude mods set to Inherit (they should not be in SecretModState)
            var selection = selectedMods
                .Where(kvp => !modsToInherit.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            // If we're converting a character/design to Conflict Resolution, remove bulktag commands from their macros
            if (editingCharacterIndex.HasValue && editingCharacterIndex.Value >= 0 && editingCharacterIndex.Value < plugin.Characters.Count)
            {
                var character = plugin.Characters[editingCharacterIndex.Value];
                if (character.SecretModState == null || !character.SecretModState.Any())
                {
                    // First time setting up Conflict Resolution for this character - convert macro
                    character.Macros = Plugin.ConvertMacroToConflictResolution(character.Macros);
                }
            }

            if (editingDesign != null && (editingDesign.SecretModState == null || !editingDesign.SecretModState.Any()))
            {
                // First time setting up Conflict Resolution for this design - convert macro
                editingDesign.Macro = Plugin.ConvertMacroToConflictResolution(editingDesign.Macro);
                if (!string.IsNullOrEmpty(editingDesign.AdvancedMacro))
                {
                    editingDesign.AdvancedMacro = Plugin.ConvertMacroToConflictResolution(editingDesign.AdvancedMacro);
                }
            }

            // Save selection to editingDesign if we have one (for design-level editing)
            if (editingDesign != null)
            {
                editingDesign.SecretModState = selection.Any() ? selection : null;
            }

            onSave?.Invoke(selection);
            Plugin.Log.Information($"[PIN DEBUG] Saving pins via callback: {string.Join(", ", pinnedMods)}");
            onSavePins?.Invoke(pinnedMods);

            // Invoke inherit callback for mods that need Penumbra inheritance restored
            if (modsToInherit.Count > 0)
            {
                onSaveInherit?.Invoke(modsToInherit);
            }
        }

        // Helper methods for new UI
        private List<ModEntry> GetFilteredModsForSelectedCategory()
        {
            List<ModEntry> categoryMods;

            // Check if global search is active
            if (!string.IsNullOrEmpty(globalSearchFilter))
            {
                // Global search: search across ALL mods regardless of category
                categoryMods = availableMods.Where(m =>
                    m.Name.Contains(globalSearchFilter, StringComparison.OrdinalIgnoreCase) ||
                    m.Directory.Contains(globalSearchFilter, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }
            else
            {
                // Category-specific filtering (existing logic)
                if (selectedCategory == 0) // Currently Affecting You
                {
                    // Display: Show all currently affecting customization mods for visibility
                    // Note: Snapshot feature still only includes Gear/Hair for safety
                    categoryMods = availableMods.Where(m => m.IsCurrentlyAffecting &&
                        (m.ModType == ModType.Gear || m.ModType == ModType.Hair ||
                         m.ModType == ModType.Eyes || m.ModType == ModType.Tattoos ||
                         m.ModType == ModType.EarsTails || m.ModType == ModType.FacePaint)).ToList();
                }
                else
                {
                    // Get mods for this specific category
                    var targetType = categoryTypes[selectedCategory];

                    // Special case for Mounts/Minions category (includes both Mount and Minion types)
                    if (targetType == ModType.Mount)
                    {
                        categoryMods = availableMods.Where(m => m.ModType == ModType.Mount || m.ModType == ModType.Minion).ToList();
                    }
                    else if (targetType == ModType.Other)
                    {
                        // Include both Other and Unknown mods in 'Other' category
                        categoryMods = availableMods.Where(m => m.ModType == ModType.Other || m.ModType == ModType.Unknown).ToList();
                    }
                    else
                    {
                        categoryMods = availableMods.Where(m => m.ModType == targetType).ToList();
                    }
                }

                // Apply category-specific search filter if present
                if (!string.IsNullOrEmpty(searchFilter))
                {
                    categoryMods = categoryMods.Where(m =>
                        m.Name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase) ||
                        m.Directory.Contains(searchFilter, StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                }
            }

            // Sort with currently affecting mods at the top
            // For "Currently Affecting You" category, prioritize Gear/Hair over other mod types
            if (selectedCategory == 0) // Currently Affecting You
            {
                return categoryMods
                    .OrderByDescending(m => m.IsCurrentlyAffecting)
                    .ThenByDescending(m => m.ModType == ModType.Gear || m.ModType == ModType.Hair) // Gear/Hair first
                    .ThenBy(m => m.ModType.ToString()) // Group other types together
                    .ThenBy(m => m.Name)
                    .ToList();
            }
            else
            {
                return categoryMods
                    .OrderByDescending(m => m.IsCurrentlyAffecting)
                    .ThenBy(m => m.Name)
                    .ToList();
            }
        }

        private void DrawModEntry(ModEntry mod, bool requiresCtrlClick = false)
        {
            float scale = (Plugin.Instance?.Configuration?.UIScaleMultiplier ?? 1f);
            scale = MathF.Max(0.85f, MathF.Min(scale, 2.0f));

            var dl = ImGui.GetWindowDrawList();
            float rowW = ImGui.GetContentRegionAvail().X;
            var (rowMin, rowMax, _) = Boutique.DrawTableRowChassis(dl, rowW, scale, mod.IsCurrentlyAffecting);

            // Determine 3-state value: 0=Enable, 1=Disable, 2=Inherit.
            int currentState;
            if (modsToInherit.Contains(mod.Directory))
                currentState = 2;
            else if (selectedMods.TryGetValue(mod.Directory, out var sel))
                currentState = sel ? 0 : 1;
            else
                currentState = 2;
            bool isSelected = currentState == 0;

            bool isPinned = pinnedMods.Contains(mod.Directory);
            bool hasOptions = ModHasOptionsCache(mod.Directory, mod.Name);
            bool hasCustomOptions = (editingDesign?.ModOptionSettings?.ContainsKey(mod.Directory) ?? false)
                                  || (GetEditingCharacter()?.ModOptionSettings?.ContainsKey(mod.Directory) ?? false);

            // Layout per mockup CSS:
            //   .row-cluster { gap: 4px; margin-left: 2px; }
            //   .mod-name { margin-left: 4px; }
            // So: state → 2px → pin → 4px → gear → 4px → name.
            float padL = 14f * scale;
            float padR = 12f * scale;
            float midY = (rowMin.Y + rowMax.Y) * 0.5f;
            float stateW = Boutique.StateCtrlW * scale;
            float stateH = Boutique.StateCtrlH * scale;
            float stateToCluster = 2f * scale;            // .row-cluster margin-left
            float clusterGap = Boutique.ClusterIconGap * scale;  // 4px between pin and gear
            float iconSide = Boutique.ClusterIconSide * scale;
            float nameGap = 4f * scale;                   // .mod-name margin-left

            float xCursor = rowMin.X + padL;
            var stateMin = new Vector2(xCursor, midY - stateH * 0.5f);
            var stateMax = stateMin + new Vector2(stateW, stateH);

            // ── State control ───────────────────────────────────────────
            if (plugin.Configuration.RespectPenumbraInheritance)
            {
                // 3-state combo with popup
                var stateValue = (Boutique.StateValue)currentState;
                bool isOpen = openStateComboKey == mod.Directory;
                bool comboClicked = Boutique.DrawStateComboBody(dl, stateMin, stateMax,
                    stateValue, isOpen, scale, $"sc_{mod.Directory}");
                if (comboClicked)
                {
                    if (!requiresCtrlClick || ImGui.GetIO().KeyCtrl)
                    {
                        openStateComboKey = mod.Directory;
                        ImGui.OpenPopup($"##scp_{mod.Directory}");
                    }
                    else
                    {
                        Boutique.Tooltip("Hold Ctrl to toggle this design-scoped mod");
                    }
                }

                Boutique.PushBoutiquePopupStyles(scale);
                ImGui.SetNextWindowPos(new Vector2(stateMin.X, stateMax.Y + 2f * scale));
                if (ImGui.BeginPopup($"##scp_{mod.Directory}"))
                {
                    float popupW = 184f * scale;
                    if (Boutique.DrawStatePopupItem(dl, popupW, scale,
                        Boutique.StateValue.Enable, stateValue == Boutique.StateValue.Enable, $"e_{mod.Directory}"))
                    {
                        selectedMods[mod.Directory] = true;
                        modsToInherit.Remove(mod.Directory);
                        RunModAnalysis(mod);
                        ImGui.CloseCurrentPopup();
                        openStateComboKey = null;
                    }
                    if (Boutique.DrawStatePopupItem(dl, popupW, scale,
                        Boutique.StateValue.Disable, stateValue == Boutique.StateValue.Disable, $"d_{mod.Directory}"))
                    {
                        selectedMods[mod.Directory] = false;
                        modsToInherit.Remove(mod.Directory);
                        ClearModAnalysis(mod);
                        ImGui.CloseCurrentPopup();
                        openStateComboKey = null;
                    }
                    if (mod.IsInherited)
                    {
                        if (Boutique.DrawStatePopupItem(dl, popupW, scale,
                            Boutique.StateValue.Inherit, stateValue == Boutique.StateValue.Inherit, $"i_{mod.Directory}"))
                        {
                            selectedMods.Remove(mod.Directory);
                            modsToInherit.Add(mod.Directory);
                            ClearModAnalysis(mod);
                            ImGui.CloseCurrentPopup();
                            openStateComboKey = null;
                        }
                    }
                    ImGui.EndPopup();
                }
                else if (isOpen)
                {
                    openStateComboKey = null;
                }
                Boutique.PopBoutiquePopupStyles();
            }
            else
            {
                // Checkbox mode (binary On/Off). No "ON"/"OFF" text, the
                // check glyph alone communicates state, and the cluster sits
                // closer to the checkbox without the label slot reserved.
                bool isOn = currentState == 0;
                bool checkboxClicked = Boutique.DrawBoutiqueCheckbox(dl, stateMin, scale, isOn,
                    $"chk_{mod.Directory}", label: null,
                    wrapperWidth: 0f);
                if (checkboxClicked)
                {
                    if (!requiresCtrlClick || ImGui.GetIO().KeyCtrl)
                    {
                        bool newOn = !isOn;
                        selectedMods[mod.Directory] = newOn;
                        if (newOn) RunModAnalysis(mod);
                        else ClearModAnalysis(mod);
                    }
                    else
                    {
                        Boutique.Tooltip("Hold Ctrl to toggle this design-scoped mod");
                    }
                }
            }
            // In combo mode the state control is StateCtrlW wide. In checkbox
            // mode (no label, no wrapper) the box is just 14 px wide; the
            // cluster sits flush to its right edge with the standard cluster
            // gap.
            float stateActualW = plugin.Configuration.RespectPenumbraInheritance
                ? stateW
                : Boutique.CheckboxSide * scale;
            xCursor = stateMin.X + stateActualW + stateToCluster;

            // ── Pin + Gear cluster ──────────────────────────────────────
            float iconFs = UiBuilder.IconFont.FontSize * 0.65f;

            var pinMin = new Vector2(xCursor, midY - iconSide * 0.5f);
            // Both states use a pin glyph (the unpinned state was a bookmark
            // before, which read as a different concept). Pinned state uses
            // the filled thumbtack to imply "stuck in place"; unpinned uses
            // the unfilled map-pin outline to imply "available to pin".
            string pinGlyph = isPinned
                ? FontAwesomeIcon.Thumbtack.ToIconString()
                : FontAwesomeIcon.MapPin.ToIconString();
            // Idle bumped from TextGhost to TextDim so the unpinned pin is
            // visibly readable; hover lifts to gold.
            Vector4 pinIdle = isPinned ? Boutique.Gold : Boutique.TextDim;
            Vector4 pinHover = isPinned ? Boutique.GoldBright : Boutique.GoldWarm;
            string pinTooltip = isPinned
                ? "Unpin (mod will get auto-disabled when switching)"
                : "Pin (mod will never get auto-disabled)";
            if (Boutique.DrawClusterIcon(dl, pinMin, scale, $"pin_{mod.Directory}",
                UiBuilder.IconFont, iconFs, pinGlyph, pinIdle, pinHover, pinTooltip))
            {
                if (isPinned)
                {
                    pinnedMods.Remove(mod.Directory);
                }
                else
                {
                    pinnedMods.Add(mod.Directory);
                    selectedMods[mod.Directory] = true;
                }
            }
            xCursor += iconSide + clusterGap;

            // Gear always renders so the cluster geometry is constant. Color
            // varies by state: TextGhost when the mod has no configurable
            // options, dim cyan-soft when it has options but none customised,
            // bright cyan-soft when the user has custom options for this
            // character/design.
            var gearMin = new Vector2(xCursor, midY - iconSide * 0.5f);
            Vector4 gearIdle, gearHover;
            string gearTooltip;
            if (!hasOptions)
            {
                // Brighter than TextGhost so the gear is readable when no
                // options exist; hover stays the same colour (no interaction).
                gearIdle = Boutique.TextDim;
                gearHover = Boutique.TextDim;
                gearTooltip = "No configuration options";
            }
            else if (hasCustomOptions)
            {
                gearIdle = Boutique.WithAlpha(Boutique.CyanSoft, 0.95f);
                gearHover = Boutique.Cyan;
                gearTooltip = "Edit mod configuration options";
            }
            else
            {
                gearIdle = Boutique.WithAlpha(Boutique.CyanSoft, 0.75f);
                gearHover = Boutique.CyanSoft;
                gearTooltip = "Configure mod options";
            }
            if (Boutique.DrawClusterIcon(dl, gearMin, scale, $"gear_{mod.Directory}",
                UiBuilder.IconFont, iconFs, FontAwesomeIcon.Cog.ToIconString(),
                gearIdle, gearHover, gearTooltip))
            {
                if (hasOptions) OpenModOptionsPanel(mod);
            }
            xCursor += iconSide + nameGap;

            // ── Mod name (truncates with ...) ───────────────────────────
            float nameMaxX = rowMax.X - padR;
            using (Boutique.Body13?.Push())
            {
                float fh = ImGui.GetFontSize();
                float nameAvail = nameMaxX - xCursor;
                string displayName = Boutique.TruncateToWidth(mod.Name, nameAvail);
                Vector4 nameColor;
                if (currentState == 0) nameColor = Boutique.Text;
                else if (currentState == 1) nameColor = Boutique.TextDim;
                else nameColor = Boutique.TextDim; // Inherit also dims
                dl.AddText(new Vector2(xCursor, midY - fh * 0.5f),
                    Boutique.U32(nameColor), displayName);
            }

            // Invisible button over name area for tooltip + right-click context menu
            ImGui.SetCursorScreenPos(new Vector2(xCursor, rowMin.Y));
            float nameAreaW = nameMaxX - xCursor;
            float nameAreaH = rowMax.Y - rowMin.Y;
            if (nameAreaW > 4f * scale && nameAreaH > 0f)
            {
                ImGui.InvisibleButton($"##ModRow_{mod.Directory}", new Vector2(nameAreaW, nameAreaH));
                bool nameHovered = ImGui.IsItemHovered();
                DrawModCategoryContextMenu(mod);

                if (nameHovered)
                {
                    DrawModRowTooltip(mod, requiresCtrlClick);
                }
            }

            // Advance cursor to row end. Contextual warning (if any) renders below.
            ImGui.SetCursorScreenPos(new Vector2(rowMin.X, rowMax.Y));

            // Show contextual warnings for selected mods (existing behaviour)
            if (isSelected && mod.Analysis != null && !dismissedWarnings.Contains(mod.Directory))
            {
                DrawContextualWarning(mod);
            }
        }

        // Boutique tooltip for the mod row name area: full mod name, categories,
        // dependencies (with met/unmet status), and a hint about the design-scoped
        // gating when applicable.
        private void DrawModRowTooltip(ModEntry mod, bool requiresCtrlClick)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(mod.Name);
            if (mod.Categories.Any())
            {
                sb.Append("\nCategories: ");
                sb.Append(string.Join(", ", mod.Categories));
            }
            if (mod.Dependencies.Any())
            {
                sb.Append("\n\nDependencies:");
                foreach (var dep in mod.Dependencies)
                {
                    string status = dep.IsFound
                        ? (selectedMods.TryGetValue(dep.RequiredModPath, out var v) && v ? "[OK]" : "[MISSING]")
                        : "[NOT FOUND]";
                    sb.Append($"\n  {status} {dep.RequiredModName}");
                }
            }
            if (mod.HasOnlyModels)
                sb.Append("\n\nOnly contains models, needs a texture mod to look right.");
            else if (mod.HasOnlyTextures)
                sb.Append("\n\nOnly contains textures/materials, needs a model mod.");
            if (requiresCtrlClick)
                sb.Append("\n\nHold Ctrl to toggle this design-scoped mod.");

            // Wider wrap than default so dependency lists stay readable
            Boutique.Tooltip(sb.ToString(), 320f);
        }

        private void RunModAnalysis(ModEntry mod)
        {
            mod.Analysis = plugin.PenumbraIntegration?.AnalyzeModForDependenciesAndConflicts(
                mod.Directory, mod.Name, mod.ModType, selectedMods);

            if (mod.Analysis != null)
            {
                mod.HasDependency = mod.Analysis.HasDependency;
                mod.DependencyType = mod.Analysis.DependencyType;
                mod.HasConflicts = mod.Analysis.HasConflicts;
                mod.ConflictingMods = mod.Analysis.ConflictingMods;
            }

            dismissedWarnings.Remove(mod.Directory);

            if (mod.Dependencies.Any())
            {
                HandleDependencySelection(mod);
            }
        }

        private void ClearModAnalysis(ModEntry mod)
        {
            mod.Analysis = null;
            mod.HasDependency = false;
            mod.DependencyType = "";
            mod.HasConflicts = false;
            mod.ConflictingMods = new List<string>();
        }

        private void DrawModCategoryContextMenu(ModEntry mod)
        {
            var modIdentifier = $"{mod.Name}|{mod.Directory}";
            var isManuallyOverridden = plugin.UserOverrideManager.HasOverride(modIdentifier);

            // Use standard context menu on the invisible button
            if (ImGui.BeginPopupContextItem($"ModCategoryMenu_{mod.Directory}"))
            {
                ImGui.Text($"Move '{mod.Name}' to:");
                ImGui.Separator();

                // Get all category types and names
                var categoryTypes = new ModType[]
                {
                    ModType.Gear, ModType.Hair, ModType.Body, ModType.Tattoos,
                    ModType.Eyes, ModType.EarsTails, ModType.Face, ModType.FacePaint,
                    ModType.Mount, ModType.Minion, ModType.Emote, ModType.StandingIdle,
                    ModType.ChairSitting, ModType.GroundSitting, ModType.LyingDozing,
                    ModType.MixedIdle, ModType.Movement, ModType.JobVFX, ModType.VFX,
                    ModType.Skeleton, ModType.Other
                };

                var categoryDisplayNames = new string[]
                {
                    "Gear", "Hair", "Bodies", "Tattoos", "Eyes", "Ears/Horns/Tails",
                    "Sculpts", "Makeup/Face Paint", "Mounts", "Minions", "Emotes", "Standing Idle",
                    "Chair Sitting", "Ground Sitting", "Lying/Dozing", "Mixed Idle",
                    "Movement", "Job VFX", "VFX", "Skeletons", "Other"
                };

                for (int i = 0; i < categoryTypes.Length; i++)
                {
                    var categoryType = categoryTypes[i];
                    var displayName = categoryDisplayNames[i];

                    // Show current category with checkmark
                    var isCurrent = mod.ModType == categoryType;
                    var text = isCurrent ? $"✓ {displayName}" : displayName;

                    if (ImGui.MenuItem(text, "", isCurrent))
                    {
                        if (!isCurrent)
                        {
                            // Set override and update mod type
                            plugin.UserOverrideManager.SetOverride(modIdentifier, categoryType);
                            mod.ModType = categoryType;

                            // Also update the cache so future loads use the correct category
                            if (plugin.modCategorizationCache != null)
                            {
                                plugin.modCategorizationCache[mod.Directory] = categoryType;
                            }

                            Plugin.Log.Info($"[UserOverride] Moved '{mod.Name}' to {displayName} category");

                            // Save cache to disk immediately to prevent loss on crash
                            plugin.UpdateModCache(mod.Directory, mod.Name, categoryType);
                        }
                    }
                }

                ImGui.Separator();

                // Reset to automatic option
                if (isManuallyOverridden)
                {
                    if (ImGui.MenuItem("Reset to Automatic"))
                    {
                        plugin.UserOverrideManager.RemoveOverride(modIdentifier);

                        // Re-analyze mod type automatically
                        mod.ModType = DetermineModTypeFromPaths(mod.Directory, mod.Name, null);

                        // Also update the cache with the automatic categorization
                        if (plugin.modCategorizationCache != null)
                        {
                            plugin.modCategorizationCache[mod.Directory] = mod.ModType;
                        }

                        Plugin.Log.Info($"[UserOverride] Reset '{mod.Name}' to automatic categorization: {mod.ModType}");

                        // Save cache to disk immediately to prevent loss on crash
                        plugin.UpdateModCache(mod.Directory, mod.Name, mod.ModType);
                    }
                }
                else
                {
                    ImGui.TextDisabled("(Automatically categorized)");
                }

                ImGui.EndPopup();
            }
        }

        /// <summary>
        /// Determines mod type by analyzing the actual file paths that the mod affects.
        /// This reads the mod's JSON files directly to get the real game file paths.
        /// </summary>
        private ModType DetermineModTypeFromPaths(string modDir, string modName, Dictionary<string, object?>? changedItems)
        {
            try
            {
                // Check for user override first - user's choice always wins
                var modIdentifier = $"{modName}|{modDir}";
                if (plugin.UserOverrideManager.HasOverride(modIdentifier))
                {
                    var overrideType = plugin.UserOverrideManager.GetOverride(modIdentifier);
                    if (overrideType.HasValue)
                    {
                        return overrideType.Value;
                    }
                }

                // Try to get actual file paths by reading mod JSON files directly
                // Get the full mod directory path from Penumbra
                var penumbraModPath = plugin.PenumbraIntegration?.GetModDirectory();
                if (string.IsNullOrEmpty(penumbraModPath))
                {
                    // Could not get Penumbra mod directory (log removed to prevent spam)
                    var fetchedChangedItems = plugin.PenumbraIntegration?.GetModChangedItems(modDir, modName);
                    return AnalyzeModFromItemNames(modName, fetchedChangedItems ?? new Dictionary<string, object?>());
                }

                var fullModPath = Path.Combine(penumbraModPath, modDir);
                var modFiles = GetModFilePathsFromJson(fullModPath, modName);
                if (!modFiles.Any())
                {
                    // No file paths found in mod JSON - falling back to changed items (log removed to prevent spam)

                    // Fallback to changed items (which gives item names)
                    var fetchedItems = plugin.PenumbraIntegration?.GetModChangedItems(modDir, modName);
                    if (fetchedItems == null || !fetchedItems.Any())
                    {
                        // No changed items for mod - falling back to name-based detection (log removed to prevent spam)
                        return DetermineModTypeFromName(modDir, modName);
                    }

                    return AnalyzeModFromItemNames(modName, fetchedItems);
                }


                // Count different types of changes to determine primary purpose
                var typeCounts = new Dictionary<ModType, int>
                {
                    [ModType.Gear] = 0,
                    [ModType.Hair] = 0,
                    [ModType.Face] = 0,
                    [ModType.Eyes] = 0,
                    [ModType.Tattoos] = 0,
                    [ModType.FacePaint] = 0,
                    [ModType.Body] = 0,
                    [ModType.EarsTails] = 0,
                    [ModType.Mount] = 0,
                    [ModType.Minion] = 0,
                    [ModType.Emote] = 0,
                    [ModType.StandingIdle] = 0,
                    [ModType.ChairSitting] = 0,
                    [ModType.GroundSitting] = 0,
                    [ModType.LyingDozing] = 0,
                    [ModType.Movement] = 0,
                    [ModType.JobVFX] = 0,
                    [ModType.VFX] = 0,
                    [ModType.Skeleton] = 0,
                    [ModType.Other] = 0
                };

                var hasBodyPaths = false;
                var hasSmallclothesPaths = false;
                var hasAnimationPaths = false;
                var hasVfxPaths = false;
                var uncategorizedTextures = 0;

                // Analyze each file path using proper FFXIV file path patterns
                foreach (var filePath in modFiles)
                {
                    var pathLower = filePath.ToLowerInvariant();


                    // Eyes - iris/eye textures
                    if (pathLower.Contains("_iri_") || pathLower.Contains("/eye/"))
                    {
                        typeCounts[ModType.Eyes]++;
                    }
                    // Face Paint/Makeup - face decals only
                    else if (pathLower.Contains("decal_face"))
                    {
                        typeCounts[ModType.FacePaint]++;
                    }
                    // Face Sculpts - actual face model changes
                    if ((pathLower.Contains("chara/human/") && pathLower.Contains("/obj/face/") && pathLower.Contains(".mdl")) ||
                             pathLower.Contains("_fac.mdl"))
                    {
                        typeCounts[ModType.Face]++;
                    }
                    // Face Paint/Makeup - face textures (will be overridden if models are also present)
                    if ((pathLower.Contains("chara/human/") && pathLower.Contains("/obj/face/") && pathLower.Contains(".tex")) ||
                             (pathLower.Contains("_fac_base.tex") || pathLower.Contains("_fac_norm.tex")))
                    {
                        typeCounts[ModType.FacePaint]++;
                    }
                    // Tattoos vs Scales distinction - check mod name and description OR direct tattoo paths
                    else if (pathLower.Contains("/tattoo/") ||
                            ((pathLower.Contains("_base.tex") || pathLower.Contains("_b_d.tex")) &&
                             (pathLower.Contains("bibo") || pathLower.Contains("tbse") || pathLower.Contains("gen3") ||
                              pathLower.Contains("eve") || pathLower.Contains("nyaughty"))))
                    {
                        var modNameLower = modName.ToLowerInvariant();

                        // Direct tattoo path = tattoos
                        if (pathLower.Contains("/tattoo/"))
                        {
                            typeCounts[ModType.Tattoos]++;
                        }
                        // Check if it's a scales mod (skin modification) vs tattoo (overlay)
                        else if (modNameLower.Contains("scale") || modNameLower.Contains("skin") || modNameLower.Contains("dragonborn"))
                        {
                            typeCounts[ModType.Body]++;
                        }
                        else if (modNameLower.Contains("tattoo") || modNameLower.Contains("ink"))
                        {
                            typeCounts[ModType.Tattoos]++;
                        }
                        else
                        {
                            // Default to tattoos to be safe if both face and body textures
                            typeCounts[ModType.Tattoos]++;
                        }
                    }
                    // Equipment Decals - go to Other
                    else if (pathLower.Contains("decal_equip") || pathLower.Contains("_stigma"))
                    {
                        typeCounts[ModType.Other]++;
                    }
                    // Body modifications - actual body models (not just textures)
                    else if (pathLower.Contains("_bdy.mdl") ||
                             (pathLower.Contains("chara/human/") && pathLower.Contains("/obj/body/") && pathLower.Contains(".mdl")))
                    {
                        hasBodyPaths = true;
                        typeCounts[ModType.Body]++;
                    }
                    // Smallclothes - base underwear (e0000) that body mods modify
                    else if (pathLower.Contains("chara/equipment/e0000/"))
                    {
                        hasSmallclothesPaths = true;
                        // Don't count as gear - will be handled by body+smallclothes rule
                    }
                    // Tattoos - body textures without models (like --c0101b0001_b_d.tex) AND body framework tattoos
                    else if ((pathLower.Contains("chara/human/") && pathLower.Contains("/obj/body/") && pathLower.Contains(".tex")) ||
                             pathLower.Contains("/skin/") && pathLower.Contains(".tex") ||
                             // Body framework tattoo patterns
                             pathLower.Contains("chara/bibo_") && pathLower.Contains(".tex") ||
                             pathLower.Contains("chara/gen3_") && pathLower.Contains(".tex") ||
                             pathLower.Contains("chara/tbse_") && pathLower.Contains(".tex") ||
                             pathLower.Contains("chara/rue_") && pathLower.Contains(".tex") ||
                             pathLower.Contains("chara/yab_") && pathLower.Contains(".tex"))
                    {
                        typeCounts[ModType.Tattoos]++;
                    }
                    // Ears - race-specific ear modifications (zear)
                    else if (pathLower.Contains("chara/human/") && (pathLower.Contains("/obj/zear/") || pathLower.Contains("_zer_")))
                    {
                        typeCounts[ModType.EarsTails]++;
                    }
                    // Tails - race-specific tail modifications
                    else if (pathLower.Contains("chara/human/") && (pathLower.Contains("/obj/tail/") || pathLower.Contains("_til_")))
                    {
                        typeCounts[ModType.EarsTails]++;
                    }
                    // Equipment ears/tails ONLY (actual ear/tail equipment)
                    else if ((pathLower.Contains("chara/accessory/") || pathLower.Contains("chara/equipment/")) &&
                             (pathLower.Contains("_ear_") ||
                              (pathLower.Contains("tail") && !pathLower.Contains("_til_") &&
                               (modName.ToLowerInvariant().Contains("tail") || pathLower.Contains("tail")))))
                    {
                        typeCounts[ModType.EarsTails]++;
                    }
                    // Equipment (gear) - consolidated detection for all equipment types
                    else if (pathLower.Contains("chara/equipment/") || pathLower.Contains("chara/weapon/") || pathLower.Contains("chara/accessory/") ||
                             pathLower.Contains("_top.mdl") || pathLower.Contains("_met.mdl") || // Body/Chest
                             pathLower.Contains("_dwn.mdl") || pathLower.Contains("_leg.mdl") || // Legs
                             pathLower.Contains("_glv.mdl") || // Hands
                             pathLower.Contains("_sho.mdl") || // Feet
                             pathLower.Contains("_hed.mdl") || // Head
                             pathLower.Contains("_ear.mdl") || pathLower.Contains("_nek.mdl") || pathLower.Contains("_wrs.mdl") || // Accessories
                             pathLower.Contains("_rir.mdl") || pathLower.Contains("_ril.mdl") || // Rings
                             pathLower.Contains("_a.mdl") || pathLower.Contains("_b.mdl") || pathLower.Contains("_c.mdl") || pathLower.Contains("_d.mdl") || pathLower.Contains("_s.mdl")) // Weapons
                    {
                        typeCounts[ModType.Gear]++;
                    }
                    // Hair modifications - paths AND specific hair file patterns AND custom hair textures
                    else if ((pathLower.Contains("chara/human/") && pathLower.Contains("/obj/hair/")) ||
                             pathLower.Contains("_hir.mdl") || pathLower.Contains("_hir_") ||
                             // Custom hair texture patterns commonly used in hair mods
                             (pathLower.Contains("/hair_") && pathLower.Contains(".tex")) ||
                             (pathLower.Contains("/scalp_") && pathLower.Contains(".tex")) ||
                             pathLower.Contains("chara/hair/") || // Some mods use chara/hair/ directly
                             (pathLower.Contains("chara/") && pathLower.Contains("hair") && pathLower.Contains(".tex"))) // General hair texture patterns
                    {
                        typeCounts[ModType.Hair]++;
                    }
                    // Mount/Minion/NPC detection - check for mount-specific paths and names
                    else if (pathLower.Contains("chara/mount/") || pathLower.Contains("chara/demihuman/") ||
                             pathLower.Contains("chara/monster/") || pathLower.Contains("chara/minion/") ||
                             (pathLower.Contains("bg/ffxiv/") && pathLower.Contains("/obj/")))
                    {
                        var modNameLower = modName.ToLowerInvariant();
                        // Check for mount indicators: dedicated mount paths, mount animations, or mount keywords
                        if (pathLower.Contains("chara/mount/") ||
                            pathLower.Contains("/mt_m") && pathLower.Contains("/resident/mount.pap") ||
                            modNameLower.Contains("mount") || modNameLower.Contains("chocobo") ||
                            modNameLower.Contains("horse") || modNameLower.Contains("riding"))
                        {
                            typeCounts[ModType.Mount]++;
                        }
                        // Check for minion indicators: dedicated minion paths or minion keywords
                        else if (pathLower.Contains("chara/minion/") ||
                                 modNameLower.Contains("minion") || modNameLower.Contains("pet") ||
                                 modNameLower.Contains("loft") || modNameLower.Contains("companion"))
                        {
                            typeCounts[ModType.Minion]++;
                        }
                        else
                        {
                            typeCounts[ModType.Other]++;
                        }
                    }
                    // Animation detection with specific idle subcategories based on file paths
                    else if (pathLower.Contains("/emote/") || pathLower.Contains("/animation/") || pathLower.Contains(".pap"))
                    {
                        hasAnimationPaths = true;
                        var modNameLower = modName.ToLowerInvariant();

                        // Standing Idle - pose##_loop.pap, pose##_start.pap
                        if (pathLower.Contains("/emote/pose") && !pathLower.Contains("s_pose") && !pathLower.Contains("j_pose") && !pathLower.Contains("l_pose"))
                        {
                            typeCounts[ModType.StandingIdle]++;
                        }
                        // Chair Sitting - s_pose##, sit.pap
                        else if (pathLower.Contains("/emote/s_pose") || pathLower.Contains("/emote/sit.pap") || pathLower.Contains("event_base_chair"))
                        {
                            typeCounts[ModType.ChairSitting]++;
                        }
                        // Ground Sitting - j_pose##, jmn.pap
                        else if (pathLower.Contains("/emote/j_pose") || pathLower.Contains("/emote/jmn.pap") || pathLower.Contains("event_base_ground"))
                        {
                            typeCounts[ModType.GroundSitting]++;
                        }
                        // Lying/Dozing - l_pose##
                        else if (pathLower.Contains("/emote/l_pose"))
                        {
                            typeCounts[ModType.LyingDozing]++;
                        }
                        // Movement animations
                        else if (pathLower.Contains("walk") ||
                                 pathLower.Contains("run") ||
                                 pathLower.Contains("movement") ||
                                 modNameLower.Contains("walk") ||
                                 modNameLower.Contains("movement") ||
                                 modNameLower.Contains("run"))
                        {
                            typeCounts[ModType.Movement]++;
                        }
                        // Everything else is emote (dance, gesture, etc.)
                        else
                        {
                            typeCounts[ModType.Emote]++;
                        }
                    }
                    // VFX detection - enhanced pattern matching
                    else if (pathLower.Contains("/vfx/") || pathLower.Contains(".avfx") || pathLower.Contains(".vfx") || pathLower.Contains("/effect/"))
                    {
                        hasVfxPaths = true;
                        var modNameLower = modName.ToLowerInvariant();

                        // Check if it's job-related VFX
                        var jobKeywords = new[] { "ast", "whm", "sch", "sage", "blm", "rdm", "smn", "pct", "war", "pld", "drk", "gnb",
                                                "nin", "drg", "mnk", "sam", "rpr", "vpr", "brd", "mch", "dnc" };
                        var isJobVFX = jobKeywords.Any(job => modNameLower.Contains(job)) ||
                                      modNameLower.Contains("skill") || modNameLower.Contains("ability") ||
                                      modNameLower.Contains("spell") || modNameLower.Contains("weapon");

                        if (isJobVFX)
                        {
                            typeCounts[ModType.JobVFX]++;
                        }
                        else
                        {
                            typeCounts[ModType.VFX]++;
                        }
                    }
                    // Skeletons
                    else if (pathLower.Contains(".sklb") || pathLower.Contains(".eid") || pathLower.Contains(".skp"))
                    {
                        typeCounts[ModType.Skeleton]++;
                    }
                    // Supporting textures - if mod already has clear gear indicators, count textures as gear too
                    else if ((pathLower.Contains(".tex") || pathLower.Contains(".mtrl")) &&
                             (typeCounts[ModType.Gear] > 0 || // Already detected gear files
                              modName.ToLowerInvariant().Contains("cardigan") || modName.ToLowerInvariant().Contains("dress") ||
                              modName.ToLowerInvariant().Contains("shirt") || modName.ToLowerInvariant().Contains("pants") ||
                              modName.ToLowerInvariant().Contains("armor") || modName.ToLowerInvariant().Contains("coat") ||
                              pathLower.Contains("cardigan") || pathLower.Contains("dress") ||
                              pathLower.Contains("shirt") || pathLower.Contains("pants")))
                    {
                        typeCounts[ModType.Gear]++;
                    }
                    else
                    {
                        // Track uncategorized texture files separately
                        if (pathLower.Contains(".tex"))
                        {
                            uncategorizedTextures++;
                        }
                        typeCounts[ModType.Other]++;
                    }
                }

                // Log the type counts for debugging
                // Removed type analysis debug logging

                // Check if this is a creature-type mod and classify via changed items
                var hasCreaturePaths = modFiles.Any(path => {
                    var pathLower = path.ToLowerInvariant();
                    return pathLower.Contains("chara/mount/") || pathLower.Contains("chara/demihuman/") ||
                           pathLower.Contains("chara/monster/") || pathLower.Contains("chara/minion/") ||
                           (pathLower.Contains("bg/ffxiv/") && pathLower.Contains("/obj/")) ||
                           pathLower.Contains("/mt_m") && pathLower.Contains("/resident/mount.pap");
                });

                ModType result;
                if (hasCreaturePaths)
                {
                    // Use changed items to classify creature type
                    result = ClassifyCreatureTypeFromChangedItems(modDir, modName);
                    // Removed spam log: Plugin.Log.Information($"[SecretMode] Creature-type mod '{modName}' classified as {result} via changed items");
                }
                else
                {
                    // Determine primary type with smart logic
                    result = DeterminePrimaryModType(modName, typeCounts, hasBodyPaths, hasSmallclothesPaths, hasAnimationPaths, hasVfxPaths, uncategorizedTextures);
                }

                return result;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[SecretMode] Error analyzing paths for mod {modName}: {ex}");
                return DetermineModTypeFromName(modDir, modName);
            }
        }

        /// <summary>
        /// Determine the primary mod type using smart logic that considers primary vs secondary purposes
        /// </summary>
        private ModType DeterminePrimaryModType(string modName, Dictionary<ModType, int> typeCounts, bool hasBodyPaths, bool hasSmallclothesPaths, bool hasAnimationPaths, bool hasVfxPaths, int uncategorizedTextures)
        {
            var modNameLower = modName.ToLowerInvariant();

            // Body + Smallclothes = Body mod (body frameworks like Neolithe, Bibo, etc.)
            if (hasBodyPaths && hasSmallclothesPaths)
            {
                return ModType.Body;
            }

            // Face Sculpts vs Makeup - if both Face and FacePaint are present, prioritize Face (sculpts include textures)
            if (typeCounts[ModType.Face] > 0 && typeCounts[ModType.FacePaint] > 0)
            {
                return ModType.Face;
            }

            // Hair + uncategorized textures = Hair mod (if Hair is primary detected content)
            if (typeCounts[ModType.Hair] > 0 && uncategorizedTextures > 0 &&
                typeCounts[ModType.Hair] >= typeCounts[ModType.EarsTails] && // Don't override ears/tails mods
                typeCounts[ModType.Hair] >= typeCounts[ModType.Gear] && // Don't override gear mods
                typeCounts[ModType.Hair] >= typeCounts[ModType.Body]) // Don't override body mods
            {
                return ModType.Hair;
            }

            // COMPREHENSIVE MOD ANALYSIS: Determine primary purpose for mods with multiple content types
            var bodyScore = typeCounts[ModType.Body] + (hasBodyPaths ? 5 : 0);
            var gearScore = typeCounts[ModType.Gear];
            var hairScore = typeCounts[ModType.Hair];
            var tattooScore = typeCounts[ModType.Tattoos];

            // Known body mod frameworks - these should be Body even if they include gear
            var isBodyFramework = modNameLower.Contains("bibo") || modNameLower.Contains("rue") || modNameLower.Contains("gen3") ||
                                 modNameLower.Contains("tbse") || modNameLower.Contains("yab") || modNameLower.Contains("the_body");

            // If it's a body framework with substantial body content, prioritize Body over gear
            if (isBodyFramework && bodyScore >= 3)
            {
                return ModType.Body;
            }

            // If gear heavily outweighs body content and it's not a known body framework
            if (!isBodyFramework && gearScore >= 4 && gearScore > (bodyScore + tattooScore))
            {
                return ModType.Gear;
            }

            // Traditional body mods (non-framework but body-focused)
            if (hasBodyPaths && (modNameLower.Contains("body") && !modNameLower.Contains("armor") && !modNameLower.Contains("gear")))
            {
                return ModType.Body;
            }

            // If it's primarily animations, classify by animation type
            if (hasAnimationPaths)
            {
                // Check for mixed idle animations first
                var idleTypes = new[] {
                    typeCounts[ModType.StandingIdle],
                    typeCounts[ModType.ChairSitting],
                    typeCounts[ModType.GroundSitting],
                    typeCounts[ModType.LyingDozing]
                };
                var idleTypeCount = idleTypes.Count(count => count > 0);

                // If it affects multiple idle types, classify as Mixed Idle
                if (idleTypeCount > 1)
                {
                    return ModType.MixedIdle;
                }

                // Otherwise, return the most prominent animation type
                var animationTypes = new[] {
                    (ModType.Emote, typeCounts[ModType.Emote]),
                    (ModType.StandingIdle, typeCounts[ModType.StandingIdle]),
                    (ModType.ChairSitting, typeCounts[ModType.ChairSitting]),
                    (ModType.GroundSitting, typeCounts[ModType.GroundSitting]),
                    (ModType.LyingDozing, typeCounts[ModType.LyingDozing]),
                    (ModType.Movement, typeCounts[ModType.Movement])
                };
                var topAnimationType = animationTypes.OrderByDescending(t => t.Item2).First().Item1;
                return topAnimationType;
            }

            // If it's primarily VFX, classify by VFX type
            if (hasVfxPaths && (typeCounts[ModType.VFX] > 0 || typeCounts[ModType.JobVFX] > 0))
            {
                // Prioritize Job VFX over general VFX
                if (typeCounts[ModType.JobVFX] > 0)
                {
                    return ModType.JobVFX;
                }
                else
                {
                    return ModType.VFX;
                }
            }

            // Find the type with the most changes
            var dominantType = typeCounts.OrderByDescending(kvp => kvp.Value).First();

            // Only classify if we have significant evidence
            if (dominantType.Value > 0)
            {
                return dominantType.Key;
            }

            // Final fallback
            return ModType.Unknown;
        }


        /// <summary>
        /// Fallback method: determine mod type from name when path analysis isn't available
        /// Very conservative approach to avoid false positives
        /// </summary>
        private ModType DetermineModTypeFromName(string modDir, string modName)
        {
            var nameToCheck = modName.ToLowerInvariant();


            // Only very specific and unambiguous patterns

            // Known body mods (exact matches only)
            if (nameToCheck == "bibo+" || nameToCheck == "bibo" || nameToCheck == "ivcs")
            {
                return ModType.Body;
            }

            // Very obvious hair mods
            if (nameToCheck.StartsWith("hair ") || nameToCheck.EndsWith(" hair") ||
                (nameToCheck.Contains("hair") && !nameToCheck.Contains("gear") && !nameToCheck.Contains("outfit")))
            {
                return ModType.Hair;
            }

            // Animation/emote keywords - try to distinguish idle types
            if (nameToCheck.Contains("emote") || nameToCheck.Contains("animation") || nameToCheck.Contains("pose"))
            {
                if (nameToCheck.Contains("idle") || nameToCheck.Contains("thinking"))
                {
                    return ModType.StandingIdle;
                }
                else if (nameToCheck.Contains("sit") && nameToCheck.Contains("chair"))
                {
                    return ModType.ChairSitting;
                }
                else if (nameToCheck.Contains("sit") && (nameToCheck.Contains("ground") || nameToCheck.Contains("gsit")))
                {
                    return ModType.GroundSitting;
                }
                else if (nameToCheck.Contains("doze") || nameToCheck.Contains("sleep") || nameToCheck.Contains("lying"))
                {
                    return ModType.LyingDozing;
                }
                else
                {
                    return ModType.Emote;
                }
            }

            // VFX keywords
            if (nameToCheck.Contains("vfx") || nameToCheck.Contains("effect"))
            {
                return ModType.VFX;
            }

            // When in doubt, classify as Unknown rather than guessing
            // Mod could not be classified by name, using Unknown (log removed to prevent spam)
            return ModType.Unknown;
        }

        /// <summary>
        /// Gets actual game file paths by reading mod JSON files directly (like RoleplayingVoiceDalamud does)
        /// </summary>
        private List<string> GetModFilePathsFromJson(string modDirectory, string modName)
        {
            var filePaths = new List<string>();

            try
            {
                if (!Directory.Exists(modDirectory))
                {
                    // Mod directory does not exist (log removed to prevent spam)
                    return filePaths;
                }

                // Look for JSON files in the mod directory
                foreach (string file in Directory.EnumerateFiles(modDirectory, "*.json"))
                {
                    if (file.EndsWith("meta.json")) continue; // Skip meta.json files

                    try
                    {
                        string jsonContent = File.ReadAllText(file);

                        // Try to parse as either default_mod.json or group JSON
                        if (file.EndsWith("default_mod.json"))
                        {
                            // Parse default mod option
                            var option = System.Text.Json.JsonSerializer.Deserialize<ModOption>(jsonContent);
                            if (option?.Files != null)
                            {
                                foreach (var kvp in option.Files)
                                {
                                    if (!string.IsNullOrEmpty(kvp.Key))
                                    {
                                        filePaths.Add(kvp.Key);
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Try to parse as group JSON
                            var group = System.Text.Json.JsonSerializer.Deserialize<ModGroup>(jsonContent);
                            if (group?.Options != null)
                            {
                                foreach (var option in group.Options)
                                {
                                    if (option?.Files != null)
                                    {
                                        foreach (var kvp in option.Files)
                                        {
                                            if (!string.IsNullOrEmpty(kvp.Key))
                                            {
                                                filePaths.Add(kvp.Key);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Failed to parse JSON file (log removed to prevent spam)
                    }
                }

                // v4 fallback
                if (filePaths.Count == 0)
                    filePaths.AddRange(GetModFilePathsFromV4Meta(modDirectory));

            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[SecretMode] Error reading mod JSON files for '{modName}': {ex}");
            }

            return filePaths;
        }

        private static List<string> GetModFilePathsFromV4Meta(string modDirectory)
        {
            var filePaths = new List<string>();

            try
            {
                var metaPath = Path.Combine(modDirectory, "meta.json");
                if (!File.Exists(metaPath))
                    return filePaths;

                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(metaPath));
                var root = doc.RootElement;
                if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
                    return filePaths;
                if (!root.TryGetProperty("FileVersion", out var version) || version.ValueKind != System.Text.Json.JsonValueKind.Number || version.GetInt32() < 4)
                    return filePaths;

                CollectFilesKeys(root, filePaths);
            }
            catch
            {
            }

            return filePaths;
        }

        private static void CollectFilesKeys(System.Text.Json.JsonElement element, List<string> filePaths)
        {
            switch (element.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Name == "Files" && property.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            foreach (var file in property.Value.EnumerateObject())
                            {
                                if (!string.IsNullOrEmpty(file.Name))
                                    filePaths.Add(file.Name);
                            }
                        }
                        else
                        {
                            CollectFilesKeys(property.Value, filePaths);
                        }
                    }

                    break;
                case System.Text.Json.JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        CollectFilesKeys(item, filePaths);
                    break;
            }
        }

        // Classifies a mod from changed item names when file paths aren't available
        private ModType AnalyzeModFromItemNames(string modName, Dictionary<string, object?> changedItems)
        {
            var typeCounts = new Dictionary<ModType, int>
            {
                [ModType.Gear] = 0,
                [ModType.Hair] = 0,
                [ModType.Face] = 0,
                [ModType.Eyes] = 0,
                [ModType.Tattoos] = 0,
                [ModType.FacePaint] = 0,
                [ModType.Body] = 0,
                [ModType.EarsTails] = 0,
                [ModType.Mount] = 0,
                [ModType.Minion] = 0,
                [ModType.Emote] = 0,
                [ModType.StandingIdle] = 0,
                [ModType.VFX] = 0,
                [ModType.Skeleton] = 0,
                [ModType.Other] = 0
            };

            foreach (var (itemName, itemData) in changedItems)
            {
                var itemNameLower = itemName.ToLowerInvariant();
                var itemDataStr = itemData?.ToString()?.ToLowerInvariant() ?? "";

                // Remove debug logging for performance

                // Hair - Customization items with "hair" or "hairstyle"
                if (itemNameLower.Contains("hair") || itemDataStr.Contains("hairstyle") || itemDataStr.Contains("hair"))
                {
                    typeCounts[ModType.Hair]++;
                }
                // Eyes - look for "iris" in item names (like "Customization: Midlander Female Face (Iris) 5")
                else if (itemNameLower.Contains("iris") || itemNameLower.Contains("(iris)"))
                {
                    typeCounts[ModType.Eyes]++;
                }
                // Mount - look for "mount" in item names
                else if (itemNameLower.Contains("mount") || itemNameLower.Contains("(mount)"))
                {
                    typeCounts[ModType.Mount]++;
                }
                // Minion - look for "minion" in item names  
                else if (itemNameLower.Contains("(companion)") || itemNameLower.Contains("companion") ||
                         itemNameLower.Contains("minion") || itemNameLower.Contains("(minion)"))
                {
                    typeCounts[ModType.Minion]++;
                }
                // Face Paint - look for face decal or face paint
                else if (itemNameLower.Contains("face decal") || itemNameLower.Contains("face paint") ||
                         itemNameLower.Contains("facepaint") || itemNameLower.Contains("decal"))
                {
                    typeCounts[ModType.FacePaint]++;
                }
                // Tattoo - look for customization with tattoo, overlay or body decal patterns
                else if ((itemNameLower.Contains("customization") || itemNameLower.Contains("skin")) &&
                         (itemNameLower.Contains("tattoo") || itemNameLower.Contains("overlay") ||
                          itemNameLower.Contains("body decal") || itemNameLower.Contains("skin material")))
                {
                    typeCounts[ModType.Tattoos]++;
                }
                // Face - look for "face" but not decal, paint, iris or hair (like "Customization: Midlander Female Face 5")
                else if (itemNameLower.Contains("face") && !itemNameLower.Contains("iris") &&
                         !itemNameLower.Contains("hair") && !itemNameLower.Contains("decal") &&
                         !itemNameLower.Contains("paint"))
                {
                    typeCounts[ModType.Face]++;
                }
                // Emotes - look for emote patterns in item names
                else if (itemNameLower.Contains("emote") || itemNameLower.Contains("/emote/") ||
                         itemNameLower.Contains("pose") || itemNameLower.Contains("animation") ||
                         itemNameLower.Contains("idle") || itemNameLower.Contains("expression"))
                {
                    typeCounts[ModType.Emote]++;
                }
                // Ears/Tails
                else if (itemNameLower.Contains("tail") || itemNameLower.Contains("ear") ||
                         itemNameLower.Contains("horn"))
                {
                    typeCounts[ModType.EarsTails]++;
                }
                // Body/Customization - look for body-related customization (but not tattoos)
                else if (itemNameLower.Contains("customization") &&
                         (itemNameLower.Contains("body") || itemNameLower.Contains("skin")) &&
                         !itemNameLower.Contains("tattoo") && !itemNameLower.Contains("overlay"))
                {
                    // Check if it's a body mod or a tattoo based on other context
                    if (itemDataStr.Contains("body") || itemDataStr.Contains("smallclothes") ||
                        itemDataStr.Contains("undergarment"))
                    {
                        typeCounts[ModType.Body]++;
                    }
                    else
                    {
                        typeCounts[ModType.Tattoos]++; // Skin customizations are often tattoos
                    }
                }
                // Everything else that's not customization is likely gear
                else if (!itemNameLower.Contains("customization"))
                {
                    typeCounts[ModType.Gear]++;
                }
                else
                {
                    typeCounts[ModType.Other]++;
                }
            }

            // Check mod name for specific patterns
            var modNameLower = modName.ToLowerInvariant();
            if (modNameLower.Contains("tattoo") || modNameLower.Contains("bibo") || modNameLower.Contains("gen3") || modNameLower.Contains("tbse"))
            {
                typeCounts[ModType.Tattoos] += 5; // Give it extra weight
            }
            else if (modNameLower.Contains("body") && !modNameLower.Contains("armor"))
            {
                typeCounts[ModType.Body] += 3;
            }
            else if (modNameLower.Contains("hair"))
            {
                typeCounts[ModType.Hair] += 3;
            }
            else if (modNameLower.Contains("eye"))
            {
                typeCounts[ModType.Eyes] += 3;
            }

            // Find the dominant type
            var dominantType = typeCounts.OrderByDescending(kvp => kvp.Value).First();


            return dominantType.Value > 0 ? dominantType.Key : ModType.Unknown;
        }

        /// <summary>
        /// Classifies creature-type mods as Mount, Minion, or Other using changed items
        /// </summary>
        private ModType ClassifyCreatureTypeFromChangedItems(string modDir, string modName)
        {
            try
            {
                var changedItems = plugin.PenumbraIntegration?.GetModChangedItems(modDir, modName);
                if (changedItems == null || !changedItems.Any())
                {
                    // No changed items for creature mod - defaulting to Other (log removed to prevent spam)
                    return ModType.Other;
                }

                foreach (var (itemName, itemData) in changedItems)
                {
                    var itemNameLower = itemName.ToLowerInvariant();


                    if (itemNameLower.Contains("(mount)") || itemNameLower.Contains("mount"))
                    {
                        // Creature mod classified as Mount from item (log removed to prevent spam)
                        return ModType.Mount;
                    }

                    if (itemNameLower.Contains("(companion)") || itemNameLower.Contains("companion"))
                    {
                        // Creature mod classified as Minion from item (log removed to prevent spam)
                        return ModType.Minion;
                    }
                }

                // Creature mod has no mount/companion indicators - defaulting to Other (log removed to prevent spam)
                return ModType.Other;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[SecretMode] Error classifying creature mod '{modName}': {ex}");
                return ModType.Other;
            }
        }


        /// <summary>
        /// Detects dependencies for all loaded mods
        /// </summary>
        private void DetectAllModDependencies()
        {
            try
            {
                // Detecting dependencies for mods (log removed to prevent spam)

                foreach (var mod in availableMods)
                {
                    mod.Dependencies = DetectModDependencies(mod, availableMods);

                    if (mod.Dependencies.Any())
                    {
                        // Mod has dependencies (log removed to prevent spam)
                    }
                }

                // Update dependency flags for each mod
                UpdateModDependencyFlags();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[SecretMode] Error detecting mod dependencies: {ex}");
            }
        }

        /// <summary>
        /// Updates the dependency flags (HasOnlyModels, HasOnlyTextures) for each mod by checking their file contents
        /// </summary>
        private void UpdateModDependencyFlags()
        {
            try
            {
                var penumbraModPath = plugin.PenumbraIntegration?.GetModDirectory();
                if (string.IsNullOrEmpty(penumbraModPath))
                    return;

                foreach (var mod in availableMods)
                {
                    var fullModPath = Path.Combine(penumbraModPath, mod.Directory);
                    var (hasOnlyModels, hasOnlyTextures) = CheckModDependencyType(fullModPath);
                    mod.HasOnlyModels = hasOnlyModels;
                    mod.HasOnlyTextures = hasOnlyTextures;

                    if (hasOnlyModels)
                    {
                        // Mod contains only model files (no textures) (log removed to prevent spam)
                    }
                }
            }
            catch (Exception ex)
            {
                // Error updating HasOnlyModels flags (log removed to prevent spam)
            }
        }

        /// <summary>
        /// Checks if a mod contains only model files and no texture files
        /// </summary>
        private (bool hasOnlyModels, bool hasOnlyTextures) CheckModDependencyType(string modDirectory)
        {
            var hasModels = false;
            var hasTextures = false;

            try
            {
                if (!Directory.Exists(modDirectory))
                    return (false, false);

                // Check all JSON files for file references
                foreach (string file in Directory.EnumerateFiles(modDirectory, "*.json"))
                {
                    if (file.EndsWith("meta.json")) continue;

                    try
                    {
                        string jsonContent = File.ReadAllText(file);

                        // Simple check for file extensions in the JSON content
                        if (jsonContent.Contains(".mdl", StringComparison.OrdinalIgnoreCase))
                            hasModels = true;

                        if (jsonContent.Contains(".tex", StringComparison.OrdinalIgnoreCase) ||
                            jsonContent.Contains(".mtrl", StringComparison.OrdinalIgnoreCase))
                            hasTextures = true;

                        // If we found both, no need to continue checking
                        if (hasModels && hasTextures)
                            break;
                    }
                    catch
                    {
                        // Parse errors don't matter for this check
                    }
                }

                // v4 fallback
                if (!hasModels && !hasTextures)
                {
                    foreach (var path in GetModFilePathsFromV4Meta(modDirectory))
                    {
                        if (path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
                            hasModels = true;

                        if (path.EndsWith(".tex", StringComparison.OrdinalIgnoreCase) ||
                            path.EndsWith(".mtrl", StringComparison.OrdinalIgnoreCase))
                            hasTextures = true;

                        if (hasModels && hasTextures)
                            break;
                    }
                }
            }
            catch
            {
                // Ignore errors and assume false
                return (false, false);
            }

            // Determine the dependency type
            var hasOnlyModels = hasModels && !hasTextures;
            var hasOnlyTextures = hasTextures && !hasModels;

            return (hasOnlyModels, hasOnlyTextures);
        }

        /// <summary>
        /// Detects dependencies for a mod based on name patterns and file contents
        /// </summary>
        private List<ModDependency> DetectModDependencies(ModEntry mod, List<ModEntry> allMods)
        {
            var dependencies = new List<ModDependency>();
            var modNameLower = mod.Name.ToLowerInvariant();

            // ONLY check dependencies for gear mods that have no textures
            if (mod.ModType != ModType.Gear || !mod.HasOnlyModels)
            {
                return dependencies; // Early return for non-gear or mods with textures
            }

            // Body mods should never have dependencies - they ARE the dependency
            if (mod.ModType == ModType.Body)
            {
                return dependencies;
            }

            // Checking dependencies for texture-less gear mod (log removed to prevent spam)

            // Pattern 1: Check for body type indicators in gear mod names
            // e.g., "[Koko] Anno's Santa's Helper YAB/Rue" depends on "[Anno] Santa's Helper"
            var bodyTypes = new[] { "bibo", "bibo+", "tbse", "yab", "rue", "gen3", "citrus", "yas" };
            foreach (var bodyType in bodyTypes)
            {
                if (modNameLower.Contains(bodyType))
                {
                    // Look for potential original mod by removing body type suffixes
                    var potentialOriginalName = mod.Name;
                    foreach (var bt in bodyTypes)
                    {
                        potentialOriginalName = potentialOriginalName.Replace($" {bt}", "", StringComparison.OrdinalIgnoreCase);
                        potentialOriginalName = potentialOriginalName.Replace($" [{bt}]", "", StringComparison.OrdinalIgnoreCase);
                        potentialOriginalName = potentialOriginalName.Replace($" for {bt}", "", StringComparison.OrdinalIgnoreCase);
                        potentialOriginalName = potentialOriginalName.Replace($"/{bt}", "", StringComparison.OrdinalIgnoreCase);
                    }

                    // Search for the original mod
                    var originalMod = allMods.FirstOrDefault(m =>
                        m.Name != mod.Name &&
                        m.Name.Contains(potentialOriginalName.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (originalMod != null)
                    {
                        // Detected dependency (log removed to prevent spam)
                        dependencies.Add(new ModDependency
                        {
                            RequiredModName = originalMod.Name,
                            RequiredModPath = originalMod.Directory,
                            IsFound = true
                        });
                        break; // Only one dependency per pattern
                    }
                }
            }

            // Pattern 2: Check if mod has "[Models Only]" in name
            if (modNameLower.Contains("[models only]") || modNameLower.Contains("models only"))
            {
                // Extract the base mod name
                var baseName = mod.Name.Replace("[Models Only]", "", StringComparison.OrdinalIgnoreCase)
                                      .Replace("Models Only", "", StringComparison.OrdinalIgnoreCase)
                                      .Trim();

                // Look for the texture provider mod
                var textureMod = allMods.FirstOrDefault(m =>
                    m.Name != mod.Name &&
                    m.Name.Contains(baseName, StringComparison.OrdinalIgnoreCase) &&
                    !m.Name.Contains("[Models Only]", StringComparison.OrdinalIgnoreCase));

                if (textureMod != null)
                {
                    // Models-only mod depends on another mod for textures (log removed to prevent spam)
                    dependencies.Add(new ModDependency
                    {
                        RequiredModName = textureMod.Name,
                        RequiredModPath = textureMod.Directory,
                        IsFound = true
                    });
                }
            }

            // Pattern 3: Check meta.json for explicit dependencies mentioned in description
            try
            {
                var penumbraModPath = plugin.PenumbraIntegration?.GetModDirectory();
                if (!string.IsNullOrEmpty(penumbraModPath))
                {
                    var metaPath = Path.Combine(penumbraModPath, mod.Directory, "meta.json");
                    if (File.Exists(metaPath))
                    {
                        var metaContent = File.ReadAllText(metaPath);
                        var metaJson = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(metaContent);

                        if (metaJson != null && metaJson.TryGetValue("Description", out var descObj))
                        {
                            var description = descObj?.ToString() ?? "";

                            // Look for phrases like "requires", "depends on", "needs"
                            if (description.Contains("requires", StringComparison.OrdinalIgnoreCase) ||
                                description.Contains("depends on", StringComparison.OrdinalIgnoreCase) ||
                                description.Contains("needs", StringComparison.OrdinalIgnoreCase))
                            {
                                // Try to extract mod names from description
                                foreach (var otherMod in allMods.Where(m => m.Name != mod.Name))
                                {
                                    if (description.Contains(otherMod.Name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        // Found explicit dependency in description (log removed to prevent spam)
                                        dependencies.Add(new ModDependency
                                        {
                                            RequiredModName = otherMod.Name,
                                            RequiredModPath = otherMod.Directory,
                                            IsFound = true
                                        });
                                        break; // Only one explicit dependency to avoid duplication
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Error checking meta.json for dependencies (log removed to prevent spam)
            }

            return dependencies;
        }

        /// <summary>
        /// Handles automatic enabling of dependencies when a mod is selected
        /// </summary>
        private void HandleDependencySelection(ModEntry mod)
        {
            var unmetDependencies = mod.Dependencies
                .Where(d => d.IsFound && (!selectedMods.ContainsKey(d.RequiredModPath) || !selectedMods[d.RequiredModPath]))
                .ToList();

            if (!unmetDependencies.Any())
                return;

            // Auto-enable dependencies
            foreach (var dep in unmetDependencies)
            {
                if (selectedMods.ContainsKey(dep.RequiredModPath))
                {
                    selectedMods[dep.RequiredModPath] = true;
                    // Auto-enabled dependency (log removed to prevent spam)
                }
            }
        }

        /// <summary>
        /// Boutique pagination row: "Showing X to Y of Z mods" caption on the
        /// left, chamfered page-btn cluster on the right (First/Prev arrows,
        /// up to 7 numbered pages with the current one in gold border, then
        /// Next/Last arrows). Sits above the footer.
        /// </summary>
        private void DrawPaginationControls(int totalPages, int totalMods)
        {
            if (totalPages <= 1) return;

            float scale = (Plugin.Instance?.Configuration?.UIScaleMultiplier ?? 1f);
            scale = MathF.Max(0.85f, MathF.Min(scale, 2.0f));
            var dl = ImGui.GetWindowDrawList();

            float availW = ImGui.GetContentRegionAvail().X;
            float rowH = 32f * scale;
            var rowStart = ImGui.GetCursorScreenPos();
            var rowMin = rowStart;
            var rowMax = new Vector2(rowMin.X + availW, rowMin.Y + rowH);

            // Top hairline (gold-deep fade) above the row
            dl.AddLine(rowMin, new Vector2(rowMax.X, rowMin.Y),
                Boutique.U32(Boutique.BorderSoft), 1f * scale);

            float padX = 14f * scale;
            float midY = (rowMin.Y + rowMax.Y) * 0.5f;

            // ── Left caption: "Showing X to Y of Z mods" ────────────────
            // Bumped to Kicker12 + TextDim filler so the caption is readable
            // (was Kicker11 + TextFaint, "almost invisible" against the dark bg).
            int firstIdx = currentPage * ModsPerPage + 1;
            int lastIdx = Math.Min(firstIdx + ModsPerPage - 1, totalMods);
            using (Boutique.Kicker12?.Push())
            {
                float fs = ImGui.GetFontSize();
                float trackPx = Boutique.Track26(fs);
                float captionY = midY - fs * 0.5f;
                float xCursor = rowMin.X + padX;

                string p1 = "SHOWING ";
                Boutique.DrawTrackedText(dl, new Vector2(xCursor, captionY), p1,
                    Boutique.U32(Boutique.TextDim), trackPx);
                xCursor += Boutique.MeasureTrackedText(p1, trackPx);

                string range = $"{firstIdx} TO {lastIdx}";
                Boutique.DrawTrackedText(dl, new Vector2(xCursor, captionY), range,
                    Boutique.U32(Boutique.Text), trackPx);
                xCursor += Boutique.MeasureTrackedText(range, trackPx);

                string p2 = " OF ";
                Boutique.DrawTrackedText(dl, new Vector2(xCursor, captionY), p2,
                    Boutique.U32(Boutique.TextDim), trackPx);
                xCursor += Boutique.MeasureTrackedText(p2, trackPx);

                string total = totalMods.ToString();
                Boutique.DrawTrackedText(dl, new Vector2(xCursor, captionY), total,
                    Boutique.U32(Boutique.GoldWarm), trackPx);
                xCursor += Boutique.MeasureTrackedText(total, trackPx);

                string p3 = " MODS";
                Boutique.DrawTrackedText(dl, new Vector2(xCursor, captionY), p3,
                    Boutique.U32(Boutique.TextDim), trackPx);
            }

            // ── Right page-btn cluster ──────────────────────────────────
            float btnSide = Boutique.PageBtnSide * scale;
            float btnGap = 5f * scale;
            float btnY = midY - btnSide * 0.5f;

            // Compute window of numbered pages to show (max 7, current centred when possible)
            int maxNumbered = 7;
            int firstPage = Math.Max(0, currentPage - 3);
            int lastPage = Math.Min(totalPages - 1, firstPage + maxNumbered - 1);
            if (lastPage - firstPage + 1 < maxNumbered)
                firstPage = Math.Max(0, lastPage - maxNumbered + 1);
            int numberedCount = lastPage - firstPage + 1;

            // Total cluster width: 2 arrow groups (First+Prev, Next+Last) + numbered pages
            int totalBtns = 2 + numberedCount + 2;
            float clusterW = totalBtns * btnSide + (totalBtns - 1) * btnGap;
            float clusterX = rowMax.X - padX - clusterW;

            // Wardrobe-style transition: new current fades IN (currentT 0→1),
            // outgoing previous fades OUT (1 - currentT) over PageTransitionDur.
            float pageT = PageTransitionT;
            bool isTrans = IsPageTransitioning;
            float fadeIn = pageT;
            float fadeOut = isTrans ? (1f - pageT) : 0f;

            // First page (angles-left icon)
            bool firstDisabled = currentPage == 0;
            if (Boutique.DrawPageBtnIcon(dl, new Vector2(clusterX, btnY), scale, "first",
                UiBuilder.IconFont, UiBuilder.IconFont.FontSize * 0.55f,
                FontAwesomeIcon.AngleDoubleLeft.ToIconString(),
                current: false, disabled: firstDisabled))
            {
                TriggerPageChange(0);
            }
            clusterX += btnSide + btnGap;

            // Prev page
            bool prevDisabled = currentPage == 0;
            if (Boutique.DrawPageBtnIcon(dl, new Vector2(clusterX, btnY), scale, "prev",
                UiBuilder.IconFont, UiBuilder.IconFont.FontSize * 0.55f,
                FontAwesomeIcon.AngleLeft.ToIconString(),
                current: false, disabled: prevDisabled))
            {
                TriggerPageChange(currentPage - 1);
            }
            clusterX += btnSide + btnGap;

            // Numbered pages, current fades in, previous fades out. Track
            // each numbered button's X position so we can draw a sliding
            // gold indicator that lerps from the previous current to the new
            // current during the transition (matches the wardrobe pager
            // dot-position lerp).
            float numberedStart = clusterX;
            for (int p = firstPage; p <= lastPage; p++)
            {
                bool isCurrent = p == currentPage;
                bool isOutgoing = isTrans && p == pagePrevIdx;
                if (Boutique.DrawPageBtn(dl, new Vector2(clusterX, btnY), scale,
                    $"pg{p}", (p + 1).ToString(),
                    current: isCurrent, disabled: false,
                    currentT: isCurrent ? fadeIn : 1f,
                    outgoingT: isOutgoing ? fadeOut : 0f))
                {
                    TriggerPageChange(p);
                }
                clusterX += btnSide + btnGap;
            }

            // Sliding gold indicator: a 2px gold underline + soft glow that
            // lerps between the previous and new current button while a
            // transition is in flight. Static on the current button at rest.
            // Mirrors the wardrobe pager dot's "active position lerp".
            float SlotX(int pageIdx)
            {
                int rel = pageIdx - firstPage;
                return numberedStart + rel * (btnSide + btnGap);
            }
            int prevVisible = (pagePrevIdx >= firstPage && pagePrevIdx <= lastPage)
                ? pagePrevIdx : currentPage;
            int curVisible = currentPage;
            float fromX = SlotX(prevVisible);
            float toX = SlotX(curVisible);
            float indicatorX = isTrans ? (fromX + (toX - fromX) * pageT) : toX;
            float underY = btnY + btnSide + 1f * scale;
            var indMin = new Vector2(indicatorX + 3f * scale, underY);
            var indMax = new Vector2(indicatorX + btnSide - 3f * scale, underY + 2f * scale);
            // Soft glow stack
            for (int g = 3; g > 0; g--)
            {
                float r = g * 2f * scale;
                dl.AddRectFilled(
                    new Vector2(indMin.X - r, indMin.Y - r),
                    new Vector2(indMax.X + r, indMax.Y + r),
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.16f / g)));
            }
            dl.AddRectFilled(indMin, indMax, Boutique.U32(Boutique.Gold));

            // Next page
            bool nextDisabled = currentPage >= totalPages - 1;
            if (Boutique.DrawPageBtnIcon(dl, new Vector2(clusterX, btnY), scale, "next",
                UiBuilder.IconFont, UiBuilder.IconFont.FontSize * 0.55f,
                FontAwesomeIcon.AngleRight.ToIconString(),
                current: false, disabled: nextDisabled))
            {
                TriggerPageChange(currentPage + 1);
            }
            clusterX += btnSide + btnGap;

            // Last page
            bool lastDisabled = currentPage >= totalPages - 1;
            if (Boutique.DrawPageBtnIcon(dl, new Vector2(clusterX, btnY), scale, "last",
                UiBuilder.IconFont, UiBuilder.IconFont.FontSize * 0.55f,
                FontAwesomeIcon.AngleDoubleRight.ToIconString(),
                current: false, disabled: lastDisabled))
            {
                TriggerPageChange(totalPages - 1);
            }

            ImGui.Dummy(new Vector2(availW, rowH));
        }

        /// <summary>
        /// Draw contextual warning for a selected mod showing dependency or conflict information
        /// </summary>
        private void DrawContextualWarning(ModEntry mod)
        {
            if (mod.Analysis == null) return;

            var showWarning = false;
            var warningText = "";
            var warningColor = ColorSchemes.Dark.AccentYellow;

            // Check for dependency warnings
            if (mod.Analysis.HasDependency)
            {
                showWarning = true;
                warningText = mod.Analysis.DependencyType; // Remove emoji, will use FontAwesome icon instead
                warningColor = ColorSchemes.Dark.AccentYellow;
            }
            // Check for conflict warnings (only show if no dependency warning)
            else if (mod.Analysis.HasConflicts && mod.Analysis.ConflictingMods.Any())
            {
                showWarning = true;
                var conflictNames = mod.Analysis.ConflictingMods
                    .Select(path => availableMods.FirstOrDefault(m => m.Directory == path)?.Name ?? Path.GetFileName(path))
                    .Take(3) // Limit to 3 names to avoid huge warnings
                    .ToList();

                var nameList = string.Join(", ", conflictNames);
                if (mod.Analysis.ConflictingMods.Count > 3)
                    nameList += $" and {mod.Analysis.ConflictingMods.Count - 3} more";

                warningText = $"Conflicts with: {nameList}"; // Remove emoji, will use FontAwesome icon instead
                warningColor = ColorSchemes.Dark.AccentRed;
            }

            if (showWarning)
            {
                ImGui.Indent(30); // Indent to align with mod name

                // Warning icon using FontAwesome
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.PushStyleColor(ImGuiCol.Text, warningColor);
                ImGui.Text(FontAwesomeIcon.ExclamationTriangle.ToIconString());
                ImGui.PopStyleColor();
                ImGui.PopFont();

                ImGui.SameLine();
                ImGui.Spacing();
                ImGui.SameLine();

                // Warning text
                ImGui.PushStyleColor(ImGuiCol.Text, warningColor);
                ImGui.Text(warningText);
                ImGui.PopStyleColor();

                ImGui.SameLine();

                // Dismiss button
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.5f, 0.5f, 0.3f));
                ImGui.PushStyleColor(ImGuiCol.Text, ColorSchemes.Dark.TextMuted);
                if (ImGui.SmallButton($"{FontAwesomeIcon.Times.ToIconString()}##dismiss{mod.Directory}"))
                {
                    dismissedWarnings.Add(mod.Directory);
                }
                ImGui.PopStyleColor(2);
                ImGui.PopFont();

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Dismiss this warning");
                }

                ImGui.Unindent(30);
            }
        }

        /// <summary>
        /// Open the mod options configuration panel
        /// </summary>
        /// <summary>Helper to get the character being edited (if any).</summary>
        private Character? GetEditingCharacter()
        {
            if (editingCharacterIndex.HasValue && editingCharacterIndex.Value >= 0 &&
                editingCharacterIndex.Value < plugin.Characters.Count)
                return plugin.Characters[editingCharacterIndex.Value];
            return null;
        }

        private void OpenModOptionsPanel(ModEntry mod)
        {
            optionsEditingMod = mod;

            // Get available options from Penumbra
            availableModOptions = plugin.PenumbraIntegration.GetModOptions(mod.Directory, mod.Name);
            optionGroupTypes = new Dictionary<string, int>();

            // Parse group types from Penumbra API
            // 0 = Single-select, 1 = Multi-select
            var rawOptions = plugin.PenumbraIntegration.GetModOptionsRaw(mod.Directory, mod.Name);
            foreach (var (groupName, (optionNames, groupType)) in rawOptions)
            {
                optionGroupTypes[groupName] = groupType;
            }

            // Load current settings for this mod - check design first, then character
            if (editingDesign?.ModOptionSettings?.ContainsKey(mod.Directory) ?? false)
            {
                // Use design's saved options
                currentModOptions = new Dictionary<string, List<string>>(editingDesign.ModOptionSettings[mod.Directory]);
            }
            else if (GetEditingCharacter()?.ModOptionSettings?.ContainsKey(mod.Directory) ?? false)
            {
                // Use character's saved options
                currentModOptions = new Dictionary<string, List<string>>(GetEditingCharacter()!.ModOptionSettings![mod.Directory]);
            }
            else if (currentCollectionId != Guid.Empty)
            {
                // Get current options from Penumbra
                var (success, _, _, options) = plugin.PenumbraIntegration.GetCurrentModSettings(currentCollectionId, mod.Directory, mod.Name);
                if (success && options.Any())
                {
                    currentModOptions = options;
                }
                else
                {
                    // No current settings, so fall back to per-group defaults
                    currentModOptions = new Dictionary<string, List<string>>();
                    foreach (var (groupName, optionNames) in availableModOptions)
                    {
                        if (optionNames.Any())
                        {
                            var groupType = optionGroupTypes?.ContainsKey(groupName) == true ? optionGroupTypes[groupName] : 0;
                            var isMultiSelect = groupType == 1 || groupType == 2;

                            // Multi-select starts empty, single-select takes the first option
                            if (isMultiSelect)
                                currentModOptions[groupName] = new List<string>();
                            else
                                currentModOptions[groupName] = new List<string> { optionNames.First() };
                        }
                    }
                }
            }
            else
            {
                currentModOptions = new Dictionary<string, List<string>>();
                foreach (var (groupName, optionNames) in availableModOptions)
                {
                    if (optionNames.Any())
                    {
                        var groupType = optionGroupTypes?.ContainsKey(groupName) == true ? optionGroupTypes[groupName] : 0;
                        var isMultiSelect = groupType == 1 || groupType == 2;

                        // Multi-select starts empty, single-select takes the first option
                        if (isMultiSelect)
                            currentModOptions[groupName] = new List<string>();
                        else
                            currentModOptions[groupName] = new List<string> { optionNames.First() };
                    }
                }
            }

            shouldOpenOptionsPopup = true;
        }

        /// <summary>
        /// Draw the mod options configuration popup
        /// </summary>
        // Boutique mod-option group header: small gold-deep accent bar on the
        // left + tracked-caps Oswald label. Used for combo / radio / checkbox
        // group headers inside the options popup so they read consistently.
        private void DrawModOptionGroupHeader(string groupName)
        {
            var dl = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            float fontSize = 0f;
            float trackPx = 0f;
            float labelW = 0f;
            string label = groupName.ToUpperInvariant();
            using (Boutique.Kicker11?.Push())
            {
                fontSize = ImGui.GetFontSize();
                trackPx = Boutique.Track28(fontSize);
                labelW = Boutique.MeasureTrackedText(label, trackPx);
            }
            float h = MathF.Max(fontSize, 16f);
            // Gold-deep accent bar on the left
            dl.AddRectFilled(
                new Vector2(pos.X, pos.Y + 2f),
                new Vector2(pos.X + 2f, pos.Y + h - 2f),
                Boutique.U32(Boutique.GoldDeep));
            using (Boutique.Kicker11?.Push())
            {
                Boutique.DrawTrackedText(dl,
                    new Vector2(pos.X + 10f, pos.Y),
                    label, Boutique.U32(Boutique.Text), trackPx);
            }
            ImGui.Dummy(new Vector2(0, h + 4f));
        }

        private void DrawModOptionsPopup()
        {
            if (optionsEditingMod == null)
                return;
            if (availableModOptions == null)
                return;
            if (currentModOptions == null)
                return;

            // If optionGroupTypes is null, we need to reload it
            if (optionGroupTypes == null)
            {
                optionGroupTypes = new Dictionary<string, int>();
                var rawOptions = plugin.PenumbraIntegration.GetModOptionsRaw(optionsEditingMod.Directory, optionsEditingMod.Name);
                foreach (var (groupName, (optionNames, groupType)) in rawOptions)
                {
                    optionGroupTypes[groupName] = groupType;
                }
            }

            var popupId = $"ModOptions_{optionsEditingMod.Directory}";

            // Open popup if flag is set
            if (shouldOpenOptionsPopup)
            {
                ImGui.OpenPopup(popupId);
                shouldOpenOptionsPopup = false;
                isOptionsPopupOpen = true;
            }

            ImGui.SetNextWindowSize(new Vector2(560, 600), ImGuiCond.Always);
            // Boutique styling: dark velvet bg + gold border. NoScrollbar on
            // the popup itself so the OptionsArea child handles all scrolling
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.04f, 0.05f, 0.08f, 0.97f));
            ImGui.PushStyleColor(ImGuiCol.Border, Boutique.WithAlpha(Boutique.Gold, 0.55f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16f, 14f));

            const ImGuiWindowFlags popupFlags =
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
                ImGuiWindowFlags.NoResize;

            if (ImGui.BeginPopupModal(popupId, ref isOptionsPopupOpen, popupFlags))
            {
                Boutique.PushFormStyle();

                // ── Boutique title row ──
                // Layout: "CONFIGURE" kicker + diamond divider + mod name,
                // all on the same vertical baseline. Bottom gold-fade rule.
                float titleW = ImGui.GetContentRegionAvail().X;
                float titleRowH = 28f;
                {
                    var dl2 = ImGui.GetWindowDrawList();
                    var titlePos = ImGui.GetCursorScreenPos();
                    float titleMidY = titlePos.Y + titleRowH * 0.5f;

                    // Pre-measure kicker so we don't nest Push blocks.
                    float kickerTrackPx, kickerW;
                    using (Boutique.Kicker11?.Push())
                    {
                        kickerTrackPx = Boutique.Track32(ImGui.GetFontSize());
                        kickerW = Boutique.MeasureTrackedText("CONFIGURE", kickerTrackPx);
                    }

                    using (Boutique.Kicker11?.Push())
                    {
                        float fs = ImGui.GetFontSize();
                        Boutique.DrawTrackedText(dl2,
                            new Vector2(titlePos.X, titleMidY - fs * 0.5f),
                            "CONFIGURE", Boutique.U32(Boutique.GoldDeep), kickerTrackPx);
                    }

                    // Diamond divider
                    var diaC = new Vector2(titlePos.X + kickerW + 14f, titleMidY);
                    dl2.AddTriangleFilled(
                        diaC + new Vector2(0, -3f), diaC + new Vector2(3f, 0), diaC + new Vector2(0, 3f),
                        Boutique.U32(Boutique.GoldDeep));
                    dl2.AddTriangleFilled(
                        diaC + new Vector2(0, -3f), diaC + new Vector2(0, 3f), diaC + new Vector2(-3f, 0),
                        Boutique.U32(Boutique.GoldDeep));

                    // Mod name (Outfit Med 13)
                    using (Boutique.Body13?.Push())
                    {
                        float fs = ImGui.GetFontSize();
                        float nameX = titlePos.X + kickerW + 28f;
                        float nameY = titleMidY - fs * 0.5f;
                        // Truncate if it would exceed available width
                        float nameAvail = titlePos.X + titleW - nameX;
                        string display = Boutique.TruncateToWidth(optionsEditingMod.Name, nameAvail);
                        dl2.AddText(new Vector2(nameX, nameY),
                            Boutique.U32(Boutique.Text), display);
                    }

                    // Bottom hairline (gold-fade)
                    Boutique.DrawGoldFadeRule(dl2,
                        new Vector2(titlePos.X, titlePos.Y + titleRowH + 4f),
                        titleW, 1f);

                    ImGui.Dummy(new Vector2(titleW, titleRowH + 12f));
                }

                // ── Status caption ──
                var hasCustomOptions = false;
                {
                    string statusText;
                    Vector4 statusCol;
                    if (editingDesign != null)
                    {
                        hasCustomOptions = editingDesign.ModOptionSettings?.ContainsKey(optionsEditingMod.Directory) ?? false;
                        statusText = hasCustomOptions
                            ? "Custom options configured for this design"
                            : "Using current Penumbra settings";
                        statusCol = hasCustomOptions ? Boutique.CyanSoft : Boutique.NpAmber;
                    }
                    else
                    {
                        var editChar = GetEditingCharacter();
                        hasCustomOptions = editChar?.ModOptionSettings?.ContainsKey(optionsEditingMod.Directory) ?? false;
                        statusText = hasCustomOptions
                            ? "Custom options configured for this character"
                            : "Using current Penumbra settings";
                        statusCol = hasCustomOptions ? Boutique.CyanSoft : Boutique.NpAmber;
                    }
                    using (Boutique.Body13?.Push())
                    {
                        ImGui.TextColored(statusCol, statusText);
                    }
                    ImGui.Dummy(new Vector2(0, 4f));
                }

                // Scrollable options area. Reserve 56 px for the footer below.
                // Stronger frame border so combos/checkboxes are legible on
                // the dark popup bg; tighter padding so glyphs aren't bloated.
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5f, 2f));
                ImGui.PushStyleColor(ImGuiCol.Border, Boutique.WithAlpha(Boutique.Gold, 0.45f));
                if (ImGui.BeginChild("OptionsArea", new Vector2(0, -56f), false,
                    ImGuiWindowFlags.AlwaysUseWindowPadding))
                {
                    // Filter and organize options by type to match Penumbra's layout
                    var filteredOptions = availableModOptions
                        .Where(kvp => kvp.Value.Any() &&
                               kvp.Key != "Necessary Files" &&
                               kvp.Key != "Done!")
                        .ToList();

                    // Group by type for consistent layout
                    var comboGroups = new List<(string name, string[] options)>();
                    var radioGroups = new List<(string name, string[] options)>();
                    var checkboxGroups = new List<(string name, string[] options)>();

                    // Get fresh type information right when we need it
                    var rawOptionsForTypes = plugin.PenumbraIntegration.GetModOptionsRaw(optionsEditingMod.Directory, optionsEditingMod.Name);

                    foreach (var (groupName, optionNames) in filteredOptions)
                    {
                        // Look up the type from fresh data
                        var groupType = 0;
                        if (rawOptionsForTypes.ContainsKey(groupName))
                        {
                            groupType = rawOptionsForTypes[groupName].Item2;
                        }

                        var isMultiSelect = groupType == 1 || groupType == 2;

                        if (isMultiSelect)
                        {
                            checkboxGroups.Add((groupName, optionNames.ToArray()));
                        }
                        else if (optionNames.Count > 2)
                        {
                            comboGroups.Add((groupName, optionNames.ToArray()));
                        }
                        else
                        {
                            radioGroups.Add((groupName, optionNames.ToArray()));
                        }
                    }


                    // Draw dropdown combos first (single-choice, >2 options)
                    foreach (var (groupName, optionNames) in comboGroups)
                    {
                        var currentSelection = currentModOptions.ContainsKey(groupName) && currentModOptions[groupName].Any()
                            ? currentModOptions[groupName].First()
                            : optionNames.First();

                        var currentIndex = Array.IndexOf(optionNames, currentSelection);
                        if (currentIndex < 0) currentIndex = 0;

                        DrawModOptionGroupHeader(groupName);

                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 12f);
                        if (ImGui.Combo($"##{groupName}_combo", ref currentIndex, optionNames, optionNames.Length))
                        {
                            currentModOptions[groupName] = new List<string> { optionNames[currentIndex] };
                        }

                        ImGui.Dummy(new Vector2(0, 6f));
                    }

                    // Draw radio button groups second (single-choice, ≤2 options)
                    foreach (var (groupName, optionNames) in radioGroups)
                    {
                        DrawModOptionGroupHeader(groupName);

                        var currentSelection = currentModOptions.ContainsKey(groupName) && currentModOptions[groupName].Any()
                            ? currentModOptions[groupName].First()
                            : optionNames.First();

                        // Indent radios under the section header
                        ImGui.Indent(12f);
                        for (int i = 0; i < optionNames.Length; i++)
                        {
                            if (i > 0) ImGui.SameLine();
                            if (ImGui.RadioButton($"{optionNames[i]}##{groupName}", currentSelection == optionNames[i]))
                            {
                                currentModOptions[groupName] = new List<string> { optionNames[i] };
                            }
                        }
                        ImGui.Unindent(12f);

                        ImGui.Dummy(new Vector2(0, 6f));
                    }

                    // Draw checkbox groups last (multi-choice, Type 1/2)
                    foreach (var (groupName, optionNames) in checkboxGroups)
                    {
                        DrawModOptionGroupHeader(groupName);
                        ImGui.Indent(12f);

                        var currentSelections = currentModOptions.ContainsKey(groupName)
                            ? currentModOptions[groupName]
                            : new List<string>();

                        foreach (var optionName in optionNames)
                        {
                            var isSelected = currentSelections.Contains(optionName);
                            if (ImGui.Checkbox($"{optionName}##{groupName}", ref isSelected))
                            {
                                if (isSelected)
                                {
                                    if (!currentSelections.Contains(optionName))
                                        currentSelections.Add(optionName);
                                }
                                else
                                {
                                    currentSelections.Remove(optionName);
                                }
                                currentModOptions[groupName] = new List<string>(currentSelections);
                            }
                        }
                        ImGui.Unindent(12f);

                        ImGui.Dummy(new Vector2(0, 8f));
                    }
                }
                ImGui.EndChild();
                ImGui.PopStyleColor();
                ImGui.PopStyleVar(2);

                ImGui.Separator();
                ImGui.Dummy(new Vector2(0, 6f));

                // Boutique footer: chamfered cancel + (optional) chamfered
                // clear + gold-pill save. Layout walks RIGHT-TO-LEFT from the
                // row's right edge, so cancel sits flush next to save when
                // the clear button isn't shown, no phantom slot reserved.
                {
                    var fdl = ImGui.GetWindowDrawList();
                    float btnH = 30f;
                    float saveW = 88f;
                    float clearW = 80f;
                    float cancelW = 80f;
                    float gap = 6f;
                    var rowStart = ImGui.GetCursorScreenPos();
                    float rowMidY = rowStart.Y + btnH * 0.5f;
                    float rowRight = rowStart.X + ImGui.GetContentRegionAvail().X;

                    // SAVE on the far right
                    var saveMin = new Vector2(rowRight - saveW, rowMidY - btnH * 0.5f);
                    var saveMax = saveMin + new Vector2(saveW, btnH);

                    // CLEAR (optional) immediately to the left of SAVE
                    Vector2 clearMin = default, clearMax = default;
                    float leftOfSaveX = saveMin.X;
                    if (hasCustomOptions)
                    {
                        clearMin = new Vector2(saveMin.X - gap - clearW, rowMidY - btnH * 0.5f);
                        clearMax = clearMin + new Vector2(clearW, btnH);
                        leftOfSaveX = clearMin.X;
                    }

                    // CANCEL adjacent to whichever button is to its right
                    var cancelMin = new Vector2(leftOfSaveX - gap - cancelW, rowMidY - btnH * 0.5f);
                    var cancelMax = cancelMin + new Vector2(cancelW, btnH);

                    if (Boutique.DrawCancelBtn(fdl, cancelMin, cancelMax,
                            "CANCEL", 1.4f, 1f, "modopts_cancel"))
                    {
                        ImGui.CloseCurrentPopup();
                    }

                    if (hasCustomOptions)
                    {
                        if (Boutique.DrawCancelBtn(fdl, clearMin, clearMax,
                                "CLEAR", 1.4f, 1f, "modopts_clear"))
                        {
                            ClearModOptions();
                            ImGui.CloseCurrentPopup();
                        }
                    }

                    if (Boutique.DrawSavePill(fdl, saveMin, saveMax,
                            "SAVE", 1.6f, 1f, "modopts_save",
                            disabled: false, sheenProvider: _modMgrSheen))
                    {
                        SaveModOptions();
                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.Dummy(new Vector2(0, btnH + 4f));
                }

                Boutique.PopFormStyle();
                ImGui.EndPopup();
            }
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);

            // Only clean up when popup is actually closed
            if (!ImGui.IsPopupOpen(popupId) && !shouldOpenOptionsPopup)
            {
                // Popup was closed, clean up
                optionsEditingMod = null;
                availableModOptions = null;
                currentModOptions = null;
                optionGroupTypes = null;
                isOptionsPopupOpen = false;
            }
        }

        /// <summary>Save the current mod options to the design or character.</summary>
        private void SaveModOptions()
        {
            if (optionsEditingMod == null || currentModOptions == null)
                return;

            // Save to design if we have one, otherwise save to character
            if (editingDesign != null)
            {
                editingDesign.ModOptionSettings ??= new Dictionary<string, Dictionary<string, List<string>>>();
                editingDesign.ModOptionSettings[optionsEditingMod.Directory] = new Dictionary<string, List<string>>(currentModOptions);
            }
            else
            {
                var character = GetEditingCharacter();
                if (character != null)
                {
                    character.ModOptionSettings ??= new Dictionary<string, Dictionary<string, List<string>>>();
                    character.ModOptionSettings[optionsEditingMod.Directory] = new Dictionary<string, List<string>>(currentModOptions);
                    plugin.SaveConfiguration();
                }
            }

            // Apply the options immediately to Penumbra if we have a collection
            if (currentCollectionId != Guid.Empty)
            {
                _ = Task.Run(async () =>
                {
                    foreach (var (groupName, options) in currentModOptions)
                    {
                        plugin.PenumbraIntegration.TrySetModSettings(currentCollectionId, optionsEditingMod.Directory, optionsEditingMod.Name, groupName, options);
                        await Task.Delay(10);
                    }
                });
            }
        }

        /// <summary>Clear custom mod options (revert to Penumbra defaults).</summary>
        private void ClearModOptions()
        {
            if (optionsEditingMod == null)
                return;

            if (editingDesign != null)
            {
                editingDesign.ModOptionSettings?.Remove(optionsEditingMod.Directory);
            }
            else
            {
                var character = GetEditingCharacter();
                if (character != null)
                {
                    character.ModOptionSettings?.Remove(optionsEditingMod.Directory);
                    plugin.SaveConfiguration();
                }
            }
        }

        /// <summary>
        /// Cached check for whether a mod has configurable options (performance optimization)
        /// </summary>
        private bool ModHasOptionsCache(string modDirectory, string modName)
        {
            var key = $"{modDirectory}|{modName}";

            if (modOptionsCache.ContainsKey(key))
                return modOptionsCache[key];

            // Check if this mod actually has options by trying to get them
            // Add small delay to prevent overwhelming Penumbra with rapid queries
            try
            {
                var options = plugin.PenumbraIntegration?.GetModOptions(modDirectory, modName) ?? new Dictionary<string, List<string>>();
                var hasOptions = options.Any();

                // Fallback: check for multiple group JSON files if Penumbra API didn't find options
                if (!hasOptions)
                {
                    var penumbraModPath = plugin.PenumbraIntegration?.GetModDirectory();
                    if (!string.IsNullOrEmpty(penumbraModPath))
                    {
                        var fullModPath = Path.Combine(penumbraModPath, modDirectory);
                        if (Directory.Exists(fullModPath))
                        {
                            var groupFiles = Directory.GetFiles(fullModPath, "group_*.json");
                            hasOptions = groupFiles.Length > 1; // Has multiple group files = has options
                        }
                    }
                }

                modOptionsCache[key] = hasOptions;

                // Small delay to space out Penumbra API calls
                Thread.Sleep(1);

                return hasOptions;
            }
            catch (Exception ex)
            {
                // Error checking options for mod (log removed to prevent spam)
                modOptionsCache[key] = false;
                return false;
            }
        }

        // Static cache for mod type determination to avoid creating windows
        private static SecretModeModWindow? _staticInstance = null;

        /// <summary>Determines a mod's type via path analysis. Shared with non-UI callers so categorisation matches what the UI shows.</summary>
        public static ModType DetermineModType(string modDir, string modName, Plugin plugin)
        {
            // Create a cached instance to avoid expensive window creation on every call
            if (_staticInstance == null)
            {
                _staticInstance = new SecretModeModWindow(plugin);
            }
            return _staticInstance.DetermineModTypeFromPaths(modDir, modName, null);
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        /// <summary>
        /// Gets the gear and hair mods that are currently affecting the character (same as what's shown in Currently Affecting You tab)
        /// </summary>
        public HashSet<string> GetCurrentlyAffectingGearAndHairMods()
        {
            if (availableMods == null) return new HashSet<string>();

            // Use the same filtering logic as the "Currently Affecting You" tab
            var gearAndHairMods = availableMods.Where(m => m.IsCurrentlyAffecting &&
                (m.ModType == ModType.Gear || m.ModType == ModType.Hair))
                .Select(m => m.Directory)
                .ToHashSet();

            return gearAndHairMods;
        }
    }
}

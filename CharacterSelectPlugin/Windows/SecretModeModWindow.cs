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
        
        // Dependency and conflict fields from analysis
        public bool HasDependency { get; set; } = false;
        public string DependencyType { get; set; } = "";
        public bool HasConflicts { get; set; } = false;
        public List<string> ConflictingMods { get; set; } = new();
    }

    public class SecretModeModWindow : Window, IDisposable
    {
        private Plugin plugin;
        private UIStyles uiStyles;
        private List<ModEntry> availableMods = new();
        private Dictionary<string, bool> selectedMods = new();
        private string searchFilter = "";
        private bool isLoading = true;
        private int? editingCharacterIndex = null;
        private CharacterDesign? editingDesign = null;
        private string? editingCharacterName = null;
        private Action<Dictionary<string, bool>>? onSave = null;
        private Action<HashSet<string>>? onSavePins = null;
        
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
        
        // Old UI fields still used by loading logic
        private int displayMode = 0;
        private bool showGear = true;
        private bool showHair = true;
        private bool showOther = true;
        
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
            ImGuiWindowFlags.None)
        {
            this.plugin = plugin;
            this.uiStyles = new UIStyles(plugin);
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(900, 600),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };
        }

        public void Open(int? characterIndex = null, Dictionary<string, bool>? existingSelection = null, HashSet<string>? existingPins = null, Action<Dictionary<string, bool>>? saveCallback = null, Action<HashSet<string>>? savePinsCallback = null, CharacterDesign? design = null, string? characterName = null)
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
            userHasSelectedCollection = false; // Reset on each open to allow fresh auto-detection
            
            // Initialize with existing selection if provided
            selectedMods.Clear();
            if (existingSelection != null)
            {
                foreach (var kvp in existingSelection)
                    selectedMods[kvp.Key] = kvp.Value;
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
                
                // Pre-select nothing since we don't know what's enabled
                selectedMods[modDir] = false;
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
                        HasDependency = conflictAnalysis?.HasDependency ?? false,
                        DependencyType = conflictAnalysis?.DependencyType ?? "",
                        HasConflicts = conflictAnalysis?.HasConflicts ?? false,
                        ConflictingMods = conflictAnalysis?.ConflictingMods ?? new List<string>()
                    };
                    
                    availableMods.Add(entry);
                    
                    // No pre-selection - all mods start unchecked unless already configured
                    if (!selectedMods.ContainsKey(modDir))
                    {
                        selectedMods[modDir] = false;
                    }
                    
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
                        HasDependency = conflictAnalysis?.HasDependency ?? false,
                        DependencyType = conflictAnalysis?.DependencyType ?? "",
                        HasConflicts = conflictAnalysis?.HasConflicts ?? false,
                        ConflictingMods = conflictAnalysis?.ConflictingMods ?? new List<string>()
                    };
                    
                    availableMods.Add(entry);
                    
                    // No pre-selection - all mods start unchecked unless already configured
                    if (!selectedMods.ContainsKey(modDir))
                    {
                        selectedMods[modDir] = false;
                    }
                    
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
            if (categoryIndex == 0) return availableMods.Count(m => m.IsCurrentlyAffecting);
            
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
            // Update window title based on current context
            var contextTitle = GetContextualWindowTitle();
            if (WindowName != contextTitle)
            {
                WindowName = contextTitle;
            }
            
            uiStyles.PushMainWindowStyle();
            
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
                
                // Draw mod options popup if open
                DrawModOptionsPopup();
            }
            finally
            {
                uiStyles.PopMainWindowStyle();
            }
        }
        
        private string GetContextualWindowTitle()
        {
            return "Mod Manager";
        }
        
        private void DrawLoadingState()
        {
            // Update loading animations and messages
            UpdateLoadingAnimations();
            
            var contentSize = ImGui.GetContentRegionAvail();
            var centerX = contentSize.X / 2;
            var centerY = contentSize.Y / 2;
            
            // Center the loading display
            var loadingWidth = 450f;
            var loadingHeight = 180f;
            ImGui.SetCursorPos(new Vector2(centerX - loadingWidth / 2, centerY - loadingHeight / 2));
            
            // Enhanced panel styling
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(25, 20));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.1f, 0.1f, 0.15f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.3f, 0.5f, 0.8f, 0.6f));
            
            ImGui.BeginChild("LoadingPanel", new Vector2(loadingWidth, loadingHeight), true, ImGuiWindowFlags.NoScrollbar);
            
            // Title
            var title = "Loading Mod Information";
            var titleSize = ImGui.CalcTextSize(title);
            ImGui.SetCursorPosX((loadingWidth - titleSize.X) / 2 - 25);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 1.0f, 1.0f));
            ImGui.Text(title);
            ImGui.PopStyleColor();
            
            ImGui.Spacing();
            
            // Standard progress bar with enhanced styling
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.2f, 0.6f, 1.0f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
            ImGui.ProgressBar(loadingProgress, new Vector2(loadingWidth - 50, 20), $"{modsLoaded}/{totalModsToLoad}");
            ImGui.PopStyleColor(2);
            
            ImGui.Spacing();
            
            // Witty loading message
            if (!string.IsNullOrEmpty(currentLoadingMessage))
            {
                var messageSize = ImGui.CalcTextSize(currentLoadingMessage);
                ImGui.SetCursorPosX((loadingWidth - messageSize.X) / 2 - 25);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
                ImGui.Text(currentLoadingMessage);
                ImGui.PopStyleColor();
            }
            
            ImGui.Spacing();
            ImGui.Spacing();
            
            // Cancel button
            var cancelText = "Cancel";
            var cancelButtonSize = new Vector2(80, 28);
            ImGui.SetCursorPosX((loadingWidth - cancelButtonSize.X) / 2 - 25);
            
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.3f, 0.3f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.8f, 0.4f, 0.4f, 1.0f));
            
            if (ImGui.Button(cancelText, cancelButtonSize))
            {
                loadingCancellation?.Cancel();
                IsOpen = false;
            }
            
            ImGui.PopStyleColor(3);
            
            ImGui.EndChild();
            
            // Pop all styles
            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar(2);
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
            // Context header showing which character/design is being edited
            if (!string.IsNullOrEmpty(editingCharacterName) || editingDesign != null)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ColorSchemes.Dark.AccentBlue);
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 10));
                
                var contextText = "";
                if (!string.IsNullOrEmpty(editingCharacterName) && editingDesign != null)
                {
                    var designName = string.IsNullOrEmpty(editingDesign.Name) ? "New Design" : editingDesign.Name;
                    contextText = $"Configuring mods for: {editingCharacterName} - {designName}";
                }
                else if (!string.IsNullOrEmpty(editingCharacterName))
                {
                    contextText = $"Configuring mods for: {editingCharacterName}";
                }
                else if (editingDesign != null)
                {
                    var designName = string.IsNullOrEmpty(editingDesign.Name) ? "New Design" : editingDesign.Name;
                    contextText = $"Configuring mods for design: {designName}";
                }
                
                // Center the context text
                var textSize = ImGui.CalcTextSize(contextText);
                var windowWidth = ImGui.GetWindowContentRegionMax().X;
                ImGui.SetCursorPosX((windowWidth - textSize.X) / 2);
                
                ImGui.Text(contextText);
                ImGui.PopStyleVar();
                ImGui.PopStyleColor();
                ImGui.Separator();
                ImGui.Spacing();
            }
            
            // Collection selector
            if (availableCollections.Any())
            {
                ImGui.Text("Penumbra Collection:");
                ImGui.SameLine();
                
                var collectionsList = availableCollections.ToList();
                var collectionNames = collectionsList.Select(kvp => kvp.Value).ToArray();
                
                ImGui.SetNextItemWidth(300);
                if (ImGui.Combo("##CollectionSelect", ref selectedCollectionIndex, collectionNames, collectionNames.Length))
                {
                    var selectedKvp = collectionsList[selectedCollectionIndex];
                    currentCollectionId = selectedKvp.Key;
                    currentCollectionName = selectedKvp.Value;
                    userHasSelectedCollection = true;
                    _ = LoadCurrentMods();
                }
                
                ImGui.SameLine();
                if (uiStyles.IconButton(FontAwesomeIcon.Sync.ToIconString(), "Refresh mods"))
                {
                    _ = LoadCurrentMods();
                }
            }
            else
            {
                ImGui.TextColored(ColorSchemes.Dark.AccentRed, "Warning: No Penumbra collections found");
            }
            
            ImGui.Separator();
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
            
            // Sidebar for categories
            ImGui.BeginChild("CategorySidebar", new Vector2(200, -40), true);
            
            ImGui.Text("Categories");
            ImGui.Separator();
            
            for (int i = 0; i < categoryNames.Length; i++)
            {
                var modCount = GetModCountForCategory(i);
                var categoryText = $"{categoryNames[i]} ({modCount})";
                
                // Highlight selected category
                bool isSelected = selectedCategory == i;
                if (isSelected)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ColorSchemes.Dark.AccentBlue);
                }
                
                if (ImGui.Selectable(categoryText, isSelected))
                {
                    selectedCategory = i;
                    // Reset to first page when switching categories
                    categoryPageNumbers[i] = 0;
                    currentPage = 0;
                    // Clear search when switching categories
                    searchFilter = "";
                }
                
                if (isSelected)
                {
                    ImGui.PopStyleColor();
                }
            }
            
            ImGui.EndChild();
            
            // Main mod list area
            ImGui.SameLine();
            ImGui.BeginChild("ModListArea", new Vector2(-1, -40), true);
            
            // Sticky search bar in header
            var searchBarHeight = ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y * 2;
            ImGui.BeginChild("SearchHeader", new Vector2(-1, searchBarHeight), true, ImGuiWindowFlags.NoScrollbar);
            
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##Search", "Search mods...", ref searchFilter, 100);
            
            ImGui.EndChild();
            
            ImGui.Separator();
            
            // Scrollable mod list with pagination
            ImGui.BeginChild("ModList", new Vector2(-1, -30), true);
            
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
            
            // Show search result count if searching
            if (!string.IsNullOrWhiteSpace(searchFilter))
            {
                ImGui.TextColored(ColorSchemes.Dark.AccentGreen, $"Found {totalMods} matches");
                ImGui.Separator();
            }
            
            foreach (var mod in pagedMods)
            {
                DrawModEntry(mod);
            }
            
            ImGui.EndChild();
            
            // Pagination controls
            DrawPaginationControls(totalPages, totalMods);
            
            ImGui.EndChild();
        }
        
        private void DrawBottomButtons()
        {
            ImGui.Separator();
            
            var selectedCount = selectedMods.Count(kvp => kvp.Value);
            ImGui.Text($"Selected: {selectedCount} mods");
            
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - 185);
            
            uiStyles.PushDarkButtonStyle();
            if (ImGui.Button("Apply", new Vector2(100, 0)))
            {
                SaveSelection();
                IsOpen = false;
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(75, 0)))
            {
                IsOpen = false;
            }
            uiStyles.PopDarkButtonStyle();
        }
        
        // Path-based mod type analysis implemented below
        
        private void SaveSelection()
        {
            // Filter to only selected mods
            var selection = selectedMods
                .Where(kvp => kvp.Value)
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
        }
        
        // Helper methods for new UI
        private List<ModEntry> GetFilteredModsForSelectedCategory()
        {
            List<ModEntry> categoryMods;
            
            if (selectedCategory == 0) // Currently Affecting You
            {
                // Show only Gear and Hair mods that are currently affecting
                categoryMods = availableMods.Where(m => m.IsCurrentlyAffecting && 
                    (m.ModType == ModType.Gear || m.ModType == ModType.Hair)).ToList();
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
            
            if (!string.IsNullOrEmpty(searchFilter))
            {
                categoryMods = categoryMods.Where(m => 
                    m.Name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase) ||
                    m.Directory.Contains(searchFilter, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }
            
            // Sort with currently affecting mods at the top
            return categoryMods
                .OrderByDescending(m => m.IsCurrentlyAffecting)
                .ThenBy(m => m.Name)
                .ToList();
        }
        
        private void DrawModEntry(ModEntry mod)
        {
            // Store the cursor position at the start of the row for context menu
            var rowStartPos = ImGui.GetCursorScreenPos();
            
            // Get selection state from selectedMods dictionary
            var isSelected = selectedMods.ContainsKey(mod.Directory) ? selectedMods[mod.Directory] : false;
                             
            var isPinned = pinnedMods.Contains(mod.Directory);
            
            // Checkbox for selection
            if (ImGui.Checkbox($"##sel{mod.Directory}", ref isSelected))
            {
                selectedMods[mod.Directory] = isSelected;
                
                // If mod is being selected, analyze for dependencies and conflicts
                if (isSelected)
                {
                    // Run contextual analysis
                    mod.Analysis = plugin.PenumbraIntegration?.AnalyzeModForDependenciesAndConflicts(
                        mod.Directory, mod.Name, mod.ModType, selectedMods);
                    
                    // Update mod entry with analysis results
                    if (mod.Analysis != null)
                    {
                        mod.HasDependency = mod.Analysis.HasDependency;
                        mod.DependencyType = mod.Analysis.DependencyType;
                        mod.HasConflicts = mod.Analysis.HasConflicts;
                        mod.ConflictingMods = mod.Analysis.ConflictingMods;
                    }
                    
                    // Clear dismissed warning for this mod when newly selected
                    dismissedWarnings.Remove(mod.Directory);
                    
                    // If enabling a mod with old-style dependencies, prompt to enable them too
                    if (mod.Dependencies.Any())
                    {
                        HandleDependencySelection(mod);
                    }
                }
                else
                {
                    // Clear analysis when deselected
                    mod.Analysis = null;
                    mod.HasDependency = false;
                    mod.DependencyType = "";
                    mod.HasConflicts = false;
                    mod.ConflictingMods = new List<string>();
                }
            }
            
            ImGui.SameLine();
            
            // Pin button
            ImGui.PushFont(UiBuilder.IconFont);
            var pinIcon = isPinned ? FontAwesomeIcon.Thumbtack.ToIconString() : FontAwesomeIcon.MapPin.ToIconString();
            var pinColor = isPinned ? ColorSchemes.Dark.AccentYellow : ColorSchemes.Dark.TextMuted;
            
            ImGui.PushStyleColor(ImGuiCol.Text, pinColor);
            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 2)); // Reduce padding to help centering
            if (ImGui.Button($"{pinIcon}##pin{mod.Directory}", new Vector2(20, 20)))
            {
                if (isPinned)
                {
                    Plugin.Log.Information($"[PIN DEBUG] Unpinning mod: {mod.Directory}");
                    pinnedMods.Remove(mod.Directory);
                }
                else
                {
                    Plugin.Log.Information($"[PIN DEBUG] Pinning mod: {mod.Directory}");
                    pinnedMods.Add(mod.Directory);
                    // Automatically check the mod when pinning it
                    selectedMods[mod.Directory] = true;
                }
                Plugin.Log.Information($"[PIN DEBUG] Current pinned mods: {string.Join(", ", pinnedMods)}");
            }
            ImGui.PopStyleVar(2); // Pop both FramePadding and ButtonTextAlign
            ImGui.PopStyleColor();
            ImGui.PopFont();
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(isPinned ? "Unpin mod (will be disabled when switching)" : "Pin mod (never gets disabled)");
            }
            
            ImGui.SameLine();
            
            // Edit icon for configurable mods only
            var hasOptions = ModHasOptionsCache(mod.Directory, mod.Name);
            var hasCustomOptions = editingDesign?.ModOptionSettings?.ContainsKey(mod.Directory) ?? false;
            
            if (hasOptions)
            {
                ImGui.PushFont(UiBuilder.IconFont);
                var iconColor = hasCustomOptions ? ColorSchemes.Dark.AccentBlue : ColorSchemes.Dark.AccentYellow;
                ImGui.PushStyleColor(ImGuiCol.Text, iconColor);
                ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 2));
                
                if (ImGui.Button($"{FontAwesomeIcon.Edit.ToIconString()}##edit{mod.Directory}", new Vector2(20, 20)))
                {
                    OpenModOptionsPanel(mod);
                }
                
                ImGui.PopStyleVar(2);
                ImGui.PopStyleColor();
                ImGui.PopFont();
                
                if (ImGui.IsItemHovered())
                {
                    var tooltip = hasCustomOptions 
                        ? "Edit mod configuration options" 
                        : "Configure mod options";
                    ImGui.SetTooltip(tooltip);
                }
            }
            else
            {
                // Empty space to maintain alignment
                ImGui.Dummy(new Vector2(20, 20));
            }
            
            ImGui.SameLine();
            
            // Mod name and status
            ImGui.Text(mod.Name);
            
            if (mod.IsCurrentlyAffecting)
            {
                ImGui.SameLine();
                ImGui.TextColored(ColorSchemes.Dark.AccentGreen, $"★ Priority {mod.Priority}");
            }
            else if (mod.IsEnabled)
            {
                ImGui.SameLine();
                ImGui.TextColored(ColorSchemes.Dark.AccentYellow, $"Enabled");
            }
            
            // Show dependency indicators
            if (mod.Dependencies.Any())
            {
                ImGui.SameLine();
                
                // Check if all dependencies are met
                var unmetDependencies = mod.Dependencies.Where(d => !d.IsFound || 
                    !selectedMods.ContainsKey(d.RequiredModPath) || 
                    !selectedMods[d.RequiredModPath]).ToList();
                
                if (unmetDependencies.Any())
                {
                    ImGui.TextColored(ColorSchemes.Dark.AccentRed, $"⚠ Missing {unmetDependencies.Count} dependencies");
                }
                else
                {
                    ImGui.TextColored(ColorSchemes.Dark.AccentGreen, "✓ Dependencies met");
                }
            }
            
            // Show dependency warnings for incomplete gear mods only
            if (mod.ModType == ModType.Gear)
            {
                if (mod.HasOnlyModels)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(ColorSchemes.Dark.AccentYellow, "⚠ Needs texture mod");
                }
                else if (mod.HasOnlyTextures)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(ColorSchemes.Dark.AccentYellow, "⚠ Needs model mod");
                }
            }
            
            // Tooltip with categories and dependencies
            if (ImGui.IsItemHovered())
            {
                var tooltipLines = new List<string>();
                
                if (mod.Categories.Any())
                {
                    tooltipLines.Add($"Categories: {string.Join(", ", mod.Categories)}");
                }
                
                if (mod.Dependencies.Any())
                {
                    tooltipLines.Add("");
                    tooltipLines.Add("Dependencies:");
                    foreach (var dep in mod.Dependencies)
                    {
                        var status = dep.IsFound ? 
                            (selectedMods.ContainsKey(dep.RequiredModPath) && selectedMods[dep.RequiredModPath] ? "✓" : "✗") : 
                            "⚠ Not found";
                        tooltipLines.Add($"  {status} {dep.RequiredModName}");
                    }
                }
                
                if (mod.HasOnlyModels)
                {
                    tooltipLines.Add("");
                    tooltipLines.Add("This mod contains only models and requires texture dependencies.");
                }
                else if (mod.HasOnlyTextures)
                {
                    tooltipLines.Add("");
                    tooltipLines.Add("This mod contains only textures/materials and requires model dependencies.");
                }
                
                if (tooltipLines.Any())
                {
                    ImGui.SetTooltip(string.Join("\n", tooltipLines));
                }
            }
            
            // Show contextual warnings for selected mods
            if (isSelected && mod.Analysis != null && !dismissedWarnings.Contains(mod.Directory))
            {
                DrawContextualWarning(mod);
            }
            
            // Right-click context menu for manual categorization - draw invisible button over entire row
            var rowEndPos = ImGui.GetCursorScreenPos();
            var rowSize = new Vector2(ImGui.GetContentRegionAvail().X, rowEndPos.Y - rowStartPos.Y);
            
            ImGui.SetCursorScreenPos(rowStartPos);
            ImGui.InvisibleButton($"##ModRow_{mod.Directory}", rowSize);
            
            DrawModCategoryContextMenu(mod);
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
                
                // Extracted file paths from mod JSON files (log removed to prevent spam)
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[SecretMode] Error reading mod JSON files for '{modName}': {ex}");
            }
            
            return filePaths;
        }

        /// <summary>
        /// Fallback method to analyze mods based on item names when file paths aren't available
        /// </summary>
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
                        // Ignore parse errors for this check
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
        /// Draw pagination controls at the bottom of the mod list
        /// </summary>
        private void DrawPaginationControls(int totalPages, int totalMods)
        {
            if (totalPages <= 1) return;
            
            ImGui.Separator();
            
            var buttonWidth = 30f;
            var pageText = $"Page {currentPage + 1} of {totalPages} ({totalMods} mods)";
            var textSize = ImGui.CalcTextSize(pageText);
            
            // Center the pagination controls
            var totalWidth = buttonWidth * 4 + textSize.X + ImGui.GetStyle().ItemSpacing.X * 4;
            var startX = (ImGui.GetContentRegionAvail().X - totalWidth) / 2;
            
            ImGui.SetCursorPosX(startX);
            
            // First page button
            ImGui.BeginDisabled(currentPage == 0);
            if (ImGui.Button("<<", new Vector2(buttonWidth, 0)))
            {
                currentPage = 0;
                categoryPageNumbers[selectedCategory] = currentPage;
            }
            ImGui.EndDisabled();
            
            ImGui.SameLine();
            
            // Previous page button
            ImGui.BeginDisabled(currentPage == 0);
            if (ImGui.Button("<", new Vector2(buttonWidth, 0)))
            {
                currentPage--;
                categoryPageNumbers[selectedCategory] = currentPage;
            }
            ImGui.EndDisabled();
            
            ImGui.SameLine();
            ImGui.Text(pageText);
            ImGui.SameLine();
            
            // Next page button
            ImGui.BeginDisabled(currentPage >= totalPages - 1);
            if (ImGui.Button(">", new Vector2(buttonWidth, 0)))
            {
                currentPage++;
                categoryPageNumbers[selectedCategory] = currentPage;
            }
            ImGui.EndDisabled();
            
            ImGui.SameLine();
            
            // Last page button
            ImGui.BeginDisabled(currentPage >= totalPages - 1);
            if (ImGui.Button(">>", new Vector2(buttonWidth, 0)))
            {
                currentPage = totalPages - 1;
                categoryPageNumbers[selectedCategory] = currentPage;
            }
            ImGui.EndDisabled();
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
        private void OpenModOptionsPanel(ModEntry mod)
        {
            // Allow opening options panel even without a design being edited
            // In this case, we'll just show current settings without saving to a design
            
            optionsEditingMod = mod;
            
            // Get available options from Penumbra
            availableModOptions = plugin.PenumbraIntegration.GetModOptions(mod.Directory, mod.Name);
            optionGroupTypes = new Dictionary<string, int>();
            
            // Parse group types from Penumbra API - the int in the tuple is the group type
            // 0 = Single-select, 1 = Multi-select
            var rawOptions = plugin.PenumbraIntegration.GetModOptionsRaw(mod.Directory, mod.Name);
            foreach (var (groupName, (optionNames, groupType)) in rawOptions)
            {
                optionGroupTypes[groupName] = groupType;
            }
            
            // Load current settings for this mod
            if (editingDesign?.ModOptionSettings?.ContainsKey(mod.Directory) ?? false)
            {
                // Use design's saved options
                currentModOptions = new Dictionary<string, List<string>>(editingDesign.ModOptionSettings[mod.Directory]);
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
                    // No current settings - use defaults (handle multi-select vs single-select)
                    currentModOptions = new Dictionary<string, List<string>>();
                    foreach (var (groupName, optionNames) in availableModOptions)
                    {
                        if (optionNames.Any())
                        {
                            var groupType = optionGroupTypes?.ContainsKey(groupName) == true ? optionGroupTypes[groupName] : 0;
                            var isMultiSelect = groupType == 1 || groupType == 2;
                            
                            if (isMultiSelect)
                            {
                                // Multi-select: start with empty selection
                                currentModOptions[groupName] = new List<string>();
                            }
                            else
                            {
                                // Single-select: use first option as default
                                currentModOptions[groupName] = new List<string> { optionNames.First() };
                            }
                        }
                    }
                }
            }
            else
            {
                // Default to appropriate selection based on group type
                currentModOptions = new Dictionary<string, List<string>>();
                foreach (var (groupName, optionNames) in availableModOptions)
                {
                    if (optionNames.Any())
                    {
                        var groupType = optionGroupTypes?.ContainsKey(groupName) == true ? optionGroupTypes[groupName] : 0;
                        var isMultiSelect = groupType == 1 || groupType == 2;
                        
                        if (isMultiSelect)
                        {
                            // Multi-select: start with empty selection
                            currentModOptions[groupName] = new List<string>();
                        }
                        else
                        {
                            // Single-select: use first option as default
                            currentModOptions[groupName] = new List<string> { optionNames.First() };
                        }
                    }
                }
            }
            
            shouldOpenOptionsPopup = true;
        }
        
        /// <summary>
        /// Draw the mod options configuration popup
        /// </summary>
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
            
            ImGui.SetNextWindowSize(new Vector2(500, 600), ImGuiCond.FirstUseEver);
            
            if (ImGui.BeginPopupModal(popupId, ref isOptionsPopupOpen))
            {
                // Title
                ImGui.Text($"Configure: {optionsEditingMod.Name}");
                ImGui.Separator();
                
                // Show status based on whether we're editing a design
                var hasCustomOptions = false;
                if (editingDesign != null)
                {
                    hasCustomOptions = editingDesign.ModOptionSettings?.ContainsKey(optionsEditingMod.Directory) ?? false;
                    if (hasCustomOptions)
                    {
                        ImGui.TextColored(ColorSchemes.Dark.AccentBlue, "✓ Custom options configured for this design");
                    }
                    else
                    {
                        ImGui.TextColored(ColorSchemes.Dark.AccentYellow, "⚠ Using current Penumbra settings");
                    }
                }
                else
                {
                    ImGui.TextColored(ColorSchemes.Dark.AccentGreen, "Editing current Penumbra settings");
                }
                ImGui.Separator();
                
                // Scrollable area for options
                if (ImGui.BeginChild("OptionsArea", new Vector2(0, 450)))
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
                        
                        ImGui.PushStyleColor(ImGuiCol.Text, ColorSchemes.Dark.AccentBlue);
                        ImGui.Text(groupName);
                        ImGui.PopStyleColor();
                        
                        ImGui.SetNextItemWidth(400);
                        if (ImGui.Combo($"##{groupName}_combo", ref currentIndex, optionNames, optionNames.Length))
                        {
                            currentModOptions[groupName] = new List<string> { optionNames[currentIndex] };
                        }
                        
                        ImGui.Spacing();
                    }
                    
                    // Draw radio button groups second (single-choice, ≤2 options)
                    foreach (var (groupName, optionNames) in radioGroups)
                    {
                        // Simple section header with no child window - same style as checkboxes
                        ImGui.PushStyleColor(ImGuiCol.Text, ColorSchemes.Dark.AccentBlue);
                        ImGui.Text(groupName);
                        ImGui.PopStyleColor();
                        
                        var currentSelection = currentModOptions.ContainsKey(groupName) && currentModOptions[groupName].Any()
                            ? currentModOptions[groupName].First()
                            : optionNames.First();
                        
                        // Draw radio buttons inline
                        ImGui.SameLine();
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 20); // Small indent
                        
                        for (int i = 0; i < optionNames.Length; i++)
                        {
                            if (i > 0) ImGui.SameLine();
                            
                            if (ImGui.RadioButton($"{optionNames[i]}##{groupName}", currentSelection == optionNames[i]))
                            {
                                currentModOptions[groupName] = new List<string> { optionNames[i] };
                            }
                        }
                        
                        ImGui.Spacing();
                    }
                    
                    // Draw checkbox groups last (multi-choice, Type 1/2)
                    foreach (var (groupName, optionNames) in checkboxGroups)
                    {
                        // Simple section header with no child window
                        ImGui.PushStyleColor(ImGuiCol.Text, ColorSchemes.Dark.AccentBlue);
                        ImGui.Text(groupName);
                        ImGui.PopStyleColor();
                        
                        ImGui.Spacing();
                        
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
                        
                        ImGui.Spacing();
                        ImGui.Separator();
                        ImGui.Spacing();
                    }
                }
                ImGui.EndChild();
                
                ImGui.Separator();
                
                // Buttons
                if (ImGui.Button("Save to Design", new Vector2(120, 0)))
                {
                    SaveModOptionsToDesign();
                    ImGui.CloseCurrentPopup();
                }
                
                ImGui.SameLine();
                
                if (hasCustomOptions && ImGui.Button("Clear Design Options", new Vector2(150, 0)))
                {
                    ClearModOptionsFromDesign();
                    ImGui.CloseCurrentPopup();
                }
                
                ImGui.SameLine();
                
                if (ImGui.Button("Cancel", new Vector2(80, 0)))
                {
                    ImGui.CloseCurrentPopup();
                }
                
                ImGui.EndPopup();
            }
            
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
        
        /// <summary>
        /// Save the current mod options to the design
        /// </summary>
        private void SaveModOptionsToDesign()
        {
            if (optionsEditingMod == null || currentModOptions == null)
                return;
            
            // If editing a design, save to design
            if (editingDesign != null)
            {
                // Initialize the design's mod options if needed
                editingDesign.ModOptionSettings ??= new Dictionary<string, Dictionary<string, List<string>>>();
                
                // Save the current options
                editingDesign.ModOptionSettings[optionsEditingMod.Directory] = new Dictionary<string, List<string>>(currentModOptions);
            }
            
            // Apply the options immediately to Penumbra if we have a collection
            if (currentCollectionId != Guid.Empty)
            {
                _ = Task.Run(async () =>
                {
                    foreach (var (groupName, options) in currentModOptions)
                    {
                        plugin.PenumbraIntegration.TrySetModSettings(currentCollectionId, optionsEditingMod.Directory, optionsEditingMod.Name, groupName, options);
                        await Task.Delay(10); // Small delay to avoid overwhelming Penumbra
                    }
                });
            }
            
            // Saved mod options to design (log removed to prevent spam)
        }
        
        /// <summary>
        /// Clear the mod options from the design (use Penumbra defaults)
        /// </summary>
        private void ClearModOptionsFromDesign()
        {
            if (optionsEditingMod == null || editingDesign == null)
                return;
                
            // Remove from design settings
            editingDesign.ModOptionSettings?.Remove(optionsEditingMod.Directory);
            
            // Cleared mod options from design (log removed to prevent spam)
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
        
        /// <summary>
        /// Public static method to determine mod type using the sophisticated path analysis.
        /// This allows other parts of the plugin to use the same categorization logic as the UI.
        /// </summary>
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

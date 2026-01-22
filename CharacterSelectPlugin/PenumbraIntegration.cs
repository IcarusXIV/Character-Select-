using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin;

namespace CharacterSelectPlugin
{
    /// <summary>
    /// Penumbra API Collection Types
    /// </summary>
    public enum ApiCollectionType : byte
    {
        Yourself = 0,
        Current = 0xE2,
        Default = 0xE0,
        Interface = 0xE1
    }

    /// <summary>
    /// Penumbra API Error Codes
    /// </summary>
    public enum PenumbraApiEc
    {
        Success = 0,
        NothingChanged = 1,
        CollectionMissing = 2,
        InvalidArgument = 11,
        UnknownError = 255
    }
    /// <summary>
    /// Handles integration with Penumbra API for collection management and mod tagging
    /// </summary>
    public class PenumbraIntegration : IDisposable
    {
        private readonly IPluginLog log;
        private readonly IDalamudPluginInterface pluginInterface;
        
        // Availability check  
        private ICallGateSubscriber<int>? penumbraApiVersion;
        
        // Event subscribers for mod cache updates - using EventSubscriber pattern
        private IDisposable? modAddedSubscriber;
        private IDisposable? modDeletedSubscriber; 
        private IDisposable? modMovedSubscriber;
        
        // Static debounce mechanism for mod deletion warnings (shared across instances)
        private static readonly Dictionary<string, DateTime> recentModDeletionWarnings = new();
        private static readonly object debounceLock = new object();
        private readonly TimeSpan debounceTime = TimeSpan.FromSeconds(5); // Increased from 2 to 5 seconds
        
        public bool IsPenumbraAvailable { get; private set; }
        
        public PenumbraIntegration(IDalamudPluginInterface pluginInterface, IPluginLog log)
        {
            this.pluginInterface = pluginInterface;
            this.log = log;
            
            InitializePenumbraAPI();
        }
        
        private void InitializePenumbraAPI()
        {
            try
            {
                // Check if Penumbra is available
                penumbraApiVersion = pluginInterface.GetIpcSubscriber<int>("Penumbra.ApiVersion");
                var version = penumbraApiVersion.InvokeFunc();
                
                if (version < 5)
                {
                    log.Warning($"Penumbra API version {version} is too old, requires version 5+");
                    return;
                }
                
                // Penumbra API is available - IPC calls will be made on-demand
                
                // Initialize event subscribers for mod cache updates
                InitializeModEventSubscribers();
                
                IsPenumbraAvailable = true;
                log.Information($"Penumbra API v{version} integration initialized successfully");
            }
            catch (Exception ex)
            {
                log.Warning($"Failed to initialize Penumbra API: {ex.Message}");
                IsPenumbraAvailable = false;
            }
        }
        
        private void InitializeModEventSubscribers()
        {
            try
            {
                // Create EventSubscriber instances using the correct Penumbra pattern
                modAddedSubscriber = new EventSubscriber<string>(pluginInterface, "Penumbra.ModAdded", log, OnModAdded);
                modDeletedSubscriber = new EventSubscriber<string>(pluginInterface, "Penumbra.ModDeleted", log, OnModDeleted);
                modMovedSubscriber = new EventSubscriber<string, string>(pluginInterface, "Penumbra.ModMoved", log, OnModMoved);
                
                log.Information("Penumbra mod event subscriptions initialized successfully");
            }
            catch (Exception ex)
            {
                log.Warning($"Failed to initialize Penumbra mod event subscriptions: {ex.Message}");
            }
        }
        
        // Copy of Penumbra.Api EventSubscriber for single parameter events
        private sealed class EventSubscriber<T1> : IDisposable
        {
            private readonly string _label;
            private readonly IPluginLog _log;
            private readonly Dictionary<Action<T1>, Action<T1>> _delegates = new();
            private ICallGateSubscriber<T1, object?>? _subscriber;
            private bool _disabled;

            public EventSubscriber(IDalamudPluginInterface pi, string label, IPluginLog log, params Action<T1>[] actions)
            {
                _label = label;
                _log = log;
                try
                {
                    _subscriber = pi.GetIpcSubscriber<T1, object?>(label);
                    foreach (var action in actions)
                        Event += action;

                    _disabled = false;
                }
                catch (Exception e)
                {
                    _log.Error($"Error registering IPC Subscriber for {label}\n{e}");
                    _subscriber = null;
                }
            }

            public event Action<T1> Event
            {
                add
                {
                    if (_subscriber != null && !_delegates.ContainsKey(value))
                    {
                        void Action(T1 a)
                        {
                            try
                            {
                                value(a);
                            }
                            catch (Exception e)
                            {
                                _log.Error($"Exception invoking IPC event {_label}:\n{e}");
                            }
                        }

                        if (_delegates.TryAdd(value, Action) && !_disabled)
                            _subscriber.Subscribe(Action);
                    }
                }
                remove
                {
                    if (_subscriber != null && _delegates.Remove(value, out var action))
                        _subscriber.Unsubscribe(action);
                }
            }

            public void Dispose()
            {
                if (!_disabled)
                {
                    if (_subscriber != null)
                        foreach (var action in _delegates.Values)
                            _subscriber.Unsubscribe(action);

                    _disabled = true;
                }
                _subscriber = null;
                _delegates.Clear();
            }
        }

        // Copy of Penumbra.Api EventSubscriber for two parameter events
        private sealed class EventSubscriber<T1, T2> : IDisposable
        {
            private readonly string _label;
            private readonly IPluginLog _log;
            private readonly Dictionary<Action<T1, T2>, Action<T1, T2>> _delegates = new();
            private ICallGateSubscriber<T1, T2, object?>? _subscriber;
            private bool _disabled;

            public EventSubscriber(IDalamudPluginInterface pi, string label, IPluginLog log, params Action<T1, T2>[] actions)
            {
                _label = label;
                _log = log;
                try
                {
                    _subscriber = pi.GetIpcSubscriber<T1, T2, object?>(label);
                    foreach (var action in actions)
                        Event += action;

                    _disabled = false;
                }
                catch (Exception e)
                {
                    _log.Error($"Error registering IPC Subscriber for {label}\n{e}");
                    _subscriber = null;
                }
            }

            public event Action<T1, T2> Event
            {
                add
                {
                    if (_subscriber != null && !_delegates.ContainsKey(value))
                    {
                        void Action(T1 a, T2 b)
                        {
                            try
                            {
                                value(a, b);
                            }
                            catch (Exception e)
                            {
                                _log.Error($"Exception invoking IPC event {_label}:\n{e}");
                            }
                        }

                        if (_delegates.TryAdd(value, Action) && !_disabled)
                            _subscriber.Subscribe(Action);
                    }
                }
                remove
                {
                    if (_subscriber != null && _delegates.Remove(value, out var action))
                        _subscriber.Unsubscribe(action);
                }
            }

            public void Dispose()
            {
                if (!_disabled)
                {
                    if (_subscriber != null)
                        foreach (var action in _delegates.Values)
                            _subscriber.Unsubscribe(action);

                    _disabled = true;
                }
                _subscriber = null;
                _delegates.Clear();
            }
        }
        
        private void OnModAdded(string modDirectoryName)
        {
            try
            {
                log.Information($"New mod detected: {modDirectoryName}");
                
                // Get mod display name
                var modList = GetModList();
                if (modList != null && modList.TryGetValue(modDirectoryName, out var modDisplayName))
                {
                    // Categorize the new mod and update cache
                    var plugin = CharacterSelectPlugin.Plugin.Instance;
                    if (plugin?.modCategorizationCache != null)
                    {
                        var modType = CharacterSelectPlugin.Windows.SecretModeModWindow.DetermineModType(modDirectoryName, modDisplayName, plugin);
                        plugin.modCategorizationCache[modDirectoryName] = modType;
                        
                        // Update persistent cache
                        plugin.UpdateModCache(modDirectoryName, modDisplayName, modType);
                        
                        log.Information($"Added new mod '{modDisplayName}' to cache as {modType}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error($"Error processing added mod {modDirectoryName}: {ex}");
            }
        }
        
        private void OnModDeleted(string modDirectoryName)
        {
            try
            {
                log.Information($"Mod deleted: {modDirectoryName}");
                
                // Check debounce - prevent duplicate warnings for the same mod
                var now = DateTime.UtcNow;
                bool shouldSkip = false;
                
                lock (debounceLock)
                {
                    if (recentModDeletionWarnings.TryGetValue(modDirectoryName, out var lastWarning))
                    {
                        var timeSinceLastWarning = now - lastWarning;
                        log.Information($"Mod {modDirectoryName}: Last warning was {timeSinceLastWarning.TotalSeconds:F2} seconds ago");
                        
                        if (timeSinceLastWarning < debounceTime)
                        {
                            log.Information($"DEBOUNCE: Skipping duplicate mod deletion warning for {modDirectoryName} (within {debounceTime.TotalSeconds}s debounce period)");
                            shouldSkip = true;
                        }
                    }
                    else
                    {
                        log.Information($"Mod {modDirectoryName}: First deletion event seen");
                    }
                }
                
                if (shouldSkip) return;
                
                // Check if any character/design uses this mod in Conflict Resolution
                var plugin = CharacterSelectPlugin.Plugin.Instance;
                if (plugin != null)
                {
                    var affectedItems = new List<(string character, string design)>();
                    
                    foreach (var character in plugin.Characters)
                    {
                        // Check character-level CR mods
                        if (character.SecretModState?.ContainsKey(modDirectoryName) == true)
                        {
                            affectedItems.Add((character.Name, "Base Character"));
                        }
                        
                        // Check each design's CR mods
                        foreach (var design in character.Designs)
                        {
                            if (design.SecretModState?.ContainsKey(modDirectoryName) == true)
                            {
                                affectedItems.Add((character.Name, design.Name));
                            }
                        }
                    }
                    
                    // Send warning if any characters/designs are affected
                    if (affectedItems.Count > 0)
                    {
                        // Extract mod name from directory path for cleaner display
                        var modName = System.IO.Path.GetFileName(modDirectoryName) ?? modDirectoryName;
                        
                        // Build single consolidated message in red
                        var messageBuilder = new System.Text.StringBuilder();
                        
                        if (affectedItems.Count == 1)
                        {
                            messageBuilder.AppendLine($"[CS+] WARNING: Deleted mod '{modName}' was used in the following Design:");
                        }
                        else
                        {
                            messageBuilder.AppendLine($"[CS+] WARNING: Deleted mod '{modName}' was used in the following Designs:");
                        }
                        
                        foreach (var (character, design) in affectedItems)
                        {
                            if (design == "Base Character")
                            {
                                messageBuilder.AppendLine($"  • {character} (Character-level CR)");
                            }
                            else
                            {
                                messageBuilder.AppendLine($"  • {character} → Design: {design}");
                            }
                        }
                        
                        if (affectedItems.Count == 1)
                        {
                            messageBuilder.Append("This design may not apply correctly, please double check.");
                        }
                        else
                        {
                            messageBuilder.Append("These designs may not apply correctly, please double check.");
                        }
                        
                        Plugin.ChatGui.PrintError(messageBuilder.ToString());
                        
                        // Record this warning to prevent duplicates
                        lock (debounceLock)
                        {
                            recentModDeletionWarnings[modDirectoryName] = now;
                            
                            // Clean up old entries (older than 10 minutes)
                            var cutoff = now - TimeSpan.FromMinutes(10);
                            var keysToRemove = recentModDeletionWarnings
                                .Where(kvp => kvp.Value < cutoff)
                                .Select(kvp => kvp.Key)
                                .ToList();
                            foreach (var key in keysToRemove)
                            {
                                recentModDeletionWarnings.Remove(key);
                            }
                        }
                    }
                    
                    // Remove from cache
                    if (plugin.modCategorizationCache != null && plugin.modCategorizationCache.ContainsKey(modDirectoryName))
                    {
                        plugin.modCategorizationCache.Remove(modDirectoryName);
                        
                        // Update persistent cache
                        plugin.RemoveFromModCache(modDirectoryName);
                        
                        log.Information($"Removed deleted mod '{modDirectoryName}' from cache");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error($"Error processing deleted mod {modDirectoryName}: {ex}");
            }
        }
        
        private void OnModMoved(string oldDirectoryName, string newDirectoryName)
        {
            try
            {
                log.Information($"Mod moved/renamed: {oldDirectoryName} -> {newDirectoryName}");
                
                // Update cache with new directory name
                var plugin = CharacterSelectPlugin.Plugin.Instance;
                if (plugin?.modCategorizationCache != null && plugin.modCategorizationCache.TryGetValue(oldDirectoryName, out var modType))
                {
                    // Remove old entry and add new entry
                    plugin.modCategorizationCache.Remove(oldDirectoryName);
                    plugin.modCategorizationCache[newDirectoryName] = modType;
                    
                    // Update persistent cache
                    var modList = GetModList();
                    var modDisplayName = modList?.GetValueOrDefault(newDirectoryName, newDirectoryName) ?? newDirectoryName;
                    plugin.MoveInModCache(oldDirectoryName, newDirectoryName, modDisplayName, modType);
                    
                    log.Information($"Updated cache for moved mod: {oldDirectoryName} -> {newDirectoryName}");
                }
            }
            catch (Exception ex)
            {
                log.Error($"Error processing moved mod {oldDirectoryName} -> {newDirectoryName}: {ex}");
            }
        }
        
        /// <summary>
        /// Switch the Penumbra UI to display the specified collection and set it as current
        /// This fixes both the collection assignment and the UI display
        /// </summary>
        public bool SwitchCollection(string collectionName)
        {
            if (!IsPenumbraAvailable)
            {
                log.Warning("Penumbra API not available for collection switching");
                return false;
            }
            
            try
            {
                // First, get all available collections to find the GUID
                var collections = GetAvailableCollections();
                var targetCollection = collections.FirstOrDefault(kvp => kvp.Value == collectionName);
                
                if (targetCollection.Key == Guid.Empty)
                {
                    log.Warning($"Collection '{collectionName}' not found in available collections");
                    return false;
                }
                
                // Use the correct SetCollection API signature
                // Only set "Current" to update the Penumbra UI display (collection assignment already works)
                var setCollectionIpc = pluginInterface.GetIpcSubscriber<byte, Guid?, bool, bool, (int, (Guid Id, string Name)?)>("Penumbra.SetCollection");
                
                log.Debug($"Setting Penumbra UI current collection - Name: {collectionName}, GUID: {targetCollection.Key}");
                
                // Set the current/UI collection for display only
                var (resultInt, oldCollection) = setCollectionIpc.InvokeFunc(
                    (byte)ApiCollectionType.Current,  // Set as current collection (controls UI display only)
                    targetCollection.Key,       // Collection GUID
                    false,                      // Don't allow creation
                    false                       // Don't allow deletion
                );
                
                var result = (PenumbraApiEc)resultInt;
                
                log.Debug($"SetCollection(Current) result: {result}");
                
                if (result == PenumbraApiEc.Success || result == PenumbraApiEc.NothingChanged)
                {
                    log.Information($"Successfully switched Penumbra UI to collection: {collectionName} (GUID: {targetCollection.Key})");
                    return true;
                }
                else
                {
                    log.Warning($"Failed to switch Penumbra collection '{collectionName}': {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                log.Error($"Error switching to collection '{collectionName}': {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Get list of all available Penumbra collections
        /// </summary>
        public Dictionary<Guid, string> GetAvailableCollections()
        {
            if (!IsPenumbraAvailable)
                return new Dictionary<Guid, string>();
            
            try
            {
                var ipc = pluginInterface.GetIpcSubscriber<Dictionary<Guid, string>>("Penumbra.GetCollections.V5");
                return ipc.InvokeFunc();
            }
            catch (Exception ex)
            {
                log.Error($"Error getting Penumbra collections: {ex}");
                return new Dictionary<Guid, string>();
            }
        }
        
        /// <summary>
        /// Get the collection actually affecting the player character (most accurate method)
        /// </summary>
        public (bool success, Guid collectionId, string collectionName) GetPlayerCollection()
        {
            if (!IsPenumbraAvailable)
                return (false, Guid.Empty, string.Empty);
            
            try
            {
                // Use GetCollectionForObject with object ID 0 (player) - most accurate method
                var ipc = pluginInterface.GetIpcSubscriber<int, (bool, bool, (Guid, string))>("Penumbra.GetCollectionForObject.V5");
                var (objectValid, individualSet, (id, name)) = ipc.InvokeFunc(0); // 0 = player object
                
                if (objectValid)
                {
                    return (true, id, name);
                }
                
                log.Warning("Player object not valid for collection detection");
                return (false, Guid.Empty, string.Empty);
            }
            catch (Exception ex)
            {
                log.Debug($"GetCollectionForObject.V5 failed: {ex.Message}, trying fallback");
                
                // Fallback to the older method
                return GetCurrentCollectionFallback();
            }
        }
        
        /// <summary>
        /// Fallback collection detection method
        /// </summary>
        private (bool success, Guid collectionId, string collectionName) GetCurrentCollectionFallback()
        {
            try
            {
                // Try the GetCollection method
                var ipc = pluginInterface.GetIpcSubscriber<byte, (Guid, string)?>("Penumbra.GetCollection");
                var result = ipc.InvokeFunc(0); // 0 = current character/yourself (ApiCollectionType.Current)
                
                if (result?.Item1 != null && result?.Item2 != null)
                {
                    log.Information($"Fallback: got current collection: {result.Value.Item2}");
                    return (true, result.Value.Item1, result.Value.Item2);
                }
                
                // Final fallback: use first available collection
                var collections = GetAvailableCollections();
                if (collections.Any())
                {
                    var firstCollection = collections.First();
                    log.Information($"Final fallback: using first available collection: {firstCollection.Value}");
                    return (true, firstCollection.Key, firstCollection.Value);
                }
                
                return (false, Guid.Empty, string.Empty);
            }
            catch (Exception ex)
            {
                log.Error($"All collection detection methods failed: {ex}");
                return (false, Guid.Empty, string.Empty);
            }
        }
        
        /// <summary>
        /// Get the current collection ID safely (legacy method for backward compatibility)
        /// </summary>
        public (bool success, Guid collectionId, string collectionName) GetCurrentCollection()
        {
            return GetPlayerCollection();
        }
        
        /// <summary>
        /// Get list of all installed mods
        /// </summary>
        public Dictionary<string, string> GetModList()
        {
            if (!IsPenumbraAvailable)
                return new Dictionary<string, string>();
            
            try
            {
                var ipc = pluginInterface.GetIpcSubscriber<Dictionary<string, string>>("Penumbra.GetModList");
                return ipc.InvokeFunc();
            }
            catch (Exception ex)
            {
                log.Error($"Error getting mod list: {ex}");
                return new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Get the Penumbra mod directory path
        /// </summary>
        public string? GetModDirectory()
        {
            if (!IsPenumbraAvailable)
                return null;
            
            try
            {
                var ipc = pluginInterface.GetIpcSubscriber<string>("Penumbra.GetModDirectory");
                return ipc.InvokeFunc();
            }
            catch (Exception ex)
            {
                log.Error($"Error getting mod directory: {ex}");
                return null;
            }
        }
        
        /// <summary>
        /// Get changed items for a specific mod
        /// </summary>
        public Dictionary<string, object?> GetModChangedItems(string modDirectory, string modName)
        {
            if (!IsPenumbraAvailable)
                return new Dictionary<string, object?>();
            
            try
            {
                var ipc = pluginInterface.GetIpcSubscriber<string, string, Dictionary<string, object?>>("Penumbra.GetChangedItems.V5");
                return ipc.InvokeFunc(modDirectory, modName);
            }
            catch (Exception ex)
            {
                log.Error($"Error getting changed items for mod {modName}: {ex}");
                return new Dictionary<string, object?>();
            }
        }
        
        /// <summary>
        /// Get all mods with their changed items for analysis
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> GetAllModsChangedItems()
        {
            if (!IsPenumbraAvailable)
                return new Dictionary<string, IReadOnlyDictionary<string, object?>>();
            
            try
            {
                var ipc = pluginInterface.GetIpcSubscriber<IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>>("Penumbra.GetChangedItemAdapterDictionary");
                return ipc.InvokeFunc();
            }
            catch (Exception ex)
            {
                log.Error($"Error getting all mods changed items: {ex}");
                return new Dictionary<string, IReadOnlyDictionary<string, object?>>();
            }
        }
        
        /// <summary>
        /// Get comprehensive changed item data - shows ALL mod impacts (using the correct API)
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> GetChangedItemAdapterDictionary()
        {
            if (!IsPenumbraAvailable)
                return new Dictionary<string, IReadOnlyDictionary<string, object?>>();
            
            try
            {
                var ipc = pluginInterface.GetIpcSubscriber<IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>>(
                    "Penumbra.GetChangedItemAdapterDictionary");
                var result = ipc.InvokeFunc();
                log.Information($"GetChangedItemAdapterDictionary returned {result.Count} mods with changed items");
                return result;
            }
            catch (Exception ex)
            {
                log.Error($"Error getting changed item adapter dictionary: {ex}");
                return new Dictionary<string, IReadOnlyDictionary<string, object?>>();
            }
        }
        
        /// <summary>
        /// Check which mods affect a specific changed item (reverse lookup)
        /// </summary>
        public (string ModDirectory, string ModName)[] CheckCurrentChangedItem(string changedItem)
        {
            if (!IsPenumbraAvailable)
                return Array.Empty<(string, string)>();
            
            try
            {
                // Get the function first
                var ipcFunc = pluginInterface.GetIpcSubscriber<Func<string, (string, string)[]>>(
                    "Penumbra.CheckCurrentChangedItemFunc");
                var checkFunc = ipcFunc.InvokeFunc();
                
                // Use the function to check the item
                return checkFunc(changedItem);
            }
            catch (Exception ex)
            {
                log.Debug($"Error checking current changed item: {ex}");
                return Array.Empty<(string, string)>();
            }
        }
        
        /// <summary>
        /// Get the CheckCurrentChangedItem function for direct use
        /// </summary>
        public Func<string, (string, string)[]>? GetCheckCurrentChangedItemFunction()
        {
            if (!IsPenumbraAvailable)
                return null;
            
            try
            {
                var ipcFunc = pluginInterface.GetIpcSubscriber<Func<string, (string, string)[]>>(
                    "Penumbra.CheckCurrentChangedItemFunc");
                return ipcFunc.InvokeFunc();
            }
            catch (Exception ex)
            {
                log.Debug($"Error getting CheckCurrentChangedItemFunc: {ex}");
                return null;
            }
        }
        
        /// <summary>
        /// Analyze what mods are actually shown in Penumbra's On-Screen tab
        /// This will help us understand what we should be detecting
        /// </summary>
        public void AnalyzeOnScreenTabMods()
        {
            // Method removed to reduce log spam
        }
        
        /// <summary>
        /// Log the structure of a resource tree for debugging
        /// </summary>
        private void LogResourceTreeStructure(JsonElement element, int depth = 0)
        {
            var indent = new string(' ', depth * 2);
            
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    log.Information($"{indent}{property.Name}: {property.Value.ValueKind}");
                    if (depth < 3) // Limit recursion depth
                    {
                        LogResourceTreeStructure(property.Value, depth + 1);
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array && depth < 2)
            {
                var array = element.EnumerateArray().Take(3).ToList(); // First 3 elements
                log.Information($"{indent}Array with {element.GetArrayLength()} elements");
                for (int i = 0; i < array.Count; i++)
                {
                    log.Information($"{indent}[{i}]:");
                    LogResourceTreeStructure(array[i], depth + 1);
                }
            }
            else
            {
                var value = element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
                if (value?.Length > 100) value = value.Substring(0, 100) + "...";
                log.Information($"{indent}Value: {value}");
            }
        }
        
        /// <summary>
        /// Test various potential IPC methods to find On-Screen tab equivalent
        /// </summary>
        public void TestOnScreenMethods()
        {
            if (!IsPenumbraAvailable)
                return;
                
            log.Information("Testing Penumbra IPC methods for On-Screen tab equivalent...");
                
            var potentialMethods = new[]
            {
                "Penumbra.GetEffectiveChanges",
                "Penumbra.GetEffectiveChanges.V5", 
                "Penumbra.GetCurrentChanges",
                "Penumbra.GetCurrentChanges.V5",
                "Penumbra.GetActiveChanges",
                "Penumbra.GetActiveChanges.V5",
                "Penumbra.GetResolvedFiles",
                "Penumbra.GetResolvedFiles.V5",
                "Penumbra.GetCurrentlyAppliedMods",
                "Penumbra.GetCurrentlyAppliedMods.V5",
                "Penumbra.GetGameObjectChanges",
                "Penumbra.GetGameObjectChanges.V5",
                "Penumbra.ResolveGameObjectPath",
                "Penumbra.ResolveGameObjectPath.V5",
                "Penumbra.ResolvePlayerPath",
                "Penumbra.ResolvePlayerPath.V5",
                "Penumbra.GetGamePaths",
                "Penumbra.GetGamePaths.V5"
            };
            
            foreach (var method in potentialMethods)
            {
                try
                {
                    // Try different parameter combinations
                    var ipc1 = pluginInterface.GetIpcSubscriber<object>(method);
                    var result1 = ipc1.InvokeFunc();
                    log.Information($"IPC Method {method} (no params) exists and returned: {result1?.GetType()}");
                    
                    if (result1 != null)
                    {
                        log.Information($"  Result sample: {result1}");
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        var ipc2 = pluginInterface.GetIpcSubscriber<int, object>(method);
                        var result2 = ipc2.InvokeFunc(0); // Try with collection type
                        log.Information($"IPC Method {method} (int param) exists and returned: {result2?.GetType()}");
                        
                        if (result2 != null)
                        {
                            log.Information($"  Result sample: {result2}");
                        }
                    }
                    catch (Exception ex2)
                    {
                        try
                        {
                            // Try with string parameter (game path)
                            var ipc3 = pluginInterface.GetIpcSubscriber<string, object>(method);
                            var result3 = ipc3.InvokeFunc("chara/equipment/e6001/model/c0101e6001_top.mdl");
                            log.Information($"IPC Method {method} (string param) exists and returned: {result3?.GetType()}");
                            
                            if (result3 != null)
                            {
                                log.Information($"  Result sample: {result3}");
                            }
                        }
                        catch (Exception ex3)
                        {
                            log.Debug($"IPC Method {method} not available: {ex.Message}");
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Get resolved file paths for the current character - this might be the On-Screen tab equivalent
        /// </summary>
        public Dictionary<string, string> GetResolvedPaths()
        {
            if (!IsPenumbraAvailable)
                return new Dictionary<string, string>();
            
            try
            {
                // Try to get resolved paths which should show what mods are actually affecting items
                var ipc = pluginInterface.GetIpcSubscriber<Dictionary<string, string>>("Penumbra.GetResolvedFiles.V5");
                return ipc.InvokeFunc();
            }
            catch (Exception ex)
            {
                log.Debug($"GetResolvedFiles.V5 not available: {ex.Message}");
                return new Dictionary<string, string>();
            }
        }
        
        /// <summary>
        /// Get the exact same data as Penumbra's On-Screen tab - resolved file mappings
        /// This shows ACTUAL paths -> game paths mappings for what's currently loaded
        /// </summary>
        public Dictionary<ushort, Dictionary<string, HashSet<string>>> GetOnScreenTabData()
        {
            if (!IsPenumbraAvailable)
                return new Dictionary<ushort, Dictionary<string, HashSet<string>>>();
            
            try
            {
                var ipc = pluginInterface.GetIpcSubscriber<Dictionary<ushort, Dictionary<string, HashSet<string>>>>(
                    "Penumbra.GetPlayerResourcePaths.V5");
                var result = ipc.InvokeFunc();
                log.Information($"GetPlayerResourcePaths.V5 returned data for {result.Count} objects");
                
                // Log some sample data to understand the structure
                foreach (var (objectId, pathMappings) in result.Take(1))
                {
                    log.Information($"Object {objectId} has {pathMappings.Count} path mappings");
                    foreach (var (actualPath, gamePaths) in pathMappings.Take(5))
                    {
                        log.Debug($"  Actual: {actualPath} -> Game: [{string.Join(", ", gamePaths)}]");
                    }
                }
                
                return result;
            }
            catch (Exception ex)
            {
                log.Debug($"GetPlayerResourcePaths.V5 not available: {ex.Message}");
                return new Dictionary<ushort, Dictionary<string, HashSet<string>>>();
            }
        }
        
        /// <summary>
        /// Get complete resource trees with mod information - shows which mod is affecting each path
        /// This is the same data that Penumbra's On-Screen tab uses
        /// </summary>
        public Dictionary<ushort, object> GetResourceTreesWithModInfo()
        {
            if (!IsPenumbraAvailable)
            {
                log.Debug("Penumbra not available for GetResourceTreesWithModInfo");
                return new Dictionary<ushort, object>();
            }
            
            try
            {
                // Try newer version first (post-PCP additions)
                try
                {
                    var ipc6 = pluginInterface.GetIpcSubscriber<bool, Dictionary<ushort, object>>(
                        "Penumbra.GetPlayerResourceTrees.V6");
                    var result6 = ipc6.InvokeFunc(true); // true for UI data including mod names
                    log.Information($"[CRITICAL] GetPlayerResourceTrees.V6 returned {result6.Count} objects. Object IDs: [{string.Join(", ", result6.Keys)}]");
                    return result6;
                }
                catch (Exception ex6)
                {
                    log.Information($"[CRITICAL] V6 failed: {ex6.Message}");
                }
                
                // Fallback to V5
                var ipc = pluginInterface.GetIpcSubscriber<bool, Dictionary<ushort, object>>(
                    "Penumbra.GetPlayerResourceTrees.V5");
                var result = ipc.InvokeFunc(true); // true for UI data including mod names
                log.Information($"[CRITICAL] GetPlayerResourceTrees.V5 returned {result.Count} objects. Object IDs: [{string.Join(", ", result.Keys)}]");
                
                return result;
            }
            catch (Exception ex)
            {
                log.Warning($"GetPlayerResourceTrees failed: {ex.Message}");
                return new Dictionary<ushort, object>();
            }
        }
        
        /// <summary>
        /// Extract affecting mod names using the exact same method as Penumbra's On-Screen tab
        /// </summary>
        public HashSet<string> GetOnScreenTabMods()
        {
            log.Information("[CRITICAL] GetOnScreenTabMods called - starting execution");
            var affectingMods = new HashSet<string>();
            
            try
            {
                // The On-Screen tab gets all visible character resource trees, not just player-related ones
                // Try the method that gets all game object resource trees currently loaded
                try
                {
                    var gameObjectTreesIpc = pluginInterface.GetIpcSubscriber<bool, ushort[], Dictionary<ushort, object>>("Penumbra.GetGameObjectResourceTrees.V5");
                    // Get resource trees for all currently loaded game objects (empty array means all)
                    var allResourceTrees = gameObjectTreesIpc.InvokeFunc(true, new ushort[0]);
                    
                    log.Information($"[CRITICAL] GetGameObjectResourceTrees found {allResourceTrees.Count} objects");
                    
                    // Extract mod names from all loaded resource trees
                    foreach (var (objectId, treeObj) in allResourceTrees)
                    {
                        log.Debug($"Processing resource tree for object {objectId}");
                        
                        if (treeObj != null)
                        {
                            ExtractModNamesFromResourceTree(treeObj, affectingMods);
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Information($"GetGameObjectResourceTrees failed, trying GetPlayerResourceTrees: {ex.Message}");
                    
                    // Fallback to player resource trees only
                    var playerTreesIpc = pluginInterface.GetIpcSubscriber<bool, Dictionary<ushort, object>>("Penumbra.GetPlayerResourceTrees.V5");
                    var playerResourceTrees = playerTreesIpc.InvokeFunc(true);
                    
                    log.Information($"[CRITICAL] GetPlayerResourceTrees found {playerResourceTrees.Count} objects");
                    
                    foreach (var (objectId, treeObj) in playerResourceTrees)
                    {
                        log.Debug($"Processing player resource tree for object {objectId}");
                        
                        if (treeObj != null)
                        {
                            ExtractModNamesFromResourceTree(treeObj, affectingMods);
                        }
                    }
                }
                
                log.Debug($"Final affecting mods count: {affectingMods.Count}");
                
                return affectingMods;
            }
            catch (Exception ex)
            {
                log.Error($"[CRITICAL] Error in GetOnScreenTabMods: {ex}");
                return affectingMods;
            }
        }
        
        /// <summary>
        /// Extract mod name from a file path (e.g., "F:\XIVMODS2\ModName\..." -> "ModName")
        /// </summary>
        private string? ExtractModNameFromPath(string filePath)
        {
            try
            {
                // Log some sample paths to understand the structure
                
                var parts = filePath.Split('\\', '/');
                
                // Look for common mod directory patterns
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    var part = parts[i];
                    
                    // Common mod directory names
                    if (part.Contains("XIVMODS", StringComparison.OrdinalIgnoreCase) ||
                        part.Contains("mods", StringComparison.OrdinalIgnoreCase) ||
                        part.Contains("Penumbra", StringComparison.OrdinalIgnoreCase))
                    {
                        // The next part should be the mod name
                        if (i + 1 < parts.Length)
                        {
                            var modName = parts[i + 1];
                            log.Debug($"Found mod name: {modName}");
                            return modName;
                        }
                    }
                }
                
                // Fallback: if it's a rooted path, try to find the mod name differently
                if (Path.IsPathRooted(filePath))
                {
                    // For paths like F:\XIVMODS2\ModName\..., take the folder after the drive
                    if (parts.Length >= 3)
                    {
                        var modName = parts[2]; // Skip drive and first folder
                        log.Debug($"Fallback mod name: {modName}");
                        return modName;
                    }
                }
                
                log.Debug("Could not extract mod name from path");
                return null;
            }
            catch (Exception ex)
            {
                log.Debug($"Error extracting mod name from path: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Recursively extract mod names from resource tree data
        /// </summary>
        private void ExtractModNamesFromResourceTree(object treeData, HashSet<string> affectingMods)
        {
            try
            {
                // Convert to JSON string and parse with System.Text.Json like the original
                var jsonString = treeData.ToString();
                if (string.IsNullOrEmpty(jsonString)) return;
                
                var resourceTree = JsonDocument.Parse(jsonString);
                var foundMods = ExtractModNamesFromResourceTreeJson(resourceTree.RootElement);
                
                foreach (var mod in foundMods)
                {
                    affectingMods.Add(mod);
                }
            }
            catch (Exception ex)
            {
                log.Debug($"Error extracting mod names from tree data: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Extract mod names from resource tree JSON - looks for mod indicators in paths
        /// </summary>
        private HashSet<string> ExtractModNamesFromResourceTreeJson(JsonElement resourceTree)
        {
            var modNames = new HashSet<string>();
            
            try
            {
                // Look for Nodes array
                if (!resourceTree.TryGetProperty("Nodes", out var nodesElement) ||
                    nodesElement.ValueKind != JsonValueKind.Array)
                {
                    return modNames;
                }
                
                foreach (var node in nodesElement.EnumerateArray())
                {
                    ExtractModNamesFromNode(node, modNames);
                }
            }
            catch (Exception ex)
            {
                log.Debug($"Error extracting mod names from resource tree: {ex.Message}");
            }
            
            return modNames;
        }
        
        /// <summary>
        /// Extract mod names from a single resource node
        /// </summary>
        private void ExtractModNamesFromNode(JsonElement node, HashSet<string> modNames)
        {
            try
            {
                // Check if this node has an ActualPath that differs from GamePath
                var hasActualPath = node.TryGetProperty("ActualPath", out var actualPathElement);
                var hasGamePath = node.TryGetProperty("GamePath", out var gamePathElement);
                
                if (hasActualPath && hasGamePath)
                {
                    var actualPath = actualPathElement.GetString();
                    var gamePath = gamePathElement.GetString();
                    
                    // If paths differ, this indicates a mod is affecting this resource
                    if (!string.IsNullOrEmpty(actualPath) && !string.IsNullOrEmpty(gamePath) &&
                        !actualPath.Equals(gamePath, StringComparison.OrdinalIgnoreCase))
                    {
                        // Extract mod name from the actual path
                        var modName = ExtractModNameFromActualPath(actualPath);
                        if (!string.IsNullOrEmpty(modName))
                        {
                            modNames.Add(modName);
                        }
                    }
                }
                
                // Process child nodes recursively
                if (node.TryGetProperty("Children", out var childrenElement) &&
                    childrenElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in childrenElement.EnumerateArray())
                    {
                        ExtractModNamesFromNode(child, modNames);
                    }
                }
            }
            catch (Exception ex)
            {
                log.Debug($"Error extracting mod names from node: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Extract mod name from an actual path that contains mod information
        /// This handles the format seen in the On-Screen tab like "[M] Shall we Dance v1.0 | top | medium"
        /// </summary>
        private string? ExtractModNameFromActualPath(string actualPath)
        {
            if (string.IsNullOrEmpty(actualPath)) return null;
            
            try
            {
                // Method 1: Check for bracketed mod names like "[M] Shall we Dance v1.0"
                if (actualPath.StartsWith("[") && actualPath.Contains("]"))
                {
                    var closeBracket = actualPath.IndexOf(']');
                    if (closeBracket > 0)
                    {
                        var modPrefix = actualPath.Substring(0, closeBracket + 1);
                        
                        // Look for the mod name after the bracket
                        var afterBracket = actualPath.Substring(closeBracket + 1).Trim();
                        if (afterBracket.Contains(" | "))
                        {
                            // Format: "[M] Mod Name | option | variant"
                            var parts = afterBracket.Split(" | ");
                            if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                            {
                                return modPrefix + " " + parts[0].Trim();
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(afterBracket))
                        {
                            // Format: "[M] Mod Name"
                            return modPrefix + " " + afterBracket.Split('|')[0].Trim();
                        }
                    }
                }
                
                // Method 2: Check for file system paths (fallback to directory extraction)
                if (actualPath.Contains("\\") && actualPath.Contains("XIVMODS", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = actualPath.Split('\\', '/');
                    
                    // Look for XIVMODS directory and take the next part as mod name
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        if (parts[i].Contains("XIVMODS", StringComparison.OrdinalIgnoreCase))
                        {
                            if (i + 1 < parts.Length)
                            {
                                return parts[i + 1];
                            }
                        }
                    }
                }
                
                log.Debug($"Could not extract mod name from path: {actualPath}");
                return null;
            }
            catch (Exception ex)
            {
                log.Debug($"Error extracting mod name from path {actualPath}: {ex.Message}");
                return null;
            }
        }
        
        private Func<string, (string ModDirectory, string ModName)[]>? GetCheckCurrentChangedItemFunc()
        {
            try
            {
                var ipc = pluginInterface.GetIpcSubscriber<Func<string, (string, string)[]>>("Penumbra.CheckCurrentChangedItemFunc");
                return ipc.InvokeFunc();
            }
            catch (Exception ex)
            {
                log.Error($"Error getting CheckCurrentChangedItemFunc: {ex}");
                return null;
            }
        }
        
        /// <summary>
        /// Extract mod names from resource tree JSON - looks for mod indicators in paths
        /// </summary>
        private HashSet<string> ExtractModNamesFromResourceTree(JsonElement resourceTree)
        {
            var modNames = new HashSet<string>();
            
            try
            {
                // Look for Nodes array
                if (!resourceTree.TryGetProperty("Nodes", out var nodesElement) ||
                    nodesElement.ValueKind != JsonValueKind.Array)
                {
                    return modNames;
                }
                
                foreach (var node in nodesElement.EnumerateArray())
                {
                    ExtractModNamesFromNode(node, modNames);
                }
            }
            catch (Exception ex)
            {
                log.Debug($"Error extracting mod names from resource tree: {ex.Message}");
            }
            
            return modNames;
        }
        
        /// <summary>
        /// Extract mods that are currently affecting the player - equivalent to Penumbra's On-Screen tab
        /// This uses the resource tree parsing that successfully identifies the correct mods
        /// </summary>
        public HashSet<string> GetCurrentlyAffectingMods(Guid? overrideCollectionId = null)
        {
            var affectingMods = new HashSet<string>();
            
            try
            {
                // Getting currently affecting mods using On-Screen tab method
                
                // Get the collection ID
                var collectionId = Guid.Empty;
                if (overrideCollectionId.HasValue && overrideCollectionId.Value != Guid.Empty)
                {
                    collectionId = overrideCollectionId.Value;
                }
                else
                {
                    var (success, detectedId, detectedName) = GetPlayerCollection();
                    if (success)
                    {
                        collectionId = detectedId;
                        // Using player collection
                    }
                    else
                    {
                        log.Warning("Could not get player collection");
                        return affectingMods;
                    }
                }
                
                // Method 1: Use the successful resource tree parsing approach
                log.Information("[GetCurrentlyAffectingMods] Trying Method 1: Resource tree parsing");
                var resourceTreeMods = GetOnScreenTabMods();
                log.Information($"[GetCurrentlyAffectingMods] Resource tree returned {resourceTreeMods.Count} mod names");
                
                if (resourceTreeMods.Any())
                {
                    // Resource tree parsing found mods
                    
                    // Now we need to map the extracted mod names to actual mod directory names
                    var modList = GetModList();
                    var modSettings = GetAllModSettingsRobust(collectionId);
                    
                    if (modList.Any() && modSettings != null)
                    {
                        // Debug: Log some sample mod list entries for comparison
                        
                        // Match the extracted mod names with actual mod directories
                        foreach (var extractedModName in resourceTreeMods)
                        {
                            // Try to find the mod directory by matching the extracted name
                            var matchingMod = FindModDirectoryByName(extractedModName, modList, modSettings, collectionId);
                            if (!string.IsNullOrEmpty(matchingMod))
                            {
                                affectingMods.Add(matchingMod);
                            }
                            else
                            {
                                log.Warning($"❌ Could not find mod directory for extracted name: '{extractedModName}'");
                                
                                // Debug: Show what the matching function tried
                                log.Information($"   Cleaned extracted name: '{CleanModName(extractedModName)}'");
                                log.Information($"   Checking against {modList.Count} mod list entries...");
                                
                                // Show some partial matches for debugging
                                var enabledMods = modSettings.Where(kvp => kvp.Value.Item1).Select(kvp => kvp.Key).ToHashSet();
                                var cleanedExtracted = CleanModName(extractedModName).ToLowerInvariant();
                                var partialMatches = new List<string>();
                                
                                foreach (var (modDir, modName) in modList)
                                {
                                    if (!enabledMods.Contains(modDir)) continue;
                                    
                                    var cleanedModName = CleanModName(modName).ToLowerInvariant();
                                    var cleanedModDir = CleanModName(modDir).ToLowerInvariant();
                                    
                                    if (cleanedExtracted.Contains(cleanedModName) || cleanedModName.Contains(cleanedExtracted) ||
                                        cleanedExtracted.Contains(cleanedModDir) || cleanedModDir.Contains(cleanedExtracted))
                                    {
                                        partialMatches.Add($"'{modDir}' (name: '{modName}')");
                                    }
                                }
                                
                                if (partialMatches.Any())
                                {
                                    log.Information($"   Potential partial matches: {string.Join(", ", partialMatches.Take(3))}");
                                }
                                else
                                {
                                    log.Information($"   No partial matches found among {enabledMods.Count} enabled mods");
                                }
                            }
                        }
                        
                        if (affectingMods.Any())
                        {
                            log.Debug($"Successfully mapped {affectingMods.Count} affecting mod directories");
                            return affectingMods;
                        }
                    }
                }
                
                // Method 2: Fallback to path-based approach if resource tree parsing didn't work
                log.Information("Using path-based fallback for affecting mods");
                var modSettings2 = GetAllModSettingsRobust(collectionId);
                if (modSettings2 == null)
                {
                    log.Warning("Could not get mod settings for collection");
                    return affectingMods;
                }
                
                var resourcePaths = GetOnScreenTabData();
                if (resourcePaths.Any())
                {
                    var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    
                    // Collect all paths that are currently loaded
                    foreach (var (objectId, pathMappings) in resourcePaths)
                    {
                        if (objectId != 0) continue; // Only player character
                        
                        foreach (var (actualPath, gamePaths) in pathMappings)
                        {
                            // Only interested in paths that differ from game paths (indicating mod override)
                            if (!string.IsNullOrEmpty(actualPath))
                            {
                                foreach (var gamePath in gamePaths)
                                {
                                    if (!actualPath.Equals(gamePath, StringComparison.OrdinalIgnoreCase))
                                    {
                                        loadedPaths.Add(gamePath);
                                    }
                                }
                            }
                        }
                    }
                    
                    log.Information($"Found {loadedPaths.Count} game paths with mod overrides");
                    
                    // Now check which enabled mods affect these paths
                    var changedItemData = GetChangedItemAdapterDictionary();
                    foreach (var (modDir, modChangedItems) in changedItemData)
                    {
                        if (!modSettings2.ContainsKey(modDir)) continue;
                        
                        var (isEnabled, priority, _, _, _) = modSettings2[modDir];
                        if (!isEnabled) continue;
                        
                        // Check if this mod affects any of the loaded paths
                        var affectsLoadedPaths = false;
                        foreach (var (itemPath, _) in modChangedItems)
                        {
                            if (loadedPaths.Contains(itemPath))
                            {
                                affectsLoadedPaths = true;
                                break;
                            }
                        }
                        
                        if (affectsLoadedPaths)
                        {
                            affectingMods.Add(modDir);
                            log.Debug($"Mod {modDir} affects currently loaded paths");
                        }
                    }
                    
                    if (affectingMods.Any())
                    {
                        log.Information($"Found {affectingMods.Count} mods affecting loaded paths");
                        return affectingMods;
                    }
                }
                
                // Method 3: Final fallback - show high-priority enabled gear/hair mods
                log.Information("Using priority-based fallback for affecting mods");
                
                var changedItems = GetChangedItemAdapterDictionary();
                foreach (var (modDir, changes) in changedItems)
                {
                    if (!modSettings2.ContainsKey(modDir)) continue;
                    
                    var (isEnabled, priority, _, _, _) = modSettings2[modDir];
                    if (!isEnabled || !changes.Any()) continue;
                    
                    // Only include high-priority mods that affect visible items
                    if (priority > 0)
                    {
                        var hasVisibleItems = false;
                        foreach (var (itemPath, _) in changes.Take(10))
                        {
                            if (IsVisibleGamePath(itemPath))
                            {
                                hasVisibleItems = true;
                                break;
                            }
                        }
                        
                        if (hasVisibleItems)
                        {
                            affectingMods.Add(modDir);
                            log.Debug($"High-priority mod: {modDir} (priority: {priority})");
                        }
                    }
                }
                
                log.Information($"Priority fallback found {affectingMods.Count} affecting mods");
            }
            catch (Exception ex)
            {
                log.Error($"Error getting currently affecting mods: {ex}");
            }
            
            return affectingMods;
        }
        
        /// <summary>
        /// Find the mod directory name that matches an extracted mod name from resource tree
        /// </summary>
        private string? FindModDirectoryByName(string extractedModName, Dictionary<string, string> modList, Dictionary<string, (bool, int, Dictionary<string, List<string>>, bool, bool)> modSettings, Guid collectionId)
        {
            if (string.IsNullOrEmpty(extractedModName)) return null;
            
            try
            {
                // Only consider enabled mods in the current collection
                var enabledMods = modSettings.Where(kvp => kvp.Value.Item1).Select(kvp => kvp.Key).ToHashSet();
                
                // Strategy 1: Exact directory name match (for cases where the extracted name is the directory)
                if (modList.ContainsKey(extractedModName) && enabledMods.Contains(extractedModName))
                {
                    return extractedModName;
                }
                
                // Strategy 2: Clean the extracted mod name and try partial matching
                var cleanedExtracted = CleanModName(extractedModName);
                
                foreach (var (modDir, modName) in modList)
                {
                    if (!enabledMods.Contains(modDir)) continue; // Only enabled mods
                    
                    var cleanedModName = CleanModName(modName);
                    var cleanedModDir = CleanModName(modDir);
                    
                    // Check if the cleaned extracted name contains or is contained in the mod name/directory
                    if (cleanedExtracted.Contains(cleanedModName, StringComparison.OrdinalIgnoreCase) ||
                        cleanedModName.Contains(cleanedExtracted, StringComparison.OrdinalIgnoreCase) ||
                        cleanedExtracted.Contains(cleanedModDir, StringComparison.OrdinalIgnoreCase) ||
                        cleanedModDir.Contains(cleanedExtracted, StringComparison.OrdinalIgnoreCase))
                    {
                        log.Information($"Partial match: '{extractedModName}' -> '{modDir}' (name: {modName})");
                        return modDir;
                    }
                }
                
                // Strategy 3: Try fuzzy word-based matching
                var extractedWords = ExtractKeyWords(cleanedExtracted);
                if (extractedWords.Any())
                {
                    var bestMatch = "";
                    var bestScore = 0;
                    
                    foreach (var (modDir, modName) in modList)
                    {
                        if (!enabledMods.Contains(modDir)) continue; // Only enabled mods
                        
                        var modWords = ExtractKeyWords(CleanModName(modName)).Concat(ExtractKeyWords(CleanModName(modDir))).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        
                        var matchingWords = extractedWords.Count(word => modWords.Contains(word));
                        if (matchingWords > bestScore && matchingWords >= Math.Min(2, extractedWords.Count))
                        {
                            bestScore = matchingWords;
                            bestMatch = modDir;
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(bestMatch))
                    {
                        log.Information($"Word-based match: '{extractedModName}' -> '{bestMatch}' (score: {bestScore})");
                        return bestMatch;
                    }
                }
                
                log.Debug($"No match found for extracted mod name: {extractedModName}");
                return null;
            }
            catch (Exception ex)
            {
                log.Debug($"Error finding mod directory for name {extractedModName}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Clean mod name for better matching - removes common prefixes, version numbers, etc.
        /// </summary>
        private string CleanModName(string modName)
        {
            if (string.IsNullOrEmpty(modName)) return "";
            
            var cleaned = modName;
            
            // Remove common prefixes
            if (cleaned.StartsWith("[M] ")) cleaned = cleaned.Substring(4);
            if (cleaned.StartsWith("[")) 
            {
                var closeBracket = cleaned.IndexOf(']');
                if (closeBracket >= 0) cleaned = cleaned.Substring(closeBracket + 1).Trim();
            }
            
            // Remove version numbers and common suffixes
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+v?\d+(\.\d+)*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s*\|\s*.*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase); // Remove "| option | variant"
            
            return cleaned.Trim();
        }
        
        /// <summary>
        /// Extract key words from a mod name for fuzzy matching
        /// </summary>
        private List<string> ExtractKeyWords(string text)
        {
            if (string.IsNullOrEmpty(text)) return new List<string>();
            
            // Split on common delimiters and filter out short/common words
            var words = text.Split(new char[] { ' ', '-', '_', '+', '(', ')', '[', ']', '|' }, StringSplitOptions.RemoveEmptyEntries)
                           .Where(w => w.Length >= 3) // Skip very short words
                           .Where(w => !new[] { "the", "and", "for", "with", "mod", "ffxiv" }.Contains(w.ToLowerInvariant()))
                           .ToList();
                           
            return words;
        }
        
        /// <summary>
        /// Check if a game path represents a visible item (equipment, hair, face, body)
        /// </summary>
        private bool IsVisibleGamePath(string itemPath)
        {
            if (string.IsNullOrEmpty(itemPath)) return false;
            
            var path = itemPath.ToLowerInvariant();
            
            // Equipment paths
            if (path.Contains("chara/equipment/") && (path.Contains(".mdl") || path.Contains(".tex"))) return true;
            
            // Hair paths
            if (path.Contains("chara/human/") && path.Contains("/hair/") && (path.Contains(".mdl") || path.Contains(".tex"))) return true;
            
            // Face paths
            if (path.Contains("chara/human/") && path.Contains("/face/") && (path.Contains(".mdl") || path.Contains(".tex"))) return true;
            
            // Body paths (but exclude skin - we only want gear and hair)
            if (path.Contains("chara/human/") && path.Contains("/body/") && path.Contains(".mdl")) return true;
            
            // Accessory paths
            if (path.Contains("chara/accessory/") && (path.Contains(".mdl") || path.Contains(".tex"))) return true;
            
            return false;
        }
        
        /// <summary>
        /// Simplified resource tree parsing - only looks for obvious mod paths
        /// </summary>
        private HashSet<string> ExtractModDirectoriesFromResourceTreeSimple(JsonElement resourceTree)
        {
            var modDirectories = new HashSet<string>();
            
            try
            {
                if (!resourceTree.TryGetProperty("Nodes", out var nodesElement) || 
                    nodesElement.ValueKind != JsonValueKind.Array)
                {
                    return modDirectories;
                }
                
                // Just look for obvious modded paths - paths that contain mod directory structures
                foreach (var node in nodesElement.EnumerateArray())
                {
                    if (node.TryGetProperty("ActualPath", out var actualPathElement))
                    {
                        var actualPath = actualPathElement.GetString();
                        if (!string.IsNullOrEmpty(actualPath) && actualPath.Contains("mods", StringComparison.OrdinalIgnoreCase))
                        {
                            var modDir = ExtractModDirectoryFromPath(actualPath);
                            if (!string.IsNullOrEmpty(modDir))
                            {
                                modDirectories.Add(modDir);
                                log.Debug($"Simple extraction found mod: {modDir}");
                            }
                        }
                    }
                    
                    // Recursively check children but keep it simple
                    if (node.TryGetProperty("Children", out var childrenElement) && 
                        childrenElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var child in childrenElement.EnumerateArray())
                        {
                            var childMods = ExtractModDirectoriesFromResourceTreeSimple(child);
                            foreach (var mod in childMods)
                            {
                                modDirectories.Add(mod);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Debug($"Error in simple resource tree extraction: {ex.Message}");
            }
            
            return modDirectories;
        }
        
        /// <summary>
        /// Extract mod directory names from a resource tree JSON structure
        /// The key insight: we need to find nodes where ActualPath differs from GamePath - those are modded files
        /// </summary>
        private HashSet<string> ExtractModDirectoriesFromResourceTree(JsonElement resourceTree)
        {
            var modDirectories = new HashSet<string>();
            
            // Get the Nodes array from the resource tree
            if (!resourceTree.TryGetProperty("Nodes", out var nodesElement))
            {
                log.Information("Resource tree has no 'Nodes' property");
                return modDirectories;
            }
            
            if (nodesElement.ValueKind != JsonValueKind.Array)
            {
                log.Information($"Nodes property is not an array, it's: {nodesElement.ValueKind}");
                return modDirectories;
            }
            
            var nodeCount = 0;
            foreach (var node in nodesElement.EnumerateArray())
            {
                nodeCount++;
            }
            log.Information($"Resource tree has {nodeCount} nodes to process");
            
            // Recursively extract mod directories from all nodes
            foreach (var node in nodesElement.EnumerateArray())
            {
                ExtractModDirectoriesFromNode(node, modDirectories);
            }
            
            return modDirectories;
        }
        
        /// <summary>
        /// Recursively extract mod directories from a resource node
        /// Key insight: modded files have ActualPath != GamePath, and ActualPath contains mod directory
        /// </summary>
        private void ExtractModDirectoriesFromNode(JsonElement node, HashSet<string> modDirectories)
        {
            // Log properties for first few nodes only
            var properties = node.EnumerateObject().Select(p => p.Name).ToList();
            
            // Get both ActualPath and GamePath to detect modifications
            var hasActualPath = node.TryGetProperty("ActualPath", out var actualPathElement);
            var hasGamePath = node.TryGetProperty("GamePath", out var gamePathElement);
            
            if (hasActualPath && hasGamePath)
            {
                var actualPath = actualPathElement.GetString();
                var gamePath = gamePathElement.GetString();
                
                // If ActualPath differs from GamePath, this file is modded
                if (!string.IsNullOrEmpty(actualPath) && !string.IsNullOrEmpty(gamePath) && 
                    !actualPath.Equals(gamePath, StringComparison.OrdinalIgnoreCase))
                {
                    // Extract mod directory from the actual path
                    var modDir = ExtractModDirectoryFromPath(actualPath);
                    if (!string.IsNullOrEmpty(modDir))
                    {
                        modDirectories.Add(modDir);
                    }
                }
                else if (!string.IsNullOrEmpty(actualPath) && string.IsNullOrEmpty(gamePath))
                {
                    // Some nodes might not have GamePath but still be modded
                    var modDir = ExtractModDirectoryFromPath(actualPath);
                    if (!string.IsNullOrEmpty(modDir))
                    {
                        modDirectories.Add(modDir);
                    }
                }
            }
            else if (hasActualPath)
            {
                var actualPath = actualPathElement.GetString();
                log.Information($"Node with only ActualPath: {actualPath}");
                
                // Try to extract mod directory anyway
                if (!string.IsNullOrEmpty(actualPath))
                {
                    var modDir = ExtractModDirectoryFromPath(actualPath);
                    if (!string.IsNullOrEmpty(modDir))
                    {
                        modDirectories.Add(modDir);
                    }
                }
            }
            
            // Process child nodes recursively
            if (node.TryGetProperty("Children", out var childrenElement) && childrenElement.ValueKind == JsonValueKind.Array)
            {
                var childCount = 0;
                foreach (var child in childrenElement.EnumerateArray())
                {
                    childCount++;
                }
                log.Information($"Processing {childCount} child nodes");
                
                foreach (var child in childrenElement.EnumerateArray())
                {
                    ExtractModDirectoriesFromNode(child, modDirectories);
                }
            }
        }
        
        /// <summary>
        /// Extract the mod directory name from a full file path
        /// This handles both mod file paths and potentially other path formats from Penumbra
        /// </summary>
        private string? ExtractModDirectoryFromPath(string actualPath)
        {
            try
            {
                if (string.IsNullOrEmpty(actualPath))
                    return null;
                
                
                // Method 1: Standard mod path structure
                // "C:\path\to\penumbra\mods\ModDirectoryName\subpath\file.ext"
                if (actualPath.Contains("mods", StringComparison.OrdinalIgnoreCase))
                {
                    var pathParts = actualPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                    log.Information($"Path parts: [{string.Join(", ", pathParts)}]");
                    
                    for (int i = 0; i < pathParts.Length - 1; i++)
                    {
                        if (pathParts[i].Equals("mods", StringComparison.OrdinalIgnoreCase))
                        {
                            // The next part should be the mod directory name
                            if (i + 1 < pathParts.Length)
                            {
                                var modDir = pathParts[i + 1];
                                log.Information($"Extracted mod directory via 'mods' path: {modDir}");
                                return modDir;
                            }
                        }
                    }
                }
                
                // Method 2: Check if this path looks like it's from a specific Penumbra mod structure
                // Sometimes paths might be relative or have different formats
                if (actualPath.Contains("\\") || actualPath.Contains("/"))
                {
                    var pathParts = actualPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    // Look for common mod directory patterns
                    // If we have a path with multiple parts, the first or second part might be the mod name
                    if (pathParts.Length >= 2)
                    {
                        // Look for FFXIVPenumbra specifically, then get the next directory
                        for (int i = 0; i < pathParts.Length - 1; i++)
                        {
                            if (pathParts[i].Contains("Penumbra", StringComparison.OrdinalIgnoreCase))
                            {
                                // The next part should be the mod directory name
                                if (i + 1 < pathParts.Length)
                                {
                                    var modDir = pathParts[i + 1];
                                    // Make sure it's not a game path like "chara"
                                    if (!modDir.Equals("chara", StringComparison.OrdinalIgnoreCase) &&
                                        !modDir.Equals("common", StringComparison.OrdinalIgnoreCase) &&
                                        !modDir.Equals("shader", StringComparison.OrdinalIgnoreCase))
                                    {
                                        log.Information($"Extracted mod directory via Penumbra path: {modDir}");
                                        return modDir;
                                    }
                                }
                            }
                        }
                        
                        // Fallback: Skip drive letters and system paths, find first non-system directory
                        var startIndex = 0;
                        // Skip drive letter if present (like "F:")
                        if (pathParts[0].Length == 2 && pathParts[0].EndsWith(":"))
                        {
                            startIndex = 1;
                        }
                        
                        // Find first directory that looks like a mod name
                        for (int i = startIndex; i < pathParts.Length; i++)
                        {
                            var part = pathParts[i];
                            // Skip system directories
                            if (part.Contains("Program Files", StringComparison.OrdinalIgnoreCase) ||
                                part.Contains("Windows", StringComparison.OrdinalIgnoreCase) ||
                                part.Contains("Users", StringComparison.OrdinalIgnoreCase) ||
                                part.Contains("AppData", StringComparison.OrdinalIgnoreCase) ||
                                part.Contains("Penumbra", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            
                            // Skip game directories
                            if (part.Equals("chara", StringComparison.OrdinalIgnoreCase) ||
                                part.Equals("common", StringComparison.OrdinalIgnoreCase) ||
                                part.Equals("shader", StringComparison.OrdinalIgnoreCase))
                            {
                                break; // We've reached game content, no mod found
                            }
                            
                            // This looks like a mod directory
                            if (!part.Contains("."))
                            {
                                log.Information($"Extracted mod directory via fallback analysis: {part}");
                                return part;
                            }
                        }
                    }
                }
                
                // Method 3: If all else fails, and the path is short, it might be a mod directory itself
                if (!actualPath.Contains(".") && !actualPath.Contains("\\") && !actualPath.Contains("/"))
                {
                    log.Information($"Path appears to be mod directory itself: {actualPath}");
                    return actualPath;
                }
                
                log.Information($"Could not extract mod directory from path: {actualPath}");
                return null;
            }
            catch (Exception ex)
            {
                log.Debug($"Error extracting mod directory from path {actualPath}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Get all mod settings for a collection using various fallback methods
        /// </summary>
        public Dictionary<string, (bool, int, Dictionary<string, List<string>>, bool, bool)>? GetAllModSettingsRobust(Guid collectionId)
        {
            if (!IsPenumbraAvailable)
                return null;

            var methodsToTry = new[]
            {
                "Penumbra.GetAllModSettings",
                "Penumbra.GetAllModSettings.V5", // In case user has older version
            };

            foreach (var method in methodsToTry)
            {
                try
                {
                    log.Debug($"Trying IPC method: {method}");
                    var ipc = pluginInterface.GetIpcSubscriber<Guid, bool, bool, int, (int, Dictionary<string, (bool, int, Dictionary<string, List<string>>, bool, bool)>?)>(method);
                    var result = ipc?.InvokeFunc(collectionId, false, false, 0);
                    
                    if (result?.Item1 == 0 && result?.Item2 != null) // 0 = Success
                    {
                        log.Information($"Successfully got mod settings using method: {method}");
                        return result.Value.Item2;
                    }
                    else if (result?.Item1 != 0)
                    {
                        log.Warning($"Method {method} returned error code: {result?.Item1}");
                    }
                }
                catch (Dalamud.Plugin.Ipc.Exceptions.IpcNotReadyError ex)
                {
                    log.Debug($"IPC method {method} not ready: {ex.Message}");
                }
                catch (Exception ex)
                {
                    log.Error($"Error calling IPC method {method}: {ex.Message}");
                }
            }

            log.Warning("All IPC methods for GetAllModSettings failed");
            return null;
        }

        /// <summary>
        /// Get mod description, tags, and metadata
        /// </summary>
        public (string description, List<string> modTags, List<string> localTags, string author, string version, string website) GetModMetadata(string modDirectory, string modName)
        {
            if (!IsPenumbraAvailable)
                return (string.Empty, new List<string>(), new List<string>(), string.Empty, string.Empty, string.Empty);
            
            try
            {
                // This would need a new Penumbra API method to get mod metadata
                // For now, return empty data as placeholder
                log.Debug($"Getting metadata for mod: {modName} ({modDirectory})");
                return (string.Empty, new List<string>(), new List<string>(), string.Empty, string.Empty, string.Empty);
            }
            catch (Exception ex)
            {
                log.Error($"Error getting mod metadata for {modName}: {ex}");
                return (string.Empty, new List<string>(), new List<string>(), string.Empty, string.Empty, string.Empty);
            }
        }
        
        /// <summary>
        /// Get mod option groups with raw data including group types
        /// </summary>
        public IReadOnlyDictionary<string, (string[], int)> GetModOptionsRaw(string modDirectory, string modName)
        {
            if (!IsPenumbraAvailable)
            {
                log.Debug($"Penumbra not available for GetModOptionsRaw({modName})");
                return new Dictionary<string, (string[], int)>();
            }
            
            try
            {
                var ipc = pluginInterface.GetIpcSubscriber<string, string, IReadOnlyDictionary<string, (string[], int)>?>("Penumbra.GetAvailableModSettings.V5");
                var result = ipc.InvokeFunc(modDirectory, modName);
                
                return result ?? new Dictionary<string, (string[], int)>();
            }
            catch (Exception ex)
            {
                log.Debug($"Error getting raw mod options for {modName}: {ex.Message}");
                return new Dictionary<string, (string[], int)>();
            }
        }

        /// <summary>
        /// Get mod option groups and their option names
        /// </summary>
        public Dictionary<string, List<string>> GetModOptions(string modDirectory, string modName)
        {
            if (!IsPenumbraAvailable)
            {
                log.Debug($"Penumbra not available for GetModOptions({modName})");
                return new Dictionary<string, List<string>>();
            }
            
            try
            {
                var ipc = pluginInterface.GetIpcSubscriber<string, string, IReadOnlyDictionary<string, (string[], int)>?>("Penumbra.GetAvailableModSettings.V5");
                var result = ipc.InvokeFunc(modDirectory, modName);
                
                
                if (result != null)
                {
                    var options = new Dictionary<string, List<string>>();
                    
                    foreach (var (groupName, (optionNames, groupType)) in result)
                    {
                        options[groupName] = optionNames.ToList();
                    }
                    
                    return options;
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
                log.Debug($"Error getting mod options for {modName}: {ex.Message}");
            }
            
            return new Dictionary<string, List<string>>();
        }
        
        /// <summary>
        /// Get changed items with proper resource names and icon types
        /// This is the key method for accurate mod categorization
        /// </summary>
        public List<(string name, string iconType, string resourceType, string gamePath)> GetChangedItemsWithDetails(string modDirectory, string modName)
        {
            var items = new List<(string name, string iconType, string resourceType, string gamePath)>();
            
            if (!IsPenumbraAvailable)
                return items;
            
            try
            {
                // First get the basic changed items
                var changedItems = GetModChangedItems(modDirectory, modName);
                if (changedItems == null || !changedItems.Any())
                    return items;
                
                // For each changed item, try to get more detailed information
                foreach (var (path, itemData) in changedItems)
                {
                    var name = "Unknown";
                    var iconType = "Unknown";
                    var resourceType = "Unknown";
                    
                    // Try to extract information from the itemData if it's available
                    // The exact structure depends on what Penumbra provides
                    if (itemData != null)
                    {
                        // This might need adjustment based on actual Penumbra API structure
                        var dataStr = itemData.ToString() ?? "";
                        if (!string.IsNullOrEmpty(dataStr))
                        {
                            name = dataStr;
                        }
                    }
                    
                    // Analyze the path to determine type
                    iconType = AnalyzePathForIconType(path);
                    resourceType = AnalyzePathForResourceType(path);
                    
                    items.Add((name, iconType, resourceType, path));
                }
                
                log.Debug($"Found {items.Count} detailed changed items for mod {modName}");
            }
            catch (Exception ex)
            {
                log.Error($"Error getting detailed changed items for {modName}: {ex}");
            }
            
            return items;
        }
        
        /// <summary>
        /// Analyze game path to determine icon type (equipment slot, emote, etc.)
        /// </summary>
        private string AnalyzePathForIconType(string path)
        {
            var pathLower = path.ToLowerInvariant();
            
            // Equipment patterns from Penumbra
            if (pathLower.Contains("chara/equipment/"))
            {
                if (pathLower.Contains("_hed")) return "Head";
                if (pathLower.Contains("_top") || pathLower.Contains("_met")) return "Body";
                if (pathLower.Contains("_glv")) return "Hands";
                if (pathLower.Contains("_dwn") || pathLower.Contains("_leg")) return "Legs";
                if (pathLower.Contains("_sho")) return "Feet";
                return "Equipment";
            }
            
            // Accessory patterns
            if (pathLower.Contains("chara/accessory/"))
            {
                if (pathLower.Contains("_ear")) return "Ears";
                if (pathLower.Contains("_nek")) return "Neck";
                if (pathLower.Contains("_wrs")) return "Wrists";
                if (pathLower.Contains("_rir") || pathLower.Contains("_ril")) return "Finger";
                return "Accessory";
            }
            
            // Weapon patterns
            if (pathLower.Contains("chara/weapon/"))
            {
                if (pathLower.Contains("_s.")) return "Offhand";
                return "Mainhand";
            }
            
            // Character customization patterns
            if (pathLower.Contains("chara/human/"))
            {
                if (pathLower.Contains("/hair/")) return "Hair";
                if (pathLower.Contains("/face/")) return "Face";
                if (pathLower.Contains("/body/")) return "Body";
                if (pathLower.Contains("/tail/")) return "Tail";
                return "Customization";
            }
            
            // Animation patterns
            if (pathLower.Contains("/emote/") || pathLower.Contains("_emt."))
                return "Emote";
            if (pathLower.Contains("/action/") || pathLower.Contains("/animation/"))
                return "Action";
            
            // VFX patterns
            if (pathLower.Contains("/vfx/") || pathLower.Contains(".avfx"))
                return "VFX";
            
            return "Unknown";
        }
        
        /// <summary>
        /// Analyze game path to determine resource type
        /// </summary>
        private string AnalyzePathForResourceType(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            
            return extension switch
            {
                ".mdl" => "Model",
                ".tex" => "Texture", 
                ".mtrl" => "Material",
                ".avfx" => "VFX",
                ".pap" => "Animation",
                ".tmb" => "Timeline",
                ".sklb" => "Skeleton",
                ".shpk" => "Shader",
                ".imc" => "MetaData",
                _ => "Other"
            };
        }
        
        /// <summary>
        /// Get current mod settings for a specific mod
        /// </summary>
        public (bool success, bool enabled, int priority, Dictionary<string, List<string>> options) GetCurrentModSettings(Guid collectionId, string modDirectory, string modName)
        {
            if (!IsPenumbraAvailable)
                return (false, false, 0, new Dictionary<string, List<string>>());
            
            try
            {
                var ipc = pluginInterface.GetIpcSubscriber<Guid, string, string, bool, (int, (bool, int, Dictionary<string, List<string>>, bool)?)>("Penumbra.GetCurrentModSettings.V5");
                var result = ipc.InvokeFunc(collectionId, modDirectory, modName, false);
                
                if (result.Item1 == 0) // 0 = Success
                {
                    if (result.Item2 != null)
                    {
                        var (enabled, priority, options, _) = result.Item2.Value;
                        return (true, enabled, priority, options);
                    }
                    else
                    {
                        // Success but no settings configured - return default state
                        log.Debug($"GetCurrentModSettings: mod {modName} exists but has no settings configured");
                        return (true, false, 0, new Dictionary<string, List<string>>());
                    }
                }
                
                log.Warning($"GetCurrentModSettings failed with error code: {result.Item1}");
                return (false, false, 0, new Dictionary<string, List<string>>());
            }
            catch (Exception ex)
            {
                log.Error($"Error getting current mod settings for {modName}: {ex}");
                return (false, false, 0, new Dictionary<string, List<string>>());
            }
        }
        
        /// <summary>
        /// Set multiple options for a mod option group
        /// </summary>
        public bool TrySetModSettings(Guid collectionId, string modDirectory, string modName, string optionGroupName, List<string> optionNames)
        {
            if (!IsPenumbraAvailable)
                return false;

            try
            {
                var ipc = pluginInterface.GetIpcSubscriber<Guid, string, string, string, IReadOnlyList<string>, int>("Penumbra.TrySetModSettings.V5");
                var result = ipc.InvokeFunc(collectionId, modDirectory, modName, optionGroupName, optionNames);

                if (result == 0) // 0 = Success
                {
                    return true;
                }
                else if (result == 1) // 1 = NothingChanged (already in correct state)
                {
                    log.Debug($"TrySetModSettings - no change needed for {modName}.{optionGroupName} (already in correct state)");
                    return true; // Treat as success since options are already correct
                }

                log.Warning($"TrySetModSettings failed with error code: {result}");
                return false;
            }
            catch (Exception ex)
            {
                log.Error($"Error setting mod options for {modName}.{optionGroupName}: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Set mod inheritance state. When inherit=true, removes explicit settings and uses parent collection's.
        /// </summary>
        public bool TryInheritMod(Guid collectionId, string modDirectory, string modName, bool inherit)
        {
            if (!IsPenumbraAvailable)
                return false;

            try
            {
                var ipc = pluginInterface.GetIpcSubscriber<Guid, string, string, bool, int>("Penumbra.TryInheritMod");
                var result = ipc.InvokeFunc(collectionId, modDirectory, modName, inherit);

                if (result == 0) // Success
                {
                    log.Debug($"TryInheritMod - set inherit={inherit} for {modName}");
                    return true;
                }
                else if (result == 1) // NothingChanged
                {
                    log.Debug($"TryInheritMod - no change needed for {modName} (already in correct state)");
                    return true;
                }

                log.Warning($"TryInheritMod failed with error code: {result}");
                return false;
            }
            catch (Exception ex)
            {
                log.Error($"Error setting mod inheritance for {modName}: {ex}");
                return false;
            }
        }
        
        /// <summary>
        /// Apply all mod option settings for a design
        /// </summary>
        public async Task<bool> ApplyModOptionsForDesign(Guid collectionId, Dictionary<string, Dictionary<string, List<string>>> modOptions)
        {
            if (!IsPenumbraAvailable || modOptions == null)
                return false;
            
            var allSucceeded = true;
            
            foreach (var (modDirectory, groupSettings) in modOptions)
            {
                // Get mod name from directory
                var modList = GetModList();
                if (!modList.TryGetValue(modDirectory, out var modName))
                {
                    log.Warning($"Could not find mod name for directory: {modDirectory}");
                    continue;
                }
                
                foreach (var (groupName, options) in groupSettings)
                {
                    var success = TrySetModSettings(collectionId, modDirectory, modName, groupName, options);
                    if (!success)
                    {
                        allSucceeded = false;
                        log.Warning($"Failed to apply options for {modName}.{groupName}");
                    }
                }
                
                // Small delay to avoid overwhelming Penumbra
                await Task.Delay(10);
            }
            
            return allSucceeded;
        }
        
        /// <summary>
        /// Capture current mod option settings for all enabled mods
        /// </summary>
        public Dictionary<string, Dictionary<string, List<string>>> CaptureCurrentModOptions(Guid collectionId, IEnumerable<string> modDirectories)
        {
            var result = new Dictionary<string, Dictionary<string, List<string>>>();
            
            if (!IsPenumbraAvailable)
                return result;
            
            var modList = GetModList();
            
            foreach (var modDirectory in modDirectories)
            {
                if (!modList.TryGetValue(modDirectory, out var modName))
                    continue;
                
                var (success, _, _, options) = GetCurrentModSettings(collectionId, modDirectory, modName);
                if (success && options.Any())
                {
                    result[modDirectory] = options;
                }
            }
            
            log.Debug($"Captured mod options for {result.Count} mods");
            return result;
        }

        /// <summary>
        /// Analyze a mod for dependencies and conflicts with selected mods
        /// </summary>
        public ModConflictAnalysisResult AnalyzeModForDependenciesAndConflicts(string modDirectory, string modName, CharacterSelectPlugin.Windows.ModType modType, Dictionary<string, bool> selectedMods)
        {
            var result = new ModConflictAnalysisResult();
            
            // Get changed items for this mod
            var changedItems = GetModChangedItems(modDirectory, modName);
            if (!changedItems.Any())
                return result;
            
            // Check for dependencies (only for Gear mods)
            if (modType == CharacterSelectPlugin.Windows.ModType.Gear)
            {
                var models = changedItems.Keys.Where(path => path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase));
                var textures = changedItems.Keys.Where(path => path.EndsWith(".tex", StringComparison.OrdinalIgnoreCase));
                
                bool hasModels = models.Any();
                bool hasTextures = textures.Any();
                
                if (hasModels && !hasTextures)
                {
                    result.HasDependency = true;
                    result.DependencyType = "Model-only mod - requires original texture mod to work properly";
                }
                else if (hasTextures && !hasModels)
                {
                    result.HasDependency = true;
                    result.DependencyType = "Texture-only mod - requires original model mod to work properly";
                }
            }
            
            // Check for conflicts with other selected mods
            var conflictingMods = new List<string>();
            
            foreach (var selectedMod in selectedMods.Where(kvp => kvp.Value && kvp.Key != modDirectory))
            {
                var otherModChangedItems = GetModChangedItems(selectedMod.Key, "");
                
                // Find overlapping paths
                var overlappingPaths = changedItems.Keys.Intersect(otherModChangedItems.Keys).ToList();
                
                // For hair mods, filter out skin texture conflicts
                if (modType == CharacterSelectPlugin.Windows.ModType.Hair)
                {
                    // Check if the other mod is also a hair mod by examining its changed items
                    var isOtherModHair = IsHairMod(otherModChangedItems.Keys);
                    
                    if (isOtherModHair)
                    {
                        // Both are hair mods - filter out skin texture conflicts
                        overlappingPaths = overlappingPaths.Where(path => !IsSkinTextureConflict(path)).ToList();
                    }
                }
                
                if (overlappingPaths.Any())
                {
                    conflictingMods.Add(selectedMod.Key);
                    result.ConflictingPaths.AddRange(overlappingPaths);
                }
            }
            
            if (conflictingMods.Any())
            {
                result.HasConflicts = true;
                result.ConflictingMods = conflictingMods;
            }
            
            return result;
        }
        
        /// <summary>
        /// Check if a mod is a hair mod based on its changed items
        /// </summary>
        private bool IsHairMod(IEnumerable<string> changedItems)
        {
            foreach (var item in changedItems)
            {
                // Check for hair-related customization items
                if (item.Contains("Hair", StringComparison.OrdinalIgnoreCase) && 
                    item.Contains("Customization:", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                
                // Check for hair file paths
                if (item.Contains("/hair/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Check if a conflicting path is a skin texture that should be ignored for hair mods
        /// </summary>
        private bool IsSkinTextureConflict(string path)
        {
            // Check for skin texture customization items
            if (path.Contains("Customization:", StringComparison.OrdinalIgnoreCase) && 
                path.Contains("Skin Textures", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            
            // Could add more specific skin texture patterns here if needed
            // For example: face textures, body textures that are commonly shared by hair mods
            
            return false;
        }
        
        
        
        public void Dispose()
        {
            // Dispose event subscriptions
            try
            {
                modAddedSubscriber?.Dispose();
                modDeletedSubscriber?.Dispose();
                modMovedSubscriber?.Dispose();
            }
            catch (Exception ex)
            {
                log.Warning($"Error during event subscription disposal: {ex.Message}");
            }
            
            // Dispose of IPC subscribers if needed
            penumbraApiVersion = null;
            modAddedSubscriber = null;
            modDeletedSubscriber = null;
            modMovedSubscriber = null;
        }

    }
    
    /// <summary>
    /// Result of analyzing a mod for dependencies and conflicts
    /// </summary>

    public class ModConflictAnalysisResult
    {
        public bool HasDependency { get; set; }
        public string DependencyType { get; set; } = "";
        
        public bool HasConflicts { get; set; }
        public List<string> ConflictingMods { get; set; } = new();
        public List<string> ConflictingPaths { get; set; } = new();
    }
}
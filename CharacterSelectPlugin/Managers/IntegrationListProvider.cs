using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Plugin.Ipc;

namespace CharacterSelectPlugin.Managers
{
    /// <summary>
    /// Provides cached lists of available options from integrated plugins.
    /// Used to populate autocomplete dropdowns in character/design forms.
    /// </summary>
    public class IntegrationListProvider : IDisposable
    {
        private readonly Plugin plugin;

        // IPC Subscribers
        private ICallGateSubscriber<Dictionary<Guid, string>>? penumbraGetCollectionsIpc;
        private ICallGateSubscriber<Dictionary<Guid, string>>? glamourerGetDesignsIpc;
        private ICallGateSubscriber<Dictionary<Guid, (string, string, uint, bool)>>? glamourerGetDesignsExtendedIpc;
        private ICallGateSubscriber<IList<(Guid, string, string, IList<(string, ushort, byte, ushort)>, int, bool)>>? customizePlusGetProfileListIpc;
        private ICallGateSubscriber<List<(Guid, string)>>? moodlesGetPresetsIpc;
        private ICallGateSubscriber<string, uint, object[]>? honorificGetTitleListIpc;
        private readonly List<ICallGateSubscriber<nint, object?>> glamourerStateChangedIpcs = new();


        // Cached lists
        private List<string> cachedPenumbraCollections = new();
        private List<string> cachedGlamourerDesigns = new();
        private List<GlamourerDesignEntry> cachedGlamourerDesignsExtended = new();
        private List<string> cachedCustomizePlusProfiles = new();
        private List<string> cachedMoodlesPresets = new();
        private List<string> cachedHonorificTitles = new();

        // Cache timestamps
        private DateTime lastPenumbraRefresh = DateTime.MinValue;
        private DateTime lastGlamourerRefresh = DateTime.MinValue;
        private DateTime lastGlamourerExtendedRefresh = DateTime.MinValue;
        private DateTime lastCustomizePlusRefresh = DateTime.MinValue;
        private DateTime lastMoodlesRefresh = DateTime.MinValue;
        private DateTime lastHonorificRefresh = DateTime.MinValue;

        // Cache duration
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

        public IntegrationListProvider(Plugin plugin)
        {
            this.plugin = plugin;
            InitializeIpcSubscribers();
        }

        private void InitializeIpcSubscribers()
        {
            try
            {
                penumbraGetCollectionsIpc = Plugin.PluginInterface.GetIpcSubscriber<Dictionary<Guid, string>>("Penumbra.GetCollections.V5");
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"[IntegrationListProvider] Penumbra IPC not available: {ex.Message}");
            }

            try
            {
                glamourerGetDesignsIpc = Plugin.PluginInterface.GetIpcSubscriber<Dictionary<Guid, string>>("Glamourer.GetDesignList.V2");
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"[IntegrationListProvider] Glamourer IPC not available: {ex.Message}");
            }

            try
            {
                // Extended list also carries each design's folder path.
                glamourerGetDesignsExtendedIpc = Plugin.PluginInterface.GetIpcSubscriber<Dictionary<Guid, (string, string, uint, bool)>>("Glamourer.GetDesignListExtended");
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"[IntegrationListProvider] Glamourer extended IPC not available: {ex.Message}");
            }

            try
            {
                customizePlusGetProfileListIpc = Plugin.PluginInterface.GetIpcSubscriber<IList<(Guid, string, string, IList<(string, ushort, byte, ushort)>, int, bool)>>("CustomizePlus.Profile.GetList");
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"[IntegrationListProvider] Customize+ IPC not available: {ex.Message}");
            }

            try
            {
                // Moodles GetRegisteredProfilesV2 returns List<(Guid ID, string FullPath)>
                moodlesGetPresetsIpc = Plugin.PluginInterface.GetIpcSubscriber<List<(Guid, string)>>("Moodles.GetRegisteredProfilesV2");
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"[IntegrationListProvider] Moodles IPC not available: {ex.Message}");
            }

            try
            {
                // Honorific GetCharacterTitleList takes (string name, uint world) and returns TitleData[]
                // We'll store as object[] since TitleData is internal to Honorific
                honorificGetTitleListIpc = Plugin.PluginInterface.GetIpcSubscriber<string, uint, object[]>("Honorific.GetCharacterTitleList");
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"[IntegrationListProvider] Honorific IPC not available: {ex.Message}");
            }

            // old + current event names
            foreach (var label in new[] { "Glamourer.StateChanged", "Glamourer.StateChanged.V2", "Penumbra.StateChanged.V2" })
            {
                try
                {
                    var sub = Plugin.PluginInterface.GetIpcSubscriber<nint, object?>(label);
                    sub.Subscribe(OnGlamourerStateChanged);
                    glamourerStateChangedIpcs.Add(sub);
                }
                catch (Exception ex)
                {
                    Plugin.Log.Debug($"[IntegrationListProvider] {label} IPC not available: {ex.Message}");
                }
            }
        }

        private void OnGlamourerStateChanged(nint address)
        {
            try
            {
                var local = Plugin.ObjectTable.LocalPlayer;
                if (local != null && local.Address == address)
                    activeGlamourerDirty = true;
            }
            catch
            {
            }
        }

        public IReadOnlyList<string> GetPenumbraCollections(bool forceRefresh = false)
        {
            if (!forceRefresh && DateTime.Now - lastPenumbraRefresh < CacheDuration && cachedPenumbraCollections.Count > 0)
            {
                return cachedPenumbraCollections;
            }

            try
            {
                var collections = penumbraGetCollectionsIpc?.InvokeFunc();
                if (collections != null)
                {
                    cachedPenumbraCollections = collections.Values
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    lastPenumbraRefresh = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"[IntegrationListProvider] Failed to get Penumbra collections: {ex.Message}");
            }

            return cachedPenumbraCollections;
        }

        /// <summary>Gets available Glamourer designs.</summary>
        public IReadOnlyList<string> GetGlamourerDesigns(bool forceRefresh = false)
        {
            if (!forceRefresh && DateTime.Now - lastGlamourerRefresh < CacheDuration && cachedGlamourerDesigns.Count > 0)
            {
                return cachedGlamourerDesigns;
            }

            try
            {
                var designs = glamourerGetDesignsIpc?.InvokeFunc();
                if (designs != null)
                {
                    cachedGlamourerDesigns = designs.Values
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    lastGlamourerRefresh = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"[IntegrationListProvider] Failed to get Glamourer designs: {ex.Message}");
            }

            return cachedGlamourerDesigns;
        }

        // Designs with their folder path, falling back to a flat list when the
        // extended IPC is unavailable
        public IReadOnlyList<GlamourerDesignEntry> GetGlamourerDesignsExtended(bool forceRefresh = false)
        {
            if (!forceRefresh && DateTime.Now - lastGlamourerExtendedRefresh < CacheDuration && cachedGlamourerDesignsExtended.Count > 0)
            {
                return cachedGlamourerDesignsExtended;
            }

            try
            {
                var designs = glamourerGetDesignsExtendedIpc?.InvokeFunc();
                if (designs != null)
                {
                    var entries = new List<GlamourerDesignEntry>(designs.Count);
                    foreach (var kvp in designs)
                    {
                        // Item1 = display name, Item2 = full path (folder chain + design name as the leaf).
                        var fullPath = kvp.Value.Item2 ?? "";
                        var name = kvp.Value.Item1 ?? "";
                        var slash = fullPath.LastIndexOf('/');
                        if (string.IsNullOrEmpty(name))
                            name = slash >= 0 ? fullPath.Substring(slash + 1) : fullPath;
                        entries.Add(new GlamourerDesignEntry
                        {
                            Id = kvp.Key,
                            Name = name,
                            FolderPath = slash > 0 ? fullPath.Substring(0, slash) : "",
                        });
                    }
                    cachedGlamourerDesignsExtended = entries
                        .OrderBy(e => e.FolderPath, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    lastGlamourerExtendedRefresh = DateTime.Now;
                    return cachedGlamourerDesignsExtended;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"[IntegrationListProvider] Failed to get extended Glamourer designs: {ex.Message}");
            }

            // Flat entries from the plain name list, so import still works without folders
            if (cachedGlamourerDesignsExtended.Count == 0)
            {
                cachedGlamourerDesignsExtended = GetGlamourerDesigns(forceRefresh)
                    .Select(n => new GlamourerDesignEntry { Id = Guid.Empty, Name = n, FolderPath = "" })
                    .ToList();
            }
            return cachedGlamourerDesignsExtended;
        }

        public IReadOnlyList<(string Folder, List<GlamourerDesignEntry> Designs)> GetGlamourerDesignsGrouped(bool forceRefresh = false)
        {
            return GetGlamourerDesignsExtended(forceRefresh)
                .GroupBy(e => e.FolderPath)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => (g.Key, g.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList()))
                .ToList();
        }

        public IReadOnlyList<string> GetCustomizePlusProfiles(bool forceRefresh = false)
        {
            if (!forceRefresh && DateTime.Now - lastCustomizePlusRefresh < CacheDuration && cachedCustomizePlusProfiles.Count > 0)
            {
                return cachedCustomizePlusProfiles;
            }

            try
            {
                var profiles = customizePlusGetProfileListIpc?.InvokeFunc();
                if (profiles != null)
                {
                    // Profile tuple: (Guid id, string name, string characterName, IList<...> characters, int priority, bool enabled)
                    cachedCustomizePlusProfiles = profiles
                        .Select(p => p.Item2) // Item2 is the profile name
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct()
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    lastCustomizePlusRefresh = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"[IntegrationListProvider] Failed to get Customize+ profiles: {ex.Message}");
            }

            return cachedCustomizePlusProfiles;
        }

        /// <summary>Gets available Moodles presets.</summary>
        public IReadOnlyList<string> GetMoodlesPresets(bool forceRefresh = false)
        {
            if (!forceRefresh && DateTime.Now - lastMoodlesRefresh < CacheDuration && cachedMoodlesPresets.Count > 0)
            {
                return cachedMoodlesPresets;
            }

            try
            {
                var presets = moodlesGetPresetsIpc?.InvokeFunc();
                if (presets != null)
                {
                    // Preset tuple: (Guid ID, string FullPath)
                    // Use the full path as Moodles commands require it (e.g., "Chars/Male/Rayven/Rayven")
                    cachedMoodlesPresets = presets
                        .Select(p => p.Item2) // Use full path, not just the name
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct()
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    lastMoodlesRefresh = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"[IntegrationListProvider] Failed to get Moodles presets: {ex.Message}");
            }

            return cachedMoodlesPresets;
        }

        /// <summary>
        /// Gets available Honorific titles for the current character.
        /// Note: Honorific titles are per-character, not global.
        /// </summary>
        public IReadOnlyList<string> GetHonorificTitles(bool forceRefresh = false)
        {
            if (!forceRefresh && DateTime.Now - lastHonorificRefresh < CacheDuration && cachedHonorificTitles.Count > 0)
            {
                return cachedHonorificTitles;
            }

            try
            {
                var localPlayer = Plugin.ObjectTable?.LocalPlayer;
                if (localPlayer == null)
                {
                    return cachedHonorificTitles;
                }

                var name = localPlayer.Name.TextValue;
                var worldId = localPlayer.HomeWorld.RowId;

                var titles = honorificGetTitleListIpc?.InvokeFunc(name, worldId);
                if (titles != null)
                {
                    // TitleData has a Title property - we need to extract it via reflection or dynamic
                    cachedHonorificTitles = titles
                        .Select(t => ExtractTitleFromTitleData(t))
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct()
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    lastHonorificRefresh = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"[IntegrationListProvider] Failed to get Honorific titles: {ex.Message}");
            }

            return cachedHonorificTitles;
        }

        /// <summary>Gets the currently active Penumbra collection for the local player.</summary>
        public string? GetCurrentPenumbraCollection()
        {
            try
            {
                var result = plugin.PenumbraIntegration?.GetCurrentCollection();
                if (result.HasValue && result.Value.success && !string.IsNullOrEmpty(result.Value.collectionName))
                {
                    return result.Value.collectionName;
                }
            }
            catch
            {
                // Silently fail - this is called frequently during UI rendering
            }
            return null;
        }

        // Active Glamourer design for the local player, cached
        private string? cachedActiveGlamourerDesign;
        private DateTime lastActiveGlamourerRefresh = DateTime.MinValue;
        private bool activeGlamourerRefreshRunning;
        private bool activeGlamourerDirty;

        public string? GetCurrentGlamourerDesign()
        {
            double age = (DateTime.Now - lastActiveGlamourerRefresh).TotalSeconds;
            if ((age > 10 || (activeGlamourerDirty && age > 1)) && !activeGlamourerRefreshRunning)
            {
                activeGlamourerDirty = false;
                activeGlamourerRefreshRunning = true;
                Task.Run(async () =>
                {
                    try
                    {
                        var candidates = await GlamourerDesignMatcher.FindApplied();
                        cachedActiveGlamourerDesign = candidates.FirstOrDefault(c => c.Score >= 0.999f).Name;
                    }
                    catch
                    {
                        cachedActiveGlamourerDesign = null;
                    }
                    finally
                    {
                        lastActiveGlamourerRefresh = DateTime.Now;
                        activeGlamourerRefreshRunning = false;
                    }
                });
            }
            return cachedActiveGlamourerDesign;
        }

        // Active Customize+ profile name for the local player
        public string? GetCurrentCustomizePlusProfile()
        {
            try
            {
                var profileName = plugin.GetCurrentCustomizePlusProfileName();
                if (!string.IsNullOrEmpty(profileName))
                {
                    return profileName;
                }
            }
            catch
            {
                // Silently fail - this is called frequently during UI rendering
            }
            return null;
        }

        /// <summary>Extracts title string from Honorific TitleData object.</summary>
        private static string ExtractTitleFromTitleData(object titleData)
        {
            if (titleData == null)
                return "";

            try
            {
                // Try to get Title property via reflection
                var titleProperty = titleData.GetType().GetProperty("Title");
                if (titleProperty != null)
                {
                    return titleProperty.GetValue(titleData)?.ToString() ?? "";
                }

                // Try as a field
                var titleField = titleData.GetType().GetField("Title");
                if (titleField != null)
                {
                    return titleField.GetValue(titleData)?.ToString() ?? "";
                }
            }
            catch
            {
                // Silently fail
            }

            return "";
        }

        /// <summary>Forces refresh of all caches.</summary>
        public void RefreshAll()
        {
            GetPenumbraCollections(true);
            GetGlamourerDesigns(true);
            GetGlamourerDesignsExtended(true);
            GetCustomizePlusProfiles(true);
            GetMoodlesPresets(true);
            GetHonorificTitles(true);
        }

        /// <summary>Clears all caches.</summary>
        public void ClearCaches()
        {
            cachedPenumbraCollections.Clear();
            cachedGlamourerDesigns.Clear();
            cachedGlamourerDesignsExtended.Clear();
            cachedCustomizePlusProfiles.Clear();
            cachedMoodlesPresets.Clear();
            cachedHonorificTitles.Clear();

            lastPenumbraRefresh = DateTime.MinValue;
            lastGlamourerRefresh = DateTime.MinValue;
            lastGlamourerExtendedRefresh = DateTime.MinValue;
            lastCustomizePlusRefresh = DateTime.MinValue;
            lastMoodlesRefresh = DateTime.MinValue;
            lastHonorificRefresh = DateTime.MinValue;
        }

        public void Dispose()
        {
            foreach (var sub in glamourerStateChangedIpcs)
                sub.Unsubscribe(OnGlamourerStateChanged);
            ClearCaches();
        }
    }

    // A Glamourer design plus the folder it lives in
    public sealed class GlamourerDesignEntry
    {
        public Guid Id;
        public string Name = "";
        public string FolderPath = ""; // parent folder, empty for root-level designs
    }
}

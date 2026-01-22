using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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
        private ICallGateSubscriber<IList<(Guid, string, string, IList<(string, ushort, byte, ushort)>, int, bool)>>? customizePlusGetProfileListIpc;
        private ICallGateSubscriber<List<(Guid, string)>>? moodlesGetPresetsIpc;
        private ICallGateSubscriber<string, uint, object[]>? honorificGetTitleListIpc;


        // Cached lists
        private List<string> cachedPenumbraCollections = new();
        private List<string> cachedGlamourerDesigns = new();
        private List<string> cachedCustomizePlusProfiles = new();
        private List<string> cachedMoodlesPresets = new();
        private List<string> cachedHonorificTitles = new();

        // Cache timestamps
        private DateTime lastPenumbraRefresh = DateTime.MinValue;
        private DateTime lastGlamourerRefresh = DateTime.MinValue;
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

        }

        /// <summary>Gets available Penumbra collections.</summary>
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

        /// <summary>Gets available Customize+ profiles.</summary>
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
                    // FullPath includes folder structure, extract just the name
                    cachedMoodlesPresets = presets
                        .Select(p => ExtractPresetName(p.Item2))
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
                var localPlayer = Plugin.ClientState?.LocalPlayer;
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

        /// <summary>Gets the currently active Customize+ profile name for the local player.</summary>
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

        /// <summary>Extracts preset name from full path (e.g., "Folder/SubFolder/PresetName" -> "PresetName")</summary>
        private static string ExtractPresetName(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return fullPath;

            // Get the last segment after any path separator
            var lastSlash = fullPath.LastIndexOfAny(new[] { '/', '\\' });
            return lastSlash >= 0 ? fullPath.Substring(lastSlash + 1) : fullPath;
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
            GetCustomizePlusProfiles(true);
            GetMoodlesPresets(true);
            GetHonorificTitles(true);
        }

        /// <summary>Clears all caches.</summary>
        public void ClearCaches()
        {
            cachedPenumbraCollections.Clear();
            cachedGlamourerDesigns.Clear();
            cachedCustomizePlusProfiles.Clear();
            cachedMoodlesPresets.Clear();
            cachedHonorificTitles.Clear();

            lastPenumbraRefresh = DateTime.MinValue;
            lastGlamourerRefresh = DateTime.MinValue;
            lastCustomizePlusRefresh = DateTime.MinValue;
            lastMoodlesRefresh = DateTime.MinValue;
            lastHonorificRefresh = DateTime.MinValue;
        }

        public void Dispose()
        {
            ClearCaches();
        }
    }
}

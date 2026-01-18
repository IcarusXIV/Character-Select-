using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CharacterSelectPlugin
{
    /// <summary>
    /// Manages fetching and caching CS+ names for other players.
    /// Used for shared name replacement feature.
    /// </summary>
    public class SharedNameManager : IDisposable
    {
        private readonly Plugin plugin;
        private readonly IPluginLog log;
        private readonly HttpClient httpClient;

        private readonly Dictionary<string, SharedNameEntry?> nameCache = new();
        private readonly object cacheLock = new();
        private readonly HashSet<string> pendingLookups = new();
        private readonly object pendingLock = new();

        private DateTime lastLookupTime = DateTime.MinValue;
        private static readonly TimeSpan LookupCooldown = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan PositiveRefreshInterval = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan NegativeRefreshInterval = TimeSpan.FromSeconds(60);

        private const string ApiBaseUrl = "https://character-select-profile-server-production.up.railway.app";
        private const int MaxBatchSize = 20;

        public class SharedNameEntry
        {
            public string CSName { get; set; } = "";
            public Vector3 NameplateColor { get; set; } = Vector3.One;
            public DateTime CachedAt { get; set; }
            public bool IsNegativeCache { get; set; } // True if we know there's no profile
        }

        public SharedNameManager(Plugin plugin, IPluginLog log)
        {
            this.plugin = plugin;
            this.log = log;
            this.httpClient = new HttpClient();
            this.httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        /// <summary>Queues a character for name lookup. Re-queues after refresh interval.</summary>
        public void QueueLookup(string physicalCharacterName)
        {
            if (string.IsNullOrEmpty(physicalCharacterName))
                return;

            lock (cacheLock)
            {
                if (nameCache.TryGetValue(physicalCharacterName, out var entry))
                {
                    if (entry != null)
                    {
                        var age = DateTime.Now - entry.CachedAt;
                        var maxDuration = entry.IsNegativeCache ? NegativeCacheDuration : CacheDuration;
                        var refreshInterval = entry.IsNegativeCache ? NegativeRefreshInterval : PositiveRefreshInterval;

                        if (age < maxDuration && age < refreshInterval)
                            return;
                    }
                }
            }

            lock (pendingLock)
            {
                pendingLookups.Add(physicalCharacterName);
            }
        }

        /// <summary>Gets a cached CS+ name. Returns null if not found or user is blocked.</summary>
        public SharedNameEntry? GetCachedName(string physicalCharacterName)
        {
            if (string.IsNullOrEmpty(physicalCharacterName))
                return null;

            if (plugin.Configuration.BlockedCSUsers.Contains(physicalCharacterName))
                return null;

            lock (cacheLock)
            {
                if (nameCache.TryGetValue(physicalCharacterName, out var entry))
                {
                    if (entry == null)
                        return null;

                    var duration = entry.IsNegativeCache ? NegativeCacheDuration : CacheDuration;
                    if (DateTime.Now - entry.CachedAt > duration)
                    {
                        nameCache.Remove(physicalCharacterName);
                        return null;
                    }

                    if (entry.IsNegativeCache)
                        return null;

                    return entry;
                }
            }

            return null;
        }

        /// <summary>Gets a cached CS+ name by character name only (without world).</summary>
        public SharedNameEntry? GetCachedNameByCharacterName(string characterName)
        {
            if (string.IsNullOrEmpty(characterName))
                return null;

            lock (cacheLock)
            {
                foreach (var kvp in nameCache)
                {
                    var atIndex = kvp.Key.IndexOf('@');
                    if (atIndex <= 0)
                        continue;

                    var cachedCharName = kvp.Key.Substring(0, atIndex);
                    if (cachedCharName.Equals(characterName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (plugin.Configuration.BlockedCSUsers.Contains(kvp.Key))
                            continue;

                        var entry = kvp.Value;
                        if (entry == null || entry.IsNegativeCache)
                            continue;

                        var duration = entry.IsNegativeCache ? NegativeCacheDuration : CacheDuration;
                        if (DateTime.Now - entry.CachedAt > duration)
                            continue;

                        return entry;
                    }
                }
            }

            return null;
        }

        /// <summary>Searches text for any cached character name and returns the match.</summary>
        public (SharedNameEntry entry, string originalName)? FindCachedNameInText(string text, string? excludeName = null)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            lock (cacheLock)
            {
                foreach (var kvp in nameCache)
                {
                    var atIndex = kvp.Key.IndexOf('@');
                    if (atIndex <= 0)
                        continue;

                    var cachedCharName = kvp.Key.Substring(0, atIndex);

                    if (!string.IsNullOrEmpty(excludeName) &&
                        cachedCharName.Equals(excludeName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (plugin.Configuration.BlockedCSUsers.Contains(kvp.Key))
                        continue;

                    if (text.Contains(cachedCharName, StringComparison.OrdinalIgnoreCase))
                    {
                        var entry = kvp.Value;
                        if (entry == null || entry.IsNegativeCache)
                            continue;

                        var duration = entry.IsNegativeCache ? NegativeCacheDuration : CacheDuration;
                        if (DateTime.Now - entry.CachedAt > duration)
                            continue;

                        return (entry, cachedCharName);
                    }
                }
            }

            return null;
        }

        /// <summary>Checks if name is a full match (not substring of another word).</summary>
        private bool ContainsFullName(string text, string name)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(name))
                return false;

            var index = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;

            if (index > 0)
            {
                var charBefore = text[index - 1];
                if (char.IsLetter(charBefore))
                    return false;
            }

            var endIndex = index + name.Length;
            if (endIndex < text.Length)
            {
                var charAfter = text[endIndex];
                if (char.IsLetter(charAfter))
                    return false;
            }

            return true;
        }

        /// <summary>Process pending lookups. Call periodically.</summary>
        public async Task ProcessPendingLookups()
        {
            if (DateTime.Now - lastLookupTime < LookupCooldown)
                return;

            List<string> batch;
            lock (pendingLock)
            {
                if (pendingLookups.Count == 0)
                    return;

                batch = new List<string>(pendingLookups);
                if (batch.Count > MaxBatchSize)
                    batch = batch.GetRange(0, MaxBatchSize);

                foreach (var name in batch)
                    pendingLookups.Remove(name);
            }

            lastLookupTime = DateTime.Now;

            try
            {
                await PerformBatchLookup(batch);
            }
            catch (Exception ex)
            {
                log.Warning($"Shared name lookup failed: {ex.Message}");
                lock (pendingLock)
                {
                    foreach (var name in batch)
                        pendingLookups.Add(name);
                }
            }
        }

        private async Task PerformBatchLookup(List<string> characters)
        {
            if (characters.Count == 0)
                return;

            var requestBody = new { characters };
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync($"{ApiBaseUrl}/names/lookup", content);

            if (!response.IsSuccessStatusCode)
            {
                log.Debug($"Names lookup returned {response.StatusCode}");
                return;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<LookupResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result?.Results == null)
                return;

            lock (cacheLock)
            {
                foreach (var kvp in result.Results)
                {
                    var entry = new SharedNameEntry
                    {
                        CSName = kvp.Value.CsName,
                        NameplateColor = ParseColor(kvp.Value.NameplateColor),
                        CachedAt = DateTime.Now,
                        IsNegativeCache = false
                    };
                    nameCache[kvp.Key] = entry;
                }

                foreach (var name in characters)
                {
                    if (!result.Results.ContainsKey(name) && !nameCache.ContainsKey(name))
                    {
                        nameCache[name] = new SharedNameEntry
                        {
                            CachedAt = DateTime.Now,
                            IsNegativeCache = true
                        };
                    }
                }
            }

        }

        private Vector3 ParseColor(float[]? colorArray)
        {
            if (colorArray == null || colorArray.Length < 3)
                return Vector3.One;

            return new Vector3(colorArray[0], colorArray[1], colorArray[2]);
        }

        /// <summary>Clear all cached data.</summary>
        public void ClearCache()
        {
            lock (cacheLock)
            {
                nameCache.Clear();
            }

            lock (pendingLock)
            {
                pendingLookups.Clear();
            }
        }

        public void Dispose()
        {
            httpClient?.Dispose();
        }

        private class LookupResponse
        {
            public Dictionary<string, LookupEntry> Results { get; set; } = new();
        }

        private class LookupEntry
        {
            public string CsName { get; set; } = "";
            public float[]? NameplateColor { get; set; }
        }
    }
}

using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CharacterSelectPlugin
{
    /// <summary>
    /// Manages fetching and caching whether players have shared RP profiles.
    /// Used for context menu "View RP Profile" option visibility.
    /// Separate from SharedNameManager - does NOT require AllowOthersToSeeMyCSName.
    /// </summary>
    public class RPProfileLookupManager : IDisposable
    {
        private readonly Plugin plugin;
        private readonly IPluginLog log;
        private readonly HttpClient httpClient;

        private readonly Dictionary<string, ProfileLookupEntry> profileCache = new();
        private readonly object cacheLock = new();
        private readonly HashSet<string> pendingLookups = new();
        private readonly object pendingLock = new();

        private DateTime lastLookupTime = DateTime.MinValue;
        private static readonly TimeSpan LookupCooldown = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan PositiveRefreshInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan NegativeRefreshInterval = TimeSpan.FromSeconds(60);

        private const string ApiBaseUrl = "https://character-select-profile-server-production.up.railway.app";
        private const int MaxBatchSize = 20;

        public class ProfileLookupEntry
        {
            public bool HasProfile { get; set; }
            public DateTime CachedAt { get; set; }
            public bool IsNegativeCache { get; set; }
        }

        public RPProfileLookupManager(Plugin plugin, IPluginLog log)
        {
            this.plugin = plugin;
            this.log = log;
            this.httpClient = new HttpClient();
            this.httpClient.Timeout = TimeSpan.FromSeconds(10);
            this.httpClient.DefaultRequestHeaders.Add("X-Plugin-Auth", "cs-plus-gallery-client");
            this.httpClient.DefaultRequestHeaders.Add("User-Agent", "CharacterSelectPlus/1.2.0");
        }

        /// <summary>Queues a character for profile lookup.</summary>
        public void QueueLookup(string physicalCharacterName)
        {
            if (string.IsNullOrEmpty(physicalCharacterName))
                return;

            lock (cacheLock)
            {
                if (profileCache.TryGetValue(physicalCharacterName, out var entry))
                {
                    var age = DateTime.Now - entry.CachedAt;
                    var maxDuration = entry.IsNegativeCache ? NegativeCacheDuration : CacheDuration;
                    var refreshInterval = entry.IsNegativeCache ? NegativeRefreshInterval : PositiveRefreshInterval;

                    if (age < maxDuration && age < refreshInterval)
                        return;
                }
            }

            lock (pendingLock)
            {
                pendingLookups.Add(physicalCharacterName);
            }
        }

        /// <summary>Checks if a player has a shared RP profile.</summary>
        public bool HasSharedProfile(string physicalCharacterName)
        {
            if (string.IsNullOrEmpty(physicalCharacterName))
                return false;

            lock (cacheLock)
            {
                if (profileCache.TryGetValue(physicalCharacterName, out var entry))
                {
                    var duration = entry.IsNegativeCache ? NegativeCacheDuration : CacheDuration;
                    if (DateTime.Now - entry.CachedAt > duration)
                    {
                        profileCache.Remove(physicalCharacterName);
                        return false;
                    }

                    return entry.HasProfile && !entry.IsNegativeCache;
                }
            }

            return false;
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
                // Don't re-queue on failure - prevents retry storm
                // Stale refresh will naturally retry later
                log.Debug($"Profile lookup failed: {ex.Message}");
            }
        }

        private async Task PerformBatchLookup(List<string> characters)
        {
            if (characters.Count == 0)
                return;

            var requestBody = new { characters };
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync($"{ApiBaseUrl}/profiles/lookup", content);

            if (!response.IsSuccessStatusCode)
            {
                log.Debug($"Profiles lookup returned {response.StatusCode}");
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
                // Cache positive results
                foreach (var kvp in result.Results)
                {
                    profileCache[kvp.Key] = new ProfileLookupEntry
                    {
                        HasProfile = kvp.Value.HasProfile,
                        CachedAt = DateTime.Now,
                        IsNegativeCache = false
                    };
                }

                // Cache negative results for characters not in response
                foreach (var name in characters)
                {
                    if (!result.Results.ContainsKey(name) && !profileCache.ContainsKey(name))
                    {
                        profileCache[name] = new ProfileLookupEntry
                        {
                            HasProfile = false,
                            CachedAt = DateTime.Now,
                            IsNegativeCache = true
                        };
                    }
                }
            }
        }

        /// <summary>Clear all cached data.</summary>
        public void ClearCache()
        {
            lock (cacheLock)
            {
                profileCache.Clear();
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
            public bool HasProfile { get; set; }
        }
    }
}

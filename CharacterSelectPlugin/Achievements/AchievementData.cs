using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace CharacterSelectPlugin.Achievements
{
    /// <summary>
    /// Persisted achievement progress. Stored in Configuration. Points are NOT stored directly.
    /// They're computed at runtime from the unlocked achievement list against the hardcoded
    /// registry, so editing the config file can't give you free points.
    /// </summary>
    [Serializable]
    public class AchievementData
    {
        /// <summary>
        /// Map of achievement ID → unlock timestamp. Only achievements whose ID exists in
        /// <see cref="AchievementRegistry.All"/> contribute to the computed point total.
        /// Fake or unknown IDs are silently ignored.
        /// </summary>
        public Dictionary<string, DateTime> UnlockedAchievements { get; set; } = new();

        /// <summary>Points spent in the rewards shop (future feature).</summary>
        public int PointsSpent { get; set; } = 0;

        /// <summary>
        /// Running count of total character switches (across all sessions). Used for
        /// milestone tracking without needing to count from scratch on each load.
        /// </summary>
        public int TotalSwitchCount { get; set; } = 0;

        /// <summary>Whether the one-time retroactive scan has already run.</summary>
        public bool HasCompletedRetroactiveScan { get; set; } = false;

        /// <summary>Version of the last retroactive scan. Bump this when new achievements are added to re-scan.</summary>
        public int RetroactiveScanVersion { get; set; } = 0;

        /// <summary>Achievement IDs awarded during retroactive scan, for the summary message.</summary>
        public List<string>? RetroactiveUnlocks { get; set; } = null;

        /// <summary>Whether the retroactive summary has been shown to the user.</summary>
        public bool HasShownRetroactiveSummary { get; set; } = false;

        /// <summary>Achievement IDs that have been seen/acknowledged by the user (no more glow on trophy).</summary>
        public HashSet<string> SeenAchievements { get; set; } = new();

        /// <summary>Achievement IDs whose unlock celebration animation has already played in the window.
        /// Persisted so unlocks that happen while CS+ isn't running still celebrate on the next window open.</summary>
        public HashSet<string> CelebratedAchievements { get; set; } = new();

        /// <summary>True once the legacy/upgrade seed has run. On first launch with the celebration-persistence
        /// field, all currently-unlocked achievements get added to <see cref="CelebratedAchievements"/> so existing
        /// users don't get flooded with celebrations for everything they unlocked before this feature existed.</summary>
        public bool HasInitializedCelebrations { get; set; } = false;

        /// <summary>Distinct seasonal theme names ever applied (Halloween, Winter, Christmas, Valentines).
        /// Used by "Seasoned", fires when 3+ different ones have been used.</summary>
        public HashSet<string> SeasonalThemesUsed { get; set; } = new();

        // ── Computed properties (not persisted) ──

        /// <summary>
        /// Total points earned, computed from unlocked achievements against the hardcoded registry.
        /// Unknown achievement IDs contribute 0 points, so editing the config can't inflate this.
        /// </summary>
        [JsonIgnore]
        public int TotalPointsEarned => UnlockedAchievements.Keys
            .Select(id => AchievementRegistry.Get(id))
            .Where(def => def != null)
            .Sum(def => def!.Points);

        /// <summary>Points available to spend (earned minus spent). Floor of 0.</summary>
        [JsonIgnore]
        public int AvailablePoints => Math.Max(0, TotalPointsEarned - PointsSpent);

        /// <summary>Number of valid achievements unlocked.</summary>
        [JsonIgnore]
        public int UnlockedCount => UnlockedAchievements.Keys
            .Count(id => AchievementRegistry.Get(id) != null);

        /// <summary>Number of CORE (non-bonus) achievements unlocked. Used for the
        /// primary stats display and meta progress.</summary>
        [JsonIgnore]
        public int UnlockedCoreCount => UnlockedAchievements.Keys
            .Select(AchievementRegistry.Get)
            .Count(def => def != null && !def.IsBonus);

        /// <summary>Number of BONUS achievements unlocked. Shown as a secondary stat.</summary>
        [JsonIgnore]
        public int UnlockedBonusCount => UnlockedAchievements.Keys
            .Select(AchievementRegistry.Get)
            .Count(def => def != null && def.IsBonus);

        /// <summary>Whether there are unseen (new) achievements the user hasn't opened the window for.</summary>
        [JsonIgnore]
        public bool HasUnseenAchievements => UnlockedAchievements.Keys
            .Any(id => AchievementRegistry.Get(id) != null && !SeenAchievements.Contains(id));

        // ── Methods ──

        /// <summary>Check if a specific achievement has been unlocked.</summary>
        public bool IsUnlocked(string achievementId) =>
            UnlockedAchievements.ContainsKey(achievementId);

        /// <summary>
        /// Try to unlock an achievement. Returns true if newly unlocked (wasn't already).
        /// Returns false if already unlocked or the achievement ID doesn't exist in the registry.
        /// </summary>
        public bool TryUnlock(string achievementId)
        {
            if (UnlockedAchievements.ContainsKey(achievementId))
                return false;

            // Only unlock achievements that exist in the registry
            if (AchievementRegistry.Get(achievementId) == null)
                return false;

            UnlockedAchievements[achievementId] = DateTime.UtcNow;
            return true;
        }
    }
}

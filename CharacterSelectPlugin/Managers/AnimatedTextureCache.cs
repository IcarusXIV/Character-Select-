using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.IO;

namespace CharacterSelectPlugin.Managers
{
    /// <summary>Per-character cache of <see cref="AnimatedTextureWrap"/> instances.
    /// Keyed by character (reference equality is fine, characters are stable instances).
    /// Detects path/mtime changes and rebuilds; disposes wraps when characters are removed.</summary>
    public sealed class AnimatedTextureCache : IDisposable
    {
        private readonly ITextureProvider textureProvider;
        private readonly Dictionary<Character, Entry> cache = new();
        private bool disposed;

        public AnimatedTextureCache(ITextureProvider textureProvider)
        {
            this.textureProvider = textureProvider;
        }

        private sealed class Entry
        {
            public AnimatedTextureWrap? Wrap;
            public string Path = "";
            public DateTime LastWriteUtc;
            public bool LoadFailed;
        }

        /// <summary>Get the animated wrap for a character, loading on demand.
        /// Returns null if the character has no AnimatedImagePath, the file is missing,
        /// or loading previously failed.  Caller is responsible for setting IsHovered
        /// each frame.</summary>
        public AnimatedTextureWrap? GetOrLoad(Character character)
        {
            if (disposed || character == null) return null;
            var path = character.AnimatedImagePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                // Drop a cached wrap if the path was cleared or the file went away
                if (cache.TryGetValue(character, out var stale))
                {
                    stale.Wrap?.Dispose();
                    cache.Remove(character);
                }
                return null;
            }

            DateTime mtime;
            try { mtime = File.GetLastWriteTimeUtc(path); }
            catch { return null; }

            if (cache.TryGetValue(character, out var entry))
            {
                if (entry.LoadFailed) return null;
                // path or file changed → rebuild
                if (entry.Path != path || entry.LastWriteUtc != mtime)
                {
                    entry.Wrap?.Dispose();
                    entry.Wrap = TryLoad(path);
                    entry.Path = path;
                    entry.LastWriteUtc = mtime;
                    entry.LoadFailed = entry.Wrap == null;
                }
                return entry.Wrap;
            }

            var wrap = TryLoad(path);
            cache[character] = new Entry
            {
                Wrap = wrap,
                Path = path,
                LastWriteUtc = mtime,
                LoadFailed = wrap == null,
            };
            return wrap;
        }

        /// <summary>Remove the cached wrap for a character (e.g. on delete).
        /// Safe to call with characters that aren't cached.</summary>
        public void Forget(Character character)
        {
            if (cache.TryGetValue(character, out var entry))
            {
                entry.Wrap?.Dispose();
                cache.Remove(character);
            }
        }

        /// <summary>Drop wraps for any character not in the active list.
        /// Called periodically (e.g. on character delete or roster refresh) to
        /// keep the cache from leaking after profiles are removed.</summary>
        public void PruneTo(IReadOnlyCollection<Character> activeCharacters)
        {
            if (activeCharacters == null) return;
            var keep = new HashSet<Character>(activeCharacters);
            List<Character>? toRemove = null;
            foreach (var kv in cache)
            {
                if (!keep.Contains(kv.Key))
                {
                    toRemove ??= new List<Character>();
                    toRemove.Add(kv.Key);
                }
            }
            if (toRemove == null) return;
            foreach (var k in toRemove)
            {
                cache[k].Wrap?.Dispose();
                cache.Remove(k);
            }
        }

        private AnimatedTextureWrap? TryLoad(string path)
        {
            try
            {
                return new AnimatedTextureWrap(textureProvider, path);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"[AnimatedTextureCache] Load failed for '{path}': {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var entry in cache.Values)
            {
                try { entry.Wrap?.Dispose(); } catch { }
            }
            cache.Clear();
        }
    }
}

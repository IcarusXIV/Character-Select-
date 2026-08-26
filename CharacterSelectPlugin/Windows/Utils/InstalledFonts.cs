using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace CharacterSelectPlugin.Windows.Utils
{
    // Cached list of installed fonts (registry first, directory scan fallback for Wine)
    public static class InstalledFonts
    {
        private const string FontsKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";

        private static List<(string Name, string Path)>? _all;
        private static Dictionary<string, string>? _byName;
        private static Dictionary<string, string>? _byPath;

        public static IReadOnlyList<(string Name, string Path)> All
        {
            get { EnsureLoaded(); return _all!; }
        }

        public static bool TryGetPath(string name, out string path)
        {
            EnsureLoaded();
            return _byName!.TryGetValue(name, out path!);
        }

        public static string NameForPath(string path)
        {
            EnsureLoaded();
            if (_byPath!.TryGetValue(path, out var name))
                return name;
            try { return Path.GetFileNameWithoutExtension(path); }
            catch { return path; }
        }

        private static void EnsureLoaded()
        {
            if (_all != null) return;

            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (string.IsNullOrEmpty(systemDir))
                systemDir = @"C:\Windows\Fonts";
            string userDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Fonts");

            ReadRegistry(Registry.LocalMachine, systemDir, byName);
            ReadRegistry(Registry.CurrentUser, userDir, byName);

            // Wine or empty registry: scan the font directories directly
            if (byName.Count == 0)
            {
                ScanDirectory(systemDir, byName);
                ScanDirectory(userDir, byName);
            }

            _all = byName
                .Select(kv => (Name: kv.Key, Path: kv.Value))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _byName = byName;
            _byPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, path) in _all)
                _byPath.TryAdd(path, name);
        }

        private static void ReadRegistry(RegistryKey root, string baseDir, Dictionary<string, string> byName)
        {
            try
            {
                using var key = root.OpenSubKey(FontsKey);
                if (key == null) return;
                foreach (var valueName in key.GetValueNames())
                {
                    if (key.GetValue(valueName) is not string file || file.Length == 0)
                        continue;
                    string full;
                    try { full = Path.IsPathRooted(file) ? file : Path.Combine(baseDir, file); }
                    catch { continue; }
                    if (!HasFontExtension(full) || !File.Exists(full))
                        continue;
                    string display = StripTypeSuffix(valueName).Trim();
                    if (display.Length == 0)
                        continue;
                    byName.TryAdd(display, full);
                }
            }
            catch { }
        }

        private static void ScanDirectory(string dir, Dictionary<string, string> byName)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    if (!HasFontExtension(file)) continue;
                    string display = Path.GetFileNameWithoutExtension(file);
                    if (display.Length == 0) continue;
                    byName.TryAdd(display, file);
                }
            }
            catch { }
        }

        private static bool HasFontExtension(string path)
        {
            return path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase);
        }

        private static string StripTypeSuffix(string name)
        {
            if (name.EndsWith(" (TrueType)", StringComparison.OrdinalIgnoreCase))
                return name[..^" (TrueType)".Length];
            if (name.EndsWith(" (OpenType)", StringComparison.OrdinalIgnoreCase))
                return name[..^" (OpenType)".Length];
            return name;
        }
    }
}

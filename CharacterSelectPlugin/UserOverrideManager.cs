using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Dalamud.Plugin;
using CharacterSelectPlugin.Windows;

namespace CharacterSelectPlugin
{
    public class UserOverrideManager
    {
        private readonly IDalamudPluginInterface pluginInterface;
        private readonly Dictionary<string, ModType> overrides;
        private readonly string overridesFilePath;

        public UserOverrideManager(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
            this.overrides = new Dictionary<string, ModType>();
            this.overridesFilePath = Path.Combine(pluginInterface.ConfigDirectory.FullName, "userModOverrides.json");
            
            LoadOverrides();
        }

        public void SetOverride(string modIdentifier, ModType category)
        {
            overrides[modIdentifier] = category;
            SaveOverrides();
        }

        public void RemoveOverride(string modIdentifier)
        {
            if (overrides.Remove(modIdentifier))
            {
                SaveOverrides();
            }
        }

        public bool HasOverride(string modIdentifier)
        {
            return overrides.ContainsKey(modIdentifier);
        }

        public ModType? GetOverride(string modIdentifier)
        {
            return overrides.TryGetValue(modIdentifier, out var category) ? category : null;
        }

        public Dictionary<string, ModType> GetAllOverrides()
        {
            return new Dictionary<string, ModType>(overrides);
        }

        private void SaveOverrides()
        {
            try
            {
                var json = JsonConvert.SerializeObject(overrides, Formatting.Indented);
                File.WriteAllText(overridesFilePath, json);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[UserOverrideManager] Failed to save overrides: {ex.Message}");
            }
        }

        private void LoadOverrides()
        {
            try
            {
                if (File.Exists(overridesFilePath))
                {
                    var json = File.ReadAllText(overridesFilePath);
                    var loadedOverrides = JsonConvert.DeserializeObject<Dictionary<string, ModType>>(json);
                    
                    if (loadedOverrides != null)
                    {
                        foreach (var kvp in loadedOverrides)
                        {
                            overrides[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[UserOverrideManager] Failed to load overrides: {ex.Message}");
            }
        }

        public void Dispose()
        {
            SaveOverrides();
        }
    }
}
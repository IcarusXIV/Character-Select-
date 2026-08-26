using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;

namespace CharacterSelectPlugin
{
    /// <summary>
    /// IPC Provider for Character Select+ to allow other plugins to interact with character switching
    /// </summary>
    public class IPCProvider : IDisposable
    {
        private readonly Plugin plugin;
        private readonly IDalamudPluginInterface pluginInterface;

        // IPC Providers
        private readonly Dalamud.Plugin.Ipc.ICallGateProvider<string[]> getCharacterListProvider;
        private readonly Dalamud.Plugin.Ipc.ICallGateProvider<string> getCurrentCharacterProvider;
        private readonly Dalamud.Plugin.Ipc.ICallGateProvider<string, string[]> getCharacterDesignsProvider;
        private readonly Dalamud.Plugin.Ipc.ICallGateProvider<string, bool> switchToCharacterProvider;
        private readonly Dalamud.Plugin.Ipc.ICallGateProvider<string, string, bool> switchToCharacterDesignProvider;
        private readonly Dalamud.Plugin.Ipc.ICallGateProvider<bool> getQuickSwitchVisibilityProvider;
        private readonly Dalamud.Plugin.Ipc.ICallGateProvider<bool> toggleQuickSwitchProvider;
        private readonly Dalamud.Plugin.Ipc.ICallGateProvider<string, string, object> onCharacterChangedProvider;
        private readonly Dalamud.Plugin.Ipc.ICallGateProvider<string?> getSyncedNameProvider;
        private readonly Dalamud.Plugin.Ipc.ICallGateProvider<string?, object> onSyncedNameChangedProvider;

        private readonly Dalamud.Plugin.Ipc.ICallGateProvider<int> getCharacterCountProvider;
        private readonly Dalamud.Plugin.Ipc.ICallGateProvider<List<(string, bool, string?)>> getCharactersProvider;
        private readonly Dalamud.Plugin.Ipc.ICallGateProvider<string, string, string> getDesignPreviewPathProvider;
        private readonly Dalamud.Plugin.Ipc.ICallGateProvider<string, string> getCharacterColorProvider;
#if DEV_BUILD
        private readonly Dalamud.Plugin.Ipc.ICallGateProvider<string, int, int, bool> applyToObjectProvider;
#endif

        public IPCProvider(Plugin plugin, IDalamudPluginInterface pluginInterface)
        {
            this.plugin = plugin;
            this.pluginInterface = pluginInterface;

            // Initialize IPC providers
            getCharacterListProvider = pluginInterface.GetIpcProvider<string[]>("CharacterSelect.GetCharacterList");
            getCurrentCharacterProvider = pluginInterface.GetIpcProvider<string>("CharacterSelect.GetCurrentCharacter");
            getCharacterDesignsProvider = pluginInterface.GetIpcProvider<string, string[]>("CharacterSelect.GetCharacterDesigns");
            switchToCharacterProvider = pluginInterface.GetIpcProvider<string, bool>("CharacterSelect.SwitchToCharacter");
            switchToCharacterDesignProvider = pluginInterface.GetIpcProvider<string, string, bool>("CharacterSelect.SwitchToCharacterDesign");
            getQuickSwitchVisibilityProvider = pluginInterface.GetIpcProvider<bool>("CharacterSelect.GetQuickSwitchVisibility");
            toggleQuickSwitchProvider = pluginInterface.GetIpcProvider<bool>("CharacterSelect.ToggleQuickSwitch");
            onCharacterChangedProvider = pluginInterface.GetIpcProvider<string, string, object>("CharacterSelect.OnCharacterChanged");
            getSyncedNameProvider = pluginInterface.GetIpcProvider<string?>("CharacterSelect.GetSyncedName");
            onSyncedNameChangedProvider = pluginInterface.GetIpcProvider<string?, object>("CharacterSelect.OnSyncedNameChanged");
            
            getCharacterCountProvider = pluginInterface.GetIpcProvider<int>("CharacterSelect.GetCharacterCount");
            getCharactersProvider = pluginInterface.GetIpcProvider<List<(string, bool, string?)>>("CharacterSelect.GetCharacters");
            getDesignPreviewPathProvider = pluginInterface.GetIpcProvider<string, string, string>("CharacterSelect.GetDesignPreviewPath");
            getCharacterColorProvider = pluginInterface.GetIpcProvider<string, string>("CharacterSelect.GetCharacterColor");
#if DEV_BUILD
            applyToObjectProvider = pluginInterface.GetIpcProvider<string, int, int, bool>("CharacterSelect.ApplyToObject");
#endif

            // Register IPC methods
            RegisterMethods();
        }

        private void RegisterMethods()
        {
            getCharacterListProvider.RegisterFunc(GetCharacterList);
            getCurrentCharacterProvider.RegisterFunc(GetCurrentCharacter);
            getCharacterDesignsProvider.RegisterFunc(GetCharacterDesigns);
            switchToCharacterProvider.RegisterFunc(SwitchToCharacter);
            switchToCharacterDesignProvider.RegisterFunc(SwitchToCharacterDesign);
            getQuickSwitchVisibilityProvider.RegisterFunc(GetQuickSwitchVisibility);
            toggleQuickSwitchProvider.RegisterFunc(ToggleQuickSwitch);
            getSyncedNameProvider.RegisterFunc(GetSyncedName);

            getCharacterCountProvider.RegisterFunc(GetCharacterCount);
            getCharactersProvider.RegisterFunc(GetCharacters);
            getDesignPreviewPathProvider.RegisterFunc(GetDesignPreviewPath);
            getCharacterColorProvider.RegisterFunc(GetCharacterColor);
#if DEV_BUILD
            applyToObjectProvider.RegisterFunc(ApplyToObject);
#endif

            Plugin.Log.Info("[IPC] All IPC methods registered including GetCharacterCount and GetCharacters");
        }

        /// <summary>
        /// Get list of all character names
        /// </summary>
        private string[] GetCharacterList()
        {
            return plugin.Characters.Select(c => c.Name).ToArray();
        }

        /// <summary>
        /// Get currently active character name, or empty string if none
        /// </summary>
        private string GetCurrentCharacter()
        {
            return plugin.activeCharacter?.Name ?? "";
        }

        /// <summary>
        /// Get list of design names for a specific character
        /// </summary>
        private string[] GetCharacterDesigns(string characterName)
        {
            var character = plugin.Characters.FirstOrDefault(c => c.Name == characterName);
            if (character == null)
                return Array.Empty<string>();
            
            return character.Designs.Select(d => d.Name).ToArray();
        }

        /// <summary>
        /// Switch to a character by name (no design)
        /// </summary>
        private bool SwitchToCharacter(string characterName)
        {
            var character = plugin.Characters.FirstOrDefault(c => c.Name == characterName);
            if (character == null)
                return false;
            
            try
            {
                plugin.ApplyProfile(character, -1);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Switch to a character and specific design
        /// </summary>
        private bool SwitchToCharacterDesign(string characterName, string designName)
        {
            var character = plugin.Characters.FirstOrDefault(c => c.Name == characterName);
            if (character == null)
                return false;
            
            var designIndex = character.Designs.FindIndex(d => d.Name == designName);
            if (designIndex == -1)
                return false;
            
            try
            {
                plugin.ApplyProfile(character, designIndex);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get whether the Quick Switch window is currently visible
        /// </summary>
        private bool GetQuickSwitchVisibility()
        {
            return plugin.QuickSwitchWindow?.IsOpen ?? false;
        }

        /// <summary>
        /// Toggle the Quick Switch window visibility
        /// </summary>
        private bool ToggleQuickSwitch()
        {
            try
            {
                if (plugin.QuickSwitchWindow?.IsOpen ?? false)
                {
                    plugin.QuickSwitchWindow.IsOpen = false;
                }
                else
                {
                    plugin.QuickSwitchWindow.IsOpen = true;
                }
                return plugin.QuickSwitchWindow?.IsOpen ?? false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get count of characters
        /// </summary>
        private int GetCharacterCount()
        {
            var count = plugin.Characters.Count;
            Plugin.Log.Info($"[IPC] GetCharacterCount called, returning: {count}");
            return count;
        }

        /// <summary>
        /// Get character information
        /// Returns list of tuples with (name, isActive, currentDesign)
        /// </summary>
        private List<(string, bool, string?)> GetCharacters()
        {
            var result = new List<(string, bool, string?)>();
            
            Plugin.Log.Info($"[IPC] GetCharacters called. Total characters: {plugin.Characters.Count}");
            Plugin.Log.Info($"[IPC] Active character: {plugin.activeCharacter?.Name ?? "None"}");
            
            foreach (var character in plugin.Characters)
            {
                bool isActive = plugin.activeCharacter?.Name == character.Name;
                string? currentDesign = null;
                
                Plugin.Log.Info($"[IPC] Character: {character.Name}, Active: {isActive}");
                result.Add((character.Name, isActive, currentDesign));
            }
            
            Plugin.Log.Info($"[IPC] GetCharacters returning {result.Count} characters");
            return result;
        }

        // Empty string when the design has no preview
        private string GetDesignPreviewPath(string characterName, string designName)
        {
            var character = plugin.Characters.FirstOrDefault(c => c.Name == characterName);
            var design = character?.Designs.FirstOrDefault(d => d.Name == designName);
            return design?.PreviewImagePath ?? "";
        }

        // Nameplate colour as #RRGGBB, empty string if the character is missing
        private string GetCharacterColor(string characterName)
        {
            var character = plugin.Characters.FirstOrDefault(c => c.Name == characterName);
            if (character == null)
                return "";
            var c = character.NameplateColor;
            return $"#{(int)(c.X * 255):X2}{(int)(c.Y * 255):X2}{(int)(c.Z * 255):X2}";
        }

#if DEV_BUILD
        // Returns true once the apply is started; the apply itself runs asynchronously
        private bool ApplyToObject(string characterName, int objectIndex, int designIndex)
        {
            var character = plugin.Characters.FirstOrDefault(c => c.Name == characterName);
            if (character == null)
            {
                Plugin.Log.Warning($"[IPC] ApplyToObject: character '{characterName}' not found (index {objectIndex})");
                return false;
            }

            try
            {
                Plugin.Log.Info($"[IPC] ApplyToObject: applying '{characterName}' (design {designIndex}) to object index {objectIndex}");
                // Empty target name skips the name-match check, which only guards swapped real targets
                _ = plugin.ApplyToTarget(character, designIndex, objectIndex,
                    Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc, "", skipRateLimit: true);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"[IPC] ApplyToObject threw: {ex.Message}");
                return false;
            }
        }
#endif

        // the name Name Sync shows for the local player, or null when nothing is shared
        private string? GetSyncedName()
        {
            var c = plugin.activeCharacter;
            if (c == null) return null;
            if (!plugin.Configuration.AllowOthersToSeeMyCSName) return null;
            if (c.ExcludeFromNameSync) return null;
            return !string.IsNullOrWhiteSpace(c.Alias) ? c.Alias : c.Name;
        }

        public void NotifySyncedNameChanged()
        {
            try
            {
                onSyncedNameChangedProvider.SendMessage(GetSyncedName());
            }
            catch
            {
                // Ignore notification failures
            }
        }

        public void NotifyCharacterChanged(string characterName, string designName)
        {
            try
            {
                onCharacterChangedProvider.SendMessage(characterName, designName);
            }
            catch
            {
                // Ignore notification failures
            }
        }

        public void Dispose()
        {
            // IPC providers are automatically cleaned up by Dalamud
        }
    }
}
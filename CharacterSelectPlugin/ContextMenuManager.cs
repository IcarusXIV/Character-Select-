using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;

namespace CharacterSelectPlugin.Managers
{
    public class ContextMenuManager : IDisposable
    {
        private readonly Plugin plugin;
        private readonly IContextMenu contextMenu;

        private static readonly string[] ValidAddons =
        [
            "PartyMemberList",
            "FriendList",
            "FreeCompany",
            "LinkShell",
            "CrossWorldLinkshell",
            "_PartyList",
            "ChatLog",
            "LookingForGroup",
            "BlackList",
            "ContentMemberList",
            "SocialList",
            "ContactList",
            "CharacterInspect",
            "_Target",
            "NamePlate",
            "_NaviMap",
            "SelectString",
            "SelectIconString"
        ];

        private static readonly Dictionary<uint, string> WorldIdToName = new()
        {
            { 404, "Marilith" },
            { 410, "Rafflesia" },
            { 411, "White Rook" },
            { 100, "FictitiousWorld" },
        };

        public ContextMenuManager(Plugin plugin, IContextMenu contextMenu)
        {
            this.plugin = plugin;
            this.contextMenu = contextMenu;
            this.contextMenu.OnMenuOpened += OnMenuOpened;
        }

        public void Dispose()
        {
            this.contextMenu.OnMenuOpened -= OnMenuOpened;
        }

        private void OnMenuOpened(IMenuOpenedArgs args)
        {
            if (args.Target is MenuTargetDefault def && ValidAddons.Contains(args.AddonName))
            {
                HandleUIContextMenu(args, def);
                return;
            }

            if (args.Target is MenuTargetDefault objTarget && args.AddonName == null)
            {
                HandleGameObjectContextMenu(args, objTarget);
                return;
            }
        }

        private void HandleUIContextMenu(IMenuOpenedArgs args, MenuTargetDefault def)
        {
            if (def.TargetHomeWorld.RowId == 0)
                return;

            var name = def.TargetName;
            var worldRow = def.TargetHomeWorld;

            string worldName = worldRow.RowId > 0
                ? worldRow.Value.Name.ToString()
                : $"World-{worldRow.RowId}";

            if (!string.IsNullOrWhiteSpace(name))
            {
                var physicalName = $"{name}@{worldName}";

                // Queue lookup for this player (for future interactions)
                plugin.RPProfileLookupManager?.QueueLookup(physicalName);

                // View RP Profile - for UI menus (chat, friend list, FC, etc.) always show
                // since we can't pre-fetch non-nearby players
                if (plugin.Configuration.ShowViewRPContextMenu)
                {
                    args.AddMenuItem(new MenuItem
                    {
                        Name = "View RP Profile",
                        Priority = 0,
                        PrefixChar = 'C',
                        PrefixColor = 37,
                        OnClicked = _ => Task.Run(() => plugin.TryRequestRPProfile(physicalName)),
                        IsEnabled = true
                    });
                }

                // Get CS+ name from cache (if any)
                var csEntry = plugin.SharedNameManager?.GetCachedName(physicalName);
                var csName = csEntry?.CSName;

                // Only show Block/Report options if they have a CS+ name
                if (!string.IsNullOrEmpty(csName))
                {
                    // Block CS+ User (only show if not already blocked)
                    if (plugin.Configuration.ShowBlockUserContextMenu &&
                        !plugin.Configuration.BlockedCSUsers.Contains(physicalName))
                    {
                        args.AddMenuItem(new MenuItem
                        {
                            Name = "Block CS+ User",
                            Priority = 0,
                            PrefixChar = 'C',
                            PrefixColor = 37,
                            OnClicked = _ => BlockUser(physicalName, csName),
                            IsEnabled = true
                        });
                    }

                    // Report CS+ Name
                    if (plugin.Configuration.ShowReportUserContextMenu)
                    {
                        args.AddMenuItem(new MenuItem
                        {
                            Name = "Report CS+ Name",
                            Priority = 0,
                            PrefixChar = 'C',
                            PrefixColor = 37,
                            OnClicked = _ => plugin.OpenReportWindow(physicalName, csName),
                            IsEnabled = true
                        });
                    }
                }
            }
        }

        private void HandleGameObjectContextMenu(IMenuOpenedArgs args, MenuTargetDefault target)
        {
            try
            {
                var currentTarget = Plugin.TargetManager.Target;

                if (currentTarget != null &&
                    currentTarget.ObjectKind == ObjectKind.Player &&
                    currentTarget is IPlayerCharacter player)
                {
                    string characterName = player.Name.TextValue;
                    string worldName = player.HomeWorld.Value.Name.ToString();

                    if (!string.IsNullOrWhiteSpace(characterName) && !string.IsNullOrWhiteSpace(worldName))
                    {
                        var physicalName = $"{characterName}@{worldName}";

                        // Queue lookup for this player (backup, should already be cached via nameplate)
                        plugin.RPProfileLookupManager?.QueueLookup(physicalName);

                        // View RP Profile - for nearby players, only show if confirmed they have a profile
                        // (pre-fetched via nameplate processing)
                        if (plugin.Configuration.ShowViewRPContextMenu &&
                            plugin.RPProfileLookupManager?.HasSharedProfile(physicalName) == true)
                        {
                            args.AddMenuItem(new MenuItem
                            {
                                Name = "View RP Profile",
                                Priority = 0,
                                PrefixChar = 'C',
                                PrefixColor = 37,
                                OnClicked = _ => Task.Run(() => plugin.TryRequestRPProfile(physicalName)),
                                IsEnabled = true
                            });
                        }

                        // Get CS+ name from cache (if any)
                        var csEntry = plugin.SharedNameManager?.GetCachedName(physicalName);
                        var csName = csEntry?.CSName;

                        // Only show Block/Report options if they have a CS+ name
                        if (!string.IsNullOrEmpty(csName))
                        {
                            // Block CS+ User (only show if not already blocked)
                            if (plugin.Configuration.ShowBlockUserContextMenu &&
                                !plugin.Configuration.BlockedCSUsers.Contains(physicalName))
                            {
                                args.AddMenuItem(new MenuItem
                                {
                                    Name = "Block CS+ User",
                                    Priority = 0,
                                    PrefixChar = 'C',
                                    PrefixColor = 37,
                                    OnClicked = _ => BlockUser(physicalName, csName),
                                    IsEnabled = true
                                });
                            }

                            // Report CS+ Name
                            if (plugin.Configuration.ShowReportUserContextMenu)
                            {
                                args.AddMenuItem(new MenuItem
                                {
                                    Name = "Report CS+ Name",
                                    Priority = 0,
                                    PrefixChar = 'C',
                                    PrefixColor = 37,
                                    OnClicked = _ => plugin.OpenReportWindow(physicalName, csName),
                                    IsEnabled = true
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error handling game object context menu: {ex.Message}");
            }
        }

        private void BlockUser(string physicalName, string csName)
        {
            plugin.Configuration.BlockedCSUsers.Add(physicalName);
            plugin.Configuration.Save();
            Plugin.Log.Info($"Blocked CS+ user: {physicalName} (CS+ name: {csName})");
        }
    }
}

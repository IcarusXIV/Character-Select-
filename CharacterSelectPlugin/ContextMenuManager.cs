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
                args.AddMenuItem(new MenuItem
                {
                    Name = "View RP Profile",
                    OnClicked = _ => Task.Run(() => plugin.TryRequestRPProfile($"{name}@{worldName}")),
                    IsEnabled = true
                });
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
                        args.AddMenuItem(new MenuItem
                        {
                            Name = "View RP Profile",
                            OnClicked = _ => Task.Run(() => plugin.TryRequestRPProfile($"{characterName}@{worldName}")),
                            IsEnabled = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error handling game object context menu: {ex.Message}");
            }
        }
    }
}

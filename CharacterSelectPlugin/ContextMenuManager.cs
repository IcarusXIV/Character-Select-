using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
        ];

        private static readonly Dictionary<uint, string> WorldIdToName = new()
        {
            { 404, "Marilith" },
            { 410, "Rafflesia" },
            { 411, "White Rook" },
            { 100, "FictitiousWorld" }, // Add all worlds you support here
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
            if (args.Target is not MenuTargetDefault def || !ValidAddons.Contains(args.AddonName))
                return;

            // Skip if the clicked thing has no valid home world (NPCs, FC actions, etc)
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

    }
}

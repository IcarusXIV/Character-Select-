using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using CharacterSelectPlugin.Windows.Components;
using CharacterSelectPlugin.Windows.Styles;
using Dalamud.Interface;
using CharacterSelectPlugin.Effects;

namespace CharacterSelectPlugin.Windows
{
    public class MainWindow : Window, IDisposable
    {
        private Plugin plugin;
        private CharacterGrid characterGrid;
        private CharacterForm characterForm;
        private DesignPanel designPanel;
        private SettingsPanel settingsPanel;
        private ReorderWindow reorderWindow;
        private UIStyles uiStyles;
        private FavoriteSparkEffect diceEffect = new();
        public bool IsDesignPanelOpen => designPanel?.IsOpen ?? false;
        public bool IsEditCharacterWindowOpen => characterForm?.IsEditWindowOpen ?? false;
        public bool IsReorderWindowOpen => reorderWindow?.IsOpen ?? false;
        
        public DesignPanel? GetDesignPanel() => designPanel;

        public MainWindow(Plugin plugin)
            : base("Character Select+", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoDocking)
        {
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(850, 700),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };

            this.plugin = plugin;
            this.uiStyles = new UIStyles(plugin);

            this.characterGrid = new CharacterGrid(plugin, uiStyles);
            this.characterForm = new CharacterForm(plugin, uiStyles);
            this.designPanel = new DesignPanel(plugin, uiStyles);
            this.settingsPanel = new SettingsPanel(plugin, uiStyles, this);
            this.reorderWindow = new ReorderWindow(plugin, uiStyles);
        }
        public void InvalidateLayout()
        {
            characterGrid?.InvalidateCache();
        }

        public void Dispose()
        {
            characterGrid?.Dispose();
            characterForm?.Dispose();
            designPanel?.Dispose();
            settingsPanel?.Dispose();
            reorderWindow?.Dispose();
        }

        public override void Draw()
        {
            // Main window position
            plugin.MainWindowPos = ImGui.GetWindowPos();
            plugin.MainWindowSize = ImGui.GetWindowSize();

            // UI styling
            uiStyles.PushMainWindowStyle();

            try
            {
                DrawHeader();
                DrawMainContent();
                DrawBottomBar();
                DrawSupportButton();

                settingsPanel.Draw();
                reorderWindow.Draw();
            }

            finally
            {
                uiStyles.PopMainWindowStyle();
            }
            float deltaTime = ImGui.GetIO().DeltaTime;
            diceEffect.Update(deltaTime);
            diceEffect.Draw();
        }

        private void DrawHeader()
        {
            // Draw "Choose your character" text with character count
            int totalCharacters = plugin.Characters.Count;
            string headerText = $"Choose your character";
            ImGui.Text(headerText);

            // Add character count in subtle gray
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
            ImGui.Text($"({totalCharacters} total)");
            ImGui.PopStyleColor();

            // Move to the same line and position Discord button at far right
            ImGui.SameLine();

            var dpiScale = ImGui.GetIO().DisplayFramebufferScale.X;
            var uiScale = plugin.Configuration.UIScaleMultiplier;
            var totalScale = dpiScale * uiScale;

            float buttonWidth = 70 * totalScale;
            float buttonHeight = ImGui.GetTextLineHeight() + ImGui.GetStyle().FramePadding.Y * 2;
            float availableWidth = ImGui.GetContentRegionAvail().X;

            // Position button at far right of the same line
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availableWidth - buttonWidth);

            // Discord button with proper text alignment
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.39f, 0.96f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.35f, 0.39f, 0.96f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.25f, 0.29f, 0.86f, 1.0f));

            if (ImGui.Button("Discord", new Vector2(buttonWidth, buttonHeight)))
            {
                Dalamud.Utility.Util.OpenLink("https://discord.gg/8JykGErcX4");
            }

            ImGui.PopStyleColor(3);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Join our Discord community!");
            }

            ImGui.Separator();
        }


        public void UpdateSortType()
        {
            characterGrid.SetSortType((Plugin.SortType)plugin.Configuration.CurrentSortIndex);
        }

        private void DrawMainContent()
        {
            // Character form (Add/Edit)
            if (plugin.IsAddCharacterWindowOpen || characterForm.IsEditWindowOpen)
            {
                characterForm.Draw();
            }

            float characterGridWidth = 0;
            if (designPanel.IsOpen)
            {
                var dpiScale = ImGui.GetIO().DisplayFramebufferScale.X;
                var uiScale = plugin.Configuration.UIScaleMultiplier;
                var totalScale = dpiScale * uiScale;
                float scaledPanelWidth = designPanel.PanelWidth * totalScale;

                characterGridWidth = -(scaledPanelWidth + 10);
            }

            // Main area
            ImGui.BeginChild("CharacterGrid", new Vector2(characterGridWidth, -30), true);
            characterGrid.Draw();
            ImGui.EndChild();

            // Design panel (right side)
            if (designPanel.IsOpen)
            {
                ImGui.SameLine();
                float characterGridHeight = ImGui.GetItemRectSize().Y;

                var dpiScale = ImGui.GetIO().DisplayFramebufferScale.X;
                var uiScale = plugin.Configuration.UIScaleMultiplier;
                var totalScale = dpiScale * uiScale;
                float scaledPanelWidth = designPanel.PanelWidth * totalScale;

                ImGui.BeginChild("DesignPanel", new Vector2(scaledPanelWidth, characterGridHeight), true);
                designPanel.Draw();
                ImGui.EndChild();
            }
        }
        public void OpenAddCharacterWindow(bool secretMode = false)
        {
            characterForm.ResetFields();
            if (secretMode)
            {
                characterForm.SetSecretMode(true);
            }
            plugin.IsAddCharacterWindowOpen = true;
        }

        public void CloseAddCharacterWindow()
        {
            plugin.IsAddCharacterWindowOpen = false;
            characterForm.SetSecretMode(false);
        }

        private void DrawBottomBar()
        {
            ImGui.SetCursorPos(new Vector2(10, ImGui.GetWindowHeight() - 30));

            // Settings Button
            if (uiStyles.IconButton("\uf013", "Settings"))
            {
                plugin.IsSettingsOpen = !plugin.IsSettingsOpen;
            }
            plugin.SettingsButtonPos = ImGui.GetItemRectMin();
            plugin.SettingsButtonSize = ImGui.GetItemRectSize();

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                ImGui.BeginTooltip();
                ImGui.Text("Open Settings Menu.");
                ImGui.Text("You can find options for adjusting your Character Grid.");
                ImGui.Text("As well as the Opt-In for Glamourer Automations.");
                ImGui.EndTooltip();
            }

            ImGui.SameLine();

            // Reorder Button
            if (ImGui.Button("Reorder Characters"))
            {
                reorderWindow.Open();
            }

            ImGui.SameLine();

            // Quick Switch Button
            if (ImGui.Button("Quick Switch"))
            {
                plugin.QuickSwitchWindow.IsOpen = !plugin.QuickSwitchWindow.IsOpen;
            }
            plugin.QuickSwitchButtonPos = ImGui.GetItemRectMin();
            plugin.QuickSwitchButtonSize = ImGui.GetItemRectSize();

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                ImGui.BeginTooltip();
                ImGui.Text("Opens a more compact UI to swap between Characters & Designs.");
                ImGui.EndTooltip();
            }

            ImGui.SameLine();

            // Gallery Button
            if (ImGui.Button("Gallery"))
            {
                plugin.GalleryWindow.IsOpen = !plugin.GalleryWindow.IsOpen;
            }
            plugin.GalleryButtonPos = ImGui.GetItemRectMin();
            plugin.GalleryButtonSize = ImGui.GetItemRectSize();

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                ImGui.BeginTooltip();
                ImGui.Text("Browse the Character Showcase Gallery");
                ImGui.Text("See other players' characters and share your own!");
                ImGui.EndTooltip();
            }

            ImGui.SameLine();

            if (ImGui.Button("Tutorial"))
            {
                plugin.TutorialManager.StartTutorial();
            }
            ImGui.SameLine();

            // Patch Notes Button
            if (ImGui.Button("Patch Notes"))
            {
                plugin.PatchNotesWindow.OpenMainMenuOnClose = false;
                plugin.PatchNotesWindow.IsOpen = !plugin.PatchNotesWindow.IsOpen;
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                ImGui.BeginTooltip();
                ImGui.Text("View what's new in Character Select+");
                ImGui.Text("See the latest features and updates!");
                ImGui.EndTooltip();
            }

            ImGui.SameLine();

            // Random Button
            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button("\uf522##RandomSelect", new Vector2(30, 25)))
            {
                // Trigger dice effect
                Vector2 effectPos = ImGui.GetItemRectMin() + ImGui.GetItemRectSize() / 2;
                diceEffect.Trigger(effectPos, true);

                plugin.SelectRandomCharacterAndDesign();
            }
            ImGui.PopFont();

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                ImGui.BeginTooltip();
                ImGui.Text("Select Random Character & Design");
                if (plugin.Configuration.RandomSelectionFavoritesOnly)
                    ImGui.Text("(Favourites Only)");
                ImGui.EndTooltip();
            }
        }

        private void DrawSupportButton()
        {
            var dpiScale = ImGui.GetIO().DisplayFramebufferScale.X;
            var uiScale = plugin.Configuration.UIScaleMultiplier;
            var totalScale = dpiScale * uiScale;

            Vector2 windowPos = ImGui.GetWindowPos();
            Vector2 windowSize = ImGui.GetWindowSize();
            float buttonWidth = 105 * totalScale;
            float buttonHeight = 25 * totalScale;
            float padding = 5 * totalScale;

            ImGui.SetCursorScreenPos(new Vector2(
                windowPos.X + windowSize.X - buttonWidth - padding,
                windowPos.Y + windowSize.Y - buttonHeight - padding
            ));

            // Start button
            if (ImGui.Button("##SupportDev", new Vector2(buttonWidth, buttonHeight)))
            {
                Dalamud.Utility.Util.OpenLink("https://ko-fi.com/icarusxiv");
            }

            // Icon + text combo
            Vector2 textPos = ImGui.GetItemRectMin() + new Vector2(6 * totalScale, 4 * totalScale);
            ImGui.GetWindowDrawList().AddText(UiBuilder.IconFont, ImGui.GetFontSize(), textPos, ImGui.GetColorU32(ImGuiCol.Text), "\uf004");
            ImGui.GetWindowDrawList().AddText(textPos + new Vector2(22 * totalScale, 0), ImGui.GetColorU32(ImGuiCol.Text), "Support Dev");

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Enjoy Character Select+? Consider supporting development!");
            }
        }

        // Public methods
        public void OpenEditCharacterWindow(int index) => characterForm.OpenEditCharacterWindow(index);
        public void OpenDesignPanel(int characterIndex) => designPanel.Open(characterIndex);
        public void CloseDesignPanel() => designPanel.Close();
        public void SortCharacters() => characterGrid.SortCharacters();
    }
}

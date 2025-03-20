using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using CharacterSelectPlugin.Windows;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using System.Linq;
using Dalamud.Game.Gui;
using System;

namespace CharacterSelectPlugin
{
    public sealed class Plugin : IDalamudPlugin
    {
        [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
        [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] internal static IClientState ClientState { get; private set; } = null!;
        [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
        [PluginService] internal static IPluginLog Log { get; private set; } = null!;
        [PluginService] private static IChatGui ChatGui { get; set; } = null!;



        private const string CommandName = "/select";

        public Configuration Configuration { get; init; }
        public readonly WindowSystem WindowSystem = new("CharacterSelectPlugin");
        private MainWindow MainWindow { get; init; }
        public QuickSwitchWindow QuickSwitchWindow { get; init; } // ✅ New Quick Switch Window


        // Character data storage
        public List<Character> Characters => Configuration.Characters;
        public Vector3 NewCharacterColor { get; set; } = new Vector3(1.0f, 1.0f, 1.0f); // Default to white

        // Temporary fields for adding a new character
        public string NewCharacterName { get; set; } = "";
        public string NewCharacterMacros { get; set; } = "";
        public string? NewCharacterImagePath { get; set; }
        public List<CharacterDesign> NewCharacterDesigns { get; set; } = new();
        public string NewPenumbraCollection { get; set; } = "";
        public string NewGlamourerDesign { get; set; } = "";
        public string NewCustomizeProfile { get; set; } = "";
        public string PluginPath => PluginInterface.GetPluginConfigDirectory();
        public string PluginDirectory => PluginInterface.AssemblyLocation.DirectoryName ?? "";




        public bool IsAddCharacterWindowOpen { get; set; } = false;
        // 🔹 Settings Variables
        public bool IsSettingsOpen { get; set; } = false;  // Tracks if settings panel is open
        public float ProfileImageScale { get; set; } = 1.0f;  // Image scaling (1.0 = normal size)
        public int ProfileColumns { get; set; } = 3;  // Number of profiles per row
        public float ProfileSpacing { get; set; } = 10.0f; // Default spacing



        public Plugin()
        {
            Configuration = Configuration.Load(PluginInterface);
            EnsureConfigurationDefaults();

            try
            {
                var assembly = System.Reflection.Assembly.Load("System.Windows.Forms");
                if (assembly != null)
                {
                    Plugin.Log.Info("✅ System.Windows.Forms successfully loaded.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"❌ Failed to load System.Windows.Forms: {ex.Message}");
            }



            // Initialize the MainWindow and ConfigWindow properly
            MainWindow = new MainWindow(this);
            QuickSwitchWindow = new QuickSwitchWindow(this); // ✅ Add Quick Switch Window


            WindowSystem.AddWindow(MainWindow);
            WindowSystem.AddWindow(QuickSwitchWindow); // ✅ Register the Quick Switch Window



            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the Character Select+ UI"
            });
            CommandManager.AddHandler("/selectswitch", new CommandInfo(OnQuickSwitchCommand)
            {
                HelpMessage = "Opens the Quick Character Switcher UI."
            });


            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += ToggleQuickSwitchUI;
            PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;

            CommandManager.AddHandler("/select", new CommandInfo(OnSelectCommand)
            {
                HelpMessage = "Use /select <Character Name> to apply a profile."
            });

        }
        private void OnQuickSwitchCommand(string command, string args)
        {
            QuickSwitchWindow.IsOpen = !QuickSwitchWindow.IsOpen; // ✅ Toggle Window On/Off
        }
        public void ApplyProfile(Character character, int designIndex)
        {
            if (character == null) return;

            // ✅ Apply the character's macro
            ExecuteMacro(character.Macros);

            // ✅ If a design is selected, apply that too
            if (designIndex >= 0 && designIndex < character.Designs.Count)
            {
                ExecuteMacro(character.Designs[designIndex].Macro);
            }
        }



        private void EnsureConfigurationDefaults()
        {
            bool updated = false;

            // ✅ Keep existing check for IsConfigWindowMovable
            if (Configuration.GetType().GetProperty("IsConfigWindowMovable")?.CanWrite ?? false)
            {
                if (Configuration.GetType().GetProperty("IsConfigWindowMovable")?.GetValue(Configuration) is not bool)
                {
                    Configuration.GetType().GetProperty("IsConfigWindowMovable")?.SetValue(Configuration, true);
                    updated = true;
                }
            }

            // ✅ Fix: Correctly handle nullable values & avoid unboxing issues
            var profileImageScaleProperty = Configuration.GetType().GetProperty("ProfileImageScale");
            if (profileImageScaleProperty != null && profileImageScaleProperty.CanWrite)
            {
                object? value = profileImageScaleProperty.GetValue(Configuration);
                ProfileImageScale = value is float scale ? scale : 1.0f;
            }
            else
            {
                ProfileImageScale = 1.0f;
                updated = true;
            }

            var profileColumnsProperty = Configuration.GetType().GetProperty("ProfileColumns");
            if (profileColumnsProperty != null && profileColumnsProperty.CanWrite)
            {
                object? value = profileColumnsProperty.GetValue(Configuration);
                ProfileColumns = value is int columns ? columns : 3;
            }
            else
            {
                ProfileColumns = 3;
                updated = true;
            }

            var profileSpacingProperty = Configuration.GetType().GetProperty("ProfileSpacing");
            if (profileSpacingProperty != null && profileSpacingProperty.CanWrite)
            {
                object? value = profileSpacingProperty.GetValue(Configuration);
                ProfileSpacing = value is float spacing ? spacing : 10.0f;
            }
            else
            {
                ProfileSpacing = 10.0f;  // ✅ Default value if missing
                updated = true;
            }


            // ✅ Only save if anything was updated
            if (updated) Configuration.Save();
        }



        public void Dispose()
        {
            WindowSystem.RemoveAllWindows();
            MainWindow.Dispose();
            CommandManager.RemoveHandler(CommandName);
        }

        private void OnCommand(string command, string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                // No argument → Open the plugin UI
                ToggleMainUI();
            }
            else
            {
                // Argument given → Try to apply a character profile
                OnSelectCommand(command, args);
            }
        }

        private void OnSelectCommand(string command, string args)
        {
            string[] splitArgs = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

            if (splitArgs.Length < 1)
            {
                ChatGui.PrintError("[Character Select+] Invalid usage. Use /select <Character Name> [Optional Design Name]");
                return;
            }

            string characterName = splitArgs[0];
            string? designName = splitArgs.Length > 1 ? splitArgs[1] : null;

            var character = Characters.FirstOrDefault(c => c.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));

            if (character == null)
            {
                ChatGui.PrintError($"[Character Select+] Character '{characterName}' not found.");
                return;
            }

            if (designName == null)
            {
                // Normal Character Selection
                ChatGui.Print($"[Character Select+] Applying profile: {character.Name}");
                ExecuteMacro(character.Macros);
            }
            else
            {
                // Apply a specific design WITHOUT switching characters
                var design = character.Designs.FirstOrDefault(d => d.Name.Equals(designName, StringComparison.OrdinalIgnoreCase));

                if (design != null)
                {
                    ChatGui.Print($"[Character Select+] Applied design '{designName}' to {character.Name}.");
                    ExecuteMacro(design.Macro);
                }
                else
                {
                    ChatGui.PrintError($"[Character Select+] Design '{designName}' not found for {character.Name}.");
                }
            }
        }



        private void DrawUI() => WindowSystem.Draw();

        public void ToggleQuickSwitchUI() => QuickSwitchWindow.Toggle();
        public void ToggleMainUI() => MainWindow.Toggle();

        public void OpenAddCharacterWindow()
        {
            IsAddCharacterWindowOpen = true;
        }

        public void SaveNewCharacter(string macroToSave)
        {
            if (!string.IsNullOrEmpty(NewCharacterName))
            {
                var newCharacter = new Character(
                    NewCharacterName,
                    macroToSave, // ✅ Preserve Advanced Mode Macro when saving
                    NewCharacterImagePath,
                    new List<CharacterDesign>(NewCharacterDesigns),
                    NewCharacterColor,
                    NewPenumbraCollection,
                    NewGlamourerDesign,
                    NewCustomizeProfile
                );

                // ✅ Auto-create a Design based on Glamourer Design if available
                if (!string.IsNullOrWhiteSpace(NewGlamourerDesign))
                {
                    string defaultDesignName = $"{NewCharacterName} {NewGlamourerDesign}";
                    string defaultMacro = $"/glamour apply {NewGlamourerDesign} | self\n/penumbra redraw self";

                    var defaultDesign = new CharacterDesign(
                        defaultDesignName,
                        defaultMacro,
                        false,  // Not in Advanced Mode
                        ""
                    );

                    newCharacter.Designs.Add(defaultDesign); // ✅ Automatically add the default design
                }

                Configuration.Characters.Add(newCharacter);
                SaveConfiguration();

                // ✅ Reset Fields AFTER Saving
                NewCharacterName = "";
                NewCharacterMacros = ""; // ✅ But don't wipe macros too early!
                NewCharacterImagePath = null;
                NewCharacterDesigns.Clear();
                NewPenumbraCollection = "";
                NewGlamourerDesign = "";
                NewCustomizeProfile = "";
            }
        }






        // ✅ Move Reset Fields into its own function
        private void ResetCharacterFields()
        {
            NewCharacterName = "";
            NewCharacterMacros = "";
            NewCharacterImagePath = null;
            NewCharacterDesigns.Clear(); // ✅ Clears AFTER saving
            NewPenumbraCollection = "";
            NewGlamourerDesign = "";
            NewCustomizeProfile = "";
        }


        public void CloseAddCharacterWindow()
        {
            IsAddCharacterWindowOpen = false;
        }

        /// <summary>
        /// ✅ Executes a macro by sending text commands to the game.
        /// </summary>
        public void ExecuteMacro(string macroText)
        {
            if (string.IsNullOrWhiteSpace(macroText))
                return;

            string[] lines = macroText.Split('\n');
            foreach (var line in lines)
            {
                CommandManager.ProcessCommand(line.Trim());
            }
        }

        // 🔹 FIX: Properly Added SaveConfiguration()
        // 🔹 FIX: Properly Save Profile Spacing
        public void SaveConfiguration()
        {
            var profileImageScaleProperty = Configuration.GetType().GetProperty("ProfileImageScale");
            if (profileImageScaleProperty != null && profileImageScaleProperty.CanWrite)
            {
                profileImageScaleProperty.SetValue(Configuration, ProfileImageScale);
            }

            var profileColumnsProperty = Configuration.GetType().GetProperty("ProfileColumns");
            if (profileColumnsProperty != null && profileColumnsProperty.CanWrite)
            {
                profileColumnsProperty.SetValue(Configuration, ProfileColumns);
            }

            // ✅ ADD THIS for Profile Spacing
            var profileSpacingProperty = Configuration.GetType().GetProperty("ProfileSpacing");
            if (profileSpacingProperty != null && profileSpacingProperty.CanWrite)
            {
                profileSpacingProperty.SetValue(Configuration, ProfileSpacing);
            }

            Configuration.Save();
        }


    }
}

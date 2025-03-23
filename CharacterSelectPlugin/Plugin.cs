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
using CharacterSelectPlugin.Managers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using System.Text.RegularExpressions;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using static FFXIVClientStructs.FFXIV.Client.UI.Misc.BannerHelper.Delegates;
using System.Threading.Tasks;


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
        [PluginService] internal static IFramework Framework { get; private set; } = null!;



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
        public string NewCharacterHonorificTitle { get; set; } = "";
        public string NewCharacterHonorificPrefix { get; set; } = "";
        public string NewCharacterHonorificSuffix { get; set; } = "";
        public Vector3 NewCharacterHonorificColor { get; set; } = new Vector3(1.0f, 1.0f, 1.0f);
        public Vector3 NewCharacterHonorificGlow { get; set; } = new Vector3(0.0f, 0.0f, 0.0f); // Default to no glow
        public string NewCharacterMoodlePreset { get; set; } = "";
        public PoseManager PoseManager { get; private set; } = null!;
        public byte NewCharacterIdlePoseIndex { get; set; } = 0;
        public PoseRestorer PoseRestorer { get; private set; } = null!;








        public bool IsAddCharacterWindowOpen { get; set; } = false;
        // 🔹 Settings Variables
        public bool IsSettingsOpen { get; set; } = false;  // Tracks if settings panel is open
        public float ProfileImageScale { get; set; } = 1.0f;  // Image scaling (1.0 = normal size)
        public int ProfileColumns { get; set; } = 3;  // Number of profiles per row
        public float ProfileSpacing { get; set; } = 10.0f; // Default spacing



        public unsafe Plugin()
        {
            Configuration = Configuration.Load(PluginInterface);
            EnsureConfigurationDefaults();
            Configuration = Configuration.Load(PluginInterface);
            EnsureConfigurationDefaults();

            // 🛠 Patch macros only after loading config + setting defaults
            foreach (var character in Configuration.Characters)
            {
                var newMacro = SanitizeMacro(character.Macros, character);
                if (character.Macros != newMacro)
                    character.Macros = newMacro;
            }

            // Optionally save once
            Configuration.Save();


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

            PoseManager = new PoseManager(ClientState, Framework, ChatGui, CommandManager);
            PoseRestorer = new PoseRestorer(ClientState, this);

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
            ClientState.Login += OnLogin;


            CommandManager.AddHandler("/select", new CommandInfo(OnSelectCommand)
            {
                HelpMessage = "Use /select <Character Name> to apply a profile."
            });
            // Idles
            CommandManager.AddHandler("/spose", new CommandInfo((_, args) =>
            {
                if (byte.TryParse(args, out var poseIndex))
                {
                    PoseManager.ApplyPose(EmoteController.PoseType.Idle, poseIndex);
                }
                else
                {
                    ChatGui.PrintError("[Character Select+] Usage: /spose <0-6>");
                }
            })
            {
                HelpMessage = "Set your character’s Idle pose to a specific index."
            });
            // Chair Sits
            CommandManager.AddHandler("/sitpose", new CommandInfo((_, args) =>
            {
                if (byte.TryParse(args, out var poseIndex))
                {
                    PoseManager.ApplyPose(EmoteController.PoseType.Sit, poseIndex);
                    ChatGui.Print($"[Character Select+] Sitting pose set to {poseIndex}.");
                }
                else
                {
                    ChatGui.PrintError("[Character Select+] Usage: /sitpose <0–6>");
                }
            })
            {
                HelpMessage = "Set your character's Sitting pose to a specific index."
            });
            // Ground Sits
            CommandManager.AddHandler("/groundsitpose", new CommandInfo((_, args) =>
            {
                if (byte.TryParse(args, out var poseIndex))
                {
                    PoseManager.ApplyPose(EmoteController.PoseType.GroundSit, poseIndex);
                    ChatGui.Print($"[Character Select+] Ground Sit pose set to {poseIndex}.");
                }
                else
                {
                    ChatGui.PrintError("[Character Select+] Usage: /groundsitpose <0–6>");
                }
            })
            {
                HelpMessage = "Set your character's Ground Sitting pose to a specific index."
            });
            // Doze Poses
            CommandManager.AddHandler("/dozepose", new CommandInfo((_, args) =>
            {
                if (byte.TryParse(args, out var poseIndex))
                {
                    PoseManager.ApplyPose(EmoteController.PoseType.Doze, poseIndex);
                    ChatGui.Print($"[Character Select+] Dozing pose set to {poseIndex}.");
                }
                else
                {
                    ChatGui.PrintError("[Character Select+] Usage: /dozepose <0–6>");
                }
            })
            {
                HelpMessage = "Set your character's Dozing pose to a specific index."
            });



            CommandManager.AddHandler("/savepose", new CommandInfo((_, _) =>
            {
                var state = PlayerState.Instance();

                // Save current poses into your config
                Configuration.DefaultPoses.Idle = state->SelectedPoses[(int)EmoteController.PoseType.Idle];
                Configuration.DefaultPoses.Sit = state->SelectedPoses[(int)EmoteController.PoseType.Sit];
                Configuration.DefaultPoses.GroundSit = state->SelectedPoses[(int)EmoteController.PoseType.GroundSit];
                Configuration.DefaultPoses.Doze = state->SelectedPoses[(int)EmoteController.PoseType.Doze];

                Configuration.Save();
                ChatGui.Print("[Character Select+] Saved current poses for persistence.");
            })
            {
                HelpMessage = "Saves your current idle/sit/ground/doze poses persistently."
            });

        }
        private void OnLogin()
        {
            Task.Run(() =>
            {
                Task.Delay(1500).Wait(); // Wait for game state to settle
                ApplyStoredPoses();
            });
        }
        private unsafe void ApplyStoredPoses()
        {
            if (ClientState.LocalPlayer == null)
                return;

            var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)ClientState.LocalPlayer.Address;

            PlayerState.Instance()->SelectedPoses[(int)EmoteController.PoseType.Idle] = Configuration.DefaultPoses.Idle;
            PlayerState.Instance()->SelectedPoses[(int)EmoteController.PoseType.Sit] = Configuration.DefaultPoses.Sit;
            PlayerState.Instance()->SelectedPoses[(int)EmoteController.PoseType.GroundSit] = Configuration.DefaultPoses.GroundSit;
            PlayerState.Instance()->SelectedPoses[(int)EmoteController.PoseType.Doze] = Configuration.DefaultPoses.Doze;

            // Only set CPoseState if currently in that pose mode
            byte mode = character->ModeParam;
            EmoteController.PoseType currentType = mode switch
            {
                1 => EmoteController.PoseType.GroundSit,
                2 => EmoteController.PoseType.Sit,
                3 => EmoteController.PoseType.Doze,
                _ => EmoteController.PoseType.Idle,
            };

            byte stored = currentType switch
            {
                EmoteController.PoseType.Idle => Configuration.DefaultPoses.Idle,
                EmoteController.PoseType.Sit => Configuration.DefaultPoses.Sit,
                EmoteController.PoseType.GroundSit => Configuration.DefaultPoses.GroundSit,
                EmoteController.PoseType.Doze => Configuration.DefaultPoses.Doze,
                _ => 0,
            };

            character->EmoteController.CPoseState = stored;
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

            // ✅ Only apply idle pose if it's NOT "None"
            if (character.IdlePoseIndex < 7)
            {
                PoseManager.ApplyPose(EmoteController.PoseType.Idle, character.IdlePoseIndex);
            }
            PoseRestorer.RestorePosesFor(character);

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
            CommandManager.RemoveHandler("/spose");
            CommandManager.RemoveHandler("/savepose");

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
            if (string.IsNullOrWhiteSpace(args))
            {
                ChatGui.PrintError("[Character Select+] Usage: /select <Character Name> [Optional Design Name]");
                return;
            }

            // Match either "quoted strings" or bare words
            var matches = Regex.Matches(args, "\"([^\"]+)\"|\\S+")
                .Cast<Match>()
                .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Value)
                .ToArray();

            if (matches.Length < 1)
            {
                ChatGui.PrintError("[Character Select+] Invalid usage. Use /select <Character Name> [Optional Design Name]");
                return;
            }

            string characterName = matches[0];
            string? designName = matches.Length > 1 ? string.Join(" ", matches.Skip(1)) : null;

            var character = Characters.FirstOrDefault(c => c.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));

            if (character == null)
            {
                ChatGui.PrintError($"[Character Select+] Character '{characterName}' not found.");
                return;
            }

            if (string.IsNullOrWhiteSpace(designName))
            {
                ChatGui.Print($"[Character Select+] Applying profile: {character.Name}");
                ExecuteMacro(character.Macros);
            }
            else
            {
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
                    NewCustomizeProfile,

                    // 🔹 Add Honorific Fields
                    NewCharacterHonorificTitle,
                    NewCharacterHonorificPrefix,
                    NewCharacterHonorificSuffix,
                    NewCharacterHonorificColor,
                    NewCharacterHonorificGlow,
                    NewCharacterMoodlePreset //MOODLES
                )
                {
                    IdlePoseIndex = NewCharacterIdlePoseIndex // ✅ IdLES
                };

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

                // 🔹 Reset Honorific Fields
                NewCharacterHonorificTitle = "";
                NewCharacterHonorificPrefix = "";
                NewCharacterHonorificSuffix = "";
                NewCharacterHonorificColor = new Vector3(1.0f, 1.0f, 1.0f); // Reset to white
                NewCharacterHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f); // Reset to white
                NewCharacterMoodlePreset = ""; //MOODLES
                NewCharacterIdlePoseIndex = 8; // IDLES
            }
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
        public static string SanitizeMacro(string macro, Character character)
        {
            var lines = macro.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();

            void AddOrReplace(string command, string? fullLine = null, bool insertAtTop = false)
            {
                if (!lines.Any(l => l.StartsWith(command, StringComparison.OrdinalIgnoreCase)))
                {
                    if (insertAtTop)
                        lines.Insert(0, fullLine ?? command);
                    else
                        lines.Add(fullLine ?? command);
                }
            }

            // Always ensure these are present
            AddOrReplace("/customize profile disable <me>");
            AddOrReplace("/honorific force clear");
            AddOrReplace("/moodle remove self preset all");

            if (!lines.Any(l => l.Contains("/penumbra redraw self")))
                lines.Add("/penumbra redraw self");

            if (!string.IsNullOrWhiteSpace(character.CustomizeProfile))
            {
                string enableLine = $"/customize profile enable <me>, {character.CustomizeProfile}";
                if (!lines.Any(l => l.Equals(enableLine, StringComparison.OrdinalIgnoreCase)))
                {
                    int disableIndex = lines.FindIndex(l => l.StartsWith("/customize profile disable", StringComparison.OrdinalIgnoreCase));
                    if (disableIndex != -1)
                        lines.Insert(disableIndex + 1, enableLine);
                    else
                        lines.Insert(0, enableLine);
                }
            }

            // ➕ /savepose should go right before /penumbra redraw self
            int redrawIndex = lines.FindIndex(l => l.StartsWith("/penumbra redraw", StringComparison.OrdinalIgnoreCase));
            if (!lines.Any(l => l.StartsWith("/savepose", StringComparison.OrdinalIgnoreCase)))
            {
                if (redrawIndex != -1)
                    lines.Insert(redrawIndex, "/savepose");
                else
                    lines.Add("/savepose");
            }

            return string.Join("\n", lines);
        }


    }
}

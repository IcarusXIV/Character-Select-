using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using CharacterSelectPlugin.Windows;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System;
using CharacterSelectPlugin.Managers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using System.Text.RegularExpressions;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System.Threading.Tasks;
using Dalamud.Plugin.Ipc;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Dalamud.Game.ClientState.Objects;

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
        [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
        [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
        [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;





        private const string CommandName = "/select";

        public Configuration Configuration { get; init; }
        public readonly WindowSystem WindowSystem = new("CharacterSelectPlugin");
        private MainWindow MainWindow { get; init; }
        public QuickSwitchWindow QuickSwitchWindow { get; init; } // ✅ New Quick Switch Window
        public PatchNotesWindow PatchNotesWindow { get; private set; } = null!;
        public RPProfileWindow RPProfileEditor { get; private set; }
        public RPProfileViewWindow RPProfileViewer { get; private set; }



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
        private bool shouldApplyPoses = false;
        private DateTime loginTime;
        public static readonly string CurrentPluginVersion = "1.1.0.2"; // 🧠 Match repo.json and .csproj version
        private ICallGateSubscriber<string, RPProfile>? requestProfile;
        private ICallGateProvider<string, RPProfile>? provideProfile;
        private ContextMenuManager? contextMenuManager;
        private static readonly Dictionary<string, string> ActiveProfilesByPlayerName = new();
        public string NewCharacterTag { get; set; } = "";
        public List<string> KnownTags => Configuration.KnownTags;





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

            // 🔹 Patch existing Design macros to add automation if missing
            foreach (var character in Configuration.Characters)
            {
                foreach (var design in character.Designs)
                {
                    string macroToPatch = design.IsAdvancedMode ? design.AdvancedMacro : design.Macro;

                    if (string.IsNullOrWhiteSpace(macroToPatch))
                        continue;

                    string updated = SanitizeDesignMacro(macroToPatch, design, character, Configuration.EnableAutomations); // <-- pass character too

                    if (updated != macroToPatch)
                    {
                        if (design.IsAdvancedMode)
                            design.AdvancedMacro = updated;
                        else
                            design.Macro = updated;
                    }
                }
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
            QuickSwitchWindow.IsOpen = Configuration.IsQuickSwitchWindowOpen; // ✅ Restore last open state

            RPProfileEditor = new RPProfileWindow(this);
            WindowSystem.AddWindow(RPProfileEditor);

            RPProfileViewer = new RPProfileViewWindow(this);
            WindowSystem.AddWindow(RPProfileViewer);

            // This player REGISTERING their profile, if someone else requests it
            provideProfile = PluginInterface.GetIpcProvider<string, RPProfile>("CharacterSelect.RPProfile.Provide");
            provideProfile.RegisterFunc(HandleProfileRequest);

            // This player SENDING a request to another
            requestProfile = PluginInterface.GetIpcSubscriber<string, RPProfile>("CharacterSelect.RPProfile.Provide");

            // Patch Notes
            PatchNotesWindow = new PatchNotesWindow(this);
            if (Configuration.LastSeenVersion != CurrentPluginVersion)
                PatchNotesWindow.IsOpen = true;
            //PatchNotesWindow.IsOpen = true;

            WindowSystem.AddWindow(MainWindow);
            WindowSystem.AddWindow(QuickSwitchWindow); // ✅ Register the Quick Switch Window
            WindowSystem.AddWindow(PatchNotesWindow); // ✅ Register the Patch Notes Window


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
                // ChatGui.Print("[Character Select+] Saved current poses for persistence.");
            })
            {
                HelpMessage = "Saves your current idle/sit/ground/doze poses persistently."
            });
            
            CommandManager.AddHandler("/viewrp", new CommandInfo(OnViewRPCommand)
            {
                HelpMessage = "View the RP profile of a character (if shared). Usage: /viewrp self | t | First Last@World"
            });
            ClientState.Login += () =>
            {
                Plugin.Log.Debug($"[Character Select+] Local character name: {ClientState.LocalPlayer?.Name.TextValue}");
            };

            contextMenuManager = new ContextMenuManager(this, Plugin.ContextMenu);

        }
        private void OnLogin()
        {
            loginTime = DateTime.Now;
            shouldApplyPoses = true;
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
            if (ClientState.LocalPlayer is { } player && player.HomeWorld.IsValid)
            {
                string localName = player.Name.TextValue;
                string worldName = player.HomeWorld.Value.Name.ToString();
                string fullKey = $"{localName}@{worldName}";
                string newProfileKey = $"{character.Name}@{worldName}";

                // 🧹 Remove all old entries for this player
                var toRemove = ActiveProfilesByPlayerName
                    .Where(kvp => kvp.Key.StartsWith($"{localName}@{worldName}", StringComparison.OrdinalIgnoreCase))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var oldKey in toRemove)
                    ActiveProfilesByPlayerName.Remove(oldKey);

                // ✅ Register the new key → active character mapping
                ActiveProfilesByPlayerName[fullKey] = newProfileKey;
                character.LastInGameName = fullKey;

                Plugin.Log.Debug($"[ApplyProfile] Set LastInGameName = {fullKey} for profile {character.Name}");
                var profileToSend = new RPProfile
                {
                    Pronouns = character.RPProfile?.Pronouns,
                    Gender = character.RPProfile?.Gender,
                    Age = character.RPProfile?.Age,
                    Orientation = character.RPProfile?.Orientation,
                    Relationship = character.RPProfile?.Relationship,
                    Occupation = character.RPProfile?.Occupation,
                    Abilities = character.RPProfile?.Abilities,
                    Bio = character.RPProfile?.Bio,
                    Tags = character.RPProfile?.Tags,
                    CustomImagePath = !string.IsNullOrEmpty(character.RPProfile?.CustomImagePath)
    ? character.RPProfile.CustomImagePath
    : character.ImagePath,
                    ImageZoom = character.RPProfile?.ImageZoom ?? 1.0f,
                    ImageOffset = character.RPProfile?.ImageOffset ?? Vector2.Zero,
                    Sharing = character.RPProfile?.Sharing ?? ProfileSharing.AlwaysShare,
                    ProfileImageUrl = character.RPProfile?.ProfileImageUrl,
                    CharacterName = character.Name, // ✅ force correct name
                    NameplateColor = character.NameplateColor // ✅ force correct color
                };

                _ = Plugin.UploadProfileAsync(profileToSend, character.LastInGameName ?? character.Name);
            }
            SaveConfiguration();
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
            SaveConfiguration();

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
            if (!Configuration.GetType().GetProperties().Any(p => p.Name == nameof(Configuration.EnableAutomations)))
            {
                Configuration.EnableAutomations = false;
                updated = true;
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
            contextMenuManager?.Dispose();

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

            var character = Characters.FirstOrDefault(c =>
    c.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));

            if (character == null)
            {
                ChatGui.PrintError($"[Character Select+] Character '{characterName}' not found.");
                return;
            }

            if (string.IsNullOrWhiteSpace(designName))
            {
                ChatGui.Print($"[Character Select+] Applying profile: {character.Name}");
                ApplyProfile(character, -1); // -1 skips design
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



        private void DrawUI()
        {
            WindowSystem.Draw();

            // Track and persist Quick Switch window state
            bool currentState = QuickSwitchWindow.IsOpen;
            if (Configuration.IsQuickSwitchWindowOpen != currentState)
            {
                Configuration.IsQuickSwitchWindowOpen = currentState;
                Configuration.Save();
            }
            // ✅ Apply stored poses safely once after login
            if (shouldApplyPoses && ClientState.LocalPlayer != null && (DateTime.Now - loginTime).TotalSeconds > 2)
            {
                shouldApplyPoses = false;

                unsafe
                {
                    var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)ClientState.LocalPlayer.Address;

                    PlayerState.Instance()->SelectedPoses[(int)EmoteController.PoseType.Idle] = Configuration.DefaultPoses.Idle;
                    PlayerState.Instance()->SelectedPoses[(int)EmoteController.PoseType.Sit] = Configuration.DefaultPoses.Sit;
                    PlayerState.Instance()->SelectedPoses[(int)EmoteController.PoseType.GroundSit] = Configuration.DefaultPoses.GroundSit;
                    PlayerState.Instance()->SelectedPoses[(int)EmoteController.PoseType.Doze] = Configuration.DefaultPoses.Doze;

                    // ✅ Only set CPoseState if currently in that pose mode
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
            }
        }


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
                var folderToSave = (NewCharacterTag == "All") ? "" : NewCharacterTag;
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
                    IdlePoseIndex = NewCharacterIdlePoseIndex, // ✅ IdLES
                    Tags = string.IsNullOrWhiteSpace(NewCharacterTag)
    ? new List<string>()
    : NewCharacterTag.Split(',').Select(f => f.Trim()).ToList()
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

            lines = lines
       .Where(l =>
           !l.TrimStart().StartsWith("/savepose", StringComparison.OrdinalIgnoreCase) ||
           character.IdlePoseIndex != 7)
       .ToList();


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

            // ✅ Only insert /savepose if idle pose is actually selected
            if (character.IdlePoseIndex != 7)
            {
                // ➕ /savepose should go right before /penumbra redraw self
                int redrawIndex = lines.FindIndex(l => l.StartsWith("/penumbra redraw", StringComparison.OrdinalIgnoreCase));
                if (!lines.Any(l => l.StartsWith("/savepose", StringComparison.OrdinalIgnoreCase)))
                {
                    if (redrawIndex != -1)
                        lines.Insert(redrawIndex, "/savepose");
                    else
                        lines.Add("/savepose");
                }
            }


            return string.Join("\n", lines);
        }
        public static string SanitizeDesignMacro(string macro, CharacterDesign design, Character character, bool enableAutomations)
        {
            var lines = macro.Split('\n').Select(l => l.Trim()).ToList();

            // ➕ Add automation if missing (only if enabled)
            if (enableAutomations &&
                !lines.Any(l => l.StartsWith("/glamour automation enable", StringComparison.OrdinalIgnoreCase)))
            {
                string automationName = string.IsNullOrWhiteSpace(design.Automation) ? "None" : design.Automation;

                int index = lines.FindIndex(l => l.StartsWith("/penumbra redraw", StringComparison.OrdinalIgnoreCase));
                if (index != -1)
                    lines.Insert(index, $"/glamour automation enable {automationName}");
                else
                    lines.Add($"/glamour automation enable {automationName}");
            }

            // 🔧 Fix Customize+ lines to always disable first, then enable (if needed)

            // Remove ALL existing customize lines
            lines.RemoveAll(l => l.StartsWith("/customize profile", StringComparison.OrdinalIgnoreCase));

            // Always insert disable before redraw
            int redrawIndex = lines.FindIndex(l => l.StartsWith("/penumbra redraw", StringComparison.OrdinalIgnoreCase));
            int insertIndex = redrawIndex != -1 ? redrawIndex : lines.Count;
            lines.Insert(insertIndex, "/customize profile disable <me>");

            // Conditionally insert enable right after disable
            string customizeProfile = !string.IsNullOrWhiteSpace(design.CustomizePlusProfile)
                ? design.CustomizePlusProfile
                : character.CustomizeProfile;

            if (!string.IsNullOrWhiteSpace(customizeProfile))
            {
                lines.Insert(insertIndex + 1, $"/customize profile enable <me>, {customizeProfile}");
            }


            return string.Join("\n", lines);
        }

        public void OpenRPProfileWindow(Character character)
        {
            RPProfileViewer.IsOpen = false;                      // ✅ Close first to reset state
            RPProfileViewer.SetCharacter(character);             // ✅ Set new character
            RPProfileViewer.IsOpen = true;                       // ✅ Reopen fresh
        }

        public void OpenRPProfileViewWindow(Character character)
        {
            RPProfileViewer.SetCharacter(character);
            RPProfileViewer.IsOpen = true;
        }
        private RPProfile HandleProfileRequest(string requestedName)
        {
            // If the sender includes "@World", use it as-is
            string lookupKey = requestedName;

            // Otherwise, assume it's FullName and append your own world
            if (!requestedName.Contains('@') && ClientState.LocalPlayer?.HomeWorld.IsValid == true)
            {
                string world = ClientState.LocalPlayer.HomeWorld.Value.Name.ToString();
                lookupKey = $"{requestedName}@{world}";
            }

            if (ActiveProfilesByPlayerName.TryGetValue(lookupKey, out var overrideName))
            {
                lookupKey = overrideName;
            }

            // Find the matching character with LastInGameName == lookupKey
            var character = Characters.FirstOrDefault(c =>
                string.Equals(c.LastInGameName, lookupKey, StringComparison.OrdinalIgnoreCase));

            return character?.RPProfile ?? new RPProfile();
        }


        private async void OnViewRPCommand(string command, string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                ChatGui.PrintError("[Character Select+] Usage: /viewrp <Character Name>");
                return;
            }

            string targetName = args.Trim();

            if (targetName.Equals("self", StringComparison.OrdinalIgnoreCase))
            {
                var me = Plugin.ClientState.LocalPlayer;
                if (me != null && me.HomeWorld.IsValid)
                {
                    var localNameStr = me.Name.TextValue;
                    var worldNameStr = me.HomeWorld.Value.Name.ToString();
                    targetName = $"{localNameStr}@{worldNameStr}";
                }
            }
            if (targetName.Equals("<t>", StringComparison.OrdinalIgnoreCase) || targetName.Equals("t", StringComparison.OrdinalIgnoreCase))
            {
                var rawTarget = TargetManager.Target;

                if (rawTarget == null)
                {
                    ChatGui.PrintError("[Character Select+] You are not targeting anything.");
                    return;
                }

                ChatGui.Print($"[DEBUG] Target kind: {rawTarget.ObjectKind}, Name: {rawTarget.Name}");

                if (rawTarget.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
                {
                    ChatGui.PrintError("[Character Select+] You must target a player.");
                    return;
                }

                string name = rawTarget.Name.ToString();
                string world = ClientState.LocalPlayer?.HomeWorld.Value.Name.ToString() ?? "Unknown";

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(world))
                {
                    ChatGui.PrintError("[Character Select+] Could not resolve target's full name.");
                    return;
                }

                targetName = $"{name}@{world}";
            }


            ChatGui.Print($"[Character Select+] Looking for {targetName}'s profile");

            // ✅ Try to get local name first
            string? localName = ClientState.LocalPlayer?.Name.TextValue;

            // ✅ If player is trying to view their own profile, skip IPC
            if (ActiveProfilesByPlayerName.TryGetValue(targetName, out var overrideName))
            {
                var character = Characters.FirstOrDefault(c =>
                    string.Equals(c.LastInGameName, overrideName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Name, overrideName, StringComparison.OrdinalIgnoreCase));

                if (character?.RPProfile == null || character.RPProfile.IsEmpty())
                {
                    ChatGui.PrintError($"[Character Select+] No RP profile set for {overrideName}.");
                    return;
                }

                RPProfileViewer.SetCharacter(character);
                RPProfileViewer.IsOpen = true;
                return;
            }
            else if (!string.IsNullOrEmpty(ClientState.LocalPlayer?.Name.TextValue) &&
                     ClientState.LocalPlayer?.Name.TextValue.Equals(targetName, StringComparison.OrdinalIgnoreCase) == true)
            {

                var match = Characters.FirstOrDefault(c => c.LastInGameName != null &&
                                                           c.LastInGameName.Equals(localName, StringComparison.OrdinalIgnoreCase));
                if (match == null || match.RPProfile == null || match.RPProfile.IsEmpty())
                {
                    ChatGui.PrintError("[DEBUG] No matching Character Select+ profile or RPProfile found.");
                    return;
                }

                RPProfileViewer.SetCharacter(match);
                RPProfileViewer.IsOpen = true;
                return;
            }

            // ✅ Only hits this if not matched above
            try
            {
                var profile = await DownloadProfileAsync(targetName);

                if (profile != null && !profile.IsEmpty())
                {
                    RPProfileViewer.SetExternalProfile(profile);
                    RPProfileViewer.IsOpen = true;
                    ChatGui.Print($"[Character Select+] Received RP profile from {targetName}.");
                }
                else
                {
                    ChatGui.Print($"[Character Select+] {targetName} is currently not sharing their RP Profile or has not yet created one.");
                }
            }
            catch (Exception ex)
            {
                ChatGui.PrintError($"[Character Select+] IPC request failed: {ex.Message}");
            }
        }
        public void SetActiveCharacter(Character character)
        {
            if (ClientState.LocalPlayer is { } player && player.HomeWorld.IsValid)
            {
                string localName = player.Name.TextValue;
                string worldName = player.HomeWorld.Value.Name.ToString();
                string fullKey = $"{localName}@{worldName}";

                ActiveProfilesByPlayerName[fullKey] = character.Name;
                character.LastInGameName = fullKey;

                Plugin.Log.Debug($"[SetActiveCharacter] Set LastInGameName = {fullKey} for profile {character.Name}");

                // ✅ THIS is the correct upload:
                var profileToSend = new RPProfile
                {
                    Pronouns = character.RPProfile?.Pronouns,
                    Gender = character.RPProfile?.Gender,
                    Age = character.RPProfile?.Age,
                    Orientation = character.RPProfile?.Orientation,
                    Relationship = character.RPProfile?.Relationship,
                    Occupation = character.RPProfile?.Occupation,
                    Abilities = character.RPProfile?.Abilities,
                    Bio = character.RPProfile?.Bio,
                    Tags = character.RPProfile?.Tags,
                    CustomImagePath = !string.IsNullOrEmpty(character.RPProfile?.CustomImagePath)
    ? character.RPProfile.CustomImagePath
    : character.ImagePath,
                    ImageZoom = character.RPProfile?.ImageZoom ?? 1.0f,
                    ImageOffset = character.RPProfile?.ImageOffset ?? Vector2.Zero,
                    Sharing = character.RPProfile?.Sharing ?? ProfileSharing.AlwaysShare,
                    ProfileImageUrl = character.RPProfile?.ProfileImageUrl,
                    CharacterName = character.Name, // ✅ force correct name
                    NameplateColor = character.NameplateColor // ✅ force correct color
                };

                _ = Plugin.UploadProfileAsync(profileToSend, character.LastInGameName ?? character.Name);
            }
        }
        public async Task TryRequestRPProfile(string targetName)
        {
            var profile = await DownloadProfileAsync(targetName);

            if (profile != null && !profile.IsEmpty())
            {
                RPProfileViewer.SetExternalProfile(profile);
                RPProfileViewer.IsOpen = true;
                ChatGui.Print($"[Character Select+] Received RP profile for {targetName}.");
            }
            else
            {
                ChatGui.Print($"[Character Select+] No shared RP profile found for {targetName}.");
            }
        }

        public static async Task UploadProfileAsync(RPProfile profile, string characterName)
        {
            try
            {
                using var http = new HttpClient();
                using var form = new MultipartFormDataContent();

                // 🔍 Get character match from config
                var config = PluginInterface.GetPluginConfig() as Configuration;
                Character? match = config?.Characters.FirstOrDefault(c => c.LastInGameName == characterName);

                if (match != null)
                {
                    profile.CharacterName ??= match.Name;

                    if (profile.NameplateColor.X <= 0f && profile.NameplateColor.Y <= 0f && profile.NameplateColor.Z <= 0f)
                        profile.NameplateColor = match.NameplateColor;

                    // ✅ Only overwrite if NOT set
                    if (Math.Abs(profile.ImageZoom) < 0.01f)
                        profile.ImageZoom = match.RPProfile.ImageZoom;

                    if (profile.ImageOffset == Vector2.Zero)
                        profile.ImageOffset = match.RPProfile.ImageOffset;
                }

                // 🧠 Determine correct image to upload
                string? imagePathToUpload = null;

                if (!string.IsNullOrEmpty(profile.CustomImagePath) && File.Exists(profile.CustomImagePath))
                {
                    imagePathToUpload = profile.CustomImagePath; // ✅ FIXED LINE
                }
                else if (!string.IsNullOrEmpty(match?.ImagePath) && File.Exists(match.ImagePath))
                {
                    imagePathToUpload = match.ImagePath;
                }

                // ✅ Attach image if found
                if (!string.IsNullOrEmpty(imagePathToUpload) && File.Exists(imagePathToUpload))
                {
                    var imageStream = File.OpenRead(imagePathToUpload);
                    var imageContent = new StreamContent(imageStream);
                    imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");

                    form.Add(imageContent, "image", $"{Guid.NewGuid()}.png");
                }

                // 📨 Upload JSON
                string json = JsonConvert.SerializeObject(profile);
                form.Add(new StringContent(json, Encoding.UTF8, "application/json"), "profile");

                // 🌐 Send
                string urlSafeName = Uri.EscapeDataString(characterName);
                var response = await http.PostAsync($"https://character-select-profile-server-production.up.railway.app/upload/{urlSafeName}", form);

                var responseJson = await response.Content.ReadAsStringAsync();
                var updated = JsonConvert.DeserializeObject<RPProfile>(responseJson);
                if (updated?.ProfileImageUrl is { Length: > 0 })
                    profile.ProfileImageUrl = updated.ProfileImageUrl;
                if (match?.RPProfile != null && updated?.ProfileImageUrl is { Length: > 0 })
                {
                    match.RPProfile.ProfileImageUrl = updated.ProfileImageUrl;
                    Plugin.Log.Debug($"[UploadProfile] Updated ProfileImageUrl for {characterName} = {updated.ProfileImageUrl}");
                    config?.Save();
                }

                if (!response.IsSuccessStatusCode)
                    Plugin.Log.Warning($"[UploadProfile] Failed to upload profile for {characterName}: {response.StatusCode}");
                else
                    Plugin.Log.Debug($"[UploadProfile] Successfully uploaded profile for {characterName}");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[UploadProfile] Exception: {ex.Message}");
            }
        }


        public static async Task<RPProfile?> FetchProfileAsync(string characterName)
        {
            try
            {
                using var http = new HttpClient();
                string urlSafeName = Uri.EscapeDataString(characterName);
                var response = await http.GetAsync($"https://character-select-profile-server-production.up.railway.app/profiles/{urlSafeName}");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return RPProfileJson.Deserialize(json);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[FetchProfile] Error fetching profile for {characterName}: {ex.Message}");
            }

            return null;
        }
        public static async Task<RPProfile?> DownloadProfileAsync(string characterName)
        {
            try
            {
                using var http = new HttpClient();
                string urlSafeName = Uri.EscapeDataString(characterName);

                var response = await http.GetAsync($"https://character-select-profile-server-production.up.railway.app/view/{urlSafeName}");
                if (!response.IsSuccessStatusCode)
                {
                    Plugin.Log.Warning($"[DownloadProfile] Profile not found for {characterName}");
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync();
                return RPProfileJson.Deserialize(json);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[DownloadProfile] Exception: {ex.Message}");
                return null;
            }
        }
        public static string GenerateTargetMacro(string original)
        {
            var lines = original.Split('\n');
            var result = new List<string>();

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // 🚫 Skip lines that should never apply to targets
                if (
                    line.StartsWith("/customize profile disable", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("/honorific", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("/moodle", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("/spose", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("/savepose", StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue; // Omit this line entirely
                }

                // 🎯 Rewriting self-targeting lines to <t>
                bool shouldTarget =
                    line.Contains("/penumbra", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("/glamour", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("/customize profile enable", StringComparison.OrdinalIgnoreCase);

                if (shouldTarget)
                {
                    line = line
                        .Replace(" self", " <t>")
                        .Replace(" Self", " <t>")
                        .Replace("| self", "| <t>")
                        .Replace("| Self", "| <t>")
                        .Replace("<me>", "<t>");
                }

                // 🎯 Specific override
                if (line.StartsWith("/penumbra redraw", StringComparison.OrdinalIgnoreCase))
                    line = "/penumbra redraw target";

                result.Add(line);
            }

            return string.Join("\n", result);
        }


    }
}

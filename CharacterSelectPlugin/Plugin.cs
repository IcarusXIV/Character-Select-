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
using static FFXIVClientStructs.FFXIV.Client.Game.Control.EmoteController;

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


        public static readonly string CurrentPluginVersion = "1.1.1.1"; // 🧠 Match repo.json and .csproj version


        private const string CommandName = "/select";

        public Configuration Configuration { get; init; }
        public readonly WindowSystem WindowSystem = new("CharacterSelectPlugin");
        private MainWindow MainWindow { get; init; }
        public QuickSwitchWindow QuickSwitchWindow { get; set; } // ✅ New Quick Switch Window
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

        private ICallGateSubscriber<string, RPProfile>? requestProfile;
        private ICallGateProvider<string, RPProfile>? provideProfile;
        private ContextMenuManager? contextMenuManager;
        private static readonly Dictionary<string, string> ActiveProfilesByPlayerName = new();
        public string NewCharacterTag { get; set; } = "";
        public List<string> KnownTags => Configuration.KnownTags;
        public string NewCharacterAutomation { get; set; } = "";
        private int framesSinceLogin = 0;
        internal byte lastPoseAppliedByPlugin = 255;
        internal byte lastIdlePoseForcedByPlugin = 255;
        internal bool shouldReapplyPoseForLogin = false;
        public byte LastIdlePoseAppliedByPlugin { get; set; } = 255;
        internal byte lastSeenIdlePose = 255;
        internal int suppressIdleSaveForFrames = 0;
        internal byte lastSeenSitPose = 255;
        internal byte lastSeenGroundSitPose = 255;
        internal byte lastSeenDozePose = 255;

        internal int suppressSitSaveForFrames = 0;
        internal int suppressGroundSitSaveForFrames = 0;
        internal int suppressDozeSaveForFrames = 0;
        private bool pendingCharacterAutoload = false;
        private int characterAutoloadFrameDelay = 0;
        private DateTime pluginInitTime = DateTime.Now;
        private bool hasLoggedInOnce = false;
        private bool hasAutoAppliedCharacter = false;
        private string currentSessionId = "";
        private static string CurrentSessionId = Guid.NewGuid().ToString();
        private static string SessionPath => Path.Combine(PluginInterface.ConfigDirectory.FullName, "last_session.txt");
        private static string SessionInfoPath => Path.Combine(PluginInterface.GetPluginConfigDirectory(), "session_info.txt");
        private string? lastAppliedCharacter = null;
        public float UIScaleMultiplier => Configuration.UIScaleMultiplier;
        private bool restoredLastInGameName = false;
        private string? _pendingSessionCharacterName = null;




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
            // ✅ SortOrder fallback (put this RIGHT HERE ⬇️)
            if (Configuration.CurrentSortIndex == (int)MainWindow.SortType.Manual)
            {
                for (int i = 0; i < Configuration.Characters.Count; i++)
                {
                    if (Configuration.Characters[i].SortOrder == 0 && i != 0)
                    {
                        Configuration.Characters[i].SortOrder = i;
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

            PoseManager = new PoseManager(ClientState, Framework, ChatGui, CommandManager, this);
            PoseRestorer = new PoseRestorer(ClientState, this);

            // Initialize the MainWindow and ConfigWindow properly
            MainWindow = new MainWindow(this);
            MainWindow.SortCharacters();
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
            Framework.Update += FrameworkUpdate;
            string sessionFilePath = Path.Combine(PluginInterface.GetPluginConfigDirectory(), "boot_session.txt");

            // 🧠 Only generate a new session ID if the file does not exist
            if (!File.Exists(sessionFilePath))
            {
                this.currentSessionId = Guid.NewGuid().ToString();
                File.WriteAllText(sessionFilePath, currentSessionId);
            }
            else
            {
                this.currentSessionId = File.ReadAllText(sessionFilePath).Trim();
            }
            if (File.Exists(SessionInfoPath))
            {
                string nameFromFile = File.ReadAllText(SessionInfoPath).Trim();
                Plugin.Log.Debug($"[Startup] Found session_info.txt = {nameFromFile}");
                _pendingSessionCharacterName = nameFromFile; // ✅ NEW FIELD
            }
            else
            {
                Plugin.Log.Debug("[Startup] No session_info.txt found.");
            }


            CommandManager.AddHandler("/select", new CommandInfo(OnSelectCommand)
            {
                HelpMessage = "Use /select <Character Name> to apply a profile."
            });
            // Idles
            CommandManager.AddHandler("/sidle", new CommandInfo((_, args) =>
            {
                if (byte.TryParse(args, out var poseIndex))
                {
                    PoseManager.ApplyPose(EmoteController.PoseType.Idle, poseIndex);
                    ExecuteMacro("/penumbra redraw self");
                }
                else
                {
                    ChatGui.PrintError("[Character Select+] Usage: /sidle <0-6>");
                }
            })
            {
                HelpMessage = "Set your character’s Idle pose to a specific index."
            });
            // Chair Sits
            CommandManager.AddHandler("/ssit", new CommandInfo((_, args) =>
            {
                if (byte.TryParse(args, out var poseIndex))
                {
                    PoseManager.ApplyPose(EmoteController.PoseType.Sit, poseIndex);
                    ExecuteMacro("/penumbra redraw self");
                }
                else
                {
                    ChatGui.PrintError("[Character Select+] Usage: /ssit <0–6>");
                }
            })
            {
                HelpMessage = "Set your character's Sitting pose to a specific index."
            });
            // Ground Sits
            CommandManager.AddHandler("/sgroundsit", new CommandInfo((_, args) =>
            {
                if (byte.TryParse(args, out var poseIndex))
                {
                    PoseManager.ApplyPose(EmoteController.PoseType.GroundSit, poseIndex);
                    ExecuteMacro("/penumbra redraw self");
                }
                else
                {
                    ChatGui.PrintError("[Character Select+] Usage: /sgroundsit <0–6>");
                }
            })
            {
                HelpMessage = "Set your character's Ground Sitting pose to a specific index."
            });
            // Doze Poses
            CommandManager.AddHandler("/sdoze", new CommandInfo((_, args) =>
            {
                if (byte.TryParse(args, out var poseIndex))
                {
                    PoseManager.ApplyPose(EmoteController.PoseType.Doze, poseIndex);
                    ExecuteMacro("/penumbra redraw self");
                }
                else
                {
                    ChatGui.PrintError("[Character Select+] Usage: /sdoze <0–6>");
                }
            })
            {
                HelpMessage = "Set your character's Dozing pose to a specific index."
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
            this.CleanupUnusedProfileImages();

        }

        private void OnLogin()
        {
            if (ClientState.LocalPlayer == null || !ClientState.IsLoggedIn)
            {
                Plugin.Log.Debug("[OnLogin] Ignored – LocalPlayer is null or not logged in.");
                return;
            }

            loginTime = DateTime.Now;
            shouldApplyPoses = true;
            suppressIdleSaveForFrames = 60;
        }


        private unsafe void ApplyStoredPoses()
        {
            if (ClientState.LocalPlayer?.Address is not nint address || address == IntPtr.Zero)
                return;

            var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)ClientState.LocalPlayer.Address;
            if (character == null)
                return;

            // Get current poses
            byte currentIdle = PlayerState.Instance()->SelectedPoses[(int)EmoteController.PoseType.Idle];
            byte currentSit = PlayerState.Instance()->SelectedPoses[(int)EmoteController.PoseType.Sit];
            byte currentGroundSit = PlayerState.Instance()->SelectedPoses[(int)EmoteController.PoseType.GroundSit];
            byte currentDoze = PlayerState.Instance()->SelectedPoses[(int)EmoteController.PoseType.Doze];

            byte savedIdle = Configuration.DefaultPoses.Idle;
            byte lastPluginIdle = Configuration.LastIdlePoseAppliedByPlugin;
            Plugin.Log.Debug($"[ApplyStoredPoses] SavedIdle = {savedIdle}, LastPluginIdle = {lastPluginIdle}");
            if (savedIdle < 7 && savedIdle == lastPluginIdle)
            {
                PlayerState.Instance()->SelectedPoses[(int)EmoteController.PoseType.Idle] = savedIdle;

                if (TranslatePoseState(character->ModeParam) == EmoteController.PoseType.Idle)
                    character->EmoteController.CPoseState = savedIdle;
            }

            byte savedSit = Configuration.DefaultPoses.Sit;
            if (savedSit < 7)
                PlayerState.Instance()->SelectedPoses[(int)EmoteController.PoseType.Sit] = savedSit;

            byte savedGroundSit = Configuration.DefaultPoses.GroundSit;
            if (savedGroundSit < 7)
                PlayerState.Instance()->SelectedPoses[(int)EmoteController.PoseType.GroundSit] = savedGroundSit;

            byte savedDoze = Configuration.DefaultPoses.Doze;
            if (savedDoze < 7)
                PlayerState.Instance()->SelectedPoses[(int)EmoteController.PoseType.Doze] = savedDoze;

            // Set CPoseState if the current mode matches the type we're restoring
            var currentType = TranslatePoseState(character->ModeParam);

            byte currentSelected = PlayerState.Instance()->SelectedPoses[(int)currentType];
            byte intended = currentType switch
            {
                EmoteController.PoseType.Idle => savedIdle,
                EmoteController.PoseType.Sit => savedSit,
                EmoteController.PoseType.GroundSit => savedGroundSit,
                EmoteController.PoseType.Doze => savedDoze,
                _ => 255
            };

            // ✅ Force CPoseState to match current selected pose
            // (only if plugin isn’t trying to override it)
            if (currentSelected < 7)
                character->EmoteController.CPoseState = currentSelected;
        }

        private void OnQuickSwitchCommand(string command, string args)
        {
            QuickSwitchWindow.IsOpen = !QuickSwitchWindow.IsOpen; // ✅ Toggle Window On/Off
        }
        public void ApplyProfile(Character character, int designIndex)
        {
            // 🧱 ABSOLUTE FAIL-SAFE: Do nothing if world isn’t ready
            if (!ClientState.IsLoggedIn ||
                ClientState.TerritoryType == 0 ||
                ClientState.LocalPlayer == null ||
                string.IsNullOrEmpty(ClientState.LocalPlayer.Name.TextValue) ||
                !ClientState.LocalPlayer.HomeWorld.IsValid)
            {
                Plugin.Log.Debug("[ApplyProfile] Skipped: Player not fully loaded.");
                return;
            }
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
                string pluginCharacterKey = $"{character.Name}@{worldName}"; // plugin character identity
                character.LastInGameName = $"{localName}@{worldName}";        // who is currently logged in

                Configuration.LastUsedCharacterByPlayer[fullKey] = pluginCharacterKey;
                Configuration.Save();

                Plugin.Log.Debug($"[ApplyProfile] Saved: {fullKey} → {pluginCharacterKey}");
                Plugin.Log.Debug($"[SetActiveCharacter] Updated LastUsedCharacterKey = {fullKey}");
                Plugin.Log.Debug($"[ApplyProfile] Set LastInGameName = {character.LastInGameName} for profile {character.Name}");


                Plugin.Log.Debug($"[ApplyProfile] Set LastInGameName = {fullKey} for profile {character.Name}");
                var profileToSend = new RPProfile
                {
                    Pronouns = character.RPProfile?.Pronouns,
                    Gender = character.RPProfile?.Gender,
                    Age = character.RPProfile?.Age,
                    Race = character.RPProfile?.Race,
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
                PoseManager.ApplyPose(PoseType.Idle, character.IdlePoseIndex);
                Configuration.LastIdlePoseAppliedByPlugin = character.IdlePoseIndex;
                Configuration.Save(); // 💾 save so it survives logout
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
            contextMenuManager?.Dispose();
            Framework.Update += FrameworkUpdate;
            try
            {
                string sessionFilePath = Path.Combine(PluginInterface.GetPluginConfigDirectory(), "boot_session.txt");
                if (File.Exists(sessionFilePath))
                {
                    File.Delete(sessionFilePath);
                    Plugin.Log.Debug("[Dispose] Deleted boot_session.txt for next launch.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"[Dispose] Failed to delete boot_session.txt: {ex.Message}");
            }
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
                    NewCharacterMoodlePreset, //MOODLES
                    NewCharacterAutomation // Glamourer Automations
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
                    var defaultDesign = new CharacterDesign(
                    defaultDesignName,
                    "",  // macro will be filled below
                    false,
                    ""
                    );

                    // ✅ Sanitize to include Automation fallback
                    defaultDesign.Macro = SanitizeDesignMacro(
                        $"/glamour apply {NewGlamourerDesign} | self\n/penumbra redraw self",
                        defaultDesign,
                        newCharacter,
                        Configuration.EnableAutomations
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
                NewCharacterAutomation = ""; //AUTOMATIONS
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

            // ✅ Remove all /savepose lines entirely — it's deprecated
            lines = lines
                .Where(l => !l.TrimStart().StartsWith("/savepose", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // ✅ Insert /glamour automation enable {X} after last /glamour apply
            if (PluginInterface.GetPluginConfig() is Configuration config && config.EnableAutomations)
            {
                string automation = string.IsNullOrWhiteSpace(character.CharacterAutomation) ? "None" : character.CharacterAutomation.Trim();

                if (!lines.Any(l => l.StartsWith("/glamour automation enable", StringComparison.OrdinalIgnoreCase)))
                {
                    int lastGlamourIndex = -1;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (lines[i].StartsWith("/glamour apply", StringComparison.OrdinalIgnoreCase))
                            lastGlamourIndex = i;
                    }

                    string automationLine = $"/glamour automation enable {automation}";

                    if (lastGlamourIndex != -1)
                        lines.Insert(lastGlamourIndex + 1, automationLine); // Insert after the last /glamour apply
                    else
                        lines.Insert(0, automationLine); // Fallback
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
            // ✅ Migrate old pose commands to new ones
            for (int i = 0; i < lines.Count; i++)
            {
                lines[i] = lines[i]
                    .Replace("/spose", "/sidle")
                    .Replace("/sitpose", "/ssit")
                    .Replace("/groundsitpose", "/sgroundsit")
                    .Replace("/dozepose", "/sdoze");
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
                string automationName = !string.IsNullOrWhiteSpace(design.Automation)
                    ? design.Automation
                    : (!string.IsNullOrWhiteSpace(character.CharacterAutomation)
                        ? character.CharacterAutomation
                        : "None");

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
    string.Equals(c.Name, overrideName, StringComparison.OrdinalIgnoreCase));


                if (character?.RPProfile == null || character.RPProfile.IsEmpty())
                {
                    ChatGui.PrintError($"[Character Select+] No RP profile set for {targetName}.");
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
            Plugin.Log.Debug("[SetActiveCharacter] CALLED");

            if (ClientState.LocalPlayer is { } player && player.HomeWorld.IsValid)
            {
                string localName = player.Name.TextValue;
                string worldName = player.HomeWorld.Value.Name.ToString();
                string fullKey = $"{localName}@{worldName}"; // Who is logged in
                string pluginCharacterKey = $"{localName}@{worldName}"; // Who was selected

                ActiveProfilesByPlayerName[fullKey] = character.Name;

                // ✅ This is what OnLogin uses to find the right profile
                character.LastInGameName = pluginCharacterKey;

                // ✅ This is the key logic: player → selected plugin character
                Configuration.LastUsedCharacterByPlayer[fullKey] = pluginCharacterKey;

                // 📝 Write the session info file so the plugin remembers the last applied character name
                File.WriteAllText(SessionInfoPath, character.Name);
                Plugin.Log.Debug($"[ApplyProfile] 📝 Wrote session_info.txt = {character.Name}");

                Configuration.Save();

                // ✅ These log lines now match and won’t be skipped
                Plugin.Log.Debug($"[SetActiveCharacter] Saved: {fullKey} → {pluginCharacterKey}");
                Plugin.Log.Debug($"[SetActiveCharacter] Set LastInGameName = {pluginCharacterKey} for profile {character.Name}");


                // ✅ THIS is the correct upload:
                var profileToSend = new RPProfile
                {
                    Pronouns = character.RPProfile?.Pronouns,
                    Gender = character.RPProfile?.Gender,
                    Age = character.RPProfile?.Age,
                    Race = character.RPProfile?.Race,
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
                    line.StartsWith("/spose", StringComparison.OrdinalIgnoreCase)
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
        public void CleanupUnusedProfileImages()
        {
            try
            {
                var dir = PluginInterface.GetPluginConfigDirectory();
                if (!Directory.Exists(dir))
                    return;

                var allFiles = Directory.GetFiles(dir, "RPImage_*.png");
                var usedPaths = this.Characters
                    .Select(c => c.RPProfile?.ProfileImageUrl)
                    .Where(url => !string.IsNullOrEmpty(url))
                    .Select(url =>
                    {
                        var hash = Convert.ToBase64String(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(url!)))
                            .Replace("/", "_").Replace("+", "-");
                        return Path.Combine(dir, $"RPImage_{hash}.png");
                    }).ToHashSet();

                foreach (var file in allFiles)
                {
                    if (!usedPaths.Contains(file))
                    {
                        File.Delete(file);
                        Log.Debug($"[Cleanup] Deleted orphaned image: {file}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Cleanup] Failed to clean up profile images: {ex.Message}");
            }
        }
        private void FrameworkUpdate(IFramework framework)
        {
            if (!ClientState.IsLoggedIn || ClientState.LocalPlayer == null)
                return;

            unsafe
            {
                var state = PlayerState.Instance();
                if (state == null || (nint)state == IntPtr.Zero)
                    return;

                try
                {
                    // === IDLE POSE ===
                    var currentIdle = state->SelectedPoses[(int)EmoteController.PoseType.Idle];
                    if (lastSeenIdlePose == 255)
                        lastSeenIdlePose = currentIdle;

                    if (suppressIdleSaveForFrames > 0)
                        suppressIdleSaveForFrames--;
                    else if (currentIdle != lastSeenIdlePose && currentIdle < 7 && currentIdle != Configuration.LastIdlePoseAppliedByPlugin)
                    {
                        Configuration.DefaultPoses.Idle = currentIdle;
                        Configuration.LastIdlePoseAppliedByPlugin = currentIdle;
                        Configuration.Save();
                        Plugin.Log.Debug($"[AutoSave] Detected manual idle change to {currentIdle}, saved.");
                    }
                    lastSeenIdlePose = currentIdle;

                    // === SIT POSE ===
                    var currentSit = state->SelectedPoses[(int)EmoteController.PoseType.Sit];
                    if (lastSeenSitPose == 255)
                        lastSeenSitPose = currentSit;

                    if (suppressSitSaveForFrames > 0)
                        suppressSitSaveForFrames--;
                    else if (currentSit != lastSeenSitPose && currentSit < 7)
                    {
                        Configuration.DefaultPoses.Sit = currentSit;
                        Configuration.Save();
                        Plugin.Log.Debug($"[AutoSave] Detected manual sit change to {currentSit}, saved.");
                    }
                    lastSeenSitPose = currentSit;

                    // === GROUNDSIT POSE ===
                    var currentGroundSit = state->SelectedPoses[(int)EmoteController.PoseType.GroundSit];
                    if (lastSeenGroundSitPose == 255)
                        lastSeenGroundSitPose = currentGroundSit;

                    if (suppressGroundSitSaveForFrames > 0)
                        suppressGroundSitSaveForFrames--;
                    else if (currentGroundSit != lastSeenGroundSitPose && currentGroundSit < 7)
                    {
                        Configuration.DefaultPoses.GroundSit = currentGroundSit;
                        Configuration.Save();
                        Plugin.Log.Debug($"[AutoSave] Detected manual ground sit change to {currentGroundSit}, saved.");
                    }
                    lastSeenGroundSitPose = currentGroundSit;

                    // === DOZE POSE ===
                    var currentDoze = state->SelectedPoses[(int)EmoteController.PoseType.Doze];
                    if (lastSeenDozePose == 255)
                        lastSeenDozePose = currentDoze;

                    if (suppressDozeSaveForFrames > 0)
                        suppressDozeSaveForFrames--;
                    else if (currentDoze != lastSeenDozePose && currentDoze < 7)
                    {
                        Configuration.DefaultPoses.Doze = currentDoze;
                        Configuration.Save();
                        Plugin.Log.Debug($"[AutoSave] Detected manual doze change to {currentDoze}, saved.");
                    }
                    lastSeenDozePose = currentDoze;
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"[CrashPrevent] Exception while accessing PlayerState: {ex.Message}");
                }
            }

            if (ClientState.LocalPlayer is { } player && player.HomeWorld.IsValid && ClientState.IsLoggedIn)
            {
                string world = player.HomeWorld.Value.Name.ToString();
                string fullKey = $"{player.Name.TextValue}@{world}";

                if (!Configuration.EnableLastUsedCharacterAutoload)
                    return;
                if (Configuration.EnableLastUsedCharacterAutoload &&
                    lastAppliedCharacter != fullKey &&
                    ClientState.TerritoryType != 0 &&
                    DateTime.Now - loginTime > TimeSpan.FromSeconds(3)) // give a short delay to load
                {
                    if (Configuration.LastUsedCharacterByPlayer.TryGetValue(fullKey, out var lastUsedKey))
                    {
                        var character = Characters.FirstOrDefault(c =>
                            $"{c.Name}@{ClientState.LocalPlayer!.HomeWorld.Value.Name}" == lastUsedKey);

                        if (character != null)
                        {
                            Plugin.Log.Debug($"[AutoLoad] ✅ Applying {character.Name} for {fullKey}");
                            ApplyProfile(character, -1);
                            lastAppliedCharacter = fullKey; // ✅ mark it
                        }
                        else if (lastAppliedCharacter != $"!notfound:{lastUsedKey}")
                        {
                            Plugin.Log.Debug($"[AutoLoad] ❌ No match found for {lastUsedKey}");
                            lastAppliedCharacter = $"!notfound:{lastUsedKey}"; // mark it so it doesn't log again
                        }
                    }
                    else
                    {
                        Plugin.Log.Debug($"[AutoLoad] ❌ No previous character stored for {fullKey}");
                    }
                }
            }
            // ✅ Handle deferred ApplyProfile from session_info.txt
            if (_pendingSessionCharacterName != null &&
                ClientState.IsLoggedIn &&
                ClientState.LocalPlayer != null &&
                ClientState.TerritoryType != 0 &&
                DateTime.Now - loginTime > TimeSpan.FromSeconds(3))
            {
                var character = Characters.FirstOrDefault(c =>
                    c.Name.Equals(_pendingSessionCharacterName, StringComparison.OrdinalIgnoreCase));

                if (character != null)
                {
                    Plugin.Log.Debug($"[DeferredStartup] Applying session profile: {_pendingSessionCharacterName}");
                    ApplyProfile(character, -1);
                }
                else
                {
                    Plugin.Log.Warning($"[DeferredStartup] Could not find matching character for {_pendingSessionCharacterName}");
                }

                _pendingSessionCharacterName = null; // ✅ Run only once
            }

            // === Restore on Login ===
            if (!shouldApplyPoses)
                return;

            framesSinceLogin++;
            if (framesSinceLogin >= 5)
            {
                ApplyStoredPoses();
                shouldApplyPoses = false;
                framesSinceLogin = 0;
            }
        }

        private EmoteController.PoseType TranslatePoseState(byte state)
        {
            return state switch
            {
                1 => EmoteController.PoseType.GroundSit,
                2 => EmoteController.PoseType.Sit,
                3 => EmoteController.PoseType.Doze,
                _ => EmoteController.PoseType.Idle
            };
        }
    }
}

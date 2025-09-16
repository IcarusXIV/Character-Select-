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
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game;

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
        [PluginService] internal static IChatGui ChatGui { get; set; } = null!;
        [PluginService] internal static IFramework Framework { get; private set; } = null!;
        [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
        [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
        [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
        [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
        [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
        [PluginService] internal static ICondition Condition { get; private set; } = null!;

        public static readonly string CurrentPluginVersion = "2.0.0.7"; // Match repo.json and .csproj version


        private const string CommandName = "/select";

        public Configuration Configuration { get; init; }
        public readonly WindowSystem WindowSystem = new("CharacterSelectPlugin");
        public MainWindow MainWindow { get; init; }
        public QuickSwitchWindow QuickSwitchWindow { get; set; } // Quick Switch Window
        public PatchNotesWindow PatchNotesWindow { get; private set; } = null!;
        public RPProfileWindow RPProfileEditor { get; private set; }
        public RPProfileViewWindow RPProfileViewer { get; private set; }
        public GalleryWindow GalleryWindow { get; private set; } = null!;
        public TutorialManager TutorialManager { get; private set; } = null!;
        public enum SortType { Manual, Favorites, Alphabetical, Recent, Oldest }

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
        public ImprovedPoseManager PoseManager { get; private set; } = null!;
        public byte NewCharacterIdlePoseIndex { get; set; } = 0;
        public SimplifiedPoseRestorer PoseRestorer { get; private set; } = null!;
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
        private DateTime pluginInitTime = DateTime.Now;
        private string currentSessionId = "";
        private static string CurrentSessionId = Guid.NewGuid().ToString();
        private static string SessionPath => Path.Combine(PluginInterface.ConfigDirectory.FullName, "last_session.txt");
        private static string SessionInfoPath => Path.Combine(PluginInterface.GetPluginConfigDirectory(), "session_info.txt");
        private string? lastAppliedCharacter = null;
        public float UIScaleMultiplier => Configuration.UIScaleMultiplier;
        private string? _pendingSessionCharacterName = null;
        private float secondsSinceLogin = 0;
        private bool isLoginComplete = false;
        public bool IsSecretMode { get; set; } = false;
        private Character? activeCharacter = null!;
        private string lastExecutedGearsetCommand = "";
        private DateTime lastGearsetCommandTime = DateTime.MinValue;
        private readonly Dictionary<string, (string characterName, string designName, DateTime time)> lastAppliedByJob = new();
        public void RefreshTreeItems(Character character)
        {

        }

        public bool IsAddCharacterWindowOpen { get; set; } = false;
        // Settings Variables
        public bool IsSettingsOpen { get; set; } = false;  // Tracks if settings panel is open
        public float ProfileImageScale { get; set; } = 1.0f;  // Image scaling (1.0 = normal size)
        public int ProfileColumns { get; set; } = 3;  // Number of profiles per row
        public float ProfileSpacing { get; set; } = 10.0f; // Default spacing

        // Tutorial
        public Vector2? MainWindowPos { get; set; }
        public Vector2? MainWindowSize { get; set; }
        public Vector2? AddCharacterButtonPos { get; set; }
        public Vector2? AddCharacterButtonSize { get; set; }
        public Vector2? CharacterNameFieldPos { get; set; }
        public Vector2? CharacterNameFieldSize { get; set; }
        public Vector2? PenumbraFieldPos { get; set; }
        public Vector2? PenumbraFieldSize { get; set; }
        public Vector2? GlamourerFieldPos { get; set; }
        public Vector2? GlamourerFieldSize { get; set; }
        public Vector2? SaveButtonPos { get; set; }
        public Vector2? SaveButtonSize { get; set; }
        public Vector2? FirstCharacterDesignsButtonPos { get; set; }
        public Vector2? FirstCharacterDesignsButtonSize { get; set; }
        public Vector2? DesignPanelAddButtonPos { get; set; }
        public Vector2? DesignPanelAddButtonSize { get; set; }
        public Vector2? DesignNameFieldPos { get; set; }
        public Vector2? DesignNameFieldSize { get; set; }
        public Vector2? DesignGlamourerFieldPos { get; set; }
        public Vector2? DesignGlamourerFieldSize { get; set; }
        public Vector2? SaveDesignButtonPos { get; set; }
        public Vector2? SaveDesignButtonSize { get; set; }
        public bool IsDesignPanelOpen { get; set; } = false;
        public bool IsEditDesignWindowOpen { get; set; } = false;
        public string EditedDesignName { get; set; } = "";
        public string EditedGlamourerDesign { get; set; } = "";
        public Vector2? RPProfileButtonPos { get; set; }
        public Vector2? RPProfileButtonSize { get; set; }
        public Vector2? EditProfileButtonPos { get; set; }
        public Vector2? EditProfileButtonSize { get; set; }
        public bool IsRPProfileViewerOpen { get; set; } = false;
        public bool IsRPProfileEditorOpen { get; set; } = false;
        public Vector2? RPBioFieldPos { get; set; }
        public Vector2? RPBioFieldSize { get; set; }
        public Vector2? RPSharingDropdownPos { get; set; }
        public Vector2? RPSharingDropdownSize { get; set; }
        public Vector2? SaveRPProfileButtonPos { get; set; }
        public Vector2? SaveRPProfileButtonSize { get; set; }
        public Vector2? RPPronounsFieldPos { get; set; }
        public Vector2? RPPronounsFieldSize { get; set; }
        public Vector2? RPProfileViewWindowPos { get; set; }
        public Vector2? RPProfileViewWindowSize { get; set; }
        public Vector2? RPProfileEditorWindowPos { get; set; }
        public Vector2? RPProfileEditorWindowSize { get; set; }
        public Vector2? RPBackgroundDropdownPos { get; set; }
        public Vector2? RPBackgroundDropdownSize { get; set; }
        public Vector2? RPVisualEffectsPos { get; set; }
        public Vector2? RPVisualEffectsSize { get; set; }
        public Vector2? SettingsButtonPos { get; set; }
        public Vector2? SettingsButtonSize { get; set; }
        public Vector2? QuickSwitchButtonPos { get; set; }
        public Vector2? QuickSwitchButtonSize { get; set; }
        public Vector2? GalleryButtonPos { get; set; }
        public Vector2? GalleryButtonSize { get; set; }
        private NPCDialogueProcessor? dialogueProcessor;
        public bool NewCharacterIsAdvancedMode { get; set; } = false;

        public unsafe Plugin(IGameInteropProvider gameInteropProvider)
        {
            try
            {
                GameInteropProvider = gameInteropProvider;
                var existingConfig = PluginInterface.GetPluginConfig() as Configuration;
                if (existingConfig != null)
                {
                    BackupManager.CreateBackupIfNeeded(existingConfig, CurrentPluginVersion);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"[Backup] Could not create pre-load backup: {ex.Message}");
            }

            // Load configuration
            Configuration = LoadConfigurationSafely();
            EnsureConfigurationDefaults();
            Configuration = Configuration.Load(PluginInterface);
            EnsureConfigurationDefaults();

            // Patch macros only after loading config + setting
            foreach (var character in Configuration.Characters)
            {
                var newMacro = SanitizeMacro(character.Macros, character);
                if (character.Macros != newMacro)
                    character.Macros = newMacro;
            }

            // Patch existing Design macros to add automation if missing
            foreach (var character in Configuration.Characters)
            {
                foreach (var design in character.Designs)
                {
                    string macroToPatch = design.IsAdvancedMode ? design.AdvancedMacro : design.Macro;

                    if (string.IsNullOrWhiteSpace(macroToPatch))
                        continue;

                    string updated = SanitizeDesignMacro(macroToPatch, design, character, Configuration.EnableAutomations);

                    if (updated != macroToPatch)
                    {
                        if (design.IsAdvancedMode)
                            design.AdvancedMacro = updated;
                        else
                            design.Macro = updated;
                    }
                }
            }
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

            PoseManager = new ImprovedPoseManager(ClientState, Framework, this);
            PoseRestorer = new SimplifiedPoseRestorer(ClientState, PoseManager);

            // Initialize the MainWindow and ConfigWindow
            MainWindow = new Windows.MainWindow(this);
            MainWindow.SortCharacters();
            QuickSwitchWindow = new QuickSwitchWindow(this); // Quick Switch Window
            QuickSwitchWindow.IsOpen = Configuration.IsQuickSwitchWindowOpen; // Restore last open state

            RPProfileEditor = new RPProfileWindow(this);
            WindowSystem.AddWindow(RPProfileEditor);

            RPProfileViewer = new RPProfileViewWindow(this);
            WindowSystem.AddWindow(RPProfileViewer);

            GalleryWindow = new GalleryWindow(this);
            WindowSystem.AddWindow(GalleryWindow);
            TutorialManager = new TutorialManager(this);
            MigrateBackgroundImageNames();

            // This player registering their profile, if someone else requests it
            provideProfile = PluginInterface.GetIpcProvider<string, RPProfile>("CharacterSelect.RPProfile.Provide");
            provideProfile.RegisterFunc(HandleProfileRequest);

            // This player sending a request to another
            requestProfile = PluginInterface.GetIpcSubscriber<string, RPProfile>("CharacterSelect.RPProfile.Provide");

            // Patch Notes
            PatchNotesWindow = new PatchNotesWindow(this);
            if (Configuration.LastSeenVersion != CurrentPluginVersion)
            {
                PatchNotesWindow.OpenMainMenuOnClose = true;
                PatchNotesWindow.IsOpen = true;
            }
            // PatchNotesWindow.IsOpen = true;
            // Tutorial system - show on first launch

            //if (!Configuration.HasSeenTutorial && Configuration.ShowTutorialOnStartup)
            //{
            //    TutorialManager.StartTutorial();
            //}

            WindowSystem.AddWindow(MainWindow);
            WindowSystem.AddWindow(QuickSwitchWindow); // Quick Switch Window
            WindowSystem.AddWindow(PatchNotesWindow); // Patch Notes Window


            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the Character Select+ UI"
            });
            CommandManager.AddHandler("/selectswitch", new CommandInfo(OnQuickSwitchCommand)
            {
                HelpMessage = "Opens the Quick Character Switcher UI."
            });

            CommandManager.AddHandler("/gallery", new CommandInfo(OnGalleryCommand)
            {
                HelpMessage = "Opens the Character Showcase Gallery"
            });


            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += ToggleQuickSwitchUI;
            PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
            ClientState.Login += OnLogin;
            Framework.Update += FrameworkUpdate;
            string sessionFilePath = Path.Combine(PluginInterface.GetPluginConfigDirectory(), "boot_session.txt");

            // Only generate a new session ID if the file does not exist
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
                _pendingSessionCharacterName = nameFromFile;
            }
            else
            {
                Plugin.Log.Debug("[Startup] No session_info.txt found.");
            }


            CommandManager.AddHandler("/select", new CommandInfo(OnSelectCommand)
            {
                HelpMessage = "Use /select <Character Name> [Design Name] to apply a profile, or /select random for random selection."
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
            try
            {
                dialogueProcessor = new NPCDialogueProcessor(
                    this, 
                    SigScanner, 
                    GameInteropProvider,
                    ChatGui, 
                    ClientState, 
                    Log,
                    Condition
                );
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to initialize dialogue processor: {ex.Message}");
            }

        }
        public static HttpClient CreateAuthenticatedHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-Plugin-Auth", "cs-plus-gallery-client");
            client.DefaultRequestHeaders.Add("User-Agent", "CharacterSelectPlus/1.2.0");
            client.Timeout = TimeSpan.FromSeconds(15);
            return client;
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
            secondsSinceLogin = 0f;

            var id = ClientState.LocalPlayer.ClassJob.RowId;
            if (Configuration.LastKnownJobId == 0 && id != 0)
            {
                Configuration.LastKnownJobId = id;
                Configuration.Save();
                Plugin.Log.Debug($"[JobSwitch] Primed LastKnownJobId on login = {id}");
            }
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

            // Force CPoseState to match current selected pose
            if (currentSelected < 7)
                character->EmoteController.CPoseState = currentSelected;
        }

        private void OnQuickSwitchCommand(string command, string args)
        {
            QuickSwitchWindow.IsOpen = !QuickSwitchWindow.IsOpen; // Toggle Window On/Off
        }
        private void OnGalleryCommand(string command, string args)
        {
            // Emergency stop if costs are too high, I am broke!
            if (args.Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                GalleryWindow.EmergencyStop();
                ChatGui.Print("[Character Select+] Gallery emergency stop activated!");
                return;
            }

            GalleryWindow.IsOpen = !GalleryWindow.IsOpen;
        }
        public void ApplyProfile(Character character, int designIndex)
        {
            activeCharacter = character;
            // Do nothing if world isn't ready, honestly I don't know if it will ever truly be ready for...you! You special thing. 
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

                // Remove all old entries for this player
                var toRemove = ActiveProfilesByPlayerName
                    .Where(kvp => kvp.Key.StartsWith($"{localName}@{worldName}", StringComparison.OrdinalIgnoreCase))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var oldKey in toRemove)
                    ActiveProfilesByPlayerName.Remove(oldKey);

                // Register key
                ActiveProfilesByPlayerName[fullKey] = character.Name;
                string pluginCharacterKey = $"{character.Name}@{worldName}"; // plugin character identity
                character.LastInGameName = $"{localName}@{worldName}";        // who is currently logged in

                Configuration.LastUsedCharacterByPlayer[fullKey] = pluginCharacterKey;
                Configuration.Save();

                Plugin.Log.Debug($"[ApplyProfile] Saved: {fullKey} → {pluginCharacterKey}");
                Plugin.Log.Debug($"[SetActiveCharacter] Updated LastUsedCharacterKey = {fullKey}");
                Plugin.Log.Debug($"[ApplyProfile] Set LastInGameName = {character.LastInGameName} for profile {character.Name}");

                bool shouldUploadToGallery = ShouldUploadToGallery(character, fullKey);

                if (shouldUploadToGallery)
                {
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
                        CharacterName = character.Name, // force correct name
                        NameplateColor = character.RPProfile?.ProfileColor ?? character.NameplateColor, // force correct colour
                        GalleryStatus = character.GalleryStatus,
                        Links = character.RPProfile?.Links,

                        // Include background and effects data
                        BackgroundImage = character.RPProfile?.BackgroundImage ?? character.BackgroundImage,
                        Effects = character.RPProfile?.Effects ?? character.Effects ?? new ProfileEffects(),

                        // Include animation theme for backwards compatibility
                        AnimationTheme = character.RPProfile?.AnimationTheme ?? ProfileAnimationTheme.CircuitBoard,
                        LastActiveTime = Configuration.ShowRecentlyActiveStatus ? DateTime.UtcNow : null
                    };

                    _ = Plugin.UploadProfileAsync(profileToSend, character.LastInGameName ?? character.Name);
                    Plugin.Log.Info($"[ApplyProfile] ✓ Uploaded profile for {character.Name} (main character: {fullKey})");
                }
                else
                {
                    Plugin.Log.Info($"[ApplyProfile] ⚠ Skipped gallery upload for {character.Name} (not on main character or not public)");
                }
            }
            SaveConfiguration();
            if (character == null) return;

            // Apply the character's macro
            ExecuteMacro(character.Macros, character, null);

            // If a design is selected, apply that too
            if (designIndex >= 0 && designIndex < character.Designs.Count)
            {
                ExecuteMacro(character.Designs[designIndex].Macro);
            }

            // Only apply idle pose if it's not "None"
            if (isLoginComplete)
            {
                // Apply poses immediately
                if (character.IdlePoseIndex < 7)
                {
                    PoseManager.ApplyPose(PoseType.Idle, character.IdlePoseIndex);
                    Configuration.LastIdlePoseAppliedByPlugin = character.IdlePoseIndex;
                    Configuration.Save();
                }
                else
                {
                    Plugin.Log.Debug("[ApplyProfile] Skipping idle pose apply because it is set to None.");
                }
                PoseRestorer.RestorePosesFor(character);
            }
            else
            {
                // Defer until login completes
                activeCharacter = character;
                shouldApplyPoses = true;
            }
            this.QuickSwitchWindow.UpdateSelectionFromCharacter(character);
            SaveConfiguration();
        }

        private bool ShouldUploadToGallery(Character character, string currentPhysicalCharacter)
        {
            // Is there a main character set?
            var userMain = Configuration.GalleryMainCharacter;
            if (string.IsNullOrEmpty(userMain))
            {
                Plugin.Log.Debug($"[ShouldUpload] No main character set - not uploading {character.Name}");
                return false;
            }

            // Are we currently on the main character?
            if (currentPhysicalCharacter != userMain)
            {
                Plugin.Log.Debug($"[ShouldUpload] Current character '{currentPhysicalCharacter}' != main '{userMain}' - not uploading {character.Name}");
                return false;
            }

            // Is this CS+ character set to public sharing?
            var sharing = character.RPProfile?.Sharing ?? ProfileSharing.AlwaysShare;
            if (sharing != ProfileSharing.ShowcasePublic)
            {
                Plugin.Log.Debug($"[ShouldUpload] Character '{character.Name}' sharing is '{sharing}' (not public) - not uploading");
                return false;
            }

            Plugin.Log.Debug($"[ShouldUpload] ✓ All checks passed - will upload {character.Name} as {currentPhysicalCharacter}");
            return true;
        }

        private void EnsureConfigurationDefaults()
        {
            bool updated = false;

            // Keep existing check for IsConfigWindowMovable
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

            // Handle nullable values & avoid unboxing issues
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
                ProfileSpacing = 10.0f;  // Default value if missing
                updated = true;
            }
            if (updated) Configuration.Save();
        }



        public void Dispose()
        {
            WindowSystem.RemoveAllWindows();
            MainWindow.Dispose();
            CommandManager.RemoveHandler(CommandName);
            CommandManager.RemoveHandler("/spose");
            CommandManager.RemoveHandler("/gallery");
            contextMenuManager?.Dispose();
            Framework.Update += FrameworkUpdate;
            PoseManager?.Dispose();
            dialogueProcessor?.Dispose();
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
                ToggleMainUI();
            }
            else
            {
                OnSelectCommand(command, args);
            }
        }

        private void OnSelectCommand(string command, string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                ChatGui.PrintError("[Character Select+] Usage: /select <Character Name> [Optional Design Name] or /select random");
                return;
            }

            // Handle random selection
            if (args.Trim().Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                SelectRandomCharacterAndDesign();
                return;
            }

            // Rest of the existing method remains the same...
            var matches = Regex.Matches(args, "\"([^\"]+)\"|\\S+")
                .Cast<Match>()
                .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Value)
                .ToArray();

            if (matches.Length < 1)
            {
                ChatGui.PrintError("[Character Select+] Invalid usage. Use /select <Character Name> [Optional Design Name] or /select random");
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
                ApplyProfile(character, -1);
            }
            else
            {
                var design = character.Designs.FirstOrDefault(d => d.Name.Equals(designName, StringComparison.OrdinalIgnoreCase));

                if (design != null)
                {
                    ChatGui.Print($"[Character Select+] Applied design '{designName}' to {character.Name}.");
                    ExecuteMacro(design.Macro, character, design.Name);
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
            TutorialManager.DrawTutorialOverlay();

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
                    macroToSave, // Preserve Advanced Mode Macro when saving
                    NewCharacterImagePath,
                    new List<CharacterDesign>(NewCharacterDesigns),
                    NewCharacterColor,
                    NewPenumbraCollection,
                    NewGlamourerDesign,
                    NewCustomizeProfile,

                    // Add Honorific Fields
                    NewCharacterHonorificTitle,
                    NewCharacterHonorificPrefix,
                    NewCharacterHonorificSuffix,
                    NewCharacterHonorificColor,
                    NewCharacterHonorificGlow,
                    NewCharacterMoodlePreset, //MOODLES
                    NewCharacterAutomation // Glamourer Automations
                )
                {
                    IdlePoseIndex = NewCharacterIdlePoseIndex,
                    IsAdvancedMode = NewCharacterIsAdvancedMode, // Use the plugin property
                    Tags = string.IsNullOrWhiteSpace(NewCharacterTag)
                ? new List<string>()
                : NewCharacterTag.Split(',').Select(f => f.Trim()).ToList()
                };

                // Auto-create a Design based on Glamourer Design if available
                if (!string.IsNullOrWhiteSpace(NewGlamourerDesign))
                {
                    string defaultDesignName = $"{NewCharacterName} {NewGlamourerDesign}";
                    var defaultDesign = new CharacterDesign(
                    defaultDesignName,
                    new Vector3(1.0f, 1.0f, 1.0f), // Default to white
                    "",  // macro will be filled below
                    false,
                    ""
                    );

                    // Sanitize to include Automation fallback
                    defaultDesign.Macro = SanitizeDesignMacro(
                        $"/glamour apply {NewGlamourerDesign} | self\n/penumbra redraw self",
                        defaultDesign,
                        newCharacter,
                        Configuration.EnableAutomations
                    );


                    newCharacter.Designs.Add(defaultDesign); // Automatically add the default design
                }

                Configuration.Characters.Add(newCharacter);
                SaveConfiguration();

                // Reset Fields after Saving
                NewCharacterName = "";
                NewCharacterMacros = "";
                NewCharacterImagePath = null;
                NewCharacterDesigns.Clear();
                NewPenumbraCollection = "";
                NewGlamourerDesign = "";
                NewCustomizeProfile = "";

                // Reset Honorific Fields
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

        // Executes a macro by sending text commands to the game.
        public void ExecuteMacro(string macroText)
        {
            ExecuteMacro(macroText, null, null);
        }
        public void ExecuteMacro(string macroText, Character? character, string? designName)
        {
            ExecuteMacro(macroText, character, designName, false);
        }

        public unsafe void ExecuteMacro(string macroText, Character? character, string? designName, bool filterJobChanges = true)
        {
            if (string.IsNullOrWhiteSpace(macroText))
                return;

            // Always filter job changes by default
            if (filterJobChanges)
            {
                macroText = FilterJobChangeCommands(macroText);

                if (string.IsNullOrWhiteSpace(macroText))
                {
                    Log.Debug("[ExecuteMacro] All commands were filtered out");
                    return;
                }
            }
            else
            {
                Log.Debug($"[ExecuteMacro] ELSE BRANCH - Manual application detected");

                if (character != null && !string.IsNullOrEmpty(designName))
                {
                    Log.Debug($"[ExecuteMacro] Checking for recent application...");

                    // Get the target gearset
                    string targetGearset = GetTargetGearsetFromMacro(macroText);

                    if (!string.IsNullOrEmpty(targetGearset))
                    {
                        string trackingKey = $"{character.Name}_{designName}_{targetGearset}";

                        if (lastAppliedByJob.ContainsKey(trackingKey) && 
                            (DateTime.Now - lastAppliedByJob[trackingKey].time).TotalSeconds < 60)
                        {
                            Log.Debug($"[ExecuteMacro] Same design+gearset applied recently, filtering job changes");
                            macroText = FilterJobChangeCommands(macroText);
                            Log.Debug($"[ExecuteMacro] Filtered macro: {macroText.Replace("\n", " | ")}");
                        }

                        // Update tracking with string key
                        lastAppliedByJob[trackingKey] = (character.Name, designName, DateTime.Now);
                        Log.Debug($"[ExecuteMacro] Updated tracking for key: {trackingKey}");
                    }
                }
            }
            var pluginCommands = new List<string>();
            var gameCommands = new List<string>();

            // Track gearset usage for future filtering
            var currentJobId = ClientState.LocalPlayer?.ClassJob.RowId ?? 0;

            // Check if this macro contains wait commands
            bool containsWaitCommands = macroText.Split('\n')
                .Any(line => line.Trim().StartsWith("/wait", StringComparison.OrdinalIgnoreCase));

            // Separate plugin commands from game commands
            foreach (var raw in macroText.Split('\n'))
            {
                var cmd = raw.Trim();
                if (cmd.Length == 0) continue;

                // Check for duplicate gearset commands FIRST
                if (IsGearsetChangeCommand(cmd))
                {
                    // ... keep your existing gearset tracking code here
                }

                // KEY CHANGE: If macro contains waits, send ALL commands through game system
                if (containsWaitCommands)
                {
                    if (cmd.StartsWith("/"))
                    {
                        gameCommands.Add(cmd);
                        Log.Debug($"Queued for sequential execution: '{cmd}'");
                    }
                }
                else
                {
                    // Normal execution: immediate plugin commands
                    bool handledByPlugin = CommandManager.ProcessCommand(cmd);

                    if (handledByPlugin)
                    {
                        Log.Debug($"Plugin command executed: '{cmd}'");
                    }
                    else if (cmd.StartsWith("/"))
                    {
                        gameCommands.Add(cmd);
                        Log.Debug($"Queued game command: '{cmd}'");
                    }
                    else
                    {
                        Log.Debug($"Skipping non-command text: '{cmd}'");
                    }
                }
            }

            if (gameCommands.Count > 0)
            {
                ExecuteGameCommands(gameCommands);
            }

            if (character != null)
            {
                Configuration.LastUsedCharacterKey = character.Name;

                if (!string.IsNullOrEmpty(designName))
                {
                    Configuration.LastUsedDesignCharacterKey = character.Name;
                    Configuration.LastUsedDesignByCharacter[character.Name] = designName;
                    Log.Debug($"[MacroTracker] Saved last design {designName} for {character.Name}");
                }
                else
                {
                    Configuration.LastUsedDesignCharacterKey = null;
                    Configuration.LastUsedDesignByCharacter.Remove(character.Name);
                    Log.Debug($"[MacroTracker] Cleared design for {character.Name}");
                }

                Configuration.Save();
            }
        }
        private string GetTargetGearsetFromMacro(string macro)
        {
            var lines = macro.Split('\n');
            foreach (var line in lines)
            {
                var cmd = line.Trim();
                if (IsGearsetChangeCommand(cmd))
                {
                    var gearsetNumber = ExtractGearsetNumber(cmd);
                    return gearsetNumber?.ToString() ?? "";
                }
            }
            return "";
        }

        // Execute game commands using the macro system
        private unsafe void ExecuteGameCommands(List<string> commands)
        {
            if (commands.Count == 0) return;
            if (commands.Count > 15)
            {
                Plugin.Log.Warning($"Too many game commands ({commands.Count}), max is 15. Truncating.");
                commands = commands.Take(15).ToList();
            }

            var raptureShellModule = RaptureShellModule.Instance();
            if (raptureShellModule == null)
            {
                Plugin.Log.Warning("Could not get RaptureShellModule instance");
                return;
            }
            var macro = new RaptureMacroModule.Macro();
            macro.Name.Ctor();
            foreach (ref var line in macro.Lines)
            {
                line.Ctor();
            }

            try
            {
                // Set up the macro lines
                for (int i = 0; i < commands.Count && i < 15; i++)
                {
                    var cmd = commands[i];
                    if (string.IsNullOrWhiteSpace(cmd))
                    {
                        macro.Lines[i].Clear();
                        continue;
                    }

                    var encoded = System.Text.Encoding.UTF8.GetBytes(cmd + "\0");
                    if (encoded.Length == 0)
                    {
                        macro.Lines[i].Clear();
                        continue;
                    }

                    fixed (byte* encodedPtr = encoded)
                    {
                        macro.Lines[i].SetString(encodedPtr);
                    }
                }

                // Execute the macro
                raptureShellModule->ExecuteMacro(&macro);
                Plugin.Log.Debug($"Executed {commands.Count} game commands via macro system");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to execute game commands: {ex.Message}");
            }
            finally
            {
                // Clean up the macro object
                foreach (ref var line in macro.Lines)
                {
                    line.Dtor();
                }
            }
        }


        public void SaveConfiguration()
        {
            try
            {
                // Update properties first
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

                var profileSpacingProperty = Configuration.GetType().GetProperty("ProfileSpacing");
                if (profileSpacingProperty != null && profileSpacingProperty.CanWrite)
                {
                    profileSpacingProperty.SetValue(Configuration, ProfileSpacing);
                }

                // Save configuration
                Configuration.Save();

                // Create backup occasionally (roughly every 10th save)
                if (DateTime.Now.Millisecond % 100 == 0)
                {
                    BackupManager.CreateBackupIfNeeded(Configuration, CurrentPluginVersion);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Config] Failed to save configuration: {ex.Message}");

                // Create emergency backup
                try
                {
                    BackupManager.CreateEmergencyBackup(Configuration);
                }
                catch (Exception backupEx)
                {
                    Plugin.Log.Error($"[Config] Emergency backup also failed: {backupEx.Message}");
                }
            }
        }
        private Configuration LoadConfigurationSafely()
        {
            try
            {
                // Try to load normal configuration
                var config = Configuration.Load(PluginInterface);
                Plugin.Log.Debug("[Config] Configuration loaded successfully");
                return config;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Config] Failed to load configuration: {ex.Message}");

                // Try to restore from backup
                Plugin.Log.Info("[Config] Attempting to restore from backup...");
                var backupConfig = BackupManager.RestoreFromBackup();

                if (backupConfig != null)
                {
                    Plugin.Log.Info("[Config] Configuration restored from backup successfully!");
                    return backupConfig;
                }
                else
                {
                    Plugin.Log.Warning("[Config] Backup restoration failed, creating new configuration");
                    return new Configuration(PluginInterface);
                }
            }
        }

        public void CreateManualBackup()
        {
            try
            {
                BackupManager.CreateEmergencyBackup(Configuration);
                Plugin.Log.Info("[Backup] Manual backup created successfully");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Backup] Manual backup failed: {ex.Message}");
            }
        }

        public BackupInfo GetBackupInfo()
        {
            return BackupManager.GetBackupInfo();
        }
        public void OpenDesignPanel(int characterIndex)
        {
            MainWindow.OpenDesignPanel(characterIndex);
        }

        public void OpenEditCharacterWindow(int index)
        {
            MainWindow.OpenEditCharacterWindow(index);
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

            // Remove old pose commands and replace with new ones (always do this)
            lines = lines
                .Where(l => !l.TrimStart().StartsWith("/savepose", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Migrate old pose commands to new ones (always do this)
            for (int i = 0; i < lines.Count; i++)
            {
                lines[i] = lines[i]
                    .Replace("/spose", "/sidle")
                    .Replace("/sitpose", "/ssit")
                    .Replace("/groundsitpose", "/sgroundsit")
                    .Replace("/dozepose", "/sdoze");
            }

            // Insert /glamour automation enable {X} after last /glamour apply
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
                        lines.Insert(lastGlamourIndex + 1, automationLine);
                    else
                        lines.Insert(0, automationLine);
                }
            }

            // For Advanced Mode characters
            if (character.IsAdvancedMode)
            {
                // Only ensure redraw is present if there are any plugin commands
                bool hasPluginCommands = lines.Any(l =>
                    l.StartsWith("/penumbra", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("/glamour", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("/customize", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("/honorific", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("/moodle", StringComparison.OrdinalIgnoreCase));

                if (hasPluginCommands && !lines.Any(l => l.Contains("/penumbra redraw self")))
                {
                    lines.Add("/penumbra redraw self");
                }

                return string.Join("\n", lines);
            }

            // For non-Advanced Mode characters, do full sanitization
            AddOrReplace("/customize profile disable <me>");
            AddOrReplace("/honorific force clear");
            AddOrReplace("/moodle remove self preset all");

            if (!lines.Any(l => l.Contains("/penumbra redraw self")))
                lines.Add("/penumbra redraw self");

            // Handle Customize+ profile enabling
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

            return string.Join("\n", lines);
        }

        public static string SanitizeDesignMacro(string macro, CharacterDesign design, Character character, bool enableAutomations)
        {
            var lines = macro.Split('\n').Select(l => l.Trim()).ToList();

            // Add automation if missing (only if enabled)
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

            // Customize+ lines to always disable first, then enable (if needed)

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
            RPProfileViewer.IsOpen = false;
            RPProfileViewer.SetCharacter(character);
            RPProfileViewer.IsOpen = true;
        }

        public void OpenRPProfileViewWindow(Character character)
        {
            RPProfileViewer.SetCharacter(character);
            RPProfileViewer.IsOpen = true;
            IsRPProfileViewerOpen = true;
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

            // Find the matching character with LastInGameName
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

            // Try to get local name first
            string? localName = ClientState.LocalPlayer?.Name.TextValue;

            // If player is trying to view their own profile
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

            // Only hits this if not matched above
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

                // This is what OnLogin uses to find the right profile
                character.LastInGameName = pluginCharacterKey;

                // This is the key logic: player -> selected plugin character
                Configuration.LastUsedCharacterByPlayer[fullKey] = pluginCharacterKey;

                // Write the session info file so the plugin remembers the last applied character name
                File.WriteAllText(SessionInfoPath, character.Name);
                Plugin.Log.Debug($"[ApplyProfile] 📝 Wrote session_info.txt = {character.Name}");

                Configuration.Save();

                // These log lines now match and won't be skipped
                Plugin.Log.Debug($"[SetActiveCharacter] Saved: {fullKey} → {pluginCharacterKey}");
                Plugin.Log.Debug($"[SetActiveCharacter] Set LastInGameName = {pluginCharacterKey} for profile {character.Name}");

                // Only upload if we should upload to gallery
                bool shouldUploadToGallery = ShouldUploadToGallery(character, fullKey);

                if (shouldUploadToGallery)
                {
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
                        CharacterName = character.Name, // force correct name
                        NameplateColor = character.RPProfile?.ProfileColor ?? character.NameplateColor, // force correct colour
                        BackgroundImage = character.RPProfile?.BackgroundImage ?? character.BackgroundImage,
                        Effects = character.RPProfile?.Effects ?? character.Effects ?? new ProfileEffects(),
                        GalleryStatus = character.GalleryStatus,
                        Links = character.RPProfile?.Links,
                        LastActiveTime = Configuration.ShowRecentlyActiveStatus ? DateTime.UtcNow : null
                    };

                    _ = Plugin.UploadProfileAsync(profileToSend, character.LastInGameName ?? character.Name);
                    Plugin.Log.Info($"[SetActiveCharacter] ✓ Uploaded profile for {character.Name}");
                }
                else
                {
                    Plugin.Log.Info($"[SetActiveCharacter] ⚠ Skipped gallery upload for {character.Name}");
                }
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
            Stream? imageStream = null;
            StreamContent? imageContent = null;

            try
            {
                using var http = new HttpClient();
                using var form = new MultipartFormDataContent();

                // Get character match from config
                var config = PluginInterface.GetPluginConfig() as Configuration;
                Character? match = config?.Characters.FirstOrDefault(c => c.LastInGameName == characterName);

                if (match != null)
                {
                    // Only set character name if it's not already set
                    profile.CharacterName ??= match.Name;

                    // Only set nameplate colour if it's not set (all zeros)
                    if (profile.NameplateColor.X <= 0f
                     && profile.NameplateColor.Y <= 0f
                     && profile.NameplateColor.Z <= 0f)
                    {
                        profile.NameplateColor = match.NameplateColor;
                    }

                    // Ensure background and effects are included in upload
                    if (profile.Effects == null && match.Effects != null)
                    {
                        profile.Effects = new ProfileEffects
                        {
                            CircuitBoard = match.Effects.CircuitBoard,
                            Fireflies = match.Effects.Fireflies,
                            FallingLeaves = match.Effects.FallingLeaves,
                            Butterflies = match.Effects.Butterflies,
                            Bats = match.Effects.Bats,
                            Fire = match.Effects.Fire,
                            Smoke = match.Effects.Smoke,
                            ColorScheme = match.Effects.ColorScheme,
                            CustomParticleColor = match.Effects.CustomParticleColor
                        };
                    }
                }

                // Determine correct image to upload
                string? imagePathToUpload = null;
                if (!string.IsNullOrEmpty(profile.CustomImagePath) && File.Exists(profile.CustomImagePath))
                {
                    imagePathToUpload = profile.CustomImagePath;
                }
                else if (!string.IsNullOrEmpty(match?.ImagePath) && File.Exists(match.ImagePath))
                {
                    imagePathToUpload = match.ImagePath;
                }

                // Attach image if found
                if (!string.IsNullOrEmpty(imagePathToUpload))
                {
                    imageStream = File.OpenRead(imagePathToUpload);
                    imageContent = new StreamContent(imageStream);
                    imageContent.Headers.ContentType =
                        new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                    form.Add(imageContent, "image", $"{Guid.NewGuid()}.png");
                }

                // Upload JSON - The profile parameter already has the correct data!
                string json = JsonConvert.SerializeObject(profile);
                form.Add(new StringContent(json, Encoding.UTF8, "application/json"), "profile");

                // Add a flag to indicate this is an update, not a new profile
                form.Add(new StringContent("true", Encoding.UTF8, "text/plain"), "isUpdate");

                // Send both CS+ character name and physical character name
                form.Add(new StringContent(profile.CharacterName ?? "Unknown", Encoding.UTF8, "text/plain"), "csCharacterName");

                string urlSafeName = Uri.EscapeDataString(characterName);

                // Use PUT for updates instead of POST to preserve likes
                var request = new HttpRequestMessage(HttpMethod.Put, $"https://character-select-profile-server-production.up.railway.app/upload/{urlSafeName}")
                {
                    Content = form
                };

                Plugin.Log.Info($"[UploadProfile] Updating profile for CS+ character '{profile.CharacterName}' as physical character '{characterName}'");

                var response = await http.SendAsync(request);

                imageContent?.Dispose();
                imageStream?.Dispose();

                // Process response
                var responseJson = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var updated = JsonConvert.DeserializeObject<RPProfile>(responseJson);

                    if (updated?.ProfileImageUrl is { Length: > 0 })
                    {
                        profile.ProfileImageUrl = updated.ProfileImageUrl;

                        // Update the stored profile with the new image URL
                        if (match?.RPProfile != null)
                        {
                            match.RPProfile.ProfileImageUrl = updated.ProfileImageUrl;
                            Plugin.Log.Debug($"[UploadProfile] Updated ProfileImageUrl for {characterName} = {updated.ProfileImageUrl}");
                            config?.Save();
                        }
                    }

                    Plugin.Log.Info($"[UploadProfile] Successfully updated profile for CS+ character {profile.CharacterName} as {characterName}");
                }
                else
                {
                    Plugin.Log.Warning($"[UploadProfile] Failed to upload profile for {characterName}: {response.StatusCode}");
                    Plugin.Log.Warning($"[UploadProfile] Server response: {responseJson}");
                }
            }
            catch (NullReferenceException nre)
            {
                Plugin.Log.Debug($"[UploadProfile] NullReference for {characterName}: {nre.Message}");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[UploadProfile] Exception: {ex}");
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
                var profile = RPProfileJson.Deserialize(json);

                // Ensure we got all the background and effects data
                if (profile != null)
                {
                    Plugin.Log.Debug($"[DownloadProfile] Downloaded profile");
                    Plugin.Log.Debug($"[DownloadProfile] Profile belongs to CS+ character: {profile.CharacterName}");
                    Plugin.Log.Debug($"[DownloadProfile] BackgroundImage: {profile.BackgroundImage ?? "null"}");
                    Plugin.Log.Debug($"[DownloadProfile] Effects: {(profile.Effects != null ? "present" : "null")}");
                    if (profile.Effects != null)
                    {
                        Plugin.Log.Debug($"[DownloadProfile] Effects - Fireflies: {profile.Effects.Fireflies}, Leaves: {profile.Effects.FallingLeaves}, etc.");
                    }
                }

                return profile;
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

                // Skip lines that should never apply to targets
                if (
                    line.StartsWith("/customize profile disable", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("/honorific", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("/moodle", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("/spose", StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                // Rewriting self-targeting lines to <t>
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

                // Specific override
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
            if (Configuration.EnableSafeMode)
                return;
            if (!ClientState.IsLoggedIn || ClientState.LocalPlayer == null)
                return;
            if (Configuration.EnableLoginDelay)
            {
                secondsSinceLogin += (float)Framework.UpdateDelta.TotalSeconds;

                if (secondsSinceLogin < 6f)
                    return;
            }

            var player = ClientState.LocalPlayer!;
            uint currentJobId = player.ClassJob.RowId;

            if (Configuration.ReapplyDesignOnJobChange && currentJobId != Configuration.LastKnownJobId)
            {
                var oldJob = Configuration.LastKnownJobId;
                Configuration.LastKnownJobId = currentJobId;

                if (!Configuration.EnableAutomations)
                {
                    Plugin.Log.Debug($"[JobSwitch] Detected job change: {oldJob} → {currentJobId}");
                    var reapplied = false;

                    if (!string.IsNullOrEmpty(Configuration.LastUsedDesignCharacterKey) &&
                        Configuration.LastUsedDesignByCharacter.TryGetValue(Configuration.LastUsedDesignCharacterKey, out var designName))
                    {
                        var designCharacter = Characters.FirstOrDefault(c => c.Name == Configuration.LastUsedDesignCharacterKey);
                        var design = designCharacter?.Designs.FirstOrDefault(d => d.Name == designName);
                        if (design != null)
                        {
                            Plugin.Log.Debug($"[JobSwitch] Reapplying design {design.Name} for {designCharacter.Name} (filtered)");
                            ExecuteMacro(design.Macro, designCharacter, design.Name, filterJobChanges: true);
                            reapplied = true;
                        }
                    }

                    if (!reapplied && !string.IsNullOrEmpty(Configuration.LastUsedCharacterKey))
                    {
                        var character = Characters.FirstOrDefault(c => c.Name == Configuration.LastUsedCharacterKey);
                        if (character != null)
                        {
                            Plugin.Log.Debug($"[JobSwitch] Reapplying character macro for {character.Name} (filtered)");
                            ExecuteMacro(character.Macros, character, null, filterJobChanges: true);
                            reapplied = true;
                        }
                    }
                }
            }

            // Safe login timer
            if (!ClientState.IsLoggedIn)
            {
                secondsSinceLogin = 0;
                isLoginComplete = false;
            }
            else if (!isLoginComplete)
            {
                secondsSinceLogin += (float)Framework.UpdateDelta.TotalSeconds;

                if (secondsSinceLogin >= 5)
                {
                    isLoginComplete = true;
                    TryRestorePosesAfterLogin();
                }
            }

            if (!ClientState.IsLoggedIn || ClientState.LocalPlayer == null || ClientState.TerritoryType == 0)
                return;
            unsafe
            {
                var state = PlayerState.Instance();
                if (state == null || (nint)state == IntPtr.Zero)
                    return;

                try
                {
                    // IDLE POSE
                    if (Configuration.EnablePoseAutoSave)
                    {
                        if (Configuration.ApplyIdleOnLogin)
                        {
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
                        }
                    }

                    // SIT POSE
                    if (Configuration.EnablePoseAutoSave)
                    {
                        if (Configuration.ApplyIdleOnLogin)
                        {
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
                        }
                    }
                    // GROUNDSIT POSE
                    if (Configuration.EnablePoseAutoSave)
                    {
                        if (Configuration.ApplyIdleOnLogin)
                        {
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
                        }
                    }

                    // DOZE POSE
                    if (Configuration.EnablePoseAutoSave)
                    {
                        if (Configuration.ApplyIdleOnLogin)
                        {
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
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"[CrashPrevent] Exception while accessing PlayerState: {ex.Message}");
                }
            }

            if (player.HomeWorld.IsValid && ClientState.IsLoggedIn)
            {
                string world = player.HomeWorld.Value.Name.ToString();
                string fullKey = $"{player.Name.TextValue}@{world}";

                if (!Configuration.EnableLastUsedCharacterAutoload)
                    return;
                if (Configuration.EnableLastUsedCharacterAutoload &&
                    lastAppliedCharacter != fullKey &&
                    ClientState.TerritoryType != 0 &&
                    (Configuration.EnableLoginDelay ? DateTime.Now - loginTime > TimeSpan.FromSeconds(3) : true))
                {
                    // Main Character Only Logic takes priority over everything
                    if (Configuration.EnableMainCharacterOnly && !string.IsNullOrEmpty(Configuration.MainCharacterName))
                    {
                        var mainCharacter = Characters.FirstOrDefault(c => c.Name == Configuration.MainCharacterName);
                        if (mainCharacter != null)
                        {
                            Plugin.Log.Debug($"[AutoLoad-Main] ✅ Applying main character {mainCharacter.Name} for {fullKey} (overriding any assignments)");
                            ApplyProfile(mainCharacter, -1);
                            lastAppliedCharacter = fullKey;
                        }
                        else
                        {
                            Plugin.Log.Debug($"[AutoLoad-Main] ❌ Main character '{Configuration.MainCharacterName}' not found, falling back to assignments/last used");
                            ApplyLastUsedCharacter(fullKey);
                        }
                    }
                    else
                    {
                        // Check assignments, then last used
                        ApplyLastUsedCharacter(fullKey);
                    }
                }
            }
            // Handle deferred ApplyProfile from session_info.txt
            if (Configuration.EnableLastUsedCharacterAutoload &&
                _pendingSessionCharacterName != null &&
                ClientState.IsLoggedIn &&
                ClientState.LocalPlayer != null &&
                ClientState.TerritoryType != 0 &&
                (Configuration.EnableLoginDelay ? DateTime.Now - loginTime > TimeSpan.FromSeconds(3) : true))
            {
                // Check if current character has a specific assignment - if so, skip deferred startup
                string localName = ClientState.LocalPlayer.Name.TextValue;
                string worldName = ClientState.LocalPlayer.HomeWorld.Value.Name.ToString();
                string fullKey = $"{localName}@{worldName}";

                bool hasAssignment = Configuration.CharacterAssignments.ContainsKey(fullKey);

                if (hasAssignment)
                {
                    Plugin.Log.Debug($"[DeferredStartup] Skipping session profile '{_pendingSessionCharacterName}' - {fullKey} has specific assignment");
                }
                else
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
                }

                _pendingSessionCharacterName = null; // Run only once
            }

            // Restore poses on Login
            if (!shouldApplyPoses)
                return;

            framesSinceLogin++;
            if (framesSinceLogin >= 5)
            {
                if (Configuration.ApplyIdleOnLogin)
                {
                    ApplyStoredPoses();
                }
                else
                {
                    Log.Debug("[Login] Skipping ApplyStoredPoses because ApplyIdleOnLogin is false.");
                }

                shouldApplyPoses = false;
                framesSinceLogin = 0;
            }
        }
        private void ApplyLastUsedCharacter(string fullKey)
        {
            // First check for specific character assignment
            if (Configuration.CharacterAssignments.TryGetValue(fullKey, out var assignedCharacterName))
            {
                // Check if assignment is "None" - skip all auto-application
                if (assignedCharacterName == "None")
                {
                    Plugin.Log.Debug($"[AutoLoad-Assignment] ⚠ Assignment set to 'None' for {fullKey} - skipping all auto-application");
                    lastAppliedCharacter = fullKey;
                    return;
                }

                var assignedCharacter = Characters.FirstOrDefault(c => c.Name == assignedCharacterName);
                if (assignedCharacter != null)
                {
                    Plugin.Log.Debug($"[AutoLoad-Assignment] ✅ Applying assigned character {assignedCharacter.Name} for {fullKey}");
                    ApplyProfile(assignedCharacter, -1);
                    lastAppliedCharacter = fullKey;
                    return;
                }
                else
                {
                    Plugin.Log.Debug($"[AutoLoad-Assignment] ❌ Assigned character '{assignedCharacterName}' not found for {fullKey}");
                    // Remove invalid assignment (but keep "None" assignments)
                    if (assignedCharacterName != "None")
                    {
                        Configuration.CharacterAssignments.Remove(fullKey);
                        Configuration.Save();
                    }
                }
            }

            // Fall back to existing "last used" logic
            if (Configuration.LastUsedCharacterByPlayer.TryGetValue(fullKey, out var lastUsedKey))
            {
                var character = Characters.FirstOrDefault(c =>
                    $"{c.Name}@{ClientState.LocalPlayer!.HomeWorld.Value.Name}" == lastUsedKey);

                if (character != null)
                {
                    Plugin.Log.Debug($"[AutoLoad-LastUsed] ✅ Applying {character.Name} for {fullKey}");
                    ApplyProfile(character, -1);
                    lastAppliedCharacter = fullKey;
                }
                else if (lastAppliedCharacter != $"!notfound:{lastUsedKey}")
                {
                    Plugin.Log.Debug($"[AutoLoad-LastUsed] ❌ No match found for {lastUsedKey}");
                    lastAppliedCharacter = $"!notfound:{lastUsedKey}";
                }
            }
            else
            {
                Plugin.Log.Debug($"[AutoLoad] ❌ No assignment or previous character stored for {fullKey}");
            }
        }
        public string FilterJobChangeCommands(string macro)
        {
            if (string.IsNullOrWhiteSpace(macro))
                return macro;

            try
            {
                var lines = macro.Split('\n');
                var filteredLines = new List<string>();

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();

                    // Simply skip ALL gearset change commands during reapplication
                    if (IsGearsetChangeCommand(trimmedLine))
                    {
                        Log.Debug($"[JobFilter] Skipping gearset command during reapplication: {trimmedLine}");
                        continue; // Skip this line entirely
                    }

                    filteredLines.Add(line);
                }

                string result = string.Join("\n", filteredLines);
                Log.Debug($"[JobFilter] Filtered {lines.Length - filteredLines.Count} gearset commands");
                return result;
            }
            catch (Exception ex)
            {
                Log.Error($"[JobFilter] Error filtering: {ex.Message}");
                return macro; // Return original on error
            }
        }


        private bool IsGearsetChangeCommand(string command)
        {
            var trimmed = command.Trim();
            return trimmed.StartsWith("/gearset change", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("/gs change", StringComparison.OrdinalIgnoreCase);
        }

        private uint? ExtractGearsetNumber(string command)
        {
            try
            {
                // Extract gearset number from command
                var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 3)
                    return null;

                var target = parts[2]; // Usually the third part contains the gearset number

                // If it's a number, return it
                if (uint.TryParse(target, out uint gearsetNumber))
                {
                    return gearsetNumber;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
        public void SelectRandomCharacterAndDesign()
        {
            var random = new Random();

            // Get available characters based on settings
            var availableCharacters = Configuration.RandomSelectionFavoritesOnly
                ? Characters.Where(c => c.IsFavorite).ToList()
                : Characters.ToList();

            // Fallback to all characters if no favourites exist
            if (availableCharacters.Count == 0 && Configuration.RandomSelectionFavoritesOnly)
            {
                availableCharacters = Characters.ToList();
                ChatGui.Print("[Character Select+] No favourite characters found, selecting from all characters.");
            }

            if (availableCharacters.Count == 0)
            {
                ChatGui.PrintError("[Character Select+] No characters available for random selection.");
                return;
            }

            // Select random character
            var selectedCharacter = availableCharacters[random.Next(availableCharacters.Count)];

            // Get available designs for the selected character
            var availableDesigns = Configuration.RandomSelectionFavoritesOnly
                ? selectedCharacter.Designs.Where(d => d.IsFavorite).ToList()
                : selectedCharacter.Designs.ToList();

            // Fallback to all designs if no favourites exist
            if (availableDesigns.Count == 0 && Configuration.RandomSelectionFavoritesOnly)
            {
                availableDesigns = selectedCharacter.Designs.ToList();
            }

            // Apply character first
            ExecuteMacro(selectedCharacter.Macros, selectedCharacter, null);
            SetActiveCharacter(selectedCharacter);

            string message = $"[Character Select+] Random selection: {selectedCharacter.Name}";

            // Apply random design if available
            if (availableDesigns.Count > 0)
            {
                var selectedDesign = availableDesigns[random.Next(availableDesigns.Count)];
                ExecuteMacro(selectedDesign.Macro, selectedCharacter, selectedDesign.Name);
                message += $" with design '{selectedDesign.Name}'";

                // Update last used design tracking
                Configuration.LastUsedDesignCharacterKey = selectedCharacter.Name;
                Configuration.LastUsedDesignByCharacter[selectedCharacter.Name] = selectedDesign.Name;
            }
            else
            {
                message += " (no designs available)";
            }

            // Apply poses if login is complete
            if (isLoginComplete)
            {
                if (selectedCharacter.IdlePoseIndex < 7)
                {
                    PoseManager.ApplyPose(PoseType.Idle, selectedCharacter.IdlePoseIndex);
                    Configuration.LastIdlePoseAppliedByPlugin = selectedCharacter.IdlePoseIndex;
                    Configuration.Save();
                }
                PoseRestorer.RestorePosesFor(selectedCharacter);
            }
            else
            {
                activeCharacter = selectedCharacter;
                shouldApplyPoses = true;
            }

            ChatGui.Print(message);
            SaveConfiguration();
        }
        public string? GetTargetedPlayerName()
        {
            try
            {
                var target = TargetManager.Target;

                if (target == null || target.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
                    return null;

                if (target is IPlayerCharacter player)
                {
                    string characterName = player.Name.TextValue;
                    string worldName = player.HomeWorld.Value.Name.ToString();

                    if (!string.IsNullOrWhiteSpace(characterName) && !string.IsNullOrWhiteSpace(worldName))
                    {
                        return $"{characterName}@{worldName}";
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error getting targeted player name: {ex.Message}");
            }

            return null;
        }
        private void MigrateBackgroundImageNames()
        {
            bool configChanged = false;

            foreach (var character in Configuration.Characters)
            {
                if (!string.IsNullOrEmpty(character.BackgroundImage))
                {
                    string oldName = character.BackgroundImage;
                    string newName = oldName;

                    // Convert PNG to JPG
                    if (oldName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        newName = oldName.Substring(0, oldName.Length - 4) + ".jpg";
                    }
                    // Add .jpg if no extension
                    else if (!oldName.Contains("."))
                    {
                        newName = oldName + ".jpg";
                    }

                    if (newName != oldName)
                    {
                        character.BackgroundImage = newName;
                        configChanged = true;
                        Log.Info($"Migrated background: {oldName} -> {newName}");
                    }
                }

                if (character.RPProfile != null && !string.IsNullOrEmpty(character.RPProfile.BackgroundImage))
                {
                    string oldName = character.RPProfile.BackgroundImage;
                    string newName = oldName;

                    if (oldName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        newName = oldName.Substring(0, oldName.Length - 4) + ".jpg";
                    }
                    else if (!oldName.Contains("."))
                    {
                        newName = oldName + ".jpg";
                    }

                    if (newName != oldName)
                    {
                        character.RPProfile.BackgroundImage = newName;
                        configChanged = true;
                        Log.Info($"Migrated RP profile background: {oldName} -> {newName}");
                    }
                }
            }

            if (configChanged)
            {
                SaveConfiguration();
                Log.Info("Background migration completed and saved");
            }
        }

        public Character? GetActiveCharacter()
        {
            if (ClientState.LocalPlayer?.HomeWorld.IsValid != true)
                return null;

            string localName = ClientState.LocalPlayer.Name.TextValue;
            string worldName = ClientState.LocalPlayer.HomeWorld.Value.Name.ToString();
            string fullKey = $"{localName}@{worldName}";

            // Find which CS+ character is currently active for this physical character
            if (ActiveProfilesByPlayerName.TryGetValue(fullKey, out var activeCharacterName))
            {
                return Characters.FirstOrDefault(c => c.Name == activeCharacterName);
            }

            return null;
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
        private void TryRestorePosesAfterLogin()
        {
            if (ClientState.LocalPlayer == null || activeCharacter == null)
                return;

            Plugin.Log.Debug("[SafeRestore] Applying poses after 5s login delay.");
            PoseRestorer.RestorePosesFor(activeCharacter);
        }
        public void AddCharacterAssignment(string realCharacter, string csCharacter)
        {
            Configuration.CharacterAssignments[realCharacter] = csCharacter;
            Configuration.Save();
        }

        public void RemoveCharacterAssignment(string realCharacter)
        {
            Configuration.CharacterAssignments.Remove(realCharacter);
            Configuration.Save();
        }

        public string? GetAssignedCharacter(string realCharacter)
        {
            return Configuration.CharacterAssignments.TryGetValue(realCharacter, out var assigned) ? assigned : null;
        }

    }
}

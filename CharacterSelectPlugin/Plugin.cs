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
using Dalamud.Game.ClientState.Objects.Types;
using static FFXIVClientStructs.FFXIV.Client.Game.Control.EmoteController;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game;
using System.Threading;
using System.Drawing.Imaging;
using Dalamud.Game.Text.SeStringHandling;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin
{
    public sealed class Plugin : IDalamudPlugin
    {
        public static Plugin? Instance { get; private set; }
        
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

        private static readonly string Version = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "(Unknown Version)";
        public static readonly string CurrentPluginVersion = Version; // Match repo.json and .csproj version


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
        public SecretModeModWindow? SecretModeModWindow { get; set; } = null;
        public enum SortType { Manual, Favorites, Alphabetical, Recent, Oldest }

        public List<Character> Characters => Configuration.Characters;
        public Vector3 NewCharacterColor { get; set; } = new Vector3(1.0f, 1.0f, 1.0f); // Default to white

        // Temporary fields for adding a new character
        public string NewCharacterName { get; set; } = "";
        public string NewCharacterMacros { get; set; } = "";
        public string? NewCharacterImagePath { get; set; }
        public List<CharacterDesign> NewCharacterDesigns { get; set; } = new();
        public Dictionary<string, bool>? NewSecretModState { get; set; } = null;
        public List<string>? NewSecretModPins { get; set; } = null;
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
        
        // Penumbra Integration
        public PenumbraIntegration PenumbraIntegration { get; private set; } = null!;
        public UserOverrideManager UserOverrideManager { get; private set; } = null!;
        
        // IPC Provider for other plugins
        private IPCProvider? ipcProvider;
        
        // Target application safety tracking
        private readonly Dictionary<int, DateTime> lastTargetApplicationTime = new();
        private readonly TimeSpan minimumTargetApplicationInterval = TimeSpan.FromSeconds(2);
        
        // Startup coordination between AutoLoad-LastUsed and DeferredStartup
        private bool characterAlreadyAppliedOnStartup = false;
        private bool autoLoadAlreadyRanThisStartup = false;
        
        
        // Target application IPC subscribers with correct signatures
        private ICallGateSubscriber<Dictionary<Guid, string>>? penumbraGetCollectionsIpc;
        private ICallGateSubscriber<int, Guid?, bool, bool, (int, (Guid, string)?)>? penumbraSetCollectionForObjectIpc;
        private ICallGateSubscriber<int, object>? penumbraRedrawObjectIpc;
        private ICallGateSubscriber<Dictionary<Guid, string>>? glamourerGetDesignsIpc;
        private ICallGateSubscriber<Guid, int, uint, ulong, int>? glamourerApplyDesignIpc;
        private ICallGateSubscriber<IList<(Guid, string, string, IList<(string, ushort, byte, ushort)>, int, bool)>>? customizePlusGetProfileListIpc;
        private ICallGateSubscriber<Guid, (int, string?)>? customizePlusGetByUniqueIdIpc;
        private ICallGateSubscriber<ushort, string, (int, Guid?)>? customizePlusSetTempProfileIpc;

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
        public bool IsSecretMode { get; set; } = false; // Now controlled by EnableConflictResolution setting
        internal Character? activeCharacter = null!;
        private string lastExecutedGearsetCommand = "";
        private DateTime lastGearsetCommandTime = DateTime.MinValue;
        private readonly Dictionary<string, (string characterName, string designName, DateTime time)> lastAppliedByJob = new();
        private readonly Dictionary<string, (string designName, DateTime time)> lastRandomDesignApplied = new();
        private readonly Dictionary<string, DateTime> lastDesignMacroExecuted = new();
        private bool randomDesignCRAppliedThisSession = false;
        
        // Conflict Resolution mod categorization cache
        internal Dictionary<string, ModType>? modCategorizationCache = null;
        private readonly string ModCacheFilePath;
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
                Instance = this;
                GameInteropProvider = gameInteropProvider;
                
                // Initialize cache file path
                ModCacheFilePath = Path.Combine(PluginInterface.GetPluginConfigDirectory(), "mod_categorization_cache.json");
                
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
            
            // Initialize Penumbra integration and services
            PenumbraIntegration = new PenumbraIntegration(PluginInterface, Log);
            
            // Test available IPC methods for debugging
            PenumbraIntegration.TestOnScreenMethods();

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
            
            // Cache initialization will happen after UserOverrideManager is ready

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
            
            // Initialize Penumbra integration services
            PenumbraIntegration = new PenumbraIntegration(PluginInterface, Log);
            UserOverrideManager = new UserOverrideManager(PluginInterface);
            
            // Initialize mod categorization cache now that UserOverrideManager is ready
            if (Configuration.EnableConflictResolution)
            {
                InitializeModCategorizationCache();
            }

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
            
            // Initialize SecretModeModWindow (Mod Manager)
            SecretModeModWindow = new SecretModeModWindow(this);
            WindowSystem.AddWindow(SecretModeModWindow);
            
            // Initialize IPC provider for other plugins
            ipcProvider = new IPCProvider(this, PluginInterface);
            
            MigrateBackgroundImageNames();

            // This player registering their profile, if someone else requests it
            provideProfile = PluginInterface.GetIpcProvider<string, RPProfile>("CharacterSelect.RPProfile.Provide");
            provideProfile.RegisterFunc(HandleProfileRequest);

            // This player sending a request to another
            requestProfile = PluginInterface.GetIpcSubscriber<string, RPProfile>("CharacterSelect.RPProfile.Provide");
            
            // Initialize target application IPC subscribers with correct signatures
            penumbraGetCollectionsIpc = PluginInterface.GetIpcSubscriber<Dictionary<Guid, string>>("Penumbra.GetCollections.V5");
            penumbraSetCollectionForObjectIpc = PluginInterface.GetIpcSubscriber<int, Guid?, bool, bool, (int, (Guid, string)?)>("Penumbra.SetCollectionForObject.V5");
            penumbraRedrawObjectIpc = PluginInterface.GetIpcSubscriber<int, object>("Penumbra.RedrawObject.V5");
            glamourerGetDesignsIpc = PluginInterface.GetIpcSubscriber<Dictionary<Guid, string>>("Glamourer.GetDesignList.V2");
            glamourerApplyDesignIpc = PluginInterface.GetIpcSubscriber<Guid, int, uint, ulong, int>("Glamourer.ApplyDesign");
            customizePlusGetProfileListIpc = PluginInterface.GetIpcSubscriber<IList<(Guid, string, string, IList<(string, ushort, byte, ushort)>, int, bool)>>("CustomizePlus.Profile.GetList");
            customizePlusGetByUniqueIdIpc = PluginInterface.GetIpcSubscriber<Guid, (int, string?)>("CustomizePlus.Profile.GetByUniqueId");
            customizePlusSetTempProfileIpc = PluginInterface.GetIpcSubscriber<ushort, string, (int, Guid?)>("CustomizePlus.Profile.SetTemporaryProfileOnCharacter");

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
                HelpMessage = "Use /select <Character Name> [Design Name] to apply a profile, /select random for random selection, /select jobchange on|off to toggle reapply on job change, /select mods to open Mod Manager, or /select save [CR] to save current look as design."
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
                lastAppliedCharacter = null;
                Plugin.Log.Debug($"[Character Select+] Local character name: {ClientState.LocalPlayer?.Name.TextValue}");
            };

            contextMenuManager = new ContextMenuManager(this, Plugin.ContextMenu);
            this.CleanupUnusedProfileImages();
            
            // Cleanup orphaned design preview images
            try
            {
                Windows.Components.DesignPanel.CleanupOrphanedPreviewImages(this);
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to cleanup orphaned preview images: {ex.Message}");
            }
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

            // Check if current character is assigned "None" - skip pose application
            if (ClientState.LocalPlayer != null && ClientState.LocalPlayer.HomeWorld.IsValid)
            {
                string world = ClientState.LocalPlayer.HomeWorld.Value.Name.ToString();
                string fullKey = $"{ClientState.LocalPlayer.Name.TextValue}@{world}";
                
                if (Configuration.CharacterAssignments.TryGetValue(fullKey, out var assignedCharacterName) && 
                    assignedCharacterName == "None")
                {
                    Plugin.Log.Debug($"[ApplyStoredPoses] Character {fullKey} assigned 'None' - skipping pose application");
                    return;
                }
            }

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
                        IsNSFW = character.RPProfile?.IsNSFW ?? false,

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

            // Capture original mod state if Conflict Resolution is enabled
            if (Configuration.EnableConflictResolution && character.OriginalCollectionState == null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var (success, collectionId, collectionName) = PenumbraIntegration.GetCurrentCollection();
                        if (success)
                        {
                            // Get all enabled mods in the collection
                            var modSettings = await Task.Run(() => PenumbraIntegration.GetAllModSettingsRobust(collectionId));
                            if (modSettings != null)
                            {
                                var enabledMods = modSettings
                                    .Where(kvp => kvp.Value.Item1) // Only enabled mods
                                    .Select(kvp => kvp.Key)
                                    .ToList();
                                
                                // Capture the current options for all enabled mods
                                character.OriginalCollectionState = PenumbraIntegration.CaptureCurrentModOptions(collectionId, enabledMods);
                                Log.Info($"Captured original mod state for {character.Name}: {character.OriginalCollectionState?.Count ?? 0} mods with options");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Error capturing original mod state: {ex}");
                    }
                });
            }
            
            // Switch Penumbra UI collection to match the character's collection
            if (!string.IsNullOrEmpty(character.PenumbraCollection))
            {
                var success = PenumbraIntegration.SwitchCollection(character.PenumbraCollection);
                if (success)
                {
                    Log.Information($"Successfully switched Penumbra UI collection to: {character.PenumbraCollection}");
                }
                else
                {
                    Log.Warning($"Failed to switch Penumbra UI collection to: {character.PenumbraCollection}");
                }
            }
            
            // Apply the character's macro
            ExecuteMacro(character.Macros, character, null);
            
            // Apply Secret Mode state - first character-level, then design-specific
            if (designIndex >= 0 && designIndex < character.Designs.Count)
            {
                var design = character.Designs[designIndex];
                
                // Apply design-specific Secret Mode state using proper design-level conflict resolution
                if (design.SecretModState != null && design.SecretModState.Any())
                {
                    _ = ApplyDesignModState(character, design);
                }
                else
                {
                    // Fallback to character-level Secret Mode state
                    _ = ApplySecretModState(character);
                }
                
                // Apply design-specific mod options if they exist
                if (design.ModOptionSettings != null && design.ModOptionSettings.Any())
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Get current collection
                            var (success, collectionId, collectionName) = PenumbraIntegration.GetCurrentCollection();
                            if (success)
                            {
                                // First, restore original state if available
                                if (character.OriginalCollectionState != null && character.OriginalCollectionState.Any())
                                {
                                    Log.Info($"Restoring original mod options before applying design '{design.Name}'");
                                    await PenumbraIntegration.ApplyModOptionsForDesign(collectionId, character.OriginalCollectionState);
                                    await Task.Delay(100); // Small delay to ensure state is applied
                                }
                                
                                // Then apply design-specific options
                                Log.Info($"Applying mod options for design '{design.Name}' in collection '{collectionName}'");
                                await PenumbraIntegration.ApplyModOptionsForDesign(collectionId, design.ModOptionSettings);
                            }
                            else
                            {
                                Log.Warning("Could not get current collection to apply mod options");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Error applying mod options for design: {ex}");
                        }
                    });
                }
                
                // Apply the design's macro (use advanced macro for CR mode)
                string macroText = design.IsAdvancedMode ? design.AdvancedMacro : design.Macro;
                ExecuteMacro(macroText, character, design.Name);
                
                // Track last used design for auto-reapplication (only for auto-reapplication and commands, not UI)
                Configuration.LastUsedDesignByCharacter[character.Name] = design.Name;
                Configuration.LastUsedDesignCharacterKey = character.Name;
                Configuration.Save();
            }
            else
            {
                // No design selected - just apply character-level Secret Mode state
                // But skip if a random design has already applied its CR this session
                if (!randomDesignCRAppliedThisSession)
                {
                    _ = ApplySecretModState(character);
                }
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
            Framework.Update -= FrameworkUpdate; // Fixed: should be -= not +=
            PoseManager?.Dispose();
            dialogueProcessor?.Dispose();
            
            // Dispose Penumbra integration services
            PenumbraIntegration?.Dispose();
            
            // Dispose IPC provider
            ipcProvider?.Dispose();
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
            
            Instance = null;
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
                ChatGui.PrintError("[Character Select+] Usage: /select <Character Name> [Optional Design Name], /select random [Character Name], /select jobchange on|off, /select mods, or /select save [CR]");
                return;
            }

            // Handle random selection
            if (args.Trim().StartsWith("random", StringComparison.OrdinalIgnoreCase))
            {
                var randomArgs = args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (randomArgs.Length == 1)
                {
                    // /select random - random character and design
                    SelectRandomCharacterAndDesign();
                }
                else if (randomArgs.Length == 2)
                {
                    // /select random CHARACTER - random design only from specific character
                    var targetCharacterName = randomArgs[1];
                    SelectRandomDesignOnly(targetCharacterName);
                }
                else
                {
                    ChatGui.PrintError("[Character Select+] Usage: /select random [Character Name]");
                }
                return;
            }

            // Handle jobchange on/off subcommand
            if (args.Trim().StartsWith("jobchange", StringComparison.OrdinalIgnoreCase))
            {
                var jobchangeArgs = args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (jobchangeArgs.Length == 2)
                {
                    string setting = jobchangeArgs[1].ToLower();
                    if (setting == "on")
                    {
                        Configuration.ReapplyDesignOnJobChange = true;
                        Configuration.Save();
                        ChatGui.Print("[Character Select+] Reapply design on job change: Enabled");
                        return;
                    }
                    else if (setting == "off")
                    {
                        Configuration.ReapplyDesignOnJobChange = false;
                        Configuration.Save();
                        ChatGui.Print("[Character Select+] Reapply design on job change: Disabled");
                        return;
                    }
                }
                ChatGui.PrintError("[Character Select+] Usage: /select jobchange on|off");
                return;
            }

            // Handle save subcommand
            if (args.Trim().StartsWith("save", StringComparison.OrdinalIgnoreCase))
            {
                HandleSaveCommand(args);
                return;
            }

            // Handle mods subcommand
            if (args.Trim().Equals("mods", StringComparison.OrdinalIgnoreCase))
            {
                if (SecretModeModWindow != null)
                {
                    if (SecretModeModWindow.IsOpen)
                    {
                        SecretModeModWindow.IsOpen = false;
                    }
                    else
                    {
                        // Open the mod manager in standalone mode (no specific character/design context)
                        SecretModeModWindow.Open(
                            characterIndex: null,
                            existingSelection: null,
                            existingPins: null,
                            saveCallback: null,
                            savePinsCallback: null,
                            design: null,
                            characterName: "Standalone Browser"
                        );
                    }
                }
                else
                {
                    ChatGui.PrintError("[Character Select+] Mod Manager is not available");
                }
                return;
            }

            // Rest of the existing method remains the same...
            var matches = Regex.Matches(args, "\"([^\"]+)\"|\\S+")
                .Cast<Match>()
                .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Value)
                .ToArray();

            if (matches.Length < 1)
            {
                ChatGui.PrintError("[Character Select+] Invalid usage. Use /select <Character Name> [Optional Design Name], /select random, /select jobchange on|off, /select mods, or /select save [CR]");
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
                    var designIndex = character.Designs.IndexOf(design);
                    ChatGui.Print($"[Character Select+] Applied design '{designName}' to {character.Name}.");
                    ApplyProfile(character, designIndex);
                }
                else
                {
                    ChatGui.PrintError($"[Character Select+] Design '{designName}' not found for {character.Name}.");
                }
            }
        }

        private void HandleSaveCommand(string args)
        {
            var parts = args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            // Validate command structure: /select save [CR]
            if (parts.Length < 1 || !parts[0].Equals("save", StringComparison.OrdinalIgnoreCase))
            {
                ChatGui.PrintError("[Character Select+] Usage: /select save [CR]");
                return;
            }
            
            // Parse CR flag - /select save [CR]
            bool useConflictResolution = false;
            
            if (parts.Length > 1 && parts[1].Equals("CR", StringComparison.OrdinalIgnoreCase))
            {
                if (Configuration.EnableConflictResolution)
                {
                    useConflictResolution = true;
                }
                else
                {
                    ChatGui.PrintError("[Character Select+] Conflict Resolution is not enabled in settings.");
                    return;
                }
            }

            // Find currently applied CS+ character 
            Character? currentCharacter = null;
            
            // Try to get the last used character for this player first
            var currentPlayerName = ClientState.LocalPlayer?.Name.TextValue;
            if (!string.IsNullOrEmpty(currentPlayerName) && Configuration.LastUsedCharacterByPlayer.ContainsKey(currentPlayerName))
            {
                var lastUsedCharacterName = Configuration.LastUsedCharacterByPlayer[currentPlayerName];
                currentCharacter = Characters.FirstOrDefault(c => c.Name.Equals(lastUsedCharacterName, StringComparison.OrdinalIgnoreCase));
            }
            
            // Fallback to the last used character key
            if (currentCharacter == null && !string.IsNullOrEmpty(Configuration.LastUsedCharacterKey))
            {
                currentCharacter = Characters.FirstOrDefault(c => c.Name.Equals(Configuration.LastUsedCharacterKey, StringComparison.OrdinalIgnoreCase));
            }
            
            // Final fallback to first character if available
            if (currentCharacter == null && Characters.Count > 0)
            {
                currentCharacter = Characters[0];
                ChatGui.Print($"[Character Select+] Using first available character: {currentCharacter.Name}");
            }

            if (currentCharacter == null)
            {
                ChatGui.PrintError("[Character Select+] No Character Select+ profiles available. Create a character profile first.");
                return;
            }

            // Use smart snapshot - same as Shift+Click or Ctrl+Shift+Click on the UI button
            var designPanel = MainWindow?.GetDesignPanel();
            if (designPanel != null)
            {
                // Call the smart snapshot method directly
                designPanel.CreateSmartSnapshotFromCommand(currentCharacter, useConflictResolution);
            }
            else
            {
                ChatGui.PrintError("[Character Select+] Unable to access design panel for snapshot creation.");
            }
        }

        private void CreateSnapshotDesignForCharacter(Character character, string designName, bool useConflictResolution)
        {
            Task.Run(async () =>
            {
                try
                {
                    ChatGui.Print($"[Character Select+] Creating design '{designName}' for {character.Name}...");

                    var newDesign = new CharacterDesign(
                        designName,
                        "echo Design snapshot created",
                        false,
                        "",
                        "",
                        "",
                        "",
                        null
                    );

                    // Check for clipboard image and save it
                    var clipboardImagePath = await SaveClipboardImageForDesign(Guid.NewGuid());
                    if (!string.IsNullOrEmpty(clipboardImagePath))
                    {
                        newDesign.PreviewImagePath = clipboardImagePath;
                        Log.Information($"Saved clipboard image for command snapshot: {clipboardImagePath}");
                    }

                    // Detect and set Glamourer design data
                    var glamourerData = await GetGlamourerDesignDataForCommand();
                    if (!string.IsNullOrEmpty(glamourerData))
                    {
                        newDesign.GlamourerDesign = glamourerData;
                    }

                    // Detect and set Customize+ profile
                    var customizePlusProfile = await GetCustomizePlusProfileForCommand();
                    if (!string.IsNullOrEmpty(customizePlusProfile))
                    {
                        newDesign.CustomizePlusProfile = customizePlusProfile;
                    }

                    // Generate appropriate macro
                    newDesign.Macro = GenerateCommandSnapshotMacro(newDesign, useConflictResolution);

                    // Add the design to the character
                    character.Designs.Add(newDesign);
                    
                    // Save configuration
                    Configuration.Save();

                    ChatGui.Print($"[Character Select+] ✅ Design '{designName}' created successfully!");
                    
                    if (string.IsNullOrEmpty(glamourerData) && string.IsNullOrEmpty(customizePlusProfile))
                    {
                        ChatGui.Print("[Character Select+] ⚠ No Glamourer or Customize+ state detected. You may need to manually configure the design.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Error creating snapshot design via command: {ex}");
                    ChatGui.PrintError($"[Character Select+] Failed to create design: {ex.Message}");
                }
            });
        }

        private async Task<string> GetGlamourerDesignDataForCommand()
        {
            try
            {
                // Get the current player's object ID
                var localPlayer = ClientState.LocalPlayer;
                if (localPlayer == null)
                {
                    Log.Warning("Local player not found for Glamourer design export");
                    return string.Empty;
                }

                // Use Glamourer IPC to export current design
                var glamourerExportIpc = PluginInterface.GetIpcSubscriber<int, uint, string>("Glamourer.GetDesignFromCharacter");
                var designData = await Task.Run(() => 
                {
                    try
                    {
                        return glamourerExportIpc.InvokeFunc(localPlayer.ObjectIndex, 0); // 0 for current design
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Glamourer IPC call failed: {ex.Message}");
                        return string.Empty;
                    }
                });

                if (!string.IsNullOrEmpty(designData))
                {
                    Log.Info("Successfully retrieved Glamourer design data via command");
                    return designData;
                }
                
                Log.Info("No Glamourer design data available");
                return string.Empty;
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to get Glamourer design data via command: {ex}");
                return string.Empty;
            }
        }

        private async Task<string> GetCustomizePlusProfileForCommand()
        {
            try
            {
                // Get the current player's object ID
                var localPlayer = ClientState.LocalPlayer;
                if (localPlayer == null)
                {
                    Log.Warning("Local player not found for Customize+ profile export");
                    return string.Empty;
                }

                // Use Customize+ IPC to get the active profile for the current character
                var customizePlusActiveProfileIpc = PluginInterface.GetIpcSubscriber<ushort, (int, Guid?)>("CustomizePlus.Profile.GetActiveProfileIdOnCharacter");
                var customizePlusExportIpc = PluginInterface.GetIpcSubscriber<Guid, (int, string?)>("CustomizePlus.Profile.GetProfileById");
                
                var profileData = await Task.Run(() => 
                {
                    try
                    {
                        // Get active profile ID for current character
                        var activeProfileResult = customizePlusActiveProfileIpc.InvokeFunc((ushort)localPlayer.ObjectIndex);
                        if (activeProfileResult.Item1 != 0 || !activeProfileResult.Item2.HasValue)
                        {
                            Log.Info("No active Customize+ profile found for current character");
                            return string.Empty;
                        }

                        // Export the active profile
                        var profileExportResult = customizePlusExportIpc.InvokeFunc(activeProfileResult.Item2.Value);
                        if (profileExportResult.Item1 != 0 || string.IsNullOrEmpty(profileExportResult.Item2))
                        {
                            Log.Warning("Failed to export Customize+ profile data");
                            return string.Empty;
                        }

                        return profileExportResult.Item2;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Customize+ IPC call failed: {ex.Message}");
                        return string.Empty;
                    }
                });

                if (!string.IsNullOrEmpty(profileData))
                {
                    Log.Info("Successfully retrieved Customize+ profile data via command");
                    return profileData;
                }
                
                Log.Info("No Customize+ profile data available");
                return string.Empty;
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to get Customize+ profile via command: {ex}");
                return string.Empty;
            }
        }

        private string GenerateCommandSnapshotMacro(CharacterDesign design, bool useConflictResolution)
        {
            var macroLines = new List<string>();

            // Add Glamourer apply if we have it
            if (!string.IsNullOrEmpty(design.GlamourerDesign))
            {
                if (useConflictResolution && Configuration.EnableConflictResolution)
                {
                    // Use Glamourer apply with design name for conflict resolution
                    macroLines.Add($"/glamourer apply \"{design.Name}\" to self");
                }
                else
                {
                    // Use standard Glamourer apply
                    macroLines.Add($"/glamourer apply \"{design.Name}\" to self");
                }
            }

            // Add Customize+ apply if we have it
            if (!string.IsNullOrEmpty(design.CustomizePlusProfile))
            {
                // Use proper Customize+ command syntax
                macroLines.Add($"/customize+ set temporary \"{design.Name}\"");
            }

            // Add a redraw command to refresh appearance
            if (macroLines.Count > 0)
            {
                macroLines.Add("/penumbra redraw self");
            }

            // Add a default message if no automation was detected
            if (macroLines.Count == 0)
            {
                macroLines.Add("echo No automation detected - manual setup required");
            }

            return string.Join("\n", macroLines);
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
                : NewCharacterTag.Split(',').Select(f => f.Trim()).ToList(),
                    SecretModState = NewSecretModState,
                    SecretModPins = NewSecretModPins
                };

                // Auto-create a Design based on Glamourer Design if available
                if (!string.IsNullOrWhiteSpace(NewGlamourerDesign))
                {
                    string defaultDesignName = $"{NewCharacterName} {NewGlamourerDesign}";
                    var defaultDesign = new CharacterDesign(
                    defaultDesignName,
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

                    // If this is a CR Character, make the auto-design inherit the CR mod state
                    if (Configuration.EnableConflictResolution && NewSecretModState != null && NewSecretModState.Any())
                    {
                        // Copy the character's mod state to the design
                        defaultDesign.SecretModState = new Dictionary<string, bool>(NewSecretModState);
                        
                        // Copy pin overrides if any exist
                        if (NewSecretModPins != null && NewSecretModPins.Any())
                        {
                            // Design pin overrides would unpin character pins, but for auto-generated design,
                            // we want to keep the same pins, so we don't set SecretModPinOverrides
                        }
                        
                        Log.Information($"Auto-generated design '{defaultDesignName}' inherited CR mod state with {NewSecretModState.Count} mod selections");
                    }

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
                NewSecretModState = null;
                NewSecretModPins = null;

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

        private void InitializeModCategorizationCache()
        {
            if (modCategorizationCache != null || PenumbraIntegration?.IsPenumbraAvailable != true)
                return;

            try
            {
                Log.Info("Initializing mod categorization cache...");

                // Try to load cache from disk first
                ModCacheData? diskCache = null;
                if (File.Exists(ModCacheFilePath))
                {
                    try
                    {
                        var json = File.ReadAllText(ModCacheFilePath);
                        diskCache = JsonConvert.DeserializeObject<ModCacheData>(json);
                        Log.Info($"Loaded mod cache from disk (version {diskCache?.Version}, {diskCache?.Mods.Count} mods)");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Failed to load mod cache from disk: {ex.Message}");
                    }
                }

                // Get current mod list from Penumbra
                var currentModList = PenumbraIntegration.GetModList() ?? new Dictionary<string, string>();
                
                if (!currentModList.Any())
                {
                    Log.Warning("No mods found in Penumbra, skipping cache initialization");
                    modCategorizationCache = new Dictionary<string, ModType>();
                    return;
                }

                modCategorizationCache = new Dictionary<string, ModType>();
                var cacheData = new ModCacheData { LastUpdated = DateTime.UtcNow };
                var needsSave = false;
                var newModCount = 0;
                var removedModCount = 0;

                // Process current mods
                foreach (var (modDir, modName) in currentModList)
                {
                    // Check if we have cached data
                    if (diskCache?.Mods.ContainsKey(modDir) == true)
                    {
                        // Use cached categorization
                        var cached = diskCache.Mods[modDir];
                        modCategorizationCache[modDir] = cached.Type;
                        cacheData.Mods[modDir] = new ModCacheEntry
                        {
                            Name = modName,
                            Type = cached.Type,
                            LastSeen = DateTime.UtcNow
                        };
                    }
                    else
                    {
                        // New mod - categorize it
                        var modType = SecretModeModWindow.DetermineModType(modDir, modName, this);
                        modCategorizationCache[modDir] = modType;
                        cacheData.Mods[modDir] = new ModCacheEntry
                        {
                            Name = modName,
                            Type = modType,
                            LastSeen = DateTime.UtcNow
                        };
                        newModCount++;
                        needsSave = true;
                    }
                }

                // Check for removed mods
                if (diskCache != null)
                {
                    foreach (var modDir in diskCache.Mods.Keys)
                    {
                        if (!currentModList.ContainsKey(modDir))
                        {
                            removedModCount++;
                            needsSave = true;
                        }
                    }
                }

                // Save cache if changed
                if (needsSave || diskCache == null)
                {
                    SaveModCache(cacheData);
                }

                Log.Info($"Mod categorization cache initialized with {modCategorizationCache.Count} mods " +
                         $"({newModCount} new, {removedModCount} removed)");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to initialize mod categorization cache: {ex}");
                modCategorizationCache = new Dictionary<string, ModType>();
            }
        }
        
        private void SaveModCache(ModCacheData cacheData)
        {
            try
            {
                var json = JsonConvert.SerializeObject(cacheData, Formatting.Indented);
                File.WriteAllText(ModCacheFilePath, json);
                Log.Info($"Saved mod cache to disk ({cacheData.Mods.Count} mods)");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to save mod cache to disk: {ex}");
            }
        }
        
        // Methods for updating mod cache from Penumbra events
        public void UpdateModCache(string modDir, string modName, ModType modType)
        {
            try
            {
                // Load current cache from disk
                ModCacheData? diskCache = null;
                if (File.Exists(ModCacheFilePath))
                {
                    var json = File.ReadAllText(ModCacheFilePath);
                    diskCache = JsonConvert.DeserializeObject<ModCacheData>(json);
                }
                
                if (diskCache == null)
                    diskCache = new ModCacheData { LastUpdated = DateTime.UtcNow };
                
                // Add new mod entry
                diskCache.Mods[modDir] = new ModCacheEntry
                {
                    Name = modName,
                    Type = modType,
                    LastSeen = DateTime.UtcNow
                };
                diskCache.LastUpdated = DateTime.UtcNow;
                
                // Save updated cache
                SaveModCache(diskCache);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to update mod cache for new mod {modDir}: {ex}");
            }
        }
        
        public void RemoveFromModCache(string modDir)
        {
            try
            {
                // Load current cache from disk
                if (!File.Exists(ModCacheFilePath))
                    return;
                
                var json = File.ReadAllText(ModCacheFilePath);
                var diskCache = JsonConvert.DeserializeObject<ModCacheData>(json);
                
                if (diskCache == null)
                    return;
                
                // Remove mod entry
                if (diskCache.Mods.Remove(modDir))
                {
                    diskCache.LastUpdated = DateTime.UtcNow;
                    SaveModCache(diskCache);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to remove mod from cache {modDir}: {ex}");
            }
        }
        
        public void MoveInModCache(string oldModDir, string newModDir, string modName, ModType modType)
        {
            try
            {
                // Load current cache from disk
                if (!File.Exists(ModCacheFilePath))
                    return;
                
                var json = File.ReadAllText(ModCacheFilePath);
                var diskCache = JsonConvert.DeserializeObject<ModCacheData>(json);
                
                if (diskCache == null)
                    return;
                
                // Remove old entry and add new entry
                diskCache.Mods.Remove(oldModDir);
                diskCache.Mods[newModDir] = new ModCacheEntry
                {
                    Name = modName,
                    Type = modType,
                    LastSeen = DateTime.UtcNow
                };
                diskCache.LastUpdated = DateTime.UtcNow;
                
                SaveModCache(diskCache);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to move mod in cache {oldModDir} -> {newModDir}: {ex}");
            }
        }

        public static string ConvertMacroToConflictResolution(string macro)
        {
            if (string.IsNullOrWhiteSpace(macro))
                return macro;
                
            var lines = macro.Split('\n').Select(l => l.Trim()).ToList();
            
            // Remove bulktag lines when explicitly converting to Conflict Resolution
            lines.RemoveAll(l => l.StartsWith("/penumbra bulktag", StringComparison.OrdinalIgnoreCase));
            
            return string.Join("\n", lines);
        }

        public static string SanitizeDesignMacro(string macro, CharacterDesign design, Character character, bool enableAutomations)
        {
            var lines = macro.Split('\n').Select(l => l.Trim()).ToList();

            // Remove automation lines if automations are disabled
            if (!enableAutomations)
            {
                lines.RemoveAll(l => l.StartsWith("/glamour automation enable", StringComparison.OrdinalIgnoreCase));
            }
            // Add automation if missing (only if enabled)
            else if (!lines.Any(l => l.StartsWith("/glamour automation enable", StringComparison.OrdinalIgnoreCase)))
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

        public static string ConvertToSecretModeMacro(string macro, string? characterPenumbraCollection = null)
        {
            if (string.IsNullOrWhiteSpace(macro))
                return macro;

            // Check if Conflict Resolution is enabled - if so, don't generate bulktag commands
            if (Plugin.Instance?.Configuration?.EnableConflictResolution == true)
                return macro;

            var lines = macro.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
            var result = new List<string>();
            
            // Extract values from existing commands
            string? penumbraCollection = null;
            string? glamourerDesign = null;
            
            foreach (var line in lines)
            {
                if (line.StartsWith("/penumbra collection individual", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 2)
                        penumbraCollection = parts[1].Trim();
                }
                else if (line.StartsWith("/glamour apply", StringComparison.OrdinalIgnoreCase) && !line.Contains("no clothes"))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 1)
                    {
                        var designPart = parts[0].Substring("/glamour apply".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(designPart))
                            glamourerDesign = designPart;
                    }
                }
            }

            // Use provided character collection if available, otherwise use extracted collection
            string collectionToUse = !string.IsNullOrWhiteSpace(characterPenumbraCollection) 
                ? characterPenumbraCollection 
                : penumbraCollection;

            // Add secret mode commands at the beginning (for designs)
            if (!string.IsNullOrWhiteSpace(characterPenumbraCollection) && !string.IsNullOrWhiteSpace(glamourerDesign))
            {
                result.Add($"/penumbra bulktag disable {characterPenumbraCollection} | gear");
                result.Add($"/penumbra bulktag disable {characterPenumbraCollection} | hair");
                result.Add($"/penumbra bulktag enable {characterPenumbraCollection} | {glamourerDesign}");
                result.Add("/glamour apply no clothes | self");
            }
            // Add secret mode commands for characters (with collection line)
            else if (!string.IsNullOrWhiteSpace(collectionToUse) && !string.IsNullOrWhiteSpace(glamourerDesign))
            {
                result.Add($"/penumbra collection individual | {collectionToUse} | self");
                result.Add($"/penumbra bulktag disable {collectionToUse} | gear");
                result.Add($"/penumbra bulktag disable {collectionToUse} | hair");
                result.Add($"/penumbra bulktag enable {collectionToUse} | {glamourerDesign}");
                result.Add("/glamour apply no clothes | self");
            }

            // Add all other lines, replacing the original penumbra collection and glamour apply commands
            foreach (var line in lines)
            {
                if (line.StartsWith("/penumbra collection individual", StringComparison.OrdinalIgnoreCase))
                {
                    // Skip - already added above
                    continue;
                }
                else if (line.StartsWith("/glamour apply", StringComparison.OrdinalIgnoreCase) && !line.Contains("no clothes"))
                {
                    // Replace with our version
                    if (!string.IsNullOrWhiteSpace(glamourerDesign))
                        result.Add($"/glamour apply {glamourerDesign} | self");
                }
                else
                {
                    // Keep all other lines (including custom commands, honorific, moodle, etc.)
                    result.Add(line);
                }
            }

            return string.Join("\n", result);
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

        public static async Task UploadProfileAsync(RPProfile profile, string characterName, bool isCharacterApplication = true)
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

                // Only preserve server NSFW for character applications, not profile editor saves
                if (isCharacterApplication)
                {
                    try 
                    {
                        using var checkHttp = CreateAuthenticatedHttpClient();
                        var galleryResponse = await checkHttp.GetAsync("https://character-select-profile-server-production.up.railway.app/gallery?nsfw=true");
                        
                        if (galleryResponse.IsSuccessStatusCode)
                        {
                            var galleryJson = await galleryResponse.Content.ReadAsStringAsync();
                            var galleryProfiles = JsonConvert.DeserializeObject<List<GalleryProfile>>(galleryJson);
                            
                            // Find this character's profile in gallery
                            var existingGalleryProfile = galleryProfiles?.FirstOrDefault(p => 
                                p.CharacterName == (profile.CharacterName ?? characterName) ||
                                p.CharacterId.Contains(characterName));
                            
                            if (existingGalleryProfile != null && existingGalleryProfile.IsNSFW)
                            {
                                // Server has NSFW=true, preserve it for character applications
                                profile.IsNSFW = true;
                                Plugin.Log.Debug($"[UploadProfile] Preserving server NSFW=true for character application: {characterName}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Debug($"[UploadProfile] Could not check server NSFW status: {ex.Message}");
                    }
                }
                else
                {
                    Plugin.Log.Debug($"[UploadProfile] Profile editor save - respecting user's NSFW choice: {profile.IsNSFW} for {characterName}");
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
                    // Check if current character has any assignment - skip job change reapplication to let assignment logic handle it
                    if (player.HomeWorld.IsValid)
                    {
                        string world = player.HomeWorld.Value.Name.ToString();
                        string fullKey = $"{player.Name.TextValue}@{world}";
                        
                        if (Configuration.CharacterAssignments.TryGetValue(fullKey, out var assignedCharacterName))
                        {
                            Plugin.Log.Debug($"[JobSwitch] Character {fullKey} has assignment '{assignedCharacterName}' - skipping job change reapplication to let assignment logic handle it");
                            return;
                        }
                    }

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
                randomDesignCRAppliedThisSession = false; // Reset for new session
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
                string? assignmentValue = hasAssignment ? Configuration.CharacterAssignments[fullKey] : null;

                if (characterAlreadyAppliedOnStartup)
                {
                    Plugin.Log.Debug($"[DeferredStartup] Skipping - character already applied by AutoLoad-LastUsed");
                }
                else if (assignmentValue == "None")
                {
                    Plugin.Log.Debug($"[DeferredStartup] Skipping - {fullKey} assigned to 'None'");
                }
                else if (hasAssignment)
                {
                    Plugin.Log.Debug($"[DeferredStartup] Skipping session profile '{_pendingSessionCharacterName}' - {fullKey} has specific assignment to '{assignmentValue}'");
                }
                else
                {
                    var character = Characters.FirstOrDefault(c =>
                        c.Name.Equals(_pendingSessionCharacterName, StringComparison.OrdinalIgnoreCase));

                    if (character != null)
                    {
                        Plugin.Log.Debug($"[DeferredStartup] Applying session profile: {_pendingSessionCharacterName}");
                        ApplyProfile(character, -1);
                        characterAlreadyAppliedOnStartup = true; // Mark as handled
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
            // Prevent AutoLoad-LastUsed from running multiple times during startup
            if (autoLoadAlreadyRanThisStartup)
            {
                return;
            }
            autoLoadAlreadyRanThisStartup = true;

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
                    int designIndex = GetLastUsedDesignIndex(assignedCharacter);
                    Plugin.Log.Debug($"[AutoLoad-Assignment] Design index for {assignedCharacter.Name}: {designIndex}");
                    ApplyProfile(assignedCharacter, designIndex);
                    lastAppliedCharacter = fullKey;
                    characterAlreadyAppliedOnStartup = true; // Mark as handled
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
                    int designIndex = GetLastUsedDesignIndex(character);
                    Plugin.Log.Debug($"[AutoLoad-LastUsed] Design index for {character.Name}: {designIndex}");
                    ApplyProfile(character, designIndex);
                    lastAppliedCharacter = fullKey;
                    characterAlreadyAppliedOnStartup = true; // Mark as handled
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
        
        private int GetLastUsedDesignIndex(Character character)
        {
            // Return -1 (no design) if design auto-reapplication is disabled
            if (!Configuration.EnableLastUsedDesignAutoload)
            {
                Plugin.Log.Debug($"[AutoLoad-Design] Design auto-reapplication is disabled");
                return -1;
            }
            
            Plugin.Log.Debug($"[AutoLoad-Design] Design auto-reapplication is ENABLED for {character.Name}");
            
            // Try to get the last used design for this character
            if (Configuration.LastUsedDesignByCharacter.TryGetValue(character.Name, out var lastDesignName))
            {
                // Find the design with the matching name
                var design = character.Designs.FirstOrDefault(d => d.Name.Equals(lastDesignName, StringComparison.OrdinalIgnoreCase));
                if (design != null)
                {
                    int designIndex = character.Designs.IndexOf(design);
                    Plugin.Log.Debug($"[AutoLoad-Design] ✅ Found last used design '{lastDesignName}' at index {designIndex} for {character.Name}");
                    return designIndex;
                }
                else
                {
                    Plugin.Log.Debug($"[AutoLoad-Design] ❌ Last used design '{lastDesignName}' not found for {character.Name}");
                }
            }
            else
            {
                Plugin.Log.Debug($"[AutoLoad-Design] ❌ No last used design stored for {character.Name}");
            }
            
            return -1; // No design or not found
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
                
                // Create colored fallback message
                var builder = new SeStringBuilder();
                builder.AddText("[").AddBlue("Character Select+", true).AddText("] ");
                builder.AddText("No favourite characters found, selecting from ").AddBlue("all characters", false).AddText(".");
                ChatGui.Print(builder.BuiltString);
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

            // Switch Penumbra UI collection to match the character's collection
            if (!string.IsNullOrEmpty(selectedCharacter.PenumbraCollection))
            {
                var success = PenumbraIntegration.SwitchCollection(selectedCharacter.PenumbraCollection);
                if (success)
                {
                    Log.Information($"Successfully switched Penumbra UI collection to: {selectedCharacter.PenumbraCollection}");
                }
                else
                {
                    Log.Warning($"Failed to switch Penumbra UI collection to: {selectedCharacter.PenumbraCollection}");
                }
            }

            // Apply random design if available
            if (availableDesigns.Count > 0)
            {
                var selectedDesign = availableDesigns[random.Next(availableDesigns.Count)];
                ExecuteMacro(selectedDesign.Macro, selectedCharacter, selectedDesign.Name);

                // Apply conflict resolution if enabled
                if (Configuration.EnableConflictResolution)
                {
                    // Apply mod state asynchronously first, then execute macro with proper threading
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ApplyDesignModState(selectedCharacter, selectedDesign);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Error applying conflict resolution for random selection: {ex}");
                        }
                    });
                }

                // Update last used design tracking
                Configuration.LastUsedDesignCharacterKey = selectedCharacter.Name;
                Configuration.LastUsedDesignByCharacter[selectedCharacter.Name] = selectedDesign.Name;
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

            // Update character tracking for job change and quick switch features
            if (ClientState.LocalPlayer != null)
            {
                string playerKey = ClientState.LocalPlayer.Name.ToString();
                Configuration.LastUsedCharacterByPlayer[playerKey] = selectedCharacter.Name;
            }

            // Update Quick Switch window to reflect the new selection (same as normal character click)
            QuickSwitchWindow.UpdateSelectionFromCharacter(selectedCharacter);

            // Send themed chat message if enabled
            if (Configuration.ShowRandomSelectionChatMessages)
            {
                SeString message = GetRandomSelectionChatMessage(selectedCharacter.Name);
                ChatGui.Print(message);
            }
            SaveConfiguration();
        }

        public void SelectRandomDesignOnly(string characterName)
        {
            var character = Characters.FirstOrDefault(c => c.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));
            if (character == null)
            {
                ChatGui.PrintError($"[Character Select+] Character '{characterName}' not found.");
                return;
            }

            // Get available designs for the character
            var availableDesigns = Configuration.RandomSelectionFavoritesOnly
                ? character.Designs.Where(d => d.IsFavorite).ToList()
                : character.Designs.ToList();

            // Fallback to all designs if no favourites exist
            if (availableDesigns.Count == 0 && Configuration.RandomSelectionFavoritesOnly)
            {
                availableDesigns = character.Designs.ToList();
            }

            if (availableDesigns.Count == 0)
            {
                ChatGui.PrintError($"[Character Select+] No designs available for character '{characterName}'.");
                return;
            }

            // Select random design
            var random = new Random();
            var selectedDesign = availableDesigns[random.Next(availableDesigns.Count)];

            
            // Apply conflict resolution if this design has it
            if (Configuration.EnableConflictResolution && selectedDesign.SecretModState != null && selectedDesign.SecretModState.Any())
            {
                ApplyDesignModState(character, selectedDesign).GetAwaiter().GetResult();
                // Set flag to prevent character CR from overwriting design CR
                randomDesignCRAppliedThisSession = true;
            }
            
            // Execute the selected design's macro
            ExecuteMacro(selectedDesign.Macro, character, selectedDesign.Name);

            // Update last used design tracking
            Configuration.LastUsedDesignCharacterKey = character.Name;
            Configuration.LastUsedDesignByCharacter[character.Name] = selectedDesign.Name;
            Configuration.Save();

            // Send themed chat message if enabled
            if (Configuration.ShowRandomSelectionChatMessages)
            {
                SeString message = GetRandomSelectionChatMessage(character.Name);
                ChatGui.Print(message);
            }
        }

        private SeString GetRandomSelectionChatMessage(string characterName)
        {
            var random = new Random();
            var builder = new SeStringBuilder();
            
            // Check if Halloween theme is active
            bool isHalloween = SeasonalThemeManager.IsSeasonalThemeEnabled(Configuration) && 
                              SeasonalThemeManager.GetEffectiveTheme(Configuration) == SeasonalTheme.Halloween;
            
            // Check if Winter/Christmas theme is active
            bool isWinterChristmas = SeasonalThemeManager.IsSeasonalThemeEnabled(Configuration) &&
                                    (SeasonalThemeManager.GetEffectiveTheme(Configuration) == SeasonalTheme.Winter ||
                                     SeasonalThemeManager.GetEffectiveTheme(Configuration) == SeasonalTheme.Christmas);
            
            if (isHalloween)
            {
                // Add purple plugin prefix for Halloween
                builder.AddText("[").AddPurple("Character Select+", true).AddText("] ");
                
                // Halloween themed messages with purple text and white character names
                var halloweenMessages = new System.Action<SeStringBuilder, string>[]
                {
                    (b, name) => b.AddWhite(name, true).AddPurple(" was transformed by a mysterious hex...", false),
                    (b, name) => b.AddPurple("Dark magic coursed through ", false).AddWhite(name, true).AddPurple("...", false),
                    (b, name) => b.AddPurple("A wicked spell took hold of ", false).AddWhite(name, true).AddPurple("...", false),
                    (b, name) => b.AddWhite(name, true).AddPurple(" was bewitched by ancient forces...", false),
                    (b, name) => b.AddPurple("Shadowy enchantments changed ", false).AddWhite(name, true).AddPurple("...", false),
                    (b, name) => b.AddWhite(name, true).AddPurple(" fell under a malevolent charm...", false),
                    (b, name) => b.AddPurple("Otherworldly powers possessed ", false).AddWhite(name, true).AddPurple("...", false),
                    (b, name) => b.AddPurple("Spectral forces reshaped ", false).AddWhite(name, true).AddPurple("...", false),
                    (b, name) => b.AddWhite(name, true).AddPurple(" was cursed with a new form...", false),
                    (b, name) => b.AddPurple("Eerie magic enveloped ", false).AddWhite(name, true).AddPurple("...", false),
                    (b, name) => b.AddWhite(name, true).AddPurple(" succumbed to dark sorcery...", false),
                    (b, name) => b.AddPurple("Sinister whispers changed ", false).AddWhite(name, true).AddPurple("...", false),
                    (b, name) => b.AddWhite(name, true).AddPurple(" was touched by haunting magic...", false),
                    (b, name) => b.AddPurple("Phantom energies transformed ", false).AddWhite(name, true).AddPurple("...", false),
                    (b, name) => b.AddWhite(name, true).AddPurple(" was enshrouded by mystical darkness...", false),
                    (b, name) => b.AddPurple("Forbidden rituals altered ", false).AddWhite(name, true).AddPurple("...", false),
                    (b, name) => b.AddWhite(name, true).AddPurple(" was influenced by ethereal shadows...", false),
                    (b, name) => b.AddPurple("Necromantic forces shifted ", false).AddWhite(name, true).AddPurple("...", false)
                };
                
                var selectedMessage = halloweenMessages[random.Next(halloweenMessages.Length)];
                selectedMessage(builder, characterName);
            }
            else if (isWinterChristmas)
            {
                // Add silver/white plugin prefix for Winter/Christmas
                builder.AddText("[").AddWhite("Character Select+", true).AddText("] ");
                
                // Winter/Christmas themed messages with blue text and white character names
                var winterMessages = new System.Action<SeStringBuilder, string>[]
                {
                    (b, name) => b.AddWhite(name, true).AddBlue(" was touched by winter magic...", false),
                    (b, name) => b.AddBlue("Crystalline frost transformed ", false).AddWhite(name, true).AddBlue("...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" was blessed by snowfall...", false),
                    (b, name) => b.AddBlue("The spirit of winter embraced ", false).AddWhite(name, true).AddBlue("...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" received a frosty makeover...", false),
                    (b, name) => b.AddBlue("Icicle magic reshaped ", false).AddWhite(name, true).AddBlue("...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" was gifted by winter winds...", false),
                    (b, name) => b.AddBlue("A Christmas miracle changed ", false).AddWhite(name, true).AddBlue("...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" was wrapped in holiday cheer...", false),
                    (b, name) => b.AddBlue("Seasonal enchantment visited ", false).AddWhite(name, true).AddBlue("...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" received festive inspiration...", false),
                    (b, name) => b.AddBlue("The magic of the season touched ", false).AddWhite(name, true).AddBlue("...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" was blessed with winter wonder...", false),
                    (b, name) => b.AddBlue("Holiday spirit transformed ", false).AddWhite(name, true).AddBlue("...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" was kissed by snowflakes...", false),
                    (b, name) => b.AddBlue("A winter's dream reshaped ", false).AddWhite(name, true).AddBlue("...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" was touched by frost magic...", false),
                    (b, name) => b.AddBlue("Festive energy flowed through ", false).AddWhite(name, true).AddBlue("...", false),
                    // Gift-opening themed messages
                    (b, name) => b.AddWhite(name, true).AddBlue(" unwrapped a magical transformation...", false),
                    (b, name) => b.AddBlue("A festive surprise awaited ", false).AddWhite(name, true).AddBlue("...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" discovered holiday magic in their gift box...", false),
                    (b, name) => b.AddBlue("A wrapped present transformed ", false).AddWhite(name, true).AddBlue("...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" opened a winter wonderland makeover...", false),
                    (b, name) => b.AddBlue("Surprise gift magic changed ", false).AddWhite(name, true).AddBlue("...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" revealed a Christmas surprise transformation...", false),
                    (b, name) => b.AddBlue("A magical holiday package blessed ", false).AddWhite(name, true).AddBlue("...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" unwrapped their festive destiny...", false),
                    (b, name) => b.AddBlue("Gift box magic flowed through ", false).AddWhite(name, true).AddBlue("...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" found seasonal enchantment in a wrapped box...", false),
                    (b, name) => b.AddBlue("A surprise gift revealed ", false).AddWhite(name, true).AddBlue("'s new form...", false)
                };
                
                var selectedMessage = winterMessages[random.Next(winterMessages.Length)];
                selectedMessage(builder, characterName);
            }
            else
            {
                // Add blue plugin prefix for normal
                builder.AddText("[").AddBlue("Character Select+", true).AddText("] ");
                
                // Normal themed messages with blue text and white character names
                var normalMessages = new System.Action<SeStringBuilder, string>[]
                {
                    (b, name) => b.AddWhite(name, true).AddBlue(" underwent a random transformation...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" adopted a new appearance...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" received a style makeover...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" embraced a different look...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" was given a fresh appearance...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" changed their style...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" got a random makeover...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" experimented with a new design...", false),
                    (b, name) => b.AddBlue("Fashion magic touched ", false).AddWhite(name, true).AddBlue("...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" discovered a fresh aesthetic...", false),
                    (b, name) => b.AddBlue("Style inspiration struck ", false).AddWhite(name, true).AddBlue("...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" refreshed their entire wardrobe...", false),
                    (b, name) => b.AddBlue("Creative energy flowed through ", false).AddWhite(name, true).AddBlue("...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" reinvented their personal style...", false),
                    (b, name) => b.AddBlue("Aesthetic inspiration influenced ", false).AddWhite(name, true).AddBlue("...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" explored a bold new direction...", false),
                    (b, name) => b.AddBlue("Design innovation guided ", false).AddWhite(name, true).AddBlue("...", false),
                    (b, name) => b.AddWhite(name, true).AddBlue(" embraced creative transformation...", false)
                };
                
                var selectedMessage = normalMessages[random.Next(normalMessages.Length)];
                selectedMessage(builder, characterName);
            }
            
            return builder.BuiltString;
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
            if (ClientState.LocalPlayer == null)
                return;

            var currentActiveCharacter = GetActiveCharacter();
            if (currentActiveCharacter == null)
            {
                Plugin.Log.Debug("[SafeRestore] No active character found for current player");
                return;
            }

            Plugin.Log.Debug("[SafeRestore] Applying poses after 5s login delay.");
            PoseRestorer.RestorePosesFor(currentActiveCharacter);
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

        public void SwitchPenumbraCollection(string collectionName)
        {
            try
            {
                // Use the proper PenumbraIntegration method instead of old IPC call
                bool result = PenumbraIntegration.SwitchCollection(collectionName);
                
                if (result)
                {
                    Log.Info($"[Penumbra] Successfully switched to collection: {collectionName}");
                }
                else
                {
                    Log.Warning($"[Penumbra] Failed to switch to collection: {collectionName}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Penumbra] Error switching to collection '{collectionName}': {ex.Message}");
            }
        }
        
        public async Task ApplyDesignModState(Character character, CharacterDesign design)
        {
            // Check if Conflict Resolution is enabled
            if (!Configuration.EnableConflictResolution || design.SecretModState == null)
                return;

            try
            {
                Log.Info($"Applying Conflict Resolution mod state for design: {design.Name}");
                
                // Check if Penumbra is available
                if (PenumbraIntegration?.IsPenumbraAvailable != true)
                {
                    Log.Warning("Penumbra is not available - skipping Conflict Resolution mod state application");
                    return;
                }
                
                // Get current collection
                var getCurrentCollection = PluginInterface.GetIpcSubscriber<byte, (Guid, string)?>("Penumbra.GetCollection");
                var currentCollectionResult = getCurrentCollection?.InvokeFunc(0xE2); // ApiCollectionType.Current
                
                if (currentCollectionResult?.Item1 == null)
                {
                    Log.Warning("Could not get current Penumbra collection for design mod state");
                    return;
                }
                
                var collectionId = currentCollectionResult.Value.Item1;
                
                // Get all mod settings to determine what needs to be disabled
                var getAllModSettings = PluginInterface.GetIpcSubscriber<Guid, bool, bool, int, (int, Dictionary<string, (bool, int, Dictionary<string, List<string>>, bool, bool)>?)>("Penumbra.GetAllModSettings");
                var result = getAllModSettings?.InvokeFunc(collectionId, false, false, 0);
                
                if (result?.Item2 == null)
                {
                    Log.Warning("Could not get mod settings for design mod state");
                    return;
                }
                
                var trySetMod = PluginInterface.GetIpcSubscriber<Guid, string, string, bool, int>("Penumbra.TrySetMod.V5");
                if (trySetMod == null)
                {
                    Log.Warning("TrySetMod IPC not available for design mod state");
                    return;
                }
                
                // Use cached mod categorization (fast lookup)
                var pinnedMods = new HashSet<string>(character.SecretModPins ?? new List<string>());
                
                // Get mod categories from cache (should be instant)
                var modCategories = new Dictionary<string, CharacterSelectPlugin.Windows.ModType>();
                if (modCategorizationCache == null)
                {
                    Log.Error("Mod categorization cache not initialized! Cannot apply Conflict Resolution.");
                    return;
                }
                
                foreach (var (modDir, settings) in result.Value.Item2)
                {
                    if (modCategorizationCache.ContainsKey(modDir))
                    {
                        modCategories[modDir] = modCategorizationCache[modDir];
                    }
                    else
                    {
                        Log.Warning($"Mod {modDir} not found in categorization cache, skipping");
                    }
                }
                
                // Get all mods that should be treated as "character-specific"
                // This includes: Gear, Hair (always), and any individually checked mods from other categories
                var managedMods = new HashSet<string>();
                
                // Always include all Gear and Hair mods
                foreach (var (modDir, settings) in result.Value.Item2)
                {
                    if (modCategories.ContainsKey(modDir))
                    {
                        var modType = modCategories[modDir];
                        if (modType == ModType.Gear || modType == ModType.Hair)
                        {
                            managedMods.Add(modDir);
                        }
                    }
                }
                
                // Add individually checked mods from other categories (current design)
                foreach (var (modDir, isSelected) in design.SecretModState)
                {
                    if (isSelected)
                    {
                        managedMods.Add(modDir);
                    }
                }
                
                // Also include any mods that have been selected in ANY design for this character
                // These should be treated like Gear/Hair mods (managed across all design switches)
                foreach (var characterDesign in character.Designs)
                {
                    if (characterDesign.SecretModState != null)
                    {
                        foreach (var (modDir, wasSelected) in characterDesign.SecretModState)
                        {
                            if (wasSelected)
                            {
                                managedMods.Add(modDir);
                            }
                        }
                    }
                }
                
                // Also check character-level selections if they exist
                if (character.SecretModState != null)
                {
                    foreach (var (modDir, wasSelected) in character.SecretModState)
                    {
                        if (wasSelected)
                        {
                            managedMods.Add(modDir);
                        }
                    }
                }
                
                // Build lists of mods to disable and enable
                var modsToDisable = new List<string>();
                var modsToEnable = new List<string>();
                
                // First pass: Collect mods that need to be disabled
                foreach (var modDir in managedMods)
                {
                    // Skip if we don't have settings for this mod
                    if (!result.Value.Item2.ContainsKey(modDir))
                        continue;
                    
                    var settings = result.Value.Item2[modDir];
                    
                    // NEVER disable pinned mods
                    if (pinnedMods.Contains(modDir))
                    {
                        continue;
                    }
                    
                    // If mod is enabled and not in our selection, disable it
                    if (settings.Item1 && (!design.SecretModState.ContainsKey(modDir) || !design.SecretModState[modDir]))
                    {
                        modsToDisable.Add(modDir);
                    }
                }
                
                // Second pass: Collect mods that need to be enabled
                foreach (var (modDir, shouldEnable) in design.SecretModState)
                {
                    if (shouldEnable)
                    {
                        modsToEnable.Add(modDir);
                    }
                }
                
                // Execute all disable operations in parallel
                var disableTasks = modsToDisable.Select(modDir => 
                    Task.Run(() => trySetMod.InvokeFunc(collectionId, modDir, "", false))
                ).ToArray();
                
                // Execute all enable operations in parallel
                var enableTasks = modsToEnable.Select(modDir => 
                    Task.Run(() => trySetMod.InvokeFunc(collectionId, modDir, "", true))
                ).ToArray();
                
                // Wait for all operations to complete
                await Task.WhenAll(disableTasks.Concat(enableTasks));
                
                Log.Info($"[DEBUG] Conflict Resolution for design '{design.Name}': Disabled {modsToDisable.Count} mods, Enabled {modsToEnable.Count} mods");

                // Apply design-specific mod option settings (detailed configurations)
                if (design.ModOptionSettings != null && design.ModOptionSettings.Any())
                {
                    Log.Info($"Applying mod option settings for design '{design.Name}' - {design.ModOptionSettings.Count} mods with custom options");
                    await Task.Delay(50); // Small delay before applying detailed options
                    PenumbraIntegration?.ApplyModOptionsForDesign(collectionId, design.ModOptionSettings);
                }

                Log.Info($"Applied Conflict Resolution mod state for design '{design.Name}' - {design.SecretModState.Count} mods configured");
            }
            catch (Exception ex)
            {
                Log.Error($"Error applying design mod state: {ex}");
            }
        }
        
        public async Task ApplySecretModState(Character character)
        {
            // Check if Conflict Resolution is enabled
            if (!Configuration.EnableConflictResolution)
                return;
                
            if (character.SecretModState == null || !character.SecretModState.Any())
                return;
                
            try
            {
                Log.Info($"Applying Secret Mode mod state for character: {character.Name}");
                
                // Get current collection
                var getCurrentCollection = PluginInterface.GetIpcSubscriber<byte, (Guid, string)?>("Penumbra.GetCollection");
                var currentCollectionResult = getCurrentCollection?.InvokeFunc(0xE2); // ApiCollectionType.Current
                
                if (currentCollectionResult?.Item1 == null)
                {
                    Log.Warning("Could not get current Penumbra collection");
                    return;
                }
                
                var collectionId = currentCollectionResult.Value.Item1;
                
                // Get all mod settings to determine what needs to be disabled
                var getAllModSettings = PluginInterface.GetIpcSubscriber<Guid, bool, bool, int, (int, Dictionary<string, (bool, int, Dictionary<string, List<string>>, bool, bool)>?)>("Penumbra.GetAllModSettings");
                var result = getAllModSettings?.InvokeFunc(collectionId, false, false, 0);
                
                if (result?.Item2 == null)
                {
                    Log.Warning("Could not get mod settings");
                    return;
                }
                
                var trySetMod = PluginInterface.GetIpcSubscriber<Guid, string, string, bool, int>("Penumbra.TrySetMod.V5");
                if (trySetMod == null)
                {
                    Log.Warning("TrySetMod IPC not available");
                    return;
                }
                
                var pinnedMods = new HashSet<string>(character.SecretModPins ?? new List<string>());
                
                // Use cached mod categorization (fast lookup)
                var modCategories = new Dictionary<string, CharacterSelectPlugin.Windows.ModType>();
                if (modCategorizationCache == null)
                {
                    Log.Error("Mod categorization cache not initialized! Cannot apply Conflict Resolution.");
                    return;
                }
                
                foreach (var (modDir, settings) in result.Value.Item2)
                {
                    if (modCategorizationCache.ContainsKey(modDir))
                    {
                        modCategories[modDir] = modCategorizationCache[modDir];
                    }
                    else
                    {
                        Log.Warning($"Mod {modDir} not found in categorization cache, skipping");
                    }
                }
                
                // Get all mods that should be treated as "character-specific"
                // This includes: Gear, Hair (always), and any individually checked mods from other categories
                var managedMods = new HashSet<string>();
                
                // Always include all Gear and Hair mods
                foreach (var (modDir, settings) in result.Value.Item2)
                {
                    if (modCategories.ContainsKey(modDir))
                    {
                        var modType = modCategories[modDir];
                        if (modType == ModType.Gear || modType == ModType.Hair)
                        {
                            managedMods.Add(modDir);
                        }
                    }
                }
                
                // Add individually checked mods from other categories (treat them like Gear/Hair)
                foreach (var (modDir, isSelected) in character.SecretModState)
                {
                    if (isSelected)
                    {
                        managedMods.Add(modDir);
                    }
                }
                
                // Also include any mods that have been selected in ANY design for this character
                // These should be treated like Gear/Hair mods (managed when applying character)
                foreach (var characterDesign in character.Designs)
                {
                    if (characterDesign.SecretModState != null)
                    {
                        foreach (var (modDir, wasSelected) in characterDesign.SecretModState)
                        {
                            if (wasSelected)
                            {
                                managedMods.Add(modDir);
                            }
                        }
                    }
                }
                
                // Build lists of mods to disable and enable
                var modsToDisable = new List<string>();
                var modsToEnable = new List<string>();
                
                // First pass: Collect mods that need to be disabled
                foreach (var modDir in managedMods)
                {
                    // Skip if we don't have settings for this mod
                    if (!result.Value.Item2.ContainsKey(modDir))
                        continue;
                    
                    var settings = result.Value.Item2[modDir];
                    
                    // NEVER disable pinned mods
                    if (pinnedMods.Contains(modDir))
                    {
                        continue;
                    }
                    
                    // If mod is enabled and not in our selection, disable it
                    if (settings.Item1 && (!character.SecretModState.ContainsKey(modDir) || !character.SecretModState[modDir]))
                    {
                        modsToDisable.Add(modDir);
                    }
                }
                
                // Second pass: Collect mods that need to be enabled
                foreach (var (modDir, shouldEnable) in character.SecretModState)
                {
                    if (shouldEnable)
                    {
                        modsToEnable.Add(modDir);
                    }
                }
                
                // Execute all disable operations in parallel
                var disableTasks = modsToDisable.Select(modDir => 
                    Task.Run(() => trySetMod.InvokeFunc(collectionId, modDir, "", false))
                ).ToArray();
                
                // Execute all enable operations in parallel
                var enableTasks = modsToEnable.Select(modDir => 
                    Task.Run(() => trySetMod.InvokeFunc(collectionId, modDir, "", true))
                ).ToArray();
                
                // Wait for all operations to complete
                await Task.WhenAll(disableTasks.Concat(enableTasks));
                
                Log.Info($"Applied Conflict Resolution mod state for character '{character.Name}' - {character.SecretModState.Count} mods configured");
            }
            catch (Exception ex)
            {
                Log.Error($"Error applying Secret Mode mod state: {ex}");
            }
        }
        
        /// <summary>
        /// Simplified mod type determination for switching logic
        /// </summary>
        private CharacterSelectPlugin.Windows.ModType DetermineModTypeForSwitching(string modDir, string modName)
        {
            // This is a simplified version - in reality, you'd want to reuse the logic from SecretModeModWindow
            // For now, we'll use basic name/path analysis
            var nameLower = modName.ToLowerInvariant();
            var dirLower = modDir.ToLowerInvariant();
            
            // Basic categorization
            if (nameLower.Contains("hair") || dirLower.Contains("hair")) return ModType.Hair;
            if (nameLower.Contains("body") || nameLower.Contains("bibo") || nameLower.Contains("tbse")) return ModType.Body;
            if (nameLower.Contains("face") && !nameLower.Contains("face paint")) return ModType.Face;
            if (nameLower.Contains("eye") || nameLower.Contains("iris")) return ModType.Eyes;
            if (nameLower.Contains("tattoo")) return ModType.Tattoos;
            if (nameLower.Contains("face paint") || nameLower.Contains("makeup")) return ModType.FacePaint;
            if (nameLower.Contains("ear") || nameLower.Contains("tail")) return ModType.EarsTails;
            if (nameLower.Contains("mount")) return ModType.Mount;
            if (nameLower.Contains("minion") || nameLower.Contains("companion")) return ModType.Minion;
            if (nameLower.Contains("emote") || nameLower.Contains("pose") || nameLower.Contains("idle")) return ModType.Emote;
            if (nameLower.Contains("vfx") || nameLower.Contains("effect")) return ModType.VFX;
            if (nameLower.Contains("skeleton") || nameLower.Contains("bone")) return ModType.Skeleton;
            
            // Default to gear for anything else
            return ModType.Gear;
        }


        // Cache data structures
        private class ModCacheData
        {
            public int Version { get; set; } = 1;
            public DateTime LastUpdated { get; set; }
            public Dictionary<string, ModCacheEntry> Mods { get; set; } = new();
        }
        
        private class ModCacheEntry
        {
            public string Name { get; set; } = "";
            public ModType Type { get; set; }
            public DateTime LastSeen { get; set; }
        }

        /// <summary>
        /// Apply character/design to target using direct IPC calls instead of macros
        /// This works for GPose actors spawned by Brio/Ktisis unlike the old macro approach
        /// </summary>
        public async Task<bool> ApplyToTarget(Character character, int designIndex = -1)
        {
            try
            {
                // Get target info from the main thread to ensure fresh data
                IGameObject? targetObject = null;
                await Framework.RunOnFrameworkThread(() =>
                {
                    targetObject = GetCurrentTarget();
                });
                
                if (targetObject == null)
                {
                    ChatGui.PrintError("[Character Select+] No valid target selected.");
                    return false;
                }
                
                
                return await ApplyToTarget(character, designIndex, (int)targetObject.ObjectIndex, targetObject.ObjectKind, targetObject.Name?.ToString() ?? "Unknown");
            }
            catch (Exception ex)
            {
                Log.Error($"Error applying character to target: {ex}");
                ChatGui.PrintError($"[Character Select+] Failed to apply to target: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get the current target with proper handling for GPose and regular gameplay
        /// </summary>
        public IGameObject? GetCurrentTarget()
        {
            try
            {
                // Check if we're in GPose first
                var isInGPose = ClientState.IsGPosing;
                
                // Get all available targets
                var target = TargetManager.Target;
                var softTarget = TargetManager.SoftTarget;
                var focusTarget = TargetManager.FocusTarget;
                var mouseOverTarget = TargetManager.MouseOverTarget;
                
                // In GPose, targeting works differently - show more ObjectTable info
                if (isInGPose)
                {
                    for (int i = 0; i < Math.Min(ObjectTable.Length, 20); i++)
                    {
                        var obj = ObjectTable[i];
                        if (obj != null)
                        {
                            // GPose object scan without debug logging
                        }
                    }
                    
                    // In GPose, try GPoseTarget from native TargetSystem (what Ktisis/Brio/Glamourer use)
                    if (target == null)
                    {
                        
                        try
                        {
                            unsafe
                            {
                                var targetSystem = TargetSystem.Instance();
                                if (targetSystem != null && targetSystem->GPoseTarget != null)
                                {
                                    var gposeTarget = ObjectTable.CreateObjectReference((IntPtr)targetSystem->GPoseTarget);
                                    if (gposeTarget != null)
                                    {
                                        return gposeTarget;
                                    }
                                    else
                                    {
                                    }
                                }
                                else
                                {
                                    Log.Debug($"[GetCurrentTarget] No GPoseTarget set - user needs to target a GPose actor first");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[GetCurrentTarget] Error accessing GPoseTarget: {ex}");
                        }
                        
                        // Fallback to other targeting methods
                        if (softTarget != null)
                        {
                            Log.Debug($"[GetCurrentTarget] Fallback to SoftTarget: {softTarget.Name} (Index: {softTarget.ObjectIndex})");
                            return softTarget;
                        }
                        
                        if (focusTarget != null)
                        {
                                return focusTarget;
                        }
                        
                        Log.Warning($"[GetCurrentTarget] No target found - user needs to select a GPose actor using Ktisis/Brio 'Target Actor'");
                        return null;
                    }
                }
                else
                {
                    // Regular gameplay - show fewer objects
                    for (int i = 0; i < Math.Min(ObjectTable.Length, 10); i++)
                    {
                        var obj = ObjectTable[i];
                        if (obj != null)
                        {
                            // Object scan logic without debug logging
                        }
                    }
                }
                
                return target;
            }
            catch (Exception ex)
            {
                Log.Error($"[GetCurrentTarget] Error: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Check if a target is a valid type for appearance modifications
        /// </summary>
        private bool IsValidTargetForModification(IGameObject target)
        {
            // Only allow modification of players, GPose actors, and companions
            return target.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player ||
                   target.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc ||
                   target.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion;
        }
        
        /// <summary>
        /// Comprehensive validation for target object safety before modifications
        /// </summary>
        private async Task<bool> ValidateTargetObjectSafety(int objectIndex, string targetName)
        {
            try
            {
                // Get the current target object to validate it's still valid
                IGameObject? targetObject = null;
                await Framework.RunOnFrameworkThread(() =>
                {
                    if (objectIndex >= 0 && objectIndex < ObjectTable.Length)
                    {
                        targetObject = ObjectTable[objectIndex];
                    }
                });
                
                if (targetObject == null)
                {
                    Log.Warning($"[ValidateTarget] Target object at index {objectIndex} is null");
                    return false;
                }
                
                // Validate object is still valid and hasn't been destroyed/replaced
                if (targetObject.Address == nint.Zero)
                {
                    Log.Warning($"[ValidateTarget] Target object at index {objectIndex} has invalid address");
                    return false;
                }
                
                // Validate object name hasn't changed (indicates object was replaced)
                var currentName = targetObject.Name?.ToString() ?? "";
                if (!string.IsNullOrEmpty(targetName) && !currentName.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warning($"[ValidateTarget] Target name changed from '{targetName}' to '{currentName}' - object may have been replaced");
                    return false;
                }
                
                // Check if we're in a cutscene or other unsafe state
                if (Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInCutSceneEvent] ||
                    Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.WatchingCutscene78] ||
                    Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInEvent])
                {
                    Log.Warning($"[ValidateTarget] Cannot modify target during cutscene or event");
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[ValidateTarget] Error validating target safety: {ex}");
                return false;
            }
        }
        
        /// <summary>
        /// Apply character/design to target using direct IPC calls - thread-safe overload
        /// </summary>
        public async Task<bool> ApplyToTarget(Character character, int designIndex, int objectIndex, Dalamud.Game.ClientState.Objects.Enums.ObjectKind objectKind, string targetName)
        {
            try
            {
                // Validate target type - accept Players, NPCs, and GPose actors
                var validTypes = new[] { 
                    Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player, 
                    Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc, 
                    Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc,
                    Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion 
                };
                
                if (!validTypes.Contains(objectKind))
                {
                    ChatGui.PrintError($"[Character Select+] Invalid target type: {objectKind}");
                    return false;
                }
                
                // Check for rapid successive applications to prevent crashes
                if (lastTargetApplicationTime.TryGetValue(objectIndex, out var lastTime))
                {
                    var timeSinceLastApplication = DateTime.Now - lastTime;
                    if (timeSinceLastApplication < minimumTargetApplicationInterval)
                    {
                        var waitTime = minimumTargetApplicationInterval - timeSinceLastApplication;
                        ChatGui.PrintError($"[Character Select+] Please wait {waitTime.TotalSeconds:F1} seconds between target applications to prevent crashes");
                        return false;
                    }
                }
                
                // Update last application time
                lastTargetApplicationTime[objectIndex] = DateTime.Now;
                
                // Comprehensive safety validation before any modifications
                if (!await ValidateTargetObjectSafety(objectIndex, targetName))
                {
                    ChatGui.PrintError($"[Character Select+] Target object validation failed - cannot apply safely");
                    return false;
                }
                
                Log.Information($"[ApplyToTarget] Applying {character.Name} to target: {targetName} (Index: {objectIndex})");
                
                // Get the macro text to parse
                string macroText;
                if (designIndex >= 0 && designIndex < character.Designs.Count)
                {
                    var design = character.Designs[designIndex];
                    macroText = design.IsAdvancedMode ? design.AdvancedMacro : design.Macro;
                }
                else
                {
                    macroText = character.Macros;
                }
                
                // Apply Conflict Resolution mod state to target BEFORE macro commands (like regular character application)
                if (Configuration.EnableConflictResolution)
                {
                    await ApplyConflictResolutionToTarget(character, designIndex, objectIndex);
                }
                
                // Parse macro text and execute via IPC after conflict resolution has set up mod state
                var commands = ParseMacroForTargetApplication(macroText);
                var success = await ExecuteTargetCommands(commands, objectIndex);
                
                return success;
            }
            catch (Exception ex)
            {
                Log.Error($"Error applying character to target: {ex}");
                ChatGui.PrintError($"[Character Select+] Failed to apply to target: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Apply Conflict Resolution mod state to target object
        /// </summary>
        private async Task ApplyConflictResolutionToTarget(Character character, int designIndex, int objectIndex)
        {
            try
            {
                
                // For target application, we need to determine what collection to apply conflict resolution to
                // This could be either from a collection switch command or the character's main collection
                string targetCollectionName = "";
                Guid currentCollectionId = Guid.Empty;
                
                // Extract collection name from character macro or design macro
                string macroText;
                if (designIndex >= 0 && designIndex < character.Designs.Count)
                {
                    var design = character.Designs[designIndex];
                    macroText = design.IsAdvancedMode ? design.AdvancedMacro : design.Macro;
                }
                else
                {
                    macroText = character.Macros;
                }
                
                // First, try to find a collection name in the macro
                var lines = macroText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.Contains("/penumbra collection individual", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split('|');
                        if (parts.Length >= 2)
                        {
                            targetCollectionName = parts[1].Trim();
                            break;
                        }
                    }
                }
                
                // If no collection command found in macro, use the character's main collection
                if (string.IsNullOrEmpty(targetCollectionName))
                {
                    targetCollectionName = character.PenumbraCollection;
                }
                
                if (string.IsNullOrEmpty(targetCollectionName))
                {
                    return;
                }
                
                // Get the target collection ID
                var collections = penumbraGetCollectionsIpc?.InvokeFunc();
                if (collections == null)
                {
                    return;
                }
                
                var targetCollection = collections.FirstOrDefault(kvp => 
                    string.Equals(kvp.Value, targetCollectionName, StringComparison.OrdinalIgnoreCase));
                
                if (targetCollection.Key == Guid.Empty)
                {
                    return;
                }
                
                currentCollectionId = targetCollection.Key;
                
                // Check if we have any conflict resolution data to apply
                bool hasCharacterMods = character.SecretModState != null && character.SecretModState.Any();
                bool hasDesignMods = designIndex >= 0 && designIndex < character.Designs.Count && 
                                   character.Designs[designIndex].SecretModState != null && 
                                   character.Designs[designIndex].SecretModState.Any();
                
                if (!hasCharacterMods && !hasDesignMods)
                {
                    return; // Exit early if no conflict resolution data is available
                }
                
                // Apply character-level mod state first
                if (hasCharacterMods)
                {
                    await ApplyModStateToObject(character, currentCollectionId, objectIndex);
                }
                
                // Apply design-specific mod state if we're applying a specific design
                if (hasDesignMods)
                {
                    var design = character.Designs[designIndex];
                    
                    // Create a temporary character with the design's mod state for ApplyModStateToObject
                    var tempCharacter = new Character("", "", null, new List<CharacterDesign>(), Vector3.Zero, "", "", "", "", "", "", Vector3.Zero, Vector3.Zero, "", "", "") 
                    { 
                        SecretModState = design.SecretModState 
                    };
                    await ApplyModStateToObject(tempCharacter, currentCollectionId, objectIndex);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error applying Conflict Resolution to target: {ex}");
            }
        }
        
        /// <summary>
        /// Apply mod state to a specific object (target)
        /// </summary>
        private async Task ApplyModStateToObject(Character character, Guid collectionId, int objectIndex)
        {
            try
            {
                if (character.SecretModState == null || !character.SecretModState.Any())
                    return;
                
                // Get all mod settings for the collection using robust method
                var modSettings = PenumbraIntegration?.GetAllModSettingsRobust(collectionId);
                if (modSettings == null)
                {
                    return;
                }
                
                // Use robust IPC calling for TrySetMod as well - actually test the methods work
                ICallGateSubscriber<Guid, string, string, bool, int>? trySetModIpc = null;
                var trySetModMethods = new[] { "Penumbra.TrySetMod.V5", "Penumbra.TrySetMod" };
                
                foreach (var method in trySetModMethods)
                {
                    try
                    {
                        var testIpc = PluginInterface.GetIpcSubscriber<Guid, string, string, bool, int>(method);
                        if (testIpc != null)
                        {
                            // Actually test if the method is available by making a dummy call
                            // This will throw if the method isn't registered
                            var dummyResult = testIpc.InvokeFunc(Guid.Empty, "", "", false);
                            // If we get here, the method works (even if it returns an error, it's registered)
                            trySetModIpc = testIpc;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        continue;
                    }
                }
                
                if (trySetModIpc == null)
                {
                    return;
                }
                
                var modsToDisable = new List<string>();
                var modsToEnable = new List<string>();
                
                // Collect mods that need state changes
                foreach (var (modDir, shouldEnable) in character.SecretModState)
                {
                    if (!modSettings.ContainsKey(modDir))
                        continue;
                    
                    var currentSettings = modSettings[modDir];
                    var isCurrentlyEnabled = currentSettings.Item1;
                    
                    if (isCurrentlyEnabled != shouldEnable)
                    {
                        if (shouldEnable)
                            modsToEnable.Add(modDir);
                        else
                            modsToDisable.Add(modDir);
                    }
                }
                
                // Apply changes with error handling like normal character application
                var allTasks = new List<Task>();
                
                foreach (var modDir in modsToDisable)
                {
                    allTasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            var result = trySetModIpc.InvokeFunc(collectionId, modDir, "", false);
                            if (result != 0) // 0 = Success
                            {
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[ApplyToTarget] Error disabling mod {modDir} on object {objectIndex}: {ex.Message}");
                        }
                    }));
                }
                
                foreach (var modDir in modsToEnable)
                {
                    allTasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            var result = trySetModIpc.InvokeFunc(collectionId, modDir, "", true);
                            if (result != 0) // 0 = Success
                            {
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[ApplyToTarget] Error enabling mod {modDir} on object {objectIndex}: {ex.Message}");
                        }
                    }));
                }
                
                await Task.WhenAll(allTasks);
            }
            catch (Exception ex)
            {
                Log.Error($"Error applying mod state to object: {ex}");
            }
        }
        
        /// <summary>
        /// Parse macro text and extract commands for target application
        /// </summary>
        private List<object> ParseMacroForTargetApplication(string macroText)
        {
            var commands = new List<object>();
            
            if (string.IsNullOrEmpty(macroText))
            {
                return commands;
            }
            
            var lines = macroText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//"))
                {
                    continue;
                }
                
                // Parse Penumbra collection commands - handle "individual" format: /penumbra collection individual | CollectionName | self
                if (trimmed.Contains("/penumbra collection individual", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmed.Split('|');
                    if (parts.Length >= 2)
                    {
                        var collectionName = parts[1].Trim();
                        if (!string.IsNullOrEmpty(collectionName))
                        {
                            commands.Add(new PenumbraCommand { CollectionName = collectionName });
                        }
                    }
                }
                // Parse Glamourer design commands - handle pipe format: /glamour apply DesignName | Self
                else if (trimmed.Contains("/glamour apply", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmed.Split('|');
                    if (parts.Length >= 1)
                    {
                        var designPart = parts[0].Replace("/glamour apply", "").Trim();
                        if (!string.IsNullOrEmpty(designPart))
                        {
                            commands.Add(new GlamourerCommand { DesignName = designPart });
                        }
                    }
                }
                // Parse CustomizePlus profile commands - handle enable format: /customize profile enable <me>, ProfileName
                else if (trimmed.Contains("/customize profile enable", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmed.Split(',');
                    if (parts.Length >= 2)
                    {
                        var profileName = parts[1].Trim();
                        if (!string.IsNullOrEmpty(profileName))
                        {
                            commands.Add(new CustomizePlusCommand { ProfileName = profileName });
                        }
                    }
                }
            }
            
            return commands;
        }
        
        /// <summary>
        /// Extract quoted parameter from command line
        /// </summary>
        private string ExtractQuotedParameter(string commandLine)
        {
            var firstQuote = commandLine.IndexOf('"');
            if (firstQuote == -1) return "";
            
            var secondQuote = commandLine.IndexOf('"', firstQuote + 1);
            if (secondQuote == -1) return "";
            
            return commandLine.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
        }
        
        /// <summary>
        /// Execute parsed commands via IPC for target application with enhanced safety
        /// </summary>
        private async Task<bool> ExecuteTargetCommands(List<object> commands, int objectIndex)
        {
            bool allSucceeded = true;
            
            foreach (var command in commands)
            {
                try
                {
                    // Validate object is still safe before each command - must run on main thread
                    IGameObject? targetObject = null;
                    await Framework.RunOnFrameworkThread(() =>
                    {
                        targetObject = objectIndex >= 0 && objectIndex < ObjectTable.Length ? ObjectTable[objectIndex] : null;
                    });
                    
                    if (targetObject?.Address == nint.Zero)
                    {
                        Log.Warning($"[ExecuteTargetCommands] Target object at index {objectIndex} became invalid, aborting remaining commands");
                        allSucceeded = false;
                        break;
                    }
                    
                    switch (command)
                    {
                        case PenumbraCommand penCmd:
                            allSucceeded &= await ExecutePenumbraCommand(penCmd, (uint)objectIndex);
                            break;
                        case GlamourerCommand glamCmd:
                            allSucceeded &= await ExecuteGlamourerCommand(glamCmd, (uint)objectIndex);
                            break;
                        case CustomizePlusCommand custCmd:
                            allSucceeded &= await ExecuteCustomizePlusCommand(custCmd, (uint)objectIndex);
                            break;
                    }
                    
                    // Increased delay between commands to allow game to process changes safely
                    await Task.Delay(200);
                    
                    // Additional validation after equipment/weapon related commands
                    if (command is GlamourerCommand)
                    {
                        // Extra delay after Glamourer commands to prevent weapon loading race conditions
                        await Task.Delay(300);
                        
                        // Validate object is still valid after glamour changes - must run on main thread
                        await Framework.RunOnFrameworkThread(() =>
                        {
                            targetObject = objectIndex >= 0 && objectIndex < ObjectTable.Length ? ObjectTable[objectIndex] : null;
                        });
                        
                        if (targetObject?.Address == nint.Zero)
                        {
                            Log.Warning($"[ExecuteTargetCommands] Target object became invalid after Glamourer command");
                            allSucceeded = false;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Error executing command {command}: {ex}");
                    allSucceeded = false;
                    
                    // If we get an exception, add a longer delay before continuing
                    await Task.Delay(500);
                }
            }
            
            // Final validation and redraw with enhanced safety - this is where LoadWeapon crashes occurred
            if (allSucceeded)
            {
                // Final object validation before redraw to prevent LoadWeapon crashes - must run on main thread
                IGameObject? finalTargetObject = null;
                await Framework.RunOnFrameworkThread(() =>
                {
                    finalTargetObject = objectIndex >= 0 && objectIndex < ObjectTable.Length ? ObjectTable[objectIndex] : null;
                });
                
                if (finalTargetObject?.Address == nint.Zero)
                {
                    Log.Warning($"[ExecuteTargetCommands] Target object became invalid before final redraw, skipping redraw");
                    return false;
                }
                
                // Add delay before redraw to ensure all previous changes have been processed
                await Task.Delay(500);
                
                // Final redraw with enhanced retry logic
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        // Re-validate object before each redraw attempt - must run on main thread
                        await Framework.RunOnFrameworkThread(() =>
                        {
                            finalTargetObject = objectIndex >= 0 && objectIndex < ObjectTable.Length ? ObjectTable[objectIndex] : null;
                        });
                        
                        if (finalTargetObject?.Address == nint.Zero)
                        {
                            Log.Warning($"[ExecuteTargetCommands] Target object became invalid during redraw attempts");
                            break;
                        }
                        
                        penumbraRedrawObjectIpc?.InvokeFunc((int)objectIndex);
                        break;
                    }
                    catch (Dalamud.Plugin.Ipc.Exceptions.IpcNotReadyError)
                    {
                        if (attempt < 3)
                        {
                            await Task.Delay(attempt * 200);
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[ApplyToTarget] Failed to trigger redraw on attempt {attempt}: {ex}");
                        if (attempt < 3)
                        {
                            await Task.Delay(attempt * 300);
                        }
                        else
                        {
                            Log.Error($"[ApplyToTarget] All redraw attempts failed, this may prevent visual updates");
                            break;
                        }
                    }
                }
            }
            else
            {
                Log.Warning($"[ExecuteTargetCommands] Some commands failed, skipping final redraw for safety");
            }
            
            return allSucceeded;
        }
        
        /// <summary>
        /// Execute Penumbra collection command with retry logic
        /// </summary>
        private async Task<bool> ExecutePenumbraCommand(PenumbraCommand command, uint objectIndex)
        {
            const int maxRetries = 5;
            const int baseDelayMs = 200;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Get available collections
                    var collections = penumbraGetCollectionsIpc?.InvokeFunc();
                    if (collections == null)
                    {
                        return false;
                    }
                    
                    // Find collection by name
                    var targetCollection = collections.FirstOrDefault(kvp => 
                        string.Equals(kvp.Value, command.CollectionName, StringComparison.OrdinalIgnoreCase));
                    
                    if (targetCollection.Key == Guid.Empty)
                    {
                        return false;
                    }
                    
                    // Apply collection to target object (using collection GUID)
                    var (ec, pair) = penumbraSetCollectionForObjectIpc?.InvokeFunc((int)objectIndex, targetCollection.Key, true, true) ?? (-1, null);
                    
                    if (ec == 0 || ec == 1) // Success or NothingChanged
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                catch (Dalamud.Plugin.Ipc.Exceptions.IpcNotReadyError)
                {
                    if (attempt < maxRetries)
                    {
                        int delayMs = baseDelayMs * attempt;
                        await Task.Delay(delayMs);
                        continue;
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    Log.Error($"Error executing Penumbra command: {ex}");
                    return false;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Execute Glamourer design command with retry logic
        /// </summary>
        private async Task<bool> ExecuteGlamourerCommand(GlamourerCommand command, uint objectIndex)
        {
            const int maxRetries = 5;
            const int baseDelayMs = 200;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Check if Glamourer IPC subscriber is available
                    if (glamourerGetDesignsIpc == null)
                    {
                        return false;
                    }

                    // Get available designs
                    var designs = glamourerGetDesignsIpc.InvokeFunc();
                    if (designs == null)
                    {
                        return false;
                    }
                    
                    // Find design by name
                    var targetDesign = designs.FirstOrDefault(kvp => 
                        string.Equals(kvp.Value, command.DesignName, StringComparison.OrdinalIgnoreCase));
                    
                    if (targetDesign.Key == Guid.Empty)
                    {
                        return false;
                    }
                    
                    // Apply design via IPC with correct parameters and default flags
                    // DesignDefault = Once | Equipment | Customization = 0x01 | 0x02 | 0x04 = 0x07
                    const ulong designDefaultFlags = 0x07uL; // ApplyFlag.Once | ApplyFlag.Equipment | ApplyFlag.Customization
                    var result = glamourerApplyDesignIpc!.InvokeFunc(targetDesign.Key, (int)objectIndex, 0u, designDefaultFlags);
                    
                    return result == 0; // 0 = Success
                }
                catch (Dalamud.Plugin.Ipc.Exceptions.IpcNotReadyError)
                {
                    if (attempt < maxRetries)
                    {
                        int delayMs = baseDelayMs * attempt;
                        await Task.Delay(delayMs);
                        continue;
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    Log.Error($"Error executing Glamourer command: {ex}");
                    return false;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Execute CustomizePlus profile command
        /// </summary>
        private async Task<bool> ExecuteCustomizePlusCommand(CustomizePlusCommand command, uint objectIndex)
        {
            try
            {
                
                // Get available profiles
                var profiles = customizePlusGetProfileListIpc?.InvokeFunc();
                if (profiles == null)
                {
                    return false;
                }
                
                
                // Find profile by name
                var targetProfile = profiles.FirstOrDefault(profile => 
                    string.Equals(profile.Item2, command.ProfileName, StringComparison.OrdinalIgnoreCase));
                
                if (targetProfile.Item1 == Guid.Empty)
                {
                    return false;
                }
                
                
                // Get the actual profile JSON data by Guid
                var (errorCode, profileJson) = customizePlusGetByUniqueIdIpc?.InvokeFunc(targetProfile.Item1) ?? (1, null);
                if (errorCode != 0 || string.IsNullOrEmpty(profileJson))
                {
                    return false;
                }
                
                
                // Apply profile to target object using JSON data
                var result = customizePlusSetTempProfileIpc?.InvokeFunc((ushort)objectIndex, profileJson);
                
                
                // Check if the result indicates success
                if (result.HasValue && result.Value.Item1 == 0)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error executing CustomizePlus command: {ex}");
                return false;
            }
        }
        
        // Command classes for parsed macro commands
        private class PenumbraCommand
        {
            public string CollectionName { get; set; } = "";
        }
        
        private class GlamourerCommand
        {
            public string DesignName { get; set; } = "";
        }
        
        private class CustomizePlusCommand
        {
            public string ProfileName { get; set; } = "";
        }

        private async Task<string> SaveClipboardImageForDesign(Guid designId)
        {
            try
            {
                string imagePath = "";
                
                // Clipboard operations need to be on STA thread
                var thread = new Thread(() =>
                {
                    try
                    {
                        if (!System.Windows.Forms.Clipboard.ContainsImage())
                            return;

                        var image = System.Windows.Forms.Clipboard.GetImage();
                        if (image == null)
                            return;

                        // Create designs directory if it doesn't exist
                        var designsDir = Path.Combine(PluginInterface.ConfigDirectory.FullName, "Designs");
                        Directory.CreateDirectory(designsDir);

                        // Generate filename with timestamp
                        var fileName = $"design_{designId:N}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                        imagePath = Path.Combine(designsDir, fileName);

                        // Save the image as PNG
                        image.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
                        Log.Information($"Saved clipboard image to: {imagePath}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Failed to save clipboard image: {ex.Message}");
                    }
                });
                
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                return imagePath;
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to process clipboard image: {ex.Message}");
                return "";
            }
        }

    }
}

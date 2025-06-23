using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using System.Windows.Forms;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using Dalamud.Interface;
using System.Runtime.CompilerServices;
using Dalamud.Plugin.Services;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace CharacterSelectPlugin.Windows
{
    public class MainWindow : Window, IDisposable
    {
        private Plugin plugin;
        private int selectedCharacterIndex = -1;
        private string editedCharacterName = "";
        private string editedCharacterMacros = "";
        private string? editedCharacterImagePath = null;
        private List<CharacterDesign> editedCharacterDesigns = new();
        private bool isEditCharacterWindowOpen = false;
        private int activeDesignCharacterIndex = -1;
        private bool isDesignPanelOpen = false;
        private string? pendingImagePath = null; // Temporary storage for the selected image path
        private Vector3 editedCharacterColor = new Vector3(1.0f, 1.0f, 1.0f); // Default to white
        private string editedCharacterPenumbra = "";
        private string editedCharacterGlamourer = "";
        private string editedCharacterCustomize = "";
        private bool isAdvancedModeCharacter = false; // Separate Advanced Mode for Characters
        private bool isAdvancedModeDesign = false;    // Separate Advanced Mode for Designs
        private string advancedCharacterMacroText = ""; // Macro text for Character Advanced Mode
        private string advancedDesignMacroText = "";    // Macro text for Design Advanced Mode
        private bool isEditDesignWindowOpen = false;
        private string editedDesignName = "";
        private string editedDesignMacro = "";
        private string editedGlamourerDesign = "";
        private HashSet<string> knownHonorifics = new HashSet<string>();
        private string originalDesignName = ""; // Stores the original name before editing
        private bool isAdvancedModeWindowOpen = false; // Tracks if Advanced Mode window is open
                                                       // Honorific Fields
        private string tempHonorificTitle = "";
        private string tempHonorificPrefix = "Prefix"; // Default to Prefix
        private string tempHonorificSuffix = "Suffix"; // Default to Suffix
        private Vector3 tempHonorificColor = new Vector3(1.0f, 1.0f, 1.0f); // Default to White
        private Vector3 tempHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f); // Default to White

        // For Editing Characters
        private string editedCharacterHonorificTitle = "";
        private string editedCharacterHonorificPrefix = "Prefix";
        private string editedCharacterHonorificSuffix = "Suffix";
        private Vector3 editedCharacterHonorificColor = new Vector3(1.0f, 1.0f, 1.0f);
        private Vector3 editedCharacterHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f);
        private string editedCharacterAutomation = "";
        private string tempCharacterAutomation = "";  // Temp variable for automation
        private CharacterDesign? draggedDesign = null;
        private Dictionary<Guid, string> folderRenameBuffers = new();
        private bool isNewDesign = false;
        private bool isSecretCharacter;
        // Set to true when Ctrl+Shift-click “Add Character”
        private bool isSecretMode = false;
        // Set to true when Ctrl+Shift-click “Add Design”
        private bool isSecretDesignMode = false;





        //MOODLES
        public string MoodlePreset { get; set; } = "";
        // Temporary storage for Moodle preset input in Add/Edit Character window
        private string tempMoodlePreset = "";

        // Stores the selected Moodle preset for an edited character
        private string editedCharacterMoodlePreset = "";

        private string editedAutomation = "";
        private string editedCustomizeProfile = "";
        private bool showSearchBar = false;
        private string searchQuery = "";

        // Shared Designs
        private bool isImportWindowOpen = false;
        private Character? targetForDesignImport = null;

        // Reordering
        private bool isReorderWindowOpen = false;
        private List<Character> reorderBuffer = new();
        // Tags
        private string selectedTag = "All";
        private string editedCharacterTag = "";
        private bool showTagFilter = false;


        // Add Sorting Function
        public enum SortType { Manual, Favorites, Alphabetical, Recent, Oldest }
        private SortType currentSort;

        private enum DesignSortType { Favorites, Alphabetical, Recent, Oldest, Manual }
        private DesignSortType currentDesignSort = DesignSortType.Alphabetical;
        private bool isDesignSortWindowOpen = false;
        private Character? sortTargetCharacter = null;
        private List<DesignFolder> workingFolders = new();
        private Dictionary<Guid, string> workingRenameBuffers = new();
        private string newFolderNameInput = "";
        private bool hasLoadedWorkingFolders = false;
        private bool isCreatingFolder = false;
        private string newFolderName = "";
        private Guid renameFolderId;
        private string renameFolderBuf = "";
        private Guid deleteFolderId;
        private bool isRenameFolderPopupOpen = false;
        private bool isDeleteFolderPopupOpen = false;
        private bool isRenamingFolder = false;
        private DesignFolder? draggedFolder = null;
        private double rowPressStart;
        private CharacterDesign? rowPressDesign;
        private const double DragThreshold = 0.5; // seconds to hold before drag kicks in

        public void SortCharacters()
        {
            if (currentSort == SortType.Favorites)
            {
                plugin.Characters.Sort((a, b) =>
                {
                    int favCompare = b.IsFavorite.CompareTo(a.IsFavorite); // ⭐ Favourites first
                    if (favCompare != 0) return favCompare;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); // Alphabetical within favourites
                });
            }
            if (currentSort == SortType.Manual)
            {
                plugin.Characters.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            }
            else if (currentSort == SortType.Alphabetical)
            {
                plugin.Characters.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)); // Alphabetical
            }
            else if (currentSort == SortType.Recent)
            {
                plugin.Characters.Sort((a, b) => b.DateAdded.CompareTo(a.DateAdded)); // Most Recent First
            }
            else if (currentSort == SortType.Oldest)
            {
                plugin.Characters.Sort((a, b) => a.DateAdded.CompareTo(b.DateAdded)); // Oldest First
            }
        }

        private void SortDesigns(Character character)
        {
            // If the user is in Manual mode, leave the list alone.
            if (currentDesignSort == DesignSortType.Manual)
                return;
            if (currentDesignSort == DesignSortType.Favorites)
            {
                character.Designs.Sort((a, b) =>
                {
                    int favCompare = b.IsFavorite.CompareTo(a.IsFavorite); // Favourites first
                    if (favCompare != 0) return favCompare;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            }
            else if (currentDesignSort == DesignSortType.Alphabetical)
            {
                character.Designs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            else if (currentDesignSort == DesignSortType.Recent)
            {
                character.Designs.Sort((a, b) => b.DateAdded.CompareTo(a.DateAdded));
            }
            else if (currentDesignSort == DesignSortType.Oldest)
            {
                character.Designs.Sort((a, b) => a.DateAdded.CompareTo(b.DateAdded));
            }
        }

        public MainWindow(Plugin plugin)
    : base("Character Select+", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(850, 700),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };

            this.plugin = plugin;

            // Load saved sorting preference
            currentSort = (SortType)plugin.Configuration.CurrentSortIndex;
            SortCharacters(); // Apply sorting on startup
                              // Gather all existing honorifics at startup

        }


        public void Dispose() { }

        public override void Draw()
        {

            // Save original scale
            ImGui.PushFont(UiBuilder.DefaultFont);
            ImGui.SetWindowFontScale(plugin.Configuration.UIScaleMultiplier);
            ImGui.Text("Choose your character");
            ImGui.Separator();

            if (!plugin.IsAddCharacterWindowOpen && !isEditCharacterWindowOpen)
            {
                if (ImGui.Button("Add Character"))
                {
                    // Detect Ctrl+Shift for “secret” mode
                    var io = ImGui.GetIO();
                    isSecretMode = io.KeyCtrl && io.KeyShift;

                    // Preserve any designs the user has already added
                    var tempSavedDesigns = new List<CharacterDesign>(plugin.NewCharacterDesigns);
                    ResetCharacterFields();
                    plugin.NewCharacterDesigns = tempSavedDesigns;

                    // Preload the appropriate macro
                    plugin.NewCharacterMacros = isSecretMode
                        ? GenerateSecretMacro()
                        : GenerateMacro();

                    // Open the Add Character window
                    plugin.OpenAddCharacterWindow();
                    isEditCharacterWindowOpen = false;
                    isDesignPanelOpen = false;
                    isAdvancedModeCharacter = false;
                }

                // Tag Toggle + Dropdown (like Search)
                float tagDropdownWidth = 200f;
                float tagIconOffset = 70f;
                float tagDropdownOffset = tagDropdownWidth + tagIconOffset + 10;

                ImGui.SameLine(ImGui.GetWindowWidth() - tagIconOffset);
                ImGui.PushFont(UiBuilder.IconFont);
                if (ImGui.Button("\uf0b0")) // filter icon
                {
                    showTagFilter = !showTagFilter;
                }
                ImGui.PopFont();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Filter by Tags.");
                    ImGui.EndTooltip();
                }

                // Tag Filter Dropdown (only shows if toggled)
                if (showTagFilter)
                {
                    ImGui.SameLine(ImGui.GetWindowWidth() - tagDropdownOffset);
                    ImGui.SetNextItemWidth(tagDropdownWidth);
                    if (ImGui.BeginCombo("##TagFilter", selectedTag))
                    {
                        var allTags = plugin.Characters
                            .SelectMany(c => c.Tags ?? new List<string>())
                            .Distinct()
                            .OrderBy(f => f)
                            .Prepend("All")
                            .ToList();

                        foreach (var tag in allTags)
                        {
                            bool isSelected = tag == selectedTag;
                            if (ImGui.Selectable(tag, isSelected))
                                selectedTag = tag;

                            if (isSelected)
                                ImGui.SetItemDefaultFocus();
                        }

                        ImGui.EndCombo();
                    }
                }
            }

            if (plugin.IsAddCharacterWindowOpen || isEditCharacterWindowOpen)
            {
                float baseLines = 26f;
                if (isAdvancedModeWindowOpen)
                    baseLines += 6f; // give extra space if Advanced Mode is showing

                float maxContentHeight = ImGui.GetTextLineHeightWithSpacing() * baseLines;
                float availableHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() * 2.5f;

                float scrollHeight = Math.Min(maxContentHeight, availableHeight);

                ImGui.BeginChild("CharacterFormScrollable", new Vector2(0, scrollHeight), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);
                DrawCharacterForm();
                ImGui.EndChild();
            }


            // Search Button (toggle)
            ImGui.SameLine(ImGui.GetWindowWidth() - 35);
            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button("\uf002")) // FontAwesome "search" icon
            {
                showSearchBar = !showSearchBar;
                if (!showSearchBar) searchQuery = ""; // Clear when closed
            }
            ImGui.PopFont();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                ImGui.BeginTooltip();
                ImGui.Text("Search for a Character.");
                ImGui.EndTooltip();
            }

            // Search Input Field
            if (showSearchBar)
            {
                ImGui.SameLine(ImGui.GetWindowWidth() - 250); // Adjust position
                ImGui.SetNextItemWidth(210f); // Width of the input box
                ImGui.InputTextWithHint("##SearchCharacters", "Search characters...", ref searchQuery, 100);
            }

            ImGui.BeginChild("CharacterGrid", new Vector2(isDesignPanelOpen ? -250 : 0, -30), true);
            DrawCharacterGrid();
            ImGui.EndChild(); // Close Character Grid Properly

            if (isDesignPanelOpen)
            {
                ImGui.SameLine();
                float characterGridHeight = ImGui.GetItemRectSize().Y; // Get height of the Character Grid
                ImGui.SetNextWindowSizeConstraints(new Vector2(250, characterGridHeight), new Vector2(250, characterGridHeight));
                ImGui.BeginChild("DesignPanel", new Vector2(250, characterGridHeight), true);
                DrawDesignPanel();
                ImGui.EndChild();
            }

            // Ensure proper bottom-left alignment
            ImGui.SetCursorPos(new Vector2(10, ImGui.GetWindowHeight() - 30));

            // Settings Button
            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button("\uf013")) // Gear icon (Settings)
            {
                plugin.IsSettingsOpen = !plugin.IsSettingsOpen;
            }
            ImGui.PopFont();
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
                isReorderWindowOpen = true;
                reorderBuffer = plugin.Characters.ToList();
            }

            ImGui.SameLine();

            // Quick Switch Button
            if (ImGui.Button("Quick Switch"))
            {
                plugin.QuickSwitchWindow.IsOpen = !plugin.QuickSwitchWindow.IsOpen; // Toggle Quick Switch Window
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                ImGui.BeginTooltip();
                ImGui.Text("Opens a more compact UI to swap between Characters & Designs.");
                ImGui.EndTooltip();
            }

            if (plugin.IsSettingsOpen)
            {
                ImGui.SetNextWindowSize(new Vector2(300, 180), ImGuiCond.FirstUseEver); // Adjusted for new setting

                bool isSettingsOpen = plugin.IsSettingsOpen;
                if (ImGui.Begin("Settings", ref isSettingsOpen, ImGuiWindowFlags.NoCollapse))
                {
                    if (!isSettingsOpen)
                        plugin.IsSettingsOpen = false;

                    ImGui.Text("Settings Panel");
                    ImGui.Separator();

                    // Profile Image Scale
                    float tempScale = plugin.ProfileImageScale;
                    if (ImGui.SliderFloat("Profile Image Scale", ref tempScale, 0.5f, 2.0f, "%.1f"))
                    {
                        plugin.ProfileImageScale = tempScale;
                        plugin.SaveConfiguration();
                    }

                    // Profile Columns
                    int tempColumns = plugin.ProfileColumns;
                    if (ImGui.InputInt("Profiles Per Row", ref tempColumns, 1, 1))
                    {
                        tempColumns = Math.Clamp(tempColumns, 1, 6);
                        plugin.ProfileColumns = tempColumns;
                        plugin.SaveConfiguration();
                    }

                    // Profile Spacing - Match the layout of Profile Image Scale
                    float tempSpacing = plugin.ProfileSpacing;

                    // Slider first
                    ImGui.SetNextItemWidth(150);
                    if (ImGui.SliderFloat("##ProfileSpacing", ref tempSpacing, 0.0f, 50.0f, "%.1f"))
                    {
                        plugin.ProfileSpacing = tempSpacing;
                        plugin.SaveConfiguration();
                    }

                    // Align label to the right of the slider
                    ImGui.SameLine();
                    ImGui.Text("Profile Spacing");

                    // Automation Opt-In Section
                    ImGui.Separator();
                    ImGui.Text("Glamourer Automations");

                    // Tooltip Icon (always next to label)
                    ImGui.SameLine();
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.TextUnformatted("\uf05a");
                    ImGui.PopFont();

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.PushTextWrapPos(300);
                        ImGui.TextUnformatted("Enable support for Glamourer Automations in Characters & Designs.");
                        ImGui.Separator();
                        ImGui.TextUnformatted("When enabled, you’ll be able to assign an Automation to each character & design.");
                        ImGui.TextUnformatted("⚠️ Characters & Designs without automations will require a fallback Automation in Glamourer named:");
                        ImGui.TextUnformatted("\"None\"");
                        ImGui.TextUnformatted("You also must enter your in-game character name in Glamourer next to \"Any World\" and Set to Character.");
                        ImGui.PopTextWrapPos();
                        ImGui.EndTooltip();
                    }

                    bool automationToggle = plugin.Configuration.EnableAutomations;
                    if (ImGui.Checkbox("Enable Automations", ref automationToggle))
                    {
                        plugin.Configuration.EnableAutomations = automationToggle;

                        bool changed = false;

                        // Character-level Automation Handling
                        foreach (var character in plugin.Characters)
                        {
                            if (!automationToggle)
                            {
                                character.CharacterAutomation = string.Empty;
                            }
                            else if (string.IsNullOrWhiteSpace(character.CharacterAutomation))
                            {
                                character.CharacterAutomation = "None";
                            }
                        }

                        if (!automationToggle)
                        {
                            // Remove automation lines from all macros

                            // From Designs
                            foreach (var character in plugin.Characters)
                            {
                                foreach (var design in character.Designs)
                                {
                                    string macro = design.IsAdvancedMode ? design.AdvancedMacro : design.Macro;
                                    if (string.IsNullOrWhiteSpace(macro))
                                        continue;

                                    var cleaned = string.Join("\n", macro
                                        .Split('\n')
                                        .Where(line => !line.TrimStart().StartsWith("/glamour automation enable", StringComparison.OrdinalIgnoreCase))
                                        .Select(line => line.TrimEnd()));

                                    if (design.IsAdvancedMode && cleaned != design.AdvancedMacro)
                                    {
                                        design.AdvancedMacro = cleaned;
                                        changed = true;
                                    }
                                    else if (!design.IsAdvancedMode && cleaned != design.Macro)
                                    {
                                        design.Macro = cleaned;
                                        changed = true;
                                    }
                                }
                            }

                            // From Characters
                            foreach (var character in plugin.Characters)
                            {
                                if (string.IsNullOrWhiteSpace(character.Macros))
                                    continue;

                                var cleaned = string.Join("\n", character.Macros
                                    .Split('\n')
                                    .Where(line => !line.TrimStart().StartsWith("/glamour automation enable", StringComparison.OrdinalIgnoreCase))
                                    .Select(line => line.TrimEnd()));

                                if (cleaned != character.Macros)
                                {
                                    character.Macros = cleaned;
                                    changed = true;
                                }
                            }
                        }
                        else
                        {
                            // Re-add automation lines using SanitizeDesignMacro and SanitizeMacro

                            // Designs
                            foreach (var character in plugin.Characters)
                            {
                                foreach (var design in character.Designs)
                                {
                                    string macro = design.IsAdvancedMode ? design.AdvancedMacro : design.Macro;
                                    if (string.IsNullOrWhiteSpace(macro))
                                        continue;

                                    string updated = Plugin.SanitizeDesignMacro(macro, design, character, true);

                                    if (design.IsAdvancedMode && updated != design.AdvancedMacro)
                                    {
                                        design.AdvancedMacro = updated;
                                        changed = true;
                                    }
                                    else if (!design.IsAdvancedMode && updated != design.Macro)
                                    {
                                        design.Macro = updated;
                                        changed = true;
                                    }
                                }
                            }

                            // Characters
                            foreach (var character in plugin.Characters)
                            {
                                string updated = Plugin.SanitizeMacro(character.Macros, character);
                                if (updated != character.Macros)
                                {
                                    character.Macros = updated;
                                    changed = true;
                                }
                            }
                        }

                        // Save once at end if anything changed
                        if (changed)
                            plugin.SaveConfiguration();
                    }
                    bool enableCompactQuickSwitch = plugin.Configuration.QuickSwitchCompact;
                    if (ImGui.Checkbox("Compact Quick Switch Bar", ref enableCompactQuickSwitch))
                    {
                        plugin.Configuration.QuickSwitchCompact = enableCompactQuickSwitch;
                        plugin.Configuration.Save();
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted("When enabled, the Quick Switch window will hide its title bar and frame, showing only the dropdowns and apply button.");
                        ImGui.EndTooltip();
                    }
                    bool enableAutoload = plugin.Configuration.EnableLastUsedCharacterAutoload;
                    if (ImGui.Checkbox("Auto-Apply Last Used Character on Login", ref enableAutoload))
                    {
                        plugin.Configuration.EnableLastUsedCharacterAutoload = enableAutoload;
                        plugin.Configuration.Save();
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted("Automatically applies the last character you used when logging into the game.");
                        ImGui.EndTooltip();
                    }
                    bool applyIdle = plugin.Configuration.ApplyIdleOnLogin;
                    if (ImGui.Checkbox("Apply idle pose on login", ref applyIdle))
                    {
                        plugin.Configuration.ApplyIdleOnLogin = applyIdle;
                        plugin.Configuration.Save();
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted("Automatically applies your idle pose after logging in. Disable if you’re seeing pose bugs.");
                        ImGui.EndTooltip();
                    }
                    bool reapplyDesign = plugin.Configuration.ReapplyDesignOnJobChange;
                    if (ImGui.Checkbox("Reapply last design on job change", ref reapplyDesign))
                    {
                        plugin.Configuration.ReapplyDesignOnJobChange = reapplyDesign;
                        plugin.Configuration.Save();
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted("If checked, Character Select+ will reapply the last used design when you switch jobs.");
                        ImGui.EndTooltip();
                    }
                    // Legacy Settings
                    // bool delay = plugin.Configuration.EnableLoginDelay;
                    // if (ImGui.Checkbox("Enable delay on login", ref delay))
                    // {
                    //     plugin.Configuration.EnableLoginDelay = delay;
                    //     plugin.SaveConfiguration();
                    // }
                    // if (ImGui.IsItemHovered())
                    // {
                    //     ImGui.BeginTooltip();
                    //     ImGui.TextUnformatted("If enabled, Character Select+ will wait after logging in before auto-loading your character profile and applying poses.");
                    //     ImGui.EndTooltip();
                    // }

                    // bool safeMode = plugin.Configuration.EnableSafeMode;
                    // if (ImGui.Checkbox("Enable Safe Mode (Disables All Auto Features)", ref safeMode))
                    // {
                    //     plugin.Configuration.EnableSafeMode = safeMode;
                    //     plugin.Configuration.Save();
                    // }
                    // if (ImGui.IsItemHovered())
                    // {
                    //     ImGui.BeginTooltip();
                    //     ImGui.TextUnformatted("Temporarily disables all automatic pose, macro, and profile application logic. Useful for debugging crashes.");
                    //     ImGui.EndTooltip();
                    // }

                    // bool poseAutoSave = plugin.Configuration.EnablePoseAutoSave;
                    // if (ImGui.Checkbox("Pose auto-save", ref poseAutoSave))
                    // {
                    //     plugin.Configuration.EnablePoseAutoSave = poseAutoSave;
                    //     plugin.SaveConfiguration();
                    // }
                    // if (ImGui.IsItemHovered())
                    // {
                    //     ImGui.BeginTooltip();
                    //     ImGui.Text("Tracks pose changes (idle/sit/ground/doze) and auto-saves them.");
                    //     ImGui.Text("If you experience crashes when switching characters, try turning this off.");
                    //     ImGui.EndTooltip();
                    // }
                    float scaleSetting = plugin.Configuration.UIScaleMultiplier;
                    if (ImGui.SliderFloat("UI Scale", ref scaleSetting, 0.5f, 2.0f, "%.2fx"))
                    {
                        plugin.Configuration.UIScaleMultiplier = scaleSetting;
                        plugin.SaveConfiguration();
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text("Scales the entire Character Select+ UI manually.");
                        ImGui.Text("Useful for high-DPI monitors (2K / 3K / 4K).");
                        ImGui.EndTooltip();
                    }

                    // Position "Sort By" Dropdown in the Bottom-Right
                    ImGui.Separator();
                    ImGui.Text("Sort By:");
                    ImGui.SameLine();

                    if (ImGui.BeginCombo("##SortDropdown", currentSort.ToString()))
                    {
                        if (ImGui.Selectable("Favorites", currentSort == SortType.Favorites))
                        {
                            currentSort = SortType.Favorites;
                            plugin.Configuration.CurrentSortIndex = (int)currentSort;
                            plugin.Configuration.Save();
                            SortCharacters();
                        }
                        if (ImGui.Selectable("Alphabetical", currentSort == SortType.Alphabetical))
                        {
                            currentSort = SortType.Alphabetical;
                            plugin.Configuration.CurrentSortIndex = (int)currentSort;
                            plugin.Configuration.Save();
                            SortCharacters();
                        }
                        if (ImGui.Selectable("Most Recent", currentSort == SortType.Recent))
                        {
                            currentSort = SortType.Recent;
                            plugin.Configuration.CurrentSortIndex = (int)currentSort;
                            plugin.Configuration.Save();
                            SortCharacters();
                        }
                        if (ImGui.Selectable("Oldest", currentSort == SortType.Oldest))
                        {
                            currentSort = SortType.Oldest;
                            plugin.Configuration.CurrentSortIndex = (int)currentSort;
                            plugin.Configuration.Save();
                            SortCharacters();
                        }

                        ImGui.EndCombo();
                    }
                    if (isAdvancedModeWindowOpen)
                    {
                        ImGui.SetNextWindowSize(new Vector2(500, 200), ImGuiCond.FirstUseEver);
                        if (ImGui.Begin("Advanced Macro Editor", ref isAdvancedModeWindowOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize))
                        {
                            ImGui.Text("Edit Design Macro Manually:");
                            ImGui.InputTextMultiline("##AdvancedDesignMacro", ref advancedDesignMacroText, 2000, new Vector2(-1, -1), ImGuiInputTextFlags.AllowTabInput);

                            // Auto-save on typing
                            if (isAdvancedModeDesign)
                            {
                                editedDesignMacro = advancedDesignMacroText;
                            }
                        }
                        ImGui.End();
                    }

                    ImGui.End();
                }
            }
            // Get position for bottom-right corner after all layout is done
            Vector2 windowPos = ImGui.GetWindowPos();
            Vector2 windowSize = ImGui.GetWindowSize();
            float buttonWidth = 105;
            float buttonHeight = 25;
            float padding = 05;

            // Set position to bottom-right, accounting for padding
            // Set cursor and button area
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
            Vector2 textPos = ImGui.GetItemRectMin() + new Vector2(6, 4); // Padding inside button
            ImGui.GetWindowDrawList().AddText(UiBuilder.IconFont, ImGui.GetFontSize(), textPos, ImGui.GetColorU32(ImGuiCol.Text), "\uf004");
            ImGui.GetWindowDrawList().AddText(textPos + new Vector2(22, 0), ImGui.GetColorU32(ImGuiCol.Text), "Support Dev");

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Enjoy Character Select+? Consider supporting development!");
            }
            DrawReorderWindow();
            // Draw Import Designs Window if Open
            if (isImportWindowOpen && targetForDesignImport != null)
            {
                DrawImportDesignWindow();
            }
            ImGui.SetWindowFontScale(1f);
            ImGui.PopFont();
        }



        // Resets input fields for a new character
        private void ResetCharacterFields()
        {
            plugin.NewCharacterName = "";
            plugin.NewCharacterColor = new Vector3(1.0f, 1.0f, 1.0f); // Reset to white
            plugin.NewPenumbraCollection = "";
            plugin.NewGlamourerDesign = "";
            plugin.NewCharacterAutomation = "";
            plugin.NewCustomizeProfile = "";
            plugin.NewCharacterImagePath = null;
            plugin.NewCharacterDesigns.Clear();
            plugin.NewCharacterHonorificTitle = "";
            plugin.NewCharacterHonorificPrefix = "Prefix";
            plugin.NewCharacterHonorificSuffix = "Suffix";
            plugin.NewCharacterHonorificColor = new Vector3(1.0f, 1.0f, 1.0f); // Default White
            plugin.NewCharacterHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f);  // Default White
            plugin.NewCharacterMoodlePreset = ""; // RESET Moodle Preset
            plugin.NewCharacterIdlePoseIndex = 7; // 7 = None

            tempHonorificTitle = "";
            tempHonorificPrefix = "Prefix";
            tempHonorificSuffix = "Suffix";
            tempHonorificColor = new Vector3(1.0f, 1.0f, 1.0f);
            tempHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f);
            tempMoodlePreset = ""; // RESET Temporary Moodle Preset


            // Preserve Advanced Mode Macro when Resetting Fields
            if (!isAdvancedModeCharacter)
            {
                plugin.NewCharacterMacros = GenerateMacro(); // Only reset macro in Normal Mode
            }
            // Do NOT touch plugin.NewCharacterMacros if Advanced Mode is active

        }


        private void DrawCharacterForm()
        {
            float scale = plugin.Configuration.UIScaleMultiplier;
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4 * scale, 2 * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4 * scale, 4 * scale));

            string tempName = isEditCharacterWindowOpen ? editedCharacterName : plugin.NewCharacterName;
            string tempMacros = isEditCharacterWindowOpen ? editedCharacterMacros : plugin.NewCharacterMacros;
            string? imagePath = isEditCharacterWindowOpen ? editedCharacterImagePath : plugin.NewCharacterImagePath;
            string tempPenumbra = isEditCharacterWindowOpen ? editedCharacterPenumbra : plugin.NewPenumbraCollection;
            string tempGlamourer = isEditCharacterWindowOpen ? editedCharacterGlamourer : plugin.NewGlamourerDesign;
            string tempCustomize = isEditCharacterWindowOpen ? editedCharacterCustomize : plugin.NewCustomizeProfile;
            Vector3 tempColor = isEditCharacterWindowOpen ? editedCharacterColor : plugin.NewCharacterColor;
            string tempTag = isEditCharacterWindowOpen ? editedCharacterTag : plugin.NewCharacterTag;



            float labelWidth = 130 * scale; // Keep labels aligned
            float inputWidth = 250 * scale; // Longer input bars
            float inputOffset = 10 * scale; // Moves input fields slightly right

            // Character Name
            ImGui.SetCursorPosX(10);
            ImGui.Text("Character Name*");
            ImGui.SameLine(labelWidth);
            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.InputText("##CharacterName", ref tempName, 50);
            if (isEditCharacterWindowOpen) editedCharacterName = tempName;
            else plugin.NewCharacterName = tempName;

            // Tooltip Icon
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();
            if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Enter your OC's name or nickname for profile here."); }

            ImGui.Separator();

            // Tags Input (comma-separated)
            ImGui.SetCursorPosX(10);
            ImGui.Text("Character Tags");
            ImGui.SameLine(labelWidth);
            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.InputTextWithHint("##Tags", "e.g. Casual, Battle, Beach", ref tempTag, 100);

            // ⬅ Save depending on Add/Edit mode
            if (isEditCharacterWindowOpen)
                editedCharacterTag = tempTag;
            else
                plugin.NewCharacterTag = tempTag;

            // Tooltip Icon
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted("\uf05a"); // info icon
            ImGui.PopFont();

            // Tooltip Text
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted("You can assign multiple tags by separating them with commas.");
                ImGui.TextUnformatted("Examples: Casual, Favorites, Seasonal");
                ImGui.EndTooltip();
            }

            ImGui.Separator();

            // Nameplate Color
            ImGui.SetCursorPosX(10);
            ImGui.Text("Nameplate Color");
            ImGui.SameLine(labelWidth);
            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.ColorEdit3("##NameplateColor", ref tempColor);
            if (isEditCharacterWindowOpen) editedCharacterColor = tempColor;
            else plugin.NewCharacterColor = tempColor;

            // Tooltip Icon
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();
            if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Affects your character's nameplate under their profile picture in Character Select+."); }

            ImGui.Separator();

            // Penumbra Collection
            ImGui.SetCursorPosX(10);
            ImGui.Text("Penumbra Collection*");
            ImGui.SameLine(labelWidth);
            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.InputText("##PenumbraCollection", ref tempPenumbra, 50);
            // Preserve Advanced Mode Edits While Allowing Normal Mode Updates
            if (isEditCharacterWindowOpen)
            {
                // ─── EDIT CHARACTER ───
                if (editedCharacterPenumbra != tempPenumbra)
                {
                    editedCharacterPenumbra = tempPenumbra;

                    if (isAdvancedModeCharacter)
                    {
                        var col = editedCharacterPenumbra;

                        // 1) collection line
                        advancedCharacterMacroText = PatchMacroLine(
                            advancedCharacterMacroText,
                            "/penumbra collection",
                            $"/penumbra collection individual | {col} | self"
                        );

                        // 2) all bulk‐disable lines
                        advancedCharacterMacroText = UpdateCollectionInLines(
                            advancedCharacterMacroText,
                            "/penumbra bulktag disable",
                            col
                        );

                        // 3) bulk‐enable line (if already present)
                        advancedCharacterMacroText = UpdateCollectionInLines(
                            advancedCharacterMacroText,
                            "/penumbra bulktag enable",
                            col
                        );
                    }
                    else
                    {
                        // Normal Edit: full regen
                        editedCharacterMacros = GenerateMacro();
                    }
                }
            }
            else
            {
                // ─── ADD CHARACTER ───
                if (plugin.NewPenumbraCollection != tempPenumbra)
                {
                    plugin.NewPenumbraCollection = tempPenumbra;
                    var col = plugin.NewPenumbraCollection;

                    if (isAdvancedModeCharacter)
                    {
                        advancedCharacterMacroText = PatchMacroLine(
                            advancedCharacterMacroText,
                            "/penumbra collection",
                            $"/penumbra collection individual | {col} | self"
                        );

                        advancedCharacterMacroText = UpdateCollectionInLines(
                            advancedCharacterMacroText,
                            "/penumbra bulktag disable",
                            col
                        );

                        advancedCharacterMacroText = UpdateCollectionInLines(
                            advancedCharacterMacroText,
                            "/penumbra bulktag enable",
                            col
                        );

                        plugin.NewCharacterMacros = advancedCharacterMacroText;
                    }
                    else
                    {
                        // Normal Add: full regen
                        plugin.NewCharacterMacros = GenerateMacro();
                    }
                }
            }

            // Tooltip Icon
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300);
                ImGui.TextUnformatted("Enter the name of the Penumbra collection to apply to this character.");
                ImGui.TextUnformatted("Must be entered EXACTLY as it is named in Penumbra!");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            ImGui.Separator();


            // Glamourer Design
            ImGui.SetCursorPosX(10);
            ImGui.Text("Glamourer Design*");
            ImGui.SameLine(labelWidth);
            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.InputText("##GlamourerDesign", ref tempGlamourer, 50);
            // ── GLAMOURER DESIGN ──
            if (isEditCharacterWindowOpen)
            {
                if (editedCharacterGlamourer != tempGlamourer)
                {
                    // capture the old design so we target exactly that line
                    var oldGlam = editedCharacterGlamourer;
                    editedCharacterGlamourer = tempGlamourer;

                    if (isAdvancedModeCharacter)
                    {
                        var col = editedCharacterPenumbra;
                        var glam = editedCharacterGlamourer;

                        // 1) patch only the exact "/glamour apply OLD | …" line
                        advancedCharacterMacroText = PatchMacroLine(
                            advancedCharacterMacroText,
                            $"/glamour apply {oldGlam} |",
                            $"/glamour apply {glam} | self"
                        );

                        // 2) update all bulk‐disable lines’ COLLECTION bit
                        advancedCharacterMacroText = UpdateCollectionInLines(
                            advancedCharacterMacroText,
                            "/penumbra bulktag disable",
                            col
                        );

                        // 3) rewrite the one existing enable‐line in place
                        advancedCharacterMacroText = UpdateBulkTagEnableDesignInMacro(
                            advancedCharacterMacroText,
                            col,
                            glam
                        );
                    }
                    else
                    {
                        // Normal Edit: full regen
                        editedCharacterMacros = GenerateMacro();
                    }
                }
            }
            else
            {
                if (plugin.NewGlamourerDesign != tempGlamourer)
                {
                    // capture the old draft design
                    var oldGlam = plugin.NewGlamourerDesign;
                    plugin.NewGlamourerDesign = tempGlamourer;

                    if (isAdvancedModeCharacter)
                    {
                        var col = plugin.NewPenumbraCollection;
                        var glam = plugin.NewGlamourerDesign;

                        advancedCharacterMacroText = PatchMacroLine(
                            advancedCharacterMacroText,
                            $"/glamour apply {oldGlam} |",
                            $"/glamour apply {glam} | self"
                        );

                        advancedCharacterMacroText = UpdateCollectionInLines(
                            advancedCharacterMacroText,
                            "/penumbra bulktag disable",
                            col
                        );

                        advancedCharacterMacroText = UpdateBulkTagEnableDesignInMacro(
                            advancedCharacterMacroText,
                            col,
                            glam
                        );

                        plugin.NewCharacterMacros = advancedCharacterMacroText;
                    }
                    else
                    {
                        // Normal Add: full regen
                        plugin.NewCharacterMacros = GenerateMacro();
                    }
                }
            }


            // Tooltip Icon
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300);
                ImGui.TextUnformatted("Enter the name of the Glamourer design to apply to this character.");
                ImGui.TextUnformatted("Must be entered EXACTLY as it is named in Glamourer!");
                ImGui.TextUnformatted("Note: You can add additional designs later.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            ImGui.Separator();

            // Character Automation (if enabled)
            if (plugin.Configuration.EnableAutomations)
            {
                ImGui.SetCursorPosX(10);
                ImGui.Text("Glam. Automation");
                ImGui.SameLine(labelWidth);
                ImGui.SetCursorPosX(labelWidth + inputOffset);
                ImGui.SetNextItemWidth(inputWidth);

                string tempCharacterAutomation = isEditCharacterWindowOpen ? editedCharacterAutomation : plugin.NewCharacterAutomation;

                if (ImGui.InputText("##Glam.Automation", ref tempCharacterAutomation, 50))
                {
                    if (isEditCharacterWindowOpen
        ? editedCharacterAutomation != tempCharacterAutomation
        : plugin.NewCharacterAutomation != tempCharacterAutomation)
                    {
                        if (isEditCharacterWindowOpen)
                            editedCharacterAutomation = tempCharacterAutomation;
                        else
                            plugin.NewCharacterAutomation = tempCharacterAutomation;

                        if (isAdvancedModeCharacter)
                        {
                            var line = string.IsNullOrWhiteSpace(tempCharacterAutomation)
                                ? "/glamour automation enable None"
                                : $"/glamour automation enable {tempCharacterAutomation}";
                            advancedCharacterMacroText = PatchMacroLine(
                                advancedCharacterMacroText,
                                "/glamour automation enable",
                                line
                            );
                            if (!isEditCharacterWindowOpen)
                                plugin.NewCharacterMacros = advancedCharacterMacroText;
                        }
                        else
                        {
                            if (isEditCharacterWindowOpen)
                                editedCharacterMacros = GenerateMacro();
                            else
                                plugin.NewCharacterMacros = GenerateMacro();
                        }
                    }
                }

                // Tooltip
                ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextUnformatted("\uf05a");
                ImGui.PopFont();
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.PushTextWrapPos(300);
                    ImGui.TextUnformatted("Enter the name of a Glamourer Automation profile to apply when this character is activated.");
                    ImGui.TextUnformatted("Design-level automations override this if both are set.");
                    ImGui.TextUnformatted("Leave blank to default to a fallback profile named 'None'.");
                    ImGui.TextUnformatted("Steps to make it work:");
                    ImGui.TextUnformatted("1. Create an Automation in Glamourer named \"None\"");
                    ImGui.TextUnformatted("2. Enter your in-game character name next to \"Any World\"");
                    ImGui.TextUnformatted("3. Set to Character.");
                    ImGui.PopTextWrapPos();
                    ImGui.EndTooltip();
                }

                ImGui.Separator();
            }

            // Customize+ Profile
            ImGui.SetCursorPosX(10);
            ImGui.Text("Customize+ Profile");
            ImGui.SameLine(labelWidth);
            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.InputText("##CustomizeProfile", ref tempCustomize, 50);
            if (isEditCharacterWindowOpen
    ? editedCharacterCustomize != tempCustomize
    : plugin.NewCustomizeProfile != tempCustomize)
            {
                if (isEditCharacterWindowOpen)
                    editedCharacterCustomize = tempCustomize;
                else
                    plugin.NewCustomizeProfile = tempCustomize;

                if (isAdvancedModeCharacter)
                {
                    // always ensure disable-line is correct
                    advancedCharacterMacroText = PatchMacroLine(
                        advancedCharacterMacroText,
                        "/customize profile disable",
                        "/customize profile disable <me>"
                    );

                    // enable-line if non-empty, otherwise remove it
                    if (!string.IsNullOrWhiteSpace(tempCustomize))
                    {
                        advancedCharacterMacroText = PatchMacroLine(
                            advancedCharacterMacroText,
                            "/customize profile enable",
                            $"/customize profile enable <me>, {tempCustomize}"
                        );
                    }
                    else
                    {
                        // strip any existing enable line
                        advancedCharacterMacroText = string.Join("\n",
                            advancedCharacterMacroText
                                .Split('\n')
                                .Where(l => !l.TrimStart().StartsWith("/customize profile enable"))
                        );
                    }

                    if (!isEditCharacterWindowOpen)
                        plugin.NewCharacterMacros = advancedCharacterMacroText;
                }
                else
                {
                    if (isEditCharacterWindowOpen)
                        editedCharacterMacros = GenerateMacro();
                    else
                        plugin.NewCharacterMacros = GenerateMacro();
                }
            }


            // Tooltip Icon
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300);
                ImGui.TextUnformatted("Enter the name of the Customize+ profile to apply to this character.");
                ImGui.TextUnformatted("Must be entered EXACTLY as it is named in Customize+!");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }



            ImGui.Separator();

            // Honorific Title Section (Proper Alignment)
            ImGui.SetCursorPosX(10);
            ImGui.Text("Honorific Title");
            ImGui.SameLine();

            // Move cursor for input alignment
            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);

            // ── HONORIFIC CONTROLS ──

            // 1) Draw widgets & detect any change
            bool changed = false;

            // Title
            changed |= ImGui.InputText("##HonorificTitle", ref tempHonorificTitle, 50);

            // Placement combo (Prefix / Suffix)
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80 * scale);
            if (ImGui.BeginCombo("##HonorificPlacement", tempHonorificPrefix))
            {
                foreach (var opt in new[] { "Prefix", "Suffix" })
                {
                    if (ImGui.Selectable(opt, tempHonorificPrefix == opt))
                    {
                        tempHonorificPrefix = opt;
                        tempHonorificSuffix = opt;
                        changed = true;
                    }
                    if (tempHonorificPrefix == opt)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            // Color picker
            ImGui.SameLine();
            ImGui.SetNextItemWidth(40 * scale);
            changed |= ImGui.ColorEdit3("##HonorificColor", ref tempHonorificColor, ImGuiColorEditFlags.NoInputs);

            // Glow picker
            ImGui.SameLine();
            ImGui.SetNextItemWidth(40 * scale);
            changed |= ImGui.ColorEdit3("##HonorificGlow", ref tempHonorificGlow, ImGuiColorEditFlags.NoInputs);

            // 2) If anything changed, update model + macro
            if (changed)
            {
                // sync temp → your data store
                if (isEditCharacterWindowOpen)
                {
                    editedCharacterHonorificTitle = tempHonorificTitle;
                    editedCharacterHonorificPrefix = tempHonorificPrefix;
                    editedCharacterHonorificSuffix = tempHonorificSuffix;
                    editedCharacterHonorificColor = tempHonorificColor;
                    editedCharacterHonorificGlow = tempHonorificGlow;
                }
                else
                {
                    plugin.NewCharacterHonorificTitle = tempHonorificTitle;
                    plugin.NewCharacterHonorificPrefix = tempHonorificPrefix;
                    plugin.NewCharacterHonorificSuffix = tempHonorificSuffix;
                    plugin.NewCharacterHonorificColor = tempHonorificColor;
                    plugin.NewCharacterHonorificGlow = tempHonorificGlow;
                }

                if (isAdvancedModeCharacter)
                {
                    // build the new set-line
                    var c = tempHonorificColor;
                    var g = tempHonorificGlow;
                    string colorHex = $"#{(int)(c.X * 255):X2}{(int)(c.Y * 255):X2}{(int)(c.Z * 255):X2}";
                    string glowHex = $"#{(int)(g.X * 255):X2}{(int)(g.Y * 255):X2}{(int)(g.Z * 255):X2}";
                    string setLine = $"/honorific force set {tempHonorificTitle} | {tempHonorificPrefix} | {colorHex} | {glowHex}";

                    // split into lines
                    var lines = advancedCharacterMacroText.Split('\n').ToList();

                    // look for existing "force set"
                    var setIdx = lines.FindIndex(l =>
                        l.TrimStart().StartsWith("/honorific force set", StringComparison.OrdinalIgnoreCase));

                    if (setIdx >= 0)
                    {
                        // replace it
                        lines[setIdx] = setLine;
                    }
                    else
                    {
                        // find "force clear"
                        var clearIdx = lines.FindIndex(l =>
                            l.TrimStart().StartsWith("/honorific force clear", StringComparison.OrdinalIgnoreCase));
                        if (clearIdx >= 0)
                            lines.Insert(clearIdx + 1, setLine);
                        else
                            lines.Add(setLine);
                    }

                    // rejoin
                    advancedCharacterMacroText = string.Join("\n", lines);

                    // if Add‐mode, update the draft as well
                    if (!isEditCharacterWindowOpen)
                        plugin.NewCharacterMacros = advancedCharacterMacroText;
                }
                else
                {
                    // Normal Mode: full live-regen
                    if (isEditCharacterWindowOpen)
                        editedCharacterMacros = GenerateMacro();
                    else
                        plugin.NewCharacterMacros = GenerateMacro();
                }
            }

            ImGui.SameLine();

            // Tooltip for Honorific Title
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300);
                ImGui.TextUnformatted("This will set a forced title when you switch to this character.");
                ImGui.TextUnformatted("The dropdown selects if the title appears above (prefix) or below (suffix) your name in-game.");
                ImGui.TextUnformatted("Use the Honorific plug-in’s 'Clear' button if you need to remove it.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            ImGui.Separator();

            // Moodle Preset Input
            ImGui.SetCursorPosX(10);
            ImGui.Text("Moodle Preset");
            ImGui.SameLine();

            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.InputText("##MoodlePreset", ref tempMoodlePreset, 50);

            // Update stored preset value
            if (isEditCharacterWindowOpen
    ? editedCharacterMoodlePreset != tempMoodlePreset
    : plugin.NewCharacterMoodlePreset != tempMoodlePreset)
            {
                if (isEditCharacterWindowOpen)
                    editedCharacterMoodlePreset = tempMoodlePreset;
                else
                    plugin.NewCharacterMoodlePreset = tempMoodlePreset;

                if (isAdvancedModeCharacter)
                {
                    // remove–all line is static, so no change there
                    // patch only the apply line
                    if (!string.IsNullOrWhiteSpace(tempMoodlePreset))
                    {
                        advancedCharacterMacroText = PatchMacroLine(
                            advancedCharacterMacroText,
                            "/moodle apply self preset",
                            $"/moodle apply self preset \"{tempMoodlePreset}\""
                        );
                    }
                    else
                    {
                        // strip apply line when blank
                        advancedCharacterMacroText = string.Join("\n",
                            advancedCharacterMacroText
                                .Split('\n')
                                .Where(l => !l.TrimStart().StartsWith("/moodle apply self preset"))
                        );
                    }
                    if (!isEditCharacterWindowOpen)
                        plugin.NewCharacterMacros = advancedCharacterMacroText;
                }
                else
                {
                    if (isEditCharacterWindowOpen)
                        editedCharacterMacros = GenerateMacro();
                    else
                        plugin.NewCharacterMacros = GenerateMacro();
                }
            }

            // Tooltip for Moodle Preset
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300);
                ImGui.TextUnformatted("Enter the Moodle preset name exactly as saved in the Moodle plugin.");
                ImGui.TextUnformatted("Example: 'HappyFawn' will apply the preset named 'HappyFawn'.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            ImGui.Separator();

            // Idle Pose Dropdown (None + 0–6)
            ImGui.SetCursorPosX(10);
            ImGui.Text("Idle Pose");
            ImGui.SameLine();

            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);

            // Poses start from index 0
            string[] poseOptions = { "None", "0", "1", "2", "3", "4", "5", "6" };
            // Get the actual stored pose index (can be 0–6 or 7 for None)
            byte storedIndex = isEditCharacterWindowOpen
                ? plugin.Characters[selectedCharacterIndex].IdlePoseIndex
                : plugin.NewCharacterIdlePoseIndex;

            // Convert to dropdown index: "None" (7) → 0, others shift by +1
            int dropdownIndex = storedIndex == 7 ? 0 : storedIndex + 1;

            if (ImGui.BeginCombo("##IdlePose", poseOptions[dropdownIndex]))
            {
                for (int i = 0; i < poseOptions.Length; i++)
                {
                    bool selected = i == dropdownIndex;
                    if (ImGui.Selectable(poseOptions[i], selected))
                    {
                        byte newIndex = (byte)(i == 0 ? 7 : i - 1);

                        if (isEditCharacterWindowOpen
                            ? plugin.Characters[selectedCharacterIndex].IdlePoseIndex != newIndex
                            : plugin.NewCharacterIdlePoseIndex != newIndex)
                        {
                            if (isEditCharacterWindowOpen)
                                plugin.Characters[selectedCharacterIndex].IdlePoseIndex = newIndex;
                            else
                                plugin.NewCharacterIdlePoseIndex = newIndex;

                            if (isAdvancedModeCharacter)
                            {
                                if (newIndex != 7)
                                {
                                    // patch /sidle line
                                    advancedCharacterMacroText = PatchMacroLine(
                                        advancedCharacterMacroText,
                                        "/sidle",
                                        $"/sidle {newIndex}"
                                    );
                                }
                                else
                                {
                                    // remove any existing /sidle
                                    advancedCharacterMacroText = string.Join("\n",
                                        advancedCharacterMacroText
                                            .Split('\n')
                                            .Where(l => !l.TrimStart().StartsWith("/sidle"))
                                    );
                                }
                                if (!isEditCharacterWindowOpen)
                                    plugin.NewCharacterMacros = advancedCharacterMacroText;
                            }
                            else
                            {
                                if (isEditCharacterWindowOpen)
                                    editedCharacterMacros = GenerateMacro();
                                else
                                    plugin.NewCharacterMacros = GenerateMacro();
                            }
                        }
                    }
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }


            // Tooltip Icon
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted("Sets your character's idle pose (0–6).");
                ImGui.TextUnformatted("Choose 'None' if you don’t want Character Select+ to change your idle.");
                ImGui.EndTooltip();
            }

            ImGui.Separator();

            if (isEditCharacterWindowOpen)
                editedCharacterMacros = tempMacros;
            else
// Ensure Advanced Mode changes are actually applied to new characters
if (isAdvancedModeCharacter)
            {
                if (!string.IsNullOrWhiteSpace(advancedCharacterMacroText))
                {
                    plugin.NewCharacterMacros = advancedCharacterMacroText; // Save changes properly
                }
            }
            else
            {
                // honor secret‐mode when opening “Add Character”
                plugin.NewCharacterMacros = isSecretMode
                    ? GenerateSecretMacro()
                    : GenerateMacro();
            }

            if (ImGui.Button("Choose Image"))
            {
                try
                {
                    Thread thread = new Thread(() =>
                    {
                        try
                        {
                            using (OpenFileDialog openFileDialog = new OpenFileDialog())
                            {
                                openFileDialog.Filter = "PNG files (*.png)|*.png";
                                openFileDialog.Title = "Select Character Image";

                                if (openFileDialog.ShowDialog() == DialogResult.OK)
                                {
                                    lock (this) // Prevent race conditions
                                    {
                                        pendingImagePath = openFileDialog.FileName;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.Error($"Error opening file picker: {ex.Message}");
                        }
                    });

                    thread.SetApartmentState(ApartmentState.STA); // Required for OpenFileDialog
                    thread.Start();
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"Critical file picker error: {ex.Message}");
                }
            }

            // Apply the image path safely on the next frame
            if (pendingImagePath != null)
            {
                lock (this) // Prevent potential race conditions
                {
                    if (isEditCharacterWindowOpen)
                        editedCharacterImagePath = pendingImagePath;
                    else
                        plugin.NewCharacterImagePath = pendingImagePath;

                    pendingImagePath = null; // Reset after applying
                }
            }

            // Get Plugin Directory and Default Image Path
            string pluginDirectory = plugin.PluginDirectory;
            string defaultImagePath = Path.Combine(pluginDirectory, "Assets", "Default.png");

            // Assign Default Image if None Selected
            // Ensure we get the correct plugin directory
            string pluginDir = plugin.PluginDirectory;
            string defaultImgPath = Path.Combine(pluginDirectory, "Assets", "Default.png");

            // Determine which image to display
            string finalImagePath = !string.IsNullOrEmpty(imagePath) && File.Exists(imagePath)
            ? imagePath
             : defaultImagePath; // Always use Default.png if no other image is chosen


            if (!string.IsNullOrEmpty(finalImagePath) && File.Exists(finalImagePath))
            {
                var texture = Plugin.TextureProvider.GetFromFile(finalImagePath).GetWrapOrDefault();
                if (texture != null)
                {
                    float originalWidth = texture.Width;
                    float originalHeight = texture.Height;
                    float maxSize = 100f; // Maximum size for preview

                    float aspectRatio = originalWidth / originalHeight;
                    float displayWidth, displayHeight;

                    if (aspectRatio > 1) // Landscape (wider than tall)
                    {
                        displayWidth = maxSize;
                        displayHeight = maxSize / aspectRatio;
                    }
                    else // Portrait or Square (taller or equal)
                    {
                        displayHeight = maxSize;
                        displayWidth = maxSize * aspectRatio;
                    }

                    ImGui.Image(texture.ImGuiHandle, new Vector2(displayWidth, displayHeight));
                }
                else
                {
                    ImGui.Text($"Failed to load image: {Path.GetFileName(finalImagePath)}");
                }
            }
            else
            {
                ImGui.Text("No Image Available");
            }


            List<CharacterDesign> designsToDisplay = isEditCharacterWindowOpen ? editedCharacterDesigns : plugin.NewCharacterDesigns;

            for (int i = 0; i < designsToDisplay.Count; i++)
            {
                var design = designsToDisplay[i];
                string tempDesignName = design.Name;
                string tempDesignMacro = design.Macro;

                ImGui.InputText($"Design Name {i + 1}", ref tempDesignName, 100);
                ImGui.Text("Design Macros:");
                ImGui.BeginChild($"DesignMacroChild_{i}", new Vector2(300, 100), true);
                float minHeight = 110;
                float maxHeight = 300;
                float totalHeight = ImGui.GetContentRegionAvail().Y - 55;
                float inputHeight = Math.Clamp(totalHeight, minHeight, maxHeight);

                ImGui.BeginChild("AdvancedModeSection", new Vector2(0, inputHeight), true, ImGuiWindowFlags.NoScrollbar);
                ImGui.InputTextMultiline("##AdvancedDesignMacro", ref advancedDesignMacroText, 2000, new Vector2(-1, inputHeight - 10), ImGuiInputTextFlags.AllowTabInput);
                ImGui.EndChild();

                ImGui.EndChild();

                designsToDisplay[i] = new CharacterDesign(tempDesignName, tempDesignMacro);
            }

            // Character Advanced Mode Toggle Button
            if (ImGui.Button(isAdvancedModeCharacter ? "Exit Advanced Mode" : "Advanced Mode"))
            {
                isAdvancedModeCharacter = !isAdvancedModeCharacter;
                if (isAdvancedModeCharacter)
                {
                    advancedCharacterMacroText = isEditCharacterWindowOpen
                        ? (!string.IsNullOrWhiteSpace(editedCharacterMacros) ? editedCharacterMacros : GenerateMacro())
                        : (!string.IsNullOrWhiteSpace(plugin.NewCharacterMacros) ? plugin.NewCharacterMacros : GenerateMacro());
                }
            }


            // Tooltip Icon (Info about Advanced Mode)
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 5); // Add slight spacing

            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.SetNextWindowPos(ImGui.GetCursorScreenPos() + new Vector2(20, -5));

                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300);
                ImGui.TextUnformatted("⚠️ Do not touch this unless you know what you're doing.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }



            // Show Advanced Mode Editor When Enabled
            if (isAdvancedModeCharacter)
            {
                ImGui.Text("Edit Macro Manually:");
                ImGui.InputTextMultiline("##AdvancedCharacterMacro", ref advancedCharacterMacroText, 2000, new Vector2(500, 150), ImGuiInputTextFlags.AllowTabInput);
            }
            // Check if required fields are filled
            bool canSaveCharacter = !string.IsNullOrWhiteSpace(tempName) &&
                                    !string.IsNullOrWhiteSpace(tempPenumbra) &&
                                    !string.IsNullOrWhiteSpace(tempGlamourer);

            // Disable the button if any required field is empty
            if (!canSaveCharacter)
                ImGui.BeginDisabled();

            if (ImGui.Button(isEditCharacterWindowOpen ? "Save Changes" : "Save Character"))
            {
                if (isEditCharacterWindowOpen)
                {
                    SaveEditedCharacter();
                }
                else
                {
                    // Pass Advanced Macro when Saving a New Character
                    string finalMacro = isAdvancedModeCharacter ? advancedCharacterMacroText : plugin.NewCharacterMacros;
                    plugin.SaveNewCharacter(finalMacro);
                }

                isEditCharacterWindowOpen = false;
                plugin.CloseAddCharacterWindow();
                isSecretMode = false;
            }



            if (!canSaveCharacter)
                ImGui.EndDisabled();

            ImGui.SameLine();

            if (ImGui.Button("Cancel"))
            {
                isEditCharacterWindowOpen = false;
                plugin.CloseAddCharacterWindow();
                isSecretMode = false;
            }
            ImGui.PopStyleVar(2);

        }

        private void DrawCharacterGrid()
        {
            // Get spacing & column settings
            float profileSpacing = plugin.ProfileSpacing;
            int columnCount = plugin.ProfileColumns;

            // Adjust column count if Design Panel is open
            if (isDesignPanelOpen)
            {
                columnCount = Math.Max(1, columnCount - 1);
            }

            // Calculate dynamic column width
            float columnWidth = (250 * plugin.ProfileImageScale) + profileSpacing;
            float availableWidth = ImGui.GetContentRegionAvail().X;

            // Ensure column count fits within available space
            columnCount = Math.Max(1, Math.Min(columnCount, (int)(availableWidth / columnWidth)));

            // Outer scrollable container (handles both horizontal & vertical scrolling)
            ImGui.BeginChild("CharacterGridContainer", new Vector2(0, 0), false,
                ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.AlwaysVerticalScrollbar);

            // Begin column layout
            if (columnCount > 1)
            {
                ImGui.Columns(columnCount, "CharacterGrid", false);
            }

            var visibleCharacters = plugin.Characters
    .Where(c => selectedTag == "All" || (c.Tags?.Contains(selectedTag) ?? false))
    .ToList();

            for (int i = 0; i < visibleCharacters.Count; i++)
            {
                var character = visibleCharacters[i];

                // Apply Search Filter
                if (!string.IsNullOrWhiteSpace(searchQuery) &&
                    !character.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
                    continue;

                {
                    var filteredChar = visibleCharacters[i];

                    // Ensure column width is properly set
                    if (columnCount > 1)
                    {
                        int colIndex = i % columnCount;
                        if (colIndex >= 0 && colIndex < ImGui.GetColumnsCount())
                        {
                            ImGui.SetColumnWidth(colIndex, columnWidth);
                        }
                    }

                    // Image Scaling
                    float scale = plugin.ProfileImageScale;
                    float maxSize = Math.Clamp(250 * scale, 64, 512); // Prevents excessive scaling
                    float nameplateHeight = 30;

                    float displayWidth, displayHeight;

                    string pluginDirectory = plugin.PluginDirectory;
                    string defaultImagePath = Path.Combine(pluginDirectory, "Assets", "Default.png");

                    string finalImagePath = !string.IsNullOrEmpty(character.ImagePath) && File.Exists(character.ImagePath)
                        ? character.ImagePath
                        : (File.Exists(defaultImagePath) ? defaultImagePath : "");

                    if (!string.IsNullOrEmpty(finalImagePath) && File.Exists(finalImagePath))
                    {
                        var texture = Plugin.TextureProvider.GetFromFile(finalImagePath).GetWrapOrDefault();

                        if (texture != null)
                        {
                            float originalWidth = texture.Width;
                            float originalHeight = texture.Height;
                            float aspectRatio = originalWidth / originalHeight;

                            if (aspectRatio > 1) // Landscape
                            {
                                displayWidth = maxSize;
                                displayHeight = maxSize / aspectRatio;
                            }
                            else // Portrait or Square
                            {
                                displayHeight = maxSize;
                                displayWidth = maxSize * aspectRatio;
                            }

                            float paddingX = (maxSize - displayWidth) / 2;
                            float paddingY = (maxSize - displayHeight) / 2;
                            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + paddingX);
                            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + paddingY);

                            ImGui.Image(texture.ImGuiHandle, new Vector2(displayWidth, displayHeight));

                            // Left-click - normal macro execution
                            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                            {
                                if (activeDesignCharacterIndex != -1)
                                {
                                    activeDesignCharacterIndex = -1;
                                    isDesignPanelOpen = false;
                                }
                                plugin.ExecuteMacro(character.Macros, character, null);
                                plugin.SetActiveCharacter(character);

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
                                    CharacterName = character.Name,
                                    NameplateColor = character.NameplateColor
                                };

                                _ = Plugin.UploadProfileAsync(profileToSend, character.LastInGameName ?? character.Name);
                            }

                            // Right-click - apply to <t>
                            if (ImGui.BeginPopupContextItem($"##ContextMenu_{character.Name}"))
                            {
                                if (ImGui.Selectable("Apply to Target"))
                                {
                                    // Always run the target macro – this replaces self with <t>
                                    string macro = Plugin.GenerateTargetMacro(character.Macros);

                                    if (!string.IsNullOrWhiteSpace(macro))
                                        plugin.ExecuteMacro(macro);
                                }


                                // Coloured line under "Apply to Target"
                                ImGui.Spacing();
                                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(character.NameplateColor, 1.0f));
                                ImGui.BeginChild($"##Separator_{character.Name}", new Vector2(ImGui.GetContentRegionAvail().X, 3), false);
                                ImGui.EndChild();
                                ImGui.PopStyleColor();
                                ImGui.Spacing();

                                // Scrollable design list
                                if (character.Designs.Count > 0)
                                {
                                    float itemHeight = ImGui.GetTextLineHeightWithSpacing();
                                    float maxVisible = 10;
                                    float scrollHeight = Math.Min(character.Designs.Count, maxVisible) * itemHeight + 8;

                                    if (ImGui.BeginChild($"##DesignScroll_{character.Name}", new Vector2(300, scrollHeight)))
                                    {
                                        foreach (var design in character.Designs)
                                        {
                                            if (ImGui.Selectable($"Apply Design: {design.Name}"))
                                            {
                                                var macro = Plugin.GenerateTargetMacro(
                                                    design.IsAdvancedMode ? design.AdvancedMacro : design.Macro
                                                );
                                                plugin.ExecuteMacro(macro);
                                            }
                                        }
                                        ImGui.EndChild();
                                    }
                                }

                                ImGui.EndPopup();
                            }


                        }
                    }

                    // Nameplate Rendering (Keeps consistent alignment)
                    DrawNameplate(character, maxSize, nameplateHeight);

                    // Buttons Section (Proper Spacing)
                    float buttonWidth = maxSize / 3.1f;
                    float btnWidth = maxSize / 3.2f;
                    float btnHeight = 24;
                    float btnSpacing = 4;

                    float btnStartX = ImGui.GetCursorPosX() + (maxSize - (3 * btnWidth + 2 * btnSpacing)) / 2;
                    ImGui.SetCursorPosX(btnStartX);

                    // "Designs" Button
                    if (ImGui.Button($"Designs##{i}", new Vector2(btnWidth, btnHeight)))
                    {
                        if (activeDesignCharacterIndex == i && isDesignPanelOpen)
                        {
                            activeDesignCharacterIndex = -1;
                            isDesignPanelOpen = false;
                        }
                        else
                        {
                            activeDesignCharacterIndex = i;
                            isDesignPanelOpen = true;
                        }
                    }

                    ImGui.SameLine(0, btnSpacing);
                    if (ImGui.Button($"Edit##{i}", new Vector2(btnWidth, btnHeight)))
                    {
                        OpenEditCharacterWindow(i);
                        isDesignPanelOpen = false;
                    }

                    ImGui.SameLine(0, btnSpacing);
                    bool isCtrlShiftPressed = ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift;
                    if (ImGui.Button($"Delete##{i}", new Vector2(btnWidth, btnHeight)))
                    {
                        if (isCtrlShiftPressed)
                        {
                            plugin.Characters.RemoveAt(i);
                            plugin.Configuration.Save();
                        }
                    }

                    // Tooltip for Delete Button
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text("Hold Ctrl + Shift and click to delete.");
                        ImGui.EndTooltip();
                    }
                }
                ImGui.NextColumn(); // Move to next column
            }

            if (columnCount > 1)
            {
                ImGui.Columns(1);
            }

            ImGui.EndChild(); // Close Outer Scrollable Container
        }


        private void DrawNameplate(Character character, float width, float height)
        {
            var cursorPos = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();

            // Nameplate Background
            drawList.AddRectFilled(
                new Vector2(cursorPos.X, cursorPos.Y),
                new Vector2(cursorPos.X + width, cursorPos.Y + height),
                ImGui.GetColorU32(new Vector4(0, 0, 0, 0.8f)) // Black background with slight transparency
            );

            // Nameplate Colour Strip
            drawList.AddRectFilled(
                new Vector2(cursorPos.X, cursorPos.Y + height - 4),
                new Vector2(cursorPos.X + width, cursorPos.Y + height),
                ImGui.GetColorU32(new Vector4(character.NameplateColor.X, character.NameplateColor.Y, character.NameplateColor.Z, 1.0f))
            );

            // Character Name
            var textSize = ImGui.CalcTextSize(character.Name);
            var textPosX = cursorPos.X + (width - textSize.X) / 2;
            var textPosY = cursorPos.Y + (height - textSize.Y) / 2;

            drawList.AddText(new Vector2(textPosX, textPosY), ImGui.GetColorU32(ImGuiCol.Text), character.Name);

            // Draw Favorite Star Symbol
            string starSymbol = character.IsFavorite ? "★" : "☆";
            var starPos = new Vector2(cursorPos.X + 5, cursorPos.Y + 5);
            var starSize = ImGui.CalcTextSize(starSymbol);
            drawList.AddText(starPos, ImGui.GetColorU32(ImGuiCol.Text), starSymbol);

            // Hover + Click Region (no layout reservation)
            var starEnd = new Vector2(starPos.X + starSize.X + 4, starPos.Y + starSize.Y + 2);
            if (ImGui.IsMouseHoveringRect(starPos, starEnd))
            {
                ImGui.SetTooltip($"{(character.IsFavorite ? "Remove" : "Add")} {character.Name} as a Favourite");

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    character.IsFavorite = !character.IsFavorite;
                    plugin.SaveConfiguration();
                    SortCharacters();
                }
            }
            // RP Profile Button — ID Card Icon
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.SetWindowFontScale(0.85f); // Shrink to match star size

            string icon = "\uf2c2"; // ID Card
            var iconSize = ImGui.CalcTextSize(icon);
            var iconPos = new Vector2(cursorPos.X + width - iconSize.X - 12, cursorPos.Y + 6);
            var iconColor = ImGui.GetColorU32(ImGuiCol.Text);

            // Draw the icon at reduced scale
            drawList.AddText(iconPos, iconColor, icon);

            // Reset scale and font
            ImGui.SetWindowFontScale(1.0f);
            ImGui.PopFont();

            // Clickable area
            var iconHitMin = iconPos;
            var iconHitMax = iconPos + iconSize + new Vector2(4, 4);

            if (ImGui.IsMouseHoveringRect(iconHitMin, iconHitMax))
            {
                ImGui.SetTooltip($"View RolePlay Profile for {character.Name}");

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    plugin.OpenRPProfileViewWindow(character);
                }
            }

            ImGui.Dummy(new Vector2(width, height)); // Maintain proper positioning
        }


        // Place GenerateMacro() here:
        private string GenerateMacro()
        {
            string penumbra = isEditCharacterWindowOpen ? editedCharacterPenumbra : plugin.NewPenumbraCollection;
            string glamourer = isEditCharacterWindowOpen ? editedCharacterGlamourer : plugin.NewGlamourerDesign;
            string customize = isEditCharacterWindowOpen ? editedCharacterCustomize : plugin.NewCustomizeProfile;
            string honorificTitle = isEditCharacterWindowOpen ? editedCharacterHonorificTitle : plugin.NewCharacterHonorificTitle;
            string honorificPrefix = isEditCharacterWindowOpen ? editedCharacterHonorificPrefix : plugin.NewCharacterHonorificPrefix;
            string honorificSuffix = isEditCharacterWindowOpen ? editedCharacterHonorificSuffix : plugin.NewCharacterHonorificSuffix;
            Vector3 honorificColor = isEditCharacterWindowOpen ? editedCharacterHonorificColor : plugin.NewCharacterHonorificColor;
            Vector3 honorificGlow = isEditCharacterWindowOpen ? editedCharacterHonorificGlow : plugin.NewCharacterHonorificGlow;

            if (string.IsNullOrWhiteSpace(penumbra) || string.IsNullOrWhiteSpace(glamourer))
                return "/penumbra redraw self";

            string macro =
                $"/penumbra collection individual | {penumbra} | self\n" +
                $"/glamour apply {glamourer} | self\n";

            // Character Automation (if enabled)
            string automation = isEditCharacterWindowOpen ? editedCharacterAutomation : plugin.NewCharacterAutomation;

            if (plugin.Configuration.EnableAutomations)
            {
                if (string.IsNullOrWhiteSpace(automation))
                    macro += "/glamour automation enable None\n";
                else
                    macro += $"/glamour automation enable {automation}\n";
            }


            macro += "/customize profile disable <me>\n";

            if (!string.IsNullOrWhiteSpace(customize))
                macro += $"/customize profile enable <me>, {customize}\n";

            // Ensure honorific is always cleared before setting a new one
            macro += "/honorific force clear\n";

            if (!string.IsNullOrWhiteSpace(honorificTitle))
            {
                string colorHex = $"#{(int)(honorificColor.X * 255):X2}{(int)(honorificColor.Y * 255):X2}{(int)(honorificColor.Z * 255):X2}";
                string glowHex = $"#{(int)(honorificGlow.X * 255):X2}{(int)(honorificGlow.Y * 255):X2}{(int)(honorificGlow.Z * 255):X2}";
                macro += $"/honorific force set {honorificTitle} | {honorificPrefix} | {colorHex} | {glowHex}\n";
            }

            macro += "/moodle remove self preset all\n";

            string moodlePreset = isEditCharacterWindowOpen ? editedCharacterMoodlePreset : tempMoodlePreset;
            if (!string.IsNullOrWhiteSpace(moodlePreset))
                macro += $"/moodle apply self preset \"{moodlePreset}\"\n";

            int idlePose = isEditCharacterWindowOpen ? plugin.Characters[selectedCharacterIndex].IdlePoseIndex : plugin.NewCharacterIdlePoseIndex;
            if (idlePose != 7)
            {
                macro += $"/sidle {idlePose}\n";
            }

            macro += "/penumbra redraw self";

            return macro;
        }
        // Secret‐mode macro (when Ctrl+Shift+“Add Character”)
        private string GenerateSecretMacro()
        {
            // exactly the same inputs as GenerateMacro()
            string penumbra = isEditCharacterWindowOpen ? editedCharacterPenumbra : plugin.NewPenumbraCollection;
            string glamourer = isEditCharacterWindowOpen ? editedCharacterGlamourer : plugin.NewGlamourerDesign;
            string customize = isEditCharacterWindowOpen ? editedCharacterCustomize : plugin.NewCustomizeProfile;
            string honorTitle = isEditCharacterWindowOpen ? editedCharacterHonorificTitle : plugin.NewCharacterHonorificTitle;
            string honorPref = isEditCharacterWindowOpen ? editedCharacterHonorificPrefix : plugin.NewCharacterHonorificPrefix;
            string honorSuf = isEditCharacterWindowOpen ? editedCharacterHonorificSuffix : plugin.NewCharacterHonorificSuffix;
            Vector3 honorColor = isEditCharacterWindowOpen ? editedCharacterHonorificColor : plugin.NewCharacterHonorificColor;
            Vector3 honorGlow = isEditCharacterWindowOpen ? editedCharacterHonorificGlow : plugin.NewCharacterHonorificGlow;
            string moodlePreset = isEditCharacterWindowOpen ? editedCharacterMoodlePreset : plugin.NewCharacterMoodlePreset;
            int idlePose = isEditCharacterWindowOpen
                                    ? plugin.Characters[selectedCharacterIndex].IdlePoseIndex
                                    : plugin.NewCharacterIdlePoseIndex;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"/penumbra collection individual | {penumbra} | self");
            sb.AppendLine($"/penumbra bulktag disable {penumbra} | gear");
            sb.AppendLine($"/penumbra bulktag disable {penumbra} | hair");
            sb.AppendLine($"/penumbra bulktag enable {penumbra} | {glamourer}");
            sb.AppendLine("/glamour apply no clothes | self");
            sb.AppendLine($"/glamour apply {glamourer} | self");
            sb.AppendLine("/customize profile disable <me>");
            if (!string.IsNullOrWhiteSpace(customize))
                sb.AppendLine($"/customize profile enable <me>, {customize}");
            sb.AppendLine("/honorific force clear");
            if (!string.IsNullOrWhiteSpace(honorTitle))
            {
                var colorHex = $"#{(int)(honorColor.X * 255):X2}{(int)(honorColor.Y * 255):X2}{(int)(honorColor.Z * 255):X2}";
                var glowHex = $"#{(int)(honorGlow.X * 255):X2}{(int)(honorGlow.Y * 255):X2}{(int)(honorGlow.Z * 255):X2}";
                sb.AppendLine($"/honorific force set {honorTitle} | {honorPref} | {colorHex} | {glowHex}");
            }
            sb.AppendLine("/moodle remove self preset all");
            if (!string.IsNullOrWhiteSpace(moodlePreset))
                sb.AppendLine($"/moodle apply self preset \"{moodlePreset}\"");
            if (idlePose != 7)
                sb.AppendLine($"/sidle {idlePose}");
            sb.Append("/penumbra redraw self");
            return sb.ToString();
        }



        // Add ExtractGlamourerDesignFromMacro
        private string ExtractGlamourerDesignFromMacro(string macro)// Store old honorific before updating

        {
            // Find the Glamourer line in the macro
            string[] lines = macro.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("/glamour apply ", StringComparison.OrdinalIgnoreCase))
                {
                    return line.Replace("/glamour apply ", "").Replace(" | self", "").Trim();
                }
            }
            return ""; // Return empty if nothing was found
        }
        private static string TruncateWithEllipsis(string text, float maxWidth)
        {
            while (ImGui.CalcTextSize(text + "...").X > maxWidth && text.Length > 0)
                text = text[..^1];
            return text + "...";
        }


        private void DrawDesignPanel()
        {
            bool anyRowHovered = false;
            bool anyHeaderHovered = false;
            float scale = plugin.Configuration.UIScaleMultiplier;
            var style = ImGui.GetStyle();

            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, style.FramePadding * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, style.ItemSpacing * scale);

            if (activeDesignCharacterIndex < 0 || activeDesignCharacterIndex >= plugin.Characters.Count)
                return;

            var character = plugin.Characters[activeDesignCharacterIndex];

            // Close Add Design when switching characters
            if (selectedCharacterIndex != activeDesignCharacterIndex)
            {
                isEditDesignWindowOpen = false;
                isAdvancedModeWindowOpen = false;
                editedDesignName = "";
                editedGlamourerDesign = "";
                editedAutomation = "";
                editedCustomizeProfile = "";
                editedDesignMacro = "";
                advancedDesignMacroText = "";
                selectedCharacterIndex = activeDesignCharacterIndex; // Update tracking
            }

            // Header with Add Button
            string name = $"Designs for {character.Name}";
            float maxTextWidth = ImGui.GetContentRegionAvail().X - 75f; // space for buttons + buffer

            var clippedName = ImGui.CalcTextSize(name).X > maxTextWidth
                ? TruncateWithEllipsis(name, maxTextWidth)
                : name;

            ImGui.TextUnformatted(clippedName);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(name);

            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 60f);
            ImGui.SameLine();

            // Plus Button (Green)
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(new Vector4(0.27f, 1.07f, 0.27f, 1.0f)));
            // read modifier keys once
            var io = ImGui.GetIO();
            bool ctrlHeld = io.KeyCtrl;
            bool shiftHeld = io.KeyShift;

            if (ImGui.Button("+##AddDesign"))
            {
                if (ctrlHeld && shiftHeld)
                {
                    // ── SECRET DESIGN MODE ──
                    isSecretDesignMode = true;
                    AddNewDesign();
                    // preload secret macro
                    editedDesignMacro = GenerateSecretDesignMacro(plugin.Characters[activeDesignCharacterIndex]);
                    if (isAdvancedModeDesign)
                        advancedDesignMacroText = editedDesignMacro;
                }
                else if (shiftHeld)
                {
                    // ── IMPORT MODE (Shift only) ──
                    isSecretDesignMode = false;
                    isImportWindowOpen = true;
                    targetForDesignImport = plugin.Characters[activeDesignCharacterIndex];
                }
                else
                {
                    // ── NORMAL ADD ──
                    isSecretDesignMode = false;
                    AddNewDesign();
                    // preload normal macro
                    editedDesignMacro = GenerateDesignMacro(plugin.Characters[activeDesignCharacterIndex]);
                    if (isAdvancedModeDesign)
                        advancedDesignMacroText = editedDesignMacro;
                }
            }
            ImGui.PopStyleColor();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("Click to add a new design\nHold Shift to import from another character");
                ImGui.EndTooltip();
            }
            ImGui.SameLine();
            // Folder Button (Yellow)
            // Folder icon button (matches + and x in size)
            // ─── Inline “Add Folder” button & popup ───
            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button("\uf07b##AddFolder", new Vector2(20, 20)))
                ImGui.OpenPopup("CreateFolderPopup");
            ImGui.PopFont();

            // ONLY here, *before* you BeginChild the list, handle the popup:
            if (ImGui.BeginPopup("CreateFolderPopup"))
            {
                ImGui.Text("New Folder Name:");
                ImGui.SetNextItemWidth(200 * plugin.Configuration.UIScaleMultiplier);
                ImGui.InputText("##NewFolder", ref newFolderName, 100);

                ImGui.Separator();
                if (ImGui.Button("Create"))
                {
                    var folder = new DesignFolder(newFolderName, Guid.NewGuid())
                    {
                        ParentFolderId = null,
                        SortOrder = character.DesignFolders.Count
                    };
                    character.DesignFolders.Add(folder);
                    plugin.SaveConfiguration();
                    plugin.RefreshTreeItems(character);
                    newFolderName = "";
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    newFolderName = "";
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("Add Folder");
                ImGui.EndTooltip();
            }

            // Close Button (Red)
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - 20);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.27f, 0.27f, 1.0f));
            if (ImGui.Button("x##CloseDesignPanel"))
            {
                activeDesignCharacterIndex = -1;
                isDesignPanelOpen = false;
                isEditDesignWindowOpen = false;
                isAdvancedModeWindowOpen = false; // Close pop-up window too
            }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                ImGui.BeginTooltip();
                ImGui.Text("Close Design Panel.");
                ImGui.EndTooltip();
            }

            ImGui.Separator();

            // 1RENDER THE FORM **FIRST** BEFORE THE LIST
            if (isEditDesignWindowOpen)
            {
                ImGui.BeginChild("EditDesignForm", new Vector2(0, 320), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize);

                bool isNewDesign = string.IsNullOrEmpty(editedDesignName);
                ImGui.Text(isNewDesign ? "Add Design" : "Edit Design");

                float inputWidth = 200;
                ImGui.Text("Design Name*");
                ImGui.SetCursorPosX(10);
                ImGui.SetNextItemWidth(inputWidth);
                ImGui.InputText("##DesignName", ref editedDesignName, 100);

                ImGui.Separator();

                // Glamourer Design Label
                ImGui.Text("Glamourer Design*");

                // Tooltip Icon
                ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf05a"); // Info icon
                ImGui.PopFont();

                if (ImGui.IsItemHovered()) // Show tooltip on hover
                {
                    ImGui.BeginTooltip();
                    ImGui.PushTextWrapPos(300);
                    ImGui.TextUnformatted("Enter the name of the Glamourer design to apply to this character.");
                    ImGui.TextUnformatted("Must be entered EXACTLY as it is named in Glamourer!");
                    ImGui.TextUnformatted("Note: You can add additional designs later.");
                    ImGui.PopTextWrapPos();
                    ImGui.EndTooltip();
                }

                // Input Field
                ImGui.SetCursorPosX(10);
                ImGui.SetNextItemWidth(inputWidth);
                var oldGlam = editedGlamourerDesign;
                if (ImGui.InputText("##GlamourerDesign", ref editedGlamourerDesign, 100))
                {
                    // Always regenerate the preview from the *same* generator you're about to save with
                    if (!isAdvancedModeDesign)
                    {
                        editedDesignMacro = isSecretDesignMode
                            ? GenerateSecretDesignMacro(character)
                            : GenerateDesignMacro(character);
                    }
                    else
                    {
                        // 1) patch only the exact apply‐line for the old design name
                        advancedDesignMacroText = PatchMacroLine(
                            advancedDesignMacroText,
                            $"/glamour apply {oldGlam} |",
                            $"/glamour apply {editedGlamourerDesign} | self"
                        );

                        // 2) now patch the DESIGN bit in your existing bulktag‐enable line
                        advancedDesignMacroText = UpdateBulkTagEnableDesignInMacro(
                            advancedDesignMacroText,
                            character.PenumbraCollection,
                            editedGlamourerDesign
                        );
                    }
                }

                if (plugin.Configuration.EnableAutomations)
                {
                    // Automation Label
                    ImGui.Text("Glamourer Automation");

                    ImGui.SameLine();
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.Text("\uf05a"); // Info icon
                    ImGui.PopFont();

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.PushTextWrapPos(300);
                        ImGui.TextUnformatted("Optional: Enter the name of a Glamourer automation to use with this design.");
                        ImGui.Separator();
                        ImGui.TextUnformatted("⚠️ This must match the name of the automation EXACTLY.");
                        ImGui.TextUnformatted("Just like with Glamourer Designs, capitalization and spacing matter.");
                        ImGui.Separator();
                        ImGui.TextUnformatted("If you don't want to use an automation here, create one in Glamourer called:");
                        ImGui.TextUnformatted("None");
                        ImGui.TextUnformatted("and leave it completely empty.");
                        ImGui.Separator();
                        ImGui.TextUnformatted("Why? Character Select+ always includes an automation line.");
                        ImGui.TextUnformatted("This makes sure your macro is still valid even when no automation is intended.");
                        ImGui.PopTextWrapPos();
                        ImGui.EndTooltip();
                    }

                    // Automation Input Field
                    ImGui.SetCursorPosX(10);
                    ImGui.SetNextItemWidth(inputWidth);
                    // ── Automation Input Field ──
                    if (!isAdvancedModeDesign)
                    {
                        // Normal Mode: full regenerate
                        editedDesignMacro = isSecretDesignMode
                            ? GenerateSecretDesignMacro(character)
                            : GenerateDesignMacro(character);
                    }
                    else
                    {
                        // Advanced Mode: patch only the automation line in place
                        var line = string.IsNullOrWhiteSpace(editedAutomation)
                            ? "/glamour automation enable None"
                            : $"/glamour automation enable {editedAutomation}";
                        advancedDesignMacroText = PatchMacroLine(
                            advancedDesignMacroText,
                            "/glamour automation enable",
                            line
                        );
                    }
                }
                // 🔹 Customize+ Label
                ImGui.Text("Customize+ Profile");

                ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf05a"); // Info icon
                ImGui.PopFont();

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.PushTextWrapPos(300);
                    ImGui.TextUnformatted("Optional: Enter the name of a Customize+ profile to apply with this design.");
                    ImGui.Separator();
                    ImGui.TextUnformatted("If left blank:");
                    ImGui.TextUnformatted("• Uses the Customize+ profile set for the character (if any).");
                    ImGui.TextUnformatted("• Otherwise disables all Customize+ profiles.");
                    ImGui.PopTextWrapPos();
                    ImGui.EndTooltip();
                }

                // Input Field
                ImGui.SetCursorPosX(10);
                ImGui.SetNextItemWidth(200);
                // ── Customize+ Profile Input Field ──
                if (ImGui.InputText("##CustomizePlus", ref editedCustomizeProfile, 100))
                {
                    if (!isAdvancedModeDesign)
                    {
                        // Normal Mode: full regenerate
                        editedDesignMacro = isSecretDesignMode
                            ? GenerateSecretDesignMacro(character)
                            : GenerateDesignMacro(character);
                    }
                    else
                    {
                        // Advanced Mode: patch only the two customize lines

                        // 1) ensure the disable line is correct
                        advancedDesignMacroText = PatchMacroLine(
                            advancedDesignMacroText,
                            "/customize profile disable",
                            "/customize profile disable <me>"
                        );

                        // 2) patch or remove the enable line
                        if (!string.IsNullOrWhiteSpace(editedCustomizeProfile))
                        {
                            advancedDesignMacroText = PatchMacroLine(
                                advancedDesignMacroText,
                                "/customize profile enable",
                                $"/customize profile enable <me>, {editedCustomizeProfile}"
                            );
                        }
                        else
                        {
                            // remove any existing enable line
                            advancedDesignMacroText = string.Join("\n",
                                advancedDesignMacroText
                                    .Split('\n')
                                    .Where(l => !l.TrimStart().StartsWith("/customize profile enable"))
                            );
                        }
                    }
                }

                ImGui.Separator();
                // Identify whether we're adding or editing
                bool isAddMode = string.IsNullOrWhiteSpace(editedDesignName);

                // Add spacing
                ImGui.Dummy(new Vector2(0, 3));

                // Automation Reminder
                if (isAddMode && plugin.Configuration.EnableAutomations)
                {
                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.85f, 0.3f, 1f));
                    ImGui.PushFont(UiBuilder.IconFont);

                    ImGui.PushID("AddAutomationTip");
                    ImGui.TextUnformatted("\uf071");
                    ImGui.PopID();

                    ImGui.PopFont();
                    ImGui.PopStyleColor();

                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
                    {
                        ImGui.BeginTooltip();
                        ImGui.PushTextWrapPos(300);
                        ImGui.TextUnformatted("Don't forget to create an Automation in Glamourer named \"None\" and enter your in-game character name next to \"Any World\" and Set to Character.");
                        ImGui.PopTextWrapPos();
                        ImGui.EndTooltip();
                    }
                }

                if (ImGui.Button(isAdvancedModeDesign ? "Exit Advanced Mode" : "Advanced Mode"))
                {
                    isAdvancedModeDesign = !isAdvancedModeDesign;
                    isAdvancedModeWindowOpen = isAdvancedModeDesign;

                    // Always update macro preview with latest edits when toggling ON
                    if (isAdvancedModeDesign)
                    {
                        advancedDesignMacroText = !string.IsNullOrWhiteSpace(editedDesignMacro)
                            ? editedDesignMacro
                            : GenerateDesignMacro(plugin.Characters[activeDesignCharacterIndex]);
                    }
                }

                // Tooltip Icon
                ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf05a");
                ImGui.PopFont();
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.PushTextWrapPos(300);
                    ImGui.TextUnformatted("⚠️ Do not touch this unless you know what you're doing.");
                    ImGui.PopTextWrapPos();
                    ImGui.EndTooltip();
                }



                ImGui.Separator();

                // Align Buttons Properly
                float buttonWidth = 85;
                float buttonHeight = 20;
                float buttonSpacing = 8;
                float totalButtonWidth = (buttonWidth * 2 + buttonSpacing);
                float availableWidth = ImGui.GetContentRegionAvail().X;
                float buttonPosX = (availableWidth > totalButtonWidth)
                    ? (availableWidth - totalButtonWidth) / 2
                    : 0; // fallback: align left if not enough space

                ImGui.SetCursorPosX(buttonPosX);

                if (!isAdvancedModeDesign && !isSecretDesignMode)
                    editedDesignMacro = GenerateDesignMacro(character);
                bool canSave = !string.IsNullOrWhiteSpace(editedDesignName) && !string.IsNullOrWhiteSpace(editedGlamourerDesign);

                if (!canSave)
                    ImGui.BeginDisabled();

                if (ImGui.Button("Save Design", new Vector2(buttonWidth, buttonHeight)))
                {
                    SaveDesign(plugin.Characters[activeDesignCharacterIndex]);
                    isSecretDesignMode = false;
                    isEditDesignWindowOpen = false;
                    isAdvancedModeWindowOpen = false; // Close pop-up after saving
                }

                if (!canSave)
                    ImGui.EndDisabled();

                ImGui.SameLine();

                if (ImGui.Button("Cancel", new Vector2(buttonWidth, buttonHeight)))
                {
                    isSecretDesignMode = false;
                    isEditDesignWindowOpen = false;
                    isAdvancedModeWindowOpen = false;
                }

                ImGui.EndChild(); // END FORM
            }

            ImGui.Separator(); // Visually separate the list
            ImGui.Text("Sort Designs By:");
            ImGui.SameLine();

            if (ImGui.BeginCombo("##DesignSortDropdown", currentDesignSort.ToString()))
            {
                if (ImGui.Selectable("Favorites", currentDesignSort == DesignSortType.Favorites))
                {
                    currentDesignSort = DesignSortType.Favorites;
                    SortDesigns(plugin.Characters[activeDesignCharacterIndex]);
                }
                if (ImGui.Selectable("Alphabetical", currentDesignSort == DesignSortType.Alphabetical))
                {
                    currentDesignSort = DesignSortType.Alphabetical;
                    SortDesigns(plugin.Characters[activeDesignCharacterIndex]);
                }
                if (ImGui.Selectable("Newest", currentDesignSort == DesignSortType.Recent))
                {
                    currentDesignSort = DesignSortType.Recent;
                    SortDesigns(plugin.Characters[activeDesignCharacterIndex]);
                }
                if (ImGui.Selectable("Oldest", currentDesignSort == DesignSortType.Oldest))
                {
                    currentDesignSort = DesignSortType.Oldest;
                    SortDesigns(plugin.Characters[activeDesignCharacterIndex]);
                }
                if (ImGui.Selectable("Manual", currentDesignSort == DesignSortType.Manual))
                {
                    currentDesignSort = DesignSortType.Manual;
                }
                ImGui.EndCombo();
            }


            ImGui.Separator();


            // RENDER THE DESIGN LIST
            ImGui.BeginChild(
                "DesignListBackground",
                new Vector2(0, ImGui.GetContentRegionAvail().Y),
                true,
                ImGuiWindowFlags.NoScrollbar
            );

            // 1) Build unified top-level list (only roots)
            var renderItems = new List<(string name, bool isFolder, object item, DateTime dateAdded, int manual)>();

            // 1a) Root folders (no parent)
            foreach (var f in character.DesignFolders
                     .Where(f => f.ParentFolderId == null))
            {
                renderItems.Add((
                    f.Name,
                    true,
                    f as object,
                    DateTime.MinValue,
                    f.SortOrder
                ));
            }

            // 1b) Root designs (no folder)
            foreach (var d in character.Designs
                     .Where(d => d.FolderId == null))
            {
                renderItems.Add((
                    d.Name,
                    false,
                    d as object,
                    d.DateAdded,
                    d.SortOrder
                ));
            }

            // 2) Sort according to currentDesignSort
            switch (currentDesignSort)
            {
                case DesignSortType.Favorites:
                    renderItems = renderItems
                        .OrderByDescending(x => x.isFolder ? false : ((CharacterDesign)x.item).IsFavorite)
                        .ThenBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    break;
                case DesignSortType.Alphabetical:
                    renderItems = renderItems
                        .OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    break;
                case DesignSortType.Recent:
                    renderItems = renderItems
                        .OrderByDescending(x => x.dateAdded)
                        .ToList();
                    break;
                case DesignSortType.Oldest:
                    renderItems = renderItems
                        .OrderBy(x => x.dateAdded)
                        .ToList();
                    break;
                case DesignSortType.Manual:
                    renderItems = renderItems
                        .OrderBy(x => x.manual)
                        .ToList();
                    break;
            }

            // 3) Render each entry
            foreach (var entry in renderItems)
            {
                if (entry.isFolder)
                {
                    var folder = (DesignFolder)entry.item;

                    // 3a) Inline‐rename?
                    bool isThisRenaming = isRenamingFolder && folder.Id == renameFolderId;
                    bool open = false;

                    if (isThisRenaming)
                    {
                        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.2f, 0.2f, 0.2f, 1f));
                        ImGui.SetNextItemWidth(200);
                        if (ImGui.InputText("##InlineRename",
                                            ref renameFolderBuf,
                                            128,
                                            ImGuiInputTextFlags.EnterReturnsTrue))
                        {
                            folder.Name = renameFolderBuf;
                            isRenamingFolder = false;
                            plugin.SaveConfiguration();
                            plugin.RefreshTreeItems(character);
                        }
                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        // 3b) Single CollapsingHeader for the folder
                        open = ImGui.CollapsingHeader(
                            $"{folder.Name}##F{folder.Id}",
                            ImGuiTreeNodeFlags.SpanFullWidth
                        );

                        // Drag‐source so you can pick up folders
                        if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceAllowNullID))
                        {
                            draggedFolder = folder;
                            ImGui.SetDragDropPayload("FOLDER_MOVE", IntPtr.Zero, 0);
                            ImGui.TextUnformatted($"Moving Folder: {folder.Name}");
                            ImGui.EndDragDropSource();
                        }

                        // Right‐click context menu
                        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                            ImGui.OpenPopup($"FolderCtx{folder.Id}");
                        if (ImGui.BeginPopup($"FolderCtx{folder.Id}"))
                        {
                            if (ImGui.MenuItem("Rename Folder"))
                            {
                                renameFolderId = folder.Id;
                                renameFolderBuf = folder.Name;
                                isRenamingFolder = true;
                                ImGui.CloseCurrentPopup();
                            }
                            if (ImGui.MenuItem("Delete Folder"))
                            {
                                // 1) Un-folder any direct designs in THIS folder
                                foreach (var d in character.Designs.Where(d => d.FolderId == folder.Id))
                                    d.FolderId = null;

                                // 2) Reparent any *subfolders* up to root
                                foreach (var sub in character.DesignFolders.Where(f => f.ParentFolderId == folder.Id))
                                    sub.ParentFolderId = null;

                                // 3) Finally, remove THIS folder itself
                                character.DesignFolders.RemoveAll(f2 => f2.Id == folder.Id);

                                plugin.SaveConfiguration();
                                plugin.RefreshTreeItems(character);
                                ImGui.CloseCurrentPopup();
                            }
                            ImGui.EndPopup();
                        }
                    }

                    // 3c) Hover + drop‐to‐folder logic (shared for designs & folders)
                    var hdrMin = ImGui.GetItemRectMin();
                    var hdrMax = ImGui.GetItemRectMax();
                    bool overHeader = ImGui.IsMouseHoveringRect(hdrMin, hdrMax, true);
                    if ((draggedDesign != null || draggedFolder != null) && overHeader)
                    {
                        var dl = ImGui.GetWindowDrawList();
                        uint col = ImGui.GetColorU32(new Vector4(0.3f, 0.5f, 1f, 1f));
                        dl.AddRect(hdrMin, hdrMax, col, 0, ImDrawFlags.None, 2);
                    }
                    if (overHeader) anyHeaderHovered = true;

                    // Drop designs into folder
                    if (draggedDesign != null && overHeader && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    {
                        draggedDesign.FolderId = folder.Id;
                        plugin.SaveConfiguration();
                        plugin.RefreshTreeItems(character);
                        draggedDesign = null;
                    }

                    // Drop folders into folder (nesting)
                    if (draggedFolder != null
                        && overHeader
                        && ImGui.IsMouseReleased(ImGuiMouseButton.Left)
                        && draggedFolder != folder)
                    {
                        draggedFolder.ParentFolderId = folder.Id;
                        plugin.SaveConfiguration();
                        plugin.RefreshTreeItems(character);
                        draggedFolder = null;
                    }

                    // 3d) If expanded, draw nested folders & designs
                    if (open)
                    {
                        // ── child folders ──
                        foreach (var child in character.DesignFolders
         .Where(f2 => f2.ParentFolderId == folder.Id)
         .OrderBy(f2 => f2.SortOrder))
                        {
                            ImGui.Indent(20);

                            // Inline-rename check for this child
                            bool isChildRenaming = isRenamingFolder && child.Id == renameFolderId;
                            bool childOpen = false;

                            if (isChildRenaming)
                            {
                                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.2f, 0.2f, 0.2f, 1f));
                                ImGui.SetNextItemWidth(200);
                                if (ImGui.InputText("##InlineRenameChild",
                                                    ref renameFolderBuf,
                                                    128,
                                                    ImGuiInputTextFlags.EnterReturnsTrue))
                                {
                                    child.Name = renameFolderBuf;
                                    isRenamingFolder = false;
                                    plugin.SaveConfiguration();
                                    plugin.RefreshTreeItems(character);
                                }
                                ImGui.PopStyleColor();
                            }
                            else
                            {
                                // Child header
                                childOpen = ImGui.CollapsingHeader(
                                    $"{child.Name}##F{child.Id}",
                                    ImGuiTreeNodeFlags.SpanFullWidth
                                );
                                var childhdrMin = ImGui.GetItemRectMin();
                                var childhdrMax = ImGui.GetItemRectMax();
                                bool overChildHeader = ImGui.IsMouseHoveringRect(childhdrMin, childhdrMax, true);
                                if (draggedDesign != null && overChildHeader && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                                {
                                    draggedDesign.FolderId = child.Id;
                                    plugin.SaveConfiguration();
                                    plugin.RefreshTreeItems(character);
                                    draggedDesign = null;
                                }

                                // Drag source for folders
                                if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceAllowNullID))
                                {
                                    draggedFolder = child;
                                    ImGui.SetDragDropPayload("FOLDER_MOVE", IntPtr.Zero, 0);
                                    ImGui.TextUnformatted($"Moving Folder: {child.Name}");
                                    ImGui.EndDragDropSource();
                                }

                                // Right-click context menu
                                if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                                    ImGui.OpenPopup($"FolderCtx{child.Id}");
                                if (ImGui.BeginPopup($"FolderCtx{child.Id}"))
                                {
                                    if (ImGui.MenuItem("Rename Folder"))
                                    {
                                        renameFolderId = child.Id;
                                        renameFolderBuf = child.Name;
                                        isRenamingFolder = true;
                                        ImGui.CloseCurrentPopup();
                                    }
                                    if (ImGui.MenuItem("Delete Folder"))
                                    {
                                        // 1) Un-folder any direct designs in THIS folder
                                        foreach (var d in character.Designs.Where(d => d.FolderId == folder.Id))
                                            d.FolderId = null;

                                        // 2) Reparent any *subfolders* up to root
                                        foreach (var sub in character.DesignFolders.Where(f => f.ParentFolderId == folder.Id))
                                            sub.ParentFolderId = null;

                                        // 3) Finally, remove THIS folder itself
                                        character.DesignFolders.RemoveAll(f2 => f2.Id == folder.Id);

                                        plugin.SaveConfiguration();
                                        plugin.RefreshTreeItems(character);
                                        ImGui.CloseCurrentPopup();
                                    }
                                    ImGui.EndPopup();
                                }
                            }

                            // Draw this child’s designs if expanded
                            if (childOpen)
                            {
                                foreach (var d2 in character.Designs
                                         .Where(d2 => d2.FolderId == child.Id)
                                         .OrderBy(d2 => d2.SortOrder))
                                {
                                    DrawDesignRow(character, d2, true);
                                    if (ImGui.IsItemHovered()) anyRowHovered = true;
                                }
                            }

                            ImGui.Unindent();
                        }

                        // ── this folder’s own designs ──
                        foreach (var d in character.Designs
                                 .Where(d => d.FolderId == folder.Id)
                                 .OrderBy(d => d.SortOrder))
                        {
                            DrawDesignRow(character, d, true);
                            if (ImGui.IsItemHovered()) anyRowHovered = true;
                        }
                    }
                }
                else
                {
                    // 3e) Standalone design
                    var design = (CharacterDesign)entry.item;
                    DrawDesignRow(character, design, false);
                    if (ImGui.IsItemHovered()) anyRowHovered = true;
                }
            }

            // 4) Drop outside any header → un‐folder
            if (draggedDesign != null
                && ImGui.IsMouseReleased(ImGuiMouseButton.Left)
                && !anyHeaderHovered
                && !anyRowHovered)
            {
                draggedDesign.FolderId = null;
                plugin.SaveConfiguration();
                plugin.RefreshTreeItems(character);
                draggedDesign = null;
            }
            // Drop to root (outside any header)
            if (draggedFolder != null
                && ImGui.IsMouseReleased(ImGuiMouseButton.Left)
                && !anyHeaderHovered
                && !anyRowHovered)
            {
                draggedFolder.ParentFolderId = null;
                plugin.SaveConfiguration();
                plugin.RefreshTreeItems(character);
                draggedFolder = null;
            }

            ImGui.EndChild();


            // RENDER THE ADVANCED MODE POP-UP WINDOW
            if (isAdvancedModeWindowOpen)
            {
                ImGui.SetNextWindowSize(new Vector2(500, 200), ImGuiCond.FirstUseEver);
                if (ImGui.Begin("Advanced Macro Editor", ref isAdvancedModeWindowOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize))
                {
                    ImGui.Text("Edit Design Macro Manually:");
                    ImGui.InputTextMultiline("##AdvancedDesignMacroPopup", ref advancedDesignMacroText, 2000, new Vector2(-1, -1), ImGuiInputTextFlags.AllowTabInput);
                }
                ImGui.End();
                if (!isAdvancedModeWindowOpen)
                    isAdvancedModeDesign = false;
            }
            ImGui.PopStyleVar(2);
        }
        private void DrawDesignRow(Character character, CharacterDesign design, bool isInsideFolder)
        {
            var style = ImGui.GetStyle();
            var io = ImGui.GetIO();

            // 1) Optional indent for folders
            if (isInsideFolder)
            {
                // 1a) Read ImGui's standard indent spacing
                float indentAmt = ImGui.GetStyle().IndentSpacing;
                // 1b) Actually apply it
                ImGui.Indent(indentAmt);
            }

            ImGui.PushID(design.Name);

            // 2) Reserve the row
            var rowMin = ImGui.GetCursorScreenPos();
            float rowW = ImGui.GetContentRegionAvail().X;
            float rowH = ImGui.GetTextLineHeightWithSpacing() + style.FramePadding.Y * 2;
            ImGui.Dummy(new Vector2(rowW, rowH));
            var rowMax = rowMin + new Vector2(rowW, rowH);

            // 3) Detect hover (we’ll need it for handles, stars, name, buttons…)
            bool hovered = ImGui.IsMouseHoveringRect(rowMin, rowMax, true);

            // 4) Common metrics
            float pad = style.FramePadding.X;
            float spacing = style.ItemSpacing.X;
            float btnW = rowH;
            var btnSz = new Vector2(btnW, btnW);

            // handle is 65% tall
            float hs = btnW * 0.65f;
            var handleSz = new Vector2(hs, hs);

            // running X
            float x = rowMin.X + pad;

            // ── HANDLE ──
            if (hovered)
            {
                // compute a 65%-height, half-as-wide bar
                float barHeight = rowH * 0.65f;
                float barWidth = barHeight * 0.5f;
                var barSize = new Vector2(barWidth, barHeight);
                // vertically center it
                float yOff = (rowH - barHeight) * 0.5f;

                ImGui.SetCursorScreenPos(new Vector2(x + pad, rowMin.Y + yOff));

                // use the character’s nameplate colour instead of a fixed blue
                var handleColor = new Vector4(
                    character.NameplateColor.X,
                    character.NameplateColor.Y,
                    character.NameplateColor.Z,
                    1.0f
                );

                ImGui.PushStyleColor(ImGuiCol.Button, handleColor);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, handleColor);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, handleColor);

                ImGui.Button($"##handle_{design.Name}", barSize);
                ImGui.PopStyleColor(3);

                // only start drag if they click+drag that thin bar
                if (ImGui.IsItemActive()
                    && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 4f)
                    && ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceAllowNullID))
                {
                    draggedDesign = design;
                    ImGui.SetDragDropPayload("DESIGN_MOVE", IntPtr.Zero, 0);
                    ImGui.TextUnformatted($"Moving: {design.Name}");
                    ImGui.EndDragDropSource();
                }

                // advance X by the _actual_ bar width
                x += barWidth + spacing;
            }

            // ── STAR ──
            if (hovered)
            {
                ImGui.SetCursorScreenPos(new Vector2(x, rowMin.Y));
                string star = design.IsFavorite ? "★" : "☆";
                if (ImGui.Button(star, btnSz))
                {
                    design.IsFavorite = !design.IsFavorite;
                    plugin.SaveConfiguration();
                    SortDesigns(character);
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(design.IsFavorite ? "Unfavorite" : "Mark as Favorite");
                x += btnW + spacing;
            }

            // ── NAME ──
            // reserve 3 icons + spacing + padding on the right
            float rightZone = 3 * btnW + 2 * spacing + pad;
            float availW = rowW - (x - rowMin.X) - rightZone;
            ImGui.SetCursorScreenPos(new Vector2(x, rowMin.Y + style.FramePadding.Y));
            var name = design.Name;
            if (ImGui.CalcTextSize(name).X > availW)
                name = TruncateWithEllipsis(name, availW);
            ImGui.TextUnformatted(name);

            // ── MANUAL DROP TARGET ──
            // if we’re mid-drag, they just let go, and the mouse is over this row,
            // reorder it here without using BeginDragDropTarget:
            if (draggedDesign != null
             && ImGui.IsMouseReleased(ImGuiMouseButton.Left)
             && ImGui.IsMouseHoveringRect(rowMin, rowMax, true)
             && draggedDesign != design)
            {
                var list = character.Designs;
                list.Remove(draggedDesign);
                int idx = list.IndexOf(design);
                draggedDesign.FolderId = design.FolderId;
                list.Insert(idx, draggedDesign);
                draggedDesign = null;
                plugin.SaveConfiguration();
                plugin.RefreshTreeItems(character);
            }

            // ── BLUE OUTLINE WHILE DRAGGING OVER ──
            if (draggedDesign != null && hovered)
            {
                var dl = ImGui.GetWindowDrawList();
                uint col = ImGui.GetColorU32(new Vector4(0.27f, 0.53f, 0.90f, 1f));
                dl.AddRect(rowMin, rowMax, col, 0, ImDrawFlags.None, 2);
            }

            // ── RIGHT-SIDE ICONS ──
            if (hovered)
            {
                float bx = rowMin.X + rowW - pad - btnW;
                ImGui.SetCursorScreenPos(new Vector2(bx - 2 * (btnW + spacing), rowMin.Y));

                ImGui.PushFont(UiBuilder.IconFont);
                if (ImGui.Button("\uf00c", btnSz))
                    plugin.ExecuteMacro(design.Macro, character, design.Name);
                ImGui.PopFont();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Apply Design");

                ImGui.SameLine(0, spacing);
                ImGui.PushFont(UiBuilder.IconFont);
                if (ImGui.Button("\uf044", btnSz))
                    OpenEditDesignWindow(character, design);
                ImGui.PopFont();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Edit Design");

                ImGui.SameLine(0, spacing);
                ImGui.PushFont(UiBuilder.IconFont);
                if (ImGui.Button("\uf2ed", btnSz) && io.KeyCtrl && io.KeyShift)
                {
                    character.Designs.Remove(design);
                    plugin.SaveConfiguration();
                }
                ImGui.PopFont();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold Ctrl+Shift to delete");
            }

            // ── CLEANUP ──
            ImGui.PopID();
            if (isInsideFolder) ImGui.Unindent();
            ImGui.SetCursorScreenPos(new Vector2(rowMin.X, rowMin.Y + rowH));
            ImGui.Separator();
        }



        private void AddNewDesign()
        {
            isNewDesign = true; // Track that it is a new design
            isEditDesignWindowOpen = true; // Ensure the edit design form is opened properly
            editedDesignName = ""; // Reset for new design
            editedGlamourerDesign = ""; // Reset for new design
            editedDesignMacro = ""; // Clear macro for new design
            isAdvancedModeDesign = false; // Ensure Advanced Mode starts OFF
            editedAutomation = ""; // Reset for new automation
            editedCustomizeProfile = ""; // Reset for new Customize+ Profile
        }

        private void OpenEditDesignWindow(Character character, CharacterDesign design)
        {
            isNewDesign = false;
            isEditDesignWindowOpen = true;
            originalDesignName = design.Name;
            editedDesignName = design.Name;
            editedDesignMacro = design.IsAdvancedMode ? design.AdvancedMacro ?? "" : design.Macro ?? "";
            editedGlamourerDesign = !string.IsNullOrWhiteSpace(design.GlamourerDesign)
                ? design.GlamourerDesign
                : ExtractGlamourerDesignFromMacro(design.Macro ?? "");

            editedAutomation = design.Automation ?? "";
            editedCustomizeProfile = design.CustomizePlusProfile ?? "";
            isAdvancedModeDesign = design.IsAdvancedMode;
            isAdvancedModeWindowOpen = design.IsAdvancedMode;
            advancedDesignMacroText = design.AdvancedMacro ?? "";
        }

        private void SaveDesign(Character character)
        {
            if (string.IsNullOrWhiteSpace(editedDesignName) || string.IsNullOrWhiteSpace(editedGlamourerDesign))
                return;

            // If we're editing, try to find the existing design by name
            var existingDesign = !isNewDesign
                ? character.Designs.FirstOrDefault(d => d.Name == originalDesignName)
                : null;

            if (existingDesign != null)
            {
                // Update existing design
                existingDesign.Name = editedDesignName;

                bool wasPreviouslyAdvanced = existingDesign.IsAdvancedMode;
                bool keepAdvanced = wasPreviouslyAdvanced && !isAdvancedModeDesign;

                existingDesign.Macro = keepAdvanced
                    ? existingDesign.AdvancedMacro
                    : (isAdvancedModeDesign ? advancedDesignMacroText : GenerateDesignMacro(plugin.Characters[activeDesignCharacterIndex]));

                existingDesign.AdvancedMacro = isAdvancedModeDesign || keepAdvanced
                    ? advancedDesignMacroText
                    : "";

                existingDesign.IsAdvancedMode = isAdvancedModeDesign || keepAdvanced;
                existingDesign.Automation = editedAutomation;
                existingDesign.GlamourerDesign = editedGlamourerDesign;
                existingDesign.CustomizePlusProfile = editedCustomizeProfile;
            }
            else
            {
                // If no match, add new design
                character.Designs.Add(new CharacterDesign(
                    editedDesignName,
                    isAdvancedModeDesign ? advancedDesignMacroText : GenerateDesignMacro(plugin.Characters[activeDesignCharacterIndex]),
                    isAdvancedModeDesign,
                    isAdvancedModeDesign ? advancedDesignMacroText : "",
                    editedGlamourerDesign,
                    editedAutomation,
                    editedCustomizeProfile
                )
                {
                    DateAdded = DateTime.UtcNow
                });
            }

            plugin.SaveConfiguration();
            isEditDesignWindowOpen = false;
            isAdvancedModeWindowOpen = false;
            isNewDesign = false;
        }



        private string GenerateDesignMacro(Character character)
        {
            if (string.IsNullOrWhiteSpace(editedGlamourerDesign))
                return "";

            string macro = $"/glamour apply {editedGlamourerDesign} | self";

            // Conditionally include automation line
            if (plugin.Configuration.EnableAutomations)
            {
                string automationToUse =
                    !string.IsNullOrWhiteSpace(editedAutomation)
                        ? editedAutomation
                        : (!string.IsNullOrWhiteSpace(character.CharacterAutomation)
                            ? character.CharacterAutomation
                            : "None");

                macro += $"\n/glamour automation enable {automationToUse}";
            }

            // Always disable Customize+ first
            macro += "\n/customize profile disable <me>";

            // Determine Customize+ profile
            string customizeProfileToUse = !string.IsNullOrWhiteSpace(editedCustomizeProfile)
                ? editedCustomizeProfile
                : !string.IsNullOrWhiteSpace(character.CustomizeProfile)
                    ? character.CustomizeProfile
                    : string.Empty;

            // Enable only if needed
            if (!string.IsNullOrWhiteSpace(customizeProfileToUse))
                macro += $"\n/customize profile enable <me>, {customizeProfileToUse}";

            // Redraw line
            macro += "\n/penumbra redraw self";

            return macro;
        }
        private string GenerateSecretDesignMacro(Character character)
        {
            // 1) Which Penumbra collection to target (taken from the character)
            var collection = character.PenumbraCollection;

            // 2) What the form is currently set to
            var design = editedGlamourerDesign;
            var custom = !string.IsNullOrWhiteSpace(editedCustomizeProfile)
                             ? editedCustomizeProfile
                             : character.CustomizeProfile;

            var sb = new System.Text.StringBuilder();

            // 3) Bulk-tag lines
            sb.AppendLine($"/penumbra bulktag disable {collection} | gear");
            sb.AppendLine($"/penumbra bulktag disable {collection} | hair");
            sb.AppendLine($"/penumbra bulktag enable  {collection} | {design}");

            // 4) Glamourer “no clothes” + design
            sb.AppendLine("/glamour apply no clothes | self");
            sb.AppendLine($"/glamour apply {design} | self");

            // 5) Customize+
            sb.AppendLine("/customize profile disable <me>");
            if (!string.IsNullOrWhiteSpace(custom))
                sb.AppendLine($"/customize profile enable <me>, {custom}");

            // 6) Final redraw
            sb.Append("/penumbra redraw self");

            return sb.ToString();
        }


        private void OpenEditCharacterWindow(int index)
        {
            if (index < 0 || index >= plugin.Characters.Count)
                return;

            selectedCharacterIndex = index;
            var character = plugin.Characters[index];

            string pluginDirectory = plugin.PluginDirectory;
            string defaultImagePath = Path.Combine(pluginDirectory, "Assets", "Default.png");

            editedCharacterName = character.Name;
            editedCharacterPenumbra = character.PenumbraCollection;
            editedCharacterGlamourer = character.GlamourerDesign;
            editedCharacterCustomize = character.CustomizeProfile ?? "";
            editedCharacterColor = character.NameplateColor;
            editedCharacterMacros = character.Macros;
            editedCharacterImagePath = !string.IsNullOrEmpty(character.ImagePath) ? character.ImagePath : defaultImagePath;
            editedCharacterTag = character.Tags != null && character.Tags.Count > 0
    ? string.Join(", ", character.Tags)
    : "";


            // Load Honorific Fields Properly
            editedCharacterHonorificTitle = character.HonorificTitle ?? "";
            editedCharacterHonorificPrefix = character.HonorificPrefix ?? "Prefix";
            editedCharacterHonorificSuffix = character.HonorificSuffix ?? "Suffix";
            editedCharacterHonorificColor = character.HonorificColor;
            editedCharacterHonorificGlow = character.HonorificGlow;
            editedCharacterMoodlePreset = character.MoodlePreset ?? "";

            // Check if MoodlePreset exists in older profiles
            string safeAutomation = character.CharacterAutomation == "None" ? "" : character.CharacterAutomation ?? "";

            editedCharacterAutomation = safeAutomation;

            character.IdlePoseIndex = plugin.Characters[selectedCharacterIndex].IdlePoseIndex;


            tempHonorificTitle = editedCharacterHonorificTitle;
            tempHonorificPrefix = editedCharacterHonorificPrefix;
            tempHonorificSuffix = editedCharacterHonorificSuffix;
            tempHonorificColor = editedCharacterHonorificColor;
            tempHonorificGlow = editedCharacterHonorificGlow;
            tempMoodlePreset = editedCharacterMoodlePreset;
            tempCharacterAutomation = safeAutomation;


            if (isAdvancedModeCharacter)
            {
                advancedCharacterMacroText = !string.IsNullOrWhiteSpace(character.Macros)
                    ? character.Macros
                    : GenerateMacro();
            }

            isEditCharacterWindowOpen = true;
        }

        private void SaveEditedCharacter()
        {
            if (selectedCharacterIndex < 0 || selectedCharacterIndex >= plugin.Characters.Count)
                return;

            var character = plugin.Characters[selectedCharacterIndex];

            character.Name = editedCharacterName;
            character.Tags = string.IsNullOrWhiteSpace(editedCharacterTag)
    ? new List<string>()
    : editedCharacterTag.Split(',').Select(f => f.Trim()).ToList();
            character.PenumbraCollection = editedCharacterPenumbra;
            character.GlamourerDesign = editedCharacterGlamourer;
            character.CustomizeProfile = editedCharacterCustomize;
            character.NameplateColor = editedCharacterColor;

            // Save Honorific Fields
            character.HonorificTitle = editedCharacterHonorificTitle;
            character.HonorificPrefix = editedCharacterHonorificPrefix;
            character.HonorificSuffix = editedCharacterHonorificSuffix;
            character.HonorificColor = editedCharacterHonorificColor;
            character.HonorificGlow = editedCharacterHonorificGlow;
            character.MoodlePreset = editedCharacterMoodlePreset;

            // Save Character Automation
            character.CharacterAutomation = editedCharacterAutomation; // Save the edited automation value

            // Ensure MoodlePreset is saved even if previously missing
            character.MoodlePreset = string.IsNullOrWhiteSpace(editedCharacterMoodlePreset) ? "" : editedCharacterMoodlePreset;


            // Ensure Macro Updates Correctly
            character.Macros = isAdvancedModeCharacter ? advancedCharacterMacroText : GenerateMacro();

            if (!string.IsNullOrEmpty(editedCharacterImagePath))
            {
                character.ImagePath = editedCharacterImagePath;
            }

            plugin.SaveConfiguration();
            isEditCharacterWindowOpen = false;
        }
        private void DrawImportDesignWindow()
        {
            if (!isImportWindowOpen || targetForDesignImport == null)
                return;

            ImGui.SetNextWindowSize(new Vector2(600, 500), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Import Designs", ref isImportWindowOpen, ImGuiWindowFlags.NoCollapse))
            {
                var grouped = plugin.Characters
                    .Where(c => c != targetForDesignImport && c.Designs.Count > 0)
                    .OrderBy(c => c.Name)
                    .ToList();

                foreach (var character in grouped)
                {
                    float barWidth = 6f;
                    float barHeight = ImGui.GetTextLineHeight();
                    var color = new Vector4(character.NameplateColor, 1.0f);

                    // Horizontal layout: bar + header
                    ImGui.BeginGroup();

                    // Left-coloured bar
                    ImGui.PushStyleColor(ImGuiCol.ChildBg, color);
                    ImGui.BeginChild($"##ColorAccent_{character.Name}", new Vector2(barWidth, barHeight), false);
                    ImGui.EndChild();
                    ImGui.PopStyleColor();

                    ImGui.SameLine();

                    // CollapsingHeader without extra flags = starts collapsed
                    if (ImGui.CollapsingHeader(character.Name))
                    {
                        ImGui.Indent();

                        foreach (var design in character.Designs)
                        {
                            ImGui.TextUnformatted($"• {design.Name}");
                            ImGui.SameLine();

                            if (ImGui.Button($"+##Import_{character.Name}_{design.Name}"))
                            {
                                var clone = new CharacterDesign(
                                    name: design.Name + " (Copy)",
                                    macro: design.Macro,
                                    isAdvancedMode: design.IsAdvancedMode,
                                    advancedMacro: design.AdvancedMacro,
                                    glamourerDesign: design.GlamourerDesign ?? "",
                                    automation: design.Automation ?? "",
                                    customizePlusProfile: design.CustomizePlusProfile ?? ""
                                );

                                targetForDesignImport.Designs.Add(clone);
                                plugin.SaveConfiguration();
                            }

                            if (ImGui.IsItemHovered())
                            {
                                ImGui.BeginTooltip();
                                ImGui.Text("Click to import this design");
                                ImGui.EndTooltip();
                            }
                        }
                        ImGui.Unindent();
                        ImGui.Spacing();
                    }
                    ImGui.EndGroup();
                    ImGui.Spacing();
                }
            }
            ImGui.End();
        }
        private string PatchMacroLine(string existing, string prefix, string replacement)
        {
            var lines = existing.Split('\n').ToList();
            var idx = lines.FindIndex(l => l.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) lines[idx] = replacement;
            else lines.Insert(0, replacement);
            return string.Join("\n", lines);
        }

        // Patches *all* lines matching `prefix`
        private string PatchAllMacroLines(string existing, string prefix, string replacement)
        {
            var lines = existing.Split('\n')
                .Select(l => l.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? replacement
                    : l
                );
            return string.Join("\n", lines);
        }
        private string UpdateCollectionInLines(string existing, string prefix, string newCollection)
        {
            var lines = existing.Split('\n').Select(line =>
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    // split after prefix
                    var rest = trimmed.Substring(prefix.Length).TrimStart();
                    // rest now starts with e.g. "COLLECTION | gear"
                    // so remove old collection token
                    var afterCollection = rest.IndexOf('|') >= 0
                        ? rest.Substring(rest.IndexOf('|'))  // includes the '|' and everything after
                        : rest.Substring(rest.IndexOf(' ')); // fallback
                                                             // build new line
                    return $"{prefix} {newCollection} {afterCollection}";
                }
                return line;
            });
            return string.Join("\n", lines);
        }
        private string UpdateBulkTagEnableDesignInMacro(string existing, string newCollection, string newDesign)
        {
            var lines = existing
                .Split('\n')
                .Select(line =>
                {
                    var t = line.TrimStart();
                    if (t.StartsWith("/penumbra bulktag enable", StringComparison.OrdinalIgnoreCase))
                        return $"/penumbra bulktag enable {newCollection} | {newDesign}";
                    return line;
                });
            return string.Join("\n", lines);
        }

        private unsafe void DrawReorderWindow()
        {
            if (!isReorderWindowOpen)
                return;

            ImGui.SetNextWindowSize(new Vector2(500, 600), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Reorder Characters", ref isReorderWindowOpen, ImGuiWindowFlags.NoCollapse))
            {
                ImGui.BeginChild("CharacterReorderScroll", new Vector2(0, -45), true); // scrollable list area

                for (int i = 0; i < reorderBuffer.Count; i++)
                {
                    var character = reorderBuffer[i];
                    ImGui.PushID(i);

                    float iconSize = 36;
                    if (!string.IsNullOrEmpty(character.ImagePath) && File.Exists(character.ImagePath))
                    {
                        var texture = Plugin.TextureProvider.GetFromFile(character.ImagePath).GetWrapOrDefault();
                        if (texture != null)
                        {
                            ImGui.Image(texture.ImGuiHandle, new Vector2(iconSize, iconSize));
                            ImGui.SameLine();
                        }
                    }

                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 6));
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 4); // Optional tweak to center text better
                    ImGui.Selectable(character.Name, false, ImGuiSelectableFlags.AllowDoubleClick);
                    ImGui.PopStyleVar();


                    // Drag Source
                    if (ImGui.BeginDragDropSource())
                    {
                        int dragIndex = i;
                        ImGui.SetDragDropPayload("CHARACTER_REORDER", new nint(Unsafe.AsPointer(ref dragIndex)), (uint)sizeof(int));
                        ImGui.Text($"Moving: {character.Name}");
                        ImGui.EndDragDropSource();
                    }

                    // Drop Target
                    if (ImGui.BeginDragDropTarget())
                    {
                        var payload = ImGui.AcceptDragDropPayload("CHARACTER_REORDER");
                        if (payload.NativePtr != null)
                        {
                            int dragIndex = *(int*)payload.Data;
                            if (dragIndex >= 0 && dragIndex < reorderBuffer.Count && dragIndex != i)
                            {
                                var item = reorderBuffer[dragIndex];
                                reorderBuffer.RemoveAt(dragIndex);
                                reorderBuffer.Insert(i, item);
                            }
                        }
                        ImGui.EndDragDropTarget();
                    }

                    ImGui.PopID();
                }

                ImGui.EndChild(); // end scrollable region

                // Buttons fixed at the bottom
                float buttonWidth = 120;
                float spacing = 20;
                float totalWidth = (buttonWidth * 2) + spacing;
                float centerX = (ImGui.GetWindowContentRegionMax().X - totalWidth) / 2f;

                ImGui.SetCursorPosX(centerX);

                if (ImGui.Button("Save Order", new Vector2(buttonWidth, 0)))
                {
                    for (int i = 0; i < reorderBuffer.Count; i++)
                        reorderBuffer[i].SortOrder = i;

                    plugin.Characters.Clear();
                    plugin.Characters.AddRange(reorderBuffer);

                    currentSort = SortType.Manual;
                    plugin.Configuration.CurrentSortIndex = (int)currentSort;
                    plugin.SaveConfiguration();
                    SortCharacters();
                    isReorderWindowOpen = false;
                }
                ImGui.End();
            }
        }
    }
}

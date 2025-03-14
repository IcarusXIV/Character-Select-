using System;
using System.Numerics;
using Dalamud.Interface.Utility;
using System.Threading.Tasks;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using CharacterSelectPlugin;
using System.Windows.Forms;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Dalamud.Interface;
using Dalamud.Plugin.Services;

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
        private string? pendingImagePath = null; // ✅ Temporary storage for the selected image path
        private Vector3 editedCharacterColor = new Vector3(1.0f, 1.0f, 1.0f); // ✅ Default to white
        private string editedCharacterPenumbra = "";
        private string editedCharacterGlamourer = "";
        private string editedCharacterCustomize = "";
        private string editedCharacterHonorific = "";
        private bool isAdvancedModeCharacter = false; // Separate Advanced Mode for Characters
        private bool isAdvancedModeDesign = false;    // Separate Advanced Mode for Designs
        private string advancedCharacterMacroText = ""; // Macro text for Character Advanced Mode
        private string advancedDesignMacroText = "";    // Macro text for Design Advanced Mode
        private bool isEditDesignWindowOpen = false;
        private string editedDesignName = "";
        private string editedDesignMacro = "";
        private string editedGlamourerDesign = "";
        private HashSet<string> knownHonorifics = new HashSet<string>();

        // 🔹 Add Sorting Function
        private enum SortType { Favorites, Alphabetical, Recent, Oldest }
        private SortType currentSort;

        private void SortCharacters()
        {
            if (currentSort == SortType.Favorites)
            {
                plugin.Characters.Sort((a, b) =>
                {
                    int favCompare = b.IsFavorite.CompareTo(a.IsFavorite); // ⭐ Favorites first
                    if (favCompare != 0) return favCompare;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); // 🔠 Alphabetical within favorites
                });
            }
            else if (currentSort == SortType.Alphabetical)
            {
                plugin.Characters.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)); // 🔠 Alphabetical
            }
            else if (currentSort == SortType.Recent)
            {
                plugin.Characters.Sort((a, b) => b.DateAdded.CompareTo(a.DateAdded)); // 🔄 Most Recent First
            }
            else if (currentSort == SortType.Oldest)
            {
                plugin.Characters.Sort((a, b) => a.DateAdded.CompareTo(b.DateAdded)); // ⏳ Oldest First
            }
        }
        public MainWindow(Plugin plugin)
    : base("Character Select+", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(600, 700),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };

            this.plugin = plugin;

            // ✅ Load saved sorting preference
            currentSort = (SortType)plugin.Configuration.CurrentSortIndex;
            SortCharacters(); // ✅ Apply sorting on startup
                              // 🔹 Gather all existing honorifics at startup
            foreach (var character in plugin.Characters)
            {
                if (!string.IsNullOrWhiteSpace(character.Honorific))
                {
                    knownHonorifics.Add(character.Honorific);
                }
            }

        }


        public void Dispose() { }

        public override void Draw()
        {
            ImGui.Text("Choose your character");
            ImGui.Separator();

            if (ImGui.Button("Add Character"))
            {
                var tempSavedDesigns = new List<CharacterDesign>(plugin.NewCharacterDesigns); // ✅ Store existing designs
                ResetCharacterFields(); // ✅ Resets fields before opening window
                plugin.NewCharacterDesigns = tempSavedDesigns; // ✅ Restore designs after reset

                plugin.OpenAddCharacterWindow();
                isEditCharacterWindowOpen = false;
                isDesignPanelOpen = false;
                isAdvancedModeCharacter = false; // ✅ Force Advanced Mode to be off
            }

            if (plugin.IsAddCharacterWindowOpen || isEditCharacterWindowOpen)
            {
                DrawCharacterForm();
            }

            ImGui.BeginChild("CharacterGrid", new Vector2(isDesignPanelOpen ? -250 : 0, -30), true);
            DrawCharacterGrid();
            ImGui.EndChild(); // ✅ Close Character Grid Properly

            if (isDesignPanelOpen)
            {
                ImGui.SameLine();
                float characterGridHeight = ImGui.GetItemRectSize().Y; // Get height of the Character Grid
                ImGui.SetNextWindowSizeConstraints(new Vector2(250, characterGridHeight), new Vector2(250, characterGridHeight));
                ImGui.BeginChild("DesignPanel", new Vector2(250, characterGridHeight), true);
                DrawDesignPanel();
                ImGui.EndChild();
            }

            // 🔹 Ensure proper bottom-left alignment
            ImGui.SetCursorPos(new Vector2(10, ImGui.GetWindowHeight() - 30));

            // 🔹 Use a gear icon for better UI clarity
            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button("\uf013")) // ⚙ Gear icon with NO extra text
            {
                plugin.IsSettingsOpen = !plugin.IsSettingsOpen;
            }
            ImGui.PopFont();
            if (plugin.IsSettingsOpen)
            {
                ImGui.SetNextWindowSize(new Vector2(300, 180), ImGuiCond.FirstUseEver); // ✅ Adjusted for new setting

                bool isSettingsOpen = plugin.IsSettingsOpen;
                if (ImGui.Begin("Settings", ref isSettingsOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize))
                {
                    if (!isSettingsOpen)
                        plugin.IsSettingsOpen = false;

                    ImGui.Text("Settings Panel");
                    ImGui.Separator();

                    // 🔹 Profile Image Scale
                    float tempScale = plugin.ProfileImageScale;
                    if (ImGui.SliderFloat("Profile Image Scale", ref tempScale, 0.5f, 2.0f, "%.1f"))
                    {
                        plugin.ProfileImageScale = tempScale;
                        plugin.SaveConfiguration();
                    }

                    // 🔹 Profile Columns
                    int tempColumns = plugin.ProfileColumns;
                    if (ImGui.InputInt("Profiles Per Row", ref tempColumns, 1, 1))
                    {
                        tempColumns = Math.Clamp(tempColumns, 1, 6);
                        plugin.ProfileColumns = tempColumns;
                        plugin.SaveConfiguration();
                    }

                    // 🔹 Profile Spacing - Match the layout of Profile Image Scale
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
                    // 🔹 Position "Sort By" Dropdown in the Bottom-Right
                    ImGui.SetCursorPos(new Vector2(ImGui.GetWindowWidth() - 150, ImGui.GetWindowHeight() - 35)); // ✅ Adjust position

                    ImGui.Text("Sort By:");
                    ImGui.SameLine();

                    // Create the dropdown menu
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
                        ImGui.EndCombo(); // ✅ Close dropdown properly
                    }




                    ImGui.End();
                }
            }
            // 🔹 Position the Support Button near the bottom-right corner
            float buttonWidth = 110;
            float buttonHeight = 25;
            float padding = 10;

            // Set button position near the bottom-right
            ImGui.SetCursorPos(new Vector2(
                ImGui.GetWindowWidth() - buttonWidth - padding,  // Align to right
                ImGui.GetWindowHeight() - buttonHeight - padding // Align to bottom
            ));

            // 🔹 Create the Support Button
            if (ImGui.Button("💙 Support Dev", new Vector2(buttonWidth, buttonHeight)))
            {
                Dalamud.Utility.Util.OpenLink("https://ko-fi.com/icarusxiv");
            }

            // Tooltip on hover
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Enjoy Character Select+? Consider supporting development!");

        }



        // Resets input fields for a new character
        private void ResetCharacterFields()
        {
            plugin.NewCharacterName = "";
            plugin.NewCharacterColor = new Vector3(1.0f, 1.0f, 1.0f); // Reset to white
            plugin.NewPenumbraCollection = "";
            plugin.NewGlamourerDesign = "";
            plugin.NewCustomizeProfile = "";
            plugin.NewHonorific = "";
            plugin.NewCharacterImagePath = null;
            plugin.NewCharacterDesigns.Clear();

            // ✅ Fix: Preserve Advanced Mode Macro when Resetting Fields
            if (!isAdvancedModeCharacter)
            {
                plugin.NewCharacterMacros = GenerateMacro(); // ✅ Only reset macro in Normal Mode
            }
            // ✅ Do NOT touch plugin.NewCharacterMacros if Advanced Mode is active


        }



        private void DrawCharacterForm()
        {
            string tempName = isEditCharacterWindowOpen ? editedCharacterName : plugin.NewCharacterName;
            string tempMacros = isEditCharacterWindowOpen ? editedCharacterMacros : plugin.NewCharacterMacros;
            string? imagePath = isEditCharacterWindowOpen ? editedCharacterImagePath : plugin.NewCharacterImagePath;
            string tempPenumbra = isEditCharacterWindowOpen ? editedCharacterPenumbra : plugin.NewPenumbraCollection;
            string tempGlamourer = isEditCharacterWindowOpen ? editedCharacterGlamourer : plugin.NewGlamourerDesign;
            string tempCustomize = isEditCharacterWindowOpen ? editedCharacterCustomize : plugin.NewCustomizeProfile;
            string tempHonorific = isEditCharacterWindowOpen ? editedCharacterHonorific : plugin.NewHonorific;
            Vector3 tempColor = isEditCharacterWindowOpen ? editedCharacterColor : plugin.NewCharacterColor;



            float labelWidth = 130; // Keep labels aligned
            float inputWidth = 250; // Longer input bars
            float inputOffset = 10; // Moves input fields slightly right

            // Character Name
            ImGui.SetCursorPosX(10);
            ImGui.Text("Character Name*");
            ImGui.SameLine(labelWidth);
            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.InputText("##CharacterName", ref tempName, 50);
            if (isEditCharacterWindowOpen) editedCharacterName = tempName;
            else plugin.NewCharacterName = tempName;

            // ℹ Tooltip Icon
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();
            if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Enter your OC's name or nickname for profile here."); }

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

            // ℹ Tooltip Icon
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
            // ✅ Fix: Preserve Advanced Mode Edits While Allowing Normal Mode Updates
            if (isEditCharacterWindowOpen)
            {
                if (editedCharacterPenumbra != tempPenumbra)
                {
                    editedCharacterPenumbra = tempPenumbra;

                    if (isAdvancedModeCharacter && !string.IsNullOrWhiteSpace(advancedCharacterMacroText))
                    {
                        // ✅ Only update if Advanced Mode was already in use
                        advancedCharacterMacroText = GenerateMacro();
                    }
                }
            }
            else
            {
                if (plugin.NewPenumbraCollection != tempPenumbra)
                {
                    plugin.NewPenumbraCollection = tempPenumbra;

                    if (isAdvancedModeCharacter && !string.IsNullOrWhiteSpace(advancedCharacterMacroText))
                    {
                        // ✅ Preserve Advanced Mode macro when adding new characters
                        plugin.NewCharacterMacros = advancedCharacterMacroText;
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
            if (isEditCharacterWindowOpen)
            {
                if (editedCharacterGlamourer != tempGlamourer)
                {
                    editedCharacterGlamourer = tempGlamourer;

                    if (isAdvancedModeCharacter && !string.IsNullOrWhiteSpace(advancedCharacterMacroText))
                    {
                        advancedCharacterMacroText = GenerateMacro();
                    }
                }
            }
            else
            {
                if (plugin.NewGlamourerDesign != tempGlamourer)
                {
                    plugin.NewGlamourerDesign = tempGlamourer;

                    if (isAdvancedModeCharacter && !string.IsNullOrWhiteSpace(advancedCharacterMacroText))
                    {
                        plugin.NewCharacterMacros = advancedCharacterMacroText;
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


            // Customize+ Profile
            ImGui.SetCursorPosX(10);
            ImGui.Text("Customize+ Profile");
            ImGui.SameLine(labelWidth);
            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.InputText("##CustomizeProfile", ref tempCustomize, 50);
            if (isEditCharacterWindowOpen)
            {
                if (editedCharacterCustomize != tempCustomize)
                {
                    editedCharacterCustomize = tempCustomize;

                    if (isAdvancedModeCharacter && !string.IsNullOrWhiteSpace(advancedCharacterMacroText))
                    {
                        advancedCharacterMacroText = GenerateMacro();
                    }
                }
            }
            else
            {
                if (plugin.NewCustomizeProfile != tempCustomize)
                {
                    plugin.NewCustomizeProfile = tempCustomize;

                    if (isAdvancedModeCharacter && !string.IsNullOrWhiteSpace(advancedCharacterMacroText))
                    {
                        plugin.NewCharacterMacros = advancedCharacterMacroText;
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
                ImGui.TextUnformatted("Enter the name of the Customize+ profile to apply to this character.");
                ImGui.TextUnformatted("Must be entered EXACTLY as it is named in Customize+!");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            ImGui.Separator();


            // Honorific
            ImGui.SetCursorPosX(10);
            ImGui.Text("Honorific");
            ImGui.SameLine(labelWidth);
            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.InputText("##Honorific", ref tempHonorific, 50);
            if (isEditCharacterWindowOpen)
            {
                if (editedCharacterHonorific != tempHonorific)
                {
                    editedCharacterHonorific = tempHonorific;

                    if (isAdvancedModeCharacter && !string.IsNullOrWhiteSpace(advancedCharacterMacroText))
                    {
                        advancedCharacterMacroText = GenerateMacro();
                    }
                }
            }
            else
            {
                if (plugin.NewHonorific != tempHonorific)
                {
                    plugin.NewHonorific = tempHonorific;

                    if (isAdvancedModeCharacter && !string.IsNullOrWhiteSpace(advancedCharacterMacroText))
                    {
                        plugin.NewCharacterMacros = advancedCharacterMacroText;
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
                ImGui.TextUnformatted("Enter the honorific to apply to this character.");
                ImGui.TextUnformatted("Must be entered EXACTLY as it is written in Honorific!");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            ImGui.Separator();

            if (isEditCharacterWindowOpen)
                editedCharacterMacros = tempMacros;
            else
// ✅ Ensure Advanced Mode changes are actually applied to new characters
if (isAdvancedModeCharacter)
            {
                if (!string.IsNullOrWhiteSpace(advancedCharacterMacroText))
                {
                    plugin.NewCharacterMacros = advancedCharacterMacroText; // ✅ Save changes properly
                }
            }
            else
            {
                plugin.NewCharacterMacros = GenerateMacro(); // ✅ Generate normal macro if not in Advanced Mode
            }

            // ✅ Uses Advanced Mode if enabled


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





            // ✅ Apply the image path safely on the next frame
            if (pendingImagePath != null)
            {
                lock (this) // ✅ Prevent potential race conditions
                {
                    if (isEditCharacterWindowOpen)
                        editedCharacterImagePath = pendingImagePath;
                    else
                        plugin.NewCharacterImagePath = pendingImagePath;

                    pendingImagePath = null; // Reset after applying
                }
            }

            // ✅ Get Plugin Directory and Default Image Path
            string pluginDirectory = plugin.PluginDirectory;
            string defaultImagePath = Path.Combine(pluginDirectory, "Assets", "Default.png");

            // ✅ Assign Default Image if None Selected
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
                    ImGui.Image(texture.ImGuiHandle, new Vector2(100, 100)); // Show image
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
                ImGui.InputTextMultiline($"##DesignMacro{i}", ref tempDesignMacro, 300, new Vector2(300, 100), ImGuiInputTextFlags.AllowTabInput);
                ImGui.EndChild();

                designsToDisplay[i] = new CharacterDesign(tempDesignName, tempDesignMacro);
            }

            // 🔹 Character Advanced Mode Toggle Button
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


            // ℹ Tooltip Icon (Info about Advanced Mode)
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 5); // Add slight spacing

            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                // ✅ Move tooltip to the **right side** of the button
                ImGui.SetNextWindowPos(ImGui.GetCursorScreenPos() + new Vector2(20, -5));

                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300);
                ImGui.TextUnformatted("⚠️ Do not touch this unless you know what you're doing.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }



            // 🔹 Show Advanced Mode Editor When Enabled
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
                    // ✅ Pass Advanced Macro when Saving a New Character
                    string finalMacro = isAdvancedModeCharacter ? advancedCharacterMacroText : plugin.NewCharacterMacros;
                    plugin.SaveNewCharacter(finalMacro);
                }

                isEditCharacterWindowOpen = false;
                plugin.CloseAddCharacterWindow();
            }



            if (!canSaveCharacter)
                ImGui.EndDisabled();

            ImGui.SameLine();

            if (ImGui.Button("Cancel"))
            {
                isEditCharacterWindowOpen = false;
                plugin.CloseAddCharacterWindow();
            }

        }

        private void DrawCharacterGrid()
        {
            // 🔹 Get spacing and column settings
            float profileSpacing = plugin.ProfileSpacing;
            int columnCount = plugin.ProfileColumns;

            // 🔹 Reduce the number of columns if the Design Panel is open
            if (isDesignPanelOpen)
            {
                columnCount = Math.Max(1, columnCount - 1);
            }

            // 🔹 Define column width, ensuring proper spacing
            float columnWidth = (250 * plugin.ProfileImageScale) + profileSpacing;
            ImGui.Columns(columnCount, "CharacterGrid", false);

            for (int i = 0; i < plugin.Characters.Count; i++)
            {
                var character = plugin.Characters[i];

                // 🔹 Ensure correct column width
                ImGui.SetColumnWidth(i % columnCount, columnWidth);

                // 🔹 Image Scaling
                float scale = plugin.ProfileImageScale;
                float maxSize = 250 * scale;
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

                        // ✅ Click Image to Execute Macro
                        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                        {
                            if (activeDesignCharacterIndex != -1)
                            {
                                activeDesignCharacterIndex = -1;
                                isDesignPanelOpen = false;
                            }
                            plugin.ExecuteMacro(character.Macros);
                        }
                    }
                }

                // ✅ Nameplate Rendering (Preserves Correct Positioning)
                DrawNameplate(character, maxSize, nameplateHeight);

                // 🔹 Buttons Section (Correct Spacing)
                float buttonWidth = maxSize / 3.1f;
                float btnWidth = maxSize / 3.2f;
                float btnHeight = 24;
                float btnSpacing = 4;

                float btnStartX = ImGui.GetCursorPosX() + (maxSize - (3 * btnWidth + 2 * btnSpacing)) / 2;
                ImGui.SetCursorPosX(btnStartX);

                // ✅ "Designs" Button
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

                // ✅ Tooltip Fix for Delete Button
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Hold Ctrl + Shift and click to delete.");
                    ImGui.EndTooltip();
                }

                ImGui.NextColumn(); // ✅ Moves to the next column properly
            }

            ImGui.Columns(1); // ✅ Ensure Columns Are Closed Properly
        }

        private void DrawNameplate(Character character, float width, float height)
        {
            var cursorPos = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();

            // 🔹 Nameplate Background
            drawList.AddRectFilled(
                new Vector2(cursorPos.X, cursorPos.Y),
                new Vector2(cursorPos.X + width, cursorPos.Y + height),
                ImGui.GetColorU32(new Vector4(0, 0, 0, 0.8f)) // ✅ Black background with slight transparency
            );

            // 🔹 Nameplate Color Strip
            drawList.AddRectFilled(
                new Vector2(cursorPos.X, cursorPos.Y + height - 4),
                new Vector2(cursorPos.X + width, cursorPos.Y + height),
                ImGui.GetColorU32(new Vector4(character.NameplateColor.X, character.NameplateColor.Y, character.NameplateColor.Z, 1.0f))
            );

            // 🔹 Character Name
            var textSize = ImGui.CalcTextSize(character.Name);
            var textPosX = cursorPos.X + (width - textSize.X) / 2;
            var textPosY = cursorPos.Y + (height - textSize.Y) / 2;

            drawList.AddText(new Vector2(textPosX, textPosY), ImGui.GetColorU32(ImGuiCol.Text), character.Name);

            // ⭐ Add Favorite Star in the Top-Left Corner
            string starSymbol = character.IsFavorite ? "★" : "☆";
            var starPos = new Vector2(cursorPos.X + 5, cursorPos.Y + 5); // Position near top-left
            drawList.AddText(starPos, ImGui.GetColorU32(ImGuiCol.Text), starSymbol);

            // 🔹 Clickable Area for Toggling Favorite
            if (ImGui.IsMouseHoveringRect(starPos, new Vector2(starPos.X + 20, starPos.Y + 20)) && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                character.IsFavorite = !character.IsFavorite; // Toggle favorite
                plugin.SaveConfiguration();
                SortCharacters(); // ✅ Resort after toggling
            }

            ImGui.Dummy(new Vector2(width, height)); // ✅ Maintain proper positioning
        }


        // Place GenerateMacro() here:
        private string GenerateMacro()
        {
            string penumbra = isEditCharacterWindowOpen ? editedCharacterPenumbra : plugin.NewPenumbraCollection;
            string glamourer = isEditCharacterWindowOpen ? editedCharacterGlamourer : plugin.NewGlamourerDesign;
            string customize = isEditCharacterWindowOpen ? editedCharacterCustomize : plugin.NewCustomizeProfile;
            string honorific = isEditCharacterWindowOpen ? editedCharacterHonorific : plugin.NewHonorific;

            if (string.IsNullOrWhiteSpace(penumbra) || string.IsNullOrWhiteSpace(glamourer))
                return "/penumbra redraw self"; // Prevents blank macro

            string macro = $"/penumbra collection individual | {penumbra} | self\n" +
                           $"/glamour apply {glamourer} | self\n";

            // 🔹 Always disable Customize+ before enabling a new profile
            macro += "/customize profile disable <me>\n";

            // 🔹 Add Customize+ profile if set
            if (!string.IsNullOrWhiteSpace(customize))
            {
                macro += $"/customize profile enable <me>, {customize}\n";
            }


            // 🔹 Disable ALL known honorifics first
            foreach (var known in knownHonorifics)
            {
                macro += $"/honorific title disable {known}\n";
            }

            // 🔹 Enable ONLY the selected Honorific
            if (!string.IsNullOrWhiteSpace(honorific))
            {
                macro += $"/honorific title enable {honorific}\n";
            }


            macro += "/penumbra redraw self";

            return macro;
        }

        // 🔹 Add ExtractGlamourerDesignFromMacro BELOW GenerateMacro()
        private string ExtractGlamourerDesignFromMacro(string macro)
        {
            // 🔹 Find the Glamourer line in the macro
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

        private void DrawDesignPanel()
        {
            if (activeDesignCharacterIndex < 0 || activeDesignCharacterIndex >= plugin.Characters.Count)
                return;

            var character = plugin.Characters[activeDesignCharacterIndex];

            // 🔹 Header with Add Button
            ImGui.Text($"Designs for {character.Name}");
            ImGui.SameLine();
            // 🔹 Plus Button (Orange)
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(new Vector4(0.27f, 1.07f, 0.27f, 1.0f))); // Green
            if (ImGui.Button("+##AddDesign"))
            {
                AddNewDesign();
            }
            ImGui.PopStyleColor();

            // 🔹 Move 'x' button to top-right corner
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - 20);
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(new Vector4(1.0f, 0.27f, 0.27f, 1.0f))); // Red
            if (ImGui.Button("x##CloseDesignPanel"))
            {
                activeDesignCharacterIndex = -1;
                isDesignPanelOpen = false;
            }
            ImGui.PopStyleColor();

            ImGui.Separator();

            // 🔹 1️⃣ RENDER THE FORM **FIRST** BEFORE THE LIST
            // 🔹 RENDER FORM FIRST
            if (isEditDesignWindowOpen)
            {
                ImGui.BeginChild("EditDesignForm", new Vector2(0, 320), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize);

                bool isNewDesign = string.IsNullOrEmpty(editedDesignName);
                ImGui.Text(isNewDesign ? "Add Design" : "Edit Design");

                float inputWidth = 200;
                ImGui.Text("Design Name");
                ImGui.SetCursorPosX(10);
                ImGui.SetNextItemWidth(inputWidth);
                ImGui.InputText("##DesignName", ref editedDesignName, 100);

                ImGui.Separator();

                ImGui.Text("Glamourer Design");
                ImGui.SetCursorPosX(10);
                ImGui.SetNextItemWidth(inputWidth);
                ImGui.InputText("##GlamourerDesign", ref editedGlamourerDesign, 100);

                ImGui.Separator();

                // 🔹 Advanced Mode Toggle
                if (ImGui.Button(isAdvancedModeDesign ? "Exit Advanced Mode" : "Advanced Mode"))
                {
                    isAdvancedModeDesign = !isAdvancedModeDesign;
                    if (isAdvancedModeDesign)
                    {
                        advancedDesignMacroText = !string.IsNullOrWhiteSpace(editedDesignMacro) ? editedDesignMacro : GenerateDesignMacro();
                    }
                }

                // ℹ Tooltip Icon
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

                // 🔹 Advanced Mode Input Box
                if (isAdvancedModeDesign)
                {
                    float totalHeight = ImGui.GetContentRegionAvail().Y - 55;
                    float minHeight = 110;
                    float maxHeight = 160;
                    float inputHeight = Math.Clamp(totalHeight, minHeight, maxHeight);

                    ImGui.BeginChild("AdvancedModeSection", new Vector2(0, inputHeight), true, ImGuiWindowFlags.NoScrollbar);
                    ImGui.Separator();
                    ImGui.Text("Edit Macro Manually:");
                    ImGui.InputTextMultiline("##AdvancedDesignMacro", ref advancedDesignMacroText, 2000, new Vector2(-1, inputHeight - 10), ImGuiInputTextFlags.AllowTabInput);
                    ImGui.EndChild();
                }

                // 🔹 Ensure separation from form elements
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 14);

                // 🔹 Align Buttons Properly
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 3));

                float buttonWidth = 85;
                float buttonHeight = 20;
                float buttonSpacing = 8;
                float totalButtonWidth = (buttonWidth * 2 + buttonSpacing);
                float buttonPosX = (ImGui.GetContentRegionAvail().X - totalButtonWidth) / 2;

                ImGui.SetCursorPosX(buttonPosX);

                bool canSave = !string.IsNullOrWhiteSpace(editedDesignName) && !string.IsNullOrWhiteSpace(editedGlamourerDesign);

                if (!canSave)
                    ImGui.BeginDisabled();

                if (ImGui.Button("Save Design", new Vector2(buttonWidth, buttonHeight)))
                {
                    SaveDesign(plugin.Characters[activeDesignCharacterIndex]);
                    isEditDesignWindowOpen = false;
                }

                if (!canSave)
                    ImGui.EndDisabled();

                ImGui.SameLine();

                if (ImGui.Button("Cancel", new Vector2(buttonWidth, buttonHeight)))
                {
                    isEditDesignWindowOpen = false;
                }

                // ✅ Restore default button style
                ImGui.PopStyleVar();
                ImGui.EndChild(); // ✅ END FORM
            }

            // 🔹 SEPARATE THE DESIGN LIST FROM THE FORM
            ImGui.Separator(); // ✅ Visually separate the list

            // 🔹 NOW RENDER THE DESIGN LIST
            ImGui.BeginChild("DesignListBackground", new Vector2(0, ImGui.GetContentRegionAvail().Y), true, ImGuiWindowFlags.NoScrollbar);

            foreach (var design in character.Designs)
            {
                float rowWidth = ImGui.GetContentRegionAvail().X;

                ImGui.Text(design.Name);
                ImGui.SameLine();
                ImGui.SetCursorPosX(rowWidth - 80);

                // 🔹 Apply Button ✅
                ImGui.PushFont(UiBuilder.IconFont);
                if (ImGui.Button("\uf00c" + $"##Apply{design.Name}"))
                {
                    plugin.ExecuteMacro(design.Macro);
                }
                ImGui.PopFont();

                ImGui.SameLine();

                // 🔹 Edit Icon ✏️
                ImGui.PushFont(UiBuilder.IconFont);
                if (ImGui.Button("\uf044" + $"##Edit{design.Name}"))
                {
                    OpenEditDesignWindow(character, design);
                }
                ImGui.PopFont();

                ImGui.SameLine();

                // 🔹 Delete Icon 🗑️
                ImGui.PushFont(UiBuilder.IconFont);
                if (ImGui.Button("\uf2ed" + $"##Delete{design.Name}")) // 🔹 Uses Design Name
                {
                    bool isCtrlShiftPressed = ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift;
                    if (isCtrlShiftPressed)
                    {
                        character.Designs.Remove(design);
                        plugin.SaveConfiguration();
                    }
                }
                ImGui.PopFont(); // ✅ Pop icon font before tooltip

                // ✅ Tooltip Fix
                if (ImGui.IsItemHovered())
                {
                    ImGui.PushFont(UiBuilder.DefaultFont); // ✅ Use default font
                    ImGui.BeginTooltip();
                    ImGui.Text("Hold Ctrl + Shift and click to delete.");
                    ImGui.EndTooltip();
                    ImGui.PopFont(); // ✅ Reset font
                }



                ImGui.Separator();
            }

            ImGui.EndChild(); // ✅ END DESIGN LIST



        }


        private void AddNewDesign()
        {
            isEditDesignWindowOpen = true; // ✅ Ensure the edit design form is opened properly
            editedDesignName = ""; // ✅ Reset for new design
            editedGlamourerDesign = ""; // ✅ Reset for new design
            editedDesignMacro = ""; // ✅ Clear macro for new design
            isAdvancedModeDesign = false; // ✅ Ensure Advanced Mode starts OFF
        }

        private void OpenEditDesignWindow(Character character, CharacterDesign design)
        {
            isEditDesignWindowOpen = true;
            editedDesignName = design.Name;

            // ✅ Load normal macro OR Advanced Macro, depending on mode
            editedDesignMacro = design.IsAdvancedMode ? design.AdvancedMacro : design.Macro;

            editedGlamourerDesign = ExtractGlamourerDesignFromMacro(design.Macro);
            isAdvancedModeDesign = design.IsAdvancedMode; // ✅ Restore Advanced Mode state
            advancedDesignMacroText = design.AdvancedMacro; // ✅ Restore stored Advanced Macro
        }


        private void SaveDesign(Character character)
        {
            if (string.IsNullOrWhiteSpace(editedDesignName) || string.IsNullOrWhiteSpace(editedGlamourerDesign))
                return; // Prevent saving if fields are empty

            // 🔹 Check if this design already exists
            var existingDesign = character.Designs.FirstOrDefault(d => d.Name == editedDesignName);

            if (existingDesign != null)
            {
                // ✅ Preserve Advanced Mode Macro
                existingDesign.IsAdvancedMode = isAdvancedModeDesign;
                existingDesign.Macro = isAdvancedModeDesign ? advancedDesignMacroText : GenerateDesignMacro();
                existingDesign.AdvancedMacro = isAdvancedModeDesign ? advancedDesignMacroText : ""; // ✅ Store separately
            }
            else
            {
                character.Designs.Add(new CharacterDesign(
                    editedDesignName,
                    isAdvancedModeDesign ? advancedDesignMacroText : GenerateDesignMacro(),
                    isAdvancedModeDesign, // ✅ Track if Advanced Mode was used
                    isAdvancedModeDesign ? advancedDesignMacroText : "" // ✅ Store separately
                ));
            }

            plugin.SaveConfiguration();
            isEditDesignWindowOpen = false;
        }



        private string GenerateDesignMacro()
        {
            if (string.IsNullOrWhiteSpace(editedGlamourerDesign))
                return "";

            return $"/glamour apply {editedGlamourerDesign} | self\n/penumbra redraw self";
        }


        private void OpenEditCharacterWindow(int index)
        {
            if (index < 0 || index >= plugin.Characters.Count)
                return;

            selectedCharacterIndex = index;
            var character = plugin.Characters[index];

            editedCharacterName = character.Name;
            editedCharacterPenumbra = character.PenumbraCollection;
            editedCharacterGlamourer = character.GlamourerDesign;
            editedCharacterCustomize = character.CustomizeProfile;
            editedCharacterHonorific = character.Honorific;
            editedCharacterColor = character.NameplateColor;
            editedCharacterMacros = character.Macros; // ✅ Correctly loads normal macros

            // ✅ Only assign Advanced Mode text if it's enabled
            if (isAdvancedModeCharacter)
            {
                advancedCharacterMacroText = !string.IsNullOrWhiteSpace(character.Macros)
                    ? character.Macros
                    : GenerateMacro(); // ✅ Use default macro if none is set
            }

            isEditCharacterWindowOpen = true;
            isAdvancedModeDesign = false;
        }


        private void SaveEditedCharacter()
        {
            if (selectedCharacterIndex < 0 || selectedCharacterIndex >= plugin.Characters.Count)
                return;

            var character = plugin.Characters[selectedCharacterIndex];

            character.Name = editedCharacterName;
            character.PenumbraCollection = editedCharacterPenumbra;
            character.GlamourerDesign = editedCharacterGlamourer;
            character.CustomizeProfile = editedCharacterCustomize;
            // 🔹 Store old honorific before updating
            string oldHonorific = character.Honorific;

            // 🔹 Update to the new honorific
            character.Honorific = editedCharacterHonorific;

            // 🔹 Check if an honorific was removed
            if (!string.IsNullOrWhiteSpace(oldHonorific) && string.IsNullOrWhiteSpace(editedCharacterHonorific))
            {
                // 🔹 See if ANY character is still using the removed honorific
                bool stillUsed = plugin.Characters.Any(c => c.Honorific == oldHonorific);

                if (!stillUsed)
                {
                    // 🔹 Remove from known honorifics if no other character uses it
                    knownHonorifics.Remove(oldHonorific);

                    // 🔹 Update all character macros to remove disable lines for this honorific
                    foreach (var c in plugin.Characters)
                    {
                        c.Macros = GenerateMacro();
                    }
                }
            }

            character.NameplateColor = editedCharacterColor;

            // ✅ Preserve Advanced Mode Text if it exists
            if (isAdvancedModeCharacter && !string.IsNullOrWhiteSpace(character.Macros))
            {
                character.Macros = advancedCharacterMacroText; // ✅ Save Advanced Mode changes ONLY if already in use
            }
            else if (!isAdvancedModeCharacter)
            {
                character.Macros = editedCharacterMacros; // ✅ Otherwise, save normal macro edits
            }

            // 🔹 FIX: Save selected image properly without removing core functions
            if (!string.IsNullOrEmpty(editedCharacterImagePath))
            {
                character.ImagePath = editedCharacterImagePath;
            }
            if (!string.IsNullOrWhiteSpace(editedCharacterHonorific))
            {
                knownHonorifics.Add(editedCharacterHonorific);
            }


            plugin.SaveConfiguration();
            isEditCharacterWindowOpen = false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Textures.TextureWraps;
using CharacterSelectPlugin.Windows.Styles;
using CharacterSelectPlugin.Managers;

namespace CharacterSelectPlugin.Windows
{
    public partial class SecretModeModWindow
    {
        private void DrawClassicLayout()
{
            // Update window title based on current context
            var contextTitle = GetContextualWindowTitle();
            if (WindowName != contextTitle)
            {
                WindowName = contextTitle;
            }

            uiStyles.PushMainWindowStyle();

            try
            {
                if (isLoading)
                {
                    DrawClassicLoadingState();
                    return;
                }

                DrawClassicHeader();
                DrawClassicMainContent();
                DrawClassicBottomButtons();

                // Draw mod options popup if open
                DrawClassicModOptionsPopup();
            }
            finally
            {
                uiStyles.PopMainWindowStyle();
            }
        }

        private void DrawClassicHeader()
{
            // Context header showing which character/design is being edited
            if (!string.IsNullOrEmpty(editingCharacterName) || editingDesign != null)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ColorSchemes.Dark.AccentBlue);
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 10));

                var contextText = "";
                if (!string.IsNullOrEmpty(editingCharacterName) && editingDesign != null)
                {
                    var designName = string.IsNullOrEmpty(editingDesign.Name) ? "New Design" : editingDesign.Name;
                    contextText = $"Configuring mods for: {editingCharacterName} - {designName}";
                }
                else if (!string.IsNullOrEmpty(editingCharacterName))
                {
                    contextText = $"Configuring mods for: {editingCharacterName}";
                }
                else if (editingDesign != null)
                {
                    var designName = string.IsNullOrEmpty(editingDesign.Name) ? "New Design" : editingDesign.Name;
                    contextText = $"Configuring mods for design: {designName}";
                }

                // Center the context text
                var textSize = ImGui.CalcTextSize(contextText);
                var windowWidth = ImGui.GetWindowContentRegionMax().X;
                ImGui.SetCursorPosX((windowWidth - textSize.X) / 2);

                ImGui.Text(contextText);
                ImGui.PopStyleVar();
                ImGui.PopStyleColor();
                ImGui.Separator();
                ImGui.Spacing();
            }

            // Collection selector
            if (availableCollections.Any())
            {
                ImGui.Text("Penumbra Collection:");
                ImGui.SameLine();

                var collectionsList = availableCollections.ToList();
                var collectionNames = collectionsList.Select(kvp => kvp.Value).ToArray();

                ImGui.SetNextItemWidth(300);
                if (ImGui.Combo("##CollectionSelect", ref selectedCollectionIndex, collectionNames, collectionNames.Length))
                {
                    var selectedKvp = collectionsList[selectedCollectionIndex];
                    currentCollectionId = selectedKvp.Key;
                    currentCollectionName = selectedKvp.Value;
                    userHasSelectedCollection = true;
                    _ = LoadCurrentMods();
                }

                ImGui.SameLine();
                if (uiStyles.IconButton(FontAwesomeIcon.Sync.ToIconString(), "Refresh mods"))
                {
                    _ = LoadCurrentMods();
                }
            }
            else
            {
                ImGui.TextColored(ColorSchemes.Dark.AccentRed, "Warning: No Penumbra collections found");
            }

            ImGui.Separator();

            // Global search bar (full width)
            ImGui.Spacing();

            ImGui.SetNextItemWidth(-1); // Full width
            if (ImGui.InputTextWithHint("##GlobalSearch", "Global search across all mods...", ref globalSearchFilter, 200))
            {
                // Clear category-specific search when using global search
                if (!string.IsNullOrEmpty(globalSearchFilter))
                {
                    searchFilter = "";
                    currentPage = 0; // Reset pagination when searching
                }
            }

            ImGui.Spacing();
        }

        private void DrawClassicMainContent()
{
            // Check if no mods available
            if (!availableMods.Any())
            {
                var center = ImGui.GetContentRegionAvail() / 2;
                ImGui.SetCursorPos(center - new Vector2(100, 30));
                ImGui.TextColored(ColorSchemes.Dark.TextMuted, "No mods found. This could mean:");
                ImGui.BulletText("Penumbra is not installed or running");
                ImGui.BulletText("Penumbra has no mods in the current collection");
                ImGui.BulletText("No mods are currently affecting your character");

                ImGui.Separator();
                if (ImGui.Button("Retry Loading Mods"))
                {
                    _ = LoadCurrentMods();
                }
                return;
            }

            // Sidebar for categories
            ImGui.BeginChild("CategorySidebar", new Vector2(200, -40), true);

            ImGui.Text("Categories");
            ImGui.Separator();

            for (int i = 0; i < categoryNames.Length; i++)
            {
                var modCount = GetModCountForCategory(i);
                var categoryText = $"{categoryNames[i]} ({modCount})";

                // Highlight selected category
                bool isSelected = selectedCategory == i;
                if (isSelected)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ColorSchemes.Dark.AccentBlue);
                }

                if (ImGui.Selectable(categoryText, isSelected))
                {
                    selectedCategory = i;
                    // Reset to first page when switching categories
                    categoryPageNumbers[i] = 0;
                    currentPage = 0;
                    // Clear search when switching categories
                    searchFilter = "";
                    // Clear global search when switching categories
                    globalSearchFilter = "";
                }

                if (isSelected)
                {
                    ImGui.PopStyleColor();
                }
            }

            ImGui.EndChild();

            // Main mod list area
            ImGui.SameLine();
            ImGui.BeginChild("ModListArea", new Vector2(-1, -40), true);

            // Sticky search bar in header
            var searchBarHeight = ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y * 2;
            ImGui.BeginChild("SearchHeader", new Vector2(-1, searchBarHeight), true, ImGuiWindowFlags.NoScrollbar);

            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##Search", "Search mods...", ref searchFilter, 100))
            {
                // Clear global search when using category search
                if (!string.IsNullOrEmpty(searchFilter))
                {
                    globalSearchFilter = "";
                    currentPage = 0; // Reset pagination when searching
                }
            }

            ImGui.EndChild();

            ImGui.Separator();

            // Scrollable mod list with pagination
            ImGui.BeginChild("ModList", new Vector2(-1, -30), true);

            // Get filtered mods and handle pagination
            var categoryMods = GetFilteredModsForSelectedCategory();
            var totalMods = categoryMods.Count;
            var totalPages = (int)Math.Ceiling((double)totalMods / ModsPerPage);

            // Ensure current page is valid for this category
            if (!categoryPageNumbers.ContainsKey(selectedCategory))
                categoryPageNumbers[selectedCategory] = 0;

            currentPage = categoryPageNumbers[selectedCategory];
            if (currentPage >= totalPages && totalPages > 0)
                currentPage = totalPages - 1;

            // Get mods for current page
            var pagedMods = categoryMods
                .Skip(currentPage * ModsPerPage)
                .Take(ModsPerPage)
                .ToList();

            // Show search result count if searching
            if (!string.IsNullOrWhiteSpace(searchFilter))
            {
                ImGui.TextColored(ColorSchemes.Dark.AccentGreen, $"Found {totalMods} matches");
                ImGui.Separator();
            }

            // Select All for Currently Affecting You tab; selects every non-restricted Gear/Hair mod affecting the character.
            if (selectedCategory == 0)
            {
                var gearHairMods = categoryMods
                    .Where(m => m.ModType == ModType.Gear || m.ModType == ModType.Hair)
                    .ToList();

                if (gearHairMods.Count > 0)
                {
                    int alreadySelected = gearHairMods.Count(m => selectedMods.TryGetValue(m.Directory, out var v) && v);
                    bool allSelected = alreadySelected == gearHairMods.Count;

                    string label = allSelected
                        ? $"Deselect All Gear/Hair ({gearHairMods.Count})"
                        : $"Select All Gear/Hair ({gearHairMods.Count})";

                    if (ImGui.Button(label))
                    {
                        foreach (var mod in gearHairMods)
                        {
                            selectedMods[mod.Directory] = !allSelected;
                        }
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text(allSelected
                            ? "Deselect every Gear/Hair mod currently affecting you"
                            : "Select every Gear/Hair mod currently affecting you");
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f),
                            "Does not touch the Ctrl+click-restricted section below the divider.");
                        ImGui.EndTooltip();
                    }
                    ImGui.Separator();
                }
            }

            // Draw mod entries with divider between Gear/Hair and other types for "Currently Affecting You"
            bool hasDrawnDivider = false;
            bool hasPreviousGearHair = false;

            foreach (var mod in pagedMods)
            {
                // Check if we need to draw a divider (only for "Currently Affecting You" tab)
                if (selectedCategory == 0 && !hasDrawnDivider && hasPreviousGearHair)
                {
                    bool isCurrentGearHair = mod.ModType == ModType.Gear || mod.ModType == ModType.Hair;

                    // If we transition from Gear/Hair to other types, draw divider
                    if (!isCurrentGearHair)
                    {
                        ImGui.Spacing();
                        ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.5f, 0.5f, 0.5f, 0.3f));
                        ImGui.Separator();
                        ImGui.PopStyleColor();

                        // Add small text label for the divider with warning
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 0.8f));
                        ImGui.Text("|- Other Affecting Mods (Eyes, Tattoos, etc.)");
                        ImGui.PopStyleColor();

                        // Warning icon with tooltip
                        ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.8f, 0.2f, 0.9f)); // Orange warning colour
                        ImGui.Text("\uf071"); // Warning triangle icon
                        ImGui.PopStyleColor();
                        ImGui.PopFont();

                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.PushTextWrapPos(350f);
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.9f, 0.8f, 1.0f)); // Warm white
                            ImGui.Text("Design Selection Warning");
                            ImGui.PopStyleColor();
                            ImGui.Separator();
                            ImGui.TextUnformatted("Selecting these mods will tie them to this specific design:");
                            ImGui.Bullet(); ImGui.SameLine(); ImGui.TextUnformatted("They will be DISABLED when switching to other designs");
                            ImGui.Bullet(); ImGui.SameLine(); ImGui.TextUnformatted("They will be ENABLED when switching back to this design");
                            ImGui.Spacing();
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 1.0f, 0.9f, 1.0f)); // Light green
                            ImGui.TextUnformatted("Tip: Only select mods that should be specific to this character/outfit. Leave general customization mods (like ears/tails or tattoos) unselected so they stay active across all designs.");
                            ImGui.PopStyleColor();
                            ImGui.PopTextWrapPos();
                            ImGui.EndTooltip();
                        }

                        ImGui.Spacing();
                        hasDrawnDivider = true;
                    }
                }

                // Check if this mod requires Ctrl+click (other mods after divider in Currently Affecting You tab)
                bool requiresCtrlClick = selectedCategory == 0 && hasDrawnDivider &&
                                        mod.ModType != ModType.Gear && mod.ModType != ModType.Hair;
                DrawClassicModEntry(mod, requiresCtrlClick);

                // Track if this mod is Gear/Hair for next iteration
                if (selectedCategory == 0)
                {
                    bool isGearHair = mod.ModType == ModType.Gear || mod.ModType == ModType.Hair;
                    if (isGearHair)
                        hasPreviousGearHair = true;
                }
            }

            ImGui.EndChild();

            // Pagination controls
            DrawClassicPaginationControls(totalPages, totalMods);

            ImGui.EndChild();
        }

        private void DrawClassicLoadingState()
{
            // Update loading animations and messages
            UpdateLoadingAnimations();

            var contentSize = ImGui.GetContentRegionAvail();
            var centerX = contentSize.X / 2;
            var centerY = contentSize.Y / 2;

            // Center the loading display
            var loadingWidth = 450f;
            var loadingHeight = 180f;
            ImGui.SetCursorPos(new Vector2(centerX - loadingWidth / 2, centerY - loadingHeight / 2));

            // Enhanced panel styling
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(25, 20));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.1f, 0.1f, 0.15f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.3f, 0.5f, 0.8f, 0.6f));

            ImGui.BeginChild("LoadingPanel", new Vector2(loadingWidth, loadingHeight), true, ImGuiWindowFlags.NoScrollbar);

            // Title
            var title = "Loading Mod Information";
            var titleSize = ImGui.CalcTextSize(title);
            ImGui.SetCursorPosX((loadingWidth - titleSize.X) / 2 - 25);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 1.0f, 1.0f));
            ImGui.Text(title);
            ImGui.PopStyleColor();

            ImGui.Spacing();

            // Standard progress bar with enhanced styling
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.2f, 0.6f, 1.0f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
            ImGui.ProgressBar(loadingProgress, new Vector2(loadingWidth - 50, 20), $"{modsLoaded}/{totalModsToLoad}");
            ImGui.PopStyleColor(2);

            ImGui.Spacing();

            // Witty loading message
            if (!string.IsNullOrEmpty(currentLoadingMessage))
            {
                var messageSize = ImGui.CalcTextSize(currentLoadingMessage);
                ImGui.SetCursorPosX((loadingWidth - messageSize.X) / 2 - 25);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
                ImGui.Text(currentLoadingMessage);
                ImGui.PopStyleColor();
            }

            ImGui.Spacing();
            ImGui.Spacing();

            // Cancel button
            var cancelText = "Cancel";
            var cancelButtonSize = new Vector2(80, 28);
            ImGui.SetCursorPosX((loadingWidth - cancelButtonSize.X) / 2 - 25);

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.3f, 0.3f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.8f, 0.4f, 0.4f, 1.0f));

            if (ImGui.Button(cancelText, cancelButtonSize))
            {
                loadingCancellation?.Cancel();
                IsOpen = false;
            }

            ImGui.PopStyleColor(3);

            ImGui.EndChild();

            // Pop all styles
            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar(2);
        }

        private void DrawClassicModEntry(ModEntry mod, bool requiresCtrlClick = false)
{
            // Store the cursor position at the start of the row for context menu
            var rowStartPos = ImGui.GetCursorScreenPos();

            var isPinned = pinnedMods.Contains(mod.Directory);

            // Determine current state: Enable (0), Disable (1), Inherit (2)
            int currentState;
            if (modsToInherit.Contains(mod.Directory))
                currentState = 2; // Inherit
            else if (selectedMods.ContainsKey(mod.Directory))
                currentState = selectedMods[mod.Directory] ? 0 : 1; // Enable or Disable
            else
                currentState = 2; // Not in selectedMods = Inherit by default

            // Track if mod is selected (enabled) for warnings display
            bool isSelected = currentState == 0;

            // Show dropdown when RespectPenumbraInheritance is ON, otherwise use checkbox
            if (plugin.Configuration.RespectPenumbraInheritance)
            {
                // Three-state dropdown: Enable, Disable, Inherit
                string[] options = mod.IsInherited
                    ? new[] { "Enable", "Disable", "Inherit" }
                    : new[] { "Enable", "Disable" };

                ImGui.SetNextItemWidth(85);
                if (ImGui.Combo($"##state{mod.Directory}", ref currentState, options, options.Length))
                {
                    bool allowAction = !requiresCtrlClick || ImGui.GetIO().KeyCtrl;

                    if (allowAction)
                    {
                        // Update state tracking
                        modsToInherit.Remove(mod.Directory);

                        if (currentState == 0) // Enable
                        {
                            selectedMods[mod.Directory] = true;
                            RunModAnalysis(mod);
                        }
                        else if (currentState == 1) // Disable
                        {
                            selectedMods[mod.Directory] = false;
                            ClearModAnalysis(mod);
                        }
                        else // Inherit
                        {
                            selectedMods.Remove(mod.Directory);
                            modsToInherit.Add(mod.Directory);
                            ClearModAnalysis(mod);
                        }
                    }
                }

                // Tooltip for inherited mods
                if (ImGui.IsItemHovered() && mod.IsInherited)
                {
                    ImGui.SetTooltip("This mod is inherited from a parent collection.\nSelect 'Inherit' to let Penumbra manage it.");
                }
            }
            else
            {
                // Original checkbox behaviour
                isSelected = selectedMods.ContainsKey(mod.Directory) ? selectedMods[mod.Directory] : false;

                bool checkboxClicked = ImGui.Checkbox($"##sel{mod.Directory}", ref isSelected);

                if (checkboxClicked)
                {
                    bool allowAction = !requiresCtrlClick || ImGui.GetIO().KeyCtrl;

                    if (!allowAction)
                    {
                        isSelected = !isSelected; // Revert
                    }
                    else
                    {
                        selectedMods[mod.Directory] = isSelected;

                        if (isSelected)
                            RunModAnalysis(mod);
                        else
                            ClearModAnalysis(mod);
                    }
                }

                if (requiresCtrlClick && ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Hold Ctrl while clicking to select this mod");
                }
            }

            ImGui.SameLine();

            // Pin button
            ImGui.PushFont(UiBuilder.IconFont);
            var pinIcon = isPinned ? FontAwesomeIcon.Thumbtack.ToIconString() : FontAwesomeIcon.MapPin.ToIconString();
            var pinColor = isPinned ? ColorSchemes.Dark.AccentYellow : ColorSchemes.Dark.TextMuted;

            ImGui.PushStyleColor(ImGuiCol.Text, pinColor);
            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 2)); // Reduce padding to help centering
            if (ImGui.Button($"{pinIcon}##pin{mod.Directory}", new Vector2(20, 20)))
            {
                if (isPinned)
                {
                    Plugin.Log.Information($"[PIN DEBUG] Unpinning mod: {mod.Directory}");
                    pinnedMods.Remove(mod.Directory);
                }
                else
                {
                    Plugin.Log.Information($"[PIN DEBUG] Pinning mod: {mod.Directory}");
                    pinnedMods.Add(mod.Directory);
                    // Automatically check the mod when pinning it
                    selectedMods[mod.Directory] = true;
                }
                Plugin.Log.Information($"[PIN DEBUG] Current pinned mods: {string.Join(", ", pinnedMods)}");
            }
            ImGui.PopStyleVar(2); // Pop both FramePadding and ButtonTextAlign
            ImGui.PopStyleColor();
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(isPinned ? "Unpin mod (will be disabled when switching)" : "Pin mod (never gets disabled)");
            }

            ImGui.SameLine();

            // Edit icon for configurable mods only
            var hasOptions = ModHasOptionsCache(mod.Directory, mod.Name);
            var hasCustomOptions = editingDesign?.ModOptionSettings?.ContainsKey(mod.Directory) ?? false;

            if (hasOptions)
            {
                ImGui.PushFont(UiBuilder.IconFont);
                var iconColor = hasCustomOptions ? ColorSchemes.Dark.AccentBlue : ColorSchemes.Dark.AccentYellow;
                ImGui.PushStyleColor(ImGuiCol.Text, iconColor);
                ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 2));

                if (ImGui.Button($"{FontAwesomeIcon.Edit.ToIconString()}##edit{mod.Directory}", new Vector2(20, 20)))
                {
                    ClassicOpenModOptionsPanel(mod);
                }

                ImGui.PopStyleVar(2);
                ImGui.PopStyleColor();
                ImGui.PopFont();

                if (ImGui.IsItemHovered())
                {
                    var tooltip = hasCustomOptions
                        ? "Edit mod configuration options"
                        : "Configure mod options";
                    ImGui.SetTooltip(tooltip);
                }
            }
            else
            {
                // Empty space to maintain alignment
                ImGui.Dummy(new Vector2(20, 20));
            }

            ImGui.SameLine();

            // Mod name and status
            ImGui.Text(mod.Name);

            if (mod.IsCurrentlyAffecting)
            {
                ImGui.SameLine();
                ImGui.TextColored(ColorSchemes.Dark.AccentGreen, $"★ Priority {mod.Priority}");
            }
            else if (mod.IsEnabled)
            {
                ImGui.SameLine();
                ImGui.TextColored(ColorSchemes.Dark.AccentYellow, $"Enabled");
            }

            // Show dependency indicators
            if (mod.Dependencies.Any())
            {
                ImGui.SameLine();

                // Check if all dependencies are met
                var unmetDependencies = mod.Dependencies.Where(d => !d.IsFound ||
                    !selectedMods.ContainsKey(d.RequiredModPath) ||
                    !selectedMods[d.RequiredModPath]).ToList();

                if (unmetDependencies.Any())
                {
                    ImGui.TextColored(ColorSchemes.Dark.AccentRed, $"⚠ Missing {unmetDependencies.Count} dependencies");
                }
                else
                {
                    ImGui.TextColored(ColorSchemes.Dark.AccentGreen, "✓ Dependencies met");
                }
            }

            // Show dependency warnings for incomplete gear mods only
            if (mod.ModType == ModType.Gear)
            {
                if (mod.HasOnlyModels)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(ColorSchemes.Dark.AccentYellow, "⚠ Needs texture mod");
                }
                else if (mod.HasOnlyTextures)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(ColorSchemes.Dark.AccentYellow, "⚠ Needs model mod");
                }
            }

            // Tooltip with categories and dependencies
            if (ImGui.IsItemHovered())
            {
                var tooltipLines = new List<string>();

                if (mod.Categories.Any())
                {
                    tooltipLines.Add($"Categories: {string.Join(", ", mod.Categories)}");
                }

                if (mod.Dependencies.Any())
                {
                    tooltipLines.Add("");
                    tooltipLines.Add("Dependencies:");
                    foreach (var dep in mod.Dependencies)
                    {
                        var status = dep.IsFound ?
                            (selectedMods.ContainsKey(dep.RequiredModPath) && selectedMods[dep.RequiredModPath] ? "✓" : "✗") :
                            "⚠ Not found";
                        tooltipLines.Add($"  {status} {dep.RequiredModName}");
                    }
                }

                if (mod.HasOnlyModels)
                {
                    tooltipLines.Add("");
                    tooltipLines.Add("This mod contains only models and requires texture dependencies.");
                }
                else if (mod.HasOnlyTextures)
                {
                    tooltipLines.Add("");
                    tooltipLines.Add("This mod contains only textures/materials and requires model dependencies.");
                }

                if (tooltipLines.Any())
                {
                    ImGui.SetTooltip(string.Join("\n", tooltipLines));
                }
            }

            // Show contextual warnings for selected mods
            if (isSelected && mod.Analysis != null && !dismissedWarnings.Contains(mod.Directory))
            {
                DrawContextualWarning(mod);
            }

            // Right-click context menu for manual categorization - draw invisible button over entire row
            var rowEndPos = ImGui.GetCursorScreenPos();
            var rowSize = new Vector2(ImGui.GetContentRegionAvail().X, rowEndPos.Y - rowStartPos.Y);

            ImGui.SetCursorScreenPos(rowStartPos);
            ImGui.InvisibleButton($"##ModRow_{mod.Directory}", rowSize);

            DrawModCategoryContextMenu(mod);
        }

        /// <summary>Draw the mod options configuration popup.</summary>
        private void DrawClassicModOptionsPopup()
{
            if (optionsEditingMod == null)
                return;
            if (availableModOptions == null)
                return;
            if (currentModOptions == null)
                return;

            // If optionGroupTypes is null, we need to reload it
            if (optionGroupTypes == null)
            {
                optionGroupTypes = new Dictionary<string, int>();
                var rawOptions = plugin.PenumbraIntegration.GetModOptionsRaw(optionsEditingMod.Directory, optionsEditingMod.Name);
                foreach (var (groupName, (optionNames, groupType)) in rawOptions)
                {
                    optionGroupTypes[groupName] = groupType;
                }
            }

            var popupId = $"ModOptions_{optionsEditingMod.Directory}";

            // Open popup if flag is set
            if (shouldOpenOptionsPopup)
            {
                ImGui.OpenPopup(popupId);
                shouldOpenOptionsPopup = false;
                isOptionsPopupOpen = true;
            }

            ImGui.SetNextWindowSize(new Vector2(500, 600), ImGuiCond.FirstUseEver);

            if (ImGui.BeginPopupModal(popupId, ref isOptionsPopupOpen))
            {
                // Title
                ImGui.Text($"Configure: {optionsEditingMod.Name}");
                ImGui.Separator();

                // Show status based on whether we're editing a design
                var hasCustomOptions = false;
                if (editingDesign != null)
                {
                    hasCustomOptions = editingDesign.ModOptionSettings?.ContainsKey(optionsEditingMod.Directory) ?? false;
                    if (hasCustomOptions)
                    {
                        ImGui.TextColored(ColorSchemes.Dark.AccentBlue, "✓ Custom options configured for this design");
                    }
                    else
                    {
                        ImGui.TextColored(ColorSchemes.Dark.AccentYellow, "⚠ Using current Penumbra settings");
                    }
                }
                else
                {
                    ImGui.TextColored(ColorSchemes.Dark.AccentGreen, "Editing current Penumbra settings");
                }
                ImGui.Separator();

                // Scrollable area for options
                if (ImGui.BeginChild("OptionsArea", new Vector2(0, 450)))
                {
                    // Filter and organize options by type to match Penumbra's layout
                    var filteredOptions = availableModOptions
                        .Where(kvp => kvp.Value.Any() &&
                               kvp.Key != "Necessary Files" &&
                               kvp.Key != "Done!")
                        .ToList();

                    // Group by type for consistent layout
                    var comboGroups = new List<(string name, string[] options)>();
                    var radioGroups = new List<(string name, string[] options)>();
                    var checkboxGroups = new List<(string name, string[] options)>();

                    // Get fresh type information right when we need it
                    var rawOptionsForTypes = plugin.PenumbraIntegration.GetModOptionsRaw(optionsEditingMod.Directory, optionsEditingMod.Name);

                    foreach (var (groupName, optionNames) in filteredOptions)
                    {
                        // Look up the type from fresh data
                        var groupType = 0;
                        if (rawOptionsForTypes.ContainsKey(groupName))
                        {
                            groupType = rawOptionsForTypes[groupName].Item2;
                        }

                        var isMultiSelect = groupType == 1 || groupType == 2;

                        if (isMultiSelect)
                        {
                            checkboxGroups.Add((groupName, optionNames.ToArray()));
                        }
                        else if (optionNames.Count > 2)
                        {
                            comboGroups.Add((groupName, optionNames.ToArray()));
                        }
                        else
                        {
                            radioGroups.Add((groupName, optionNames.ToArray()));
                        }
                    }


                    // Draw dropdown combos first (single-choice, >2 options)
                    foreach (var (groupName, optionNames) in comboGroups)
                    {
                        var currentSelection = currentModOptions.ContainsKey(groupName) && currentModOptions[groupName].Any()
                            ? currentModOptions[groupName].First()
                            : optionNames.First();

                        var currentIndex = Array.IndexOf(optionNames, currentSelection);
                        if (currentIndex < 0) currentIndex = 0;

                        ImGui.PushStyleColor(ImGuiCol.Text, ColorSchemes.Dark.AccentBlue);
                        ImGui.Text(groupName);
                        ImGui.PopStyleColor();

                        ImGui.SetNextItemWidth(400);
                        if (ImGui.Combo($"##{groupName}_combo", ref currentIndex, optionNames, optionNames.Length))
                        {
                            currentModOptions[groupName] = new List<string> { optionNames[currentIndex] };
                        }

                        ImGui.Spacing();
                    }

                    // Draw radio button groups second (single-choice, ≤2 options)
                    foreach (var (groupName, optionNames) in radioGroups)
                    {
                        // Simple section header with no child window - same style as checkboxes
                        ImGui.PushStyleColor(ImGuiCol.Text, ColorSchemes.Dark.AccentBlue);
                        ImGui.Text(groupName);
                        ImGui.PopStyleColor();

                        var currentSelection = currentModOptions.ContainsKey(groupName) && currentModOptions[groupName].Any()
                            ? currentModOptions[groupName].First()
                            : optionNames.First();

                        // Draw radio buttons inline
                        ImGui.SameLine();
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 20); // Small indent

                        for (int i = 0; i < optionNames.Length; i++)
                        {
                            if (i > 0) ImGui.SameLine();

                            if (ImGui.RadioButton($"{optionNames[i]}##{groupName}", currentSelection == optionNames[i]))
                            {
                                currentModOptions[groupName] = new List<string> { optionNames[i] };
                            }
                        }

                        ImGui.Spacing();
                    }

                    // Draw checkbox groups last (multi-choice, Type 1/2)
                    foreach (var (groupName, optionNames) in checkboxGroups)
                    {
                        // Simple section header with no child window
                        ImGui.PushStyleColor(ImGuiCol.Text, ColorSchemes.Dark.AccentBlue);
                        ImGui.Text(groupName);
                        ImGui.PopStyleColor();

                        ImGui.Spacing();

                        var currentSelections = currentModOptions.ContainsKey(groupName)
                            ? currentModOptions[groupName]
                            : new List<string>();

                        foreach (var optionName in optionNames)
                        {
                            var isSelected = currentSelections.Contains(optionName);
                            if (ImGui.Checkbox($"{optionName}##{groupName}", ref isSelected))
                            {
                                if (isSelected)
                                {
                                    if (!currentSelections.Contains(optionName))
                                        currentSelections.Add(optionName);
                                }
                                else
                                {
                                    currentSelections.Remove(optionName);
                                }
                                currentModOptions[groupName] = new List<string>(currentSelections);
                            }
                        }

                        ImGui.Spacing();
                        ImGui.Separator();
                        ImGui.Spacing();
                    }
                }
                ImGui.EndChild();

                ImGui.Separator();

                // Buttons
                if (ImGui.Button("Save to Design", new Vector2(120, 0)))
                {
                    ClassicSaveModOptionsToDesign();
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (hasCustomOptions && ImGui.Button("Clear Design Options", new Vector2(150, 0)))
                {
                    ClassicClearModOptionsFromDesign();
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel", new Vector2(80, 0)))
                {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }

            // Only clean up when popup is actually closed
            if (!ImGui.IsPopupOpen(popupId) && !shouldOpenOptionsPopup)
            {
                // Popup was closed, clean up
                optionsEditingMod = null;
                availableModOptions = null;
                currentModOptions = null;
                optionGroupTypes = null;
                isOptionsPopupOpen = false;
            }
        }

        /// <summary>Draw pagination controls at the bottom of the mod list.</summary>
        private void DrawClassicPaginationControls(int totalPages, int totalMods)
{
            if (totalPages <= 1) return;

            ImGui.Separator();

            var buttonWidth = 30f;
            var pageText = $"Page {currentPage + 1} of {totalPages} ({totalMods} mods)";
            var textSize = ImGui.CalcTextSize(pageText);

            // Center the pagination controls
            var totalWidth = buttonWidth * 4 + textSize.X + ImGui.GetStyle().ItemSpacing.X * 4;
            var startX = (ImGui.GetContentRegionAvail().X - totalWidth) / 2;

            ImGui.SetCursorPosX(startX);

            // First page button
            ImGui.BeginDisabled(currentPage == 0);
            if (ImGui.Button("<<", new Vector2(buttonWidth, 0)))
            {
                currentPage = 0;
                categoryPageNumbers[selectedCategory] = currentPage;
            }
            ImGui.EndDisabled();

            ImGui.SameLine();

            // Previous page button
            ImGui.BeginDisabled(currentPage == 0);
            if (ImGui.Button("<", new Vector2(buttonWidth, 0)))
            {
                currentPage--;
                categoryPageNumbers[selectedCategory] = currentPage;
            }
            ImGui.EndDisabled();

            ImGui.SameLine();
            ImGui.Text(pageText);
            ImGui.SameLine();

            // Next page button
            ImGui.BeginDisabled(currentPage >= totalPages - 1);
            if (ImGui.Button(">", new Vector2(buttonWidth, 0)))
            {
                currentPage++;
                categoryPageNumbers[selectedCategory] = currentPage;
            }
            ImGui.EndDisabled();

            ImGui.SameLine();

            // Last page button
            ImGui.BeginDisabled(currentPage >= totalPages - 1);
            if (ImGui.Button(">>", new Vector2(buttonWidth, 0)))
            {
                currentPage = totalPages - 1;
                categoryPageNumbers[selectedCategory] = currentPage;
            }
            ImGui.EndDisabled();
        }

        private void DrawClassicBottomButtons()
{
            ImGui.Separator();

            var selectedCount = selectedMods.Count(kvp => kvp.Value);
            ImGui.Text($"Selected: {selectedCount} mods");

            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - 185);

            uiStyles.PushDarkButtonStyle();
            if (ImGui.Button("Apply", new Vector2(100, 0)))
            {
                SaveSelection();
                IsOpen = false;
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(75, 0)))
            {
                IsOpen = false;
            }
            uiStyles.PopDarkButtonStyle();
        }

        /// <summary>Open the mod options configuration panel.</summary>
        private void ClassicOpenModOptionsPanel(ModEntry mod)
{

            optionsEditingMod = mod;

            // Get available options from Penumbra
            availableModOptions = plugin.PenumbraIntegration.GetModOptions(mod.Directory, mod.Name);
            optionGroupTypes = new Dictionary<string, int>();

            // Parse group types from Penumbra API - the int in the tuple is the group type
            // 0 = Single-select, 1 = Multi-select
            var rawOptions = plugin.PenumbraIntegration.GetModOptionsRaw(mod.Directory, mod.Name);
            foreach (var (groupName, (optionNames, groupType)) in rawOptions)
            {
                optionGroupTypes[groupName] = groupType;
            }

            // Load current settings for this mod
            if (editingDesign?.ModOptionSettings?.ContainsKey(mod.Directory) ?? false)
            {
                // Use design's saved options
                currentModOptions = new Dictionary<string, List<string>>(editingDesign.ModOptionSettings[mod.Directory]);
            }
            else if (currentCollectionId != Guid.Empty)
            {
                // Get current options from Penumbra
                var (success, _, _, options) = plugin.PenumbraIntegration.GetCurrentModSettings(currentCollectionId, mod.Directory, mod.Name);
                if (success && options.Any())
                {
                    currentModOptions = options;
                }
                else
                {
                    // No current settings - use defaults (handle multi-select vs single-select)
                    currentModOptions = new Dictionary<string, List<string>>();
                    foreach (var (groupName, optionNames) in availableModOptions)
                    {
                        if (optionNames.Any())
                        {
                            var groupType = optionGroupTypes?.ContainsKey(groupName) == true ? optionGroupTypes[groupName] : 0;
                            var isMultiSelect = groupType == 1 || groupType == 2;

                            if (isMultiSelect)
                            {
                                // Multi-select: start with empty selection
                                currentModOptions[groupName] = new List<string>();
                            }
                            else
                            {
                                // Single-select: use first option as default
                                currentModOptions[groupName] = new List<string> { optionNames.First() };
                            }
                        }
                    }
                }
            }
            else
            {
                // Default to appropriate selection based on group type
                currentModOptions = new Dictionary<string, List<string>>();
                foreach (var (groupName, optionNames) in availableModOptions)
                {
                    if (optionNames.Any())
                    {
                        var groupType = optionGroupTypes?.ContainsKey(groupName) == true ? optionGroupTypes[groupName] : 0;
                        var isMultiSelect = groupType == 1 || groupType == 2;

                        if (isMultiSelect)
                        {
                            // Multi-select: start with empty selection
                            currentModOptions[groupName] = new List<string>();
                        }
                        else
                        {
                            // Single-select: use first option as default
                            currentModOptions[groupName] = new List<string> { optionNames.First() };
                        }
                    }
                }
            }

            shouldOpenOptionsPopup = true;
        }

        /// <summary>Save the current mod options to the design.</summary>
        private void ClassicSaveModOptionsToDesign()
{
            if (optionsEditingMod == null || currentModOptions == null)
                return;

            // If editing a design, save to design
            if (editingDesign != null)
            {
                // Initialize the design's mod options if needed
                editingDesign.ModOptionSettings ??= new Dictionary<string, Dictionary<string, List<string>>>();

                // Save the current options
                editingDesign.ModOptionSettings[optionsEditingMod.Directory] = new Dictionary<string, List<string>>(currentModOptions);
            }

            // Apply the options immediately to Penumbra if we have a collection
            if (currentCollectionId != Guid.Empty)
            {
                _ = Task.Run(async () =>
                {
                    foreach (var (groupName, options) in currentModOptions)
                    {
                        plugin.PenumbraIntegration.TrySetModSettings(currentCollectionId, optionsEditingMod.Directory, optionsEditingMod.Name, groupName, options);
                        await Task.Delay(10); // Small delay to avoid overwhelming Penumbra
                    }
                });
            }

            // Saved mod options to design (log removed to prevent spam)
        }

        /// <summary>Clear mod options from the design (use Penumbra defaults).</summary>
        private void ClassicClearModOptionsFromDesign()
{
            if (optionsEditingMod == null || editingDesign == null)
                return;

            // Remove from design settings
            editingDesign.ModOptionSettings?.Remove(optionsEditingMod.Directory);

            // Cleared mod options from design (log removed to prevent spam)
        }

    }
}

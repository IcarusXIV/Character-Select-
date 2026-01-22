using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Utility;
using System.Collections.Generic;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin.Windows
{
    public class QuickSwitchWindow : Window
    {
        private readonly Plugin plugin;
        private int selectedCharacterIndex = -1;
        private int selectedDesignIndex = -1;
        private int lastAppliedCharacterIndex = -1;
        private bool hasAppliedMacroThisSession = false;
        private bool hasInitializedSelection = false;
        private bool userIsInteracting = false;
        private string lastTrackedDesignName = "";

        public QuickSwitchWindow(Plugin plugin)
            : base("Quick Character Switch", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoResize)
        {
            this.plugin = plugin;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(360, 75),
                MaximumSize = new Vector2(360, 75)
            };
        }

        public override void Draw()
        {
            float scale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;

            int themeColorCount = ThemeHelper.PushThemeColors(plugin.Configuration);

            try
            {
                if (!hasInitializedSelection && plugin.Characters.Count > 0)
                {
                    InitializeLastUsedSelection();
                    hasInitializedSelection = true;
                }

            if (plugin.Configuration.QuickSwitchCompact)
            {
                this.Flags = ImGuiWindowFlags
                    .NoTitleBar
                    | ImGuiWindowFlags.NoResize
                    | ImGuiWindowFlags.NoScrollbar
                    | ImGuiWindowFlags.NoScrollWithMouse
                    | ImGuiWindowFlags.NoBackground;
                SizeConstraints = new WindowSizeConstraints
                {
                    MinimumSize = new System.Numerics.Vector2(360 * scale, 28 * scale),
                    MaximumSize = new System.Numerics.Vector2(360 * scale, 28 * scale),
                };

                // Get button opacity - use custom value if Custom theme, otherwise 1.0 (opaque)
                float buttonOpacity = 1.0f;
                if (plugin.Configuration.SelectedTheme == ThemeSelection.Custom)
                {
                    buttonOpacity = plugin.Configuration.CustomTheme.CompactQuickSwitchButtonOpacity;
                }

                // Push button colours for compact mode (NoBackground means semi-transparent buttons are see-through)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.16f, 0.16f, 0.16f, buttonOpacity));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.22f, 0.22f, 0.22f, buttonOpacity));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.28f, 0.28f, 0.28f, buttonOpacity));
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.12f, 0.12f, 0.12f, buttonOpacity));
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.18f, 0.18f, 0.18f, buttonOpacity));
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.22f, 0.22f, 0.22f, buttonOpacity));
            }
            else
            {
                this.Flags = ImGuiWindowFlags.NoResize
                           | ImGuiWindowFlags.NoScrollbar
                           | ImGuiWindowFlags.NoScrollWithMouse;
                SizeConstraints = new WindowSizeConstraints
                {
                    MinimumSize = new System.Numerics.Vector2(360 * scale, 55 * scale),
                    MaximumSize = new System.Numerics.Vector2(360 * scale, 58 * scale),
                };
            }

            float dropdownWidth = 135 * scale;
            float spacing = 6 * scale;

            ImGui.SetNextItemWidth(dropdownWidth);
            int tempCharacterIndex = selectedCharacterIndex;

            if (ImGui.BeginCombo("##CharacterDropdown", GetSelectedCharacterName(), ImGuiComboFlags.HeightRegular))
            {
                for (int i = 0; i < plugin.Characters.Count; i++)
                {
                    var character = plugin.Characters[i];
                    bool isSelected = (tempCharacterIndex == i);

                    if (ImGui.Selectable(character.Name, isSelected))
                    {
                        tempCharacterIndex = i;

                        if (character.Designs.Count > 0)
                        {
                            var sortedDesigns = GetSortedDesigns(character);
                            if (sortedDesigns.Count > 0)
                            {
                                selectedDesignIndex = GetOriginalIndex(character, sortedDesigns[0]);
                            }
                        }
                        else
                        {
                            selectedDesignIndex = -1;
                        }
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            selectedCharacterIndex = tempCharacterIndex;

            ImGui.SameLine(0, spacing);

            if (selectedCharacterIndex >= 0 && selectedCharacterIndex < plugin.Characters.Count)
            {
                var selectedCharacter = plugin.Characters[selectedCharacterIndex];

                if (!userIsInteracting)
                    UpdateSelectedDesignFromConfig(selectedCharacter);

                int tempDesignIndex = selectedDesignIndex;

                ImGui.SetNextItemWidth(dropdownWidth);
                if (ImGui.BeginCombo("##DesignDropdown", GetSelectedDesignName(selectedCharacter), ImGuiComboFlags.HeightRegular))
                {
                    userIsInteracting = true;

                    var orderedDesigns = GetSortedDesigns(selectedCharacter)
                        .Select((d, index) => new { Design = d, OriginalIndex = GetOriginalIndex(selectedCharacter, d) })
                        .ToList();

                    for (int j = 0; j < orderedDesigns.Count; j++)
                    {
                        var entry = orderedDesigns[j];
                        bool isSelected = (tempDesignIndex == entry.OriginalIndex);

                        if (ImGui.Selectable(entry.Design.Name, isSelected))
                        {
                            tempDesignIndex = entry.OriginalIndex;
                            userIsInteracting = true;
                            lastTrackedDesignName = entry.Design.Name;
                        }

                        // Preview tooltip
                        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(entry.Design.PreviewImagePath) && File.Exists(entry.Design.PreviewImagePath))
                        {
                            try
                            {
                                var texture = Plugin.TextureProvider.GetFromFile(entry.Design.PreviewImagePath).GetWrapOrDefault();
                                if (texture != null)
                                {
                                    float maxSize = 300f * scale;
                                    var (displayWidth, displayHeight) = CalculateImageDimensions(texture, maxSize);

                                    var mousePos = ImGui.GetMousePos();
                                    var dropdownRect = ImGui.GetItemRectMax();
                                    var viewportSize = ImGui.GetMainViewport().Size;

                                    var tooltipPos = new Vector2(dropdownRect.X + 10, mousePos.Y - displayHeight / 2);

                                    if (tooltipPos.X + displayWidth > viewportSize.X)
                                        tooltipPos.X = ImGui.GetItemRectMin().X - displayWidth - 10;

                                    if (tooltipPos.Y < 0)
                                        tooltipPos.Y = 0;
                                    else if (tooltipPos.Y + displayHeight > viewportSize.Y)
                                        tooltipPos.Y = viewportSize.Y - displayHeight;

                                    ImGui.SetNextWindowPos(tooltipPos);
                                    ImGui.BeginTooltip();
                                    ImGui.Image(texture.Handle, new Vector2(displayWidth, displayHeight));
                                    ImGui.EndTooltip();
                                }
                            }
                            catch { }
                        }

                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }

                    ImGui.EndCombo();
                }

                selectedDesignIndex = tempDesignIndex;
            }
            else
            {
                ImGui.BeginDisabled();
                ImGui.SetNextItemWidth(dropdownWidth);
                ImGui.Combo("##DesignDropdown", ref selectedDesignIndex, Array.Empty<string>(), 0);
                ImGui.EndDisabled();
            }

            ImGui.SameLine(0, spacing);

            if (selectedCharacterIndex >= 0)
            {
                if (ImGui.Button("Apply", new Vector2(50, ImGui.GetFrameHeight())))
                {
                    userIsInteracting = false;
                    ApplySelection();
                }

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    userIsInteracting = false;

                    var io = ImGui.GetIO();
                    if (io.KeyCtrl)
                        RevertToCurrentPlayerCharacter();
                    else
                        ApplyToTarget();
                }
            }
            else
            {
                ImGui.BeginDisabled();
                ImGui.Button("Apply", new Vector2(50, ImGui.GetFrameHeight()));
                ImGui.EndDisabled();
            }

                if (selectedCharacterIndex >= 0)
                {
                    Vector4 charColor = GetNameplateColor(plugin.Characters[selectedCharacterIndex]);
                    ImGui.PushStyleColor(ImGuiCol.ChildBg, charColor);
                    ImGui.BeginChild("ColorBar", new Vector2(ImGui.GetContentRegionAvail().X, 3), false);
                    ImGui.EndChild();
                    ImGui.PopStyleColor();
                }
            }
            finally
            {
                // Pop compact mode opaque colours if we pushed them
                if (plugin.Configuration.QuickSwitchCompact)
                {
                    ImGui.PopStyleColor(6);
                }
                ThemeHelper.PopThemeColors(themeColorCount);
            }
        }

        /// <summary>Initialises dropdown selections from last used character.</summary>
        private void InitializeLastUsedSelection()
        {
            try
            {
                Plugin.Log.Debug("[QuickSwitch] Initializing last used selection...");

                if (Plugin.ClientState.LocalPlayer?.HomeWorld.IsValid == true)
                {
                    string localName = Plugin.ClientState.LocalPlayer.Name.TextValue;
                    string worldName = Plugin.ClientState.LocalPlayer.HomeWorld.Value.Name.ToString();
                    string fullKey = $"{localName}@{worldName}";

                    if (plugin.Configuration.LastUsedCharacterByPlayer.TryGetValue(fullKey, out var lastUsedKey))
                    {
                        var character = plugin.Characters.FirstOrDefault(c =>
                            $"{c.Name}@{worldName}" == lastUsedKey);

                        if (character != null)
                        {
                            selectedCharacterIndex = plugin.Characters.IndexOf(character);
                            Plugin.Log.Debug($"[QuickSwitch] Found last used character: {character.Name} at index {selectedCharacterIndex}");

                            if (plugin.Configuration.LastUsedDesignByCharacter.TryGetValue(character.Name, out var lastDesignName))
                            {
                                var design = character.Designs.FirstOrDefault(d => d.Name == lastDesignName);
                                if (design != null)
                                {
                                    selectedDesignIndex = character.Designs.IndexOf(design);
                                    lastTrackedDesignName = lastDesignName;
                                    Plugin.Log.Debug($"[QuickSwitch] Found last used design: {lastDesignName} at index {selectedDesignIndex}");
                                }
                            }
                            return;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(plugin.Configuration.LastUsedCharacterKey))
                {
                    var character = plugin.Characters.FirstOrDefault(c => c.Name == plugin.Configuration.LastUsedCharacterKey);
                    if (character != null)
                    {
                        selectedCharacterIndex = plugin.Characters.IndexOf(character);
                        Plugin.Log.Debug($"[QuickSwitch] Found global last used character: {character.Name} at index {selectedCharacterIndex}");

                        if (plugin.Configuration.LastUsedDesignByCharacter.TryGetValue(character.Name, out var lastDesignName))
                        {
                            var design = character.Designs.FirstOrDefault(d => d.Name == lastDesignName);
                            if (design != null)
                            {
                                selectedDesignIndex = character.Designs.IndexOf(design);
                                lastTrackedDesignName = lastDesignName;
                                Plugin.Log.Debug($"[QuickSwitch] Found last used design for global character: {lastDesignName} at index {selectedDesignIndex}");
                            }
                        }
                        return;
                    }
                }

                if (!string.IsNullOrEmpty(plugin.Configuration.MainCharacterName))
                {
                    var mainCharacter = plugin.Characters.FirstOrDefault(c => c.Name == plugin.Configuration.MainCharacterName);
                    if (mainCharacter != null)
                    {
                        selectedCharacterIndex = plugin.Characters.IndexOf(mainCharacter);
                        Plugin.Log.Debug($"[QuickSwitch] Defaulting to main character: {mainCharacter.Name} at index {selectedCharacterIndex}");
                        return;
                    }
                }

                if (plugin.Characters.Count > 0)
                {
                    selectedCharacterIndex = 0;
                    Plugin.Log.Debug($"[QuickSwitch] Defaulting to first character: {plugin.Characters[0].Name}");
                }

                Plugin.Log.Debug($"[QuickSwitch] Final selection - Character: {selectedCharacterIndex}, Design: {selectedDesignIndex}");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[QuickSwitch] Error initializing selection: {ex.Message}");
                if (plugin.Characters.Count > 0)
                    selectedCharacterIndex = 0;
            }
        }

        public void RefreshSelection()
        {
            hasInitializedSelection = false;
        }

        public void UpdateSelectionFromCharacter(Character character)
        {
            if (character == null) return;

            var index = plugin.Characters.IndexOf(character);
            if (index >= 0)
            {
                selectedCharacterIndex = index;

                if (plugin.Configuration.LastUsedDesignByCharacter.TryGetValue(character.Name, out var lastDesignName))
                {
                    var design = character.Designs.FirstOrDefault(d => d.Name == lastDesignName);
                    selectedDesignIndex = design != null ? character.Designs.IndexOf(design) : -1;
                }
                else
                {
                    selectedDesignIndex = -1;
                }

                Plugin.Log.Debug($"[QuickSwitch] Updated selection to character: {character.Name} (index {selectedCharacterIndex})");
            }
        }

        private List<CharacterDesign> GetSortedDesigns(Character character)
        {
            var sortIndex = plugin.Configuration.CurrentDesignSortIndex;
            var designs = character.Designs.ToList();
            
            // 0=Favorites, 1=Alphabetical, 2=Recent, 3=Oldest, 4=Manual
            if (sortIndex == 4) // Manual
                return designs;

            if (sortIndex == 0) // Favorites
            {
                designs.Sort((a, b) =>
                {
                    int favCompare = b.IsFavorite.CompareTo(a.IsFavorite);
                    if (favCompare != 0) return favCompare;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            }
            else if (sortIndex == 1) // Alphabetical
            {
                designs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            else if (sortIndex == 2) // Recent
            {
                designs.Sort((a, b) => b.DateAdded.CompareTo(a.DateAdded));
            }
            else if (sortIndex == 3) // Oldest
            {
                designs.Sort((a, b) => a.DateAdded.CompareTo(b.DateAdded));
            }
            
            return designs;
        }
        
        private int GetOriginalIndex(Character character, CharacterDesign design)
        {
            return character.Designs.FindIndex(d => d.Id == design.Id);
        }

        private Vector4 GetNameplateColor(Character character)
        {
            return new Vector4(character.NameplateColor.X, character.NameplateColor.Y, character.NameplateColor.Z, 1.0f);
        }

        private string GetSelectedCharacterName()
        {
            return (selectedCharacterIndex >= 0 && selectedCharacterIndex < plugin.Characters.Count)
                ? plugin.Characters[selectedCharacterIndex].Name
                : "Select Character";
        }

        private string GetSelectedDesignName(Character character)
        {
            return (selectedDesignIndex >= 0 && selectedDesignIndex < character.Designs.Count)
                ? character.Designs[selectedDesignIndex].Name
                : "Select Design";
        }

        private Vector4 GetContrastingTextColor(Vector4 bgColor)
        {
            float brightness = (0.299f * bgColor.X + 0.587f * bgColor.Y + 0.114f * bgColor.Z);
            return brightness > 0.5f ? new Vector4(0, 0, 0, 1) : new Vector4(1, 1, 1, 1); // 
        }

        private void ApplySelection()
        {
            if (selectedCharacterIndex < 0 || selectedCharacterIndex >= plugin.Characters.Count)
                return;

            var character = plugin.Characters[selectedCharacterIndex];
            plugin.ApplyProfile(character, selectedDesignIndex);

            bool shouldRestoreCharacterIdle = character.IdlePoseIndex < 7;

            if (selectedDesignIndex >= 0 && selectedDesignIndex < character.Designs.Count)
            {
                var design = character.Designs[selectedDesignIndex];
                var macro = design.IsAdvancedMode ? design.AdvancedMacro : design.Macro;

                bool designHasSidle = !string.IsNullOrEmpty(macro) &&
                                     macro.Split('\n').Any(line => line.Trim().StartsWith("/sidle", StringComparison.OrdinalIgnoreCase));

                if (designHasSidle)
                {
                    shouldRestoreCharacterIdle = false;
                    Plugin.Log.Debug("[QuickSwitch] Skipping character idle restoration - design contains /sidle command");
                }
            }

            if (shouldRestoreCharacterIdle)
                plugin.PoseRestorer.RestorePosesFor(character);
            else if (character.IdlePoseIndex >= 7)
                Plugin.Log.Debug("[QuickSwitch] Skipping idle pose restore — IdlePoseIndex is None.");
        }

        private void ApplyToTarget()
        {
            if (selectedCharacterIndex < 0 || selectedCharacterIndex >= plugin.Characters.Count)
                return;

            var character = plugin.Characters[selectedCharacterIndex];

            var target = plugin.GetCurrentTarget();
            if (target == null)
            {
                Plugin.ChatGui.PrintError("[Character Select+] No target selected.");
                return;
            }

            var targetInfo = new { ObjectIndex = target.ObjectIndex, ObjectKind = target.ObjectKind, Name = target.Name?.ToString() ?? "Unknown" };
            var designIndex = selectedDesignIndex >= 0 && selectedDesignIndex < character.Designs.Count ? selectedDesignIndex : -1;

            _ = Task.Run(async () =>
            {
                try
                {
                    await plugin.ApplyToTarget(character, -1);
                    Plugin.Log.Information($"[QuickSwitch] Applied character {character.Name} to target: {targetInfo.Name}");

                    if (designIndex >= 0)
                    {
                        await plugin.ApplyToTarget(character, designIndex);
                        Plugin.Log.Information($"[QuickSwitch] Applied design '{character.Designs[designIndex].Name}' to target: {targetInfo.Name}");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"[QuickSwitch] Error applying to target: {ex}");
                }
            });
        }

        private void RevertToCurrentPlayerCharacter()
        {
            if (plugin.activeCharacter != null)
            {
                var matchingCharacterIndex = plugin.Characters.FindIndex(c => c.Name == plugin.activeCharacter.Name);
                if (matchingCharacterIndex >= 0)
                {
                    selectedCharacterIndex = matchingCharacterIndex;

                    if (plugin.Configuration.LastUsedDesignByCharacter.TryGetValue(plugin.activeCharacter.Name, out var lastDesignName))
                    {
                        var character = plugin.Characters[matchingCharacterIndex];
                        var designIndex = character.Designs.FindIndex(d => d.Name.Equals(lastDesignName, StringComparison.OrdinalIgnoreCase));
                        selectedDesignIndex = designIndex >= 0 ? designIndex : -1;
                        Plugin.Log.Information($"[QuickSwitch] Reverted to active character: {plugin.activeCharacter.Name} with design: {(designIndex >= 0 ? lastDesignName : "None")}");
                    }
                    else
                    {
                        selectedDesignIndex = -1;
                        Plugin.Log.Information($"[QuickSwitch] Reverted to active character: {plugin.activeCharacter.Name} (no design)");
                    }

                    userIsInteracting = false;
                    return;
                }
            }

            if (plugin.Characters.Count > 0)
            {
                selectedCharacterIndex = 0;
                selectedDesignIndex = -1;
                userIsInteracting = false;
                Plugin.Log.Information($"[QuickSwitch] No active character found, reverted to first character: {plugin.Characters[0].Name}");
            }
        }

        private bool ShouldUploadToServer(Character character)
        {
            var sharing = character.RPProfile?.Sharing ?? ProfileSharing.AlwaysShare;

            if (sharing == ProfileSharing.NeverShare)
            {
                Plugin.Log.Debug($"[QuickSwitch-ShouldUpload] NeverShare - not uploading {character.Name}");
                return false;
            }

            Plugin.Log.Debug($"[QuickSwitch-ShouldUpload] ✓ {sharing} - uploading {character.Name}");
            return true;
        }

        private ProfileSharing GetEffectiveSharingForUpload(Character character, string currentPhysicalCharacter)
        {
            var sharing = character.RPProfile?.Sharing ?? ProfileSharing.AlwaysShare;

            if (sharing != ProfileSharing.ShowcasePublic)
                return sharing;

            var userMain = plugin.Configuration.GalleryMainCharacter;
            bool onMainCharacter = !string.IsNullOrEmpty(userMain) && currentPhysicalCharacter == userMain;

            if (onMainCharacter)
            {
                Plugin.Log.Debug($"[QuickSwitch-Sharing] ShowcasePublic on Main Character - will appear in Gallery");
                return ProfileSharing.ShowcasePublic;
            }
            else
            {
                Plugin.Log.Debug($"[QuickSwitch-Sharing] ShowcasePublic but not on Main Character - sending as AlwaysShare");
                return ProfileSharing.AlwaysShare;
            }
        }

        private (float width, float height) CalculateImageDimensions(Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap texture, float maxSize)
        {
            float originalWidth = texture.Width;
            float originalHeight = texture.Height;
            float aspectRatio = originalWidth / originalHeight;

            if (aspectRatio > 1) // Landscape
            {
                return (maxSize, maxSize / aspectRatio);
            }
            else // Portrait or Square
            {
                return (maxSize * aspectRatio, maxSize);
            }
        }

        private void UpdateSelectedDesignFromConfig(Character character)
        {
            if (plugin.Configuration.LastUsedDesignByCharacter.TryGetValue(character.Name, out var lastUsedDesignName))
            {
                if (lastUsedDesignName != lastTrackedDesignName)
                {
                    userIsInteracting = false;
                    lastTrackedDesignName = lastUsedDesignName;

                    var activeDesign = character.Designs.FirstOrDefault(d => d.Name.Equals(lastUsedDesignName, StringComparison.OrdinalIgnoreCase));
                    selectedDesignIndex = activeDesign != null ? character.Designs.IndexOf(activeDesign) : -1;
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(lastTrackedDesignName))
                {
                    userIsInteracting = false;
                    lastTrackedDesignName = "";
                    selectedDesignIndex = -1;
                }
            }
        }
    }
}

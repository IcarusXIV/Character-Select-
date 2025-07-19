using System;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Dalamud.Interface.Windowing;

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
            // Initialize selection on first draw or when characters are available
            if (!hasInitializedSelection && plugin.Characters.Count > 0)
            {
                InitializeLastUsedSelection();
                hasInitializedSelection = true;
            }

            // Compact Quick Switch toggle
            if (plugin.Configuration.QuickSwitchCompact)
            {
                // No title‐bar, no resize, no scrollbar, no background
                this.Flags = ImGuiWindowFlags
                    .NoTitleBar
                    | ImGuiWindowFlags.NoResize
                    | ImGuiWindowFlags.NoScrollbar
                    | ImGuiWindowFlags.NoScrollWithMouse
                    | ImGuiWindowFlags.NoBackground;
                SizeConstraints = new WindowSizeConstraints
                {
                    MinimumSize = new System.Numerics.Vector2(360, 28),
                    MaximumSize = new System.Numerics.Vector2(360, 28),
                };
            }
            else
            {
                // Full window
                this.Flags = ImGuiWindowFlags.NoResize
                           | ImGuiWindowFlags.NoScrollbar
                           | ImGuiWindowFlags.NoScrollWithMouse;
                SizeConstraints = new WindowSizeConstraints
                {
                    MinimumSize = new System.Numerics.Vector2(360, 55),
                    MaximumSize = new System.Numerics.Vector2(360, 58),
                };
            }

            float dropdownWidth = 135;
            float spacing = 6;

            // Character Dropdown
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
                            var ordered = character.Designs
                                .Select((d, index) => new { Design = d, Index = index })
                                .OrderBy(x => x.Design.Name, StringComparer.OrdinalIgnoreCase)
                                .ToList();

                            selectedDesignIndex = ordered[0].Index;
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

            // Design Dropdown
            if (selectedCharacterIndex >= 0 && selectedCharacterIndex < plugin.Characters.Count)
            {
                var selectedCharacter = plugin.Characters[selectedCharacterIndex];
                int tempDesignIndex = selectedDesignIndex;

                ImGui.SetNextItemWidth(dropdownWidth);
                if (ImGui.BeginCombo("##DesignDropdown", GetSelectedDesignName(selectedCharacter), ImGuiComboFlags.HeightRegular))
                {
                    var orderedDesigns = selectedCharacter.Designs
                        .Select((d, index) => new { Design = d, OriginalIndex = index })
                        .OrderBy(x => x.Design.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    for (int j = 0; j < orderedDesigns.Count; j++)
                    {
                        var entry = orderedDesigns[j];
                        bool isSelected = (tempDesignIndex == entry.OriginalIndex);

                        if (ImGui.Selectable(entry.Design.Name, isSelected))
                        {
                            tempDesignIndex = entry.OriginalIndex; // store original index to stay consistent
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

            // Apply Button
            if (selectedCharacterIndex >= 0)
            {
                if (ImGui.Button("Apply", new Vector2(50, ImGui.GetFrameHeight())))
                {
                    ApplySelection();
                }
            }
            else
            {
                ImGui.BeginDisabled();
                ImGui.Button("Apply", new Vector2(50, ImGui.GetFrameHeight()));
                ImGui.EndDisabled();
            }

            // Nameplate Colour Bar
            if (selectedCharacterIndex >= 0)
            {
                Vector4 charColor = GetNameplateColor(plugin.Characters[selectedCharacterIndex]);

                ImGui.PushStyleColor(ImGuiCol.ChildBg, charColor);
                ImGui.BeginChild("ColorBar", new Vector2(ImGui.GetContentRegionAvail().X, 3), false);
                ImGui.EndChild();
                ImGui.PopStyleColor();
            }
        }

        // Initialize the dropdown selections based on last used character
        private void InitializeLastUsedSelection()
        {
            try
            {
                Plugin.Log.Debug("[QuickSwitch] Initializing last used selection...");

                // Try to get the last used character for the current player
                if (Plugin.ClientState.LocalPlayer?.HomeWorld.IsValid == true)
                {
                    string localName = Plugin.ClientState.LocalPlayer.Name.TextValue;
                    string worldName = Plugin.ClientState.LocalPlayer.HomeWorld.Value.Name.ToString();
                    string fullKey = $"{localName}@{worldName}";

                    if (plugin.Configuration.LastUsedCharacterByPlayer.TryGetValue(fullKey, out var lastUsedKey))
                    {
                        // Find character by the stored key format
                        var character = plugin.Characters.FirstOrDefault(c =>
                            $"{c.Name}@{worldName}" == lastUsedKey);

                        if (character != null)
                        {
                            selectedCharacterIndex = plugin.Characters.IndexOf(character);
                            Plugin.Log.Debug($"[QuickSwitch] Found last used character: {character.Name} at index {selectedCharacterIndex}");

                            // Try to set last used design for this character
                            if (plugin.Configuration.LastUsedDesignByCharacter.TryGetValue(character.Name, out var lastDesignName))
                            {
                                var design = character.Designs.FirstOrDefault(d => d.Name == lastDesignName);
                                if (design != null)
                                {
                                    selectedDesignIndex = character.Designs.IndexOf(design);
                                    Plugin.Log.Debug($"[QuickSwitch] Found last used design: {lastDesignName} at index {selectedDesignIndex}");
                                }
                            }
                            return;
                        }
                    }
                }

                // Try the global last used character key
                if (!string.IsNullOrEmpty(plugin.Configuration.LastUsedCharacterKey))
                {
                    var character = plugin.Characters.FirstOrDefault(c => c.Name == plugin.Configuration.LastUsedCharacterKey);
                    if (character != null)
                    {
                        selectedCharacterIndex = plugin.Characters.IndexOf(character);
                        Plugin.Log.Debug($"[QuickSwitch] Found global last used character: {character.Name} at index {selectedCharacterIndex}");

                        // Also try to get last design for this character
                        if (plugin.Configuration.LastUsedDesignByCharacter.TryGetValue(character.Name, out var lastDesignName))
                        {
                            var design = character.Designs.FirstOrDefault(d => d.Name == lastDesignName);
                            if (design != null)
                            {
                                selectedDesignIndex = character.Designs.IndexOf(design);
                                Plugin.Log.Debug($"[QuickSwitch] Found last used design for global character: {lastDesignName} at index {selectedDesignIndex}");
                            }
                        }
                        return;
                    }
                }

                //  Try main character if set
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

                // Default to first character
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
                // Fallback to first character if anything goes wrong
                if (plugin.Characters.Count > 0)
                {
                    selectedCharacterIndex = 0;
                }
            }
        }

        // Public method to refresh the selection
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

                // Try to maintain the last used design for this character
                if (plugin.Configuration.LastUsedDesignByCharacter.TryGetValue(character.Name, out var lastDesignName))
                {
                    var design = character.Designs.FirstOrDefault(d => d.Name == lastDesignName);
                    if (design != null)
                    {
                        selectedDesignIndex = character.Designs.IndexOf(design);
                    }
                    else
                    {
                        selectedDesignIndex = -1;
                    }
                }
                else
                {
                    selectedDesignIndex = -1;
                }

                Plugin.Log.Debug($"[QuickSwitch] Updated selection to character: {character.Name} (index {selectedCharacterIndex})");
            }
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

            plugin.ExecuteMacro(character.Macros, character, null);
            plugin.SetActiveCharacter(character);

            if (Plugin.ClientState.LocalPlayer is { } player && player.HomeWorld.IsValid)
            {
                string localName = player.Name.TextValue;
                string worldName = player.HomeWorld.Value.Name.ToString();
                string fullKey = $"{localName}@{worldName}";

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
                        CharacterName = character.Name,
                        NameplateColor = character.RPProfile?.ProfileColor ?? character.NameplateColor,
                        BackgroundImage = character.BackgroundImage,
                        Effects = character.Effects ?? new ProfileEffects(),
                        GalleryStatus = character.GalleryStatus,
                        Links = character.RPProfile?.Links,
                        LastActiveTime = plugin.Configuration.ShowRecentlyActiveStatus ? DateTime.UtcNow : null
                    };

                    _ = Plugin.UploadProfileAsync(profileToSend, character.LastInGameName ?? character.Name);
                    Plugin.Log.Info($"[QuickSwitch] ✓ Uploaded profile for {character.Name}");
                }
                else
                {
                    Plugin.Log.Info($"[QuickSwitch] ⚠ Skipped gallery upload for {character.Name} (not on main character or not public)");
                }
            }

            // Always apply the design if selected
            if (selectedDesignIndex >= 0 && selectedDesignIndex < character.Designs.Count)
            {
                plugin.ExecuteMacro(character.Designs[selectedDesignIndex].Macro);
                plugin.Configuration.LastUsedDesignByCharacter[character.Name] = character.Designs[selectedDesignIndex].Name;
                plugin.Configuration.LastUsedDesignCharacterKey = character.Name;
                plugin.Configuration.Save();
            }

            // Always apply idle pose if a valid one is set
            if (character.IdlePoseIndex < 7)
                plugin.PoseRestorer.RestorePosesFor(character);
            else
                Plugin.Log.Debug("[QuickSwitch] Skipping idle pose restore — IdlePoseIndex is None.");
        }

        private bool ShouldUploadToGallery(Character character, string currentPhysicalCharacter)
        {
            // Is there a main character set?
            var userMain = plugin.Configuration.GalleryMainCharacter;
            if (string.IsNullOrEmpty(userMain))
            {
                Plugin.Log.Debug($"[QuickSwitch-ShouldUpload] No main character set - not uploading {character.Name}");
                return false;
            }

            // Are we currently on the main character?
            if (currentPhysicalCharacter != userMain)
            {
                Plugin.Log.Debug($"[QuickSwitch-ShouldUpload] Current character '{currentPhysicalCharacter}' != main '{userMain}' - not uploading {character.Name}");
                return false;
            }

            // Is this CS+ character set to public sharing?
            var sharing = character.RPProfile?.Sharing ?? ProfileSharing.AlwaysShare;
            if (sharing != ProfileSharing.ShowcasePublic)
            {
                Plugin.Log.Debug($"[QuickSwitch-ShouldUpload] Character '{character.Name}' sharing is '{sharing}' (not public) - not uploading");
                return false;
            }

            Plugin.Log.Debug($"[QuickSwitch-ShouldUpload] ✓ All checks passed - will upload {character.Name} as {currentPhysicalCharacter}");
            return true;
        }
    }
}

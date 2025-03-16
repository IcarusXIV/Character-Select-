using System;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;

namespace CharacterSelectPlugin.Windows
{
    public class QuickSwitchWindow : Window
    {
        private readonly Plugin plugin;
        private int selectedCharacterIndex = -1;
        private int selectedDesignIndex = -1;

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
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(360, 55),
                MaximumSize = new Vector2(360, 58)
            };

            float dropdownWidth = 135;
            float spacing = 6;

            // 🔹 Character Dropdown
            ImGui.SetNextItemWidth(dropdownWidth);
            int tempCharacterIndex = selectedCharacterIndex;

            if (ImGui.BeginCombo("##CharacterDropdown", GetSelectedCharacterName(), ImGuiComboFlags.HeightLargest))
            {
                for (int i = 0; i < plugin.Characters.Count; i++)
                {
                    var character = plugin.Characters[i];
                    bool isSelected = (tempCharacterIndex == i);

                    if (ImGui.Selectable(character.Name, isSelected))
                    {
                        tempCharacterIndex = i;
                        selectedDesignIndex = -1;
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            selectedCharacterIndex = tempCharacterIndex;

            ImGui.SameLine(0, spacing);

            // 🔹 Design Dropdown
            if (selectedCharacterIndex >= 0 && selectedCharacterIndex < plugin.Characters.Count)
            {
                var selectedCharacter = plugin.Characters[selectedCharacterIndex];
                int tempDesignIndex = selectedDesignIndex;

                ImGui.SetNextItemWidth(dropdownWidth);
                if (ImGui.BeginCombo("##DesignDropdown", GetSelectedDesignName(selectedCharacter), ImGuiComboFlags.HeightLargest))
                {
                    for (int i = 0; i < selectedCharacter.Designs.Count; i++)
                    {
                        bool isSelected = (tempDesignIndex == i);

                        if (ImGui.Selectable(selectedCharacter.Designs[i].Name, isSelected))
                        {
                            tempDesignIndex = i;
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

            // 🔹 Apply Button
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

            // 🔹 Nameplate Color Bar (Appears below dropdowns if a character is selected)
            if (selectedCharacterIndex >= 0)
            {
                Vector4 charColor = GetNameplateColor(plugin.Characters[selectedCharacterIndex]);

                ImGui.PushStyleColor(ImGuiCol.ChildBg, charColor);
                ImGui.BeginChild("ColorBar", new Vector2(ImGui.GetContentRegionAvail().X, 3), false);
                ImGui.EndChild();
                ImGui.PopStyleColor();
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
            // Calculate luminance (brightness perception)
            float brightness = (0.299f * bgColor.X + 0.587f * bgColor.Y + 0.114f * bgColor.Z);
            return brightness > 0.5f ? new Vector4(0, 0, 0, 1) : new Vector4(1, 1, 1, 1); // Black for bright backgrounds, white for dark
        }


        private void ApplySelection()
        {
            if (selectedCharacterIndex >= 0 && selectedCharacterIndex < plugin.Characters.Count)
            {
                var character = plugin.Characters[selectedCharacterIndex];

                if (selectedDesignIndex >= 0 && selectedDesignIndex < character.Designs.Count)
                {
                    plugin.ExecuteMacro(character.Designs[selectedDesignIndex].Macro);
                }
                else
                {
                    plugin.ExecuteMacro(character.Macros);
                }
            }
        }
    }
}

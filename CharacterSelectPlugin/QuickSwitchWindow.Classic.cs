using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin.Windows
{
    public partial class QuickSwitchWindow
    {
        private void DrawClassicLayout()
        {
            float scale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;

            RespectCloseHotkey = !plugin.Configuration.QuickSwitchIgnoreEscape;

            int themeColorCount = ThemeHelper.PushThemeColors(plugin.Configuration);
            int themeStyleVarCount = ThemeHelper.PushThemeStyleVars(plugin.Configuration.UIScaleMultiplier);

            try
            {
                if (!hasInitializedSelection && plugin.Characters.Count > 0)
                {
                    InitializeLastUsedSelection();
                    hasInitializedSelection = true;
                }

                var baseFlags = plugin.Configuration.QuickSwitchCompact
                    ? ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground
                    : ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

                if (plugin.Configuration.QuickSwitchIgnoreEscape)
                    baseFlags |= ImGuiWindowFlags.NoFocusOnAppearing;

                this.Flags = baseFlags;

                if (plugin.Configuration.QuickSwitchCompact)
                {
                    SizeConstraints = new Dalamud.Interface.Windowing.WindowSizeConstraints
                    {
                        MinimumSize = new Vector2(360 * scale, 28 * scale),
                        MaximumSize = new Vector2(360 * scale, 28 * scale),
                    };

                    float buttonOpacity = 1.0f;
                    if (plugin.Configuration.SelectedTheme == ThemeSelection.Custom)
                    {
                        buttonOpacity = plugin.Configuration.CustomTheme.CompactQuickSwitchButtonOpacity;
                    }

                    ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.16f, 0.16f, 0.16f, buttonOpacity));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.22f, 0.22f, 0.22f, buttonOpacity));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.28f, 0.28f, 0.28f, buttonOpacity));
                    ImGui.PushStyleColor(ImGuiCol.FrameBg,        new Vector4(0.12f, 0.12f, 0.12f, buttonOpacity));
                    ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.18f, 0.18f, 0.18f, buttonOpacity));
                    ImGui.PushStyleColor(ImGuiCol.FrameBgActive,  new Vector4(0.22f, 0.22f, 0.22f, buttonOpacity));
                }
                else
                {
                    SizeConstraints = new Dalamud.Interface.Windowing.WindowSizeConstraints
                    {
                        MinimumSize = new Vector2(360 * scale, 55 * scale),
                        MaximumSize = new Vector2(360 * scale, 58 * scale),
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
                    UIStyles.ApplyHoverSheenToLastItemStatic("quickswitch_apply_btn");

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
                if (plugin.Configuration.QuickSwitchCompact)
                {
                    ImGui.PopStyleColor(6);
                }
                ThemeHelper.PopThemeStyleVars(themeStyleVarCount);
                ThemeHelper.PopThemeColors(themeColorCount);
            }
        }
    }
}

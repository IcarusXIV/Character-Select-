using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Windows.Forms;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.ImGuiSeStringRenderer;
using CharacterSelectPlugin.Windows.Styles;
using CharacterSelectPlugin.Windows.Utils;
using SeString = Dalamud.Game.Text.SeStringHandling.SeString;
using SeStringBuilder = Lumina.Text.SeStringBuilder;
using DalamudSeStringBuilder = Dalamud.Game.Text.SeStringHandling.SeStringBuilder;

namespace CharacterSelectPlugin.Windows.Components
{
    public class CharacterForm : IDisposable
    {
        private Plugin plugin;
        private UIStyles uiStyles;

        // Form state
        public bool IsEditWindowOpen { get; private set; } = false;
        private int selectedCharacterIndex = -1;
        private bool isSecretMode = false;
        private bool isAdvancedModeCharacter = false;
        private string? pendingImagePath = null;

        // Edit fields
        private string editedCharacterName = "";
        private string originalCharacterName = ""; // Track original name for warning resolution
        private string editedCharacterMacros = "";
        private string? editedCharacterImagePath = null;
        private string nameValidationError = "";
        private Vector3 editedCharacterColor = new Vector3(1.0f, 1.0f, 1.0f);
        private string editedCharacterPenumbra = "";
        private string editedCharacterGlamourer = "";
        private string editedCharacterCustomize = "";
        private string editedCharacterTag = "";
        private string editedCharacterAutomation = "";
        private string editedCharacterMoodlePreset = "";
        private int? editedCharacterGearset = null;
        private bool editedCharacterExcludeFromNameSync = false;

        // Honorific fields
        private string editedCharacterHonorificTitle = "";
        private string editedCharacterHonorificPrefix = "Prefix";
        private string editedCharacterHonorificSuffix = "Suffix";
        private Vector3 editedCharacterHonorificColor = new Vector3(1.0f, 1.0f, 1.0f);
        private Vector3 editedCharacterHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f);
        private int? editedCharacterHonorificGradientSet = null;
        private string? editedCharacterHonorificAnimationStyle = null;

        // Temp fields for live updates
        private string tempHonorificTitle = "";
        private string tempHonorificPrefix = "Prefix";
        private string tempHonorificSuffix = "Suffix";
        private Vector3 tempHonorificColor = new Vector3(1.0f, 1.0f, 1.0f);
        private Vector3 tempHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f);
        private int? tempHonorificGradientSet = null;
        private string? tempHonorificAnimationStyle = null;
        private string tempMoodlePreset = "";

        // Gradient preset names and data from Honorific (exact base64 encoded color arrays)
        private static readonly string[] GradientPresetNames = new[]
        {
            "Pride Rainbow", "Transgender", "Lesbian", "Bisexual",
            "Black & White", "Black & Red", "Black & Blue", "Black & Yellow",
            "Black & Green", "Black & Pink", "Black & Cyan", "Cherry Blossom",
            "Golden", "Pastel Rainbow", "Dark Rainbow", "Non-binary"
        };

        private static readonly string[] GradientPresetData = new[]
        {
            "5AMD6RsC7TMC8ksB92MB/HsA/5EA/6IA/7IA/8MA/9QA/+UA5+MEutAKjr0RYaoYNZYeCIMlAHlFAG9rAGaRAF23AFTdAkv9FkXnKj/RPjm8UjOmZi2QcymCcymCcymCcymCcymCcymCZi2QUjOmPjm8Kj/RFkXnAkv9AFTdAF23AGaRAG9rAHlFCIMlNZYeYaoYjr0RutAK5+ME/+UA/9QA/8MA/7IA/6IA/5EA/HsA92MB8ksB7TMC6RsC5AMD", // Pride Rainbow
            "W876b8nygsXplsDhqbvYvbfQ0LLI5K2/9aq59rXC+MDL+cvU+tbd/OHm/ezv/vf4//z9/fH0/Obr+9zi+tHZ+MbQ97vH9rC+7qu72q/Ex7TMs7nUn77djMLleMftZcz2Zcz2eMftjMLln77ds7nUx7TM2q/E7qu99rC+97vH+MbQ+tHZ+9zi/Obr/fH0//z9/vf4/ezv/OHm+tbd+cvU+MDL9rXC9aq55K2/0LLIvbfQqbvYlsDhgsXpb8nyW876", // Transgender
            "1S0A2lQT33ol46E46MdL7e5d8Opg9Nhe98Zc+rVZ/aNX/6Rm/7eG/8qm/93H//Hn/fj79Nnp67vY4p3G2n+10WKkzGCgxl2cwVuZvFmVtleRskqJrzqBrCp4qBpvpQpmpQpmqBpvrCp4rzqBskqJtleRvFmVwVuZxl2czGCg0WKk2oC1457H67zY9Nrp/fj7//Hn/93H/8qm/7eG/6Rm/aNX+rVZ98dc9Nhe8Opg7exd6MZK46A43nkl2lMT1S0A", // Lesbian
            "1gJwzgx1xxZ6vyB/uCmDsDOIqT2NoUeSm0+Wm0+Wm0+Wm0+WlU6XgUuZbUibWUWeRkKgMj+iHjylCjmnCjmnHjylMj+iRkKgWUWebUibgUuZlU6Xm0+Wm0+Wm0+Wm0+WoUeSqT2NsDOIuCmDvyB/xxZ6zgx11gJw", // Bisexual
            "////9/f37+/v5+fn39/f19fXzs7OxsbGvr6+tra2rq6upqamnp6elpaWjo6OhoaGfX19dXV1bW1tZWVlXV1dVVVVTU1NRUVFPT09NTU1LS0tJCQkHBwcFBQUDAwMBAQEBAQEDAwMFBQUHBwcJCQkLS0tNTU1PT09RUVFTU1NVVVVXV1dZWVlbW1tdXV1fX19hoaGjo6OlpaWnp6epqamrq6utra2vr6+xsbGzs7O19fX39/f5+fn7+/v9/f3////", // Black & White
            "/wAA9QAA6wAA4QAA1wAAzAAAwgAAuAAArgAApAAAmgAAkAAAhgAAewAAcQAAZwAAXQAAUwAASQAAPwAANQAAKwAAIAAAFgAADAAAAgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgAADAAAFgAAIAAAKgAANQAAPwAASQAAUwAAXQAAZwAAcQAAewAAhgAAkAAAmgAApAAArgAAuAAAwgAAzAAA1wAA4QAA6wAA9QAA/wAA", // Black & Red
            "AAD/AAD1AADrAADhAADXAADMAADCAAC4AACuAACkAACaAACQAACGAAB7AABxAABnAABdAABTAABJAAA/AAA1AAArAAAgAAAWAAAMAAACAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAAAMAAAWAAAgAAAqAAA1AAA/AABJAABTAABdAABnAABxAAB7AACGAACQAACaAACkAACuAAC4AADCAADMAADXAADhAADrAAD1AAD/", // Black & Blue
            "//8A9fUA6+sA4eEA19cAzMwAwsIAuLgArq4ApKQAmpoAkJAAhoYAe3sAcXEAZ2cAXV0AU1MASUkAPz8ANTUAKysAICAAFhYADAwAAgIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgIADAwAFhYAICAAKioANTUAPz8ASUkAU1MAXV0AZ2cAcXEAe3sAhoYAkJAAmpoApKQArq4AuLgAwsIAzMwA19cA4eEA6+sA9fUA//8A", // Black & Yellow
            "AP8AAPUAAOsAAOEAANcAAMwAAMIAALgAAK4AAKQAAJoAAJAAAIYAAHsAAHEAAGcAAF0AAFMAAEkAAD8AADUAACsAACAAABYAAAwAAAIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIAAAwAABYAACAAACoAADUAAD8AAEkAAFMAAF0AAGcAAHEAAHsAAIYAAJAAAJoAAKQAAK4AALgAAMIAAMwAANcAAOEAAOsAAPUAAP8A", // Black & Green
            "/wD/9QD16wDr4QDh1wDXzADMwgDCuAC4rgCupACkmgCakACQhgCGewB7cQBxZwBnXQBdUwBTSQBJPwA/NQA1KwArIAAgFgAWDAAMAgACAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgACDAAMFgAWIAAgKgAqNQA1PwA/SQBJUwBTXQBdZwBncQBxewB7hgCGkACQmgCapACkrgCuuAC4wgDCzADM1wDX4QDh6wDr9QD1/wD/", // Black & Pink
            "AP//APX1AOvrAOHhANfXAMzMAMLCALi4AK6uAKSkAJqaAJCQAIaGAHt7AHFxAGdnAF1dAFNTAElJAD8/ADU1ACsrACAgABYWAAwMAAICAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAICAAwMABYWACAgACoqADU1AD8/AElJAFNTAF1dAGdnAHFxAHt7AIaGAJCQAJqaAKSkAK6uALi4AMLCAMzMANfXAOHhAOvrAPX1AP//", // Black & Cyan
            "7s7/7Mj36sPv573n5bje47LW4azO3qbG3KC+2pq32pW13pG84YzD5YjK6ITR7H/Y8Hvg83bm93Lt+m30/Wn5/Wf3+Wbt9GXj72Ta6mPQ5mLH4mK+3mG02mCq1l+h0l6Yzl2Oyl2GymCEzmaI0WyM1HOP13mT2n6X34Sb4oqf5ZCi6Jam65up76Gt8qex9a60+bS4/Lq8/r/A/cHF+8LK+sPQ+cXV98bb9sfg9Mjm88rs8svy8Mz378387s7/7s7/", // Cherry Blossom
            "/5IA/5QE/5YI/5kL/5sP/50T/58X/6Eb/6Mf/6Yj/6gn/6or/6wv/68z/7E2/7M6/7Y+/7hC/7pG/71J/79N/8FR/8NV/8VZ/8dd/8ph/8xl/85p/9Jz/9mJ/+Cl/+a1/+uu/+2c/++L/+2D/+p+/+Z5/+N0/+Bw/9xr/9lm/9Vh/9Jc/89X/8tS/8hN/8VI/8FE/74//7s6/7c1/7Qx/7As/60n/6oi/6Yd/6MY/58T/5wO/5kK/5UF/5IA/5IA", // Golden
            "/7y8/8K8/8i8/868/9S8/9q8/+G8/+e8/+28//O8//m8/v68+f+88/+87f+86P+84f+82/+81f+8z/+8yf+8w/+8vf+8vP/BvP/HvP/NvP/TvP/avP/gvP/mvP/svP/yvP/4vP//vPn/vPP/vOz/vOX/vN//vNj/vNL/vMz/vMX/vL//v7z/xrz/zLz/0rz/2rz/4Lz/5rz/7bz/87z/+rz//7z+/7z4/7zx/7zr/7zk/7ze/7zX/7zR/7zK/7y8", // Pastel Rainbow
            "MgAAMgUAMgkAMg4AMhIAMhcAMhsAMiAAMiUAMioAMi4AMTIALTIAKDIAJDIAHzIAGjIAFTIAETIADDIABzIAAzIAADICADIGADILADIQADIUADIZADIeADIiADInADIrADIwAC8yACsyACYyACEyABwyABgyABMyAA0yAAkyAAQyAQEyBQAyCgAyDwAyEwAyGQAyHgAyIgAyJwAyLAAyMQAyMgAvMgAqMgAlMgAgMgAbMgAWMgASMgANMgAAMgAA", // Dark Rainbow
            "//Qz//VK//Zg//h3//mO//qk//u7//3S//7o////9O366dr13sjv07Xqx6PlvJDgsX7apmvVm1nQik+5eUWiZzuLVjF0RShcNB5FIhQuEQoXAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEQoXIhQuNB5FRShcVjF0ZzuLeUWiik+5m1nQpmvVsX7avJDgx6Pl07Xq3sjv6dr19O36//////7o//3S//u7//qk//mO//h3//Zg//VK//Qz"  // Non-binary
        };

        // Decoded gradient color arrays (lazy initialized)
        private static byte[][,]? _decodedGradients = null;
        private static byte[][,] DecodedGradients
        {
            get
            {
                if (_decodedGradients == null)
                {
                    _decodedGradients = new byte[GradientPresetData.Length][,];
                    for (int i = 0; i < GradientPresetData.Length; i++)
                    {
                        var arr = Convert.FromBase64String(GradientPresetData[i]);
                        var arr2 = new byte[arr.Length / 3, 3];
                        for (var j = 0; j < arr.Length; j += 3)
                        {
                            arr2[j / 3, 0] = arr[j];
                            arr2[j / 3, 1] = arr[j + 1];
                            arr2[j / 3, 2] = arr[j + 2];
                        }
                        _decodedGradients[i] = arr2;
                    }
                }
                return _decodedGradients;
            }
        }

        // Animation timer for preview
        private static readonly System.Diagnostics.Stopwatch AnimationTimer = System.Diagnostics.Stopwatch.StartNew();
        private string advancedCharacterMacroText = "";

        public CharacterForm(Plugin plugin, UIStyles uiStyles)
        {
            this.plugin = plugin;
            this.uiStyles = uiStyles;
        }

        public void Dispose()
        {
        }

        public void Draw()
        {
            if (!plugin.IsAddCharacterWindowOpen && !IsEditWindowOpen)
                return;

            var totalScale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);

            // Check if Conflict Resolution is enabled and determine secret mode
            if (plugin.Configuration.EnableConflictResolution)
            {
                // Check if plugin.IsSecretMode is set (from Ctrl+Shift+Edit or Add)
                if (plugin.IsSecretMode && !isSecretMode)
                {
                    isSecretMode = true;
                    plugin.IsSecretMode = false; // Reset the flag
                }
                
                // For editing existing characters, check if they already have secret mode data
                if (IsEditWindowOpen && selectedCharacterIndex >= 0 && selectedCharacterIndex < plugin.Characters.Count)
                {
                    var character = plugin.Characters[selectedCharacterIndex];
                    bool hasSecretModeData = character.SecretModState != null || 
                                           (character.Designs?.Any(d => d.SecretModState != null) == true);
                    
                    if (hasSecretModeData && !isSecretMode)
                    {
                        isSecretMode = true;
                    }
                }
                
                if (!IsEditWindowOpen && isSecretMode)
                {
                    plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                }
            }

            uiStyles.PushFormStyle();

            try
            {
                float baseLines = 26f;
                if (isAdvancedModeCharacter)
                    baseLines += 6f;

                float maxContentHeight = ImGui.GetTextLineHeightWithSpacing() * baseLines;
                float availableHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() * 2.5f;
                float scrollHeight = Math.Min(maxContentHeight, availableHeight);

                ImGui.BeginChild("CharacterFormScrollable", new Vector2(0, scrollHeight), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);
                DrawCharacterFormContent(totalScale);
                ImGui.EndChild();
            }
            finally
            {
                uiStyles.PopFormStyle();
            }
        }

        private void DrawCharacterFormContent(float scale)
        {
            float labelWidth = 130 * scale;
            float inputWidth = 250 * scale;
            float inputOffset = 10 * scale;

            string tempName = IsEditWindowOpen ? editedCharacterName : plugin.NewCharacterName;
            string tempMacros = IsEditWindowOpen ? editedCharacterMacros : plugin.NewCharacterMacros;
            string? imagePath = IsEditWindowOpen ? editedCharacterImagePath : plugin.NewCharacterImagePath;
            string tempPenumbra = IsEditWindowOpen ? editedCharacterPenumbra : plugin.NewPenumbraCollection;
            string tempGlamourer = IsEditWindowOpen ? editedCharacterGlamourer : plugin.NewGlamourerDesign;
            string tempCustomize = IsEditWindowOpen ? editedCharacterCustomize : plugin.NewCustomizeProfile;
            Vector3 tempColor = IsEditWindowOpen ? editedCharacterColor : plugin.NewCharacterColor;
            string tempTag = IsEditWindowOpen ? editedCharacterTag : plugin.NewCharacterTag;

            // Character Name
            DrawFormField("Character Name*", labelWidth, inputWidth, inputOffset, () =>
            {
                // Show red border if there's a validation error
                if (!string.IsNullOrEmpty(nameValidationError))
                {
                    ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 2.0f);
                }
                
                ImGui.InputText("##CharacterName", ref tempName, 50);
                plugin.CharacterNameFieldPos = ImGui.GetItemRectMin();
                plugin.CharacterNameFieldSize = ImGui.GetItemRectSize();

                if (!string.IsNullOrEmpty(nameValidationError))
                {
                    ImGui.PopStyleColor();
                    ImGui.PopStyleVar();
                    
                    // Show error message
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.3f, 0.3f, 1.0f));
                    ImGui.TextWrapped(nameValidationError);
                    ImGui.PopStyleColor();
                }

                if (IsEditWindowOpen) editedCharacterName = tempName;
                else plugin.NewCharacterName = tempName;

                // Validate name on change
                ValidateCharacterName(tempName);
            }, "Enter your OC's name or nickname for profile here.", scale,
            // Name Sync exclusion checkbox - after tooltip, only show if Name Sync sharing is enabled
            plugin.Configuration.AllowOthersToSeeMyCSName ? () =>
            {
                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 4 * scale); // Small gap
                bool tempExclude = IsEditWindowOpen ? editedCharacterExcludeFromNameSync : false;
                if (ImGui.Checkbox("Exclude from Name Sync", ref tempExclude))
                {
                    if (IsEditWindowOpen) editedCharacterExcludeFromNameSync = tempExclude;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("When checked, Name Sync won't apply to this character.");
                }
            } : null);

            ImGui.Separator();

            // Character Tags
            DrawFormField("Character Tags", labelWidth, inputWidth, inputOffset, () =>
            {
                ImGui.InputTextWithHint("##Tags", "e.g. Casual, Battle, Beach", ref tempTag, 100);

                if (IsEditWindowOpen) editedCharacterTag = tempTag;
                else plugin.NewCharacterTag = tempTag;
            }, "You can assign multiple tags by separating them with commas.\nExamples: Casual, Favourites, Seasonal", scale);

            ImGui.Separator();

            // Nameplate Colour
            DrawFormField("Nameplate Color", labelWidth, inputWidth, inputOffset, () =>
            {
                ImGui.ColorEdit3("##NameplateColor", ref tempColor);

                if (IsEditWindowOpen) editedCharacterColor = tempColor;
                else plugin.NewCharacterColor = tempColor;
            }, "Affects your character's nameplate under their profile picture in Character Select+.", scale);

            ImGui.Separator();

            // Penumbra Collection
            DrawFormField("Penumbra Collection*", labelWidth, inputWidth, inputOffset, () =>
            {
                var penumbraOptions = plugin.IntegrationListProvider?.GetPenumbraCollections() ?? Array.Empty<string>();
                var currentPenumbra = plugin.IntegrationListProvider?.GetCurrentPenumbraCollection();
                string oldValue = tempPenumbra;

                if (AutocompleteCombo.Draw("##PenumbraCollection", ref tempPenumbra, penumbraOptions, inputWidth, "Select collection...", currentActive: currentPenumbra))
                {
                    plugin.PenumbraFieldPos = ImGui.GetItemRectMin();
                    plugin.PenumbraFieldSize = ImGui.GetItemRectSize();

                    if (IsEditWindowOpen)
                    {
                        editedCharacterPenumbra = tempPenumbra;
                        if (isAdvancedModeCharacter)
                        {
                            UpdateAdvancedMacroPenumbra(tempPenumbra);
                        }
                        else
                        {
                            editedCharacterMacros = GenerateMacro();
                        }
                    }
                    else
                    {
                        plugin.NewPenumbraCollection = tempPenumbra;
                        if (isAdvancedModeCharacter)
                        {
                            UpdateAdvancedMacroPenumbra(tempPenumbra);
                            plugin.NewCharacterMacros = advancedCharacterMacroText;
                        }
                        else
                        {
                            plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                        }
                    }
                }
                else
                {
                    // Still track position even when not changed
                    plugin.PenumbraFieldPos = ImGui.GetItemRectMin();
                    plugin.PenumbraFieldSize = ImGui.GetItemRectSize();
                }
            }, "Select the Penumbra collection for this character. Right-click to clear.", scale);

            ImGui.Separator();

            // Glamourer Design
            DrawFormField("Glamourer Design*", labelWidth, inputWidth, inputOffset, () =>
            {
                var glamourerOptions = plugin.IntegrationListProvider?.GetGlamourerDesigns() ?? Array.Empty<string>();
                string oldValue = tempGlamourer;

                if (AutocompleteCombo.Draw("##GlamourerDesign", ref tempGlamourer, glamourerOptions, inputWidth, "Select design..."))
                {
                    plugin.GlamourerFieldPos = ImGui.GetItemRectMin();
                    plugin.GlamourerFieldSize = ImGui.GetItemRectSize();

                    if (IsEditWindowOpen)
                    {
                        editedCharacterGlamourer = tempGlamourer;
                        if (isAdvancedModeCharacter)
                        {
                            UpdateAdvancedMacroGlamourer(oldValue, tempGlamourer);
                        }
                        else
                        {
                            editedCharacterMacros = GenerateMacro();
                        }
                    }
                    else
                    {
                        plugin.NewGlamourerDesign = tempGlamourer;
                        if (isAdvancedModeCharacter)
                        {
                            UpdateAdvancedMacroGlamourer(oldValue, tempGlamourer);
                            plugin.NewCharacterMacros = advancedCharacterMacroText;
                        }
                        else
                        {
                            plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                        }
                    }
                }
                else
                {
                    // Still track position even when not changed
                    plugin.GlamourerFieldPos = ImGui.GetItemRectMin();
                    plugin.GlamourerFieldSize = ImGui.GetItemRectSize();
                }
            }, "Select the Glamourer design for this character. Right-click to clear.\nYou can add additional designs later.", scale);

            ImGui.Separator();

            // Automation (if enabled)
            if (plugin.Configuration.EnableAutomations)
            {
                DrawAutomationField(labelWidth, inputWidth, inputOffset, scale);
                ImGui.Separator();
            }

            // Customize+ Profile
            DrawCustomizeField(labelWidth, inputWidth, inputOffset, scale);
            ImGui.Separator();

            // Honorific Section
            DrawHonorificSection(labelWidth, inputWidth, inputOffset, scale);
            ImGui.Separator();

            // Moodle Preset
            DrawMoodleField(labelWidth, inputWidth, inputOffset, scale);
            ImGui.Separator();

            // Idle Pose
            DrawIdlePoseField(labelWidth, inputWidth, inputOffset, scale);
            ImGui.Separator();

            // Assigned Gearset (only if enabled)
            if (plugin.Configuration.EnableGearsetAssignments)
            {
                DrawGearsetField(labelWidth, inputWidth, inputOffset, scale);
                ImGui.Separator();
            }

            // Mod Manager (Conflict Resolution)
            if (isSecretMode)
            {
                DrawSecretModeModsField(labelWidth, inputWidth, inputOffset, scale);
                ImGui.Separator();
            }

            // Image Selection
            DrawImageSelection(scale);
            ImGui.Separator();

            // Advanced Mode Toggle
            DrawAdvancedModeSection(scale);
            ImGui.Separator();

            // Buttons!
            DrawActionButtons(scale);
        }

        private void DrawFormField(string label, float labelWidth, float inputWidth, float inputOffset,
                                 System.Action drawInput, string tooltip, float scale, System.Action? afterTooltip = null)
        {
            ImGui.SetCursorPosX(10 * scale);
            ImGui.Text(label);
            ImGui.SameLine(labelWidth);
            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);

            drawInput();

            // Tooltip
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300 * scale);
                ImGui.TextUnformatted(tooltip);
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            // Optional content after tooltip
            afterTooltip?.Invoke();
        }

        private void DrawSecretModeModsField(float labelWidth, float inputWidth, float inputOffset, float scale)
        {
            DrawFormField("Mod Manager", labelWidth, inputWidth, inputOffset, () =>
            {
                var selectedCount = IsEditWindowOpen && plugin.Characters[selectedCharacterIndex].SecretModState != null
                    ? plugin.Characters[selectedCharacterIndex].SecretModState.Count
                    : (plugin.NewSecretModState?.Count ?? 0);
                
                var buttonText = selectedCount > 0 
                    ? $"Configure Mods ({selectedCount} selected)###SecretMods"
                    : "Configure Mods###SecretMods";
                
                // Validate that character name is filled before opening mod manager
                string characterName = IsEditWindowOpen ? editedCharacterName : plugin.NewCharacterName;
                bool hasValidName = !string.IsNullOrWhiteSpace(characterName);
                
                if (!hasValidName)
                    ImGui.BeginDisabled();
                
                if (ImGui.Button(buttonText, new Vector2(inputWidth, 0)))
                {
                    if (hasValidName)
                    {
                        // Open the Secret Mode mod selection window
                        if (plugin.SecretModeModWindow == null)
                        {
                            plugin.SecretModeModWindow = new SecretModeModWindow(plugin);
                            plugin.WindowSystem.AddWindow(plugin.SecretModeModWindow);
                        }
                        
                        Dictionary<string, bool>? currentSelection = null;
                        HashSet<string>? currentPins = null;
                        if (IsEditWindowOpen)
                        {
                            currentSelection = plugin.Characters[selectedCharacterIndex].SecretModState;
                            currentPins = plugin.Characters[selectedCharacterIndex].SecretModPins != null ? new HashSet<string>(plugin.Characters[selectedCharacterIndex].SecretModPins) : null;
                            Plugin.Log.Information($"[PIN DEBUG] Character form loading pins for character {selectedCharacterIndex}: {currentPins?.Count ?? 0} pins - {string.Join(", ", currentPins ?? new HashSet<string>())}");
                        }
                        else
                        {
                            currentSelection = plugin.NewSecretModState;
                            currentPins = plugin.NewSecretModPins != null ? new HashSet<string>(plugin.NewSecretModPins) : null;
                            Plugin.Log.Information($"[PIN DEBUG] Character form loading pins for new character: {currentPins?.Count ?? 0} pins - {string.Join(", ", currentPins ?? new HashSet<string>())}");
                        }
                        
                        Plugin.Log.Information($"[PIN DEBUG] About to pass pins to mod manager: {currentPins?.Count ?? 0} pins - {string.Join(", ", currentPins ?? new HashSet<string>())}");
                        plugin.SecretModeModWindow.Open(
                            IsEditWindowOpen ? selectedCharacterIndex : null,
                            currentSelection,
                            currentPins,
                            (selection) =>
                            {
                                if (IsEditWindowOpen)
                                {
                                    plugin.Characters[selectedCharacterIndex].SecretModState = selection;
                                    plugin.SaveConfiguration();
                                }
                                else
                                {
                                    plugin.NewSecretModState = selection;
                                }
                            },
                            (pins) =>
                            {
                                if (IsEditWindowOpen)
                                {
                                    Plugin.Log.Information($"[PIN DEBUG] Character save callback: saving {pins?.Count ?? 0} pins to character {selectedCharacterIndex}");
                                    plugin.Characters[selectedCharacterIndex].SecretModPins = pins?.ToList();
                                    plugin.SaveConfiguration();
                                }
                                else
                                {
                                    Plugin.Log.Information($"[PIN DEBUG] New character save callback: saving {pins?.Count ?? 0} pins to NewSecretModPins");
                                    plugin.NewSecretModPins = pins?.ToList();
                                }
                            },
                            null,  // No design context for character-level operations
                            characterName,  // Pass the character name for context
                            (inheritMods) =>
                            {
                                // Inherit callback - restore Penumbra inheritance for these mods
                                if (inheritMods != null && inheritMods.Count > 0)
                                {
                                    _ = plugin.RestoreModInheritance(inheritMods);
                                }
                            }
                        );
                    }
                }
                
                if (!hasValidName)
                {
                    ImGui.EndDisabled();
                    
                    // Show tooltip explaining why the button is disabled
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        ImGui.BeginTooltip();
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.7f, 0.7f, 1.0f));
                        ImGui.Text("Please enter a Character Name before configuring mods.");
                        ImGui.PopStyleColor();
                        ImGui.EndTooltip();
                    }
                }
            }, "Select which mods to enable and configure their options for this character.\nAllows different characters to use different mod combinations and settings.", scale);
        }
        
        private void DrawAutomationField(float labelWidth, float inputWidth, float inputOffset, float scale)
        {
            string tempCharacterAutomation = IsEditWindowOpen ? editedCharacterAutomation : plugin.NewCharacterAutomation;

            DrawFormField("Glam. Automation", labelWidth, inputWidth, inputOffset, () =>
            {
                // Glamourer doesn't expose an IPC to get automation names, so use plain text input
                ImGui.SetNextItemWidth(inputWidth);
                if (ImGui.InputText("##Glam.Automation", ref tempCharacterAutomation, 100))
                {
                    if (IsEditWindowOpen)
                    {
                        editedCharacterAutomation = tempCharacterAutomation;
                        if (isAdvancedModeCharacter)
                        {
                            UpdateAdvancedMacroAutomation(tempCharacterAutomation);
                        }
                        else
                        {
                            editedCharacterMacros = GenerateMacro();
                        }
                    }
                    else
                    {
                        plugin.NewCharacterAutomation = tempCharacterAutomation;
                        if (isAdvancedModeCharacter)
                        {
                            UpdateAdvancedMacroAutomation(tempCharacterAutomation);
                            plugin.NewCharacterMacros = advancedCharacterMacroText;
                        }
                        else
                        {
                            plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                        }
                    }
                }
            }, "Enter the name of a Glamourer Automation for this character.\nMust match the automation name EXACTLY as shown in Glamourer.\nDesign-level automations override this if both are set.", scale);
        }

        private void DrawCustomizeField(float labelWidth, float inputWidth, float inputOffset, float scale)
        {
            string tempCustomize = IsEditWindowOpen ? editedCharacterCustomize : plugin.NewCustomizeProfile;

            DrawFormField("Customize+ Profile", labelWidth, inputWidth, inputOffset, () =>
            {
                var customizeOptions = plugin.IntegrationListProvider?.GetCustomizePlusProfiles() ?? Array.Empty<string>();
                var currentCustomize = plugin.IntegrationListProvider?.GetCurrentCustomizePlusProfile();

                if (AutocompleteCombo.Draw("##CustomizeProfile", ref tempCustomize, customizeOptions, inputWidth, "Select profile...", currentActive: currentCustomize))
                {
                    if (IsEditWindowOpen)
                    {
                        editedCharacterCustomize = tempCustomize;
                        if (isAdvancedModeCharacter)
                        {
                            UpdateAdvancedMacroCustomize(tempCustomize);
                        }
                        else
                        {
                            editedCharacterMacros = GenerateMacro();
                        }
                    }
                    else
                    {
                        plugin.NewCustomizeProfile = tempCustomize;
                        if (isAdvancedModeCharacter)
                        {
                            UpdateAdvancedMacroCustomize(tempCustomize);
                            plugin.NewCharacterMacros = advancedCharacterMacroText;
                        }
                        else
                        {
                            plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                        }
                    }
                }
            }, "Select the Customize+ profile for this character. Right-click to clear.", scale);
        }

        private void DrawHonorificSection(float labelWidth, float inputWidth, float inputOffset, float scale)
        {
            ImGui.SetCursorPosX(10 * scale);
            ImGui.Text("Honorific Title");
            ImGui.SameLine();
            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);

            bool changed = false;

            // Title input
            changed |= ImGui.InputText("##HonorificTitle", ref tempHonorificTitle, 50);

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
                }
                ImGui.EndCombo();
            }

            // Text colour picker
            ImGui.SameLine();
            ImGui.SetNextItemWidth(40 * scale);
            changed |= ImGui.ColorEdit3("##HonorificColor", ref tempHonorificColor, ImGuiColorEditFlags.NoInputs);

            // Glow picker with gradient options (Honorific-style)
            ImGui.SameLine();
            changed |= DrawGlowPicker(scale);

            // Tooltip
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300 * scale);
                ImGui.TextUnformatted("This will set a forced title when you switch to this character.\nThe dropdown selects if the title appears above (prefix) or below (suffix) your name in-game.\nClick the glow color box to access gradient presets.\nUse the Honorific plug-in's 'Clear' button if you need to remove it.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            // Live preview to the right of tooltip
            if (!string.IsNullOrWhiteSpace(tempHonorificTitle))
            {
                ImGui.SameLine(0, 4 * scale);
                DrawHonorificPreview(scale);
            }

            if (changed)
            {
                UpdateHonorificData();

                // Always update advanced macro when in advanced mode
                if (isAdvancedModeCharacter)
                {
                    UpdateAdvancedMacroHonorific();
                    if (!IsEditWindowOpen)
                    {
                        plugin.NewCharacterMacros = advancedCharacterMacroText;
                    }
                }
                else
                {
                    if (IsEditWindowOpen)
                    {
                        editedCharacterMacros = GenerateMacro();
                    }
                    else
                    {
                        plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                    }
                }
            }
        }

        /// <summary>
        /// Draws a glow color picker with gradient options (Honorific-style)
        /// </summary>
        private bool DrawGlowPicker(float scale)
        {
            bool modified = false;
            long animOffset = AnimationTimer.ElapsedMilliseconds;

            // When a gradient is selected, show animated color; otherwise show solid glow
            Vector3 displayColor;
            if (tempHonorificGradientSet.HasValue)
            {
                displayColor = GetGradientPreviewColor(tempHonorificGradientSet.Value, animOffset);
            }
            else
            {
                displayColor = tempHonorificGlow;
            }

            // Use ColorButton to match the text color picker size exactly
            if (ImGui.ColorButton("##GlowPickerBtn", new Vector4(displayColor, 1f), ImGuiColorEditFlags.NoTooltip))
            {
                ImGui.OpenPopup("##GlowPickerPopup");
            }

            // Tooltip
            if (ImGui.IsItemHovered())
            {
                if (tempHonorificGradientSet.HasValue)
                    ImGui.SetTooltip($"{GradientPresetNames[tempHonorificGradientSet.Value]} ({tempHonorificAnimationStyle ?? "Wave"})");
                else
                    ImGui.SetTooltip("Glow (click for gradients)");
            }

            // The popup with gradient options
            if (ImGui.BeginPopup("##GlowPickerPopup"))
            {
                float popupWidth = 200 * scale;

                // Default Glow option with color picker
                ImGui.Text("Solid Glow:");
                ImGui.SameLine();
                if (ImGui.ColorEdit3("##GlowColorPicker", ref tempHonorificGlow, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                {
                    tempHonorificGradientSet = null;
                    tempHonorificAnimationStyle = null;
                    modified = true;
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("Use##UseGlow"))
                {
                    tempHonorificGradientSet = null;
                    tempHonorificAnimationStyle = null;
                    modified = true;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.Separator();
                ImGui.Text("Animated Gradients:");

                // Gate animated gradients behind supporter acknowledgment
                if (plugin.Configuration.HasAcknowledgedHonorificSupport)
                {
                    // Tab bar for animation styles
                    if (ImGui.BeginTabBar("##GradAnimTabs"))
                    {
                        foreach (var animStyle in new[] { "Wave", "Pulse", "Static" })
                        {
                            if (ImGui.BeginTabItem(animStyle))
                            {
                                // Compact child region for presets
                                float childHeight = Math.Min(180 * scale, GradientPresetNames.Length * ImGui.GetTextLineHeightWithSpacing());
                                if (ImGui.BeginChild($"##Presets{animStyle}", new Vector2(popupWidth, childHeight)))
                                {
                                    var drawList = ImGui.GetWindowDrawList();

                                    for (int i = 0; i < GradientPresetNames.Length; i++)
                                    {
                                        bool isSelected = tempHonorificGradientSet == i && tempHonorificAnimationStyle == animStyle;

                                        // Draw invisible selectable
                                        var selectableSize = new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeightWithSpacing());
                                        var cursorPos = ImGui.GetCursorScreenPos();

                                        if (ImGui.Selectable($"##Preset{animStyle}{i}", isSelected, ImGuiSelectableFlags.None, selectableSize))
                                        {
                                            tempHonorificGradientSet = i;
                                            tempHonorificAnimationStyle = animStyle;
                                            modified = true;
                                            ImGui.CloseCurrentPopup();
                                        }

                                        // Draw the preset name with animated gradient effect on top
                                        var textPos = cursorPos + ImGui.GetStyle().FramePadding;
                                        DrawGradientTextForPicker(drawList, textPos, GradientPresetNames[i], i, animStyle);
                                    }
                                }
                                ImGui.EndChild();
                                ImGui.EndTabItem();
                            }
                        }
                        ImGui.EndTabBar();
                    }
                }
                else
                {
                    // Show message when supporter acknowledgment not enabled
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.65f, 1.0f));
                    ImGui.TextWrapped("Enable in Settings > Visual Settings to use animated gradients.");
                    ImGui.PopStyleColor();
                }

                ImGui.EndPopup();
            }

            return modified;
        }

        /// <summary>
        /// Draws text with animated gradient for the picker preview
        /// </summary>
        private void DrawGradientTextForPicker(ImDrawListPtr drawList, Vector2 pos, string text, int gradientSet, string animStyle)
        {
            long animOffset = AnimationTimer.ElapsedMilliseconds;

            float charX = pos.X;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                string charStr = c.ToString();

                Vector3 charColor = GetGradientColor(gradientSet, i, animOffset, 5, animStyle);
                uint colorU32 = ImGui.ColorConvertFloat4ToU32(new Vector4(charColor, 1f));

                drawList.AddText(new Vector2(charX, pos.Y), colorU32, charStr);
                charX += ImGui.CalcTextSize(charStr).X;
            }
        }

        private void DrawMoodleField(float labelWidth, float inputWidth, float inputOffset, float scale)
        {
            DrawFormField("Moodle Preset", labelWidth, inputWidth, inputOffset, () =>
            {
                var moodleOptions = plugin.IntegrationListProvider?.GetMoodlesPresets() ?? Array.Empty<string>();

                if (AutocompleteCombo.Draw("##MoodlePreset", ref tempMoodlePreset, moodleOptions, inputWidth, "Select preset..."))
                {
                    if (IsEditWindowOpen)
                        editedCharacterMoodlePreset = tempMoodlePreset;
                    else
                        plugin.NewCharacterMoodlePreset = tempMoodlePreset;

                    if (isAdvancedModeCharacter)
                    {
                        UpdateAdvancedMacroMoodle(tempMoodlePreset);
                        if (!IsEditWindowOpen)
                        {
                            plugin.NewCharacterMacros = advancedCharacterMacroText;
                        }
                    }
                    else
                    {
                        if (IsEditWindowOpen)
                        {
                            editedCharacterMacros = GenerateMacro();
                        }
                        else
                        {
                            plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                        }
                    }
                }
            }, "Select the Moodle preset for this character. Right-click to clear.", scale);
        }

        private void DrawIdlePoseField(float labelWidth, float inputWidth, float inputOffset, float scale)
        {
            ImGui.SetCursorPosX(10 * scale);
            ImGui.Text("Idle Pose");
            ImGui.SameLine();
            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);

            string[] poseOptions = { "None", "0", "1", "2", "3", "4", "5", "6" };
            byte storedIndex = IsEditWindowOpen
                ? plugin.Characters[selectedCharacterIndex].IdlePoseIndex
                : plugin.NewCharacterIdlePoseIndex;

            int dropdownIndex = storedIndex == 7 ? 0 : storedIndex + 1;

            if (ImGui.BeginCombo("##IdlePose", poseOptions[dropdownIndex]))
            {
                for (int i = 0; i < poseOptions.Length; i++)
                {
                    bool selected = i == dropdownIndex;
                    if (ImGui.Selectable(poseOptions[i], selected))
                    {
                        byte newIndex = (byte)(i == 0 ? 7 : i - 1);
                        byte currentIndex = IsEditWindowOpen
                            ? plugin.Characters[selectedCharacterIndex].IdlePoseIndex
                            : plugin.NewCharacterIdlePoseIndex;

                        if (currentIndex != newIndex)
                        {
                            if (IsEditWindowOpen)
                                plugin.Characters[selectedCharacterIndex].IdlePoseIndex = newIndex;
                            else
                                plugin.NewCharacterIdlePoseIndex = newIndex;

                            if (isAdvancedModeCharacter)
                            {
                                UpdateAdvancedMacroIdlePose(newIndex);
                                if (!IsEditWindowOpen)
                                {
                                    plugin.NewCharacterMacros = advancedCharacterMacroText;
                                }
                            }
                            else
                            {
                                if (IsEditWindowOpen)
                                {
                                    editedCharacterMacros = GenerateMacro();
                                }
                                else
                                {
                                    plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                                }
                            }
                        }
                    }
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            // Tooltip
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300 * scale);
                ImGui.TextUnformatted("Sets your character's idle pose (0–6).\nChoose 'None' if you don't want Character Select+ to change your idle.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
        }

        private void DrawGearsetField(float labelWidth, float inputWidth, float inputOffset, float scale)
        {
            ImGui.SetCursorPosX(10 * scale);
            ImGui.Text("Assigned Gearset");
            ImGui.SameLine();
            ImGui.SetCursorPosX(labelWidth + inputOffset);
            ImGui.SetNextItemWidth(inputWidth);

            // Get available gearsets
            var gearsets = plugin.GetPlayerGearsets();

            // Get current value
            int? currentGearset = IsEditWindowOpen ? editedCharacterGearset : plugin.NewCharacterGearset;

            // Build display text for current selection
            string currentDisplay = "None";
            if (currentGearset.HasValue)
            {
                var matchingGearset = gearsets.FirstOrDefault(g => g.Number == currentGearset.Value);
                if (matchingGearset.Number > 0)
                {
                    currentDisplay = plugin.GetGearsetDisplayName(matchingGearset.Number, matchingGearset.JobId, matchingGearset.Name);
                }
                else
                {
                    currentDisplay = $"Gearset {currentGearset.Value}";
                }
            }

            if (ImGui.BeginCombo("##AssignedGearset", currentDisplay))
            {
                // "None" option
                if (ImGui.Selectable("None", !currentGearset.HasValue))
                {
                    if (IsEditWindowOpen)
                        editedCharacterGearset = null;
                    else
                        plugin.NewCharacterGearset = null;
                }
                if (!currentGearset.HasValue)
                    ImGui.SetItemDefaultFocus();

                // Gearset options
                foreach (var gearset in gearsets)
                {
                    string displayName = plugin.GetGearsetDisplayName(gearset.Number, gearset.JobId, gearset.Name);
                    bool isSelected = currentGearset.HasValue && currentGearset.Value == gearset.Number;

                    if (ImGui.Selectable(displayName, isSelected))
                    {
                        if (IsEditWindowOpen)
                            editedCharacterGearset = gearset.Number;
                        else
                            plugin.NewCharacterGearset = gearset.Number;
                    }
                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            // Tooltip
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300 * scale);
                ImGui.TextUnformatted("Automatically switch to this gearset when applying this character.\nChoose 'None' to not change gearsets.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
        }

        private void DrawImageSelection(float scale)
        {
            if (ImGui.Button("Choose Image", new Vector2(0, 25 * scale)))
            {
                plugin.OpenFilePicker(
                    "Select Character Image",
                    "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|PNG files (*.png)|*.png",
                    (selectedPath) =>
                    {
                        lock (this)
                        {
                            pendingImagePath = selectedPath;
                        }
                    }
                );
            }

            // Apply pending image
            if (pendingImagePath != null)
            {
                lock (this)
                {
                    if (IsEditWindowOpen)
                        editedCharacterImagePath = pendingImagePath;
                    else
                        plugin.NewCharacterImagePath = pendingImagePath;

                    pendingImagePath = null;
                }
            }

            // Show image preview
            DrawImagePreview(scale);
        }

        private void DrawImagePreview(float scale)
        {
            string pluginDirectory = plugin.PluginDirectory;
            string defaultImagePath = Path.Combine(pluginDirectory, "Assets", "Default.png");

            string? imagePath = IsEditWindowOpen ? editedCharacterImagePath : plugin.NewCharacterImagePath;
            string finalImagePath = !string.IsNullOrEmpty(imagePath) && File.Exists(imagePath)
                ? imagePath
                : defaultImagePath;

            if (!string.IsNullOrEmpty(finalImagePath) && File.Exists(finalImagePath))
            {
                var texture = Plugin.TextureProvider.GetFromFile(finalImagePath).GetWrapOrDefault();
                if (texture != null)
                {
                    float originalWidth = texture.Width;
                    float originalHeight = texture.Height;
                    float maxSize = 100f * scale;

                    float aspectRatio = originalWidth / originalHeight;
                    float displayWidth, displayHeight;

                    if (aspectRatio > 1)
                    {
                        displayWidth = maxSize;
                        displayHeight = maxSize / aspectRatio;
                    }
                    else
                    {
                        displayHeight = maxSize;
                        displayWidth = maxSize * aspectRatio;
                    }

                    var cursorPos = ImGui.GetCursorScreenPos();
                    var imageEnd = cursorPos + new Vector2(displayWidth, displayHeight);

                    uiStyles.DrawGlowingBorder(
                        cursorPos - new Vector2(2 * scale, 2 * scale),
                        imageEnd + new Vector2(2 * scale, 2 * scale),
                        new Vector3(0.5f, 0.5f, 0.5f),
                        0.3f,
                        false,
                        scale
                    );

                    ImGui.Image((ImTextureID)texture.Handle, new Vector2(displayWidth, displayHeight));
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
        }

        private void DrawAdvancedModeSection(float scale)
        {
            if (ImGui.Button(isAdvancedModeCharacter ? "Exit Advanced Mode" : "Advanced Mode", new Vector2(0, 25 * scale)))
            {
                isAdvancedModeCharacter = !isAdvancedModeCharacter;

                // Update the character's advanced mode flag
                if (IsEditWindowOpen && selectedCharacterIndex >= 0 && selectedCharacterIndex < plugin.Characters.Count)
                {
                    plugin.Characters[selectedCharacterIndex].IsAdvancedMode = isAdvancedModeCharacter;
                    plugin.SaveConfiguration();
                }

                if (isAdvancedModeCharacter)
                {
                    // When entering advanced mode, use existing macro if available, otherwise generate
                    if (IsEditWindowOpen)
                    {
                        advancedCharacterMacroText = !string.IsNullOrWhiteSpace(editedCharacterMacros)
                            ? editedCharacterMacros
                            : GenerateMacro();
                    }
                    else
                    {
                        advancedCharacterMacroText = !string.IsNullOrWhiteSpace(plugin.NewCharacterMacros)
                            ? plugin.NewCharacterMacros
                            : ((isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro());
                        plugin.NewCharacterMacros = advancedCharacterMacroText;
                    }
                }
                else
                {
                    // When exiting advanced mode, preserve the current macro state
                    if (IsEditWindowOpen)
                    {
                        editedCharacterMacros = advancedCharacterMacroText;
                    }
                    else
                    {
                        plugin.NewCharacterMacros = advancedCharacterMacroText;
                    }
                }
            }

            // Tooltip
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (5 * scale));
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a");
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300 * scale);
                ImGui.TextUnformatted("⚠️ Do not touch this unless you know what you're doing.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            // Advanced mode editor
            if (isAdvancedModeCharacter)
            {
                ImGui.Text("Edit Macro Manually:");

                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.1f, 0.1f, 0.1f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));

                ImGui.InputTextMultiline("##AdvancedCharacterMacro", ref advancedCharacterMacroText, 2000,
                    new Vector2(500 * scale, 150 * scale), ImGuiInputTextFlags.AllowTabInput);

                ImGui.PopStyleColor(2);

                // Real-time sync when user types in advanced mode
                if (!IsEditWindowOpen)
                {
                    plugin.NewCharacterMacros = advancedCharacterMacroText;
                }
                else
                {
                    editedCharacterMacros = advancedCharacterMacroText;
                }
            }
        }
        private float GetSafeScale(float baseScale)
        {
            return Math.Clamp(baseScale, 0.3f, 5.0f); // Prevent extreme scaling
        }

        private void DrawActionButtons(float scale)
        {
            string tempName = IsEditWindowOpen ? editedCharacterName : plugin.NewCharacterName;
            string tempPenumbra = IsEditWindowOpen ? editedCharacterPenumbra : plugin.NewPenumbraCollection;
            string tempGlamourer = IsEditWindowOpen ? editedCharacterGlamourer : plugin.NewGlamourerDesign;

            bool canSaveCharacter = !string.IsNullOrWhiteSpace(tempName) &&
                                   !string.IsNullOrWhiteSpace(tempPenumbra) &&
                                   !string.IsNullOrWhiteSpace(tempGlamourer) &&
                                   string.IsNullOrEmpty(nameValidationError);

            uiStyles.PushDarkButtonStyle(scale);

            if (!canSaveCharacter)
                ImGui.BeginDisabled();

            if (ImGui.Button(IsEditWindowOpen ? "Save Changes" : "Save Character", new Vector2(0, 30 * scale)))
            {
                if (IsEditWindowOpen)
                {
                    SaveEditedCharacter();
                }
                else
                {
                    string finalMacro;
                    if (isAdvancedModeCharacter)
                    {
                        finalMacro = advancedCharacterMacroText;
                    }
                    else
                    {
                        finalMacro = plugin.NewCharacterMacros;
                    }

                    plugin.SaveNewCharacter(finalMacro);
                }

                CloseForm();
            }

            plugin.SaveButtonPos = ImGui.GetItemRectMin();
            plugin.SaveButtonSize = ImGui.GetItemRectSize();

            if (!canSaveCharacter)
                ImGui.EndDisabled();

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(0, 30 * scale)))
            {
                CloseForm();
            }

            uiStyles.PopDarkButtonStyle();
        }

        // Advanced mode update methods
        private void UpdateAdvancedMacroPenumbra(string collection)
        {
            advancedCharacterMacroText = PatchMacroLine(
                advancedCharacterMacroText,
                "/penumbra collection",
                $"/penumbra collection individual | {collection} | self"
            );

            advancedCharacterMacroText = UpdateCollectionInLines(
                advancedCharacterMacroText,
                "/penumbra bulktag disable",
                collection
            );

            advancedCharacterMacroText = UpdateCollectionInLines(
                advancedCharacterMacroText,
                "/penumbra bulktag enable",
                collection
            );
        }

        private void UpdateAdvancedMacroGlamourer(string oldGlamourer, string newGlamourer)
        {
            var lines = advancedCharacterMacroText.Split('\n').ToList();

            // Find and replace the main glamour apply line (not "no clothes")
            bool foundExistingLine = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("/glamour apply", StringComparison.OrdinalIgnoreCase) &&
                    !line.Contains("no clothes", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"/glamour apply {newGlamourer} | self";
                    foundExistingLine = true;
                    break;
                }
            }

            // Update bulktag enable line if it exists (for secret mode....shhh! how can it stay a secret if I keep mentioning it??)
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("/penumbra bulktag enable", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 2)
                    {
                        var collection = parts[0].Replace("/penumbra bulktag enable", "").Trim();
                        lines[i] = $"/penumbra bulktag enable {collection} | {newGlamourer}";
                    }
                    break;
                }
            }

            if (!foundExistingLine && !string.IsNullOrWhiteSpace(newGlamourer))
            {
                var insertPos = GetProperInsertPosition(lines, "/glamour apply");
                lines.Insert(insertPos, $"/glamour apply {newGlamourer} | self");
            }

            advancedCharacterMacroText = string.Join("\n", lines);
        }

        private void UpdateAdvancedMacroAutomation(string automation)
        {
            var line = string.IsNullOrWhiteSpace(automation)
                ? "/glamour automation enable None"
                : $"/glamour automation enable {automation}";

            advancedCharacterMacroText = PatchMacroLine(
                advancedCharacterMacroText,
                "/glamour automation enable",
                line
            );
        }

        private void UpdateAdvancedMacroCustomize(string customize)
        {
            advancedCharacterMacroText = PatchMacroLine(
                advancedCharacterMacroText,
                "/customize profile disable",
                "/customize profile disable <me>"
            );

            if (!string.IsNullOrWhiteSpace(customize))
            {
                advancedCharacterMacroText = PatchMacroLine(
                    advancedCharacterMacroText,
                    "/customize profile enable",
                    $"/customize profile enable <me>, {customize}"
                );
            }
            else
            {
                advancedCharacterMacroText = string.Join("\n",
                    advancedCharacterMacroText
                        .Split('\n')
                        .Where(l => !l.TrimStart().StartsWith("/customize profile enable"))
                );
            }
        }

        private void UpdateAdvancedMacroHonorific()
        {
            var lines = advancedCharacterMacroText.Split('\n').ToList();

            var clearIdx = lines.FindIndex(l =>
                l.TrimStart().StartsWith("/honorific force clear", StringComparison.OrdinalIgnoreCase));

            if (clearIdx < 0)
            {
                var insertPos = GetProperInsertPosition(lines, "/honorific force clear");
                lines.Insert(insertPos, "/honorific force clear | silent");
                clearIdx = insertPos;
            }
            else
            {
                // Update existing clear line to include silent
                if (!lines[clearIdx].Contains("silent", StringComparison.OrdinalIgnoreCase))
                {
                    lines[clearIdx] = "/honorific force clear | silent";
                }
            }

            if (!string.IsNullOrWhiteSpace(tempHonorificTitle))
            {
                var c = tempHonorificColor;
                var g = tempHonorificGlow;
                string colorHex = $"#{(int)(c.X * 255):X2}{(int)(c.Y * 255):X2}{(int)(c.Z * 255):X2}";
                string glowHex = $"#{(int)(g.X * 255):X2}{(int)(g.Y * 255):X2}{(int)(g.Z * 255):X2}";

                string gradientPart = "";
                if (tempHonorificGradientSet.HasValue && !string.IsNullOrEmpty(tempHonorificAnimationStyle))
                {
                    gradientPart = $" | +{tempHonorificGradientSet.Value}/{tempHonorificAnimationStyle}";
                }

                string setLine = $"/honorific force set {tempHonorificTitle} | {tempHonorificPrefix} | {colorHex} | {glowHex}{gradientPart} | silent";

                var setIdx = lines.FindIndex(l =>
                    l.TrimStart().StartsWith("/honorific force set", StringComparison.OrdinalIgnoreCase));

                if (setIdx >= 0)
                {
                    lines[setIdx] = setLine;
                }
                else
                {
                    lines.Insert(clearIdx + 1, setLine);
                }
            }
            else
            {
                lines.RemoveAll(l => l.TrimStart().StartsWith("/honorific force set", StringComparison.OrdinalIgnoreCase));
            }

            advancedCharacterMacroText = string.Join("\n", lines);
        }


        private void UpdateAdvancedMacroMoodle(string preset)
        {
            var lines = advancedCharacterMacroText.Split('\n').ToList();

            var removeIdx = lines.FindIndex(l =>
                l.TrimStart().StartsWith("/moodle remove self preset all", StringComparison.OrdinalIgnoreCase));

            if (removeIdx < 0)
            {
                var insertPos = GetProperInsertPosition(lines, "/moodle remove");
                lines.Insert(insertPos, "/moodle remove self preset all");
                removeIdx = insertPos;
            }

            if (!string.IsNullOrWhiteSpace(preset))
            {
                string applyLine = $"/moodle apply self preset \"{preset}\"";
                var applyIdx = lines.FindIndex(l =>
                    l.TrimStart().StartsWith("/moodle apply self preset", StringComparison.OrdinalIgnoreCase));

                if (applyIdx >= 0)
                {
                    lines[applyIdx] = applyLine;
                }
                else
                {
                    lines.Insert(removeIdx + 1, applyLine);
                }
            }
            else
            {
                lines.RemoveAll(l => l.TrimStart().StartsWith("/moodle apply self preset", StringComparison.OrdinalIgnoreCase));
            }

            advancedCharacterMacroText = string.Join("\n", lines);
        }

        private void UpdateAdvancedMacroIdlePose(byte poseIndex)
        {
            var lines = advancedCharacterMacroText.Split('\n').ToList();

            if (poseIndex != 7)
            {
                string sidleLine = $"/sidle {poseIndex}";
                var sidleIdx = lines.FindIndex(l =>
                    l.TrimStart().StartsWith("/sidle", StringComparison.OrdinalIgnoreCase));

                if (sidleIdx >= 0)
                {
                    lines[sidleIdx] = sidleLine;
                }
                else
                {
                    var insertPos = GetProperInsertPosition(lines, "/sidle");
                    lines.Insert(insertPos, sidleLine);
                }
            }
            else
            {
                // Remove any existing sidle line when pose is "None"
                lines.RemoveAll(l => l.TrimStart().StartsWith("/sidle", StringComparison.OrdinalIgnoreCase));
            }

            advancedCharacterMacroText = string.Join("\n", lines);
        }

        private void UpdateHonorificData()
        {
            if (IsEditWindowOpen)
            {
                editedCharacterHonorificTitle = tempHonorificTitle;
                editedCharacterHonorificPrefix = tempHonorificPrefix;
                editedCharacterHonorificSuffix = tempHonorificSuffix;
                editedCharacterHonorificColor = tempHonorificColor;
                editedCharacterHonorificGlow = tempHonorificGlow;
                editedCharacterHonorificGradientSet = tempHonorificGradientSet;
                editedCharacterHonorificAnimationStyle = tempHonorificAnimationStyle;
            }
            else
            {
                plugin.NewCharacterHonorificTitle = tempHonorificTitle;
                plugin.NewCharacterHonorificPrefix = tempHonorificPrefix;
                plugin.NewCharacterHonorificSuffix = tempHonorificSuffix;
                plugin.NewCharacterHonorificColor = tempHonorificColor;
                plugin.NewCharacterHonorificGlow = tempHonorificGlow;
                plugin.NewCharacterHonorificGradientSet = tempHonorificGradientSet;
                plugin.NewCharacterHonorificAnimationStyle = tempHonorificAnimationStyle;
            }
        }

        /// <summary>
        /// Draws an animated preview of the Honorific title with the current settings in a dark container
        /// </summary>
        private void DrawHonorificPreview(float scale)
        {
            if (string.IsNullOrWhiteSpace(tempHonorificTitle))
                return;

            var textSize = ImGui.CalcTextSize(tempHonorificTitle);
            var padding = new Vector2(8 * scale, 4 * scale);
            var boxSize = textSize + padding * 2;

            // Draw dark background box
            var drawList = ImGui.GetWindowDrawList();
            var boxStart = ImGui.GetCursorScreenPos();
            var boxEnd = boxStart + boxSize;

            // Dark background with slight border
            drawList.AddRectFilled(boxStart, boxEnd, ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 1f)), 4f);
            drawList.AddRect(boxStart, boxEnd, ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.3f, 1f)), 4f);

            // Text position inside the box
            var textPos = boxStart + padding;

            // Build SeString with proper color and glow
            SeString seString;
            if (tempHonorificGradientSet.HasValue)
            {
                // For gradients, build per-character SeString with animated colors
                seString = BuildGradientSeString(tempHonorificTitle, tempHonorificGradientSet.Value,
                    tempHonorificAnimationStyle ?? "Wave", tempHonorificColor);
            }
            else
            {
                // Build SeString with solid color and glow
                seString = BuildColoredSeString(tempHonorificTitle, tempHonorificColor, tempHonorificGlow);
            }

            // Render using Dalamud's SeString renderer for smooth text
            ImGuiHelpers.SeStringWrapped(seString.Encode(), new SeStringDrawParams
            {
                Color = 0xFFFFFFFF,
                WrapWidth = float.MaxValue,
                TargetDrawList = drawList,
                Font = UiBuilder.DefaultFont,
                FontSize = UiBuilder.DefaultFontSizePx,
                ScreenOffset = textPos
            });

            // Reserve space for the box
            ImGui.Dummy(boxSize);
        }

        /// <summary>
        /// Builds an SeString with solid color and glow effect
        /// </summary>
        private SeString BuildColoredSeString(string text, Vector3 color, Vector3 glow)
        {
            var builder = new SeStringBuilder();

            // Add text color
            builder.PushColorRgba(new Vector4(color, 1f));

            // Add edge/glow color
            builder.PushEdgeColorRgba(new Vector4(glow, 1f));

            builder.Append(text);

            builder.PopEdgeColor();
            builder.PopColor();

            return SeString.Parse(builder.GetViewAsSpan());
        }

        /// <summary>
        /// Builds an SeString with animated gradient glow effect
        /// </summary>
        private SeString BuildGradientSeString(string text, int gradientSet, string animStyle, Vector3 textColor)
        {
            var builder = new SeStringBuilder();
            long animOffset = AnimationTimer.ElapsedMilliseconds;

            // Add base text color
            builder.PushColorRgba(new Vector4(textColor, 1f));

            for (int i = 0; i < text.Length; i++)
            {
                // Calculate gradient color for this character
                Vector3 glowColor = GetGradientColor(gradientSet, i, animOffset, 5, animStyle);

                // Push edge color for this character
                builder.PushEdgeColorRgba(new Vector4(glowColor, 1f));
                builder.Append(text[i].ToString());
                builder.PopEdgeColor();
            }

            builder.PopColor();

            return SeString.Parse(builder.GetViewAsSpan());
        }

        /// <summary>
        /// Gets a color from the gradient using Honorific's exact algorithm
        /// </summary>
        private Vector3 GetGradientColor(int gradientSet, int charIndex, long rawMilliseconds, int throttle, string animStyle)
        {
            if (gradientSet < 0 || gradientSet >= DecodedGradients.Length)
                return new Vector3(1f, 1f, 1f);

            var colors = DecodedGradients[gradientSet];
            var colorCount = colors.GetLength(0);

            // Honorific's exact timing: divide by 15 first, then by throttle
            var animationOffset = rawMilliseconds / 15;

            int index;
            if (animStyle == "Pulse")
            {
                // Pulse: whole text uses same color (charIndex multiplier = 0)
                index = (int)((animationOffset / throttle) % colorCount);
            }
            else if (animStyle == "Static")
            {
                // Static: spread gradient across text length, no animation
                index = (int)Math.Round(charIndex / (float)Math.Max(1, 16) * colorCount) % colorCount;
            }
            else // Wave
            {
                // Wave: position based on character index + time (charIndex multiplier = 1)
                index = (int)((animationOffset / throttle + charIndex) % colorCount);
            }

            return new Vector3(
                colors[index, 0] / 255f,
                colors[index, 1] / 255f,
                colors[index, 2] / 255f
            );
        }

        /// <summary>
        /// Gets a representative color from a gradient preset (for button preview)
        /// </summary>
        private Vector3 GetGradientPreviewColor(int preset, long rawMilliseconds)
        {
            if (preset < 0 || preset >= DecodedGradients.Length)
                return new Vector3(1f, 1f, 1f);

            var colors = DecodedGradients[preset];
            var colorCount = colors.GetLength(0);
            // Match Honorific timing: /15 then /5 (throttle)
            var index = (int)((rawMilliseconds / 15 / 5) % colorCount);

            return new Vector3(
                colors[index, 0] / 255f,
                colors[index, 1] / 255f,
                colors[index, 2] / 255f
            );
        }
        private string PatchMacroLine(string existing, string prefix, string replacement)
        {
            var lines = existing.Split('\n').ToList();
            var idx = lines.FindIndex(l => l.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

            if (idx >= 0)
            {
                lines[idx] = replacement;
            }
            else
            {
                int insertPosition = GetProperInsertPosition(lines, prefix);
                lines.Insert(insertPosition, replacement);
            }

            return string.Join("\n", lines);
        }

        private int GetProperInsertPosition(List<string> lines, string prefix)
        {
            var order = new[]
            {
                "/penumbra collection",
                "/penumbra bulktag disable",
                "/penumbra bulktag enable",
                "/glamour apply no clothes",
                "/glamour apply",
                "/glamour automation enable",
                "/customize profile disable",
                "/customize profile enable",
                "/honorific force clear",
                "/honorific force set",
                "/moodle remove",
                "/moodle apply",
                "/sidle",
                "/penumbra redraw"
            };

            int targetOrder = Array.FindIndex(order, o => prefix.StartsWith(o, StringComparison.OrdinalIgnoreCase));
            if (targetOrder == -1) return lines.Count;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].TrimStart();
                int lineOrder = Array.FindIndex(order, o => line.StartsWith(o, StringComparison.OrdinalIgnoreCase));

                if (lineOrder > targetOrder || lineOrder == -1)
                {
                    return i;
                }
            }

            return lines.Count;
        }

        private string UpdateCollectionInLines(string existing, string prefix, string newCollection)
        {
            var lines = existing.Split('\n').Select(line =>
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var rest = trimmed.Substring(prefix.Length).TrimStart();
                    var afterCollection = rest.IndexOf('|') >= 0
                        ? rest.Substring(rest.IndexOf('|'))
                        : rest.Substring(rest.IndexOf(' '));
                    return $"{prefix} {newCollection} {afterCollection}";
                }
                return line;
            });
            return string.Join("\n", lines);
        }

        private string GenerateMacro()
        {
            string penumbra = IsEditWindowOpen ? editedCharacterPenumbra : plugin.NewPenumbraCollection;
            string glamourer = IsEditWindowOpen ? editedCharacterGlamourer : plugin.NewGlamourerDesign;
            string customize = IsEditWindowOpen ? editedCharacterCustomize : plugin.NewCustomizeProfile;
            string honorificTitle = IsEditWindowOpen ? editedCharacterHonorificTitle : plugin.NewCharacterHonorificTitle;
            string honorificPrefix = IsEditWindowOpen ? editedCharacterHonorificPrefix : plugin.NewCharacterHonorificPrefix;
            Vector3 honorificColor = IsEditWindowOpen ? editedCharacterHonorificColor : plugin.NewCharacterHonorificColor;
            Vector3 honorificGlow = IsEditWindowOpen ? editedCharacterHonorificGlow : plugin.NewCharacterHonorificGlow;
            string automation = IsEditWindowOpen ? editedCharacterAutomation : plugin.NewCharacterAutomation;
            string moodlePreset = IsEditWindowOpen ? editedCharacterMoodlePreset : plugin.NewCharacterMoodlePreset;
            int idlePose = IsEditWindowOpen ? plugin.Characters[selectedCharacterIndex].IdlePoseIndex : plugin.NewCharacterIdlePoseIndex;

            if (string.IsNullOrWhiteSpace(penumbra) || string.IsNullOrWhiteSpace(glamourer))
                return "/penumbra redraw self";

            string macro = $"/penumbra collection individual | {penumbra} | self\n";
            macro += $"/glamour apply {glamourer} | self\n";

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

            macro += "/honorific force clear | silent\n";
            if (!string.IsNullOrWhiteSpace(honorificTitle))
            {
                string colorHex = $"#{(int)(honorificColor.X * 255):X2}{(int)(honorificColor.Y * 255):X2}{(int)(honorificColor.Z * 255):X2}";
                string glowHex = $"#{(int)(honorificGlow.X * 255):X2}{(int)(honorificGlow.Y * 255):X2}{(int)(honorificGlow.Z * 255):X2}";
                int? gradientSet = IsEditWindowOpen ? editedCharacterHonorificGradientSet : plugin.NewCharacterHonorificGradientSet;
                string? animStyle = IsEditWindowOpen ? editedCharacterHonorificAnimationStyle : plugin.NewCharacterHonorificAnimationStyle;

                string gradientPart = "";
                if (gradientSet.HasValue && !string.IsNullOrEmpty(animStyle))
                {
                    gradientPart = $" | +{gradientSet.Value}/{animStyle}";
                }

                macro += $"/honorific force set {honorificTitle} | {honorificPrefix} | {colorHex} | {glowHex}{gradientPart} | silent\n";
            }

            macro += "/moodle remove self preset all\n";
            if (!string.IsNullOrWhiteSpace(moodlePreset))
                macro += $"/moodle apply self preset \"{moodlePreset}\"\n";

            if (idlePose != 7)
                macro += $"/sidle {idlePose}\n";

            macro += "/penumbra redraw self";

            return macro;
        }

        private string GenerateSecretMacro()
        {
            string penumbra = IsEditWindowOpen ? editedCharacterPenumbra : plugin.NewPenumbraCollection;
            string glamourer = IsEditWindowOpen ? editedCharacterGlamourer : plugin.NewGlamourerDesign;
            string customize = IsEditWindowOpen ? editedCharacterCustomize : plugin.NewCustomizeProfile;
            string honorTitle = IsEditWindowOpen ? editedCharacterHonorificTitle : plugin.NewCharacterHonorificTitle;
            string honorPref = IsEditWindowOpen ? editedCharacterHonorificPrefix : plugin.NewCharacterHonorificPrefix;
            Vector3 honorColor = IsEditWindowOpen ? editedCharacterHonorificColor : plugin.NewCharacterHonorificColor;
            Vector3 honorGlow = IsEditWindowOpen ? editedCharacterHonorificGlow : plugin.NewCharacterHonorificGlow;
            int? honorGradientSet = IsEditWindowOpen ? editedCharacterHonorificGradientSet : plugin.NewCharacterHonorificGradientSet;
            string? honorAnimStyle = IsEditWindowOpen ? editedCharacterHonorificAnimationStyle : plugin.NewCharacterHonorificAnimationStyle;
            string moodlePreset = IsEditWindowOpen ? editedCharacterMoodlePreset : plugin.NewCharacterMoodlePreset;
            int idlePose = IsEditWindowOpen
                                    ? plugin.Characters[selectedCharacterIndex].IdlePoseIndex
                                    : plugin.NewCharacterIdlePoseIndex;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"/penumbra collection individual | {penumbra} | self");
            sb.AppendLine($"/penumbra bulktag disable {penumbra} | gear");
            sb.AppendLine($"/penumbra bulktag disable {penumbra} | hair");
            sb.AppendLine($"/penumbra bulktag enable {penumbra} | {glamourer}");
            sb.AppendLine("/glamour apply no clothes | self");
            sb.AppendLine($"/glamour apply {glamourer} | self");

            if (plugin.Configuration.EnableAutomations)
            {
                string automation = IsEditWindowOpen ? editedCharacterAutomation : plugin.NewCharacterAutomation;
                if (string.IsNullOrWhiteSpace(automation))
                    sb.AppendLine("/glamour automation enable None");
                else
                    sb.AppendLine($"/glamour automation enable {automation}");
            }

            sb.AppendLine("/customize profile disable <me>");
            if (!string.IsNullOrWhiteSpace(customize))
                sb.AppendLine($"/customize profile enable <me>, {customize}");

            sb.AppendLine("/honorific force clear | silent");
            if (!string.IsNullOrWhiteSpace(honorTitle))
            {
                var colorHex = $"#{(int)(honorColor.X * 255):X2}{(int)(honorColor.Y * 255):X2}{(int)(honorColor.Z * 255):X2}";
                var glowHex = $"#{(int)(honorGlow.X * 255):X2}{(int)(honorGlow.Y * 255):X2}{(int)(honorGlow.Z * 255):X2}";

                string gradientPart = "";
                if (honorGradientSet.HasValue && !string.IsNullOrEmpty(honorAnimStyle))
                {
                    gradientPart = $" | +{honorGradientSet.Value}/{honorAnimStyle}";
                }

                sb.AppendLine($"/honorific force set {honorTitle} | {honorPref} | {colorHex} | {glowHex}{gradientPart} | silent");
            }

            sb.AppendLine("/moodle remove self preset all");
            if (!string.IsNullOrWhiteSpace(moodlePreset))
                sb.AppendLine($"/moodle apply self preset \"{moodlePreset}\"");

            if (idlePose != 7)
                sb.AppendLine($"/sidle {idlePose}");

            sb.Append("/penumbra redraw self");
            return sb.ToString();
        }

        public void SetSecretMode(bool secretMode)
        {
            isSecretMode = secretMode;
            if (secretMode && !IsEditWindowOpen)
            {
                plugin.NewCharacterMacros = (secretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
            }
        }

        private void CloseForm()
        {
            IsEditWindowOpen = false;
            plugin.CloseAddCharacterWindow();
            
            // Close Mod Manager window if it's open
            if (plugin.SecretModeModWindow?.IsOpen ?? false)
            {
                plugin.SecretModeModWindow.IsOpen = false;
            }
            
            isSecretMode = false;
            isAdvancedModeCharacter = false;
            ResetFields();
        }

        public void ResetFields()
        {
            plugin.NewCharacterName = "";
            plugin.NewCharacterColor = new Vector3(1.0f, 1.0f, 1.0f);
            plugin.NewPenumbraCollection = "";
            plugin.NewGlamourerDesign = "";
            plugin.NewCharacterAutomation = "";
            plugin.NewCustomizeProfile = "";
            plugin.NewCharacterImagePath = null;
            plugin.NewCharacterDesigns.Clear();
            plugin.NewCharacterHonorificTitle = "";
            plugin.NewCharacterHonorificPrefix = "Prefix";
            plugin.NewCharacterHonorificSuffix = "Suffix";
            plugin.NewCharacterHonorificColor = new Vector3(1.0f, 1.0f, 1.0f);
            plugin.NewCharacterHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f);
            plugin.NewCharacterHonorificGradientSet = null;
            plugin.NewCharacterHonorificAnimationStyle = null;
            plugin.NewCharacterMoodlePreset = "";
            plugin.NewCharacterIdlePoseIndex = 7;
            plugin.NewCharacterIsAdvancedMode = false;
            // Reset local temp fields
            tempHonorificTitle = "";
            tempHonorificPrefix = "Prefix";
            tempHonorificSuffix = "Suffix";
            tempHonorificColor = new Vector3(1.0f, 1.0f, 1.0f);
            tempHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f);
            tempHonorificGradientSet = null;
            tempHonorificAnimationStyle = null;
            tempMoodlePreset = "";

            // Reset edit fields
            editedCharacterName = "";
            editedCharacterMacros = "";
            editedCharacterImagePath = null;
            editedCharacterColor = new Vector3(1.0f, 1.0f, 1.0f);
            editedCharacterPenumbra = "";
            editedCharacterGlamourer = "";
            editedCharacterCustomize = "";
            editedCharacterTag = "";
            editedCharacterAutomation = "";
            editedCharacterMoodlePreset = "";
            editedCharacterGearset = null;
            editedCharacterHonorificTitle = "";
            editedCharacterHonorificPrefix = "Prefix";
            editedCharacterHonorificSuffix = "Suffix";
            editedCharacterHonorificColor = new Vector3(1.0f, 1.0f, 1.0f);
            editedCharacterHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f);
            editedCharacterHonorificGradientSet = null;
            editedCharacterHonorificAnimationStyle = null;

            advancedCharacterMacroText = "";

            // Only regenerate macro if not in advanced mode
            if (!isAdvancedModeCharacter)
            {
                plugin.NewCharacterMacros = GenerateMacro();
            }
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
            character.CharacterAutomation = editedCharacterAutomation;
            character.HonorificTitle = editedCharacterHonorificTitle;
            character.HonorificPrefix = editedCharacterHonorificPrefix;
            character.HonorificSuffix = editedCharacterHonorificSuffix;
            character.HonorificColor = editedCharacterHonorificColor;
            character.HonorificGlow = editedCharacterHonorificGlow;
            character.HonorificGradientSet = editedCharacterHonorificGradientSet;
            character.HonorificAnimationStyle = editedCharacterHonorificAnimationStyle;
            character.MoodlePreset = editedCharacterMoodlePreset;
            character.AssignedGearset = editedCharacterGearset;
            character.ExcludeFromNameSync = editedCharacterExcludeFromNameSync;

            character.Macros = isAdvancedModeCharacter ? advancedCharacterMacroText : editedCharacterMacros;

            if (!string.IsNullOrEmpty(editedCharacterImagePath))
            {
                character.ImagePath = editedCharacterImagePath;
            }

            // Note: SecretModState is handled directly in the SecretModeModWindow callback
            // and doesn't need to be copied here since it's already persisted to the character object

            plugin.SaveConfiguration();

            // Check if name changed and user has an active warning
            if (!string.IsNullOrEmpty(editedCharacterName) &&
                editedCharacterName != originalCharacterName &&
                plugin.ActiveNameWarning != null)
            {
                // Fire and forget - check name change for warning resolution
                _ = CheckNameChangeForWarningAsync(editedCharacterName);
            }
        }

        private async System.Threading.Tasks.Task CheckNameChangeForWarningAsync(string newName)
        {
            try
            {
                var result = await plugin.CheckNameChangeForWarning(newName);

                if (result.HasWarning && !string.IsNullOrEmpty(result.Message))
                {
                    // Show feedback in chat
                    Plugin.Framework.RunOnTick(() =>
                    {
                        if (result.Resolved)
                        {
                            // Green success message
                            var msg = new DalamudSeStringBuilder()
                                .AddText("[")
                                .AddGreen("CS+", true)
                                .AddText("] ")
                                .AddGreen(result.Message, false)
                                .Build();
                            Plugin.ChatGui.Print(msg);
                        }
                        else if (result.PendingReview)
                        {
                            // Yellow pending message
                            var msg = new DalamudSeStringBuilder()
                                .AddText("[")
                                .AddYellow("CS+", true)
                                .AddText("] ")
                                .AddYellow(result.Message, false)
                                .Build();
                            Plugin.ChatGui.Print(msg);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[CharacterForm] Error checking name change: {ex.Message}");
            }
        }

        public void OpenEditCharacterWindow(int index)
        {
            if (index < 0 || index >= plugin.Characters.Count)
                return;

            selectedCharacterIndex = index;
            var character = plugin.Characters[index];

            string pluginDirectory = plugin.PluginDirectory;
            string defaultImagePath = Path.Combine(pluginDirectory, "Assets", "Default.png");

            editedCharacterName = character.Name;
            originalCharacterName = character.Name; // Store original for warning resolution check
            editedCharacterPenumbra = character.PenumbraCollection;
            editedCharacterGlamourer = character.GlamourerDesign;
            editedCharacterCustomize = character.CustomizeProfile ?? "";
            editedCharacterColor = character.NameplateColor;

            editedCharacterMacros = character.Macros;

            editedCharacterImagePath = !string.IsNullOrEmpty(character.ImagePath) ? character.ImagePath : defaultImagePath;
            editedCharacterTag = character.Tags != null && character.Tags.Count > 0
                ? string.Join(", ", character.Tags)
                : "";

            editedCharacterHonorificTitle = character.HonorificTitle ?? "";
            editedCharacterHonorificPrefix = character.HonorificPrefix ?? "Prefix";
            editedCharacterHonorificSuffix = character.HonorificSuffix ?? "Suffix";
            editedCharacterHonorificColor = character.HonorificColor;
            editedCharacterHonorificGlow = character.HonorificGlow;
            editedCharacterHonorificGradientSet = character.HonorificGradientSet;
            editedCharacterHonorificAnimationStyle = character.HonorificAnimationStyle;
            editedCharacterMoodlePreset = character.MoodlePreset ?? "";
            editedCharacterGearset = character.AssignedGearset;
            editedCharacterExcludeFromNameSync = character.ExcludeFromNameSync;

            string safeAutomation = character.CharacterAutomation == "None" ? "" : character.CharacterAutomation ?? "";
            editedCharacterAutomation = safeAutomation;

            // Copy to temp fields
            tempHonorificTitle = editedCharacterHonorificTitle;
            tempHonorificPrefix = editedCharacterHonorificPrefix;
            tempHonorificSuffix = editedCharacterHonorificSuffix;
            tempHonorificColor = editedCharacterHonorificColor;
            tempHonorificGlow = editedCharacterHonorificGlow;
            tempHonorificGradientSet = editedCharacterHonorificGradientSet;
            tempHonorificAnimationStyle = editedCharacterHonorificAnimationStyle;
            tempMoodlePreset = editedCharacterMoodlePreset;

            if (isAdvancedModeCharacter)
            {
                advancedCharacterMacroText = character.Macros;
            }
            // Restore advanced mode state
            isAdvancedModeCharacter = character.IsAdvancedMode;

            if (isAdvancedModeCharacter)
            {
                advancedCharacterMacroText = character.Macros;
            }
            IsEditWindowOpen = true;
        }

        private void ValidateCharacterName(string name)
        {
            nameValidationError = "";
            
            if (string.IsNullOrWhiteSpace(name))
                return;

            // Check if name already exists
            bool nameExists;
            if (IsEditWindowOpen && selectedCharacterIndex >= 0 && selectedCharacterIndex < plugin.Characters.Count)
            {
                // When editing, exclude the current character from the check
                var currentCharName = plugin.Characters[selectedCharacterIndex].Name;
                nameExists = plugin.Characters.Any(c => 
                    !c.Name.Equals(currentCharName, StringComparison.OrdinalIgnoreCase) && 
                    c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                // When creating new, check all characters
                nameExists = plugin.Characters.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }

            if (nameExists)
            {
                nameValidationError = "You already have a character with this name. Please choose a different name. " +
                                    "Try adding a number or variation (e.g., Name 2, Name Alt, etc.)";
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using ImGuiNET;
using Dalamud.Interface;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin.Windows.Components
{
    public class ReorderWindow : IDisposable
    {
        private Plugin plugin;
        private UIStyles uiStyles;

        public bool IsOpen { get; private set; } = false;
        private List<Character> reorderBuffer = new();

        public ReorderWindow(Plugin plugin, UIStyles uiStyles)
        {
            this.plugin = plugin;
            this.uiStyles = uiStyles;
        }

        public void Dispose()
        {
        }

        public void Draw()
        {
            if (!IsOpen)
                return;

            // Calculate dynamic window size based on DPI and UI scale
            var dpiScale = ImGui.GetIO().DisplayFramebufferScale.X;
            var uiScale = plugin.Configuration.UIScaleMultiplier;
            var totalScale = GetSafeScale(dpiScale * uiScale);

            // Base dimensions
            var windowWidth = 500f * totalScale;
            var minHeight = 300f * totalScale;
            var maxHeight = 800f * totalScale;

            // Calculate dynamic height based on number of characters
            float headerHeight = 30f * totalScale;
            float buttonHeight = 80f * totalScale; // Bottom buttons + padding
            float characterRowHeight = 68f * totalScale; // Each character row (icon + padding)

            // Calculate content height based on character count
            float contentHeight = reorderBuffer.Count * characterRowHeight;

            // Total window height = header + content + buttons (with reasonable limits) who'd have thought math would be involved...
            var windowHeight = Math.Clamp(
                headerHeight + contentHeight + buttonHeight,
                minHeight,
                maxHeight
            );

            // Center the window on screen
            var viewport = ImGui.GetMainViewport();
            var centerPos = new Vector2(
                viewport.Pos.X + (viewport.Size.X - windowWidth) * 0.5f,
                viewport.Pos.Y + (viewport.Size.Y - windowHeight) * 0.5f
            );

            ImGui.SetNextWindowPos(centerPos, ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(windowWidth, windowHeight), ImGuiCond.Always);

            bool isOpenRef = IsOpen;
            var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize;

            if (ImGui.Begin("Reorder Characters", ref isOpenRef, windowFlags))
            {
                IsOpen = isOpenRef;

                // Apply modern styling that scales with DPI
                ApplyScaledStyles(totalScale);

                try
                {
                    DrawReorderContent(totalScale);
                }
                finally
                {
                    PopScaledStyles();
                }
            }
            ImGui.End();

            if (!IsOpen)
            {
                // Clean up when window is closed
                reorderBuffer.Clear();
            }
        }

        private void ApplyScaledStyles(float scale)
        {
            // Style 
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.1f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.1f, 0.1f, 0.12f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.95f, 0.95f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.16f, 0.16f, 0.2f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.22f, 0.22f, 0.28f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.28f, 0.28f, 0.35f, 1.0f));

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5.0f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8.0f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8 * scale, 5 * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6 * scale, 3 * scale));
        }

        private void PopScaledStyles()
        {
            ImGui.PopStyleVar(4);
            ImGui.PopStyleColor(6);
        }

        public void Open()
        {
            IsOpen = true;
            reorderBuffer = plugin.Characters.ToList();
        }

        private void DrawReorderContent(float scale)
        {
            // Instruction text at the top
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.8f, 1f));
            ImGui.Text("Drag character rows to reorder them");
            ImGui.PopStyleColor();

            ImGui.Separator();
            ImGui.Spacing();

            // Calculate scroll area height
            float buttonAreaHeight = 80f * scale;
            float scrollHeight = ImGui.GetContentRegionAvail().Y - buttonAreaHeight;

            // Scrollable character list
            ImGui.BeginChild("CharacterReorderScroll", new Vector2(0, scrollHeight), true);
            DrawCharacterList(scale);
            ImGui.EndChild();

            // Bottom buttons
            DrawActionButtons(scale);
        }

        private void DrawCharacterList(float scale)
        {
            for (int i = 0; i < reorderBuffer.Count; i++)
            {
                var character = reorderBuffer[i];

                ImGui.PushID(i);
                DrawCharacterRow(character, i, scale);
                ImGui.PopID();
            }
        }

        private void DrawCharacterRow(Character character, int index, float scale)
        {
            float iconSize = 48 * scale;
            float rowHeight = iconSize + (16 * scale);

            var cursorStart = ImGui.GetCursorScreenPos();
            var rowMin = cursorStart;
            var rowMax = cursorStart + new Vector2(ImGui.GetContentRegionAvail().X, rowHeight);

            // Invisible button
            ImGui.SetCursorScreenPos(rowMin);
            ImGui.InvisibleButton($"##CharRow{index}", new Vector2(ImGui.GetContentRegionAvail().X, rowHeight));
            bool isHovered = ImGui.IsItemHovered();
            HandleDragAndDrop(index, scale);

            // Background based on hover
            if (isHovered)
            {
                var hoverColor = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 0.5f));
                ImGui.GetWindowDrawList().AddRectFilled(rowMin, rowMax, hoverColor, 6f * scale);
            }

            // Subtle border around each row
            var borderColor = ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.4f, 0.3f));
            ImGui.GetWindowDrawList().AddRect(rowMin, rowMax, borderColor, 6f * scale, ImDrawFlags.None, 1f);

            // Character image
            float imageMargin = 8 * scale;
            if (!string.IsNullOrEmpty(character.ImagePath) && File.Exists(character.ImagePath))
            {
                var texture = Plugin.TextureProvider.GetFromFile(character.ImagePath).GetWrapOrDefault();
                if (texture != null)
                {
                    // Calculate image dimensions
                    float originalWidth = texture.Width;
                    float originalHeight = texture.Height;
                    float aspectRatio = originalWidth / originalHeight;

                    float displayWidth = iconSize;
                    float displayHeight = iconSize;

                    if (aspectRatio > 1) // Landscape
                    {
                        displayHeight = iconSize / aspectRatio;
                    }
                    else if (aspectRatio < 1) // Portrait
                    {
                        displayWidth = iconSize * aspectRatio;
                    }

                    // Center the image
                    float offsetX = (iconSize - displayWidth) / 2;
                    float offsetY = (iconSize - displayHeight) / 2;

                    ImGui.SetCursorScreenPos(cursorStart + new Vector2(imageMargin + offsetX, imageMargin + offsetY));
                    ImGui.Image(texture.ImGuiHandle, new Vector2(displayWidth, displayHeight));

                    // Add glowing border around image based on character colour
                    var imageMin = cursorStart + new Vector2(imageMargin, imageMargin);
                    var imageMax = imageMin + new Vector2(iconSize, iconSize);
                    uiStyles.DrawGlowingBorder(imageMin, imageMax, character.NameplateColor, 0.6f, isHovered);
                }
            }

            // Character name and details
            float textStartX = imageMargin + iconSize + (12 * scale);
            ImGui.SetCursorScreenPos(cursorStart + new Vector2(textStartX, imageMargin));

            // Character name with colour
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(character.NameplateColor, 1f));
            ImGui.Text(character.Name);
            ImGui.PopStyleColor();

            // Character details
            float lineHeight = ImGui.GetTextLineHeight();
            ImGui.SetCursorScreenPos(cursorStart + new Vector2(textStartX, imageMargin + lineHeight + (4 * scale)));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1f));

            string details = $"Designs: {character.Designs.Count}";
            if (character.Tags != null && character.Tags.Count > 0)
            {
                details += $" | Tags: {string.Join(", ", character.Tags.Take(2))}{(character.Tags.Count > 2 ? "..." : "")}";
            }

            ImGui.Text(details);
            ImGui.PopStyleColor();

            // Favourite star
            if (character.IsFavorite)
            {
                float rowWidth = ImGui.GetContentRegionAvail().X;
                ImGui.SetCursorScreenPos(rowMin + new Vector2(rowWidth - (30 * scale), imageMargin));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.9f, 0.2f, 1f));
                ImGui.Text("★");
                ImGui.PopStyleColor();
            }

            // Show drag cursor when hovering
            if (isHovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }

            ImGui.SetCursorScreenPos(new Vector2(rowMin.X, rowMax.Y + (4 * scale)));
        }

        private unsafe void HandleDragAndDrop(int index, float scale)
        {
            // Drag source
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceAllowNullID))
            {
                int dragIndex = index;
                ImGui.SetDragDropPayload("CHARACTER_REORDER", new nint(Unsafe.AsPointer(ref dragIndex)), (uint)sizeof(int));

                // Ghost image for drag...race?
                var character = reorderBuffer[index];

                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.1f, 0.1f, 0.1f, 0.95f));
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8 * scale, 6 * scale));
                ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);

                ImGui.BeginGroup();

                // Mini character preview
                if (!string.IsNullOrEmpty(character.ImagePath) && File.Exists(character.ImagePath))
                {
                    var texture = Plugin.TextureProvider.GetFromFile(character.ImagePath).GetWrapOrDefault();
                    if (texture != null)
                    {
                        float previewSize = 32 * scale;
                        ImGui.Image(texture.ImGuiHandle, new Vector2(previewSize, previewSize));
                        ImGui.SameLine();
                    }
                }

                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(character.NameplateColor, 1f));
                ImGui.Text(character.Name);
                ImGui.PopStyleColor();

                ImGui.EndGroup();

                ImGui.PopStyleVar(2);
                ImGui.PopStyleColor(2);
                ImGui.EndDragDropSource();
            }

            // Drop target
            if (ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("CHARACTER_REORDER");
                if (payload.NativePtr != null)
                {
                    int dragIndex = *(int*)payload.Data;
                    if (dragIndex >= 0 && dragIndex < reorderBuffer.Count && dragIndex != index)
                    {
                        var item = reorderBuffer[dragIndex];
                        reorderBuffer.RemoveAt(dragIndex);
                        reorderBuffer.Insert(index, item);
                    }
                }
                ImGui.EndDragDropTarget();
            }

            // Visual feedback during drag
            if (ImGui.IsItemHovered() && ImGui.GetDragDropPayload().NativePtr != null)
            {
                var itemMin = ImGui.GetItemRectMin();
                var itemMax = ImGui.GetItemRectMax();
                var color = ImGui.GetColorU32(new Vector4(0.3f, 0.7f, 1f, 0.8f));
                ImGui.GetWindowDrawList().AddRect(itemMin, itemMax, color, 6f * scale, ImDrawFlags.None, 2f);
            }
        }

        private void DrawActionButtons(float scale)
        {
            ImGui.Separator();

            // Show (just the) tips with scaled icon
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf0c4"); // Link/chain icon
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.Text("Tip: Drag characters by their entire row to reorder them.");
            ImGui.PopStyleColor();

            ImGui.Spacing();

            float buttonWidth = 120 * scale;
            float spacing = 20 * scale;
            float buttonHeight = 30 * scale;
            float totalWidth = (buttonWidth * 2) + spacing;
            float centerX = (ImGui.GetWindowContentRegionMax().X - totalWidth) / 2f;

            ImGui.SetCursorPosX(centerX);

            // Save Order button
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.5f, 0.3f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.6f, 0.4f, 1.0f));

            if (ImGui.Button("Save Order", new Vector2(buttonWidth, buttonHeight)))
            {
                SaveReorderedCharacters();
                IsOpen = false;
            }

            ImGui.PopStyleColor(3);

            ImGui.SameLine(0, spacing);

            // Cancel button
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.2f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.3f, 0.3f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.4f, 0.4f, 1.0f));

            if (ImGui.Button("Cancel", new Vector2(buttonWidth, buttonHeight)))
            {
                IsOpen = false;
            }

            ImGui.PopStyleColor(3);
        }

        private void SaveReorderedCharacters()
        {
            // Update sort orders
            for (int i = 0; i < reorderBuffer.Count; i++)
            {
                reorderBuffer[i].SortOrder = i;
            }

            plugin.Characters.Clear();
            plugin.Characters.AddRange(reorderBuffer);

            plugin.Configuration.CurrentSortIndex = (int)Plugin.SortType.Manual;
            plugin.SaveConfiguration();

            reorderBuffer.Clear();
        }
        private float GetSafeScale(float baseScale)
        {
            return Math.Clamp(baseScale, 0.3f, 5.0f); // Prevent extreme scaling
        }
    }
}

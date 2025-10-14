using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin.Windows.Components
{
    public class ReorderWindow : IDisposable
    {
        private Plugin plugin;
        private UIStyles uiStyles;

        public bool IsOpen { get; private set; } = false;
        private List<Character> reorderBuffer = new();
        
        // Custom drag system (copied from CharacterGrid)
        private int? draggedCharacterIndex = null;
        private bool isDragging = false;
        private Vector2 dragStartPos = Vector2.Zero;
        private const float DragThreshold = 5f;
        private int? currentDropTargetIndex = null;

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

            // Calculate dynamic window size - use UI scale multiplier only, let Dalamud handle DPI
            var totalScale = GetSafeScale(plugin.Configuration.UIScaleMultiplier);

            // Base dimensions (unscaled, let Dalamud handle DPI)
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

            // Make the entire row a selectable item that can be dragged
            ImGui.SetCursorScreenPos(rowMin);
            bool isSelected = false; // You could track selected state if needed
            bool clicked = ImGui.Selectable($"##CharRow{index}", isSelected, ImGuiSelectableFlags.SpanAllColumns, new Vector2(ImGui.GetContentRegionAvail().X, rowHeight));
            bool isHovered = ImGui.IsItemHovered();

            // Custom drag system - no ImGui drag-drop API
            // Start dragging
            if (ImGui.IsItemActive() && draggedCharacterIndex == null)
            {
                dragStartPos = ImGui.GetMousePos();
                draggedCharacterIndex = index;
                isDragging = false;
            }

            // Check if we've moved far enough to start dragging
            if (draggedCharacterIndex == index && ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                Vector2 currentPos = ImGui.GetMousePos();
                float distance = Vector2.Distance(dragStartPos, currentPos);

                if (distance > DragThreshold * scale)
                {
                    isDragging = true;
                }
            }

            // Find drop target during dragging
            if (isDragging && draggedCharacterIndex != null)
            {
                bool hoveringArea = ImGui.IsMouseHoveringRect(rowMin, rowMax);
                if (hoveringArea && index != draggedCharacterIndex)
                {
                    currentDropTargetIndex = index;
                    
                    // Draw drop zone visual feedback
                    var drawList = ImGui.GetWindowDrawList();
                    uint dropZoneColor = ImGui.GetColorU32(new Vector4(0.3f, 0.7f, 1f, 0.8f));
                    drawList.AddRect(rowMin - new Vector2(2 * scale, 2 * scale), 
                                   rowMax + new Vector2(2 * scale, 2 * scale), 
                                   dropZoneColor, 6f * scale, ImDrawFlags.None, 3f * scale);
                }
            }

            // End dragging and perform reorder
            if (draggedCharacterIndex == index && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                if (isDragging && currentDropTargetIndex.HasValue)
                {
                    int sourceIndex = draggedCharacterIndex.Value;
                    int targetIndex = currentDropTargetIndex.Value;
                    
                    if (sourceIndex != targetIndex && sourceIndex >= 0 && sourceIndex < reorderBuffer.Count)
                    {
                        // Move character from source to target position
                        var draggedChar = reorderBuffer[sourceIndex];
                        reorderBuffer.RemoveAt(sourceIndex);
                        
                        // Adjust target index if we removed an item before it
                        if (sourceIndex < targetIndex)
                            targetIndex--;
                            
                        reorderBuffer.Insert(targetIndex, draggedChar);
                    }
                }
                
                // Reset drag state
                draggedCharacterIndex = null;
                isDragging = false;
                currentDropTargetIndex = null;
            }

            // Draw drag cursor and visual feedback
            if (isDragging && draggedCharacterIndex == index)
            {
                // Keep normal hand cursor while dragging
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                
                // Draw ghost image of the character being dragged
                var mousePos = ImGui.GetMousePos();
                var ghostAlpha = 0.7f;
                
                // Draw ghost character row following mouse
                var ghostMin = mousePos + new Vector2(5, 5);
                var ghostMax = ghostMin + new Vector2(300 * scale, rowHeight);
                
                // Semi-transparent background for ghost
                var ghostBgColor = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, ghostAlpha));
                ImGui.GetForegroundDrawList().AddRectFilled(ghostMin, ghostMax, ghostBgColor, 6f * scale);
                
                // Ghost border
                var ghostBorderColor = ImGui.GetColorU32(new Vector4(character.NameplateColor, ghostAlpha));
                ImGui.GetForegroundDrawList().AddRect(ghostMin, ghostMax, ghostBorderColor, 6f * scale, ImDrawFlags.None, 2f);
                
                // Ghost character image
                if (!string.IsNullOrEmpty(character.ImagePath) && File.Exists(character.ImagePath))
                {
                    var texture = Plugin.TextureProvider.GetFromFile(character.ImagePath).GetWrapOrDefault();
                    if (texture != null)
                    {
                        var ghostImageSize = iconSize * 0.8f; // Slightly smaller
                        var ghostImagePos = ghostMin + new Vector2(8 * scale, 8 * scale);
                        
                        // Draw ghost image with transparency
                        ImGui.GetForegroundDrawList().AddImage(
                            (ImTextureID)texture.Handle,
                            ghostImagePos,
                            ghostImagePos + new Vector2(ghostImageSize, ghostImageSize),
                            Vector2.Zero,
                            Vector2.One,
                            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, ghostAlpha))
                        );
                    }
                }
                
                // Ghost text
                var ghostTextPos = ghostMin + new Vector2(iconSize + 16 * scale, 12 * scale);
                ImGui.GetForegroundDrawList().AddText(
                    ghostTextPos,
                    ImGui.GetColorU32(new Vector4(character.NameplateColor, ghostAlpha)),
                    character.Name
                );
            }

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
                    ImGui.Image((ImTextureID)texture.Handle, new Vector2(displayWidth, displayHeight));

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

            // Update CharacterGrid to use the new sort type
            plugin.MainWindow.UpdateSortType();

            reorderBuffer.Clear();
        }
        private float GetSafeScale(float baseScale)
        {
            return Math.Clamp(baseScale, 0.3f, 5.0f); // Prevent extreme scaling
        }
    }
}

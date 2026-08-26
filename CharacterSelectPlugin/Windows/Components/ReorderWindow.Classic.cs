using System;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin.Windows.Components
{
    public partial class ReorderWindow
    {
        private int themeColorCount = 0;
        private int themeStyleVarCount = 0;

        private static readonly Vector4 ClassicAccent = new(0.3f, 0.7f, 1f, 1f);
        private (int From, int To)? classicPageMove = null;

        private void DrawClassicLayout()
        {
            if (!IsOpen)
                return;

            var totalScale = GetSafeScale(plugin.Configuration.UIScaleMultiplier);
            var windowWidth = 500f * totalScale;

            // Size the window to fit the pages, within limits
            float gap = 6f * totalScale;
            float pad = 8f * totalScale;
            float innerW = windowWidth - 40f * totalScale;
            float rowH;
            int perRow;
            if (plugin.Configuration.ReorderPagesMatchGrid)
            {
                perRow = Math.Max(1, plugin.Configuration.ProfileColumns);
                float cardW = MathF.Min((innerW - pad * 2f - (perRow - 1) * gap) / perRow, 96f * totalScale);
                rowH = cardW * 1.375f + gap;
            }
            else
            {
                float cardW = 64f * totalScale;
                perRow = Math.Max(1, (int)((innerW - pad * 2f + gap) / (cardW + gap)));
                rowH = 94f * totalScale;
            }
            float contentHeight = 0f;
            foreach (var page in pageBuffer)
            {
                int rows = Math.Max(1, (int)Math.Ceiling(page.Count / (double)perRow));
                contentHeight += 34f * totalScale + pad * 2f + rows * rowH + 16f * totalScale;
            }

            var windowHeight = Math.Clamp(contentHeight + 200f * totalScale, 350f * totalScale, 800f * totalScale);

            // Center the window on screen
            var viewport = ImGui.GetMainViewport();
            var centerPos = new Vector2(
                viewport.Pos.X + (viewport.Size.X - windowWidth) * 0.5f,
                viewport.Pos.Y + (viewport.Size.Y - windowHeight) * 0.5f);

            ImGui.SetNextWindowPos(centerPos, ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(windowWidth, windowHeight), ImGuiCond.Always);

            bool isOpenRef = IsOpen;
            var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize;

            if (ImGui.Begin("Reorder Characters", ref isOpenRef, windowFlags))
            {
                IsOpen = isOpenRef;
                ClassicApplyScaledStyles(totalScale);
                try
                {
                    DrawClassicPagesContent(totalScale);
                }
                finally
                {
                    ClassicPopScaledStyles();
                }
            }
            ImGui.End();

            if (!IsOpen)
            {
                // Clean up when window is closed
                reorderBuffer.Clear();
                pageBuffer.Clear();
                pageNameBuffer.Clear();
                pageSelection.Clear();
                ClearPagesPreview();
            }
        }

        private void ClassicApplyScaledStyles(float scale)
        {
            // Apply theme colors (supports Custom theme, seasonal themes, and default)
            themeColorCount = ThemeHelper.PushThemeColors(plugin.Configuration);
            themeStyleVarCount = ThemeHelper.PushThemeStyleVars(plugin.Configuration.UIScaleMultiplier);
        }

        private void ClassicPopScaledStyles()
        {
            ThemeHelper.PopThemeStyleVars(themeStyleVarCount);
            ThemeHelper.PopThemeColors(themeColorCount);
        }

        private void DrawClassicPagesContent(float scale)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.8f, 1f));
            ImGui.TextWrapped("Drag characters between pages to organise them. Ctrl+Click selects several at once, right-click moves via menu.");
            ImGui.PopStyleColor();

            bool mg = plugin.Configuration.ReorderPagesMatchGrid;
            if (ImGui.Checkbox("Match roster grid layout", ref mg))
            {
                plugin.Configuration.ReorderPagesMatchGrid = mg;
                plugin.SaveConfiguration();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Cards use the same profiles per row as the main window");

            ImGui.Separator();
            ImGui.Spacing();

            float buttonAreaHeight = 84f * scale;
            float scrollHeight = ImGui.GetContentRegionAvail().Y - buttonAreaHeight;

            pDropPage = -1;
            pDropIndex = -1;

            // Scrollable page list
            ImGui.BeginChild("ClassicPagesScroll", new Vector2(0, scrollHeight), true);
            for (int p = 0; p < pageBuffer.Count; p++)
                DrawClassicFolder(p, scale);

            // Also acts as a drop target
            if (ImGui.Button("+ Add Page", new Vector2(ImGui.GetContentRegionAvail().X, 26f * scale)) && !pDragActive)
                AddNewPage(null);
            var addMin = ImGui.GetItemRectMin();
            var addMax = ImGui.GetItemRectMax();
            if (pDragActive && pDragCard != null && ImGui.IsMouseHoveringRect(addMin, addMax))
            {
                ImGui.GetWindowDrawList().AddRect(addMin, addMax, ImGui.GetColorU32(ClassicAccent), 4f * scale, ImDrawFlags.None, 2f);
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    AddNewPage(pDragCard);
                    ResetCardDrag();
                }
            }

            DrawMoveToPopup();
            DragAutoScroll(scale);
            ImGui.EndChild();

            // Applied after the loop so the page list isn't mutated mid-draw
            if (pendingDeletePage >= 0)
            {
                DeletePage(pendingDeletePage);
                pendingDeletePage = -1;
            }
            if (classicPageMove.HasValue)
            {
                MovePage(classicPageMove.Value.From, classicPageMove.Value.To);
                classicPageMove = null;
            }
            if (pDragActive)
                DrawClassicCardGhost(scale);
            ResolveCardDragRelease();

            DrawClassicPagesButtons(scale);
        }

        private void DrawClassicFolder(int p, float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            var cards = pageBuffer[p];
            ImGui.PushID(p);

            // Page header: number, name, reorder arrows, delete
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ClassicAccent, $"Page {p + 1}");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(140f * scale);
            string name = pageNameBuffer[p];
            if (ImGui.InputTextWithHint("##pgname", "name (optional)", ref name, 40))
            {
                pageNameBuffer[p] = name;
                PublishPagesPreview();
            }
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.SmallButton("##pgup") && p > 0)
                classicPageMove = (p, p - 1);
            ImGui.PopFont();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Move page up");
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.SmallButton("##pgdn") && p < pageBuffer.Count - 1)
                classicPageMove = (p, p + 2);
            ImGui.PopFont();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Move page down");
            if (pageBuffer.Count > 1 && cards.Count == 0)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Delete"))
                    pendingDeletePage = p;
            }
            ImGui.SameLine();
            ImGui.TextDisabled(cards.Count == 1 ? "1 character" : $"{cards.Count} characters");

            // Card grid, optionally matching the roster's profiles per row
            float availW = ImGui.GetContentRegionAvail().X;
            float gap = 6f * scale;
            float pad = 8f * scale;
            float cardW, cardH;
            int perRow;
            if (plugin.Configuration.ReorderPagesMatchGrid)
            {
                perRow = Math.Max(1, plugin.Configuration.ProfileColumns);
                cardW = MathF.Min((availW - pad * 2f - (perRow - 1) * gap) / perRow, 96f * scale);
                cardH = cardW * 1.375f;
            }
            else
            {
                cardW = 64f * scale;
                cardH = 88f * scale;
                perRow = Math.Max(1, (int)((availW - pad * 2f + gap) / (cardW + gap)));
            }
            int rows = Math.Max(1, (int)Math.Ceiling(cards.Count / (double)perRow));
            float bodyH = pad * 2f + rows * cardH + (rows - 1) * gap;

            var bodyMin = ImGui.GetCursorScreenPos();
            var bodyMax = bodyMin + new Vector2(availW, bodyH);
            ImGui.Dummy(new Vector2(availW, bodyH));

            bool isDropTargetPage = pDragActive && pDropPage == p;
            uint bg = ImGui.GetColorU32(isDropTargetPage
                ? new Vector4(0.3f, 0.7f, 1f, 0.10f)
                : new Vector4(1f, 1f, 1f, 0.03f));
            uint border = ImGui.GetColorU32(isDropTargetPage
                ? new Vector4(0.3f, 0.7f, 1f, 0.8f)
                : new Vector4(0.4f, 0.4f, 0.4f, 0.3f));
            dl.AddRectFilled(bodyMin, bodyMax, bg, 6f * scale);
            dl.AddRect(bodyMin, bodyMax, border, 6f * scale, ImDrawFlags.None, 1f);

            if (cards.Count == 0)
            {
                string hint = "Drop characters here";
                var sz = ImGui.CalcTextSize(hint);
                dl.AddText(new Vector2(bodyMin.X + (availW - sz.X) * 0.5f, bodyMin.Y + (bodyH - sz.Y) * 0.5f),
                    ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 0.8f)), hint);
            }

            for (int i = 0; i < cards.Count; i++)
            {
                int row = i / perRow;
                int col = i % perRow;
                var cMin = new Vector2(bodyMin.X + pad + col * (cardW + gap), bodyMin.Y + pad + row * (cardH + gap));
                DrawClassicMiniCard(dl, cards[i], p, i, cMin, cardW, cardH, scale);
            }

            // Insertion marker while dragging over this page
            if (pDragActive && pDragCard != null)
            {
                var mp = ImGui.GetMousePos();
                if (mp.X >= bodyMin.X && mp.X <= bodyMax.X && mp.Y >= bodyMin.Y && mp.Y <= bodyMax.Y)
                {
                    pDropPage = p;
                    pDropIndex = CardInsertIndex(mp, bodyMin, pad, gap, cardW, cardH, perRow, cards.Count);
                    if (cards.Count > 0)
                    {
                        var top = InsertLineTop(pDropIndex, cards.Count, bodyMin, pad, gap, cardW, cardH, perRow);
                        dl.AddRectFilled(new Vector2(top.X - 1f * scale, top.Y), new Vector2(top.X + 1f * scale, top.Y + cardH),
                            ImGui.GetColorU32(new Vector4(0.3f, 0.7f, 1f, 0.9f)));
                    }
                }
            }

            ImGui.SetCursorScreenPos(new Vector2(bodyMin.X, bodyMax.Y));
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.PopID();
        }

        private void DrawClassicMiniCard(ImDrawListPtr dl, Character character, int pageIdx, int cardIdx,
            Vector2 min, float cardW, float cardH, float scale)
        {
            var max = min + new Vector2(cardW, cardH);
            float rounding = 5f * scale;

            ImGui.SetCursorScreenPos(min);
            ImGui.InvisibleButton($"##ccard_{pageIdx}_{cardIdx}", new Vector2(cardW, cardH));
            bool hovered = ImGui.IsItemHovered();
            bool selected = pageSelection.Contains(character);
            bool isDraggingThis = pDragActive && pDragCard == character;

            MiniCardBehaviour(character, pageIdx, scale);

            // Portrait fills the card above the name band
            float bandH = 20f * scale;
            var portMax = new Vector2(max.X, max.Y - bandH);
            var npV = character.NameplateColor;

            dl.AddRectFilled(min, portMax, ImGui.GetColorU32(new Vector4(npV * 0.35f, 1f)),
                rounding, ImDrawFlags.RoundCornersTop);
            if (!string.IsNullOrEmpty(character.ImagePath) && File.Exists(character.ImagePath))
            {
                try
                {
                    var tex = Plugin.TextureProvider.GetFromFile(character.ImagePath).GetWrapOrDefault();
                    if (tex != null && tex.Width > 0 && tex.Height > 0)
                    {
                        var (uv0, uv1) = CoverUv(tex.Width, tex.Height, cardW, portMax.Y - min.Y);
                        dl.AddImageRounded((ImTextureID)tex.Handle, min, portMax, uv0, uv1,
                            0xFFFFFFFF, rounding, ImDrawFlags.RoundCornersTop);
                    }
                }
                catch { }
            }

            dl.AddRectFilled(new Vector2(min.X, portMax.Y), max,
                ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.14f, 1f)),
                rounding, ImDrawFlags.RoundCornersBottom);
            string nameText = plugin.GetRosterDisplayName(character);
            var nameSz = ImGui.CalcTextSize(nameText);
            dl.PushClipRect(new Vector2(min.X + 2f * scale, portMax.Y), max - new Vector2(2f * scale, 0), true);
            dl.AddText(new Vector2(min.X + MathF.Max(2f * scale, (cardW - nameSz.X) * 0.5f),
                                   portMax.Y + (bandH - nameSz.Y) * 0.5f),
                ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1f)), nameText);
            dl.PopClipRect();

            if (character.IsFavorite)
            {
                dl.AddText(new Vector2(min.X + 3f * scale, min.Y + 2f * scale),
                    ImGui.GetColorU32(new Vector4(1f, 0.9f, 0.2f, 1f)), "★");
            }

            if (selected)
                dl.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.3f, 0.7f, 1f, 0.10f)), rounding);

            // Accent border for selected or dragged cards
            Vector4 borderCol;
            float borderW;
            if (isDraggingThis || selected) { borderCol = ClassicAccent; borderW = 2f; }
            else if (hovered) { borderCol = new Vector4(1f, 1f, 1f, 0.35f); borderW = 1f; }
            else { borderCol = new Vector4(0.4f, 0.4f, 0.4f, 0.4f); borderW = 1f; }
            dl.AddRect(min, max, ImGui.GetColorU32(borderCol), rounding, ImDrawFlags.None, borderW);

            if (hovered && !pDragActive)
                ImGui.SetTooltip(nameText);
        }

        // Preview card that follows the cursor while dragging
        private void DrawClassicCardGhost(float scale)
        {
            if (pDragCard == null) return;
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            var fdl = ImGui.GetForegroundDrawList();
            var mouse = ImGui.GetMousePos();
            float w = 52f * scale;
            float h = 68f * scale;
            float rounding = 5f * scale;
            var min = mouse + new Vector2(10f * scale, 6f * scale);
            var max = min + new Vector2(w, h);

            fdl.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 0.85f)), rounding);
            if (!string.IsNullOrEmpty(pDragCard.ImagePath) && File.Exists(pDragCard.ImagePath))
            {
                try
                {
                    var tex = Plugin.TextureProvider.GetFromFile(pDragCard.ImagePath).GetWrapOrDefault();
                    if (tex != null && tex.Width > 0 && tex.Height > 0)
                    {
                        var (uv0, uv1) = CoverUv(tex.Width, tex.Height, w, h);
                        fdl.AddImageRounded((ImTextureID)tex.Handle, min, max, uv0, uv1,
                            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.9f)), rounding);
                    }
                }
                catch { }
            }
            var npV = pDragCard.NameplateColor;
            fdl.AddRect(min, max, ImGui.GetColorU32(new Vector4(npV, 0.9f)), rounding, ImDrawFlags.None, 2f);

            if (pageSelection.Count > 1)
            {
                float r = 9f * scale;
                var c = new Vector2(max.X, min.Y);
                fdl.AddCircleFilled(c, r, ImGui.GetColorU32(ClassicAccent));
                string t = pageSelection.Count.ToString();
                var sz = ImGui.CalcTextSize(t);
                fdl.AddText(new Vector2(c.X - sz.X * 0.5f, c.Y - sz.Y * 0.5f),
                    ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 1f)), t);
            }
        }

        private void DrawClassicPagesButtons(float scale)
        {
            ImGui.Separator();

            if (pageSelection.Count > 0)
                ImGui.TextDisabled(pageSelection.Count == 1 ? "1 selected" : $"{pageSelection.Count} selected");
            else
                ImGui.TextDisabled("Tip: drag a character onto another page to move it.");

            ImGui.Spacing();

            float buttonWidth = 120 * scale;
            float spacing = 20 * scale;
            float buttonHeight = 30 * scale;
            float totalWidth = (buttonWidth * 2) + spacing;
            float centerX = (ImGui.GetWindowContentRegionMax().X - totalWidth) / 2f;

            ImGui.SetCursorPosX(centerX);

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.5f, 0.3f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.6f, 0.4f, 1.0f));
            if (ImGui.Button("Save Roster", new Vector2(buttonWidth, buttonHeight)))
            {
                SavePages();
                IsOpen = false;
            }
            ImGui.PopStyleColor(3);

            ImGui.SameLine(0, spacing);

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.2f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.3f, 0.3f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.4f, 0.4f, 1.0f));
            if (ImGui.Button("Cancel", new Vector2(buttonWidth, buttonHeight)))
            {
                IsOpen = false;
            }
            ImGui.PopStyleColor(3);
        }
    }
}

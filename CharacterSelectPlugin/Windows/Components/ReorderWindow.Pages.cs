using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin.Windows.Components
{
    public partial class ReorderWindow
    {
        public static List<Character>? PagesPreviewOrder = null;
        public static List<int>? PagesPreviewSizes = null;
        public static List<string>? PagesPreviewNames = null;

        private List<List<Character>> pageBuffer = new();
        private List<string> pageNameBuffer = new();
        private readonly HashSet<Character> pageSelection = new();
        private int pagesMovedCount = 0;

        // card drag
        private Character? pDragCard = null;
        private bool pDragActive = false;
        private Vector2 pDragStart = Vector2.Zero;
        private int pDropPage = -1;
        private int pDropIndex = -1;

        // folder tab drag (page reorder)
        private int? pTabDragIndex = null;
        private bool pTabDragging = false;
        private int pTabDropIndex = -1;
        private Vector2 pTabDragStart = Vector2.Zero;

        // rename
        private int renamingPage = -1;
        private string renameBuffer = "";
        private bool renameFocusPending = false;

        // context menu target
        private Character? ctxCard = null;
        private int ctxCardPage = -1;
        private int pendingDeletePage = -1;

        private void InitPagesBuffer(List<Character> order)
        {
            pageBuffer.Clear();
            pageNameBuffer.Clear();
            pageSelection.Clear();
            pagesMovedCount = 0;
            renamingPage = -1;
            pendingDeletePage = -1;
            ctxCard = null;
            ResetCardDrag();
            pTabDragIndex = null;
            pTabDragging = false;

            var cfgPages = plugin.Configuration.RosterPages;
            if (cfgPages.Count == 0)
            {
                // legacy auto pages of 40
                for (int i = 0; i < order.Count; i += 40)
                {
                    pageBuffer.Add(order.Skip(i).Take(40).ToList());
                    pageNameBuffer.Add("");
                }
            }
            else
            {
                int pos = 0;
                foreach (var p in cfgPages)
                {
                    int count = Math.Max(0, Math.Min(p.Size, order.Count - pos));
                    pageBuffer.Add(order.Skip(pos).Take(count).ToList());
                    pageNameBuffer.Add(p.Name ?? "");
                    pos += count;
                }
                if (pos < order.Count)
                    pageBuffer[pageBuffer.Count - 1].AddRange(order.Skip(pos));
            }
            if (pageBuffer.Count == 0)
            {
                pageBuffer.Add(new List<Character>());
                pageNameBuffer.Add("");
            }
        }

        private void ResetCardDrag()
        {
            pDragCard = null;
            pDragActive = false;
            pDropPage = -1;
            pDropIndex = -1;
        }

        private void PublishPagesPreview()
        {
            PagesPreviewOrder = pageBuffer.SelectMany(p => p).ToList();
            PagesPreviewSizes = pageBuffer.Select(p => p.Count).ToList();
            PagesPreviewNames = pageNameBuffer.ToList();
        }

        internal static void ClearPagesPreview()
        {
            PagesPreviewOrder = null;
            PagesPreviewSizes = null;
            PagesPreviewNames = null;
        }

        private List<Character> OrderedSelection()
            => pageBuffer.SelectMany(p => p).Where(c => pageSelection.Contains(c)).ToList();

        private void MoveSelectionTo(int targetPage, int insertIndex, Character grabbed)
        {
            var moving = pageSelection.Contains(grabbed) ? OrderedSelection() : new List<Character> { grabbed };
            if (moving.Count == 0) return;
            if (targetPage < 0 || targetPage >= pageBuffer.Count) return;

            var target = pageBuffer[targetPage];
            int adjusted = Math.Clamp(insertIndex, 0, target.Count);
            foreach (var c in moving)
            {
                int idxInTarget = target.IndexOf(c);
                if (idxInTarget >= 0 && idxInTarget < adjusted) adjusted--;
            }
            foreach (var page in pageBuffer)
                page.RemoveAll(c => moving.Contains(c));
            adjusted = Math.Clamp(adjusted, 0, pageBuffer[targetPage].Count);
            pageBuffer[targetPage].InsertRange(adjusted, moving);
            pagesMovedCount += moving.Count;
            PublishPagesPreview();
        }

        private void AddNewPage(Character? withCard)
        {
            pageBuffer.Add(new List<Character>());
            pageNameBuffer.Add("");
            if (withCard != null)
                MoveSelectionTo(pageBuffer.Count - 1, 0, withCard);
            else
                PublishPagesPreview();
        }

        private void DeletePage(int index)
        {
            if (pageBuffer.Count <= 1 || index < 0 || index >= pageBuffer.Count) return;
            int fold = index > 0 ? index - 1 : 1;
            pageBuffer[fold].AddRange(pageBuffer[index]);
            pageBuffer.RemoveAt(index);
            pageNameBuffer.RemoveAt(index);
            pagesMovedCount++;
            PublishPagesPreview();
        }

        private void MovePage(int from, int to)
        {
            if (from == to || from < 0 || from >= pageBuffer.Count) return;
            var page = pageBuffer[from];
            var name = pageNameBuffer[from];
            pageBuffer.RemoveAt(from);
            pageNameBuffer.RemoveAt(from);
            if (from < to) to--;
            to = Math.Clamp(to, 0, pageBuffer.Count);
            pageBuffer.Insert(to, page);
            pageNameBuffer.Insert(to, name);
            pagesMovedCount++;
            PublishPagesPreview();
        }

        // pages body
        private void DrawPagesBody(Vector2 min, Vector2 max, float scale)
        {
            ImGui.SetCursorScreenPos(min);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 4f * scale);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0, 0, 0, 0.20f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Boutique.WithAlpha(Boutique.GoldDeep, 1f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Boutique.WithAlpha(Boutique.Gold, 0.85f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Boutique.Gold);

            ImGui.BeginChild("##reorder_pages_body", max - min, false,
                ImGuiWindowFlags.AlwaysVerticalScrollbar | ImGuiWindowFlags.NoBackground);

            pDropPage = -1;
            pDropIndex = -1;
            pTabDropIndex = -1;

            ImGui.Dummy(new Vector2(0, 8f * scale));

            for (int p = 0; p < pageBuffer.Count; p++)
                DrawFolder(p, scale);

            // end drop slot for page drags
            if (pTabDragging && pTabDragIndex.HasValue && pTabDragIndex.Value != pageBuffer.Count - 1)
            {
                var mp = ImGui.GetMousePos();
                float endY = ImGui.GetCursorScreenPos().Y - 6f * scale;
                if (mp.Y >= endY - 8f * scale)
                {
                    float margin = 12f * scale;
                    float x0 = ImGui.GetWindowPos().X + margin;
                    float x1 = x0 + ImGui.GetWindowSize().X - margin * 2f;
                    pTabDropIndex = pageBuffer.Count;
                    ImGui.GetWindowDrawList().AddRectFilled(
                        new Vector2(x0, endY), new Vector2(x1, endY + 2f * scale),
                        Boutique.U32(Boutique.Gold));
                }
            }

            DrawNewPageGhost(scale);
            DrawMoveToPopup();
            DragAutoScroll(scale);

            ImGui.Dummy(new Vector2(0, 4f * scale));
            ImGui.EndChild();
            ImGui.PopStyleColor(5);
            ImGui.PopStyleVar();

            if (pendingDeletePage >= 0)
            {
                DeletePage(pendingDeletePage);
                pendingDeletePage = -1;
            }
            if (pDragActive)
                DrawCardDragGhost(scale);
            ResolveCardDragRelease();
            ResolveTabDrag();
        }

        private void DragAutoScroll(float scale)
        {
            if (!pDragActive && !pTabDragging) return;
            var wp = ImGui.GetWindowPos();
            var ws = ImGui.GetWindowSize();
            var mp = ImGui.GetMousePos();
            float edge = 44f * scale;
            float speed = 640f * scale * ImGui.GetIO().DeltaTime;
            if (mp.Y < wp.Y + edge)
                ImGui.SetScrollY(ImGui.GetScrollY() - speed * ((wp.Y + edge - mp.Y) / edge));
            else if (mp.Y > wp.Y + ws.Y - edge)
                ImGui.SetScrollY(ImGui.GetScrollY() + speed * ((mp.Y - (wp.Y + ws.Y - edge)) / edge));
        }

        private void DrawFolder(int pageIdx, float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            float margin = 12f * scale;
            float winX = ImGui.GetWindowPos().X;
            float bodyW = ImGui.GetWindowSize().X - margin * 2f;

            float tabH = 22f * scale;
            float gap = 6f * scale;
            float pad = 8f * scale;

            float cardW, cardH;
            int perRow;
            if (plugin.Configuration.ReorderPagesMatchGrid)
            {
                perRow = Math.Max(1, plugin.Configuration.ProfileColumns);
                cardW = MathF.Min((bodyW - pad * 2f - (perRow - 1) * gap) / perRow, 96f * scale);
                cardH = cardW * 1.375f;
            }
            else
            {
                cardW = 64f * scale;
                cardH = 88f * scale;
                perRow = Math.Max(1, (int)((bodyW - pad * 2f + gap) / (cardW + gap)));
            }
            var cards = pageBuffer[pageIdx];
            int rows = Math.Max(1, (int)Math.Ceiling(cards.Count / (double)perRow));
            float bodyH = pad * 2f + rows * cardH + (rows - 1) * gap;

            var origin = new Vector2(winX + margin, ImGui.GetCursorScreenPos().Y);

            // page drop marker
            if (pTabDragging && pTabDragIndex.HasValue)
            {
                float markerY = origin.Y - 4f * scale;
                var mp = ImGui.GetMousePos();
                if (mp.Y >= markerY - 6f * scale && mp.Y < origin.Y + (tabH + bodyH) * 0.5f
                    && pageIdx != pTabDragIndex.Value && pageIdx != pTabDragIndex.Value + 1)
                {
                    pTabDropIndex = pageIdx;
                    dl.AddRectFilled(new Vector2(origin.X, markerY), new Vector2(origin.X + bodyW, markerY + 2f * scale),
                        Boutique.U32(Boutique.Gold));
                }
            }

            bool isDropTargetPage = pDragActive && pDropPage == pageIdx;

            // folder tab
            float tabTextW;
            string pageLabel = $"PAGE {pageIdx + 1}";
            string pageName = pageNameBuffer[pageIdx];
            using (Plugin.Instance?.OswaldMed10?.Push())
            {
                float trackPx = 2.6f * scale;
                tabTextW = Boutique.MeasureTrackedText(pageLabel, trackPx);
                if (!string.IsNullOrEmpty(pageName))
                    tabTextW += 10f * scale + Boutique.MeasureTrackedText(pageName.ToUpperInvariant(), trackPx);
            }
            float tabW = tabTextW + 26f * scale;
            var tabMin = origin;
            var tabMax = origin + new Vector2(tabW, tabH);

            ImGui.SetCursorScreenPos(tabMin);
            bool tabClicked = ImGui.InvisibleButton($"##folder_tab_{pageIdx}", new Vector2(tabW, tabH));
            bool tabHovered = ImGui.IsItemHovered();
            bool tabActive = ImGui.IsItemActive();

            if (tabClicked && !pTabDragging && ImGui.GetIO().KeyCtrl
                && pageBuffer.Count > 1 && cards.Count == 0)
                pendingDeletePage = pageIdx;

            // start page drag
            if (tabActive && pTabDragIndex == null && renamingPage != pageIdx)
            {
                pTabDragIndex = pageIdx;
                pTabDragStart = ImGui.GetMousePos();
                pTabDragging = false;
            }
            if (pTabDragIndex == pageIdx && ImGui.IsMouseDown(ImGuiMouseButton.Left)
                && !pTabDragging && Vector2.Distance(pTabDragStart, ImGui.GetMousePos()) > DragThreshold * scale)
            {
                pTabDragging = true;
            }
            if (tabHovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                renamingPage = pageIdx;
                renameBuffer = pageName;
                renameFocusPending = true;
                pTabDragIndex = null;
                pTabDragging = false;
            }

            bool tabIsDragging = pTabDragging && pTabDragIndex == pageIdx;
            uint tabBgTop = Boutique.U32(new Vector4(0.10f, 0.11f, 0.15f, 1f));
            uint tabBgBot = Boutique.U32(new Vector4(0.075f, 0.082f, 0.11f, 1f));
            uint tabBorder = tabIsDragging
                ? Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.7f))
                : (isDropTargetPage || tabHovered
                    ? Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.40f))
                    : Boutique.U32(Boutique.BorderSoft));

            float slant = 10f * scale;
            Span<Vector2> tabPoly = stackalloc Vector2[4]
            {
                tabMin,
                new Vector2(tabMax.X - slant, tabMin.Y),
                new Vector2(tabMax.X, tabMax.Y),
                new Vector2(tabMin.X, tabMax.Y),
            };
            dl.AddConvexPolyFilled(ref tabPoly[0], 4, tabBgTop);
            dl.AddPolyline(ref tabPoly[0], 4, tabBorder, ImDrawFlags.Closed, 1f * scale);

            // tab text or rename input
            if (renamingPage == pageIdx)
            {
                float inputW = MathF.Max(tabW - slant, 160f * scale);
                float fontH = ImGui.GetFontSize();
                ImGui.SetCursorScreenPos(tabMin);
                ImGui.SetNextItemWidth(inputW);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f * scale);
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f * scale, (tabH - fontH) * 0.5f));
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.10f, 0.11f, 0.15f, 1f));
                ImGui.PushStyleColor(ImGuiCol.Border, Boutique.WithAlpha(Boutique.Gold, 0.55f));
                if (renameFocusPending)
                {
                    ImGui.SetKeyboardFocusHere();
                    renameFocusPending = false;
                }
                bool done = ImGui.InputText($"##rename_{pageIdx}", ref renameBuffer, 40,
                    ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
                if (done || ImGui.IsItemDeactivated())
                {
                    pageNameBuffer[pageIdx] = renameBuffer.Trim();
                    renamingPage = -1;
                    PublishPagesPreview();
                }
                ImGui.PopStyleColor(2);
                ImGui.PopStyleVar(3);
            }
            else
            {
                using (Plugin.Instance?.OswaldMed10?.Push())
                {
                    float trackPx = 2.6f * scale;
                    float fontH = ImGui.GetFontSize();
                    float textY = tabMin.Y + (tabH - fontH) * 0.5f;
                    float tx = tabMin.X + 10f * scale;
                    Boutique.DrawTrackedText(dl, new Vector2(tx, textY), pageLabel, Boutique.U32(Boutique.Gold), trackPx);
                    tx += Boutique.MeasureTrackedText(pageLabel, trackPx);
                    if (!string.IsNullOrEmpty(pageName))
                    {
                        tx += 10f * scale;
                        Boutique.DrawTrackedText(dl, new Vector2(tx, textY),
                            pageName.ToUpperInvariant(), Boutique.U32(Boutique.TextDim), trackPx);
                    }
                }
            }

            if (tabHovered && renamingPage != pageIdx)
            {
                string tip = "Drag to reorder pages. Double-click to rename.";
                if (pageBuffer.Count > 1)
                    tip += " Ctrl+Click deletes an empty page.";
                Boutique.Tooltip(tip);
            }

            using (Plugin.Instance?.OswaldMed10?.Push())
            {
                float trackPx = 2.4f * scale;
                string countText = cards.Count == 1 ? "1 CHARACTER" : $"{cards.Count} CHARACTERS";
                float w = Boutique.MeasureTrackedText(countText, trackPx);
                float fontH = ImGui.GetFontSize();
                Boutique.DrawTrackedText(dl,
                    new Vector2(origin.X + bodyW - w, tabMin.Y + (tabH - fontH) * 0.5f),
                    countText, Boutique.U32(Boutique.TextFaint), trackPx);
            }

            // folder body
            var bodyMin = new Vector2(origin.X, tabMax.Y);
            var bodyMax = new Vector2(origin.X + bodyW, tabMax.Y + bodyH);

            uint bodyBg = isDropTargetPage
                ? Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.035f))
                : Boutique.U32(new Vector4(0.055f, 0.063f, 0.078f, 0.5f));
            uint bodyBorder = isDropTargetPage
                ? Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.40f))
                : Boutique.U32(Boutique.BorderSoft);
            dl.AddRectFilled(bodyMin, bodyMax, bodyBg);
            dl.AddRect(bodyMin, bodyMax, bodyBorder, 0f, ImDrawFlags.None, 1f * scale);

            if (cards.Count == 0)
            {
                using (Plugin.Instance?.OswaldMed10?.Push())
                {
                    float trackPx = 3.0f * scale;
                    string hint = "DROP CARDS HERE";
                    float w = Boutique.MeasureTrackedText(hint, trackPx);
                    float fontH = ImGui.GetFontSize();
                    Boutique.DrawTrackedText(dl,
                        new Vector2(bodyMin.X + (bodyW - w) * 0.5f, bodyMin.Y + (bodyH - fontH) * 0.5f),
                        hint, Boutique.U32(Boutique.WithAlpha(Boutique.TextFaint, 0.7f)), trackPx);
                }
            }

            for (int i = 0; i < cards.Count; i++)
            {
                int row = i / perRow;
                int col = i % perRow;
                var cMin = new Vector2(
                    bodyMin.X + pad + col * (cardW + gap),
                    bodyMin.Y + pad + row * (cardH + gap));
                DrawMiniCard(dl, cards[i], pageIdx, i, cMin, cardW, cardH, scale);
            }

            if (pDragActive && pDragCard != null)
            {
                var mp = ImGui.GetMousePos();
                if (mp.X >= tabMin.X && mp.X <= bodyMax.X && mp.Y >= tabMin.Y && mp.Y <= bodyMax.Y)
                {
                    pDropPage = pageIdx;
                    pDropIndex = mp.Y > tabMax.Y
                        ? CardInsertIndex(mp, bodyMin, pad, gap, cardW, cardH, perRow, cards.Count)
                        : cards.Count;
                    if (cards.Count > 0)
                    {
                        var top = InsertLineTop(pDropIndex, cards.Count, bodyMin, pad, gap, cardW, cardH, perRow);
                        DrawVerticalDropLine(dl, new Vector2(top.X - 1f * scale, top.Y), cardH, scale);
                    }
                }
            }

            ImGui.SetCursorScreenPos(new Vector2(origin.X, bodyMax.Y + 12f * scale));
        }

        private static int CardInsertIndex(Vector2 mp, Vector2 bodyMin, float pad, float gap,
            float cardW, float cardH, int perRow, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int row = i / perRow;
                int col = i % perRow;
                var cMin = new Vector2(bodyMin.X + pad + col * (cardW + gap), bodyMin.Y + pad + row * (cardH + gap));
                var cMax = cMin + new Vector2(cardW, cardH);
                if (mp.Y < cMin.Y - gap * 0.5f) return i;
                if (mp.Y <= cMax.Y + gap * 0.5f && mp.X < (cMin.X + cMax.X) * 0.5f) return i;
            }
            return count;
        }

        private static Vector2 InsertLineTop(int index, int count, Vector2 bodyMin, float pad, float gap,
            float cardW, float cardH, int perRow)
        {
            if (index < count)
            {
                int row = index / perRow;
                int col = index % perRow;
                return new Vector2(bodyMin.X + pad + col * (cardW + gap) - gap * 0.5f,
                                   bodyMin.Y + pad + row * (cardH + gap));
            }
            int lastRow = (count - 1) / perRow;
            int lastCol = (count - 1) % perRow;
            return new Vector2(bodyMin.X + pad + lastCol * (cardW + gap) + cardW + gap * 0.5f,
                               bodyMin.Y + pad + lastRow * (cardH + gap));
        }

        private static (Vector2 Uv0, Vector2 Uv1) CoverUv(float texW, float texH, float boxW, float boxH)
        {
            Vector2 uv0 = Vector2.Zero, uv1 = Vector2.One;
            float boxAR = boxW / boxH;
            float texAR = texW / texH;
            if (texAR > boxAR)
            {
                float inset = (1f - boxAR / texAR) * 0.5f;
                uv0.X = inset; uv1.X = 1f - inset;
            }
            else
            {
                float inset = (1f - texAR / boxAR) * 0.5f;
                uv0.Y = inset; uv1.Y = 1f - inset;
            }
            return (uv0, uv1);
        }

        private void DrawVerticalDropLine(ImDrawListPtr dl, Vector2 top, float height, float scale)
        {
            uint goldU = Boutique.U32(Boutique.Gold);
            dl.AddRectFilled(top, top + new Vector2(2f * scale, height), goldU);
            float d = 3f * scale;
            float midX = top.X + 1f * scale;
            dl.AddQuadFilled(
                new Vector2(midX, top.Y - d), new Vector2(midX + d, top.Y),
                new Vector2(midX, top.Y + d), new Vector2(midX - d, top.Y), goldU);
            float by = top.Y + height;
            dl.AddQuadFilled(
                new Vector2(midX, by - d), new Vector2(midX + d, by),
                new Vector2(midX, by + d), new Vector2(midX - d, by), goldU);
        }

        private void DrawMiniCard(ImDrawListPtr dl, Character character, int pageIdx, int cardIdx,
            Vector2 min, float cardW, float cardH, float scale)
        {
            var max = min + new Vector2(cardW, cardH);

            ImGui.SetCursorScreenPos(min);
            ImGui.InvisibleButton($"##pcard_{pageIdx}_{cardIdx}", new Vector2(cardW, cardH));
            bool hovered = ImGui.IsItemHovered();
            bool selected = pageSelection.Contains(character);
            bool isDraggingThis = pDragActive && pDragCard == character;

            MiniCardBehaviour(character, pageIdx, scale);

            // portrait
            float portH = cardH - 20f * scale;
            var portMax = new Vector2(max.X, min.Y + portH);
            var npV = character.NameplateColor;
            uint topU = Boutique.U32(new Vector4(npV.X, npV.Y, npV.Z, 1f));
            uint botU = Boutique.U32(new Vector4(npV.X * 0.25f, npV.Y * 0.25f, npV.Z * 0.25f, 1f));
            dl.AddRectFilledMultiColor(min, portMax, topU, topU, botU, botU);
            if (!string.IsNullOrEmpty(character.ImagePath) && File.Exists(character.ImagePath))
            {
                try
                {
                    var tex = Plugin.TextureProvider.GetFromFile(character.ImagePath).GetWrapOrDefault();
                    if (tex != null && tex.Width > 0 && tex.Height > 0)
                    {
                        var (uv0, uv1) = CoverUv(tex.Width, tex.Height, cardW, portH);
                        dl.AddImage((ImTextureID)tex.Handle, min, portMax, uv0, uv1);
                    }
                }
                catch { }
            }

            // name band
            uint bandBg = Boutique.U32(new Vector4(0.055f, 0.063f, 0.078f, 1f));
            dl.AddRectFilled(new Vector2(min.X, portMax.Y), max, bandBg);
            using (Plugin.Instance?.OswaldMed10?.Push())
            {
                string name = plugin.GetRosterDisplayName(character);
                var sz = ImGui.CalcTextSize(name);
                float bandH = max.Y - portMax.Y;
                dl.PushClipRect(new Vector2(min.X + 2f * scale, portMax.Y), max - new Vector2(2f * scale, 0), true);
                dl.AddText(new Vector2(min.X + MathF.Max(2f * scale, (cardW - sz.X) * 0.5f),
                                       portMax.Y + (bandH - sz.Y) * 0.5f),
                    Boutique.U32(Boutique.Text), name);
                dl.PopClipRect();
            }

            // favourite star
            if (character.IsFavorite)
            {
                float starSize = 9f * scale;
                dl.AddText(UiBuilder.IconFont, starSize,
                    new Vector2(min.X + 3f * scale, min.Y + 3f * scale),
                    Boutique.U32(Boutique.Gold), "");
            }

            // frame
            uint border;
            if (isDraggingThis) border = Boutique.U32(Boutique.Gold);
            else if (selected) border = Boutique.U32(Boutique.Gold);
            else if (hovered) border = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.30f));
            else border = Boutique.U32(Boutique.BorderSoft);
            dl.AddRect(min, max, border, 0f, ImDrawFlags.None, selected ? 1.5f * scale : 1f * scale);

            if (selected)
            {
                float tri = 13f * scale;
                dl.AddTriangleFilled(
                    new Vector2(max.X - tri, min.Y),
                    new Vector2(max.X, min.Y),
                    new Vector2(max.X, min.Y + tri),
                    Boutique.U32(Boutique.Gold));
            }

            if (hovered && !pDragActive)
                Boutique.Tooltip(plugin.GetRosterDisplayName(character));
        }

        private void MiniCardBehaviour(Character character, int pageIdx, float scale)
        {
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                pDragCard = character;
                pDragStart = ImGui.GetMousePos();
                pDragActive = false;
            }
            if (pDragCard == character && ImGui.IsMouseDown(ImGuiMouseButton.Left)
                && !pDragActive && Vector2.Distance(pDragStart, ImGui.GetMousePos()) > DragThreshold * scale)
            {
                pDragActive = true;
                if (!pageSelection.Contains(character) && !ImGui.GetIO().KeyCtrl)
                    pageSelection.Clear();
                pageSelection.Add(character);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                if (!pageSelection.Contains(character))
                {
                    pageSelection.Clear();
                    pageSelection.Add(character);
                }
                ctxCard = character;
                ctxCardPage = pageIdx;
                ImGui.OpenPopup("##page_move_ctx");
            }
        }

        private void ResolveCardDragRelease()
        {
            if (pDragCard == null) return;
            if (!ImGui.IsMouseReleased(ImGuiMouseButton.Left)) return;

            if (pDragActive && pDropPage >= 0)
            {
                MoveSelectionTo(pDropPage, pDropIndex, pDragCard);
            }
            else if (!pDragActive)
            {
                // plain click: select
                if (ImGui.GetIO().KeyCtrl)
                {
                    if (!pageSelection.Add(pDragCard)) pageSelection.Remove(pDragCard);
                }
                else
                {
                    pageSelection.Clear();
                    pageSelection.Add(pDragCard);
                }
            }
            ResetCardDrag();
        }

        private void DrawCardDragGhost(float scale)
        {
            if (pDragCard == null) return;
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            var fdl = ImGui.GetForegroundDrawList();
            var mouse = ImGui.GetMousePos();
            float w = 52f * scale;
            float h = 68f * scale;
            var min = mouse + new Vector2(10f * scale, 6f * scale);
            var max = min + new Vector2(w, h);

            int count = Math.Max(1, pageSelection.Count);
            if (count > 1)
            {
                fdl.AddRect(min + new Vector2(4f * scale, 4f * scale), max + new Vector2(4f * scale, 4f * scale),
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.18f)), 0f, ImDrawFlags.None, 1f * scale);
                fdl.AddRect(min + new Vector2(2f * scale, 2f * scale), max + new Vector2(2f * scale, 2f * scale),
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.35f)), 0f, ImDrawFlags.None, 1f * scale);
            }

            var npV = pDragCard.NameplateColor;
            uint topU = Boutique.U32(new Vector4(npV.X, npV.Y, npV.Z, 0.95f));
            uint botU = Boutique.U32(new Vector4(npV.X * 0.25f, npV.Y * 0.25f, npV.Z * 0.25f, 0.95f));
            fdl.AddRectFilledMultiColor(min, max, topU, topU, botU, botU);
            if (!string.IsNullOrEmpty(pDragCard.ImagePath) && File.Exists(pDragCard.ImagePath))
            {
                try
                {
                    var tex = Plugin.TextureProvider.GetFromFile(pDragCard.ImagePath).GetWrapOrDefault();
                    if (tex != null)
                        fdl.AddImage((ImTextureID)tex.Handle, min, max);
                }
                catch { }
            }
            fdl.AddRect(min, max, Boutique.U32(Boutique.Gold), 0f, ImDrawFlags.None, 1.5f * scale);

            if (count > 1)
            {
                float bs = 16f * scale;
                var bMin = new Vector2(max.X - bs * 0.5f, min.Y - bs * 0.5f);
                fdl.AddRectFilled(bMin, bMin + new Vector2(bs, bs), Boutique.U32(Boutique.Gold));
                using (Plugin.Instance?.OswaldMed10?.Push())
                {
                    string t = count.ToString();
                    var sz = ImGui.CalcTextSize(t);
                    fdl.AddText(bMin + new Vector2((bs - sz.X) * 0.5f, (bs - sz.Y) * 0.5f),
                        Boutique.U32(new Vector4(0.10f, 0.08f, 0f, 1f)), t);
                }
            }
        }

        private void ResolveTabDrag()
        {
            if (pTabDragIndex == null) return;
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                if (pTabDragging && pTabDropIndex >= 0)
                    MovePage(pTabDragIndex.Value, pTabDropIndex);
                pTabDragIndex = null;
                pTabDragging = false;
                pTabDropIndex = -1;
            }
        }

        private void DrawNewPageGhost(float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            float margin = 12f * scale;
            float winX = ImGui.GetWindowPos().X;
            float bodyW = ImGui.GetWindowSize().X - margin * 2f;
            float h = 40f * scale;
            var min = new Vector2(winX + margin, ImGui.GetCursorScreenPos().Y);
            var max = min + new Vector2(bodyW, h);

            ImGui.SetCursorScreenPos(min);
            bool clicked = ImGui.InvisibleButton("##new_page_ghost", new Vector2(bodyW, h));
            bool hovered = ImGui.IsItemHovered();

            bool dropHere = pDragActive && pDragCard != null
                && ImGui.IsMouseHoveringRect(min, max);

            uint border = (hovered || dropHere)
                ? Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.35f))
                : Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.14f));
            dl.AddRect(min, max, border, 0f, ImDrawFlags.None, 1f * scale);

            using (Plugin.Instance?.OswaldMed10?.Push())
            {
                float trackPx = 3.0f * scale;
                string label = "+ NEW PAGE";
                float w = Boutique.MeasureTrackedText(label, trackPx);
                float fontH = ImGui.GetFontSize();
                uint col = (hovered || dropHere) ? Boutique.U32(Boutique.Gold) : Boutique.U32(Boutique.TextFaint);
                Boutique.DrawTrackedText(dl,
                    new Vector2(min.X + (bodyW - w) * 0.5f, min.Y + (h - fontH) * 0.5f),
                    label, col, trackPx);
            }

            if (dropHere && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                AddNewPage(pDragCard);
                ResetCardDrag();
            }
            else if (clicked && !pDragActive)
            {
                AddNewPage(null);
            }

            ImGui.SetCursorScreenPos(new Vector2(min.X, max.Y + 8f * scale));
        }

        private void DrawMoveToPopup()
        {
            if (ImGui.BeginPopup("##page_move_ctx"))
            {
                int count = Math.Max(1, pageSelection.Count);
                ImGui.TextDisabled(count == 1 ? "Move to" : $"Move {count} to");
                ImGui.Separator();
                for (int p = 0; p < pageBuffer.Count; p++)
                {
                    if (p == ctxCardPage && count == 1) continue;
                    string label = string.IsNullOrEmpty(pageNameBuffer[p])
                        ? $"Page {p + 1}"
                        : $"Page {p + 1} - {pageNameBuffer[p]}";
                    if (ImGui.Selectable($"{label}##ctx_{p}") && ctxCard != null)
                    {
                        MoveSelectionTo(p, pageBuffer[p].Count, ctxCard);
                        ctxCard = null;
                    }
                }
                if (ImGui.Selectable("+ New Page##ctx_new") && ctxCard != null)
                {
                    AddNewPage(ctxCard);
                    ctxCard = null;
                }
                ImGui.EndPopup();
            }
        }

        private void SavePages()
        {
            // sync with live roster
            var live = new HashSet<Character>(plugin.Characters);
            var pagesForSave = pageBuffer.Select(pg => pg.Where(live.Contains).ToList()).ToList();
            var seen = new HashSet<Character>(pagesForSave.SelectMany(pg => pg));
            foreach (var c in plugin.Characters)
                if (!seen.Contains(c))
                    pagesForSave[pagesForSave.Count - 1].Add(c);

            var flat = pagesForSave.SelectMany(pg => pg).ToList();
            for (int i = 0; i < flat.Count; i++)
                flat[i].SortOrder = i;
            plugin.Characters.Clear();
            plugin.Characters.AddRange(flat);
            plugin.Configuration.RosterPages = pagesForSave
                .Select((pg, i) => new RosterPage { Size = pg.Count, Name = pageNameBuffer[i] })
                .ToList();
            plugin.Configuration.CurrentSortIndex = (int)Plugin.SortType.Manual;
            plugin.SaveConfiguration();
            plugin.AchievementTracker?.OnCharactersReordered();
            plugin.MainWindow.UpdateSortType();
            ClearPagesPreview();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
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
        private List<Character> originalBuffer = new();
        private int movedCount = 0;

        // Drag state
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

        public void Dispose() { }

        public void Open()
        {
            IsOpen = true;
            reorderBuffer = plugin.Characters.ToList();
            originalBuffer = plugin.Characters.ToList();
            movedCount = 0;
            draggedCharacterIndex = null;
            isDragging = false;
            currentDropTargetIndex = null;
        }

        public void Draw()
        {
            if (!IsOpen) return;

            float scale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);
            float winW = 460f * scale;
            float winH = 620f * scale;

            // Centre on first appearance
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(
                new Vector2(viewport.Pos.X + (viewport.Size.X - winW) * 0.5f,
                            viewport.Pos.Y + (viewport.Size.Y - winH) * 0.5f),
                ImGuiCond.Appearing);
            ImGui.SetNextWindowSize(new Vector2(winW, winH), ImGuiCond.Always);

            // Transparent ImGui chrome - we paint our own
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0));

            var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
                      | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar
                      | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings;

            bool open = IsOpen;
            if (ImGui.Begin("##ReorderRoster", ref open, flags))
            {
                IsOpen = open;
                try { DrawContent(scale); }
                finally { /* nothing to pop here */ }
            }
            ImGui.End();

            ImGui.PopStyleColor();
            ImGui.PopStyleVar(3);

            if (!IsOpen)
            {
                reorderBuffer.Clear();
                originalBuffer.Clear();
            }
        }

        private void DrawContent(float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            var winMin = ImGui.GetWindowPos();
            var winMax = winMin + ImGui.GetWindowSize();

            // Window chassis: velvet gradient bg + border-soft outer + faint gold inner.
            uint bgTop = Boutique.U32(new Vector4(0x06 / 255f, 0x07 / 255f, 0x09 / 255f, 1f));
            uint bgBot = Boutique.U32(new Vector4(0x03 / 255f, 0x04 / 255f, 0x0A / 255f, 1f));
            dl.AddRectFilledMultiColor(winMin, winMax, bgTop, bgTop, bgBot, bgBot);
            dl.AddRect(winMin, winMax, Boutique.U32(Boutique.BorderSoft), 0f, ImDrawFlags.None, 1f * scale);
            dl.AddRect(winMin + new Vector2(1, 1), winMax - new Vector2(1, 1),
                Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.025f)),
                0f, ImDrawFlags.None, 1f * scale);

            // BL + BR corner brackets (12px L-shapes, gold-at-40%)
            DrawCornerBrackets(dl, winMin, winMax, scale);

            // Vertical layout. Header gets the most growth - it's the hero.
            float ribbonH = 28f * scale;
            float headerH = 84f * scale;
            float legendH = 30f * scale;
            float footerH = 50f * scale;

            var ribbonMin = winMin;
            var ribbonMax = new Vector2(winMax.X, winMin.Y + ribbonH);

            var headerMin = new Vector2(winMin.X, ribbonMax.Y);
            var headerMax = new Vector2(winMax.X, ribbonMax.Y + headerH);

            var legendMin = new Vector2(winMin.X, headerMax.Y);
            var legendMax = new Vector2(winMax.X, headerMax.Y + legendH);

            var footerMin = new Vector2(winMin.X, winMax.Y - footerH);
            var footerMax = winMax;

            var bodyMin = new Vector2(winMin.X, legendMax.Y);
            var bodyMax = new Vector2(winMax.X, footerMin.Y);

            DrawRibbon(dl, ribbonMin, ribbonMax, scale);
            DrawHeader(dl, headerMin, headerMax, scale);
            DrawLegend(dl, legendMin, legendMax, scale);
            DrawBody(dl, bodyMin, bodyMax, scale);
            DrawFooter(dl, footerMin, footerMax, scale);
        }

        // ── Ribbon ──────────────────────────────────────────────────────────
        private void DrawRibbon(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
        {
            BoutiqueChassis.DrawRibbonBackground(dl, min, max, scale);

            float padX = 12f * scale;
            float midY = (min.Y + max.Y) * 0.5f;
            double t = ImGui.GetTime();

            // Pulsing gold pip on the left
            float pipR = 3f * scale;
            float pulse = 0.55f + 0.45f * (float)Math.Sin(t * 2.4);
            for (int g = 3; g >= 1; g--)
            {
                float pad = (8f * scale) * g / 3f;
                uint glowCol = Boutique.U32(Boutique.WithAlpha(Boutique.GoldWarm, 0.20f * pulse / g));
                var pipCentre = new Vector2(min.X + padX + pipR, midY);
                dl.AddRectFilled(pipCentre - new Vector2(pad, pad), pipCentre + new Vector2(pad, pad), glowCol);
            }
            var pipPos = new Vector2(min.X + padX + pipR, midY);
            dl.AddRectFilled(pipPos - new Vector2(pipR, pipR), pipPos + new Vector2(pipR, pipR), Boutique.U32(Boutique.Gold));

            // Centred title: "REORDER ◆ CHARACTER ROSTER" (gold diamond separator drawn,
            // not a unicode glyph, keeps the chassis vocabulary consistent).
            using (Plugin.Instance?.OswaldMed10?.Push())
            {
                float trackPx = 2.6f * scale;
                string left  = "REORDER";
                string right = "CHARACTER ROSTER";
                float leftW  = Boutique.MeasureTrackedText(left, trackPx);
                float rightW = Boutique.MeasureTrackedText(right, trackPx);
                float gap    = 12f * scale;
                float diaSize = 3.5f * scale;
                float totalW = leftW + gap + diaSize * 2f + gap + rightW;
                float startX = (min.X + max.X) * 0.5f - totalW * 0.5f;
                float fontH  = ImGui.GetFontSize();
                float textY  = midY - fontH * 0.5f;
                Boutique.DrawTrackedText(dl, new Vector2(startX, textY), left, Boutique.U32(Boutique.TextDim), trackPx);
                // Drawn diamond separator
                var diaCentre = new Vector2(startX + leftW + gap + diaSize, midY);
                dl.AddQuadFilled(
                    diaCentre + new Vector2(0, -diaSize),
                    diaCentre + new Vector2(diaSize, 0),
                    diaCentre + new Vector2(0, diaSize),
                    diaCentre + new Vector2(-diaSize, 0),
                    Boutique.U32(Boutique.GoldDeep));
                Boutique.DrawTrackedText(dl,
                    new Vector2(startX + leftW + gap + diaSize * 2f + gap, textY),
                    right, Boutique.U32(Boutique.Gold), trackPx);
            }

            // Right-side count tag: "{N} ENTRIES"
            using (Plugin.Instance?.OswaldMed10?.Push())
            {
                string countText = $"{reorderBuffer.Count:00} ENTRIES";
                float trackPx = 2.4f * scale;
                float w = Boutique.MeasureTrackedText(countText, trackPx);
                float h = ImGui.GetFontSize();
                float padInX = 7f * scale;
                float padInY = 2f * scale;
                var tagMax = new Vector2(max.X - padX, midY + h * 0.5f + padInY);
                var tagMin = new Vector2(tagMax.X - w - padInX * 2f, midY - h * 0.5f - padInY);
                dl.AddRect(tagMin, tagMax, Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.40f)), 0f, ImDrawFlags.None, 1f * scale);
                Boutique.DrawTrackedText(dl,
                    new Vector2(tagMin.X + padInX, midY - h * 0.5f),
                    countText, Boutique.U32(Boutique.Gold), trackPx);
            }
        }

        // ── Header ──────────────────────────────────────────────────────────
        private void DrawHeader(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
        {
            // Bottom border-soft hairline
            dl.AddLine(new Vector2(min.X, max.Y - 1f * scale), new Vector2(max.X, max.Y - 1f * scale),
                Boutique.U32(Boutique.BorderSoft), 1f * scale);
            // Centred gold accent under the divider
            float aLeft = min.X + (max.X - min.X) * 0.25f;
            float aRight = min.X + (max.X - min.X) * 0.75f;
            var goldFade = Boutique.WithAlpha(Boutique.Gold, 0.50f);
            var goldClear = Boutique.WithAlpha(Boutique.Gold, 0f);
            dl.AddRectFilledMultiColor(
                new Vector2(aLeft,  max.Y - 1f * scale),
                new Vector2((aLeft + aRight) * 0.5f, max.Y),
                Boutique.U32(goldClear), Boutique.U32(goldFade), Boutique.U32(goldFade), Boutique.U32(goldClear));
            dl.AddRectFilledMultiColor(
                new Vector2((aLeft + aRight) * 0.5f, max.Y - 1f * scale),
                new Vector2(aRight, max.Y),
                Boutique.U32(goldFade), Boutique.U32(goldClear), Boutique.U32(goldClear), Boutique.U32(goldFade));

            // Title: "REORDER ROSTER" - OswaldSemiSmall is 16px nominal × ach × statBoost
            // = ~24.5px baked, the tier we use for hero display titles. Sits sharp
            // because it's rendered at its native baked size.
            using (Plugin.Instance?.OswaldSemiSmall?.Push())
            {
                float trackPx = 5f * scale;
                string title = "REORDER ROSTER";
                float w = Boutique.MeasureTrackedText(title, trackPx);
                float titleY = min.Y + 18f * scale;
                Boutique.DrawTrackedText(dl,
                    new Vector2((min.X + max.X) * 0.5f - w * 0.5f, titleY),
                    title, Boutique.U32(Boutique.Text), trackPx);
            }

            // Sub-line: "DRAG TO ARRANGE ◆ SAVE TO COMMIT"
            using (Plugin.Instance?.OswaldMed11?.Push())
            {
                float trackPx = 3.4f * scale;
                string left = "DRAG TO ARRANGE";
                string right = "SAVE TO COMMIT";
                float lw = Boutique.MeasureTrackedText(left, trackPx);
                float rw = Boutique.MeasureTrackedText(right, trackPx);
                float gap = 14f * scale;
                float diaSize = 3.5f * scale;
                float totalW = lw + gap + diaSize * 2f + gap + rw;
                float startX = (min.X + max.X) * 0.5f - totalW * 0.5f;
                float subY = max.Y - 16f * scale - ImGui.GetFontSize();
                float fontH = ImGui.GetFontSize();
                uint subCol = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.60f));
                Boutique.DrawTrackedText(dl, new Vector2(startX, subY), left, subCol, trackPx);
                var diaCentre = new Vector2(startX + lw + gap + diaSize, subY + fontH * 0.5f);
                dl.AddQuadFilled(
                    diaCentre + new Vector2(0, -diaSize),
                    diaCentre + new Vector2(diaSize, 0),
                    diaCentre + new Vector2(0, diaSize),
                    diaCentre + new Vector2(-diaSize, 0),
                    subCol);
                Boutique.DrawTrackedText(dl,
                    new Vector2(startX + lw + gap + diaSize * 2f + gap, subY),
                    right, subCol, trackPx);
            }

            // Close button: top-right 28x28 chamfered slip with explicit FontAwesome X
            float btnSize = 28f * scale;
            var btnMin = new Vector2(max.X - 12f * scale - btnSize, min.Y + 12f * scale);
            var btnMax = btnMin + new Vector2(btnSize, btnSize);
            ImGui.SetCursorScreenPos(btnMin);
            bool clicked = ImGui.InvisibleButton("##reorder_close", new Vector2(btnSize, btnSize));
            bool hovered = ImGui.IsItemHovered();
            if (hovered) Boutique.Tooltip("Close");
            uint bg = hovered
                ? Boutique.U32(Boutique.WithAlpha(Boutique.Red, 0.14f))
                : Boutique.U32(new Vector4(0.08f, 0.09f, 0.12f, 0.75f));
            uint border = hovered
                ? Boutique.U32(Boutique.WithAlpha(Boutique.Red, 0.65f))
                : Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.22f));
            dl.AddRectFilled(btnMin, btnMax, bg);
            dl.AddRect(btnMin, btnMax, border, 0f, ImDrawFlags.None, 1f * scale);

            // Explicit FontAwesome times glyph () so source-encoding round-trips
            // can never strip it. Render at 14px so it's clearly visible.
            string xGlyph = "";
            ImGui.PushFont(UiBuilder.IconFont);
            var xSz = ImGui.CalcTextSize(xGlyph);
            ImGui.PopFont();
            // Render at the icon font's NATIVE baked size so it stays sharp.
            float iconNativeSize = UiBuilder.IconFont.FontSize;
            uint xCol = hovered ? Boutique.U32(Boutique.Red) : Boutique.U32(Boutique.Text);
            dl.AddText(UiBuilder.IconFont, iconNativeSize,
                new Vector2(btnMin.X + (btnSize - xSz.X) * 0.5f,
                            btnMin.Y + (btnSize - xSz.Y) * 0.5f),
                xCol, xGlyph);

            if (clicked) IsOpen = false;
        }

        // ── Legend ──────────────────────────────────────────────────────────
        private void DrawLegend(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
        {
            // Subtle dark wash + bottom hairline
            dl.AddRectFilled(min, max, Boutique.U32(new Vector4(0.04f, 0.05f, 0.06f, 0.40f)));
            dl.AddLine(new Vector2(min.X, max.Y - 1f * scale), new Vector2(max.X, max.Y - 1f * scale),
                Boutique.U32(Boutique.BorderSoft), 1f * scale);

            float padX = 16f * scale;
            float midY = (min.Y + max.Y) * 0.5f;
            using (Plugin.Instance?.OswaldMed10?.Push())
            {
                float trackPx = 3.0f * scale;
                float fontH = ImGui.GetFontSize();
                float textY = midY - fontH * 0.5f;
                Boutique.DrawTrackedText(dl, new Vector2(min.X + padX, textY),
                    "#", Boutique.U32(Boutique.GoldDeep), trackPx);
                float charColX = min.X + padX + 56f * scale;
                Boutique.DrawTrackedText(dl, new Vector2(charColX, textY),
                    "CHARACTER", Boutique.U32(Boutique.TextFaint), trackPx);
                string np = "NAMEPLATE";
                float npW = Boutique.MeasureTrackedText(np, trackPx);
                Boutique.DrawTrackedText(dl, new Vector2(max.X - padX - npW, textY),
                    np, Boutique.U32(Boutique.GoldDeep), trackPx);
            }
        }

        // ── Body (scrollable list) ──────────────────────────────────────────
        private void DrawBody(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
        {
            // Begin a child for scrolling. Custom transparent bg.
            ImGui.SetCursorScreenPos(min);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 4f * scale);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0, 0, 0, 0.20f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Boutique.WithAlpha(Boutique.GoldDeep, 1f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Boutique.WithAlpha(Boutique.Gold, 0.85f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Boutique.Gold);

            ImGui.BeginChild("##reorder_body", max - min, false,
                ImGuiWindowFlags.AlwaysVerticalScrollbar | ImGuiWindowFlags.NoBackground);

            float bodyDrawTop = ImGui.GetCursorScreenPos().Y;
            ImGui.Dummy(new Vector2(0, 4f * scale));

            for (int i = 0; i < reorderBuffer.Count; i++)
            {
                if (currentDropTargetIndex == i && isDragging && draggedCharacterIndex.HasValue
                    && i != draggedCharacterIndex.Value)
                {
                    DrawDropLine(ImGui.GetWindowDrawList(), scale);
                }
                DrawCharacterRow(reorderBuffer[i], i, scale);
            }
            // Drop line at end of list
            if (currentDropTargetIndex == reorderBuffer.Count && isDragging)
            {
                DrawDropLine(ImGui.GetWindowDrawList(), scale);
            }

            ImGui.Dummy(new Vector2(0, 4f * scale));
            ImGui.EndChild();
            ImGui.PopStyleColor(5); // ChildBg + 4 scrollbar colours
            ImGui.PopStyleVar();
        }

        private void DrawDropLine(ImDrawListPtr dl, float scale)
        {
            float y = ImGui.GetCursorScreenPos().Y + 2f * scale;
            float xMin = ImGui.GetWindowPos().X + 14f * scale;
            float xMax = ImGui.GetWindowPos().X + ImGui.GetWindowSize().X - 14f * scale;
            uint goldU = Boutique.U32(Boutique.Gold);
            // 1px line
            dl.AddRectFilled(new Vector2(xMin, y), new Vector2(xMax, y + 1f * scale), goldU);
            // Diamond endpoints
            float d = 3f * scale;
            float midY = y + 0.5f * scale;
            dl.AddQuadFilled(
                new Vector2(xMin + 1, midY - d),
                new Vector2(xMin + 1 + d, midY),
                new Vector2(xMin + 1, midY + d),
                new Vector2(xMin + 1 - d, midY),
                goldU);
            dl.AddQuadFilled(
                new Vector2(xMax - 1, midY - d),
                new Vector2(xMax - 1 + d, midY),
                new Vector2(xMax - 1, midY + d),
                new Vector2(xMax - 1 - d, midY),
                goldU);
            ImGui.Dummy(new Vector2(0, 4f * scale));
        }

        // ── Row (32px) ──────────────────────────────────────────────────────
        private void DrawCharacterRow(Character character, int index, float scale)
        {
            float rowH = 44f * scale;
            float rowMargin = 8f * scale;
            var dl = ImGui.GetWindowDrawList();

            var rowMin = new Vector2(ImGui.GetWindowPos().X + rowMargin, ImGui.GetCursorScreenPos().Y);
            float rowW = ImGui.GetWindowSize().X - rowMargin * 2f;
            var rowMax = rowMin + new Vector2(rowW, rowH);

            ImGui.SetCursorScreenPos(rowMin);
            bool clicked = ImGui.InvisibleButton($"##reorder_row_{index}", new Vector2(rowW, rowH));
            bool hovered = ImGui.IsItemHovered();
            bool active  = ImGui.IsItemActive();
            bool isDraggingThis = isDragging && draggedCharacterIndex == index;

            // ── Drag/drop state machine ──
            if (active && draggedCharacterIndex == null)
            {
                draggedCharacterIndex = index;
                dragStartPos = ImGui.GetMousePos();
                isDragging = false;
            }
            if (draggedCharacterIndex == index && ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                if (!isDragging && Vector2.Distance(dragStartPos, ImGui.GetMousePos()) > DragThreshold * scale)
                    isDragging = true;
            }

            // While dragging, any row hover sets it as drop target. Use the row's
            // vertical midpoint to decide above-vs-below for cleaner drop feedback.
            if (isDragging && draggedCharacterIndex.HasValue && draggedCharacterIndex.Value != index)
            {
                bool over = ImGui.IsMouseHoveringRect(rowMin, rowMax);
                if (over)
                {
                    var mp = ImGui.GetMousePos();
                    float midY = (rowMin.Y + rowMax.Y) * 0.5f;
                    int target = mp.Y < midY ? index : index + 1;
                    // Don't show drop line directly above or below the dragged row's
                    // current slot (would be a no-op move).
                    int dragged = draggedCharacterIndex.Value;
                    if (target != dragged && target != dragged + 1)
                        currentDropTargetIndex = target;
                }
            }

            if (draggedCharacterIndex == index && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                if (isDragging && currentDropTargetIndex.HasValue)
                {
                    int from = draggedCharacterIndex.Value;
                    int to = currentDropTargetIndex.Value;
                    if (from != to && from != to - 1)
                    {
                        var moved = reorderBuffer[from];
                        reorderBuffer.RemoveAt(from);
                        if (from < to) to--;
                        reorderBuffer.Insert(to, moved);
                        movedCount++;
                    }
                }
                draggedCharacterIndex = null;
                isDragging = false;
                currentDropTargetIndex = null;
            }

            // ── Visual ──
            // Background
            uint bgCol;
            uint borderCol;
            if (isDraggingThis)
            {
                bgCol = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.10f));
                borderCol = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.55f));
                // Stacked-rect drop shadow (no blur)
                for (int s = 0; s < 3; s++)
                {
                    float off = (1 + s) * scale;
                    float a = new[] { 0.25f, 0.15f, 0.08f }[s];
                    dl.AddRect(rowMin + new Vector2(0, off), rowMax + new Vector2(0, off),
                        Boutique.U32(new Vector4(0, 0, 0, a)), 0f, ImDrawFlags.None, 1f * scale);
                }
            }
            else if (hovered)
            {
                bgCol = Boutique.U32(new Vector4(0.078f, 0.086f, 0.118f, 0.65f));
                borderCol = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.20f));
            }
            else
            {
                bgCol = 0u;
                borderCol = 0u;
            }
            if (bgCol != 0u) dl.AddRectFilled(rowMin, rowMax, bgCol);
            if (borderCol != 0u) dl.AddRect(rowMin, rowMax, borderCol, 0f, ImDrawFlags.None, 1f * scale);

            // Drag handle (4x26 gold bar)
            float handleH = 26f * scale;
            var handleMin = new Vector2(rowMin.X + 8f * scale, rowMin.Y + (rowH - handleH) * 0.5f);
            var handleMax = handleMin + new Vector2(4f * scale, handleH);
            uint handleCol;
            if (isDraggingThis)
                handleCol = Boutique.U32(Boutique.GoldBright);
            else if (hovered)
                handleCol = Boutique.U32(Boutique.Gold);
            else
                handleCol = 0u;
            if (handleCol != 0u) dl.AddRectFilled(handleMin, handleMax, handleCol);

            // Position number column (32px wide, Oswald Med 13 → ~17px baked)
            float posColX = handleMax.X + 10f * scale;
            float posColW = 32f * scale;
            using (Plugin.Instance?.OswaldMed13?.Push())
            {
                string posText = (index + 1).ToString("00");
                var posSize = ImGui.CalcTextSize(posText);
                uint posCol = isDraggingThis
                    ? Boutique.U32(Boutique.GoldBright)
                    : (hovered ? Boutique.U32(Boutique.Gold) : Boutique.U32(Boutique.GoldDeep));
                dl.AddText(new Vector2(posColX + (posColW - posSize.X) * 0.5f,
                                       rowMin.Y + (rowH - posSize.Y) * 0.5f),
                    posCol, posText);
            }

            // Thumbnail (32x32 chamfered)
            float thumbX = posColX + posColW + 12f * scale;
            float thumbSize = 32f * scale;
            float thumbY = rowMin.Y + (rowH - thumbSize) * 0.5f;
            DrawThumbnail(dl, character, new Vector2(thumbX, thumbY), thumbSize, scale);

            // Name + alias on a single baseline. Reserve only the small pip area
            // on the right; no colour-name label per user feedback.
            float nameX = thumbX + thumbSize + 12f * scale;
            float pipReserve = 30f * scale;
            float nameMaxX = rowMax.X - pipReserve;
            float charNameW;
            using (Plugin.Instance?.OutfitBody15?.Push())
            {
                string name = character.Name ?? "";
                var nameSize = ImGui.CalcTextSize(name);
                charNameW = nameSize.X;
                float nameY = rowMin.Y + (rowH - nameSize.Y) * 0.5f;
                dl.PushClipRect(new Vector2(nameX, rowMin.Y), new Vector2(nameMaxX, rowMax.Y), true);
                dl.AddText(new Vector2(nameX, nameY), Boutique.U32(Boutique.Text), name);
                dl.PopClipRect();
            }
            if (!string.IsNullOrWhiteSpace(character.Alias))
            {
                using (Plugin.Instance?.OswaldMed11?.Push())
                {
                    float aliasTrack = 3.2f * scale;
                    string alias = character.Alias!.ToUpperInvariant();
                    float aliasW = Boutique.MeasureTrackedText(alias, aliasTrack);
                    float aliasX = nameX + charNameW + 12f * scale;
                    if (aliasX + aliasW < nameMaxX)
                    {
                        float aliasY = rowMin.Y + (rowH - ImGui.GetFontSize()) * 0.5f;
                        Boutique.DrawTrackedText(dl, new Vector2(aliasX, aliasY),
                            alias, Boutique.U32(Boutique.TextFaint), aliasTrack);
                    }
                }
            }

            // Nameplate-colour diamond pip on the right (no label)
            var npV = character.NameplateColor;
            uint npCol = Boutique.U32(new Vector4(npV.X, npV.Y, npV.Z, 1f));
            float pipSize = 5f * scale;
            float pipX = rowMax.X - 14f * scale;
            float midRowY = (rowMin.Y + rowMax.Y) * 0.5f;
            var pipCentre = new Vector2(pipX, midRowY);
            dl.AddQuadFilled(
                pipCentre + new Vector2(0, -pipSize),
                pipCentre + new Vector2(pipSize, 0),
                pipCentre + new Vector2(0, pipSize),
                pipCentre + new Vector2(-pipSize, 0),
                npCol);

            // Faint bottom hairline so rows read as distinct cards even at rest.
            // Skipped on the row currently being dragged (already framed in gold)
            // and on the last row (border belongs to the body, not row).
            if (!isDraggingThis && !hovered && index < reorderBuffer.Count - 1)
            {
                dl.AddLine(
                    new Vector2(rowMin.X + 4f * scale, rowMax.Y),
                    new Vector2(rowMax.X - 4f * scale, rowMax.Y),
                    Boutique.U32(Boutique.WithAlpha(Boutique.BorderSoft, 0.40f)),
                    1f * scale);
            }

            // Ghost preview: follows cursor while dragging so the user feels they
            // have a grip on the character. Foreground draw list = renders above
            // everything else, even outside the body's clip rect.
            if (isDraggingThis)
                DrawDragGhost(character, scale);

            // Cursor advance with extra gap so rows aren't visually jammed.
            ImGui.SetCursorScreenPos(new Vector2(rowMin.X, rowMax.Y + 3f * scale));
        }

        // ── Drag ghost ──────────────────────────────────────────────────────
        // Floating preview of the character held by the cursor: thumbnail + name +
        // nameplate-colour border. Drawn on the foreground draw list so it can
        // bleed past the window edges without being clipped.
        private void DrawDragGhost(Character character, float scale)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            var fdl = ImGui.GetForegroundDrawList();
            var mouse = ImGui.GetMousePos();

            float ghostH = 40f * scale;
            float thumbSize = 30f * scale;
            float padX = 8f * scale;
            float gap = 10f * scale;

            // Measure name width with the right font pushed
            float nameW;
            using (Plugin.Instance?.OutfitBody15?.Push())
                nameW = ImGui.CalcTextSize(character.Name ?? "").X;

            float ghostW = padX + thumbSize + gap + MathF.Min(nameW, 200f * scale) + padX;
            // Offset slightly down-right of the cursor so it doesn't sit under the pointer.
            var origin = mouse + new Vector2(12f * scale, 6f * scale);
            var min = origin;
            var max = origin + new Vector2(ghostW, ghostH);

            // Stacked-rect drop shadow (no blur, translates 1:1 to ImDrawList)
            for (int s = 0; s < 3; s++)
            {
                float off = (1 + s) * scale;
                float a = new[] { 0.40f, 0.22f, 0.10f }[s];
                fdl.AddRectFilled(
                    min + new Vector2(0, off), max + new Vector2(0, off),
                    Boutique.U32(new Vector4(0, 0, 0, a)));
            }

            // Body: dark velvet at 92% alpha so it reads as a tangible card
            uint bg = Boutique.U32(new Vector4(0.06f, 0.07f, 0.10f, 0.92f));
            fdl.AddRectFilled(min, max, bg);

            // Nameplate-colour border (1.5px so it pops slightly more than rest borders)
            var npV = character.NameplateColor;
            uint borderCol = Boutique.U32(new Vector4(npV.X, npV.Y, npV.Z, 0.85f));
            fdl.AddRect(min, max, borderCol, 0f, ImDrawFlags.None, 1.5f * scale);

            // Thumbnail (gradient + image overlay, same shape as the row's thumbnail)
            float thumbY = min.Y + (ghostH - thumbSize) * 0.5f;
            var thumbPos = new Vector2(min.X + padX, thumbY);
            uint topU = Boutique.U32(new Vector4(npV.X, npV.Y, npV.Z, 1f));
            uint botU = Boutique.U32(new Vector4(npV.X * 0.4f, npV.Y * 0.4f, npV.Z * 0.4f, 1f));
            fdl.AddRectFilledMultiColor(thumbPos, thumbPos + new Vector2(thumbSize, thumbSize), topU, topU, botU, botU);
            if (!string.IsNullOrEmpty(character.ImagePath) && File.Exists(character.ImagePath))
            {
                try
                {
                    var tex = Plugin.TextureProvider.GetFromFile(character.ImagePath).GetWrapOrDefault();
                    if (tex != null)
                        fdl.AddImage((ImTextureID)tex.Handle, thumbPos, thumbPos + new Vector2(thumbSize, thumbSize));
                }
                catch { }
            }
            fdl.AddRect(thumbPos, thumbPos + new Vector2(thumbSize, thumbSize),
                Boutique.U32(new Vector4(1f, 1f, 1f, 0.10f)), 0f, ImDrawFlags.None, 1f * scale);

            // Name
            using (Plugin.Instance?.OutfitBody15?.Push())
            {
                string name = character.Name ?? "";
                var size = ImGui.CalcTextSize(name);
                float textX = thumbPos.X + thumbSize + gap;
                float textY = min.Y + (ghostH - size.Y) * 0.5f;
                float textRight = max.X - padX;
                fdl.PushClipRect(new Vector2(textX, min.Y), new Vector2(textRight, max.Y), true);
                fdl.AddText(new Vector2(textX, textY), Boutique.U32(Boutique.Text), name);
                fdl.PopClipRect();
            }
        }

        private void DrawThumbnail(ImDrawListPtr dl, Character character, Vector2 pos, float size, float scale)
        {
            // 4px chamfered slip with nameplate-colour gradient
            float chamfer = 4f * scale;
            var npV = character.NameplateColor;
            uint topU = Boutique.U32(new Vector4(npV.X, npV.Y, npV.Z, 1f));
            uint botU = Boutique.U32(new Vector4(npV.X * 0.4f, npV.Y * 0.4f, npV.Z * 0.4f, 1f));

            Span<Vector2> poly = stackalloc Vector2[8]
            {
                new Vector2(pos.X, pos.Y + chamfer),
                new Vector2(pos.X + chamfer, pos.Y),
                new Vector2(pos.X + size, pos.Y),
                new Vector2(pos.X + size, pos.Y + size - chamfer),
                new Vector2(pos.X + size - chamfer, pos.Y + size),
                new Vector2(pos.X, pos.Y + size),
                new Vector2(pos.X, pos.Y),
                new Vector2(pos.X, pos.Y),
            };
            // Filled with gradient (approximate via two filled rects + chamfer corners)
            // Simpler: just draw the bg gradient as a filled rect, then re-cut chamfers
            dl.AddRectFilledMultiColor(pos, pos + new Vector2(size, size), topU, topU, botU, botU);

            // Try to draw character image on top
            if (!string.IsNullOrEmpty(character.ImagePath) && File.Exists(character.ImagePath))
            {
                try
                {
                    var tex = Plugin.TextureProvider.GetFromFile(character.ImagePath).GetWrapOrDefault();
                    if (tex != null)
                    {
                        dl.AddImage((ImTextureID)tex.Handle, pos, pos + new Vector2(size, size));
                    }
                }
                catch { }
            }

            // 1px white-at-10% inner stroke
            dl.AddRect(pos, pos + new Vector2(size, size),
                Boutique.U32(new Vector4(1f, 1f, 1f, 0.10f)), 0f, ImDrawFlags.None, 1f * scale);
        }

        // ── Footer ──────────────────────────────────────────────────────────
        private void DrawFooter(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
        {
            // Top hairline + gold gradient accent
            dl.AddLine(min, new Vector2(max.X, min.Y), Boutique.U32(Boutique.BorderSoft), 1f * scale);
            float aLeft = min.X + (max.X - min.X) * 0.30f;
            float aRight = min.X + (max.X - min.X) * 0.70f;
            var goldFade = Boutique.WithAlpha(Boutique.Gold, 0.30f);
            var goldClear = Boutique.WithAlpha(Boutique.Gold, 0f);
            dl.AddRectFilledMultiColor(
                new Vector2(aLeft, min.Y), new Vector2((aLeft + aRight) * 0.5f, min.Y + 1f * scale),
                Boutique.U32(goldClear), Boutique.U32(goldFade), Boutique.U32(goldFade), Boutique.U32(goldClear));
            dl.AddRectFilledMultiColor(
                new Vector2((aLeft + aRight) * 0.5f, min.Y), new Vector2(aRight, min.Y + 1f * scale),
                Boutique.U32(goldFade), Boutique.U32(goldClear), Boutique.U32(goldClear), Boutique.U32(goldFade));

            // Subtle dark wash
            dl.AddRectFilled(min + new Vector2(0, 1f * scale), max,
                Boutique.U32(new Vector4(0.04f, 0.05f, 0.06f, 0.55f)));

            float padX = 14f * scale;
            float midY = (min.Y + max.Y) * 0.5f;

            // Left: "MOVED N · UNSAVED" readout
            using (Plugin.Instance?.OswaldMed10?.Push())
            {
                float trackPx = 3.0f * scale;
                string a = "MOVED";
                string b = movedCount.ToString();
                string c = "/";
                string d = movedCount > 0 ? "UNSAVED" : "NO CHANGES";
                float gap = 6f * scale;
                float fontH = ImGui.GetFontSize();
                float textY = midY - fontH * 0.5f;
                float x = min.X + padX;
                Boutique.DrawTrackedText(dl, new Vector2(x, textY), a, Boutique.U32(Boutique.TextFaint), trackPx);
                x += Boutique.MeasureTrackedText(a, trackPx) + gap;
                Boutique.DrawTrackedText(dl, new Vector2(x, textY), b,
                    Boutique.U32(movedCount > 0 ? Boutique.Gold : Boutique.TextFaint), trackPx);
                x += Boutique.MeasureTrackedText(b, trackPx) + gap;
                Boutique.DrawTrackedText(dl, new Vector2(x, textY), c, Boutique.U32(Boutique.GoldDeep), trackPx);
                x += Boutique.MeasureTrackedText(c, trackPx) + gap;
                Boutique.DrawTrackedText(dl, new Vector2(x, textY), d, Boutique.U32(Boutique.TextFaint), trackPx);
            }

            // Right: Save Order + Cancel buttons sized to fit a 50px footer.
            float btnH = 30f * scale;
            float btnGap = 8f * scale;
            float saveW = 116f * scale;
            float cancelW = 82f * scale;
            float saveX = max.X - padX - saveW;
            float cancelX = saveX - btnGap - cancelW;
            float btnY = midY - btnH * 0.5f;

            DrawFooterButton(dl, "CANCEL", new Vector2(cancelX, btnY), new Vector2(cancelW, btnH), scale, isPrimary: false, onClick: () => { IsOpen = false; });
            DrawFooterButton(dl, "SAVE ORDER", new Vector2(saveX, btnY), new Vector2(saveW, btnH), scale, isPrimary: true, onClick: () =>
            {
                SaveReorderedCharacters();
                IsOpen = false;
            });
        }

        private void DrawFooterButton(ImDrawListPtr dl, string label, Vector2 pos, Vector2 size, float scale, bool isPrimary, Action onClick)
        {
            ImGui.SetCursorScreenPos(pos);
            bool clicked = ImGui.InvisibleButton($"##reorder_btn_{label}", size);
            bool hovered = ImGui.IsItemHovered();
            var btnMin = pos;
            var btnMax = pos + size;

            if (isPrimary)
            {
                // Gold gradient fill
                var topV = Boutique.Gold;
                var botV = Boutique.WithAlpha(Boutique.GoldDeep, 1f);
                if (hovered)
                {
                    topV = Boutique.WithAlpha(Boutique.GoldBright, 1f);
                    botV = Boutique.WithAlpha(Boutique.Gold, 1f);
                }
                uint topU = Boutique.U32(topV);
                uint botU = Boutique.U32(botV);
                dl.AddRectFilledMultiColor(btnMin, btnMax, topU, topU, botU, botU);
                dl.AddRect(btnMin, btnMax, Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.7f)),
                    0f, ImDrawFlags.None, 1f * scale);
            }
            else
            {
                uint bg = Boutique.U32(new Vector4(0.078f, 0.086f, 0.118f, 0.85f));
                uint border = hovered
                    ? Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.30f))
                    : Boutique.U32(Boutique.BorderSoft);
                dl.AddRectFilled(btnMin, btnMax, bg);
                dl.AddRect(btnMin, btnMax, border, 0f, ImDrawFlags.None, 1f * scale);
            }

            // Use the DEFAULT ImGui font (same as MainWindow's ADD CHARACTER pill).
            // Custom Oswald handles are baked at fixed pixel sizes and bilinear-scale
            // at runtime when GlobalScale != 1, which reads as blurry. Default font
            // is rasterised at the right DPI by Dalamud.
            {
                float trackPx = 2.0f * scale;
                float w = Boutique.MeasureTrackedText(label, trackPx);
                float h = ImGui.GetFontSize();
                uint textU = isPrimary
                    ? Boutique.U32(new Vector4(0.10f, 0.08f, 0f, 1f))
                    : (hovered ? Boutique.U32(Boutique.Text) : Boutique.U32(Boutique.TextDim));
                Boutique.DrawTrackedText(dl,
                    new Vector2(btnMin.X + (size.X - w) * 0.5f, btnMin.Y + (size.Y - h) * 0.5f),
                    label, textU, trackPx);
            }

            if (clicked) onClick();
        }

        // ── Corner brackets (BL + BR only, per boutique convention) ─────────
        private void DrawCornerBrackets(ImDrawListPtr dl, Vector2 winMin, Vector2 winMax, float scale)
        {
            float bSize = 12f * scale;
            float bInset = 8f * scale;
            uint bCol = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.40f));

            // Bottom-left
            var bl = new Vector2(winMin.X + bInset, winMax.Y - bInset);
            dl.AddLine(bl, new Vector2(bl.X, bl.Y - bSize), bCol, 1f * scale);
            dl.AddLine(bl, new Vector2(bl.X + bSize, bl.Y), bCol, 1f * scale);

            // Bottom-right
            var br = new Vector2(winMax.X - bInset, winMax.Y - bInset);
            dl.AddLine(br, new Vector2(br.X, br.Y - bSize), bCol, 1f * scale);
            dl.AddLine(br, new Vector2(br.X - bSize, br.Y), bCol, 1f * scale);
        }

        // ── Helpers ─────────────────────────────────────────────────────────
        private static string NameplateColorName(Vector3 c)
        {
            // Approximate dominant colour name for the rightmost label.
            float r = c.X, g = c.Y, b = c.Z;
            float max = MathF.Max(r, MathF.Max(g, b));
            float min = MathF.Min(r, MathF.Min(g, b));
            float chroma = max - min;
            if (chroma < 0.10f) return r > 0.6f ? "WHITE" : (r < 0.25f ? "BLACK" : "GREY");
            // Hue bucket
            float h;
            if      (max == r) h = 60f * (((g - b) / chroma) % 6f);
            else if (max == g) h = 60f * (((b - r) / chroma) + 2f);
            else               h = 60f * (((r - g) / chroma) + 4f);
            if (h < 0) h += 360f;
            if (h < 15)  return "RED";
            if (h < 40)  return "AMBER";
            if (h < 65)  return "GOLD";
            if (h < 95)  return "YELLOW";
            if (h < 165) return "GREEN";
            if (h < 200) return "CYAN";
            if (h < 245) return "BLUE";
            if (h < 285) return "VIOLET";
            if (h < 335) return "MAGENTA";
            return "ROSE";
        }

        private void SaveReorderedCharacters()
        {
            for (int i = 0; i < reorderBuffer.Count; i++)
                reorderBuffer[i].SortOrder = i;
            plugin.Characters.Clear();
            plugin.Characters.AddRange(reorderBuffer);
            plugin.Configuration.CurrentSortIndex = (int)Plugin.SortType.Manual;
            plugin.SaveConfiguration();
            plugin.AchievementTracker?.OnCharactersReordered();
            plugin.MainWindow.UpdateSortType();
            reorderBuffer.Clear();
            originalBuffer.Clear();
        }

        private float GetSafeScale(float baseScale) => Math.Clamp(baseScale, 0.3f, 5.0f);
    }
}

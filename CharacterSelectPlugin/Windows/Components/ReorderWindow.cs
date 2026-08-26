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
    public partial class ReorderWindow : IDisposable
    {
        private Plugin plugin;
        private UIStyles uiStyles;

        public bool IsOpen { get; private set; } = false;
        private List<Character> reorderBuffer = new();
        private const float DragThreshold = 5f;

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
            InitPagesBuffer(reorderBuffer);
            PublishPagesPreview();
        }

        public void Draw()
        {
            if (Plugin.UseClassicLayout) { DrawClassicLayout(); return; }
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
                pageBuffer.Clear();
                pageNameBuffer.Clear();
                pageSelection.Clear();
                ClearPagesPreview();
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

            // vertical layout
            float ribbonH = 28f * scale;
            float headerH = 84f * scale;
            float footerH = 50f * scale;

            var ribbonMin = winMin;
            var ribbonMax = new Vector2(winMax.X, winMin.Y + ribbonH);

            var headerMin = new Vector2(winMin.X, ribbonMax.Y);
            var headerMax = new Vector2(winMax.X, ribbonMax.Y + headerH);

            var footerMin = new Vector2(winMin.X, winMax.Y - footerH);
            var footerMax = winMax;

            DrawRibbon(dl, ribbonMin, ribbonMax, scale);
            DrawHeader(dl, headerMin, headerMax, scale);
            DrawPagesBody(new Vector2(winMin.X, headerMax.Y), new Vector2(winMax.X, footerMin.Y), scale);
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

            // grid-match toggle, left of close
            bool matchGrid = plugin.Configuration.ReorderPagesMatchGrid;
            var gBtnMin = new Vector2(btnMin.X - 8f * scale - btnSize, btnMin.Y);
            var gBtnMax = gBtnMin + new Vector2(btnSize, btnSize);
            ImGui.SetCursorScreenPos(gBtnMin);
            bool gClicked = ImGui.InvisibleButton("##reorder_matchgrid", new Vector2(btnSize, btnSize));
            bool gHovered = ImGui.IsItemHovered();
            if (gHovered)
            {
                int cols = Math.Max(1, plugin.Configuration.ProfileColumns);
                Boutique.Tooltip(matchGrid
                    ? $"Cards match the roster grid ({cols} per row). Click for compact cards."
                    : "Match the roster's profiles per row");
            }
            uint gBorder = matchGrid
                ? Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.65f))
                : (gHovered ? Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.40f))
                            : Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.22f)));
            dl.AddRectFilled(gBtnMin, gBtnMax, Boutique.U32(new Vector4(0.08f, 0.09f, 0.12f, 0.75f)));
            dl.AddRect(gBtnMin, gBtnMax, gBorder, 0f, ImDrawFlags.None, 1f * scale);
            string gGlyph = "";
            ImGui.PushFont(UiBuilder.IconFont);
            var gSz = ImGui.CalcTextSize(gGlyph);
            ImGui.PopFont();
            uint gCol = matchGrid
                ? Boutique.U32(Boutique.Gold)
                : (gHovered ? Boutique.U32(Boutique.Text) : Boutique.U32(Boutique.TextDim));
            dl.AddText(UiBuilder.IconFont, iconNativeSize,
                new Vector2(gBtnMin.X + (btnSize - gSz.X) * 0.5f,
                            gBtnMin.Y + (btnSize - gSz.Y) * 0.5f),
                gCol, gGlyph);
            if (gClicked)
            {
                plugin.Configuration.ReorderPagesMatchGrid = !matchGrid;
                plugin.SaveConfiguration();
            }
        }

        // footer
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

            // left readout
            using (Plugin.Instance?.OswaldMed10?.Push())
            {
                float trackPx = 3.0f * scale;
                string a = "SELECTED";
                string b = pageSelection.Count.ToString();
                string c = "/";
                string d = pagesMovedCount > 0 ? "UNSAVED" : "NO CHANGES";
                bool hot = pageSelection.Count > 0;
                float gap = 6f * scale;
                float fontH = ImGui.GetFontSize();
                float textY = midY - fontH * 0.5f;
                float x = min.X + padX;
                Boutique.DrawTrackedText(dl, new Vector2(x, textY), a, Boutique.U32(Boutique.TextFaint), trackPx);
                x += Boutique.MeasureTrackedText(a, trackPx) + gap;
                Boutique.DrawTrackedText(dl, new Vector2(x, textY), b,
                    Boutique.U32(hot ? Boutique.Gold : Boutique.TextFaint), trackPx);
                x += Boutique.MeasureTrackedText(b, trackPx) + gap;
                Boutique.DrawTrackedText(dl, new Vector2(x, textY), c, Boutique.U32(Boutique.GoldDeep), trackPx);
                x += Boutique.MeasureTrackedText(c, trackPx) + gap;
                Boutique.DrawTrackedText(dl, new Vector2(x, textY), d, Boutique.U32(Boutique.TextFaint), trackPx);
            }

            float btnH = 30f * scale;
            float btnGap = 8f * scale;
            float saveW = 124f * scale;
            float cancelW = 82f * scale;
            float saveX = max.X - padX - saveW;
            float cancelX = saveX - btnGap - cancelW;
            float btnY = midY - btnH * 0.5f;

            DrawFooterButton(dl, "CANCEL", new Vector2(cancelX, btnY), new Vector2(cancelW, btnH), scale, isPrimary: false, onClick: () => { IsOpen = false; });
            DrawFooterButton(dl, "SAVE ROSTER", new Vector2(saveX, btnY), new Vector2(saveW, btnH), scale, isPrimary: true, onClick: () =>
            {
                SavePages();
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

        private float GetSafeScale(float baseScale) => Math.Clamp(baseScale, 0.3f, 5.0f);
    }
}

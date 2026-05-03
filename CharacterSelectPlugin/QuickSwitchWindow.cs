using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Utility;
using System.Collections.Generic;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin.Windows
{
    public class QuickSwitchWindow : Window
    {
        private readonly Plugin plugin;
        private int selectedCharacterIndex = -1;
        private int selectedDesignIndex = -1;
        private bool hasInitializedSelection = false;
        private bool userIsInteracting = false;
        private string lastTrackedDesignName = "";

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

        private int chromeColorCount = 0;
        public override void PreDraw()
        {
            chromeColorCount = ThemeHelper.PushWindowChromeColors(plugin.Configuration);
        }
        public override void PostDraw()
        {
            ThemeHelper.PopWindowChromeColors(chromeColorCount);
            chromeColorCount = 0;
        }

        public override void Draw()
        {
            bool compact = plugin.Configuration.QuickSwitchCompact;
            float scale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;

            RespectCloseHotkey = !plugin.Configuration.QuickSwitchIgnoreEscape;

            // Preserve original size envelopes.
            if (compact)
            {
                SizeConstraints = new WindowSizeConstraints
                {
                    MinimumSize = new Vector2(360 * scale, 28 * scale),
                    MaximumSize = new Vector2(360 * scale, 28 * scale),
                };
            }
            else
            {
                SizeConstraints = new WindowSizeConstraints
                {
                    MinimumSize = new Vector2(360 * scale, 55 * scale),
                    MaximumSize = new Vector2(360 * scale, 58 * scale),
                };
            }

            // Mood-ring chassis paints itself; suppress ImGui's window background
            // and titlebar (a custom X handles close).
            var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
                      | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
                      | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBackground;
            if (plugin.Configuration.QuickSwitchIgnoreEscape)
                flags |= ImGuiWindowFlags.NoFocusOnAppearing;
            this.Flags = flags;

            if (!hasInitializedSelection && plugin.Characters.Count > 0)
            {
                InitializeLastUsedSelection();
                hasInitializedSelection = true;
            }

            int themeColorCount = ThemeHelper.PushThemeColors(plugin.Configuration);
            int themeStyleVarCount = ThemeHelper.PushThemeStyleVars(plugin.Configuration.UIScaleMultiplier);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

            try
            {
                var winMin = ImGui.GetWindowPos();
                var winMax = winMin + ImGui.GetWindowSize();
                var dl = ImGui.GetWindowDrawList();

                Character selectedCharacter = (selectedCharacterIndex >= 0 && selectedCharacterIndex < plugin.Characters.Count)
                    ? plugin.Characters[selectedCharacterIndex] : null;
                if (selectedCharacter != null && !userIsInteracting)
                    UpdateSelectedDesignFromConfig(selectedCharacter);

                Vector4 npColor = selectedCharacter != null
                    ? GetNameplateColor(selectedCharacter)
                    : new Vector4(0.40f, 0.42f, 0.48f, 1f); // muted neutral when no selection

                DrawMoodRingChassis(dl, winMin, winMax, npColor, scale);

                if (!compact)
                    DrawMoodRingHeader(dl, winMin, winMax, scale, selectedCharacter, npColor);

                // Row layout. Compact uses the full body; expanded reserves the
                // top 19 px for the header and 4 px at the bottom for the bar.
                float rowTop = compact ? winMin.Y + 1f : winMin.Y + 19f * scale;
                float rowBot = winMax.Y - 4f * scale;
                float rowH = rowBot - rowTop;
                float frameH = ImGui.GetFrameHeight();
                float ctrlY = rowTop + (rowH - frameH) * 0.5f;
                float startX = winMin.X + 8f * scale;

                DrawMoodRingControls(startX, ctrlY, frameH, scale, selectedCharacter, npColor, winMin, winMax, dl);

                // No close button in compact mode (matches original behaviour:
                // compact had NoTitleBar; users close via Escape or command).
                if (!compact)
                    DrawCloseButton(dl, winMin, winMax, scale);

                DrawMoodRingColorBar(dl, winMin, winMax, scale, npColor, selectedCharacter);
            }
            finally
            {
                ImGui.PopStyleVar();
                ThemeHelper.PopThemeStyleVars(themeStyleVarCount);
                ThemeHelper.PopThemeColors(themeColorCount);
            }
        }

        // ── Mood ring chassis ───────────────────────────────────────────────
        // Vertical gradient: dark velvet at top, character's nameplate colour
        // mixed in at the bottom (low alpha tint). The whole overlay shifts
        // colour with the character, with the 3 px nameplate bar as the
        // saturation peak at the very bottom.
        private void DrawMoodRingChassis(ImDrawListPtr dl, Vector2 min, Vector2 max,
            Vector4 np, float scale)
        {
            var topCol = new Vector4(0.024f, 0.027f, 0.035f, 0.96f);
            // Bottom is dark + a small fraction of NP colour mixed in.
            var botCol = new Vector4(
                MathF.Min(1f, 0.06f + np.X * 0.18f),
                MathF.Min(1f, 0.06f + np.Y * 0.18f),
                MathF.Min(1f, 0.06f + np.Z * 0.18f),
                0.96f);
            uint topU = ImGui.ColorConvertFloat4ToU32(topCol);
            uint botU = ImGui.ColorConvertFloat4ToU32(botCol);
            dl.AddRectFilledMultiColor(min, max, topU, topU, botU, botU);

            // Soft outer border, neutral grey (no gold for this option).
            uint borderU = ImGui.ColorConvertFloat4ToU32(new Vector4(0.13f, 0.14f, 0.18f, 1f));
            dl.AddRect(min, max, borderU, 0f, ImDrawFlags.None, 1f);
        }

        // Expanded-mode header: NP-coloured glyph (initial), character name in
        // tracked-caps, status tag right-aligned, plus a subtle NP-tinted
        // divider hairline below.
        private void DrawMoodRingHeader(ImDrawListPtr dl, Vector2 winMin, Vector2 winMax,
            float scale, Character selectedChar, Vector4 np)
        {
            float hdrH = 18f * scale;
            var hMin = new Vector2(winMin.X + 1f, winMin.Y + 1f);
            var hMax = new Vector2(winMax.X - 1f, winMin.Y + hdrH);
            float midY = (hMin.Y + hMax.Y) * 0.5f;

            // Glyph: small NP-coloured square with the character's initial.
            float padX = 8f * scale;
            float glyphSize = 12f * scale;
            var glyphMin = new Vector2(hMin.X + padX, midY - glyphSize * 0.5f);
            var glyphMax = glyphMin + new Vector2(glyphSize, glyphSize);
            dl.AddRectFilled(glyphMin, glyphMax, ImGui.ColorConvertFloat4ToU32(np));

            // Initial. Dark vs light text decided by NP luma.
            string initial = (selectedChar?.Name?.Length > 0)
                ? char.ToUpperInvariant(selectedChar.Name[0]).ToString() : ",";
            float luma = 0.299f * np.X + 0.587f * np.Y + 0.114f * np.Z;
            Vector4 ink = luma > 0.55f
                ? new Vector4(0.05f, 0.05f, 0.08f, 1f)
                : new Vector4(0.95f, 0.95f, 0.97f, 1f);
            var glyphFont = Plugin.Instance?.OswaldSemi9;
            if (glyphFont != null)
            {
                using (glyphFont.Push())
                {
                    var sz = ImGui.CalcTextSize(initial);
                    var tp = new Vector2(glyphMin.X + (glyphSize - sz.X) * 0.5f,
                                         glyphMin.Y + (glyphSize - sz.Y) * 0.5f);
                    dl.AddText(tp, ImGui.ColorConvertFloat4ToU32(ink), initial);
                }
            }

            // Status tag (right-aligned, before the X close button).
            string tag = "ACTIVE";
            float closeReserve = 22f * scale;
            float tagW = 0f;
            float tagX = 0f;
            var tagFont = Plugin.Instance?.OswaldMed9;
            if (tagFont != null)
            {
                using (tagFont.Push())
                {
                    float fs = ImGui.GetFontSize();
                    float track = fs * 0.34f;
                    tagW = MeasureTrackedTextLocal(tag, track);
                    tagX = hMax.X - closeReserve - tagW;
                    // NP-warm-toned tag colour (soft, not full saturation).
                    Vector4 tagCol = new Vector4(
                        0.55f + np.X * 0.3f,
                        0.55f + np.Y * 0.3f,
                        0.55f + np.Z * 0.3f,
                        1f);
                    DrawTrackedTextLocal(dl, new Vector2(tagX, midY - fs * 0.5f),
                        tag, ImGui.ColorConvertFloat4ToU32(tagCol), track);
                }
            }

            // Character name (tracked-caps), between glyph and status tag.
            float nameX = glyphMax.X + 8f * scale;
            float nameMaxX = (tagW > 0 ? tagX - 8f * scale : hMax.X - closeReserve);
            float nameMaxW = MathF.Max(0f, nameMaxX - nameX);
            string name = (selectedChar?.Name ?? ",").ToUpperInvariant();
            var nameFont = Plugin.Instance?.OswaldMed11;
            if (nameFont != null)
            {
                using (nameFont.Push())
                {
                    float fs = ImGui.GetFontSize();
                    float track = fs * 0.10f;
                    string display = TruncateTrackedTextLocal(name, track, nameMaxW);
                    Vector4 textCol = new Vector4(0.92f, 0.93f, 0.95f, 1f);
                    DrawTrackedTextLocal(dl, new Vector2(nameX, midY - fs * 0.5f),
                        display, ImGui.ColorConvertFloat4ToU32(textCol), track);
                }
            }

            // Divider hairline beneath the header, NP-tinted, mostly solid
            // with a gentle fade only at the outer edges.
            DrawDividerHairlineLocal(dl, winMin.X, winMin.Y + 19f * scale, winMax.X,
                new Vector4(np.X, np.Y, np.Z, 0.32f));
        }

        // Vanilla ImGui dropdowns + Apply button styled with an NP-tinted
        // colour scheme. Apply gets the full NP colour as its fill and is
        // sized to extend to the chassis top (under header), bottom (merging
        // with the colour bar) and right edges.
        private void DrawMoodRingControls(float startX, float ctrlY, float frameH,
            float scale, Character selectedCharacter, Vector4 np,
            Vector2 winMin, Vector2 winMax, ImDrawListPtr dl)
        {
            float winMinX = winMin.X;
            float winMinY = winMin.Y;
            float winMaxX = winMax.X;
            float winMaxY = winMax.Y;
            // Dropdowns stay neutral dark, only the chevron inside picks up
            // the NP colour, and the box around it does not.
            var frameBg = new Vector4(0.04f, 0.05f, 0.07f, 0.85f);
            var frameHov = new Vector4(0.07f, 0.08f, 0.11f, 0.92f);
            var frameAct = new Vector4(0.10f, 0.11f, 0.14f, 0.96f);

            // Apply hero: NP at full saturation; hover brighter, active darker.
            Vector4 applyBg = np;
            Vector4 applyHov = new Vector4(
                MathF.Min(1f, np.X * 1.10f),
                MathF.Min(1f, np.Y * 1.10f),
                MathF.Min(1f, np.Z * 1.10f),
                np.W);
            Vector4 applyAct = new Vector4(np.X * 0.85f, np.Y * 0.85f, np.Z * 0.85f, np.W);

            // Selected combo row: NP-tinted (selection still reads as the active
            // item). Hover/active lift neutrally, no yellow flash.
            Vector4 headerSel = new Vector4(np.X, np.Y, np.Z, 0.28f);
            Vector4 headerHov = new Vector4(0.10f, 0.11f, 0.14f, 0.92f);
            Vector4 headerActC = new Vector4(np.X, np.Y, np.Z, 0.42f);

            var popupBg = new Vector4(0.024f, 0.027f, 0.035f, 0.97f);
            var brightText = new Vector4(0.92f, 0.93f, 0.95f, 1f);

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 0f);

            ImGui.PushStyleColor(ImGuiCol.FrameBg, frameBg);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, frameHov);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, frameAct);
            // Combo arrow buttons match the combo body so there is no
            // separate coloured "box" around the chevron. The apply
            // button overrides these locally before its own ImGui.Button.
            ImGui.PushStyleColor(ImGuiCol.Button, frameBg);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, frameHov);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, frameAct);
            ImGui.PushStyleColor(ImGuiCol.Header, headerSel);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, headerHov);
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, headerActC);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, popupBg);
            ImGui.PushStyleColor(ImGuiCol.Text, brightText);

            try
            {
                float dropdownWidth = 135f * scale;
                float spacing = 6f * scale;

                ImGui.SetCursorScreenPos(new Vector2(startX, ctrlY));

                // Character dropdown
                var charComboMin = new Vector2(startX, ctrlY);
                var charComboMax = new Vector2(startX + dropdownWidth, ctrlY + frameH);
                ImGui.SetNextItemWidth(dropdownWidth);
                int tempCharacterIndex = selectedCharacterIndex;
                if (ImGui.BeginCombo("##CharacterDropdown", GetSelectedCharacterName(), ImGuiComboFlags.HeightRegular))
                {
                    for (int i = 0; i < plugin.Characters.Count; i++)
                    {
                        var character = plugin.Characters[i];
                        bool isSelected = tempCharacterIndex == i;
                        if (ImGui.Selectable(character.Name, isSelected))
                        {
                            tempCharacterIndex = i;
                            if (character.Designs.Count > 0)
                            {
                                var sortedDesigns = GetSortedDesigns(character);
                                if (sortedDesigns.Count > 0)
                                    selectedDesignIndex = GetOriginalIndex(character, sortedDesigns[0]);
                            }
                            else
                            {
                                selectedDesignIndex = -1;
                            }
                        }
                        if (isSelected) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
                selectedCharacterIndex = tempCharacterIndex;

                // NP-coloured chevron overlay over ImGui's default arrow.
                DrawNpChevronOverlay(dl, charComboMin, charComboMax, scale, np, frameBg);

                ImGui.SameLine(0, spacing);

                // Design dropdown
                float designX = startX + dropdownWidth + spacing;
                var designComboMin = new Vector2(designX, ctrlY);
                var designComboMax = new Vector2(designX + dropdownWidth, ctrlY + frameH);
                if (selectedCharacter != null)
                {
                    int tempDesignIndex = selectedDesignIndex;
                    ImGui.SetNextItemWidth(dropdownWidth);
                    if (ImGui.BeginCombo("##DesignDropdown", GetSelectedDesignName(selectedCharacter), ImGuiComboFlags.HeightRegular))
                    {
                        userIsInteracting = true;
                        var orderedDesigns = GetSortedDesigns(selectedCharacter)
                            .Select(d => new { Design = d, OriginalIndex = GetOriginalIndex(selectedCharacter, d) })
                            .ToList();

                        for (int j = 0; j < orderedDesigns.Count; j++)
                        {
                            var entry = orderedDesigns[j];
                            bool isSelected = tempDesignIndex == entry.OriginalIndex;

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
                                        var (dispW, dispH) = CalculateImageDimensions(texture, maxSize);
                                        var mousePos = ImGui.GetMousePos();
                                        var dropdownRect = ImGui.GetItemRectMax();
                                        var viewportSize = ImGui.GetMainViewport().Size;
                                        var tooltipPos = new Vector2(dropdownRect.X + 10, mousePos.Y - dispH / 2);
                                        if (tooltipPos.X + dispW > viewportSize.X)
                                            tooltipPos.X = ImGui.GetItemRectMin().X - dispW - 10;
                                        if (tooltipPos.Y < 0) tooltipPos.Y = 0;
                                        else if (tooltipPos.Y + dispH > viewportSize.Y)
                                            tooltipPos.Y = viewportSize.Y - dispH;
                                        ImGui.SetNextWindowPos(tooltipPos);
                                        // Restore neutral framing for the preview tooltip.
                                        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 8f));
                                        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.06f, 0.07f, 0.09f, 0.96f));
                                        ImGui.BeginTooltip();
                                        ImGui.Image(texture.Handle, new Vector2(dispW, dispH));
                                        ImGui.EndTooltip();
                                        ImGui.PopStyleColor();
                                        ImGui.PopStyleVar();
                                    }
                                }
                                catch { }
                            }

                            if (isSelected) ImGui.SetItemDefaultFocus();
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

                // NP-coloured chevron overlay over the design dropdown.
                DrawNpChevronOverlay(dl, designComboMin, designComboMax, scale, np, frameBg);

                // Apply button, extends from row top to chassis bottom (so the
                // 3 px nameplate bar visually flows into it) and out to the
                // chassis right edge. NP-coloured fill, contrast-aware text.
                bool compact = plugin.Configuration.QuickSwitchCompact;
                float applyTopY = compact ? winMinY + 1f : winMinY + 20f * scale;
                float applyBotY = winMaxY;
                float applyLeftX = designX + dropdownWidth + spacing;
                float applyRightX = winMaxX;
                float applyW = MathF.Max(40f, applyRightX - applyLeftX);
                float applyH = applyBotY - applyTopY;

                ImGui.SetCursorScreenPos(new Vector2(applyLeftX, applyTopY));

                // Apply uses NP fill locally + black ink (overrides the global
                // FrameBg-matched Button colours so the apply box is the only
                // NP-coloured element).
                ImGui.PushStyleColor(ImGuiCol.Button, applyBg);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, applyHov);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, applyAct);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.05f, 0.05f, 0.08f, 1f));

                if (selectedCharacter != null)
                {
                    if (ImGui.Button("Apply", new Vector2(applyW, applyH)))
                    {
                        userIsInteracting = false;
                        ApplySelection();
                    }
                    CharacterSelectPlugin.Windows.Styles.UIStyles.ApplyHoverSheenToLastItemStatic("quickswitch_apply_btn");

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        userIsInteracting = false;
                        var io = ImGui.GetIO();
                        if (io.KeyCtrl) RevertToCurrentPlayerCharacter();
                        else ApplyToTarget();
                    }
                }
                else
                {
                    ImGui.BeginDisabled();
                    ImGui.Button("Apply", new Vector2(applyW, applyH));
                    ImGui.EndDisabled();
                }

                ImGui.PopStyleColor(4); // applyBg, hov, act, ink
            }
            finally
            {
                ImGui.PopStyleColor(11);
                ImGui.PopStyleVar(3);
            }
        }

        // 3 px nameplate colour bar at the bottom (this surface's signature).
        private void DrawMoodRingColorBar(ImDrawListPtr dl, Vector2 winMin, Vector2 winMax,
            float scale, Vector4 np, Character selectedCharacter)
        {
            float barH = 3f * scale;
            var bMin = new Vector2(winMin.X, winMax.Y - barH);
            var bMax = winMax;
            Vector4 col = selectedCharacter != null ? np : new Vector4(0.4f, 0.4f, 0.45f, 1f);
            dl.AddRectFilled(bMin, bMax, ImGui.ColorConvertFloat4ToU32(col));
        }

        // Custom close button. Centred vertically in the header band (the
        // header runs from y=1 to y=19, so y=10 is its midpoint). Hover
        // backdrop keeps the glyph legible against any nameplate-coloured
        // apply zone behind it in compact mode.
        private void DrawCloseButton(ImDrawListPtr dl, Vector2 winMin, Vector2 winMax, float scale)
        {
            float btnSize = 14f * scale;
            float padX = 7f * scale;
            float headerMidY = winMin.Y + 10f * scale;
            var bMin = new Vector2(winMax.X - padX - btnSize, headerMidY - btnSize * 0.5f);
            var bMax = bMin + new Vector2(btnSize, btnSize);

            ImGui.SetCursorScreenPos(bMin);
            bool clicked = ImGui.InvisibleButton("##qsClose", new Vector2(btnSize, btnSize));
            bool hovered = ImGui.IsItemHovered();

            if (hovered)
                dl.AddRectFilled(bMin, bMax,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f)));

            Vector4 glyphCol = hovered
                ? new Vector4(1f, 1f, 1f, 1f)
                : new Vector4(0.85f, 0.87f, 0.92f, 0.85f);
            uint colU = ImGui.ColorConvertFloat4ToU32(glyphCol);
            float inset = 4f * scale;
            float thick = 1.4f * scale;
            dl.AddLine(bMin + new Vector2(inset, inset), bMax - new Vector2(inset, inset), colU, thick);
            dl.AddLine(new Vector2(bMax.X - inset, bMin.Y + inset),
                       new Vector2(bMin.X + inset, bMax.Y - inset), colU, thick);

            if (clicked) IsOpen = false;
        }

        // Replace ImGui's default white combo chevron with an NP-coloured one.
        // We paint a small chassis-coloured rect over ImGui's chevron first to
        // erase it, then draw our own triangle in NP. The arrow lives in a
        // square on the right edge of the combo (width ≈ combo height).
        private static void DrawNpChevronOverlay(ImDrawListPtr dl, Vector2 comboMin, Vector2 comboMax,
            float scale, Vector4 np, Vector4 frameBg)
        {
            float h = comboMax.Y - comboMin.Y;
            float arrowCx = comboMax.X - h * 0.5f;
            float arrowCy = (comboMin.Y + comboMax.Y) * 0.5f;

            // Cover ImGui's default chevron with the same colour as the combo
            // body so the box stays uniform, no visible second chevron.
            float coverR = 6f * scale;
            dl.AddRectFilled(
                new Vector2(arrowCx - coverR, arrowCy - coverR),
                new Vector2(arrowCx + coverR, arrowCy + coverR),
                ImGui.ColorConvertFloat4ToU32(frameBg));

            float r = 5f * scale;
            dl.AddTriangleFilled(
                new Vector2(arrowCx - r, arrowCy - r * 0.5f),
                new Vector2(arrowCx + r, arrowCy - r * 0.5f),
                new Vector2(arrowCx,     arrowCy + r * 0.75f),
                ImGui.ColorConvertFloat4ToU32(np));
        }

        // ── Local typography helpers (per-glyph tracked-caps) ───────────────
        private static float MeasureTrackedTextLocal(string text, float trackPx)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            float w = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                w += ImGui.CalcTextSize(text.Substring(i, 1)).X;
                if (i < text.Length - 1) w += trackPx;
            }
            return w;
        }

        private static void DrawTrackedTextLocal(ImDrawListPtr dl, Vector2 pos, string text, uint colour, float trackPx)
        {
            if (string.IsNullOrEmpty(text)) return;
            float x = pos.X;
            for (int i = 0; i < text.Length; i++)
            {
                string g = text.Substring(i, 1);
                dl.AddText(new Vector2(x, pos.Y), colour, g);
                x += ImGui.CalcTextSize(g).X;
                if (i < text.Length - 1) x += trackPx;
            }
        }

        private static string TruncateTrackedTextLocal(string text, float trackPx, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0) return "";
            if (MeasureTrackedTextLocal(text, trackPx) <= maxWidth) return text;
            string ellipsis = "...";
            float ellW = MeasureTrackedTextLocal(ellipsis, trackPx);
            if (ellW > maxWidth) return "";
            string result = "";
            float w = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                string g = text.Substring(i, 1);
                float gw = ImGui.CalcTextSize(g).X + (i > 0 ? trackPx : 0);
                if (w + gw + trackPx + ellW > maxWidth) break;
                result += g;
                w += gw;
            }
            return result + ellipsis;
        }

        // Divider hairline: solid through most of the span, gentle fade only
        // at the outer 8% on each side.
        private static void DrawDividerHairlineLocal(ImDrawListPtr dl, float x0, float y, float x1, Vector4 colour)
        {
            float w = x1 - x0;
            uint solid = ImGui.ColorConvertFloat4ToU32(colour);
            uint clear = ImGui.ColorConvertFloat4ToU32(new Vector4(colour.X, colour.Y, colour.Z, 0f));
            dl.AddRectFilledMultiColor(new Vector2(x0,             y), new Vector2(x0 + 0.08f * w, y + 1f), clear, solid, solid, clear);
            dl.AddRectFilledMultiColor(new Vector2(x0 + 0.08f * w, y), new Vector2(x0 + 0.92f * w, y + 1f), solid, solid, solid, solid);
            dl.AddRectFilledMultiColor(new Vector2(x0 + 0.92f * w, y), new Vector2(x1,             y + 1f), solid, clear, clear, solid);
        }

        /// <summary>Initialises dropdown selections from last used character.</summary>
        private void InitializeLastUsedSelection()
        {
            try
            {
                Plugin.Log.Debug("[QuickSwitch] Initializing last used selection...");

                if (Plugin.ObjectTable.LocalPlayer?.HomeWorld.IsValid == true)
                {
                    string localName = Plugin.ObjectTable.LocalPlayer.Name.TextValue;
                    string worldName = Plugin.ObjectTable.LocalPlayer.HomeWorld.Value.Name.ToString();
                    string fullKey = $"{localName}@{worldName}";

                    if (plugin.Configuration.LastUsedCharacterByPlayer.TryGetValue(fullKey, out var lastUsedKey))
                    {
                        var character = plugin.Characters.FirstOrDefault(c =>
                            $"{c.Name}@{worldName}" == lastUsedKey);

                        if (character != null)
                        {
                            selectedCharacterIndex = plugin.Characters.IndexOf(character);
                            Plugin.Log.Debug($"[QuickSwitch] Found last used character: {character.Name} at index {selectedCharacterIndex}");

                            if (plugin.Configuration.LastUsedDesignByCharacter.TryGetValue(character.Name, out var lastDesignName))
                            {
                                var design = character.Designs.FirstOrDefault(d => d.Name == lastDesignName);
                                if (design != null)
                                {
                                    selectedDesignIndex = character.Designs.IndexOf(design);
                                    lastTrackedDesignName = lastDesignName;
                                    Plugin.Log.Debug($"[QuickSwitch] Found last used design: {lastDesignName} at index {selectedDesignIndex}");
                                }
                            }
                            return;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(plugin.Configuration.LastUsedCharacterKey))
                {
                    var character = plugin.Characters.FirstOrDefault(c => c.Name == plugin.Configuration.LastUsedCharacterKey);
                    if (character != null)
                    {
                        selectedCharacterIndex = plugin.Characters.IndexOf(character);
                        Plugin.Log.Debug($"[QuickSwitch] Found global last used character: {character.Name} at index {selectedCharacterIndex}");

                        if (plugin.Configuration.LastUsedDesignByCharacter.TryGetValue(character.Name, out var lastDesignName))
                        {
                            var design = character.Designs.FirstOrDefault(d => d.Name == lastDesignName);
                            if (design != null)
                            {
                                selectedDesignIndex = character.Designs.IndexOf(design);
                                lastTrackedDesignName = lastDesignName;
                                Plugin.Log.Debug($"[QuickSwitch] Found last used design for global character: {lastDesignName} at index {selectedDesignIndex}");
                            }
                        }
                        return;
                    }
                }

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
                if (plugin.Characters.Count > 0)
                    selectedCharacterIndex = 0;
            }
        }

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

                if (plugin.Configuration.LastUsedDesignByCharacter.TryGetValue(character.Name, out var lastDesignName))
                {
                    var design = character.Designs.FirstOrDefault(d => d.Name == lastDesignName);
                    selectedDesignIndex = design != null ? character.Designs.IndexOf(design) : -1;
                }
                else
                {
                    selectedDesignIndex = -1;
                }

                Plugin.Log.Debug($"[QuickSwitch] Updated selection to character: {character.Name} (index {selectedCharacterIndex})");
            }
        }

        private List<CharacterDesign> GetSortedDesigns(Character character)
        {
            var sortIndex = plugin.Configuration.CurrentDesignSortIndex;
            var designs = character.Designs.ToList();
            
            // 0=Favorites, 1=Alphabetical, 2=Recent, 3=Oldest, 4=Manual
            if (sortIndex == 4) // Manual
                return designs;

            if (sortIndex == 0) // Favorites
            {
                designs.Sort((a, b) =>
                {
                    int favCompare = b.IsFavorite.CompareTo(a.IsFavorite);
                    if (favCompare != 0) return favCompare;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            }
            else if (sortIndex == 1) // Alphabetical
            {
                designs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            else if (sortIndex == 2) // Recent
            {
                designs.Sort((a, b) => b.DateAdded.CompareTo(a.DateAdded));
            }
            else if (sortIndex == 3) // Oldest
            {
                designs.Sort((a, b) => a.DateAdded.CompareTo(b.DateAdded));
            }
            
            return designs;
        }
        
        private int GetOriginalIndex(Character character, CharacterDesign design)
        {
            return character.Designs.FindIndex(d => d.Id == design.Id);
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
            plugin.AchievementTracker?.OnSwitchFromQuickSwitch();
            plugin.AchievementTracker?.CheckSwitchMethodsAll();
            // ApplyProfile handles per-pose detection and PoseRestorer internally.
            // DO NOT call PoseRestorer again here, the previous duplicate call would
            // fire ~500ms later via RunOnTick and clobber the design's pose override.
            plugin.ApplyProfile(character, selectedDesignIndex);
        }

        private void ApplyToTarget()
        {
            if (selectedCharacterIndex < 0 || selectedCharacterIndex >= plugin.Characters.Count)
                return;

            var character = plugin.Characters[selectedCharacterIndex];

            var target = plugin.GetCurrentTarget();
            if (target == null)
            {
                Plugin.ChatGui.PrintError("[Character Select+] No target selected.");
                return;
            }

            var targetInfo = new { ObjectIndex = target.ObjectIndex, ObjectKind = target.ObjectKind, Name = target.Name?.ToString() ?? "Unknown" };
            var designIndex = selectedDesignIndex >= 0 && selectedDesignIndex < character.Designs.Count ? selectedDesignIndex : -1;

            _ = Task.Run(async () =>
            {
                try
                {
                    await plugin.ApplyToTarget(character, -1);
                    Plugin.Log.Information($"[QuickSwitch] Applied character {character.Name} to target: {targetInfo.Name}");

                    if (designIndex >= 0)
                    {
                        await plugin.ApplyToTarget(character, designIndex);
                        Plugin.Log.Information($"[QuickSwitch] Applied design '{character.Designs[designIndex].Name}' to target: {targetInfo.Name}");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"[QuickSwitch] Error applying to target: {ex}");
                }
            });
        }

        private void RevertToCurrentPlayerCharacter()
        {
            if (plugin.activeCharacter != null)
            {
                var matchingCharacterIndex = plugin.Characters.FindIndex(c => c.Name == plugin.activeCharacter.Name);
                if (matchingCharacterIndex >= 0)
                {
                    selectedCharacterIndex = matchingCharacterIndex;

                    if (plugin.Configuration.LastUsedDesignByCharacter.TryGetValue(plugin.activeCharacter.Name, out var lastDesignName))
                    {
                        var character = plugin.Characters[matchingCharacterIndex];
                        var designIndex = character.Designs.FindIndex(d => d.Name.Equals(lastDesignName, StringComparison.OrdinalIgnoreCase));
                        selectedDesignIndex = designIndex >= 0 ? designIndex : -1;
                        Plugin.Log.Information($"[QuickSwitch] Reverted to active character: {plugin.activeCharacter.Name} with design: {(designIndex >= 0 ? lastDesignName : "None")}");
                    }
                    else
                    {
                        selectedDesignIndex = -1;
                        Plugin.Log.Information($"[QuickSwitch] Reverted to active character: {plugin.activeCharacter.Name} (no design)");
                    }

                    userIsInteracting = false;
                    return;
                }
            }

            if (plugin.Characters.Count > 0)
            {
                selectedCharacterIndex = 0;
                selectedDesignIndex = -1;
                userIsInteracting = false;
                Plugin.Log.Information($"[QuickSwitch] No active character found, reverted to first character: {plugin.Characters[0].Name}");
            }
        }

        private bool ShouldUploadToServer(Character character)
        {
            var sharing = character.RPProfile?.Sharing ?? ProfileSharing.AlwaysShare;

            if (sharing == ProfileSharing.NeverShare)
            {
                Plugin.Log.Debug($"[QuickSwitch-ShouldUpload] NeverShare - not uploading {character.Name}");
                return false;
            }

            Plugin.Log.Debug($"[QuickSwitch-ShouldUpload] ✓ {sharing} - uploading {character.Name}");
            return true;
        }

        private ProfileSharing GetEffectiveSharingForUpload(Character character, string currentPhysicalCharacter)
        {
            var sharing = character.RPProfile?.Sharing ?? ProfileSharing.AlwaysShare;

            if (sharing != ProfileSharing.ShowcasePublic)
                return sharing;

            var userMain = plugin.Configuration.GalleryMainCharacter;
            bool onMainCharacter = !string.IsNullOrEmpty(userMain) && currentPhysicalCharacter == userMain;

            if (onMainCharacter)
            {
                Plugin.Log.Debug($"[QuickSwitch-Sharing] ShowcasePublic on Main Character - will appear in Gallery");
                return ProfileSharing.ShowcasePublic;
            }
            else
            {
                Plugin.Log.Debug($"[QuickSwitch-Sharing] ShowcasePublic but not on Main Character - sending as AlwaysShare");
                return ProfileSharing.AlwaysShare;
            }
        }

        private (float width, float height) CalculateImageDimensions(Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap texture, float maxSize)
        {
            float originalWidth = texture.Width;
            float originalHeight = texture.Height;
            float aspectRatio = originalWidth / originalHeight;

            if (aspectRatio > 1) // Landscape
            {
                return (maxSize, maxSize / aspectRatio);
            }
            else // Portrait or Square
            {
                return (maxSize * aspectRatio, maxSize);
            }
        }

        private void UpdateSelectedDesignFromConfig(Character character)
        {
            if (plugin.Configuration.LastUsedDesignByCharacter.TryGetValue(character.Name, out var lastUsedDesignName))
            {
                if (lastUsedDesignName != lastTrackedDesignName)
                {
                    userIsInteracting = false;
                    lastTrackedDesignName = lastUsedDesignName;

                    var activeDesign = character.Designs.FirstOrDefault(d => d.Name.Equals(lastUsedDesignName, StringComparison.OrdinalIgnoreCase));
                    selectedDesignIndex = activeDesign != null ? character.Designs.IndexOf(activeDesign) : -1;
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(lastTrackedDesignName))
                {
                    userIsInteracting = false;
                    lastTrackedDesignName = "";
                    selectedDesignIndex = -1;
                }
            }
        }
    }
}

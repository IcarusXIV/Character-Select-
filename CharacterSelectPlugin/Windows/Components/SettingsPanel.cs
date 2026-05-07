using System;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using CharacterSelectPlugin.Windows.Styles;
using System.Collections.Generic;
using CharacterSelectPlugin.Managers;
using System.IO;
using System.Windows.Forms;
using System.Threading;

namespace CharacterSelectPlugin.Windows.Components
{
    public partial class SettingsPanel : IDisposable
    {
        private Plugin plugin;
        private UIStyles uiStyles;
        private MainWindow mainWindow;

        private int manualMigrationCharacterIndex = -1;
        private string manualMigrationPreviousName = "";
        private string manualMigrationPhysicalName = "";
        private string manualMigrationStatusMessage = "";
        private DateTime manualMigrationStatusTime = DateTime.MinValue;

        // Holds in-flight UI Scale slider value during drag, applied on release.
        private float _pendingUIScale = float.NaN;

        // Delete-my-data flow state
        private bool deleteDataInProgress;
        private string deleteDataStatusMessage = "";
        private DateTime deleteDataStatusTime = DateTime.MinValue;
        private string? pendingExpandSection = null; // Section to force-expand on next draw
        private int selectedBlockedUserIndex = -1;
        private string newRealCharacterBuffer = "";
        private string newCSCharacterBuffer = "";
        private bool newAssignmentUseDesign = false;
        private string newAssignmentDesignBuffer = "";
        private string editingAssignmentKey = "";
        private string editingAssignmentValue = "";
        private bool editingAssignmentUseDesign = false;
        private string editingAssignmentDesignBuffer = "";
        private string backupNameBuffer = "";
        private List<BackupFileInfo> availableBackups = new();
        private string lastBackupStatusMessage = "";
        private bool lastBackupStatusIsError = false;

        // Random Groups
        private string newRandomGroupName = "";
        private DateTime lastBackupStatusTime = DateTime.MinValue;
        private string? pendingImportPath = null;
        private bool isCapturingRevealKey = false;

        // Boutique chassis: single-category nav
        private int activeCategoryIndex = 0;
        private int prevCategoryIndex = 0;
        private double categoryChangeStartT = 0;
        private const float CategoryChangeDurationS = 0.18f;
        // Maps section names to their categories[] index, used by ExpandSection.
        private static readonly Dictionary<string, int> CategoryNameToIndex = new()
        {
            { "Visual Settings",        0 },
            { "Glamourer Automations",  1 },
            { "Behavior Settings",      2 },
            { "Achievements",           3 },
            { "Random Groups",          4 },
            { "Honorific",              5 },
            { "Main Character",         6 },
            { "Character Assignments",  7 },
            { "Job Assignments",        8 },
            { "Immersive Dialogue",     9 },
            { "Name Sync",             10 },
            { "Conflict Resolution",   11 },
            { "Backup & Restore",      12 },
            { "Account & Data",        13 },
            { "Community & Moderation", 2 }, // legacy alias → Behavior
        };
        // Short labels for the rail so long category names don't clip the
        // 184px rail width. Index aligns with Categories[].
        private static readonly string[] CategoryShortNames =
        {
            "VISUAL", "AUTOMATIONS", "BEHAVIOUR", "ACHIEVEMENTS", "RANDOM",
            "HONORIFIC", "MAIN CHAR", "CHAR ASSIGN", "JOB ASSIGN", "DIALOGUE",
            "NAME SYNC", "CONFLICT", "BACKUP", "ACCOUNT",
        };
        // Each category: display name, rainbow tint, FontAwesome glyph.
        private static readonly (string name, Vector4 tint, string icon)[] Categories =
        {
            ("Visual Settings",       new Vector4(1.00f, 0.35f, 0.35f, 1f), ""),  // red, palette
            ("Glamourer Automations", new Vector4(1.00f, 0.60f, 0.20f, 1f), ""),  // orange, cog
            ("Behavior Settings",     new Vector4(1.00f, 0.90f, 0.30f, 1f), ""),  // yellow, cogs
            ("Achievements",          new Vector4(1.00f, 0.84f, 0.00f, 1f), ""),  // gold, trophy
            ("Random Groups",         new Vector4(0.85f, 0.95f, 0.30f, 1f), ""),  // yellow-green, dice
            ("Honorific",             new Vector4(0.70f, 1.00f, 0.30f, 1f), ""),  // lime, crown
            ("Main Character",        new Vector4(0.30f, 0.90f, 0.40f, 1f), ""),  // green, user
            ("Character Assignments", new Vector4(0.30f, 0.90f, 0.90f, 1f), ""),  // cyan, users-cog
            ("Job Assignments",       new Vector4(0.20f, 0.80f, 0.85f, 1f), ""),  // teal, briefcase
            ("Immersive Dialogue",    new Vector4(0.40f, 0.60f, 1.00f, 1f), ""),  // blue, comment
            ("Name Sync",             new Vector4(0.55f, 0.40f, 1.00f, 1f), ""),  // indigo, id-card
            ("Conflict Resolution",   new Vector4(0.80f, 0.40f, 1.00f, 1f), ""),  // purple, hammer
            ("Backup & Restore",      new Vector4(1.00f, 0.45f, 0.70f, 1f), ""),  // pink, archive
            ("Account & Data",        new Vector4(1.00f, 0.55f, 0.55f, 1f), ""),  // mauve, sync
        };

        // Key capture for reveal names
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        // Common key codes and their display names
        private static readonly Dictionary<int, string> KeyNames = new()
        {
            { 0x08, "Backspace" }, { 0x09, "Tab" }, { 0x0D, "Enter" }, { 0x10, "Shift" },
            { 0x11, "Ctrl" }, { 0x12, "Alt" }, { 0x13, "Pause" }, { 0x14, "Caps Lock" },
            { 0x1B, "Escape" }, { 0x20, "Space" }, { 0x21, "Page Up" }, { 0x22, "Page Down" },
            { 0x23, "End" }, { 0x24, "Home" }, { 0x25, "Left" }, { 0x26, "Up" },
            { 0x27, "Right" }, { 0x28, "Down" }, { 0x2D, "Insert" }, { 0x2E, "Delete" },
            { 0x30, "0" }, { 0x31, "1" }, { 0x32, "2" }, { 0x33, "3" }, { 0x34, "4" },
            { 0x35, "5" }, { 0x36, "6" }, { 0x37, "7" }, { 0x38, "8" }, { 0x39, "9" },
            { 0x41, "A" }, { 0x42, "B" }, { 0x43, "C" }, { 0x44, "D" }, { 0x45, "E" },
            { 0x46, "F" }, { 0x47, "G" }, { 0x48, "H" }, { 0x49, "I" }, { 0x4A, "J" },
            { 0x4B, "K" }, { 0x4C, "L" }, { 0x4D, "M" }, { 0x4E, "N" }, { 0x4F, "O" },
            { 0x50, "P" }, { 0x51, "Q" }, { 0x52, "R" }, { 0x53, "S" }, { 0x54, "T" },
            { 0x55, "U" }, { 0x56, "V" }, { 0x57, "W" }, { 0x58, "X" }, { 0x59, "Y" },
            { 0x5A, "Z" }, { 0x60, "Numpad 0" }, { 0x61, "Numpad 1" }, { 0x62, "Numpad 2" },
            { 0x63, "Numpad 3" }, { 0x64, "Numpad 4" }, { 0x65, "Numpad 5" }, { 0x66, "Numpad 6" },
            { 0x67, "Numpad 7" }, { 0x68, "Numpad 8" }, { 0x69, "Numpad 9" },
            { 0x6A, "Numpad *" }, { 0x6B, "Numpad +" }, { 0x6D, "Numpad -" },
            { 0x6E, "Numpad ." }, { 0x6F, "Numpad /" },
            { 0x70, "F1" }, { 0x71, "F2" }, { 0x72, "F3" }, { 0x73, "F4" },
            { 0x74, "F5" }, { 0x75, "F6" }, { 0x76, "F7" }, { 0x77, "F8" },
            { 0x78, "F9" }, { 0x79, "F10" }, { 0x7A, "F11" }, { 0x7B, "F12" },
            { 0x90, "Num Lock" }, { 0x91, "Scroll Lock" },
            { 0xA0, "Left Shift" }, { 0xA1, "Right Shift" },
            { 0xA2, "Left Ctrl" }, { 0xA3, "Right Ctrl" },
            { 0xA4, "Left Alt" }, { 0xA5, "Right Alt" },
            { 0xBA, ";" }, { 0xBB, "=" }, { 0xBC, "," }, { 0xBD, "-" },
            { 0xBE, "." }, { 0xBF, "/" }, { 0xC0, "`" },
            { 0xDB, "[" }, { 0xDC, "\\" }, { 0xDD, "]" }, { 0xDE, "'" }
        };

        public SettingsPanel(Plugin plugin, UIStyles uiStyles, MainWindow mainWindow)
        {
            this.plugin = plugin;
            this.uiStyles = uiStyles;
            this.mainWindow = mainWindow;
        }

        public void Dispose()
        {
        }

        public void Draw()
        {
            if (Plugin.UseClassicLayout) { DrawClassicLayout(); return; }
            if (!plugin.IsSettingsOpen)
                return;

            var totalScale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);

            // 720x880 baseline, capped to viewport
            var viewport = ImGui.GetMainViewport();
            float wantW = 720f * totalScale;
            float wantH = 880f * totalScale;
            float maxW = viewport.Size.X * 0.95f;
            float maxH = viewport.Size.Y * 0.95f;
            var windowWidth = MathF.Min(wantW, maxW);
            var windowHeight = MathF.Min(wantH, maxH);

            var centerPos = new Vector2(
                viewport.Pos.X + (viewport.Size.X - windowWidth) * 0.5f,
                viewport.Pos.Y + (viewport.Size.Y - windowHeight) * 0.5f
            );
            ImGui.SetNextWindowPos(centerPos, ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(windowWidth, windowHeight), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(
                new Vector2(720f * totalScale, 560f * totalScale),
                new Vector2(maxW, maxH));

            bool isSettingsOpen = plugin.IsSettingsOpen;
            // NoTitleBar so we don't get two close X buttons (boutique header has its own)
            var windowFlags = ImGuiWindowFlags.NoCollapse
                            | ImGuiWindowFlags.NoScrollbar
                            | ImGuiWindowFlags.NoTitleBar;

            ImGui.PushStyleColor(ImGuiCol.WindowBg, Boutique.Surface0);
            ImGui.PushStyleColor(ImGuiCol.Border, Boutique.Border);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);

            try
            {
                if (ImGui.Begin("Character Select+ Settings", ref isSettingsOpen, windowFlags))
                {
                    if (!isSettingsOpen)
                        plugin.IsSettingsOpen = false;

                    DrawBoutiqueChassis(totalScale);

                    // Window corner brackets (BL + BR only, boutique theme law)
                    DrawWindowBrackets(totalScale);
                }
                ImGui.End();
            }
            finally
            {
                ImGui.PopStyleVar(3);
                ImGui.PopStyleColor(2);
            }
        }

        // ── Boutique chassis composition ────────────────────────────────────
        private void DrawBoutiqueChassis(float scale)
        {
            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            // Mockup spec: ribbon 30, header ~88, rail 184. Stop inflating
            // these "for impact": the spec is the spec.
            float ribbonH = 30f * scale;
            float headerH = 110f * scale;
            float railW   = 184f * scale;

            var ribbonMin = winPos;
            var ribbonMax = new Vector2(winPos.X + winSize.X, winPos.Y + ribbonH);
            var headerMin = new Vector2(winPos.X, ribbonMax.Y);
            var headerMax = new Vector2(winPos.X + winSize.X, headerMin.Y + headerH);
            var bodyMin   = new Vector2(winPos.X, headerMax.Y);
            var bodyMax   = new Vector2(winPos.X + winSize.X, winPos.Y + winSize.Y);
            var railMin   = bodyMin;
            var railMax   = new Vector2(bodyMin.X + railW, bodyMax.Y);
            var contentMin = new Vector2(railMax.X, bodyMin.Y);
            var contentMax = bodyMax;

            var dl = ImGui.GetWindowDrawList();
            DrawBoutiqueSettingsRibbon(dl, ribbonMin, ribbonMax, scale);
            DrawBoutiqueSettingsHeader(dl, headerMin, headerMax, scale);
            DrawBoutiqueBodyBackdrop(dl, bodyMin, bodyMax, scale);
            DrawBoutiqueNavRail(dl, railMin, railMax, scale);
            DrawBoutiqueContent(dl, contentMin, contentMax, scale);
        }

        private void DrawBoutiqueSettingsRibbon(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
        {
            // Background gradient + gold hairlines
            BoutiqueChassis.DrawRibbonBackground(dl, min, max, scale);

            // Pulsing gold pip: explicit 6x6 square with halo (mockup .ribbon-pip).
            // Using DrawGoldPip's defaults rendered too small at this scale; draw
            // it inline for full control.
            float padX = 14f * scale;
            float midY = (min.Y + max.Y) * 0.5f;
            double time = ImGui.GetTime();
            float pipPulse = 0.55f + 0.45f * (float)Math.Sin(time * 2.4);
            float pipR = 3.5f * scale;
            float pipGlowR = 8f * scale;
            var pipCentre = new Vector2(min.X + padX + pipR, midY);
            // Halo (concentric): three soft layers
            for (int g = 3; g >= 1; g--)
            {
                float pad = pipGlowR * g / 3f;
                uint glowCol = Boutique.U32(Boutique.WithAlpha(Boutique.GoldWarm, 0.22f * pipPulse / g));
                dl.AddRectFilled(pipCentre - new Vector2(pad, pad), pipCentre + new Vector2(pad, pad), glowCol);
            }
            dl.AddRectFilled(pipCentre - new Vector2(pipR, pipR), pipCentre + new Vector2(pipR, pipR),
                Boutique.U32(Boutique.Gold));

            // Meta text: bigger Oswald Med 13 with wide tracking for proper presence.
            float textX = min.X + padX + pipR * 2 + 12f * scale;
            using (Plugin.Instance?.OswaldMed13?.Push())
            {
                float trackPx = 2.6f * scale;
                float fontH = ImGui.GetFontSize();
                float textY = midY - fontH * 0.5f;
                Boutique.DrawTrackedText(dl, new Vector2(textX, textY),
                    "SETTINGS", Boutique.U32(Boutique.Text), trackPx);
                float settingsW = Boutique.MeasureTrackedText("SETTINGS", trackPx);
                Boutique.DrawTrackedText(dl,
                    new Vector2(textX + settingsW + 12f * scale, textY),
                    "//", Boutique.U32(Boutique.TextGhost), trackPx);
                float sepW = Boutique.MeasureTrackedText("//", trackPx);
                Boutique.DrawTrackedText(dl,
                    new Vector2(textX + settingsW + 12f * scale + sepW + 12f * scale, textY),
                    "CHARACTER SELECT+", Boutique.U32(Boutique.TextDim), trackPx);
            }

            // Close X at the top-right of the ribbon (where the native ImGui
            // title-bar X used to live, before NoTitleBar removed it). Custom
            // chrome owns the affordance now.
            float xSize = 22f * scale;
            var closeBoxMin = new Vector2(max.X - padX - xSize, midY - xSize * 0.5f);
            var closeBoxMax = closeBoxMin + new Vector2(xSize, xSize);
            ImGui.SetCursorScreenPos(closeBoxMin);
            bool clicked = ImGui.InvisibleButton("##boutique_settings_close_ribbon", new Vector2(xSize, xSize));
            bool hovered = ImGui.IsItemHovered();
            if (hovered) Boutique.Tooltip("Close");
            uint closeBg = hovered
                ? Boutique.U32(Boutique.WithAlpha(Boutique.Red, 0.12f))
                : Boutique.U32(new Vector4(0f, 0f, 0f, 0.30f));
            uint closeBorder = hovered
                ? Boutique.U32(Boutique.WithAlpha(Boutique.Red, 0.55f))
                : Boutique.U32(Boutique.BorderSoft);
            dl.AddRectFilled(closeBoxMin, closeBoxMax, closeBg);
            dl.AddRect(closeBoxMin, closeBoxMax, closeBorder, 0f, ImDrawFlags.None, 1f);

            // FontAwesome times glyph (). Escape literal so the Edit tool
            // round-trip never strips the high-codepoint character again.
            string xGlyph = "";
            ImGui.PushFont(UiBuilder.IconFont);
            var xNat = ImGui.CalcTextSize(xGlyph);
            ImGui.PopFont();
            float xRender = 12f * scale;
            float xRatio = xRender / UiBuilder.IconFont.FontSize;
            float xVisualW = xNat.X * xRatio;
            uint xCol = hovered ? Boutique.U32(Boutique.Red) : Boutique.U32(Boutique.TextFaint);
            // Centre using the rendered font size for the Y axis (CalcTextSize.Y
            // includes line-height padding which pushed the glyph slightly high).
            var xPos = new Vector2(
                closeBoxMin.X + (xSize - xVisualW) * 0.5f,
                closeBoxMin.Y + (xSize - xRender) * 0.5f);
            dl.AddText(UiBuilder.IconFont, xRender, xPos, xCol, xGlyph);
            if (clicked) plugin.IsSettingsOpen = false;
        }

        private void DrawBoutiqueSettingsHeader(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
        {
            // Header backdrop: subtle gold radial at the bottom + dark gradient
            uint top = Boutique.U32(new Vector4(0x0C / 255f, 0x0E / 255f, 0x14 / 255f, 1f));
            uint bot = Boutique.U32(Boutique.Bg);
            dl.AddRectFilledMultiColor(min, max, top, top, bot, bot);
            Boutique.DrawAuroraSpot(dl,
                new Vector2((min.X + max.X) * 0.5f, max.Y),
                240f * scale, 60f * scale,
                Boutique.WithAlpha(Boutique.Gold, 0.045f), 8);

            // No corner X here, the ribbon owns the close affordance now.

            // Bigger hero title: OswaldSemiMid (~22px source, baked ~33-34px)
            // with wide 0.32em tracking. Vertically centred in the upper half
            // of the taller header.
            float titleY = min.Y + 16f * scale;
            using (Plugin.Instance?.OswaldSemiMid?.Push())
            {
                float trackPx = 7f * scale; // ~0.32em at 22px source
                string title = "SETTINGS";
                float titleW = Boutique.MeasureTrackedText(title, trackPx);
                float titleX = (min.X + max.X) * 0.5f - titleW * 0.5f;
                Boutique.DrawTrackedText(dl,
                    new Vector2(titleX + 2f, titleY + 2f),
                    title, Boutique.U32(new Vector4(0f, 0f, 0f, 0.55f)), trackPx);
                Boutique.DrawTrackedText(dl, new Vector2(titleX, titleY),
                    title, Boutique.U32(Boutique.Text), trackPx);
            }

            // Bigger subtitle: OswaldSemi13 with 0.40em-equivalent tracking.
            // Centred horizontally below the title with a generous gap.
            using (Plugin.Instance?.OswaldSemi13?.Push())
            {
                float trackPx = 5f * scale;
                string a = $"{Categories.Length} SECTIONS";
                string sep = "  //  ";
                string b = "70+ CONTROLS";
                float aW = Boutique.MeasureTrackedText(a, trackPx);
                float sepW = Boutique.MeasureTrackedText(sep, trackPx);
                float bW = Boutique.MeasureTrackedText(b, trackPx);
                float totalW = aW + sepW + bW;
                float subX = (min.X + max.X) * 0.5f - totalW * 0.5f;
                float subY = titleY + 44f * scale;
                Boutique.DrawTrackedText(dl, new Vector2(subX, subY),
                    a, Boutique.U32(Boutique.GoldWarm), trackPx);
                Boutique.DrawTrackedText(dl, new Vector2(subX + aW, subY),
                    sep, Boutique.U32(Boutique.GoldDeep), trackPx);
                Boutique.DrawTrackedText(dl, new Vector2(subX + aW + sepW, subY),
                    b, Boutique.U32(Boutique.GoldWarm), trackPx);
            }

            // Header rule: short gold gradient hairline centred
            float ruleY = max.Y - 4f * scale;
            float ruleW = 220f * scale;
            float ruleX = (min.X + max.X) * 0.5f - ruleW * 0.5f;
            uint goldStrong = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.50f));
            uint goldClear = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0f));
            dl.AddRectFilledMultiColor(
                new Vector2(ruleX, ruleY),
                new Vector2(ruleX + ruleW * 0.5f, ruleY + 1f),
                goldClear, goldStrong, goldStrong, goldClear);
            dl.AddRectFilledMultiColor(
                new Vector2(ruleX + ruleW * 0.5f, ruleY),
                new Vector2(ruleX + ruleW, ruleY + 1f),
                goldStrong, goldClear, goldClear, goldStrong);
        }

        private void DrawBoutiqueBodyBackdrop(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
        {
            // Body fill follows the window-frame token (Surface0). The bottom
            // is darkened slightly to keep the velvet-fade feel of the mockup
            // while keeping the surface tinted by the user's window-frame
            // colour rather than the list-bg colour.
            Vector4 top = Boutique.Surface0;
            Vector4 bot = Boutique.Lerp(top, new Vector4(0f, 0f, 0f, top.W), 0.45f);
            dl.AddRectFilledMultiColor(min, max, Boutique.U32(top), Boutique.U32(top), Boutique.U32(bot), Boutique.U32(bot));
        }

        private void DrawWindowBrackets(float scale)
        {
            var wPos = ImGui.GetWindowPos();
            var wSize = ImGui.GetWindowSize();
            var dl = ImGui.GetWindowDrawList();
            float bsize = 14f * scale;
            float binset = 5f * scale;
            uint bcol = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.40f));
            // Bottom-left
            var bl = new Vector2(wPos.X + binset, wPos.Y + wSize.Y - binset);
            dl.AddLine(new Vector2(bl.X, bl.Y - bsize), bl, bcol, 1f);
            dl.AddLine(bl, new Vector2(bl.X + bsize, bl.Y), bcol, 1f);
            // Bottom-right
            var br = new Vector2(wPos.X + wSize.X - binset, wPos.Y + wSize.Y - binset);
            dl.AddLine(new Vector2(br.X, br.Y - bsize), br, bcol, 1f);
            dl.AddLine(br, new Vector2(br.X - bsize, br.Y), bcol, 1f);
        }

        // ── Nav rail ────────────────────────────────────────────────────────
        private void DrawBoutiqueNavRail(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
        {
            // Rail backdrop: very dark, slightly transparent over the velvet body
            uint railBg = Boutique.U32(new Vector4(0x08 / 255f, 0x0A / 255f, 0x0E / 255f, 0.55f));
            dl.AddRectFilled(min, max, railBg);

            // Right-edge gold accent hairline (fades top + bottom)
            uint goldFade = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.0f));
            uint goldMid = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.25f));
            float fadeH = (max.Y - min.Y) * 0.18f;
            dl.AddRectFilledMultiColor(
                new Vector2(max.X - 1f, min.Y),
                new Vector2(max.X, min.Y + fadeH),
                goldFade, goldFade, goldMid, goldMid);
            dl.AddRectFilled(
                new Vector2(max.X - 1f, min.Y + fadeH),
                new Vector2(max.X, max.Y - fadeH),
                goldMid);
            dl.AddRectFilledMultiColor(
                new Vector2(max.X - 1f, max.Y - fadeH),
                new Vector2(max.X, max.Y),
                goldMid, goldMid, goldFade, goldFade);

            // BorderSoft solid hairline behind the gold (matches mockup)
            dl.AddLine(new Vector2(max.X - 1f, min.Y), new Vector2(max.X - 1f, max.Y),
                Boutique.U32(Boutique.BorderSoft), 1f);

            // Layout
            float padTop = 20f * scale;
            float capH = 30f * scale;
            float itemH = 42f * scale;
            float capPadX = 18f * scale;

            // CATEGORIES cap: bumped to OswaldMed11 for legibility and changed
            // from TextGhost to GoldDeep so it actually reads. Mockup uses
            // TextGhost but at our scale it disappears against the velvet bg.
            using (Plugin.Instance?.OswaldMed11?.Push())
            {
                float trackPx = 4.6f * scale;
                Boutique.DrawTrackedText(dl,
                    new Vector2(min.X + capPadX, min.Y + padTop),
                    "CATEGORIES", Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.65f)), trackPx);
            }
            // Cap underline (gold-deep fading), pulled down to clear the bigger font
            float capUlY = min.Y + padTop + 18f * scale;
            uint goldDeepCol = Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.55f));
            uint goldDeepClear = Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0f));
            dl.AddRectFilledMultiColor(
                new Vector2(min.X + capPadX, capUlY),
                new Vector2(max.X - capPadX, capUlY + 1f),
                goldDeepCol, goldDeepClear, goldDeepClear, goldDeepCol);

            // Items
            // Items live in a scrollable child so all 14 categories stay
            // reachable even when the window is narrow / not full height.
            float listTop = min.Y + padTop + capH + 6f * scale;
            float listH = max.Y - listTop - 6f * scale;
            ImGui.SetCursorScreenPos(new Vector2(min.X, listTop));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
            // NoScrollbar hides the visible bar (which fought with the active
            // chevron tab on the right edge). Mouse wheel still scrolls.
            ImGui.BeginChild("##rail_items", new Vector2(max.X - min.X, listH),
                false, ImGuiWindowFlags.NoScrollbar);
            // CRITICAL: drawing has to use the CHILD'S draw list so items get
            // clipped to the child rect. Using the parent's `dl` made items
            // render past the rail viewport up into the header area.
            var railDl = ImGui.GetWindowDrawList();
            var listOrigin = ImGui.GetCursorScreenPos();
            for (int i = 0; i < Categories.Length; i++)
            {
                float itemY = listOrigin.Y + i * itemH;
                DrawNavRailItem(railDl, i,
                    new Vector2(listOrigin.X, itemY),
                    new Vector2(listOrigin.X + max.X - min.X, itemY + itemH),
                    scale);
            }
            // The InvisibleButtons inside DrawNavRailItem already advance the
            // cursor by itemH each, so the child's content size is correct.
            // A redundant Dummy here was reserving 2x the height and creating
            // the "infinite scroll" the user reported.
            ImGui.EndChild();
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
        }

        private void DrawNavRailItem(ImDrawListPtr dl, int index, Vector2 min, Vector2 max, float scale)
        {
            ref var cat = ref Categories[index];
            bool isActive = index == activeCategoryIndex;
            float midY = (min.Y + max.Y) * 0.5f;

            // Hit region
            ImGui.SetCursorScreenPos(min);
            bool clicked = ImGui.InvisibleButton($"##nav_{index}", max - min);
            bool hovered = ImGui.IsItemHovered();

            // Background: hover gold@4%, active gold gradient fade
            if (isActive)
            {
                uint a1 = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.10f));
                uint a2 = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.02f));
                uint a3 = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0f));
                // 0% to 80% width: a1 → a2; 80% to 100%: a2 → a3
                float w = max.X - min.X;
                float seam = min.X + w * 0.80f;
                dl.AddRectFilledMultiColor(min, new Vector2(seam, max.Y), a1, a2, a2, a1);
                dl.AddRectFilledMultiColor(
                    new Vector2(seam, min.Y), max, a2, a3, a3, a2);
            }
            else if (hovered)
            {
                dl.AddRectFilled(min, max, Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.04f)));
            }

            // Active accent: 3px gold left bar with glow
            if (isActive)
            {
                var barMin = new Vector2(min.X, min.Y + 8f * scale);
                var barMax = new Vector2(min.X + 3f * scale, max.Y - 8f * scale);
                // Glow halo (concentric layers)
                for (int g = 3; g >= 1; g--)
                {
                    float pad = g * 1.5f * scale;
                    var gMin = new Vector2(barMin.X - pad, barMin.Y - pad);
                    var gMax = new Vector2(barMax.X + pad, barMax.Y + pad);
                    uint gCol = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.10f / g));
                    dl.AddRectFilled(gMin, gMax, gCol);
                }
                dl.AddRectFilled(barMin, barMax, Boutique.U32(Boutique.GoldWarm));
            }

            // Icon glyph in the rainbow tint (a). Anchored at a fixed left
            // gutter so labels line up cleanly across rows regardless of glyph
            // metrics. Bigger size (16px) for more impact.
            float iconGutter = min.X + 24f * scale;
            float iconSize = 16f * scale;
            ImGui.PushFont(UiBuilder.IconFont);
            var iconNatural = ImGui.CalcTextSize(cat.icon);
            ImGui.PopFont();
            float iconRatio = iconSize / UiBuilder.IconFont.FontSize;
            var iconDrawn = iconNatural * iconRatio;
            var iconPos = new Vector2(iconGutter - iconDrawn.X * 0.5f,
                                      midY - iconDrawn.Y * 0.5f);
            Vector4 iconCol;
            if (isActive)        iconCol = cat.tint;
            else if (hovered)    iconCol = Boutique.WithAlpha(cat.tint, 0.85f);
            else                 iconCol = Boutique.WithAlpha(cat.tint, 0.55f);
            // Tiny halo behind the icon when active to lift it off the rail bg.
            if (isActive)
            {
                var hMin = iconPos - new Vector2(4f * scale, 4f * scale);
                var hMax = iconPos + iconDrawn + new Vector2(4f * scale, 4f * scale);
                dl.AddRectFilled(hMin, hMax, Boutique.U32(Boutique.WithAlpha(cat.tint, 0.10f)));
            }
            dl.AddText(UiBuilder.IconFont, iconSize, iconPos, Boutique.U32(iconCol), cat.icon);

            // Label (tracked-caps Oswald Med 11). Brighter inactive colour per
            // user feedback so non-selected categories don't feel "almost
            // impossible to read" at standard scale.
            float labelX = iconGutter + 18f * scale;
            using (Plugin.Instance?.OswaldMed11?.Push())
            {
                float trackPx = 2.6f * scale;
                float fontH = ImGui.GetFontSize();
                // Short label so long category names don't clip the rail.
                string label = (index < CategoryShortNames.Length)
                    ? CategoryShortNames[index]
                    : cat.name.ToUpperInvariant();
                Vector4 labelCol;
                if (isActive)      labelCol = Boutique.Text;
                else if (hovered)  labelCol = Boutique.GoldWarm;
                else               labelCol = Boutique.WithAlpha(Boutique.Text, 0.78f);
                Boutique.DrawTrackedText(dl, new Vector2(labelX, midY - fontH * 0.5f),
                    label, Boutique.U32(labelCol), trackPx);
            }

            // Active row chevron tab on the right edge
            if (isActive)
            {
                double now = ImGui.GetTime();
                float t = (float)Math.Clamp((now - categoryChangeStartT) / CategoryChangeDurationS, 0, 1);
                float eased = 1f - MathF.Pow(1f - t, 3f);
                float chevOffset = 6f * (1f - eased) * scale;
                float cs = 3.5f * scale;
                var cTip = new Vector2(max.X - cs * 1.6f - chevOffset, midY);
                var cTop = new Vector2(cTip.X + cs, cTip.Y - cs);
                var cBot = new Vector2(cTip.X + cs, cTip.Y + cs);
                dl.AddTriangleFilled(cTop, cBot, cTip,
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.55f * eased)));
            }

            if (clicked && index != activeCategoryIndex)
            {
                prevCategoryIndex = activeCategoryIndex;
                activeCategoryIndex = index;
                categoryChangeStartT = ImGui.GetTime();
            }
        }

        // ── Content area ────────────────────────────────────────────────────
        private void DrawBoutiqueContent(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
        {
            // Apply pendingExpandSection: translate the legacy "expand X" call
            // into a simple active-category switch.
            if (!string.IsNullOrEmpty(pendingExpandSection)
                && CategoryNameToIndex.TryGetValue(pendingExpandSection, out int targetIdx))
            {
                if (targetIdx != activeCategoryIndex)
                {
                    prevCategoryIndex = activeCategoryIndex;
                    activeCategoryIndex = targetIdx;
                    categoryChangeStartT = ImGui.GetTime();
                }
                pendingExpandSection = null;
            }

            // Index-bounds safety
            if (activeCategoryIndex < 0 || activeCategoryIndex >= Categories.Length)
                activeCategoryIndex = 0;

            ref var cat = ref Categories[activeCategoryIndex];

            // Section head (rainbow pip + kicker + title + rainbow accent).
            // Tighter padX so the content doesn't sit awkwardly far from the rail.
            float padX = 16f * scale;
            float padTop = 16f * scale;
            float headBlockH = 44f * scale;
            DrawSectionHead(dl,
                new Vector2(min.X + padX, min.Y + padTop),
                new Vector2(max.X - padX, min.Y + padTop + headBlockH),
                scale, activeCategoryIndex, cat.name, cat.tint);

            // Group-card chamfered slip wrapper around the content. Surface0
            // background, BorderSoft outline, 10px slip-polygon chamfer at TR
            // and BL, 2px gold-deep top accent. This gives the right side the
            // boutique container feel without rebuilding each section's body.
            float cardTop = min.Y + padTop + headBlockH + 12f * scale;
            float cardH = max.Y - cardTop - 14f * scale;
            if (cardH < 80f * scale) cardH = 80f * scale;
            var cardMin = new Vector2(min.X + padX, cardTop);
            var cardMax = new Vector2(max.X - padX, cardTop + cardH);
            DrawSectionGroupCard(dl, cardMin, cardMax, scale);

            // Scrollable content child INSIDE the slip card, tighter padding.
            float cardPad = 8f * scale;
            float contentTop = cardTop + cardPad + 2f * scale;
            float contentH = cardH - cardPad * 2 - 2f * scale;
            if (contentH < 60f * scale) contentH = 60f * scale;
            ImGui.SetCursorScreenPos(new Vector2(cardMin.X + cardPad, contentTop));

            ApplyBoutiqueLegacyWidgetStyles(scale);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Boutique.WithAlpha(Boutique.Gold, 0.25f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Boutique.WithAlpha(Boutique.Gold, 0.45f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Boutique.WithAlpha(Boutique.Gold, 0.65f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6f * scale, 2f * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f * scale, 4f * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 6f * scale);

            try
            {
                // Drop AlwaysVerticalScrollbar: only show when content actually
                // exceeds the child height. Saves the awkward empty bar.
                ImGui.BeginChild($"##settings_content_{activeCategoryIndex}",
                    new Vector2(cardMax.X - cardMin.X - cardPad * 2, contentH),
                    false, ImGuiWindowFlags.None);

                DrawActiveCategoryContent(scale);

                ImGui.EndChild();
            }
            finally
            {
                ImGui.PopStyleVar(4);
                ImGui.PopStyleColor(5);
                PopBoutiqueLegacyWidgetStyles();
            }
        }

        private void DrawSectionHead(ImDrawListPtr dl, Vector2 min, Vector2 max,
            float scale, int categoryIdx, string title, Vector4 tint)
        {
            // Single-row layout: rainbow pip + kicker number + big title.
            // Breadcrumb dropped (the title already says where you are).
            float titleBaseY = min.Y + 4f * scale;

            // Rainbow tint pip (4x4 square) at the very left, vertically aligned
            // with the title's optical centre. Acts as a quiet section colour
            // marker without competing with the gold.
            float pipSize = 4f * scale;
            float pipY = titleBaseY + 14f * scale;
            dl.AddRectFilled(
                new Vector2(min.X, pipY - pipSize * 0.5f),
                new Vector2(min.X + pipSize, pipY + pipSize * 0.5f),
                Boutique.U32(tint));

            // Kicker: "0X //" in tracked-caps gold-deep. Bigger Med13 so it
            // matches the visual weight of the title beside it.
            float kickerX = min.X + pipSize + 12f * scale;
            float kickerW;
            using (Plugin.Instance?.OswaldMed13?.Push())
            {
                float trackPx = 3.2f * scale;
                string kicker = $"{(categoryIdx + 1):D2}  //";
                kickerW = Boutique.MeasureTrackedText(kicker, trackPx);
                Boutique.DrawTrackedText(dl,
                    new Vector2(kickerX, titleBaseY + 4f * scale),
                    kicker, Boutique.U32(Boutique.GoldDeep), trackPx);
            }

            // Big tracked-caps title (Oswald SemiBold ~16px for impact).
            float titleX = kickerX + kickerW + 14f * scale;
            using (Plugin.Instance?.OswaldSemiSmall?.Push())
            {
                float trackPx = 5f * scale;
                string upper = title.ToUpperInvariant();
                Boutique.DrawTrackedText(dl,
                    new Vector2(titleX + 1.2f, titleBaseY + 1.2f),
                    upper, Boutique.U32(new Vector4(0f, 0f, 0f, 0.55f)), trackPx);
                Boutique.DrawTrackedText(dl, new Vector2(titleX, titleBaseY),
                    upper, Boutique.U32(Boutique.Text), trackPx);
            }

            // Bottom hairline (BorderSoft full width) + rainbow accent overlay
            // (option b): rainbow gradient fades from tint to transparent over
            // a fixed width. The rainbow "owns" the start of the underline; the
            // rest is the standard boutique hairline.
            float ruleY = max.Y - 8f * scale;
            dl.AddLine(new Vector2(min.X, ruleY), new Vector2(max.X, ruleY),
                Boutique.U32(Boutique.BorderSoft), 1f);
            float accentW = 140f * scale;
            uint tintFull = Boutique.U32(tint);
            uint tintClear = Boutique.U32(Boutique.WithAlpha(tint, 0f));
            dl.AddRectFilledMultiColor(
                new Vector2(min.X, ruleY),
                new Vector2(min.X + accentW, ruleY + 1f),
                tintFull, tintClear, tintClear, tintFull);
        }

        // Slip-polygon group card with a 2 px gold-deep top accent
        private void DrawSectionGroupCard(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
        {
            float chamfer = 10f * scale;
            CodexChassis.FillSlip(dl, min, max,
                chamfer,
                Boutique.U32(new Vector4(0x0E / 255f, 0x10 / 255f, 0x14 / 255f, 0.62f)));
            CodexChassis.StrokeSlip(dl, min, max, chamfer,
                Boutique.U32(Boutique.BorderSoft), 1f);
            // Top stripe stops short of the TR chamfer
            var stripeMin = new Vector2(min.X, min.Y);
            var stripeMax = new Vector2(max.X - chamfer, min.Y + 2f * scale);
            dl.AddRectFilled(stripeMin, stripeMax,
                Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.55f)));
        }

        private static Vector4 SlotOverrideOr(string key, Vector4 fallback)
        {
            var p = Plugin.Instance;
            if (p?.Configuration?.SelectedTheme == ThemeSelection.Custom &&
                p.Configuration.CustomTheme != null &&
                p.Configuration.CustomTheme.ColorOverrides.TryGetValue(key, out var packed) &&
                packed.HasValue)
            {
                return CustomThemeDefinitions.UnpackColor(packed.Value);
            }
            return fallback;
        }

        private void ApplyBoutiqueLegacyWidgetStyles(float scale)
        {
            // Each editor-exposed slot pulls the user's override first; if the
            // slot is unset, the boutique default ships in. This lets every
            // ImGui slot key in CustomThemeDefinitions reach the legacy widgets
            // rendered inside the boutique settings window without the chassis
            // silently overwriting them.
            ImGui.PushStyleColor(ImGuiCol.FrameBg, SlotOverrideOr("color.frameBg", Boutique.WithAlpha(Boutique.Surface0, 0.85f)));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, SlotOverrideOr("color.frameBgHovered", Boutique.WithAlpha(Boutique.Surface1, 0.95f)));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, SlotOverrideOr("color.frameBgActive", Boutique.WithAlpha(Boutique.Surface2, 0.95f)));
            ImGui.PushStyleColor(ImGuiCol.Header, Boutique.WithAlpha(Boutique.Surface1, 0.85f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Boutique.WithAlpha(Boutique.Gold, 0.10f));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, Boutique.WithAlpha(Boutique.Gold, 0.20f));
            ImGui.PushStyleColor(ImGuiCol.Button, SlotOverrideOr("color.button", Boutique.WithAlpha(Boutique.Surface1, 0.90f)));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, SlotOverrideOr("color.buttonHovered", Boutique.WithAlpha(Boutique.Gold, 0.15f)));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, SlotOverrideOr("color.buttonActive", Boutique.WithAlpha(Boutique.Gold, 0.25f)));
            ImGui.PushStyleColor(ImGuiCol.CheckMark, Boutique.GoldWarm);
            ImGui.PushStyleColor(ImGuiCol.SliderGrab, Boutique.GoldWarm);
            ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Boutique.Gold);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, SlotOverrideOr("color.popupBg", Boutique.RibbonBot));
            ImGui.PushStyleColor(ImGuiCol.Border, Boutique.BorderSoft);
            ImGui.PushStyleColor(ImGuiCol.Separator, SlotOverrideOr("color.separator", Boutique.BorderSoft));
            ImGui.PushStyleColor(ImGuiCol.Text, SlotOverrideOr("color.text", Boutique.Text));
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, SlotOverrideOr("color.textDisabled", Boutique.TextDim));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 0f);
        }

        private void PopBoutiqueLegacyWidgetStyles()
        {
            ImGui.PopStyleVar(4);
            ImGui.PopStyleColor(17);
        }

        // Dispatches to the existing per-category Draw method based on the
        // active rail selection. Keeps the legacy section contents working
        // unchanged; the boutique chrome wraps them.
        private void DrawActiveCategoryContent(float scale)
        {
            float contentWidth = ImGui.GetContentRegionAvail().X;
            float labelWidth = 140f * scale;
            float inputWidth = contentWidth - labelWidth - (20f * scale);

            switch (activeCategoryIndex)
            {
                case 0:  DrawVisualSettings(labelWidth, inputWidth); break;
                case 1:  DrawAutomationSettings(); break;
                case 2:  DrawBehaviorSettings(); break;
                case 3:  DrawAchievementSettings(); break;
                case 4:  DrawRandomGroupsSettings(); break;
                case 5:  DrawHonorificSettings(); break;
                case 6:  DrawMainCharacterSettings(labelWidth, inputWidth); break;
                case 7:  DrawCharacterAssignmentSettings(); break;
                case 8:  DrawJobAssignmentSettings(); break;
                case 9:  DrawDialogueSettings(); break;
                case 10: DrawNameSyncSettings(); break;
                case 11: DrawConflictResolutionSettings(); break;
                case 12: DrawBackupSettings(); break;
                case 13: DrawAccountAndDataSettings(); break;
            }
        }

        private void DrawVisualSettings(float labelWidth, float inputWidth)
        {
            // Legacy params unused in boutique layout (rows handle their own widths).
            _ = labelWidth; _ = inputWidth;
            float scale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);
            float sliderW = 220f * scale;
            float dropW = 200f * scale;

            Boutique.SettingRow("vis.uiScale", "UI Scale",
                "Scales the entire CS+ UI up or down. 1.0 is the default. Heads up: text can take a couple of seconds to adjust after changing.",
                sliderW, scale,
                () =>
                {
                    float v = float.IsNaN(_pendingUIScale) ? plugin.Configuration.UIScaleMultiplier : _pendingUIScale;
                    if (Boutique.SliderTrack("vis.uiScale", ref v, 0.7f, 1.5f, "%.2f", sliderW, scale))
                        _pendingUIScale = v;
                    if (ImGui.IsItemDeactivated() && !float.IsNaN(_pendingUIScale))
                    {
                        plugin.Configuration.UIScaleMultiplier = Math.Clamp(_pendingUIScale, 0.7f, 1.5f);
                        _pendingUIScale = float.NaN;
                        plugin.SaveConfiguration();
                        mainWindow.InvalidateLayout();
                    }
                });

            // Profile Image Scale
            Boutique.SettingRow("vis.profileScale", "Profile Image Scale",
                "Adjusts the size of character profile images in the grid.",
                sliderW, scale,
                () =>
                {
                    float v = plugin.ProfileImageScale;
                    if (Boutique.SliderTrack("vis.profileScale", ref v, 0.5f, 2.0f, "%.1f", sliderW, scale))
                    {
                        plugin.ProfileImageScale = v;
                        plugin.SaveConfiguration();
                        mainWindow.InvalidateLayout();
                    }
                });

            // Profiles Per Row (slider with int snapping)
            Boutique.SettingRow("vis.profilesPerRow", "Profiles Per Row",
                "Number of character profiles to display per row.",
                sliderW, scale,
                () =>
                {
                    int v = plugin.ProfileColumns;
                    if (Boutique.SliderTrackInt("vis.profilesPerRow", ref v, 1, 6, sliderW, scale))
                    {
                        plugin.ProfileColumns = Math.Clamp(v, 1, 6);
                        plugin.SaveConfiguration();
                        mainWindow.InvalidateLayout();
                    }
                });

            // Profile Spacing
            Boutique.SettingRow("vis.profileSpacing", "Profile Spacing",
                "Spacing between character profile cards.",
                sliderW, scale,
                () =>
                {
                    float v = plugin.ProfileSpacing;
                    if (Boutique.SliderTrack("vis.profileSpacing", ref v, 0f, 50f, "%.1f", sliderW, scale))
                    {
                        plugin.ProfileSpacing = v;
                        plugin.SaveConfiguration();
                        mainWindow.InvalidateLayout();
                    }
                });

            // Sort Characters By (Wardrobe-style sort pill, no typing)
            Boutique.SettingRow("vis.sort", "Sort Characters By",
                "Choose how characters are sorted in the main grid.",
                dropW, scale,
                () =>
                {
                    var sortLabels = new[] { "Favourites", "Alphabetical", "Most Recent", "Oldest", "Manual" };
                    var sortTypes = new[] { Plugin.SortType.Favorites, Plugin.SortType.Alphabetical,
                                            Plugin.SortType.Recent, Plugin.SortType.Oldest, Plugin.SortType.Manual };
                    var currentSort = (Plugin.SortType)plugin.Configuration.CurrentSortIndex;
                    int currentIdx = Array.IndexOf(sortTypes, currentSort);
                    int picked = Boutique.SortPill("vis.sort", "SORT", currentIdx, sortLabels, dropW, scale);
                    if (picked >= 0)
                    {
                        plugin.Configuration.CurrentSortIndex = (int)sortTypes[picked];
                        plugin.Configuration.Save();
                        mainWindow.UpdateSortType();
                    }
                });

            // Character Hover Effects (toggle pill)
            Boutique.SettingRow("vis.hoverFx", "Character Hover Effects",
                "Characters grow slightly when hovered over for visual feedback.",
                46f * scale, scale,
                () =>
                {
                    bool v = plugin.Configuration.EnableCharacterHoverEffects;
                    if (Boutique.TogglePill("vis.hoverFx", ref v, scale))
                    {
                        plugin.Configuration.EnableCharacterHoverEffects = v;
                        plugin.SaveConfiguration();
                    }
                });

            // Classic layout toggle. Reverts pre-rework rendering on most
            // windows. Themes still apply colour palette over top.
            Boutique.SettingRow("vis.classicLayout", "Classic Mode",
                "Reverts most CS+ windows to their pre-redesign look.",
                46f * scale, scale,
                () =>
                {
                    bool v = plugin.Configuration.UseClassicLayout;
                    if (Boutique.TogglePill("vis.classicLayout", ref v, scale))
                    {
                        plugin.Configuration.UseClassicLayout = v;
                        plugin.SaveConfiguration();
                        mainWindow.InvalidateLayout();
                    }
                });

            // Theme (Wardrobe-style sort pill). Built-ins + saved presets are
            // flattened into one option list; the picked index maps back to
            // either a ThemeSelection or a preset name.
            Boutique.SettingRow("vis.theme", "Theme",
                "Choose a built-in theme or load a saved Custom Theme preset.",
                dropW, scale,
                () =>
                {
                    var themes = Enum.GetValues<ThemeSelection>();
                    var presets = plugin.Configuration.ThemePresets;
                    var optionLabels = new List<string>();
                    foreach (var t in themes)
                    {
                        optionLabels.Add(t == ThemeSelection.Custom
                            ? "Custom (New)"
                            : SeasonalThemeManager.GetThemeSelectionDisplayName(t));
                    }
                    foreach (var p in presets)
                        optionLabels.Add($"Preset: {p.Name}");

                    var currentSelection = plugin.Configuration.SelectedTheme;
                    var activePresetName = plugin.Configuration.ActivePresetName;
                    int currentIdx;
                    if (currentSelection == ThemeSelection.Custom && !string.IsNullOrEmpty(activePresetName))
                    {
                        int pIdx = presets.FindIndex(p => p.Name == activePresetName);
                        currentIdx = pIdx >= 0 ? themes.Length + pIdx : Array.IndexOf(themes, ThemeSelection.Custom);
                    }
                    else
                    {
                        currentIdx = Array.IndexOf(themes, currentSelection);
                    }

                    int picked = Boutique.SortPill("vis.theme", "THEME", currentIdx, optionLabels, dropW, scale);
                    if (picked >= 0)
                    {
                        if (picked < themes.Length)
                        {
                            var theme = themes[picked];
                            plugin.Configuration.SelectedTheme = theme;
                            if (theme == ThemeSelection.Custom)
                            {
                                plugin.Configuration.ActivePresetName = null;
                                var customTheme = plugin.Configuration.CustomTheme;
                                customTheme.ColorOverrides.Clear();
                                customTheme.BackgroundImagePath = null;
                                customTheme.BackgroundImageOpacity = 0.3f;
                                customTheme.BackgroundImageZoom = 1.0f;
                                customTheme.BackgroundImageOffsetX = 0f;
                                customTheme.BackgroundImageOffsetY = 0f;
                                customTheme.FavoriteIconId = 0;
                                customTheme.UseNameplateColorForCardGlow = true;
                            }
                            plugin.Configuration.Save();
                            plugin.Configuration.UseSeasonalTheme = (theme == ThemeSelection.Current);
                            if (theme == ThemeSelection.Halloween || theme == ThemeSelection.Winter
                                || theme == ThemeSelection.Christmas || theme == ThemeSelection.Valentines)
                            {
                                plugin.AchievementTracker?.OnSeasonalThemeSet();
                                plugin.AchievementTracker?.OnSeasonalThemeUsed(theme.ToString());
                            }
                        }
                        else
                        {
                            // Saved preset
                            var preset = presets[picked - themes.Length];
                            plugin.Configuration.SelectedTheme = ThemeSelection.Custom;
                            plugin.Configuration.CustomTheme.CopyFrom(preset.Config);
                            plugin.Configuration.ActivePresetName = preset.Name;
                            plugin.Configuration.Save();
                            plugin.AchievementTracker?.OnCustomThemeSet();
                        }
                    }
                });

            // Show custom theme editor when Custom theme is selected
            if (plugin.Configuration.SelectedTheme == ThemeSelection.Custom)
            {
                ImGui.Dummy(new Vector2(0, 6f * scale));
                DrawCustomThemeEditor();
            }
        }

        private static string SortTypeLabel(Plugin.SortType s) => s switch
        {
            Plugin.SortType.Favorites => "Favourites",
            Plugin.SortType.Alphabetical => "Alphabetical",
            Plugin.SortType.Recent => "Most Recent",
            Plugin.SortType.Oldest => "Oldest",
            Plugin.SortType.Manual => "Manual",
            _ => s.ToString(),
        };
        private static Plugin.SortType LabelToSortType(string s) => s switch
        {
            "Favourites" => Plugin.SortType.Favorites,
            "Alphabetical" => Plugin.SortType.Alphabetical,
            "Most Recent" => Plugin.SortType.Recent,
            "Oldest" => Plugin.SortType.Oldest,
            "Manual" => Plugin.SortType.Manual,
            _ => Plugin.SortType.Favorites,
        };

        private void DrawThemePopup()
        {
            ImGui.PushStyleColor(ImGuiCol.PopupBg, Boutique.RibbonBot);
            ImGui.PushStyleColor(ImGuiCol.Border, Boutique.GoldDeep);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4f, 4f));
            if (ImGui.BeginPopup("##vis.themePopup"))
            {
                var currentSelection = plugin.Configuration.SelectedTheme;
                var activePresetName = plugin.Configuration.ActivePresetName;
                var presets = plugin.Configuration.ThemePresets;

                foreach (ThemeSelection theme in Enum.GetValues<ThemeSelection>())
                {
                    string themeDisplay = theme == ThemeSelection.Custom
                        ? "Custom (New)"
                        : SeasonalThemeManager.GetThemeSelectionDisplayName(theme);
                    bool isSelected = currentSelection == theme
                        && (theme != ThemeSelection.Custom || string.IsNullOrEmpty(activePresetName));
                    if (ImGui.Selectable(themeDisplay, isSelected))
                    {
                        plugin.Configuration.SelectedTheme = theme;
                        if (theme == ThemeSelection.Custom)
                        {
                            plugin.Configuration.ActivePresetName = null;
                            var customTheme = plugin.Configuration.CustomTheme;
                            customTheme.ColorOverrides.Clear();
                            customTheme.BackgroundImagePath = null;
                            customTheme.BackgroundImageOpacity = 0.3f;
                            customTheme.BackgroundImageZoom = 1.0f;
                            customTheme.BackgroundImageOffsetX = 0f;
                            customTheme.BackgroundImageOffsetY = 0f;
                            customTheme.FavoriteIconId = 0;
                            customTheme.UseNameplateColorForCardGlow = true;
                        }
                        plugin.Configuration.Save();
                        plugin.Configuration.UseSeasonalTheme = (theme == ThemeSelection.Current);
                        if (theme == ThemeSelection.Halloween || theme == ThemeSelection.Winter
                            || theme == ThemeSelection.Christmas || theme == ThemeSelection.Valentines)
                        {
                            plugin.AchievementTracker?.OnSeasonalThemeSet();
                            plugin.AchievementTracker?.OnSeasonalThemeUsed(theme.ToString());
                        }
                    }
                    if (ImGui.IsItemHovered() && theme == ThemeSelection.Current)
                    {
                        Boutique.Tooltip(SeasonalThemeManager.GetThemeSelectionDescription(theme));
                    }
                }

                if (presets.Count > 0)
                {
                    ImGui.Separator();
                    ImGui.PushStyleColor(ImGuiCol.Text, Boutique.TextDim);
                    ImGui.Text("Saved Presets:");
                    ImGui.PopStyleColor();
                    foreach (var preset in presets)
                    {
                        bool isPresetSelected = currentSelection == ThemeSelection.Custom
                            && preset.Name == activePresetName;
                        if (ImGui.Selectable($"  {preset.Name}", isPresetSelected))
                        {
                            plugin.Configuration.SelectedTheme = ThemeSelection.Custom;
                            plugin.Configuration.CustomTheme.CopyFrom(preset.Config);
                            plugin.Configuration.ActivePresetName = preset.Name;
                            plugin.Configuration.Save();
                            plugin.AchievementTracker?.OnCustomThemeSet();
                        }
                        if (ImGui.IsItemHovered())
                            Boutique.Tooltip($"Load preset: {preset.Name}");
                    }
                }
                ImGui.EndPopup();
            }
            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor(2);
        }

        private void DrawHonorificSettings()
        {
            float scale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);
            float toggleW = 42f * scale;

            // Setup note callout
            Boutique.Callout(Boutique.CalloutKind.Info, FontAwesomeIcon.InfoCircle,
                "Note",
                "Animated title glows (Wave, Pulse, Static) require the corresponding option to be enabled in Honorific's plugin settings as well.",
                scale);

            // Animated Gradients sub-group: support acknowledgement
            Boutique.Callout(Boutique.CalloutKind.Warning, FontAwesomeIcon.Heart,
                "Animated Gradients - by Caraxi",
                "The animated gradient feature (Wave, Pulse, Static) in Honorific titles was created by Caraxi. If you'd like to use these features, please consider supporting their work via Ko-Fi.",
                scale);

            // Ko-Fi support button
            ImGui.Dummy(new Vector2(0, 4f * scale));
            if (Boutique.OutlineButton("hon.kofi", "SUPPORT CARAXI ON KO-FI", scale))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://ko-fi.com/Caraxi",
                        UseShellExecute = true,
                    });
                }
                catch { }
            }
            if (ImGui.IsItemHovered())
                Boutique.Tooltip("Opens Caraxi's Ko-Fi page in your browser.");
            ImGui.Dummy(new Vector2(0, 8f * scale));

            // Supporter acknowledgement toggle
            Boutique.SettingRow("hon.acknowledged",
                "I have supported Caraxi",
                "Enables animated gradient features (Wave, Pulse, Static) in character Honorific titles. Honour-based system, please support the developer if you use these features.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.HasAcknowledgedHonorificSupport;
                    if (Boutique.TogglePill("hon.acknowledged", ref v, scale))
                    {
                        plugin.Configuration.HasAcknowledgedHonorificSupport = v;
                        plugin.Configuration.Save();
                    }
                });

            // Status callout below the toggle
            if (!plugin.Configuration.HasAcknowledgedHonorificSupport)
            {
                Boutique.Callout(Boutique.CalloutKind.Info, FontAwesomeIcon.Lock,
                    "Animated gradients disabled",
                    "Enable the toggle above to use Wave, Pulse, and Static title animations.",
                    scale);
            }
            else
            {
                Boutique.Callout(Boutique.CalloutKind.Info, FontAwesomeIcon.Check,
                    "Animated gradients enabled",
                    "Thank you for supporting Caraxi!",
                    scale);
            }
        }

        private void DrawAutomationSettings()
        {
            float scale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);
            float toggleW = 42f * scale;

            // Warning callout (Glamourer 'None' automation requirement)
            Boutique.Callout(Boutique.CalloutKind.Warning, FontAwesomeIcon.ExclamationTriangle,
                "Glamourer setup required",
                "Requires a 'None' automation defined in Glamourer for characters that don't have an automation set. You must also bind your in-game character name to 'Any World' and 'Set to Character' in Glamourer.",
                scale);

            // Enable toggle
            Boutique.SettingRow("auto.enable", "Enable Automations",
                "When enabled, you'll be able to assign a Glamourer Automation to each character and design.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.EnableAutomations;
                    if (Boutique.TogglePill("auto.enable", ref v, scale))
                    {
                        plugin.Configuration.EnableAutomations = v;
                        UpdateAutomationSettings(v);
                    }
                });
        }

        private void DrawBehaviorSettings()
        {
            float scale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);
            float toggleW = 42f * scale;

            // ── General behaviour toggles ──
            Boutique.SettingRow("beh.updateNotif", "Notify of updates in chat",
                "Shows a chat message when a new CS+ version is available on GitHub. Checked every 30 minutes, notifies once per session.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.ShowUpdateNotification;
                    if (Boutique.TogglePill("beh.updateNotif", ref v, scale))
                    {
                        plugin.Configuration.ShowUpdateNotification = v;
                        plugin.Configuration.Save();
                    }
                });

            Boutique.SettingRow("beh.rememberWindow", "Remember Main Window state on startup",
                "When enabled, the Main Window will automatically open on startup if it was open when you last closed the game or disabled the plugin.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.RememberMainWindowState;
                    if (Boutique.TogglePill("beh.rememberWindow", ref v, scale))
                    {
                        plugin.Configuration.RememberMainWindowState = v;
                        plugin.Configuration.Save();
                    }
                });

            Boutique.SettingRow("beh.qsCompact", "Compact Quick Switch Bar",
                "When enabled, the Quick Switch window will hide its title bar and frame, showing only the dropdowns and apply button.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.QuickSwitchCompact;
                    if (Boutique.TogglePill("beh.qsCompact", ref v, scale))
                    {
                        plugin.Configuration.QuickSwitchCompact = v;
                        plugin.Configuration.Save();
                    }
                });

            Boutique.SettingRow("beh.qsIgnoreEscape", "Quick Switch ignores Escape key",
                "When enabled, pressing Escape won't close the Quick Switch window. This also prevents Quick Switch from stealing focus when opened.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.QuickSwitchIgnoreEscape;
                    if (Boutique.TogglePill("beh.qsIgnoreEscape", ref v, scale))
                    {
                        plugin.Configuration.QuickSwitchIgnoreEscape = v;
                        plugin.Configuration.Save();
                    }
                });

            Boutique.SettingRow("beh.autoLastChar", "Auto-Apply Last Used Character on Login",
                "Automatically applies the last character you used when logging into the game.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.EnableLastUsedCharacterAutoload;
                    if (Boutique.TogglePill("beh.autoLastChar", ref v, scale))
                    {
                        plugin.Configuration.EnableLastUsedCharacterAutoload = v;
                        plugin.Configuration.Save();
                    }
                });

            // Nested toggle: only shows when parent is enabled. The
            // subOption flag draws an L-shaped tether from the indent edge
            // to this row's pip so the relationship reads at a glance.
            if (plugin.Configuration.EnableLastUsedCharacterAutoload)
            {
                ImGui.Indent(20f * scale);
                Boutique.SettingRow("beh.autoLastDesign", "Also Apply Last Used Design",
                    "Also applies the last design you used for that character when logging in. Requires the toggle above to be enabled.",
                    toggleW, scale,
                    () =>
                    {
                        bool v = plugin.Configuration.EnableLastUsedDesignAutoload;
                        if (Boutique.TogglePill("beh.autoLastDesign", ref v, scale))
                        {
                            plugin.Configuration.EnableLastUsedDesignAutoload = v;
                            plugin.Configuration.Save();
                        }
                    },
                    subOption: true);
                ImGui.Unindent(20f * scale);
            }

            Boutique.SettingRow("beh.applyIdle", "Apply idle pose on login",
                "Automatically applies your idle pose after logging in. Disable if you're seeing pose bugs.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.ApplyIdleOnLogin;
                    if (Boutique.TogglePill("beh.applyIdle", ref v, scale))
                    {
                        plugin.Configuration.ApplyIdleOnLogin = v;
                        plugin.Configuration.Save();
                    }
                });

            Boutique.SettingRow("beh.reapplyDesign", "Reapply last design on job change",
                "When enabled, CS+ reapplies the last used design when you switch jobs.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.ReapplyDesignOnJobChange;
                    if (Boutique.TogglePill("beh.reapplyDesign", ref v, scale))
                    {
                        plugin.Configuration.ReapplyDesignOnJobChange = v;
                        plugin.Configuration.Save();
                    }
                });

            Boutique.SettingRow("beh.randomFavOnly", "Random Selection: Favourites Only",
                "When enabled, random selection only picks from favourited characters and designs. Requires at least one favourite to work.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.RandomSelectionFavoritesOnly;
                    if (Boutique.TogglePill("beh.randomFavOnly", ref v, scale))
                    {
                        plugin.Configuration.RandomSelectionFavoritesOnly = v;
                        plugin.Configuration.Save();
                    }
                });

            Boutique.SettingRow("beh.randomChat", "Show Random Selection Chat Messages",
                "Displays themed chat messages when using random selection. Messages become spooky during Halloween season.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.ShowRandomSelectionChatMessages;
                    if (Boutique.TogglePill("beh.randomChat", ref v, scale))
                    {
                        plugin.Configuration.ShowRandomSelectionChatMessages = v;
                        plugin.Configuration.Save();
                    }
                });

            Boutique.SettingRow("beh.imguiFilePicker", "Use In-Game File Browser",
                "Use an in-game file browser instead of the Windows file dialog. Recommended for Linux/Wine users or if you prefer not to leave the game window.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.UseImGuiFilePicker;
                    if (Boutique.TogglePill("beh.imguiFilePicker", ref v, scale))
                    {
                        plugin.Configuration.UseImGuiFilePicker = v;
                        plugin.Configuration.Save();
                    }
                });

            // ── Context Menu Options ──
            Boutique.SubSectionHeader("CONTEXT MENU OPTIONS", null, scale);

            Boutique.SettingRow("beh.ctxViewRP", "Show 'View RP Profile' in context menu",
                "Right-clicking players shows a 'View RP Profile' option, letting you view other CS+ users' RP profiles.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.ShowViewRPContextMenu;
                    if (Boutique.TogglePill("beh.ctxViewRP", ref v, scale))
                    {
                        plugin.Configuration.ShowViewRPContextMenu = v;
                        plugin.Configuration.Save();
                    }
                });

            Boutique.SettingRow("beh.ctxBlock", "Show 'Block CS+ User' in context menu",
                "Right-clicking CS+ users shows a 'Block CS+ User' option. Blocked users' CS+ names won't be displayed to you.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.ShowBlockUserContextMenu;
                    if (Boutique.TogglePill("beh.ctxBlock", ref v, scale))
                    {
                        plugin.Configuration.ShowBlockUserContextMenu = v;
                        plugin.Configuration.Save();
                    }
                });

            Boutique.SettingRow("beh.ctxReport", "Show 'Report CS+ Name' in context menu",
                "Right-clicking CS+ users shows a 'Report CS+ Name' option. Use this to report offensive CS+ names to moderators.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.ShowReportUserContextMenu;
                    if (Boutique.TogglePill("beh.ctxReport", ref v, scale))
                    {
                        plugin.Configuration.ShowReportUserContextMenu = v;
                        plugin.Configuration.Save();
                    }
                });

            // ── Blocked Users ──
            Boutique.SubSectionHeader($"BLOCKED USERS ({plugin.Configuration.BlockedCSUsers.Count})", null, scale);

            if (plugin.Configuration.BlockedCSUsers.Count > 0)
            {
                var blockedList = plugin.Configuration.BlockedCSUsers.ToList();
                float listH = Math.Min(150f, blockedList.Count * 26f + 12f) * scale;
                ImGui.PushStyleColor(ImGuiCol.ChildBg, Boutique.WithAlpha(Boutique.Velvet, 0.85f));
                ImGui.PushStyleColor(ImGuiCol.Border, Boutique.BorderSoft);
                ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
                ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0f);
                ImGui.BeginChild("##BlockedUsersList", new Vector2(-1, listH), true);
                for (int i = 0; i < blockedList.Count; i++)
                {
                    var name = blockedList[i];
                    bool selected = selectedBlockedUserIndex == i;
                    if (ImGui.Selectable($"{name}##blocked_{i}", selected))
                        selectedBlockedUserIndex = selected ? -1 : i;
                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(0))
                    {
                        plugin.Configuration.BlockedCSUsers.Remove(name);
                        plugin.Configuration.Save();
                        selectedBlockedUserIndex = -1;
                    }
                }
                ImGui.EndChild();
                ImGui.PopStyleVar(2);
                ImGui.PopStyleColor(2);

                if (selectedBlockedUserIndex >= 0 && selectedBlockedUserIndex < blockedList.Count)
                {
                    ImGui.Dummy(new Vector2(0, 4f * scale));
                    if (Boutique.OutlineButton("beh.unblockSelected", "UNBLOCK SELECTED", scale))
                    {
                        plugin.Configuration.BlockedCSUsers.Remove(blockedList[selectedBlockedUserIndex]);
                        plugin.Configuration.Save();
                        selectedBlockedUserIndex = -1;
                    }
                    if (ImGui.IsItemHovered())
                        Boutique.Tooltip("Remove the selected user from your block list. You can also double-click a user to unblock them.");
                }
            }
            else
            {
                using (Plugin.Instance?.OutfitMed12?.Push())
                {
                    ImGui.TextColored(Boutique.TextFaint, "No blocked users.");
                }
            }

            ImGui.Dummy(new Vector2(0, 6f * scale));
        }

        private static readonly string[] ToastPositionLabels =
        {
            "Bottom Right",
            "Bottom Left",
            "Top Right",
            "Top Left",
            "Top Center",
            "Bottom Center",
        };

        private void DrawAchievementSettings()
        {
            float scale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);
            float toggleW = 46f * scale;

            // Master toggle
            Boutique.SettingRow("ach.master", "Enable Achievement system",
                "Master toggle for the achievement system. When off, the trophy button, toasts, and chat messages are hidden. Your progress is preserved.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.EnableAchievementSystem;
                    if (Boutique.TogglePill("ach.master", ref v, scale))
                    {
                        plugin.Configuration.EnableAchievementSystem = v;
                        plugin.Configuration.Save();
                    }
                });

            bool achGated = !plugin.Configuration.EnableAchievementSystem;
            if (achGated) ImGui.BeginDisabled();

            // Toast notifications
            Boutique.SettingRow("ach.toasts", "Show achievement toast notifications",
                "Slide-in toast when an achievement unlocks. Click to dismiss; up to 3 stack at once.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.ShowAchievementNotifications;
                    if (Boutique.TogglePill("ach.toasts", ref v, scale))
                    {
                        plugin.Configuration.ShowAchievementNotifications = v;
                        plugin.Configuration.Save();
                    }
                });

            // Toast position: dropdown only on this row. The TEST preview
            // button gets its own indented sub-row below so the description
            // here has full breathing room and the dropdown / button pairing
            // doesn't crash into each other on the same line.
            bool toastGated = !plugin.Configuration.ShowAchievementNotifications;
            if (toastGated && !achGated) ImGui.BeginDisabled();
            float positionW = 160f * scale;
            Boutique.SettingRow("ach.toastPos", "Toast position",
                "Where the achievement toast appears on screen. Pick a corner that doesn't conflict with your in-game UI.",
                positionW, scale,
                () =>
                {
                    int posIdx = (int)plugin.Configuration.AchievementToastPosition;
                    int picked = Boutique.SortPill("ach.toastPos", "AT", posIdx,
                        ToastPositionLabels, positionW, scale);
                    if (picked >= 0)
                    {
                        plugin.Configuration.AchievementToastPosition = (Configuration.ToastPosition)picked;
                        plugin.Configuration.Save();
                    }
                });

            // Test sub-row: tethered under Toast position via subOption flag.
            ImGui.Indent(20f * scale);
            Boutique.SettingRow("ach.testToast", "Preview toast",
                "Fires a sample achievement toast at the selected position so you can confirm placement.",
                70f * scale, scale,
                () =>
                {
                    if (Boutique.OutlineButton("ach.testToastBtn", "TEST", scale))
                        plugin.RunAchievementToastTest();
                },
                subOption: true);
            ImGui.Unindent(20f * scale);
            if (toastGated && !achGated) ImGui.EndDisabled();

            // Chat toggle
            Boutique.SettingRow("ach.chat", "Show achievement messages in chat",
                "Prints a chat line when an achievement unlocks.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.ShowAchievementChatMessages;
                    if (Boutique.TogglePill("ach.chat", ref v, scale))
                    {
                        plugin.Configuration.ShowAchievementChatMessages = v;
                        plugin.Configuration.Save();
                    }
                });

            if (achGated) ImGui.EndDisabled();
        }

        private void DrawRandomGroupsSettings()
        {
            float scale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);
            float availW = ImGui.GetContentRegionAvail().X;
            var dl = ImGui.GetWindowDrawList();
            double time = ImGui.GetTime();

            // ── Workbench (create form) ──────────────────────────────────
            // Help line above, input + gold pill below. Anchored at top.
            using (Plugin.Instance?.OutfitMed12?.Push())
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Boutique.TextDim);
                ImGui.TextUnformatted("Create groups for ");
                ImGui.SameLine(0, 0);
                ImGui.PushStyleColor(ImGuiCol.Text, Boutique.GoldWarm);
                ImGui.TextUnformatted("/select random <name>");
                ImGui.PopStyleColor();
                ImGui.SameLine(0, 0);
                ImGui.TextUnformatted(".");
                ImGui.PopStyleColor();
            }
            ImGui.Dummy(new Vector2(0, 6f * scale));

            // Input + Create gold pill on the same row, both 28px tall.
            float pillTrack = 1.6f * scale;
            var pillSize = Boutique.DrawGoldPillSize("CREATE", pillTrack, scale);
            pillSize.X = MathF.Max(pillSize.X, 90f * scale);
            float gap = 8f * scale;
            float inputW = availW - pillSize.X - gap;
            if (inputW < 120f * scale) inputW = 120f * scale;

            var workbenchOrigin = ImGui.GetCursorScreenPos();
            // Boutique text input (auto-styled).
            bool entered = Boutique.DrawBoutiqueTextInput("##rg_newGroupName",
                ref newRandomGroupName, 50, inputW, "Group name...",
                ImGuiInputTextFlags.EnterReturnsTrue);

            // Pill anchored to the right of the input on the same row.
            bool canCreate = !string.IsNullOrWhiteSpace(newRandomGroupName) &&
                !plugin.Configuration.RandomGroups.Any(g => g.Name.Equals(newRandomGroupName.Trim(), StringComparison.OrdinalIgnoreCase));

            float pillX = workbenchOrigin.X + inputW + gap;
            float pillY = workbenchOrigin.Y;
            var pillMin = new Vector2(pillX, pillY);
            var pillMax = pillMin + pillSize;
            ImGui.SetCursorScreenPos(pillMin);
            bool pillClicked = ImGui.InvisibleButton("##rg_createPill", pillSize);
            bool pillHovered = ImGui.IsItemHovered() && canCreate;
            if (canCreate)
            {
                Boutique.DrawGoldPill(dl, pillMin, pillMax, "CREATE", pillTrack, scale, pillHovered, showPlus: true);
            }
            else
            {
                // Disabled: faded gold + ghost text
                Boutique.FillSlip(dl, pillMin, pillMax, Boutique.ChamSm * scale,
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.18f)));
                using (Plugin.Instance?.OswaldSemi11?.Push())
                {
                    float labelW = Boutique.MeasureTrackedText("CREATE", pillTrack);
                    var inkPos = new Vector2(
                        pillMin.X + (pillSize.X - labelW) * 0.5f,
                        pillMin.Y + (pillSize.Y - ImGui.GetFontSize()) * 0.5f);
                    Boutique.DrawTrackedText(dl, inkPos, "CREATE",
                        Boutique.U32(Boutique.TextFaint), pillTrack);
                }
            }
            if ((entered || pillClicked) && canCreate)
            {
                plugin.AchievementTracker?.OnRandomGroupCreated();
                plugin.Configuration.RandomGroups.Add(new Configuration.RandomGroup
                {
                    Name = newRandomGroupName.Trim()
                });
                plugin.Configuration.Save();
                newRandomGroupName = "";
            }

            // Position cursor below the workbench row so subsequent items flow.
            ImGui.SetCursorScreenPos(new Vector2(workbenchOrigin.X, workbenchOrigin.Y + pillSize.Y + 4f * scale));
            ImGui.Dummy(new Vector2(0, 14f * scale));

            // ── Group list ───────────────────────────────────────────────
            if (plugin.Configuration.RandomGroups.Count == 0)
            {
                using (Plugin.Instance?.OutfitMed12?.Push())
                    ImGui.TextColored(Boutique.TextFaint, "No groups yet, type a name above.");
                ImGui.Dummy(new Vector2(0, 6f * scale));
                return;
            }

            int groupToDelete = -1;
            for (int i = 0; i < plugin.Configuration.RandomGroups.Count; i++)
            {
                var group = plugin.Configuration.RandomGroups[i];
                bool isExpanded = expandedRandomGroups.Contains(i);
                int charCount = group.CharacterNames.Count;
                bool toggleExpand;
                bool delete;
                DrawRandomGroupCard(dl, group, charCount, isExpanded, scale, time, i,
                    out toggleExpand, out delete);
                if (toggleExpand)
                {
                    if (isExpanded) expandedRandomGroups.Remove(i);
                    else expandedRandomGroups.Add(i);
                }
                if (delete) groupToDelete = i;
            }

            if (groupToDelete >= 0)
            {
                plugin.Configuration.RandomGroups.RemoveAt(groupToDelete);
                plugin.Configuration.Save();
                expandedRandomGroups.Remove(groupToDelete);
                var newExpanded = new HashSet<int>();
                foreach (var idx in expandedRandomGroups)
                    newExpanded.Add(idx > groupToDelete ? idx - 1 : idx);
                expandedRandomGroups = newExpanded;
            }

            ImGui.Dummy(new Vector2(0, 6f * scale));
        }

        // Custom-painted Random Group artefact card.
        //   8 px TR+BL chamfer slip, surface1@0.6 fill, BorderSoft outline,
        //   2 px gold-deep top hairline (respects chamfer).
        //   Header row: diamond + name + count + command chip + chevron + X.
        //   Expanded body: velvet@0.4 bg, 2-column character chips with 2 px
        //   left bar membership signal.
        private void DrawRandomGroupCard(ImDrawListPtr dl, Configuration.RandomGroup group,
            int charCount, bool isExpanded, float scale, double time, int idx,
            out bool toggleExpand, out bool delete)
        {
            toggleExpand = false;
            delete = false;

            float availW = ImGui.GetContentRegionAvail().X;
            var origin = ImGui.GetCursorScreenPos();
            float headerH = 40f * scale;
            float chamfer = 8f * scale;

            // ── Compute body height (only when expanded) ──
            float bodyH = 0f;
            var characters = plugin.Configuration.Characters;
            int chipRows = 0;
            float chipH = 28f * scale;
            float chipColGap = 8f * scale;
            float chipRowGap = 6f * scale;
            float bodyPadX = 14f * scale;
            float bodyPadTop = 12f * scale;
            float bodyPadBot = 12f * scale;
            float chipColW = (availW - bodyPadX * 2 - chipColGap) * 0.5f;

            if (isExpanded)
            {
                if (characters.Count == 0)
                {
                    bodyH = bodyPadTop + 18f * scale + bodyPadBot;
                }
                else
                {
                    chipRows = (characters.Count + 1) / 2;
                    bodyH = bodyPadTop + chipRows * chipH + (chipRows - 1) * chipRowGap + bodyPadBot;
                }
            }

            float totalH = headerH + bodyH;
            var min = origin;
            var max = origin + new Vector2(availW, totalH);

            // ── Card silhouette ──
            Boutique.FillSlip(dl, min, max, chamfer,
                Boutique.U32(Boutique.WithAlpha(Boutique.Surface1, 0.60f)));

            // 1 px BorderSoft outline (slip polygon)
            Span<Vector2> pts = stackalloc Vector2[6];
            Boutique.BuildSlipPolygon(min, max, chamfer, pts);
            for (int s = 0; s < 6; s++) dl.PathLineTo(pts[s]);
            dl.PathStroke(Boutique.U32(Boutique.BorderSoft), ImDrawFlags.Closed, 1f * scale);

            // 2 px gold-deep top hairline (stops 8 px before TR chamfer)
            dl.AddRectFilled(
                new Vector2(min.X, min.Y),
                new Vector2(max.X - chamfer, min.Y + 2f * scale),
                Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.55f)));

            // ── Header hit area (whole row except the X) ──
            // X eat-rect on the right; place its hit BEFORE the header so X
            // wins click priority when overlapping.
            float headerPadX = 12f * scale;
            float xSize = 16f * scale;
            float chevSize = 14f * scale;
            float xX = max.X - headerPadX - xSize;
            float chevX = xX - chevSize - 8f * scale;

            ImGui.SetCursorScreenPos(new Vector2(xX, min.Y + (headerH - xSize) * 0.5f));
            bool xClicked = ImGui.InvisibleButton($"##rg.x_{idx}", new Vector2(xSize, xSize));
            bool xHovered = ImGui.IsItemHovered();
            if (xHovered) Boutique.Tooltip($"Delete {group.Name}");

            // Header row click (covers everything LEFT of the X hit-rect)
            ImGui.SetCursorScreenPos(min);
            bool headerClicked = ImGui.InvisibleButton($"##rg.head_{idx}",
                new Vector2(xX - min.X, headerH));
            bool headerHovered = ImGui.IsItemHovered();
            // If the X is hovered, swallow header hover so we don't double-paint
            if (xHovered) headerHovered = false;

            if (headerHovered)
            {
                // Subtle hover wash on the header rect
                dl.AddRectFilled(min, new Vector2(max.X, min.Y + headerH),
                    Boutique.U32(new Vector4(1f, 1f, 1f, 0.025f)));
            }

            // ── Header content ──
            float midY = min.Y + headerH * 0.5f;

            // Diamond glyph (small, gold-deep), 6×6 rotated square via 4 triangles
            float dSize = 5f * scale;
            var dCentre = new Vector2(min.X + headerPadX + dSize * 0.5f, midY);
            uint diamondCol = Boutique.U32(Boutique.GoldDeep);
            dl.AddQuadFilled(
                new Vector2(dCentre.X, dCentre.Y - dSize),
                new Vector2(dCentre.X + dSize, dCentre.Y),
                new Vector2(dCentre.X, dCentre.Y + dSize),
                new Vector2(dCentre.X - dSize, dCentre.Y),
                diamondCol);

            // Group name (Oswald Med 13, gold-warm)
            float nameX = dCentre.X + dSize + 8f * scale;
            float nameY = midY;
            float nameW;
            using (Plugin.Instance?.OswaldMed13?.Push())
            {
                var nameSz = ImGui.CalcTextSize(group.Name);
                nameY = midY - nameSz.Y * 0.5f;
                nameW = nameSz.X;
                dl.AddText(new Vector2(nameX, nameY),
                    Boutique.U32(Boutique.GoldWarm), group.Name);
            }

            // Member count (Outfit Body 11, TextDim), "(N)"
            float countX = nameX + nameW + 6f * scale;
            using (Plugin.Instance?.OutfitBody12?.Push())
            {
                string countStr = $"({charCount})";
                var countSz = ImGui.CalcTextSize(countStr);
                dl.AddText(new Vector2(countX, midY - countSz.Y * 0.5f),
                    Boutique.U32(Boutique.TextDim), countStr);
            }

            // Command chip, right-anchored, 4 px chamfer, monospace
            string commandStr = $"/select random {group.Name.ToLower().Replace(" ", "")}";
            float chipPadX = 8f * scale;
            float chipH_cmd = 22f * scale;
            float chipChamfer = 3f * scale;
            float cmdMaxW = chevX - 12f * scale - countX - 24f * scale;
            using (Plugin.Instance?.OutfitBody12?.Push())
            {
                // Truncate with ellipsis if too wide
                string display = commandStr;
                var sz = ImGui.CalcTextSize(display);
                if (sz.X + chipPadX * 2 > cmdMaxW)
                {
                    string ell = "...";
                    float ellW = ImGui.CalcTextSize(ell).X;
                    while (display.Length > 1 &&
                        ImGui.CalcTextSize(display).X + ellW + chipPadX * 2 > cmdMaxW)
                    {
                        display = display.Substring(0, display.Length - 1);
                    }
                    display += ell;
                    sz = ImGui.CalcTextSize(display);
                }
                float chipW = sz.X + chipPadX * 2;
                float chipMaxX = chevX - 8f * scale;
                float chipMinX = chipMaxX - chipW;
                if (chipMinX < countX + 12f * scale) chipMinX = countX + 12f * scale;
                var chipMin = new Vector2(chipMinX, midY - chipH_cmd * 0.5f);
                var chipMax = new Vector2(chipMaxX, midY + chipH_cmd * 0.5f);
                Boutique.FillSlip(dl, chipMin, chipMax, chipChamfer,
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.08f)));
                Span<Vector2> chipPts = stackalloc Vector2[6];
                Boutique.BuildSlipPolygon(chipMin, chipMax, chipChamfer, chipPts);
                for (int s = 0; s < 6; s++) dl.PathLineTo(chipPts[s]);
                dl.PathStroke(Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.45f)),
                    ImDrawFlags.Closed, 1f * scale);
                var cmdInkPos = new Vector2(
                    chipMin.X + (chipMax.X - chipMin.X - sz.X) * 0.5f,
                    chipMin.Y + (chipH_cmd - sz.Y) * 0.5f);
                dl.AddText(cmdInkPos, Boutique.U32(Boutique.GoldWarm), display);
            }

            // Chevron (right of command chip, before X)
            string chevGlyph = isExpanded ? "" : "";
            FontAwesomeIcon chevIcon = isExpanded
                ? FontAwesomeIcon.ChevronDown
                : FontAwesomeIcon.ChevronRight;
            chevGlyph = chevIcon.ToIconString();
            uint chevCol = Boutique.U32(headerHovered ? Boutique.GoldWarm : Boutique.TextGhost);
            ImGui.PushFont(UiBuilder.IconFont);
            var chevSz = ImGui.CalcTextSize(chevGlyph);
            ImGui.PopFont();
            float chevPx = 12f * scale;
            float chevRatio = chevPx / UiBuilder.IconFont.FontSize;
            var chevPos = new Vector2(
                chevX + (chevSize - chevSz.X * chevRatio) * 0.5f,
                midY - chevSz.Y * chevRatio * 0.5f);
            dl.AddText(UiBuilder.IconFont, chevPx, chevPos, chevCol, chevGlyph);

            // Delete X
            string xGlyph = FontAwesomeIcon.Times.ToIconString();
            uint xCol = Boutique.U32(xHovered ? Boutique.Red : Boutique.TextGhost);
            ImGui.PushFont(UiBuilder.IconFont);
            var xSz = ImGui.CalcTextSize(xGlyph);
            ImGui.PopFont();
            float xPx = 12f * scale;
            float xRatio = xPx / UiBuilder.IconFont.FontSize;
            var xPos = new Vector2(
                xX + (xSize - xSz.X * xRatio) * 0.5f,
                midY - xSz.Y * xRatio * 0.5f);
            dl.AddText(UiBuilder.IconFont, xPx, xPos, xCol, xGlyph);

            if (headerClicked) toggleExpand = true;
            if (xClicked) delete = true;

            // ── Expanded body ──
            if (isExpanded)
            {
                var bodyMin = new Vector2(min.X, min.Y + headerH);
                var bodyMax = new Vector2(max.X, max.Y);
                // Velvet wash
                dl.AddRectFilled(bodyMin, bodyMax,
                    Boutique.U32(Boutique.WithAlpha(Boutique.Velvet, 0.40f)));
                // Top hairline gold-deep@22%
                dl.AddRectFilled(
                    new Vector2(bodyMin.X + 12f * scale, bodyMin.Y),
                    new Vector2(bodyMax.X - 12f * scale, bodyMin.Y + 1f * scale),
                    Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.22f)));

                if (characters.Count == 0)
                {
                    using (Plugin.Instance?.OutfitMed12?.Push())
                    {
                        var msg = "No characters created yet.";
                        var sz = ImGui.CalcTextSize(msg);
                        dl.AddText(
                            new Vector2(bodyMin.X + bodyPadX,
                                        bodyMin.Y + bodyPadTop),
                            Boutique.U32(Boutique.TextFaint), msg);
                    }
                }
                else
                {
                    for (int c = 0; c < characters.Count; c++)
                    {
                        var character = characters[c];
                        bool isInGroup = group.CharacterNames.Contains(character.Name);
                        int row = c / 2;
                        int col = c % 2;
                        float chipMinX = bodyMin.X + bodyPadX + col * (chipColW + chipColGap);
                        float chipMinY = bodyMin.Y + bodyPadTop + row * (chipH + chipRowGap);
                        var chipMin = new Vector2(chipMinX, chipMinY);
                        var chipMax = new Vector2(chipMinX + chipColW, chipMinY + chipH);

                        ImGui.SetCursorScreenPos(chipMin);
                        bool chipClicked = ImGui.InvisibleButton(
                            $"##rg.chip_{idx}_{c}",
                            new Vector2(chipColW, chipH));
                        bool chipHovered = ImGui.IsItemHovered();

                        // ── Background fill: members get a gold-tinted tile,
                        // non-members get a dim surface tile. Hover lifts a step.
                        Vector4 bgCol;
                        if (isInGroup)
                            bgCol = chipHovered
                                ? Boutique.WithAlpha(Boutique.Gold, 0.16f)
                                : Boutique.WithAlpha(Boutique.Gold, 0.10f);
                        else
                            bgCol = chipHovered
                                ? Boutique.WithAlpha(Boutique.Surface2, 0.85f)
                                : Boutique.WithAlpha(Boutique.Surface0, 0.55f);
                        dl.AddRectFilled(chipMin, chipMax, Boutique.U32(bgCol));

                        // 1 px hairline border so the tile reads as discrete.
                        Vector4 borderC = isInGroup
                            ? Boutique.WithAlpha(Boutique.GoldDeep, chipHovered ? 0.85f : 0.55f)
                            : Boutique.WithAlpha(Boutique.BorderSoft, chipHovered ? 1f : 0.6f);
                        dl.AddRect(chipMin, chipMax, Boutique.U32(borderC),
                            0f, ImDrawFlags.None, 1f * scale);

                        // 3 px left bar (sharper than the 2 px before).
                        // Members: solid gold. Non-members: dim gold-deep.
                        // Hover lifts both states by one notch.
                        Vector4 barCol;
                        if (isInGroup) barCol = chipHovered ? Boutique.GoldWarm : Boutique.Gold;
                        else           barCol = chipHovered
                                                 ? Boutique.WithAlpha(Boutique.GoldDeep, 0.55f)
                                                 : Boutique.WithAlpha(Boutique.GoldDeep, 0.28f);
                        dl.AddRectFilled(chipMin,
                            new Vector2(chipMin.X + 3f * scale, chipMax.Y),
                            Boutique.U32(barCol));

                        // ── Right-side state badge ──
                        // Members: filled check inside a gold square.
                        // Non-members: hollow plus glyph in TextGhost (or
                        // GoldWarm on hover) so it reads as "tap to add".
                        float badgeSide = 16f * scale;
                        float badgeRightPad = 8f * scale;
                        var badgeMin = new Vector2(
                            chipMax.X - badgeRightPad - badgeSide,
                            chipMin.Y + (chipH - badgeSide) * 0.5f);
                        var badgeMax = badgeMin + new Vector2(badgeSide, badgeSide);

                        if (isInGroup)
                        {
                            // Filled gold tile + dark check
                            dl.AddRectFilled(badgeMin, badgeMax,
                                Boutique.U32(chipHovered ? Boutique.GoldWarm : Boutique.Gold));
                            string checkGlyph = FontAwesomeIcon.Check.ToIconString();
                            ImGui.PushFont(UiBuilder.IconFont);
                            var ckSz = ImGui.CalcTextSize(checkGlyph);
                            ImGui.PopFont();
                            float ckPx = 9f * scale;
                            float ckRatio = ckPx / UiBuilder.IconFont.FontSize;
                            var ckPos = new Vector2(
                                badgeMin.X + (badgeSide - ckSz.X * ckRatio) * 0.5f,
                                badgeMin.Y + (badgeSide - ckSz.Y * ckRatio) * 0.5f);
                            dl.AddText(UiBuilder.IconFont, ckPx, ckPos,
                                Boutique.U32(new Vector4(0.10f, 0.08f, 0f, 1f)),
                                checkGlyph);
                        }
                        else
                        {
                            // Hollow tile + plus glyph
                            dl.AddRect(badgeMin, badgeMax,
                                Boutique.U32(chipHovered
                                    ? Boutique.GoldWarm
                                    : Boutique.WithAlpha(Boutique.GoldDeep, 0.55f)),
                                0f, ImDrawFlags.None, 1f * scale);
                            string plusGlyph = FontAwesomeIcon.Plus.ToIconString();
                            ImGui.PushFont(UiBuilder.IconFont);
                            var pSz = ImGui.CalcTextSize(plusGlyph);
                            ImGui.PopFont();
                            float pPx = 8f * scale;
                            float pRatio = pPx / UiBuilder.IconFont.FontSize;
                            var pPos = new Vector2(
                                badgeMin.X + (badgeSide - pSz.X * pRatio) * 0.5f,
                                badgeMin.Y + (badgeSide - pSz.Y * pRatio) * 0.5f);
                            uint plusCol = Boutique.U32(chipHovered
                                ? Boutique.GoldWarm
                                : Boutique.TextGhost);
                            dl.AddText(UiBuilder.IconFont, pPx, pPos, plusCol, plusGlyph);
                        }

                        // ── Name (truncate to leave room for the badge) ──
                        using (Plugin.Instance?.OutfitMed13?.Push())
                        {
                            string display = character.Name;
                            var nameSize = ImGui.CalcTextSize(display);
                            float nameLeft = chipMin.X + 10f * scale;
                            float nameRight = badgeMin.X - 6f * scale;
                            float nameAvail = nameRight - nameLeft;
                            if (nameSize.X > nameAvail)
                            {
                                string ell = "...";
                                float ellW = ImGui.CalcTextSize(ell).X;
                                while (display.Length > 1 &&
                                    ImGui.CalcTextSize(display).X + ellW > nameAvail)
                                {
                                    display = display.Substring(0, display.Length - 1);
                                }
                                display += ell;
                                nameSize = ImGui.CalcTextSize(display);
                            }
                            Vector4 inkCol = isInGroup ? Boutique.Text : Boutique.TextDim;
                            if (chipHovered) inkCol = Boutique.Text;
                            dl.AddText(
                                new Vector2(nameLeft,
                                            chipMin.Y + (chipH - nameSize.Y) * 0.5f),
                                Boutique.U32(inkCol), display);
                        }

                        if (chipHovered && character.Name.Length > 16)
                            Boutique.Tooltip(isInGroup
                                ? $"{character.Name}, click to remove"
                                : $"{character.Name}, click to add");

                        if (chipClicked)
                        {
                            if (isInGroup)
                                group.CharacterNames.Remove(character.Name);
                            else
                                group.CharacterNames.Add(character.Name);
                            plugin.Configuration.Save();
                        }
                    }
                }
            }

            // Advance cursor below the card + 8 px gap
            ImGui.SetCursorScreenPos(new Vector2(origin.X, max.Y + 8f * scale));
            ImGui.Dummy(new Vector2(0, 0));
        }

        // Tracks which random groups are expanded
        private HashSet<int> expandedRandomGroups = new();

        private void DrawMainCharacterSettings(float labelWidth, float inputWidth)
        {
            _ = labelWidth; _ = inputWidth;
            float scale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);
            float toggleW = 42f * scale;
            float dropW = 200f * scale;

            Boutique.SettingRow("main.onlyMode", "Enable Main Character Only Mode",
                "When enabled, only your designated main character auto-applies on login. If no main character is set, the normal auto-apply behaviour is used.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.EnableMainCharacterOnly;
                    if (Boutique.TogglePill("main.onlyMode", ref v, scale))
                    {
                        plugin.Configuration.EnableMainCharacterOnly = v;
                        plugin.Configuration.Save();
                    }
                });

            Boutique.SettingRow("main.crown", "Show Ribbon Marker on Main Character",
                "Displays a gold ribbon in the top corner of your main character's card.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.ShowMainCharacterCrown;
                    if (Boutique.TogglePill("main.crown", ref v, scale))
                    {
                        plugin.Configuration.ShowMainCharacterCrown = v;
                        plugin.Configuration.Save();
                    }
                });

            Boutique.SettingRow("main.select", "Select Main Character",
                "The main character is marked with a gold ribbon and can be set to auto-apply exclusively on login.",
                dropW, scale,
                () =>
                {
                    var labels = new List<string> { "None" };
                    foreach (var ch in plugin.Characters.OrderBy(c => c.Name))
                        labels.Add(ch.Name);

                    string currentName = plugin.Configuration.MainCharacterName ?? "None";
                    int currentIdx = labels.FindIndex(s => s == currentName);
                    if (currentIdx < 0) currentIdx = 0;
                    int picked = Boutique.SortPill("main.select", "MAIN", currentIdx, labels, dropW, scale);
                    if (picked >= 0)
                    {
                        var newName = picked == 0 ? null : labels[picked];
                        plugin.Configuration.MainCharacterName = newName;
                        plugin.Configuration.Save();
                        if (newName != null) plugin.AchievementTracker?.OnMainCharacterSet();
                    }
                });

            // Inline single-line status (subtle text instead of a full callout box).
            if (!string.IsNullOrEmpty(plugin.Configuration.MainCharacterName))
            {
                var mainCharacter = plugin.Characters.FirstOrDefault(c => c.Name == plugin.Configuration.MainCharacterName);
                ImGui.Dummy(new Vector2(0, 6f * scale));
                if (mainCharacter != null)
                {
                    using (Plugin.Instance?.OutfitMed12?.Push())
                    {
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.TextColored(Boutique.GoldWarm, FontAwesomeIcon.Bookmark.ToIconString());
                        ImGui.PopFont();
                        ImGui.SameLine(0, 6f * scale);
                        ImGui.TextColored(Boutique.TextDim, $"Current main: {mainCharacter.Name}");
                    }
                }
                else
                {
                    Boutique.Callout(Boutique.CalloutKind.Warning, FontAwesomeIcon.ExclamationTriangle,
                        "Main character not found",
                        $"\"{plugin.Configuration.MainCharacterName}\" no longer exists in your character list.",
                        scale);
                    ImGui.Dummy(new Vector2(0, 4f * scale));
                    if (Boutique.OutlineButton("main.clearMissing", "CLEAR", scale))
                    {
                        plugin.Configuration.MainCharacterName = null;
                        plugin.Configuration.Save();
                    }
                }
            }

            ImGui.Dummy(new Vector2(0, 6f * scale));
        }

        private void DrawDialogueSettings()
        {
            float scale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);
            float toggleW = 42f * scale;
            float dropW = 180f * scale;
            float inputW = 200f * scale;

            // Intro callouts
            Boutique.Callout(Boutique.CalloutKind.Info, FontAwesomeIcon.Comment,
                "What this does",
                "Uses your CS+ Character's name and pronouns in NPC dialogue. Requires a completed RP Profile (name & pronouns).",
                scale);
            Boutique.Callout(Boutique.CalloutKind.Warning, FontAwesomeIcon.ExclamationTriangle,
                "They/Them note",
                "Users with They/Them pronouns may occasionally see garbled text in chat. Switch between chat tabs to refresh the display if this happens.",
                scale);

            // Master toggle
            Boutique.SettingRow("dlg.master", "Enable Immersive Dialogue",
                "Replaces NPC dialogue text with your CS+ Character's name and pronouns instead of your game character. Requires an active CS+ character with RP Profile data.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.EnableDialogueIntegration;
                    bool wasEnabled = plugin.Configuration.EnableDialogueIntegration;
                    if (Boutique.TogglePill("dlg.master", ref v, scale))
                    {
                        plugin.Configuration.EnableDialogueIntegration = v;
                        if (!v)
                        {
                            plugin.Configuration.EnableLuaHookDialogue = false;
                            plugin.Configuration.ReplaceNameInDialogue = false;
                            plugin.Configuration.ReplacePronounsInDialogue = false;
                            plugin.Configuration.ReplaceGenderedTerms = false;
                            plugin.Configuration.EnableAdvancedTitleReplacement = false;
                            plugin.Configuration.EnableSmartGrammarInDialogue = false;
                            plugin.Configuration.EnableRaceReplacement = false;
                            plugin.Configuration.ShowDialogueReplacementPreview = false;
                        }
                        else
                        {
                            plugin.Configuration.EnableLuaHookDialogue = true;
                            plugin.Configuration.ReplaceNameInDialogue = true;
                            plugin.Configuration.ReplacePronounsInDialogue = true;
                            plugin.Configuration.ReplaceGenderedTerms = true;
                            plugin.Configuration.EnableAdvancedTitleReplacement = true;
                            plugin.Configuration.EnableSmartGrammarInDialogue = true;
                            plugin.Configuration.EnableRaceReplacement = true;
                            plugin.Configuration.ShowDialogueReplacementPreview = false;
                            plugin.EnsureDialogueProcessorInitialized();
                        }
                        plugin.Configuration.Save();
                        if (v && !wasEnabled) plugin.AchievementTracker?.OnImmersiveDialogueEnabled();
                    }
                });

            if (plugin.Configuration.EnableDialogueIntegration)
            {
                Boutique.SettingRow("dlg.replaceName", "Use CS+ Character Name",
                    "Replace your real character name with your CS+ character name in dialogue.",
                    toggleW, scale,
                    () =>
                    {
                        bool v = plugin.Configuration.ReplaceNameInDialogue;
                        if (Boutique.TogglePill("dlg.replaceName", ref v, scale))
                        {
                            plugin.Configuration.ReplaceNameInDialogue = v;
                            plugin.Configuration.Save();
                        }
                    });

                Boutique.SettingRow("dlg.replacePronouns", "Use CS+ Character Pronouns",
                    "Replace pronouns in dialogue with your character's pronouns from their RP Profile.",
                    toggleW, scale,
                    () =>
                    {
                        bool v = plugin.Configuration.ReplacePronounsInDialogue;
                        if (Boutique.TogglePill("dlg.replacePronouns", ref v, scale))
                        {
                            plugin.Configuration.ReplacePronounsInDialogue = v;
                            plugin.Configuration.Save();
                        }
                    });

                Boutique.SettingRow("dlg.replaceGendered", "Use Gender-Neutral Terms",
                    "Replace gendered terms like 'sir/lady' or 'man/woman' with appropriate alternatives based on your character's pronouns.",
                    toggleW, scale,
                    () =>
                    {
                        bool v = plugin.Configuration.ReplaceGenderedTerms;
                        if (Boutique.TogglePill("dlg.replaceGendered", ref v, scale))
                        {
                            plugin.Configuration.ReplaceGenderedTerms = v;
                            plugin.Configuration.Save();
                        }
                    });

                // ── They/Them sub-section ──
                Boutique.SubSectionHeader("THEY/THEM PRONOUN SETTINGS", null, scale);

                Boutique.SettingRow("dlg.neutralStyle", "Neutral Title Style",
                    "Friend / Mx. / Traveler / Adventurer / Custom. Used to replace gendered honorifics ('honored sir' → 'honored friend').",
                    dropW, scale,
                    () =>
                    {
                        var styleOptions = new List<string> { "Friend", "Mx.", "Traveler", "Adventurer", "Custom" };
                        int currentIdx = (int)plugin.Configuration.TheyThemStyle;
                        int picked = Boutique.SortPill("dlg.neutralStyle", "STYLE", currentIdx, styleOptions, dropW, scale);
                        if (picked >= 0)
                        {
                            plugin.Configuration.TheyThemStyle = (Configuration.GenderNeutralStyle)picked;
                            plugin.Configuration.Save();
                        }
                    });

                if (plugin.Configuration.TheyThemStyle == Configuration.GenderNeutralStyle.Custom)
                {
                    ImGui.Indent(20f * scale);
                    Boutique.SettingRow("dlg.customTitle", "Custom Title",
                        "Your preferred gender-neutral title (e.g. 'Warrior', 'Champion', 'Canadian').",
                        inputW, scale,
                        () =>
                        {
                            var s = plugin.Configuration.CustomGenderNeutralTitle;
                            if (Boutique.DrawBoutiqueTextInput("##CustomGenderNeutral", ref s, 50, inputW, "Custom title"))
                            {
                                plugin.Configuration.CustomGenderNeutralTitle = s;
                                plugin.Configuration.Save();
                            }
                        },
                        subOption: true);
                    ImGui.Unindent(20f * scale);
                }

                // Inline preview line (italic-feel via TextDim, no callout box).
                var characterName = plugin.Characters.FirstOrDefault()?.Name ?? "Warrior of Light";
                ImGui.Dummy(new Vector2(0, 4f * scale));
                using (Plugin.Instance?.OutfitMed12?.Push())
                {
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.TextColored(Boutique.TextFaint, FontAwesomeIcon.Eye.ToIconString());
                    ImGui.PopFont();
                    ImGui.SameLine(0, 6f * scale);
                    ImGui.PushTextWrapPos();
                    ImGui.TextColored(Boutique.TextDim,
                        $"Preview: \"Sir {characterName}\" -> \"{plugin.Configuration.GetGenderNeutralFormalTitle()} {characterName}\"");
                    ImGui.PopTextWrapPos();
                }
            }

            ImGui.Dummy(new Vector2(0, 6f * scale));
        }

        private void DrawNameSyncSettings()
        {
            float scale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);
            float toggleW = 42f * scale;
            float dropW = 90f * scale;
            float keyBtnW = 110f * scale;

            // Mark feature as seen when this section is opened
            if (!plugin.Configuration.SeenFeatures.Contains(FeatureKeys.NameSync))
            {
                plugin.Configuration.SeenFeatures.Add(FeatureKeys.NameSync);
                plugin.Configuration.Save();
            }

            // ── YOUR NAME ──
            Boutique.SubSectionHeader("YOUR NAME", null, scale);

            Boutique.SettingRow("ns.simpleGlow", "Use simple glow",
                "Use a simple solid glow instead of the animated wave effect. Enable this if you're experiencing crashes (especially with Honorific's animated gradient titles). The game has internal limits on nameplate effects.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.UseSimpleNameplateGlow;
                    if (Boutique.TogglePill("ns.simpleGlow", ref v, scale))
                    {
                        plugin.Configuration.UseSimpleNameplateGlow = v;
                        plugin.Configuration.Save();
                    }
                });

            Boutique.SettingRow("ns.showSelf", "Show my CS+ name to myself",
                "Replace your in-game name with your CS+ character name in nameplates, chat, and party list. Client-side only, other players don't see this unless they also have CS+ and you opt in below.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.EnableNameReplacement;
                    if (Boutique.TogglePill("ns.showSelf", ref v, scale))
                    {
                        plugin.Configuration.EnableNameReplacement = v;
                        if (v)
                        {
                            plugin.AchievementTracker?.OnNameSyncEnabled();
                            plugin.Configuration.NameReplacementNameplate = true;
                            plugin.Configuration.NameReplacementChat = true;
                            plugin.Configuration.NameReplacementPartyList = true;
                            plugin.EnsurePlayerNameProcessorInitialized();
                        }
                        plugin.Configuration.Save();
                    }
                });

            if (plugin.Configuration.EnableNameReplacement)
            {
                ImGui.Indent(20f * scale);
                Boutique.SettingRow("ns.nameplate", "Nameplate",
                    "Replace your nameplate above your character with your CS+ name.",
                    toggleW, scale,
                    () =>
                    {
                        bool v = plugin.Configuration.NameReplacementNameplate;
                        if (Boutique.TogglePill("ns.nameplate", ref v, scale))
                        {
                            plugin.Configuration.NameReplacementNameplate = v;
                            plugin.Configuration.Save();
                        }
                    },
                    subOption: true);

                Boutique.SettingRow("ns.chat", "Chat messages",
                    "Replace your name in chat-message sender display.",
                    toggleW, scale,
                    () =>
                    {
                        bool v = plugin.Configuration.NameReplacementChat;
                        if (Boutique.TogglePill("ns.chat", ref v, scale))
                        {
                            plugin.Configuration.NameReplacementChat = v;
                            plugin.Configuration.Save();
                        }
                    },
                    subOption: true);

                Boutique.SettingRow("ns.partyList", "Party list",
                    "Replace your name in the party list.",
                    toggleW, scale,
                    () =>
                    {
                        bool v = plugin.Configuration.NameReplacementPartyList;
                        if (Boutique.TogglePill("ns.partyList", ref v, scale))
                        {
                            plugin.Configuration.NameReplacementPartyList = v;
                            plugin.Configuration.Save();
                        }
                    },
                    subOption: true);

                Boutique.SettingRow("ns.hideFc", "Hide FC tag",
                    "Hide your Free Company tag from your nameplate. Only affects nameplate, not other UI elements.",
                    toggleW, scale,
                    () =>
                    {
                        bool v = plugin.Configuration.HideFCTagInNameplate;
                        if (Boutique.TogglePill("ns.hideFc", ref v, scale))
                        {
                            plugin.Configuration.HideFCTagInNameplate = v;
                            plugin.Configuration.Save();
                        }
                    },
                    subOption: true);
                ImGui.Unindent(20f * scale);
            }

            // ── SHARING ──
            Boutique.SubSectionHeader("SHARING", null, scale);

            Boutique.SettingRow("ns.allowOthers", "Allow others to see my CS+ name",
                "When enabled, other CS+ users who have 'Show other CS+ users' names' turned on will see your CS+ character name instead of your in-game name. Requires your RP Profile sharing set to 'Direct Sharing' or 'Public' (not Private).",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.AllowOthersToSeeMyCSName;
                    if (Boutique.TogglePill("ns.allowOthers", ref v, scale))
                    {
                        plugin.Configuration.AllowOthersToSeeMyCSName = v;
                        if (v) plugin.AchievementTracker?.OnSharedNameEnabled();
                        plugin.Configuration.Save();
                    }
                });

            // ── OTHER CS+ USERS ──
            Boutique.SubSectionHeader("OTHER CS+ USERS", null, scale);

            Boutique.SettingRow("ns.enableShared", "Show other CS+ users' names",
                "See other CS+ users' character names instead of their in-game names. Only shows for users who have opted in. Independent of self-name replacement.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.EnableSharedNameReplacement;
                    if (Boutique.TogglePill("ns.enableShared", ref v, scale))
                    {
                        plugin.Configuration.EnableSharedNameReplacement = v;
                        if (v) plugin.EnsurePlayerNameProcessorInitialized();
                        plugin.Configuration.Save();
                    }
                });

            if (plugin.Configuration.EnableSharedNameReplacement)
            {
                ImGui.Indent(20f * scale);
                Boutique.SettingRow("ns.simpleGlowOthers", "Use simple glow for others",
                    "Use a simple solid glow instead of an animated wave effect for other players' nameplates. Disables periodic nameplate refresh. Enable if you notice performance issues or crashes with many CS+ users nearby.",
                    toggleW, scale,
                    () =>
                    {
                        bool v = plugin.Configuration.UseSimpleGlowForOthers;
                        if (Boutique.TogglePill("ns.simpleGlowOthers", ref v, scale))
                        {
                            plugin.Configuration.UseSimpleGlowForOthers = v;
                            plugin.Configuration.Save();
                        }
                    },
                    subOption: true);
                ImGui.Unindent(20f * scale);
            }

            // ── QUICK REVEAL ──
            Boutique.SubSectionHeader("QUICK REVEAL", null, scale);

            Boutique.SettingRow("ns.revealKeybind", "Hold key to reveal actual names",
                "Hold the selected key to temporarily see actual in-game names instead of CS+ names. Useful for checking who someone really is.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.EnableRevealActualNamesKeybind;
                    if (Boutique.TogglePill("ns.revealKeybind", ref v, scale))
                    {
                        plugin.Configuration.EnableRevealActualNamesKeybind = v;
                        plugin.Configuration.Save();
                    }
                });

            if (plugin.Configuration.EnableRevealActualNamesKeybind)
            {
                ImGui.Indent(20f);

                // Get current key display name
                string currentKeyName;
                if (plugin.Configuration.RevealActualNamesCustomKey > 0)
                {
                    currentKeyName = !string.IsNullOrEmpty(plugin.Configuration.RevealActualNamesCustomKeyName)
                        ? plugin.Configuration.RevealActualNamesCustomKeyName
                        : $"Key {plugin.Configuration.RevealActualNamesCustomKey}";
                }
                else
                {
                    currentKeyName = plugin.Configuration.RevealActualNamesKey switch
                    {
                        Configuration.RevealNamesKeyOption.Alt => "Alt",
                        Configuration.RevealNamesKeyOption.Ctrl => "Ctrl",
                        Configuration.RevealNamesKeyOption.Shift => "Shift",
                        _ => "Alt"
                    };
                }

                ImGui.Indent(20f * scale);

                // Modifier dropdown row
                var modifierOptions = new List<string> { "None", "Ctrl", "Shift", "Alt" };
                int currentModifierIndex = plugin.Configuration.RevealActualNamesModifier switch
                {
                    0x11 => 1, 0x10 => 2, 0x12 => 3, _ => 0
                };
                Boutique.SettingRow("ns.revealMod", "Modifier",
                    "Optional modifier key held alongside the main key (Ctrl / Shift / Alt). Pick None for a single-key bind.",
                    dropW, scale,
                    () =>
                    {
                        int picked = Boutique.SortPill("ns.revealMod", "MOD", currentModifierIndex,
                            modifierOptions, dropW, scale);
                        if (picked >= 0)
                        {
                            plugin.Configuration.RevealActualNamesModifier = picked switch
                            {
                                1 => 0x11, 2 => 0x10, 3 => 0x12, _ => 0
                            };
                            plugin.Configuration.RevealActualNamesModifierName = picked > 0 ? modifierOptions[picked] : "";
                            plugin.Configuration.Save();
                        }
                    },
                    subOption: true);

                // Key bind row (button shows current key, click to capture).
                if (isCapturingRevealKey)
                {
                    int? capturedKey = null;
                    foreach (var kvp in KeyNames)
                    {
                        if (kvp.Key == 0x10 || kvp.Key == 0x11 || kvp.Key == 0x12 ||
                            kvp.Key == 0xA0 || kvp.Key == 0xA1 || kvp.Key == 0xA2 ||
                            kvp.Key == 0xA3 || kvp.Key == 0xA4 || kvp.Key == 0xA5)
                            continue;
                        if ((GetAsyncKeyState(kvp.Key) & 0x8000) != 0)
                        {
                            capturedKey = kvp.Key;
                            break;
                        }
                    }
                    if (capturedKey.HasValue)
                    {
                        plugin.Configuration.RevealActualNamesCustomKey = capturedKey.Value;
                        plugin.Configuration.RevealActualNamesCustomKeyName = KeyNames.TryGetValue(capturedKey.Value, out var name) ? name : $"Key {capturedKey.Value}";
                        plugin.Configuration.Save();
                        isCapturingRevealKey = false;
                    }
                }

                Boutique.SettingRow("ns.revealKey", "Key",
                    isCapturingRevealKey
                        ? "Press any key to bind it. Press CANCEL to abort."
                        : "Click to rebind. Modifier keys (Ctrl / Shift / Alt) are set via the Modifier row above.",
                    keyBtnW, scale,
                    () =>
                    {
                        string label = isCapturingRevealKey ? "PRESS A KEY..." : currentKeyName.ToUpperInvariant();
                        if (Boutique.OutlineButton("ns.revealKeyBtn", label, scale))
                        {
                            if (isCapturingRevealKey) isCapturingRevealKey = false;
                            else isCapturingRevealKey = true;
                        }
                    },
                    subOption: true);

                ImGui.Unindent(20f * scale);
            }

            ImGui.Dummy(new Vector2(0, 6f * scale));
        }

        private void DrawCharacterAssignmentSettings()
        {
            float scale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);

            // Intro callout
            Boutique.Callout(Boutique.CalloutKind.Info, FontAwesomeIcon.UserCheck,
                "Character Assignments",
                "Assign specific CS+ Characters to auto-apply when logging into specific in-game characters.",
                scale);

            // Warning if Auto-Apply Last Used Character is disabled
            if (!plugin.Configuration.EnableLastUsedCharacterAutoload)
            {
                Boutique.Callout(Boutique.CalloutKind.Warning, FontAwesomeIcon.ExclamationTriangle,
                    "Auto-Apply Last Used Character is disabled",
                    "Character Assignments require this feature. Enable 'Auto-Apply Last Used Character on Login' in the Behavior section to use assignments.",
                    scale);
            }

            // Warning if Main Character Only Mode is enabled
            if (plugin.Configuration.EnableMainCharacterOnly)
            {
                Boutique.Callout(Boutique.CalloutKind.Warning, FontAwesomeIcon.ExclamationTriangle,
                    "Main Character Only Mode is enabled",
                    "Character Assignments will be ignored. Disable Main Character Only Mode in the Main Character section to use assignments.",
                    scale);
            }

            var dl = ImGui.GetWindowDrawList();
            double time = ImGui.GetTime();

            // Build the in-game character list (used by both editor + composer)
            var knownRealCharacters = plugin.Configuration.LastUsedCharacterByPlayer.Keys.ToList();
            if (Plugin.ClientState.IsLoggedIn && Plugin.ObjectTable.LocalPlayer != null)
            {
                var player = Plugin.ObjectTable.LocalPlayer;
                if (player.HomeWorld.IsValid)
                {
                    string currentFormat = $"{player.Name.TextValue}@{player.HomeWorld.Value.Name}";
                    if (!knownRealCharacters.Contains(currentFormat))
                        knownRealCharacters.Insert(0, currentFormat);
                }
            }
            knownRealCharacters = knownRealCharacters.OrderBy(x => x).ToList();

            // ── Composer pane (always at top, drafting a new assignment) ──
            DrawAssignmentComposerCard(dl, scale, time, knownRealCharacters);

            // ── Editor card (active assignment in edit mode) ──
            if (!string.IsNullOrEmpty(editingAssignmentKey))
            {
                DrawAssignmentEditorCard(dl, scale, time);
            }

            // ── Bond plate stack (existing assignments) ──
            string? assignmentToRemove = null;
            foreach (var assignment in plugin.Configuration.CharacterAssignments.ToList())
            {
                bool editClicked, removeClicked;
                DrawAssignmentBondPlate(dl, assignment.Key, assignment.Value, scale, time,
                    out editClicked, out removeClicked);
                if (editClicked)
                {
                    editingAssignmentKey = assignment.Key;
                    var (charName, designName) = ParseCharacterAssignmentValue(assignment.Value);
                    editingAssignmentValue = charName;
                    editingAssignmentUseDesign = !string.IsNullOrEmpty(designName);
                    editingAssignmentDesignBuffer = designName ?? "";
                }
                if (removeClicked) assignmentToRemove = assignment.Key;
            }
            if (assignmentToRemove != null)
            {
                plugin.Configuration.CharacterAssignments.Remove(assignmentToRemove);
                plugin.Configuration.Save();
            }

            // Helpful tip when no known characters yet
            if (!knownRealCharacters.Any())
            {
                Boutique.Callout(Boutique.CalloutKind.Info, FontAwesomeIcon.Lightbulb,
                    "Tip",
                    "The plugin will remember character names after you log into them and use a CS+ character at least once.",
                    scale);
            }

            // ── Known In-Game Characters (slim plates) ──
            if (plugin.Configuration.LastUsedCharacterByPlayer.Any())
            {
                Boutique.SubSectionHeader($"KNOWN IN-GAME CHARACTERS ({plugin.Configuration.LastUsedCharacterByPlayer.Count})", null, scale);
                Boutique.Callout(Boutique.CalloutKind.Info, FontAwesomeIcon.InfoCircle,
                    "Pruning these names",
                    "These are the in-game characters CS+ has seen you log into. Remove any that no longer exist or that you want pruned from the dropdowns above and in the Gallery Main Character picker.",
                    scale);

                string? currentPlayerKey = null;
                if (Plugin.ClientState.IsLoggedIn && Plugin.ObjectTable.LocalPlayer is { } player && player.HomeWorld.IsValid)
                    currentPlayerKey = $"{player.Name.TextValue}@{player.HomeWorld.Value.Name}";

                string? toRemoveKey = null;
                foreach (var kvp in plugin.Configuration.LastUsedCharacterByPlayer.OrderBy(k => k.Key))
                {
                    bool isCurrent = kvp.Key == currentPlayerKey;
                    if (DrawKnownCharacterPlate(dl, kvp.Key, kvp.Value, isCurrent, scale))
                        toRemoveKey = kvp.Key;
                }
                if (toRemoveKey != null)
                    RemoveKnownInGameCharacter(toRemoveKey);
            }

            ImGui.Dummy(new Vector2(0, 6f * scale));
        }

        // BOND PLATE, a saved assignment, rendered as a relationship card.
        // 64 px tall, 8 px TR+BL chamfer slip, surface1@0.6 fill, BorderSoft
        // outline, 2 px gold-deep top hairline. Two-column inner layout split
        // by a vertical gold-deep binding hairline + a small gold lozenge
        // clasp in the centre. Edit + Delete affordances top-right.
        private void DrawAssignmentBondPlate(ImDrawListPtr dl, string physicalKey, string value,
            float scale, double time, out bool editClicked, out bool removeClicked)
        {
            editClicked = false;
            removeClicked = false;

            float availW = ImGui.GetContentRegionAvail().X;
            var origin = ImGui.GetCursorScreenPos();
            float plateH = 64f * scale;
            float chamfer = 8f * scale;
            var min = origin;
            var max = origin + new Vector2(availW, plateH);

            // ── Card silhouette ──
            Boutique.FillSlip(dl, min, max, chamfer,
                Boutique.U32(Boutique.WithAlpha(Boutique.Surface1, 0.60f)));
            Span<Vector2> pts = stackalloc Vector2[6];
            Boutique.BuildSlipPolygon(min, max, chamfer, pts);
            for (int s = 0; s < 6; s++) dl.PathLineTo(pts[s]);
            dl.PathStroke(Boutique.U32(Boutique.BorderSoft), ImDrawFlags.Closed, 1f * scale);
            dl.AddRectFilled(
                new Vector2(min.X, min.Y),
                new Vector2(max.X - chamfer, min.Y + 2f * scale),
                Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.55f)));

            // Action affordances (top-right): Edit + Delete
            float actionsRightPad = 8f * scale;
            float actionTop = min.Y + 8f * scale;
            float actionSize = 18f * scale;
            float actionGap = 4f * scale;
            var deleteMin = new Vector2(max.X - actionsRightPad - actionSize, actionTop);
            var editMin = new Vector2(deleteMin.X - actionGap - actionSize, actionTop);

            ImGui.SetCursorScreenPos(deleteMin);
            removeClicked = ImGui.InvisibleButton($"##ca.del_{physicalKey}", new Vector2(actionSize, actionSize));
            bool deleteHovered = ImGui.IsItemHovered();
            if (deleteHovered) Boutique.Tooltip($"Remove assignment for {physicalKey}");

            ImGui.SetCursorScreenPos(editMin);
            editClicked = ImGui.InvisibleButton($"##ca.edit_{physicalKey}", new Vector2(actionSize, actionSize));
            bool editHovered = ImGui.IsItemHovered();
            if (editHovered) Boutique.Tooltip($"Edit assignment for {physicalKey}");

            // Hover wash on whole card (subtle)
            bool cardHovered = ImGui.IsMouseHoveringRect(min, max) && !ImGui.IsAnyItemActive();
            if (cardHovered)
                dl.AddRectFilled(min, max, Boutique.U32(new Vector4(1f, 1f, 1f, 0.020f)));

            // ── Layout split: left half / centre binding / right half ──
            float sidePadX = 14f * scale;
            float centerW = 28f * scale;
            float halfW = (availW - sidePadX * 2 - centerW) * 0.5f;

            float leftMinX = min.X + sidePadX;
            float leftMaxX = leftMinX + halfW;
            float rightMaxX = max.X - sidePadX;
            float rightMinX = rightMaxX - halfW;
            float centerMidX = min.X + availW * 0.5f;

            // Account for the action icons on the right side: shrink right text width
            float rightTextRight = editMin.X - 8f * scale;
            float rightTextW = rightTextRight - rightMinX;
            if (rightTextW < 60f * scale) rightTextW = 60f * scale;

            // Parse the assignment value (CS+ char + optional design)
            string csName;
            string? designName;
            bool isNone = value == "None";
            if (isNone)
            {
                csName = "None";
                designName = null;
            }
            else
            {
                (csName, designName) = ParseCharacterAssignmentValue(value);
            }

            // Parse physical name into First Last and @World
            string firstLast = physicalKey;
            string world = "";
            int atIdx = physicalKey.IndexOf('@');
            if (atIdx > 0)
            {
                firstLast = physicalKey.Substring(0, atIdx);
                world = "@" + physicalKey.Substring(atIdx + 1);
            }

            // ── LEFT half: IN-GAME ──
            float kickerY = min.Y + 12f * scale;
            float nameY = min.Y + 26f * scale;
            float worldY = min.Y + 42f * scale;

            // Green seal (6×6 filled square at --good 60%)
            float sealSide = 6f * scale;
            var sealMin = new Vector2(leftMinX, kickerY + 4f * scale);
            dl.AddRectFilled(sealMin, sealMin + new Vector2(sealSide, sealSide),
                Boutique.U32(Boutique.WithAlpha(Boutique.Green, 0.60f)));

            // Kicker `IN-GAME` Oswald Semi 9 tracked-caps gold-deep
            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                float trackPx = 1.2f * scale;
                Boutique.DrawTrackedText(dl,
                    new Vector2(leftMinX + sealSide + 6f * scale, kickerY),
                    "IN-GAME", Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.85f)), trackPx);
            }

            // Name (OutfitMed13, white) + ellipsis truncation
            using (Plugin.Instance?.OutfitMed13?.Push())
            {
                string display = TruncateToWidth(firstLast, halfW);
                dl.AddText(new Vector2(leftMinX, nameY),
                    Boutique.U32(Boutique.Text), display);
            }

            // World (OutfitBody12, dim) + ellipsis truncation
            using (Plugin.Instance?.OutfitBody12?.Push())
            {
                string display = TruncateToWidth(world, halfW);
                dl.AddText(new Vector2(leftMinX, worldY),
                    Boutique.U32(Boutique.TextDim), display);
            }

            // ── CENTRE: Binding ──
            DrawBondBinding(dl, centerMidX, min.Y + 10f * scale, max.Y - 10f * scale,
                cardHovered, time, scale);

            // ── RIGHT half: CS+ ──
            // Kicker anchors to rightTextRight (LEFT of the action icons)
            // so it never overlaps the edit/delete glyphs in the corner.
            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                float trackPx = 1.2f * scale;
                string kicker = "CS+";
                float kickerW = Boutique.MeasureTrackedText(kicker, trackPx);
                Boutique.DrawTrackedText(dl,
                    new Vector2(rightTextRight - kickerW, kickerY),
                    kicker, Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.85f)), trackPx);
            }

            // CS+ name (OutfitMed13, gold-warm; or Red if "None")
            using (Plugin.Instance?.OutfitMed13?.Push())
            {
                string display = TruncateToWidth(isNone ? "None" : csName, rightTextW);
                Vector4 nameCol = isNone
                    ? Boutique.WithAlpha(Boutique.Red, 0.80f)
                    : Boutique.GoldWarm;
                var nameSz = ImGui.CalcTextSize(display);
                dl.AddText(new Vector2(rightTextRight - nameSz.X, nameY),
                    Boutique.U32(nameCol), display);
            }

            // Design tag / status (OutfitBody12)
            using (Plugin.Instance?.OutfitBody12?.Push())
            {
                string display;
                Vector4 tagCol;
                if (isNone)
                {
                    display = "(skip auto-apply)";
                    tagCol = Boutique.WithAlpha(Boutique.Red, 0.65f);
                }
                else if (!string.IsNullOrEmpty(designName))
                {
                    display = $"({designName})";
                    tagCol = Boutique.TextDim;
                }
                else
                {
                    display = "(no design)";
                    tagCol = Boutique.TextFaint;
                }
                display = TruncateToWidth(display, rightTextW);
                var tagSz = ImGui.CalcTextSize(display);
                dl.AddText(new Vector2(rightTextRight - tagSz.X, worldY),
                    Boutique.U32(tagCol), display);
            }

            // Edit pencil
            string editGlyph = FontAwesomeIcon.PencilAlt.ToIconString();
            DrawIconCentered(dl, editMin, actionSize, editGlyph,
                editHovered ? Boutique.GoldWarm : Boutique.TextGhost, scale);

            // Delete X
            string xGlyph = FontAwesomeIcon.Times.ToIconString();
            DrawIconCentered(dl, deleteMin, actionSize, xGlyph,
                deleteHovered ? Boutique.Red : Boutique.TextGhost, scale);

            // Advance cursor below + 8 px gap
            ImGui.SetCursorScreenPos(new Vector2(origin.X, max.Y + 8f * scale));
            ImGui.Dummy(new Vector2(0, 0));
        }

        // EDITOR CARD, bond plate silhouette but with a STATIC in-game name
        // on the left and editable CS+ controls on the right. Cancel + Save
        // pills at the bottom.
        private void DrawAssignmentEditorCard(ImDrawListPtr dl, float scale, double time)
        {
            float availW = ImGui.GetContentRegionAvail().X;
            float chamfer = 8f * scale;
            var origin = ImGui.GetCursorScreenPos();

            // Pre-measure. Header rows: kicker + name display on left, CS+
            // SortPill on right (+ optional Design SortPill stacked under it).
            var editChar = plugin.Configuration.Characters.FirstOrDefault(c => c.Name == editingAssignmentValue);
            bool showDesign = editChar != null && editChar.Designs.Any();
            float headerH = showDesign ? 92f * scale : 60f * scale;
            float pillsRowH = 44f * scale;
            float plateH = headerH + pillsRowH;
            var min = origin;
            var max = origin + new Vector2(availW, plateH);

            // ── Card silhouette ── (slightly brighter than composer to read as "live edit")
            Boutique.FillSlip(dl, min, max, chamfer,
                Boutique.U32(Boutique.WithAlpha(Boutique.Surface1, 0.75f)));
            Span<Vector2> pts = stackalloc Vector2[6];
            Boutique.BuildSlipPolygon(min, max, chamfer, pts);
            for (int s = 0; s < 6; s++) dl.PathLineTo(pts[s]);
            dl.PathStroke(Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.85f)),
                ImDrawFlags.Closed, 1.5f * scale);
            // Stronger gold hairline so users see this is the live one
            dl.AddRectFilled(
                new Vector2(min.X, min.Y),
                new Vector2(max.X - chamfer, min.Y + 2f * scale),
                Boutique.U32(Boutique.Gold));

            // EDITING kicker top-centre
            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                float trackPx = 1.4f * scale;
                string kicker = "EDITING";
                float kw = Boutique.MeasureTrackedText(kicker, trackPx);
                Boutique.DrawTrackedText(dl,
                    new Vector2(min.X + (availW - kw) * 0.5f, min.Y + 6f * scale),
                    kicker, Boutique.U32(Boutique.GoldWarm), trackPx);
            }

            float sidePadX = 14f * scale;
            float centerW = 28f * scale;
            float halfW = (availW - sidePadX * 2 - centerW) * 0.5f;
            float leftMinX = min.X + sidePadX;
            float rightMaxX = max.X - sidePadX;
            float rightMinX = rightMaxX - halfW;
            float centerMidX = min.X + availW * 0.5f;

            // Parse physical name
            string firstLast = editingAssignmentKey;
            string world = "";
            int atIdx = editingAssignmentKey.IndexOf('@');
            if (atIdx > 0)
            {
                firstLast = editingAssignmentKey.Substring(0, atIdx);
                world = "@" + editingAssignmentKey.Substring(atIdx + 1);
            }

            // ── LEFT: static client (read-only) ──
            float kickerY = min.Y + 22f * scale;
            float nameY = min.Y + 36f * scale;
            float worldY = min.Y + 52f * scale;

            float sealSide = 6f * scale;
            dl.AddRectFilled(
                new Vector2(leftMinX, kickerY + 4f * scale),
                new Vector2(leftMinX + sealSide, kickerY + 4f * scale + sealSide),
                Boutique.U32(Boutique.WithAlpha(Boutique.Green, 0.60f)));

            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                float trackPx = 1.2f * scale;
                Boutique.DrawTrackedText(dl,
                    new Vector2(leftMinX + sealSide + 6f * scale, kickerY),
                    "IN-GAME", Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.85f)), trackPx);
            }
            using (Plugin.Instance?.OutfitMed13?.Push())
            {
                dl.AddText(new Vector2(leftMinX, nameY),
                    Boutique.U32(Boutique.Text),
                    TruncateToWidth(firstLast, halfW));
            }
            using (Plugin.Instance?.OutfitBody12?.Push())
            {
                dl.AddText(new Vector2(leftMinX, worldY),
                    Boutique.U32(Boutique.TextDim),
                    TruncateToWidth(world, halfW));
            }

            // ── CENTRE binding ──
            DrawBondBinding(dl, centerMidX, min.Y + 18f * scale, min.Y + headerH - 4f * scale,
                false, time, scale);

            // ── RIGHT: CS+ character SortPill ──
            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                float trackPx = 1.2f * scale;
                string kicker = "CS+";
                float kw = Boutique.MeasureTrackedText(kicker, trackPx);
                Boutique.DrawTrackedText(dl,
                    new Vector2(rightMaxX - kw, kickerY),
                    kicker, Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.85f)), trackPx);
            }

            // CS+ character SortPill (full half-width, anchored right)
            var csOptions = new List<string> { "None" };
            foreach (var c in plugin.Configuration.Characters.OrderBy(c => c.Name))
                csOptions.Add(c.Name);
            int curCsIdx = csOptions.FindIndex(s => s == editingAssignmentValue);
            if (curCsIdx < 0) curCsIdx = -1;

            float pillH = 26f * scale;
            ImGui.SetCursorScreenPos(new Vector2(rightMinX, nameY - 4f * scale));
            int pickedCs = Boutique.SortPill("ca.edit.cs", "CS+",
                curCsIdx, csOptions, halfW, scale);
            if (pickedCs >= 0)
            {
                string newName = csOptions[pickedCs];
                if (newName != editingAssignmentValue)
                {
                    editingAssignmentValue = newName;
                    editingAssignmentUseDesign = false;
                    editingAssignmentDesignBuffer = "";
                }
            }

            // Design pill stacks UNDER the CS+ pill (same X). First option
            // "(no design)" clears useDesign; picking a real design sets it.
            if (showDesign)
            {
                var designOptions = new List<string> { "(no design)" };
                foreach (var d in editChar!.Designs.OrderBy(d => d.Name))
                    designOptions.Add(d.Name);
                int curDesignIdx = editingAssignmentUseDesign && !string.IsNullOrWhiteSpace(editingAssignmentDesignBuffer)
                    ? designOptions.FindIndex(s => s == editingAssignmentDesignBuffer)
                    : 0;
                if (curDesignIdx < 0) curDesignIdx = 0;
                float designPillY = nameY + 28f * scale;
                ImGui.SetCursorScreenPos(new Vector2(rightMinX, designPillY));
                int pickedD = Boutique.SortPill("ca.edit.design", "DESIGN",
                    curDesignIdx, designOptions, halfW, scale);
                if (pickedD >= 0)
                {
                    if (pickedD == 0)
                    {
                        editingAssignmentUseDesign = false;
                        editingAssignmentDesignBuffer = "";
                    }
                    else
                    {
                        editingAssignmentUseDesign = true;
                        editingAssignmentDesignBuffer = designOptions[pickedD];
                    }
                }
            }

            // ── Pills row: Cancel (outline) + Save (gold pill) ──
            float pillsY = max.Y - pillsRowH + 8f * scale;
            ImGui.SetCursorScreenPos(new Vector2(leftMinX, pillsY));
            if (Boutique.OutlineButton("ca.edit.cancel", "CANCEL", scale))
            {
                editingAssignmentKey = "";
                editingAssignmentValue = "";
                editingAssignmentUseDesign = false;
                editingAssignmentDesignBuffer = "";
            }

            // Save = gold pill anchored right
            float savePillTrack = 1.6f * scale;
            var savePillSize = Boutique.DrawGoldPillSize("SAVE", savePillTrack, scale);
            savePillSize.X = MathF.Max(savePillSize.X, 84f * scale);
            var savePillMin = new Vector2(rightMaxX - savePillSize.X, pillsY);
            var savePillMax = savePillMin + savePillSize;
            ImGui.SetCursorScreenPos(savePillMin);
            bool saveClicked = ImGui.InvisibleButton("##ca.edit.save", savePillSize);
            bool saveHovered = ImGui.IsItemHovered();
            Boutique.DrawGoldPill(dl, savePillMin, savePillMax, "SAVE",
                savePillTrack, scale, saveHovered, showPlus: false);
            if (saveClicked)
            {
                string designToSave = editingAssignmentUseDesign ? editingAssignmentDesignBuffer : null;
                plugin.Configuration.CharacterAssignments[editingAssignmentKey] =
                    BuildCharacterAssignmentValue(editingAssignmentValue, designToSave);
                plugin.Configuration.Save();
                editingAssignmentKey = "";
                editingAssignmentValue = "";
                editingAssignmentUseDesign = false;
                editingAssignmentDesignBuffer = "";
            }

            ImGui.SetCursorScreenPos(new Vector2(origin.X, max.Y + 10f * scale));
            ImGui.Dummy(new Vector2(0, 0));
        }

        // COMPOSER PANE, same silhouette as bond plates but darker, no gold
        // hairline; a DRAFTING kicker. Builds an assignment via two halves.
        private void DrawAssignmentComposerCard(ImDrawListPtr dl, float scale, double time,
            List<string> knownRealCharacters)
        {
            float availW = ImGui.GetContentRegionAvail().X;
            float chamfer = 8f * scale;
            var origin = ImGui.GetCursorScreenPos();

            var newSelectedChar = plugin.Characters.FirstOrDefault(c => c.Name == newCSCharacterBuffer);
            bool showDesign = newSelectedChar != null && newSelectedChar.Designs.Any();

            // Pre-measure. Header rows: kicker line + row1 (in-game / cs+) +
            // row2 (manual input on left, conditional design pill on right).
            // The design pill stacks UNDER the cs+ pill - no separate full-
            // width design row, no toggle.
            float headerH = 88f * scale;
            float pillsRowH = 44f * scale;
            float plateH = headerH + pillsRowH;
            var min = origin;
            var max = origin + new Vector2(availW, plateH);

            // ── Card silhouette ── (one notch darker than saved cards, "draft")
            Boutique.FillSlip(dl, min, max, chamfer,
                Boutique.U32(Boutique.WithAlpha(Boutique.Surface0, 0.55f)));
            Span<Vector2> pts = stackalloc Vector2[6];
            Boutique.BuildSlipPolygon(min, max, chamfer, pts);
            for (int s = 0; s < 6; s++) dl.PathLineTo(pts[s]);
            dl.PathStroke(Boutique.U32(Boutique.WithAlpha(Boutique.BorderSoft, 0.55f)),
                ImDrawFlags.Closed, 1f * scale);

            // DRAFTING kicker centred at top edge
            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                float trackPx = 1.4f * scale;
                string kicker = "DRAFTING";
                float kw = Boutique.MeasureTrackedText(kicker, trackPx);
                Boutique.DrawTrackedText(dl,
                    new Vector2(min.X + (availW - kw) * 0.5f, min.Y + 6f * scale),
                    kicker, Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.70f)), trackPx);
            }

            float sidePadX = 14f * scale;
            float centerW = 28f * scale;
            float halfW = (availW - sidePadX * 2 - centerW) * 0.5f;
            float leftMinX = min.X + sidePadX;
            float rightMaxX = max.X - sidePadX;
            float rightMinX = rightMaxX - halfW;
            float centerMidX = min.X + availW * 0.5f;

            float kickerY = min.Y + 22f * scale;
            float row1Y = min.Y + 36f * scale;
            float row2Y = min.Y + 64f * scale;

            // Seal + kickers
            float sealSide = 6f * scale;
            dl.AddRectFilled(
                new Vector2(leftMinX, kickerY + 4f * scale),
                new Vector2(leftMinX + sealSide, kickerY + 4f * scale + sealSide),
                Boutique.U32(Boutique.WithAlpha(Boutique.Green, 0.60f)));
            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                float trackPx = 1.2f * scale;
                Boutique.DrawTrackedText(dl,
                    new Vector2(leftMinX + sealSide + 6f * scale, kickerY),
                    "IN-GAME", Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.85f)), trackPx);
                string csKicker = "CS+";
                float ckw = Boutique.MeasureTrackedText(csKicker, trackPx);
                Boutique.DrawTrackedText(dl,
                    new Vector2(rightMaxX - ckw, kickerY),
                    csKicker, Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.85f)), trackPx);
            }

            // ── LEFT half: in-game character ──
            // Row 1: SortPill of known characters (or placeholder if empty)
            int curRealIdx = knownRealCharacters.FindIndex(s => s == newRealCharacterBuffer);
            ImGui.SetCursorScreenPos(new Vector2(leftMinX, row1Y));
            if (knownRealCharacters.Count > 0)
            {
                int picked = Boutique.SortPill("ca.new.real",
                    knownRealCharacters.Count == 0 ? "EMPTY" : "PICK",
                    curRealIdx, knownRealCharacters, halfW, scale);
                if (picked >= 0) newRealCharacterBuffer = knownRealCharacters[picked];
            }
            else
            {
                // No known characters, show a static "type below" cue
                using (Plugin.Instance?.OutfitBody12?.Push())
                {
                    dl.AddText(new Vector2(leftMinX, row1Y + 5f * scale),
                        Boutique.U32(Boutique.TextFaint),
                        "No remembered names, type below");
                }
            }

            // Row 2: manual text input fallback
            ImGui.SetCursorScreenPos(new Vector2(leftMinX, row2Y));
            string realBuf = newRealCharacterBuffer;
            if (Boutique.DrawBoutiqueTextInput("##ca.new.realManual",
                ref realBuf, 100, halfW, "First Last@World"))
            {
                newRealCharacterBuffer = realBuf;
            }

            // ── CENTRE binding (lights when both sides resolved) ──
            bool bothSet = !string.IsNullOrWhiteSpace(newRealCharacterBuffer)
                           && !string.IsNullOrWhiteSpace(newCSCharacterBuffer);
            DrawBondBinding(dl, centerMidX, min.Y + 18f * scale, max.Y - pillsRowH - 6f * scale,
                bothSet, time, scale);

            // ── RIGHT half: CS+ SortPill at row1, optional Design SortPill at row2
            var csOptions = new List<string> { "None" };
            foreach (var c in plugin.Characters.OrderBy(c => c.Name))
                csOptions.Add(c.Name);
            int curCsIdx = csOptions.FindIndex(s => s == newCSCharacterBuffer);
            ImGui.SetCursorScreenPos(new Vector2(rightMinX, row1Y));
            int pickedCs = Boutique.SortPill("ca.new.cs", "CS+",
                curCsIdx, csOptions, halfW, scale);
            if (pickedCs >= 0)
            {
                string newName = csOptions[pickedCs];
                if (newName != newCSCharacterBuffer)
                {
                    newCSCharacterBuffer = newName;
                    newAssignmentUseDesign = false;
                    newAssignmentDesignBuffer = "";
                }
            }

            // Design pill stacks UNDER the CS+ pill (same X, row2). The first
            // option is "(no design)" which clears useDesign + buffer; picking
            // any real design sets useDesign = true. No separate toggle.
            if (showDesign)
            {
                var designOptions = new List<string> { "(no design)" };
                foreach (var d in newSelectedChar!.Designs.OrderBy(d => d.Name))
                    designOptions.Add(d.Name);
                int curDesignIdx = newAssignmentUseDesign && !string.IsNullOrWhiteSpace(newAssignmentDesignBuffer)
                    ? designOptions.FindIndex(s => s == newAssignmentDesignBuffer)
                    : 0;
                if (curDesignIdx < 0) curDesignIdx = 0;
                ImGui.SetCursorScreenPos(new Vector2(rightMinX, row2Y));
                int pickedD = Boutique.SortPill("ca.new.design", "DESIGN",
                    curDesignIdx, designOptions, halfW, scale);
                if (pickedD >= 0)
                {
                    if (pickedD == 0)
                    {
                        newAssignmentUseDesign = false;
                        newAssignmentDesignBuffer = "";
                    }
                    else
                    {
                        newAssignmentUseDesign = true;
                        newAssignmentDesignBuffer = designOptions[pickedD];
                    }
                }
            }

            // ── Pills row: status + ADD gold pill ──
            float pillsY = max.Y - pillsRowH + 8f * scale;
            bool exists = !string.IsNullOrWhiteSpace(newRealCharacterBuffer)
                          && plugin.Configuration.CharacterAssignments.ContainsKey(newRealCharacterBuffer);
            bool canAdd = !string.IsNullOrWhiteSpace(newRealCharacterBuffer) &&
                          !string.IsNullOrWhiteSpace(newCSCharacterBuffer) &&
                          !exists;

            float addPillTrack = 1.6f * scale;
            var addPillSize = Boutique.DrawGoldPillSize("ADD", addPillTrack, scale);
            addPillSize.X = MathF.Max(addPillSize.X, 84f * scale);

            // Status text on the left of the pill
            string? statusText = null;
            Vector4 statusCol = Boutique.WithAlpha(Boutique.Red, 0.75f);
            if (exists) statusText = "ASSIGNMENT EXISTS";
            else if (string.IsNullOrWhiteSpace(newRealCharacterBuffer) || string.IsNullOrWhiteSpace(newCSCharacterBuffer))
            {
                statusText = "PICK BOTH SIDES";
                statusCol = Boutique.TextFaint;
            }

            if (statusText != null)
            {
                using (Plugin.Instance?.OswaldSemi9?.Push())
                {
                    float trackPx = 1.2f * scale;
                    Boutique.DrawTrackedText(dl,
                        new Vector2(leftMinX, pillsY + 8f * scale),
                        statusText, Boutique.U32(statusCol), trackPx);
                }
            }

            // ADD pill anchored right
            var addPillMin = new Vector2(rightMaxX - addPillSize.X, pillsY);
            var addPillMax = addPillMin + addPillSize;
            ImGui.SetCursorScreenPos(addPillMin);
            bool addClicked = ImGui.InvisibleButton("##ca.new.add", addPillSize);
            bool addHovered = ImGui.IsItemHovered() && canAdd;
            if (canAdd)
            {
                Boutique.DrawGoldPill(dl, addPillMin, addPillMax, "ADD",
                    addPillTrack, scale, addHovered, showPlus: true);
            }
            else
            {
                // Disabled pill: faded gold
                Boutique.FillSlip(dl, addPillMin, addPillMax, Boutique.ChamSm * scale,
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.18f)));
                using (Plugin.Instance?.OswaldSemi11?.Push())
                {
                    float labelW = Boutique.MeasureTrackedText("ADD", addPillTrack);
                    var inkPos = new Vector2(
                        addPillMin.X + (addPillSize.X - labelW) * 0.5f,
                        addPillMin.Y + (addPillSize.Y - ImGui.GetFontSize()) * 0.5f);
                    Boutique.DrawTrackedText(dl, inkPos, "ADD",
                        Boutique.U32(Boutique.TextFaint), addPillTrack);
                }
            }
            if (addClicked && canAdd)
            {
                string designToSave = newAssignmentUseDesign ? newAssignmentDesignBuffer : null;
                plugin.Configuration.CharacterAssignments[newRealCharacterBuffer] =
                    BuildCharacterAssignmentValue(newCSCharacterBuffer, designToSave);
                plugin.AchievementTracker?.OnAssignmentSet();
                plugin.AchievementTracker?.CheckAssignmentCount();
                plugin.Configuration.Save();
                Plugin.Log.Debug($"[CharacterAssignment] Added: {newRealCharacterBuffer} -> {BuildCharacterAssignmentValue(newCSCharacterBuffer, designToSave)}");
                newRealCharacterBuffer = "";
                newCSCharacterBuffer = "";
                newAssignmentUseDesign = false;
                newAssignmentDesignBuffer = "";
            }

            ImGui.SetCursorScreenPos(new Vector2(origin.X, max.Y + 10f * scale));
            ImGui.Dummy(new Vector2(0, 0));
        }

        // KNOWN-CHARACTER PLATE, slim 32 px row, 1 px hairlines top/bottom,
        // green seal + name + dim world + tiny X glyph at the right.
        // Returns true when the X is clicked.
        private bool DrawKnownCharacterPlate(ImDrawListPtr dl, string physicalKey,
            string csMapping, bool isCurrent, float scale)
        {
            float availW = ImGui.GetContentRegionAvail().X;
            float plateH = 32f * scale;
            var origin = ImGui.GetCursorScreenPos();
            var min = origin;
            var max = origin + new Vector2(availW, plateH);

            dl.AddLine(new Vector2(min.X, min.Y),
                       new Vector2(max.X, min.Y),
                       Boutique.U32(Boutique.WithAlpha(Boutique.BorderSoft, 0.45f)),
                       1f * scale);

            // X hit rect on the right
            float padX = 10f * scale;
            float xSize = 16f * scale;
            float midY = min.Y + plateH * 0.5f;
            var xMin = new Vector2(max.X - padX - xSize, midY - xSize * 0.5f);
            ImGui.SetCursorScreenPos(xMin);
            bool xClicked = ImGui.InvisibleButton($"##ca.knownX_{physicalKey}",
                new Vector2(xSize, xSize));
            bool xHovered = ImGui.IsItemHovered();
            if (xHovered) Boutique.Tooltip($"Remove {physicalKey} from known characters");

            // Whole-row hover wash (subtle)
            bool rowHovered = ImGui.IsMouseHoveringRect(min, max) && !ImGui.IsAnyItemActive();
            if (rowHovered)
                dl.AddRectFilled(min, max, Boutique.U32(new Vector4(1f, 1f, 1f, 0.020f)));

            // Green seal
            float sealSide = 5f * scale;
            dl.AddRectFilled(
                new Vector2(min.X + padX, midY - sealSide * 0.5f),
                new Vector2(min.X + padX + sealSide, midY + sealSide * 0.5f),
                Boutique.U32(Boutique.WithAlpha(Boutique.Green, isCurrent ? 0.85f : 0.55f)));

            // Parse name@world
            string firstLast = physicalKey;
            string world = "";
            int atIdx = physicalKey.IndexOf('@');
            if (atIdx > 0)
            {
                firstLast = physicalKey.Substring(0, atIdx);
                world = "@" + physicalKey.Substring(atIdx + 1);
            }

            float textX = min.X + padX + sealSide + 8f * scale;
            float textY = midY;
            using (Plugin.Instance?.OutfitMed13?.Push())
            {
                var sz = ImGui.CalcTextSize(firstLast);
                Vector4 nameCol = isCurrent ? Boutique.GoldWarm : Boutique.Text;
                dl.AddText(new Vector2(textX, textY - sz.Y * 0.5f),
                    Boutique.U32(nameCol), firstLast);
                textX += sz.X + 4f * scale;
            }
            using (Plugin.Instance?.OutfitBody12?.Push())
            {
                var sz = ImGui.CalcTextSize(world);
                dl.AddText(new Vector2(textX, textY - sz.Y * 0.5f),
                    Boutique.U32(Boutique.TextDim), world);
                textX += sz.X + 6f * scale;
            }
            if (isCurrent)
            {
                using (Plugin.Instance?.OutfitBody12?.Push())
                {
                    string tag = "(currently logged in)";
                    var sz = ImGui.CalcTextSize(tag);
                    dl.AddText(new Vector2(textX, textY - sz.Y * 0.5f),
                        Boutique.U32(Boutique.WithAlpha(Boutique.Green, 0.80f)), tag);
                }
            }

            // X glyph
            string xGlyph = FontAwesomeIcon.Times.ToIconString();
            DrawIconCentered(dl, xMin, xSize, xGlyph,
                xHovered ? Boutique.Red : Boutique.TextGhost, scale);

            // Bottom hairline
            dl.AddLine(new Vector2(min.X, max.Y),
                       new Vector2(max.X, max.Y),
                       Boutique.U32(Boutique.WithAlpha(Boutique.BorderSoft, 0.45f)),
                       1f * scale);

            ImGui.SetCursorScreenPos(new Vector2(origin.X, max.Y));
            ImGui.Dummy(new Vector2(0, 0));
            return xClicked;
        }

        // Vertical gold-deep hairline + small lozenge clasp in the centre of
        // a bond/composer card. Pulses gently when "active" (hover or both
        // halves resolved in the composer).
        private static void DrawBondBinding(ImDrawListPtr dl, float midX, float topY, float botY,
            bool active, double time, float scale)
        {
            float lineThick = 2f * scale;
            float lineLeft = midX - lineThick * 0.5f;
            float midPointY = (topY + botY) * 0.5f;
            // Three slices for the gradient effect
            var topCol = Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, active ? 0.45f : 0.30f));
            var midCol = Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, active ? 0.85f : 0.55f));
            var botCol = Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, active ? 0.45f : 0.30f));
            dl.AddRectFilledMultiColor(
                new Vector2(lineLeft, topY),
                new Vector2(lineLeft + lineThick, midPointY),
                topCol, topCol, midCol, midCol);
            dl.AddRectFilledMultiColor(
                new Vector2(lineLeft, midPointY),
                new Vector2(lineLeft + lineThick, botY),
                midCol, midCol, botCol, botCol);

            // Lozenge clasp at midpoint, pulses when active
            float pulse = active
                ? 0.9f + 0.12f * MathF.Sin((float)time * 2.4f)
                : 1f;
            float dSize = 5f * scale * pulse;
            uint clasp = Boutique.U32(active ? Boutique.Gold : Boutique.WithAlpha(Boutique.GoldDeep, 0.65f));
            dl.AddQuadFilled(
                new Vector2(midX, midPointY - dSize),
                new Vector2(midX + dSize, midPointY),
                new Vector2(midX, midPointY + dSize),
                new Vector2(midX - dSize, midPointY),
                clasp);
        }

        // Helper: draw a FontAwesome glyph centred in a box.
        private static void DrawIconCentered(ImDrawListPtr dl, Vector2 min, float side,
            string glyph, Vector4 colour, float scale)
        {
            ImGui.PushFont(UiBuilder.IconFont);
            var sz = ImGui.CalcTextSize(glyph);
            ImGui.PopFont();
            float fs = MathF.Max(8f, side * 0.65f);
            float ratio = fs / UiBuilder.IconFont.FontSize;
            var pos = new Vector2(
                min.X + (side - sz.X * ratio) * 0.5f,
                min.Y + (side - sz.Y * ratio) * 0.5f);
            dl.AddText(UiBuilder.IconFont, fs, pos, Boutique.U32(colour), glyph);
        }

        // Helper: truncate a string with ellipsis to fit within maxW (current font).
        private static string TruncateToWidth(string text, float maxW)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sz = ImGui.CalcTextSize(text);
            if (sz.X <= maxW) return text;
            string ell = "...";
            float ellW = ImGui.CalcTextSize(ell).X;
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (ImGui.CalcTextSize(text.Substring(0, mid)).X + ellW <= maxW) lo = mid;
                else hi = mid - 1;
            }
            return text.Substring(0, lo) + ell;
        }

        // Prunes a physical character from every list that tracks "known players": the recently-used
        // dictionary, any matching Character Assignment, and the Gallery Main Character if it points
        // to this entry. Leaves individual Character.LastInGameName fields alone - those belong to
        // CS+ characters the user still owns.
        private void RemoveKnownInGameCharacter(string physicalKey)
        {
            bool changed = false;

            if (plugin.Configuration.LastUsedCharacterByPlayer.Remove(physicalKey))
                changed = true;

            if (plugin.Configuration.CharacterAssignments.Remove(physicalKey))
                changed = true;

            if (string.Equals(plugin.Configuration.GalleryMainCharacter, physicalKey, StringComparison.Ordinal))
            {
                plugin.Configuration.GalleryMainCharacter = null;
                changed = true;
            }

            if (changed)
            {
                plugin.Configuration.Save();
                Plugin.Log.Info($"[Settings] Removed known in-game character: {physicalKey}");
            }
        }

        // Job assignment UI state, composer
        private int newJobAssignmentType = 0; // 0 = Specific Job, 1 = Role
        private int newJobAssignmentJobIndex = 0;
        private int newJobAssignmentRoleIndex = 0;
        private string newJobAssignmentCharacterBuffer = "";
        private bool newJobAssignmentUseDesign = false;
        private string newJobAssignmentDesignBuffer = "";

        // Job assignment UI state, editor (live edit of an existing entry)
        private string editingJobAssignmentKey = "";
        private string editingJobAssignmentCharacter = "";
        private bool editingJobAssignmentUseDesign = false;
        private string editingJobAssignmentDesignBuffer = "";

        // Job data for UI
        private static readonly (uint Id, string Name, string Role)[] JobData = new[]
        {
            // Tanks
            (19u, "Paladin", "Tank"), (21u, "Warrior", "Tank"), (32u, "Dark Knight", "Tank"), (37u, "Gunbreaker", "Tank"),
            // Healers
            (24u, "White Mage", "Healer"), (28u, "Scholar", "Healer"), (33u, "Astrologian", "Healer"), (40u, "Sage", "Healer"),
            // Melee DPS
            (20u, "Monk", "Melee"), (22u, "Dragoon", "Melee"), (30u, "Ninja", "Melee"), (34u, "Samurai", "Melee"), (39u, "Reaper", "Melee"), (41u, "Viper", "Melee"),
            // Ranged Physical DPS
            (23u, "Bard", "Ranged"), (31u, "Machinist", "Ranged"), (38u, "Dancer", "Ranged"),
            // Caster DPS
            (25u, "Black Mage", "Caster"), (27u, "Summoner", "Caster"), (35u, "Red Mage", "Caster"), (36u, "Blue Mage", "Caster"), (42u, "Pictomancer", "Caster"),
            // Crafters
            (8u, "Carpenter", "Crafter"), (9u, "Blacksmith", "Crafter"), (10u, "Armorer", "Crafter"), (11u, "Goldsmith", "Crafter"),
            (12u, "Leatherworker", "Crafter"), (13u, "Weaver", "Crafter"), (14u, "Alchemist", "Crafter"), (15u, "Culinarian", "Crafter"),
            // Gatherers
            (16u, "Miner", "Gatherer"), (17u, "Botanist", "Gatherer"), (18u, "Fisher", "Gatherer")
        };

        private static readonly string[] RoleNames = new[] { "Tank", "Healer", "Melee", "Ranged", "Caster", "Crafter", "Gatherer" };

        private void DrawJobAssignmentSettings()
        {
            float scale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);
            float toggleW = 42f * scale;

            // Job-based switching toggle
            Boutique.SettingRow("ja.enable", "Enable Job-Based Character Switching",
                "Automatically switch CS+ character/design when you change jobs in-game. Job-specific assignments take priority over role assignments.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.EnableJobAssignments;
                    if (Boutique.TogglePill("ja.enable", ref v, scale))
                    {
                        plugin.Configuration.EnableJobAssignments = v;
                        plugin.Configuration.Save();
                    }
                });

            bool enableJobAssignments = plugin.Configuration.EnableJobAssignments;
            if (enableJobAssignments)
            {
                Boutique.Callout(Boutique.CalloutKind.Warning, FontAwesomeIcon.ExclamationTriangle,
                    "Conflicts with Glamourer Automations",
                    "Both features trigger on job change and will fight each other. Disable Glamourer Automations if using Job-Based Switching, or vice versa.",
                    scale);
            }

            // Gearset assignments toggle
            Boutique.SettingRow("ja.gearset", "Enable Gearset Assignments",
                "Allow assigning a gearset to each character/design. When applied, CS+ will automatically switch to that gearset. Configure gearsets in the Add/Edit Character or Design forms.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.EnableGearsetAssignments;
                    if (Boutique.TogglePill("ja.gearset", ref v, scale))
                    {
                        plugin.Configuration.EnableGearsetAssignments = v;
                        plugin.Configuration.Save();
                        if (v) plugin.AchievementTracker?.OnGearsetAssignmentsEnabled();
                    }
                });

            bool enableGearsetAssignments = plugin.Configuration.EnableGearsetAssignments;
            if (!enableJobAssignments && !enableGearsetAssignments)
            {
                Boutique.Callout(Boutique.CalloutKind.Info, FontAwesomeIcon.InfoCircle,
                    "Nothing to configure",
                    "Enable a feature above to configure job or gearset assignments.",
                    scale);
                return;
            }

            if (!enableJobAssignments)
            {
                ImGui.Dummy(new Vector2(0, 6f * scale));
                return;
            }

            // Intro callout for the duty-plate UI below
            Boutique.Callout(Boutique.CalloutKind.Info, FontAwesomeIcon.UserCheck,
                "Job Assignments",
                "Bind a CS+ character (and an optional design) to a specific job or to a whole role. When you change jobs in-game, CS+ applies whichever match is most specific.",
                scale);

            var dl = ImGui.GetWindowDrawList();
            double time = ImGui.GetTime();

            // ── Composer pane (always at top, drafting a new duty plate) ──
            DrawJobComposerCard(dl, scale, time);

            // ── Editor card (active assignment in edit mode) ──
            if (!string.IsNullOrEmpty(editingJobAssignmentKey))
            {
                DrawJobEditorCard(dl, scale, time);
            }

            // ── Duty-plate stack (existing assignments) ──
            string? assignmentToRemove = null;
            foreach (var assignment in plugin.Configuration.JobAssignments.ToList())
            {
                bool editClicked, removeClicked;
                DrawJobBondPlate(dl, assignment.Key, assignment.Value, scale, time,
                    out editClicked, out removeClicked);
                if (editClicked)
                {
                    editingJobAssignmentKey = assignment.Key;
                    var (charName, designName) = plugin.ParseJobAssignment(assignment.Value);
                    editingJobAssignmentCharacter = charName ?? "";
                    editingJobAssignmentUseDesign = !string.IsNullOrEmpty(designName);
                    editingJobAssignmentDesignBuffer = designName ?? "";
                }
                if (removeClicked) assignmentToRemove = assignment.Key;
            }
            if (assignmentToRemove != null)
            {
                plugin.Configuration.JobAssignments.Remove(assignmentToRemove);
                plugin.Configuration.Save();
            }

            // Empty-state hint
            if (plugin.Configuration.JobAssignments.Count == 0 && string.IsNullOrEmpty(editingJobAssignmentKey))
            {
                ImGui.Dummy(new Vector2(0, 4f * scale));
                Boutique.Callout(Boutique.CalloutKind.Info, FontAwesomeIcon.Lightbulb,
                    "No duties bound yet",
                    "Use the composer above to bind a job or role to a CS+ identity.",
                    scale);
            }

            // Priority note
            Boutique.Callout(Boutique.CalloutKind.Info, FontAwesomeIcon.InfoCircle,
                "Priority order",
                "Job-specific assignments take priority over role assignments. If both 'Reapply Last Design on Job Change' and Job Assignments are enabled, Job Assignments are checked first.",
                scale);

            ImGui.Dummy(new Vector2(0, 6f * scale));
        }

        // Role colour palette, the ONE memorable mark on each duty plate.
        // Used for the 3 px left stripe + chip border + lozenge-clasp tint.
        private static Vector4 RoleColour(string role) => role switch
        {
            "Tank"     => new Vector4(0.24f, 0.42f, 0.79f, 1f),
            "Healer"   => new Vector4(0.29f, 0.87f, 0.50f, 1f),
            "Melee"    => new Vector4(0.90f, 0.28f, 0.30f, 1f),
            "Ranged"   => new Vector4(0.95f, 0.66f, 0.23f, 1f),
            "Caster"   => new Vector4(0.64f, 0.56f, 1.00f, 1f),
            "Crafter"  => new Vector4(0.77f, 0.60f, 0.42f, 1f),
            "Gatherer" => new Vector4(0.50f, 0.69f, 0.41f, 1f),
            _           => Boutique.GoldDeep,
        };

        // Single-letter glyph for role chips. For specific jobs we use the
        // first letter of the job name (collisions are disambiguated by the
        // role-coloured stripe + chip border).
        private static string RoleGlyph(string role) => role switch
        {
            "Tank"     => "T",
            "Healer"   => "H",
            "Melee"    => "M",
            "Ranged"   => "R",
            "Caster"   => "C",
            "Crafter"  => "X",
            "Gatherer" => "G",
            _           => "?",
        };

        // Resolve a saved JobAssignments key into (kicker, displayName, role).
        // Keys come in two shapes: "Job_<id>" or "Role_<roleName>".
        private static (string Kicker, string Name, string Role) ParseJobAssignmentKey(string key)
        {
            if (key.StartsWith("Job_", StringComparison.Ordinal))
            {
                var idStr = key.Substring(4);
                if (uint.TryParse(idStr, out var jobId))
                {
                    var info = JobData.FirstOrDefault(j => j.Id == jobId);
                    if (info.Name != null)
                        return ("JOB", info.Name, info.Role);
                }
                return ("JOB", $"Job {idStr}", "");
            }
            if (key.StartsWith("Role_", StringComparison.Ordinal))
            {
                var roleName = key.Substring(5);
                return ("ROLE", roleName, roleName);
            }
            return ("JOB", key, "");
        }

        // DUTY PLATE, a saved Job Assignment. Same silhouette as the bond
        // plate (8 px TR+BL chamfer slip, 64 px tall) but the LEFT half is a
        // job-or-role badge: 3 px role-coloured stripe + chamfered chip with
        // mono-glyph + kicker (JOB/ROLE) + name. Right half is the CS+ side.
        private void DrawJobBondPlate(ImDrawListPtr dl, string assignmentKey, string value,
            float scale, double time, out bool editClicked, out bool removeClicked)
        {
            editClicked = false;
            removeClicked = false;

            float availW = ImGui.GetContentRegionAvail().X;
            var origin = ImGui.GetCursorScreenPos();
            float plateH = 64f * scale;
            float chamfer = 8f * scale;
            var min = origin;
            var max = origin + new Vector2(availW, plateH);

            // ── Card silhouette ──
            Boutique.FillSlip(dl, min, max, chamfer,
                Boutique.U32(Boutique.WithAlpha(Boutique.Surface1, 0.55f)));
            Span<Vector2> pts = stackalloc Vector2[6];
            Boutique.BuildSlipPolygon(min, max, chamfer, pts);
            for (int s = 0; s < 6; s++) dl.PathLineTo(pts[s]);
            dl.PathStroke(Boutique.U32(Boutique.BorderSoft), ImDrawFlags.Closed, 1f * scale);
            dl.AddRectFilled(
                new Vector2(min.X, min.Y),
                new Vector2(max.X - chamfer, min.Y + 2f * scale),
                Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.55f)));

            // Parse key + value
            var (kicker, displayName, role) = ParseJobAssignmentKey(assignmentKey);
            Vector4 roleCol = RoleColour(role);
            string chipLetter = kicker == "ROLE"
                ? RoleGlyph(role)
                : (string.IsNullOrEmpty(displayName) ? "?" : displayName.Substring(0, 1).ToUpperInvariant());

            // 3 px role stripe along the straight portion of the left edge
            dl.AddRectFilled(
                new Vector2(min.X, min.Y),
                new Vector2(min.X + 3f * scale, max.Y - chamfer),
                Boutique.U32(roleCol));

            // Action affordances (top-right): Edit + Delete
            float actionsRightPad = 8f * scale;
            float actionTop = min.Y + 8f * scale;
            float actionSize = 18f * scale;
            float actionGap = 4f * scale;
            var deleteMin = new Vector2(max.X - actionsRightPad - actionSize, actionTop);
            var editMin = new Vector2(deleteMin.X - actionGap - actionSize, actionTop);

            ImGui.SetCursorScreenPos(deleteMin);
            removeClicked = ImGui.InvisibleButton($"##ja.del_{assignmentKey}", new Vector2(actionSize, actionSize));
            bool deleteHovered = ImGui.IsItemHovered();
            if (deleteHovered) Boutique.Tooltip($"Remove this {(kicker == "ROLE" ? "role" : "job")} assignment");

            ImGui.SetCursorScreenPos(editMin);
            editClicked = ImGui.InvisibleButton($"##ja.edit_{assignmentKey}", new Vector2(actionSize, actionSize));
            bool editHovered = ImGui.IsItemHovered();
            if (editHovered) Boutique.Tooltip($"Edit this {(kicker == "ROLE" ? "role" : "job")} assignment");

            bool cardHovered = ImGui.IsMouseHoveringRect(min, max) && !ImGui.IsAnyItemActive();
            if (cardHovered)
                dl.AddRectFilled(min, max, Boutique.U32(new Vector4(1f, 1f, 1f, 0.020f)));

            // Layout split
            float sidePadX = 14f * scale;
            float chipColW = 36f * scale;
            float centerW = 28f * scale;
            float halfW = (availW - sidePadX * 2 - centerW) * 0.5f;
            float leftMinX = min.X + sidePadX;
            float chipMinX = leftMinX + 4f * scale;
            float textColX = chipMinX + chipColW + 8f * scale;
            float leftMaxX = leftMinX + halfW;
            float rightMaxX = max.X - sidePadX;
            float rightMinX = rightMaxX - halfW;
            float centerMidX = min.X + availW * 0.5f;

            float rightTextRight = editMin.X - 8f * scale;
            float rightTextW = rightTextRight - rightMinX;
            if (rightTextW < 60f * scale) rightTextW = 60f * scale;

            // Job/role chip (28×28 chamfered)
            float chipSide = 28f * scale;
            float chipMidY = min.Y + plateH * 0.5f;
            var chipMn = new Vector2(chipMinX, chipMidY - chipSide * 0.5f);
            var chipMx = chipMn + new Vector2(chipSide, chipSide);
            Boutique.FillSlip(dl, chipMn, chipMx, 4f * scale,
                Boutique.U32(Boutique.WithAlpha(roleCol, 0.22f)));
            Boutique.StrokeSlip(dl, chipMn, chipMx, 4f * scale,
                Boutique.U32(Boutique.WithAlpha(roleCol, 0.70f)), 1f * scale);
            using (Plugin.Instance?.OswaldSemi13?.Push())
            {
                var letterSz = ImGui.CalcTextSize(chipLetter);
                dl.AddText(
                    new Vector2(chipMn.X + (chipSide - letterSz.X) * 0.5f,
                                chipMn.Y + (chipSide - letterSz.Y) * 0.5f),
                    Boutique.U32(Boutique.GoldBright), chipLetter);
            }

            // Kicker + name
            float kickerY = min.Y + 14f * scale;
            float nameY = min.Y + 30f * scale;
            float subY = min.Y + 46f * scale;

            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                float trackPx = 1.2f * scale;
                Boutique.DrawTrackedText(dl,
                    new Vector2(textColX, kickerY),
                    kicker, Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.85f)), trackPx);
            }
            float nameMaxW = leftMaxX - textColX;
            using (Plugin.Instance?.OutfitMed13?.Push())
            {
                string display = TruncateToWidth(displayName, nameMaxW);
                dl.AddText(new Vector2(textColX, nameY),
                    Boutique.U32(Boutique.Text), display);
            }
            // Sub-line: for specific jobs show the role; for role rows show "ALL <role> JOBS"
            string sub = kicker == "ROLE"
                ? $"All {role} jobs"
                : (string.IsNullOrEmpty(role) ? "" : $"({role})");
            if (!string.IsNullOrEmpty(sub))
            {
                using (Plugin.Instance?.OutfitBody12?.Push())
                {
                    string display = TruncateToWidth(sub, nameMaxW);
                    dl.AddText(new Vector2(textColX, subY),
                        Boutique.U32(Boutique.TextDim), display);
                }
            }

            // Centre binding (vertical line + lozenge clasp). Lozenge tinted
            // by the role colour, same signature mark family as the stripe.
            DrawJobBondBinding(dl, centerMidX, min.Y + 10f * scale, max.Y - 10f * scale,
                roleCol, cardHovered, time, scale);

            // ── RIGHT half: CS+ ──
            var (charName, designName) = plugin.ParseJobAssignment(value);
            string csName = charName ?? "(invalid)";
            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                float trackPx = 1.2f * scale;
                string csKicker = "CS+";
                float kickerW = Boutique.MeasureTrackedText(csKicker, trackPx);
                Boutique.DrawTrackedText(dl,
                    new Vector2(rightTextRight - kickerW, kickerY),
                    csKicker, Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.85f)), trackPx);
            }
            using (Plugin.Instance?.OutfitMed13?.Push())
            {
                string display = TruncateToWidth(csName, rightTextW);
                var nameSz = ImGui.CalcTextSize(display);
                dl.AddText(new Vector2(rightTextRight - nameSz.X, nameY),
                    Boutique.U32(Boutique.GoldWarm), display);
            }
            using (Plugin.Instance?.OutfitBody12?.Push())
            {
                string display;
                Vector4 tagCol;
                if (!string.IsNullOrEmpty(designName))
                {
                    display = $"({designName})";
                    tagCol = Boutique.TextDim;
                }
                else
                {
                    display = "any design";
                    tagCol = Boutique.TextFaint;
                }
                display = TruncateToWidth(display, rightTextW);
                var tagSz = ImGui.CalcTextSize(display);
                dl.AddText(new Vector2(rightTextRight - tagSz.X, subY),
                    Boutique.U32(tagCol), display);
            }

            // Edit + delete glyphs
            string editGlyph = FontAwesomeIcon.PencilAlt.ToIconString();
            DrawIconCentered(dl, editMin, actionSize, editGlyph,
                editHovered ? Boutique.GoldWarm : Boutique.TextGhost, scale);
            string xGlyph = FontAwesomeIcon.Times.ToIconString();
            DrawIconCentered(dl, deleteMin, actionSize, xGlyph,
                deleteHovered ? Boutique.Red : Boutique.TextGhost, scale);

            ImGui.SetCursorScreenPos(new Vector2(origin.X, max.Y + 8f * scale));
            ImGui.Dummy(new Vector2(0, 0));
        }

        // Vertical gold-deep hairline + lozenge clasp tinted by the role
        // colour. Same primitive family as DrawBondBinding but the lozenge
        // outer ring picks up the role hue so the central mark visually
        // ties left and right halves together.
        private static void DrawJobBondBinding(ImDrawListPtr dl, float midX, float topY, float botY,
            Vector4 roleCol, bool active, double time, float scale)
        {
            float lineThick = 2f * scale;
            float lineLeft = midX - lineThick * 0.5f;
            float midPointY = (topY + botY) * 0.5f;
            var topCol = Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, active ? 0.45f : 0.30f));
            var midCol = Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, active ? 0.85f : 0.55f));
            var botCol = Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, active ? 0.45f : 0.30f));
            dl.AddRectFilledMultiColor(
                new Vector2(lineLeft, topY),
                new Vector2(lineLeft + lineThick, midPointY),
                topCol, topCol, midCol, midCol);
            dl.AddRectFilledMultiColor(
                new Vector2(lineLeft, midPointY),
                new Vector2(lineLeft + lineThick, botY),
                midCol, midCol, botCol, botCol);

            float pulse = active ? 0.9f + 0.12f * MathF.Sin((float)time * 2.4f) : 1f;
            float dOuter = 6f * scale * pulse;
            float dInner = 3.2f * scale * pulse;
            uint outerCol = Boutique.U32(Boutique.WithAlpha(roleCol, active ? 0.95f : 0.70f));
            uint innerCol = Boutique.U32(active ? Boutique.GoldBright : Boutique.GoldWarm);
            dl.AddQuadFilled(
                new Vector2(midX, midPointY - dOuter),
                new Vector2(midX + dOuter, midPointY),
                new Vector2(midX, midPointY + dOuter),
                new Vector2(midX - dOuter, midPointY),
                outerCol);
            dl.AddQuadFilled(
                new Vector2(midX, midPointY - dInner),
                new Vector2(midX + dInner, midPointY),
                new Vector2(midX, midPointY + dInner),
                new Vector2(midX - dInner, midPointY),
                innerCol);
        }

        // EDITOR CARD, same silhouette as the duty plate but with a static
        // job/role badge on the left and editable CS+ + design SortPills on
        // the right. The role-colour stripe of the bound job paints the
        // left edge live as the user re-picks.
        private void DrawJobEditorCard(ImDrawListPtr dl, float scale, double time)
        {
            float availW = ImGui.GetContentRegionAvail().X;
            float chamfer = 8f * scale;
            var origin = ImGui.GetCursorScreenPos();

            var editChar = plugin.Configuration.Characters.FirstOrDefault(c => c.Name == editingJobAssignmentCharacter);
            bool showDesign = editChar != null && editChar.Designs.Any();
            float headerH = showDesign ? 92f * scale : 60f * scale;
            float pillsRowH = 44f * scale;
            float plateH = headerH + pillsRowH;
            var min = origin;
            var max = origin + new Vector2(availW, plateH);

            Boutique.FillSlip(dl, min, max, chamfer,
                Boutique.U32(Boutique.WithAlpha(Boutique.Surface1, 0.75f)));
            Span<Vector2> pts = stackalloc Vector2[6];
            Boutique.BuildSlipPolygon(min, max, chamfer, pts);
            for (int s = 0; s < 6; s++) dl.PathLineTo(pts[s]);
            dl.PathStroke(Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.85f)),
                ImDrawFlags.Closed, 1.5f * scale);
            dl.AddRectFilled(
                new Vector2(min.X, min.Y),
                new Vector2(max.X - chamfer, min.Y + 2f * scale),
                Boutique.U32(Boutique.Gold));

            // Resolve the job/role this row is bound to (so the stripe paints live)
            var (kicker, displayName, role) = ParseJobAssignmentKey(editingJobAssignmentKey);
            Vector4 roleCol = RoleColour(role);
            string chipLetter = kicker == "ROLE"
                ? RoleGlyph(role)
                : (string.IsNullOrEmpty(displayName) ? "?" : displayName.Substring(0, 1).ToUpperInvariant());

            dl.AddRectFilled(
                new Vector2(min.X, min.Y),
                new Vector2(min.X + 3f * scale, max.Y - chamfer),
                Boutique.U32(roleCol));

            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                float trackPx = 1.4f * scale;
                string editKicker = "EDITING";
                float kw = Boutique.MeasureTrackedText(editKicker, trackPx);
                Boutique.DrawTrackedText(dl,
                    new Vector2(min.X + (availW - kw) * 0.5f, min.Y + 6f * scale),
                    editKicker, Boutique.U32(Boutique.GoldWarm), trackPx);
            }

            float sidePadX = 14f * scale;
            float chipColW = 36f * scale;
            float centerW = 28f * scale;
            float halfW = (availW - sidePadX * 2 - centerW) * 0.5f;
            float leftMinX = min.X + sidePadX;
            float chipMinX = leftMinX + 4f * scale;
            float textColX = chipMinX + chipColW + 8f * scale;
            float rightMaxX = max.X - sidePadX;
            float rightMinX = rightMaxX - halfW;
            float centerMidX = min.X + availW * 0.5f;

            // Static job/role chip (same anatomy as the saved plate)
            float chipSide = 28f * scale;
            float chipMidY = min.Y + headerH * 0.5f;
            var chipMn = new Vector2(chipMinX, chipMidY - chipSide * 0.5f);
            var chipMx = chipMn + new Vector2(chipSide, chipSide);
            Boutique.FillSlip(dl, chipMn, chipMx, 4f * scale,
                Boutique.U32(Boutique.WithAlpha(roleCol, 0.22f)));
            Boutique.StrokeSlip(dl, chipMn, chipMx, 4f * scale,
                Boutique.U32(Boutique.WithAlpha(roleCol, 0.70f)), 1f * scale);
            using (Plugin.Instance?.OswaldSemi13?.Push())
            {
                var letterSz = ImGui.CalcTextSize(chipLetter);
                dl.AddText(
                    new Vector2(chipMn.X + (chipSide - letterSz.X) * 0.5f,
                                chipMn.Y + (chipSide - letterSz.Y) * 0.5f),
                    Boutique.U32(Boutique.GoldBright), chipLetter);
            }

            float kickerY = min.Y + 22f * scale;
            float nameY = min.Y + 38f * scale;
            float subY = min.Y + 54f * scale;

            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                float trackPx = 1.2f * scale;
                Boutique.DrawTrackedText(dl,
                    new Vector2(textColX, kickerY),
                    kicker, Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.85f)), trackPx);
            }
            using (Plugin.Instance?.OutfitMed13?.Push())
            {
                dl.AddText(new Vector2(textColX, nameY),
                    Boutique.U32(Boutique.Text),
                    TruncateToWidth(displayName, halfW - chipColW - 8f * scale));
            }
            string sub = kicker == "ROLE" ? $"All {role} jobs" : (string.IsNullOrEmpty(role) ? "" : $"({role})");
            if (!string.IsNullOrEmpty(sub))
            {
                using (Plugin.Instance?.OutfitBody12?.Push())
                {
                    dl.AddText(new Vector2(textColX, subY),
                        Boutique.U32(Boutique.TextDim),
                        TruncateToWidth(sub, halfW - chipColW - 8f * scale));
                }
            }

            DrawJobBondBinding(dl, centerMidX, min.Y + 18f * scale, min.Y + headerH - 4f * scale,
                roleCol, false, time, scale);

            // ── RIGHT: CS+ + design SortPills ──
            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                float trackPx = 1.2f * scale;
                string csKicker = "CS+";
                float kw = Boutique.MeasureTrackedText(csKicker, trackPx);
                Boutique.DrawTrackedText(dl,
                    new Vector2(rightMaxX - kw, kickerY),
                    csKicker, Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.85f)), trackPx);
            }

            var csOptions = new List<string>();
            foreach (var c in plugin.Configuration.Characters.OrderBy(c => c.Name))
                csOptions.Add(c.Name);
            int curCsIdx = csOptions.FindIndex(s => s == editingJobAssignmentCharacter);

            ImGui.SetCursorScreenPos(new Vector2(rightMinX, nameY - 4f * scale));
            int pickedCs = Boutique.SortPill("ja.edit.cs", "CS+",
                curCsIdx, csOptions, halfW, scale);
            if (pickedCs >= 0)
            {
                string newName = csOptions[pickedCs];
                if (newName != editingJobAssignmentCharacter)
                {
                    editingJobAssignmentCharacter = newName;
                    editingJobAssignmentUseDesign = false;
                    editingJobAssignmentDesignBuffer = "";
                }
            }

            if (showDesign)
            {
                var designOptions = new List<string> { "(no design)" };
                foreach (var d in editChar!.Designs.OrderBy(d => d.Name))
                    designOptions.Add(d.Name);
                int curDesignIdx = editingJobAssignmentUseDesign && !string.IsNullOrWhiteSpace(editingJobAssignmentDesignBuffer)
                    ? designOptions.FindIndex(s => s == editingJobAssignmentDesignBuffer)
                    : 0;
                if (curDesignIdx < 0) curDesignIdx = 0;
                float designPillY = nameY + 28f * scale;
                ImGui.SetCursorScreenPos(new Vector2(rightMinX, designPillY));
                int pickedD = Boutique.SortPill("ja.edit.design", "DESIGN",
                    curDesignIdx, designOptions, halfW, scale);
                if (pickedD >= 0)
                {
                    if (pickedD == 0)
                    {
                        editingJobAssignmentUseDesign = false;
                        editingJobAssignmentDesignBuffer = "";
                    }
                    else
                    {
                        editingJobAssignmentUseDesign = true;
                        editingJobAssignmentDesignBuffer = designOptions[pickedD];
                    }
                }
            }

            // ── Pills row: Cancel + Save ──
            float pillsY = max.Y - pillsRowH + 8f * scale;
            ImGui.SetCursorScreenPos(new Vector2(leftMinX, pillsY));
            if (Boutique.OutlineButton("ja.edit.cancel", "CANCEL", scale))
            {
                editingJobAssignmentKey = "";
                editingJobAssignmentCharacter = "";
                editingJobAssignmentUseDesign = false;
                editingJobAssignmentDesignBuffer = "";
            }

            float savePillTrack = 1.6f * scale;
            var savePillSize = Boutique.DrawGoldPillSize("SAVE", savePillTrack, scale);
            savePillSize.X = MathF.Max(savePillSize.X, 84f * scale);
            var savePillMin = new Vector2(rightMaxX - savePillSize.X, pillsY);
            var savePillMax = savePillMin + savePillSize;
            ImGui.SetCursorScreenPos(savePillMin);
            bool saveClicked = ImGui.InvisibleButton("##ja.edit.save", savePillSize);
            bool saveHovered = ImGui.IsItemHovered() && !string.IsNullOrWhiteSpace(editingJobAssignmentCharacter);
            if (!string.IsNullOrWhiteSpace(editingJobAssignmentCharacter))
            {
                Boutique.DrawGoldPill(dl, savePillMin, savePillMax, "SAVE",
                    savePillTrack, scale, saveHovered, showPlus: false);
            }
            else
            {
                Boutique.FillSlip(dl, savePillMin, savePillMax, Boutique.ChamSm * scale,
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.18f)));
                using (Plugin.Instance?.OswaldSemi11?.Push())
                {
                    float labelW = Boutique.MeasureTrackedText("SAVE", savePillTrack);
                    var inkPos = new Vector2(
                        savePillMin.X + (savePillSize.X - labelW) * 0.5f,
                        savePillMin.Y + (savePillSize.Y - ImGui.GetFontSize()) * 0.5f);
                    Boutique.DrawTrackedText(dl, inkPos, "SAVE",
                        Boutique.U32(Boutique.TextFaint), savePillTrack);
                }
            }
            if (saveClicked && !string.IsNullOrWhiteSpace(editingJobAssignmentCharacter))
            {
                string newValue = editingJobAssignmentUseDesign && !string.IsNullOrWhiteSpace(editingJobAssignmentDesignBuffer)
                    ? $"Design:{editingJobAssignmentCharacter}:{editingJobAssignmentDesignBuffer}"
                    : $"Character:{editingJobAssignmentCharacter}";
                plugin.Configuration.JobAssignments[editingJobAssignmentKey] = newValue;
                plugin.Configuration.Save();
                editingJobAssignmentKey = "";
                editingJobAssignmentCharacter = "";
                editingJobAssignmentUseDesign = false;
                editingJobAssignmentDesignBuffer = "";
            }

            ImGui.SetCursorScreenPos(new Vector2(origin.X, max.Y + 10f * scale));
            ImGui.Dummy(new Vector2(0, 0));
        }

        // COMPOSER PANE, same silhouette as a duty plate, darker. The top
        // row is a SEGMENTED CONTROL (SPECIFIC JOB / ROLE) so both modes
        // are visible at once, no wizard cascade. Below: source SortPill
        // (job or role) + CS+ SortPill + optional design SortPill stacked
        // under CS+. Footer row: status text + ADD gold pill.
        private void DrawJobComposerCard(ImDrawListPtr dl, float scale, double time)
        {
            float availW = ImGui.GetContentRegionAvail().X;
            float chamfer = 8f * scale;
            var origin = ImGui.GetCursorScreenPos();

            var newSelectedChar = plugin.Characters.FirstOrDefault(c => c.Name == newJobAssignmentCharacterBuffer);
            bool showDesign = newSelectedChar != null && newSelectedChar.Designs.Any();

            // Vertical layout: kicker row + segmented row + pills row + (design row?) + footer
            float headerH = showDesign ? 144f * scale : 110f * scale;
            float pillsRowH = 44f * scale;
            float plateH = headerH + pillsRowH;
            var min = origin;
            var max = origin + new Vector2(availW, plateH);

            // ── Card silhouette ──
            Boutique.FillSlip(dl, min, max, chamfer,
                Boutique.U32(Boutique.WithAlpha(Boutique.Surface0, 0.55f)));
            Span<Vector2> pts = stackalloc Vector2[6];
            Boutique.BuildSlipPolygon(min, max, chamfer, pts);
            for (int s = 0; s < 6; s++) dl.PathLineTo(pts[s]);
            dl.PathStroke(Boutique.U32(Boutique.WithAlpha(Boutique.BorderSoft, 0.55f)),
                ImDrawFlags.Closed, 1f * scale);

            // DRAFTING kicker centred
            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                float trackPx = 1.4f * scale;
                string kicker = "DRAFTING";
                float kw = Boutique.MeasureTrackedText(kicker, trackPx);
                Boutique.DrawTrackedText(dl,
                    new Vector2(min.X + (availW - kw) * 0.5f, min.Y + 6f * scale),
                    kicker, Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.70f)), trackPx);
            }

            // Resolve the role colour for the live preview stripe
            string previewRole = newJobAssignmentType == 0
                ? (JobData.ElementAtOrDefault(newJobAssignmentJobIndex).Role ?? "")
                : (RoleNames.ElementAtOrDefault(newJobAssignmentRoleIndex) ?? "");
            Vector4 previewRoleCol = RoleColour(previewRole);

            dl.AddRectFilled(
                new Vector2(min.X, min.Y),
                new Vector2(min.X + 3f * scale, max.Y - chamfer),
                Boutique.U32(Boutique.WithAlpha(previewRoleCol, 0.85f)));

            float sidePadX = 14f * scale;
            float centerW = 28f * scale;
            float halfW = (availW - sidePadX * 2 - centerW) * 0.5f;
            float leftMinX = min.X + sidePadX;
            float rightMaxX = max.X - sidePadX;
            float rightMinX = rightMaxX - halfW;
            float centerMidX = min.X + availW * 0.5f;

            // Row 1: segmented type tabs (SPECIFIC JOB / ROLE)
            float segY = min.Y + 24f * scale;
            float segH = 26f * scale;
            float segHalfW = (availW - sidePadX * 2) * 0.5f;
            DrawSegmentedTab(dl, "ja.new.type.job",
                new Vector2(leftMinX, segY),
                new Vector2(leftMinX + segHalfW - 1f * scale, segY + segH),
                "SPECIFIC JOB", newJobAssignmentType == 0, scale,
                () => newJobAssignmentType = 0);
            DrawSegmentedTab(dl, "ja.new.type.role",
                new Vector2(leftMinX + segHalfW + 1f * scale, segY),
                new Vector2(rightMaxX, segY + segH),
                "ROLE", newJobAssignmentType == 1, scale,
                () => newJobAssignmentType = 1);

            // Row 2: kickers (source on left, CS+ on right)
            float kickerY = segY + segH + 14f * scale;
            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                float trackPx = 1.2f * scale;
                string srcKicker = newJobAssignmentType == 0 ? "JOB" : "ROLE";
                Boutique.DrawTrackedText(dl,
                    new Vector2(leftMinX, kickerY),
                    srcKicker, Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.85f)), trackPx);
                string csKicker = "CS+";
                float ckw = Boutique.MeasureTrackedText(csKicker, trackPx);
                Boutique.DrawTrackedText(dl,
                    new Vector2(rightMaxX - ckw, kickerY),
                    csKicker, Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.85f)), trackPx);
            }

            // Row 3: source SortPill (left) + CS+ SortPill (right)
            float pillsY = kickerY + 14f * scale;

            // Source pill
            ImGui.SetCursorScreenPos(new Vector2(leftMinX, pillsY));
            if (newJobAssignmentType == 0)
            {
                var jobOptions = JobData.Select(j => $"{j.Name} ({j.Role})").ToList();
                int picked = Boutique.SortPill("ja.new.job", "JOB",
                    newJobAssignmentJobIndex, jobOptions, halfW, scale);
                if (picked >= 0) newJobAssignmentJobIndex = picked;
            }
            else
            {
                var roleOptions = RoleNames.ToList();
                int picked = Boutique.SortPill("ja.new.role", "ROLE",
                    newJobAssignmentRoleIndex, roleOptions, halfW, scale);
                if (picked >= 0) newJobAssignmentRoleIndex = picked;
            }

            // CS+ pill
            var csOptions = new List<string>();
            foreach (var c in plugin.Characters.OrderBy(c => c.Name))
                csOptions.Add(c.Name);
            int curCsIdx = csOptions.FindIndex(s => s == newJobAssignmentCharacterBuffer);
            ImGui.SetCursorScreenPos(new Vector2(rightMinX, pillsY));
            int pickedCs = Boutique.SortPill("ja.new.cs", "CS+",
                curCsIdx, csOptions, halfW, scale);
            if (pickedCs >= 0)
            {
                string newName = csOptions[pickedCs];
                if (newName != newJobAssignmentCharacterBuffer)
                {
                    newJobAssignmentCharacterBuffer = newName;
                    newJobAssignmentUseDesign = false;
                    newJobAssignmentDesignBuffer = "";
                }
            }

            // Centre binding spans the source/CS+ pill row + design row
            float bindTop = segY + segH + 4f * scale;
            float bindBot = pillsY + 26f * scale + (showDesign ? 30f * scale : 0f);
            bool bothSet = (newJobAssignmentType == 0
                    ? JobData.ElementAtOrDefault(newJobAssignmentJobIndex).Name != null
                    : !string.IsNullOrEmpty(RoleNames.ElementAtOrDefault(newJobAssignmentRoleIndex)))
                && !string.IsNullOrWhiteSpace(newJobAssignmentCharacterBuffer);
            DrawJobBondBinding(dl, centerMidX, bindTop, bindBot,
                previewRoleCol, bothSet, time, scale);

            // Row 4 (optional): design SortPill stacked under CS+
            if (showDesign)
            {
                var designOptions = new List<string> { "(no design)" };
                foreach (var d in newSelectedChar!.Designs.OrderBy(d => d.Name))
                    designOptions.Add(d.Name);
                int curDesignIdx = newJobAssignmentUseDesign && !string.IsNullOrWhiteSpace(newJobAssignmentDesignBuffer)
                    ? designOptions.FindIndex(s => s == newJobAssignmentDesignBuffer)
                    : 0;
                if (curDesignIdx < 0) curDesignIdx = 0;
                float designPillY = pillsY + 30f * scale;
                ImGui.SetCursorScreenPos(new Vector2(rightMinX, designPillY));
                int pickedD = Boutique.SortPill("ja.new.design", "DESIGN",
                    curDesignIdx, designOptions, halfW, scale);
                if (pickedD >= 0)
                {
                    if (pickedD == 0)
                    {
                        newJobAssignmentUseDesign = false;
                        newJobAssignmentDesignBuffer = "";
                    }
                    else
                    {
                        newJobAssignmentUseDesign = true;
                        newJobAssignmentDesignBuffer = designOptions[pickedD];
                    }
                }
            }

            // Footer row: status + ADD pill
            float footerY = max.Y - pillsRowH + 8f * scale;

            // Build proposed key + check for duplicates
            string proposedKey;
            bool sourceValid;
            if (newJobAssignmentType == 0)
            {
                var selJob = JobData.ElementAtOrDefault(newJobAssignmentJobIndex);
                proposedKey = $"Job_{selJob.Id}";
                sourceValid = selJob.Name != null;
            }
            else
            {
                string selRole = RoleNames.ElementAtOrDefault(newJobAssignmentRoleIndex) ?? "";
                proposedKey = $"Role_{selRole}";
                sourceValid = !string.IsNullOrEmpty(selRole);
            }
            bool exists = sourceValid && plugin.Configuration.JobAssignments.ContainsKey(proposedKey);
            bool canAdd = sourceValid && !exists && !string.IsNullOrWhiteSpace(newJobAssignmentCharacterBuffer);

            string? statusText = null;
            Vector4 statusCol = Boutique.TextFaint;
            if (exists)
            {
                statusText = "ASSIGNMENT EXISTS";
                statusCol = Boutique.WithAlpha(Boutique.Red, 0.75f);
            }
            else if (string.IsNullOrWhiteSpace(newJobAssignmentCharacterBuffer))
            {
                statusText = newJobAssignmentType == 0 ? "PICK JOB + CS+ CHARACTER" : "PICK ROLE + CS+ CHARACTER";
            }

            if (statusText != null)
            {
                using (Plugin.Instance?.OswaldSemi9?.Push())
                {
                    float trackPx = 1.2f * scale;
                    Boutique.DrawTrackedText(dl,
                        new Vector2(leftMinX, footerY + 8f * scale),
                        statusText, Boutique.U32(statusCol), trackPx);
                }
            }

            float addPillTrack = 1.6f * scale;
            var addPillSize = Boutique.DrawGoldPillSize("ADD", addPillTrack, scale);
            addPillSize.X = MathF.Max(addPillSize.X, 84f * scale);
            var addPillMin = new Vector2(rightMaxX - addPillSize.X, footerY);
            var addPillMax = addPillMin + addPillSize;
            ImGui.SetCursorScreenPos(addPillMin);
            bool addClicked = ImGui.InvisibleButton("##ja.new.add", addPillSize);
            bool addHovered = ImGui.IsItemHovered() && canAdd;
            if (canAdd)
            {
                Boutique.DrawGoldPill(dl, addPillMin, addPillMax, "ADD",
                    addPillTrack, scale, addHovered, showPlus: true);
            }
            else
            {
                Boutique.FillSlip(dl, addPillMin, addPillMax, Boutique.ChamSm * scale,
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.18f)));
                using (Plugin.Instance?.OswaldSemi11?.Push())
                {
                    float labelW = Boutique.MeasureTrackedText("ADD", addPillTrack);
                    var inkPos = new Vector2(
                        addPillMin.X + (addPillSize.X - labelW) * 0.5f,
                        addPillMin.Y + (addPillSize.Y - ImGui.GetFontSize()) * 0.5f);
                    Boutique.DrawTrackedText(dl, inkPos, "ADD",
                        Boutique.U32(Boutique.TextFaint), addPillTrack);
                }
            }
            if (addClicked && canAdd)
            {
                string value = newJobAssignmentUseDesign && !string.IsNullOrWhiteSpace(newJobAssignmentDesignBuffer)
                    ? $"Design:{newJobAssignmentCharacterBuffer}:{newJobAssignmentDesignBuffer}"
                    : $"Character:{newJobAssignmentCharacterBuffer}";
                plugin.Configuration.JobAssignments[proposedKey] = value;
                plugin.AchievementTracker?.OnJobAssignmentSet();
                plugin.AchievementTracker?.CheckJobAssignmentCount();
                plugin.Configuration.Save();
                newJobAssignmentCharacterBuffer = "";
                newJobAssignmentUseDesign = false;
                newJobAssignmentDesignBuffer = "";
            }

            ImGui.SetCursorScreenPos(new Vector2(origin.X, max.Y + 10f * scale));
            ImGui.Dummy(new Vector2(0, 0));
        }

        // Two-tab segmented control. Active half gets a 2 px gold-warm
        // bottom underline + faint gold wash; inactive half gets a thin
        // BorderSoft hairline. Tracked-caps label centred.
        private void DrawSegmentedTab(ImDrawListPtr dl, string id,
            Vector2 min, Vector2 max, string label, bool active, float scale, Action onClick)
        {
            ImGui.SetCursorScreenPos(min);
            bool clicked = ImGui.InvisibleButton($"##{id}", max - min);
            bool hovered = ImGui.IsItemHovered();
            if (clicked) onClick();

            // Bg
            uint bgCol = active
                ? Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.08f))
                : Boutique.U32(Boutique.WithAlpha(Boutique.Surface1, 0.40f));
            dl.AddRectFilled(min, max, bgCol);

            // Border (faint hairline)
            uint borderCol = active
                ? Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.65f))
                : Boutique.U32(Boutique.WithAlpha(Boutique.BorderSoft, hovered ? 0.85f : 0.45f));
            dl.AddRect(min, max, borderCol, 0f, ImDrawFlags.None, 1f * scale);

            // Active underline (2 px gold-warm) + glow halo above it
            if (active)
            {
                float underlineH = 2f * scale;
                dl.AddRectFilled(
                    new Vector2(min.X + 2f * scale, max.Y - underlineH),
                    new Vector2(max.X - 2f * scale, max.Y),
                    Boutique.U32(Boutique.GoldWarm));
                // 4-slice vertical glow above the underline
                for (int g = 0; g < 4; g++)
                {
                    float a = 0.16f * (1f - g / 4f);
                    dl.AddRectFilled(
                        new Vector2(min.X + 4f * scale, max.Y - underlineH - (g + 1) * scale),
                        new Vector2(max.X - 4f * scale, max.Y - underlineH - g * scale),
                        Boutique.U32(Boutique.WithAlpha(Boutique.GoldWarm, a)));
                }
            }

            // Centred tracked-caps label
            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                float trackPx = 1.6f * scale;
                float lw = Boutique.MeasureTrackedText(label, trackPx);
                var fontSz = ImGui.GetFontSize();
                Vector4 textCol = active ? Boutique.GoldBright : (hovered ? Boutique.GoldWarm : Boutique.TextDim);
                var inkPos = new Vector2(
                    min.X + (max.X - min.X - lw) * 0.5f,
                    min.Y + ((max.Y - min.Y) - fontSz) * 0.5f);
                Boutique.DrawTrackedText(dl, inkPos, label, Boutique.U32(textCol), trackPx);
            }
        }

        private void DrawFixedSetting(string label, float labelWidth, float inputWidth, Action drawControl)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text(label);
            ImGui.SameLine(labelWidth);
            ImGui.SetNextItemWidth(inputWidth);
            drawControl();
            ImGui.Spacing();
        }

        private void UpdateAutomationSettings(bool enableAutomations)
        {
            bool changed = false;

            // Character-level Automation Handling
            foreach (var character in plugin.Characters)
            {
                if (!enableAutomations)
                {
                    character.CharacterAutomation = string.Empty;
                }
                else if (string.IsNullOrWhiteSpace(character.CharacterAutomation))
                {
                    character.CharacterAutomation = "None";
                }
            }

            if (!enableAutomations)
            {
                // Remove automation lines from all macros
                foreach (var character in plugin.Characters)
                {
                    foreach (var design in character.Designs)
                    {
                        string macro = design.IsAdvancedMode ? design.AdvancedMacro : design.Macro;
                        if (string.IsNullOrWhiteSpace(macro))
                            continue;

                        var cleaned = string.Join("\n", macro
                            .Split('\n')
                            .Where(line => !line.TrimStart().StartsWith("/glamour automation enable", StringComparison.OrdinalIgnoreCase))
                            .Select(line => line.TrimEnd()));

                        if (design.IsAdvancedMode && cleaned != design.AdvancedMacro)
                        {
                            design.AdvancedMacro = cleaned;
                            changed = true;
                        }
                        else if (!design.IsAdvancedMode && cleaned != design.Macro)
                        {
                            design.Macro = cleaned;
                            changed = true;
                        }
                    }
                }

                foreach (var character in plugin.Characters)
                {
                    if (string.IsNullOrWhiteSpace(character.Macros))
                        continue;

                    var cleaned = string.Join("\n", character.Macros
                        .Split('\n')
                        .Where(line => !line.TrimStart().StartsWith("/glamour automation enable", StringComparison.OrdinalIgnoreCase))
                        .Select(line => line.TrimEnd()));

                    if (cleaned != character.Macros)
                    {
                        character.Macros = cleaned;
                        changed = true;
                    }
                }
            }
            else
            {
                // Re-add automation lines
                foreach (var character in plugin.Characters)
                {
                    foreach (var design in character.Designs)
                    {
                        string macro = design.IsAdvancedMode ? design.AdvancedMacro : design.Macro;
                        if (string.IsNullOrWhiteSpace(macro))
                            continue;

                        string updated = Plugin.SanitizeDesignMacro(macro, design, character, true);

                        if (design.IsAdvancedMode && updated != design.AdvancedMacro)
                        {
                            design.AdvancedMacro = updated;
                            changed = true;
                        }
                        else if (!design.IsAdvancedMode && updated != design.Macro)
                        {
                            design.Macro = updated;
                            changed = true;
                        }
                    }
                }

                foreach (var character in plugin.Characters)
                {
                    string updated = Plugin.SanitizeMacro(character.Macros, character);
                    if (updated != character.Macros)
                    {
                        character.Macros = updated;
                        changed = true;
                    }
                }
            }

            if (changed)
                plugin.SaveConfiguration();
        }
        private void DrawConflictResolutionSettings()
        {
            float scale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);
            float toggleW = 42f * scale;

            // Experimental warning callout
            Boutique.Callout(Boutique.CalloutKind.Warning, FontAwesomeIcon.ExclamationTriangle,
                "Experimental feature",
                "Conflict Resolution automatically manages mod conflicts by controlling which mods are enabled per character. Use at your own risk.",
                scale);

            // Master toggle
            Boutique.SettingRow("cr.enable", "Enable Conflict Resolution",
                "Lets you select specific mods per character/design that automatically enable/disable when switching. Prevents mod conflicts without manual Penumbra management.",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.EnableConflictResolution;
                    bool wasEnabled = plugin.Configuration.EnableConflictResolution;
                    if (Boutique.TogglePill("cr.enable", ref v, scale))
                    {
                        if (v && !wasEnabled)
                        {
                            plugin.BackupPenumbraCollections();
                            plugin.AchievementTracker?.OnConflictResolutionEnabled();
                        }
                        plugin.Configuration.EnableConflictResolution = v;
                        plugin.SaveConfiguration();
                    }
                });

            if (plugin.Configuration.EnableConflictResolution)
            {
                // How-to bullet list as a single info callout
                Boutique.Callout(Boutique.CalloutKind.Info, FontAwesomeIcon.InfoCircle,
                    "How it works",
                    "Hold Ctrl+Shift while clicking Add Character/Design to auto-categorise mods in CS+ (no Penumbra changes). " +
                    "Right-click to move mods if categorisation is wrong. Gear/Hair mods are managed automatically per character; " +
                    "other categories are managed manually. Configure individual mod settings per character, and pin critical mods to keep always active.",
                    scale);

                Boutique.SettingRow("cr.respectInheritance", "Respect Penumbra Inheritance",
                    "Mod manager shows a dropdown (Enable/Disable/Inherit) instead of a checkbox. The 'Inherit' option appears for mods inherited from parent collections and lets Penumbra manage them. Useful if you use Penumbra's collection inheritance feature.",
                    toggleW, scale,
                    () =>
                    {
                        bool v = plugin.Configuration.RespectPenumbraInheritance;
                        if (Boutique.TogglePill("cr.respectInheritance", ref v, scale))
                        {
                            plugin.Configuration.RespectPenumbraInheritance = v;
                            plugin.SaveConfiguration();
                        }
                    });
            }

            ImGui.Dummy(new Vector2(0, 6f * scale));
        }

        private float GetSafeScale(float baseScale)
        {
            return Math.Clamp(baseScale, 0.3f, 5.0f); // Prevent extreme scaling
        }

        private void DrawBackupSettings()
        {
            float scale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);

            // Pending import file (kept from original flow)
            if (pendingImportPath != null)
            {
                string importPath;
                lock (this)
                {
                    importPath = pendingImportPath;
                    pendingImportPath = null;
                }

                if (File.Exists(importPath))
                {
                    Plugin.Log.Info($"[Settings] Processing import file: {importPath}");
                    AddImportedFileToBackups(importPath);
                }
                else
                {
                    lastBackupStatusMessage = "Selected file does not exist";
                    lastBackupStatusIsError = true;
                    lastBackupStatusTime = DateTime.Now;
                }
            }

            // Intro callout
            Boutique.Callout(Boutique.CalloutKind.Info, FontAwesomeIcon.Save,
                "Backups",
                "Create manual snapshots of your CS+ configuration and restore from any saved point.",
                scale);

            var backupInfo = BackupManager.GetBackupInfo();
            RefreshAvailableBackups();
            var dl = ImGui.GetWindowDrawList();
            double time = ImGui.GetTime();

            // (1) Archive Ledger card
            DrawArchiveLedgerCard(dl, scale, time, backupInfo);

            // (2) Snapshot Composer
            DrawSnapshotComposerCard(dl, scale, time);

            // (3) Restore card with snapshot plates inside
            DrawRestoreCard(dl, scale, time);

            // (4) Restore overwrites warning (expanded body to surface emergency rollback)
            Boutique.Callout(Boutique.CalloutKind.Warning, FontAwesomeIcon.ExclamationTriangle,
                "Restore overwrites your config",
                "Restoring overwrites your current configuration. CS+ creates an emergency snapshot first, so you can roll back from the next plate up if anything goes wrong.",
                scale);

            ImGui.Dummy(new Vector2(0, 6f * scale));
        }

        // ARCHIVE LEDGER, replaces the flat status text. Chamfered slip
        // with a pulsing wax-seal diamond on the left + 3 inline ledger
        // rows (last auto, count, version).
        private void DrawArchiveLedgerCard(ImDrawListPtr dl, float scale, double time,
            BackupInfo backupInfo)
        {
            float availW = ImGui.GetContentRegionAvail().X;
            var origin = ImGui.GetCursorScreenPos();
            float cardH = 78f * scale;
            float chamfer = 8f * scale;
            var min = origin;
            var max = origin + new Vector2(availW, cardH);

            // Slip chrome
            Boutique.FillSlip(dl, min, max, chamfer,
                Boutique.U32(Boutique.WithAlpha(Boutique.Surface1, 0.50f)));
            Span<Vector2> pts = stackalloc Vector2[6];
            Boutique.BuildSlipPolygon(min, max, chamfer, pts);
            for (int s = 0; s < 6; s++) dl.PathLineTo(pts[s]);
            dl.PathStroke(Boutique.U32(Boutique.BorderSoft), ImDrawFlags.Closed, 1f * scale);
            dl.AddRectFilled(
                new Vector2(min.X, min.Y),
                new Vector2(max.X - chamfer, min.Y + 2f * scale),
                Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.55f)));

            float padX = 14f * scale;
            float leftMinX = min.X + padX;

            // Wax seal: pulsing if any backups exist, static otherwise
            float sealCx = leftMinX + 8f * scale;
            float sealCy = min.Y + cardH * 0.5f - 6f * scale;
            bool hasBackups = backupInfo.BackupExists || availableBackups.Count > 0;
            DrawWaxSeal(dl, new Vector2(sealCx, sealCy), 7f, hasBackups, time, scale);

            // ARCHIVE kicker below the seal
            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                float trackPx = 1.2f * scale;
                string kicker = "ARCHIVE";
                float kw = Boutique.MeasureTrackedText(kicker, trackPx);
                Boutique.DrawTrackedText(dl,
                    new Vector2(sealCx - kw * 0.5f, min.Y + cardH * 0.5f + 8f * scale),
                    kicker,
                    Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.85f)),
                    trackPx);
            }

            // Centre column: 3 ledger rows
            float colX = leftMinX + 40f * scale;
            float kickerColW = 78f * scale;
            float row1Y = min.Y + 14f * scale;
            float row2Y = min.Y + 32f * scale;
            float row3Y = min.Y + 50f * scale;

            string lastAutoVal;
            Vector4 lastAutoCol;
            if (backupInfo.BackupExists && backupInfo.LastBackupDate.HasValue)
            {
                lastAutoVal = backupInfo.LastBackupDate.Value.ToString("yyyy-MM-dd HH:mm");
                lastAutoCol = Boutique.Text;
            }
            else
            {
                lastAutoVal = "never";
                lastAutoCol = Boutique.TextFaint;
            }

            DrawLedgerRow(dl, "LAST AUTO", lastAutoVal, lastAutoCol,
                new Vector2(colX, row1Y), kickerColW, scale);
            DrawLedgerRow(dl, "BACKUPS",
                availableBackups.Count.ToString(),
                availableBackups.Count > 0 ? Boutique.GoldWarm : Boutique.TextFaint,
                new Vector2(colX, row2Y), kickerColW, scale);
            string versionVal = string.IsNullOrEmpty(backupInfo.LastBackupVersion)
                ? ","
                : backupInfo.LastBackupVersion;
            DrawLedgerRow(dl, "VERSION", versionVal,
                string.IsNullOrEmpty(backupInfo.LastBackupVersion) ? Boutique.TextFaint : Boutique.Text,
                new Vector2(colX, row3Y), kickerColW, scale);

            ImGui.SetCursorScreenPos(new Vector2(origin.X, max.Y + 10f * scale));
            ImGui.Dummy(new Vector2(0, 0));
        }

        private static void DrawLedgerRow(ImDrawListPtr dl, string kicker, string value,
            Vector4 valueCol, Vector2 pos, float kickerColW, float scale)
        {
            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                float trackPx = 1.2f * scale;
                Boutique.DrawTrackedText(dl, pos, kicker,
                    Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.85f)), trackPx);
            }
            using (Plugin.Instance?.OutfitMed13?.Push())
            {
                dl.AddText(new Vector2(pos.X + kickerColW, pos.Y - 2f * scale),
                    Boutique.U32(valueCol), value);
            }
        }

        // SNAPSHOT COMPOSER, same silhouette as Job/Character composer.
        // Surface0@0.55 chamfered slip with DRAFTING SNAPSHOT kicker on top,
        // single name input row, gold SAVE pill on the right. Inline 5 s
        // success line at the bottom of the card (success = check, error = x).
        private void DrawSnapshotComposerCard(ImDrawListPtr dl, float scale, double time)
        {
            float availW = ImGui.GetContentRegionAvail().X;
            var origin = ImGui.GetCursorScreenPos();
            float chamfer = 8f * scale;

            bool hasFeedback = !string.IsNullOrEmpty(lastBackupStatusMessage)
                && (DateTime.Now - lastBackupStatusTime) < TimeSpan.FromSeconds(5);
            float feedbackH = hasFeedback ? 22f * scale : 0f;
            float cardH = 78f * scale + feedbackH;
            var min = origin;
            var max = origin + new Vector2(availW, cardH);

            Boutique.FillSlip(dl, min, max, chamfer,
                Boutique.U32(Boutique.WithAlpha(Boutique.Surface0, 0.55f)));
            Span<Vector2> pts = stackalloc Vector2[6];
            Boutique.BuildSlipPolygon(min, max, chamfer, pts);
            for (int s = 0; s < 6; s++) dl.PathLineTo(pts[s]);
            dl.PathStroke(Boutique.U32(Boutique.WithAlpha(Boutique.BorderSoft, 0.55f)),
                ImDrawFlags.Closed, 1f * scale);

            // Centred top kicker
            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                float trackPx = 1.8f * scale;
                string kicker = "DRAFTING SNAPSHOT";
                float kw = Boutique.MeasureTrackedText(kicker, trackPx);
                Boutique.DrawTrackedText(dl,
                    new Vector2(min.X + (availW - kw) * 0.5f, min.Y + 8f * scale),
                    kicker, Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.80f)), trackPx);
            }

            float padX = 14f * scale;
            float leftMinX = min.X + padX;
            float rightMaxX = max.X - padX;

            // Row: NAME kicker + text input + SAVE gold pill
            float rowY = min.Y + 32f * scale;

            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                float trackPx = 1.6f * scale;
                var kickerFontSz = ImGui.GetFontSize();
                Boutique.DrawTrackedText(dl,
                    new Vector2(leftMinX, rowY + (26f * scale - kickerFontSz) * 0.5f),
                    "NAME", Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.95f)), trackPx);
            }

            // SAVE pill anchored right
            float savePillTrack = 1.6f * scale;
            var savePillSize = Boutique.DrawGoldPillSize("SAVE", savePillTrack, scale);
            savePillSize.X = MathF.Max(savePillSize.X, 84f * scale);
            var savePillMin = new Vector2(rightMaxX - savePillSize.X, rowY);
            var savePillMax = savePillMin + savePillSize;

            // Text input fills the middle
            float kickerW = 56f * scale;
            float gapL = 8f * scale;
            float gapR = 10f * scale;
            float inputX = leftMinX + kickerW + gapL;
            float inputW = savePillMin.X - gapR - inputX;
            ImGui.SetCursorScreenPos(new Vector2(inputX, rowY));
            if (Boutique.DrawBoutiqueTextInput("##BackupName",
                ref backupNameBuffer, 50, inputW,
                "Optional · timestamp used if empty"))
            {
                backupNameBuffer = string.Join("_",
                    backupNameBuffer.Split(Path.GetInvalidFileNameChars(),
                        StringSplitOptions.RemoveEmptyEntries));
            }

            // SAVE pill
            ImGui.SetCursorScreenPos(savePillMin);
            bool sealClicked = ImGui.InvisibleButton("##bk.save", savePillSize);
            bool sealHovered = ImGui.IsItemHovered();
            Boutique.DrawGoldPill(dl, savePillMin, savePillMax, "SAVE",
                savePillTrack, scale, sealHovered, showPlus: true);
            if (sealClicked) CreateManualBackup();

            // Inline 5 s feedback
            if (hasFeedback)
            {
                float feedbackY = max.Y - feedbackH + 4f * scale;
                float fade = (float)(5.0 - (DateTime.Now - lastBackupStatusTime).TotalSeconds) / 0.6f;
                fade = Math.Clamp(fade, 0f, 1f);
                Vector4 iconCol = lastBackupStatusIsError ? Boutique.Red : Boutique.Green;
                Vector4 textCol = lastBackupStatusIsError
                    ? Boutique.WithAlpha(Boutique.Red, 0.85f * fade)
                    : Boutique.WithAlpha(Boutique.Text, 0.85f * fade);

                // Icon
                string iconGlyph = (lastBackupStatusIsError
                    ? FontAwesomeIcon.Times
                    : FontAwesomeIcon.Check).ToIconString();
                var iconBox = new Vector2(leftMinX, feedbackY);
                DrawIconCentered(dl, iconBox, 14f * scale, iconGlyph,
                    Boutique.WithAlpha(iconCol, fade), scale);

                using (Plugin.Instance?.OutfitBody12?.Push())
                {
                    string display = TruncateToWidth(lastBackupStatusMessage,
                        rightMaxX - leftMinX - 22f * scale);
                    dl.AddText(new Vector2(leftMinX + 18f * scale, feedbackY - 1f * scale),
                        Boutique.U32(textCol), display);
                }
            }

            ImGui.SetCursorScreenPos(new Vector2(origin.X, max.Y + 10f * scale));
            ImGui.Dummy(new Vector2(0, 0));
        }

        // RESTORE CARD, single chamfered envelope holding all snapshot
        // plates + an ADD CONFIG FILE footer row. Header reads "ARCHIVE
        // SNAPSHOTS (n)". Empty state collapses to a single line.
        private void DrawRestoreCard(ImDrawListPtr dl, float scale, double time)
        {
            float availW = ImGui.GetContentRegionAvail().X;
            var origin = ImGui.GetCursorScreenPos();
            float chamfer = 8f * scale;

            float headerH = 28f * scale;
            float plateH = 48f * scale;
            float footerH = 40f * scale;
            int rowCount = Math.Min(availableBackups.Count, 10);
            float listH = rowCount > 0 ? (rowCount * plateH) : (40f * scale);
            float cardH = headerH + listH + footerH;
            var min = origin;
            var max = origin + new Vector2(availW, cardH);

            // Envelope chrome
            Boutique.FillSlip(dl, min, max, chamfer,
                Boutique.U32(Boutique.WithAlpha(Boutique.Surface1, 0.45f)));
            Span<Vector2> pts = stackalloc Vector2[6];
            Boutique.BuildSlipPolygon(min, max, chamfer, pts);
            for (int s = 0; s < 6; s++) dl.PathLineTo(pts[s]);
            dl.PathStroke(Boutique.U32(Boutique.BorderSoft), ImDrawFlags.Closed, 1f * scale);
            dl.AddRectFilled(
                new Vector2(min.X, min.Y),
                new Vector2(max.X - chamfer, min.Y + 2f * scale),
                Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.55f)));

            float padX = 14f * scale;
            float leftMinX = min.X + padX;
            float rightMaxX = max.X - padX;

            // Header: "ARCHIVE SNAPSHOTS (n)"
            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                float trackPx = 1.6f * scale;
                string title = $"ARCHIVE SNAPSHOTS · {availableBackups.Count}";
                Boutique.DrawTrackedText(dl,
                    new Vector2(leftMinX, min.Y + 9f * scale),
                    title, Boutique.U32(Boutique.GoldWarm), trackPx);
            }
            // Header bottom hairline
            dl.AddLine(
                new Vector2(min.X + 4f * scale, min.Y + headerH),
                new Vector2(max.X - 4f * scale, min.Y + headerH),
                Boutique.U32(Boutique.WithAlpha(Boutique.BorderSoft, 0.55f)),
                1f * scale);

            // Plates
            float plateTop = min.Y + headerH;
            if (rowCount > 0)
            {
                bool isCtrlShiftHeld = ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift;
                int idx = 0;
                foreach (var backup in availableBackups.Take(10))
                {
                    bool isLatest = idx == 0;
                    var rowMin = new Vector2(leftMinX, plateTop + idx * plateH);
                    var rowMax = new Vector2(rightMaxX, rowMin.Y + plateH);
                    bool actionTaken = DrawSnapshotPlate(dl, backup, isLatest,
                        rowMin, rowMax, isCtrlShiftHeld, time, scale);
                    if (actionTaken) break;
                    idx++;
                }
            }
            else
            {
                using (Plugin.Instance?.OswaldSemi11?.Push())
                {
                    float trackPx = 1.4f * scale;
                    string msg = "NO ARCHIVED SNAPSHOTS";
                    float mw = Boutique.MeasureTrackedText(msg, trackPx);
                    Boutique.DrawTrackedText(dl,
                        new Vector2(min.X + (availW - mw) * 0.5f,
                                    plateTop + 14f * scale),
                        msg, Boutique.U32(Boutique.TextFaint), trackPx);
                }
            }

            // Footer hairline + ADD CONFIG FILE row
            float footerY = min.Y + headerH + listH;
            dl.AddLine(
                new Vector2(min.X + 4f * scale, footerY),
                new Vector2(max.X - 4f * scale, footerY),
                Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.30f)),
                1f * scale);

            // Borrow-from-disk button: chamfered ghost, upload glyph + tracked-caps label
            float btnH = 30f * scale;
            float glyphSide = btnH;
            float glyphTextGap = 12f * scale;
            float btnPadX = 14f * scale;
            string label = "ADD CONFIG FILE";
            float labelTrackPx = 1.8f * scale;
            float labelW;
            using (Plugin.Instance?.OswaldSemi11?.Push())
                labelW = Boutique.MeasureTrackedText(label, labelTrackPx);
            float btnW = btnPadX + glyphSide + glyphTextGap + labelW + btnPadX;

            var btnMin = new Vector2(leftMinX, footerY + (footerH - btnH) * 0.5f);
            var btnMax = btnMin + new Vector2(btnW, btnH);
            ImGui.SetCursorScreenPos(btnMin);
            bool importClicked = ImGui.InvisibleButton("##bk.import", new Vector2(btnW, btnH));
            bool importHovered = ImGui.IsItemHovered();
            Vector4 borderCol = importHovered ? Boutique.Gold : Boutique.GoldDeep;
            Vector4 textCol = importHovered ? Boutique.Gold : Boutique.GoldWarm;
            if (importHovered)
                Boutique.FillSlip(dl, btnMin, btnMax, 4f * scale,
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.08f)));
            Boutique.StrokeSlip(dl, btnMin, btnMax, 4f * scale,
                Boutique.U32(borderCol), 1f * scale);

            // Upload glyph (centred in a glyphSide-wide box at btnMin.X + btnPadX)
            string uploadGlyph = FontAwesomeIcon.FileImport.ToIconString();
            var glyphBox = new Vector2(btnMin.X + btnPadX, btnMin.Y);
            DrawIconCentered(dl, glyphBox, glyphSide, uploadGlyph, textCol, scale);

            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                var fontSz = ImGui.GetFontSize();
                Boutique.DrawTrackedText(dl,
                    new Vector2(btnMin.X + btnPadX + glyphSide + glyphTextGap,
                                btnMin.Y + (btnH - fontSz) * 0.5f),
                    label, Boutique.U32(textCol), labelTrackPx);
            }
            if (importHovered) Boutique.Tooltip("Open a file browser and add an existing CharacterSelectPlus configuration to your archive.");
            if (importClicked) ImportConfigurationFile();

            ImGui.SetCursorScreenPos(new Vector2(origin.X, max.Y + 10f * scale));
            ImGui.Dummy(new Vector2(0, 0));
        }

        // SNAPSHOT PLATE, one row inside the Restore envelope. Returns
        // true if a destructive action (restore or delete) was just taken
        // (caller should break the loop because the list mutates).
        private bool DrawSnapshotPlate(ImDrawListPtr dl, BackupFileInfo backup, bool isLatest,
            Vector2 rowMin, Vector2 rowMax, bool isCtrlShiftHeld, double time, float scale)
        {
            // 1 px BorderSoft@30 hairline at top (skip on the very first row -
            // the header hairline already covers it)
            dl.AddLine(
                new Vector2(rowMin.X, rowMin.Y),
                new Vector2(rowMax.X, rowMin.Y),
                Boutique.U32(Boutique.WithAlpha(Boutique.BorderSoft, 0.30f)),
                1f * scale);

            // Hover wash
            bool rowHover = ImGui.IsMouseHoveringRect(rowMin, rowMax) && !ImGui.IsAnyItemActive();
            if (rowHover)
                dl.AddRectFilled(rowMin, rowMax,
                    Boutique.U32(new Vector4(1f, 1f, 1f, 0.020f)));

            float midY = (rowMin.Y + rowMax.Y) * 0.5f;
            float plateH = rowMax.Y - rowMin.Y;

            // Wax seal on the left
            float sealCx = rowMin.X + 8f * scale;
            DrawWaxSeal(dl, new Vector2(sealCx, midY),
                isLatest ? 6f : 5f, isLatest, time, scale);

            // Date + relative-time/size subline
            string dateMain = backup.CreatedDate.ToString("yyyy-MM-dd · HH:mm");
            string subLine = $"{RelativeTime(backup.CreatedDate)} · {backup.GetFileSizeString()}";

            float dateColX = rowMin.X + 22f * scale;
            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                float trackPx = 1.4f * scale;
                Boutique.DrawTrackedText(dl,
                    new Vector2(dateColX, midY - 14f * scale),
                    dateMain, Boutique.U32(Boutique.Text), trackPx);
            }
            using (Plugin.Instance?.OutfitBody12?.Push())
            {
                dl.AddText(new Vector2(dateColX, midY + 1f * scale),
                    Boutique.U32(Boutique.TextDim),
                    TruncateToWidth(subLine, 200f * scale));
            }

            // Right cluster: type tag + RESTORE button + delete X
            float xBoxSz = 22f * scale;
            float restoreW = 64f * scale;
            float gap = 6f * scale;
            float xBoxLeft = rowMax.X - xBoxSz;
            float xBoxTop = midY - xBoxSz * 0.5f;
            float restoreRight = xBoxLeft - gap;
            float restoreLeft = restoreRight - restoreW;
            float restoreTop = midY - 11f * scale;
            float typeTagRight = restoreLeft - gap;

            // Determine type kind. Emergency files are flagged manual but their
            // filename leads with "emergency_" - check that first.
            BackupTypeKind kind;
            if (backup.FileName.IndexOf("emergency_", StringComparison.OrdinalIgnoreCase) >= 0)
                kind = BackupTypeKind.Emergency;
            else if (backup.IsManual)
                kind = BackupTypeKind.Manual;
            else
                kind = BackupTypeKind.Auto;

            // Type tag width: enough for EMERGENCY at OswaldSemi9 + 1.4 trackPx
            float typeTagW = kind == BackupTypeKind.Emergency ? 72f * scale : 56f * scale;
            float typeTagH = 18f * scale;
            var tagMin = new Vector2(typeTagRight - typeTagW, midY - typeTagH * 0.5f);
            var tagMax = tagMin + new Vector2(typeTagW, typeTagH);
            DrawTypeTag(dl, tagMin, tagMax, kind, isLatest, scale);

            // RESTORE ghost button (chamfered)
            var restoreMin = new Vector2(restoreLeft, restoreTop);
            var restoreMax = restoreMin + new Vector2(restoreW, 22f * scale);
            ImGui.SetCursorScreenPos(restoreMin);
            bool restoreClicked = ImGui.InvisibleButton(
                $"##bk.restore_{backup.FileName}", restoreMax - restoreMin);
            bool restoreHovered = ImGui.IsItemHovered();
            Vector4 rBorder = restoreHovered ? Boutique.Gold : Boutique.GoldDeep;
            Vector4 rText = restoreHovered ? Boutique.Gold : Boutique.GoldDeep;
            if (restoreHovered)
                Boutique.FillSlip(dl, restoreMin, restoreMax, 3f * scale,
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.08f)));
            Boutique.StrokeSlip(dl, restoreMin, restoreMax, 3f * scale,
                Boutique.U32(rBorder), 1f * scale);
            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                float trackPx = 1.4f * scale;
                string label = "RESTORE";
                float lw = Boutique.MeasureTrackedText(label, trackPx);
                var fontSz = ImGui.GetFontSize();
                Boutique.DrawTrackedText(dl,
                    new Vector2(restoreMin.X + (restoreW - lw) * 0.5f,
                                restoreMin.Y + (22f * scale - fontSz) * 0.5f),
                    label, Boutique.U32(rText), trackPx);
            }
            if (restoreHovered)
                Boutique.Tooltip(
                    $"Restore configuration from:\n{backup.FileName}\n" +
                    $"Created: {backup.CreatedDate:yyyy-MM-dd HH:mm:ss}");
            if (restoreClicked)
            {
                RestoreFromBackup(backup.FilePath);
                return true;
            }

            // Delete X with three safety states
            var xMin = new Vector2(xBoxLeft, xBoxTop);
            var xMax = xMin + new Vector2(xBoxSz, xBoxSz);
            ImGui.SetCursorScreenPos(xMin);
            bool xClicked = ImGui.InvisibleButton(
                $"##bk.del_{backup.FileName}", xMax - xMin);
            bool xHovered = ImGui.IsItemHovered();

            // State logic:
            //  - armed (ctrl+shift held, regardless of hover): red border + red glyph
            //  - hover-no-shift: gold-deep border (subtle wake)
            //  - idle: BorderSoft + TextGhost
            Vector4 xBorder;
            Vector4 xGlyphCol;
            if (isCtrlShiftHeld)
            {
                xBorder = Boutique.WithAlpha(Boutique.Red, 0.85f);
                xGlyphCol = Boutique.WithAlpha(Boutique.Red, 0.95f);
            }
            else if (xHovered)
            {
                xBorder = Boutique.WithAlpha(Boutique.GoldDeep, 0.85f);
                xGlyphCol = Boutique.TextDim;
            }
            else
            {
                xBorder = Boutique.WithAlpha(Boutique.BorderSoft, 0.65f);
                xGlyphCol = Boutique.TextGhost;
            }
            Boutique.StrokeSlip(dl, xMin, xMax, 3f * scale,
                Boutique.U32(xBorder), 1f * scale);

            // Armed state: red glow halo - 3 expanding outer strokes
            if (isCtrlShiftHeld)
            {
                for (int g = 0; g < 3; g++)
                {
                    float pad = g * scale;
                    float a = 0.18f * (1f - g / 3f);
                    Boutique.StrokeSlip(dl,
                        new Vector2(xMin.X - pad, xMin.Y - pad),
                        new Vector2(xMax.X + pad, xMax.Y + pad),
                        (3f + g) * scale,
                        Boutique.U32(Boutique.WithAlpha(Boutique.Red, a)),
                        1f * scale);
                }
            }

            string xGlyph = FontAwesomeIcon.Times.ToIconString();
            DrawIconCentered(dl, xMin, xBoxSz, xGlyph, xGlyphCol, scale);

            if (xHovered)
            {
                if (isCtrlShiftHeld)
                    Boutique.Tooltip(
                        $"READY · CLICK TO DELETE\n{backup.FileName}\nThis cannot be undone.");
                else
                    Boutique.Tooltip(
                        $"Hold Ctrl+Shift to arm delete\n{backup.FileName}\n(prevents accidental deletion)");
            }
            if (xClicked && isCtrlShiftHeld)
            {
                DeleteBackup(backup.FilePath, backup.FileName);
                return true;
            }

            return false;
        }

        // WAX SEAL, diamond stack with optional glow + pulse. The plugin
        // signature mark for this section. `latest = true` paints the
        // multi-lozenge "live wax" version with breathing glow; otherwise
        // a single static GoldDeep diamond.
        private static void DrawWaxSeal(ImDrawListPtr dl, Vector2 centre,
            float baseSize, bool latest, double time, float scale)
        {
            float pulse = latest
                ? 0.85f + 0.15f * MathF.Sin((float)time * 1.6f)
                : 1f;

            if (latest)
            {
                // Glow halo: 3 outer lozenges
                for (int g = 0; g < 3; g++)
                {
                    float gSize = (baseSize + 2f * (g + 1)) * scale * pulse;
                    float gAlpha = 0.32f * (1f - g / 3f);
                    dl.AddQuadFilled(
                        new Vector2(centre.X, centre.Y - gSize),
                        new Vector2(centre.X + gSize, centre.Y),
                        new Vector2(centre.X, centre.Y + gSize),
                        new Vector2(centre.X - gSize, centre.Y),
                        Boutique.U32(Boutique.WithAlpha(Boutique.GoldWarm, gAlpha)));
                }
            }

            float outerSize = baseSize * scale * (latest ? pulse : 1f);
            dl.AddQuadFilled(
                new Vector2(centre.X, centre.Y - outerSize),
                new Vector2(centre.X + outerSize, centre.Y),
                new Vector2(centre.X, centre.Y + outerSize),
                new Vector2(centre.X - outerSize, centre.Y),
                Boutique.U32(latest ? Boutique.Gold : Boutique.GoldDeep));

            if (latest)
            {
                float midSize = (baseSize - 2f) * scale;
                if (midSize > 0)
                {
                    dl.AddQuadFilled(
                        new Vector2(centre.X, centre.Y - midSize),
                        new Vector2(centre.X + midSize, centre.Y),
                        new Vector2(centre.X, centre.Y + midSize),
                        new Vector2(centre.X - midSize, centre.Y),
                        Boutique.U32(Boutique.GoldWarm));
                }
                float innerSize = (baseSize - 4f) * scale;
                if (innerSize > 0)
                {
                    dl.AddQuadFilled(
                        new Vector2(centre.X, centre.Y - innerSize),
                        new Vector2(centre.X + innerSize, centre.Y),
                        new Vector2(centre.X, centre.Y + innerSize),
                        new Vector2(centre.X - innerSize, centre.Y),
                        Boutique.U32(Boutique.GoldBright));
                }
            }
        }

        // Backup type tag: chamfered mini-pill, transparent fill, single
        // tracked-caps label, three border + text colour pairings.
        private enum BackupTypeKind { Auto, Manual, Emergency }

        private static void DrawTypeTag(ImDrawListPtr dl, Vector2 min, Vector2 max,
            BackupTypeKind kind, bool latest, float scale)
        {
            Vector4 borderCol;
            Vector4 textCol;
            string label;
            switch (kind)
            {
                case BackupTypeKind.Manual:
                    borderCol = Boutique.GoldDeep;
                    textCol = Boutique.GoldDeep;
                    label = "MANUAL";
                    break;
                case BackupTypeKind.Emergency:
                    borderCol = Boutique.WithAlpha(Boutique.Red, 0.85f);
                    textCol = Boutique.WithAlpha(Boutique.Red, 0.85f);
                    label = "EMERGENCY";
                    break;
                default:
                    borderCol = Boutique.TextGhost;
                    textCol = Boutique.TextGhost;
                    label = "AUTO";
                    break;
            }

            Boutique.StrokeSlip(dl, min, max, 3f * scale,
                Boutique.U32(borderCol), 1f * scale);

            // Latest + manual gets a faint inner glow so the freshest manual
            // backup reads as "the one to roll back to".
            if (latest && kind == BackupTypeKind.Manual)
            {
                Boutique.StrokeSlip(dl,
                    new Vector2(min.X + 1f * scale, min.Y + 1f * scale),
                    new Vector2(max.X - 1f * scale, max.Y - 1f * scale),
                    2f * scale,
                    Boutique.U32(Boutique.WithAlpha(Boutique.GoldWarm, 0.35f)),
                    1f * scale);
            }

            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                float trackPx = 1.2f * scale;
                float lw = Boutique.MeasureTrackedText(label, trackPx);
                var fontSz = ImGui.GetFontSize();
                float w = max.X - min.X;
                float h = max.Y - min.Y;
                Boutique.DrawTrackedText(dl,
                    new Vector2(min.X + (w - lw) * 0.5f, min.Y + (h - fontSz) * 0.5f),
                    label, Boutique.U32(textCol), trackPx);
            }
        }

        // Friendly relative-time formatter for snapshot subline.
        private static string RelativeTime(DateTime then)
        {
            var span = DateTime.Now - then;
            if (span < TimeSpan.FromMinutes(1)) return "just now";
            if (span < TimeSpan.FromHours(1))
            {
                int m = (int)span.TotalMinutes;
                return m == 1 ? "1 minute ago" : $"{m} minutes ago";
            }
            if (span < TimeSpan.FromHours(24))
            {
                int h = (int)span.TotalHours;
                return h == 1 ? "1 hour ago" : $"{h} hours ago";
            }
            if (span < TimeSpan.FromDays(2)) return "yesterday";
            if (span < TimeSpan.FromDays(30))
            {
                int d = (int)span.TotalDays;
                return $"{d} days ago";
            }
            if (span < TimeSpan.FromDays(365))
            {
                int mo = (int)(span.TotalDays / 30);
                return mo == 1 ? "1 month ago" : $"{mo} months ago";
            }
            int y = (int)(span.TotalDays / 365);
            return y == 1 ? "1 year ago" : $"{y} years ago";
        }

        private void CreateManualBackup()
        {
            try
            {
                string? customName = string.IsNullOrWhiteSpace(backupNameBuffer) ? null : backupNameBuffer.Trim();
                string? backupPath = BackupManager.CreateManualBackup(plugin.Configuration, customName);

                if (!string.IsNullOrEmpty(backupPath))
                {
                    lastBackupStatusMessage = $"Snapshot sealed: {Path.GetFileName(backupPath)}";
                    lastBackupStatusIsError = false;
                    lastBackupStatusTime = DateTime.Now;
                    backupNameBuffer = "";
                    RefreshAvailableBackups();
                    plugin.AchievementTracker?.OnBackupCreated();
                }
                else
                {
                    lastBackupStatusMessage = "Failed to seal snapshot";
                    lastBackupStatusIsError = true;
                    lastBackupStatusTime = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Settings] Error creating manual backup: {ex.Message}");
                lastBackupStatusMessage = "Error sealing snapshot";
                lastBackupStatusIsError = true;
                lastBackupStatusTime = DateTime.Now;
            }
        }


        private void DrawAccountAndDataSettings()
        {
            float scale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);
            float dropW = 200f * scale;
            float inputW = 200f * scale;
            float toggleW = 80f * scale;

            // Intro callouts
            Boutique.Callout(Boutique.CalloutKind.Info, FontAwesomeIcon.InfoCircle,
                "What this does",
                "If you renamed a character before CS+ gained automatic rename detection, use these options to reclaim likes and RP profile data associated with the old name.",
                scale);
            Boutique.Callout(Boutique.CalloutKind.Info, FontAwesomeIcon.Clock,
                "When it runs",
                "The migration runs on the next upload of each affected character (i.e. next time you apply it).",
                scale);

            // ── Flow A: renamed a CS+ character ──
            Boutique.SubSectionHeader("I RENAMED A CS+ CHARACTER", null, scale);

            var characters = plugin.Characters.OrderBy(c => c.Name).ToList();
            if (characters.Count == 0)
            {
                using (Plugin.Instance?.OutfitMed12?.Push())
                    ImGui.TextColored(Boutique.TextFaint, "No CS+ characters to migrate.");
            }
            else
            {
                if (manualMigrationCharacterIndex < 0 || manualMigrationCharacterIndex >= characters.Count)
                    manualMigrationCharacterIndex = 0;

                Boutique.SettingRow("rename.csChar", "Character",
                    "Pick the CS+ character you renamed.",
                    dropW, scale,
                    () =>
                    {
                        var labels = characters.Select(c => c.Name).ToList();
                        int picked = Boutique.SortPill("rename.csChar", "CHAR",
                            manualMigrationCharacterIndex, labels, dropW, scale);
                        if (picked >= 0) manualMigrationCharacterIndex = picked;
                    });

                Boutique.SettingRow("rename.csOld", "Was previously named",
                    "The old CS+ character name (before you renamed it inside CS+).",
                    inputW, scale,
                    () =>
                    {
                        Boutique.DrawBoutiqueTextInput("##MigratePrevCSName",
                            ref manualMigrationPreviousName, 100, inputW, "Old CS+ name");
                    });

                ImGui.Dummy(new Vector2(0, 4f * scale));
                bool canApplyCSFlow = !string.IsNullOrWhiteSpace(manualMigrationPreviousName);
                if (!canApplyCSFlow) ImGui.BeginDisabled();
                if (Boutique.OutlineButton("rename.applyCS", "APPLY CS+ RENAME MIGRATION", scale))
                    ApplyCSRenameMigration();
                if (!canApplyCSFlow) ImGui.EndDisabled();
            }

            // ── Flow B: renamed in-game character ──
            Boutique.SubSectionHeader("I RENAMED MY IN-GAME CHARACTER", null, scale);

            Boutique.SettingRow("rename.physOld", "Previous in-game name",
                "Format: Name@Server. Applies to every CS+ character currently linked to your in-game character.",
                inputW, scale,
                () =>
                {
                    Boutique.DrawBoutiqueTextInput("##MigratePrevPhysical",
                        ref manualMigrationPhysicalName, 100, inputW, "Name@Server");
                });

            ImGui.Dummy(new Vector2(0, 4f * scale));
            bool canApplyPhysFlow = !string.IsNullOrWhiteSpace(manualMigrationPhysicalName)
                                    && manualMigrationPhysicalName.Contains('@');
            if (!canApplyPhysFlow) ImGui.BeginDisabled();
            if (Boutique.OutlineButton("rename.applyPhys", "APPLY IN-GAME RENAME MIGRATION", scale))
                ApplyPhysicalRenameMigration();
            if (!canApplyPhysFlow) ImGui.EndDisabled();

            // Inline status feedback (10s timeout). Subtle line instead of a
            // full callout box for a single sentence.
            if (!string.IsNullOrEmpty(manualMigrationStatusMessage)
                && (DateTime.Now - manualMigrationStatusTime).TotalSeconds < 10)
            {
                ImGui.Dummy(new Vector2(0, 8f * scale));
                using (Plugin.Instance?.OutfitMed12?.Push())
                {
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.TextColored(Boutique.Green, FontAwesomeIcon.Check.ToIconString());
                    ImGui.PopFont();
                    ImGui.SameLine(0, 6f * scale);
                    ImGui.PushTextWrapPos();
                    ImGui.TextColored(Boutique.TextDim, manualMigrationStatusMessage);
                    ImGui.PopTextWrapPos();
                }
            }

            ImGui.Dummy(new Vector2(0, 14f * scale));

            DrawServerCommunicationSection(scale, toggleW);

            ImGui.Dummy(new Vector2(0, 14f * scale));

            DrawDeleteMyDataSection(scale);

            ImGui.Dummy(new Vector2(0, 6f * scale));
        }

        private void DrawServerCommunicationSection(float scale, float toggleW)
        {
            Boutique.SubSectionHeader("SERVER COMMUNICATION", null, scale);

            Boutique.SettingRow("acct.disableServer", "Disable all server communication",
                "When on, CS+ stops uploading profiles, looking up other users, fetching the gallery, and submitting reports. Existing data on the server is unaffected (use the delete button below to wipe it).",
                toggleW, scale,
                () =>
                {
                    bool v = plugin.Configuration.DisableAllServerCommunication;
                    if (Boutique.TogglePill("acct.disableServer", ref v, scale))
                    {
                        plugin.Configuration.DisableAllServerCommunication = v;
                        plugin.Configuration.Save();
                    }
                });
        }

        private void DrawDeleteMyDataSection(float scale)
        {
            Boutique.SubSectionHeader("MY DATA ON THE SERVER", null, scale);

            using (Plugin.Instance?.OutfitMed12?.Push())
            {
                ImGui.PushTextWrapPos();
                ImGui.TextColored(Boutique.TextDim,
                    "Wipe every CS+ profile this installation has uploaded to the server (RP profile data, image, likes). The server keeps a record of which install owns each profile slot, so this only touches your own data.");
                ImGui.PopTextWrapPos();
            }

            ImGui.Dummy(new Vector2(0, 6f * scale));

            if (deleteDataInProgress) ImGui.BeginDisabled();
            if (ImGui.Button("Delete my data from server", new Vector2(220f * scale, 28f * scale)))
            {
                ImGui.OpenPopup("DeleteMyDataConfirm");
            }
            if (deleteDataInProgress) ImGui.EndDisabled();

            DrawDeleteMyDataConfirmPopup(scale);

            if (!string.IsNullOrEmpty(deleteDataStatusMessage)
                && (DateTime.Now - deleteDataStatusTime).TotalSeconds < 15)
            {
                ImGui.Dummy(new Vector2(0, 8f * scale));
                using (Plugin.Instance?.OutfitMed12?.Push())
                {
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.TextColored(Boutique.Green, FontAwesomeIcon.Check.ToIconString());
                    ImGui.PopFont();
                    ImGui.SameLine(0, 6f * scale);
                    ImGui.PushTextWrapPos();
                    ImGui.TextColored(Boutique.TextDim, deleteDataStatusMessage);
                    ImGui.PopTextWrapPos();
                }
            }
        }

        private void DrawDeleteMyDataConfirmPopup(float scale)
        {
            ImGui.SetNextWindowSize(new Vector2(420f * scale, 0));
            if (!ImGui.BeginPopupModal("DeleteMyDataConfirm", ImGuiWindowFlags.AlwaysAutoResize)) return;

            using (Plugin.Instance?.OutfitMed12?.Push())
            {
                ImGui.PushTextWrapPos();
                ImGui.TextColored(Boutique.TextDim,
                    "This permanently deletes every profile you've uploaded from this installation. The data cannot be recovered from the server.");
                ImGui.PopTextWrapPos();
            }

            ImGui.Dummy(new Vector2(0, 8f * scale));

            if (ImGui.Button("Cancel", new Vector2(120f * scale, 28f * scale)))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine(0, 8f * scale);
            if (ImGui.Button("Delete my data", new Vector2(160f * scale, 28f * scale)))
            {
                ImGui.CloseCurrentPopup();
                _ = RunDeleteMyServerDataAsync();
            }

            ImGui.EndPopup();
        }

        private async System.Threading.Tasks.Task RunDeleteMyServerDataAsync()
        {
            deleteDataInProgress = true;
            deleteDataStatusMessage = "";
            try
            {
                var (deleted, slots, error) = await Plugin.DeleteAllMyServerDataAsync();
                if (error != null)
                {
                    deleteDataStatusMessage = $"Error: {error}";
                }
                else if (deleted == 0)
                {
                    deleteDataStatusMessage = "Nothing to delete. The server has no profiles claimed by this installation.";
                }
                else
                {
                    deleteDataStatusMessage = $"Deleted {deleted} profile(s) across {slots} slot(s).";
                }
                deleteDataStatusTime = DateTime.Now;
            }
            finally
            {
                deleteDataInProgress = false;
            }
        }

        private void ApplyCSRenameMigration()
        {
            var sortedChars = plugin.Characters.OrderBy(c => c.Name).ToList();
            if (manualMigrationCharacterIndex < 0 || manualMigrationCharacterIndex >= sortedChars.Count)
                return;

            var character = sortedChars[manualMigrationCharacterIndex];
            string oldDisplayName = manualMigrationPreviousName.Trim();
            if (string.IsNullOrWhiteSpace(oldDisplayName)) return;

            if (string.IsNullOrWhiteSpace(character.LastInGameName))
            {
                manualMigrationStatusMessage = $"❌ {character.Name} has never been applied in-game - can't determine the physical character for migration.";
                manualMigrationStatusTime = DateTime.Now;
                return;
            }

            string oldFileKey = $"{oldDisplayName}_{character.LastInGameName}";
            character.AddPreviousProfileKey(oldFileKey);
            plugin.SaveConfiguration();

            manualMigrationStatusMessage = $"✓ Queued migration for {character.Name}. Apply the character once to migrate likes and remove the old file '{oldFileKey}'.";
            manualMigrationStatusTime = DateTime.Now;
            manualMigrationPreviousName = "";
        }

        private void ApplyPhysicalRenameMigration()
        {
            string oldPhysicalName = manualMigrationPhysicalName.Trim();
            if (string.IsNullOrWhiteSpace(oldPhysicalName) || !oldPhysicalName.Contains('@')) return;

            // Find every CS+ character currently linked to the user's current in-game character.
            // Their PreviousProfileKeys gets the old physical name appended.
            string? currentPhysicalName = null;
            if (Plugin.ObjectTable.LocalPlayer is { } player && player.HomeWorld.IsValid)
            {
                currentPhysicalName = $"{player.Name.TextValue}@{player.HomeWorld.Value.Name}";
            }

            if (string.IsNullOrWhiteSpace(currentPhysicalName))
            {
                manualMigrationStatusMessage = "❌ Can't determine your current in-game character. Log in first, then retry.";
                manualMigrationStatusTime = DateTime.Now;
                return;
            }

            int affected = 0;
            foreach (var character in plugin.Characters)
            {
                if (!string.Equals(character.LastInGameName, currentPhysicalName, StringComparison.Ordinal)) continue;

                string displayName = !string.IsNullOrWhiteSpace(character.Alias) ? character.Alias : character.Name;
                string oldFileKey = $"{displayName}_{oldPhysicalName}";
                character.AddPreviousProfileKey(oldFileKey);
                affected++;
            }

            if (affected > 0)
            {
                plugin.SaveConfiguration();
                manualMigrationStatusMessage = $"✓ Queued migration for {affected} character(s). Apply each one to complete the migration.";
            }
            else
            {
                manualMigrationStatusMessage = "⚠ No CS+ characters are currently linked to your in-game character. Apply a character first, then retry.";
            }
            manualMigrationStatusTime = DateTime.Now;
            manualMigrationPhysicalName = "";
        }

        private void ImportConfigurationFile()
        {
            Thread thread = new Thread(() =>
            {
                try
                {
                    using (OpenFileDialog openFileDialog = new OpenFileDialog())
                    {
                        openFileDialog.Filter = "JSON Configuration Files (*.json)|*.json|All Files (*.*)|*.*";
                        openFileDialog.Title = "Select Configuration File to Import";

                        if (openFileDialog.ShowDialog() == DialogResult.OK)
                        {
                            lock (this)
                            {
                                pendingImportPath = openFileDialog.FileName;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"[Settings] Error in import file dialog thread: {ex.Message}");
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private void RestoreFromBackup(string backupPath)
        {
            try
            {
                // Create emergency backup before restoring
                BackupManager.CreateEmergencyBackup(plugin.Configuration);

                var restoredConfig = BackupManager.ImportConfiguration(backupPath);
                if (restoredConfig != null)
                {
                    // Update the plugin interface reference using reflection
                    var pluginInterfaceField = restoredConfig.GetType()
                        .GetField("pluginInterface", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    pluginInterfaceField?.SetValue(restoredConfig, Plugin.PluginInterface);

                    // Copy all the important configuration data back to the current config
                    // This preserves the plugin instance while updating the data
                    var currentConfig = plugin.Configuration;

                    // Copy character data
                    currentConfig.Characters.Clear();
                    currentConfig.Characters.AddRange(restoredConfig.Characters);

                    // Copy all configuration properties using reflection
                    var configType = typeof(Configuration);
                    var properties = configType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                        .Where(p => p.CanWrite && p.Name != "Characters");

                    foreach (var prop in properties)
                    {
                        try
                        {
                            var value = prop.GetValue(restoredConfig);
                            prop.SetValue(currentConfig, value);
                        }
                        catch (Exception propEx)
                        {
                            Plugin.Log.Warning($"[Settings] Could not restore property {prop.Name}: {propEx.Message}");
                        }
                    }

                    // Save the updated configuration
                    currentConfig.Save();

                    lastBackupStatusMessage = $"✓ Configuration restored from {Path.GetFileName(backupPath)}";
                    lastBackupStatusTime = DateTime.Now;

                    Plugin.Log.Info($"[Settings] Successfully restored configuration from {backupPath}");
                }
                else
                {
                    lastBackupStatusMessage = "❌ Failed to restore configuration";
                    lastBackupStatusTime = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Settings] Error restoring from backup: {ex.Message}");
                lastBackupStatusMessage = "❌ Error restoring configuration";
                lastBackupStatusTime = DateTime.Now;
            }
        }

        private void RefreshAvailableBackups()
        {
            try
            {
                availableBackups = BackupManager.GetAvailableBackups();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Settings] Error refreshing available backups: {ex.Message}");
                availableBackups.Clear();
            }
        }

        private void AddImportedFileToBackups(string importPath)
        {
            try
            {
                var backupDirectory = Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "Backups");
                Directory.CreateDirectory(backupDirectory);

                string originalFileName = Path.GetFileName(importPath);
                string destinationPath = Path.Combine(backupDirectory, originalFileName);

                // If file already exists, add timestamp to avoid overwriting
                if (File.Exists(destinationPath))
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(originalFileName);
                    string extension = Path.GetExtension(originalFileName);
                    originalFileName = $"{nameWithoutExt}_{timestamp}{extension}";
                    destinationPath = Path.Combine(backupDirectory, originalFileName);
                }

                File.Copy(importPath, destinationPath, overwrite: false);

                // Update the file's LastWriteTime to current time so it appears at top of list
                File.SetLastWriteTime(destinationPath, DateTime.Now);

                lastBackupStatusMessage = $"✓ Imported file added to backups: {originalFileName}";
                lastBackupStatusTime = DateTime.Now;
                RefreshAvailableBackups();

                Plugin.Log.Info($"[Settings] Successfully imported file to backups: {destinationPath}");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Settings] Error adding imported file to backups: {ex.Message}");
                lastBackupStatusMessage = "❌ Error importing file to backups";
                lastBackupStatusTime = DateTime.Now;
            }
        }

        private void DeleteBackup(string backupFilePath, string backupFileName)
        {
            try
            {
                if (File.Exists(backupFilePath))
                {
                    File.Delete(backupFilePath);
                    lastBackupStatusMessage = $"✓ Deleted backup: {backupFileName}";
                    lastBackupStatusTime = DateTime.Now;
                    RefreshAvailableBackups();
                    Plugin.Log.Info($"[Settings] Successfully deleted backup: {backupFilePath}");
                }
                else
                {
                    lastBackupStatusMessage = "❌ Backup file not found";
                    lastBackupStatusTime = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Settings] Error deleting backup: {ex.Message}");
                lastBackupStatusMessage = "❌ Error deleting backup";
                lastBackupStatusTime = DateTime.Now;
            }
        }

        #region Custom Theme Editor

        private string? _pendingBackgroundImagePath = null;
    private string? _pendingWardrobeBackgroundImagePath = null;
        private Dictionary<string, bool> _colorCategoryExpanded = new();
        private string _presetNameBuffer = "";
        private bool _showPresetSavePopup = false;
        private bool _showPresetDeleteConfirm = false;
        private FontAwesomeIconPickerWindow? _iconPickerWindow = null;

        private void DrawCustomThemeEditor()
        {
            var totalScale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);
            var customTheme = plugin.Configuration.CustomTheme;

            // Each sub-section gets a boutique sub-header instead of a coloured
            // text label, matching the mockup's .group-head pattern.
            Boutique.SubSectionHeader("Preset Management", null, totalScale);
            DrawPresetManagement(customTheme, totalScale);

            Boutique.SubSectionHeader("Background Image", null, totalScale);
            DrawBackgroundImagePicker(customTheme, totalScale);

            Boutique.SubSectionHeader("Favourite Icon", null, totalScale);
            DrawFavoriteIconPicker(customTheme, totalScale);

            // Colour customisation
            int colourCount = 0;
            foreach (var c in CustomThemeDefinitions.GetColorCategories())
                colourCount += CustomThemeDefinitions.GetColorOptionsForCategory(c).Count()
                            + CustomThemeDefinitions.GetCustomColorOptionsForCategory(c).Count();
            Boutique.SubSectionHeader("Colour Customisation", $"{colourCount} colours", totalScale);

            // Global reset button as boutique outline button
            if (Boutique.OutlineButton("custom.resetAll", "RESET ALL COLOURS", totalScale))
            {
                customTheme.ColorOverrides.Clear();
                plugin.Configuration.Save();
            }
            if (ImGui.IsItemHovered())
                Boutique.Tooltip("Reset all colour customisations back to default values.");
            ImGui.Dummy(new Vector2(0, 6f * totalScale));

            // Draw ImGui color categories
            foreach (var category in CustomThemeDefinitions.GetColorCategories())
            {
                DrawColorCategory(category, customTheme, totalScale);
            }

            // Draw custom colour categories that aren't already covered above
            var imguiCategories = CustomThemeDefinitions.GetColorCategories().ToHashSet();
            foreach (var category in CustomThemeDefinitions.GetCustomColorCategories())
            {
                if (!imguiCategories.Contains(category))
                {
                    DrawCustomColorCategory(category, customTheme, totalScale);
                }
            }

            // Compact Quick Switch sub-section
            Boutique.SubSectionHeader("Compact Quick Switch", null, totalScale);
            DrawCompactQuickSwitchSettings(customTheme, totalScale);
        }

        private void DrawPresetManagement(CustomThemeConfig customTheme, float totalScale)
        {
            var presets = plugin.Configuration.ThemePresets;
            var activePreset = plugin.Configuration.ActivePresetName;
            var isEditingPreset = !string.IsNullOrEmpty(activePreset);

            if (isEditingPreset)
            {
                // Editing a saved preset, show preset name and Delete button
                ImGui.PushStyleColor(ImGuiCol.Text, Boutique.GreenSoft);
                ImGui.Text($"Editing: {activePreset}");
                ImGui.PopStyleColor();
                ImGui.SameLine(0, 12f * totalScale);

                if (Boutique.GhostButton("custom.preset.delete", "DELETE PRESET", totalScale))
                {
                    _showPresetDeleteConfirm = true;
                }
                if (ImGui.IsItemHovered())
                    Boutique.Tooltip($"Delete the '{activePreset}' preset.");

                ImGui.PushStyleColor(ImGuiCol.Text, Boutique.TextFaint);
                ImGui.Text("Changes are saved automatically.");
                ImGui.PopStyleColor();
            }
            else
            {
                // Custom (New).  When idle: show "+ SAVE AS PRESET" button.
                // When clicked: reveal an inline name + SAVE/CANCEL row in
                // the same place (no popup window).
                if (!_showPresetSavePopup)
                {
                    if (Boutique.PrimaryButton("custom.preset.saveAs", "+ SAVE AS PRESET", totalScale))
                    {
                        _showPresetSavePopup = true;
                        _presetNameBuffer = "My Theme";
                    }
                    if (ImGui.IsItemHovered())
                        Boutique.Tooltip("Save current settings as a new preset. Saved presets appear in the Theme dropdown.");
                }
                else
                {
                    // Inline naming row, replaces the old popup window
                    ImGui.PushStyleColor(ImGuiCol.Text, Boutique.TextFaint);
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("PRESET NAME");
                    ImGui.PopStyleColor();
                    ImGui.SameLine(0, 10f * totalScale);

                    var existingPreset = presets.FirstOrDefault(p =>
                        p.Name.Equals(_presetNameBuffer.Trim(), StringComparison.OrdinalIgnoreCase));
                    var saveButtonText = existingPreset != null ? "UPDATE" : "SAVE";

                    ImGui.SetNextItemWidth(200f * totalScale);
                    bool enterPressed = ImGui.InputText("##InlinePresetName", ref _presetNameBuffer, 50,
                        ImGuiInputTextFlags.EnterReturnsTrue);
                    ImGui.SameLine(0, 8f * totalScale);

                    bool savePressed = Boutique.PrimaryButton("custom.preset.inline.save", saveButtonText, totalScale);
                    ImGui.SameLine(0, 6f * totalScale);
                    bool cancelPressed = Boutique.GhostButton("custom.preset.inline.cancel", "CANCEL", totalScale);

                    if ((savePressed || enterPressed) && !string.IsNullOrWhiteSpace(_presetNameBuffer))
                    {
                        var trimmedName = _presetNameBuffer.Trim();
                        if (existingPreset != null)
                        {
                            existingPreset.Config = customTheme.Clone();
                        }
                        else
                        {
                            presets.Add(new ThemePreset { Name = trimmedName, Config = customTheme.Clone() });
                        }
                        plugin.Configuration.ActivePresetName = trimmedName;
                        plugin.Configuration.Save();
                        plugin.AchievementTracker?.OnThemePresetSaved();
                        _showPresetSavePopup = false;
                    }
                    if (cancelPressed) _showPresetSavePopup = false;
                }
            }

            ImGui.Dummy(new Vector2(0, 4f * totalScale));

            // Delete preset confirmation popup
            if (_showPresetDeleteConfirm)
            {
                ImGui.OpenPopup("Delete Preset?##DeletePresetConfirm");
            }

            var deletePopupOpen = true;
            if (ImGui.BeginPopupModal("Delete Preset?##DeletePresetConfirm", ref deletePopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text($"Are you sure you want to delete '{activePreset}'?");
                ImGui.Spacing();

                if (Boutique.OutlineButton("custom.preset.confirm.delete", "DELETE", totalScale))
                {
                    var presetToDelete = presets.FirstOrDefault(p => p.Name == activePreset);
                    if (presetToDelete != null) presets.Remove(presetToDelete);
                    plugin.Configuration.ActivePresetName = null;
                    customTheme.ColorOverrides.Clear();
                    customTheme.BackgroundImagePath = null;
                    customTheme.BackgroundImageOpacity = 0.3f;
                    customTheme.BackgroundImageZoom = 1.0f;
                    customTheme.BackgroundImageOffsetX = 0f;
                    customTheme.BackgroundImageOffsetY = 0f;
                    customTheme.FavoriteIconId = 0;
                    customTheme.UseNameplateColorForCardGlow = true;
                    plugin.Configuration.Save();
                    _showPresetDeleteConfirm = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine(0, 8f * totalScale);
                if (Boutique.GhostButton("custom.preset.confirm.cancel", "CANCEL", totalScale))
                {
                    _showPresetDeleteConfirm = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }

            if (!deletePopupOpen)
            {
                _showPresetDeleteConfirm = false;
            }
        }

        private void DrawBackgroundImagePicker(CustomThemeConfig customTheme, float totalScale)
        {
            // Check for pending file from file browser
            if (_pendingBackgroundImagePath != null)
            {
                string path;
                lock (this)
                {
                    path = _pendingBackgroundImagePath;
                    _pendingBackgroundImagePath = null;
                }

                if (File.Exists(path))
                {
                    customTheme.BackgroundImagePath = path;
                    plugin.Configuration.Save();
                    plugin.AchievementTracker?.OnCustomBgImageSet();
                }
            }

            // (Outer SubSectionHeader provides the section title now.)

            // Current image path display in TextDim
            var currentPath = customTheme.BackgroundImagePath ?? "None";
            if (currentPath.Length > 50)
                currentPath = "..." + currentPath.Substring(currentPath.Length - 47);
            ImGui.PushStyleColor(ImGuiCol.Text, Boutique.TextDim);
            ImGui.Text($"Current: {currentPath}");
            ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(0, 4f * totalScale));

            // Browse + Clear actions (Boutique buttons)
            if (Boutique.OutlineButton("custom.bg.browse", "BROWSE...", totalScale))
            {
                OpenBackgroundImageBrowser();
            }
            if (ImGui.IsItemHovered())
                Boutique.Tooltip("Select an image file to use as the main window background.");

            if (!string.IsNullOrEmpty(customTheme.BackgroundImagePath))
            {
                ImGui.SameLine(0, 8f * totalScale);
                if (Boutique.GhostButton("custom.bg.clear", "CLEAR", totalScale))
                {
                    customTheme.BackgroundImagePath = null;
                    plugin.Configuration.Save();
                }
                if (ImGui.IsItemHovered())
                    Boutique.Tooltip("Remove the background image.");
            }
            ImGui.Dummy(new Vector2(0, 4f * totalScale));

            // Opacity slider (only show if image is set)
            if (!string.IsNullOrEmpty(customTheme.BackgroundImagePath))
            {
                ImGui.Spacing();
                var opacity = customTheme.BackgroundImageOpacity;
                ImGui.SetNextItemWidth(200f * totalScale);
                if (ImGui.SliderFloat("Opacity", ref opacity, 0.0f, 1.0f, "%.2f"))
                {
                    customTheme.BackgroundImageOpacity = opacity;
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    plugin.Configuration.Save();
                }
                DrawTooltip("Adjust the opacity of the background image (0 = invisible, 1 = fully visible).");

                // Zoom slider
                ImGui.Spacing();
                var zoom = customTheme.BackgroundImageZoom;
                ImGui.SetNextItemWidth(200f * totalScale);
                if (ImGui.SliderFloat("Zoom", ref zoom, 0.5f, 3.0f, "%.2fx"))
                {
                    customTheme.BackgroundImageZoom = zoom;
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    plugin.Configuration.Save();
                }
                DrawTooltip("Zoom level for the background image (1.0 = fit to window, larger = zoomed in).");

                // Position X slider
                ImGui.Spacing();
                var posX = customTheme.BackgroundImageOffsetX;
                ImGui.SetNextItemWidth(200f * totalScale);
                if (ImGui.SliderFloat("Position X", ref posX, -1.0f, 1.0f, "%.2f"))
                {
                    customTheme.BackgroundImageOffsetX = posX;
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    plugin.Configuration.Save();
                }
                DrawTooltip("Horizontal position offset (-1 = left, 0 = center, 1 = right). Only affects zoomed-in images.");

                // Position Y slider
                ImGui.Spacing();
                var posY = customTheme.BackgroundImageOffsetY;
                ImGui.SetNextItemWidth(200f * totalScale);
                if (ImGui.SliderFloat("Position Y", ref posY, -1.0f, 1.0f, "%.2f"))
                {
                    customTheme.BackgroundImageOffsetY = posY;
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    plugin.Configuration.Save();
                }
                DrawTooltip("Vertical position offset (-1 = top, 0 = center, 1 = bottom). Only affects zoomed-in images.");

                // Reset button for position/zoom
                ImGui.Spacing();
                if (Boutique.GhostButton("custom.bg.resetPosZoom", "RESET POSITION & ZOOM", totalScale))
                {
                    customTheme.BackgroundImageZoom = 1.0f;
                    customTheme.BackgroundImageOffsetX = 0f;
                    customTheme.BackgroundImageOffsetY = 0f;
                    plugin.Configuration.Save();
                }
            }
        }

        private void DrawWardrobeBackgroundSettings(CustomThemeConfig customTheme, float totalScale)
        {
            // Check for pending file
            if (_pendingWardrobeBackgroundImagePath != null)
            {
                string path;
                lock (this) { path = _pendingWardrobeBackgroundImagePath; _pendingWardrobeBackgroundImagePath = null; }
                if (File.Exists(path))
                {
                    customTheme.WardrobeBackgroundImagePath = path;
                    plugin.Configuration.Save();
                }
            }

            bool open = Boutique.BoutiqueCollapsingHeader("Wardrobe Background Image", "WardBgHeader", false, totalScale);
            if (!open) return;

            ImGui.Indent(10 * totalScale);
            // Breathing room below the category bar
            ImGui.Dummy(new Vector2(0, 8f * totalScale));

            var currentPath = customTheme.WardrobeBackgroundImagePath ?? "None";
            if (currentPath.Length > 50) currentPath = "..." + currentPath.Substring(currentPath.Length - 47);
            ImGui.PushStyleColor(ImGuiCol.Text, Boutique.TextDim);
            ImGui.Text($"Current: {currentPath}");
            ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(0, 4f * totalScale));

            if (Boutique.OutlineButton("custom.wardBg.browse", "BROWSE...", totalScale))
            {
                plugin.OpenFilePicker(
                    "Select Wardrobe Background Image",
                    "Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
                    (selectedPath) => { lock (this) { _pendingWardrobeBackgroundImagePath = selectedPath; } },
                    null);
            }
            if (ImGui.IsItemHovered())
                Boutique.Tooltip("Select an image file to use as the Wardrobe window background.");

            if (!string.IsNullOrEmpty(customTheme.WardrobeBackgroundImagePath))
            {
                ImGui.SameLine(0, 8f * totalScale);
                if (Boutique.GhostButton("custom.wardBg.clear", "CLEAR", totalScale))
                {
                    customTheme.WardrobeBackgroundImagePath = null;
                    plugin.Configuration.Save();
                }
            }

            if (!string.IsNullOrEmpty(customTheme.WardrobeBackgroundImagePath))
            {
                ImGui.Spacing();
                var opacity = customTheme.WardrobeBackgroundImageOpacity;
                ImGui.SetNextItemWidth(200f * totalScale);
                if (ImGui.SliderFloat("Opacity##wardBg", ref opacity, 0.0f, 1.0f, "%.2f"))
                    customTheme.WardrobeBackgroundImageOpacity = opacity;
                if (ImGui.IsItemDeactivatedAfterEdit()) plugin.Configuration.Save();

                ImGui.Spacing();
                var zoom = customTheme.WardrobeBackgroundImageZoom;
                ImGui.SetNextItemWidth(200f * totalScale);
                if (ImGui.SliderFloat("Zoom##wardBg", ref zoom, 0.5f, 3.0f, "%.2fx"))
                    customTheme.WardrobeBackgroundImageZoom = zoom;
                if (ImGui.IsItemDeactivatedAfterEdit()) plugin.Configuration.Save();

                ImGui.Spacing();
                var posX = customTheme.WardrobeBackgroundImageOffsetX;
                ImGui.SetNextItemWidth(200f * totalScale);
                if (ImGui.SliderFloat("Position X##wardBg", ref posX, -1.0f, 1.0f, "%.2f"))
                    customTheme.WardrobeBackgroundImageOffsetX = posX;
                if (ImGui.IsItemDeactivatedAfterEdit()) plugin.Configuration.Save();

                ImGui.Spacing();
                var posY = customTheme.WardrobeBackgroundImageOffsetY;
                ImGui.SetNextItemWidth(200f * totalScale);
                if (ImGui.SliderFloat("Position Y##wardBg", ref posY, -1.0f, 1.0f, "%.2f"))
                    customTheme.WardrobeBackgroundImageOffsetY = posY;
                if (ImGui.IsItemDeactivatedAfterEdit()) plugin.Configuration.Save();

                ImGui.Spacing();
                if (Boutique.GhostButton("custom.wardBg.reset", "RESET POSITION & ZOOM", totalScale))
                {
                    customTheme.WardrobeBackgroundImageZoom = 1.0f;
                    customTheme.WardrobeBackgroundImageOffsetX = 0f;
                    customTheme.WardrobeBackgroundImageOffsetY = 0f;
                    plugin.Configuration.Save();
                }
            }

            ImGui.Unindent(10 * totalScale);
        }

        private void OpenBackgroundImageBrowser()
        {
            plugin.OpenFilePicker(
                "Select Background Image",
                "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                (selectedPath) =>
                {
                    lock (this)
                    {
                        _pendingBackgroundImagePath = selectedPath;
                    }
                }
            );
        }

        private void DrawFavoriteIconPicker(CustomThemeConfig customTheme, float totalScale)
        {
            // Check if icon picker window has confirmed a selection or was closed
            if (_iconPickerWindow != null)
            {
                if (_iconPickerWindow.Confirmed || !_iconPickerWindow.IsOpen)
                {
                    // Window was confirmed or closed - cleanup
                    // (Icon is already saved in real-time via OnIconChanged callback)
                    plugin.WindowSystem.RemoveWindow(_iconPickerWindow);
                    _iconPickerWindow = null;
                }
            }

            // (Outer SubSectionHeader provides the section title now.)

            var currentIconId = customTheme.FavoriteIconId;
            var currentIcon = currentIconId == 0 ? FontAwesomeIcon.Star : (FontAwesomeIcon)currentIconId;

            // Custom favourite icon colour
            Vector4 favoriteIconColor = new Vector4(1.0f, 0.85f, 0.0f, 1.0f);
            if (customTheme.ColorOverrides.TryGetValue("custom.favoriteIcon", out var packedFavColor) && packedFavColor.HasValue)
                favoriteIconColor = CustomThemeDefinitions.UnpackColor(packedFavColor.Value);

            // Current icon preview + actions inline
            ImGui.PushStyleColor(ImGuiCol.Text, Boutique.TextDim);
            ImGui.Text("Current:");
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.Text, favoriteIconColor);
            ImGui.Text(currentIcon.ToIconString());
            ImGui.PopStyleColor();
            ImGui.PopFont();
            ImGui.SameLine(0, 12f * totalScale);

            if (Boutique.OutlineButton("custom.icon.choose", "CHOOSE ICON...", totalScale))
            {
                _iconPickerWindow = new FontAwesomeIconPickerWindow(currentIcon, plugin.Configuration);
                _iconPickerWindow.OnIconChanged = (newIcon) =>
                {
                    customTheme.FavoriteIconId = newIcon == FontAwesomeIcon.Star ? 0 : (int)newIcon;
                    plugin.Configuration.Save();
                };
                plugin.WindowSystem.AddWindow(_iconPickerWindow);
                _iconPickerWindow.IsOpen = true;
            }

            if (currentIconId != 0)
            {
                ImGui.SameLine(0, 8f * totalScale);
                if (Boutique.GhostButton("custom.icon.reset", "RESET", totalScale))
                {
                    customTheme.FavoriteIconId = 0;
                    plugin.Configuration.Save();
                }
            }
            ImGui.Dummy(new Vector2(0, 4f * totalScale));
        }

        private void DrawCompactQuickSwitchSettings(CustomThemeConfig customTheme, float totalScale)
        {
            // Use the same expansion tracking as colour categories
            var categoryKey = "Compact Quick Switch";
            if (!_colorCategoryExpanded.ContainsKey(categoryKey))
            {
                _colorCategoryExpanded[categoryKey] = false;
            }

            var isExpanded = _colorCategoryExpanded[categoryKey];

            if (Boutique.BoutiqueCollapsingHeader(categoryKey, "CompactQSCategory", isExpanded, totalScale))
            {
                _colorCategoryExpanded[categoryKey] = true;
                ImGui.Indent(10f);

                // Breathing room below the bar
                ImGui.Dummy(new Vector2(0, 8f * totalScale));

                ImGui.PushStyleColor(ImGuiCol.Text, Boutique.TextDim);
                ImGui.TextWrapped("These settings only affect the compact version of the Quick Character Switch bar.");
                ImGui.PopStyleColor();
                ImGui.Spacing();

                var buttonOpacity = customTheme.CompactQuickSwitchButtonOpacity;
                ImGui.Text("Background Opacity");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(150 * totalScale);
                if (ImGui.SliderFloat("##CompactButtonOpacity", ref buttonOpacity, 0.0f, 1.0f, "%.2f"))
                {
                    customTheme.CompactQuickSwitchButtonOpacity = buttonOpacity;
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    plugin.Configuration.Save();
                }
                DrawTooltip("Adjusts the transparency of the compact Quick Switch bar background.\n0 = fully transparent, 1 = fully opaque.");

                if (Math.Abs(customTheme.CompactQuickSwitchButtonOpacity - 1.0f) > 0.01f)
                {
                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Boutique.WithAlpha(Boutique.Gold, 0.18f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, Boutique.WithAlpha(Boutique.Gold, 0.30f));
                    ImGui.PushStyleColor(ImGuiCol.Text, Boutique.GoldWarm);
                    if (ImGui.SmallButton("RESET##CompactOpacity"))
                    {
                        customTheme.CompactQuickSwitchButtonOpacity = 1.0f;
                        plugin.Configuration.Save();
                    }
                    ImGui.PopStyleColor(4);
                }

                ImGui.Spacing();

                bool useNameplateColor = customTheme.CompactQuickSwitchUseNameplateColor;
                if (ImGui.Checkbox("Use Nameplate Colour##CompactQSUseNp", ref useNameplateColor))
                {
                    customTheme.CompactQuickSwitchUseNameplateColor = useNameplateColor;
                    plugin.Configuration.Save();
                }
                DrawTooltip("On: chassis + Apply button colour follows the active character's nameplate colour.\nOff: use the manual colour below.");

                if (!customTheme.CompactQuickSwitchUseNameplateColor)
                {
                    var accentDefault = new Vector3(0.40f, 0.42f, 0.48f);
                    Vector3 accent = accentDefault;
                    if (customTheme.ColorOverrides.TryGetValue("custom.compactQS.accent", out var packedAccent) && packedAccent.HasValue)
                    {
                        var unpacked = CustomThemeDefinitions.UnpackColor(packedAccent.Value);
                        accent = new Vector3(unpacked.X, unpacked.Y, unpacked.Z);
                    }
                    ImGui.Text("Bar Colour");
                    ImGui.SameLine(180f * totalScale);
                    ImGui.SetNextItemWidth(150 * totalScale);
                    if (ImGui.ColorEdit3("##CompactQSAccent", ref accent, ImGuiColorEditFlags.NoInputs))
                    {
                        customTheme.ColorOverrides["custom.compactQS.accent"] = CustomThemeDefinitions.PackColor(new Vector4(accent, 1f));
                        plugin.Configuration.Save();
                    }
                    DrawTooltip("Manual chassis + Apply button colour for the compact bar.");
                }

                var applyTextDefault = new Vector3(0.05f, 0.05f, 0.08f);
                Vector3 applyText = applyTextDefault;
                if (customTheme.ColorOverrides.TryGetValue("custom.compactQS.applyText", out var packedAt) && packedAt.HasValue)
                {
                    var unpacked = CustomThemeDefinitions.UnpackColor(packedAt.Value);
                    applyText = new Vector3(unpacked.X, unpacked.Y, unpacked.Z);
                }
                ImGui.Text("Apply Button Text");
                ImGui.SameLine(180f * totalScale);
                ImGui.SetNextItemWidth(150 * totalScale);
                if (ImGui.ColorEdit3("##CompactQSApplyText", ref applyText, ImGuiColorEditFlags.NoInputs))
                {
                    customTheme.ColorOverrides["custom.compactQS.applyText"] = CustomThemeDefinitions.PackColor(new Vector4(applyText, 1f));
                    plugin.Configuration.Save();
                }
                DrawTooltip("Apply button label colour. Default is near-black for contrast against the bar colour.");

                if (customTheme.ColorOverrides.ContainsKey("custom.compactQS.applyText"))
                {
                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Boutique.WithAlpha(Boutique.Gold, 0.18f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, Boutique.WithAlpha(Boutique.Gold, 0.30f));
                    ImGui.PushStyleColor(ImGuiCol.Text, Boutique.GoldWarm);
                    if (ImGui.SmallButton("RESET##CompactQSApplyText"))
                    {
                        customTheme.ColorOverrides.Remove("custom.compactQS.applyText");
                        plugin.Configuration.Save();
                    }
                    ImGui.PopStyleColor(4);
                }

                ImGui.Unindent(10f);
            }
            else
            {
                _colorCategoryExpanded[categoryKey] = false;
            }
        }

        private void DrawColorCategory(string category, CustomThemeConfig customTheme, float totalScale)
        {
            // Initialize category expansion state if needed
            if (!_colorCategoryExpanded.ContainsKey(category))
            {
                _colorCategoryExpanded[category] = false;
            }

            var isExpanded = _colorCategoryExpanded[category];

            if (Boutique.BoutiqueCollapsingHeader(category, $"ColorCategory_{category}", isExpanded, totalScale))
            {
                _colorCategoryExpanded[category] = true;

                ImGui.Indent(10f);

                // Breathing room above the Reset button so it doesn't sit
                // glued to the category bar above it.
                ImGui.Dummy(new Vector2(0, 8f * totalScale));

                // Reset category button (boutique outline)
                if (Boutique.OutlineButton($"custom.cat.reset.{category}", $"RESET {category.ToUpperInvariant()}", totalScale))
                {
                    foreach (var option in CustomThemeDefinitions.GetColorOptionsForCategory(category))
                        customTheme.ColorOverrides.Remove(option.Key);
                    foreach (var option in CustomThemeDefinitions.GetCustomColorOptionsForCategory(category))
                        customTheme.ColorOverrides.Remove(option.Key);
                    plugin.Configuration.Save();
                }
                if (ImGui.IsItemHovered())
                    Boutique.Tooltip($"Reset all {category} colours to default.");

                ImGui.Dummy(new Vector2(0, 6f * totalScale));

                // Draw ImGui color options for this category
                foreach (var option in CustomThemeDefinitions.GetColorOptionsForCategory(category))
                {
                    DrawColorOption(option, customTheme, totalScale);
                }

                // Draw custom color options for this category (e.g., Design Panel Background)
                foreach (var option in CustomThemeDefinitions.GetCustomColorOptionsForCategory(category))
                {
                    DrawCustomColorOption(option, customTheme, totalScale);
                }

                ImGui.Unindent(10f);
            }
            else
            {
                _colorCategoryExpanded[category] = false;
            }
        }

        private void DrawColorOption(CustomThemeDefinitions.ColorOption option, CustomThemeConfig customTheme, float totalScale)
        {
            // Get current value (override or default)
            Vector4 currentColor;
            bool hasOverride = customTheme.ColorOverrides.TryGetValue(option.Key, out var packedColor) && packedColor.HasValue;

            if (hasOverride)
            {
                currentColor = CustomThemeDefinitions.UnpackColor(packedColor!.Value);
            }
            else
            {
                currentColor = option.DefaultValue;
            }

            // Label
            ImGui.AlignTextToFramePadding();
            ImGui.Text(option.Label);

            if (!string.IsNullOrEmpty(option.Description))
            {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, Boutique.TextFaint);
                ImGui.Text("(?)");
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                {
                    Boutique.Tooltip(option.Description);
                }
            }

            ImGui.SameLine(200f * totalScale);

            // Color picker
            ImGui.SetNextItemWidth(150f * totalScale);
            if (ImGui.ColorEdit4($"##{option.Key}", ref currentColor, ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
            {
                customTheme.ColorOverrides[option.Key] = CustomThemeDefinitions.PackColor(currentColor);
                plugin.Configuration.Save();
            }

            // Reset: small boutique outline button when there's an override,
            // greyed-out placeholder otherwise. GoldDeep border, GoldWarm
            // label, gold@10% hover bg, near-zero rounding for the chamfered
            // boutique read.
            ImGui.SameLine();
            if (hasOverride)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Boutique.WithAlpha(Boutique.Gold, 0.10f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, Boutique.WithAlpha(Boutique.Gold, 0.22f));
                ImGui.PushStyleColor(ImGuiCol.Border, Boutique.GoldDeep);
                ImGui.PushStyleColor(ImGuiCol.Text, Boutique.GoldWarm);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f * totalScale, 3f * totalScale));
                if (ImGui.SmallButton($"RESET##{option.Key}"))
                {
                    customTheme.ColorOverrides.Remove(option.Key);
                    plugin.Configuration.Save();
                }
                ImGui.PopStyleVar(3);
                ImGui.PopStyleColor(5);
            }
            else
            {
                // Disabled placeholder (matches the live button's shape)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.Border, Boutique.WithAlpha(Boutique.GoldDeep, 0.30f));
                ImGui.PushStyleColor(ImGuiCol.Text, Boutique.WithAlpha(Boutique.TextGhost, 0.6f));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f * totalScale, 3f * totalScale));
                ImGui.BeginDisabled();
                ImGui.SmallButton($"RESET##{option.Key}");
                ImGui.EndDisabled();
                ImGui.PopStyleVar(3);
                ImGui.PopStyleColor(3);
            }
        }

        private void DrawCustomColorCategory(string category, CustomThemeConfig customTheme, float totalScale)
        {
            // Initialize category expansion state if needed
            if (!_colorCategoryExpanded.ContainsKey(category))
            {
                _colorCategoryExpanded[category] = false;
            }

            var isExpanded = _colorCategoryExpanded[category];

            if (Boutique.BoutiqueCollapsingHeader(category, $"CustomColorCategory_{category}", isExpanded, totalScale))
            {
                _colorCategoryExpanded[category] = true;

                ImGui.Indent(10f);

                // Breathing room above Reset (don't glue it to the category bar)
                ImGui.Dummy(new Vector2(0, 8f * totalScale));

                if (Boutique.OutlineButton($"custom.cat.resetCustom.{category}", $"RESET {category.ToUpperInvariant()}", totalScale))
                {
                    foreach (var option in CustomThemeDefinitions.GetCustomColorOptionsForCategory(category))
                        customTheme.ColorOverrides.Remove(option.Key);
                    plugin.Configuration.Save();
                }
                if (ImGui.IsItemHovered())
                    Boutique.Tooltip($"Reset all {category} colours to default.");

                ImGui.Dummy(new Vector2(0, 6f * totalScale));

                // Special handling for Accents category, card glow toggle
                if (category == "Accents")
                {
                    var useNameplateColor = customTheme.UseNameplateColorForCardGlow;
                    if (ImGui.Checkbox("Use Nameplate Color for Card Glow", ref useNameplateColor))
                    {
                        customTheme.UseNameplateColorForCardGlow = useNameplateColor;
                        plugin.Configuration.Save();
                    }
                    DrawTooltip("When enabled, character cards use each character's individual nameplate color.\nWhen disabled, all cards use the custom color below.");
                    ImGui.Spacing();
                }

                // Custom color options for this category
                foreach (var option in CustomThemeDefinitions.GetCustomColorOptionsForCategory(category))
                {
                    if (option.Key == "custom.cardGlow" && customTheme.UseNameplateColorForCardGlow)
                    {
                        ImGui.BeginDisabled();
                        DrawCustomColorOption(option, customTheme, totalScale);
                        ImGui.EndDisabled();
                    }
                    else
                    {
                        DrawCustomColorOption(option, customTheme, totalScale);
                    }
                }

                // Wardrobe category: include background image controls inline
                if (category == "Wardrobe")
                {
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();
                    DrawWardrobeBackgroundSettings(customTheme, totalScale);
                }

                ImGui.Unindent(10f);
            }
            else
            {
                _colorCategoryExpanded[category] = false;
            }
        }

        private void DrawCustomColorOption(CustomThemeDefinitions.CustomColorOption option, CustomThemeConfig customTheme, float totalScale)
        {
            // Get current value (override or default)
            Vector4 currentColor;
            bool hasOverride = customTheme.ColorOverrides.TryGetValue(option.Key, out var packedColor) && packedColor.HasValue;

            if (hasOverride)
            {
                currentColor = CustomThemeDefinitions.UnpackColor(packedColor!.Value);
            }
            else
            {
                currentColor = option.DefaultValue;
            }

            // Label
            ImGui.AlignTextToFramePadding();
            ImGui.Text(option.Label);

            if (!string.IsNullOrEmpty(option.Description))
            {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, Boutique.TextFaint);
                ImGui.Text("(?)");
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                {
                    Boutique.Tooltip(option.Description);
                }
            }

            ImGui.SameLine(200f * totalScale);

            // Color picker
            ImGui.SetNextItemWidth(150f * totalScale);
            if (ImGui.ColorEdit4($"##{option.Key}", ref currentColor, ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
            {
                customTheme.ColorOverrides[option.Key] = CustomThemeDefinitions.PackColor(currentColor);
                plugin.Configuration.Save();
            }

            // Reset: small boutique outline button when there's an override,
            // greyed-out placeholder otherwise. GoldDeep border, GoldWarm
            // label, gold@10% hover bg, near-zero rounding for the chamfered
            // boutique read.
            ImGui.SameLine();
            if (hasOverride)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Boutique.WithAlpha(Boutique.Gold, 0.10f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, Boutique.WithAlpha(Boutique.Gold, 0.22f));
                ImGui.PushStyleColor(ImGuiCol.Border, Boutique.GoldDeep);
                ImGui.PushStyleColor(ImGuiCol.Text, Boutique.GoldWarm);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f * totalScale, 3f * totalScale));
                if (ImGui.SmallButton($"RESET##{option.Key}"))
                {
                    customTheme.ColorOverrides.Remove(option.Key);
                    plugin.Configuration.Save();
                }
                ImGui.PopStyleVar(3);
                ImGui.PopStyleColor(5);
            }
            else
            {
                // Disabled placeholder (matches the live button's shape)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.Border, Boutique.WithAlpha(Boutique.GoldDeep, 0.30f));
                ImGui.PushStyleColor(ImGuiCol.Text, Boutique.WithAlpha(Boutique.TextGhost, 0.6f));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f * totalScale, 3f * totalScale));
                ImGui.BeginDisabled();
                ImGui.SmallButton($"RESET##{option.Key}");
                ImGui.EndDisabled();
                ImGui.PopStyleVar(3);
                ImGui.PopStyleColor(3);
            }
        }

        #endregion

        private void DrawTooltip(string text)
        {
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300f);
                ImGui.TextUnformatted(text);
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
        }

        /// <summary>
        /// Expands a specific settings section by name.
        /// Used by feature spotlight cards to navigate directly to relevant settings.
        /// </summary>
        public void ExpandSection(string sectionName)
        {
            if (!CategoryNameToIndex.ContainsKey(sectionName)
                && sectionName != "Community & Moderation")
            {
                Plugin.Log.Warning($"[SettingsPanel] Unknown section name: {sectionName}");
                return;
            }
            pendingExpandSection = sectionName;
        }

        /// <summary>
        /// Parses a character assignment value into character name and optional design name.
        /// Supports formats: "CharName" (legacy), "Character:CharName", "Design:CharName:DesignName"
        /// </summary>
        private (string CharacterName, string? DesignName) ParseCharacterAssignmentValue(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "None")
                return (value ?? "", null);

            if (value.StartsWith("Design:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = value.Substring("Design:".Length).Split(':', 2);
                return parts.Length >= 2 ? (parts[0], parts[1]) : (parts[0], null);
            }

            if (value.StartsWith("Character:", StringComparison.OrdinalIgnoreCase))
            {
                return (value.Substring("Character:".Length), null);
            }

            // Legacy format - just the character name
            return (value, null);
        }

        /// <summary>
        /// Builds a character assignment value string from character name and optional design name.
        /// </summary>
        private string BuildCharacterAssignmentValue(string characterName, string? designName)
        {
            if (characterName == "None")
                return "None";

            if (!string.IsNullOrEmpty(designName))
                return $"Design:{characterName}:{designName}";

            return $"Character:{characterName}";
        }
    }
}

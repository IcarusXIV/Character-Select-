using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using CharacterSelectPlugin.Windows.Styles;
using CharacterSelectPlugin.Windows.Utils;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Dalamud.Interface.Textures.TextureWraps;
using CharacterSelectPlugin.Effects;

namespace CharacterSelectPlugin.Windows.Components
{
    public class DesignPanel : IDisposable
    {
        private Plugin plugin;
        private UIStyles uiStyles;

        public bool IsOpen { get; private set; } = false;
        private int activeCharacterIndex = -1;
        private Dictionary<string, FavoriteSparkEffect> designFavoriteEffects = new();

        // Open/close animation. _animT in [0,1] driven toward _animTarget.
        private float _animT = 0f;
        private float _animTarget = 0f;
        private double _animLastTime = -1;
        private const float OpenAnimDuration = 0.22f;  // 220ms full open/close

        /// <summary>True while either fully open OR mid-close-animation.
        /// Used so the panel keeps rendering as it slides shut.</summary>
        public bool IsVisible => IsOpen || _animT > 0.001f;

        /// <summary>Eased panel width for layout, 0 when fully closed,
        /// PanelWidth when fully open, smoothly interpolated mid-animation.</summary>
        public float GetAnimatedPanelWidth()
        {
            // smoothstep, symmetric ease-in-out so open and close both feel natural
            float e = _animT * _animT * (3f - 2f * _animT);
            return PanelWidth * e;
        }

        // Resizable panel
        public float PanelWidth { get; private set; } = 360f;
        private const float MinPanelWidth = 240f;
        private const float MaxPanelWidth = 600f;
        private bool isResizing = false;
        private float resizeHandleWidth = 8f;

        // Search functionality
        private bool showSearchBar = false;
        private string searchQuery = "";
        
        // Design editing state
        private bool isEditDesignWindowOpen = false;
        // Frame counter: SetScrollY applies on NEXT frame's layout pass, so applying
        // it once leaves the first visible frame at the previous scroll. 3 frames
        // absorbs the lag and guarantees the form opens at the top.
        private int _dpFormScrollResetFramesPending = 0;
        private bool isAdvancedModeDesign = false;
        private bool isAdvancedModeWindowOpen = false;
        private bool isNewDesign = false;
        private bool isSecretDesignMode = false;

        // Layout state for the inline design form (matches CharacterForm pattern).
        // _dpFormIndent = left breathing space, _dpFormContentWidth = capped width
        // for fields. Chrome (title row, section dividers) extends to full width.
        private float _dpFormIndent = 0f;
        private float _dpFormContentWidth = 0f;

        // Edit fields
        private string editedDesignName = "";
        private string editedDesignMacro = "";
        private string editedGlamourerDesign = "";
        private string editedAutomation = "";
        private string editedCustomizeProfile = "";
        private int? editedGearset = null;
        private string editedDesignPreviewPath = "";
        private string advancedDesignMacroText = "";
        private string originalAdvancedMacroText = "";
        private string originalDesignName = "";
        private string? pendingDesignImagePath = null;
        private string? pendingPastedImagePath = null;
        
        // Temporary Secret Mode state for new designs
        private Dictionary<string, bool>? temporaryDesignSecretModState = null;
        private HashSet<string>? temporaryDesignSecretModPinOverrides = null;

        // Design sorting
        private enum DesignSortType { Favorites, Alphabetical, Recent, Oldest, Manual }
        private DesignSortType currentDesignSort => GetDesignSortFromConfig();

        // Folder management
        private string newFolderName = "";
        private bool isRenamingFolder = false;
        private Guid renameFolderId;
        private string renameFolderBuf = "";
        private DesignFolder? draggedFolder = null;
        private CharacterDesign? draggedDesign = null;
        private Vector3? newFolderSelectedColor = null;
        // Boutique list collapse state (per-folder, in-memory only)
        private Dictionary<Guid, bool> boutiqueFolderCollapsed = new();
        // Per-row hover-lift progress (0..1), eased over 150ms
        private Dictionary<Guid, float> boutiqueRowLiftT = new();
        // Search input focus state (drives gold border + magnifier glow)
        private bool searchInputFocused;
        // Deferred sort after favourite toggle, lets the spark VFX play in place before
        // the row re-shuffles. We snapshot the design's PRE-toggle IsFavorite value, and
        // BuildRenderItems uses that for the sort key until the delay expires.
        private Dictionary<Guid, (bool wasFavBefore, DateTime expiresAt)> pendingFavSortHold = new();
        private const float FavSortDelayMs = 700f;
        // Row slide animation, tracks last natural Y and an active displacement that
        // eases back to zero over ~350ms when the row's logical position changes.
        private Dictionary<Guid, float> rowLastNaturalY = new();
        private Dictionary<Guid, (float displacement, double startTime)> rowSlideAnim = new();
        private const float RowSlideDurationS = 0.35f;

        // Import window
        private bool isImportWindowOpen = false;
        private Character? targetForDesignImport = null;

        // Snapshot dialog
        private bool isSnapshotDialogOpen = false;
        private string snapshotDesignName = "";
        private bool snapshotUseConflictResolution = true;
        private Character? snapshotTargetCharacter = null;
        private HashSet<string> snapshotDetectedMods = new();
        private string? snapshotDetectedCustomizePlusProfile = null;
        private bool snapshotHasClipboardImage = false;
        private bool snapshotIsProcessing = false;
        private string snapshotStatusMessage = "";

        public DesignPanel(Plugin plugin, UIStyles uiStyles)
        {
            this.plugin = plugin;
            this.uiStyles = uiStyles;

            // Load saved panel width or use default
            PanelWidth = plugin.Configuration.DesignPanelWidth;
        }

        public void Dispose()
        {
            // Save panel width on dispose
            plugin.Configuration.DesignPanelWidth = PanelWidth;
            plugin.Configuration.Save();
        }

        public void Draw()
        {
            if (!IsVisible) return;

            // Calculate responsive sizing
            var totalScale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);

            // Use the ANIMATED width so the panel slides in/out smoothly.
            // Mid-animation this is between 0 and PanelWidth.
            float scaledPanelWidth = GetAnimatedPanelWidth() * GetSafeScale(totalScale);
            float scaledMinWidth = MinPanelWidth * totalScale;
            float scaledMaxWidth = MaxPanelWidth * totalScale;
            float scaledHandleWidth = resizeHandleWidth * totalScale;

            DrawDesignPanelContent(totalScale, scaledPanelWidth);
            DrawResizeHandle(totalScale, scaledPanelWidth, scaledMinWidth, scaledMaxWidth, scaledHandleWidth);

            if (IsOpen)
            {
                UpdateEffects();
                ProcessPendingSort();
            }

            DrawImportWindow(totalScale);
            DrawAdvancedModeWindow(totalScale);
            DrawSnapshotDialog(totalScale);
        }

        /// <summary>Advance the open/close animation.  Called every frame
        /// from MainWindow regardless of whether the panel is currently
        /// rendering, so the animation never deadlocks on a render gate
        /// that depends on _animT > 0.</summary>
        public void TickAnimation()
        {
            double now = ImGui.GetTime();
            double dt = _animLastTime <= 0 ? 0 : Math.Min(now - _animLastTime, 0.1);
            _animLastTime = now;

            if (Math.Abs(_animT - _animTarget) < 0.001f)
            {
                _animT = _animTarget;
                return;
            }

            float step = (float)dt / OpenAnimDuration;
            if (_animT < _animTarget)
                _animT = Math.Min(_animTarget, _animT + step);
            else
                _animT = Math.Max(_animTarget, _animT - step);

            // Layout depends on the animated width; force a recompute mid-animation
            // so the grid reflows column count smoothly as the panel slides.
            plugin.MainWindow?.InvalidateLayout();
        }

        private void DrawResizeHandle(float totalScale, float scaledPanelWidth, float scaledMinWidth, float scaledMaxWidth, float scaledHandleWidth)
        {
            // Current window position and size
            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();

            // Position handle at the very left edge of the design panel window
            var handleMin = new Vector2(windowPos.X, windowPos.Y);
            var handleMax = new Vector2(windowPos.X + scaledHandleWidth, windowPos.Y + windowSize.Y);

            // Check if mouse is over the handle area
            bool hovered = ImGui.IsMouseHoveringRect(handleMin, handleMax);

            // Capture mouse input when over resize handle to prevent window dragging
            if (hovered || isResizing)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);

                if (hovered && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseDown(ImGuiMouseButton.Left)))
                {
                    ImGui.SetItemAllowOverlap();

                    // Create an invisible button over the resize area to capture input
                    ImGui.SetCursorScreenPos(handleMin);
                    ImGui.InvisibleButton("##resize_handle", new Vector2(scaledHandleWidth, windowSize.Y));

                    if (ImGui.IsItemActive() || isResizing)
                    {
                        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                        {
                            isResizing = true;
                        }
                    }
                }
            }

            // Handle resizing
            if (isResizing)
            {
                if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    // Current mouse position
                    float currentMouseX = ImGui.GetMousePos().X;
                    // Calculate new width based on mouse position relative to the window's right edge
                    float windowRightEdge = ImGui.GetWindowPos().X + ImGui.GetWindowSize().X;
                    float newScaledWidth = windowRightEdge - currentMouseX;
                    // Convert to base units and clamp
                    float newWidth = newScaledWidth / totalScale;
                    PanelWidth = Math.Clamp(newWidth, MinPanelWidth, MaxPanelWidth);
                    // Save the new width immediately for responsiveness
                    plugin.Configuration.DesignPanelWidth = PanelWidth;
                    // Force main window to recalculate layout
                    if (plugin.MainWindow != null)
                    {
                        plugin.MainWindow.InvalidateLayout();
                    }
                }
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    isResizing = false;
                    // Save configuration
                    plugin.Configuration.Save();
                }
            }

            // Draw visual resize handle
            var drawList = ImGui.GetWindowDrawList();
            uint handleColor = hovered || isResizing
                ? ImGui.GetColorU32(new Vector4(0.6f, 0.6f, 0.8f, 0.8f))
                : ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.6f, 0.3f));

            // Subtle line at left edge
            drawList.AddLine(
                new Vector2(handleMin.X + 2 * totalScale, handleMin.Y + 10 * totalScale),
                new Vector2(handleMin.X + 2 * totalScale, handleMax.Y - 10 * totalScale),
                handleColor,
                2f * totalScale
            );

            // Draw resize grip dots when hovered
            if (hovered || isResizing)
            {
                float dotSize = 2f * totalScale;
                float dotSpacing = 6f * totalScale;
                var centerX = handleMin.X + scaledHandleWidth / 2;
                var centerY = handleMin.Y + windowSize.Y / 2;
                for (int i = -2; i <= 2; i++)
                {
                    drawList.AddCircleFilled(
                        new Vector2(centerX, centerY + i * dotSpacing),
                        dotSize,
                        handleColor
                    );
                }
            }
        }
        private float GetSafeScale(float baseScale)
        {
            return Math.Clamp(baseScale, 0.3f, 5.0f);
        }

        private void UpdateEffects()
        {
            float deltaTime = ImGui.GetIO().DeltaTime;
            foreach (var effect in designFavoriteEffects.Values)
            {
                effect.Update(deltaTime);
            }

            // Draw sparks to the FOREGROUND draw list so they sit on top of all
            // window/child content (otherwise the row's body paint is layered above them).
            var fg = ImGui.GetForegroundDrawList();
            foreach (var kvp in designFavoriteEffects.ToList())
            {
                kvp.Value.Draw(fg);

                if (!kvp.Value.IsActive)
                {
                    designFavoriteEffects.Remove(kvp.Key);
                }
            }
        }

        public void Open(int characterIndex)
        {
            activeCharacterIndex = characterIndex;
            IsOpen = true;
            _animTarget = 1f;
            _animLastTime = ImGui.GetTime();
            plugin.IsDesignPanelOpen = true;
            // Force grid to recompute column count from the new available width.
            plugin.MainWindow?.InvalidateLayout();
        }

        public void Close()
        {
            IsOpen = false;
            _animTarget = 0f;
            _animLastTime = ImGui.GetTime();
            activeCharacterIndex = -1;
            plugin.IsDesignPanelOpen = false;
            plugin.MainWindow?.InvalidateLayout();
            
            // Close Mod Manager window if it's open
            if (plugin.SecretModeModWindow?.IsOpen ?? false)
            {
                plugin.SecretModeModWindow.IsOpen = false;
            }
            
            CloseDesignEditor();
        }

        private void DrawDesignPanelContent(float totalScale, float scaledPanelWidth)
        {
            if (activeCharacterIndex < 0 || activeCharacterIndex >= plugin.Characters.Count)
                return;

            var character = plugin.Characters[activeCharacterIndex];
            // Legacy entry point, used if Draw() is called outside of MainWindow.
            // DrawIntoRect is the preferred host-driven path.
            var winPos = ImGui.GetCursorScreenPos();
            var winSize = ImGui.GetContentRegionAvail();
            DrawBoutiqueDesignPanelAt(character, totalScale, winPos, winPos + winSize);
        }

        // BOUTIQUE DESIGN PANEL, translation of design-panel.html
        //   Layout: head strip (36px) → actionbar (44px) → search row (36px,
        //   conditional) → sort subbar (38px) → list (flex)
        // Public entry point: draws the boutique panel chrome to the CURRENT window's
        // draw list at the explicit rect. The host (MainWindow) calls this without
        // wrapping in BeginChild so the chrome renders directly into the main window.
        public void DrawIntoRect(Vector2 panelMin, Vector2 panelMax, float totalScale)
        {
            if (!IsVisible) return;
            if (activeCharacterIndex < 0 || activeCharacterIndex >= plugin.Characters.Count) return;
            var character = plugin.Characters[activeCharacterIndex];
            DrawBoutiqueDesignPanelAt(character, totalScale, panelMin, panelMax);
            // Resize handle on the LEFT edge of the panel.
            DrawResizeHandleAt(panelMin, panelMax, totalScale);
            // Particle/spark effects (favourite-toggle sparkles), needed for the host-driven path
            UpdateEffects();
            // Apply any deferred favourite-sort that's now past its delay
            ProcessPendingSort();
            // Popout windows (advanced macro editor, design import, snapshot dialog).
            // These are top-level ImGui windows, the legacy Draw() path renders
            // them, so DrawIntoRect must too or the advanced-mode toggle has no
            // visible effect when DesignPanel is hosted by MainWindow.
            DrawImportWindow(totalScale);
            DrawAdvancedModeWindow(totalScale);
            DrawSnapshotDialog(totalScale);
        }

        /// <summary>
        /// Resize handle at the panel's left edge, drag to resize the panel.
        /// Replaces DrawResizeHandle which used ImGui.GetWindow{Pos,Size} (now
        /// returns the main window, not the panel area).
        /// </summary>
        private void DrawResizeHandleAt(Vector2 panelMin, Vector2 panelMax, float totalScale)
        {
            float handleW = resizeHandleWidth * totalScale;
            var handleMin = new Vector2(panelMin.X - handleW * 0.5f, panelMin.Y);
            var handleMax = new Vector2(panelMin.X + handleW * 0.5f, panelMax.Y);
            bool hovered = ImGui.IsMouseHoveringRect(handleMin, handleMax);

            if (hovered || isResizing)
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);

            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !isResizing)
                isResizing = true;

            if (isResizing)
            {
                if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    float currentMouseX = ImGui.GetMousePos().X;
                    float panelRightEdge = panelMax.X;
                    float newScaledWidth = panelRightEdge - currentMouseX;
                    float newWidth = newScaledWidth / totalScale;
                    PanelWidth = Math.Clamp(newWidth, MinPanelWidth, MaxPanelWidth);
                    plugin.Configuration.DesignPanelWidth = PanelWidth;
                    plugin.MainWindow?.InvalidateLayout();
                }
                else
                {
                    isResizing = false;
                    plugin.Configuration.Save();
                }
            }

            // Visible 1px hairline + a brighter strip on hover
            var dl = ImGui.GetWindowDrawList();
            uint hairCol = (hovered || isResizing)
                ? Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.55f))
                : Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.10f));
            dl.AddLine(new Vector2(panelMin.X, panelMin.Y + 6f * totalScale),
                       new Vector2(panelMin.X, panelMax.Y - 6f * totalScale),
                       hairCol, (hovered || isResizing) ? 2f * totalScale : 1f * totalScale);
        }

        private void DrawBoutiqueDesignPanelAt(Character character, float scale, Vector2 winPos, Vector2 winMax)
        {
            var dl = ImGui.GetWindowDrawList();
            var winSize = winMax - winPos;
            double time = ImGui.GetTime();

            float headH = 38f * scale;
            float actionH = 44f * scale;
            float searchH = 36f * scale;
            float subbarH = 36f * scale;

            var headMin = winPos;
            var headMax = new Vector2(winMax.X, winPos.Y + headH);

            // Background fill: routes through custom.designPanelBg so the
            // editor's "Design Panel" override drives the visible body. Top is
            // the override (or boutique default), bottom is darkened slightly
            // to keep the velvet-fade feel of the mockup.
            Vector4 dpBgTop = Boutique.SlotOrDefault("custom.designPanelBg", Boutique.Surface1);
            Vector4 dpBgBot = Boutique.Lerp(dpBgTop, new Vector4(0f, 0f, 0f, dpBgTop.W), 0.30f);
            uint bgTop = Boutique.U32(dpBgTop);
            uint bgBot = Boutique.U32(dpBgBot);
            dl.AddRectFilledMultiColor(winPos, winMax, bgTop, bgTop, bgBot, bgBot);

            // Solid left border in BorderSoft so the panel boundary reads cleanly.
            dl.AddLine(winPos, new Vector2(winPos.X, winMax.Y),
                Boutique.U32(Boutique.Border), 1.5f * scale);

            // Faint vertical gold accent line on left edge (echo of dp-edge brackets)
            Boutique.DrawDpAccentLine(dl, winPos, winMax, scale);

            DrawDpHead(dl, headMin, headMax, scale, time, character);

            // When the form is open it OWNS the entire panel below the head row.
            // Skip action / search / sort chrome, the form has its own title row.
            // We don't push the full ApplyScaledStyles bundle (its FrameRounding /
            // FramePadding fight the boutique form style), but we DO honour the
            // user's custom designPanelBg colour when Custom theme is selected.
            if (isEditDesignWindowOpen)
            {
                var formMin = new Vector2(winPos.X, headMax.Y);
                var formMax = winMax;
                ImGui.SetCursorScreenPos(formMin);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0f, 0f));
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 4f * scale));

                Vector4 formBg = Boutique.Surface0;
                if (plugin.Configuration.SelectedTheme == ThemeSelection.Custom)
                {
                    var customTheme = plugin.Configuration.CustomTheme;
                    if (customTheme.ColorOverrides.TryGetValue("custom.designPanelBg", out var packed) && packed.HasValue)
                    {
                        formBg = CustomThemeDefinitions.UnpackColor(packed.Value);
                    }
                }
                ImGui.PushStyleColor(ImGuiCol.ChildBg, formBg);
                ImGui.BeginChild("##boutique_dp_form",
                    new Vector2(formMax.X - formMin.X, formMax.Y - formMin.Y), false);
                if (_dpFormScrollResetFramesPending > 0)
                {
                    ImGui.SetScrollY(0f);
                    _dpFormScrollResetFramesPending--;
                }
                DrawBoutiqueDesignForm(character, scale);
                ImGui.EndChild();
                ImGui.PopStyleColor();
                ImGui.PopStyleVar(2);
                return;
            }

            var actionMin = new Vector2(winPos.X, headMax.Y);
            var actionMax = new Vector2(winMax.X, actionMin.Y + actionH);
            var searchMin = new Vector2(winPos.X, actionMax.Y);
            var searchMax = new Vector2(winMax.X, searchMin.Y + searchH);
            var subbarMin = new Vector2(winPos.X, searchMax.Y);
            var subbarMax = new Vector2(winMax.X, subbarMin.Y + subbarH);
            var listMin = new Vector2(winPos.X, subbarMax.Y);
            var listMax = winMax;

            DrawDpActionBar(dl, actionMin, actionMax, scale, time, character);
            DrawDpSearchRow(dl, searchMin, searchMax, scale);
            DrawDpSortSubbar(dl, subbarMin, subbarMax, scale, character);

            // Folder creation popup (anchored after chrome)
            DrawFolderCreationPopup(character, scale);

            // ── List area (existing flow rendered inside the boutique chassis) ──
            ImGui.SetCursorScreenPos(listMin);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0f, 0f));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 4f * scale));
            ImGui.BeginChild("##boutique_dp_list",
                new Vector2(listMax.X - listMin.X, listMax.Y - listMin.Y), false);

            ApplyScaledStyles(scale);
            try { DrawBoutiqueDesignList(character, scale); }
            finally { PopScaledStyles(); }

            ImGui.EndChild();
            ImGui.PopStyleVar(2);
        }

        // ── DP head strip (36px): collapse + title + count badge ──
        private void DrawDpHead(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale, double time, Character character)
        {
            // Background gradient + bottom hairline
            uint top = Boutique.U32(new Vector4(0x0C / 255f, 0x0E / 255f, 0x14 / 255f, 1f));
            uint bot = Boutique.U32(Boutique.Bg);
            dl.AddRectFilledMultiColor(min, max, top, top, bot, bot);
            Boutique.DrawAuroraSpot(dl,
                new Vector2(max.X - 60f * scale, max.Y + 10f * scale),
                200f * scale, 50f * scale,
                Boutique.WithAlpha(Boutique.Gold, 0.04f), 8);
            dl.AddLine(new Vector2(min.X, max.Y - 1f * scale),
                       new Vector2(max.X, max.Y - 1f * scale),
                       Boutique.U32(Boutique.BorderSoft), 1f * scale);

            float padX = 12f * scale;
            float midY = (min.Y + max.Y) * 0.5f;
            float panelW = max.X - min.X;

            // ── X close button on the RIGHT side ──
            float collSize = 22f * scale;
            var collMin = new Vector2(max.X - padX - collSize, midY - collSize * 0.5f);
            ImGui.SetCursorScreenPos(collMin);
            bool collClicked = ImGui.InvisibleButton("##dp_close", new Vector2(collSize, collSize));
            bool collHovered = ImGui.IsItemHovered();
            if (collHovered) CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Close design panel");
            var collBg = collHovered
                ? Boutique.U32(Boutique.WithAlpha(Boutique.Red, 0.20f))
                : Boutique.U32(Boutique.PillBg);
            dl.AddRectFilled(collMin, collMin + new Vector2(collSize, collSize), collBg);
            dl.AddRect(collMin, collMin + new Vector2(collSize, collSize),
                Boutique.U32(collHovered ? Boutique.Red : Boutique.BorderSoft),
                0f, ImDrawFlags.None, 1f * scale);
            ImGui.PushFont(UiBuilder.IconFont);
            var carSz = ImGui.CalcTextSize("");
            ImGui.PopFont();
            float xIconSize = 12f * scale;
            float xScale = xIconSize / UiBuilder.IconFont.FontSize;
            dl.AddText(UiBuilder.IconFont, xIconSize,
                new Vector2(collMin.X + (collSize - carSz.X * xScale) * 0.5f,
                            collMin.Y + (collSize - carSz.Y * xScale) * 0.5f),
                Boutique.U32(collHovered ? Boutique.Red : Boutique.TextDim), "");
            if (collClicked) Close();

            // Title on the left, bigger DESIGNS label + name (no count badge)
            float titleX = min.X + padX;
            float nameMaxX = collMin.X - 10f * scale;
            Vector4 npCol = character.NameplateColor.LengthSquared() > 0.001f
                ? new Vector4(character.NameplateColor.X, character.NameplateColor.Y, character.NameplateColor.Z, 1f)
                : Boutique.NpCyan;

            // "DESIGNS" label, bigger Oswald (Semi 16) for presence
            using (Plugin.Instance?.OswaldSemiSmall?.Push())
            {
                string labelText = "DESIGNS";
                Vector2 labelSize = ImGui.CalcTextSize(labelText);
                float labelY = midY - labelSize.Y * 0.5f;
                dl.AddText(new Vector2(titleX, labelY),
                    Boutique.U32(Boutique.TextDim), labelText);
                titleX += labelSize.X + 14f * scale;
            }

            // Nameplate pip, larger to match the bigger title
            Boutique.DrawSquarePip(dl, new Vector2(titleX + 3.5f * scale, midY), 3.5f * scale, npCol);
            titleX += 16f * scale;

            // Character name, Oswald Semi 16 (bigger, more confident)
            using (Plugin.Instance?.OswaldSemiSmall?.Push())
            {
                string nm = !string.IsNullOrWhiteSpace(character.Alias) ? character.Alias! : character.Name;
                Vector2 nameSize = ImGui.CalcTextSize(nm);
                float nameY = midY - nameSize.Y * 0.5f;
                dl.PushClipRect(new Vector2(titleX, min.Y), new Vector2(nameMaxX, max.Y), true);
                dl.AddText(new Vector2(titleX, nameY),
                    Boutique.U32(Boutique.Text), nm);
                dl.PopClipRect();
            }
        }

        // ── DP action bar (44px): + NEW DESIGN pill + icon cluster ──
        private void DrawDpActionBar(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale, double time, Character character)
        {
            // Background: gold radial wash bottom-left + dark vertical gradient
            uint top = Boutique.U32(new Vector4(0x0C / 255f, 0x0E / 255f, 0x14 / 255f, 1f));
            uint bot = Boutique.U32(Boutique.Bg);
            dl.AddRectFilledMultiColor(min, max, top, top, bot, bot);
            Boutique.DrawAuroraSpot(dl,
                new Vector2(min.X + 60f * scale, max.Y + 10f * scale),
                200f * scale, 50f * scale,
                Boutique.WithAlpha(Boutique.Gold, 0.045f), 8);
            // Bottom border
            dl.AddLine(new Vector2(min.X, max.Y - 1f * scale),
                       new Vector2(max.X, max.Y - 1f * scale),
                       Boutique.U32(Boutique.BorderSoft), 1f * scale);

            float padX = 10f * scale;
            float midY = (min.Y + max.Y) * 0.5f;
            float panelW = max.X - min.X;

            // ── Icon cluster sizing, folder, snapshot, wardrobe (no search, no divider) ──
            float iconSize = 30f * scale;
            float iconGap = 4f * scale;
            float clusterW = 3 * iconSize + 2 * iconGap;

            // ── + NEW DESIGN gold pill, fills available width, fixed clean height ──
            float trackPx = 2.0f * scale;
            string pillLabel = "NEW DESIGN";
            float pillH = 30f * scale;
            float availPillW = panelW - padX * 2 - clusterW - 10f * scale;
            // Natural pill width if labelled, then stretch to fill available space
            float naturalW = Boutique.DrawGoldPillSize(pillLabel, trackPx, scale).X;
            float pillW = MathF.Max(naturalW, availPillW);
            if (pillW > availPillW && availPillW > 40f * scale)
            {
                pillLabel = "NEW";
                naturalW = Boutique.DrawGoldPillSize(pillLabel, trackPx, scale).X;
                pillW = MathF.Max(naturalW, availPillW);
                if (pillW > availPillW)
                {
                    pillLabel = "+";
                    naturalW = Boutique.DrawGoldPillSize(pillLabel, trackPx, scale).X;
                    pillW = MathF.Max(naturalW, availPillW);
                }
            }
            Vector2 pillSize = new Vector2(pillW, pillH);
            var pillMin = new Vector2(min.X + padX, midY - pillSize.Y * 0.5f);
            var pillMax = pillMin + pillSize;

            ImGui.SetCursorScreenPos(pillMin);
            bool pillClicked = ImGui.InvisibleButton("##dp_newdesign", pillSize);
            bool pillHovered = ImGui.IsItemHovered();

            // Drop-shadow halo behind the pill, wardrobe-card "drop-shadow filter" equivalent
            var pillCentre = (pillMin + pillMax) * 0.5f;
            float pillRx = (pillSize.X * 0.55f);
            float pillRy = (pillSize.Y * 0.85f);
            Boutique.DrawAuroraSpot(dl, pillCentre + new Vector2(0, 2f * scale),
                pillRx + 14f * scale, pillRy + 8f * scale,
                Boutique.WithAlpha(Boutique.Gold, pillHovered ? 0.55f : 0.32f), 14);

            Boutique.DrawGoldPill(dl, pillMin, pillMax, pillLabel, trackPx, scale, pillHovered);

            // Top-edge gold-bright highlight (1px just inside the chamfered top edge)
            float chBev = 8f * scale;
            dl.AddRectFilled(
                new Vector2(pillMin.X + chBev, pillMin.Y),
                new Vector2(pillMax.X - chBev, pillMin.Y + 1f * scale),
                Boutique.U32(Boutique.WithAlpha(Boutique.GoldBright, 0.85f)));

            float sheen = uiStyles.UpdateAndGetHoverSweepProgress("dp_newdesign_pill", pillHovered);
            if (sheen >= 0f) Windows.Styles.UIStyles.DrawHoverSheen(dl, pillMin, pillMax, sheen, maxAlpha: 0.40f);
            if (pillHovered)
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(
                    "New design\nShift-click: import from another character");
            if (pillClicked)
            {
                var io = ImGui.GetIO();
                if (io.KeyShift)
                {
                    isSecretDesignMode = false;
                    isImportWindowOpen = true;
                    targetForDesignImport = character;
                }
                else
                {
                    isSecretDesignMode = false;
                    AddNewDesign();
                }
            }

            // ── Icon cluster (right): folder, snapshot, wardrobe | divider | search ──

            string[] glyphs   = { "\uf65e", "\uf030", "\uf553" };
            string[] keys     = { "folder",       "snapshot",     "wardrobe" };
            Vector4[] hovers  = { Boutique.NpAmber, Boutique.NpViolet, Boutique.CyanSoft };

            // Restore the previous (longer, more descriptive) tooltip wording
            // for Snapshot + Wardrobe \u2014 the new short versions were a regression.
            string snapshotTip = "Create Design from Current Look\n\u2022 Click: Smart snapshot";
            if (plugin.Configuration.EnableConflictResolution)
                snapshotTip += "\n\u2022 Ctrl+Shift+Click: Smart snapshot with Conflict Resolution";
            snapshotTip += "\n\nNote: Uses the most recently created design in Glamourer.";
            string[] tooltips = { "New folder", snapshotTip, "Open Wardrobe (visual design browser)" };

            float iconY = midY - iconSize * 0.5f;
            float ix = max.X - padX - clusterW;

            for (int i = 0; i < 3; i++)
            {
                DrawDpIconButton(dl, scale, time, new Vector2(ix, iconY),
                    glyphs[i], keys[i], hovers[i], tooltips[i], false, iconSize);
                ix += iconSize + iconGap;
            }
        }

        private void DrawDpIconButton(ImDrawListPtr dl, float scale, double time, Vector2 min,
            string glyph, string key, Vector4 hoverInk, string tooltip, bool isActive = false, float? sideOverride = null)
        {
            float side = sideOverride ?? (30f * scale);
            ImGui.SetCursorScreenPos(min);
            bool clicked = ImGui.InvisibleButton($"##dpic_{key}", new Vector2(side, side));
            bool hovered = ImGui.IsItemHovered();
            if (hovered && !string.IsNullOrEmpty(tooltip)) CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(tooltip);

            // Background
            uint bgCol;
            if (isActive)
                bgCol = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.06f));
            else if (hovered)
                bgCol = Boutique.U32(Boutique.Surface1);
            else
                bgCol = Boutique.U32(Boutique.PillBg);
            dl.AddRectFilled(min, min + new Vector2(side, side), bgCol);

            // Border
            uint borderCol;
            if (isActive)
                borderCol = Boutique.U32(Boutique.GoldDeep);
            else if (hovered)
                borderCol = Boutique.U32(Boutique.WithAlpha(hoverInk, 0.85f));
            else
                borderCol = Boutique.U32(Boutique.BorderSoft);
            dl.AddRect(min, min + new Vector2(side, side), borderCol, 0f, ImDrawFlags.None, 1f * scale);

            // Icon
            var ink = isActive ? Boutique.Gold : (hovered ? hoverInk : Boutique.TextDim);
            ImGui.PushFont(UiBuilder.IconFont);
            var iconSize = ImGui.CalcTextSize(glyph);
            ImGui.PopFont();
            float fSz = UiBuilder.IconFont.FontSize;
            var iconPos = new Vector2(
                min.X + (side - iconSize.X * 0.85f) * 0.5f,
                min.Y + (side - iconSize.Y * 0.85f) * 0.5f);
            dl.AddText(UiBuilder.IconFont, fSz, iconPos, Boutique.U32(ink), glyph);

            float sheen = uiStyles.UpdateAndGetHoverSweepProgress($"dpic_{key}", hovered);
            if (sheen >= 0f)
                Windows.Styles.UIStyles.DrawHoverSheen(dl, min, min + new Vector2(side, side), sheen, maxAlpha: 0.18f);

            if (clicked)
            {
                switch (key)
                {
                    case "folder":
                        ImGui.OpenPopup("CreateFolderPopup");
                        break;
                    case "snapshot":
                        if (activeCharacterIndex >= 0 && activeCharacterIndex < plugin.Characters.Count)
                            OpenSnapshotDialog(plugin.Characters[activeCharacterIndex]);
                        break;
                    case "wardrobe":
                        if (activeCharacterIndex >= 0 && activeCharacterIndex < plugin.Characters.Count)
                        {
                            var c = plugin.Characters[activeCharacterIndex];
                            if (plugin.WardrobeWindow != null)
                            {
                                plugin.WardrobeWindow.TargetCharacter = c;
                                plugin.WardrobeWindow.IsOpen = true;
                                plugin.AchievementTracker?.OnWardrobeOpened();
                            }
                        }
                        break;
                    case "search":
                        // Search row is always visible; clicking the icon clears any active query.
                        if (!string.IsNullOrEmpty(searchQuery))
                        {
                            searchQuery = "";
                        }
                        break;
                }
            }
        }

        // ── DP search row (36px, optional) ──
        private void DrawDpSearchRow(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
        {
            dl.AddRectFilled(min, max, Boutique.U32(new Vector4(0x0A / 255f, 0x0C / 255f, 0x10 / 255f, 0.55f)));
            dl.AddLine(new Vector2(min.X, max.Y - 1f * scale),
                       new Vector2(max.X, max.Y - 1f * scale),
                       Boutique.U32(Boutique.BorderSoft), 1f * scale);

            float padX = 8f * scale;
            float midY = (min.Y + max.Y) * 0.5f;
            float pillH = 28f * scale;
            var pillMin = new Vector2(min.X + padX, midY - pillH * 0.5f);
            var pillMax = new Vector2(max.X - padX, midY + pillH * 0.5f);

            Boutique.DrawSearchPillBackground(dl, pillMin, pillMax, scale, searchInputFocused);
            if (searchInputFocused)
            {
                var pillCentre = (pillMin + pillMax) * 0.5f;
                Boutique.DrawAuroraSpot(dl, pillCentre,
                    (pillMax.X - pillMin.X) * 0.55f, pillH * 0.9f,
                    Boutique.WithAlpha(Boutique.Gold, 0.18f), 8);
            }

            // Magnifier glyph
            ImGui.PushFont(UiBuilder.IconFont);
            var magSize = ImGui.CalcTextSize("\uf002");
            ImGui.PopFont();
            var magPos = new Vector2(pillMin.X + 12f * scale, midY - magSize.Y * 0.5f);
            dl.AddText(UiBuilder.IconFont, UiBuilder.IconFont.FontSize, magPos,
                Boutique.U32(Boutique.TextFaint), "\uf002");

            // Input, vertically centered inside the pill via custom FramePadding
            float lineH = ImGui.GetTextLineHeight();
            float vertPad = MathF.Max(0f, (pillH - lineH) * 0.5f);
            ImGui.SetCursorScreenPos(new Vector2(pillMin.X + 12f * scale + magSize.X + 8f * scale, pillMin.Y));
            ImGui.PushItemWidth(pillMax.X - pillMin.X - 12f * scale - magSize.X - 16f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0f, vertPad));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.Text, Boutique.Text);
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, Boutique.TextFaint);
            if (ImGui.InputTextWithHint("##boutique_dp_search", "Search designs...", ref searchQuery, 100))
            // Capture focus state so the next frame's pill border lights up
            searchInputFocused = ImGui.IsItemActive() || ImGui.IsItemFocused();
            ImGui.PopStyleColor(5);
            ImGui.PopStyleVar();
            ImGui.PopItemWidth();
        }

        // ── DP sort subbar (32px): tabs FAVOURITES / RECENT / A-Z / MANUAL ──
        private void DrawDpSortSubbar(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale, Character character)
        {
            dl.AddRectFilled(min, max, Boutique.U32(Boutique.Surface0));
            dl.AddLine(new Vector2(min.X, max.Y - 1f * scale),
                       new Vector2(max.X, max.Y - 1f * scale),
                       Boutique.U32(Boutique.BorderSoft), 1f * scale);

            (string label, DesignSortType type, int idx)[] tabs =
            {
                ("FAVOURITES", DesignSortType.Favorites,    0),
                ("RECENT",     DesignSortType.Recent,       2),
                ("A-Z",        DesignSortType.Alphabetical, 1),
                ("MANUAL",     DesignSortType.Manual,       4),
            };

            float padX = 6f * scale;
            float availW = (max.X - min.X) - padX * 2;
            float slotW = availW / tabs.Length;
            float baseX = min.X + padX;

            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                float fontH = ImGui.GetFontSize();

                for (int i = 0; i < tabs.Length; i++)
                {
                    var t = tabs[i];
                    bool isActive = currentDesignSort == t.type;
                    float slotL = baseX + i * slotW;
                    float slotR = slotL + slotW;
                    float midX = (slotL + slotR) * 0.5f;

                    // Hit area = full slot
                    ImGui.SetCursorScreenPos(new Vector2(slotL, min.Y));
                    bool clicked = ImGui.InvisibleButton($"##dp_sorttab_{i}", new Vector2(slotW, max.Y - min.Y));
                    bool hovered = ImGui.IsItemHovered();

                    // Text centered in slot
                    Vector2 labelSize = ImGui.CalcTextSize(t.label);
                    float labelX = midX - labelSize.X * 0.5f;
                    float labelY = min.Y + ((max.Y - min.Y) - labelSize.Y) * 0.5f - 1f * scale;

                    Vector4 ink = isActive ? Boutique.Text
                                : hovered ? Boutique.TextDim
                                          : Boutique.TextFaint;
                    dl.AddText(new Vector2(labelX, labelY), Boutique.U32(ink), t.label);

                    // Active underline + glow
                    if (isActive)
                    {
                        float ulY = max.Y - 2f * scale;
                        float ulHalf = labelSize.X * 0.5f + 4f * scale;
                        var ulMin = new Vector2(midX - ulHalf, ulY);
                        var ulMax = new Vector2(midX + ulHalf, ulY + 2f * scale);
                        for (int g = 3; g > 0; g--)
                        {
                            float r = g * 2f * scale;
                            dl.AddRectFilled(ulMin - new Vector2(r, r * 0.5f),
                                             ulMax + new Vector2(r, r * 0.5f),
                                Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.14f / g)));
                        }
                        dl.AddRectFilled(ulMin, ulMax, Boutique.U32(Boutique.Gold));
                    }

                    if (clicked)
                    {
                        SetDesignSort(t.idx);
                        SortDesigns(character);
                    }

                    // Hairline divider at slot boundary
                    if (i < tabs.Length - 1)
                    {
                        float dx = slotR;
                        dl.AddLine(new Vector2(dx, min.Y + 10f * scale),
                                   new Vector2(dx, max.Y - 10f * scale),
                                   Boutique.U32(Boutique.BorderSoft), 1f * scale);
                    }
                }
            }
        }

        private void ApplyScaledStyles(float scale)
        {
            // Check for custom Design Panel background colour
            var designPanelBg = new Vector4(0.08f, 0.08f, 0.1f, 0.98f);
            var designPanelChildBg = new Vector4(0.1f, 0.1f, 0.12f, 0.95f);

            if (plugin.Configuration.SelectedTheme == ThemeSelection.Custom)
            {
                var customTheme = plugin.Configuration.CustomTheme;
                if (customTheme.ColorOverrides.TryGetValue("custom.designPanelBg", out var packed) && packed.HasValue)
                {
                    var customColor = CustomThemeDefinitions.UnpackColor(packed.Value);
                    designPanelBg = customColor;
                    designPanelChildBg = customColor;
                }
            }

            ImGui.PushStyleColor(ImGuiCol.WindowBg, designPanelBg);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, designPanelChildBg);
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

        private void DrawHeader(Character character, float scale)
        {
            float buttonSize = 25f * scale;
            float spacing = 2f * scale;

            
            ImGui.BeginGroup();

            // Add and Folder buttons
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.27f, 1.07f, 0.27f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.25f, 0.25f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.35f, 0.35f, 0.35f, 1f));

            if (ImGui.Button("+##AddDesign", new Vector2(buttonSize, buttonSize)))
            {
                var io = ImGui.GetIO();
                bool ctrlHeld = io.KeyCtrl;
                bool shiftHeld = io.KeyShift;

                if (ctrlHeld && shiftHeld && plugin.Configuration.EnableConflictResolution)
                {
                    isSecretDesignMode = true;
                    AddNewDesign();
                    editedDesignMacro = (!plugin.Configuration.EnableConflictResolution && isSecretDesignMode) ? GenerateSecretDesignMacro(character) : GenerateDesignMacro(character);
                    if (isAdvancedModeDesign)
                        advancedDesignMacroText = editedDesignMacro;
                }
                else if (shiftHeld)
                {
                    isSecretDesignMode = false;
                    isImportWindowOpen = true;
                    targetForDesignImport = character;
                }
                else
                {
                    isSecretDesignMode = false;
                    AddNewDesign();
                    editedDesignMacro = GenerateDesignMacro(character);
                    if (isAdvancedModeDesign)
                        advancedDesignMacroText = editedDesignMacro;
                }
            }

            plugin.DesignPanelAddButtonPos = ImGui.GetItemRectMin();
            plugin.DesignPanelAddButtonSize = ImGui.GetItemRectSize();
            uiStyles.ApplyHoverSheenToLastItem("design_add_btn");

            ImGui.PopStyleColor(4);

            if (ImGui.IsItemHovered())
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(
                    "Click to add a new design\nHold Shift to import from another character");

            ImGui.SameLine(0, spacing);

            // Folder Button
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.7f, 0.3f, 1.0f)); // Yellow
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.25f, 0.25f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.35f, 0.35f, 0.35f, 1f));

            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button("\uf07b##AddFolder"))
                ImGui.OpenPopup("CreateFolderPopup");
            ImGui.PopFont();
            uiStyles.ApplyHoverSheenToLastItem("design_folder_btn");

            ImGui.PopStyleColor(4);

            DrawFolderCreationPopup(character, scale);

            if (ImGui.IsItemHovered())
            {
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Add Folder");
            }

            // Search button
            ImGui.SameLine(0, spacing);
            if (uiStyles.IconButton("\uf002", "Search designs"))
            {
                showSearchBar = !showSearchBar;
                if (!showSearchBar)
                {
                    searchQuery = "";
                }
            }
            if (ImGui.IsItemHovered())
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Search designs");

            // Wardrobe button (visual design browser)
            ImGui.SameLine(0, spacing);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.83f, 0.69f, 0.22f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.22f, 0.15f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.35f, 0.30f, 0.18f, 1f));
            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button("\uf00a##OpenWardrobe"))
            {
                if (plugin.WardrobeWindow != null)
                {
                    // Design Panel's wardrobe button clears any Shift+Click target - uses active character
                    plugin.WardrobeWindow.TargetCharacter = null;
                    plugin.WardrobeWindow.IsOpen = !plugin.WardrobeWindow.IsOpen;
                    if (plugin.WardrobeWindow.IsOpen) plugin.AchievementTracker?.OnWardrobeOpened();
                }
            }
            ImGui.PopFont();
            uiStyles.ApplyHoverSheenToLastItem("design_wardrobe_btn");
            ImGui.PopStyleColor(4);
            if (ImGui.IsItemHovered())
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Open Wardrobe (visual design browser)");

            // Snapshot button (right-aligned)
            ImGui.SameLine();
            float availableWidth = ImGui.GetContentRegionAvail().X;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availableWidth - (buttonSize * 2) - (5 * scale));

            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));        // Dark gray
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.4f, 0.4f, 0.9f)); // Medium gray  
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));  // Light gray
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 1.0f, 1.0f));          // White text
            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));        // Center icon

            if (ImGui.Button($"\uf030##CreateSnapshot"))
            {
                if (activeCharacterIndex >= 0 && activeCharacterIndex < plugin.Characters.Count)
                {
                    var io = ImGui.GetIO();
                    var selectedCharacter = plugin.Characters[activeCharacterIndex];
                    
                    if (io.KeyCtrl && io.KeyShift)
                    {
                        // Ctrl+Shift: Smart snapshot with CR
                        CreateSmartSnapshot(selectedCharacter, useConflictResolution: true);
                    }
                    else
                    {
                        // Regular click: Smart snapshot without CR
                        CreateSmartSnapshot(selectedCharacter, useConflictResolution: false);
                    }
                }
            }

            ImGui.PopStyleVar(1);
            ImGui.PopStyleColor(4);
            ImGui.PopFont();
            uiStyles.ApplyHoverSheenToLastItem("design_snapshot_btn");

            if (ImGui.IsItemHovered())
            {
                string tooltip = "Create Design from Current Look\n• Click: Smart snapshot";
                if (plugin.Configuration.EnableConflictResolution)
                    tooltip += "\n• Ctrl+Shift+Click: Smart snapshot with Conflict Resolution";
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(tooltip);
            }

            // Close button
            ImGui.SameLine(0, spacing);

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.27f, 0.27f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.2f, 0.2f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.3f, 0.3f, 1f));

            if (ImGui.Button("×##CloseDesignPanel"))
            {
                Close();
            }
            uiStyles.ApplyHoverSheenToLastItem("design_close_btn");

            ImGui.PopStyleColor(4);

            if (ImGui.IsItemHovered())
            {
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Close Design Panel");
            }

            ImGui.EndGroup();

            ImGui.Spacing();

            // Character name
            string name = $"Designs for {character.Name}";
            ImGui.TextUnformatted(name);
            if (ImGui.IsItemHovered())
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(name);

            ImGui.Spacing();
        }

        private void DrawFolderCreationPopup(Character character, float scale)
        {
            // Boutique-styled popup: dark velvet bg, gold-deep border, no rounding
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14f * scale, 12f * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6f * scale, 8f * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f * scale, 6f * scale));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.04f, 0.05f, 0.08f, 0.97f));
            ImGui.PushStyleColor(ImGuiCol.Border, Boutique.WithAlpha(Boutique.Gold, 0.55f));
            ImGui.PushStyleColor(ImGuiCol.Text, Boutique.Text);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(20f / 255f, 24f / 255f, 32f / 255f, 0.6f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(24f / 255f, 28f / 255f, 38f / 255f, 0.75f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(28f / 255f, 32f / 255f, 44f / 255f, 0.85f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(20f / 255f, 24f / 255f, 32f / 255f, 0.7f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Boutique.WithAlpha(Boutique.Gold, 0.18f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Boutique.WithAlpha(Boutique.Gold, 0.28f));
            ImGui.PushStyleColor(ImGuiCol.Separator, Boutique.WithAlpha(Boutique.Gold, 0.30f));

            bool open = ImGui.BeginPopup("CreateFolderPopup");
            if (open)
            {
                // Header label in tracked-caps Oswald
                using (Plugin.Instance?.OswaldSemi11?.Push())
                {
                    var dl2 = ImGui.GetWindowDrawList();
                    var pos = ImGui.GetCursorScreenPos();
                    string title = "NEW FOLDER";
                    var ts = ImGui.CalcTextSize(title);
                    dl2.AddText(pos, Boutique.U32(Boutique.GoldWarm), title);
                    // gold-fade rule below the label
                    float ruleY = pos.Y + ts.Y + 4f * scale;
                    dl2.AddRectFilledMultiColor(
                        new Vector2(pos.X, ruleY),
                        new Vector2(pos.X + 200f * scale, ruleY + 1f),
                        Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.55f)),
                        Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0f)),
                        Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0f)),
                        Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.55f)));
                    ImGui.Dummy(new Vector2(0, ts.Y + 8f * scale));
                }

                ImGui.TextColored(Boutique.TextDim, "Name");
                ImGui.SetNextItemWidth(200 * scale);
                ImGui.InputText("##NewFolder", ref newFolderName, 100);

                ImGui.Spacing();
                ImGui.TextColored(Boutique.TextDim, "Colour");

                // Colour selection
                var quickColors = new[]
                {
                    (Vector3?)null, // Auto
                    new Vector3(0.8f, 0.2f, 0.2f), // Red
                    new Vector3(0.3f, 0.8f, 0.3f), // Green
                    new Vector3(0.3f, 0.5f, 0.9f), // Blue
                    new Vector3(0.7f, 0.3f, 0.9f)  // Purple
                };

                float colorButtonSize = 30f * scale;
                for (int i = 0; i < quickColors.Length; i++)
                {
                    var color = quickColors[i];
                    bool isSelected = (newFolderSelectedColor == null && color == null) ||
                                     (newFolderSelectedColor != null && color != null &&
                                      Vector3.Distance(newFolderSelectedColor.Value, color.Value) < 0.1f);

                    if (i > 0) ImGui.SameLine();

                    Vector4 buttonColor = color.HasValue
                        ? new Vector4(color.Value.X, color.Value.Y, color.Value.Z, 1.0f)
                        : new Vector4(0.45f, 0.45f, 0.5f, 1.0f);

                    ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(buttonColor.X * 1.2f, buttonColor.Y * 1.2f, buttonColor.Z * 1.2f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(buttonColor.X * 0.8f, buttonColor.Y * 0.8f, buttonColor.Z * 0.8f, 1.0f));

                    if (isSelected)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Border, Boutique.Gold);
                        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 2f * scale);
                    }

                    if (ImGui.Button($"##Color{i}", new Vector2(colorButtonSize, colorButtonSize)))
                    {
                        newFolderSelectedColor = color;
                    }
                    if (ImGui.IsItemHovered() && color == null) CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Auto (use character nameplate colour)");

                    if (isSelected)
                    {
                        ImGui.PopStyleVar();
                        ImGui.PopStyleColor();
                    }

                    ImGui.PopStyleColor(3);
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 80f * scale;
                // CREATE, gold-tinted button
                ImGui.PushStyleColor(ImGuiCol.Button, Boutique.WithAlpha(Boutique.Gold, 0.20f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Boutique.WithAlpha(Boutique.Gold, 0.35f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, Boutique.WithAlpha(Boutique.Gold, 0.50f));
                ImGui.PushStyleColor(ImGuiCol.Text, Boutique.GoldWarm);
                if (ImGui.Button("CREATE", new Vector2(buttonWidth, 0)))
                {
                    var folder = new DesignFolder(newFolderName, Guid.NewGuid())
                    {
                        ParentFolderId = null,
                        SortOrder = character.DesignFolders.Count,
                        CustomColor = newFolderSelectedColor
                    };
                    character.DesignFolders.Add(folder);
                    plugin.AchievementTracker?.OnDesignFolderCreated();
                    plugin.SaveConfiguration();
                    plugin.RefreshTreeItems(character);
                    newFolderName = "";
                    newFolderSelectedColor = null;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.PopStyleColor(4);

                ImGui.SameLine();
                if (ImGui.Button("CANCEL", new Vector2(buttonWidth, 0)))
                {
                    newFolderName = "";
                    newFolderSelectedColor = null;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            ImGui.PopStyleColor(10);
            ImGui.PopStyleVar(5);
        }

        // ── Boutique-themed wrapper that takes the full list area, with a header,
        //    scrollable body, and footer. Replaces the cramped inline form. ──
        private void DrawBoutiqueDesignForm(Character character, float scale)
        {
            float remainingH = ImGui.GetContentRegionAvail().Y;
            if (remainingH <= 0) return;

            float fs = Boutique.FormScale;
            var dl = ImGui.GetWindowDrawList();

            // No chassis: no velvet ground, no header bar, no footer bar. Render
            // the title row + sections + footer buttons inline in the panel's
            // content area. Same approach as the character form.
            float availW = ImGui.GetContentRegionAvail().X;
            _dpFormIndent = 10f * fs;
            _dpFormContentWidth = MathF.Max(80f * fs, availW - _dpFormIndent - 8f * fs);

            Boutique.PushFormStyle();
            // Tighter input rows + tighter row-to-row spacing for the narrow
            // sidebar. PushFormStyle's defaults (8/4 padding, 6/4 spacing) are
            // tuned for the wide character form and feel cluttered here.
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f * fs, 3f * fs));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,  new Vector2(4f * fs, 4f * fs));
            // Lift the input bg off the form ground. PushFormStyle's default
            // FrameBg (#0E1118) is virtually identical to the design panel's
            // Surface0 (#0E1014) form bg, so inputs disappear into the surface.
            // Surface2 (#1A1D27) gives a clear visual lift while staying in the
            // codex palette. The boutique input primitives now read FrameBg
            // from style so this propagates to text inputs and combos.
            ImGui.PushStyleColor(ImGuiCol.FrameBg,        Boutique.Surface2);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Boutique.Surface3);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive,  Boutique.Lerp(Boutique.Surface3, Boutique.GoldDeep, 0.15f));
            try
            {
                using (Plugin.Instance?.OutfitMed12?.Push())
                {
                    DrawDesignFormTitleRow(character, fs);
                    DrawDesignFormFields(character, scale);
                    DrawDesignFormFooterButtons(character, fs);
                }
            }
            finally
            {
                ImGui.PopStyleColor(3);
                ImGui.PopStyleVar(2);
                Boutique.PopFormStyle();
            }
        }

        // Two-line title block. Row 1: kicker + diamond divider. Row 2: pip +
        // design name (pip and name pair as a single identity unit). The
        // previous single-row inline layout ran out of width for long names;
        // pairing the pip with the name on row 2 keeps the visual coupling
        // while giving the name full content width.
        private void DrawDesignFormTitleRow(Character character, float fs)
        {
            string kicker = isNewDesign ? "NEW DESIGN" : "EDIT DESIGN";
            Vector4? npCol = (!isNewDesign && character.NameplateColor.LengthSquared() > 0.001f)
                ? new Vector4(character.NameplateColor.X, character.NameplateColor.Y, character.NameplateColor.Z, 1f)
                : (Vector4?)null;
            string headerTitle = isNewDesign ? "" : (originalDesignName ?? "").ToUpperInvariant();
            bool hasTitle = !string.IsNullOrEmpty(headerTitle);

            var dl = ImGui.GetWindowDrawList();
            ImGui.SetCursorPosX(_dpFormIndent);
            var rowStart = ImGui.GetCursorScreenPos();
            float availW = ImGui.GetContentRegionAvail().X;

            // Tight two-line block: 24px kicker line + 22px title line.
            float kickerLineH = 24f * fs;
            float titleLineH = hasTitle ? 22f * fs : 0f;
            float totalH = kickerLineH + titleLineH;

            ImFontPtr kickerFont;
            using (Plugin.Instance?.OswaldMed11?.Push()) { kickerFont = ImGui.GetFont(); }

            float midY = rowStart.Y + kickerLineH * 0.5f;
            float cursorX = rowStart.X;
            const float capCenterRatio = 0.465f;
            float textY = midY - kickerFont.FontSize * capCenterRatio;
            float diamondCY = midY + 2f * fs;

            // Row 1: kicker + diamond divider. (Pip moves to row 2 with the name.)
            float kickerTrack = kickerFont.FontSize * 0.32f;
            float kickerW = Boutique.MeasureTrackedText(kicker, kickerTrack);
            Boutique.DrawTrackedText(dl,
                new Vector2(cursorX, textY),
                kicker, Boutique.U32(Boutique.TextDim), kickerTrack);
            cursorX += kickerW + 6f * fs;

            var sepC = new Vector2(cursorX + 3f * fs, diamondCY);
            dl.AddTriangleFilled(
                sepC + new Vector2(0, -3f * fs),
                sepC + new Vector2(3f * fs, 0),
                sepC + new Vector2(0, 3f * fs),
                Boutique.U32(Boutique.GoldDeep));
            dl.AddTriangleFilled(
                sepC + new Vector2(0, -3f * fs),
                sepC + new Vector2(0, 3f * fs),
                sepC + new Vector2(-3f * fs, 0),
                Boutique.U32(Boutique.GoldDeep));

            // Row 2: nameplate pip + design name pair. OswaldMed11 (~14.3px)
            // tracked-caps, slightly smaller than the kicker's body font but
            // bigger than the previous OswaldMed10 attempt.
            if (hasTitle)
            {
                using (Plugin.Instance?.OswaldMed11?.Push())
                {
                    float titleFontH = ImGui.GetFontSize();
                    float titleTrack = titleFontH * 0.20f;
                    float row2MidY = rowStart.Y + kickerLineH + titleLineH * 0.5f;
                    float titleY = row2MidY - titleFontH * 0.5f;
                    float row2X = rowStart.X;

                    if (npCol.HasValue)
                    {
                        Boutique.DrawSquarePip(dl,
                            new Vector2(row2X + 4f * fs, row2MidY), 4f * fs, npCol.Value);
                        row2X += 16f * fs;
                    }

                    Boutique.DrawTrackedText(dl,
                        new Vector2(row2X, titleY),
                        headerTitle, Boutique.U32(Boutique.Text), titleTrack);
                }
            }

            // No X close: the Cancel button in the footer covers it. Keeping the
            // title row chrome-free matches the simpler-form direction.

            ImGui.SetCursorScreenPos(rowStart);
            ImGui.Dummy(new Vector2(availW, totalH));
        }

        // Inline footer: Cancel + Save Design buttons left-aligned at the bottom
        // of the form content. Lifted off the bottom edge with breathing space.
        private void DrawDesignFormFooterButtons(Character character, float fs)
        {
            bool canSave = !string.IsNullOrWhiteSpace(editedDesignName)
                        && !string.IsNullOrWhiteSpace(editedGlamourerDesign);
            string disabledReason = null;
            if (!canSave)
            {
                if (string.IsNullOrWhiteSpace(editedDesignName))
                    disabledReason = "Enter a design name first.";
                else if (string.IsNullOrWhiteSpace(editedGlamourerDesign))
                    disabledReason = "Pick a Glamourer design first.";
            }

            ImGui.Dummy(new Vector2(0f, 14f * fs));

            var dl = ImGui.GetWindowDrawList();
            ImGui.SetCursorPosX(_dpFormIndent);
            float availW = _dpFormContentWidth;
            float btnH = 26f * fs;
            // Auto-fit to the panel width: 1/3 cancel, 2/3 save with a small gap.
            // Was hardcoded at 90 + 130 + 8 = 228 + indent, which overflowed any
            // panel narrower than ~245px and the buttons got cut off.
            float gap = 6f * fs;
            float cancelW = MathF.Max(60f * fs, (availW - gap) * 0.36f);
            float saveW   = MathF.Max(90f * fs, availW - cancelW - gap);

            var rowStart = ImGui.GetCursorScreenPos();

            // Default ImGui font (no Oswald push) so the gold pill matches the
            // main window's "ADD CHARACTER" button across the plugin.
            var cancelMin = new Vector2(rowStart.X, rowStart.Y);
            var cancelMax = cancelMin + new Vector2(cancelW, btnH);
            if (Boutique.DrawCancelBtn(dl, cancelMin, cancelMax,
                    "CANCEL", 1.6f * fs, fs, "dpform_cancel", ImGui.GetFont()))
            {
                CloseDesignEditor();
            }

            var saveMin = new Vector2(cancelMax.X + gap, rowStart.Y);
            var saveMax = saveMin + new Vector2(saveW, btnH);
            if (Boutique.DrawSavePill(dl, saveMin, saveMax,
                    "SAVE DESIGN", 1.8f * fs, fs,
                    $"dp_{(isNewDesign ? "new" : "edit")}",
                    !canSave, uiStyles.UpdateAndGetHoverSweepProgress, disabledReason)
                && canSave)
            {
                SaveDesign(character);
                CloseDesignEditor();
            }

            ImGui.SetCursorScreenPos(rowStart);
            ImGui.Dummy(new Vector2(availW, btnH));
            ImGui.Dummy(new Vector2(0f, 14f * fs));
        }
        // Extracted form field rendering, used by the boutique form wrapper.
        private void DrawDesignFormFields(Character character, float scale)
        {
            // Compact sidebar form. The design panel is a narrow sidebar so the
            // character form's larger Oswald sizes look bloated here. Section /
            // field labels are the smallest readable Oswald caps; ItemSpacing.y
            // from PushFormStyle (5.2px) carries label-to-input + field-to-field
            // gaps so we don't add explicit Dummy padding.
            bool firstSection = true;
            void Section(string label)
            {
                if (!firstSection)
                    ImGui.Dummy(new Vector2(0f, 5f * Boutique.FormScale));
                firstSection = false;
                ImGui.SetCursorPosX(_dpFormIndent);
                float dividerWidth = _dpFormContentWidth;
                using (Plugin.Instance?.OswaldSemi14?.Push())
                {
                    Boutique.DrawSimpleSectionLabel(label.ToUpperInvariant(), scale, dividerWidth);
                }
            }

            void Field(string label, bool required, string tooltip, Action<float> drawInput)
            {
                ImGui.SetCursorPosX(_dpFormIndent);
                using (Plugin.Instance?.OswaldSemi12?.Push())
                {
                    Boutique.DrawFieldLabel(label.ToUpperInvariant(), required, tooltip);
                }
                ImGui.SetCursorPosX(_dpFormIndent);
                ImGui.SetNextItemWidth(_dpFormContentWidth);
                drawInput(_dpFormContentWidth);
            }

            // ── Identity ──
            Section("Identity");
            Field("Design Name", true,
                "Required. The display name for this design.",
                w =>
                {
                    if (Boutique.DrawBoutiqueTextInput("##DesignName", ref editedDesignName, 100, w, "Enter name..."))
                    {
                        plugin.EditedDesignName = editedDesignName;
                    }
                    plugin.DesignNameFieldPos = ImGui.GetItemRectMin();
                    plugin.DesignNameFieldSize = ImGui.GetItemRectSize();
                });

            // ── Integrations ──
            Section("Integrations");
            Field("Glamourer Design", true,
                "Select the Glamourer design for this outfit. Right-click to clear.",
                w => DrawGlamourerInputInline(character, w));
            if (plugin.Configuration.EnableAutomations)
            {
                Field("Glamourer Automation", false,
                    "Optional: name of a Glamourer automation. Must match exactly.",
                    w => DrawAutomationInputInline(w));
            }
            Field("Customize+ Profile", false,
                "Optional: select a Customize+ profile. Right-click to clear.",
                w => DrawCustomizeInputInline(w));
            if (plugin.Configuration.EnableGearsetAssignments)
            {
                Field("Assigned Gearset", false,
                    "Optional: switch to this gearset when applying this design.",
                    w => DrawGearsetInputInline(w));
            }

            // ── Preview ──
            Section("Preview");
            DrawPreviewImageField(scale);

            // ── Conflict Resolution (only when CR is enabled in settings) ──
            if (plugin.Configuration.EnableConflictResolution)
            {
                Section("Conflict Resolution");
                ImFontPtr crLblF, crDescF;
                using (Plugin.Instance?.OutfitMed13?.Push()) { crLblF  = ImGui.GetFont(); }
                using (Plugin.Instance?.OutfitMed13?.Push()) { crDescF = ImGui.GetFont(); }
                ImGui.SetCursorPosX(_dpFormIndent);
                Boutique.DrawBoutiqueCheckbox(
                    "dp_use_cr", ref isSecretDesignMode,
                    "Use Conflict Resolution",
                    "Per-design mod state",
                    scale, crLblF, crDescF);

                if (isSecretDesignMode)
                {
                    DrawSecretModeDesignField(character, scale);
                }
            }

            // ── Advanced Mode ──
            Section("Advanced Mode");
            DrawAdvancedModeToggle(scale);
        }

        // Input-only helpers for use inside Field() callbacks.
        private void DrawGlamourerInputInline(Character character, float width)
        {
            var glamourerOptions = plugin.IntegrationListProvider?.GetGlamourerDesigns() ?? Array.Empty<string>();
            if (AutocompleteCombo.Draw("##GlamourerDesign", ref editedGlamourerDesign, glamourerOptions, width, "Select design..."))
            {
                plugin.EditedGlamourerDesign = editedGlamourerDesign;
                if (!isAdvancedModeDesign)
                {
                    editedDesignMacro = (!plugin.Configuration.EnableConflictResolution && isSecretDesignMode)
                        ? GenerateSecretDesignMacro(character)
                        : GenerateDesignMacro(character);
                }
                else
                {
                    UpdateAdvancedMacroGlamourerFixed(editedGlamourerDesign);
                }
            }
            plugin.DesignGlamourerFieldPos = ImGui.GetItemRectMin();
            plugin.DesignGlamourerFieldSize = ImGui.GetItemRectSize();
        }

        private void DrawAutomationInputInline(float width)
        {
            Boutique.DrawBoutiqueTextInput("##GlamourerAutomation", ref editedAutomation, 100, width, "Exact name");
        }

        private void DrawCustomizeInputInline(float width)
        {
            var customizeOptions = plugin.IntegrationListProvider?.GetCustomizePlusProfiles() ?? Array.Empty<string>();
            var currentCustomize = plugin.IntegrationListProvider?.GetCurrentCustomizePlusProfile();
            if (AutocompleteCombo.Draw("##CustomizePlus", ref editedCustomizeProfile, customizeOptions, width, "Select profile...", currentActive: currentCustomize))
            {
                if (!isAdvancedModeDesign)
                {
                    editedDesignMacro = (isSecretDesignMode && !plugin.Configuration.EnableConflictResolution)
                        ? GenerateSecretDesignMacro(plugin.Characters[activeCharacterIndex])
                        : GenerateDesignMacro(plugin.Characters[activeCharacterIndex]);
                }
                else
                {
                    UpdateAdvancedMacroCustomize();
                }
            }
        }

        private void DrawGearsetInputInline(float width)
        {
            var gearsets = plugin.GetPlayerGearsets();
            var displayList = new List<string> { "None (use character setting)" };
            var displayToNumber = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var g in gearsets)
            {
                string display = plugin.GetGearsetDisplayName(g.Number, g.JobId, g.Name);
                if (!displayToNumber.ContainsKey(display))
                {
                    displayList.Add(display);
                    displayToNumber[display] = g.Number;
                }
            }

            string current = "None (use character setting)";
            if (editedGearset.HasValue)
            {
                var match = gearsets.FirstOrDefault(g => g.Number == editedGearset.Value);
                if (match.Number > 0)
                    current = plugin.GetGearsetDisplayName(match.Number, match.JobId, match.Name);
                else
                    current = $"Gearset {editedGearset.Value}";
            }

            if (AutocompleteCombo.Draw("##AssignedGearset", ref current, displayList, width, "Select gearset...", allowCustomInput: false))
            {
                int? newValue;
                if (current == "None (use character setting)") newValue = null;
                else if (displayToNumber.TryGetValue(current, out int n)) newValue = n;
                else newValue = editedGearset;
                editedGearset = newValue;
            }
        }
        private void DrawDesignForm(Character character, float scale)
        {
            float formHeight = 320f * scale;
            ImGui.BeginChild("EditDesignForm", new Vector2(0, formHeight), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize);

            bool isNewDesignForm = string.IsNullOrEmpty(editedDesignName);
            ImGui.Text(isNewDesignForm ? "Add Design" : "Edit Design");

            float inputWidth = Math.Max(150f * scale, ImGui.GetContentRegionAvail().X - (50f * scale));

            // Design Name
            ImGui.Text("Design Name*");
            ImGui.SetCursorPosX(10 * scale);
            ImGui.SetNextItemWidth(inputWidth);
            if (ImGui.InputText("##DesignName", ref editedDesignName, 100))
            {
                plugin.EditedDesignName = editedDesignName;
            }
            plugin.DesignNameFieldPos = ImGui.GetItemRectMin();
            plugin.DesignNameFieldSize = ImGui.GetItemRectSize();

            ImGui.Separator();

            DrawGlamourerField(character, inputWidth, scale);

            if (plugin.Configuration.EnableAutomations)
            {
                DrawAutomationField(inputWidth, scale);
            }

            DrawCustomizeField(inputWidth, scale);

            if (plugin.Configuration.EnableGearsetAssignments)
            {
                DrawGearsetField(inputWidth, scale);
            }

            DrawPreviewImageField(scale);

            // Secret Mode Mod Selection (only for secret mode designs)
            if (isSecretDesignMode)
            {
                DrawSecretModeDesignField(character, scale);
                ImGui.Separator();
            }

            ImGui.Separator();

            DrawAdvancedModeToggle(scale);

            ImGui.Separator();

            DrawFormActionButtons(character, scale);

            ImGui.EndChild();
        }

        private void DrawBoutiqueLabel(string label, bool required, string tooltip)
        {
            using (Plugin.Instance?.OswaldSemi12?.Push())
            {
                Boutique.DrawFieldLabel(label.ToUpperInvariant(), required, tooltip);
            }
        }

        private void DrawGlamourerField(Character character, float inputWidth, float scale)
        {
            DrawBoutiqueLabel("Glamourer Design", true,
                "Select the Glamourer design for this outfit. Right-click to clear.");
            var glamourerOptions = plugin.IntegrationListProvider?.GetGlamourerDesigns() ?? Array.Empty<string>();

            if (AutocompleteCombo.Draw("##GlamourerDesign", ref editedGlamourerDesign, glamourerOptions, inputWidth, "Select design..."))
            {
                plugin.EditedGlamourerDesign = editedGlamourerDesign;

                if (!isAdvancedModeDesign)
                {
                    // If Conflict Resolution is ON, always use regular macro
                    // If Conflict Resolution is OFF, use bulktag macro only if user has configured mods
                    editedDesignMacro = (!plugin.Configuration.EnableConflictResolution && isSecretDesignMode)
                        ? GenerateSecretDesignMacro(character)
                        : GenerateDesignMacro(character);
                }
                else
                {
                    UpdateAdvancedMacroGlamourerFixed(editedGlamourerDesign);
                }
            }
            plugin.DesignGlamourerFieldPos = ImGui.GetItemRectMin();
            plugin.DesignGlamourerFieldSize = ImGui.GetItemRectSize();
        }

        private void DrawAutomationField(float inputWidth, float scale)
        {
            DrawBoutiqueLabel("Glamourer Automation", false,
                "Optional: name of a Glamourer automation. Must match exactly.");
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.InputText("##GlamourerAutomation", ref editedAutomation, 100);
        }

        private void DrawCustomizeField(float inputWidth, float scale)
        {
            DrawBoutiqueLabel("Customize+ Profile", false,
                "Optional: select a Customize+ profile. Right-click to clear.");
            var customizeOptions = plugin.IntegrationListProvider?.GetCustomizePlusProfiles() ?? Array.Empty<string>();
            var currentCustomize = plugin.IntegrationListProvider?.GetCurrentCustomizePlusProfile();

            if (AutocompleteCombo.Draw("##CustomizePlus", ref editedCustomizeProfile, customizeOptions, inputWidth, "Select profile...", currentActive: currentCustomize))
            {
                if (!isAdvancedModeDesign)
                {
                    editedDesignMacro = (isSecretDesignMode && !plugin.Configuration.EnableConflictResolution)
                        ? GenerateSecretDesignMacro(plugin.Characters[activeCharacterIndex])
                        : GenerateDesignMacro(plugin.Characters[activeCharacterIndex]);
                }
                else
                {
                    UpdateAdvancedMacroCustomize();
                }
            }
        }

        private void DrawGearsetField(float inputWidth, float scale)
        {
            DrawBoutiqueLabel("Assigned Gearset", false,
                "Optional: switch to this gearset when applying this design.");
            ImGui.SetNextItemWidth(inputWidth);

            var gearsets = plugin.GetPlayerGearsets();
            string currentDisplay = "None (use character setting)";
            if (editedGearset.HasValue)
            {
                var matchingGearset = gearsets.FirstOrDefault(g => g.Number == editedGearset.Value);
                if (matchingGearset.Number > 0)
                    currentDisplay = plugin.GetGearsetDisplayName(matchingGearset.Number, matchingGearset.JobId, matchingGearset.Name);
                else
                    currentDisplay = $"Gearset {editedGearset.Value}";
            }

            if (ImGui.BeginCombo("##AssignedGearset", currentDisplay))
            {
                if (ImGui.Selectable("None (use character setting)", !editedGearset.HasValue))
                    editedGearset = null;
                if (!editedGearset.HasValue)
                    ImGui.SetItemDefaultFocus();

                foreach (var gearset in gearsets)
                {
                    string displayName = plugin.GetGearsetDisplayName(gearset.Number, gearset.JobId, gearset.Name);
                    bool isSelected = editedGearset.HasValue && editedGearset.Value == gearset.Number;
                    if (ImGui.Selectable(displayName, isSelected))
                        editedGearset = gearset.Number;
                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }

        private void DrawPreviewImageField(float scale)
        {
            ImGui.SetCursorPosX(_dpFormIndent);
            DrawBoutiqueLabel("Preview Image", false,
                "Optional: image shown when hovering this design in the list.");
            ImGui.SetCursorPosX(_dpFormIndent);
            if (ImGui.Button("Browse..."))
            {
                SelectPreviewImage();
            }

            // Add Paste button
            ImGui.SameLine();
            bool clipboardHasImage = IsClipboardImageAvailable();
            
            if (!clipboardHasImage)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
            }
            
            if (ImGui.Button("Paste"))
            {
                if (clipboardHasImage)
                {
                    PasteImageFromClipboard();
                }
            }
            
            if (!clipboardHasImage)
            {
                ImGui.PopStyleVar();
            }
            
            if (ImGui.IsItemHovered())
            {
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(clipboardHasImage
                    ? "Paste image from clipboard"
                    : "No image in clipboard\nCopy a screenshot first (Win+Shift+S)");
            }

            // Add Clear button
            ImGui.SameLine();
            if (ImGui.Button("Clear") && !string.IsNullOrEmpty(editedDesignPreviewPath))
            {
                editedDesignPreviewPath = "";
            }

            // Apply pending image path from file picker
            if (pendingDesignImagePath != null)
            {
                lock (this)
                {
                    editedDesignPreviewPath = pendingDesignImagePath;
                    pendingDesignImagePath = null;
                }
            }

            // Apply pending pasted image path
            if (pendingPastedImagePath != null)
            {
                lock (this)
                {
                    editedDesignPreviewPath = pendingPastedImagePath;
                    pendingPastedImagePath = null;
                }
            }

            // Show current preview
            if (!string.IsNullOrEmpty(editedDesignPreviewPath) && File.Exists(editedDesignPreviewPath))
            {
                var texture = Plugin.TextureProvider.GetFromFile(editedDesignPreviewPath).GetWrapOrDefault();
                if (texture != null)
                {
                    float maxSize = 100f * scale;
                    var (width, height) = CalculateImageDimensions(texture, maxSize);
                    ImGui.SetCursorPosX(_dpFormIndent);
                    ImGui.Image((ImTextureID)texture.Handle, new Vector2(width, height));
                }
            }
            else if (!string.IsNullOrEmpty(editedDesignPreviewPath))
            {
                ImGui.SetCursorPosX(_dpFormIndent);
                ImGui.Text("Preview: " + Path.GetFileName(editedDesignPreviewPath));
            }
        }

        private void DrawSecretModeDesignField(Character character, float scale)
        {
            ImGui.SetCursorPosX(_dpFormIndent);
            DrawBoutiqueLabel("Mod Manager", false,
                "Select which mods to enable and configure for this design.");
            ImGui.SetCursorPosX(_dpFormIndent);

            // Get mod count for button text

            // Get mod count for button text
            int selectedModCount = 0;
            Dictionary<string, bool> modState = null;
            HashSet<string> pinOverrides = null;

            if (isNewDesign)
            {
                // For new designs, use temporary state
                modState = temporaryDesignSecretModState ?? new Dictionary<string, bool>();
                pinOverrides = temporaryDesignSecretModPinOverrides ?? new HashSet<string>();
                selectedModCount = modState.Count(kvp => kvp.Value);
            }
            else if (!string.IsNullOrEmpty(originalDesignName))
            {
                // For existing designs, use the design's state
                var currentDesign = character.Designs.FirstOrDefault(d => d.Name == originalDesignName);
                if (currentDesign != null)
                {
                    modState = currentDesign.SecretModState ?? new Dictionary<string, bool>();
                    pinOverrides = currentDesign.SecretModPinOverrides ?? new HashSet<string>();
                    selectedModCount = modState.Count(kvp => kvp.Value);
                }
            }

            string buttonText = selectedModCount > 0 
                ? $"Configure Mods ({selectedModCount} selected)"
                : "Configure Mods";

            // Validate that design name is filled before opening mod manager
            bool hasValidDesignName = !string.IsNullOrWhiteSpace(editedDesignName);
            
            if (!hasValidDesignName)
                ImGui.BeginDisabled();
            
            if (ImGui.Button(buttonText))
            {
                if (hasValidDesignName)
                {
                    // Open Secret Mode mod window for this design
                    var currentDesignForWindow = isNewDesign ? null : character.Designs.FirstOrDefault(d => d.Name == originalDesignName);
                    plugin.SecretModeModWindow.Open(
                        activeCharacterIndex,
                        modState,
                        LogAndReturnPins(character),
                        (newModState) =>
                        {
                            // Save callback for design-level mod state
                            if (isNewDesign)
                            {
                                // For new designs, store temporarily
                                temporaryDesignSecretModState = newModState;
                            }
                            else if (!string.IsNullOrEmpty(originalDesignName))
                            {
                                // For existing designs, save directly AND update temporary state
                                var design = character.Designs.FirstOrDefault(d => d.Name == originalDesignName);
                                if (design != null)
                                {
                                    design.SecretModState = newModState;
                                    temporaryDesignSecretModState = newModState; // Keep temp state in sync
                                    plugin.SaveConfiguration();
                                }
                            }
                        },
                        (pins) =>
                        {
                            // Character pin callback
                            Plugin.Log.Information($"[PIN DEBUG] Design save callback: saving {pins?.Count ?? 0} pins to character");
                            character.SecretModPins = pins?.ToList();
                            plugin.SaveConfiguration();
                        },
                        currentDesignForWindow,  // Pass the design context
                        character.Name,  // Pass the character name for context
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
            
            // Quick update button for gear/hair changes
            ImGui.SameLine();
            
            ImGui.PushFont(UiBuilder.IconFont);
            
            bool canQuickUpdate = hasValidDesignName && plugin.Configuration.EnableConflictResolution;
            
            if (!canQuickUpdate)
                ImGui.BeginDisabled();
            
            if (ImGui.Button("\uf2f1")) // Import icon - suggests pulling in current state
            {
                if (canQuickUpdate)
                {
                    PerformQuickGearHairUpdate(character);
                }
            }
            
            if (!canQuickUpdate)
                ImGui.EndDisabled();
            
            ImGui.PopFont();
            
            if (ImGui.IsItemHovered())
            {
                if (canQuickUpdate)
                {
                    CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Update gear/hair changes");
                }
                else if (!hasValidDesignName)
                {
                    CharacterSelectPlugin.Windows.Styles.Boutique.WarningTooltip(
                        "Disabled", "Enter a Design Name first.");
                }
                else if (!plugin.Configuration.EnableConflictResolution)
                {
                    CharacterSelectPlugin.Windows.Styles.Boutique.WarningTooltip(
                        "Disabled", "Conflict Resolution must be enabled.");
                }
            }

            if (!hasValidDesignName)
            {
                ImGui.EndDisabled();

                // Show tooltip explaining why the button is disabled
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    CharacterSelectPlugin.Windows.Styles.Boutique.WarningTooltip(
                        "Disabled", "Please enter a Design Name before configuring mods.");
                }
            }
        }

        private void DrawAdvancedModeToggle(float scale)
        {
            float fs = Boutique.FormScale;
            ImFontPtr lblF, descF;
            using (Plugin.Instance?.OutfitMed13?.Push()) { lblF  = ImGui.GetFont(); }
            using (Plugin.Instance?.OutfitMed13?.Push()) { descF = ImGui.GetFont(); }

            bool prev = isAdvancedModeDesign;
            ImGui.SetCursorPosX(_dpFormIndent);
            Boutique.DrawBoutiqueCheckbox(
                "dp_enable_adv", ref isAdvancedModeDesign,
                "Enable advanced mode",
                "Custom macro on apply",
                scale, lblF, descF);

            if (prev != isAdvancedModeDesign)
            {
                if (isAdvancedModeDesign)
                {
                    // Just toggled ON, load existing macro (or generate fresh)
                    // and pop the editor open as a convenience.
                    if (activeCharacterIndex >= 0 && activeCharacterIndex < plugin.Characters.Count && !isNewDesign)
                    {
                        var character = plugin.Characters[activeCharacterIndex];
                        var existingDesign = character.Designs.FirstOrDefault(d => d.Name == originalDesignName);
                        advancedDesignMacroText = (existingDesign != null && !string.IsNullOrEmpty(existingDesign.AdvancedMacro))
                            ? existingDesign.AdvancedMacro
                            : EnsureProperDesignMacroStructure();
                    }
                    else
                    {
                        advancedDesignMacroText = EnsureProperDesignMacroStructure();
                    }
                    isAdvancedModeWindowOpen = true;
                }
                else
                {
                    // Toggled OFF, close the window if it's open.
                    isAdvancedModeWindowOpen = false;
                }
            }

            // "EDIT MACRO" button to (re)open the editor without touching the
            // checkbox state. Only shown when advanced mode is on; closing the
            // window no longer unchecks the toggle, so this is the way back in.
            if (isAdvancedModeDesign)
            {
                ImGui.Dummy(new Vector2(0f, 4f * fs));
                ImGui.SetCursorPosX(_dpFormIndent);
                using (Plugin.Instance?.OswaldSemi13?.Push())
                {
                    if (Boutique.DrawChamferedTextButton(
                            "EDIT MACRO", 130f * fs, 26f * fs, scale, "dp_edit_macro_btn"))
                    {
                        if (string.IsNullOrEmpty(advancedDesignMacroText))
                        {
                            if (activeCharacterIndex >= 0 && activeCharacterIndex < plugin.Characters.Count && !isNewDesign)
                            {
                                var character = plugin.Characters[activeCharacterIndex];
                                var existingDesign = character.Designs.FirstOrDefault(d => d.Name == originalDesignName);
                                advancedDesignMacroText = (existingDesign != null && !string.IsNullOrEmpty(existingDesign.AdvancedMacro))
                                    ? existingDesign.AdvancedMacro
                                    : EnsureProperDesignMacroStructure();
                            }
                            else
                            {
                                advancedDesignMacroText = EnsureProperDesignMacroStructure();
                            }
                        }
                        isAdvancedModeWindowOpen = true;
                    }
                }
            }
        }

        private void DrawFormActionButtons(Character character, float scale)
        {
            float buttonWidth = 85 * scale;
            float buttonHeight = 20 * scale;
            float buttonSpacing = 8 * scale;
            float totalButtonWidth = (buttonWidth * 2 + buttonSpacing);
            float availableWidth = ImGui.GetContentRegionAvail().X;
            float buttonPosX = (availableWidth > totalButtonWidth) ? (availableWidth - totalButtonWidth) / 2f : 0;

            ImGui.SetCursorPosX(buttonPosX);

            bool canSave = !string.IsNullOrWhiteSpace(editedDesignName) && !string.IsNullOrWhiteSpace(editedGlamourerDesign);

            // Center text in buttons
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4 * scale, 4 * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));

            // Save button styling
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.5f, 0.3f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.6f, 0.4f, 1.0f));

            if (!canSave)
                ImGui.BeginDisabled();

            if (ImGui.Button("Save Design", new Vector2(buttonWidth, 0)))
            {
                SaveDesign(character);
                CloseDesignEditor();
            }
            plugin.SaveDesignButtonPos = ImGui.GetItemRectMin();
            plugin.SaveDesignButtonSize = ImGui.GetItemRectSize();

            if (!canSave)
                ImGui.EndDisabled();

            ImGui.PopStyleColor(3);

            ImGui.SameLine();

            // Cancel button styling
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.2f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.3f, 0.3f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.4f, 0.4f, 1.0f));

            if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
            {
                CloseDesignEditor();
            }

            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar(2);
        }

        private void DrawSortingControls(Character character, float scale)
        {
            ImGui.Text("Sort Designs By:");
            ImGui.SameLine();

            float comboWidth = Math.Max(120f * scale, ImGui.GetContentRegionAvail().X - (20f * scale));
            ImGui.SetNextItemWidth(comboWidth);

            if (ImGui.BeginCombo("##DesignSortDropdown", currentDesignSort.ToString()))
            {
                if (ImGui.Selectable("Favourites", currentDesignSort == DesignSortType.Favorites))
                {
                    SetDesignSort(0); // Favorites
                    SortDesigns(character);
                }
                if (ImGui.Selectable("Alphabetical", currentDesignSort == DesignSortType.Alphabetical))
                {
                    SetDesignSort(1); // Alphabetical
                    SortDesigns(character);
                }
                if (ImGui.Selectable("Newest", currentDesignSort == DesignSortType.Recent))
                {
                    SetDesignSort(2); // Recent
                    SortDesigns(character);
                }
                if (ImGui.Selectable("Oldest", currentDesignSort == DesignSortType.Oldest))
                {
                    SetDesignSort(3); // Oldest
                    SortDesigns(character);
                }
                if (ImGui.Selectable("Manual", currentDesignSort == DesignSortType.Manual))
                {
                    SetDesignSort(4); // Manual
                }
                ImGui.EndCombo();
            }
            
            // Search input field
            if (showSearchBar)
            {
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.InputTextWithHint("##SearchDesigns", "Search designs...", ref searchQuery, 100))
                {
                }
            }
        }
        
        // ── Boutique design list (translated from design-mockups/design-panel/01-themed.html) ──
        private enum DpRowAction { Apply, Edit, Delete }

        private void DrawBoutiqueDesignList(Character character, float scale)
        {
            float remainingHeight = ImGui.GetContentRegionAvail().Y;
            remainingHeight = Math.Max(remainingHeight, 100f * scale);

            // Transparent child so we can paint our own velvet bg
            ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 0f));
            ImGui.BeginChild("BoutiqueDesignList", new Vector2(0, remainingHeight), false,
                ImGuiWindowFlags.AlwaysVerticalScrollbar);

            // Velvet vertical-gradient list ground (matches features.html / mockup v6).
            // Anchor to the child window's screen rect, NOT GetCursorScreenPos +
            // GetContentRegionAvail. The cursor moves above the viewport when scrolled
            // and avail.Y grows past the visible area, which would otherwise drift the
            // gradient mapping (and make the bg appear to "switch colour" halfway down).
            var listMin = ImGui.GetWindowPos();
            var listMax = listMin + ImGui.GetWindowSize();
            var dl = ImGui.GetWindowDrawList();
            // List ground follows the user's Design Panel override so the
            // entire panel (header + list area) shifts together.
            Vector4 listTopV = Boutique.SlotOrDefault("custom.designPanelBg", Boutique.Velvet);
            Vector4 listBotV = Boutique.Lerp(listTopV, new Vector4(0f, 0f, 0f, listTopV.W), 0.40f);
            uint vTop = Boutique.U32(listTopV);
            uint vBot = Boutique.U32(listBotV);
            dl.AddRectFilled(listMin, listMax, vTop);
            dl.AddRectFilledMultiColor(listMin, listMax, vTop, vTop, vBot, vBot);

            var renderItems = BuildRenderItems(character);

            bool anyRowHovered = false;
            bool anyHeaderHovered = false;

            // Top breathing room (mockup .list-area { padding: 8px 0 })
            ImGui.Dummy(new Vector2(0, 14f * scale));

            foreach (var entry in renderItems)
            {
                if (entry.isFolder)
                {
                    var folder = (DesignFolder)entry.item;
                    bool folderWasHovered = false;
                    DrawBoutiqueFolderItem(character, folder, ref folderWasHovered, scale);
                    if (folderWasHovered) anyHeaderHovered = true;
                }
                else
                {
                    var design = (CharacterDesign)entry.item;
                    bool rowWasHovered = false;
                    DrawBoutiqueDesignRow(character, design, scale, false, default, 0f, ref rowWasHovered);
                    if (rowWasHovered) anyRowHovered = true;
                }
            }

            HandleDropToRoot(anyHeaderHovered, anyRowHovered, character);

            ImGui.Dummy(new Vector2(0, 12f * scale));
            ImGui.EndChild();
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
        }

        private void DrawBoutiqueFolderItem(Character character, DesignFolder folder, ref bool wasHovered, float scale)
        {
            bool isRenaming = isRenamingFolder && folder.Id == renameFolderId;
            bool isCollapsed = boutiqueFolderCollapsed.TryGetValue(folder.Id, out bool c) ? c : false;
            var folderColor4 = GetFolderColor(character, folder);
            var folderColor = new Vector4(folderColor4.X, folderColor4.Y, folderColor4.Z, 1f);

            float marginTop = 14f * scale;
            float headH = 32f * scale;
            float chamfer = 8f * scale;
            float headPadL = 8f * scale; // 8px breathing margin from panel edge

            // Top spacing
            ImGui.Dummy(new Vector2(0, marginTop));
            var rowOriginX = ImGui.GetCursorScreenPos().X;
            var headMin = new Vector2(rowOriginX + headPadL, ImGui.GetCursorScreenPos().Y);
            float headW = ImGui.GetContentRegionAvail().X - headPadL * 2;
            var headMax = headMin + new Vector2(headW, headH);

            ImGui.PushID($"bfolder_{folder.Id}");

            if (isRenaming)
            {
                ImGui.SetCursorScreenPos(new Vector2(headMin.X + 16f * scale, headMin.Y + (headH - ImGui.GetFrameHeight()) * 0.5f));
                ImGui.PushItemWidth(headW - 32f * scale);
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.10f, 0.12f, 0.16f, 1f));
                if (ImGui.InputText($"##InlineRenameBoutique_{folder.Id}", ref renameFolderBuf, 128, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    folder.Name = renameFolderBuf;
                    isRenamingFolder = false;
                    plugin.SaveConfiguration();
                    plugin.RefreshTreeItems(character);
                }
                ImGui.PopStyleColor();
                ImGui.PopItemWidth();
                ImGui.PopID();
                ImGui.SetCursorScreenPos(new Vector2(rowOriginX, headMax.Y));

                if (!isCollapsed)
                    DrawBoutiqueFolderContents(character, folder, scale, folderColor);
                wasHovered = false;
                return;
            }

            // Whole-head hit area (excluding chevron region on the right)
            float chevSlot = 28f * scale;
            ImGui.SetCursorScreenPos(headMin);
            bool clicked = ImGui.InvisibleButton("##bfolder_btn", new Vector2(headW - chevSlot, headH));
            bool hoveredHead = ImGui.IsItemHovered();
            wasHovered = hoveredHead;

            // Drag source on the head
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceAllowNullId))
            {
                draggedFolder = folder;
                ImGui.SetDragDropPayload("FOLDER_MOVE", ReadOnlySpan<byte>.Empty, ImGuiCond.None);
                ImGui.TextUnformatted($"Moving Folder: {folder.Name}");
                ImGui.EndDragDropSource();
            }
            DrawFolderContextMenu(character, folder, scale);

            // Drop handling
            if (hoveredHead && (draggedDesign != null || draggedFolder != null))
            {
                var dlx = ImGui.GetWindowDrawList();
                uint dropCol = ImGui.GetColorU32(new Vector4(0.30f, 0.50f, 1f, 1f));
                dlx.AddRect(headMin, headMax, dropCol, 0, ImDrawFlags.None, 2f * scale);
            }
            if (hoveredHead && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                if (draggedDesign != null)
                {
                    draggedDesign.FolderId = folder.Id;
                    plugin.SaveConfiguration();
                    plugin.RefreshTreeItems(character);
                    draggedDesign = null;
                }
                else if (draggedFolder != null && draggedFolder != folder)
                {
                    draggedFolder.ParentFolderId = folder.Id;
                    plugin.SaveConfiguration();
                    plugin.RefreshTreeItems(character);
                    draggedFolder = null;
                }
            }

            var dl = ImGui.GetWindowDrawList();

            // ── Folder tab body, Surface2 → Surface1 vertical gradient with TR-only chamfer ──
            Vector4 topCol = hoveredHead
                ? new Vector4(Boutique.Surface2.X * 1.08f, Boutique.Surface2.Y * 1.08f, Boutique.Surface2.Z * 1.08f, 1f)
                : Boutique.Surface2;
            Vector4 botCol = Boutique.Surface1;
            Boutique.DrawFolderTabBody(dl, headMin, headMax, topCol, botCol, Boutique.Velvet, chamfer);

            // Inset 1px hairline
            Boutique.StrokeSlip(dl, headMin, headMax, chamfer, // re-uses slip but only TR will look chamfered
                Boutique.U32(Boutique.WithAlpha(new Vector4(80/255f, 90/255f, 100/255f, 1f), 0.20f)), 1f);

            // Top binding stripe (2px coloured edge with glow)
            Boutique.DrawFolderTopBinding(dl, headMin, headMax, scale, folderColor, chamfer);

            // TR diagonal corner glow inside the chamfered notch
            Boutique.DrawRowCornerGlow(dl, new Vector2(headMax.X, headMin.Y), scale, folderColor, 0.35f);

            float midY = (headMin.Y + headMax.Y) * 0.5f;
            float cursorX = headMin.X + 10f * scale;

            // Folder glyph (open / closed)
            string folderGlyph = isCollapsed ? "" : ""; // fa-folder / fa-folder-open
            ImGui.PushFont(UiBuilder.IconFont);
            var glyphSize = ImGui.CalcTextSize(folderGlyph);
            ImGui.PopFont();
            float glyphFs = UiBuilder.IconFont.FontSize;
            // Folder glyph glow
            for (int i = 2; i > 0; i--)
            {
                float r = i * 1.5f * scale;
                dl.AddCircleFilled(new Vector2(cursorX + glyphSize.X * 0.5f, midY), r,
                    Boutique.U32(Boutique.WithAlpha(folderColor, 0.18f / i)));
            }
            dl.AddText(UiBuilder.IconFont, glyphFs,
                new Vector2(cursorX, midY - glyphSize.Y * 0.5f),
                Boutique.U32(folderColor), folderGlyph);
            cursorX += glyphSize.X + 8f * scale;

            // Roman numeral kicker (calculate from folder index)
            int folderIndex = character.DesignFolders
                .Where(f2 => f2.ParentFolderId == folder.ParentFolderId)
                .OrderBy(f2 => f2.SortOrder)
                .ToList()
                .IndexOf(folder) + 1;
            string roman = ToRoman(folderIndex);

            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                var rSize = ImGui.CalcTextSize(roman);
                dl.AddText(new Vector2(cursorX, midY - rSize.Y * 0.5f),
                    Boutique.U32(new Vector4(folderColor.X, folderColor.Y, folderColor.Z, 0.85f)), roman);
                cursorX += rSize.X + 10f * scale;
            }

            // Count badge on the right (just text, not boxed - matches v6 mockup)
            int folderCount =
                character.Designs.Count(d => d.FolderId == folder.Id) +
                character.DesignFolders.Count(f2 => f2.ParentFolderId == folder.Id);
            string countText = $"{folderCount:00} {(folderCount == 1 ? "LOOK" : "LOOKS")}";

            float chevX = headMax.X - 26f * scale;
            float countX;
            using (Plugin.Instance?.OswaldSemi9?.Push())
            {
                var ctSize = ImGui.CalcTextSize(countText);
                countX = chevX - ctSize.X - 10f * scale;
                dl.AddText(new Vector2(countX, midY - ctSize.Y * 0.5f),
                    Boutique.U32(Boutique.TextFaint), countText);
            }

            // Folder label (between roman numeral and count)
            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                string lbl = folder.Name.ToUpperInvariant();
                var lblSize = ImGui.CalcTextSize(lbl);
                float lblY = midY - lblSize.Y * 0.5f;
                float lblMaxX = countX - 8f * scale;
                dl.PushClipRect(new Vector2(cursorX, headMin.Y), new Vector2(lblMaxX, headMax.Y), true);
                dl.AddText(new Vector2(cursorX, lblY),
                    Boutique.U32(hoveredHead ? Boutique.Gold : Boutique.Text), lbl);
                dl.PopClipRect();
            }

            // Chevron button (independent click target)
            var chevMin = new Vector2(chevX, midY - 11f * scale);
            ImGui.SetCursorScreenPos(chevMin);
            bool chevClicked = ImGui.InvisibleButton("##bfolder_chev", new Vector2(22f * scale, 22f * scale));
            bool chevHovered = ImGui.IsItemHovered();
            uint chevBg = Boutique.U32(new Vector4(0, 0, 0, 0.30f));
            uint chevBorder = chevHovered
                ? Boutique.U32(folderColor)
                : Boutique.U32(Boutique.BorderSoft);
            dl.AddRectFilled(chevMin, chevMin + new Vector2(22f * scale, 22f * scale), chevBg);
            dl.AddRect(chevMin, chevMin + new Vector2(22f * scale, 22f * scale), chevBorder, 0f, ImDrawFlags.None, 1f * scale);
            // Chevron triangle (down when open, right when collapsed)
            float chevCx = chevMin.X + 11f * scale;
            float chevCy = chevMin.Y + 11f * scale;
            float chevTSize = 3.5f * scale;
            uint chevTC = Boutique.U32(chevHovered ? folderColor : Boutique.TextDim);
            if (isCollapsed)
            {
                dl.AddTriangleFilled(
                    new Vector2(chevCx - chevTSize * 0.5f, chevCy - chevTSize),
                    new Vector2(chevCx + chevTSize, chevCy),
                    new Vector2(chevCx - chevTSize * 0.5f, chevCy + chevTSize),
                    chevTC);
            }
            else
            {
                dl.AddTriangleFilled(
                    new Vector2(chevCx - chevTSize, chevCy - chevTSize * 0.5f),
                    new Vector2(chevCx + chevTSize, chevCy - chevTSize * 0.5f),
                    new Vector2(chevCx, chevCy + chevTSize),
                    chevTC);
            }

            if (clicked || chevClicked)
            {
                boutiqueFolderCollapsed[folder.Id] = !isCollapsed;
                isCollapsed = !isCollapsed;
            }

            ImGui.PopID();
            ImGui.SetCursorScreenPos(new Vector2(rowOriginX, headMax.Y));

            // ── Spine + folder body ──
            if (!isCollapsed)
            {
                float spineX = headMin.X; // align spine to left edge of folder head
                float spineTopY = headMax.Y + 2f * scale;
                DrawBoutiqueFolderContents(character, folder, scale, folderColor);
                float spineBotY = ImGui.GetCursorScreenPos().Y - 2f * scale;
                Boutique.DrawFolderSpine(dl, spineX, spineTopY, spineBotY, scale, folderColor);
            }
        }

        private void DrawBoutiqueFolderContents(Character character, DesignFolder folder, float scale, Vector4 folderColor)
        {
            var foldersToShow = character.DesignFolders.Where(f => f.ParentFolderId == folder.Id);
            var designsToShow = character.Designs.Where(d => d.FolderId == folder.Id);

            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                foldersToShow = foldersToShow.Where(f => FolderContainsMatchingDesigns(character, f));
                designsToShow = designsToShow.Where(d => MatchesSearchQuery(d));
            }

            foreach (var child in foldersToShow.OrderBy(f => f.SortOrder))
            {
                bool dummy = false;
                DrawBoutiqueFolderItem(character, child, ref dummy, scale);
            }
            foreach (var design in designsToShow.OrderBy(d => d.SortOrder))
            {
                bool dummy = false;
                // Spine X is at panel left + 8px (matches folder head padX)
                float spineX = ImGui.GetCursorScreenPos().X + 8f * scale;
                DrawBoutiqueDesignRow(character, design, scale, true, folderColor, spineX, ref dummy);
            }
        }

        private static string ToRoman(int n)
        {
            if (n <= 0) return "";
            string[] thousands = { "", "M", "MM", "MMM" };
            string[] hundreds  = { "", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM" };
            string[] tens      = { "", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC" };
            string[] ones      = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
            return thousands[(n / 1000) % 4] + hundreds[(n / 100) % 10]
                 + tens[(n / 10) % 10] + ones[n % 10];
        }

        private void DrawBoutiqueDesignRow(Character character, CharacterDesign design, float scale,
            bool isInsideFolder, Vector4 folderColor, float spineX, ref bool wasHovered)
        {
            // ── Layout constants (v6) ─────────────────────────────────────
            float rowH = 38f * scale;
            float chamfer = 6f * scale;
            float marginV = 2f * scale;
            float padOutsideFolder = 8f * scale;
            float indentInsideFolder = 18f * scale; // 8 (margin) + 10 (clear spine)

            ImGui.Dummy(new Vector2(0, marginV));
            var rowOriginX = ImGui.GetCursorScreenPos().X;
            float leftPad = isInsideFolder ? indentInsideFolder : padOutsideFolder;
            float rightPad = padOutsideFolder;
            float naturalY = ImGui.GetCursorScreenPos().Y;
            // Track position changes in WINDOW-local coords (scroll-stable). Using
            // GetCursorScreenPos here would treat scroll movement as a position
            // change, firing slide animations on every visible row each frame and
            // producing visible bounce-back when the user stops scrolling.
            float naturalLocalY = ImGui.GetCursorPos().Y;

            // ── Slide animation: detect natural-Y change frame-to-frame and ease the
            //    visual displacement back to zero over RowSlideDurationS. ────────────
            double now = ImGui.GetTime();
            if (rowLastNaturalY.TryGetValue(design.Id, out float prevY))
            {
                float deltaY = prevY - naturalLocalY;
                if (MathF.Abs(deltaY) > 4f * scale) // threshold to ignore micro-shifts
                {
                    rowSlideAnim[design.Id] = (deltaY, now);
                }
            }
            rowLastNaturalY[design.Id] = naturalLocalY;

            float slideDisplacement = 0f;
            if (rowSlideAnim.TryGetValue(design.Id, out var anim))
            {
                float t = (float)((now - anim.startTime) / RowSlideDurationS);
                if (t >= 1f)
                    rowSlideAnim.Remove(design.Id);
                else
                {
                    // ease-out cubic, strong start, gentle finish
                    float eased = 1f - MathF.Pow(1f - t, 3f);
                    slideDisplacement = anim.displacement * (1f - eased);
                }
            }

            var rowMin = new Vector2(rowOriginX + leftPad, naturalY + slideDisplacement);
            float rowW = ImGui.GetContentRegionAvail().X - leftPad - rightPad;
            var rowMax = rowMin + new Vector2(rowW, rowH);

            ImGui.PushID($"brow_{design.Id}");

            bool isApplied = IsDesignCurrentlyActive(character, design);

            // Layout-only dummy (no input claim) so subsequent items advance correctly
            ImGui.SetCursorScreenPos(rowMin);
            ImGui.Dummy(new Vector2(rowW, rowH));

            // Hover detection via rect-test, does NOT claim mouse input
            bool hovered = ImGui.IsMouseHoveringRect(rowMin, rowMax, true);
            wasHovered = hovered;

            // Narrow drag-source button in the name area (NOT covering the star or actions)
            // so the action buttons + star can claim clicks unimpeded.
            float dragHitL = rowMin.X + 30f * scale;
            float dragHitR = rowMax.X - 80f * scale;
            if (dragHitR > dragHitL)
            {
                ImGui.SetCursorScreenPos(new Vector2(dragHitL, rowMin.Y));
                ImGui.InvisibleButton("##brow_drag", new Vector2(dragHitR - dragHitL, rowH));
                if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left) &&
                    ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceAllowNullId))
                {
                    draggedDesign = design;
                    ImGui.SetDragDropPayload("DESIGN_MOVE", ReadOnlySpan<byte>.Empty, ImGuiCond.None);
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 0.85f));
                    ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.10f, 0.10f, 0.12f, 0.92f));
                    ImGui.BeginGroup();
                    ImGui.Text(design.Name);
                    ImGui.EndGroup();
                    ImGui.PopStyleColor(2);
                    ImGui.EndDragDropSource();
                }
            }

            var dl = ImGui.GetWindowDrawList();

            // Hover lift (eased over 150ms via per-row T tracker)
            float dt = ImGui.GetIO().DeltaTime;
            boutiqueRowLiftT.TryGetValue(design.Id, out float liftT);
            liftT = hovered ? MathF.Min(1f, liftT + dt / 0.150f) : MathF.Max(0f, liftT - dt / 0.150f);
            boutiqueRowLiftT[design.Id] = liftT;
            float liftY = Boutique.EasedLift(liftT, 1f * scale);

            rowMin = new Vector2(rowMin.X, rowMin.Y + liftY);
            rowMax = new Vector2(rowMax.X, rowMax.Y + liftY);
            float midY = (rowMin.Y + rowMax.Y) * 0.5f;

            if (isInsideFolder)
            {
                Boutique.DrawSpineTick(dl, spineX, rowMin.X, midY, scale, folderColor, isApplied);
            }

            // Body gradient + slip-polygon chamfer cuts
            Vector4 bodyTop, bodyBot;
            if (isApplied)
            {
                bodyTop = new Vector4(0x1A / 255f, 0x14 / 255f, 0x08 / 255f, 1f);
                bodyBot = Boutique.Surface0;
            }
            else
            {
                bodyTop = Boutique.Surface1;
                bodyBot = Boutique.Surface0;
            }
            if (hovered)
            {
                bodyTop = new Vector4(bodyTop.X * 1.10f, bodyTop.Y * 1.10f, bodyTop.Z * 1.10f, 1f);
                bodyBot = new Vector4(bodyBot.X * 1.06f, bodyBot.Y * 1.06f, bodyBot.Z * 1.06f, 1f);
            }
            Boutique.DrawRowBodyGradient(dl, rowMin, rowMax, bodyTop, bodyBot, Boutique.Velvet, chamfer);

            Vector4 hairCol = isApplied
                ? Boutique.WithAlpha(Boutique.Gold, 0.30f)
                : Boutique.WithAlpha(new Vector4(80f / 255f, 90f / 255f, 100f / 255f, 1f), 0.18f);
            Boutique.DrawRowInsetHairline(dl, rowMin, rowMax, chamfer, hairCol);

            float cornerStrength = isApplied
                ? (hovered ? 0.45f : 0.34f)
                : (hovered ? 0.18f : 0.10f);
            Boutique.DrawRowCornerGlow(dl, new Vector2(rowMax.X, rowMin.Y), scale,
                Boutique.Gold, cornerStrength);

            // Left rail
            float railX = rowMin.X + 3f * scale;
            float railHalfH = 11f * scale;
            Vector4 railCol;
            float railGlowA = 0f;
            float railGlowR = 0f;
            if (isApplied)
            {
                railCol = Boutique.Gold;
                double t = ImGui.GetTime();
                float pulse = 0.5f + 0.5f * MathF.Sin((float)(t * 2.0 * Math.PI / 4.5));
                railGlowA = 0.45f + pulse * 0.30f;
                railGlowR = 5f * scale + pulse * 5f * scale;
            }
            else if (hovered)
            {
                railCol = Boutique.GoldDeep;
                railGlowA = 0.45f;
                railGlowR = 3f * scale;
            }
            else
            {
                railCol = Boutique.BorderSoft;
            }
            Boutique.DrawRowRail(dl, railX, midY, railHalfH, scale, railCol, railGlowA, railGlowR);

            float sheenProg = uiStyles.UpdateAndGetHoverSweepProgress($"brow_{design.Id}", hovered);
            if (sheenProg >= 0f)
            {
                Windows.Styles.UIStyles.DrawHoverSheen(dl, rowMin, rowMax, sheenProg, maxAlpha: 0.14f);
            }

            // ── Single-line layout: [rail] [star] [name] ........ [delta OR actions] ──
            float starColW = 14f * scale;
            float bodyStartX = rowMin.X + 12f * scale;
            float bodyMaxX = rowMax.X - 8f * scale;

            float actBtnSize = 22f * scale;
            float actGap = 1f * scale;
            float actionsW = 3 * actBtnSize + 2 * actGap;

            // Reserve right zone for actions on hover, or delta time at rest
            float rightZoneW;
            if (hovered)
                rightZoneW = actionsW + 6f * scale;
            else
            {
                using (Plugin.Instance?.OswaldSemi9?.Push())
                {
                    string deltaShort = FormatDeltaShort(design.LastApplied ?? design.DateAdded);
                    var dSz = ImGui.CalcTextSize(deltaShort);
                    rightZoneW = dSz.X + 8f * scale;
                }
            }

            // ── Favourite star (LEFT, vertically centred with the name) ──
            string starGlyph = ""; // fa-star solid
            if (plugin.Configuration.SelectedTheme == ThemeSelection.Custom)
            {
                int customIconId = plugin.Configuration.CustomTheme.FavoriteIconId;
                if (customIconId != 0)
                    starGlyph = ((FontAwesomeIcon)customIconId).ToIconString();
            }
            else if (SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration))
            {
                switch (SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration))
                {
                    case SeasonalTheme.Halloween:  starGlyph = "\uf6e2"; break;
                    case SeasonalTheme.Winter:
                    case SeasonalTheme.Christmas:  starGlyph = "\uf2dc"; break;
                    case SeasonalTheme.Valentines: starGlyph = "\uf004"; break;
                }
            }
            ImGui.PushFont(UiBuilder.IconFont);
            var rawStarSz = ImGui.CalcTextSize(starGlyph);
            ImGui.PopFont();
            float starFs = UiBuilder.IconFont.FontSize * 0.62f;
            float starScale = starFs / UiBuilder.IconFont.FontSize;
            float starW = rawStarSz.X * starScale;
            float starH = rawStarSz.Y * starScale;
            var starPos = new Vector2(
                bodyStartX + (starColW - starW) * 0.5f,
                midY - starH * 0.5f);

            var starHitMin = new Vector2(bodyStartX, midY - starColW * 0.5f);
            var starHitSz  = new Vector2(starColW, starColW);
            ImGui.SetCursorScreenPos(starHitMin);
            bool starClicked = ImGui.InvisibleButton("##brow_starhit", starHitSz);
            bool starHovered = ImGui.IsItemHovered();
            if (starHovered)
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(design.IsFavorite ? "Remove from favourites" : "Add to favourites");
            if (starClicked)
                ToggleFavourite(character, design, starPos + new Vector2(starW, starH) * 0.5f);

            // Favourite icon colour: Custom theme honours custom.favoriteIcon;
            // seasonal themes pick a tinted colour matching the glyph swap.
            Vector4 favBase = Boutique.Gold;
            var favPlugin = Plugin.Instance;
            if (favPlugin?.Configuration?.SelectedTheme == ThemeSelection.Custom &&
                favPlugin.Configuration.CustomTheme != null &&
                favPlugin.Configuration.CustomTheme.ColorOverrides.TryGetValue("custom.favoriteIcon", out var favPacked) &&
                favPacked.HasValue)
            {
                favBase = CharacterSelectPlugin.Windows.Styles.CustomThemeDefinitions.UnpackColor(favPacked.Value);
            }
            else if (favPlugin?.Configuration != null &&
                     SeasonalThemeManager.IsSeasonalThemeEnabled(favPlugin.Configuration))
            {
                switch (SeasonalThemeManager.GetEffectiveTheme(favPlugin.Configuration))
                {
                    case SeasonalTheme.Halloween:  favBase = new Vector4(1f, 0.55f, 0.10f, 1f); break;
                    case SeasonalTheme.Winter:     favBase = new Vector4(0.85f, 0.95f, 1.0f, 1f); break;
                    case SeasonalTheme.Christmas:  favBase = new Vector4(1f, 1f, 1f, 1f); break;
                    case SeasonalTheme.Valentines: favBase = new Vector4(1f, 0.35f, 0.55f, 1f); break;
                }
            }
            Vector4 favHover = Boutique.Lerp(favBase, new Vector4(1f, 1f, 1f, favBase.W), 0.20f);

            // Glow halo for favourited
            if (design.IsFavorite)
            {
                for (int i = 2; i > 0; i--)
                {
                    float r = (i + 1.5f) * scale;
                    dl.AddCircleFilled(starPos + new Vector2(starW, starH) * 0.5f, r,
                        Boutique.U32(Boutique.WithAlpha(favBase, 0.14f / i)));
                }
            }

            Vector4 sCol;
            if (design.IsFavorite)
                sCol = isApplied ? favHover : favBase;
            else
                sCol = starHovered ? favHover : Boutique.TextGhost;
            dl.AddText(UiBuilder.IconFont, starFs, starPos, Boutique.U32(sCol), starGlyph);

            // ── Name (Outfit Medium 13), vertically centred ──
            float nameStartX = bodyStartX + starColW + 8f * scale;
            float nameClipMaxX = bodyMaxX - rightZoneW;
            using (Plugin.Instance?.OutfitMed13?.Push())
            {
                string nameStr = design.Name;
                var nameMeasure = ImGui.CalcTextSize(nameStr);
                float nameAvail = nameClipMaxX - nameStartX;
                if (nameMeasure.X > nameAvail && nameAvail > 20f * scale)
                {
                    nameStr = TruncateWithEllipsis(nameStr, nameAvail);
                    nameMeasure = ImGui.CalcTextSize(nameStr);
                }
                float nameY = midY - nameMeasure.Y * 0.5f;
                uint nameCol = isApplied
                    ? Boutique.U32(Boutique.GoldWarm)
                    : Boutique.U32(Boutique.Text);
                dl.PushClipRect(new Vector2(nameStartX, rowMin.Y), new Vector2(nameClipMaxX, rowMax.Y), true);
                dl.AddText(new Vector2(nameStartX, nameY), nameCol, nameStr);
                dl.PopClipRect();
            }

            // ── Right cluster: actions on hover, delta at rest ──
            if (hovered)
            {
                float ax = rowMax.X - actionsW - 6f * scale;
                float ay = midY - actBtnSize * 0.5f;
                DrawDpRowActionButton(dl, scale, new Vector2(ax, ay), actBtnSize,
                    "", "Apply design", Boutique.Green,
                    character, design, DpRowAction.Apply, 1f);
                ax += actBtnSize + actGap;
                DrawDpRowActionButton(dl, scale, new Vector2(ax, ay), actBtnSize,
                    "", "Edit design", Boutique.CyanSoft,
                    character, design, DpRowAction.Edit, 1f);
                ax += actBtnSize + actGap;
                DrawDpRowActionButton(dl, scale, new Vector2(ax, ay), actBtnSize,
                    "", "Hold Ctrl+Shift to delete", Boutique.Red,
                    character, design, DpRowAction.Delete, 1f);
            }
            else
            {
                using (Plugin.Instance?.OswaldSemi9?.Push())
                {
                    string deltaText = FormatDeltaShort(design.LastApplied ?? design.DateAdded);
                    var dSz = ImGui.CalcTextSize(deltaText);
                    float dx = rowMax.X - dSz.X - 8f * scale;
                    float dy = midY - dSz.Y * 0.5f;
                    Vector4 deltaCol = isApplied ? Boutique.GoldDeep : Boutique.TextFaint;
                    dl.AddText(new Vector2(dx, dy), Boutique.U32(deltaCol), deltaText);
                }
            }

            HandleDesignDragDrop(character, design, rowMin, rowMax, hovered, scale);

            ImGui.PopID();
            // Advance cursor by NATURAL row height (subtract liftY + slideDisplacement so the
            // hover/slide visual offsets don't accumulate into the layout flow).
            ImGui.SetCursorScreenPos(new Vector2(rowOriginX, rowMax.Y - liftY - slideDisplacement + marginV));
        }

        // Compact 2-character delta: "3D", "2W", "1M", "TODAY"
        private static string FormatDeltaShort(DateTime when)
        {
            var span = DateTime.UtcNow - when.ToUniversalTime();
            if (span.TotalDays < 1) return "TODAY";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}D";
            if (span.TotalDays < 30) return $"{(int)(span.TotalDays / 7)}W";
            if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}M";
            return $"{(int)(span.TotalDays / 365)}Y";
        }

        // Long form for the meta line: "3D AGO", "2W AGO", "TODAY"
        private static string FormatDeltaLong(DateTime when)
        {
            var span = DateTime.UtcNow - when.ToUniversalTime();
            if (span.TotalDays < 1) return "TODAY";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}D AGO";
            if (span.TotalDays < 30) return $"{(int)(span.TotalDays / 7)}W AGO";
            if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}M AGO";
            return $"{(int)(span.TotalDays / 365)}Y AGO";
        }

        private void ToggleFavourite(Character character, CharacterDesign design, Vector2 effectPos)
        {
            // Snapshot the OLD favourite state so the sort key keeps using it for ~700ms.
            // This holds the row in place while the spark VFX plays.
            bool wasFav = design.IsFavorite;
            design.IsFavorite = !design.IsFavorite;
            pendingFavSortHold[design.Id] = (wasFav, DateTime.UtcNow.AddMilliseconds(FavSortDelayMs));

            string effectKey = $"{character.Name}_{design.Name}";
            if (!designFavoriteEffects.ContainsKey(effectKey))
                designFavoriteEffects[effectKey] = new FavoriteSparkEffect();
            designFavoriteEffects[effectKey].Trigger(effectPos, design.IsFavorite, plugin.Configuration);
            plugin.SaveConfiguration();
        }

        // Returns the value BuildRenderItems should use for sort-by-favourite, the held
        // pre-toggle value if a delay is active, otherwise the current truth.
        private bool GetSortFavValue(CharacterDesign design)
        {
            if (pendingFavSortHold.TryGetValue(design.Id, out var hold) && DateTime.UtcNow < hold.expiresAt)
                return hold.wasFavBefore;
            return design.IsFavorite;
        }

        private void ProcessPendingSort()
        {
            // Clean up expired holds, when removed, the next BuildRenderItems frame will
            // sort using the new IsFavorite value, causing the row to slide into position.
            if (pendingFavSortHold.Count == 0) return;
            var now = DateTime.UtcNow;
            var toRemove = pendingFavSortHold
                .Where(kv => now >= kv.Value.expiresAt)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var k in toRemove) pendingFavSortHold.Remove(k);
        }
        private void DrawDpRowActionButton(ImDrawListPtr dl, float scale, Vector2 min, float size,
            string glyph, string tooltip, Vector4 hoverInk,
            Character character, CharacterDesign design, DpRowAction action, float baseAlpha)
        {
            ImGui.SetCursorScreenPos(min);
            bool clicked = ImGui.InvisibleButton($"##dpact_{action}", new Vector2(size, size));
            bool hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                if (action == DpRowAction.Apply
                    && !string.IsNullOrEmpty(design.PreviewImagePath)
                    && File.Exists(design.PreviewImagePath))
                {
                    DrawBoutiquePreviewTooltip(design, tooltip, scale);
                }
                else
                {
                    CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(tooltip);
                }
            }

            if (hovered)
            {
                dl.AddRectFilled(min, min + new Vector2(size, size),
                    Boutique.U32(Boutique.WithAlpha(hoverInk, 0.10f)));
                dl.AddRect(min, min + new Vector2(size, size),
                    Boutique.U32(Boutique.WithAlpha(hoverInk, 0.40f)),
                    0f, ImDrawFlags.None, 1f * scale);
            }

            Vector4 ink = hovered
                ? new Vector4(hoverInk.X, hoverInk.Y, hoverInk.Z, baseAlpha)
                : new Vector4(Boutique.TextFaint.X, Boutique.TextFaint.Y, Boutique.TextFaint.Z, baseAlpha);

            ImGui.PushFont(UiBuilder.IconFont);
            var iconSize = ImGui.CalcTextSize(glyph);
            ImGui.PopFont();
            float iconFs = UiBuilder.IconFont.FontSize * 0.75f;
            float iconScale = iconFs / UiBuilder.IconFont.FontSize;
            var iconPos = new Vector2(
                min.X + (size - iconSize.X * iconScale) * 0.5f,
                min.Y + (size - iconSize.Y * iconScale) * 0.5f);
            dl.AddText(UiBuilder.IconFont, iconFs, iconPos, Boutique.U32(ink), glyph);

            if (clicked)
            {
                switch (action)
                {
                    case DpRowAction.Apply: ApplyDesignFromRow(character, design); break;
                    case DpRowAction.Edit:  EditDesignFromRow(character, design);  break;
                    case DpRowAction.Delete:
                        var io = ImGui.GetIO();
                        if (io.KeyCtrl && io.KeyShift)
                        {
                            character.Designs.Remove(design);
                            plugin.SaveConfiguration();
                        }
                        break;
                }
            }
        }

        // ── Boutique-styled tooltip helpers ──
        private void DrawBoutiqueTooltip(string text)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 7f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.04f, 0.05f, 0.08f, 0.96f));
            ImGui.PushStyleColor(ImGuiCol.Border, Boutique.WithAlpha(Boutique.Gold, 0.55f));
            ImGui.PushStyleColor(ImGuiCol.Text, Boutique.Text);
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(text);
            ImGui.EndTooltip();
            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar(3);
        }

        private void DrawBoutiquePreviewTooltip(CharacterDesign design, string actionLabel, float scale)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 8f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6f, 6f));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.04f, 0.05f, 0.08f, 0.96f));
            ImGui.PushStyleColor(ImGuiCol.Border, Boutique.WithAlpha(Boutique.Gold, 0.55f));
            ImGui.PushStyleColor(ImGuiCol.Text, Boutique.Text);
            ImGui.BeginTooltip();

            // Action label
            ImGui.TextColored(Boutique.GoldWarm, actionLabel);

            // Preview image (with gold inner-frame "gilt" inset, like the gallery card frame)
            var texture = Plugin.TextureProvider.GetFromFile(design.PreviewImagePath).GetWrapOrDefault();
            if (texture != null)
            {
                float maxSize = 240f * scale;
                var (w, h) = CalculateImageDimensions(texture, maxSize);
                var imgPos = ImGui.GetCursorScreenPos();
                ImGui.Image((ImTextureID)texture.Handle, new Vector2(w, h));
                // Gold inset frame
                var fdl = ImGui.GetWindowDrawList();
                fdl.AddRect(imgPos, imgPos + new Vector2(w, h),
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.40f)),
                    0f, ImDrawFlags.None, 1f);
            }

            // Design name caption (Outfit Med 13 if available)
            using (Plugin.Instance?.OutfitMed13?.Push())
            {
                ImGui.TextColored(Boutique.Text, design.Name);
            }

            ImGui.EndTooltip();
            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar(4);
        }

        private void ApplyDesignFromRow(Character character, CharacterDesign design)
        {
            if (plugin.Configuration.EnableGearsetAssignments)
            {
                var effectiveGearset = design.AssignedGearset ?? character.AssignedGearset;
                if (effectiveGearset.HasValue)
                    plugin.SwitchToGearset(effectiveGearset.Value);
            }

            if (design.SecretModState != null && design.SecretModState.Any())
            {
                if (!string.IsNullOrWhiteSpace(character.PenumbraCollection))
                    plugin.EnsurePenumbraCollectionAssignment(character.PenumbraCollection);

                _ = Task.Run(async () =>
                {
                    await plugin.ApplyDesignModState(character, design);
                    Plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        plugin.ExecuteMacro(design.Macro, character, design.Name);
                        TrackLastUsedDesign(character, design);
                    });
                });
            }
            else
            {
                plugin.ExecuteMacro(design.Macro, character, design.Name);
                TrackLastUsedDesign(character, design);
            }

            plugin.AchievementTracker?.OnDesignApplied();
        }

        private void TrackLastUsedDesign(Character character, CharacterDesign design)
        {
            plugin.Configuration.LastUsedDesignByCharacter[character.Name] = design.Name;
            plugin.Configuration.LastUsedDesignCharacterKey = character.Name;
            plugin.Configuration.LastUsedCharacterKey = character.Name;

            if (Plugin.ObjectTable.LocalPlayer is { } player && player.HomeWorld.IsValid)
            {
                string localName = player.Name.TextValue;
                string worldName = player.HomeWorld.Value.Name.ToString();
                string fullKey = $"{localName}@{worldName}";
                string pluginCharacterKey = $"{character.Name}@{worldName}";
                plugin.Configuration.LastUsedCharacterByPlayer[fullKey] = pluginCharacterKey;
            }
            plugin.Configuration.Save();
        }

        private void EditDesignFromRow(Character character, CharacterDesign design)
        {
            bool isCtrlShift = ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift;
            OpenEditDesignWindow(character, design);

            if (isCtrlShift && plugin.Configuration.EnableConflictResolution)
            {
                isSecretDesignMode = true;
                editedDesignMacro = (!plugin.Configuration.EnableConflictResolution && isSecretDesignMode)
                    ? GenerateSecretDesignMacro(character)
                    : GenerateDesignMacro(character);
                if (isAdvancedModeDesign)
                    advancedDesignMacroText = editedDesignMacro;
            }
        }

        private void DrawDesignList(Character character, float scale)
        {
            float remainingHeight = ImGui.GetContentRegionAvail().Y;

            // Minimum height
            remainingHeight = Math.Max(remainingHeight, 100f * scale);

            ImGui.BeginChild("DesignListBackground", new Vector2(0, remainingHeight), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);

            // Build unified list of folders and designs
            var renderItems = BuildRenderItems(character);

            // Render each item
            bool anyRowHovered = false;
            bool anyHeaderHovered = false;

            foreach (var entry in renderItems)
            {
                if (entry.isFolder)
                {
                    var folder = (DesignFolder)entry.item;
                    bool folderWasHovered = false;
                    DrawFolderItem(character, folder, ref folderWasHovered, scale);
                    if (folderWasHovered) anyHeaderHovered = true;
                }
                else
                {
                    var design = (CharacterDesign)entry.item;
                    DrawDesignRow(character, design, false, scale);
                    if (ImGui.IsItemHovered()) anyRowHovered = true;
                }
            }

            // Handle dropping outside any header
            HandleDropToRoot(anyHeaderHovered, anyRowHovered, character);

            ImGui.EndChild();
        }

        private void DrawFolderItem(Character character, DesignFolder folder, ref bool wasHovered, float scale)
        {
            bool isRenaming = isRenamingFolder && folder.Id == renameFolderId;
            bool open = false;

            // Get folder colour
            var folderColor = GetFolderColor(character, folder);

            if (isRenaming)
            {
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.2f, 0.2f, 0.2f, 1f));
                ImGui.SetNextItemWidth(200 * scale);
                if (ImGui.InputText("##InlineRename", ref renameFolderBuf, 128, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    folder.Name = renameFolderBuf;
                    isRenamingFolder = false;
                    plugin.SaveConfiguration();
                    plugin.RefreshTreeItems(character);
                }
                ImGui.PopStyleColor();
            }
            else
            {
                // Style the folder header with custom colour
                ImGui.PushStyleColor(ImGuiCol.Header, folderColor);
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(folderColor.X * 1.2f, folderColor.Y * 1.2f, folderColor.Z * 1.2f, folderColor.W));
                ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(folderColor.X * 1.4f, folderColor.Y * 1.4f, folderColor.Z * 1.4f, folderColor.W));

                open = ImGui.CollapsingHeader($"{folder.Name}##F{folder.Id}", ImGuiTreeNodeFlags.SpanFullWidth);

                ImGui.PopStyleColor(3);

                // Drag source
                if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceAllowNullId))
                {
                    draggedFolder = folder;
                    ImGui.SetDragDropPayload("FOLDER_MOVE", ReadOnlySpan<byte>.Empty, ImGuiCond.None);
                    ImGui.TextUnformatted($"Moving Folder: {folder.Name}");
                    ImGui.EndDragDropSource();
                }

                // Context menu
                DrawFolderContextMenu(character, folder, scale);
            }

            // Handle hover and drop logic
            var hdrMin = ImGui.GetItemRectMin();
            var hdrMax = ImGui.GetItemRectMax();
            bool overHeader = ImGui.IsMouseHoveringRect(hdrMin, hdrMax, true);
            wasHovered = overHeader;

            if ((draggedDesign != null || draggedFolder != null) && overHeader)
            {
                var dl = ImGui.GetWindowDrawList();
                uint col = ImGui.GetColorU32(new Vector4(0.3f, 0.5f, 1f, 1f));
                dl.AddRect(hdrMin, hdrMax, col, 0, ImDrawFlags.None, 2 * scale);
            }

            // Drop handling
            if (overHeader && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                if (draggedDesign != null)
                {
                    draggedDesign.FolderId = folder.Id;
                    plugin.SaveConfiguration();
                    plugin.RefreshTreeItems(character);
                    draggedDesign = null;
                }
                else if (draggedFolder != null && draggedFolder != folder)
                {
                    draggedFolder.ParentFolderId = folder.Id;
                    plugin.SaveConfiguration();
                    plugin.RefreshTreeItems(character);
                    draggedFolder = null;
                }
            }

            // Draw folder content
            if (open)
            {
                DrawFolderContents(character, folder, scale);
            }
        }

        private void DrawFolderContextMenu(Character character, DesignFolder folder, float scale)
        {
            if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                ImGui.OpenPopup($"FolderCtx{folder.Id}");

            if (ImGui.BeginPopup($"FolderCtx{folder.Id}"))
            {
                if (ImGui.MenuItem("Rename Folder"))
                {
                    renameFolderId = folder.Id;
                    renameFolderBuf = folder.Name;
                    isRenamingFolder = true;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.Separator();

                // Folder colour menu
                if (ImGui.BeginMenu("Folder Colour"))
                {
                    // Auto colour option
                    if (ImGui.MenuItem("Auto Colour", "", folder.CustomColor == null))
                    {
                        folder.CustomColor = null;
                        plugin.SaveConfiguration();
                    }

                    ImGui.Separator();

                    // Preset colours
                    var presetColors = new[]
                    {
                        ("Red", new Vector3(0.8f, 0.2f, 0.2f)),
                        ("Green", new Vector3(0.3f, 0.8f, 0.3f)),
                        ("Blue", new Vector3(0.3f, 0.5f, 0.9f)),
                        ("Yellow", new Vector3(0.9f, 0.8f, 0.2f)),
                        ("Purple", new Vector3(0.7f, 0.3f, 0.9f)),
                        ("Orange", new Vector3(1.0f, 0.6f, 0.2f)),
                        ("Pink", new Vector3(0.9f, 0.4f, 0.7f)),
                        ("Cyan", new Vector3(0.3f, 0.8f, 0.8f))
                    };

                    foreach (var (colorName, color) in presetColors)
                    {
                        bool isSelected = folder.CustomColor.HasValue &&
                            Vector3.Distance(folder.CustomColor.Value, color) < 0.1f;

                        if (ImGui.MenuItem(colorName, "", isSelected))
                        {
                            folder.CustomColor = color;
                            plugin.SaveConfiguration();
                        }
                    }

                    ImGui.Separator();

                    // Custom colour picker
                    ImGui.Text("Custom Colour:");
                    Vector3 tempColor = folder.CustomColor ?? GetAutoGeneratedColor(character, folder);

                    if (ImGui.ColorEdit3("##CustomFolderColour", ref tempColor, ImGuiColorEditFlags.NoInputs))
                    {
                        folder.CustomColor = tempColor;
                        plugin.SaveConfiguration();
                    }

                    ImGui.EndMenu();
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Delete Folder"))
                {
                    DeleteFolder(character, folder);
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
        }

        private void DrawFolderContents(Character character, DesignFolder folder, float scale)
        {
            float indentAmount = 15f * scale;

            // Apply search filter
            var foldersToShow = character.DesignFolders
                     .Where(f => f.ParentFolderId == folder.Id);
            var designsToShow = character.Designs
                     .Where(d => d.FolderId == folder.Id);
                     
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                foldersToShow = foldersToShow.Where(f => FolderContainsMatchingDesigns(character, f));
                designsToShow = designsToShow.Where(d => MatchesSearchQuery(d));
            }

            // Child folders
            foreach (var child in foldersToShow.OrderBy(f => f.SortOrder))
            {
                ImGui.Indent(indentAmount);
                bool childWasHovered = false;
                DrawFolderItem(character, child, ref childWasHovered, scale);
                ImGui.Unindent(indentAmount);
            }

            foreach (var design in designsToShow.OrderBy(d => d.SortOrder))
            {
                ImGui.Indent(indentAmount);
                DrawDesignRow(character, design, true, scale);
                ImGui.Unindent(indentAmount);
            }

            // Visual separation
            ImGui.Spacing();
            ImGui.Separator();
        }

        private void DrawDesignRow(Character character, CharacterDesign design, bool isInsideFolder, float scale)
        {
            ImGui.PushID(design.Name);

            var rowMin = ImGui.GetCursorScreenPos();
            float rowW = ImGui.GetContentRegionAvail().X;
            float rowH = 32f * scale;
            ImGui.Dummy(new Vector2(rowW, rowH));
            var rowMax = rowMin + new Vector2(rowW, rowH);

            bool hovered = ImGui.IsMouseHoveringRect(rowMin, rowMax, true);

            // Dark row background
            if (hovered)
            {
                var hoverColor = ImGui.GetColorU32(new Vector4(0.25f, 0.25f, 0.25f, 0.8f));
                ImGui.GetWindowDrawList().AddRectFilled(rowMin, rowMax, hoverColor, 4f * scale);
            }

            // Draw design row content with compact styling, america's next top model has nothing on me now!
            DrawDesignRowContent(character, design, rowMin, rowMax, rowH, hovered, rowW, scale);

            // Handle drag and drop
            HandleDesignDragDrop(character, design, rowMin, rowMax, hovered, scale);

            ImGui.PopID();
            ImGui.SetCursorScreenPos(new Vector2(rowMin.X, rowMin.Y + rowH));

            // Subtle separator
            if (!isInsideFolder)
            {
                var separatorColor = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 0.5f));
                ImGui.GetWindowDrawList().AddLine(
                    new Vector2(rowMin.X + (10 * scale), rowMax.Y),
                    new Vector2(rowMax.X - (10 * scale), rowMax.Y),
                    separatorColor, 1f * scale
                );
            }
        }

        private void DrawDesignRowContent(Character character, CharacterDesign design, Vector2 rowMin, Vector2 rowMax, float rowH, bool hovered, float rowW, float scale)
        {
            float pad = 8f * scale;
            float spacing = 4f * scale;
            float btnSize = 24f * scale;
            float x = rowMin.X + (2f * scale);

            // Drag handle
            if (hovered)
            {
                float handleWidth = 12f * scale;
                float handleHeight = rowH * 0.6f;
                float yOff = (rowH - handleHeight) / 2;

                ImGui.SetCursorScreenPos(new Vector2(x + pad, rowMin.Y + yOff));

                var handleColor = new Vector4(character.NameplateColor.X, character.NameplateColor.Y, character.NameplateColor.Z, 0.8f);

                ImGui.PushStyleColor(ImGuiCol.Button, handleColor);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, handleColor);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, handleColor);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2f * scale);

                ImGui.Button($"##handle_{design.Name}", new Vector2(handleWidth, handleHeight));

                ImGui.PopStyleVar();
                ImGui.PopStyleColor(3);

                // Enable drag and drop
                if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left) &&
                    ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceAllowNullId))
                {
                    draggedDesign = design;
                    ImGui.SetDragDropPayload("DESIGN_MOVE", ReadOnlySpan<byte>.Empty, ImGuiCond.None);

                    // Ghost image
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.1f, 0.1f, 0.1f, 0.9f));
                    ImGui.BeginGroup();
                    ImGui.Text("📄");
                    ImGui.SameLine();
                    ImGui.Text(design.Name);
                    ImGui.EndGroup();
                    ImGui.PopStyleColor(2);
                    ImGui.EndDragDropSource();
                }

                x += handleWidth + spacing;
            }

            // Favourite star/ghost
            ImGui.SetCursorScreenPos(new Vector2(x, rowMin.Y + (rowH - btnSize) / 2));
            
            // Check for seasonal themes
            var effectiveTheme = SeasonalThemeManager.IsSeasonalThemeEnabled(plugin.Configuration)
                ? SeasonalThemeManager.GetEffectiveTheme(plugin.Configuration)
                : SeasonalTheme.Default;

            string star;
            bool usesFontAwesome = false;

            if (effectiveTheme == SeasonalTheme.Halloween)
            {
                star = "\uf6e2"; // Ghost icon for Halloween
                usesFontAwesome = true;
            }
            else if (effectiveTheme == SeasonalTheme.Winter || effectiveTheme == SeasonalTheme.Christmas)
            {
                star = "\uf2dc"; // Snowflake icon for Winter/Christmas
                usesFontAwesome = true;
            }
            else if (effectiveTheme == SeasonalTheme.Valentines)
            {
                star = "\uf004"; // Heart icon for Valentine's
                usesFontAwesome = true;
            }
            else
            {
                star = design.IsFavorite ? "★" : "☆"; // Normal stars
                usesFontAwesome = false;
            }

            Vector4 starColor;
            if (effectiveTheme == SeasonalTheme.Halloween)
            {
                var themeColors = SeasonalThemeManager.GetCurrentThemeColors(plugin.Configuration);
                starColor = design.IsFavorite
                    ? new Vector4(themeColors.PrimaryAccent.X, themeColors.PrimaryAccent.Y, themeColors.PrimaryAccent.Z, hovered ? 1f : 0.7f) // Orange
                    : new Vector4(1.0f, 1.0f, 1.0f, hovered ? 0.8f : 0.6f); // White
            }
            else if (effectiveTheme == SeasonalTheme.Winter || effectiveTheme == SeasonalTheme.Christmas)
            {
                starColor = design.IsFavorite
                    ? new Vector4(1.0f, 1.0f, 1.0f, hovered ? 1f : 0.8f) // Pure white for favourited snowflake
                    : new Vector4(0.7f, 0.7f, 0.8f, hovered ? 0.8f : 0.5f); // Light grey for unfavourited
            }
            else if (effectiveTheme == SeasonalTheme.Valentines)
            {
                starColor = design.IsFavorite
                    ? new Vector4(1.0f, 1.0f, 1.0f, hovered ? 1f : 0.9f) // Solid white for favourited heart
                    : new Vector4(0.7f, 0.5f, 0.55f, hovered ? 0.7f : 0.4f); // Muted for unfavourited
            }
            else
            {
                starColor = design.IsFavorite
                    ? new Vector4(1f, 0.8f, 0.2f, hovered ? 1f : 0.7f) // Gold for normal favourites
                    : new Vector4(0.5f, 0.5f, 0.5f, hovered ? 0.8f : 0.4f); // Grey for normal unfavourited
            }

            // Ensure proper icon centering with explicit alignment
            bool scaleDownIcon = effectiveTheme == SeasonalTheme.Valentines; // Heart needs to be smaller
            if (scaleDownIcon)
            {
                ImGui.SetWindowFontScale(0.85f);
            }
            if (usesFontAwesome)
            {
                ImGui.PushFont(UiBuilder.IconFont);
            }

            ImGui.PushStyleColor(ImGuiCol.Text, starColor);
            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f)); // CENTER ICON

            bool buttonClicked = ImGui.Button($"{star}##{design.Name}", new Vector2(btnSize, btnSize));

            ImGui.PopStyleVar();
            ImGui.PopStyleColor();

            if (usesFontAwesome)
            {
                ImGui.PopFont();
            }
            if (scaleDownIcon)
            {
                ImGui.SetWindowFontScale(1.0f);
            }
            
            if (buttonClicked)
            {
                bool wasFavorite = design.IsFavorite;
                design.IsFavorite = !design.IsFavorite;

                // Trigger particle effect
                Vector2 effectPos = ImGui.GetItemRectMin() + ImGui.GetItemRectSize() / 2;
                string effectKey = $"{character.Name}_{design.Name}";
                if (!designFavoriteEffects.ContainsKey(effectKey))
                    designFavoriteEffects[effectKey] = new FavoriteSparkEffect();
                designFavoriteEffects[effectKey].Trigger(effectPos, design.IsFavorite, plugin.Configuration);

                plugin.SaveConfiguration();
                SortDesigns(character);
            }
            
            // Add tooltip for all favourite buttons
            if (ImGui.IsItemHovered())
            {
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(design.IsFavorite ? "Remove from favourites" : "Add to favourites");
            }

            x += btnSize + spacing;

            // Design name styling
            float rightZone = hovered ? (3 * btnSize + 2 * spacing + pad) : 0; // Only show buttons on hover
            float availW = rowW - (x - rowMin.X) - rightZone - pad;

            ImGui.SetCursorScreenPos(new Vector2(x, rowMin.Y + (rowH - ImGui.GetTextLineHeight()) / 2));

            var name = design.Name;
            if (ImGui.CalcTextSize(name).X > availW)
                name = TruncateWithEllipsis(name, availW);

            // Design name
            bool isActive = IsDesignCurrentlyActive(character, design);
            var textColor = isActive ? new Vector4(0.2f, 0.9f, 0.2f, 1f) : new Vector4(0.9f, 0.9f, 0.9f, 1f); // Green for active, light gray for inactive
            ImGui.PushStyleColor(ImGuiCol.Text, textColor);
            ImGui.TextUnformatted(name);
            ImGui.PopStyleColor();

            // Action buttons (only when hovered, compact)
            if (hovered)
            {
                DrawCompactDesignActionButtons(character, design, rowMin, rowW, rowH, btnSize, spacing, pad, scale);
            }
        }

        private void DrawCompactDesignActionButtons(Character character, CharacterDesign design, Vector2 rowMin, float rowW, float rowH, float btnSize, float spacing, float pad, float scale)
        {
            // Position buttons
            float startX = rowMin.X + rowW - (3 * btnSize + 2 * spacing + pad);
            float buttonY = rowMin.Y + (rowH - btnSize) / 2;

            // Dark button styling
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.25f, 0.25f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.35f, 0.35f, 0.35f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f * scale);

            // Apply button
            ImGui.SetCursorScreenPos(new Vector2(startX, buttonY));
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 0.8f, 0.3f, 1f)); // Green
            if (ImGui.Button("\uf00c", new Vector2(btnSize, btnSize)))
            {
                // Switch gearset if assigned (design overrides character)
                if (plugin.Configuration.EnableGearsetAssignments)
                {
                    var effectiveGearset = design.AssignedGearset ?? character.AssignedGearset;
                    if (effectiveGearset.HasValue)
                    {
                        plugin.SwitchToGearset(effectiveGearset.Value);
                    }
                }

                // Check if this is a Secret Mode (Conflict Resolution) design
                if (design.SecretModState != null && design.SecretModState.Any())
                {
                    // Ensure the correct Penumbra collection is assigned before CR modifies it
                    if (!string.IsNullOrWhiteSpace(character.PenumbraCollection))
                    {
                        plugin.EnsurePenumbraCollectionAssignment(character.PenumbraCollection);
                    }

                    // Apply mod state asynchronously first, then execute macro with proper threading
                    _ = Task.Run(async () =>
                    {
                        await plugin.ApplyDesignModState(character, design);
                        Plugin.Framework.RunOnFrameworkThread(() => {
                            plugin.ExecuteMacro(design.Macro, character, design.Name);
                            // Track last used design and character for auto-reapplication and UI feedback
                            plugin.Configuration.LastUsedDesignByCharacter[character.Name] = design.Name;
                            plugin.Configuration.LastUsedDesignCharacterKey = character.Name;
                            plugin.Configuration.LastUsedCharacterKey = character.Name;
                            
                            // Update player-specific character tracking for green highlighting
                            if (Plugin.ObjectTable.LocalPlayer is { } player && player.HomeWorld.IsValid)
                            {
                                string localName = player.Name.TextValue;
                                string worldName = player.HomeWorld.Value.Name.ToString();
                                string fullKey = $"{localName}@{worldName}";
                                string pluginCharacterKey = $"{character.Name}@{worldName}";
                                plugin.Configuration.LastUsedCharacterByPlayer[fullKey] = pluginCharacterKey;
                            }
                            
                            plugin.Configuration.Save();
                        });
                    });
                }
                else
                {
                    // Regular design - just execute the macro
                    plugin.ExecuteMacro(design.Macro, character, design.Name);
                    // Track last used design and character for auto-reapplication and UI feedback
                    plugin.Configuration.LastUsedDesignByCharacter[character.Name] = design.Name;
                    plugin.Configuration.LastUsedDesignCharacterKey = character.Name;
                    plugin.Configuration.LastUsedCharacterKey = character.Name;
                    
                    // Update player-specific character tracking for green highlighting
                    if (Plugin.ObjectTable.LocalPlayer is { } player && player.HomeWorld.IsValid)
                    {
                        string localName = player.Name.TextValue;
                        string worldName = player.HomeWorld.Value.Name.ToString();
                        string fullKey = $"{localName}@{worldName}";
                        string pluginCharacterKey = $"{character.Name}@{worldName}";
                        plugin.Configuration.LastUsedCharacterByPlayer[fullKey] = pluginCharacterKey;
                    }
                    
                    plugin.Configuration.Save();
                }

                plugin.AchievementTracker?.OnDesignApplied();
            }
            ImGui.PopStyleColor();
            ImGui.PopFont();

            if (ImGui.IsItemHovered())
            {
                if (!string.IsNullOrEmpty(design.PreviewImagePath) && File.Exists(design.PreviewImagePath))
                    DrawBoutiquePreviewTooltip(design, "Apply Design", scale);
                else
                    CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Apply Design");
            }

            // Edit button
            ImGui.SetCursorScreenPos(new Vector2(startX + btnSize + spacing, buttonY));
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 0.7f, 1f, 1f)); // Blue
            if (ImGui.Button("\uf044", new Vector2(btnSize, btnSize)))
            {
                bool isCtrlShift = ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift;
                
                // Open edit window first
                OpenEditDesignWindow(character, design);
                
                // Then convert to secret mode if Ctrl+Shift was held and Conflict Resolution is enabled
                if (isCtrlShift && plugin.Configuration.EnableConflictResolution)
                {
                    // Set secret mode flag
                    isSecretDesignMode = true;
                    
                    // Generate and set the appropriate macro in the edit fields
                    editedDesignMacro = (!plugin.Configuration.EnableConflictResolution && isSecretDesignMode) ? GenerateSecretDesignMacro(character) : GenerateDesignMacro(character);
                    if (isAdvancedModeDesign)
                    {
                        advancedDesignMacroText = editedDesignMacro;
                    }
                }
            }
            ImGui.PopStyleColor();
            ImGui.PopFont();

            if (ImGui.IsItemHovered()) CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Edit Design");

            // Delete button
            ImGui.SetCursorScreenPos(new Vector2(startX + 2 * (btnSize + spacing), buttonY));
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f)); // Red
            var io = ImGui.GetIO();
            if (ImGui.Button("\uf2ed", new Vector2(btnSize, btnSize)) && io.KeyCtrl && io.KeyShift)
            {
                character.Designs.Remove(design);
                plugin.SaveConfiguration();
            }
            ImGui.PopStyleColor();
            ImGui.PopFont();

            if (ImGui.IsItemHovered()) CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Hold Ctrl+Shift to delete");

            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
        }

        private void HandleDesignDragDrop(Character character, CharacterDesign design, Vector2 rowMin, Vector2 rowMax, bool hovered, float scale)
        {
            // Manual drop target
            if (draggedDesign != null && ImGui.IsMouseReleased(ImGuiMouseButton.Left) &&
                ImGui.IsMouseHoveringRect(rowMin, rowMax, true) && draggedDesign != design)
            {
                var list = character.Designs;
                list.Remove(draggedDesign);
                int idx = list.IndexOf(design);
                draggedDesign.FolderId = design.FolderId;
                list.Insert(idx, draggedDesign);
                draggedDesign = null;
                plugin.SaveConfiguration();
                plugin.RefreshTreeItems(character);
            }

            // Blue outline while dragging over
            if (draggedDesign != null && hovered)
            {
                var dl = ImGui.GetWindowDrawList();
                uint col = ImGui.GetColorU32(new Vector4(0.27f, 0.53f, 0.90f, 1f));
                dl.AddRect(rowMin, rowMax, col, 0, ImDrawFlags.None, 2 * scale);
            }
        }

        private void HandleDropToRoot(bool anyHeaderHovered, bool anyRowHovered, Character character)
        {
            if (draggedDesign != null && ImGui.IsMouseReleased(ImGuiMouseButton.Left) &&
                !anyHeaderHovered && !anyRowHovered)
            {
                draggedDesign.FolderId = null;
                plugin.SaveConfiguration();
                plugin.RefreshTreeItems(character);
                draggedDesign = null;
            }

            if (draggedFolder != null && ImGui.IsMouseReleased(ImGuiMouseButton.Left) &&
                !anyHeaderHovered && !anyRowHovered)
            {
                draggedFolder.ParentFolderId = null;
                plugin.SaveConfiguration();
                plugin.RefreshTreeItems(character);
                draggedFolder = null;
            }
        }

        private void DrawImportWindow(float scale)
        {
            if (!isImportWindowOpen || targetForDesignImport == null)
                return;

            float winW = 480f * scale;
            float winH = 540f * scale;
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(
                new Vector2(viewport.Pos.X + (viewport.Size.X - winW) * 0.5f,
                            viewport.Pos.Y + (viewport.Size.Y - winH) * 0.5f),
                ImGuiCond.Appearing);
            ImGui.SetNextWindowSize(new Vector2(winW, winH), ImGuiCond.Always);

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0));

            var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
                      | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar
                      | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings;

            if (ImGui.Begin("##ImportDesigns", ref isImportWindowOpen, flags))
            {
                DrawImportContent(scale);
            }
            ImGui.End();

            ImGui.PopStyleColor();
            ImGui.PopStyleVar(3);
        }

        private void DrawImportContent(float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            var winMin = ImGui.GetWindowPos();
            var winMax = winMin + ImGui.GetWindowSize();

            // Window background + outer border + faint gold inner glow
            uint bgTop = Boutique.U32(new Vector4(0x06 / 255f, 0x07 / 255f, 0x09 / 255f, 1f));
            uint bgBot = Boutique.U32(new Vector4(0x03 / 255f, 0x04 / 255f, 0x0A / 255f, 1f));
            dl.AddRectFilledMultiColor(winMin, winMax, bgTop, bgTop, bgBot, bgBot);
            dl.AddRect(winMin, winMax, Boutique.U32(Boutique.BorderSoft), 0f, ImDrawFlags.None, 1f * scale);
            dl.AddRect(winMin + new Vector2(1, 1), winMax - new Vector2(1, 1),
                Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.025f)),
                0f, ImDrawFlags.None, 1f * scale);

            // BL + BR corner brackets
            float bSize = 12f * scale;
            float bInset = 8f * scale;
            uint bCol = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.40f));
            var bl = new Vector2(winMin.X + bInset, winMax.Y - bInset);
            dl.AddLine(bl, new Vector2(bl.X, bl.Y - bSize), bCol, 1f * scale);
            dl.AddLine(bl, new Vector2(bl.X + bSize, bl.Y), bCol, 1f * scale);
            var br = new Vector2(winMax.X - bInset, winMax.Y - bInset);
            dl.AddLine(br, new Vector2(br.X, br.Y - bSize), bCol, 1f * scale);
            dl.AddLine(br, new Vector2(br.X - bSize, br.Y), bCol, 1f * scale);

            float ribbonH = 28f * scale;
            float headerH = 92f * scale;
            float footerH = 48f * scale;

            var ribbonMin = winMin;
            var ribbonMax = new Vector2(winMax.X, winMin.Y + ribbonH);
            var headerMin = new Vector2(winMin.X, ribbonMax.Y);
            var headerMax = new Vector2(winMax.X, ribbonMax.Y + headerH);
            var footerMin = new Vector2(winMin.X, winMax.Y - footerH);
            var bodyMin = new Vector2(winMin.X, headerMax.Y);
            var bodyMax = new Vector2(winMax.X, footerMin.Y);

            DrawImportRibbon(dl, ribbonMin, ribbonMax, scale);
            DrawImportHeader(dl, headerMin, headerMax, scale);
            DrawImportBody(dl, bodyMin, bodyMax, scale);
            DrawImportFooter(dl, footerMin, winMax, scale);
        }

        private void DrawImportRibbon(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
        {
            BoutiqueChassis.DrawRibbonBackground(dl, min, max, scale);
            float padX = 12f * scale;
            float midY = (min.Y + max.Y) * 0.5f;
            double t = ImGui.GetTime();

            // Pulsing gold pip
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
            dl.AddRectFilled(pipPos - new Vector2(pipR, pipR), pipPos + new Vector2(pipR, pipR),
                Boutique.U32(Boutique.Gold));

            // Title flush-left, beside the pip - matches main.html family
            // ribbon style (pip + meta text on the left, count on the right).
            using (Plugin.Instance?.OswaldMed10?.Push())
            {
                float trackPx = 2.6f * scale;
                string left = "IMPORT";
                string right = "DESIGNS";
                float lw = Boutique.MeasureTrackedText(left, trackPx);
                float gap = 10f * scale;
                float diaSize = 3.5f * scale;
                float fontH = ImGui.GetFontSize();
                float textY = midY - fontH * 0.5f;
                // Start the title 12 px past the pip
                float startX = pipPos.X + pipR + 12f * scale;
                Boutique.DrawTrackedText(dl, new Vector2(startX, textY), left, Boutique.U32(Boutique.TextDim), trackPx);
                var diaCentre = new Vector2(startX + lw + gap + diaSize, midY);
                dl.AddQuadFilled(
                    diaCentre + new Vector2(0, -diaSize),
                    diaCentre + new Vector2(diaSize, 0),
                    diaCentre + new Vector2(0, diaSize),
                    diaCentre + new Vector2(-diaSize, 0),
                    Boutique.U32(Boutique.GoldDeep));
                Boutique.DrawTrackedText(dl, new Vector2(startX + lw + gap + diaSize * 2f + gap, textY),
                    right, Boutique.U32(Boutique.Gold), trackPx);
            }

            // Right-side count tag
            var sources = plugin.Characters.Where(c => c != targetForDesignImport && c.Designs.Count > 0).ToList();
            using (Plugin.Instance?.OswaldMed10?.Push())
            {
                string countText = $"{sources.Count:00} SOURCES";
                float trackPx = 2.4f * scale;
                float w = Boutique.MeasureTrackedText(countText, trackPx);
                float h = ImGui.GetFontSize();
                float padInX = 7f * scale;
                float padInY = 2f * scale;
                var tagMax = new Vector2(max.X - padX, midY + h * 0.5f + padInY);
                var tagMin = new Vector2(tagMax.X - w - padInX * 2f, midY - h * 0.5f - padInY);
                dl.AddRect(tagMin, tagMax, Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.40f)),
                    0f, ImDrawFlags.None, 1f * scale);
                Boutique.DrawTrackedText(dl, new Vector2(tagMin.X + padInX, midY - h * 0.5f),
                    countText, Boutique.U32(Boutique.Gold), trackPx);
            }
        }

        private void DrawImportHeader(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
        {
            // Bottom hairline + gold fade accent
            dl.AddLine(new Vector2(min.X, max.Y - 1f * scale), new Vector2(max.X, max.Y - 1f * scale),
                Boutique.U32(Boutique.BorderSoft), 1f * scale);
            float aLeft = min.X + (max.X - min.X) * 0.25f;
            float aRight = min.X + (max.X - min.X) * 0.75f;
            var goldFade = Boutique.WithAlpha(Boutique.Gold, 0.50f);
            var goldClear = Boutique.WithAlpha(Boutique.Gold, 0f);
            dl.AddRectFilledMultiColor(
                new Vector2(aLeft, max.Y - 1f * scale), new Vector2((aLeft + aRight) * 0.5f, max.Y),
                Boutique.U32(goldClear), Boutique.U32(goldFade), Boutique.U32(goldFade), Boutique.U32(goldClear));
            dl.AddRectFilledMultiColor(
                new Vector2((aLeft + aRight) * 0.5f, max.Y - 1f * scale), new Vector2(aRight, max.Y),
                Boutique.U32(goldFade), Boutique.U32(goldClear), Boutique.U32(goldClear), Boutique.U32(goldFade));

            // Hero stack: measure both lines so we can vertically center them
            // as ONE block in the 92 px header with a tight 4 px gap so the
            // sub-line reads as a continuation of the title, not a separate row.
            float titleH, subH;
            using (Plugin.Instance?.OswaldSemiMidSmall?.Push())
                titleH = ImGui.GetFontSize();
            using (Plugin.Instance?.OswaldSemi14?.Push())
                subH = ImGui.GetFontSize();
            float stackGap = 4f * scale;
            float stackH = titleH + stackGap + subH;
            float headerMidY = (min.Y + max.Y) * 0.5f;
            float titleY = headerMidY - stackH * 0.5f;
            float subY = titleY + titleH + stackGap;

            // Title: IMPORT DESIGNS at SemiMidSmall (~20 px baked) reads as a
            // proper hero without crowding the 92 px header strip.
            using (Plugin.Instance?.OswaldSemiMidSmall?.Push())
            {
                float trackPx = 4.5f * scale;
                string title = "IMPORT DESIGNS";
                float w = Boutique.MeasureTrackedText(title, trackPx);
                Boutique.DrawTrackedText(dl,
                    new Vector2((min.X + max.X) * 0.5f - w * 0.5f, titleY),
                    title, Boutique.U32(Boutique.Text), trackPx);
            }

            // Sub-line: "INTO  {diamond}  {target name}" - diamond separator
            // matches the mockup, target name in their nameplate colour
            string targetName = !string.IsNullOrWhiteSpace(targetForDesignImport!.Alias)
                ? targetForDesignImport.Alias!
                : (targetForDesignImport.Name ?? "");
            var npV = targetForDesignImport.NameplateColor;
            using (Plugin.Instance?.OswaldSemi14?.Push())
            {
                float trackPx = 3.4f * scale;
                string into = "INTO";
                float intoW = Boutique.MeasureTrackedText(into, trackPx);
                float gap = 10f * scale;
                float diaSize = 3.5f * scale;
                float nameW = ImGui.CalcTextSize(targetName).X;
                float totalW = intoW + gap + diaSize * 2f + gap + nameW;
                float startX = (min.X + max.X) * 0.5f - totalW * 0.5f;
                float midSubY = subY + ImGui.GetFontSize() * 0.5f;
                Boutique.DrawTrackedText(dl, new Vector2(startX, subY), into,
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.55f)), trackPx);
                var diaCentre = new Vector2(startX + intoW + gap + diaSize, midSubY);
                dl.AddQuadFilled(
                    diaCentre + new Vector2(0, -diaSize),
                    diaCentre + new Vector2(diaSize, 0),
                    diaCentre + new Vector2(0, diaSize),
                    diaCentre + new Vector2(-diaSize, 0),
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.45f)));
                dl.AddText(new Vector2(startX + intoW + gap + diaSize * 2f + gap, subY),
                    Boutique.U32(new Vector4(npV.X, npV.Y, npV.Z, 1f)), targetName);
            }

            // Close X top-right. 28 x 28 chamfered slip - this is the only
            // close affordance on the modal (footer CLOSE button removed).
            float btnSize = 28f * scale;
            var btnMin = new Vector2(max.X - 12f * scale - btnSize, min.Y + 12f * scale);
            var btnMax = btnMin + new Vector2(btnSize, btnSize);
            ImGui.SetCursorScreenPos(btnMin);
            bool clicked = ImGui.InvisibleButton("##import_close", new Vector2(btnSize, btnSize));
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

            // Explicit  so source-encoding round-trips never lose the glyph.
            string xGlyph = "";
            ImGui.PushFont(UiBuilder.IconFont);
            var xSz = ImGui.CalcTextSize(xGlyph);
            ImGui.PopFont();
            uint xCol = hovered ? Boutique.U32(Boutique.Red) : Boutique.U32(Boutique.Text);
            dl.AddText(UiBuilder.IconFont, UiBuilder.IconFont.FontSize,
                new Vector2(btnMin.X + (btnSize - xSz.X) * 0.5f,
                            btnMin.Y + (btnSize - xSz.Y) * 0.5f),
                xCol, xGlyph);
            if (clicked) isImportWindowOpen = false;
        }

        private void DrawImportBody(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
        {
            ImGui.SetCursorScreenPos(min);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 4f * scale);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0, 0, 0, 0.20f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Boutique.WithAlpha(Boutique.GoldDeep, 1f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Boutique.WithAlpha(Boutique.Gold, 0.85f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Boutique.Gold);

            ImGui.BeginChild("##import_body", max - min, false,
                ImGuiWindowFlags.AlwaysVerticalScrollbar | ImGuiWindowFlags.NoBackground);
            ImGui.Dummy(new Vector2(0, 8f * scale));

            var sources = plugin.Characters
                .Where(c => c != targetForDesignImport && c.Designs.Count > 0)
                .OrderBy(c => c.Name)
                .ToList();

            foreach (var src in sources)
                DrawImportSourceSection(src, scale);

            ImGui.Dummy(new Vector2(0, 8f * scale));
            ImGui.EndChild();
            ImGui.PopStyleColor(5); // ChildBg + 4 scrollbar
            ImGui.PopStyleVar();
        }

        private void DrawImportSourceSection(Character src, float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            float headW = ImGui.GetContentRegionAvail().X - 16f * scale;
            float headH = 36f * scale;

            // Use the cursor's natural position - DON'T shift X each call (causes
            // diagonal layout when the offset compounds across rows). Inset is the
            // ImGui.Indent below + an internal item draw inset, applied INSIDE.
            var headMin = ImGui.GetCursorScreenPos();
            var headMax = headMin + new Vector2(headW, headH);

            bool headClicked = ImGui.InvisibleButton($"##import_src_{src.Name}", new Vector2(headW, headH));
            bool headHovered = ImGui.IsItemHovered();

            if (!_importExpandedSources.TryGetValue(src.Name, out bool expanded))
                expanded = false;
            if (headClicked)
            {
                expanded = !expanded;
                _importExpandedSources[src.Name] = expanded;
            }

            // Header bg: transparent at rest, Surface2 @ 85% on hover
            if (headHovered)
            {
                var s2 = Boutique.Surface2;
                dl.AddRectFilled(headMin, headMax, Boutique.U32(new Vector4(s2.X, s2.Y, s2.Z, 0.85f)));
            }
            // Left accent bar (3px). GoldDeep at rest, brightens to Gold with a
            // wider halo on hover or expand. The halo is one extra rect at low
            // alpha behind the bar, no blur (ImGui-translatable).
            bool barLit = headHovered || expanded;
            float barX = headMin.X;
            float barY0 = headMin.Y + 6f * scale;
            float barY1 = headMax.Y - 6f * scale;
            if (barLit)
            {
                uint halo = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.20f));
                dl.AddRectFilled(
                    new Vector2(barX - 1f * scale, barY0 - 2f * scale),
                    new Vector2(barX + 5f * scale, barY1 + 2f * scale), halo);
            }
            dl.AddRectFilled(
                new Vector2(barX, barY0), new Vector2(barX + 3f * scale, barY1),
                Boutique.U32(barLit ? Boutique.Gold : Boutique.GoldDeep));

            // Bottom hairline at BorderSoft @ 50% so each folder reads as a row
            dl.AddLine(new Vector2(headMin.X, headMax.Y), new Vector2(headMax.X, headMax.Y),
                Boutique.U32(Boutique.WithAlpha(Boutique.BorderSoft, 0.50f)), 1f * scale);

            // Folder icon (closed/open). fa-folder = , fa-folder-open = .
            string folderGlyph = expanded ? "" : "";
            ImGui.PushFont(UiBuilder.IconFont);
            var folderSz = ImGui.CalcTextSize(folderGlyph);
            ImGui.PopFont();
            float folderX = headMin.X + 14f * scale;
            float midY = (headMin.Y + headMax.Y) * 0.5f;
            uint iconCol = barLit ? Boutique.U32(Boutique.Gold) : Boutique.U32(Boutique.GoldDeep);
            dl.AddText(UiBuilder.IconFont, UiBuilder.IconFont.FontSize,
                new Vector2(folderX, midY - folderSz.Y * 0.5f),
                iconCol, folderGlyph);

            // Nameplate pip
            var npV = src.NameplateColor;
            float pipSize = 4f * scale;
            float pipX = folderX + folderSz.X + 12f * scale;
            dl.AddQuadFilled(
                new Vector2(pipX, midY - pipSize),
                new Vector2(pipX + pipSize, midY),
                new Vector2(pipX, midY + pipSize),
                new Vector2(pipX - pipSize, midY),
                Boutique.U32(new Vector4(npV.X, npV.Y, npV.Z, 1f)));

            // Source character name
            string srcName = !string.IsNullOrWhiteSpace(src.Alias) ? src.Alias : (src.Name ?? "");
            var srcSize = ImGui.CalcTextSize(srcName);
            dl.AddText(new Vector2(pipX + 12f * scale, midY - srcSize.Y * 0.5f),
                Boutique.U32(Boutique.Text), srcName);

            // Right-side count badge. GoldDeep at rest, brightens to Gold on
            // hover / when expanded so it reads as the same affordance as the
            // accent bar and folder icon.
            using (Plugin.Instance?.OswaldMed10?.Push())
            {
                string count = $"{src.Designs.Count:00}";
                float trackPx = 2.4f * scale;
                float w = Boutique.MeasureTrackedText(count, trackPx);
                float h = ImGui.GetFontSize();
                float padInX = 6f * scale;
                float padInY = 2f * scale;
                var tagMax = new Vector2(headMax.X - 10f * scale, midY + h * 0.5f + padInY);
                var tagMin = new Vector2(tagMax.X - w - padInX * 2f, midY - h * 0.5f - padInY);
                uint badgeBorder = barLit
                    ? Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.60f))
                    : Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.55f));
                uint badgeText = barLit ? Boutique.U32(Boutique.Gold) : Boutique.U32(Boutique.GoldDeep);
                dl.AddRect(tagMin, tagMax, badgeBorder, 0f, ImDrawFlags.None, 1f * scale);
                Boutique.DrawTrackedText(dl, new Vector2(tagMin.X + padInX, midY - h * 0.5f),
                    count, badgeText, trackPx);
            }

            // Expanded design list. Critical: do NOT use ImGui.Indent here. The
            // 20 px child indent must be applied INSIDE each design row's content
            // (text x), not by shifting the row container, so the design row's
            // hover bg + bottom hairline span the same width as a folder header.
            // This also defuses the "diagonal staircase" bug where stacking row
            // X offsets compounded across rows in the previous port.
            if (expanded)
            {
                foreach (var design in src.Designs)
                    DrawImportDesignRow(src, design, scale);
                ImGui.Dummy(new Vector2(0, 4f * scale));
            }
        }

        private readonly Dictionary<string, bool> _importExpandedSources = new();
        private readonly Dictionary<Guid, double> _importFlashTimes = new();
        private const float ImportFlashDuration = 1.6f;

        private void DrawImportDesignRow(Character src, CharacterDesign design, float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            // Same width formula as the folder header so containers line up.
            float rowW = ImGui.GetContentRegionAvail().X - 16f * scale;
            float rowH = 30f * scale;

            // 20 px child indent applied INSIDE the row, not by shifting the
            // container. Hover bg + hairline still span the full body width.
            float contentInsetX = 20f * scale;

            var rowMin = ImGui.GetCursorScreenPos();
            var rowMax = rowMin + new Vector2(rowW, rowH);

            // The whole row is hoverable (visual highlight) but does NOT trigger
            // import - import is only triggered by the explicit + button on the
            // right. This avoids accidental imports from a click-anywhere-on-row.
            bool rowHovered = ImGui.IsMouseHoveringRect(rowMin, rowMax);

            // Just-imported flash. Falls off over ImportFlashDuration seconds.
            float flashAlpha = 0f;
            if (_importFlashTimes.TryGetValue(design.Id, out var startT))
            {
                float elapsed = (float)(ImGui.GetTime() - startT);
                if (elapsed >= ImportFlashDuration)
                {
                    _importFlashTimes.Remove(design.Id);
                }
                else
                {
                    float t = elapsed / ImportFlashDuration;
                    flashAlpha = Math.Max(0f, 0.32f * (1f - t));
                }
            }

            // Bg layers: hover first, then flash overlay (so the flash reads on
            // top while the user keeps the cursor on the row).
            if (rowHovered)
            {
                var s2 = Boutique.Surface2;
                dl.AddRectFilled(rowMin, rowMax, Boutique.U32(new Vector4(s2.X, s2.Y, s2.Z, 0.55f)));
            }
            if (flashAlpha > 0f)
            {
                dl.AddRectFilled(rowMin, rowMax, Boutique.U32(Boutique.WithAlpha(Boutique.Gold, flashAlpha)));
            }
            // Bottom hairline at BorderSoft @ 30% - full-width like the mockup
            dl.AddLine(new Vector2(rowMin.X, rowMax.Y), new Vector2(rowMax.X, rowMax.Y),
                Boutique.U32(Boutique.WithAlpha(Boutique.BorderSoft, 0.30f)), 1f * scale);

            // Right-side + button. 22x22, the only thing that triggers import.
            float btnSize = 22f * scale;
            float btnPad = 6f * scale;
            float midY = (rowMin.Y + rowMax.Y) * 0.5f;
            var btnMin = new Vector2(rowMax.X - btnPad - btnSize, midY - btnSize * 0.5f);
            var btnMax = btnMin + new Vector2(btnSize, btnSize);

            ImGui.SetCursorScreenPos(btnMin);
            bool btnClicked = ImGui.InvisibleButton($"##import_btn_{design.Id}", new Vector2(btnSize, btnSize));
            bool btnHovered = ImGui.IsItemHovered();
            if (btnHovered) CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip($"Import '{design.Name}'");

            uint btnBg = btnHovered
                ? Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.20f))
                : Boutique.U32(new Vector4(0.078f, 0.086f, 0.118f, 0.85f));
            uint btnBorder = btnHovered
                ? Boutique.U32(Boutique.Gold)
                : Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.85f));
            dl.AddRectFilled(btnMin, btnMax, btnBg);
            dl.AddRect(btnMin, btnMax, btnBorder, 0f, ImDrawFlags.None, 1f * scale);

            string plusGlyph = "";
            ImGui.PushFont(UiBuilder.IconFont);
            var plusSz = ImGui.CalcTextSize(plusGlyph);
            ImGui.PopFont();
            uint plusCol = btnHovered ? Boutique.U32(Boutique.GoldBright) : Boutique.U32(Boutique.Gold);
            dl.AddText(UiBuilder.IconFont, UiBuilder.IconFont.FontSize,
                new Vector2(btnMin.X + (btnSize - plusSz.X) * 0.5f,
                            btnMin.Y + (btnSize - plusSz.Y) * 0.5f),
                plusCol, plusGlyph);

            if (btnClicked)
            {
                var json = JsonConvert.SerializeObject(design);
                var clone = JsonConvert.DeserializeObject<CharacterDesign>(json);
                clone!.Name = design.Name + " (Copy)";
                clone.Id = Guid.NewGuid();
                clone.DateAdded = DateTime.UtcNow;
                clone.FolderId = null;
                targetForDesignImport!.Designs.Add(clone);
                plugin.SaveConfiguration();
                plugin.AchievementTracker?.OnDesignImported();
                _importFlashTimes[design.Id] = ImGui.GetTime();
            }

            // Design name, indented INSIDE the row (not by container shift)
            string name = design.Name ?? "";
            var nameSize = ImGui.CalcTextSize(name);
            float nameX = rowMin.X + contentInsetX;
            float nameMaxX = btnMin.X - 8f * scale;
            dl.PushClipRect(new Vector2(nameX, rowMin.Y), new Vector2(nameMaxX, rowMax.Y), true);
            uint nameCol = flashAlpha > 0.10f
                ? Boutique.U32(Boutique.GoldBright)
                : Boutique.U32(rowHovered ? Boutique.Text : Boutique.TextDim);
            dl.AddText(new Vector2(nameX, midY - nameSize.Y * 0.5f), nameCol, name);
            dl.PopClipRect();

            // Advance cursor past the row (the InvisibleButton above set it to btnMin)
            ImGui.SetCursorScreenPos(new Vector2(rowMin.X, rowMax.Y));
            ImGui.Dummy(new Vector2(0, 1f * scale));
        }

        private void DrawImportFooter(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
        {
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

            dl.AddRectFilled(min + new Vector2(0, 1f * scale), max,
                Boutique.U32(new Vector4(0.04f, 0.05f, 0.06f, 0.55f)));

            // Centered hint: "CLICK [+] TO IMPORT". The header X is the only
            // close affordance; no redundant CLOSE button in the footer.
            float midY = (min.Y + max.Y) * 0.5f;

            using (Plugin.Instance?.OswaldMed10?.Push())
            {
                float trackPx = 2.6f * scale;
                string leftWord = "CLICK";
                string rightWord = "TO IMPORT";
                float lw = Boutique.MeasureTrackedText(leftWord, trackPx);
                float rw = Boutique.MeasureTrackedText(rightWord, trackPx);
                float h = ImGui.GetFontSize();
                float gap = 8f * scale;
                float plusBox = 14f * scale;
                uint faint = Boutique.U32(Boutique.TextFaint);

                float totalW = lw + gap + plusBox + gap + rw;
                float x = (min.X + max.X) * 0.5f - totalW * 0.5f;

                Boutique.DrawTrackedText(dl, new Vector2(x, midY - h * 0.5f), leftWord, faint, trackPx);
                x += lw + gap;

                var pMin = new Vector2(x, midY - plusBox * 0.5f);
                var pMax = pMin + new Vector2(plusBox, plusBox);
                dl.AddRect(pMin, pMax, Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.45f)),
                    0f, ImDrawFlags.None, 1f * scale);
                ImGui.PushFont(UiBuilder.IconFont);
                var pSz = ImGui.CalcTextSize("");
                ImGui.PopFont();
                dl.AddText(UiBuilder.IconFont, UiBuilder.IconFont.FontSize * 0.75f,
                    new Vector2(pMin.X + (plusBox - pSz.X * 0.75f) * 0.5f,
                                pMin.Y + (plusBox - pSz.Y * 0.75f) * 0.5f),
                    Boutique.U32(Boutique.Gold), "");
                x += plusBox + gap;
                Boutique.DrawTrackedText(dl, new Vector2(x, midY - h * 0.5f), rightWord, faint, trackPx);
            }
        }

        private void DrawAdvancedModeWindow(float scale)
        {
            if (!isAdvancedModeWindowOpen)
                return;

            if (string.IsNullOrEmpty(originalAdvancedMacroText))
                originalAdvancedMacroText = advancedDesignMacroText;

            var windowSize = new Vector2(720 * scale, 580 * scale);
            ImGui.SetNextWindowSize(windowSize, ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(560 * scale, 420 * scale), new Vector2(1400, 1200));

            // Push boutique window style, velvet bg, no rounding. Window
            // border left at Dalamud's default so the popout matches the rest
            // of the plugin's windows (and other plugins') outline style.
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0f, 0f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, Boutique.Velvet);

            bool open = ImGui.Begin("##bAdvMacro", ref isAdvancedModeWindowOpen,
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar);
            if (open)
            {
                var dl = ImGui.GetWindowDrawList();
                var winPos = ImGui.GetWindowPos();
                var winSize = ImGui.GetWindowSize();
                var winMax = winPos + winSize;

                // Velvet ground gradient
                uint vTop = Boutique.U32(Boutique.Velvet);
                uint vBot = Boutique.U32(new Vector4(0x03 / 255f, 0x04 / 255f, 0x0A / 255f, 1f));
                dl.AddRectFilledMultiColor(winPos, winMax, vTop, vTop, vBot, vBot);

                float headH = 40f * scale;
                float footerH = 56f * scale;
                var headMin = winPos;
                var headMax = new Vector2(winMax.X, winPos.Y + headH);
                var bodyMin = new Vector2(winPos.X, headMax.Y);
                var bodyMax = new Vector2(winMax.X, winMax.Y - footerH);
                var footMin = new Vector2(winPos.X, bodyMax.Y);

                // Header, kicker + design name
                string designName = !string.IsNullOrEmpty(originalDesignName)
                    ? originalDesignName.ToUpperInvariant()
                    : "(NEW DESIGN)";
                using (Plugin.Instance?.OswaldSemi11?.Push())
                {
                    ImFontPtr labelF = ImGui.GetFont();
                    using (Plugin.Instance?.OswaldSemi13?.Push())
                    {
                        ImFontPtr titleF = ImGui.GetFont();
                        if (Boutique.DrawFormHeader(dl, headMin, headMax, scale,
                            "ADVANCED MACRO", designName, null, labelF, titleF, UiBuilder.IconFont, ""))
                        {
                            // X close = cancel (revert macro changes, close window).
                            // Does NOT uncheck the advanced-mode toggle, closing
                            // the editor no longer flips advanced mode off; the
                            // form's checkbox owns that state independently.
                            advancedDesignMacroText = originalAdvancedMacroText;
                            originalAdvancedMacroText = "";
                            isAdvancedModeWindowOpen = false;
                        }
                    }
                }

                // Body. Push OutfitMed13 (16.9px) so the macro editor toolbar
                // and any inline text render at a readable size. Without this
                // push the body falls back to whatever the calling site had.
                // No section head, the user said the surface was doing too
                // much. Macro editor takes the body directly under the header.
                ImGui.SetCursorScreenPos(bodyMin + new Vector2(16f * scale, 14f * scale));
                Boutique.PushFormStyle();
                var advBodyScope = Plugin.Instance?.OutfitMed13?.Push();

                ImFontPtr smallF;
                using (Plugin.Instance?.OswaldSemi11?.Push()) { smallF = ImGui.GetFont(); }

                float editorPxH = (bodyMax.Y - ImGui.GetCursorScreenPos().Y - 14f * scale) / Boutique.FormScale;
                Boutique.DrawMacroEditor(ref advancedDesignMacroText, "bAdvMacroText", scale,
                    regenerate: () =>
                    {
                        // Best-effort re-generation: if we have an active character + design,
                        // regenerate from its fields; otherwise leave the buffer untouched.
                        if (activeCharacterIndex >= 0 && activeCharacterIndex < plugin.Characters.Count)
                        {
                            var ch = plugin.Characters[activeCharacterIndex];
                            return GenerateDesignMacro(ch);
                        }
                        return advancedDesignMacroText ?? string.Empty;
                    },
                    paste: () =>
                    {
                        try
                        {
                            string clip = "";
                            var t = new Thread(() => { try { clip = Clipboard.GetText() ?? ""; } catch { } });
                            t.SetApartmentState(ApartmentState.STA);
                            t.Start();
                            t.Join();
                            if (!string.IsNullOrEmpty(clip)) advancedDesignMacroText = clip;
                        }
                        catch (Exception ex) { Plugin.Log.Warning($"Paste macro failed: {ex.Message}"); }
                    },
                    smallFont: smallF,
                    editorH: editorPxH);

                advBodyScope?.Dispose();
                Boutique.PopFormStyle();

                // Footer
                uint footBg = Boutique.U32(Boutique.Surface1);
                dl.AddRectFilled(footMin, winMax, footBg);
                dl.AddLine(new Vector2(footMin.X, footMin.Y),
                           new Vector2(winMax.X, footMin.Y),
                           Boutique.U32(Boutique.BorderSoft), 1f * scale);

                float footPadX = 14f * scale;
                float footMidY = (footMin.Y + winMax.Y) * 0.5f;
                float btnH = 30f * scale;
                float cancelW = 84f * scale;
                float saveW = 140f * scale;

                // Default ImGui font (no Oswald push) so the gold pill matches
                // the main window's "ADD CHARACTER" button across the plugin.
                var saveMin = new Vector2(winMax.X - footPadX - saveW, footMidY - btnH * 0.5f);
                var saveMax = saveMin + new Vector2(saveW, btnH);
                if (Boutique.DrawSavePill(dl, saveMin, saveMax,
                    "SAVE MACRO", 1.8f * scale, scale, "advmacro",
                    false, uiStyles.UpdateAndGetHoverSweepProgress))
                {
                    if (activeCharacterIndex >= 0 && activeCharacterIndex < plugin.Characters.Count && !isNewDesign)
                    {
                        var character = plugin.Characters[activeCharacterIndex];
                        var existingDesign = character.Designs.FirstOrDefault(d => d.Name == originalDesignName);
                        if (existingDesign != null)
                        {
                            existingDesign.AdvancedMacro = advancedDesignMacroText;
                            existingDesign.IsAdvancedMode = true;
                            plugin.Configuration.Save();
                        }
                    }
                    originalAdvancedMacroText = "";
                    isAdvancedModeWindowOpen = false;
                }

                var cancelMin = new Vector2(saveMin.X - footPadX - cancelW, footMidY - btnH * 0.5f);
                var cancelMax = cancelMin + new Vector2(cancelW, btnH);
                if (Boutique.DrawCancelBtn(dl, cancelMin, cancelMax,
                    "CANCEL", 1.6f * scale, scale, "advmacro", ImGui.GetFont()))
                {
                    advancedDesignMacroText = originalAdvancedMacroText;
                    originalAdvancedMacroText = "";
                    isAdvancedModeWindowOpen = false;
                }
            }
            ImGui.End();

            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);
        }

        // Utility methods
        private void SelectPreviewImage()
        {
            plugin.OpenFilePicker(
                "Select Design Preview Image",
                "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|PNG files (*.png)|*.png",
                (selectedPath) =>
                {
                    lock (this)
                    {
                        pendingDesignImagePath = selectedPath;
                    }
                }
            );
        }

        private void PasteImageFromClipboard()
        {
            try
            {
                Thread thread = new Thread(() =>
                {
                    try
                    {
                        // Check if clipboard contains image data
                        if (!Clipboard.ContainsImage())
                        {
                            Plugin.Log.Warning("No image found in clipboard");
                            return;
                        }

                        // Get image from clipboard
                        using (var clipboardImage = Clipboard.GetImage())
                        {
                            if (clipboardImage == null)
                            {
                                Plugin.Log.Warning("Failed to get image from clipboard");
                                return;
                            }

                            // Create directory if it doesn't exist
                            string configDir = plugin.PluginPath;
                            string imagesDir = Path.Combine(configDir, "Images");
                            string previewsDir = Path.Combine(imagesDir, "DesignPreviews");
                            
                            Directory.CreateDirectory(previewsDir);

                            // Generate unique filename with timestamp
                            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                            string fileName = $"design_preview_{timestamp}.png";
                            string fullPath = Path.Combine(previewsDir, fileName);

                            // Save image as PNG
                            clipboardImage.Save(fullPath, ImageFormat.Png);

                            // Set the path for UI update
                            lock (this)
                            {
                                pendingPastedImagePath = fullPath;
                            }

                            Plugin.Log.Info($"Pasted image saved to: {fullPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error($"Error pasting image from clipboard: {ex.Message}");
                    }
                });

                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Critical clipboard paste error: {ex.Message}");
            }
        }

        private bool IsClipboardImageAvailable()
        {
            try
            {
                return Clipboard.ContainsImage();
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void CleanupOrphanedPreviewImages(Plugin plugin)
        {
            try
            {
                string configDir = plugin.PluginPath;
                string previewsDir = Path.Combine(configDir, "Images", "DesignPreviews");
                
                if (!Directory.Exists(previewsDir))
                    return;

                // Get all images in the previews directory
                var imageFiles = Directory.GetFiles(previewsDir, "*.png")
                    .Concat(Directory.GetFiles(previewsDir, "*.jpg"))
                    .Concat(Directory.GetFiles(previewsDir, "*.jpeg"))
                    .ToList();

                if (!imageFiles.Any())
                    return;

                // Collect all preview image paths currently in use
                var referencedImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                foreach (var character in plugin.Characters)
                {
                    foreach (var design in character.Designs)
                    {
                        if (!string.IsNullOrEmpty(design.PreviewImagePath) && 
                            File.Exists(design.PreviewImagePath))
                        {
                            referencedImages.Add(Path.GetFullPath(design.PreviewImagePath));
                        }
                    }
                }

                // Delete orphaned images
                int deletedCount = 0;
                foreach (var imageFile in imageFiles)
                {
                    string fullImagePath = Path.GetFullPath(imageFile);
                    
                    if (!referencedImages.Contains(fullImagePath))
                    {
                        try
                        {
                            File.Delete(imageFile);
                            deletedCount++;
                            Plugin.Log.Info($"Deleted orphaned preview image: {Path.GetFileName(imageFile)}");
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.Warning($"Failed to delete orphaned image {imageFile}: {ex.Message}");
                        }
                    }
                }

                if (deletedCount > 0)
                {
                    Plugin.Log.Info($"Cleanup completed: {deletedCount} orphaned preview images deleted");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error during preview image cleanup: {ex.Message}");
            }
        }

        private (float width, float height) CalculateImageDimensions(IDalamudTextureWrap texture, float maxSize)
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

        private void AddNewDesign()
        {
            isNewDesign = true;
            isEditDesignWindowOpen = true;
            plugin.IsEditDesignWindowOpen = true;
            _dpFormScrollResetFramesPending = 3;
            editedDesignName = "";
            editedGlamourerDesign = "";
            editedDesignMacro = "";
            isAdvancedModeDesign = false;
            editedAutomation = "";
            editedCustomizeProfile = "";
            editedGearset = null;
            editedDesignPreviewPath = "";
            plugin.EditedDesignName = editedDesignName;
            plugin.EditedGlamourerDesign = editedGlamourerDesign;
        }

        private void OpenEditDesignWindow(Character character, CharacterDesign design)
        {
            isNewDesign = false;
            isEditDesignWindowOpen = true;
            plugin.IsEditDesignWindowOpen = true;
            _dpFormScrollResetFramesPending = 3;
            originalDesignName = design.Name;
            editedDesignName = design.Name;
            editedDesignMacro = design.IsAdvancedMode ? design.AdvancedMacro ?? "" : design.Macro ?? "";
            editedGlamourerDesign = !string.IsNullOrWhiteSpace(design.GlamourerDesign)
                ? design.GlamourerDesign
                : ExtractGlamourerDesignFromMacro(design.Macro ?? "");

            editedAutomation = design.Automation ?? "";
            editedCustomizeProfile = design.CustomizePlusProfile ?? "";
            editedGearset = design.AssignedGearset;
            editedDesignPreviewPath = design.PreviewImagePath ?? "";
            isAdvancedModeDesign = design.IsAdvancedMode;
            isAdvancedModeWindowOpen = design.IsAdvancedMode;
            advancedDesignMacroText = design.AdvancedMacro ?? "";
            
            // Check if this is a Secret Mode (Conflict Resolution) design
            if ((design.SecretModState != null && design.SecretModState.Any()) ||
                (design.ModOptionSettings != null && design.ModOptionSettings.Any()) ||
                (design.SecretModPinOverrides != null && design.SecretModPinOverrides.Any()))
            {
                isSecretDesignMode = true;
                // Load the existing mod state into temporary storage for editing
                if (design.SecretModState != null)
                {
                    temporaryDesignSecretModState = new Dictionary<string, bool>(design.SecretModState);
                }
                if (design.SecretModPinOverrides != null)
                {
                    temporaryDesignSecretModPinOverrides = new HashSet<string>(design.SecretModPinOverrides);
                }
            }
        }

        private void CloseDesignEditor()
        {
            isEditDesignWindowOpen = false;
            plugin.IsEditDesignWindowOpen = false;
            isAdvancedModeWindowOpen = false;
            isNewDesign = false;
            isSecretDesignMode = false;
            
            // Close Mod Manager window if it's open
            if (plugin.SecretModeModWindow?.IsOpen ?? false)
            {
                plugin.SecretModeModWindow.IsOpen = false;
            }
            
            ResetEditFields();
        }

        private void ResetEditFields()
        {
            editedDesignName = "";
            editedDesignMacro = "";
            editedGlamourerDesign = "";
            editedAutomation = "";
            editedCustomizeProfile = "";
            editedDesignPreviewPath = "";
            advancedDesignMacroText = "";
            originalDesignName = "";
            temporaryDesignSecretModState = null;
            temporaryDesignSecretModPinOverrides = null;
        }

        private void SaveDesign(Character character)
        {
            if (string.IsNullOrWhiteSpace(editedDesignName) || string.IsNullOrWhiteSpace(editedGlamourerDesign))
                return;

            var existingDesign = !isNewDesign
                ? character.Designs.FirstOrDefault(d => d.Name == originalDesignName)
                : null;

            if (existingDesign != null)
            {
                // Update existing design
                existingDesign.Name = editedDesignName;
                bool wasPreviouslyAdvanced = existingDesign.IsAdvancedMode;
                bool keepAdvanced = wasPreviouslyAdvanced && !isAdvancedModeDesign;

                // For advanced mode with empty macro, generate from form fields
                string advancedMacroToUse = advancedDesignMacroText;
                if ((isAdvancedModeDesign || keepAdvanced) && string.IsNullOrWhiteSpace(advancedMacroToUse))
                {
                    advancedMacroToUse = GenerateDesignMacro(character);
                }

                existingDesign.Macro = keepAdvanced
                    ? advancedMacroToUse
                    : (isAdvancedModeDesign ? advancedMacroToUse : GenerateDesignMacro(character));

                existingDesign.AdvancedMacro = isAdvancedModeDesign || keepAdvanced
                    ? advancedMacroToUse
                    : "";

                existingDesign.IsAdvancedMode = isAdvancedModeDesign || keepAdvanced;
                existingDesign.Automation = editedAutomation;
                existingDesign.GlamourerDesign = editedGlamourerDesign;
                existingDesign.CustomizePlusProfile = editedCustomizeProfile;
                existingDesign.AssignedGearset = editedGearset;
                existingDesign.PreviewImagePath = editedDesignPreviewPath;
                if (!string.IsNullOrWhiteSpace(editedDesignPreviewPath))
                    plugin.AchievementTracker?.OnDesignPreviewSet();
                // Per-design Customize+ profile achievement
                if (!string.IsNullOrWhiteSpace(editedCustomizeProfile))
                    plugin.AchievementTracker?.OnPerDesignCustomizePlusSet();
                // Per-design mod option overrides achievement (Tinkerer)
                if (existingDesign.ModOptionSettings?.Count > 0)
                    plugin.AchievementTracker?.OnPerDesignModOptionsSet();

                // Apply any Secret Mode state that was configured during editing
                if (temporaryDesignSecretModState != null)
                {
                    existingDesign.SecretModState = temporaryDesignSecretModState;
                }
                if (temporaryDesignSecretModPinOverrides != null)
                {
                    existingDesign.SecretModPinOverrides = temporaryDesignSecretModPinOverrides;
                }
            }
            else
            {
                // Add new design - generate macro from fields if advanced mode has empty macro
                string macroForNewDesign = isAdvancedModeDesign
                    ? (string.IsNullOrWhiteSpace(advancedDesignMacroText) ? GenerateDesignMacro(character) : advancedDesignMacroText)
                    : GenerateDesignMacro(character);

                var newDesign = new CharacterDesign(
                    editedDesignName,
                    macroForNewDesign,
                    isAdvancedModeDesign,
                    isAdvancedModeDesign ? macroForNewDesign : "",
                    editedGlamourerDesign,
                    editedAutomation,
                    editedCustomizeProfile,
                    editedDesignPreviewPath
                )
                {
                    DateAdded = DateTime.UtcNow,
                    AssignedGearset = editedGearset
                };

                // Apply any Secret Mode state that was configured during editing
                if (temporaryDesignSecretModState != null)
                {
                    newDesign.SecretModState = temporaryDesignSecretModState;
                }
                if (temporaryDesignSecretModPinOverrides != null)
                {
                    newDesign.SecretModPinOverrides = temporaryDesignSecretModPinOverrides;
                }

                character.Designs.Add(newDesign);
            }

            plugin.AchievementTracker?.OnDesignCreated();
            plugin.SaveConfiguration();
        }

        private void DeleteFolder(Character character, DesignFolder folder)
        {
            foreach (var d in character.Designs.Where(d => d.FolderId == folder.Id))
                d.FolderId = null;

            foreach (var sub in character.DesignFolders.Where(f => f.ParentFolderId == folder.Id))
                sub.ParentFolderId = null;

            character.DesignFolders.RemoveAll(f => f.Id == folder.Id);

            plugin.SaveConfiguration();
            plugin.RefreshTreeItems(character);
        }

        private DesignSortType GetDesignSortFromConfig()
        {
            return plugin.Configuration.CurrentDesignSortIndex switch
            {
                0 => DesignSortType.Favorites,
                1 => DesignSortType.Alphabetical,
                2 => DesignSortType.Recent,
                3 => DesignSortType.Oldest,
                4 => DesignSortType.Manual,
                _ => DesignSortType.Alphabetical // Default fallback
            };
        }
        
        private void SetDesignSort(int sortIndex)
        {
            plugin.Configuration.CurrentDesignSortIndex = sortIndex;
            plugin.Configuration.Save();
        }

        private void SortDesigns(Character character)
        {
            var sortType = currentDesignSort;
            if (sortType == DesignSortType.Manual)
                return;

            // Sort all designs - both root level and within folders
            SortDesignList(character.Designs, sortType);
        }
        
        private void SortDesignList(List<CharacterDesign> designs, DesignSortType sortType)
        {
            if (sortType == DesignSortType.Favorites)
            {
                designs.Sort((a, b) =>
                {
                    int favCompare = b.IsFavorite.CompareTo(a.IsFavorite);
                    if (favCompare != 0) return favCompare;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            }
            else if (sortType == DesignSortType.Alphabetical)
            {
                designs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            else if (sortType == DesignSortType.Recent)
            {
                designs.Sort((a, b) => b.DateAdded.CompareTo(a.DateAdded));
            }
            else if (sortType == DesignSortType.Oldest)
            {
                designs.Sort((a, b) => a.DateAdded.CompareTo(b.DateAdded));
            }
        }

        private Vector4 GetFolderColor(Character character, DesignFolder folder)
        {
            Vector3 baseColor;

            if (folder.CustomColor.HasValue)
            {
                baseColor = folder.CustomColor.Value;
            }
            else
            {
                baseColor = GetAutoGeneratedColor(character, folder);
            }

            return new Vector4(baseColor.X, baseColor.Y, baseColor.Z, 0.6f);
        }

        private Vector3 GetAutoGeneratedColor(Character character, DesignFolder folder)
        {
            return character.NameplateColor;
        }

        private List<(string name, bool isFolder, object item, DateTime dateAdded, int manual)> BuildRenderItems(Character character)
        {
            var renderItems = new List<(string name, bool isFolder, object item, DateTime dateAdded, int manual)>();

            // Apply search filtering if active
            var designsToShow = character.Designs.AsEnumerable();
            var foldersToShow = character.DesignFolders.AsEnumerable();
            
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                designsToShow = designsToShow.Where(d => MatchesSearchQuery(d));
                foldersToShow = foldersToShow.Where(f => FolderContainsMatchingDesigns(character, f));
            }

            foreach (var f in foldersToShow.Where(f => f.ParentFolderId == null))
            {
                renderItems.Add((f.Name, true, f as object, DateTime.MinValue, f.SortOrder));
            }

            foreach (var d in designsToShow.Where(d => d.FolderId == null))
            {
                renderItems.Add((d.Name, false, d as object, d.DateAdded, d.SortOrder));
            }

            switch (currentDesignSort)
            {
                case DesignSortType.Favorites:
                    renderItems = renderItems
                        .OrderByDescending(x => x.isFolder ? false : GetSortFavValue((CharacterDesign)x.item))
                        .ThenBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    break;
                case DesignSortType.Alphabetical:
                    renderItems = renderItems
                        .OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    break;
                case DesignSortType.Recent:
                    renderItems = renderItems
                        .OrderByDescending(x => x.dateAdded)
                        .ToList();
                    break;
                case DesignSortType.Oldest:
                    renderItems = renderItems
                        .OrderBy(x => x.dateAdded)
                        .ToList();
                    break;
                case DesignSortType.Manual:
                    renderItems = renderItems
                        .OrderBy(x => x.manual)
                        .ToList();
                    break;
            }

            return renderItems;
        }

        private string GenerateDesignMacro(Character character)
        {
            if (string.IsNullOrWhiteSpace(editedGlamourerDesign))
                return "";

            string macro = $"/glamour apply {editedGlamourerDesign} | self";

            // Conditionally include automation line
            if (plugin.Configuration.EnableAutomations)
            {
                string automationToUse = !string.IsNullOrWhiteSpace(editedAutomation)
                    ? editedAutomation
                    : (!string.IsNullOrWhiteSpace(character.CharacterAutomation)
                        ? character.CharacterAutomation
                        : "None");

                macro += $"\n/glamour automation enable {automationToUse}";
            }

            // Always disable Customize+ first
            macro += "\n/customize profile disable <me>";

            // Determine Customize+ profile
            string customizeProfileToUse = !string.IsNullOrWhiteSpace(editedCustomizeProfile)
                ? editedCustomizeProfile
                : !string.IsNullOrWhiteSpace(character.CustomizeProfile)
                    ? character.CustomizeProfile
                    : string.Empty;

            // Enable only if needed
            if (!string.IsNullOrWhiteSpace(customizeProfileToUse))
                macro += $"\n/customize profile enable <me>, {customizeProfileToUse}";

            // Redraw line
            macro += "\n/penumbra redraw self";

            return macro;
        }

        private string GenerateSecretDesignMacro(Character character)
        {
            // Which Penumbra collection to target (taken from the character)
            var collection = character.PenumbraCollection;

            // What the form is currently set to
            var design = editedGlamourerDesign;
            var custom = !string.IsNullOrWhiteSpace(editedCustomizeProfile)
                             ? editedCustomizeProfile
                             : character.CustomizeProfile;

            var sb = new System.Text.StringBuilder();

            // Only add bulk-tag lines if Conflict Resolution is disabled
            if (!plugin.Configuration.EnableConflictResolution)
            {
                sb.AppendLine($"/penumbra bulktag disable {collection} | gear");
                sb.AppendLine($"/penumbra bulktag disable {collection} | hair");
                sb.AppendLine($"/penumbra bulktag enable  {collection} | {design}");
                // Glamourer "no clothes" for secret mode
                sb.AppendLine("/glamour apply no clothes | self");
            }

            // Glamourer design
            sb.AppendLine($"/glamour apply {design} | self");

            // Automation (if enabled)
            if (plugin.Configuration.EnableAutomations)
            {
                string automationToUse = !string.IsNullOrWhiteSpace(editedAutomation)
                    ? editedAutomation
                    : (!string.IsNullOrWhiteSpace(character.CharacterAutomation)
                        ? character.CharacterAutomation
                        : "None");
                sb.AppendLine($"/glamour automation enable {automationToUse}");
            }

            // Customize+
            sb.AppendLine("/customize profile disable <me>");
            if (!string.IsNullOrWhiteSpace(custom))
                sb.AppendLine($"/customize profile enable <me>, {custom}");

            // Final redraw
            sb.Append("/penumbra redraw self");

            return sb.ToString();
        }

        private string EnsureProperDesignMacroStructure()
        {
            var character = plugin.Characters[activeCharacterIndex];
            string glamourer = !string.IsNullOrWhiteSpace(editedGlamourerDesign) ? editedGlamourerDesign : "[Glamourer Design]";

            var sb = new System.Text.StringBuilder();

            if (isSecretDesignMode)
            {
                string collection = character.PenumbraCollection;

                // Only add bulk-tag lines if Conflict Resolution is disabled
                if (!plugin.Configuration.EnableConflictResolution)
                {
                    sb.AppendLine($"/penumbra bulktag disable {collection} | gear");
                    sb.AppendLine($"/penumbra bulktag disable {collection} | hair");
                    sb.AppendLine($"/penumbra bulktag enable {collection} | {glamourer}");
                    sb.AppendLine("/glamour apply no clothes | self");
                }

                sb.AppendLine($"/glamour apply {glamourer} | self");
            }
            else
            {
                sb.AppendLine($"/glamour apply {glamourer} | self");
            }

            // Conditionally include automation line
            if (plugin.Configuration.EnableAutomations)
            {
                string automationToUse = !string.IsNullOrWhiteSpace(editedAutomation)
                    ? editedAutomation
                    : (!string.IsNullOrWhiteSpace(character.CharacterAutomation)
                        ? character.CharacterAutomation
                        : "None");
                sb.AppendLine($"/glamour automation enable {automationToUse}");
            }

            // Always disable Customize+ first
            sb.AppendLine("/customize profile disable <me>");

            // Determine Customize+ profile
            string customizeProfileToUse = !string.IsNullOrWhiteSpace(editedCustomizeProfile)
                ? editedCustomizeProfile
                : !string.IsNullOrWhiteSpace(character.CustomizeProfile)
                    ? character.CustomizeProfile
                    : string.Empty;

            // Enable only if needed
            if (!string.IsNullOrWhiteSpace(customizeProfileToUse))
                sb.AppendLine($"/customize profile enable <me>, {customizeProfileToUse}");

            // Redraw line
            sb.Append("/penumbra redraw self");

            return sb.ToString();
        }

        private void UpdateAdvancedMacroGlamourerFixed(string newGlamourer)
        {
            var lines = advancedDesignMacroText.Split('\n').ToList();

            // Find and replace the main glamour apply line (not "no clothes")
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("/glamour apply", StringComparison.OrdinalIgnoreCase) &&
                    !line.Contains("no clothes", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"/glamour apply {newGlamourer} | self";
                    break;
                }
            }

            // Update bulktag enable line if it exists (for secret mode)
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("/penumbra bulktag enable", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract the collection name and replace the design part
                    var parts = line.Split('|');
                    if (parts.Length >= 2)
                    {
                        var collection = parts[0].Replace("/penumbra bulktag enable", "").Trim();
                        lines[i] = $"/penumbra bulktag enable {collection} | {newGlamourer}";
                    }
                    break;
                }
            }

            advancedDesignMacroText = string.Join("\n", lines);
        }

        private void UpdateAdvancedMacroCustomize()
        {
            advancedDesignMacroText = PatchMacroLine(
                advancedDesignMacroText,
                "/customize profile disable",
                "/customize profile disable <me>"
            );

            if (!string.IsNullOrWhiteSpace(editedCustomizeProfile))
            {
                advancedDesignMacroText = PatchMacroLine(
                    advancedDesignMacroText,
                    "/customize profile enable",
                    $"/customize profile enable <me>, {editedCustomizeProfile}"
                );
            }
            else
            {
                advancedDesignMacroText = string.Join("\n",
                    advancedDesignMacroText
                        .Split('\n')
                        .Where(l => !l.TrimStart().StartsWith("/customize profile enable"))
                );
            }
        }

        private string PatchMacroLine(string existing, string prefix, string replacement)
        {
            var lines = existing.Split('\n').ToList();
            var idx = lines.FindIndex(l => l.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

            if (idx >= 0)
            {
                // Replace existing line
                lines[idx] = replacement;
            }
            else
            {
                int insertPosition = GetProperDesignInsertPosition(lines, prefix);
                lines.Insert(insertPosition, replacement);
            }

            return string.Join("\n", lines);
        }

        private int GetProperDesignInsertPosition(List<string> lines, string prefix)
        {
            // Order for design macro commands
            var order = new[]
            {
                "/penumbra bulktag disable",
                "/penumbra bulktag enable",
                "/glamour apply no clothes",
                "/glamour apply",
                "/glamour automation enable",
                "/customize profile disable",
                "/customize profile enable",
                "/penumbra redraw"
            };

            int targetOrder = Array.FindIndex(order, o => prefix.StartsWith(o, StringComparison.OrdinalIgnoreCase));
            if (targetOrder == -1) return lines.Count; // Unknown command goes at end

            // Find the position where this command should be inserted
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

        private string ExtractGlamourerDesignFromMacro(string macro)
        {
            string[] lines = macro.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("/glamour apply ", StringComparison.OrdinalIgnoreCase))
                {
                    return line.Replace("/glamour apply ", "").Replace(" | self", "").Trim();
                }
            }
            return "";
        }

        private static string TruncateWithEllipsis(string text, float maxWidth)
        {
            while (ImGui.CalcTextSize(text + "...").X > maxWidth && text.Length > 0)
                text = text[..^1];
            return text + "...";
        }
        
        // Search helper methods
        private bool MatchesSearchQuery(CharacterDesign design)
        {
            if (string.IsNullOrWhiteSpace(searchQuery))
                return true;
                
            var query = searchQuery.ToLowerInvariant();
            
            // Search in design name
            if (design.Name.ToLowerInvariant().Contains(query))
                return true;
                
            // Search in glamourer design name
            if (!string.IsNullOrWhiteSpace(design.GlamourerDesign) && 
                design.GlamourerDesign.ToLowerInvariant().Contains(query))
                return true;
                
            // Search in automation
            if (!string.IsNullOrWhiteSpace(design.Automation) && 
                design.Automation.ToLowerInvariant().Contains(query))
                return true;
                
            // Search in tags
            if (design.Tag?.ToLowerInvariant().Contains(query) == true)
                return true;
                
            return false;
        }
        
        private bool FolderContainsMatchingDesigns(Character character, DesignFolder folder)
        {
            if (string.IsNullOrWhiteSpace(searchQuery))
                return true;
                
            // Check if folder name matches
            if (folder.Name.ToLowerInvariant().Contains(searchQuery.ToLowerInvariant()))
                return true;
                
            // Check if any design in this folder matches
            if (character.Designs.Any(d => d.FolderId == folder.Id && MatchesSearchQuery(d)))
                return true;
                
            // Check if any subfolder contains matching designs
            var subfolders = character.DesignFolders.Where(f => f.ParentFolderId == folder.Id);
            foreach (var subfolder in subfolders)
            {
                if (FolderContainsMatchingDesigns(character, subfolder))
                    return true;
            }
                
            return false;
        }

        private HashSet<string> LogAndReturnPins(Character character)
        {
            var pins = new HashSet<string>(character.SecretModPins ?? new List<string>());
            Plugin.Log.Information($"[PIN DEBUG] Design panel loading pins for character '{character.Name}': {pins.Count} pins - {string.Join(", ", pins)}");
            Plugin.Log.Information($"[PIN DEBUG] Design panel character object hash: {character.GetHashCode()}");
            return pins;
        }

        private void DrawSnapshotDialog(float scale)
        {
            if (!isSnapshotDialogOpen)
                return;

            // Force window size to fit content without scrolling
            ImGui.SetNextWindowSize(new Vector2(500 * scale, 400 * scale), ImGuiCond.Always);
            ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f));

            bool isOpen = true;
            if (ImGui.Begin("Create Design from Current Look", ref isOpen, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse))
            {
                if (snapshotTargetCharacter == null)
                {
                    ImGui.Text("Error: No character selected");
                    ImGui.End();
                    isSnapshotDialogOpen = false;
                    return;
                }

                // Apply simple dialog styling

                // Header with icon and styling
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 1.0f, 1.0f), "\uf030");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1.0f), "Snapshot Current Character State");
                
                // Subtle styled separator
                ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.4f, 0.6f, 0.8f, 0.5f));
                ImGui.Separator();
                ImGui.PopStyleColor();
                ImGui.Spacing();

                // Design name input with improved styling
                ImGui.TextColored(new Vector4(0.8f, 0.9f, 1.0f, 1.0f), "Design Name:");
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.1f, 0.15f, 0.2f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.15f, 0.2f, 0.25f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.2f, 0.25f, 0.3f, 1.0f));
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##SnapshotName", ref snapshotDesignName, 256);
                ImGui.PopStyleColor(3);
                ImGui.Spacing();

                // Conflict Resolution checkbox (only if enabled in settings)
                if (plugin.Configuration.EnableConflictResolution)
                {
                    ImGui.Checkbox("Use Conflict Resolution", ref snapshotUseConflictResolution);
                    if (ImGui.IsItemHovered())
                        CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Create design with conflict resolution features enabled");
                    ImGui.Spacing();
                }

                // Styled section header
                ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.4f, 0.6f, 0.8f, 0.5f));
                ImGui.Separator();
                ImGui.PopStyleColor();
                ImGui.Spacing();

                // Auto-detection status with improved layout
                ImGui.TextColored(new Vector4(0.8f, 0.9f, 1.0f, 1.0f), "Auto-Detection Status:");
                ImGui.Spacing();

                // Create a child region for detection status to control layout better
                ImGui.BeginChild("DetectionStatus", new Vector2(0, 90 * scale), false);

                // Glamourer detection with icon
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "\uf013");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.Text("Glamourer State:");
                ImGui.SameLine();
                
                float statusPosX = ImGui.GetContentRegionAvail().X - 80 * scale;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + statusPosX);
                
                if (snapshotDetectedMods.Count > 0)
                {
                    ImGui.TextColored(new Vector4(0.3f, 0.8f, 0.3f, 1.0f), "Detected");
                }
                else if (snapshotIsProcessing)
                {
                    ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.3f, 1.0f), "Detecting...");
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.8f, 0.3f, 0.3f, 1.0f), "None");
                }

                // Customize+ detection with icon
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "\uf007");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.Text("Customize+ Profile:");
                ImGui.SameLine();
                
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + statusPosX);
                
                if (!string.IsNullOrEmpty(snapshotDetectedCustomizePlusProfile))
                {
                    ImGui.TextColored(new Vector4(0.3f, 0.8f, 0.3f, 1.0f), "Found");
                }
                else if (snapshotIsProcessing)
                {
                    ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.3f, 1.0f), "Detecting...");
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.8f, 0.3f, 0.3f, 1.0f), "None");
                }

                // Clipboard image detection with icon
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "\uf03e");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.Text("Clipboard Image:");
                ImGui.SameLine();
                
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + statusPosX);
                
                if (snapshotHasClipboardImage)
                {
                    ImGui.TextColored(new Vector4(0.3f, 0.8f, 0.3f, 1.0f), "Available");
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.8f, 0.3f, 0.3f, 1.0f), "None");
                }

                ImGui.EndChild();

                // Status message
                if (!string.IsNullOrEmpty(snapshotStatusMessage))
                {
                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.8f, 0.6f, 0.3f, 1.0f), snapshotStatusMessage);
                }

                // Bottom section with buttons
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.4f, 0.6f, 0.8f, 0.5f));
                ImGui.Separator();
                ImGui.PopStyleColor();
                ImGui.Spacing();

                // Buttons with improved styling
                float buttonWidth = 120 * scale;
                float spacing = 10 * scale;
                float totalButtonWidth = (buttonWidth * 2) + spacing;
                float offsetX = (ImGui.GetContentRegionAvail().X - totalButtonWidth) * 0.5f;
                
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);

                // Create button with plugin-style colors
                bool canCreate = !string.IsNullOrWhiteSpace(snapshotDesignName) && !snapshotIsProcessing;
                if (!canCreate)
                    ImGui.BeginDisabled();

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.6f, 0.9f, 0.7f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.7f, 1.0f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.8f, 1.0f, 1.0f));

                if (ImGui.Button("Create Design", new Vector2(buttonWidth, 0)))
                {
                    CreateSnapshotDesign();
                }

                ImGui.PopStyleColor(3);

                if (!canCreate)
                    ImGui.EndDisabled();

                // Cancel button
                ImGui.SameLine(0, spacing);
                if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
                {
                    isSnapshotDialogOpen = false;
                }
                ImGui.End();
            }

            if (!isOpen)
                isSnapshotDialogOpen = false;
        }

        private void OpenSnapshotDialog(Character character)
        {
            snapshotTargetCharacter = character;
            snapshotDesignName = $"Design {DateTime.Now:yyyy-MM-dd HH:mm}";
            snapshotUseConflictResolution = plugin.Configuration.EnableConflictResolution;
            snapshotDetectedMods.Clear();
            snapshotDetectedCustomizePlusProfile = null;
            snapshotHasClipboardImage = false;
            snapshotIsProcessing = false;
            snapshotStatusMessage = "";
            
            // Start background detection tasks
            Task.Run(async () =>
            {
                try
                {
                    snapshotIsProcessing = true;
                    snapshotStatusMessage = "Detecting Glamourer state...";
                    
                    // Detect Glamourer state
                    await DetectGlamourerState();
                    
                    snapshotStatusMessage = "Detecting Customize+ profile...";
                    
                    // Detect Customize+ profile
                    await DetectCustomizePlusProfile();
                    
                    snapshotStatusMessage = "Checking clipboard for images...";
                    
                    // Check clipboard for images
                    CheckClipboardForImage();
                    
                    snapshotStatusMessage = "Detection complete";
                    snapshotIsProcessing = false;
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"Error during snapshot detection: {ex}");
                    snapshotStatusMessage = "Error during auto-detection";
                    snapshotIsProcessing = false;
                }
            });
            
            isSnapshotDialogOpen = true;
        }

        private void CreateSnapshotDesign()
        {
            if (snapshotTargetCharacter == null)
                return;

            snapshotIsProcessing = true;
            snapshotStatusMessage = "Creating design...";

            Task.Run(async () =>
            {
                try
                {
                    // Generate the appropriate macro based on CR mode
                    var snapshotMacro = GenerateSnapshotMacro(snapshotUseConflictResolution);
                    
                    // For CR mode, generate different macros
                    var regularMacro = GenerateSnapshotMacro(false); // Regular macro without CR
                    var advancedMacro = snapshotUseConflictResolution ? GenerateSnapshotMacro(true) : ""; // CR macro if enabled
                    
                    var newDesign = new CharacterDesign(
                        snapshotDesignName,
                        regularMacro, // Always use regular macro for base
                        snapshotUseConflictResolution, // Enable Advanced Mode if CR is checked
                        advancedMacro, // Advanced/CR macro
                        "", // GlamourerDesign - will be set later
                        "", // Automation
                        "", // CustomizePlusProfile - will be set later
                        null // PreviewImagePath - will be set later
                    );

                    // Create Glamourer design from current state if detected
                    if (snapshotDetectedMods.Count > 0)
                    {
                        var glamourerDesignName = $"{snapshotDesignName}";
                        var glamourerDesignId = await CreateGlamourerDesignFromCurrentState(glamourerDesignName);
                        if (glamourerDesignId != Guid.Empty)
                        {
                            // Store the design name, not the GUID, for CS+ compatibility
                            newDesign.GlamourerDesign = glamourerDesignName;
                            Plugin.Log.Information($"Created Glamourer design: {glamourerDesignName} (ID: {glamourerDesignId})");
                        }
                    }

                    // Set Customize+ profile if detected (only if it's not the Character default)
                    if (!string.IsNullOrEmpty(snapshotDetectedCustomizePlusProfile) && 
                        snapshotDetectedCustomizePlusProfile != "Character")
                    {
                        newDesign.CustomizePlusProfile = snapshotDetectedCustomizePlusProfile;
                    }

                    // Set up Secret Mode state for CR mode
                    if (snapshotUseConflictResolution)
                    {
                        // Get only gear/hair mods from Currently Affecting You tab (prevents body/sculpt/eye mods from being managed)
                        var allAffectingMods = plugin.PenumbraIntegration?.GetOnScreenTabMods();
                        var currentlyAffectingMods = new HashSet<string>();
                        
                        if (allAffectingMods != null)
                        {
                            foreach (var modDir in allAffectingMods)
                            {
                                try
                                {
                                    // Get mod type from cache or determine it
                                    ModType modType;
                                    if (plugin.modCategorizationCache.ContainsKey(modDir))
                                    {
                                        modType = plugin.modCategorizationCache[modDir];
                                    }
                                    else
                                    {
                                        // Use the static method to determine mod type
                                        modType = SecretModeModWindow.DetermineModType(modDir, "", plugin);
                                        plugin.modCategorizationCache[modDir] = modType;
                                    }

                                    // Only include gear and hair mods (safe to toggle, won't break body/sculpt/eyes)
                                    if (modType == ModType.Gear || modType == ModType.Hair)
                                    {
                                        currentlyAffectingMods.Add(modDir);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Plugin.Log.Warning($"Failed to determine mod type for {modDir}: {ex.Message}");
                                }
                            }
                        }
                        if (currentlyAffectingMods != null && currentlyAffectingMods.Count > 0)
                        {
                            // Create mod state dictionary with all currently affecting mods enabled
                            newDesign.SecretModState = new Dictionary<string, bool>();
                            foreach (var modName in currentlyAffectingMods)
                            {
                                newDesign.SecretModState[modName] = true;
                            }
                            Plugin.Log.Information($"Detected {newDesign.SecretModState.Count} currently affecting mods for CR design");
                        }
                        else
                        {
                            Plugin.Log.Information("No currently affecting mods detected for CR design");
                        }
                    }

                    // Save clipboard image if available
                    if (snapshotHasClipboardImage)
                    {
                        var imagePath = await SaveClipboardImageForDesign(newDesign.Id);
                        if (!string.IsNullOrEmpty(imagePath))
                        {
                            newDesign.PreviewImagePath = imagePath;
                        }
                    }

                    // The macro was already set during construction, no need to regenerate

                    // Add the design to the character
                    snapshotTargetCharacter.Designs.Add(newDesign);
                    
                    // Save configuration
                    plugin.Configuration.Save();

                    snapshotStatusMessage = "Design created successfully!";
                    
                    // Close dialog after a brief delay
                    await Task.Delay(1000);
                    isSnapshotDialogOpen = false;
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"Error creating snapshot design: {ex}");
                    snapshotStatusMessage = $"Error: {ex.Message}";
                }
                finally
                {
                    snapshotIsProcessing = false;
                }
            });
        }

        private string GenerateSnapshotMacro(bool useConflictResolution)
        {
            var macroLines = new List<string>();

            if (useConflictResolution)
            {
                // CR Mode: Generate macro that works with Secret Mode CR system
                // No bulktag commands - CR system handles mod management automatically
                
                // Add Glamourer apply if we have a design
                if (snapshotDetectedMods.Count > 0)
                {
                    macroLines.Add($"/glamour apply {snapshotDesignName} | self");
                }

                // Add Customize+ profile commands if we have a non-Character profile
                if (!string.IsNullOrEmpty(snapshotDetectedCustomizePlusProfile) && snapshotDetectedCustomizePlusProfile != "Character")
                {
                    macroLines.Add("/customize profile disable <me>");
                    macroLines.Add($"/customize profile enable <me>, {snapshotDetectedCustomizePlusProfile}");
                }

                // Add penumbra redraw at the end
                macroLines.Add("/penumbra redraw self");
            }
            else
            {
                // Regular Mode: Generate bulktag macros for non-CR designs
                // Add Glamourer apply if we have a design
                if (snapshotDetectedMods.Count > 0)
                {
                    macroLines.Add($"/glamour apply {snapshotDesignName} | self");
                }

                // Add Customize+ profile commands if we have a non-Character profile
                if (!string.IsNullOrEmpty(snapshotDetectedCustomizePlusProfile) && snapshotDetectedCustomizePlusProfile != "Character")
                {
                    macroLines.Add("/customize profile disable <me>");
                    macroLines.Add($"/customize profile enable <me>, {snapshotDetectedCustomizePlusProfile}");
                }

                // Always add penumbra redraw at the end
                macroLines.Add("/penumbra redraw self");
            }

            return string.Join("\n", macroLines);
        }

        private async Task<Guid> CreateGlamourerDesignFromCurrentState(string designName)
        {
            try
            {
                // Get current player's object index (usually 0 for local player)
                var playerIndex = 0;
                
                // First, get the current state data from Glamourer
                var glamourerStateIpc = Plugin.PluginInterface.GetIpcSubscriber<int, uint, (int, string?)>("Glamourer.GetStateBase64");
                var (stateError, stateData) = await Task.Run(() => glamourerStateIpc.InvokeFunc(playerIndex, 0));
                
                if (stateError != 0 || string.IsNullOrEmpty(stateData))
                {
                    Plugin.Log.Warning($"Failed to get Glamourer state for design creation (error: {stateError})");
                    return Guid.Empty;
                }
                
                // Create design from the state data
                var glamourerAddDesignIpc = Plugin.PluginInterface.GetIpcSubscriber<string, string, (int, Guid)>("Glamourer.AddDesign");
                var (addError, designId) = await Task.Run(() => glamourerAddDesignIpc.InvokeFunc(stateData, designName));
                
                if (addError == 0 && designId != Guid.Empty) // Success
                {
                    Plugin.Log.Information($"Created Glamourer design '{designName}' with ID {designId}");
                    return designId;
                }
                else
                {
                    Plugin.Log.Warning($"Failed to create Glamourer design (error: {addError})");
                    return Guid.Empty;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to create Glamourer design: {ex.Message}");
                return Guid.Empty;
            }
        }

        private async Task DetectGlamourerState()
        {
            try
            {
                snapshotDetectedMods.Clear();
                
                // Get current player's object index (usually 0 for local player)
                var playerIndex = 0;
                
                // Use real Glamourer IPC to get current state
                var glamourerStateIpc = Plugin.PluginInterface.GetIpcSubscriber<int, uint, (int, string?)>("Glamourer.GetStateBase64");
                var (errorCode, stateData) = await Task.Run(() => glamourerStateIpc.InvokeFunc(playerIndex, 0));
                
                if (errorCode == 0 && !string.IsNullOrEmpty(stateData)) // Success
                {
                    // We have a valid state, which means there are modifications
                    snapshotDetectedMods.Add("Current Glamourer State");
                    Plugin.Log.Information($"Glamourer detection completed: Active state detected");
                }
                else
                {
                    Plugin.Log.Information($"Glamourer detection completed: No modifications detected (error: {errorCode})");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"Failed to detect Glamourer state: {ex.Message}");
                snapshotDetectedMods.Clear();
            }
        }

        private async Task DetectCustomizePlusProfile()
        {
            try
            {
                // Get current player's object index (usually 0 for local player)
                var playerIndex = (ushort)0;
                
                // Use real Customize+ IPC to get active profile
                var customizePlusIpc = Plugin.PluginInterface.GetIpcSubscriber<ushort, (int, Guid?)>("CustomizePlus.Profile.GetActiveProfileIdOnCharacter");
                var (errorCode, profileId) = await Task.Run(() => customizePlusIpc.InvokeFunc(playerIndex));
                
                if (errorCode == 0 && profileId.HasValue && profileId.Value != Guid.Empty) // Success with profile
                {
                    // Get profile list to find the profile name
                    var profileListIpc = Plugin.PluginInterface.GetIpcSubscriber<(Guid, string, string, List<(string, ushort, byte, ushort)>, int, bool)[]>("CustomizePlus.Profile.GetList");
                    var profileList = await Task.Run(() => profileListIpc.InvokeFunc());
                    
                    // Find the active profile in the list
                    var activeProfile = profileList.FirstOrDefault(p => p.Item1 == profileId.Value);
                    
                    if (activeProfile.Item1 != Guid.Empty) // Found the profile
                    {
                        var profileName = activeProfile.Item2; // The Name field from IPCProfileDataTuple
                        
                        // If it's an empty name or default, treat as Character
                        if (string.IsNullOrWhiteSpace(profileName) || profileName == "Default")
                        {
                            profileName = "Character";
                        }
                        
                        snapshotDetectedCustomizePlusProfile = profileName;
                        Plugin.Log.Information($"Customize+ detection completed: Profile '{profileName}' active");
                    }
                    else
                    {
                        snapshotDetectedCustomizePlusProfile = "Character";
                        Plugin.Log.Information("Customize+ detection completed: Active profile not found in profile list");
                    }
                }
                else
                {
                    // No profile or error - assume Character default
                    snapshotDetectedCustomizePlusProfile = "Character";
                    Plugin.Log.Information($"Customize+ detection completed: Character profile active (error: {errorCode})");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"Failed to detect Customize+ profile: {ex.Message}");
                snapshotDetectedCustomizePlusProfile = "Character";
            }
        }

        private void CheckClipboardForImage()
        {
            try
            {
                // Clipboard operations need to be on STA thread
                var thread = new Thread(() =>
                {
                    try
                    {
                        // Check if clipboard contains image data
                        snapshotHasClipboardImage = System.Windows.Forms.Clipboard.ContainsImage();
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Warning($"Failed to check clipboard for image: {ex.Message}");
                        snapshotHasClipboardImage = false;
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"Failed to check clipboard for image: {ex.Message}");
                snapshotHasClipboardImage = false;
            }
        }

        private async Task<string> GetGlamourerDesignData()
        {
            try
            {
                // In real implementation, this would use Glamourer IPC to export current state
                await Task.Delay(200);
                
                // Example IPC call:
                // return await plugin.DalamudPluginInterface.GetIpcSubscriber<string>("Glamourer.ExportCurrentDesign").InvokeAsync();
                
                // Mock data for testing
                return "MockGlamourerDesignData_" + DateTime.Now.Ticks;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to get Glamourer design data: {ex}");
                return string.Empty;
            }
        }

        private async Task<string> GetCustomizePlusProfileData(string profileName)
        {
            try
            {
                // In real implementation, this would use Customize+ IPC to export profile
                await Task.Delay(200);
                
                // Example IPC call:
                // return await plugin.DalamudPluginInterface.GetIpcSubscriber<string>("CustomizePlus.ExportProfile").InvokeAsync(profileName);
                
                // Mock data for testing
                return $"MockCustomizePlusProfile_{profileName}_{DateTime.Now.Ticks}";
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to get Customize+ profile data: {ex}");
                return string.Empty;
            }
        }

        private async Task<string> SaveClipboardImageForDesign(Guid designId)
        {
            try
            {
                string imagePath = "";
                
                // Clipboard operations need to be on STA thread
                var thread = new Thread(() =>
                {
                    try
                    {
                        if (!System.Windows.Forms.Clipboard.ContainsImage())
                            return;

                        var image = System.Windows.Forms.Clipboard.GetImage();
                        if (image == null)
                            return;

                        // Create designs directory if it doesn't exist
                        var designsDir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "Designs");
                        Directory.CreateDirectory(designsDir);

                        // Save image with design ID as filename
                        imagePath = Path.Combine(designsDir, $"{designId}.png");
                        
                        using (var bitmap = new System.Drawing.Bitmap(image))
                        {
                            bitmap.Save(imagePath, ImageFormat.Png);
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error($"Failed to save clipboard image: {ex}");
                        imagePath = "";
                    }
                });
                
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                return imagePath;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to save clipboard image: {ex}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Sets up the snapshot state and creates a design from a chat command, using the same logic as the UI button
        /// </summary>
        public void SetupSnapshotFromCommand(Character character, string designName, bool useConflictResolution)
        {
            // Set up the snapshot state variables (same as OpenSnapshotDialog)
            snapshotTargetCharacter = character;
            snapshotDesignName = designName;
            snapshotUseConflictResolution = useConflictResolution;
            snapshotDetectedMods = new HashSet<string>();
            snapshotDetectedCustomizePlusProfile = "";
            snapshotHasClipboardImage = Clipboard.ContainsImage();
            snapshotIsProcessing = false;
            snapshotStatusMessage = "";

            // Start the detection and creation process (same as the UI button logic)
            Task.Run(async () =>
            {
                try
                {
                    // Run detection in parallel (same as UI)
                    var detectionTasks = new Task[]
                    {
                        DetectGlamourerState(),
                        DetectCustomizePlusProfile()
                    };

                    await Task.WhenAll(detectionTasks);
                    
                    // Create the design (same as clicking "Create Design" button)
                    CreateSnapshotDesign();
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"Error in snapshot creation from command: {ex}");
                    Plugin.ChatGui.PrintError($"[Character Select+] Failed to create snapshot design: {ex.Message}");
                }
            });
        }

        public void CreateSmartSnapshotFromCommand(Character character, bool useConflictResolution)
        {
            CreateSmartSnapshot(character, useConflictResolution);
        }

        private void CreateSmartSnapshot(Character character, bool useConflictResolution)
        {
            Task.Run(async () =>
            {
                try
                {
                    Plugin.Log.Information($"Starting smart snapshot for character '{character.Name}' with CR: {useConflictResolution}");

                    // Get the most recently created Glamourer design
                    var recentDesign = await GetMostRecentGlamourerDesign();
                    if (recentDesign == null)
                    {
                        Plugin.ChatGui.PrintError("[Character Select+] No recent Glamourer design found. Please create a design in Glamourer first or use the regular snapshot dialog.");
                        return;
                    }

                    Plugin.Log.Information($"Found recent Glamourer design: '{recentDesign.Value.Name}' created on {recentDesign.Value.CreationDate}");

                    // Set snapshot data using the recent design
                    snapshotTargetCharacter = character;
                    snapshotDesignName = recentDesign.Value.Name;
                    snapshotUseConflictResolution = useConflictResolution;
                    snapshotIsProcessing = true;

                    // Auto-detect current state
                    var detectionTasks = new Task[]
                    {
                        DetectGlamourerState(),
                        DetectCustomizePlusProfile(),
                        Task.Run(() => CheckClipboardForImage())
                    };

                    await Task.WhenAll(detectionTasks);

                    // Create the CS+ design with the Glamourer design field populated
                    CreateSmartSnapshotDesign(recentDesign.Value);

                    Plugin.ChatGui.Print($"[Character Select+] Smart snapshot created: '{recentDesign.Value.Name}' {(useConflictResolution ? "with" : "without")} CR");
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"Error in smart snapshot creation: {ex}");
                    Plugin.ChatGui.PrintError($"[Character Select+] Failed to create smart snapshot: {ex.Message}");
                }
            });
        }

        private async Task<(string Name, DateTimeOffset CreationDate, Guid Id)?> GetMostRecentGlamourerDesign()
        {
            try
            {
                // Get Glamourer API with correct IPC method names
                var glamourerApi = Plugin.PluginInterface.GetIpcSubscriber<Dictionary<Guid, string>>("Glamourer.GetDesignList.V2");
                var designsDict = await Task.Run(() => glamourerApi.InvokeFunc());

                if (designsDict == null || designsDict.Count == 0)
                    return null;

                var glamourerJObjectApi = Plugin.PluginInterface.GetIpcSubscriber<Guid, Newtonsoft.Json.Linq.JObject?>("Glamourer.GetDesignJObject");

                // Get design data with timestamps
                var designsWithTimestamps = new List<(string Name, DateTimeOffset CreationDate, Guid Id)>();

                foreach (var kvp in designsDict)
                {
                    try
                    {
                        var designJson = await Task.Run(() => glamourerJObjectApi.InvokeFunc(kvp.Key));
                        if (designJson != null)
                        {
                            var name = designJson["Name"]?.Value<string>() ?? kvp.Value;
                            var creationDate = designJson["CreationDate"]?.Value<DateTimeOffset>() ?? DateTimeOffset.MinValue;
                            
                            designsWithTimestamps.Add((name, creationDate, kvp.Key));
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Warning($"Failed to get timestamp for design {kvp.Key}: {ex.Message}");
                    }
                }

                // Return the most recently created design
                return designsWithTimestamps
                    .Where(d => d.CreationDate > DateTimeOffset.MinValue)
                    .OrderByDescending(d => d.CreationDate)
                    .FirstOrDefault();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to get recent Glamourer designs: {ex}");
                return null;
            }
        }

        private string GenerateSnapshotMacro(Character character, string glamourerDesign, string customizePlusProfile)
        {
            if (string.IsNullOrWhiteSpace(glamourerDesign))
                return "";

            string macro = $"/glamour apply {glamourerDesign} | self";

            // Conditionally include automation line
            if (plugin.Configuration.EnableAutomations)
            {
                string automationToUse = !string.IsNullOrWhiteSpace(character.CharacterAutomation)
                    ? character.CharacterAutomation
                    : "None";

                macro += $"\n/glamour automation enable {automationToUse}";
            }

            // Always disable Customize+ first
            macro += "\n/customize profile disable <me>";

            // Determine Customize+ profile
            string customizeProfileToUse = !string.IsNullOrWhiteSpace(customizePlusProfile)
                ? customizePlusProfile
                : !string.IsNullOrWhiteSpace(character.CustomizeProfile)
                    ? character.CustomizeProfile
                    : string.Empty;

            // Enable only if needed
            if (!string.IsNullOrWhiteSpace(customizeProfileToUse))
                macro += $"\n/customize profile enable <me>, {customizeProfileToUse}";

            // Redraw line
            macro += "\n/penumbra redraw self";

            return macro;
        }

        private void CreateSmartSnapshotDesign((string Name, DateTimeOffset CreationDate, Guid Id) recentDesign)
        {
            try
            {
                if (snapshotTargetCharacter == null)
                {
                    Plugin.Log.Error("No target character set for smart snapshot");
                    return;
                }

                Plugin.Log.Information($"Creating smart snapshot design for character '{snapshotTargetCharacter.Name}' using Glamourer design '{recentDesign.Name}'");

                // Generate the proper macro for the snapshot design
                string snapshotMacro = GenerateSnapshotMacro(snapshotTargetCharacter, recentDesign.Name, 
                    !string.IsNullOrEmpty(snapshotDetectedCustomizePlusProfile) && snapshotDetectedCustomizePlusProfile != "Character" 
                        ? snapshotDetectedCustomizePlusProfile 
                        : "");

                // Create new design based on detected character state
                var newDesign = new CharacterDesign(
                    name: recentDesign.Name,
                    macro: snapshotMacro,
                    isAdvancedMode: false,
                    advancedMacro: "",
                    glamourerDesign: recentDesign.Name, // Use the Glamourer design name
                    automation: "",
                    customizePlusProfile: !string.IsNullOrEmpty(snapshotDetectedCustomizePlusProfile) && snapshotDetectedCustomizePlusProfile != "Character" 
                        ? snapshotDetectedCustomizePlusProfile 
                        : ""
                );

                // Set CR mode if requested
                if (snapshotUseConflictResolution)
                {
                    // Get only gear/hair mods from Currently Affecting You tab (prevents body/sculpt/eye mods from being managed)
                    var allAffectingMods = plugin.PenumbraIntegration?.GetOnScreenTabMods();
                    var currentlyAffectingMods = new HashSet<string>();
                    
                    if (allAffectingMods != null)
                    {
                        foreach (var modDir in allAffectingMods)
                        {
                            try
                            {
                                // Get mod type from cache or determine it
                                ModType modType;
                                if (plugin.modCategorizationCache.ContainsKey(modDir))
                                {
                                    modType = plugin.modCategorizationCache[modDir];
                                }
                                else
                                {
                                    // Use the static method to determine mod type
                                    modType = SecretModeModWindow.DetermineModType(modDir, "", plugin);
                                    plugin.modCategorizationCache[modDir] = modType;
                                }

                                // Only include gear and hair mods (safe to toggle, won't break body/sculpt/eyes)
                                if (modType == ModType.Gear || modType == ModType.Hair)
                                {
                                    currentlyAffectingMods.Add(modDir);
                                }
                            }
                            catch (Exception ex)
                            {
                                Plugin.Log.Warning($"Failed to determine mod type for {modDir}: {ex.Message}");
                            }
                        }
                    }
                    if (currentlyAffectingMods != null && currentlyAffectingMods.Count > 0)
                    {
                        // Create mod state dictionary with all currently affecting mods enabled
                        newDesign.SecretModState = new Dictionary<string, bool>();
                        foreach (var modName in currentlyAffectingMods)
                        {
                            newDesign.SecretModState[modName] = true;
                        }
                        Plugin.Log.Information($"Smart snapshot detected {newDesign.SecretModState.Count} currently affecting mods for CR design");
                    }
                    else
                    {
                        Plugin.Log.Information("Smart snapshot: No currently affecting mods detected for CR design");
                    }
                }

                // Handle clipboard image if available
                if (snapshotHasClipboardImage)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            var imagePath = await SaveClipboardImageForDesign(Guid.NewGuid());
                            if (!string.IsNullOrEmpty(imagePath))
                            {
                                newDesign.PreviewImagePath = imagePath;
                                Plugin.Log.Information($"Saved clipboard image for smart snapshot: {imagePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.Warning($"Failed to save clipboard image for smart snapshot: {ex}");
                        }
                    });
                }

                // Add to character's designs
                snapshotTargetCharacter.Designs.Add(newDesign);

                // Save configuration
                plugin.Configuration.Save();

                Plugin.Log.Information($"Smart snapshot design '{newDesign.Name}' created successfully for character '{snapshotTargetCharacter.Name}'");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error creating smart snapshot design: {ex}");
                Plugin.ChatGui.PrintError($"[Character Select+] Failed to create smart snapshot design: {ex.Message}");
            }
            finally
            {
                snapshotIsProcessing = false;
            }
        }



        private void CloseSnapshotDialog()
        {
            isSnapshotDialogOpen = false;
            snapshotDesignName = "";
            snapshotUseConflictResolution = true;
            snapshotTargetCharacter = null;
            snapshotDetectedMods.Clear();
            snapshotDetectedCustomizePlusProfile = null;
            snapshotHasClipboardImage = false;
            snapshotIsProcessing = false;
            snapshotStatusMessage = "";
        }

        private bool IsDesignCurrentlyActive(Character character, CharacterDesign design)
        {
            // Only show active design for the currently active CS+ character
            var currentActiveCharacter = GetCurrentActiveCharacter();
            if (currentActiveCharacter == null || currentActiveCharacter.Name != character.Name)
                return false;

            if (plugin?.Configuration?.LastUsedDesignByCharacter == null)
                return false;

            if (!plugin.Configuration.LastUsedDesignByCharacter.TryGetValue(character.Name, out var lastUsedDesignName))
                return false;

            return design.Name.Equals(lastUsedDesignName, StringComparison.OrdinalIgnoreCase);
        }

        private Character? GetCurrentActiveCharacter()
        {
            // Use the same logic as the plugin uses to determine current character
            Character? currentCharacter = null;

            // Try player-specific mapping first
            if (Plugin.ObjectTable.LocalPlayer is { } player && player.HomeWorld.IsValid)
            {
                string localName = player.Name.TextValue;
                string worldName = player.HomeWorld.Value.Name.ToString();
                string fullKey = $"{localName}@{worldName}";
                
                if (plugin.Configuration.LastUsedCharacterByPlayer.TryGetValue(fullKey, out var lastUsedCharacterName))
                {
                    // lastUsedCharacterName is in format "CharacterName@WorldName", extract just the character name
                    var characterName = lastUsedCharacterName.Contains("@") ? lastUsedCharacterName.Split('@')[0] : lastUsedCharacterName;
                    currentCharacter = plugin.Characters.FirstOrDefault(c => c.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));
                }
            }

            // Fallback to global last used
            if (currentCharacter == null && !string.IsNullOrEmpty(plugin.Configuration.LastUsedCharacterKey))
            {
                currentCharacter = plugin.Characters.FirstOrDefault(c => c.Name.Equals(plugin.Configuration.LastUsedCharacterKey, StringComparison.OrdinalIgnoreCase));
            }

            return currentCharacter;
        }

        /// <summary>
        /// Performs quick update of gear and hair mods for the current design
        /// </summary>
        private void PerformQuickGearHairUpdate(Character character)
        {
            try
            {
                Plugin.Log.Information("Starting quick gear/hair update...");
                
                // Get all currently affecting mods using the existing method
                var allAffectingMods = plugin.PenumbraIntegration.GetCurrentlyAffectingMods();
                Plugin.Log.Information($"Found {allAffectingMods.Count} total affecting mods");
                
                if (!allAffectingMods.Any())
                {
                    Plugin.Log.Warning("No affecting mods detected for quick update");
                    return;
                }
                
                // Filter for gear and hair mods only
                var gearHairMods = new HashSet<string>();
                var modList = plugin.PenumbraIntegration.GetModList();
                
                foreach (var modDir in allAffectingMods)
                {
                    // Check if mod is in categorization cache
                    if (plugin.modCategorizationCache?.TryGetValue(modDir, out var modType) == true)
                    {
                        if (modType == CharacterSelectPlugin.Windows.ModType.Gear || 
                            modType == CharacterSelectPlugin.Windows.ModType.Hair)
                        {
                            gearHairMods.Add(modDir);
                            Plugin.Log.Debug($"✓ Included {modType} mod: {modDir}");
                        }
                        else
                        {
                            Plugin.Log.Debug($"✗ Excluded {modType} mod: {modDir}");
                        }
                    }
                    else if (modList.TryGetValue(modDir, out var modName))
                    {
                        // Not in cache, check by changed items
                        var changedItems = plugin.PenumbraIntegration.GetModChangedItems(modDir, modName);
                        if (IsGearMod(changedItems.Keys) || IsHairMod(changedItems.Keys))
                        {
                            gearHairMods.Add(modDir);
                            Plugin.Log.Debug($"✓ Included gear/hair mod by analysis: {modDir}");
                        }
                    }
                }
                
                Plugin.Log.Information($"Filtered to {gearHairMods.Count} gear/hair mods");
                
                if (!gearHairMods.Any())
                {
                    Plugin.Log.Information("No gear/hair mods currently affecting - nothing to update");
                    return;
                }
                
                // Create new mod state with only gear/hair mods enabled
                var newModState = new Dictionary<string, bool>();
                
                // Get existing mod state to preserve non-gear/hair selections
                Dictionary<string, bool> existingState = null;
                if (isNewDesign)
                {
                    existingState = temporaryDesignSecretModState ?? new Dictionary<string, bool>();
                }
                else if (!string.IsNullOrEmpty(originalDesignName))
                {
                    var currentDesign = character.Designs.FirstOrDefault(d => d.Name == originalDesignName);
                    existingState = currentDesign?.SecretModState ?? new Dictionary<string, bool>();
                }
                
                // Preserve existing non-gear/hair mod selections
                if (existingState != null)
                {
                    foreach (var (modDir, enabled) in existingState)
                    {
                        if (!gearHairMods.Contains(modDir))
                        {
                            newModState[modDir] = enabled; // Keep existing state for non-gear/hair mods
                        }
                    }
                }
                
                // Add the new gear/hair mods as enabled
                foreach (var modDir in gearHairMods)
                {
                    newModState[modDir] = true;
                }
                
                // Update the design's mod state
                if (isNewDesign)
                {
                    temporaryDesignSecretModState = newModState;
                    Plugin.Log.Information($"Updated temporary design state with {gearHairMods.Count} gear/hair mods");
                }
                else if (!string.IsNullOrEmpty(originalDesignName))
                {
                    var design = character.Designs.FirstOrDefault(d => d.Name == originalDesignName);
                    if (design != null)
                    {
                        design.SecretModState = newModState;
                        temporaryDesignSecretModState = newModState; // Keep temp state in sync
                        plugin.SaveConfiguration();
                        Plugin.Log.Information($"Updated design '{design.Name}' with {gearHairMods.Count} gear/hair mods");
                    }
                }
                
                Plugin.Log.Information("Quick gear/hair update completed successfully");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error during quick gear/hair update: {ex}");
            }
        }
        
        /// <summary>
        /// Check if a mod is a gear mod based on its changed items
        /// </summary>
        private bool IsGearMod(IEnumerable<string> changedItems)
        {
            foreach (var item in changedItems)
            {
                // Check for equipment-related items
                if (item.Contains("Equipment:", StringComparison.OrdinalIgnoreCase) ||
                    item.Contains("/equipment/", StringComparison.OrdinalIgnoreCase) ||
                    item.Contains("gear", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        
        /// <summary>
        /// Check if a mod is a hair mod based on its changed items
        /// </summary>
        private bool IsHairMod(IEnumerable<string> changedItems)
        {
            foreach (var item in changedItems)
            {
                // Check for hair-related customization items
                if (item.Contains("Hair", StringComparison.OrdinalIgnoreCase) && 
                    item.Contains("Customization:", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                
                // Check for hair file paths
                if (item.Contains("/hair/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin.Windows
{
    /// <summary>
    /// ImGui-based file browser window for selecting image files.
    /// Alternative to Windows file dialog for Linux/Wine users.
    /// </summary>
    public partial class ImGuiFileBrowserWindow : Window
    {
        public string? SelectedPath { get; private set; }
        public bool Confirmed { get; private set; }
        public Action<string>? OnFileSelected { get; set; }

        private Configuration? configuration;
        private string currentDirectory;
        private string[] currentFiles = Array.Empty<string>();
        private string[] currentDirectories = Array.Empty<string>();
        private string? selectedFile;
        private string? previewPath;
        private string searchFilter = "";
        // Pre-formatted size string per file. Populated in RefreshDirectory so
        // the per-frame file row doesn't have to hit the filesystem via
        // FileInfo(...) for every visible file. Keyed by full path.
        private readonly Dictionary<string, string> fileMetaCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly string[] allowedExtensions;
        private readonly List<string> quickAccessPaths = new();
        private readonly List<string> recentDirectories = new();
        private string pathInput = "";
        private readonly Stack<string> backStack = new();
        private readonly Stack<string> forwardStack = new();
        // Cached file metadata so the preview pane shows size/dim without
        // re-reading the file every frame.
        private long previewFileSize;
        private DateTime previewFileModified;
        private int previewImageW;
        private int previewImageH;

        // Sort options
        private enum SortOption { Name, DateModified, Size, Type }
        private static readonly string[] SortOptionNames = { "Name", "Date Modified", "Size", "Type" };
        private SortOption currentSort = SortOption.Name;
        private bool sortDescending = false;

        public ImGuiFileBrowserWindow(string title = "Select File", string[]? extensions = null)
            : base($"{title}###ImGuiFileBrowser")
        {
            Size = new Vector2(900, 600);
            SizeCondition = ImGuiCond.FirstUseEver;
            Flags = ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

            allowedExtensions = extensions ?? new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

            // Set initial directory
            currentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrEmpty(currentDirectory) || !Directory.Exists(currentDirectory))
            {
                currentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            BuildQuickAccessPaths();
            RefreshDirectory();
        }

        public void SetConfiguration(Configuration config)
        {
            configuration = config;
        }

        private bool IsPinned(string path)
        {
            return configuration?.PinnedFileBrowserPaths.Contains(path) == true;
        }

        private void TogglePin(string path)
        {
            if (configuration == null) return;

            if (configuration.PinnedFileBrowserPaths.Contains(path))
                configuration.PinnedFileBrowserPaths.Remove(path);
            else
                configuration.PinnedFileBrowserPaths.Add(path);

            configuration.Save();
        }

        private void BuildQuickAccessPaths()
        {
            quickAccessPaths.Clear();

            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!string.IsNullOrEmpty(pictures) && Directory.Exists(pictures))
                quickAccessPaths.Add(pictures);
            if (!string.IsNullOrEmpty(documents) && Directory.Exists(documents))
                quickAccessPaths.Add(documents);
            if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop))
                quickAccessPaths.Add(desktop);
            if (Directory.Exists(downloads))
                quickAccessPaths.Add(downloads);
            if (!string.IsNullOrEmpty(userProfile) && Directory.Exists(userProfile))
                quickAccessPaths.Add(userProfile);

            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady)
                        quickAccessPaths.Add(drive.RootDirectory.FullName);
                }
            }
            catch { }
        }

        private void RefreshDirectory()
        {
            try
            {
                pathInput = currentDirectory;

                // Sort directories using same sort option as files
                var dirs = Directory.GetDirectories(currentDirectory)
                    .Where(d => !new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.Hidden));
                currentDirectories = ApplyDirectorySorting(dirs).ToArray();

                // Files sorted by current sort option
                var files = Directory.GetFiles(currentDirectory)
                    .Where(f => allowedExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    .Where(f => !new FileInfo(f).Attributes.HasFlag(FileAttributes.Hidden));

                currentFiles = ApplySorting(files).ToArray();

                // Pre-format size strings for the file list so the per-frame
                // file row never calls FileInfo. Massive FPS win on folders
                // with hundreds/thousands of files.
                fileMetaCache.Clear();
                foreach (var f in currentFiles)
                {
                    try
                    {
                        long len = new FileInfo(f).Length;
                        string sizeStr = len < 1024
                            ? $"{len}B"
                            : len < 1024 * 1024
                                ? $"{len / 1024}K"
                                : $"{len / (1024.0 * 1024.0):F1}M";
                        fileMetaCache[f] = sizeStr;
                    }
                    catch { fileMetaCache[f] = string.Empty; }
                }

                if (!recentDirectories.Contains(currentDirectory))
                {
                    recentDirectories.Insert(0, currentDirectory);
                    if (recentDirectories.Count > 10)
                        recentDirectories.RemoveAt(recentDirectories.Count - 1);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"Error reading directory {currentDirectory}: {ex.Message}");
                currentFiles = Array.Empty<string>();
                currentDirectories = Array.Empty<string>();
            }
        }

        private IEnumerable<string> ApplyDirectorySorting(IEnumerable<string> dirs)
        {
            IOrderedEnumerable<string> sorted = currentSort switch
            {
                SortOption.DateModified => sortDescending
                    ? dirs.OrderByDescending(d => new DirectoryInfo(d).LastWriteTime)
                    : dirs.OrderBy(d => new DirectoryInfo(d).LastWriteTime),
                // Name, Size, and Type all sort folders alphabetically
                _ => sortDescending
                    ? dirs.OrderByDescending(d => Path.GetFileName(d))
                    : dirs.OrderBy(d => Path.GetFileName(d))
            };
            return sorted;
        }

        private IEnumerable<string> ApplySorting(IEnumerable<string> files)
        {
            IOrderedEnumerable<string> sorted = currentSort switch
            {
                SortOption.Name => sortDescending
                    ? files.OrderByDescending(f => Path.GetFileName(f))
                    : files.OrderBy(f => Path.GetFileName(f)),
                SortOption.DateModified => sortDescending
                    ? files.OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    : files.OrderBy(f => new FileInfo(f).LastWriteTime),
                SortOption.Size => sortDescending
                    ? files.OrderByDescending(f => new FileInfo(f).Length)
                    : files.OrderBy(f => new FileInfo(f).Length),
                SortOption.Type => sortDescending
                    ? files.OrderByDescending(f => Path.GetExtension(f)).ThenBy(f => Path.GetFileName(f))
                    : files.OrderBy(f => Path.GetExtension(f)).ThenBy(f => Path.GetFileName(f)),
                _ => files.OrderBy(f => Path.GetFileName(f))
            };
            return sorted;
        }

        private void NavigateTo(string path)
        {
            if (Directory.Exists(path) && path != currentDirectory)
            {
                backStack.Push(currentDirectory);
                forwardStack.Clear();
                currentDirectory = path;
                selectedFile = null;
                previewPath = null;
                RefreshDirectory();
            }
        }

        private void NavigateUp()
        {
            var parent = Directory.GetParent(currentDirectory);
            if (parent != null)
                NavigateTo(parent.FullName);
        }

        private void NavigateBack()
        {
            if (backStack.Count == 0) return;
            forwardStack.Push(currentDirectory);
            currentDirectory = backStack.Pop();
            selectedFile = null;
            previewPath = null;
            RefreshDirectory();
        }

        private void NavigateForward()
        {
            if (forwardStack.Count == 0) return;
            backStack.Push(currentDirectory);
            currentDirectory = forwardStack.Pop();
            selectedFile = null;
            previewPath = null;
            RefreshDirectory();
        }

        private int _chromeColorCount = 0;
        public override void PreDraw()
        {
            if (Plugin.UseClassicLayout) return;
            // Chrome (WindowBg / TitleBg / TitleBgActive / MenuBarBg) follows
            // the active theme so the file browser blends with the rest of the
            // plugin. Border + scrollbar accents stay gold to match the
            // boutique chassis vocabulary.
            var cfg = Plugin.Instance?.Configuration;
            _chromeColorCount = cfg != null
                ? CharacterSelectPlugin.Windows.Styles.ThemeHelper.PushWindowChromeColors(cfg)
                : 0;
            ImGui.PushStyleColor(ImGuiCol.Border, Boutique.BorderSoft);
            ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, Boutique.Velvet);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Boutique.WithAlpha(Boutique.Gold, 0.25f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Boutique.WithAlpha(Boutique.Gold, 0.45f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Boutique.WithAlpha(Boutique.Gold, 0.65f));
            ImGui.PushStyleColor(ImGuiCol.ResizeGrip, Boutique.WithAlpha(Boutique.Gold, 0.20f));
            ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, Boutique.WithAlpha(Boutique.Gold, 0.45f));
            ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, Boutique.WithAlpha(Boutique.Gold, 0.70f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 0f);
        }

        public override void PostDraw()
        {
            if (_chromeColorCount == 0) return;
            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor(9);
            CharacterSelectPlugin.Windows.Styles.ThemeHelper.PopWindowChromeColors(_chromeColorCount);
            _chromeColorCount = 0;
        }

        public override void Draw()
        {
            if (Plugin.UseClassicLayout) { DrawClassicLayout(); return; }
            float scale = (Plugin.Instance?.Configuration?.UIScaleMultiplier ?? 1f);
            scale = MathF.Max(0.85f, MathF.Min(scale, 2.0f));
            var dl = ImGui.GetWindowDrawList();

            // Boutique form style stack, single source of truth for all
            // inputs/buttons inside the picker. ChildBg adds the inner contrast.
            CharacterSelectPlugin.Windows.Styles.Boutique.PushFormStyle();
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.04f, 0.05f, 0.08f, 0.96f));

            // ── Header strip (40px) ──
            // 2px gold top binding + tracked-caps "SELECT IMAGE" label + X close.
            float headH = 40f * scale;
            var winPos = ImGui.GetCursorScreenPos();
            float winW = ImGui.GetContentRegionAvail().X;
            var headMin = winPos;
            var headMax = new Vector2(winPos.X + winW, winPos.Y + headH);

            // Vertical gradient (Surface2 → Surface1)
            uint headTop = Boutique.U32(Boutique.Surface2);
            uint headBot = Boutique.U32(Boutique.Surface1);
            dl.AddRectFilledMultiColor(headMin, headMax, headTop, headTop, headBot, headBot);
            // Bottom hairline
            dl.AddLine(new Vector2(headMin.X, headMax.Y - 1f * scale),
                       new Vector2(headMax.X, headMax.Y - 1f * scale),
                       Boutique.U32(Boutique.BorderSoft), 1f * scale);
            // 2px gold top binding with soft glow
            dl.AddRectFilled(new Vector2(headMin.X, headMin.Y),
                             new Vector2(headMax.X, headMin.Y + 2f * scale),
                             Boutique.U32(Boutique.Gold));

            // Label, Oswald Semi 12px tracked 0.32em (matches mockup)
            string label = "SELECT IMAGE";
            using (Plugin.Instance?.OswaldSemi12?.Push())
            {
                float trackPx = ImGui.GetFontSize() * 0.32f;
                float labelY = headMin.Y + (headH - ImGui.GetFontSize()) * 0.5f;
                CharacterSelectPlugin.Windows.Styles.Boutique.DrawTrackedText(
                    dl, new Vector2(headMin.X + 12f * scale, labelY),
                    label, Boutique.U32(Boutique.Text), trackPx);
            }

            // X close on the right
            float xSize = 24f * scale;
            var xMin = new Vector2(headMax.X - 12f * scale - xSize, headMin.Y + (headH - xSize) * 0.5f);
            var xMax = xMin + new Vector2(xSize, xSize);
            ImGui.SetCursorScreenPos(xMin);
            bool xClicked = ImGui.InvisibleButton("##fp_x", new Vector2(xSize, xSize));
            bool xHovered = ImGui.IsItemHovered();
            uint xBg = Boutique.U32(xHovered
                ? Boutique.WithAlpha(Boutique.Red, 0.20f)
                : new Vector4(0.078f, 0.094f, 0.125f, 0.6f));
            uint xBorder = Boutique.U32(xHovered ? Boutique.Red : Boutique.BorderSoft);
            dl.AddRectFilled(xMin, xMax, xBg);
            dl.AddRect(xMin, xMax, xBorder, 0f, ImDrawFlags.None, 1f * scale);
            var iconFont = UiBuilder.IconFont;
            string xGlyph = "";
            ImGui.PushFont(iconFont);
            var xs = ImGui.CalcTextSize(xGlyph);
            ImGui.PopFont();
            float xIconSize = iconFont.FontSize * 0.65f;
            float xScaleR = xIconSize / iconFont.FontSize;
            dl.AddText(iconFont, xIconSize,
                xMin + new Vector2((xSize - xs.X * xScaleR) * 0.5f, (xSize - xs.Y * xScaleR) * 0.5f),
                Boutique.U32(xHovered ? Boutique.Red : Boutique.Text), xGlyph);
            if (xClicked)
            {
                Confirmed = false;
                SelectedPath = null;
                IsOpen = false;
            }

            ImGui.SetCursorScreenPos(new Vector2(winPos.X, headMax.Y));

            // ── Path bar (36px) ──
            float pathH = 36f * scale;
            var pathMin = ImGui.GetCursorScreenPos();
            var pathMax = new Vector2(pathMin.X + winW, pathMin.Y + pathH);
            dl.AddRectFilled(pathMin, pathMax,
                Boutique.U32(new Vector4(0.039f, 0.047f, 0.063f, 0.55f)));
            dl.AddLine(new Vector2(pathMin.X, pathMax.Y - 1f * scale),
                       new Vector2(pathMax.X, pathMax.Y - 1f * scale),
                       Boutique.U32(Boutique.BorderSoft), 1f * scale);
            ImGui.SetCursorScreenPos(new Vector2(pathMin.X + 10f * scale, pathMin.Y + (pathH - 26f * scale) * 0.5f));
            DrawPathBar();
            ImGui.SetCursorScreenPos(new Vector2(winPos.X, pathMax.Y));

            // ── 3-column body ──
            // Mockup proportions: 200 / 320 / flex
            float footerH = 52f * scale;
            float contentHeight = ImGui.GetContentRegionAvail().Y - footerH;
            float qaW = 200f * scale;
            float listW = 320f * scale;

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

            ImGui.BeginChild("##fp_qa", new Vector2(qaW, contentHeight), false);
            DrawQuickAccess();
            ImGui.EndChild();
            ImGui.SameLine();

            ImGui.BeginChild("##fp_list", new Vector2(listW, contentHeight), false);
            DrawFileListContent();
            ImGui.EndChild();
            ImGui.SameLine();

            ImGui.BeginChild("##fp_preview", new Vector2(0, contentHeight), false);
            DrawPreview();
            ImGui.EndChild();

            ImGui.PopStyleVar(2);

            // Vertical column separators, drawn on the FOREGROUND draw list so
            // they render above each child window's background fill. Drawing
            // them on the parent's `dl` put them under the children's bg,
            // which is why the files|preview separator wasn't appearing.
            var fgDl = ImGui.GetForegroundDrawList();
            var qaMin = new Vector2(winPos.X, pathMax.Y);
            uint sepCol = Boutique.U32(Boutique.BorderSoft);
            fgDl.AddLine(new Vector2(qaMin.X + qaW, qaMin.Y),
                         new Vector2(qaMin.X + qaW, qaMin.Y + contentHeight),
                         sepCol, 1f * scale);
            fgDl.AddLine(new Vector2(qaMin.X + qaW + listW, qaMin.Y),
                         new Vector2(qaMin.X + qaW + listW, qaMin.Y + contentHeight),
                         sepCol, 1f * scale);

            // ── Footer (52px) ──
            DrawBoutiqueBottomBar(scale);

            // Pops the inner ChildBg only; window-level colours pop in PostDraw.
            ImGui.PopStyleColor(1);
            CharacterSelectPlugin.Windows.Styles.Boutique.PopFormStyle();
        }

        private void DrawBoutiqueBottomBar(float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            float footPadX = 14f * scale;
            float btnH = 30f * scale;
            float cancelW = 96f * scale;
            float openW = 130f * scale;
            float footerH = 52f * scale;

            // Footer surface, vertical gradient + top hairline border
            var pos = ImGui.GetCursorScreenPos();
            float w = ImGui.GetContentRegionAvail().X;
            var footMin = pos;
            var footMax = pos + new Vector2(w, footerH);
            uint topCol = Boutique.U32(Boutique.Surface1);
            uint botCol = Boutique.U32(Boutique.Surface0);
            dl.AddRectFilledMultiColor(footMin, footMax, topCol, topCol, botCol, botCol);
            dl.AddLine(new Vector2(footMin.X, footMin.Y),
                       new Vector2(footMax.X, footMin.Y),
                       Boutique.U32(Boutique.BorderSoft), 1f * scale);

            float midY = (footMin.Y + footMax.Y) * 0.5f;
            float btnY = midY - btnH * 0.5f;

            // Selected file name (left, gold-warm). Clipped so it doesn't
            // bleed into the buttons. Filter pill removed per the user's
            // request - the filter list is fixed at the call-site anyway.
            string selectedDisplay = selectedFile != null
                ? Path.GetFileName(selectedFile)
                : "No file selected";
            using (Plugin.Instance?.OutfitMed13?.Push())
            {
                float nameY = midY - ImGui.GetFontSize() * 0.5f;
                Vector4 nameCol = selectedFile != null ? Boutique.GoldWarm : Boutique.TextDim;
                float nameLeftX = footMin.X + footPadX;
                float reservedW = cancelW + openW + footPadX * 3;
                float nameMaxX = footMax.X - reservedW;
                dl.PushClipRect(new Vector2(nameLeftX, footMin.Y),
                                new Vector2(nameMaxX, footMax.Y), true);
                dl.AddText(new Vector2(nameLeftX, nameY), Boutique.U32(nameCol), selectedDisplay);
                dl.PopClipRect();
            }

            // CANCEL + OPEN, no Oswald push so they use the same default
            // ImGui font as the main window's "ADD CHARACTER" gold pill.
            var cancelMin = new Vector2(footMax.X - footPadX - cancelW - footPadX - openW, btnY);
            var cancelMax = cancelMin + new Vector2(cancelW, btnH);
            if (CharacterSelectPlugin.Windows.Styles.Boutique.DrawCancelBtn(
                    dl, cancelMin, cancelMax, "CANCEL", 1.6f * scale, scale, "fp", ImGui.GetFont()))
            {
                Confirmed = false;
                SelectedPath = null;
                IsOpen = false;
            }

            var openMin = new Vector2(footMax.X - footPadX - openW, btnY);
            var openMax = openMin + new Vector2(openW, btnH);
            bool hasSelection = selectedFile != null;
            if (CharacterSelectPlugin.Windows.Styles.Boutique.DrawSavePill(
                    dl, openMin, openMax, "OPEN", 1.8f * scale, scale, "fp_open",
                    !hasSelection, _staticSheen)
                && hasSelection)
            {
                ConfirmSelection();
            }

            ImGui.Dummy(new Vector2(0, footerH));
        }

        // Static sheen tracker so file picker doesn't need a UIStyles instance
        private static readonly System.Collections.Generic.Dictionary<string, DateTime> _fpSheenStarts = new();
        private const float FpSheenDuration = 0.65f;
        private static float _staticSheen(string id, bool hovered)
        {
            if (!hovered)
            {
                _fpSheenStarts.Remove(id);
                return -1f;
            }
            if (!_fpSheenStarts.ContainsKey(id))
                _fpSheenStarts[id] = DateTime.UtcNow;
            float elapsed = (float)(DateTime.UtcNow - _fpSheenStarts[id]).TotalSeconds;
            if (elapsed >= FpSheenDuration) return -1f;
            return elapsed / FpSheenDuration;
        }

        private void DrawPathBar()
        {
            // Mockup path bar: [back] [forward] [up] [crumb breadcrumb] [refresh]
            // All buttons 26x26 squares; crumb fills remaining width with a
            // dark velvet pill displaying the current path as a breadcrumb
            // (segments separated by gold-deep "/" glyphs).
            float scale = (Plugin.Instance?.Configuration?.UIScaleMultiplier ?? 1f);
            scale = MathF.Max(0.85f, MathF.Min(scale, 2.0f));
            float btnH = 26f * scale;
            float btnW = 26f * scale;
            float gap = 6f * scale;

            DrawPathNavIconButton(FontAwesomeIcon.ArrowLeft, "##fp_nav_back", "Back",
                btnW, btnH, scale, NavigateBack, enabled: backStack.Count > 0);
            ImGui.SameLine(0, gap);
            DrawPathNavIconButton(FontAwesomeIcon.ArrowRight, "##fp_nav_fwd", "Forward",
                btnW, btnH, scale, NavigateForward, enabled: forwardStack.Count > 0);
            ImGui.SameLine(0, gap);
            DrawPathNavIconButton(FontAwesomeIcon.ArrowUp, "##fp_nav_up", "Up",
                btnW, btnH, scale, NavigateUp);
            ImGui.SameLine(0, gap);

            // Crumb pill, fills the middle. Uses an InputText overlay so
            // user can still type a path; the breadcrumb segments draw over
            // the input when it's NOT focused.
            float crumbW = ImGui.GetContentRegionAvail().X - btnW - gap;
            DrawPathCrumb(crumbW, btnH, scale);

            ImGui.SameLine(0, gap);

            DrawPathNavIconButton(FontAwesomeIcon.SyncAlt, "##fp_nav_refresh", "Refresh",
                btnW, btnH, scale, RefreshDirectory);
        }

        private void DrawPathCrumb(float w, float h, float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var max = pos + new Vector2(w, h);

            // Background
            dl.AddRectFilled(pos, max,
                Boutique.U32(new Vector4(0.078f, 0.094f, 0.125f, 0.6f)));

            // Determine focus state by overlaying an invisible InputText
            // anchored to the same rect; if it's active, render an editable
            // path. Otherwise paint the breadcrumb segments.
            float fontH = ImGui.GetTextLineHeight();
            float padX = 10f * scale;
            float padY = MathF.Max(0f, (h - fontH) * 0.5f);

            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(padX, padY));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0f, 0f, 0f, 0f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0f, 0f, 0f, 0f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0f, 0f, 0f, 0f));

            ImGui.SetCursorScreenPos(pos);
            ImGui.SetNextItemWidth(w);
            // Use an empty placeholder when focused so user sees just their typing
            bool changed = ImGui.InputText("##fp_path_input", ref pathInput, 1024,
                ImGuiInputTextFlags.EnterReturnsTrue);
            bool isFocused = ImGui.IsItemActive();
            if (changed && Directory.Exists(pathInput))
                NavigateTo(pathInput);

            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar(2);

            // Border (focus state lifts to GoldDeep)
            uint borderCol = Boutique.U32(isFocused ? Boutique.GoldDeep : Boutique.BorderSoft);
            dl.AddRect(pos, max, borderCol, 0f, ImDrawFlags.None, 1f * scale);

            // Breadcrumb overlay when not focused, paint over the input text
            if (!isFocused)
            {
                // Mask out the input's own text rendering by overdrawing the bg
                dl.AddRectFilled(pos + new Vector2(1f, 1f), max - new Vector2(1f, 1f),
                    Boutique.U32(new Vector4(0.078f, 0.094f, 0.125f, 1f)));

                using (Plugin.Instance?.OutfitMed12?.Push())
                {
                    var segs = SplitPathSegments(currentDirectory);
                    float crumbFontH = ImGui.GetFontSize();
                    float cursorX = pos.X + padX;
                    float cursorY = pos.Y + (h - crumbFontH) * 0.5f;
                    float maxRight = max.X - padX;

                    var sepCol = Boutique.U32(Boutique.GoldDeep);
                    var dimCol = Boutique.U32(Boutique.TextDim);
                    var activeCol = Boutique.U32(Boutique.GoldWarm);

                    for (int i = 0; i < segs.Count; i++)
                    {
                        bool isLast = i == segs.Count - 1;
                        string seg = segs[i];
                        var segSize = ImGui.CalcTextSize(seg);
                        if (cursorX + segSize.X > maxRight)
                        {
                            dl.AddText(new Vector2(cursorX, cursorY), dimCol, "...");
                            break;
                        }
                        dl.AddText(new Vector2(cursorX, cursorY),
                            isLast ? activeCol : dimCol, seg);
                        cursorX += segSize.X;
                        if (!isLast)
                        {
                            var sepSize = ImGui.CalcTextSize(" / ");
                            dl.AddText(new Vector2(cursorX, cursorY), sepCol, " / ");
                            cursorX += sepSize.X;
                        }
                    }
                }
            }
        }

        private static List<string> SplitPathSegments(string path)
        {
            var segs = new List<string>();
            if (string.IsNullOrEmpty(path)) return segs;
            // Normalise separators
            var p = path.Replace('\\', '/');
            // Drive root like "C:" should be its own segment
            if (p.Length >= 2 && p[1] == ':')
            {
                segs.Add(p.Substring(0, 2));
                p = p.Substring(2).TrimStart('/');
            }
            foreach (var part in p.Split('/', StringSplitOptions.RemoveEmptyEntries))
                segs.Add(part);
            return segs;
        }

        // Small boutique-styled icon button used by the path bar nav row.
        // Square slip with BorderSoft border, lifts to GoldDeep on hover.
        // Centred FontAwesome glyph at 75% of the icon font size.
        private void DrawPathNavIconButton(FontAwesomeIcon icon, string id, string tooltip,
            float w, float h, float scale, Action onClick, bool enabled = true)
        {
            var dl = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var max = pos + new Vector2(w, h);

            ImGui.SetCursorScreenPos(pos);
            bool clicked = ImGui.InvisibleButton(id, new Vector2(w, h)) && enabled;
            bool hovered = enabled && ImGui.IsItemHovered();

            uint bgCol = Boutique.U32(hovered
                ? Boutique.Surface2
                : new Vector4(0.078f, 0.094f, 0.125f, 0.6f));
            uint borderCol = Boutique.U32(hovered
                ? Boutique.GoldDeep
                : Boutique.BorderSoft);

            dl.AddRectFilled(pos, max, bgCol);
            dl.AddRect(pos, max, borderCol, 0f, ImDrawFlags.None, 1f * scale);

            string glyph = icon.ToIconString();
            var iconFont = UiBuilder.IconFont;
            float glyphPx = iconFont.FontSize * 0.65f;
            float glyphScale = glyphPx / iconFont.FontSize;
            ImGui.PushFont(iconFont);
            var glyphSz = ImGui.CalcTextSize(glyph);
            ImGui.PopFont();
            Vector4 inkCol = enabled
                ? (hovered ? Boutique.GoldWarm : Boutique.TextDim)
                : Boutique.TextGhost;
            dl.AddText(iconFont, glyphPx,
                new Vector2(pos.X + (w - glyphSz.X * glyphScale) * 0.5f,
                            pos.Y + (h - glyphSz.Y * glyphScale) * 0.5f),
                Boutique.U32(inkCol), glyph);

            if (hovered) CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(tooltip);
            if (clicked) onClick?.Invoke();
        }

        // Small boutique-styled icon button used by the path bar nav row.
        // Square slip with BorderSoft border, lifts to GoldDeep on hover.
        // Centred FontAwesome glyph at 75% of the icon font size.
        private void DrawPathNavIconButton(FontAwesomeIcon icon, string id, string tooltip,
            float w, float h, float fs, Action onClick)
        {
            var dl = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var max = pos + new Vector2(w, h);

            ImGui.SetCursorScreenPos(pos);
            bool clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
            bool hovered = ImGui.IsItemHovered();

            uint bgCol = Boutique.U32(hovered
                ? Boutique.Surface2
                : new Vector4(0.078f, 0.094f, 0.125f, 0.6f));
            uint borderCol = Boutique.U32(hovered
                ? Boutique.GoldDeep
                : Boutique.BorderSoft);

            dl.AddRectFilled(pos, max, bgCol);
            dl.AddRect(pos, max, borderCol, 0f, ImDrawFlags.None, 1f * fs);

            string glyph = icon.ToIconString();
            var iconFont = UiBuilder.IconFont;
            float glyphPx = iconFont.FontSize * 0.75f;
            float glyphScale = glyphPx / iconFont.FontSize;
            ImGui.PushFont(iconFont);
            var glyphSz = ImGui.CalcTextSize(glyph);
            ImGui.PopFont();
            Vector4 inkCol = hovered ? Boutique.GoldWarm : Boutique.TextDim;
            dl.AddText(iconFont, glyphPx,
                new Vector2(pos.X + (w - glyphSz.X * glyphScale) * 0.5f,
                            pos.Y + (h - glyphSz.Y * glyphScale) * 0.5f),
                Boutique.U32(inkCol), glyph);

            if (hovered) CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(tooltip);
            if (clicked) onClick?.Invoke();
        }

        private void DrawQuickAccess()
        {
            float scale = (Plugin.Instance?.Configuration?.UIScaleMultiplier ?? 1f);
            scale = MathF.Max(0.85f, MathF.Min(scale, 2.0f));

            // Column header strip, "QUICK ACCESS" in tracked-caps, GoldWarm
            DrawQaColumnHead("QUICK ACCESS", scale);

            ImGui.BeginChild("##fp_qa_body", new Vector2(0, 0), false, ImGuiWindowFlags.HorizontalScrollbar);
            ImGui.Dummy(new Vector2(0, 6f * scale));

            // ── SYSTEM section ──
            DrawQaSectionLabel("SYSTEM", scale);
            foreach (var path in quickAccessPaths)
            {
                var name = GetQuickAccessName(path);
                var icon = GetPathIcon(path);
                bool isActive = string.Equals(currentDirectory, path, StringComparison.OrdinalIgnoreCase);
                bool isPinned = IsPinned(path);
                if (DrawQaEntry(name, icon, isActive, isPinned, $"##fp_qa_sys_{path}", scale))
                    NavigateTo(path);
            }

            // ── PINNED section ──
            var pinnedPaths = configuration?.PinnedFileBrowserPaths;
            if (pinnedPaths != null && pinnedPaths.Count > 0)
            {
                ImGui.Dummy(new Vector2(0, 4f * scale));
                DrawQaSectionLabel("PINNED", scale);

                string? pathToRemove = null;
                for (int i = 0; i < pinnedPaths.Count; i++)
                {
                    var pinPath = pinnedPaths[i];
                    if (!Directory.Exists(pinPath)) continue;
                    var pinName = Path.GetFileName(pinPath);
                    if (string.IsNullOrEmpty(pinName)) pinName = pinPath;
                    bool isActive = string.Equals(currentDirectory, pinPath, StringComparison.OrdinalIgnoreCase);

                    if (DrawQaEntry(pinName, FontAwesomeIcon.Folder, isActive, true, $"##fp_qa_pin_{i}", scale))
                        NavigateTo(pinPath);

                    if (ImGui.BeginPopupContextItem($"##fp_qa_pinctx{i}"))
                    {
                        if (ImGui.MenuItem("Unpin from Quick Access"))
                            pathToRemove = pinPath;
                        ImGui.EndPopup();
                    }
                }
                if (pathToRemove != null) TogglePin(pathToRemove);
            }

            // ── RECENT section ──
            if (recentDirectories.Count > 0)
            {
                ImGui.Dummy(new Vector2(0, 4f * scale));
                DrawQaSectionLabel("RECENT", scale);
                foreach (var path in recentDirectories.Take(5))
                {
                    if (!Directory.Exists(path)) continue;
                    var name = Path.GetFileName(path);
                    if (string.IsNullOrEmpty(name)) name = path;
                    bool isActive = string.Equals(currentDirectory, path, StringComparison.OrdinalIgnoreCase);
                    if (DrawQaEntry(name, FontAwesomeIcon.Clock, isActive, false, $"##fp_qa_rec_{path}", scale))
                        NavigateTo(path);
                }
            }

            ImGui.Dummy(new Vector2(0, 8f * scale));
            ImGui.EndChild();
        }

        // Column header strip at the top of each picker column.
        // Tracked-caps Oswald Semi 11px in GoldWarm with bottom hairline.
        private void DrawQaColumnHead(string label, float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            float w = ImGui.GetContentRegionAvail().X;
            float h = 30f * scale;
            var max = pos + new Vector2(w, h);

            dl.AddRectFilled(pos, max, Boutique.U32(new Vector4(0f, 0f, 0f, 0.30f)));
            dl.AddLine(new Vector2(pos.X, max.Y - 1f * scale),
                       new Vector2(max.X, max.Y - 1f * scale),
                       Boutique.U32(Boutique.BorderSoft), 1f * scale);

            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                float trackPx = ImGui.GetFontSize() * 0.32f;
                float labelY = pos.Y + (h - ImGui.GetFontSize()) * 0.5f;
                CharacterSelectPlugin.Windows.Styles.Boutique.DrawTrackedText(
                    dl, new Vector2(pos.X + 12f * scale, labelY),
                    label, Boutique.U32(Boutique.GoldWarm), trackPx);
            }
            ImGui.Dummy(new Vector2(w, h));
        }

        // File list column head matching the Wardrobe sort-pill blueprint:
        // a SORT kicker + value + chevron pill on the right, sort-direction
        // square on its right, and the folder/count label on the left
        // (truncated with "..." if long).
        private void DrawFileListColumnHead(string label, float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            float w = ImGui.GetContentRegionAvail().X;
            float h = 30f * scale;
            var max = pos + new Vector2(w, h);

            dl.AddRectFilled(pos, max, Boutique.U32(new Vector4(0f, 0f, 0f, 0.30f)));
            dl.AddLine(new Vector2(pos.X, max.Y - 1f * scale),
                       new Vector2(max.X, max.Y - 1f * scale),
                       Boutique.U32(Boutique.BorderSoft), 1f * scale);

            // Reserved width on the right for sort pill + dir button
            float pillW = 168f * scale;
            float dirSide = 22f * scale;
            float padR = 10f * scale;
            float gap = 4f * scale;
            float ctrlsW = pillW + gap + dirSide + padR;

            // Label on the left, truncated to fit before the sort controls
            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                float trackPx = ImGui.GetFontSize() * 0.32f;
                float labelY = pos.Y + (h - ImGui.GetFontSize()) * 0.5f;
                float maxLabelW = w - 12f * scale - ctrlsW - 8f * scale;
                string display = label;
                float labelW = CharacterSelectPlugin.Windows.Styles.BoutiqueChassis
                    .MeasureTrackedText(display, trackPx);
                if (labelW > maxLabelW && display.Length > 4)
                {
                    const string ell = "...";
                    float ellW = CharacterSelectPlugin.Windows.Styles.BoutiqueChassis
                        .MeasureTrackedText(ell, trackPx);
                    for (int k = display.Length - 1; k > 0; k--)
                    {
                        var trunc = display.Substring(0, k);
                        float w2 = CharacterSelectPlugin.Windows.Styles.BoutiqueChassis
                            .MeasureTrackedText(trunc, trackPx);
                        if (w2 + ellW <= maxLabelW) { display = trunc + ell; break; }
                    }
                }
                CharacterSelectPlugin.Windows.Styles.Boutique.DrawTrackedText(
                    dl, new Vector2(pos.X + 12f * scale, labelY),
                    display, Boutique.U32(Boutique.GoldWarm), trackPx);
            }

            // Sort controls layout: pill (left) + direction button (right)
            float ctrlY = pos.Y + (h - dirSide) * 0.5f;
            float dirX = max.X - padR - dirSide;
            float pillX = dirX - gap - pillW;
            float pillH = dirSide;
            var pillMin = new Vector2(pillX, ctrlY);
            var pillMax = pillMin + new Vector2(pillW, pillH);

            // Sort pill (Wardrobe-style: SORT kicker + value + chevron)
            ImGui.SetCursorScreenPos(pillMin);
            bool pillClicked = ImGui.InvisibleButton("##fp_sort_pill", new Vector2(pillW, pillH));
            bool pillHovered = ImGui.IsItemHovered();
            if (pillClicked)
            {
                fpSortPopupOpen = true;
                fpSortPopupAnchor = new Vector2(pillMin.X, pillMax.Y + 4f * scale);
                ImGui.OpenPopup("##fp_sort_popup");
            }

            dl.AddRectFilled(pillMin, pillMax,
                Boutique.U32(new Vector4(8f / 255f, 10f / 255f, 14f / 255f, 0.85f)));
            Vector4 pillBorderC = fpSortPopupOpen
                ? Boutique.Gold
                : (pillHovered ? Boutique.GoldDeep : Boutique.BorderSoft);
            dl.AddRect(pillMin, pillMax, Boutique.U32(pillBorderC), 0f, ImDrawFlags.None, 1f * scale);

            float pillPadX = 12f * scale;
            // Kicker SORT, brighter and slightly bigger so it's actually
            // readable. Was OswaldMed9 + TextGhost which was too dim.
            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                float kY = (pillMin.Y + pillMax.Y) * 0.5f - ImGui.GetFontSize() * 0.5f;
                Boutique.DrawTrackedText(dl,
                    new Vector2(pillMin.X + pillPadX, kY),
                    "SORT", Boutique.U32(Boutique.TextDim), 2.5f * scale);
            }
            // Value (right-aligned before chevron)
            using (Plugin.Instance?.OswaldMed11?.Push())
            {
                string sortVal = SortOptionNames[(int)currentSort].ToUpperInvariant();
                float vY = (pillMin.Y + pillMax.Y) * 0.5f - ImGui.GetFontSize() * 0.5f;
                float trackPx = 1.8f * scale;
                float vW = Boutique.MeasureTrackedText(sortVal, trackPx);
                float chevW = 14f * scale;
                float vX = pillMax.X - pillPadX - chevW - vW;
                Boutique.DrawTrackedText(dl,
                    new Vector2(vX, vY),
                    sortVal, Boutique.U32(Boutique.GoldWarm), trackPx);
            }
            // Chevron
            float chR = 4f * scale;
            var chC = new Vector2(pillMax.X - pillPadX - chR, (pillMin.Y + pillMax.Y) * 0.5f);
            dl.AddTriangleFilled(
                chC + new Vector2(-chR, -chR * 0.5f),
                chC + new Vector2( chR, -chR * 0.5f),
                chC + new Vector2(0f, chR),
                Boutique.U32(Boutique.GoldDeep));

            // Direction button (square slip with border)
            ImGui.SetCursorScreenPos(new Vector2(dirX, ctrlY));
            bool dirClicked = ImGui.InvisibleButton("##fp_sort_dir", new Vector2(dirSide, dirSide));
            bool dirHovered = ImGui.IsItemHovered();
            uint dirBg = Boutique.U32(dirHovered ? Boutique.Surface2
                : new Vector4(0.078f, 0.094f, 0.125f, 0.6f));
            uint dirBorder = Boutique.U32(dirHovered ? Boutique.GoldDeep : Boutique.BorderSoft);
            var dirMin = new Vector2(dirX, ctrlY);
            var dirMax = dirMin + new Vector2(dirSide, dirSide);
            dl.AddRectFilled(dirMin, dirMax, dirBg);
            dl.AddRect(dirMin, dirMax, dirBorder, 0f, ImDrawFlags.None, 1f * scale);
            string dirGlyph = (sortDescending
                ? FontAwesomeIcon.SortAmountDown
                : FontAwesomeIcon.SortAmountUp).ToIconString();
            var iconFont = UiBuilder.IconFont;
            float dirIconPx = iconFont.FontSize * 0.60f;
            float dirIconScale = dirIconPx / iconFont.FontSize;
            ImGui.PushFont(iconFont);
            var dirGlyphSz = ImGui.CalcTextSize(dirGlyph);
            ImGui.PopFont();
            dl.AddText(iconFont, dirIconPx,
                new Vector2(dirX + (dirSide - dirGlyphSz.X * dirIconScale) * 0.5f,
                            ctrlY + (dirSide - dirGlyphSz.Y * dirIconScale) * 0.5f),
                Boutique.U32(dirHovered ? Boutique.GoldWarm : Boutique.TextDim),
                dirGlyph);
            if (dirHovered)
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(
                    sortDescending ? "Sort descending - click for ascending" : "Sort ascending - click for descending");
            if (dirClicked) { sortDescending = !sortDescending; RefreshDirectory(); }

            // ── Popup (Wardrobe blueprint) ──
            ImGui.SetNextWindowPos(fpSortPopupAnchor);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(6f / 255f, 7f / 255f, 9f / 255f, 0.97f));
            ImGui.PushStyleColor(ImGuiCol.Border, Boutique.WithAlpha(Boutique.GoldDeep, 0.85f));
            ImGui.PushStyleColor(ImGuiCol.Text, Boutique.Text);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 4f * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 1f * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
            if (ImGui.BeginPopup("##fp_sort_popup"))
            {
                float itemH = 24f * scale;
                float itemPadX = 14f * scale;
                float itemW = pillW; // match pill width so text fits
                var popupFont = Plugin.Instance?.OswaldMed11;

                for (int i = 0; i < SortOptionNames.Length; i++)
                {
                    bool isSel = i == (int)currentSort;
                    var rowMn = ImGui.GetCursorScreenPos();
                    var rowMx = new Vector2(rowMn.X + itemW, rowMn.Y + itemH);
                    ImGui.InvisibleButton($"##fp_sort_item_{i}", new Vector2(itemW, itemH));
                    bool itemHov = ImGui.IsItemHovered();
                    bool itemClk = ImGui.IsItemClicked();
                    if (itemClk)
                    {
                        currentSort = (SortOption)i;
                        RefreshDirectory();
                        fpSortPopupOpen = false;
                        ImGui.CloseCurrentPopup();
                    }

                    var pdl = ImGui.GetWindowDrawList();
                    if (isSel)
                    {
                        pdl.AddRectFilled(rowMn, rowMx,
                            Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.18f)));
                        pdl.AddRectFilled(rowMn,
                            new Vector2(rowMn.X + 2f * scale, rowMx.Y),
                            Boutique.U32(Boutique.Gold));
                    }
                    else if (itemHov)
                    {
                        pdl.AddRectFilled(rowMn, rowMx,
                            Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.10f)));
                    }

                    if (popupFont != null)
                    {
                        using (popupFont.Push())
                        {
                            float fontH = ImGui.GetFontSize();
                            float trackPx = fontH * 0.18f;
                            string itemLabel = SortOptionNames[i].ToUpperInvariant();
                            // Bright Text by default, was TextDim which read
                            // as washed out against the dark popup bg.
                            Vector4 col = isSel ? Boutique.GoldWarm
                                : (itemHov ? Boutique.GoldBright : Boutique.Text);
                            Boutique.DrawTrackedText(pdl,
                                new Vector2(rowMn.X + itemPadX, rowMn.Y + (itemH - fontH) * 0.5f),
                                itemLabel, Boutique.U32(col), trackPx);
                        }
                    }
                }
                ImGui.EndPopup();
            }
            else if (fpSortPopupOpen)
            {
                fpSortPopupOpen = false;
            }
            ImGui.PopStyleVar(4);
            ImGui.PopStyleColor(3);

            ImGui.Dummy(new Vector2(w, h));
        }

        private bool fpSortPopupOpen = false;
        private Vector2 fpSortPopupAnchor;

        // Inline mini-section label inside the QA column body.
        // Oswald Semi 11 tracked-caps in TextDim, brighter than the old
        // TextFaint, with a slightly heavier weight to match the rest of the
        // form's section dividers.
        private void DrawQaSectionLabel(string label, float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            ImGui.Dummy(new Vector2(0, 4f * scale));
            var pos = ImGui.GetCursorScreenPos();
            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                float trackPx = ImGui.GetFontSize() * 0.28f;
                CharacterSelectPlugin.Windows.Styles.Boutique.DrawTrackedText(
                    dl, new Vector2(pos.X + 12f * scale, pos.Y + 2f * scale),
                    label, Boutique.U32(Boutique.TextDim), trackPx);
            }
            ImGui.Dummy(new Vector2(0, ImGui.GetTextLineHeight() + 6f * scale));
        }

        // One QA row: 28px tall, icon + name + optional pin marker.
        // Active row gets a soft gold gradient bg + a 2px gold left bar.
        // Returns true if clicked.
        private bool DrawQaEntry(string label, FontAwesomeIcon icon, bool isActive, bool isPinned,
            string id, float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            float w = ImGui.GetContentRegionAvail().X;
            float h = 28f * scale;
            var pos = ImGui.GetCursorScreenPos();
            var max = pos + new Vector2(w, h);

            ImGui.SetCursorScreenPos(pos);
            bool clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
            bool hovered = ImGui.IsItemHovered();

            // Backgrounds
            if (isActive)
            {
                // Gradient: gold@10% → transparent (mockup matches a 90deg fade)
                uint left = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.10f));
                uint right = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0f));
                dl.AddRectFilledMultiColor(pos, max, left, right, right, left);
                // 2px gold left bar
                dl.AddRectFilled(new Vector2(pos.X, pos.Y + 4f * scale),
                                 new Vector2(pos.X + 2f * scale, max.Y - 4f * scale),
                                 Boutique.U32(Boutique.Gold));
            }
            else if (hovered)
            {
                dl.AddRectFilled(pos, max,
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.05f)));
            }

            // Icon
            var iconFont = UiBuilder.IconFont;
            float iconPx = iconFont.FontSize * 0.65f;
            float iconScale = iconPx / iconFont.FontSize;
            ImGui.PushFont(iconFont);
            string glyph = icon.ToIconString();
            var glyphSz = ImGui.CalcTextSize(glyph);
            ImGui.PopFont();
            Vector4 iconColor = isActive ? Boutique.Gold
                : (hovered ? Boutique.GoldWarm : Boutique.TextFaint);
            dl.AddText(iconFont, iconPx,
                new Vector2(pos.X + 12f * scale, pos.Y + (h - glyphSz.Y * iconScale) * 0.5f),
                Boutique.U32(iconColor), glyph);

            // Label, Outfit body 12px. Bright Text by default, GoldWarm when
            // active, Text on hover. Was TextDim by default which was too dim.
            using (Plugin.Instance?.OutfitMed12?.Push())
            {
                float labelY = pos.Y + (h - ImGui.GetFontSize()) * 0.5f;
                Vector4 labelCol = isActive ? Boutique.GoldWarm : Boutique.Text;
                dl.AddText(new Vector2(pos.X + 12f * scale + glyphSz.X * iconScale + 8f * scale, labelY),
                    Boutique.U32(labelCol), label);
            }

            // Pin marker (right side), gold filled bookmark when pinned, faint outline on hover otherwise
            if (isPinned || hovered)
            {
                ImGui.PushFont(iconFont);
                string pinGlyph = isPinned
                    ? FontAwesomeIcon.Bookmark.ToIconString()
                    : FontAwesomeIcon.Bookmark.ToIconString();
                var pinSz = ImGui.CalcTextSize(pinGlyph);
                ImGui.PopFont();
                float pinPx = iconFont.FontSize * 0.55f;
                float pinScale = pinPx / iconFont.FontSize;
                Vector4 pinColor = isPinned ? Boutique.Gold : Boutique.TextGhost;
                dl.AddText(iconFont, pinPx,
                    new Vector2(max.X - 14f * scale - pinSz.X * pinScale,
                                pos.Y + (h - pinSz.Y * pinScale) * 0.5f),
                    Boutique.U32(pinColor), pinGlyph);
            }

            return clicked;
        }

        private string GetQuickAccessName(string path)
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (path == pictures) return "Pictures";
            if (path == documents) return "Documents";
            if (path == desktop) return "Desktop";
            if (path == downloads) return "Downloads";
            if (path == userProfile) return "Home";

            if (path.Length <= 3 && path.Contains(':'))
            {
                try
                {
                    var drive = new DriveInfo(path);
                    var label = string.IsNullOrEmpty(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel;
                    return $"{label} ({drive.Name.TrimEnd('\\')})";
                }
                catch { return path; }
            }

            return Path.GetFileName(path) ?? path;
        }

        private FontAwesomeIcon GetPathIcon(string path)
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (path == pictures) return FontAwesomeIcon.Images;
            if (path == documents) return FontAwesomeIcon.FileAlt;
            if (path == desktop) return FontAwesomeIcon.Desktop;
            if (path == downloads) return FontAwesomeIcon.Download;
            if (path == userProfile) return FontAwesomeIcon.Home;
            if (path.Length <= 3 && path.Contains(':')) return FontAwesomeIcon.Hdd;

            return FontAwesomeIcon.Folder;
        }

        private void DrawFileListContent()
        {
            float scale = (Plugin.Instance?.Configuration?.UIScaleMultiplier ?? 1f);
            scale = MathF.Max(0.85f, MathF.Min(scale, 2.0f));

            // Column header, folder name + " · N ITEMS" + sort controls
            string folderName = Path.GetFileName(currentDirectory);
            if (string.IsNullOrEmpty(folderName)) folderName = currentDirectory;
            int itemCount = currentDirectories.Length + currentFiles.Length;
            string headLabel = $"{folderName.ToUpperInvariant()} · {itemCount} ITEMS";
            DrawFileListColumnHead(headLabel, scale);

            ImGui.BeginChild("##fp_list_body", new Vector2(0, 0), false);
            ImGui.Dummy(new Vector2(0, 6f * scale));

            // Parent directory shortcut row
            if (Directory.GetParent(currentDirectory) != null)
            {
                if (DrawFileRow("..", FontAwesomeIcon.Folder, "UP",
                        false, true, "##fp_row_up", scale, doubleClick: false))
                {
                    NavigateUp();
                }
            }

            // Directories
            for (int di = 0; di < currentDirectories.Length; di++)
            {
                var dir = currentDirectories[di];
                var name = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(searchFilter) &&
                    !name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool clicked = DrawFileRow(name, FontAwesomeIcon.Folder, "FOLDER",
                    false, true, $"##fp_row_dir_{di}", scale, doubleClick: true,
                    pinPath: dir);

                if (ImGui.IsItemActive() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    NavigateTo(dir);
                else if (clicked)
                    NavigateTo(dir);

                if (ImGui.BeginPopupContextItem($"##fp_dirctx_{di}"))
                {
                    if (ImGui.MenuItem("Open")) NavigateTo(dir);
                    if (IsPinned(dir))
                    {
                        if (ImGui.MenuItem("Unpin from Quick Access")) TogglePin(dir);
                    }
                    else
                    {
                        if (ImGui.MenuItem("Pin to Quick Access")) TogglePin(dir);
                    }
                    ImGui.EndPopup();
                }
            }

            // Files
            for (int fi = 0; fi < currentFiles.Length; fi++)
            {
                var file = currentFiles[fi];
                var name = Path.GetFileName(file);
                if (!string.IsNullOrEmpty(searchFilter) &&
                    !name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Read pre-formatted size from the directory-load cache so the
                // file row doesn't hit the filesystem every frame.
                fileMetaCache.TryGetValue(file, out var meta);
                meta ??= string.Empty;

                bool isSelected = string.Equals(selectedFile, file, StringComparison.OrdinalIgnoreCase);
                bool clicked = DrawFileRow(name, FontAwesomeIcon.Image, meta,
                    isSelected, false, $"##fp_row_file_{fi}", scale, doubleClick: true);

                if (clicked)
                {
                    selectedFile = file;
                    previewPath = file;
                    UpdatePreviewMetadata();
                }
                if (ImGui.IsItemActive() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    selectedFile = file;
                    previewPath = file;
                    UpdatePreviewMetadata();
                    ConfirmSelection();
                }
            }

            if (currentFiles.Length == 0 && currentDirectories.Length == 0)
            {
                ImGui.Dummy(new Vector2(0, 12f * scale));
                using (Plugin.Instance?.OutfitMed12?.Push())
                {
                    ImGui.SetCursorPosX(12f * scale);
                    ImGui.TextColored(Boutique.TextFaint, "No matching items");
                }
            }

            ImGui.EndChild();
        }

        // Single 28-px file/folder row. Icon + name + meta (right-aligned).
        // For folders, an optional pin marker on the right is clickable for
        // pin/unpin (when isDirectory is true and pinPath != null). Selected
        // state: gradient bg + 2px gold left bar. Returns true on row click.
        private bool DrawFileRow(string name, FontAwesomeIcon icon, string meta,
            bool isSelected, bool isDirectory, string id, float scale, bool doubleClick,
            string? pinPath = null)
        {
            var dl = ImGui.GetWindowDrawList();
            float w = ImGui.GetContentRegionAvail().X;
            float h = 28f * scale;
            var pos = ImGui.GetCursorScreenPos();
            var max = pos + new Vector2(w, h);

            // Virtualization: rows scrolled off-screen skip every paint /
            // CalcTextSize / font push below and just advance the cursor.
            // Without this the browser does ~10 CalcTextSize calls + 2 font
            // pushes + truncation loop PER row PER frame for every file in
            // the folder, even those the user can't see.
            if (!ImGui.IsRectVisible(new Vector2(w, h)))
            {
                ImGui.Dummy(new Vector2(w, h));
                return false;
            }

            // Pin button, only for directories with a pinPath. Created FIRST
            // so it sits on top of the row's hit area for click priority.
            float pinSide = 22f * scale;
            float pinRightPad = 8f * scale;
            bool isPinned = pinPath != null && IsPinned(pinPath);
            bool pinClicked = false;
            bool pinHovered = false;
            if (isDirectory && pinPath != null)
            {
                var pinMin = new Vector2(max.X - pinRightPad - pinSide, pos.Y + (h - pinSide) * 0.5f);
                ImGui.SetCursorScreenPos(pinMin);
                pinClicked = ImGui.InvisibleButton($"{id}_pin", new Vector2(pinSide, pinSide));
                pinHovered = ImGui.IsItemHovered();
                if (pinHovered)
                    CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(
                        isPinned ? "Unpin from Quick Access" : "Pin to Quick Access");
                if (pinClicked) TogglePin(pinPath);
            }

            ImGui.SetCursorScreenPos(pos);
            bool clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
            bool hovered = ImGui.IsItemHovered() || pinHovered;
            // If the pin was clicked, swallow the row click so we don't navigate
            if (pinClicked) clicked = false;

            // Bg: selected = gradient + gold bar; hovered = subtle white wash
            if (isSelected)
            {
                uint l = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.10f));
                uint r = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0f));
                dl.AddRectFilledMultiColor(pos, max, l, r, r, l);
                dl.AddRectFilled(new Vector2(pos.X, pos.Y + 4f * scale),
                                 new Vector2(pos.X + 2f * scale, max.Y - 4f * scale),
                                 Boutique.U32(Boutique.Gold));
            }
            else if (hovered)
            {
                dl.AddRectFilled(pos, max,
                    Boutique.U32(new Vector4(1f, 1f, 1f, 0.03f)));
            }

            // Icon (cyan for directories, faint for files; gold-warm when selected)
            var iconFont = UiBuilder.IconFont;
            float iconPx = iconFont.FontSize * 0.70f;
            float iconScale = iconPx / iconFont.FontSize;
            string glyph = icon.ToIconString();
            ImGui.PushFont(iconFont);
            var glyphSz = ImGui.CalcTextSize(glyph);
            ImGui.PopFont();
            Vector4 iconCol;
            if (isSelected) iconCol = Boutique.GoldWarm;
            else if (isDirectory) iconCol = Boutique.CyanSoft;
            else iconCol = Boutique.TextFaint;
            dl.AddText(iconFont, iconPx,
                new Vector2(pos.X + 12f * scale, pos.Y + (h - glyphSz.Y * iconScale) * 0.5f),
                Boutique.U32(iconCol), glyph);

            // Name (Outfit Med 12), clip to fit before pin (if any) + meta
            float metaW = string.IsNullOrEmpty(meta) ? 0f : 56f * scale;
            float pinReserve = (isDirectory && pinPath != null) ? (pinSide + pinRightPad + 4f * scale) : 0f;
            float nameLeft = pos.X + 12f * scale + glyphSz.X * iconScale + 8f * scale;
            float nameRight = max.X - 12f * scale - metaW - pinReserve;
            using (Plugin.Instance?.OutfitMed12?.Push())
            {
                float nameY = pos.Y + (h - ImGui.GetFontSize()) * 0.5f;
                Vector4 nameCol = isSelected ? Boutique.GoldWarm : Boutique.Text;
                // Truncate manually if needed
                string display = name;
                var nameSz = ImGui.CalcTextSize(display);
                if (nameLeft + nameSz.X > nameRight && display.Length > 1)
                {
                    const string ell = "...";
                    float ellW = ImGui.CalcTextSize(ell).X;
                    for (int k = display.Length - 1; k > 0; k--)
                    {
                        var trunc = display.Substring(0, k);
                        if (nameLeft + ImGui.CalcTextSize(trunc).X + ellW <= nameRight)
                        {
                            display = trunc + ell;
                            break;
                        }
                    }
                }
                dl.PushClipRect(new Vector2(nameLeft, pos.Y), new Vector2(nameRight, max.Y), true);
                dl.AddText(new Vector2(nameLeft, nameY), Boutique.U32(nameCol), display);
                dl.PopClipRect();
            }

            // Meta (right-aligned). Plain AddText, not tracked-caps, so we
            // skip the per-glyph allocations on every visible row each frame.
            if (!string.IsNullOrEmpty(meta))
            {
                using (Plugin.Instance?.OswaldSemi11?.Push())
                {
                    var metaSz = ImGui.CalcTextSize(meta);
                    float metaY = pos.Y + (h - ImGui.GetFontSize()) * 0.5f;
                    float metaRightX = max.X - 12f * scale - pinReserve;
                    dl.AddText(new Vector2(metaRightX - metaSz.X, metaY),
                        Boutique.U32(Boutique.TextDim), meta);
                }
            }

            // Pin marker, gold filled bookmark when pinned, faint outline on hover only.
            if (isDirectory && pinPath != null && (isPinned || hovered))
            {
                var pinIconFont = UiBuilder.IconFont;
                string pinGlyph = FontAwesomeIcon.Bookmark.ToIconString();
                ImGui.PushFont(pinIconFont);
                var pinSz = ImGui.CalcTextSize(pinGlyph);
                ImGui.PopFont();
                float pinPx = pinIconFont.FontSize * 0.60f;
                float pinIconScale = pinPx / pinIconFont.FontSize;
                Vector4 pinColor = isPinned
                    ? (pinHovered ? Boutique.GoldWarm : Boutique.Gold)
                    : (pinHovered ? Boutique.GoldWarm : Boutique.TextGhost);
                float pinIconX = max.X - pinRightPad - pinSide + (pinSide - pinSz.X * pinIconScale) * 0.5f;
                float pinIconY = pos.Y + (h - pinSz.Y * pinIconScale) * 0.5f;
                dl.AddText(pinIconFont, pinPx,
                    new Vector2(pinIconX, pinIconY),
                    Boutique.U32(pinColor), pinGlyph);
            }

            return clicked;
        }

        private void UpdatePreviewMetadata()
        {
            if (string.IsNullOrEmpty(previewPath) || !File.Exists(previewPath))
            {
                previewFileSize = 0;
                previewFileModified = DateTime.MinValue;
                previewImageW = previewImageH = 0;
                return;
            }
            try
            {
                var info = new FileInfo(previewPath);
                previewFileSize = info.Length;
                previewFileModified = info.LastWriteTime;
            }
            catch { previewFileSize = 0; previewFileModified = DateTime.MinValue; }
            // Image dims will be filled in lazily when DrawPreview reads the texture
            previewImageW = previewImageH = 0;
        }

        private void DrawPreview()
        {
            float scale = (Plugin.Instance?.Configuration?.UIScaleMultiplier ?? 1f);
            scale = MathF.Max(0.85f, MathF.Min(scale, 2.0f));

            DrawQaColumnHead("PREVIEW", scale);

            ImGui.BeginChild("##fp_preview_body", new Vector2(0, 0), false);
            ImGui.Dummy(new Vector2(0, 14f * scale));

            if (string.IsNullOrEmpty(previewPath))
            {
                ImGui.Dummy(new Vector2(0, 12f * scale));
                using (Plugin.Instance?.OutfitMed12?.Push())
                {
                    var availW = ImGui.GetContentRegionAvail().X;
                    var msgSz = ImGui.CalcTextSize("Select a file to preview");
                    ImGui.SetCursorPosX((availW - msgSz.X) * 0.5f);
                    ImGui.TextColored(Boutique.TextFaint, "Select a file to preview");
                }
                ImGui.EndChild();
                return;
            }

            if (!File.Exists(previewPath))
            {
                ImGui.Dummy(new Vector2(0, 12f * scale));
                using (Plugin.Instance?.OutfitMed12?.Push())
                {
                    var availW = ImGui.GetContentRegionAvail().X;
                    var msgSz = ImGui.CalcTextSize("File not found");
                    ImGui.SetCursorPosX((availW - msgSz.X) * 0.5f);
                    ImGui.TextColored(Boutique.Red, "File not found");
                }
                ImGui.EndChild();
                return;
            }

            // ── Image card with chamfered slip + gold gilt frame ──
            // No meta block below, the user prefers a larger preview, so the
            // image fills the available column with the file metadata stripped.
            var texture = Plugin.TextureProvider?.GetFromFile(previewPath).GetWrapOrDefault();
            if (texture != null)
            {
                if (previewImageW == 0) { previewImageW = texture.Width; previewImageH = texture.Height; }

                float availW = ImGui.GetContentRegionAvail().X;
                float availH = ImGui.GetContentRegionAvail().Y;
                float horizMargin = 28f * scale;
                float vertMargin = 28f * scale;
                // Square box scaled to the smaller axis (minus margins) so it
                // takes the full preview pane without overflowing either way.
                float boxSide = MathF.Max(80f * scale,
                    MathF.Min(availW - horizMargin, availH - vertMargin));

                var dl = ImGui.GetWindowDrawList();
                float boxX = ImGui.GetCursorScreenPos().X + (availW - boxSide) * 0.5f;
                float boxY = ImGui.GetCursorScreenPos().Y;
                var boxMin = new Vector2(boxX, boxY);
                var boxMax = boxMin + new Vector2(boxSide, boxSide);

                // Slip-polygon (chamfered) outer fill
                Span<Vector2> outer = stackalloc Vector2[6];
                Boutique.BuildSlipPolygon(boxMin, boxMax, 8f * scale, outer);
                unsafe
                {
                    fixed (Vector2* p = outer)
                        dl.AddConvexPolyFilled(p, 6, Boutique.U32(new Vector4(0.078f, 0.094f, 0.125f, 1f)));
                }
                for (int k = 0; k < 6; k++) dl.PathLineTo(outer[k]);
                dl.PathStroke(Boutique.U32(Boutique.BorderSoft), ImDrawFlags.Closed, 1f * scale);

                // Inner image, fit-to-box, axis-aligned (clip rect for chamfer corners)
                float inset = 6f * scale;
                var imgRectMin = boxMin + new Vector2(inset, inset);
                var imgRectMax = boxMax - new Vector2(inset, inset);
                float imgW = imgRectMax.X - imgRectMin.X;
                float imgH = imgRectMax.Y - imgRectMin.Y;
                float texAR = (float)texture.Width / texture.Height;
                float fitW, fitH;
                if (texAR >= 1f) { fitW = imgW; fitH = imgW / texAR; }
                else            { fitH = imgH; fitW = imgH * texAR; }
                var fitMin = imgRectMin + new Vector2((imgW - fitW) * 0.5f, (imgH - fitH) * 0.5f);
                var fitMax = fitMin + new Vector2(fitW, fitH);
                dl.PushClipRect(imgRectMin, imgRectMax, true);
                dl.AddImage(texture.Handle, fitMin, fitMax);
                dl.PopClipRect();

                // Gold gilt inset frame (2nd inset, gold-at-25%)
                Span<Vector2> gilt = stackalloc Vector2[6];
                var giltMin = boxMin + new Vector2(4f * scale, 4f * scale);
                var giltMax = boxMax - new Vector2(4f * scale, 4f * scale);
                Boutique.BuildSlipPolygon(giltMin, giltMax, 5f * scale, gilt);
                for (int k = 0; k < 6; k++) dl.PathLineTo(gilt[k]);
                dl.PathStroke(Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.25f)),
                    ImDrawFlags.Closed, 1f * scale);

                ImGui.Dummy(new Vector2(0, boxSide + 14f * scale));
            }

            ImGui.EndChild();
        }

        private void DrawPreviewMeta(float scale)
        {
            string fileName = Path.GetFileName(previewPath ?? "");
            string sizeStr = previewFileSize switch
            {
                < 1024 => $"{previewFileSize}B",
                < 1024 * 1024 => $"{previewFileSize / 1024} KB",
                _ => $"{previewFileSize / (1024.0 * 1024.0):F1} MB"
            };
            string dimStr = (previewImageW > 0 && previewImageH > 0)
                ? $"{previewImageW} × {previewImageH}"
                : ",";
            string ageStr = previewFileModified == DateTime.MinValue
                ? ","
                : RelativeTime(previewFileModified);
            string formatStr = (Path.GetExtension(previewPath ?? "") ?? "")
                .TrimStart('.').ToUpperInvariant();
            if (string.IsNullOrEmpty(formatStr)) formatStr = ",";

            var metaPairs = new (string key, string val)[]
            {
                ("FILE",     fileName),
                ("SIZE",     sizeStr),
                ("DIM",      dimStr),
                ("MODIFIED", ageStr),
                ("FORMAT",   formatStr),
            };

            var dl = ImGui.GetWindowDrawList();
            float padX = 14f * scale;
            float padY = 12f * scale;
            float rowH = 24f * scale;
            float blockW = ImGui.GetContentRegionAvail().X - 28f * scale;
            float blockH = padY * 2 + rowH * metaPairs.Length;

            float startX = ImGui.GetCursorScreenPos().X + 14f * scale;
            float startY = ImGui.GetCursorScreenPos().Y;
            var blockMin = new Vector2(startX, startY);
            var blockMax = blockMin + new Vector2(blockW, blockH);

            dl.AddRectFilled(blockMin, blockMax,
                Boutique.U32(new Vector4(0.078f, 0.094f, 0.125f, 0.5f)));
            dl.AddRectFilled(blockMin, new Vector2(blockMin.X + 2f * scale, blockMax.Y),
                Boutique.U32(Boutique.GoldDeep));

            using (Plugin.Instance?.OswaldSemi12?.Push())
            {
                float trackPx = ImGui.GetFontSize() * 0.22f;
                float fontH = ImGui.GetFontSize();
                for (int i = 0; i < metaPairs.Length; i++)
                {
                    var (key, val) = metaPairs[i];
                    float rowY = blockMin.Y + padY + i * rowH;
                    float textY = rowY + (rowH - fontH) * 0.5f;

                    // Key on left, brighter than before so it actually reads
                    CharacterSelectPlugin.Windows.Styles.Boutique.DrawTrackedText(
                        dl, new Vector2(blockMin.X + 14f * scale, textY),
                        key, Boutique.U32(Boutique.Text), trackPx);

                    // Val on right (truncated if too long)
                    string display = (val ?? "").ToUpperInvariant();
                    float maxValW = blockMax.X - 14f * scale - (blockMin.X + 14f * scale + 60f * scale);
                    float valW = CharacterSelectPlugin.Windows.Styles.BoutiqueChassis
                        .MeasureTrackedText(display, trackPx);
                    if (valW > maxValW && display.Length > 1)
                    {
                        const string ell = "...";
                        float ellW = CharacterSelectPlugin.Windows.Styles.BoutiqueChassis
                            .MeasureTrackedText(ell, trackPx);
                        for (int k = display.Length - 1; k > 0; k--)
                        {
                            var trunc = display.Substring(0, k);
                            float w = CharacterSelectPlugin.Windows.Styles.BoutiqueChassis
                                .MeasureTrackedText(trunc, trackPx);
                            if (w + ellW <= maxValW)
                            {
                                display = trunc + ell;
                                valW = w + ellW;
                                break;
                            }
                        }
                    }
                    CharacterSelectPlugin.Windows.Styles.Boutique.DrawTrackedText(
                        dl, new Vector2(blockMax.X - 14f * scale - valW, textY),
                        display, Boutique.U32(Boutique.GoldWarm), trackPx);
                }
            }
            ImGui.Dummy(new Vector2(0, blockH + 8f * scale));
        }

        private static string RelativeTime(DateTime t)
        {
            var span = DateTime.Now - t;
            if (span.TotalMinutes < 1) return "JUST NOW";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}M AGO";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}H AGO";
            if (span.TotalDays < 30) return $"{(int)span.TotalDays}D AGO";
            if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}MO AGO";
            return $"{(int)(span.TotalDays / 365)}Y AGO";
        }

        // (Old DrawBottomBar replaced by DrawBoutiqueBottomBar)

        private void ConfirmSelection()
        {
            if (selectedFile != null)
            {
                Confirmed = true;
                SelectedPath = selectedFile;

                // Remember last used directory
                if (configuration != null)
                {
                    configuration.LastBrowserDirectory = currentDirectory;
                    configuration.Save();
                }

                OnFileSelected?.Invoke(selectedFile);
                Plugin.Instance?.AchievementTracker?.OnFileBrowserUsed();
                IsOpen = false;
            }
        }

        public void Open(string? startDirectory = null)
        {
            Confirmed = false;
            SelectedPath = null;
            selectedFile = null;
            previewPath = null;
            searchFilter = "";

            if (!string.IsNullOrEmpty(startDirectory) && Directory.Exists(startDirectory))
                currentDirectory = startDirectory;
            else if (!string.IsNullOrEmpty(configuration?.LastBrowserDirectory) &&
                     Directory.Exists(configuration.LastBrowserDirectory))
                currentDirectory = configuration.LastBrowserDirectory;

            RefreshDirectory();
            IsOpen = true;
        }

        public override void OnClose()
        {
            base.OnClose();
        }
    }
}

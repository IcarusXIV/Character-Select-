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
    public class ImGuiFileBrowserWindow : Window
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
        private readonly string[] allowedExtensions;
        private readonly List<string> quickAccessPaths = new();
        private readonly List<string> recentDirectories = new();
        private string pathInput = "";

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
            Flags = ImGuiWindowFlags.NoDocking;

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
            if (Directory.Exists(path))
            {
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

        public override void Draw()
        {
            // Push dark styling
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.12f, 0.12f, 0.12f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.2f, 0.4f, 0.6f, 0.6f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.3f, 0.5f, 0.7f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.25f, 0.45f, 0.65f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);

            DrawPathBar();
            ImGui.Spacing();

            var contentHeight = ImGui.GetContentRegionAvail().Y - 45;

            // Left panel - Quick access
            ImGui.BeginChild("LeftPanel", new Vector2(180, contentHeight), true);
            DrawQuickAccess();
            ImGui.EndChild();

            ImGui.SameLine();

            // Middle panel - File list with fixed header
            ImGui.BeginGroup();

            // Fixed header (no scrolling)
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.1f, 0.1f, 0.12f, 1f));
            ImGui.BeginChild("FileListHeader", new Vector2(340, 32), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            DrawFileListHeader();
            ImGui.EndChild();
            ImGui.PopStyleColor();

            // Scrolling file list
            ImGui.BeginChild("FileList", new Vector2(340, contentHeight - 34), true);
            DrawFileListContent();
            ImGui.EndChild();

            ImGui.EndGroup();

            ImGui.SameLine();

            // Right panel - Preview
            ImGui.BeginChild("Preview", new Vector2(0, contentHeight), true);
            DrawPreview();
            ImGui.EndChild();

            ImGui.Spacing();
            DrawBottomBar();

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(5);
        }

        private void DrawPathBar()
        {
            // Navigation buttons
            ImGui.PushFont(UiBuilder.IconFont);

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.25f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.35f, 1f));

            if (ImGui.Button(FontAwesomeIcon.ArrowUp.ToIconString(), new Vector2(30, 26)))
                NavigateUp();
            ImGui.PopFont();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Go up one directory");

            ImGui.SameLine();

            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button(FontAwesomeIcon.SyncAlt.ToIconString(), new Vector2(30, 26)))
                RefreshDirectory();
            ImGui.PopFont();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Refresh");

            ImGui.PopStyleColor(2);

            ImGui.SameLine();

            // Path input - lighter background to show it's editable
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.18f, 0.18f, 0.2f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.22f, 0.22f, 0.25f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.25f, 0.25f, 0.28f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.3f, 0.3f, 0.35f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 180);
            if (ImGui.InputText("##PathInput", ref pathInput, 1024, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                if (Directory.Exists(pathInput))
                    NavigateTo(pathInput);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Type a path and press Enter to navigate");

            ImGui.PopStyleVar();
            ImGui.PopStyleColor(4);

            ImGui.SameLine();

            // Search - more space now
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(new Vector4(0.6f, 0.7f, 0.85f, 1f), FontAwesomeIcon.Search.ToIconString());
            ImGui.PopFont();
            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.18f, 0.18f, 0.2f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.22f, 0.22f, 0.25f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.3f, 0.3f, 0.35f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            ImGui.SetNextItemWidth(145);
            ImGui.InputTextWithHint("##Search", "Filter files...", ref searchFilter, 100);
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
        }

        private void DrawQuickAccess()
        {
            // Header
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(ColorSchemes.Dark.AccentBlue, FontAwesomeIcon.Star.ToIconString());
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.TextColored(ColorSchemes.Dark.AccentBlue, "Quick Access");

            ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.3f, 0.5f, 0.7f, 0.5f));
            ImGui.Separator();
            ImGui.PopStyleColor();
            ImGui.Spacing();

            foreach (var path in quickAccessPaths)
            {
                var name = GetQuickAccessName(path);
                var icon = GetPathIcon(path);
                var isSelected = currentDirectory == path;

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextColored(isSelected ? ColorSchemes.Dark.AccentBlue : new Vector4(0.6f, 0.7f, 0.85f, 1f),
                    icon.ToIconString());
                ImGui.PopFont();
                ImGui.SameLine();

                if (ImGui.Selectable(name, isSelected))
                    NavigateTo(path);
            }

            // Pinned folders
            var pinnedPaths = configuration?.PinnedFileBrowserPaths;
            if (pinnedPaths != null && pinnedPaths.Count > 0)
            {
                ImGui.Spacing();
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1f), FontAwesomeIcon.Thumbtack.ToIconString());
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1f), "Pinned");

                ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.9f, 0.7f, 0.3f, 0.3f));
                ImGui.Separator();
                ImGui.PopStyleColor();

                string? pathToRemove = null;
                for (int i = 0; i < pinnedPaths.Count; i++)
                {
                    var pinPath = pinnedPaths[i];
                    if (!Directory.Exists(pinPath)) continue;

                    var pinName = Path.GetFileName(pinPath);
                    if (string.IsNullOrEmpty(pinName)) pinName = pinPath;
                    var isSelected2 = currentDirectory == pinPath;

                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.TextColored(isSelected2 ? ColorSchemes.Dark.AccentBlue : new Vector4(0.9f, 0.7f, 0.3f, 0.8f),
                        FontAwesomeIcon.Folder.ToIconString());
                    ImGui.PopFont();
                    ImGui.SameLine();

                    if (ImGui.Selectable($"{pinName}##pin{i}", isSelected2))
                        NavigateTo(pinPath);

                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(pinPath);

                    // Right-click to unpin
                    if (ImGui.BeginPopupContextItem($"##pinctx{i}"))
                    {
                        if (ImGui.MenuItem("Unpin from Quick Access"))
                            pathToRemove = pinPath;
                        ImGui.EndPopup();
                    }
                }

                if (pathToRemove != null)
                    TogglePin(pathToRemove);
            }

            // Recent section
            if (recentDirectories.Count > 0)
            {
                ImGui.Spacing();
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), FontAwesomeIcon.History.ToIconString());
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Recent");

                ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.4f, 0.4f, 0.4f, 0.3f));
                ImGui.Separator();
                ImGui.PopStyleColor();

                foreach (var path in recentDirectories.Take(5))
                {
                    if (!Directory.Exists(path)) continue;
                    var name = Path.GetFileName(path);
                    if (string.IsNullOrEmpty(name)) name = path;

                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
                    if (ImGui.Selectable($"  {name}##recent{path}", currentDirectory == path))
                        NavigateTo(path);
                    ImGui.PopStyleColor();

                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(path);
                }
            }
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

        private void DrawFileListHeader()
        {
            // Vertical positioning
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 1);

            // Add left padding
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 6);

            // Sort header - fixed at top
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(ColorSchemes.Dark.AccentBlue, FontAwesomeIcon.FolderOpen.ToIconString());
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.TextColored(ColorSchemes.Dark.AccentBlue, "Files");
            ImGui.SameLine();

            // Right-align the sort controls
            var sortControlsWidth = 145f;
            ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - sortControlsWidth);

            // Sort dropdown
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.15f, 0.15f, 0.18f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.2f, 0.2f, 0.23f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.15f, 0.18f, 1f));
            ImGui.SetNextItemWidth(100);
            var sortIndex = (int)currentSort;
            if (ImGui.Combo("##Sort", ref sortIndex, SortOptionNames, SortOptionNames.Length))
            {
                currentSort = (SortOption)sortIndex;
                RefreshDirectory();
            }
            ImGui.PopStyleColor(3);

            ImGui.SameLine();

            // Sort direction button
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.25f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.35f, 1f));
            ImGui.PushFont(UiBuilder.IconFont);
            var sortIcon = sortDescending ? FontAwesomeIcon.SortAmountDown : FontAwesomeIcon.SortAmountUp;
            if (ImGui.Button(sortIcon.ToIconString(), new Vector2(26, 0)))
            {
                sortDescending = !sortDescending;
                RefreshDirectory();
            }
            ImGui.PopFont();
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(sortDescending ? "Descending" : "Ascending");
        }

        private void DrawFileListContent()
        {
            // Directories
            for (int di = 0; di < currentDirectories.Length; di++)
            {
                var dir = currentDirectories[di];
                var name = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(searchFilter) &&
                    !name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var pinned = IsPinned(dir);

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextColored(pinned ? new Vector4(0.9f, 0.7f, 0.3f, 1f) : new Vector4(1f, 0.85f, 0.4f, 1f),
                    FontAwesomeIcon.Folder.ToIconString());
                ImGui.PopFont();
                ImGui.SameLine();

                if (ImGui.Selectable($"{name}##dir{di}", false, ImGuiSelectableFlags.AllowDoubleClick))
                {
                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        NavigateTo(dir);
                }

                // Right-click context menu
                if (ImGui.BeginPopupContextItem($"##dirctx{di}"))
                {
                    if (ImGui.MenuItem("Open"))
                        NavigateTo(dir);

                    if (pinned)
                    {
                        if (ImGui.MenuItem("Unpin from Quick Access"))
                            TogglePin(dir);
                    }
                    else
                    {
                        if (ImGui.MenuItem("Pin to Quick Access"))
                            TogglePin(dir);
                    }
                    ImGui.EndPopup();
                }
            }

            // Files
            foreach (var file in currentFiles)
            {
                var name = Path.GetFileName(file);
                if (!string.IsNullOrEmpty(searchFilter) &&
                    !name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), FontAwesomeIcon.FileImage.ToIconString());
                ImGui.PopFont();
                ImGui.SameLine();

                var isSelected = selectedFile == file;
                if (ImGui.Selectable(name, isSelected, ImGuiSelectableFlags.AllowDoubleClick))
                {
                    selectedFile = file;
                    previewPath = file;

                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        ConfirmSelection();
                }
            }

            if (currentFiles.Length == 0 && currentDirectories.Length == 0)
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No files found");
            }
        }

        private void DrawPreview()
        {
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(ColorSchemes.Dark.AccentBlue, FontAwesomeIcon.Eye.ToIconString());
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.TextColored(ColorSchemes.Dark.AccentBlue, "Preview");

            ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.3f, 0.5f, 0.7f, 0.5f));
            ImGui.Separator();
            ImGui.PopStyleColor();

            if (string.IsNullOrEmpty(previewPath))
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Select a file to preview");
                return;
            }

            // File info
            var fileName = Path.GetFileName(previewPath);
            ImGui.TextWrapped(fileName);

            try
            {
                var fileInfo = new FileInfo(previewPath);
                var sizeStr = fileInfo.Length < 1024 * 1024
                    ? $"{fileInfo.Length / 1024.0:F1} KB"
                    : $"{fileInfo.Length / (1024.0 * 1024.0):F1} MB";
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), sizeStr);
            }
            catch { }

            ImGui.Spacing();

            // Load texture fresh each frame (like other components do)
            if (File.Exists(previewPath))
            {
                var texture = Plugin.TextureProvider?.GetFromFile(previewPath).GetWrapOrDefault();

                if (texture != null)
                {
                    var availSize = ImGui.GetContentRegionAvail();
                    var texSize = new Vector2(texture.Width, texture.Height);

                    var scale = Math.Min(availSize.X / texSize.X, (availSize.Y - 10) / texSize.Y);
                    scale = Math.Min(scale, 1f);
                    var displaySize = texSize * scale;

                    var offsetX = (availSize.X - displaySize.X) / 2;
                    if (offsetX > 0)
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);

                    // Draw border around image
                    var pos = ImGui.GetCursorScreenPos();
                    var drawList = ImGui.GetWindowDrawList();
                    drawList.AddRect(
                        pos - new Vector2(2, 2),
                        pos + displaySize + new Vector2(2, 2),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.5f, 0.7f, 0.5f)),
                        4f, ImDrawFlags.None, 2f);

                    ImGui.Image(texture.Handle, displaySize);

                    // Show dimensions
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), $"{texture.Width} x {texture.Height}");
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Loading preview...");
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(0.7f, 0.4f, 0.4f, 1f), "File not found");
            }
        }

        private void DrawBottomBar()
        {
            // Selected file display
            var selectedDisplay = selectedFile != null ? Path.GetFileName(selectedFile) : "No file selected";
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), $"Selected: {selectedDisplay}");

            ImGui.SameLine();

            var buttonWidth = 90f;
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - buttonWidth * 2 - 25);

            // Select button (now first)
            var hasSelection = selectedFile != null;
            if (!hasSelection)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.5f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.2f, 0.2f, 0.5f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.4f, 0.4f, 1f));
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.3f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.5f, 0.35f, 1f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
            }

            if (ImGui.Button("Select", new Vector2(buttonWidth, 28)) && hasSelection)
                ConfirmSelection();

            ImGui.PopStyleColor(3);

            ImGui.SameLine();

            // Cancel button (now second)
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.2f, 0.2f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.25f, 0.25f, 1f));
            if (ImGui.Button("Cancel", new Vector2(buttonWidth, 28)))
            {
                Confirmed = false;
                SelectedPath = null;
                IsOpen = false;
            }
            ImGui.PopStyleColor(2);
        }

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

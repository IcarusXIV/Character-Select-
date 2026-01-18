using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;

namespace CharacterSelectPlugin.Windows
{
    public class IconPickerWindow : Window
    {
        public uint? SelectedIconId { get; private set; }
        public bool Confirmed { get; private set; }
        
        private readonly Dictionary<string, List<(uint start, uint end)>> _iconCategories;
        private readonly Dictionary<string, bool> _categoryState;
        private string _selectedCategory = "General";
        private List<uint> _currentIcons = new();
        private uint? _hoveredIcon;
        private double _lastClickTime;
        private string _searchFilter = "";
        private HashSet<uint> _favoriteIcons;
        
        public IconPickerWindow(uint? initialIcon = null) : base("Icon Picker###IconPicker")
        {
            Size = new Vector2(800, 600);
            SizeCondition = ImGuiCond.FirstUseEver;
            Flags = ImGuiWindowFlags.NoDocking;
            
            SelectedIconId = initialIcon;

            _favoriteIcons = new HashSet<uint>(Plugin.Instance?.Configuration.FavoriteIconIds ?? new List<uint>());

            _iconCategories = new Dictionary<string, List<(uint, uint)>>
            {
                {
                    "General", new List<(uint, uint)>
                    {
                        (0, 95), (101, 132), (651, 652), (654, 655), (695, 698), 
                        (66001, 66001), (66021, 66023), (66031, 66033), (66041, 66043),
                        (66051, 66053), (66061, 66063), (66071, 66073), (66081, 66083),
                        (66101, 66105), (66121, 66125), (66141, 66145), (66161, 66171),
                        (66181, 66191), (66301, 66341), (66401, 66423), (66452, 66473),
                        (60001, 60048), (60071, 60074), (61471, 61489), (61501, 61548),
                        (61551, 61598), (61751, 61768), (61801, 61850), (61875, 61880)
                    }
                },
                {
                    "Jobs", new List<(uint, uint)>
                    {
                        (62001, 62042), (62801, 62842), (62226, 62267), 
                        (62101, 62142), (62301, 62320), (62401, 62422),
                        (82271, 82286) // Phantom Jobs
                    }
                },
                {
                    "Weapons & Equipment", new List<(uint, uint)>
                    {
                        (1, 1000), (20001, 29999), (30001, 39999), (40001, 49999),
                        (50001, 59999) // Comprehensive weapon/equipment ranges
                    }
                },
                {
                    "Items & Materials", new List<(uint, uint)>
                    {
                        (5001, 5999), (25001, 25999), (35001, 35999), (45001, 45999),
                        (55001, 55999), (65001, 65127), (65130, 65134), (65137, 65137)
                    }
                },
                {
                    "Quests", new List<(uint, uint)>
                    {
                        (71001, 71006), (71021, 71025), (71041, 71045), (71061, 71065), 
                        (71081, 71085), (71101, 71102), (71121, 71125), (71141, 71145),
                        (71201, 71205), (71221, 71225), (61721, 61723), (61731, 61733),
                        (63875, 63892), (63900, 63977), (63979, 63987)
                    }
                },
                {
                    "Map Markers", new List<(uint, uint)>
                    {
                        (60401, 60408), (60412, 60482), (60501, 60508), (60511, 60515), 
                        (60550, 60565), (60567, 60583), (60585, 60611), (60640, 60649),
                        (60651, 60662), (60751, 60792), (60901, 60999)
                    }
                },
                {
                    "Minions", new List<(uint, uint)>
                    {
                        (4401, 4521), (4523, 4611), (4613, 4939), (4941, 4962), 
                        (4964, 4967), (4971, 4973), (4977, 4979),
                        (59401, 59521), (59523, 59611), (59613, 59939), (59941, 59962),
                        (59964, 59967), (59971, 59973), (59977, 59979)
                    }
                },
                {
                    "Mounts", new List<(uint, uint)>
                    {
                        (4001, 4045), (4047, 4098), (4101, 4276), (4278, 4329), 
                        (4331, 4332), (4334, 4335), (4339, 4339), (4343, 4343)
                    }
                },
                {
                    "Emotes", new List<(uint, uint)>
                    {
                        (246001, 246004), (246101, 246133), (246201, 246280), 
                        (246282, 246299), (246301, 246324), (246327, 246453),
                        (246456, 246457), (246459, 246459), (246463, 246470)
                    }
                },
                {
                    "Shapes & Symbols", new List<(uint, uint)>
                    {
                        (82091, 82093), (90001, 90004), (90200, 90263), (90401, 90463), 
                        (61901, 61918), (230131, 230143), (230201, 230215), (230301, 230317),
                        (230401, 230433), (230701, 230715), (230626, 230629), (230631, 230641),
                        (180021, 180028)
                    }
                }
            };

            _categoryState = new Dictionary<string, bool> { { "Favorites", false } };
            foreach (var key in _iconCategories.Keys)
            {
                _categoryState[key] = key == _selectedCategory;
            }
            
            LoadIconsForCategory(_selectedCategory);
        }
        
        private void LoadIconsForCategory(string category)
        {
            _currentIcons.Clear();
            
            if (category == "Favorites")
            {
                _currentIcons.AddRange(_favoriteIcons.OrderBy(i => i));
            }
            else if (_iconCategories.TryGetValue(category, out var ranges))
            {
                foreach (var (start, end) in ranges)
                {
                    for (uint i = start; i <= end; i++)
                    {
                        _currentIcons.Add(i);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(_searchFilter))
            {
                var filterLower = _searchFilter.ToLower();
                _currentIcons = _currentIcons.Where(id => 
                    id.ToString().Contains(filterLower)).ToList();
            }
        }
        
        public override void Draw()
        {
            var windowSize = ImGui.GetWindowSize();
            var buttonHeight = 30f;
            var sidebarWidth = 150f;
            var padding = 8f;

            if (ImGui.BeginChild("Categories", new Vector2(sidebarWidth, -buttonHeight - padding * 2), true))
            {
                ImGui.Text("Categories");
                ImGui.Separator();

                {
                    var isSelected = "Favorites" == _selectedCategory;
                    if (isSelected) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));

                    var buttonText = $"Favorites ({_favoriteIcons.Count})";
                    if (ImGui.Button(buttonText, new Vector2(-1, 0)))
                    {
                        _selectedCategory = "Favorites";
                        foreach (var key in _categoryState.Keys.ToList())
                            _categoryState[key] = key == "Favorites";
                        LoadIconsForCategory("Favorites");
                    }
                    
                    if (isSelected) ImGui.PopStyleColor();
                }
                
                ImGui.Separator();
                
                foreach (var category in _iconCategories.Keys)
                {
                    var isSelected = category == _selectedCategory;
                    if (isSelected) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
                    
                    if (ImGui.Button(category, new Vector2(-1, 0)))
                    {
                        _selectedCategory = category;
                        foreach (var key in _categoryState.Keys.ToList())
                            _categoryState[key] = key == category;
                        LoadIconsForCategory(category);
                    }
                    
                    if (isSelected) ImGui.PopStyleColor();
                }
            }
            ImGui.EndChild();
            
            ImGui.SameLine();

            ImGui.BeginGroup();

            ImGui.Text("Search by ID:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##search", ref _searchFilter, 50))
            {
                LoadIconsForCategory(_selectedCategory);
            }
            
            ImGui.Separator();
            ImGui.Spacing();

            var remainingHeight = ImGui.GetContentRegionAvail().Y - buttonHeight - padding * 2;
            if (ImGui.BeginChild("IconGrid", new Vector2(-1, remainingHeight), false, ImGuiWindowFlags.HorizontalScrollbar))
            {
                DrawIconGrid();
            }
            ImGui.EndChild();
            
            ImGui.EndGroup();

            ImGui.Separator();

            ImGui.Text("Selected:");
            ImGui.SameLine();
            if (SelectedIconId.HasValue)
            {
                var iconTexture = GetGameIcon(SelectedIconId.Value);
                if (iconTexture != null)
                {
                    ImGui.Image(iconTexture.Handle, new Vector2(24, 24));
                    ImGui.SameLine();
                }
                ImGui.Text($"Icon ID: {SelectedIconId.Value}");
            }
            else
            {
                ImGui.Text("None");
            }
            
            ImGui.SameLine(windowSize.X - 150);

            if (ImGui.Button("Cancel", new Vector2(70, 0)))
            {
                SelectedIconId = null;
                Confirmed = false;
                IsOpen = false;
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button("Confirm", new Vector2(70, 0)))
            {
                Confirmed = true;
                IsOpen = false;
            }
        }
        
        private void DrawIconGrid()
        {
            if (_currentIcons.Count == 0)
            {
                ImGui.Text("No icons found.");
                return;
            }
            
            var availableWidth = ImGui.GetContentRegionAvail().X;
            var iconSize = 48f;
            var spacing = 8f;
            var cellSize = iconSize + spacing;

            var iconsPerRow = Math.Max(1, (int)Math.Floor(availableWidth / cellSize));
            var actualCellWidth = availableWidth / iconsPerRow;
            
            var drawList = ImGui.GetWindowDrawList();
            var startPos = ImGui.GetCursorScreenPos();
            
            for (int i = 0; i < _currentIcons.Count; i++)
            {
                int col = i % iconsPerRow;
                int row = i / iconsPerRow;
                
                var cellPos = startPos + new Vector2(
                    col * actualCellWidth,
                    row * (cellSize + spacing)
                );
                
                var cellMin = cellPos;
                var cellMax = cellPos + new Vector2(actualCellWidth - spacing, cellSize);
                
                var iconId = _currentIcons[i];
                bool isHovered = ImGui.IsMouseHoveringRect(cellMin, cellMax);
                bool isSelected = SelectedIconId == iconId;
                bool isFavorite = _favoriteIcons.Contains(iconId);
                bool clicked = isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
                bool rightClicked = isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right);

                var bgColor = isSelected 
                    ? new Vector4(0.3f, 0.6f, 0.3f, 0.8f) 
                    : isHovered 
                        ? new Vector4(0.4f, 0.4f, 0.4f, 0.6f)
                        : new Vector4(0.2f, 0.2f, 0.2f, 0.4f);
                
                drawList.AddRectFilled(cellMin, cellMax, ImGui.ColorConvertFloat4ToU32(bgColor), 4f);

                if (isSelected || isHovered)
                {
                    var borderColor = isSelected 
                        ? new Vector4(0.4f, 0.8f, 0.4f, 1.0f)
                        : new Vector4(0.6f, 0.6f, 0.6f, 1.0f);
                    drawList.AddRect(cellMin, cellMax, ImGui.ColorConvertFloat4ToU32(borderColor), 4f, ImDrawFlags.None, 1.5f);
                }

                var iconTexture = GetGameIcon(iconId);
                if (iconTexture != null)
                {
                    var iconPos = cellMin + new Vector2(((actualCellWidth - spacing) - iconSize) / 2, (cellSize - iconSize) / 2);
                    drawList.AddImage(iconTexture.Handle, iconPos, iconPos + new Vector2(iconSize, iconSize));
                }
                else
                {
                    var fallbackText = iconId.ToString();
                    var textSize = ImGui.CalcTextSize(fallbackText);
                    var textPos = cellMin + new Vector2(
                        ((actualCellWidth - spacing) - textSize.X) / 2,
                        (cellSize - textSize.Y) / 2
                    );
                    drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.6f, 0.6f, 1.0f)), fallbackText);
                }

                if (isFavorite)
                {
                    var starPos = cellMin + new Vector2(actualCellWidth - spacing - 16f, 2f);
                    var starColor = new Vector4(1.0f, 0.84f, 0.0f, 1.0f);
                    drawList.AddText(starPos, ImGui.ColorConvertFloat4ToU32(starColor), "★");
                }

                if (clicked)
                {
                    SelectedIconId = iconId;
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    if (_lastClickTime > 0 && now - _lastClickTime < 250)
                    {
                        Confirmed = true;
                        IsOpen = false;
                    }
                    
                    _lastClickTime = now;
                }

                if (rightClicked)
                {
                    if (_favoriteIcons.Contains(iconId))
                        _favoriteIcons.Remove(iconId);
                    else
                        _favoriteIcons.Add(iconId);

                    if (Plugin.Instance?.Configuration != null)
                    {
                        Plugin.Instance.Configuration.FavoriteIconIds = _favoriteIcons.ToList();
                        Plugin.Instance.Configuration.Save();
                    }

                    if (_selectedCategory == "Favorites")
                        LoadIconsForCategory("Favorites");
                }

                if (isHovered)
                {
                    _hoveredIcon = iconId;
                    ImGui.BeginTooltip();
                    ImGui.Text($"Icon ID: {iconId}");
                    ImGui.TextDisabled(isFavorite ? "Right-click to unfavorite" : "Right-click to favorite");
                    ImGui.EndTooltip();
                }
            }

            int totalRows = (_currentIcons.Count + iconsPerRow - 1) / iconsPerRow;
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + totalRows * (cellSize + spacing));
        }
        
        private IDalamudTextureWrap? GetGameIcon(uint iconId)
        {
            try
            {
                var texture = Plugin.TextureProvider.GetFromGameIcon(iconId);
                return texture?.GetWrapOrEmpty();
            }
            catch
            {
                return null;
            }
        }
    }
}
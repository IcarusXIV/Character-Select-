using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin.Windows
{
    /// <summary>
    /// A window for picking FontAwesome icons, used by the Custom Theme editor.
    /// </summary>
    public class FontAwesomeIconPickerWindow : Window
    {
        public FontAwesomeIcon? SelectedIcon { get; private set; }
        public bool Confirmed { get; private set; }

        /// <summary>
        /// Callback invoked when the selected icon changes (for real-time preview).
        /// </summary>
        public Action<FontAwesomeIcon>? OnIconChanged { get; set; }

        private string _searchFilter = "";
        private string _selectedCategory = "Favorites";
        private double _lastClickTime;
        private FontAwesomeIcon? _initialIcon;
        private Configuration? _configuration;

        private static readonly FontAwesomeIcon[] AllIcons;
        private static readonly Dictionary<string, FontAwesomeIcon[]> IconCategories;
        private static readonly string[] CategoryOrder = {
            "Favorites", "Popular", "Symbols", "Nature", "Fantasy", "People", "Hands",
            "Media", "Food & Drink", "Arrows", "Weather", "Travel", "Gaming", "Tech",
            "Buildings", "Misc", "All Icons"
        };

        static FontAwesomeIconPickerWindow()
        {
            AllIcons = Enum.GetValues<FontAwesomeIcon>()
                .Where(i => i != FontAwesomeIcon.None && (int)i != 0)
                .OrderBy(i => i.ToString())
                .ToArray();

            IconCategories = new Dictionary<string, FontAwesomeIcon[]>
            {
                { "All Icons", AllIcons },
                { "Popular", new[] {
                    FontAwesomeIcon.Star, FontAwesomeIcon.Heart, FontAwesomeIcon.Crown, FontAwesomeIcon.Gem,
                    FontAwesomeIcon.Fire, FontAwesomeIcon.Bolt, FontAwesomeIcon.Moon, FontAwesomeIcon.Sun,
                    FontAwesomeIcon.Snowflake, FontAwesomeIcon.Leaf, FontAwesomeIcon.Feather, FontAwesomeIcon.Bookmark,
                    FontAwesomeIcon.Magic, FontAwesomeIcon.HatWizard, FontAwesomeIcon.Shield, FontAwesomeIcon.Dragon
                }},
                { "Symbols", new[] {
                    FontAwesomeIcon.Star, FontAwesomeIcon.Heart, FontAwesomeIcon.Check, FontAwesomeIcon.Times,
                    FontAwesomeIcon.Plus, FontAwesomeIcon.Minus, FontAwesomeIcon.Circle, FontAwesomeIcon.Square,
                    FontAwesomeIcon.Bell, FontAwesomeIcon.Flag, FontAwesomeIcon.Tag, FontAwesomeIcon.Thumbtack,
                    FontAwesomeIcon.Exclamation, FontAwesomeIcon.Question, FontAwesomeIcon.InfoCircle, FontAwesomeIcon.ExclamationTriangle,
                    FontAwesomeIcon.Ban, FontAwesomeIcon.Certificate, FontAwesomeIcon.Award, FontAwesomeIcon.Medal
                }},
                { "Nature", new[] {
                    FontAwesomeIcon.Sun, FontAwesomeIcon.Moon, FontAwesomeIcon.Star, FontAwesomeIcon.Cloud,
                    FontAwesomeIcon.Leaf, FontAwesomeIcon.Snowflake, FontAwesomeIcon.Fire, FontAwesomeIcon.Water,
                    FontAwesomeIcon.Tree, FontAwesomeIcon.Mountain, FontAwesomeIcon.Feather, FontAwesomeIcon.Paw,
                    FontAwesomeIcon.Dragon, FontAwesomeIcon.Cat, FontAwesomeIcon.Dog, FontAwesomeIcon.Horse,
                    FontAwesomeIcon.Dove, FontAwesomeIcon.Crow, FontAwesomeIcon.Fish, FontAwesomeIcon.Bug,
                    FontAwesomeIcon.Spider, FontAwesomeIcon.Frog, FontAwesomeIcon.Hippo, FontAwesomeIcon.Otter
                }},
                { "Fantasy", new[] {
                    FontAwesomeIcon.Crown, FontAwesomeIcon.Gem, FontAwesomeIcon.Shield, FontAwesomeIcon.Skull,
                    FontAwesomeIcon.Book, FontAwesomeIcon.Scroll, FontAwesomeIcon.Flask, FontAwesomeIcon.Magic,
                    FontAwesomeIcon.Dice, FontAwesomeIcon.DiceD20, FontAwesomeIcon.DiceD6, FontAwesomeIcon.Key,
                    FontAwesomeIcon.Lock, FontAwesomeIcon.Unlock, FontAwesomeIcon.Gift, FontAwesomeIcon.Bomb,
                    FontAwesomeIcon.Crosshairs, FontAwesomeIcon.Palette, FontAwesomeIcon.Dragon, FontAwesomeIcon.Ghost,
                    FontAwesomeIcon.HatWizard, FontAwesomeIcon.Dungeon, FontAwesomeIcon.Ring,
                    FontAwesomeIcon.Khanda, FontAwesomeIcon.SkullCrossbones, FontAwesomeIcon.Ankh, FontAwesomeIcon.Cross
                }},
                { "People", new[] {
                    FontAwesomeIcon.User, FontAwesomeIcon.Users, FontAwesomeIcon.UserFriends, FontAwesomeIcon.UserCircle,
                    FontAwesomeIcon.Child, FontAwesomeIcon.Ghost, FontAwesomeIcon.Mask, FontAwesomeIcon.HandPaper,
                    FontAwesomeIcon.Eye, FontAwesomeIcon.Smile, FontAwesomeIcon.Frown, FontAwesomeIcon.Meh,
                    FontAwesomeIcon.Grin, FontAwesomeIcon.GrinHearts, FontAwesomeIcon.GrinStars, FontAwesomeIcon.Angry,
                    FontAwesomeIcon.Dizzy, FontAwesomeIcon.Tired, FontAwesomeIcon.Laugh, FontAwesomeIcon.Robot
                }},
                { "Hands", new[] {
                    FontAwesomeIcon.HandPaper, FontAwesomeIcon.HandRock, FontAwesomeIcon.HandScissors, FontAwesomeIcon.HandPeace,
                    FontAwesomeIcon.HandPointUp, FontAwesomeIcon.HandPointDown, FontAwesomeIcon.HandPointLeft, FontAwesomeIcon.HandPointRight,
                    FontAwesomeIcon.Handshake, FontAwesomeIcon.ThumbsUp, FontAwesomeIcon.ThumbsDown, FontAwesomeIcon.HandMiddleFinger,
                    FontAwesomeIcon.Hands, FontAwesomeIcon.HandsHelping, FontAwesomeIcon.PrayingHands, FontAwesomeIcon.HandSpock
                }},
                { "Media", new[] {
                    FontAwesomeIcon.Music, FontAwesomeIcon.Microphone, FontAwesomeIcon.Headphones, FontAwesomeIcon.Guitar,
                    FontAwesomeIcon.Camera, FontAwesomeIcon.Video, FontAwesomeIcon.Film, FontAwesomeIcon.Play,
                    FontAwesomeIcon.Pause, FontAwesomeIcon.Stop, FontAwesomeIcon.PaintBrush, FontAwesomeIcon.Pen,
                    FontAwesomeIcon.Palette, FontAwesomeIcon.Image, FontAwesomeIcon.Images, FontAwesomeIcon.PhotoVideo
                }},
                { "Food & Drink", new[] {
                    FontAwesomeIcon.Coffee, FontAwesomeIcon.MugHot, FontAwesomeIcon.GlassMartini, FontAwesomeIcon.Utensils,
                    FontAwesomeIcon.Beer, FontAwesomeIcon.WineBottle, FontAwesomeIcon.Cocktail, FontAwesomeIcon.GlassCheers,
                    FontAwesomeIcon.Lemon, FontAwesomeIcon.Carrot, FontAwesomeIcon.Cookie,
                    FontAwesomeIcon.IceCream, FontAwesomeIcon.CandyCane, FontAwesomeIcon.PizzaSlice, FontAwesomeIcon.Hamburger
                }},
                { "Arrows", new[] {
                    FontAwesomeIcon.ArrowUp, FontAwesomeIcon.ArrowDown, FontAwesomeIcon.ArrowLeft, FontAwesomeIcon.ArrowRight,
                    FontAwesomeIcon.ChevronUp, FontAwesomeIcon.ChevronDown, FontAwesomeIcon.ChevronLeft, FontAwesomeIcon.ChevronRight,
                    FontAwesomeIcon.AngleDoubleUp, FontAwesomeIcon.AngleDoubleDown, FontAwesomeIcon.AngleDoubleLeft, FontAwesomeIcon.AngleDoubleRight,
                    FontAwesomeIcon.SyncAlt, FontAwesomeIcon.Sync, FontAwesomeIcon.Redo, FontAwesomeIcon.Undo
                }},
                { "Weather", new[] {
                    FontAwesomeIcon.Sun, FontAwesomeIcon.Moon, FontAwesomeIcon.Cloud, FontAwesomeIcon.CloudSun,
                    FontAwesomeIcon.CloudMoon, FontAwesomeIcon.CloudRain, FontAwesomeIcon.CloudShowersHeavy, FontAwesomeIcon.Snowflake,
                    FontAwesomeIcon.Bolt, FontAwesomeIcon.Wind, FontAwesomeIcon.Rainbow, FontAwesomeIcon.Umbrella,
                    FontAwesomeIcon.Smog, FontAwesomeIcon.Tornado, FontAwesomeIcon.TemperatureLow, FontAwesomeIcon.TemperatureHigh
                }},
                { "Travel", new[] {
                    FontAwesomeIcon.Plane, FontAwesomeIcon.Car, FontAwesomeIcon.Bus, FontAwesomeIcon.Train,
                    FontAwesomeIcon.Ship, FontAwesomeIcon.Rocket, FontAwesomeIcon.Bicycle, FontAwesomeIcon.Motorcycle,
                    FontAwesomeIcon.Helicopter, FontAwesomeIcon.Taxi, FontAwesomeIcon.Truck, FontAwesomeIcon.Anchor,
                    FontAwesomeIcon.Compass, FontAwesomeIcon.Map, FontAwesomeIcon.Globe, FontAwesomeIcon.GlobeAmericas
                }},
                { "Gaming", new[] {
                    FontAwesomeIcon.Gamepad, FontAwesomeIcon.Chess, FontAwesomeIcon.ChessKnight, FontAwesomeIcon.ChessQueen,
                    FontAwesomeIcon.ChessRook, FontAwesomeIcon.ChessBishop, FontAwesomeIcon.ChessPawn, FontAwesomeIcon.ChessKing,
                    FontAwesomeIcon.PuzzlePiece, FontAwesomeIcon.Dice, FontAwesomeIcon.DiceD20, FontAwesomeIcon.DiceD6,
                    FontAwesomeIcon.Ghost, FontAwesomeIcon.Headset, FontAwesomeIcon.VrCardboard
                }},
                { "Tech", new[] {
                    FontAwesomeIcon.Desktop, FontAwesomeIcon.Laptop, FontAwesomeIcon.Mobile, FontAwesomeIcon.Tablet,
                    FontAwesomeIcon.Server, FontAwesomeIcon.Database, FontAwesomeIcon.Wifi, FontAwesomeIcon.Rss,
                    FontAwesomeIcon.Microchip, FontAwesomeIcon.Memory, FontAwesomeIcon.Hdd, FontAwesomeIcon.Keyboard,
                    FontAwesomeIcon.Mouse, FontAwesomeIcon.Print, FontAwesomeIcon.Plug, FontAwesomeIcon.Robot
                }},
                { "Buildings", new[] {
                    FontAwesomeIcon.Home, FontAwesomeIcon.Building, FontAwesomeIcon.Church, FontAwesomeIcon.Hospital,
                    FontAwesomeIcon.School, FontAwesomeIcon.University, FontAwesomeIcon.Warehouse, FontAwesomeIcon.Store,
                    FontAwesomeIcon.Hotel, FontAwesomeIcon.Landmark, FontAwesomeIcon.Monument, FontAwesomeIcon.Mosque,
                    FontAwesomeIcon.Synagogue, FontAwesomeIcon.Industry, FontAwesomeIcon.City, FontAwesomeIcon.Dungeon
                }},
                { "Misc", new[] {
                    FontAwesomeIcon.Hourglass, FontAwesomeIcon.Clock, FontAwesomeIcon.Calendar, FontAwesomeIcon.Stopwatch,
                    FontAwesomeIcon.Comment, FontAwesomeIcon.Comments, FontAwesomeIcon.Envelope, FontAwesomeIcon.Phone,
                    FontAwesomeIcon.Fingerprint, FontAwesomeIcon.Barcode, FontAwesomeIcon.Qrcode, FontAwesomeIcon.Hashtag,
                    FontAwesomeIcon.At, FontAwesomeIcon.Percent, FontAwesomeIcon.Infinity, FontAwesomeIcon.Om
                }}
            };
        }

        public FontAwesomeIconPickerWindow(FontAwesomeIcon? initialIcon = null, Configuration? configuration = null)
            : base("Select Icon###FontAwesomeIconPicker")
        {
            Size = new Vector2(500, 450);
            SizeCondition = ImGuiCond.FirstUseEver;
            Flags = ImGuiWindowFlags.NoDocking;

            _initialIcon = initialIcon;
            SelectedIcon = initialIcon ?? FontAwesomeIcon.Star;
            _configuration = configuration;

            if (_configuration?.FavoriteIconIds?.Count > 0)
                _selectedCategory = "Favorites";
            else
                _selectedCategory = "Popular";
        }

        private FontAwesomeIcon[] GetFavoriteIcons()
        {
            if (_configuration?.FavoriteIconIds == null || _configuration.FavoriteIconIds.Count == 0)
                return Array.Empty<FontAwesomeIcon>();

            return _configuration.FavoriteIconIds
                .Select(id => (FontAwesomeIcon)id)
                .Where(icon => Enum.IsDefined(typeof(FontAwesomeIcon), icon))
                .ToArray();
        }

        private bool IsIconFavorite(FontAwesomeIcon icon)
        {
            return _configuration?.FavoriteIconIds?.Contains((uint)icon) ?? false;
        }

        private void ToggleFavorite(FontAwesomeIcon icon)
        {
            if (_configuration == null) return;

            _configuration.FavoriteIconIds ??= new List<uint>();

            var iconId = (uint)icon;
            if (_configuration.FavoriteIconIds.Contains(iconId))
            {
                _configuration.FavoriteIconIds.Remove(iconId);
            }
            else
            {
                _configuration.FavoriteIconIds.Add(iconId);
            }
            _configuration.Save();
        }

        // PreDraw runs BEFORE Dalamud calls ImGui.Begin for the window, so
        // pushes here actually colour the window frame / border / padding gap.
        // Pushing inside Draw() only colours the content area and lets the
        // outer Dalamud-managed strip stay grey.
        private int _chromeColorCount = 0;
        public override void PreDraw()
        {
            // Chrome (WindowBg / TitleBg / TitleBgActive / MenuBarBg) follows
            // the active theme. Border + scrollbar accents stay gold.
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
            ImGui.PushStyleColor(ImGuiCol.PopupBg, Boutique.RibbonBot);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 0f);
        }

        public override void PostDraw()
        {
            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor(10);
            CharacterSelectPlugin.Windows.Styles.ThemeHelper.PopWindowChromeColors(_chromeColorCount);
            _chromeColorCount = 0;
        }

        public override void Draw()
        {
            // Inner content colours (frames, buttons, separators, text).
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0x08 / 255f, 0x0A / 255f, 0x0E / 255f, 0.65f));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0x08 / 255f, 0x0A / 255f, 0x0E / 255f, 0.85f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0x10 / 255f, 0x12 / 255f, 0x18 / 255f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0x14 / 255f, 0x16 / 255f, 0x1C / 255f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0x10 / 255f, 0x10 / 255f, 0x12 / 255f, 0.90f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Boutique.WithAlpha(Boutique.Gold, 0.15f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Boutique.WithAlpha(Boutique.Gold, 0.25f));
            ImGui.PushStyleColor(ImGuiCol.Separator, Boutique.WithAlpha(Boutique.Gold, 0.25f));
            ImGui.PushStyleColor(ImGuiCol.Text, Boutique.Text);
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, Boutique.TextFaint);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 0f);

            try
            {
                DrawBoutiqueContent();
            }
            finally
            {
                ImGui.PopStyleVar(2);
                ImGui.PopStyleColor(10);
            }
        }

        private void DrawBoutiqueContent()
        {
            var windowSize = ImGui.GetWindowSize();
            var buttonHeight = 30f;
            var sidebarWidth = 120f;
            var padding = 8f;

            if (ImGui.BeginChild("Categories", new Vector2(sidebarWidth, -buttonHeight - padding * 2), true))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Boutique.GoldWarm);
                ImGui.Text("CATEGORIES");
                ImGui.PopStyleColor();
                ImGui.Separator();

                foreach (var category in CategoryOrder)
                {
                    if (category == "Favorites" && GetFavoriteIcons().Length == 0 && _selectedCategory != "Favorites")
                        continue;

                    var isSelected = category == _selectedCategory;
                    if (isSelected)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, Boutique.WithAlpha(Boutique.Gold, 0.18f));
                        ImGui.PushStyleColor(ImGuiCol.Text, Boutique.GoldWarm);
                    }

                    if (ImGui.Button(category == "Favorites" ? "Favorites" : category, new Vector2(-1, 0)))
                        _selectedCategory = category;

                    if (category == "Favorites")
                    {
                        var buttonMin = ImGui.GetItemRectMin();
                        var drawList = ImGui.GetWindowDrawList();
                        ImGui.PushFont(UiBuilder.IconFont);
                        var starStr = FontAwesomeIcon.Star.ToIconString();
                        var starSize = ImGui.CalcTextSize(starStr);
                        ImGui.PopFont();
                        drawList.AddText(UiBuilder.IconFont, 12f,
                            buttonMin + new Vector2(4, (ImGui.GetItemRectSize().Y - starSize.Y) / 2 + 1),
                            ImGui.ColorConvertFloat4ToU32(Boutique.GoldWarm),
                            starStr);
                    }

                    if (isSelected)
                        ImGui.PopStyleColor(2);
                }
            }
            ImGui.EndChild();

            ImGui.SameLine();

            ImGui.BeginGroup();

            ImGui.PushStyleColor(ImGuiCol.Text, Boutique.TextDim);
            ImGui.Text("Search:");
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##search", "Type to filter icons...", ref _searchFilter, 50);

            ImGui.Separator();
            ImGui.Spacing();

            var remainingHeight = ImGui.GetContentRegionAvail().Y - buttonHeight - padding * 2;
            if (ImGui.BeginChild("IconGrid", new Vector2(-1, remainingHeight), true))
            {
                DrawIconGrid();
            }
            ImGui.EndChild();

            ImGui.EndGroup();

            ImGui.Separator();

            ImGui.PushStyleColor(ImGuiCol.Text, Boutique.TextDim);
            ImGui.Text("Selected:");
            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (SelectedIcon.HasValue)
            {
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.PushStyleColor(ImGuiCol.Text, Boutique.GoldWarm);
                ImGui.Text(SelectedIcon.Value.ToIconString());
                ImGui.PopStyleColor();
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, Boutique.TextDim);
                ImGui.Text($"({SelectedIcon.Value})");
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Boutique.TextFaint);
                ImGui.Text("None");
                ImGui.PopStyleColor();
            }

            // Right-aligned action buttons (Cancel ghost + Confirm primary).
            // Use SetCursorPosX with a generous fixed estimate for the button
            // pair so Confirm always lands inside the visible content region.
            // Previous attempt measured text without the boutique font pushed,
            // which under-counted the buttons and pushed Confirm off-screen.
            float scale = 1f;
            float gap = 10f;
            float rightPad = 4f;
            const float estimatedButtonsW = 200f; // tightened: ~85 + 10 + 105
            ImGui.SameLine();
            float maxX = ImGui.GetContentRegionMax().X;
            float curX = ImGui.GetCursorPosX();
            float targetX = maxX - estimatedButtonsW - rightPad;
            if (targetX > curX)
                ImGui.SetCursorPosX(targetX);

            if (Boutique.GhostButton("iconpicker.cancel", "CANCEL", scale))
            {
                SelectedIcon = _initialIcon;
                if (_initialIcon.HasValue)
                    OnIconChanged?.Invoke(_initialIcon.Value);
                Confirmed = false;
                IsOpen = false;
            }

            ImGui.SameLine(0, gap);

            if (Boutique.PrimaryButton("iconpicker.confirm", "CONFIRM", scale))
            {
                Confirmed = true;
                IsOpen = false;
            }
        }

        private void DrawIconGrid()
        {
            var searchLower = _searchFilter.ToLowerInvariant();

            FontAwesomeIcon[] icons;
            if (_selectedCategory == "Favorites")
            {
                icons = GetFavoriteIcons();
                if (icons.Length == 0)
                {
                    ImGui.TextDisabled("No favorite icons yet.");
                    ImGui.TextDisabled("Right-click any icon to add it to favorites.");
                    return;
                }
            }
            else if (!IconCategories.TryGetValue(_selectedCategory, out icons!))
            {
                return;
            }

            var filteredIcons = icons.Where(icon =>
                string.IsNullOrEmpty(searchLower) ||
                icon.ToString().ToLowerInvariant().Contains(searchLower)
            ).ToList();

            if (filteredIcons.Count == 0)
            {
                ImGui.TextDisabled("No icons match your search.");
                return;
            }

            var availableWidth = ImGui.GetContentRegionAvail().X;
            var iconSize = 36f;
            var spacing = 6f;
            var cellSize = iconSize + spacing;
            var iconsPerRow = Math.Max(1, (int)Math.Floor(availableWidth / cellSize));

            var drawList = ImGui.GetWindowDrawList();
            var startPos = ImGui.GetCursorScreenPos();

            for (int i = 0; i < filteredIcons.Count; i++)
            {
                int col = i % iconsPerRow;
                int row = i / iconsPerRow;

                var cellPos = startPos + new Vector2(col * cellSize, row * cellSize);
                var cellMin = cellPos;
                var cellMax = cellPos + new Vector2(iconSize, iconSize);

                var icon = filteredIcons[i];
                bool isHovered = ImGui.IsMouseHoveringRect(cellMin, cellMax);
                bool isSelected = SelectedIcon == icon;
                bool isFavorite = IsIconFavorite(icon);
                bool leftClicked = isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
                bool rightClicked = isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right);

                // Boutique gold accents instead of the previous blue selection.
                var bgColor = isSelected
                    ? Boutique.WithAlpha(Boutique.Gold, 0.18f)
                    : isHovered
                        ? Boutique.WithAlpha(Boutique.Gold, 0.10f)
                        : new Vector4(0.04f, 0.05f, 0.07f, 0.55f);

                drawList.AddRectFilled(cellMin, cellMax, ImGui.ColorConvertFloat4ToU32(bgColor), 0f);

                if (isSelected || isHovered)
                {
                    var borderColor = isSelected ? Boutique.Gold : Boutique.GoldDeep;
                    drawList.AddRect(cellMin, cellMax, ImGui.ColorConvertFloat4ToU32(borderColor), 0f, ImDrawFlags.None, 1.5f);
                }

                if (isFavorite)
                {
                    var starPos = cellMin + new Vector2(iconSize - 10, 2);
                    drawList.AddText(UiBuilder.IconFont, 10f, starPos,
                        ImGui.ColorConvertFloat4ToU32(Boutique.GoldWarm),
                        FontAwesomeIcon.Star.ToIconString());
                }

                ImGui.PushFont(UiBuilder.IconFont);
                var iconStr = icon.ToIconString();
                var textSize = ImGui.CalcTextSize(iconStr);
                var textPos = cellMin + new Vector2((iconSize - textSize.X) / 2, (iconSize - textSize.Y) / 2);

                var iconColor = isSelected
                    ? Boutique.GoldBright
                    : Boutique.Text;
                drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(iconColor), iconStr);
                ImGui.PopFont();

                if (leftClicked)
                {
                    SelectedIcon = icon;
                    OnIconChanged?.Invoke(icon);

                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (_lastClickTime > 0 && now - _lastClickTime < 300)
                    {
                        Confirmed = true;
                        IsOpen = false;
                    }
                    _lastClickTime = now;
                }

                if (rightClicked)
                    ToggleFavorite(icon);

                if (isHovered)
                {
                    // Boutique-themed tooltip: dark velvet bg, gold-deep
                    // border, near-zero rounding, tight padding.
                    ImGui.PushStyleColor(ImGuiCol.PopupBg, Boutique.RibbonBot);
                    ImGui.PushStyleColor(ImGuiCol.Border, Boutique.GoldDeep);
                    ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
                    ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 0f);
                    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8));
                    ImGui.BeginTooltip();
                    ImGui.PushStyleColor(ImGuiCol.Text, Boutique.GoldWarm);
                    ImGui.Text(icon.ToString());
                    ImGui.PopStyleColor();
                    ImGui.PushStyleColor(ImGuiCol.Text, Boutique.TextDim);
                    ImGui.Text("Left-click to select, double-click to confirm");
                    ImGui.PopStyleColor();
                    if (isFavorite)
                        ImGui.TextColored(Boutique.GoldWarm, "Right-click to remove from favourites");
                    else
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, Boutique.TextFaint);
                        ImGui.Text("Right-click to add to favourites");
                        ImGui.PopStyleColor();
                    }
                    ImGui.EndTooltip();
                    ImGui.PopStyleVar(3);
                    ImGui.PopStyleColor(2);
                }
            }

            int totalRows = (filteredIcons.Count + iconsPerRow - 1) / iconsPerRow;
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + totalRows * cellSize);
        }

        public override void OnClose()
        {
            base.OnClose();
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace CharacterSelectPlugin.Windows
{
    public class IconBarWindow : Window, IDisposable
    {
        private readonly Plugin plugin;
        private int scroll = 0;
        private float wheelAccum = 0f;
        private int arrowHeldDir = 0;
        private float arrowHoldTime = 0f;
        private float arrowRepeatAccum = 0f;
        private readonly Dictionary<string, bool> fileExists = new();
        private readonly ConcurrentDictionary<string, IDalamudTextureWrap?> thumbs = new(); // null = decode pending or failed
        private readonly Dictionary<string, float> hoverPop = new();
        private const int ThumbPx = 128;

        private static readonly Vector4 BarBg = new(0.024f, 0.027f, 0.035f, 0.92f);
        private static readonly Vector4 BarBorder = new(0.145f, 0.157f, 0.204f, 1f);
        private static readonly Vector4 HoverOutline = new(0.553f, 0.576f, 0.635f, 1f);
        private static readonly Vector4 ArrowRest = new(0.357f, 0.380f, 0.455f, 1f);
        private static readonly Vector4 ArrowHover = new(0.910f, 0.918f, 0.941f, 1f);
        private static readonly Vector4 ArrowSpent = new(0.200f, 0.216f, 0.290f, 1f);

        public IconBarWindow(Plugin plugin)
            : base("Quick Switch Icon Bar", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoResize)
        {
            this.plugin = plugin;
        }

        public override void OnOpen()
        {
            fileExists.Clear();
        }

        public void Dispose()
        {
            foreach (var wrap in thumbs.Values)
                wrap?.Dispose();
            thumbs.Clear();
        }

        public override void Draw()
        {
            var cfg = plugin.Configuration;
            float scale = ImGuiHelpers.GlobalScale * cfg.UIScaleMultiplier
                * Math.Clamp(cfg.QuickSwitchIconBarScale, 0.5f, 3f);

            RespectCloseHotkey = !cfg.QuickSwitchIgnoreEscape;

            var order = GetOrderedCharacters();
            int total = order.Count;
            int maxTiles = Math.Clamp(cfg.QuickSwitchIconBarMaxTiles, 5, 15);
            int visible = Math.Min(maxTiles, total);
            bool vertical = ResolveOrientation(cfg);

            float tile = 28f * scale;
            float gap = 2f * scale;
            float pad = 2f * scale;
            float arrowZone = 12f * scale;
            float grip = 10f * scale;
            bool overflow = total > visible;

            float length = pad * 2f + grip + gap
                + Math.Max(1, visible) * tile
                + Math.Max(0, visible - 1) * gap
                + (overflow ? (arrowZone + gap) * 2f : 0f);
            float cross = tile + pad * 2f;
            var size = vertical ? new Vector2(cross, length) : new Vector2(length, cross);

            SizeConstraints = new WindowSizeConstraints { MinimumSize = size, MaximumSize = size };
            var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
                      | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
                      | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBackground;
            if (cfg.QuickSwitchIgnoreEscape)
                flags |= ImGuiWindowFlags.NoFocusOnAppearing;
            Flags = flags;

            var winMin = ImGui.GetWindowPos();
            var winMax = winMin + ImGui.GetWindowSize();
            var dl = ImGui.GetWindowDrawList();

            float bgOpacity = 1f;
            if (cfg.SelectedTheme == ThemeSelection.Custom)
                bgOpacity = cfg.CustomTheme.CompactQuickSwitchButtonOpacity;

            if (bgOpacity > 0.01f)
            {
                dl.AddRectFilled(winMin, winMax, ImGui.ColorConvertFloat4ToU32(
                    new Vector4(BarBg.X, BarBg.Y, BarBg.Z, BarBg.W * bgOpacity)));
                dl.AddRect(winMin, winMax, ImGui.ColorConvertFloat4ToU32(
                    new Vector4(BarBorder.X, BarBorder.Y, BarBorder.Z, bgOpacity)));
            }

            if (total == 0)
            {
                ImGui.SetCursorScreenPos(winMin + new Vector2(pad * 2f, pad * 2f));
                ImGui.TextDisabled("No characters");
                return;
            }

            int maxScroll = Math.Max(0, total - visible);
            scroll = Math.Clamp(scroll, 0, maxScroll);

            if (overflow && ImGui.IsWindowHovered())
            {
                wheelAccum += ImGui.GetIO().MouseWheel;
                while (wheelAccum >= 1f) { scroll--; wheelAccum -= 1f; }
                while (wheelAccum <= -1f) { scroll++; wheelAccum += 1f; }
                scroll = Math.Clamp(scroll, 0, maxScroll);
            }

            var activeChar = plugin.GetActiveCharacter();
            float cursor = (vertical ? winMin.Y : winMin.X) + pad;
            float crossStart = (vertical ? winMin.X : winMin.Y) + pad;

            // drag grip, deliberately item-free
            {
                uint gripCol = ImGui.ColorConvertFloat4ToU32(ArrowRest);
                float gx = vertical ? crossStart + tile * 0.5f : cursor + grip * 0.5f;
                float gy = vertical ? cursor + grip * 0.5f : crossStart + tile * 0.5f;
                float step = 4f * scale;
                for (int d = -1; d <= 1; d++)
                {
                    var dot = vertical ? new Vector2(gx + d * step, gy) : new Vector2(gx, gy + d * step);
                    dl.AddCircleFilled(dot, 1.2f * scale, gripCol);
                }
                cursor += grip + gap;
            }

            if (overflow)
            {
                int act = DrawArrow(dl, Place(cursor, crossStart, vertical),
                        vertical ? new Vector2(tile, arrowZone) : new Vector2(arrowZone, tile),
                        -1, vertical, scroll > 0, scale);
                if (act == 2) scroll = 0;
                else if (act == 1) scroll = Math.Max(0, scroll - 1);
                cursor += arrowZone + gap;
            }

            for (int i = 0; i < visible; i++)
            {
                var character = order[scroll + i];
                var pos = Place(cursor, crossStart, vertical);

                ImGui.SetCursorScreenPos(pos);
                ImGui.InvisibleButton($"##iconBarTile{i}", new Vector2(tile, tile));
                bool hovered = ImGui.IsItemHovered();
                bool clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
                bool rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);

                DrawTile(dl, pos, new Vector2(tile, tile), character);

                string displayName = plugin.GetRosterDisplayName(character);

                hoverPop.TryGetValue(character.Name, out float pop);
                float popTarget = hovered && !cfg.ReduceMotion ? 1f : 0f;
                pop += (popTarget - pop) * Math.Min(1f, ImGui.GetIO().DeltaTime * 14f);
                if (pop < 0.01f) pop = 0f;
                hoverPop[character.Name] = pop;

                bool popActive = pop > 0.01f;
                var borderDl = dl;
                var borderPos = pos;
                float borderSize = tile;
                if (popActive)
                {
                    float grown = tile * (1f + 0.18f * pop);
                    var grownPos = pos - new Vector2((grown - tile) * 0.5f, (grown - tile) * 0.5f);
                    var fg = ImGui.GetForegroundDrawList();
                    DrawTile(fg, grownPos, new Vector2(grown, grown), character);
                    borderDl = fg;
                    borderPos = grownPos;
                    borderSize = grown;
                }

                if (character == activeChar)
                {
                    var np = GetNameplateColor(character);
                    borderDl.AddRect(borderPos, borderPos + new Vector2(borderSize, borderSize),
                        ImGui.ColorConvertFloat4ToU32(np), 0f, ImDrawFlags.None, 2f * scale);
                }
                else if (hovered)
                {
                    borderDl.AddRect(borderPos, borderPos + new Vector2(borderSize, borderSize),
                        ImGui.ColorConvertFloat4ToU32(HoverOutline), 0f, ImDrawFlags.None, 1f);
                }

                if (hovered)
                {
                    var vp = ImGui.GetMainViewport();
                    float ext = tile * 0.09f + 4f * scale;
                    var tipSize = ImGui.CalcTextSize(displayName) + ImGui.GetStyle().WindowPadding * 2f;
                    Vector2 tipPos;
                    if (vertical)
                    {
                        float tx = pos.X + tile + ext;
                        if (tx + tipSize.X > vp.Pos.X + vp.Size.X)
                            tx = pos.X - ext - tipSize.X;
                        tipPos = new Vector2(tx, pos.Y + (tile - tipSize.Y) * 0.5f);
                    }
                    else
                    {
                        float ty = pos.Y + tile + ext;
                        if (ty + tipSize.Y > vp.Pos.Y + vp.Size.Y)
                            ty = pos.Y - ext - tipSize.Y;
                        tipPos = new Vector2(
                            Math.Clamp(pos.X + (tile - tipSize.X) * 0.5f, vp.Pos.X, vp.Pos.X + vp.Size.X - tipSize.X), ty);
                    }
                    ImGui.SetNextWindowPos(tipPos);
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(displayName);
                    ImGui.EndTooltip();
                }

                if (clicked)
                    ApplyCharacter(character, -1);

                string popId = $"##iconBarPop_{character.Name}";
                if (rightClicked)
                {
                    if (ImGui.GetIO().KeyCtrl)
                        ApplyToTarget(character, -1);
                    else
                        ImGui.OpenPopup(popId);
                }

                ImGui.SetNextWindowSizeConstraints(new Vector2(140f * scale, 0f), new Vector2(280f * scale, 320f * scale));
                if (ImGui.BeginPopup(popId))
                {
                    ImGui.TextDisabled(displayName);
                    ImGui.Separator();
                    var designs = GetSortedDesigns(character);
                    if (designs.Count == 0)
                        ImGui.TextDisabled("No designs");
                    for (int j = 0; j < designs.Count; j++)
                    {
                        if (ImGui.Selectable($"{designs[j].Name}##iconBarDesign{j}"))
                            ApplyCharacter(character, GetOriginalIndex(character, designs[j]));
                        if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && ImGui.GetIO().KeyCtrl)
                        {
                            ApplyToTarget(character, GetOriginalIndex(character, designs[j]));
                            ImGui.CloseCurrentPopup();
                        }

                        string? previewPath = designs[j].PreviewImagePath;
                        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(previewPath) && ImageExists(previewPath))
                        {
                            try
                            {
                                var preview = Plugin.TextureProvider.GetFromFile(previewPath).GetWrapOrDefault();
                                if (preview != null && preview.Width > 0 && preview.Height > 0)
                                {
                                    float maxSize = 300f * ImGuiHelpers.GlobalScale * cfg.UIScaleMultiplier;
                                    float ratio = Math.Min(maxSize / preview.Width, maxSize / preview.Height);
                                    float dispW = preview.Width * ratio;
                                    float dispH = preview.Height * ratio;
                                    var mousePos = ImGui.GetMousePos();
                                    var rowRect = ImGui.GetItemRectMax();
                                    var viewportSize = ImGui.GetMainViewport().Size;
                                    var tooltipPos = new Vector2(rowRect.X + 10, mousePos.Y - dispH / 2);
                                    if (tooltipPos.X + dispW > viewportSize.X)
                                        tooltipPos.X = ImGui.GetItemRectMin().X - dispW - 10;
                                    if (tooltipPos.Y < 0) tooltipPos.Y = 0;
                                    else if (tooltipPos.Y + dispH > viewportSize.Y)
                                        tooltipPos.Y = viewportSize.Y - dispH;
                                    ImGui.SetNextWindowPos(tooltipPos);
                                    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 8f));
                                    ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.06f, 0.07f, 0.09f, 0.96f));
                                    ImGui.BeginTooltip();
                                    ImGui.Image(preview.Handle, new Vector2(dispW, dispH));
                                    ImGui.EndTooltip();
                                    ImGui.PopStyleColor();
                                    ImGui.PopStyleVar();
                                }
                            }
                            catch { }
                        }
                    }
                    ImGui.EndPopup();
                }

                cursor += tile + gap;
            }

            if (overflow)
            {
                int act = DrawArrow(dl, Place(cursor, crossStart, vertical),
                        vertical ? new Vector2(tile, arrowZone) : new Vector2(arrowZone, tile),
                        1, vertical, scroll < maxScroll, scale);
                if (act == 2) scroll = maxScroll;
                else if (act == 1) scroll = Math.Min(maxScroll, scroll + 1);
            }
        }

        private static Vector2 Place(float alongAxis, float crossAxis, bool vertical)
            => vertical ? new Vector2(crossAxis, alongAxis) : new Vector2(alongAxis, crossAxis);

        private bool autoVertical = false;

        private bool ResolveOrientation(Configuration cfg)
        {
            int o = cfg.QuickSwitchIconBarOrientation;
            if (o == 1) return false;
            if (o == 2) return true;

            // auto: flip when the drag grip touches a screen edge; hold while dragging
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                var vp = ImGui.GetMainViewport();
                float s = ImGuiHelpers.GlobalScale * cfg.UIScaleMultiplier
                    * Math.Clamp(cfg.QuickSwitchIconBarScale, 0.5f, 3f);
                var gp = ImGui.GetWindowPos() + (autoVertical
                    ? new Vector2(16f * s, 7f * s)
                    : new Vector2(7f * s, 16f * s));
                float t = 32f * ImGuiHelpers.GlobalScale;
                bool nearLeft = gp.X - vp.Pos.X < t;
                bool nearRight = vp.Pos.X + vp.Size.X - gp.X < t;
                bool nearTop = gp.Y - vp.Pos.Y < t;
                bool nearBottom = vp.Pos.Y + vp.Size.Y - gp.Y < t;

                if ((nearLeft || nearRight) && !nearTop && !nearBottom)
                    autoVertical = true;
                else if ((nearTop || nearBottom) && !nearLeft && !nearRight)
                    autoVertical = false;
            }
            return autoVertical;
        }

        private List<Character> GetOrderedCharacters()
        {
            var list = plugin.Characters.ToList();
            if (plugin.Configuration.QuickSwitchIconBarFavouritesFirst)
                list = list.OrderByDescending(c => c.IsFavorite).ToList();
            return list;
        }

        private bool ImageExists(string path)
        {
            if (!fileExists.TryGetValue(path, out bool exists))
            {
                exists = File.Exists(path);
                fileExists[path] = exists;
            }
            return exists;
        }

        private IDalamudTextureWrap? GetThumb(string path)
        {
            if (thumbs.TryGetValue(path, out var wrap))
                return wrap;
            if (!thumbs.TryAdd(path, null))
                return null;

            _ = Task.Run(() =>
            {
                try
                {
                    using var img = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
                    int side = Math.Min(img.Width, img.Height);
                    int cx = (img.Width - side) / 2;
                    int cy = (img.Height - side) / 2;
                    img.Mutate(m => m.Crop(new Rectangle(cx, cy, side, side)).Resize(ThumbPx, ThumbPx));
                    byte[] buf = new byte[ThumbPx * ThumbPx * 4];
                    img.CopyPixelDataTo(buf);
                    thumbs[path] = Plugin.TextureProvider.CreateFromRaw(
                        RawImageSpecification.Rgba32(ThumbPx, ThumbPx), buf, "CSPlus_IconBarThumb");
                }
                catch (Exception ex)
                {
                    Plugin.Log.Debug($"[IconBar] Thumbnail failed for {path}: {ex.Message}");
                }
            });
            return null;
        }

        private void DrawTile(ImDrawListPtr dl, Vector2 pos, Vector2 tileSize, Character character)
        {
            if (!string.IsNullOrEmpty(character.ImagePath) && ImageExists(character.ImagePath))
            {
                var thumb = GetThumb(character.ImagePath);
                if (thumb != null)
                {
                    dl.AddImage(thumb.Handle, pos, pos + tileSize);
                    return;
                }

                // full-res fallback while the thumbnail decodes
                var tex = Plugin.TextureProvider.GetFromFile(character.ImagePath).GetWrapOrDefault();
                if (tex != null && tex.Width > 0 && tex.Height > 0)
                {
                    // cover-crop the source to a centred square
                    Vector2 uv0 = Vector2.Zero, uv1 = Vector2.One;
                    if (tex.Width > tex.Height)
                    {
                        float inset = (1f - (float)tex.Height / tex.Width) * 0.5f;
                        uv0.X = inset; uv1.X = 1f - inset;
                    }
                    else if (tex.Height > tex.Width)
                    {
                        float inset = (1f - (float)tex.Width / tex.Height) * 0.5f;
                        uv0.Y = inset; uv1.Y = 1f - inset;
                    }
                    dl.AddImage(tex.Handle, pos, pos + tileSize, uv0, uv1);
                    return;
                }
            }

            var np = GetNameplateColor(character);
            dl.AddRectFilled(pos, pos + tileSize, ImGui.ColorConvertFloat4ToU32(np));
            string displayName = plugin.GetRosterDisplayName(character);
            string initial = displayName.Length > 0 ? char.ToUpperInvariant(displayName[0]).ToString() : "?";
            var ink = GetContrastingTextColor(np);
            var glyphSize = ImGui.CalcTextSize(initial);
            var glyphPos = pos + (tileSize - glyphSize) * 0.5f;
            dl.AddText(glyphPos, ImGui.ColorConvertFloat4ToU32(ink), initial);
        }

        // 0 none, 1 step, 2 jump to end
        private int DrawArrow(ImDrawListPtr dl, Vector2 pos, Vector2 zone, int dir, bool vertical, bool enabled, float scale)
        {
            ImGui.SetCursorScreenPos(pos);
            ImGui.InvisibleButton($"##iconBarArrow{dir}", zone);
            bool hovered = ImGui.IsItemHovered() && enabled;
            bool clicked = enabled && ImGui.IsItemClicked(ImGuiMouseButton.Left);

            // hold to repeat
            if (enabled && ImGui.IsItemActive())
            {
                if (arrowHeldDir != dir)
                {
                    arrowHeldDir = dir;
                    arrowHoldTime = 0f;
                    arrowRepeatAccum = 0f;
                }
                arrowHoldTime += ImGui.GetIO().DeltaTime;
                if (arrowHoldTime > 0.35f)
                {
                    arrowRepeatAccum += ImGui.GetIO().DeltaTime;
                    if (arrowRepeatAccum >= 0.07f)
                    {
                        arrowRepeatAccum = 0f;
                        clicked = true;
                    }
                }
            }
            else if (arrowHeldDir == dir)
            {
                arrowHeldDir = 0;
                arrowHoldTime = 0f;
            }

            var col = !enabled ? ArrowSpent : (hovered ? ArrowHover : ArrowRest);
            uint u = ImGui.ColorConvertFloat4ToU32(col);
            var c = pos + zone * 0.5f;
            float half = 3.5f * scale;

            if (vertical)
            {
                if (dir < 0)
                    dl.AddTriangleFilled(new Vector2(c.X - half, c.Y + half), new Vector2(c.X + half, c.Y + half), new Vector2(c.X, c.Y - half), u);
                else
                    dl.AddTriangleFilled(new Vector2(c.X - half, c.Y - half), new Vector2(c.X + half, c.Y - half), new Vector2(c.X, c.Y + half), u);
            }
            else
            {
                if (dir < 0)
                    dl.AddTriangleFilled(new Vector2(c.X + half, c.Y - half), new Vector2(c.X + half, c.Y + half), new Vector2(c.X - half, c.Y), u);
                else
                    dl.AddTriangleFilled(new Vector2(c.X - half, c.Y - half), new Vector2(c.X - half, c.Y + half), new Vector2(c.X + half, c.Y), u);
            }

            if (clicked && ImGui.GetIO().KeyShift)
                return 2;
            return clicked ? 1 : 0;
        }

        private void ApplyCharacter(Character character, int designIndex)
        {
            plugin.QuickSwitchWindow?.UpdateSelectionFromCharacter(character);
            plugin.AchievementTracker?.OnSwitchFromQuickSwitch();
            plugin.AchievementTracker?.CheckSwitchMethodsAll();
            plugin.ApplyProfile(character, designIndex);
        }

        private void ApplyToTarget(Character character, int designIndex)
        {
            var target = plugin.GetCurrentTarget();
            if (target == null)
            {
                Plugin.ChatGui.PrintError("[Character Select+] No target selected.");
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await plugin.ApplyToTarget(character, -1);
                    if (designIndex >= 0)
                        await plugin.ApplyToTarget(character, designIndex);
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"[IconBar] Error applying to target: {ex}");
                }
            });
        }

        private List<CharacterDesign> GetSortedDesigns(Character character)
        {
            var sortIndex = plugin.Configuration.CurrentDesignSortIndex;
            var designs = character.Designs.ToList();

            // 0=Favorites, 1=Alphabetical, 2=Recent, 3=Oldest, 4=Manual
            if (sortIndex == 4)
                return designs;

            if (sortIndex == 0)
            {
                designs.Sort((a, b) =>
                {
                    int favCompare = b.IsFavorite.CompareTo(a.IsFavorite);
                    if (favCompare != 0) return favCompare;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            }
            else if (sortIndex == 1)
                designs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            else if (sortIndex == 2)
                designs.Sort((a, b) => b.DateAdded.CompareTo(a.DateAdded));
            else if (sortIndex == 3)
                designs.Sort((a, b) => a.DateAdded.CompareTo(b.DateAdded));

            return designs;
        }

        private static int GetOriginalIndex(Character character, CharacterDesign design)
            => character.Designs.FindIndex(d => d.Id == design.Id);

        private static Vector4 GetNameplateColor(Character character)
            => new(character.NameplateColor.X, character.NameplateColor.Y, character.NameplateColor.Z, 1.0f);

        private static Vector4 GetContrastingTextColor(Vector4 bgColor)
        {
            float brightness = 0.299f * bgColor.X + 0.587f * bgColor.Y + 0.114f * bgColor.Z;
            return brightness > 0.5f ? new Vector4(0, 0, 0, 1) : new Vector4(1, 1, 1, 1);
        }
    }
}

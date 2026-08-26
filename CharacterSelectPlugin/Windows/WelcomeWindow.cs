using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using CharacterSelectPlugin.Windows.Styles;
using BoutiqueChassis = CharacterSelectPlugin.Windows.Styles.Boutique;

namespace CharacterSelectPlugin.Windows
{
    public class WelcomeWindow : Window
    {
        private readonly Plugin plugin;

        public WelcomeWindow(Plugin plugin) : base("##CSPlusWelcome",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings)
        {
            this.plugin = plugin;
        }

        public override void PreDraw()
        {
            float scale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;
            float winW = 620f * scale;
            float winH = 340f * scale;
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
        }

        public override void PostDraw()
        {
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(3);
        }

        public override void Draw()
        {
            float scale = ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier;
            var dl = ImGui.GetWindowDrawList();
            var winMin = ImGui.GetWindowPos();
            var winMax = winMin + ImGui.GetWindowSize();

            // chassis
            uint bgTop = Boutique.U32(new Vector4(0x06 / 255f, 0x07 / 255f, 0x09 / 255f, 1f));
            uint bgBot = Boutique.U32(new Vector4(0x03 / 255f, 0x04 / 255f, 0x0A / 255f, 1f));
            dl.AddRectFilledMultiColor(winMin, winMax, bgTop, bgTop, bgBot, bgBot);
            dl.AddRect(winMin, winMax, Boutique.U32(Boutique.BorderSoft), 0f, ImDrawFlags.None, 1f * scale);
            dl.AddRect(winMin + new Vector2(1, 1), winMax - new Vector2(1, 1),
                Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.025f)), 0f, ImDrawFlags.None, 1f * scale);
            DrawCornerBrackets(dl, winMin, winMax, scale);

            float ribbonH = 28f * scale;
            var ribbonMin = winMin;
            var ribbonMax = new Vector2(winMax.X, winMin.Y + ribbonH);
            DrawRibbon(dl, ribbonMin, ribbonMax, scale);

            // hero title + kicker, same treatment as the settings header
            float midX = (winMin.X + winMax.X) * 0.5f;
            float titleY = ribbonMax.Y + 22f * scale;
            float titleBottom = titleY;
            using (Plugin.Instance?.OswaldSemiMid?.Push())
            {
                float titleTrack = 7f * scale;
                string title = "A HEADS UP";
                float titleW = Boutique.MeasureTrackedText(title, titleTrack);
                float titleX = midX - titleW * 0.5f;
                Boutique.DrawTrackedText(dl, new Vector2(titleX + 2f, titleY + 2f),
                    title, Boutique.U32(new Vector4(0f, 0f, 0f, 0.55f)), titleTrack);
                Boutique.DrawTrackedText(dl, new Vector2(titleX, titleY),
                    title, Boutique.U32(Boutique.Text), titleTrack);
                titleBottom = titleY + ImGui.GetFontSize();
            }
            using (Plugin.Instance?.OswaldSemi13?.Push())
            {
                float kickTrack = 5f * scale;
                string kicker = "ACHIEVEMENTS ARE ON";
                float kW = Boutique.MeasureTrackedText(kicker, kickTrack);
                float kY = titleBottom + 10f * scale;
                Boutique.DrawTrackedText(dl, new Vector2(midX - kW * 0.5f, kY),
                    kicker, Boutique.U32(Boutique.Gold), kickTrack);
                titleBottom = kY + ImGui.GetFontSize();
            }

            // body copy, wrapped to an explicit width
            float padX = 34f * scale;
            float bodyTop = titleBottom + 22f * scale;
            float bodyW = (winMax.X - winMin.X) - padX * 2f;
            var bodyPos = new Vector2(winMin.X + padX, bodyTop);

            string line1 = "CS+ has achievements, and they're on by default. They're there to give you a more fun, rewarding way to find your way around the plugin's features than reading patch notes or the guide.";
            string line2 = "They unlock as you use things, and each one pops a toast notification and a chat message. The toasts are fairly attention grabbing, so if that's not your thing, you can turn the whole system off now, or later under Settings > Achievements.";

            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), bodyPos, Boutique.U32(Boutique.Text), line1, bodyW);
            float line1H = ImGui.CalcTextSize(line1, false, bodyW).Y;
            var line2Pos = new Vector2(bodyPos.X, bodyPos.Y + line1H + 12f * scale);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), line2Pos, Boutique.U32(Boutique.TextDim), line2, bodyW);

            // footer buttons, sized from their labels, primary on the left
            float btnH = 30f * scale;
            float btnGap = 8f * scale;
            float btnPad = 18f * scale;
            float trackPx = 2.0f * scale;
            string keepLabel = "KEEP THEM ON";
            string offLabel = "TURN THEM OFF";
            float keepW = Boutique.MeasureTrackedText(keepLabel, trackPx) + btnPad * 2f;
            float offW = Boutique.MeasureTrackedText(offLabel, trackPx) + btnPad * 2f;
            float totalW = keepW + btnGap + offW;
            float btnX = winMin.X + ((winMax.X - winMin.X) - totalW) * 0.5f;
            float btnY = winMax.Y - 22f * scale - btnH;

            DrawPillButton(dl, keepLabel, new Vector2(btnX, btnY), new Vector2(keepW, btnH), scale, isPrimary: true, onClick: () => Choose(true));
            DrawPillButton(dl, offLabel, new Vector2(btnX + keepW + btnGap, btnY), new Vector2(offW, btnH), scale, isPrimary: false, onClick: () => Choose(false));
        }

        private void Choose(bool enableAchievements)
        {
            plugin.Configuration.EnableAchievementSystem = enableAchievements;
            plugin.Configuration.ShowWelcomePrompt = false;
            plugin.Configuration.Save();
            IsOpen = false;
        }

        private void DrawRibbon(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
        {
            BoutiqueChassis.DrawRibbonBackground(dl, min, max, scale);

            float padX = 12f * scale;
            float midY = (min.Y + max.Y) * 0.5f;
            double t = ImGui.GetTime();

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

            using (Plugin.Instance?.OswaldMed10?.Push())
            {
                float trackPx = 2.6f * scale;
                string left = "WELCOME";
                string right = "CHARACTER SELECT+";
                float leftW = Boutique.MeasureTrackedText(left, trackPx);
                float rightW = Boutique.MeasureTrackedText(right, trackPx);
                float gap = 12f * scale;
                float diaSize = 3.5f * scale;
                float totalW = leftW + gap + diaSize * 2f + gap + rightW;
                float startX = (min.X + max.X) * 0.5f - totalW * 0.5f;
                float fontH = ImGui.GetFontSize();
                float textY = midY - fontH * 0.5f;
                Boutique.DrawTrackedText(dl, new Vector2(startX, textY), left, Boutique.U32(Boutique.TextDim), trackPx);
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
        }

        private static void DrawCornerBrackets(ImDrawListPtr dl, Vector2 winMin, Vector2 winMax, float scale)
        {
            float bSize = 12f * scale;
            float bInset = 8f * scale;
            uint bCol = Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.40f));

            var bl = new Vector2(winMin.X + bInset, winMax.Y - bInset);
            dl.AddLine(bl, new Vector2(bl.X, bl.Y - bSize), bCol, 1f * scale);
            dl.AddLine(bl, new Vector2(bl.X + bSize, bl.Y), bCol, 1f * scale);

            var br = new Vector2(winMax.X - bInset, winMax.Y - bInset);
            dl.AddLine(br, new Vector2(br.X, br.Y - bSize), bCol, 1f * scale);
            dl.AddLine(br, new Vector2(br.X - bSize, br.Y), bCol, 1f * scale);
        }

        private static void DrawPillButton(ImDrawListPtr dl, string label, Vector2 pos, Vector2 size, float scale, bool isPrimary, Action onClick)
        {
            ImGui.SetCursorScreenPos(pos);
            bool clicked = ImGui.InvisibleButton($"##welcome_btn_{label}", size);
            bool hovered = ImGui.IsItemHovered();
            var btnMin = pos;
            var btnMax = pos + size;

            if (isPrimary)
            {
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
                dl.AddRect(btnMin, btnMax, Boutique.U32(Boutique.WithAlpha(Boutique.GoldDeep, 0.7f)), 0f, ImDrawFlags.None, 1f * scale);
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

            float trackPx = 2.0f * scale;
            float w = Boutique.MeasureTrackedText(label, trackPx);
            float h = ImGui.GetFontSize();
            uint textU = isPrimary
                ? Boutique.U32(new Vector4(0.10f, 0.08f, 0f, 1f))
                : (hovered ? Boutique.U32(Boutique.Text) : Boutique.U32(Boutique.TextDim));
            Boutique.DrawTrackedText(dl,
                new Vector2(btnMin.X + (size.X - w) * 0.5f, btnMin.Y + (size.Y - h) * 0.5f),
                label, textU, trackPx);

            if (clicked) onClick();
        }
    }
}

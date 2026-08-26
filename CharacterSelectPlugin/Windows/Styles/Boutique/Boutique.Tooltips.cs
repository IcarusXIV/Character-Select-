using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CharacterSelectPlugin.Windows.Styles;

public static partial class Boutique
{
    // ── Boutique tooltip ────────────────────────────────────────────────
    // Single canonical tooltip helper. Dark velvet bg, gold-tinted border,
    // wrapped body copy in OutfitMed13 (heavier stroke than Regular at the
    // same size for easier first-glance reading).

    // Default wrap width in unscaled pixels; default overloads multiply by Scale
    public const float TooltipWrapDefault = 300f;

    // Tooltip on the most recently drawn item
    public static void Tooltip(string text) => Tooltip(text, TooltipWrapDefault * Scale);

    /// <summary>Tooltip with custom wrap width (in pixels).</summary>
    public static void Tooltip(string text, float wrapWidth)
    {
        if (string.IsNullOrEmpty(text)) return;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 7f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.04f, 0.05f, 0.08f, 0.96f));
        ImGui.PushStyleColor(ImGuiCol.Border, WithAlpha(Gold, 0.55f));
        ImGui.BeginTooltip();
        using (Body13?.Push())
        {
            ImGui.PushTextWrapPos(wrapWidth);
            ImGui.TextColored(Text, text);
            ImGui.PopTextWrapPos();
        }
        ImGui.EndTooltip();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar();
    }

    // Tracked-caps title over body lines
    public static void TitledTooltip(string title, string body, Vector4? titleColor = null)
        => TitledTooltip(title, body, TooltipWrapDefault * Scale, titleColor);

    public static void TitledTooltip(string title, string body, float wrapWidth, Vector4? titleColor = null)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 7f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.04f, 0.05f, 0.08f, 0.96f));
        ImGui.PushStyleColor(ImGuiCol.Border, WithAlpha(Gold, 0.55f));
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(wrapWidth);

        using (Kicker9?.Push())
        {
            float fs = ImGui.GetFontSize();
            float trackPx = Track32(fs);
            var dl = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            DrawTrackedText(dl, pos, title.ToUpperInvariant(),
                U32(titleColor ?? GoldWarm), trackPx);
            float w = MeasureTrackedText(title.ToUpperInvariant(), trackPx);
            ImGui.Dummy(new Vector2(w, fs));
        }
        ImGui.Spacing();
        using (Body13?.Push())
        {
            ImGui.TextColored(Text, body);
        }

        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar();
    }

    // Red-bordered variant for destructive or restricted actions
    public static void WarningTooltip(string title, string body)
        => WarningTooltip(title, body, TooltipWrapDefault * Scale);

    public static void WarningTooltip(string title, string body, float wrapWidth)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 7f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.04f, 0.05f, 0.08f, 0.96f));
        ImGui.PushStyleColor(ImGuiCol.Border, WithAlpha(Red, 0.55f));
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(wrapWidth);

        using (Kicker9?.Push())
        {
            float fs = ImGui.GetFontSize();
            float trackPx = Track32(fs);
            var dl = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            DrawTrackedText(dl, pos, title.ToUpperInvariant(), U32(Red), trackPx);
            float w = MeasureTrackedText(title.ToUpperInvariant(), trackPx);
            ImGui.Dummy(new Vector2(w, fs));
        }
        ImGui.Spacing();
        using (Body13?.Push())
        {
            ImGui.TextColored(Text, body);
        }

        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar();
    }
}

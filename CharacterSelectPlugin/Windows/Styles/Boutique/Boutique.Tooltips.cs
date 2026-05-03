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

    /// <summary>
    /// Default tooltip wrap width in pixels. Kept narrow so tooltips feel
    /// like compact captions rather than paragraphs of text. Callers needing
    /// wider tooltips can pass an override to the wrapWidth param overloads.
    /// </summary>
    public const float TooltipWrapDefault = 220f;

    /// <summary>Single-line tracked tooltip on the most recently drawn item.</summary>
    public static void Tooltip(string text) => Tooltip(text, TooltipWrapDefault);

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

    /// <summary>Multi-line tooltip with a tracked-caps title and body lines.</summary>
    public static void TitledTooltip(string title, string body, Vector4? titleColor = null)
        => TitledTooltip(title, body, TooltipWrapDefault, titleColor);

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

    /// <summary>Warning-tinted tooltip (red-warm border, used for destructive or restricted actions).</summary>
    public static void WarningTooltip(string title, string body)
        => WarningTooltip(title, body, TooltipWrapDefault);

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

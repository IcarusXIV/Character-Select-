using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Windows.Forms;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.ImGuiSeStringRenderer;
using CharacterSelectPlugin.Windows.Styles;
using CharacterSelectPlugin.Windows.Utils;
using SeString = Dalamud.Game.Text.SeStringHandling.SeString;
using SeStringBuilder = Lumina.Text.SeStringBuilder;
using DalamudSeStringBuilder = Dalamud.Game.Text.SeStringHandling.SeStringBuilder;

namespace CharacterSelectPlugin.Windows.Components
{
    public partial class CharacterForm : IDisposable
    {
        private Plugin plugin;
        private UIStyles uiStyles;

        // Form state
        public bool IsEditWindowOpen { get; private set; } = false;
        private int selectedCharacterIndex = -1;
        private bool isSecretMode = false;
        private bool isAdvancedModeCharacter = false;
        private string? pendingImagePath = null;
        private string? pendingAnimatedImagePath = null;
        private string? pendingCutoutImagePath = null;
        private string? pendingCutoutBackdropPath = null;
        // 0 = None, 1 = Animated, 2 = Pop-out.  UI-only, derived from data on load,
        // cleared on save so only the active mode's path persists.
        private int _hoverModeRadio = 0;
        private bool wasFormVisibleLastFrame = false;
        // SetScrollY only takes effect on the next layout pass; apply across 3
        // frames so the form actually opens at the top.
        private int _formScrollResetFramesPending = 0;
        private float _formIndent = 0f;
        private float _formContentWidth = 0f;

        // Edit fields
        private string editedCharacterName = "";
        private string originalCharacterName = ""; // Track original name for warning resolution
        private string editedCharacterMacros = "";
        private string? editedCharacterImagePath = null;
        private string? editedAnimatedImagePath = null;
        private string? editedCutoutImagePath = null;
        private string? editedCutoutBackdropPath = null;
        // Cutout tuning state (mirrors Character defaults so new characters
        // start at sensible values; updated on edit-load).
        private float editedCutoutScale = 3.25f;
        private float editedCutoutAnchorX = 0.65f;
        private float editedCutoutAnchorY = 1.00f;
        // Portrait + GIF framing controls
        private float editedPortraitOffsetX = 0f;
        private float editedPortraitOffsetY = 0f;
        private float editedPortraitZoom    = 1f;
        private float editedAnimatedOffsetX = 0f;
        private float editedAnimatedOffsetY = 0f;
        private float editedAnimatedZoom    = 1f;
        private string nameValidationError = "";
        private Vector3 editedCharacterColor = new Vector3(1.0f, 1.0f, 1.0f);
        private string editedCharacterPenumbra = "";
        private string editedCharacterGlamourer = "";
        private string editedCharacterCustomize = "";
        private string editedCharacterTag = "";
        private string editedCharacterAutomation = "";
        private string editedCharacterMoodlePreset = "";
        private int? editedCharacterGearset = null;
        private bool editedCharacterExcludeFromNameSync = false;
        private bool editedCharacterUseGlitchNameEffect = false;
        private string editedCharacterAlias = "";

        // Honorific fields
        private string editedCharacterHonorificTitle = "";
        private string editedCharacterHonorificPrefix = "Prefix";
        private string editedCharacterHonorificSuffix = "Suffix";
        private Vector3 editedCharacterHonorificColor = new Vector3(1.0f, 1.0f, 1.0f);
        private Vector3 editedCharacterHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f);
        private Vector3? editedCharacterHonorificColor3 = null;  // Second colour for two-colour gradient
        private int? editedCharacterHonorificGradientSet = null;
        private string? editedCharacterHonorificAnimationStyle = null;

        // Temp fields for live updates
        private string tempHonorificTitle = "";
        private string tempHonorificPrefix = "Prefix";
        private string tempHonorificSuffix = "Suffix";
        private Vector3 tempHonorificColor = new Vector3(1.0f, 1.0f, 1.0f);
        private Vector3 tempHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f);
        private Vector3 tempHonorificColor3 = new Vector3(0.5f, 0.5f, 1.0f);  // Default light blue for contrast
        private int? tempHonorificGradientSet = null;
        private string? tempHonorificAnimationStyle = null;
        private string tempMoodlePreset = "";

        // Gradient preset names and data from Honorific (exact base64 encoded color arrays)
        private static readonly string[] GradientPresetNames = new[]
        {
            "Pride Rainbow", "Transgender", "Lesbian", "Bisexual",
            "Black & White", "Black & Red", "Black & Blue", "Black & Yellow",
            "Black & Green", "Black & Pink", "Black & Cyan", "Cherry Blossom",
            "Golden", "Pastel Rainbow", "Dark Rainbow", "Non-binary"
        };

        private static readonly string[] GradientPresetData = new[]
        {
            "5AMD6RsC7TMC8ksB92MB/HsA/5EA/6IA/7IA/8MA/9QA/+UA5+MEutAKjr0RYaoYNZYeCIMlAHlFAG9rAGaRAF23AFTdAkv9FkXnKj/RPjm8UjOmZi2QcymCcymCcymCcymCcymCcymCZi2QUjOmPjm8Kj/RFkXnAkv9AFTdAF23AGaRAG9rAHlFCIMlNZYeYaoYjr0RutAK5+ME/+UA/9QA/8MA/7IA/6IA/5EA/HsA92MB8ksB7TMC6RsC5AMD", // Pride Rainbow
            "W876b8nygsXplsDhqbvYvbfQ0LLI5K2/9aq59rXC+MDL+cvU+tbd/OHm/ezv/vf4//z9/fH0/Obr+9zi+tHZ+MbQ97vH9rC+7qu72q/Ex7TMs7nUn77djMLleMftZcz2Zcz2eMftjMLln77ds7nUx7TM2q/E7qu99rC+97vH+MbQ+tHZ+9zi/Obr/fH0//z9/vf4/ezv/OHm+tbd+cvU+MDL9rXC9aq55K2/0LLIvbfQqbvYlsDhgsXpb8nyW876", // Transgender
            "1S0A2lQT33ol46E46MdL7e5d8Opg9Nhe98Zc+rVZ/aNX/6Rm/7eG/8qm/93H//Hn/fj79Nnp67vY4p3G2n+10WKkzGCgxl2cwVuZvFmVtleRskqJrzqBrCp4qBpvpQpmpQpmqBpvrCp4rzqBskqJtleRvFmVwVuZxl2czGCg0WKk2oC1457H67zY9Nrp/fj7//Hn/93H/8qm/7eG/6Rm/aNX+rVZ98dc9Nhe8Opg7exd6MZK46A43nkl2lMT1S0A", // Lesbian
            "1gJwzgx1xxZ6vyB/uCmDsDOIqT2NoUeSm0+Wm0+Wm0+Wm0+WlU6XgUuZbUibWUWeRkKgMj+iHjylCjmnCjmnHjylMj+iRkKgWUWebUibgUuZlU6Xm0+Wm0+Wm0+Wm0+WoUeSqT2NsDOIuCmDvyB/xxZ6zgx11gJw", // Bisexual
            "////9/f37+/v5+fn39/f19fXzs7OxsbGvr6+tra2rq6upqamnp6elpaWjo6OhoaGfX19dXV1bW1tZWVlXV1dVVVVTU1NRUVFPT09NTU1LS0tJCQkHBwcFBQUDAwMBAQEBAQEDAwMFBQUHBwcJCQkLS0tNTU1PT09RUVFTU1NVVVVXV1dZWVlbW1tdXV1fX19hoaGjo6OlpaWnp6epqamrq6utra2vr6+xsbGzs7O19fX39/f5+fn7+/v9/f3////", // Black & White
            "/wAA9QAA6wAA4QAA1wAAzAAAwgAAuAAArgAApAAAmgAAkAAAhgAAewAAcQAAZwAAXQAAUwAASQAAPwAANQAAKwAAIAAAFgAADAAAAgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgAADAAAFgAAIAAAKgAANQAAPwAASQAAUwAAXQAAZwAAcQAAewAAhgAAkAAAmgAApAAArgAAuAAAwgAAzAAA1wAA4QAA6wAA9QAA/wAA", // Black & Red
            "AAD/AAD1AADrAADhAADXAADMAADCAAC4AACuAACkAACaAACQAACGAAB7AABxAABnAABdAABTAABJAAA/AAA1AAArAAAgAAAWAAAMAAACAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAAAMAAAWAAAgAAAqAAA1AAA/AABJAABTAABdAABnAABxAAB7AACGAACQAACaAACkAACuAAC4AADCAADMAADXAADhAADrAAD1AAD/", // Black & Blue
            "//8A9fUA6+sA4eEA19cAzMwAwsIAuLgArq4ApKQAmpoAkJAAhoYAe3sAcXEAZ2cAXV0AU1MASUkAPz8ANTUAKysAICAAFhYADAwAAgIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgIADAwAFhYAICAAKioANTUAPz8ASUkAU1MAXV0AZ2cAcXEAe3sAhoYAkJAAmpoApKQArq4AuLgAwsIAzMwA19cA4eEA6+sA9fUA//8A", // Black & Yellow
            "AP8AAPUAAOsAAOEAANcAAMwAAMIAALgAAK4AAKQAAJoAAJAAAIYAAHsAAHEAAGcAAF0AAFMAAEkAAD8AADUAACsAACAAABYAAAwAAAIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIAAAwAABYAACAAACoAADUAAD8AAEkAAFMAAF0AAGcAAHEAAHsAAIYAAJAAAJoAAKQAAK4AALgAAMIAAMwAANcAAOEAAOsAAPUAAP8A", // Black & Green
            "/wD/9QD16wDr4QDh1wDXzADMwgDCuAC4rgCupACkmgCakACQhgCGewB7cQBxZwBnXQBdUwBTSQBJPwA/NQA1KwArIAAgFgAWDAAMAgACAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgACDAAMFgAWIAAgKgAqNQA1PwA/SQBJUwBTXQBdZwBncQBxewB7hgCGkACQmgCapACkrgCuuAC4wgDCzADM1wDX4QDh6wDr9QD1/wD/", // Black & Pink
            "AP//APX1AOvrAOHhANfXAMzMAMLCALi4AK6uAKSkAJqaAJCQAIaGAHt7AHFxAGdnAF1dAFNTAElJAD8/ADU1ACsrACAgABYWAAwMAAICAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAICAAwMABYWACAgACoqADU1AD8/AElJAFNTAF1dAGdnAHFxAHt7AIaGAJCQAJqaAKSkAK6uALi4AMLCAMzMANfXAOHhAOvrAPX1AP//", // Black & Cyan
            "7s7/7Mj36sPv573n5bje47LW4azO3qbG3KC+2pq32pW13pG84YzD5YjK6ITR7H/Y8Hvg83bm93Lt+m30/Wn5/Wf3+Wbt9GXj72Ta6mPQ5mLH4mK+3mG02mCq1l+h0l6Yzl2Oyl2GymCEzmaI0WyM1HOP13mT2n6X34Sb4oqf5ZCi6Jam65up76Gt8qex9a60+bS4/Lq8/r/A/cHF+8LK+sPQ+cXV98bb9sfg9Mjm88rs8svy8Mz378387s7/7s7/", // Cherry Blossom
            "/5IA/5QE/5YI/5kL/5sP/50T/58X/6Eb/6Mf/6Yj/6gn/6or/6wv/68z/7E2/7M6/7Y+/7hC/7pG/71J/79N/8FR/8NV/8VZ/8dd/8ph/8xl/85p/9Jz/9mJ/+Cl/+a1/+uu/+2c/++L/+2D/+p+/+Z5/+N0/+Bw/9xr/9lm/9Vh/9Jc/89X/8tS/8hN/8VI/8FE/74//7s6/7c1/7Qx/7As/60n/6oi/6Yd/6MY/58T/5wO/5kK/5UF/5IA/5IA", // Golden
            "/7y8/8K8/8i8/868/9S8/9q8/+G8/+e8/+28//O8//m8/v68+f+88/+87f+86P+84f+82/+81f+8z/+8yf+8w/+8vf+8vP/BvP/HvP/NvP/TvP/avP/gvP/mvP/svP/yvP/4vP//vPn/vPP/vOz/vOX/vN//vNj/vNL/vMz/vMX/vL//v7z/xrz/zLz/0rz/2rz/4Lz/5rz/7bz/87z/+rz//7z+/7z4/7zx/7zr/7zk/7ze/7zX/7zR/7zK/7y8", // Pastel Rainbow
            "MgAAMgUAMgkAMg4AMhIAMhcAMhsAMiAAMiUAMioAMi4AMTIALTIAKDIAJDIAHzIAGjIAFTIAETIADDIABzIAAzIAADICADIGADILADIQADIUADIZADIeADIiADInADIrADIwAC8yACsyACYyACEyABwyABgyABMyAA0yAAkyAAQyAQEyBQAyCgAyDwAyEwAyGQAyHgAyIgAyJwAyLAAyMQAyMgAvMgAqMgAlMgAgMgAbMgAWMgASMgANMgAAMgAA", // Dark Rainbow
            "//Qz//VK//Zg//h3//mO//qk//u7//3S//7o////9O366dr13sjv07Xqx6PlvJDgsX7apmvVm1nQik+5eUWiZzuLVjF0RShcNB5FIhQuEQoXAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEQoXIhQuNB5FRShcVjF0ZzuLeUWiik+5m1nQpmvVsX7avJDgx6Pl07Xq3sjv6dr19O36//////7o//3S//u7//qk//mO//h3//Zg//VK//Qz"  // Non-binary
        };

        // Decoded gradient color arrays (lazy initialized)
        private static byte[][,]? _decodedGradients = null;
        private static byte[][,] DecodedGradients
        {
            get
            {
                if (_decodedGradients == null)
                {
                    _decodedGradients = new byte[GradientPresetData.Length][,];
                    for (int i = 0; i < GradientPresetData.Length; i++)
                    {
                        var arr = Convert.FromBase64String(GradientPresetData[i]);
                        var arr2 = new byte[arr.Length / 3, 3];
                        for (var j = 0; j < arr.Length; j += 3)
                        {
                            arr2[j / 3, 0] = arr[j];
                            arr2[j / 3, 1] = arr[j + 1];
                            arr2[j / 3, 2] = arr[j + 2];
                        }
                        _decodedGradients[i] = arr2;
                    }
                }
                return _decodedGradients;
            }
        }

        // Animation timer for preview
        private static readonly System.Diagnostics.Stopwatch AnimationTimer = System.Diagnostics.Stopwatch.StartNew();
        private string advancedCharacterMacroText = "";

        public CharacterForm(Plugin plugin, UIStyles uiStyles)
        {
            this.plugin = plugin;
            this.uiStyles = uiStyles;
        }

        public void Dispose()
        {
        }

        public void Draw()
        {
            if (Plugin.UseClassicLayout) { DrawClassicLayout(); return; }
            if (!plugin.IsAddCharacterWindowOpen && !IsEditWindowOpen)
            {
                wasFormVisibleLastFrame = false;
                return;
            }

            // First frame the form appears, snap the parent grid scroll back to top
            // so the user can see the header instead of mid-page wherever they were.
            // Also flag the form's own BeginChild to reset its scroll on the next
            // frame so previously-opened-and-scrolled forms don't re-open at the
            // bottom.
            if (!wasFormVisibleLastFrame)
            {
                ImGui.SetScrollY(0f);
                _formScrollResetFramesPending = 3;
                wasFormVisibleLastFrame = true;
            }

            var totalScale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);

            // Check if Conflict Resolution is enabled and determine secret mode
            if (plugin.Configuration.EnableConflictResolution)
            {
                // For editing existing characters, check if they already have secret mode data
                if (IsEditWindowOpen && selectedCharacterIndex >= 0 && selectedCharacterIndex < plugin.Characters.Count)
                {
                    var character = plugin.Characters[selectedCharacterIndex];
                    bool hasSecretModeData = character.SecretModState != null ||
                                           (character.Designs?.Any(d => d.SecretModState != null) == true);

                    if (hasSecretModeData && !isSecretMode)
                    {
                        isSecretMode = true;
                    }
                }

                if (!IsEditWindowOpen && isSecretMode)
                {
                    plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                }
            }

            // No chassis. The outer BeginChild fills the parent's FULL width so the
            // scrollbar sits at the right edge of the available area (next to the
            // dp-edge), not in the middle of the form. Form content INSIDE is
            // constrained via the _formIndent + _formContentWidth class fields.
            float fs = Boutique.FormScale;
            float maxFormW = 580f * fs;

            ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
            ImGui.BeginChild("##character_form_inline",
                Vector2.Zero,
                false,
                ImGuiWindowFlags.NoBackground);

            // Snap THIS child's scroll to the top on first 3 frames the form is
            // visible. SetScrollY applies on the NEXT frame's layout pass, so a
            // single-frame call leaves the form at the previous scroll briefly.
            // 3 frames absorbs that lag without flicker on slow loops.
            if (_formScrollResetFramesPending > 0)
            {
                ImGui.SetScrollY(0f);
                _formScrollResetFramesPending--;
            }

            float availW = ImGui.GetContentRegionAvail().X;
            _formIndent = 24f * fs;
            _formContentWidth = MathF.Min(availW - _formIndent - 8f * fs, maxFormW);

            DrawInlineTitleRow(fs);

            Boutique.PushFormStyle();
            try
            {
                // OutfitMed12 (15.6px Medium weight), same size as OutfitBody12 but
                // heavier stroke so typed-in text is far easier on the eyes. Reduces
                // squinting at small-size body text in input fields.
                using (Plugin.Instance?.OutfitMed12?.Push())
                {
                    DrawCharacterFormContent(totalScale);
                    DrawInlineFooterButtons(fs);
                }
            }
            finally
            {
                Boutique.PopFormStyle();
            }

            ImGui.EndChild();
            ImGui.PopStyleColor();
        }

        // Inline title row: kicker + diamond + pip + NAME + X close. No bg fill,
        // no gold binding strip; just text and an X icon-button on the right.
        private void DrawInlineTitleRow(float fs)
        {
            string kicker = IsEditWindowOpen ? "EDIT CHARACTER" : "NEW CHARACTER";
            string headerTitle = "";
            Vector4? npCol = null;
            if (IsEditWindowOpen && selectedCharacterIndex >= 0 && selectedCharacterIndex < plugin.Characters.Count)
            {
                var ch = plugin.Characters[selectedCharacterIndex];
                headerTitle = (ch.Name ?? "").ToUpperInvariant();
                if (ch.NameplateColor.LengthSquared() > 0.001f)
                    npCol = new Vector4(ch.NameplateColor.X, ch.NameplateColor.Y, ch.NameplateColor.Z, 1f);
            }

            var dl = ImGui.GetWindowDrawList();
            ImGui.SetCursorPosX(_formIndent);
            var rowStart = ImGui.GetCursorScreenPos();
            // Title-row chrome (kicker + X close) spans the FULL available width
            // from indent to the BeginChild's right edge, not the field-content
            // width, so the X sits flush against the right-edge scrollbar.
            float availW = ImGui.GetContentRegionAvail().X;
            float rowH = 32f * fs;

            ImFontPtr kickerFont, titleFont;
            using (Plugin.Instance?.OswaldMed11?.Push())     { kickerFont = ImGui.GetFont(); }
            using (Plugin.Instance?.OswaldSemiSmall?.Push()) { titleFont  = ImGui.GetFont(); }

            float midY = rowStart.Y + rowH * 0.5f;
            float cursorX = rowStart.X;

            // Both texts render at the form's body font (OutfitMed12, 15.6px),
            // NOT at OswaldMed11/SemiSmall. So compute pos.Y once based on the
            // kicker's intended caps-centre and use it for both texts. This is
            // what makes them share a vertical line.
            const float capCenterRatio = 0.465f;
            float textY = midY - kickerFont.FontSize * capCenterRatio;

            float kickerTrack = kickerFont.FontSize * 0.32f;
            float kickerW = Boutique.MeasureTrackedText(kicker, kickerTrack);
            Boutique.DrawTrackedText(dl,
                new Vector2(cursorX, textY),
                kicker, Boutique.U32(Boutique.TextDim), kickerTrack);
            // Tighter gap from kicker to diamond, shifts the diamond, pip,
            // and name LEFT as a group (was 12*fs).
            cursorX += kickerW + 6f * fs;

            // Diamond / pip sit at the visual cap centre of the actual rendered
            // text (OutfitMed12).
            float diamondCY = midY + 2f * fs;

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
            cursorX += 14f * fs;

            if (npCol.HasValue)
            {
                Boutique.DrawSquarePip(dl,
                    new Vector2(cursorX + 4f * fs, diamondCY), 4f * fs, npCol.Value);
                cursorX += 16f * fs;
            }

            if (!string.IsNullOrEmpty(headerTitle))
            {
                float titleTrack = titleFont.FontSize * 0.20f;
                Boutique.DrawTrackedText(dl,
                    new Vector2(cursorX, textY),
                    headerTitle, Boutique.U32(Boutique.Text), titleTrack);
            }

            float xSize = 24f * fs;
            var xMin = new Vector2(rowStart.X + availW - xSize, midY - xSize * 0.5f);
            ImGui.SetCursorScreenPos(xMin);
            bool xClicked = ImGui.InvisibleButton("##bform_close_inline", new Vector2(xSize, xSize));
            bool xHovered = ImGui.IsItemHovered();
            uint xBg = xHovered
                ? Boutique.U32(Boutique.WithAlpha(Boutique.Red, 0.20f))
                : Boutique.U32(new Vector4(20f / 255f, 24f / 255f, 32f / 255f, 0.6f));
            dl.AddRectFilled(xMin, xMin + new Vector2(xSize, xSize), xBg);
            dl.AddRect(xMin, xMin + new Vector2(xSize, xSize),
                Boutique.U32(xHovered ? Boutique.Red : Boutique.BorderSoft),
                0f, ImDrawFlags.None, 1f * fs);
            ImGui.PushFont(UiBuilder.IconFont);
            string xGlyph = "";
            var xs = ImGui.CalcTextSize(xGlyph);
            ImGui.PopFont();
            float xIconSize = 12f * fs;
            float xScaleR = xIconSize / UiBuilder.IconFont.FontSize;
            dl.AddText(UiBuilder.IconFont, xIconSize,
                xMin + new Vector2((xSize - xs.X * xScaleR) * 0.5f, (xSize - xs.Y * xScaleR) * 0.5f),
                Boutique.U32(xHovered ? Boutique.Red : Boutique.TextDim), xGlyph);
            if (xClicked) CloseForm();

            ImGui.SetCursorScreenPos(rowStart);
            ImGui.Dummy(new Vector2(availW, rowH));
        }

        // Inline footer row: CANCEL (left of save) + SAVE pill (right). No bg, no
        // hairline, no enclosing footer bar; just two buttons in their natural flow.
        private void DrawInlineFooterButtons(float fs)
        {
            string vName = IsEditWindowOpen ? editedCharacterName : plugin.NewCharacterName;
            string vPenumbra = IsEditWindowOpen ? editedCharacterPenumbra : plugin.NewPenumbraCollection;
            string vGlamourer = IsEditWindowOpen ? editedCharacterGlamourer : plugin.NewGlamourerDesign;
            bool canSave = !string.IsNullOrWhiteSpace(vName)
                        && !string.IsNullOrWhiteSpace(vPenumbra)
                        && !string.IsNullOrWhiteSpace(vGlamourer)
                        && string.IsNullOrEmpty(nameValidationError);

            string disabledReason = null;
            if (!canSave)
            {
                if (!string.IsNullOrEmpty(nameValidationError))
                    disabledReason = nameValidationError;
                else if (string.IsNullOrWhiteSpace(vName))
                    disabledReason = "Enter a character name first.";
                else if (string.IsNullOrWhiteSpace(vPenumbra))
                    disabledReason = "Pick a Penumbra collection first.";
                else if (string.IsNullOrWhiteSpace(vGlamourer))
                    disabledReason = "Pick a Glamourer design first.";
            }

            // Lift the buttons off the bottom edge, bigger top breathing
            // space than before. Trailing dummy below the buttons keeps them
            // off the BeginChild's bottom edge when the user scrolls down.
            ImGui.Dummy(new Vector2(0f, 22f * fs));

            var dl = ImGui.GetWindowDrawList();
            ImGui.SetCursorPosX(_formIndent);
            float availW = _formContentWidth;
            float btnH = 32f * fs;
            float cancelW = 110f * fs;
            float saveW   = 160f * fs;
            float gap     = 10f * fs;

            var rowStart = ImGui.GetCursorScreenPos();

            // LEFT-aligned: Cancel first, Save right of it. Anchored at _formIndent.
            // No Oswald font push, gold pills use the default ImGui font (same
            // as the main window's "ADD CHARACTER" pill) for consistent look.
            var cancelMin = new Vector2(rowStart.X, rowStart.Y);
            var cancelMax = cancelMin + new Vector2(cancelW, btnH);
            if (Boutique.DrawCancelBtn(dl, cancelMin, cancelMax,
                    "CANCEL", 1.6f * fs, fs, "charform", ImGui.GetFont()))
            {
                CloseForm();
            }

            var saveMin = new Vector2(cancelMax.X + gap, rowStart.Y);
            var saveMax = saveMin + new Vector2(saveW, btnH);
            if (Boutique.DrawSavePill(dl, saveMin, saveMax,
                    IsEditWindowOpen ? "SAVE CHANGES" : "SAVE CHARACTER",
                    1.8f * fs, fs,
                    IsEditWindowOpen ? "char_edit" : "char_new",
                    !canSave, uiStyles.UpdateAndGetHoverSweepProgress, disabledReason)
                && canSave)
            {
                if (IsEditWindowOpen)
                {
                    SaveEditedCharacter();
                }
                else
                {
                    string finalMacro = isAdvancedModeCharacter
                        ? advancedCharacterMacroText
                        : plugin.NewCharacterMacros;
                    var created = plugin.SaveNewCharacter(finalMacro);
                    ApplyEditedFramingToNew(created);
                }
                CloseForm();
            }
            plugin.SaveButtonPos = saveMin;
            plugin.SaveButtonSize = saveMax - saveMin;

            ImGui.SetCursorScreenPos(rowStart);
            ImGui.Dummy(new Vector2(availW, btnH));
            // Trailing breathing space so buttons aren't glued to the bottom
            // edge of the BeginChild when scrolled.
            ImGui.Dummy(new Vector2(0f, 18f * fs));
        }
        private void DrawCharacterFormContent(float scale)
        {
            float labelWidth = 130 * scale;
            float inputWidth = 250 * scale;
            float inputOffset = 10 * scale;

            // Section divider, OswaldSemiSmall (20.8px) so the section is the
            // largest non-title text in the form and clearly separates groups.
            // Hairline extends to the FULL right edge (chrome), fields below
            // stay capped to _formContentWidth.
            bool firstSection = true;
            void Section(string label)
            {
                if (!firstSection)
                    ImGui.Dummy(new Vector2(0f, 6f * Boutique.FormScale));
                firstSection = false;
                ImGui.SetCursorPosX(_formIndent);
                float dividerWidth = ImGui.GetContentRegionAvail().X;
                using (Plugin.Instance?.OswaldSemiSmall?.Push())
                {
                    Boutique.DrawSimpleSectionLabel(label.ToUpperInvariant(), scale, dividerWidth);
                }
            }

            // ─── I · IDENTITY ───
            Section("Identity");

            // Row 1: Name (+optional Alias)
            var idCols = new List<FieldCol>();
            idCols.Add(Col(2f, "Character Name", true,
                "Enter your OC's name or nickname for profile here.",
                w => DrawCharacterNameInput(w, scale)));

            if (plugin.Configuration.EnableNameReplacement || plugin.Configuration.EnableSharedNameReplacement)
            {
                idCols.Add(Col(1f, "Alias", false,
                    "Optional alias used for Name Sync.\nIf set, this name is displayed instead of Character Name.\nLeave empty to use the Character Name.",
                    w => DrawCharacterAliasInput(w)));
            }
            DrawFieldRow(scale, idCols.ToArray());

            // Row 2: Tags + Nameplate Colour
            DrawFieldRow(scale,
                Col(2f, "Character Tags", false,
                    "You can assign multiple tags by separating them with commas.\nExamples: Casual, Favourites, Seasonal",
                    w => DrawCharacterTagsInput(w)),
                Col(1f, "Nameplate Colour", false,
                    "Affects your character's nameplate under their profile picture.",
                    w => DrawNameplateColourInput(scale)));

            // Per v2-simple spec: Exclude from Name Sync toggle on its own row at the
            // end of Identity, not inline as the Name field's afterInput.
            if (plugin.Configuration.AllowOthersToSeeMyCSName)
                DrawExcludeFromNameSyncToggle(scale);

#if DEV_BUILD
            // Glitch Pack is gated behind the achievement shop in public builds.
            // The toggle UI only renders in dev builds; the underlying field is
            // always serialised so dev-build profiles round-trip cleanly.
            DrawGlitchNameEffectToggle(scale);
#endif

            // ─── II · INTEGRATIONS ───
            Section("Integrations");

            DrawFieldRow(scale,
                Col(1f, "Penumbra Collection", true,
                    "Select the Penumbra collection for this character. Right-click to clear.",
                    w => DrawPenumbraInput(w)),
                Col(1f, "Glamourer Design", true,
                    "Select the Glamourer design for this character. Right-click to clear.\nYou can add additional designs later.",
                    w => DrawGlamourerInput(w)));

            var integCols = new List<FieldCol>();
            if (plugin.Configuration.EnableAutomations)
            {
                integCols.Add(Col(1f, "Glam. Automation", false,
                    "Enter the name of a Glamourer Automation for this character.\nMust match the automation name EXACTLY as shown in Glamourer.\nDesign-level automations override this if both are set.",
                    w => DrawAutomationInput(w)));
            }
            integCols.Add(Col(1f, "Customize+ Profile", false,
                "Select the Customize+ profile for this character. Right-click to clear.",
                w => DrawCustomizeInput(w)));
            DrawFieldRow(scale, integCols.ToArray());

            // ─── III · HONORIFIC ───
            Section("Honorific");
            DrawHonorificSection(labelWidth, inputWidth, inputOffset, scale);

            // ─── IV · ENHANCEMENTS ───
            Section("Enhancements");

            var enhCols = new List<FieldCol>();
            enhCols.Add(Col(1f, "Moodle Preset", false,
                "Select the Moodle preset for this character. Right-click to clear.",
                w => DrawMoodleInput(w)));
            enhCols.Add(Col(1f, "Idle Pose", false,
                "Sets your character's idle pose (0-6).\nChoose 'None' if you don't want Character Select+ to change your idle.",
                w => DrawIdlePoseInput(w)));
            if (plugin.Configuration.EnableGearsetAssignments)
            {
                enhCols.Add(Col(1f, "Gearset", false,
                    "Automatically switch to this gearset when applying this character.\nChoose 'None' to not change gearsets.",
                    w => DrawGearsetInput(w)));
            }
            DrawFieldRow(scale, enhCols.ToArray());

            // Mod Manager (Conflict Resolution), simple boutique checkbox per
            // v2-simple spec (no special gold-deep left bar chip).
            if (plugin.Configuration.EnableConflictResolution)
            {
                ImFontPtr crLblF, crDescF;
                using (Plugin.Instance?.OutfitMed13?.Push()) { crLblF  = ImGui.GetFont(); }
                using (Plugin.Instance?.OutfitMed13?.Push()) { crDescF = ImGui.GetFont(); }
                ImGui.SetCursorPosX(_formIndent);
                Boutique.DrawBoutiqueCheckbox(
                    "use_cr", ref isSecretMode,
                    "Use Conflict Resolution",
                    "Manual mod state for this character",
                    scale, crLblF, crDescF);

                if (isSecretMode)
                {
                    DrawSecretModeModsField(labelWidth, inputWidth, inputOffset, scale);
                }
            }

            Section("Portrait");
            DrawImageSelection(scale);
            DrawHoverModeSelection(scale);

            Section("Advanced Mode");
            DrawAdvancedModeSection(scale);
            // Action buttons are now in the boutique footer (Draw method); not rendered here.
        }

        // ─── Identity inputs ───
        private void DrawCharacterNameInput(float width, float scale)
        {
            string tempName = IsEditWindowOpen ? editedCharacterName : plugin.NewCharacterName;

            if (Boutique.DrawBoutiqueTextInput("##CharacterName", ref tempName, 50, width))
            {
                if (IsEditWindowOpen) editedCharacterName = tempName;
                else plugin.NewCharacterName = tempName;
                ValidateCharacterName(tempName);
            }
            plugin.CharacterNameFieldPos = ImGui.GetItemRectMin();
            plugin.CharacterNameFieldSize = ImGui.GetItemRectSize();

            if (!string.IsNullOrEmpty(nameValidationError))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.3f, 0.3f, 1.0f));
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
                ImGui.TextWrapped(nameValidationError);
                ImGui.PopTextWrapPos();
                ImGui.PopStyleColor();
            }
        }

        private void DrawExcludeFromNameSyncToggle(float scale)
        {
            bool tempExclude = IsEditWindowOpen ? editedCharacterExcludeFromNameSync : plugin.NewCharacterExcludeFromNameSync;
            ImFontPtr labelF, descF;
            // Both label + description at OutfitMed13 (16.9px Medium weight).
            // Medium weight (vs Regular) eliminates the thin-stroke readability
            // issue. Description differentiated only by colour (TextDim).
            using (Plugin.Instance?.OutfitMed13?.Push()) { labelF = ImGui.GetFont(); }
            using (Plugin.Instance?.OutfitMed13?.Push()) { descF  = ImGui.GetFont(); }
            ImGui.SetCursorPosX(_formIndent);
            if (Boutique.DrawBoutiqueCheckbox(
                "exclude_namesync", ref tempExclude,
                "Exclude from Name Sync",
                "Skip Name Sync for this character",
                scale, labelF, descF))
            {
                if (IsEditWindowOpen) editedCharacterExcludeFromNameSync = tempExclude;
                else plugin.NewCharacterExcludeFromNameSync = tempExclude;
            }
            if (ImGui.IsItemHovered())
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("When checked, Name Sync won't apply to this character.");
        }

        private void DrawGlitchNameEffectToggle(float scale)
        {
            bool tempGlitch = IsEditWindowOpen ? editedCharacterUseGlitchNameEffect : plugin.NewCharacterUseGlitchNameEffect;
            ImFontPtr labelF, descF;
            using (Plugin.Instance?.OutfitMed13?.Push()) { labelF = ImGui.GetFont(); }
            using (Plugin.Instance?.OutfitMed13?.Push()) { descF  = ImGui.GetFont(); }
            ImGui.SetCursorPosX(_formIndent);
            if (Boutique.DrawBoutiqueCheckbox(
                "glitch_name_effect", ref tempGlitch,
                "Glitch Name Effect",
                "Renders the name in SD Glitch with a periodic chromatic burst",
                scale, labelF, descF))
            {
                if (IsEditWindowOpen) editedCharacterUseGlitchNameEffect = tempGlitch;
                else plugin.NewCharacterUseGlitchNameEffect = tempGlitch;
            }
            if (ImGui.IsItemHovered())
                CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip(
                    "Applies to this character's name in the character card and RP profile.\n" +
                    "Every few seconds the name briefly glitches with cyan/magenta\n" +
                    "chromatic ghosts and a letter-scramble pulse.");
        }

        private void DrawCharacterAliasInput(float width)
        {
            string tempAlias = IsEditWindowOpen ? editedCharacterAlias : plugin.NewCharacterAlias;
            if (Boutique.DrawBoutiqueTextInput("##CharacterAlias", ref tempAlias, 100, width, "Optional"))
            {
                if (IsEditWindowOpen) editedCharacterAlias = tempAlias;
                else plugin.NewCharacterAlias = tempAlias;
            }
        }

        private void DrawCharacterTagsInput(float width)
        {
            string tempTag = IsEditWindowOpen ? editedCharacterTag : plugin.NewCharacterTag;
            if (Boutique.DrawBoutiqueTextInput("##Tags", ref tempTag, 100, width, "e.g. Casual, Battle, Beach"))
            {
                if (IsEditWindowOpen) editedCharacterTag = tempTag;
                else plugin.NewCharacterTag = tempTag;
            }

            // Live chips below the input (mockup .tag-chips)
            if (!string.IsNullOrWhiteSpace(tempTag))
            {
                DrawTagChips(tempTag, width);
            }
        }

        // Boutique tag chips: small gold-tinted pills with tracked-caps text. Wraps to
        // multiple rows when chips exceed the input width.
        private void DrawTagChips(string commaSeparated, float maxWidth)
        {
            float fs = Boutique.FormScale;
            var tags = commaSeparated
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToUpperInvariant())
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct()
                .ToArray();
            if (tags.Length == 0) return;

            // Bumped from OswaldSemi9 (11.7px) to OswaldSemi11 (14.3px), the
            // chips were unreadable at the smaller size. Padding bumped slightly
            // to keep the chip visually balanced with the bigger glyphs.
            ImFontPtr font;
            using (Plugin.Instance?.OswaldSemi11?.Push()) { font = ImGui.GetFont(); }

            float chipPadX = 9f * fs;
            float chipPadY = 4f * fs;
            float chipGap = 6f * fs;

            var dl = ImGui.GetWindowDrawList();
            var rowStart = ImGui.GetCursorScreenPos() + new Vector2(0f, 4f * fs);
            var pos = rowStart;
            float maxRowY = pos.Y;

            foreach (var tag in tags)
            {
                ImGui.PushFont(font);
                var ts = ImGui.CalcTextSize(tag);
                ImGui.PopFont();
                float chipW = ts.X + chipPadX * 2f;
                float chipH = ts.Y + chipPadY * 2f;

                // Wrap to next row if it would exceed maxWidth
                if (pos.X + chipW > rowStart.X + maxWidth && pos.X > rowStart.X)
                {
                    pos = new Vector2(rowStart.X, pos.Y + chipH + 4f * fs);
                }

                var chipMin = pos;
                var chipMax = pos + new Vector2(chipW, chipH);

                dl.AddRectFilled(chipMin, chipMax,
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.06f)));
                dl.AddRect(chipMin, chipMax,
                    Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.22f)),
                    0f, ImDrawFlags.None, 1f * fs);
                dl.AddText(font, font.FontSize,
                    chipMin + new Vector2(chipPadX, chipPadY),
                    Boutique.U32(Boutique.GoldWarm), tag);

                pos.X += chipW + chipGap;
                if (chipMax.Y > maxRowY) maxRowY = chipMax.Y;
            }

            // Reserve vertical space so the next field flows below the chips
            float consumedH = (maxRowY - rowStart.Y) + 4f * fs;
            ImGui.Dummy(new Vector2(maxWidth, consumedH));
        }

        private void DrawNameplateColourInput(float scale)
        {
            Vector3 tempColor = IsEditWindowOpen ? editedCharacterColor : plugin.NewCharacterColor;
            if (CharacterSelectPlugin.Windows.Styles.Boutique.DrawBoutiqueColorSwatch(
                "NameplateColor", ref tempColor, scale))
            {
                if (IsEditWindowOpen) editedCharacterColor = tempColor;
                else plugin.NewCharacterColor = tempColor;
            }
            DrawSwatchHexAfter(scale,
                $"#{(int)(tempColor.X * 255):X2}{(int)(tempColor.Y * 255):X2}{(int)(tempColor.Z * 255):X2}");
        }

        // Centers a small hex/label text vertically against the 28*scale boutique
        // swatch on the same line. Replaces AlignTextToFramePadding which only
        // aligns to FramePadding.y, not against the chamfered 28×28 swatch.
        private void DrawSwatchHexAfter(float scale, string label)
        {
            ImGui.SameLine(0f, 8f * scale);
            float swatchH = 28f * scale;
            float fontH = ImGui.GetFontSize();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (swatchH - fontH) * 0.5f);
            ImGui.TextColored(Boutique.TextFaint, label);
        }

        // ─── Integrations inputs ───
        private void DrawPenumbraInput(float width)
        {
            string tempPenumbra = IsEditWindowOpen ? editedCharacterPenumbra : plugin.NewPenumbraCollection;
            string oldValue = tempPenumbra;

            var penumbraOptions = plugin.IntegrationListProvider?.GetPenumbraCollections() ?? Array.Empty<string>();
            var currentPenumbra = plugin.IntegrationListProvider?.GetCurrentPenumbraCollection();

            bool changed = AutocompleteCombo.Draw("##PenumbraCollection", ref tempPenumbra, penumbraOptions, width,
                "Select collection...", currentActive: currentPenumbra);
            plugin.PenumbraFieldPos = ImGui.GetItemRectMin();
            plugin.PenumbraFieldSize = ImGui.GetItemRectSize();

            if (changed)
            {
                if (IsEditWindowOpen)
                {
                    editedCharacterPenumbra = tempPenumbra;
                    if (isAdvancedModeCharacter) UpdateAdvancedMacroPenumbra(tempPenumbra);
                    else editedCharacterMacros = GenerateMacro();
                }
                else
                {
                    plugin.NewPenumbraCollection = tempPenumbra;
                    if (isAdvancedModeCharacter)
                    {
                        UpdateAdvancedMacroPenumbra(tempPenumbra);
                        plugin.NewCharacterMacros = advancedCharacterMacroText;
                    }
                    else
                    {
                        plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                    }
                }
            }
        }

        private void DrawGlamourerInput(float width)
        {
            string tempGlamourer = IsEditWindowOpen ? editedCharacterGlamourer : plugin.NewGlamourerDesign;
            string oldValue = tempGlamourer;

            var glamourerOptions = plugin.IntegrationListProvider?.GetGlamourerDesigns() ?? Array.Empty<string>();

            bool changed = AutocompleteCombo.Draw("##GlamourerDesign", ref tempGlamourer, glamourerOptions, width, "Select design...");
            plugin.GlamourerFieldPos = ImGui.GetItemRectMin();
            plugin.GlamourerFieldSize = ImGui.GetItemRectSize();

            if (changed)
            {
                if (IsEditWindowOpen)
                {
                    editedCharacterGlamourer = tempGlamourer;
                    if (isAdvancedModeCharacter) UpdateAdvancedMacroGlamourer(oldValue, tempGlamourer);
                    else editedCharacterMacros = GenerateMacro();
                }
                else
                {
                    plugin.NewGlamourerDesign = tempGlamourer;
                    if (isAdvancedModeCharacter)
                    {
                        UpdateAdvancedMacroGlamourer(oldValue, tempGlamourer);
                        plugin.NewCharacterMacros = advancedCharacterMacroText;
                    }
                    else
                    {
                        plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                    }
                }
            }
        }

        private void DrawAutomationInput(float width)
        {
            string tempAutomation = IsEditWindowOpen ? editedCharacterAutomation : plugin.NewCharacterAutomation;
            if (Boutique.DrawBoutiqueTextInput("##Glam.Automation", ref tempAutomation, 100, width, "Exact name"))
            {
                if (IsEditWindowOpen)
                {
                    editedCharacterAutomation = tempAutomation;
                    if (isAdvancedModeCharacter) UpdateAdvancedMacroAutomation(tempAutomation);
                    else editedCharacterMacros = GenerateMacro();
                }
                else
                {
                    plugin.NewCharacterAutomation = tempAutomation;
                    if (isAdvancedModeCharacter)
                    {
                        UpdateAdvancedMacroAutomation(tempAutomation);
                        plugin.NewCharacterMacros = advancedCharacterMacroText;
                    }
                    else
                    {
                        plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                    }
                }
            }
        }

        private void DrawCustomizeInput(float width)
        {
            string tempCustomize = IsEditWindowOpen ? editedCharacterCustomize : plugin.NewCustomizeProfile;
            var customizeOptions = plugin.IntegrationListProvider?.GetCustomizePlusProfiles() ?? Array.Empty<string>();
            var currentCustomize = plugin.IntegrationListProvider?.GetCurrentCustomizePlusProfile();

            if (AutocompleteCombo.Draw("##CustomizeProfile", ref tempCustomize, customizeOptions, width, "Select profile...", currentActive: currentCustomize))
            {
                if (IsEditWindowOpen)
                {
                    editedCharacterCustomize = tempCustomize;
                    if (isAdvancedModeCharacter) UpdateAdvancedMacroCustomize(tempCustomize);
                    else editedCharacterMacros = GenerateMacro();
                }
                else
                {
                    plugin.NewCustomizeProfile = tempCustomize;
                    if (isAdvancedModeCharacter)
                    {
                        UpdateAdvancedMacroCustomize(tempCustomize);
                        plugin.NewCharacterMacros = advancedCharacterMacroText;
                    }
                    else
                    {
                        plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                    }
                }
            }
        }

        // ─── Enhancements inputs ───
        private void DrawMoodleInput(float width)
        {
            var moodleOptions = plugin.IntegrationListProvider?.GetMoodlesPresets() ?? Array.Empty<string>();
            if (AutocompleteCombo.Draw("##MoodlePreset", ref tempMoodlePreset, moodleOptions, width, "Select preset..."))
            {
                if (IsEditWindowOpen) editedCharacterMoodlePreset = tempMoodlePreset;
                else plugin.NewCharacterMoodlePreset = tempMoodlePreset;

                if (isAdvancedModeCharacter)
                {
                    UpdateAdvancedMacroMoodle(tempMoodlePreset);
                    if (!IsEditWindowOpen) plugin.NewCharacterMacros = advancedCharacterMacroText;
                }
                else
                {
                    if (IsEditWindowOpen) editedCharacterMacros = GenerateMacro();
                    else plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                }
            }
        }

        private void DrawIdlePoseInput(float width)
        {
            string[] poseOptions = { "None", "Pose 1", "Pose 2", "Pose 3", "Pose 4", "Pose 5", "Pose 6", "Pose 7" };
            byte storedIndex = IsEditWindowOpen
                ? plugin.Characters[selectedCharacterIndex].IdlePoseIndex
                : plugin.NewCharacterIdlePoseIndex;
            int dropdownIndex = storedIndex >= 7 ? 0 : storedIndex + 1;

            string current = poseOptions[dropdownIndex];
            string previous = current;
            if (AutocompleteCombo.Draw("##IdlePose", ref current, poseOptions, width, "Select pose...", allowCustomInput: false))
            {
                int newDropdown = Array.IndexOf(poseOptions, current);
                if (newDropdown < 0) newDropdown = 0;
                byte newIndex = (byte)(newDropdown == 0 ? 7 : newDropdown - 1);

                byte currentIndex = IsEditWindowOpen
                    ? plugin.Characters[selectedCharacterIndex].IdlePoseIndex
                    : plugin.NewCharacterIdlePoseIndex;

                if (currentIndex != newIndex)
                {
                    if (IsEditWindowOpen) plugin.Characters[selectedCharacterIndex].IdlePoseIndex = newIndex;
                    else plugin.NewCharacterIdlePoseIndex = newIndex;

                    if (isAdvancedModeCharacter)
                    {
                        UpdateAdvancedMacroIdlePose(newIndex);
                        if (!IsEditWindowOpen) plugin.NewCharacterMacros = advancedCharacterMacroText;
                    }
                    else
                    {
                        if (IsEditWindowOpen) editedCharacterMacros = GenerateMacro();
                        else plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
                    }
                }
            }
        }

        private void DrawGearsetInput(float width)
        {
            var gearsets = plugin.GetPlayerGearsets();
            int? currentGearset = IsEditWindowOpen ? editedCharacterGearset : plugin.NewCharacterGearset;

            // Build display strings + lookup
            var displayList = new List<string> { "None" };
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

            string current = "None";
            if (currentGearset.HasValue)
            {
                var match = gearsets.FirstOrDefault(g => g.Number == currentGearset.Value);
                if (match.Number > 0)
                    current = plugin.GetGearsetDisplayName(match.Number, match.JobId, match.Name);
                else
                    current = $"Gearset {currentGearset.Value}";
            }

            if (AutocompleteCombo.Draw("##AssignedGearset", ref current, displayList, width, "Select gearset...", allowCustomInput: false))
            {
                int? newValue;
                if (current == "None") newValue = null;
                else if (displayToNumber.TryGetValue(current, out int n)) newValue = n;
                else newValue = currentGearset; // unknown, keep prior

                if (IsEditWindowOpen) editedCharacterGearset = newValue;
                else plugin.NewCharacterGearset = newValue;
            }
        }

        private void DrawFormField(string label, float labelWidth, float inputWidth, float inputOffset,
                                 System.Action drawInput, string tooltip, float scale, System.Action? afterTooltip = null)
        {
            // Boutique label above the input (tracked-caps Oswald, optional "*" required + info tooltip)
            bool required = label.EndsWith("*");
            string clean = required ? label.TrimEnd('*').TrimEnd() : label;

            // Respect the form's left indent so this field aligns with field rows
            // rendered via DrawFieldRow (e.g. when CR is enabled the Mod Manager
            // single-column field needs to line up with the rest).
            ImGui.SetCursorPosX(_formIndent);
            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                CharacterSelectPlugin.Windows.Styles.Boutique.DrawFieldLabel(
                    clean.ToUpperInvariant(), required, tooltip);
            }

            // Input on its own row below the label, indented to match the label
            ImGui.SetCursorPosX(_formIndent);
            ImGui.SetNextItemWidth(inputWidth > 0 ? inputWidth : MathF.Min(_formContentWidth, ImGui.GetContentRegionAvail().X));
            drawInput();

            afterTooltip?.Invoke();
            // ItemSpacing.y from PushFormStyle (= 5 * fs) provides the 5px label-to-next-label
            // rhythm; an explicit Dummy of 5*fs raises field-to-field gap to 10px (mockup
            // .section { gap: 10px } between stacked .field children).
            ImGui.Dummy(new Vector2(0, 5f * Boutique.FormScale));
        }

        // ─── Field-row layout (mockup .field-row with flex children) ───
        // A column spec for DrawFieldRow. drawInput receives the computed input
        // width so combos / inputs can size themselves. afterInput runs after
        // the input on the same column (e.g. inline checkbox).
        private struct FieldCol
        {
            public float Flex;
            public string Label;
            public bool Required;
            public string? Tooltip;
            public Action<float> DrawInput;
            public Action? AfterInput;
        }

        private static FieldCol Col(float flex, string label, bool required, string? tooltip,
            Action<float> drawInput, Action? afterInput = null)
            => new FieldCol { Flex = flex, Label = label, Required = required, Tooltip = tooltip,
                              DrawInput = drawInput, AfterInput = afterInput };

        // Renders multiple labeled fields side by side at the given flex weights.
        // Each column = label-above-input, sized proportionally. A single col
        // collapses to full-width.
        private void DrawFieldRow(float scale, params FieldCol[] cols)
        {
            if (cols == null || cols.Length == 0) return;

            // Indent the row to the form's left edge + use the form's content
            // width budget rather than the parent's full width (which would be
            // the whole BeginChild minus scrollbar).
            ImGui.SetCursorPosX(_formIndent);
            float availW = _formContentWidth;
            float fs = Boutique.FormScale;
            float gap = 10f * fs;
            float totalGap = cols.Length > 1 ? gap * (cols.Length - 1) : 0f;
            float totalFlex = 0f;
            for (int i = 0; i < cols.Length; i++) totalFlex += cols[i].Flex;
            if (totalFlex <= 0f) totalFlex = cols.Length;

            // Cap individual input widths so they don't sprawl. A name field
            // doesn't need 500px of pixels, 240*fs (~310px) covers ~30 chars
            // of body text comfortably. Wider columns leave empty space rather
            // than stretching the input.
            float maxInputW = 260f * fs;
            float usableW = availW - totalGap;
            for (int i = 0; i < cols.Length; i++)
            {
                if (i > 0) ImGui.SameLine(0f, gap);
                float colW = usableW * cols[i].Flex / totalFlex;
                float inputW = MathF.Min(colW, maxInputW);

                ImGui.BeginGroup();

                // Field label = OswaldSemi13 (= 16.9px) tracked 0.20em, TextDim.
                // Bumped substantially so the LABEL dominates the row, not the input.
                using (Plugin.Instance?.OswaldSemi13?.Push())
                {
                    CharacterSelectPlugin.Windows.Styles.Boutique.DrawFieldLabel(
                        cols[i].Label.ToUpperInvariant(), cols[i].Required, cols[i].Tooltip);
                }

                ImGui.SetNextItemWidth(inputW);
                cols[i].DrawInput?.Invoke(inputW);

                cols[i].AfterInput?.Invoke();

                ImGui.EndGroup();
            }
            // Tighter inter-row breathing, let ItemSpacing.y carry the gap +
            // a small explicit dummy. Less whitespace = more information density.
            ImGui.Dummy(new Vector2(0f, 1f * Boutique.FormScale));
        }

        private void DrawSecretModeModsField(float labelWidth, float inputWidth, float inputOffset, float scale)
        {
            DrawFormField("Mod Manager", labelWidth, inputWidth, inputOffset, () =>
            {
                var selectedCount = IsEditWindowOpen && plugin.Characters[selectedCharacterIndex].SecretModState != null
                    ? plugin.Characters[selectedCharacterIndex].SecretModState.Count
                    : (plugin.NewSecretModState?.Count ?? 0);

                var buttonText = selectedCount > 0
                    ? $"Configure Mods ({selectedCount} selected)###SecretMods"
                    : "Configure Mods###SecretMods";

                // Validate that character name is filled before opening mod manager
                string characterName = IsEditWindowOpen ? editedCharacterName : plugin.NewCharacterName;
                bool hasValidName = !string.IsNullOrWhiteSpace(characterName);

                // Reserve space for the quick-update refresh button on the same row
                float refreshButtonWidth = 30f * scale;
                float buttonGap = 4f * scale;
                float configureButtonWidth = inputWidth - refreshButtonWidth - buttonGap;

                if (!hasValidName)
                    ImGui.BeginDisabled();

                if (ImGui.Button(buttonText, new Vector2(configureButtonWidth, 0)))
                {
                    if (hasValidName)
                    {
                        // Open the Secret Mode mod selection window
                        if (plugin.SecretModeModWindow == null)
                        {
                            plugin.SecretModeModWindow = new SecretModeModWindow(plugin);
                            plugin.WindowSystem.AddWindow(plugin.SecretModeModWindow);
                        }

                        Dictionary<string, bool>? currentSelection = null;
                        HashSet<string>? currentPins = null;
                        if (IsEditWindowOpen)
                        {
                            currentSelection = plugin.Characters[selectedCharacterIndex].SecretModState;
                            currentPins = plugin.Characters[selectedCharacterIndex].SecretModPins != null ? new HashSet<string>(plugin.Characters[selectedCharacterIndex].SecretModPins) : null;
                            Plugin.Log.Information($"[PIN DEBUG] Character form loading pins for character {selectedCharacterIndex}: {currentPins?.Count ?? 0} pins - {string.Join(", ", currentPins ?? new HashSet<string>())}");
                        }
                        else
                        {
                            currentSelection = plugin.NewSecretModState;
                            currentPins = plugin.NewSecretModPins != null ? new HashSet<string>(plugin.NewSecretModPins) : null;
                            Plugin.Log.Information($"[PIN DEBUG] Character form loading pins for new character: {currentPins?.Count ?? 0} pins - {string.Join(", ", currentPins ?? new HashSet<string>())}");
                        }

                        Plugin.Log.Information($"[PIN DEBUG] About to pass pins to mod manager: {currentPins?.Count ?? 0} pins - {string.Join(", ", currentPins ?? new HashSet<string>())}");
                        plugin.SecretModeModWindow.Open(
                            IsEditWindowOpen ? selectedCharacterIndex : null,
                            currentSelection,
                            currentPins,
                            (selection) =>
                            {
                                if (IsEditWindowOpen)
                                {
                                    plugin.Characters[selectedCharacterIndex].SecretModState = selection;
                                    plugin.SaveConfiguration();
                                }
                                else
                                {
                                    plugin.NewSecretModState = selection;
                                }
                            },
                            (pins) =>
                            {
                                if (IsEditWindowOpen)
                                {
                                    Plugin.Log.Information($"[PIN DEBUG] Character save callback: saving {pins?.Count ?? 0} pins to character {selectedCharacterIndex}");
                                    plugin.Characters[selectedCharacterIndex].SecretModPins = pins?.ToList();
                                    plugin.SaveConfiguration();
                                }
                                else
                                {
                                    Plugin.Log.Information($"[PIN DEBUG] New character save callback: saving {pins?.Count ?? 0} pins to NewSecretModPins");
                                    plugin.NewSecretModPins = pins?.ToList();
                                }
                            },
                            null,  // No design context for character-level operations
                            characterName,  // Pass the character name for context
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

                if (!hasValidName)
                {
                    ImGui.EndDisabled();

                    // Show tooltip explaining why the button is disabled
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        ImGui.BeginTooltip();
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.7f, 0.7f, 1.0f));
                        ImGui.Text("Please enter a Character Name before configuring mods.");
                        ImGui.PopStyleColor();
                        ImGui.EndTooltip();
                    }
                }

                // Quick update refresh button - same pattern as the Designs panel. Pulls in the
                // currently-affecting gear/hair mods without having to open the mod manager window.
                ImGui.SameLine(0, buttonGap);
                ImGui.PushFont(UiBuilder.IconFont);

                bool canQuickUpdate = hasValidName && plugin.Configuration.EnableConflictResolution;

                if (!canQuickUpdate)
                    ImGui.BeginDisabled();

                if (ImGui.Button("\uf2f1##CharacterQuickUpdate", new Vector2(refreshButtonWidth, 0)))
                {
                    if (canQuickUpdate)
                    {
                        PerformQuickCharacterGearHairUpdate();
                    }
                }

                if (!canQuickUpdate)
                    ImGui.EndDisabled();

                ImGui.PopFont();

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.BeginTooltip();
                    if (canQuickUpdate)
                    {
                        ImGui.Text("Update gear/hair changes");
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f),
                            "Pulls currently-affecting gear/hair mods into this character's mod state.");
                    }
                    else
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.7f, 0.7f, 1.0f));
                        if (!hasValidName)
                            ImGui.Text("Enter a Character Name first");
                        else if (!plugin.Configuration.EnableConflictResolution)
                            ImGui.Text("Conflict Resolution must be enabled");
                        ImGui.PopStyleColor();
                    }
                    ImGui.EndTooltip();
                }
            }, "Select which mods to enable and configure their options for this character.\nAllows different characters to use different mod combinations and settings.", scale);
        }

        /// <summary>
        /// Pulls currently-affecting gear/hair mods into the character's SecretModState, mirroring
        /// the per-design "quick update" in DesignPanel. Preserves any existing non-gear/hair
        /// selections so the user's broader config isn't clobbered.
        /// </summary>
        private void PerformQuickCharacterGearHairUpdate()
        {
            try
            {
                Plugin.Log.Information("[CharacterQuickUpdate] Starting character-level quick gear/hair update...");

                var allAffectingMods = plugin.PenumbraIntegration.GetCurrentlyAffectingMods();
                Plugin.Log.Information($"[CharacterQuickUpdate] Found {allAffectingMods.Count} total affecting mods");

                if (!allAffectingMods.Any())
                {
                    Plugin.Log.Warning("[CharacterQuickUpdate] No affecting mods detected");
                    return;
                }

                // Filter to gear/hair mods only, using the same categorization logic as DesignPanel
                var gearHairMods = new HashSet<string>();
                var modList = plugin.PenumbraIntegration.GetModList();

                foreach (var modDir in allAffectingMods)
                {
                    if (plugin.modCategorizationCache?.TryGetValue(modDir, out var modType) == true)
                    {
                        if (modType == CharacterSelectPlugin.Windows.ModType.Gear ||
                            modType == CharacterSelectPlugin.Windows.ModType.Hair)
                        {
                            gearHairMods.Add(modDir);
                        }
                    }
                    else if (modList.TryGetValue(modDir, out var modName))
                    {
                        // Fall back to analysing changed items if not cached
                        var changedItems = plugin.PenumbraIntegration.GetModChangedItems(modDir, modName);
                        if (IsGearOrHairMod(changedItems.Keys))
                        {
                            gearHairMods.Add(modDir);
                        }
                    }
                }

                Plugin.Log.Information($"[CharacterQuickUpdate] Filtered to {gearHairMods.Count} gear/hair mods");

                if (!gearHairMods.Any())
                {
                    Plugin.Log.Information("[CharacterQuickUpdate] No gear/hair mods currently affecting - nothing to update");
                    return;
                }

                // Merge with existing state so non-gear/hair selections are preserved
                var newModState = new Dictionary<string, bool>();
                Dictionary<string, bool>? existingState = IsEditWindowOpen
                    ? plugin.Characters[selectedCharacterIndex].SecretModState
                    : plugin.NewSecretModState;

                if (existingState != null)
                {
                    foreach (var (modDir, enabled) in existingState)
                    {
                        if (!gearHairMods.Contains(modDir))
                            newModState[modDir] = enabled;
                    }
                }

                foreach (var modDir in gearHairMods)
                    newModState[modDir] = true;

                // Commit to the right destination
                if (IsEditWindowOpen)
                {
                    plugin.Characters[selectedCharacterIndex].SecretModState = newModState;
                    plugin.SaveConfiguration();
                    Plugin.Log.Information($"[CharacterQuickUpdate] Updated character '{plugin.Characters[selectedCharacterIndex].Name}' with {gearHairMods.Count} gear/hair mods");
                }
                else
                {
                    plugin.NewSecretModState = newModState;
                    Plugin.Log.Information($"[CharacterQuickUpdate] Updated new-character state with {gearHairMods.Count} gear/hair mods");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[CharacterQuickUpdate] Error during quick update: {ex}");
            }
        }

        /// <summary>Minimal gear/hair classifier for mods not in the categorization cache.</summary>
        private static bool IsGearOrHairMod(IEnumerable<string> changedItems)
        {
            foreach (var item in changedItems)
            {
                if (string.IsNullOrEmpty(item)) continue;
                var lower = item.ToLowerInvariant();

                // Equipment
                if (lower.Contains("equipment/e") || lower.Contains("weapon/w") ||
                    lower.Contains("accessory/a") || lower.Contains("hat") || lower.Contains("body") ||
                    lower.Contains("hand") || lower.Contains("leg") || lower.Contains("foot") ||
                    lower.Contains("earring") || lower.Contains("necklace") || lower.Contains("bracelet") ||
                    lower.Contains("ring"))
                {
                    return true;
                }

                // Hair
                if (lower.Contains("/hair/") || lower.Contains("hair/h") || lower.Contains("hairstyle"))
                    return true;
            }
            return false;
        }

        private void DrawHonorificSection(float labelWidth, float inputWidth, float inputOffset, float scale)
        {
            bool changed = false;

            // Row 1: Title (flex 2) + Prefix/Suffix combo (flex 1)
            DrawFieldRow(scale,
                Col(2f, "Title Text", false,
                    "This sets a forced title when you switch to this character.\nUse the Honorific plug-in's Clear button if you need to remove it.",
                    w =>
                    {
                        if (Boutique.DrawBoutiqueTextInput("##HonorificTitle", ref tempHonorificTitle, 50, w, "e.g. Court Sorcerer"))
                            HonorificFieldChanged();
                    }),
                Col(1f, "Placement", false,
                    "Prefix shows the title above your name; Suffix shows it below.",
                    w =>
                    {
                        var placementOptions = new[] { "Prefix", "Suffix" };
                        string current = tempHonorificPrefix;
                        if (AutocompleteCombo.Draw("##HonorificPlacement", ref current, placementOptions, w, "Prefix", allowCustomInput: false))
                        {
                            if (current == "Prefix" || current == "Suffix")
                            {
                                tempHonorificPrefix = current;
                                tempHonorificSuffix = current;
                                HonorificFieldChanged();
                            }
                        }
                    }));

            // Row 2: 3-col grid \u2014 Colour 1 (text) | Colour 2 (glow + gradient picker) | Animation
            DrawFieldRow(scale,
                Col(1f, "Colour 1", false,
                    "Text colour of the honorific title.",
                    w => DrawHonorificTextColourSwatch(scale)),
                Col(1f, "Colour 2", false,
                    "Glow colour. Click to choose a gradient preset (Honorific-style) and animation style.",
                    w => DrawHonorificGlowSwatch(scale)),
                Col(1f, "Animation", false,
                    "Animation style for gradient glows (Wave / Pulse / Static). Solid when no gradient is selected.",
                    w => DrawHonorificAnimationCombo(w, scale)));

            // Preview chip below (only when the title has content)
            if (!string.IsNullOrWhiteSpace(tempHonorificTitle))
            {
                ImGui.Dummy(new Vector2(0, 4f * scale));
                DrawHonorificPreviewChip(scale);
            }
        }

        // Apply changes after any honorific field is touched (regenerates macro).
        private void HonorificFieldChanged()
        {
            UpdateHonorificData();

            if (isAdvancedModeCharacter)
            {
                UpdateAdvancedMacroHonorific();
                if (!IsEditWindowOpen) plugin.NewCharacterMacros = advancedCharacterMacroText;
            }
            else
            {
                if (IsEditWindowOpen) editedCharacterMacros = GenerateMacro();
                else plugin.NewCharacterMacros = (isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
            }
        }

        // Text colour: solid swatch + hex.
        private void DrawHonorificTextColourSwatch(float scale)
        {
            if (Boutique.DrawBoutiqueColorSwatch("HonorificColor", ref tempHonorificColor, scale))
                HonorificFieldChanged();
            DrawSwatchHexAfter(scale,
                $"#{(int)(tempHonorificColor.X * 255):X2}{(int)(tempHonorificColor.Y * 255):X2}{(int)(tempHonorificColor.Z * 255):X2}");
        }

        // Glow / gradient swatch \u2014 opens the gradient picker popup. Animated when
        // a gradient is selected; solid swatch otherwise.
        private void DrawHonorificGlowSwatch(float scale)
        {
            // Compute display colour: animated for gradient modes, solid for none.
            long animOffset = AnimationTimer.ElapsedMilliseconds;
            Vector3 displayColour;
            if (tempHonorificGradientSet.HasValue)
            {
                if (tempHonorificGradientSet.Value == -1)
                    displayColour = GetTwoColourPreviewColor(tempHonorificGlow, tempHonorificColor3, animOffset);
                else
                    displayColour = GetGradientPreviewColor(tempHonorificGradientSet.Value, animOffset);
            }
            else
            {
                displayColour = tempHonorificGlow;
            }

            // Custom-paint a chamfered swatch \u2014 same shape as DrawBoutiqueColorSwatch
            // but with a per-frame display colour (animated for gradients).
            var dl = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            float side = 28f * scale;

            ImGui.SetCursorScreenPos(pos);
            ImGui.InvisibleButton("##HonorificGlowSwatch", new Vector2(side, side));
            bool hovered = ImGui.IsItemHovered();
            bool clicked = ImGui.IsItemClicked();

            Span<Vector2> pts = stackalloc Vector2[6];
            Boutique.BuildSlipPolygon(pos, pos + new Vector2(side, side), 5f * scale, pts);
            unsafe
            {
                fixed (Vector2* p = pts)
                    dl.AddConvexPolyFilled(p, 6, Boutique.U32(new Vector4(displayColour, 1f)));
            }
            for (int i = 0; i < 6; i++) dl.PathLineTo(pts[i]);
            dl.PathStroke(Boutique.U32(hovered ? Boutique.Gold : Boutique.BorderSoft),
                ImDrawFlags.Closed, 1f * scale);
            // Tiny TL corner highlight
            dl.AddLine(pos + new Vector2(2f, 2f), pos + new Vector2(6f, 2f),
                Boutique.U32(new Vector4(1f, 1f, 1f, 0.20f)), 1f);
            dl.AddLine(pos + new Vector2(2f, 2f), pos + new Vector2(2f, 6f),
                Boutique.U32(new Vector4(1f, 1f, 1f, 0.20f)), 1f);

            // Tooltip
            if (hovered)
            {
                if (tempHonorificGradientSet.HasValue)
                {
                    if (tempHonorificGradientSet.Value == -1)
                        CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip($"Two Colour Gradient ({tempHonorificAnimationStyle ?? "Wave"})");
                    else
                        CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip($"{GradientPresetNames[tempHonorificGradientSet.Value]} ({tempHonorificAnimationStyle ?? "Wave"})");
                }
                else
                {
                    CharacterSelectPlugin.Windows.Styles.Boutique.Tooltip("Solid glow. Click to choose a gradient.");
                }
            }

            if (clicked) ImGui.OpenPopup("##GlowPickerPopup");

            // Reuse the existing popup body \u2014 extracted from the legacy DrawGlowPicker.
            DrawHonorificGlowPickerPopup(scale);

            // Hex display: solid hex when no gradient; mode label when gradient.
            string hexLabel = tempHonorificGradientSet.HasValue
                ? (tempHonorificGradientSet.Value == -1 ? "TWO-COLOUR" : "GRADIENT")
                : $"#{(int)(tempHonorificGlow.X * 255):X2}{(int)(tempHonorificGlow.Y * 255):X2}{(int)(tempHonorificGlow.Z * 255):X2}";
            DrawSwatchHexAfter(scale, hexLabel);
        }

        // Animation style combo. Solid (read-only) if no gradient is active.
        private void DrawHonorificAnimationCombo(float width, float scale)
        {
            bool gradientActive = tempHonorificGradientSet.HasValue;
            string current = gradientActive
                ? (tempHonorificAnimationStyle ?? "Wave")
                : "Solid";

            var options = gradientActive
                ? new[] { "Wave", "Pulse", "Static" }
                : new[] { "Solid" };

            if (!gradientActive) ImGui.BeginDisabled();
            if (AutocompleteCombo.Draw("##HonorificAnimation", ref current, options, width,
                placeholder: "Wave", allowCustomInput: false))
            {
                if (gradientActive)
                {
                    tempHonorificAnimationStyle = current;
                    HonorificFieldChanged();
                }
            }
            if (!gradientActive) ImGui.EndDisabled();
        }

        // Honorific preview: same crisp rendering as the legacy DrawHonorificPreview
        // (text + padding box, SeString rendered via UiBuilder.DefaultFont at the box
        // origin), wrapped in a small boutique-flavoured chamber: 2px gold-deep left
        // accent bar and a tracked-caps "PREVIEW" kicker rendered to the LEFT of the
        // box. Box position + size are computed from the actual SeString text size
        // and rounded to integer pixels so the glyph pass is pixel-perfect.
        private void DrawHonorificPreviewChip(float scale)
        {
            if (string.IsNullOrWhiteSpace(tempHonorificTitle)) return;

            var dl = ImGui.GetWindowDrawList();

            // Pre-measure the title at the SAME font the SeString renderer will use,
            // so the box size matches the rendered glyphs exactly. Without this, the
            // box was sized in OutfitBody but the text was drawn in DefaultFont,
            // causing visible blur from sub-pixel offsets.
            var defFont = UiBuilder.DefaultFont;
            ImGui.PushFont(defFont);
            var textSize = ImGui.CalcTextSize(tempHonorificTitle);
            ImGui.PopFont();

            // Layout: "PREVIEW" kicker | 8px gap | preview box
            ImFontPtr labelFont;
            using (Plugin.Instance?.OswaldSemi9?.Push()) { labelFont = ImGui.GetFont(); }
            ImGui.PushFont(labelFont);
            var labelSize = ImGui.CalcTextSize("PREVIEW");
            ImGui.PopFont();

            var padding = new Vector2(8f * scale, 4f * scale);
            var boxSize = textSize + padding * 2f;

            // Round all positions to integers so the SeString renderer paints
            // glyphs at pixel boundaries (no bilinear blur).
            var rowStart = ImGui.GetCursorScreenPos();
            float rowH = MathF.Max(boxSize.Y, labelSize.Y);
            float labelY = MathF.Round(rowStart.Y + (rowH - labelSize.Y) * 0.5f);
            float boxY   = MathF.Round(rowStart.Y + (rowH - boxSize.Y) * 0.5f);

            // 2px gold-deep accent bar to the left of the kicker
            dl.AddRectFilled(
                new Vector2(rowStart.X, rowStart.Y),
                new Vector2(rowStart.X + 2f * scale, rowStart.Y + rowH),
                Boutique.U32(Boutique.GoldDeep));

            // PREVIEW kicker (tracked-caps Oswald)
            float kickerX = MathF.Round(rowStart.X + 8f * scale);
            dl.AddText(labelFont, labelFont.FontSize,
                new Vector2(kickerX, labelY),
                Boutique.U32(Boutique.TextFaint), "PREVIEW");

            // Preview box (legacy dark/grey rect, keeps the crisp rendering)
            var boxStart = new Vector2(MathF.Round(kickerX + labelSize.X + 10f * scale), boxY);
            var boxEnd = boxStart + boxSize;
            dl.AddRectFilled(boxStart, boxEnd,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 1f)));
            dl.AddRect(boxStart, boxEnd,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.3f, 1f)));

            var textPos = boxStart + padding;

            SeString seString;
            if (tempHonorificGradientSet.HasValue)
            {
                seString = BuildGradientSeString(tempHonorificTitle, tempHonorificGradientSet.Value,
                    tempHonorificAnimationStyle ?? "Wave", tempHonorificColor,
                    tempHonorificGradientSet.Value == -1 ? tempHonorificGlow : null,
                    tempHonorificGradientSet.Value == -1 ? tempHonorificColor3 : null);
            }
            else
            {
                seString = BuildColoredSeString(tempHonorificTitle, tempHonorificColor, tempHonorificGlow);
            }

            ImGuiHelpers.SeStringWrapped(seString.Encode(), new SeStringDrawParams
            {
                Color = 0xFFFFFFFF,
                WrapWidth = float.MaxValue,
                TargetDrawList = dl,
                Font = defFont,
                FontSize = UiBuilder.DefaultFontSizePx,
                ScreenOffset = new Vector2(MathF.Round(textPos.X), MathF.Round(textPos.Y))
            });

            // Reserve vertical space for the row
            ImGui.Dummy(new Vector2(0f, rowH));
        }

        // Extracted gradient + animation popup body (called from DrawHonorificGlowSwatch).
        private void DrawHonorificGlowPickerPopup(float scale)
        {
            if (!ImGui.BeginPopup("##GlowPickerPopup")) return;
            float popupWidth = 220 * scale;

            // Default Glow option with colour picker
            ImGui.Text("Solid Glow:");
            ImGui.SameLine();
            if (ImGui.ColorEdit3("##GlowColorPicker", ref tempHonorificGlow,
                ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
            {
                tempHonorificGradientSet = null;
                tempHonorificAnimationStyle = null;
                HonorificFieldChanged();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Use##UseGlow"))
            {
                tempHonorificGradientSet = null;
                tempHonorificAnimationStyle = null;
                HonorificFieldChanged();
                ImGui.CloseCurrentPopup();
            }

            ImGui.Separator();

            if (plugin.Configuration.HasAcknowledgedHonorificSupport)
            {
                string gradientLabel = tempHonorificGradientSet.HasValue
                    ? (tempHonorificGradientSet.Value == -1 ? "Two Colour Gradient" : GradientPresetNames[tempHonorificGradientSet.Value])
                    : "Select Gradient...";

                ImGui.SetNextItemWidth(popupWidth);
                if (ImGui.BeginCombo("##GradientSelect", gradientLabel, ImGuiComboFlags.HeightLargest))
                {
                    if (ImGui.BeginTabBar("##GradAnimTabs"))
                    {
                        foreach (var animStyle in new[] { "Wave", "Pulse", "Static" })
                        {
                            if (ImGui.BeginTabItem(animStyle))
                            {
                                float childHeight = Math.Min(180 * scale,
                                    (GradientPresetNames.Length + 1) * ImGui.GetTextLineHeightWithSpacing());
                                if (ImGui.BeginChild($"##Presets{animStyle}",
                                    new Vector2(popupWidth - 16 * scale, childHeight)))
                                {
                                    var drawList = ImGui.GetWindowDrawList();

                                    bool isTwoColourSelected = tempHonorificGradientSet == -1 && tempHonorificAnimationStyle == animStyle;
                                    if (ImGui.Selectable("Two Colour Gradient", isTwoColourSelected, ImGuiSelectableFlags.DontClosePopups))
                                    {
                                        tempHonorificGradientSet = -1;
                                        tempHonorificAnimationStyle = animStyle;
                                        HonorificFieldChanged();
                                        ImGui.CloseCurrentPopup();
                                    }

                                    for (int i = 0; i < GradientPresetNames.Length; i++)
                                    {
                                        bool isSelected = tempHonorificGradientSet == i && tempHonorificAnimationStyle == animStyle;

                                        var selectableSize = new Vector2(ImGui.GetContentRegionAvail().X,
                                            ImGui.GetTextLineHeightWithSpacing());
                                        var cursorPos = ImGui.GetCursorScreenPos();

                                        if (ImGui.Selectable($"##preset_{animStyle}_{i}", isSelected,
                                            ImGuiSelectableFlags.DontClosePopups, selectableSize))
                                        {
                                            tempHonorificGradientSet = i;
                                            tempHonorificAnimationStyle = animStyle;
                                            HonorificFieldChanged();
                                            ImGui.CloseCurrentPopup();
                                        }

                                        DrawGradientTextForPicker(drawList,
                                            cursorPos + new Vector2(4f * scale, 2f * scale),
                                            GradientPresetNames[i], i, animStyle);
                                    }
                                }
                                ImGui.EndChild();
                                ImGui.EndTabItem();
                            }
                        }
                        ImGui.EndTabBar();
                    }
                    ImGui.EndCombo();
                }

                // Live preview of the chosen gradient
                if (tempHonorificGradientSet.HasValue && tempHonorificGradientSet.Value != -1)
                {
                    ImGui.Text("Preview:");
                    var drawList = ImGui.GetWindowDrawList();
                    var previewPos = ImGui.GetCursorScreenPos() + new Vector2(60 * scale, 0);
                    string previewText = string.IsNullOrWhiteSpace(tempHonorificTitle)
                        ? "Sample Title" : tempHonorificTitle;
                    ImGui.Dummy(new Vector2(popupWidth, ImGui.GetTextLineHeightWithSpacing()));
                    DrawGradientTextForPicker(drawList, previewPos, previewText,
                        tempHonorificGradientSet.Value, tempHonorificAnimationStyle ?? "Wave");
                }

                // Two-colour gradient pickers (only when -1 is selected)
                if (tempHonorificGradientSet == -1)
                {
                    if (ImGui.ColorEdit3("##TwoColour1", ref tempHonorificGlow, ImGuiColorEditFlags.NoInputs))
                        HonorificFieldChanged();
                    ImGui.SameLine();
                    if (ImGui.ColorEdit3("Colours##TwoColour2", ref tempHonorificColor3, ImGuiColorEditFlags.NoInputs))
                        HonorificFieldChanged();
                }
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.65f, 1.0f));
                ImGui.TextWrapped("Enable in Settings > Visual Settings to use animated gradients.");
                ImGui.PopStyleColor();
            }

            ImGui.EndPopup();
        }

        /// <summary>
        /// Gets a preview colour for the two-colour gradient (alternates between the two)
        /// </summary>
        private Vector3 GetTwoColourPreviewColor(Vector3 color1, Vector3 color2, long animOffset)
        {
            // Simple wave between the two colours
            float t = (float)(Math.Sin(animOffset / 500.0) * 0.5 + 0.5);
            return Vector3.Lerp(color1, color2, t);
        }

        /// <summary>
        /// Draws text with animated gradient for the picker preview
        /// </summary>
        private void DrawGradientTextForPicker(ImDrawListPtr drawList, Vector2 pos, string text, int gradientSet, string animStyle)
        {
            long animOffset = AnimationTimer.ElapsedMilliseconds;

            float charX = pos.X;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                string charStr = c.ToString();

                // For two-colour gradient, pass the current colours
                Vector3 charColor = GetGradientColor(gradientSet, i, animOffset, 5, animStyle, text.Length,
                    gradientSet == -1 ? tempHonorificGlow : null,
                    gradientSet == -1 ? tempHonorificColor3 : null);
                uint colorU32 = ImGui.ColorConvertFloat4ToU32(new Vector4(charColor, 1f));

                drawList.AddText(new Vector2(charX, pos.Y), colorU32, charStr);
                charX += ImGui.CalcTextSize(charStr).X;
            }
        }

        private void DrawImageSelection(float scale)
        {
            // Apply any pending pasted/picked image first
            ApplyPendingImagePath();

            float fs = Boutique.FormScale;
            float previewSide = 96f * fs;        // 96, earlier shrink wasn't needed
            float gap        = 12f * fs;
            float pathInputW = 240f * fs;        // capped path input
            float btnH       = 22f * fs;         // smaller action buttons
            float btnW       = 70f * fs;
            float btnGap     = 6f * fs;

            // Indent to match the rest of the form's left edge.
            ImGui.SetCursorPosX(_formIndent);
            var rowStart = ImGui.GetCursorScreenPos();

            // Left: chamfered preview
            DrawPortraitPreviewBox(rowStart, previewSide, scale);
            ImGui.Dummy(new Vector2(previewSide, previewSide));

            // Right column: path input + action buttons
            ImGui.SameLine(0f, gap);
            ImGui.BeginGroup();

            // Path display (capped width)
            string? imagePath = IsEditWindowOpen ? editedCharacterImagePath : plugin.NewCharacterImagePath;
            string display = imagePath ?? "";
            if (Boutique.DrawBoutiqueTextInput("##PortraitPath", ref display, 512, pathInputW, "No image selected"))
            {
                if (IsEditWindowOpen) editedCharacterImagePath = display;
                else plugin.NewCharacterImagePath = display;
            }

            ImGui.Dummy(new Vector2(0f, 4f * fs));

            // Action buttons row, compact fixed-size buttons in natural flow
            if (DrawPortraitActionButton("BROWSE", "", btnW, btnH, scale, "browse"))
            {
                plugin.OpenFilePicker(
                    "Select Character Image",
                    "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|PNG files (*.png)|*.png",
                    (selectedPath) =>
                    {
                        lock (this) { pendingImagePath = selectedPath; }
                    });
            }
            ImGui.SameLine(0f, btnGap);
            bool clipboardHasImage = false;
            try { clipboardHasImage = Clipboard.ContainsImage(); } catch { }
            if (!clipboardHasImage) ImGui.BeginDisabled();
            if (DrawPortraitActionButton("PASTE", "", btnW, btnH, scale, "paste"))
            {
                PasteCharacterImageFromClipboard();
            }
            if (!clipboardHasImage) ImGui.EndDisabled();
            ImGui.SameLine(0f, btnGap);
            if (DrawPortraitActionButton("CLEAR", "", btnW, btnH, scale, "clear"))
            {
                if (IsEditWindowOpen) editedCharacterImagePath = null;
                else plugin.NewCharacterImagePath = null;
            }

            // Compact framing sliders inside the right group, beneath the buttons
            ImGui.Dummy(new Vector2(0f, 6f * fs));
            DrawFramingSliders(fs, pathInputW,
                () => editedPortraitOffsetX, v => editedPortraitOffsetX = v,
                () => editedPortraitOffsetY, v => editedPortraitOffsetY = v,
                () => editedPortraitZoom,    v => editedPortraitZoom    = v,
                "portrait");

            ImGui.EndGroup();

            ImGui.Dummy(new Vector2(0f, 5f * fs));
        }

        // Compact stacked Offset X / Offset Y / Zoom rows. Designed to live
        // inside the right-side path/buttons group, no section header. rowWidth
        // is the available width inside the group (typically pathInputW).
        private void DrawFramingSliders(float fs, float rowWidth,
            Func<float> getOffX, Action<float> setOffX,
            Func<float> getOffY, Action<float> setOffY,
            Func<float> getZoom, Action<float> setZoom,
            string idSuffix,
            bool showResetButtons = false)
        {
            // Tighten label column to the widest label + small gap so the
            // sliders sit right next to their labels.
            float labelW = MathF.Max(MathF.Max(
                ImGui.CalcTextSize("Offset X").X,
                ImGui.CalcTextSize("Offset Y").X),
                ImGui.CalcTextSize("Zoom").X) + 8f * fs;
            float resetW = showResetButtons ? ImGui.GetFrameHeight() + 4f * fs : 0f;
            float sliderW = MathF.Max(40f, rowWidth - labelW - resetW);
            float startX = ImGui.GetCursorPosX();

            void DrawResetButton(string id, Action onReset)
            {
                ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                bool clicked = ImGui.Button($"{FontAwesomeIcon.Undo.ToIconString()}##{id}",
                    new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight()));
                ImGui.PopFont();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Reset");
                if (clicked) onReset();
            }

            // Offset X
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Offset X");
            ImGui.SameLine();
            ImGui.SetCursorPosX(startX + labelW);
            ImGui.SetNextItemWidth(sliderW);
            {
                float v = getOffX();
                if (ImGui.SliderFloat($"##{idSuffix}OffX", ref v, -1f, 1f, "%.2f")) setOffX(v);
            }
            if (showResetButtons) DrawResetButton($"{idSuffix}OffX_reset", () => setOffX(0f));

            // Offset Y
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Offset Y");
            ImGui.SameLine();
            ImGui.SetCursorPosX(startX + labelW);
            ImGui.SetNextItemWidth(sliderW);
            {
                float v = getOffY();
                if (ImGui.SliderFloat($"##{idSuffix}OffY", ref v, -1f, 1f, "%.2f")) setOffY(v);
            }
            if (showResetButtons) DrawResetButton($"{idSuffix}OffY_reset", () => setOffY(0f));

            // Zoom
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Zoom");
            ImGui.SameLine();
            ImGui.SetCursorPosX(startX + labelW);
            ImGui.SetNextItemWidth(sliderW);
            {
                float v = getZoom();
                if (ImGui.SliderFloat($"##{idSuffix}Zoom", ref v, 0.5f, 3.0f, "%.2f×")) setZoom(v);
            }
            if (showResetButtons) DrawResetButton($"{idSuffix}Zoom_reset", () => setZoom(1f));
        }

        // Apply any pending pasted/picked image path (called from DrawImageSelection)
        private void ApplyPendingImagePath()
        {
            if (pendingImagePath != null)
            {
                lock (this)
                {
                    if (IsEditWindowOpen) editedCharacterImagePath = pendingImagePath;
                    else plugin.NewCharacterImagePath = pendingImagePath;
                    pendingImagePath = null;
                }
            }
        }

        // Apply any pending picked path (called from DrawHoverModeSelection)
        private void ApplyPendingAnimatedImagePath()
        {
            if (pendingAnimatedImagePath != null)
            {
                lock (this)
                {
                    if (IsEditWindowOpen) editedAnimatedImagePath = pendingAnimatedImagePath;
                    else plugin.NewCharacterAnimatedImagePath = pendingAnimatedImagePath;
                    pendingAnimatedImagePath = null;
                }
            }
        }
        private void ApplyPendingCutoutImagePath()
        {
            if (pendingCutoutImagePath != null)
            {
                lock (this)
                {
                    if (IsEditWindowOpen) editedCutoutImagePath = pendingCutoutImagePath;
                    else plugin.NewCharacterCutoutImagePath = pendingCutoutImagePath;
                    pendingCutoutImagePath = null;
                }
            }
        }
        private void ApplyPendingCutoutBackdropPath()
        {
            if (pendingCutoutBackdropPath != null)
            {
                lock (this)
                {
                    if (IsEditWindowOpen) editedCutoutBackdropPath = pendingCutoutBackdropPath;
                    else plugin.NewCharacterCutoutBackdropPath = pendingCutoutBackdropPath;
                    pendingCutoutBackdropPath = null;
                }
            }
        }

        // Hover mode picker, radio (None / Animated / Pop-out) followed by the
        // file pickers for the active mode.  Mutually exclusive: switching the
        // radio doesn't clear paths mid-edit (so the user can toggle and not
        // lose what they typed); the inactive mode's path is cleared on save.
        private void DrawHoverModeSelection(float scale)
        {
            ApplyPendingAnimatedImagePath();
            ApplyPendingCutoutImagePath();
            ApplyPendingCutoutBackdropPath();

            float fs = Boutique.FormScale;
            float pathInputW = 240f * fs;
            float btnH       = 22f * fs;
            float btnW       = 70f * fs;
            float btnGap     = 6f * fs;

            // Header label
            ImGui.SetCursorPosX(_formIndent);
            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                Boutique.DrawFieldLabel("ON HOVER", false, null);
            }
            ImGui.Dummy(new Vector2(0f, 3f * fs));

            // Radio row
            ImGui.SetCursorPosX(_formIndent);
            if (ImGui.RadioButton("None##hovermode", _hoverModeRadio == 0)) _hoverModeRadio = 0;
            ImGui.SameLine(0f, 14f * fs);
            if (ImGui.RadioButton("GIF##hovermode", _hoverModeRadio == 1)) _hoverModeRadio = 1;
            ImGui.SameLine(0f, 14f * fs);
            if (ImGui.RadioButton("Pop-out##hovermode", _hoverModeRadio == 2)) _hoverModeRadio = 2;
            ImGui.Dummy(new Vector2(0f, 5f * fs));

            // Conditional file pickers
            if (_hoverModeRadio == 1)
            {
                // Mirror portrait layout: preview on left, path/buttons/sliders in right group
                float previewSide = 96f * fs;
                float gap = 12f * fs;

                ImGui.SetCursorPosX(_formIndent);
                var rowStart = ImGui.GetCursorScreenPos();

                // Left: GIF preview
                DrawAnimatedPreviewBox(rowStart, previewSide, scale);
                ImGui.Dummy(new Vector2(previewSide, previewSide));

                // Right: path input + BROWSE/CLEAR + framing sliders
                ImGui.SameLine(0f, gap);
                ImGui.BeginGroup();

                string animDisplay = GetAnimatedPath() ?? "";
                if (Boutique.DrawBoutiqueTextInput("##animPath", ref animDisplay, 512, pathInputW, "No animated image"))
                {
                    SetAnimatedPath(animDisplay);
                }

                ImGui.Dummy(new Vector2(0f, 4f * fs));

                if (DrawPortraitActionButton("BROWSE", "", btnW, btnH, scale, "anim_browse"))
                {
                    plugin.OpenFilePicker("Select Animated Image",
                        "Animated images (*.gif;*.webp)|*.gif;*.webp|GIF files (*.gif)|*.gif|WebP files (*.webp)|*.webp",
                        (p) => { lock (this) { pendingAnimatedImagePath = p; } });
                }
                ImGui.SameLine(0f, btnGap);
                if (DrawPortraitActionButton("CLEAR", "", btnW, btnH, scale, "anim_clear"))
                {
                    SetAnimatedPath(null);
                }

                ImGui.Dummy(new Vector2(0f, 6f * fs));
                DrawFramingSliders(fs, pathInputW,
                    () => editedAnimatedOffsetX, v => editedAnimatedOffsetX = v,
                    () => editedAnimatedOffsetY, v => editedAnimatedOffsetY = v,
                    () => editedAnimatedZoom,    v => editedAnimatedZoom    = v,
                    "gif");

                ImGui.EndGroup();
            }
            else if (_hoverModeRadio == 2)
            {
                DrawHoverPickerRow(scale, fs, "CUTOUT  (.png transparent)",
                    GetCutoutPath(), SetCutoutPath,
                    "Select Cutout Image",
                    "PNG files (*.png)|*.png",
                    (p) => { lock (this) { pendingCutoutImagePath = p; } },
                    "cutout", pathInputW, btnW, btnH, btnGap, "No cutout image");

                ImGui.Dummy(new Vector2(0f, 4f * fs));

                DrawHoverPickerRow(scale, fs, "BACKDROP SWAP  (optional)",
                    GetCutoutBackdropPath(), SetCutoutBackdropPath,
                    "Select Backdrop Swap Image",
                    "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
                    (p) => { lock (this) { pendingCutoutBackdropPath = p; } },
                    "cutout_bg", pathInputW, btnW, btnH, btnGap, "No backdrop swap");

                ImGui.Dummy(new Vector2(0f, 6f * fs));
                DrawCutoutTuningSliders(fs);
            }

            ImGui.Dummy(new Vector2(0f, 5f * fs));
        }

        // Shared compact path-row (label, input, BROWSE, CLEAR) for hover-mode pickers
        private void DrawHoverPickerRow(float scale, float fs, string label,
            string currentValue, Action<string?> setValue,
            string pickerTitle, string pickerFilter, Action<string> pickerCallback,
            string idSuffix,
            float pathInputW, float btnW, float btnH, float btnGap, string placeholder)
        {
            ImGui.SetCursorPosX(_formIndent);
            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                Boutique.DrawFieldLabel(label.ToUpperInvariant(), false, null);
            }
            ImGui.Dummy(new Vector2(0f, 3f * fs));
            ImGui.SetCursorPosX(_formIndent);

            string display = currentValue ?? "";
            if (Boutique.DrawBoutiqueTextInput($"##{idSuffix}Path", ref display, 512, pathInputW, placeholder))
            {
                setValue(display);
            }
            ImGui.SameLine(0f, 8f * fs);
            if (DrawPortraitActionButton("BROWSE", "", btnW, btnH, scale, $"{idSuffix}_browse"))
            {
                plugin.OpenFilePicker(pickerTitle, pickerFilter, pickerCallback);
            }
            ImGui.SameLine(0f, btnGap);
            if (DrawPortraitActionButton("CLEAR", "", btnW, btnH, scale, $"{idSuffix}_clear"))
            {
                setValue(null);
            }
        }

        // Path getters/setters bound to either edit-state or new-state depending on form mode
        private string? GetAnimatedPath() => IsEditWindowOpen ? editedAnimatedImagePath : plugin.NewCharacterAnimatedImagePath;
        private void SetAnimatedPath(string? v)
        {
            if (IsEditWindowOpen) editedAnimatedImagePath = v;
            else plugin.NewCharacterAnimatedImagePath = v;
        }
        private string? GetCutoutPath() => IsEditWindowOpen ? editedCutoutImagePath : plugin.NewCharacterCutoutImagePath;
        private void SetCutoutPath(string? v)
        {
            if (IsEditWindowOpen) editedCutoutImagePath = v;
            else plugin.NewCharacterCutoutImagePath = v;
        }
        private string? GetCutoutBackdropPath() => IsEditWindowOpen ? editedCutoutBackdropPath : plugin.NewCharacterCutoutBackdropPath;
        private void SetCutoutBackdropPath(string? v)
        {
            if (IsEditWindowOpen) editedCutoutBackdropPath = v;
            else plugin.NewCharacterCutoutBackdropPath = v;
        }

        // Cutout tuning sliders (Scale + Pos X + Pos Y).  Per-character so users
        // can dial each cutout's size and position to fit the card.
        // Includes a live mini preview above the sliders so they can see
        // what they're tuning without leaving the form.
        private void DrawCutoutTuningSliders(float fs)
        {
            ImGui.SetCursorPosX(_formIndent);
            using (Plugin.Instance?.OswaldSemi11?.Push())
            {
                Boutique.DrawFieldLabel("CUTOUT TUNING", false, null);
            }
            ImGui.Dummy(new Vector2(0f, 4f * fs));

            float previewW = 160f * fs;
            float previewH = 140f * fs;
            float gap = 12f * fs;

            ImGui.SetCursorPosX(_formIndent);
            var rowStart = ImGui.GetCursorScreenPos();

            // Left: shrunk live preview
            DrawCutoutPreview(rowStart, previewW, previewH, fs);
            ImGui.Dummy(new Vector2(previewW, previewH));

            // Right: stacked Size / Pos X / Pos Y sliders, mirroring portrait/GIF layout
            ImGui.SameLine(0f, gap);
            ImGui.BeginGroup();

            float labelW = MathF.Max(MathF.Max(
                ImGui.CalcTextSize("Size").X,
                ImGui.CalcTextSize("Pos X").X),
                ImGui.CalcTextSize("Pos Y").X) + 8f * fs;
            float sliderRowW = 220f * fs;
            float sliderW = MathF.Max(40f, sliderRowW - labelW);
            float startX = ImGui.GetCursorPosX();

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Size");
            ImGui.SameLine();
            ImGui.SetCursorPosX(startX + labelW);
            ImGui.SetNextItemWidth(sliderW);
            {
                float v = editedCutoutScale;
                if (ImGui.SliderFloat("##cutoutScale", ref v, 1.0f, 6.0f, "%.2f×")) editedCutoutScale = v;
            }

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Pos X");
            ImGui.SameLine();
            ImGui.SetCursorPosX(startX + labelW);
            ImGui.SetNextItemWidth(sliderW);
            {
                float v = editedCutoutAnchorX;
                if (ImGui.SliderFloat("##cutoutAnchorX", ref v, 0.0f, 1.0f, "%.2f")) editedCutoutAnchorX = v;
            }

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Pos Y");
            ImGui.SameLine();
            ImGui.SetCursorPosX(startX + labelW);
            ImGui.SetNextItemWidth(sliderW);
            {
                float v = editedCutoutAnchorY;
                if (ImGui.SliderFloat("##cutoutAnchorY", ref v, 0.0f, 1.0f, "%.2f")) editedCutoutAnchorY = v;
            }

            ImGui.EndGroup();
        }

        // Live mini preview of the cutout, clipped to the preview rect.
        private void DrawCutoutPreview(Vector2 origin, float previewW, float previewH, float fs)
        {
            var cutoutPath = GetCutoutPath();
            var dl = ImGui.GetWindowDrawList();

            float padding = 6f * fs;

            var previewMin = origin;
            var previewMax = origin + new Vector2(previewW, previewH);

            // Background fill + faint border
            uint bgCol     = ImGui.GetColorU32(new Vector4(0.04f, 0.05f, 0.07f, 1.00f));
            uint borderCol = ImGui.GetColorU32(new Vector4(0.20f, 0.22f, 0.28f, 1.00f));
            dl.AddRectFilled(previewMin, previewMax, bgCol);
            dl.AddRect(previewMin, previewMax, borderCol, 0f, ImDrawFlags.None, 1f);

            // Sample card, scaled to ~35% of the preview width so it stays
            // legible inside the smaller canvas.
            float cardW = 56f * fs;
            float imageH = 56f * fs;
            float nameH = 16f * fs;
            float cardH = imageH + nameH;
            var cardMin = new Vector2(
                previewMin.X + (previewW - cardW) * 0.5f,
                previewMax.Y - cardH - padding);
            var cardMax = cardMin + new Vector2(cardW, cardH);
            var portraitMin = cardMin;
            var portraitMax = new Vector2(cardMax.X, cardMin.Y + imageH);

            // Clip cutout draws to the preview rect
            dl.PushClipRect(previewMin, previewMax, true);

            // Cutout rendered with current slider values
            if (!string.IsNullOrEmpty(cutoutPath) && System.IO.File.Exists(cutoutPath))
            {
                var tex = Plugin.TextureProvider.GetFromFile(cutoutPath).GetWrapOrDefault();
                if (tex != null && tex.Width > 0 && tex.Height > 0)
                {
                    var slipSize = cardMax - cardMin;
                    var portraitSize = portraitMax - portraitMin;

                    float dispW = slipSize.X * editedCutoutScale;
                    float imgAR = tex.Width / (float)tex.Height;
                    float dispH = dispW / imgAR;
                    var poseSize = new Vector2(dispW, dispH);

                    // pose-anchor fixed at bottom-center (matches the live render)
                    var anchorWorld = portraitMin + new Vector2(
                        portraitSize.X * editedCutoutAnchorX,
                        portraitSize.Y * editedCutoutAnchorY);
                    var poseMin = anchorWorld - new Vector2(poseSize.X * 0.5f, poseSize.Y * 1.0f);
                    var poseMax = poseMin + poseSize;

                    dl.AddImage(tex.Handle, poseMin, poseMax);
                }
            }
            else
            {
                // Friendly placeholder text
                var msg = "Pick a cutout above to preview";
                var size = ImGui.CalcTextSize(msg);
                var textPos = new Vector2(
                    previewMin.X + (previewW - size.X) * 0.5f,
                    previewMin.Y + previewH * 0.4f - size.Y * 0.5f);
                dl.AddText(textPos, ImGui.GetColorU32(new Vector4(0.5f, 0.52f, 0.6f, 0.85f)), msg);
            }

            dl.PopClipRect();

            // Card outline drawn on top so the user can always see where the
            // card edges are relative to the cutout.  Photo area in faint
            // grey, nameplate area in slightly more visible band, full
            // outline in gold so it reads.
            uint photoCol = ImGui.GetColorU32(new Vector4(0.10f, 0.12f, 0.16f, 0.55f));
            uint nameCol  = ImGui.GetColorU32(new Vector4(0.06f, 0.07f, 0.10f, 0.85f));
            dl.AddRectFilled(portraitMin, portraitMax, photoCol);
            dl.AddRectFilled(new Vector2(cardMin.X, portraitMax.Y), cardMax, nameCol);

            uint goldCol = ImGui.GetColorU32(new Vector4(0.72f, 0.56f, 0.10f, 0.80f));
            dl.AddRect(cardMin, cardMax, goldCol, 0f, ImDrawFlags.None, 1.5f);
            // Hairline between portrait and nameplate
            dl.AddLine(new Vector2(cardMin.X, portraitMax.Y), new Vector2(cardMax.X, portraitMax.Y),
                goldCol, 1f);
        }

        // 96x96 chamfered slip-polygon preview with inset gilt frame.
        private static void DrawFramedZoomedImage(ImDrawListPtr dl, Vector2 boxMin, Vector2 boxMax,
            Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap texture,
            float zoom, float offsetX, float offsetY)
        {
            var size = boxMax - boxMin;
            float aspect = (float)texture.Width / texture.Height;
            float drawW, drawH;
            if (aspect > 1f) { drawW = size.X; drawH = size.X / aspect; }
            else { drawH = size.Y; drawW = size.Y * aspect; }
            drawW *= zoom;
            drawH *= zoom;
            var off = new Vector2(size.X * offsetX, size.Y * offsetY);
            var drawMin = boxMin + new Vector2((size.X - drawW) * 0.5f, (size.Y - drawH) * 0.5f) + off;
            var drawMax = drawMin + new Vector2(drawW, drawH);
            dl.PushClipRect(boxMin, boxMax, true);
            dl.AddImage(texture.Handle, drawMin, drawMax);
            dl.PopClipRect();
        }

        private void DrawPortraitPreviewBox(Vector2 origin, float side, float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            var min = origin;
            var max = origin + new Vector2(side, side);

            string pluginDirectory = plugin.PluginDirectory;
            string defaultImagePath = Path.Combine(pluginDirectory, "Assets", "Default.png");
            string? imagePath = IsEditWindowOpen ? editedCharacterImagePath : plugin.NewCharacterImagePath;
            string finalImagePath = !string.IsNullOrEmpty(imagePath) && File.Exists(imagePath)
                ? imagePath
                : defaultImagePath;

            if (Plugin.UseClassicLayout)
            {
                uiStyles.DrawGlowingBorder(
                    min - new Vector2(2 * scale, 2 * scale),
                    max + new Vector2(2 * scale, 2 * scale),
                    new Vector3(0.5f, 0.5f, 0.5f), 0.3f, false, scale);
                if (File.Exists(finalImagePath))
                {
                    var tex = Plugin.TextureProvider.GetFromFile(finalImagePath).GetWrapOrDefault();
                    if (tex != null)
                        DrawFramedZoomedImage(dl, min, max, tex, editedPortraitZoom, editedPortraitOffsetX, editedPortraitOffsetY);
                }
                return;
            }

            float fs = Boutique.FormScale;
            float chamfer = 6f * fs;

            // Background slip polygon (dark velvet with diagonal stripe)
            Span<Vector2> pts = stackalloc Vector2[6];
            Boutique.BuildSlipPolygon(min, max, chamfer, pts);

            uint bgCol = Boutique.U32(Boutique.Surface2);
            unsafe
            {
                fixed (Vector2* p = pts)
                    dl.AddConvexPolyFilled(p, 6, bgCol);
            }

            float inset = 4f * fs;
            var imgMin = min + new Vector2(inset, inset);
            var imgMax = max - new Vector2(inset, inset);

            if (File.Exists(finalImagePath))
            {
                var texture = Plugin.TextureProvider.GetFromFile(finalImagePath).GetWrapOrDefault();
                if (texture != null)
                    DrawFramedZoomedImage(dl, imgMin, imgMax, texture, editedPortraitZoom, editedPortraitOffsetX, editedPortraitOffsetY);
            }
            else
            {
                // "No image" placeholder, empty diagonal-hatched area + ghost text
                ImFontPtr ghostFont;
                using (Plugin.Instance?.OswaldSemi9?.Push()) { ghostFont = ImGui.GetFont(); }
                string ghost = "NO IMAGE";
                var gs = ImGui.CalcTextSize(ghost);
                dl.AddText(ghostFont, ghostFont.FontSize,
                    min + new Vector2((side - gs.X) * 0.5f, (side - gs.Y) * 0.5f),
                    Boutique.U32(Boutique.TextGhost), ghost);
            }

            // Gilt, 1px gold-at-20% inset frame
            Span<Vector2> giltPts = stackalloc Vector2[6];
            Boutique.BuildSlipPolygon(min + new Vector2(3f * fs, 3f * fs),
                max - new Vector2(3f * fs, 3f * fs),
                chamfer - 2f * fs, giltPts);
            for (int i = 0; i < 6; i++) dl.PathLineTo(giltPts[i]);
            dl.PathStroke(Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.20f)),
                ImDrawFlags.Closed, 1f * fs);

            // Outer chamfered border
            for (int i = 0; i < 6; i++) dl.PathLineTo(pts[i]);
            dl.PathStroke(Boutique.U32(Boutique.BorderSoft), ImDrawFlags.Closed, 1f * fs);
        }

        // GIF preview, same chassis as the portrait preview but driven by
        // the GIF path + GIF framing values, so the user can see how their
        // animated image is framed before saving.
        private void DrawAnimatedPreviewBox(Vector2 origin, float side, float scale)
        {
            var dl = ImGui.GetWindowDrawList();
            var min = origin;
            var max = origin + new Vector2(side, side);
            string? gifPath = GetAnimatedPath();

            if (Plugin.UseClassicLayout)
            {
                uiStyles.DrawGlowingBorder(
                    min - new Vector2(2 * scale, 2 * scale),
                    max + new Vector2(2 * scale, 2 * scale),
                    new Vector3(0.5f, 0.5f, 0.5f), 0.3f, false, scale);
                if (!string.IsNullOrEmpty(gifPath) && File.Exists(gifPath))
                {
                    var tex = Plugin.TextureProvider.GetFromFile(gifPath).GetWrapOrDefault();
                    if (tex != null && tex.Width > 0 && tex.Height > 0)
                        DrawFramedZoomedImage(dl, min, max, tex, editedAnimatedZoom, editedAnimatedOffsetX, editedAnimatedOffsetY);
                }
                return;
            }

            float fs = Boutique.FormScale;
            float chamfer = 6f * fs;

            // Background slip polygon
            Span<Vector2> pts = stackalloc Vector2[6];
            Boutique.BuildSlipPolygon(min, max, chamfer, pts);
            uint bgCol = Boutique.U32(Boutique.Surface2);
            unsafe { fixed (Vector2* p = pts) dl.AddConvexPolyFilled(p, 6, bgCol); }

            float inset = 4f * fs;
            var imgMin = min + new Vector2(inset, inset);
            var imgMax = max - new Vector2(inset, inset);

            if (!string.IsNullOrEmpty(gifPath) && File.Exists(gifPath))
            {
                // For preview we use the static-texture loader rather than the
                // animated wrap, first frame is enough for framing decisions
                // and avoids spinning up the cache for a tuning preview.
                var texture = Plugin.TextureProvider.GetFromFile(gifPath).GetWrapOrDefault();
                if (texture != null && texture.Width > 0 && texture.Height > 0)
                    DrawFramedZoomedImage(dl, imgMin, imgMax, texture, editedAnimatedZoom, editedAnimatedOffsetX, editedAnimatedOffsetY);
            }
            else
            {
                ImFontPtr ghostFont;
                using (Plugin.Instance?.OswaldSemi9?.Push()) { ghostFont = ImGui.GetFont(); }
                string ghost = "NO GIF";
                var gs = ImGui.CalcTextSize(ghost);
                dl.AddText(ghostFont, ghostFont.FontSize,
                    min + new Vector2((side - gs.X) * 0.5f, (side - gs.Y) * 0.5f),
                    Boutique.U32(Boutique.TextGhost), ghost);
            }

            // Gilt, 1px gold-at-20% inset frame
            Span<Vector2> giltPts = stackalloc Vector2[6];
            Boutique.BuildSlipPolygon(min + new Vector2(3f * fs, 3f * fs),
                max - new Vector2(3f * fs, 3f * fs),
                chamfer - 2f * fs, giltPts);
            for (int i = 0; i < 6; i++) dl.PathLineTo(giltPts[i]);
            dl.PathStroke(Boutique.U32(Boutique.WithAlpha(Boutique.Gold, 0.20f)),
                ImDrawFlags.Closed, 1f * fs);

            // Outer chamfered border
            for (int i = 0; i < 6; i++) dl.PathLineTo(pts[i]);
            dl.PathStroke(Boutique.U32(Boutique.BorderSoft), ImDrawFlags.Closed, 1f * fs);
        }

        // Chamfered action button (BROWSE / PASTE / CLEAR). Returns true when clicked.
        private bool DrawPortraitActionButton(string label, string icon, float w, float h, float scale, string id)
        {
            float fs = Boutique.FormScale;
            var dl = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var max = pos + new Vector2(w, h);
            float chamfer = 5f * fs;

            ImGui.SetCursorScreenPos(pos);
            bool clicked = ImGui.InvisibleButton($"##bportrait_{id}", new Vector2(w, h));
            bool hovered = ImGui.IsItemHovered();

            Span<Vector2> pts = stackalloc Vector2[6];
            Boutique.BuildSlipPolygon(pos, max, chamfer, pts);

            Vector4 bg = hovered
                ? new Vector4(28f / 255f, 32f / 255f, 42f / 255f, 0.92f)
                : new Vector4(20f / 255f, 24f / 255f, 32f / 255f, 0.78f);
            unsafe
            {
                fixed (Vector2* p = pts)
                    dl.AddConvexPolyFilled(p, 6, Boutique.U32(bg));
            }
            for (int i = 0; i < 6; i++) dl.PathLineTo(pts[i]);
            dl.PathStroke(Boutique.U32(hovered ? Boutique.GoldDeep : Boutique.BorderSoft),
                ImDrawFlags.Closed, 1f * fs);

            // Icon + label centred. OswaldSemi13 (16.9px), bumped further from
            // Semi11 since the previous size still read as small inside the 22*fs
            // button frame.
            ImFontPtr labelFont;
            using (Plugin.Instance?.OswaldSemi13?.Push()) { labelFont = ImGui.GetFont(); }
            float iconFontSize = 11f * fs;

            ImGui.PushFont(UiBuilder.IconFont);
            var iconSz = ImGui.CalcTextSize(icon);
            ImGui.PopFont();
            float iconScale = iconFontSize / UiBuilder.IconFont.FontSize;
            float iconW = iconSz.X * iconScale;

            ImGui.PushFont(labelFont);
            var labelSz = ImGui.CalcTextSize(label);
            ImGui.PopFont();

            float gap = 6f * fs;
            float totalW = iconW + gap + labelSz.X;
            float startX = pos.X + (w - totalW) * 0.5f;

            Vector4 inkCol = hovered ? Boutique.GoldWarm : Boutique.TextDim;
            dl.AddText(UiBuilder.IconFont, iconFontSize,
                new Vector2(startX, pos.Y + (h - iconFontSize) * 0.5f),
                Boutique.U32(inkCol), icon);
            dl.AddText(labelFont, labelFont.FontSize,
                new Vector2(startX + iconW + gap, pos.Y + (h - labelFont.FontSize) * 0.5f),
                Boutique.U32(inkCol), label);

            return clicked;
        }

        // Mirrors DesignPanel.PasteImageFromClipboard but writes to character images dir.
        private void PasteCharacterImageFromClipboard()
        {
            try
            {
                Thread thread = new Thread(() =>
                {
                    try
                    {
                        if (!Clipboard.ContainsImage())
                        {
                            Plugin.Log.Warning("No image found in clipboard");
                            return;
                        }
                        using (var clipboardImage = Clipboard.GetImage())
                        {
                            if (clipboardImage == null) return;

                            string imagesDir = Path.Combine(plugin.PluginPath, "Images", "CharacterPortraits");
                            Directory.CreateDirectory(imagesDir);

                            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                            string fullPath = Path.Combine(imagesDir,
                                $"character_portrait_{timestamp}.png");
                            clipboardImage.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);

                            lock (this) { pendingImagePath = fullPath; }
                            Plugin.Log.Info($"Pasted character portrait saved to: {fullPath}");
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

        private void DrawAdvancedModeSection(float scale)
        {
            float fs = Boutique.FormScale;

            // Simple boutique checkbox per v2-simple spec (no special gold-deep left
            // bar chip, that vocabulary was overdoing it for a flat toggle).
            ImFontPtr lblF, descF;
            using (Plugin.Instance?.OutfitMed13?.Push()) { lblF  = ImGui.GetFont(); }
            using (Plugin.Instance?.OutfitMed13?.Push()) { descF = ImGui.GetFont(); }
            bool prev = isAdvancedModeCharacter;
            ImGui.SetCursorPosX(_formIndent);
            Boutique.DrawBoutiqueCheckbox(
                "enable_adv", ref isAdvancedModeCharacter,
                "Enable advanced mode",
                "Custom macro runs when character is applied",
                scale, lblF, descF);
            // Toggle change side-effects (preserved from legacy)
            if (prev != isAdvancedModeCharacter)
            {
                if (IsEditWindowOpen && selectedCharacterIndex >= 0 && selectedCharacterIndex < plugin.Characters.Count)
                {
                    plugin.Characters[selectedCharacterIndex].IsAdvancedMode = isAdvancedModeCharacter;
                    plugin.SaveConfiguration();
                }

                if (isAdvancedModeCharacter)
                {
                    if (IsEditWindowOpen)
                    {
                        advancedCharacterMacroText = !string.IsNullOrWhiteSpace(editedCharacterMacros)
                            ? editedCharacterMacros
                            : GenerateMacro();
                    }
                    else
                    {
                        advancedCharacterMacroText = !string.IsNullOrWhiteSpace(plugin.NewCharacterMacros)
                            ? plugin.NewCharacterMacros
                            : ((isSecretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro());
                        plugin.NewCharacterMacros = advancedCharacterMacroText;
                    }
                }
                else
                {
                    if (IsEditWindowOpen) editedCharacterMacros = advancedCharacterMacroText;
                    else plugin.NewCharacterMacros = advancedCharacterMacroText;
                }
            }

            if (!isAdvancedModeCharacter) return;

            // ── Macro toolbar + line numbers + textarea ──
            ImFontPtr smallFont;
            using (Plugin.Instance?.OswaldSemi9?.Push()) { smallFont = ImGui.GetFont(); }
            Boutique.DrawMacroEditor(ref advancedCharacterMacroText,
                "AdvancedCharacterMacro", scale,
                regenerate: () => isSecretMode && !plugin.Configuration.EnableConflictResolution
                    ? GenerateSecretMacro() : GenerateMacro(),
                paste: () => PasteMacroFromClipboardInto(ref advancedCharacterMacroText),
                smallFont: smallFont);

            // Real-time sync
            if (!IsEditWindowOpen) plugin.NewCharacterMacros = advancedCharacterMacroText;
            else editedCharacterMacros = advancedCharacterMacroText;
        }

        // Helper: synchronously read text clipboard on STA thread and assign to target.
        private void PasteMacroFromClipboardInto(ref string target)
        {
            try
            {
                string clip = "";
                var t = new Thread(() => { try { clip = Clipboard.GetText() ?? ""; } catch { } });
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                t.Join();
                if (!string.IsNullOrEmpty(clip)) target = clip;
            }
            catch (Exception ex) { Plugin.Log.Warning($"Paste macro failed: {ex.Message}"); }
        }

        private float GetSafeScale(float baseScale)
        {
            return Math.Clamp(baseScale, 0.3f, 5.0f); // Prevent extreme scaling
        }

        private void DrawActionButtons(float scale)
        {
            string tempName = IsEditWindowOpen ? editedCharacterName : plugin.NewCharacterName;
            string tempPenumbra = IsEditWindowOpen ? editedCharacterPenumbra : plugin.NewPenumbraCollection;
            string tempGlamourer = IsEditWindowOpen ? editedCharacterGlamourer : plugin.NewGlamourerDesign;

            bool canSaveCharacter = !string.IsNullOrWhiteSpace(tempName) &&
                                   !string.IsNullOrWhiteSpace(tempPenumbra) &&
                                   !string.IsNullOrWhiteSpace(tempGlamourer) &&
                                   string.IsNullOrEmpty(nameValidationError);

            uiStyles.PushDarkButtonStyle(scale);

            if (!canSaveCharacter)
                ImGui.BeginDisabled();

            if (ImGui.Button(IsEditWindowOpen ? "Save Changes" : "Save Character", new Vector2(0, 30 * scale)))
            {
                if (IsEditWindowOpen)
                {
                    SaveEditedCharacter();
                }
                else
                {
                    string finalMacro;
                    if (isAdvancedModeCharacter)
                    {
                        finalMacro = advancedCharacterMacroText;
                    }
                    else
                    {
                        finalMacro = plugin.NewCharacterMacros;
                    }

                    var created = plugin.SaveNewCharacter(finalMacro);
                    ApplyEditedFramingToNew(created);
                }

                CloseForm();
            }

            plugin.SaveButtonPos = ImGui.GetItemRectMin();
            plugin.SaveButtonSize = ImGui.GetItemRectSize();

            if (!canSaveCharacter)
                ImGui.EndDisabled();

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(0, 30 * scale)))
            {
                CloseForm();
            }

            uiStyles.PopDarkButtonStyle();
        }

        // Advanced mode update methods
        private void UpdateAdvancedMacroPenumbra(string collection)
        {
            advancedCharacterMacroText = PatchMacroLine(
                advancedCharacterMacroText,
                "/penumbra collection",
                $"/penumbra collection individual | {collection} | self"
            );

            advancedCharacterMacroText = UpdateCollectionInLines(
                advancedCharacterMacroText,
                "/penumbra bulktag disable",
                collection
            );

            advancedCharacterMacroText = UpdateCollectionInLines(
                advancedCharacterMacroText,
                "/penumbra bulktag enable",
                collection
            );
        }

        private void UpdateAdvancedMacroGlamourer(string oldGlamourer, string newGlamourer)
        {
            var lines = advancedCharacterMacroText.Split('\n').ToList();

            // Find and replace the main glamour apply line (not "no clothes")
            bool foundExistingLine = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("/glamour apply", StringComparison.OrdinalIgnoreCase) &&
                    !line.Contains("no clothes", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"/glamour apply {newGlamourer} | self";
                    foundExistingLine = true;
                    break;
                }
            }

            // Update bulktag enable line if it exists (for secret mode....shhh! how can it stay a secret if I keep mentioning it??)
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("/penumbra bulktag enable", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 2)
                    {
                        var collection = parts[0].Replace("/penumbra bulktag enable", "").Trim();
                        lines[i] = $"/penumbra bulktag enable {collection} | {newGlamourer}";
                    }
                    break;
                }
            }

            if (!foundExistingLine && !string.IsNullOrWhiteSpace(newGlamourer))
            {
                var insertPos = GetProperInsertPosition(lines, "/glamour apply");
                lines.Insert(insertPos, $"/glamour apply {newGlamourer} | self");
            }

            advancedCharacterMacroText = string.Join("\n", lines);
        }

        private void UpdateAdvancedMacroAutomation(string automation)
        {
            var line = string.IsNullOrWhiteSpace(automation)
                ? "/glamour automation enable None"
                : $"/glamour automation enable {automation}";

            advancedCharacterMacroText = PatchMacroLine(
                advancedCharacterMacroText,
                "/glamour automation enable",
                line
            );
        }

        private void UpdateAdvancedMacroCustomize(string customize)
        {
            advancedCharacterMacroText = PatchMacroLine(
                advancedCharacterMacroText,
                "/customize profile disable",
                "/customize profile disable <me>"
            );

            if (!string.IsNullOrWhiteSpace(customize))
            {
                advancedCharacterMacroText = PatchMacroLine(
                    advancedCharacterMacroText,
                    "/customize profile enable",
                    $"/customize profile enable <me>, {customize}"
                );
            }
            else
            {
                advancedCharacterMacroText = string.Join("\n",
                    advancedCharacterMacroText
                        .Split('\n')
                        .Where(l => !l.TrimStart().StartsWith("/customize profile enable"))
                );
            }
        }

        private void UpdateAdvancedMacroHonorific()
        {
            var lines = advancedCharacterMacroText.Split('\n').ToList();

            var clearIdx = lines.FindIndex(l =>
                l.TrimStart().StartsWith("/honorific force clear", StringComparison.OrdinalIgnoreCase));

            if (clearIdx < 0)
            {
                var insertPos = GetProperInsertPosition(lines, "/honorific force clear");
                lines.Insert(insertPos, "/honorific force clear | silent");
                clearIdx = insertPos;
            }
            else
            {
                // Update existing clear line to include silent
                if (!lines[clearIdx].Contains("silent", StringComparison.OrdinalIgnoreCase))
                {
                    lines[clearIdx] = "/honorific force clear | silent";
                }
            }

            if (!string.IsNullOrWhiteSpace(tempHonorificTitle))
            {
                var c = tempHonorificColor;
                var g = tempHonorificGlow;
                var c3 = tempHonorificColor3;
                string colorHex = $"#{(int)(c.X * 255):X2}{(int)(c.Y * 255):X2}{(int)(c.Z * 255):X2}";
                string glowHex = $"#{(int)(g.X * 255):X2}{(int)(g.Y * 255):X2}{(int)(g.Z * 255):X2}";
                string color3Hex = $"#{(int)(c3.X * 255):X2}{(int)(c3.Y * 255):X2}{(int)(c3.Z * 255):X2}";

                string gradientPart = "";
                if (tempHonorificGradientSet.HasValue && !string.IsNullOrEmpty(tempHonorificAnimationStyle))
                {
                    if (tempHonorificGradientSet.Value == -1)
                    {
                        // Two-colour gradient: include Color3 in the command
                        gradientPart = $" | {color3Hex} | +-1/{tempHonorificAnimationStyle}";
                    }
                    else
                    {
                        gradientPart = $" | +{tempHonorificGradientSet.Value}/{tempHonorificAnimationStyle}";
                    }
                }

                string setLine = $"/honorific force set {tempHonorificTitle} | {tempHonorificPrefix} | {colorHex} | {glowHex}{gradientPart} | silent";

                var setIdx = lines.FindIndex(l =>
                    l.TrimStart().StartsWith("/honorific force set", StringComparison.OrdinalIgnoreCase));

                if (setIdx >= 0)
                {
                    lines[setIdx] = setLine;
                }
                else
                {
                    lines.Insert(clearIdx + 1, setLine);
                }
            }
            else
            {
                lines.RemoveAll(l => l.TrimStart().StartsWith("/honorific force set", StringComparison.OrdinalIgnoreCase));
            }

            advancedCharacterMacroText = string.Join("\n", lines);
        }


        private void UpdateAdvancedMacroMoodle(string preset)
        {
            var lines = advancedCharacterMacroText.Split('\n').ToList();

            var removeIdx = lines.FindIndex(l =>
                l.TrimStart().StartsWith("/moodle remove self preset all", StringComparison.OrdinalIgnoreCase));

            if (removeIdx < 0)
            {
                var insertPos = GetProperInsertPosition(lines, "/moodle remove");
                lines.Insert(insertPos, "/moodle remove self preset all");
                removeIdx = insertPos;
            }

            if (!string.IsNullOrWhiteSpace(preset))
            {
                string applyLine = $"/moodle apply self preset \"{preset}\"";
                var applyIdx = lines.FindIndex(l =>
                    l.TrimStart().StartsWith("/moodle apply self preset", StringComparison.OrdinalIgnoreCase));

                if (applyIdx >= 0)
                {
                    lines[applyIdx] = applyLine;
                }
                else
                {
                    lines.Insert(removeIdx + 1, applyLine);
                }
            }
            else
            {
                lines.RemoveAll(l => l.TrimStart().StartsWith("/moodle apply self preset", StringComparison.OrdinalIgnoreCase));
            }

            advancedCharacterMacroText = string.Join("\n", lines);
        }

        private void UpdateAdvancedMacroIdlePose(byte poseIndex)
        {
            var lines = advancedCharacterMacroText.Split('\n').ToList();

            if (poseIndex != 7)
            {
                string sidleLine = $"/sidle {poseIndex}";
                var sidleIdx = lines.FindIndex(l =>
                    l.TrimStart().StartsWith("/sidle", StringComparison.OrdinalIgnoreCase));

                if (sidleIdx >= 0)
                {
                    lines[sidleIdx] = sidleLine;
                }
                else
                {
                    var insertPos = GetProperInsertPosition(lines, "/sidle");
                    lines.Insert(insertPos, sidleLine);
                }
            }
            else
            {
                // Remove any existing sidle line when pose is "None"
                lines.RemoveAll(l => l.TrimStart().StartsWith("/sidle", StringComparison.OrdinalIgnoreCase));
            }

            advancedCharacterMacroText = string.Join("\n", lines);
        }

        private void UpdateHonorificData()
        {
            if (IsEditWindowOpen)
            {
                editedCharacterHonorificTitle = tempHonorificTitle;
                editedCharacterHonorificPrefix = tempHonorificPrefix;
                editedCharacterHonorificSuffix = tempHonorificSuffix;
                editedCharacterHonorificColor = tempHonorificColor;
                editedCharacterHonorificGlow = tempHonorificGlow;
                editedCharacterHonorificColor3 = tempHonorificGradientSet == -1 ? tempHonorificColor3 : null;
                editedCharacterHonorificGradientSet = tempHonorificGradientSet;
                editedCharacterHonorificAnimationStyle = tempHonorificAnimationStyle;
            }
            else
            {
                plugin.NewCharacterHonorificTitle = tempHonorificTitle;
                plugin.NewCharacterHonorificPrefix = tempHonorificPrefix;
                plugin.NewCharacterHonorificSuffix = tempHonorificSuffix;
                plugin.NewCharacterHonorificColor = tempHonorificColor;
                plugin.NewCharacterHonorificGlow = tempHonorificGlow;
                plugin.NewCharacterHonorificColor3 = tempHonorificGradientSet == -1 ? tempHonorificColor3 : null;
                plugin.NewCharacterHonorificGradientSet = tempHonorificGradientSet;
                plugin.NewCharacterHonorificAnimationStyle = tempHonorificAnimationStyle;
            }
        }

        /// <summary>
        /// Builds an SeString with solid color and glow effect
        /// </summary>
        private SeString BuildColoredSeString(string text, Vector3 color, Vector3 glow)
        {
            var builder = new SeStringBuilder();

            // Add text color
            builder.PushColorRgba(new Vector4(color, 1f));

            // Add edge/glow color
            builder.PushEdgeColorRgba(new Vector4(glow, 1f));

            builder.Append(text);

            builder.PopEdgeColor();
            builder.PopColor();

            return SeString.Parse(builder.GetViewAsSpan());
        }

        /// <summary>
        /// Builds an SeString with animated gradient glow effect
        /// </summary>
        private SeString BuildGradientSeString(string text, int gradientSet, string animStyle, Vector3 textColor,
            Vector3? twoColourFirst = null, Vector3? twoColourSecond = null)
        {
            var builder = new SeStringBuilder();
            long animOffset = AnimationTimer.ElapsedMilliseconds;

            // Add base text color
            builder.PushColorRgba(new Vector4(textColor, 1f));

            for (int i = 0; i < text.Length; i++)
            {
                // Calculate gradient color for this character
                Vector3 glowColor = GetGradientColor(gradientSet, i, animOffset, 5, animStyle, text.Length, twoColourFirst, twoColourSecond);

                // Push edge color for this character
                builder.PushEdgeColorRgba(new Vector4(glowColor, 1f));
                builder.Append(text[i].ToString());
                builder.PopEdgeColor();
            }

            builder.PopColor();

            return SeString.Parse(builder.GetViewAsSpan());
        }

        /// <summary>
        /// Gets a color from the gradient using Honorific's exact algorithm
        /// </summary>
        private Vector3 GetGradientColor(int gradientSet, int charIndex, long rawMilliseconds, int throttle, string animStyle,
            int textLength = 16, Vector3? twoColourFirst = null, Vector3? twoColourSecond = null)
        {
            // Handle two-colour gradient (gradientSet == -1)
            if (gradientSet == -1 && twoColourFirst.HasValue && twoColourSecond.HasValue)
            {
                return GetTwoColourGradientColor(twoColourFirst.Value, twoColourSecond.Value,
                    charIndex, rawMilliseconds, throttle, animStyle, textLength);
            }

            if (gradientSet < 0 || gradientSet >= DecodedGradients.Length)
                return new Vector3(1f, 1f, 1f);

            var colors = DecodedGradients[gradientSet];
            var colorCount = colors.GetLength(0);

            // Honorific's exact timing: divide by 15 first, then by throttle
            var animationOffset = rawMilliseconds / 15;

            int index;
            if (animStyle == "Pulse")
            {
                // Pulse: whole text uses same color (charIndex multiplier = 0)
                index = (int)((animationOffset / throttle) % colorCount);
            }
            else if (animStyle == "Static")
            {
                // Static: spread gradient across text length, no animation
                index = (int)Math.Round(charIndex / (float)Math.Max(1, textLength) * colorCount) % colorCount;
            }
            else // Wave
            {
                // Wave: position based on character index + time (charIndex multiplier = 1)
                index = (int)((animationOffset / throttle + charIndex) % colorCount);
            }

            return new Vector3(
                colors[index, 0] / 255f,
                colors[index, 1] / 255f,
                colors[index, 2] / 255f
            );
        }

        /// <summary>
        /// Gets a color for two-colour gradient animation (matching Honorific's GradientSystem.GetDualColourStyle)
        /// </summary>
        private Vector3 GetTwoColourGradientColor(Vector3 color1, Vector3 color2, int charIndex,
            long rawMilliseconds, int throttle, string animStyle, int textLength)
        {
            // Honorific generates a gradient: color1 -> fade -> color2 -> fade -> color1
            // We simulate this with 64 steps like Honorific does
            const int GradientSteps = 64;

            var animationOffset = rawMilliseconds / 15;

            int index;
            if (animStyle == "Pulse")
            {
                // Pulse: whole text uses same color
                index = (int)((animationOffset / throttle) % GradientSteps);
            }
            else if (animStyle == "Static")
            {
                // Static: spread gradient across text, no animation
                index = (int)Math.Round(charIndex / (float)Math.Max(1, textLength) * GradientSteps) % GradientSteps;
            }
            else // Wave
            {
                // Wave: position based on character index + time
                index = (int)((animationOffset / throttle + charIndex) % GradientSteps);
            }

            // Calculate interpolation: 0->32 goes color1->color2, 32->64 goes color2->color1
            float t;
            if (index < GradientSteps / 2)
            {
                t = index / (float)(GradientSteps / 2);  // 0 to 1
            }
            else
            {
                t = 1f - ((index - GradientSteps / 2) / (float)(GradientSteps / 2));  // 1 to 0
            }

            return Vector3.Lerp(color1, color2, t);
        }

        /// <summary>
        /// Gets a representative color from a gradient preset (for button preview)
        /// </summary>
        private Vector3 GetGradientPreviewColor(int preset, long rawMilliseconds)
        {
            if (preset < 0 || preset >= DecodedGradients.Length)
                return new Vector3(1f, 1f, 1f);

            var colors = DecodedGradients[preset];
            var colorCount = colors.GetLength(0);
            // Match Honorific timing: /15 then /5 (throttle)
            var index = (int)((rawMilliseconds / 15 / 5) % colorCount);

            return new Vector3(
                colors[index, 0] / 255f,
                colors[index, 1] / 255f,
                colors[index, 2] / 255f
            );
        }
        private string PatchMacroLine(string existing, string prefix, string replacement)
        {
            var lines = existing.Split('\n').ToList();
            var idx = lines.FindIndex(l => l.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

            if (idx >= 0)
            {
                lines[idx] = replacement;
            }
            else
            {
                int insertPosition = GetProperInsertPosition(lines, prefix);
                lines.Insert(insertPosition, replacement);
            }

            return string.Join("\n", lines);
        }

        private int GetProperInsertPosition(List<string> lines, string prefix)
        {
            var order = new[]
            {
                "/penumbra collection",
                "/penumbra bulktag disable",
                "/penumbra bulktag enable",
                "/glamour apply no clothes",
                "/glamour apply",
                "/glamour automation enable",
                "/customize profile disable",
                "/customize profile enable",
                "/honorific force clear",
                "/honorific force set",
                "/moodle remove",
                "/moodle apply",
                "/sidle",
                "/penumbra redraw"
            };

            int targetOrder = Array.FindIndex(order, o => prefix.StartsWith(o, StringComparison.OrdinalIgnoreCase));
            if (targetOrder == -1) return lines.Count;

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

        private string UpdateCollectionInLines(string existing, string prefix, string newCollection)
        {
            var lines = existing.Split('\n').Select(line =>
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var rest = trimmed.Substring(prefix.Length).TrimStart();
                    var afterCollection = rest.IndexOf('|') >= 0
                        ? rest.Substring(rest.IndexOf('|'))
                        : rest.Substring(rest.IndexOf(' '));
                    return $"{prefix} {newCollection} {afterCollection}";
                }
                return line;
            });
            return string.Join("\n", lines);
        }

        private string GenerateMacro()
        {
            string penumbra = IsEditWindowOpen ? editedCharacterPenumbra : plugin.NewPenumbraCollection;
            string glamourer = IsEditWindowOpen ? editedCharacterGlamourer : plugin.NewGlamourerDesign;
            string customize = IsEditWindowOpen ? editedCharacterCustomize : plugin.NewCustomizeProfile;
            string honorificTitle = IsEditWindowOpen ? editedCharacterHonorificTitle : plugin.NewCharacterHonorificTitle;
            string honorificPrefix = IsEditWindowOpen ? editedCharacterHonorificPrefix : plugin.NewCharacterHonorificPrefix;
            Vector3 honorificColor = IsEditWindowOpen ? editedCharacterHonorificColor : plugin.NewCharacterHonorificColor;
            Vector3 honorificGlow = IsEditWindowOpen ? editedCharacterHonorificGlow : plugin.NewCharacterHonorificGlow;
            string automation = IsEditWindowOpen ? editedCharacterAutomation : plugin.NewCharacterAutomation;
            string moodlePreset = IsEditWindowOpen ? editedCharacterMoodlePreset : plugin.NewCharacterMoodlePreset;
            int idlePose = IsEditWindowOpen ? plugin.Characters[selectedCharacterIndex].IdlePoseIndex : plugin.NewCharacterIdlePoseIndex;

            if (string.IsNullOrWhiteSpace(penumbra) || string.IsNullOrWhiteSpace(glamourer))
                return "/penumbra redraw self";

            string macro = $"/penumbra collection individual | {penumbra} | self\n";
            macro += $"/glamour apply {glamourer} | self\n";

            if (plugin.Configuration.EnableAutomations)
            {
                if (string.IsNullOrWhiteSpace(automation))
                    macro += "/glamour automation enable None\n";
                else
                    macro += $"/glamour automation enable {automation}\n";
            }

            macro += "/customize profile disable <me>\n";
            if (!string.IsNullOrWhiteSpace(customize))
                macro += $"/customize profile enable <me>, {customize}\n";

            macro += "/honorific force clear | silent\n";
            if (!string.IsNullOrWhiteSpace(honorificTitle))
            {
                string colorHex = $"#{(int)(honorificColor.X * 255):X2}{(int)(honorificColor.Y * 255):X2}{(int)(honorificColor.Z * 255):X2}";
                string glowHex = $"#{(int)(honorificGlow.X * 255):X2}{(int)(honorificGlow.Y * 255):X2}{(int)(honorificGlow.Z * 255):X2}";
                int? gradientSet = IsEditWindowOpen ? editedCharacterHonorificGradientSet : plugin.NewCharacterHonorificGradientSet;
                string? animStyle = IsEditWindowOpen ? editedCharacterHonorificAnimationStyle : plugin.NewCharacterHonorificAnimationStyle;
                Vector3? color3 = IsEditWindowOpen ? editedCharacterHonorificColor3 : plugin.NewCharacterHonorificColor3;

                string gradientPart = "";
                if (gradientSet.HasValue && !string.IsNullOrEmpty(animStyle))
                {
                    if (gradientSet.Value == -1 && color3.HasValue)
                    {
                        // Two-colour gradient: include Color3 in the command
                        string color3Hex = $"#{(int)(color3.Value.X * 255):X2}{(int)(color3.Value.Y * 255):X2}{(int)(color3.Value.Z * 255):X2}";
                        gradientPart = $" | {color3Hex} | +-1/{animStyle}";
                    }
                    else
                    {
                        gradientPart = $" | +{gradientSet.Value}/{animStyle}";
                    }
                }

                macro += $"/honorific force set {honorificTitle} | {honorificPrefix} | {colorHex} | {glowHex}{gradientPart} | silent\n";
            }

            macro += "/moodle remove self preset all\n";
            if (!string.IsNullOrWhiteSpace(moodlePreset))
                macro += $"/moodle apply self preset \"{moodlePreset}\"\n";

            if (idlePose != 7)
                macro += $"/sidle {idlePose}\n";

            macro += "/penumbra redraw self";

            return macro;
        }

        private string GenerateSecretMacro()
        {
            string penumbra = IsEditWindowOpen ? editedCharacterPenumbra : plugin.NewPenumbraCollection;
            string glamourer = IsEditWindowOpen ? editedCharacterGlamourer : plugin.NewGlamourerDesign;
            string customize = IsEditWindowOpen ? editedCharacterCustomize : plugin.NewCustomizeProfile;
            string honorTitle = IsEditWindowOpen ? editedCharacterHonorificTitle : plugin.NewCharacterHonorificTitle;
            string honorPref = IsEditWindowOpen ? editedCharacterHonorificPrefix : plugin.NewCharacterHonorificPrefix;
            Vector3 honorColor = IsEditWindowOpen ? editedCharacterHonorificColor : plugin.NewCharacterHonorificColor;
            Vector3 honorGlow = IsEditWindowOpen ? editedCharacterHonorificGlow : plugin.NewCharacterHonorificGlow;
            Vector3? honorColor3 = IsEditWindowOpen ? editedCharacterHonorificColor3 : plugin.NewCharacterHonorificColor3;
            int? honorGradientSet = IsEditWindowOpen ? editedCharacterHonorificGradientSet : plugin.NewCharacterHonorificGradientSet;
            string? honorAnimStyle = IsEditWindowOpen ? editedCharacterHonorificAnimationStyle : plugin.NewCharacterHonorificAnimationStyle;
            string moodlePreset = IsEditWindowOpen ? editedCharacterMoodlePreset : plugin.NewCharacterMoodlePreset;
            int idlePose = IsEditWindowOpen
                                    ? plugin.Characters[selectedCharacterIndex].IdlePoseIndex
                                    : plugin.NewCharacterIdlePoseIndex;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"/penumbra collection individual | {penumbra} | self");
            sb.AppendLine($"/penumbra bulktag disable {penumbra} | gear");
            sb.AppendLine($"/penumbra bulktag disable {penumbra} | hair");
            sb.AppendLine($"/penumbra bulktag enable {penumbra} | {glamourer}");
            sb.AppendLine("/glamour apply no clothes | self");
            sb.AppendLine($"/glamour apply {glamourer} | self");

            if (plugin.Configuration.EnableAutomations)
            {
                string automation = IsEditWindowOpen ? editedCharacterAutomation : plugin.NewCharacterAutomation;
                if (string.IsNullOrWhiteSpace(automation))
                    sb.AppendLine("/glamour automation enable None");
                else
                    sb.AppendLine($"/glamour automation enable {automation}");
            }

            sb.AppendLine("/customize profile disable <me>");
            if (!string.IsNullOrWhiteSpace(customize))
                sb.AppendLine($"/customize profile enable <me>, {customize}");

            sb.AppendLine("/honorific force clear | silent");
            if (!string.IsNullOrWhiteSpace(honorTitle))
            {
                var colorHex = $"#{(int)(honorColor.X * 255):X2}{(int)(honorColor.Y * 255):X2}{(int)(honorColor.Z * 255):X2}";
                var glowHex = $"#{(int)(honorGlow.X * 255):X2}{(int)(honorGlow.Y * 255):X2}{(int)(honorGlow.Z * 255):X2}";

                string gradientPart = "";
                if (honorGradientSet.HasValue && !string.IsNullOrEmpty(honorAnimStyle))
                {
                    if (honorGradientSet.Value == -1 && honorColor3.HasValue)
                    {
                        // Two-colour gradient: include Color3 in the command
                        var color3Hex = $"#{(int)(honorColor3.Value.X * 255):X2}{(int)(honorColor3.Value.Y * 255):X2}{(int)(honorColor3.Value.Z * 255):X2}";
                        gradientPart = $" | {color3Hex} | +-1/{honorAnimStyle}";
                    }
                    else
                    {
                        gradientPart = $" | +{honorGradientSet.Value}/{honorAnimStyle}";
                    }
                }

                sb.AppendLine($"/honorific force set {honorTitle} | {honorPref} | {colorHex} | {glowHex}{gradientPart} | silent");
            }

            sb.AppendLine("/moodle remove self preset all");
            if (!string.IsNullOrWhiteSpace(moodlePreset))
                sb.AppendLine($"/moodle apply self preset \"{moodlePreset}\"");

            if (idlePose != 7)
                sb.AppendLine($"/sidle {idlePose}");

            sb.Append("/penumbra redraw self");
            return sb.ToString();
        }

        public void SetSecretMode(bool secretMode)
        {
            isSecretMode = secretMode;
            if (secretMode && !IsEditWindowOpen)
            {
                plugin.NewCharacterMacros = (secretMode && !plugin.Configuration.EnableConflictResolution) ? GenerateSecretMacro() : GenerateMacro();
            }
        }

        private void CloseForm()
        {
            IsEditWindowOpen = false;
            plugin.CloseAddCharacterWindow();

            if (plugin.SecretModeModWindow?.IsOpen ?? false)
            {
                plugin.SecretModeModWindow.IsOpen = false;
            }

            isSecretMode = false;
            isAdvancedModeCharacter = false;
            // Force the form to treat the next open as a fresh appearance so
            // its scroll resets to the top. Draw() never runs while the form
            // is closed (MainWindow gates on the open flags), so the early
            // return at the top of Draw can't reset wasFormVisibleLastFrame
            // for us, we have to do it here.
            wasFormVisibleLastFrame = false;
            ResetFields();
        }

        public void ResetFields()
        {
            plugin.NewCharacterName = "";
            plugin.NewCharacterAlias = "";
            plugin.NewCharacterExcludeFromNameSync = false;
            plugin.NewCharacterUseGlitchNameEffect = false;
            plugin.NewCharacterColor = new Vector3(1.0f, 1.0f, 1.0f);
            plugin.NewPenumbraCollection = "";
            plugin.NewGlamourerDesign = "";
            plugin.NewCharacterAutomation = "";
            plugin.NewCustomizeProfile = "";
            plugin.NewCharacterImagePath = null;
            plugin.NewCharacterAnimatedImagePath = null;
            plugin.NewCharacterCutoutImagePath = null;
            plugin.NewCharacterCutoutBackdropPath = null;
            plugin.NewCharacterDesigns.Clear();
            plugin.NewCharacterHonorificTitle = "";
            plugin.NewCharacterHonorificPrefix = "Prefix";
            plugin.NewCharacterHonorificSuffix = "Suffix";
            plugin.NewCharacterHonorificColor = new Vector3(1.0f, 1.0f, 1.0f);
            plugin.NewCharacterHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f);
            plugin.NewCharacterHonorificColor3 = null;
            plugin.NewCharacterHonorificGradientSet = null;
            plugin.NewCharacterHonorificAnimationStyle = null;
            plugin.NewCharacterMoodlePreset = "";
            plugin.NewCharacterIdlePoseIndex = 7;
            plugin.NewCharacterIsAdvancedMode = false;
            // Reset local temp fields
            tempHonorificTitle = "";
            tempHonorificPrefix = "Prefix";
            tempHonorificSuffix = "Suffix";
            tempHonorificColor = new Vector3(1.0f, 1.0f, 1.0f);
            tempHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f);
            tempHonorificColor3 = new Vector3(0.5f, 0.5f, 1.0f);
            tempHonorificGradientSet = null;
            tempHonorificAnimationStyle = null;
            tempMoodlePreset = "";

            // Reset edit fields
            editedCharacterName = "";
            editedCharacterMacros = "";
            editedCharacterImagePath = null;
            editedAnimatedImagePath = null;
            editedCutoutImagePath = null;
            editedCutoutBackdropPath = null;
            editedCutoutScale = 3.25f;
            editedCutoutAnchorX = 0.65f;
            editedCutoutAnchorY = 1.00f;
            editedPortraitOffsetX = 0f;
            editedPortraitOffsetY = 0f;
            editedPortraitZoom    = 1f;
            editedAnimatedOffsetX = 0f;
            editedAnimatedOffsetY = 0f;
            editedAnimatedZoom    = 1f;
            _hoverModeRadio = 0;
            editedCharacterColor = new Vector3(1.0f, 1.0f, 1.0f);
            editedCharacterPenumbra = "";
            editedCharacterGlamourer = "";
            editedCharacterCustomize = "";
            editedCharacterTag = "";
            editedCharacterAutomation = "";
            editedCharacterMoodlePreset = "";
            editedCharacterGearset = null;
            editedCharacterExcludeFromNameSync = false;
            editedCharacterUseGlitchNameEffect = false;
            editedCharacterAlias = "";
            editedCharacterHonorificTitle = "";
            editedCharacterHonorificPrefix = "Prefix";
            editedCharacterHonorificSuffix = "Suffix";
            editedCharacterHonorificColor = new Vector3(1.0f, 1.0f, 1.0f);
            editedCharacterHonorificGlow = new Vector3(1.0f, 1.0f, 1.0f);
            editedCharacterHonorificColor3 = null;
            editedCharacterHonorificGradientSet = null;
            editedCharacterHonorificAnimationStyle = null;

            advancedCharacterMacroText = "";

            // Only regenerate macro if not in advanced mode
            if (!isAdvancedModeCharacter)
            {
                plugin.NewCharacterMacros = GenerateMacro();
            }
        }

        // Plugin.SaveNewCharacter constructs the Character with default
        // framing values, these private edited* fields aren't reachable
        // from there.  Patch them onto the freshly-created character so the
        // grid renders the user's chosen zoom / offset / cutout tuning.
        private void ApplyEditedFramingToNew(Character? c)
        {
            if (c == null) return;

            c.PortraitOffsetX = editedPortraitOffsetX;
            c.PortraitOffsetY = editedPortraitOffsetY;
            c.PortraitZoom    = editedPortraitZoom;

            if (_hoverModeRadio == 1)
            {
                c.AnimatedOffsetX = editedAnimatedOffsetX;
                c.AnimatedOffsetY = editedAnimatedOffsetY;
                c.AnimatedZoom    = editedAnimatedZoom;
            }
            else if (_hoverModeRadio == 2)
            {
                c.CutoutScale   = editedCutoutScale;
                c.CutoutAnchorX = editedCutoutAnchorX;
                c.CutoutAnchorY = editedCutoutAnchorY;
            }

            plugin.SaveConfiguration();
        }

        private void SaveEditedCharacter()
        {
            if (selectedCharacterIndex < 0 || selectedCharacterIndex >= plugin.Characters.Count)
                return;

            var character = plugin.Characters[selectedCharacterIndex];

            // Capture pre-edit identity so we can detect a rename/alias change below. The server
            // filename is derived from (Alias ?? Name), so that's the value that determines whether
            // a rename leaves an orphan file on the server.
            string oldDisplayName = !string.IsNullOrWhiteSpace(character.Alias) ? character.Alias : character.Name;
            string? oldLastInGameName = character.LastInGameName;

            character.Name = editedCharacterName;
            character.Tags = string.IsNullOrWhiteSpace(editedCharacterTag)
                ? new List<string>()
                : editedCharacterTag.Split(',').Select(f => f.Trim()).ToList();
            character.PenumbraCollection = editedCharacterPenumbra;
            character.GlamourerDesign = editedCharacterGlamourer;
            character.CustomizeProfile = editedCharacterCustomize;
            character.NameplateColor = editedCharacterColor;
            character.CharacterAutomation = editedCharacterAutomation;
            character.HonorificTitle = editedCharacterHonorificTitle;
            character.HonorificPrefix = editedCharacterHonorificPrefix;
            character.HonorificSuffix = editedCharacterHonorificSuffix;
            character.HonorificColor = editedCharacterHonorificColor;
            character.HonorificGlow = editedCharacterHonorificGlow;
            character.HonorificColor3 = editedCharacterHonorificColor3;
            character.HonorificGradientSet = editedCharacterHonorificGradientSet;
            character.HonorificAnimationStyle = editedCharacterHonorificAnimationStyle;
            character.MoodlePreset = editedCharacterMoodlePreset;
            character.AssignedGearset = editedCharacterGearset;
            character.ExcludeFromNameSync = editedCharacterExcludeFromNameSync;
            character.UseGlitchNameEffect = editedCharacterUseGlitchNameEffect;
            // Mirror the glitch toggle into the RP profile so other users see the
            // pack applied when they view this character's profile.
            if (character.RPProfile != null)
                character.RPProfile.AppliedPack = editedCharacterUseGlitchNameEffect ? "glitch" : null;
            character.Alias = string.IsNullOrWhiteSpace(editedCharacterAlias) ? null : editedCharacterAlias;

            // Keep rp.CharacterName aligned with Name/Alias so renames and alias changes don't
            // leave stale values behind. Stale rp.CharacterName can collide across characters on
            // the server (same filename) and cause name/image mismatches via upload paths that
            // pass the stored RPProfile directly instead of going through BuildProfileForUpload.
            if (character.RPProfile != null)
            {
                character.RPProfile.CharacterName = !string.IsNullOrWhiteSpace(character.Alias)
                    ? character.Alias
                    : character.Name;
            }

            // Rename migration: if the effective display name (Alias ?? Name) changed and we know
            // which in-game character this CS+ character was last applied to, record the old
            // server fileKey so the next upload migrates likes and deletes the orphan on the server.
            string newDisplayName = !string.IsNullOrWhiteSpace(character.Alias) ? character.Alias : character.Name;
            if (!string.Equals(oldDisplayName, newDisplayName, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(oldLastInGameName))
            {
                string oldFileKey = $"{oldDisplayName}_{oldLastInGameName}";
                character.AddPreviousProfileKey(oldFileKey);
            }

            character.Macros = isAdvancedModeCharacter ? advancedCharacterMacroText : editedCharacterMacros;

            if (!string.IsNullOrEmpty(editedCharacterImagePath))
            {
                character.ImagePath = editedCharacterImagePath;
            }

            // Portrait framing (always saved, applies regardless of hover mode)
            character.PortraitOffsetX = editedPortraitOffsetX;
            character.PortraitOffsetY = editedPortraitOffsetY;
            character.PortraitZoom    = editedPortraitZoom;

            // Hover mode, only the active mode's paths persist on save.
            // Switching modes mid-edit doesn't clobber paths, but committing
            // the radio's current selection wins.
            if (_hoverModeRadio == 1)
            {
                character.AnimatedImagePath = string.IsNullOrWhiteSpace(editedAnimatedImagePath) ? null : editedAnimatedImagePath;
                character.AnimatedOffsetX = editedAnimatedOffsetX;
                character.AnimatedOffsetY = editedAnimatedOffsetY;
                character.AnimatedZoom    = editedAnimatedZoom;
                character.CutoutImagePath = null;
                character.CutoutBackdropPath = null;
            }
            else if (_hoverModeRadio == 2)
            {
                character.AnimatedImagePath = null;
                character.CutoutImagePath = string.IsNullOrWhiteSpace(editedCutoutImagePath) ? null : editedCutoutImagePath;
                character.CutoutBackdropPath = string.IsNullOrWhiteSpace(editedCutoutBackdropPath) ? null : editedCutoutBackdropPath;
                character.CutoutScale = editedCutoutScale;
                character.CutoutAnchorX = editedCutoutAnchorX;
                character.CutoutAnchorY = editedCutoutAnchorY;
                // Migrate old saves: pose-anchor was (0.5, 0.5) before; force to
                // (0.5, 1.0) on every save so the data matches what the renderer
                // and form preview both assume.
                character.CutoutPoseAx = 0.5f;
                character.CutoutPoseAy = 1.0f;
            }
            else // None
            {
                character.AnimatedImagePath = null;
                character.CutoutImagePath = null;
                character.CutoutBackdropPath = null;
            }

            // Note: SecretModState is handled directly in the SecretModeModWindow callback
            // and doesn't need to be copied here since it's already persisted to the character object

            // Achievement hooks for feature discovery
            if (!string.IsNullOrWhiteSpace(character.Alias)) plugin.AchievementTracker?.OnAliasSet();
            if (!string.IsNullOrEmpty(editedCharacterImagePath)) plugin.AchievementTracker?.OnProfileImageSet();
            if (character.IdlePoseIndex < 7) plugin.AchievementTracker?.OnPoseSet();
            if (isAdvancedModeCharacter) plugin.AchievementTracker?.OnAdvancedModeUsed();
            if (!string.IsNullOrWhiteSpace(character.RPProfile?.Pronouns)) plugin.AchievementTracker?.OnPronounsSet();
            if (editedCharacterColor != default) plugin.AchievementTracker?.OnNameplateColorSet();
            if (character.Tags?.Count > 0) plugin.AchievementTracker?.OnTagsUsed();
            if (!string.IsNullOrWhiteSpace(editedCharacterAutomation)) plugin.AchievementTracker?.OnGlamourerAutomationSet();

            // New integration achievements
            if (!string.IsNullOrWhiteSpace(character.HonorificTitle)) plugin.AchievementTracker?.OnHonorificTitleSet();
            if (!string.IsNullOrWhiteSpace(character.CustomizeProfile)) plugin.AchievementTracker?.OnCustomizePlusSet();
            if (character.HonorificGradientSet == -1) plugin.AchievementTracker?.OnTwoColourGradientSet();
            // Triple integration: all three plugin fields set on this character
            if (!string.IsNullOrWhiteSpace(character.GlamourerDesign)
                && !string.IsNullOrWhiteSpace(character.CustomizeProfile)
                && !string.IsNullOrWhiteSpace(character.HonorificTitle))
                plugin.AchievementTracker?.OnTripleIntegrationSet();

            // ERP profile depth checks
            var rp = character.RPProfile;
            if (rp != null)
            {
                if ((rp.Bio?.Length ?? 0) >= 500) plugin.AchievementTracker?.OnLongBioWritten();
                if (!string.IsNullOrWhiteSpace(rp.BannerImagePath)) plugin.AchievementTracker?.OnBannerImageSet();
                if (!string.IsNullOrWhiteSpace(rp.BackgroundImageUrl) || !string.IsNullOrWhiteSpace(rp.RPBackgroundImageUrl))
                    plugin.AchievementTracker?.OnUrlBackgroundSet();

                int totalBoxes = (rp.LeftContentBoxes?.Count ?? 0) + (rp.RightContentBoxes?.Count ?? 0);
                if (totalBoxes >= 6) plugin.AchievementTracker?.OnSixContentBoxes();

                // Layout type checks
                bool hasTimeline = (rp.LeftContentBoxes?.Any(b => b.LayoutType == ContentBoxLayoutType.Timeline) ?? false)
                                || (rp.RightContentBoxes?.Any(b => b.LayoutType == ContentBoxLayoutType.Timeline) ?? false);
                bool hasQuote    = (rp.LeftContentBoxes?.Any(b => b.LayoutType == ContentBoxLayoutType.Quote) ?? false)
                                || (rp.RightContentBoxes?.Any(b => b.LayoutType == ContentBoxLayoutType.Quote) ?? false);
                if (hasTimeline) plugin.AchievementTracker?.OnTimelineLayoutUsed();
                if (hasQuote)    plugin.AchievementTracker?.OnQuoteLayoutUsed();

                // Layout type breadth - count distinct layouts across ALL characters (not just this one)
                var distinctLayouts = new HashSet<ContentBoxLayoutType>();
                foreach (var c in plugin.Characters)
                {
                    if (c.RPProfile?.LeftContentBoxes != null)
                        foreach (var b in c.RPProfile.LeftContentBoxes) distinctLayouts.Add(b.LayoutType);
                    if (c.RPProfile?.RightContentBoxes != null)
                        foreach (var b in c.RPProfile.RightContentBoxes) distinctLayouts.Add(b.LayoutType);
                }
                if (distinctLayouts.Count >= 5) plugin.AchievementTracker?.OnLayoutTypesExplored();

                // Fully Realised composite
                bool hasBio      = !string.IsNullOrWhiteSpace(rp.Bio);
                bool hasPronouns = !string.IsNullOrWhiteSpace(rp.Pronouns);
                bool hasImage    = !string.IsNullOrWhiteSpace(character.ImagePath);
                bool hasBg       = !string.IsNullOrWhiteSpace(rp.BackgroundImage)
                                || !string.IsNullOrWhiteSpace(rp.BackgroundImageUrl)
                                || !string.IsNullOrWhiteSpace(rp.RPBackgroundImageUrl);
                bool hasBox      = totalBoxes > 0;
                if (hasBio && hasPronouns && hasImage && hasBg && hasBox)
                    plugin.AchievementTracker?.OnProfileCompleted();
            }

            plugin.SaveConfiguration();

            // Check if name changed and user has an active warning
            if (!string.IsNullOrEmpty(editedCharacterName) &&
                editedCharacterName != originalCharacterName &&
                plugin.ActiveNameWarning != null)
            {
                // Fire and forget - check name change for warning resolution
                _ = CheckNameChangeForWarningAsync(editedCharacterName);
            }
        }

        private async System.Threading.Tasks.Task CheckNameChangeForWarningAsync(string newName)
        {
            try
            {
                var result = await plugin.CheckNameChangeForWarning(newName);

                if (result.HasWarning && !string.IsNullOrEmpty(result.Message))
                {
                    // Show feedback in chat
                    Plugin.Framework.RunOnTick(() =>
                    {
                        if (result.Resolved)
                        {
                            // Green success message
                            var msg = new DalamudSeStringBuilder()
                                .AddText("[")
                                .AddGreen("CS+", true)
                                .AddText("] ")
                                .AddGreen(result.Message, false)
                                .Build();
                            Plugin.ChatGui.Print(msg);
                        }
                        else if (result.PendingReview)
                        {
                            // Yellow pending message
                            var msg = new DalamudSeStringBuilder()
                                .AddText("[")
                                .AddYellow("CS+", true)
                                .AddText("] ")
                                .AddYellow(result.Message, false)
                                .Build();
                            Plugin.ChatGui.Print(msg);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[CharacterForm] Error checking name change: {ex.Message}");
            }
        }

        public void OpenEditCharacterWindow(int index)
        {
            if (index < 0 || index >= plugin.Characters.Count)
                return;

            selectedCharacterIndex = index;
            var character = plugin.Characters[index];

            string pluginDirectory = plugin.PluginDirectory;
            string defaultImagePath = Path.Combine(pluginDirectory, "Assets", "Default.png");

            editedCharacterName = character.Name;
            originalCharacterName = character.Name; // Store original for warning resolution check
            editedCharacterPenumbra = character.PenumbraCollection;
            editedCharacterGlamourer = character.GlamourerDesign;
            editedCharacterCustomize = character.CustomizeProfile ?? "";
            editedCharacterColor = character.NameplateColor;

            editedCharacterMacros = character.Macros;

            editedCharacterImagePath = !string.IsNullOrEmpty(character.ImagePath) ? character.ImagePath : defaultImagePath;
            editedAnimatedImagePath = character.AnimatedImagePath;
            editedCutoutImagePath = character.CutoutImagePath;
            editedCutoutBackdropPath = character.CutoutBackdropPath;
            editedCutoutScale = character.CutoutScale;
            editedCutoutAnchorX = character.CutoutAnchorX;
            editedCutoutAnchorY = character.CutoutAnchorY;
            editedPortraitOffsetX = character.PortraitOffsetX;
            editedPortraitOffsetY = character.PortraitOffsetY;
            editedPortraitZoom    = character.PortraitZoom;
            editedAnimatedOffsetX = character.AnimatedOffsetX;
            editedAnimatedOffsetY = character.AnimatedOffsetY;
            editedAnimatedZoom    = character.AnimatedZoom;
            // Derive radio state, both can't be set, but if they are
            // (corrupt config), prefer cutout (newer feature).
            if (!string.IsNullOrWhiteSpace(character.CutoutImagePath)) _hoverModeRadio = 2;
            else if (!string.IsNullOrWhiteSpace(character.AnimatedImagePath)) _hoverModeRadio = 1;
            else _hoverModeRadio = 0;
            editedCharacterTag = character.Tags != null && character.Tags.Count > 0
                ? string.Join(", ", character.Tags)
                : "";

            editedCharacterHonorificTitle = character.HonorificTitle ?? "";
            editedCharacterHonorificPrefix = character.HonorificPrefix ?? "Prefix";
            editedCharacterHonorificSuffix = character.HonorificSuffix ?? "Suffix";
            editedCharacterHonorificColor = character.HonorificColor;
            editedCharacterHonorificGlow = character.HonorificGlow;
            editedCharacterHonorificColor3 = character.HonorificColor3;
            editedCharacterHonorificGradientSet = character.HonorificGradientSet;
            editedCharacterHonorificAnimationStyle = character.HonorificAnimationStyle;
            editedCharacterMoodlePreset = character.MoodlePreset ?? "";
            editedCharacterGearset = character.AssignedGearset;
            editedCharacterExcludeFromNameSync = character.ExcludeFromNameSync;
            editedCharacterUseGlitchNameEffect = character.UseGlitchNameEffect;
            editedCharacterAlias = character.Alias ?? "";

            string safeAutomation = character.CharacterAutomation == "None" ? "" : character.CharacterAutomation ?? "";
            editedCharacterAutomation = safeAutomation;

            // Copy to temp fields
            tempHonorificTitle = editedCharacterHonorificTitle;
            tempHonorificPrefix = editedCharacterHonorificPrefix;
            tempHonorificSuffix = editedCharacterHonorificSuffix;
            tempHonorificColor = editedCharacterHonorificColor;
            tempHonorificGlow = editedCharacterHonorificGlow;
            tempHonorificColor3 = editedCharacterHonorificColor3 ?? new Vector3(0.5f, 0.5f, 1.0f);
            tempHonorificGradientSet = editedCharacterHonorificGradientSet;
            tempHonorificAnimationStyle = editedCharacterHonorificAnimationStyle;
            tempMoodlePreset = editedCharacterMoodlePreset;

            if (isAdvancedModeCharacter)
            {
                advancedCharacterMacroText = character.Macros;
            }
            // Restore advanced mode state
            isAdvancedModeCharacter = character.IsAdvancedMode;

            if (isAdvancedModeCharacter)
            {
                advancedCharacterMacroText = character.Macros;
            }
            IsEditWindowOpen = true;
        }

        private void ValidateCharacterName(string name)
        {
            nameValidationError = "";

            if (string.IsNullOrWhiteSpace(name))
                return;

            // Check if name already exists
            bool nameExists;
            if (IsEditWindowOpen && selectedCharacterIndex >= 0 && selectedCharacterIndex < plugin.Characters.Count)
            {
                // When editing, exclude the current character from the check
                var currentCharName = plugin.Characters[selectedCharacterIndex].Name;
                nameExists = plugin.Characters.Any(c =>
                    !c.Name.Equals(currentCharName, StringComparison.OrdinalIgnoreCase) &&
                    c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                // When creating new, check all characters
                nameExists = plugin.Characters.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }

            if (nameExists)
            {
                nameValidationError = "You already have a character with this name. Please choose a different name. " +
                                    "Try adding a number or variation (e.g., Name 2, Name Alt, etc.)";
            }
        }
    }
}

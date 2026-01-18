using System;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin.Windows;

public class WarningModalWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly HttpClient httpClient;

    private NameWarning? currentWarning = null;
    private bool checkboxChecked = false;
    private bool isAcknowledging = false;
    private string? errorMessage = null;

    private const string ApiBaseUrl = "https://character-select-profile-server-production.up.railway.app";

    public WarningModalWindow(Plugin plugin) : base("###CSPlusWarningModal",
        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        this.httpClient = new HttpClient();
        this.httpClient.Timeout = TimeSpan.FromSeconds(15);

        Size = new Vector2(450, 340);
        SizeCondition = ImGuiCond.Always;
    }

    public void Dispose()
    {
        httpClient?.Dispose();
    }

    public void ShowWarning(NameWarning warning)
    {
        this.currentWarning = warning;
        this.checkboxChecked = false;
        this.isAcknowledging = false;
        this.errorMessage = null;
        IsOpen = true;
    }

    public override void PreDraw()
    {
        var viewport = ImGui.GetMainViewport();
        var windowSize = Size ?? new Vector2(450, 340);
        var centerPos = new Vector2(
            viewport.Pos.X + (viewport.Size.X - windowSize.X) / 2,
            viewport.Pos.Y + (viewport.Size.Y - windowSize.Y) / 2
        );
        ImGui.SetNextWindowPos(centerPos, ImGuiCond.Always);
    }

    public override void Draw()
    {
        if (currentWarning == null)
        {
            IsOpen = false;
            return;
        }

        var scale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);
        var windowWidth = ImGui.GetWindowWidth();
        var contentWidth = windowWidth - 40 * scale;

        int themeColorCount = ThemeHelper.PushThemeColors(plugin.Configuration);
        int themeStyleVarCount = ThemeHelper.PushThemeStyleVars(plugin.Configuration.UIScaleMultiplier);

        try
        {
            Vector4 accentColor = currentWarning.Status switch
            {
                "permaban" => new Vector4(0.9f, 0.25f, 0.25f, 1.0f),
                "warning2" => new Vector4(1.0f, 0.6f, 0.2f, 1.0f),
                _ => new Vector4(1.0f, 0.85f, 0.3f, 1.0f)
            };

            var drawList = ImGui.GetWindowDrawList();
            var windowPos = ImGui.GetWindowPos();
            drawList.AddRectFilled(
                new Vector2(windowPos.X, windowPos.Y),
                new Vector2(windowPos.X + windowWidth, windowPos.Y + 4 * scale),
                ImGui.ColorConvertFloat4ToU32(accentColor)
            );

            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 12 * scale);

            string title = currentWarning.Status == "permaban"
                ? "CS+ Names Feature Disabled"
                : "CS+ Name Warning";

            ImGui.PushStyleColor(ImGuiCol.Text, accentColor);
            CenterText(title, windowWidth, 1.2f);
            ImGui.PopStyleColor();

            if (currentWarning.Status != "permaban")
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
                CenterText($"Strike {currentWarning.StrikeNumber} of 3", windowWidth, 1.0f);
                ImGui.PopStyleColor();
            }

            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
            CenterText("Your CS+ name has been reported and hidden:", windowWidth, 1.0f);
            ImGui.PopStyleColor();

            ImGui.Spacing();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.5f, 0.5f, 1.0f));
            CenterText($"\"{currentWarning.OffensiveCSName}\"", windowWidth, 1.1f);
            ImGui.PopStyleColor();

            ImGui.Spacing();
            ImGui.Spacing();

            if (currentWarning.Status == "permaban")
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
                CenterText("Due to repeated violations, your CS+ name", windowWidth, 1.0f);
                CenterText("will no longer be visible to other players.", windowWidth, 1.0f);
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.75f, 0.8f, 0.9f, 1.0f));
                CenterText("Change your CS+ name to restore visibility.", windowWidth, 1.0f);
                ImGui.PopStyleColor();

                if (currentWarning.Status == "warning2")
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
                    CenterText("Your new name will require moderator approval.", windowWidth, 1.0f);
                    ImGui.PopStyleColor();
                }
            }

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Spacing();

            string checkboxText = currentWarning.Status switch
            {
                "permaban" => "I understand",
                "warning2" => "I understand (my next name requires review)",
                _ => "I understand and will change my name"
            };

            var checkboxWidth = ImGui.CalcTextSize(checkboxText).X + 30 * scale;
            ImGui.SetCursorPosX((windowWidth - checkboxWidth) / 2);
            ImGui.Checkbox(checkboxText, ref checkboxChecked);

            if (!string.IsNullOrEmpty(errorMessage))
            {
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.4f, 0.4f, 1.0f));
                CenterText(errorMessage, windowWidth, 1.0f);
                ImGui.PopStyleColor();
            }

            ImGui.Spacing();
            ImGui.Spacing();

            var buttonWidth = 140f * scale;
            var buttonHeight = 32f * scale;
            ImGui.SetCursorPosX((windowWidth - buttonWidth) / 2);

            if (isAcknowledging)
            {
                ImGui.BeginDisabled();
                ImGui.Button("Acknowledging...", new Vector2(buttonWidth, buttonHeight));
                ImGui.EndDisabled();
            }
            else if (!checkboxChecked)
            {
                ImGui.BeginDisabled();
                ImGui.Button("Acknowledge", new Vector2(buttonWidth, buttonHeight));
                ImGui.EndDisabled();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.5f, 0.2f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.6f, 0.25f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.3f, 0.65f, 0.3f, 1.0f));

                if (ImGui.Button("Acknowledge", new Vector2(buttonWidth, buttonHeight)))
                {
                    _ = AcknowledgeWarning();
                }

                ImGui.PopStyleColor(3);
            }
        }
        finally
        {
            ThemeHelper.PopThemeStyleVars(themeStyleVarCount);
            ThemeHelper.PopThemeColors(themeColorCount);
        }
    }

    private void CenterText(string text, float windowWidth, float fontScale)
    {
        if (fontScale != 1.0f) ImGui.SetWindowFontScale(fontScale);
        var textWidth = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorPosX((windowWidth - textWidth) / 2);
        ImGui.Text(text);
        if (fontScale != 1.0f) ImGui.SetWindowFontScale(1.0f);
    }

    private async Task AcknowledgeWarning()
    {
        if (currentWarning == null) return;

        if (currentWarning.Id.StartsWith("test-"))
        {
            Plugin.Log.Info($"Test warning dismissed: {currentWarning.Id}");
            IsOpen = false;
            currentWarning = null;
            return;
        }

        isAcknowledging = true;
        errorMessage = null;

        try
        {
            var response = await httpClient.PostAsync(
                $"{ApiBaseUrl}/user/warnings/{currentWarning.Id}/acknowledge",
                new StringContent("{}", Encoding.UTF8, "application/json")
            );

            if (response.IsSuccessStatusCode)
            {
                Plugin.Log.Info($"Warning acknowledged: {currentWarning.Id}");
                IsOpen = false;
                currentWarning = null;
            }
            else
            {
                errorMessage = "Failed to acknowledge. Please try again.";
                Plugin.Log.Error($"Failed to acknowledge warning: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"Error: {ex.Message}";
            Plugin.Log.Error($"Error acknowledging warning: {ex.Message}");
        }
        finally
        {
            isAcknowledging = false;
        }
    }

    private float GetSafeScale(float scale)
    {
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0)
            return 1.0f;
        return Math.Clamp(scale, 0.5f, 3.0f);
    }

    public override void OnClose()
    {
        if (currentWarning != null && !checkboxChecked)
        {
            IsOpen = true;
        }
    }
}

public class NameWarning
{
    public string Id { get; set; } = "";
    public string PhysicalName { get; set; } = "";
    public string OffensiveCSName { get; set; } = "";
    public int StrikeNumber { get; set; }
    public string Status { get; set; } = "";
    public bool Acknowledged { get; set; }
    public bool Resolved { get; set; }
}

public class UserWarningsResponse
{
    public bool HasUnacknowledgedWarning { get; set; }
    public NameWarning[]? UnacknowledgedWarnings { get; set; }
    public NameWarning? ActiveWarning { get; set; }
    public int StrikeCount { get; set; }
    public bool IsPermabanned { get; set; }
}

public class NameChangeResponse
{
    public bool Success { get; set; }
    public bool Resolved { get; set; }
    public bool NeedsReview { get; set; }
    public string? Message { get; set; }
}

public class NameChangeResult
{
    public bool HasWarning { get; set; }
    public bool Resolved { get; set; }
    public bool PendingReview { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
}

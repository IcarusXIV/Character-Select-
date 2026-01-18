using System;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using CharacterSelectPlugin.Windows.Styles;

namespace CharacterSelectPlugin.Windows;

public class ReportUserWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly HttpClient httpClient;

    private string physicalName = "";
    private string csName = "";
    private string reportMessage = "";
    private bool isSubmitting = false;
    private string? submitResult = null;
    private DateTime? submitResultTime = null;

    private const string ApiBaseUrl = "https://character-select-profile-server-production.up.railway.app";

    public ReportUserWindow(Plugin plugin) : base("Report CS+ User###CSPlusReportWindow",
        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar)
    {
        this.plugin = plugin;
        this.httpClient = new HttpClient();
        this.httpClient.Timeout = TimeSpan.FromSeconds(15);

        Size = new Vector2(420, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
        httpClient?.Dispose();
    }

    public void Open(string physicalName, string csName)
    {
        this.physicalName = physicalName;
        this.csName = csName;
        this.reportMessage = "";
        this.isSubmitting = false;
        this.submitResult = null;
        this.submitResultTime = null;
        IsOpen = true;
    }

    public override void Draw()
    {
        var totalScale = GetSafeScale(ImGuiHelpers.GlobalScale * plugin.Configuration.UIScaleMultiplier);

        if (submitResultTime.HasValue && DateTime.Now - submitResultTime.Value > TimeSpan.FromSeconds(5))
        {
            submitResult = null;
            submitResultTime = null;
        }

        int themeColorCount = ThemeHelper.PushThemeColors(plugin.Configuration);
        int themeStyleVarCount = ThemeHelper.PushThemeStyleVars(plugin.Configuration.UIScaleMultiplier);

        try
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.6f, 0.6f, 1.0f));
            ImGui.Text("Report Offensive CS+ Name");
            ImGui.PopStyleColor();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
            ImGui.Text("CS+ Name:");
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.5f, 0.5f, 1.0f));
            ImGui.Text(csName);
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
            ImGui.Text("In-Game Name:");
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.Text(physicalName);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.9f, 1.0f));
            ImGui.Text("Describe the issue:");
            ImGui.PopStyleColor();

            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextMultiline("##reportMessage", ref reportMessage, 500,
                new Vector2(-1, 80 * totalScale));

            if (!string.IsNullOrEmpty(submitResult))
            {
                ImGui.Spacing();
                var isError = submitResult.StartsWith("Error") || submitResult.StartsWith("Please");
                var color = isError
                    ? new Vector4(1.0f, 0.4f, 0.4f, 1.0f)
                    : new Vector4(0.4f, 1.0f, 0.4f, 1.0f);
                ImGui.PushStyleColor(ImGuiCol.Text, color);
                ImGui.TextWrapped(submitResult);
                ImGui.PopStyleColor();
            }

            ImGui.Spacing();
            ImGui.Spacing();

            var buttonWidth = 120f * totalScale;

            if (isSubmitting)
            {
                ImGui.BeginDisabled();
                ImGui.Button("Submitting...", new Vector2(buttonWidth, 0));
                ImGui.EndDisabled();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.3f, 0.3f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.8f, 0.4f, 0.4f, 1.0f));
                if (ImGui.Button("Submit Report", new Vector2(buttonWidth, 0)))
                {
                    if (string.IsNullOrWhiteSpace(reportMessage))
                    {
                        submitResult = "Please enter a message describing the issue.";
                        submitResultTime = DateTime.Now;
                    }
                    else
                    {
                        _ = SubmitReport();
                    }
                }
                ImGui.PopStyleColor(3);
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
            {
                IsOpen = false;
            }
        }
        finally
        {
            ThemeHelper.PopThemeStyleVars(themeStyleVarCount);
            ThemeHelper.PopThemeColors(themeColorCount);
        }
    }

    private float GetSafeScale(float scale)
    {
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0)
            return 1.0f;
        return Math.Clamp(scale, 0.5f, 3.0f);
    }

    private async Task SubmitReport()
    {
        isSubmitting = true;
        submitResult = null;

        try
        {
            var reporterName = GetReporterName();

            var requestBody = new
            {
                reportedCharacterId = physicalName,
                reportedCharacterName = physicalName,
                offensiveCSName = csName,
                reporterCharacter = reporterName,
                reason = "Offensive CS+ Name",
                details = reportMessage
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync($"{ApiBaseUrl}/reports", content);

            if (response.IsSuccessStatusCode)
            {
                submitResult = "Report submitted successfully. Thank you!";
                submitResultTime = DateTime.Now;

                await Task.Delay(2000);
                IsOpen = false;
            }
            else
            {
                submitResult = $"Error: Server returned {response.StatusCode}";
                submitResultTime = DateTime.Now;
            }
        }
        catch (Exception ex)
        {
            submitResult = $"Error: {ex.Message}";
            submitResultTime = DateTime.Now;
            Plugin.Log.Error($"Failed to submit report: {ex.Message}");
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private string GetReporterName()
    {
        var localPlayer = Plugin.ClientState?.LocalPlayer;
        if (localPlayer != null)
        {
            var name = localPlayer.Name.TextValue;
            var world = localPlayer.HomeWorld.Value.Name.ToString();
            return $"{name}@{world}";
        }
        return "Unknown@Unknown";
    }
}

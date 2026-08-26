using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using CharacterSelectPlugin.Managers;

namespace CharacterSelectPlugin.Windows.Components
{
    public partial class DesignPanel
    {
        private bool isSnapshotPickerOpen = false;
        private List<(string Name, Guid Id, float Score, int Fields)> snapshotPickerCandidates = new();
        private Character? snapshotPickerCharacter;
        private bool snapshotPickerUseCR;
        private bool snapshotPickerHadExactTie;

        private Task<List<(string Name, Guid Id, float Score, int Fields)>> FindAppliedGlamourerDesigns()
            => GlamourerDesignMatcher.FindApplied();

        private async Task RunSmartSnapshot(Character character, bool useConflictResolution, string designName, Guid designId)
        {
            snapshotTargetCharacter = character;
            snapshotDesignName = designName;
            snapshotUseConflictResolution = useConflictResolution;
            snapshotIsProcessing = true;

            var detectionTasks = new Task[]
            {
                DetectGlamourerState(),
                DetectCustomizePlusProfile(),
                Task.Run(() => CheckClipboardForImage())
            };
            await Task.WhenAll(detectionTasks);

            CreateSmartSnapshotDesign((designName, DateTimeOffset.MinValue, designId));

            Plugin.ChatGui.Print($"[Character Select+] Smart snapshot created: '{designName}' {(useConflictResolution ? "with" : "without")} CR");
        }

        private void DrawSmartSnapshotPicker(float scale)
        {
            if (!isSnapshotPickerOpen)
                return;

            var io = ImGui.GetIO();
            ImGui.SetNextWindowSize(new Vector2(440 * scale, 0), ImGuiCond.Always);
            ImGui.SetNextWindowPos(new Vector2(io.DisplaySize.X * 0.5f, io.DisplaySize.Y * 0.5f), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

            bool open = isSnapshotPickerOpen;
            if (ImGui.Begin("Which design is this look?##SnapshotPicker", ref open, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize))
            {
                ImGui.TextWrapped(snapshotPickerHadExactTie
                    ? "Several designs match your current look exactly. Pick the one to save this design with:"
                    : "No design matches your current look exactly. Closest matches:");
                ImGui.Spacing();

                foreach (var c in snapshotPickerCandidates)
                {
                    if (ImGui.Button($"{c.Name}  ({c.Score * 100f:0}% match)##pick_{c.Id}", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
                    {
                        var character = snapshotPickerCharacter;
                        var useCR = snapshotPickerUseCR;
                        var name = c.Name;
                        var id = c.Id;
                        isSnapshotPickerOpen = false;
                        if (character != null)
                            Task.Run(() => RunSmartSnapshot(character, useCR, name, id));
                    }
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.Button("Use Newest Design"))
                {
                    var character = snapshotPickerCharacter;
                    var useCR = snapshotPickerUseCR;
                    isSnapshotPickerOpen = false;
                    if (character != null)
                    {
                        Task.Run(async () =>
                        {
                            var recent = await GetMostRecentGlamourerDesign();
                            if (recent == null || string.IsNullOrEmpty(recent.Value.Name))
                            {
                                Plugin.ChatGui.PrintError("[Character Select+] No recent Glamourer design found.");
                                return;
                            }
                            await RunSmartSnapshot(character, useCR, recent.Value.Name, recent.Value.Id);
                        });
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                    isSnapshotPickerOpen = false;
            }
            ImGui.End();

            if (!open)
                isSnapshotPickerOpen = false;
        }
    }
}

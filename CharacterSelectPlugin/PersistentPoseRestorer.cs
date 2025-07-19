using CharacterSelectPlugin.Managers;
using CharacterSelectPlugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using System;

public unsafe class SimplifiedPoseRestorer
{
    private readonly IClientState clientState;
    private readonly ImprovedPoseManager poseManager;

    public SimplifiedPoseRestorer(IClientState clientState, ImprovedPoseManager poseManager)
    {
        this.clientState = clientState;
        this.poseManager = poseManager;
    }

    public void RestorePosesFor(Character character)
    {
        if (clientState.LocalPlayer == null)
            return;
        Plugin.Framework.RunOnTick(() =>
        {
            ApplyCharacterPoses(character);
        }, delayTicks: 30);
    }

    private void ApplyCharacterPoses(Character character)
    {
        if (clientState.LocalPlayer?.Address == IntPtr.Zero)
            return;

        var poses = new[]
        {
            (EmoteController.PoseType.Idle, character.IdlePoseIndex),
            (EmoteController.PoseType.Sit, character.SitPoseIndex),
            (EmoteController.PoseType.GroundSit, character.GroundSitPoseIndex),
            (EmoteController.PoseType.Doze, character.DozePoseIndex)
        };

        foreach (var (type, index) in poses)
        {
            if (index < 7) 
            {
                poseManager.ApplyPose(type, index);
            }
        }
    }
}

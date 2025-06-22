// CharacterSelectPlugin/Managers/PoseRestorer.cs
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System;

namespace CharacterSelectPlugin.Managers;

public unsafe class PoseRestorer
{
    private readonly IClientState clientState;
    private readonly Plugin plugin;

    public PoseRestorer(IClientState clientState, Plugin plugin)
    {
        this.clientState = clientState;
        this.plugin = plugin;
    }

    public void RestorePosesFor(Character character)
    {
        if (clientState.LocalPlayer == null) return;

        Plugin.Framework.RunOnTick(() =>
        {
            ApplyPose(character);
        });
    }

    private void ApplyPose(Character character)
    {
        var local = clientState.LocalPlayer;
        if (local == null || local.Address == IntPtr.Zero)
            return;

        var charPtr = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)local.Address;

        // This also ensures you're not in cutscene or a bad player state
        if (charPtr->GameObject.ObjectIndex == 0xFFFF)
            return;

        TrySetPose(EmoteController.PoseType.Idle, character.IdlePoseIndex, charPtr);
        TrySetPose(EmoteController.PoseType.Sit, character.SitPoseIndex, charPtr);
        TrySetPose(EmoteController.PoseType.GroundSit, character.GroundSitPoseIndex, charPtr);
        TrySetPose(EmoteController.PoseType.Doze, character.DozePoseIndex, charPtr);
    }

    private void TrySetPose(EmoteController.PoseType type, byte desired, FFXIVClientStructs.FFXIV.Client.Game.Character.Character* charPtr)
    {
        if (desired >= 254) return;

        byte current = PlayerState.Instance()->SelectedPoses[(int)type];
        if (current == desired) return;

        PlayerState.Instance()->SelectedPoses[(int)type] = desired;

        switch (type)
        {
            case EmoteController.PoseType.Idle:
                plugin.Configuration.LastIdlePoseAppliedByPlugin = desired;
                break;
            case EmoteController.PoseType.Sit:
                plugin.Configuration.LastSitPoseAppliedByPlugin = desired;
                break;
            case EmoteController.PoseType.GroundSit:
                plugin.Configuration.LastGroundSitPoseAppliedByPlugin = desired;
                break;
            case EmoteController.PoseType.Doze:
                plugin.Configuration.LastDozePoseAppliedByPlugin = desired;
                break;
        }

        plugin.Configuration.Save();

        if (TranslatePoseState(charPtr->ModeParam) == type)
            charPtr->EmoteController.CPoseState = desired;
    }

    private EmoteController.PoseType TranslatePoseState(byte state)
    {
        return state switch
        {
            1 => EmoteController.PoseType.GroundSit,
            2 => EmoteController.PoseType.Sit,
            3 => EmoteController.PoseType.Doze,
            _ => EmoteController.PoseType.Idle
        };
    }
}

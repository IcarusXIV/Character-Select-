using CharacterSelectPlugin.Managers;
using CharacterSelectPlugin;
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

    /// <summary>
    /// Restores saved poses; pass false on a flag to skip that pose (e.g. when
    /// a design macro already sets it explicitly).
    /// </summary>
    public void RestorePosesFor(
        Character character,
        bool applyIdle = true,
        bool applySit = true,
        bool applyGroundSit = true,
        bool applyDoze = true)
    {
        if (Plugin.ObjectTable.LocalPlayer == null) return;

        Plugin.Framework.RunOnTick(() =>
        {
            ApplyPose(character, applyIdle, applySit, applyGroundSit, applyDoze);
        }, delayTicks: 30);
    }

    private void ApplyPose(Character character, bool applyIdle, bool applySit, bool applyGroundSit, bool applyDoze)
    {
        var local = Plugin.ObjectTable.LocalPlayer;
        if (local == null || local.Address == IntPtr.Zero)
            return;

        var charPtr = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)local.Address;

        // This also ensures you're not in cutscene or a bad player state
        if (charPtr->GameObject.ObjectIndex == 0xFFFF)
            return;

        if (applyIdle)      TrySetPose(EmoteController.PoseType.Idle,      character.IdlePoseIndex,      charPtr);
        if (applySit)       TrySetPose(EmoteController.PoseType.Sit,       character.SitPoseIndex,       charPtr);
        if (applyGroundSit) TrySetPose(EmoteController.PoseType.GroundSit, character.GroundSitPoseIndex, charPtr);
        if (applyDoze)      TrySetPose(EmoteController.PoseType.Doze,      character.DozePoseIndex,      charPtr);
    }

    private void TrySetPose(EmoteController.PoseType type, byte desired, FFXIVClientStructs.FFXIV.Client.Game.Character.Character* charPtr)
    {
        if (desired >= 7) return; // Valid poses are 0-6 only; 7+ means "None"

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

        // Use the PoseManager's new method instead of direct memory write
        if (TranslatePoseState(charPtr->ModeParam) == type)
        {
            plugin.PoseManager?.ApplyPose(type, desired);
        }
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

using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace CharacterSelectPlugin.Managers;

public unsafe class PoseManager
{
    private readonly IClientState clientState;
    private readonly IFramework framework;
    private readonly IChatGui chatGui;
    private readonly ICommandManager commandManager;
    private readonly Plugin plugin;


    public PoseManager(IClientState clientState, IFramework framework, IChatGui chatGui, ICommandManager commandManager, Plugin plugin)
    {
        this.clientState = clientState;
        this.framework = framework;
        this.chatGui = chatGui;
        this.commandManager = commandManager;
        this.plugin = plugin;
    }

    public void ApplyPose(EmoteController.PoseType type, byte index)
    {
        Plugin.Log.Debug($"[ApplyPose] Applying {type} pose {index}");

        if (index >= 7 || clientState.LocalPlayer == null)
            return;

        var charPtr = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)clientState.LocalPlayer.Address;

        // Apply to memory
        PlayerState.Instance()->SelectedPoses[(int)type] = index;

        if (TranslatePoseState(charPtr->ModeParam) == type)
            charPtr->EmoteController.CPoseState = index;

        // Persist if plugin-driven
        switch (type)
        {
            case EmoteController.PoseType.Idle:
                plugin.Configuration.DefaultPoses.Idle = index;
                plugin.Configuration.LastIdlePoseAppliedByPlugin = index;
                plugin.lastSeenIdlePose = index;
                plugin.suppressIdleSaveForFrames = 60; // longer block
                Plugin.Log.Debug($"[ApplyPose] Idle pose set to {index} and suppressed for 60 frames.");
                break;

            case EmoteController.PoseType.Sit:
                plugin.Configuration.DefaultPoses.Sit = index;
                plugin.lastSeenSitPose = index;
                plugin.suppressSitSaveForFrames = 60;
                Plugin.Log.Debug($"[ApplyPose] Sit pose set to {index} and suppressed for 60 frames.");
                break;

            case EmoteController.PoseType.GroundSit:
                plugin.Configuration.DefaultPoses.GroundSit = index;
                plugin.lastSeenGroundSitPose = index;
                plugin.suppressGroundSitSaveForFrames = 60;
                Plugin.Log.Debug($"[ApplyPose] Ground Sit pose set to {index} and suppressed for 60 frames.");
                break;

            case EmoteController.PoseType.Doze:
                plugin.Configuration.DefaultPoses.Doze = index;
                plugin.lastSeenDozePose = index;
                plugin.suppressDozeSaveForFrames = 60;
                Plugin.Log.Debug($"[ApplyPose] Doze pose set to {index} and suppressed for 60 frames.");
                break;
        }

        // This makes the change persist!
        plugin.Configuration.Save();
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


    public byte GetPose(EmoteController.PoseType type)
        => PlayerState.Instance()->CurrentPose(type);

    public byte GetSelectedPose(EmoteController.PoseType type)
        => PlayerState.Instance()->SelectedPoses[(int)type];
}

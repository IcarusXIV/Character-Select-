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
        if (index >= 7 || clientState.LocalPlayer == null) return;

        // Update selected pose
        PlayerState.Instance()->SelectedPoses[(int)type] = index;

        // Get native character
        var localChar = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)clientState.LocalPlayer.Address;

        // Apply CPoseState directly, if in the correct mode
        if (TranslatePoseState(localChar->ModeParam) == type)
            localChar->EmoteController.CPoseState = index;

        // ✅ Save to config so it persists across login/zoning
        switch (type)
        {
            case EmoteController.PoseType.Idle:
                plugin.Configuration.DefaultPoses.Idle = index;
                break;
            case EmoteController.PoseType.Sit:
                plugin.Configuration.DefaultPoses.Sit = index;
                break;
            case EmoteController.PoseType.GroundSit:
                plugin.Configuration.DefaultPoses.GroundSit = index;
                break;
            case EmoteController.PoseType.Doze:
                plugin.Configuration.DefaultPoses.Doze = index;
                break;
        }

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

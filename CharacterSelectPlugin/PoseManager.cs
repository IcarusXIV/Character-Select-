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


    public PoseManager(IClientState clientState, IFramework framework, IChatGui chatGui, ICommandManager commandManager)
    {
        this.clientState = clientState;
        this.framework = framework;
        this.chatGui = chatGui;
        this.commandManager = commandManager;
    }

    public void ApplyPose(EmoteController.PoseType type, byte index)
    {
        if (index >= 7 || clientState.LocalPlayer == null) return;

        // Update selected pose
        PlayerState.Instance()->SelectedPoses[(int)type] = index;

        // Get native character
        var localChar = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)clientState.LocalPlayer.Address;

        // Apply CPoseState directly
        localChar->EmoteController.CPoseState = index;
    }

    public byte GetPose(EmoteController.PoseType type)
        => PlayerState.Instance()->CurrentPose(type);

    public byte GetSelectedPose(EmoteController.PoseType type)
        => PlayerState.Instance()->SelectedPoses[(int)type];
}

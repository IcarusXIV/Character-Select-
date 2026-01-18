using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CharacterSelectPlugin.Managers;

public class PoseManager
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

        framework.Update += OnFrameworkUpdate;
    }

    public void ApplyPose(EmoteController.PoseType type, byte index)
    {
        Plugin.Log.Debug($"[ApplyPose] Applying {type} pose {index}");

        if (index >= 7 || clientState.LocalPlayer == null)
            return;

        var characterAddress = clientState.LocalPlayer.Address;
        
        unsafe
        {
            var charPtr = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)characterAddress;

            // Apply to memory first
            PlayerState.Instance()->SelectedPoses[(int)type] = index;

            // Check pose state
            var currentState = TranslatePoseState(charPtr->ModeParam);
            if (currentState == type)
            {
                // We're in the correct state, now update the visual pose
                var currentPose = charPtr->EmoteController.CPoseState;
                
                // Command-based approach (default for sync compatibility)
                if (plugin.Configuration.UseCommandBasedPoses ?? true)
                {
                    // Use /cpose to cycle if not already at target
                    if (currentPose != index)
                    {
                        // Direct memory write for immediate effect
                        charPtr->EmoteController.CPoseState = index;
                        Plugin.Log.Debug($"[ApplyPose] Set CPoseState directly to {index} for immediate effect");
                        
                        // Command approach ensures sync plugins see the change
                        StartApplyPoseTask(type, index, characterAddress);
                    }
                    else
                    {
                        Plugin.Log.Debug($"[ApplyPose] Already at target pose {index}, no need to cycle");
                    }
                }
                else
                {
                    // Direct memory write (legacy)
                    charPtr->EmoteController.CPoseState = index;
                }
            }
            else
            {
                // Not in correct state, memory updated for later
                Plugin.Log.Debug($"[ApplyPose] Not in correct state for {type}, only updating memory");
            }
        }

        // Save to config
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

    private void StartApplyPoseTask(EmoteController.PoseType type, byte index, IntPtr characterAddress)
    {
        _ = Task.Run(async () => 
        {
            await ApplyPoseViaCommand(type, index, characterAddress);
        });
    }
    
    private async Task ApplyPoseViaCommand(EmoteController.PoseType type, byte targetIndex, IntPtr characterAddress)
    {
        // Brief delay for memory write to settle
        await Task.Delay(50);
        
        var maxAttempts = 8;
        var attempts = 0;
        
        // Cycle /cpose for network sync
        while (attempts < maxAttempts)
        {
            // Check state on framework thread
            var (currentPose, shouldContinue) = await framework.RunOnFrameworkThread(() =>
            {
                unsafe
                {
                    var charPtr = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)characterAddress;
                    var current = charPtr->EmoteController.CPoseState;
                    
                    if (current == targetIndex)
                    {
                        Plugin.Log.Debug($"[ApplyPoseViaCommand] Confirmed at target pose {targetIndex}");
                        return (current, false);
                    }
                    
                    Plugin.Log.Debug($"[ApplyPoseViaCommand] Executing /cpose to sync from {current} to {targetIndex}");
                    commandManager.ProcessCommand("/cpose");
                    
                    return (current, true);
                }
            });
            
            if (!shouldContinue)
                break;
            
            // Brief delay between cycles
            await Task.Delay(50);
            attempts++;
        }
        
        if (attempts >= maxAttempts)
        {
            Plugin.Log.Warning($"[ApplyPoseViaCommand] Could not sync pose to {targetIndex} after {maxAttempts} attempts");
        }
        else
        {
            Plugin.Log.Info($"[ApplyPoseViaCommand] Successfully synced pose to {targetIndex}");
        }
    }

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        if (!plugin.Configuration.EnablePoseAutoSave || !clientState.IsLoggedIn)
            return;
        if (clientState.LocalPlayer == null)
            return;

        var charPtr = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)clientState.LocalPlayer.Address;
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

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
    }
}

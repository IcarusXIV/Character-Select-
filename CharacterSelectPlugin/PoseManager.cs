using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CharacterSelectPlugin.Managers;

public unsafe class ImprovedPoseManager
{
    private readonly IClientState clientState;
    private readonly IFramework framework;
    private readonly Plugin plugin;

    private readonly PoseState currentState = new();
    private DateTime lastPoseChange = DateTime.MinValue;
    private const float POSE_CHANGE_COOLDOWN = 0.5f; // Prevent rapid changes
    private DateTime loginGraceStart = DateTime.MinValue;

    public ImprovedPoseManager(IClientState clientState, IFramework framework, Plugin plugin)
    {
        this.clientState = clientState;
        this.framework = framework;
        this.plugin = plugin;

        framework.Update += OnFrameworkUpdate;
        clientState.Login += OnLogin;
    }

    public bool ApplyPose(EmoteController.PoseType type, byte index)
    {
        if (!CanApplyPose(type, index))
            return false;

        try
        {
            var success = SetPoseInternal(type, index);
            if (success)
            {
                currentState.SetPluginControlled(type, index);
                lastPoseChange = DateTime.Now;
                SavePoseToConfig(type, index);
                Plugin.Log.Debug($"[PoseManager] Successfully applied {type} pose {index}");
            }
            return success;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[PoseManager] Failed to apply pose: {ex.Message}");
            return false;
        }
    }

    private bool CanApplyPose(EmoteController.PoseType type, byte index)
    {
        if (index >= 7)
        {
            Plugin.Log.Debug($"[PoseManager] Invalid pose index: {index}");
            return false;
        }

        if (clientState.LocalPlayer?.Address == IntPtr.Zero)
        {
            Plugin.Log.Debug("[PoseManager] Player not available");
            return false;
        }

        // Cooldown to prevent spam
        if ((DateTime.Now - lastPoseChange).TotalSeconds < POSE_CHANGE_COOLDOWN)
        {
            Plugin.Log.Debug("[PoseManager] Pose change on cooldown");
            return false;
        }

        return true;
    }

    private bool SetPoseInternal(EmoteController.PoseType type, byte index)
    {
        var playerState = PlayerState.Instance();
        if (playerState == null)
            return false;

        var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)clientState.LocalPlayer!.Address;
        if (character == null || character->GameObject.ObjectIndex == 0xFFFF)
            return false;

        playerState->SelectedPoses[(int)type] = index;

        // Only update CPoseState if we're currently in that pose mode
        var currentMode = TranslatePoseState(character->ModeParam);
        if (currentMode == type)
        {
            character->EmoteController.CPoseState = index;
        }

        return true;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!plugin.Configuration.EnablePoseAutoSave || !clientState.IsLoggedIn)
            return;

        try
        {
            UpdatePoseTracking();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[PoseManager] Framework update error: {ex.Message}");
        }
    }

    private void UpdatePoseTracking()
    {
        var playerState = PlayerState.Instance();
        if (playerState == null)
            return;

        foreach (EmoteController.PoseType type in Enum.GetValues<EmoteController.PoseType>())
        {
            var currentPose = playerState->SelectedPoses[(int)type];
            var lastKnown = currentState.GetLastKnown(type);

            // Detect user-initiated changes
            if (currentPose != lastKnown &&
                !currentState.IsPluginControlled(type) &&
                currentPose < 7)
            {
                // Skip saving pose resets to 0 within 5 seconds of login
                var secondsSinceLogin = (DateTime.Now - loginGraceStart).TotalSeconds;
                if (currentPose == 0 && secondsSinceLogin < 5)
                {
                    currentState.SetUserControlled(type, lastKnown); // Keep original pose in tracking
                    continue;
                }

                Plugin.Log.Debug($"[PoseManager] User changed {type} pose to {currentPose}");
                SavePoseToConfig(type, currentPose);
                currentState.SetUserControlled(type, currentPose);
            }
        }
    }

    private void SavePoseToConfig(EmoteController.PoseType type, byte index)
    {
        switch (type)
        {
            case EmoteController.PoseType.Idle:
                plugin.Configuration.DefaultPoses.Idle = index;
                plugin.Configuration.LastIdlePoseAppliedByPlugin = index;
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

    private void OnLogin()
    {
        loginGraceStart = DateTime.Now;
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        clientState.Login -= OnLogin;
    }
}

public class PoseState
{
    private readonly Dictionary<EmoteController.PoseType, byte> lastKnownPoses = new();
    private readonly Dictionary<EmoteController.PoseType, bool> pluginControlled = new();
    private readonly Dictionary<EmoteController.PoseType, DateTime> lastChangeTime = new();

    public void SetPluginControlled(EmoteController.PoseType type, byte pose)
    {
        lastKnownPoses[type] = pose;
        pluginControlled[type] = true;
        lastChangeTime[type] = DateTime.Now;

        var timer = new System.Timers.Timer(2000);
        timer.Elapsed += (sender, e) => {
            pluginControlled[type] = false;
            timer.Dispose();
        };
        timer.AutoReset = false;
        timer.Start();
    }

    public void SetUserControlled(EmoteController.PoseType type, byte pose)
    {
        lastKnownPoses[type] = pose;
        pluginControlled[type] = false;
        lastChangeTime[type] = DateTime.Now;
    }

    public byte GetLastKnown(EmoteController.PoseType type)
    {
        return lastKnownPoses.TryGetValue(type, out byte value) ? value : (byte)255;
    }

    public bool IsPluginControlled(EmoteController.PoseType type)
    {
        return pluginControlled.TryGetValue(type, out bool value) && value;
    }
}

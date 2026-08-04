using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using Rewired;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class DisableVerticalCameraInCockpitFix(ConfigFile config) : ConfigurableFix(config)
{
    protected override string Description =>
        "When enabled, prevents cockpit camera from moving vertically with vertical camera movement keys.";
    
    [HarmonyPatch(typeof(CameraCockpitState), nameof(CameraCockpitState.UpdateState))]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> CameraCockpitStateUpdateStateTranspiler(
        IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        
        var playerInputField = AccessTools.Field(typeof(GameManager), nameof(GameManager.playerInput));
        
        var getAxisMethod = AccessTools.Method(typeof(Player), nameof(Player.GetAxis),
            [typeof(string)]);
        
        var virtualJoystickEnabledField =
            AccessTools.Field(typeof(PlayerSettings), nameof(PlayerSettings.virtualJoystickEnabled));
        
        matcher.MatchForward(false,
            new CodeMatch(OpCodes.Ldsfld, playerInputField),
            new CodeMatch(OpCodes.Ldstr, "Move Vertical"),
            new CodeMatch(OpCodes.Callvirt, getAxisMethod)
        ).ThrowIfInvalid("Couldn't find Move Vertical pattern.");
        
        var blockStart = matcher.Pos;
        
        matcher.MatchForward(false,
            new CodeMatch(OpCodes.Ldsfld, virtualJoystickEnabledField)
        ).ThrowIfInvalid("Couldn't find start of block after Move Vertical pattern.");
        
        var blockEnd = matcher.Pos;
        
        matcher.RemoveInstructionsInRange(blockStart, blockEnd - 1);
        
        return matcher.InstructionEnumeration();
    }
}
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch(typeof(CameraCockpitState))]
// ReSharper disable once InconsistentNaming
internal class RequireFreelookWithoutVJ(ConfigFile config): ConfigurableFix(config)
{
    protected override bool DefaultEnabled => false;

    protected override string Description =>
        $"if true, {GetType().Name} is enabled\nEnables needing to hold down freelook button to activate freelook"
        + " even when Virtual Joystick is disabled (releasing freelook snaps back to center).";
    
    [HarmonyPatch(typeof(CameraCockpitState), nameof(CameraCockpitState.UpdateState))]
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> CameraCockpitState_RequireFreelookWithoutVJ(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        var cursorVisibleGetter = AccessTools.PropertyGetter(typeof(UnityEngine.Cursor),nameof(UnityEngine.Cursor.visible));
        var radialMenuIsInUseGetter = AccessTools.Method(typeof(RadialMenuMain), nameof(RadialMenuMain.IsInUse));
        
        matcher.MatchForward(
            true,
            new CodeMatch(OpCodes.Call, cursorVisibleGetter),
            new CodeMatch(ci => ci.opcode == OpCodes.Brtrue || ci.opcode == OpCodes.Brtrue_S),
            new CodeMatch(OpCodes.Call, radialMenuIsInUseGetter),
            new CodeMatch(ci => ci.opcode == OpCodes.Brtrue || ci.opcode == OpCodes.Brtrue_S)
            );

        if (!matcher.IsValid)
            return matcher.InstructionEnumeration();
        
        var skipOperand = (Label)matcher.Instruction.operand;
        var playerInput = AccessTools.Field(typeof(GameManager), nameof(GameManager.playerInput));
        var getButton = AccessTools.Method(typeof(Rewired.Player), nameof(Rewired.Player.GetButton), new[] { typeof(string) });

        matcher.Advance(1);
        matcher.InsertAndAdvance(new CodeInstruction(OpCodes.Ldsfld, playerInput));
        matcher.InsertAndAdvance(new CodeInstruction(OpCodes.Ldstr, "Free Look"));
        matcher.InsertAndAdvance(new CodeInstruction(OpCodes.Callvirt, getButton));
        matcher.InsertAndAdvance(new CodeInstruction(OpCodes.Brfalse_S, skipOperand));

        return matcher.InstructionEnumeration();
    }
    
    [HarmonyPatch(typeof(CameraCockpitState), nameof(CameraCockpitState.UpdateState))]
    [HarmonyPostfix]
    public static void ResetFreelook(CameraCockpitState __instance)
    {
        if (!__instance.pilot.dead && !PlayerSettings.virtualJoystickEnabled && !GameManager.playerInput.GetButton("Free Look"))
        {
            __instance.panView = 0.0f;
            __instance.tiltView = 0.0f;
        }
    }
}
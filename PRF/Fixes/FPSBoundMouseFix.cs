using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;


namespace PRF;

[Fix]
[HarmonyPatch]
internal class FPSBoundMouseFix(ConfigFile config) : ConfigurableFix(config)
{
  [HarmonyPatch(typeof(CameraCockpitState), nameof(CameraCockpitState.UpdateState))]
  [HarmonyTranspiler]
  internal static IEnumerable<CodeInstruction> FPSBoundFix(IEnumerable<CodeInstruction> instructions)
  {
    var matcher = new CodeMatcher(instructions);
    
    var viewSensitivityField = AccessTools.Field(typeof(PlayerSettings), nameof(PlayerSettings.viewSensitivity));
    var unscaledDeltaTimeGetter = AccessTools.PropertyGetter(typeof(Time), nameof(Time.unscaledDeltaTime));

    while (true)
    {
      matcher.MatchForward(
        false,
        new CodeMatch(OpCodes.Ldc_R4, 120f),
        new CodeMatch(OpCodes.Mul),
        new CodeMatch(OpCodes.Ldsfld, viewSensitivityField),
        new CodeMatch(OpCodes.Mul),
        new CodeMatch(OpCodes.Call, unscaledDeltaTimeGetter),
        new CodeMatch(OpCodes.Mul)
      );

      if (!matcher.IsValid)
        break;
      
      PRF.Logger.LogDebug($"Found match at position {matcher.Pos}");
      PRF.Logger.LogDebug(matcher.Instruction.ToString());
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4, 1f)
      );
      matcher.Advance(3);
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4, 1f)
      );
    }

    return matcher.InstructionEnumeration();
  }
}
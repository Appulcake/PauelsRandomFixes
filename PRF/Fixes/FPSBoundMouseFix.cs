using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;


namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class FPSBoundMouseFix(ConfigFile config) : ConfigurableFix(config)
{
  private static void LogMatching(int pos, string matchField, string instruction)
  {
    PRF.Logger.LogDebug($"Found match for {matchField} at position {pos}");
    PRF.Logger.LogDebug(instruction);
  }
  
  // Cockpit freelook (with VJ on + Freelook button, and with regular Freelook)
  [HarmonyPatch(typeof(CameraCockpitState), nameof(CameraCockpitState.UpdateState))]
  [HarmonyTranspiler]
  internal static IEnumerable<CodeInstruction> Cockpit_FPSBoundFix(IEnumerable<CodeInstruction> instructions)
  {
    var matcher = new CodeMatcher(instructions);
    
    var viewSensitivityField = AccessTools.Field(typeof(PlayerSettings), nameof(PlayerSettings.viewSensitivity));
    var unscaledDeltaTimeGetter = AccessTools.PropertyGetter(typeof(Time), nameof(Time.unscaledDeltaTime));

    while (true)
    {
      matcher.MatchForward(
        false,
        new CodeMatch(OpCodes.Ldc_R4),
        new CodeMatch(OpCodes.Mul),
        new CodeMatch(OpCodes.Ldsfld, viewSensitivityField),
        new CodeMatch(OpCodes.Mul),
        new CodeMatch(OpCodes.Call, unscaledDeltaTimeGetter)
      );

      if (!matcher.IsValid)
        break;

      LogMatching(matcher.Pos,"CameraCockpitState", matcher.Instruction.ToString());
      
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4, 1f) // 120 => 1
      );
      matcher.Advance(3);
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4, 1f) // unscaledDeltaTime => 1
      );
    }

    return matcher.InstructionEnumeration();
  }

  
  // Orbit 3rd person camera pan/tilt and zoom
  [HarmonyPatch(typeof(CameraOrbitState), nameof(CameraOrbitState.Inputs))]
  [HarmonyTranspiler]
  internal static IEnumerable<CodeInstruction> OrbitCamera_FPSBoundFix(IEnumerable<CodeInstruction> instructions)
  {
    var matcher = new CodeMatcher(instructions);
    
    var viewSensitivityField = AccessTools.Field(typeof(PlayerSettings), nameof(PlayerSettings.viewSensitivity));
    var unscaledDeltaTimeGetter = AccessTools.PropertyGetter(typeof(Time), nameof(Time.unscaledDeltaTime));
    
    while (true)
    {
      matcher.MatchForward(
        false,
        new CodeMatch(OpCodes.Ldc_R4),
        new CodeMatch(OpCodes.Mul),
        new CodeMatch(OpCodes.Ldsfld, viewSensitivityField),
        new CodeMatch(OpCodes.Mul),
        new CodeMatch(OpCodes.Call, unscaledDeltaTimeGetter)
      );
      
      if (!matcher.IsValid)
        break;
      
      LogMatching(matcher.Pos,"CameraOrbitState - Pan and Tilt", matcher.Instruction.ToString());

      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4, 1f) // 90 => 1
      );
      matcher.Advance(3);
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4, 1f) // unscaledDeltaTime => 1
      );
    }

    matcher = new CodeMatcher(matcher.InstructionEnumeration());

    while (true)
    {
      matcher.MatchForward(
        false,
        new CodeMatch(OpCodes.Ldstr, "Zoom View"),
        new CodeMatch(OpCodes.Callvirt),
        new CodeMatch(OpCodes.Ldc_R4),
        new CodeMatch(OpCodes.Mul),
        new CodeMatch(OpCodes.Call, unscaledDeltaTimeGetter)
        );

      if (!matcher.IsValid)
        break;
      
      LogMatching(matcher.Pos,"CameraOrbitState - Zoom", matcher.Instruction.ToString());

      matcher.Advance(2);
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4, 1f) // 60 => 1
        );
      matcher.Advance(1);
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4, 1f) // unscaledDeltaTime => 1
      );
    }

    return matcher.InstructionEnumeration();
  }
  
  
  // TV / Cinema camera
  [HarmonyPatch(typeof(CameraTVState), nameof(CameraTVState.UpdateState))]
  [HarmonyTranspiler]
  internal static IEnumerable<CodeInstruction> TVCamera_FPSBoundFix(IEnumerable<CodeInstruction> instructions)
  {
    var matcher = new CodeMatcher(instructions);
    
    var unscaledDeltaTimeGetter = AccessTools.PropertyGetter(typeof(Time), nameof(Time.unscaledDeltaTime));
    
    while (true)
    {
      matcher.MatchForward(
        false,
        new CodeMatch(OpCodes.Ldc_R4),
        new CodeMatch(OpCodes.Mul),
        new CodeMatch(OpCodes.Call, unscaledDeltaTimeGetter)
      );
      
      if (!matcher.IsValid)
        break;
      
      LogMatching(matcher.Pos,"CameraTVState", matcher.Instruction.ToString());
      
      // Is still multiplied somewhere and becomes way too fast without
      // the additional deltaTime reduction, additional reduction to slow it down.
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4, 0.02f) // 3 => 1 then * 0.02
      );
      matcher.Advance(1);
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4, 1f) // unscaledDeltaTime => 1
      );
    }

    return matcher.InstructionEnumeration();
  }
  
  
  // Loadout selection camera when selecting an airfield, to spin your plane around
  [HarmonyPatch(typeof(CameraSelectionState), nameof(CameraSelectionState.MoveCamera))]
  [HarmonyTranspiler]
  internal static IEnumerable<CodeInstruction> CameraSelectionState_FPSBoundFix(IEnumerable<CodeInstruction> instructions)
  {
    var matcher = new CodeMatcher(instructions);

    var orbitalAngleGetter = AccessTools.Field(typeof(CameraSelectionState), nameof(CameraSelectionState.orbitalAngle));
    
    while (true)
    {
      matcher.MatchForward(
        false,
        new CodeMatch(OpCodes.Ldfld, orbitalAngleGetter),
        new CodeMatch(OpCodes.Ldloc_0),
        new CodeMatch(OpCodes.Ldc_R4),
        new CodeMatch(OpCodes.Mul),
        new CodeMatch(OpCodes.Ldarg_2)
      );
      
      if (!matcher.IsValid)
        break;
      
      LogMatching(matcher.Pos,"CameraSelectionState - Rotate", matcher.Instruction.ToString());

      matcher.Advance(2);
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4, -3f) // -150 * 0.02 => -3
      );
      matcher.Advance(1);
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4, 1f) // deltaTime => 1
      );
    }
    
    matcher = new CodeMatcher(matcher.InstructionEnumeration());

    var cameraHeightGetter = AccessTools.Field(typeof(CameraSelectionState), nameof(CameraSelectionState.cameraHeight));

    while (true)
    {
      matcher.MatchForward(
        false,
        new CodeMatch(OpCodes.Ldfld, cameraHeightGetter),
        new CodeMatch(OpCodes.Ldloc_1),
        new CodeMatch(OpCodes.Ldc_R4),
        new CodeMatch(OpCodes.Mul),
        new CodeMatch(OpCodes.Ldarg_2)
      );
      
      if  (!matcher.IsValid)
        break;
      
      LogMatching(matcher.Pos,"CameraSelectionState - Height", matcher.Instruction.ToString());

      matcher.Advance(2);
      matcher.SetInstructionAndAdvance(
          new CodeInstruction(OpCodes.Ldc_R4, -0.01f) // -0.5 * 0.02 => -0.01
        );
      matcher.Advance(1);
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4, 1f) // deltaTime => 1
      );
    }

    return matcher.InstructionEnumeration();
  }
  
  
  // Encyclopedia camera
  [HarmonyPatch(typeof(CameraEncyclopediaState), nameof(CameraEncyclopediaState.MoveCamera))]
  [HarmonyTranspiler]
  internal static IEnumerable<CodeInstruction> CameraEncyclopediaState_FPSBoundFix(IEnumerable<CodeInstruction> instructions)
  {
    var matcher = new CodeMatcher(instructions);

    var cameraAngleGetter = AccessTools.Field(typeof(CameraEncyclopediaState), nameof(CameraEncyclopediaState.cameraAngle));
    var deltaTimeGetter = AccessTools.PropertyGetter(typeof(Time), nameof(Time.deltaTime));
    
    while (true)
    {
      matcher.MatchForward(
        false,
        new CodeMatch(OpCodes.Ldfld, cameraAngleGetter),
        new CodeMatch(OpCodes.Ldloc_0),
        new CodeMatch(OpCodes.Ldc_R4),
        new CodeMatch(OpCodes.Mul),
        new CodeMatch(OpCodes.Call, deltaTimeGetter)
      );
      
      if (!matcher.IsValid)
        break;
      
      LogMatching(matcher.Pos,"CameraEncyclopediaState - Rotate", matcher.Instruction.ToString());

      matcher.Advance(2);
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4,  2f) // 100 * 0.02 => 2
      );
      matcher.Advance(1);
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4, 1f) // deltaTime => 1
      );
    }
    
    matcher = new CodeMatcher(matcher.InstructionEnumeration());

    var cameraHeightGetter = AccessTools.Field(typeof(CameraEncyclopediaState), nameof(CameraEncyclopediaState.cameraHeight));

    while (true)
    {
      matcher.MatchForward(
        false,
        new CodeMatch(OpCodes.Ldfld, cameraHeightGetter),
        new CodeMatch(OpCodes.Ldloc_1),
        new CodeMatch(OpCodes.Ldc_R4),
        new CodeMatch(OpCodes.Mul),
        new CodeMatch(OpCodes.Call, deltaTimeGetter)
      );
      
      if  (!matcher.IsValid)
        break;
      
      LogMatching(matcher.Pos,"CameraEncyclopediaState - Height", matcher.Instruction.ToString());

      matcher.Advance(2);
      matcher.SetInstructionAndAdvance(
          new CodeInstruction(OpCodes.Ldc_R4, -0.02f) // -3 => -1 then 0.02
        );
      matcher.Advance(1);
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4, 1f) // deltaTime => 1
      );
    }

    return matcher.InstructionEnumeration();
  }

  
  // Map panning
  [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.MapControls))]
  [HarmonyTranspiler]
  internal static IEnumerable<CodeInstruction> DynamicMap_FPSBoundFix(IEnumerable<CodeInstruction> instructions)
  {
    var matcher = new CodeMatcher(instructions);

    var unscaledDeltaTimeGetter = AccessTools.PropertyGetter(typeof(Time), nameof(Time.unscaledDeltaTime));
    
    while (true)
    {
      matcher.MatchForward(
        false,
        new CodeMatch(OpCodes.Ldc_R4),
        new CodeMatch(OpCodes.Call, unscaledDeltaTimeGetter),
        new CodeMatch(OpCodes.Ldc_R4)
      );
      
      if (!matcher.IsValid)
        break;
      
      LogMatching(matcher.Pos,"Map panning", matcher.Instruction.ToString());
      
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4,  5f) // 150 => 5
      );
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4, 1f) // unscaledDeltaTime => 1
      );
    }

    return matcher.InstructionEnumeration();
  }
  
  
  /*
  // Virtual Joystick
  [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.PlayerAxisControls))]
  [HarmonyTranspiler]
  internal static IEnumerable<CodeInstruction> VirtualJoystick_FPSBoundFix(IEnumerable<CodeInstruction> instructions)
  {
    var matcher = new CodeMatcher(instructions);

    var unscaledDeltaTimeGetter = AccessTools.PropertyGetter(typeof(Time), nameof(Time.unscaledDeltaTime));
    
    while (true)
    {
      matcher.MatchForward(
        false,
        new CodeMatch(OpCodes.Call, unscaledDeltaTimeGetter)
      );
      
      if (!matcher.IsValid)
        break;
      
      LogMatching(matcher.Pos,"Virtual Joystick", matcher.Instruction.ToString());
      
      //Replacing Mathf.Min(Time.unscaledDeltaTime, 0.1f) with Mathf.Min(1, 1) for simplicity
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4,  1f) // unscaledDeltaTime => 1
      );
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4, 1f) // 0.1 => 1
      );
      matcher.Advance(2);
      matcher.SetInstructionAndAdvance(
        new CodeInstruction(OpCodes.Ldc_R4, 1f) // 30 => 1
      );
    }

    return matcher.InstructionEnumeration();
  }

  [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.PlayerAxisControls))]
  [HarmonyPostfix]
  internal static void PlayerAxisLogger(ref PilotPlayerState __instance)
  {
    if (PlayerSettings.virtualJoystickEnabled)
    {
      if (!__instance.player.GetButton("Free Look") && GameManager.playerInput.GetAxis("Pan View") != 0)
      {
        PRF.Logger.LogInfo("PANVIEWLOG: " + GameManager.playerInput.GetAxis("Pan View"));
      }
    }
  }
  */

  private static int _lastProcessedFrame = -1;
  
  [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.PlayerAxisControls))]
  [HarmonyPrefix]
  public static bool TempVJReplace(ref PilotPlayerState __instance)
  {
    //PRF.Logger.LogInfo($"frame={Time.frameCount} axis={GameManager.playerInput.GetAxis("Pan View")}");
    
    if (__instance.pilot.aircraft.cockpit.IsDetached())
      return false;
    float num = PlayerSettings.virtualJoystickInvertPitch ? -1f : 1f;
    if ((!PlayerSettings.virtualJoystickEnabled ? 0 : (DynamicMap.mapMaximized ? 1 : (RadialMenuMain.IsInUse() ? 1 : 0))) != 0)
    {
      __instance.controlInputs.pitch = Mathf.Clamp(__instance.pitchInput, -1f, 1f);
      __instance.controlInputs.roll = Mathf.Clamp(__instance.rollInput, -1f, 1f);
      __instance.controlInputs.yaw = Mathf.Clamp(__instance.yawInput, -1f, 1f);
    }
    else if ((double) __instance.pilotStrength < 0.2)
    {
      __instance.controlInputs.pitch = 0.0f;
      __instance.controlInputs.roll = 0.0f;
      __instance.controlInputs.yaw = 0.0f;
    }
    else
    {
      __instance.pitchInput = 0.0f;
      __instance.rollInput = 0.0f;
      __instance.yawInput = 0.0f;
      
      // Don't duplicate events per update frame
      if (PlayerSettings.virtualJoystickEnabled && _lastProcessedFrame != Time.frameCount)
      {
        if (!SceneSingleton<FlightHud>.i.virtualJoystickPos.gameObject.activeSelf)
          SceneSingleton<FlightHud>.i.virtualJoystickPos.gameObject.SetActive(true);
        if (!__instance.player.GetButton("Free Look"))
        {
          Vector3 a = SceneSingleton<FlightHud>.i.virtualJoystickPos.transform.localPosition;
          
          if (CameraStateManager.cameraMode == CameraMode.cockpit)
            // deltaTime removed here?
            a = Vector3.ClampMagnitude(
              a + 
              (float) ((double) PlayerSettings.virtualJoystickSensitivity)
              * new Vector3(GameManager.playerInput.GetAxis("Pan View"),
                -num * GameManager.playerInput.GetAxis("Tilt View"), 0.0f), 
              150f
            );
          
          Vector3 joystickPos = Vector3.Lerp(a, Vector3.zero, PlayerSettings.virtualJoystickCentering * 2f * Time.deltaTime);
          
          // Extra logging
          if (GameManager.playerInput.GetAxis("Pan View") != 0)
          {
            //PRF.Logger.LogInfo("a vector: " + a);
            //PRF.Logger.LogInfo("joystick pos: " + joystickPos);
          }
          
          SceneSingleton<FlightHud>.i.SetVirtualJoystick(joystickPos);
        }
        if (!DynamicMap.mapMaximized && !RadialMenuMain.IsInUse() && !Leaderboard.IsOpen())
        {
          __instance.pitchInput = (float) (-(double) SceneSingleton<FlightHud>.i.virtualJoystickPos.transform.localPosition.y / 150.0);
          __instance.rollInput = SceneSingleton<FlightHud>.i.virtualJoystickPos.transform.localPosition.x / 150f;
          if ((double) __instance.pilot.aircraft.radarAlt < (double) __instance.pilot.aircraft.definition.spawnOffset.y + 1.0)
            __instance.yawInput = SceneSingleton<FlightHud>.i.virtualJoystickPos.transform.localPosition.x / 150f;
        }
        
        _lastProcessedFrame = Time.frameCount;
        PRF.Logger.LogInfo($"frame={Time.frameCount} axis={GameManager.playerInput.GetAxis("Pan View")}");
      }
      else if (SceneSingleton<FlightHud>.i.virtualJoystickPos.gameObject.activeSelf)
        SceneSingleton<FlightHud>.i.virtualJoystickPos.gameObject.SetActive(false);
      __instance.pitchInput += __instance.player.GetAxis("Pitch");
      __instance.rollInput += __instance.player.GetAxis("Roll");
      __instance.yawInput += __instance.player.GetAxis("Yaw");
      __instance.controlInputs.pitch = Mathf.Clamp(__instance.pitchInput, -1f, 1f);
      __instance.controlInputs.roll = Mathf.Clamp(__instance.rollInput, -1f, 1f);
      __instance.controlInputs.yaw = Mathf.Clamp(__instance.yawInput, -1f, 1f);
      if (!__instance.pilot.aircraft.IsAutoHoverEnabled())
        return false;
      __instance.PlayerThrottleAxis1Controls();
    }

    return false;
  }
}
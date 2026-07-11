using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal class FPSBoundMouseFix : ConfigurableFix
{
    private static ConfigEntry<bool> _enableCenteringDuringFreelook = null!;
    private static ConfigEntry<float> _cockpitFreelookSensitivity = null!;
    private static ConfigEntry<float> _mapPanSensitivity = null!;
    private static ConfigEntry<float> _orbitCamSensitivity = null!;
    private static ConfigEntry<float> _orbitZoomSensitivity = null!;
    private static ConfigEntry<float> _TVCamSensitivity = null!;
    private static ConfigEntry<float> _virtualJoystickCenteringForce = null!;
    private static ConfigEntry<float> _virtualJoystickSensitivityX = null!;
    private static ConfigEntry<float> _virtualJoystickSensitivityY = null!;
    
    private static Vector3 _globalJoystickPos; // Bridge vector to transfer info from UpdateState to FixedUpdateState
    
    public FPSBoundMouseFix(ConfigFile config) : base(config)
    {
        _enableCenteringDuringFreelook = config.Bind(GetType().Name + " - Misc", "Enable Centering VJ During Freelook",
            false,
            "Enable centering force to act on Virtual Joystick while freelook is active (instead of freezing last input)");
        
        _cockpitFreelookSensitivity = config.Bind(GetType().Name + " - Sensitivity", "Cockpit Freelook Sensitivity", 1f,
            "Cockpit freelook sensitivity");
        _mapPanSensitivity = config.Bind(GetType().Name + " - Sensitivity", "Map Panning Sensitivity", 1f,
            "Map panning sensitivity");
        _orbitCamSensitivity = config.Bind(GetType().Name + " - Sensitivity", "Orbit Cam Sensitivity", 1f,
            "Orbit cam sensitivity");
        _orbitZoomSensitivity = config.Bind(GetType().Name + " - Sensitivity", "Orbit Cam Zoom Sensitivity", 1f,
            "Orbit cam zoom sensitivity");
        _TVCamSensitivity = config.Bind(GetType().Name + " - Sensitivity", "TV (Flyby) Cam Sensitivity", 1f,
            "TV (Flyby) cam sensitivity");
        _virtualJoystickCenteringForce = config.Bind(GetType().Name + " - Sensitivity",
            "Virtual Joystick Centering Sensitivity", 1f,
            "Virtual joystick centering force sensitivity - stacks with vanilla setting, here to give extra control");
        _virtualJoystickSensitivityX = config.Bind(GetType().Name + " - Sensitivity", "Virtual Joystick X-Sensitivity",
            1f,
            "Virtual joystick X-sensitivity - stacks with vanilla setting, here to give extra control");
        _virtualJoystickSensitivityY = config.Bind(GetType().Name + " - Sensitivity", "Virtual Joystick Y-Sensitivity",
            1f,
            "Virtual joystick Y-sensitivity - stacks with vanilla setting, here to give extra control");
    }
    
    protected override bool DefaultEnabled => false;
    
    protected override string Description =>
        $"{base.Description}\nFixes mouse virtual joystick and freelook sensitivities being dependent"
        + " on FPS. Since the game uses GetAxis for both mouse and controller axes, with this enabled behaviour will"
        + " be flipped for freelook with controllers, and their sensitivity will be FPS dependent.";
    
    public static float GetCockpitFreelookSensitivity() => _cockpitFreelookSensitivity.Value * 0.5f;
    
    public static float GetMapPanSensitivity() => _mapPanSensitivity.Value * 25f;
    
    public static float GetOrbitCamSensitivity() => _orbitCamSensitivity.Value * 0.5f;
    
    public static float GetOrbitZoomSensitivity() => _orbitZoomSensitivity.Value;
    
    public static float GetTVCamSensitivity() => _TVCamSensitivity.Value * 0.5f;
    
    private static float GetVirtualJoystickCenteringForce() => _virtualJoystickCenteringForce.Value * 4f;
    
    private static float GetVirtualJoystickSensitivityX() => _virtualJoystickSensitivityX.Value * 0.5f;
    
    private static float GetVirtualJoystickSensitivityY() => _virtualJoystickSensitivityY.Value * 0.5f;
    
    // Cockpit freelook (with VJ on + Freelook button, and with regular Freelook)
    [HarmonyPatch(typeof(CameraCockpitState), nameof(CameraCockpitState.UpdateState))]
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Cockpit_FPSBoundFix(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        
        while (true)
        {
            matcher.MatchForward(
                false,
                new CodeMatch(OpCodes.Ldc_R4),
                new CodeMatch(OpCodes.Mul),
                new CodeMatch(OpCodes.Ldsfld, ReusedRefs.GetViewSensitivity),
                new CodeMatch(OpCodes.Mul),
                new CodeMatch(OpCodes.Call, ReusedRefs.GetUnscaledDeltaTime)
            );
            
            if (!matcher.IsValid)
                break;
            
            matcher.SetInstructionAndAdvance(
                new CodeInstruction(OpCodes.Ldc_R4, 1f)
            ); // 120f => 1f
            matcher.Advance(3);
            matcher.SetInstructionAndAdvance(
                new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(typeof(FPSBoundMouseFix), nameof(GetCockpitFreelookSensitivity)))
            ); // unscaledDeltaTime => _cockpitFreelookSensitivity
        }
        
        return matcher.InstructionEnumeration();
    }
    
    
    // Map panning
    [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.MapControls))]
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> DynamicMap_FPSBoundFix(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        
        while (true)
        {
            matcher.MatchForward(
                false,
                new CodeMatch(OpCodes.Ldc_R4),
                new CodeMatch(OpCodes.Call, ReusedRefs.GetUnscaledDeltaTime),
                new CodeMatch(OpCodes.Ldc_R4)
            );
            
            if (!matcher.IsValid)
                break;
            
            matcher.SetInstructionAndAdvance(
                new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(typeof(FPSBoundMouseFix), nameof(GetMapPanSensitivity)))
            ); // 150 => GetMapPanSensitivity which is _mapPanSensitivity * 25 so 25f by default
            matcher.SetInstructionAndAdvance(
                new CodeInstruction(OpCodes.Ldc_R4, 1f)
            ); // unscaledDeltaTime => 1f, this is in a Mathf.Min function
        }
        
        return matcher.InstructionEnumeration();
    }
    
    
    // Orbit 3rd person camera pan/tilt and zoom
    [HarmonyPatch(typeof(CameraOrbitState), nameof(CameraOrbitState.Inputs))]
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> OrbitCamera_FPSBoundFix(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        
        while (true)
        {
            matcher.MatchForward(
                false,
                new CodeMatch(OpCodes.Ldc_R4),
                new CodeMatch(OpCodes.Mul),
                new CodeMatch(OpCodes.Ldsfld, ReusedRefs.GetViewSensitivity),
                new CodeMatch(OpCodes.Mul),
                new CodeMatch(OpCodes.Call, ReusedRefs.GetUnscaledDeltaTime)
            );
            
            if (!matcher.IsValid)
                break;
            
            matcher.SetInstructionAndAdvance(
                new CodeInstruction(OpCodes.Ldc_R4, 1f)
            ); // 90 => 1
            matcher.Advance(3);
            matcher.SetInstructionAndAdvance(
                new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(typeof(FPSBoundMouseFix), nameof(GetOrbitCamSensitivity)))
            ); // unscaledDeltaTime => _orbitCamSensitivity
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
                new CodeMatch(OpCodes.Call, ReusedRefs.GetUnscaledDeltaTime)
            );
            
            if (!matcher.IsValid)
                break;
            
            matcher.Advance(2);
            matcher.SetInstructionAndAdvance(
                new CodeInstruction(OpCodes.Ldc_R4, 1f)
            ); // 60 => 1
            matcher.Advance(1);
            matcher.SetInstructionAndAdvance(
                new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(typeof(FPSBoundMouseFix), nameof(GetOrbitZoomSensitivity)))
            ); // unscaledDeltaTime => _orbitZoomSensitivity
        }
        
        return matcher.InstructionEnumeration();
    }
    
    
    // TV / Cinema camera
    [HarmonyPatch(typeof(CameraTVState), nameof(CameraTVState.UpdateState))]
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> TVCamera_FPSBoundFix(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        
        while (true)
        {
            matcher.MatchForward(
                false,
                new CodeMatch(OpCodes.Ldc_R4),
                new CodeMatch(OpCodes.Mul),
                new CodeMatch(OpCodes.Call, ReusedRefs.GetUnscaledDeltaTime)
            );
            
            if (!matcher.IsValid)
                break;
            
            // Is still multiplied somewhere and becomes way too fast without
            // the additional deltaTime reduction, additional reduction to slow it down.
            matcher.SetInstructionAndAdvance(
                new CodeInstruction(OpCodes.Ldc_R4, 0.02f)
            ); // 3 => 1 then * 0.02
            matcher.Advance(1);
            matcher.SetInstructionAndAdvance(
                new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(typeof(FPSBoundMouseFix), nameof(GetTVCamSensitivity)))
            ); // unscaledDeltaTime => _TVCamSensitivity
        }
        
        return matcher.InstructionEnumeration();
    }
    
    
    // Loadout selection camera when selecting an airfield, to spin your plane around
    [HarmonyPatch(typeof(CameraSelectionState), nameof(CameraSelectionState.MoveCamera))]
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> CameraSelectionState_FPSBoundFix(
        IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        
        var orbitalAngleGetter =
            AccessTools.Field(typeof(CameraSelectionState), nameof(CameraSelectionState.orbitalAngle));
        
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
        
        var cameraHeightGetter =
            AccessTools.Field(typeof(CameraSelectionState), nameof(CameraSelectionState.cameraHeight));
        
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
            
            if (!matcher.IsValid)
                break;
            
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
    internal static IEnumerable<CodeInstruction> CameraEncyclopediaState_FPSBoundFix(
        IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        
        var cameraAngleGetter =
            AccessTools.Field(typeof(CameraEncyclopediaState), nameof(CameraEncyclopediaState.cameraAngle));
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
            
            matcher.Advance(2);
            matcher.SetInstructionAndAdvance(
                new CodeInstruction(OpCodes.Ldc_R4, 2f) // 100 * 0.02 => 2
            );
            matcher.Advance(1);
            matcher.SetInstructionAndAdvance(
                new CodeInstruction(OpCodes.Ldc_R4, 1f) // deltaTime => 1
            );
        }
        
        matcher = new CodeMatcher(matcher.InstructionEnumeration());
        
        var cameraHeightGetter =
            AccessTools.Field(typeof(CameraEncyclopediaState), nameof(CameraEncyclopediaState.cameraHeight));
        
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
            
            if (!matcher.IsValid)
                break;
            
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
    
    // Moved function setting virtual joystick's joystickPos value to UpdateState to not double up deltaTime from running it in FixedUpdateState
    [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.UpdateState))]
    [HarmonyPostfix]
    public static void UpdateStateAddition(Pilot pilot, ref PilotPlayerState __instance)
    {
        if (PlayerSettings.virtualJoystickEnabled && !__instance.player.GetButton("Free Look"))
        {
            var num = PlayerSettings.virtualJoystickInvertPitch ? -1f : 1f;
            var a = SceneSingleton<FlightHud>.i.virtualJoystickPos.transform.localPosition;
            
            // Extra _virtualJoystickSensitivityX and Y multipliers, individually applied to each GetAxis, instead of deltaTime and flat 30f
            // Stacks with vanilla virtualJoystickSensitivity setting and gives more control to support wider ranges of sensitivity
            // And enables sensitivity control per axis
            
            if (CameraStateManager.cameraMode == CameraMode.cockpit)
            {
                var pan = GameManager.playerInput.GetAxis("Pan View") * GetVirtualJoystickSensitivityX();
                var tilt = -num * GameManager.playerInput.GetAxis("Tilt View") * GetVirtualJoystickSensitivityY();
                
                a = Vector3.ClampMagnitude(_globalJoystickPos + (float)(double)PlayerSettings.virtualJoystickSensitivity
                    * new Vector3(pan, tilt, 0.0f), 150f);
            }
            
            // this _globalJoystickPos gets used in PlayerAxisControls ran in FixedUpdateState to for SetVirtualJoystick
            // The static 2f virtualJoystickCentering multiplier is replaced by GetVirtualJoystickCenteringForce which is _virtualJoystickCenteringForce * 4
            
            _globalJoystickPos = Vector3.Lerp(a, Vector3.zero,
                PlayerSettings.virtualJoystickCentering * GetVirtualJoystickCenteringForce() * Time.deltaTime);
        }
        else if (_enableCenteringDuringFreelook.Value ||
                 !PlayerSettings
                     .virtualJoystickEnabled) // Account for VJ being turned off via hotkey / LockedMapControlsWithVJFix
        {
            _globalJoystickPos =
                Vector3.zero; // No interpolation to zero to emulate instant turn off when toggling VJ, otherwise this falls behind toggling VJ setting virtualJoystickPos to zero
        }
    }
    
    
    // PlayerAxisControls no longer sets its joystickPos and instead gets that data via _globalJoystickPos from UpdateState
    [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.PlayerAxisControls))]
    [HarmonyPrefix]
    public static bool PlayerAxisControlsReplace(ref PilotPlayerState __instance)
    {
        if (__instance.pilot.aircraft.cockpit.IsDetached())
            return false;
        // Moved to UpdateState:
        // float num = PlayerSettings.virtualJoystickInvertPitch ? -1f : 1f;
        if (PlayerSettings.virtualJoystickEnabled && (DynamicMap.mapMaximized || RadialMenuMain.IsInUse()))
        {
            __instance.controlInputs.pitch = Mathf.Clamp(__instance.pitchInput, -1f, 1f);
            __instance.controlInputs.roll = Mathf.Clamp(__instance.rollInput, -1f, 1f);
            __instance.controlInputs.yaw = Mathf.Clamp(__instance.yawInput, -1f, 1f);
        }
        else if (__instance.pilotStrength < 0.2)
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
            if (PlayerSettings.virtualJoystickEnabled)
            {
                if (!SceneSingleton<FlightHud>.i.virtualJoystickPos.gameObject.activeSelf)
                    SceneSingleton<FlightHud>.i.virtualJoystickPos.gameObject.SetActive(true);
                if (!__instance.player.GetButton("Free Look"))
                    
                    // Moved to UpdateState, original code:
                    /*
                    Vector3 a = SceneSingleton<FlightHud>.i.virtualJoystickPos.transform.localPosition;
                    if (CameraStateManager.cameraMode == CameraMode.cockpit)
                    {
                      a = Vector3.ClampMagnitude(a + (float) ((double) PlayerSettings.virtualJoystickSensitivity * (double) Mathf.Min(Time.unscaledDeltaTime, 0.1f) * 30.0) * new Vector3(GameManager.playerInput.GetAxis("Pan View"), -num * GameManager.playerInput.GetAxis("Tilt View"), 0.0f), 150f);
                    }
                    Vector3 joystickPos = Vector3.Lerp(a, Vector3.zero, PlayerSettings.virtualJoystickCentering * 2f * Time.deltaTime);
                    */
                    // Getting _globalJoystickPos from UpdateState instead of joystickPos from this FixedUpdateState
                    // (which'd add another layer of deltaTime based on physics FPS)
                
                    SceneSingleton<FlightHud>.i.SetVirtualJoystick(_globalJoystickPos);
                else if
                    (_enableCenteringDuringFreelook
                     .Value) // Enable centering to continue happening during freelook with config enabled
                    SceneSingleton<FlightHud>.i.SetVirtualJoystick(_globalJoystickPos);
                if (!DynamicMap.mapMaximized && !RadialMenuMain.IsInUse() && !Leaderboard.IsOpen())
                {
                    __instance.pitchInput =
                        (float)(-(double)SceneSingleton<FlightHud>.i.virtualJoystickPos.transform.localPosition.y /
                                150.0);
                    __instance.rollInput =
                        SceneSingleton<FlightHud>.i.virtualJoystickPos.transform.localPosition.x / 150f;
                    if (__instance.pilot.aircraft.radarAlt < __instance.pilot.aircraft.definition.spawnOffset.y + 1.0)
                        __instance.yawInput = SceneSingleton<FlightHud>.i.virtualJoystickPos.transform.localPosition.x /
                                              150f;
                }
            }
            else if (SceneSingleton<FlightHud>.i.virtualJoystickPos.gameObject.activeSelf)
            {
                SceneSingleton<FlightHud>.i.virtualJoystickPos.gameObject.SetActive(false);
            }
            
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
    
    private static class ReusedRefs
    {
        public static readonly FieldInfo GetViewSensitivity =
            AccessTools.Field(typeof(PlayerSettings), nameof(PlayerSettings.viewSensitivity));
        
        public static readonly MethodInfo GetUnscaledDeltaTime =
            AccessTools.PropertyGetter(typeof(Time), nameof(Time.unscaledDeltaTime));
    }
}
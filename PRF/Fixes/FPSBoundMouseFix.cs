#if CLIENT
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.UI;
using Rewired;
using UnityEngine;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class FPSBoundMouseFix : ConfigurableFix
{
    private static ConfigEntry<bool> _enableCenteringDuringFreelook = null!;
    private static ConfigEntry<float> _cockpitFreelookSensitivity = null!;
    private static ConfigEntry<float> _mapPanSensitivity = null!;
    private static ConfigEntry<float> _orbitCamSensitivity = null!;
    private static ConfigEntry<float> _orbitZoomSensitivity = null!;
    
    // ReSharper disable once InconsistentNaming
    private static ConfigEntry<float> _TVCamSensitivity = null!;
    private static ConfigEntry<float> _virtualJoystickCenteringForce = null!;
    private static ConfigEntry<float> _virtualJoystickSensitivityX = null!;
    private static ConfigEntry<float> _virtualJoystickSensitivityY = null!;
    
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
    
    protected override string Description =>
        $"{base.Description}\nFixes mouse virtual joystick and freelook sensitivities being dependent"
        + " on FPS. Does not affect controller/absolute input methods.";
    
    private static float GetCockpitFreelookSensitivity() => _cockpitFreelookSensitivity.Value * 0.5f;
    public static float GetMapPanSensitivity() => _mapPanSensitivity.Value * 41.6665f; // Aimed to calibrate so 1.0 config = 1:1 map:mouse pixel movement
    public static float GetOrbitCamSensitivity() => _orbitCamSensitivity.Value * 0.5f;
    public static float GetOrbitZoomSensitivity() => _orbitZoomSensitivity.Value;
    public static float GetTVCamSensitivity() => _TVCamSensitivity.Value * 0.01f;
    private static float GetVirtualJoystickCenteringForce() => _virtualJoystickCenteringForce.Value * 4f;
    private static float GetVirtualJoystickSensitivityX() => _virtualJoystickSensitivityX.Value * 0.5f;
    private static float GetVirtualJoystickSensitivityY() => _virtualJoystickSensitivityY.Value * 0.5f;
    
    private static bool _skipNextVjUpdate;
    
    // Map panning
    [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.MapControls))]
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> DynamicMap_FPSBoundFix(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        var patched = 0;
        
        while (true)
        {
            // 1 block of:
            // ldc.r4 (absolute multiplier, e.g. 150f)
            // call for unscaledDeltaTime
            // mul
            // This is so that the other use of unscaledDeltaTime call is not found as that's not for mouse map panning
            // Technically could patch that one too, but it's out of scope for this, as usual map panning would be with
            // this method that's done when holding LMB + using Pan/Tilt View from mouse
            // Instead of directly custom binding "Move Map Horizontal/Vertical" to mouse
            
            matcher.MatchForward(
                false,
                new CodeMatch(OpCodes.Ldc_R4),
                new CodeMatch(OpCodes.Call, ReusedRefs.GetUnscaledDeltaTime),
                new CodeMatch(OpCodes.Ldc_R4)
            );
            
            if (!matcher.IsValid)
                break;
            
            // Old functionality can remain here, map movement can be done via "Move Map Horizontal/Vertical", which is
            // not bound to mouse by default and would be very unorthodox to do so (it'd make map move on any mouse movement)
            // So we can leave this as is, where it's treated as absolute input only, getting its deltaTime multiplication
            // And then the second method is in the Input.GetMouseButton(0) block, which is only active while holding LMB,
            // similarly here it'd be highly unorthodox to rebind gamepad bind for this, so we can just treat this as relative
            // only and retain the simple transpiler removing the excess deltaTime multiplication
            //
            // So this stays at the fixed calibrated 25 * Min(1, 0.03), while that could be further replaced to be
            // 25 * 0.03 or even 0.75 at default calibration and be adjusted by config, this keeps it less invasive and
            // easier to see where it's coming from in original code
            
            // 150 => GetMapPanSensitivity which is _mapPanSensitivity * 25 so 25f by default
            // Update during rewrite: Found that 20.875f is a more accurate 1:1 calibration as default for 1.0 config
            matcher.SetAndAdvance(OpCodes.Call, ReusedRefs.MapPanSensitivity);
            // unscaledDeltaTime => 1f, this is in a Mathf.Min function
            matcher.SetAndAdvance(OpCodes.Ldc_R4, 1f);
            
            patched++;
        }
        
        if (patched != 1)
            PRF.Logger.LogWarning($"FPSBoundMouseFix: Expected 1 map control axis match, patched {patched}.");
        
        return matcher.InstructionEnumeration();
    }
    // End of Map panning
    
    
    // Cockpit freelook (with VJ on + Freelook button, and with regular Freelook)
    [HarmonyPatch(typeof(CameraCockpitState), nameof(CameraCockpitState.UpdateState))]
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Cockpit_FPSBoundFix(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        var patched = 0;
        
        while (true)
        {
            // 4 different blocks of:
            // ldc.r4 (absolute multiplier, e.g. 120f)
            // mul
            // ldsfld for PlayerSettings.viewSensitivity
            // mul
            // call for unscaledDeltaTime
            
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
            
            // Copy absolute multiplier, usually 120f here
            
            var absoluteMult = matcher.Instruction.operand;
            
            // Then roll back and expand the stack from Rewired.Player + "Action Name" to follow with:
            // Absolute mult + Custom freelook sens config + GetAxisRelative call
            // then keep existing PlayerSettings.viewSensitivity + mul
            // and finally replace * unscaledDeltaTime with * 1f as it's not necessary
            // For a resulting call of e.g.:
            // GetAxisRelative(GameManager.playerInput, "Pan View", 120f, GetCockpitFreelookSensitivity())
            
            matcher.Advance(-1);
            matcher.SetAndAdvance(OpCodes.Ldc_R4, absoluteMult);
            matcher.SetAndAdvance(OpCodes.Call, ReusedRefs.CockpitFreelookSensitivity);
            matcher.SetAndAdvance(OpCodes.Call, ReusedRefs.AxisRelative);
            matcher.Advance(2);
            matcher.SetAndAdvance(OpCodes.Ldc_R4, 1f);
            
            patched++;
        }
        
        if (patched != 4)
            PRF.Logger.LogWarning($"FPSBoundMouseFix: Expected 4 cockpit freelook axis matches, patched {patched}.");
        
        return matcher.InstructionEnumeration();
    }
    // End of Cockpit freelook
    
    
    // Orbit 3rd person camera pan/tilt and zoom
    [HarmonyPatch(typeof(CameraOrbitState), nameof(CameraOrbitState.Inputs))]
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> OrbitCamera_FPSBoundFix(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        
        // Find Pan View and Tilt View axis getters
        // Then expand them to use AxisRelative
        PatchOrbitAxisGetter(matcher, "Pan View", OpCodes.Stloc_2);
        PatchOrbitAxisGetter(matcher, "Tilt View", OpCodes.Stloc_3);
        
        // Find this.panView or this.tiltView blocks, one has axis to load reference ldloc.2, other ldloc.3
        // Then remove their absolute mult and deltaTime mult as those are already accounted for via AxisRelative
        OrbitViewSetterCleanup(matcher, OpCodes.Ldloc_2);
        OrbitViewSetterCleanup(matcher, OpCodes.Ldloc_3);
        
        // Find Zoom View axis getter, then expand it and use AxisRelative
        PatchOrbitZoom(matcher);
        
        return matcher.InstructionEnumeration();
    }
    
    private static void PatchOrbitAxisGetter(CodeMatcher matcher, string actionName, OpCode storeAxisOpcode)
    {
        matcher.Start().MatchForward(
            false,
            new CodeMatch(OpCodes.Ldstr, actionName),
            new CodeMatch(OpCodes.Callvirt, ReusedRefs.GetAxis),
            new CodeMatch(storeAxisOpcode)
        );
        
        if (!matcher.IsValid)
            throw new InvalidOperationException($"Could not find orbit camera {actionName} axis.");
        
        matcher.Advance(1);
        matcher.SetAndAdvance(OpCodes.Ldc_R4, 90f);
        
        matcher.InsertAndAdvance(
            new CodeInstruction(OpCodes.Call, ReusedRefs.OrbitCamSensitivity),
            new CodeInstruction(OpCodes.Call, ReusedRefs.AxisRelative)
        );
    }
    
    private static void OrbitViewSetterCleanup(CodeMatcher matcher, OpCode loadAxisOpcode)
    {
        matcher.Start().MatchForward(
            false,
            new CodeMatch(loadAxisOpcode),
            new CodeMatch(OpCodes.Mul),
            new CodeMatch(OpCodes.Ldc_R4, 90f),
            new CodeMatch(OpCodes.Mul),
            new CodeMatch(OpCodes.Ldsfld, ReusedRefs.GetViewSensitivity),
            new CodeMatch(OpCodes.Mul),
            new CodeMatch(OpCodes.Call, ReusedRefs.GetUnscaledDeltaTime),
            new CodeMatch(OpCodes.Mul)
        );
        
        if (!matcher.IsValid)
            throw new InvalidOperationException("Could not find orbit camera View setter.");
        
        matcher.Advance(2);
        matcher.SetAndAdvance(OpCodes.Ldc_R4, 1f);
        
        matcher.Advance(3);
        matcher.Set(OpCodes.Ldc_R4, 1f);
    }
    
    private static void PatchOrbitZoom(CodeMatcher matcher)
    {
        matcher.Start().MatchForward(
            false,
            new CodeMatch(OpCodes.Ldstr, "Zoom View"),
            new CodeMatch(OpCodes.Callvirt, ReusedRefs.GetAxis),
            new CodeMatch(OpCodes.Ldc_R4, 60f),
            new CodeMatch(OpCodes.Mul),
            new CodeMatch(OpCodes.Call, ReusedRefs.GetUnscaledDeltaTime),
            new CodeMatch(OpCodes.Mul)
        );
        
        if (!matcher.IsValid)
            throw new InvalidOperationException("Could not find orbit camera Zoom View axis getter.");
        
        matcher.Advance(1);
        
        // Expand arguments and call AxisRelative instead of GetAxis
        matcher.SetAndAdvance(OpCodes.Ldc_R4, 60f);
        matcher.SetAndAdvance(OpCodes.Call, ReusedRefs.OrbitZoomSensitivity);
        matcher.SetAndAdvance(OpCodes.Call, ReusedRefs.AxisRelative);
        
        // Disable deltaTime by setting it to 1f
        matcher.Set(OpCodes.Ldc_R4, 1f);
    }
    // End of Orbit 3rd person camera pan/tilt and zoom
    
    
    // TV / Cinema camera
    [HarmonyPatch(typeof(CameraTVState), nameof(CameraTVState.UpdateState))]
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> TVCamera_FPSBoundFix(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        var patched = 0;
        
        while (true)
        {
            matcher.MatchForward(
                false,
                new CodeMatch(OpCodes.Callvirt, ReusedRefs.GetAxis),
                new CodeMatch(OpCodes.Ldc_R4),
                new CodeMatch(OpCodes.Mul),
                new CodeMatch(OpCodes.Call, ReusedRefs.GetUnscaledDeltaTime)
            );
            
            if (!matcher.IsValid)
                break;
            
            var absoluteMult = matcher.InstructionAt(1).operand;
            
            matcher.SetAndAdvance(OpCodes.Ldc_R4, absoluteMult);
            matcher.SetAndAdvance(OpCodes.Call, ReusedRefs.TVCamSensitivity);
            matcher.SetAndAdvance(OpCodes.Call, ReusedRefs.AxisRelative);
            matcher.Set(OpCodes.Ldc_R4, 1f);
            
            patched++;
        }
        
        if (patched != 2)
            PRF.Logger.LogWarning($"FPSBoundMouseFix: Expected 2 TV camera axis matches, patched {patched}.");
        
        return matcher.InstructionEnumeration();
    }
    // End of TV / Cinema camera
    
    
    // Loadout selection camera when selecting an airfield, to spin your plane around
    [HarmonyPatch(typeof(CameraSelectionState), nameof(CameraSelectionState.MoveCamera))]
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> CameraSelectionState_FPSBoundFix(
        IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        
        LoadoutCameraPatch(matcher, OpCodes.Ldloc_0, ReusedRefs.OrbitalAngleGetter, "Pan View");
        LoadoutCameraPatch(matcher, OpCodes.Ldloc_1, ReusedRefs.OrbitalCameraHeightGetter, "Tilt View");
        
        return matcher.InstructionEnumeration();
    }
    
    private static void LoadoutCameraPatch(CodeMatcher matcher, OpCode axis, FieldInfo selectionField, string action)
    {
        // Loadout selection camera is more complicated as the axis values are retrieved, but then several oprations
        // are done on them, changing it here to use GetAxisRelative would introduce the absolute and deltaTime multiplier
        // at a much earlier stage, causing these extra operations to have a different effect
        // So instead, it's all kept the same, except find where orbitalAngle and cameraHeight are set,
        // and conditionally replace deltaTime with a multiplier fitting old behaviour when relative (mouse) input is detected
        // This keeps absolute (controller) input identical
        
        matcher.Start().MatchForward(
            false,
            new CodeMatch(OpCodes.Ldfld, selectionField),
            new CodeMatch(axis),
            new CodeMatch(OpCodes.Ldc_R4),
            new CodeMatch(OpCodes.Mul),
            new CodeMatch(OpCodes.Ldarg_2),
            new CodeMatch(OpCodes.Mul)
        );
        
        if (!matcher.IsValid)
        {
            PRF.Logger.LogWarning($"FPSBoundMouseFix: Could not find loadout camera {action}.");
        }
        else
        {
            matcher.Advance(4);
            matcher.InsertAndAdvance(new CodeInstruction(OpCodes.Ldstr, action));
            matcher.Advance(1);
            matcher.InsertAndAdvance(new CodeInstruction(OpCodes.Call, ReusedRefs.AxisTimeMultiplier));
        }
    }
    // End of Loadout selection camera at airfield
    
    
    // Encyclopedia camera
    [HarmonyPatch(typeof(CameraEncyclopediaState), nameof(CameraEncyclopediaState.MoveCamera))]
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> CameraEncyclopediaState_FPSBoundFix(
        IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        
        EncyclopediaCameraPatch(matcher, OpCodes.Ldloc_0, ReusedRefs.EncyclopediaAngleGetter, "Pan View");
        EncyclopediaCameraPatch(matcher, OpCodes.Ldloc_1, ReusedRefs.EncyclopediaCameraHeightGetter, "Tilt View");
        
        return matcher.InstructionEnumeration();
    }
    
    private static void EncyclopediaCameraPatch(CodeMatcher matcher, OpCode axis, FieldInfo selectionField,
        string action)
    {
        // Very similar principle to LoadoutCameraPatch, just slightly different matching structure
        // They both happened to be calibrated to 0.02 multiplier for relative (mouse) input, so they can reuse the same
        // GetAxisTimeMultiplier as return multiplier instead of deltaTime on relative input
        
        matcher.Start().MatchForward(
            false,
            new CodeMatch(OpCodes.Ldfld, selectionField),
            new CodeMatch(axis),
            new CodeMatch(OpCodes.Ldc_R4),
            new CodeMatch(OpCodes.Mul),
            new CodeMatch(OpCodes.Call, ReusedRefs.GetDeltaTime)
        );
        
        if (!matcher.IsValid)
        {
            PRF.Logger.LogWarning($"FPSBoundMouseFix: Could not find encyclopedia camera {action}.");
        }
        else
        {
            matcher.Advance(4);
            matcher.InsertAndAdvance(new CodeInstruction(OpCodes.Ldstr, action));
            matcher.Advance(1);
            matcher.InsertAndAdvance(new CodeInstruction(OpCodes.Call, ReusedRefs.AxisTimeMultiplier));
        }
    }
    // End of Encyclopedia camera
    
    
    // Virtual Joystick adjustments - VJ posiiton being set in UpdateState() instead of FixedUpdateState()
    [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.UpdateState))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void UpdateVirtualJoystick(Pilot pilot, PilotPlayerState __instance)
    {
        // Same conditions to start touching VJ as PlayerAxisControls()
        if (pilot.aircraft == null || pilot.aircraft.cockpit.IsDetached() || __instance.pilotStrength < 0.2f ||
            !PlayerSettings.virtualJoystickEnabled || DynamicMap.mapMaximized || RadialMenuMain.IsInUse() || LeaderboardMenu.IsOpen())
            return;
        
        // Skip first frame handling VJ after escape menu is closed to prevent race condition applying a wildly offset
        // mouse position of where cursor happens to be on menu close affecting VJ
        if (_skipNextVjUpdate)
        {
            _skipNextVjUpdate = false;
            return;
        }
        
        var freeLook = __instance.player.GetButton("Free Look");
        
        if (freeLook && !_enableCenteringDuringFreelook.Value)
            return;
        
        // Recreation of PlayerAxisControls() setting VJ, via our own GetAxisRelativeClamped as a way to get axis input
        
        var hud = SceneSingleton<FlightHud>.i;
        var joystickPos = hud.virtualJoystickPos.transform.localPosition;
        
        if (!freeLook && CameraStateManager.cameraMode == CameraMode.cockpit)
        {
            var invertPitch = PlayerSettings.virtualJoystickInvertPitch ? -1f : 1f;
            
            var pan = GetAxisRelativeClamped(__instance.player, "Pan View", 30f,
                GetVirtualJoystickSensitivityX(), 0.1f);
            var tilt = GetAxisRelativeClamped(__instance.player, "Tilt View", 30f,
                GetVirtualJoystickSensitivityY(), 0.1f);
            
            joystickPos += PlayerSettings.virtualJoystickSensitivity * new Vector3(pan, -invertPitch * tilt, 0f);
            joystickPos = Vector3.ClampMagnitude(joystickPos, 150f);
        }
        
        joystickPos = CenterVirtualJoystick(joystickPos);
        
        hud.SetVirtualJoystick(joystickPos);
    }
    
    // Prevent PlayerAxisControls (ran in FixedUpdateState) from setting VJ, it only reads it
    [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.PlayerAxisControls))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> DisableFixedUpdateVjSetter(IEnumerable<CodeInstruction> instructions)
    {
        // This finds the section in PlayerAxisControls (ran from FixedUpdate) that's:
        // if (!this.player.GetButton("Free Look"))
        // And replaces the check via a custom method that always returns true, turning it into
        // if (!true)
        // as a way to skip that block, bypassing this part from setting VJ, as it's only meant to be read here
        
        var matcher = new CodeMatcher(instructions);
        
        matcher.MatchForward(
            true,
            new CodeMatch(OpCodes.Ldstr, "Free Look"),
            new CodeMatch(OpCodes.Callvirt, ReusedRefs.ButtonGetter)
        );
        
        if (!matcher.IsValid)
        {
            PRF.Logger.LogError("FPSBoundMouseFix: Could not find VJ Free Look condition.");
            return matcher.InstructionEnumeration();
        }
        
        matcher.Set(OpCodes.Call, ReusedRefs.SkipVanillaVjUpdateMethod);
        
        return matcher.InstructionEnumeration();
    }
    
    // ReSharper disable once UnusedParameter.Local
    private static bool SkipVanillaVjUpdate(Player player,
        // ReSharper disable once UnusedParameter.Local
        string actionName) => true;
    
    private static Vector3 CenterVirtualJoystick(Vector3 joystickPos)
    {
        // Custom exponential VJ recentering that's more accurate across various FPS than vanilla's
        // Especially noticeable if you have a steep frametime spike/lag while it's recentering
        
        var centeringRate = PlayerSettings.virtualJoystickCentering * GetVirtualJoystickCenteringForce();
        
        if (centeringRate <= 0f)
            return joystickPos;
        
        var t = 1f - Mathf.Exp(-centeringRate * Time.deltaTime);
        
#pragma warning disable Harmony003
        joystickPos = Vector3.Lerp(joystickPos, Vector3.zero, t);
        
        if (joystickPos.sqrMagnitude < 0.000001f)
            joystickPos = Vector3.zero;
#pragma warning restore Harmony003
        
        return joystickPos;
    }
    
    // Guard to make sure closing escape menu keeps VJ centered
    [HarmonyPatch(typeof(LeaderboardMenu), nameof(LeaderboardMenu.Close))]
    [HarmonyPostfix]
    private static void ResetVjAfterMenuClose()
    {
        if (!PlayerSettings.virtualJoystickEnabled || SceneSingleton<FlightHud>.i == null || !SceneSingleton<FlightHud>.i.virtualJoystickPos.gameObject.activeSelf)
            return;
        
        SceneSingleton<FlightHud>.i.SetVirtualJoystick(Vector3.zero);
        _skipNextVjUpdate = true;
    }
    // End of Virtual Joystick adjustments
    
    
    private static float GetAxisTimeMultiplier(string actionName, float absoluteDeltaTime) =>
        GameManager.playerInput.GetAxisCoordinateMode(actionName) == AxisCoordinateMode.Absolute
            ? absoluteDeltaTime
            : 0.02f;
    
    private static float GetAxisRelative(Player player, string actionName, float absoluteToRelMult,
        float relativeToRelMult)
    {
        var value = player.GetAxis(actionName);
        
        if (player.GetAxisCoordinateMode(actionName) == AxisCoordinateMode.Absolute)
            return value * Time.unscaledDeltaTime * absoluteToRelMult;
        
        return value * relativeToRelMult;
    }
    
    private static float GetAxisRelativeClamped(Player player, string actionName, float absoluteToRelMult,
        float relativeToRelMult, float maxAbsoluteDeltaTime)
    {
        var value = player.GetAxis(actionName);
        
        if (player.GetAxisCoordinateMode(actionName) == AxisCoordinateMode.Absolute)
            return value * Mathf.Min(Time.unscaledDeltaTime, maxAbsoluteDeltaTime) * absoluteToRelMult;
        
        return value * relativeToRelMult;
    }
    
    private static class ReusedRefs
    {
        internal static readonly FieldInfo GetViewSensitivity =
            AccessTools.Field(typeof(PlayerSettings), nameof(PlayerSettings.viewSensitivity));
        
        internal static readonly MethodInfo GetUnscaledDeltaTime =
            AccessTools.PropertyGetter(typeof(Time), nameof(Time.unscaledDeltaTime));
        
        internal static readonly MethodInfo GetDeltaTime =
            AccessTools.PropertyGetter(typeof(Time), nameof(Time.deltaTime));
        
        internal static readonly MethodInfo GetAxis =
            AccessTools.Method(typeof(Player), nameof(Player.GetAxis), [typeof(string)]);
        
        internal static readonly FieldInfo OrbitalAngleGetter
            = AccessTools.Field(typeof(CameraSelectionState), nameof(CameraSelectionState.orbitalAngle));
        
        internal static readonly FieldInfo OrbitalCameraHeightGetter
            = AccessTools.Field(typeof(CameraSelectionState), nameof(CameraSelectionState.cameraHeight));
        
        internal static readonly FieldInfo EncyclopediaAngleGetter
            = AccessTools.Field(typeof(CameraEncyclopediaState), nameof(CameraEncyclopediaState.cameraAngle));
        
        internal static readonly FieldInfo EncyclopediaCameraHeightGetter
            = AccessTools.Field(typeof(CameraEncyclopediaState), nameof(CameraEncyclopediaState.cameraHeight));
        
        public static readonly MethodInfo ButtonGetter =
            AccessTools.Method(typeof(Player), nameof(Player.GetButton), [typeof(string)]);
        
        internal static MethodInfo AxisRelative { get; } =
            AccessTools.Method(
                typeof(FPSBoundMouseFix), nameof(GetAxisRelative),
                [typeof(Player), typeof(string), typeof(float), typeof(float)]);
        
        internal static MethodInfo MapPanSensitivity { get; }
            = AccessTools.Method(typeof(FPSBoundMouseFix), nameof(GetMapPanSensitivity));
        
        internal static MethodInfo CockpitFreelookSensitivity { get; }
            = AccessTools.Method(typeof(FPSBoundMouseFix), nameof(GetCockpitFreelookSensitivity));
        
        internal static MethodInfo OrbitCamSensitivity { get; }
            = AccessTools.Method(typeof(FPSBoundMouseFix), nameof(GetOrbitCamSensitivity));
        
        internal static MethodInfo OrbitZoomSensitivity { get; }
            = AccessTools.Method(typeof(FPSBoundMouseFix), nameof(GetOrbitZoomSensitivity));
        
        internal static MethodInfo TVCamSensitivity { get; }
            = AccessTools.Method(typeof(FPSBoundMouseFix), nameof(GetTVCamSensitivity));
        
        public static MethodInfo SkipVanillaVjUpdateMethod { get; } =
            AccessTools.Method(typeof(FPSBoundMouseFix), nameof(SkipVanillaVjUpdate));
        
        internal static MethodInfo AxisTimeMultiplier { get; } =
            AccessTools.Method(typeof(FPSBoundMouseFix), nameof(GetAxisTimeMultiplier),
                [typeof(string), typeof(float)]);
    }
}
#endif
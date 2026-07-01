using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class AbsoluteZoom : ConfigurableFix
{
    private static ConfigEntry<float> _cockpitSensitivity = null!;
    private static ConfigEntry<float> _orbitSensitivity = null!;
    private static ConfigEntry<float> _tvSensitivity = null!;
    private static ConfigEntry<bool> _useAbsolute = null!;
    private static ConfigEntry<bool> _useNegative = null!;

    public AbsoluteZoom(ConfigFile config) : base(config)
    {
        _useAbsolute = config.Bind(GetType().Name, "Use Absolute zoom", true,
            "Use absolute zoom. If this is unchecked, this will use the relative implementation.");

        _useNegative = config.Bind(GetType().Name, "Use Negative region", true,
            "Whether or not to use the negative region of the axis for the absolute bind. Note that the relative mode REQUIRES your device to have a negative portion.");
        _cockpitSensitivity = config.Bind(GetType().Name, "Relative Cockpit zoom sensitivity", 5f,
            new ConfigDescription(
                "Sensitivity of the default RELATIVE zoom in cockpit view. Only works if this is set to relative mode.",
                new AcceptableValueRange<float>(0f, 30f)));

        _orbitSensitivity = config.Bind(GetType().Name, "Relative Orbit (cam 2) zoom sensitivity", 1f,
            new ConfigDescription(
                "Sensitivity of the default RELATIVE zoom in orbit view. Only works if this is set to relative mode.",
                new AcceptableValueRange<float>(0f, 30f)));

        _tvSensitivity = config.Bind(GetType().Name, "Relative Flyby (cam 3) zoom sensitivity", 1f,
            new ConfigDescription(
                "Sensitivity of the default RELATIVE zoom in flyby view. Only works if this is set to relative mode.",
                new AcceptableValueRange<float>(0f, 30f)));
    }

    public static float GetCockpitSensitivity()
    {
        return _cockpitSensitivity.Value;
    }

    public static float GetOrbitSensitivity()
    {
        return _orbitSensitivity.Value;
    }

    public static float GetTVSensitivity()
    {
        return _tvSensitivity.Value;
    }

    [HarmonyPatch(typeof(CameraCockpitState), nameof(CameraCockpitState.UpdateState))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    public static void CockpitCamStateUpdatePostfix(CameraCockpitState __instance, ref CameraStateManager cam)
    {
        if (!_useAbsolute.Value || __instance.pilot.dead) return;
        var input = GameManager.playerInput.GetAxis("Zoom View");
        if (_useNegative.Value) input = (input + 1) / 2;
        cam.mainCamera.fieldOfView = Mathf.Lerp(__instance.minFOV, __instance.maxFOV, input);
        cam.cockpitCamRender.fieldOfView = cam.mainCamera.fieldOfView;
    }


    [HarmonyPatch(typeof(CameraCockpitState), nameof(CameraCockpitState.UpdateState))]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> CockpitFovAdjFix(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions)
            .MatchForward(
                false,
                new CodeMatch(
                    OpCodes.Ldfld,
                    AccessTools.Field(typeof(CameraCockpitState), nameof(CameraCockpitState.FOVAdjustment))
                ),
                new CodeMatch(OpCodes.Ldc_R4, 5f)
            );

        if (matcher.IsValid)
        {
            PRF.Logger.LogDebug("Found cockpit fov adjustment constant.");
            matcher.Advance(1);


            matcher.SetInstruction(
                new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(typeof(AbsoluteZoom), nameof(GetCockpitSensitivity))
                )
            );
        }

        return matcher.InstructionEnumeration();
    }
    
    [HarmonyPatch(typeof(CameraOrbitState), nameof(CameraOrbitState.Inputs))]
    [HarmonyPostfix]
    public static void AbsoluteZoomInOrbit(CameraOrbitState __instance, ref CameraStateManager cam)
    {
        PRF.Logger.LogDebug($"{__instance.viewDistAdjust}:{__instance.zoomSpeed}");
        if (!_useAbsolute.Value) return;
        var input = GameManager.playerInput.GetAxis("Zoom View");
        if (_useNegative.Value) input = (input + 1) / 2;
        __instance.viewDistAdjust = Mathf.Lerp(0.0f, 10.0f, input);
    }

    [HarmonyPatch(typeof(CameraOrbitState), nameof(CameraOrbitState.Inputs))]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> OrbitDistFix(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions)
            .MatchForward(
                false,
                new CodeMatch(
                    OpCodes.Ldstr,
                    "Zoom View"
                ),
                new CodeMatch(ci => ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt)
            );
    
        if (matcher.IsValid)
        {
            PRF.Logger.LogDebug("Orbit zoom view thing found");
            matcher.Advance(2);
            matcher.InsertAndAdvance(
                new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(typeof(AbsoluteZoom), nameof(GetOrbitSensitivity))
                )
            );
            matcher.Insert(new CodeInstruction(OpCodes.Mul));
        }
    
        return matcher.InstructionEnumeration();
    }

    [HarmonyPatch(typeof(CameraTVState), nameof(CameraTVState.UpdateState))]
    [HarmonyPostfix]
    public static void AbsoluteZoomTV(CameraTVState __instance, ref CameraStateManager cam)
    {
        if (!_useAbsolute.Value) return;
        var input = GameManager.playerInput.GetAxis("Zoom View");
        if (_useNegative.Value) input = (input + 1) / 2;
        cam.mainCamera.fieldOfView = Mathf.Lerp(5.0f, 80.0f, input); // This is hardcoded in the game code smh...
    }
    
    [HarmonyPatch(typeof(CameraTVState), nameof(CameraTVState.UpdateState))]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> TVFovFix(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions)
            .MatchForward(
                false,
                new CodeMatch(
                    OpCodes.Ldstr,
                    "Zoom View"
                ),
                new CodeMatch(ci => ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt)
            );

        if (matcher.IsValid)
        {
            PRF.Logger.LogDebug("TV zoom view thing found");
            matcher.Advance(2);
            matcher.InsertAndAdvance(
                new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(typeof(AbsoluteZoom), nameof(GetTVSensitivity))
                    )
                );
            matcher.Insert(new CodeInstruction(OpCodes.Mul));
        }

        return matcher.InstructionEnumeration();
    }
}
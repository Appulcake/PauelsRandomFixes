#if CLIENT
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
// ReSharper disable InconsistentNaming

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class TargetCamZoomFix : ConfigurableFix
{
    private static ConfigEntry<float> _zoomInSpeedMultiplier = null!;
    
    public TargetCamZoomFix(ConfigFile config) : base(config)
    {
        _zoomInSpeedMultiplier = config.Bind(GetType().Name, "Zoom In Speed Multiplier", 1f,
            "Target camera zoom in speed multiplier, 1.0 is vanilla speed, 0 disables automatic zoom in.");
    }
    
    protected override string Description =>
        $"{base.Description}\nPrevents the target camera from zooming out while the map is maximised, " +
        "and allows adjusting its zoom in speed.";
    
    [HarmonyPatch(typeof(TargetCam), nameof(TargetCam.Update))]
    [HarmonyPrefix]
    internal static void TargetCamUpdatePrefix(TargetCam __instance, out float __state)
    {
        __state = __instance.cam != null ? __instance.cam.fieldOfView : 0f;
        
        // SetTargetCam() seems to stop being ran when map is open, it has a 3 second camTimeout where then AimCamera()
        // starts zooming out and freeze/disable camera after a while
        // This keeps it refreshed while there's targets (and camera is not in landing cam mode)
        
        if (DynamicMap.mapMaximized && __instance.currentMode != TargetCam.CamMode.landingMode && __instance.targetList?.Count > 0)
            __instance.camTimeout = 3f;
    }
    
    [HarmonyPatch(typeof(TargetCam), nameof(TargetCam.Update))]
    [HarmonyPostfix]
    internal static void TargetCamUpdatePostfix(TargetCam __instance, float __state)
    {
        var speed = _zoomInSpeedMultiplier.Value;
        if (Mathf.Approximately(speed, 1f) || float.IsNaN(speed) || float.IsInfinity(speed))
            return;
        
        var cam = __instance.cam;
        if (cam == null || !cam.enabled || __instance.currentMode == TargetCam.CamMode.landingMode || __instance.camTimeout < 2.5f)
            return;
        
        // Update() already ran Mathf.Lerp(this.cam.fieldOfView, this.targetFOV, Time.deltaTime) on FOV
        // Undo that specific change/zooming out here
        var vanillaT = Mathf.Clamp01(Time.deltaTime);
        var adjustedT = Mathf.Clamp01(Time.deltaTime * Mathf.Max(0f, speed));
        cam.fieldOfView += (adjustedT - vanillaT) * (__instance.targetFOV - __state);
    }
}
#endif
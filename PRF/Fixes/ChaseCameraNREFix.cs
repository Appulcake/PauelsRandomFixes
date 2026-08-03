using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch(typeof(CameraChaseState))]
internal class ChaseCameraNreFix(ConfigFile config) : ConfigurableFix(config)
{
    protected override string Description =>
        $"{base.Description}\nFixes an NRE causing UI to break when aircraft is destroyed while in Chase Camera.";
    
    [HarmonyPatch(nameof(CameraChaseState.LeaveState))]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    private static bool LeaveStatePrefix(CameraStateManager cam, CameraChaseState __instance)
    {
        cam.cameraPivot.SetParent(null);
        
        ((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).shadowDistance = 2000f;
        
        var followingUnit = cam.followingUnit;
        
        if (followingUnit == null)
            return false;
        
        if (followingUnit is Aircraft aircraft)
            aircraft.cockpit.onParentDetached -= __instance.CameraChaseState_OnCockpitDetach;
        
        // Vanilla 0.34 no longer checks if followingUnit is null for some reason when setting this, causing NRE
        // Issue is LeaveState is called double (one from SetFollowingUnit(null) then another from currentState.LeaveState)
        // By the second time, there's no followingUnit and thus NRE happens, currentState = state cannot be set, leading
        // to indefinite NRE spam in LateUpdate's currentState.UpdateState(this) on a null unit at CheckInput
        
        followingUnit.SetDoppler(true);
        
        return false;
    }
}
#if CLIENT
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class LaserVisualHitFix(ConfigFile config) : ConfigurableFix(config)
{
    protected override string Description =>
        $"{base.Description}\nFixes an issue with laser beams visually targeting the wrong target/position.";
    
    // This is a 0.34 regression in Laser.cs, where LateUpdate() now sets beamTransform via hitTransform that
    // FixedUpdate() sets, but only when it's not null, and FixedUpdate() only sets it after a valid Linecast hit
    // So whenever for any reason e.g. desync client doesn't get a valid linecast hit (or even just the laser being free
    // aimed into the sky after it once had a hitTransform set), it'll just keep using some previous hitTransform
    // So this can result in client observed laser weapons firing at completely different targets and off angle from
    // where their turret is aiming at and where the actual damage is going to
    
    [HarmonyPatch(typeof(Laser), nameof(Laser.FixedUpdate))]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    private static void Prefix(Laser __instance)
    {
        if (GameManager.IsHeadless)
            return;
        
        // Simply set hitTransform to null because a valid FixedUpdate Linecast hit will blindly set it anyway,
        // and otherwise doesn't do anything with it
        
        __instance.hitTransform = null;
    }
    
    [HarmonyPatch(typeof(Laser), nameof(Laser.FixedUpdate))]
    [HarmonyPostfix]
// ReSharper disable once InconsistentNaming
    private static void Postfix(Laser __instance)
    {
        if (GameManager.IsHeadless || __instance.beamRenderer == null || !__instance.beamRenderer.enabled ||
            __instance.hitTransform != null || __instance.beamTransform == null)
            return;
        
        Vector3 end;
        
        var hq = __instance.attachedUnit?.NetworkHQ;
        
        if (__instance.currentTarget != null && hq != null && !hq.IsTargetBeingTracked(__instance.currentTarget) &&
            hq.TryGetKnownPosition(__instance.currentTarget, out var knownPosition))
        {
            // Same as vanilla fall back in case no live/known target datalink position
            end = knownPosition.ToLocalPosition();
        }
        else if (__instance.currentTargetTransform != null)
        {
            end = __instance.currentTargetTransform.position;
        }
        else
        {
            // Fall back if client Linecast missed and not directly firing at a target, to still visualise a beam
            // at correct length
            
            var fallbackRange = __instance.info.targetRequirements.maxRange;
            
            if (fallbackRange <= 0f)
                fallbackRange = 20000f;
            
            end = __instance.transform.position + __instance.transform.forward * fallbackRange;
        }
        
        var delta = end - __instance.beamTransform.position;
        
        if (delta.sqrMagnitude < 0.0001f)
            return;
        
        __instance.beamTransform.rotation = Quaternion.LookRotation(delta);
        __instance.beamTransform.localScale = new Vector3(__instance.beamScale, __instance.beamScale, delta.magnitude);
    }
}
#endif
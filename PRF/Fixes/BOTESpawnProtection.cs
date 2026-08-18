using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Jobs;
using UnityEngine;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
// ReSharper disable once InconsistentNaming
internal class BOTESpawnProtection(ConfigFile config) : ConfigurableFix(config)
{
    private const float ProtectionDuration = 10f;
    private const string ShipPartBridgeTypeName = "NOComponentWIP.ShipPartBridge";
    
    protected override string Description =>
        $"{base.Description}\n" +
        "Adds a small grace period to spawned BOTE player ships, making them immune to damage to prevent desynced " +
        "underwater position state from immediately destroying them on spawn. Also corrects ship spawn position to be " +
        "properly, gently placed on water's surface, instead of underwater.\n\n" + 
        "Needed on client, and ideally also on server. No downsides if keeping it on when not using BOTE, but off by " + 
        "default for those who don't ever use BOTE.";
    
    protected override bool DefaultEnabled => false;
    
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.SetLocalSim))]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    private static void SetLocalSimPrefix(Aircraft __instance, bool localSim)
    {
        // Set a spawn protected spawning Bote craft to be gently on water surface instead of possibly underwater
        // Importantly this runs right before SetComplexPhysics() would be called, after which relocating it
        // would involve dealing with all the separate parts
        
        if (!localSim || !IsSpawnProtected(__instance))
            return;
        
        var cushions = __instance.GetComponentsInChildren<AirCushion>(true);
        if (cushions.Length == 0)
            return;
        
        var requiredRise = 0f;
        
        foreach (var cushion in cushions)
        {
            if (cushion == null || cushion.castTransform == null || cushion.maxHeight <= 0f)
                continue;
            
            var targetY = Datum.LocalSeaY + cushion.maxHeight * 0.5f;
            requiredRise = Mathf.Max(requiredRise, targetY - cushion.castTransform.position.y);
        }
        
        if (requiredRise <= 0f)
            return;
        
        var correctedPosition = __instance.rb.position + Vector3.up * requiredRise;
        __instance.rb.position = correctedPosition;
        __instance.transform.position = correctedPosition;
    }
    
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.Awake))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void AircraftAwakePostfix(Aircraft __instance)
    {
        // Add tracker for newly spawned Bote ships to track their 10s spawn window
        
        if (!IsBoteShip(__instance))
            return;
        
        if (!__instance.TryGetComponent<BoteShipSpawnProtection>(out var protection))
            protection = __instance.gameObject.AddComponent<BoteShipSpawnProtection>();
        
        protection.ProtectFor(ProtectionDuration);
    }
    
    [HarmonyPatch(typeof(UnitPart), nameof(UnitPart.TakeDamage))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    // ReSharper disable once InconsistentNaming
    private static bool UnitPartTakeDamagePrefix(UnitPart __instance)
    {
        // Prevent damage from applying to new Bote ship within its spawn invulnerability window
        
        if (__instance.GetUnit() is not Aircraft aircraft)
            return true;
        
        return !IsSpawnProtected(aircraft, true);
    }
    
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.RpcDamage))]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    // Prevent damage from applying to new Bote ship within its spawn invulnerability window
    private static bool RpcDamagePrefix(Aircraft __instance) => !IsSpawnProtected(__instance, true);
    
    [HarmonyPatch(typeof(Pilot), nameof(Pilot.TakeGForceDamage))]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    // Prevent pilot of freshly spawned Bote ship from taking damage
    private static bool PilotTakeGForceDamagePrefix(Pilot __instance)
    {
        var aircraft = __instance.aircraft;
        return aircraft == null || !IsSpawnProtected(aircraft);
    }
    
    [HarmonyPatch(typeof(Pilot), nameof(Pilot.TakeWaterDamage))]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    // Prevent pilot of freshly spawned Bote ship from taking damage
    private static bool PilotTakeWaterDamagePrefix(Pilot __instance)
    {
        var aircraft = __instance.aircraft;
        return aircraft == null || !IsSpawnProtected(aircraft);
    }
    
    [HarmonyPatch(typeof(AirCushion), nameof(AirCushion.FixedUpdate))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void AirCushionFixedUpdatePostfix(AirCushion __instance)
    {
        // Fix AirCushions being normally unable to generate any "thrust" upwards when they're at y < 0, which
        // can happen when an LCAC is pushed underwater briefly right after spawn
        // This only happens inside the 10s spawn protection window
        
        if (__instance.attachedUnit is not Aircraft aircraft || !aircraft.LocalSim ||
            !IsSpawnProtected(aircraft) || aircraft.disabled || __instance.sinking || __instance.deflating ||
            __instance.castTransform == null || __instance.castTransform.position.y > Datum.LocalSeaY ||
            __instance.spring <= 0f || __instance.condition <= 0f)
            return;
        
        var rb = aircraft.rb;
        var surfaceNormal = Vector3.up;
        var springThrust = __instance.spring * __instance.maxHeight;
        var verticalSpeed = Vector3.Dot(rb.velocity, -surfaceNormal);
        var dampingThrust = __instance.damp * verticalSpeed;
        var recoveryThrust = Mathf.Max(springThrust + dampingThrust, 0f);
        __instance.currentThrust = recoveryThrust;
        
        var forcePosition = __instance.castTransform.TransformPoint(__instance.thrustApplyOffset);
        rb.AddForceAtPosition(recoveryThrust * surfaceNormal * __instance.condition, forcePosition);
        
        var localAngularVelocity = __instance.castTransform.InverseTransformDirection(rb.angularVelocity);
        var yawDamping = -localAngularVelocity.y * __instance.steerAxisDamping * rb.mass;
        var aligningTorque = 50f * -Vector3.Cross(surfaceNormal, __instance.castTransform.up);
        aligningTorque -= 15f * rb.angularVelocity;
        
        rb.AddTorque(
            (aligningTorque * rb.mass * __instance.aligningStrength + __instance.castTransform.up * yawDamping) *
            __instance.condition);
    }
    
    [HarmonyPatch(typeof(Pilot), nameof(Pilot.Pilot_OnAeroInputsApplied))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void PilotAeroInputsPostfix(Pilot __instance,
        // ReSharper disable once InconsistentNaming
        ref PartResult __result)
    {
        // Prevent loss of (engine) control for freshly spawned Bote ships if they're pushed underwater
        
        if (__result != PartResult.Remove || __instance.dead || __instance.ejected)
            return;
        
        var aircraft = __instance.aircraft;
        
        if (aircraft == null || !aircraft.LocalSim || !IsSpawnProtected(aircraft) ||
            __instance.transform.position.y >= Datum.LocalSeaY - 10f)
            return;
        
        __result = PartResult.None;
    }
    
    private static bool IsSpawnProtected(Aircraft aircraft, bool requireServer = false)
    {
        if (aircraft == null || (requireServer && !aircraft.IsServer))
            return false;
        
        return aircraft.TryGetComponent<BoteShipSpawnProtection>(out var protection) &&
               protection.Active;
    }
    
    private static bool IsBoteShip(Aircraft aircraft)
    {
        var bridgeType = AccessTools.TypeByName(ShipPartBridgeTypeName);
        return bridgeType != null && aircraft.GetComponent(bridgeType) != null;
    }
}

internal sealed class BoteShipSpawnProtection : MonoBehaviour
{
    private float _protectedUntil;
    public bool Active => Time.timeSinceLevelLoad < _protectedUntil;
    
    public void ProtectFor(float duration) => _protectedUntil = Time.timeSinceLevelLoad + duration;
}
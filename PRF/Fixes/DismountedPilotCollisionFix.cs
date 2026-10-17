using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class DismountedPilotCollisionFix(ConfigFile config) : ConfigurableFix(config)
{
    private const int PilotGroundOnlyLayer = 28;
    private static bool _pilotCollisionLayerConfigured;
    protected override string Description => "Fixes dismounted pilots colliding with everything including planes.";
    
    [HarmonyPatch(typeof(PilotDismounted), nameof(PilotDismounted.Setup))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void PilotDismountedSetup_Postfix(PilotDismounted __instance)
    {
        ConfigurePilotCollisionLayer();
        
        var colliders = __instance.GetComponentsInChildren<Collider>(true);
        foreach (var collider in colliders)
        {
            if (collider == null || collider.isTrigger)
                continue;
            
            collider.gameObject.layer = PilotGroundOnlyLayer;
        }
    }
    
    private static void ConfigurePilotCollisionLayer()
    {
        if (_pilotCollisionLayerConfigured)
            return;
        
        for (var layer = 0; layer < 32; layer++)
        {
            var allowCollision = layer == PhysicsLayers.Statics || layer == PhysicsLayers.Ships ||
                                 layer == PhysicsLayers.Water;
            Physics.IgnoreLayerCollision(PilotGroundOnlyLayer, layer, !allowCollision);
        }
        
        _pilotCollisionLayerConfigured = true;
    }
}
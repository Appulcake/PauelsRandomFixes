using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class ResupplyAttributionFix(ConfigFile config) : ConfigurableFix(config)
{
    protected override string Description =>
        $"{base.Description}\nFixes resupplies not rewarding the supplier on rearms.";
    
#pragma warning disable Harmony003
    [HarmonyPatch(typeof(Unit), nameof(Unit.RpcRearm))]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    private static void RpcRearmPrefix(Unit __instance, RearmEventArgs args)
    {
        if (__instance == null || !__instance.IsServer || args.Rearmer == null || args.Stations == null)
            return;
        
        var rearmValue = 0f;
        var stationCount = Mathf.Min(args.Stations.Length, __instance.weaponStations.Count);
        
        for (var i = 0; i < stationCount; i++)
        {
            var amount = args.Stations[i];
            
            if (amount <= 0)
                continue;
            
            var weaponInfo = __instance.weaponStations[i].WeaponInfo;
            
            if (weaponInfo == null)
                continue;
            
            rearmValue += amount * weaponInfo.costPerRound;
        }
        
        if (rearmValue <= 0f)
            return;
        
        SupplyAttribution.HandleRearm(args.Rearmer, __instance, rearmValue);
    }
#pragma warning restore Harmony003
    
    private static class SupplyAttribution
    {
        private static Player? ResolvePlayer(Unit? unit, int depth = 0)
        {
            if (unit == null || depth > 5)
                return null;
            
            if (unit is Aircraft aircraft)
                return aircraft.Player;
            
            if (unit is GroundVehicle groundVehicle && groundVehicle.Networkowner != null)
                return groundVehicle.Networkowner;
            
            if (unit is not Container container)
                return null;
            
            if (UnitRegistry.TryGetUnit(container.ownerID, out var ownerUnit) && ownerUnit != null && ownerUnit != unit)
            {
                // Recursive so that a chain also resolves, where ownership could be e.g.
                // player deploys truck from plane => truck rearms someone, in this case don't just take the truck as
                // rearm owner and try to find original player
                
                var owner = ResolvePlayer(ownerUnit, depth + 1);
                
                if (owner != null)
                    return owner;
            }
            
            if (UnitRegistry.TryGetPersistentUnit(container.ownerID, out var persistentOwner) &&
                persistentOwner != null && persistentOwner.player != null)
            {
                return persistentOwner.player;
            }
            
            return null;
        }
        
        internal static void HandleRearm(Unit? sourceUnit, Unit? target, float refillValue)
        {
            if (sourceUnit == null || target == null || refillValue <= 0f || sourceUnit.GetPlayer() != null)
                return;
            
            var directOwner = ResolvePlayer(sourceUnit);
            
            if (directOwner == null)
                return;
            
            // Prevent self rearm from giving rewards
            var targetOwner = ResolvePlayer(target);
            
            if (directOwner == targetOwner)
                return;
            
            var hq = sourceUnit.NetworkHQ != null ? sourceUnit.NetworkHQ : directOwner.HQ;
            
            if (hq == null)
                return;
            
            hq.ReportSupplyAction(directOwner, target, refillValue);
        }
    }
}
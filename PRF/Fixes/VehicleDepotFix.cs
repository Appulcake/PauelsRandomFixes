using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking;
// ReSharper disable InconsistentNaming

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class VehicleDepotFix(ConfigFile config) : ConfigurableFix(config)
{
    protected override string Description =>
        $"{base.Description}\nFixes vehicle depots not belonging to an airbase they're in and thus not getting " +
        "captured when the airbase is captured, and fixes an issue where if a depot is removed by a mission " + 
        "objective, it can break unit spawns on that faction because the null entry depot is not cleaned up from " +
        "that faction's depot list.\n\n" +
        "This fix is only needed on host/server.";
    
    [HarmonyPatch(typeof(VehicleDepot), nameof(VehicleDepot.OnStartClient))]
    [HarmonyPostfix]
    private static void VehicleDepotOnStartClientPostfix(VehicleDepot __instance)
    {
        if (GameManager.gameState == GameState.Encyclopedia)
            return;
        
        // VehicleDepot.OnStartClient() doesn't run Building.cs' airbase registration, so it'd never be part of its
        // buildings list and thus when the airbase is captured the vehicle depot won't be
        __instance.ClientAddBuildingToAirbase();
    }
    
    [HarmonyPatch(typeof(Unit), nameof(Unit.HQChanged))]
    [HarmonyPostfix]
    private static void HQChangedPostfix(Unit __instance, FactionHQ oldHQ, FactionHQ newHQ)
    {
        if (!NetworkManagerNuclearOption.i.Server.Active || __instance is not VehicleDepot depot || oldHQ == newHQ)
            return;
        
        // There's no direct remove depot method call so when a depot changes factions, it still remains in the old
        // faction's list and on vehicle deployments would start using the stock of the other faction still
        if (oldHQ != null)
        {
            for (var i = oldHQ.depotSorted.Count - 1; i >= 0; i--)
            {
                if (oldHQ.depotSorted[i].depot == depot)
                    oldHQ.depotSorted.RemoveAt(i);
            }
        }
        
        if (newHQ == null)
            return;
        
        for (var i = 0; i < newHQ.depotSorted.Count; i++)
        {
            if (newHQ.depotSorted[i].depot == depot)
                return;
        }
        
        newHQ.AddDepot(depot);
    }
    
    // Depots don't have a remove method and thus old/null depot entries can exist, then when FactionHQ.DeployUnits()
    // calls SortDepots() it doesn't do a null check and runs into an NRE, preventing unit spawns on that faction
    // Only DeployVehicles() checks for a null/invalid depot but that's too late, that'd only come a bit later
    // This can happen for example when a mission objective removes a vehicle depot from existence
    [HarmonyPatch(typeof(FactionHQ), nameof(FactionHQ.SortDepots))]
    [HarmonyPrefix]
    private static void FactionHQSortDepotsPrefix(FactionHQ __instance)
    {
        for (var i = __instance.depotSorted.Count - 1; i >= 0; i--)
        {
            if (__instance.depotSorted[i].depot == null)
                __instance.depotSorted.RemoveAt(i);
        }
    }
}
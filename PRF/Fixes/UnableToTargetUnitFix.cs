using BepInEx.Configuration;
using HarmonyLib;

#if CLIENT
namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class UnableToTargetUnitFix(ConfigFile config) : ConfigurableFix(config)
{
    protected override string Description =>
        $"{base.Description}\nFixes client being unable to target units in certain mission scenarios (target selected " +
        "sound plays but unit isn't actually added to target list).";
    
    // On aircraft spawn, instantiate a combat HUD marker for any units that's also on the map but doesn't have a marker yet
    // normally all map icons should also have such a marker instantiated, so this doesn't give any new info
    [HarmonyPatch(typeof(CombatHUD), nameof(CombatHUD.SetAircraft))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void SetAircraftPostfix(CombatHUD __instance, Aircraft aircraft)
    {
        if (aircraft == null)
            return;
        
        var map = SceneSingleton<DynamicMap>.i;
        
        if (map == null || map.HQ != aircraft.NetworkHQ)
            return;
        
        foreach (var mapIcon in map.mapIcons)
        {
            if (mapIcon is not UnitMapIcon unitIcon)
                continue;
            
            var unit = unitIcon.unit;
            
            if (unit == null || unit == aircraft || unit.disabled || __instance.MarkerExists(unit))
                continue;
            
            __instance.CreateMarker(unit.persistentID);
        }
    }
    
    // This one might not be necessary but is a failback check on unit selection to see if it should be possible to
    // select this unit (if its icon exists on map)
    [HarmonyPatch(typeof(CombatHUD), nameof(CombatHUD.SelectUnit))]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    private static void SelectUnitPrefix(CombatHUD __instance, Unit unit)
    {
        if (unit == null || __instance.aircraft == null || __instance.MarkerExists(unit))
            return;
        
        var map = SceneSingleton<DynamicMap>.i;
        
        if (map == null || map.HQ != __instance.aircraft.NetworkHQ)
            return;
        
        if (!DynamicMap.TryGetMapIcon(unit, out _))
            return;
        
        __instance.CreateMarker(unit.persistentID);
    }
}
#endif
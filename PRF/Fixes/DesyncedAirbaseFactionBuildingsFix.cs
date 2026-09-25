#if CLIENT
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.SavedMission;
// ReSharper disable InconsistentNaming

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class DesyncedAirbaseFactionBuildingsFix(ConfigFile config) : ConfigurableFix(config)
{
    protected override string Description =>
        $"{base.Description}\nFixes client visual mismatch where some buildings in airbases can show as the wrong " + 
        "faction, e.g. when joining you see a bunch of hostile buildings in an otherwise friendly airbase.";
    
    // Issue stems from how when you join a match as client, you get the right syncvars for the HQ states for all units/
    // buildings, it triggers a HQChanged() to make sure things are in the right order, but then you get
    // Mission.OnSceneLoaded() => LinkSavedUnit(savedBuilding) => SetupAirbase() => savedBuilding.SetAirbase() where
    // it reads the default/original faction state of those (savedBuilding = custom placed) buildings, and resets their
    // faction back to how they'd be in the mission's starting phase, despite already being captured
    // So client gets proper info that these buildings are already captured, but then resets their factions back to
    // uncaptured because of this possible race condition
    // This fix prevents that secondary changing factions, since a change at this stage means they were already set
    // (by mirage sync message earlier) and thus shouldn't be overwritten
    
    [HarmonyPatch(typeof(SavedBuilding), nameof(SavedBuilding.SetAirbase))]
    [HarmonyPrefix]
    private static void SetAirbasePrefix(SavedBuilding __instance, out State __state)
    {
        var unit = __instance.Unit;
        if (unit == null || !unit.IsClientOnly)
        {
            __state = default;
            return;
        }
        
        __state = new State(unit, unit.NetworkHQ);
    }
    
    [HarmonyPatch(typeof(SavedBuilding), nameof(SavedBuilding.SetAirbase))]
    [HarmonyPostfix]
    private static void SetAirbasePostfix(State __state)
    {
#pragma warning disable Harmony003
        if (__state.Unit == null)
            return;
        
        if (__state.Unit.NetworkHQ != __state.HQ)
        {
            __state.Unit.HQ.Value = __state.HQ;
        }
#pragma warning restore Harmony003
    }
    
    private readonly struct State(Unit unit, FactionHQ hq)
    {
        public readonly Unit Unit = unit;
        public readonly FactionHQ HQ = hq;
    }
}
#endif
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.SavedMission;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class MissionLoadFix(ConfigFile config) : ConfigurableFix(config)
{
    protected override string Description =>
        "Fixes 0.34.2 regression causing many v5 missions to fail to load.";
    
    // 0.34.1:
    // public List<VehicleWaypoint> waypoints = new List<VehicleWaypoint>();
    //
    // 0.34.2:
    // public List<VehicleWaypoint> waypoints;
    //
    // MissionConverter_V5_to_V6.ConvertShip constructs SavedShip with the
    // parameterless constructor and then immediately uses waypoints.Add()
    [HarmonyPatch(typeof(SavedShip), MethodType.Constructor)]
    [HarmonyPostfix]
    private static void SavedShipConstructorPostfix(SavedShip __instance)
    {
        __instance.waypoints ??= new List<VehicleWaypoint>();
    }
    
    // Same as SavedShip.
    [HarmonyPatch(typeof(SavedVehicle), MethodType.Constructor)]
    [HarmonyPostfix]
    private static void SavedVehicleConstructorPostfix(SavedVehicle __instance)
    {
        __instance.waypoints ??= new List<VehicleWaypoint>();
    }
    
    // 0.34.1:
    // public Restrictions restrictions = new Restrictions();
    //
    // 0.34.2:
    // public Restrictions restrictions;
    [HarmonyPatch(typeof(MissionFaction), MethodType.Constructor)]
    [HarmonyPostfix]
    private static void MissionFactionConstructorPostfix(MissionFaction __instance)
    {
        __instance.restrictions ??= new Restrictions();
    }
    
    // 0.34.1:
    // public GlobalPosition[] exitPoints = new GlobalPosition[0];
    //
    // 0.34.2:
    // public GlobalPosition[] exitPoints;
    [HarmonyPatch(typeof(SavedRunway), MethodType.Constructor)]
    [HarmonyPostfix]
    private static void SavedRunwayConstructorPostfix(SavedRunway __instance)
    {
        __instance.exitPoints ??= Array.Empty<GlobalPosition>();
    }
}
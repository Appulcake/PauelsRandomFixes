using System;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking;
// ReSharper disable InconsistentNaming

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class MountedWeaponDuplicateLaunchRpcFix(ConfigFile config) : ConfigurableFix(config)
{
    protected override string Description =>
        $"{base.Description}\nPrevents player mounted missiles/cargo from sending their fired RPC message twice on " +
        "server, which causes other observers to expend two mounts for one launch (duplicate ammo reduction and " +
        "also visual mounts that disappear off the plane).\n\nThis fix is only needed on host/server.";
    
    // Player client on launching a mounted missile/cargo runs CmdLaunchMissile() => server receives and runs
    // UserCode_CmdLaunchMissile_644415535(), which runs both:
    // WeaponStation.LaunchMount() => MountedMissile.Fire() => Aircraft.RpcLaunchMissile()
    // and
    // RpcLaunchMissile() directly
    // And thus two RPCs are sent to all remote observers, so they expend duplicated cargo/missiles (ammo reduction in
    // ammo counter, and visually duplicate disappear off mounts too)
    
    // Holder while executing a player's launch command
    [ThreadStatic]
    private static LaunchContext? currentLaunch;
    
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.UserCode_CmdLaunchMissile_644415535))]
    [HarmonyPrefix]
    private static void CmdLaunchMissilePrefix(Aircraft __instance, byte stationIndex, out LaunchContext? __state)
    {
        __state = currentLaunch;
        if (NetworkManagerNuclearOption.i != null && NetworkManagerNuclearOption.i.Server.Active)
            currentLaunch = new LaunchContext(__instance, stationIndex);
    }
    
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.UserCode_CmdLaunchMissile_644415535))]
    [HarmonyFinalizer]
    private static Exception CmdLaunchMissileFinalizer(Exception __exception, LaunchContext? __state)
    {
        currentLaunch = __state;
        return __exception;
    }
    
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.RpcLaunchMissile))]
    [HarmonyPrefix]
    private static bool RpcLaunchMissilePrefix(Aircraft __instance, byte stationIndex)
    {
        var context = currentLaunch;
        if (context == null || !ReferenceEquals(context.Aircraft, __instance) || context.StationIndex != stationIndex)
            return true;
        
        // The RPC has already been sent in this launch command, so this is a duplicate event that can be cancelled
        if (context.RpcSent)
            return false;
        
        context.RpcSent = true;
        return true;
    }
    
    private sealed class LaunchContext(Aircraft aircraft, byte stationIndex)
    {
        public readonly Aircraft Aircraft = aircraft;
        public readonly byte StationIndex = stationIndex;
        public bool RpcSent;
    }
}
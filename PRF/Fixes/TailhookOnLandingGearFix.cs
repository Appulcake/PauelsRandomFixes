#if CLIENT
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class TailhookOnLandingGearFix(ConfigFile config) : ConfigurableFix(config)
{
    private const float MinimumDeployRadarAltitude = 1f;
    
    protected override string Description =>
        $"{base.Description}\nTies tail hook state to landing gear, when you deploy gear tail hook also deploys, and " +
        "when retracting gear it also retracts.";
    
    [HarmonyPatch(typeof(TailHook), nameof(TailHook.CheckDeployConditions))]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    private static bool CheckDeployConditionsPrefix(TailHook __instance)
    {
        if (__instance.unitPart?.parentUnit is Aircraft aircraft)
            UpdateTailHook(__instance, aircraft, GetDesiredState(aircraft));
        
        return false;
    }
    
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.SetGear), typeof(LandingGear.GearState))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void SetGearPostfix(Aircraft __instance, LandingGear.GearState gearState)
    {
        var deploy = GetDesiredState(__instance, gearState);
        
        // No LINQ to save performance in lookup
        foreach (var part in __instance.GetAllParts())
        {
            if (part == null)
                continue;
            
            foreach (var tailHook in part.GetComponentsInChildren<TailHook>(true))
                UpdateTailHook(tailHook, __instance, deploy);
        }
    }
    
    private static bool GetDesiredState(Aircraft aircraft) => GetDesiredState(aircraft, aircraft.gearState);
    
    private static bool GetDesiredState(Aircraft aircraft, LandingGear.GearState gearState)
    {
        return gearState switch
        {
            LandingGear.GearState.Extending => true,
            LandingGear.GearState.LockedExtended => true,
            
            LandingGear.GearState.Retracting => false,
            LandingGear.GearState.LockedRetracted => false,
            
            _ => aircraft.gearDeployed
        };
    }
    
    private static void UpdateTailHook(TailHook tailHook, Aircraft aircraft, bool deploy)
    {
        // This is to prevent it deploying from retracted state on the ground (e.g. when you spawn so it's not out)
        if (deploy && !tailHook.deployed && aircraft.radarAlt <= MinimumDeployRadarAltitude)
            return;
        
        if (tailHook.deployed == deploy)
            return;
        
        tailHook.deployed = deploy;
        
        if (!deploy)
            tailHook.Unhook();
        
        tailHook.enabled = true;
        
        if (GameManager.IsLocalAircraft(aircraft))
            SceneSingleton<AircraftActionsReport>.i.ReportText(deploy ? "Tail Hook Deployed" : "Tail Hook Retracted",
                4f);
    }
}
#endif
using System;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class BoteFobInitialiseFix(ConfigFile config) : ConfigurableFix(config)
{
    protected override string Description =>
        $"{base.Description}\n" + 
        "Workaround for BOTE FOBs not getting properly saved into CurrentMission airbases list, so that they can show " + 
        "up for any future (re)joining clients too. Only needed on server, inert on client.";
    
    [HarmonyPatch(typeof(Airbase), nameof(Airbase.OnStartServer))]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    private static void Prefix(Airbase __instance)
    {
        // This follows implementation from the proper PR pending on Bote at:
        // https://github.com/MinecrackTyler/BoscaliOceanTrainingExercise/pull/17
        //
        // Idea is to add a newly created Bote FOB to proper CurrentMission's airbases list
        // and then to serialise those changes so future joining clients get this state (otherwise they'd get the old
        // version that was serialised on mission start, without any of the newly placed FOBs since)
        
        if (!__instance.IsCustom)
            return;
        
        var saved = __instance.SavedAirbase;
        if (saved == null || string.IsNullOrEmpty(saved.UniqueName) ||
            !saved.UniqueName.StartsWith("FOB_", StringComparison.Ordinal)) return;
        
        var mission = MissionManager.CurrentMission;
        if (mission?.airbases == null)
            return;
        
        if (mission.airbases.Any(x => x != null && x.UniqueName == saved.UniqueName)) return;
        saved.Center = __instance.center.GlobalPosition();
        
        saved.SelectionPosition =
            __instance.aircraftSelectionTransform != null
                ? __instance.aircraftSelectionTransform.GlobalPosition()
                : saved.Center;
        
        if (__instance.CurrentHQ != null) saved.faction = __instance.CurrentHQ.faction.factionName;
        
        saved.SavedInMission = true;
        saved.Airbase = __instance;
        mission.airbases.Add(saved);
        
        // Using the multipart mission serialiser instead of doing checks in-place whether it's a <64kB mission or not
        // This keeps it simpler and more compatible with any mission
        
        var networkMission = NetworkManagerNuclearOption.i.NetworkMission;
        networkMission.partSender = NetworkMission.PartSender.Create(new NetworkMission.SyncMission(mission));
        networkMission.sendAsParts = true;
    }
}
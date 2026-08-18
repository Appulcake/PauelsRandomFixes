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
        if (!__instance.IsCustom)
            return;
        
        var saved = __instance.SavedAirbase;
        
        if (saved == null || string.IsNullOrEmpty(saved.UniqueName) ||
            !saved.UniqueName.StartsWith("FOB_", StringComparison.Ordinal)) return;
        
        var mission = MissionManager.CurrentMission;
        
        if (mission?.airbases == null)
            return;
        
        // Future-proof against BOTE fixing this itself.
        if (mission.airbases.Any(x => x != null && x.UniqueName == saved.UniqueName)) return;
        
        // BOTE moved the live transform after Airbase.Awake(),
        // so repair the serialized positions.
        saved.Center = __instance.center.GlobalPosition();
        
        saved.SelectionPosition =
            __instance.aircraftSelectionTransform != null
                ? __instance.aircraftSelectionTransform.GlobalPosition()
                : saved.Center;
        
        if (__instance.CurrentHQ != null) saved.faction = __instance.CurrentHQ.faction.factionName;
        
        saved.SavedInMission = true;
        saved.Airbase = __instance;
        
        mission.airbases.Add(saved);
        
        // NetworkMission normally keeps the serialization made at mission start.
        // Force its late-join cache to be rebuilt from the now-current mission.
        //
        // Always using the game's multipart representation avoids having to
        // reproduce its <=64 KB single-message/fallback logic.
        var networkMission = NetworkManagerNuclearOption.i.NetworkMission;
        
        networkMission.partSender = NetworkMission.PartSender.Create(new NetworkMission.SyncMission(mission));
        
        networkMission.sendAsParts = true;
    }
}
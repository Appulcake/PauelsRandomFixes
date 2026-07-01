using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking;

namespace PRF;

[Fix]
[HarmonyPatch]
internal class RestrictedWeaponsAISpawnFix(ConfigFile config): ConfigurableFix(config)
{
    [HarmonyPatch(typeof(WeaponChecker), nameof(WeaponChecker.GetAvailableWeaponsNonAlloc))]
    [HarmonyPostfix]
    public static void FixEmptyOutput(Player player, HardpointSet hardpointSet, Airbase airbase, FactionHQ hq,
        bool allowEmpty, ref List<WeaponMount?> outAvailable)
    {
        if (allowEmpty || outAvailable.Count != 0) return;
        outAvailable.Add(null);
    }
}
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class RestrictedWeaponsAISpawnFix(ConfigFile config): ConfigurableFix(config)
{
    [HarmonyPatch(typeof(WeaponChecker), nameof(WeaponChecker.GetAvailableWeaponsNonAlloc))]
    [HarmonyPostfix]
    public static void FixEmptyOutput(Player player, HardpointSet hardpointSet, Airbase airbase, FactionHQ hq,
        bool allowEmpty, ref List<WeaponMount?> outAvailable)
    {
        if (allowEmpty && outAvailable.Count == 1 && outAvailable[0] == null)
        {
            outAvailable.Clear();
            return;
        }
        if (allowEmpty || outAvailable.Count != 0) return;
        outAvailable.Add(null);
    }
    
    [HarmonyPatch(typeof(WeaponSelector), nameof(WeaponSelector.PopulateOptions))]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> CallAvailableWeaponsAllowEmptyTrue(
        IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions)
            .MatchForward(
                false,
                new CodeMatch(OpCodes.Ldc_I4_0),
                new CodeMatch(OpCodes.Ldarg_0),
                new CodeMatch(OpCodes.Ldfld,
                    AccessTools.Field(typeof(WeaponSelector), nameof(WeaponSelector.getCache))),
                new CodeMatch(ci => ci.Calls(AccessTools.Method(typeof(WeaponChecker), nameof(WeaponChecker.GetAvailableWeaponsNonAlloc))))
            );

        if (matcher.IsValid)
        {
            PRF.Logger.LogDebug("Found call on player side.");
            matcher.SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldc_I4_1));
        }
        
        return matcher.InstructionEnumeration();
    }
}
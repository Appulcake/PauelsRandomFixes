using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using Rewired;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class LookAtTargetFix(ConfigFile config) : ConfigurableFix(config)
{
    [HarmonyPatch(typeof(CameraCockpitState), nameof(CameraCockpitState.UpdateState))]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> CockpitLookAtTargetFix(IEnumerable<CodeInstruction> instructions,
        ILGenerator generator)
    {
        var matcher = new CodeMatcher(instructions);
        
        var padLockTargetField = AccessTools.Field(typeof(PlayerSettings), nameof(PlayerSettings.padLockTarget));
        var padLockField = AccessTools.Field(typeof(CameraCockpitState), nameof(CameraCockpitState.padLock));
        
        // Finding this branch:
        // if (PlayerSettings.padLockTarget && padLock && SceneSingleton<CombatHUD>.i.aircraft != null &&
        // SceneSingleton<CombatHUD>.i.GetTargetList().Count > 0)
        
        matcher.MatchForward(
            true,
            new CodeMatch(OpCodes.Ldsfld, padLockTargetField),
            new CodeMatch(ci => ci.opcode == OpCodes.Brfalse || ci.opcode == OpCodes.Brfalse_S),
            new CodeMatch(OpCodes.Ldarg_0),
            new CodeMatch(OpCodes.Ldfld, padLockField),
            new CodeMatch(ci => ci.opcode == OpCodes.Brfalse || ci.opcode == OpCodes.Brfalse_S)
        );
        
        if (matcher.IsInvalid)
            return matcher.InstructionEnumeration();
        
        // Adding custom continue skip label in case padLockTarget && padLock already checks out
        // If not, add a branch to also do a check for holding Look At Target key to still continue
        // Extra branch is inserted before the original padLock => brfalse to not need to modify it and reuse later
        
        // We end up changing
        // PlayerSettings.padLockTarget && padLock && <rest>
        // into
        // ((PlayerSettings.padLockTarget && padLock) || <hold look at button>) && <rest>
        
        var continueLabel = generator.DefineLabel();
        var playerInputField = AccessTools.Field(typeof(GameManager), nameof(GameManager.playerInput));
        var getButton = AccessTools.Method(typeof(Player), nameof(Player.GetButton), [typeof(string)]);
        
        matcher.InsertAndAdvance(new CodeInstruction(OpCodes.Brtrue, continueLabel));
        matcher.InsertAndAdvance(new CodeInstruction(OpCodes.Ldsfld, playerInputField));
        matcher.InsertAndAdvance(new CodeInstruction(OpCodes.Ldstr, "Cycle Look At"));
        matcher.InsertAndAdvance(new CodeInstruction(OpCodes.Callvirt, getButton));
        matcher.Advance(1);
        matcher.Instruction.labels.Add(continueLabel);
        
        return matcher.InstructionEnumeration();
    }
    
    // Handle when Look At Target key is up to snap back to center instead of lingering where camera got left off
    [HarmonyPatch(typeof(CameraCockpitState), nameof(CameraCockpitState.UpdateState))]
    [HarmonyPostfix]
    public static void OnReleaseLookAtTargetCheck(CameraCockpitState __instance)
    {
        if (!GameManager.playerInput.GetButtonUp("Cycle Look At")) return;
        __instance.panView = 0f;
        __instance.tiltView = 0f;
    }
}
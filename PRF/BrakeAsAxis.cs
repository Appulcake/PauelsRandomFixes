using BepInEx.Configuration;
using HarmonyLib;

namespace PRF;

[Fix]
[HarmonyPatch]
internal class BrakeAsAxis(ConfigFile config): ConfigurableFix(config)
{
    [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.PlayerControls))]
    [HarmonyPostfix]
    public static void PlayerControlsPostfix(PilotPlayerState __instance)
    {
        __instance.controlInputs.brake = (__instance.player.GetAxisRaw("Brake") + 1) / 2f;
    }
}
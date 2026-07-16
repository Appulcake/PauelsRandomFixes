using BepInEx.Configuration;
using HarmonyLib;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class BrakeAsAxis : ConfigurableFix
{
    private static ConfigEntry<bool> _useBrakesNegativeRegion = null!;
    
    public BrakeAsAxis(ConfigFile config) : base(config)
    {
        _useBrakesNegativeRegion = config.Bind(GetType().Name, "UseBrakesNegativeRegion", true,
            "Use negative region of input for the brake axis");
    }
    
    protected override string Description =>
        $"{base.Description}\nFixes \"Brake Axis\" bind to function as an analogue brake input.";
    
    protected override bool DefaultEnabled => false;
    
    [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.PlayerControls))]
    [HarmonyPostfix]
    public static void PlayerControlsPostfix(PilotPlayerState __instance)
    {
        if (_useBrakesNegativeRegion.Value)
            __instance.controlInputs.brake = (__instance.player.GetAxisRaw("Brake") + 1) / 2f;
        else
            __instance.controlInputs.brake = __instance.player.GetAxisRaw("Brake");
    }
}
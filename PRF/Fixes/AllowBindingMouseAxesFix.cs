#if CLIENT
using BepInEx.Configuration;
using HarmonyLib;
using Rewired.UI.ControlMapper;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch(typeof(ControlMapper))]
internal class AllowBindingMouseAxesFix(ConfigFile config) : ConfigurableFix(config)
{
    protected override string Description =>
        $"{base.Description}\nAdds ability to rebind axes to mouse by moving mouse during assignment.";
    
    [HarmonyPatch(typeof(ControlMapper), nameof(ControlMapper.Awake))]
    [HarmonyPostfix]
    internal static void ControlMapperAwakePostFix(ControlMapper __instance)
    {
        __instance.ignoreMouseXAxisAssignment = false;
        __instance.ignoreMouseYAxisAssignment = false;
    }
}
#endif
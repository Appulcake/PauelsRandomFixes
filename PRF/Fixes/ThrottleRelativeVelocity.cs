using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class ThrottleRelativeVelocity : ConfigurableFix
{
    private static ConfigEntry<float> _inputSensitivity = null!;
    
    public ThrottleRelativeVelocity(ConfigFile config) : base(config)
    {
        _inputSensitivity = config.Bind(GetType().Name, "Relative Sensitivity", 3.00f,
            "Sensitivity of the relative throttle input.");
    }
    
    protected override string Description =>
        $"{base.Description}\nFixes \"Throttle Axis\" bind to function as analogue input for relative throttle up/down"
        + " inputs. Not relevant if you don't have Relative Throttle on (i.e. when using a physical throttle).";
    
    [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.PlayerThrottleAxis1Controls))]
    [HarmonyPrefix]
    public static bool ThrottleAxis1ControlsReplacer(PilotPlayerState __instance)
    {
        // Early exit when not using relative throttle to prevent any interference
        if (!PlayerSettings.throttleUseRelative)
            return true;
        
        // Ended up needing to rewrite this to
        // 1) support analogue throttle changes to custom axis 1 (e.g. when holding axis modifier + throttle inputs)
        // 2) current throttle state not overwriting custom axis state when axis modifier was held
        // 3) split setting throttle vs custom axis state properly
        // This was also for necessary compatibility with something like ManualEngineSwivelFix
        
        var inputPlayer = __instance.player;
        var throttleInput = Mathf.Clamp(inputPlayer.GetAxisRaw("Throttle"), -1f, 1f);
        
        var axisModifier = inputPlayer.GetButton("Axis Modifier");
        
        if (!axisModifier)
            __instance.simulatedThrottle =
                Mathf.Clamp(__instance.simulatedThrottle + throttleInput * _inputSensitivity.Value * Time.deltaTime,
                    -1f, 1f);
        
        var customAxisInput = Mathf.Clamp(inputPlayer.GetAxisRaw("Custom Axis 1"), -1f, 1f);
        var previousCustomAxisInput = Mathf.Clamp(inputPlayer.GetAxisRawPrev("Custom Axis 1"), -1f, 1f);
        var customAxisDifference = Mathf.Abs(customAxisInput - previousCustomAxisInput);
        var customAxisOutput = __instance.controlInputs.customAxis1;
        
        if (customAxisDifference is > 0f and < 0.5f)
            customAxisOutput = customAxisInput;
        else if (Mathf.Abs(customAxisInput) > 0.5f)
            customAxisOutput += Mathf.Clamp(customAxisInput - customAxisOutput, -Time.deltaTime, Time.deltaTime);
        
        if (axisModifier) customAxisOutput += throttleInput * Time.deltaTime;
        
        customAxisOutput = Mathf.Clamp01(customAxisOutput);
        
        if (!Mathf.Approximately(__instance.controlInputs.customAxis1, customAxisOutput))
            __instance.controlInputs.customAxis1 = customAxisOutput;
        
        var simulatedThrottle = 0.5f * (__instance.simulatedThrottle + 1f);
        
        if (__instance.collective && PlayerSettings.invertCollective)
            simulatedThrottle = 1f - simulatedThrottle;
        
        __instance.controlInputs.throttle = Mathf.Clamp01(simulatedThrottle);
        
        return false;
    }
}
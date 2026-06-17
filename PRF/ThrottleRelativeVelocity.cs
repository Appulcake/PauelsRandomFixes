using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace PRF;

[Fix]
[HarmonyPatch]
internal class ThrottleRelativeVelocity: ConfigurableFix
{
    private static ConfigEntry<float> _inputSensitivity = null!;

    public ThrottleRelativeVelocity(ConfigFile config) : base(config)
    {
        _inputSensitivity = config.Bind(GetType().Name, "RelativeSensitivity", 3.00f, "Sensitivity of the relative throttle input.");

    }
    
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.PlayerThrottleAxis1Controls))]
    public static bool ThrottleAxis1ControlsReplacer(PilotPlayerState __instance)
    {
        var throttleInput = Mathf.Clamp(__instance.player.GetAxisRaw("Throttle"), -1f, 1f);
        var prevThrottleInput = Mathf.Clamp(__instance.player.GetAxisRawPrev("Throttle"), -1f, 1f);
        var customAxisInput = Mathf.Clamp(__instance.player.GetAxisRaw("Custom Axis 1"), -1f, 1f);
        if (PlayerSettings.throttleUseRelative)
        {
            // CHANGES HERE
            throttleInput = Mathf.Clamp(__instance.simulatedThrottle + throttleInput * _inputSensitivity.Value * Time.deltaTime,-1, 1);
            prevThrottleInput = __instance.simulatedThrottle;
            // End of changes
        }

        if (__instance.player.GetButton("Axis Modifier"))
        {
            customAxisInput += throttleInput;
            throttleInput = 0.0f;
        }

        var throttleInputDiff = Mathf.Abs(throttleInput - prevThrottleInput);
        if (throttleInputDiff > 0.0 && throttleInputDiff < 0.5)
            __instance.simulatedThrottle =
                throttleInput; // if throttle has linear movement (moved less than half the axis in a frame)
        else if (Mathf.Abs(throttleInput) > 0.5) // if throttle is binary and not zero
            // force slow throttle on binary input.
            __instance.simulatedThrottle += Mathf.Clamp(throttleInput - __instance.simulatedThrottle, -Time.deltaTime,
                Time.deltaTime);
        var simThrottle = __instance.simulatedThrottle;
        var prevCustomAxisInput = Mathf.Clamp(__instance.player.GetAxisRawPrev("Custom Axis 1"), -1f, 1f);
        var customAxisDiff = Mathf.Abs(customAxisInput - prevCustomAxisInput);
        var customAxisOutput = __instance.controlInputs.customAxis1;
        if (customAxisDiff > 0.0 && customAxisDiff < 0.5)
            customAxisOutput = customAxisInput;
        else if (Mathf.Abs(customAxisInput) > 0.5)
            customAxisOutput += Mathf.Clamp(customAxisInput - customAxisOutput, -Time.deltaTime, Time.deltaTime);
        if (!Mathf.Approximately(__instance.controlInputs.customAxis1, customAxisOutput))
            __instance.controlInputs.customAxis1 = Mathf.Clamp01(customAxisOutput);
        if (PlayerSettings.throttleUseNegative || PlayerSettings.throttleUseRelative)
            simThrottle = (float)(0.5 * (simThrottle + 1.0));
        if (__instance.collective && PlayerSettings.invertCollective)
            simThrottle = 1f - simThrottle;
        __instance.controlInputs.throttle = Mathf.Clamp01(simThrottle);

        return false;
    }
}
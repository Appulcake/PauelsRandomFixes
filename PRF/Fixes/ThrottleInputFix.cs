#if CLIENT
using BepInEx.Configuration;
using HarmonyLib;
using Rewired;
using UnityEngine;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class ThrottleInputFix : ConfigurableFix
{
    private static ConfigEntry<AbsoluteInputMode> _absoluteInputMode = null!;
    private static ConfigEntry<float> _absoluteCenterIgnoreThreshold = null!;
    private static ConfigEntry<AnalogueIncrementalInputMode> _analogueIncrementalInputMode = null!;
    
    private static ConfigEntry<RelativeInputMode> _relativeInputMode = null!;
    private static ConfigEntry<float> _relativeSensitivity = null!;
    private static ConfigEntry<float> _relativeDeadzone = null!;
    
    private static ConfigEntry<bool> _applyThrottleModesToCustomAxis = null!;
    private static ConfigEntry<float> _relativeCustomAxisSensitivity = null!;
    
    public ThrottleInputFix(ConfigFile config) : base(config)
    {
        _absoluteInputMode = config.Bind(GetType().Name, "Absolute Input Mode", AbsoluteInputMode.Direct,
            "Direct makes the throttle axis fully authoritative. Moving your input to a position directly moves the in-game " +
            "throttle to that position, without randomly dropping/ignoring inputs and changing to relative mode suddenly " +
            "based on magnitude of motion vs last frame (which can especially be noticeable during FPS drops).\n" + 
            "Likely best for physical throttles, sliders, and other absolute axes.\n\n" +
            
            "Direct Ignore Center works like Direct, but input around the center is ignored and the previous throttle position " +
            "is kept. This is mainly useful for controls such as trackpads/touchpads emulating a joystick, where releasing the " +
            "input snaps the virtual axis back to (exact) center. Increasing Absolute Center Ignore Threshold widens the ignored " +
            "range, which also means throttle positions inside that range cannot be selected with the axis.\n " + 
            "Example usecase: use trackpad on Steam Controller as absolute throttle - touch at certain position or slide along it " +
            "to control throttle on an absolute range like a volume slider. When releasing touch, throttle stays at that point " + 
            "without snapping back to 50%, by ignoring the very center spot from acting on throttle.\n\n" +
            
            "Vanilla Hybrid uses the game's original throttle handling. Small changes between frames are applied " +
            "directly, while larger changes may instead be applied gradually (acting like relative throttle), and some edge " +
            "cases result in ignored inputs.");
        _absoluteCenterIgnoreThreshold = config.Bind(GetType().Name, "Absolute Center Ignore Threshold", 0f,
            new ConfigDescription(
                "Size of the center zone ignored by Direct Ignore Center. 0 means only exact center is ignored. " +
                "Higher values widen the ignored range. Throttle keeps its previous position while input is inside this zone.",
                new AcceptableValueRange<float>(0f, 1f)));
        
        _analogueIncrementalInputMode = config.Bind(GetType().Name, "Analogue Increase/Decrease Mode",
            AnalogueIncrementalInputMode.Relative,
            "Controls analogue axes bound to \"Increase Throttle\" or \"Decrease Throttle\" when relative throttle is disabled. " +
            "Relative treats them as proportional increase/decrease controls. Absolute treats them as absolute throttle positions, " +
            "with Increase covering 50-100% and Decrease covering 50-0%. Binary buttons and keyboard keys remain relative.");
        
        _relativeInputMode = config.Bind(GetType().Name, "Relative Input Mode", RelativeInputMode.Proportional,
            "Proportional makes analogue relative throttle inputs adjust throttle faster or slower depending on how much " +
            "the input is pressed. Binary inputs such as keyboard keys/gamepad buttons still adjust at full speed.\n\n" +
            
            "Full Rate ignores analogue input value, any input above the configured deadzone is treated as fully pressed. " +
            "Useful if you use an analogue trigger or axis for relative throttle but always want the same full " +
            "adjustment speed.");
        
        _relativeSensitivity = config.Bind(GetType().Name, "Relative Sensitivity", 1f,
            "Overall speed multiplier for relative throttle movement.");
        _relativeDeadzone = config.Bind(GetType().Name, "Relative Deadzone", 0.1f,
            new ConfigDescription(
                "Minimum input required in Full Rate mode before relative throttle starts moving. " 
                + "0.1 (10%) matches vanilla's hardcoded threshold.",
                new AcceptableValueRange<float>(0f, 1f)));
        
        _applyThrottleModesToCustomAxis = config.Bind(GetType().Name, "Apply Throttle Modes To Custom Axis", true,
            "Applies the configured throttle input modes when throttle is redirected to Custom Axis 1 by holding " +
            "\"Axis Modifier\".\n\nDoes not change the directly bound \"Custom Axis 1\" input.");
        _relativeCustomAxisSensitivity = config.Bind(GetType().Name, "Relative Custom Axis Sensitivity", 1f,
            "Speed multiplier for relative throttle input while it's redirected to Custom Axis 1 with \"Axis Modifier\".");
    }
    
    protected override string Description =>
        $"{base.Description}\n" +
        "Improves and makes throttle axis handling configurable for both absolute and relative throttle " + 
        "(based on \"Use Throttle Relative Axis\" in the game's settings).\n\n" +
        
        "With relative throttle disabled, full \"Throttle Axis\" binds use the selected Absolute Input Mode, while " + 
        "\"Increase Throttle\" and \"Decrease Throttle\" binds are automatically treated as incremental/relative inputs.\n\n" +
        "With relative throttle enabled, Proportional mode allows analogue inputs to control how quickly throttle moves, " +
        "while binary inputs still move it at full speed. " +
        "Also fixes relative throttle going into negative range, causing it to \"stick\" where you need to first " +
        // ReSharper disable once UseVerbatimString
        "increment it for a while before it comes out of this zone and starts going up from 0%.";
    
    private enum AbsoluteInputMode
    {
        Direct,
        DirectIgnoreCenter,
        VanillaHybrid
    }
    
    private enum RelativeInputMode
    {
        Proportional,
        FullRate
    }
    
    private enum ThrottleInputKind
    {
        None,
        FullAxis,
        IncrementalAxis,
        IncrementalButton
    }
    
    private enum AnalogueIncrementalInputMode
    {
        Relative,
        Absolute
    }
    
    private static ThrottleInputKind _lastThrottleInputKind;
    private static PilotPlayerState? _throttleInputState;
    private static bool? _analogueIncrementalRelativeOverride;
    
    private static AnalogueIncrementalInputMode GetAnalogueIncrementalInputMode()
    {
        if (!_analogueIncrementalRelativeOverride.HasValue)
            return _analogueIncrementalInputMode.Value;
        
        return _analogueIncrementalRelativeOverride.Value
            ? AnalogueIncrementalInputMode.Relative
            : AnalogueIncrementalInputMode.Absolute;
    }
    
    // "API" for optional companion mods, null = use configured mode, true = Relative, false = Absolute
    internal static void SetAnalogueIncrementalInputModeOverride(bool? relative)
    {
        _analogueIncrementalRelativeOverride = relative;
    }
    
    internal static bool? GetAnalogueIncrementalInputModeOverride() =>
        _analogueIncrementalRelativeOverride;
    
    internal static bool GetEffectiveAnalogueIncrementalInputMode() =>
        GetAnalogueIncrementalInputMode() == AnalogueIncrementalInputMode.Relative;
    
    // I think exposing a 1.0 as default to users is more meaningful so calibrate the default 3 units/s movement to 1.0
    // instead of a more cryptic 3.0 default or explaining that -1 to 1 is 2 units so at 3 u/s it takes 0.67s on 3.0 to
    // move it from 0% to 100%, instead it's just 1.0 as "normal rate"
    private static float GetRelativeSensitivity() => _relativeSensitivity.Value * 3f;
    
    [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.PlayerThrottleAxis1Controls))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
// ReSharper disable once InconsistentNaming
    private static bool PlayerThrottleAxis1ControlsPrefix(PilotPlayerState __instance)
    {
        var inputPlayer = __instance.player;
        var throttleInput = Mathf.Clamp(inputPlayer.GetAxisRaw("Throttle"), -1f, 1f);
        var previousThrottleInput = Mathf.Clamp(inputPlayer.GetAxisRawPrev("Throttle"), -1f, 1f);
        var axisModifier = inputPlayer.GetButton("Axis Modifier");
        var inputKind = GetThrottleInputKind(__instance);
        
        UpdateThrottle(__instance, throttleInput, previousThrottleInput, axisModifier, inputKind);
        UpdateCustomAxis1(__instance, throttleInput, previousThrottleInput, axisModifier, inputKind);
        ApplyThrottleOutput(__instance);
        
        return false;
    }
    
    // Reset throttle input state on entering new PilotPlayerState, just in case it's not cleared for any reason between instances
    [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.EnterState))]
    [HarmonyPrefix]
    private static void EnterStatePrefix()
    {
        _throttleInputState = null;
        _lastThrottleInputKind = ThrottleInputKind.None;
    }
    
    private static void UpdateThrottle(PilotPlayerState state, float current, float previous, bool axisModifier, ThrottleInputKind inputKind)
    {
        if (axisModifier)
            return;
        
        if (ShouldUseRelativeHandling(inputKind))
        {
            ApplyRelativeThrottle(state, current);
            return;
        }
        
        if (!ShouldUseAbsoluteHandling(inputKind))
            return;
        
        ApplyAbsoluteInputMode(ref state.simulatedThrottle, current, previous);
    }
    
    private static bool ShouldUseRelativeHandling(ThrottleInputKind inputKind)
    {
        if (PlayerSettings.throttleUseRelative)
            return true;
        
        // Vanilla Hybrid intentionally returns to vanilla handling
        if (_absoluteInputMode.Value == AbsoluteInputMode.VanillaHybrid)
            return false;
        
        return inputKind switch
        {
            ThrottleInputKind.IncrementalButton => true,
            ThrottleInputKind.IncrementalAxis =>
                GetAnalogueIncrementalInputMode() == AnalogueIncrementalInputMode.Relative,
            _ => false
        };
    }
    
    private static bool ShouldUseAbsoluteHandling(ThrottleInputKind inputKind)
    {
        if (_absoluteInputMode.Value == AbsoluteInputMode.VanillaHybrid)
            return true;
        
        return inputKind switch
        {
            ThrottleInputKind.FullAxis => true,
            ThrottleInputKind.IncrementalAxis =>
                GetAnalogueIncrementalInputMode() == AnalogueIncrementalInputMode.Absolute,
            _ => false
        };
    }
    
    private static void ApplyRelativeThrottle(PilotPlayerState state, float input)
    {
        var signedState = PlayerSettings.throttleUseRelative || PlayerSettings.throttleUseNegative;
        var sensitivity = GetRelativeSensitivity();
        
        // -1-1 has twice the travel of 0-1, with negative region or relative throttle we get -1-1 range instead of 0-1
        // This allows keeping the -1-1 range everywhere else including for relative throttle, and only reduce it to
        // 0-1 throttle setting when applying it at the end, accounting for different rate of change with -1-1 vs 0-1 input
        
        if (!signedState)
            sensitivity *= 0.5f;
        
        state.simulatedThrottle = ApplyRelativeInput(state.simulatedThrottle, input, sensitivity, signedState ? -1f : 0f, 1f);
    }
    
#pragma warning disable Harmony003
    private static float ApplyRelativeInput(float value, float input, float sensitivity, float min, float max)
    {
        input = GetRelativeInput(input);
        return Mathf.Clamp(value + input * sensitivity * Time.deltaTime, min, max);
        
    }
#pragma warning restore Harmony003
    
    private static void ApplyThrottleOutput(PilotPlayerState state)
    {
        var output = state.simulatedThrottle;
        
        // Relative throttle uses -1 to 1 range to represent 0-100% throttle, keeping it that way here too as it's
        // consistently used that way globally in code, only clamped when using it as actual output at the end
        
        if (PlayerSettings.throttleUseRelative || PlayerSettings.throttleUseNegative)
            output = 0.5f * (output + 1f);
        
        if (state.collective && PlayerSettings.invertCollective)
            output = 1f - output;
        
        state.controlInputs.throttle = Mathf.Clamp01(output);
    }
    
    private static void UpdateCustomAxis1(PilotPlayerState state, float throttleInput, float previousThrottleInput, bool axisModifier, ThrottleInputKind inputKind)
    {
        // When _applyThrottleModesToCustomAxis is disabled for modifier input, reproduce vanilla's combined
        // Custom Axis 1 + Throttle behaviour
        
        var useVanillaModifier = axisModifier && !_applyThrottleModesToCustomAxis.Value;
        var modifierInput = useVanillaModifier ? GetVanillaModifierInput(throttleInput) : 0f;
        
        // Directly bound Custom Axis 1 always keeps vanilla behaviour
        var output = UpdateVanillaCustomAxis1(state, modifierInput);
        
        if (!axisModifier || useVanillaModifier)
        {
            state.controlInputs.customAxis1 = output;
            return;
        }
        
        // Axis Modifier redirects throttle to Custom Axis 1
        if (ShouldUseRelativeHandling(inputKind))
        {
            output = ApplyRelativeInput(output, throttleInput, _relativeCustomAxisSensitivity.Value, 0f, 1f);
        }
        else if (ShouldUseAbsoluteHandling(inputKind))
        {
            output = ApplyAbsoluteCustomAxisInput(output, throttleInput, previousThrottleInput);
        }
        
        state.controlInputs.customAxis1 = output;
    }
    
    private static float GetVanillaModifierInput(float input)
    {
        if (!PlayerSettings.throttleUseRelative)
            return input;
        
        return Mathf.Abs(input) > 0.1f ? Mathf.Sign(input) : 0f;
    }
    
    private static float ApplyAbsoluteCustomAxisInput(float output, float current, float previous)
    {
        // Custom Axis output is stored as 0-1, while throttle absolute handling may have a signed -1-1 state
        
        var absoluteState = OutputToAbsoluteState(output);
        ApplyAbsoluteInputMode(ref absoluteState, current, previous);
        
        return AbsoluteStateToOutput(absoluteState);
    }
    
    private static float UpdateVanillaCustomAxis1(PilotPlayerState state, float modifierInput)
    {
        var inputPlayer = state.player;
        var current = Mathf.Clamp(inputPlayer.GetAxisRaw("Custom Axis 1"), -1f, 1f) + modifierInput;
        var previous = Mathf.Clamp(inputPlayer.GetAxisRawPrev("Custom Axis 1"), -1f, 1f);
        var output = state.controlInputs.customAxis1;
        
        ApplyVanillaHybrid(ref output, current, previous);
        return Mathf.Clamp01(output);
    }
    
    private static void ApplyAbsoluteInputMode(ref float value, float current, float previous)
    {
        switch (_absoluteInputMode.Value)
        {
            case AbsoluteInputMode.Direct:
                value = current;
                break;
            
            case AbsoluteInputMode.DirectIgnoreCenter:
                if (Mathf.Abs(current) > _absoluteCenterIgnoreThreshold.Value)
                    value = current;
                break;
            
            case AbsoluteInputMode.VanillaHybrid:
            default:
                ApplyVanillaHybrid(ref value, current, previous);
                break;
        }
    }
    
    private static void ApplyVanillaHybrid(ref float value, float current, float previous)
    {
        // Vanilla absolute throttle behaviour
        // Small frame to frame changes are applied directly to throttle, larger changes can switch to gradual/relative
        // The exact 0.5 change is unhandled just like vanilla's, and can result in some inputs being ignored in rare cases
        
        var difference = Mathf.Abs(current - previous);
        
        if (difference is > 0f and < 0.5f)
            value = current;
        else if (Mathf.Abs(current) > 0.5f)
            value += Mathf.Clamp(current - value, -Time.deltaTime, Time.deltaTime);
    }
    
    private static float GetRelativeInput(float input)
    {
        if (_relativeInputMode.Value != RelativeInputMode.FullRate)
            return input;
        return Mathf.Abs(input) > _relativeDeadzone.Value ? Mathf.Sign(input) : 0f;
    }
    
#pragma warning disable Harmony003
    private static float AbsoluteStateToOutput(float value)
    {
        if (PlayerSettings.throttleUseNegative)
            value = 0.5f * (value + 1f);
        return Mathf.Clamp01(value);
    }
#pragma warning restore Harmony003
    
    private static ThrottleInputKind GetThrottleInputKind(PilotPlayerState state)
    {
        if (!ReferenceEquals(_throttleInputState, state))
        {
            _throttleInputState = state;
            _lastThrottleInputKind = ThrottleInputKind.None;
        }
        
        var currentKind = GetCurrentThrottleInputKind(state.player);
        
        // When an input stops (e.g. keyboard ctrl/shift or binary gamepad button binds are no longer pressed),
        // rewired's element map returns none, which could result in a 0 input, instead we just keep the previous
        // state until it's actually changed by a different input type
        // Otherwise releasing a binary button could result in throttle jumping back to 50%
        
        if (currentKind != ThrottleInputKind.None)
            _lastThrottleInputKind = currentKind;
        
        return _lastThrottleInputKind;
    }
    
    private static ThrottleInputKind GetCurrentThrottleInputKind(Player inputPlayer)
    {
        var currentKind = ThrottleInputKind.None;
        var sources = inputPlayer.GetCurrentInputSources("Throttle");
        
        foreach (var source in sources)
        {
            var map = source.actionElementMap;
            
            if (map == null)
                continue;
            
            // Increase/Decrease Throttle gets priority if multiple types are simultaneously contributing
            // This is useful in case you have a plugged in but otherwise inactive controller constantly
            // transmitting a FullAxis state, while you're pressing some binary buttons to increase/decrease throttle
            
            if (map.ShowInField(AxisRange.Positive) || map.ShowInField(AxisRange.Negative))
            {
                // Binary Increase/Decrease always gets highest priority
                if (map.elementType == ControllerElementType.Button)
                    return ThrottleInputKind.IncrementalButton;
                
                // Analogue trigger / split-axis Increase/Decrease
                if (map.elementType == ControllerElementType.Axis)
                {
                    currentKind = ThrottleInputKind.IncrementalAxis;
                    continue;
                }
            }
            
            // Don't overwrite an already detected split analogue source
            if (map.ShowInField(AxisRange.Full) && currentKind == ThrottleInputKind.None)
                currentKind = ThrottleInputKind.FullAxis;
        }
        
        return currentKind;
    }
    
    private static float OutputToAbsoluteState(float output) =>
        PlayerSettings.throttleUseNegative ? output * 2f - 1f : output;
}
#endif
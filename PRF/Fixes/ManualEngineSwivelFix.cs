using BepInEx.Configuration;
using HarmonyLib;
using Rewired;
using UnityEngine;
using Player = NuclearOption.Networking.Player;

// ReSharper disable InconsistentNaming

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class ManualEngineSwivelFix : ConfigurableFix
{
    private const float AxisInputThreshold = 0.01f;
    private static ConfigEntry<bool> _disableLowSpeedSwivelLimit = null!;
    private static ConfigEntry<bool> _customAxisSwitchesToManual = null!;
    
    private static Aircraft? _stateAircraft;
    private static bool _manualSwivelEnabled;
    
    private static bool _axisInputInitialized;
    private static float _customAxisRawPrevious;
    private static float _throttleRawPrevious;
    private static bool _axisModifierPrevious;
    
    public ManualEngineSwivelFix(ConfigFile config) : base(config)
    {
        _disableLowSpeedSwivelLimit = config.Bind(GetType().Name, "DisableLowSpeedSwivelLimit", true,
            "Also disable the newly introduced 45 degree swivel limit in manual mode when flying slowly.");
        _customAxisSwitchesToManual = config.Bind(GetType().Name, "AllowAxisInputToManual", true,
            "Allow Custom Axis 1 input to switch auto vectoring to manual.");
    }
    
    protected override string Description =>
        "";
    
    protected override bool DefaultEnabled => false;
    
    private static bool IsManualSwivelEnabled(Aircraft aircraft)
    {
        CheckAircraftState(aircraft);
        return _manualSwivelEnabled;
    }
    
    private static void CheckAircraftState(Aircraft aircraft)
    {
        // Check if player is still in same aircraft
        if (ReferenceEquals(_stateAircraft, aircraft))
            return;
        
        _stateAircraft = aircraft;
        
        // On new aircraft, set to automatic by default
        _manualSwivelEnabled = false;
        
        _axisInputInitialized = false;
    }
    
    private static void ToggleManualSwivel(Aircraft aircraft, bool? enabled = null)
    {
        CheckAircraftState(aircraft);
        _manualSwivelEnabled = enabled ?? !_manualSwivelEnabled;
    }
    
    private static void ClearStateForAircraft(Aircraft aircraft)
    {
        if (!ReferenceEquals(_stateAircraft, aircraft))
            return;
        
        _stateAircraft = null;
        _manualSwivelEnabled = false;
        _axisInputInitialized = false;
    }
    
    [HarmonyPatch(typeof(DuctedThrustSystem), nameof(DuctedThrustSystem.ChooseOperatingMode))]
    [HarmonyPrefix]
    private static bool ChooseOperatingModePrefix(DuctedThrustSystem __instance)
    {
        var aircraft = __instance.aircraft;
        
        if (!GameManager.IsLocalAircraft(aircraft))
            return false;
        
        if (IsManualSwivelEnabled(aircraft))
        {
            __instance.SwitchMode(DuctedThrustSystem.DuctedThrustMode.Manual);
            return false;
        }
        
        // If our manual mode is off, return to automatic switch (without the new "smart" check for custom axis input)
        
        if (__instance.mode == DuctedThrustSystem.DuctedThrustMode.Manual)
            __instance.SwitchMode(DuctedThrustSystem.DuctedThrustMode.Forward);
        
        switch (__instance.mode)
        {
            case DuctedThrustSystem.DuctedThrustMode.Forward:
                __instance.ForwardMode();
                break;
            
            case DuctedThrustSystem.DuctedThrustMode.Takeoff:
                __instance.TakeoffMode();
                break;
            
            case DuctedThrustSystem.DuctedThrustMode.Hover:
                __instance.HoverMode();
                break;
            
            case DuctedThrustSystem.DuctedThrustMode.Reverse:
                __instance.ReverseMode();
                break;
        }
        
        return false;
    }
    
    [HarmonyPatch(typeof(DuctedThrustSystem), nameof(DuctedThrustSystem.Swivel))]
    [HarmonyPrefix]
    private static void SwivelPrefix(DuctedThrustSystem __instance, out SwivelPatchState __state)
    {
        // Optionally for _disableLowSpeedSwivelLimit, during manual mode, disable 45 degree limit on low speeds
        // Save snapshot of minSpeedForForward to set in Postfix, so it's not also changed for when e.g.
        // Auto mode (or something else) later uses it
        
        __state = default;
        
        if (!_disableLowSpeedSwivelLimit.Value)
            return;
        
        var aircraft = __instance.aircraft;
        
        if (!GameManager.IsLocalAircraft(aircraft) || !IsManualSwivelEnabled(aircraft))
            return;
        
        __state.Changed = true;
        __state.OriginalMinSpeed = __instance.minSpeedForForward;
        
        __instance.minSpeedForForward = float.NegativeInfinity;
    }
    
    [HarmonyPatch(typeof(DuctedThrustSystem), nameof(DuctedThrustSystem.Swivel))]
    [HarmonyPostfix]
    private static void SwivelPostfix(DuctedThrustSystem __instance, SwivelPatchState __state)
    {
        // Disable warnings for __state not being modified, yes we're just reading from it
#pragma warning disable Harmony003
        if (__state.Changed)
        {
            __instance.minSpeedForForward = __state.OriginalMinSpeed;
#pragma warning restore Harmony003
        }
    }
    
    [HarmonyPatch(typeof(SwivelDuctSystem), nameof(SwivelDuctSystem.CheckForManualInput))]
    [HarmonyPrefix]
    private static bool CheckForManualInputPrefix(SwivelDuctSystem __instance)
    {
        var aircraft = __instance.aircraft;
        
        if (!GameManager.IsLocalAircraft(aircraft))
            return true;
        
        // Disable vanilla "smart" axis checking, just hard set manual vs auto based on manual toggle state
        
        if (IsManualSwivelEnabled(aircraft))
            __instance.swivelDuctMode = SwivelDuctSystem.SwivelDuctMode.Manual;
        else if (__instance.swivelDuctMode == SwivelDuctSystem.SwivelDuctMode.Manual)
            __instance.swivelDuctMode = SwivelDuctSystem.SwivelDuctMode.Forward;
        
        return false;
    }
    
    [HarmonyPatch(typeof(Player), nameof(Player.RemoveAircraft))]
    [HarmonyPostfix]
    private static void RemoveAircraftPostfix(Aircraft aircraft)
    {
        // Aircraft state clean-up/check when player aircraft is removed
        ClearStateForAircraft(aircraft);
    }
    
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.TogglePitchLimiter))]
    [HarmonyPrefix]
    private static bool TogglePitchLimiterPrefix(Aircraft __instance)
    {
        // Hook into Aircraft.TogglePitchLimiter() to change FA button press behaviour to instead change our
        // manual mode toggle when also holding Axis Modifier, otherwise preserve default behaviour
        // (especially important as TogglePitchLimiter is also called in e.g. OnStartClient)
        
        // This one could perhaps return false since vanilla method does this same check then returns,
        // but I wanted to keep it compatible in case something else relies on/changes the vanilla method
        if (GameManager.gameState != GameState.SinglePlayer && GameManager.gameState != GameState.Multiplayer)
            return true;
        
        if (!GameManager.IsLocalAircraft(__instance))
            return true;
        
        var inputPlayer = ReInput.players.GetPlayer(0);
        
        if (inputPlayer == null)
            return true;
        
        // Make sure this part only runs when coming in from a FA button up moment
        if (!inputPlayer.GetButtonUp("Flight Assist") || !inputPlayer.GetButton("Axis Modifier") ||
            !HasSwivelSystem(__instance))
            return true;
        
        ToggleManualSwivel(__instance);
        
        return false;
    }
    
    [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.PlayerThrottleAxis1Controls))]
    [HarmonyPrefix]
    private static void PlayerThrottleAxis1ControlsPrefix(PilotPlayerState __instance)
    {
        if (!_customAxisSwitchesToManual.Value)
            return;
        
        var aircraft = __instance.pilot?.aircraft;
        
        if (aircraft == null || !GameManager.IsLocalAircraft(aircraft))
            return;
        
        CheckAircraftState(aircraft);
        
        var inputPlayer = __instance.player;
        
        if (inputPlayer == null)
            return;
        
        var customAxisRaw = Mathf.Clamp(inputPlayer.GetAxisRaw("Custom Axis 1"), -1f, 1f);
        
        var throttleRaw = Mathf.Clamp(inputPlayer.GetAxisRaw("Throttle"), -1f, 1f);
        
        var axisModifier = inputPlayer.GetButton("Axis Modifier");
        
        /*
         * Do not treat the axis's existing resting position as input when
         * entering a new aircraft. Seed our own previous values first.
         */
        if (!_axisInputInitialized)
        {
            _customAxisRawPrevious = customAxisRaw;
            _throttleRawPrevious = throttleRaw;
            _axisModifierPrevious = axisModifier;
            _axisInputInitialized = true;
            
            return;
        }
        
        var customAxisChanged =
            Mathf.Abs(customAxisRaw - _customAxisRawPrevious) > AxisInputThreshold;
        
        var modifierPressedWithThrottle =
            axisModifier && !_axisModifierPrevious && Mathf.Abs(throttleRaw) > AxisInputThreshold;
        
        var modifierThrottleChanged =
            axisModifier && Mathf.Abs(throttleRaw - _throttleRawPrevious) > AxisInputThreshold;
        
        /*
         * Always update the stored physical state, even if already manual,
         * so returning to automatic does not compare against stale inputs.
         */
        _customAxisRawPrevious = customAxisRaw;
        _throttleRawPrevious = throttleRaw;
        _axisModifierPrevious = axisModifier;
        
        if (IsManualSwivelEnabled(aircraft))
            return;
        
        if (!customAxisChanged && !modifierPressedWithThrottle && !modifierThrottleChanged)
            return;
        
        /*
         * Avoid running the component search every input invocation. It is
         * only needed when genuine physical input has changed.
         */
        if (!HasSwivelSystem(aircraft))
            return;
        
        ToggleManualSwivel(aircraft, true);
    }
    
    private static bool HasSwivelSystem(Aircraft aircraft) =>
        aircraft.GetComponentInChildren<DuctedThrustSystem>(true) != null ||
        aircraft.GetComponentInChildren<SwivelDuctSystem>(true) != null;
    
    private struct SwivelPatchState
    {
        public bool Changed;
        public float OriginalMinSpeed;
    }
}
using System.Collections.Generic;
using System.Reflection.Emit;
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
    
    private static ConfigEntry<bool> _enableLongPressToggle = null!;
    private static ConfigEntry<LongPressToggleInput> _longPressToggleInput = null!;
    
    private static Aircraft? _stateAircraft;
    private static bool _manualSwivelEnabled;
    
    private static bool _axisInputInitialized;
    private static float _customAxisRawPrevious;
    private static float _throttleRawPrevious;
    private static bool _axisModifierPrevious;
    
    public ManualEngineSwivelFix(ConfigFile config) : base(config)
    {
        _disableLowSpeedSwivelLimit = config.Bind(GetType().Name, "Disable Low Speed Swivel Limit", true,
            "Also disable the newly introduced 45 degree swivel limit in when flying slowly, in manual mode.");
        _customAxisSwitchesToManual = config.Bind(GetType().Name, "Axis Input Switches To Manual", true,
            "When enabled, player inputs on Custom Axis 1 (directly or via Throttle input when holding" +
            " \"Axis Modifier\") switches auto vectoring to manual.");
        _enableLongPressToggle = config.Bind(GetType().Name, "Enable Long Press Toggle Hotkey", false,
            "Enables an additional long press input for toggling engine vectoring.");
        _longPressToggleInput = config.Bind(GetType().Name, "Long Press Toggle Hotkey", LongPressToggleInput.Radar,
            "Existing input whose long press action toggles engine vectoring, when EnableLongPressToggle is enabled." +
            " Its normal short press action remains unchanged.");
    }
    
    protected override string Description =>
        "When enabled, allows overriding engine swivel system to be toggled between auto vs fully manual, without" +
        " the game trying to be \"smart\" about it and change engine vector whenever it feels like it. In manual mode," +
        " the swivel will always stay where player points it, unless specifically toggled back to auto mode." +
        "\n\nToggle by holding \"Axis Modifier\" and press \"Toggle Flight Assist\" (this will only toggle" +
        " engine vectoring mode, not flight assist itself, to allow toggling FA and engine vector mode separately)." +
        "\n\nOptionally, can disable 45 degree swivel limit on low speeds when on manual mode, and auto toggling to manual" +
        " vectoring when player inputs on Custom Axis 1 in auto mode, instead of needing to toggle it to manual first" +
        " (both enabled by default).\n\nThis engine vectoring fix is applicable to both swivel duct system (Vagrant," +
        " Medusa), and ducted thrust system craft (Vortex). Does not affect tilt-wing (e.g. Tarantula) or wing sweep" +
        " (e.g. Alkyon).";
    
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
    
    private static bool TrySetManualSwivel(Aircraft? aircraft, bool? enabled = null)
    {
        if (aircraft == null || !GameManager.IsLocalAircraft(aircraft))
            return false;
        
        // Clean up / potentially reset aircraft state before checking for swivel system
        CheckAircraftState(aircraft);
        
        if (!HasSwivelSystem(aircraft))
            return false;
        
        _manualSwivelEnabled = enabled ?? !_manualSwivelEnabled;
        
        return true;
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
        
        var inputPlayer = ReInput.players.GetPlayer(0);
        
        // Make sure this part only runs when coming in from a FA button up moment
        if (inputPlayer == null || !inputPlayer.GetButtonUp("Flight Assist") || !inputPlayer.GetButton("Axis Modifier"))
            return true;
        
        return !TrySetManualSwivel(__instance);
    }
    
    [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.PlayerThrottleAxis1Controls))]
    [HarmonyPrefix]
    private static void PlayerThrottleAxis1ControlsPrefix(PilotPlayerState __instance)
    {
        // Initial structure of PlayerThrottleAxis1ControlsPrefix was based on AI assisted suggestion on how to
        // approach checking for Custom Axis modification detection to disengage auto mode
        // Also added an AxisInputThreshold to filter out extremely minute changes (erroneous inputs from jitter)
        
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
        
        // Keep updating stored input even in manual to prevent toggling mode from returning to some very old,
        // drastically different state first
        _customAxisRawPrevious = customAxisRaw;
        _throttleRawPrevious = throttleRaw;
        _axisModifierPrevious = axisModifier;
        
        if (IsManualSwivelEnabled(aircraft))
            return;
        
        if (!customAxisChanged && !modifierPressedWithThrottle && !modifierThrottleChanged)
            return;
        
        TrySetManualSwivel(aircraft, true);
    }
    
    [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.PlayerControls))]
    [HarmonyPrefix]
    private static void PlayerControlsPrefix(PilotPlayerState __instance)
    {
        if (!_enableLongPressToggle.Value || !GameManager.flightControlsEnabled || __instance.pilotStrength < 0.2f)
            return;
        
        var inputButtonName = GetLongPressInputName();
        
        // In case it's set to night vision which returns a null
        if (inputButtonName == null)
            return;
        
        var inputPlayer = __instance.player;
        
        if (inputPlayer == null || !inputPlayer.GetButtonTimedPressDown(inputButtonName, PlayerSettings.pressDelay))
            return;
        
        TrySetManualSwivel(__instance.pilot?.aircraft);
    }
    
    [HarmonyPatch(typeof(NightVision), nameof(NightVision.Update))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> NightVisionUpdateTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        
        var getButtonDown =
            AccessTools.Method(typeof(Rewired.Player), nameof(Rewired.Player.GetButtonDown), [typeof(string)]);
        var replacementMethod = AccessTools.Method(typeof(ManualEngineSwivelFix), nameof(HandleNightVisionInput));
        
        matcher.MatchForward(true,
            new CodeMatch(OpCodes.Ldstr, "Night Vis"),
            new CodeMatch(ci => ci.Calls(getButtonDown))
        ).ThrowIfInvalid("Couldn't find NightVision.cs pattern.");
        
        matcher.SetInstruction(new CodeInstruction(OpCodes.Call, replacementMethod));
        
        return matcher.InstructionEnumeration();
    }
    
    private static bool HandleNightVisionInput(Rewired.Player inputPlayer, string inputName)
    {
        // Vanilla behaviour when off / not set to use NV as long press
        if (!_enableLongPressToggle.Value || _longPressToggleInput.Value != LongPressToggleInput.NightVision)
            return inputPlayer.GetButtonDown(inputName);
        
        // Short press still toggles NV
        if (inputPlayer.GetButtonTimedPressUp(inputName, 0f, PlayerSettings.clickDelay))
            return true;
        
        // Long press instead toggles engine vectoring
        if (!inputPlayer.GetButtonTimedPressDown(inputName, PlayerSettings.pressDelay))
            return false;
        
        if (GameManager.GetLocalAircraft(out var aircraft))
            TrySetManualSwivel(aircraft);
        
        return false;
    }
    
    private static string? GetLongPressInputName()
    {
        return _longPressToggleInput.Value switch
        {
            LongPressToggleInput.Radar => "Radar",
            LongPressToggleInput.LinkGuns => "Link Guns",
            LongPressToggleInput.NavLights => "Nav Lights",
            // Night vision is in NightVision.Update
            _ => null
        };
    }
    
    private static bool HasSwivelSystem(Aircraft aircraft) =>
        aircraft.GetComponentInChildren<DuctedThrustSystem>(true) != null ||
        aircraft.GetComponentInChildren<SwivelDuctSystem>(true) != null;
    
    private struct SwivelPatchState
    {
        public bool Changed;
        public float OriginalMinSpeed;
    }
    
    private enum LongPressToggleInput
    {
        Radar,
        NightVision,
        LinkGuns,
        NavLights
    }
}
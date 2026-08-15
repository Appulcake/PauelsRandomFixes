#if CLIENT
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
// ReSharper disable once InconsistentNaming
internal class LockedMapControlsWithVJFix : ConfigurableFix
{
    private static ConfigEntry<bool> _freezeVjOnMapOpen = null!;
    private static ConfigEntry<bool> _restoreVjPosOnMapClose = null!;
    
    private static Vector3 _mapOpenVjPosition;
    private static bool _hasMapOpenVjPosition;
    
    public LockedMapControlsWithVJFix(ConfigFile config) : base(config)
    {
        _freezeVjOnMapOpen = config.Bind(GetType().Name, "Freeze VJ when map is open", false,
            "This'll lock last VJ input when map is open, continuing last VJ input until map is closed" +
            " (other control inputs still work during this, just VJ stays active without centering).");
        
        _restoreVjPosOnMapClose = config.Bind(GetType().Name, "Restore VJ position when map closes", false,
            "Restores Virtual Joystick to the position it had when the map was opened.\n" +
            " When Freeze VJ is disabled, the VJ stays centered while the map is open, then returns to its previous position on close.");
    }
    
    protected override string Description =>
        $"{base.Description}\nFixes locked and stuck controls with map open when Virtual Joystick is enabled.";
    
    [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.PlayerAxisControls))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> AllowControlsWithMapOpen(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        
        matcher.MatchForward(
            false,
            new CodeMatch(OpCodes.Call, ReusedRefs.MapMaximizedGetter),
            new CodeMatch(ci => ci.opcode == OpCodes.Brtrue || ci.opcode == OpCodes.Brtrue_S),
            new CodeMatch(OpCodes.Call, ReusedRefs.RadialMenuInUse)
        );
        
        if (!matcher.IsValid)
        {
            PRF.Logger.LogError("LockedMapControlsWithVJFix: Could not find match for map/radial in use condition.");
            
            return matcher.InstructionEnumeration();
        }
        
        // Disable map maximised check to prevent ceasing and locking inputs during map
        matcher.Set(OpCodes.Ldc_I4_0, null);
        
        return matcher.InstructionEnumeration();
    }
    
    [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.Maximize))]
    [HarmonyPrefix]
    private static void CaptureVjOnMapOpen()
    {
        if (!DynamicMap.AllowedToOpen || DynamicMap.mapMaximized || !PlayerSettings.virtualJoystickEnabled ||
            SceneSingleton<FlightHud>.i == null)
            return;
        
        _mapOpenVjPosition = SceneSingleton<FlightHud>.i.virtualJoystickPos.transform.localPosition;
        
        _hasMapOpenVjPosition = true;
        
        if (!_freezeVjOnMapOpen.Value)
            SceneSingleton<FlightHud>.i.SetVirtualJoystick(Vector3.zero);
    }
    
    [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.PlayerAxisControls))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void ApplyFrozenVjWithMapOpen(PilotPlayerState __instance)
    {
        if (!_freezeVjOnMapOpen.Value || !_hasMapOpenVjPosition || !DynamicMap.mapMaximized ||
            !PlayerSettings.virtualJoystickEnabled || __instance.pilot.aircraft == null ||
            __instance.pilot.aircraft.cockpit.IsDetached() || __instance.pilotStrength < 0.2f)
            return;
        
        __instance.pitchInput += -_mapOpenVjPosition.y / 150f;
        __instance.rollInput += _mapOpenVjPosition.x / 150f;
        
        if (__instance.pilot.aircraft.radarAlt < __instance.pilot.aircraft.definition.spawnOffset.y + 1f)
            __instance.yawInput += _mapOpenVjPosition.x / 150f;
        
        __instance.controlInputs.pitch = Mathf.Clamp(__instance.pitchInput, -1f, 1f);
        __instance.controlInputs.roll = Mathf.Clamp(__instance.rollInput, -1f, 1f);
        __instance.controlInputs.yaw = Mathf.Clamp(__instance.yawInput, -1f, 1f);
    }
    
    [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.Minimize))]
    [HarmonyPostfix]
    private static void RestoreVjAfterMapClose()
    {
        if (!_hasMapOpenVjPosition)
            return;
        
        if (SceneSingleton<FlightHud>.i != null)
        {
            if (PlayerSettings.virtualJoystickEnabled && (_freezeVjOnMapOpen.Value || _restoreVjPosOnMapClose.Value))
                SceneSingleton<FlightHud>.i.SetVirtualJoystick(_mapOpenVjPosition);
            else
                SceneSingleton<FlightHud>.i.SetVirtualJoystick(Vector3.zero);
        }
        
        _hasMapOpenVjPosition = false;
    }
    
    private static class ReusedRefs
    {
        internal static readonly MethodInfo MapMaximizedGetter =
            AccessTools.PropertyGetter(typeof(DynamicMap), nameof(DynamicMap.mapMaximized));
        
        internal static readonly MethodInfo RadialMenuInUse =
            AccessTools.Method(typeof(RadialMenuMain), nameof(RadialMenuMain.IsInUse));
    }
}
#endif
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
// ReSharper disable InconsistentNaming

namespace PRF.Fixes;

[Fix]
[HarmonyPatch(typeof(DynamicMap))]
internal class LockedMapControlsWithVJFix(ConfigFile config) : ConfigurableFix(config)
{
    protected override string Description =>
        $"{base.Description}\nFixes locked and stuck controls with map open when Virtual Joystick is enabled.";
    
    // Track whether we turned it off or not, to cover edge case of:
    //  VJ is off => player opens map => manually turns VJ on in settings => closes map, make sure it stays on
    // Instead of tracking what it was on map open and returning it to the off state on close
    // (which would ignore player turning it on during map open)
    private static bool _wasTurnedOffByMap;

    private static void ToggleVJ(bool enabled)
    {
        if (PlayerSettings.virtualJoystickEnabled == enabled) return;
        PlayerSettings.virtualJoystickEnabled = enabled;
        PlayerPrefs.SetInt("VirtualJoystickEnabled", enabled ? 1 : 0);
        // Reset VJ to center, since its centering force on HUD pauses while map is open, so on map close it'd snap to previous vector
        if (SceneSingleton<FlightHud>.i != null)
            SceneSingleton<FlightHud>.i.SetVirtualJoystick(Vector3.zero);
    }

    // Transparent prefixes on map open and close actions to trigger setting _wasTurnedOffByMap and ToggleVJ()
    // under the same circumstances as the map will be opened/closed after this
    
    [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.Maximize))]
    [HarmonyPrefix]
    public static void DynamicMapMaximise()
    {
        if (!DynamicMap.AllowedToOpen || DynamicMap.mapMaximized || !PlayerSettings.virtualJoystickEnabled) return;
        
        _wasTurnedOffByMap = true;
        ToggleVJ(false);
    }
    
    [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.Minimize))]
    [HarmonyPrefix]
    public static void DynamicMapMinimise()
    {
        if (!DynamicMap.mapMaximized || !_wasTurnedOffByMap) return;

        _wasTurnedOffByMap = false;
        ToggleVJ(true);
    }
}
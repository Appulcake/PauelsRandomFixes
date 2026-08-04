#if CLIENT
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class TargetDesignatorFix : ConfigurableFix
{
    private static ConfigEntry<bool> _alwaysShowIcon = null!;
    private static ConfigEntry<bool> _fadeWhenSafetyOn = null!;
    private static ConfigEntry<float> _opacitySafetyOn = null!;
    
    private static Image _cachedDesignator = null!;
    private static CanvasGroup _opacityGroup = null!;
    
    public TargetDesignatorFix(ConfigFile config) : base(config)
    {
        _alwaysShowIcon = config.Bind(GetType().Name, "Always Show Icon", true,
            "Always show Target Designator icon, even when the selected weapon's safety is on.");
        _fadeWhenSafetyOn = config.Bind(GetType().Name, "Fade Icon When Safety Is On", false,
            "Fade the Target Designator icon while weapon safety is on.");
        _opacitySafetyOn = config.Bind(GetType().Name, "Icon Opacity With Safety On", 0.25f,
            new ConfigDescription(
                "Target Designator icon opacity while weapon safety is on and FadeIconWhenSafetyOn is enabled.",
                new AcceptableValueRange<float>(0f, 1f)));
    }
    
    protected override string Description =>
        "When enabled, fixes Target Designator indicator on center of screen inconsistently showing depending on weapon" +
        " selected, and not properly updating on gear-up state until weapons are swapped.";
    
    [HarmonyPatch(typeof(CombatHUD), nameof(CombatHUD.LateUpdate))]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(CombatHUD __instance)
    {
        var designator = __instance.targetDesignator;
        if (designator == null)
            return;
        
        VerifyIcon(designator);
        
        var aircraft = __instance.aircraft;
        
        // Do not carry the previous aircraft's HUD state into spectating,
        // menus, or the next aircraft.
        if (aircraft == null)
        {
            designator.enabled = false;
            _opacityGroup.alpha = 1f;
            return;
        }
        
        // Landing gear full-up state (default vanilla check)
        var gearFullyRetracted = aircraft.gearState == LandingGear.GearState.LockedRetracted;
        
        // Weapon station's weapon safety on as primary check, in case something affects when safety is on or not
        var station = __instance.GetWeaponStation();
        
        // Use weapon safety as primary check and only fall-back to gear up check if that fails
        var safetyOn = station?.SafetyIsOn(aircraft) ?? !gearFullyRetracted;
        
        // Account for HUDSlingState's own behaviour where it shows/hides it depending on sling state
        var slingSuppressed = __instance.weaponState is HUDSlingState && !designator.enabled;
        
        var shouldShowIcon = _alwaysShowIcon.Value || !safetyOn;
        
        designator.enabled = shouldShowIcon && !slingSuppressed;
        
        // Additional optional fading out effect if _fadeWhenSafetyOn is on
        _opacityGroup.alpha = shouldShowIcon && _fadeWhenSafetyOn.Value && safetyOn ? _opacitySafetyOn.Value : 1f;
    }
    
    private static void VerifyIcon(Image designator)
    {
        if (_cachedDesignator == designator && _opacityGroup != null)
            return;
        
        _cachedDesignator = designator;
        
        _opacityGroup = designator.GetComponent<CanvasGroup>() ?? designator.gameObject.AddComponent<CanvasGroup>();
        _opacityGroup.interactable = false;
        _opacityGroup.blocksRaycasts = false;
    }
}
#endif
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking;

// ReSharper disable InconsistentNaming

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class WarheadDesyncFix(ConfigFile config) : ConfigurableFix(config)
{
    private static FactionHQ? _prioritisedWarheadDistribution;
    
    protected override string Description =>
        $"{base.Description}\nPrevents disabled warhead storages from receiving warheads, which causes a situation " +
        "where clients could show a higher available warhead count on an airbase than what's effectively available. " +
        "(That consequence is because a warhead storage's selfDisabled state doesn't sync from server to client, so " +
        "if a selfDisabled storage has nukes in it, the client will still count them while the server doesn't. With " +
        "this fix, selfDisabled storages wouldn't erroneously have inaccessible nukes in them to begin with.)\n\n" +
        //
        "Also de-prioritises putting warheads into airbases that don't have at least one funtional non-helipad hangar, " +
        "otherwise an airbase with valid warhead storage can still receive nukes which'll be inaccessible (as you " +
        "either can't spawn at them at all, or only as helicopter). If no such better airbase target exists at all, " +
        "it falls back to being able to place them at such bases (so the nuke doesn't just disappear). This also " +
        "doesn't apply to intentional player ferried warheads, only to freshly produced ones.";
    
    [HarmonyPatch(typeof(Airbase), nameof(Airbase.HasStorage))]
    [HarmonyPrefix]
    private static bool HasStoragePrefix(Airbase __instance, ref bool __result)
    {
        if (!NetworkManagerNuclearOption.i.Server.Active)
            return true;
        
        // Vanilla HasStorage() only checks whether the airbase has any WarheadStorage components,
        // not whether any of them are currently selfDisabled/accessible
        // Warhead distribution and AddWarheads() use this check before choosing a storage
        //
        // If every storage at an airbase is disabled, vanilla AddWarheads() still tries to distribute warheads here
        // Since no valid storage is found, it'll just try to add it to index 0 and thus warhead is added to stores[0]
        // regardless of whether it's disabled or not
        //
        // Returning false here instead follows vanilla's existing "no storage here" path, which causes the warheads
        // to redistribute through the faction rather than inserting them into an inaccessible storage
        // This accessibility state of selfDisabled doesn't sync to clients, so they still thought these are available
        // to use, but even if they saw them as unavailable, this'd result in a lot of the faction's nukes going into
        // a completely inaccessible storage
        
        __result = HasUsableStorage(__instance);
        
        // De-prioritise warhead placement in airbases without a usable/not disabled, non-helipad hangar
        // Otherwise warheads can get placed in airbases with no hangars (so can't even spawn) or helipad only hangars
        // (helis can't carry nukes), making them inaccessible
        //
        // This is done by temporarily marking all airbases that don't have at least one usable non-helipad hangar as
        // if they don't have any usable storage space, so they can't become a warhead destination (but only if at least
        // one airbase on the map does have a valid non-helipad hangar, if no airbases have them it returns to previous
        // behaviour where such airbases can still get warheads, so it doesn't just disappear)
        //
        // This intentionally only applies to fresh produced warheads, if someone intentionally ferries some warheads
        // from another airbase to such an airbase and disembarks, they can still be deposited here just fine
        
        if (__result && _prioritisedWarheadDistribution != null &&
            _prioritisedWarheadDistribution.ContainsAirbase(__instance) &&
            !HasUsableNonHelipadHangar(__instance)) __result = false;
        
        return false;
    }
    
    [HarmonyPatch(typeof(WarheadStorage), nameof(WarheadStorage.Disable))]
    [HarmonyPostfix]
    private static void DisablePostfix(WarheadStorage __instance)
    {
        // Vanilla's storage check doesn't account for a possible Airbase == null situation and still returns as a
        // valid warhead storage, even if all storages there are disabled
        
        // Vanilla only clears the stored warheads if attachedUnit.GetAirbase() succeeds
        // If that lookup is null when the storage is disabled, selfDisabled becomes true, while the number can remain
        // positive, creating the same invalid state this fix aims to prevent
        
        if (!NetworkManagerNuclearOption.i.Server.Active)
            return;
        
        if (__instance is { selfDisabled: true, number: > 0 })
            __instance.Networknumber = 0;
    }
    
    [HarmonyPatch(typeof(FactionHQ), nameof(FactionHQ.AddWarheadStockpile))]
    [HarmonyPrefix]
    private static void AddWarheadStockpilePrefix(FactionHQ __instance)
    {
        if (!NetworkManagerNuclearOption.i.Server.Active)
            return;
        
        _prioritisedWarheadDistribution = __instance.GetAirbases()
            .Any(airbase => HasUsableStorage(airbase) && HasUsableNonHelipadHangar(airbase))
            ? __instance
            : null;
    }
    
    [HarmonyPatch(typeof(FactionHQ), nameof(FactionHQ.AddWarheadStockpile))]
    [HarmonyFinalizer]
    private static void AddWarheadStockpileFinalizer()
    {
        _prioritisedWarheadDistribution = null;
    }
    
    private static bool HasUsableStorage(Airbase airbase)
    {
        return airbase.stores.Any(storage => storage != null && !storage.Disabled);
    }
    
    private static bool HasUsableNonHelipadHangar(Airbase airbase)
    {
        if (airbase.disabled)
            return false;
        
        return airbase.hangars.Any(hangar =>
            hangar != null && !hangar.Disabled && hangar.attachedUnit != null &&
            hangar.attachedUnit.definition != null && hangar.attachedUnit.definition.code != "HPAD");
    }
}
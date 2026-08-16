using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class WarheadDesyncFix(ConfigFile config) : ConfigurableFix(config)
{
    protected override string Description =>
        $"{base.Description}\nPrevents disabled warhead storages from receiving warheads, which causes a situation " +
        "where clients could show a higher available warhead count on an airbase than what's effectively available. " +
        "(That consequence is because a warhead storage's selfDisabled state doesn't sync from server to client, so " +
        "if a selfDisabled storage has nukes in it, the client will still count them while the server doesn't. With " +
        "this fix, selfDisabled storages wouldn't erroneously have inaccessible nukes in them to begin with.)";
    
    [HarmonyPatch(typeof(Airbase), nameof(Airbase.HasStorage))]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    private static bool HasStoragePrefix(Airbase __instance,
        // ReSharper disable once InconsistentNaming
        ref bool __result)
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
        
        __result = __instance.stores.Any(storage => !storage.Disabled);
        return false;
    }
    
    [HarmonyPatch(typeof(WarheadStorage), nameof(WarheadStorage.Disable))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
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
}
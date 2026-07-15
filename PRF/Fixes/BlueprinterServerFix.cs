using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Blueprinter;
using HarmonyLib;
using Mirage;
using UnityEngine;
using UnityEngine.Rendering;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class BlueprinterServerFix(ConfigFile config) : ConfigurableFix(config)
{
    protected override string Description =>
        $"{base.Description}\nFixes Blueprinter's prefabHash reassignments from being reset/cleaned up before full game load is complete, fixing various prefab mix-up issues with too many BP content mods.";
    
    private static readonly Dictionary<int, NetworkIdentity> SavedNetworkIdentities = new();
    
    [HarmonyPatch(typeof(PrefabHashAssigner), nameof(PrefabHashAssigner.AssignFromBundles))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void AssignFromBundlesPostfix(IReadOnlyDictionary<string, LoadedBundle> bundles)
    {
        // Check if it's running on a dedicated/headless server, no need to do this on client
        var dedicatedServer = Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
        
        if (!dedicatedServer || bundles == null)
        {
            PRF.Logger.LogDebug($"[BlueprinterServerFix] Not on dedicated server, exiting patch.");
            return;
        }
        
        var savedIdentities = 0;
        
        foreach (var loadedBundle in bundles.Values)
        {
            // Don't judge I'm trying my best to guard it from null in every way
            
            if (loadedBundle?.AssetBundle == null)
                continue;
            
            var assetBundle = loadedBundle?.AssetBundle;
            
            if (assetBundle == null)
                continue;
            
            GameObject[] topLevelEntries;
            
            try
            {
                topLevelEntries = assetBundle.LoadAllAssets<GameObject>() ?? Array.Empty<GameObject>();
            }
            catch (Exception ex)
            {
                // Yet it still complained about it being possibly null here
                
                PRF.Logger.LogError(
                    $"[BlueprinterServerFix] Failed to save prefabs from \"{loadedBundle?.filePath}\": {ex.Message}");
                
                continue;
            }
            
            foreach (var entry in topLevelEntries)
            {
                if (!entry)
                    continue;
                
                var identities = entry.GetComponentsInChildren<NetworkIdentity>(true);
                
                foreach (var identity in identities)
                {
                    if (!identity)
                        continue;
                    
                    var instanceID = identity.GetInstanceID();
                    
                    if (SavedNetworkIdentities.ContainsKey(instanceID))
                        continue;
                    
                    SavedNetworkIdentities.Add(instanceID, identity);
                    savedIdentities++;
                }
            }
        }
        
        PRF.Logger.LogDebug(
            $"[BlueprinterServerFix] Saved {savedIdentities} new NetworkIdentity prefabs; {SavedNetworkIdentities.Count} total.");
    }
}
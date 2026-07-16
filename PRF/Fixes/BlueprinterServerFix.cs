using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using Mirage;
using UnityEngine;
using UnityEngine.Rendering;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class BlueprinterServerFix(ConfigFile config) : ConfigurableFix(config)
{
    private static readonly Dictionary<int, NetworkIdentity> SavedNetworkIdentities = new();
    
    protected override string Description =>
        $"{base.Description}\nFixes Blueprinter's prefabHash reassignments from being reset/cleaned up before full game load is complete, fixing various prefab mix-up issues with too many BP content mods.";
    
    // Prepare + TargetMethod to check whether Blueprinter even exists
    [HarmonyPrepare]
    [UsedImplicitly]
    private static bool Prepare() => AccessTools.TypeByName("Blueprinter.PrefabHashAssigner") != null;
    
    [HarmonyTargetMethod]
    [UsedImplicitly]
    private static MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName("Blueprinter.PrefabHashAssigner");
        
        return type == null
            ? null
            : AccessTools.Method(type, "AssignFromBundles");
    }
    
    [HarmonyPostfix]
    [UsedImplicitly]
    // ReSharper disable once InconsistentNaming
    private static void AssignFromBundlesPostfix(object __0)
    {
        // Check if it's running on a dedicated/headless server, no need to do this on client
        var dedicatedServer = Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
        
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (!dedicatedServer || __0 == null)
        {
            PRF.Logger.LogDebug("[BlueprinterServerFix] Not on dedicated server, exiting patch.");
            return;
        }
        
        // Check whether AssignFromBundles and its LoadedBundle bundles exists/is accessible
        var valuesProperty = AccessTools.Property(__0.GetType(), "Values");
        
        if (valuesProperty?.GetValue(__0) is not IEnumerable bundles)
        {
            PRF.Logger.LogDebug("[BlueprinterServerFix] Could not read Blueprinter bundles.");
            return;
        }
        
        var savedIdentities = 0;
        
        foreach (var loadedBundle in bundles)
        {
            if (loadedBundle == null)
                continue;
            
            var assetBundle =
                AccessTools.Field(loadedBundle.GetType(), "AssetBundle")?.GetValue(loadedBundle) as AssetBundle;
            
            if (assetBundle == null)
                continue;
            
            GameObject[] topLevelEntries;
            
            try
            {
                topLevelEntries = assetBundle.LoadAllAssets<GameObject>() ?? Array.Empty<GameObject>();
            }
            catch (Exception ex)
            {
                var filePath =
                    AccessTools.Field(loadedBundle.GetType(), "filePath")?.GetValue(loadedBundle)?.ToString() ??
                    "<Unknown Filepath>";
                
                PRF.Logger.LogError(
                    $"[BlueprinterServerFix] Failed to save prefabs from \"{filePath}\": {ex.Message}");
                
                continue;
            }
            
            foreach (var entry in topLevelEntries)
            {
                if (!entry)
                    continue;
                
                var identities = entry.GetComponentsInChildren<NetworkIdentity>(true);
                
                // Save all NetworkIdentities to SavedNetworkIdentities that have their prefabHash changed
                // this keeps them from getting cleared by Unity which'd revert them to original and incorrect prefabHash
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
        
        PRF.Logger.LogInfo(
            $"[BlueprinterServerFix] Saved {savedIdentities} new NetworkIdentity prefabs; {SavedNetworkIdentities.Count} total.");
    }
}
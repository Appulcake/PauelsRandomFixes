using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class ExplosionKillAttributionFix(ConfigFile config) : ConfigurableFix(config)
{
    private const float AttributionWindow = 0.5f;
    private const float ExtraMatchRadius = 2f;
    private const float MinimumMatchRadius = 4f;
    private static readonly List<RecentKill> RecentKills = new(16);
    
    protected override string Description =>
        $"{base.Description}\n" +
        "Attributes kills from secondary explosions on a unit's death to the unit that killed the original exploding " +
        "target.\nFor example, when player A kills an ammo box that then explodes and kills another unit, that " +
        "other unit's death will get attributed to player A.";
    
    private static bool ServerActive =>
        NetworkManagerNuclearOption.i != null && NetworkManagerNuclearOption.i.Server.Active;
    
    [HarmonyPatch(typeof(MessageManager), nameof(MessageManager.RpcKillMessage))]
    [HarmonyPrefix]
    private static void RpcKillMessagePrefix(PersistentID killerID, PersistentID killedID)
    {
#pragma warning disable Harmony003
        if (!ServerActive || !killerID.IsValid || !killedID.IsValid || killerID == killedID)
#pragma warning restore Harmony003
            return;
        
        if (!UnitRegistry.TryGetPersistentUnit(killedID, out var persistentUnit) || persistentUnit == null ||
            persistentUnit.unit == null) return;
        
        RecordKill(persistentUnit.unit, killedID, killerID);
    }
    
    [HarmonyPatch(typeof(Shockwave), nameof(Shockwave.Start))]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    private static void ShockwaveStartPrefix(Shockwave __instance,
        // ReSharper disable once InconsistentNaming
        ref PersistentID ___ownerID)
    {
        if (!ServerActive || __instance == null)
            return;
        
        var previousOwner = ___ownerID;
        
        if (!TryResolveOwner(__instance.transform.position, previousOwner, out var killerID))
            return;
        
        ___ownerID = killerID;
    }
    
    private static void RecordKill(Unit sourceUnit, PersistentID killedID, PersistentID killerID)
    {
        var now = Time.timeSinceLevelLoad;
        RemoveOldEntries(now);
        
        var radius = Mathf.Max(MinimumMatchRadius, sourceUnit.maxRadius + ExtraMatchRadius);
        var record = new RecentKill(killedID, killerID, sourceUnit.transform.position, radius * radius, now);
        
        for (var i = RecentKills.Count - 1; i >= 0; i--)
        {
            if (RecentKills[i].KilledID != killedID)
                continue;
            
            RecentKills[i] = record;
            return;
        }
        
        RecentKills.Add(record);
    }
    
    private static bool TryResolveOwner(Vector3 shockwavePosition, PersistentID currentOwner, out PersistentID killerID)
    {
        killerID = PersistentID.None;
        
        var now = Time.timeSinceLevelLoad;
        RemoveOldEntries(now);
        
#pragma warning disable Harmony003
        if (currentOwner.IsValid)
#pragma warning restore Harmony003
        {
            for (var i = RecentKills.Count - 1; i >= 0; i--)
            {
                var record = RecentKills[i];
                
                if (record.KilledID != currentOwner)
                    continue;
                
                var distanceSqr = (record.Position - shockwavePosition).sqrMagnitude;
                
                if (distanceSqr > record.MatchRadiusSqr)
                    continue;
                
                killerID = record.KillerID;
                return true;
            }
            
            return false;
        }
        
        var closestDistanceSqr = float.PositiveInfinity;
        var closestIndex = -1;
        
        for (var i = RecentKills.Count - 1; i >= 0; i--)
        {
            var record = RecentKills[i];
            var distanceSqr = (record.Position - shockwavePosition).sqrMagnitude;
            
            if (distanceSqr > record.MatchRadiusSqr || distanceSqr >= closestDistanceSqr) continue;
            
            closestDistanceSqr = distanceSqr;
            closestIndex = i;
        }
        
        if (closestIndex < 0)
            return false;
        
        var closest = RecentKills[closestIndex];
        
        killerID = closest.KillerID;
        
        return true;
    }
    
    private static void RemoveOldEntries(float now)
    {
        for (var i = RecentKills.Count - 1; i >= 0; i--)
        {
            if (now - RecentKills[i].Time <= AttributionWindow)
                continue;
            
            RecentKills.RemoveAt(i);
        }
    }
    
    private readonly struct RecentKill
    {
        internal readonly PersistentID KilledID;
        internal readonly PersistentID KillerID;
        internal readonly Vector3 Position;
        internal readonly float MatchRadiusSqr;
        internal readonly float Time;
        
        internal RecentKill(PersistentID killedID, PersistentID killerID, Vector3 position, float matchRadiusSqr,
            float time)
        {
            KilledID = killedID;
            KillerID = killerID;
            Position = position;
            MatchRadiusSqr = matchRadiusSqr;
            Time = time;
        }
    }
}
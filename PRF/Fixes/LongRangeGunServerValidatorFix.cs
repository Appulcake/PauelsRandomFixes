using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class LongRangeGunServerValidatorFix(ConfigFile config) : ConfigurableFix(config)
{
    // Max 30 seconds to track a bullet (~2000m/s railgun projectile with drag goes out to ~60km max that way)
    private const float ExtendedSnapshotLifetime = 30f;
    
    // Same 0.1 snapshot interval as vanilla, helps limit tracking with very rapid firing guns
    private const float SnapshotInterval = 0.1f;
    
    private const float VanillaMaximumDistance = 3000f;
    private const float AbsoluteMaximumDistance = 60000f;
    
    private const float MaximumAngle = 10f;
    private const float TimeTolerance = 3f;
    private const float DistanceTolerance = 3000f;
    private const float MaximumReportedSpeedMultiplier = 3f;
    
    private static readonly Dictionary<PersistentID, List<ExtendedFiringSnapshot>> ExtendedFiringLogs = new();
    
    protected override string Description =>
        $"{base.Description}\nFixes server rejecting client player bullet impact calls from shots further than 3000m" +
        " away (game has a hardcoded 3000m or 5s bullet limit for HitPlausible), so that player controlled long range" +
        " guns (e.g. railguns) can do proper damage when playing on a server. Only needed on server's side.";
    
    [HarmonyPatch(typeof(HitValidator), nameof(HitValidator.LogFiring))]
    [HarmonyPostfix]
    private static void LogExtendedFiringSnapshot(PersistentID __0, Vector3 __1, Vector3 __2)
    {
        var shooterID = __0;
        var position = __1;
        var velocity = __2;
        
        var now = Time.timeSinceLevelLoad;
        
        if (!ExtendedFiringLogs.TryGetValue(shooterID, out var snapshots))
        {
            snapshots = new List<ExtendedFiringSnapshot>();
            ExtendedFiringLogs.Add(shooterID, snapshots);
        }
        
        // At most 10 per second (0.1 interval) shots make it into here to get added to tracked shots dictionary
        if (snapshots.Count > 0)
        {
            var latest = snapshots[snapshots.Count - 1];
            
            if (now - latest.Timestamp < SnapshotInterval)
                return;
        }
        
        // Only run this when adding a new record
        RemoveExpiredSnapshots(snapshots, now);
        snapshots.Add(new ExtendedFiringSnapshot(position, velocity, now));
    }
    
    [HarmonyPatch(typeof(HitValidator), nameof(HitValidator.HitValidated))]
    [HarmonyPostfix]
    private static void ExtendLongRangeHitValidation(Unit claimer, Vector3 hitPosition, Vector3 hitVelocity,
        // ReSharper disable once InconsistentNaming
        ref bool __result)
    {
        // Ignore gun hits already handled and validated by vanilla HitValidator
        if (__result || claimer == null)
            return;
        
        if (!ExtendedFiringLogs.TryGetValue(claimer.persistentID, out var snapshots))
            return;
        
        var now = Time.timeSinceLevelLoad;
        
        RemoveExpiredSnapshots(snapshots, now);
        
        if (snapshots.Count == 0)
        {
            ExtendedFiringLogs.Remove(claimer.persistentID);
            return;
        }
        
        // This value should be identical for every possible snapshot, so calculate it only once
        // Harmony non-ref patch warning I believe is incorrect, as no values are being modified here, only read
        var reportedHitSpeed = hitVelocity.magnitude;
        
        for (var i = snapshots.Count - 1; i >= 0; i--)
        {
            // Check with our own ExtendedHitPlausible whether the hit would be plausible and possibly overrule validation
            if (!ExtendedHitPlausible(snapshots[i], hitPosition, reportedHitSpeed, now))
                continue;
            
            __result = true;
            return;
        }
    }
    
    private static bool ExtendedHitPlausible(ExtendedFiringSnapshot snapshot, Vector3 hitPosition,
        float reportedHitSpeed, float now)
    {
        // Check whether projectile's lifetime is in sane limits, ExtendedSnapshotLifetime's 30s should cover plenty distance
        // More similar I believe incorrect Harmony003 warnings, we're only reading from these values here so shouldn't matter
        var age = now - snapshot.Timestamp;
        
        if (age is < 0f or > ExtendedSnapshotLifetime)
            return false;
        
        var toHit = hitPosition - snapshot.Origin;
        var hitTravelDistance = toHit.magnitude;
        
        switch (hitTravelDistance)
        {
            // Don't interfere with a hit rejected sub vanilla's hardcoded 3000m limit, as it's likely rejected for other
            // (and valid) reasons
            case <= VanillaMaximumDistance:
            // Also ignore above 60km as it's (probably?) unlikely for a bullet to be actually hitting there
            // Rider suggested a switch statement here
            case > AbsoluteMaximumDistance:
                return false;
        }
        
        var firingSpeed = snapshot.Velocity.magnitude;
        
        //
        if (firingSpeed <= Mathf.Epsilon)
            return false;
        
        // Rough estimated sane angle limits of where the shot could've come from to be plausible, I just went with 10 degrees
        var angle = Vector3.Angle(toHit, snapshot.Velocity);
        
        if (angle >= MaximumAngle)
            return false;
        
        // Maximum possible sane limit for how far the round could've got based on launch velocity + bullet lifetime
        // I chose an added tolerance of +3s for time and +3000m for distance
        var maximumPlausibleDistance = firingSpeed * (age + TimeTolerance) + DistanceTolerance;
        if (hitTravelDistance > maximumPlausibleDistance)
            return false;
        
        // Maximum possible sane limit for how fast the round is on impact vs launch with a tolerance of
        // a factor of 3x, seems highly unlikely for a bullet to be over 2x faster on impact vs firing
        if (reportedHitSpeed > firingSpeed * MaximumReportedSpeedMultiplier)
            return false;
        
        return true;
    }
    
    private static void RemoveExpiredSnapshots(List<ExtendedFiringSnapshot> snapshots, float now)
    {
        // Handle removing expired shots (>30s) from snapshot dictionary
        // Oldest should be on front
        var expiredCount = 0;
        
        while (expiredCount < snapshots.Count && now - snapshots[expiredCount].Timestamp > ExtendedSnapshotLifetime)
            expiredCount++;
        
        if (expiredCount > 0)
            snapshots.RemoveRange(0, expiredCount);
    }
    
    [HarmonyPatch(typeof(HitValidator), nameof(HitValidator.Initialize))]
    [HarmonyPostfix]
    private static void ClearExtendedFiringLogs()
    {
        // Clear snapshot dictionary on new HitValidator initialisation (e.g. map reload)
        ExtendedFiringLogs.Clear();
    }
    
    // Rider keeps putting this on bottom even though I like it near top at the other variables
    private readonly struct ExtendedFiringSnapshot(Vector3 origin, Vector3 velocity, float timestamp)
    {
        public readonly Vector3 Origin = origin;
        public readonly Vector3 Velocity = velocity;
        public readonly float Timestamp = timestamp;
    }
}
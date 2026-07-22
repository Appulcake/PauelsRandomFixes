using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal sealed class GunSelfDamageFix(ConfigFile config) : ConfigurableFix(config)
{
    // This is only used after the original Linecast has already hit
    // one of the firing unit's own colliders.
    private const int RaycastBufferSize = 64;
    
    //Usercode in Unit.cs that's called by client to indicate their (authoritative) simulated bullet hit something
    private const string CmdClaimHitUserCodeMethod = "UserCode_CmdClaimHit_-1122942669";
    private static readonly RaycastHit[] RaycastBuffer = new RaycastHit[RaycastBufferSize];
    
    private static readonly MethodInfo PhysicsLinecast =
        AccessTools.Method(typeof(Physics), nameof(Physics.Linecast),
            new[] { typeof(Vector3), typeof(Vector3), typeof(RaycastHit).MakeByRefType(), typeof(int) })
        ?? throw new MissingMethodException("Could not find the required Physics.Linecast overload.");
    
    private static readonly MethodInfo FilteredLinecast =
        AccessTools.Method(typeof(GunSelfDamageFix), nameof(LinecastIgnoringOwner))
        ?? throw new MissingMethodException("Could not find LinecastIgnoringOwner.");
    
    protected override string Description =>
        "Fixes the possibility of fired bullets impacting the owner's plane/vehicle, which"
        + " really shouldn't even be possible in any circumstance. This can especially happen when playing on a server"
        + " with high ping, in certain planes (Brawler with its 35mm is especially susceptible), going at higher" +
        " speeds, manoeuvring aggressively while firing."
        + "\n\nFix is most effective (and only required to fully work) on client as client's BulletSim has the authority to" + 
        " trigger the impacts that cause damage, and is done by ignoring bullet collision with owner's vehicle, continuing" +
        " the bullet's path."
        + "\n\nThe fix on server's end adds an extra safeguard that prevents a bullet self-hit claim from a client from"
        + " applying damage to their vehicle, but it won't stop the client's BulletSim to already be stopped as it"
        + " called an impact, thus an unfixed client while won't be damaging themselves, will still have their bullets"
        + " that collided with them just disappear and no longer keep travelling to hit original intended target. Doesn't"
        + " interfere with clients running the fix themselves.";
    
    // Client sided patch for the client-authoritative BulletSim
    // This path has: IsServer: False (not on server's side) | HasAuthority: True (can call impacts for damage) | LocalSim: True (simulated by this client, = owner of gun)
    // remoteSim: False (not another client on server) | visualOnly: False (not just visual simulated bullets that server does, those can't call damage impacts normally)
    // This fix path is inert on server
    [HarmonyPatch(typeof(BulletSim.Bullet), nameof(BulletSim.Bullet.TrajectoryTrace))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> TrajectoryTraceTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        
        // First Physics.Linecast Call is regular bullet that's prone to self-hitting
        // Second one later is for what seems to be proximity-fuse, highly unlikely to be causing issues
        matcher
            .MatchForward(
                false,
                new CodeMatch(instruction => instruction.Calls(PhysicsLinecast)))
            .ThrowIfInvalid("Could not find the primary BulletSim Linecast.");
        
        // Physics.Linecast has these arguments:
        //
        // Vector3 start
        // Vector3 end (start + this.velocity *1.5f * deltaTime)
        // out RaycastHit hitInfo
        // int layerMask (-8193 / layer 13)
        //
        // Adding another argument to forward Unit owner, which is ldarg.3
        matcher.InsertAndAdvance(new CodeInstruction(OpCodes.Ldarg_3));
        
        // Redirect RaycastHit linecast check to own filtered method
        matcher.Instruction.opcode = OpCodes.Call;
        matcher.Instruction.operand = FilteredLinecast;
        
        return matcher.InstructionEnumeration();
    }
    
    private static bool LinecastIgnoringOwner(Vector3 start, Vector3 end, out RaycastHit hitInfo, int layerMask,
        Unit owner)
    {
        var hit = Physics.Linecast(start, end, out hitInfo, layerMask);
        
        if (!hit || owner == null || hitInfo.collider == null)
            return hit;
        
        if (!ColliderBelongsToOwner(hitInfo.collider, owner))
            return true;
        
        
        // RaycastNonAlloc check would not order hits by distance by default, causing a non-collide/damage check to also
        // apply to a very close target sometimes (that'd be hit within less than one update frame)
        // Or further chain-hits within same owner (multiple components hit) would not be checked for any more
        return TryFindNearestNonOwnerHit(start, end, layerMask, owner, out hitInfo);
    }
    
    private static bool TryFindNearestNonOwnerHit(Vector3 start, Vector3 end, int layerMask, Unit owner,
        out RaycastHit hitInfo)
    {
        // If I'm honest here, I got a bit lost in the sauce on figuring out exact RaycastNonAlloc usage to fix the
        // follow-up check to prevent it from hitting a secondary component (likely to happen on belly mounted
        // brawler gun for example, hitting fuselage + cockpit + nose, only cancelling out the first impact was not enough
        //
        // So this is mostly AI assisted here
        
        hitInfo = default;
        
        var trace = end - start;
        var traceLength = trace.magnitude;
        
        if (traceLength <= Mathf.Epsilon)
            return false;
        
        var direction = trace / traceLength;
        
        var hitCount = Physics.RaycastNonAlloc(start, direction, RaycastBuffer, traceLength, layerMask,
            QueryTriggerInteraction.UseGlobal);
        
        var foundNonOwnerHit = false;
        var nearestDistance = float.PositiveInfinity;
        
        for (var i = 0; i < hitCount; i++)
        {
            var candidate = RaycastBuffer[i];
            
            if (candidate.collider == null || candidate.distance >= nearestDistance ||
                ColliderBelongsToOwner(candidate.collider, owner)) continue;
            
            nearestDistance = candidate.distance;
            hitInfo = candidate;
            foundNonOwnerHit = true;
        }
        
        return foundNonOwnerHit;
    }
    
    private static bool ColliderBelongsToOwner(Collider collider, Unit owner)
    {
        // Check whether damage hit the owner/parent vehicle that fired the gun
        // Regular Transform/Ridigbody checks wouldn't always return parent, some subcomponents like say Brawler's
        // nose had its own RigidBody and detached root
        // However, it always returned a IDamageable inherited class (e.g. AeroPart for nose, like all UnitPart)
        // So IDamageable.GetUnit() check can be done to compare if it's the owner to check for self-damage
        
        var damageable = collider.gameObject.GetComponent<IDamageable>();
        
        if (damageable != null && SameUnit(damageable.GetUnit(), owner))
            return true;
        
        // Regular RidigBody and Transform owner checks for safety
        
        var colliderTransform = collider.transform;
        var ownerTransform = owner.transform;
        
        if (colliderTransform == ownerTransform || colliderTransform.IsChildOf(ownerTransform))
            return true;
        
        if (owner.rb != null && collider.attachedRigidbody == owner.rb)
            return true;
        
        // Extra redundancy check in children components
        var parentUnit = collider.GetComponentInParent<Unit>();
        
        return SameUnit(parentUnit, owner);
    }
    
    private static bool SameUnit(Unit first, Unit second)
    {
        if (first == null || second == null)
            return false;
        
        if (first == second)
            return true;
        
        // In case direct unit comparison fails, I noticed in Unit.cs persistentID was commonly used to keep track
        // of specific units, and all components looked at by Unity Runtime Editor showed same persistentID
        // So thought it'd be a good backup comparison
        return !first.persistentID.Equals(PersistentID.None) && first.persistentID.Equals(second.persistentID);
    }
    
    
    // Server sided patch that can at least cancel out a claimed damage hit coming from a client in case the owner
    // of the bullet is same as the owner who shot it, this cancels it triggering HitOnPhysicsFrame() which when
    // successfully validating the hit (which it usually does, as it's especially lenient on close ranges), would trigger
    // damage events to happen to owner's plane via DamageEffects.ArmorPenetrate()
    //
    // This is mainly intended for clients not running the client-sided patch (which circumvents even sending CmdClaimHit)
    // and cannot influence the client's BulletSim from stopping the linecast as it stops on this impact calling the hit
    // So the bullet won't continue flying on client and is "eaten", but at least it won't damage/destroy their plane
    [HarmonyPatch(typeof(Unit), CmdClaimHitUserCodeMethod)]
    [HarmonyPrefix]
    private static bool RejectBulletSelfHitClaim(Unit __instance, PersistentID __0)
    {
        var hitID = __0;
        
        if (__instance == null || __instance.persistentID.Equals(PersistentID.None) ||
            !hitID.Equals(__instance.persistentID))
            return true;
        
        // Don't really want to keep this logging in as in some cases it could be triggered quite a lot
        // But can be useful to re-enable to debug its activity
        /*
        PRF.Logger.LogDebug(
            $"Rejected bullet self-hit claim from " +
            $"{__instance.unitName} " +
            $"PersistentID={__instance.persistentID}.");
        */
        
        return false;
    }
}
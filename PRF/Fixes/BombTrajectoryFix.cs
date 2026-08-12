using BepInEx.Configuration;
using HarmonyLib;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class BombTrajectoryFix(ConfigFile config) : ConfigurableFix(config)
{
    protected override string Description =>
        $"{base.Description}\nRestores pre 0.34 climbing trajectory for bombs, preventing their large overshoot and" +
        " newly introduced inaccuracy before terminal guidance phase.";
    
    [HarmonyPatch(typeof(Kinematics), nameof(Kinematics.GetBallisticAimPoint))]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    private static bool GetBallisticAimPointPrefix(Missile missile, float timeToTarget, ref GlobalPosition __result)
    {
        if (!missile.TryGetComponent<OpticalSeekerBomb>(out _))
            return true;
        
        if (missile.rb.velocity.y < 0f || timeToTarget < 10f)
            return true;
        
        __result = missile.GlobalPosition() + missile.rb.velocity * 10000f;
        
        return false;
    }
}
#if CLIENT
using System;
using BepInEx.Configuration;
using HarmonyLib;
// ReSharper disable InconsistentNaming

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal class TailHookGuidanceFix(ConfigFile config) : ConfigurableFix(config)
{
    private const float MinimumDeployRadarAltitude = 30f;
    private const float MaximumDeployRadarAltitude = 120f;
    private const float MaximumDeploySpeed = 100f;
    private const float StoppedSpeed = 2f;
    private const float GroundMessageSuppressAltitude = 1f;
    
    private static Aircraft? _aircraftState;
    private static bool _guidanceActive;
    private static DeploymentIssue _lastDeploymentIssues;
    private static bool _deploymentIssuesInitialised;
    
    private static AircraftActionsReport? _guidanceReport;
    private static string? _guidanceMessage;
    private static bool _guidanceVisible;
    
    protected override string Description =>
        $"{base.Description}\nDisplays guidance messages when landing on and gear down on why the tail hook can't " +
        "deploy, like required speed and (radar) altitude brackets.";
    
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.SetGear), typeof(LandingGear.GearState))]
    [HarmonyPostfix]
    private static void SetGearPostfix(Aircraft __instance, LandingGear.GearState gearState)
    {
        if (!GameManager.IsLocalAircraft(__instance))
            return;
        
        CheckAircraftState(__instance);
        switch (gearState)
        {
            case LandingGear.GearState.Extending:
                _guidanceActive = true;
                _deploymentIssuesInitialised = false;
                break;
            
            case LandingGear.GearState.Retracting:
            case LandingGear.GearState.LockedRetracted:
                DisableGuidance();
                break;
        }
    }
    
    [HarmonyPatch(typeof(TailHook), nameof(TailHook.CheckDeployConditions))]
    [HarmonyPostfix]
    private static void CheckDeployConditionsPostfix(TailHook __instance)
    {
        if (__instance.unitPart?.parentUnit is not Aircraft aircraft || !GameManager.IsLocalAircraft(aircraft))
            return;
        
        CheckAircraftState(aircraft);
        if (!_guidanceActive)
            return;
        
        if (aircraft.gearState is LandingGear.GearState.Retracting or LandingGear.GearState.LockedRetracted)
        {
            DisableGuidance();
            return;
        }
        
        // This is on "Tail Hook Deployed" vanilla message, altitude and descent state doesn't matter any more once
        // deployed
        if (__instance.deployed)
        {
            ClearGuidanceMessage();
            _lastDeploymentIssues = DeploymentIssue.None;
            _deploymentIssuesInitialised = true;
            return;
        }
        
        // Suppress guidance once landed or when vanilla retracts hook on stopping
        if (aircraft.radarAlt <= GroundMessageSuppressAltitude || aircraft.speed < StoppedSpeed)
        {
            ClearGuidanceMessage();
            _deploymentIssuesInitialised = false;
            return;
        }
        
        var issues = GetDeploymentIssues(aircraft);
        
        if (issues == DeploymentIssue.None)
        {
            ClearGuidanceMessage();
            _deploymentIssuesInitialised = false;
            return;
        }
        
        // Send new guidance report when reason for not being able to deploy changes
        if (_deploymentIssuesInitialised && issues == _lastDeploymentIssues)
            return;
        
        _lastDeploymentIssues = issues;
        _deploymentIssuesInitialised = true;
        ReportGuidance(issues);
    }
    
    [HarmonyPatch(typeof(AircraftActionsReport), nameof(AircraftActionsReport.Initialize), typeof(Aircraft))]
    [HarmonyPostfix]
    private static void AircraftActionsReportInitializePostfix(AircraftActionsReport __instance)
    {
        _guidanceReport = __instance;
        _guidanceMessage = null;
        _guidanceVisible = false;
    }
    
    [HarmonyPatch(typeof(AircraftActionsReport), nameof(AircraftActionsReport.ReportText))]
    [HarmonyPrefix]
    private static void ReportTextPrefix(AircraftActionsReport __instance, out bool __state)
    {
        // Tail hook guidance is at end of report list, when a new report comes remove it and add it back
        __state = _guidanceVisible && ReferenceEquals(_guidanceReport, __instance) && DetachGuidance(__instance);
    }
    
    [HarmonyPatch(typeof(AircraftActionsReport), nameof(AircraftActionsReport.ReportText))]
    [HarmonyPostfix]
    private static void ReportTextPostfix(AircraftActionsReport __instance, bool __state)
    {
        if (__state && _guidanceVisible && ReferenceEquals(_guidanceReport, __instance) && _guidanceMessage != null)
            AttachGuidance(__instance, _guidanceMessage);
    }
    
    private static void SetGuidanceMessage(string message)
    {
        var report = SceneSingleton<AircraftActionsReport>.i;
        if (report == null || report.actionsText == null)
            return;
        
        if (!ReferenceEquals(_guidanceReport, report))
        {
            _guidanceReport = report;
            _guidanceMessage = null;
            _guidanceVisible = false;
        }
        
        if (_guidanceVisible && _guidanceMessage != null)
        {
            var text = report.actionsText.text;
            if (text == _guidanceMessage)
            {
                report.actionsText.text = message;
                _guidanceMessage = message;
                return;
            }
            
            var suffix = "\n" + _guidanceMessage;
            if (text.EndsWith(suffix, StringComparison.Ordinal))
            {
                report.actionsText.text = text.Substring(0, text.Length - _guidanceMessage.Length) + message;
                _guidanceMessage = message;
                return;
            }
            
            _guidanceVisible = false;
            _guidanceMessage = null;
        }
        
        _guidanceMessage = message;
        _guidanceVisible = true;
        AttachGuidance(report, message);
    }
    
    private static void ClearGuidanceMessage()
    {
        if (!_guidanceVisible)
            return;
        
        if (_guidanceReport != null)
            DetachGuidance(_guidanceReport);
        
        _guidanceVisible = false;
        _guidanceMessage = null;
    }
    
    private static void AttachGuidance(AircraftActionsReport report, string message)
    {
        if (report.actionsText == null)
            return;
        
        if (report.actionsText.text.Length > 0)
            report.actionsText.text += "\n";
        
        report.actionsText.text += message;
        report.messageLines++;
        report.background.enabled = true;
    }
    
    private static bool DetachGuidance(AircraftActionsReport report)
    {
        if (!_guidanceVisible || _guidanceMessage == null || report.actionsText == null)
            return false;
        
        var text = report.actionsText.text;
        if (text == _guidanceMessage)
        {
            report.actionsText.text = string.Empty;
        }
        else
        {
            var suffix = "\n" + _guidanceMessage;
            if (!text.EndsWith(suffix, StringComparison.Ordinal))
                return false;
            
            report.actionsText.text = text.Substring(0, text.Length - suffix.Length);
        }
        
        if (report.messageLines > 0)
            report.messageLines--;
        
        if (report.messageLines <= 0)
            report.background.enabled = false;
        
        return true;
    }
    
    private static DeploymentIssue GetDeploymentIssues(Aircraft aircraft)
    {
        var issues = DeploymentIssue.None;
        var altitudeValid = true;
        switch (aircraft.radarAlt)
        {
            case <= MinimumDeployRadarAltitude:
                issues |= DeploymentIssue.TooLow;
                altitudeValid = false;
                break;
            case >= MaximumDeployRadarAltitude:
                issues |= DeploymentIssue.TooHigh;
                altitudeValid = false;
                break;
        }
        
        if (aircraft.speed >= MaximumDeploySpeed)
            issues |= DeploymentIssue.TooFast;
        
        // Only warn about not currently descending (one of the hook deployment criteria) when inside the correct
        // altitude bracket, outside of that altitude related guidance is just to climb to or descend to X
        if (altitudeValid && aircraft.rb.velocity.y >= 0f)
            issues |= DeploymentIssue.NotDescending;
        
        return issues;
    }
    
    private static void ReportGuidance(DeploymentIssue issues)
    {
        var message = "Tail Hook not deployed:";
        if ((issues & DeploymentIssue.TooHigh) != 0)
            message += $"<br>Descend below {AltitudeReading(MaximumDeployRadarAltitude)} radar altitude";
        else if ((issues & DeploymentIssue.TooLow) != 0)
            message += $"<br>Climb above {AltitudeReading(MinimumDeployRadarAltitude)} radar altitude";
        
        if ((issues & DeploymentIssue.TooFast) != 0)
            message += $"<br>Slow down below {UnitConverter.SpeedReading(MaximumDeploySpeed)}";
        
        if ((issues & DeploymentIssue.NotDescending) != 0)
            message += "<br>Descend to deploy";
        
        SetGuidanceMessage(message);
    }
    
    private static void CheckAircraftState(Aircraft aircraft)
    {
        if (ReferenceEquals(_aircraftState, aircraft))
            return;
        
        ClearGuidanceMessage();
        _aircraftState = aircraft;
        _guidanceActive = false;
        _lastDeploymentIssues = DeploymentIssue.None;
        _deploymentIssuesInitialised = false;
    }
    
    private static void DisableGuidance()
    {
        ClearGuidanceMessage();
        _guidanceActive = false;
        _lastDeploymentIssues = DeploymentIssue.None;
        _deploymentIssuesInitialised = false;
    }
    
    // Altitude can add a + in front to indicate positive so say 120m shows as +120m, this I believe might be coming
    // from a mod like DetailedClimbRate perhaps but this ensures there's no + in front to keep the reading concise
    // Also DetailedClimbRate includes a leading space for some reason, above 100 it returns:
    // \u00A0+{roundval / 100}{unit} so this needed a bit of special trim/sanitising
    private static string AltitudeReading(float altitude)
    {
        var reading = UnitConverter.AltitudeReading(altitude).TrimStart();
        if (reading.Length > 0 && reading[0] == '+')
            reading = reading.Substring(1);
        
        return reading;
    }
    
    [Flags]
    private enum DeploymentIssue : byte
    {
        None = 0,
        TooLow = 1 << 0,
        TooHigh = 1 << 1,
        TooFast = 1 << 2,
        NotDescending = 1 << 3
    }
}
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;

namespace PRF;

/// <summary>
///     Main plugin class.
/// </summary>
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
// ReSharper disable once InconsistentNaming
public class PRF : BaseUnityPlugin
{
    internal new static ManualLogSource Logger { get; private set; } = null!;
    
    internal static List<ConfigurableFix> Fixes { get; } = [];
    
    private void Awake()
    {
        Logger = base.Logger;
        LoadFixes();
    }
    
    private void LoadFixes()
    {
        var fixTypes = typeof(ConfigurableFix)
            .Assembly
            .GetTypes()
            .Where(t =>
                t is
                {
                    IsClass: true,
                    IsAbstract: false
                } &&
                typeof(ConfigurableFix).IsAssignableFrom(t) &&
                t.GetCustomAttribute<FixAttribute>() != null);
        
        foreach (var type in fixTypes)
            try
            {
                var fix = (ConfigurableFix)Activator.CreateInstance(type, Config)!;
                Fixes.Add(fix);
                
                var attribute = type.GetCustomAttribute<FixAttribute>();
                
                Logger.LogInfo(
                    $"Loaded fix: {attribute?.DisplayName ?? type.Name}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load fix {type.Name}: {ex}");
            }
    }
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class FixAttribute(string? displayName = null) : Attribute
{
    public string? DisplayName { get; } = displayName;
}
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
    /// <summary>
    ///     PRF logger instance.
    /// </summary>
    public new static ManualLogSource Logger { get; private set; } = null!;
    
    private static List<ConfigurableFix> Fixes { get; } = [];
    
    private void Awake()
    {
        Logger = base.Logger;
    }
    
    private void Start()
    {
        LoadFixes();
    }
    
    private void LoadFixes()
    {
        var fixTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(GetTypesSafe)
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
    
    private static IEnumerable<Type> GetTypesSafe(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // If the assembly fails to load completely, salvage the types that successfully loaded
            return ex.Types.Where(t => t != null);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to read types from assembly {assembly.FullName}: {ex.Message}");
            return [];
        }
    }
}

/// <summary>
///     Attribute that signifies that the class is a Fix and should be loaded by PRF
/// </summary>
/// <param name="displayName"> display name of this fix in the config file.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FixAttribute(string? displayName = null) : Attribute
{
    /// <summary>
    ///     Display name of the fix.
    /// </summary>
    public string? DisplayName { get; } = displayName;
}
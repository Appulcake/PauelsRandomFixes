using System;
using BepInEx.Configuration;
using HarmonyLib;

namespace PRF;

internal abstract class ConfigurableFix : HarmonyPatch
{
    private readonly ConfigEntry<bool> _enabled;
    private Harmony? Harmony { get; }
    private bool IsPatched { get; set; }
    
    protected virtual bool DefaultEnabled => true;
    
    protected virtual string Description => $"if true, {GetType().Name} is enabled";
    
    protected ConfigurableFix(ConfigFile config)
    {
        var type = GetType().Name;
        // ReSharper disable twice VirtualMemberCallInConstructor
        _enabled = config.Bind("FIXES", type, DefaultEnabled, Description);
        Harmony ??= new Harmony($"{PluginInfo.PLUGIN_GUID}.{type}");
        _enabled.SettingChanged += OnEnabledChanged;
        if (_enabled.Value)
            ApplyPatches();
    }
    
    private void OnEnabledChanged(object sender, EventArgs e)
    {
        if (_enabled.Value)
            ApplyPatches();
        else
            RemovePatches();
    }
    
    private void ApplyPatches()
    {
        if (IsPatched)
            return;
        var type = GetType();
        PRF.Logger.LogDebug($"Patching {type.Name}...");
        Harmony!.CreateClassProcessor(type).Patch();
        PRF.Logger.LogDebug($"Patched {type.Name}!");
        
        IsPatched = true;
    }
    
    private void RemovePatches()
    {
        if (!IsPatched)
            return;
        
        Harmony!.UnpatchSelf();
        IsPatched = false;
    }
}
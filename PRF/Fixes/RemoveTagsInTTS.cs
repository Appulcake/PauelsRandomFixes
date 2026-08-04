#if CLIENT
using System;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Chat;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch(typeof(ChatManager))]
// ReSharper disable once InconsistentNaming
internal class RemoveTagsInTTS : ConfigurableFix
{
    private const string DefaultPattern = @"<[^>]*>|\[[^\]]*\]";
    
    private static ConfigEntry<string> _removeRegex = null!;
    
    private static Regex _ttsFormattingRegex = null!;
    
    public RemoveTagsInTTS(ConfigFile config) : base(config)
    {
        _removeRegex = config.Bind(GetType().Name, "Regex blacklist", DefaultPattern,
            "Regex pattern of what to remove. Default removes <> and [] tags.");
        
        UpdateRegex();
        _removeRegex.SettingChanged += (_, _) => UpdateRegex();
    }
    
    protected override string Description =>
        $"{base.Description}\nPrevents TTS from reading out HTML tags in messages.";
    
    private static void UpdateRegex()
    {
        try
        {
            _ttsFormattingRegex = new Regex(_removeRegex.Value, RegexOptions.Compiled);
            
            PRF.Logger.LogDebug($"Updated TTS regex blacklist with: {_removeRegex.Value}");
        }
        catch (ArgumentException exception)
        {
            PRF.Logger.LogWarning(
                $"Invalid regex \"{_removeRegex.Value}\". Keeping previous regex.\n{exception.Message}");
        }
    }
    
    [HarmonyPatch(nameof(ChatManager.RunTTS))]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    internal static void RunTTSPrefix(ref string playerName, ref string message)
    {
        var regex = _ttsFormattingRegex;
        
        playerName = regex.Replace(playerName, string.Empty).Trim();
        message = regex.Replace(message, string.Empty);
    }
}
#endif
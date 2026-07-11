using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Chat;

namespace PRF.Fixes;

[Fix]
[HarmonyPatch(typeof(ChatManager))]
// ReSharper disable once InconsistentNaming
internal class RemoveTagsInTTS(ConfigFile config) : ConfigurableFix(config)
{
    [HarmonyPatch(nameof(ChatManager.RunTTS))]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    internal static void RunTTSPrefix(ref string playerName, ref string message)
    {
        playerName = Regex.Replace(playerName, "<.*?>", string.Empty);
        message = Regex.Replace(message, "<.*?>", string.Empty);
    }
}
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine.UI;
using System;

namespace PRF;

[Fix]
[HarmonyPatch(typeof(MessageUI))]
internal class ClickthroughChatbox(ConfigFile config): ConfigurableFix(config)
{
    [HarmonyPatch(nameof(MessageUI.Awake))]
    [HarmonyPostfix]
    private static void Postfix(MessageUI __instance)
    {
        __instance.messageText.raycastTarget = false;
        __instance.killFeedText.raycastTarget = false;

        var image = __instance.messageBackground.GetComponent<Image>();

        if (image != null)
            image.raycastTarget = false;
    }
}
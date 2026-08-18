using HarmonyLib;
using Sandbox;
using Sandbox.Engine.Platform.VideoMode;

namespace ClientPlugin.Patches.SystemAbstraction;

// DXVK adapter priority does not rank physical GPU capability. Suppress only
// the misleading better-GPU notification and keep the adapter scan intact.
[HarmonyPatch(typeof(MyVideoSettingsManager), "OnVideoAdaptersResponse")]
[HarmonyPatchCategory("Finish")]
static class MyVideoSettingsManagerOnVideoAdaptersResponsePatch
{
    [HarmonyPostfix]
    static void Postfix()
    {
        MySandboxGame.ShowIsBetterGCAvailableNotification = false;
    }
}

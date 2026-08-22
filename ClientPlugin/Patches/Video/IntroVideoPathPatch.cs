using HarmonyLib;
using Sandbox.Game.Gui;

namespace ClientPlugin.Patches.Video;

[HarmonyPatch(typeof(MyGuiScreenIntroVideo), "TryPlayVideo")]
[HarmonyPatchCategory("Finish")]
static class IntroVideoPathPatch
{
    static void Prefix(MyGuiScreenIntroVideo __instance)
    {
        // Normalize the Windows path before the direct File.Exists check.
        var currentVideo = __instance.m_currentVideo;
        if (!string.IsNullOrEmpty(currentVideo) && currentVideo.Contains('\\'))
            __instance.m_currentVideo = currentVideo.Replace('\\', '/');
    }
}

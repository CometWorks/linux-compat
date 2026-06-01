using HarmonyLib;
using Sandbox.Game.Gui;

namespace ClientPlugin.Patches.Video;

[HarmonyPatch(typeof(MyGuiScreenIntroVideo), "TryPlayVideo")]
[HarmonyPatchCategory("Finish")]
static class TryPlayVideoDiagPatch
{
    static void Prefix(MyGuiScreenIntroVideo __instance)
    {
        // Normalize backslashes to forward slashes BEFORE the stock
        // File.Exists(Path.Combine(ContentPath, m_currentVideo)) check.
        // Stock game hardcodes "Videos\\BackgroundNN.wmv" and "Videos\\KSH.wmv"
        // paths which fail File.Exists on Linux.
        var currentVideo = __instance.m_currentVideo;
        if (!string.IsNullOrEmpty(currentVideo) && currentVideo.Contains('\\'))
        {
            __instance.m_currentVideo = currentVideo.Replace('\\', '/');
        }
    }
}

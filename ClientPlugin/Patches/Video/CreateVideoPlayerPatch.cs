using System;
using ClientPlugin.Compatibility.Video;
using HarmonyLib;
using VRage;
using VRage.Platform.Windows;
using VRage.Utils;

namespace ClientPlugin.Patches.Video;

/// <summary>
/// Creates the FFmpeg and SDL video player instead of the unavailable DirectShow player.
/// </summary>
[HarmonyPatch(typeof(MyVRagePlatform), nameof(MyVRagePlatform.CreateVideoPlayer))]
[HarmonyPatchCategory("Finish")]
static class CreateVideoPlayerPatch
{
    static bool Prefix(ref IVideoPlayer __result)
    {
        try
        {
            MyLog.Default.WriteLineAndConsole("[LinuxCompat] CreateVideoPlayer: constructing MyLinuxVideoPlayer");
            __result = new MyLinuxVideoPlayer();
            return false;
        }
        catch (Exception ex)
        {
            MyLog.Default.WriteLineAndConsole($"[LinuxCompat] MyLinuxVideoPlayer construction failed: {ex}");
            __result = null;
            return false;
        }
    }
}

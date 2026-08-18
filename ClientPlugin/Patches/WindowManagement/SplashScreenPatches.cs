using System;
using ClientPlugin.Compatibility;
using HarmonyLib;
using Sandbox.Game;
using VRage.Platform.Windows.Forms;
using VRageMath;

namespace ClientPlugin.Patches.WindowManagement;

/// <summary>
/// Supplies SDL splash behavior for the preloader-disabled WinForms methods.
/// Pulsar's <c>-sesplash</c> flag controls whether the game calls them.
/// </summary>
[HarmonyPatch(typeof(MyWindowsWindows), nameof(MyWindowsWindows.ShowSplashScreen))]
[HarmonyPatchCategory("Finish")]
static class ShowSplashScreenPatch
{
    static bool Prefix(string image, Vector2 scale)
    {
        if (!RenderingConfig.AllowRendering)
            return false;

        string gameIcon = MyPerGameSettings.GameIcon;
        if (string.IsNullOrEmpty(gameIcon))
        {
            string appName = MyPerGameSettings.BasicGameInfo.ApplicationName;
            if (!string.IsNullOrEmpty(appName))
                gameIcon = appName + ".ico";
        }

        Console.WriteLine($"[LinuxCompat] ShowSplashScreen prefix: image='{image}' gameIcon='{gameIcon}' scale=({scale.X},{scale.Y})");
        MySdlSplashScreen.Show(image, gameIcon, scale);
        return false;
    }
}

[HarmonyPatch(typeof(MyWindowsWindows), nameof(MyWindowsWindows.HideSplashScreen))]
[HarmonyPatchCategory("Finish")]
static class HideSplashScreenPatch
{
    static bool Prefix()
    {
        if (!RenderingConfig.AllowRendering)
            return false;

        Console.WriteLine("[LinuxCompat] HideSplashScreen prefix");
        MySdlSplashScreen.Hide();
        return false;
    }
}

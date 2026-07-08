using System;
using ClientPlugin.Compatibility;
using HarmonyLib;
using Sandbox.Game;
using VRage.Platform.Windows.Forms;
using VRageMath;

namespace ClientPlugin.Patches.WindowManagement;

/// <summary>
/// Redirects the stock WinForms-based splash screen implementation in
/// <see cref="MyWindowsWindows"/> to our SDL3-based
/// <see cref="MySdlSplashScreen"/>. The original <c>ShowSplashScreen</c> /
/// <c>HideSplashScreen</c> bodies are NOP-ed in the Preloader (they
/// reference <c>System.Windows.Forms</c> types that do not exist on Linux);
/// these Harmony prefixes supply the Linux behavior. The actual SDL3 calls
/// run on <see cref="SdlRenderThread"/>; the prefix simply hands off and
/// blocks until the splash is up.
///
/// Requires the <c>-sesplash</c> flag passed to Pulsar so
/// <c>MyFakes.ENABLE_SPLASHSCREEN</c> becomes true and
/// <c>MyCommonProgramStartup.InitSplashScreen</c> calls
/// <c>MyVRage.Platform.Windows.ShowSplashScreen</c>. When the flag is
/// absent these patches are simply never called, and the rest of the
/// SDL3 flow (init done in <c>SdlRenderThread.Start</c>, game-window
/// creation in <see cref="ClientPlugin.Patches.PlatformGuards.CreateWindowPatch"/>)
/// remains correct.
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

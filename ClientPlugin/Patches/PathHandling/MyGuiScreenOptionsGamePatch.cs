using System.IO;
using HarmonyLib;
using Sandbox.Game.Gui;
using VRage.FileSystem;

namespace ClientPlugin.Patches.PathHandling;

// Enumerate crosshair indicators through a Linux-compatible System.IO path.
[HarmonyPatch(typeof(MyGuiScreenOptionsGame), "InitCrosshairIndicators")]
[HarmonyPatchCategory("Finish")]
static class MyGuiScreenOptionsGameInitCrosshairIndicatorsPatch
{
    static bool Prefix(MyGuiScreenOptionsGame __instance)
    {
        var dir = Path.Combine(MyFileSystem.ContentPath, "Textures/GUI/Indicators");
        if (!Directory.Exists(dir))
            return false;

        foreach (var item in Directory.EnumerateFiles(dir))
        {
            if (item.Contains("HitIndicator"))
                __instance.m_crosshairFiles.Add(item);
        }
        return false;
    }
}

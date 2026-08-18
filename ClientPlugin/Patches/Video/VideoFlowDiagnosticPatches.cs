using HarmonyLib;
using Sandbox.Game.Gui;

namespace ClientPlugin.Patches.Video;

[HarmonyPatch(typeof(MyGuiScreenIntroVideo), "TryPlayVideo")]
[HarmonyPatchCategory("Finish")]
static class TryPlayVideoDiagPatch
{
    private static readonly System.Reflection.FieldInfo CurrentVideoField =
        AccessTools.Field(typeof(MyGuiScreenIntroVideo), "m_currentVideo");

    static void Prefix(MyGuiScreenIntroVideo __instance)
    {
        // Normalize the Windows path before the direct File.Exists check.
        var currentVideo = CurrentVideoField?.GetValue(__instance) as string;
        if (!string.IsNullOrEmpty(currentVideo) && currentVideo.Contains('\\'))
        {
            CurrentVideoField.SetValue(__instance, currentVideo.Replace('\\', '/'));
        }
    }
}

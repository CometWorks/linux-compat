using HarmonyLib;
using Sandbox.Game.Screens.Helpers;

namespace ClientPlugin.Patches.NullSafety;

[HarmonyPatch(typeof(MyAsyncSaving), "OnSnapshotDone")]
[HarmonyPatchCategory("Init")]
static class MyAsyncSavingOnSnapshotDonePatch
{
    static void Postfix()
    {
        MyAsyncSaving.m_screenshotTaken = true;
    }
}

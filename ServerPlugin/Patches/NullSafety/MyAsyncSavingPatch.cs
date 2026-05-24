using HarmonyLib;
using Sandbox.Game.Screens.Helpers;

namespace ServerPlugin.Patches.NullSafety;

[HarmonyPatch(typeof(MyAsyncSaving), "OnSnapshotDone")]
[HarmonyPatchCategory("Init")]
static class MyAsyncSavingOnSnapshotDonePatch
{
    static void Postfix()
    {
        var field = AccessTools.Field(typeof(MyAsyncSaving), "m_screenshotTaken");
        if (field != null)
            field.SetValue(null, true);
    }
}

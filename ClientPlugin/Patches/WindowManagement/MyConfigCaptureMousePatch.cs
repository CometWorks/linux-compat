using HarmonyLib;
using Sandbox.Engine.Utils;

namespace ClientPlugin.Patches.WindowManagement;

// Disable default mouse capture so GUI screens can release SDL relative mode.
[HarmonyPatch(typeof(MyConfig), "NewConfigWasStarted")]
[HarmonyPatchCategory("Finish")]
static class MyConfigCaptureMousePatch
{
    static void Postfix(MyConfig __instance)
    {
        __instance.CaptureMouse = false;
    }
}

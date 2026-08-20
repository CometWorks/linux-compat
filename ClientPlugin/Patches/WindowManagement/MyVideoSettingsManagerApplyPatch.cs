using HarmonyLib;
using Sandbox.Engine.Platform.VideoMode;
using VRageRender;

namespace ClientPlugin.Patches.WindowManagement;

// Explicit display settings may drive SDL before SwitchSettings. Internal
// backbuffer changes must not resize the window.
[HarmonyPatch(typeof(MyVideoSettingsManager), "Apply", typeof(MyRenderDeviceSettings))]
[HarmonyPatchCategory("Finish")]
static class MyVideoSettingsManagerApplyPatch
{
    static void Prefix(MyRenderDeviceSettings settings)
    {
        var current = MyVideoSettingsManager.CurrentDeviceSettings;
        if (
            settings.BackBufferWidth == current.BackBufferWidth
            && settings.BackBufferHeight == current.BackBufferHeight
            && settings.WindowMode == current.WindowMode
            && settings.AdapterOrdinal == current.AdapterOrdinal
        )
            return;
        ModeChangeForwarder.DriveDirect(settings);
    }
}

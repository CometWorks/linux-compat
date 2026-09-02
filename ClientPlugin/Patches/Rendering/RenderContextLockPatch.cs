using HarmonyLib;
using SharpDX.Direct3D11;
using VRage;
using VRage.Platform.Windows.Render;
using VRageRender;

namespace ClientPlugin.Patches.Rendering;

// Enable D3D11 multithread protection on the immediate context as soon as the
// render device exists. With it on, DXVK serializes every immediate-context
// call and Present behind its context lock, so no thread other than the
// render thread can race the DXVK command-stream thread. The flag lives on the
// device instance, so it is re-applied whenever the game recreates the device.
// Deferred contexts and true headless mode (MyNullRender) are unaffected.
[HarmonyPatch(typeof(MyWindowsRender), nameof(MyWindowsRender.CreateRenderDevice))]
[HarmonyPatchCategory("Finish")]
static class RenderContextLockPatch
{
    static void Postfix()
    {
        using var multithread = MyPlatformRender.DeviceInstance.QueryInterface<Multithread>();
        multithread.SetMultithreadProtected(true);
    }
}

using System;
using ClientPlugin.Compatibility;
using HarmonyLib;
using VRage;
using VRage.Library.Utils;
using VRageRender;
using VRageRender.ExternalApp;

namespace ClientPlugin.Patches.PlatformGuards;

[HarmonyPatch(typeof(MyGameRenderComponent), "Start")]
[HarmonyPatchCategory("Finish")]
static class GameRenderComponentStartPatch
{
    static bool Prefix(
        MyGameRenderComponent __instance,
        MyGameTimer timer,
        InitHandler windowInitializer,
        MyRenderDeviceSettings? settingsToTry,
        float maxFrameRate
    )
    {
        Console.WriteLine(
            "[LinuxCompat] MyGameRenderComponent.Start: calling window initializer on main thread"
        );
        IVRageWindow window = windowInitializer();

        __instance.RenderThread = MyRenderThread.Start(
            timer,
            () => window,
            settingsToTry,
            maxFrameRate
        );

        MyVRage.Platform.Render.OnSuspending += delegate
        {
            __instance.RenderThread.Suspend = true;
        };
        MyVRage.Platform.Render.OnResuming += delegate
        {
            __instance.RenderThread.Suspend = false;
        };

        return false;
    }
}

// Keep hidden Wayland Vulkan surfaces bufferless until ShowAndFocus configures the toplevel.
[HarmonyPatch(typeof(MyRenderProxy), nameof(MyRenderProxy.Present))]
[HarmonyPatchCategory("Finish")]
static class MyRenderProxyPresentPatch
{
    static bool Prefix()
    {
        return SdlInput2Provider.Instance?.PresentEnabled ?? true;
    }
}

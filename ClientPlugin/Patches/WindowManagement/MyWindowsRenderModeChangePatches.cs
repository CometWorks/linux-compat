using ClientPlugin.Patches.PlatformGuards;
using HarmonyLib;
using SharpDX.Direct3D11;
using VRage;
using VRage.Platform.Windows.Render;
using VRageRender;

namespace ClientPlugin.Patches.WindowManagement;

// Forward mode transitions to SDL because the WinForms GameWindow is absent.
// Same-mode size changes flow only from the SDL window to the backbuffer;
// reversing that edge creates HiDPI resize feedback.

internal static class ModeChangeForwarder
{
    // Null until the first explicit SDL mode change.
    private static MyWindowModeEnum? s_lastForwardedMode;

    public static void Forward(MyRenderDeviceSettings? settings)
    {
        if (!settings.HasValue)
            return;
        var s = settings.Value;

        // Same-mode sizes are driven by SDL window events.
        if (s_lastForwardedMode.HasValue && s_lastForwardedMode.Value == s.WindowMode)
            return;
        s_lastForwardedMode = s.WindowMode;

        var sdl = SdlInput2Provider.Instance;
        if (sdl == null)
            return;
        var adapters = MyPlatformRender.GetAdaptersList();
        if (adapters == null || s.AdapterOrdinal < 0 || s.AdapterOrdinal >= adapters.Length)
            return;
        sdl.OnModeChanged(
            s.WindowMode,
            s.BackBufferWidth,
            s.BackBufferHeight,
            adapters[s.AdapterOrdinal].DesktopBounds
        );
    }

    // Explicit settings choices may resize SDL without changing mode.
    public static void DriveDirect(MyRenderDeviceSettings settings)
    {
        s_lastForwardedMode = settings.WindowMode;
        var sdl = SdlInput2Provider.Instance;
        if (sdl == null)
            return;
        var adapters = MyPlatformRender.GetAdaptersList();
        if (
            adapters == null
            || settings.AdapterOrdinal < 0
            || settings.AdapterOrdinal >= adapters.Length
        )
            return;
        sdl.OnModeChanged(
            settings.WindowMode,
            settings.BackBufferWidth,
            settings.BackBufferHeight,
            adapters[settings.AdapterOrdinal].DesktopBounds
        );
    }
}

[HarmonyPatch(typeof(MyWindowsRender), nameof(MyWindowsRender.CreateRenderDevice))]
[HarmonyPatchCategory("Finish")]
static class MyWindowsRenderCreateRenderDevicePatch
{
    static void Postfix(MyRenderDeviceSettings? settings)
    {
        using var multithread = MyPlatformRender.DeviceInstance.QueryInterface<Multithread>();
        multithread.SetMultithreadProtected(true);
        ModeChangeForwarder.Forward(settings);
    }
}

[HarmonyPatch(typeof(MyWindowsRender), nameof(MyWindowsRender.ApplyRenderSettings))]
[HarmonyPatchCategory("Finish")]
static class MyWindowsRenderApplyRenderSettingsPatch
{
    static void Postfix(MyRenderDeviceSettings? settings) => ModeChangeForwarder.Forward(settings);
}

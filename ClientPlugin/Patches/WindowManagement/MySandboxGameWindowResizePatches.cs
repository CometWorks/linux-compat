using ClientPlugin.Patches.PlatformGuards;
using HarmonyLib;
using Sandbox;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Patches.WindowManagement;

// Reconciles the backbuffer from SDL drawable size on the game thread.
// Same-mode resize flow is one-way from window to backbuffer to avoid feedback.
internal static class BackbufferResizeRequest
{
    // Periodic reconciliation covers missed SDL resize signals.
    private const int PERIODIC_CHECK_INTERVAL_FRAMES = 60;

    private static bool s_requested;
    private static int s_frameCounter;

    public static void Request()
    {
        s_requested = true;
    }

    public static void ProcessIfRequested(MySandboxGame game)
    {
        if (++s_frameCounter >= PERIODIC_CHECK_INTERVAL_FRAMES)
        {
            s_frameCounter = 0;
            s_requested = true;
        }

        if (!s_requested)
            return;
        s_requested = false;

        if (Sandbox.Engine.Platform.Game.IsDedicated)
            return;

        var sdl = SdlInput2Provider.Instance;
        if (sdl == null)
            return;

        // Fullscreen transitions briefly expose stale windowed state.
        if (!sdl.IsWindowed)
            return;

        var render = game?.GameRenderComponent?.RenderThread;
        if (render == null)
            return;

        Vector2I target = sdl.ClientSizePixels;
        if (target.X <= 0 || target.Y <= 0)
            return;

        // The render-thread backbuffer value avoids duplicate in-flight requests.
        Vector2I backbuffer = MyRenderProxy.BackBufferResolution;
        if (backbuffer == target)
            return;

        MyRenderDeviceSettings current = render.CurrentSettings;
        current.BackBufferWidth = target.X;
        current.BackBufferHeight = target.Y;
        MyLog.Default.WriteLine(
            $"Backbuffer resize: {backbuffer.X}x{backbuffer.Y} -> {target.X}x{target.Y} (mode={current.WindowMode})"
        );

        game.SwitchSettings(current);
    }
}

[HarmonyPatch(typeof(MySandboxGame), "Update")]
[HarmonyPatchCategory("Finish")]
static class MySandboxGameUpdatePatch
{
    static void Prefix(MySandboxGame __instance)
    {
        BackbufferResizeRequest.ProcessIfRequested(__instance);
    }
}

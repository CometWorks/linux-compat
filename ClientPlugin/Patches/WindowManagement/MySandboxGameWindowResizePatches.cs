using ClientPlugin.Patches.PlatformGuards;
using HarmonyLib;
using Sandbox;
using Sandbox.Engine.Platform.VideoMode;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Patches.WindowManagement;

// Reconciles the backbuffer from SDL drawable size on the game thread.
// Same-mode resize flow is one-way from window to backbuffer to avoid feedback.
internal static class BackbufferResizeRequest
{
    private static readonly object Sync = new object();
    private static bool s_requested;
    private static int s_pendingModeChanges;

    // The render thread applies a switch frames later, so the backbuffer
    // still reports the old size while a resize is in flight.
    private static Vector2I? s_inFlight;

    public static void Request()
    {
        lock (Sync)
        {
            if (s_pendingModeChanges == 0)
                s_requested = true;
        }
    }

    public static void BeginModeChange()
    {
        lock (Sync)
        {
            s_pendingModeChanges++;
            s_requested = false;
            s_inFlight = null;
        }
    }

    public static void CompleteModeChange()
    {
        lock (Sync)
        {
            if (--s_pendingModeChanges == 0)
                s_requested = true;
        }
    }

    public static void ProcessIfRequested(MySandboxGame game)
    {
        lock (Sync)
        {
            if (!s_requested)
                return;
            s_requested = false;
        }

        if (Sandbox.Engine.Platform.Game.IsDedicated)
            return;

        var sdl = SdlInput2Provider.Instance;
        if (sdl == null)
            return;

        var render = game?.GameRenderComponent?.RenderThread;
        if (render == null)
            return;

        Vector2I target = sdl.ClientSizePixels;
        if (target.X <= 0 || target.Y <= 0)
            return;

        MyRenderDeviceSettings current = MyVideoSettingsManager.CurrentDeviceSettings;
        if (current.BackBufferWidth <= 0 || current.BackBufferHeight <= 0)
            current = render.CurrentSettings;
        if (current.BackBufferWidth <= 0 || current.BackBufferHeight <= 0)
            return;

        Vector2I backbuffer = MyRenderProxy.BackBufferResolution;
        lock (Sync)
        {
            if (s_inFlight == backbuffer)
                s_inFlight = null;
            if (target == (s_inFlight ?? backbuffer))
                return;
            s_inFlight = target;
        }

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

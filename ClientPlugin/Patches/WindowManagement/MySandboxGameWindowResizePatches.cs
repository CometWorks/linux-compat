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

        // The render-thread backbuffer value avoids duplicate in-flight requests.
        Vector2I backbuffer = MyRenderProxy.BackBufferResolution;
        if (backbuffer == target)
            return;

        MyRenderDeviceSettings current = MyVideoSettingsManager.CurrentDeviceSettings;
        if (current.BackBufferWidth <= 0 || current.BackBufferHeight <= 0)
            current = render.CurrentSettings;
        if (current.BackBufferWidth <= 0 || current.BackBufferHeight <= 0)
            return;
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

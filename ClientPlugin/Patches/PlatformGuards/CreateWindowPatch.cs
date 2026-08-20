using System;
using System.Threading;
using ClientPlugin.Compatibility;
using HarmonyLib;
using Sandbox;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using VRage;
using VRage.Ansel;
using VRage.Platform.Windows;
using VRage.Platform.Windows.Forms;
using VRageRender;

namespace ClientPlugin.Patches.PlatformGuards;

[HarmonyPatch(typeof(MySandboxGame), "InitializeRenderThread")]
[HarmonyPatchCategory("Finish")]
static class CreateWindowPatch
{
    static bool Prefix(MySandboxGame __instance, ref IVRageWindow __result)
    {
        __instance.DrawThread = Thread.CurrentThread;

        // Resolve geometry before the first map to avoid a visible jump.
        ResolveInitialGeometry(out int initialW, out int initialH, out int? initialX, out int? initialY);

        // SDL window creation is synchronous and confined to its owner thread.
        var sdlWindow = SdlGameWindow.Create("Space Engineers", initialW, initialH, initialX, initialY);
        SdlInput2Provider.Instance = sdlWindow;

        var windows = (MyWindowsWindows)MyVRage.Platform.Windows;
        windows.Window = sdlWindow;
        windows.WindowHandle = sdlWindow.Handle;

        var platform = MyVRage.Platform as MyVRagePlatform;
        if (platform != null)
        {
            platform.Input = sdlWindow;

            var ansel = platform.Ansel as MyAnsel;
            if (ansel != null)
                ansel.WindowHandle = sdlWindow.Handle;
        }

        __result = sdlWindow;

        // MySandboxGame.Update reaches window housekeeping through `form`.
        __instance.form = sdlWindow;

        sdlWindow.OnManualWindowCloseRequest += () =>
        {
            if (IsInGame())
            {
                __instance.Window_OnManualWindowCloseRequest();
                return;
            }

            sdlWindow.Hide();
            sdlWindow.CloseManually();
        };

        sdlWindow.OnExit += () =>
        {
            __instance.OnExit();
        };

        __instance.UpdateMouseCapture();

        var config = MySandboxGame.Config;
        if (config.SyncRendering)
        {
            var viewport = new MyViewport(0f, 0f, config.ScreenWidth.Value, config.ScreenHeight.Value);
            __instance.RenderThread_SizeChanged((int)viewport.Width, (int)viewport.Height, viewport);
        }

        Console.WriteLine("[LinuxCompat] SDL3 window initialized via InitializeRenderThread");
        return false;
    }

    private static bool IsInGame()
    {
        var gameplayScreen = MyGuiScreenGamePlay.Static;
        return gameplayScreen != null
            && gameplayScreen.LoadingDone
            && MySandboxGame.IsGameReady
            && !MyScreenManager.ExistsScreenOfType(typeof(MyGuiScreenLoading));
    }

    // Prefer saved window geometry, then configured size, then 1280x720.
    private static void ResolveInitialGeometry(out int width, out int height, out int? x, out int? y)
    {
        width = 1280;
        height = 720;
        x = null;
        y = null;

        var config = MySandboxGame.Config;
        if (config != null)
        {
            int? sw = config.ScreenWidth;
            int? sh = config.ScreenHeight;
            if (sw.HasValue && sw.Value > 0) width = sw.Value;
            if (sh.HasValue && sh.Value > 0) height = sh.Value;
        }

        if (PluginWindowConfig.TryGetWindowedSize(out int savedW, out int savedH)
            && savedW > 0 && savedH > 0)
        {
            width = savedW;
            height = savedH;
        }

        bool havePos = PluginWindowConfig.TryGetWindowedPosition(out int savedX, out int savedY);
        if (havePos)
        {
            x = savedX;
            y = savedY;
        }

        // Clamp against the primary display before the window has an assigned display.
        if (TryGetPrimaryDisplayBounds(out int dx, out int dy, out int dw, out int dh)
            && dw >= 640 && dh >= 480)
        {
            if (width > dw) width = dw;
            if (height > dh) height = dh;
            if (!havePos)
            {
                x = dx + (dw - width) / 2;
                y = dy + (dh - height) / 2;
            }
            else
            {
                if (x!.Value < dx) x = dx;
                if (y!.Value < dy) y = dy;
                if (x!.Value + width > dx + dw) x = dx + dw - width;
                if (y!.Value + height > dy + dh) y = dy + dh - height;
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("libSDL3.so", EntryPoint = "SDL_GetPrimaryDisplay")]
    private static extern uint SDL_GetPrimaryDisplay();

    [System.Runtime.InteropServices.DllImport("libSDL3.so", EntryPoint = "SDL_GetDisplayBounds")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I1)]
    private static extern bool SDL_GetDisplayBounds(uint displayId, out SdlRectNative rect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SdlRectNative { public int X, Y, W, H; }

    // SDL video queries share X11 state and must run on the SDL thread.
    private static bool TryGetPrimaryDisplayBounds(out int x, out int y, out int w, out int h)
    {
        var result = TryGetPrimaryDisplayBoundsOnRenderThread();
        x = result.X;
        y = result.Y;
        w = result.W;
        h = result.H;
        return result.Ok;
    }

    private static (bool Ok, int X, int Y, int W, int H) TryGetPrimaryDisplayBoundsOnRenderThread()
    {
        try
        {
            return Compatibility.SdlRenderThread.Invoke(() =>
            {
                uint id = SDL_GetPrimaryDisplay();
                if (id == 0)
                    return (false, 0, 0, 0, 0);
                if (!SDL_GetDisplayBounds(id, out SdlRectNative r))
                    return (false, 0, 0, 0, 0);
                if (r.W <= 0 || r.H <= 0)
                    return (false, 0, 0, 0, 0);
                return (true, r.X, r.Y, r.W, r.H);
            });
        }
        catch
        {
            return (false, 0, 0, 0, 0);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using VRage.Utils;

namespace ClientPlugin.Compatibility;

/// <summary>
/// Owns SDL video, windows, event pumping, and clipboard access. All SDL video
/// calls use this thread to protect SDL's X11 connection state.
/// </summary>
internal static class SdlRenderThread
{
    private const string Lib = "libSDL3.so";
    private const uint SDL_INIT_VIDEO = 0x20u;

    // Avoid busy-spinning while retaining sub-frame input polling.
    private const int LOOP_SLEEP_MS = 1;

    // Treat SDL initialization that exceeds ten seconds as a native deadlock.
    private const int START_TIMEOUT_MS = 10_000;

    private static Thread s_thread;
    private static volatile int s_threadManagedId;
    private static volatile bool s_running;
    private static volatile bool s_initOk;

    private static readonly object s_queueLock = new object();
    private static readonly Queue<Action> s_queue = new Queue<Action>();
    private static readonly ManualResetEventSlim s_started = new ManualResetEventSlim(false);

    /// <summary>
    /// Receives parsed SDL events on the render thread after the game window
    /// is created.
    /// </summary>
    internal delegate void SdlEventHandlerDelegate(ref SdlEvent ev);
    internal static SdlEventHandlerDelegate EventHandler;

    /// <summary>
    /// Runs after each event-pump pass to refresh cached SDL state.
    /// </summary>
    internal static Action MouseSnapshotCallback;

    /// <summary>True if the calling thread is the SDL render thread.</summary>
    internal static bool IsCurrent =>
        s_threadManagedId != 0 && Thread.CurrentThread.ManagedThreadId == s_threadManagedId;

    /// <summary>True once SDL_Init succeeded.</summary>
    internal static bool IsInitialized => s_initOk;

    /// <summary>
    /// Starts SDL once and blocks until initialization completes.
    /// </summary>
    internal static void Start()
    {
        if (s_thread != null)
            return;

        var thread = new Thread(Run)
        {
            // Keep the SDL context alive for the process lifetime.
            IsBackground = false,
            Name = "LinuxCompat-SDL",
        };
        s_thread = thread;
        thread.Start();

        // s_started is signaled for both success and failure; timeout means
        // native loading or SDL_Init is wedged.
        if (!s_started.Wait(START_TIMEOUT_MS))
        {
            Console.Error.WriteLine(
                $"[LinuxCompat] SdlRenderThread.Start: SDL_Init did not complete within {START_TIMEOUT_MS / 1000} s. "
                    + "The render thread is wedged; killing the process to surface the failure."
            );
            try
            {
                Console.Error.Flush();
            }
            catch { }
            // Runtime shutdown can block when a thread is stuck in native code.
            try
            {
                Process.GetCurrentProcess().Kill();
            }
            catch { }
            // Fallback if Process.Kill returns.
            Environment.FailFast("SdlRenderThread SDL_Init timeout");
        }
    }

    /// <summary>
    /// Stops and joins the render thread. Safe from any thread.
    /// </summary>
    internal static void Stop()
    {
        if (s_thread == null)
            return;

        s_running = false;
        Dispatch(static () => { });

        if (!IsCurrent)
            s_thread.Join();

        s_thread = null;
        s_threadManagedId = 0;
    }

    /// <summary>
    /// Queues an action, or runs it inline when already on the render thread.
    /// </summary>
    internal static void Dispatch(Action action)
    {
        if (action == null)
            return;

        if (IsCurrent)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LogException("Dispatch (inline)", ex);
            }
            return;
        }

        lock (s_queueLock)
        {
            s_queue.Enqueue(action);
            Monitor.Pulse(s_queueLock);
        }
    }

    /// <summary>
    /// Runs an action on the render thread and propagates its exception.
    /// </summary>
    internal static void Invoke(Action action)
    {
        if (action == null)
            return;

        if (IsCurrent)
        {
            action();
            return;
        }

        using var done = new ManualResetEventSlim(false);
        Exception captured = null;

        Dispatch(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
            finally
            {
                done.Set();
            }
        });

        done.Wait();
        if (captured != null)
            throw new InvalidOperationException(
                "SdlRenderThread.Invoke target threw an exception",
                captured
            );
    }

    /// <summary>
    /// Runs a function synchronously on the render thread and returns its result.
    /// </summary>
    internal static T Invoke<T>(Func<T> func)
    {
        T result = default;
        Invoke(() =>
        {
            result = func();
        });
        return result;
    }

    private static void Run()
    {
        s_threadManagedId = Thread.CurrentThread.ManagedThreadId;

        // DXVK uses the tested X11 path. Set SDL's environment before SDL_Init.
        ForceX11VideoDriver();

        // Long world loads cannot answer _NET_WM_PING. Disable it before
        // window creation to avoid false "not responding" dialogs.
        SDL_SetHint("SDL_VIDEO_X11_NET_WM_PING", "0");

        s_initOk = SDL_Init(SDL_INIT_VIDEO);
        if (!s_initOk)
        {
            Console.WriteLine(
                $"[LinuxCompat] SdlRenderThread SDL_Init(VIDEO) failed: {GetErrorString()}"
            );
        }
        else
        {
            Console.WriteLine("[LinuxCompat] SdlRenderThread initialised SDL3 (video)");
            SdlJoystick.Initialize();
        }

        s_running = true;
        s_started.Set();

        while (s_running)
        {
            DrainQueue();

            if (s_initOk)
            {
                while (SDL_PollEvent(out var ev))
                {
                    try
                    {
                        SdlJoystick.HandleEvent(ev.Type);
                    }
                    catch (Exception ex)
                    {
                        LogException("joystick event", ex);
                    }

                    var handler = EventHandler;
                    if (handler != null)
                    {
                        try
                        {
                            handler(ref ev);
                        }
                        catch (Exception ex)
                        {
                            LogException("event handler", ex);
                        }
                    }
                }

                try
                {
                    MouseSnapshotCallback?.Invoke();
                }
                catch (Exception ex)
                {
                    LogException("mouse snapshot", ex);
                }

                try
                {
                    SdlJoystick.UpdateSnapshot();
                }
                catch (Exception ex)
                {
                    LogException("joystick snapshot", ex);
                }

                try
                {
                    SdlClipboard.PumpRenderThread();
                }
                catch (Exception ex)
                {
                    LogException("clipboard pump", ex);
                }
            }

            // Dispatch pulses wake the timed event-poll wait immediately.
            lock (s_queueLock)
            {
                if (s_queue.Count == 0 && s_running)
                    Monitor.Wait(s_queueLock, LOOP_SLEEP_MS);
            }
        }

        // Process shutdown bypasses orderly SDL teardown.
    }

    private static void DrainQueue()
    {
        // Execute outside the lock so producers can continue enqueueing.
        Action[] batch = null;
        lock (s_queueLock)
        {
            if (s_queue.Count > 0)
            {
                batch = s_queue.ToArray();
                s_queue.Clear();
            }
        }

        if (batch == null)
            return;

        for (int i = 0; i < batch.Length; i++)
        {
            try
            {
                batch[i]();
            }
            catch (Exception ex)
            {
                LogException("queued action", ex);
            }
        }
    }

    private static void ForceX11VideoDriver()
    {
        IntPtr env = SDL_GetEnvironment();
        if (env != IntPtr.Zero)
            SDL_SetEnvironmentVariable(env, "SDL_VIDEODRIVER", "x11", true);
    }

    private static void LogException(string where, Exception ex)
    {
        try
        {
            MyLog.Default?.WriteLineAndConsole($"[LinuxCompat] SdlRenderThread {where}: {ex}");
        }
        catch { }
    }

    private static string GetErrorString()
    {
        IntPtr error = SDL_GetError();
        if (error == IntPtr.Zero)
            return "Unknown SDL3 error";
        return Marshal.PtrToStringUTF8(error) ?? "Unknown SDL3 error";
    }

    #region Shared SDL event structs

    /// <summary>
    /// SDL_Event union with explicit offsets for the handled event types.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    internal struct SdlEvent
    {
        [FieldOffset(0)]
        internal uint Type;

        [FieldOffset(0)]
        internal SdlWindowEvent Window;

        [FieldOffset(0)]
        internal SdlKeyboardEvent Keyboard;

        [FieldOffset(0)]
        internal SdlTextInputEvent Text;

        [FieldOffset(0)]
        internal SdlMouseWheelEvent Wheel;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdlWindowEvent
    {
        internal uint Type;
        internal uint Reserved;
        internal ulong Timestamp;
        internal uint WindowId;
        internal int Data1;
        internal int Data2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdlKeyboardEvent
    {
        internal uint Type;
        internal uint Reserved;
        internal ulong Timestamp;
        internal uint WindowId;
        internal uint Which;
        internal uint Scancode;
        internal uint Key;
        internal ushort Mod;
        internal ushort Raw;

        [MarshalAs(UnmanagedType.I1)]
        internal bool Down;

        [MarshalAs(UnmanagedType.I1)]
        internal bool Repeat;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdlTextInputEvent
    {
        internal uint Type;
        internal uint Reserved;
        internal ulong Timestamp;
        internal uint WindowId;
        internal IntPtr Text;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdlMouseWheelEvent
    {
        internal uint Type;
        internal uint Reserved;
        internal ulong Timestamp;
        internal uint WindowId;
        internal uint Which;
        internal float X;
        internal float Y;
        internal uint Direction;
        internal float MouseX;
        internal float MouseY;
        internal int IntegerX;
        internal int IntegerY;
    }

    #endregion

    #region SDL3 P/Invoke (init / pump)

    [DllImport(Lib, EntryPoint = "SDL_Init")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_Init(uint flags);

    [DllImport(Lib, EntryPoint = "SDL_SetHint", CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetHint(string name, string value);

    [DllImport(Lib, EntryPoint = "SDL_GetEnvironment")]
    private static extern IntPtr SDL_GetEnvironment();

    [DllImport(Lib, EntryPoint = "SDL_SetEnvironmentVariable", CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetEnvironmentVariable(
        IntPtr environment,
        string name,
        string value,
        [MarshalAs(UnmanagedType.I1)] bool overwrite
    );

    [DllImport(Lib, EntryPoint = "SDL_PollEvent")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_PollEvent(out SdlEvent sdlEvent);

    [DllImport(Lib, EntryPoint = "SDL_GetError")]
    private static extern IntPtr SDL_GetError();

    #endregion
}

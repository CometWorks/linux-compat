using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using VRage.Utils;

namespace ClientPlugin.Compatibility;

/// <summary>
/// Dedicated thread that owns the SDL3 library for the entire process
/// lifetime. Started once from <see cref="Plugin.Init"/> and runs until
/// process exit.
///
/// Why a dedicated thread:
///  - SDL3 video / window / event-pump / clipboard functions must be issued
///    by a single thread (the one that initialised video). Calling them from
///    multiple threads — even with our own external lock — risks racing the
///    X11/Wayland connection state SDL holds internally.
///  - Initialising SDL once early (before splash and before the SE render
///    thread is created) lets us share the same SDL context across the
///    splash window, the main game window, and clipboard access.
///  - Both `MySdlSplashScreen` and `SdlGameWindow` are constructed on this
///    thread via <see cref="Invoke"/>; afterwards every SDL3 call coming
///    from main / SE render thread / worker threads is funnelled through
///    <see cref="Dispatch"/> or <see cref="Invoke"/>.
///
/// Loop:
///  1. Drain queued actions (mode changes, window create/destroy, clipboard
///     sets, etc.) on this thread.
///  2. Pump SDL events with `SDL_PollEvent` and forward each to the
///     registered <see cref="EventHandler"/> (see <see cref="SdlGameWindow"/>).
///  3. Refresh the shared mouse / window-size snapshot via
///     <see cref="MouseSnapshotCallback"/> so off-thread readers don't have
///     to dispatch for every frame.
///  4. Sleep briefly to keep the loop cooperative.
/// </summary>
internal static class SdlRenderThread
{
    private const string Lib = "libSDL3.so";
    private const uint SDL_INIT_VIDEO = 0x20u;

    // Loop sleep. 1ms is responsive enough for a 60+ FPS game and keeps the
    // thread from busy-spinning. SDL's own internal buffering coalesces
    // events between iterations.
    private const int LOOP_SLEEP_MS = 1;

    // Maximum time Start() waits for the render thread to finish SDL_Init
    // and signal s_started. SDL_Init normally completes in well under a
    // second; a 10 s budget is generous enough to absorb a slow X11 server
    // or sluggish dlopen of libSDL3 + transitive deps without false-failing,
    // but short enough that a hung init terminates the process while the
    // user is still in front of the screen rather than letting them stare
    // at a frozen window. On timeout we treat the init as fatal: write a
    // diagnostic to stderr (Pulsar's launcher captures it) and kill the
    // process so the user sees a clear failure rather than an opaque hang.
    private const int START_TIMEOUT_MS = 10_000;

    private static Thread s_thread;
    private static volatile int s_threadManagedId;
    private static volatile bool s_running;
    private static volatile bool s_initOk;

    private static readonly object s_queueLock = new object();
    private static readonly Queue<Action> s_queue = new Queue<Action>();
    private static readonly ManualResetEventSlim s_started = new ManualResetEventSlim(false);

    /// <summary>
    /// Receives parsed SDL events polled by the render-thread loop. Invoked
    /// on the render thread. Set by <see cref="SdlGameWindow"/> after the
    /// game window is created.
    /// </summary>
    internal delegate void SdlEventHandlerDelegate(ref SdlEvent ev);
    internal static SdlEventHandlerDelegate EventHandler;

    /// <summary>
    /// Per-iteration callback invoked on the render thread, after events are
    /// pumped. <see cref="SdlGameWindow"/> uses this to refresh the cached
    /// mouse / window-size snapshot from SDL state without forcing readers
    /// to dispatch synchronously.
    /// </summary>
    internal static Action MouseSnapshotCallback;

    /// <summary>True if the calling thread is the SDL render thread.</summary>
    internal static bool IsCurrent =>
        s_threadManagedId != 0 && Thread.CurrentThread.ManagedThreadId == s_threadManagedId;

    /// <summary>True once SDL_Init succeeded.</summary>
    internal static bool IsInitialized => s_initOk;

    /// <summary>
    /// Spawn the render thread and run SDL_Init on it. Idempotent — calling
    /// twice is a no-op. Blocks the caller until SDL is initialised so the
    /// next call site can issue SDL operations via Invoke without races.
    /// </summary>
    internal static void Start()
    {
        if (s_thread != null)
            return;

        var thread = new Thread(Run)
        {
            // Foreground so the SDL context outlives `Plugin.Dispose`. The
            // thread terminates itself when `Stop` is called from process
            // exit, otherwise it persists for the game's lifetime.
            IsBackground = false,
            Name = "LinuxCompat-SDL",
        };
        s_thread = thread;
        thread.Start();

        // Bounded wait so a hang in SDL_Init / X11 connection setup
        // surfaces as a logged failure rather than an opaque deadlock at
        // first SDL3 use. The render thread sets s_started unconditionally
        // (whether SDL_Init succeeded or failed), so reaching the timeout
        // means the thread is wedged before that point — typically in the
        // dlopen of libSDL3.so or inside SDL_Init itself.
        if (!s_started.Wait(START_TIMEOUT_MS))
        {
            Console.Error.WriteLine(
                $"[LinuxCompat] SdlRenderThread.Start: SDL_Init did not complete within {START_TIMEOUT_MS / 1000} s. " +
                "The render thread is wedged; killing the process to surface the failure.");
            try { Console.Error.Flush(); } catch { }
            // SIGKILL via Process.Kill — Environment.Exit / FailFast can
            // themselves block on runtime shutdown if a thread is stuck in
            // unmanaged code. We want a hard, unconditional kill here.
            try { Process.GetCurrentProcess().Kill(); } catch { }
            // Defensive fallback if Kill() somehow returns without ending
            // the process (it shouldn't on Linux).
            Environment.FailFast("SdlRenderThread SDL_Init timeout");
        }
    }

    /// <summary>
    /// Request the render thread to terminate and join it. Called from
    /// process shutdown paths; safe to call from any thread.
    /// </summary>
    internal static void Stop()
    {
        if (s_thread == null)
            return;

        s_running = false;
        // Wake the loop so it observes `s_running == false` immediately.
        Dispatch(static () => { });

        if (!IsCurrent)
            s_thread.Join();

        s_thread = null;
        s_threadManagedId = 0;
    }

    /// <summary>
    /// Queue an action for execution on the render thread. Returns
    /// immediately. If the caller is already on the render thread, runs
    /// inline so re-entrant calls (event handler → SetClientSize → etc.)
    /// don't deadlock.
    /// </summary>
    internal static void Dispatch(Action action)
    {
        if (action == null)
            return;

        if (IsCurrent)
        {
            try { action(); }
            catch (Exception ex) { LogException("Dispatch (inline)", ex); }
            return;
        }

        lock (s_queueLock)
        {
            s_queue.Enqueue(action);
            Monitor.Pulse(s_queueLock);
        }
    }

    /// <summary>
    /// Synchronously execute an action on the render thread. Blocks the
    /// caller until completion and rethrows any exception thrown by the
    /// action.
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
            try { action(); }
            catch (Exception ex) { captured = ex; }
            finally { done.Set(); }
        });

        done.Wait();
        if (captured != null)
            throw new InvalidOperationException(
                "SdlRenderThread.Invoke target threw an exception", captured);
    }

    /// <summary>
    /// Synchronously execute a function on the render thread and return its
    /// result.
    /// </summary>
    internal static T Invoke<T>(Func<T> func)
    {
        T result = default;
        Invoke(() => { result = func(); });
        return result;
    }

    private static void Run()
    {
        s_threadManagedId = Thread.CurrentThread.ManagedThreadId;

        // Force the X11 video driver before SDL_Init so SDL doesn't pick
        // Wayland (DXVK + Wayland is not the path we test). This must
        // happen on the same thread that calls SDL_Init since SDL_GetEnvironment
        // returns SDL's own environment view.
        ForceX11VideoDriver();

        // Disable _NET_WM_PING advertisement on X11 — large world loads block
        // the game's main thread for tens of seconds, during which the WM's
        // ping goes unanswered and triggers the "Window not responding"
        // dialog. With this hint the WM removes _NET_WM_PING from
        // WM_PROTOCOLS. Must be set before SDL_CreateWindow; pre-init is
        // safest.
        SDL_SetHint("SDL_VIDEO_X11_NET_WM_PING", "0");

        s_initOk = SDL_Init(SDL_INIT_VIDEO);
        if (!s_initOk)
        {
            Console.WriteLine($"[LinuxCompat] SdlRenderThread SDL_Init(VIDEO) failed: {GetErrorString()}");
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
                    try { SdlJoystick.HandleEvent(ev.Type); }
                    catch (Exception ex) { LogException("joystick event", ex); }

                    var handler = EventHandler;
                    if (handler != null)
                    {
                        try { handler(ref ev); }
                        catch (Exception ex) { LogException("event handler", ex); }
                    }
                }

                try { MouseSnapshotCallback?.Invoke(); }
                catch (Exception ex) { LogException("mouse snapshot", ex); }

                try { SdlJoystick.UpdateSnapshot(); }
                catch (Exception ex) { LogException("joystick snapshot", ex); }

                try { SdlClipboard.PumpRenderThread(); }
                catch (Exception ex) { LogException("clipboard pump", ex); }
            }

            // Wait briefly for either new work or the next pump tick. Using
            // Monitor.Wait with a timeout means dispatched work wakes us
            // immediately; otherwise we tick at LOOP_SLEEP_MS for event
            // polling.
            lock (s_queueLock)
            {
                if (s_queue.Count == 0 && s_running)
                    Monitor.Wait(s_queueLock, LOOP_SLEEP_MS);
            }
        }

        // No SDL_Quit on shutdown: process is exiting and the SE main loop
        // calls Process.Kill (see MySandboxGameExitPatch). SDL state cleanup
        // would only matter for orderly teardown, which we explicitly
        // bypass.
    }

    private static void DrainQueue()
    {
        // Snapshot under lock and execute outside so a long-running action
        // doesn't block other threads from enqueueing.
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
            try { batch[i](); }
            catch (Exception ex) { LogException("queued action", ex); }
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
            MyLog.Default?.WriteLineAndConsole(
                $"[LinuxCompat] SdlRenderThread {where}: {ex}");
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
    /// SDL3 event structure. The layout matches SDL's <c>SDL_Event</c> union
    /// — explicit field offsets give us access to whichever sub-event
    /// applies to the current type.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    internal struct SdlEvent
    {
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(0)] internal SdlWindowEvent Window;
        [FieldOffset(0)] internal SdlKeyboardEvent Keyboard;
        [FieldOffset(0)] internal SdlTextInputEvent Text;
        [FieldOffset(0)] internal SdlMouseWheelEvent Wheel;
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
        [MarshalAs(UnmanagedType.I1)] internal bool Down;
        [MarshalAs(UnmanagedType.I1)] internal bool Repeat;
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
    private static extern bool SDL_SetEnvironmentVariable(IntPtr environment, string name, string value, [MarshalAs(UnmanagedType.I1)] bool overwrite);

    [DllImport(Lib, EntryPoint = "SDL_PollEvent")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_PollEvent(out SdlEvent sdlEvent);

    [DllImport(Lib, EntryPoint = "SDL_GetError")]
    private static extern IntPtr SDL_GetError();

    #endregion
}

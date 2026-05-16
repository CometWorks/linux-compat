using System;
using System.Collections.Concurrent;
using VRage.Utils;

namespace ClientPlugin.Compatibility;

/// <summary>
/// Queue of actions to run on the main game thread. Producers post from any
/// thread (typically <see cref="SdlRenderThread"/>); the queue is drained
/// every frame from <c>Plugin.Update()</c>, which Pulsar invokes on the main
/// game thread.
///
/// Used to deliver clipboard-read continuations back to the requester
/// without blocking it on the SDL round-trip — see
/// <see cref="SdlClipboard.RequestText"/>.
/// </summary>
internal static class MainThreadDispatcher
{
    private static readonly ConcurrentQueue<Action> s_queue = new();

    /// <summary>Post a continuation to run on the next main-thread tick.</summary>
    public static void Post(Action action)
    {
        if (action == null)
            return;
        s_queue.Enqueue(action);
    }

    /// <summary>Drain the queue. Must be called on the main game thread.</summary>
    public static void Pump()
    {
        while (s_queue.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex)
            {
                try { MyLog.Default?.WriteLineAndConsole($"[LinuxCompat] MainThreadDispatcher action threw: {ex}"); } catch { }
            }
        }
    }
}

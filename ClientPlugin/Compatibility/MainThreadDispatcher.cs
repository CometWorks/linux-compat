using System;
using System.Collections.Concurrent;
using VRage.Utils;

namespace ClientPlugin.Compatibility;

/// <summary>
/// Cross-thread queue drained by <c>Plugin.Update()</c> on the game thread.
/// </summary>
internal static class MainThreadDispatcher
{
    private static readonly ConcurrentQueue<Action> s_queue = new();

    /// <summary>Posts a continuation for the next main-thread tick.</summary>
    public static void Post(Action action)
    {
        if (action == null)
            return;
        s_queue.Enqueue(action);
    }

    /// <summary>Drains the queue on the main game thread.</summary>
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

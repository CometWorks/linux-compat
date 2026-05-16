using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using VRage.Utils;

namespace ClientPlugin.Compatibility;

/// <summary>
/// SDL3-backed clipboard for Linux. Replaces the stock
/// <c>VRage.Platform.Windows.Forms.MyClipboardHelper</c> path which goes
/// through <c>System.Windows.Forms.Clipboard</c> + COM/OLE on an STA worker
/// thread. That path is fundamentally Windows-only: WinForms is unavailable on
/// the .NET 9 Linux runtime the game ships, and even when types resolve the
/// Win32 P/Invokes (<c>OleSetClipboard</c>, <c>RegisterClipboardFormat</c>,
/// <c>user32.dll</c>) terminate the process. The recompiled Linux build of
/// the game replaces the helper with an in-process string cache; we go a step
/// further and integrate with the actual desktop clipboard via SDL3 so
/// copy/paste interoperates with other applications.
///
/// Thread affinity:
///  - All SDL3 calls in this plugin are funnelled through
///    <see cref="SdlRenderThread"/>. The clipboard is no exception — X11
///    selection / Wayland data-device traffic piggy-backs on the SDL event
///    loop running there.
///  - On the render thread, <see cref="GetText"/> / <see cref="SetText"/> /
///    <see cref="HasText"/> call SDL3 directly.
///  - Off the render thread, sets are queued and drained by
///    <see cref="PumpRenderThread"/> (called from
///    <c>SdlRenderThread.Run</c>'s loop). The in-process cache is updated
///    synchronously so a subsequent <c>Get</c> on any thread observes the
///    value just written. Off-thread reads of the legacy synchronous getter
///    return the cache (last value the render thread observed or wrote)
///    without driving SDL — kept for property-getter compatibility, but it
///    can return stale data if another application has changed the OS
///    clipboard since the last render-thread access.
///  - <see cref="RequestText"/> is the non-blocking fresh-read API: the SDL
///    call is dispatched to the render thread; the result is posted to
///    <see cref="MainThreadDispatcher"/>, which delivers the callback on the
///    main game thread during the next <c>Plugin.Update()</c>. Paste
///    handlers (textbox, multiline, GPS) use this so the OS clipboard is
///    actually consulted on Ctrl+V without blocking the main thread.
/// </summary>
internal static class SdlClipboard
{
    private const string Lib = "libSDL3.so";

    private static string s_cachedText = string.Empty;
    private static readonly object s_cacheLock = new object();
    private static readonly ConcurrentQueue<string> s_pendingSets = new ConcurrentQueue<string>();

    public static string GetText()
    {
        if (!SdlRenderThread.IsCurrent)
        {
            // Off-thread reads serve from cache to keep this fast. The
            // cache is refreshed every time the render thread reads or
            // writes the system clipboard.
            lock (s_cacheLock)
                return s_cachedText;
        }

        try
        {
            IntPtr ptr = SDL_GetClipboardText();
            try
            {
                string value = ptr == IntPtr.Zero ? string.Empty : (Marshal.PtrToStringUTF8(ptr) ?? string.Empty);
                lock (s_cacheLock)
                    s_cachedText = value;
                return value;
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                    SDL_free(ptr);
            }
        }
        catch (Exception ex)
        {
            // libSDL3.so missing or symbol mismatch — fall back to cache so
            // copy-within-game keeps working even if the native binding fails.
            try { MyLog.Default?.WriteLineAndConsole($"[LinuxCompat] SdlClipboard.GetText failed: {ex.Message}"); } catch { }
            lock (s_cacheLock)
                return s_cachedText;
        }
    }

    public static void SetText(string text)
    {
        text ??= string.Empty;

        // Update the cache first so any subsequent read (even from another
        // thread before the render-thread pump runs) observes the new value.
        lock (s_cacheLock)
            s_cachedText = text;

        if (!SdlRenderThread.IsCurrent)
        {
            s_pendingSets.Enqueue(text);
            return;
        }

        SetTextOnRenderThread(text);
    }

    public static bool HasText()
    {
        if (!SdlRenderThread.IsCurrent)
        {
            lock (s_cacheLock)
                return !string.IsNullOrEmpty(s_cachedText);
        }

        try
        {
            return SDL_HasClipboardText();
        }
        catch (Exception ex)
        {
            try { MyLog.Default?.WriteLineAndConsole($"[LinuxCompat] SdlClipboard.HasText failed: {ex.Message}"); } catch { }
            lock (s_cacheLock)
                return !string.IsNullOrEmpty(s_cachedText);
        }
    }

    /// <summary>
    /// Non-blocking fresh-read API. The SDL clipboard read is dispatched to
    /// the render thread; the result (or <c>null</c> if the clipboard is
    /// empty / SDL failed and no cached value is available) is posted to
    /// <see cref="MainThreadDispatcher"/>, which invokes the callback on the
    /// main game thread during the next <c>Plugin.Update()</c>. Use this
    /// from paste handlers — never the synchronous <see cref="GetText"/>,
    /// which returns stale cache off the render thread.
    /// </summary>
    public static void RequestText(Action<string> callback)
    {
        if (callback == null)
            return;

        SdlRenderThread.Dispatch(() =>
        {
            string result = null;
            try
            {
                IntPtr ptr = SDL_GetClipboardText();
                try
                {
                    result = ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);
                    if (!string.IsNullOrEmpty(result))
                    {
                        lock (s_cacheLock)
                            s_cachedText = result;
                    }
                }
                finally
                {
                    if (ptr != IntPtr.Zero)
                        SDL_free(ptr);
                }
            }
            catch (Exception ex)
            {
                try { MyLog.Default?.WriteLineAndConsole($"[LinuxCompat] SdlClipboard.RequestText failed: {ex.Message}"); } catch { }
                lock (s_cacheLock)
                    result = string.IsNullOrEmpty(s_cachedText) ? null : s_cachedText;
            }

            string captured = result;
            MainThreadDispatcher.Post(() => callback(captured));
        });
    }

    /// <summary>
    /// Drains pending off-render-thread clipboard sets. Called from the
    /// render-thread loop every iteration.
    /// </summary>
    public static void PumpRenderThread()
    {
        // Coalesce: only the most recent enqueued value matters for the
        // system clipboard. Drain everything but only push the last.
        string latest = null;
        bool any = false;
        while (s_pendingSets.TryDequeue(out var value))
        {
            latest = value;
            any = true;
        }

        if (any)
            SetTextOnRenderThread(latest ?? string.Empty);
    }

    private static void SetTextOnRenderThread(string text)
    {
        try
        {
            SDL_SetClipboardText(text);
        }
        catch (Exception ex)
        {
            try { MyLog.Default?.WriteLineAndConsole($"[LinuxCompat] SdlClipboard.SetText failed: {ex.Message}"); } catch { }
        }
    }

    [DllImport(Lib, EntryPoint = "SDL_GetClipboardText")]
    private static extern IntPtr SDL_GetClipboardText();

    [DllImport(Lib, EntryPoint = "SDL_SetClipboardText")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetClipboardText([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

    [DllImport(Lib, EntryPoint = "SDL_HasClipboardText")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_HasClipboardText();

    [DllImport(Lib, EntryPoint = "SDL_free")]
    private static extern void SDL_free(IntPtr mem);
}

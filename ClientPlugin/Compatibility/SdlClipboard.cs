using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using VRage.Utils;

namespace ClientPlugin.Compatibility;

/// <summary>
/// SDL3 clipboard access serialized through <see cref="SdlRenderThread"/>.
/// Synchronous off-thread reads use a cache; <see cref="RequestText"/> returns
/// fresh text asynchronously on the game thread.
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
            // SDL access is confined to the render thread.
            lock (s_cacheLock)
                return s_cachedText;
        }

        try
        {
            IntPtr ptr = SDL_GetClipboardText();
            try
            {
                string value =
                    ptr == IntPtr.Zero
                        ? string.Empty
                        : (Marshal.PtrToStringUTF8(ptr) ?? string.Empty);
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
            // Preserve in-game clipboard access if the native binding fails.
            try
            {
                MyLog.Default?.WriteLineAndConsole(
                    $"[LinuxCompat] SdlClipboard.GetText failed: {ex.Message}"
                );
            }
            catch { }
            lock (s_cacheLock)
                return s_cachedText;
        }
    }

    public static void SetText(string text)
    {
        text ??= string.Empty;

        // Publish the value before the render-thread pump reaches SDL.
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
            try
            {
                MyLog.Default?.WriteLineAndConsole(
                    $"[LinuxCompat] SdlClipboard.HasText failed: {ex.Message}"
                );
            }
            catch { }
            lock (s_cacheLock)
                return !string.IsNullOrEmpty(s_cachedText);
        }
    }

    /// <summary>
    /// Reads SDL clipboard text on the render thread and invokes the callback
    /// on the next game-thread update. Off-thread <see cref="GetText"/> is cached.
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
                try
                {
                    MyLog.Default?.WriteLineAndConsole(
                        $"[LinuxCompat] SdlClipboard.RequestText failed: {ex.Message}"
                    );
                }
                catch { }
                lock (s_cacheLock)
                    result = string.IsNullOrEmpty(s_cachedText) ? null : s_cachedText;
            }

            string captured = result;
            MainThreadDispatcher.Post(() => callback(captured));
        });
    }

    /// <summary>
    /// Applies pending clipboard writes from the render thread.
    /// </summary>
    public static void PumpRenderThread()
    {
        // Only the newest pending clipboard value matters.
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
            try
            {
                MyLog.Default?.WriteLineAndConsole(
                    $"[LinuxCompat] SdlClipboard.SetText failed: {ex.Message}"
                );
            }
            catch { }
        }
    }

    [DllImport(Lib, EntryPoint = "SDL_GetClipboardText")]
    private static extern IntPtr SDL_GetClipboardText();

    [DllImport(Lib, EntryPoint = "SDL_SetClipboardText")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetClipboardText(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string text
    );

    [DllImport(Lib, EntryPoint = "SDL_HasClipboardText")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_HasClipboardText();

    [DllImport(Lib, EntryPoint = "SDL_free")]
    private static extern void SDL_free(IntPtr mem);
}

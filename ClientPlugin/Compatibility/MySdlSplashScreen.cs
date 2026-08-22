using System;
using System.IO;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using VRage.FileSystem;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Compatibility;

/// <summary>
/// Linux SDL3 splash screen. <see cref="Show"/> and <see cref="Hide"/> marshal
/// all window operations to <see cref="SdlRenderThread"/>.
/// </summary>
internal sealed class MySdlSplashScreen : IDisposable
{
    private const ulong SdlWindowHidden = 0x8uL;

    private const ulong SdlWindowBorderless = 0x10uL;

    private const ulong SdlWindowAlwaysOnTop = 0x8000uL;

    private const ulong SdlWindowHighPixelDensity = 0x2000uL;

    // SDL_PIXELFORMAT_RGBA32 is the byte-array RGBA alias on little-endian platforms.
    private const uint SdlPixelFormatRgba32 = 0x16762004u;

    private static readonly int SdlWindowPosCentered = unchecked((int)0x2FFF0000u);

    private static MySdlSplashScreen s_current;

    private readonly byte[] m_pixelData;

    private GCHandle m_pixelDataHandle;

    private IntPtr m_windowHandle;

    private bool m_disposed;

    /// <summary>
    /// Replaces and synchronously displays the splash on the SDL thread.
    /// </summary>
    internal static void Show(string image, string gameIcon, Vector2 scale)
    {
        SdlRenderThread.Invoke(() =>
        {
            s_current?.Dispose();
            s_current = new MySdlSplashScreen(image, gameIcon, scale);
        });
    }

    /// <summary>
    /// Hides and disposes the splash on the SDL thread.
    /// </summary>
    internal static void Hide()
    {
        if (!SdlRenderThread.IsInitialized)
            return;

        SdlRenderThread.Invoke(() =>
        {
            s_current?.Dispose();
            s_current = null;
        });
    }

    private MySdlSplashScreen(string image, string gameIcon, Vector2 scale)
    {
        // Splash image paths are Windows-style and relative to Bin64.
        string normalizedImage = image?.Replace('\\', '/');
        string path = Path.Combine(MyFileSystem.ExePath, normalizedImage ?? string.Empty);
        if (!File.Exists(path))
        {
            Console.WriteLine($"[LinuxCompat] Splash screen image not found: '{path}'.");
            MyLog.Default.WriteLine($"Splash screen image not found: '{path}'.");
            return;
        }
        Console.WriteLine($"[LinuxCompat] Splash screen image path resolved: '{path}'");

        if (!SdlRenderThread.IsInitialized)
        {
            Console.WriteLine("[LinuxCompat] Splash screen skipped: SDL3 not initialised.");
            return;
        }

        bool success = false;
        try
        {
            using Image<Rgba32> sourceImage = Image.Load<Rgba32>(path);
            int width = Math.Max(1, (int)MathF.Round(sourceImage.Width * scale.X));
            int height = Math.Max(1, (int)MathF.Round(sourceImage.Height * scale.Y));
            float contentScale = 1f;
            if (!SdlRenderThread.IsWayland)
            {
                contentScale = GetDisplayContentScale(GetPrimaryDisplay());
                if (contentScale <= 0f || !float.IsFinite(contentScale))
                    contentScale = 1f;
                width = Math.Max(1, (int)MathF.Round(width * contentScale));
                height = Math.Max(1, (int)MathF.Round(height * contentScale));
            }

            m_windowHandle = CreateWindow(
                "Space Engineers",
                width,
                height,
                SdlWindowBorderless
                    | SdlWindowAlwaysOnTop
                    | SdlWindowHidden
                    | SdlWindowHighPixelDensity
            );
            if (m_windowHandle == IntPtr.Zero)
            {
                Console.WriteLine($"[LinuxCompat] SDL_CreateWindow failed: {GetErrorString()}");
                return;
            }

            SetWindowAlwaysOnTop(m_windowHandle, true);
            SdlIconHelper.Apply(m_windowHandle, gameIcon);
            SetWindowPosition(m_windowHandle, SdlWindowPosCentered, SdlWindowPosCentered);

            if (SdlRenderThread.IsWayland)
            {
                if (!ShowWindow(m_windowHandle))
                {
                    Console.WriteLine($"[LinuxCompat] SDL_ShowWindow failed: {GetErrorString()}");
                    return;
                }

                if (!SyncWindow(m_windowHandle))
                {
                    Console.WriteLine($"[LinuxCompat] SDL_SyncWindow failed: {GetErrorString()}");
                    return;
                }
            }

            if (
                !GetWindowSizeInPixels(m_windowHandle, out int pixelWidth, out int pixelHeight)
                || pixelWidth <= 0
                || pixelHeight <= 0
            )
            {
                pixelWidth = width;
                pixelHeight = height;
            }

            using Image<Rgba32> splashImage = sourceImage.Clone(context =>
            {
                if (sourceImage.Width != pixelWidth || sourceImage.Height != pixelHeight)
                {
                    context.Resize(pixelWidth, pixelHeight);
                }
            });

            m_pixelData = new byte[pixelWidth * pixelHeight * 4];
            Span<Rgba32> destSpan = MemoryMarshal.Cast<byte, Rgba32>(m_pixelData.AsSpan());
            for (int y = 0; y < pixelHeight; y++)
            {
                Span<Rgba32> row = splashImage.Frames[0].GetPixelRowSpan(y);
                row.CopyTo(destSpan.Slice(y * pixelWidth, pixelWidth));
            }
            m_pixelDataHandle = GCHandle.Alloc(m_pixelData, GCHandleType.Pinned);

            Console.WriteLine(
                $"[LinuxCompat] Splash image loaded: window={width}x{height} pixels={pixelWidth}x{pixelHeight} "
                    + $"contentScale={contentScale:F2}"
            );

            IntPtr surface = CreateSurfaceFrom(
                pixelWidth,
                pixelHeight,
                SdlPixelFormatRgba32,
                m_pixelDataHandle.AddrOfPinnedObject(),
                pixelWidth * 4
            );
            if (surface == IntPtr.Zero)
            {
                Console.WriteLine(
                    $"[LinuxCompat] SDL_CreateSurfaceFrom failed: {GetErrorString()}"
                );
                return;
            }
            Console.WriteLine($"[LinuxCompat] Splash SDL surface created: 0x{surface.ToInt64():X}");

            try
            {
                Console.WriteLine(
                    $"[LinuxCompat] Splash window created: 0x{m_windowHandle.ToInt64():X}"
                );

                IntPtr windowSurface = GetWindowSurface(m_windowHandle);
                Console.WriteLine(
                    $"[LinuxCompat] Splash window surface: 0x{windowSurface.ToInt64():X}"
                );
                if (windowSurface == IntPtr.Zero)
                {
                    Console.WriteLine(
                        $"[LinuxCompat] SDL_GetWindowSurface failed: {GetErrorString()}"
                    );
                    return;
                }

                bool blit = BlitSurface(surface, IntPtr.Zero, windowSurface, IntPtr.Zero);
                Console.WriteLine($"[LinuxCompat] SDL_BlitSurface returned {blit}");
                if (!blit)
                {
                    Console.WriteLine($"[LinuxCompat] SDL_BlitSurface failed: {GetErrorString()}");
                    return;
                }

                if (!SdlRenderThread.IsWayland)
                {
                    bool shown = ShowWindow(m_windowHandle);
                    Console.WriteLine($"[LinuxCompat] SDL_ShowWindow returned {shown}");
                    if (!shown)
                    {
                        Console.WriteLine(
                            $"[LinuxCompat] SDL_ShowWindow failed: {GetErrorString()}"
                        );
                        return;
                    }
                }

                bool updated = UpdateWindowSurface(m_windowHandle);
                Console.WriteLine($"[LinuxCompat] SDL_UpdateWindowSurface returned {updated}");
                if (!updated)
                {
                    Console.WriteLine(
                        $"[LinuxCompat] SDL_UpdateWindowSurface failed: {GetErrorString()}"
                    );
                    return;
                }

                success = true;
                Console.WriteLine($"[LinuxCompat] Splash screen displayed successfully");
            }
            finally
            {
                DestroySurface(surface);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LinuxCompat] Splash screen exception: {ex}");
            MyLog.Default.WriteLine($"Failed to show splash screen '{path}': {ex}");
        }
        finally
        {
            if (!success)
            {
                Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        m_disposed = true;

        if (m_windowHandle != IntPtr.Zero)
        {
            DestroyWindow(m_windowHandle);
            m_windowHandle = IntPtr.Zero;
        }

        if (m_pixelDataHandle.IsAllocated)
        {
            m_pixelDataHandle.Free();
        }
    }

    private const string Lib = "libSDL3.so";

    [DllImport(Lib, EntryPoint = "SDL_GetError")]
    private static extern IntPtr GetError();

    [DllImport(Lib, EntryPoint = "SDL_CreateWindow", CharSet = CharSet.Ansi)]
    private static extern IntPtr CreateWindow(string title, int width, int height, ulong flags);

    [DllImport(Lib, EntryPoint = "SDL_DestroyWindow")]
    private static extern void DestroyWindow(IntPtr window);

    [DllImport(Lib, EntryPoint = "SDL_ShowWindow")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ShowWindow(IntPtr window);

    [DllImport(Lib, EntryPoint = "SDL_SyncWindow")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SyncWindow(IntPtr window);

    [DllImport(Lib, EntryPoint = "SDL_SetWindowAlwaysOnTop")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SetWindowAlwaysOnTop(
        IntPtr window,
        [MarshalAs(UnmanagedType.I1)] bool onTop
    );

    [DllImport(Lib, EntryPoint = "SDL_SetWindowPosition")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SetWindowPosition(IntPtr window, int x, int y);

    [DllImport(Lib, EntryPoint = "SDL_GetWindowSizeInPixels")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool GetWindowSizeInPixels(IntPtr window, out int width, out int height);

    [DllImport(Lib, EntryPoint = "SDL_GetPrimaryDisplay")]
    private static extern uint GetPrimaryDisplay();

    [DllImport(Lib, EntryPoint = "SDL_GetDisplayContentScale")]
    private static extern float GetDisplayContentScale(uint displayId);

    [DllImport(Lib, EntryPoint = "SDL_CreateSurfaceFrom")]
    private static extern IntPtr CreateSurfaceFrom(
        int width,
        int height,
        uint format,
        IntPtr pixels,
        int pitch
    );

    [DllImport(Lib, EntryPoint = "SDL_GetWindowSurface")]
    private static extern IntPtr GetWindowSurface(IntPtr window);

    [DllImport(Lib, EntryPoint = "SDL_BlitSurface")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool BlitSurface(
        IntPtr source,
        IntPtr sourceRect,
        IntPtr destination,
        IntPtr destinationRect
    );

    [DllImport(Lib, EntryPoint = "SDL_UpdateWindowSurface")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool UpdateWindowSurface(IntPtr window);

    [DllImport(Lib, EntryPoint = "SDL_DestroySurface")]
    private static extern void DestroySurface(IntPtr surface);

    private static string GetErrorString()
    {
        IntPtr error = GetError();
        if (error == IntPtr.Zero)
        {
            return "Unknown SDL3 error";
        }

        return Marshal.PtrToStringUTF8(error) ?? "Unknown SDL3 error";
    }
}

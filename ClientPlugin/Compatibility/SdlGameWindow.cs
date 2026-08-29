using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using ClientPlugin.Patches.WindowManagement;
using VRage;
using VRage.Input;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Compatibility;

internal sealed class SdlGameWindow : IVRageWindow, IVRageInput, IVRageInput2
{
    private const string Lib = "libSDL3.so";

    private const ulong SDL_WINDOW_RESIZABLE = 0x20uL;
    private const ulong SDL_WINDOW_HIDDEN = 0x8uL;
    private const ulong SDL_WINDOW_HIGH_PIXEL_DENSITY = 0x2000uL;
    private const ulong SDL_WINDOW_VULKAN = 0x10000000uL;

    private const uint SDL_EVENT_QUIT = 0x100u;

    // SDL3 window event codes from SDL_events.h.
    private const uint SDL_EVENT_WINDOW_MOVED = 0x205u;
    private const uint SDL_EVENT_WINDOW_RESIZED = 0x206u;
    private const uint SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED = 0x207u;
    private const uint SDL_EVENT_WINDOW_MOUSE_ENTER = 0x20Cu;
    private const uint SDL_EVENT_WINDOW_MOUSE_LEAVE = 0x20Du;
    private const uint SDL_EVENT_WINDOW_FOCUS_GAINED = 0x20Eu;
    private const uint SDL_EVENT_WINDOW_FOCUS_LOST = 0x20Fu;
    private const uint SDL_EVENT_WINDOW_CLOSE_REQUESTED = 0x210u;
    private const uint SDL_EVENT_KEY_DOWN = 0x300u;
    private const uint SDL_EVENT_KEY_UP = 0x301u;
    private const uint SDL_EVENT_TEXT_INPUT = 0x303u;
    private const uint SDL_EVENT_MOUSE_WHEEL = 0x403u;

    private const uint SDL_BUTTON_LMASK = 1u << 0;
    private const uint SDL_BUTTON_MMASK = 1u << 1;
    private const uint SDL_BUTTON_RMASK = 1u << 2;
    private const uint SDL_BUTTON_X1MASK = 1u << 3;
    private const uint SDL_BUTTON_X2MASK = 1u << 4;

    // Guards the input snapshot written by SDL and read by game threads.
    private readonly object m_bufferLock = new object();
    private readonly Dictionary<uint, ActionRef<MyMessage>> m_messageHandlers =
        new Dictionary<uint, ActionRef<MyMessage>>();
    private List<char> m_bufferedChars = new List<char>();
    private readonly byte[] m_keyStates = new byte[32];
    private readonly uint m_windowId;

    private Vector2I m_clientSize = new Vector2I(1280, 720);
    private Vector2I m_clientSizePixels = new Vector2I(1280, 720);
    private Vector2 m_mousePosition;
    private uint m_mouseButtonState;
    private float m_relativeDeltaXAccum;
    private float m_relativeDeltaYAccum;
    private int m_mouseWheel;
    private bool m_isVisible = true;
    private bool m_isActive = true;
    private bool m_mouseCapture;
    private bool m_showCursor = true;
    private bool m_mouseOutsideWindow;
    private int m_manualCloseQueued;

    // Prevents drag-resize feedback from redundant SDL geometry changes.
    private MyWindowModeEnum? m_appliedWindowMode;

    // Windowed geometry restored after fullscreen transitions and at startup.
    private Vector2I? m_savedWindowedSize;
    private Vector2I? m_savedWindowedPosition;

    // Reject transient tiny DXGI/DXVK bounds without blocking normal resolutions.
    private const int MIN_VALID_WINDOW_WIDTH = 480;
    private const int MIN_VALID_WINDOW_HEIGHT = 360;

    private static bool IsValidWindowedPixelSize(int w, int h) =>
        w >= MIN_VALID_WINDOW_WIDTH && h >= MIN_VALID_WINDOW_HEIGHT;

    private bool IsValidWindowedSize(int w, int h)
    {
        Vector2I pixels = WindowToPixelSize(new Vector2I(w, h));
        return IsValidWindowedPixelSize(pixels.X, pixels.Y);
    }

    // Debounce geometry saves; the render thread schedules and the game thread saves.
    private readonly object m_configLock = new object();
    private Vector2I? m_pendingWindowedSizePixels;
    private Vector2I? m_pendingWindowedPosition;
    private long m_configSaveScheduledAtTicks;
    private const int CONFIG_SAVE_DEBOUNCE_MS = 500;

    private void ScheduleConfigSave()
    {
        Volatile.Write(
            ref m_configSaveScheduledAtTicks,
            DateTime.UtcNow.AddMilliseconds(CONFIG_SAVE_DEBOUNCE_MS).Ticks
        );
    }

    private void QueueWindowedConfig(Vector2I? sizePixels, Vector2I? position)
    {
        if (!sizePixels.HasValue && !position.HasValue)
            return;
        lock (m_configLock)
        {
            if (sizePixels.HasValue)
                m_pendingWindowedSizePixels = sizePixels;
            if (position.HasValue)
                m_pendingWindowedPosition = position;
        }
        ScheduleConfigSave();
    }

    private void FlushPendingConfigSave(bool force = false)
    {
        long ticks = Volatile.Read(ref m_configSaveScheduledAtTicks);
        if (ticks == 0 && !force)
            return;
        if (!force && DateTime.UtcNow.Ticks < ticks)
            return;
        Volatile.Write(ref m_configSaveScheduledAtTicks, 0);

        if (ApplyPendingWindowConfig() || ticks != 0)
            PluginWindowConfig.Save();
    }

    private bool ApplyPendingWindowConfig()
    {
        Vector2I? sizePixels;
        Vector2I? position;
        lock (m_configLock)
        {
            sizePixels = m_pendingWindowedSizePixels;
            position = m_pendingWindowedPosition;
            m_pendingWindowedSizePixels = null;
            m_pendingWindowedPosition = null;
        }
        if (!sizePixels.HasValue && !position.HasValue)
            return false;
        if (sizePixels.HasValue)
            PluginWindowConfig.SetWindowedSize(sizePixels.Value.X, sizePixels.Value.Y);
        if (position.HasValue)
            PluginWindowConfig.SetWindowedPosition(position.Value.X, position.Value.Y);
        return true;
    }

    internal IntPtr Handle { get; private set; }

    public bool DrawEnabled => m_isVisible;
    public bool IsWindowed => m_appliedWindowMode == MyWindowModeEnum.Window;
    public bool IsActive => m_isActive;
    public Vector2I ClientSize => m_clientSize;

    // Physical drawable size used for the DXVK backbuffer on HiDPI displays.
    internal Vector2I ClientSizePixels => m_clientSizePixels;

    internal static Vector2I PixelsToPrimaryWindowSize(int width, int height)
    {
        return SdlRenderThread.Invoke(() =>
        {
            uint displayId = SDL_GetPrimaryDisplay();
            IntPtr modePtr = displayId == 0 ? IntPtr.Zero : SDL_GetDesktopDisplayMode(displayId);
            float density =
                modePtr == IntPtr.Zero
                    ? 1f
                    : Marshal.PtrToStructure<SdlDisplayMode>(modePtr).PixelDensity;
            return PixelsToWindowSize(width, height, density);
        });
    }

    // SDL thread only.
    private void RefreshWindowMetrics()
    {
        if (Handle == IntPtr.Zero)
            return;
        Vector2I logical = m_clientSize;
        Vector2I pixels;
        if (SDL_GetWindowSize(Handle, out int lw, out int lh) && lw > 0 && lh > 0)
            logical = new Vector2I(lw, lh);
        if (SDL_GetWindowSizeInPixels(Handle, out int w, out int h) && w > 0 && h > 0)
            pixels = new Vector2I(w, h);
        else
            pixels = logical;
        lock (m_bufferLock)
        {
            m_clientSize = logical;
            m_clientSizePixels = pixels;
        }
    }

    private Vector2I PixelsToWindowSize(int width, int height)
    {
        float density = SDL_GetWindowPixelDensity(Handle);
        return PixelsToWindowSize(width, height, density);
    }

    private static Vector2I PixelsToWindowSize(int width, int height, float density)
    {
        if (density <= 0f || !float.IsFinite(density))
            density = 1f;
        return new Vector2I(
            Math.Max(1, (int)MathF.Round(width / density)),
            Math.Max(1, (int)MathF.Round(height / density))
        );
    }

    private Vector2I WindowToPixelSize(Vector2I size)
    {
        float density = SDL_GetWindowPixelDensity(Handle);
        if (density <= 0f || !float.IsFinite(density))
            density = 1f;
        return new Vector2I(
            Math.Max(1, (int)MathF.Round(size.X * density)),
            Math.Max(1, (int)MathF.Round(size.Y * density))
        );
    }

    public bool MouseCapture
    {
        get => m_mouseCapture;
        set
        {
            m_mouseCapture = value;
            DispatchUpdateMouseMode();
        }
    }

    public bool ShowCursor
    {
        get => m_showCursor;
        set
        {
            m_showCursor = value;
            DispatchUpdateMouseMode();
        }
    }

    public int KeyboardDelay => 0;
    public int KeyboardSpeed => 31;

    public Vector2 MousePosition
    {
        get
        {
            lock (m_bufferLock)
            {
                if (m_mouseOutsideWindow)
                    return m_mousePosition;
                return m_mousePosition.IsValid()
                    ? m_mousePosition
                    : new Vector2(m_clientSize.X * 0.5f, m_clientSize.Y * 0.5f);
            }
        }
        set
        {
            bool shouldWarp;
            lock (m_bufferLock)
            {
                shouldWarp =
                    Math.Abs(m_mousePosition.X - value.X) > 0.5f
                    || Math.Abs(m_mousePosition.Y - value.Y) > 0.5f;
                m_mouseOutsideWindow = false;
                m_mousePosition = value;
            }
            if (shouldWarp && Handle != IntPtr.Zero)
            {
                float wx = value.X,
                    wy = value.Y;
                SdlRenderThread.Dispatch(() =>
                {
                    if (Handle != IntPtr.Zero)
                        SDL_WarpMouseInWindow(Handle, wx, wy);
                });
            }
        }
    }

    /// <summary>
    /// Reads an atomic in-window mouse snapshot for render-thread cursor placement.
    /// </summary>
    internal bool TryGetFreshInWindowMousePosition(out Vector2 position)
    {
        lock (m_bufferLock)
        {
            if (m_mouseOutsideWindow || !m_mousePosition.IsValid())
            {
                position = default;
                return false;
            }
            position = m_mousePosition;
            return true;
        }
    }

    public Vector2 MouseAreaSize => new Vector2(m_clientSize.X, m_clientSize.Y);

    public event Action OnExit;
    public event Action OnManualWindowCloseRequest;

    /// <summary>
    /// Constructs the native window on the SDL thread.
    /// </summary>
    internal static SdlGameWindow Create(
        string gameName,
        int width,
        int height,
        int? initialX = null,
        int? initialY = null
    )
    {
        return SdlRenderThread.Invoke(() =>
            new SdlGameWindow(gameName, width, height, initialX, initialY)
        );
    }

    private SdlGameWindow(string gameName, int width, int height, int? initialX, int? initialY)
    {
        // Native window creation must stay on the SDL thread.
        if (!SdlRenderThread.IsCurrent)
            throw new InvalidOperationException(
                "SdlGameWindow constructor must run on the SDL render thread; use SdlGameWindow.Create."
            );

        if (!SdlRenderThread.IsInitialized)
            throw new PlatformNotSupportedException("SDL3 video initialization failed.");

        if (width > 0 && height > 0)
            m_clientSize = new Vector2I(width, height);

        Handle = SDL_CreateWindow(
            gameName ?? "SpaceEngineers",
            m_clientSize.X,
            m_clientSize.Y,
            SDL_WINDOW_HIDDEN
                | SDL_WINDOW_RESIZABLE
                | SDL_WINDOW_HIGH_PIXEL_DENSITY
                | SDL_WINDOW_VULKAN
        );

        if (Handle == IntPtr.Zero)
            throw new PlatformNotSupportedException("SDL3 window creation failed.");
        m_windowId = SDL_GetWindowID(Handle);

        // Set _NET_WM_ICON before the window is mapped.
        SdlIconHelper.Apply(Handle, ResolveGameIcon());

        // Apply saved geometry before the first map to avoid a visible jump.
        if (!SdlRenderThread.IsWayland && initialX.HasValue && initialY.HasValue)
        {
            SDL_SetWindowPosition(Handle, initialX.Value, initialY.Value);
            m_savedWindowedPosition = new Vector2I(initialX.Value, initialY.Value);
        }
        SDL_StartTextInput(Handle);
        UpdateMouseModeOnRenderThread();

        // Wayland must configure the toplevel before DXVK attaches its first buffer.
        if (SdlRenderThread.IsWayland)
        {
            SDL_ShowWindow(Handle);
            SDL_SyncWindow(Handle);

            // The compositor assigns the output scale when the surface is mapped.
            // Reapply persisted pixels using that output's actual density.
            if (
                Sandbox.MySandboxGame.Config?.WindowMode == MyWindowModeEnum.Window
                && PluginWindowConfig.TryGetWindowedSize(out int savedW, out int savedH)
            )
            {
                Vector2I restoredSize = PixelsToWindowSize(savedW, savedH);
                SDL_SetWindowSize(Handle, restoredSize.X, restoredSize.Y);
                SDL_SyncWindow(Handle);
            }

            // Preserve SDL_WINDOW_HIDDEN until the game or Pulsar calls ShowAndFocus.
            SDL_HideWindow(Handle);
            SDL_SyncWindow(Handle);
        }
        RefreshWindowMetrics();
        if (IsValidWindowedSize(m_clientSize.X, m_clientSize.Y))
            m_savedWindowedSize = m_clientSize;

        // Receive events and one mouse snapshot per SDL loop.
        SdlRenderThread.EventHandler += HandleEvent;
        SdlRenderThread.MouseSnapshotCallback = UpdateMouseSnapshot;

        Console.WriteLine(
            $"[LinuxCompat] SDL3 window created: logical={m_clientSize.X}x{m_clientSize.Y} "
                + $"pixels={m_clientSizePixels.X}x{m_clientSizePixels.Y} "
                + $"initialPos={(initialX.HasValue && initialY.HasValue ? $"({initialX},{initialY})" : "(default)")}"
        );
    }

    // Prefer the configured icon, then <ApplicationName>.ico.
    private static string ResolveGameIcon()
    {
        string gameIcon = Sandbox.Game.MyPerGameSettings.GameIcon;
        if (!string.IsNullOrEmpty(gameIcon))
            return gameIcon;

        string appName = Sandbox.Game.MyPerGameSettings.BasicGameInfo.ApplicationName;
        if (!string.IsNullOrEmpty(appName))
            return appName + ".ico";

        return null;
    }

    public void OnModeChanged(MyWindowModeEnum mode, int width, int height, Rectangle desktopBounds)
    {
        if (Handle == IntPtr.Zero)
            return;

        BackbufferResizeRequest.BeginModeChange();
        // Serialize mode changes with other SDL window operations.
        SdlRenderThread.Dispatch(() =>
        {
            try
            {
                if (Handle == IntPtr.Zero)
                    return;
                ApplyModeChange(mode, width, height, desktopBounds);
            }
            finally
            {
                BackbufferResizeRequest.CompleteModeChange();
            }
        });
    }

    private void ApplyModeChange(
        MyWindowModeEnum mode,
        int width,
        int height,
        Rectangle desktopBounds
    )
    {
        Rectangle displayBounds = GetWindowDisplayBounds();
        if (displayBounds.Width <= 0 || displayBounds.Height <= 0)
            displayBounds = desktopBounds;

        bool initialMode = !m_appliedWindowMode.HasValue;
        bool modeChanged = initialMode || m_appliedWindowMode.Value != mode;

        // Load saved windowed geometry before the first mode transition.
        if (!m_appliedWindowMode.HasValue && !m_savedWindowedSize.HasValue)
            LoadSavedWindowedState();

        if (
            modeChanged
            && m_appliedWindowMode == MyWindowModeEnum.Window
            && mode != MyWindowModeEnum.Window
        )
        {
            CaptureCurrentWindowedState();
        }

        switch (mode)
        {
            case MyWindowModeEnum.Window:
                SDL_SetWindowFullscreenMode(Handle, IntPtr.Zero);
                SDL_SetWindowFullscreen(Handle, false);
                SDL_SetWindowAlwaysOnTop(Handle, false);
                SDL_SetWindowBordered(Handle, true);
                ApplyWindowedMode(width, height, displayBounds, modeChanged, initialMode);
                break;

            case MyWindowModeEnum.FullscreenWindow:
                // Wayland compositors, including XWayland, own borderless placement.
                if (UsesWaylandCompositor())
                {
                    SDL_SetWindowFullscreenMode(Handle, IntPtr.Zero);
                    SDL_SetWindowFullscreen(Handle, true);
                    break;
                }
                SDL_SetWindowFullscreenMode(Handle, IntPtr.Zero);
                SDL_SetWindowFullscreen(Handle, false);
                SDL_SetWindowAlwaysOnTop(Handle, true);
                SDL_SetWindowBordered(Handle, false);
                SDL_SetWindowPosition(Handle, displayBounds.X, displayBounds.Y);
                SDL_SetWindowSize(Handle, displayBounds.Width, displayBounds.Height);
                break;

            case MyWindowModeEnum.Fullscreen:
                ApplyFullscreenMode(width, height);
                break;
        }

        m_appliedWindowMode = mode;
        SDL_SyncWindow(Handle);
        RefreshWindowMetrics();
    }

    private void ApplyWindowedMode(
        int desiredWidth,
        int desiredHeight,
        Rectangle displayBounds,
        bool modeChanged,
        bool initialMode
    )
    {
        Vector2I desiredSize = PixelsToWindowSize(desiredWidth, desiredHeight);

        // DXVK can report tiny swapchain bounds instead of desktop bounds.
        bool boundsOk = IsPlausibleDisplayBounds(displayBounds);

        int targetW = desiredSize.X;
        int targetH = desiredSize.Y;
        if (boundsOk)
        {
            targetW = Math.Min(desiredSize.X, displayBounds.Width);
            targetH = Math.Min(desiredSize.Y, displayBounds.Height);
        }
        if (targetW <= 0)
            targetW = desiredSize.X;
        if (targetH <= 0)
            targetH = desiredSize.Y;

        if (modeChanged)
        {
            // Startup restores manual geometry; later mode changes honor the
            // resolution selected in the display settings. Offscreen rendering
            // has no window to restore, so the requested size always wins.
            bool restoreSaved = initialMode && !SdlRenderThread.IsOffscreen;
            int w = restoreSaved ? m_savedWindowedSize?.X ?? targetW : targetW;
            int h = restoreSaved ? m_savedWindowedSize?.Y ?? targetH : targetH;
            if (boundsOk)
            {
                w = Math.Min(w, displayBounds.Width);
                h = Math.Min(h, displayBounds.Height);
            }

            int x = 0,
                y = 0;
            if (!SdlRenderThread.IsWayland && m_savedWindowedPosition.HasValue)
            {
                x = m_savedWindowedPosition.Value.X;
                y = m_savedWindowedPosition.Value.Y;
            }
            else if (!SdlRenderThread.IsWayland && boundsOk)
            {
                x = displayBounds.X + (displayBounds.Width - w) / 2;
                y = displayBounds.Y + (displayBounds.Height - h) / 2;
            }
            else if (!SdlRenderThread.IsWayland)
            {
                // Keep SDL's default position when no trusted bounds exist.
                SDL_GetWindowPosition(Handle, out x, out y);
            }

            if (!SdlRenderThread.IsWayland && boundsOk)
                ClampWindowToDisplay(displayBounds, ref x, ref y, ref w, ref h);

            Console.WriteLine(
                $"[LinuxCompat] ApplyWindowedMode (transition): requested={desiredWidth}x{desiredHeight} "
                    + $"applied={w}x{h} at ({x},{y}) displayBounds={displayBounds.Width}x{displayBounds.Height}"
                    + $" trusted={boundsOk} savedSize={m_savedWindowedSize} savedPos={m_savedWindowedPosition}"
            );

            SDL_SetWindowSize(Handle, w, h);
            if (!SdlRenderThread.IsWayland)
                SDL_SetWindowPosition(Handle, x, y);
            m_savedWindowedSize = new Vector2I(w, h);
            if (!SdlRenderThread.IsWayland)
                m_savedWindowedPosition = new Vector2I(x, y);
            PersistSavedWindowedState();
        }
        else if (targetW != m_clientSize.X || targetH != m_clientSize.Y)
        {
            // Preserve position for windowed resolution changes unless it no longer fits.
            int curX = 0,
                curY = 0;
            bool havePos =
                !SdlRenderThread.IsWayland && SDL_GetWindowPosition(Handle, out curX, out curY);
            int x = havePos
                ? curX
                : (boundsOk ? displayBounds.X + (displayBounds.Width - targetW) / 2 : 0);
            int y = havePos
                ? curY
                : (boundsOk ? displayBounds.Y + (displayBounds.Height - targetH) / 2 : 0);

            int w = targetW;
            int h = targetH;
            if (!SdlRenderThread.IsWayland && boundsOk)
                ClampWindowToDisplay(displayBounds, ref x, ref y, ref w, ref h);

            Console.WriteLine(
                $"[LinuxCompat] ApplyWindowedMode (resize): requested={desiredWidth}x{desiredHeight} "
                    + $"applied={w}x{h} at ({x},{y}) displayBounds={displayBounds.Width}x{displayBounds.Height}"
                    + $" trusted={boundsOk}"
            );

            SDL_SetWindowSize(Handle, w, h);
            if (!SdlRenderThread.IsWayland && (!havePos || x != curX || y != curY))
                SDL_SetWindowPosition(Handle, x, y);
            m_savedWindowedSize = new Vector2I(w, h);
            if (!SdlRenderThread.IsWayland)
                m_savedWindowedPosition = new Vector2I(x, y);
            PersistSavedWindowedState();
        }
        // Matching geometry came from the WM and must not be applied back to it.
    }

    private static void ClampWindowToDisplay(
        Rectangle displayBounds,
        ref int x,
        ref int y,
        ref int w,
        ref int h
    )
    {
        if (displayBounds.Width <= 0 || displayBounds.Height <= 0)
            return;
        if (w > displayBounds.Width)
            w = displayBounds.Width;
        if (h > displayBounds.Height)
            h = displayBounds.Height;
        if (x < displayBounds.X)
            x = displayBounds.X;
        if (y < displayBounds.Y)
            y = displayBounds.Y;
        if (x + w > displayBounds.X + displayBounds.Width)
            x = displayBounds.X + displayBounds.Width - w;
        if (y + h > displayBounds.Y + displayBounds.Height)
            y = displayBounds.Y + displayBounds.Height - h;
    }

    private void CaptureCurrentWindowedState()
    {
        if (Handle == IntPtr.Zero)
            return;
        if (SDL_GetWindowSize(Handle, out int w, out int h) && IsValidWindowedSize(w, h))
            m_savedWindowedSize = new Vector2I(w, h);
        if (!SdlRenderThread.IsWayland && SDL_GetWindowPosition(Handle, out int x, out int y))
            m_savedWindowedPosition = new Vector2I(x, y);
        PersistSavedWindowedState();
    }

    private void PersistSavedWindowedState()
    {
        // Offscreen geometry is not the user's window geometry.
        if (SdlRenderThread.IsOffscreen)
            return;

        Vector2I? pixels = null;
        if (
            m_savedWindowedSize.HasValue
            && IsValidWindowedSize(m_savedWindowedSize.Value.X, m_savedWindowedSize.Value.Y)
        )
            pixels = WindowToPixelSize(m_savedWindowedSize.Value);
        QueueWindowedConfig(pixels, SdlRenderThread.IsWayland ? null : m_savedWindowedPosition);
    }

    private void LoadSavedWindowedState()
    {
        if (
            PluginWindowConfig.TryGetWindowedSize(out int w, out int h)
            && IsValidWindowedPixelSize(w, h)
        )
            m_savedWindowedSize = PixelsToWindowSize(w, h);
        if (
            !SdlRenderThread.IsWayland
            && PluginWindowConfig.TryGetWindowedPosition(out int x, out int y)
        )
            m_savedWindowedPosition = new Vector2I(x, y);
    }

    private unsafe void ApplyFullscreenMode(int width, int height)
    {
        // SDL requires a populated display mode before exclusive fullscreen.
        // Include high-density modes to preserve X11 output scaling.
        uint displayId = SDL_GetDisplayForWindow(Handle);
        if (
            displayId != 0
            && SDL_GetClosestFullscreenDisplayMode(
                displayId,
                width,
                height,
                0f,
                true,
                out SdlDisplayMode mode
            )
        )
        {
            Console.WriteLine(
                $"[LinuxCompat] Fullscreen mode: requested {width}x{height}, "
                    + $"picked {mode.W}x{mode.H} @ {mode.RefreshRate:F2}Hz, pixel_density={mode.PixelDensity:F2}"
            );
            SdlDisplayMode* modePtr = &mode;
            SDL_SetWindowFullscreenMode(Handle, (IntPtr)modePtr);
        }
        SDL_SetWindowFullscreen(Handle, true);
    }

    private Rectangle GetWindowDisplayBounds()
    {
        if (Handle != IntPtr.Zero)
        {
            uint displayId = SDL_GetDisplayForWindow(Handle);
            if (
                displayId != 0
                && SDL_GetDisplayBounds(displayId, out SdlRect rect)
                && rect.W > 0
                && rect.H > 0
            )
                return new Rectangle(rect.X, rect.Y, rect.W, rect.H);
        }

        // Hidden or unpositioned windows may not have an assigned display.
        uint primary = SDL_GetPrimaryDisplay();
        if (
            primary != 0
            && SDL_GetDisplayBounds(primary, out SdlRect prect)
            && prect.W > 0
            && prect.H > 0
        )
            return new Rectangle(prect.X, prect.Y, prect.W, prect.H);

        return default;
    }

    // Reject DXGI swapchain bounds masquerading as desktop geometry. The SDL
    // offscreen driver (headless) reports a fake 1024x768 desktop that would
    // clamp any larger requested resolution, so it is never trusted either.
    private static bool IsPlausibleDisplayBounds(Rectangle r) =>
        !SdlRenderThread.IsOffscreen && r.Width >= 640 && r.Height >= 480;

    private static bool UsesWaylandCompositor()
    {
        string sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        return SdlRenderThread.IsWayland
            || string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase);
    }

    public void AddChar(char ch)
    {
        lock (m_bufferLock)
        {
            m_bufferedChars.Add(ch);
        }
    }

    public void GetBufferedTextInput(ref List<char> swappedBuffer)
    {
        swappedBuffer.Clear();
        lock (m_bufferLock)
        {
            var temp = swappedBuffer;
            swappedBuffer = m_bufferedChars;
            m_bufferedChars = temp;
        }
    }

    public void AddMessageHandler(uint wm, ActionRef<MyMessage> action)
    {
        if (m_messageHandlers.ContainsKey(wm))
            m_messageHandlers[wm] =
                (ActionRef<MyMessage>)Delegate.Combine(m_messageHandlers[wm], action);
        else
            m_messageHandlers.Add(wm, action);
    }

    public void RemoveMessageHandler(uint wm, ActionRef<MyMessage> action)
    {
        if (m_messageHandlers.ContainsKey(wm))
            m_messageHandlers[wm] =
                (ActionRef<MyMessage>)Delegate.Remove(m_messageHandlers[wm], action);
    }

    public void CloseManually()
    {
        Exit();
    }

    private void HandleManualWindowCloseRequest()
    {
        if (Interlocked.Exchange(ref m_manualCloseQueued, 1) != 0)
            return;

        var game = Sandbox.MySandboxGame.Static;
        if (game == null)
        {
            Volatile.Write(ref m_manualCloseQueued, 0);
            Hide();
            CloseManually();
            return;
        }

        game.Invoke(
            () =>
            {
                try
                {
                    if (OnManualWindowCloseRequest != null && m_isVisible)
                    {
                        OnManualWindowCloseRequest();
                        return;
                    }

                    Hide();
                    CloseManually();
                }
                finally
                {
                    Volatile.Write(ref m_manualCloseQueued, 0);
                }
            },
            "LinuxCompat window close"
        );
    }

    /// <summary>
    /// Event polling is owned by <see cref="SdlRenderThread"/>.
    /// </summary>
    public void DoEvents() { }

    public void Exit()
    {
        m_isVisible = false;
        m_isActive = false;
        FlushPendingConfigSave(force: true);
        SdlRenderThread.Invoke(() =>
        {
            SdlRenderThread.EventHandler -= HandleEvent;
            SdlRenderThread.MouseSnapshotCallback = null;
            DestroyNativeWindow();
        });
        OnExit.InvokeIfNotNull();
    }

    public bool UpdateRenderThread()
    {
        return m_isVisible;
    }

    public void UpdateMainThread()
    {
        FlushPendingConfigSave();
    }

    public void SetCursor(Stream stream) { }

    public void ShowAndFocus()
    {
        m_isVisible = true;
        m_isActive = true;

        // FIFO dispatch applies the requested mode before showing the window.
        SdlRenderThread.Invoke(() =>
        {
            if (Handle != IntPtr.Zero)
                SDL_ShowWindow(Handle);
        });
    }

    public void Hide()
    {
        m_isVisible = false;
        m_isActive = false;
        SdlRenderThread.Dispatch(() =>
        {
            if (Handle != IntPtr.Zero)
                SDL_HideWindow(Handle);
        });
    }

    internal unsafe void CopyAsyncKeyStates(byte* data)
    {
        for (int i = 0; i < m_keyStates.Length; i++)
            data[i] = m_keyStates[i];
    }

    uint[] IVRageInput2.DeveloperKeys => new uint[4];
    bool IVRageInput2.IsCorrectlyInitialized => true;

    void IVRageInput2.GetMouseState(out MyMouseState state)
    {
        GetMouseInputState(out var inputState);
        state = new MyMouseState
        {
            X = inputState.X,
            Y = inputState.Y,
            ScrollWheelValue = inputState.ScrollWheelValue,
            LeftButton = inputState.LeftButton,
            MiddleButton = inputState.MiddleButton,
            RightButton = inputState.RightButton,
            XButton1 = inputState.XButton1,
            XButton2 = inputState.XButton2,
        };
    }

    List<string> IVRageInput2.EnumerateJoystickNames() => SdlJoystick.EnumerateJoystickNames();

    string IVRageInput2.InitializeJoystickIfPossible(string joystickInstanceName) =>
        SdlJoystick.InitializeJoystickIfPossible(joystickInstanceName);

    bool IVRageInput2.IsJoystickAxisSupported(MyJoystickAxesEnum axis) =>
        SdlJoystick.IsJoystickAxisSupported(axis);

    bool IVRageInput2.IsJoystickConnected() => SdlJoystick.IsJoystickConnected();

    void IVRageInput2.GetJoystickState(ref MyJoystickState state) =>
        SdlJoystick.GetJoystickState(ref state);

    void IVRageInput2.ShowVirtualKeyboardIfNeeded(
        Action<string> onSuccess,
        Action onCancel,
        string defaultText,
        string title,
        int maxLength
    ) { }

    unsafe void IVRageInput2.GetAsyncKeyStates(byte* data) => CopyAsyncKeyStates(data);

    void IDisposable.Dispose() { }

    /// <summary>Returns the latest render-thread input snapshot.</summary>
    internal void GetMouseInputState(out MyMouseInputState state)
    {
        if (Handle == IntPtr.Zero)
        {
            state = default;
            return;
        }

        uint buttonState;
        float relX,
            relY;
        int scrollWheel;
        lock (m_bufferLock)
        {
            buttonState = m_mouseButtonState;
            relX = m_relativeDeltaXAccum;
            relY = m_relativeDeltaYAccum;
            m_relativeDeltaXAccum = 0;
            m_relativeDeltaYAccum = 0;
            scrollWheel = m_mouseWheel;
            m_mouseWheel = 0;
        }

        state = new MyMouseInputState
        {
            X = (int)MathF.Round(relX),
            Y = (int)MathF.Round(relY),
            ScrollWheelValue = scrollWheel,
            LeftButton = (buttonState & SDL_BUTTON_LMASK) != 0,
            MiddleButton = (buttonState & SDL_BUTTON_MMASK) != 0,
            RightButton = (buttonState & SDL_BUTTON_RMASK) != 0,
            XButton1 = (buttonState & SDL_BUTTON_X1MASK) != 0,
            XButton2 = (buttonState & SDL_BUTTON_X2MASK) != 0,
        };
    }

    /// <summary>
    /// Refreshes window and mouse snapshots after each SDL event-pump pass.
    /// </summary>
    private void UpdateMouseSnapshot()
    {
        if (Handle == IntPtr.Zero)
            return;

        SDL_GetRelativeMouseState(out var relX, out var relY);
        if (SDL_GetMouseFocus() != Handle)
        {
            lock (m_bufferLock)
            {
                m_mouseButtonState = 0;
                m_relativeDeltaXAccum = 0;
                m_relativeDeltaYAccum = 0;
            }
            SetKeyState(MyKeys.LeftButton, false);
            SetKeyState(MyKeys.RightButton, false);
            SetKeyState(MyKeys.MiddleButton, false);
            SetKeyState(MyKeys.ExtraButton1, false);
            SetKeyState(MyKeys.ExtraButton2, false);
            return;
        }

        uint buttonState = SDL_GetMouseState(out var mouseX, out var mouseY);

        lock (m_bufferLock)
        {
            m_mouseButtonState = buttonState;
            // SDL_GetMouseState retains the last coordinate after mouse leave.
            if (m_showCursor && m_mouseOutsideWindow)
                m_mousePosition = -Vector2.One;
            else
            {
                m_mouseOutsideWindow = false;
                m_mousePosition = new Vector2(mouseX, mouseY);
            }
            m_relativeDeltaXAccum += relX;
            m_relativeDeltaYAccum += relY;
        }

        // Match Windows GetAsyncKeyState by exposing mouse buttons as MyKeys.
        SetKeyState(MyKeys.LeftButton, (buttonState & SDL_BUTTON_LMASK) != 0);
        SetKeyState(MyKeys.RightButton, (buttonState & SDL_BUTTON_RMASK) != 0);
        SetKeyState(MyKeys.MiddleButton, (buttonState & SDL_BUTTON_MMASK) != 0);
        SetKeyState(MyKeys.ExtraButton1, (buttonState & SDL_BUTTON_X1MASK) != 0);
        SetKeyState(MyKeys.ExtraButton2, (buttonState & SDL_BUTTON_X2MASK) != 0);
    }

    private void DestroyNativeWindow()
    {
        if (Handle != IntPtr.Zero)
        {
            SDL_DestroyWindow(Handle);
            Handle = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Handles SDL events on the SDL render thread.
    /// </summary>
    private void HandleEvent(ref SdlRenderThread.SdlEvent sdlEvent)
    {
        if (sdlEvent.Type != SDL_EVENT_QUIT && sdlEvent.Window.WindowId != m_windowId)
            return;

        switch (sdlEvent.Type)
        {
            case SDL_EVENT_QUIT:
            case SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                HandleManualWindowCloseRequest();
                break;
            case SDL_EVENT_WINDOW_FOCUS_GAINED:
                m_isActive = true;
                RecenterCursorIfOutsideWindow();
                break;
            case SDL_EVENT_WINDOW_FOCUS_LOST:
                m_isActive = false;
                break;
            case SDL_EVENT_WINDOW_MOUSE_ENTER:
                lock (m_bufferLock)
                {
                    m_mouseOutsideWindow = false;
                }
                break;
            case SDL_EVENT_WINDOW_MOUSE_LEAVE:
                lock (m_bufferLock)
                {
                    m_mouseOutsideWindow = true;
                    m_mousePosition = -Vector2.One;
                }
                break;
            case SDL_EVENT_WINDOW_RESIZED:
                RefreshWindowMetrics();
                BackbufferResizeRequest.Request();
                PersistCurrentWindowedSize();
                break;
            case SDL_EVENT_WINDOW_MOVED:
                if (!SdlRenderThread.IsWayland && m_appliedWindowMode == MyWindowModeEnum.Window)
                {
                    int px = sdlEvent.Window.Data1;
                    int py = sdlEvent.Window.Data2;
                    m_savedWindowedPosition = new Vector2I(px, py);
                    QueueWindowedConfig(null, m_savedWindowedPosition);
                }
                break;
            case SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
                RefreshWindowMetrics();
                BackbufferResizeRequest.Request();
                PersistCurrentWindowedSize();
                break;
            case SDL_EVENT_KEY_DOWN:
            case SDL_EVENT_KEY_UP:
                var key = MapKeycode(sdlEvent.Keyboard.Key);
                SetKeyState(key, sdlEvent.Type == SDL_EVENT_KEY_DOWN);
                ApplyModifierAliases();
                // SDL3's SDL_EVENT_TEXT_INPUT only delivers printable characters,
                // unlike Windows WM_CHAR which also delivers control chars. SE's
                // text controls depend on that buffer for editing control chars.
                if (sdlEvent.Type == SDL_EVENT_KEY_DOWN)
                {
                    if (sdlEvent.Keyboard.Key == 8u)
                        AddChar('\b');
                    else if (key == MyKeys.Enter)
                        AddChar('\r');
                }
                break;
            case SDL_EVENT_TEXT_INPUT:
                if (sdlEvent.Text.Text != IntPtr.Zero)
                {
                    string text = Marshal.PtrToStringUTF8(sdlEvent.Text.Text);
                    if (!string.IsNullOrEmpty(text))
                    {
                        foreach (char ch in text)
                            AddChar(ch);
                    }
                }
                break;
            case SDL_EVENT_MOUSE_WHEEL:
                lock (m_bufferLock)
                {
                    int delta = sdlEvent.Wheel.IntegerY;
                    if (delta == 0)
                        delta = Math.Sign(sdlEvent.Wheel.Y);
                    m_mouseWheel += delta * 120;
                }
                break;
        }
    }

    private void PersistCurrentWindowedSize()
    {
        if (
            m_appliedWindowMode != MyWindowModeEnum.Window
            || !IsValidWindowedSize(m_clientSize.X, m_clientSize.Y)
        )
            return;

        m_savedWindowedSize = m_clientSize;
        QueueWindowedConfig(m_clientSizePixels, null);
    }

    // SDL may omit MOUSE_ENTER after focus returns. Wayland cannot query global state.
    private void RecenterCursorIfOutsideWindow()
    {
        if (Handle == IntPtr.Zero)
            return;
        if (!m_showCursor)
            return;
        lock (m_bufferLock)
        {
            if (!m_mouseOutsideWindow)
                return;
        }
        if (!SDL_GetWindowSize(Handle, out int w, out int h) || w <= 0 || h <= 0)
            return;
        if (!SdlRenderThread.IsWayland && SDL_GetWindowPosition(Handle, out int wx, out int wy))
        {
            SDL_GetGlobalMouseState(out float gx, out float gy);
            if (gx >= wx && gy >= wy && gx < wx + w && gy < wy + h)
            {
                lock (m_bufferLock)
                {
                    m_mouseOutsideWindow = false;
                    m_mousePosition = new Vector2(gx - wx, gy - wy);
                }
                return;
            }
        }
        float cx = w * 0.5f;
        float cy = h * 0.5f;
        SDL_WarpMouseInWindow(Handle, cx, cy);
        lock (m_bufferLock)
        {
            m_mouseOutsideWindow = false;
            m_mousePosition = new Vector2(cx, cy);
        }
    }

    private void DispatchUpdateMouseMode()
    {
        SdlRenderThread.Dispatch(UpdateMouseModeOnRenderThread);
    }

    private void UpdateMouseModeOnRenderThread()
    {
        if (Handle == IntPtr.Zero)
            return;

        // Use relative mode whenever the game hides its software cursor.
        SDL_SetWindowRelativeMouseMode(Handle, !m_showCursor);

        // Keep the hardware cursor hidden because it renders ahead of the software cursor.
        SDL_HideCursor();
    }

    private void SetKeyState(MyKeys key, bool value)
    {
        if (key == MyKeys.None)
            return;

        int index = (byte)key;
        int byteIndex = index / 8;
        byte mask = (byte)(1 << index % 8);
        if (value)
            m_keyStates[byteIndex] |= mask;
        else
            m_keyStates[byteIndex] &= (byte)(~mask);
    }

    private bool GetKeyState(MyKeys key)
    {
        int index = (byte)key;
        return (m_keyStates[index / 8] & (1 << index % 8)) != 0;
    }

    private void ApplyModifierAliases()
    {
        SetKeyState(MyKeys.Shift, GetKeyState(MyKeys.LeftShift) || GetKeyState(MyKeys.RightShift));
        SetKeyState(
            MyKeys.Control,
            GetKeyState(MyKeys.LeftControl) || GetKeyState(MyKeys.RightControl)
        );
        SetKeyState(MyKeys.Alt, GetKeyState(MyKeys.LeftAlt) || GetKeyState(MyKeys.RightAlt));
    }

    private static MyKeys MapKeycode(uint keycode)
    {
        if (keycode >= 'a' && keycode <= 'z')
            return (MyKeys)(keycode - 32);
        if (keycode >= '0' && keycode <= '9')
            return (MyKeys)keycode;

        return keycode switch
        {
            13u => MyKeys.Enter,
            8u => MyKeys.Back,
            9u => MyKeys.Tab,
            27u => MyKeys.Escape,
            32u => MyKeys.Space,
            59u => MyKeys.OemSemicolon,
            61u => MyKeys.OemPlus,
            44u => MyKeys.OemComma,
            45u => MyKeys.OemMinus,
            46u => MyKeys.OemPeriod,
            47u => MyKeys.OemQuestion,
            96u => MyKeys.OemTilde,
            91u => MyKeys.OemOpenBrackets,
            92u => MyKeys.OemPipe,
            93u => MyKeys.OemCloseBrackets,
            39u => MyKeys.OemQuotes,
            127u => MyKeys.Delete,
            1073741881u => MyKeys.CapsLock,
            1073741882u => MyKeys.F1,
            1073741883u => MyKeys.F2,
            1073741884u => MyKeys.F3,
            1073741885u => MyKeys.F4,
            1073741886u => MyKeys.F5,
            1073741887u => MyKeys.F6,
            1073741888u => MyKeys.F7,
            1073741889u => MyKeys.F8,
            1073741890u => MyKeys.F9,
            1073741891u => MyKeys.F10,
            1073741892u => MyKeys.F11,
            1073741893u => MyKeys.F12,
            1073741894u => MyKeys.Snapshot,
            1073741895u => MyKeys.ScrollLock,
            1073741896u => MyKeys.Pause,
            1073741897u => MyKeys.Insert,
            1073741898u => MyKeys.Home,
            1073741899u => MyKeys.PageUp,
            1073741901u => MyKeys.End,
            1073741902u => MyKeys.PageDown,
            1073741903u => MyKeys.Right,
            1073741904u => MyKeys.Left,
            1073741905u => MyKeys.Down,
            1073741906u => MyKeys.Up,
            1073741907u => MyKeys.NumLock,
            1073741908u => MyKeys.Divide,
            1073741909u => MyKeys.Multiply,
            1073741910u => MyKeys.Subtract,
            1073741911u => MyKeys.Add,
            1073741912u => MyKeys.Enter,
            1073741913u => MyKeys.NumPad1,
            1073741914u => MyKeys.NumPad2,
            1073741915u => MyKeys.NumPad3,
            1073741916u => MyKeys.NumPad4,
            1073741917u => MyKeys.NumPad5,
            1073741918u => MyKeys.NumPad6,
            1073741919u => MyKeys.NumPad7,
            1073741920u => MyKeys.NumPad8,
            1073741921u => MyKeys.NumPad9,
            1073741922u => MyKeys.NumPad0,
            1073741923u => MyKeys.Decimal,
            1073741925u => MyKeys.Apps,
            1073742048u => MyKeys.LeftControl,
            1073742049u => MyKeys.LeftShift,
            1073742050u => MyKeys.LeftAlt,
            1073742051u => MyKeys.LeftWindows,
            1073742052u => MyKeys.RightControl,
            1073742053u => MyKeys.RightShift,
            1073742054u => MyKeys.RightAlt,
            1073742055u => MyKeys.RightWindows,
            _ => MyKeys.None,
        };
    }

    internal struct MyMouseInputState
    {
        public int X;
        public int Y;
        public int ScrollWheelValue;
        public bool LeftButton;
        public bool MiddleButton;
        public bool RightButton;
        public bool XButton1;
        public bool XButton2;
    }

    #region SDL3 P/Invoke structs

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlRect
    {
        internal int X;
        internal int Y;
        internal int W;
        internal int H;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlDisplayMode
    {
        internal uint DisplayId;
        internal uint Format;
        internal int W;
        internal int H;
        internal float PixelDensity;
        internal float RefreshRate;
        internal int RefreshRateNumerator;
        internal int RefreshRateDenominator;
        internal IntPtr Internal;
    }

    #endregion

    #region SDL3 P/Invoke

    [DllImport(Lib, EntryPoint = "SDL_CreateWindow", CharSet = CharSet.Ansi)]
    private static extern IntPtr SDL_CreateWindow(string title, int width, int height, ulong flags);

    [DllImport(Lib, EntryPoint = "SDL_DestroyWindow")]
    private static extern void SDL_DestroyWindow(IntPtr window);

    [DllImport(Lib, EntryPoint = "SDL_GetWindowID")]
    private static extern uint SDL_GetWindowID(IntPtr window);

    [DllImport(Lib, EntryPoint = "SDL_ShowWindow")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_ShowWindow(IntPtr window);

    [DllImport(Lib, EntryPoint = "SDL_SyncWindow")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SyncWindow(IntPtr window);

    [DllImport(Lib, EntryPoint = "SDL_HideWindow")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_HideWindow(IntPtr window);

    [DllImport(Lib, EntryPoint = "SDL_SetWindowSize")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowSize(IntPtr window, int width, int height);

    [DllImport(Lib, EntryPoint = "SDL_SetWindowPosition")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowPosition(IntPtr window, int x, int y);

    [DllImport(Lib, EntryPoint = "SDL_GetWindowPosition")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetWindowPosition(IntPtr window, out int x, out int y);

    [DllImport(Lib, EntryPoint = "SDL_SetWindowBordered")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowBordered(
        IntPtr window,
        [MarshalAs(UnmanagedType.I1)] bool bordered
    );

    [DllImport(Lib, EntryPoint = "SDL_SetWindowAlwaysOnTop")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowAlwaysOnTop(
        IntPtr window,
        [MarshalAs(UnmanagedType.I1)] bool onTop
    );

    [DllImport(Lib, EntryPoint = "SDL_SetWindowFullscreen")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowFullscreen(
        IntPtr window,
        [MarshalAs(UnmanagedType.I1)] bool enabled
    );

    [DllImport(Lib, EntryPoint = "SDL_SetWindowFullscreenMode")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowFullscreenMode(IntPtr window, IntPtr mode);

    [DllImport(Lib, EntryPoint = "SDL_GetClosestFullscreenDisplayMode")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetClosestFullscreenDisplayMode(
        uint displayId,
        int w,
        int h,
        float refreshRate,
        [MarshalAs(UnmanagedType.I1)] bool includeHighDensityModes,
        out SdlDisplayMode mode
    );

    [DllImport(Lib, EntryPoint = "SDL_GetDisplayForWindow")]
    private static extern uint SDL_GetDisplayForWindow(IntPtr window);

    [DllImport(Lib, EntryPoint = "SDL_GetPrimaryDisplay")]
    private static extern uint SDL_GetPrimaryDisplay();

    [DllImport(Lib, EntryPoint = "SDL_GetDesktopDisplayMode")]
    private static extern IntPtr SDL_GetDesktopDisplayMode(uint displayId);

    [DllImport(Lib, EntryPoint = "SDL_GetDisplayBounds")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetDisplayBounds(uint displayId, out SdlRect rect);

    [DllImport(Lib, EntryPoint = "SDL_GetWindowSize")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetWindowSize(IntPtr window, out int w, out int h);

    [DllImport(Lib, EntryPoint = "SDL_GetWindowSizeInPixels")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetWindowSizeInPixels(IntPtr window, out int w, out int h);

    [DllImport(Lib, EntryPoint = "SDL_GetWindowPixelDensity")]
    private static extern float SDL_GetWindowPixelDensity(IntPtr window);

    [DllImport(Lib, EntryPoint = "SDL_StartTextInput")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_StartTextInput(IntPtr window);

    [DllImport(Lib, EntryPoint = "SDL_GetMouseState")]
    private static extern uint SDL_GetMouseState(out float x, out float y);

    [DllImport(Lib, EntryPoint = "SDL_GetMouseFocus")]
    private static extern IntPtr SDL_GetMouseFocus();

    [DllImport(Lib, EntryPoint = "SDL_GetRelativeMouseState")]
    private static extern uint SDL_GetRelativeMouseState(out float x, out float y);

    [DllImport(Lib, EntryPoint = "SDL_GetGlobalMouseState")]
    private static extern uint SDL_GetGlobalMouseState(out float x, out float y);

    [DllImport(Lib, EntryPoint = "SDL_WarpMouseInWindow")]
    private static extern void SDL_WarpMouseInWindow(IntPtr window, float x, float y);

    [DllImport(Lib, EntryPoint = "SDL_SetWindowRelativeMouseMode")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowRelativeMouseMode(
        IntPtr window,
        [MarshalAs(UnmanagedType.I1)] bool enabled
    );

    [DllImport(Lib, EntryPoint = "SDL_HideCursor")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_HideCursor();

    #endregion
}

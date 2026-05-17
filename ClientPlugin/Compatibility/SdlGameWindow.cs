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
    // SDL3 window event codes (SDL_events.h): SHOWN=0x202, HIDDEN=0x203,
    // EXPOSED=0x204, MOVED=0x205, RESIZED=0x206, PIXEL_SIZE_CHANGED=0x207.
    // The MOUSE_ENTER/MOUSE_LEAVE/FOCUS_*/CLOSE_REQUESTED values below are correct.
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

    // Single lock guarding the input snapshot (mouse position, button state,
    // accumulated relative delta, scroll wheel, buffered text input). The
    // render thread's per-iteration MouseSnapshotCallback writes; readers on
    // any thread (main game loop, IVRageInput2 callers) read.
    private readonly object m_bufferLock = new object();
    private readonly Dictionary<uint, ActionRef<MyMessage>> m_messageHandlers = new Dictionary<uint, ActionRef<MyMessage>>();
    private List<char> m_bufferedChars = new List<char>();
    private readonly byte[] m_keyStates = new byte[32];

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

    // Tracks the last applied window mode so windowed-to-windowed updates
    // (drag-resize) skip redundant SetWindowSize/Position which would fight the
    // window manager.
    private MyWindowModeEnum? m_appliedWindowMode;

    // Last-known windowed (Window mode) size/position. Updated every time the
    // user drags the window or resizes it, and before switching to a
    // fullscreen mode. Restored when returning to Window mode on subsequent
    // mode changes and on startup (loaded from the game config).
    private Vector2I? m_savedWindowedSize;
    private Vector2I? m_savedWindowedPosition;

    // Minimum window size we consider a valid "windowed" state. Guards
    // against the WM or a buggy adapter (DXGI/DXVK reporting 58x128
    // swapchain bounds as a display) briefly driving the window to a few
    // pixels — we must neither persist that to config nor restore it as the
    // remembered windowed size on the next launch. Well below the smallest
    // conventional resolution (640x480) so any legitimate user choice passes.
    private const int MIN_VALID_WINDOW_WIDTH = 480;
    private const int MIN_VALID_WINDOW_HEIGHT = 360;

    private static bool IsValidWindowedSize(int w, int h) =>
        w >= MIN_VALID_WINDOW_WIDTH && h >= MIN_VALID_WINDOW_HEIGHT;

    // Debounced Config.Save(). MySandboxGame.OnExit does not save the config,
    // so move-only changes would never reach disk unless we save ourselves.
    // A WM drag emits many WINDOW_MOVED events per second — writing the XML
    // config file on each one would hammer the disk, so we coalesce by
    // scheduling a save a short time after the last geometry change. The
    // schedule timestamp is read by main-thread UpdateMainThread and written
    // by the render thread's HandleEvent — kept atomic via Volatile.Read /
    // Volatile.Write of a long.
    private long m_configSaveScheduledAtTicks;
    private const int CONFIG_SAVE_DEBOUNCE_MS = 500;

    private void ScheduleConfigSave()
    {
        Volatile.Write(ref m_configSaveScheduledAtTicks,
            DateTime.UtcNow.AddMilliseconds(CONFIG_SAVE_DEBOUNCE_MS).Ticks);
    }

    private void FlushPendingConfigSave(bool force = false)
    {
        long ticks = Volatile.Read(ref m_configSaveScheduledAtTicks);
        if (ticks == 0)
            return;
        if (!force && DateTime.UtcNow.Ticks < ticks)
            return;
        Volatile.Write(ref m_configSaveScheduledAtTicks, 0);
        PluginWindowConfig.Save();
    }

    internal IntPtr Handle { get; private set; }

    public bool DrawEnabled => m_isVisible;
    public bool IsWindowed => m_appliedWindowMode == MyWindowModeEnum.Window;
    public bool IsActive => m_isActive;
    public Vector2I ClientSize => m_clientSize;

    // Physical pixel size of the window contents; on HiDPI displays this may be
    // larger than ClientSize. MySandboxGame.UpdateLinuxWindowResize reads this
    // to drive the DXVK backbuffer resize.
    internal Vector2I ClientSizePixels => m_clientSizePixels;

    // Programmatic resize. Called from window-management patches when user
    // video-settings trigger a mode change. Dispatches the SDL call onto the
    // render thread.
    internal void SetClientSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || Handle == IntPtr.Zero)
            return;

        SdlRenderThread.Dispatch(() =>
        {
            if (Handle == IntPtr.Zero)
                return;
            m_clientSize = new Vector2I(width, height);
            SDL_SetWindowSize(Handle, width, height);
            RefreshPixelSize();
        });
    }

    // Refresh the cached pixel size from SDL. Caller must be on the render
    // thread.
    private void RefreshPixelSize()
    {
        if (Handle == IntPtr.Zero)
            return;
        if (SDL_GetWindowSizeInPixels(Handle, out int w, out int h) && w > 0 && h > 0)
            m_clientSizePixels = new Vector2I(w, h);
        else
            m_clientSizePixels = m_clientSize;
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
            if (m_mouseOutsideWindow)
                return m_mousePosition;
            return m_mousePosition.IsValid()
                ? m_mousePosition
                : new Vector2(m_clientSize.X * 0.5f, m_clientSize.Y * 0.5f);
        }
        set
        {
            bool shouldWarp = Math.Abs(m_mousePosition.X - value.X) > 0.5f || Math.Abs(m_mousePosition.Y - value.Y) > 0.5f;
            m_mouseOutsideWindow = false;
            m_mousePosition = value;
            if (shouldWarp && Handle != IntPtr.Zero)
            {
                float wx = value.X, wy = value.Y;
                SdlRenderThread.Dispatch(() =>
                {
                    if (Handle != IntPtr.Zero)
                        SDL_WarpMouseInWindow(Handle, wx, wy);
                });
            }
        }
    }

    /// <summary>
    /// Tries to read the freshest in-window mouse position snapshot from the
    /// SDL polling thread under <see cref="m_bufferLock"/>, so the Vector2
    /// read is not torn against the per-iteration UpdateMouseSnapshot
    /// writer. Returns false when the cursor is outside the window or the
    /// snapshot is invalid — callers (the render-thread cursor repositioner)
    /// should leave the queued sprite untouched in that case rather than
    /// teleporting it to an off-window coordinate.
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
    /// Construct an SdlGameWindow on the render thread. The actual SDL3
    /// window is created from inside the render-thread context so all
    /// subsequent SDL calls remain affined to the same thread.
    /// </summary>
    internal static SdlGameWindow Create(string gameName, int width, int height, int? initialX = null, int? initialY = null)
    {
        return SdlRenderThread.Invoke(() => new SdlGameWindow(gameName, width, height, initialX, initialY));
    }

    private SdlGameWindow(string gameName, int width, int height, int? initialX, int? initialY)
    {
        // Constructor MUST run on the render thread — `Create` enforces this.
        // SDL_Init / SDL_SetHint / X11 driver selection are done once when
        // SdlRenderThread starts, so we do not repeat them here.
        if (!SdlRenderThread.IsCurrent)
            throw new InvalidOperationException(
                "SdlGameWindow constructor must run on the SDL render thread; use SdlGameWindow.Create.");

        if (!SdlRenderThread.IsInitialized)
            throw new PlatformNotSupportedException("SDL3 video initialization failed.");

        if (width > 0 && height > 0)
            m_clientSize = new Vector2I(width, height);

        Handle = SDL_CreateWindow(gameName ?? "SpaceEngineers", m_clientSize.X, m_clientSize.Y,
            SDL_WINDOW_HIDDEN | SDL_WINDOW_RESIZABLE | SDL_WINDOW_HIGH_PIXEL_DENSITY | SDL_WINDOW_VULKAN);

        if (Handle == IntPtr.Zero)
            throw new PlatformNotSupportedException("SDL3 window creation failed.");

        // Set the window/taskbar icon while the window is still hidden so
        // it appears with the correct icon the first time the WM maps it.
        // SDL_SetWindowIcon writes _NET_WM_ICON on X11, which is what
        // taskbars/launchers also read for the taskbar icon.
        SdlIconHelper.Apply(Handle, ResolveGameIcon());

        // Apply the caller-supplied initial geometry while the window is
        // still hidden so it shows up at the right place the first time
        // the WM maps it — rather than snapping from SDL's default position
        // to the saved position after the first ApplyModeChange.
        if (initialX.HasValue && initialY.HasValue)
        {
            SDL_SetWindowPosition(Handle, initialX.Value, initialY.Value);
            m_savedWindowedPosition = new Vector2I(initialX.Value, initialY.Value);
        }
        if (IsValidWindowedSize(m_clientSize.X, m_clientSize.Y))
            m_savedWindowedSize = m_clientSize;

        SDL_StartTextInput(Handle);
        UpdateMouseModeOnRenderThread();
        RefreshPixelSize();

        // Plug into the render-thread loop: HandleEvent runs for every
        // polled SDL event, UpdateMouseSnapshot runs once per iteration.
        SdlRenderThread.EventHandler += HandleEvent;
        SdlRenderThread.MouseSnapshotCallback = UpdateMouseSnapshot;

        Console.WriteLine(
            $"[LinuxCompat] SDL3 window created: logical={m_clientSize.X}x{m_clientSize.Y} " +
            $"pixels={m_clientSizePixels.X}x{m_clientSizePixels.Y} " +
            $"initialPos={(initialX.HasValue && initialY.HasValue ? $"({initialX},{initialY})" : "(default)")}");
    }

    // Same fallback chain used by ShowSplashScreenPatch: prefer the
    // explicit MyPerGameSettings.GameIcon, fall back to "<AppName>.ico"
    // (which resolves to "SpaceEngineers.ico" for SE).
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

        // Mode-change requests come from various threads (MyWindowsRender's
        // CreateRenderDevice / ApplyRenderSettings postfixes, video-settings
        // dialog, etc.). All SDL window operations must happen on the render
        // thread, so dispatch the apply there. FIFO ordering guarantees
        // that subsequent calls (e.g. ShowAndFocus) see the new mode applied.
        SdlRenderThread.Dispatch(() =>
        {
            if (Handle == IntPtr.Zero)
                return;
            ApplyModeChange(mode, width, height, desktopBounds);
            // The window's drawable size just changed (mode and/or
            // dimensions). Ask the per-frame processor to re-check the
            // backbuffer against the new SDL pixel size on its next tick.
            BackbufferResizeRequest.Request();
        });
    }

    private void ApplyModeChange(MyWindowModeEnum mode, int width, int height, Rectangle desktopBounds)
    {
        Rectangle displayBounds = GetWindowDisplayBounds();
        if (displayBounds.Width <= 0 || displayBounds.Height <= 0)
            displayBounds = desktopBounds;

        bool modeChanged = !m_appliedWindowMode.HasValue || m_appliedWindowMode.Value != mode;

        // First time we're ever called: hydrate saved windowed state from the
        // game config so subsequent Window transitions can restore it. This
        // also lets us respect a saved windowed size across launches even
        // when the initial mode is Fullscreen.
        if (!m_appliedWindowMode.HasValue && !m_savedWindowedSize.HasValue)
            LoadSavedWindowedState();

        // Switching AWAY from Window mode: capture current window size and
        // position so we can restore both when returning to Window.
        if (modeChanged
            && m_appliedWindowMode == MyWindowModeEnum.Window
            && mode != MyWindowModeEnum.Window)
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
                ApplyWindowedMode(width, height, displayBounds, modeChanged);
                break;

            case MyWindowModeEnum.FullscreenWindow:
                // XWayland does not honor the manual borderless+resize sequence
                // reliably, so map borderless fullscreen to SDL fullscreen there.
                // Native X11 keeps the borderless-window behavior.
                if (IsXWayland())
                {
                    ApplyFullscreenMode(displayBounds.Width, displayBounds.Height);
                    break;
                }
                SDL_SetWindowFullscreenMode(Handle, IntPtr.Zero);
                SDL_SetWindowFullscreen(Handle, false);
                SDL_SetWindowAlwaysOnTop(Handle, true);
                SDL_SetWindowBordered(Handle, false);
                SDL_SetWindowPosition(Handle, displayBounds.X, displayBounds.Y);
                SDL_SetWindowSize(Handle, displayBounds.Width, displayBounds.Height);
                m_clientSize = new Vector2I(displayBounds.Width, displayBounds.Height);
                RefreshPixelSize();
                break;

            case MyWindowModeEnum.Fullscreen:
                ApplyFullscreenMode(width, height);
                break;
        }

        m_appliedWindowMode = mode;
    }

    private void ApplyWindowedMode(int desiredWidth, int desiredHeight, Rectangle displayBounds, bool modeChanged)
    {
        // Only trust the display bounds for clamping when they look like a
        // real desktop. DXGI adapter DesktopBounds on DXVK sometimes report
        // the current swapchain buffer area (e.g. 58x128), which would
        // otherwise shrink the window to a few pixels.
        bool boundsOk = IsPlausibleDisplayBounds(displayBounds);

        int targetW = desiredWidth;
        int targetH = desiredHeight;
        if (boundsOk)
        {
            targetW = Math.Min(desiredWidth, displayBounds.Width);
            targetH = Math.Min(desiredHeight, displayBounds.Height);
        }
        if (targetW <= 0) targetW = desiredWidth;
        if (targetH <= 0) targetH = desiredHeight;

        if (modeChanged)
        {
            // Transition INTO Window mode (either from startup, fullscreen,
            // or fullscreen-window). Prefer the persisted size+position so
            // the window returns to where the user left it.
            int w = m_savedWindowedSize?.X ?? targetW;
            int h = m_savedWindowedSize?.Y ?? targetH;
            if (boundsOk)
            {
                w = Math.Min(w, displayBounds.Width);
                h = Math.Min(h, displayBounds.Height);
            }

            int x, y;
            if (m_savedWindowedPosition.HasValue)
            {
                x = m_savedWindowedPosition.Value.X;
                y = m_savedWindowedPosition.Value.Y;
            }
            else if (boundsOk)
            {
                x = displayBounds.X + (displayBounds.Width - w) / 2;
                y = displayBounds.Y + (displayBounds.Height - h) / 2;
            }
            else
            {
                // No trusted bounds and no saved position — let SDL keep its
                // default (centered on primary at create time).
                SDL_GetWindowPosition(Handle, out x, out y);
            }

            // Ensure the window is fully inside the current display. If the
            // saved position puts part of it offscreen (monitor reshuffle,
            // screen resolution change), shrink and/or recenter as needed.
            if (boundsOk)
                ClampWindowToDisplay(displayBounds, ref x, ref y, ref w, ref h);

            Console.WriteLine(
                $"[LinuxCompat] ApplyWindowedMode (transition): requested={desiredWidth}x{desiredHeight} " +
                $"applied={w}x{h} at ({x},{y}) displayBounds={displayBounds.Width}x{displayBounds.Height}" +
                $" trusted={boundsOk} savedSize={m_savedWindowedSize} savedPos={m_savedWindowedPosition}");

            SDL_SetWindowSize(Handle, w, h);
            SDL_SetWindowPosition(Handle, x, y);
            m_clientSize = new Vector2I(w, h);
            m_savedWindowedSize = new Vector2I(w, h);
            m_savedWindowedPosition = new Vector2I(x, y);
            PersistSavedWindowedState();
        }
        else if (targetW != m_clientSize.X || targetH != m_clientSize.Y)
        {
            // Settings-dialog resolution change while already in Window mode.
            // Resize without repositioning; if the new size no longer fits at
            // the current position, recenter it on the display.
            bool havePos = SDL_GetWindowPosition(Handle, out int curX, out int curY);
            int x = havePos
                ? curX
                : (boundsOk
                    ? displayBounds.X + (displayBounds.Width - targetW) / 2
                    : 0);
            int y = havePos
                ? curY
                : (boundsOk
                    ? displayBounds.Y + (displayBounds.Height - targetH) / 2
                    : 0);

            int w = targetW;
            int h = targetH;
            if (boundsOk)
                ClampWindowToDisplay(displayBounds, ref x, ref y, ref w, ref h);

            Console.WriteLine(
                $"[LinuxCompat] ApplyWindowedMode (resize): requested={desiredWidth}x{desiredHeight} " +
                $"applied={w}x{h} at ({x},{y}) displayBounds={displayBounds.Width}x{displayBounds.Height}" +
                $" trusted={boundsOk}");

            SDL_SetWindowSize(Handle, w, h);
            if (!havePos || x != curX || y != curY)
                SDL_SetWindowPosition(Handle, x, y);
            m_clientSize = new Vector2I(w, h);
            m_savedWindowedSize = new Vector2I(w, h);
            m_savedWindowedPosition = new Vector2I(x, y);
            PersistSavedWindowedState();
        }
        // else: already at the requested size (mirror-back from a user drag
        // that UpdateLinuxWindowResize pushed through SwitchSettings). Leave
        // the window geometry alone so we don't fight the window manager.

        RefreshPixelSize();
    }

    private static void ClampWindowToDisplay(Rectangle displayBounds, ref int x, ref int y, ref int w, ref int h)
    {
        if (displayBounds.Width <= 0 || displayBounds.Height <= 0)
            return;
        if (w > displayBounds.Width) w = displayBounds.Width;
        if (h > displayBounds.Height) h = displayBounds.Height;
        if (x < displayBounds.X) x = displayBounds.X;
        if (y < displayBounds.Y) y = displayBounds.Y;
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
        if (SDL_GetWindowPosition(Handle, out int x, out int y))
            m_savedWindowedPosition = new Vector2I(x, y);
        PersistSavedWindowedState();
    }

    private void PersistSavedWindowedState()
    {
        if (m_savedWindowedSize.HasValue
            && IsValidWindowedSize(m_savedWindowedSize.Value.X, m_savedWindowedSize.Value.Y))
            PluginWindowConfig.SetWindowedSize(m_savedWindowedSize.Value.X, m_savedWindowedSize.Value.Y);
        if (m_savedWindowedPosition.HasValue)
            PluginWindowConfig.SetWindowedPosition(m_savedWindowedPosition.Value.X, m_savedWindowedPosition.Value.Y);
    }

    private void LoadSavedWindowedState()
    {
        if (PluginWindowConfig.TryGetWindowedSize(out int w, out int h)
            && IsValidWindowedSize(w, h))
            m_savedWindowedSize = new Vector2I(w, h);
        if (PluginWindowConfig.TryGetWindowedPosition(out int x, out int y))
            m_savedWindowedPosition = new Vector2I(x, y);
    }

    private unsafe void ApplyFullscreenMode(int width, int height)
    {
        // SDL3 requires a populated SDL_DisplayMode struct to be set on the
        // window before SDL_SetWindowFullscreen(true) will switch resolutions.
        // SDL_GetClosestFullscreenDisplayMode returns the closest supported
        // mode for the requested size; pass it by pointer to
        // SDL_SetWindowFullscreenMode.
        //
        // Pass includeHighDensityModes=true so SDL can pick a mode whose
        // pixel_density matches the current desktop's content scale. On X11
        // with `xrandr --scale`, going exclusive-fullscreen normally performs
        // an XRRSetScreenConfigAndRate which drops the output's scale factor
        // (xrandr treats scale as separate from mode), so the 200% desktop
        // resets to 100% on exit. Picking a high-density mode lets SDL honor
        // the existing pixel_density rather than forcing a scale-less mode.
        uint displayId = SDL_GetDisplayForWindow(Handle);
        if (displayId != 0 &&
            SDL_GetClosestFullscreenDisplayMode(displayId, width, height, 0f, true, out SdlDisplayMode mode))
        {
            Console.WriteLine($"[LinuxCompat] Fullscreen mode: requested {width}x{height}, " +
                              $"picked {mode.W}x{mode.H} @ {mode.RefreshRate:F2}Hz, pixel_density={mode.PixelDensity:F2}");
            SdlDisplayMode* modePtr = &mode;
            SDL_SetWindowFullscreenMode(Handle, (IntPtr)modePtr);
        }
        SDL_SetWindowFullscreen(Handle, true);
        m_clientSize = new Vector2I(width, height);
        RefreshPixelSize();
    }

    private Rectangle GetWindowDisplayBounds()
    {
        if (Handle != IntPtr.Zero)
        {
            uint displayId = SDL_GetDisplayForWindow(Handle);
            if (displayId != 0 && SDL_GetDisplayBounds(displayId, out SdlRect rect)
                && rect.W > 0 && rect.H > 0)
                return new Rectangle(rect.X, rect.Y, rect.W, rect.H);
        }

        // Fall back to the primary display. SDL_GetDisplayForWindow can return
        // 0 for a hidden / never-positioned window (first OnModeChanged fires
        // before ShowAndFocus on Linux).
        uint primary = SDL_GetPrimaryDisplay();
        if (primary != 0 && SDL_GetDisplayBounds(primary, out SdlRect prect)
            && prect.W > 0 && prect.H > 0)
            return new Rectangle(prect.X, prect.Y, prect.W, prect.H);

        return default;
    }

    // Rectangle looks like real desktop geometry (not a bogus DXGI adapter
    // output that reports the swapchain buffer area). Used before clamping
    // a requested window size so we don't shrink the window to a few pixels.
    private static bool IsPlausibleDisplayBounds(Rectangle r) =>
        r.Width >= 640 && r.Height >= 480;

    private static bool IsXWayland()
    {
        string sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        return string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase);
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
            m_messageHandlers[wm] = (ActionRef<MyMessage>)Delegate.Combine(m_messageHandlers[wm], action);
        else
            m_messageHandlers.Add(wm, action);
    }

    public void RemoveMessageHandler(uint wm, ActionRef<MyMessage> action)
    {
        if (m_messageHandlers.ContainsKey(wm))
            m_messageHandlers[wm] = (ActionRef<MyMessage>)Delegate.Remove(m_messageHandlers[wm], action);
    }

    public void CloseManually()
    {
        Exit();
    }

    private void HandleManualWindowCloseRequest()
    {
        if (OnManualWindowCloseRequest != null && m_isVisible)
        {
            OnManualWindowCloseRequest();
            return;
        }

        Hide();
        CloseManually();
    }

    /// <summary>
    /// Stub. Event polling is owned by <see cref="SdlRenderThread"/>'s loop;
    /// callers used to invoke this to nudge the SDL pump from main / SE
    /// render thread, but that is no longer necessary.
    /// </summary>
    public void DoEvents()
    {
    }

    public void Exit()
    {
        m_isVisible = false;
        m_isActive = false;
        // Ensure any debounced window-geometry change reaches disk before the
        // game tears down. MySandboxGame.OnExit does not flush the config.
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
        // Event polling, mode-change application and clipboard pumping all
        // live on SdlRenderThread now (see SdlRenderThread.Run). The only
        // main-thread housekeeping left here is flushing any debounced
        // window-config save once the user finishes dragging — that path
        // writes the SE config XML and is intentionally kept off the SDL
        // thread to avoid blocking event polling on disk I/O.
        FlushPendingConfigSave();
    }

    public void SetCursor(Stream stream)
    {
    }

    public void ShowAndFocus()
    {
        m_isVisible = true;
        m_isActive = true;

        // Any OnModeChanged dispatched before this call has already been
        // queued onto the render thread; FIFO order ensures it runs before
        // the SDL_ShowWindow we issue here, so fullscreen starts in the
        // final mode rather than briefly flashing at the initial windowed
        // geometry.
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

    // IVRageInput2
    uint[] IVRageInput2.DeveloperKeys => new uint[4];
    bool IVRageInput2.IsCorrectlyInitialized => true;

    void IVRageInput2.GetMouseState(out MyMouseState state)
    {
        // No DoEvents() needed: SdlRenderThread pumps events autonomously
        // and refreshes our snapshot every iteration.
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

    List<string> IVRageInput2.EnumerateJoystickNames() => new();
    string IVRageInput2.InitializeJoystickIfPossible(string joystickInstanceName) => null;
    bool IVRageInput2.IsJoystickAxisSupported(MyJoystickAxesEnum axis) => false;
    bool IVRageInput2.IsJoystickConnected() => false;
    void IVRageInput2.GetJoystickState(ref MyJoystickState state) { }
    void IVRageInput2.ShowVirtualKeyboardIfNeeded(Action<string> onSuccess, Action onCancel, string defaultText, string title, int maxLength) { }
    unsafe void IVRageInput2.GetAsyncKeyStates(byte* data) => CopyAsyncKeyStates(data);
    void IDisposable.Dispose() { }

    /// <summary>
    /// Returns the most recent input snapshot. Reads only cached fields;
    /// the render thread's <see cref="UpdateMouseSnapshot"/> populates them.
    /// </summary>
    internal void GetMouseInputState(out MyMouseInputState state)
    {
        if (Handle == IntPtr.Zero)
        {
            state = default;
            return;
        }

        uint buttonState;
        float relX, relY;
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
    /// Render-thread callback invoked by <see cref="SdlRenderThread"/>'s
    /// loop after each event-pump pass. Refreshes the cached mouse snapshot
    /// (absolute position, button state, accumulated relative delta) and
    /// the live window size so off-thread readers don't have to issue SDL
    /// calls from the wrong thread.
    /// </summary>
    private void UpdateMouseSnapshot()
    {
        if (Handle == IntPtr.Zero)
            return;

        uint buttonState = SDL_GetMouseState(out var mouseX, out var mouseY);
        SDL_GetRelativeMouseState(out var relX, out var relY);

        if (SDL_GetWindowSize(Handle, out int curW, out int curH) && curW > 0 && curH > 0)
            m_clientSize = new Vector2I(curW, curH);

        lock (m_bufferLock)
        {
            m_mouseButtonState = buttonState;
            // SDL_GetMouseState can stick to the last in-window coordinate
            // after the pointer leaves. Trust SDL's leave/enter events
            // first: once we know the pointer left, keep reporting an
            // out-of-window position until an enter event arrives.
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

        // Mirror mouse buttons into the async-keystate buffer so that
        // MyInput.IsKeyPress(MyKeys.LeftButton) etc. observe them. On
        // Windows GetAsyncKeyState reports VK_LBUTTON/RBUTTON/MBUTTON/
        // XBUTTON1/XBUTTON2 alongside keyboard keys, and game code plus
        // mods (notably Build Vision 2 via RichHud, which polls
        // MyAPIGateway.Input.IsKeyPress(MyKeys.LeftButton) for its
        // "Select/Confirm" bind) rely on that. SDL3 keeps mouse buttons in
        // a separate state stream, so without this mirror MyKeys-backed
        // mouse-button binds never fire on Linux even though the
        // MyMouseState path (IsLeftMousePressed/IsRightMousePressed) works.
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
    /// Render-thread event handler installed in
    /// <see cref="SdlRenderThread.EventHandler"/>. All access to SDL3 inside
    /// this method is therefore on the SDL-owning thread.
    /// </summary>
    private void HandleEvent(ref SdlRenderThread.SdlEvent sdlEvent)
    {
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
                m_mouseOutsideWindow = true;
                m_mousePosition = -Vector2.One;
                break;
            case SDL_EVENT_WINDOW_MOUSE_ENTER:
                m_mouseOutsideWindow = false;
                break;
            case SDL_EVENT_WINDOW_MOUSE_LEAVE:
                m_mouseOutsideWindow = true;
                m_mousePosition = -Vector2.One;
                break;
            case SDL_EVENT_WINDOW_RESIZED:
                m_clientSize = new Vector2I(sdlEvent.Window.Data1, sdlEvent.Window.Data2);
                RefreshPixelSize();
                BackbufferResizeRequest.Request();
                // Track the real windowed geometry whenever we're in Window
                // mode — whether the user dragged the edge or settings drove
                // it. Reject implausibly tiny sizes so we can't persist
                // garbage if the WM or DXVK reports a transient bogus size.
                if (m_appliedWindowMode == MyWindowModeEnum.Window
                    && IsValidWindowedSize(m_clientSize.X, m_clientSize.Y))
                {
                    m_savedWindowedSize = m_clientSize;
                    PluginWindowConfig.SetWindowedSize(m_clientSize.X, m_clientSize.Y);
                    ScheduleConfigSave();
                }
                break;
            case SDL_EVENT_WINDOW_MOVED:
                if (m_appliedWindowMode == MyWindowModeEnum.Window)
                {
                    int px = sdlEvent.Window.Data1;
                    int py = sdlEvent.Window.Data2;
                    m_savedWindowedPosition = new Vector2I(px, py);
                    PluginWindowConfig.SetWindowedPosition(px, py);
                    ScheduleConfigSave();
                }
                break;
            case SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
                m_clientSizePixels = new Vector2I(sdlEvent.Window.Data1, sdlEvent.Window.Data2);
                BackbufferResizeRequest.Request();
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

    // When focus is restored (e.g. user alt-tabbed away with the cursor
    // outside the window and came back), the OS pointer can be sitting
    // anywhere — far outside the window, or pinned at the very last pixel
    // of an adjacent screen edge. Two failure modes occur on Linux/X11:
    //
    //   1. Pointer ends up outside the window. The software cursor uses
    //      window-relative coordinates and DrawMouseCursor hides itself
    //      once those go negative or beyond the client size, so the
    //      in-game cursor stays invisible until the user wiggles it back
    //      inside. Fix: warp the OS pointer to the window center.
    //
    //   2. Pointer is technically inside the window (e.g. global x=3839
    //      in a 3840-wide maximized window), but FOCUS_LOST set
    //      m_mouseOutsideWindow=true and m_mousePosition=(-1,-1). SDL
    //      does not reliably synthesize MOUSE_ENTER after FOCUS_GAINED
    //      when the pointer was already inside the window at the moment
    //      focus returned — verified at the right screen edge: FOCUS_LOST
    //      → FOCUS_GAINED, then no MOUSE_ENTER for several seconds —
    //      while the left edge happens to self-heal via a quick WM-driven
    //      LEAVE/ENTER cycle (panel/hot-zone) so the bug appeared
    //      asymmetric. GetMouseInputState then keeps clamping
    //      m_mousePosition to (-1,-1) and the software cursor stays
    //      invisible. Fix: clear m_mouseOutsideWindow and seed
    //      m_mousePosition from the known global coords ourselves — we
    //      have authoritative info that the pointer is in the window, no
    //      need to wait for an event SDL may never deliver.
    //
    // Only acts when the GUI cursor is active (m_showCursor == true);
    // during gameplay relative mouse mode already pins the cursor to the
    // center.
    //
    // The global-vs-window-bounds test also distinguishes alt-tab from
    // mouse-edge focus-follows-mouse in plain Window mode: a focus change
    // caused by the pointer crossing the window edge necessarily leaves
    // the pointer inside the window when FOCUS_GAINED fires, so it falls
    // into the no-warp branch and the cursor stays where the user just
    // moved it.
    private void RecenterCursorIfOutsideWindow()
    {
        if (Handle == IntPtr.Zero)
            return;
        if (!m_showCursor)
            return;
        if (!SDL_GetWindowSize(Handle, out int w, out int h) || w <= 0 || h <= 0)
            return;
        if (!SDL_GetWindowPosition(Handle, out int wx, out int wy))
            return;
        SDL_GetGlobalMouseState(out float gx, out float gy);
        bool inside = gx >= wx && gy >= wy && gx < wx + w && gy < wy + h;
        if (inside)
        {
            m_mouseOutsideWindow = false;
            m_mousePosition = new Vector2(gx - wx, gy - wy);
            return;
        }
        float cx = w * 0.5f;
        float cy = h * 0.5f;
        SDL_WarpMouseInWindow(Handle, cx, cy);
        m_mouseOutsideWindow = false;
        m_mousePosition = new Vector2(cx, cy);
    }

    private void DispatchUpdateMouseMode()
    {
        SdlRenderThread.Dispatch(UpdateMouseModeOnRenderThread);
    }

    private void UpdateMouseModeOnRenderThread()
    {
        if (Handle == IntPtr.Zero)
            return;

        // On Linux, capture the cursor via relative mouse mode whenever the game
        // wants it hidden (in-game camera control). In the main menu m_showCursor
        // is true and the cursor moves freely.
        SDL_SetWindowRelativeMouseMode(Handle, !m_showCursor);

        // Always hide the OS/hardware cursor. The game renders its own software
        // cursor; showing both produces a visible offset because the software
        // cursor is drawn 1-2 frames behind the real pointer position.
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
        SetKeyState(MyKeys.Control, GetKeyState(MyKeys.LeftControl) || GetKeyState(MyKeys.RightControl));
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

    [DllImport(Lib, EntryPoint = "SDL_ShowWindow")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_ShowWindow(IntPtr window);

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
    private static extern bool SDL_SetWindowBordered(IntPtr window, [MarshalAs(UnmanagedType.I1)] bool bordered);

    [DllImport(Lib, EntryPoint = "SDL_SetWindowAlwaysOnTop")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowAlwaysOnTop(IntPtr window, [MarshalAs(UnmanagedType.I1)] bool onTop);

    [DllImport(Lib, EntryPoint = "SDL_SetWindowFullscreen")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowFullscreen(IntPtr window, [MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport(Lib, EntryPoint = "SDL_SetWindowFullscreenMode")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowFullscreenMode(IntPtr window, IntPtr mode);

    [DllImport(Lib, EntryPoint = "SDL_GetClosestFullscreenDisplayMode")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetClosestFullscreenDisplayMode(uint displayId, int w, int h, float refreshRate, [MarshalAs(UnmanagedType.I1)] bool includeHighDensityModes, out SdlDisplayMode mode);

    [DllImport(Lib, EntryPoint = "SDL_GetDisplayForWindow")]
    private static extern uint SDL_GetDisplayForWindow(IntPtr window);

    [DllImport(Lib, EntryPoint = "SDL_GetPrimaryDisplay")]
    private static extern uint SDL_GetPrimaryDisplay();

    [DllImport(Lib, EntryPoint = "SDL_GetDisplayBounds")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetDisplayBounds(uint displayId, out SdlRect rect);

    [DllImport(Lib, EntryPoint = "SDL_GetWindowSize")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetWindowSize(IntPtr window, out int w, out int h);

    [DllImport(Lib, EntryPoint = "SDL_GetWindowSizeInPixels")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetWindowSizeInPixels(IntPtr window, out int w, out int h);

    [DllImport(Lib, EntryPoint = "SDL_StartTextInput")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_StartTextInput(IntPtr window);

    [DllImport(Lib, EntryPoint = "SDL_GetMouseState")]
    private static extern uint SDL_GetMouseState(out float x, out float y);

    [DllImport(Lib, EntryPoint = "SDL_GetRelativeMouseState")]
    private static extern uint SDL_GetRelativeMouseState(out float x, out float y);

    [DllImport(Lib, EntryPoint = "SDL_GetGlobalMouseState")]
    private static extern uint SDL_GetGlobalMouseState(out float x, out float y);

    [DllImport(Lib, EntryPoint = "SDL_WarpMouseInWindow")]
    private static extern void SDL_WarpMouseInWindow(IntPtr window, float x, float y);

    [DllImport(Lib, EntryPoint = "SDL_SetWindowRelativeMouseMode")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowRelativeMouseMode(IntPtr window, [MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport(Lib, EntryPoint = "SDL_HideCursor")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_HideCursor();

    #endregion
}

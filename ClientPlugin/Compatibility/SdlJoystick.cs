using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using VRage.Input;

namespace ClientPlugin.Compatibility;

/// <summary>
/// SDL3 joystick/gamepad backend replacing the Windows DirectInput+XInput
/// implementation (<c>VRage.Platform.Windows.Input.MyDirectInput</c>) behind
/// the five joystick methods of <c>IVRageInput2</c> (see SdlGameWindow).
///
/// Threading contract (mirrors the mouse-snapshot pattern):
///  - <see cref="Initialize"/>, <see cref="HandleEvent"/> and
///    <see cref="UpdateSnapshot"/> run on the SDL render thread only.
///  - The game thread reads a lock-protected snapshot
///    (<see cref="IsJoystickConnected"/>, <see cref="GetJoystickState"/>,
///    <see cref="EnumerateJoystickNames"/>, <see cref="IsJoystickAxisSupported"/>).
///  - <see cref="InitializeJoystickIfPossible"/> is called by MyVRageInput
///    every frame while no device is connected, so it must stay cheap: it
///    only dispatches to the SDL thread when the requested name matches an
///    attached device or a device is currently open.
///
/// State encoding matches what the game gets on Windows so the
/// deadzone/sensitivity/exponent math in MyVRageInput/MyControllerHelper
/// behaves identically:
///  - axes 0..65535 with 32768 center (MyDirectInput forces InputRange(0, 65535)),
///  - Buttons[] bytes with 0x80 = pressed (game tests &gt; 0),
///  - POV hats in hundredths of degrees clockwise from north, -1 centered,
///  - Xbox-class devices use the XInput-over-DirectInput layout: left stick
///    on X/Y, right stick on RotationX/Y, combined triggers on Z plus
///    separate Z_Left/Z_Right, buttons in XInput order, d-pad on POV 0.
/// </summary>
internal static class SdlJoystick
{
    private const string Lib = "libSDL3.so";

    private const uint SDL_INIT_JOYSTICK = 0x200u;
    private const uint SDL_INIT_GAMEPAD = 0x2000u;

    private const uint SDL_EVENT_JOYSTICK_ADDED = 0x605u;
    private const uint SDL_EVENT_JOYSTICK_REMOVED = 0x606u;

    // SDL_GamepadAxis
    private const int SDL_GAMEPAD_AXIS_LEFTX = 0;
    private const int SDL_GAMEPAD_AXIS_LEFTY = 1;
    private const int SDL_GAMEPAD_AXIS_RIGHTX = 2;
    private const int SDL_GAMEPAD_AXIS_RIGHTY = 3;
    private const int SDL_GAMEPAD_AXIS_LEFT_TRIGGER = 4;
    private const int SDL_GAMEPAD_AXIS_RIGHT_TRIGGER = 5;

    // SDL_GamepadButton
    private const int SDL_GAMEPAD_BUTTON_SOUTH = 0;
    private const int SDL_GAMEPAD_BUTTON_EAST = 1;
    private const int SDL_GAMEPAD_BUTTON_WEST = 2;
    private const int SDL_GAMEPAD_BUTTON_NORTH = 3;
    private const int SDL_GAMEPAD_BUTTON_BACK = 4;
    private const int SDL_GAMEPAD_BUTTON_START = 6;
    private const int SDL_GAMEPAD_BUTTON_LEFT_STICK = 7;
    private const int SDL_GAMEPAD_BUTTON_RIGHT_STICK = 8;
    private const int SDL_GAMEPAD_BUTTON_LEFT_SHOULDER = 9;
    private const int SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER = 10;
    private const int SDL_GAMEPAD_BUTTON_DPAD_UP = 11;
    private const int SDL_GAMEPAD_BUTTON_DPAD_DOWN = 12;
    private const int SDL_GAMEPAD_BUTTON_DPAD_LEFT = 13;
    private const int SDL_GAMEPAD_BUTTON_DPAD_RIGHT = 14;

    // SDL hat bitmask
    private const byte SDL_HAT_UP = 0x01;
    private const byte SDL_HAT_RIGHT = 0x02;
    private const byte SDL_HAT_DOWN = 0x04;
    private const byte SDL_HAT_LEFT = 0x08;

    // XInput button order produced by the Windows xusb driver through
    // DirectInput: A, B, X, Y, LB, RB, Back, Start, LS, RS. The game's
    // default controller bindings (J01..J10) assume this order.
    private static readonly int[] GamepadButtonOrder =
    {
        SDL_GAMEPAD_BUTTON_SOUTH,
        SDL_GAMEPAD_BUTTON_EAST,
        SDL_GAMEPAD_BUTTON_WEST,
        SDL_GAMEPAD_BUTTON_NORTH,
        SDL_GAMEPAD_BUTTON_LEFT_SHOULDER,
        SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER,
        SDL_GAMEPAD_BUTTON_BACK,
        SDL_GAMEPAD_BUTTON_START,
        SDL_GAMEPAD_BUTTON_LEFT_STICK,
        SDL_GAMEPAD_BUTTON_RIGHT_STICK,
    };

    private static readonly object Lock = new object();

    // SDL thread only
    private static bool s_subsystemReady;
    private static IntPtr s_joystick;
    private static IntPtr s_gamepad;

    // Shared, guarded by Lock
    private static List<string> s_deviceNames = new List<string>();
    private static string s_openDeviceName;
    private static bool s_connected;
    private static MyJoystickState s_state;
    private static bool s_xSupported, s_ySupported, s_zSupported;
    private static bool s_rxSupported, s_rySupported, s_rzSupported;
    private static bool s_slider1Supported, s_slider2Supported;

    #region SDL render thread

    /// <summary>
    /// Called once from SdlRenderThread.Run after SDL_Init(VIDEO) succeeded.
    /// </summary>
    internal static void Initialize()
    {
        // Keep reporting device state while the game window is unfocused;
        // the game applies its own focus gate in MyVRageInput.Update.
        SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");

        if (!SDL_InitSubSystem(SDL_INIT_JOYSTICK | SDL_INIT_GAMEPAD))
        {
            Console.WriteLine($"[LinuxCompat] SdlJoystick SDL_InitSubSystem(JOYSTICK|GAMEPAD) failed: {GetErrorString()}");
            return;
        }

        s_subsystemReady = true;
        RefreshDeviceNames();
        Console.WriteLine("[LinuxCompat] SdlJoystick initialised SDL3 (joystick, gamepad)");
    }

    /// <summary>
    /// Called from the SDL event loop for every polled event; reacts to
    /// device hotplug. Cheap no-op for all other event types.
    /// </summary>
    internal static void HandleEvent(uint eventType)
    {
        if (!s_subsystemReady)
            return;

        if (eventType == SDL_EVENT_JOYSTICK_ADDED || eventType == SDL_EVENT_JOYSTICK_REMOVED)
        {
            RefreshDeviceNames();
            Console.WriteLine(eventType == SDL_EVENT_JOYSTICK_ADDED
                ? "[LinuxCompat] SdlJoystick device added"
                : "[LinuxCompat] SdlJoystick device removed");

            // Tell the game a device changed so the Options -> Controller list
            // refreshes live and a just-plugged-in selected device gets opened
            // without a restart. On Windows MyGameForm drives this from the
            // WM_DEVICECHANGE window message; SDL has no such pump on Linux, so
            // bridge the SDL hotplug event to the same MyVRageInput callback.
            // DeviceChangeCallback mutates game-thread input state, so it must
            // run on the main thread — hop off the SDL thread via the queue
            // that Plugin.Update drains. MyInput.Static is null until input is
            // initialized (early startup hotplug), hence the null-guard.
            MainThreadDispatcher.Post(() => MyInput.Static?.DeviceChangeCallback());
        }
    }

    /// <summary>
    /// Called every SDL loop iteration after the event pump. Refreshes the
    /// state snapshot of the open device; detects physical disconnect.
    /// </summary>
    internal static void UpdateSnapshot()
    {
        if (!s_subsystemReady || s_joystick == IntPtr.Zero)
            return;

        if (!SDL_JoystickConnected(s_joystick))
        {
            string name;
            lock (Lock)
                name = s_openDeviceName;
            Console.WriteLine($"[LinuxCompat] SdlJoystick device disconnected: {name}");
            CloseDevice();
            return;
        }

        var state = new MyJoystickState();
        if (s_gamepad != IntPtr.Zero)
            FillFromGamepad(ref state);
        else
            FillFromJoystick(ref state);

        lock (Lock)
        {
            s_state = state;
            s_connected = true;
        }
    }

    private static unsafe void FillFromGamepad(ref MyJoystickState state)
    {
        state.X = SDL_GetGamepadAxis(s_gamepad, SDL_GAMEPAD_AXIS_LEFTX) + 32768;
        state.Y = SDL_GetGamepadAxis(s_gamepad, SDL_GAMEPAD_AXIS_LEFTY) + 32768;
        state.RotationX = SDL_GetGamepadAxis(s_gamepad, SDL_GAMEPAD_AXIS_RIGHTX) + 32768;
        state.RotationY = SDL_GetGamepadAxis(s_gamepad, SDL_GAMEPAD_AXIS_RIGHTY) + 32768;

        // Triggers 0..32767. The xusb driver reports them combined on Z
        // (32768 + (LT - RT) scaled); XInput additionally provides separate
        // Z_Left/Z_Right which MyDirectInput copies as trigger*256 (0..65280).
        int lt = SDL_GetGamepadAxis(s_gamepad, SDL_GAMEPAD_AXIS_LEFT_TRIGGER);
        int rt = SDL_GetGamepadAxis(s_gamepad, SDL_GAMEPAD_AXIS_RIGHT_TRIGGER);
        state.Z = Math.Clamp(32768 + lt - rt, 0, 65535);
        state.Z_Left = lt * 2;
        state.Z_Right = rt * 2;

        for (int i = 0; i < GamepadButtonOrder.Length; i++)
            state.Buttons[i] = SDL_GetGamepadButton(s_gamepad, GamepadButtonOrder[i]) ? (byte)0x80 : (byte)0;

        bool up = SDL_GetGamepadButton(s_gamepad, SDL_GAMEPAD_BUTTON_DPAD_UP);
        bool down = SDL_GetGamepadButton(s_gamepad, SDL_GAMEPAD_BUTTON_DPAD_DOWN);
        bool left = SDL_GetGamepadButton(s_gamepad, SDL_GAMEPAD_BUTTON_DPAD_LEFT);
        bool right = SDL_GetGamepadButton(s_gamepad, SDL_GAMEPAD_BUTTON_DPAD_RIGHT);
        state.PointOfViewControllers[0] = DirectionsToPov(up, right, down, left);
        state.PointOfViewControllers[1] = -1;
        state.PointOfViewControllers[2] = -1;
        state.PointOfViewControllers[3] = -1;
    }

    private static unsafe void FillFromJoystick(ref MyJoystickState state)
    {
        int axes = SDL_GetNumJoystickAxes(s_joystick);
        for (int i = 0; i < axes && i < 8; i++)
        {
            int value = SDL_GetJoystickAxis(s_joystick, i) + 32768;
            switch (i)
            {
                case 0: state.X = value; break;
                case 1: state.Y = value; break;
                case 2: state.Z = value; break;
                case 3: state.RotationX = value; break;
                case 4: state.RotationY = value; break;
                case 5: state.RotationZ = value; break;
                case 6: state.Sliders[0] = value; break;
                case 7: state.Sliders[1] = value; break;
            }
        }

        int buttons = Math.Min(SDL_GetNumJoystickButtons(s_joystick), 128);
        for (int i = 0; i < buttons; i++)
            state.Buttons[i] = SDL_GetJoystickButton(s_joystick, i) ? (byte)0x80 : (byte)0;

        int hats = Math.Min(SDL_GetNumJoystickHats(s_joystick), 4);
        for (int i = 0; i < 4; i++)
        {
            if (i < hats)
            {
                byte hat = SDL_GetJoystickHat(s_joystick, i);
                state.PointOfViewControllers[i] = DirectionsToPov(
                    (hat & SDL_HAT_UP) != 0,
                    (hat & SDL_HAT_RIGHT) != 0,
                    (hat & SDL_HAT_DOWN) != 0,
                    (hat & SDL_HAT_LEFT) != 0);
            }
            else
            {
                state.PointOfViewControllers[i] = -1;
            }
        }
    }

    /// <summary>
    /// Digital directions to a DirectInput POV angle in hundredths of
    /// degrees clockwise from north; -1 when centered.
    /// </summary>
    private static int DirectionsToPov(bool up, bool right, bool down, bool left)
    {
        if (up && right) return 4500;
        if (right && down) return 13500;
        if (down && left) return 22500;
        if (left && up) return 31500;
        if (up) return 0;
        if (right) return 9000;
        if (down) return 18000;
        if (left) return 27000;
        return -1;
    }

    /// <summary>
    /// Refresh the attached-device name list. SDL thread only.
    /// </summary>
    private static unsafe void RefreshDeviceNames()
    {
        var names = new List<string>();
        IntPtr ids = SDL_GetJoysticks(out int count);
        if (ids != IntPtr.Zero)
        {
            for (int i = 0; i < count; i++)
            {
                uint id = ((uint*)ids)[i];
                IntPtr namePtr = SDL_GetJoystickNameForID(id);
                string name = namePtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(namePtr) : null;
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }
            SDL_free(ids);
        }

        bool changed;
        lock (Lock)
        {
            changed = !names.SequenceEqual(s_deviceNames);
            s_deviceNames = names;
        }

        // Log the attached device names whenever the set changes. This is the
        // string the player must match in Options -> Controller (substring),
        // so surfacing it makes "why isn't my controller selected" debuggable.
        if (changed)
            Console.WriteLine(names.Count == 0
                ? "[LinuxCompat] SdlJoystick devices: (none)"
                : $"[LinuxCompat] SdlJoystick devices: {string.Join(", ", names.Select(n => $"'{n}'"))}");
    }

    /// <summary>
    /// Open the first attached device whose name contains
    /// <paramref name="joystickInstanceName"/>, mirroring
    /// MyDirectInput.InitializeJoystickIfPossible exactly: a null name means
    /// "Disabled" (Options -> Controller sets the instance name to null when
    /// the user picks the "Disabled" entry, see MyGuiScreenOptionsController)
    /// and opens NO device. A non-null name opens only the matching device,
    /// so an unselected controller — e.g. one the player never chose but
    /// leaves plugged in — never becomes active on its own. SDL thread only.
    /// </summary>
    private static unsafe string OpenDevice(string joystickInstanceName)
    {
        CloseDevice();

        // Disabled / nothing selected: open no device, just like Windows.
        // Opening the first available device here was the cause of the
        // "cannot disable the controller" and "unselected controllers
        // activate" bugs.
        if (joystickInstanceName == null)
            return null;

        RefreshDeviceNames();

        string openedName = null;
        IntPtr ids = SDL_GetJoysticks(out int count);
        if (ids == IntPtr.Zero)
            return null;

        try
        {
            for (int i = 0; i < count; i++)
            {
                uint id = ((uint*)ids)[i];
                IntPtr namePtr = SDL_GetJoystickNameForID(id);
                string name = namePtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(namePtr) : null;
                if (string.IsNullOrEmpty(name))
                    continue;
                if (!name.Contains(joystickInstanceName))
                    continue;

                s_joystick = SDL_OpenJoystick(id);
                if (s_joystick == IntPtr.Zero)
                {
                    Console.WriteLine($"[LinuxCompat] SdlJoystick failed to open '{name}': {GetErrorString()}");
                    continue;
                }

                if (SDL_IsGamepad(id))
                    s_gamepad = SDL_OpenGamepad(id);

                openedName = name;
                break;
            }
        }
        finally
        {
            SDL_free(ids);
        }

        if (openedName == null)
            return null;

        bool isGamepad = s_gamepad != IntPtr.Zero;
        int axes = SDL_GetNumJoystickAxes(s_joystick);
        int buttons = SDL_GetNumJoystickButtons(s_joystick);
        int hats = SDL_GetNumJoystickHats(s_joystick);

        lock (Lock)
        {
            s_openDeviceName = openedName;
            if (isGamepad)
            {
                // XInput-style layout: sticks + combined triggers on Z.
                // ZLeft/ZRight report Z support like MyDirectInput does.
                s_xSupported = s_ySupported = s_zSupported = true;
                s_rxSupported = s_rySupported = true;
                s_rzSupported = false;
                s_slider1Supported = s_slider2Supported = false;
            }
            else
            {
                s_xSupported = axes > 0;
                s_ySupported = axes > 1;
                s_zSupported = axes > 2;
                s_rxSupported = axes > 3;
                s_rySupported = axes > 4;
                s_rzSupported = axes > 5;
                s_slider1Supported = axes > 6;
                s_slider2Supported = axes > 7;
            }
        }

        Console.WriteLine(
            $"[LinuxCompat] SdlJoystick opened '{openedName}' " +
            $"({(isGamepad ? "gamepad" : "joystick")}, {axes} axes, {buttons} buttons, {hats} hats)");

        UpdateSnapshot();
        return openedName;
    }

    /// <summary>SDL thread only.</summary>
    private static void CloseDevice()
    {
        if (s_gamepad != IntPtr.Zero)
        {
            SDL_CloseGamepad(s_gamepad);
            s_gamepad = IntPtr.Zero;
        }

        if (s_joystick != IntPtr.Zero)
        {
            SDL_CloseJoystick(s_joystick);
            s_joystick = IntPtr.Zero;
        }

        lock (Lock)
        {
            s_openDeviceName = null;
            s_connected = false;
            s_state = default;
            s_xSupported = s_ySupported = s_zSupported = false;
            s_rxSupported = s_rySupported = s_rzSupported = false;
            s_slider1Supported = s_slider2Supported = false;
        }
    }

    #endregion

    #region Game thread (IVRageInput2 backing)

    internal static List<string> EnumerateJoystickNames()
    {
        lock (Lock)
            return new List<string>(s_deviceNames);
    }

    internal static string InitializeJoystickIfPossible(string joystickInstanceName)
    {
        if (!s_subsystemReady)
            return null;

        // Disabled / nothing selected. Mirror MyDirectInput: open no device.
        // If one is currently open (the user just switched the Options ->
        // Controller selection to "Disabled"), close it so its input stops
        // immediately. Never fall back to the first available device.
        if (joystickInstanceName == null)
        {
            lock (Lock)
            {
                if (s_openDeviceName == null)
                    return null;
            }

            SdlRenderThread.Invoke(CloseDevice);
            return null;
        }

        // A specific device is selected. Fast path without dispatching to the
        // SDL thread: MyVRageInput calls this every frame while disconnected
        // (ResetJoystickState -> SearchForJoystickNow). Only cross threads
        // when there is a matching device to open, or a device open under a
        // different name that must be replaced/closed.
        lock (Lock)
        {
            bool anyMatch = false;
            foreach (var name in s_deviceNames)
            {
                if (name.Contains(joystickInstanceName))
                {
                    anyMatch = true;
                    break;
                }
            }

            if (!anyMatch && s_openDeviceName == null)
                return null;
        }

        return SdlRenderThread.Invoke(() => OpenDevice(joystickInstanceName));
    }

    internal static bool IsJoystickAxisSupported(MyJoystickAxesEnum axis)
    {
        lock (Lock)
        {
            switch (axis)
            {
                case MyJoystickAxesEnum.Xpos:
                case MyJoystickAxesEnum.Xneg:
                    return s_xSupported;
                case MyJoystickAxesEnum.Ypos:
                case MyJoystickAxesEnum.Yneg:
                    return s_ySupported;
                case MyJoystickAxesEnum.Zpos:
                case MyJoystickAxesEnum.Zneg:
                case MyJoystickAxesEnum.ZLeft:
                case MyJoystickAxesEnum.ZRight:
                    return s_zSupported;
                case MyJoystickAxesEnum.RotationXpos:
                case MyJoystickAxesEnum.RotationXneg:
                    return s_rxSupported;
                case MyJoystickAxesEnum.RotationYpos:
                case MyJoystickAxesEnum.RotationYneg:
                    return s_rySupported;
                case MyJoystickAxesEnum.RotationZpos:
                case MyJoystickAxesEnum.RotationZneg:
                    return s_rzSupported;
                case MyJoystickAxesEnum.Slider1pos:
                case MyJoystickAxesEnum.Slider1neg:
                    return s_slider1Supported;
                case MyJoystickAxesEnum.Slider2pos:
                case MyJoystickAxesEnum.Slider2neg:
                    return s_slider2Supported;
                default:
                    return false;
            }
        }
    }

    internal static bool IsJoystickConnected()
    {
        lock (Lock)
            return s_connected;
    }

    internal static void GetJoystickState(ref MyJoystickState state)
    {
        lock (Lock)
            state = s_state;
    }

    #endregion

    private static string GetErrorString()
    {
        IntPtr error = SDL_GetError();
        if (error == IntPtr.Zero)
            return "Unknown SDL3 error";
        return Marshal.PtrToStringUTF8(error) ?? "Unknown SDL3 error";
    }

    #region SDL3 P/Invoke (joystick / gamepad)

    [DllImport(Lib, EntryPoint = "SDL_InitSubSystem")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_InitSubSystem(uint flags);

    [DllImport(Lib, EntryPoint = "SDL_SetHint", CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetHint(string name, string value);

    [DllImport(Lib, EntryPoint = "SDL_GetError")]
    private static extern IntPtr SDL_GetError();

    [DllImport(Lib, EntryPoint = "SDL_free")]
    private static extern void SDL_free(IntPtr mem);

    [DllImport(Lib, EntryPoint = "SDL_GetJoysticks")]
    private static extern IntPtr SDL_GetJoysticks(out int count);

    [DllImport(Lib, EntryPoint = "SDL_GetJoystickNameForID")]
    private static extern IntPtr SDL_GetJoystickNameForID(uint instanceId);

    [DllImport(Lib, EntryPoint = "SDL_OpenJoystick")]
    private static extern IntPtr SDL_OpenJoystick(uint instanceId);

    [DllImport(Lib, EntryPoint = "SDL_CloseJoystick")]
    private static extern void SDL_CloseJoystick(IntPtr joystick);

    [DllImport(Lib, EntryPoint = "SDL_JoystickConnected")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_JoystickConnected(IntPtr joystick);

    [DllImport(Lib, EntryPoint = "SDL_GetNumJoystickAxes")]
    private static extern int SDL_GetNumJoystickAxes(IntPtr joystick);

    [DllImport(Lib, EntryPoint = "SDL_GetNumJoystickButtons")]
    private static extern int SDL_GetNumJoystickButtons(IntPtr joystick);

    [DllImport(Lib, EntryPoint = "SDL_GetNumJoystickHats")]
    private static extern int SDL_GetNumJoystickHats(IntPtr joystick);

    [DllImport(Lib, EntryPoint = "SDL_GetJoystickAxis")]
    private static extern short SDL_GetJoystickAxis(IntPtr joystick, int axis);

    [DllImport(Lib, EntryPoint = "SDL_GetJoystickButton")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetJoystickButton(IntPtr joystick, int button);

    [DllImport(Lib, EntryPoint = "SDL_GetJoystickHat")]
    private static extern byte SDL_GetJoystickHat(IntPtr joystick, int hat);

    [DllImport(Lib, EntryPoint = "SDL_IsGamepad")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_IsGamepad(uint instanceId);

    [DllImport(Lib, EntryPoint = "SDL_OpenGamepad")]
    private static extern IntPtr SDL_OpenGamepad(uint instanceId);

    [DllImport(Lib, EntryPoint = "SDL_CloseGamepad")]
    private static extern void SDL_CloseGamepad(IntPtr gamepad);

    [DllImport(Lib, EntryPoint = "SDL_GetGamepadAxis")]
    private static extern short SDL_GetGamepadAxis(IntPtr gamepad, int axis);

    [DllImport(Lib, EntryPoint = "SDL_GetGamepadButton")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetGamepadButton(IntPtr gamepad, int button);

    #endregion
}

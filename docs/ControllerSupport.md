# Controller Support on Linux

Plan for adding analog controller (gamepad / joystick / HOTAS) support to Space Engineers
running natively on Linux via Pulsar for Linux, as part of the LinuxCompat plugin.

## Goal

A player plugs in an Xbox-style gamepad or a joystick/HOTAS and the game's **built-in**
controller support works exactly like on Windows: the device shows up in the options,
the gamepad control scheme works, analog stick/trigger values drive ship movement,
rotation and character control.

## Research findings

### Reference plugin on Windows: Analog Grid Control

`ananace/dotnet-SEAnalogGridControlPlugin` (PluginHub: `AnalogGridControl.xml`) is the
plugin providing rich analog HOTAS/wheel support on Windows. Its architecture:

- **Device layer** (Windows-only): `SharpDX.DirectInput` — enumerates
  `DeviceClass.GameControl` devices, polls `JoystickState` (X/Y/Z, RotationX/Y/Z,
  2 sliders, 128 buttons, 4 POV hats), normalizes with per-axis device ranges.
- **Mapping layer** (portable): `Bind` objects map device axes/buttons/hats to game
  axes (strafe, pitch/yaw/roll, brake) and actions (fire, toolbar, lights, ...), with
  deadzone/curve/invert. Persisted as XML.
- **Injection layer** (portable): Harmony patches on `MyShipController.MoveAndRotate`
  and `MyMotorSuspension`; a session component fires actions.

Only its device layer is Windows-specific. However, we do **not** port this plugin
itself. The game has its own complete controller pipeline (official gamepad support)
that is platform-independent above a single small interface — implementing that
interface with SDL3 gives native controller support to every player without a second
plugin, and (as a later phase) a DirectInput shim can make AGC itself work unmodified.

### The game's native controller pipeline

```
SDL3 / evdev (Linux)                              ← missing piece (this plan)
  └─ IVRageInput2 (5 joystick methods)            ← the seam; implemented by
     MyDirectInput (SharpDX DirectInput+XInput)      VRage.Platform.Windows on Windows
       └─ MyVRageInput.UpdateStates()             ← platform-independent from here up
            └─ m_actualJoystickState : MyJoystickState
                 └─ MyControllerHelper (bindings, deadzone, curves)
                      └─ MyGuiScreenGamePlay.MoveAndRotatePlayerOrCamera()
                           └─ MyShipController.MoveAndRotate(move, rotate, roll)
```

`IVRageInput2` joystick surface to implement (`VRage/Input/IVRageInput2.cs`):

| Method | Windows behavior (`MyDirectInput`) to replicate |
|---|---|
| `List<string> EnumerateJoystickNames()` | Names of attached game controllers |
| `string InitializeJoystickIfPossible(string name)` | Opens first device whose name *contains* `name`; `null` opens the first device; returns actual name or `null`; retries with `null` if the requested name is not found |
| `bool IsJoystickAxisSupported(MyJoystickAxesEnum)` | Per-axis capability flags of the opened device |
| `bool IsJoystickConnected()` | Opened device still attached |
| `void GetJoystickState(ref MyJoystickState)` | Fills the state struct (see encoding below) |

`MyJoystickState` value encoding (must match exactly — the deadzone/curve math in
`MyVRageInput`/`MyControllerHelper` depends on it):

| Field | Encoding |
|---|---|
| `X, Y, Z, RotationX/Y/Z, Sliders[2]` | `0..65535`, center `32767` (MyDirectInput forces `InputRange(0, 65535)` on all DirectInput axes) |
| `Buttons[128]` | byte per button; pressed test is `> 0` (use `0x80` like DirectInput) |
| `PointOfViewControllers[4]` | hundredths of degrees clockwise from north (`0..35999`), `-1` when centered |
| `Z_Left, Z_Right` | analog triggers: XInput `0..255` value `* 256` → `0..65280` |

Init flow in `MyVRageInput` (all private, platform-independent):
`LoadContent()` → `InitializeJoystickIfPossible()` → `Input2.EnumerateJoystickNames()`
+ `Input2.InitializeJoystickIfPossible(m_joystickInstanceName)` → `SetConnectedJoystick`.
Reconnection: `UpdateStates()` polls `Input2.IsJoystickConnected()` and periodically
calls `SearchForJoystickNow()`, so hotplug works for free once enumeration works.
`m_joystickInstanceName` comes from the game config (`JoystickInstanceName`).

### Current LinuxCompat state

- `SdlGameWindow` already implements `IVRageInput2` and is wired in via
  `MyVRagePlatformInput2Patch` (prefix on `MyVRagePlatform.get_Input2`). The five
  joystick methods are stubs returning empty/false/no-op (`SdlGameWindow.cs:769-773`).
- `MyVRageInputLoadContentPatch` replaces `LoadContent` and currently **skips** the
  original joystick initialization — it must additionally invoke the private
  `MyVRageInput.InitializeJoystickIfPossible()` after setting up the keyboard state.
- `MyVRageInputInitializeJoystickPatch` / `MyVRageInputSearchForJoystickPatch` already
  pass through to the original methods when `Input2` is present — no change needed.
- SDL3 is loaded process-wide by the Headless/Pulsar `NativeLibraryPreloader`
  (`DllImport("libSDL3.so")` binds to the already-loaded handle). Only
  `SDL_INIT_VIDEO` and `SDL_INIT_AUDIO` are initialized today; the event loop lives on
  the dedicated SDL thread (`SdlRenderThread.Run`).
- The Remote plugin already exposes `/v1/input/joystick/axis|button` endpoints that
  inject `MyJoystickState` directly into `MyVRageInput` — useful to verify the
  game-side pipeline in isolation from the device layer.

## Design

### New component: `ClientPlugin/Compatibility/SdlJoystick.cs`

A static SDL3 joystick/gamepad backend following the existing per-file P/Invoke style
(`private const string Lib = "libSDL3.so"`).

**Initialization & threading.** `SdlRenderThread.Run` additionally initializes
`SDL_INIT_JOYSTICK | SDL_INIT_GAMEPAD` after `SDL_INIT_VIDEO`. All SDL joystick calls
happen on the SDL thread. Like the existing mouse handling, the SDL thread refreshes a
**state snapshot** (axes/buttons/hats plus the device list) every loop iteration under
a lock; the game thread's `IVRageInput2` methods only read the snapshot, except
`InitializeJoystickIfPossible`, which dispatches the open/close to the SDL thread via
the existing `SdlRenderThread.Invoke`. Device add/remove arrives as
`SDL_EVENT_JOYSTICK_ADDED/REMOVED` in the existing event handler.

**Device model.** One "active" device at a time (matches `MyDirectInput`). Two paths:

1. **Gamepad path** — `SDL_IsGamepad(id)` true (Xbox/PS/etc. with a known mapping).
   Read via the SDL_Gamepad API and emit the same layout XInput-over-DirectInput
   produces on Windows:

   | MyJoystickState | SDL_Gamepad source |
   |---|---|
   | `X`, `Y` | `LEFTX`, `LEFTY` (`-32768..32767` → `0..65535`) |
   | `RotationX`, `RotationY` | `RIGHTX`, `RIGHTY` |
   | `Z` | combined triggers like DirectInput: `32767 + (LT − RT)/2` |
   | `Z_Left`, `Z_Right` | `LEFT/RIGHT_TRIGGER` (`0..32767` → `0..65280`) |
   | `Buttons[0..9]` | XInput order: A, B, X, Y, LB, RB, Back, Start, LS, RS |
   | `PointOfViewControllers[0]` | D-pad buttons → angle (up=0, right=9000, ...) |

2. **Generic joystick path** — HOTAS/wheels without a gamepad mapping. Raw SDL_Joystick
   API: axes in device order → `X, Y, Z, RotationX, RotationY, RotationZ, Sliders[0..1]`;
   buttons as-is (up to 128); `SDL_GetJoystickHat(0..3)` → POV angles.

Axis-supported flags derive from `SDL_GetNumJoystickAxes` / gamepad-ness.

### `SdlGameWindow` changes

Replace the five stubs with delegation to `SdlJoystick`. Name matching, `null`
fallback and return-value semantics copied from `MyDirectInput.InitializeJoystickIfPossible`.

### Patch changes

`MyVRageInputLoadContentPatch`: after installing the keyboard state, reflectively call
the private `MyVRageInput.InitializeJoystickIfPossible()` so the joystick configured in
the game config is opened at startup (or the first device by default).

### Rendering-disabled mode

When `RenderingConfig.AllowRendering` is false there is no SDL thread and therefore no
controller support. Acceptable: that mode exists for headless/CI scenarios. Note it in
the log once.

### Phase 2 (future, not in this change): DirectInput shim for AGC

Reimplement the small `SharpDX.DirectInput` surface AGC uses (`DirectInput`,
`GetDevices`, `Joystick`, `JoystickState`, `JoystickOffset`, ranges) backed by
`SdlJoystick`, following the `XAudio2Shim` + Preloader assembly-redirect precedent.
That would let Analog Grid Control (and other DirectInput plugins) run unmodified for
full HOTAS bind management. Out of scope for the first iteration.

## Test plan: simulated controller input

### Level 0 — game-side pipeline only (already available)

The Remote plugin's `/v1/input/joystick/axis|button` endpoints inject
`MyJoystickState` directly. Use to confirm bindings/response independent of the SDL
backend, and to disambiguate failures (device layer vs game layer).

### Level 1 — virtual kernel device (primary, end-to-end)

Create a **virtual Xbox 360 gamepad** with Python `evdev`'s `UInput` (vendor
`0x045e`, product `0x028e`, axes `ABS_X/Y/RX/RY/Z/RZ`, hat `ABS_HAT0X/Y`, buttons
`BTN_SOUTH/EAST/NORTH/WEST/TL/TR/SELECT/START/MODE/THUMBL/THUMBR`). The kernel exposes
it as `/dev/input/event*`; SDL3 picks it up through udev exactly like real hardware,
and its built-in mapping DB recognizes it as a gamepad. `/dev/uinput` is already
user-writable on this machine. This exercises the **entire stack**: udev enumeration →
SDL3 → SdlJoystick → IVRageInput2 → MyVRageInput → bindings → ship movement.

Fallback if the created event node is not readable by the game process (logind
`uaccess` ACL should grant it on a desktop seat; verify early): an SDL *virtual
joystick* (`SDL_AttachVirtualJoystick`) test hook inside SdlJoystick, enabled by an
environment variable and fed from a small local file/pipe.

### Level 2 — automated in-game test script

`tests/controller_test.py` (uv script; deps: `evdev`, plus `se_remote.py` from the
se-remote skill for the Remote API):

1. Start the virtual gamepad (context manager; also test creating it *after* game
   start for hotplug coverage).
2. Start the game via the Headless `Interim` launcher (`StartGame.sh` /
   `RunGameInWeston.sh`), wait for the Remote API.
3. **Detection check**: assert the LinuxCompat log (`[LinuxCompat] Joystick added: ...`)
   and/or `MyInput.Static` sees the device (game log lists joystick names).
4. Load a test world with a ship, seat the character in the cockpit (Remote API UI
   automation / an existing test world from `~/dev/se1/TestWorlds`).
5. **Analog axis check**: push virtual left stick forward (thrust), hold a few
   seconds; assert grid velocity/position changed via the Remote API; release; verify
   inertia dampers stop the ship. Repeat for yaw (right stick X) — assert orientation
   change; and a trigger axis.
6. **Button check**: press A (e.g. bound action) and verify the mapped effect.
7. Analog gradation: feed 25% / 50% / 100% stick deflection and assert monotonically
   increasing acceleration (true analog input, not digitized).
8. Stop the game (`StopGame.sh`), clean up the virtual device.

The script must be re-runnable standalone (no model tokens needed) and print a clear
PASS/FAIL per check.

## Implementation steps

1. `SdlJoystick.cs`: P/Invoke declarations, subsystem init, event handling, snapshot,
   gamepad + generic mapping, `MyJoystickState` fill.
2. `SdlRenderThread`: init joystick+gamepad subsystems, forward joystick events, call
   `SdlJoystick.UpdateSnapshot()` per loop tick.
3. `SdlGameWindow`: implement the five `IVRageInput2` joystick methods.
4. `MyVRageInputLoadContentPatch`: trigger joystick init after keyboard setup.
5. Build; sanity-run the game; check device enumeration in log with a virtual pad.
6. Write `tests/controller_test.py`; iterate until all checks pass.

## Risks / notes

- **Event node permissions**: virtual device nodes get the seat user's ACL via logind
  `uaccess` on desktop sessions; verify early, use the SDL virtual joystick fallback
  otherwise.
- **SDL thread affinity**: all `SDL_Joystick*`/`SDL_Gamepad*` calls stay on the SDL
  thread; the game only reads snapshots. Violations are intermittent crashes — easy to
  avoid up front.
- **Axis conventions**: SDL Y axes are negative-up like DirectInput's raw values after
  the 0..65535 range mapping; verify sign against Windows behavior in-game (invert
  flags exist in the game options if needed, but defaults must match Windows).
- **Multiple devices**: `MyDirectInput` supports a single active device selected by
  name in the options; we mirror that. AGC-style multi-device aggregation is Phase 2.
- **Steam Input**: if the game is launched through Steam with Steam Input enabled,
  Steam may already translate the pad; irrelevant for the Headless test launcher but
  worth a note in the release notes.

#!/usr/bin/env -S uv run --script
# /// script
# requires-python = ">=3.11"
# dependencies = [
#     "evdev>=1.7",
#     "httpx",
# ]
# ///
"""End-to-end test for LinuxCompat controller support.

Creates virtual gamepads via /dev/uinput (the kernel exposes them exactly
like real hardware, so SDL3 discovers them through udev), starts Space
Engineers via the Headless Interim launcher, and verifies that the game's
built-in controller pipeline honors the Options -> Controller selection and
reacts to simulated analog axis values and button presses.

The plugin now mirrors the Windows behavior (VRage.Platform.Windows
MyDirectInput) exactly: only the controller selected in Options -> Controller
is opened, and the "Disabled" entry (a null instance name) opens no device at
all. There is no plug-and-play "open the first device" fallback — that used to
make "Disabled" impossible and activated controllers the player never chose.

Two phases, each a full game launch:

  Phase 1 — DISABLED
    Config selection is null ("Disabled"). Two virtual pads are plugged in.
    Asserts the plugin opens NO device (no "SdlJoystick opened" line appears
    while the game runs), so no controller input can reach the game.

  Phase 2 — SELECTED
    Config selection is the active pad's SDL name ("Xbox 360 Controller").
    A differently-named decoy pad is also plugged in. Asserts:
      * detection   — the SELECTED pad is opened, and the decoy is NOT,
      * analog move — pushing the selected pad's left stick moves the player,
      * analog look — moving its right stick turns the camera,
      * button      — pressing Start opens an in-game menu screen.

The game only processes controller input while its window has focus (same as
on Windows); the test activates the window with wmctrl when available.

Re-runnable standalone:
    tests/controller_test.py [--mode both|disabled|selected] [--keep-running]

Requirements:
  - /dev/uinput writable by the current user
  - Headless Interim launcher (see the se-remote skill)
  - the Remote plugin enabled in the game (Pulsar profile)
  - LinuxCompat built from this dev folder (compiled by the launcher)
"""

from __future__ import annotations

import argparse
import math
import os
import re
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path

import evdev
from evdev import AbsInfo, UInput
from evdev import ecodes as e

SKILL_DIR = Path(os.environ.get("SE_REMOTE_SKILL_DIR", "~/.claude/skills/se-remote")).expanduser()
sys.path.insert(0, str(SKILL_DIR))

from se_remote import RemoteAPI  # noqa: E402

GAME_CONFIG_DIR = Path("~/.config/SpaceEngineers").expanduser()
GAME_CONFIG_FILE = GAME_CONFIG_DIR / "SpaceEngineers.cfg"

# The active pad uses the Xbox 360 pad VID/PID, so SDL's built-in mapping
# database recognizes it as a gamepad and reports it under this name (not the
# custom uinput name). This is the string written into the game's controller
# selection so the plugin opens exactly this device.
ACTIVE_PAD_UINPUT_NAME = "LinuxCompat Virtual Gamepad"
ACTIVE_PAD_SDL_NAME = "Xbox 360 Controller"

# A second, differently-named pad that must NEVER be opened while the active
# pad is selected. Uses a VID/PID that is not in SDL's gamepad database, so
# SDL keeps its verbatim uinput name — clearly distinct from the active pad.
DECOY_PAD_NAME = "LinuxCompat Decoy Pad"

AXIS_MAX = 32767
TRIGGER_MAX = 255


class Check:
    """Collects PASS/FAIL results and prints a summary."""

    def __init__(self) -> None:
        self.results: list[tuple[str, bool, str]] = []

    def record(self, name: str, ok: bool, detail: str = "") -> bool:
        self.results.append((name, ok, detail))
        print(f"  [{'PASS' if ok else 'FAIL'}] {name}" + (f" — {detail}" if detail else ""),
              flush=True)
        return ok

    def summary(self) -> bool:
        print("\n=== Summary ===")
        ok = True
        for name, passed, detail in self.results:
            print(f"  [{'PASS' if passed else 'FAIL'}] {name}" + (f" — {detail}" if detail else ""))
            ok = ok and passed
        print(f"\n{'ALL CHECKS PASSED' if ok else 'SOME CHECKS FAILED'}", flush=True)
        return ok


class VirtualGamepad:
    """Virtual Xbox-360-layout gamepad backed by /dev/uinput.

    With the Xbox 360 VID/PID (045e:028e) SDL treats it as a mapped gamepad
    and renames it to "Xbox 360 Controller"; with an unknown VID/PID SDL keeps
    the given uinput name and treats it as a generic joystick — used for the
    decoy so it is clearly distinguishable from the active pad.
    """

    def __init__(self, name: str, vendor: int, product: int) -> None:
        stick = AbsInfo(value=0, min=-AXIS_MAX - 1, max=AXIS_MAX, fuzz=16, flat=128, resolution=0)
        trigger = AbsInfo(value=0, min=0, max=TRIGGER_MAX, fuzz=0, flat=0, resolution=0)
        hat = AbsInfo(value=0, min=-1, max=1, fuzz=0, flat=0, resolution=0)
        events = {
            e.EV_ABS: [
                (e.ABS_X, stick),
                (e.ABS_Y, stick),
                (e.ABS_RX, stick),
                (e.ABS_RY, stick),
                (e.ABS_Z, trigger),
                (e.ABS_RZ, trigger),
                (e.ABS_HAT0X, hat),
                (e.ABS_HAT0Y, hat),
            ],
            e.EV_KEY: [
                e.BTN_SOUTH, e.BTN_EAST, e.BTN_NORTH, e.BTN_WEST,
                e.BTN_TL, e.BTN_TR, e.BTN_SELECT, e.BTN_START,
                e.BTN_MODE, e.BTN_THUMBL, e.BTN_THUMBR,
            ],
        }
        self.name = name
        self.ui = UInput(events, name=name, vendor=vendor, product=product,
                         version=0x0110, bustype=e.BUS_USB)

    def close(self) -> None:
        self.ui.close()

    def axis(self, code: int, value: int) -> None:
        self.ui.write(e.EV_ABS, code, value)
        self.ui.syn()

    def button(self, code: int, pressed: bool) -> None:
        self.ui.write(e.EV_KEY, code, 1 if pressed else 0)
        self.ui.syn()

    def neutral(self) -> None:
        for code in (e.ABS_X, e.ABS_Y, e.ABS_RX, e.ABS_RY,
                     e.ABS_HAT0X, e.ABS_HAT0Y, e.ABS_Z, e.ABS_RZ):
            self.ui.write(e.EV_ABS, code, 0)
        self.ui.syn()


def make_active_pad() -> VirtualGamepad:
    # Xbox 360 pad VID/PID -> SDL reports it as ACTIVE_PAD_SDL_NAME.
    return VirtualGamepad(ACTIVE_PAD_UINPUT_NAME, vendor=0x045E, product=0x028E)


def make_decoy_pad() -> VirtualGamepad:
    # Unknown VID/PID -> SDL keeps the verbatim name, distinct from the active
    # pad, so selecting the active pad must not open this one.
    return VirtualGamepad(DECOY_PAD_NAME, vendor=0x1209, product=0x0001)


def node_for(name: str) -> str | None:
    for path in evdev.list_devices():
        dev = evdev.InputDevice(path)
        found = dev.name == name
        dev.close()
        if found:
            return path
    return None


# --- Game controller selection (Options -> Controller) via the config -------
# The selection is persisted as the "joystickInstanceName" entry in the
# ControlsGeneral dictionary of SpaceEngineers.cfg (see MyVRageInput.Save/
# LoadControls). A missing <Value> means null == "Disabled".

# The game's XmlSerializer is picky: rewriting SpaceEngineers.cfg with a
# generic XML writer (ElementTree) drops the UTF-8 BOM and re-orders things
# enough that the game rejects the file and regenerates defaults — which also
# wipes the GDPR consent and re-shows the consent screen. So edit surgically,
# as text, touching ONLY the ControlsGeneral dictionary and preserving every
# other byte (BOM, declaration, namespaces, all other settings).

_CONTROLS_GENERAL_RE = re.compile(
    r'(<Key>ControlsGeneral</Key>.*?'
    r'<Value xsi:type="SerializableDictionaryOfStringString">\s*)'
    r'(<dictionary\s*/>|<dictionary>.*?</dictionary>)',
    re.DOTALL,
)

_JOYSTICK_NAME_RE = re.compile(
    r'<Key>joystickInstanceName</Key>\s*(?:<Value>(?P<val>.*?)</Value>)?',
    re.DOTALL,
)


def _read_config_text() -> tuple[bytes, str]:
    raw = GAME_CONFIG_FILE.read_bytes()
    bom = b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b""
    return bom, raw[len(bom):].decode("utf-8")


def set_joystick_selection(name: str | None) -> None:
    """Write the Options -> Controller selection into the game config.

    name=None selects the "Disabled" entry (an empty controls dictionary — the
    game reads a missing joystickInstanceName as a null instance name).
    """
    bom, text = _read_config_text()

    if name is None:
        new_dict = "<dictionary />"
    else:
        new_dict = (
            "<dictionary>\n"
            "            <item>\n"
            "              <Key>joystickInstanceName</Key>\n"
            f"              <Value>{name}</Value>\n"
            "            </item>\n"
            "          </dictionary>"
        )

    new_text, n = _CONTROLS_GENERAL_RE.subn(lambda m: m.group(1) + new_dict, text)
    if n != 1:
        raise RuntimeError(f"expected exactly one ControlsGeneral dictionary, matched {n}")
    GAME_CONFIG_FILE.write_bytes(bom + new_text.encode("utf-8"))


def read_joystick_selection() -> str | None:
    _, text = _read_config_text()
    m = _CONTROLS_GENERAL_RE.search(text)
    if not m:
        return None
    inner = m.group(2)
    jm = _JOYSTICK_NAME_RE.search(inner)
    if not jm:
        return None
    return jm.group("val")


def newest_console_log() -> Path | None:
    logs = sorted(GAME_CONFIG_DIR.glob("Console_*.log"), key=lambda p: p.stat().st_mtime)
    return logs[-1] if logs else None


def log_lines_since(since: float) -> list[str]:
    log = newest_console_log()
    if not log or log.stat().st_mtime < since - 5:
        return []
    return log.read_text(errors="replace").splitlines()


def wait_for_log_line(pattern: str, since: float, timeout: float = 60.0) -> str | None:
    """Poll the newest console log for a regex; return the matching line."""
    rx = re.compile(pattern)
    deadline = time.time() + timeout
    while time.time() < deadline:
        for line in log_lines_since(since):
            if rx.search(line):
                return line
        time.sleep(1)
    return None


def find_log_line(pattern: str, since: float) -> str | None:
    rx = re.compile(pattern)
    for line in log_lines_since(since):
        if rx.search(line):
            return line
    return None


# Always run the game headless (offscreen rendering, keeps the SDL thread and
# thus controller support) at a small windowed resolution so the test never
# takes over a desktop. -sources makes the launcher compile plugins from the
# configured dev folders.
GAME_ARGS = ["-skipintro", "-nosplash", "-sources", "--headless", "--resolution", "640x480"]
LAUNCHER = Path("~/.local/share/Headless/Interim").expanduser()


def start_game() -> None:
    subprocess.Popen(
        [str(LAUNCHER), *GAME_ARGS],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        start_new_session=True,
    )


def stop_game() -> None:
    subprocess.run([str(SKILL_DIR / "StopGame.sh")], check=False, cwd=SKILL_DIR)


def focus_game_window() -> None:
    """Give the game window input focus; the game (like on Windows) ignores
    controller input while unfocused."""
    if shutil.which("wmctrl"):
        subprocess.run(["wmctrl", "-a", "Space Engineers"], check=False)


def dist(a: list[float], b: list[float]) -> float:
    return math.sqrt(sum((x - y) ** 2 for x, y in zip(a, b)))


def angle_between(a: list[float], b: list[float]) -> float:
    dot = sum(x * y for x, y in zip(a, b))
    la = math.sqrt(sum(x * x for x in a))
    lb = math.sqrt(sum(x * x for x in b))
    dot = max(-1.0, min(1.0, dot / (la * lb)))
    return math.degrees(math.acos(dot))


def get_character(api: RemoteAPI) -> dict | None:
    try:
        return api._get("/v1/character")
    except Exception:
        return None


def wait_for_gameplay(api: RemoteAPI, timeout: float = 240.0) -> bool:
    """Wait until a session is active and the local character exists."""
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            state = api.get_state()
            if state.get("active") and not state.get("paused"):
                if get_character(api) is not None:
                    return True
        except Exception:
            pass
        time.sleep(2)
    return False


def enter_world(api: RemoteAPI) -> bool:
    """Get into a world with a character: continue last save or quick start."""
    state = api.get_state()
    if state.get("active"):
        if state.get("paused"):
            api.unpause()
        return wait_for_gameplay(api, 30)

    api.wait_for_screen("MyGuiScreenMainMenu", has_focus=True, max_wait=120)
    for control in ("ContinueGame", "QuickStart"):
        try:
            api.control_click(control)
        except Exception:
            continue
        if wait_for_gameplay(api, 240):
            return True
    return False


def wait_until_stationary(api: RemoteAPI, window: float = 3.0, threshold: float = 1.0,
                          timeout: float = 150.0) -> bool:
    """Wait until the character barely moves (e.g. the quick-start drop pod
    has landed), so position deltas measure input effects, not free fall."""
    deadline = time.time() + timeout
    prev = get_character(api)
    while time.time() < deadline and prev:
        time.sleep(window)
        cur = get_character(api)
        if not cur:
            return False
        moved = dist(prev["position"], cur["position"])
        if moved < threshold:
            return True
        print(f"  ... still moving ({moved:.1f} m / {window:.0f}s), waiting", flush=True)
        prev = cur
    return False


def ensure_standing(api: RemoteAPI) -> bool:
    """Leave any seat (quick-start spawns seated in the drop pod)."""
    char = get_character(api)
    if not char:
        return False
    if char.get("state") != "sitting":
        return True
    try:
        api._post("/v1/character/use")
    except Exception as ex:
        print(f"  leave-seat failed: {ex}", flush=True)
        return False
    time.sleep(2)
    char = get_character(api)
    return bool(char) and char.get("state") != "sitting"


def connect_api() -> RemoteAPI:
    api = RemoteAPI()
    api.wait_for_api(max_wait=180)
    print("  Remote API is up", flush=True)
    focus_game_window()
    return api


# --- Phase: DISABLED --------------------------------------------------------

def phase_disabled(check: Check, keep_running: bool) -> None:
    """With the selection set to Disabled and two pads plugged in, the plugin
    must open no device at all."""
    print("\n########## Phase 1: DISABLED ##########", flush=True)
    set_joystick_selection(None)
    check.record("config selection is Disabled (null)", read_joystick_selection() is None)

    active = make_active_pad()
    decoy = make_decoy_pad()
    api = None
    try:
        check.record("active pad node created", node_for(ACTIVE_PAD_UINPUT_NAME) is not None)
        check.record("decoy pad node created", node_for(DECOY_PAD_NAME) is not None)

        started_at = time.time()
        print("=== Starting the game (Disabled) ===", flush=True)
        start_game()
        api = connect_api()

        line = wait_for_log_line(r"\[LinuxCompat\] SdlJoystick initialised", started_at, 90)
        check.record("SdlJoystick subsystem initialised", line is not None,
                     line or "log line not found")

        # The game searches for a joystick every frame while none is connected
        # (UpdateStates -> ResetJoystickState -> SearchForJoystickNow), even at
        # the main menu. With the OLD bug the null selection opened the first
        # device within seconds; with the fix it must never open one.
        print("=== Watching for any device being opened (should be none) ===", flush=True)
        opened = wait_for_log_line(r"\[LinuxCompat\] SdlJoystick opened '", started_at, 45)
        check.record("no controller opened while Disabled", opened is None,
                     opened or "no device opened (correct)")

        # Enter a world and confirm that pushing a stick does NOT move the
        # character: with Disabled selected, no controller input is accepted.
        print("=== Entering a world ===", flush=True)
        if not check.record("gameplay session with character", enter_world(api)):
            return
        focus_game_window()
        print("=== Waiting for a stationary character (drop pod landing) ===", flush=True)
        check.record("character stationary", wait_until_stationary(api))
        check.record("character standing (left any seat)", ensure_standing(api))

        print("=== Pushing left stick while Disabled (must not move) ===", flush=True)
        active.neutral()
        time.sleep(1)
        a = get_character(api)
        time.sleep(3)
        b = get_character(api)
        baseline = dist(a["position"], b["position"]) if a and b else 999.0

        active.axis(e.ABS_Y, -AXIS_MAX)  # push forward
        time.sleep(3)
        active.neutral()
        c = get_character(api)
        moved = dist(b["position"], c["position"]) if b and c else 999.0
        # Allow only tiny idle drift; a real move (see the selected phase) is
        # several metres. Disabled must stay within the same noise as baseline.
        check.record("no movement from stick while Disabled", moved <= baseline + 1.0,
                     f"baseline {baseline:.2f} m, with stick {moved:.2f} m")
    finally:
        active.close()
        decoy.close()
        if api is not None:
            try:
                api.close()
            except Exception:
                pass
        if not keep_running:
            print("Stopping the game...", flush=True)
            stop_game()
            time.sleep(3)


# --- Phase: SELECTED --------------------------------------------------------

def phase_selected(check: Check, keep_running: bool) -> None:
    """With the active pad selected and a decoy also plugged in, only the
    selected pad is opened and it drives the game."""
    print("\n########## Phase 2: SELECTED ##########", flush=True)
    set_joystick_selection(ACTIVE_PAD_SDL_NAME)
    check.record(f"config selection is '{ACTIVE_PAD_SDL_NAME}'",
                 read_joystick_selection() == ACTIVE_PAD_SDL_NAME)

    active = make_active_pad()
    decoy = make_decoy_pad()
    api = None
    try:
        check.record("active pad node created", node_for(ACTIVE_PAD_UINPUT_NAME) is not None)
        check.record("decoy pad node created", node_for(DECOY_PAD_NAME) is not None)

        started_at = time.time()
        print("=== Starting the game (Selected) ===", flush=True)
        start_game()
        api = connect_api()

        line = wait_for_log_line(r"\[LinuxCompat\] SdlJoystick initialised", started_at, 90)
        check.record("SdlJoystick subsystem initialised", line is not None,
                     line or "log line not found")

        # --- Check: the SELECTED device is opened ---------------------------
        opened = wait_for_log_line(r"\[LinuxCompat\] SdlJoystick opened '", started_at, 60)
        check.record("selected controller opened",
                     opened is not None and ACTIVE_PAD_SDL_NAME in opened,
                     opened or "no device opened")

        # --- Check: the decoy is NEVER opened -------------------------------
        decoy_opened = find_log_line(
            r"\[LinuxCompat\] SdlJoystick opened '.*" + re.escape(DECOY_PAD_NAME), started_at)
        check.record("decoy controller not opened", decoy_opened is None,
                     decoy_opened or "decoy stayed closed (correct)")

        # --- Enter a world --------------------------------------------------
        print("=== Entering a world ===", flush=True)
        if not check.record("gameplay session with character", enter_world(api)):
            return
        focus_game_window()

        print("=== Waiting for a stationary character (drop pod landing) ===", flush=True)
        check.record("character stationary", wait_until_stationary(api))
        check.record("character standing (left any seat)", ensure_standing(api))

        # --- Check: analog movement (left stick) ----------------------------
        print("=== Analog movement (left stick) ===", flush=True)
        active.neutral()
        time.sleep(1)
        a = get_character(api)
        time.sleep(3)
        b = get_character(api)
        baseline = dist(a["position"], b["position"]) if a and b else 999.0

        active.axis(e.ABS_Y, -AXIS_MAX)  # push forward (up = negative Y)
        time.sleep(3)
        active.neutral()
        c = get_character(api)
        moved = dist(b["position"], c["position"]) if b and c else 0.0
        check.record("character moved on selected pad left stick", moved > baseline + 2.0,
                     f"baseline {baseline:.2f} m, with stick {moved:.2f} m")

        # --- Check: analog look (right stick) -------------------------------
        print("=== Analog look (right stick) ===", flush=True)
        time.sleep(1)
        a = get_character(api)
        time.sleep(2)
        b = get_character(api)
        drift = angle_between(a["forward"], b["forward"]) if a and b else 999.0

        active.axis(e.ABS_RX, AXIS_MAX)  # look right
        time.sleep(2)
        active.neutral()
        c = get_character(api)
        turned = angle_between(b["forward"], c["forward"]) if b and c else 0.0
        check.record("camera turned on selected pad right stick", turned > drift + 5.0,
                     f"baseline {drift:.1f} deg, with stick {turned:.1f} deg")

        # --- Check: button press (Start opens a menu) -----------------------
        print("=== Button press (Start) ===", flush=True)
        active.button(e.BTN_START, True)
        time.sleep(0.3)
        active.button(e.BTN_START, False)
        time.sleep(1.5)
        screens = api.list_screens()
        menu_open = any("Menu" in (s.get("type") or "") for s in screens)
        check.record("Start button opened a menu screen", menu_open,
                     ", ".join(s.get("type", "?") for s in screens))
        if menu_open:
            api.key("Escape")
            time.sleep(1)
    finally:
        active.close()
        decoy.close()
        if api is not None:
            try:
                api.close()
            except Exception:
                pass
        if not keep_running:
            print("Stopping the game...", flush=True)
            stop_game()
            time.sleep(3)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mode", choices=("both", "disabled", "selected"), default="both",
                        help="which phase(s) to run (default: both)")
    parser.add_argument("--keep-running", action="store_true",
                        help="leave the game running after the last phase")
    args = parser.parse_args()

    check = Check()

    print("=== Preflight ===", flush=True)
    try:
        fd = os.open("/dev/uinput", os.O_WRONLY | os.O_NONBLOCK)
        os.close(fd)
        check.record("uinput writable", True)
    except OSError as ex:
        check.record("uinput writable", False, str(ex))
        check.summary()
        return 1

    if not GAME_CONFIG_FILE.exists():
        check.record("game config exists", False, str(GAME_CONFIG_FILE))
        check.summary()
        return 1

    original_selection = read_joystick_selection()
    print(f"  Saved current controller selection: {original_selection!r}", flush=True)

    try:
        if args.mode in ("both", "disabled"):
            keep = args.keep_running and args.mode == "disabled"
            phase_disabled(check, keep_running=keep)
        if args.mode in ("both", "selected"):
            keep = args.keep_running and args.mode in ("selected", "both")
            phase_selected(check, keep_running=keep)
    finally:
        # Restore the user's original controller selection.
        try:
            set_joystick_selection(original_selection)
            print(f"  Restored controller selection: {original_selection!r}", flush=True)
        except Exception as ex:
            print(f"  WARNING: failed to restore controller selection: {ex}", flush=True)

    return 0 if check.summary() else 1


if __name__ == "__main__":
    sys.exit(main())

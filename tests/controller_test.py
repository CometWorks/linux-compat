#!/usr/bin/env -S uv run --script
# /// script
# requires-python = ">=3.11"
# dependencies = [
#     "evdev>=1.7",
#     "httpx",
# ]
# ///
"""End-to-end test for LinuxCompat controller support.

Creates a virtual Xbox-360-class gamepad via /dev/uinput (the kernel exposes
it exactly like real hardware, so SDL3 discovers it through udev), starts
Space Engineers via the Headless Interim launcher, and verifies that the
game's built-in controller pipeline reacts to simulated analog axis values
and button presses:

  1. detection   — LinuxCompat's SdlJoystick opens the virtual device
                   (asserted from the game's console log; no configuration
                   needed, the plugin auto-opens the first device),
  2. analog move — pushing the virtual left stick moves the player character
                   (asserted from /v1/character position via the Remote API,
                   against a neutral-stick baseline),
  3. analog look — moving the virtual right stick turns the camera
                   (asserted from the character's forward vector),
  4. button      — pressing Start opens an in-game menu screen.

The game only processes controller input while its window has focus (same
as on Windows); the test activates the window with wmctrl when available.

Re-runnable standalone:  tests/controller_test.py [--keep-running] [--no-start]

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
from pathlib import Path

import evdev
from evdev import AbsInfo, UInput
from evdev import ecodes as e

SKILL_DIR = Path(os.environ.get("SE_REMOTE_SKILL_DIR", "~/.claude/skills/se-remote")).expanduser()
sys.path.insert(0, str(SKILL_DIR))

from se_remote import RemoteAPI  # noqa: E402

GAME_CONFIG_DIR = Path("~/.config/SpaceEngineers").expanduser()

# The evdev device name; SDL reports it as the joystick name and LinuxCompat
# logs it when the device is opened.
PAD_NAME = "LinuxCompat Virtual Gamepad"

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
    """Virtual Xbox-360-layout gamepad backed by /dev/uinput."""

    def __init__(self) -> None:
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
        # Xbox 360 pad VID/PID so SDL's built-in mapping database recognizes
        # the device as a gamepad regardless of the custom name.
        self.ui = UInput(events, name=PAD_NAME, vendor=0x045E, product=0x028E,
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


def newest_console_log() -> Path | None:
    logs = sorted(GAME_CONFIG_DIR.glob("Console_*.log"), key=lambda p: p.stat().st_mtime)
    return logs[-1] if logs else None


def wait_for_log_line(pattern: str, since: float, timeout: float = 60.0) -> str | None:
    """Poll the newest console log for a regex; return the matching line."""
    rx = re.compile(pattern)
    deadline = time.time() + timeout
    while time.time() < deadline:
        log = newest_console_log()
        if log and log.stat().st_mtime >= since - 5:
            for line in log.read_text(errors="replace").splitlines():
                if rx.search(line):
                    return line
        time.sleep(1)
    return None


# Always run the game headless (offscreen rendering, keeps the SDL thread
# and thus controller support) at a small windowed resolution so the test
# never takes over a desktop. -sources makes the launcher compile plugins
# from the configured dev folders.
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


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--no-start", action="store_true",
                        help="assume the game is already running")
    parser.add_argument("--keep-running", action="store_true",
                        help="leave the game running after the test")
    args = parser.parse_args()

    check = Check()
    api = None

    print("=== Preflight ===", flush=True)
    try:
        fd = os.open("/dev/uinput", os.O_WRONLY | os.O_NONBLOCK)
        os.close(fd)
        check.record("uinput writable", True)
    except OSError as ex:
        check.record("uinput writable", False, str(ex))
        check.summary()
        return 1

    print("=== Virtual gamepad ===", flush=True)
    pad = VirtualGamepad()
    try:
        node = None
        for path in evdev.list_devices():
            dev = evdev.InputDevice(path)
            if dev.name == PAD_NAME:
                node = path
            dev.close()
        check.record("virtual device node created", node is not None, node or "not found")

        started_at = time.time()
        if not args.no_start:
            print("=== Starting the game ===", flush=True)
            start_game()

        api = RemoteAPI()
        api.wait_for_api(max_wait=180)
        print("  Remote API is up", flush=True)
        focus_game_window()

        # --- Check 1: device detection and opening by the SDL backend -------
        print("=== Detection ===", flush=True)
        line = wait_for_log_line(r"\[LinuxCompat\] SdlJoystick initialised", started_at, 60)
        check.record("SdlJoystick subsystem initialised", line is not None,
                     line or "log line not found")

        # --- Enter a world ----------------------------------------------------
        print("=== Entering a world ===", flush=True)
        if not check.record("gameplay session with character", enter_world(api)):
            check.summary()
            return 1
        focus_game_window()

        # The device is only opened once the game processes input (the game
        # searches for a joystick every frame while none is connected). SDL
        # reports the virtual pad under its gamepad-mapping name ("Xbox 360
        # Controller"), not the custom uinput name, so match any device.
        line = wait_for_log_line(
            r"\[LinuxCompat\] SdlJoystick opened '", started_at, 60)
        check.record("virtual gamepad opened", line is not None, line or "log line not found")

        print("=== Waiting for a stationary character (drop pod landing) ===", flush=True)
        stationary = wait_until_stationary(api)
        check.record("character stationary", stationary)

        check.record("character standing (left any seat)", ensure_standing(api))

        # --- Check 2: analog movement (left stick) ---------------------------
        print("=== Analog movement (left stick) ===", flush=True)
        pad.neutral()
        time.sleep(1)
        a = get_character(api)
        time.sleep(3)
        b = get_character(api)
        baseline = dist(a["position"], b["position"]) if a and b else 999.0

        pad.axis(e.ABS_Y, -AXIS_MAX)  # push forward (up = negative Y)
        time.sleep(3)
        pad.neutral()
        c = get_character(api)
        moved = dist(b["position"], c["position"]) if b and c else 0.0
        check.record("character moved on left stick", moved > baseline + 2.0,
                     f"baseline {baseline:.2f} m, with stick {moved:.2f} m")

        # --- Check 3: analog look (right stick) ------------------------------
        print("=== Analog look (right stick) ===", flush=True)
        time.sleep(1)
        a = get_character(api)
        time.sleep(2)
        b = get_character(api)
        drift = angle_between(a["forward"], b["forward"]) if a and b else 999.0

        pad.axis(e.ABS_RX, AXIS_MAX)  # look right
        time.sleep(2)
        pad.neutral()
        c = get_character(api)
        turned = angle_between(b["forward"], c["forward"]) if b and c else 0.0
        check.record("camera turned on right stick", turned > drift + 5.0,
                     f"baseline {drift:.1f} deg, with stick {turned:.1f} deg")

        # --- Check 4: button press (Start opens a menu) ----------------------
        print("=== Button press (Start) ===", flush=True)
        pad.button(e.BTN_START, True)
        time.sleep(0.3)
        pad.button(e.BTN_START, False)
        time.sleep(1.5)
        screens = api.list_screens()
        menu_open = any("Menu" in (s.get("type") or "") for s in screens)
        check.record("Start button opened a menu screen", menu_open,
                     ", ".join(s.get("type", "?") for s in screens))
        if menu_open:
            api.key("Escape")
            time.sleep(1)

        ok = check.summary()

        if not ok:
            print("\nDiagnostic hint: verify the game-side pipeline independently with")
            print("the Remote joystick injection (bypasses the SDL device layer):")
            print("  api.joystick_axis('Y', 0, hold_frames=60)")

        if not args.keep_running and not args.no_start:
            print("Stopping the game...", flush=True)
            stop_game()

        return 0 if ok else 1
    finally:
        pad.close()
        if api is not None:
            try:
                api.close()
            except Exception:
                pass


if __name__ == "__main__":
    sys.exit(main())

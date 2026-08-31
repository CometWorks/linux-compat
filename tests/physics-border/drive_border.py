#!/usr/bin/env python3
"""Drive one bounded-world border-cleanup run.

Usage: drive_border.py <world-path> <game-log-dir> [timeout-seconds]

Waits for the Remote API, loads the bounded test world, then polls the game
log until the SE-side border cleanup line
"HavokWorld_EntityLeftWorld removed entity" mentioning BorderDriftShip
appears (the drifting grid crosses the broad-phase border a few seconds into
the session). Exits the game gracefully afterwards so the session-unload
path lands in the same log. Exit 0 = cleanup line seen, 1 = timeout.
Run via: uv run --project <se-remote dir> drive_border.py ...
"""

import os
import sys
import time
from pathlib import Path

sys.path.insert(0, os.environ.get("SE_REMOTE_DIR", os.getcwd()))

from se_remote import RemoteAPI  # noqa: E402

REMOVED_MARKER = "HavokWorld_EntityLeftWorld removed entity"
GRID_NAME = "BorderDriftShip"


def newest_game_log(log_dir: Path, not_before: float) -> Path | None:
    logs = [p for p in log_dir.glob("SpaceEngineers*.log") if p.stat().st_mtime >= not_before]
    return max(logs, key=lambda p: p.stat().st_mtime) if logs else None


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__, file=sys.stderr)
        return 2
    world_path = sys.argv[1]
    log_dir = Path(sys.argv[2])
    timeout = float(sys.argv[3]) if len(sys.argv) > 3 else 300.0

    start_time = time.time()
    deadline = time.monotonic() + timeout
    found = False

    with RemoteAPI(
        "http://127.0.0.1:24158", username="admin", password="SpaceEngineers"
    ) as api:
        print("waiting for the Remote API ...", flush=True)
        api.wait_for_api(max_wait=180.0)
        print("API up; requesting world load:", world_path, flush=True)
        api.load(world_path)

        print("waiting for an active session ...", flush=True)
        while time.monotonic() < deadline:
            try:
                if api.get_state().get("active"):
                    break
            except Exception:
                pass
            time.sleep(2.0)
        else:
            print("TIMEOUT waiting for the session", flush=True)
            return 1
        print("session active; waiting for the border cleanup log line ...", flush=True)

        while time.monotonic() < deadline:
            log = newest_game_log(log_dir, start_time - 60)
            if log is not None:
                text = log.read_text(encoding="utf-8", errors="replace")
                if REMOVED_MARKER in text and GRID_NAME in text:
                    line = next(l for l in text.splitlines() if REMOVED_MARKER in l)
                    print("cleanup confirmed:", line.strip(), flush=True)
                    found = True
                    break
            time.sleep(2.0)
        if not found:
            print(f"TIMEOUT: no '{REMOVED_MARKER}' line for {GRID_NAME}", flush=True)

        # Graceful exit either way: the session-unload ordering belongs in the
        # same log (it is a candidate for the settings-less CreateHkWorld path).
        print("requesting graceful game exit ...", flush=True)
        try:
            api.exit_game()
        except Exception as e:
            print("exit request failed (game may already be closing):", e, flush=True)

    for _ in range(60):
        if os.system("pgrep -x Interim.bin > /dev/null") != 0:
            break
        time.sleep(2.0)

    return 0 if found else 1


if __name__ == "__main__":
    sys.exit(main())

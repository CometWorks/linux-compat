#!/usr/bin/env python3
"""Drive the game client through one diagnostics run.

Usage: drive_client.py <world-path> <suite-log-path> [timeout-seconds]

Waits for the Remote API, loads the given world, then polls the suite log
until its END terminator appears. Exits 0 on a terminated (complete) log,
non-zero otherwise. Run via: uv run --project <se-remote dir> drive_client.py ...
"""

import os
import sys
import time

# se_remote.py lives in the se-remote skill folder; the harness passes it on
# sys.path via PYTHONPATH or --project cwd.
sys.path.insert(0, os.environ.get("SE_REMOTE_DIR", os.getcwd()))

from se_remote import RemoteAPI  # noqa: E402

END_MARKER = "=== END LinuxCompatDiagnostics ==="


def log_has_end(path: str) -> bool:
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            return END_MARKER in f.read()
    except OSError:
        return False


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__, file=sys.stderr)
        return 2
    world_path = sys.argv[1]
    suite_log = sys.argv[2]
    timeout = float(sys.argv[3]) if len(sys.argv) > 3 else 600.0

    deadline = time.monotonic() + timeout

    with RemoteAPI(
        "http://127.0.0.1:24158", username="admin", password="SpaceEngineers"
    ) as api:
        print("waiting for the Remote API ...", flush=True)
        api.wait_for_api(max_wait=180.0)
        print("API up; requesting world load:", world_path, flush=True)
        api.load(world_path)

        # World load recompiles the mod against the fresh plugin build, so
        # the first run after a cache clear takes a while. The API may drop
        # requests during the loading screen; tolerate errors while polling.
        print("waiting for an active session ...", flush=True)
        while time.monotonic() < deadline:
            try:
                if api.get_state().get("active"):
                    break
            except Exception:
                pass
            time.sleep(2.0)
        else:
            print("TIMEOUT: session never became active", file=sys.stderr)
            return 3
        print("session active; waiting for the suite log terminator ...", flush=True)

        while time.monotonic() < deadline:
            if log_has_end(suite_log):
                print("suite log terminated - run complete", flush=True)
                return 0
            time.sleep(2.0)

    if os.path.exists(suite_log):
        print("TIMEOUT: suite log exists but never terminated", file=sys.stderr)
    else:
        print("TIMEOUT: suite log was never written", file=sys.stderr)
    return 3


if __name__ == "__main__":
    sys.exit(main())

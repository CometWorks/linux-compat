#!/bin/bash
# Mod API boundary test suite for the linux-compat plugin (dedicated server).
#
# Runs the LinuxCompatDiagnostics suite on the local Magnetar DS offline:
# the mod is fake-Workshop-registered (the DS rejects local mods in
# multiplayer and cannot download items offline), a scratch world referencing
# it is generated, and the DS is launched with -noimplicitmod (the Magnetar
# companion mod is not available offline).
#
# Usage: tests/mod-api/run-server.sh [--skip-build]
# Exit codes: 0 all probes passed; 1 probe failures; 2 harness/run failure.
set -u

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"

INSTANCE="$HOME/.config/SpaceEngineersDedicated"
LAUNCHER="$HOME/.local/share/Magnetar/MagnetarInterim"
DS64="${DS64:-$HOME/.steam/steam/steamapps/common/SpaceEngineersDedicatedServer/DedicatedServer64}"
FAKE_ID=900000001
SUITE_LOG="$INSTANCE/Storage/$FAKE_ID.sbm_LinuxCompatDiagnostics/LinuxCompatDiagnostics.log"
SUITE_TIMEOUT="${SUITE_TIMEOUT:-600}"

SKIP_BUILD=0
for arg in "$@"; do
    case "$arg" in
        --skip-build) SKIP_BUILD=1 ;;
        *) echo "unknown option: $arg" >&2; exit 2 ;;
    esac
done

fail() {
    echo "HARNESS ERROR: $*" >&2
    exit 2
}

echo "== linux-compat mod API suite (dedicated server) =="
[ -x "$LAUNCHER" ] || fail "Magnetar Interim launcher not found at $LAUNCHER"
[ -d "$DS64" ] || fail "DS64 not found at $DS64"
pgrep -f "Bin/MagnetarInterim" >/dev/null && fail "a MagnetarInterim instance is already running"

# 1. Build (Magnetar recompiles dev-folder plugins itself, but a broken tree
#    should fail fast here rather than inside the DS).
if [ "$SKIP_BUILD" -eq 0 ]; then
    echo "== building LinuxCompat.sln (Debug) =="
    dotnet build "$REPO_DIR/LinuxCompat.sln" -c Debug || fail "plugin build failed"
fi

# 2. Fake-Workshop registration, scratch world, LoadWorld pointer.
echo "== preparing the DS instance =="
python3 "$SCRIPT_DIR/prepare_ds.py" "$SCRIPT_DIR/LinuxCompatDiagnostics" || fail "DS preparation failed"

# 3. Clear the compiled-mods cache (dev builds randomize the plugin assembly
#    identity) and the previous run's results.
rm -f "$INSTANCE/Performance/Cache/CompiledMods"/*.cache
rm -f "$SUITE_LOG"

# 4. Launch the DS in the background.
echo "== starting the dedicated server =="
DS_OUT="$(mktemp /tmp/linuxcompat-ds-run.XXXXXX.log)"
SE_LINUX_COMPAT_TRACE_INGRESS="${SE_LINUX_COMPAT_TRACE_INGRESS:-0}" \
"$LAUNCHER" -config "$HOME/.config/Magnetar" -ds64 "$DS64" -path "$INSTANCE" \
    -noimplicitmod >"$DS_OUT" 2>&1 &
DS_PID=$!

stop_ds() {
    kill "$DS_PID" >/dev/null 2>&1
    for _ in $(seq 1 15); do
        kill -0 "$DS_PID" >/dev/null 2>&1 || return 0
        sleep 1
    done
    kill -9 "$DS_PID" >/dev/null 2>&1
}
trap stop_ds EXIT

# 5. Wait for the suite log terminator (or DS death).
echo "== waiting for the suite (timeout ${SUITE_TIMEOUT}s) =="
DEADLINE=$((SECONDS + SUITE_TIMEOUT))
RESULT=timeout
while [ "$SECONDS" -lt "$DEADLINE" ]; do
    if [ -f "$SUITE_LOG" ] && grep -q "=== END LinuxCompatDiagnostics ===" "$SUITE_LOG"; then
        RESULT=done
        break
    fi
    if ! kill -0 "$DS_PID" >/dev/null 2>&1; then
        RESULT=died
        break
    fi
    sleep 3
done

stop_ds
trap - EXIT

DS_LOG="$(ls -t "$INSTANCE"/SpaceEngineersDedicated_*.log 2>/dev/null | head -1)"
if [ "$RESULT" != "done" ]; then
    echo "DS run $RESULT; launcher output: $DS_OUT" >&2
    [ -n "$DS_LOG" ] && { echo "--- last DS log lines ($DS_LOG):" >&2; tail -20 "$DS_LOG" >&2; }
    fail "suite did not complete on the dedicated server"
fi

# 6. Confirm the dev-folder plugin build was used.
if [ -n "$DS_LOG" ] && grep -qoE 'LinuxCompat(Server)?_[a-z0-9]+\.[a-z0-9]+' "$DS_LOG"; then
    echo "verified: dev-folder LinuxCompat assembly loaded ($(grep -oE 'LinuxCompat(Server)?_[a-z0-9]+\.[a-z0-9]+' "$DS_LOG" | head -1))"
else
    echo "WARNING: no randomized LinuxCompat assembly marker in the DS log" >&2
fi

# 7. Parse and report.
echo "== results (dedicated server) =="
python3 "$SCRIPT_DIR/parse_results.py" "$SUITE_LOG"

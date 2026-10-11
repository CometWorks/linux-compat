#!/bin/bash
# Mod API boundary test suite for the linux-compat plugin (dedicated server).
#
# Runs the LinuxCompatDiagnostics suite on an isolated Magnetar DS offline:
# its own Magnetar config folder, with this working tree registered as the
# linux-compat dev folder source, and its own DS data folder, both under
# MODAPI_DS_ROOT. The mod is fake-Workshop-registered (the DS rejects local
# mods in multiplayer and cannot download items offline), a scratch world
# referencing it is generated, and the DS is launched with -noimplicitmod (the
# Magnetar companion mod is not available offline).
#
# Usage: tests/mod-api/run-server.sh [--skip-build]
# Exit codes: 0 all probes passed; 1 probe failures; 2 harness/run failure.
#
# Environment:
#   MODAPI_DS_ROOT        config and data root (~/.se-test/linux-compat-mod-api)
#   MODAPI_DS_PORT        DS ServerPort (27150)
#   MODAPI_DS_STEAM_PORT  DS SteamPort (27151)
#   MAGNETAR_DIR          Magnetar install with MagnetarInterim.bin (~/.config/Magnetar)
#   DS64                  DedicatedServer64 of the DS install
#   TEMPLATE_INSTANCE     DS instance the config and world template are read from
#                         (~/.config/SpaceEngineersDedicated)
#   DOTNET_COMPAT_DIR     dotnet-compat checkout to compile instead of the hub build
set -u

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"

ROOT="${MODAPI_DS_ROOT:-$HOME/.se-test/linux-compat-mod-api}"
SERVER_PORT="${MODAPI_DS_PORT:-27150}"
STEAM_PORT="${MODAPI_DS_STEAM_PORT:-27151}"
CONFIG="$ROOT/magnetar"
INSTANCE="$ROOT/data"
LAUNCHER="${MAGNETAR_DIR:-$HOME/.config/Magnetar}/MagnetarInterim.bin"
DS64="${DS64:-$HOME/.steam/steam/steamapps/common/SpaceEngineersDedicatedServer/DedicatedServer64}"
FAKE_ID=900000001
SUITE_LOG="$INSTANCE/Storage/$FAKE_ID.sbm_LinuxCompatDiagnostics/LinuxCompatDiagnostics.log"
SUITE_TIMEOUT="${SUITE_TIMEOUT:-600}"
PID_FILE="$ROOT/server.pid"

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
echo "repo: $REPO_DIR"
echo "root: $ROOT (ports $SERVER_PORT/$STEAM_PORT)"
[ -x "$LAUNCHER" ] || fail "Magnetar Interim launcher not found at $LAUNCHER"
[ -d "$DS64" ] || fail "DS64 not found at $DS64"
if [ -f "$PID_FILE" ] && kill -0 "$(cat "$PID_FILE")" 2>/dev/null; then
    fail "a server of this root is still running (pid $(cat "$PID_FILE"))"
fi

# 1. Build (Magnetar recompiles dev-folder plugins itself, but a broken tree
#    should fail fast here rather than inside the DS).
if [ "$SKIP_BUILD" -eq 0 ]; then
    echo "== building LinuxCompat.sln (Debug) =="
    dotnet build "$REPO_DIR/LinuxCompat.sln" -c Debug || fail "plugin build failed"
fi

# 2. Magnetar config with the dev folder source, DS data folder, fake-Workshop
#    registration, scratch world.
echo "== preparing the DS instance =="
python3 "$SCRIPT_DIR/prepare_ds.py" "$SCRIPT_DIR/LinuxCompatDiagnostics" \
    "$ROOT" "$REPO_DIR" "$SERVER_PORT" "$STEAM_PORT" || fail "DS preparation failed"

# 3. Clear the compiled-mods cache (dev builds randomize the plugin assembly
#    identity) and the previous run's results.
rm -f "$INSTANCE/Performance/Cache/CompiledMods"/*.cache
rm -f "$SUITE_LOG"

# 4. Launch the DS in the background.
echo "== starting the dedicated server =="
DS_OUT="$ROOT/launcher.log"
SE_LINUX_COMPAT_TRACE_INGRESS="${SE_LINUX_COMPAT_TRACE_INGRESS:-0}" \
"$LAUNCHER" -multiInstance -stableLogs -consent deny -config "$CONFIG" \
    -ds64 "$DS64" -path "$INSTANCE" -noimplicitmod >"$DS_OUT" 2>&1 &
DS_PID=$!
echo "$DS_PID" >"$PID_FILE"

stop_ds() {
    kill "$DS_PID" >/dev/null 2>&1
    for _ in $(seq 1 15); do
        kill -0 "$DS_PID" >/dev/null 2>&1 || break
        sleep 1
    done
    kill -9 "$DS_PID" >/dev/null 2>&1
    rm -f "$PID_FILE"
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

DS_LOG="$INSTANCE/SpaceEngineersDedicated.log"
if [ "$RESULT" != "done" ]; then
    echo "DS run $RESULT; launcher output: $DS_OUT" >&2
    [ -f "$DS_LOG" ] && { echo "--- last DS log lines ($DS_LOG):" >&2; tail -20 "$DS_LOG" >&2; }
    fail "suite did not complete on the dedicated server"
fi

# 6. Confirm Magnetar compiled this working tree. Its info.log (a new one per
#    launch) names the folder a dev folder plugin is compiled from; a MagnetarHub
#    build of linux-compat is downloaded or taken from the cache instead, and
#    its randomized assembly name looks just like a dev build's.
COMPILED="Compiling files for LinuxCompat (linux-compat) from $REPO_DIR:"
if grep -qF "$COMPILED" "$CONFIG/info.log"; then
    echo "verified: LinuxCompat compiled from $REPO_DIR"
else
    fail "Magnetar did not compile LinuxCompat from $REPO_DIR (see $CONFIG/info.log); the run tested another build"
fi

# 7. Parse and report.
echo "== results (dedicated server) =="
python3 "$SCRIPT_DIR/parse_results.py" "$SUITE_LOG"

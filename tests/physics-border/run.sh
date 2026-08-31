#!/bin/bash
# Bounded-world border cleanup test for MyPhysicsCreateHkWorldPatch.
#
# In a bounded world (WorldSizeKm > 0) both HkWorld creation paths configure
# BROADPHASE_BORDER_REMOVE_ENTITY, so Havok removes any body that crosses the
# broad-phase border. The game must then run its SE-side cleanup
# (MyPhysics.HavokWorld_EntityLeftWorld, log line "removed entity"); a silent
# Havok-only removal leaves SE driving a stale broad-phase handle.
#
# Phase A exercises the vanilla creation path (session settings present, the
# Harmony prefix falls through). Phase B sets
# SE_LINUX_COMPAT_FORCE_HKWORLD_PREFIX=1 so every world is created through the
# patch's replacement path and the cleanup must arrive via its deferred
# EntityLeftWorld handler. Both phases load the committed 1 km world whose
# "BorderDriftShip" grid drifts across the border a few seconds in.
#
# Usage: tests/physics-border/run.sh [--skip-build] [--phase A|B]
# Exit codes: 0 both phases pass; 1 test failure; 2 harness failure.
set -u

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"

SE_REMOTE_DIR="${SE_REMOTE_DIR:-$HOME/.claude/skills/se-remote}"
SE_APPDATA="${SE_APPDATA:-$HOME/.config/SpaceEngineers}"
WORLD_SRC="$SCRIPT_DIR/world"
WORLD_DST="$SE_APPDATA/Saves/76561198223054696/Border Cleanup Test"
DRIVE_TIMEOUT="${DRIVE_TIMEOUT:-300}"

SKIP_BUILD=0
ONLY_PHASE=""
while [ $# -gt 0 ]; do
    case "$1" in
        --skip-build) SKIP_BUILD=1 ;;
        --phase) shift; ONLY_PHASE="$1" ;;
        *) echo "unknown option: $1" >&2; exit 2 ;;
    esac
    shift
done

fail() {
    echo "HARNESS ERROR: $*" >&2
    exit 2
}

echo "== linux-compat physics border cleanup test =="
[ -x "$HOME/.config/Pulsar/Interim.bin" ] || fail "Pulsar Interim launcher not found"
[ -d "$SE_REMOTE_DIR" ] || fail "se-remote skill not found at $SE_REMOTE_DIR"
[ -d "$WORLD_SRC" ] || fail "committed world not found at $WORLD_SRC"

if [ "$SKIP_BUILD" -eq 0 ]; then
    echo "== building LinuxCompat.sln (Debug) =="
    dotnet build "$REPO_DIR/LinuxCompat.sln" -c Debug || fail "plugin build failed"
fi

echo "== deploying world =="
mkdir -p "$WORLD_DST"
rsync -a --delete "$WORLD_SRC/" "$WORLD_DST/" || fail "world deploy failed"

GAME_LOCK="$HOME/.cache/se-game.lock"
echo "== acquiring the game lock =="
exec 9>"$GAME_LOCK"
if ! flock -w 900 9; then
    fail "game lock held by: $(cat "$GAME_LOCK" 2>/dev/null)"
fi
echo "pid $$ - linux-compat physics border test" >&9

stop_game() {
    "$SE_REMOTE_DIR/StopGame.sh" >/dev/null 2>&1
    flock -u 9 2>/dev/null
}
trap stop_game EXIT

RESULT=0

run_phase() {
    local phase="$1" force="$2"
    echo
    echo "== phase $phase (SE_LINUX_COMPAT_FORCE_HKWORLD_PREFIX=${force:-unset}) =="
    if pgrep -x Interim.bin >/dev/null; then
        fail "an Interim.bin instance is already running (not started by this harness)"
    fi

    local mark
    mark="$(date +%s)"

    if [ -n "$force" ]; then
        SE_LINUX_COMPAT_FORCE_HKWORLD_PREFIX="$force" "$SE_REMOTE_DIR/StartGame.sh" \
            || fail "game start failed"
    else
        "$SE_REMOTE_DIR/StartGame.sh" || fail "game start failed"
    fi

    SE_REMOTE_DIR="$SE_REMOTE_DIR" uv run --project "$SE_REMOTE_DIR" python \
        "$SCRIPT_DIR/drive_border.py" "$WORLD_DST" "$SE_APPDATA" "$DRIVE_TIMEOUT"
    local drive_rc=$?
    "$SE_REMOTE_DIR/StopGame.sh" >/dev/null 2>&1

    # Newest game log written by this phase.
    local game_log
    game_log="$(find "$SE_APPDATA" -maxdepth 1 -name 'SpaceEngineers*.log' -newermt "@$mark" \
        -printf '%T@ %p\n' 2>/dev/null | sort -rn | head -1 | cut -d' ' -f2-)"
    [ -n "$game_log" ] || fail "no game log produced by phase $phase"
    echo "phase $phase game log: $game_log"

    if ! grep -qoE 'LinuxCompat_[a-z0-9]+\.[a-z0-9]+' "$game_log"; then
        echo "WARNING: no randomized LinuxCompat_* assembly in the log - the run may have used a shipped plugin build!" >&2
    fi

    local replacement_lines
    replacement_lines="$(grep -c "\[LinuxCompat\] CreateHkWorld replacement path" "$game_log")"
    echo "phase $phase: $replacement_lines CreateHkWorld replacement-path line(s)"

    if [ "$drive_rc" -ne 0 ]; then
        echo "phase $phase FAIL: no SE-side border cleanup (grep '$game_log' for BorderDriftShip)" >&2
        RESULT=1
        return
    fi

    if [ -n "$force" ]; then
        if [ "$replacement_lines" -eq 0 ]; then
            echo "phase $phase FAIL: forced mode never took the replacement path" >&2
            RESULT=1
            return
        fi
    else
        if [ "$replacement_lines" -ne 0 ]; then
            echo "phase $phase FAIL: settings-less replacement path fired on a normal run" >&2
            RESULT=1
            return
        fi
    fi
    echo "phase $phase PASS"
}

[ "$ONLY_PHASE" = "B" ] || run_phase A ""
[ "$ONLY_PHASE" = "A" ] || run_phase B 1

stop_game
trap - EXIT

echo
if [ "$RESULT" -eq 0 ]; then
    echo "RESULT: PASS"
else
    echo "RESULT: FAIL"
fi
exit "$RESULT"

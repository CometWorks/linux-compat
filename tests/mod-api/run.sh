#!/bin/bash
# Mod API boundary test suite for the linux-compat plugin (game client).
#
# Builds the plugin from this working tree, deploys the LinuxCompatDiagnostics
# mod, clears the compiled-mods cache (REQUIRED: dev builds randomize the
# plugin assembly identity, so cached mods pin a stale assembly), starts the
# game headless via the Pulsar Interim launcher, loads the diagnostics world,
# waits for the suite to finish, and parses the results.
#
# Usage: tests/mod-api/run.sh [--skip-build] [--keep-running]
#
# Exit codes: 0 all probes passed; 1 probe failures; 2 harness/run failure.
#
# Requirements:
#   - Pulsar Interim at ~/.config/Pulsar/Interim.bin with the Legacy profile
#     containing dotnet-compat + linux-compat + remote dev-folder plugins
#     (the production two-plugin stack plus the Remote test plugin).
#   - The se-remote skill checkout (SE_REMOTE_DIR, default
#     ~/.claude/skills/se-remote) with its uv environment prepared.
#   - The "Linux Compat Diagnostics" world (references the mod by name).
set -u

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"

SE_REMOTE_DIR="${SE_REMOTE_DIR:-$HOME/.claude/skills/se-remote}"
SE_APPDATA="${SE_APPDATA:-$HOME/.config/SpaceEngineers}"
MOD_SRC="$SCRIPT_DIR/LinuxCompatDiagnostics"
MOD_DST="$SE_APPDATA/Mods/LinuxCompatDiagnostics"
WORLD_PATH="${WORLD_PATH:-$SE_APPDATA/Saves/76561198223054696/Linux Compat Diagnostics}"
SUITE_LOG="$SE_APPDATA/Storage/LinuxCompatDiagnostics_LinuxCompatDiagnostics/LinuxCompatDiagnostics.log"
SUITE_TIMEOUT="${SUITE_TIMEOUT:-600}"

SKIP_BUILD=0
KEEP_RUNNING=0
for arg in "$@"; do
    case "$arg" in
        --skip-build) SKIP_BUILD=1 ;;
        --keep-running) KEEP_RUNNING=1 ;;
        *) echo "unknown option: $arg" >&2; exit 2 ;;
    esac
done

fail() {
    echo "HARNESS ERROR: $*" >&2
    exit 2
}

echo "== linux-compat mod API suite (client) =="
echo "repo:      $REPO_DIR"
echo "world:     $WORLD_PATH"
echo "suite log: $SUITE_LOG"

[ -x "$HOME/.config/Pulsar/Interim.bin" ] || fail "Pulsar Interim launcher not found"
[ -d "$SE_REMOTE_DIR" ] || fail "se-remote skill not found at $SE_REMOTE_DIR"
[ -d "$WORLD_PATH" ] || fail "diagnostics world not found at $WORLD_PATH"

# 1. Build the plugin from the working tree (both client and server targets).
if [ "$SKIP_BUILD" -eq 0 ]; then
    echo "== building LinuxCompat.sln (Debug) =="
    dotnet build "$REPO_DIR/LinuxCompat.sln" -c Debug || fail "plugin build failed"
fi

# 2. Deploy the mod from the repo (source of truth) to the game's Mods dir.
echo "== deploying mod =="
mkdir -p "$MOD_DST"
rsync -a --delete --exclude '*.7z' "$MOD_SRC/" "$MOD_DST/" || fail "mod deploy failed"
# The case-pair negative control cannot live in git (same name, different
# case would break Windows checkouts); generate the lowercase sibling here.
printf 'lower-case-file\n' > "$MOD_DST/TestData/CaseSensitivity/casepair.txt"

# 3. Clear caches and the previous run's results.
echo "== clearing compiled-mods cache and old results =="
rm -f "$SE_APPDATA/Performance/Cache/CompiledMods"/*.cache
rm -f "$SUITE_LOG"

# 4. Start the game headless (always --headless; never fullscreen).
#    ~/.cache/se-game.lock is the machine-wide advisory lock shared with the
#    other automation sessions (exclusive flock held while a game instance
#    runs; auto-released if the holder dies).
GAME_LOCK="$HOME/.cache/se-game.lock"
echo "== acquiring the game lock =="
exec 9>"$GAME_LOCK"
if ! flock -w 900 9; then
    fail "game lock held by: $(cat "$GAME_LOCK" 2>/dev/null)"
fi
echo "pid $$ - linux-compat mod-api suite: client run" >&9

echo "== starting the game =="
if pgrep -x Interim.bin >/dev/null; then
    fail "an Interim.bin instance is already running (not started by this harness)"
fi
"$SE_REMOTE_DIR/StartGame.sh" || fail "game start failed"

stop_game() {
    if [ "$KEEP_RUNNING" -eq 0 ]; then
        "$SE_REMOTE_DIR/StopGame.sh" >/dev/null 2>&1
        flock -u 9 2>/dev/null
    fi
}
trap stop_game EXIT

# 5. Load the world and wait for the suite to terminate its log.
echo "== driving the client =="
SE_REMOTE_DIR="$SE_REMOTE_DIR" uv run --project "$SE_REMOTE_DIR" python \
    "$SCRIPT_DIR/drive_client.py" "$WORLD_PATH" "$SUITE_LOG" "$SUITE_TIMEOUT"
DRIVE_RC=$?

# 6. Confirm the run exercised the working-tree plugin build, not a shipped one.
#    Dev-folder builds randomize the assembly name (LinuxCompat_xxxxx).
GAME_LOG="$SE_APPDATA/SpaceEngineers.log"
[ -f "$GAME_LOG" ] || GAME_LOG="$(ls -t "$SE_APPDATA"/SpaceEngineers*.log 2>/dev/null | head -1)"
if [ -f "$GAME_LOG" ]; then
    if grep -qoE 'LinuxCompat_[a-z0-9]+\.[a-z0-9]+' "$GAME_LOG"; then
        echo "verified: dev-folder LinuxCompat assembly loaded ($(grep -oE 'LinuxCompat_[a-z0-9]+\.[a-z0-9]+' "$GAME_LOG" | head -1))"
    else
        echo "WARNING: no randomized LinuxCompat_* assembly in $GAME_LOG - the run may have used a shipped plugin build!" >&2
    fi
fi

stop_game
trap - EXIT

[ "$DRIVE_RC" -eq 0 ] || fail "client drive failed (rc=$DRIVE_RC); check $GAME_LOG and ~/.config/Pulsar/Legacy/info.log"

# 7. Parse and report. The security manifest makes the containment probes a
#    regression test: a probe that stops being reported fails the run even
#    when nothing FAILs (see parse_results.py --update-security-manifest).
echo "== results =="
python3 "$SCRIPT_DIR/parse_results.py" "$SUITE_LOG" \
    --security-manifest "$SCRIPT_DIR/security-probes.txt"

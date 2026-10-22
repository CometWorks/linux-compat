#!/bin/bash
# Pulsar client mod test for the linux-compat plugin (game client).
#
# Loads Build Info (Workshop 514062285) as a Pulsar client mod, the way players
# enable it in Pulsar's mod list, in an empty offline world, and checks that it
# can read its own files. Pulsar never adds client mods to the world's mod list,
# so this covers the mod-location calls of a mod that MySession.Mods does not
# name (DIS-0041: "File not found in mod folder", VanillaDefinitions.txt, fonts).
#
# Usage: tests/client-mods/run.sh [--render] [--keep-running] [LINUX_COMPAT_DIR]
#
# LINUX_COMPAT_DIR is the source tree Pulsar compiles as the linux-compat dev
# folder (default: this working tree). Its native assets are read from
# ../../linux-dependencies and ../../linux-native-wrappers, as for any dev
# folder build of the plugin.
#
# Exit codes: 0 Build Info logged no error; 1 it logged errors; 2 harness or
# run failure.
set -u

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"

PULSAR_SOURCE="${PULSAR_SOURCE:-$HOME/.config/Pulsar}"
TEST_ROOT="${TEST_ROOT:-$HOME/.se-test/linux-compat-client-mods}"
REMOTE_PORT="${REMOTE_PORT:-24170}"
CLIENT_ID="${CLIENT_ID:-76561199500000170}"
GAME_DIR="${GAME_DIR:-$HOME/.steam/steam/steamapps/common/SpaceEngineers}"
BUILD_INFO="${BUILD_INFO:-$HOME/.steam/steam/steamapps/workshop/content/244850/514062285}"
SE_REMOTE_DIR="${SE_REMOTE_DIR:-$HOME/.claude/skills/se-remote}"
REMOTE_CFG="${REMOTE_CFG:-$HOME/.config/SpaceEngineers/Remote.cfg}"
SETTLE="${SETTLE:-60}"

RENDER=0
KEEP_RUNNING=0
LC_DIR="$REPO_DIR"
for arg in "$@"; do
    case "$arg" in
        --render) RENDER=1 ;;
        --keep-running) KEEP_RUNNING=1 ;;
        -*) echo "unknown option: $arg" >&2; exit 2 ;;
        *) LC_DIR="$(cd "$arg" && pwd)" ;;
    esac
done

INSTANCE="$TEST_ROOT/pulsar"
APPDATA="$TEST_ROOT/appdata"
STEAMAPPS="$TEST_ROOT/steamapps"
WORLD="$APPDATA/Saves/$CLIENT_ID/BuildInfoTest"
RESULTS="$TEST_ROOT/results"
# Build Info's own log, in its local storage.
MOD_LOG_DIR="$APPDATA/Storage/514062285.sbm_BuildInfo"

fail() {
    echo "HARNESS ERROR: $*" >&2
    exit 2
}

echo "== linux-compat Pulsar client mod test =="
echo "linux-compat: $LC_DIR"
echo "test root:    $TEST_ROOT"

[ -x "$PULSAR_SOURCE/Interim.bin" ] || fail "Pulsar Interim not found in $PULSAR_SOURCE"
[ -d "$SE_REMOTE_DIR" ] || fail "se-remote skill not found at $SE_REMOTE_DIR"
[ -d "$BUILD_INFO/Data/Scripts" ] || fail "Build Info not found at $BUILD_INFO"
[ -f "$LC_DIR/ClientPlugin/ClientPlugin.xml" ] || fail "no linux-compat tree at $LC_DIR"
grep -q "<Name>remote</Name>" "$PULSAR_SOURCE/Interim/Sources/sources.xml" ||
    fail "the Pulsar source folder has no 'remote' dev folder plugin"

# 1. A Pulsar instance of its own: copied launcher, shared NuGet and Libraries,
#    copied plugin sources and caches, never the Preloader (see
#    testing/pulsar-dev-instances in the workspace for why).
if [ ! -x "$INSTANCE/Interim.bin" ]; then
    echo "== creating the Pulsar instance =="
    mkdir -p "$INSTANCE/Interim"
    cp -p "$PULSAR_SOURCE"/Interim.* "$INSTANCE/"
    for shared in NuGet Libraries; do
        ln -s "$PULSAR_SOURCE/$shared" "$INSTANCE/$shared"
    done
    for part in config.xml DevFolder GitHub Local Sources; do
        cp -r "$PULSAR_SOURCE/Interim/$part" "$INSTANCE/Interim/$part"
    done
fi

# 2. The profile: the source folder's dev folder plugins, with linux-compat
#    from the tree under test, and Build Info as the only client mod.
python3 - "$PULSAR_SOURCE/Interim" "$INSTANCE/Interim" "$LC_DIR" <<'PY' || fail "profile setup failed"
import re, sys
src, dst, lc = sys.argv[1:]
sources = open(f"{src}/Sources/sources.xml").read()
plugin = re.search(r"<LocalPlugin>\s*<Name>linux-compat</Name>.*?</LocalPlugin>", sources, re.S)
if not plugin:
    sys.exit("the Pulsar source folder has no 'linux-compat' dev folder plugin")
fixed = re.sub(r"<Folder>.*?</Folder>", f"<Folder>{lc}</Folder>", plugin.group(0))
open(f"{dst}/Sources/sources.xml", "w").write(sources.replace(plugin.group(0), fixed))
profile = open(f"{src}/Profiles/Current.xml").read()
profile = re.sub(r"<Mods\s*/>|<Mods>.*?</Mods>",
                 "<Mods>\n    <unsignedLong>514062285</unsignedLong>\n  </Mods>", profile, flags=re.S)
import os
os.makedirs(f"{dst}/Profiles", exist_ok=True)
open(f"{dst}/Profiles/Current.xml", "w").write(profile)
PY

# 3. Pulsar finds client mods in Bin64/../../../workshop/content/244850, derived
#    lexically, so a symlinked game folder gives the instance a Workshop folder
#    of its own that holds only Build Info.
mkdir -p "$STEAMAPPS/common" "$STEAMAPPS/workshop/content/244850"
[ -e "$STEAMAPPS/common/SpaceEngineers" ] || ln -s "$GAME_DIR" "$STEAMAPPS/common/SpaceEngineers"
rsync -a --delete "$BUILD_INFO/" "$STEAMAPPS/workshop/content/244850/514062285/" ||
    fail "Build Info copy failed"

# 4. The game's user data: the Remote API port and the game's empty world, no
#    compiled-mods cache (dev builds randomize the plugin assembly identity),
#    and no old logs.
mkdir -p "$APPDATA/Saves/$CLIENT_ID" "$RESULTS"
sed "s|<ListenPort>[0-9]*</ListenPort>|<ListenPort>$REMOTE_PORT</ListenPort>|" \
    "$REMOTE_CFG" > "$APPDATA/Remote.cfg" || fail "no Remote.cfg at $REMOTE_CFG"
rm -rf "$WORLD"
cp -r "$GAME_DIR/Content/CustomWorlds/Empty World" "$WORLD" || fail "world copy failed"
rm -rf "$APPDATA/Performance/Cache/CompiledMods" "$MOD_LOG_DIR"
rm -f "$APPDATA"/SpaceEngineers*.log "$INSTANCE/Interim/info.log" "$RESULTS"/*

# 5. Start the client: offline, its own instance, appdata, identity and port.
FLAGS=(--headless --quality minimal --resolution 1280x720)
if [ "$RENDER" -eq 0 ]; then
    export PULSAR_NO_RENDER=true
    FLAGS+=(--no-render)
fi
echo "== starting the game =="
"$INSTANCE/Interim.bin" -multiInstance -lazySteam -noprompt -noupdate -nosplash -sources \
    -bin64 "$STEAMAPPS/common/SpaceEngineers/Bin64" -appdata "$APPDATA" \
    --no-steam --client-id "$CLIENT_ID" "${FLAGS[@]}" > "$RESULTS/client.out" 2>&1 &

# Only the processes started with this appdata folder are ours.
our_pids() { pgrep -f -- "-appdata $APPDATA" | grep -vx "$$"; }
stop_game() {
    [ "$KEEP_RUNNING" -eq 1 ] && return
    for p in $(our_pids); do kill "$p" 2>/dev/null; done
    sleep 5
    for p in $(our_pids); do kill -9 "$p" 2>/dev/null; done
}
trap stop_game EXIT

# 6. Load the world and let Build Info finish its background loading.
echo "== driving the client =="
SE_REMOTE_DIR="$SE_REMOTE_DIR" uv run --quiet --project "$SE_REMOTE_DIR" python - \
    "$REMOTE_PORT" "$WORLD" "$SETTLE" <<'PY' || fail "client drive failed; see $RESULTS/client.out"
import os, sys, time
sys.path.insert(0, os.environ["SE_REMOTE_DIR"])
from se_remote import RemoteAPI
port, world, settle = sys.argv[1], sys.argv[2], float(sys.argv[3])
with RemoteAPI(f"http://127.0.0.1:{port}", username="admin", password="SpaceEngineers") as api:
    api.wait_for_api(max_wait=1800.0)
    print("API up; loading", world, flush=True)
    api.load(world)
    deadline = time.monotonic() + 600
    while time.monotonic() < deadline:
        try:
            if api.get_state().get("active"):
                break
        except Exception:
            pass
        time.sleep(2)
    else:
        sys.exit("session never became active")
    print(f"session active; waiting {settle:.0f} s for Build Info", flush=True)
    time.sleep(settle)
PY

GAME_LOG="$(ls -t "$APPDATA"/SpaceEngineers_*.log 2>/dev/null | head -1)"
[ -n "$GAME_LOG" ] || fail "no game log in $APPDATA"
cp "$GAME_LOG" "$RESULTS/game.log"
cp "$INSTANCE/Interim/info.log" "$RESULTS/pulsar.log" 2>/dev/null
cp "$MOD_LOG_DIR/info.log" "$RESULTS/buildinfo.log" 2>/dev/null
stop_game
trap - EXIT

# 7. Results. The run counts only if Pulsar loaded Build Info as a client mod,
#    its scripts ran, and the plugin built from the tree under test was used.
echo "== results =="
ok=1
grep -qE "Loading client mod scripts for (Build Info \()?514062285" "$RESULTS/pulsar.log" ||
    { echo "INVALID: Pulsar did not load Build Info as a client mod"; ok=0; }
grep -q "Registered modules from: 514062285" "$RESULTS/game.log" ||
    { echo "INVALID: Build Info's scripts did not run"; ok=0; }
grep -qF "Compiling files from $LC_DIR:" "$RESULTS/pulsar.log" ||
    { echo "INVALID: linux-compat was not compiled from $LC_DIR"; ok=0; }
[ "$ok" -eq 1 ] || fail "invalid run; see $RESULTS"

errors="$(grep -A1 "Build Info error/exception" "$RESULTS/game.log")"
echo "fonts parsed: $(grep -c "Parsed font 'BI_" "$RESULTS/buildinfo.log" 2>/dev/null) (expected 2)"
if [ -n "$errors" ]; then
    echo "$errors" | head -20
    echo "FAIL: Build Info logged $(grep -c "Build Info error/exception" "$RESULTS/game.log") errors"
    exit 1
fi
grep -q "Parsed font 'BI_Monospace'" "$RESULTS/buildinfo.log" 2>/dev/null ||
    { echo "FAIL: Build Info did not parse its fonts"; exit 1; }
echo "PASS: Build Info read its files as a client mod"

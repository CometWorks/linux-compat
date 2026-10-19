#!/usr/bin/env bash
# Builds LinuxCompat at each given git ref (or "." for the working tree) and runs the mod corpus
# check on every build. Builds run in parallel, then the checks do.
#   tests/mod-corpus/run.sh [ref...]          default: .
# BIN64, MODS and OUT override the game's Bin64, the Workshop content folder and the output folder.
set -euo pipefail

repo=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
steam=${STEAM:-$HOME/.steam/steam/steamapps}
bin64=${BIN64:-$steam/common/SpaceEngineers/Bin64}
mods=${MODS:-$steam/workshop/content/244850}
out=${OUT:-$(mktemp -d -t mod-corpus.XXXXXX)}
[ $# -gt 0 ] || set -- .
mkdir -p "$out"

worktrees=()
cleanup() { for w in "${worktrees[@]}"; do git -C "$repo" worktree remove --force "$w"; done; }
trap cleanup EXIT

dotnet build "$repo/tests/mod-corpus/ModCorpus.csproj" -c Release -o "$out/harness" >"$out/harness.log" 2>&1 \
    || { echo "harness build failed, see $out/harness.log"; exit 2; } &

labels=()
for ref in "$@"; do
    label=${ref//[^A-Za-z0-9._-]/_}
    [ "$ref" = . ] && label=worktree
    labels+=("$label")
    src=$repo
    if [ "$ref" != . ]; then
        src=$out/src-$label
        git -C "$repo" worktree add --detach -q "$src" "$ref"
        worktrees+=("$src")
        # The local paths (Steamworks, linux-dependencies) the plugin build needs.
        [ ! -f "$repo/Directory.Build.props.user" ] || cp "$repo/Directory.Build.props.user" "$src/"
    fi
    dotnet build "$src/ClientPlugin/ClientPlugin.csproj" -c Release -p:Pulsar= -p:Magnetar= -p:Bin64="$bin64" \
        -o "$out/$label" >"$out/$label.build.log" 2>&1 \
        || { echo "$ref: build failed, see $out/$label.build.log"; exit 2; } &
done
fail=0
for job in $(jobs -p); do wait "$job" || fail=1; done
[ $fail = 0 ] || exit 2

for label in "${labels[@]}"; do
    dotnet "$out/harness/ModCorpus.dll" "$out/$label/LinuxCompat.dll" "$bin64" "$mods" >"$out/$label.txt" 2>&1 &
done
for job in $(jobs -p); do wait "$job" || fail=1; done

for i in "${!labels[@]}"; do
    echo "${@:i+1:1}: $(tail -n 1 "$out/${labels[i]}.txt")"
done
echo "reports in $out"
exit $fail

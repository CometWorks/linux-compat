# Mod corpus check

Runs `CompilationRewriter` from a built `LinuxCompat.dll` on the scripts of every
Workshop mod on this machine and reports what the rewrite breaks. Run it on a
rewriter change before a release.

Each top-level folder under a mod's `Data/Scripts` is one assembly, as in the
game's `MyScriptManager`. The check compiles it the way the game does: C# 6, the
game's preprocessor symbols, the `Bin64` assemblies mod scripts can
reference, the framework assemblies `netstandard` forwards to, and the shim
reference added. It compiles every assembly twice, as written and after the
rewrite, and reports:

- `NEW-ERRORS`: errors only the rewritten compilation has, with up to 8 of them
- `CRASH`: the rewriter threw

A mod that does not compile as written is still checked; only its new errors
count. The last line is a summary, and the exit code is 0 when there are no new
errors and no crashes:

```
SUMMARY assemblies=276 clean_unrewritten=248 rewritten=232 new_errors=0 crashes=0
```

1.0.35 broke Build Info this way (CS0121, `ToGameText(null)` between its `string`
and `string[]` overloads), and this check finds it: on that version it reports
the two Build Info assemblies, `514062285/BuildInfo` and `2392775805/BuildInfo`.

The harness loads the plugin through reflection, so it works with any build of
it. Roslyn and the game assemblies come from `Bin64` at run time, so the mods
are compiled by the game's own compiler. It is not part of `LinuxCompat.sln`,
and its own `Directory.Build.props` shadows the repository root's, like
`tests/rewriter`.

## Running it

Build the plugin without deploying it, build the harness, and run it with the
plugin, the game's `Bin64` and the Workshop content folder of the game:

```bash
dotnet build ClientPlugin/ClientPlugin.csproj -c Release -p:Pulsar= -p:Magnetar= -o /tmp/lc
dotnet build tests/mod-corpus/ModCorpus.csproj -c Release -o /tmp/mod-corpus
dotnet /tmp/mod-corpus/ModCorpus.dll /tmp/lc/LinuxCompat.dll \
    ~/.steam/steam/steamapps/common/SpaceEngineers/Bin64 \
    ~/.steam/steam/steamapps/workshop/content/244850
```

`run.sh` does all of that for one or more git refs, building them in parallel
and then checking them in parallel. `.` is the working tree. Each ref is built
in a temporary worktree with a copy of `Directory.Build.props.user`:

```bash
tests/mod-corpus/run.sh origin/main .
```

It prints the summary line of each ref and the folder with the full reports.
`BIN64`, `MODS` and `OUT` override the default `Bin64`, Workshop and output
folders. Set `CORPUS_BASE=1` to also list the first errors of mods that do not
compile as written.

With the 276 assemblies of 999 Workshop items on sirius a run takes about 80
seconds for two refs and up to 2 GB of RAM per ref.

## Limits

- Compile only. Nothing runs, so a rewrite that compiles but behaves
  differently goes unnoticed. `tests/mod-api` covers behavior.
- No script whitelist check. The game also rejects a mod that uses a type or
  member outside `MyScriptWhitelist`; the harness skips that step.
- Local mods only: the mods downloaded into the given Workshop folder, nothing
  from the Workshop that this machine has not subscribed to, and no world-local
  mods from `Mods`.
- Mod scripts only. The rewriter leaves in-game (programmable block) scripts
  alone.
- The symbols are the stable branch's: `BRANCH_STABLE`, `STABLE`, and
  `VERSION_`/`BUILD_` from `SpaceEngineersGame.SE_VERSION` in `Bin64`.

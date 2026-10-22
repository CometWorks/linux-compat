# Mod API boundary test suite

Automated verification that the mod API boundary of this plugin gives mods
Windows semantics (backslash paths, synthetic `C:\` drive, CRLF, Windows
Stopwatch frequency) while the game internals stay native, with translation
happening only at the boundary: the Roslyn rewriter in `Shared/Rewriter/`, the
wrappers in `Shared/Patches/PathHandling/ModApiWrappers/`, and the
`PathCache.ResolveAbsolute` ingress funnel.

## Layout

- `LinuxCompatDiagnostics/`: the test mod, and the source of truth. The harness
  deploys it to `~/.config/SpaceEngineers/Mods/`. A session component runs ~500
  expected-vs-actual probes during world load and writes
  `Storage/LinuxCompatDiagnostics_LinuxCompatDiagnostics/LinuxCompatDiagnostics.log`.
- `run.sh`: client harness. Builds the plugin, deploys the mod, clears the
  compiled-mods cache, starts the game headless, loads the diagnostics world,
  waits for the suite and parses the results. Exit 0 = green.
- `drive_client.py`: Remote-API driver used by `run.sh` (needs the `se-remote`
  skill checkout, `SE_REMOTE_DIR`).
- `run-server.sh`: dedicated server harness. Builds the plugin, prepares an
  isolated Magnetar DS instance, clears the compiled-mods cache, launches the DS
  offline with `-noimplicitmod`, waits for the suite and parses the results.
  Exit 0 = green.
- `prepare_ds.py`: DS preparation used by `run-server.sh`. Under
  `MODAPI_DS_ROOT` (default `~/.se-test/linux-compat-mod-api`) it writes a
  Magnetar config folder that registers this working tree as the
  `linux-compat` dev folder source, and a DS data folder with its own
  dedicated config, the offline fake-Workshop registration of the mod (the DS
  rejects local mods in multiplayer and cannot download Workshop items
  offline) and the scratch world that references it. The dedicated config and
  the `Empty Creative` world template are read from `TEMPLATE_INSTANCE`
  (default `~/.config/SpaceEngineersDedicated`); nothing outside the root is
  written.
  Run it only while that root's DS is stopped.
- `prepare_client_mod.py`: stages the mod as a Pulsar client mod, see "Client
  mod run" below.
- `parse_results.py`: log parser, usable standalone on a suite log. It reports
  the security probes as their own section and enforces the manifest.
- `security-probes.txt`: the security probe manifest (see below).

## Ownership tags

The production stack runs the game on .NET 10 with two plugins: `dotnet-compat`
(applied first, owns .NET-10-vs-Framework differences that reproduce on
Windows) and `linux-compat` (owns Linux-only differences). Every probe carries
the owner:

- `[LINUX]`: the expected value is what the mod observes on Windows (backslash
  separators, drive letters, case-insensitive lookups, CRLF on disk, 10 MHz
  Stopwatch). A FAIL is a linux-compat bug.
- `[DOTNET]`: the expected value is what raw .NET 10 on Windows gives
  (`Encoding.Default` = UTF-8, no codepage 1252, ICU collation, culture
  formatting). A FAIL is filed against the `dotnet-compat` repo, not here. It
  either shows a dotnet-compat shim in action (verify its intent there) or a
  genuine dotnet-compat gap.

The dotnet-compat mod rewriter only activates for mods that fail to compile.
This mod compiles cleanly, so only the linux-compat rewriter transforms it. A
rewriter-detection probe checks exactly that: `typeof(Stopwatch).FullName`
must be the `WindowsStopwatch` shim.

## Home folder leaks

Mods must never see the Linux home folder or user name; on Windows they see
`C:\users\<user>\...`, so on Linux they get `C:\users\steamuser\...`.
`parse_results.py` fails the run on any log line that names the home folder or
the user name of whoever runs it, whatever the probe on that line reported. The
log is written by the mod, so such a line is a leak. `ProbesHomeLeak.cs` covers
the call shapes, definition paths, caught exceptions and a world listed and read
through `UserDataPath + "\Saves"`. `ProbesRareLeaks.cs` covers the rarer reaches:
path getters as method groups, checkpoint paths, a parallel task's exceptions,
`def.GetObjectBuilder()`, Windows paths written into a definition, and the
dedicated server's `Plugins` list.

## Security probes

Every path-containment probe is named `security: ...` and comes in matched
halves, so neither direction can regress unnoticed:

- `security: refused, <op>: <case>` is an access the boundary must reject.
  Traversal (`../`, backslash, mid-path, deep-to-root), bare `..`, absolute
  native paths, `%2e%2e`, `\\?\` and UNC prefixes, invented drive letters
  (`C:`, `Z:`, `Q:`), a drive left after one translation (`C:C:\...`, a drive
  after a dot segment or after the Content folder), separators in storage
  filenames, and the engine's protected `Data/Scripts` location, across game
  content, mod location, the object builder serializer and all three storage
  scopes. Every one of these is refused on Windows too, so a FAIL means the
  Linux port is **wider** than Windows. The one exception is a `ModItem` the
  mod builds that names no loaded mod: Windows reads from whatever folder its
  `Name` selects, and the Linux port refuses it on purpose. A loaded mod is a
  mod of the world or a Pulsar client mod, and an item naming one reads from
  that mod's folder.
- `security: allowed, <op>: <case>` is an access a real mod is entitled to and
  which an over-tightened containment check would break. Relative, backslash
  and wrong-case paths, `..` that stays *inside* the root
  (`Data/../Data/...`), leading `./`, absolute paths built from the egress
  values (`IMyGamePaths.ContentPath`, `IMyModContext.ModPath`), the user data
  folder in Windows form through the object builder serializer (backslashes,
  forward slashes or another casing), and a write/exists/read/delete round trip
  in local, world and global storage.

The audit these cover is
`~/ws/se/pulsar-tests/se1/reports/security/mod-path-audit.md`.

`security-probes.txt` lists every security probe name a green run reports.
`parse_results.py --security-manifest` fails the run when one of them is
missing, not only when one of them fails. A probe deleted along with the check
it guards, or a suite that died before reaching that section, is a regression
too. After adding or renaming probes, regenerate and commit it:

```
python3 tests/mod-api/parse_results.py <suite.log> \
    --update-security-manifest tests/mod-api/security-probes.txt
```

## Client mod run

Players can also enable a mod in Pulsar instead of in the world, as a client
mod. Pulsar compiles its scripts with a mod context of its own and never adds
it to the session's mods, so the mod API has to accept that context's
`ModItem` as well. Build Info, enabled that way, found none of its textures and
fonts on 1.0.35 and 1.0.36. To cover it, stage the mod as a client mod:

```
python3 tests/mod-api/prepare_client_mod.py tests/mod-api/LinuxCompatDiagnostics \
    <root> <pulsar folder>/Legacy
```

Pulsar loads client mods from the Workshop folder next to the game's `Bin64`.
The script builds a stand-in Steam library under `<root>`, with symlinks to the
game install and the mod copied in under the fake Workshop id `900000001`, so
the real library stays untouched. It also enables the mod in that Pulsar
folder's current profile, so use a Pulsar folder of your own. Start that Pulsar
with `-bin64 <the path the script prints>`, load a copy of the diagnostics
world whose `<Mods>` is empty, and parse the log the suite writes under
`Storage/900000001.sbm_LinuxCompatDiagnostics/`. The suite logs `INFO loaded as
= "client mod"`. Pass no security manifest, as for the dedicated server: under
a Workshop id the suite skips 4 PathUtils probes. With that, a client mod run
passes like a world mod run; a build without the fix fails 33 probes there, the
`ReadFileInModLocation` section among them.

## Log format

```
PASS [LINUX] name | expected=... | actual=...
FAIL [DOTNET] name | expected=... | actual=...
INFO name = value
=== SUMMARY pass=N fail=N linux_fail=N dotnet_fail=N info=N ===
=== END LinuxCompatDiagnostics ===
```

The `END` line is the terminator; without it the run died mid-suite (parser
exit code 2). All FAILs and the summary are mirrored to `SpaceEngineers.log`,
so results survive a broken storage API.

## Notes

- The client harness holds an exclusive `flock` on `~/.cache/se-game.lock`
  while the game runs. It is the machine-wide advisory lock shared with the
  other automation sessions, and it is auto-released if the holder dies. It
  waits up to 15 minutes for it. The DS harness runs isolated with
  `-multiInstance` and takes no lock. Give parallel DS runs their own
  `MODAPI_DS_ROOT`, `MODAPI_DS_PORT` and `MODAPI_DS_STEAM_PORT`.
- DS provenance: the Magnetar dev folder source must be named `linux-compat`,
  because only a name matching the plugin id overrides the implicit core
  plugin; otherwise the MagnetarHub build is loaded. `run-server.sh` writes
  that source itself and fails the run unless the launch's `info.log` says
  `Compiling files for LinuxCompat (linux-compat) from <this working tree>`.
  Set `DOTNET_COMPAT_DIR` to compile a dotnet-compat checkout too; otherwise
  the MagnetarHub build of dotnet-compat runs.
- The compiled-mods cache clear in `run.sh` is not optional. Dev builds
  randomize the plugin assembly identity, and cached mods pin the stale
  assembly, so world load then fails with
  `FileNotFoundException: LinuxCompat_...`.
- `run.sh` checks the game log for a randomized
  `LinuxCompat_<random>.<random>` assembly name. That name alone does not
  prove a dev folder build: Magnetar gives hub builds the same kind of name,
  which is why `run-server.sh` checks `info.log` instead.
- `TestData/CaseSensitivity/casepair.txt` (lowercase sibling of
  `CasePair.txt`) is generated at deploy time; a file pair differing only in
  case cannot be committed without breaking Windows checkouts.
- The mod ships a model and texture (copied from vanilla `GratedCatwalk`)
  referenced from `Data/DiagBlocks.sbc` with backslashes and deliberately
  wrong casing, to exercise the definition/asset pipeline. A second definition
  is mutated at runtime to a Windows-shaped absolute path, to exercise the
  `PathCache.ResolveAbsolute` ingress funnel.
- Dedicated server: the mod runs unchanged there and writes the same log
  format, so `run-server.sh` parses the results with the same parser. It
  passes no security manifest; the manifest is enforced on the client run.

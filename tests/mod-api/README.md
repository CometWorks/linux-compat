# Mod API boundary test suite

Automated verification that the mod API boundary of this plugin gives mods
Windows semantics (backslash paths, synthetic `C:\` drive, CRLF, Windows
Stopwatch frequency) while the game internals stay native, with translation
happening only at the boundary (Roslyn rewriter in `Shared/Rewriter/` +
wrappers in `Shared/Patches/PathHandling/ModApiWrappers/` + the
`PathCache.ResolveAbsolute` ingress funnel).

## Layout

- `LinuxCompatDiagnostics/` - the test mod (source of truth; deployed to
  `~/.config/SpaceEngineers/Mods/` by the harness). A session component runs
  ~260 expected-vs-actual probes during world load and writes
  `Storage/LinuxCompatDiagnostics_LinuxCompatDiagnostics/LinuxCompatDiagnostics.log`.
- `run.sh` - client harness: build plugin, deploy mod, clear the
  compiled-mods cache, start the game headless, load the diagnostics world,
  wait for the suite, parse results. Exit 0 = green.
- `drive_client.py` - Remote-API driver used by `run.sh` (needs the
  `se-remote` skill checkout, `SE_REMOTE_DIR`).
- `parse_results.py` - log parser; usable standalone on a suite log. Reports
  the security probes as their own section and enforces the manifest.
- `security-probes.txt` - the security probe manifest (see below).

## Ownership tags

The production stack runs the game on .NET 10 with two plugins: `dotnet-compat`
(applied first, owns .NET-10-vs-Framework differences that reproduce on
Windows) and `linux-compat` (owns Linux-only differences). Every probe carries
the owner:

- `[LINUX]` - expected value is what the mod observes on **Windows**
  (backslash separators, drive letters, case-insensitive lookups, CRLF on
  disk, 10 MHz Stopwatch). A FAIL is a linux-compat bug.
- `[DOTNET]` - expected value is what **raw .NET 10 on Windows** gives
  (`Encoding.Default` = UTF-8, no codepage 1252, ICU collation, culture
  formatting). A FAIL is filed against the `se-dotnet-compat` repo, not here -
  it either shows a dotnet-compat shim in action (verify its intent there) or
  a genuine dotnet-compat gap.

The dotnet-compat mod rewriter only activates for mods that FAIL to compile;
this mod compiles cleanly, so only the linux-compat rewriter transforms it.
The suite verifies that with a rewriter-detection probe
(`typeof(Stopwatch).FullName` must be the `WindowsStopwatch` shim).

## Security probes

Every path-containment probe is named `security: ...` and comes in matched
halves, so neither direction can regress unnoticed:

- `security: refused, <op>: <case>` - the boundary must reject the access.
  Traversal (`../`, backslash, mid-path, deep-to-root), bare `..`, absolute
  native paths, `%2e%2e`, `\\?\` and UNC prefixes, invented drive letters
  (`C:`, `Z:`, `Q:`), separators in storage filenames, and the engine's
  protected `Data/Scripts` location - across game content, mod location and
  all three storage scopes. Every one of these is refused on Windows too, so a
  FAIL means the Linux port is **wider** than Windows.
- `security: allowed, <op>: <case>` - the access a real mod is entitled to and
  which an over-tightened containment check would break. Relative, backslash
  and wrong-case paths, `..` that stays *inside* the root
  (`Data/../Data/...`), leading `./`, absolute paths built from the egress
  values (`IMyGamePaths.ContentPath`, `IMyModContext.ModPath`), and a
  write/exists/read/delete round trip in local, world and global storage.

The audit these cover is
`~/dev/se1/pulsar-tests/se1/reports/security/mod-path-audit.md`.

`security-probes.txt` lists every security probe name a green run reports.
`parse_results.py --security-manifest` fails the run when one of them is
**missing**, not just when it fails - a probe deleted along with the check it
guards, or a suite that died before reaching that section, is a regression too.
After adding or renaming probes, regenerate and commit it:

```
python3 tests/mod-api/parse_results.py <suite.log> \
    --update-security-manifest tests/mod-api/security-probes.txt
```

## Log format

```
PASS [LINUX] name | expected=... | actual=...
FAIL [DOTNET] name | expected=... | actual=...
INFO name = value
=== SUMMARY pass=N fail=N linux_fail=N dotnet_fail=N info=N ===
=== END LinuxCompatDiagnostics ===
```

The `END` line is the terminator; without it the run died mid-suite (parser
exit code 2). All FAILs and the summary are mirrored to `SpaceEngineers.log`
so results survive a broken storage API.

## Notes

- Both harnesses hold an exclusive `flock` on `~/.cache/se-game.lock` while a
  game instance runs — the machine-wide advisory lock shared with the other
  automation sessions (auto-released if the holder dies). They wait up to 15
  minutes for it.
- DS provenance requirements (both were silent-failure modes): the Magnetar
  sources entry for `se-linux-compat` must point at the
  `~/dev/se1/se-linux-compat` symlink (the dev-folder plugin id is the folder
  basename, and only the id `se-linux-compat` overrides the implicit core
  plugin — otherwise the GitHub release is loaded), and the profile's
  `DataFile` must be `ServerPlugin/ServerPlugin.xml`.
- The compiled-mods cache clear in `run.sh` is REQUIRED: dev builds randomize
  the plugin assembly identity, and cached mods pin the stale assembly
  (world load then fails with `FileNotFoundException: LinuxCompat_...`).
- `run.sh` verifies a randomized `LinuxCompat_*` assembly name appears in the
  game log - proof the working tree was compiled, not a shipped plugin build.
- `TestData/CaseSensitivity/casepair.txt` (lowercase sibling of
  `CasePair.txt`) is generated at deploy time; a file pair differing only in
  case cannot be committed without breaking Windows checkouts.
- The mod ships a model + texture (copied from vanilla `GratedCatwalk`)
  referenced from `Data/DiagBlocks.sbc` with backslashes and deliberately
  wrong casing to exercise the definition/asset pipeline; a second definition
  is mutated at runtime to a Windows-shaped absolute path to exercise the
  `PathCache.ResolveAbsolute` ingress funnel.
- Dedicated server: the suite mod is API-compatible with the DS, but the DS
  rejects local mods in multiplayer and cannot download Workshop items
  offline; running it there needs the fake-Workshop registration procedure
  (content/244850/<id> + appworkshop ACF entries, crossplay off,
  `-noimplicitmod`). Not automated yet.

# Testing

The automated suites and what each one covers. Each suite has its own README
with the commands and environment it needs.

| Suite | Covers | Needs |
| --- | --- | --- |
| [tests/mod-api](../tests/mod-api/README.md) | The mod API boundary: Windows path, newline and stopwatch semantics for mods, path containment, and no Linux home folder in anything a mod sees. ~550 probes, of which 199 are security probes. Runs as a world mod, and on the client also as a Pulsar client mod. | The game client and the dedicated server |
| [tests/client-mods](../tests/client-mods/README.md) | Mod-location reads of a Pulsar client mod, a mod the world's mod list does not name: Build Info loaded as one must read its own textures, fonts and data files. | The game client, Pulsar and Build Info |
| [tests/physics-border](../tests/physics-border/README.md) | Bounded-world broad-phase border cleanup, through both `MyPhysics.CreateHkWorld` creation paths. | The game client |
| [tests/mod-location](../tests/mod-location/README.md) | A built plugin's mod location file API (`FileExistsInModLocation`, `ReadFileInModLocation`, `ReadBinaryFileInModLocation`) with backslash paths, for a world mod and a Pulsar client mod, and the mod items it refuses. | The game's `Bin64` |
| [tests/definition-file-order](../tests/definition-file-order/README.md) | `WindowsStringOrder`, the Windows sort order of the definition files, against data sorted by Wine's `CompareStringEx`. | Nothing; standalone |
| [tests/path-translation](../tests/path-translation/README.md) | The root mapping in `PathTranslation.Init()`, in both directions, round trips without the user name, and `WindowsException.Scrub`. | Nothing; standalone |
| [tests/rewriter](../tests/rewriter/README.md) | `CompilationRewriter` on small mod sources: mods naming the shims are refused, definition assignments used as values or in object initializers, and both with their config switches off. | Nothing; standalone |
| [tests/mod-corpus](../tests/mod-corpus/README.md) | A built plugin's `CompilationRewriter` on the scripts of every local Workshop mod: errors only the rewritten compilation has, and rewriter crashes. Compile only. | The game's `Bin64` and downloaded Workshop mods |
| [tests/steam-overlay](../tests/steam-overlay/README.md) | The Wayland Steam overlay input bridge. | Nothing for the basic checks; Steam and SDL3 for the optional live check |

Every suite that starts a game client runs it headless and holds an exclusive
`flock` on `~/.cache/se-game.lock`, the machine-wide lock shared with the other
automation on this machine, except `tests/client-mods`, which runs an isolated
client of its own. The dedicated server run of `tests/mod-api` takes no
lock; it runs on its own Magnetar config, data folder and ports instead.

## Mod files of client mods

Players can enable a mod in Pulsar instead of in the world. Pulsar compiles such
a client mod with a mod context of its own and never adds it to the session's
mods. 1.0.35 and 1.0.36 refused that context's `ModItem`, so a client mod found
none of its own files: Build Info lost its textures and fonts. Three checks cover
it:

- `tests/client-mods/run.sh` loads Build Info itself as a client mod in the
  game and fails on any of its file errors.
- `tests/mod-location/run.sh <ref>...` calls the file API of each ref's build
  with Build Info's backslash paths, for a world mod and a client mod. A broken
  build fails the 18 client mod checks.
- `tests/mod-api` with the mod staged as a client mod by
  `tests/mod-api/prepare_client_mod.py` (see "Client mod run" in its README).
  The `ReadFileInModLocation` section, the backslash `Textures\` probes
  included, fails on a broken build. Its `loaded as` line says which way the
  mod was loaded.

`tests/mod-corpus` can't catch it: the scripts compile the same either way, and
only the file lookup at run time differs.

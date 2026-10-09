# Testing

The automated suites and what each one covers. Each suite has its own README
with the commands and environment it needs.

| Suite | Covers | Needs |
| --- | --- | --- |
| [tests/mod-api](../tests/mod-api/README.md) | The mod API boundary: Windows path, newline and stopwatch semantics for mods, and path containment. ~410 probes, of which 149 are security probes. | The game client and the dedicated server |
| [tests/physics-border](../tests/physics-border/README.md) | Bounded-world broad-phase border cleanup, through both `MyPhysics.CreateHkWorld` creation paths. | The game client |
| [tests/definition-file-order](../tests/definition-file-order/README.md) | `WindowsStringOrder`, the Windows sort order of the definition files, against data sorted by Wine's `CompareStringEx`. | Nothing; standalone |
| [tests/path-translation](../tests/path-translation/README.md) | The root mapping in `PathTranslation.Init()`, in both directions. | Nothing; standalone |
| [tests/steam-overlay](../tests/steam-overlay/README.md) | The Wayland Steam overlay input bridge. | Nothing for the basic checks; Steam and SDL3 for the optional live check |

Every suite that starts a game instance runs it headless and holds an exclusive
`flock` on `~/.cache/se-game.lock`, the machine-wide lock shared with the other
automation on this machine.

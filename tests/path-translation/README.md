# Path translation tests

Verifies that `PathTranslation.Init()` maps the install the game is running from. The install
root is `MyFileSystem.RootPath`, which the launcher sets to the folder above the executable
directory before any plugin runs, so the plugin knows nothing about Steam or where the game
was installed. Standalone: it compiles `Shared/Patches/PathHandling/PathTranslation.cs`
verbatim against a fabricated `$HOME` and root path, so it needs neither the game nor Steam,
and touches nothing on disk. `IngressTraceStub.cs` replaces the real `IngressTrace`, which
reaches into `PathCache`, `PathHelpers` and `MyLog` and cannot compile outside the game;
`MyFileSystemStub.cs` provides the one `RootPath` field `PathTranslation` reads.

Not part of `LinuxCompat.sln` — it targets plain `net10.0` with no game references, and its own
`Directory.Build.props` shadows the repository root's, so the plugin's build-time game path
detection does not apply to the test build.

```bash
cd tests/path-translation && dotnet run -c Release
```

Exit code 0 means every check passed. Covered:

| Case | What it pins down |
| --- | --- |
| `under home` | An install inside `$HOME` beats the shorter home mapping; siblings still map to home |
| `outside` | An install outside `$HOME`; the exact prefix and a longer sibling folder name |
| `server` | An install root not named `SpaceEngineers` is used as given |
| `trailing slash` | A root path with a trailing separator |
| `no root` | `RootPath` unset: no install mapping, passthrough in both directions, nothing thrown |
| `other` | User data, temp, home and passthrough mappings are untouched |
| `unset home` | `$HOME` unset resolves to the real profile, not an assumed `/home/<user>` |

Translation is asymmetric, so most cases assert both directions.

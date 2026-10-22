# Mod location check

Calls the mod location file API of a built `LinuxCompat.dll`, the
`MyAPIGateway.Utilities` wrapper in
`Shared/Patches/PathHandling/ModApiWrappers/WrappedUtilities.cs`, without
starting the game:

- `FileExistsInModLocation`, `ReadFileInModLocation` and
  `ReadBinaryFileInModLocation`, with the backslash paths Build Info uses
  (`Textures\ActionIcons\ToggleEnabled.dds`, `Data\VanillaDefinitions.txt`,
  `Fonts\BI_SEOutlined\FontDataPA.xml`) and their forward slash forms
- for a world mod, which is in the session's mods, and for a client mod, which
  Pulsar compiles with a mod context of its own and never adds to the session's
  mods
- and the items the wrapper refuses: a Workshop item naming no loaded mod, the
  default item, and an item with the client mod's id whose rooted `Name` points
  at another folder

The harness sets up what the game holds after loading the mods, without the
game: the session's mods with the world mod, the script manager's contexts with
the client mod, the definition manager's mod folders with both, and
`MyFileSystem` on a temporary folder. It loads the plugin through reflection,
so it works with any build of it. The game assemblies come from `Bin64`.

```bash
tests/mod-location/run.sh origin/main .
```

It builds each git ref in a temporary worktree (`.` is the working tree),
prints each one's summary line and the folder with the full reports.
`BIN64` and `OUT` override the default `Bin64` and output folders. The exit code
is 0 when every check passes:

```
SUMMARY pass=40 fail=0
```

Builds from #81 (1.0.35) to 1.0.36 fail the 18 client mod checks: #81 made the
wrapper trust only the session's mods. Builds before #81 fail 2 refusal checks
instead, the escapes #81 closed.

# Pulsar client mod test

Pulsar can load Workshop mods on the client without the world listing them
(its "client mods"). It adds them to `MyDefinitionManager.LoadData` and compiles
their scripts with `MyScriptManager.LoadScripts`, but never to
`MySession.Static.Mods`. The mod-location readers in `WrappedUtilities` accept
only a `ModItem` of a loaded mod, so they have to recognize these mods too.
1.0.36 did not: every `FileExistsInModLocation`, `ReadFileInModLocation` and
`ReadBinaryFileInModLocation` of a client mod failed, and Build Info logged
`File not found in mod folder: Textures\...`, `The Data\VanillaDefinitions.txt
was not found in current mod's folder!` and `Error parsing font 'BI_SEOutlined'`
(DIS-0041).

`run.sh` is the known positive for that: it loads Build Info (Workshop
514062285) as a Pulsar client mod in the game's empty world, waits a minute, and
passes when Build Info logged no error and parsed its two fonts. On 1.0.36 it
fails with 35 Build Info errors: 32 missing textures, `VanillaDefinitions.txt`
and both fonts.

```bash
tests/client-mods/run.sh                  # this working tree
tests/client-mods/run.sh /path/to/tree    # another linux-compat source tree
```

Exit 0 = pass, 1 = Build Info logged errors, 2 = the run failed or did not
count: Pulsar did not load Build Info as a client mod, its scripts did not run,
or linux-compat was not compiled from the given tree.

To check an older version, export it next to the working tree, so its native
assets resolve the same way (`../../linux-dependencies`,
`../../linux-native-wrappers`):

```bash
mkdir -p ../v1036 && git archive 3601da7 | tar -x -C ../v1036
tests/client-mods/run.sh ../v1036
```

## What it needs

- Pulsar Interim in `PULSAR_SOURCE` (default `~/.config/Pulsar`) whose
  `Interim/Sources/sources.xml` registers the `remote` and `linux-compat` dev
  folder plugins, and whose current profile enables them. Clients started with
  `--client-id` also need DirectTransport.
- Build Info downloaded (`BUILD_INFO`, default the Steam Workshop folder), and
  the game in `GAME_DIR`.
- The se-remote skill checkout (`SE_REMOTE_DIR`).

## How it stays out of the way

It runs isolated, so it takes no game lock: a Pulsar instance of its own under
`TEST_ROOT` (default `~/.se-test/linux-compat-client-mods`; copied launcher,
symlinked `NuGet` and `Libraries`, copied plugin sources), its own game user
data folder, client id (`CLIENT_ID`) and Remote API port (`REMOTE_PORT`,
default 24170), `-multiInstance --no-steam`, headless and without rendering
unless `--render` is given. It stops only the processes started with its own
user data folder. The source Pulsar folder is only read. Each run clears the
instance's dev folder builds, so Pulsar compiles the tree under test again, and
turns on experimental mode in its own game config, which the game's worlds need
when the client renders.

Pulsar finds client mods in `Bin64/../../../workshop/content/244850`, worked out
from the path as given, so the instance gets a Steam library of its own:
`steamapps/common/SpaceEngineers` is a symlink to the game, and
`steamapps/workshop/content/244850/514062285` a copy of Build Info.

Results are in `TEST_ROOT/results`: the game log, Pulsar's log, Build Info's own
log (`buildinfo.log`) and the client's output.

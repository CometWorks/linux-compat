# Linux compatibility for Space Engineers (version 1)

This plugin contains the compatibility patches required to run Space Engineers
natively on Linux, without Proton or Wine. It covers both the game client
(loaded by Pulsar) and the dedicated server (loaded by Magnetar), so there is
nothing to install from this repository.

## Features

- The game runs natively on Linux, on both Wayland and X11.
  Set `SDL_VIDEODRIVER=x11` to force X11.
- The game's window (in windowed mode) can be resized freely and is compatible
  with display scaling.
- Fonts and icons are sharper than on Windows.
- Mods keep Windows semantics (backslash paths, a synthetic `C:\` drive, CRLF)
  while the game's internals use native Linux paths.

The loader compiles the plugin from source and loads it into the game's process
before the game starts. The plugin then replaces the Windows-only parts of the
engine at runtime. It never modifies any of the game's own files.

## Configuration

LinuxCompat keeps mods inside the folders they may use, as Windows does, and in
a few places it is stricter than Windows. If a mod you rely on breaks because of
one of these limits, turn off only the one it trips over. All of them are on by
default.

| Setting | Off means |
| --- | --- |
| Refuse unloaded mod items | A mod can read files through a `ModItem` of any mod, loaded or not, as on Windows. A crafted one can point at any folder. |
| Refuse leftover drive letters | Mod location and game content reads accept a path that still has a drive letter after translation, like `C:C:\...`, which can reach outside the folder. |
| Check serializer paths as opened | `MyObjectBuilderSerializer` checks the path translated once, which a leftover drive letter can get around. |
| Refuse mods using the rewriter shims | A mod may call LinuxCompat's rewriter helpers itself and read native paths with them. |
| Translate assignment values | An assignment to a definition path used as a value, like `p = (def.Model = x)`, gives the mod the native path. |

On the client, open the settings in game with Ctrl+Alt+/ and pick LinuxCompat.
They are saved to `Storage/LinuxCompat.cfg` in the game's user data folder. On
a dedicated server, edit them in Quasar, or in `LinuxCompat.cfg` in the
server's user data folder, which the plugin writes on its first start.

The last two settings change how mods are compiled, so they apply to mods
compiled after the change: reload the world or restart the server. The
Performance plugin's compiled mods cache does not know about these settings;
clear it after changing them.

## Documentation

- [Installation](Docs/Installation.md): prerequisites and the system packages
  your distribution has to provide
- [Architecture](Docs/Architecture.md): how the plugin works, covering the
  startup sequence, the native library shims, SDL, and path translation
- [Development](Docs/Development.md): building the plugin, versioning, and the
  diagnostic environment variables
- [Testing](Docs/Testing.md): the automated test suites

## Bug reports

Please start a support thread on the [Pulsar Discord](https://discord.gg/z8ZczP2YZY)

## Credits

- `SpaceGT` for his 50% contribution in helping to finish this plugin.
- `OwendB` for his relentless testing effort.
- `Linux123123` for the discussions and support in late 2024 (my first attempt on this).

## Legal

Space Engineers is a trademark of Keen Software House s.r.o.

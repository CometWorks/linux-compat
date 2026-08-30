# Linux compatibility for Space Engineers (version 1)

## Overview

This plugin contains the compatibility patches required to run Space Engineers
natively on Linux, without Proton or Wine. Pulsar applies it automatically right
after the `dotnet-compat` plugin, so there is nothing to install from this
repository.

## Features

- The game runs natively on Linux, on both Wayland and X11.
  Set `SDL_VIDEODRIVER=x11` to force X11.
- The game's window (in windowed mode) can be resized freely and is compatible
  with display scaling.
- Fonts and icons are sharper than on Windows.
- Mods keep Windows semantics (backslash paths, a synthetic `C:\` drive, CRLF)
  while the game's internals use native Linux paths. See
  [ARCHITECTURE.md](ARCHITECTURE.md).

## Prerequisites

- [Space Engineers](https://store.steampowered.com/app/244850/Space_Engineers/) installed by Steam for Linux 
  (standard installation for Proton)
- [Pulsar](https://github.com/SpaceGT/Pulsar/),
  which downloads and applies this plugin for you

## How it works

Pulsar loads the plugin into the game's process before the game starts, and the
plugin uses Harmony patches to replace the Windows-only parts of the engine at
runtime, without modifying any of the game's own files.

It redirects the calls bound for Windows DLLs to their Linux counterparts, so
DirectX 11 runs on DXVK over Vulkan and the OpenAL, Steamworks and Epic SDKs
come from their Linux builds. The game's own native libraries (`Havok`,
`VRage.Native` and `RecastDetour`) have no Linux build, so the original Windows
binaries keep running behind Linux shim libraries that convert between the
Windows and Linux ABIs.

An SDL implementation takes over the Win32 window, input, cursor and clipboard
code, and video playback runs through FFmpeg instead of DirectShow.

Linux filesystems are case-sensitive and use a different separator, so the
plugin resolves the casing of the paths the game builds and keeps mods
believing they still run on Windows.

Differences that are not Linux-specific, like the ones between .NET Framework
and the .NET 10 runtime the game now uses, show up on Windows too and belong to
the separate `dotnet-compat` plugin, which Pulsar applies first.

## Development

Local build paths (`Bin64`, `DS64`, `Pulsar`, `Magnetar`, `Steamworks`,
`Dependencies`, `Wrappers`) are empty in `Directory.Build.props`. To override
them, copy its first `PropertyGroup` into `Directory.Build.props.user`
(git-ignored) in the repo root, wrapped in a top-level `<Project>` element, and
fill in your paths.

`Bin64` and `DS64` are auto-detected from Steam if left empty.

`Shared/` compiles into both `ClientPlugin` and `ServerPlugin`; verify that both
build. Format the code with `csharpier` before committing.

## Testing

`tests/mod-api/` holds the automated mod API boundary suite (client and
dedicated server harnesses). See [tests/mod-api/README.md](tests/mod-api/README.md).

## Bug reports

Please start a support thread on the [Pulsar Discord](https://discord.gg/z8ZczP2YZY)

## Credits

- `SpaceGT` for his 50% contribution in helping to finish this plugin.
- `OwendB` for his relentless testing effort.
- `Linux123123` for the discussions and support in late 2024 (my first attempt on this).

## Legal

Space Engineers is a trademark of Keen Software House s.r.o.

# Linux compatibility for Space Engineers (version 1)

This plugin contains the compatibility patches required to run the Game on Linux. This plugin is
applied automatically by Pulsar for Linux right after the `dotnet-compat` plugin.

## Prerequisites

- Steam for Linux
- [Space Engineers](https://store.steampowered.com/app/244850/Space_Engineers/) installed by Steam for Linux (standard installation for Proton)

## Installation

Download the Native or the Flatpak build from [Releases](https://github.com/viktor-ferenczi/se-linux-compat/releases),
install and run it. Please read the release notes to decide which one is the best for your situation. 

## Features

- With the plugin, the game will run natively on Linux and does not use Proton/Wine at all.
- The game's window (in windowed mode) can be resized freely and is compatible with display scaling.
- Runs on native Wayland and X11. Set `SDL_VIDEODRIVER=x11` to force X11.
- Fonts and icons are sharper than on Windows.

## Development

Local build paths (`Bin64`, `DS64`, `Pulsar`, `Magnetar`, `Steamworks`, `Dependencies`, `Wrappers`)
are empty in `Directory.Build.props`. To override them, copy its first `PropertyGroup` into
`Directory.Build.props.user` (git-ignored) in the repo root, wrapped in a top-level
`<Project>` element, and fill in your paths.

`Bin64` and `DS64` are auto-detected from Steam if left empty.

## Path translation architecture

Mod code must believe it runs on Windows while the game's internals use native
Linux paths. All translation happens at the mod API boundary; internal game
methods are never patched just to accept mod-shaped paths.

- **Path mapping at mod call sites**: the Roslyn rewriter
  (`Shared/Rewriter/WindowsSemanticsRewriter.cs`) injects the translation into
  compiled mod code, so the mapping applies only to mods — plugins and engine
  code calling the same Mod API members keep native paths. Reads of
  path-returning members (`IMySession.CurrentPath`/`ThumbPath`,
  `IMyGamePaths.*`, `IMyConfigDedicated.PremadeCheckpointPath`/`GetFilePath`,
  `IMyModContext`/`MyModContext.ModPath`/`ModPathData`, `IMyModel.AssetName`,
  `ModItem.GetPath()`) are wrapped in `WindowsPath.FromGame`; path-accepting
  arguments and setters (`IMyUtilities` file readers,
  `IMyConfigDedicated.Load`/`Save`/`PremadeCheckpointPath`, `IMySession.Save`)
  are wrapped in `WindowsPath.ToGame`.
- **Windows semantics in mod code**: the same rewriter redirects
  `System.IO.Path` (including `using static` imports), `Environment.NewLine`,
  `Stopwatch`, `StringBuilder.AppendLine`, and `TextWriter.WriteLine` to
  Windows-behaving shims during mod compilation.
- **Windows behavior emulation for all API callers**: `WrappedUtilities`
  (`Shared/Patches/PathHandling/ModApiWrappers/`) wraps
  `MyAPIGateway.Utilities` for storage filename validation, CRLF XML
  serialization, and filesystem casing resolution — behavior, not path
  mapping.
- **Ingress for shared data**: paths a mod writes into shared mutable data
  (definitions, object builders) cannot be intercepted at a call boundary. They
  are restored by exactly one sanctioned funnel, `PathCache.ResolveAbsolute`
  (also injected into `MyFileSystem.Open`), which owns the drive-letter check,
  separator normalization, and on-disk casing resolution.
- **Diagnostics**: `SE_LINUX_COMPAT_TRACE_INGRESS=1` logs every site that still
  receives a drive-prefixed path with its calling chain; debug builds always
  flag conversions happening outside the sanctioned funnel.

Issue ownership between the two plugins Pulsar applies: problems caused by
.NET 10 (they reproduce on Windows too) belong to `dotnet-compat`;
Linux-only problems belong to this repo. `dotnet-compat` runs first and also
rewrites failing mod compilations, replaces the script whitelist bootstrap, and
sorts `MyFileProviderAggregator.GetFiles`; this plugin's rewriter, shim
whitelist batch, and `MyFileSystem.GetFiles` sorting operate at different
layers and must both stay in place.

## Bug reports

Please start a support thread on the [Pulsar Discord](https://discord.gg/z8ZczP2YZY)

## Credits

- `SpaceGT` for his 50% contribution in helping to finish this plugin.
- `OwendB` for his relentless testing effort.
- `Linux123123` for the discussions and support in late 2024 (my first attempt on this).

## Legal

Space Engineers is a trademark of Keen Software House s.r.o.

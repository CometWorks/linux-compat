# Development

Building the plugin from this repository, and the knobs that help while
debugging it. [Architecture.md](Architecture.md) covers how the plugin works.

## Local build paths

The build paths (`Bin64`, `DS64`, `Pulsar`, `Magnetar`, `Steamworks`,
`Dependencies`, `Wrappers`) are empty in `Directory.Build.props`. To override
them, copy its first `PropertyGroup` into `Directory.Build.props.user`
(git-ignored) in the repo root, wrapped in a top-level `<Project>` element, and
fill in your paths.

`Bin64` and `DS64` are auto-detected from Steam if left empty. `Pulsar` and
`Magnetar` are never auto-detected. Leave them empty and load the working copy
through a loader development source; a deployed copy shows up as a separate
local plugin and can shadow the published one. Setting them, in the `.user`
file or on the command line, enables the post-build deployment into those
plugin loader folders:

```bash
dotnet build LinuxCompat.sln -c Debug -p:Pulsar=$HOME/.config/Pulsar
```

`Shared/` compiles into both `ClientPlugin` and `ServerPlugin`, so verify that
both build. Format the code with `csharpier` before committing.

## Versioning

The plugin version appears in three places that must stay in step:

- `AssemblyVersion` / `FileVersion` in `Directory.Build.props`, used by the
  csproj builds of both projects.
- `[assembly: AssemblyVersion]` in `ClientPlugin/Plugin.cs`, used when Pulsar
  compiles the client plugin from source.
- `[assembly: AssemblyVersion]` in `ServerPlugin/Plugin.cs`, used when
  Magnetar compiles the server plugin from source.

The attributes are guarded with `#if !LOCAL_BUILD`, which both csproj files
define and neither loader does, so exactly one of the two sources applies to
any given build.

`<Commit>TODO</Commit>` in the plugin manifests is a placeholder. PluginHub and
MagnetarHub carry the real commit in their own copy of the manifest.

## Diagnostic environment variables

None of these are needed for normal play; they exist for isolating defects.

| Variable | Effect |
| --- | --- |
| `SDL_VIDEODRIVER` | Read by SDL3 itself; also restricts the startup dependency check to that driver (`x11`, `wayland`, or a headless driver such as `offscreen`). |
| `PULSAR_NO_RENDER` | Set in-process by Pulsar, not on the command line: disables the DXVK and SDL window paths. Plain `--headless` keeps offscreen rendering and does not set it. |
| `SE_HAVOK_SEQUENTIAL=1` | Step Havok clusters sequentially on the main thread instead of draining the job queue from many threads. |
| `SE_HAVOK_SINGLETHREAD=1` | Additionally step each world single-threaded (implies sequential). |
| `SE_HAVOK_NO_FINALIZE=1` | Suppress the `HkHandle` finalizer, leaking the native objects instead of releasing them from the finalizer thread. |
| `SE_SHADER_OVERRIDE` | Directory mirroring the game's shader tree; a shader source found there is compiled instead of the game's own. |
| `SE_LINUX_COMPAT_TRACE_INGRESS=1` | Report every internal site that still receives a drive-prefixed mod path, with the stack that delivered it. |
| `SE_LINUX_COMPAT_FORCE_HKWORLD_PREFIX=1` | Force every `HkWorld` through the patch's replacement creation path (used by the physics border test). |

## Scope

Differences that are not Linux-specific belong elsewhere. The game runs on
.NET 10, and anything that also reproduces on Windows (`Encoding.Default`,
missing codepages, ICU collation, culture formatting) is owned by the
`dotnet-compat` plugin, which both loaders apply first. A bug that reproduces
on Windows is filed against the `dotnet-compat` repository, not this one. The
mod API suite tags every probe with the owning plugin for exactly this reason;
see [tests/mod-api/README.md](../tests/mod-api/README.md).

The other rule that decides where a change goes is the path translation
boundary, described in
[Architecture.md](Architecture.md#paths-and-mod-semantics): never patch an
internal game method just to make it tolerate a mod-shaped path.

## Documentation

`README.md` is the user-facing overview. Keep it short enough to skim: what the
plugin is, what it gives you, and links. Everything else goes in this folder
(`Installation.md`, `Architecture.md`, `Development.md`, `Testing.md`), and
each document opens with one or two sentences on what it covers. A test suite
documents itself in its own `tests/<suite>/README.md`.

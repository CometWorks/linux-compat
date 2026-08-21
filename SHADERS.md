# Shader compilation findings

## Verdict

Vanilla Space Engineers can pause when it encounters a shader permutation that
is absent from both of its shader caches. That behavior is part of the vanilla
renderer. The roughly 25-second first-world pause observed on Linux is not a
reasonable vanilla-equivalent case, however: the Linux compatibility patch
computes a different cache key and cannot use any of the 1,724 shader bytecode
entries shipped with the game.

The primary fix is to restore byte-for-byte compatibility with vanilla's HLSL
preprocessing and shader-cache lookup. Raising the file descriptor limit does
not address this pause and is not justified by the evidence collected here.

## Scope

The captured run used Space Engineers `1.209.024 b0`, the current LinuxCompat
shader patch, DXVK `2.7.1+`, and the NVIDIA `610.57.4` driver. The prepared
decompiled source is `1.210.014 b0`. `MyShaderCompiler` is unchanged between
those builds; `MyShaderCache` only gained an early check for a missing cache
pair, which was already handled as an `IOException` in `1.209.024`.

A native Windows run was not available on this machine. Vanilla behavior was
verified from decompiled builds `1.209.024` and `1.210.014` and from the
installed vanilla `Content/ShaderCache`, not from a Windows timing comparison.
This is enough to establish the cache and blocking behavior, but not a Windows
wall-clock benchmark.

## Vanilla shader path

Vanilla performs these steps for a shader request:

1. Resolve the HLSL file below `Content/Shaders`.
2. Add the global and request-specific macros.
3. Run `D3DPreprocess`, including all referenced HLSL files and applying
   preprocessor directives.
4. Hash the complete preprocessed text together with the shader profile.
5. Look first in the shipped `Content/ShaderCache`, then in the user's
   `ShaderCache2`.
6. On a miss, call `D3DCompile` synchronously, store the resulting DXBC in
   `ShaderCache2`, and log `Shader was not precompiled`.
7. Create the D3D11 shader object synchronously on the calling thread.

The relevant vanilla code is in the prepared game-code reference:

- `VRage.Render11/VRageRender/MyShaderCompiler.cs:192-245,291-419`
- `VRage.Render11/VRage/Render11/Shader/MyShaderCache.cs:45-55,160-244`
- `VRage.Render11/VRageRender/MyVertexShaders.cs:57-134`
- `VRage.Render11/VRageRender/MyPixelShaders.cs:63-140`

The installed game contains 1,724 matching `.hash`/`.cache` pairs in
`Content/ShaderCache`. The user cache contained 379 pairs after the tests.

### What one cache entry represents

An entry is one compiled shader stage for one exact preprocessed output and
profile. It is not a material, draw call, or complete Vulkan pipeline. A
material pass usually needs at least a vertex and pixel shader entry, and the
same HLSL file produces many entries for render passes, vertex layouts, texture
combinations, skinning, instancing, alpha masking, and other macros. Different
macro arrays can share an entry if preprocessing reduces them to identical
text.

The shipped cache has this profile distribution:

| Profile | Stage | Entries |
| --- | --- | ---: |
| `vs_5_0` | Vertex | 639 |
| `ps_5_0` | Pixel | 937 |
| `gs_5_0` | Geometry | 19 |
| `cs_5_0` | Compute | 129 |
| Total | | 1,724 |

Each entry is a pair named
`<32-hex-hash>.<profile>.hash` and
`<32-hex-hash>.<profile>.cache`:

- The filename stem starts as MD5 of the complete preprocessed source. Each
  digest byte is XORed with the numeric `MyShaderProfile` value before it is
  printed as hexadecimal. The profile also appears in the extension.
- The `.hash` file starts with `CacheCrc=<32-hex-MD5>\n`, followed by a gzip
  stream containing the complete preprocessed HLSL text. Despite the name,
  this file stores the source, not only its hash.
- The `.cache` file has the same header followed by raw DXBC bytecode. Its
  first four payload bytes are `DXBC`.
- `CacheCrc` is an MD5 of the DXBC payload, despite being called a CRC. Both
  files repeat it so a torn or mismatched pair is rejected.

`TryFetch` does more than look for the filename. It checks that both files
exist, decompresses the stored source, requires an exact text match, recomputes
the DXBC MD5, and requires both headers to match it. A missing, corrupt, stale,
or hash-colliding pair is deleted and treated as a miss. This validation is why
using the shipped bytecode without reproducing vanilla's preprocessed source is
not safe.

The lookup order is always shipped cache first, then user cache. A shipped hit
does not need to be copied into `ShaderCache2`. A genuine miss writes a new user
pair that can be reused by later processes as long as the preprocessed output
and profile remain identical.

### Entry sizes

The post-test cache inventories differed sharply:

| Cache | Pairs | Average `.hash` | Average `.cache` | Disk usage |
| --- | ---: | ---: | ---: | ---: |
| Shipped `Content/ShaderCache` | 1,724 | 12,121 B | 8,624 B | 41 MiB |
| Linux user `ShaderCache2` | 379 | 1,032 B | 731,581 B | 267 MiB |

The installed shipped entries were generated as optimized shaders and stripped
of debug, reflection, test, and private blobs. Runtime misses are debug builds,
so even a vanilla-generated user entry is expected to be much larger than a
shipped entry. The full 85-fold bytecode-size difference cannot be assigned to
LinuxCompat alone.

LinuxCompat does make the gap worse. Its `.hash` payload is small because it
stores only macros plus the root file, while `D3DCompile` receives a separately
expanded source that is not stored in the entry. The regex expander expands
includes before evaluating conditional directives and repeats shared include
trees reached by different paths. Examples without macro-dependent pruning:

| Root shader | Root size | Compiler input after regex expansion | Include expansions |
| --- | ---: | ---: | ---: |
| `Standard/Vertex.hlsl` | 483 B | 178,663 B | 32 |
| `Standard/Pixel.hlsl` | 1,857 B | 711,785 B | 124 |
| `Glass/Pixel.hlsl` | 3,702 B | 948,143 B | 163 |

By comparison, vanilla's shipped preprocessed sources average 59,229 bytes and
the largest installed one is 97,219 bytes. The sets are not one-to-one, but the
scale shows why the regex expansion is expensive. Debug DXBC can also embed
source data: a sampled Linux entry contained an `SPDB` blob and Microsoft PDB
data, while a sampled shipped entry did not.

A representative vertex-shader compile using the captured 179,802-byte
expanded input produced 415,552 bytes with LinuxCompat's
`DEBUG | SKIP_OPTIMIZATION` flags and 397,900 bytes with vanilla's `DEBUG`
flag. The former took about 30 ms and the latter about 39 ms in this isolated
test. Thus the flag mismatch matters for correctness and output size, but
`SKIP_OPTIMIZATION` is not the main cause of the observed compile pause. The
cache miss and oversized include input are higher-priority fixes.

Vanilla cache misses can still cause visible hitches:

- Initial renderer setup creates shaders synchronously. Vanilla Windows sets
  `UseParallelRenderInit` to `false` in
  `VRage.Platform.Windows/.../MyWindowsRender.cs:23`.
- Some material shaders are first requested during scene updates and can block
  the render thread.
- GeometryStage2 model jobs request shaders from worker threads. Those jobs do
  not make compilation asynchronous; they only move the synchronous request to
  a worker and can still contend for CPU or be awaited later.
- The renderer's per-hash `InProgressMonitor` prevents two threads from
  compiling and writing the same permutation concurrently.

Therefore, a short vanilla first-use hitch for a genuinely unshipped
permutation is expected. A cold run recompiling common shipped permutations is
not.

## Linux cache incompatibility

`ClientPlugin/Patches/Rendering/ShaderCompilerPatch.cs` replaces vanilla's
lower `MyShaderCompiler.Compile` overload. It constructs the cache source as:

```text
macro #defines + unprocessed root HLSL file
```

It hashes and queries the vanilla cache with that text. Only after the cache
miss does `ClientPlugin/Compatibility/Rendering/D3DCompilerLinux.cs` recursively
expand `#include` directives for `D3DCompile`.

This differs from vanilla in several important ways:

1. The cache key is based on source that still contains `#include` directives.
   Every one of the 1,724 shipped `.hash` files contains fully preprocessed
   source and none still contains an `#include` directive.
2. Changes to an included file do not change LinuxCompat's cache key. Existing
   user bytecode can therefore remain selected after a shared include changes.
3. The regex include expander does not evaluate conditional preprocessing. It
   expands includes even in inactive branches, can duplicate large include
   trees, and cannot reproduce `D3DPreprocess` output or its `#line`
   directives.
4. The prefix bypasses vanilla's per-hash `InProgressMonitor`, allowing
   concurrent requests for the same uncached hash to compile and write the same
   two cache files concurrently.
5. Compile flags differ. For normal runtime requests, vanilla uses
   `D3DCOMPILE_DEBUG`; LinuxCompat also sets
   `D3DCOMPILE_SKIP_OPTIMIZATION`. For optimized tool requests, vanilla uses
   `DEBUG | OPTIMIZATION_LEVEL3`, while LinuxCompat omits `DEBUG`. This changes
   compile cost and can change the generated DXBC and its runtime performance.
6. The prefix logs a cache miss before compiling, then vanilla's caller logs
   the same miss after the prefix returns. The warnings are duplicated; the
   shader compilation itself was not duplicated in the captured run.

Runtime evidence confirms the mismatch:

| Measurement | Result |
| --- | ---: |
| Warning records | 234 |
| Unique LinuxCompat hashes | 117 |
| Occurrences per hash | exactly 2 |
| Runtime hashes found in shipped cache | 0 of 117 |
| Runtime hashes found in user cache after the run | 117 of 117 |
| Shipped `.hash` files still containing `#include` | 0 of 1,724 |
| Sample LinuxCompat user `.hash` files containing `#include` | yes |

All duplicate warning pairs had the same renderer-log thread ID. The first
record is emitted by `ShaderCompilerPatch.cs:119-124`; the second is emitted by
the unchanged vanilla caller at `MyShaderCompiler.cs:231-235`. Their timestamp
gap is a useful approximation of HLSL-to-DXBC compilation plus cache-write
time, not evidence of two compilations.

## Captured freeze

The first world load had the following timeline:

| Event | Time |
| --- | --- |
| Loading screen started | 14:17:46.371 |
| `RunLoadingAction` started | 14:17:46.571 |
| `Session loaded` | 14:17:59.592 |
| First shader-miss warning | 14:18:04.423 |
| Loading screen unloaded | 14:18:04.664 |
| HUD loaded | 14:18:04.667 |
| Banner-download log entry | 14:18:04.921 |
| Dense shader-miss burst ended | 14:18:29.110 |
| GC log entry | 14:18:29.630 |

The dense burst contained 79 actual shader misses and lasted 24.687 seconds.
After the banner-download line, the game log was quiet for 24.709 seconds. This
strongly correlates the visible stale 100% frame with first-scene shader work,
although neither the game nor DXVK log records the first successfully presented
world frame.

Across all 117 unique misses, the duplicate-warning gaps were:

| Statistic | Duration |
| --- | ---: |
| Minimum | 0.017 s |
| Median | 0.047 s |
| 90th percentile, linear interpolation | 0.377 s |
| Maximum | 2.794 s |
| Sum | 18.182 s |

The sum is not wall-clock stall time because requests overlap across threads.
The live dump also suspended or slowed the target for about 2.4 seconds, so it
perturbed the observation but cannot explain the full interval.

The second world load in the same process did not repeat any first-world hash
at its loading transition. It reached `Session loaded` in 9.799 seconds versus
13.018 seconds for the first load. Eight new permutations appeared about 82
seconds later after additional model/UI activity. This demonstrates effective
in-process shader reuse. It does not prove that `ShaderCache2` works across
processes, and it does not make the cold-cache behavior acceptable.

## Interaction with DXVK

Space Engineers' shader compiler and DXVK's compiler are separate consecutive
stages:

```text
HLSL source
  -> d3dcompiler_47.dll / D3DCompile
  -> Direct3D bytecode (DXBC)
  -> ID3D11Device::Create*Shader
  -> DXVK DXBC parsing and SPIR-V generation
  -> Vulkan shader pipeline libraries and linked pipelines
  -> NVIDIA driver machine code and driver disk cache
```

The Space Engineers cache stores DXBC. A hit avoids the first stage but DXVK
still has to consume that DXBC. The caches are not interchangeable.

In the upstream DXVK 2.7.1 baseline:

- D3D11 shader creation parses DXBC and generates SPIR-V synchronously in the
  calling application thread. See
  `src/d3d11/d3d11_shader.cpp:10-82` in the matching DXVK source.
- Registering a shader queues a Vulkan shader pipeline-library compile when
  graphics pipeline libraries are supported and `canPrecompileShader` accepts
  that shader. See
  `src/dxvk/dxvk_pipemanager.cpp:307-315`.
- This run had graphics pipeline libraries enabled and 12
  `dxvk-shader-{h,n,l}` worker threads.
- A first draw can still synchronously acquire or create a missing pipeline
  library and link a base pipeline. See
  `src/dxvk/dxvk_shader.cpp:1376-1463` and
  `src/dxvk/dxvk_graphics.cpp:1063-1093,1313-1365`.

This produces three kinds of pause:

1. **Game compiler pause:** `D3DCompile` and Space Engineers cache I/O block the
   thread requesting an uncached shader. This is the work directly measured by
   the paired warnings.
2. **DXVK translation pause:** DXVK 2.7.1 translates new DXBC on the requesting
   thread during `Create*Shader`.
3. **Vulkan/driver pipeline pause:** DXVK normally compiles shader pipeline
   libraries on 12 low-priority workers, causing high CPU load. If first use
   wins the race, the render path compiles or waits synchronously. Pipeline
   linking can also block briefly.

DXVK 2.0 deliberately moved shader pipeline-library compilation toward D3D
shader creation to reduce draw-time stutter. Its release notes warn that games
which load many shaders can spend longer at high CPU during loading. That is
helpful when the application loads a finite set. Restoring the Space Engineers
cache will not remove the corresponding DXVK jobs because cache hits still call
`Create*Shader`. It will remove the unnecessary `D3DCompile` and cache-write
work competing with those jobs.

DXVK 2.7 removed its legacy state cache because graphics pipeline libraries
largely replaced it. The absence of a `.dxvk-cache` file in this run is
expected. The NVIDIA driver cache was present below
`~/.cache/nvidia/GLCache`; it is a separate final-machine-code cache.

DXVK 3.0 moves DXBC-to-SPIR-V work to workers and adds its own serialized shader
IR cache. It may reduce application-thread stalls, but upgrading DXVK is not a
substitute for restoring Space Engineers' shipped cache. It also raises driver
requirements and should be evaluated separately after the application cache is
fixed.

## File descriptor limits

There is no evidence that the current shader path hit or approached the file
descriptor limit:

| Measurement during shader activity | Result |
| --- | ---: |
| Open descriptors | 429 |
| Soft `RLIMIT_NOFILE` | 524,288 |
| Hard `RLIMIT_NOFILE` | 524,288 |
| Limit utilization | 0.082% |
| `EMFILE` log entries | 0 |
| `Too many open files` log entries | 0 |

The 429 descriptors included 309 regular/path-backed files, 54 NVIDIA device
descriptors, 27 anonymous inodes, 12 sockets, eight `memfd` objects, eight
pipes, five DMA buffers, and other devices. This is total process usage, not
shader-compiler usage.

The managed shader path does not retain file handles:

- `File.ReadAllText`, `File.ReadAllBytes`, and cache `FileStream` instances are
  closed by their APIs or `using` scopes.
- `Directory.EnumerateFileSystemEntries` is disposed by `foreach`.
- The custom recursive include expander reads one complete file at a time; it
  does not keep the include tree open.
- Vanilla's `MyIncludeProcessor.Close` closes every include stream supplied to
  D3DCompiler.
- Both D3DCompiler result blobs are released in `finally`.

A standalone stress run of the current native wrapper performed 2,000
successful and failing compilations. Its descriptor count remained four before
initialization, during compilation, and after blob release. This does not prove
that no future compiler input can trigger a native bug, but it disproves a
simple per-compilation descriptor leak.

Reports of `eventfd: Too many open files` under Proton commonly involve Wine
esync/fsync descriptors. LinuxCompat runs the game directly on .NET without
Wine or Proton, so those reports do not describe this compiler path.

### Recommendation on limits

Do not raise or override `RLIMIT_NOFILE` as part of this fix. The current limit
has more than three orders of magnitude of headroom. A higher limit would not
fix a leak and there is no measured legitimate demand for it here.

If an actual user report contains `EMFILE`, first record the process limit and
sample `/proc/$PID/fd` by target while shaders compile. Use `strace` on
`openat`, `close`, `eventfd2`, `pipe2`, and `socket` if the count grows. Raise a
low inherited limit only when descriptor usage is stable and demonstrably
legitimate; fix the owner if usage grows without bound.

## Required fix

The correct target is vanilla cache compatibility, not faster ad-hoc runtime
compilation.

1. Extend `libD3DCompiler.so` to expose the loaded
   `d3dcompiler_47.dll`'s `D3DPreprocess` export. The installed DLL already
   exports `D3DPreprocess`; the native wrapper currently exposes only
   `SE_D3DCompile` and blob access/release functions.
2. Provide include handling with the same local-parent and shader-root search
   semantics as vanilla `MyIncludeProcessor`. To match SharpDX
   `PreprocessFromFile` and the shipped cache, `D3DPreprocess` must receive the
   original HLSL, macro array, and an empty source name. The native include
   handler must retain the real source filepath separately. The resulting text
   must include the same macro evaluation, include expansion, and `#line`
   directives as vanilla. The later `D3DCompile` call still receives the real
   filepath as its source name.
3. Use that exact preprocessed blob for `MyShaderCache.GetShaderHash`,
   `TryFetch`, and `Store`. Remove the regex include expander from the cache
   identity path.
4. Compile a miss with the same source, macros, include semantics, profile, and
   flags as vanilla, including `D3DCOMPILE_DEBUG` and without adding
   `D3DCOMPILE_SKIP_OPTIMIZATION` to normal runtime requests. Prefer restoring
   the vanilla method shape over replacing the whole method if the native
   boundary can support it.
5. Preserve vanilla's per-hash exclusion around lookup, compile, and store.
6. Remove the prefix's warning. Let the unchanged vanilla caller emit one
   warning after a genuine miss.

Microsoft documents that `D3DPreprocess` emits `#line` directives and accepts a
custom `ID3DInclude`. A home-grown textual expander cannot be expected to
produce compatible cache bytes.

## Mitigation options

These options are ordered by how much unnecessary work they remove.

### 1. Restore shipped-cache compatibility

Implement the required fix above first. It eliminates HLSL compilation for
common shipped permutations and feeds DXVK the small optimized DXBC already
provided by the game. This addresses both the stale loading frame and the large
Linux user cache at their source.

Simply copying `Content/ShaderCache` into `ShaderCache2`, skipping source
validation, or looking up entries by filename is ineffective or unsafe. The
game already checks the shipped directory first, and LinuxCompat does not
currently calculate those filenames or source bytes.

### 2. Stop expanding inactive and repeated include trees

Use the real D3D preprocessor and a native include implementation for both
cache identity and compilation. This also improves genuinely uncached shaders
from mods or shader overrides. It reduces the compiler input, keeps include
changes in the key, and preserves conditional include behavior.

Using `D3D_COMPILE_STANDARD_FILE_INCLUDE` alone is unlikely to be compatible.
Vanilla searches relative to the including file and then the global shader
root, including case-insensitive Windows paths. Rewriting includes to absolute
Linux paths would also alter `#line` output and therefore miss the shipped
cache. The native include handler needs to reproduce vanilla's behavior.

### 3. Preserve vanilla flags and serialization

Restore vanilla's compile flags and per-hash exclusion. This prevents
concurrent writes and keeps generated DXBC behavior consistent with Windows.
Changing flags alone is not expected to remove the current pause; the isolated
sample made `DEBUG` compilation slower than `DEBUG | SKIP_OPTIMIZATION`.

If torn files are observed, write each part to a temporary file before renaming
it. This prevents either individual file from becoming partially visible, but
it does not make the two-file pair atomic. Existing source and MD5 validation
will reject a pair interrupted between renames. A new single-file format or
commit marker would be needed for true pair atomicity and is not justified by
the current evidence.

### 4. Strip non-executable data from genuine misses

Expose `D3DStripShader` through `libD3DCompiler.so` and strip debug,
reflection, test, and private blobs before `Store` and `Create*Shader` during
normal release gameplay. These are the same categories vanilla removes from
the shipped optimized entries, which DXVK already accepts. Keep unstripped
bytecode only when shader debugging is explicitly required.

This does not reduce HLSL compile time, but it can sharply reduce user-cache
size, cache-write I/O, bytecode hashing, and the data DXVK must inspect. Measure
the result on genuine mod and override misses and run rendering checks before
enabling it by default.

### 5. Handle the existing user cache safely

Until the key includes active include content, invalidate the Linux user cache
after a game shader update or an `SE_SHADER_OVERRIDE` change. Otherwise an
include-only change can leave the stored root text and hash unchanged and
return stale DXBC. A safe interim code fix is to key user entries on the exact
regex-expanded compiler input plus macros and profile. That still cannot use
the shipped cache, but it removes the stale-include bug while the native
preprocessor is being implemented.

After the preprocessing fix, keep `ShaderCache2` and the NVIDIA shader cache
across normal starts. They remove work on later runs, although each cache covers
a different stage. The old LinuxCompat entries will no longer match and can be
removed once by recognizing `.hash` payloads that still contain unresolved
`#include` directives. Do not clear unrelated valid entries.

The NVIDIA cache should not be given an unlimited size by default. Increase
`__GL_SHADER_DISK_CACHE_SIZE` or disable cleanup only after logs or repeated-run
measurements show eviction. No eviction problem was established in this run.

### 6. Reduce DXVK contention if it remains after the cache fix

DXVK currently uses all 12 available compiler threads. A local
`dxvk.numCompilerThreads` value such as 4 or 6 can leave CPU time for the render
and game threads. This may improve responsiveness but extends pipeline warm-up
and can move stutter later into gameplay. It should be tested, not made the
default based on this one run.

`dxvk.enableGraphicsPipelineLibrary = True` does not stop the shader
pipeline-library jobs queued by `registerShader`, so it does not target the
12-worker burst seen when shaders are created. It only suppresses later
background compilation of optimized full pipelines and may reduce steady-state
performance. It is a debugging control, not a recommended fix for this pause.

### 7. Evaluate DXVK 3 separately

DXVK 3 moves DXBC-to-SPIR-V conversion to workers and adds a persistent shader
IR cache. It may reduce application-thread stalls and repeat translation work.
It cannot repair Space Engineers cache misses and still leaves first-use Vulkan
pipeline and driver work. Upgrade only after checking native-DXVK integration,
Vulkan 1.4 driver requirements, rendering correctness, and repeat-run timing.

### 8. Improve the loading transition only as a last resort

Holding the loading screen until known shader and DXVK queues drain would hide
the stale 100% frame, but it would not reduce work and may lengthen every load.
Prewarming every possible permutation has the same problem and scales poorly.
Consider either only if a measured pause remains after shipped-cache hits work.

## Verification plan

Use an isolated empty `ShaderCache2`; do not delete the user's cache.

1. Compare native-preprocessor output byte-for-byte with several installed
   `Content/ShaderCache/*.hash` payloads covering vertex, pixel, compute, local
   includes, root includes, and macro-heavy material shaders.
2. Load the same Quick Start world with an empty user cache. Common
   permutations should hit `Content/ShaderCache`; only genuinely unshipped
   permutations may log one warning each.
3. Confirm that changing active preprocessed content in an included `.hlsli`
   file changes the hash and cannot return stale bytecode.
4. Request the same cold hash concurrently and confirm one compile, one cache
   write pair, and cache hits for followers.
5. Record `DXVK_HUD=compiler` or equivalent counters while correlating game
   warning timestamps with first world-frame presentation. This separates game
   compiler work from remaining DXVK/driver work.
6. Repeat the load in a fresh process. Confirm the shipped/user DXBC cache and
   NVIDIA driver cache each reduce only their own stage.
7. Sample descriptor count during the cold run. It should stay bounded; no
   limit adjustment is expected.

## Sources

- Current repository:
  `ClientPlugin/Patches/Rendering/ShaderCompilerPatch.cs` and
  `ClientPlugin/Compatibility/Rendering/D3DCompilerLinux.cs`
- Decompiled Space Engineers source under
  `~/.config/opencode/skills/se-dev-game-code/Data/Decompiled`, including Git
  commit `2133868` for `1.209.024 b0`
- Captured game log:
  `~/.config/SpaceEngineers/SpaceEngineers_20260821_141737177.log`
- Captured renderer log:
  `~/.config/SpaceEngineers/VRageRender-DirectX11_20260821_141737302.log`
- Captured DXVK output: `/tmp/opencode/load-100-capture.log`
- [Microsoft `D3DPreprocess` documentation](https://learn.microsoft.com/en-us/windows/win32/api/d3dcompiler/nf-d3dcompiler-d3dpreprocess)
- [DXVK 2.0 release notes](https://github.com/doitsujin/dxvk/releases/tag/v2.0)
- [DXVK 2.7 release notes](https://github.com/doitsujin/dxvk/releases/tag/v2.7)
- [DXVK 3.0 release notes](https://github.com/doitsujin/dxvk/releases/tag/v3.0)
- Upstream DXVK 2.7.1 baseline source inspected at
  `/tmp/opencode/dxvk-v2.7.1`; the runtime identifies itself as `2.7.1+`

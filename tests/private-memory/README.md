# Process private memory tests

Verifies `ProcessPrivateMemory.Get`, which the preloader writes into
`MyWindowsSystem.ProcessPrivateMemory`. `MyGeneralStats.Update` reads that
getter on every update, and on Linux each uncached read builds a new
`Process` snapshot from `/proc/self/stat`, `/proc/self/status` and the stat
file of every thread, 15 KB to 90 KB of garbage per read. `Get` reads at most
once a second, and only `/proc/self/status`, into a stack buffer.

The suite is standalone. It compiles
`Shared/Patches/SystemAbstraction/ProcessPrivateMemory.cs` verbatim, so it
needs neither the game nor Steam. It is not part of `LinuxCompat.sln`, and its
own `Directory.Build.props` shadows the repository root's.

```bash
cd tests/private-memory && dotnet run -c Release
```

Exit code 0 means every check passed. Allocation is measured with
`GC.GetAllocatedBytesForCurrentThread`. Covered:

| Check | What it pins down |
| --- | --- |
| `an uncached read allocates` | The instrument sees one `PrivateMemorySize64` read |
| `a status read allocates under 1 KB` | A refresh leaves only the file handle as garbage |
| `a status read is the Process value` | VmData plus VmStk, as `PrivateMemorySize64` reports it |
| `the first read is the process value` | `Get` returns what `Process` reports |
| `1000 reads within a second allocate nothing` | Reads within a second come from the cache |
| `reads within a second return the cached value` | The same value, not a fresh read |
| `the refreshed value sees the new memory` | The cache expires after a second: the new value includes 64 MB allocated in between |

# Release assert test

Covers `Shared/Compatibility/ReleaseAssertPatch.cs` (SE-1800).

`MyDebug.FailRelease` and `MyDebug.AssertRelease` end in
`MyDebugListenerProvider.MyTraceListener.TraceMessage` in
`VRage.Platform.Windows.dll`. It logs `ASSERT: ...` and then shows a WinForms
Abort/Retry/Ignore box. On Linux `System.Windows.Forms` can't be loaded, so the
method throws before it logs anything, and the assert crashes the client or the
server. A world with two grids sharing an entity id does that while loading.

The test patches the game's `VRage.Platform.Windows.dll` the way the preloader
does, then calls `MyTraceListener.Fail` in both the shipped and the patched
assembly with a fresh `MyLog`. The shipped one throws `FileNotFoundException`
for `System.Windows.Forms`; the patched one must log the assert and return.

The suite is standalone and not part of `LinuxCompat.sln`. Its own
`Directory.Build.props` shadows the repository root's.

```bash
cd tests/release-asserts && dotnet run -c Release                 # DS assemblies
dotnet run -c Release -- ~/.steam/steam/steamapps/common/SpaceEngineers/Bin64
```

Exit code 0 means the patched listener logged the assert and returned.

# DLC ownership checks

Run `dotnet run --project tests/dlc-ownership/DlcOwnershipTests.csproj`.

The tests compile the real `MySteamServiceDlcPatches.cs` and `SteamAppInfo.cs`
against fake Steamworks, Harmony and logging stubs. They check:

- the unlock decision: owned and released, owned with an unknown or other
  release state, owned but `prerelease` or `preloadonly`, not owned, installed;
- the app info cache reader on made-up `appinfo.vdf` files in formats 28 and 29,
  found through a temporary `HOME`;
- that a missing, broken or unknown cache, an empty DLC list and a failing
  Steamworks call never lock owned DLC;
- the DLC block written to the game log once per start.

If this machine has a Steam client cache, the last check reads it too and
prints the release state it has for the Warfare 3 Pack (5070970).

Steam's real answers for an owned but unreleased DLC can't be reproduced here,
since none of us owns one. The log block is there so a player who does can send
it to us.

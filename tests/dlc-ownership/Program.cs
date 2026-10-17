using System.Reflection;
using ClientPlugin.Compatibility;
using ClientPlugin.Patches.PlatformGuards;
using DlcOwnershipTests;
using Steamworks;
using VRage.Utils;

// Steam answers below are made up; the app ids are real.
const uint deluxe = 573160;
const uint released = 1049790; // Decorative Pack
const uint notOwned = 1084680; // Style Pack
const uint warfare3 = 5070970; // Warfare 3 Pack, unreleased on 2026-10-17

var realHome = Environment.GetEnvironmentVariable("HOME");
var home = Directory.CreateTempSubdirectory("dlc-ownership-").FullName;
var cacheDir = Path.Combine(home, ".steam", "root", "appcache");
var cache = Path.Combine(cacheDir, "appinfo.vdf");
Directory.CreateDirectory(cacheDir);
Environment.SetEnvironmentVariable("HOME", home);
var log = MyLog.Default.Lines;
try
{
    // The decision itself.
    Check(SteamDlcOwnership.IsUnlocked(false, true, "released"), "owned and released: unlocked");
    Check(
        SteamDlcOwnership.IsUnlocked(false, true, null),
        "owned, release state unknown: unlocked"
    );
    Check(SteamDlcOwnership.IsUnlocked(false, true, "unavailable"), "owned, other state: unlocked");
    Check(!SteamDlcOwnership.IsUnlocked(false, true, "prerelease"), "owned but prerelease: locked");
    Check(
        !SteamDlcOwnership.IsUnlocked(false, true, "PreloadOnly"),
        "owned but preload only: locked"
    );
    Check(!SteamDlcOwnership.IsUnlocked(false, false, "released"), "not owned: locked");
    Check(SteamDlcOwnership.IsUnlocked(true, true, "prerelease"), "installed wins over the state");

    // The cache reader, both formats.
    foreach (var format in new[] { 28, 29 })
    {
        AppInfoFile.Write(
            cache,
            format,
            (deluxe, "released"),
            (42, "prerelease"),
            (warfare3, "prerelease"),
            (released, null)
        );
        var states = SteamAppInfo.ReadReleaseStates(cache, [deluxe, warfare3, released, 7]);
        Check(
            states.Count == 2 && states[deluxe] == "released" && states[warfare3] == "prerelease",
            $"format {format}: release states of the asked apps only"
        );
    }
    Check(SteamAppInfo.FindCache() == cache, "cache found under ~/.steam/root");

    SteamApps.Dlcs =
    [
        new(deluxe, true, true, true, "Deluxe Edition"),
        new(released, true, true, false, "Decorative Pack"),
        new(notOwned, true, false, false, "Style Pack"),
        new(warfare3, true, true, false, "Warfare 3 Pack"),
    ];

    // End to end with a readable cache.
    AppInfoFile.Write(
        cache,
        29,
        (deluxe, "released"),
        (released, "released"),
        (notOwned, "released"),
        (warfare3, "prerelease")
    );
    Reset();
    Check(Owned(released), "owned and released DLC is unlocked");
    Check(!Owned(notOwned), "DLC without a license stays locked");
    Check(!Owned(warfare3), "owned prerelease DLC stays locked");
    Check(Owned(deluxe), "installed DLC is unlocked");
    Check(log.Count == 7, "one log block: list, cache, header, one line per DLC");
    Check(log[0] == "[LinuxCompat] Steam DLC list: 4 of 4 read", "list line");
    Check(
        log[1]
            == "[LinuxCompat] Steam app info cache ~/.steam/root/appcache/appinfo.vdf: release state of 4 DLC",
        "cache line, home folder hidden"
    );
    Check(
        log.Contains($"[LinuxCompat]   {released} 1 1 0 released 1 Decorative Pack"),
        "released DLC line"
    );
    Check(
        log.Contains($"[LinuxCompat]   {warfare3} 1 1 0 prerelease 0 Warfare 3 Pack"),
        "prerelease DLC line"
    );
    Owned(released);
    Check(log.Count == 7, "logged once per start");

    // No cache: the license decides, as before.
    File.Delete(cache);
    Reset();
    Check(Owned(warfare3), "without a cache owned DLC stays unlocked");
    Check(log.Contains("[LinuxCompat] Steam app info cache not found"), "missing cache logged");
    Check(
        log.Contains($"[LinuxCompat]   {warfare3} 1 1 0 - 1 Warfare 3 Pack"),
        "unknown state shown as -"
    );

    // A broken or unknown cache: same.
    foreach (
        var bytes in new[]
        {
            new byte[] { 0x27, 0x44, 0x56, 0x07, 1, 0, 0, 0, 0 },
            new byte[] { 0x29, 0x44, 0x56, 0x07, 1, 0 },
        }
    )
    {
        File.WriteAllBytes(cache, bytes);
        Reset();
        Check(Owned(warfare3), "with an unreadable cache owned DLC stays unlocked");
        Check(
            log.Any(l => l.StartsWith("[LinuxCompat] Steam app info cache unreadable")),
            "unreadable cache logged"
        );
    }

    // Steam lists no DLC (count 0, not ready yet): nothing gets locked.
    AppInfoFile.Write(cache, 29, (warfare3, "prerelease"));
    Reset();
    SteamApps.CountOverride = 0;
    Check(Owned(warfare3), "an empty DLC list locks nothing");
    Check(log[0] == "[LinuxCompat] Steam DLC list: 0 of 0 read", "empty list logged");
    SteamApps.CountOverride = null;

    // Steamworks throws: fall through to the game's method.
    Reset();
    SteamApps.Throw = true;
    Check(
        !SteamDlcOwnership.TryGetOwnership(released, out _),
        "Steamworks failure falls through to the game"
    );
    Check(log[0].StartsWith("[LinuxCompat] Steam DLC list failed"), "list failure logged");
    SteamApps.Throw = false;
    Check(Owned(released), "after the failure ownership answers again");
}
finally
{
    Environment.SetEnvironmentVariable("HOME", realHome);
    Directory.Delete(home, true);
}

// The real cache of this machine's Steam client, if there is one.
var real = SteamAppInfo.FindCache();
if (real == null)
    Console.WriteLine("SKIP no local Steam app info cache");
else
{
    var states = SteamAppInfo.ReadReleaseStates(real, [244850, warfare3]);
    Check(
        states.GetValueOrDefault(244850u) == "released",
        "local cache: Space Engineers is released"
    );
    Console.WriteLine(
        $"INFO local cache: Warfare 3 Pack is {states.GetValueOrDefault(warfare3) ?? "not in the cache"}"
    );
}

Console.WriteLine("DLC ownership checks passed.");

static bool Owned(uint id)
{
    Check(SteamDlcOwnership.TryGetOwnership(id, out var result), $"ownership of {id} answered");
    return result;
}

static void Reset()
{
    MyLog.Default.Lines.Clear();
    typeof(SteamDlcOwnership)
        .GetField("_releaseStates", BindingFlags.Static | BindingFlags.NonPublic)!
        .SetValue(null, null);
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
    Console.WriteLine("PASS " + message);
}

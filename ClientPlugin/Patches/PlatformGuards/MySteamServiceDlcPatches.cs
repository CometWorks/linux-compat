using System;
using System.Collections.Generic;
using ClientPlugin.Compatibility;
using HarmonyLib;
using Steamworks;
using VRage.Steam;
using VRage.Utils;

namespace ClientPlugin.Patches.PlatformGuards;

// Steam reports DLC as "installed" per platform: BIsDlcInstalled answers false
// unless the DLC's content is installed for the OS the calling process runs as.
// Space Engineers ships Windows depots only, so a native Linux client sees just
// the three DLCs that carry their own installed depot (Deluxe 573160, Frostbite
// 1241550, Signal Pack 2914120) and every other owned DLC stays locked - blocks
// greyed out, DLC banners not highlighted. The same account, same Steam client,
// running the game under Proton reports all owned DLCs as installed, because
// there the process presents itself as Windows.
//
// Every other DLC's content already lives in the base game install, so nothing
// is missing on disk and ownership is what actually gates the blocks. Answer
// with BIsSubscribedApp instead: it is the license check, a superset of
// BIsDlcInstalled (installed implies owned), and stays false for DLC the user
// does not own.
//
// A license can exist before Keen releases the DLC, for example from a retail
// key. Under Proton BIsDlcInstalled stays false for such a DLC until release,
// so the game keeps it locked there, while BIsSubscribedApp is already true.
// Steamworks has no call for the release state, so read the store's
// ReleaseState from the Steam client's app info cache (SteamAppInfo) and keep
// DLC marked "prerelease" or "preloadonly" locked. Any other or missing state,
// or a cache that can't be read, leaves the license check alone: this filter
// may only ever lock DLC that isn't out yet.
//
// Known limit: Steam rewrites the cache on its own schedule. On release day an
// owner may stay locked until Steam has saved the new state and the game is
// restarted.
//
// Signals considered and not used: the available flag of BGetDLCDataByIndex
// is about the store, not the release. On an account without either DLC it was
// false for both the unreleased Warfare 3 Pack and the released Space
// Engineers 2013 (573900), which is no longer sold, so it would lock owners of
// DLC that left the store. The game ignores it too. BIsAppInstalled and GetAppInstallDir work for base apps only,
// GetDlcDownloadProgress needs DLC depots, which SE's DLC don't have, and
// purchase time, free weekend and family sharing say nothing about release.
// The ownership ticket API is not in Steamworks.NET.
//
// Not covered by the in-game test suites: none of us owns an unreleased DLC.
// tests/dlc-ownership checks the decision and the log block with faked Steam
// answers, and the cache reader against made-up files and, where present, the
// local Steam cache.
static class SteamDlcOwnership
{
    static readonly object Lock = new();
    static Dictionary<uint, string> _releaseStates;

    internal static bool IsUnreleased(string releaseState) =>
        string.Equals(releaseState, "prerelease", StringComparison.OrdinalIgnoreCase)
        || string.Equals(releaseState, "preloadonly", StringComparison.OrdinalIgnoreCase);

    internal static bool IsUnlocked(bool installed, bool subscribed, string releaseState) =>
        installed || (subscribed && !IsUnreleased(releaseState));

    // Returns false (and leaves result untouched) to fall through to the
    // original method if the Steamworks call fails for any reason.
    internal static bool TryGetOwnership(uint dlcId, out bool result)
    {
        ReleaseStates().TryGetValue(dlcId, out var releaseState);
        try
        {
            var appId = (AppId_t)dlcId;
            result = IsUnlocked(
                SteamApps.BIsDlcInstalled(appId),
                SteamApps.BIsSubscribedApp(appId),
                releaseState
            );
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[LinuxCompat] DLC ownership check of {dlcId} failed: {e.Message}");
            result = false;
            return false;
        }
    }

    static Dictionary<uint, string> ReleaseStates()
    {
        lock (Lock)
            return _releaseStates ??= LoadReleaseStates();
    }

    // Runs once per game start. Also writes one block to the game log, so a
    // player can send us what Steam reports for their DLC without running
    // anything else.
    static Dictionary<uint, string> LoadReleaseStates()
    {
        var log = new List<string>();
        var dlcs = new List<(uint Id, bool Available, string Name)>();
        try
        {
            var count = SteamApps.GetDLCCount();
            for (var i = 0; i < count; i++)
            {
                if (
                    SteamApps.BGetDLCDataByIndex(
                        i,
                        out var appId,
                        out var available,
                        out var name,
                        128
                    )
                )
                    dlcs.Add(((uint)appId, available, name));
            }
            log.Add($"[LinuxCompat] Steam DLC list: {dlcs.Count} of {count} read");
        }
        catch (Exception e)
        {
            log.Add($"[LinuxCompat] Steam DLC list failed: {e.Message}");
        }

        var states = new Dictionary<uint, string>();
        var home = Environment.GetEnvironmentVariable("HOME");
        try
        {
            var cache = SteamAppInfo.FindCache();
            if (cache == null)
                log.Add("[LinuxCompat] Steam app info cache not found");
            else if (dlcs.Count > 0)
            {
                states = SteamAppInfo.ReadReleaseStates(cache, dlcs.ConvertAll(d => d.Id));
                log.Add(
                    $"[LinuxCompat] Steam app info cache {Scrub(cache, home)}: release state of {states.Count} DLC"
                );
            }
        }
        catch (Exception e)
        {
            states = new Dictionary<uint, string>();
            log.Add($"[LinuxCompat] Steam app info cache unreadable: {Scrub(e.Message, home)}");
        }

        try
        {
            log.Add(
                "[LinuxCompat]   appId available subscribed installed releaseState unlocked name"
            );
            foreach (var dlc in dlcs)
            {
                var appId = (AppId_t)dlc.Id;
                var subscribed = SteamApps.BIsSubscribedApp(appId);
                var installed = SteamApps.BIsDlcInstalled(appId);
                states.TryGetValue(dlc.Id, out var state);
                var unlocked = IsUnlocked(installed, subscribed, state);
                log.Add(
                    $"[LinuxCompat]   {dlc.Id} {Bit(dlc.Available)} {Bit(subscribed)} {Bit(installed)} {state ?? "-"} {Bit(unlocked)} {dlc.Name}"
                );
            }
        }
        catch (Exception e)
        {
            log.Add($"[LinuxCompat] Steam DLC ownership failed: {e.Message}");
        }

        foreach (var line in log)
        {
            Console.WriteLine(line);
            MyLog.Default?.WriteLine(line);
        }
        return states;
    }

    static string Scrub(string text, string home) =>
        string.IsNullOrEmpty(home) ? text : text.Replace(home, "~");

    static char Bit(bool value) => value ? '1' : '0';
}

[HarmonyPatch(typeof(MySteamService), nameof(MySteamService.IsDlcInstalled))]
[HarmonyPatchCategory("Finish")]
static class MySteamServiceIsDlcInstalledPatch
{
    static bool Prefix(uint dlcId, ref bool __result)
    {
        return !SteamDlcOwnership.TryGetOwnership(dlcId, out __result);
    }
}

[HarmonyPatch(typeof(MySteamService), nameof(MySteamService.IsDlcSupported))]
[HarmonyPatchCategory("Finish")]
static class MySteamServiceIsDlcSupportedPatch
{
    static bool Prefix(uint dlcId, ref bool __result)
    {
        return !SteamDlcOwnership.TryGetOwnership(dlcId, out __result);
    }
}

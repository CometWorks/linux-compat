using System;
using System.Collections.Generic;
using HarmonyLib;
using Steamworks;
using VRage.Steam;

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
// Ownership alone is too loose for DLC that has not been released yet: a retail
// or pre-release key grants the license before launch, and BIsDlcInstalled stays
// false for it on every platform until release. So ownership only counts when
// Steam also lists the DLC as available (BGetDLCDataByIndex).
// BIsDlcInstalled answering true is trusted as is.
static class SteamDlcOwnership
{
    static HashSet<uint> _availableDlcs;

    // Returns false (and leaves result untouched) to fall through to the
    // original method if the Steamworks call fails for any reason.
    internal static bool TryGetOwnership(uint dlcId, out bool result)
    {
        try
        {
            var appId = (AppId_t)dlcId;
            result = SteamApps.BIsDlcInstalled(appId) ||
                     (SteamApps.BIsSubscribedApp(appId) && IsAvailable(dlcId));
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[LinuxCompat] DLC ownership check for {dlcId} failed: {e.Message}");
            result = false;
            return false;
        }
    }

    static bool IsAvailable(uint dlcId)
    {
        var dlcs = _availableDlcs ?? LoadAvailableDlcs();
        return dlcs.Contains(dlcId);
    }

    // The DLC list is fixed for the session, so query it once. An empty list
    // is not cached, in case Steam had not populated it yet.
    static HashSet<uint> LoadAvailableDlcs()
    {
        var dlcs = new HashSet<uint>();
        var count = SteamApps.GetDLCCount();
        for (var i = 0; i < count; i++)
        {
            if (!SteamApps.BGetDLCDataByIndex(i, out var appId, out var available, out _, 128))
                continue;

            if (available)
                dlcs.Add((uint)appId);
            else if (SteamApps.BIsSubscribedApp(appId))
                Console.WriteLine($"[LinuxCompat] DLC {appId} is owned but not available");
        }

        if (count > 0)
            _availableDlcs = dlcs;

        return dlcs;
    }
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

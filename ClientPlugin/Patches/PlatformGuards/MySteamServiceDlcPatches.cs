using System;
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
static class SteamDlcOwnership
{
    // Returns false (and leaves result untouched) to fall through to the
    // original method if the Steamworks call fails for any reason.
    internal static bool TryGetOwnership(uint dlcId, out bool result)
    {
        try
        {
            result = SteamApps.BIsSubscribedApp((AppId_t)dlcId);
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[LinuxCompat] BIsSubscribedApp({dlcId}) failed: {e.Message}");
            result = false;
            return false;
        }
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

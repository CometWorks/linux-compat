using HarmonyLib;
using VRage.FileSystem;

namespace ServerPlugin.Patches.PathHandling;

// Triggers the Level 1 static path cache build once MyFileSystem.Init has
// populated ContentPath (ExePath is set in the static constructor of
// MyFileSystem from Assembly.GetEntryAssembly().Location, so it is already
// available by the time any Init runs).
//
// The build is a single recursive enumeration of Content/ and Bin64/ and
// runs synchronously on the Init caller's thread. Game startup already
// pauses here for asset DB construction; the extra walk is unnoticed.
[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.Init))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemInitPatch
{
    static void Postfix()
    {
        PathCache.BuildStaticCache();
    }
}

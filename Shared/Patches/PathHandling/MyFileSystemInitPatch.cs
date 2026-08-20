using HarmonyLib;
using VRage.FileSystem;

namespace ClientPlugin.Patches.PathHandling;

// Build the static file cache after MyFileSystem has populated its roots.
[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.Init))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemInitPatch
{
    static void Postfix()
    {
        PathCache.BuildStaticCache();
    }
}

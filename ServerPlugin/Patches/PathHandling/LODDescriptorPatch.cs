using System.IO;
using HarmonyLib;
using VRage.FileSystem;
using VRageRender.Import;

namespace ServerPlugin.Patches.PathHandling;

// Ports Topic 4.3 MyLODDescriptor.GetModelAbsoluteFilePath fix from
// dotnet-game-local. Replaces plain Path.Combine with case-insensitive
// resolution so LOD model files load from content/mod directories on
// Linux regardless of path casing.
[HarmonyPatch(typeof(MyLODDescriptor), nameof(MyLODDescriptor.GetModelAbsoluteFilePath))]
[HarmonyPatchCategory("Finish")]
static class MyLODDescriptorGetModelAbsoluteFilePathPatch
{
    static bool Prefix(MyLODDescriptor __instance, ref string __result, string parentAssetFilePath)
    {
        if (__instance.Model == null)
        {
            __result = null;
            return false;
        }

        string modelFile = __instance.Model;
        if (!modelFile.Contains(".mwm"))
            modelFile += ".mwm";

        var lowered = parentAssetFilePath?.ToLower() ?? "";
        if (parentAssetFilePath != null && Path.IsPathRooted(parentAssetFilePath) && lowered.Contains("models"))
        {
            var rootPath = parentAssetFilePath.Substring(0, lowered.IndexOf("models"));
            var candidate = PathHelpers.ResolveContentFilePath(modelFile, rootPath);
            if (MyFileSystem.FileExists(candidate))
            {
                __result = candidate;
                return false;
            }

            candidate = PathHelpers.ResolveContentFilePath(modelFile, MyFileSystem.ContentPath);
            __result = MyFileSystem.FileExists(candidate) ? candidate : null;
            return false;
        }

        __result = PathHelpers.ResolveContentFilePath(modelFile, MyFileSystem.ContentPath);
        return false;
    }
}

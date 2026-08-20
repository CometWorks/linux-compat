using HarmonyLib;
using VRage.FileSystem;
using VRage.Render11.GeometryStage2.Model;

namespace ClientPlugin.Patches.PathHandling;

// Preserve model-path casing and resolve it against the Linux filesystem.
[HarmonyPatch(typeof(MyMwmUtils), nameof(MyMwmUtils.GetFullMwmFilepath))]
[HarmonyPatchCategory("Finish")]
static class MyMwmUtilsGetFullMwmFilepathPatch
{
    static bool Prefix(ref string __result, string mwmFilepath)
    {
        if (!mwmFilepath.EndsWith(".mwm"))
            mwmFilepath += ".mwm";
        __result = PathHelpers.ResolveContentFilePath(mwmFilepath, MyFileSystem.ContentPath);
        return false;
    }
}

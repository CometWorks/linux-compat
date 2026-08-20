using System.IO;
using HarmonyLib;
using VRage.FileSystem;
using VRageRender.Import;

namespace ClientPlugin.Patches.PathHandling;

// Finish must precede early model loads that use lower-cased paths on Linux.
[HarmonyPatch(typeof(MyModelImporter), nameof(MyModelImporter.ImportData))]
[HarmonyPatchCategory("Finish")]
static class MyModelImporterPatch
{
    static void Prefix(ref string assetFileName)
    {
        if (assetFileName == null)
            return;

        assetFileName = assetFileName.Replace('\\', '/');

        var fullPath = Path.IsPathRooted(assetFileName)
            ? assetFileName
            : Path.Combine(MyFileSystem.ContentPath, assetFileName);

        var resolved = PathCache.ResolveAbsolute(fullPath);
        if (resolved != fullPath && File.Exists(resolved))
        {
            assetFileName = resolved;
        }
    }
}

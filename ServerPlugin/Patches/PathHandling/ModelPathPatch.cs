using System.IO;
using HarmonyLib;
using VRage.FileSystem;
using VRageRender.Import;

namespace ServerPlugin.Patches.PathHandling;

// Must run in the "Finish" category so the patch is active before
// MyDX11Render.CreateDevice loads early models (e.g. LoadingQuad.mwm).
// In "Init" the patch was applied too late and ImportData received the
// lowercased path that doesn't match Linux's case-sensitive filesystem.
[HarmonyPatch(typeof(MyModelImporter), nameof(MyModelImporter.ImportData))]
[HarmonyPatchCategory("Finish")]
static class MyModelImporterPatch
{
    static void Prefix(ref string assetFileName)
    {
        if (assetFileName == null) return;

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

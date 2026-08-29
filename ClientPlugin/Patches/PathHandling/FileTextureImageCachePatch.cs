using System.IO;
using HarmonyLib;
using VRage.FileSystem;
using VRage.Render11.Resources;

namespace ClientPlugin.Patches.PathHandling;

// Normalize separators and on-disk casing before Linux texture file access.
[HarmonyPatch(typeof(MyFileTextureImageCache), "LoadImage")]
[HarmonyPatchCategory("Finish")]
static class FileTextureImageCacheLoadImagePatch
{
    static void Prefix(ref string filepath)
    {
        if (string.IsNullOrEmpty(filepath))
            return;

        // The funnel restores synthetic drive prefixes and resolves casing.
        filepath = PathCache.ResolveAbsolute(filepath);
        if (Path.IsPathRooted(filepath))
            return;

        var contentPath = MyFileSystem.ContentPath;
        if (string.IsNullOrEmpty(contentPath))
            return;

        var full = Path.Combine(contentPath, filepath);
        var resolved = PathCache.ResolveAbsolute(full);
        if (resolved != full && File.Exists(resolved))
            filepath = resolved;
    }
}

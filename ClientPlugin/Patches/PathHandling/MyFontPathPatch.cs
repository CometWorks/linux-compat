using System;
using System.IO;
using HarmonyLib;
using VRage.FileSystem;
using VRageRender;

namespace ClientPlugin.Patches.PathHandling;

// Normalize the constructor argument so its readonly directory uses Linux casing.
[HarmonyPatch(typeof(MyFont), MethodType.Constructor, typeof(string), typeof(int), typeof(bool))]
[HarmonyPatchCategory("Finish")]
static class MyFontConstructorPatch
{
    static void Prefix(ref string fontFilePath, bool dummyFont)
    {
        if (dummyFont || string.IsNullOrEmpty(fontFilePath))
            return;

        try
        {
            string contentPath = MyFileSystem.ContentPath;
            string combined = Path.IsPathRooted(fontFilePath)
                ? fontFilePath
                : (contentPath != null ? Path.Combine(contentPath, fontFilePath) : fontFilePath);

            string normalized = PathHelpers.Normalize(combined);
            fontFilePath = Path.IsPathRooted(normalized)
                ? PathCache.ResolveAbsolute(normalized)
                : normalized;
        }
        catch
        {
            // Let the constructor report the original path failure.
        }
    }
}

using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using VRage.FileSystem;
using VRageRender;

namespace ClientPlugin.Patches.PathHandling;

// MyFont retains its unnormalized constructor path after FileExists returns.
// Recompute m_fontDirectory with Linux separators and on-disk casing.
[HarmonyPatch(typeof(MyFont), MethodType.Constructor, typeof(string), typeof(int), typeof(bool))]
[HarmonyPatchCategory("Finish")]
static class MyFontConstructorPatch
{
    private static FieldInfo s_fontDirectoryField;

    static void Prefix(string fontFilePath, bool dummyFont)
    {
        if (dummyFont)
            return;

        try
        {
            string contentPath = MyFileSystem.ContentPath;
            string combined = Path.IsPathRooted(fontFilePath)
                ? fontFilePath
                : (contentPath != null ? Path.Combine(contentPath, fontFilePath) : fontFilePath);

            string normalized = combined?.Replace('\\', '/');
            string resolved = (normalized != null && Path.IsPathRooted(normalized))
                ? PathCache.ResolveAbsolute(normalized)
                : normalized;

            bool existsAsIs = combined != null && File.Exists(combined);
            bool existsResolved = resolved != null && File.Exists(resolved);
        }
        catch
        {
            // Diagnostic only; never break game startup.
        }
    }

    static void Postfix(MyFont __instance, string fontFilePath, bool dummyFont)
    {
        if (dummyFont || string.IsNullOrEmpty(fontFilePath))
            return;

        // Recompute the constructor path before calling GetDirectoryName.
        string path = Path.IsPathRooted(fontFilePath)
            ? fontFilePath
            : Path.Combine(MyFileSystem.ContentPath, fontFilePath);
        path = PathHelpers.Normalize(path);

        // Texture consumers require the directory's on-disk casing.
        if (Path.IsPathRooted(path))
            path = PathCache.ResolveAbsolute(path);

        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir))
            return;

        s_fontDirectoryField ??= AccessTools.Field(typeof(MyFont), "m_fontDirectory")
            ?? throw new InvalidOperationException("MyFont.m_fontDirectory not found");
        s_fontDirectoryField.SetValue(__instance, dir);
    }
}

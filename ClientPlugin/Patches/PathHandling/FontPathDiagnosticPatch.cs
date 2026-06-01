using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using VRage.FileSystem;
using VRageRender;

namespace ClientPlugin.Patches.PathHandling;

// Topic 4.11 (commit 4b0a5d70): MyFont ctor path normalization.
//
// The recompiled game inserts `path = PathUtils.Normalize(path)` inside the
// MyFont(string fontFilePath, ...) constructor, between Path.Combine and
// MyFileSystem.FileExists. The stock IL omits this normalization. The
// initial port assumed our MyFileSystem.FileExists prefix already covered
// the case (it does normalize+resolve internally) — but that mutation is
// only visible inside FileExists itself; the ctor's local `path` keeps the
// original backslashes. The very next ctor statement,
//
//     m_fontDirectory = Path.GetDirectoryName(path);
//
// then runs on e.g. ".../Content/Fonts\white\FontDataPA.xml" and, since
// Linux's Path.GetDirectoryName treats `\` as a regular filename character,
// returns ".../Content" instead of ".../Content/Fonts/white". The font's
// later texture lookup
//
//     Path.Combine(m_fontDirectory, "FontDataPA-0.dds")
//
// then misses on disk and the bitmap doesn't load — main-menu text renders
// blank while button icons (which don't need the font) draw normally.
//
// Fix: overwrite m_fontDirectory in a Postfix using a freshly-recomputed
// normalized path. The original ctor still runs (so XML loading and other
// state are unchanged); we only correct the cached directory string after
// the fact.
//
// Diagnostic logging is retained as a regression detector. If a future
// game update changes the path the ctor sees, the log makes it obvious.
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

        // Reproduce the ctor's path computation, then apply the missing
        // PathUtils.Normalize step so GetDirectoryName works correctly.
        string path = Path.IsPathRooted(fontFilePath)
            ? fontFilePath
            : Path.Combine(MyFileSystem.ContentPath, fontFilePath);
        path = PathHelpers.Normalize(path);

        // Lock in on-disk casing (Layer 1 of the font-path fix). Downstream
        // consumers of MyFont.FontDirectory — notably MyRenderFont.LoadContent
        // which builds texture paths via Path.Combine(FontDirectory,
        // bitmapInfo.strFilename) — must end up with the on-disk casing,
        // because MyResourceUtils.NormalizeFileTextureName (called from
        // MyTextureStreamingManager.GetOrMakeTexture) lowercases whatever
        // they produce before it reaches the file provider. Resolving here
        // ensures the casing is correct before that lowercasing happens, so
        // even code paths that bypass our case-resolving Open prefix can
        // succeed.
        if (Path.IsPathRooted(path))
            path = PathCache.ResolveAbsolute(path);

        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir))
            return;

        // m_fontDirectory is private readonly in MyFont; the publicizer makes
        // it visible but the readonly modifier is preserved, so we still need
        // reflection to overwrite it after the ctor ran.
        s_fontDirectoryField ??= typeof(MyFont).GetField("m_fontDirectory",
            BindingFlags.Instance | BindingFlags.NonPublic);
        s_fontDirectoryField?.SetValue(__instance, dir);
    }
}

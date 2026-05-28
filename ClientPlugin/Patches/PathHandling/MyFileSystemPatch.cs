using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Sandbox.Engine.Voxels;
using VRage.FileSystem;

namespace ClientPlugin.Patches.PathHandling;

// Case-insensitive resolution lives in PathCache (PathHelpers.cs). The
// formerly-separate PathNormalizer was a duplicate file-keyed cache; see
// Docs/Fixes.md 2026-04-25 (path resolver cache redesign).

// MyFileSystem.Open is the lowest-level gateway every file open funnels
// through (OpenRead delegates to it). The recompiled game performs both
// NormalizePath + ResolveCaseSensitivePath here; the stock IL omits
// both. Backslash normalization alone is not enough: callers like
// MyTextureStreamingManager / MyResourceUtils.NormalizeFileTextureName
// lowercase texture paths before passing them down (see MyResourceUtils
// .cs line 350 in dotnet-game-local), and any code path that bypasses
// MyFileTextureImageCache.LoadImage (e.g. MyFileTexture.LoadResource,
// MyTextureCache mip-map probes) reaches Open with a lowercased path.
// On Linux this is the failure that produced "Texture does not exist:
// .../Content/fonts/white/fontdatapa-0.dds" while the on-disk file is
// PascalCased.
//
// Implemented as a Cecil prepatch (see MyFileSystemOpenPrepatch.cs) that
// injects `path = PathCache.ResolveAbsolute(path)` into the body of
// MyFileSystem.Open itself, rather than as a Harmony Prefix. A Prefix
// here is silently bypassed by JIT inlining: OpenRead and Open are tiny
// static methods with constant arguments at every call site, the JIT
// inlines them into hot callers (e.g. ParseAtlasDescription_Patch1), and
// the inlined IL skips the Harmony prologue stub. Putting the
// normalization inside Open's own body means inlined copies still carry
// it. The MyTextureAtlas regression detector in MyTextureAtlasPatch.cs
// is the canary for any future regression of this layer.

[HarmonyPatch(typeof(MyStorageBase), nameof(MyStorageBase.LoadFromFile))]
[HarmonyPatchCategory("Finish")]
static class MyStorageBaseLoadFromFilePatch
{
    static void Prefix(ref string absoluteFilePath)
    {
        if (absoluteFilePath != null)
            absoluteFilePath = PathCache.ResolveAbsolute(absoluteFilePath);
    }
}

// OpenWrite mirrors Open: the recompiled game does NormalizePath +
// ResolveCaseSensitivePath here too. The new-file case needs special
// handling because PathCache.ResolveAbsolute returns the input unchanged
// when the leaf doesn't yet exist on disk (typical for a fresh write).
// Without resolving the parent, a write to "Foo/Bar/new.txt" when only
// "foo/bar/" exists creates a duplicate-cased "Foo/Bar/" sibling — which
// is exactly the per-save / per-screenshot folder-duplication bug the
// recompiled game fixes at this layer.
[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.OpenWrite), typeof(string), typeof(FileMode))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemOpenWritePatch
{
    static void Prefix(ref string path)
    {
        if (path == null)
            return;

        path = path.Replace('\\', '/');

        // Strip any synthetic Windows drive prefix a mod may have composed
        // off ModContext.ModPath (see PathTranslation.Untranslate). Has to
        // happen before the IsPathRooted gate — on Linux the BCL false-
        // negatives "C:/..." paths and the write would silently no-op.
        path = PathTranslation.Untranslate(path);

        if (!Path.IsPathRooted(path))
            return;

        var resolved = PathCache.ResolveAbsolute(path);
        if (resolved != path)
        {
            path = resolved;
            return;
        }

        // Full path didn't resolve (new file). Try to resolve the parent
        // directory so the on-disk casing wins.
        var dir = Path.GetDirectoryName(path);
        var leaf = Path.GetFileName(path);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(leaf))
            return;

        var resolvedDir = PathCache.ResolveAbsolute(dir);
        if (resolvedDir != dir)
            path = resolvedDir + "/" + leaf;
    }
}

[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.FileExists))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemFileExistsPatch
{
    static void Prefix(ref string path)
    {
        if (path == null)
            return;

        path = path.Replace('\\', '/');

        // Strip any synthetic Windows drive prefix (see PathTranslation
        // .Untranslate). Mods that probe FileExists with an absolute path
        // built off ModContext.ModPath would otherwise false-negative here
        // because Path.IsPathRooted("C:/...") is false on Linux.
        path = PathTranslation.Untranslate(path);

        // Resolve case-insensitively so callers like MyMeshes.LoadMwm find
        // mesh assets when on-disk casing differs from the in-game path
        // (e.g. "Models/Cubes/Small/Armor/..." vs "Models/Cubes/small/armor/...").
        if (Path.IsPathRooted(path))
            path = PathCache.ResolveAbsolute(path);
    }
}

[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.DirectoryExists))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemDirectoryExistsPatch
{
    static void Prefix(ref string path)
    {
        if (path == null)
            return;

        path = path.Replace('\\', '/');

        // Strip any synthetic Windows drive prefix (see PathTranslation
        // .Untranslate) so a "C:/users/steamuser/..." probe is matched
        // against the real Linux directory.
        path = PathTranslation.Untranslate(path);

        // Mirror FileExists: a probe for "Mods/Workshop" should see the
        // on-disk "mods/workshop". On miss, ResolveAbsolute returns the
        // input unchanged and Directory.Exists then returns false, which
        // is the same outcome as before — this only fixes false negatives.
        if (Path.IsPathRooted(path))
            path = PathCache.ResolveAbsolute(path);
    }
}

// All three GetFiles overloads need the same treatment as FileExists /
// DirectoryExists: flip separators, strip the synthetic Windows drive
// prefix that ToWindowsPath may have stamped onto a mod-built path, and
// resolve case-insensitively. MyTexts.LoadSupportedLanguages reaches the
// 3-arg overload with a path the mod composed off ModContext.ModPathData
// (e.g. "C:\users\steamuser\.steam\debian-installation\...\Localization");
// without Untranslate, Directory.EnumerateFiles sees a drive-prefixed
// string that Path.IsPathRooted false-negatives on Linux, and the
// enumeration silently returns zero entries.
//
// Enumeration order (Postfix): the stock providers return whatever order
// the OS yields. On Windows NTFS that is per-directory lexicographic
// (case-insensitive); on Linux ext4/btrfs `readdir` order is undefined.
// Several consumers are order-sensitive -- MyScriptManager batches .cs
// files into per-subdirectory assemblies, and MyTexts (*.resx) /
// MyLocalization (*.sbl) let a later file override an earlier one for the
// same string id. Unsorted Linux order therefore changes which mod
// scripts group together and which translation wins, diverging from the
// Windows-tested behaviour. We materialize and sort __result by full path
// with OrdinalIgnoreCase, which mirrors Windows' case-insensitive index
// and matches MyDefinitionManager's own .sbc path sort. Sorting globally
// by full path is deterministic and also fixes the nested-subdirectory
// grouping case better than Windows' per-directory + breadth-first walk.
[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.GetFiles), typeof(string))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemGetFilesPatch
{
    static void Prefix(ref string path)
    {
        if (path == null)
            return;

        path = path.Replace('\\', '/');
        path = PathTranslation.Untranslate(path);

        if (Path.IsPathRooted(path))
            path = PathCache.ResolveAbsolute(path);
    }

    static void Postfix(ref IEnumerable<string> __result) => GetFilesSort.Apply(ref __result);
}

[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.GetFiles), typeof(string), typeof(string))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemGetFilesFilterPatch
{
    static void Prefix(ref string path)
    {
        if (path == null)
            return;

        path = path.Replace('\\', '/');
        path = PathTranslation.Untranslate(path);

        if (Path.IsPathRooted(path))
            path = PathCache.ResolveAbsolute(path);
    }

    static void Postfix(ref IEnumerable<string> __result) => GetFilesSort.Apply(ref __result);
}

[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.GetFiles), typeof(string), typeof(string), typeof(MySearchOption))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemGetFilesSearchOptionPatch
{
    static void Prefix(ref string path)
    {
        if (path == null)
            return;

        path = path.Replace('\\', '/');
        path = PathTranslation.Untranslate(path);

        if (Path.IsPathRooted(path))
            path = PathCache.ResolveAbsolute(path);
    }

    static void Postfix(ref IEnumerable<string> __result) => GetFilesSort.Apply(ref __result);
}

// Shared full-path sort applied to every MyFileSystem.GetFiles result so
// enumeration order is deterministic and Windows-like across platforms.
static class GetFilesSort
{
    public static void Apply(ref IEnumerable<string> result)
    {
        if (result == null)
            return;

        // Materialize once: providers may hand back a lazy IEnumerable, and
        // sorting (and any downstream re-enumeration) must not re-run it.
        var sorted = result.ToList();
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        result = sorted;
    }
}

// IsDirectory's stock body calls DirectoryExists(path) then File.GetAttributes(path).
// Our DirectoryExists Prefix normalizes its own `ref string path` parameter,
// but that change lives in DirectoryExists's stack frame only — the local
// `path` in IsDirectory is the unmodified caller-supplied string. So a
// drive-prefixed or case-mismatched input passes the first check (Prefix
// rewrites it inside DirectoryExists) and then the subsequent
// File.GetAttributes(originalPath) throws FileNotFoundException on the
// still-synthetic path. Replace the body with a normalized impl that
// re-uses the same path once.
[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.IsDirectory))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemIsDirectoryPatch
{
    static bool Prefix(string path, ref bool __result)
    {
        if (path == null)
        {
            __result = false;
            return false;
        }

        path = path.Replace('\\', '/');
        path = PathTranslation.Untranslate(path);

        if (Path.IsPathRooted(path))
            path = PathCache.ResolveAbsolute(path);

        if (!MyFileSystem.DirectoryExists(path))
        {
            __result = false;
            return false;
        }

        try
        {
            var attributes = File.GetAttributes(path);
            __result = attributes.HasFlag(FileAttributes.Directory);
        }
        catch
        {
            __result = false;
        }
        return false;
    }
}

// EnsureDirectoryExists feeds Directory.CreateDirectory on miss. Resolving
// the path first means an existing differently-cased dir is reused; on a
// genuine miss the existing CreateDirectoryRecursive prefix
// (MyFileSystemCreateDirectoryRecursivePatch below) walks segments
// case-insensitively. This belt-and-braces avoids a duplicate-cased dir
// being created when EnsureDirectoryExists is called with a path that
// already exists on disk under different casing.
[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.EnsureDirectoryExists))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemEnsureDirectoryExistsPatch
{
    static void Prefix(ref string path)
    {
        if (path == null)
            return;

        path = path.Replace('\\', '/');

        // Strip any synthetic Windows drive prefix (see PathTranslation
        // .Untranslate). EnsureDirectoryExists with a drive-prefixed input
        // would otherwise skip the resolve, then CreateDirectoryRecursive
        // below would try to mkdir a "C:" segment at the FS root.
        path = PathTranslation.Untranslate(path);

        if (Path.IsPathRooted(path))
            path = PathCache.ResolveAbsolute(path);
    }
}

// Ports Topic 4.1 CreateDirectoryRecursive change from dotnet-game-local.
// On Linux the baseline recursion creates duplicate directories with the
// wrong case when an ancestor already exists on disk with different
// casing. Walk segments case-insensitively, reusing existing directories
// before creating new ones.
[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.CreateDirectoryRecursive))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemCreateDirectoryRecursivePatch
{
    static bool Prefix(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        path = PathHelpers.Normalize(path);

        var segments = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var resolved = path.StartsWith(Path.DirectorySeparatorChar)
            ? Path.DirectorySeparatorChar.ToString()
            : "";

        foreach (var segment in segments)
        {
            var candidate = Path.Combine(resolved, segment);
            if (Directory.Exists(candidate))
            {
                resolved = candidate;
                continue;
            }

            if (Directory.Exists(resolved))
            {
                string found = null;
                try
                {
                    foreach (var entry in Directory.EnumerateDirectories(resolved))
                    {
                        if (string.Equals(Path.GetFileName(entry), segment, StringComparison.OrdinalIgnoreCase))
                        {
                            found = entry;
                            break;
                        }
                    }
                }
                catch
                {
                    // Fall through and treat as not found
                }
                resolved = found ?? candidate;
            }
            else
            {
                resolved = candidate;
            }

            if (!Directory.Exists(resolved))
                Directory.CreateDirectory(resolved);
        }

        return false;
    }
}

// Ports Topic 10.7 (commit 67278f4c): Fix StackOverflowException in
// MyFileSystem.IsGameContent on Linux. The original method, after testing
// the input path against ContentPath, falls back to recursing on
// path.Replace('/', '\\'). On Linux that recursion ping-pongs forever
// because (a) NormalizePath in our other MyFileSystem patches converts all
// separators back to '/', and (b) backslash isn't a path separator on
// Linux anyway -- the Windows-style fallback can never succeed.
//
// Strategy: Prefix replacement matching the upstream Linux behavior --
// return based on the rooted/ContentPath check only, never recurse.
[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.IsGameContent))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemIsGameContentPatch
{
    static bool Prefix(string path, ref bool __result)
    {
        if (!Path.IsPathRooted(path))
        {
            __result = true;
            return false;
        }

        __result = path.StartsWith(MyFileSystem.ContentPath, StringComparison.OrdinalIgnoreCase);
        return false;
    }
}

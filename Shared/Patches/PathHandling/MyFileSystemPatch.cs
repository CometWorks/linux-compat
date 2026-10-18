using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Sandbox.Engine.Voxels;
using VRage.FileSystem;

namespace ClientPlugin.Patches.PathHandling;

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

// Resolve the parent of new files to avoid duplicate directories with different casing.
[HarmonyPatch(
    typeof(MyFileSystem),
    nameof(MyFileSystem.OpenWrite),
    typeof(string),
    typeof(FileMode)
)]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemOpenWritePatch
{
    static void Prefix(ref string path)
    {
        if (path == null)
            return;

        // ResolveAbsolute is the single ingress funnel: it normalizes separators,
        // restores synthetic drive prefixes, and resolves on-disk casing.
        path = PathCache.ResolveAbsolute(path);

        if (!Path.IsPathRooted(path) || File.Exists(path))
            return;

        // New files still need the parent's on-disk casing.
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

        path = PathCache.ResolveAbsolute(path);
    }
}

// Sorts GetFiles results case-insensitively, whatever order the provider returned. Script grouping
// and localization override precedence depend on it. MyFileSystemPathPrepatch calls it from the
// GetFiles bodies.
public static class GetFilesSort
{
    public static IEnumerable<string> Sorted(IEnumerable<string> result)
    {
        if (result == null)
            return null;

        // Materialize lazy providers once before sorting.
        var sorted = result.ToList();
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        return sorted;
    }
}

// DirectoryExists prefix argument changes do not propagate back into IsDirectory.
// Normalize once so File.GetAttributes receives the same resolved path.
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

// Reuse existing directories whose casing differs from the requested path.
[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.EnsureDirectoryExists))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemEnsureDirectoryExistsPatch
{
    static void Prefix(ref string path)
    {
        if (path == null)
            return;

        path = PathCache.ResolveAbsolute(path);
    }
}

// Reuse case-insensitive ancestor matches before creating directories.
[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.CreateDirectoryRecursive))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemCreateDirectoryRecursivePatch
{
    static bool Prefix(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        path = PathHelpers.Normalize(path);

        var segments = path.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries
        );
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
                        if (
                            string.Equals(
                                Path.GetFileName(entry),
                                segment,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            found = entry;
                            break;
                        }
                    }
                }
                catch { }
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

// Separator fallback recursion cannot converge on Linux because normalization restores '/'.
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

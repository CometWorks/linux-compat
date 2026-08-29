using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using VRage.FileSystem;

namespace ClientPlugin.Patches.PathHandling;

// Static game files use a flat cache; mutable directories use mtime-validated child caches.
public static class PathHelpers
{
    /// <summary>Normalizes separators and whitespace for Linux filesystem calls.</summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        // Avoid allocations for already-normalized hot-path input.
        if (path.IndexOf('\\') < 0)
        {
            int start = 0;
            int end = path.Length;
            while (start < end && char.IsWhiteSpace(path[start]))
                start++;
            while (end > start && char.IsWhiteSpace(path[end - 1]))
                end--;
            if (start == 0 && end == path.Length)
                return path;
            return path.Substring(start, end - start);
        }

        path = path.Replace("\\\\", "/");
        path = path.Replace("\\", "/");
        return path.Trim();
    }

    /// <summary>
    /// Converts a mod-supplied path to native Linux form at the mod API boundary.
    /// Windows-shaped absolute paths are restored to their Linux roots; relative
    /// paths only get their separators normalized.
    /// </summary>
    public static string FromModPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        return PathTranslation.Untranslate(Normalize(path));
    }

    /// <summary>
    /// Converts Linux paths to synthetic Windows paths exposed only to mods.
    /// Known roots are translated; unmatched rooted paths receive a <c>C:</c> prefix.
    /// </summary>
    public static string ToWindowsPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        var flipped = path.IndexOf('/') < 0 ? path : path.Replace('/', '\\');
        var translated = PathTranslation.Translate(flipped);
        if (!ReferenceEquals(translated, flipped))
            return translated;
        if (
            flipped.Length >= 2
            && flipped[1] == ':'
            && (
                (flipped[0] >= 'A' && flipped[0] <= 'Z') || (flipped[0] >= 'a' && flipped[0] <= 'z')
            )
        )
            return flipped;
        if (flipped.Length > 0 && flipped[0] == '\\')
            return "C:" + flipped;
        return flipped;
    }

    /// <summary>
    /// Treats backslashes as separators on Linux.
    /// Its signature matches <see cref="Path.GetFileName(string)"/> for operand-only transpilers.
    /// </summary>
    public static string GetFileName(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        return Path.GetFileName(path.Replace('\\', '/'));
    }

    /// <summary>Treats backslashes as separators on Linux.</summary>
    public static string GetFileNameWithoutExtension(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        return Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
    }

    public static string ResolveContentFilePath(string relativePath, string rootPath)
    {
        return PathCache.Resolve(relativePath, rootPath);
    }
}

static class CaseInsensitivePathResolver
{
    public static string Resolve(string relativePath, string rootPath) =>
        PathCache.Resolve(relativePath, rootPath);
}

/// <summary>
/// Resolves immutable Content/Bin64 paths from a flat cache and mutable paths
/// from per-directory, mtime-validated caches.
/// </summary>
public static class PathCache
{
    // Lower-cased absolute and root-relative paths map to real disk casing.
    private static Dictionary<string, string> s_staticMap;
    private static volatile bool s_staticReady;

    // Known roots avoid walking from "/" for dynamic lookups.
    private static string s_contentRoot;
    private static string s_bin64Root;

    private sealed class DirEntry
    {
        // Null means the directory is absent or unreadable.
        public Dictionary<string, string> ChildMap;
        public long MtimeTicks = -1;
        public readonly object Sync = new();
    }

    // Keys are canonical, real-cased absolute directory paths.
    private static readonly ConcurrentDictionary<string, DirEntry> s_dirs = new(
        StringComparer.Ordinal
    );

    private static string s_modsRoot;
    private static string s_userDataRoot;
    private static int s_mutableRootsResolved;

    /// <summary>Builds the immutable cache after MyFileSystem.Init; idempotent.</summary>
    public static void BuildStaticCache()
    {
        if (s_staticReady)
            return;

        var contentRoot = NormalizeRoot(MyFileSystem.ContentPath);
        var bin64Root = NormalizeRoot(MyFileSystem.ExePath);

        if (contentRoot == null && bin64Root == null)
            return;

        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        if (contentRoot != null)
        {
            AddRoot(map, contentRoot);
            s_contentRoot = contentRoot;
        }
        if (bin64Root != null)
        {
            AddRoot(map, bin64Root);
            s_bin64Root = bin64Root;
        }

        s_staticMap = map;
        s_staticReady = true;
    }

    private static string NormalizeRoot(string p)
    {
        if (string.IsNullOrEmpty(p))
            return null;
        p = PathHelpers.Normalize(p);
        if (p.Length > 1 && p.EndsWith('/'))
            p = p.TrimEnd('/');
        return p;
    }

    private static void AddRoot(Dictionary<string, string> map, string root)
    {
        map[root.ToLowerInvariant()] = root;

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories);
        }
        catch
        {
            return;
        }

        var rootLen = root.Length;
        foreach (var raw in entries)
        {
            string sub;
            try
            {
                sub = PathHelpers.Normalize(raw);
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrEmpty(sub))
                continue;

            map[sub.ToLowerInvariant()] = sub;

            if (
                sub.Length > rootLen
                && sub.StartsWith(root, StringComparison.Ordinal)
                && sub[rootLen] == '/'
            )
            {
                var rel = sub.Substring(rootLen + 1);
                if (rel.Length > 0)
                    map[rel.ToLowerInvariant()] = sub;
            }
        }
    }

    private static void EnsureMutableRoots()
    {
        if (s_mutableRootsResolved == 1)
            return;

        var mods = NormalizeRoot(MyFileSystem.ModsPath);
        var user = NormalizeRoot(MyFileSystem.UserDataPath);
        if (mods != null)
            s_modsRoot = mods;
        if (user != null)
            s_userDataRoot = user;
        if (s_modsRoot != null && s_userDataRoot != null)
            s_mutableRootsResolved = 1;
    }

    /// <summary>Resolves an absolute path case-insensitively, returning the input on a miss.</summary>
    public static string ResolveAbsolute(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
            return absolutePath;

        var path = PathHelpers.Normalize(absolutePath);
        // Linux does not consider drive-prefixed mod paths rooted.
        path = PathTranslation.Untranslate(path);
        if (!Path.IsPathRooted(path))
            return path;

        if (s_staticReady)
        {
            var hit = s_staticMap;
            if (hit != null && hit.TryGetValue(path.ToLowerInvariant(), out var real))
                return real;
        }

        if (File.Exists(path) || Directory.Exists(path))
            return path;

        try
        {
            path = Path.GetFullPath(path);
        }
        catch
        { /* keep input on canonicalization failure */
        }

        if (s_staticReady)
        {
            var hit = s_staticMap;
            if (hit != null && hit.TryGetValue(path.ToLowerInvariant(), out var real))
                return real;
        }

        if (File.Exists(path) || Directory.Exists(path))
            return path;

        return WalkFromRoot(path) ?? path;
    }

    /// <summary>
    /// Resolves relative paths against the supplied root. Rooted input is
    /// resolved directly; misses retain the constructed path.
    /// </summary>
    public static string Resolve(string relativePath, string rootPath)
    {
        relativePath = PathHelpers.Normalize(relativePath);
        rootPath = PathHelpers.Normalize(rootPath);

        if (string.IsNullOrEmpty(relativePath))
            return relativePath;

        // Restore synthetic mod paths before the Linux rooted-path check.
        relativePath = PathTranslation.Untranslate(relativePath);

        if (Path.IsPathRooted(relativePath))
            return ResolveAbsolute(relativePath);

        if (!string.IsNullOrEmpty(rootPath))
        {
            var fullPath = Path.Combine(rootPath, relativePath).Replace('\\', '/');
            return Path.IsPathRooted(fullPath) ? ResolveAbsolute(fullPath) : fullPath;
        }

        // Root-relative static lookup is valid only without an explicit root.
        if (s_staticReady)
        {
            var hit = s_staticMap;
            if (hit != null && hit.TryGetValue(relativePath.ToLowerInvariant(), out var real))
                return real;
        }

        return relativePath;
    }

    private static string WalkFromRoot(string fullPath)
    {
        EnsureMutableRoots();

        string startRoot = "/";
        if (s_userDataRoot != null && PrefixMatches(fullPath, s_userDataRoot))
            startRoot = s_userDataRoot;
        if (
            s_modsRoot != null
            && PrefixMatches(fullPath, s_modsRoot)
            && (startRoot == "/" || s_modsRoot.Length > startRoot.Length)
        )
            startRoot = s_modsRoot;
        if (
            s_contentRoot != null
            && PrefixMatches(fullPath, s_contentRoot)
            && (startRoot == "/" || s_contentRoot.Length > startRoot.Length)
        )
            startRoot = s_contentRoot;
        if (
            s_bin64Root != null
            && PrefixMatches(fullPath, s_bin64Root)
            && (startRoot == "/" || s_bin64Root.Length > startRoot.Length)
        )
            startRoot = s_bin64Root;

        string rel;
        if (startRoot == "/")
        {
            rel = fullPath.TrimStart('/');
        }
        else
        {
            rel =
                fullPath.Length == startRoot.Length
                    ? string.Empty
                    : fullPath.Substring(startRoot.Length).TrimStart('/');
        }

        if (rel.Length == 0)
            return startRoot;

        var segments = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = startRoot;

        foreach (var seg in segments)
        {
            var entry = GetOrRefresh(current);
            if (entry.ChildMap == null)
                return null;

            if (entry.ChildMap.TryGetValue(seg, out var realName))
            {
                current = AppendChild(current, realName);
                continue;
            }

            var lower = seg.ToLowerInvariant();
            if (!ReferenceEquals(lower, seg) && entry.ChildMap.TryGetValue(lower, out realName))
            {
                current = AppendChild(current, realName);
                continue;
            }

            return null;
        }

        return current;
    }

    private static bool PrefixMatches(string fullPath, string root)
    {
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return false;
        return fullPath.Length == root.Length || fullPath[root.Length] == '/';
    }

    private static string AppendChild(string parent, string child) =>
        parent == "/" ? "/" + child : parent + "/" + child;

    private static DirEntry GetOrRefresh(string realCasedDirPath)
    {
        var entry = s_dirs.GetOrAdd(realCasedDirPath, _ => new DirEntry());

        long currentMtime = ReadMtime(realCasedDirPath);

        if (entry.ChildMap != null && entry.MtimeTicks == currentMtime)
            return entry;

        lock (entry.Sync)
        {
            currentMtime = ReadMtime(realCasedDirPath);
            if (entry.ChildMap != null && entry.MtimeTicks == currentMtime)
                return entry;

            Populate(entry, realCasedDirPath, currentMtime);
            return entry;
        }
    }

    private static long ReadMtime(string dirPath)
    {
        try
        {
            return Directory.GetLastWriteTimeUtc(dirPath).Ticks;
        }
        catch
        {
            return 0;
        }
    }

    private static void Populate(DirEntry entry, string dirPath, long mtime)
    {
        Dictionary<string, string> map = null;

        try
        {
            if (Directory.Exists(dirPath))
            {
                map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var sub in Directory.EnumerateFileSystemEntries(dirPath))
                {
                    var name = Path.GetFileName(sub);
                    if (string.IsNullOrEmpty(name))
                        continue;

                    map[name] = name;
                    var lower = name.ToLowerInvariant();
                    if (!ReferenceEquals(lower, name))
                        map[lower] = name;
                }
            }
        }
        catch
        {
            map = null;
        }

        entry.ChildMap = map;
        entry.MtimeTicks = mtime;
    }
}

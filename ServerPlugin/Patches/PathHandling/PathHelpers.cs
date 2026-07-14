using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using VRage.FileSystem;

namespace ServerPlugin.Patches.PathHandling;

// Two-level case-insensitive path cache. See dapper-juggling-scroll plan
// for rationale; replaces the disabled directory-keyed cache.
//
// Level 1 (static): one Dictionary<string,string> built once at game start
// by recursively walking Content/ and Bin64/. Lower-cased keys, real-cased
// values; both absolute and root-relative keys map to the same value. O(1)
// lookup, never invalidated (game files immutable per Docs/Plan.md).
//
// Level 2 (dynamic): per-directory ConcurrentDictionary covering everything
// outside Content/Bin64 (UserDataPath, ModsPath, temp, saves, blueprints).
// Mtime validated on every lookup; rebuilds the affected directory's child
// listing when files are created/deleted at runtime.
// Public so the Cecil prepatch on VRage.Game (MyModContextPrepatch) can
// emit a cross-assembly `call` into ToWindowsPath. The AssemblyResolve
// handler in Preloader.cs answers the VRage.Game-side AssemblyRef to
// LinuxCompat at runtime.
public static class PathHelpers
{
    /// <summary>
    /// Normalizes a path for Linux: converts backslashes to forward slashes
    /// and trims whitespace. Mirrors PathUtils.Normalize on Unix.
    /// </summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        // Fast path: already-normalized input avoids two Replace allocations
        // and the Trim. Hot callers (per-frame texture lookup, MyFileSystem
        // .FileExists prefix on already-rewritten paths) hit this branch.
        if (path.IndexOf('\\') < 0)
        {
            // Still trim — PathUtils.Normalize trimmed unconditionally.
            int start = 0;
            int end = path.Length;
            while (start < end && char.IsWhiteSpace(path[start])) start++;
            while (end > start && char.IsWhiteSpace(path[end - 1])) end--;
            if (start == 0 && end == path.Length)
                return path;
            return path.Substring(start, end - start);
        }

        path = path.Replace("\\\\", "/");
        path = path.Replace("\\", "/");
        return path.Trim();
    }

    /// <summary>
    /// Converts a Linux path to a Windows-shape path:
    /// <list type="bullet">
    ///   <item>Forward slashes are flipped to backslashes.</item>
    ///   <item>Known Linux roots (<c>~/.config/SpaceEngineers</c>,
    ///     <c>~/.steam/.../SpaceEngineers</c>, <c>/tmp</c>, <c>$HOME</c>)
    ///     are rewritten to their synthetic Windows equivalents via
    ///     <see cref="PathTranslation.Translate"/>, so the mod-visible
    ///     prefixes match the Windows reference shape.</item>
    ///   <item>Paths that don't hit a translation entry but are absolute
    ///     (start with <c>\</c>) and have no drive prefix get <c>C:</c>
    ///     prepended so mods checking the second char for drive-ness still
    ///     see a Windows-shape path.</item>
    ///   <item>Input that already has a drive prefix is left alone after
    ///     separator flipping (translation may still rewrite it if a
    ///     C:-prefixed Linux path slipped through earlier).</item>
    /// </list>
    /// Exposed only to mods — all callers are read-only egress from the
    /// wrapper classes in this folder. The engine never round-trips these
    /// strings back through the filesystem (it uses its own MyFileSystem
    /// paths), so synthesising a drive letter and rewriting prefixes is
    /// safe.
    /// </summary>
    public static string ToWindowsPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        var flipped = path.IndexOf('/') < 0 ? path : path.Replace('/', '\\');
        // Prefix-translate known Linux roots to synthetic Windows roots.
        // On a hit, Translate returns a drive-prefixed Windows path; on a
        // miss it returns the same string reference.
        var translated = PathTranslation.Translate(flipped);
        if (!ReferenceEquals(translated, flipped))
            return translated;
        // Already has a drive prefix — leave as-is.
        if (flipped.Length >= 2 && flipped[1] == ':' &&
            ((flipped[0] >= 'A' && flipped[0] <= 'Z') || (flipped[0] >= 'a' && flipped[0] <= 'z')))
            return flipped;
        // Absolute (rooted) but no drive — promote to C:.
        if (flipped.Length > 0 && flipped[0] == '\\')
            return "C:" + flipped;
        return flipped;
    }

    /// <summary>
    /// Cross-platform replacement for <see cref="Path.GetFileName(string)"/>.
    /// The BCL only recognizes `\` as a directory separator on Windows, so
    /// on Linux <c>Path.GetFileName("Textures\\foo.dds")</c> returns the
    /// whole string instead of the leaf. Transpiler patches in this
    /// namespace swap the static call to this helper for callers that
    /// consume literal Windows-style paths from game data (.tai atlas
    /// manifests, TransparentMaterials.sbc) without normalizing first.
    /// Signature matches <see cref="Path.GetFileName(string)"/> so the
    /// transpiler only needs to rewrite the call operand.
    /// </summary>
    public static string GetFileName(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        return Path.GetFileName(path.Replace('\\', '/'));
    }

    /// <summary>
    /// Cross-platform replacement for
    /// <see cref="Path.GetFileNameWithoutExtension(string)"/>. Same Linux
    /// separator issue as <see cref="GetFileName"/>.
    /// </summary>
    public static string GetFileNameWithoutExtension(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        return Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
    }

    /// <summary>
    /// Resolves a content-relative file path by normalizing separators and
    /// performing case-insensitive directory/file matching.
    /// </summary>
    public static string ResolveContentFilePath(string relativePath, string rootPath)
    {
        return PathCache.Resolve(relativePath, rootPath);
    }
}

/// <summary>
/// Backwards-compatibility shim. Existing callers (MyDefinitionManagerPatch,
/// MyAPIUtilitiesPatch) reference this name directly.
/// </summary>
static class CaseInsensitivePathResolver
{
    public static string Resolve(string relativePath, string rootPath)
        => PathCache.Resolve(relativePath, rootPath);
}

/// <summary>
/// Two-level case-insensitive resolver. Level 1 is a single flat dictionary
/// covering immutable Content/ and Bin64/; Level 2 is a per-directory cache
/// with mtime invalidation covering everything else.
/// </summary>
public static class PathCache
{
    // ---- Level 1: static cache for Content/ and Bin64/ ----
    //
    // Key: lower-cased path, either absolute or root-relative (forward
    // slashes, no trailing slash). Value: real-cased absolute path on disk.
    // Built once by BuildStaticCache(); read-only afterward.
    private static Dictionary<string, string> s_staticMap;
    private static volatile bool s_staticReady;
    // Real-cased absolute roots used as a prefix shortcut for Level 2 walks
    // (avoid walking from "/" when the input lives under a known root).
    private static string s_contentRoot;
    private static string s_bin64Root;

    // ---- Level 2: dynamic per-directory cache ----

    private sealed class DirEntry
    {
        // Lowercase child-name -> on-disk-cased child-name. Null means the
        // directory does not exist on disk (or enumeration failed).
        public Dictionary<string, string> ChildMap;
        // Directory.GetLastWriteTimeUtc().Ticks at the time ChildMap was
        // populated; -1 means never populated (forces refresh).
        public long MtimeTicks = -1;
        public readonly object Sync = new();
    }

    // Keyed by real-cased canonical absolute path of a directory (no
    // trailing slash). Ordinal comparer is correct because the walker only
    // ever produces canonical real-cased keys.
    private static readonly ConcurrentDictionary<string, DirEntry> s_dirs =
        new(StringComparer.Ordinal);

    // ---- Mutable-root prefixes used for walk shortcuts ----
    private static string s_modsRoot;
    private static string s_userDataRoot;
    private static int s_mutableRootsResolved;

    /// <summary>
    /// Build the Level 1 static cache by recursively enumerating Content/
    /// and Bin64/. Called once after MyFileSystem.Init populates ContentPath
    /// and ExePath. Idempotent — second calls return immediately.
    /// </summary>
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
        // Write to the volatile field has release semantics; readers that
        // observe s_staticReady == true are guaranteed to see s_staticMap.
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
        // The root itself.
        map[root.ToLowerInvariant()] = root;

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(
                root, "*", SearchOption.AllDirectories);
        }
        catch
        {
            return;
        }

        var rootLen = root.Length;
        foreach (var raw in entries)
        {
            string sub;
            try { sub = PathHelpers.Normalize(raw); }
            catch { continue; }

            if (string.IsNullOrEmpty(sub))
                continue;

            // Absolute lower-cased key.
            map[sub.ToLowerInvariant()] = sub;

            // Root-relative lower-cased key (no leading slash).
            if (sub.Length > rootLen &&
                sub.StartsWith(root, StringComparison.Ordinal) &&
                sub[rootLen] == '/')
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
        if (mods != null) s_modsRoot = mods;
        if (user != null) s_userDataRoot = user;
        if (s_modsRoot != null && s_userDataRoot != null)
            s_mutableRootsResolved = 1;
    }

    /// <summary>
    /// Resolve an absolute path case-insensitively. Returns the input
    /// unchanged on miss.
    /// </summary>
    public static string ResolveAbsolute(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
            return absolutePath;

        var path = PathHelpers.Normalize(absolutePath);
        // Mods that build absolute paths off ModContext.ModPath (e.g.
        // ColorfulIcons replacing every block icon with
        // `$"{ModContext.ModPath}\\Textures\\..."`) hand us Windows-shape
        // strings — drive-prefixed, "C:\...". Path.IsPathRooted on Linux
        // only recognizes a leading '/' as the root marker, so without an
        // Untranslate pass here the drive-prefixed path false-negatives the
        // rooted check below, early-returns unchanged, and File.Exists then
        // fails on the still-`C:`-prefixed string. Untranslate is a no-op
        // for paths without a drive prefix.
        path = PathTranslation.Untranslate(path);
        if (!Path.IsPathRooted(path))
            return path;

        // Level 1 probe (lower-cased absolute key).
        if (s_staticReady)
        {
            var hit = s_staticMap;
            if (hit != null && hit.TryGetValue(path.ToLowerInvariant(), out var real))
                return real;
        }

        // Fast path: file or directory exists with given casing.
        if (File.Exists(path) || Directory.Exists(path))
            return path;

        // Canonicalize ".." segments — some game callers join roots with
        // "../../../.config/SpaceEngineers/..." sequences.
        try { path = Path.GetFullPath(path); }
        catch { /* keep input on canonicalization failure */ }

        // Re-probe Level 1 after canonicalization.
        if (s_staticReady)
        {
            var hit = s_staticMap;
            if (hit != null && hit.TryGetValue(path.ToLowerInvariant(), out var real))
                return real;
        }

        if (File.Exists(path) || Directory.Exists(path))
            return path;

        // Level 2 walk.
        return WalkFromRoot(path) ?? path;
    }

    /// <summary>
    /// Resolve a relative path against an explicit root. If
    /// <paramref name="relativePath"/> is rooted it is resolved as an
    /// absolute path. Returns the (possibly unresolved) full path on miss.
    /// A non-empty <paramref name="rootPath"/> is authoritative: relative
    /// lookups are rooted there before probing the global content cache.
    /// </summary>
    public static string Resolve(string relativePath, string rootPath)
    {
        relativePath = PathHelpers.Normalize(relativePath);
        rootPath = PathHelpers.Normalize(rootPath);

        if (string.IsNullOrEmpty(relativePath))
            return relativePath;

        // A mod that builds an absolute asset path off ModContext.ModPath
        // (e.g. Light Block Improvements assigning
        // `def.Model = Path.Combine(ModContext.ModPath, "Models\\Foo.mwm")`)
        // hands us a Windows-shape, drive-prefixed string
        // ("C:/users/steamuser/.../Models/Foo.mwm"). Path.IsPathRooted on
        // Linux only treats a leading '/' as rooted, so such a path would
        // false-negative the check below, get joined onto rootPath (Content/),
        // and fail to load. Untranslate rewrites the drive prefix back to the
        // native path (making it rooted); it is a no-op for non-drive-prefixed
        // input, so relative/native callers are unaffected. Mirrors the same
        // pass already done in ResolveAbsolute.
        relativePath = PathTranslation.Untranslate(relativePath);

        if (Path.IsPathRooted(relativePath))
            return ResolveAbsolute(relativePath);

        if (!string.IsNullOrEmpty(rootPath))
        {
            var fullPath = Path.Combine(rootPath, relativePath).Replace('\\', '/');
            return Path.IsPathRooted(fullPath) ? ResolveAbsolute(fullPath) : fullPath;
        }

        // Level 1 probe via root-relative key (the static cache stores
        // root-relative keys for every Content/ and Bin64/ entry). This is
        // only valid when the caller did not provide an explicit root.
        if (s_staticReady)
        {
            var hit = s_staticMap;
            if (hit != null && hit.TryGetValue(relativePath.ToLowerInvariant(), out var real))
                return real;
        }

        return relativePath;
    }

    /// <summary>
    /// Walk <paramref name="fullPath"/> segment-by-segment from the longest
    /// known mutable-root prefix (or "/" as a fallback), looking up each
    /// segment in the Level 2 per-directory cache.
    ///
    /// Loop invariant: <c>current</c> is always the real-cased absolute
    /// path of an existing directory we have already resolved. Cache keys
    /// stay canonical because every key produced here came out of a real
    /// directory enumeration.
    /// </summary>
    private static string WalkFromRoot(string fullPath)
    {
        EnsureMutableRoots();

        // Pick the longest known root that matches the input prefix. Roots
        // come from MyFileSystem and are already real-cased, so we can skip
        // walking the parts above them.
        string startRoot = "/";
        if (s_userDataRoot != null && PrefixMatches(fullPath, s_userDataRoot))
            startRoot = s_userDataRoot;
        if (s_modsRoot != null && PrefixMatches(fullPath, s_modsRoot) &&
            (startRoot == "/" || s_modsRoot.Length > startRoot.Length))
            startRoot = s_modsRoot;
        // Content/Bin64 roots help if Level 1 didn't pre-load (e.g. early
        // boot before BuildStaticCache, or paths added after init).
        if (s_contentRoot != null && PrefixMatches(fullPath, s_contentRoot) &&
            (startRoot == "/" || s_contentRoot.Length > startRoot.Length))
            startRoot = s_contentRoot;
        if (s_bin64Root != null && PrefixMatches(fullPath, s_bin64Root) &&
            (startRoot == "/" || s_bin64Root.Length > startRoot.Length))
            startRoot = s_bin64Root;

        string rel;
        if (startRoot == "/")
        {
            rel = fullPath.TrimStart('/');
        }
        else
        {
            rel = fullPath.Length == startRoot.Length
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
                return null;  // Directory missing or unreadable.

            // Two-probe lookup: real-case first (no allocation when caller
            // already has correct casing), then lower-case fallback.
            if (entry.ChildMap.TryGetValue(seg, out var realName))
            {
                current = AppendChild(current, realName);
                continue;
            }

            var lower = seg.ToLowerInvariant();
            if (!ReferenceEquals(lower, seg) &&
                entry.ChildMap.TryGetValue(lower, out realName))
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
        // Boundary: either exact match, or next char is a separator.
        return fullPath.Length == root.Length || fullPath[root.Length] == '/';
    }

    private static string AppendChild(string parent, string child)
        => parent == "/" ? "/" + child : parent + "/" + child;

    /// <summary>
    /// Fetch or refresh the cached child listing for a real-cased directory
    /// path. Validates mtime on every call; rebuilds when the directory's
    /// last-write-time has advanced (file created/deleted) or when the
    /// entry has never been populated.
    /// </summary>
    private static DirEntry GetOrRefresh(string realCasedDirPath)
    {
        var entry = s_dirs.GetOrAdd(realCasedDirPath, _ => new DirEntry());

        long currentMtime = ReadMtime(realCasedDirPath);

        // Fast path: ChildMap populated and mtime unchanged.
        if (entry.ChildMap != null && entry.MtimeTicks == currentMtime)
            return entry;

        lock (entry.Sync)
        {
            // Re-check inside lock — another thread may have refreshed.
            currentMtime = ReadMtime(realCasedDirPath);
            if (entry.ChildMap != null && entry.MtimeTicks == currentMtime)
                return entry;

            Populate(entry, realCasedDirPath, currentMtime);
            return entry;
        }
    }

    private static long ReadMtime(string dirPath)
    {
        try { return Directory.GetLastWriteTimeUtc(dirPath).Ticks; }
        catch { return 0; }
    }

    /// <summary>
    /// (Re)build <paramref name="entry"/>.ChildMap by enumerating
    /// <paramref name="dirPath"/>. Each child is indexed under its on-disk
    /// name and (when different) under its lower-cased form, so the
    /// two-probe lookup in WalkFromRoot is branch-light.
    ///
    /// Errors (permission denied, race with deletion) leave ChildMap null;
    /// the walker treats null as a miss and the entry stays in the cache so
    /// subsequent retries hit the same per-entry lock instead of all
    /// thundering against the failing directory.
    /// </summary>
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

        // Single reference assignment per field — readers see either the
        // old map or the new map, never a torn state.
        entry.ChildMap = map;
        entry.MtimeTicks = mtime;
    }
}

using System;
using System.IO;
using ClientPlugin.Patches.PathHandling;

namespace ClientPlugin.Rewriter;

/// <summary>
/// Drop-in replacement for <see cref="System.IO.Path"/> that emulates Windows
/// semantics on a Linux host. The companion <see cref="PathSubstitutionRewriter"/>
/// rewrites every <c>System.IO.Path.X(...)</c> reference inside mod scripts to
/// <c>ClientPlugin.Rewriter.WindowsPath.X(...)</c> at compile time, so a mod
/// written against the Windows BCL behaves the same when its assembly is
/// loaded on Linux.
///
/// Reference behaviour (captured by the PathDiagnostics mod on Windows):
/// <list type="bullet">
///   <item><c>DirectorySeparatorChar='\'</c>, <c>AltDirectorySeparatorChar='/'</c>.</item>
///   <item>Both <c>\</c> and <c>/</c> are recognised as separators when parsing input.</item>
///   <item><c>Combine</c> emits <c>\</c> between segments.</item>
///   <item>A leading single <c>\</c> or <c>/</c> roots the path; a <c>X:</c> drive prefix also roots.</item>
/// </list>
///
/// Outputs from this class are not consumed by the host filesystem directly:
/// the game's filesystem patches (see <c>PathHelpers.Normalize</c>) translate
/// <c>\</c> back to <c>/</c> before any real I/O happens.
/// </summary>
public static class WindowsPath
{
    public const char DirectorySeparatorChar = '\\';
    public const char AltDirectorySeparatorChar = '/';
    public const char VolumeSeparatorChar = ':';
    public const char PathSeparator = ';';

    // The Windows invalid-char sets. Pulled from .NET Framework's published
    // values so mods that hash or count these arrays see the Windows shape.
    private static readonly char[] InvalidFileNameChars =
    [
        '"', '<', '>', '|', '\0',
        (char)1, (char)2, (char)3, (char)4, (char)5, (char)6, (char)7,
        (char)8, (char)9, (char)10, (char)11, (char)12, (char)13, (char)14,
        (char)15, (char)16, (char)17, (char)18, (char)19, (char)20, (char)21,
        (char)22, (char)23, (char)24, (char)25, (char)26, (char)27, (char)28,
        (char)29, (char)30, (char)31,
        ':', '*', '?', '\\', '/'
    ];

    private static readonly char[] InvalidPathChars =
    [
        '|', '\0',
        (char)1, (char)2, (char)3, (char)4, (char)5, (char)6, (char)7,
        (char)8, (char)9, (char)10, (char)11, (char)12, (char)13, (char)14,
        (char)15, (char)16, (char)17, (char)18, (char)19, (char)20, (char)21,
        (char)22, (char)23, (char)24, (char)25, (char)26, (char)27, (char)28,
        (char)29, (char)30, (char)31
    ];

    public static char[] GetInvalidFileNameChars() => (char[])InvalidFileNameChars.Clone();
    public static char[] GetInvalidPathChars() => (char[])InvalidPathChars.Clone();

    // ---- Predicate helpers -----------------------------------------------

    private static bool IsAnySeparator(char c) => c == '\\' || c == '/';

    /// <summary>
    /// Returns true if <paramref name="path"/> starts with a drive prefix
    /// like <c>"C:"</c>. Single-letter drive only — matches Windows behaviour.
    /// </summary>
    private static bool HasDrivePrefix(string path)
    {
        return path.Length >= 2 && path[1] == ':' &&
               ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'));
    }

    // ---- Inspection ------------------------------------------------------

    public static bool IsPathRooted(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        if (IsAnySeparator(path[0]))
            return true;
        return HasDrivePrefix(path);
    }

    public static string GetPathRoot(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        if (IsAnySeparator(path[0]))
        {
            // Windows collapses the form into "\" (or "\\" for UNC). The
            // diagnostic reference shows a single-backslash path returns "\".
            return DirectorySeparatorChar.ToString();
        }
        if (HasDrivePrefix(path))
        {
            // "C:" or "C:\" or "C:foo" — the root is always "C:\" when
            // separator follows, "C:" otherwise. Diagnostic reference shows
            // "C:\\Users\\..." returns "C:\\".
            if (path.Length >= 3 && IsAnySeparator(path[2]))
                return path.Substring(0, 2) + DirectorySeparatorChar;
            return path.Substring(0, 2);
        }
        return "";
    }

    public static string GetFileName(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        int last = -1;
        for (int i = path.Length - 1; i >= 0; i--)
        {
            char c = path[i];
            if (IsAnySeparator(c) || c == VolumeSeparatorChar)
            {
                last = i;
                break;
            }
        }
        return last < 0 ? path : path.Substring(last + 1);
    }

    public static string GetDirectoryName(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        int last = -1;
        for (int i = path.Length - 1; i >= 0; i--)
        {
            if (IsAnySeparator(path[i]))
            {
                last = i;
                break;
            }
        }
        if (last < 0)
            return "";
        // Trim trailing separators in the directory portion, then convert
        // any remaining `/` separators to `\` to match Windows output.
        int end = last;
        while (end > 0 && IsAnySeparator(path[end - 1]))
            end--;
        return ToBackslashes(path.Substring(0, end == 0 ? last : end));
    }

    public static string GetExtension(string path)
    {
        if (path == null)
            return null;
        for (int i = path.Length - 1; i >= 0; i--)
        {
            char c = path[i];
            if (c == '.')
                return i == path.Length - 1 ? "" : path.Substring(i);
            if (IsAnySeparator(c) || c == VolumeSeparatorChar)
                return "";
        }
        return "";
    }

    public static string GetFileNameWithoutExtension(string path)
    {
        var fileName = GetFileName(path);
        if (string.IsNullOrEmpty(fileName))
            return fileName;
        int dot = fileName.LastIndexOf('.');
        return dot < 0 ? fileName : fileName.Substring(0, dot);
    }

    public static bool HasExtension(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        for (int i = path.Length - 1; i >= 0; i--)
        {
            char c = path[i];
            if (c == '.')
                return i < path.Length - 1;
            if (IsAnySeparator(c) || c == VolumeSeparatorChar)
                return false;
        }
        return false;
    }

    public static string ChangeExtension(string path, string extension)
    {
        if (path == null)
            return null;
        int cut = path.Length;
        for (int i = path.Length - 1; i >= 0; i--)
        {
            char c = path[i];
            if (c == '.')
            {
                cut = i;
                break;
            }
            if (IsAnySeparator(c) || c == VolumeSeparatorChar)
                break;
        }
        string head = path.Substring(0, cut);
        if (extension == null)
            return head;
        if (extension.Length == 0)
            return head;
        return extension[0] == '.' ? head + extension : head + "." + extension;
    }

    // ---- Construction ----------------------------------------------------

    public static string Combine(string path1, string path2)
    {
        if (path1 == null) throw new ArgumentNullException(nameof(path1));
        if (path2 == null) throw new ArgumentNullException(nameof(path2));
        if (path2.Length == 0)
            return path1;
        if (path1.Length == 0 || IsPathRooted(path2))
            return path2;
        char last = path1[path1.Length - 1];
        if (IsAnySeparator(last) || last == VolumeSeparatorChar)
            return path1 + path2;
        return path1 + DirectorySeparatorChar + path2;
    }

    public static string Combine(string path1, string path2, string path3)
        => Combine(Combine(path1, path2), path3);

    public static string Combine(string path1, string path2, string path3, string path4)
        => Combine(Combine(Combine(path1, path2), path3), path4);

    public static string Combine(params string[] paths)
    {
        if (paths == null) throw new ArgumentNullException(nameof(paths));
        string result = "";
        for (int i = 0; i < paths.Length; i++)
        {
            if (paths[i] == null) throw new ArgumentNullException(nameof(paths));
            result = result.Length == 0 ? paths[i] : Combine(result, paths[i]);
        }
        return result;
    }

    public static string Join(string path1, string path2)
    {
        if (string.IsNullOrEmpty(path1)) return path2 ?? "";
        if (string.IsNullOrEmpty(path2)) return path1;
        char last = path1[path1.Length - 1];
        bool hasSep = IsAnySeparator(last) || IsAnySeparator(path2[0]);
        return hasSep ? path1 + path2 : path1 + DirectorySeparatorChar + path2;
    }

    public static string Join(string path1, string path2, string path3)
        => Join(Join(path1, path2), path3);

    public static string Join(string path1, string path2, string path3, string path4)
        => Join(Join(Join(path1, path2), path3), path4);

    public static string Join(params string[] paths)
    {
        if (paths == null) throw new ArgumentNullException(nameof(paths));
        string result = "";
        for (int i = 0; i < paths.Length; i++)
        {
            if (string.IsNullOrEmpty(paths[i])) continue;
            result = result.Length == 0 ? paths[i] : Join(result, paths[i]);
        }
        return result;
    }

    /// <summary>
    /// Returns an absolute path with backslash separators and a Windows-style
    /// drive prefix, mirroring how <see cref="Path.GetFullPath(string)"/>
    /// behaves on Windows:
    /// <list type="bullet">
    ///   <item>Input with drive prefix (<c>C:\...</c>) is returned with
    ///     separators flipped to <c>\</c>; the drive is preserved.</item>
    ///   <item>Input rooted with a leading separator (<c>\foo</c> or
    ///     <c>/foo</c>) is promoted to drive <c>C:</c>.</item>
    ///   <item>Relative input is resolved against the current working
    ///     directory (real Linux cwd, with separators flipped) and prefixed
    ///     with <c>C:</c>. The Bin64 directory names are Linux-shaped but the
    ///     path is otherwise Windows-rooted.</item>
    /// </list>
    /// Outputs from this method are not consumed by the host filesystem
    /// directly — the game's filesystem patches (PathHelpers.Normalize) strip
    /// the synthetic <c>C:</c> drive and flip <c>\</c> back to <c>/</c>.
    /// </summary>
    public static string GetFullPath(string path)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));

        if (HasDrivePrefix(path))
        {
            // Already an absolute Windows path. Flip separators and return.
            // Path.GetFullPath on Linux would treat this as a relative path
            // and prepend cwd, which is wrong for drive-prefixed input.
            // Run through Translate in case it's a C:-prefixed Linux path
            // from an earlier promotion that should be rewritten.
            var flipped = ToBackslashes(path);
            var translated = PathTranslation.Translate(flipped);
            return ReferenceEquals(translated, flipped) ? flipped : translated;
        }

        if (path.Length > 0 && IsAnySeparator(path[0]))
        {
            // Rooted with no drive — Windows promotes to current drive C:.
            // Translate first so e.g. "/home/<user>/..." maps to its
            // synthetic Windows equivalent before C: is prepended.
            var flipped = ToBackslashes(path);
            var translated = PathTranslation.Translate(flipped);
            return ReferenceEquals(translated, flipped) ? "C:" + flipped : translated;
        }

        // Relative path: resolve against the real Linux cwd. The resulting
        // absolute Linux path almost always lives under one of the
        // translation prefixes (Bin64 is under the SE install root); on a
        // miss fall back to C: prefix.
        string forward = path.Replace('\\', '/');
        string full = Path.GetFullPath(forward);
        string fullFlipped = ToBackslashes(full);
        var fullTranslated = PathTranslation.Translate(fullFlipped);
        return ReferenceEquals(fullTranslated, fullFlipped)
            ? "C:" + fullFlipped
            : fullTranslated;
    }

    public static string GetTempPath() => PathTranslation.TempPath;

    public static string GetTempFileName()
    {
        // Real Windows API hits the filesystem; mods that rely on the file
        // existing afterwards would break either way on a Linux host with a
        // rewritten path. We delegate to Path.GetTempFileName (a real Linux
        // temp file gets created) and then flip the separators.
        return ToBackslashes(Path.GetTempFileName());
    }

    public static string GetRandomFileName() => Path.GetRandomFileName();

    // ---- Mod-source rewriter targets ------------------------------------

    /// <summary>
    /// Translates a path produced by an engine API call into the Windows
    /// shape that mods expect. Emitted by <c>PathSubstitutionRewriter</c>
    /// around invocations whose return value is a filesystem path but whose
    /// receiver isn't a wrappable reference type (e.g. <c>ModItem.GetPath()</c>
    /// — a struct method, so no interface-dispatch wrapper can intercept it).
    ///
    /// Mirrors <c>PathHelpers.ToWindowsPath</c>: flip separators, apply the
    /// shared prefix-translation table, and promote rooted-no-drive paths
    /// to <c>C:</c> on miss.
    /// </summary>
    public static string FromGame(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        var flipped = ToBackslashes(path);
        var translated = PathTranslation.Translate(flipped);
        if (!ReferenceEquals(translated, flipped))
            return translated;
        if (HasDrivePrefix(flipped))
            return flipped;
        if (flipped[0] == '\\')
            return "C:" + flipped;
        return flipped;
    }

    /// <summary>
    /// Translates <c>item.GetPath()</c> in one call. The rewriter substitutes
    /// <c>modItem.GetPath()</c> with <c>WindowsPath.FromGame(modItem)</c> so it
    /// never has to synthesize an enclosing call around the invocation —
    /// previously that synthesis broke the conditional-access form
    /// <c>x?.ModItem.GetPath()</c> by stranding the <c>.</c> member-binding
    /// token outside its <c>?.</c> context.
    /// </summary>
    public static string FromGame(VRage.Game.MyObjectBuilder_Checkpoint.ModItem item)
    {
        return FromGame(item.GetPath());
    }

    /// <summary>
    /// Conditional-access companion to <see cref="FromGame(VRage.Game.MyObjectBuilder_Checkpoint.ModItem)"/>.
    /// The rewriter turns <c>x?.ModItem.GetPath()</c> into
    /// <c>WindowsPath.FromGame(x?.ModItem)</c>: peeling <c>.GetPath()</c> off
    /// the WhenNotNull spine makes the receiver type <see cref="System.Nullable{T}"/>,
    /// and this overload preserves null propagation — <c>x</c> being null
    /// flows through as a null return, matching the original short-circuit
    /// semantics.
    /// </summary>
    public static string FromGame(VRage.Game.MyObjectBuilder_Checkpoint.ModItem? item)
    {
        return item.HasValue ? FromGame(item.Value.GetPath()) : null;
    }

    // ---- Internal helpers ------------------------------------------------

    private static string ToBackslashes(string path)
    {
        if (string.IsNullOrEmpty(path) || path.IndexOf('/') < 0)
            return path;
        return path.Replace('/', '\\');
    }
}

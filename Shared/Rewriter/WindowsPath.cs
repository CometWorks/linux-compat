using System;
using System.IO;
using ClientPlugin.Patches.PathHandling;

namespace ClientPlugin.Rewriter;

/// <summary>
/// Emulates Windows <see cref="System.IO.Path"/> semantics for rewritten mod code.
/// Filesystem patches translate its output before Linux I/O.
/// </summary>
public static class WindowsPath
{
    public const char DirectorySeparatorChar = '\\';
    public const char AltDirectorySeparatorChar = '/';
    public const char VolumeSeparatorChar = ':';
    public const char PathSeparator = ';';

    // Preserve the .NET Framework invalid-character values exposed on Windows.
    private static readonly char[] InvalidFileNameChars =
    [
        '"',
        '<',
        '>',
        '|',
        '\0',
        (char)1,
        (char)2,
        (char)3,
        (char)4,
        (char)5,
        (char)6,
        (char)7,
        (char)8,
        (char)9,
        (char)10,
        (char)11,
        (char)12,
        (char)13,
        (char)14,
        (char)15,
        (char)16,
        (char)17,
        (char)18,
        (char)19,
        (char)20,
        (char)21,
        (char)22,
        (char)23,
        (char)24,
        (char)25,
        (char)26,
        (char)27,
        (char)28,
        (char)29,
        (char)30,
        (char)31,
        ':',
        '*',
        '?',
        '\\',
        '/',
    ];

    // Ordering matches .NET Framework Path.GetInvalidPathChars() on Windows.
    private static readonly char[] InvalidPathChars =
    [
        '"',
        '<',
        '>',
        '|',
        '\0',
        (char)1,
        (char)2,
        (char)3,
        (char)4,
        (char)5,
        (char)6,
        (char)7,
        (char)8,
        (char)9,
        (char)10,
        (char)11,
        (char)12,
        (char)13,
        (char)14,
        (char)15,
        (char)16,
        (char)17,
        (char)18,
        (char)19,
        (char)20,
        (char)21,
        (char)22,
        (char)23,
        (char)24,
        (char)25,
        (char)26,
        (char)27,
        (char)28,
        (char)29,
        (char)30,
        (char)31,
    ];

    public static char[] GetInvalidFileNameChars() => (char[])InvalidFileNameChars.Clone();

    public static char[] GetInvalidPathChars() => (char[])InvalidPathChars.Clone();

    private static bool IsAnySeparator(char c) => c == '\\' || c == '/';

    private static bool HasDrivePrefix(string path)
    {
        return path.Length >= 2
            && path[1] == ':'
            && ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'));
    }

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
            // Windows returns a single separator for separator-rooted paths.
            return DirectorySeparatorChar.ToString();
        }
        if (HasDrivePrefix(path))
        {
            // Include the separator only when it follows the drive prefix.
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

    public static string Combine(string path1, string path2)
    {
        if (path1 == null)
            throw new ArgumentNullException(nameof(path1));
        if (path2 == null)
            throw new ArgumentNullException(nameof(path2));
        if (path2.Length == 0)
            return path1;
        if (path1.Length == 0 || IsPathRooted(path2))
            return path2;
        char last = path1[path1.Length - 1];
        if (IsAnySeparator(last) || last == VolumeSeparatorChar)
            return path1 + path2;
        return path1 + DirectorySeparatorChar + path2;
    }

    public static string Combine(string path1, string path2, string path3) =>
        Combine(Combine(path1, path2), path3);

    public static string Combine(string path1, string path2, string path3, string path4) =>
        Combine(Combine(Combine(path1, path2), path3), path4);

    public static string Combine(params string[] paths)
    {
        if (paths == null)
            throw new ArgumentNullException(nameof(paths));
        string result = "";
        for (int i = 0; i < paths.Length; i++)
        {
            if (paths[i] == null)
                throw new ArgumentNullException(nameof(paths));
            result = result.Length == 0 ? paths[i] : Combine(result, paths[i]);
        }
        return result;
    }

    public static string Join(string path1, string path2)
    {
        if (string.IsNullOrEmpty(path1))
            return path2 ?? "";
        if (string.IsNullOrEmpty(path2))
            return path1;
        char last = path1[path1.Length - 1];
        bool hasSep = IsAnySeparator(last) || IsAnySeparator(path2[0]);
        return hasSep ? path1 + path2 : path1 + DirectorySeparatorChar + path2;
    }

    public static string Join(string path1, string path2, string path3) =>
        Join(Join(path1, path2), path3);

    public static string Join(string path1, string path2, string path3, string path4) =>
        Join(Join(Join(path1, path2), path3), path4);

    public static string Join(params string[] paths)
    {
        if (paths == null)
            throw new ArgumentNullException(nameof(paths));
        string result = "";
        for (int i = 0; i < paths.Length; i++)
        {
            if (string.IsNullOrEmpty(paths[i]))
                continue;
            result = result.Length == 0 ? paths[i] : Join(result, paths[i]);
        }
        return result;
    }

    /// <summary>
    /// Returns a Windows-shaped absolute path for mod code.
    /// Relative paths use the Linux working directory before translation.
    /// </summary>
    public static string GetFullPath(string path)
    {
        if (path == null)
            throw new ArgumentNullException(nameof(path));

        if (HasDrivePrefix(path))
        {
            // Linux Path.GetFullPath treats drive-prefixed input as relative.
            var flipped = ToBackslashes(path);
            var translated = PathTranslation.Translate(flipped);
            return ReferenceEquals(translated, flipped) ? flipped : translated;
        }

        if (path.Length > 0 && IsAnySeparator(path[0]))
        {
            // Windows promotes separator-rooted paths to the current drive.
            var flipped = ToBackslashes(path);
            var translated = PathTranslation.Translate(flipped);
            return ReferenceEquals(translated, flipped) ? "C:" + flipped : translated;
        }

        string forward = path.Replace('\\', '/');
        string full = Path.GetFullPath(forward);
        string fullFlipped = ToBackslashes(full);
        var fullTranslated = PathTranslation.Translate(fullFlipped);
        return ReferenceEquals(fullTranslated, fullFlipped) ? "C:" + fullFlipped : fullTranslated;
    }

    public static string GetTempPath() => PathTranslation.TempPath;

    public static string GetTempFileName()
    {
        // Preserve GetTempFileName's side effect of creating a real file.
        // Translate so the result lives under the same root GetTempPath reports.
        return FromGame(Path.GetTempFileName());
    }

    public static string GetRandomFileName() => Path.GetRandomFileName();

    /// <summary>
    /// Translates engine paths to the Windows shape exposed to mods.
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
    /// Rewriter target for the struct method <c>ModItem.GetPath()</c>.
    /// </summary>
    public static string FromGame(VRage.Game.MyObjectBuilder_Checkpoint.ModItem item)
    {
        return FromGame(item.GetPath());
    }

    /// <summary>
    /// Preserves null propagation for rewritten conditional access.
    /// </summary>
    public static string FromGame(VRage.Game.MyObjectBuilder_Checkpoint.ModItem? item)
    {
        return item.HasValue ? FromGame(item.Value.GetPath()) : null;
    }

    private static string ToBackslashes(string path)
    {
        if (string.IsNullOrEmpty(path) || path.IndexOf('/') < 0)
            return path;
        return path.Replace('/', '\\');
    }
}

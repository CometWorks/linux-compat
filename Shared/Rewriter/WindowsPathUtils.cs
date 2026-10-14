using System;
using System.IO;
using System.Security;
using ClientPlugin.Patches.PathHandling;
using VRage.Library.Utils;

namespace ClientPlugin.Rewriter;

/// <summary>
/// Stands in for <see cref="PathUtils"/> in rewritten mod code. Paths go in through
/// <see cref="WindowsPath.ToGame"/> and come back through <see cref="WindowsPath.FromGame(string)"/>,
/// and the paths in exception messages are translated too, so mods see the same Windows-shaped
/// paths as everywhere else and never the Linux user's home folder.
/// </summary>
public static class WindowsPathUtils
{
    /// <summary>
    /// Still rejects anything outside the local Mods folder and Content/Data, Workshop mods
    /// included, as on Windows.
    /// </summary>
    public static void EnsureSafePath(string path)
    {
        try
        {
            PathUtils.EnsureSafePath(WindowsPath.ToGame(path));
        }
        catch (Exception e) when (Translated(e) is { } translated)
        {
            throw translated;
        }
    }

    public static string[] GetFilesRecursively(string path, string searchPattern)
    {
        string[] files;
        try
        {
            files = PathUtils.GetFilesRecursively(WindowsPath.ToGame(path), searchPattern);
        }
        catch (Exception e) when (Translated(e) is { } translated)
        {
            throw translated;
        }

        for (int i = 0; i < files.Length; i++)
            files[i] = WindowsPath.FromGame(files[i]);
        return files;
    }

    /// <summary>The Windows branch of <see cref="PathUtils.Normalize"/>.</summary>
    public static string Normalize(string path) => path.Replace('/', '\\').Replace(@"\\", @"\");

    // A copy with the paths in the message translated. The original is not kept as the inner
    // exception, because its message names the native path.
    private static Exception Translated(Exception e)
    {
        var message = PathTranslation.TranslateText(e.Message);
        if (message == e.Message)
            return null;
        return e switch
        {
            SecurityException => new SecurityException(message),
            DirectoryNotFoundException => new DirectoryNotFoundException(message),
            FileNotFoundException => new FileNotFoundException(message),
            PathTooLongException => new PathTooLongException(message),
            IOException => new IOException(message),
            UnauthorizedAccessException => new UnauthorizedAccessException(message),
            _ => null,
        };
    }
}

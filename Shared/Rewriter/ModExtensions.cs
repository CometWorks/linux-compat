using System;

namespace ClientPlugin.Rewriter.ModExtensions;

/// <summary>
/// Lets the rewriter translate a path read in the middle of a conditional access chain, where the
/// read cannot be wrapped in a call: <c>a?.GamePaths.UserDataPath.Trim()</c> becomes
/// <c>a?.GamePaths.LinuxCompatFromGame(o => o.UserDataPath).Trim()</c>. The rewriter imports this
/// namespace into the mod files it uses it in.
/// </summary>
public static class LinuxCompatPathExtensions
{
    public static string LinuxCompatFromGame<T>(this T owner, Func<T, string> read) =>
        WindowsPath.FromGame(read(owner));

    public static string LinuxCompatFromGameText<T>(this T owner, Func<T, string> read) =>
        WindowsPath.FromGameText(read(owner));

    public static string[] LinuxCompatFromGameText<T>(this T owner, Func<T, string[]> read) =>
        WindowsPath.FromGameText(read(owner));
}

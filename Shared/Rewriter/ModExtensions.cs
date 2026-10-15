using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace ClientPlugin.Rewriter.ModExtensions;

/// <summary>
/// Lets the rewriter translate a path read in the middle of a conditional access chain, where the
/// read cannot be wrapped in a call: <c>a?.GamePaths.UserDataPath.Trim()</c> becomes
/// <c>a?.GamePaths.LinuxCompatFromGame(o => o.UserDataPath).Trim()</c>. It also renames calls that
/// return a path through an <c>out</c> argument to the extensions here. The rewriter imports this
/// namespace into the mod files it uses it in.
/// </summary>
public static class LinuxCompatPathExtensions
{
    public static string LinuxCompatFromGame<T>(this T owner, Func<T, string> read) =>
        WindowsPath.FromGame(read(owner));

    public static List<string> LinuxCompatFromGame<T>(this T owner, Func<T, List<string>> read) =>
        WindowsPath.FromGame(read(owner));

    public static Exception[] LinuxCompatFromGame<T>(this T owner, Func<T, Exception[]> read) =>
        WindowsPath.FromGame(read(owner));

    public static MyObjectBuilder_DefinitionBase LinuxCompatFromGame<T>(
        this T owner,
        Func<T, MyObjectBuilder_DefinitionBase> read
    ) => WindowsPath.FromGame(read(owner));

    public static string LinuxCompatFromGameText<T>(this T owner, Func<T, string> read) =>
        WindowsPath.FromGameText(read(owner));

    public static string[] LinuxCompatFromGameText<T>(this T owner, Func<T, string[]> read) =>
        WindowsPath.FromGameText(read(owner));

    public static List<string> LinuxCompatFromGameText<T>(
        this T owner,
        Func<T, List<string>> read
    ) => WindowsPath.FromGameText(read(owner));

    /// <summary>
    /// The rewriter renames <c>block.CalcLocalMatrix(out m, out model)</c> to this, because an
    /// <c>out</c> path cannot be wrapped where it is passed.
    /// </summary>
    public static void LinuxCompatCalcLocalMatrix(
        this IMyCubeBlock block,
        out Matrix localMatrix,
        out string currModel
    )
    {
        block.CalcLocalMatrix(out localMatrix, out currModel);
        currModel = WindowsPath.FromGame(currModel);
    }
}

using System;
using Microsoft.CodeAnalysis;

namespace VRage.Scripting
{
    // The game's enum, same members in the same order.
    public enum MyApiTarget
    {
        None,
        Mod,
        Ingame,
    }
}

// Stand-ins for the shims. As in the plugin, the shim reference is the assembly holding the
// rewriter, so these are what mod source binds to. Only the members the cases use.
namespace ClientPlugin.Rewriter
{
    // Replaces the real ShimRegistration, which reaches into MyScriptCompiler.
    internal static class ShimRegistration
    {
        public static PortableExecutableReference Reference { get; } =
            MetadataReference.CreateFromFile(typeof(ShimRegistration).Assembly.Location);
    }

    public static class WindowsPath
    {
        public static string ToGame(string path) => path;

        public static string FromGame(string path) => path;

        public static string ToGameText(string text) => text;

        public static string FromGameText(string text) => text;

        public static string[] ToGameText(string[] texts) => texts;

        public static string[] FromGameText(string[] texts) => texts;
    }
}

namespace ClientPlugin.Rewriter.ModExtensions
{
    public static class LinuxCompatPathExtensions
    {
        public static string LinuxCompatFromGame<T>(this T owner, Func<T, string> read) =>
            read(owner);

        public static string LinuxCompatFromGameText<T>(this T owner, Func<T, string> read) =>
            read(owner);

        public static string[] LinuxCompatFromGameText<T>(this T owner, Func<T, string[]> read) =>
            read(owner);

        public static void LinuxCompatCalcLocalMatrix() { }
    }
}

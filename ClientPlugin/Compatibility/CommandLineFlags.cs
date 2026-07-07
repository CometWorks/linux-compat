using System;
using System.Linq;

namespace ClientPlugin.Compatibility;

internal static class CommandLineFlags
{
    internal static bool Headless { get; } = Environment.GetCommandLineArgs()
        .Any(arg => string.Equals(arg, "--headless", StringComparison.OrdinalIgnoreCase));
}

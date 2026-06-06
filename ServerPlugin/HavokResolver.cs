using System;
using System.IO;
using ServerPlugin.Compatibility;

namespace ServerPlugin;

internal static class HavokResolver
{
    public static void Initialize()
    {
        var gameRoot = Environment.GetEnvironmentVariable("SPACE_ENGINEERS_ROOT");
        if (string.IsNullOrEmpty(gameRoot))
        {
            Console.WriteLine("[LinuxCompat] WARNING: SPACE_ENGINEERS_ROOT not set, cannot initialize Havok");
            return;
        }

        var havokDllPath = Path.Combine(gameRoot, "Bin64", "Havok.dll");
        if (!File.Exists(havokDllPath))
        {
            Console.WriteLine($"[LinuxCompat] WARNING: Havok.dll not found at {havokDllPath}");
            return;
        }

        HavokLinux.Init(havokDllPath);
        Console.WriteLine($"[LinuxCompat] Havok initialized: {havokDllPath}");
    }
}

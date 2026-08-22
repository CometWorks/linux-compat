using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;
using VRage.FileSystem;

namespace ClientPlugin.Compatibility;

internal static class NativeLibraries
{
    private static readonly Dictionary<string, string> Aliases = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["Havok.dll"] = "libHavok.so",
        ["RecastDetour.dll"] = "libRecastDetour.so",
        ["VRage.Native.dll"] = "libVRageNative.so",
        ["d3d11"] = "libdxvk_d3d11.so",
        ["d3d11.dll"] = "libdxvk_d3d11.so",
        ["dxgi"] = "libdxvk_dxgi.so",
        ["dxgi.dll"] = "libdxvk_dxgi.so",
        ["openal"] = "libopenal.so",
        ["OpenAL32"] = "libopenal.so",
        ["OpenAL32.dll"] = "libopenal.so",
        ["soft_oal"] = "libopenal.so",
        ["soft_oal.dll"] = "libopenal.so",
        ["EOSSDK-Shipping"] = "libEOSSDK-Linux-Shipping.so",
        ["EOSSDK-Shipping.dll"] = "libEOSSDK-Linux-Shipping.so",
        ["steam_api64"] = "libsteam_api.so",
        ["steam_api64.dll"] = "libsteam_api.so",
    };

    private static int s_initialized;

#if !MAGNETAR
    [DllImport("libc", CallingConvention = CallingConvention.Cdecl, EntryPoint = "setenv")]
    private static extern int SetEnvironmentVariableNative(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value,
        int overwrite
    );
#endif

    internal static void Initialize()
    {
        if (Interlocked.Exchange(ref s_initialized, 1) != 0)
            return;

#if !MAGNETAR
        // DXVK reads these with getenv, which ignores Environment.SetEnvironmentVariable on Unix.
        SetEnvironmentVariableNative("DXVK_WSI_DRIVER", "SDL3", overwrite: 0);

        // "none" stops DXVK writing <exe>_dxgi.log / <exe>_d3d11.log; it still logs to stderr.
        SetEnvironmentVariableNative("DXVK_LOG_PATH", "none", overwrite: 0);
#endif

        AssemblyLoadContext.Default.ResolvingUnmanagedDll += Resolve;
    }

    private static IntPtr Resolve(Assembly assembly, string libraryName)
    {
        switch (libraryName.ToUpperInvariant())
        {
            case "HAVOK.DLL":
                HavokLinux.EnsureInitialized();
                break;
            case "RECASTDETOUR.DLL":
                RecastDetourLinux.EnsureInitialized();
                break;
            case "VRAGE.NATIVE.DLL":
                VRageNativeLinux.EnsureInitialized();
                break;
        }

        return
            Aliases.TryGetValue(libraryName, out var target)
            && NativeLibrary.TryLoad(
                target,
                typeof(NativeLibraries).Assembly,
                searchPath: null,
                out var handle
            )
            ? handle
            : IntPtr.Zero;
    }
}

internal static class NativeWrapper
{
#if MAGNETAR
    private const string DataDirectory = "SpaceEngineersDedicated";
#else
    private const string DataDirectory = "SpaceEngineers";
#endif

    private static readonly Lazy<string> CacheDirectory = new(CreateCacheDirectory);

    internal static bool Initialize(string dllName, Action<string, string> initialize)
    {
        var dllPath = Path.Combine(MyFileSystem.ExePath, dllName);
        if (!File.Exists(dllPath))
            throw new FileNotFoundException("Original Windows library was not found.", dllPath);

        var cacheDirectory = CacheDirectory.Value;
        var sidecarPath = cacheDirectory == null ? null : Path.Combine(cacheDirectory, dllName);
        initialize(dllPath, sidecarPath);
        Console.WriteLine(
            $"[LinuxCompat] initialized {dllName}: {dllPath} (sidecar: {sidecarPath ?? "<none>"})"
        );
        return true;
    }

    private static string CreateCacheDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            DataDirectory,
            "NativeWrapperCache"
        );
        if (TryCreateDirectory(directory))
            return directory;

        directory = Path.Combine(
            Path.GetTempPath(),
            $"{DataDirectory}-NativeWrapperCache-{Environment.UserName}"
        );
        return TryCreateDirectory(directory) ? directory : null;
    }

    private static bool TryCreateDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(
                $"[LinuxCompat] WARNING: cannot create NativeWrapperCache at {directory}: {e.Message}"
            );
            return false;
        }
    }
}

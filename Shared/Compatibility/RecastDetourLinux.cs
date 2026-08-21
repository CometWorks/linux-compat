using System;
using System.Runtime.InteropServices;

namespace ClientPlugin.Compatibility;

public static class RecastDetourLinux
{
    private static readonly Lazy<bool> Initialized = new(() =>
        NativeWrapper.Initialize("RecastDetour.dll", Init)
    );

    [DllImport("libRecastDetour.so", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Init(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string dllPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sidecarPath
    );

    internal static void EnsureInitialized() => _ = Initialized.Value;
}

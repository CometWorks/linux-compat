using System;
using System.Runtime.InteropServices;

namespace ClientPlugin.Compatibility;

public static class HavokLinux
{
    private static readonly Lazy<bool> Initialized = new(() =>
        NativeWrapper.Initialize("Havok.dll", Init)
    );

    [DllImport("libHavok.so", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Init(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string dllPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sidecarPath
    );

    internal static void EnsureInitialized() => _ = Initialized.Value;
}

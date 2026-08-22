using System;
using System.Runtime.InteropServices;

namespace ClientPlugin.Compatibility;

public static class VRageNativeLinux
{
    private static readonly Lazy<bool> Initialized = new(() =>
        NativeWrapper.Initialize("VRage.Native.dll", Init)
    );

    [DllImport("libVRageNative.so", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Init(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string dllPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sidecarPath
    );

    internal static void EnsureInitialized() => _ = Initialized.Value;
}

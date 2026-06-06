using System.Runtime.InteropServices;

namespace ServerPlugin.Compatibility;

public static class VRageNativeLinux
{
    [DllImport("libVRageNative.so")]
    public static extern void Init(string dllPath);
}

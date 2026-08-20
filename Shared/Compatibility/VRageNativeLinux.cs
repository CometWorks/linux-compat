using System.Runtime.InteropServices;

namespace ClientPlugin.Compatibility;

public static class VRageNativeLinux
{
    [DllImport("libVRageNative.so")]
    public static extern void Init(string dllPath);
}

using System.Runtime.InteropServices;

namespace ServerPlugin.Compatibility;

public static class HavokLinux
{
    [DllImport("libHavok.so")]
    public static extern void Init(string dllPath);
}

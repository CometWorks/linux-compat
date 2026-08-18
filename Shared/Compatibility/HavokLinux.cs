using System.Runtime.InteropServices;

namespace ClientPlugin.Compatibility;

public static class HavokLinux
{
    [DllImport("libHavok.so")]
    public static extern void Init(string dllPath);
}

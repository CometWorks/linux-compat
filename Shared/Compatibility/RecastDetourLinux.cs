using System.Runtime.InteropServices;

namespace ClientPlugin.Compatibility;

public static class RecastDetourLinux
{
    [DllImport("libRecastDetour.so")]
    public static extern void Init(string dllPath);
}

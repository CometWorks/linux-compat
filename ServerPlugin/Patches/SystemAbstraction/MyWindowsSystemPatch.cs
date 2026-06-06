// Init, CPUCounter, RAMCounter, ProcessPrivateMemory are handled by the preloader
// because they reference System.Diagnostics.PerformanceCounter or psapi.dll P/Invoke
// which prevents Harmony from parsing the IL.

using System;
using System.Diagnostics;
using System.IO;
using HarmonyLib;

namespace ServerPlugin.Patches.SystemAbstraction;

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "GetOsName")]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemGetOsNamePatch
{
    static bool Prefix(ref string __result)
    {
        __result = "Linux";
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "GetTotalPhysicalMemory")]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemGetTotalPhysicalMemoryPatch
{
    static bool Prefix(ref ulong __result)
    {
        __result = TryReadMemInfo("MemTotal");
        return false;
    }

    static ulong TryReadMemInfo(string key)
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith(key + ":", StringComparison.Ordinal))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && ulong.TryParse(parts[1], out var result))
                        return result * 1024;
                }
            }
        }
        catch
        {
        }
        return 0;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "ThreeLetterISORegionName", MethodType.Getter)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemThreeLetterISOPatch
{
    static bool Prefix(ref string __result)
    {
        __result = string.Empty;
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "TwoLetterISORegionName", MethodType.Getter)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemTwoLetterISOPatch
{
    static bool Prefix(ref string __result)
    {
        __result = string.Empty;
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "RegionLatitude", MethodType.Getter)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemRegionLatitudePatch
{
    static bool Prefix(ref string __result)
    {
        __result = string.Empty;
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "RegionLongitude", MethodType.Getter)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemRegionLongitudePatch
{
    static bool Prefix(ref string __result)
    {
        __result = string.Empty;
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "HasSwappedMouseButtons", MethodType.Getter)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemHasSwappedMouseButtonsPatch
{
    static bool Prefix(ref bool __result)
    {
        __result = false;
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "IsUsingGeforceNow", MethodType.Getter)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemIsUsingGeforceNowPatch
{
    static bool Prefix(ref bool __result)
    {
        __result = false;
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "IsUsingGeforceNowCloud", MethodType.Getter)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemIsUsingGeforceNowCloudPatch
{
    static bool Prefix(ref bool __result)
    {
        __result = false;
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "IsSingleInstance", MethodType.Getter)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemIsSingleInstancePatch
{
    static bool Prefix(ref bool __result)
    {
        __result = true;
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "LogEnvironmentInformation")]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemLogEnvironmentInformationPatch
{
    static bool Prefix()
    {
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "OpenUrl")]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemOpenUrlPatch
{
    static bool Prefix(string url, ref bool __result)
    {
        try
        {
            var uri = new Uri(url);
            if (uri.Scheme == "https")
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri.ToString(),
                    UseShellExecute = true
                });
            }
            __result = true;
        }
        catch
        {
            __result = false;
        }
        return false;
    }
}

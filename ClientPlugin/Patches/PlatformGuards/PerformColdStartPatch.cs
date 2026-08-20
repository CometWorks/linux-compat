using System.IO;
using HarmonyLib;
using Sandbox;
using Sandbox.Engine.Utils;
using VRage.FileSystem;

namespace ClientPlugin.Patches.PlatformGuards;

// NGEN is unavailable on Linux and unnecessary under tiered JIT. Preserve the
// cold-start marker so assembly preloading does not repeat.
[HarmonyPatch(typeof(MyCommonProgramStartup), nameof(MyCommonProgramStartup.PerformColdStart))]
[HarmonyPatchCategory("Finish")]
static class PerformColdStartPatch
{
    static void Prefix()
    {
        MyFakes.ENABLE_NGEN = false;
    }

    static void Postfix()
    {
        // The marker normally belongs to the disabled NGEN block.
        var path = Path.Combine(MyFileSystem.UserDataPath, "ColdStart.txt");
        if (!File.Exists(path))
        {
            File.Create(path).Dispose();
        }
    }
}

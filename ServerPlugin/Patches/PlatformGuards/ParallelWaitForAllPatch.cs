using System;
using System.Threading;
using HarmonyLib;
using ParallelTasks;

namespace ServerPlugin.Patches.PlatformGuards;

[HarmonyPatch(typeof(Parallel), nameof(Parallel.WaitForAll))]
[HarmonyPatchCategory("Init")]
static class ParallelWaitForAllPatch
{
    static bool Prefix(WaitHandle[] waitHandles, TimeSpan timeout, ref bool __result)
    {
        __result = WaitHandle.WaitAll(waitHandles, timeout);
        return false;
    }
}

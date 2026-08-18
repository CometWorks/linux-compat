using System;
using System.Threading;
using HarmonyLib;
using ParallelTasks;

namespace ClientPlugin.Patches.PlatformGuards;

[HarmonyPatch(typeof(Parallel), nameof(Parallel.WaitForAll))]
#if DEDICATED
[HarmonyPatchCategory("Finish")]
#else
[HarmonyPatchCategory("Init")]
#endif
static class ParallelWaitForAllPatch
{
    static bool Prefix(WaitHandle[] waitHandles, TimeSpan timeout, ref bool __result)
    {
        __result = WaitHandle.WaitAll(waitHandles, timeout);
        return false;
    }
}

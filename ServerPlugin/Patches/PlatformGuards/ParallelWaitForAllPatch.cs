using System;
using System.Threading;
using HarmonyLib;
using ParallelTasks;

namespace ServerPlugin.Patches.PlatformGuards;

// Applied in the "Finish" category (Preloader.Finish runs before MySandboxGame
// is constructed) rather than "Init" (which runs from Plugin.Init, only
// reached after MySandboxGame.Run -> Initialize() returns successfully).
// If Initialize() throws -- which happens early during the bring-up while
// later subsystems are still being ported -- the `using` block in
// DedicatedServer.RunInternal disposes MySandboxGame, and Dispose calls
// PrioritizedScheduler.WaitForTasksToFinish -> Parallel.WaitForAll, which
// in turn calls Thread.SetApartmentState(STA). On Linux .NET that throws
// PlatformNotSupportedException ("COM Interop is not supported on this
// platform"), which surfaces as the only console-visible exception and
// masks the original Initialize() failure. Applying this patch in "Finish"
// ensures Parallel.WaitForAll is replaced before any of that happens.
[HarmonyPatch(typeof(Parallel), nameof(Parallel.WaitForAll))]
[HarmonyPatchCategory("Finish")]
static class ParallelWaitForAllPatch
{
    static bool Prefix(WaitHandle[] waitHandles, TimeSpan timeout, ref bool __result)
    {
        __result = WaitHandle.WaitAll(waitHandles, timeout);
        return false;
    }
}

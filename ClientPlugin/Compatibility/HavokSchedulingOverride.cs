using System;
using Sandbox.Engine.Utils;

namespace ClientPlugin.Compatibility;

/// <summary>
/// Diagnostic override of the Havok stepping mode, for isolating native
/// physics crashes (see defect L3, the Aerodynamic Physics SIGSEGV).
///
///   SE_HAVOK_SEQUENTIAL=1    step clusters sequentially on the main thread
///                            (MyPhysics.StepWorldsSequential) instead of
///                            draining the job queue from many threads; each
///                            world still steps with Havok multithreading.
///   SE_HAVOK_SINGLETHREAD=1  additionally step each world single-threaded
///                            (implies sequential).
///
/// MyFakes' static initializer turns both flags on, and a multiplayer event
/// can rewrite them, so the override is re-asserted every frame from
/// Plugin.Update rather than set once at init.
/// </summary>
internal static class HavokSchedulingOverride
{
    private static readonly bool Sequential = IsSet("SE_HAVOK_SEQUENTIAL") || IsSet("SE_HAVOK_SINGLETHREAD");
    private static readonly bool SingleThread = IsSet("SE_HAVOK_SINGLETHREAD");

    private static bool _logged;

    private static bool IsSet(string name) => Environment.GetEnvironmentVariable(name) == "1";

    public static void Apply()
    {
        if (!Sequential)
            return;

        MyFakes.ENABLE_HAVOK_PARALLEL_SCHEDULING = false;
        if (SingleThread)
            MyFakes.ENABLE_HAVOK_MULTITHREADING = false;

        if (!_logged && VRage.Utils.MyLog.Default != null)
        {
            _logged = true;
            VRage.Utils.MyLog.Default.WriteLine(
                $"[LinuxCompat] Havok scheduling override active: sequential=true singleThread={SingleThread}");
        }
    }
}

using HarmonyLib;
using Havok;

namespace ClientPlugin.Patches.Diagnostics;

/// <summary>
/// Diagnostic kill switch for HkHandle finalization (SE_HAVOK_NO_FINALIZE=1),
/// for isolating native physics crashes (defect L3).
///
/// ~HkHandle releases native Havok objects from the .NET finalizer thread
/// (HkBaseSystem.InitThread -> Dispose(false) -> QuitThread). If a managed
/// wrapper dies while native Havok still references the object - the modern
/// JIT ends lifetimes at last use, unlike .NET Framework's - that release
/// frees memory the physics step is still walking. Suppressing the finalizer
/// leaks the native objects instead; if the SIGSEGV disappears with this on,
/// the finalizer's release path is the trigger.
/// </summary>
[HarmonyPatch(typeof(HkHandle), "Finalize")]
[HarmonyPatchCategory("Init")]
// ReSharper disable once UnusedType.Global
static class HkHandleFinalizerPatch
{
    private static readonly bool Suppress =
        System.Environment.GetEnvironmentVariable("SE_HAVOK_NO_FINALIZE") == "1";

    private static bool _logged;

    // ReSharper disable once UnusedMember.Local
    private static bool Prefix()
    {
        if (!_logged && VRage.Utils.MyLog.Default != null)
        {
            _logged = true;
            VRage.Utils.MyLog.Default.WriteLine(
                $"[LinuxCompat] HkHandle finalizer patch active, suppress={Suppress}");
        }
        return !Suppress;
    }
}

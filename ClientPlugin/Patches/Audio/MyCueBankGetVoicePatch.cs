using System;
using System.Reflection;
using HarmonyLib;
using VRage.Audio;
using VRage.Data.Audio;

namespace ClientPlugin.Patches.Audio;

// Concurrent audio shutdown or wave disposal can invalidate GetVoice state.
// Treat NullReferenceException as an unavailable voice, matching its entry guard.
[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
internal static class MyCueBankGetVoicePatch
{
    private static MethodBase TargetMethod()
    {
        // `out int` is `ref int` in the CLR signature.
        return typeof(MyCueBank).GetMethod(
            "GetVoice",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            new[]
            {
                typeof(MyCueId),
                typeof(int).MakeByRefType(),
                typeof(MySoundDimensions),
                typeof(int),
                typeof(MyVoicePoolType),
            },
            null);
    }

    private static Exception Finalizer(Exception __exception, ref MySourceVoice __result)
    {
        if (__exception is NullReferenceException)
        {
            __result = null;
            return null;
        }
        return __exception;
    }
}

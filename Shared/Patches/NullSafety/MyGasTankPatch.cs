using System;
using HarmonyLib;
using Sandbox.Game.Entities.Blocks;

namespace ClientPlugin.Patches.NullSafety;

// Return zero when session initialization races power computation; the resource sink recomputes it.
[HarmonyPatch(typeof(MyGasTank), "ComputeRequiredPower")]
[HarmonyPatchCategory("Init")]
static class MyGasTankComputeRequiredPowerPatch
{
    static Exception Finalizer(Exception __exception, ref float __result)
    {
        if (__exception is NullReferenceException)
        {
            __result = 0f;
            return null;
        }

        return __exception;
    }
}

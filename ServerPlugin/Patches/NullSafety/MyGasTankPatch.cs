using System;
using HarmonyLib;
using Sandbox.Game.Entities.Blocks;

namespace ServerPlugin.Patches.NullSafety;

// Ports Topic 10.4 (commit 1d1786b7): NRE fix in MyGasTank.ComputeRequiredPower.
// The original method dereferences MySession.Static.Settings.EnableOxygen.
// Different .NET 10 / Linux timing during gas-tank initialization can lead
// to this method being called before MySession.Static is set, throwing an
// NRE on the simulation thread.
//
// Strategy: Finalizer that swallows NREs and returns 0 power. Returning 0
// during the narrow window where MySession.Static is null is harmless --
// the resource sink will recompute power once the session is available.
// The Finalizer has zero overhead when no exception is thrown.
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

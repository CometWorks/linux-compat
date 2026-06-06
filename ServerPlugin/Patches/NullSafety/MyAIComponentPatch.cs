using System;
using HarmonyLib;
using Sandbox.Game.AI;

namespace ServerPlugin.Patches.NullSafety;

[HarmonyPatch(typeof(MyAIComponent), "LoadData")]
[HarmonyPatchCategory("Init")]
static class MyAIComponentLoadDataPatch
{
    static Exception Finalizer(Exception __exception)
    {
        if (__exception is NullReferenceException)
            return null;

        return __exception;
    }
}

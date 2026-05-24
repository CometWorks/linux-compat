using System;
using HarmonyLib;
using Sandbox.Game.Screens.Helpers;
using VRage.Game;

namespace ServerPlugin.Patches.NullSafety;

[HarmonyPatch(typeof(MyToolbarItemCubeBlock), "Init")]
[HarmonyPatchCategory("Init")]
static class MyToolbarItemCubeBlockInitPatch
{
    static Exception Finalizer(Exception __exception, ref bool __result)
    {
        if (__exception is NullReferenceException)
        {
            __result = false;
            return null;
        }
        return __exception;
    }
}

using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.World;

namespace ClientPlugin.Patches.NullSafety;

// Prevent spawns until the session exists during Linux initialization.
[HarmonyPatch(typeof(MyFloatingObjects), "CanSpawn")]
[HarmonyPatchCategory("Init")]
static class MyFloatingObjectsCanSpawnPatch
{
    static bool Prefix(ref bool __result)
    {
        if (MySession.Static != null)
            return true;

        __result = false;
        return false;
    }
}

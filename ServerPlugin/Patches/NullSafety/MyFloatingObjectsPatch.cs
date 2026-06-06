using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.World;

namespace ServerPlugin.Patches.NullSafety;

// Ports Topic 10.2 (commit d95bf20b): NRE fix in MyFloatingObjects.CanSpawn
// during world load. The original method dereferences
// MySession.Static.SimplifiedSimulation, which NREs when CanSpawn is invoked
// before MySession.Static is assigned (an initialization-order issue that
// surfaces with different timing on Linux / .NET 10).
//
// Strategy: Prefix that returns false when MySession.Static is null. During
// the brief init window before the session is ready, "no spawn" is the
// correct conservative answer. Once Static is non-null, the prefix is a
// single null-compare and falls through to the original method.
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

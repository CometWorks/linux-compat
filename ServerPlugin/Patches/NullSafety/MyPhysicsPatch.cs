using HarmonyLib;
using Havok;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Utils;
using Sandbox.Game;
using Sandbox.Game.World;
using VRage;

namespace ServerPlugin.Patches.NullSafety;

// Ports Topic 10.1 (commit 6536f3cf): Fix NRE in MyPhysics.CreateHkWorld when
// MySession.Static or its Settings are not yet initialized. The original
// code dereferences MySession.Static.Settings twice (PhysicsIterations,
// WorldSizeKm). Different timing on Linux (.NET 10) can cause CreateHkWorld
// to be invoked before MySession.Static is assigned, throwing an NRE.
//
// Strategy: Prefix that lets the original method run when settings are
// available (zero overhead in the common case). Only when MySession.Static
// or its Settings are null does the prefix reimplement CreateHkWorld with
// safe defaults (PhysicsIterations = 8, WorldSizeKm = 0).
[HarmonyPatch(typeof(MyPhysics), nameof(MyPhysics.CreateHkWorld))]
[HarmonyPatchCategory("Init")]
static class MyPhysicsCreateHkWorldPatch
{
    static bool Prefix(float broadphaseSize, ref HkWorld __result)
    {
        // Run the original method when settings are available.
        if (MySession.Static?.Settings != null)
            return true;

        // MySession.Static (or its Settings) is null — reimplement the
        // method body using safe defaults that mirror commit 6536f3cf:
        //   physicsIterations = 8
        //   worldSizeKm       = 0  (skip the EntityLeftWorld handler hookup)
        var cInfo = MyPhysics.CreateWorldCInfo(
            MyPerGameSettings.EnableGlobalGravity,
            broadphaseSize,
            MyFakes.WHEEL_SOFTNESS ? float.MaxValue : MyPhysics.RestingVelocity,
            MyFakes.ENABLE_HAVOK_MULTITHREADING,
            8);

        var hkWorld = new HkWorld(ref cInfo);
        hkWorld.MarkForWrite();

        if (MyFakes.ENABLE_HAVOK_MULTITHREADING)
            hkWorld.InitMultithreading(MyPhysics.m_threadPool, MyPhysics.m_jobQueue);

        hkWorld.DeactivationRotationSqrdA /= 3f;
        hkWorld.DeactivationRotationSqrdB /= 3f;
        MyPhysics.InitCollisionFilters(hkWorld);

        __result = hkWorld;
        return false;
    }
}

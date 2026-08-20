using HarmonyLib;
using Havok;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Utils;
using Sandbox.Game;
using Sandbox.Game.World;
using VRage;

namespace ClientPlugin.Patches.NullSafety;

// CreateHkWorld can run before session settings exist on Linux.
[HarmonyPatch(typeof(MyPhysics), nameof(MyPhysics.CreateHkWorld))]
[HarmonyPatchCategory("Init")]
static class MyPhysicsCreateHkWorldPatch
{
    static bool Prefix(float broadphaseSize, ref HkWorld __result)
    {
        if (MySession.Static?.Settings != null)
            return true;

        // Use 8 physics iterations and omit the EntityLeftWorld hookup without settings.
        var cInfo = MyPhysics.CreateWorldCInfo(
            MyPerGameSettings.EnableGlobalGravity,
            broadphaseSize,
            MyFakes.WHEEL_SOFTNESS ? float.MaxValue : MyPhysics.RestingVelocity,
            MyFakes.ENABLE_HAVOK_MULTITHREADING,
            8
        );

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

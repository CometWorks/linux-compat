using System;
using HarmonyLib;
using Havok;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Utils;
using Sandbox.Game;
using Sandbox.Game.World;
using VRage;
using VRage.Utils;

namespace ClientPlugin.Patches.NullSafety;

// CreateHkWorld dereferences MySession.Static.Settings (PhysicsIterations,
// WorldSizeKm); init timing on Linux (.NET 10) can invoke it before the
// session exists, which would NRE (preventive port of dotnet-game-local
// commit 6536f3cf; never observed in a live run so far). The prefix falls
// through to the original whenever settings are available.
//
// The settings-less replacement cannot know PhysicsIterations or WorldSizeKm.
// Solver iterations are creation-time-only (HkWorld.CInfo), so vanilla's
// clamp minimum of 8 is the fallback. The EntityLeftWorld hookup, however,
// must not be dropped: both paths configure BROADPHASE_BORDER_REMOVE_ENTITY,
// so in a bounded world Havok silently removes bodies at the broad-phase
// border and without the handler SE keeps driving a body whose broad-phase
// handle is gone (stale-handle corruption). The decision vanilla makes at
// creation time (hook only when WorldSizeKm > 0) is deferred to fire time,
// when the session settings almost certainly exist.
[HarmonyPatch(typeof(MyPhysics), nameof(MyPhysics.CreateHkWorld))]
[HarmonyPatchCategory("Init")]
static class MyPhysicsCreateHkWorldPatch
{
    // Test hook: SE_LINUX_COMPAT_FORCE_HKWORLD_PREFIX=1 forces every world
    // through the replacement path so the deferred EntityLeftWorld handler can
    // be integration-tested against a live bounded world. Off in production.
    private static readonly bool ForcedOn =
        Environment.GetEnvironmentVariable("SE_LINUX_COMPAT_FORCE_HKWORLD_PREFIX") == "1";

    static bool Prefix(float broadphaseSize, ref HkWorld __result)
    {
        var settings = MySession.Static?.Settings;
        if (settings != null && !ForcedOn)
            return true;

        // broadphaseSize identifies the caller: the game's only call site is
        // MyPhysics.OnClusterCreated (cluster bbox size); 100000 is the
        // default argument, i.e. an external/reflection caller.
        MyLog.Default?.WriteLine(
            "[LinuxCompat] CreateHkWorld replacement path: "
                + $"broadphaseSize={broadphaseSize}, settings={(settings == null ? "null" : "present (forced)")}"
        );

        var cInfo = MyPhysics.CreateWorldCInfo(
            MyPerGameSettings.EnableGlobalGravity,
            broadphaseSize,
            MyFakes.WHEEL_SOFTNESS ? float.MaxValue : MyPhysics.RestingVelocity,
            MyFakes.ENABLE_HAVOK_MULTITHREADING,
            settings?.PhysicsIterations ?? 8
        );

        var hkWorld = new HkWorld(ref cInfo);
        hkWorld.MarkForWrite();

        hkWorld.EntityLeftWorld += DeferredEntityLeftWorld;

        if (MyFakes.ENABLE_HAVOK_MULTITHREADING)
            hkWorld.InitMultithreading(MyPhysics.m_threadPool, MyPhysics.m_jobQueue);

        hkWorld.DeactivationRotationSqrdA /= 3f;
        hkWorld.DeactivationRotationSqrdB /= 3f;
        MyPhysics.InitCollisionFilters(hkWorld);

        __result = hkWorld;
        return false;
    }

    private static void DeferredEntityLeftWorld(HkEntity hkEntity)
    {
        var settings = MySession.Static?.Settings;
        if (settings == null)
        {
            // Vanilla would have had no handler hooked either; the game's
            // handler needs the session, so no SE-side cleanup is possible.
            // Loud log: a body left the broad-phase pre-session, which is the
            // stale-handle scenario worth knowing about.
            MyLog.Default?.WriteLine(
                "[LinuxCompat] EntityLeftWorld fired before session settings exist; no SE-side cleanup possible"
            );
            return;
        }

        if (settings.WorldSizeKm > 0)
            MyPhysics.HavokWorld_EntityLeftWorld(hkEntity);
    }
}

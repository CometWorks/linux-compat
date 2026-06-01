using System;
using System.Collections.Generic;
using HarmonyLib;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Weapons;
using SpaceEngineers.Game.Entities.Weapons;

namespace ClientPlugin.Patches.NullSafety;

[HarmonyPatch(typeof(MyLargeGatlingTurret), "OnModelChange")]
[HarmonyPatchCategory("Init")]
static class MyLargeGatlingTurretOnModelChangePatch
{
    static Exception Finalizer(Exception __exception, MyLargeGatlingTurret __instance)
    {
        if (__exception is KeyNotFoundException)
        {
            __instance.m_base1 = null;
            __instance.m_base2 = null;
            return null;
        }
        return __exception;
    }
}

[HarmonyPatch(typeof(MyLaserAntenna), "OnModelChange")]
[HarmonyPatchCategory("Init")]
static class MyLaserAntennaOnModelChangePatch
{
    static Exception Finalizer(Exception __exception, MyLaserAntenna __instance)
    {
        if (__exception is KeyNotFoundException)
        {
            __instance.m_base1 = null;
            __instance.m_base2 = null;
            return null;
        }
        return __exception;
    }
}

// Ports Topic 10.5 (commit 49c137a1) for MyAngleGrinder. The grinder's
// UpdateAfterSimulation reads base.Subparts["grinder"] every tick. If the
// subpart wasn't loaded (e.g. because the model couldn't be resolved on
// Linux's case-sensitive filesystem before the path patches kicked in),
// this throws KeyNotFoundException on every tick the tool is held.
//
// Strategy: Finalizer that swallows the exception and lets the next tick
// retry. Zero overhead when the subpart is present.
[HarmonyPatch(typeof(MyAngleGrinder), nameof(MyAngleGrinder.UpdateAfterSimulation))]
[HarmonyPatchCategory("Init")]
static class MyAngleGrinderUpdateAfterSimulationPatch
{
    static Exception Finalizer(Exception __exception)
    {
        if (__exception is KeyNotFoundException || __exception is NullReferenceException)
            return null;

        return __exception;
    }
}

// Ports Topic 10.5 (commit 49c137a1) for MyHandDrill. Two unsafe spots:
//   * Init: m_spike = base.Subparts["Spike"] -> KeyNotFoundException if the
//     subpart isn't loaded. m_spike then stays null, m_spikeBasePos stays
//     default(Vector3), and the drill is non-functional but the game can
//     keep running.
//   * UpdateAfterSimulation: m_spike.PositionComp dereferences m_spike per
//     tick.
//
// Strategy: A Finalizer on each method swallows the relevant exception.
[HarmonyPatch(typeof(MyHandDrill), "Init", typeof(VRage.ObjectBuilders.MyObjectBuilder_EntityBase))]
[HarmonyPatchCategory("Init")]
static class MyHandDrillInitPatch
{
    static Exception Finalizer(Exception __exception)
    {
        if (__exception is KeyNotFoundException)
            return null;

        return __exception;
    }
}

[HarmonyPatch(typeof(MyHandDrill), nameof(MyHandDrill.UpdateAfterSimulation))]
[HarmonyPatchCategory("Init")]
static class MyHandDrillUpdateAfterSimulationPatch
{
    static Exception Finalizer(Exception __exception)
    {
        if (__exception is NullReferenceException)
            return null;

        return __exception;
    }
}

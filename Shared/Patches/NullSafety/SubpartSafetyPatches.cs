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
            var m_base1 = AccessTools.Field(typeof(MyLargeGatlingTurret).BaseType, "m_base1");
            var m_base2 = AccessTools.Field(typeof(MyLargeGatlingTurret).BaseType, "m_base2");
            m_base1.SetValue(__instance, null);
            m_base2.SetValue(__instance, null);
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
            var m_base1 = AccessTools.Field(typeof(MyLaserAntenna), "m_base1");
            var m_base2 = AccessTools.Field(typeof(MyLaserAntenna), "m_base2");
            m_base1.SetValue(__instance, null);
            m_base2.SetValue(__instance, null);
            return null;
        }
        return __exception;
    }
}

// Missing model subparts can throw during updates; retry on the next tick.
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

// A missing Spike subpart must not abort drill initialization or later updates.
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

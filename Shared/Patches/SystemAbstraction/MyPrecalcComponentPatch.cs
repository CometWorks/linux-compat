using System.Diagnostics;
using ClientPlugin.Rewriter;
using HarmonyLib;
using VRage.Voxels;

namespace ClientPlugin.Patches.SystemAbstraction;

// Each precalc worker batch stops after MaxPrecalcTime * 10000 Stopwatch ticks, which is
// MaxPrecalcTime milliseconds only at the 10 MHz Windows frequency. Linux reports 1 GHz, so
// the budget shrinks from 20 ms to 0.2 ms and a batch ends after one to three voxel jobs. The
// update thread starts at most one batch per worker per update, so meshing crawls along at
// about 25 jobs per update and the loading screen waits for the clipmaps much longer.
[HarmonyPatch(typeof(MyPrecalcComponent), nameof(MyPrecalcComponent.LoadData))]
[HarmonyPatchCategory("Finish")]
static class MyPrecalcComponentLoadDataPatch
{
    static bool scaled;

    static void Prefix()
    {
        if (scaled)
            return;
        scaled = true;
        MyPrecalcComponent.MaxPrecalcTime =
            MyPrecalcComponent.MaxPrecalcTime * Stopwatch.Frequency / WindowsStopwatch.Frequency;
    }
}

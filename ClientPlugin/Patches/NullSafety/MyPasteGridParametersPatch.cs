using System.Collections.Generic;
using HarmonyLib;
using Sandbox.Game.Entities;
using VRage.Game;
using VRageMath;

namespace ClientPlugin.Patches.NullSafety;

// A blueprint saved from a world with a partially built block carries a
// <ConstructionStockpile> with no Items children, which the game's XML reader
// deserializes to Items == null, discarding the field's own empty-array
// initializer. VRage's event serializer refuses that null when
// MyCubeGrid.TryPasteGrid_Implementation is raised in a hosted session
// (OnlineMode other than OFFLINE), so the paste fails:
//   Error serializing MyObjectBuilder_ConstructionStockpile.Items,
//   member contains null, but it's not allowed ...
// This is a vanilla game bug: it reproduces on the Windows binaries under
// Proton with this plugin absent. Every paste route funnels through this one
// constructor, so normalizing here fixes the UI clipboard, the grid storage
// helper, visual scripting and plugin-raised pastes alike. An empty Items
// array round-trips as zero items, identical to what a stockpile with no
// components means, so the wire format is unchanged.
[HarmonyPatch(
    typeof(MyCubeGrid.MyPasteGridParameters),
    MethodType.Constructor,
    new[]
    {
        typeof(List<MyObjectBuilder_CubeGrid>),
        typeof(bool),
        typeof(Vector3),
        typeof(bool),
        typeof(MyCubeGrid.RelativeOffset),
        typeof(List<ulong>),
    }
)]
[HarmonyPatchCategory("Init")]
static class MyPasteGridParametersCtorPatch
{
    static void Prefix(List<MyObjectBuilder_CubeGrid> entities)
    {
        if (entities == null)
            return;

        foreach (var grid in entities)
            NormalizeStockpiles(grid, depth: 0);
    }

    private static void NormalizeStockpiles(MyObjectBuilder_CubeGrid grid, int depth)
    {
        // Projected grids nest further projectors; bound the recursion anyway.
        if (grid?.CubeBlocks == null || depth > 8)
            return;

        foreach (var block in grid.CubeBlocks)
        {
            if (block == null)
                continue;

            var stockpile = block.ConstructionStockpile;
            if (stockpile != null && stockpile.Items == null)
                stockpile.Items = [];

            if (block is MyObjectBuilder_ProjectorBase projector)
            {
                NormalizeStockpiles(projector.ProjectedGrid, depth + 1);
                if (projector.ProjectedGrids != null)
                {
                    foreach (var projectedGrid in projector.ProjectedGrids)
                        NormalizeStockpiles(projectedGrid, depth + 1);
                }
            }
        }
    }
}

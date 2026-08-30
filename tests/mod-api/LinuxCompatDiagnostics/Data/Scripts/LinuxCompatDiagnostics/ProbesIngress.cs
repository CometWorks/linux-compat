using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace LinuxCompatDiagnostics
{
    /// <summary>
    /// Ingress probes: Windows-shaped paths written into shared mutable data
    /// (definitions, object builders) must be restored to native form by the
    /// engine-side funnel (PathCache.ResolveAbsolute) when the engine loads
    /// the referenced asset. All [LINUX].
    ///
    /// The mod ships Data/DiagBlocks.sbc defining two blocks whose Model/Icon
    /// references use backslashes and deliberately wrong casing:
    ///   CubeBlock/LinuxCompatDiagBlock        - SBC-referenced assets
    ///   CubeBlock/LinuxCompatDiagBlockMutable - definition mutated at runtime
    /// plus the model Models/LinuxCompatDiag/TestCube.mwm and icon texture
    /// Textures/LinuxCompatDiag/TestIcon.dds.
    /// </summary>
    public partial class LinuxCompatDiagnosticsSession
    {
        private const string DiagBlockSubtype = "LinuxCompatDiagBlock";
        private const string DiagBlockMutableSubtype = "LinuxCompatDiagBlockMutable";

        private void ProbeDefinitionPipeline()
        {
            Section("Definition pipeline (mod SBC with backslash + wrong-case assets)");
            // The definitions were parsed during preload; their existence
            // proves the mod's Data/*.sbc went through the loader.
            var def = GetDiagBlockDefinition(DiagBlockSubtype);
            CheckTrue(
                OwnerLinux,
                "LinuxCompatDiagBlock definition loaded",
                def != null,
                def == null ? "<null>" : def.Id.ToString()
            );
            if (def != null)
            {
                Info("DiagBlock def.Model (raw)", def.Model);
                Info(
                    "DiagBlock def.Icons[0] (raw)",
                    def.Icons != null && def.Icons.Length > 0 ? def.Icons[0] : "<none>"
                );
                CheckTrue(
                    OwnerLinux,
                    "DiagBlock def.Model ends with testcube.mwm",
                    EndsWithIgnoreCase(def.Model, "testcube.mwm"),
                    def.Model
                );
            }

            var defMutable = GetDiagBlockDefinition(DiagBlockMutableSubtype);
            CheckTrue(
                OwnerLinux,
                "LinuxCompatDiagBlockMutable definition loaded",
                defMutable != null,
                defMutable == null ? "<null>" : defMutable.Id.ToString()
            );
        }

        private static MyCubeBlockDefinition GetDiagBlockDefinition(string subtype)
        {
            try
            {
                var id = new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), subtype);
                MyCubeBlockDefinition def;
                MyDefinitionManager.Static.TryGetCubeBlockDefinition(id, out def);
                return def;
            }
            catch
            {
                return null;
            }
        }

        private void ProbeIngressSpawns()
        {
            Section("Ingress round-trip: entity spawns with mod asset paths");
            try
            {
                // Probe 1: spawn a block whose model comes straight from the
                // shipped SBC (backslash + wrong-case reference).
                SpawnAndVerify(
                    "SBC-referenced model",
                    DiagBlockSubtype,
                    new Vector3D(1000000, 1000000, 1000000)
                );

                // Probe 2: overwrite the second definition's Model with a
                // Windows-shaped absolute path built from the ModPath egress
                // value (wrong casing on the tail), then spawn. The engine
                // must restore the path through the sanctioned ingress funnel
                // when it loads the model.
                var def = GetDiagBlockDefinition(DiagBlockMutableSubtype);
                var ctx = ModContext;
                if (def == null || ctx == null || ctx.ModPath == null)
                {
                    CheckTrue(
                        OwnerLinux,
                        "runtime-mutated model preconditions",
                        false,
                        def == null ? "<no definition>" : "<no ModPath>"
                    );
                }
                else
                {
                    string winAbsolute = ctx.ModPath + "\\Models\\linuxcompatdiag\\TESTCUBE.mwm";
                    Info("runtime-mutated def.Model input", winAbsolute);
                    def.Model = winAbsolute;
                    SpawnAndVerify(
                        "runtime-mutated model",
                        DiagBlockMutableSubtype,
                        new Vector3D(1000000, 1000000, 1000100)
                    );

                    // Icon ingress: assignable without exception; the icon is
                    // only rendered by GUI so deeper verification is not
                    // possible headless.
                    CheckNoThrow(
                        OwnerLinux,
                        "runtime-mutated def.Icons assignment",
                        () =>
                        {
                            def.Icons = new string[]
                            {
                                ctx.ModPath + "\\Textures\\linuxcompatdiag\\TESTICON.dds",
                            };
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                CheckTrue(
                    OwnerLinux,
                    "ingress spawn probes completed",
                    false,
                    "<EXCEPTION: " + ex.GetType().Name + ": " + ex.Message + ">"
                );
            }
        }

        private void SpawnAndVerify(string label, string subtype, Vector3D position)
        {
            IMyEntity entity = null;
            try
            {
                var blockOb =
                    MyObjectBuilderSerializer.CreateNewObject(
                        new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), subtype)
                    ) as MyObjectBuilder_CubeBlock;
                if (blockOb == null)
                {
                    CheckTrue(OwnerLinux, label + ": block OB created", false, "<null>");
                    return;
                }
                blockOb.Min = new SerializableVector3I(0, 0, 0);

                var gridOb = new MyObjectBuilder_CubeGrid();
                gridOb.GridSizeEnum = MyCubeSize.Large;
                gridOb.IsStatic = true;
                gridOb.CreatePhysics = false;
                gridOb.PositionAndOrientation = new MyPositionAndOrientation(
                    position,
                    Vector3.Forward,
                    Vector3.Up
                );
                gridOb.PersistentFlags = MyPersistentEntityFlags2.InScene;
                gridOb.DisplayName = "LinuxCompatDiagnostics " + label;
                if (gridOb.CubeBlocks == null)
                    gridOb.CubeBlocks = new List<MyObjectBuilder_CubeBlock>();
                gridOb.CubeBlocks.Add(blockOb);

                entity = MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(gridOb);
                CheckTrue(
                    OwnerLinux,
                    label + ": grid spawned",
                    entity != null,
                    entity == null ? "<null>" : entity.EntityId.ToString()
                );
                if (entity == null)
                    return;

                var grid = entity as IMyCubeGrid;
                var slim = grid == null ? null : grid.GetCubeBlock(new Vector3I(0, 0, 0));
                var fat = slim == null ? null : slim.FatBlock;
                CheckTrue(
                    OwnerLinux,
                    label + ": fat block present",
                    fat != null,
                    fat == null ? "<null>" : fat.GetType().Name
                );
                if (fat == null)
                    return;

                var model = ((IMyEntity)fat).Model;
                CheckTrue(
                    OwnerLinux,
                    label + ": block model loaded",
                    model != null,
                    model == null ? "<null>" : ("vertices=" + model.GetVerticesCount())
                );
                if (model == null)
                    return;

                // AssetName egress is on the rewriter's FromGame list: the
                // mod must see a Windows-shaped path to the actual model.
                string assetName = model.AssetName;
                Info(label + ": model.AssetName", assetName);
                CheckTrue(
                    OwnerLinux,
                    label + ": AssetName ends with testcube.mwm",
                    EndsWithIgnoreCase(assetName, "testcube.mwm"),
                    assetName
                );
                CheckTrue(
                    OwnerLinux,
                    label + ": AssetName has no forward slashes",
                    assetName != null && assetName.IndexOf('/') < 0,
                    assetName
                );
                CheckTrue(
                    OwnerLinux,
                    label + ": model has geometry",
                    model.GetVerticesCount() > 0,
                    model.GetVerticesCount()
                );
            }
            finally
            {
                // Keep the diagnostics world clean even though the harness
                // never saves it.
                if (entity != null)
                {
                    try
                    {
                        entity.Close();
                    }
                    catch { }
                }
            }
        }
    }
}

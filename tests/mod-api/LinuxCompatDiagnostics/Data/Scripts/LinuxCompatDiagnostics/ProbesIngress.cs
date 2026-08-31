using System;
using System.Collections.Generic;
using System.IO;
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

        // ----- security: drive-prefixed ingress -----

        /// <summary>
        /// Drive-prefixed traversal. Every Windows-shaped path a mod holds comes
        /// from an egress member, so feeding one back with ".." appended is the
        /// realistic ingress attack: the boundary untranslates it to a native
        /// path, and the containment check must still reject it. Unmapped drive
        /// letters keep their Linux-rooted body (Wine maps Z: to /), which is
        /// exactly why the root check, not the translation, has to be the gate.
        /// The round-trip cases in the same section prove the legal direction
        /// still works: an egress path fed straight back must resolve.
        /// </summary>
        private void ProbeIngressTraversalRefused()
        {
            Section("security: drive-prefixed ingress paths");
            var utils = MyAPIGateway.Utilities;
            var gp = utils.GamePaths;
            string upBs = UpToRoot.Replace('/', '\\');

            if (gp == null || gp.ContentPath == null)
            {
                CheckTrue(
                    OwnerLinux,
                    "security: ingress preconditions (ContentPath)",
                    false,
                    "<null ContentPath>"
                );
            }
            else
            {
                // --- refused: ContentPath as the mod sees it, walked back out.
                CheckContentRefused(
                    "ingress egress-ContentPath + root traversal",
                    gp.ContentPath + "\\" + upBs + "etc\\passwd"
                );
                CheckContentRefused(
                    "ingress egress-ContentPath + sibling escape",
                    gp.ContentPath + "\\..\\Bin64\\SpaceEngineers.exe"
                );

                // --- allowed: the same egress value used the way mods use it.
                CheckContentAllowed(
                    "ingress egress-ContentPath round trip",
                    gp.ContentPath + "\\Data\\CubeBlocks\\CubeBlocks_Armor.sbc"
                );
                CheckContentAllowed(
                    "ingress egress-ContentPath round trip (inner traversal)",
                    gp.ContentPath + "\\Data\\..\\Data\\CubeBlocks\\CubeBlocks_Armor.sbc"
                );
            }

            // --- refused: synthetic drives a mod can invent. C:\ is mapped only
            // under the known prefixes; anything else falls back to a
            // Linux-rooted body, so these must be refused by the root check
            // rather than by the translation happening to fail.
            CheckContentRefused("ingress invented C: path", "C:\\etc\\passwd");
            CheckContentRefused("ingress invented Z: path", "Z:\\etc\\passwd");
            CheckContentRefused("ingress invented unmapped drive", "Q:\\etc\\passwd");
            CheckContentRefused(
                "ingress invented Windows system path",
                "C:\\Windows\\System32\\drivers\\etc\\hosts"
            );

            // Same shapes through the mod-location reader, whose root is the
            // mod's own folder.
            var me = OwnModItem();
            var ctx = ModContext;
            string modPath = ctx == null ? null : ctx.ModPath;
            if (modPath == null)
            {
                CheckTrue(
                    OwnerLinux,
                    "security: ingress preconditions (ModPath)",
                    false,
                    "<null ModPath>"
                );
            }
            else
            {
                CheckModRefused(
                    "ingress egress-ModPath + root traversal",
                    modPath + "\\" + upBs + "etc\\passwd",
                    me
                );
                // A sibling whose name does not extend this mod's own folder
                // name: refused by the prefix check itself on both platforms.
                // (A sibling that *does* extend it — "...DiagnosticsEvil" —
                // passes that check on Windows too; see S5 in the audit. It is
                // not asserted here because refusing it would diverge from
                // Windows.)
                CheckModRefused(
                    "ingress egress-ModPath + sibling mod escape",
                    modPath + "\\..\\OtherModFolder\\secret.txt",
                    me
                );
                CheckModAllowed(
                    "ingress egress-ModPath round trip",
                    modPath + "\\TestData\\CaseSensitivity\\expected.txt",
                    "lowercase-content",
                    me
                );
                CheckModAllowed(
                    "ingress egress-ModPath round trip (wrong case)",
                    modPath + "\\testdata\\casesensitivity\\EXPECTED.TXT",
                    "lowercase-content",
                    me
                );
            }

            CheckModRefused("ingress invented Z: path in mod location", "Z:\\etc\\passwd", me);

            // Storage takes bare filenames, so a drive-prefixed name is simply
            // an invalid filename there — refused by the character check.
            CheckStorageWriteRefused("ingress drive-prefixed storage name", "Z:\\escape.txt");
        }
    }
}

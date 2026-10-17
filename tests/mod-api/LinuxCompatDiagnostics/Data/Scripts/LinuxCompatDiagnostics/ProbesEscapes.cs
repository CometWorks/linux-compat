using System;
using System.IO;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.ObjectBuilders;

namespace LinuxCompatDiagnostics
{
    /// <summary>
    /// SE1-0291: the verified mod-API escapes split out of SE1-0263. Each probe reproduces one
    /// escape and asserts it is refused (A1-A4) or that the value a mod reads back is Windows-shaped
    /// (B4). B2 (programmable-script paths) stays open after this ticket, so its probe only reports
    /// a boolean, never a path, because the harness fails any log line that names the home folder.
    /// </summary>
    public partial class LinuxCompatDiagnosticsSession
    {
        private void ProbeVerifiedEscapes()
        {
            Section("Verified mod-API escapes (SE1-0291)");
            ProbeInventedModItemRefused();
            ProbeDoubleDriveRefused();
            ProbeContentDriveRefused();
            ProbeSerializerDriveRefused();
            ProbeAssignmentValueOracle();

            // B2 (programmable-block scripts) is not rewritten and still sees native paths. It
            // stays open after SE1-0291 and is not probed live here: a programmable block compiled
            // inside the suite would need types outside the mod whitelist, and a probe that fails to
            // compile takes the whole suite down. The leak and its reach are recorded in the
            // SE1-0263 note; a dedicated probe belongs with its fix.
            Info("B2 programmable-script path leak", "known open (not fixed by SE1-0291)");
        }

        /// <summary>
        /// A1: a <c>ModItem</c> the mod builds that names no loaded mod must be refused. Vanilla
        /// <c>GetPath()</c> is <c>Combine(ModsPath, Name)</c>, so a rooted or crafted Name would
        /// point anywhere; only the session's own loaded copy is trusted. The mod's real item still
        /// reads its own files.
        /// </summary>
        private void ProbeInventedModItemRefused()
        {
            var utils = MyAPIGateway.Utilities;

            // A rooted Name escapes vanilla Combine entirely: "/proc/" + "self/environ".
            var rootedName = new MyObjectBuilder_Checkpoint.ModItem("/proc/", 0, null);
            CheckModItemRefused("A1 rooted local mod name", "self/environ", rootedName);
            CheckModItemRefused(
                "A1 rooted local mod name, absolute",
                "/proc/self/environ",
                rootedName
            );

            // A plausible but unloaded workshop item.
            var unloaded = new MyObjectBuilder_Checkpoint.ModItem(
                "999999999.sbm",
                999999999,
                "Steam"
            );
            CheckModItemRefused("A1 unloaded workshop item", "Data/CubeBlocks.sbc", unloaded);

            // An unloaded local mod by name.
            var unloadedLocal = new MyObjectBuilder_Checkpoint.ModItem("NoSuchLocalMod", 0, null);
            CheckModItemRefused(
                "A1 unloaded local mod name",
                "TestData/expected.txt",
                unloadedLocal
            );

            // Allowed half: the mod's own loaded item still reads its own file.
            CheckProbe(
                OwnerLinux,
                "A1 own loaded mod item still reads",
                "lowercase-content",
                () => ReadFirstLineInModLocation("TestData/CaseSensitivity/expected.txt")
            );
        }

        /// <summary>
        /// A2: a double drive prefix (<c>C:C:\...</c>) survived one untranslate pass and the open
        /// mapped the remaining <c>C:\users\steamuser</c> into the home folder. The mod uses its own
        /// legitimate item, so only the crafted file argument matters.
        /// </summary>
        private void ProbeDoubleDriveRefused()
        {
            var me = OwnModItem();
            CheckModRefused(
                "A2 double-drive home escape",
                "C:C:\\users\\steamuser\\.ssh\\id_rsa",
                me
            );
            CheckModRefused("A2 double-drive install escape", "C:C:\\Program Files\\x", me);
            CheckModRefused("A2 drive after a dot segment", ".\\C:\\etc\\passwd", me);
        }

        /// <summary>
        /// A3: a drive segment hidden after a leading dot (<c>.\C:\proc\self\environ</c>) passed the
        /// Content containment check and the engine's resolver then stripped the Content root and
        /// opened the native target.
        /// </summary>
        private void ProbeContentDriveRefused()
        {
            CheckContentRefused("A3 drive after a dot segment", ".\\C:\\proc\\self\\environ");
            CheckContentRefused("A3 drive mid path", "Data\\C:\\proc\\self\\environ");
            CheckContentRefused("A3 double drive", "C:C:\\proc\\self\\environ");
        }

        /// <summary>
        /// A4: <c>MyObjectBuilderSerializer</c> checks its path with <c>PathUtils.EnsureSafePath</c>
        /// but the open went through the engine resolver, which translated a second time. A
        /// Content-prefixed drive escape must now be refused by the check, because it resolves the
        /// path the same way the open does.
        /// </summary>
        private void ProbeSerializerDriveRefused()
        {
            var gp = MyAPIGateway.Utilities.GamePaths;
            if (gp == null || gp.ContentPath == null)
            {
                CheckTrue(
                    OwnerLinux,
                    "A4 preconditions (ContentPath)",
                    false,
                    "<null ContentPath>"
                );
                return;
            }

            CheckSerializerRefused(
                "A4 content-prefixed drive escape",
                gp.ContentPath + "\\C:\\etc\\passwd"
            );
            CheckSerializerRefused(
                "A4 invented drive",
                "C:\\users\\steamuser\\linuxcompat-probe.sbc"
            );
            CheckSerializerRefused("A4 double drive", "C:C:\\users\\steamuser\\x.sbc");
        }

        /// <summary>
        /// B4: an assignment to a definition path member used as a value yields the stored native
        /// string. The mod must read back the Windows form instead, for a simple assignment used as
        /// a value and for a compound assignment, so it cannot be used as a native-path oracle.
        /// </summary>
        private void ProbeAssignmentValueOracle()
        {
            var def = GetDiagBlockDefinition(DiagBlockMutableSubtype);
            if (def == null || ModContext == null)
            {
                CheckTrue(OwnerLinux, "B4 mutable definition available", false, "<null>");
                return;
            }

            string[] icons = def.Icons;
            string model = def.Model;
            string cube = ModContext.ModPath + "\\Models\\LinuxCompatDiag\\TestCube.mwm";
            try
            {
                // Simple assignment used as a value.
                string viaSimple = (def.Model = cube.Replace('\\', '/'));
                CheckNoHomeFolder("B4 simple assignment value is Windows-shaped", viaSimple);
                CheckEquals(
                    OwnerLinux,
                    "B4 simple assignment value equals the Windows path",
                    cube,
                    viaSimple
                );

                // Compound assignment used as a value.
                def.Model = cube.Replace('\\', '/');
                string viaCompound = (def.Model += "");
                CheckNoHomeFolder("B4 compound assignment value is Windows-shaped", viaCompound);

                // Array element assignment used as a value.
                def.Icons = new string[] { cube.Replace('\\', '/') };
                string viaElement = (def.Icons[0] = cube.Replace('\\', '/'));
                CheckNoHomeFolder("B4 element assignment value is Windows-shaped", viaElement);
            }
            finally
            {
                def.Model = model;
                def.Icons = icons;
            }
        }

        private void CheckModItemRefused(
            string label,
            string file,
            MyObjectBuilder_Checkpoint.ModItem item
        )
        {
            var utils = MyAPIGateway.Utilities;
            string captured = file;
            CheckProbe(
                OwnerLinux,
                "security: refused, exists: " + label,
                false,
                () => utils.FileExistsInModLocation(captured, item)
            );
            CheckThrows(
                OwnerLinux,
                "security: refused, read: " + label,
                typeof(FileNotFoundException),
                () =>
                {
                    using (var r = utils.ReadFileInModLocation(captured, item))
                        r.ReadLine();
                }
            );
            CheckThrows(
                OwnerLinux,
                "security: refused, binary read: " + label,
                typeof(FileNotFoundException),
                () =>
                {
                    using (var r = utils.ReadBinaryFileInModLocation(captured, item))
                        r.ReadByte();
                }
            );
        }

        /// <summary>
        /// A serializer path the boundary must refuse. The type is not mod-whitelisted, so the
        /// exception is matched by name, like <see cref="CheckPathUtilsException"/>.
        /// </summary>
        private void CheckSerializerRefused(string label, string path)
        {
            string captured = path;
            Info("security input (" + label + ")", captured);
            CheckSerializerThrows(
                "security: refused, serialize: " + label,
                () =>
                {
                    MyObjectBuilderSerializer.SerializeXML(
                        captured,
                        false,
                        new MyObjectBuilder_WorldConfiguration()
                    );
                }
            );
            CheckSerializerThrows(
                "security: refused, deserialize: " + label,
                () =>
                {
                    MyObjectBuilder_WorldConfiguration world;
                    MyObjectBuilderSerializer.DeserializeXML(captured, out world);
                }
            );
        }

        private void CheckSerializerThrows(string name, Action probe)
        {
            try
            {
                probe();
            }
            catch (Exception ex)
            {
                Report(
                    OwnerLinux,
                    name,
                    ex.GetType().Name == "SecurityException",
                    "<throws SecurityException>",
                    "<EXCEPTION: " + ex.GetType().Name + ": " + ex.Message + ">"
                );
                return;
            }
            Report(OwnerLinux, name, false, "<throws SecurityException>", "<no exception>");
        }
    }
}

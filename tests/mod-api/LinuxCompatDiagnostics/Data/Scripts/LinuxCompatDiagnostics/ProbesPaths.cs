using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.ModAPI;
using VRage.Game;

namespace LinuxCompatDiagnostics
{
    /// <summary>
    /// Path-egress probes: System.IO.Path shims, Environment, GamePaths,
    /// Session paths, ModContext. All [LINUX]: mods must observe Windows
    /// semantics (backslash separators, synthetic drive letters).
    /// Shape checks (drive-rooted, suffix) are used instead of exact paths so
    /// the same expectations hold on real Windows and under the synthetic
    /// C:\ mapping.
    /// </summary>
    public partial class LinuxCompatDiagnosticsSession
    {
        private void ProbeEnvironment()
        {
            Section("Environment");
            // Only CurrentManagedThreadId, NewLine and ProcessorCount are
            // mod-whitelisted (MySpaceGameDefaultIlChecker.AllowMembers);
            // referencing any other Environment member fails the IL check at
            // mod compile time and disables the whole script, so their
            // runtime behavior cannot be probed from a mod at all.
            Info("Environment.OSVersion", "<not mod-whitelisted; unreferencable>");
            Info("Environment.CurrentDirectory", "<not mod-whitelisted; unreferencable>");
            Info("Environment.SystemDirectory", "<not mod-whitelisted; unreferencable>");
            Info("Environment.UserName", "<not mod-whitelisted; unreferencable>");
            // Environment.NewLine reads are redirected by the rewriter.
            CheckProbe(OwnerLinux, "Environment.NewLine", "\r\n", () => Environment.NewLine);
            TryInfo("Environment.ProcessorCount", () => Environment.ProcessorCount);
            TryInfo("Environment.CurrentManagedThreadId", () => Environment.CurrentManagedThreadId);
        }

        private void ProbePathStaticMembers()
        {
            Section("System.IO.Path static members (rewritten to WindowsPath)");
            CheckProbe(
                OwnerLinux,
                "Path.DirectorySeparatorChar",
                '\\',
                () => Path.DirectorySeparatorChar
            );
            CheckProbe(
                OwnerLinux,
                "Path.AltDirectorySeparatorChar",
                '/',
                () => Path.AltDirectorySeparatorChar
            );
            CheckProbe(OwnerLinux, "Path.VolumeSeparatorChar", ':', () => Path.VolumeSeparatorChar);
            CheckProbe(OwnerLinux, "Path.PathSeparator", ';', () => Path.PathSeparator);
            // .NET Framework on Windows: 36 invalid path chars, 41 invalid
            // filename chars (matches the reference capture under Proton).
            CheckProbe(
                OwnerLinux,
                "Path.GetInvalidPathChars().Length",
                36,
                () => Path.GetInvalidPathChars().Length
            );
            CheckProbe(
                OwnerLinux,
                "Path.GetInvalidFileNameChars().Length",
                41,
                () => Path.GetInvalidFileNameChars().Length
            );

            string temp = null;
            try
            {
                temp = Path.GetTempPath();
            }
            catch (Exception ex)
            {
                temp = "<EXCEPTION: " + ex.GetType().Name + ">";
            }
            Info("Path.GetTempPath()", temp);
            CheckTrue(
                OwnerLinux,
                "GetTempPath is Windows temp shape",
                temp != null
                    && IsDriveRooted(temp)
                    && temp.EndsWith("\\Temp\\")
                    && temp.IndexOf('/') < 0,
                temp
            );
        }

        private void ProbePathMethods()
        {
            Section("System.IO.Path method probes");

            // Relative path with backslashes: canonical mod-content shape.
            CheckProbe(
                OwnerLinux,
                "GetFileName(Data\\Scripts\\Foo.cs)",
                "Foo.cs",
                () => Path.GetFileName("Data\\Scripts\\Foo.cs")
            );
            CheckProbe(
                OwnerLinux,
                "GetDirectoryName(Data\\Scripts\\Foo.cs)",
                "Data\\Scripts",
                () => Path.GetDirectoryName("Data\\Scripts\\Foo.cs")
            );
            CheckProbe(
                OwnerLinux,
                "GetExtension(Data\\Scripts\\Foo.cs)",
                ".cs",
                () => Path.GetExtension("Data\\Scripts\\Foo.cs")
            );
            CheckProbe(
                OwnerLinux,
                "GetFileNameWithoutExtension",
                "Foo",
                () => Path.GetFileNameWithoutExtension("Data\\Scripts\\Foo.cs")
            );
            CheckProbe(
                OwnerLinux,
                "IsPathRooted(Data\\Scripts\\Foo.cs)",
                false,
                () => Path.IsPathRooted("Data\\Scripts\\Foo.cs")
            );
            CheckProbe(
                OwnerLinux,
                "GetPathRoot(Data\\Scripts\\Foo.cs)",
                "",
                () => Path.GetPathRoot("Data\\Scripts\\Foo.cs")
            );

            // Forward slashes must behave identically (Windows accepts both).
            CheckProbe(
                OwnerLinux,
                "GetDirectoryName(Data/Scripts/Foo.cs)",
                "Data\\Scripts",
                () => Path.GetDirectoryName("Data/Scripts/Foo.cs")
            );

            // Rooted without drive.
            CheckProbe(
                OwnerLinux,
                "IsPathRooted(\\Models\\Ammo\\Foo.mwm)",
                true,
                () => Path.IsPathRooted("\\Models\\Ammo\\Foo.mwm")
            );
            CheckProbe(
                OwnerLinux,
                "GetPathRoot(\\Models\\Ammo\\Foo.mwm)",
                "\\",
                () => Path.GetPathRoot("\\Models\\Ammo\\Foo.mwm")
            );

            // Drive-rooted input: GetFullPath must be the identity.
            CheckProbe(
                OwnerLinux,
                "GetFullPath(C:\\...\\test.sbm) identity",
                "C:\\Users\\X\\AppData\\Roaming\\SpaceEngineers\\Mods\\test.sbm",
                () =>
                    Path.GetFullPath(
                        "C:\\Users\\X\\AppData\\Roaming\\SpaceEngineers\\Mods\\test.sbm"
                    )
            );
            CheckProbe(
                OwnerLinux,
                "GetPathRoot(C:\\...) drive",
                "C:\\",
                () =>
                    Path.GetPathRoot(
                        "C:\\Users\\X\\AppData\\Roaming\\SpaceEngineers\\Mods\\test.sbm"
                    )
            );

            // Relative input: full path is drive-rooted with backslashes.
            string full = null;
            try
            {
                full = Path.GetFullPath("Data\\Scripts\\Foo.cs");
            }
            catch (Exception ex)
            {
                full = "<EXCEPTION: " + ex.GetType().Name + ">";
            }
            CheckTrue(
                OwnerLinux,
                "GetFullPath(relative) Windows-shaped",
                full != null
                    && IsDriveRooted(full)
                    && full.EndsWith("\\Data\\Scripts\\Foo.cs")
                    && full.IndexOf('/') < 0,
                full
            );

            // Linux-native input a mod should never see, but may construct.
            string fullNix = null;
            try
            {
                fullNix = Path.GetFullPath("/home/user/.config/SpaceEngineers/Mods/test.sbm");
            }
            catch (Exception ex)
            {
                fullNix = "<EXCEPTION: " + ex.GetType().Name + ">";
            }
            CheckTrue(
                OwnerLinux,
                "GetFullPath(/home/...) Windows-shaped",
                fullNix != null
                    && fullNix.Length >= 2
                    && fullNix[1] == ':'
                    && fullNix.EndsWith("\\Mods\\test.sbm")
                    && fullNix.IndexOf('/') < 0,
                fullNix
            );

            // Combine: backslash joins, absolute second arg semantics.
            CheckProbe(
                OwnerLinux,
                "Combine(Data, Scripts, Foo.cs)",
                "Data\\Scripts\\Foo.cs",
                () => Path.Combine("Data", "Scripts", "Foo.cs")
            );
            CheckProbe(
                OwnerLinux,
                "Combine(Data\\, Scripts)",
                "Data\\Scripts",
                () => Path.Combine("Data\\", "Scripts")
            );
            CheckProbe(
                OwnerLinux,
                "Combine(/abs/base, sub)",
                "/abs/base\\sub",
                () => Path.Combine("/abs/base", "sub")
            );
            CheckProbe(
                OwnerLinux,
                "Combine(C:\\base, sub)",
                "C:\\base\\sub",
                () => Path.Combine("C:\\base", "sub")
            );
            CheckProbe(
                OwnerLinux,
                "Combine(x, C:\\abs) second-absolute-wins",
                "C:\\abs",
                () => Path.Combine("x", "C:\\abs")
            );

            // Remaining WindowsPath shim surface.
            CheckProbe(
                OwnerLinux,
                "ChangeExtension(Data\\Foo.cs, .txt)",
                "Data\\Foo.txt",
                () => Path.ChangeExtension("Data\\Foo.cs", ".txt")
            );
            CheckProbe(
                OwnerLinux,
                "HasExtension(Data\\Foo.cs)",
                true,
                () => Path.HasExtension("Data\\Foo.cs")
            );
            CheckProbe(
                OwnerLinux,
                "HasExtension(Data\\Foo)",
                false,
                () => Path.HasExtension("Data\\Foo")
            );

            // GetTempFileName creates a real file and must report it under the
            // same synthetic temp root as GetTempPath.
            string tempFile = null;
            try
            {
                tempFile = Path.GetTempFileName();
            }
            catch (Exception ex)
            {
                tempFile = "<EXCEPTION: " + ex.GetType().Name + ": " + ex.Message + ">";
            }
            Info("Path.GetTempFileName()", tempFile);
            string tempRoot = null;
            try
            {
                tempRoot = Path.GetTempPath();
            }
            catch { }
            CheckTrue(
                OwnerLinux,
                "GetTempFileName lives under GetTempPath",
                tempFile != null
                    && tempRoot != null
                    && tempFile.StartsWith(tempRoot)
                    && tempFile.IndexOf('/') < 0,
                tempFile
            );
        }

        private void ProbeGamePaths()
        {
            Section("MyAPIGateway.Utilities.GamePaths");
            var gp = MyAPIGateway.Utilities.GamePaths;
            if (gp == null)
            {
                CheckTrue(OwnerLinux, "GamePaths available", false, "<null>");
                return;
            }

            string content = null,
                mods = null,
                user = null,
                saves = null;
            try
            {
                content = gp.ContentPath;
            }
            catch (Exception ex)
            {
                content = "<EXCEPTION: " + ex.GetType().Name + ">";
            }
            try
            {
                mods = gp.ModsPath;
            }
            catch (Exception ex)
            {
                mods = "<EXCEPTION: " + ex.GetType().Name + ">";
            }
            try
            {
                user = gp.UserDataPath;
            }
            catch (Exception ex)
            {
                user = "<EXCEPTION: " + ex.GetType().Name + ">";
            }
            try
            {
                saves = gp.SavesPath;
            }
            catch (Exception ex)
            {
                saves = "<EXCEPTION: " + ex.GetType().Name + ">";
            }

            CheckWindowsShapedAbsolute("GamePaths.ContentPath", content);
            CheckWindowsShapedAbsolute("GamePaths.ModsPath", mods);
            CheckWindowsShapedAbsolute("GamePaths.UserDataPath", user);
            CheckWindowsShapedAbsolute("GamePaths.SavesPath", saves);
            Info("GamePaths.ContentPath", content);
            Info("GamePaths.UserDataPath", user);

            CheckTrue(
                OwnerLinux,
                "ContentPath ends with \\Content",
                content != null && content.EndsWith("\\Content"),
                content
            );
            CheckTrue(
                OwnerLinux,
                "ModsPath = UserDataPath + \\Mods",
                user != null && mods == user + "\\Mods",
                mods
            );
            CheckTrue(
                OwnerLinux,
                "SavesPath starts with UserDataPath + \\Saves",
                user != null && saves != null && saves.StartsWith(user + "\\Saves"),
                saves
            );

            // ModScopeName resolves the calling mod assembly via a stack walk.
            // Local deployment: "LinuxCompatDiagnostics_LinuxCompatDiagnostics";
            // (fake-)Workshop deployment: "<id>.sbm_LinuxCompatDiagnostics".
            // Either way it must be the MOD scope, not the plugin or engine.
            string scope = null;
            try
            {
                scope = gp.ModScopeName;
            }
            catch (Exception ex)
            {
                scope = "<EXCEPTION: " + ex.GetType().Name + ">";
            }
            CheckTrue(
                OwnerLinux,
                "GamePaths.ModScopeName is the mod scope",
                scope != null && scope.EndsWith("_LinuxCompatDiagnostics"),
                scope
            );

            TryInfo("Utilities.IsDedicated", () => MyAPIGateway.Utilities.IsDedicated);
        }

        private void ProbeConfigDedicated()
        {
            Section("MyAPIGateway.Utilities.ConfigDedicated");
            var cd = MyAPIGateway.Utilities.ConfigDedicated;
            if (cd == null)
            {
                Info("ConfigDedicated", "<null (expected on non-dedicated)>");
                return;
            }

            string filePath = null;
            try
            {
                filePath = cd.GetFilePath();
            }
            catch (Exception ex)
            {
                filePath = "<EXCEPTION: " + ex.GetType().Name + ">";
            }
            CheckWindowsShapedAbsolute("ConfigDedicated.GetFilePath()", filePath);
            Info("ConfigDedicated.GetFilePath()", filePath);

            TryInfo("ConfigDedicated.PremadeCheckpointPath", () => cd.PremadeCheckpointPath);

            // Setter round-trip: a Windows-shaped write must read back
            // Windows-shaped (the engine stores it native in between).
            string original = null;
            try
            {
                original = cd.PremadeCheckpointPath;
            }
            catch { }
            try
            {
                cd.PremadeCheckpointPath = "C:\\Probe\\PremadeCheckpoint";
                CheckProbe(
                    OwnerLinux,
                    "PremadeCheckpointPath set/get round-trip",
                    "C:\\Probe\\PremadeCheckpoint",
                    () => cd.PremadeCheckpointPath
                );
            }
            catch (Exception ex)
            {
                CheckTrue(
                    OwnerLinux,
                    "PremadeCheckpointPath set/get round-trip",
                    false,
                    "<EXCEPTION: " + ex.GetType().Name + ": " + ex.Message + ">"
                );
            }
            finally
            {
                try
                {
                    cd.PremadeCheckpointPath = original;
                }
                catch { }
            }
        }

        private void ProbeSessionPaths()
        {
            Section("MyAPIGateway.Session path members");
            var s = MyAPIGateway.Session;
            if (s == null)
            {
                CheckTrue(OwnerLinux, "Session available", false, "<null>");
                return;
            }

            string current = null,
                thumb = null;
            try
            {
                current = s.CurrentPath;
            }
            catch (Exception ex)
            {
                current = "<EXCEPTION: " + ex.GetType().Name + ">";
            }
            try
            {
                thumb = s.ThumbPath;
            }
            catch (Exception ex)
            {
                thumb = "<EXCEPTION: " + ex.GetType().Name + ">";
            }

            CheckWindowsShapedAbsolute("Session.CurrentPath", current);
            Info("Session.CurrentPath", current);
            CheckTrue(
                OwnerLinux,
                "CurrentPath contains \\Saves\\",
                current != null && current.IndexOf("\\Saves\\", StringComparison.Ordinal) >= 0,
                current
            );
            CheckTrue(
                OwnerLinux,
                "ThumbPath = CurrentPath + \\thumb.jpg",
                current != null && thumb == current + "\\thumb.jpg",
                thumb
            );
            TryInfo("Session.Name", () => s.Name);
            TryInfo("Session.IsServer", () => s.IsServer);
        }

        private void ProbeOwnModContext()
        {
            Section("Own ModContext");
            var ctx = ModContext;
            if (ctx == null)
            {
                CheckTrue(OwnerLinux, "ModContext available", false, "<null>");
                return;
            }

            // ModName comes from the deployment metadata: reliable for local
            // mods; the fake-Workshop registration used on the DS has no
            // Workshop title source, so record it there instead of asserting.
            bool localMod = false;
            try
            {
                localMod = ctx.ModItem.PublishedFileId == 0;
            }
            catch { }
            if (localMod)
                CheckProbe(
                    OwnerLinux,
                    "ModContext.ModName",
                    "LinuxCompatDiagnostics",
                    () => ctx.ModName
                );
            else
                TryInfo("ModContext.ModName (workshop deployment)", () => ctx.ModName);
            TryInfo("ModContext.ModId", () => ctx.ModId);
            TryInfo("ModContext.ModServiceName", () => ctx.ModServiceName);
            TryInfo("ModContext.IsBaseGame", () => ctx.IsBaseGame);

            string modPath = null,
                modPathData = null,
                itemPath = null;
            try
            {
                modPath = ctx.ModPath;
            }
            catch (Exception ex)
            {
                modPath = "<EXCEPTION: " + ex.GetType().Name + ">";
            }
            try
            {
                modPathData = ctx.ModPathData;
            }
            catch (Exception ex)
            {
                modPathData = "<EXCEPTION: " + ex.GetType().Name + ">";
            }
            try
            {
                itemPath = ctx.ModItem.GetPath();
            }
            catch (Exception ex)
            {
                itemPath = "<EXCEPTION: " + ex.GetType().Name + ">";
            }

            CheckWindowsShapedAbsolute("ModContext.ModPath", modPath);
            Info("ModContext.ModPath", modPath);
            // A locally-deployed mod folder is named after the mod; a
            // (fake-)Workshop deployment used for DS runs is named after the
            // numeric published id, so only check the suffix for local mods.
            bool isLocal = false;
            try
            {
                isLocal = ctx.ModItem.PublishedFileId == 0;
            }
            catch { }
            if (isLocal)
                CheckTrue(
                    OwnerLinux,
                    "ModPath ends with \\LinuxCompatDiagnostics",
                    modPath != null && modPath.EndsWith("\\LinuxCompatDiagnostics"),
                    modPath
                );
            CheckTrue(
                OwnerLinux,
                "ModPathData = ModPath + \\Data",
                modPath != null && modPathData == modPath + "\\Data",
                modPathData
            );
            CheckTrue(
                OwnerLinux,
                "ModItem.GetPath() = ModPath",
                modPath != null && itemPath == modPath,
                itemPath
            );
        }

        private void ProbeLoadedMods()
        {
            Section("MyAPIGateway.Session.Mods");
            List<MyObjectBuilder_Checkpoint.ModItem> mods = null;
            try
            {
                mods = MyAPIGateway.Session.Mods;
            }
            catch (Exception ex)
            {
                CheckTrue(
                    OwnerLinux,
                    "Session.Mods enumerable",
                    false,
                    "<EXCEPTION: " + ex.GetType().Name + ">"
                );
                return;
            }
            CheckTrue(
                OwnerLinux,
                "Session.Mods enumerable",
                mods != null,
                mods == null ? "<null>" : ("count=" + mods.Count)
            );
            if (mods == null)
                return;

            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                string path = null;
                try
                {
                    path = m.GetPath();
                }
                catch (Exception ex)
                {
                    path = "<EXCEPTION: " + ex.GetType().Name + ">";
                }
                Info("mod[" + i + "] " + m.Name + " GetPath()", path);
                CheckWindowsShapedAbsolute("mod[" + i + "].GetPath()", path);
                try
                {
                    var mctx = m.GetModContext();
                    if (mctx != null)
                        CheckWindowsShapedAbsolute("mod[" + i + "].Context.ModPath", mctx.ModPath);
                    else
                        Info("mod[" + i + "].Context", "<null>");
                }
                catch (Exception ex)
                {
                    Info(
                        "mod[" + i + "].Context",
                        "<EXCEPTION: " + ex.GetType().Name + ": " + ex.Message + ">"
                    );
                }
            }
        }

        // ----- security: path containment -----
        //
        // Every probe below is named "security: ...". parse_results.py keeps a
        // manifest of these names and fails the run if one stops being
        // reported, so a probe cannot silently disappear along with the check
        // it guards. Refusal cases and the matching legal-access cases live
        // side by side on purpose: a fix that over-tightens containment breaks
        // the "allowed" half, and one that loosens it breaks the "refused"
        // half.

        // Deep enough to reach "/" from any install location; GetFullPath
        // clamps at the filesystem root, so the exact depth does not matter.
        private const string UpToRoot = "../../../../../../../../../../../../../../../../";

        private const string ArmorSbc = "Data/CubeBlocks/CubeBlocks_Armor.sbc";

        /// <summary>
        /// Containment of the game-content readers. The engine guards them with
        /// Path.GetFullPath(Path.Combine(ContentPath, file)).StartsWith(ContentPath),
        /// so every refusal below also holds on Windows. On Linux the guard is
        /// easy to lose: the casing resolver returns its input unchanged as soon
        /// as the path exists, and the kernel resolves ".." itself, so an
        /// uncanonicalized "&lt;ContentPath&gt;/../../etc/passwd" would both open and
        /// pass a raw StartsWith test.
        /// </summary>
        private void ProbeContentTraversal()
        {
            Section("security: game content readers stay under ContentPath");

            // --- refused: escapes to the filesystem root. On Windows the same
            // strings land on C:\etc\passwd, which does not exist, so the
            // assertion holds there for the same reason it is written this way.
            CheckContentRefused("content root traversal", UpToRoot + "etc/passwd");
            CheckContentRefused(
                "content root traversal (backslash)",
                UpToRoot.Replace('/', '\\') + "etc\\passwd"
            );
            CheckContentRefused(
                "content traversal after valid segments",
                "Data/CubeBlocks/" + UpToRoot + "etc/passwd"
            );
            CheckContentRefused("content traversal to /proc", UpToRoot + "proc/self/environ");

            // --- refused: sibling of Content inside the install. The target
            // exists on both platforms, so this case fails loudly if
            // containment regresses instead of passing on a missing file.
            CheckContentRefused("content sibling escape", "../Bin64/SpaceEngineers.exe");
            CheckContentRefused(
                "content sibling escape (backslash)",
                "..\\Bin64\\SpaceEngineers.exe"
            );

            // --- refused: bare traversal segments, encodings that must not be
            // decoded, and rooted prefixes that drop ContentPath in Combine.
            CheckContentRefused("content bare ..", "..");
            CheckContentRefused("content percent-encoded traversal", "%2e%2e/%2e%2e/etc/passwd");
            CheckContentRefused("content extended-length prefix", "\\\\?\\C:\\etc\\passwd");
            CheckContentRefused("content UNC share", "\\\\server\\share\\secret.txt");
            CheckContentRefused("content absolute native path", "/etc/passwd");

            // --- allowed: the legal-access half. These are the shapes real
            // mods use; an over-tightened containment check breaks them.
            CheckContentAllowed("content relative path", ArmorSbc);
            CheckContentAllowed("content backslash path", "Data\\CubeBlocks\\CubeBlocks_Armor.sbc");
            CheckContentAllowed("content lowercased path", "data/cubeblocks/cubeblocks_armor.sbc");
            // ".." that stays inside the root must still resolve: canonicalizing
            // the containment check must not become "reject any dots".
            CheckContentAllowed("content inner traversal", "Data/../" + ArmorSbc);
            CheckContentAllowed("content leading dot segment", "./" + ArmorSbc);
        }

        /// <summary>
        /// A path the game-content API must refuse: FileExists is false and both
        /// readers throw FileNotFoundException, which is the Windows shape.
        /// </summary>
        private void CheckContentRefused(string label, string path)
        {
            var utils = MyAPIGateway.Utilities;
            string captured = path;
            Info("security input (" + label + ")", captured);
            CheckProbe(
                OwnerLinux,
                "security: refused, exists: " + label,
                false,
                () => utils.FileExistsInGameContent(captured)
            );
            CheckThrows(
                OwnerLinux,
                "security: refused, read: " + label,
                typeof(FileNotFoundException),
                () =>
                {
                    using (var r = utils.ReadFileInGameContent(captured))
                        r.ReadLine();
                }
            );
            CheckThrows(
                OwnerLinux,
                "security: refused, binary read: " + label,
                typeof(FileNotFoundException),
                () =>
                {
                    using (var r = utils.ReadBinaryFileInGameContent(captured))
                        r.ReadByte();
                }
            );
        }

        /// <summary>
        /// A path the game-content API must keep serving: exists is true and the
        /// readers return the file's real first bytes (BOM + "&lt;?xml").
        /// </summary>
        private void CheckContentAllowed(string label, string path)
        {
            var utils = MyAPIGateway.Utilities;
            string captured = path;
            CheckProbe(
                OwnerLinux,
                "security: allowed, exists: " + label,
                true,
                () => utils.FileExistsInGameContent(captured)
            );
            CheckProbe(
                OwnerLinux,
                "security: allowed, read: " + label,
                "EF BB BF 3C 3F 78 6D 6C",
                () =>
                {
                    using (var r = utils.ReadBinaryFileInGameContent(captured))
                        return r == null ? null : HexBytes(r.ReadBytes(8));
                }
            );
        }

        /// <summary>
        /// Containment of the mod-location readers. Their root is the mod's own
        /// folder and Data/Scripts inside it is additionally protected, so a mod
        /// cannot read its own (or another mod's) source.
        /// </summary>
        private void ProbeModLocationTraversal()
        {
            Section("security: mod location readers stay under the mod folder");
            var me = OwnModItem();

            // --- refused
            CheckModRefused("mod root traversal", UpToRoot + "etc/passwd", me);
            CheckModRefused(
                "mod root traversal (backslash)",
                UpToRoot.Replace('/', '\\') + "etc\\passwd",
                me
            );
            CheckModRefused(
                "mod traversal after valid segments",
                "TestData/" + UpToRoot + "etc/passwd",
                me
            );
            CheckModRefused("mod absolute native path", "/etc/passwd", me);
            CheckModRefused("mod bare ..", "..", me);
            CheckModRefused("mod UNC share", "\\\\server\\share\\secret.txt", me);
            // Data/Scripts is protected by the engine even though it is inside
            // the mod folder: a mod must not be able to read script source.
            CheckModRefused(
                "mod protected Data/Scripts",
                "Data/Scripts/LinuxCompatDiagnostics/LinuxCompatDiagnostics.cs",
                me
            );
            CheckModRefused(
                "mod protected Data/Scripts (backslash)",
                "Data\\Scripts\\LinuxCompatDiagnostics\\ProbesPaths.cs",
                me
            );

            // --- allowed
            CheckModAllowed(
                "mod relative path",
                "TestData/CaseSensitivity/expected.txt",
                "lowercase-content",
                me
            );
            CheckModAllowed(
                "mod backslash path",
                "TestData\\CaseSensitivity\\expected.txt",
                "lowercase-content",
                me
            );
            CheckModAllowed(
                "mod uppercased path",
                "TESTDATA/CASESENSITIVITY/EXPECTED.TXT",
                "lowercase-content",
                me
            );
            CheckModAllowed(
                "mod inner traversal",
                "TestData/Subdir/../CaseSensitivity/expected.txt",
                "lowercase-content",
                me
            );
            CheckModAllowed(
                "mod nested subdir",
                "TestData/Subdir/nested.txt",
                "nested-content",
                me
            );
        }

        private void CheckModRefused(
            string label,
            string path,
            MyObjectBuilder_Checkpoint.ModItem me
        )
        {
            var utils = MyAPIGateway.Utilities;
            string captured = path;
            Info("security input (" + label + ")", captured);
            CheckProbe(
                OwnerLinux,
                "security: refused, exists: " + label,
                false,
                () => utils.FileExistsInModLocation(captured, me)
            );
            CheckThrows(
                OwnerLinux,
                "security: refused, read: " + label,
                typeof(FileNotFoundException),
                () =>
                {
                    using (var r = utils.ReadFileInModLocation(captured, me))
                        r.ReadLine();
                }
            );
            CheckThrows(
                OwnerLinux,
                "security: refused, binary read: " + label,
                typeof(FileNotFoundException),
                () =>
                {
                    using (var r = utils.ReadBinaryFileInModLocation(captured, me))
                        r.ReadByte();
                }
            );
        }

        private void CheckModAllowed(
            string label,
            string path,
            string expectedFirstLine,
            MyObjectBuilder_Checkpoint.ModItem me
        )
        {
            var utils = MyAPIGateway.Utilities;
            string captured = path;
            CheckProbe(
                OwnerLinux,
                "security: allowed, exists: " + label,
                true,
                () => utils.FileExistsInModLocation(captured, me)
            );
            CheckProbe(
                OwnerLinux,
                "security: allowed, read: " + label,
                expectedFirstLine,
                () => ReadFirstLineInModLocation(captured)
            );
        }

        /// <summary>
        /// Containment of the storage APIs — the one place the game really does
        /// intend a sandbox. Separators are rejected by the engine's fixed
        /// invalid-character list, so the only traversal shapes left are bare
        /// dot segments, which its GetFullPath prefix check stops.
        /// </summary>
        private void ProbeStorageTraversal()
        {
            Section("security: storage filenames stay in the scope folder");
            var utils = MyAPIGateway.Utilities;
            var owner = typeof(LinuxCompatDiagnosticsSession);

            // --- refused
            CheckStorageWriteRefused("storage bare .. (local)", "..");
            CheckStorageWriteRefused("storage ..\\escape.txt (local)", "..\\escape.txt");
            CheckStorageWriteRefused("storage ../escape.txt (local)", "../escape.txt");
            CheckStorageWriteRefused("storage sub/dir/f.txt (local)", "sub/dir/f.txt");
            CheckStorageWriteRefused("storage absolute path (local)", "/tmp/escape.txt");
            CheckStorageWriteRefused("storage drive-prefixed path (local)", "C:\\escape.txt");

            CheckThrows(
                OwnerLinux,
                "security: refused, write: storage ..\\escape.txt (world)",
                typeof(FileNotFoundException),
                () =>
                {
                    using (var w = utils.WriteFileInWorldStorage("..\\escape.txt", owner))
                        w.Write("x");
                }
            );
            CheckThrows(
                OwnerLinux,
                "security: refused, write: storage ../escape.txt (global)",
                typeof(FileNotFoundException),
                () =>
                {
                    using (var w = utils.WriteFileInGlobalStorage("../escape.txt"))
                        w.Write("x");
                }
            );
            // Nothing may have landed next to the scope folder.
            CheckProbe(
                OwnerLinux,
                "security: refused, aftermath: escape.txt absent from world storage",
                false,
                () => utils.FileExistsInWorldStorage("escape.txt", owner)
            );
            CheckProbe(
                OwnerLinux,
                "security: refused, aftermath: escape.txt absent from local storage",
                false,
                () => utils.FileExistsInLocalStorage("escape.txt", owner)
            );

            // --- allowed: a legal round trip in each of the three scopes.
            CheckStorageRoundTrip("storage local scope", 0);
            CheckStorageRoundTrip("storage world scope", 1);
            CheckStorageRoundTrip("storage global scope", 2);

            // "..." is a legal Linux filename but is normalized away on
            // Windows, so there is no shared expectation to assert.
            TryInfo(
                "security: storage ... (no Windows reference)",
                () =>
                {
                    using (var w = utils.WriteFileInLocalStorage("...", owner))
                        w.Write("x");
                    return "<created>";
                }
            );
        }

        private void CheckStorageWriteRefused(string label, string name)
        {
            var utils = MyAPIGateway.Utilities;
            var owner = typeof(LinuxCompatDiagnosticsSession);
            string captured = name;
            Info("security input (" + label + ")", captured);
            CheckThrows(
                OwnerLinux,
                "security: refused, write: " + label,
                typeof(FileNotFoundException),
                () =>
                {
                    using (var w = utils.WriteFileInLocalStorage(captured, owner))
                        w.Write("x");
                }
            );
            CheckProbe(
                OwnerLinux,
                "security: refused, exists: " + label,
                false,
                () => utils.FileExistsInLocalStorage(captured, owner)
            );
            CheckNoThrow(
                OwnerLinux,
                "security: refused, delete is a no-op: " + label,
                () => utils.DeleteFileInLocalStorage(captured, owner)
            );
        }

        /// <summary>
        /// Write, read back, and delete one file in the given storage scope
        /// (0 local, 1 world, 2 global) — the access every mod is entitled to.
        /// </summary>
        private void CheckStorageRoundTrip(string label, int scope)
        {
            var utils = MyAPIGateway.Utilities;
            var owner = typeof(LinuxCompatDiagnosticsSession);
            const string name = "SecurityRoundTrip.txt";
            const string payload = "round-trip-ok";

            CheckNoThrow(
                OwnerLinux,
                "security: allowed, write: " + label,
                () =>
                {
                    TextWriter w =
                        scope == 0 ? utils.WriteFileInLocalStorage(name, owner)
                        : scope == 1 ? utils.WriteFileInWorldStorage(name, owner)
                        : utils.WriteFileInGlobalStorage(name);
                    using (w)
                        w.Write(payload);
                }
            );
            CheckProbe(
                OwnerLinux,
                "security: allowed, exists: " + label,
                true,
                () =>
                    scope == 0 ? utils.FileExistsInLocalStorage(name, owner)
                    : scope == 1 ? utils.FileExistsInWorldStorage(name, owner)
                    : utils.FileExistsInGlobalStorage(name)
            );
            CheckProbe(
                OwnerLinux,
                "security: allowed, read: " + label,
                payload,
                () =>
                {
                    TextReader r =
                        scope == 0 ? utils.ReadFileInLocalStorage(name, owner)
                        : scope == 1 ? utils.ReadFileInWorldStorage(name, owner)
                        : utils.ReadFileInGlobalStorage(name);
                    using (r)
                        return r.ReadToEnd();
                }
            );
            CheckNoThrow(
                OwnerLinux,
                "security: allowed, delete: " + label,
                () =>
                {
                    if (scope == 0)
                        utils.DeleteFileInLocalStorage(name, owner);
                    else if (scope == 1)
                        utils.DeleteFileInWorldStorage(name, owner);
                    else
                        utils.DeleteFileInGlobalStorage(name);
                }
            );
        }
    }
}

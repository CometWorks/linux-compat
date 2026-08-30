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
    }
}

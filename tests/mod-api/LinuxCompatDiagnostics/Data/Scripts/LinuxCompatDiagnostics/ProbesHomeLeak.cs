using System;
using System.IO;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Library.Utils;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace LinuxCompatDiagnostics
{
    /// <summary>
    /// SE1-0210: no path a mod sees may name the Linux home folder. On Windows
    /// a mod sees C:\users\&lt;user&gt;\..., so on Linux it must see
    /// C:\users\steamuser\... in every call shape, and the paths it builds from
    /// them must lead back to the same files.
    /// </summary>
    public partial class LinuxCompatDiagnosticsSession
    {
        // Initialized before LoadData runs, from a static field initializer.
        private static readonly string StaticUserDataPath = UserDataPathOf(
            MyAPIGateway.Utilities
        );

        private static string UserDataPathOf(IMyUtilities utilities)
        {
            return utilities == null ? null : utilities.GamePaths.UserDataPath;
        }

        private static string UserDataPathOf(IMyGamePaths paths)
        {
            return paths.UserDataPath;
        }

        // The NormalizePath helpers mods write for themselves.
        private static string NormalizeFull(string path)
        {
            return Path.GetFullPath(path).TrimEnd('\\');
        }

        private static string NormalizeSeparators(string path)
        {
            return path.Replace('/', '\\').TrimEnd('\\');
        }

        private void ProbeHomeFolderLeak()
        {
            Section("Home folder leak (SE1-0210)");

            // The reported call shape, verbatim.
            string savesRoot = NormalizeFull(
                Path.Combine(MyAPIGateway.Utilities.GamePaths.UserDataPath, "Saves")
            );
            CheckNoHomeFolder("savesRoot (GetFullPath)", savesRoot);
            CheckNoHomeFolder(
                "savesRoot (separators)",
                NormalizeSeparators(
                    Path.Combine(MyAPIGateway.Utilities.GamePaths.UserDataPath, "Saves")
                )
            );

            var utilities = MyAPIGateway.Utilities;
            var gamePaths = utilities.GamePaths;
            Func<string> lambda = () => gamePaths.UserDataPath;
            CheckNoHomeFolder("UserDataPath via local IMyUtilities", utilities.GamePaths.UserDataPath);
            CheckNoHomeFolder("UserDataPath via lambda", lambda());
            CheckNoHomeFolder("UserDataPath via helper(IMyUtilities)", UserDataPathOf(utilities));
            CheckNoHomeFolder("UserDataPath via helper(IMyGamePaths)", UserDataPathOf(gamePaths));
            CheckNoHomeFolder(
                "UserDataPath via ?.",
                MyAPIGateway.Utilities?.GamePaths?.UserDataPath
            );
            CheckNoHomeFolder(
                "UserDataPath in interpolation",
                $"{MyAPIGateway.Utilities.GamePaths.UserDataPath}\\Saves"
            );
            CheckNoHomeFolder("UserDataPath in static initializer", StaticUserDataPath);
            CheckNoHomeFolder(
                "UserDataPath as the receiver of ?.",
                gamePaths.UserDataPath?.TrimEnd('\\')
            );
            CheckNoHomeFolder(
                "UserDataPath mid-chain after ?.",
                MyAPIGateway.Utilities?.GamePaths.UserDataPath.TrimEnd('\\')
            );
            CheckNoHomeFolder(
                "CurrentPath mid-chain after ?.",
                MyAPIGateway.Session?.CurrentPath.Replace('/', '\\')
            );
            CheckNoHomeFolder("MyLog.Default.GetFilePath()", MyLog.Default.GetFilePath());
            CheckNoHomeFolder("SavesPath", gamePaths.SavesPath);
            CheckNoHomeFolder("ModsPath", gamePaths.ModsPath);
            CheckNoHomeFolder("ContentPath", gamePaths.ContentPath);
            CheckNoHomeFolder("Session.CurrentPath", MyAPIGateway.Session.CurrentPath);
            CheckNoHomeFolder("ModContext.ModPath", ModContext.ModPath);
            CheckNoHomeFolder("Path.GetTempPath()", Path.GetTempPath());
            CheckNoHomeFolder("Path.GetFullPath(\"x\")", Path.GetFullPath("x"));

            CheckTrue(
                OwnerLinux,
                "savesRoot = UserDataPath + \\Saves",
                savesRoot == gamePaths.UserDataPath + "\\Saves",
                savesRoot
            );

            // List the save folders through savesRoot and read one file back.
            string[] configs = new string[0];
            CheckNoThrow(
                OwnerLinux,
                "list worlds under savesRoot",
                () => configs = PathUtils.GetFilesRecursively(savesRoot, "Sandbox_config.sbc")
            );
            Info("worlds under savesRoot", configs.Length);
            string current = MyAPIGateway.Session.CurrentPath + "\\Sandbox_config.sbc";
            CheckTrue(
                OwnerLinux,
                "the current world is listed under savesRoot",
                Array.IndexOf(configs, current) >= 0,
                configs.Length > 0 ? configs[0] : "<none>"
            );
            foreach (var config in configs)
                CheckNoHomeFolder("listed " + Path.GetFileName(Path.GetDirectoryName(config)), config);

            MyObjectBuilder_WorldConfiguration world = null;
            CheckProbe(
                OwnerLinux,
                "read Sandbox_config.sbc found under savesRoot",
                true,
                () =>
                    MyObjectBuilderSerializer.DeserializeXML<MyObjectBuilder_WorldConfiguration>(
                        current,
                        out world
                    )
            );
            CheckProbe(
                OwnerLinux,
                "its SessionName is this session's",
                MyAPIGateway.Session.Name,
                () => world == null ? null : world.SessionName
            );
        }

        /// <summary>
        /// The paths of a mod's own assets are absolute in its definitions, and the Context of
        /// every definition names its .sbc file.
        /// </summary>
        private void ProbeDefinitionPaths()
        {
            Section("Definition paths (SE1-0210)");
            CheckNoHomeFolder(
                "VisualScriptLogicProvider.GetCurrentSessionPath()",
                MyVisualScriptLogicProvider.GetCurrentSessionPath()
            );

            var def = GetDiagBlockDefinition(DiagBlockSubtype);
            if (def == null)
            {
                CheckTrue(OwnerLinux, "DiagBlock definition available", false, "<null>");
                return;
            }
            string models = ModContext.ModPath + "\\Models\\LinuxCompatDiag\\";
            CheckNoHomeFolder("def.Model", def.Model);
            CheckNoHomeFolder("def.Icons[0]", def.Icons[0]);
            CheckNoHomeFolder("def?.Icons[0]", def?.Icons[0]);
            string[] icons = def.Icons;
            CheckNoHomeFolder("def.Icons copied", icons[0]);
            CheckNoHomeFolder("def.BuildProgressModels[0].File", def.BuildProgressModels[0].File);
            CheckNoHomeFolder("def.Context.CurrentFile", def.Context.CurrentFile);
            CheckTrue(
                OwnerLinux,
                "def.Model is under ModContext.ModPath",
                def.Model.StartsWith(models, StringComparison.OrdinalIgnoreCase),
                def.Model
            );
            CheckTrue(
                OwnerLinux,
                "def.DisplayNameText is left alone",
                def.DisplayNameText == "LinuxCompat Diagnostics Block",
                def.DisplayNameText
            );

            string leak = null;
            int errors = 0;
            foreach (var error in MyDefinitionErrors.GetErrors())
            {
                errors++;
                foreach (var text in new[] { error.ErrorFile, error.Message })
                {
                    if (leak == null && text != null && text.IndexOf("/home/", StringComparison.Ordinal) >= 0)
                        leak = text;
                }
            }
            Info("definition errors", errors);
            CheckTrue(OwnerLinux, "definition errors name no Linux home", leak == null, leak);
        }

        /// <summary>
        /// MyObjectBuilderSerializer checks its path with PathUtils.EnsureSafePath, which
        /// refuses anything outside the Mods folder, Content and the user data folder.
        /// </summary>
        private void ProbeSerializerContainment()
        {
            Section("MyObjectBuilderSerializer containment (SE1-0210)");
            string bin64 =
                Path.GetDirectoryName(MyAPIGateway.Utilities.GamePaths.ContentPath)
                + "\\Bin64\\linuxcompat-probe.sbc";
            string home = "C:\\users\\steamuser\\linuxcompat-probe.sbc";
            MyObjectBuilder_WorldConfiguration world;
            CheckPathUtilsException(
                "security: refused, MyObjectBuilderSerializer.DeserializeXML: install Bin64",
                () =>
                    MyObjectBuilderSerializer.DeserializeXML<MyObjectBuilder_WorldConfiguration>(
                        bin64,
                        out world
                    ),
                "SecurityException",
                bin64
            );
            CheckPathUtilsException(
                "security: refused, MyObjectBuilderSerializer.DeserializeXML: home folder",
                () =>
                    MyObjectBuilderSerializer.DeserializeXML<MyObjectBuilder_WorldConfiguration>(
                        home,
                        out world
                    ),
                "SecurityException",
                home
            );
        }

        /// <summary>
        /// A caught exception names the mod's Windows paths in its message, file name and stack
        /// trace, whether the mod reads it in the catch block or in an exception filter.
        /// </summary>
        private void ProbeExceptionText()
        {
            Section("Exception text (SE1-0210)");
            const string Protected = "Data\\Scripts\\LinuxCompatDiagnostics\\ProbesPaths.cs";
            try
            {
                MyAPIGateway.Utilities.ReadFileInModLocation(Protected, OwnModItem());
                CheckTrue(OwnerLinux, "protected read throws", false, "<no exception>");
            }
            catch (FileNotFoundException e)
            {
                CheckNoHomeFolderText("caught FileNotFoundException.Message", e.Message);
                CheckNoHomeFolderText("caught FileNotFoundException.FileName", e.FileName);
                CheckNoHomeFolderText("caught FileNotFoundException.ToString()", e.ToString());
                CheckNoHomeFolderText("caught FileNotFoundException.StackTrace", e.StackTrace);
            }

            string filtered = null;
            try
            {
                MyAPIGateway.Utilities.ReadFileInModLocation(Protected, OwnModItem());
            }
            catch (Exception e) when ((filtered = e.ToString()) != null) { }
            CheckNoHomeFolderText("exception text in a filter", filtered);
        }

        private void CheckNoHomeFolderText(string name, string text)
        {
            bool linux =
                text == null
                || text.IndexOf("/home/", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("\\home\\", StringComparison.OrdinalIgnoreCase) >= 0;
            CheckTrue(OwnerLinux, name + " names no Linux home", !linux, text);
        }

        /// <summary>
        /// The path is Windows-shaped and names neither /home nor a Linux user.
        /// The harness also scans the whole log for the real user name.
        /// </summary>
        private void CheckNoHomeFolder(string name, string path)
        {
            bool linux =
                path == null
                || path.IndexOf('/') >= 0
                || path.IndexOf("\\home\\", StringComparison.OrdinalIgnoreCase) >= 0;
            CheckTrue(OwnerLinux, name + " names no Linux home", !linux && IsDriveRooted(path), path);
        }
    }
}

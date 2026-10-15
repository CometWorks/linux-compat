using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;

namespace LinuxCompatDiagnostics
{
    /// <summary>
    /// SE1-0224: the rarer ways a mod could still see a native path after SE1-0210, and the
    /// definition writes that must reach the engine as native paths again.
    /// </summary>
    public partial class LinuxCompatDiagnosticsSession
    {
        private delegate string PathGetter();

        private static string Call(Func<string> read)
        {
            return read();
        }

        private void ProbeRarePathLeaks()
        {
            Section("Rare path leaks (SE1-0224)");

            // Method groups of path getters.
            Func<string> logPath = MyLog.Default.GetFilePath;
            CheckNoHomeFolder("MyLog.Default.GetFilePath as Func<string>", logPath());
            PathGetter getter = MyLog.Default.GetFilePath;
            CheckNoHomeFolder("MyLog.Default.GetFilePath as a mod delegate", getter());
            CheckNoHomeFolder(
                "GetCurrentSessionPath passed as a method group",
                Call(MyVisualScriptLogicProvider.GetCurrentSessionPath)
            );

            // Checkpoint path fields, read from a checkpoint the mod builds.
            var checkpoint = new MyObjectBuilder_Checkpoint();
            checkpoint.VicinityModelsCache = new List<string> { "/tmp/linuxcompat/model.mwm" };
            checkpoint.VicinityArmorModelsCache = new List<string> { "/tmp/linuxcompat/armor.mwm" };
            checkpoint.CustomSkybox = "/tmp/linuxcompat/sky.dds";
            string temp = "C:\\users\\steamuser\\AppData\\Local\\Temp\\linuxcompat\\";
            CheckEquals(
                OwnerLinux,
                "checkpoint.VicinityModelsCache[0]",
                temp + "model.mwm",
                checkpoint.VicinityModelsCache[0]
            );
            CheckEquals(
                OwnerLinux,
                "checkpoint?.VicinityArmorModelsCache[0]",
                temp + "armor.mwm",
                checkpoint?.VicinityArmorModelsCache[0]
            );
            CheckEquals(
                OwnerLinux,
                "checkpoint.CustomSkybox",
                temp + "sky.dds",
                checkpoint.CustomSkybox
            );

            // Exceptions of a parallel task reach the mod without a catch block.
            const string Protected = "Data\\Scripts\\LinuxCompatDiagnostics\\ProbesPaths.cs";
            var task = MyAPIGateway.Parallel.Start(() =>
                MyAPIGateway.Utilities.ReadFileInModLocation(Protected, OwnModItem())
            );
            task.Wait(true);
            var exceptions = task.Exceptions;
            CheckTrue(
                OwnerLinux,
                "parallel task reports its exception",
                exceptions != null && exceptions.Length == 1,
                exceptions == null ? "<null>" : exceptions.Length.ToString()
            );
            if (exceptions != null && exceptions.Length > 0)
            {
                CheckNoHomeFolderText("task.Exceptions[0].Message", exceptions[0].Message);
                CheckNoHomeFolderText("task.Exceptions[0].ToString()", exceptions[0].ToString());
            }

            ProbeDefinitionBuilder();
            ProbeDefinitionWrites();
            ProbeDedicatedPlugins();
        }

        private void ProbeDefinitionBuilder()
        {
            var def = GetDiagBlockDefinition(DiagBlockSubtype);
            if (def == null)
            {
                CheckTrue(OwnerLinux, "DiagBlock definition available", false, "<null>");
                return;
            }

            var builder = def.GetObjectBuilder() as MyObjectBuilder_CubeBlockDefinition;
            if (builder == null)
            {
                CheckTrue(
                    OwnerLinux,
                    "def.GetObjectBuilder() is a cube block builder",
                    false,
                    "<null>"
                );
                return;
            }
            CheckNoHomeFolder("def.GetObjectBuilder().Model", builder.Model);
            CheckNoHomeFolder("def.GetObjectBuilder().Icons[0]", builder.Icons[0]);
            CheckEquals(
                OwnerLinux,
                "def.GetObjectBuilder().Model is def.Model",
                def.Model,
                builder.Model
            );
            CheckNoHomeFolder("def?.GetObjectBuilder().Icons[0]", def?.GetObjectBuilder().Icons[0]);

            string xml = null;
            CheckNoThrow(
                OwnerLinux,
                "serialize def.GetObjectBuilder()",
                () => xml = MyAPIGateway.Utilities.SerializeToXML(builder)
            );
            CheckNoHomeFolderText("serialized def.GetObjectBuilder()", xml);
            CheckNoHomeFolder("def.Icons[0] after GetObjectBuilder", def.Icons[0]);
        }

        /// <summary>
        /// A Windows path written into a definition is stored native: a forward slash spelling
        /// reads back in the canonical Windows form. Without the write translation it would read
        /// back as written, and engine code that loads it without MyFileSystem could not find it.
        /// </summary>
        private void ProbeDefinitionWrites()
        {
            var def = GetDiagBlockDefinition(DiagBlockMutableSubtype);
            if (def == null || ModContext == null)
            {
                CheckTrue(OwnerLinux, "mutable DiagBlock definition available", false, "<null>");
                return;
            }

            string[] icons = def.Icons;
            string model = def.Model;
            string icon = ModContext.ModPath + "\\Textures\\LinuxCompatDiag\\TestIcon.dds";
            string cube = ModContext.ModPath + "\\Models\\LinuxCompatDiag\\TestCube.mwm";
            try
            {
                def.Icons = new string[] { icon.Replace('\\', '/') };
                CheckEquals(OwnerLinux, "def.Icons = Windows path, read back", icon, def.Icons[0]);
                def.Icons[0] = icon.Replace('\\', '/');
                CheckEquals(
                    OwnerLinux,
                    "def.Icons[0] = Windows path, read back",
                    icon,
                    def.Icons[0]
                );
                def.Model = cube.Replace('\\', '/');
                CheckEquals(OwnerLinux, "def.Model = Windows path, read back", cube, def.Model);
            }
            finally
            {
                def.Icons = icons;
                def.Model = model;
            }
            CheckEquals(OwnerLinux, "def.Model restored", model, def.Model);
        }

        /// <summary>IMyConfigDedicated.Plugins holds plugin DLL paths; dedicated server only.</summary>
        private void ProbeDedicatedPlugins()
        {
            var config = MyAPIGateway.Utilities.ConfigDedicated;
            if (config == null)
                return;

            List<string> plugins = config.Plugins;
            if (plugins != null)
            {
                foreach (var plugin in plugins)
                    CheckNoHomeFolder("ConfigDedicated.Plugins item", plugin);
            }
            try
            {
                // Odd casing: only a path stored native reads back in the canonical form.
                config.Plugins = new List<string> { "c:\\USERS\\steamuser\\linuxcompat-probe.dll" };
                CheckEquals(
                    OwnerLinux,
                    "ConfigDedicated.Plugins set/get round trip",
                    "C:\\users\\steamuser\\linuxcompat-probe.dll",
                    config.Plugins[0]
                );
            }
            finally
            {
                config.Plugins = plugins;
            }
        }

        /// <summary>
        /// The checkpoint the session builds names the loading screen image native. Runs on the
        /// first update, when the session can build one.
        /// </summary>
        private void ProbeSessionCheckpoint()
        {
            Section("Session checkpoint (SE1-0224)");
            string original = MyAPIGateway
                .Session.GetCheckpoint("LinuxCompat probe")
                .CustomLoadingScreenImage;
            string image = ModContext.ModPath + "\\Textures\\LinuxCompatDiag\\TestIcon.dds";
            try
            {
                MyVisualScriptLogicProvider.SetCustomLoadingScreenImage(image);
                CheckEquals(
                    OwnerLinux,
                    "GetCheckpoint().CustomLoadingScreenImage",
                    image,
                    MyAPIGateway.Session.GetCheckpoint("LinuxCompat probe").CustomLoadingScreenImage
                );
                CheckEquals(
                    OwnerLinux,
                    "GetWorld().Checkpoint.CustomLoadingScreenImage",
                    image,
                    MyAPIGateway.Session.GetWorld().Checkpoint.CustomLoadingScreenImage
                );
            }
            finally
            {
                MyVisualScriptLogicProvider.SetCustomLoadingScreenImage(original);
            }
        }
    }
}

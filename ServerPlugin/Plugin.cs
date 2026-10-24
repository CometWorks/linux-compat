using System;
using System.IO;
using ClientPlugin.Patches.PathHandling;
using ClientPlugin.Rewriter;
using HarmonyLib;
using Microsoft.CodeAnalysis.CSharp;
using PluginSdk.Config;
using ServerPlugin.Config;
using VRage.FileSystem;
using VRage.Plugins;
using VRage.Scripting;
#if !LOCAL_BUILD
using System.Reflection;

[assembly: AssemblyVersion("1.0.39.0")]
[assembly: AssemblyFileVersion("1.0.39.0")]

#endif

namespace ServerPlugin;

// ReSharper disable once UnusedType.Global
public class Plugin : IPlugin
{
    public const string Name = "LinuxCompatServer";

    private static readonly object ConfigSync = new();
    private static LinuxCompatConfig config;

    // Discovered by the Quasar agent, which edits the live instance.
    // ReSharper disable once UnusedMember.Global
    public LinuxCompatConfig PluginConfig => LoadConfig();

    public static CSharpCompilation Rewrite(CSharpCompilation compilation, MyApiTarget target) =>
        CompilationRewriter.Rewrite(compilation, target);

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    public void Init(object gameInstance)
    {
        // The rewriter shims are registered in Preloader.Finish, before mods compile.
        var harmony = new Harmony("LinuxCompatServer");
        harmony.PatchCategory("Init");
    }

    public void Dispose() { }

    /// <summary>
    /// Called from <c>Preloader.Finish</c>: the world's mods compile before <c>Init</c>, and the
    /// path security switches have to be read from the config by then.
    /// </summary>
    internal static void InstallConfigLoader() => PathSecurity.Loader = LoadConfig;

    // The file is read and saved outside ConfigSync: ConfigStorage takes PluginSdk's configuration
    // lock, and PluginSdk holds that lock while it reads PluginConfig (plugin init, Observe). Under
    // ConfigSync, a node start deadlocked against the Quasar agent reading PluginConfig (DF-698).
    private static LinuxCompatConfig LoadConfig()
    {
        if (config != null || MyFileSystem.UserDataPath == null)
            return config;

        var path = Path.Combine(MyFileSystem.UserDataPath, "LinuxCompat.cfg");
        LinuxCompatConfig loaded;
        var broken = false;
        try
        {
            loaded = ConfigStorage.LoadXml<LinuxCompatConfig>(path);
        }
        catch (Exception ex)
        {
            // Keep the broken file for the admin to fix; run with the defaults meanwhile.
            Log($"Failed to read {path}, using the defaults: {ex.Message}");
            loaded = new LinuxCompatConfig();
            broken = true;
        }

        lock (ConfigSync)
        {
            // Another thread loaded it meanwhile: everyone uses the first one
            if (config != null)
                return config;
            config = loaded;
        }

        if (!broken)
        {
            // A fresh install gets a sparse file to edit, and Quasar's changes are kept.
            Save(path);
            loaded.PropertyChanged += (_, _) => Save(path);
        }
        return loaded;
    }

    private static void Save(string path)
    {
        try
        {
            ConfigStorage.SaveXml(config, path);
        }
        catch (Exception ex)
        {
            Log($"Failed to save {path}: {ex.Message}");
        }
    }

    private static void Log(string message)
    {
        Console.WriteLine($"[LinuxCompat] {message}");
        try
        {
            VRage.Utils.MyLog.Default?.WriteLineAndConsole($"[LinuxCompat] {message}");
        }
        catch
        {
            // MyLog is not available yet.
        }
    }

    public void Update() { }
}

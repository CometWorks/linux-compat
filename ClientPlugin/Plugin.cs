using ClientPlugin.Compatibility;
using ClientPlugin.Patches.PathHandling;
using ClientPlugin.Rewriter;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Layouts;
using HarmonyLib;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox.Graphics.GUI;
using VRage.FileSystem;
using VRage.Plugins;
using VRage.Scripting;
#if !LOCAL_BUILD
using System.Reflection;

[assembly: AssemblyVersion("1.0.39.0")]
[assembly: AssemblyFileVersion("1.0.39.0")]

#endif

namespace ClientPlugin;

// ReSharper disable once UnusedType.Global
public class Plugin : IPlugin
{
    public const string Name = "LinuxCompat";

    private SettingsGenerator settingsGenerator;

    public static CSharpCompilation Rewrite(CSharpCompilation compilation, MyApiTarget target) =>
        CompilationRewriter.Rewrite(compilation, target);

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    public void Init(object gameInstance)
    {
        // Cecil-injected mod path getters require translation before mods run.
        PathTranslation.Init();

        // Mod compilation needs the rewriter shims whitelisted and referenced.
        ShimRegistration.Register();

        // The path security switches, read from Storage/LinuxCompat.cfg on first use.
        PathSecurity.Loader = () => MyFileSystem.UserDataPath == null ? null : Config.Current;

        var harmony = new Harmony("LinuxCompat");
        harmony.PatchCategory("Init");
    }

    public void Dispose() { }

    // ReSharper disable once UnusedMember.Global
    public void OpenConfigDialog()
    {
        settingsGenerator ??= new SettingsGenerator();
        settingsGenerator.SetLayout<Simple>();
        MyGuiSandbox.AddScreen(settingsGenerator.Dialog);
    }

    public void Update()
    {
        // Run render-thread continuations on the game thread.
        MainThreadDispatcher.Pump();

        // Diagnostic Havok stepping override (SE_HAVOK_SEQUENTIAL / SE_HAVOK_SINGLETHREAD).
        HavokSchedulingOverride.Apply();
    }
}

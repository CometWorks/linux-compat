using ClientPlugin.Compatibility;
using ClientPlugin.Patches.PathHandling;
using ClientPlugin.Rewriter;
using HarmonyLib;
using Microsoft.CodeAnalysis.CSharp;
using VRage.Plugins;
using VRage.Scripting;
#if !LOCAL_BUILD
using System.Reflection;

[assembly: AssemblyVersion("1.0.17.0")]
[assembly: AssemblyFileVersion("1.0.17.0")]

#endif

namespace ClientPlugin;

// ReSharper disable once UnusedType.Global
public class Plugin : IPlugin
{
    public const string Name = "LinuxCompat";

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

        var harmony = new Harmony("LinuxCompat");
        harmony.PatchCategory("Init");
    }

    public void Dispose()
    {
        if (RenderingConfig.AllowRendering)
            SdlRenderThread.Stop();
    }

    public void Update()
    {
        // Run render-thread continuations on the game thread.
        MainThreadDispatcher.Pump();
    }
}

using ClientPlugin.Rewriter;
using HarmonyLib;
using Microsoft.CodeAnalysis.CSharp;
using VRage.Plugins;
using VRage.Scripting;
#if !LOCAL_BUILD
using System.Reflection;

[assembly: AssemblyVersion("1.0.9.0")]
[assembly: AssemblyFileVersion("1.0.9.0")]

#endif

namespace ServerPlugin;

// ReSharper disable once UnusedType.Global
public class Plugin : IPlugin
{
    public const string Name = "LinuxCompatServer";

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

    public void Update() { }
}

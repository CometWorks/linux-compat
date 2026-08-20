using HarmonyLib;
using VRage.Plugins;
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

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    public void Init(object gameInstance)
    {
        // Path rewriting must start in Preloader.Finish before server mod compilation.

        var harmony = new Harmony("LinuxCompatServer");
        harmony.PatchCategory("Init");
    }

    public void Dispose() { }

    public void Update() { }
}

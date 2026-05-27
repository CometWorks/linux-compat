using HarmonyLib;
using VRage.Plugins;

// Set the assembly version manually if compiled by Pulsar (it won't create what was in AssemblyInfo.cs before)
#if !DEV_BUILD
using System.Reflection;

[assembly: AssemblyVersion("1.0.9.0")]
[assembly: AssemblyFileVersion("1.0.9.0")]
#endif

namespace ServerPlugin;

// ReSharper disable once UnusedType.Global
public class Plugin : IPlugin
{
    public const string Name = "LinuxCompatServer";

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
        // NOTE: PathTranslation.Init() and RewriterRegistration.Register() are
        // performed from Preloader.Finish, not here. On the dedicated server
        // IPlugin.Init runs AFTER the auto-loaded session has already
        // compiled its mods — too late for the path-substitution rewriter to
        // take effect on mod source. See Preloader.cs Finish() for details.

        var harmony = new Harmony("LinuxCompatServer");
        harmony.PatchCategory("Init");
    }

    public void Dispose()
    {
    }

    public void Update()
    {
    }
}

using ServerPlugin.Patches.PathHandling;
using ServerPlugin.Rewriter;
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
        // Build the Linux→Windows prefix translation table before anything
        // that might call PathHelpers.ToWindowsPath / WindowsPath.FromGame /
        // .GetTempPath. The Cecil-injected explicit interface getters on
        // MyModContext also depend on this table being populated by the
        // time the first mod reads ModPath/ModPathData.
        PathTranslation.Init();

        // Plug our Path-substitution pass into the DotNetCompat compiler
        // hook before any mod is compiled. DotNetCompat is always loaded
        // earlier by Pulsar, so by the time this runs the extension point
        // exists. Mod compilation only happens once a session loads, well
        // after Init.
        RewriterRegistration.Register();

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

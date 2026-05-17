using ClientPlugin.Compatibility;
using ClientPlugin.Patches.PathHandling;
using ClientPlugin.Rewriter;
using HarmonyLib;
using VRage.Plugins;

// Set the assembly version manually if compiled by Pulsar (it won't create what was in AssemblyInfo.cs before)
#if !DEV_BUILD
using System.Reflection;

[assembly: AssemblyVersion("1.0.7.0")]
[assembly: AssemblyFileVersion("1.0.7.0")]
#endif

namespace ClientPlugin;

// ReSharper disable once UnusedType.Global
public class Plugin : IPlugin
{
    public const string Name = "LinuxCompat";

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
        // Build the Linux→Windows prefix translation table before anything
        // that might call PathHelpers.ToWindowsPath / WindowsPath.FromGame /
        // .GetTempPath. The Cecil-injected explicit interface getters on
        // MyModContext also depend on this table being populated by the
        // time the first mod reads ModPath/ModPathData.
        PathTranslation.Init();

        // Bring up the dedicated SDL render thread before any SDL3 use. It
        // runs SDL_Init(VIDEO) once on its own thread and from then on owns
        // every SDL3 call: splash window, main game window, event pump and
        // clipboard. Starting here ensures the thread is ready by the time
        // MyCommonProgramStartup.InitSplashScreen fires (which our
        // ShowSplashScreenPatch dispatches onto the render thread).
        SdlRenderThread.Start();

        // Plug our Path-substitution pass into the DotNetCompat compiler
        // hook before any mod is compiled. DotNetCompat is always loaded
        // earlier by Pulsar, so by the time this runs the extension point
        // exists. Mod compilation only happens once a session loads, well
        // after Init.
        RewriterRegistration.Register();

        var harmony = new Harmony("LinuxCompat");
        harmony.PatchCategory("Init");
    }

    public void Dispose()
    {
        SdlRenderThread.Stop();
    }

    public void Update()
    {
        // Drain continuations posted from the render thread (e.g. clipboard
        // read results destined for paste handlers). Runs on the main game
        // thread once per frame.
        MainThreadDispatcher.Pump();
    }
}
using ClientPlugin.Compatibility;
using HarmonyLib;
using VRage.Plugins;

// Set the assembly version manually if compiled by Pulsar (it won't create what was in AssemblyInfo.cs before)
#if !DEV_BUILD
using System.Reflection;

[assembly: AssemblyVersion("1.0.5.0")]
[assembly: AssemblyFileVersion("1.0.5.0")]
#endif

namespace ClientPlugin;

// ReSharper disable once UnusedType.Global
public class Plugin : IPlugin
{
    public const string Name = "LinuxCompat";

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
        // Bring up the dedicated SDL render thread before any SDL3 use. It
        // runs SDL_Init(VIDEO) once on its own thread and from then on owns
        // every SDL3 call: splash window, main game window, event pump and
        // clipboard. Starting here ensures the thread is ready by the time
        // MyCommonProgramStartup.InitSplashScreen fires (which our
        // ShowSplashScreenPatch dispatches onto the render thread).
        SdlRenderThread.Start();

        var harmony = new Harmony("LinuxCompat");
        harmony.PatchCategory("Init");
    }

    public void Dispose()
    {
        SdlRenderThread.Stop();
    }

    public void Update()
    {
    }
}
using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ServerPlugin.Patches.PlatformGuards;

// VRage.Dedicated.DedicatedServer.IsVcRedist2019Installed probes the Windows
// registry for the Visual C++ 2015-2019 runtime:
//
//     RegistryKey currentKey = Registry.LocalMachine
//         .TryOpenKey("SOFTWARE").TryOpenKey("Classes")
//         .TryOpenKey("Installer").TryOpenKey("Dependencies");
//     var k = currentKey.TryOpenKey("Microsoft.VS.VC_RuntimeAdditionalVSU_amd64,v14")
//          ?? currentKey.TryOpenKey("Microsoft.VS.VC_RuntimeMinimumVSU_amd64,v14");
//     return k != null;
//
// RunMain calls it as a hard gate; if false it logs "Please install latest
// C++ redistributable package for 2015-2019 x64" and exits before any world
// loading happens. On Linux the registry path does not exist (Microsoft.Win32
// .Registry returns an in-memory empty registry), so the check always fails
// and the server can never reach Game Ready.
//
// The check is a Windows-only sanity guard for MSVC-runtime DLLs that ship
// with the game itself (msvcp140.dll, vcruntime140.dll, etc. live next to
// SpaceEngineersDedicated.exe). On Linux those Windows DLLs are not used at
// all -- the native code paths the runtime would have needed them for are
// handled by libVRageNative.so / libHavok.so / libRecastDetour.so, which are
// initialized earlier in this preloader and have no MSVC-runtime dependency.
//
// We replace the method body with `return true;` so the gate is always
// satisfied. This is safe on Windows-with-redist (true is the correct
// answer), unsafe on Windows-without-redist (but this plugin only loads on
// Linux), and correct on Linux (the dependency genuinely is not needed).
public static class IsVcRedist2019InstalledPrepatch
{
    public static void Prepatch(AssemblyDefinition asmDef)
    {
        if (asmDef.Name.Name != "VRage.Dedicated")
            return;

        var type = asmDef.MainModule.GetType("VRage.Dedicated.DedicatedServer");
        if (type == null)
        {
            Console.WriteLine("[LinuxCompatServer] IsVcRedist2019InstalledPrepatch: VRage.Dedicated.DedicatedServer not found");
            return;
        }

        var method = type.Methods.FirstOrDefault(m =>
            m.Name == "IsVcRedist2019Installed" &&
            m.IsStatic &&
            m.Parameters.Count == 0 &&
            m.ReturnType.FullName == "System.Boolean");
        if (method?.Body == null)
        {
            Console.WriteLine("[LinuxCompatServer] IsVcRedist2019InstalledPrepatch: IsVcRedist2019Installed() not found");
            return;
        }

        var body = method.Body;
        body.Instructions.Clear();
        body.ExceptionHandlers.Clear();
        body.Variables.Clear();
        var il = body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Ret));

        Console.WriteLine("[LinuxCompatServer] IsVcRedist2019InstalledPrepatch: forced IsVcRedist2019Installed() => true");
    }
}

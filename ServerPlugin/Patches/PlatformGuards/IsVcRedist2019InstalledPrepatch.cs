using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ServerPlugin.Patches.PlatformGuards;

// Linux native wrappers do not depend on the Windows VC++ redistributable.
public static class IsVcRedist2019InstalledPrepatch
{
    public static void Prepatch(AssemblyDefinition asmDef)
    {
        if (asmDef.Name.Name != "VRage.Dedicated")
            return;

        var type = asmDef.MainModule.GetType("VRage.Dedicated.DedicatedServer");
        if (type == null)
        {
            Console.WriteLine(
                "[LinuxCompatServer] IsVcRedist2019InstalledPrepatch: VRage.Dedicated.DedicatedServer not found"
            );
            return;
        }

        var method = type.Methods.FirstOrDefault(m =>
            m.Name == "IsVcRedist2019Installed"
            && m.IsStatic
            && m.Parameters.Count == 0
            && m.ReturnType.FullName == "System.Boolean"
        );
        if (method?.Body == null)
        {
            Console.WriteLine(
                "[LinuxCompatServer] IsVcRedist2019InstalledPrepatch: IsVcRedist2019Installed() not found"
            );
            return;
        }

        var body = method.Body;
        body.Instructions.Clear();
        body.ExceptionHandlers.Clear();
        body.Variables.Clear();
        var il = body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Ret));

        Console.WriteLine(
            "[LinuxCompatServer] IsVcRedist2019InstalledPrepatch: forced IsVcRedist2019Installed() => true"
        );
    }
}

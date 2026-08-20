using System;
using System.Linq;
using ClientPlugin.Tools;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ClientPlugin.Patches.PathHandling;

// Inject into the method body so JIT-inlined copies retain path normalization.
public static class MyFileSystemOpenPrepatch
{
    public static void Prepatch(AssemblyDefinition asmDef)
    {
        if (asmDef.Name.Name != "VRage.Library")
            return;

        var module = asmDef.MainModule;

        var type = module.GetType("VRage.FileSystem.MyFileSystem");
        if (type == null)
        {
            Console.WriteLine("[LinuxCompat] MyFileSystemOpenPrepatch: VRage.FileSystem.MyFileSystem not found in VRage.Library (already patched or upstream renamed?)");
            return;
        }

        var openMethod = type.Methods.FirstOrDefault(m =>
            m.Name == "Open" &&
            m.IsStatic &&
            m.Parameters.Count == 4 &&
            m.Parameters[0].ParameterType.FullName == "System.String" &&
            m.Parameters[1].ParameterType.FullName == "System.IO.FileMode" &&
            m.Parameters[2].ParameterType.FullName == "System.IO.FileAccess" &&
            m.Parameters[3].ParameterType.FullName == "System.IO.FileShare");
        if (openMethod?.Body == null)
        {
            Console.WriteLine("[LinuxCompat] MyFileSystemOpenPrepatch: MyFileSystem.Open(string, FileMode, FileAccess, FileShare) not found");
            return;
        }

        if (openMethod.Body.Instructions.Any(IsResolveAbsoluteCall))
        {
            Console.WriteLine("[LinuxCompat] MyFileSystemOpenPrepatch: Open already carries the PathCache.ResolveAbsolute prologue (skipping)");
            return;
        }

        openMethod.Body.Instructions.RecordOriginalCode(openMethod);

        var resolveAbsolute = ImportResolveAbsolute(module);

        var il = openMethod.Body.GetILProcessor();
        var first = openMethod.Body.Instructions[0];

        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Call, resolveAbsolute));
        il.InsertBefore(first, il.Create(OpCodes.Starg_S, openMethod.Parameters[0]));

        openMethod.Body.Instructions.RecordPatchedCode(openMethod);

        Console.WriteLine("[LinuxCompat] MyFileSystemOpenPrepatch: injected PathCache.ResolveAbsolute prologue into MyFileSystem.Open");
    }

    private static bool IsResolveAbsoluteCall(Instruction instr)
    {
        if (instr.OpCode != OpCodes.Call) return false;
        if (instr.Operand is not MethodReference mr) return false;
        return mr.Name == "ResolveAbsolute" &&
               mr.DeclaringType != null &&
               mr.DeclaringType.FullName == "ClientPlugin.Patches.PathHandling.PathCache";
    }

    private static MethodReference ImportResolveAbsolute(ModuleDefinition module)
    {
        var linuxCompatRef = module.AssemblyReferences
            .FirstOrDefault(r => r.Name == "LinuxCompat");
        if (linuxCompatRef == null)
        {
            linuxCompatRef = new AssemblyNameReference("LinuxCompat", new Version(1, 0, 0, 0))
            {
                PublicKeyToken = Array.Empty<byte>(),
                PublicKey = Array.Empty<byte>(),
                Culture = string.Empty,
                HashAlgorithm = AssemblyHashAlgorithm.None,
            };
            module.AssemblyReferences.Add(linuxCompatRef);
        }

        var pathCacheType = new TypeReference(
            "ClientPlugin.Patches.PathHandling", "PathCache", module, linuxCompatRef, false);

        var stringRef = module.TypeSystem.String;
        var method = new MethodReference("ResolveAbsolute", stringRef, pathCacheType)
        {
            HasThis = false,
            ExplicitThis = false,
            CallingConvention = MethodCallingConvention.Default,
        };
        method.Parameters.Add(new ParameterDefinition(stringRef));
        return method;
    }
}

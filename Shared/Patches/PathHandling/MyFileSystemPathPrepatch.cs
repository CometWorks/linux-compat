using System;
using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Tools;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ClientPlugin.Patches.PathHandling;

/// <summary>
/// Resolves the path argument of the <c>MyFileSystem</c> entry points inside their own bodies.
/// <para>
/// <c>DirectoryExists</c> and the <c>GetFiles</c> overloads are one-line wrappers, so the JIT
/// inlines them into their callers, and an inlined copy never reaches a Harmony prefix. Which
/// calls get inlined changes from build to build. With LinuxCompat 1.0.34,
/// <c>MyDefinitionManager.GetDefinitionBuilders</c> listed the <c>Data</c> folder of a mod that
/// ships it as <c>data</c> unresolved and loaded none of its definitions.
/// Injected into the method bodies, the resolution also runs in every inlined copy.
/// </para>
/// </summary>
public static class MyFileSystemPathPrepatch
{
    public static void Prepatch(AssemblyDefinition asmDef)
    {
        var module = asmDef.MainModule;
        var type = module.GetType("VRage.FileSystem.MyFileSystem");
        if (type == null)
        {
            Console.WriteLine(
                "[LinuxCompat] MyFileSystemPathPrepatch: VRage.FileSystem.MyFileSystem not found in VRage.Library"
            );
            return;
        }

        var resolveAbsolute = module.ImportReference(
            typeof(PathCache).GetMethod(nameof(PathCache.ResolveAbsolute), [typeof(string)])
        );
        var sorted = module.ImportReference(
            typeof(GetFilesSort).GetMethod(
                nameof(GetFilesSort.Sorted),
                [typeof(IEnumerable<string>)]
            )
        );

        Patch(type, "Open", 4, resolveAbsolute, null);
        Patch(type, "DirectoryExists", 1, resolveAbsolute, null);
        Patch(type, "GetFiles", 1, resolveAbsolute, sorted);
        Patch(type, "GetFiles", 2, resolveAbsolute, sorted);
        Patch(type, "GetFiles", 3, resolveAbsolute, sorted);
    }

    private static void Patch(
        TypeDefinition type,
        string name,
        int parameterCount,
        MethodReference resolveAbsolute,
        MethodReference resultFilter
    )
    {
        var method = type.Methods.FirstOrDefault(m =>
            m.Name == name
            && m.IsStatic
            && m.Parameters.Count == parameterCount
            && m.Parameters[0].ParameterType.FullName == "System.String"
        );
        var label = $"MyFileSystem.{name}/{parameterCount}";
        if (method?.Body == null)
        {
            Console.WriteLine($"[LinuxCompat] MyFileSystemPathPrepatch: {label} not found");
            return;
        }

        if (method.Body.Instructions.Any(IsResolveAbsoluteCall))
        {
            Console.WriteLine($"[LinuxCompat] MyFileSystemPathPrepatch: {label} already patched");
            return;
        }

        method.Body.Instructions.RecordOriginalCode(method);

        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions[0];
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Call, resolveAbsolute));
        il.InsertBefore(first, il.Create(OpCodes.Starg_S, method.Parameters[0]));

        if (resultFilter != null)
        {
            // Turn each ret into the filter call, so branches to it pass through the filter too.
            foreach (
                var ret in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList()
            )
            {
                ret.OpCode = OpCodes.Call;
                ret.Operand = resultFilter;
                il.InsertAfter(ret, il.Create(OpCodes.Ret));
            }
        }

        method.Body.Instructions.RecordPatchedCode(method);
        Console.WriteLine($"[LinuxCompat] MyFileSystemPathPrepatch: patched {label}");
    }

    private static bool IsResolveAbsoluteCall(Instruction instr) =>
        instr.OpCode == OpCodes.Call
        && instr.Operand is MethodReference mr
        && mr.Name == nameof(PathCache.ResolveAbsolute)
        && mr.DeclaringType?.FullName == typeof(PathCache).FullName;
}

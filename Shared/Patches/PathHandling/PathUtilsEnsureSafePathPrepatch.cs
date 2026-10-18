using System;
using System.Linq;
using ClientPlugin.Tools;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ClientPlugin.Patches.PathHandling;

/// <summary>
/// Makes <c>PathUtils.EnsureSafePath</c> check the native path the file system will open.
/// <para>
/// Mods get <c>C:\users\steamuser\...</c> paths. Linux does not consider those rooted, so the check
/// combined them with the Mods folder and let them pass, and <c>MyFileSystem.Open</c> then opened
/// their native target. Through <c>MyObjectBuilderSerializer</c>, which calls the check itself, a
/// mod could read and write any file under the home folder. On Windows the same call is refused.
/// </para>
/// Injected into the method body, like <see cref="MyFileSystemPathPrepatch"/>, so callers that
/// inline it are covered too.
/// </summary>
public static class PathUtilsEnsureSafePathPrepatch
{
    public static void Prepatch(AssemblyDefinition asmDef)
    {
        var method = asmDef
            .MainModule.GetType("VRage.Library.Utils.PathUtils")
            ?.Methods.FirstOrDefault(m =>
                m.Name == "EnsureSafePath"
                && m.IsStatic
                && m.Parameters.Count == 1
                && m.Parameters[0].ParameterType.FullName == "System.String"
            );
        if (method?.Body == null)
        {
            Console.WriteLine(
                "[LinuxCompat] PathUtilsEnsureSafePathPrepatch: PathUtils.EnsureSafePath(string) not found"
            );
            return;
        }

        var fromWindowsPath = typeof(PathHelpers).GetMethod(
            nameof(PathHelpers.FromWindowsPath),
            [typeof(string)]
        );
        if (
            method.Body.Instructions.Any(i =>
                i.OpCode == OpCodes.Call
                && i.Operand is MethodReference mr
                && mr.Name == fromWindowsPath.Name
                && mr.DeclaringType.FullName == typeof(PathHelpers).FullName
            )
        )
            return;

        method.Body.Instructions.RecordOriginalCode(method);

        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions[0];
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(
            first,
            il.Create(OpCodes.Call, asmDef.MainModule.ImportReference(fromWindowsPath))
        );
        il.InsertBefore(first, il.Create(OpCodes.Starg_S, method.Parameters[0]));

        method.Body.Instructions.RecordPatchedCode(method);

        Console.WriteLine(
            "[LinuxCompat] PathUtilsEnsureSafePathPrepatch: EnsureSafePath now checks native paths"
        );
    }
}

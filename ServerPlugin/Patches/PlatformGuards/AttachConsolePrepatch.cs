using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ServerPlugin.Patches.PlatformGuards;

// Treat inherited Linux stdout as an attached console and skip kernel32 calls.
public static class AttachConsolePrepatch
{
    public static void Prepatch(AssemblyDefinition asmDef)
    {
        if (asmDef.Name.Name != "VRage.Dedicated")
            return;

        var type = asmDef.MainModule.GetType("VRage.Dedicated.DedicatedServer");
        if (type == null)
        {
            Console.WriteLine(
                "[LinuxCompatServer] AttachConsolePrepatch: VRage.Dedicated.DedicatedServer not found"
            );
            return;
        }

        var runMain = type.Methods.FirstOrDefault(m => m.Name == "RunMain" && m.IsStatic);
        if (runMain?.Body == null)
        {
            Console.WriteLine(
                "[LinuxCompatServer] AttachConsolePrepatch: RunMain method not found"
            );
            return;
        }

        var il = runMain.Body.GetILProcessor();
        var instructions = runMain.Body.Instructions;
        var rewritten = 0;

        for (var i = 0; i < instructions.Count; i++)
        {
            var instr = instructions[i];
            if (instr.OpCode != OpCodes.Call)
                continue;
            if (instr.Operand is not MethodReference mr)
                continue;
            if (mr.Name != "AttachConsole")
                continue;
            if (mr.DeclaringType?.FullName != "VRage.Dedicated.DedicatedServer")
                continue;

            // Preserve instruction identity because branches may target it.
            var ldc1 = il.Create(OpCodes.Ldc_I4_1);
            il.InsertAfter(instr, ldc1);
            instr.OpCode = OpCodes.Pop;
            instr.Operand = null;
            rewritten++;
            i++;
        }

        if (rewritten > 0)
            Console.WriteLine(
                $"[LinuxCompatServer] AttachConsolePrepatch: neutralized {rewritten} AttachConsole call(s) in DedicatedServer.RunMain"
            );
        else
            Console.WriteLine(
                "[LinuxCompatServer] AttachConsolePrepatch: no AttachConsole call found in DedicatedServer.RunMain (already patched or upstream changed?)"
            );
    }
}

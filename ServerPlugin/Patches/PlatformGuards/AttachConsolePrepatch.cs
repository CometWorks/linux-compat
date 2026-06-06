using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ServerPlugin.Patches.PlatformGuards;

// VRage.Dedicated.DedicatedServer.RunMain opens with a Windows-only console
// attach block:
//
//     if (showConsole && Environment.UserInteractive)
//     {
//         MySandboxGame.IsConsoleVisible = true;
//         if (AttachConsole(uint.MaxValue) == 0 &&
//             Marshal.GetLastWin32Error() != 5 &&
//             AllocConsole() != 0)
//         {
//             InitializeInStream();
//             InitializeOutStream();
//         }
//     }
//
// AttachConsole and AllocConsole are [DllImport("kernel32.dll")]. On Linux the
// runtime can't bind kernel32.dll, so the first AttachConsole call throws
// DllNotFoundException and the server dies before it ever boots:
//
//     System.DllNotFoundException: Unable to load shared library 'kernel32.dll'
//         at VRage.Dedicated.DedicatedServer.AttachConsole(UInt32)
//         at VRage.Dedicated.DedicatedServer.RunMain(...)
//         at VRage.Dedicated.DedicatedServer.Run_Patch1(...)
//         at SpaceEngineersDedicated.MyProgram.Main(...)
//
// On Linux the DS inherits its parent shell's stdout/stderr from the launching
// script (Magnetar -> Interim -> DS), so neither AttachConsole nor AllocConsole
// is needed: Console.Out is already wired and the runtime console pipeline
// just works.
//
// We neutralize the block by rewriting the `call AttachConsole(uint)`
// instruction to `pop; ldc.i4.1`. AttachConsole's contract is "non-zero =
// success", so the outer condition `AttachConsole(...) == 0` becomes false and
// short-circuits past Marshal.GetLastWin32Error and AllocConsole. Those calls
// stay in the IL but are unreachable, and P/Invoke linking is lazy (per-call,
// not per-type-load), so kernel32.dll is never probed.
//
// In-place mutation of the existing Call instruction (rather than il.Replace)
// preserves the instruction's identity so any branch operands pointing at it
// remain valid.
public static class AttachConsolePrepatch
{
    public static void Prepatch(AssemblyDefinition asmDef)
    {
        if (asmDef.Name.Name != "VRage.Dedicated")
            return;

        var type = asmDef.MainModule.GetType("VRage.Dedicated.DedicatedServer");
        if (type == null)
        {
            Console.WriteLine("[LinuxCompatServer] AttachConsolePrepatch: VRage.Dedicated.DedicatedServer not found");
            return;
        }

        var runMain = type.Methods.FirstOrDefault(m => m.Name == "RunMain" && m.IsStatic);
        if (runMain?.Body == null)
        {
            Console.WriteLine("[LinuxCompatServer] AttachConsolePrepatch: RunMain method not found");
            return;
        }

        var il = runMain.Body.GetILProcessor();
        var instructions = runMain.Body.Instructions;
        var rewritten = 0;

        for (var i = 0; i < instructions.Count; i++)
        {
            var instr = instructions[i];
            if (instr.OpCode != OpCodes.Call) continue;
            if (instr.Operand is not MethodReference mr) continue;
            if (mr.Name != "AttachConsole") continue;
            if (mr.DeclaringType?.FullName != "VRage.Dedicated.DedicatedServer") continue;

            // Mutate the existing Call in place (preserves any branch targets
            // referencing this instruction) so it becomes a Pop that drops the
            // pushed `uint.MaxValue` argument, then insert an Ldc_I4_1 right
            // after it to leave a "success" value on the stack in place of the
            // original return value.
            var ldc1 = il.Create(OpCodes.Ldc_I4_1);
            il.InsertAfter(instr, ldc1);
            instr.OpCode = OpCodes.Pop;
            instr.Operand = null;
            rewritten++;
            i++; // skip the inserted ldc.i4.1
        }

        if (rewritten > 0)
            Console.WriteLine($"[LinuxCompatServer] AttachConsolePrepatch: neutralized {rewritten} AttachConsole call(s) in DedicatedServer.RunMain");
        else
            Console.WriteLine("[LinuxCompatServer] AttachConsolePrepatch: no AttachConsole call found in DedicatedServer.RunMain (already patched or upstream changed?)");
    }
}

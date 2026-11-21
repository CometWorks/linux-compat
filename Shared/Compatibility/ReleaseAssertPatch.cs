using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ClientPlugin.Compatibility;

// MyDebug.FailRelease and AssertRelease reach MyTraceListener.TraceMessage in
// VRage.Platform.Windows, which logs "ASSERT: ..." and then shows a WinForms
// Abort/Retry/Ignore box. System.Windows.Forms does not exist on Linux, and the
// method has a DialogResult local, so it throws before it logs anything and the
// assert crashes the game. A world with two grids sharing an entity id does that
// while loading, where the Windows game logs the assert and drops one grid.
//
// Keep the log line and the ignored-message bookkeeping, drop the box: the
// assert continues as if Ignore was pressed.
public static class ReleaseAssertPatch
{
    public static bool Apply(AssemblyDefinition asmDef)
    {
        var body = asmDef
            .MainModule.GetType("VRage.Platform.Windows.Forms.MyDebugListenerProvider")
            ?.NestedTypes.FirstOrDefault(t => t.Name == "MyTraceListener")
            ?.Methods.FirstOrDefault(m => m.Name == "TraceMessage")
            ?.Body;

        // The dialog block runs from the LogAssertsOnly check to the store
        // into the ignoreAll argument.
        var first = body?.Instructions.FirstOrDefault(i =>
            i.Operand is MethodReference { Name: "get_LogAssertsOnly" }
        );
        var last = first;
        while (last != null && last.OpCode.Code is not (Code.Starg or Code.Starg_S))
            last = last.Next;
        if (last == null)
        {
            Console.WriteLine(
                "[LinuxCompat] Preloader: MyTraceListener.TraceMessage changed upstream, release asserts still reach WinForms"
            );
            return false;
        }

        var il = body.GetILProcessor();
        while (first != last)
        {
            var next = first.Next;
            il.Remove(first);
            first = next;
        }
        il.Remove(last);

        // The DialogResult local stays at Ignore; an int has the same layout.
        foreach (var local in body.Variables)
            if (local.VariableType.Scope?.Name == "System.Windows.Forms")
                local.VariableType = asmDef.MainModule.TypeSystem.Int32;
        return true;
    }
}

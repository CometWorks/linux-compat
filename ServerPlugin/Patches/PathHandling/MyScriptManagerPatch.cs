using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ServerPlugin.Tools;
using HarmonyLib;
using Sandbox.Game.World;

namespace ServerPlugin.Patches.PathHandling;

// Ports Topic 4.8 (Mod script path splitting) from dotnet-game-local.
// MyScriptManager.LoadScripts splits file paths on hardcoded '\\'. On
// Linux the file enumerator returns forward-slash paths so the splits
// yield a single-element array and the method bails out with a
// "misplaced .cs files" warning -- no mod scripts compile.
//
// A transpiler replaces every ldc.i4.s 92 ('\\') in the method with
// ldc.i4.s 47 ('/'), which is the Linux DirectorySeparatorChar. The
// '.' (0x2E) split used to extract the file extension is unaffected.
[HarmonyPatch(typeof(MyScriptManager))]
[HarmonyPatchCategory("Finish")]
// ReSharper disable once UnusedType.Global
static class MyScriptManagerLoadScriptsPatch
{
    // ReSharper disable once UnusedMember.Local
    [HarmonyTranspiler]
    [HarmonyPatch("LoadScripts")]
    static IEnumerable<CodeInstruction> LoadScriptsTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase patchedMethod)
    {
        // Record original IL next to this patch file for review/diffing
        // across game updates. See ServerPlugin/Tools/TranspilerHelpers.cs.
        var il = instructions.ToList();
        il.RecordOriginalCode(patchedMethod);

        // Rewrite every ldc.i4.s 92 ('\\') and ldc.i4 92 to the platform
        // DirectorySeparatorChar ('/' on Linux). Mutating operands in
        // place preserves any branch labels and exception blocks attached
        // to the original instructions.
        var sep = Path.DirectorySeparatorChar;
        foreach (var instr in il)
        {
            if (instr.opcode == OpCodes.Ldc_I4_S && instr.operand is sbyte sb && sb == (sbyte)'\\')
            {
                instr.operand = (sbyte)sep;
            }
            else if (instr.opcode == OpCodes.Ldc_I4 && instr.operand is int i && i == '\\')
            {
                instr.operand = (int)sep;
            }
        }

        // Record modified IL next to the original for side-by-side diffing.
        il.RecordPatchedCode(patchedMethod);
        return il;
    }
}

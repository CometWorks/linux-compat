using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ClientPlugin.Tools;
using HarmonyLib;
using Sandbox.Game.World;

namespace ClientPlugin.Patches.PathHandling;

// Platform separators and computed prefix depth keep sibling Linux mod scripts in one assembly.
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
        var il = instructions.ToList();
        il.RecordOriginalCode(patchedMethod);

        // Mutating operands preserves attached labels and exception blocks.
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

        // Match constructed Array.IndexOf<string> by name and declaring type;
        // MethodInfo identity differs from open and non-generic overloads.
        int strIdx = -1;
        for (int k = 1; k < il.Count - 3; k++)
        {
            if (il[k].opcode == OpCodes.Ldstr && (il[k].operand as string) == "Scripts"
                && il[k + 1].opcode == OpCodes.Call
                && il[k + 1].operand is MethodInfo mi
                && mi.Name == nameof(Array.IndexOf) && mi.DeclaringType == typeof(Array)
                && il[k + 2].opcode == OpCodes.Ldc_I4_1
                && il[k + 3].opcode == OpCodes.Add
                && IsLdloc(il[k - 1].opcode))
            {
                strIdx = k;
                break;
            }
        }
        if (strIdx < 0)
            throw new InvalidOperationException(
                "MyScriptManagerLoadScriptsPatch: Could not find the Array.IndexOf(\"Scripts\") + 1 pattern.");

        int patIdx = strIdx - 1;

        // Locate the local assigned from text.Split(sep).Length.
        object numLocal = null;
        for (int k = 2; k < patIdx; k++)
        {
            if ((il[k].opcode == OpCodes.Stloc_S || il[k].opcode == OpCodes.Stloc)
                && il[k - 1].opcode == OpCodes.Conv_I4
                && il[k - 2].opcode == OpCodes.Ldlen)
            {
                numLocal = il[k].operand;
                break;
            }
        }
        if (numLocal == null)
            throw new InvalidOperationException(
                "MyScriptManagerLoadScriptsPatch: Could not locate the `num` local (ldlen; conv.i4; stloc).");

        // Preserve labels and exception blocks from the replaced instruction range.
        var replacement = new CodeInstruction(OpCodes.Ldloc_S, numLocal)
        {
            labels = il[patIdx].labels,
            blocks = il[patIdx].blocks
        };
        il.RemoveRange(patIdx, 5);
        il.Insert(patIdx, replacement);

        il.RecordPatchedCode(patchedMethod);
        return il;
    }

    static bool IsLdloc(OpCode opcode)
    {
        return opcode == OpCodes.Ldloc || opcode == OpCodes.Ldloc_S
            || opcode == OpCodes.Ldloc_0 || opcode == OpCodes.Ldloc_1
            || opcode == OpCodes.Ldloc_2 || opcode == OpCodes.Ldloc_3;
    }
}

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

// Ports Topic 4.8 (Mod script path splitting) from dotnet-game-local.
// MyScriptManager.LoadScripts has two Linux-incompatible quirks that must
// be fixed together for mod-script assemblies to compile correctly.
//
// Stage 1 -- backslash path splits
// --------------------------------
// The stock method splits file paths on the hardcoded '\\' character.
// On Linux the file enumerator returns forward-slash paths, so the
// splits yield single-element arrays and the method bails out with a
// "misplaced .cs files" warning -- no mod scripts compile. We rewrite
// every ldc.i4.s 92 ('\\') and ldc.i4 92 to the platform
// DirectorySeparatorChar ('/' on Linux). The '.' (0x2E) split used to
// extract the file extension is unaffected.
//
// Stage 2 -- case-sensitive IndexOf("Scripts") collides with PathCache
// --------------------------------------------------------------------
// After Stage 1 the per-file batching loop computes
//     num2 = Array.IndexOf(array2, "Scripts") + 1;
// to find the index of the directory segment immediately following the
// mod's Data/Scripts directory in each file path. The lookup is
// case-sensitive. MyFileSystem.GetFiles is reached with the literal
// "Data/Scripts" produced by Path.Combine, but the case-insensitive
// PathCache resolver in MyFileSystemPatch returns paths whose case
// matches the actual on-disk directory. Many Steam workshop mods ship
// lowercase "data/scripts" directories (e.g. NexusSyncMod 2272613450),
// so Array.IndexOf returns -1, num2 collapses to 0, and the flush
// branch (Compile + list.Clear) fires for every file -- each .cs ends
// up in its own assembly and cross-file type references (e.g.
// Pad.cs referencing SpawnPad/RespawnScreen/GateVisuals/NexusAPI from
// sibling files) fail to resolve.
//
// `num` is already computed once outside the loop from
// text.Split(sep).Length and is semantically equal to
// Array.IndexOf(array2, "Scripts") + 1 whenever the file path prefix
// matches the directory prefix. We substitute the 5-instruction
// IndexOf computation with a single ldloc of the `num` local. This
// removes the case sensitivity and an unnecessary string search on
// every iteration.
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
        // across game updates. See ClientPlugin/Tools/TranspilerHelpers.cs.
        var il = instructions.ToList();
        il.RecordOriginalCode(patchedMethod);

        // --- Stage 1: rewrite '\\' constants to the platform separator.
        // Mutating operands in place preserves any branch labels and
        // exception blocks attached to the original instructions.
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

        // --- Stage 2: replace Array.IndexOf(array2, "Scripts") + 1 with ldloc(num).
        //
        // Anchor on the unique pair  ldstr "Scripts" ; call <Array.IndexOf> .
        // Array.IndexOf(string[], string) is the *constructed generic*
        // Array.IndexOf<string>, so we match by method name + declaring type
        // rather than comparing MethodInfo handles (a resolved open generic
        // or the non-generic (Array, object) overload would not compare
        // equal). The method also contains an unrelated ldstr "Scripts" used
        // as a Path.Combine argument near the top, which is not followed by
        // an IndexOf call, so the anchor stays unique. Full sequence:
        //   ldloc array2 ; ldstr "Scripts" ; call IndexOf ; ldc.i4.1 ; add
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

        // patIdx points at the array2 load, the first of the 5 instructions.
        int patIdx = strIdx - 1;

        // Locate `num`: the local assigned from text.Split(sep).Length,
        // i.e. the first stloc preceded by ldlen; conv.i4 in the method.
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

        // Replace the 5 instructions with a single ldloc(num), carrying
        // over any labels/exception blocks attached to the first one.
        var replacement = new CodeInstruction(OpCodes.Ldloc_S, numLocal)
        {
            labels = il[patIdx].labels,
            blocks = il[patIdx].blocks
        };
        il.RemoveRange(patIdx, 5);
        il.Insert(patIdx, replacement);

        // Record modified IL next to the original for side-by-side diffing.
        il.RecordPatchedCode(patchedMethod);
        return il;
    }

    // True for any opcode that loads a local onto the stack (long and short
    // forms, plus the ldloc.0..3 macros).
    static bool IsLdloc(OpCode opcode)
    {
        return opcode == OpCodes.Ldloc || opcode == OpCodes.Ldloc_S
            || opcode == OpCodes.Ldloc_0 || opcode == OpCodes.Ldloc_1
            || opcode == OpCodes.Ldloc_2 || opcode == OpCodes.Ldloc_3;
    }
}

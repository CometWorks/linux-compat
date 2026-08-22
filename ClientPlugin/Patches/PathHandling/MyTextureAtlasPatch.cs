using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ClientPlugin.Tools;
using HarmonyLib;
using VRage.Render11.Resources;

namespace ClientPlugin.Patches.PathHandling;

// Atlas manifests contain Windows paths. Normalize only the GetFileName call;
// keep material texture keys in Windows form for mod compatibility.
[HarmonyPatch(typeof(MyTextureAtlas))]
[HarmonyPatchCategory("Finish")]
// ReSharper disable once UnusedType.Global
static class MyTextureAtlasParseAtlasDescriptionPatch
{
    // ReSharper disable once UnusedMember.Local
    [HarmonyTranspiler]
    [HarmonyPatch("ParseAtlasDescription")]
    static IEnumerable<CodeInstruction> ParseAtlasDescriptionTranspiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase patchedMethod
    )
    {
        var il = instructions.ToList();
        il.RecordOriginalCode(patchedMethod);

        var target = typeof(Path).GetMethod(nameof(Path.GetFileName), new[] { typeof(string) });
        var replacement = typeof(PathHelpers).GetMethod(
            nameof(PathHelpers.GetFileName),
            new[] { typeof(string) }
        );

        // Mutate the operand in place so any branch labels or exception
        // blocks attached to the call instruction stay anchored to it.
        foreach (var instr in il)
        {
            if (instr.opcode == OpCodes.Call && instr.operand is MethodInfo mi && mi == target)
                instr.operand = replacement;
        }

        il.RecordPatchedCode(patchedMethod);
        return il;
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ClientPlugin.Tools;
using HarmonyLib;
using Sandbox.Game.GUI;
using Sandbox.Game.Gui;

namespace ClientPlugin.Patches.PathHandling;

// Normalize STEAM_THUMBNAIL_NAME before direct File.Copy access on Linux.
[HarmonyPatch(typeof(MyGuiBlueprintScreen_Reworked), "CreateScriptFromEditor")]
[HarmonyPatchCategory("Finish")]
// ReSharper disable once UnusedType.Global
static class MyGuiBlueprintScreenCreateScriptPatch
{
    // ReSharper disable once UnusedMember.Local
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase patchedMethod
    )
    {
        var il = instructions.ToList();
        il.RecordOriginalCode(patchedMethod);

        var thumbnailField = AccessTools.Field(
            typeof(MyBlueprintUtils),
            nameof(MyBlueprintUtils.STEAM_THUMBNAIL_NAME)
        );
        var normalize = AccessTools.Method(typeof(PathHelpers), nameof(PathHelpers.Normalize));

        // Walk backward so insertion does not shift pending indexes.
        for (var i = il.Count - 1; i >= 0; i--)
        {
            var instr = il[i];
            if (instr.opcode != OpCodes.Ldsfld)
                continue;
            if (instr.operand is not FieldInfo fi || fi != thumbnailField)
                continue;

            il.Insert(i + 1, new CodeInstruction(OpCodes.Call, normalize));
        }

        il.RecordPatchedCode(patchedMethod);
        return il;
    }
}

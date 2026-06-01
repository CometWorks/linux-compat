using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ClientPlugin.Tools;
using HarmonyLib;
using Sandbox.Game.GUI;
using Sandbox.Game.Gui;

namespace ClientPlugin.Patches.PathHandling;

// MyGuiBlueprintScreen_Reworked.CreateScriptFromEditor (line 3475) builds the
// thumbnail source path with:
//
//     Path.Combine(MyFileSystem.ContentPath, MyBlueprintUtils.STEAM_THUMBNAIL_NAME)
//
// where STEAM_THUMBNAIL_NAME is the static-readonly literal
// "Textures\\GUI\\Icons\\IngameProgrammingIcon.png". On Linux, Path.Combine
// preserves the embedded backslashes in the second argument, producing
// ".../Content/Textures\GUI\Icons\IngameProgrammingIcon.png". The result is
// then handed straight to File.Copy, which is direct .NET I/O and bypasses
// MyFileSystem (so MyFileSystemPatch's backslash normalization never runs).
// File.Copy throws FileNotFoundException, which propagates out of the input
// handler and crashes the game when the user clicks "Create new script from
// editor" in the Scripts dialog.
//
// Same family of bug as 33792d7 (visual scripting path split), be77fbb
// (TestFunctionLoad Guid parse), and 71fb329 (IsItem_* probes): a hardcoded
// backslash baked into a string that escapes the MyFileSystem layer.
//
// Transpiler insert a PathHelpers.Normalize(string) call immediately after
// every ldsfld of STEAM_THUMBNAIL_NAME in the method, so the value flowing
// into Path.Combine has forward slashes. Behaviour on Windows is unchanged
// (Normalize is a no-op when the input has no backslashes).
[HarmonyPatch(typeof(MyGuiBlueprintScreen_Reworked), "CreateScriptFromEditor")]
[HarmonyPatchCategory("Finish")]
// ReSharper disable once UnusedType.Global
static class MyGuiBlueprintScreenCreateScriptPatch
{
    // ReSharper disable once UnusedMember.Local
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase patchedMethod)
    {
        var il = instructions.ToList();
        il.RecordOriginalCode(patchedMethod);

        var thumbnailField = typeof(MyBlueprintUtils).GetField(nameof(MyBlueprintUtils.STEAM_THUMBNAIL_NAME),
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var normalize = typeof(PathHelpers).GetMethod(nameof(PathHelpers.Normalize));

        // Walk backwards so Insert calls don't shift the indexes still to visit.
        for (var i = il.Count - 1; i >= 0; i--)
        {
            var instr = il[i];
            if (instr.opcode != OpCodes.Ldsfld) continue;
            if (instr.operand is not FieldInfo fi || fi != thumbnailField) continue;

            il.Insert(i + 1, new CodeInstruction(OpCodes.Call, normalize));
        }

        il.RecordPatchedCode(patchedMethod);
        return il;
    }
}

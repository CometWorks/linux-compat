using System.IO;
using HarmonyLib;
using Sandbox.Game.GUI;

namespace ClientPlugin.Patches.PathHandling;

// MyBlueprintUtils.IsItem_Blueprint / IsItem_Script probe a child file by
// concatenating "\\bp.sbc" / "\\Script.cs" onto a directory path and calling
// System.IO.File.Exists directly — bypassing MyFileSystem, so the existing
// MyFileSystemPatch backslash normalization does not apply.
//
// On Linux these always return false: the probe path becomes a literal
// "<dir>\bp.sbc" filename which does not exist on disk. Visible effects:
//   - IsItem_Script (line 2780 of MyGuiBlueprintScreen_Reworked) drops every
//     local script from the script tab of the blueprint dialog.
//   - IsItem_Blueprint (passed to MyGuiFolderScreen as a folder filter at
//     line 1714) makes blueprint subfolders un-navigable in the folder
//     browser dialog.
//
// Replace both with Path.Combine so they probe "<dir>/bp.sbc" on Linux and
// "<dir>\bp.sbc" on Windows (semantics preserved), then resolve the child
// path case-insensitively for user-data paths such as local scripts.

[HarmonyPatch(typeof(MyBlueprintUtils), nameof(MyBlueprintUtils.IsItem_Blueprint))]
[HarmonyPatchCategory("Finish")]
static class MyBlueprintUtilsIsItemBlueprintPatch
{
    static bool Prefix(string path, ref bool __result)
    {
        var blueprintPath = Path.Combine(path, "bp.sbc");
        blueprintPath = PathCache.ResolveAbsolute(blueprintPath);

        __result = File.Exists(blueprintPath);
        return false;
    }
}

[HarmonyPatch(typeof(MyBlueprintUtils), nameof(MyBlueprintUtils.IsItem_Script))]
[HarmonyPatchCategory("Finish")]
static class MyBlueprintUtilsIsItemScriptPatch
{
    static bool Prefix(string path, ref bool __result)
    {
        var scriptPath = Path.Combine(path, "Script.cs");
        scriptPath = PathCache.ResolveAbsolute(scriptPath);

        __result = File.Exists(scriptPath);
        return false;
    }
}

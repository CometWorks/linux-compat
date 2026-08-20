using System.IO;
using HarmonyLib;
using Sandbox.Game.GUI;

namespace ClientPlugin.Patches.PathHandling;

// These direct File.Exists probes bypass MyFileSystem separator normalization.
// Resolve child paths case-insensitively so Linux finds scripts and blueprint folders.

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

using HarmonyLib;
using Sandbox.Game.Gui;

namespace ClientPlugin.Patches.PathHandling;

// MyGuiScreenEditor.ScriptSelected reads selected .cs files through direct
// System.IO calls. Local script selection passes a hardcoded Script.cs path, so
// resolve it before the method probes File.Exists / File.ReadAllText on Linux.
[HarmonyPatch(typeof(MyGuiScreenEditor), "ScriptSelected")]
[HarmonyPatchCategory("Finish")]
static class MyGuiScreenEditorScriptSelectedPatch
{
    static void Prefix(ref string scriptPath)
    {
        if (!string.IsNullOrEmpty(scriptPath))
            scriptPath = PathCache.ResolveAbsolute(scriptPath);
    }
}

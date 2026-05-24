using HarmonyLib;
using Sandbox.Game.SessionComponents;

namespace ServerPlugin.Patches.PathHandling;

// MyVisualScriptManagerSessionComponent has two methods that decompose
// hierarchical paths by hardcoded backslash:
//
//   BeforeStart (line 199-202): for level-script files in scenarios it does
//       text = text2.Substring("Scenarios\\".Length);
//       text = text[..text.IndexOf('\\')];
//     If LevelScriptFiles entries arrived with forward slashes (Linux saves
//     produced via Path.Combine), IndexOf('\\') returns -1 and the range
//     expression throws ArgumentOutOfRangeException — BeforeStart aborts and
//     no level scripts run.
//
//   CreateFoldersFromPath (line 855-865): walks LastIndexOf('\\') to expand
//     a virtual outline path "A\B\C" into {A, A\B, A\B\C} for the visual
//     scripting world-outline UI. With forward-slash input the loop exits
//     immediately and only the leaf is added.
//
// The original code was designed around backslash-delimited strings (the
// historical Windows-side serialization convention). Normalize forward
// slashes back to backslashes at the entry points; the original code then
// works unchanged regardless of which separator the saved data uses.

[HarmonyPatch(typeof(MyVisualScriptManagerSessionComponent), "BeforeStart")]
[HarmonyPatchCategory("Finish")]
static class MyVisualScriptManagerBeforeStartPatch
{
    static void Prefix(MyVisualScriptManagerSessionComponent __instance)
    {
        var ob = __instance.m_objectBuilder;
        if (ob == null)
            return;

        Normalize(ob.LevelScriptFiles);
        Normalize(ob.StateMachines);
    }

    static void Normalize(string[] arr)
    {
        if (arr == null)
            return;

        for (int i = 0; i < arr.Length; i++)
        {
            var s = arr[i];
            if (s != null && s.IndexOf('/') >= 0)
                arr[i] = s.Replace('/', '\\');
        }
    }
}

[HarmonyPatch(typeof(MyVisualScriptManagerSessionComponent), "CreateFoldersFromPath")]
[HarmonyPatchCategory("Finish")]
static class MyVisualScriptManagerCreateFoldersFromPathPatch
{
    static void Prefix(ref string path)
    {
        if (path != null && path.IndexOf('/') >= 0)
            path = path.Replace('/', '\\');
    }
}

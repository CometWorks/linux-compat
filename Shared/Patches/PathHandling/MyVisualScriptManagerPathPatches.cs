using HarmonyLib;
using Sandbox.Game.SessionComponents;

namespace ClientPlugin.Patches.PathHandling;

// Visual-script parsing requires backslashes even when Linux saves contain forward slashes.

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

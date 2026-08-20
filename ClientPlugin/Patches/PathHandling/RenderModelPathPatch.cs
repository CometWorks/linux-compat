using HarmonyLib;
using VRage.Render11.GeometryStage2.Instancing;
using VRage.Render11.GeometryStage2.Model;
using VRage.Render11.GeometryStage2.StaticGroup;

namespace ClientPlugin.Patches.PathHandling;

// Canonical paths keep renderer instance reloads and model data string-identical.
static class RenderModelPathCanonicalizer
{
    public static void Canonicalize(ref string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        if (
            path.Length >= 2
            && path[1] == ':'
            && ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'))
        )
        {
            path = PathTranslation.Untranslate(path.Replace('\\', '/'));
        }
    }
}

[HarmonyPatch(typeof(MyModelFactory), nameof(MyModelFactory.GetOrCreateModels))]
[HarmonyPatchCategory("Finish")]
static class MyModelFactoryGetOrCreateModelsPatch
{
    static void Prefix(ref string filepath)
    {
        RenderModelPathCanonicalizer.Canonicalize(ref filepath);
    }
}

// Reload matching requires canonical instance model paths.
[HarmonyPatch(typeof(MyInstanceComponent), nameof(MyInstanceComponent.Init))]
[HarmonyPatchCategory("Finish")]
static class MyInstanceComponentInitPatch
{
    static void Prefix(ref string modelFilepath)
    {
        RenderModelPathCanonicalizer.Canonicalize(ref modelFilepath);
    }
}

// Static-group reload matching has the same path requirement.
[HarmonyPatch(typeof(MyStaticGroupComponent), nameof(MyStaticGroupComponent.Init))]
[HarmonyPatchCategory("Finish")]
static class MyStaticGroupComponentInitPatch
{
    static void Prefix(ref string modelFilepath)
    {
        RenderModelPathCanonicalizer.Canonicalize(ref modelFilepath);
    }
}

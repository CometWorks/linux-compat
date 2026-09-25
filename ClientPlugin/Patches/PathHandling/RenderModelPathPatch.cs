using HarmonyLib;
using VRage.Render11.GeometryStage2.Instancing;
using VRage.Render11.GeometryStage2.Model;
using VRage.Render11.GeometryStage2.StaticGroup;

namespace ClientPlugin.Patches.PathHandling;

// Canonical paths keep renderer instance reloads and model data string-identical.
//
// MyModelFactory dedups models by MyMwmUtils.GetFullMwmFilepath, which on Linux
// resolves separators and casing against the disk, but the dummy-to-real swap after an
// async load matches instances by the exact string the load was started with. Two
// spellings of one file then share a factory entry while only instances of the first
// spelling get the real model; the rest keep the invisible loading dummy forever.
// Mapping every ingress path to the factory's own key makes both strings equal.
//
// Seen with the vicinity preload from a Windows server: it sends the models of fat
// blocks near the player as "Models\Cubes\...", while mod definitions that reuse a
// vanilla model hold the normalized "Models/Cubes/..." on this client. Blocks created
// before the preload finishes stay invisible.
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

        path = MyMwmUtils.GetFullMwmFilepath(path);
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

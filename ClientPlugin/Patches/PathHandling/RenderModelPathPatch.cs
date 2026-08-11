using HarmonyLib;
using VRage.Render11.GeometryStage2.Instancing;
using VRage.Render11.GeometryStage2.Model;
using VRage.Render11.GeometryStage2.StaticGroup;

namespace ClientPlugin.Patches.PathHandling;

// A mod that composes an absolute model path off ModContext.ModPath (e.g.
// Light Block Improvements assigning `def.Model = Path.Combine(
// ModContext.ModPath, "Models\\Foo.mwm")`) stores a Windows-shape,
// drive-prefixed string in the block definition. That string reaches the
// new render pipeline through TWO different ingress routes with TWO
// different shapes for the same model file:
//
//   - preload / cube-builder ghost: the raw def.Model string
//     ("C:\\users\\steamuser\\...\\Foo.mwm"), and
//   - placed-entity CreateRenderEntity: MyModel.AssetName, which the
//     MyModelConstructorPatch ingress already untranslated to the native
//     path ("/home/<user>/.../Foo.mwm").
//
// MyModelFactory dedups models by the resolved FullFilePath (the patched
// MyMwmUtils.GetFullMwmFilepath handles both shapes), so the model itself
// loads fine. But the async dummy->real swap in
// MyInstanceManager.OnReloadModels / MyStaticGroupManager.OnReloadModels
// matches instances by EXACT string equality:
//
//   item.ModelFilepath == modelData.FilePath
//
// where ModelData.FilePath is whatever raw string first requested the
// model and instance.ModelFilepath is whatever raw string created the
// instance. When the two ingress routes disagree ("C:\\..." vs
// "/home/..."), the swap silently misses and the instance keeps the
// invisible dummy model forever -- the block renders nothing while its
// game-side logic (light, terminal) works normally. On Windows this is
// structurally impossible because one canonical def.Model string flows
// through every route.
//
// Fix: canonicalize the model path at the renderer's string-identity
// ingress points, so every exact-string comparison sees one shape. Only
// drive-prefixed inputs are touched -- vanilla relative asset names
// ("Models\\Cubes\\...") and native absolute paths pass through with the
// same reference, so engine- and plugin-originated calls are unaffected.
// Mirrors the ingress untranslation MyModelConstructorPatch does for the
// game-side MyModel.
static class RenderModelPathCanonicalizer
{
    public static void Canonicalize(ref string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        // Drive-prefix detection: single letter [A-Za-z] followed by ':'.
        if (path.Length >= 2 && path[1] == ':' &&
            ((path[0] >= 'A' && path[0] <= 'Z') ||
             (path[0] >= 'a' && path[0] <= 'z')))
        {
            path = PathTranslation.Untranslate(path.Replace('\\', '/'));
        }
    }
}

// Covers every model request entering the new pipeline's factory: preload
// messages, CreateRenderEntity, CreateStaticGroup. Makes ModelData.FilePath
// (the string OnReloadModels later matches against) canonical.
[HarmonyPatch(typeof(MyModelFactory), nameof(MyModelFactory.GetOrCreateModels))]
[HarmonyPatchCategory("Finish")]
static class MyModelFactoryGetOrCreateModelsPatch
{
    static void Prefix(ref string filepath)
    {
        RenderModelPathCanonicalizer.Canonicalize(ref filepath);
    }
}

// Makes instance.ModelFilepath canonical so the dummy->real model swap in
// MyInstanceManager.OnReloadModels matches regardless of which ingress
// route created the instance.
[HarmonyPatch(typeof(MyInstanceComponent), nameof(MyInstanceComponent.Init))]
[HarmonyPatchCategory("Finish")]
static class MyInstanceComponentInitPatch
{
    static void Prefix(ref string modelFilepath)
    {
        RenderModelPathCanonicalizer.Canonicalize(ref modelFilepath);
    }
}

// Same for grid static groups (MyStaticGroupManager.OnReloadModels).
[HarmonyPatch(typeof(MyStaticGroupComponent), nameof(MyStaticGroupComponent.Init))]
[HarmonyPatchCategory("Finish")]
static class MyStaticGroupComponentInitPatch
{
    static void Prefix(ref string modelFilepath)
    {
        RenderModelPathCanonicalizer.Canonicalize(ref modelFilepath);
    }
}

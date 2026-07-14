using HarmonyLib;
using VRage.Game.Models;

namespace ClientPlugin.Patches.PathHandling;

// A mod that builds an absolute model path off ModContext.ModPath (e.g. Light
// Block Improvements assigning `def.Model = Path.Combine(ModContext.ModPath,
// "Models\\Foo.mwm")`) stores a Windows-shape, drive-prefixed string
// ("C:\\users\\steamuser\\...\\Models\\Foo.mwm") in the block definition. That
// string flows into MyModel as the asset name.
//
// MyModel.LoadData / LoadOnlyModelInfo / LoadAnimationData each do their OWN
// `Path.IsPathRooted(AssetName) ? AssetName : Path.Combine(ContentPath, ...)`
// probe and, when MyFileSystem.FileExists fails, silently substitute
// "Models\\Debug\\Error.mwm" before importing. On Linux Path.IsPathRooted is
// false for a "C:\\..." path, so the probe joins it onto ContentPath, the file
// isn't found, and the block's real model (with its "light" dummy) is never
// imported -- the cached MyModel is marked loaded with the Error model's empty
// dummy set. MyLightingLogic then reads zero light dummies and creates no
// light, even though the render path (patched separately) still shows the mesh.
// Patching MyModelImporter.ImportData cannot help: the game already swapped in
// Error.mwm before ImportData is reached.
//
// Fix at the ingress: untranslate the drive prefix back to the native Linux
// path when MyModel is constructed, so every downstream IsPathRooted/FileExists
// check and importer call sees a real, rooted path. Only drive-prefixed inputs
// are touched; native and relative asset names pass through byte-identical, so
// the model cache key (MyModel.GetId, computed from the caller's original
// string) is unaffected.
[HarmonyPatch(typeof(MyModel), MethodType.Constructor, new[] { typeof(string), typeof(bool) })]
[HarmonyPatchCategory("Finish")]
static class MyModelConstructorPatch
{
    static void Prefix(ref string assetName)
    {
        if (string.IsNullOrEmpty(assetName))
            return;

        // Drive-prefix detection: single letter [A-Za-z] followed by ':'.
        if (assetName.Length >= 2 && assetName[1] == ':' &&
            ((assetName[0] >= 'A' && assetName[0] <= 'Z') ||
             (assetName[0] >= 'a' && assetName[0] <= 'z')))
        {
            assetName = PathTranslation.Untranslate(assetName.Replace('\\', '/'));
        }
    }
}

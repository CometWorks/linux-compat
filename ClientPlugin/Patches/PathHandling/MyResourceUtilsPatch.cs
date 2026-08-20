using System;
using HarmonyLib;
using VRage.Render11.Resources;

namespace ClientPlugin.Patches.PathHandling;

// Restore absolute mod icon paths before URI normalization joins them to ContentPath.
[HarmonyPatch(
    typeof(MyResourceUtils),
    nameof(MyResourceUtils.NormalizeFileTextureName),
    new[] { typeof(string), typeof(Uri) },
    new[] { ArgumentType.Ref, ArgumentType.Out }
)]
[HarmonyPatchCategory("Finish")]
static class MyResourceUtilsNormalizeFileTextureNamePatch
{
    static void Prefix(ref string name)
    {
        if (string.IsNullOrEmpty(name))
            return;

        // Preserve relative texture names so the engine keeps its normal cache keys.
        var forward = name.Replace('\\', '/');
        var untranslated = PathTranslation.Untranslate(forward);
        if (!ReferenceEquals(untranslated, forward))
            name = untranslated;
    }
}

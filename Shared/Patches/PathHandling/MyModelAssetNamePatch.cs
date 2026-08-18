using HarmonyLib;
using VRage.Game.Models;

namespace ClientPlugin.Patches.PathHandling;

// Restore absolute mod model paths before Linux rooted-path checks can substitute Error.mwm.
[HarmonyPatch(typeof(MyModel), MethodType.Constructor, new[] { typeof(string), typeof(bool) })]
[HarmonyPatchCategory("Finish")]
static class MyModelConstructorPatch
{
    static void Prefix(ref string assetName)
    {
        if (string.IsNullOrEmpty(assetName))
            return;

        if (assetName.Length >= 2 && assetName[1] == ':' &&
            ((assetName[0] >= 'A' && assetName[0] <= 'Z') ||
             (assetName[0] >= 'a' && assetName[0] <= 'z')))
        {
            assetName = PathTranslation.Untranslate(assetName.Replace('\\', '/'));
        }
    }
}

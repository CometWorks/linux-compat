using HarmonyLib;
using VRageRender;

namespace ClientPlugin.Patches.NullSafety;

[HarmonyPatch(
    typeof(MyShadowCascades),
    nameof(MyShadowCascades.CascadeResolution),
    MethodType.Getter
)]
[HarmonyPatchCategory("Finish")]
static class MyShadowCascadesCascadeResolutionPatch
{
    static bool Prefix(MyShadowCascades __instance, ref int __result)
    {
        if (__instance.CascadeShadowmapArray == null)
        {
            __result = 0;
            return false;
        }
        return true;
    }
}

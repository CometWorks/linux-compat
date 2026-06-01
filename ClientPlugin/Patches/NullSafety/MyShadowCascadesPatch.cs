using HarmonyLib;
using VRageRender;

namespace ClientPlugin.Patches.NullSafety;

[HarmonyPatch(typeof(MyShadowCascades), "get_CascadeResolution")]
[HarmonyPatchCategory("Finish")]
static class MyShadowCascadesCascadeResolutionPatch
{
    static bool Prefix(MyShadowCascades __instance, ref int __result)
    {
        if (__instance.m_cascadeShadowmapArray == null)
        {
            __result = 0;
            return false;
        }
        return true;
    }
}

using System;
using HarmonyLib;
using VRage.Ansel;
using VRage.Platform.Windows;

namespace ServerPlugin.Patches.PlatformGuards;

[HarmonyPatch(typeof(MyVRagePlatform), "Init")]
[HarmonyPatchCategory("Finish")]
static class MyVRagePlatformInitPatch
{
    static void Postfix(MyVRagePlatform __instance)
    {
        var typeModelType = AccessTools.TypeByName("VRage.Platform.Windows.Serialization.DynamicTypeModel");
        if (typeModelType != null)
        {
            var typeModel = Activator.CreateInstance(typeModelType);
            var field = AccessTools.Field(typeof(MyVRagePlatform), "m_typeModel");
            field?.SetValue(__instance, typeModel);
        }

        var anselProp = AccessTools.Property(typeof(MyVRagePlatform), "Ansel");
        anselProp?.SetValue(__instance, new MyAnsel());
    }
}

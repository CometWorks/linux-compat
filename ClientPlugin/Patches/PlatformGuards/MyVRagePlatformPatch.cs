using System;
using HarmonyLib;
using VRage.Ansel;
using VRage.Platform.Windows;
using VRage.Platform.Windows.Serialization;

namespace ClientPlugin.Patches.PlatformGuards;

[HarmonyPatch(typeof(MyVRagePlatform), "Init")]
[HarmonyPatchCategory("Finish")]
static class MyVRagePlatformInitPatch
{
    static void Postfix(MyVRagePlatform __instance)
    {
        __instance.m_typeModel = new DynamicTypeModel();
        __instance.Ansel = new MyAnsel();
    }
}

using System;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.World;
using VRage.Game;
using VRage.Game.Components;

namespace ClientPlugin.Patches.NullSafety;

[HarmonyPatch]
[HarmonyPatchCategory("Init")]
static class SessionComponentRegistrationPatch
{
    static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(MySession), "TryRegisterSessionComponent");
    }

    static bool Prefix(MySession __instance, Type type, bool modAssembly, MyModContext context)
    {
        try
        {
            MyDefinitionId? definition = null;
            var component = (MySessionComponentBase)Activator.CreateInstance(type);

            var isRequiredByGame = component.IsRequiredByGame;
            MyDefinitionId? infoDefinition = null;
            var hasInfo = __instance.GetComponentInfo(type, out infoDefinition);
            definition = infoDefinition;

            if (isRequiredByGame || modAssembly || hasInfo)
            {
                __instance.RegisterComponent(component, component.UpdateOrder, component.Priority);
                __instance.GetComponentInfo(type, out infoDefinition);
                definition = infoDefinition;
                component.Definition = definition;
                component.ModContext = context;
            }
        }
        catch (Exception ex)
        {
            VRage.Utils.MyLog.Default.WriteLine($"Exception during loading of type : {type.Name}");
            VRage.Utils.MyLog.Default.WriteLine($"  Detail: {ex}");
        }
        return false;
    }
}

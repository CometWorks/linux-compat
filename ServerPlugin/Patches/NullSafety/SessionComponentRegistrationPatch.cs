using System;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.World;
using VRage.Game;
using VRage.Game.Components;

namespace ServerPlugin.Patches.NullSafety;

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
            var getComponentInfo = AccessTools.Method(typeof(MySession), "GetComponentInfo");
            var args = new object[] { type, null };
            var hasInfo = (bool)getComponentInfo.Invoke(__instance, args);
            definition = (MyDefinitionId?)args[1];

            if (isRequiredByGame || modAssembly || hasInfo)
            {
                __instance.RegisterComponent(component, component.UpdateOrder, component.Priority);
                getComponentInfo.Invoke(__instance, args);
                definition = (MyDefinitionId?)args[1];
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

using System;
using HarmonyLib;
using Sandbox.Game.World;
using VRage.Game;
using VRage.Game.Components;

namespace ClientPlugin.Patches.NullSafety;

[HarmonyPatch(
    typeof(MySession),
    nameof(MySession.TryRegisterSessionComponent),
    typeof(Type),
    typeof(bool),
    typeof(MyModContext)
)]
[HarmonyPatchCategory("Init")]
static class SessionComponentRegistrationPatch
{
    static bool Prefix(MySession __instance, Type type, bool modAssembly, MyModContext context)
    {
        try
        {
            var component = (MySessionComponentBase)Activator.CreateInstance(type);

            var isRequiredByGame = component.IsRequiredByGame;
            var hasInfo = __instance.GetComponentInfo(type, out MyDefinitionId? definition);

            if (isRequiredByGame || modAssembly || hasInfo)
            {
                __instance.RegisterComponent(component, component.UpdateOrder, component.Priority);
                __instance.GetComponentInfo(type, out definition);
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

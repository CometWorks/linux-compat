using ClientPlugin.Compatibility;
using HarmonyLib;
using Sandbox;
using Sandbox.AppCode;
using Sandbox.Game.World;
using VRage.Input;

namespace ClientPlugin.Patches.WindowManagement;

// Recenter after leaving SDL relative mode; SDL ignores warps while it is active.
[HarmonyPatch(typeof(MySandboxGame), nameof(MySandboxGame.SetMouseVisible))]
[HarmonyPatchCategory("Finish")]
static class MySandboxGameSetMouseVisiblePatch
{
    static void Postfix(MySandboxGame __instance, bool visible, bool __state)
    {
        if (!RenderingConfig.AllowRendering || !__state || MyExternalAppBase.IsEditorActive)
            return;

        var areaSize = MyInput.Static.GetMouseAreaSize();
        MyInput.Static.SetMousePosition((int)(areaSize.X / 2f), (int)(areaSize.Y / 2f));
    }

    static bool Prefix(MySandboxGame __instance, bool visible, out bool __state)
    {
        if (!RenderingConfig.AllowRendering)
        {
            __state = false;
            return false;
        }

        __state =
            visible
            && !__instance.IsCursorVisible
            && MySession.Static?.ControlledEntity != null
            && !MyExternalAppBase.IsEditorActive;
        return true;
    }
}

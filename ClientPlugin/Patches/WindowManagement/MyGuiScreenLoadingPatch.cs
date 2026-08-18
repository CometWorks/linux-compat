using HarmonyLib;
using Sandbox;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using VRage;

namespace ClientPlugin.Patches.WindowManagement;

// Keep the cursor available during windowed loading screens.
[HarmonyPatch(typeof(MyGuiScreenLoading), MethodType.Constructor,
    typeof(MyGuiScreenBase), typeof(MyGuiScreenGamePlay), typeof(string), typeof(string))]
[HarmonyPatchCategory("Finish")]
static class MyGuiScreenLoadingConstructorPatch
{
    static void Postfix(MyGuiScreenLoading __instance)
    {
        var config = MySandboxGame.Config;
        if (config == null)
            return;

        bool showCursor = config.WindowMode != MyWindowModeEnum.Fullscreen;
        AccessTools.PropertySetter(typeof(MyGuiScreenBase), "DrawMouseCursor")
            ?.Invoke(__instance, [showCursor]);
        MyGuiSandbox.SetMouseCursorVisibility(showCursor);
    }
}

using HarmonyLib;
using Sandbox;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using VRage;

namespace ClientPlugin.Patches.WindowManagement;

// During loading screens the original code hides the cursor. In windowed modes
// on Linux this traps the user - they cannot switch back to other apps while
// the game is loading. Show the cursor in any non-fullscreen mode, matching
// the recompiled game. Fullscreen hides it for immersion.
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
        __instance.DrawMouseCursor = showCursor;
        MyGuiSandbox.SetMouseCursorVisibility(showCursor);
    }
}

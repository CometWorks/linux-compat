using HarmonyLib;
using Sandbox;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage;

namespace ClientPlugin.Patches.WindowManagement;

// Show the cursor during windowed gameplay states with no controlled entity.
// Hide it during camera control so SDL relative mode captures the pointer.
[HarmonyPatch(typeof(MyGuiScreenGamePlay), nameof(MyGuiScreenGamePlay.Update))]
[HarmonyPatchCategory("Finish")]
static class MyGuiScreenGamePlayUpdateCursorPatch
{
    static void Postfix(MyGuiScreenGamePlay __instance)
    {
        var config = MySandboxGame.Config;
        if (config == null)
            return;

        bool hasControl = MySession.Static?.ControlledEntity != null;
        bool wantCursor = !hasControl && config.WindowMode != MyWindowModeEnum.Fullscreen;

        if (__instance.GetDrawMouseCursor() == wantCursor)
            return;

        __instance.DrawMouseCursor = wantCursor;
    }
}

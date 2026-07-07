using ClientPlugin.Compatibility;
using HarmonyLib;
using Sandbox;
using Sandbox.AppCode;
using Sandbox.Game.World;
using VRage.Input;

namespace ClientPlugin.Patches.WindowManagement;

// When the game transitions the cursor from hidden -> visible (e.g. Escape out
// of gameplay to the menu), re-center the cursor in the window.
//
// On Linux the cursor was sitting at the window edge where the user last moved
// it while in relative mouse mode, so without centering the menu cursor
// appears in a random location - often outside the clickable area. Centering
// must happen AFTER ShowCursor=true because that disables SDL relative mouse
// mode; warping while in relative mode is ignored by SDL.
[HarmonyPatch(typeof(MySandboxGame), nameof(MySandboxGame.SetMouseVisible))]
[HarmonyPatchCategory("Finish")]
static class MySandboxGameSetMouseVisiblePatch
{
    static void Postfix(MySandboxGame __instance, bool visible, bool __state)
    {
        // __state captured in prefix: whether we should center the cursor
        if (CommandLineFlags.Headless || !__state || MyExternalAppBase.IsEditorActive)
            return;

        var areaSize = MyInput.Static.GetMouseAreaSize();
        MyInput.Static.SetMousePosition((int)(areaSize.X / 2f), (int)(areaSize.Y / 2f));
    }

    static bool Prefix(MySandboxGame __instance, bool visible, out bool __state)
    {
        if (CommandLineFlags.Headless)
        {
            __state = false;
            return false;
        }

        __state = visible
            && !__instance.IsCursorVisible
            && MySession.Static?.ControlledEntity != null
            && !MyExternalAppBase.IsEditorActive;
        return true;
    }
}

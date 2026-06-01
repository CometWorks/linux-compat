using HarmonyLib;
using Sandbox;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage;

namespace ClientPlugin.Patches.WindowManagement;

// Companion to MyGuiScreenLoadingConstructorPatch (cursor visibility during
// world loading). During gameplay there are transitional states where
// MySession.Static.ControlledEntity is null - between respawn and character
// assignment, or during session init before the character is created. In
// windowed/borderless modes the cursor must become visible during these
// windows so the user can interact with the desktop.
//
// When the player is controlling an entity, DrawMouseCursor is forced back
// to false. The MyDX9Gui draw loop then calls SetMouseCursorVisibility(false),
// which engages SDL relative-mouse-mode in SdlGameWindow.UpdateMouseMode -
// that is what captures the pointer inside the window so it cannot leak onto
// other displays during camera control.
//
// In exclusive Fullscreen mode the cursor stays hidden regardless: there is
// no other application to switch to, and showing the cursor on a transient
// null-controlled-entity frame would just flash a pointer on screen.
//
// Update() runs ~60 Hz, but the setter is invoked only when the state
// actually flips, so per-frame allocations are bounded to a handful per
// control transition.
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

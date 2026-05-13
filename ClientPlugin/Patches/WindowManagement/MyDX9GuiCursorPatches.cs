using HarmonyLib;
using Sandbox;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRage;
using VRage.Input;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Patches.WindowManagement;

// Port of the recompiled MyDX9Gui cursor changes:
//   * MouseCursorPosition uses GetRawMousePosition() (not the scaled path) so the
//     software cursor tracks the real pointer with one-frame latency.
//   * DrawMouseCursor uses real window-relative mouse coordinates in windowed
//     mode (CaptureMouse = false, Window mode) so the software cursor
//     disappears only once the real pointer has actually left the window, and forces
//     waitTillLoaded so the texture is ready before it is drawn.
//   * Draw() re-evaluates cursor visibility on Linux (CaptureMouse == false):
//     when a GUI needs the pointer, force visible=true (releases SDL relative
//     mouse mode so the cursor can leave the window); when no GUI needs it,
//     force visible=false (re-engages relative mouse mode for camera control).

[HarmonyPatch(typeof(MyDX9Gui), "get_MouseCursorPosition")]
[HarmonyPatchCategory("Finish")]
static class MyDX9GuiMouseCursorPositionPatch
{
    static bool Prefix(ref Vector2 __result)
    {
        __result = MyGuiManager.GetNormalizedMousePosition(
            MyInput.Static.GetRawMousePosition(),
            MyInput.Static.GetMouseAreaSize());
        return false;
    }
}

[HarmonyPatch(typeof(MyDX9Gui), "DrawMouseCursor")]
[HarmonyPatchCategory("Finish")]
static class MyDX9GuiDrawMouseCursorPatch
{
    static bool Prefix(MyDX9Gui __instance, string mouseCursorTexture)
    {
        Vector2 raw = MyInput.Static.GetRawMousePosition();
        Vector2 area = MyInput.Static.GetMouseAreaSize();
        bool shouldDrawCursor = true;

        // In windowed mode without mouse capture, hide the software cursor only
        // once the real pointer has actually left the window. The input layer
        // supplies genuine window-relative coordinates even outside the client area.
        var config = MySandboxGame.Config;
        if (config != null && config.CaptureMouse == false && config.WindowMode == MyWindowModeEnum.Window)
        {
            shouldDrawCursor = raw.X >= 0f
                && raw.Y >= 0f
                && raw.X < area.X
                && raw.Y < area.Y;
        }

        if (mouseCursorTexture != null && shouldDrawCursor)
        {
            // Record the texture string the render-thread patch
            // (CursorRenderRatePatch) uses to identify "the" cursor sprite
            // among all queued DrawSprite messages. Set before enqueueing so
            // a fast render thread can never see the message before the name.
            CursorRenderRateState.LastCursorTextureName = mouseCursorTexture;

            Vector2 normalizedSize = MyGuiManager.GetNormalizedSize(new Vector2(64f), 1f);
            MyGuiManager.DrawSpriteBatch(
                mouseCursorTexture,
                __instance.MouseCursorDrawPosition,
                normalizedSize,
                new Color(MyGuiConstants.MOUSE_CURSOR_COLOR),
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,
                useFullClientArea: false,
                waitTillLoaded: true,
                null,
                0f,
                0f,
                ignoreBounds: true);
        }

        return false;
    }
}

// On Linux IsHardwareCursorUsed() returns false, so the stock MyDX9Gui.Draw
// code keeps calling SetMouseCursorVisibility(false) every frame even while a
// GUI screen wants the pointer. That flips SDL into relative mouse mode for
// the duration of the draw pass, which pins the cursor to the window center
// (and ruins the software cursor that Draw then tries to render). The
// recompiled game rewrites Draw() to force visible=true when CaptureMouse is
// false and a GUI needs the cursor. We achieve the same effect by
// intercepting SetMouseCursorVisibility before the original body runs, so the
// relative-mouse-mode toggle never happens mid-frame.
[HarmonyPatch(typeof(MyDX9Gui), nameof(MyDX9Gui.SetMouseCursorVisibility))]
[HarmonyPatchCategory("Finish")]
static class MyDX9GuiSetMouseCursorVisibilityPatch
{
    static void Prefix(ref bool visible)
    {
        if (visible)
            return;
        var config = MySandboxGame.Config;
        if (config == null || config.CaptureMouse != false)
            return;

        var screenWithFocus = MyScreenManager.GetScreenWithFocus();
        bool guiNeedsCursor =
            (screenWithFocus != null && screenWithFocus.GetDrawMouseCursor())
            || (MyScreenManager.InputToNonFocusedScreens && MyScreenManager.GetScreensCount() > 1);

        if (guiNeedsCursor)
            visible = true;
    }
}

// Port of the gameplay-hide half of the recompiled commit a92039a0 "Fixed the
// mouse capture". MyDX9Gui.Draw's cursor block:
//
//   if (screenWithFocus wants cursor || InputToNonFocusedScreens branch)
//       SetMouseCursorVisibility(...)   // show branch
//   else if (flag2 && screenWithFocus != null)
//       SetMouseCursorVisibility(screenWithFocus.GetDrawMouseCursor()); // hw cursor branch
//
// where flag2 = MyVideoSettingsManager.IsHardwareCursorUsed() is always false
// on Linux. That means neither branch runs during gameplay (no focused GUI
// screen, or the focused screen does not want the cursor), and
// SetMouseCursorVisibility(false) is never called. SdlGameWindow.UpdateMouseMode
// therefore never enables SDL relative mouse mode, and the OS cursor is free
// to wander off the window while the player controls a character or ship.
//
// The recompiled fix adds a new `else if (OperatingSystem.IsLinux())` branch
// to Draw that forces SetMouseCursorVisibility(false) in the gameplay case.
// We replicate it as a Harmony postfix: after Draw runs, if no GUI needs the
// cursor, call SetMouseCursorVisibility(false). The Prefix above will still
// flip the call back to visible=true whenever a GUI actually wants the cursor,
// so the two patches compose safely.
[HarmonyPatch(typeof(MyDX9Gui), nameof(MyDX9Gui.Draw))]
[HarmonyPatchCategory("Finish")]
static class MyDX9GuiDrawCapturePatch
{
    static void Postfix(MyDX9Gui __instance)
    {
        var screenWithFocus = MyScreenManager.GetScreenWithFocus();
        bool guiNeedsCursor =
            (screenWithFocus != null && screenWithFocus.GetDrawMouseCursor())
            || (MyScreenManager.InputToNonFocusedScreens && MyScreenManager.GetScreensCount() > 1);
        if (!guiNeedsCursor)
            __instance.SetMouseCursorVisibility(false);
    }
}

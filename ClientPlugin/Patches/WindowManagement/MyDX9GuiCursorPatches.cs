using HarmonyLib;
using Sandbox;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRage;
using VRage.Input;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Patches.WindowManagement;

// UI hit testing uses scaled coordinates; the software cursor uses raw window
// coordinates and controls SDL relative mode through cursor visibility.

[HarmonyPatch(typeof(MyDX9Gui), "get_MouseCursorPosition")]
[HarmonyPatchCategory("Finish")]
static class MyDX9GuiMouseCursorPositionPatch
{
    static bool Prefix(ref Vector2 __result)
    {
        __result = MyGuiManager.GetNormalizedMousePosition(
            MyInput.Static.GetMousePosition(),
            MyInput.Static.GetMouseAreaSize()
        );
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

        // Hide the software cursor only after the pointer leaves the window.
        var config = MySandboxGame.Config;
        if (
            config != null
            && config.CaptureMouse == false
            && config.WindowMode == MyWindowModeEnum.Window
        )
        {
            shouldDrawCursor = raw.X >= 0f && raw.Y >= 0f && raw.X < area.X && raw.Y < area.Y;
        }

        if (mouseCursorTexture != null && shouldDrawCursor)
        {
            // Publish the texture before enqueueing the cursor sprite.
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
                ignoreBounds: true
            );
        }

        return false;
    }
}

// A GUI that needs the software cursor must keep SDL relative mode disabled.
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

// Linux has no hardware-cursor branch to hide the pointer during gameplay.
// Force hidden state when no GUI requests the software cursor.
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

// Linux does not support the COM STA clipboard worker. Read through SDL and
// continue GPS parsing on the next game-thread update.

using System;
using ClientPlugin.Compatibility;
using HarmonyLib;
using Sandbox.Game.Localization;
using Sandbox.Game.Screens.Terminal;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage;

namespace ClientPlugin.Patches.SystemAbstraction;

[HarmonyPatch(typeof(MyTerminalGpsController), nameof(MyTerminalGpsController.OnButtonPressedNewFromClipboard))]
[HarmonyPatchCategory("Finish")]
static class MyTerminalGpsControllerNewFromClipboardPatch
{
    static bool Prefix(MyTerminalGpsController __instance, MyGuiControlButton senderButton)
    {
        var controller = __instance;

        SdlClipboard.RequestText(raw =>
        {
            if (controller == null)
                return;

            try
            {
                controller.m_clipboardText = raw ?? string.Empty;
                if (!string.IsNullOrEmpty(controller.m_clipboardText))
                {
                    MySession.Static?.Gpss?.ScanText(
                        controller.m_clipboardText,
                        MyTexts.Get(MySpaceTexts.TerminalTab_GPS_NewFromClipboard_Desc));
                }
                if (controller.m_searchBox != null)
                    controller.m_searchBox.SearchText = string.Empty;
            }
            catch (Exception)
            {
                // The GPS tab may close before the callback.
            }
        });

        return false;
    }
}

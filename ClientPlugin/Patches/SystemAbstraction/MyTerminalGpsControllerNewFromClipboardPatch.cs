// Skip the Win32-only STA worker thread that the GPS terminal tab spins up
// to read the clipboard. Stock
// Sandbox.Game.Screens.Terminal.MyTerminalGpsController.OnButtonPressedNewFromClipboard
// does:
//
//     Thread thread = new Thread(PasteFromClipboard);
//     thread.SetApartmentState(ApartmentState.STA);   // <-- throws here
//     thread.Start();
//     thread.Join();
//
// Thread.SetApartmentState(STA) is a Windows-only COM apartment configuration
// and throws System.PlatformNotSupportedException("COM Interop is not
// supported on this platform") on the Linux .NET runtime, crashing the game
// the moment the user clicks "New from clipboard" in the GPS terminal tab.
//
// Same root cause as MyGuiControlClipboardPastePatches.cs: the STA apartment
// was historically required because the original implementation called
// System.Windows.Forms.Clipboard, which uses OLE and must run on an STA
// thread. Since we redirect MyWindowsSystem.Clipboard to SDL3 (see
// MyWindowsSystemClipboardPatch / SdlClipboard), there is no COM-affinity
// requirement anymore and the worker thread is pure overhead.
//
// We replace OnButtonPressedNewFromClipboard with a two-phase version. The
// Prefix returns immediately and asks SdlClipboard.RequestText for the OS
// clipboard contents; the SDL read runs on the render thread. The
// continuation (main game thread, next Plugin.Update tick) hands the text
// to MyGpsCollection.ScanText — identical to the stock behaviour minus the
// STA dance and minus blocking the main thread on the X11 selection
// round-trip.

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
                // GPS tab may have been closed between the click and the
                // callback. Nothing meaningful to do.
            }
        });

        return false;
    }
}

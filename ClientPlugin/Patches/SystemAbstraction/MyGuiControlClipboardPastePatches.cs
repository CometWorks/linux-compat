// Skip the Win32-only STA worker thread that the textbox controls spin up
// to read the clipboard. On .NET 9 Linux the very first line of
// Sandbox.Graphics.GUI.MyGuiControlTextbox+MyGuiControlTextboxSelection.PasteText
// does:
//
//     Thread thread = new Thread(PasteFromClipboard);
//     thread.SetApartmentState(ApartmentState.STA);   // <-- throws here
//
// Thread.SetApartmentState(STA) is a Windows-only COM apartment configuration
// and throws System.PlatformNotSupportedException("COM Interop is not
// supported on this platform") on the Linux .NET runtime, blowing up
// Ctrl+V in any text input.
//
// The STA apartment was historically required because the original
// implementation called System.Windows.Forms.Clipboard, which uses OLE and
// must run on an STA thread. Since we redirect MyWindowsSystem.Clipboard to
// SDL3 (see MyWindowsSystemClipboardPatch / SdlClipboard), there is no
// COM-affinity requirement anymore and the worker thread is pure overhead.
//
// We replace PasteText with a two-phase version:
//   1. Prefix returns immediately (no blocking) and requests the OS
//      clipboard contents via SdlClipboard.RequestText. The SDL call runs
//      on the render thread.
//   2. The continuation runs on the main game thread one frame later
//      (drained by Plugin.Update → MainThreadDispatcher.Pump), at which
//      point we read the textbox's current state and insert the text.
//
// Reading textbox state inside the continuation (rather than capturing it
// at Prefix entry) keeps the paste correct even if any other code mutates
// the textbox in the ~1 frame window between Ctrl+V and the callback.

using System;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using ClientPlugin.Compatibility;
using HarmonyLib;
using Sandbox.Graphics.GUI;

namespace ClientPlugin.Patches.SystemAbstraction;

[HarmonyPatch(typeof(MyGuiControlTextbox.MyGuiControlTextboxSelection), nameof(MyGuiControlTextbox.MyGuiControlTextboxSelection.PasteText))]
[HarmonyPatchCategory("Finish")]
static class MyGuiControlTextboxPasteTextPatch
{
    static bool Prefix(MyGuiControlTextbox.MyGuiControlTextboxSelection __instance, MyGuiControlTextbox sender)
    {
        var selection = __instance;
        var target = sender;

        SdlClipboard.RequestText(raw =>
        {
            if (selection == null || target == null)
                return;

            string clipboardText = SanitizeXmlOrEmpty(raw ?? string.Empty);

            try
            {
                selection.EraseText(target);

                string text = target.m_text.ToString();
                int caret = target.CarriagePositionIndex;
                if (caret < 0) caret = 0;
                if (caret > text.Length) caret = text.Length;
                string before = text.Substring(0, caret);
                string after = text.Substring(caret);

                selection.ClipboardText = clipboardText;

                string sanitized = clipboardText.Replace("\n", "");
                string toInsert;
                if (sanitized.Length + text.Length <= target.MaxLength)
                {
                    toInsert = sanitized;
                }
                else
                {
                    int room = target.MaxLength - text.Length;
                    toInsert = (room <= 0) ? "" : sanitized.Substring(0, room);
                }

                target.SetText(new StringBuilder(before).Append(toInsert).Append(after));
                target.CarriagePositionIndex = before.Length + toInsert.Length;

                selection.Reset(target);
            }
            catch (Exception)
            {
                // Control may have been disposed between Ctrl+V and the
                // callback (screen closed). Swallow silently — there's
                // nothing meaningful to paste into.
            }
        });

        return false;
    }

    private static string SanitizeXmlOrEmpty(string clipboard)
    {
        // Match stock PasteFromClipboard: drop entire clipboard if any
        // character is not XML-safe.
        for (int i = 0; i < clipboard.Length; i++)
        {
            if (!XmlConvert.IsXmlChar(clipboard[i]))
                return string.Empty;
        }
        return clipboard;
    }
}

[HarmonyPatch(typeof(MyGuiControlMultilineText.MyGuiControlMultilineSelection), nameof(MyGuiControlMultilineText.MyGuiControlMultilineSelection.PasteText))]
[HarmonyPatchCategory("Finish")]
static class MyGuiControlMultilineTextPasteTextPatch
{
    static bool Prefix(MyGuiControlMultilineText.MyGuiControlMultilineSelection __instance, MyGuiControlMultilineText sender)
    {
        var selection = __instance;
        var target = sender;

        SdlClipboard.RequestText(raw =>
        {
            if (selection == null || target == null)
                return;

            string clipboardText = SanitizeXmlOrEmpty(raw ?? string.Empty);

            try
            {
                selection.EraseText(target);

                string text = target.m_text.ToString();
                int caret = target.CarriagePositionIndex;
                if (caret < 0) caret = 0;
                if (caret > text.Length) caret = text.Length;
                string before = text.Substring(0, caret);
                string after = text.Substring(caret);

                selection.ClipboardText = clipboardText;

                target.Text = new StringBuilder(before).Append(Regex.Replace(clipboardText, "\r\n", "\n")).Append(after);
                target.CarriagePositionIndex = before.Length + clipboardText.Length;

                selection.Reset(target);
            }
            catch (Exception)
            {
                // See note in MyGuiControlTextboxPasteTextPatch.
            }
        });

        return false;
    }

    private static string SanitizeXmlOrEmpty(string clipboard)
    {
        for (int i = 0; i < clipboard.Length; i++)
        {
            if (!XmlConvert.IsXmlChar(clipboard[i]))
                return string.Empty;
        }
        return clipboard;
    }
}

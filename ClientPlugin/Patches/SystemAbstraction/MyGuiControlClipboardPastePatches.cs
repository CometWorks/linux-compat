// Linux does not support the COM STA clipboard worker. Read through SDL, then
// inspect and update textbox state on the next game-thread update.

using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using ClientPlugin.Compatibility;
using HarmonyLib;
using Sandbox.Graphics.GUI;

namespace ClientPlugin.Patches.SystemAbstraction;

[HarmonyPatch(
    typeof(MyGuiControlTextbox.MyGuiControlTextboxSelection),
    nameof(MyGuiControlTextbox.MyGuiControlTextboxSelection.PasteText)
)]
[HarmonyPatchCategory("Finish")]
static class MyGuiControlTextboxPasteTextPatch
{
    static bool Prefix(
        MyGuiControlTextbox.MyGuiControlTextboxSelection __instance,
        MyGuiControlTextbox sender
    )
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

                StringBuilder textBuilder = target.m_text;
                string text = textBuilder.ToString();
                int caret = target.CarriagePositionIndex;
                if (caret < 0)
                    caret = 0;
                if (caret > text.Length)
                    caret = text.Length;
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
                // The control may be disposed before the callback.
            }
        });

        return false;
    }

    private static string SanitizeXmlOrEmpty(string clipboard)
    {
        // Match PasteFromClipboard by rejecting any XML-unsafe character.
        for (int i = 0; i < clipboard.Length; i++)
        {
            if (!XmlConvert.IsXmlChar(clipboard[i]))
                return string.Empty;
        }
        return clipboard;
    }
}

[HarmonyPatch(
    typeof(MyGuiControlMultilineText.MyGuiControlMultilineSelection),
    nameof(MyGuiControlMultilineText.MyGuiControlMultilineSelection.PasteText)
)]
[HarmonyPatchCategory("Finish")]
static class MyGuiControlMultilineTextPasteTextPatch
{
    static bool Prefix(
        MyGuiControlMultilineText.MyGuiControlMultilineSelection __instance,
        MyGuiControlMultilineText sender
    )
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

                StringBuilder textBuilder = target.m_text;
                string text = textBuilder.ToString();
                int caret = target.CarriagePositionIndex;
                if (caret < 0)
                    caret = 0;
                if (caret > text.Length)
                    caret = text.Length;
                string before = text.Substring(0, caret);
                string after = text.Substring(caret);

                selection.ClipboardText = clipboardText;

                target.Text = new StringBuilder(before)
                    .Append(Regex.Replace(clipboardText, "\r\n", "\n"))
                    .Append(after);
                target.CarriagePositionIndex = before.Length + clipboardText.Length;

                selection.Reset(target);
            }
            catch (Exception)
            {
                // The control may be disposed before the callback.
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

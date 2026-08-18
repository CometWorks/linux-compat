// Linux does not support the COM STA clipboard worker. Read through SDL, then
// inspect and update textbox state on the next game-thread update.

using System;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using ClientPlugin.Compatibility;
using HarmonyLib;
using Sandbox.Graphics.GUI;

namespace ClientPlugin.Patches.SystemAbstraction;

[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
static class MyGuiControlTextboxPasteTextPatch
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    static MethodBase TargetMethod()
    {
        var selectionType = typeof(MyGuiControlTextbox).GetNestedType("MyGuiControlTextboxSelection", Flags);
        return selectionType?.GetMethod("PasteText", Flags);
    }

    static bool Prefix(object __instance, MyGuiControlTextbox sender)
    {
        var selection = __instance;
        var selectionType = __instance.GetType();
        var target = sender;

        SdlClipboard.RequestText(raw =>
        {
            if (selection == null || target == null)
                return;

            string clipboardText = SanitizeXmlOrEmpty(raw ?? string.Empty);

            try
            {
                selectionType.GetMethod("EraseText", Flags)?.Invoke(selection, new object[] { target });

                StringBuilder textBuilder = AccessTools.FieldRefAccess<MyGuiControlTextbox, StringBuilder>("m_text").Invoke(target);
                string text = textBuilder.ToString();
                int caret = target.CarriagePositionIndex;
                if (caret < 0) caret = 0;
                if (caret > text.Length) caret = text.Length;
                string before = text.Substring(0, caret);
                string after = text.Substring(caret);

                AccessTools.FieldRefAccess<string>(selectionType, "ClipboardText").Invoke(selection) = clipboardText;

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

                selectionType.GetMethod("Reset", Flags)?.Invoke(selection, new object[] { target });
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

[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
static class MyGuiControlMultilineTextPasteTextPatch
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    static MethodBase TargetMethod()
    {
        var selectionType = typeof(MyGuiControlMultilineText).GetNestedType("MyGuiControlMultilineSelection", Flags);
        return selectionType?.GetMethod("PasteText", Flags);
    }

    static bool Prefix(object __instance, MyGuiControlMultilineText sender)
    {
        var selection = __instance;
        var selectionType = __instance.GetType();
        var target = sender;

        SdlClipboard.RequestText(raw =>
        {
            if (selection == null || target == null)
                return;

            string clipboardText = SanitizeXmlOrEmpty(raw ?? string.Empty);

            try
            {
                selectionType.GetMethod("EraseText", Flags)?.Invoke(selection, new object[] { target });

                StringBuilder textBuilder = AccessTools.FieldRefAccess<MyGuiControlMultilineText, StringBuilder>("m_text").Invoke(target);
                string text = textBuilder.ToString();
                int caret = target.CarriagePositionIndex;
                if (caret < 0) caret = 0;
                if (caret > text.Length) caret = text.Length;
                string before = text.Substring(0, caret);
                string after = text.Substring(caret);

                AccessTools.FieldRefAccess<string>(selectionType, "ClipboardText").Invoke(selection) = clipboardText;

                target.Text = new StringBuilder(before).Append(Regex.Replace(clipboardText, "\r\n", "\n")).Append(after);
                target.CarriagePositionIndex = before.Length + clipboardText.Length;

                selectionType.GetMethod("Reset", Flags)?.Invoke(selection, new object[] { target });
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

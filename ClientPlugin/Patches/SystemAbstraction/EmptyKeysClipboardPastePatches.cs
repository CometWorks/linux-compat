// EmptyKeys text boxes (workshop browser, store, ATM, contracts, ...) paste
// through MyClipboardService.GetText, which starts a COM STA thread and throws
// PlatformNotSupportedException on Linux. Read the clipboard through SDL and
// run the original paste with that text on the next game-thread update.

using System.Reflection;
using ClientPlugin.Compatibility;
using HarmonyLib;
using Sandbox.Graphics;

namespace ClientPlugin.Patches.SystemAbstraction;

[HarmonyPatch("EmptyKeys.UserInterface.Documents.TextEditor", "PasteFromClipboard")]
[HarmonyPatchCategory("Finish")]
static class EmptyKeysTextEditorPastePatch
{
    // Clipboard text for the deferred paste, null outside of it.
    internal static string PendingText;

    static bool Prefix(object __instance, object undoManager, MethodBase __originalMethod)
    {
        if (PendingText != null)
            return true;

        SdlClipboard.RequestText(raw =>
        {
            PendingText = raw ?? string.Empty;
            try
            {
                __originalMethod.Invoke(__instance, new[] { undoManager });
            }
            catch (TargetInvocationException)
            {
                // The editor may be gone by the time the clipboard is read.
            }
            finally
            {
                PendingText = null;
            }
        });

        return false;
    }
}

[HarmonyPatch(typeof(MyClipboardService), nameof(MyClipboardService.GetText))]
[HarmonyPatchCategory("Finish")]
static class MyClipboardServiceGetTextPatch
{
    static bool Prefix(ref string __result)
    {
        __result = EmptyKeysTextEditorPastePatch.PendingText ?? SdlClipboard.GetText();
        return false;
    }
}

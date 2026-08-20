// Replace WinForms/OLE clipboard access with SDL. Native calls stay on the SDL
// thread; off-thread synchronous reads use SdlClipboard's cache.

using ClientPlugin.Compatibility;
using HarmonyLib;

namespace ClientPlugin.Patches.SystemAbstraction;

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "Clipboard", MethodType.Getter)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemClipboardGetterPatch
{
    static bool Prefix(ref string __result)
    {
        __result = SdlClipboard.GetText();
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "Clipboard", MethodType.Setter)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemClipboardSetterPatch
{
    static bool Prefix(string value)
    {
        SdlClipboard.SetText(value);
        return false;
    }
}

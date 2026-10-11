using HarmonyLib;
using VRageRender;

namespace ClientPlugin.Patches.UIDisplay;

// The display settings save a mode's refresh rate numerator, but GetDisplayMode
// checks it as millihertz, which only works for a denominator of 1000. DXVK
// reports SDL's refresh rates, on X11 pixel clock / (htotal * vtotal), so every
// start in Fullscreen failed the check and fell back to the desktop resolution.
// Also accept a mode with the saved numerator, as GetCurrentModeDescriptor does.
[HarmonyPatch(typeof(MyAdapterInfo), nameof(MyAdapterInfo.GetDisplayMode))]
[HarmonyPatchCategory("Finish")]
static class DisplayModeRefreshRatePatch
{
    static void Postfix(
        ref MyAdapterInfo __instance,
        int width,
        int height,
        int refreshRate,
        ref MyDisplayMode? __result
    )
    {
        if (__result.HasValue || refreshRate == 0 || __instance.SupportedDisplayModes == null)
            return;

        foreach (MyDisplayMode mode in __instance.SupportedDisplayModes)
        {
            if (mode.Width == width && mode.Height == height && mode.RefreshRate == refreshRate)
            {
                __result = mode;
                return;
            }
        }
    }
}

using System.Text;
using ClientPlugin.Patches.PlatformGuards;
using HarmonyLib;
using Sandbox.Engine.Platform.VideoMode;
using Sandbox.Game.Gui;
using VRage;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Patches.UIDisplay;

// X11 window sizes need not match supported display modes. Add the live
// windowed backbuffer so resolution selection compares against its actual size.
internal static class DisplayResolutionDropdownHelper
{
    public static long GetResolutionKey(Vector2I resolution)
    {
        // Matches MyGuiScreenOptionsDisplay.GetResolutionKey: ((long)X << 32) | Y.
        return ((long)resolution.X << 32) | (uint)resolution.Y;
    }

    // Prefer SDL drawable pixels, matching backbuffer resize input.
    public static Vector2I GetCurrentBackbuffer()
    {
        var sdl = SdlInput2Provider.Instance;
        if (sdl != null)
        {
            var px = sdl.ClientSizePixels;
            if (px.X > 0 && px.Y > 0)
                return px;
        }
        return MyRenderProxy.BackBufferResolution;
    }

    // Returns the selectable live-resolution key, or zero outside windowed mode.
    public static long EnsureCurrentBackbufferEntry(MyGuiScreenOptionsDisplay screen)
    {
        if (screen.GetSelectedWindowMode() != MyWindowModeEnum.Window)
            return 0L;

        Vector2I current = GetCurrentBackbuffer();
        if (current.X <= 0 || current.Y <= 0)
            return 0L;

        long key = GetResolutionKey(current);
        var combo = screen.m_comboResolution;
        if (combo.TryGetItemByKey(key) != null)
            return key;

        var aspectEnum = MyVideoSettingsManager.GetClosestAspectRatio((float)current.X / (float)current.Y);
        var aspect = MyVideoSettingsManager.GetAspectRatio(aspectEnum);
        combo.AddItem(key, new StringBuilder($"{current.X} x {current.Y} - {aspect.TextShort}"));
        MyLog.Default.WriteLine(
            $"DisplayResolutionDropdown: added live backbuffer entry {current.X}x{current.Y} (key={key:X16}) to resolution combo");
        return key;
    }
}

[HarmonyPatch(typeof(MyGuiScreenOptionsDisplay), "UpdateResolutionComboBox")]
[HarmonyPatchCategory("Finish")]
static class MyGuiScreenOptionsDisplayUpdateResolutionComboBoxPatch
{
    static void Postfix(MyGuiScreenOptionsDisplay __instance)
    {
        DisplayResolutionDropdownHelper.EnsureCurrentBackbufferEntry(__instance);
    }
}

[HarmonyPatch(typeof(MyGuiScreenOptionsDisplay), "WriteSettingsToControls")]
[HarmonyPatchCategory("Finish")]
static class MyGuiScreenOptionsDisplayWriteSettingsToControlsPatch
{
    // Dialog initialization can briefly rebuild the list under another mode.
    // Restore and select the live entry after controls settle.
    static void Postfix(MyGuiScreenOptionsDisplay __instance)
    {
        long key = DisplayResolutionDropdownHelper.EnsureCurrentBackbufferEntry(__instance);
        if (key == 0L)
            return;

        var combo = __instance.m_comboResolution;
        if (combo.TryGetItemByKey(key) == null)
            return;
        combo.SelectItemByKey(key);
        MyLog.Default.WriteLine(
            $"DisplayResolutionDropdown: selected live backbuffer entry (key={key:X16})");
    }
}

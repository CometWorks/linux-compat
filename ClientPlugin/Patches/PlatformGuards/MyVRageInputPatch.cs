using System;
using System.Collections.Generic;
using ClientPlugin.Compatibility;
using HarmonyLib;
using VRage;
using VRage.Input;
using VRage.Input.Keyboard;
using VRage.Platform.Windows;

namespace ClientPlugin.Patches.PlatformGuards;

[HarmonyPatch(typeof(MyVRagePlatform), "get_Input2")]
[HarmonyPatchCategory("Finish")]
static class MyVRagePlatformInput2Patch
{
    static bool Prefix(ref IVRageInput2 __result)
    {
        __result = SdlInput2Provider.Input2;
        return __result == null;
    }
}

static class SdlInput2Provider
{
    // The SDL3 game window, when rendering. Stays null under PULSAR_NO_RENDER,
    // where InitializeRenderThread (and with it CreateWindowPatch) never runs.
    // Window-management patches use this and must keep the concrete type.
    public static SdlGameWindow Instance { get; set; }

    // The platform input device behind MyVRage.Platform.Input2: the SDL window
    // when rendering, the HeadlessWindow no-op stub under PULSAR_NO_RENDER.
    public static IVRageInput2 Input2 { get; set; }
}

[HarmonyPatch(typeof(MyVRageInput), nameof(MyVRageInput.LoadContent))]
[HarmonyPatchCategory("Finish")]
static class MyVRageInputLoadContentPatch
{
    static bool Prefix(MyVRageInput __instance)
    {
        var input2 = MyVRage.Platform.Input2;

        __instance.IsDirectInputInitialized = input2 != null;

        if (input2 != null)
        {
            __instance.m_keyboardState = new MyGuiLocalizedKeyboardState(input2);
            Console.WriteLine("[LinuxCompat] Input2 initialized for keyboard state");
        }

        return false;
    }
}

[HarmonyPatch(typeof(MyVRageInput), "InitializeJoystickIfPossible")]
[HarmonyPatchCategory("Finish")]
static class MyVRageInputInitializeJoystickPatch
{
    static bool Prefix(MyVRageInput __instance)
    {
        if (MyVRage.Platform.Input2 == null)
        {
            __instance.m_joysticks = new List<string>();
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(MyVRageInput), "SearchForJoystickNow")]
[HarmonyPatchCategory("Finish")]
static class MyVRageInputSearchForJoystickPatch
{
    static bool Prefix()
    {
        return MyVRage.Platform.Input2 != null;
    }
}

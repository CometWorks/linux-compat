using System;
using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Compatibility;
using ClientPlugin.Tools;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using VRage;
using VRage.Input;
using VRage.Input.Keyboard;
using VRage.Platform.Windows;

namespace ClientPlugin.Patches.PlatformGuards;

static class SdlInput2Provider
{
    // The SDL3 game window, when rendering. Stays null under PULSAR_NO_RENDER,
    // where InitializeRenderThread (and with it CreateWindowPatch) never runs.
    // Window-management patches use this and must keep the concrete type.
    public static SdlGameWindow Instance { get; set; }

    // The platform input device behind MyVRage.Platform.Input2: the SDL window
    // when rendering, the HeadlessWindow no-op stub under PULSAR_NO_RENDER.
    // MyVRagePlatformInput2Prepatch makes the game's getter return it.
    public static IVRageInput2 Input2 { get; set; }
}

// MyVRagePlatform.get_Input2 returns Input2 above when it is set, else the game's own field.
// Written into the getter's body: as a Harmony prefix it got lost on a player's machine, the
// getter returned the game's null field, and MyVRageInput.UpdateStates crashed on
// Input2.DeveloperKeys while joining a server (SE-1587).
public static class MyVRagePlatformInput2Prepatch
{
    // Called from the prepatched getter. Returns object, so that importing it into the game
    // assembly doesn't pull in a game type.
    public static object Input2Object() => SdlInput2Provider.Input2;

    public static void Prepatch(TypeDefinition myVRagePlatform)
    {
        var getter = myVRagePlatform.Methods.FirstOrDefault(m =>
            m.Name == "get_Input2" && m.Parameters.Count == 0
        );
        if (getter?.Body == null)
        {
            Console.WriteLine(
                "[LinuxCompat] MyVRagePlatformInput2Prepatch: MyVRagePlatform.get_Input2 not found"
            );
            return;
        }

        var module = myVRagePlatform.Module;
        var input2Object = module.ImportReference(
            typeof(MyVRagePlatformInput2Prepatch).GetMethod(nameof(Input2Object))
        );
        getter.Body.Instructions.RecordOriginalCode(getter);
        var il = getter.Body.GetILProcessor();
        var first = getter.Body.Instructions[0];
        var pop = il.Create(OpCodes.Pop);
        il.InsertBefore(first, il.Create(OpCodes.Call, input2Object));
        il.InsertBefore(first, il.Create(OpCodes.Castclass, getter.ReturnType));
        il.InsertBefore(first, il.Create(OpCodes.Dup));
        il.InsertBefore(first, il.Create(OpCodes.Brfalse_S, pop));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
        il.InsertBefore(first, pop);
        getter.Body.Instructions.RecordPatchedCode(getter);
        Console.WriteLine(
            "[LinuxCompat] MyVRagePlatformInput2Prepatch: patched MyVRagePlatform.get_Input2"
        );
    }
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

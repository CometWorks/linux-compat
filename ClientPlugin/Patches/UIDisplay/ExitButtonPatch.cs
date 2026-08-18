using HarmonyLib;
using Sandbox.Graphics.GUI;
using SpaceEngineers.Game.GUI;

namespace ClientPlugin.Patches.UIDisplay;

// Run after Pulsar's menu postfix, which otherwise restores the Windows label.
[HarmonyPatch(typeof(MyGuiScreenMainMenu), "CreateMainMenu")]
[HarmonyPatchCategory("Finish")]
static class MyGuiScreenMainMenuCreateMainMenuExitTextPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    static void Postfix(MyGuiControlButton ___m_exitGameButton)
    {
        if (___m_exitGameButton != null)
            ___m_exitGameButton.Text = "Exit to Linux";
    }
}

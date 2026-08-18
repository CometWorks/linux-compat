using System.Diagnostics;
using HarmonyLib;
using Sandbox;

namespace ClientPlugin.Patches.SystemAbstraction;

// ExitThreadSafe can hang while waiting for tasks on Linux; terminate immediately instead.
[HarmonyPatch(typeof(MySandboxGame), nameof(MySandboxGame.ExitThreadSafe))]
[HarmonyPatchCategory("Finish")]
static class MySandboxGameExitThreadSafePatch
{
    static bool Prefix()
    {
        MySandboxGame.IsExiting = true;
        Process.GetCurrentProcess().Kill();
        return false;
    }
}

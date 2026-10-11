using System;
using System.Diagnostics;
using HarmonyLib;
using Sandbox;

namespace ClientPlugin.Patches.SystemAbstraction;

// ExitThreadSafe can hang while waiting for tasks on Linux; terminate immediately instead.
[HarmonyPatch(typeof(MySandboxGame), nameof(MySandboxGame.ExitThreadSafe))]
[HarmonyPatchCategory("Finish")]
static class MySandboxGameExitThreadSafePatch
{
    // Cleanup that must not be skipped by the kill, set by the client.
    internal static Action BeforeKill = null;

    static bool Prefix()
    {
        MySandboxGame.IsExiting = true;
        try
        {
            BeforeKill?.Invoke();
        }
        finally
        {
            Process.GetCurrentProcess().Kill();
        }
        return false;
    }
}

using System.Diagnostics;
using HarmonyLib;
using Sandbox;

namespace ServerPlugin.Patches.SystemAbstraction;

// Ports Topic 3.5 (commit f3051162) from dotnet-game-local.
//
// On Linux the stock MySandboxGame.ExitThreadSafe shutdown path hangs in
// Static.Invoke / WaitForTasksToFinish. The test harness eventually
// SIGKILLs the process and pytest sees exit code 137 instead of a clean
// exit. Recompiled code prepends Process.GetCurrentProcess().Kill() so
// that any request to exit terminates the process immediately.
//
// We mirror that here: kill the current process from the prefix and skip
// the original method. Process.Kill on the current process delivers
// SIGKILL to ourselves and does not return, so the rest of ExitThreadSafe
// (form.Hide, WaitForTasksToFinish, feedback URL, etc.) becomes dead code
// on Linux and is intentionally skipped.
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

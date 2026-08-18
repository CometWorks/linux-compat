using HarmonyLib;
using Sandbox.ModAPI;

namespace ClientPlugin.Patches.PathHandling.ModApiWrappers;

// Wrap mod API aliases after initialization without changing engine statics.
// Type checks keep repeated session initialization idempotent.
[HarmonyPatch(typeof(MyModAPIHelper), nameof(MyModAPIHelper.Initialize))]
[HarmonyPatchCategory("Finish")]
static class ModApiWrapperInstallPatch
{
    static void Postfix()
    {
        if (MyAPIGateway.Utilities != null && MyAPIGateway.Utilities is not WrappedUtilities)
            MyAPIGateway.Utilities = new WrappedUtilities(MyAPIGateway.Utilities);

        if (MyAPIGateway.Session != null && MyAPIGateway.Session is not WrappedSession)
            MyAPIGateway.Session = new WrappedSession(MyAPIGateway.Session);
    }
}

using HarmonyLib;
using Sandbox.ModAPI;

namespace ClientPlugin.Patches.PathHandling.ModApiWrappers;

// Wrap the utilities alias after initialization without changing engine statics.
// Path MAPPING lives in the mod compilation rewriter; this wrapper only keeps
// Windows-behavior emulation (storage filename validation, CRLF XML, casing).
// The type check keeps repeated session initialization idempotent.
[HarmonyPatch(typeof(MyModAPIHelper), nameof(MyModAPIHelper.Initialize))]
[HarmonyPatchCategory("Finish")]
static class ModApiWrapperInstallPatch
{
    static void Postfix()
    {
        if (MyAPIGateway.Utilities != null && MyAPIGateway.Utilities is not WrappedUtilities)
            MyAPIGateway.Utilities = new WrappedUtilities(MyAPIGateway.Utilities);
    }
}

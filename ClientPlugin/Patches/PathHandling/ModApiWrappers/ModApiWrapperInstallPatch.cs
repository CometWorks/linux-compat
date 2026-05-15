using HarmonyLib;
using Sandbox.ModAPI;

namespace ClientPlugin.Patches.PathHandling.ModApiWrappers;

// Installs the mod-facing wrappers immediately after the engine populates
// MyAPIGateway. The Postfix replaces the two members mods can use to reach
// filesystem-shaped data:
//
//   * MyAPIGateway.Utilities = MyAPIUtilities.Static  (line 387)
//   * MyAPIGateway.Session   = MySession.Static       (line 381)
//
// Mods see Windows-shaped paths from the wrappers; engine code that reaches
// the same objects through their concrete-typed statics (MyAPIUtilities.Static,
// MySession.Static) is untouched.
//
// Idempotent: re-entering Initialize (e.g. session restart) wraps the inner
// reference only if it isn't already one of our wrappers, so we never stack
// wrappers around wrappers.
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

using System.Diagnostics.CodeAnalysis;
using ClientPlugin.Rewriter;
using HarmonyLib;
using SpaceEngineers.Game;
using VRage.Scripting;

namespace ClientPlugin.Patches.PathHandling;

/// <summary>
/// Whitelists <see cref="WindowsPath"/> for rewritten mod IL.
/// The postfix runs after DotNetCompat populates the default whitelist.
/// </summary>
[HarmonyPatchCategory("Init")]
[HarmonyPatch(typeof(MySpaceGameDefaultIlChecker), "AllowDefaultNamespaces")]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public static class WindowsPathWhitelistPatch
{
    // ReSharper disable once UnusedMember.Local
    [HarmonyPostfix]
    private static void Postfix(IMyWhitelistBatch handle)
    {
        handle.AllowTypes(MyWhitelistTarget.Both, typeof(WindowsPath));
    }
}

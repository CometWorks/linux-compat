using System.Diagnostics.CodeAnalysis;
using ClientPlugin.Rewriter;
using HarmonyLib;
using SpaceEngineers.Game;
using VRage.Scripting;

namespace ClientPlugin.Patches.PathHandling;

/// <summary>
/// Adds <see cref="WindowsPath"/> to the mod-script whitelist.
///
/// Background: <see cref="PathSubstitutionRewriter"/> rewrites every
/// <c>System.IO.Path.X(...)</c> call inside a mod to
/// <c>ClientPlugin.Rewriter.WindowsPath.X(...)</c>. Without this patch, the
/// IL checker rejects the rewritten assembly because <c>WindowsPath</c> lives
/// in an unwhitelisted assembly (this plugin).
///
/// We attach to <c>MySpaceGameDefaultIlChecker.AllowDefaultNamespaces</c> as a
/// postfix so the addition happens after the dotnet-compat plugin's own
/// prefix has populated the default whitelist (the prefix replaces the
/// original implementation, returning false; postfixes still run).
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

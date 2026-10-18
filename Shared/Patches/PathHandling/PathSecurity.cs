using System;

namespace ClientPlugin.Patches.PathHandling;

/// <summary>
/// One switch per mod-API path security fix, so a player or server admin can turn off the one
/// a popular mod trips over without losing the others. All of them are on by default.
/// </summary>
public interface IPathSecurityConfig
{
    /// <summary>A1: a <c>ModItem</c> naming no mod loaded in the session is refused.</summary>
    bool RefuseUnloadedModItems { get; }

    /// <summary>
    /// A2-A3: a mod-location or game-content path that still has a drive letter after one
    /// translation is refused.
    /// </summary>
    bool RefuseLeftoverDrives { get; }

    /// <summary>
    /// A4: <c>PathUtils.EnsureSafePath</c> checks the native path the open will use, refusing a
    /// leftover drive letter. Off, it checks the path translated once, as before.
    /// </summary>
    bool CheckSafePathAsOpened { get; }

    /// <summary>B1: mod source that names the rewriter shims fails to compile.</summary>
    bool RefuseShimUse { get; }

    /// <summary>B4: a definition path assignment used as a value gives back the Windows form.</summary>
    bool TranslateAssignmentValues { get; }
}

/// <summary>
/// The switches the path security fixes read. Each fix reads them where it runs, so a change
/// applies from the next call or, for the rewriter, the next mod compile.
/// </summary>
public static class PathSecurity
{
    private sealed class AllOn : IPathSecurityConfig
    {
        public bool RefuseUnloadedModItems => true;
        public bool RefuseLeftoverDrives => true;
        public bool CheckSafePathAsOpened => true;
        public bool RefuseShimUse => true;
        public bool TranslateAssignmentValues => true;
    }

    private static readonly IPathSecurityConfig Defaults = new AllOn();

    /// <summary>
    /// Returns the plugin's config, or null while the user data folder is not known yet; it
    /// loads the config file once and keeps it. Set by the client and the server plugin before
    /// any mod compiles. The dedicated server compiles the world's mods before
    /// <c>IPlugin.Init</c>, which is why the config is read on first use rather than from there.
    /// </summary>
    public static Func<IPathSecurityConfig> Loader;

    /// <summary>The plugin's config, or all switches on until it can be loaded.</summary>
    public static IPathSecurityConfig Config => Loader?.Invoke() ?? Defaults;
}

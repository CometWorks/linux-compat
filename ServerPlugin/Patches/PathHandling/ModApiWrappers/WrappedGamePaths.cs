using System;
using System.Runtime.CompilerServices;
using VRage.Game.ModAPI;

namespace ServerPlugin.Patches.PathHandling.ModApiWrappers;

/// <summary>
/// Mod-facing wrapper for <see cref="IMyGamePaths"/> that returns
/// Windows-style backslash paths regardless of underlying separator.
///
/// Only mod scripts receive this wrapper — the engine never reads
/// <c>MyAPIGateway.Utilities.GamePaths</c> for its own filesystem work, so
/// translating egress here is safe.
/// </summary>
internal sealed class WrappedGamePaths : IMyGamePaths
{
    private readonly IMyGamePaths _inner;

    public WrappedGamePaths(IMyGamePaths inner)
    {
        _inner = inner;
    }

    public string ContentPath  => PathHelpers.ToWindowsPath(_inner.ContentPath);
    public string ModsPath     => PathHelpers.ToWindowsPath(_inner.ModsPath);
    public string UserDataPath => PathHelpers.ToWindowsPath(_inner.UserDataPath);
    public string SavesPath    => PathHelpers.ToWindowsPath(_inner.SavesPath);

    // The inner getter (Sandbox.ModAPI.MyAPIUtilities) computes this as
    // StripDllExt(Assembly.GetCallingAssembly().ManifestModule.ScopeName).
    // Forwarding via _inner.ModScopeName would pick up *this wrapper's*
    // assembly (LinuxCompat) as the caller. Resolve here using the wrapper's
    // own caller — that is the mod assembly. NoInlining is required so the
    // JIT does not fold this getter into a caller's frame and break the
    // GetCallingAssembly stack walk.
    public string ModScopeName
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get
        {
            var scope = System.Reflection.Assembly.GetCallingAssembly().ManifestModule.ScopeName;
            const string dll = ".dll";
            if (scope.EndsWith(dll, StringComparison.InvariantCultureIgnoreCase))
                scope = scope.Substring(0, scope.Length - dll.Length);
            return scope;
        }
    }
}

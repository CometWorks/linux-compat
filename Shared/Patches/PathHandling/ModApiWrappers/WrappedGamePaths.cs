using System;
using System.Runtime.CompilerServices;
using VRage.Game.ModAPI;

namespace ClientPlugin.Patches.PathHandling.ModApiWrappers;

/// <summary>
/// Exposes Windows-shaped paths to mods without changing engine filesystem paths.
/// </summary>
internal sealed class WrappedGamePaths : IMyGamePaths
{
    private readonly IMyGamePaths _inner;

    public WrappedGamePaths(IMyGamePaths inner)
    {
        _inner = inner;
    }

    public string ContentPath => PathHelpers.ToWindowsPath(_inner.ContentPath);
    public string ModsPath => PathHelpers.ToWindowsPath(_inner.ModsPath);
    public string UserDataPath => PathHelpers.ToWindowsPath(_inner.UserDataPath);
    public string SavesPath => PathHelpers.ToWindowsPath(_inner.SavesPath);

    // Forwarding would make GetCallingAssembly return LinuxCompat instead of the mod.
    // NoInlining preserves the stack frame required by that lookup.
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

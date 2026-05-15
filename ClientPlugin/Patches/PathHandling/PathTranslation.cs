using System;
using System.Collections.Generic;

namespace ClientPlugin.Patches.PathHandling;

/// <summary>
/// Synthesizes Windows-shape paths for mod-facing egress points. Mods that
/// string-match against a prefix (or hash a canonical path) see the same
/// shape they would on Windows; the engine continues to see real Linux
/// paths because the translation only happens inside the mod-facing
/// wrappers (<see cref="PathHelpers.ToWindowsPath"/>, the Roslyn-injected
/// <c>WindowsPath.FromGame</c> / <c>WindowsPath.GetFullPath</c> /
/// <c>WindowsPath.GetTempPath</c>, and the Cecil-injected explicit
/// interface getters on <c>MyModContext</c>).
///
/// Three Linux roots leak into mod-visible strings:
///   <c>/home/&lt;user&gt;/.steam/steam/steamapps/common/SpaceEngineers</c>
///     → <c>C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers</c>
///   <c>/home/&lt;user&gt;/.steam/debian-installation/steamapps/common/SpaceEngineers</c>
///     → <c>C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers</c>
///   <c>/home/&lt;user&gt;/.config/SpaceEngineers</c>
///     → <c>C:\Users\&lt;user&gt;\AppData\Roaming\SpaceEngineers</c>
///
/// Plus <c>/tmp</c> → <c>C:\Users\&lt;user&gt;\AppData\Local\Temp</c> and a
/// last-resort <c>$HOME</c> → <c>C:\Users\&lt;user&gt;</c>.
///
/// The username is read from <see cref="Environment.UserName"/> at plugin
/// init. The user authorised including the bare username in mod-visible
/// output (it already appears in the Windows reference log we mirror).
///
/// Inputs are matched in backslash-normalized form, with or without a
/// synthetic <c>C:</c> prefix, so the table key
/// <c>\home\viktor\.config\SpaceEngineers</c> matches both
/// <c>/home/viktor/.config/SpaceEngineers</c> (from raw engine egress) and
/// <c>C:\home\viktor\.config\SpaceEngineers</c> (from a path that already
/// passed through the rooted-no-drive promotion). Mappings are sorted by
/// key length descending so the longest applicable prefix wins.
///
/// Public so the Cecil prepatch (<see cref="MyModContextPrepatch"/>) can
/// add a cross-assembly call from <c>VRage.Game</c> into this helper.
/// </summary>
public static class PathTranslation
{
    private readonly struct Mapping
    {
        // Backslash-normalized, drive-less. No trailing separator.
        public readonly string KeyNoDrive;
        // Drive-prefixed Windows path. No trailing separator.
        public readonly string Replacement;

        public Mapping(string key, string replacement)
        {
            KeyNoDrive = key;
            Replacement = replacement;
        }
    }

    private static Mapping[] s_mappings = Array.Empty<Mapping>();
    private static string s_tempPath = @"C:\Temp\";

    /// <summary>
    /// Synthetic Windows temp directory, drive-prefixed and trailing-slash
    /// terminated to match <see cref="System.IO.Path.GetTempPath"/> output
    /// shape. Populated by <see cref="Init"/>; defaults to <c>C:\Temp\</c>
    /// before init so callers that race the plugin still get a sensible
    /// answer.
    /// </summary>
    public static string TempPath => s_tempPath;

    /// <summary>
    /// Build the prefix table from <see cref="Environment.UserName"/> and
    /// <c>$HOME</c>. Idempotent — second calls rebuild against the latest
    /// environment (cheap, only a handful of entries).
    /// </summary>
    public static void Init()
    {
        var user = Environment.UserName;
        if (string.IsNullOrEmpty(user))
            user = "user";

        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrEmpty(home))
            home = "/home/" + user;
        home = home.TrimEnd('/');

        // Backslash-normalize the HOME prefix to match the key shape.
        var homeBs = home.Replace('/', '\\');

        const string winSE = @"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers";
        var winUserSE   = @"C:\Users\" + user + @"\AppData\Roaming\SpaceEngineers";
        var winUserHome = @"C:\Users\" + user;
        var winTempDir  = @"C:\Users\" + user + @"\AppData\Local\Temp";

        var list = new List<Mapping>
        {
            // Canonical Steam install locations on Linux.
            new(homeBs + @"\.steam\steam\steamapps\common\SpaceEngineers", winSE),
            new(homeBs + @"\.steam\debian-installation\steamapps\common\SpaceEngineers", winSE),
            // SE user-data root (.config on Linux, AppData\Roaming on Windows).
            new(homeBs + @"\.config\SpaceEngineers", winUserSE),
            // System temp.
            new(@"\tmp", winTempDir),
            // Last resort: any path under HOME maps to C:\Users\<user>.
            new(homeBs, winUserHome),
        };

        list.Sort((a, b) => b.KeyNoDrive.Length - a.KeyNoDrive.Length);
        s_mappings = list.ToArray();
        s_tempPath = winTempDir + @"\";
    }

    /// <summary>
    /// Apply prefix translation to a backslash-normalized path. Accepts
    /// inputs with or without a leading <c>C:</c>. On match returns the
    /// mapped Windows path (always drive-prefixed). On miss returns the
    /// input string unchanged (same reference — callers can check identity
    /// to detect a miss without re-comparing).
    /// </summary>
    public static string Translate(string flipped)
    {
        if (string.IsNullOrEmpty(flipped))
            return flipped;

        // Strip a leading "C:" if present; table keys are drive-less so a
        // path that's already been C:-promoted hits the same entry as the
        // raw Linux original.
        bool hadDrive = flipped.Length >= 2 && flipped[1] == ':' &&
                        ((flipped[0] >= 'A' && flipped[0] <= 'Z') ||
                         (flipped[0] >= 'a' && flipped[0] <= 'z'));
        var body = hadDrive ? flipped.Substring(2) : flipped;

        var mappings = s_mappings;
        for (int i = 0; i < mappings.Length; i++)
        {
            var key = mappings[i].KeyNoDrive;
            if (body.Length < key.Length)
                continue;
            if (string.Compare(body, 0, key, 0, key.Length,
                    StringComparison.OrdinalIgnoreCase) != 0)
                continue;
            // Boundary check: exact match or next char is a separator.
            if (body.Length != key.Length && body[key.Length] != '\\')
                continue;

            return mappings[i].Replacement + body.Substring(key.Length);
        }

        return flipped;
    }
}

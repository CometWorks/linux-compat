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
///     → <c>C:\users\steamuser\AppData\Roaming\SpaceEngineers</c>
///
/// Plus <c>/tmp</c> → <c>C:\users\steamuser\AppData\Local\Temp</c> and a
/// last-resort <c>$HOME</c> → <c>C:\users\steamuser</c>.
///
/// The real Linux username (read from <see cref="Environment.UserName"/> at
/// plugin init) is used only to build the Linux-side match keys. The
/// Windows-shape output uses the Proton-conventional fixed name
/// <c>steamuser</c> with lowercase <c>users</c>, so the actual Linux
/// username never reaches mod-visible strings.
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
        // Forward-slash form of Replacement / KeyNoDrive, pre-computed so the
        // hot Untranslate path doesn't allocate per call. Same content, just
        // separator-flipped.
        public readonly string ReplacementForward;
        public readonly string KeyForward;

        public Mapping(string key, string replacement)
        {
            KeyNoDrive = key;
            Replacement = replacement;
            ReplacementForward = replacement.Replace('\\', '/');
            KeyForward = key.Replace('\\', '/');
        }
    }

    private static Mapping[] s_mappings = Array.Empty<Mapping>();
    private static string s_tempPath = @"C:\Temp\";

    /// <summary>
    /// Synthetic Windows install root that the Linux Steam install
    /// directory is translated to in mod-visible strings. Public field
    /// (not const) so a later configurability pass can reassign it
    /// before <see cref="Init"/> runs. Value matches the canonical
    /// Steam install location on a default Windows install — kept long
    /// for now so paths produced by <c>Path.GetFullPath</c> and
    /// <c>GamePaths.ContentPath</c> match what a Windows mod would see.
    /// </summary>
    public static string WindowsGameInstallPath =
        @"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers";

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

        var winSE = WindowsGameInstallPath;
        // Windows-shape paths use the Proton-conventional "steamuser" name
        // (lowercase "users") so the actual Linux username never leaks into
        // mod-visible output.
        const string winUserSE   = @"C:\users\steamuser\AppData\Roaming\SpaceEngineers";
        const string winUserHome = @"C:\users\steamuser";
        const string winTempDir  = @"C:\users\steamuser\AppData\Local\Temp";

        var list = new List<Mapping>
        {
            // Canonical Steam install locations on Linux.
            new(homeBs + @"\.steam\steam\steamapps\common\SpaceEngineers", winSE),
            new(homeBs + @"\.steam\debian-installation\steamapps\common\SpaceEngineers", winSE),
            // SE user-data root (.config on Linux, AppData\Roaming on Windows).
            new(homeBs + @"\.config\SpaceEngineers", winUserSE),
            // System temp.
            new(@"\tmp", winTempDir),
            // Last resort: any path under HOME maps to C:\users\steamuser.
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

    /// <summary>
    /// Reverse of <see cref="Translate"/>. Takes a forward-slash-normalized
    /// path that may carry a synthetic Windows drive prefix (from a mod that
    /// composed an absolute path off <c>ModContext.ModPath</c>) and returns
    /// the original Linux path. No-op for input without a drive prefix.
    ///
    /// On a table hit (longest matching Windows replacement wins) the
    /// drive-prefixed Windows prefix is swapped for the Linux key. On a
    /// miss the drive prefix is stripped — the body is the original Linux
    /// path that <see cref="PathHelpers.ToWindowsPath"/> promoted to
    /// <c>C:</c> on a translation miss.
    ///
    /// Symmetric companion to <see cref="Translate"/>: callers that have
    /// just done a <c>\</c>→<c>/</c> normalize can run this to recover a
    /// Linux-rooted path that <see cref="System.IO.Path.IsPathRooted"/>
    /// recognizes — the BCL on Linux only treats a leading <c>/</c> as a
    /// root marker, so a path still carrying its <c>C:</c> prefix would
    /// false-negative the rooted check and never reach disk resolution.
    ///
    /// Tradeoff: a real Linux file at <c>/C:/...</c> would be misread as a
    /// drive-prefixed Windows path and rewritten. In practice nobody has
    /// directories named <c>C:</c> at the filesystem root on Linux, and the
    /// reverse bug (mod-emitted absolute paths failing to load) is common.
    /// </summary>
    public static string Untranslate(string forwardSlashPath)
    {
        if (string.IsNullOrEmpty(forwardSlashPath))
            return forwardSlashPath;

        // Drive prefix detection: same shape as HasDrivePrefix in WindowsPath
        // — single letter [A-Za-z] followed by ':'. Path may continue with
        // '/' (rooted) or another char (drive-relative); both are handled.
        if (forwardSlashPath.Length < 2 || forwardSlashPath[1] != ':' ||
            !((forwardSlashPath[0] >= 'A' && forwardSlashPath[0] <= 'Z') ||
              (forwardSlashPath[0] >= 'a' && forwardSlashPath[0] <= 'z')))
            return forwardSlashPath;

        var mappings = s_mappings;
        string bestKey = null;
        string bestPrefix = null;
        for (int i = 0; i < mappings.Length; i++)
        {
            var winPrefix = mappings[i].ReplacementForward;
            if (forwardSlashPath.Length < winPrefix.Length)
                continue;
            if (string.Compare(forwardSlashPath, 0, winPrefix, 0, winPrefix.Length,
                    StringComparison.OrdinalIgnoreCase) != 0)
                continue;
            // Boundary: exact match or next char is a separator. Stops a
            // C:\users\steamuser2 input from matching the C:\users\steamuser
            // home-fallback entry.
            if (forwardSlashPath.Length != winPrefix.Length && forwardSlashPath[winPrefix.Length] != '/')
                continue;
            // Longest match wins — the array isn't sorted by Replacement
            // length (it's sorted by KeyNoDrive length for the forward
            // direction), so we have to scan the whole table.
            if (bestPrefix == null || winPrefix.Length > bestPrefix.Length)
            {
                bestPrefix = winPrefix;
                bestKey = mappings[i].KeyForward;
            }
        }

        if (bestPrefix != null)
            return bestKey + forwardSlashPath.Substring(bestPrefix.Length);

        // No table hit: strip just the drive prefix. The body is the original
        // Linux path that ToWindowsPath promoted to C: on a translation miss
        // (e.g. a mod stored on a /mnt/... mount that isn't in the table).
        return forwardSlashPath.Substring(2);
    }
}

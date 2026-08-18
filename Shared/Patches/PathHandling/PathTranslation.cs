using System;
using System.Collections.Generic;

namespace ClientPlugin.Patches.PathHandling;

/// <summary>
/// Maps Linux game, user, temp, and home roots to Proton-shaped paths exposed to mods.
/// Longest-prefix matching accepts paths with or without a synthetic drive prefix.
/// </summary>
public static class PathTranslation
{
    private readonly struct Mapping
    {
        public readonly string KeyNoDrive;
        public readonly string Replacement;
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

    /// <summary>Windows install root exposed to mods.</summary>
    public static string WindowsGameInstallPath =
        @"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers";

    /// <summary>Drive-prefixed temp path with the trailing separator expected from GetTempPath.</summary>
    public static string TempPath => s_tempPath;

    public static void Init()
    {
        var user = Environment.UserName;
        if (string.IsNullOrEmpty(user))
            user = "user";

        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrEmpty(home))
            home = "/home/" + user;
        home = home.TrimEnd('/');

        var homeBs = home.Replace('/', '\\');

        var winSE = WindowsGameInstallPath;
        // Keep the real Linux user out of mod-visible paths.
        const string winUserSE   = @"C:\users\steamuser\AppData\Roaming\SpaceEngineers";
        const string winUserHome = @"C:\users\steamuser";
        const string winTempDir  = @"C:\users\steamuser\AppData\Local\Temp";

        var list = new List<Mapping>
        {
            new(homeBs + @"\.steam\steam\steamapps\common\SpaceEngineers", winSE),
            new(homeBs + @"\.steam\debian-installation\steamapps\common\SpaceEngineers", winSE),
            new(homeBs + @"\.config\SpaceEngineers", winUserSE),
            new(@"\tmp", winTempDir),
            new(homeBs, winUserHome),
        };

        list.Sort((a, b) => b.KeyNoDrive.Length - a.KeyNoDrive.Length);
        s_mappings = list.ToArray();
        s_tempPath = winTempDir + @"\";
    }

    /// <summary>Translates a normalized Linux prefix, returning the same string reference on a miss.</summary>
    public static string Translate(string flipped)
    {
        if (string.IsNullOrEmpty(flipped))
            return flipped;

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
            if (body.Length != key.Length && body[key.Length] != '\\')
                continue;

            return mappings[i].Replacement + body.Substring(key.Length);
        }

        return flipped;
    }

    /// <summary>Restores a synthetic drive-prefixed mod path to a Linux-rooted path.</summary>
    public static string Untranslate(string forwardSlashPath)
    {
        if (string.IsNullOrEmpty(forwardSlashPath))
            return forwardSlashPath;

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
            if (forwardSlashPath.Length != winPrefix.Length && forwardSlashPath[winPrefix.Length] != '/')
                continue;
            if (bestPrefix == null || winPrefix.Length > bestPrefix.Length)
            {
                bestPrefix = winPrefix;
                bestKey = mappings[i].KeyForward;
            }
        }

        if (bestPrefix != null)
            return bestKey + forwardSlashPath.Substring(bestPrefix.Length);

        // Unmapped synthetic drives retain their Linux-rooted body.
        return forwardSlashPath.Substring(2);
    }
}

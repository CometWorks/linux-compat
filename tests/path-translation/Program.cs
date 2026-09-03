using System;
using System.IO;
using ClientPlugin.Patches.PathHandling;
using VRage.FileSystem;

namespace PathTranslationTests;

/// <summary>
/// Exercises PathTranslation.Init over fabricated $HOME trees and game root paths. Verifies the
/// paths the plugin produces, not the absence of log output.
/// </summary>
internal static class Program
{
    private static readonly string WinSE = PathTranslation.WindowsGameInstallPath;

    private static string s_testRoot;
    private static int s_failures;
    private static int s_checks;

    private static int Main()
    {
        s_testRoot = Path.Combine(Path.GetTempPath(), "linuxcompat-pathtests");

        var originalHome = Environment.GetEnvironmentVariable("HOME");
        try
        {
            InstallUnderHome();
            InstallOutsideHome();
            DifferentlyNamedInstall();
            TrailingSlashOnRoot();
            UnknownRootMapsNoInstall();
            NonGameMappingsUnaffected();
            UnsetHomeIsDiscovered();
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", originalHome);
        }

        Console.WriteLine();
        Console.WriteLine($"{s_checks - s_failures}/{s_checks} checks passed");
        if (s_failures > 0)
            Console.WriteLine($"FAILED: {s_failures}");
        return s_failures == 0 ? 0 : 1;
    }

    // ---- cases -----------------------------------------------------------------------------

    /// <summary>The install root is inside $HOME, so it must beat the shorter home mapping.</summary>
    private static void InstallUnderHome()
    {
        var home = Home("underhome");
        var install = home + "/games/SpaceEngineers";

        Init(home, install);
        Expect(
            "under home: linux -> win",
            ToWin(install + "/Content/Data/x.sbc"),
            WinSE + @"\Content\Data\x.sbc"
        );
        Expect(
            "under home: win -> linux",
            ToLinux(WinSE + @"\Content\Data\x.sbc"),
            install + "/Content/Data/x.sbc"
        );
        Expect(
            "under home: sibling stays home",
            ToWin(home + "/games/Other/x"),
            @"C:\users\steamuser\games\Other\x"
        );
    }

    private static void InstallOutsideHome()
    {
        var home = Home("outside");
        var install = "/mnt/games/SteamLibrary/steamapps/common/SpaceEngineers";

        Init(home, install);
        Expect(
            "outside: linux -> win",
            ToWin(install + "/Content/Textures/icon.dds"),
            WinSE + @"\Content\Textures\icon.dds"
        );
        Expect(
            "outside: win -> linux",
            ToLinux(WinSE + @"\Content\Textures\icon.dds"),
            install + "/Content/Textures/icon.dds"
        );
        Expect("outside: prefix only", ToWin(install), WinSE);
        Expect(
            "outside: longer sibling untouched",
            ToWin(install + "Dedicated/Content"),
            @"\mnt\games\SteamLibrary\steamapps\common\SpaceEngineersDedicated\Content"
        );
    }

    /// <summary>The server install is not called SpaceEngineers; the root is used as given.</summary>
    private static void DifferentlyNamedInstall()
    {
        var home = Home("server");
        var install = "/srv/se/SpaceEngineersDedicatedServer";

        Init(home, install);
        Expect("server: linux -> win", ToWin(install + "/Content"), WinSE + @"\Content");
        Expect("server: win -> linux", ToLinux(WinSE + @"\Content"), install + "/Content");
    }

    private static void TrailingSlashOnRoot()
    {
        var home = Home("trailing");
        var install = "/opt/SpaceEngineers";

        Init(home, install + "/");
        Expect("trailing slash: linux -> win", ToWin(install + "/Content"), WinSE + @"\Content");
        Expect("trailing slash: win -> linux", ToLinux(WinSE + @"\Content"), install + "/Content");
    }

    /// <summary>Before the launcher sets RootPath there is no install to map; nothing may throw.</summary>
    private static void UnknownRootMapsNoInstall()
    {
        var home = Home("noroot");

        try
        {
            Init(home, null);
            Expect("no root: linux passthrough", ToWin("/opt/se/Content"), @"\opt\se\Content");
            Expect(
                "no root: win passthrough",
                ToLinux(WinSE + @"\Content"),
                @"/Program Files (x86)/Steam/steamapps/common/SpaceEngineers/Content"
            );
        }
        catch (Exception e)
        {
            Fail("no root: Init threw " + e.GetType().Name);
        }
    }

    private static void NonGameMappingsUnaffected()
    {
        var home = Home("other");

        Init(home, "/opt/SpaceEngineers");
        Expect(
            "user data",
            ToWin(home + "/.config/SpaceEngineers/Saves/w.sbs"),
            @"C:\users\steamuser\AppData\Roaming\SpaceEngineers\Saves\w.sbs"
        );
        Expect("temp", ToWin("/tmp/foo.tmp"), @"C:\users\steamuser\AppData\Local\Temp\foo.tmp");
        Expect("home", ToWin(home + "/notes.txt"), @"C:\users\steamuser\notes.txt");
        Expect("temp path", PathTranslation.TempPath, @"C:\users\steamuser\AppData\Local\Temp\");
        Expect("unmapped passthrough", ToWin("/opt/other/file"), @"\opt\other\file");
    }

    /// <summary>With $HOME unset the user's real profile is used, not an assumed /home layout.</summary>
    private static void UnsetHomeIsDiscovered()
    {
        Environment.SetEnvironmentVariable("HOME", null);
        var expected = Environment
            .GetFolderPath(Environment.SpecialFolder.UserProfile)
            .TrimEnd('/');
        if (string.IsNullOrEmpty(expected))
        {
            Console.WriteLine("  SKIP unset-home: no user profile on this system");
            return;
        }

        MyFileSystem.RootPath = "/opt/SpaceEngineers";
        PathTranslation.Init();
        Expect("unset home", ToWin(expected + "/notes.txt"), @"C:\users\steamuser\notes.txt");
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static void Init(string home, string gameRoot)
    {
        Environment.SetEnvironmentVariable("HOME", home);
        MyFileSystem.RootPath = gameRoot;
        PathTranslation.Init();
    }

    private static string ToWin(string linuxPath) =>
        PathTranslation.Translate(linuxPath.Replace('/', '\\'));

    private static string ToLinux(string windowsPath) =>
        PathTranslation.Untranslate(windowsPath.Replace('\\', '/'));

    // Nothing is created on disk: translation is pure string mapping over the configured roots.
    private static string Home(string name) => Path.Combine(s_testRoot, name).Replace('\\', '/');

    private static void Expect(string label, string actual, string expected)
    {
        s_checks++;
        if (string.Equals(actual, expected, StringComparison.Ordinal))
        {
            Console.WriteLine("  ok   " + label);
            return;
        }
        s_failures++;
        Console.WriteLine("  FAIL " + label);
        Console.WriteLine("       expected: " + expected);
        Console.WriteLine("       actual:   " + actual);
    }

    private static void Fail(string label)
    {
        s_checks++;
        s_failures++;
        Console.WriteLine("  FAIL " + label);
    }
}

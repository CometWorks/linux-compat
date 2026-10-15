using System;
using System.IO;
using System.Runtime.CompilerServices;
using ClientPlugin.Patches.PathHandling;
using ClientPlugin.Rewriter;
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
            WorkshopInSteamLibrary();
            DifferentlyNamedInstall();
            TrailingSlashOnRoot();
            UnknownRootMapsNoInstall();
            NonGameMappingsUnaffected();
            UnsetHomeIsDiscovered();
            PathsInText();
            RoundTripsWithoutUserName();
            CaughtExceptionsAreScrubbed(originalHome);
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
            "outside: longer sibling is another Steam app",
            ToWin(install + "Dedicated/Content"),
            @"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineersDedicated\Content"
        );
    }

    /// <summary>Workshop items are in the game's Steam library, which may be under $HOME.</summary>
    private static void WorkshopInSteamLibrary()
    {
        var home = Home("workshop");
        var library = home + "/.steam/debian-installation/steamapps";
        var item = "/workshop/content/244850/514062285/Data/Scripts/x.cs";
        var winItem =
            @"C:\Program Files (x86)\Steam\steamapps\workshop\content\244850\514062285\Data\Scripts\x.cs";

        Init(home, library + "/common/SpaceEngineers");
        Expect("workshop: linux -> win", ToWin(library + item), winItem);
        Expect("workshop: win -> linux", ToLinux(winItem), library + item);
        Expect(
            "workshop: install still wins",
            ToWin(library + "/common/SpaceEngineers/Content"),
            WinSE + @"\Content"
        );

        Init(home, "/opt/SpaceEngineers");
        Expect(
            "workshop: no steamapps mapping outside Steam",
            ToWin(library + item),
            @"C:\users\steamuser\.steam\debian-installation\steamapps" + item.Replace('/', '\\')
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

    /// <summary>Exception messages that name native paths reach mods translated.</summary>
    private static void PathsInText()
    {
        var home = Home("text");

        Init(home, home + "/Steam/steamapps/common/SpaceEngineers");
        Expect(
            "text: quoted",
            PathTranslation.TranslateText(
                "Could not find a part of the path '"
                    + home
                    + "/.config/SpaceEngineers/Mods/A B/Data'."
            ),
            @"Could not find a part of the path 'C:\users\steamuser\AppData\Roaming\SpaceEngineers\Mods\A B\Data'."
        );
        Expect(
            "text: to the end",
            PathTranslation.TranslateText(
                "Mods are not allowed to read or write by specified path "
                    + home
                    + "/Steam/steamapps/common/SpaceEngineers/Content/Textures"
            ),
            @"Mods are not allowed to read or write by specified path "
                + WinSE
                + @"\Content\Textures"
        );
        Expect(
            "text: two paths",
            PathTranslation.TranslateText("'" + home + "/a' and '/tmp/b'"),
            @"'C:\users\steamuser\a' and 'C:\users\steamuser\AppData\Local\Temp\b'"
        );
        Expect(
            "text: not a root",
            PathTranslation.TranslateText("'/opt/tmp/x' and '/tmpfoo'"),
            "'/opt/tmp/x' and '/tmpfoo'"
        );
        Expect("text: none", PathTranslation.TranslateText("no paths"), "no paths");
    }

    /// <summary>
    /// What a mod gets back names neither /home nor the user, and leads back to the same file.
    /// </summary>
    private static void RoundTripsWithoutUserName()
    {
        const string home = "/home/alice";
        Init(home, home + "/.steam/steam/steamapps/common/SpaceEngineers");
        string[] natives =
        {
            home,
            home + "/.config/SpaceEngineers",
            home + "/.config/SpaceEngineers/Saves/76561198000000000/My World/Sandbox_config.sbc",
            home + "/.config/SpaceEngineers/Mods/My Mod/Data",
            home + "/.config/SpaceEngineers/SpaceEngineers_20261015_120000000.log",
            home + "/.steam/steam/steamapps/common/SpaceEngineers/Content/Data/Blocks.sbc",
            home + "/.steam/steam/steamapps/workshop/content/244850/123/Models/a.mwm",
            home + "/.local/share/Pulsar/Legacy/GitHub/plugin/Source.cs",
            "/tmp/abc.tmp",
        };
        foreach (var native in natives)
        {
            var win = ToWin(native);
            Expect("no user: " + native, NamesUser(win, home) ? win : "<no user>", "<no user>");
            Expect("round trip: " + native, ToLinux(win), native);
        }

        Expect(
            "text: stack trace frame",
            PathTranslation.TranslateText(
                "   at ClientPlugin.X.Read(String file) in "
                    + home
                    + "/.local/share/Pulsar/Legacy/GitHub/plugin/X.cs:line 12"
            ),
            @"   at ClientPlugin.X.Read(String file) in C:\users\steamuser\.local\share\Pulsar\Legacy\GitHub\plugin\X.cs:line 12"
        );
    }

    /// <summary>
    /// What WindowsException.Scrub, run first in every catch block of mod code, leaves of an
    /// exception: no native path in the message, the file name, the stack trace or ToString().
    /// </summary>
    private static void CaughtExceptionsAreScrubbed(string realHome)
    {
        Init(realHome, "/opt/SpaceEngineers");
        var native = realHome + "/.config/SpaceEngineers/Mods/M/Data/Scripts";
        try
        {
            ThrowNotFound(native);
        }
        catch (FileNotFoundException e)
        {
            WindowsException.Scrub(e);
            var win = @"C:\users\steamuser\AppData\Roaming\SpaceEngineers\Mods\M\Data\Scripts";
            Expect(
                "scrub: message",
                e.Message,
                "Access to protected location '" + win + "' not allowed."
            );
            Expect("scrub: file name", e.FileName, win);
            Expect(
                "scrub: ToString",
                e.ToString().Contains(realHome) ? e.ToString() : "<no home>",
                "<no home>"
            );
            // This file's path is in the trace when the build carries a PDB and lives under $HOME.
            var source = SourcePath();
            if (source.StartsWith(realHome + "/", StringComparison.Ordinal))
                Expect(
                    "scrub: stack trace",
                    e.StackTrace.Contains(realHome) ? e.StackTrace : "<no home>",
                    "<no home>"
                );
        }

        try
        {
            ThrowNotFound(native);
        }
        catch (Exception e) when (WindowsException.Scrub(e))
        {
            Expect(
                "scrub: in a filter",
                e.Message.Contains(realHome) ? e.Message : "<no home>",
                "<no home>"
            );
        }

        var aggregate = new AggregateException(new IOException("'" + native + "'"));
        WindowsException.Scrub(aggregate);
        Expect(
            "scrub: aggregate",
            aggregate.InnerExceptions[0].Message.Contains(realHome)
                ? aggregate.Message
                : "<no home>",
            "<no home>"
        );
        Expect("scrub: null", WindowsException.Scrub(null) ? "true" : "false", "true");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNotFound(string path) =>
        throw new FileNotFoundException(
            "Access to protected location '" + path + "' not allowed.",
            path
        );

    private static string SourcePath([CallerFilePath] string path = "") => path;

    private static bool NamesUser(string path, string home) =>
        path.IndexOf('/') >= 0
        || path.IndexOf(@"\home\", StringComparison.OrdinalIgnoreCase) >= 0
        || path.IndexOf(Path.GetFileName(home), StringComparison.OrdinalIgnoreCase) >= 0;

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

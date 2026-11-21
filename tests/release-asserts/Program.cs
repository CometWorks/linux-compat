// Release assert test (SE-1800). Calls the game's assert listener from
// VRage.Platform.Windows.dll as shipped and as ReleaseAssertPatch leaves it.
// The shipped one throws on Linux because System.Windows.Forms is missing; the
// patched one must log the assert and return.
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using ClientPlugin.Compatibility;
using Mono.Cecil;

string gameDir =
    args.Length > 0
        ? args[0]
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".steam/steam/steamapps/common/SpaceEngineersDedicatedServer/DedicatedServer64"
        );
string work = Directory.CreateTempSubdirectory("release-asserts-").FullName;
string shipped = Path.Combine(gameDir, "VRage.Platform.Windows.dll");
string patched = Path.Combine(work, "VRage.Platform.Windows.dll");
Console.WriteLine($"game: {gameDir}");

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(gameDir);
using (
    var asm = AssemblyDefinition.ReadAssembly(
        shipped,
        new ReaderParameters { AssemblyResolver = resolver }
    )
)
{
    if (!ReleaseAssertPatch.Apply(asm))
    {
        Console.WriteLine("RESULT: FAIL (the patch did not find the dialog block)");
        return 1;
    }
    asm.Write(patched);
}

var (shippedError, shippedLogged) = RunAssert(shipped, Path.Combine(work, "shipped"));
Console.WriteLine(
    $"shipped: {shippedError?.GetType().Name ?? "returned"}, logged: {shippedLogged}"
);
var (patchedError, patchedLogged) = RunAssert(patched, Path.Combine(work, "patched"));
Console.WriteLine(
    $"patched: {patchedError?.GetType().Name ?? "returned"}, logged: {patchedLogged}"
);
if (patchedError != null)
    Console.WriteLine(patchedError);

Directory.Delete(work, true);
bool pass = patchedError == null && patchedLogged;
Console.WriteLine(pass ? "RESULT: PASS" : "RESULT: FAIL");
return pass ? 0 : 1;

// Loads the platform assembly with the game's assemblies in a context of its
// own, points MyLog.Default at a fresh log and calls MyTraceListener.Fail the
// way MyDebug.FailRelease does.
(Exception, bool) RunAssert(string platform, string dir)
{
    Directory.CreateDirectory(dir);
    string logPath = Path.Combine(dir, "test.log");
    var context = new AssemblyLoadContext(platform, isCollectible: true);
    context.Resolving += (ctx, name) =>
    {
        string file = Path.Combine(gameDir, name.Name + ".dll");
        return File.Exists(file) ? ctx.LoadFromAssemblyPath(file) : null;
    };
    Exception error = null;
    try
    {
        context
            .LoadFromAssemblyName(new AssemblyName("VRage.Library"))
            .GetType("VRage.FileSystem.MyFileSystem", true)
            .GetMethod("Init")
            .Invoke(null, [dir, dir, "Mods", null, null]);
        Type myLog = context
            .LoadFromAssemblyName(new AssemblyName("VRage"))
            .GetType("VRage.Utils.MyLog", true);
        object log = Activator.CreateInstance(myLog, [false]);
        myLog
            .GetMethod("Init", [typeof(string), typeof(StringBuilder)])
            .Invoke(log, [logPath, new StringBuilder("test")]);
        myLog.GetProperty("Default").SetValue(null, log);

        Type listenerType = context
            .LoadFromAssemblyPath(platform)
            .GetType("VRage.Platform.Windows.Forms.MyDebugListenerProvider+MyTraceListener", true);
        object listener = Activator.CreateInstance(listenerType, true);
        try
        {
            listenerType
                .GetMethod("Fail", [typeof(string), typeof(string), typeof(string), typeof(int)])
                .Invoke(listener, ["SE-1800 probe", null, "probe.cs", 42]);
        }
        catch (TargetInvocationException e)
        {
            error = e.InnerException;
        }
        myLog.GetMethod("Close").Invoke(log, [false]);
    }
    finally
    {
        context.Unload();
    }
    string text = File.Exists(logPath) ? File.ReadAllText(logPath) : "";
    return (error, text.Contains("ASSERT: SE-1800 probe\nprobe.cs:42"));
}

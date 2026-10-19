// Mod corpus check for the LinuxCompat script rewriter.
// Compiles every mod script assembly the way the game does (C# 6, the game's references, the
// shim assembly added), once as written and once rewritten, and reports errors only the
// rewritten compilation has, and rewriter crashes. See README.md.
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

var (shimDll, bin64, modsRoot) = (args[0], args[1], args[2]);
var shim = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(shimDll));
var rewriterType = shim.GetType("ClientPlugin.Rewriter.CompilationRewriter", true);
var rewrite = rewriterType.GetMethod("Rewrite", BindingFlags.Public | BindingFlags.Static);
var modTarget = Enum.Parse(rewrite.GetParameters()[1].ParameterType, "Mod");
var shimRef = (MetadataReference)
    shim.GetType("ClientPlugin.Rewriter.ShimRegistration", true)
        .GetProperty("Reference", BindingFlags.Public | BindingFlags.Static)
        .GetValue(null);

var refs = new List<string> { Assembly.Load("netstandard").Location };
foreach (
    var n in new[]
    {
        "Sandbox.Game",
        "Sandbox.Common",
        "Sandbox.Graphics",
        "VRage",
        "VRage.Library",
        "VRage.Math",
        "VRage.Game",
        "VRage.Render",
        "VRage.Input",
        "SpaceEngineers.ObjectBuilders",
        "SpaceEngineers.Game",
        "System.Collections.Immutable",
        "ProtoBuf.Net.Core",
        "VRage.Scripting",
    }
)
    refs.Add(Path.Combine(bin64, n + ".dll"));
refs.Add(typeof(object).Assembly.Location);
refs.Add(typeof(XmlEntity).Assembly.Location);
refs.Add(typeof(HashSet<>).Assembly.Location);
refs.Add(typeof(Uri).Assembly.Location);
var rt = Path.GetDirectoryName(typeof(object).Assembly.Location);
refs.Add(Path.Combine(rt, "System.Runtime.dll"));
refs.Add(Path.Combine(rt, "System.Collections.dll"));
refs.Add(Path.Combine(rt, "mscorlib.dll"));

// netstandard only forwards; Roslyn needs the assemblies it forwards to (System.Linq, Regex,
// ConcurrentDictionary...), which the .NET 10 game process has loaded.
foreach (var forwarded in Assembly.Load("netstandard").GetReferencedAssemblies())
{
    var p = Path.Combine(rt, forwarded.Name + ".dll");
    if (File.Exists(p))
        refs.Add(p);
}
var references = refs.Distinct()
    .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
    .Append(shimRef)
    .ToList();

// The symbols MySpaceGameDefaultIlCompiler defines, on the default branch. VERSION_ and BUILD_
// are the minor and build parts of SpaceEngineersGame.SE_VERSION (1210014 is 1.210.014).
var seVersion = (int)
    CSharpCompilation
        .Create("Version", references: references)
        .GetTypeByMetadataName("SpaceEngineers.Game.SpaceEngineersGame")
        .GetMembers("SE_VERSION")
        .OfType<IFieldSymbol>()
        .Single()
        .ConstantValue;
var parse = new CSharpParseOptions(
    LanguageVersion.CSharp6,
    DocumentationMode.None
).WithPreprocessorSymbols(
    "BRANCH_STABLE",
    "STABLE",
    $"VERSION_{seVersion / 1000 % 1000}",
    $"BUILD_{seVersion % 1000}"
);
var options = new CSharpCompilationOptions(
    OutputKind.DynamicallyLinkedLibrary,
    optimizationLevel: OptimizationLevel.Release,
    platform: Platform.X64
);

var work = new List<(string Label, string[] Files)>();
foreach (var mod in Directory.GetDirectories(modsRoot).OrderBy(d => d))
{
    var scripts = Path.Combine(mod, "Data", "Scripts");
    if (!Directory.Exists(scripts))
        continue;
    foreach (
        var group in Directory
            .GetFiles(scripts, "*.cs", SearchOption.AllDirectories)
            .Where(f => Path.GetRelativePath(scripts, f).Contains(Path.DirectorySeparatorChar))
            .GroupBy(f => Path.GetRelativePath(scripts, f).Split(Path.DirectorySeparatorChar)[0])
    )
        work.Add(($"{Path.GetFileName(mod)}/{group.Key}", group.OrderBy(f => f).ToArray()));
}

var results = new (string Status, string Report)[work.Count];
Parallel.For(0, work.Count, i => results[i] = Check(i, work[i].Label, work[i].Files));
foreach (var (_, report) in results)
    if (report.Length > 0)
        Console.Write(report);
int assemblies = work.Count;
int cleanBase = results.Count(r => r.Status.Contains("clean"));
int changed = results.Count(r => r.Status.Contains("rewritten"));
int broken = results.Count(r => r.Status.Contains("broken"));
int crashed = results.Count(r => r.Status == "crash");
Console.WriteLine(
    $"SUMMARY assemblies={assemblies} clean_unrewritten={cleanBase} rewritten={changed} new_errors={broken} crashes={crashed}"
);
return broken + crashed == 0 ? 0 : 1;

(string, string) Check(int index, string label, string[] files)
{
    var report = new System.Text.StringBuilder();
    var trees = files
        .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), parse, f))
        .ToArray();
    var compilation = CSharpCompilation.Create("Mod_" + index, trees, references, options);
    var baseErrors = Errors(compilation);
    var status = baseErrors.Count == 0 ? "clean" : "dirty";
    if (baseErrors.Count > 0 && Environment.GetEnvironmentVariable("CORPUS_BASE") == "1")
        foreach (var d in baseErrors.Take(3))
            report.AppendLine($"BASE {label} {d.Id} {d.GetMessage()}");
    CSharpCompilation rewritten;
    try
    {
        rewritten = (CSharpCompilation)rewrite.Invoke(null, new[] { compilation, modTarget });
    }
    catch (TargetInvocationException e)
    {
        return ("crash", report.AppendLine($"CRASH {label}: {e.InnerException}").ToString());
    }
    if (ReferenceEquals(rewritten, compilation))
        return (status, report.ToString());
    status += " rewritten";
    var baseKeys = baseErrors.Select(Key).ToHashSet();
    var added = Errors(rewritten).Where(d => !baseKeys.Contains(Key(d))).ToList();
    if (added.Count == 0)
        return (status, report.ToString());
    report.AppendLine($"NEW-ERRORS {label} (base errors: {baseErrors.Count})");
    foreach (var d in added.Take(8))
        report.AppendLine($"  {d}");
    return (status + " broken", report.ToString());
}

static List<Diagnostic> Errors(CSharpCompilation c) =>
    c.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

static string Key(Diagnostic d) => d.Id + " " + d.GetMessage();

static class GameAssemblies
{
    // Roslyn and the game assemblies load from Bin64 (see ModCorpus.csproj). Main's locals are
    // Roslyn types, so the resolver has to be in place before Main is compiled.
    [ModuleInitializer]
    internal static void Init()
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Length != 4)
        {
            Console.Error.WriteLine(
                "Usage: ModCorpus <LinuxCompat.dll> <Bin64> <workshop content/244850>"
            );
            Environment.Exit(2);
        }
        var bin64 = args[2];
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            var p = Path.Combine(bin64, name.Name + ".dll");
            return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null;
        };
    }
}

using System;
using System.IO;
using System.Linq;
using ClientPlugin.Patches.PathHandling;
using ClientPlugin.Rewriter;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VRage.Scripting;

namespace RewriterTests;

/// <summary>
/// Runs <see cref="CompilationRewriter"/> on small mod sources compiled against stand-ins for
/// the shims (<c>Stubs.cs</c>) and the game's definition types, and checks what the rewritten
/// compilation reports.
/// </summary>
internal static class Program
{
    private const string Refusal = "LinuxCompat: mods may not use the LinuxCompat rewriter shims";

    private const string GameSource = """
        namespace VRage.Game
        {
            public class MyDefinitionBase
            {
                public string[] Icons;
            }
        }

        namespace Sandbox.Definitions
        {
            public class MyCubeBlockDefinition : VRage.Game.MyDefinitionBase
            {
                public string Model;

                public class BuildProgressModel
                {
                    public float BuildRatioUpperBound;
                    public string File;
                }
            }
        }
        """;

    private static readonly MetadataReference[] Framework = (
        (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
    )
        .Split(Path.PathSeparator)
        .Where(p =>
        {
            var name = Path.GetFileName(p);
            return name.StartsWith("System.") || name is "netstandard.dll" or "mscorlib.dll";
        })
        .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
        .ToArray();

    private static MetadataReference s_game;
    private static int s_failures;
    private static int s_checks;

    private static int Main()
    {
        s_game = Emit("GameStubs", GameSource);

        CleanModCompiles();
        ObjectInitializerStaysAssignment();
        AssignmentValueTranslated();
        ShimUseRefused();
        ShimUseAllowedOutsideMods();
        SwitchesOff();

        Console.WriteLine($"{s_checks - s_failures}/{s_checks} checks passed");
        return s_failures == 0 ? 0 : 1;
    }

    /// <summary>A mod that only reaches the shims through the rewriter is not refused.</summary>
    private static void CleanModCompiles()
    {
        var result = Rewrite(
            """
            using Sandbox.Definitions;

            public class Mod
            {
                public string Read(MyCubeBlockDefinition def) => def.Model;
            }
            """
        );
        CheckNoErrors("clean mod", result);
        Check(
            "clean mod: read rewritten",
            Text(result).Contains("WindowsPath.FromGameText")
                || Text(result).Contains("LinuxCompatFromGameText")
        );
    }

    /// <summary>
    /// An object initializer member is an assignment with no value. Wrapping it in
    /// <c>FromGameText</c> made the initializer a collection initializer, which fails to compile.
    /// </summary>
    private static void ObjectInitializerStaysAssignment()
    {
        var result = Rewrite(
            """
            using Sandbox.Definitions;

            public class Mod
            {
                public MyCubeBlockDefinition.BuildProgressModel Stage(string file) =>
                    new MyCubeBlockDefinition.BuildProgressModel { BuildRatioUpperBound = 0.5f, File = file };

                public MyCubeBlockDefinition Block(string model) =>
                    new MyCubeBlockDefinition { Model = model, Icons = new[] { model } };
            }
            """
        );
        CheckNoErrors("object initializer", result);
        var text = Text(result);
        Check(
            "object initializer: File translated",
            text.Contains("File = global::ClientPlugin.Rewriter.WindowsPath.ToGameText(file)")
        );
        Check(
            "object initializer: Model translated",
            text.Contains("Model = global::ClientPlugin.Rewriter.WindowsPath.ToGameText(model)")
        );
        Check(
            "object initializer: no value wrap",
            !text.Contains("FromGameText(File =") && !text.Contains("FromGameText(Model =")
        );
    }

    /// <summary>B4: an assignment used as a value gives back the Windows form.</summary>
    private static void AssignmentValueTranslated()
    {
        var result = Rewrite(
            """
            using Sandbox.Definitions;

            public class Mod
            {
                public string Simple(MyCubeBlockDefinition def, string p) => (def.Model = p);

                public string Compound(MyCubeBlockDefinition def, string p)
                {
                    var value = def.Model += p;
                    return value;
                }

                public void Statement(MyCubeBlockDefinition def, string p)
                {
                    def.Model = p;
                }
            }
            """
        );
        CheckNoErrors("assignment value", result);
        var text = Text(result);
        Check(
            "assignment value: simple",
            text.Contains(
                "FromGameText(def.Model = global::ClientPlugin.Rewriter.WindowsPath.ToGameText(p))"
            )
        );
        Check("assignment value: compound", text.Contains("FromGameText(def.Model += p)"));
        Check(
            "assignment value: statement left alone",
            text.Contains(
                "        def.Model = global::ClientPlugin.Rewriter.WindowsPath.ToGameText(p);"
            )
        );
    }

    /// <summary>
    /// B1: a mod naming a shim itself could read a native path back, so the compile fails
    /// with the refusal, whatever spelling binds to the shim.
    /// </summary>
    private static void ShimUseRefused()
    {
        CheckRefused(
            "direct call",
            """
            public class Mod
            {
                public string Native(string p) => ClientPlugin.Rewriter.WindowsPath.ToGame(p);
            }
            """,
            "WindowsPath.ToGame(string)"
        );
        CheckRefused(
            "using static",
            """
            using static ClientPlugin.Rewriter.WindowsPath;

            public class Mod
            {
                public string Native(string p) => ToGame(p);
            }
            """,
            "WindowsPath.ToGame(string)"
        );
        CheckRefused(
            "alias",
            """
            using W = ClientPlugin.Rewriter.WindowsPath;

            public class Mod
            {
                public string Native(string p) => W.ToGame(p);
            }
            """,
            "WindowsPath.ToGame(string)"
        );
        CheckRefused(
            "method group",
            """
            using System;

            public class Mod
            {
                public Func<string, string> Native = ClientPlugin.Rewriter.WindowsPath.ToGame;
            }
            """,
            "WindowsPath.ToGame(string)"
        );
        CheckRefused(
            "extension method",
            """
            using ClientPlugin.Rewriter.ModExtensions;

            public class Mod
            {
                public string Native(string p) => p.LinuxCompatFromGame(s => s);
            }
            """,
            "LinuxCompatFromGame"
        );
    }

    /// <summary>Only mod compilations are checked; the rewriter leaves other targets alone.</summary>
    private static void ShimUseAllowedOutsideMods()
    {
        var compilation = Compile(
            """
            public class Script
            {
                public string Native(string p) => ClientPlugin.Rewriter.WindowsPath.ToGame(p);
            }
            """
        );
        var result = CompilationRewriter.Rewrite(compilation, MyApiTarget.Ingame);
        Check("other target untouched", ReferenceEquals(result, compilation));
    }

    private sealed class Switches : IPathSecurityConfig
    {
        public bool RefuseUnloadedModItems => true;
        public bool RefuseLeftoverDrives => true;
        public bool CheckSafePathAsOpened => true;
        public bool RefuseShimUse { get; set; } = true;
        public bool TranslateAssignmentValues { get; set; } = true;
    }

    /// <summary>
    /// The config switches for B1 and B4: off, a mod naming a shim compiles, and an assignment
    /// used as a value is left as the mod wrote it.
    /// </summary>
    private static void SwitchesOff()
    {
        var switches = new Switches { RefuseShimUse = false, TranslateAssignmentValues = false };
        PathSecurity.Loader = () => switches;
        try
        {
            var shimUse = Rewrite(
                """
                public class Mod
                {
                    public string Native(string p) => ClientPlugin.Rewriter.WindowsPath.ToGame(p);
                }
                """
            );
            CheckNoErrors("switch off: shim use compiles", shimUse);

            var assignment = Rewrite(
                """
                using Sandbox.Definitions;

                public class Mod
                {
                    public string Simple(MyCubeBlockDefinition def, string p) => (def.Model = p);
                }
                """
            );
            CheckNoErrors("switch off: assignment value", assignment);
            Check(
                "switch off: assignment value not wrapped",
                Text(assignment)
                    .Contains(
                        "=> (def.Model = global::ClientPlugin.Rewriter.WindowsPath.ToGameText(p))"
                    )
            );
        }
        finally
        {
            PathSecurity.Loader = null;
        }
    }

    private static void CheckRefused(string label, string source, string symbol)
    {
        var compilation = Compile(source);
        CheckNoErrors($"refused {label}: compiles unrewritten", compilation);

        var result = CompilationRewriter.Rewrite(compilation, MyApiTarget.Mod);
        var errors = result
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage())
            .ToArray();
        Check(
            $"refused {label}",
            errors.Length == 1 && errors[0].Contains(Refusal) && errors[0].Contains(symbol),
            string.Join(" | ", errors)
        );
    }

    private static CSharpCompilation Rewrite(string source) =>
        CompilationRewriter.Rewrite(Compile(source), MyApiTarget.Mod);

    private static CSharpCompilation Compile(string source) =>
        CSharpCompilation.Create(
            "Mod",
            new[] { CSharpSyntaxTree.ParseText(source, path: "Mod.cs") },
            Framework.Append(ShimRegistration.Reference).Append(s_game),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

    private static PortableExecutableReference Emit(string name, string source)
    {
        var compilation = CSharpCompilation.Create(
            name,
            new[] { CSharpSyntaxTree.ParseText(source) },
            Framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        if (!emitted.Success)
            throw new InvalidOperationException(
                $"{name} stub does not compile: {string.Join(" | ", emitted.Diagnostics)}"
            );
        return MetadataReference.CreateFromImage(image.ToArray());
    }

    private static string Text(CSharpCompilation compilation) =>
        string.Join("\n", compilation.SyntaxTrees.Select(t => t.ToString()));

    private static void CheckNoErrors(string label, CSharpCompilation compilation)
    {
        var errors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        Check($"{label}: no errors", errors.Length == 0, string.Join(" | ", errors));
    }

    private static void Check(string label, bool ok, string detail = null)
    {
        s_checks++;
        if (ok)
        {
            Console.WriteLine($"PASS {label}");
            return;
        }
        s_failures++;
        Console.WriteLine($"FAIL {label}{(string.IsNullOrEmpty(detail) ? "" : ": " + detail)}");
    }
}

using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Patches.PathHandling;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VRage.Scripting;

namespace ClientPlugin.Rewriter;

internal static class CompilationRewriter
{
    public static CSharpCompilation Rewrite(CSharpCompilation compilation, MyApiTarget target)
    {
        if (target != MyApiTarget.Mod)
            return compilation;

        if (PathSecurity.Config.RefuseShimUse)
        {
            var shimUses = FindShimUses(compilation);
            if (shimUses.Count > 0)
                return RefuseShimUses(compilation, shimUses);
        }

        var replacements = new List<(SyntaxTree OldTree, SyntaxTree NewTree)>();
        var requiresShimReference = false;

        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot();
            var rewriter = new WindowsSemanticsRewriter(compilation.GetSemanticModel(tree));
            var rewrittenRoot = rewriter.Visit(root);
            if (ReferenceEquals(root, rewrittenRoot))
                continue;

            replacements.Add((tree, tree.WithRootAndOptions(rewrittenRoot, tree.Options)));
            requiresShimReference |= rewriter.RequiresShimReference;
        }

        if (replacements.Count == 0)
            return compilation;

        foreach (var (oldTree, newTree) in replacements)
            compilation = compilation.ReplaceSyntaxTree(oldTree, newTree);

        // The whitelisted assembly identity only matches when both use this one reference.
        var reference = ShimRegistration.Reference;
        if (
            requiresShimReference
            && reference != null
            && !compilation.References.Contains(reference)
        )
            compilation = compilation.AddReferences(reference);

        return compilation;
    }

    /// <summary>
    /// Every name in the mod's own source that binds to a symbol of the shim assembly, as
    /// <c>file(line,column): symbol</c>. The shims are whitelisted for mods because the rewriter
    /// emits calls to them, so the whitelist cannot keep a mod from calling, say,
    /// <see cref="WindowsPath.ToGame(string)"/> itself and reading the native path back. Binding
    /// is checked instead of the spelled name, which also covers <c>using static</c>, aliases,
    /// method groups and the extension methods.
    /// </summary>
    private static List<string> FindShimUses(CSharpCompilation compilation)
    {
        var uses = new List<string>();
        var reference = ShimRegistration.Reference;
        if (
            reference == null
            || compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol shims
        )
            return uses;

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                var info = model.GetSymbolInfo(name);
                var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
                // Assembly symbols are canonical within a compilation, so reference equality
                // holds; the game's Roslyn predates SymbolEqualityComparer.
                if (
                    symbol?.ContainingAssembly == null
                    || !ReferenceEquals(symbol.ContainingAssembly, shims)
                )
                    continue;
                var at = name.GetLocation().GetLineSpan().StartLinePosition;
                uses.Add(
                    $"{System.IO.Path.GetFileName(tree.FilePath)}({at.Line + 1},{at.Character + 1}): {symbol.ToDisplayString()}"
                );
            }
        }
        return uses;
    }

    /// <summary>
    /// Fails the mod's compile with a readable message. Throwing here is not an option: Pulsar
    /// disables a rewriter that throws, and every later mod would compile unrewritten.
    /// </summary>
    private static CSharpCompilation RefuseShimUses(
        CSharpCompilation compilation,
        List<string> uses
    )
    {
        var options = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
        var error = CSharpSyntaxTree.ParseText(
            "#error LinuxCompat: mods may not use the LinuxCompat rewriter shims: "
                + string.Join("; ", uses.Take(5))
                + (uses.Count > 5 ? $"; and {uses.Count - 5} more" : "")
                + "\n",
            options
        );
        return compilation.AddSyntaxTrees(error);
    }
}

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VRage.Scripting;

namespace ClientPlugin.Rewriter;

internal static class CompilationRewriter
{
    public static CSharpCompilation Rewrite(CSharpCompilation compilation, MyApiTarget target)
    {
        if (target != MyApiTarget.Mod)
            return compilation;

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
}

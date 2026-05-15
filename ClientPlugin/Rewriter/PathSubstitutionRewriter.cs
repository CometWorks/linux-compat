using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ClientPlugin.Rewriter;

/// <summary>
/// Roslyn rewriter that replaces every reference to <see cref="System.IO.Path"/>
/// inside mod source with the Windows-shaped <see cref="WindowsPath"/> shim.
///
/// The rewrite is surgical: only the <em>left</em> side of a member-access
/// expression is replaced. For example
/// <code>
///   Path.Combine("Data", "Foo.cs")          // method invocation
///   System.IO.Path.DirectorySeparatorChar   // static field access
///   typeof(Path)                            // type-of expression
/// </code>
/// become
/// <code>
///   global::ClientPlugin.Rewriter.WindowsPath.Combine(...)
///   global::ClientPlugin.Rewriter.WindowsPath.DirectorySeparatorChar
///   typeof(global::ClientPlugin.Rewriter.WindowsPath)
/// </code>
/// Symbol resolution is used (not lexical name matching) so a mod that
/// declares its own type named <c>Path</c> is unaffected.
///
/// Additionally, calls to <c>MyObjectBuilder_Checkpoint.ModItem.GetPath()</c>
/// are wrapped with <c>WindowsPath.FromGame(...)</c>. The other path-shaped
/// Mod API members are intercepted by the runtime wrappers installed on
/// <c>MyAPIGateway.Utilities</c> / <c>MyAPIGateway.Session</c>; <c>ModItem</c>
/// is a struct so it can't be wrapped by interface dispatch — this is the
/// compile-time substitute for that one case.
///
/// Finally, references to <c>System.Environment.NewLine</c> are replaced
/// with the string literal <c>"\r\n"</c> so mods see the Windows line
/// terminator (length 2) instead of Linux <c>"\n"</c> (length 1). This is
/// a constant-fold, not a redirection to a property, so a mod that hashes
/// or measures <c>Environment.NewLine</c> sees the same value it would on
/// Windows.
///
/// Limitations: <c>using static System.IO.Path;</c> followed by a bare
/// <c>Combine(...)</c> call is not rewritten. In practice mods qualify with
/// <c>Path.</c> or <c>System.IO.Path.</c>, which this pass handles.
/// </summary>
internal sealed class PathSubstitutionRewriter : CSharpSyntaxRewriter
{
    private const string SystemIoPathFqn = "global::System.IO.Path";
    private const string ReplacementFqn = "global::ClientPlugin.Rewriter.WindowsPath";
    private const string FromGameFqn = "global::ClientPlugin.Rewriter.WindowsPath.FromGame";
    private const string ModItemFqn = "global::VRage.Game.MyObjectBuilder_Checkpoint.ModItem";
    private const string EnvironmentFqn = "global::System.Environment";

    private readonly SemanticModel _semanticModel;

    public PathSubstitutionRewriter(SemanticModel semanticModel)
    {
        _semanticModel = semanticModel;
    }

    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        // Recurse first so nested expressions get their own substitution.
        var rewritten = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node);

        // Bind against the *original* node — the rewritten copy may contain
        // synthesized descendants detached from the syntax tree, which would
        // make SemanticModel.GetSymbolInfo throw "not within syntax tree".
        if (IsSystemIoPathTypeReference(node.Expression))
        {
            var newType = SyntaxFactory.ParseName(ReplacementFqn)
                .WithLeadingTrivia(rewritten.Expression.GetLeadingTrivia())
                .WithTrailingTrivia(rewritten.Expression.GetTrailingTrivia());
            return rewritten.WithExpression(newType);
        }

        // Constant-fold Environment.NewLine to the Windows "\r\n" literal.
        // Matching on the property symbol (not the textual name) avoids
        // catching a mod-defined NewLine member on some other type.
        if (IsEnvironmentNewLine(node))
        {
            return SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal("\r\n"))
                .WithLeadingTrivia(rewritten.GetLeadingTrivia())
                .WithTrailingTrivia(rewritten.GetTrailingTrivia());
        }

        return rewritten;
    }

    private bool IsEnvironmentNewLine(MemberAccessExpressionSyntax node)
    {
        if (_semanticModel.GetSymbolInfo(node).Symbol is not IPropertySymbol prop)
            return false;
        if (prop.Name != "NewLine")
            return false;
        var containing = prop.ContainingType;
        if (containing == null)
            return false;
        return containing.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == EnvironmentFqn;
    }

    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // The IMyUtilities / IMySession / IMyConfigDedicated / IMyGamePaths
        // wrappers intercept every path-shaped member by interface dispatch.
        // ModItem is a struct, so its GetPath() can't be wrapped — catch the
        // call here and wrap the result in WindowsPath.FromGame(...).
        var rewritten = (InvocationExpressionSyntax)base.VisitInvocationExpression(node);

        if (IsModItemGetPath(node))
        {
            return SyntaxFactory.InvocationExpression(
                    SyntaxFactory.ParseExpression(FromGameFqn),
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(rewritten.WithoutTrivia()))))
                .WithLeadingTrivia(rewritten.GetLeadingTrivia())
                .WithTrailingTrivia(rewritten.GetTrailingTrivia());
        }

        return rewritten;
    }

    private bool IsModItemGetPath(InvocationExpressionSyntax node)
    {
        // Bind the original (pre-rewrite) node — the rewritten copy has no
        // semantic-model entry. We match by method symbol so a user-declared
        // GetPath in another type is unaffected.
        if (_semanticModel.GetSymbolInfo(node).Symbol is not IMethodSymbol method)
            return false;
        if (method.Name != "GetPath")
            return false;
        if (method.Parameters.Length != 0)
            return false;
        var containing = method.ContainingType;
        if (containing == null)
            return false;
        return containing.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == ModItemFqn;
    }

    public override SyntaxNode VisitTypeOfExpression(TypeOfExpressionSyntax node)
    {
        var rewritten = (TypeOfExpressionSyntax)base.VisitTypeOfExpression(node);
        // Bind against the original node — see VisitMemberAccessExpression.
        if (IsSystemIoPathTypeReference(node.Type))
        {
            var newType = SyntaxFactory.ParseTypeName(ReplacementFqn)
                .WithLeadingTrivia(rewritten.Type.GetLeadingTrivia())
                .WithTrailingTrivia(rewritten.Type.GetTrailingTrivia());
            return rewritten.WithType(newType);
        }
        return rewritten;
    }

    private bool IsSystemIoPathTypeReference(SyntaxNode expression)
    {
        // We only want references where the syntactic node *is* the type
        // System.IO.Path — not, say, an instance expression whose type
        // happens to be Path (impossible, since Path is a static class, but
        // the check still guards against unusual generated trees).
        var symbol = _semanticModel.GetSymbolInfo(expression).Symbol;
        if (symbol is not INamedTypeSymbol named)
            return false;
        return named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == SystemIoPathFqn;
    }
}

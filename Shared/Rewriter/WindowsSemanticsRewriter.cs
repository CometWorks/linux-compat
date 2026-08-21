using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ClientPlugin.Rewriter;

/// <summary>
/// Rewrites mod source to use Windows path, newline, XML writer, and stopwatch semantics.
/// Symbol-based matching leaves mod-defined types with the same names unchanged.
/// Bare calls imported with <c>using static System.IO.Path</c> are not rewritten.
/// </summary>
internal sealed class WindowsSemanticsRewriter : CSharpSyntaxRewriter
{
    private const string SystemIoPathFqn = "global::System.IO.Path";
    private const string ReplacementFqn = "global::ClientPlugin.Rewriter.WindowsPath";
    private const string FromGameFqn = "global::ClientPlugin.Rewriter.WindowsPath.FromGame";
    private const string ModItemFqn = "global::VRage.Game.MyObjectBuilder_Checkpoint.ModItem";
    private const string EnvironmentFqn = "global::System.Environment";
    private const string StringBuilderFqn = "global::System.Text.StringBuilder";
    private const string TextWriterFqn = "global::System.IO.TextWriter";
    private const string WindowsTextWriterWriteLineFqn =
        "global::ClientPlugin.Rewriter.WindowsTextWriter.WriteLine";
    private const string StopwatchFqn = "global::System.Diagnostics.Stopwatch";
    private const string WindowsStopwatchFqn = "global::ClientPlugin.Rewriter.WindowsStopwatch";
    private const string XmlWriterSettingsFqn = "global::System.Xml.XmlWriterSettings";

    private readonly SemanticModel _semanticModel;

    internal bool RequiresShimReference { get; private set; }

    public WindowsSemanticsRewriter(SemanticModel semanticModel)
    {
        _semanticModel = semanticModel;
    }

    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var rewritten = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node);

        // Bind the original node because synthesized nodes are detached from the syntax tree.
        if (IsSystemIoPathTypeReference(node.Expression))
        {
            RequiresShimReference = true;
            var newType = SyntaxFactory
                .ParseName(ReplacementFqn)
                .WithLeadingTrivia(rewritten.Expression.GetLeadingTrivia())
                .WithTrailingTrivia(rewritten.Expression.GetTrailingTrivia());
            return rewritten.WithExpression(newType);
        }

        if (IsEnvironmentNewLine(node))
        {
            return SyntaxFactory
                .LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal("\r\n")
                )
                .WithLeadingTrivia(rewritten.GetLeadingTrivia())
                .WithTrailingTrivia(rewritten.GetTrailingTrivia());
        }

        // Fully qualified Stopwatch expressions parse as MemberAccessExpression.
        // Replace the whole chain because its Name slot requires SimpleNameSyntax.
        if (IsNamedTypeReference(node, StopwatchFqn))
        {
            RequiresShimReference = true;
            return SyntaxFactory
                .ParseName(WindowsStopwatchFqn)
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
        return containing.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            == EnvironmentFqn;
    }

    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // ModItem is a struct, so interface wrappers cannot intercept GetPath().
        // Rewrite its receiver here; conditional access is handled at the parent node.
        var rewritten = (InvocationExpressionSyntax)base.VisitInvocationExpression(node);

        // MemberBindingExpression nodes cannot be lifted out of their conditional access.
        if (IsModItemGetPath(node) && !IsOnConditionalAccessSpine(node))
        {
            var receiver = TryGetGetPathReceiver(rewritten);
            if (receiver != null)
            {
                RequiresShimReference = true;
                return SyntaxFactory
                    .InvocationExpression(
                        SyntaxFactory.ParseExpression(FromGameFqn),
                        SyntaxFactory.ArgumentList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Argument(receiver.WithoutTrivia())
                            )
                        )
                    )
                    .WithLeadingTrivia(rewritten.GetLeadingTrivia())
                    .WithTrailingTrivia(rewritten.GetTrailingTrivia());
            }
        }

        if (IsStringBuilderAppendLine(node))
            return RewriteStringBuilderAppendLine(rewritten) ?? (SyntaxNode)rewritten;

        if (IsTextWriterWriteLine(node))
        {
            var replacement = RewriteTextWriterWriteLine(rewritten);
            if (replacement == null)
                return rewritten;

            RequiresShimReference = true;
            return replacement;
        }

        return rewritten;
    }

    private bool IsStringBuilderAppendLine(InvocationExpressionSyntax node)
    {
        // SemanticModel contains only original syntax-tree nodes.
        if (_semanticModel.GetSymbolInfo(node).Symbol is not IMethodSymbol method)
            return false;
        if (method.Name != "AppendLine")
            return false;
        var containing = method.ContainingType;
        if (containing == null)
            return false;
        // Match the declaring type so mod-defined methods remain unchanged.
        return containing.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            == StringBuilderFqn;
    }

    private SyntaxNode RewriteStringBuilderAppendLine(InvocationExpressionSyntax rewritten)
    {
        // A receiver is required; using-static calls are unsupported.
        if (rewritten.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        var receiver = memberAccess.Expression;
        var args = rewritten.ArgumentList.Arguments;

        var crlfArg = SyntaxFactory.Argument(
            SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal("\r\n")
            )
        );

        if (args.Count == 0)
        {
            return SyntaxFactory
                .InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        receiver,
                        SyntaxFactory.IdentifierName("Append")
                    ),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(crlfArg))
                )
                .WithLeadingTrivia(rewritten.GetLeadingTrivia())
                .WithTrailingTrivia(rewritten.GetTrailingTrivia());
        }

        // Keep the receiver single-evaluation semantics of AppendLine.
        if (args.Count == 1)
        {
            var inner = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    receiver,
                    SyntaxFactory.IdentifierName("Append")
                ),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(args[0]))
            );

            return SyntaxFactory
                .InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        inner,
                        SyntaxFactory.IdentifierName("Append")
                    ),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(crlfArg))
                )
                .WithLeadingTrivia(rewritten.GetLeadingTrivia())
                .WithTrailingTrivia(rewritten.GetTrailingTrivia());
        }

        return null;
    }

    private bool IsTextWriterWriteLine(InvocationExpressionSyntax node)
    {
        if (_semanticModel.GetSymbolInfo(node).Symbol is not IMethodSymbol method)
            return false;
        if (method.Name != "WriteLine")
            return false;
        var containing = method.ContainingType;
        if (containing == null)
            return false;
        // Preserve subclass overrides; inherited TextWriter methods bind to the base declaration.
        return containing.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            == TextWriterFqn;
    }

    private SyntaxNode RewriteTextWriterWriteLine(InvocationExpressionSyntax rewritten)
    {
        if (rewritten.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        var receiver = memberAccess.Expression;

        // Roslyn binds the matching WindowsTextWriter overload during mod compilation.
        var newArgs = SyntaxFactory.SeparatedList(
            new[] { SyntaxFactory.Argument(receiver.WithoutTrivia()) }.Concat(
                rewritten.ArgumentList.Arguments
            )
        );

        return SyntaxFactory
            .InvocationExpression(
                SyntaxFactory.ParseExpression(WindowsTextWriterWriteLineFqn),
                SyntaxFactory.ArgumentList(newArgs)
            )
            .WithLeadingTrivia(rewritten.GetLeadingTrivia())
            .WithTrailingTrivia(rewritten.GetTrailingTrivia());
    }

    private bool IsModItemGetPath(InvocationExpressionSyntax node)
    {
        // Match the original method symbol so mod-defined GetPath methods remain unchanged.
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

    /// <summary>
    /// Detects nodes whose member-binding tokens are valid only inside an
    /// enclosing conditional-access expression.
    /// </summary>
    private static bool IsOnConditionalAccessSpine(SyntaxNode node)
    {
        for (var parent = node.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is ConditionalAccessExpressionSyntax)
                return true;
            if (parent is StatementSyntax || parent is MemberDeclarationSyntax)
                return false;
        }
        return false;
    }

    private static ExpressionSyntax TryGetGetPathReceiver(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            return memberAccess.Expression;
        return null;
    }

    /// <summary>
    /// Rewrites conditional <c>ModItem.GetPath()</c> at the enclosing node so
    /// Roslyn member-binding tokens remain in their required context.
    /// Chained calls are left unchanged because peeling changes their receiver type.
    /// </summary>
    public override SyntaxNode VisitConditionalAccessExpression(
        ConditionalAccessExpressionSyntax node
    )
    {
        var rewritten = (ConditionalAccessExpressionSyntax)
            base.VisitConditionalAccessExpression(node);

        if (
            node.WhenNotNull is InvocationExpressionSyntax tailInvocation
            && IsModItemGetPath(tailInvocation)
            && tailInvocation.Expression is MemberAccessExpressionSyntax
            && rewritten.WhenNotNull is InvocationExpressionSyntax rewrittenTail
            && rewrittenTail.Expression is MemberAccessExpressionSyntax rewrittenAccess
        )
        {
            RequiresShimReference = true;
            // Keep nested substitutions from the rewritten receiver.
            var peeled = rewritten.WithWhenNotNull(rewrittenAccess.Expression);

            return SyntaxFactory
                .InvocationExpression(
                    SyntaxFactory.ParseExpression(FromGameFqn),
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(peeled.WithoutTrivia())
                        )
                    )
                )
                .WithLeadingTrivia(rewritten.GetLeadingTrivia())
                .WithTrailingTrivia(rewritten.GetTrailingTrivia());
        }

        return rewritten;
    }

    /// <summary>
    /// Adds the Windows CRLF default to system <c>XmlWriterSettings</c> without
    /// overriding explicit initializers.
    /// </summary>
    public override SyntaxNode VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        var rewritten = (ObjectCreationExpressionSyntax)base.VisitObjectCreationExpression(node);

        if (!IsNamedTypeReference(node.Type, XmlWriterSettingsFqn))
            return rewritten;

        var crlfAssignment = SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxFactory.IdentifierName("NewLineChars"),
            SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal("\r\n")
            )
        );

        var existingInit = rewritten.Initializer;
        if (existingInit == null)
        {
            var newInit = SyntaxFactory.InitializerExpression(
                SyntaxKind.ObjectInitializerExpression,
                SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(crlfAssignment)
            );
            return rewritten.WithInitializer(newInit);
        }

        // Preserve explicit NewLineChars initializers.
        foreach (var expr in existingInit.Expressions)
        {
            if (
                expr is AssignmentExpressionSyntax asn
                && asn.Left is IdentifierNameSyntax id
                && id.Identifier.ValueText == "NewLineChars"
            )
                return rewritten;
        }

        var augmented = existingInit.WithExpressions(existingInit.Expressions.Add(crlfAssignment));
        return rewritten.WithInitializer(augmented);
    }

    public override SyntaxNode VisitTypeOfExpression(TypeOfExpressionSyntax node)
    {
        var rewritten = (TypeOfExpressionSyntax)base.VisitTypeOfExpression(node);
        // SemanticModel contains only original syntax-tree nodes.
        if (IsSystemIoPathTypeReference(node.Type))
        {
            RequiresShimReference = true;
            var newType = SyntaxFactory
                .ParseTypeName(ReplacementFqn)
                .WithLeadingTrivia(rewritten.Type.GetLeadingTrivia())
                .WithTrailingTrivia(rewritten.Type.GetTrailingTrivia());
            return rewritten.WithType(newType);
        }
        return rewritten;
    }

    private bool IsSystemIoPathTypeReference(SyntaxNode expression) =>
        IsNamedTypeReference(expression, SystemIoPathFqn);

    /// <summary>
    /// Matches named type references without matching instances of that type.
    /// </summary>
    private bool IsNamedTypeReference(SyntaxNode expression, string fullyQualifiedName)
    {
        var symbol = _semanticModel.GetSymbolInfo(expression).Symbol;
        if (symbol is not INamedTypeSymbol named)
            return false;
        return named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            == fullyQualifiedName;
    }

    // Replace whole qualified names because Roslyn child slots require SimpleNameSyntax.

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
    {
        // ParseName can return QualifiedNameSyntax, which is invalid in SimpleNameSyntax slots.
        if (IsInSimpleNameSlot(node))
            return base.VisitIdentifierName(node);

        var replacement = TryGetTypeSubstitution(node, node.Identifier.ValueText);
        if (replacement != null)
        {
            RequiresShimReference = true;
            return replacement
                .WithLeadingTrivia(node.GetLeadingTrivia())
                .WithTrailingTrivia(node.GetTrailingTrivia());
        }

        return base.VisitIdentifierName(node);
    }

    /// <summary>
    /// Detects slots where Roslyn requires a <see cref="SimpleNameSyntax"/> result.
    /// </summary>
    private static bool IsInSimpleNameSlot(SimpleNameSyntax node)
    {
        return node.Parent switch
        {
            MemberAccessExpressionSyntax mae => mae.Name == node,
            QualifiedNameSyntax qn => qn.Right == node,
            MemberBindingExpressionSyntax mbe => mbe.Name == node,
            AliasQualifiedNameSyntax aqn => aqn.Name == node,
            _ => false,
        };
    }

    public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
    {
        var replacement = TryGetTypeSubstitution(node, node.Right.Identifier.ValueText);
        if (replacement != null)
        {
            RequiresShimReference = true;
            return replacement
                .WithLeadingTrivia(node.GetLeadingTrivia())
                .WithTrailingTrivia(node.GetTrailingTrivia());
        }

        return base.VisitQualifiedName(node);
    }

    private SyntaxNode TryGetTypeSubstitution(SyntaxNode node, string simpleName)
    {
        // Avoid semantic lookup for unrelated identifiers.
        if (simpleName != "Stopwatch")
            return null;

        var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
        if (symbol is not INamedTypeSymbol named)
            return null;

        var fqn = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (fqn == StopwatchFqn)
            return SyntaxFactory.ParseName(WindowsStopwatchFqn);

        return null;
    }
}

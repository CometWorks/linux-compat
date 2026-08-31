using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ClientPlugin.Rewriter;

/// <summary>
/// Rewrites mod source to use Windows path, newline, XML writer, and stopwatch semantics.
/// Symbol-based matching leaves mod-defined types with the same names unchanged.
/// Bare members imported with <c>using static System.IO.Path</c> are qualified with the shim.
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

    private const string ToGameFqn = "global::ClientPlugin.Rewriter.WindowsPath.ToGame";

    // Only rewrite bare Path members the shim actually implements; unknown members
    // keep their original binding rather than turning into compile errors.
    private static readonly HashSet<string> WindowsPathMemberNames = new(
        typeof(WindowsPath)
            .GetMembers(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.Name)
    );

    // Path translation is injected at mod call sites so plugins and engine code
    // calling the same Mod API members keep receiving native paths.

    // Mod API members returning filesystem paths; reads are wrapped in FromGame.
    private static readonly Dictionary<string, HashSet<string>> EgressPathMembers = new()
    {
        ["global::VRage.Game.ModAPI.IMySession"] = new() { "CurrentPath", "ThumbPath" },
        ["global::VRage.Game.ModAPI.IMyGamePaths"] = new()
        {
            "ContentPath",
            "ModsPath",
            "UserDataPath",
            "SavesPath",
        },
        ["global::VRage.Game.ModAPI.IMyConfigDedicated"] = new()
        {
            "PremadeCheckpointPath",
            "GetFilePath",
        },
        ["global::VRage.Game.ModAPI.IMyModContext"] = new() { "ModPath", "ModPathData" },
        // Mods also reach the concrete context through MyDefinitionBase.Context.
        ["global::VRage.Game.MyModContext"] = new() { "ModPath", "ModPathData" },
        ["global::VRage.Game.ModAPI.IMyModel"] = new() { "AssetName" },
    };

    // Mod API calls whose listed parameters carry filesystem paths; those
    // arguments are wrapped in ToGame.
    private static readonly Dictionary<
        (string TypeFqn, string Method),
        int[]
    > IngressPathArguments = new()
    {
        [("global::VRage.Game.ModAPI.IMyUtilities", "ReadFileInModLocation")] = new[] { 0 },
        [("global::VRage.Game.ModAPI.IMyUtilities", "ReadBinaryFileInModLocation")] = new[] { 0 },
        [("global::VRage.Game.ModAPI.IMyUtilities", "FileExistsInModLocation")] = new[] { 0 },
        [("global::VRage.Game.ModAPI.IMyUtilities", "ReadFileInGameContent")] = new[] { 0 },
        [("global::VRage.Game.ModAPI.IMyUtilities", "ReadBinaryFileInGameContent")] = new[] { 0 },
        [("global::VRage.Game.ModAPI.IMyUtilities", "FileExistsInGameContent")] = new[] { 0 },
        [("global::VRage.Game.ModAPI.IMyConfigDedicated", "Load")] = new[] { 0 },
        [("global::VRage.Game.ModAPI.IMyConfigDedicated", "Save")] = new[] { 0 },
        [("global::VRage.Game.ModAPI.IMySession", "Save")] = new[] { 0 },
    };

    // Mod API property setters accepting filesystem paths; assigned values are
    // wrapped in ToGame.
    private static readonly Dictionary<string, HashSet<string>> IngressPathSetters = new()
    {
        ["global::VRage.Game.ModAPI.IMyConfigDedicated"] = new() { "PremadeCheckpointPath" },
    };

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

        // Mod API path getters; conditional access is wrapped at the enclosing node.
        if (
            _semanticModel.GetSymbolInfo(node).Symbol is IPropertySymbol pathProperty
            && IsEgressPathMember(pathProperty)
            && !IsAssignmentTarget(node)
            && !IsOnConditionalAccessSpine(node)
            && !IsInsideNameOf(node)
        )
        {
            RequiresShimReference = true;
            return WrapShimCall(FromGameFqn, rewritten);
        }

        return rewritten;
    }

    private bool IsEgressPathMember(ISymbol symbol)
    {
        if (symbol is not IPropertySymbol && symbol is not IMethodSymbol)
            return false;
        var containing = symbol.ContainingType;
        if (containing == null)
            return false;
        return EgressPathMembers.TryGetValue(
                containing.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                out var members
            ) && members.Contains(symbol.Name);
    }

    private static bool IsAssignmentTarget(SyntaxNode node) =>
        node.Parent is AssignmentExpressionSyntax assignment && assignment.Left == node;

    private static bool IsInsideNameOf(SyntaxNode node)
    {
        for (var parent = node.Parent; parent != null; parent = parent.Parent)
        {
            if (
                parent is InvocationExpressionSyntax invocation
                && invocation.Expression is IdentifierNameSyntax id
                && id.Identifier.ValueText == "nameof"
            )
                return true;
            if (parent is StatementSyntax || parent is MemberDeclarationSyntax)
                return false;
        }
        return false;
    }

    private static InvocationExpressionSyntax WrapShimCall(string shimFqn, ExpressionSyntax expr)
    {
        return SyntaxFactory
            .InvocationExpression(
                SyntaxFactory.ParseExpression(shimFqn),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(expr.WithoutTrivia())
                    )
                )
            )
            .WithLeadingTrivia(expr.GetLeadingTrivia())
            .WithTrailingTrivia(expr.GetTrailingTrivia());
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

        // Mod API calls taking path arguments get those arguments restored to
        // native form; argument wrapping is safe under conditional access too.
        rewritten = RewriteIngressPathArguments(node, rewritten);

        // Mod API path-returning calls (IMyConfigDedicated.GetFilePath).
        if (
            !IsOnConditionalAccessSpine(node)
            && _semanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol pathMethod
            && IsEgressPathMember(pathMethod)
        )
        {
            RequiresShimReference = true;
            return WrapShimCall(FromGameFqn, rewritten);
        }

        return rewritten;
    }

    private InvocationExpressionSyntax RewriteIngressPathArguments(
        InvocationExpressionSyntax node,
        InvocationExpressionSyntax rewritten
    )
    {
        // Bind the original node; parameter positions honor named arguments.
        if (_semanticModel.GetSymbolInfo(node).Symbol is not IMethodSymbol method)
            return rewritten;
        var containing = method.ContainingType;
        if (containing == null)
            return rewritten;
        if (
            !IngressPathArguments.TryGetValue(
                (containing.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), method.Name),
                out var pathParameters
            )
        )
            return rewritten;

        var replacements = new Dictionary<ArgumentSyntax, ArgumentSyntax>();
        var arguments = rewritten.ArgumentList.Arguments;
        for (int i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            int parameterIndex = i;
            if (argument.NameColon != null)
            {
                parameterIndex = -1;
                for (int p = 0; p < method.Parameters.Length; p++)
                {
                    if (method.Parameters[p].Name == argument.NameColon.Name.Identifier.ValueText)
                    {
                        parameterIndex = p;
                        break;
                    }
                }
            }

            if (System.Array.IndexOf(pathParameters, parameterIndex) >= 0)
                replacements[argument] = argument.WithExpression(
                    WrapShimCall(ToGameFqn, argument.Expression)
                );
        }

        if (replacements.Count == 0)
            return rewritten;

        RequiresShimReference = true;
        return rewritten.WithArgumentList(
            rewritten.ArgumentList.ReplaceNodes(
                replacements.Keys,
                (original, _) => replacements[original]
            )
        );
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
    /// Restores native form for values mods assign to path-typed Mod API setters.
    /// </summary>
    public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        var rewritten = (AssignmentExpressionSyntax)base.VisitAssignmentExpression(node);

        if (!node.IsKind(SyntaxKind.SimpleAssignmentExpression))
            return rewritten;

        if (
            _semanticModel.GetSymbolInfo(node.Left).Symbol is IPropertySymbol property
            && IsIngressPathSetter(property)
        )
        {
            RequiresShimReference = true;
            return rewritten.WithRight(WrapShimCall(ToGameFqn, rewritten.Right));
        }

        return rewritten;
    }

    private bool IsIngressPathSetter(IPropertySymbol property)
    {
        var containing = property.ContainingType;
        if (containing == null)
            return false;
        return IngressPathSetters.TryGetValue(
                containing.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                out var members
            ) && members.Contains(property.Name);
    }

    /// <summary>
    /// Detects nodes whose member-binding tokens are valid only inside an
    /// enclosing conditional-access expression. Contexts like arguments,
    /// interpolations, and lambdas sever the spine: a node inside them cannot
    /// carry the enclosing conditional's member bindings, so wrapping it
    /// in place stays valid.
    /// </summary>
    private static bool IsOnConditionalAccessSpine(SyntaxNode node)
    {
        for (var parent = node.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is ConditionalAccessExpressionSyntax)
                return true;
            if (
                parent is ArgumentSyntax
                || parent is InterpolationSyntax
                || parent is AnonymousFunctionExpressionSyntax
                || parent is EqualsValueClauseSyntax
                || parent is InitializerExpressionSyntax
                || parent is StatementSyntax
                || parent is MemberDeclarationSyntax
            )
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

        // Wrap outermost conditional chains ending in a Mod API path member;
        // member bindings must stay inside their conditional, so the whole
        // chain becomes the FromGame argument (FromGame passes null through).
        if (node.Parent is not ConditionalAccessExpressionSyntax)
        {
            var tail = node.WhenNotNull;
            while (tail is ConditionalAccessExpressionSyntax inner)
                tail = inner.WhenNotNull;

            var tailSymbol = _semanticModel.GetSymbolInfo(tail).Symbol;
            if (tailSymbol != null && IsEgressPathMember(tailSymbol))
            {
                RequiresShimReference = true;
                return WrapShimCall(FromGameFqn, rewritten);
            }
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

        // Bare Path members imported via `using static System.IO.Path`.
        var bareMember = TryGetBarePathMemberSubstitution(node);
        if (bareMember != null)
        {
            RequiresShimReference = true;
            return bareMember
                .WithLeadingTrivia(node.GetLeadingTrivia())
                .WithTrailingTrivia(node.GetTrailingTrivia());
        }

        return base.VisitIdentifierName(node);
    }

    /// <summary>
    /// Qualifies bare <c>using static System.IO.Path</c> member references with the
    /// shim type so they pick up Windows semantics like qualified calls do.
    /// </summary>
    private SyntaxNode TryGetBarePathMemberSubstitution(IdentifierNameSyntax node)
    {
        if (!WindowsPathMemberNames.Contains(node.Identifier.ValueText))
            return null;

        var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
        var isStaticPathMember = symbol switch
        {
            IMethodSymbol m => m.IsStatic,
            IFieldSymbol f => f.IsStatic,
            IPropertySymbol p => p.IsStatic,
            _ => false,
        };
        if (!isStaticPathMember)
            return null;

        var containing = symbol.ContainingType;
        if (containing == null)
            return null;
        if (containing.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != SystemIoPathFqn)
            return null;

        return SyntaxFactory.ParseExpression(ReplacementFqn + "." + node.Identifier.ValueText);
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

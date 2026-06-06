using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ServerPlugin.Rewriter;

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
///   global::ServerPlugin.Rewriter.WindowsPath.Combine(...)
///   global::ServerPlugin.Rewriter.WindowsPath.DirectorySeparatorChar
///   typeof(global::ServerPlugin.Rewriter.WindowsPath)
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
/// References to <c>System.Environment.NewLine</c> are replaced with the
/// string literal <c>"\r\n"</c> so mods see the Windows line terminator
/// (length 2) instead of Linux <c>"\n"</c> (length 1). This is a
/// constant-fold, not a redirection to a property, so a mod that hashes or
/// measures <c>Environment.NewLine</c> sees the same value it would on
/// Windows.
///
/// Constant-folding the source-level reference is not enough on its own: BCL
/// methods like <c>StringBuilder.AppendLine(...)</c> and
/// <c>TextWriter.WriteLine(...)</c> read <c>Environment.NewLine</c> from
/// inside <c>System.Private.CoreLib</c>, where the rewriter cannot reach.
/// Two additional substitutions close that gap at every call site visible
/// to mod source:
/// <list type="bullet">
///   <item><c>sb.AppendLine()</c> → <c>sb.Append("\r\n")</c>;
///         <c>sb.AppendLine(x)</c> → <c>sb.Append(x).Append("\r\n")</c>.
///         <c>StringBuilder.Append</c> returns the same builder so the
///         receiver expression is evaluated exactly once, matching the
///         original semantics.</item>
///   <item><c>writer.WriteLine(args...)</c> →
///         <c>WindowsTextWriter.WriteLine(writer, args...)</c>. The helper
///         calls <c>Write(value)</c> then <c>Write("\r\n")</c> on the same
///         writer; we route through a helper rather than inline because
///         <c>WriteLine</c> returns <c>void</c> and cannot be chained.</item>
/// </list>
/// We rewrite only when the bound method symbol is declared on
/// <c>System.Text.StringBuilder</c> / <c>System.IO.TextWriter</c>, so a
/// mod-defined <c>AppendLine</c>/<c>WriteLine</c> on a custom type is
/// unaffected. A <em>runtime</em> patch on <see cref="System.Environment.NewLine"/>
/// itself is intentionally avoided — it would also affect engine and BCL
/// callers that legitimately want the host's native newline, conflating
/// mod-visible behaviour with internal behaviour (the same trap as
/// postfixing a public getter to mimic an explicit interface implementation).
///
/// References to <see cref="System.Diagnostics.Stopwatch"/> are substituted
/// with <see cref="WindowsStopwatch"/> wholesale — every syntactic position
/// (type-of, <c>new</c>, variable / parameter / field / return-type
/// declarations, generic arguments, array element types, member access,
/// casts, …). The substitution rides on <c>VisitIdentifierName</c> and
/// <c>VisitQualifiedName</c> filtered by
/// <see cref="Microsoft.CodeAnalysis.INamedTypeSymbol"/> identity, so it
/// catches every form a mod might write (<c>Stopwatch</c>,
/// <c>System.Diagnostics.Stopwatch</c>, <c>List&lt;Stopwatch&gt;</c>,
/// etc.) without enumerating positions. Symbol-identity filtering keeps a
/// mod-defined type also named <c>Stopwatch</c> unaffected. Why a full
/// type-replacement rather than per-member rewrites: a mod that allocates
/// the type and then reads <c>ElapsedTicks</c> needs the instance itself
/// to know about the Windows tick scale; routing through helpers would
/// require rewrites at every read site and break <c>StartNew()</c>'s
/// return-type contract.
///
/// Object-creation expressions of <see cref="System.Xml.XmlWriterSettings"/>
/// get a synthesized <c>NewLineChars = "\r\n"</c> assignment injected into
/// their initializer list (existing list is preserved; a missing list is
/// synthesized; an explicit <c>NewLineChars = ...</c> in the source is
/// left intact). This is the same fix as the
/// <c>StringBuilder.AppendLine</c> / <c>TextWriter.WriteLine</c> rewrites
/// applied at the call-site level: it forces the Windows-shape line
/// terminator on a BCL surface mods reach into. The runtime alternative
/// (Harmony-patching the <c>XmlWriterSettings</c> constructor) would
/// touch engine and BCL callers too, which we don't want.
///
/// Limitations: <c>using static System.IO.Path;</c> followed by a bare
/// <c>Combine(...)</c> call is not rewritten. In practice mods qualify with
/// <c>Path.</c> or <c>System.IO.Path.</c>, which this pass handles. Other
/// BCL writers that internally consume <c>Environment.NewLine</c>
/// (<c>File.WriteAllLines</c>, ...) are not yet redirected; add similar
/// call-site rewrites if a real mod exercises them.
/// </summary>
internal sealed class PathSubstitutionRewriter : CSharpSyntaxRewriter
{
    private const string SystemIoPathFqn = "global::System.IO.Path";
    private const string ReplacementFqn = "global::ServerPlugin.Rewriter.WindowsPath";
    private const string FromGameFqn = "global::ServerPlugin.Rewriter.WindowsPath.FromGame";
    private const string ModItemFqn = "global::VRage.Game.MyObjectBuilder_Checkpoint.ModItem";
    private const string EnvironmentFqn = "global::System.Environment";
    private const string StringBuilderFqn = "global::System.Text.StringBuilder";
    private const string TextWriterFqn = "global::System.IO.TextWriter";
    private const string WindowsTextWriterWriteLineFqn = "global::ServerPlugin.Rewriter.WindowsTextWriter.WriteLine";
    private const string StopwatchFqn = "global::System.Diagnostics.Stopwatch";
    private const string WindowsStopwatchFqn = "global::ServerPlugin.Rewriter.WindowsStopwatch";
    private const string XmlWriterSettingsFqn = "global::System.Xml.XmlWriterSettings";

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

        // `System.Diagnostics.Stopwatch` in expression context parses as a
        // MemberAccessExpression chain (not a QualifiedNameSyntax). When the
        // whole chain binds to the Stopwatch type, substitute it here as a
        // single unit. VisitIdentifierName cannot substitute the leaf
        // `Stopwatch` because that identifier sits in its parent's .Name
        // slot, which is typed SimpleNameSyntax — replacing it with the
        // QualifiedNameSyntax produced by ParseName(WindowsStopwatchFqn)
        // would make the base CSharpSyntaxRewriter throw InvalidCastException
        // on the parent's hard cast (`(SimpleNameSyntax)Visit(node.Name)`).
        // Repro before this fix: Nanobot Build and Repair System
        // (Steam:2111073562) crashed mod compile with exactly that exception.
        if (IsNamedTypeReference(node, StopwatchFqn))
        {
            return SyntaxFactory.ParseName(WindowsStopwatchFqn)
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
        // call here and route through WindowsPath.FromGame.
        //
        // The rewrite extracts the GetPath receiver and passes it to the
        // ModItem-typed overload: `receiver.GetPath()` → `FromGame(receiver)`.
        // Constructing the call this way (rather than wrapping the whole
        // invocation) preserves the original tree shape and lets the
        // conditional-access form `x?.ModItem.GetPath()` be rewritten cleanly
        // by VisitConditionalAccessExpression — wrapping the inner invocation
        // would orphan the `.` member-binding from its `?`, crashing the
        // Roslyn binder in BindMemberBindingExpression.
        var rewritten = (InvocationExpressionSyntax)base.VisitInvocationExpression(node);

        // Skip if the GetPath call sits on the WhenNotNull spine of a `?.`
        // chain: the receiver expression starts with a MemberBindingExpression
        // that only exists inside its CAE, so we can't lift it into an
        // argument list. The CAE handler peels `.GetPath()` off the spine
        // instead.
        if (IsModItemGetPath(node) && !IsOnConditionalAccessSpine(node))
        {
            var receiver = TryGetGetPathReceiver(rewritten);
            if (receiver != null)
            {
                return SyntaxFactory.InvocationExpression(
                        SyntaxFactory.ParseExpression(FromGameFqn),
                        SyntaxFactory.ArgumentList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Argument(receiver.WithoutTrivia()))))
                    .WithLeadingTrivia(rewritten.GetLeadingTrivia())
                    .WithTrailingTrivia(rewritten.GetTrailingTrivia());
            }
        }

        if (IsStringBuilderAppendLine(node))
            return RewriteStringBuilderAppendLine(rewritten) ?? (SyntaxNode)rewritten;

        if (IsTextWriterWriteLine(node))
            return RewriteTextWriterWriteLine(rewritten) ?? (SyntaxNode)rewritten;

        return rewritten;
    }

    private bool IsStringBuilderAppendLine(InvocationExpressionSyntax node)
    {
        // Bind original (pre-rewrite) node — see VisitMemberAccessExpression.
        if (_semanticModel.GetSymbolInfo(node).Symbol is not IMethodSymbol method)
            return false;
        if (method.Name != "AppendLine")
            return false;
        var containing = method.ContainingType;
        if (containing == null)
            return false;
        // Match by declaring type so a mod-defined AppendLine on some other
        // type is not redirected. StringBuilder.AppendLine is sealed-ish (not
        // virtual), so the symbol's ContainingType is always StringBuilder.
        return containing.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == StringBuilderFqn;
    }

    private SyntaxNode RewriteStringBuilderAppendLine(InvocationExpressionSyntax rewritten)
    {
        // Source form must be `<receiver>.AppendLine(...)`. Other invocation
        // shapes (e.g. AppendLine(...) via using-static) aren't supported by
        // the rest of the rewriter either, so bail.
        if (rewritten.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        var receiver = memberAccess.Expression;
        var args = rewritten.ArgumentList.Arguments;

        var crlfArg = SyntaxFactory.Argument(
            SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal("\r\n")));

        // sb.AppendLine() → sb.Append("\r\n")
        if (args.Count == 0)
        {
            return SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        receiver,
                        SyntaxFactory.IdentifierName("Append")),
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(crlfArg)))
                .WithLeadingTrivia(rewritten.GetLeadingTrivia())
                .WithTrailingTrivia(rewritten.GetTrailingTrivia());
        }

        // sb.AppendLine(x) → sb.Append(x).Append("\r\n").
        // Receiver appears in the rewritten tree exactly once, so it's
        // evaluated once — same as the original AppendLine call.
        if (args.Count == 1)
        {
            var inner = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    receiver,
                    SyntaxFactory.IdentifierName("Append")),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(args[0])));

            return SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        inner,
                        SyntaxFactory.IdentifierName("Append")),
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(crlfArg)))
                .WithLeadingTrivia(rewritten.GetLeadingTrivia())
                .WithTrailingTrivia(rewritten.GetTrailingTrivia());
        }

        // Future StringBuilder.AppendLine overloads (e.g. the interpolated
        // string handler ones in .NET 6+) — leave as-is. In-game C# 6 syntax
        // won't produce them, but a future compiler upgrade might.
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
        // Exact match on TextWriter: a subclass that overrides WriteLine
        // resolves to the override's declaring type and we leave it alone
        // (the override decides what to write). For non-overridden inheritance
        // (e.g. StringWriter, the engine's WriteFileInLocalStorage writer)
        // Roslyn returns the base declaration here, which we want to redirect.
        return containing.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == TextWriterFqn;
    }

    private SyntaxNode RewriteTextWriterWriteLine(InvocationExpressionSyntax rewritten)
    {
        if (rewritten.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        var receiver = memberAccess.Expression;

        // writer.WriteLine(args...) → WindowsTextWriter.WriteLine(writer, args...).
        // Roslyn picks the matching overload from WindowsTextWriter at the
        // mod-compile-time CreateCompilation; the helper's overload set
        // mirrors System.IO.TextWriter.WriteLine.
        var newArgs = SyntaxFactory.SeparatedList(
            new[] { SyntaxFactory.Argument(receiver.WithoutTrivia()) }
                .Concat(rewritten.ArgumentList.Arguments));

        return SyntaxFactory.InvocationExpression(
                SyntaxFactory.ParseExpression(WindowsTextWriterWriteLineFqn),
                SyntaxFactory.ArgumentList(newArgs))
            .WithLeadingTrivia(rewritten.GetLeadingTrivia())
            .WithTrailingTrivia(rewritten.GetTrailingTrivia());
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

    /// <summary>
    /// Whether <paramref name="node"/> sits on the
    /// <see cref="ConditionalAccessExpressionSyntax.WhenNotNull"/> spine of an
    /// enclosing <c>?.</c> expression. Member-binding tokens (<c>.X</c>) only
    /// exist on this spine; lifting a node off it into an argument list
    /// strands those tokens and crashes
    /// <c>BindMemberBindingExpression</c>. The invocation handler defers to
    /// <see cref="VisitConditionalAccessExpression"/> in that case so the
    /// rewrite can be performed at the CAE level instead.
    /// </summary>
    private static bool IsOnConditionalAccessSpine(SyntaxNode node)
    {
        for (var parent = node.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is ConditionalAccessExpressionSyntax)
                return true;
            // Stop climbing at statement / member boundaries — a conditional
            // access can only enclose us through expression-shaped ancestors.
            if (parent is StatementSyntax || parent is MemberDeclarationSyntax)
                return false;
        }
        return false;
    }

    /// <summary>
    /// For an invocation of the shape <c>receiver.GetPath()</c>, return the
    /// <c>receiver</c> expression so it can be passed as the argument of a
    /// <see cref="WindowsPath.FromGame(VRage.Game.MyObjectBuilder_Checkpoint.ModItem)"/>
    /// call. Returns <c>null</c> for unsupported shapes (e.g. an invocation
    /// via a using-static import, where there is no receiver to extract).
    /// </summary>
    private static ExpressionSyntax TryGetGetPathReceiver(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            return memberAccess.Expression;
        return null;
    }

    /// <summary>
    /// Rewrite <c>x?.ModItem.GetPath()</c> as
    /// <c>WindowsPath.FromGame(x?.ModItem)</c> by peeling <c>.GetPath()</c>
    /// off the WhenNotNull spine. Removing the trailing invocation makes the
    /// CAE evaluate to <c>ModItem?</c> (lifted because <c>ModItem</c> is a
    /// struct), which feeds the
    /// <see cref="WindowsPath.FromGame(VRage.Game.MyObjectBuilder_Checkpoint.ModItem?)"/>
    /// overload — preserving the original null-propagation semantics.
    ///
    /// Wrapping the inner invocation (the natural place for the rewrite) is
    /// not viable: the WhenNotNull's leftmost token is a member-binding
    /// (<c>.ModItem</c>) that only exists relative to its <c>?</c>. Pulling
    /// the invocation into an argument list strands that token and crashes
    /// <c>BindMemberBindingExpression</c>. The invocation handler detects
    /// this case via <see cref="IsOnConditionalAccessSpine"/> and defers
    /// here.
    ///
    /// Only the immediate "GetPath() is the whole WhenNotNull" shape is
    /// handled. Further chaining (<c>x?.ModItem.GetPath().Foo(...)</c>) is
    /// left un-translated — peeling would change the chained method's
    /// receiver type from <c>string</c> to <c>ModItem?</c>.
    /// </summary>
    public override SyntaxNode VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
    {
        var rewritten = (ConditionalAccessExpressionSyntax)base.VisitConditionalAccessExpression(node);

        if (node.WhenNotNull is InvocationExpressionSyntax tailInvocation
            && IsModItemGetPath(tailInvocation)
            && tailInvocation.Expression is MemberAccessExpressionSyntax
            && rewritten.WhenNotNull is InvocationExpressionSyntax rewrittenTail
            && rewrittenTail.Expression is MemberAccessExpressionSyntax rewrittenAccess)
        {
            // Replace the WhenNotNull spine `<receiver>.GetPath()` with just
            // `<receiver>` — keeping the rewritten copy so any nested
            // substitutions visited under the receiver are preserved.
            var peeled = rewritten.WithWhenNotNull(rewrittenAccess.Expression);

            return SyntaxFactory.InvocationExpression(
                    SyntaxFactory.ParseExpression(FromGameFqn),
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(peeled.WithoutTrivia()))))
                .WithLeadingTrivia(rewritten.GetLeadingTrivia())
                .WithTrailingTrivia(rewritten.GetTrailingTrivia());
        }

        return rewritten;
    }

    /// <summary>
    /// Inject <c>NewLineChars = "\r\n"</c> into every
    /// <c>new XmlWriterSettings { ... }</c> in mod source. Mirrors
    /// .NET Framework's Windows default — on Linux .NET 10 the
    /// property defaults to <c>"\n"</c> (it copies
    /// <see cref="System.Environment.NewLine"/> at constructor time),
    /// which would let a mod-built XML payload diverge from its
    /// Windows-baked reference. The injection is skipped when the
    /// source already assigns <c>NewLineChars</c> explicitly so a mod
    /// that opts in to <c>"\n"</c> (or anything else) is respected.
    /// Symbol-identity filtering: a mod-defined type also named
    /// <c>XmlWriterSettings</c> in another namespace is untouched.
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
                SyntaxFactory.Literal("\r\n")));

        var existingInit = rewritten.Initializer;
        if (existingInit == null)
        {
            var newInit = SyntaxFactory.InitializerExpression(
                SyntaxKind.ObjectInitializerExpression,
                SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(crlfAssignment));
            return rewritten.WithInitializer(newInit);
        }

        // Don't double up if the mod already set the property — respect
        // the explicit choice. Match on the property name lexically;
        // the initializer's left-hand side is always a SimpleNameSyntax.
        foreach (var expr in existingInit.Expressions)
        {
            if (expr is AssignmentExpressionSyntax asn &&
                asn.Left is IdentifierNameSyntax id &&
                id.Identifier.ValueText == "NewLineChars")
                return rewritten;
        }

        var augmented = existingInit.WithExpressions(
            existingInit.Expressions.Add(crlfAssignment));
        return rewritten.WithInitializer(augmented);
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

    private bool IsSystemIoPathTypeReference(SyntaxNode expression) =>
        IsNamedTypeReference(expression, SystemIoPathFqn);

    /// <summary>
    /// True if <paramref name="expression"/> is a syntactic *type* reference
    /// (the node itself binds to a named type), and that type's fully
    /// qualified name matches <paramref name="fullyQualifiedName"/>.
    /// Rejects instance expressions whose runtime type happens to be the
    /// named type — only the type-of-reference position is matched.
    /// </summary>
    private bool IsNamedTypeReference(SyntaxNode expression, string fullyQualifiedName)
    {
        var symbol = _semanticModel.GetSymbolInfo(expression).Symbol;
        if (symbol is not INamedTypeSymbol named)
            return false;
        return named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == fullyQualifiedName;
    }

    // ─── Generic type-name substitution (Stopwatch → WindowsStopwatch) ────
    //
    // These two overrides catch every syntactic position a type can occupy
    // (new, typeof, variable / parameter / field / method-return /
    // generic-arg / array-element / cast / member-access prefix) via the
    // natural tree recursion. The filter is the bound symbol's identity,
    // so a mod-defined type that shadows the name is untouched.
    //
    // For QualifiedName (e.g. `System.Diagnostics.Stopwatch`) we substitute
    // the whole qualified path as a unit and DO NOT recurse — the inner
    // IdentifierName would otherwise also bind to the same type symbol and
    // try to substitute itself, corrupting the qualifier.

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
    {
        // Skip identifiers occupying a slot that Roslyn types as
        // SimpleNameSyntax. The substitution returns ParseName(...) — a
        // QualifiedNameSyntax — which derives from NameSyntax but NOT from
        // SimpleNameSyntax, so returning it from any of these slots makes
        // the base CSharpSyntaxRewriter throw InvalidCastException when it
        // revisits the parent.
        //
        // The MemberAccess case is the one that bit Nanobot Build and
        // Repair System (Steam:2111073562) — `System.Diagnostics.Stopwatch
        // .GetTimestamp()` puts the leaf `Stopwatch` in the .Name slot of
        // an inner MemberAccessExpression. VisitMemberAccessExpression now
        // catches that chain as a whole.
        //
        // QualifiedName / AliasQualifiedName: also covered for the same
        // structural reason — Right / Name are typed SimpleNameSyntax.
        // VisitQualifiedName substitutes the whole qualified path at the
        // QualifiedName level, so skipping the leaf there is correct.
        if (IsInSimpleNameSlot(node))
            return base.VisitIdentifierName(node);

        var replacement = TryGetTypeSubstitution(node, node.Identifier.ValueText);
        if (replacement != null)
            return replacement
                .WithLeadingTrivia(node.GetLeadingTrivia())
                .WithTrailingTrivia(node.GetTrailingTrivia());

        return base.VisitIdentifierName(node);
    }

    /// <summary>
    /// True if <paramref name="node"/> occupies a syntactic slot Roslyn
    /// declares as <see cref="SimpleNameSyntax"/>. Returning anything other
    /// than a SimpleNameSyntax in such a slot makes the base
    /// <see cref="CSharpSyntaxRewriter"/> throw on its hard-cast of the
    /// child it just visited.
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
            return replacement
                .WithLeadingTrivia(node.GetLeadingTrivia())
                .WithTrailingTrivia(node.GetTrailingTrivia());

        return base.VisitQualifiedName(node);
    }

    private SyntaxNode TryGetTypeSubstitution(SyntaxNode node, string simpleName)
    {
        // Cheap text gate before the expensive semantic-model lookup —
        // VisitIdentifierName fires on every identifier in the source.
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

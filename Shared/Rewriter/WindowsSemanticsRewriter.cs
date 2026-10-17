using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ClientPlugin.Patches.PathHandling;
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
    private const string PathUtilsFqn = "global::VRage.Library.Utils.PathUtils";
    private const string WindowsPathUtilsFqn = "global::ClientPlugin.Rewriter.WindowsPathUtils";
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
    private const string FromGameTextFqn = "global::ClientPlugin.Rewriter.WindowsPath.FromGameText";
    private const string ModExtensionsNamespace = "global::ClientPlugin.Rewriter.ModExtensions";
    private const string FromGameExtension = nameof(
        ModExtensions.LinuxCompatPathExtensions.LinuxCompatFromGame
    );
    private const string FromGameTextExtension = nameof(
        ModExtensions.LinuxCompatPathExtensions.LinuxCompatFromGameText
    );
    private const string DefinitionBaseFqn = "global::VRage.Game.MyDefinitionBase";
    private const string DefinitionBuilderFqn = "global::VRage.Game.MyObjectBuilder_DefinitionBase";
    private const string ToGameTextFqn = "global::ClientPlugin.Rewriter.WindowsPath.ToGameText";
    private const string CubeBlockFqn = "global::VRage.Game.ModAPI.IMyCubeBlock";
    private const string CalcLocalMatrixExtension = nameof(
        ModExtensions.LinuxCompatPathExtensions.LinuxCompatCalcLocalMatrix
    );
    private const string ScrubFqn = "global::ClientPlugin.Rewriter.WindowsException.Scrub";

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
        ["global::VRage.Game.ModAPI.IMyModContext"] = new() { "ModPath", "ModPathData" },
        // Mods also reach the concrete context through MyDefinitionBase.Context.
        ["global::VRage.Game.MyModContext"] = new() { "ModPath", "ModPathData" },
        ["global::VRage.Game.ModAPI.IMyModel"] = new() { "AssetName" },
        ["global::VRage.Utils.MyLog"] = new() { "GetFilePath" },
        // Read through a lambda mid-chain in a conditional access; elsewhere VisitInvocationExpression
        // passes the struct itself to FromGame(ModItem).
        ["global::VRage.Game.MyObjectBuilder_Checkpoint.ModItem"] = new() { "GetPath" },
        ["global::VRage.Game.ModAPI.IMyConfigDedicated"] = new()
        {
            "PremadeCheckpointPath",
            "GetFilePath",
            "LoadWorld",
            "Plugins",
        },
        // The exceptions of a parallel task reach the mod without a catch block.
        ["global::ParallelTasks.Task"] = new() { "Exceptions" },
        ["global::Sandbox.Game.MyVisualScriptLogicProvider"] = new()
        {
            "GetCurrentSessionPath",
            "GetContentPath",
            "GetSavesPath",
            "GetModsPath",
        },
    };

    // Names to check before binding a bare identifier.
    private static readonly HashSet<string> EgressMemberNames = new(
        EgressPathMembers.Values.SelectMany(names => names)
    );

    // Strings that may hold or embed native paths, besides every string and string[] member of
    // a definition (paths of mod assets are absolute there); reads are wrapped in FromGameText.
    private static readonly Dictionary<string, HashSet<string>> EgressTextMembers = new()
    {
        ["global::VRage.Game.MyModContext"] = new() { "CurrentFile" },
        ["global::VRage.Game.MyDefinitionErrors.Error"] = new() { "ErrorFile", "Message" },
        ["global::VRage.Game.ObjectBuilders.Definitions.MyObjectBuilder_GuiTexture"] = new()
        {
            "Path",
        },
        // IMySession.GetCheckpoint and GetWorld; also what a mod reads from a save file.
        ["global::VRage.Game.MyObjectBuilder_Checkpoint"] = new()
        {
            "CustomSkybox",
            "CustomLoadingScreenImage",
            "VicinityModelsCache",
            "VicinityArmorModelsCache",
        },
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
        [("global::VRage.Utils.MyLog", "Init")] = new[] { 0 },
        [("global::Sandbox.Game.MyVisualScriptLogicProvider", "TakeScreenshot")] = new[] { 0 },
        [("global::Sandbox.Game.MyVisualScriptLogicProvider", "SetCustomSkybox")] = new[] { 0 },
        [("global::Sandbox.Game.MyVisualScriptLogicProvider", "SetCustomLoadingScreenImage")] =
            new[] { 0 },
        [("global::Sandbox.Game.MyVisualScriptLogicProvider", "SessionReloadDialog")] = new[] { 2 },
    };

    // Mod API property setters accepting filesystem paths; assigned values are
    // wrapped in ToGame.
    private static readonly Dictionary<string, HashSet<string>> IngressPathSetters = new()
    {
        ["global::VRage.Game.ModAPI.IMyConfigDedicated"] = new()
        {
            "PremadeCheckpointPath",
            "LoadWorld",
            "Plugins",
        },
    };

    private readonly SemanticModel _semanticModel;

    internal bool RequiresShimReference { get; private set; }

    // Set when a rewritten read needs the extension methods of ModExtensionsNamespace.
    private bool RequiresExtensionsUsing { get; set; }

    public WindowsSemanticsRewriter(SemanticModel semanticModel)
    {
        _semanticModel = semanticModel;
    }

    public override SyntaxNode VisitCompilationUnit(CompilationUnitSyntax node)
    {
        var rewritten = (CompilationUnitSyntax)base.VisitCompilationUnit(node);
        if (!RequiresExtensionsUsing)
            return rewritten;
        return rewritten.AddUsings(
            SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(ModExtensionsNamespace))
        );
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

        // PathUtils takes and returns filesystem paths; the shim translates both ways.
        if (IsNamedTypeReference(node.Expression, PathUtilsFqn))
        {
            RequiresShimReference = true;
            var newType = SyntaxFactory
                .ParseName(WindowsPathUtilsFqn)
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

        // Mod API path getters and definition strings. Method calls are handled by
        // VisitInvocationExpression, a conditional access tail by the conditional itself.
        var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
        if (symbol is not IMethodSymbol && !IsWriteTarget(node) && !IsInsideNameOf(node))
            return WrapEgress(node, rewritten, EgressOf(symbol)) ?? rewritten;

        return WrapMethodGroup(node, rewritten, symbol) ?? rewritten;
    }

    /// <summary>
    /// A path getter converted to a delegate, like <c>Func&lt;string&gt; f = MyLog.Default.GetFilePath;</c>,
    /// becomes <c>FromGame(new Func&lt;string&gt;(MyLog.Default.GetFilePath))</c>, wrapped in a
    /// <c>new D(....Invoke)</c> when the target is another delegate type.
    /// </summary>
    private ExpressionSyntax WrapMethodGroup(
        ExpressionSyntax node,
        ExpressionSyntax rewritten,
        ISymbol symbol
    )
    {
        if (
            symbol
                is not IMethodSymbol
                {
                    Parameters.Length: 0,
                    ReturnType.SpecialType: SpecialType.System_String,
                }
            || node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node
            || IsInsideNameOf(node)
            || EgressOf(symbol) != Egress.Path
        )
            return null;

        RequiresShimReference = true;
        const string func = "global::System.Func<string>";
        var read = SyntaxFactory
            .ObjectCreationExpression(SyntaxFactory.ParseTypeName(func))
            .WithArgumentList(
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(rewritten.WithoutTrivia())
                    )
                )
            );
        ExpressionSyntax wrapped = WrapShimCall(FromGameFqn, read);

        var target = _semanticModel.GetTypeInfo(node).ConvertedType;
        if (target is INamedTypeSymbol { TypeKind: TypeKind.Delegate } named)
        {
            var name = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (name != func)
            {
                wrapped = SyntaxFactory
                    .ObjectCreationExpression(SyntaxFactory.ParseTypeName(name))
                    .WithArgumentList(
                        SyntaxFactory.ArgumentList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Argument(
                                    SyntaxFactory.MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        wrapped,
                                        SyntaxFactory.IdentifierName("Invoke")
                                    )
                                )
                            )
                        )
                    );
            }
        }

        return wrapped
            .WithLeadingTrivia(rewritten.GetLeadingTrivia())
            .WithTrailingTrivia(rewritten.GetTrailingTrivia());
    }

    /// <summary>A member read inside a conditional access chain, like <c>a?.GamePaths.UserDataPath</c>.</summary>
    public override SyntaxNode VisitMemberBindingExpression(MemberBindingExpressionSyntax node)
    {
        var rewritten = (MemberBindingExpressionSyntax)base.VisitMemberBindingExpression(node);
        var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
        if (symbol is IMethodSymbol || IsInsideNameOf(node))
            return rewritten;
        return WrapEgress(node, rewritten, EgressOf(symbol)) ?? rewritten;
    }

    private enum Egress
    {
        None,

        // A filesystem path: FromGame.
        Path,

        // A string that may hold or embed a native path: FromGameText.
        Text,
    }

    /// <summary>
    /// Translates the value of an egress member read or call. Returns null when the node is no
    /// egress, or when it is the tail of a conditional access, which the conditional wraps whole.
    /// </summary>
    private ExpressionSyntax WrapEgress(
        ExpressionSyntax node,
        ExpressionSyntax rewritten,
        Egress egress
    )
    {
        if (egress == Egress.None || IsConditionalTail(node))
            return null;

        if (!IsOnConditionalAccessSpine(node))
        {
            RequiresShimReference = true;
            return WrapShimCall(egress == Egress.Path ? FromGameFqn : FromGameTextFqn, rewritten);
        }

        // Mid-chain in a conditional access the member binding cannot leave the chain, so the
        // read moves into a lambda of an extension call: a?.B.C.D() -> a?.B.LinuxCompatFromGame(o => o.C).D()
        const string owner = "__linuxCompatOwner";
        ExpressionSyntax receiver;
        ExpressionSyntax read;
        switch (rewritten)
        {
            case MemberAccessExpressionSyntax access:
                receiver = access.Expression;
                read = access.WithExpression(SyntaxFactory.IdentifierName(owner));
                break;
            case MemberBindingExpressionSyntax binding:
                receiver = null;
                read = SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(owner),
                    binding.Name
                );
                break;
            case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access }:
                receiver = access.Expression;
                read = ((InvocationExpressionSyntax)rewritten).WithExpression(
                    access.WithExpression(SyntaxFactory.IdentifierName(owner))
                );
                break;
            case InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax binding }:
                receiver = null;
                read = ((InvocationExpressionSyntax)rewritten).WithExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName(owner),
                        binding.Name
                    )
                );
                break;
            default:
                return null;
        }

        RequiresShimReference = true;
        RequiresExtensionsUsing = true;
        var extension = SyntaxFactory.IdentifierName(
            egress == Egress.Path ? FromGameExtension : FromGameTextExtension
        );
        ExpressionSyntax target =
            receiver == null
                ? SyntaxFactory.MemberBindingExpression(extension)
                : SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    receiver,
                    extension
                );
        var lambda = SyntaxFactory.SimpleLambdaExpression(
            SyntaxFactory.Parameter(SyntaxFactory.Identifier(owner)),
            read.WithoutTrivia()
        );
        return SyntaxFactory
            .InvocationExpression(
                target,
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(lambda))
                )
            )
            .WithLeadingTrivia(rewritten.GetLeadingTrivia())
            .WithTrailingTrivia(rewritten.GetTrailingTrivia());
    }

    private Egress EgressOf(ISymbol symbol)
    {
        // A constant may sit where only constants are allowed, such as a case label.
        if (
            symbol
            is not IPropertySymbol
                and not IFieldSymbol { IsConst: false }
                and not IMethodSymbol
        )
            return Egress.None;
        var containing = symbol.ContainingType;
        if (containing == null)
            return Egress.None;
        var fqn = containing.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (EgressPathMembers.TryGetValue(fqn, out var paths) && paths.Contains(symbol.Name))
            return Egress.Path;
        if (EgressTextMembers.TryGetValue(fqn, out var texts) && texts.Contains(symbol.Name))
            return Egress.Text;

        // def.GetObjectBuilder() carries the absolute paths of the definition's assets.
        if (
            symbol is IMethodSymbol { Name: "GetObjectBuilder", Parameters.Length: 0 } method
            && method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                == DefinitionBuilderFqn
            && IsInDefinition(containing)
        )
            return Egress.Path;

        var type = symbol switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => null,
        };
        return IsText(type) && IsInDefinition(containing) ? Egress.Text : Egress.None;
    }

    private static bool IsText(ITypeSymbol type) =>
        type?.SpecialType == SpecialType.System_String
        || (
            type is IArrayTypeSymbol array
            && array.ElementType.SpecialType == SpecialType.System_String
        );

    // A definition, or a type nested in one, such as MyCubeBlockDefinition.BuildProgressModel.
    private static bool IsInDefinition(INamedTypeSymbol type)
    {
        for (var outer = type; outer != null; outer = outer.ContainingType)
        {
            for (var t = outer; t != null; t = t.BaseType)
            {
                if (
                    t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == DefinitionBaseFqn
                )
                    return true;
            }
        }
        return false;
    }

    // The last link of a conditional access chain, wrapped whole by VisitConditionalAccessExpression.
    private static bool IsConditionalTail(SyntaxNode node) =>
        node.Parent is ConditionalAccessExpressionSyntax conditional
        && conditional.WhenNotNull == node;

    private static bool IsAssignmentTarget(SyntaxNode node) =>
        node.Parent is AssignmentExpressionSyntax assignment && assignment.Left == node;

    /// <summary>
    /// A member written to rather than read: assigned, passed by reference, or an array whose
    /// element is assigned (<c>def.Icons[0] = ...</c>).
    /// </summary>
    private static bool IsWriteTarget(SyntaxNode node) =>
        IsAssignmentTarget(node)
        || node.Parent is ArgumentSyntax { RefKindKeyword.RawKind: not (int)SyntaxKind.None }
        || node.Parent is PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax
        || (
            node.Parent is ElementAccessExpressionSyntax element
            && element.Expression == node
            && IsWriteTarget(element)
        );

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

        var outPath = RewriteCalcLocalMatrix(node, rewritten);
        if (outPath != null)
            return outPath;

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
        if (_semanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol pathMethod)
            return WrapEgress(node, rewritten, EgressOf(pathMethod)) ?? rewritten;

        return rewritten;
    }

    /// <summary>
    /// <c>IMyCubeBlock.CalcLocalMatrix(out m, out model)</c> hands the model path out through an
    /// <c>out</c> argument, so the call is renamed to an extension that translates it.
    /// </summary>
    private InvocationExpressionSyntax RewriteCalcLocalMatrix(
        InvocationExpressionSyntax node,
        InvocationExpressionSyntax rewritten
    )
    {
        if (
            _semanticModel.GetSymbolInfo(node).Symbol
                is not IMethodSymbol { Name: "CalcLocalMatrix", ContainingType: { } containing }
            || containing.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != CubeBlockFqn
        )
            return null;

        var name = SyntaxFactory.IdentifierName(CalcLocalMatrixExtension);
        ExpressionSyntax target = rewritten.Expression switch
        {
            MemberAccessExpressionSyntax access => access.WithName(
                name.WithTriviaFrom(access.Name)
            ),
            MemberBindingExpressionSyntax binding => binding.WithName(
                name.WithTriviaFrom(binding.Name)
            ),
            _ => null,
        };
        if (target == null)
            return null;

        RequiresShimReference = true;
        RequiresExtensionsUsing = true;
        return rewritten.WithExpression(target);
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
    /// Restores native form for values mods assign to path-typed Mod API setters. The value
    /// of the assignment expression itself is the stored, native string, so an assignment
    /// used as a value, like <c>string p = (def.Model = x);</c>, is translated back whole.
    /// Compound assignments store native too (the read of the left side is native), so only
    /// their value needs translating. A member of an object initializer, like
    /// <c>new BuildProgressModel { File = x }</c>, has no value and must stay an assignment.
    /// <see cref="IPathSecurityConfig.TranslateAssignmentValues"/> turns the value translation
    /// off.
    /// </summary>
    public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        var rewritten = (AssignmentExpressionSyntax)base.VisitAssignmentExpression(node);
        var simple = node.IsKind(SyntaxKind.SimpleAssignmentExpression);
        var usedAsValue =
            PathSecurity.Config.TranslateAssignmentValues
            && node.Parent is not ExpressionStatementSyntax
            && !node.Parent.IsKind(SyntaxKind.ObjectInitializerExpression);

        if (
            _semanticModel.GetSymbolInfo(node.Left).Symbol is IPropertySymbol property
            && IsIngressPathSetter(property)
        )
        {
            RequiresShimReference = true;
            if (simple)
                rewritten = rewritten.WithRight(WrapShimCall(ToGameFqn, rewritten.Right));
            return usedAsValue ? WrapShimCall(FromGameFqn, rewritten) : rewritten;
        }

        // A definition string written back, like def.Model = def.Model or def.Icons[0] = ...;
        // some engine code loads these without MyFileSystem, which would untranslate them.
        var written = node.Left is ElementAccessExpressionSyntax element
            ? element.Expression
            : node.Left;
        if (IsDefinitionText(_semanticModel.GetSymbolInfo(written).Symbol))
        {
            RequiresShimReference = true;
            if (simple)
                rewritten = rewritten.WithRight(WrapShimCall(ToGameTextFqn, rewritten.Right));
            return usedAsValue ? WrapShimCall(FromGameTextFqn, rewritten) : rewritten;
        }

        return rewritten;
    }

    private static bool IsDefinitionText(ISymbol symbol)
    {
        var type = symbol switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol { IsConst: false } field => field.Type,
            _ => null,
        };
        return IsText(type) && IsInDefinition(symbol.ContainingType);
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
        for (var child = node; child.Parent != null; child = child.Parent)
        {
            var parent = child.Parent;
            // The receiver of ?. holds no binding of its own conditional, so it can be wrapped
            // unless that conditional is itself on a spine.
            if (parent is ConditionalAccessExpressionSyntax conditional)
            {
                if (conditional.WhenNotNull == child)
                    return true;
                continue;
            }
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

            var egress = EgressOf(_semanticModel.GetSymbolInfo(tail).Symbol);
            if (egress != Egress.None)
            {
                RequiresShimReference = true;
                return WrapShimCall(
                    egress == Egress.Path ? FromGameFqn : FromGameTextFqn,
                    rewritten
                );
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

    /// <summary>
    /// Translates the native paths in a caught exception before the mod sees it: first in the
    /// filter, if there is one, otherwise first in the block.
    /// </summary>
    public override SyntaxNode VisitCatchClause(CatchClauseSyntax node)
    {
        var rewritten = (CatchClauseSyntax)base.VisitCatchClause(node);
        var identifier = rewritten.Declaration?.Identifier ?? default;
        if (identifier.IsKind(SyntaxKind.None))
            return rewritten;

        RequiresShimReference = true;
        var scrub = SyntaxFactory.InvocationExpression(
            SyntaxFactory.ParseExpression(ScrubFqn),
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(SyntaxFactory.IdentifierName(identifier.ValueText))
                )
            )
        );

        if (rewritten.Filter != null)
        {
            return rewritten.WithFilter(
                rewritten.Filter.WithFilterExpression(
                    SyntaxFactory.BinaryExpression(
                        SyntaxKind.LogicalAndExpression,
                        scrub,
                        SyntaxFactory.ParenthesizedExpression(
                            rewritten.Filter.FilterExpression.WithoutTrivia()
                        )
                    )
                )
            );
        }

        return rewritten.WithBlock(
            rewritten.Block.WithStatements(
                rewritten.Block.Statements.Insert(0, SyntaxFactory.ExpressionStatement(scrub))
            )
        );
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

        // A bare path getter converted to a delegate, imported with `using static`.
        if (EgressMemberNames.Contains(node.Identifier.ValueText))
        {
            var methodGroup = WrapMethodGroup(
                node,
                node,
                _semanticModel.GetSymbolInfo(node).Symbol
            );
            if (methodGroup != null)
                return methodGroup;
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

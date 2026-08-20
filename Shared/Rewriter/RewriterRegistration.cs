using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VRage.Scripting;

namespace ClientPlugin.Rewriter;

internal static class RewriterRegistration
{
    public static void Register()
    {
        PlumbRewriterShimReferences();

        // Anchor the process-relative stopwatch baseline before mods run.
        _ = WindowsStopwatch.GetTimestamp();

        try
        {
            // Loader-generated assembly names vary, but the extension type is stable.
            Type extType = null;
            Assembly asm = null;
            foreach (var candidate in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (candidate == typeof(RewriterRegistration).Assembly)
                    continue;
                extType = candidate.GetType(ExtensionTypeName, throwOnError: false);
                if (extType != null)
                {
                    asm = candidate;
                    break;
                }
            }
            if (extType == null)
                throw new InvalidOperationException(
                    $"No loaded assembly exports {ExtensionTypeName}. The DotNetCompat plugin must be " +
                    "installed and loaded before LinuxCompat (check the plugin list and profile order). " +
                    $"Loaded assemblies: {string.Join(", ", AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).OrderBy(n => n, StringComparer.Ordinal))}");

            var field = extType.GetField("RewriterFactories");
            if (field == null)
                throw new InvalidOperationException(
                    $"RewriterFactories field missing on {ExtensionTypeName} in {asm.GetName().Name} (incompatible DotNetCompat version?)");

            if (field.GetValue(null) is not IList list)
                throw new InvalidOperationException(
                    $"{ExtensionTypeName}.RewriterFactories in {asm.GetName().Name} is not an IList " +
                    $"(got {field.GetValue(null)?.GetType().FullName ?? "null"}; incompatible DotNetCompat version?)");

            Func<SemanticModel, CSharpSyntaxRewriter> factory = model => new PathSubstitutionRewriter(model);
            list.Add(factory);

            Console.WriteLine($"[LinuxCompat] PathSubstitutionRewriter registered with DotNetCompat compiler hook in {asm.GetName().Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LinuxCompat] PathSubstitutionRewriter registration failed: {ex}");
#if MAGNETAR
            try { VRage.Utils.MyLog.Default.WriteLineAndConsole($"[LinuxCompat] PathSubstitutionRewriter registration failed: {ex}"); } catch { }
#endif
            throw;
        }
    }

#if MAGNETAR
    private const string ExtensionTypeName = "ServerPlugin.Rewriter.CompilerHookExtensions";
#else
    private const string ExtensionTypeName = "ClientPlugin.Rewriter.CompilerHookExtensions";
#endif

    private static void PlumbRewriterShimReferences()
    {
        try
        {
            var asm = typeof(WindowsPath).Assembly;
            var reference = BuildMetadataReferenceFromLoadedAssembly(asm);
            if (reference == null)
                throw new InvalidOperationException(
                    $"Cannot extract the in-memory metadata image of {asm.GetName().Name}; " +
                    "the script compiler would reject every rewritten mod source file.");

            // The in-memory image preserves loader-renamed assembly identities.
            MyScriptCompiler.Static.m_metadataReferences.Add(reference);

            using (var batch = MyScriptCompiler.Static.Whitelist.OpenBatch())
            {
                batch.AllowTypes(MyWhitelistTarget.ModApi, typeof(WindowsPath));
                batch.AllowTypes(MyWhitelistTarget.ModApi, typeof(WindowsTextWriter));
                batch.AllowTypes(MyWhitelistTarget.ModApi, typeof(WindowsStopwatch));
            }

            Console.WriteLine($"[LinuxCompat] Rewriter shims (WindowsPath, WindowsTextWriter, WindowsStopwatch) plumbed into MyScriptCompiler from in-memory image of {asm.GetName().Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LinuxCompat] Rewriter shim plumb failed: {ex}");
#if MAGNETAR
            try { VRage.Utils.MyLog.Default.WriteLineAndConsole($"[LinuxCompat] Rewriter shim plumb failed: {ex}"); } catch { }
#endif
            throw;
        }
    }

    /// <summary>Uses the loaded assembly identity for Roslyn binding.</summary>
    private static unsafe PortableExecutableReference BuildMetadataReferenceFromLoadedAssembly(Assembly asm)
    {
        if (!asm.TryGetRawMetadata(out byte* blob, out int length))
            return null;
        var moduleMetadata = ModuleMetadata.CreateFromMetadata((IntPtr)blob, length);
        var assemblyMetadata = AssemblyMetadata.Create(moduleMetadata);
        return assemblyMetadata.GetReference(display: asm.GetName().Name);
    }
}

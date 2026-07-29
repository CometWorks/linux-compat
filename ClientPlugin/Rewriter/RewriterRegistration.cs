using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VRage.Scripting;

namespace ClientPlugin.Rewriter;

/// <summary>
/// Registers our <see cref="PathSubstitutionRewriter"/> with the DotNetCompat
/// plugin's compiler-hook extension point.
///
/// DotNetCompat is always loaded earlier than LinuxCompat (Pulsar loads it
/// first), and Microsoft.CodeAnalysis is referenced by both plugins, so the
/// <c>List&lt;Func&lt;SemanticModel, CSharpSyntaxRewriter&gt;&gt;</c> type
/// inside <c>ClientPlugin.Rewriter.CompilerHookExtensions.RewriterFactories</c>
/// is the same closed generic in both assemblies and a directly-typed
/// reference works.
///
/// We still go through reflection because LinuxCompat has no direct project
/// reference to DotNetCompat — DotNetCompat is a separate Pulsar plugin and
/// its DLL is not on LinuxCompat's compile-time hint paths.
/// </summary>
internal static class RewriterRegistration
{
    public static void Register()
    {
        // Make the LinuxCompat rewriter shims visible to the in-game/mod
        // script compiler. The rewriter emits qualified references to
        // ClientPlugin.Rewriter.WindowsPath, WindowsTextWriter and
        // WindowsStopwatch inside mod source, so the compiler needs (a) the
        // LinuxCompat assembly on its metadata reference list, and (b) all
        // shim types on its whitelist. Without (a) compilation fails with
        // "type or namespace 'ClientPlugin' could not be found"; without
        // (b) the whitelist analyzer rejects the references as prohibited
        // members.
        PlumbRewriterShimReferences();

        // Force WindowsStopwatch's static initializer to run NOW (plugin
        // init), not lazily on the mod's first GetTimestamp call. The
        // baseline timestamp captured in the static ctor anchors what
        // GetTimestamp() returns — anchoring it at plugin init time so it
        // approximates "time since process start" by the time the mod runs,
        // matching Wine/Proton's process-relative QPC shape. Without this,
        // the mod itself would trigger the static ctor and see a near-zero
        // value (the baseline cancels with the immediately-following read).
        // GetTimestamp() reads non-const fields, so it actually triggers
        // initialisation (unlike Frequency/IsHighResolution which are consts
        // and would be inlined by the JIT without touching the type).
        _ = WindowsStopwatch.GetTimestamp();

        // Every failure below is fatal. A missing rewriter is not a graceful
        // degradation: mods compile fine but then see Windows path semantics
        // applied to native paths, and fail later in ways that are near
        // impossible to trace back to this registration. Better to refuse to
        // start than to hand the user a subtly broken game.
        try
        {
            // Pulsar's assembly name for DotNetCompat depends on the install
            // path: csproj <AssemblyName> ("DotNetCompat") for packaged DLLs,
            // "DotNetCompat_<random>" for dev-folder builds (FriendlyName
            // from the plugin data file), and "dotnet_compat_<random>" for
            // GitHub-sourced installs (MakeSafeString over the repo name,
            // e.g. CometWorks/dotnet-compat). Matching on the name means
            // keeping that list in sync with Pulsar forever, so match on the
            // extension type instead — that's the only identity we actually
            // care about and it is stable across every install path.
            Type extType = null;
            Assembly asm = null;
            foreach (var candidate in AppDomain.CurrentDomain.GetAssemblies())
            {
                // LinuxCompat's own root namespace is also "ClientPlugin", so
                // exclude ourselves rather than risk matching a same-named
                // type in this assembly.
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
                    "installed and loaded before LinuxCompat (check the Pulsar plugin list and profile order). " +
                    $"Loaded assemblies: {string.Join(", ", AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).OrderBy(n => n, StringComparer.Ordinal))}");

            var field = extType.GetField("RewriterFactories");
            if (field == null)
                throw new InvalidOperationException(
                    $"RewriterFactories field missing on {ExtensionTypeName} in {asm.GetName().Name} (incompatible DotNetCompat version?)");

            // The element type Func<SemanticModel, CSharpSyntaxRewriter> is
            // identical across both assemblies (both reference the same
            // Microsoft.CodeAnalysis instance), so adding through IList works.
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
            // Log before rethrowing: the host does not reliably surface
            // plugin-init exceptions, and this message is the only clue the
            // user gets about why the game refused to start.
            Console.WriteLine($"[LinuxCompat] PathSubstitutionRewriter registration failed: {ex}");
            throw;
        }
    }

    private const string ExtensionTypeName = "ClientPlugin.Rewriter.CompilerHookExtensions";

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

            // Append directly to MyScriptCompiler.m_metadataReferences
            // (publicized via Krafs.Publicizer). We bypass the public
            // AddReferencedAssemblies API because it only accepts file paths
            // — and Pulsar dev-folder builds (a) load us from a byte[] so
            // Assembly.Location is empty, and (b) rename the assembly
            // identity in-memory (e.g. "LinuxCompat" -> "LinuxCompat_xxx.pjp")
            // so the on-disk file's identity doesn't match what mod assemblies
            // would resolve against at runtime. The in-memory image is the
            // only source whose identity matches the loaded type.
            MyScriptCompiler.Static.m_metadataReferences.Add(reference);

            // Whitelist the rewriter's emitted target types for both Ingame
            // (PB scripts) and ModApi (mod scripts). The rewriter applies to
            // every CreateCompilation, so both targets need the types allowed.
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
            // Fatal, and logged before rethrow for the same reason as above:
            // without the shim types on the compiler's reference list and
            // whitelist, every rewritten mod source file fails to compile.
            Console.WriteLine($"[LinuxCompat] Rewriter shim plumb failed: {ex}");
            throw;
        }
    }

    /// <summary>
    /// Builds a Roslyn <see cref="PortableExecutableReference"/> from the
    /// loaded assembly's in-memory metadata image. Uses the runtime helper
    /// <c>AssemblyExtensions.TryGetRawMetadata</c> (System.Reflection.Metadata,
    /// ships with the .NET runtime — no extra package). The resulting
    /// reference carries the same assembly identity that's loaded into the
    /// AppDomain, so types resolved against it match at runtime even when
    /// the host (Pulsar) has renamed the assembly relative to the on-disk
    /// file.
    /// </summary>
    private static unsafe PortableExecutableReference BuildMetadataReferenceFromLoadedAssembly(Assembly asm)
    {
        if (!asm.TryGetRawMetadata(out byte* blob, out int length))
            return null;
        var moduleMetadata = ModuleMetadata.CreateFromMetadata((IntPtr)blob, length);
        var assemblyMetadata = AssemblyMetadata.Create(moduleMetadata);
        // Pass display so diagnostics in the script compiler are readable.
        return assemblyMetadata.GetReference(display: asm.GetName().Name);
    }
}

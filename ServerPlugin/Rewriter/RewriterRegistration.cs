using System;
using System.Collections;
using System.Reflection;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VRage.Scripting;

namespace ServerPlugin.Rewriter;

/// <summary>
/// Registers our <see cref="PathSubstitutionRewriter"/> with the DotNetCompat
/// plugin's compiler-hook extension point.
///
/// DotNetCompat is always loaded earlier than LinuxCompat (Pulsar loads it
/// first), and Microsoft.CodeAnalysis is referenced by both plugins, so the
/// <c>List&lt;Func&lt;SemanticModel, CSharpSyntaxRewriter&gt;&gt;</c> type
/// inside <c>ServerPlugin.Rewriter.CompilerHookExtensions.RewriterFactories</c>
/// is the same closed generic in both assemblies and a directly-typed
/// reference works.
///
/// Namespace note: both DotNetCompat projects share <c>AssemblyName=DotNetCompat</c>
/// but their <c>RootNamespace</c> differs — DotNetCompat-client uses
/// <c>ClientPlugin</c>, DotNetCompat-server uses <c>ServerPlugin</c>. On the
/// dedicated server (where this LinuxCompat-server code runs) only the
/// server-side DotNetCompat is loaded, so the hook type lives under
/// <c>ServerPlugin.Rewriter</c>.
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
        // ServerPlugin.Rewriter.WindowsPath, WindowsTextWriter and
        // WindowsStopwatch inside mod source, so the compiler needs (a) the
        // LinuxCompat assembly on its metadata reference list, and (b) all
        // shim types on its whitelist. Without (a) compilation fails with
        // "type or namespace 'ServerPlugin' could not be found"; without
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

        try
        {
            // Pulsar names production-built plugin assemblies after the
            // <AssemblyName> in the csproj (e.g. "DotNetCompat"), but
            // dev-folder builds get a random suffix (e.g.
            // "DotNetCompat_oppbym1d.mqw"). Match either by scanning every
            // loaded assembly for the extension type — that's the only
            // identity we actually care about.
            Type extType = null;
            Assembly asm = null;
            foreach (var candidate in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = candidate.GetName().Name;
                if (name != "DotNetCompat" && !name.StartsWith("DotNetCompat_", StringComparison.Ordinal))
                    continue;
                // DotNetCompat-server's RootNamespace is "ServerPlugin" (see
                // ServerPlugin.csproj in the DotNetCompat repo). The type we
                // want is in *DotNetCompat's* ServerPlugin assembly, not in
                // this one — they happen to share the namespace because
                // every Pulsar side-plugin project of this shape uses the
                // same root namespace per side. Disambiguation is by
                // assembly identity (the filter above), not by namespace.
                extType = candidate.GetType("ServerPlugin.Rewriter.CompilerHookExtensions", throwOnError: false);
                if (extType != null)
                {
                    asm = candidate;
                    break;
                }
            }
            if (asm == null)
            {
                Console.WriteLine("[LinuxCompat] PathSubstitutionRewriter: DotNetCompat assembly not loaded, skipping registration");
                return;
            }
            if (extType == null)
            {
                Console.WriteLine("[LinuxCompat] PathSubstitutionRewriter: CompilerHookExtensions type missing from DotNetCompat (incompatible version?)");
                return;
            }

            var field = extType.GetField("RewriterFactories");
            if (field == null)
            {
                Console.WriteLine("[LinuxCompat] PathSubstitutionRewriter: RewriterFactories field missing on CompilerHookExtensions");
                return;
            }

            // The element type Func<SemanticModel, CSharpSyntaxRewriter> is
            // identical across both assemblies (both reference the same
            // Microsoft.CodeAnalysis instance), so adding through IList works.
            if (field.GetValue(null) is not IList list)
            {
                Console.WriteLine("[LinuxCompat] PathSubstitutionRewriter: RewriterFactories is not an IList");
                return;
            }

            Func<SemanticModel, CSharpSyntaxRewriter> factory = model => new PathSubstitutionRewriter(model);
            list.Add(factory);

            Console.WriteLine("[LinuxCompat] PathSubstitutionRewriter registered with DotNetCompat compiler hook");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LinuxCompat] PathSubstitutionRewriter registration failed: {ex}");
        }
    }

    private static void PlumbRewriterShimReferences()
    {
        try
        {
            var asm = typeof(WindowsPath).Assembly;
            var reference = BuildMetadataReferenceFromLoadedAssembly(asm);
            if (reference == null)
            {
                Console.WriteLine("[LinuxCompat] Rewriter shim plumb skipped: cannot extract in-memory metadata image");
                return;
            }

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
            Console.WriteLine($"[LinuxCompat] Rewriter shim plumb failed: {ex}");
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

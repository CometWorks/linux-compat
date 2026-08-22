using System;
using System.Reflection;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using VRage.Scripting;

namespace ClientPlugin.Rewriter;

/// <summary>
/// Plumbs the rewriter shim types into the script compiler before any mod compiles.
/// <para>
/// <see cref="WindowsSemanticsRewriter"/> emits references to <see cref="WindowsPath"/>,
/// <see cref="WindowsTextWriter"/> and <see cref="WindowsStopwatch"/> into mod sources, so the
/// whitelist and the mod compilations must both see this assembly. The plugin loader renames the
/// assembly, and only its in-memory image carries that identity, hence the raw metadata image
/// instead of <see cref="MetadataReference.CreateFromFile(string)"/> on <see cref="Assembly.Location"/>.
/// </para>
/// <para>
/// Registration cannot be a Harmony postfix on <c>MySpaceGameDefaultIlChecker.AllowDefaultNamespaces</c>:
/// the game runs the whitelist batch from <c>MySandboxGame.LoadData</c>, before the plugin loader
/// calls <c>IPlugin.Init</c>, so an "Init" category patch is applied too late to ever run.
/// </para>
/// </summary>
internal static class ShimRegistration
{
    private static readonly Lazy<PortableExecutableReference> LazyReference = new(CreateReference);

    private static bool registered;

    /// <summary>
    /// Metadata reference for the shim assembly, shared with <see cref="CompilationRewriter"/> so
    /// the mod compilations bind to the very assembly the whitelist was populated from.
    /// </summary>
    public static PortableExecutableReference Reference => LazyReference.Value;

    public static void Register()
    {
        if (registered)
            return;
        registered = true;

        // Anchor the process-relative stopwatch baseline before mods run.
        _ = WindowsStopwatch.GetTimestamp();

        var asm = typeof(ShimRegistration).Assembly;
        try
        {
            var reference =
                Reference
                ?? throw new InvalidOperationException(
                    $"Neither the in-memory image nor the file of {asm.GetName().Name} yields a metadata reference"
                );

            MyScriptCompiler.Static.m_metadataReferences.Add(reference);

            // MyScriptWhitelist resolves types through the compiler references added above.
            using (var batch = MyScriptCompiler.Static.Whitelist.OpenBatch())
            {
                batch.AllowTypes(
                    MyWhitelistTarget.ModApi,
                    typeof(WindowsPath),
                    typeof(WindowsTextWriter),
                    typeof(WindowsStopwatch)
                );
            }

            Log(
                $"Rewriter shims (WindowsPath, WindowsTextWriter, WindowsStopwatch) plumbed into MyScriptCompiler from {asm.GetName().Name}"
            );
        }
        catch (Exception ex)
        {
            // Without this every rewritten mod fails to compile with "is prohibited" errors.
            Log($"ERROR: Rewriter shim registration failed, mods will not compile: {ex}");
        }
    }

    private static PortableExecutableReference CreateReference()
    {
        var asm = typeof(ShimRegistration).Assembly;

        var fromImage = TryCreateFromLoadedImage(asm);
        if (fromImage != null)
            return fromImage;

        // The file identity may differ from the loader-renamed one, leaving the shims unresolvable.
        Log(
            $"ERROR: Cannot read the in-memory metadata image of {asm.GetName().Name}, falling back to its file"
        );

        return string.IsNullOrEmpty(asm.Location)
            ? null
            : MetadataReference.CreateFromFile(asm.Location);
    }

    /// <summary>Uses the loaded assembly identity for Roslyn binding.</summary>
    private static unsafe PortableExecutableReference TryCreateFromLoadedImage(Assembly asm)
    {
        if (!asm.TryGetRawMetadata(out var blob, out var length))
            return null;

        var moduleMetadata = ModuleMetadata.CreateFromMetadata((IntPtr)blob, length);
        return AssemblyMetadata.Create(moduleMetadata).GetReference(display: asm.GetName().Name);
    }

    private static void Log(string message)
    {
        Console.WriteLine($"[LinuxCompat] {message}");
        try
        {
            VRage.Utils.MyLog.Default?.WriteLineAndConsole($"[LinuxCompat] {message}");
        }
        catch
        {
            // MyLog is not available yet when the server registers from Preloader.Finish.
        }
    }
}

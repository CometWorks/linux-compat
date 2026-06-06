using System;
using System.Diagnostics;

namespace ServerPlugin.Rewriter;

/// <summary>
/// Drop-in replacement for <see cref="System.Diagnostics.Stopwatch"/> with a
/// Windows-shape tick scale.
///
/// .NET 10 on Linux exposes <see cref="System.Diagnostics.Stopwatch.Frequency"/>
/// as <c>1_000_000_000</c> (host <c>clock_monotonic</c>, nanosecond
/// resolution). Windows and Wine/Proton both expose
/// <c>QueryPerformanceCounter</c> as <c>10_000_000</c> (100ns resolution).
/// The numeric <c>Frequency</c>, the absolute values returned by
/// <c>GetTimestamp()</c>, and the per-instance <c>ElapsedTicks</c> therefore
/// differ by 100x between native Linux and Proton SE even though wall-clock
/// <see cref="Elapsed"/> agrees.
///
/// This shim wraps a real <see cref="Stopwatch"/> and rescales the
/// tick-shaped outputs to the Windows scale. <see cref="Elapsed"/> and
/// <see cref="ElapsedMilliseconds"/> already report real time and pass
/// through unchanged.
///
/// <see cref="GetTimestamp"/> additionally subtracts a baseline captured at
/// shim static init: native Linux returns nanoseconds since system boot
/// (potentially 10^13 or more), Wine/Proton's QPC counts from process start.
/// Subtracting the baseline makes raw <c>GetTimestamp()</c> values close in
/// magnitude to Wine's, so a mod that logs single snapshots sees
/// Windows-shape numbers. Mod code that subtracts two snapshots to measure
/// a duration is unaffected because the baseline cancels.
///
/// The companion <see cref="PathSubstitutionRewriter"/> substitutes every
/// reference to <see cref="System.Diagnostics.Stopwatch"/> in mod source
/// with this type — <c>new Stopwatch()</c>, <c>Stopwatch sw;</c>,
/// <c>List&lt;Stopwatch&gt;</c>, <c>Stopwatch.GetTimestamp()</c>,
/// <c>typeof(Stopwatch)</c>, etc. — via <c>VisitIdentifierName</c> /
/// <c>VisitQualifiedName</c> overrides that filter on
/// <see cref="Microsoft.CodeAnalysis.INamedTypeSymbol"/> identity. The
/// shim therefore needs the same public surface a mod might call.
/// </summary>
public sealed class WindowsStopwatch
{
    /// <summary>Matches the Windows QueryPerformanceCounter frequency (100ns ticks).</summary>
    public const long Frequency = 10_000_000L;

    /// <summary>Always <c>true</c> — Windows reports the same.</summary>
    public const bool IsHighResolution = true;

    // Integer divisor from host Stopwatch ticks to Windows-shape ticks.
    // Typically 100 on .NET 10 Linux (1e9 / 1e7). If the host clock isn't a
    // clean multiple of 10 MHz (extremely rare hardware) we fall back to
    // pass-through rather than risk divide-by-zero or wildly wrong values.
    private static readonly long _scale;

    // Process-relative baseline so absolute GetTimestamp() values start
    // close to zero (matching Wine's process-relative QPC) instead of
    // running at the boot-relative magnitude of Linux CLOCK_MONOTONIC.
    // Captured once at static init — duration measurements (timestamp
    // deltas) are unaffected because the baseline cancels.
    private static readonly long _baseline;

    static WindowsStopwatch()
    {
        var hostFreq = Stopwatch.Frequency;
        _scale = hostFreq >= Frequency && hostFreq % Frequency == 0
            ? hostFreq / Frequency
            : 0L;
        _baseline = Stopwatch.GetTimestamp();
    }

    private readonly Stopwatch _inner = new Stopwatch();

    public bool IsRunning => _inner.IsRunning;

    public TimeSpan Elapsed => _inner.Elapsed;

    public long ElapsedMilliseconds => _inner.ElapsedMilliseconds;

    public long ElapsedTicks => Scale(_inner.ElapsedTicks);

    public void Start() => _inner.Start();

    public void Stop() => _inner.Stop();

    public void Reset() => _inner.Reset();

    public void Restart() => _inner.Restart();

    public static long GetTimestamp()
    {
        return Scale(Stopwatch.GetTimestamp() - _baseline);
    }

    public static WindowsStopwatch StartNew()
    {
        var sw = new WindowsStopwatch();
        sw.Start();
        return sw;
    }

    private static long Scale(long rawTicks)
    {
        return _scale > 0 ? rawTicks / _scale : rawTicks;
    }
}

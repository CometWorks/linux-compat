using System;
using System.Diagnostics;

namespace ClientPlugin.Rewriter;

/// <summary>Exposes Linux stopwatch timestamps as Windows-style 10 MHz process-relative ticks.</summary>
public sealed class WindowsStopwatch
{
    /// <summary>Matches the Windows QueryPerformanceCounter frequency (100ns ticks).</summary>
    public const long Frequency = 10_000_000L;

    /// <summary>Always <c>true</c>, matching Windows.</summary>
    public const bool IsHighResolution = true;

    // Use pass-through when the host frequency cannot be divided safely.
    private static readonly long _scale;

    // Offset boot-relative host timestamps to a process-relative baseline.
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

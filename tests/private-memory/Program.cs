using System;
using System.Diagnostics;
using System.Threading;
using ClientPlugin.Patches.SystemAbstraction;

namespace PrivateMemoryTests;

/// <summary>
/// Checks that ProcessPrivateMemory.Get reads the process once a second, cheaply, and allocates
/// nothing in between. Allocation is counted with the thread's allocated bytes, which only ever grow.
/// </summary>
internal static class Program
{
    private const int Calls = 1000;

    private static int s_failures;
    private static int s_checks;

    private static int Main()
    {
        // The instrument: one uncached read must show up in the allocation counter.
        var uncached = Allocated(() => Process.GetCurrentProcess().PrivateMemorySize64);
        Check("an uncached read allocates", uncached > 0, $"{uncached} bytes");

        // A refresh reads /proc/self/status into a stack buffer: only the file handle is garbage.
        var statusRead = Allocated(ProcessPrivateMemory.ReadStatus);
        Check(
            "a status read allocates under 1 KB",
            statusRead < 1024 && statusRead < uncached / 10,
            $"{statusRead} bytes"
        );
        var fromStatus = ProcessPrivateMemory.ReadStatus();
        var fromProcess = Process.GetCurrentProcess().PrivateMemorySize64;
        Check(
            "a status read is the Process value",
            fromStatus > 0 && Near(fromStatus, fromProcess),
            $"{fromStatus} vs {fromProcess}"
        );

        var expected = Process.GetCurrentProcess().PrivateMemorySize64;
        var first = ProcessPrivateMemory.Get();
        Check(
            "the first read is the process value",
            first > 0 && Near(first, expected),
            $"{first} vs {expected}"
        );

        long last = 0;
        var watch = Stopwatch.StartNew();
        var cached = Allocated(() =>
        {
            for (var i = 0; i < Calls; i++)
                last = ProcessPrivateMemory.Get();
            return last;
        });
        watch.Stop();
        if (watch.ElapsedMilliseconds < 900)
        {
            Check(
                $"{Calls} reads within a second allocate nothing",
                cached == 0,
                $"{cached} bytes"
            );
            Check(
                "reads within a second return the cached value",
                last == first,
                $"{last} vs {first}"
            );
        }
        else
            Check(
                "the cached reads finished within a second",
                false,
                $"{watch.ElapsedMilliseconds} ms"
            );

        // Grow the private memory, wait out the cache, and read again.
        var ballast = new byte[64 * 1024 * 1024];
        Array.Fill(ballast, (byte)1);
        Thread.Sleep(1100);
        var refreshedValue = ProcessPrivateMemory.Get();
        Check(
            "the refreshed value sees the new memory",
            refreshedValue >= first + ballast.Length / 2,
            $"{refreshedValue} vs {first}"
        );
        GC.KeepAlive(ballast);

        Console.WriteLine($"{s_checks - s_failures}/{s_checks} checks passed");
        return s_failures == 0 ? 0 : 1;
    }

    private static long Allocated(Func<long> action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static bool Near(long a, long b) => Math.Abs(a - b) <= 4 * 1024 * 1024;

    private static void Check(string name, bool ok, string detail)
    {
        s_checks++;
        if (!ok)
            s_failures++;
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name} ({detail})");
    }
}

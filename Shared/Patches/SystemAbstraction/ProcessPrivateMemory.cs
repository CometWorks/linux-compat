using System;
using System.Diagnostics;
using System.IO;

namespace ClientPlugin.Patches.SystemAbstraction;

/// <summary>
/// The body of <c>MyWindowsSystem.ProcessPrivateMemory</c> on Linux, written into the getter by
/// <c>Preloader.ReplaceProcessPrivateMemory</c>.
/// <para>
/// <c>MyGeneralStats.Update</c> reads the getter on every update. On Linux
/// <see cref="Process.PrivateMemorySize64"/> builds a new <see cref="Process"/> snapshot each
/// time, reading <c>/proc/self/stat</c>, <c>/proc/self/status</c> and the stat file of every
/// thread: hundreds of MB/min of garbage on an idle server. The value is only shown in
/// statistics, so it is read at most once a second, and from <c>/proc/self/status</c> alone,
/// into a stack buffer.
/// </para>
/// </summary>
public static class ProcessPrivateMemory
{
    const long RefreshMilliseconds = 1000;

    static long value;
    static long readAt;

    public static long Get()
    {
        var now = Environment.TickCount64;
        if (readAt == 0 || now - readAt >= RefreshMilliseconds)
        {
            value = ReadStatus();
            readAt = now;
        }
        return value;
    }

    /// <summary>
    /// What <see cref="Process.PrivateMemorySize64"/> reports on Linux: VmData plus VmStk.
    /// </summary>
    internal static long ReadStatus()
    {
        try
        {
            Span<byte> buffer = stackalloc byte[8192];
            int length;
            using (var handle = File.OpenHandle("/proc/self/status"))
                length = RandomAccess.Read(handle, buffer, 0);
            var status = buffer[..length];
            if (TryReadKb(status, "VmData:"u8, out var data))
            {
                TryReadKb(status, "VmStk:"u8, out var stack);
                return (data + stack) * 1024;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        using var process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64;
    }

    // A "Key:   1234 kB" line of /proc/<pid>/status.
    static bool TryReadKb(ReadOnlySpan<byte> status, ReadOnlySpan<byte> key, out long kb)
    {
        kb = 0;
        var start = status.IndexOf(key);
        if (start < 0 || (start > 0 && status[start - 1] != (byte)'\n'))
            return false;
        var line = status[(start + key.Length)..];
        var end = line.IndexOf((byte)'\n');
        if (end >= 0)
            line = line[..end];
        var digits = false;
        foreach (var c in line)
        {
            if (c >= '0' && c <= '9')
            {
                kb = kb * 10 + (c - '0');
                digits = true;
            }
            else if (digits)
                break;
        }
        return digits;
    }
}

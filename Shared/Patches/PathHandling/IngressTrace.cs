using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace ClientPlugin.Patches.PathHandling;

/// <summary>
/// Records every internal site that still receives a drive-prefixed mod path.
/// Enabled with SE_LINUX_COMPAT_TRACE_INGRESS=1; the stack identifies whether the
/// path arrived through a mod API call or through shared mutable data.
/// </summary>
public static class IngressTrace
{
    public static readonly bool Enabled = IsTruthy(
        Environment.GetEnvironmentVariable("SE_LINUX_COMPAT_TRACE_INGRESS")
    );

    // One report per (site, path); repeated hot-path hits stay silent.
    private static readonly ConcurrentDictionary<string, byte> Seen = new();

    private static bool IsTruthy(string value) =>
        !string.IsNullOrEmpty(value)
        && value != "0"
        && !value.Equals("false", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reports one drive-prefix conversion with its calling chain.</summary>
    public static void Record(string input, string output)
    {
        try
        {
            // Skip Record and the Untranslate frame that invoked it.
            var stack = new StackTrace(2, false);
            var caller = DescribeCaller(stack);
            if (!Seen.TryAdd(caller + "|" + input, 0))
                return;

            var sb = new StringBuilder(256);
            sb.Append("[LinuxCompat][IngressTrace] ")
                .Append(caller)
                .Append(": '")
                .Append(input)
                .Append("' -> '")
                .Append(output)
                .AppendLine("'");
            AppendFrames(sb, stack);

            var message = sb.ToString();
            Console.WriteLine(message);
            try
            {
                VRage.Utils.MyLog.Default?.WriteLine(message);
            }
            catch
            {
                // MyLog may not exist yet during early startup.
            }
        }
        catch
        {
            // Diagnostics must never break path handling.
        }
    }

    /// <summary>First frame outside the path-handling infrastructure.</summary>
    private static string DescribeCaller(StackTrace stack)
    {
        for (int i = 0; i < stack.FrameCount; i++)
        {
            var method = stack.GetFrame(i)?.GetMethod();
            var type = method?.DeclaringType;
            if (type == null)
                continue;
            if (type.Namespace == typeof(IngressTrace).Namespace)
                continue;
            return type.FullName + "." + method.Name;
        }
        return "<unknown>";
    }

    private static void AppendFrames(StringBuilder sb, StackTrace stack)
    {
        // Enough frames to distinguish call ingress from shared-data ingress.
        const int maxFrames = 16;
        var count = Math.Min(stack.FrameCount, maxFrames);
        for (int i = 0; i < count; i++)
        {
            var method = stack.GetFrame(i)?.GetMethod();
            var type = method?.DeclaringType;
            sb.Append("    at ")
                .Append(type?.FullName ?? "<unknown>")
                .Append('.')
                .AppendLine(method?.Name ?? "<unknown>");
        }
        if (stack.FrameCount > maxFrames)
            sb.AppendLine("    ...");
    }
}

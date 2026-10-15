using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using ClientPlugin.Patches.PathHandling;

namespace ClientPlugin.Rewriter;

/// <summary>
/// Translates the native paths in the exceptions a mod catches. The rewriter calls
/// <see cref="Scrub"/> first thing in every catch block and exception filter of mod code, so
/// <see cref="Exception.Message"/>, <see cref="Exception.StackTrace"/>, <see cref="Exception.ToString"/>
/// and the file names of I/O exceptions name <c>C:\users\steamuser\...</c> like on Windows,
/// never the Linux home folder. The exception object itself is changed, so a rethrow carries
/// the translated text too.
/// </summary>
public static class WindowsException
{
    private static readonly FieldInfo StackTraceString = Field("_stackTraceString");
    private static readonly FieldInfo RemoteStackTraceString = Field("_remoteStackTraceString");

    // Every string field of an exception type, its own and inherited ones.
    private static readonly ConcurrentDictionary<Type, FieldInfo[]> StringFields = new();

    /// <summary>Always true, so it can lead an exception filter.</summary>
    public static bool Scrub(Exception exception)
    {
        if (exception == null)
            return true;

        try
        {
            // An unread trace is only frames. When it names a path, freeze the translated text;
            // a later rethrow then adds no frames to it.
            var trace = exception.StackTrace;
            var translatedTrace = PathTranslation.TranslateText(trace);
            if (translatedTrace != trace)
            {
                RemoteStackTraceString?.SetValue(exception, null);
                StackTraceString?.SetValue(exception, translatedTrace);
            }

            foreach (var field in StringFields.GetOrAdd(exception.GetType(), FindStringFields))
            {
                var value = (string)field.GetValue(exception);
                var translated = PathTranslation.TranslateText(value);
                if (translated != value)
                    field.SetValue(exception, translated);
            }
        }
        catch
        {
            // A runtime without these fields keeps the native text rather than failing the mod.
        }

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
                Scrub(inner);
        }
        else
        {
            Scrub(exception.InnerException);
        }

        return true;
    }

    private static FieldInfo[] FindStringFields(Type type)
    {
        var fields = Enumerable.Empty<FieldInfo>();
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
        {
            fields = fields.Concat(
                t.GetFields(
                        BindingFlags.Instance
                            | BindingFlags.Public
                            | BindingFlags.NonPublic
                            | BindingFlags.DeclaredOnly
                    )
                    .Where(f => f.FieldType == typeof(string) && f != StackTraceString)
            );
        }
        return fields.ToArray();
    }

    private static FieldInfo Field(string name) =>
        typeof(Exception).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
}

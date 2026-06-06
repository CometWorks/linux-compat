using System.IO;

namespace ServerPlugin.Rewriter;

/// <summary>
/// Compile-time substitute for <see cref="System.IO.TextWriter.WriteLine(string)"/>
/// (and its sibling overloads). The companion <see cref="PathSubstitutionRewriter"/>
/// rewrites every <c>writer.WriteLine(...)</c> call inside mod source to
/// <c>ServerPlugin.Rewriter.WindowsTextWriter.WriteLine(writer, ...)</c>, so
/// the line terminator emitted by mod code matches Windows even though the
/// host BCL's <see cref="System.Environment.NewLine"/> is <c>"\n"</c> on
/// .NET 10 Linux.
///
/// The implementation deliberately calls <c>Write(value)</c> then
/// <c>Write("\r\n")</c> on the same writer rather than touching
/// <see cref="System.IO.TextWriter.NewLine"/>. Mutating <c>NewLine</c> would
/// be visible to other consumers of the same writer (the engine, other mods)
/// and we want the override to apply only at the call site that the rewriter
/// reached.
///
/// Only the public overload set the in-game compiler can see (whitelisted
/// <see cref="System.IO.TextWriter"/> members) is reproduced here. The
/// argument is forwarded to the corresponding <c>Write</c> overload, so the
/// boxing / formatting behaviour is identical to <c>WriteLine</c>'s.
///
/// As with <see cref="WindowsPath"/>, this type is plumbed into
/// <c>MyScriptCompiler</c>'s metadata references and whitelist by
/// <see cref="RewriterRegistration"/> so that the rewriter's emitted
/// qualified references compile under the mod whitelist analyzer.
/// </summary>
public static class WindowsTextWriter
{
    private const string Crlf = "\r\n";

    public static void WriteLine(TextWriter writer)
    {
        writer.Write(Crlf);
    }

    public static void WriteLine(TextWriter writer, bool value)
    {
        writer.Write(value);
        writer.Write(Crlf);
    }

    public static void WriteLine(TextWriter writer, char value)
    {
        writer.Write(value);
        writer.Write(Crlf);
    }

    public static void WriteLine(TextWriter writer, char[] buffer)
    {
        writer.Write(buffer);
        writer.Write(Crlf);
    }

    public static void WriteLine(TextWriter writer, char[] buffer, int index, int count)
    {
        writer.Write(buffer, index, count);
        writer.Write(Crlf);
    }

    public static void WriteLine(TextWriter writer, decimal value)
    {
        writer.Write(value);
        writer.Write(Crlf);
    }

    public static void WriteLine(TextWriter writer, double value)
    {
        writer.Write(value);
        writer.Write(Crlf);
    }

    public static void WriteLine(TextWriter writer, float value)
    {
        writer.Write(value);
        writer.Write(Crlf);
    }

    public static void WriteLine(TextWriter writer, int value)
    {
        writer.Write(value);
        writer.Write(Crlf);
    }

    public static void WriteLine(TextWriter writer, long value)
    {
        writer.Write(value);
        writer.Write(Crlf);
    }

    public static void WriteLine(TextWriter writer, object value)
    {
        writer.Write(value);
        writer.Write(Crlf);
    }

    public static void WriteLine(TextWriter writer, string value)
    {
        writer.Write(value);
        writer.Write(Crlf);
    }

    public static void WriteLine(TextWriter writer, string format, object arg0)
    {
        writer.Write(format, arg0);
        writer.Write(Crlf);
    }

    public static void WriteLine(TextWriter writer, string format, object arg0, object arg1)
    {
        writer.Write(format, arg0, arg1);
        writer.Write(Crlf);
    }

    public static void WriteLine(TextWriter writer, string format, object arg0, object arg1, object arg2)
    {
        writer.Write(format, arg0, arg1, arg2);
        writer.Write(Crlf);
    }

    public static void WriteLine(TextWriter writer, string format, params object[] args)
    {
        writer.Write(format, args);
        writer.Write(Crlf);
    }

    public static void WriteLine(TextWriter writer, uint value)
    {
        writer.Write(value);
        writer.Write(Crlf);
    }

    public static void WriteLine(TextWriter writer, ulong value)
    {
        writer.Write(value);
        writer.Write(Crlf);
    }
}

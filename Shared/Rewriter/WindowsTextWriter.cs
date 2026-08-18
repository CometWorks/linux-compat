using System.IO;

namespace ClientPlugin.Rewriter;

/// <summary>Writes Windows CRLF endings without changing the shared writer's <see cref="TextWriter.NewLine"/>.</summary>
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

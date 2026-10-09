using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

// Sorts the lines of a file with Win32 CompareStringEx (invariant locale, no flags),
// which is what String.CompareTo does on Windows. Run it under Wine to make the
// golden data of the definition file order test.
static class NlsCheck
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int CompareStringEx(
        string locale,
        uint flags,
        string a,
        int lengthA,
        string b,
        int lengthB,
        IntPtr version,
        IntPtr reserved,
        IntPtr param
    );

    static int Compare(string a, string b) =>
        CompareStringEx("", 0, a, a.Length, b, b.Length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) - 2;

    static void Main(string[] args)
    {
        var lines = new List<string>(File.ReadAllLines(args[0]));
        lines.Sort(Compare);
        File.WriteAllText(args[1], string.Join("\n", lines) + "\n");
    }
}

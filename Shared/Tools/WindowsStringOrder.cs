using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ClientPlugin.Tools;

// The order String.CompareTo gives on Windows (NLS, word sort), for paths.
// .NET on Linux compares with ICU, which orders punctuation differently: it puts
// "LCDTextures_Economy.sbc" before "LCDTextures.sbc", Windows puts it after.
// The game sorts its definition files this way, and the resulting load order
// numbers things that travel over the network by index, like LCD images.
//
// Like an NLS sort key, strings compare on their letters, symbols and digits
// first, then on accents, then on case (lowercase first), and last on the
// hyphens and apostrophes, which word sort skips in the first three passes.
// Checked against Windows' CompareStringEx as Wine implements it, see
// tests/definition-file-order. Characters outside ASCII are approximated: an
// accented Latin letter sorts as its base letter, anything else after 'z' by
// code point.
public static class WindowsStringOrder
{
    // Weight order of the non-alphanumeric ASCII characters
    const string Symbols = " !\"#$%&()*,./:;?@[\\]^_`{|}~+<=>";

    // Linux paths with the separators Windows would have
    public static int ComparePaths(string x, string y) =>
        Compare(x?.Replace('/', '\\'), y?.Replace('/', '\\'));

    public static int Compare(string x, string y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x == null)
            return -1;
        if (y == null)
            return 1;

        Key a = new Key(x),
            b = new Key(y);
        int result = CompareLists(a.Primary, b.Primary);
        if (result == 0)
            result = CompareLists(a.Accent, b.Accent);
        if (result == 0)
            result = CompareLists(a.Case, b.Case);
        if (result == 0)
            result = CompareLists(a.Special, b.Special);
        return result != 0 ? result : string.CompareOrdinal(x, y);
    }

    sealed class Key
    {
        public readonly List<int> Primary = new(),
            Accent = new(),
            Case = new(),
            Special = new();

        public Key(string value)
        {
            string text = value.Normalize(NormalizationForm.FormD);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\'' || c == '-')
                {
                    // A later position sorts first
                    Special.Add(-i * 2 + (c == '-' ? 1 : 0));
                    continue;
                }
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    if (Accent.Count > 0)
                        Accent[Accent.Count - 1] = c;
                    continue;
                }
                Primary.Add(Weight(c));
                Accent.Add(0);
                Case.Add(char.IsUpper(c) ? 1 : 0);
            }
        }
    }

    static int Weight(char c)
    {
        int symbol = Symbols.IndexOf(c);
        if (symbol >= 0)
            return symbol + 1;
        if (c >= '0' && c <= '9')
            return 100 + c - '0';
        c = char.ToLowerInvariant(c);
        if (c >= 'a' && c <= 'z')
            return 200 + c - 'a';
        // Control characters first, everything else after the letters
        return c < ' ' ? 0 : 0x10000 + c;
    }

    static int CompareLists(List<int> a, List<int> b)
    {
        int count = Math.Min(a.Count, b.Count);
        for (int i = 0; i < count; i++)
            if (a[i] != b[i])
                return a[i].CompareTo(b[i]);
        return a.Count.CompareTo(b.Count);
    }
}

using VRage.Input;

namespace ClientPlugin.Compatibility;

internal static class SdlPhysicalKeys
{
    // SDL scancodes are USB HID positions. MyKeys are Windows virtual keys;
    // use the US key at each position for gameplay, regardless of layout.
    internal static MyKeys Map(uint scancode)
    {
        if (scancode >= 4 && scancode <= 29) // A-Z
            return MyKeys.A + (byte)(scancode - 4);
        if (scancode >= 30 && scancode <= 38) // 1-9
            return MyKeys.D1 + (byte)(scancode - 30);

        return scancode switch
        {
            39 => MyKeys.D0,
            45 => MyKeys.OemMinus,
            46 => MyKeys.OemPlus,
            47 => MyKeys.OemOpenBrackets,
            48 => MyKeys.OemCloseBrackets,
            49 or 50 => MyKeys.OemPipe,
            51 => MyKeys.OemSemicolon,
            52 => MyKeys.OemQuotes,
            53 => MyKeys.OemTilde,
            54 => MyKeys.OemComma,
            55 => MyKeys.OemPeriod,
            56 => MyKeys.OemQuestion,
            100 => MyKeys.OemBackslash, // Extra ISO key beside left Shift.
            _ => MyKeys.None,
        };
    }
}

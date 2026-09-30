using ClientPlugin.Compatibility;
using VRage.Input;

static void Check(uint scancode, MyKeys expected)
{
    MyKeys actual = SdlPhysicalKeys.Map(scancode);
    if (actual != expected)
        throw new Exception($"Scancode {scancode}: expected {expected}, got {actual}");
}

// German ^, ß, ´, Ü, +, Ö, Ä, # and the extra ISO < key.
Check(53, MyKeys.OemTilde);
Check(45, MyKeys.OemMinus);
Check(46, MyKeys.OemPlus);
Check(47, MyKeys.OemOpenBrackets);
Check(48, MyKeys.OemCloseBrackets);
Check(51, MyKeys.OemSemicolon);
Check(52, MyKeys.OemQuotes);
Check(49, MyKeys.OemPipe);
Check(50, MyKeys.OemPipe);
Check(100, MyKeys.OemBackslash);

// A non-Latin layout still exposes the same physical gameplay positions.
Check(4, MyKeys.A);
Check(29, MyKeys.Z);
Check(30, MyKeys.D1);
Check(39, MyKeys.D0);
Check(56, MyKeys.OemQuestion);
Check(0, MyKeys.None);
Check(40, MyKeys.None); // Non-printable keys keep the existing keycode mapping.

for (uint scancode = 4; scancode <= 39; scancode++)
    if (SdlPhysicalKeys.Map(scancode) == MyKeys.None)
        throw new Exception($"Unmapped letter or number position: {scancode}");
for (uint scancode = 45; scancode <= 56; scancode++)
    if (SdlPhysicalKeys.Map(scancode) == MyKeys.None)
        throw new Exception($"Unmapped punctuation position: {scancode}");

Console.WriteLine("Keyboard layout mapping checks passed.");

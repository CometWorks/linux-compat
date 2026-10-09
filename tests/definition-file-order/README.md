# Definition file order tests

Verifies that `WindowsStringOrder` sorts strings the way `String.CompareTo` does
on Windows. The game sorts its definition files by path with that call, and the
resulting load order numbers the LCD images, which travel to the server by
index. .NET on Linux compares with ICU instead, which orders punctuation
differently, so a Linux client and a Windows server used to disagree on which
image an index means.

The suite is standalone: it compiles `Shared/Tools/WindowsStringOrder.cs`
verbatim and needs neither the game nor Steam.

```bash
cd tests/definition-file-order && dotnet run -c Release
```

Exit code 0 means every check passed. Each file in `Data` lists strings in the
order Windows sorts them, and the test sorts a shuffled copy back:

| File | Content |
| --- | --- |
| `ascii-probes.txt` | Every printable ASCII character between letters, case, accent, digit and hyphen cases |
| `random-ascii.txt` | 4000 random strings of letters, digits and the punctuation found in file names |
| `game-data-files.txt` | The game's own `Content/Data` and `Content/DataPlatform` `.sbc` files as Windows paths |
| `workshop-mod-files.txt` | Every seventh relative `.sbc` path from 378 workshop mods |

The game's files are also checked as Linux paths through `ComparePaths`, and
`LCDTextures.sbc` has to sort before `LCDTextures_Economy.sbc`.

## Regenerating the data

The order comes from Win32 `CompareStringEx` as Wine implements it. `nlscheck`
is a .NET Framework console app that sorts a file's lines with it:

```bash
cd tests/definition-file-order/nlscheck && dotnet build -c Release
PROTON="$HOME/.steam/steam/steamapps/common/Proton - Experimental/files"
WINEPREFIX=/tmp/nlscheck-prefix WINEDEBUG=-all "$PROTON/bin/wine" \
    bin/Release/net48/NlsCheck.exe 'Z:/tmp/input.txt' 'Z:/tmp/sorted.txt'
```

Proton's Wine brings Wine Mono, which runs the app. A system Wine without Wine
Mono needs it installed into the prefix first.

using ClientPlugin.Tools;

// Each data file lists strings in the order Windows sorts them, as Wine's
// CompareStringEx produced it (see README.md). WindowsStringOrder has to
// reproduce that order from a shuffled copy.
static void CheckOrder(string name, string[] expected, Comparison<string> compare)
{
    var actual = expected.OrderBy(s => s.GetHashCode()).ToList();
    actual.Sort(compare);
    for (int i = 0; i < expected.Length; i++)
        if (expected[i] != actual[i])
            throw new Exception(
                $"{name}, line {i + 1}: expected [{expected[i]}], got [{actual[i]}]"
            );
    Console.WriteLine($"{name}: {expected.Length} strings in Windows order");
}

var data = Path.Combine(AppContext.BaseDirectory, "Data");
foreach (var file in Directory.GetFiles(data, "*.txt").Order())
    CheckOrder(Path.GetFileName(file), File.ReadAllLines(file), WindowsStringOrder.Compare);

// The game's own files as Linux paths, the way MyDefinitionManager sorts them
var linuxPaths = File.ReadAllLines(Path.Combine(data, "game-data-files.txt"))
    .Select(p =>
        p.Replace("C:\\SE", "/home/user/.steam/steamapps/common/SpaceEngineers").Replace('\\', '/')
    )
    .ToArray();
CheckOrder("game-data-files.txt as Linux paths", linuxPaths, WindowsStringOrder.ComparePaths);

// DIS-0001: the base LCD textures load first on Windows, and so they have to here
var lcd = Array.FindIndex(linuxPaths, p => p.EndsWith("/LCDTextures.sbc"));
var economy = Array.FindIndex(linuxPaths, p => p.EndsWith("/LCDTextures_Economy.sbc"));
if (lcd > economy)
    throw new Exception("LCDTextures.sbc sorts after LCDTextures_Economy.sbc");

Console.WriteLine("Definition file order checks passed.");

// Mod location check for LinuxCompat's MyAPIGateway.Utilities wrapper.
// Calls FileExistsInModLocation, ReadFileInModLocation and ReadBinaryFileInModLocation of a
// built LinuxCompat.dll with the backslash paths Build Info uses, for a world mod and for a
// client mod that Pulsar compiled with its own mod context, and checks the items the wrapper
// must refuse. See README.md.
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Sandbox.Definitions;
using Sandbox.Game.World;
using VRage.FileSystem;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.GameServices;
using VRage.Utils;

var plugin = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
var utils = (IMyUtilities)
    Activator.CreateInstance(
        plugin.GetType("ClientPlugin.Patches.PathHandling.ModApiWrappers.WrappedUtilities", true),
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        null,
        [null],
        null
    );

// Two mod folders with the files Build Info reads through the mod API.
string[] files =
[
    "Textures/ActionIcons/ToggleEnabled.dds",
    "Data/VanillaDefinitions.txt",
    "Fonts/BI_SEOutlined/FontDataPA.xml",
];
var root = Directory.CreateTempSubdirectory("mod-location-").FullName;

// The case resolver reads the game's roots on a miss.
MyFileSystem.Init(
    Directory.CreateDirectory(Path.Combine(root, "Content")).FullName,
    Path.Combine(root, "User")
);
string ModFolder(ulong id)
{
    var folder = Path.Combine(root, "workshop/content/244850", id.ToString());
    foreach (var file in files)
    {
        var path = Path.Combine(folder, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, $"{id} {file}\n");
    }
    return folder;
}

// A world mod is in the session's mods. A client mod is not: Pulsar loads its definitions by
// folder and compiles its scripts with a context of its own (Pulsar Legacy/Extensions/
// ModPlugin.cs, GetModContext). The harness sets up all three places the game keeps them.
var worldMod = WorkshopItem(758597413, ModFolder(758597413));
var clientMod = WorkshopItem(514062285, ModFolder(514062285));

var session = (MySession)RuntimeHelpers.GetUninitializedObject(typeof(MySession));
session.Mods = [worldMod];

// The Static setter also tells the platform layer, which this harness does not have.
typeof(MySession)
    .GetField("m_static", BindingFlags.Static | BindingFlags.NonPublic)
    .SetValue(null, session);

var clientContext = new MyModContext();
clientContext.Init(clientMod);
clientContext.Init(clientMod.PublishedFileId.ToString(), null, clientMod.GetPath());
var scripts = (MyScriptManager)RuntimeHelpers.GetUninitializedObject(typeof(MyScriptManager));
typeof(MyScriptManager)
    .GetField(nameof(MyScriptManager.ScriptsPerMod))
    .SetValue(scripts, new Dictionary<MyModContext, HashSet<MyStringId>> { [clientContext] = [] });
MyScriptManager.Static = scripts;

var definitions = RuntimeHelpers.GetUninitializedObject(typeof(MyDefinitionManager));
var setsField = typeof(MyDefinitionManager).GetField(
    "m_modDefinitionSets",
    BindingFlags.Instance | BindingFlags.NonPublic
);
var sets = (IDictionary)Activator.CreateInstance(setsField.FieldType);
sets.Add(worldMod.GetPath(), null);
sets.Add(clientMod.GetPath(), null);
setsField.SetValue(definitions, sets);
MyDefinitionManagerBase.Static = (MyDefinitionManagerBase)definitions;

int pass = 0,
    fail = 0;
void Check(string name, object expected, Func<object> actual)
{
    object got;
    try
    {
        got = actual();
    }
    catch (Exception e)
    {
        got = e.GetType().Name;
        if (Environment.GetEnvironmentVariable("MOD_LOCATION_TRACE") == "1")
            Console.Error.WriteLine(e);
    }
    bool ok = Equals(expected, got);
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name} | expected={expected} | actual={got}");
    if (ok)
        pass++;
    else
        fail++;
}

foreach (var (kind, item) in new[] { ("world mod", worldMod), ("client mod", clientMod) })
{
    foreach (var file in files)
    {
        var expected = $"{item.PublishedFileId} {file}";
        foreach (var path in new[] { file.Replace('/', '\\'), file })
        {
            Check($"{kind}: exists {path}", true, () => utils.FileExistsInModLocation(path, item));
            Check(
                $"{kind}: read {path}",
                expected,
                () =>
                {
                    using var reader = utils.ReadFileInModLocation(path, item);
                    return reader.ReadLine();
                }
            );
            Check(
                $"{kind}: binary read {path}",
                expected,
                () =>
                {
                    using var reader = utils.ReadBinaryFileInModLocation(path, item);
                    return System.Text.Encoding.UTF8.GetString(reader.ReadBytes(1024)).TrimEnd();
                }
            );
        }
    }
}

// What stays refused: an item naming no loaded mod, the base game's default item, and an item
// with the client mod's id whose rooted Name points elsewhere.
var unloaded = new MyObjectBuilder_Checkpoint.ModItem("999999999.sbm", 999999999, "Steam");
Check(
    "refused: exists, unloaded Workshop item",
    false,
    () => utils.FileExistsInModLocation(@"Textures\ActionIcons\ToggleEnabled.dds", unloaded)
);
Check(
    "refused: read, unloaded Workshop item",
    nameof(FileNotFoundException),
    () => utils.ReadFileInModLocation(@"Data\VanillaDefinitions.txt", unloaded)
);
Check(
    "refused: exists, default item",
    false,
    () => utils.FileExistsInModLocation(files[0], default)
);
var borrowed = new MyObjectBuilder_Checkpoint.ModItem(root + "/", 514062285, "Steam");
Check(
    "refused: exists, client mod id with a rooted Name",
    false,
    () => utils.FileExistsInModLocation("workshop/content/244850/758597413/" + files[1], borrowed)
);

Directory.Delete(root, true);
Console.WriteLine($"SUMMARY pass={pass} fail={fail}");
return fail == 0 ? 0 : 1;

static MyObjectBuilder_Checkpoint.ModItem WorkshopItem(ulong id, string folder)
{
    var item = new MyObjectBuilder_Checkpoint.ModItem(id, "Steam");
    item.SetModData(new FolderItem(folder));
    return item;
}

class FolderItem : MyWorkshopItem
{
    public FolderItem(string folder) => Folder = folder;
}

static class GameAssemblies
{
    // The game assemblies load from Bin64 (see ModLocation.csproj). Main's locals are game
    // types, so the resolver has to be in place before Main is compiled.
    [ModuleInitializer]
    internal static void Init()
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: ModLocation <LinuxCompat.dll> <Bin64>");
            Environment.Exit(2);
        }
        var bin64 = args[2];
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            var p = Path.Combine(bin64, name.Name + ".dll");
            return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null;
        };
    }
}

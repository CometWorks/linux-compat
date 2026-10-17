using ClientPlugin.Patches.PathHandling;
using PluginSdk.Config;

namespace ServerPlugin.Config;

// Server-side configuration, declared through Magnetar's PluginSdk and stored in
// LinuxCompat.cfg in the server's user data folder. Admins can edit it in Quasar. The path
// security fixes read it through PathSecurity.Config. The client has the same switches in
// ClientPlugin.Config.
[Section("security", caption: "Mod path security")]
public class LinuxCompatConfig : PluginConfig, IPathSecurityConfig
{
    private const string RecompileNote =
        " Applies to mods compiled after the change: restart the server, and clear the Performance plugin's compiled mods cache if it is in use.";

    private bool refuseUnloadedModItems = true;
    private bool refuseLeftoverDrives = true;
    private bool checkSafePathAsOpened = true;
    private bool refuseShimUse = true;
    private bool translateAssignmentValues = true;

    [BoolOption(
        "Refuse unloaded mod items: mod file reads and checks accept only a ModItem of a mod loaded in the world. Off: any ModItem, as on Windows, so a crafted one can point at any folder.",
        Parent = "security"
    )]
    public bool RefuseUnloadedModItems
    {
        get => refuseUnloadedModItems;
        set => SetField(ref refuseUnloadedModItems, value);
    }

    [BoolOption(
        "Refuse leftover drive letters: mod location and game content reads refuse a path that still has a drive letter after translation, like C:C:\\..., which would reach outside the folder.",
        Parent = "security"
    )]
    public bool RefuseLeftoverDrives
    {
        get => refuseLeftoverDrives;
        set => SetField(ref refuseLeftoverDrives, value);
    }

    [BoolOption(
        "Check serializer paths as opened: PathUtils.EnsureSafePath, used by MyObjectBuilderSerializer, checks the path the file system will open and refuses a leftover drive letter. Off: it checks the path translated once.",
        Parent = "security"
    )]
    public bool CheckSafePathAsOpened
    {
        get => checkSafePathAsOpened;
        set => SetField(ref checkSafePathAsOpened, value);
    }

    [BoolOption(
        "Refuse mods using the rewriter shims: a mod that calls LinuxCompat's rewriter shims itself, which could read native paths, fails to compile."
            + RecompileNote,
        Parent = "security"
    )]
    public bool RefuseShimUse
    {
        get => refuseShimUse;
        set => SetField(ref refuseShimUse, value);
    }

    [BoolOption(
        "Translate assignment values: a definition path assignment used as a value, like p = (def.Model = x), gives back the Windows path instead of the stored native one."
            + RecompileNote,
        Parent = "security"
    )]
    public bool TranslateAssignmentValues
    {
        get => translateAssignmentValues;
        set => SetField(ref translateAssignmentValues, value);
    }
}

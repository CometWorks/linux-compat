using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClientPlugin.Patches.PathHandling;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Elements;

namespace ClientPlugin;

// Client-side configuration, edited through the in-game settings dialog (see
// ClientPlugin/Settings) and stored in Storage/LinuxCompat.cfg. The path security fixes read
// it through PathSecurity.Config. The server has the same switches in
// ServerPlugin.Config.LinuxCompatConfig.
public class Config : INotifyPropertyChanged, IPathSecurityConfig
{
    private const string RecompileNote =
        " Applies to mods compiled after the change: reload the world, and clear the Performance plugin's compiled mods cache if it is in use.";

    private bool refuseUnloadedModItems = true;
    private bool refuseLeftoverDrives = true;
    private bool checkSafePathAsOpened = true;
    private bool refuseShimUse = true;
    private bool translateAssignmentValues = true;

    public readonly string Title = "LinuxCompat";

    [Separator("Mod path security")]
    [Checkbox(
        label: "Refuse unloaded mod items",
        description: "Mod file reads and checks accept only a ModItem of a mod loaded in the world. Off: any ModItem, as on Windows, so a crafted one can point at any folder."
    )]
    public bool RefuseUnloadedModItems
    {
        get => refuseUnloadedModItems;
        set => SetField(ref refuseUnloadedModItems, value);
    }

    [Checkbox(
        label: "Refuse leftover drive letters",
        description: "Mod location and game content reads refuse a path that still has a drive letter after translation, like C:C:\\..., which would reach outside the folder."
    )]
    public bool RefuseLeftoverDrives
    {
        get => refuseLeftoverDrives;
        set => SetField(ref refuseLeftoverDrives, value);
    }

    [Checkbox(
        label: "Check serializer paths as opened",
        description: "PathUtils.EnsureSafePath, used by MyObjectBuilderSerializer, checks the path the file system will open and refuses a leftover drive letter. Off: it checks the path translated once."
    )]
    public bool CheckSafePathAsOpened
    {
        get => checkSafePathAsOpened;
        set => SetField(ref checkSafePathAsOpened, value);
    }

    [Checkbox(
        label: "Refuse mods using the rewriter shims",
        description: "A mod that calls LinuxCompat's rewriter shims itself, which could read native paths, fails to compile."
            + RecompileNote
    )]
    public bool RefuseShimUse
    {
        get => refuseShimUse;
        set => SetField(ref refuseShimUse, value);
    }

    [Checkbox(
        label: "Translate assignment values",
        description: "A definition path assignment used as a value, like p = (def.Model = x), gives back the Windows path instead of the stored native one."
            + RecompileNote
    )]
    public bool TranslateAssignmentValues
    {
        get => translateAssignmentValues;
        set => SetField(ref translateAssignmentValues, value);
    }

    #region Property change notification boilerplate

    public static readonly Config Default = new Config();
    public static readonly Config Current = ConfigStorage.Load();

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}

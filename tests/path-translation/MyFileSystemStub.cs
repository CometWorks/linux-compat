namespace VRage.FileSystem;

/// <summary>
/// Stands in for the game's MyFileSystem, whose RootPath the launcher sets to the folder above
/// the executable directory before any plugin runs. PathTranslation reads only RootPath.
/// </summary>
public static class MyFileSystem
{
    public static string RootPath;
}

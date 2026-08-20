using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;

namespace ClientPlugin.Patches.PathHandling;

// Use Path.GetFileName for native directory paths shown in folder browsers.
[HarmonyPatch(typeof(MyGuiFolderScreen), "RepopulateList")]
[HarmonyPatchCategory("Finish")]
static class MyGuiFolderScreenRepopulatePatch
{
    static bool Prefix(MyGuiFolderScreen __instance)
    {
        var fileList = __instance.m_fileList;
        fileList.Items.Clear();

        var directoryItems = new List<MyGuiControlListbox.Item>();
        var fileItems = new List<MyGuiControlListbox.Item>();

        var path = Path.Combine(__instance.m_rootPath, __instance.m_pathLocalCurrent);
        if (!Directory.Exists(path))
            return false;

        foreach (var directory in Directory.GetDirectories(path))
        {
            var name = Path.GetFileName(directory);
            if (__instance.m_isItem(directory))
            {
                var itemInfo = new MyFileItem
                {
                    Type = MyFileItemType.File,
                    Name = name,
                    Path = directory,
                };
                fileItems.Add(
                    new MyGuiControlListbox.Item(
                        new StringBuilder(name),
                        directory,
                        MyGuiConstants.TEXTURE_ICON_BLUEPRINTS_FOLDER.Normal,
                        itemInfo
                    )
                );
            }
            else
            {
                var itemInfo = new MyFileItem
                {
                    Type = MyFileItemType.Directory,
                    Name = name,
                    Path = directory,
                };
                directoryItems.Add(
                    new MyGuiControlListbox.Item(
                        new StringBuilder(name),
                        directory,
                        MyGuiConstants.TEXTURE_ICON_MODS_LOCAL.Normal,
                        itemInfo
                    )
                );
            }
        }

        if (!string.IsNullOrEmpty(__instance.m_pathLocalCurrent))
        {
            var parentInfo = new MyFileItem
            {
                Type = MyFileItemType.Directory,
                Name = string.Empty,
                Path = string.Empty,
            };
            fileList.Add(
                new MyGuiControlListbox.Item(
                    new StringBuilder("[..]"),
                    __instance.m_pathLocalCurrent,
                    MyGuiConstants.TEXTURE_ICON_MODS_LOCAL.Normal,
                    parentInfo
                )
            );
        }

        foreach (var item in directoryItems)
            fileList.Add(item);

        foreach (var item in fileItems)
            fileList.Add(item);

        __instance.UpdatePathLabel();
        fileList.SelectedItems.Clear();
        return false;
    }
}

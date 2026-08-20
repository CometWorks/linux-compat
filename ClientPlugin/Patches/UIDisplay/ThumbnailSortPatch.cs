using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Sandbox;
using Sandbox.Engine.Networking;
using Sandbox.Game;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Graphics.GUI;
using SpaceEngineers.Game.GUI;
using VRage.FileSystem;
using VRage.Game;
using VRage.GameServices;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageRender;

namespace ClientPlugin.Patches.UIDisplay;

// Prefer the main save thumbnail. Linux directory enumeration is unordered,
// so sort dated backup folders before selecting the latest fallback.

[HarmonyPatch(typeof(MyGuiScreenLoadSandbox), "LoadImagePreview")]
[HarmonyPatchCategory("Finish")]
static class MyGuiScreenLoadSandboxLoadImagePreviewPatch
{
    static bool Prefix(MyGuiScreenLoadSandbox __instance)
    {
        var saveBrowser = __instance.m_saveBrowser;
        var levelImage = __instance.m_levelImage;
        var selectedRow = __instance.m_selectedRow;

        var row =
            (selectedRow >= saveBrowser.Rows.Count || selectedRow < 0)
                ? null
                : saveBrowser.Rows[selectedRow];

        if (row == null)
        {
            levelImage.SetTexture();
            return false;
        }

        saveBrowser.GetSave(row, out var info);
        if (!info.Valid || info.WorldInfo.IsCorrupted)
        {
            levelImage.SetTexture("Textures\\GUI\\Screens\\image_background.dds");
            return false;
        }

        if (info.IsCloud)
        {
            string thumbDir = Path.Combine(MyFileSystem.TempPath, "thumbs/");
            string thumbImage = Path.Combine(thumbDir, info.Name.GetHashCode().ToString()) + ".jpg";
            if (File.Exists(thumbImage))
            {
                levelImage.SetTexture(thumbImage);
                return false;
            }
            MyGameService.LoadFromCloudAsync(
                MyCloudHelper.Combine(info.Name, MyTextConstants.SESSION_THUMB_NAME_AND_EXTENSION),
                delegate(byte[] data)
                {
                    if (data != null)
                    {
                        try
                        {
                            Directory.CreateDirectory(thumbDir);
                            File.WriteAllBytes(thumbImage, data);
                            MyRenderProxy.UnloadTexture(thumbImage);
                            levelImage.SetTexture(thumbImage);
                            return;
                        }
                        catch { }
                    }
                    levelImage.SetTexture("Textures\\GUI\\Screens\\image_background.dds");
                }
            );
            return false;
        }

        string name = info.Name;
        string mainThumb = Path.Combine(name, MyTextConstants.SESSION_THUMB_NAME_AND_EXTENSION);
        if (File.Exists(mainThumb) && new FileInfo(mainThumb).Length > 0)
        {
            levelImage.SetTexture();
            levelImage.SetTexture(mainThumb);
            return false;
        }

        if (Directory.Exists(name + MyGuiScreenLoadSandbox.CONST_BACKUP))
        {
            string[] directories = Directory.GetDirectories(
                name + MyGuiScreenLoadSandbox.CONST_BACKUP
            );
            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            if (directories.Any())
            {
                string backupThumb = Path.Combine(
                    directories.Last(),
                    MyTextConstants.SESSION_THUMB_NAME_AND_EXTENSION
                );
                if (File.Exists(backupThumb) && new FileInfo(backupThumb).Length > 0)
                {
                    levelImage.SetTexture(backupThumb);
                    return false;
                }
            }
            levelImage.SetTexture("Textures\\GUI\\Screens\\image_background.dds");
        }
        else
        {
            levelImage.SetTexture("Textures\\GUI\\Screens\\image_background.dds");
        }

        return false;
    }
}

[HarmonyPatch(typeof(MyGuiScreenMainMenu), "GetThumbnail")]
[HarmonyPatchCategory("Finish")]
static class MyGuiScreenMainMenuGetThumbnailPatch
{
    static bool Prefix(MyObjectBuilder_LastSession session, ref string __result)
    {
        string text = session?.Path;
        if (text == null || !Directory.Exists(text))
        {
            string text2 = session?.RelativePath;
            if (text2 == null)
            {
                __result = null;
                return false;
            }
            text = session.GetFullPath(text2);
        }

        string mainThumb = Path.Combine(text, MyTextConstants.SESSION_THUMB_NAME_AND_EXTENSION);
        if (File.Exists(mainThumb) && new FileInfo(mainThumb).Length > 0)
        {
            __result = mainThumb;
            return false;
        }

        if (Directory.Exists(text + MyGuiScreenLoadSandbox.CONST_BACKUP))
        {
            string[] directories = Directory.GetDirectories(
                text + MyGuiScreenLoadSandbox.CONST_BACKUP
            );
            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            if (directories.Any())
            {
                string backupThumb = Path.Combine(
                    directories.Last(),
                    MyTextConstants.SESSION_THUMB_NAME_AND_EXTENSION
                );
                if (File.Exists(backupThumb) && new FileInfo(backupThumb).Length > 0)
                {
                    __result = backupThumb;
                    return false;
                }
            }
        }

        if (MySandboxGame.Config.UseCloudToSaveGame)
        {
            byte[] array = MyGameService.LoadFromCloud(
                MyCloudHelper.Combine(
                    MyCloudHelper.LocalToCloudWorldPath(text + "/"),
                    MyTextConstants.SESSION_THUMB_NAME_AND_EXTENSION
                )
            );
            if (array != null)
            {
                try
                {
                    string cloudThumb = Path.Combine(
                        text,
                        MyTextConstants.SESSION_THUMB_NAME_AND_EXTENSION
                    );
                    Directory.CreateDirectory(text);
                    File.WriteAllBytes(cloudThumb, array);
                    MyRenderProxy.UnloadTexture(cloudThumb);
                    __result = cloudThumb;
                    return false;
                }
                catch { }
            }
        }

        __result = null;
        return false;
    }
}

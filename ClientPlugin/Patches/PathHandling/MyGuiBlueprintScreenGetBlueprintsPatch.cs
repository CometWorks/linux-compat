using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Sandbox.Engine.Networking;
using Sandbox.Game.GUI;
using Sandbox.Game.Gui;
using VRage.FileSystem;
using VRage.Utils;

namespace ClientPlugin.Patches.PathHandling;

// Use native path operations for local blueprint probes and display names.
[HarmonyPatch(typeof(MyGuiBlueprintScreen_Reworked), "GetBlueprints")]
[HarmonyPatchCategory("Finish")]
static class MyGuiBlueprintScreenGetBlueprintsPatch
{
    static bool Prefix(
        MyGuiBlueprintScreen_Reworked __instance,
        string directory,
        MyBlueprintTypeEnum type
    )
    {
        var data = new List<MyBlueprintItemInfo>();
        if (!Directory.Exists(directory))
            return false;

        var directories = Directory.GetDirectories(directory);
        foreach (var dir in directories)
        {
            var path = Path.Combine(dir, "bp.sbc");
            if (!File.Exists(path))
                continue;

            var name = Path.GetFileName(dir);
            var info = new MyBlueprintItemInfo(type)
            {
                TimeCreated = File.GetCreationTimeUtc(path),
                TimeUpdated = File.GetLastWriteTimeUtc(path),
                BlueprintName = name,
                Size = MyFileSystem.GetStorageSize(dir),
            };
            info.SetAdditionalBlueprintInformation(name, name);
            data.Add(info);
        }

        __instance.SortBlueprints(data, MyBlueprintTypeEnum.LOCAL);
        __instance.AddBlueprintButtons(ref data);
        return false;
    }
}

// Callers use direct File.Exists checks before the renderer can resolve the thumbnail path.
[HarmonyPatch(typeof(MyGuiBlueprintScreen_Reworked), "GetImagePath")]
[HarmonyPatchCategory("Finish")]
static class MyGuiBlueprintScreenGetImagePathPatch
{
    static void Postfix(MyBlueprintItemInfo data, ref string __result)
    {
        __result = PathCache.ResolveAbsolute(__result);
        if (data.Type != MyBlueprintTypeEnum.CLOUD || File.Exists(__result))
            return;

        var cloudPath = data.CloudPathXML?.Replace(
            MyBlueprintUtils.BLUEPRINT_LOCAL_NAME,
            MyBlueprintUtils.THUMB_IMAGE_NAME
        );
        cloudPath ??= data.CloudPathCS?.Replace(
            MyBlueprintUtils.DEFAULT_SCRIPT_NAME + MyBlueprintUtils.SCRIPT_EXTENSION,
            MyBlueprintUtils.THUMB_IMAGE_NAME
        );
        if (cloudPath == null)
            return;

        try
        {
            var image = MyGameService.LoadFromCloud(cloudPath);
            if (image == null)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(__result));
            File.WriteAllBytes(__result, image);
        }
        catch (Exception e)
        {
            MyLog.Default.WriteLine($"Failed to cache cloud blueprint thumbnail {cloudPath}: {e}");
        }
    }
}

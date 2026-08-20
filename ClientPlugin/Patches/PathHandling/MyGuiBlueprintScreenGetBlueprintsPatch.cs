using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Sandbox.Game.Gui;
using VRage.FileSystem;

namespace ClientPlugin.Patches.PathHandling;

// Use native path operations for local blueprint probes and display names.
[HarmonyPatch(typeof(MyGuiBlueprintScreen_Reworked), "GetBlueprints")]
[HarmonyPatchCategory("Finish")]
static class MyGuiBlueprintScreenGetBlueprintsPatch
{
    static bool Prefix(
        MyGuiBlueprintScreen_Reworked __instance,
        string directory,
        MyBlueprintTypeEnum type)
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

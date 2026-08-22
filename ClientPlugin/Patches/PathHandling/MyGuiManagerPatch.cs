using System.Collections.Generic;
using System.IO;
using EmptyKeys.UserInterface;
using HarmonyLib;
using Sandbox.Graphics;
using VRage.FileSystem;

namespace ClientPlugin.Patches.PathHandling;

// EmptyKeys lookups use the Windows-style asset names stored in game definitions.
[HarmonyPatch(typeof(MyGuiManager), "LoadTexturesToImageManager")]
[HarmonyPatchCategory("Finish")]
static class MyGuiManagerLoadTexturesToImageManagerPatch
{
    static bool Prefix(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(MyFileSystem.ContentPath, file);
            ImageManager.Instance.AddImage(PathHelpers.ToWindowsPath(relative));
        }

        return false;
    }
}

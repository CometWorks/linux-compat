using System;
using System.IO;
using ClientPlugin.Compatibility;
using HarmonyLib;
using VRage.FileSystem;

namespace ClientPlugin.Patches.Rendering;

// DXVK keeps its shader IR cache (<exe-hash>.dxvk.bin and .dxvk.lut) in
// ~/.cache/dxvk unless DXVK_SHADER_CACHE_PATH names a directory. Point it at
// the game's own ShaderCache2 folder so both shader caches live with the game
// data, and drop the DXBC cache pairs left there by earlier runs.
//
// DxvkDevice reads the variable when the D3D11 device is created, which
// happens in the MySandboxGame constructor - after MyFileSystem.Init and
// before IPlugin.Init. This postfix is therefore the earliest point where the
// real user data path is known and still early enough to take effect.
[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.Init))]
[HarmonyPatchCategory("Finish")]
static class DxvkShaderCachePatch
{
    static void Postfix()
    {
        string directory = Path.Combine(MyFileSystem.UserDataPath, "ShaderCache2");

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception e)
        {
            Console.WriteLine(
                $"[LinuxCompat] WARNING: cannot create shader cache directory {directory}: {e.Message}"
            );
            return;
        }

        DeleteShaderCacheFiles(directory);

        NativeLibraries.SetNativeEnvironmentVariable("DXVK_SHADER_CACHE_PATH", directory);
        Console.WriteLine($"[LinuxCompat] DXVK shader cache directory: {directory}");
    }

    // Removes the .hash and .cache pairs MyShaderCache wrote in earlier runs.
    // DXVK's own files end in .bin and .lut and are left alone, as is anything
    // else in the folder.
    private static void DeleteShaderCacheFiles(string directory)
    {
        int removed = 0;
        int failed = 0;

        try
        {
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                string extension = Path.GetExtension(file);
                if (
                    !extension.Equals(".cache", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".hash", StringComparison.OrdinalIgnoreCase)
                )
                    continue;

                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch (Exception)
                {
                    failed++;
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(
                $"[LinuxCompat] WARNING: cannot clean shader cache directory {directory}: {e.Message}"
            );
            return;
        }

        if (removed > 0 || failed > 0)
            Console.WriteLine(
                $"[LinuxCompat] Deleted {removed} shader cache files from {directory}"
                    + (failed > 0 ? $" ({failed} could not be deleted)" : "")
            );
    }
}

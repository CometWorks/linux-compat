using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ClientPlugin.Tools;
using HarmonyLib;
using VRage.FileSystem;
using VRage.Render11.Resources;
using VRage.Utils;

namespace ClientPlugin.Patches.PathHandling;

// Logs raw, normalized, and case-resolved atlas path availability during
// render-device initialization. This must not affect startup.
[HarmonyPatch(typeof(MyTextureAtlas), MethodType.Constructor, typeof(string), typeof(string))]
[HarmonyPatchCategory("Finish")]
// ReSharper disable once UnusedType.Global
static class MyTextureAtlasCtorRegressionPatch
{
    // ReSharper disable once UnusedMember.Local
    static void Prefix(string textureDir, string atlasFile)
    {
        try
        {
            string contentPath;
            string contentPathError = null;
            try { contentPath = MyFileSystem.ContentPath; }
            catch (Exception ex) { contentPath = null; contentPathError = ex.GetType().Name + ": " + ex.Message; }

            string combined = contentPath != null ? Path.Combine(contentPath, atlasFile ?? "") : null;
            string normalized = combined != null ? combined.Replace('\\', '/') : null;
            string resolved = (normalized != null && Path.IsPathRooted(normalized))
                ? PathCache.ResolveAbsolute(normalized) : normalized;

            bool existsRaw       = combined   != null && File.Exists(combined);
            bool existsNormalized = normalized != null && File.Exists(normalized);
            bool existsResolved  = resolved   != null && File.Exists(resolved);

            MyLog.Default.WriteLine(
                "[LinuxCompat] MyTextureAtlas..ctor regression check: " +
                "textureDir=" + (textureDir ?? "<null>") +
                ", atlasFile=" + (atlasFile ?? "<null>") +
                ", ContentPath=" + (contentPath ?? "<null>") +
                (contentPathError != null ? " (getter threw: " + contentPathError + ")" : "") +
                ", combined=" + (combined ?? "<null>") +
                ", normalized=" + (normalized ?? "<null>") +
                ", resolved=" + (resolved ?? "<null>") +
                ", existsRaw=" + existsRaw +
                ", existsNormalized=" + existsNormalized +
                ", existsResolved=" + existsResolved);
        }
        catch
        {
            // Diagnostic only; must never break game startup.
        }
    }
}

// Atlas manifests contain Windows paths. Normalize only the GetFileName call;
// keep material texture keys in Windows form for mod compatibility.
[HarmonyPatch(typeof(MyTextureAtlas))]
[HarmonyPatchCategory("Finish")]
// ReSharper disable once UnusedType.Global
static class MyTextureAtlasParseAtlasDescriptionPatch
{
    // ReSharper disable once UnusedMember.Local
    [HarmonyTranspiler]
    [HarmonyPatch("ParseAtlasDescription")]
    static IEnumerable<CodeInstruction> ParseAtlasDescriptionTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase patchedMethod)
    {
        var il = instructions.ToList();
        il.RecordOriginalCode(patchedMethod);

        var target = typeof(Path).GetMethod(nameof(Path.GetFileName), new[] { typeof(string) });
        var replacement = typeof(PathHelpers).GetMethod(nameof(PathHelpers.GetFileName), new[] { typeof(string) });

        // Mutate the operand in place so any branch labels or exception
        // blocks attached to the call instruction stay anchored to it.
        foreach (var instr in il)
        {
            if (instr.opcode == OpCodes.Call && instr.operand is MethodInfo mi && mi == target)
                instr.operand = replacement;
        }

        il.RecordPatchedCode(patchedMethod);
        return il;
    }
}

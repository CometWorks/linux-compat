using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ClientPlugin.Tools;
using HarmonyLib;
using Sandbox.Definitions;
using VRage.FileSystem;
using VRage.Game;
using VRage.Utils;

namespace ClientPlugin.Patches.PathHandling;

// Missing mod Data directories leave null definition lists that must be skipped.

[HarmonyPatch(typeof(MyDefinitionManager), "LoadDefinitions",
    new[] { typeof(List<MyModContext>), typeof(List<MyDefinitionManager.DefinitionSet>) })]
[HarmonyPatchCategory("Finish")]
static class MyDefinitionManagerLoadDefinitionsPatch
{
    static bool Prefix(MyDefinitionManager __instance,
        List<MyModContext> contexts,
        List<MyDefinitionManager.DefinitionSet> definitionSets)
    {
        var list = new List<List<Tuple<MyObjectBuilder_Definitions, string>>>();
        for (int i = 0; i < contexts.Count; i++)
        {
            var ctx = contexts[i];
            if (!MyFileSystem.DirectoryExists(ctx.ModPathData))
            {
                list.Add(null);
                continue;
            }
            definitionSets[i].Context = ctx;
            __instance.m_transparentMaterialsInitialized = false;
            var preloadSet = ctx.IsBaseGame ? __instance.m_mainMenuPreloadSet : null;
            var builders = __instance.GetDefinitionBuilders(ctx, preloadSet, ctx.IsBaseGame);
            list.Add(builders);
            if (builders == null)
                return false;
        }

        Action<MyObjectBuilder_Definitions, MyModContext, MyDefinitionManager.DefinitionSet, bool>[] phases =
        {
            __instance.CompatPhase,
            __instance.LoadPhase1,
            __instance.LoadPhase2,
            __instance.LoadPhase3,
            __instance.LoadPhase4,
            __instance.LoadPhase5,
        };

        for (int j = 0; j < phases.Length; j++)
        {
            for (int k = 0; k < contexts.Count; k++)
            {
                __instance.m_currentLoadingSet = definitionSets[k];
                if (list[k] == null)
                {
                    MyLog.Default.Warning($"Missing definition {k}; Look for a Linux path conversation issue.");
                    continue;
                }

                try
                {
                    foreach (var item in list[k])
                    {
                        contexts[k].CurrentFile = item.Item2;
                        phases[j](item.Item1, contexts[k], definitionSets[k], true);
                    }
                }
                catch (Exception innerException)
                {
                    MyDefinitionManager.FailModLoading(contexts[k], j, phases.Length, innerException);
                    continue;
                }
                __instance.MergeDefinitions();
            }
        }

        for (int l = 0; l < contexts.Count; l++)
            __instance.AfterLoad(contexts[l], definitionSets[l]);

        MyDefinitionManager.m_directoryExistCache.Clear();
        return false;
    }
}

// TransparentMaterials.sbc stores backslashes that Linux Path does not treat as separators.
// Rewrite filename extraction without changing the public Texture value exposed to mods.
[HarmonyPatch(typeof(MyDefinitionManager))]
[HarmonyPatchCategory("Finish")]
// ReSharper disable once UnusedType.Global
static class MyDefinitionManagerCreateTransparentMaterialsPatch
{
    // ReSharper disable once UnusedMember.Local
    [HarmonyTranspiler]
    [HarmonyPatch("CreateTransparentMaterials")]
    static IEnumerable<CodeInstruction> CreateTransparentMaterialsTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase patchedMethod)
    {
        var il = instructions.ToList();
        il.RecordOriginalCode(patchedMethod);

        var target = typeof(Path).GetMethod(nameof(Path.GetFileNameWithoutExtension), new[] { typeof(string) });
        var replacement = typeof(PathHelpers).GetMethod(nameof(PathHelpers.GetFileNameWithoutExtension), new[] { typeof(string) });

        // Preserve branch labels and exception blocks attached to the call instruction.
        foreach (var instr in il)
        {
            if (instr.opcode == OpCodes.Call && instr.operand is MethodInfo mi && mi == target)
                instr.operand = replacement;
        }

        il.RecordPatchedCode(patchedMethod);
        return il;
    }
}

[HarmonyPatch(typeof(MyDefinitionManager), "ProcessContentFilePath")]
[HarmonyPatchCategory("Finish")]
static class MyDefinitionManagerProcessContentFilePathPatch
{
    static bool Prefix(MyModContext context, ref string contentFile, object[] extensions, bool logNoExtensions)
    {
        if (string.IsNullOrEmpty(contentFile))
            return false;

        contentFile = PathHelpers.Normalize(contentFile);
        string extension = Path.GetExtension(contentFile);

        if (extensions == null || extensions.Length == 0)
        {
            if (logNoExtensions)
                MyDefinitionErrors.Add(context, "List of supported file extensions not found. (Internal error)", TErrorSeverity.Warning);
            return false;
        }

        if (string.IsNullOrEmpty(extension))
        {
            MyDefinitionErrors.Add(context, "File does not have a proper extension: " + contentFile, TErrorSeverity.Warning);
            return false;
        }

        bool extensionOk = false;
        foreach (var e in extensions)
        {
            if (string.Equals(e as string, extension, StringComparison.OrdinalIgnoreCase))
            {
                extensionOk = true;
                break;
            }
        }
        if (!extensionOk)
        {
            MyDefinitionErrors.Add(context, "File extension of: " + contentFile + " is not supported.", TErrorSeverity.Warning);
            return false;
        }

        string resolved = CaseInsensitivePathResolver.Resolve(contentFile, context.ModPath);
        if (!MyDefinitionManager.m_directoryExistCache.TryGetValue(resolved, out var exists))
        {
            exists = MyFileSystem.DirectoryExists(Path.GetDirectoryName(resolved))
                  && System.Linq.Enumerable.Any(MyFileSystem.GetFiles(
                        Path.GetDirectoryName(resolved),
                        Path.GetFileName(resolved),
                        MySearchOption.TopDirectoryOnly));
            MyDefinitionManager.m_directoryExistCache.Add(resolved, exists);
        }

        if (exists)
        {
            contentFile = resolved;
        }
        else if (!MyFileSystem.FileExists(PathHelpers.ResolveContentFilePath(contentFile, MyFileSystem.ContentPath)))
        {
            if (contentFile.EndsWith(".mwm"))
            {
                MyDefinitionErrors.Add(context, "Resource not found, setting to error model. Resource path: " + resolved, TErrorSeverity.Error);
                contentFile = "Models/Debug/Error.mwm";
            }
            else
            {
                MyDefinitionErrors.Add(context, "Resource not found, setting to null. Resource path: " + resolved, TErrorSeverity.Error);
                contentFile = null;
            }
        }

        return false;
    }
}

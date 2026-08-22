using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using ClientPlugin.Compatibility.Rendering;
using HarmonyLib;
using SharpDX.Direct3D;
using VRage.Library.Utils;
using VRage.Render11.Shader;
using VRageRender;

namespace ClientPlugin.Patches.Rendering;

// Replaces the lower MyShaderCompiler.Compile overload with a
// vanilla-equivalent implementation that routes D3DPreprocess and D3DCompile
// through the PE-loaded d3dcompiler_47.dll. The cache key is the real
// preprocessed source, so the 1,724 entries shipped in Content/ShaderCache
// are hit exactly like on Windows.
[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
static class ShaderCompilerPatch
{
    // Vanilla's per-hash exclusion: only one thread may look up, compile and
    // store a given permutation at a time.
    private sealed class InProgressMonitor
    {
        private readonly HashSet<string> inProcess = new();

        public void Begin(string hash)
        {
            while (true)
            {
                lock (inProcess)
                {
                    if (inProcess.Add(hash))
                        break;
                }
                Thread.Sleep(1);
            }
        }

        public void End(string hash)
        {
            lock (inProcess)
            {
                inProcess.Remove(hash);
            }
        }
    }

    private static readonly InProgressMonitor InProgress = new();

    static MethodBase TargetMethod()
    {
        var type = typeof(MyShaderCompiler);
        foreach (
            var method in type.GetMethods(
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
            )
        )
        {
            if (method.Name != "Compile")
                continue;
            var parameters = method.GetParameters();
            if (
                parameters.Length == 11
                && parameters[0].ParameterType == typeof(string)
                && parameters[6].IsOut
            )
                return method;
        }
        throw new Exception("[LinuxCompat] Cannot find MyShaderCompiler.Compile overload");
    }

    static bool Prefix(
        ref byte[] __result,
        string filepath,
        ShaderMacro[] macros,
        MyShaderProfile profile,
        string sourceDescriptor,
        bool optimize,
        bool invalidateCache,
        ref bool wasCached,
        ref string compileLog,
        ref string hash,
        bool savePdb,
        bool savePreprocessed
    )
    {
        filepath = PathUtils.Normalize(filepath);

        var globalMacros = MyShaderCompiler.m_globalShaderMacros ?? Array.Empty<ShaderMacro>();
        var macroList = new List<ShaderMacro>();
        macroList.AddRange(globalMacros);
        macroList.AddRange(macros);

        MyShaderCompiler.FillGlobalMacros(macroList, optimize);
        macros = macroList.ToArray();

        string entryPoint = MyShaderCompiler.ProfileEntryPoint(profile);
        string profileStr = MyShaderCompiler.ProfileToString(profile);

        wasCached = false;
        compileLog = null;

        string shadersPath = MyShaderCompiler.ShadersPath;
        string resolvedFilepath = GetSourceFilepath(filepath, shadersPath);

        string preprocessedSource = D3DCompilerLinux.Preprocess(
            resolvedFilepath,
            macros,
            shadersPath,
            out var errors
        );
        if (preprocessedSource == null)
        {
            compileLog = errors;
            hash = "";
            __result = null;
            return false;
        }

        hash = MyShaderCache.GetShaderHash(preprocessedSource, profile);

        if (!invalidateCache)
        {
            InProgress.Begin(hash);
            if (MyShaderCache.TryFetch(preprocessedSource, profile, hash, out var cachedBytecode))
            {
                InProgress.End(hash);
                wasCached = true;
                __result = cachedBytecode;
                return false;
            }
        }

        try
        {
            byte[] bytecode = D3DCompilerLinux.Compile(
                resolvedFilepath,
                macros,
                entryPoint,
                profileStr,
                optimize,
                shadersPath,
                out compileLog
            );

            if (bytecode != null && bytecode.Length != 0)
                MyShaderCache.Store(preprocessedSource, profile, bytecode, hash);

            __result = bytecode;
            return false;
        }
        catch (Exception ex)
        {
            compileLog = ex.Message;
            throw;
        }
        finally
        {
            InProgress.End(hash);
        }
    }

    private static string GetSourceFilepath(string filepath, string shadersPath)
    {
        string overrideRoot = Environment.GetEnvironmentVariable("SE_SHADER_OVERRIDE");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            string relativePath = Path.GetRelativePath(shadersPath, filepath);
            string fullOverrideRoot = Path.GetFullPath(overrideRoot);
            if (Directory.Exists(fullOverrideRoot))
            {
                string overridePath = Path.Combine(fullOverrideRoot, relativePath);
                if (File.Exists(overridePath))
                    return overridePath;
            }
        }
        return filepath;
    }
}

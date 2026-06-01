using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using ClientPlugin.Compatibility.Rendering;
using HarmonyLib;
using SharpDX.Direct3D;
using VRage.Library.Utils;
using VRage.Render11.Shader;
using VRageRender;

namespace ClientPlugin.Patches.Rendering;

[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
static class ShaderCompilerPatch
{
    static MethodBase TargetMethod()
    {
        var type = typeof(MyShaderCompiler);
        foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (method.Name != "Compile") continue;
            var parameters = method.GetParameters();
            if (parameters.Length == 11 && parameters[0].ParameterType == typeof(string) && parameters[6].IsOut)
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
        bool savePreprocessed)
    {
        filepath = PathUtils.Normalize(filepath);

        var globalMacros = MyShaderCompiler.m_globalShaderMacros ?? Array.Empty<ShaderMacro>();
        var macroList = new List<ShaderMacro>();
        macroList.AddRange(globalMacros);
        macroList.AddRange(macros);

        MyShaderCompiler.FillGlobalMacros(macroList, optimize);
        macros = macroList.ToArray();

        string entryPoint = profile switch
        {
            MyShaderProfile.vs_5_0 => "__vertex_shader",
            MyShaderProfile.ps_5_0 => "__pixel_shader",
            MyShaderProfile.gs_5_0 => "__geometry_shader",
            MyShaderProfile.cs_5_0 => "__compute_shader",
            _ => throw new Exception()
        };

        string profileStr = profile switch
        {
            MyShaderProfile.vs_5_0 => "vs_5_0",
            MyShaderProfile.ps_5_0 => "ps_5_0",
            MyShaderProfile.gs_5_0 => "gs_5_0",
            MyShaderProfile.cs_5_0 => "cs_5_0",
            _ => throw new Exception()
        };

        wasCached = false;
        compileLog = null;

        string macroHeader = BuildMacroHeader(macros);
        string resolvedFilepath = GetSourceFilepath(filepath);
        StringBuilder sb = new StringBuilder();
        if (!string.IsNullOrEmpty(macroHeader))
        {
            sb.Append(macroHeader);
            sb.AppendLine();
        }
        sb.Append(File.ReadAllText(resolvedFilepath));
        string preprocessedSource = sb.ToString();

        if (preprocessedSource == null)
        {
            hash = "";
            __result = null;
            return false;
        }

        hash = MyShaderCache.GetShaderHash(preprocessedSource, profile);

        if (!invalidateCache)
        {
            byte[] cachedBytes = null;
            if (MyShaderCache.TryFetch(preprocessedSource, profile, hash, out cachedBytes))
            {
                wasCached = true;
                __result = cachedBytes;
                return false;
            }
        }

        try
        {
            if (!wasCached)
            {
                string msg = $"WARNING: Shader was not precompiled - {sourceDescriptor} @ profile {profile} with defines {macros.GetString()}({hash})";
                MyRender11.Log.WriteLine(msg);
            }

            byte[] bytecode = D3DCompilerLinux.Compile(resolvedFilepath, macros, entryPoint, profileStr, optimize, out compileLog);
            if (bytecode != null)
            {
                MyShaderCache.Store(preprocessedSource, profile, bytecode, hash);
            }

            if (!string.IsNullOrEmpty(compileLog))
            {
                string arg = $"{sourceDescriptor} {profileStr} {macros.GetString()}";
                if (bytecode == null)
                {
                    string msg2 = $"Compilation of shader {arg} errors:\n{compileLog}";
                    MyRender11.Log.WriteLine(msg2);
                }
            }

            __result = bytecode;
            return false;
        }
        catch (Exception ex)
        {
            compileLog = ex.Message;
            throw;
        }
    }

    private static string BuildMacroHeader(ShaderMacro[] macros)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < macros.Length; i++)
        {
            ShaderMacro macro = macros[i];
            if (string.IsNullOrEmpty(macro.Name))
                continue;
            sb.Append("#define ");
            sb.Append(macro.Name);
            if (!string.IsNullOrWhiteSpace(macro.Definition))
            {
                sb.Append(' ');
                sb.Append(macro.Definition);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string GetSourceFilepath(string filepath)
    {
        string overrideRoot = Environment.GetEnvironmentVariable("SE_SHADER_OVERRIDE");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            string shadersPath = MyShaderCompiler.ShadersPath;
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

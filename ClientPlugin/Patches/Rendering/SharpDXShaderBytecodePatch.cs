using System;
using ClientPlugin.Compatibility.Rendering;
using HarmonyLib;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;

namespace ClientPlugin.Patches.Rendering;

// Plugins that compile their own shaders through SharpDX (HDR Render, SSGI)
// reach a P/Invoke of d3dcompiler_47.dll, a Windows DLL Linux can't load.
// Every ShaderBytecode.Compile and Preprocess overload ends in one of these
// two methods, so they are replaced with the same calls into the PE-loaded
// compiler, keeping SharpDX's results and exceptions.
[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
static class SharpDXShaderBytecodePatch
{
    [HarmonyPrefix]
    [HarmonyPatch(
        typeof(ShaderBytecode),
        nameof(ShaderBytecode.Compile),
        [
            typeof(IntPtr),
            typeof(int),
            typeof(string),
            typeof(string),
            typeof(ShaderFlags),
            typeof(EffectFlags),
            typeof(ShaderMacro[]),
            typeof(Include),
            typeof(string),
            typeof(SecondaryDataFlags),
            typeof(DataStream),
        ]
    )]
    static bool CompilePrefix(
        ref CompilationResult __result,
        IntPtr textSource,
        int textSize,
        string entryPoint,
        string profile,
        ShaderFlags shaderFlags,
        EffectFlags effectFlags,
        ShaderMacro[] defines,
        Include include,
        string sourceFileName,
        SecondaryDataFlags secondaryDataFlags,
        DataStream secondaryData
    )
    {
        if (string.IsNullOrWhiteSpace(profile))
            throw new ArgumentNullException(nameof(profile));
        string upperProfile = profile.ToUpperInvariant();
        if (
            !upperProfile.StartsWith("FX_")
            && !upperProfile.StartsWith("LIB_")
            && string.IsNullOrWhiteSpace(entryPoint)
        )
            throw new ArgumentNullException(nameof(entryPoint));

        // The wrapper calls D3DCompile, which has no secondary data inputs.
        if (secondaryDataFlags != SecondaryDataFlags.None || secondaryData != null)
            throw new NotSupportedException(
                "[LinuxCompat] Shader compilation with secondary data is not supported on Linux"
            );

        var result = new Result(
            D3DCompilerLinux.CompileForSharpDX(
                textSource,
                textSize,
                sourceFileName,
                defines,
                ToNative(include),
                entryPoint,
                profile,
                (uint)shaderFlags,
                (uint)effectFlags,
                out byte[] code,
                out string errors
            )
        );

        if (result.Failure)
        {
            if (errors == null)
                throw new SharpDXException(result);
            if (Configuration.ThrowOnShaderCompileError)
                throw new CompilationException(result, errors);
        }

        __result = new CompilationResult(
            code != null ? new ShaderBytecode(code) : null,
            result,
            errors
        );
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(
        typeof(ShaderBytecode),
        nameof(ShaderBytecode.Preprocess),
        [
            typeof(IntPtr),
            typeof(int),
            typeof(ShaderMacro[]),
            typeof(Include),
            typeof(string),
            typeof(string),
        ],
        [
            ArgumentType.Normal,
            ArgumentType.Normal,
            ArgumentType.Normal,
            ArgumentType.Normal,
            ArgumentType.Out,
            ArgumentType.Normal,
        ]
    )]
    static bool PreprocessPrefix(
        ref string __result,
        IntPtr shaderSourcePtr,
        int shaderSourceLength,
        ShaderMacro[] defines,
        Include include,
        out string compilationErrors,
        string sourceFileName
    )
    {
        compilationErrors = null;
        var result = new Result(
            D3DCompilerLinux.PreprocessForSharpDX(
                shaderSourcePtr,
                shaderSourceLength,
                sourceFileName,
                defines,
                ToNative(include),
                out string text,
                out string errors
            )
        );

        if (result.Failure)
        {
            if (errors == null)
                throw new SharpDXException(result);
            compilationErrors = errors;
            throw new CompilationException(result, errors);
        }

        __result = text;
        return false;
    }

    // SharpDX's own native shadow of the Include, the object its P/Invoke
    // would have passed.
    private static IntPtr ToNative(Include include) =>
        include == null ? IntPtr.Zero : CppObject.ToCallbackPtr<Include>(include);
}

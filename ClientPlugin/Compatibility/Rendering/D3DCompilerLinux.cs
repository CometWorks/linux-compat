using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SharpDX.Direct3D;

namespace ClientPlugin.Compatibility.Rendering;

public static class D3DCompilerLinux
{
    private const uint D3DCOMPILE_DEBUG = 0x01;
    private const uint D3DCOMPILE_OPTIMIZATION_LEVEL3 = 0x8000;

    // D3DCOMPILER_STRIP_REFLECTION_DATA | STRIP_DEBUG_INFO | STRIP_TEST_BLOBS
    // | STRIP_PRIVATE_DATA: the categories vanilla strips from optimized
    // shaders before caching them.
    private const uint STRIP_FLAGS = 0x0F;

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D_SHADER_MACRO
    {
        public IntPtr Name;
        public IntPtr Definition;
    }

    private static readonly Lazy<bool> Initialized = new(() =>
        NativeWrapper.Initialize("d3dcompiler_47.dll", Init)
    );

    [DllImport("libD3DCompiler.so", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Init(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string dllPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sidecarPath
    );

    [DllImport("libD3DCompiler.so")]
    private static extern IntPtr SE_CreateIncludeHandler(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string basePath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string includeDir
    );

    [DllImport("libD3DCompiler.so")]
    private static extern void SE_DestroyIncludeHandler(IntPtr pInclude);

    [DllImport("libD3DCompiler.so")]
    private static extern int SE_D3DPreprocess(
        IntPtr pSrcData,
        ulong srcDataSize,
        IntPtr pSourceName,
        IntPtr pDefines,
        IntPtr pInclude,
        out IntPtr ppCodeText,
        out IntPtr ppErrorMsgs
    );

    [DllImport("libD3DCompiler.so")]
    private static extern int SE_D3DCompile(
        IntPtr pSrcData,
        ulong srcDataSize,
        IntPtr pSourceName,
        IntPtr pDefines,
        IntPtr pInclude,
        IntPtr pEntrypoint,
        IntPtr pTarget,
        uint flags1,
        uint flags2,
        out IntPtr ppCode,
        out IntPtr ppErrorMsgs
    );

    [DllImport("libD3DCompiler.so")]
    private static extern int SE_D3DStripShader(
        IntPtr pShaderBytecode,
        ulong bytecodeLength,
        uint stripFlags,
        out IntPtr ppStrippedBlob
    );

    [DllImport("libD3DCompiler.so")]
    private static extern IntPtr SE_BlobGetBufferPointer(IntPtr blob);

    [DllImport("libD3DCompiler.so")]
    private static extern ulong SE_BlobGetBufferSize(IntPtr blob);

    [DllImport("libD3DCompiler.so")]
    private static extern uint SE_BlobRelease(IntPtr blob);

    /// <summary>
    /// Preprocesses a shader file the same way vanilla's
    /// ShaderBytecode.PreprocessFromFile does: the raw file text with an empty
    /// source name goes through D3DPreprocess with vanilla include semantics.
    /// The returned text is byte-for-byte compatible with the preprocessed
    /// sources stored in the shipped Content/ShaderCache. Returns null and
    /// sets errors on failure.
    /// </summary>
    internal static string Preprocess(
        string sourceFilePath,
        ShaderMacro[] macros,
        string includeDir,
        out string errors
    )
    {
        errors = null;
        string source;
        try
        {
            source = File.ReadAllText(sourceFilePath);
        }
        catch (Exception ex)
        {
            errors = ex.Message;
            return null;
        }

        _ = Initialized.Value;

        byte[] sourceBytes = Encoding.UTF8.GetBytes(source);
        IntPtr include = IntPtr.Zero;
        IntPtr pSourceName = IntPtr.Zero;
        IntPtr pSrcData = IntPtr.Zero;
        IntPtr ppCode = IntPtr.Zero;
        IntPtr ppErrorMsgs = IntPtr.Zero;
        var allocations = new List<IntPtr>();

        try
        {
            include = SE_CreateIncludeHandler(Path.GetDirectoryName(sourceFilePath), includeDir);

            // SharpDX PreprocessFromFile passes an empty source name; the
            // shipped cache entries all start with '#line 1 ""'.
            pSourceName = Marshal.StringToHGlobalAnsi("");
            pSrcData = Marshal.AllocHGlobal(sourceBytes.Length);
            Marshal.Copy(sourceBytes, 0, pSrcData, sourceBytes.Length);
            IntPtr pDefines = MarshalMacros(macros, allocations);

            int hr = SE_D3DPreprocess(
                pSrcData,
                (ulong)sourceBytes.Length,
                pSourceName,
                pDefines,
                include,
                out ppCode,
                out ppErrorMsgs
            );

            if (hr < 0)
            {
                errors = GetErrorText(ppErrorMsgs) ?? $"D3DPreprocess failed: HRESULT 0x{hr:X8}";
                return null;
            }

            // The blob is NUL-terminated text, decoded to the same string
            // SharpDX produces with Marshal.PtrToStringAnsi.
            return Marshal.PtrToStringAnsi(SE_BlobGetBufferPointer(ppCode));
        }
        catch (Exception ex)
        {
            errors = ex.Message;
            return null;
        }
        finally
        {
            ReleaseAll(ppCode, ppErrorMsgs, include, allocations, pSourceName, pSrcData);
        }
    }

    /// <summary>
    /// Compiles a shader file with vanilla's exact inputs: the raw file text,
    /// the real filepath as source name, vanilla include semantics, and
    /// vanilla flags (DEBUG for runtime requests, DEBUG | OPTIMIZATION_LEVEL3
    /// plus bytecode stripping for optimized tool requests). Throws with the
    /// compiler output as the message on a failed compilation, mirroring
    /// SharpDX's ThrowOnShaderCompileError behavior.
    /// </summary>
    internal static byte[] Compile(
        string sourceFilePath,
        ShaderMacro[] macros,
        string entryPoint,
        string profile,
        bool optimize,
        string includeDir,
        out string compileLog
    )
    {
        compileLog = null;
        string source = File.ReadAllText(sourceFilePath);

        _ = Initialized.Value;

        uint flags = D3DCOMPILE_DEBUG;
        if (optimize)
            flags |= D3DCOMPILE_OPTIMIZATION_LEVEL3;

        byte[] sourceBytes = Encoding.UTF8.GetBytes(source);
        IntPtr include = IntPtr.Zero;
        IntPtr pSourceName = IntPtr.Zero;
        IntPtr pEntryPoint = IntPtr.Zero;
        IntPtr pTarget = IntPtr.Zero;
        IntPtr pSrcData = IntPtr.Zero;
        IntPtr ppCode = IntPtr.Zero;
        IntPtr ppErrorMsgs = IntPtr.Zero;
        IntPtr ppStripped = IntPtr.Zero;
        var allocations = new List<IntPtr>();

        try
        {
            include = SE_CreateIncludeHandler(Path.GetDirectoryName(sourceFilePath), includeDir);

            pSourceName = Marshal.StringToHGlobalAnsi(sourceFilePath);
            pEntryPoint = Marshal.StringToHGlobalAnsi(entryPoint);
            pTarget = Marshal.StringToHGlobalAnsi(profile);
            pSrcData = Marshal.AllocHGlobal(sourceBytes.Length);
            Marshal.Copy(sourceBytes, 0, pSrcData, sourceBytes.Length);
            IntPtr pDefines = MarshalMacros(macros, allocations);

            int hr = SE_D3DCompile(
                pSrcData,
                (ulong)sourceBytes.Length,
                pSourceName,
                pDefines,
                include,
                pEntryPoint,
                pTarget,
                flags,
                0,
                out ppCode,
                out ppErrorMsgs
            );

            compileLog = GetErrorText(ppErrorMsgs);

            if (hr < 0)
                throw new Exception(compileLog ?? $"D3DCompile failed: HRESULT 0x{hr:X8}");

            IntPtr codePtr = SE_BlobGetBufferPointer(ppCode);
            int codeSize = (int)SE_BlobGetBufferSize(ppCode);

            if (optimize && codeSize != 0)
            {
                int hr2 = SE_D3DStripShader(codePtr, (ulong)codeSize, STRIP_FLAGS, out ppStripped);
                if (hr2 < 0)
                    throw new Exception($"D3DStripShader failed: HRESULT 0x{hr2:X8}");
                codePtr = SE_BlobGetBufferPointer(ppStripped);
                codeSize = (int)SE_BlobGetBufferSize(ppStripped);
            }

            byte[] result = new byte[codeSize];
            Marshal.Copy(codePtr, result, 0, codeSize);
            return result;
        }
        finally
        {
            if (ppStripped != IntPtr.Zero)
                SE_BlobRelease(ppStripped);
            ReleaseAll(ppCode, ppErrorMsgs, include, allocations, pSourceName, pSrcData);
            if (pEntryPoint != IntPtr.Zero)
                Marshal.FreeHGlobal(pEntryPoint);
            if (pTarget != IntPtr.Zero)
                Marshal.FreeHGlobal(pTarget);
        }
    }

    private static IntPtr MarshalMacros(ShaderMacro[] macros, List<IntPtr> allocations)
    {
        int macroCount = 0;
        for (int i = 0; i < macros.Length; i++)
        {
            if (!string.IsNullOrEmpty(macros[i].Name))
                macroCount++;
        }

        int structSize = Marshal.SizeOf<D3D_SHADER_MACRO>();
        IntPtr pDefines = Marshal.AllocHGlobal(structSize * (macroCount + 1));
        allocations.Add(pDefines);

        int idx = 0;
        for (int i = 0; i < macros.Length; i++)
        {
            if (string.IsNullOrEmpty(macros[i].Name))
                continue;

            IntPtr namePtr = Marshal.StringToHGlobalAnsi(macros[i].Name);
            allocations.Add(namePtr);

            IntPtr defPtr = IntPtr.Zero;
            if (!string.IsNullOrEmpty(macros[i].Definition))
            {
                defPtr = Marshal.StringToHGlobalAnsi(macros[i].Definition);
                allocations.Add(defPtr);
            }

            var macro = new D3D_SHADER_MACRO { Name = namePtr, Definition = defPtr };
            Marshal.StructureToPtr(macro, pDefines + structSize * idx, false);
            idx++;
        }

        var terminator = new D3D_SHADER_MACRO { Name = IntPtr.Zero, Definition = IntPtr.Zero };
        Marshal.StructureToPtr(terminator, pDefines + structSize * idx, false);
        return pDefines;
    }

    private static string GetErrorText(IntPtr errorBlob)
    {
        if (errorBlob == IntPtr.Zero)
            return null;
        IntPtr msgPtr = SE_BlobGetBufferPointer(errorBlob);
        ulong msgSize = SE_BlobGetBufferSize(errorBlob);
        if (msgPtr == IntPtr.Zero || msgSize == 0)
            return null;
        return Marshal.PtrToStringAnsi(msgPtr, (int)msgSize).TrimEnd('\0');
    }

    private static void ReleaseAll(
        IntPtr ppCode,
        IntPtr ppErrorMsgs,
        IntPtr include,
        List<IntPtr> allocations,
        IntPtr pSourceName,
        IntPtr pSrcData
    )
    {
        if (ppCode != IntPtr.Zero)
            SE_BlobRelease(ppCode);
        if (ppErrorMsgs != IntPtr.Zero)
            SE_BlobRelease(ppErrorMsgs);
        if (include != IntPtr.Zero)
            SE_DestroyIncludeHandler(include);
        if (pSourceName != IntPtr.Zero)
            Marshal.FreeHGlobal(pSourceName);
        if (pSrcData != IntPtr.Zero)
            Marshal.FreeHGlobal(pSrcData);
        foreach (IntPtr ptr in allocations)
            Marshal.FreeHGlobal(ptr);
    }
}

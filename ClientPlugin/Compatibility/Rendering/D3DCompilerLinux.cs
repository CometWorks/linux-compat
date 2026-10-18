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
    private static extern IntPtr SE_CreateForwardingIncludeHandler(IntPtr linuxInclude);

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
        IntPtr include = IntPtr.Zero;
        var allocations = new List<IntPtr>();
        try
        {
            IntPtr source = ReadSource(sourceFilePath, allocations, out int size);

            _ = Initialized.Value;
            include = SE_CreateIncludeHandler(Path.GetDirectoryName(sourceFilePath), includeDir);

            // SharpDX PreprocessFromFile passes an empty source name; the
            // shipped cache entries all start with '#line 1 ""'.
            int hr = Preprocess(source, size, "", macros, include, out string text, out errors);
            if (hr < 0)
            {
                errors ??= $"D3DPreprocess failed: HRESULT 0x{hr:X8}";
                return null;
            }
            return text;
        }
        catch (Exception ex)
        {
            errors = ex.Message;
            return null;
        }
        finally
        {
            if (include != IntPtr.Zero)
                SE_DestroyIncludeHandler(include);
            FreeAll(allocations);
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
        uint flags = D3DCOMPILE_DEBUG;
        if (optimize)
            flags |= D3DCOMPILE_OPTIMIZATION_LEVEL3;

        IntPtr include = IntPtr.Zero;
        var allocations = new List<IntPtr>();
        try
        {
            IntPtr source = ReadSource(sourceFilePath, allocations, out int size);

            _ = Initialized.Value;
            include = SE_CreateIncludeHandler(Path.GetDirectoryName(sourceFilePath), includeDir);

            int hr = Compile(
                source,
                size,
                sourceFilePath,
                macros,
                include,
                entryPoint,
                profile,
                flags,
                0,
                optimize,
                out byte[] code,
                out compileLog
            );
            if (hr < 0)
                throw new Exception(compileLog ?? $"D3DCompile failed: HRESULT 0x{hr:X8}");
            return code;
        }
        finally
        {
            if (include != IntPtr.Zero)
                SE_DestroyIncludeHandler(include);
            FreeAll(allocations);
        }
    }

    /// <summary>
    /// D3DPreprocess with the inputs of SharpDX's ShaderBytecode.Preprocess.
    /// linuxInclude is SharpDX's native Include shadow, or zero. Returns the
    /// HRESULT; text is null on failure, errors is the compiler output or null.
    /// </summary>
    internal static int PreprocessForSharpDX(
        IntPtr source,
        int size,
        string sourceName,
        ShaderMacro[] macros,
        IntPtr linuxInclude,
        out string text,
        out string errors
    )
    {
        _ = Initialized.Value;
        IntPtr include = ForwardInclude(linuxInclude);
        try
        {
            return Preprocess(source, size, sourceName, macros, include, out text, out errors);
        }
        finally
        {
            if (include != IntPtr.Zero)
                SE_DestroyIncludeHandler(include);
        }
    }

    /// <summary>
    /// D3DCompile with the inputs of SharpDX's ShaderBytecode.Compile, flags
    /// passed as given. linuxInclude is SharpDX's native Include shadow, or
    /// zero. Returns the HRESULT; code is null on failure, errors is the
    /// compiler output or null.
    /// </summary>
    internal static int CompileForSharpDX(
        IntPtr source,
        int size,
        string sourceName,
        ShaderMacro[] macros,
        IntPtr linuxInclude,
        string entryPoint,
        string profile,
        uint flags1,
        uint flags2,
        out byte[] code,
        out string errors
    )
    {
        _ = Initialized.Value;
        IntPtr include = ForwardInclude(linuxInclude);
        try
        {
            return Compile(
                source,
                size,
                sourceName,
                macros,
                include,
                entryPoint,
                profile,
                flags1,
                flags2,
                false,
                out code,
                out errors
            );
        }
        finally
        {
            if (include != IntPtr.Zero)
                SE_DestroyIncludeHandler(include);
        }
    }

    // SharpDX builds its Include shadow with the Linux calling convention;
    // the PE-loaded compiler can only call it through the wrapper's forwarder.
    private static IntPtr ForwardInclude(IntPtr linuxInclude) =>
        linuxInclude == IntPtr.Zero ? IntPtr.Zero : SE_CreateForwardingIncludeHandler(linuxInclude);

    private static int Preprocess(
        IntPtr source,
        int size,
        string sourceName,
        ShaderMacro[] macros,
        IntPtr include,
        out string text,
        out string errors
    )
    {
        text = null;
        IntPtr ppCode = IntPtr.Zero;
        IntPtr ppErrorMsgs = IntPtr.Zero;
        var allocations = new List<IntPtr>();
        try
        {
            int hr = SE_D3DPreprocess(
                source,
                (ulong)size,
                Ansi(sourceName, allocations),
                MarshalMacros(macros, allocations),
                include,
                out ppCode,
                out ppErrorMsgs
            );
            errors = GetErrorText(ppErrorMsgs);

            // The blob is NUL-terminated text, decoded to the same string
            // SharpDX produces with Marshal.PtrToStringAnsi.
            if (hr >= 0)
                text = Marshal.PtrToStringAnsi(SE_BlobGetBufferPointer(ppCode));
            return hr;
        }
        finally
        {
            Release(ppCode);
            Release(ppErrorMsgs);
            FreeAll(allocations);
        }
    }

    private static int Compile(
        IntPtr source,
        int size,
        string sourceName,
        ShaderMacro[] macros,
        IntPtr include,
        string entryPoint,
        string profile,
        uint flags1,
        uint flags2,
        bool strip,
        out byte[] code,
        out string errors
    )
    {
        code = null;
        IntPtr ppCode = IntPtr.Zero;
        IntPtr ppErrorMsgs = IntPtr.Zero;
        IntPtr ppStripped = IntPtr.Zero;
        var allocations = new List<IntPtr>();
        try
        {
            int hr = SE_D3DCompile(
                source,
                (ulong)size,
                Ansi(sourceName, allocations),
                MarshalMacros(macros, allocations),
                include,
                Ansi(entryPoint, allocations),
                Ansi(profile, allocations),
                flags1,
                flags2,
                out ppCode,
                out ppErrorMsgs
            );
            errors = GetErrorText(ppErrorMsgs);
            if (hr < 0)
                return hr;

            IntPtr codePtr = SE_BlobGetBufferPointer(ppCode);
            int codeSize = (int)SE_BlobGetBufferSize(ppCode);

            if (strip && codeSize != 0)
            {
                int hr2 = SE_D3DStripShader(codePtr, (ulong)codeSize, STRIP_FLAGS, out ppStripped);
                if (hr2 < 0)
                    throw new Exception($"D3DStripShader failed: HRESULT 0x{hr2:X8}");
                codePtr = SE_BlobGetBufferPointer(ppStripped);
                codeSize = (int)SE_BlobGetBufferSize(ppStripped);
            }

            code = new byte[codeSize];
            Marshal.Copy(codePtr, code, 0, codeSize);
            return hr;
        }
        finally
        {
            Release(ppStripped);
            Release(ppCode);
            Release(ppErrorMsgs);
            FreeAll(allocations);
        }
    }

    private static IntPtr ReadSource(string path, List<IntPtr> allocations, out int size)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(File.ReadAllText(path));
        size = bytes.Length;
        IntPtr source = Marshal.AllocHGlobal(Math.Max(size, 1));
        allocations.Add(source);
        Marshal.Copy(bytes, 0, source, size);
        return source;
    }

    private static IntPtr Ansi(string value, List<IntPtr> allocations)
    {
        if (value == null)
            return IntPtr.Zero;
        IntPtr ptr = Marshal.StringToHGlobalAnsi(value);
        allocations.Add(ptr);
        return ptr;
    }

    private static IntPtr MarshalMacros(ShaderMacro[] macros, List<IntPtr> allocations)
    {
        macros ??= Array.Empty<ShaderMacro>();
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

    private static void Release(IntPtr blob)
    {
        if (blob != IntPtr.Zero)
            SE_BlobRelease(blob);
    }

    private static void FreeAll(List<IntPtr> allocations)
    {
        foreach (IntPtr ptr in allocations)
            Marshal.FreeHGlobal(ptr);
    }
}

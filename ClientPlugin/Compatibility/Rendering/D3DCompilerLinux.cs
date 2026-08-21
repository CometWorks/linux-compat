using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using SharpDX.Direct3D;

namespace ClientPlugin.Compatibility.Rendering;

public static class D3DCompilerLinux
{
    private const uint D3DCOMPILE_DEBUG = 0x01;
    private const uint D3DCOMPILE_SKIP_OPTIMIZATION = 0x04;
    private const uint D3DCOMPILE_OPTIMIZATION_LEVEL3 = 0x8000;

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
    private static extern IntPtr SE_BlobGetBufferPointer(IntPtr blob);

    [DllImport("libD3DCompiler.so")]
    private static extern ulong SE_BlobGetBufferSize(IntPtr blob);

    [DllImport("libD3DCompiler.so")]
    private static extern uint SE_BlobRelease(IntPtr blob);

    private static string FindFileCaseInsensitive(string baseDir, string relativePath)
    {
        string[] segments = relativePath.Replace('\\', '/').Split('/');
        string current = baseDir;
        foreach (string segment in segments)
        {
            if (!Directory.Exists(current))
                return null;
            string exact = Path.Combine(current, segment);
            if (File.Exists(exact) || Directory.Exists(exact))
            {
                current = exact;
                continue;
            }
            string found = null;
            foreach (string entry in Directory.EnumerateFileSystemEntries(current))
            {
                if (
                    string.Equals(
                        Path.GetFileName(entry),
                        segment,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    found = entry;
                    break;
                }
            }
            if (found == null)
                return null;
            current = found;
        }
        return File.Exists(current) ? current : null;
    }

    private static readonly Regex IncludeRegex = new Regex(
        @"^\s*#include\s+[<""]([^>""]+)[>""]",
        RegexOptions.Multiline | RegexOptions.Compiled
    );

    private static string PreprocessIncludes(
        string sourceFilePath,
        IReadOnlyList<string> includeDirs,
        HashSet<string> stack = null
    )
    {
        stack ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string fullPath = Path.GetFullPath(sourceFilePath);
        if (!stack.Add(fullPath))
            return string.Empty;

        string source = File.ReadAllText(sourceFilePath);
        string sourceDir = Path.GetDirectoryName(fullPath);

        string result = IncludeRegex.Replace(
            source,
            match =>
            {
                string includeFile = match.Groups[1].Value;

                string[] searchDirs = new string[includeDirs.Count + 1];
                searchDirs[0] = sourceDir;
                for (int i = 0; i < includeDirs.Count; i++)
                    searchDirs[i + 1] = includeDirs[i];

                foreach (string dir in searchDirs)
                {
                    string resolved = Path.Combine(dir, includeFile);
                    if (File.Exists(resolved))
                        return PreprocessIncludes(resolved, includeDirs, stack);

                    resolved = FindFileCaseInsensitive(dir, includeFile);
                    if (resolved != null)
                        return PreprocessIncludes(resolved, includeDirs, stack);
                }

                return match.Value;
            }
        );

        stack.Remove(fullPath);
        return result;
    }

    internal static byte[] Compile(
        string sourceFilePath,
        ShaderMacro[] macros,
        string entryPoint,
        string profile,
        bool optimize,
        out string compileLog
    )
    {
        uint flags = 0;
        if (optimize)
            flags |= D3DCOMPILE_OPTIMIZATION_LEVEL3;
        else
            flags |= D3DCOMPILE_DEBUG | D3DCOMPILE_SKIP_OPTIMIZATION;

        string shaderRoot = Path.GetDirectoryName(sourceFilePath);
        string probeDir = shaderRoot;
        while (probeDir != null && !File.Exists(Path.Combine(probeDir, "Common.hlsli")))
            probeDir = Path.GetDirectoryName(probeDir);
        string[] includeDirs = probeDir != null ? new[] { probeDir } : new[] { shaderRoot };

        string preprocessed = PreprocessIncludes(sourceFilePath, includeDirs);
        byte[] sourceBytes = Encoding.UTF8.GetBytes(preprocessed);

        IntPtr pSourceName = IntPtr.Zero;
        IntPtr pEntryPoint = IntPtr.Zero;
        IntPtr pTarget = IntPtr.Zero;
        IntPtr pSrcData = IntPtr.Zero;
        IntPtr pDefines = IntPtr.Zero;
        IntPtr ppCode = IntPtr.Zero;
        IntPtr ppErrorMsgs = IntPtr.Zero;
        var pinnedStrings = new List<IntPtr>();

        try
        {
            pSourceName = Marshal.StringToHGlobalAnsi(sourceFilePath);
            pEntryPoint = Marshal.StringToHGlobalAnsi(entryPoint);
            pTarget = Marshal.StringToHGlobalAnsi(profile);

            pSrcData = Marshal.AllocHGlobal(sourceBytes.Length);
            Marshal.Copy(sourceBytes, 0, pSrcData, sourceBytes.Length);

            int macroCount = 0;
            for (int i = 0; i < macros.Length; i++)
            {
                if (!string.IsNullOrEmpty(macros[i].Name))
                    macroCount++;
            }

            int structSize = Marshal.SizeOf<D3D_SHADER_MACRO>();
            pDefines = Marshal.AllocHGlobal(structSize * (macroCount + 1));

            int idx = 0;
            for (int i = 0; i < macros.Length; i++)
            {
                if (string.IsNullOrEmpty(macros[i].Name))
                    continue;

                IntPtr namePtr = Marshal.StringToHGlobalAnsi(macros[i].Name);
                pinnedStrings.Add(namePtr);

                IntPtr defPtr = IntPtr.Zero;
                if (!string.IsNullOrEmpty(macros[i].Definition))
                {
                    defPtr = Marshal.StringToHGlobalAnsi(macros[i].Definition);
                    pinnedStrings.Add(defPtr);
                }

                var macro = new D3D_SHADER_MACRO { Name = namePtr, Definition = defPtr };
                Marshal.StructureToPtr(macro, pDefines + structSize * idx, false);
                idx++;
            }

            var terminator = new D3D_SHADER_MACRO { Name = IntPtr.Zero, Definition = IntPtr.Zero };
            Marshal.StructureToPtr(terminator, pDefines + structSize * idx, false);

            _ = Initialized.Value;
            int hr = SE_D3DCompile(
                pSrcData,
                (ulong)sourceBytes.Length,
                pSourceName,
                pDefines,
                IntPtr.Zero,
                pEntryPoint,
                pTarget,
                flags,
                0,
                out ppCode,
                out ppErrorMsgs
            );

            compileLog = null;
            if (ppErrorMsgs != IntPtr.Zero)
            {
                IntPtr msgPtr = SE_BlobGetBufferPointer(ppErrorMsgs);
                ulong msgSize = SE_BlobGetBufferSize(ppErrorMsgs);
                if (msgPtr != IntPtr.Zero && msgSize > 0)
                    compileLog = Marshal.PtrToStringAnsi(msgPtr, (int)msgSize).TrimEnd('\0');
            }

            if (hr < 0)
                return null;

            IntPtr codePtr = SE_BlobGetBufferPointer(ppCode);
            int codeSize = (int)SE_BlobGetBufferSize(ppCode);
            byte[] result = new byte[codeSize];
            Marshal.Copy(codePtr, result, 0, codeSize);
            return result;
        }
        finally
        {
            if (ppCode != IntPtr.Zero)
                SE_BlobRelease(ppCode);
            if (ppErrorMsgs != IntPtr.Zero)
                SE_BlobRelease(ppErrorMsgs);
            if (pSourceName != IntPtr.Zero)
                Marshal.FreeHGlobal(pSourceName);
            if (pEntryPoint != IntPtr.Zero)
                Marshal.FreeHGlobal(pEntryPoint);
            if (pTarget != IntPtr.Zero)
                Marshal.FreeHGlobal(pTarget);
            if (pSrcData != IntPtr.Zero)
                Marshal.FreeHGlobal(pSrcData);
            if (pDefines != IntPtr.Zero)
                Marshal.FreeHGlobal(pDefines);
            foreach (IntPtr ptr in pinnedStrings)
                Marshal.FreeHGlobal(ptr);
        }
    }
}

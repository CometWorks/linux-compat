using System;
using System.Linq;
using ServerPlugin.Tools;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ServerPlugin.Patches.PathHandling;

// Cecil prepatch that injects path normalization into the body of
// VRage.FileSystem.MyFileSystem.Open(string, FileMode, FileAccess, FileShare)
// at preloader time, replacing the former Harmony Prefix
// (MyFileSystemOpenPatch, removed in this commit).
//
// Why move it from a Harmony Prefix to a Cecil IL injection:
//
//   MyFileSystem.OpenRead is a one-liner and MyFileSystem.Open is a small
//   static method with constant arguments at every call site. The JIT
//   inlines both into hot callers (e.g. MyTextureAtlas.ParseAtlasDescription
//   when it itself becomes ParseAtlasDescription_Patch1 via the transpiler
//   in MyTextureAtlasPatch.cs). Inlining substitutes the IL of the target
//   methods directly into the caller, bypassing the Harmony prologue stub
//   that a Prefix relies on. The inlined call goes straight to
//   m_fileProvider.Open(rawPath, ...) with whatever mixed-separator path
//   the caller composed, and on Linux MyClassicFileProvider.Open returns
//   null because File.Exists(rawPath) is false. ParseAtlasDescription's
//   own broad catch swallows the resulting ArgumentNullException as a
//   warning, leaving the atlas empty -- and the next FindElement(material
//   .Texture) call from MyBillboardRenderer.GatherInternal then throws
//   KeyNotFoundException on the render thread (the observed flare-gun
//   crash; see the regression detector in MyTextureAtlasPatch.cs).
//
// Putting the normalization inside Open's IL body means inlined copies of
// Open still carry it. The injected call goes through PathCache
// .ResolveAbsolute, which already covers both required steps
// (PathHelpers.Normalize -> if-rooted -> case-insensitive resolve) -- see
// PathHelpers.cs line 296. PathCache uses raw File.Exists / Directory
// .Exists, not MyFileSystem.FileExists, so there is no recursion through
// MyFileSystem.Open.
//
// Cross-assembly call into LinuxCompat requires a new AssemblyRef on the
// VRage.Library module; the AssemblyResolve handler installed in
// Preloader.cs (lines 42-47) answers that bind at runtime, including the
// Pulsar dev-folder randomized assembly identity. Same machinery already
// in use for MyModContextPrepatch.
public static class MyFileSystemOpenPrepatch
{
    public static void Prepatch(AssemblyDefinition asmDef)
    {
        if (asmDef.Name.Name != "VRage.Library")
            return;

        var module = asmDef.MainModule;

        var type = module.GetType("VRage.FileSystem.MyFileSystem");
        if (type == null)
        {
            Console.WriteLine("[LinuxCompat] MyFileSystemOpenPrepatch: VRage.FileSystem.MyFileSystem not found in VRage.Library (already patched or upstream renamed?)");
            return;
        }

        var openMethod = type.Methods.FirstOrDefault(m =>
            m.Name == "Open" &&
            m.IsStatic &&
            m.Parameters.Count == 4 &&
            m.Parameters[0].ParameterType.FullName == "System.String" &&
            m.Parameters[1].ParameterType.FullName == "System.IO.FileMode" &&
            m.Parameters[2].ParameterType.FullName == "System.IO.FileAccess" &&
            m.Parameters[3].ParameterType.FullName == "System.IO.FileShare");
        if (openMethod?.Body == null)
        {
            Console.WriteLine("[LinuxCompat] MyFileSystemOpenPrepatch: MyFileSystem.Open(string, FileMode, FileAccess, FileShare) not found");
            return;
        }

        // Idempotency: if a previous run injected the call already (e.g.
        // double preloader pass), don't add it a second time. We detect by
        // looking for the call instruction's operand name on PathCache.
        if (openMethod.Body.Instructions.Any(IsResolveAbsoluteCall))
        {
            Console.WriteLine("[LinuxCompat] MyFileSystemOpenPrepatch: Open already carries the PathCache.ResolveAbsolute prologue (skipping)");
            return;
        }

        openMethod.Body.Instructions.RecordOriginalCode(openMethod);

        var resolveAbsolute = ImportResolveAbsolute(module);

        var il = openMethod.Body.GetILProcessor();
        var first = openMethod.Body.Instructions[0];

        // Prepend:
        //   path = PathCache.ResolveAbsolute(path);
        // which in IL is:
        //   ldarg.0
        //   call    string [LinuxCompat]ServerPlugin.Patches.PathHandling.PathCache::ResolveAbsolute(string)
        //   starg.s path
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Call, resolveAbsolute));
        il.InsertBefore(first, il.Create(OpCodes.Starg_S, openMethod.Parameters[0]));

        openMethod.Body.Instructions.RecordPatchedCode(openMethod);

        Console.WriteLine("[LinuxCompat] MyFileSystemOpenPrepatch: injected PathCache.ResolveAbsolute prologue into MyFileSystem.Open");
    }

    private static bool IsResolveAbsoluteCall(Instruction instr)
    {
        if (instr.OpCode != OpCodes.Call) return false;
        if (instr.Operand is not MethodReference mr) return false;
        return mr.Name == "ResolveAbsolute" &&
               mr.DeclaringType != null &&
               mr.DeclaringType.FullName == "ServerPlugin.Patches.PathHandling.PathCache";
    }

    // Build a MethodReference to LinuxCompat's PathCache.ResolveAbsolute
    // (static, one string parameter, string return). Adds an AssemblyRef
    // to LinuxCompat on the VRage.Library module if one isn't already
    // present. Mirrors the bind pattern used by MyModContextPrepatch.
    private static MethodReference ImportResolveAbsolute(ModuleDefinition module)
    {
        var linuxCompatRef = module.AssemblyReferences
            .FirstOrDefault(r => r.Name == "LinuxCompat");
        if (linuxCompatRef == null)
        {
            linuxCompatRef = new AssemblyNameReference("LinuxCompat", new Version(1, 0, 0, 0))
            {
                PublicKeyToken = Array.Empty<byte>(),
                PublicKey = Array.Empty<byte>(),
                Culture = string.Empty,
                HashAlgorithm = AssemblyHashAlgorithm.None,
            };
            module.AssemblyReferences.Add(linuxCompatRef);
        }

        var pathCacheType = new TypeReference(
            "ServerPlugin.Patches.PathHandling", "PathCache", module, linuxCompatRef, false);

        var stringRef = module.TypeSystem.String;
        var method = new MethodReference("ResolveAbsolute", stringRef, pathCacheType)
        {
            HasThis = false,
            ExplicitThis = false,
            CallingConvention = MethodCallingConvention.Default,
        };
        method.Parameters.Add(new ParameterDefinition(stringRef));
        return method;
    }
}

// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using ServerPlugin.Compatibility;
using ServerPlugin.Patches.PathHandling;
using ServerPlugin.Rewriter;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;

// IMPORTANT: MUST NOT USE A NAMESPACE, otherwise Pulsar won't find the Preloader class!
//namespace ServerPlugin;

// ReSharper disable once UnusedType.Global
public static class Preloader
{
    static Preloader()
    {
        // Register a resolver for our own assembly BEFORE any Finish() runs.
        // The PathSubstitutionRewriter plumbs a metadata reference built from
        // this in-memory image into MyScriptCompiler. Compiled mod assemblies
        // therefore carry TypeRefs into Pulsar's randomized in-memory identity
        // (e.g. "LinuxCompatServer_fhop0d1n.jo0"); at JIT time the runtime
        // asks for that exact name and fails because Assembly.Load(byte[])-
        // loaded assemblies are not findable by simple-name through default-
        // ALC binding.
        var selfAssembly = typeof(Preloader).Assembly;
        var selfName = selfAssembly.GetName().Name;
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var name = new AssemblyName(args.Name).Name;
            if (name == null) return null;
            return name == "LinuxCompat" || name == "LinuxCompatServer" || name == selfName ? selfAssembly : null;
        };
    }

    // ReSharper disable once UnusedMember.Global
    public static IEnumerable<string> TargetDLLs { get; } =
    [
        // Game DLLs we still Cecil-patch on the dedicated server. The render,
        // audio, GUI and DirectX-bridge assemblies that the client patches
        // are linked into the server install too but never invoked, so they
        // are not pre-patched here.
        "Sandbox.Game.dll",
        "SpaceEngineers.Game.dll",
        "VRage.dll",
        "VRage.Dedicated.dll",
        "VRage.Game.dll",
        "VRage.Library.dll",
        "VRage.Platform.Windows.dll",
        "VRage.Scripting.dll",
        "VRage.Steam.dll",
    ];

    // ReSharper disable once UnusedMember.Global
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void Patch(AssemblyDefinition asmDef)
    {
        var asmName = asmDef.Name.Name;
        Console.WriteLine($"[LinuxCompatServer] Preloader.Patch: {asmName}");
        switch (asmName)
        {
            case "VRage.Platform.Windows":
                PatchVRagePlatformWindows(asmDef);
                break;
            case "VRage.Steam":
                PatchVRageSteam(asmDef);
                break;
            case "VRage.Game":
                ServerPlugin.Patches.PathHandling.MyModContextPrepatch.Prepatch(asmDef);
                break;
            case "VRage.Library":
                ServerPlugin.Patches.PathHandling.MyFileSystemOpenPrepatch.Prepatch(asmDef);
                break;
            case "SpaceEngineers.Game":
                PatchSpaceEngineersGame(asmDef);
                break;
            case "VRage.Dedicated":
                ServerPlugin.Patches.PlatformGuards.AttachConsolePrepatch.Prepatch(asmDef);
                ServerPlugin.Patches.PlatformGuards.IsVcRedist2019InstalledPrepatch.Prepatch(asmDef);
                break;
        }
    }

    // SpaceEngineers.Game.VoiceChat.OpusDevice.Native carries 10 entries of the
    // form [DllImport("Opus.dll")] for opus_encoder_create / _destroy /
    // opus_encode{,_float} / opus_decoder_create / _destroy / opus_decode /
    // opus_encoder_ctl (×2) / opus_packet_get_nb_samples. On Linux the .NET
    // runtime's name munging never collapses the literal ".dll" away, so the
    // probe order is { Opus.dll, Opus.dll.so, libOpus.dll, libOpus.dll.so } —
    // none of which exists. The actual on-disk library is libopus.so.0 (the
    // SONAME ldconfig publishes for libopus0).
    //
    // The dedicated server installs MyNullMicrophone and does not encode mic
    // input, but the OpusDevice type is still loaded as part of voice-chat
    // packet handling and the rename is a one-line string mutation, so we
    // apply it here regardless to keep behaviour parity with the client and
    // guard against any code path that does construct an OpusDevice.
    private static void PatchSpaceEngineersGame(AssemblyDefinition asmDef)
    {
        var module = asmDef.MainModule;
        var renamed = 0;
        foreach (var modRef in module.ModuleReferences)
        {
            if (string.Equals(modRef.Name, "Opus.dll", StringComparison.OrdinalIgnoreCase))
            {
                modRef.Name = "libopus.so.0";
                renamed++;
            }
        }
        if (renamed > 0)
            Console.WriteLine($"[LinuxCompatServer] Preloader: rewrote {renamed} ModuleReference(s) Opus.dll -> libopus.so.0 in SpaceEngineers.Game");
    }

    // The dedicated server still loads VRage.Platform.Windows and calls into
    // MyVRageWindows.Init -> MyVRagePlatform(...).Init() (see
    // SpaceEngineersDedicated.MyProgram). That path touches several Windows-
    // only PerformanceCounter / psapi / kernel32 P/Invokes which trip Harmony
    // IL parsing or throw at startup on Linux, so they are pre-rewritten here.
    //
    // Render/SwapChain/MyWindowsRender patches are intentionally NOT applied
    // on the server: DedicatedServer.RunInternal installs MyNullRender via
    // MyRenderProxy.Initialize and never reaches MyPlatformRender.CreateRender
    // Device or MyWindowsRender, so leaving those bodies untouched is safe.
    private static void PatchVRagePlatformWindows(AssemblyDefinition asmDef)
    {
        var myWindowsSystem = asmDef.MainModule.GetType("VRage.Platform.Windows.Sys.MyWindowsSystem");
        if (myWindowsSystem == null) return;

        NopMethodBody(myWindowsSystem, "Init");
        ReplaceWithConstant(myWindowsSystem, "get_CPUCounter", 0f);
        ReplaceWithConstant(myWindowsSystem, "get_RAMCounter", 0f);
        ReplaceProcessPrivateMemory(myWindowsSystem);

        var myCrashReporting = asmDef.MainModule.GetType("VRage.Platform.Windows.MyCrashReporting");
        if (myCrashReporting != null)
        {
            NopMethodBody(myCrashReporting, "WriteMiniDump");
        }

        var myVRagePlatform = asmDef.MainModule.GetType("VRage.Platform.Windows.MyVRagePlatform");
        if (myVRagePlatform != null)
        {
            ReplaceWithUintReturn(myVRagePlatform, "TimeBeginPeriod", 0);
            ReplaceWithUintReturn(myVRagePlatform, "TimeEndPeriod", 0);
            NopMethodBody(myVRagePlatform, "Init");
            NopMethodBody(myVRagePlatform, "Done");
            ReplaceWithBoolReturn(myVRagePlatform, "CreateInput2", false);
        }

        var myWindowsWindows = asmDef.MainModule.GetType("VRage.Platform.Windows.Forms.MyWindowsWindows");
        if (myWindowsWindows != null)
        {
            ReplaceWithDefaultReturn(myWindowsWindows, "MessageBox");
            NopMethodBody(myWindowsWindows, "CreateWindow");
            NopMethodBody(myWindowsWindows, "ShowSplashScreen");
            NopMethodBody(myWindowsWindows, "HideSplashScreen");
            NopMethodBody(myWindowsWindows, "FindWindowInParent");
            NopMethodBody(myWindowsWindows, "PostMessage");
            NopMethodBody(myWindowsWindows, "CreateToolWindow");
        }
    }

    private static void PatchVRageSteam(AssemblyDefinition asmDef)
    {
        var module = asmDef.MainModule;
        var mySteamService = module.GetType("VRage.Steam.MySteamService");
        if (mySteamService == null) return;

        // Find the SteamUserId field
        var steamUserIdField = mySteamService.Fields.FirstOrDefault(f => f.Name == "SteamUserId");
        if (steamUserIdField == null) return;

        // Replace RequestCurrentStats() with RequestUserStats(SteamUserId) in all methods
        foreach (var method in mySteamService.Methods)
        {
            if (!method.HasBody) continue;

            var il = method.Body.GetILProcessor();
            var instructions = method.Body.Instructions;

            for (int i = 0; i < instructions.Count; i++)
            {
                var instr = instructions[i];
                if (instr.OpCode == OpCodes.Call && instr.Operand is MethodReference methodRef &&
                    methodRef.Name == "RequestCurrentStats" &&
                    methodRef.DeclaringType.Name == "SteamUserStats")
                {
                    // Find or create RequestUserStats method reference
                    var steamUserStatsType = methodRef.DeclaringType;
                    var csteamIdType = steamUserIdField.FieldType;
                    var steamApiCallType = module.ImportReference(new TypeReference(
                        "Steamworks", "SteamAPICall_t", module, methodRef.DeclaringType.Scope, true));
                    var requestUserStats = new MethodReference("RequestUserStats", steamApiCallType, steamUserStatsType);
                    requestUserStats.Parameters.Add(new ParameterDefinition(csteamIdType));

                    // Insert: ldarg.0 (this), ldfld SteamUserId before the call
                    var loadThis = il.Create(OpCodes.Ldarg_0);
                    var loadField = il.Create(OpCodes.Ldfld, steamUserIdField);
                    il.InsertBefore(instr, loadThis);
                    il.InsertBefore(instr, loadField);

                    // Replace the call
                    instr.Operand = requestUserStats;

                    Console.WriteLine($"[LinuxCompatServer] Replaced RequestCurrentStats with RequestUserStats in {method.Name}");
                    i += 2; // Skip the inserted instructions
                }
            }
        }

        // Fix GetAuthSessionTicket to add SteamNetworkingIdentity parameter
        var getAuthTicket = mySteamService.Methods.FirstOrDefault(m => m.Name == "GetAuthSessionTicket");
        if (getAuthTicket?.HasBody == true)
        {
            PatchGetAuthSessionTicket(getAuthTicket, module);
        }

        // Bridge to the newer Steamworks.NET wrapper (the v1.0.0.0 build that
        // Pulsar bundles against `libsteam_api.so`) by adding a default false
        // for the trailing bool argument that the newer overloads require.
        // Without this, JITting any of these MySteamUgcClient methods throws
        // MissingMethodException — the SetItemTags variant is what currently
        // hangs blueprint Workshop publishing in PublishItemBlocking.
        //
        // Removed in newer SDK / replaced with overloads that take a default
        // bool:
        //   SteamUGC.SetItemTags(handle, tags)            -> (..., bAllowAdminTags)
        //   SteamUGC.GetNumSubscribedItems()              -> (bIncludeLocallyDisabled)
        //   SteamUGC.GetSubscribedItems(ids, n)           -> (..., bIncludeLocallyDisabled)
        var mySteamUgcClient = module.GetType("VRage.Steam.Steamworks.MySteamUgcClient");
        if (mySteamUgcClient != null)
        {
            AppendDefaultFalseToSteamUgcCall(mySteamUgcClient, "SetItemTags",
                originalParamCount: 2, module);
            AppendDefaultFalseToSteamUgcCall(mySteamUgcClient, "GetNumSubscribedItems",
                originalParamCount: 0, module);
            AppendDefaultFalseToSteamUgcCall(mySteamUgcClient, "GetSubscribedItems",
                originalParamCount: 2, module);
        }
    }

    // Locate every `call SteamUGC.<methodName>(originalParamCount args)` in
    // the given type's bodies and rewrite it as a call to the same name with
    // an extra trailing System.Boolean argument set to false.
    private static void AppendDefaultFalseToSteamUgcCall(
        TypeDefinition containerType, string methodName, int originalParamCount, ModuleDefinition module)
    {
        foreach (var method in containerType.Methods)
        {
            if (!method.HasBody) continue;
            var il = method.Body.GetILProcessor();
            // Snapshot the instruction list because we mutate it.
            var instructions = method.Body.Instructions.ToList();
            foreach (var instr in instructions)
            {
                if (instr.OpCode != OpCodes.Call) continue;
                if (instr.Operand is not MethodReference mr) continue;
                if (mr.Name != methodName) continue;
                if (mr.DeclaringType.Name != "SteamUGC") continue;
                if (mr.Parameters.Count != originalParamCount) continue;

                // Build a reference to the new overload. Preserve the original
                // calling convention/this attributes — these SteamUGC entry
                // points are static, but copying the flags is the safe pattern.
                var newRef = new MethodReference(methodName, mr.ReturnType, mr.DeclaringType)
                {
                    HasThis = mr.HasThis,
                    ExplicitThis = mr.ExplicitThis,
                    CallingConvention = mr.CallingConvention,
                };
                foreach (var p in mr.Parameters)
                    newRef.Parameters.Add(new ParameterDefinition(p.ParameterType));
                newRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));

                il.InsertBefore(instr, il.Create(OpCodes.Ldc_I4_0));
                instr.Operand = newRef;
                Console.WriteLine($"[LinuxCompatServer] Rewrote SteamUGC.{methodName}({originalParamCount}) -> ({originalParamCount + 1}, false) in {method.Name}");
            }
        }
    }

    private static void PatchGetAuthSessionTicket(MethodDefinition method, ModuleDefinition module)
    {
        var il = method.Body.GetILProcessor();
        var instructions = method.Body.Instructions;

        for (int i = 0; i < instructions.Count; i++)
        {
            var instr = instructions[i];
            if (instr.OpCode == OpCodes.Call && instr.Operand is MethodReference methodRef &&
                methodRef.Name == "GetAuthSessionTicket" &&
                methodRef.DeclaringType.Name == "SteamUser")
            {
                // Create reference to the new overload with SteamNetworkingIdentity.
                // IsValueType=true is critical: without it, Cecil encodes the local-var,
                // initobj, and by-ref signatures with ELEMENT_TYPE_CLASS instead of
                // ELEMENT_TYPE_VALUETYPE, and the JIT throws TypeLoadException
                // ("value type mismatch") the first time GetAuthSessionTicket is called
                // (e.g. on multiplayer join via MyMultiplayerClient.SendPlayerData).
                var steamNetIdType = new TypeReference(
                    "Steamworks", "SteamNetworkingIdentity", module, methodRef.DeclaringType.Scope)
                {
                    IsValueType = true
                };
                steamNetIdType = module.ImportReference(steamNetIdType);

                var newMethodRef = new MethodReference("GetAuthSessionTicket", methodRef.ReturnType, methodRef.DeclaringType);
                newMethodRef.Parameters.Add(new ParameterDefinition(new ArrayType(module.TypeSystem.Byte)));
                newMethodRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                newMethodRef.Parameters.Add(new ParameterDefinition(new ByReferenceType(module.TypeSystem.UInt32)));
                newMethodRef.Parameters.Add(new ParameterDefinition(new ByReferenceType(steamNetIdType)));

                // Add a local for SteamNetworkingIdentity
                var identityVar = new VariableDefinition(steamNetIdType);
                method.Body.Variables.Add(identityVar);

                // Insert: ldloca identityVar, initobj SteamNetworkingIdentity, ldloca identityVar
                var ldloca1 = il.Create(OpCodes.Ldloca_S, identityVar);
                var initobj = il.Create(OpCodes.Initobj, steamNetIdType);
                var ldloca2 = il.Create(OpCodes.Ldloca_S, identityVar);

                il.InsertBefore(instr, ldloca1);
                il.InsertBefore(instr, initobj);
                il.InsertBefore(instr, ldloca2);

                // Replace the call
                instr.Operand = newMethodRef;

                Console.WriteLine($"[LinuxCompatServer] Patched GetAuthSessionTicket with SteamNetworkingIdentity");
                break;
            }
        }
    }

    private static void NopMethodBody(TypeDefinition type, string methodName)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == methodName);
        if (method == null) return;

        var il = method.Body.GetILProcessor();
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void ReplaceWithBoolReturn(TypeDefinition type, string methodName, bool value)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == methodName);
        if (method == null) return;

        var il = method.Body.GetILProcessor();
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        il.Append(il.Create(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void ReplaceWithConstant(TypeDefinition type, string methodName, float value)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == methodName);
        if (method == null) return;

        var il = method.Body.GetILProcessor();
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        il.Append(il.Create(OpCodes.Ldc_R4, value));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void ReplaceWithUintReturn(TypeDefinition type, string methodName, uint value)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == methodName);
        if (method == null) return;

        // For P/Invoke methods, we need to remove the PInvokeInfo and make them regular methods
        method.IsPInvokeImpl = false;
        method.IsPreserveSig = false;
        method.PInvokeInfo = null;
        method.ImplAttributes = Mono.Cecil.MethodImplAttributes.IL | Mono.Cecil.MethodImplAttributes.Managed;
        method.Body = new Mono.Cecil.Cil.MethodBody(method);

        var il = method.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldc_I4, (int)value));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void ReplaceWithDefaultReturn(TypeDefinition type, string methodName)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == methodName && !m.IsPInvokeImpl && m.HasBody);
        if (method == null) return;

        var il = method.Body.GetILProcessor();
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        if (method.ReturnType.FullName != "System.Void")
        {
            il.Append(il.Create(OpCodes.Ldc_I4_0));
        }
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void ReplaceProcessPrivateMemory(TypeDefinition type)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == "get_ProcessPrivateMemory");
        if (method == null) return;

        var module = type.Module;
        var getCurrentProcess = module.ImportReference(typeof(System.Diagnostics.Process).GetMethod("GetCurrentProcess"));
        var privateMemSize = module.ImportReference(typeof(System.Diagnostics.Process).GetProperty("PrivateMemorySize64")!.GetGetMethod()!);

        var il = method.Body.GetILProcessor();
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        il.Append(il.Create(OpCodes.Call, getCurrentProcess));
        il.Append(il.Create(OpCodes.Callvirt, privateMemSize));
        il.Append(il.Create(OpCodes.Ret));
    }

    // ReSharper disable once UnusedMember.Global
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void Finish()
    {
        // The Directory.Enumerate* JIT pre-warm that prevents the .NET 10 +
        // MonoMod V60 SIGSEGV is owned by the se-dotnet-compat plugin
        // (Preloader.PrewarmDirectoryEnumerationStubs). The bug is generic to
        // .NET 10 + Harmony and not Linux-specific; the Linux-side PathCache
        // path is just one trigger. See se-dotnet-compat/Docs/Fixes.md.

        // See https://learn.microsoft.com/en-us/dotnet/standard/serialization/binaryformatter-security-guide
        AppContext.SetSwitch("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", true);

        // Fixes runtime loading the Keen version in some cases by initializing it explicitly
        Assembly.Load("System.Collections.Immutable");

        // Native library preloading and the Windows-DLL-name -> Linux-.so
        // alias table (EOS, Steamworks, the PE-loader wrapper libs) live in
        // Pulsar's NativeLibraryPreloader, which runs at the top of
        // Pulsar.Legacy.Program.Main before any plugin loads. All that
        // remains here is handing the wrapper libraries the absolute paths
        // to the Windows DLLs they need to PE-load.
        InitNativeWrappers();

        // Override game DLLs with the versions added as NuGet dependency by this plugin
        string[] dlls = [
            "System.Management",
        ];
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var targetName = new AssemblyName(args.Name).Name;
            return dlls.Contains(targetName) ? Assembly.Load(targetName) : null;
        };

#if DEBUG && HARMONY_DEBUG
        Harmony.DEBUG = true;
#endif

        var harmony = new Harmony("LinuxCompatServer");
        try
        {
            harmony.PatchCategory("Finish");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[LinuxCompatServer] PatchCategory(\"Finish\") threw: {e}");
            try { VRage.Utils.MyLog.Default.WriteLineAndConsole($"[LinuxCompatServer] PatchCategory(\"Finish\") threw: {e}"); } catch { }
            throw;
        }
        Console.WriteLine($"[LinuxCompatServer] PatchCategory(\"Finish\") applied {harmony.GetPatchedMethods().Count()} methods");
        try { VRage.Utils.MyLog.Default.WriteLineAndConsole($"[LinuxCompatServer] PatchCategory(\"Finish\") applied {harmony.GetPatchedMethods().Count()} methods"); } catch { }

        // Initialize the Linux→Windows path-translation table and register the
        // mod-source PathSubstitutionRewriter into DotNetCompat's compiler hook
        // *here* (preloader Finish), not in Plugin.Init. On the dedicated
        // server, IPlugin.Init runs AFTER the auto-loaded session has already
        // compiled and started its mods, so registering from Init misses the
        // mod-compile window entirely — the symptom is BuildInfo printing
        // "Mod scripts cannot read from mod folders!" because Path.GetFullPath
        // never got swapped for the WindowsPath shim during compilation.
        //
        // Safe to run here: PathTranslation.Init reads only Environment vars,
        // and RewriterRegistration touches MyScriptCompiler.Static, whose
        // static-readonly initializer fires the moment the type is referenced
        // — Harmony just patched methods on it above, so it's loaded.
        // DotNetCompat is also a Finish-category consumer and its preloader
        // runs before ours (Pulsar profile order), so its
        // CompilerHookExtensions extension point is already in the AppDomain
        // by this point.
        PathTranslation.Init();
        RewriterRegistration.Register();
    }

    private static bool s_nativeWrappersInitialized;

    // Hand the wrapper libraries the absolute paths to the Windows DLLs they
    // PE-load (the SE-shipped Havok.dll, RecastDetour.dll, VRage.Native.dll).
    // The wrapper .so files themselves are already in the process via
    // Pulsar's NativeLibraryPreloader, so the [DllImport] calls these Init()
    // methods make resolve against the preloaded handles.
    //
    // d3dcompiler_47.dll is not initialised on the server: the dedicated
    // server installs MyNullRender via MyRenderProxy.Initialize and never
    // compiles shaders.
    //
    // Guarded against duplicate invocation: Preloader.Finish() has been seen
    // to run twice (stale plugin DLL alongside a fresh one). Loading the
    // same DLL twice through the PE loader doubles every export entry and
    // overflows the 4096-slot table mid-Havok.
    private static void InitNativeWrappers()
    {
        if (s_nativeWrappersInitialized)
        {
            throw new Exception("[LinuxCompatServer] InitNativeWrappers: already initialized. This is the second attempt.");
        }
        s_nativeWrappersInitialized = true;

        var gameRoot = Environment.GetEnvironmentVariable("SPACE_ENGINEERS_ROOT");
        if (string.IsNullOrEmpty(gameRoot))
        {
            Console.WriteLine("[LinuxCompatServer] WARNING: SPACE_ENGINEERS_ROOT not set, cannot initialize native wrappers");
            return;
        }

        // On the dedicated server the binaries live under DedicatedServer64
        // rather than Bin64. Probe both so SPACE_ENGINEERS_ROOT can point at
        // either install layout.
        var binDir = Path.Combine(gameRoot, "DedicatedServer64");
        if (!Directory.Exists(binDir))
            binDir = Path.Combine(gameRoot, "Bin64");

        InitWrapper("Havok",        binDir, "Havok.dll",          HavokLinux.Init);
        InitWrapper("RecastDetour", binDir, "RecastDetour.dll",   RecastDetourLinux.Init);
        InitWrapper("VRageNative",  binDir, "VRage.Native.dll",   VRageNativeLinux.Init);
    }

    private static void InitWrapper(string name, string binDir, string dllName, Action<string> initFunc)
    {
        var dllPath = Path.Combine(binDir, dllName);
        if (!File.Exists(dllPath))
        {
            Console.WriteLine($"[LinuxCompatServer] WARNING: {dllName} not found at {dllPath}");
            return;
        }

        initFunc(dllPath);
        Console.WriteLine($"[LinuxCompatServer] {name} initialized: {dllPath}");
    }
}

// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using ClientPlugin.Compatibility;
using ClientPlugin.Compatibility.Rendering;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;

// IMPORTANT: MUST NOT USE A NAMESPACE, otherwise Pulsar won't find the Preloader class! 
//namespace ClientPlugin;

// ReSharper disable once UnusedType.Global
public static class Preloader
{
    static Preloader()
    {
        // Register a resolver for our own assembly BEFORE any Finish() runs.
        // Preloader.Patch() redirects SharpDX.XAudio2 AssemblyRefs in VRage.Audio and
        // VRage.Platform.Windows to "LinuxCompat 1.0.0.0". When other preloader plugins
        // (notably se-dotnet-compat) run Harmony.PatchCategory in their Finish(), Harmony
        // reflects on game types that now carry TypeRefs into LinuxCompat. The default
        // AssemblyLoadContext probing does not know where Pulsar placed our plugin DLL,
        // so it throws FileNotFoundException unless we answer by name here.
        //
        // We also answer for the Pulsar dev-folder randomized identity (e.g.
        // "LinuxCompat_fhop0d1n.jo0"). Pulsar renames the in-memory assembly
        // identity for hot-reload, and the PathSubstitutionRewriter plumbs a
        // metadata reference built from that in-memory image into MyScriptCompiler.
        // Compiled mod assemblies therefore carry TypeRefs into the renamed
        // identity; at JIT time the runtime asks for that exact name and fails
        // because Assembly.Load(byte[])-loaded assemblies are not findable by
        // simple-name through default-ALC binding.
        var selfAssembly = typeof(Preloader).Assembly;
        var selfName = selfAssembly.GetName().Name;
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var name = new AssemblyName(args.Name).Name;
            if (name == null) return null;
            return name == "LinuxCompat" || name == selfName ? selfAssembly : null;
        };
    }

    // ReSharper disable once UnusedMember.Global
    public static IEnumerable<string> TargetDLLs { get; } =
    [
        // Game DLLs
        "HavokWrapper.dll",
        "Sandbox.Common.dll",
        "Sandbox.Game.dll",
        "Sandbox.Graphics.dll",
        "SpaceEngineers.Game.dll",
        "VRage.dll",
        "VRage.Audio.dll",
        "VRage.Game.dll",
        "VRage.Input.dll",
        "VRage.Library.dll",
        "VRage.Math.dll",
        "VRage.Network.dll",
        "VRage.Platform.Windows.dll",
        "VRage.Render.dll",
        "VRage.Render11.dll",
        "VRage.Scripting.dll",
        "VRage.Steam.dll",

        // Dependency DLLs
        "SharpDX.dll",
        "SharpDX.DXGI.dll",
        "SixLabors.ImageSharp.dll"
    ];

    // ReSharper disable once UnusedMember.Global
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void Patch(AssemblyDefinition asmDef)
    {
        var asmName = asmDef.Name.Name;
        Console.WriteLine($"[LinuxCompat] Preloader.Patch: {asmName}");
        switch (asmName)
        {
            case "VRage.Platform.Windows":
                PatchVRagePlatformWindows(asmDef);
                break;
            case "VRage.Audio":
                PatchVRageAudio(asmDef);
                break;
            case "VRage.Steam":
                PatchVRageSteam(asmDef);
                break;
            case "SharpDX":
                PatchSharpDX(asmDef);
                break;
            case "VRage.Game":
                ClientPlugin.Patches.PathHandling.MyModContextPrepatch.Prepatch(asmDef);
                break;
            case "SpaceEngineers.Game":
                PatchSpaceEngineersGame(asmDef);
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
    // SONAME ldconfig publishes for libopus0). The first time MyVoiceChatLogic
    // tries to compress mic input or decompress an incoming voice packet,
    // opus_encoder_create / opus_decoder_create throws DllNotFoundException
    // from a ParallelTasks worker, and the game crashes mid-session.
    //
    // Cecil pre-rewrite is the durable fix: mutate the single shared
    // ModuleReference("Opus.dll") to libopus.so.0 before SpaceEngineers.Game
    // is JITted, so every P/Invoke site sees the unambiguous Linux name and
    // the runtime dlopens it directly. Verified on the shipping build:
    //   ModuleReferences (count=1): Opus.dll
    //   Total Opus.dll P/Invokes: 10
    //   All share same ModuleReference: True
    // — so a single rename rewires every site without touching individual
    // PInvokeInfo records. libopus's C ABI is stable across libopus0 versions
    // shipped in the last decade and matches the bundled Windows Opus.dll the
    // game ships, so no header/wrapper code on the managed side has to change;
    // the Steam-side capture path (MySteamMicrophone -> SteamUser.GetVoice /
    // DecompressVoice) is platform-agnostic and works through the Steamworks
    // Linux SDK as-is.
    //
    // libopus.so.0 (SONAME) is preferred over the unversioned libopus.so:
    // org.freedesktop.Platform 25.08 (the Flatpak runtime) and every
    // libopus0-bearing distro ship the SONAME unconditionally, while the
    // unversioned symlink only exists when libopus-dev is installed.
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
            Console.WriteLine($"[LinuxCompat] Preloader: rewrote {renamed} ModuleReference(s) Opus.dll -> libopus.so.0 in SpaceEngineers.Game");
        else
            Console.WriteLine("[LinuxCompat] Preloader: no Opus.dll ModuleReference in SpaceEngineers.Game (already patched or upstream changed P/Invoke names?)");
    }

    private static void PatchVRagePlatformWindows(AssemblyDefinition asmDef)
    {
        // Redirect SharpDX.XAudio2 AssemblyRef -> LinuxCompat so any XAudio2/X3DAudio
        // types referenced from VRage.Platform.Windows resolve to the shim in this plugin.
        RedirectAssemblyRef(asmDef, "SharpDX.XAudio2", "LinuxCompat");

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

        var myPlatformRender = asmDef.MainModule.GetType("VRage.Platform.Windows.Render.MyPlatformRender");
        if (myPlatformRender != null)
        {
            PatchCreateRenderDevice(myPlatformRender, asmDef.MainModule);
            PatchCreateSwapChain(myPlatformRender, asmDef.MainModule);
            PatchApplySettings(myPlatformRender);
            PatchFixSettings(myPlatformRender);
        }

        var myWindowsRender = asmDef.MainModule.GetType("VRage.Platform.Windows.Render.MyWindowsRender");
        if (myWindowsRender != null)
        {
            // The original IL is `m_windows.GameWindow?.OnModeChanged(...)`.
            // GameWindow is typed MyGameWindow (WinForms) and is null on Linux,
            // so the call is a silent no-op. NOP-ing it removes a stale call
            // for clarity; a Harmony postfix on these two methods routes the
            // mode-change to our SDL window via WindowModeRouter.
            NopGameWindowOnModeChanged(myWindowsRender, "CreateRenderDevice");
            NopGameWindowOnModeChanged(myWindowsRender, "ApplyRenderSettings");
        }
    }

    private static void PatchSharpDX(AssemblyDefinition asmDef)
    {
        var module = asmDef.MainModule;
        var resultDescriptor = module.GetType("SharpDX.ResultDescriptor");
        if (resultDescriptor == null) return;

        var method = resultDescriptor.Methods.FirstOrDefault(m => m.Name == "GetDescriptionFromResultCode");
        if (method == null) return;

        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        var il = method.Body.GetILProcessor();

        // Return "HRESULT 0x" + resultCode.ToString("X8")
        il.Append(il.Create(OpCodes.Ldstr, "HRESULT 0x"));
        il.Append(il.Create(OpCodes.Ldarga_S, method.Parameters[0]));
        il.Append(il.Create(OpCodes.Ldstr, "X8"));
        var int32ToString = module.ImportReference(
            typeof(int).GetMethod("ToString", [typeof(string)]));
        il.Append(il.Create(OpCodes.Call, int32ToString));
        var stringConcat = module.ImportReference(
            typeof(string).GetMethod("Concat", [typeof(string), typeof(string)]));
        il.Append(il.Create(OpCodes.Call, stringConcat));
        il.Append(il.Create(OpCodes.Ret));

        Console.WriteLine("[LinuxCompat] Patched SharpDX.ResultDescriptor.GetDescriptionFromResultCode to avoid kernel32.dll");
    }

    private static void PatchVRageAudio(AssemblyDefinition asmDef)
    {
        // Redirect SharpDX.XAudio2 AssemblyRef -> LinuxCompat so the shim types
        // defined in this plugin (namespaces SharpDX.XAudio2, SharpDX.XAudio2.Fx,
        // SharpDX.XAPO.Fx, SharpDX.X3DAudio, and SharpDX.ErrorEventArgs)
        // resolve in place of the real SharpDX.XAudio2.dll.
        RedirectAssemblyRef(asmDef, "SharpDX.XAudio2", "LinuxCompat");
    }

    private static readonly Version LinuxCompatVersion = new Version(1, 0, 0, 0);

    private static void RedirectAssemblyRef(AssemblyDefinition asmDef, string fromName, string toName)
    {
        var module = asmDef.MainModule;
        var asmRef = module.AssemblyReferences.FirstOrDefault(r => r.Name == fromName);
        if (asmRef == null)
        {
            Console.WriteLine($"[LinuxCompat] RedirectAssemblyRef: {asmDef.Name.Name} has no AssemblyRef '{fromName}' (skipping)");
            return;
        }

        asmRef.Name = toName;
        asmRef.Version = LinuxCompatVersion;
        asmRef.PublicKeyToken = Array.Empty<byte>();
        asmRef.PublicKey = Array.Empty<byte>();
        asmRef.Culture = string.Empty;
        asmRef.HashAlgorithm = Mono.Cecil.AssemblyHashAlgorithm.None;

        Console.WriteLine($"[LinuxCompat] Redirected AssemblyRef in {asmDef.Name.Name}: {fromName} -> {toName} {LinuxCompatVersion}");
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

                    Console.WriteLine($"[LinuxCompat] Replaced RequestCurrentStats with RequestUserStats in {method.Name}");
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
                Console.WriteLine($"[LinuxCompat] Rewrote SteamUGC.{methodName}({originalParamCount}) -> ({originalParamCount + 1}, false) in {method.Name}");
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
                var varIndex = method.Body.Variables.Count - 1;

                // Insert: ldloca identityVar, initobj SteamNetworkingIdentity, ldloca identityVar
                var ldloca1 = il.Create(OpCodes.Ldloca_S, identityVar);
                var initobj = il.Create(OpCodes.Initobj, steamNetIdType);
                var ldloca2 = il.Create(OpCodes.Ldloca_S, identityVar);

                il.InsertBefore(instr, ldloca1);
                il.InsertBefore(instr, initobj);
                il.InsertBefore(instr, ldloca2);

                // Replace the call
                instr.Operand = newMethodRef;

                Console.WriteLine($"[LinuxCompat] Patched GetAuthSessionTicket with SteamNetworkingIdentity");
                break;
            }
        }
    }

    private static void PatchCreateRenderDevice(TypeDefinition type, ModuleDefinition module)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == "CreateRenderDevice");
        if (method?.HasBody != true) return;

        var il = method.Body.GetILProcessor();
        var instructions = method.Body.Instructions;

        for (int i = 0; i < instructions.Count; i++)
        {
            var instr = instructions[i];
            if (instr.OpCode == OpCodes.Newobj && instr.Operand is MethodReference ctor &&
                ctor.DeclaringType.Name == "Device" &&
                ctor.Parameters.Count >= 3 &&
                (ctor.Parameters[0].ParameterType.Name == "Adapter" || ctor.Parameters[0].ParameterType.Name == "Adapter1"))
            {
                // Find the instruction that loads the adapter variable (a few instructions before)
                // We need to replace the adapter load with DriverType.Hardware (int 1)
                // The adapter comes from GetAdapter() call, stored in a local variable
                // Find the ldloc that pushes the adapter onto the stack before this newobj

                // Walk backwards to find the ldloc for adapter
                for (int j = i - 1; j >= 0 && j >= i - 15; j--)
                {
                    if (instructions[j].OpCode == OpCodes.Ldloc || instructions[j].OpCode == OpCodes.Ldloc_S ||
                        instructions[j].OpCode == OpCodes.Ldloc_0 || instructions[j].OpCode == OpCodes.Ldloc_1 ||
                        instructions[j].OpCode == OpCodes.Ldloc_2 || instructions[j].OpCode == OpCodes.Ldloc_3)
                    {
                        // Check if this is the adapter variable (type Adapter)
                        VariableDefinition varDef = null;
                        if (instructions[j].Operand is VariableDefinition vd)
                            varDef = vd;
                        else if (instructions[j].Operand is int vi)
                            varDef = method.Body.Variables[vi];
                        else
                        {
                            int idx = instructions[j].OpCode == OpCodes.Ldloc_0 ? 0 :
                                      instructions[j].OpCode == OpCodes.Ldloc_1 ? 1 :
                                      instructions[j].OpCode == OpCodes.Ldloc_2 ? 2 : 3;
                            varDef = method.Body.Variables[idx];
                        }

                        if (varDef?.VariableType.Name == "Adapter")
                        {
                            // Replace ldloc adapter with ldc.i4.1 (DriverType.Hardware = 1)
                            SetInstr(instructions[j], OpCodes.Ldc_I4_1);

                            // Create new constructor reference for Device(DriverType, DeviceCreationFlags, FeatureLevel[])
                            // DriverType is in SharpDX assembly (same as FeatureLevel), not SharpDX.Direct3D11
                            var featureLevelScope = (ctor.Parameters[2].ParameterType is ArrayType at ? at.ElementType : ctor.Parameters[2].ParameterType).Scope;
                            var driverTypeRef = module.ImportReference(new TypeReference("SharpDX.Direct3D", "DriverType", module, featureLevelScope, true));
                            var newCtor = new MethodReference(".ctor", module.TypeSystem.Void, ctor.DeclaringType);
                            newCtor.HasThis = true;
                            newCtor.Parameters.Add(new ParameterDefinition(driverTypeRef));
                            newCtor.Parameters.Add(new ParameterDefinition(ctor.Parameters[1].ParameterType));
                            newCtor.Parameters.Add(new ParameterDefinition(ctor.Parameters[2].ParameterType));

                            instr.Operand = newCtor;
                            Console.WriteLine("[LinuxCompat] Patched CreateRenderDevice: Device(Adapter) -> Device(DriverType.Hardware)");
                            break;
                        }
                    }
                }
                break;
            }
        }
    }

    private static void PatchCreateSwapChain(TypeDefinition type, ModuleDefinition module)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == "CreateSwapChain");
        if (method?.HasBody != true) return;

        var instructions = method.Body.Instructions;

        for (int i = 0; i < instructions.Count; i++)
        {
            var instr = instructions[i];
            if (instr.OpCode == OpCodes.Stfld && instr.Operand is FieldReference fr)
            {
                if (fr.Name == "Flags" && fr.DeclaringType.Name == "SwapChainDescription" && i > 0)
                {
                    SetInstr(instructions[i - 1], OpCodes.Ldc_I4_0);
                    Console.WriteLine("[LinuxCompat] Patched CreateSwapChain: Flags = None");
                }
                if (fr.Name == "SwapEffect" && fr.DeclaringType.Name == "SwapChainDescription" && i > 0)
                {
                    SetInstr(instructions[i - 1], OpCodes.Ldc_I4_1);
                    Console.WriteLine("[LinuxCompat] Patched CreateSwapChain: SwapEffect = Sequential");
                }
                if (fr.Name == "Usage" && fr.DeclaringType.Name == "SwapChainDescription" && i > 0)
                {
                    SetInstr(instructions[i - 1], OpCodes.Ldc_I4, 0x30);
                    Console.WriteLine("[LinuxCompat] Patched CreateSwapChain: Usage = ShaderInput | RenderTargetOutput");
                }
            }
        }

        for (int i = 0; i < instructions.Count; i++)
        {
            if (instructions[i].OpCode == OpCodes.Callvirt && instructions[i].Operand is MethodReference mr &&
                mr.Name == "MakeWindowAssociation")
            {
                for (int j = i; j >= i - 3 && j >= 0; j--)
                    NopInstr(instructions[j]);
                Console.WriteLine("[LinuxCompat] Patched CreateSwapChain: NOP'd MakeWindowAssociation");
                break;
            }
        }
    }

    private static void PatchApplySettings(TypeDefinition type)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == "ApplySettings");
        if (method?.HasBody != true) return;

        // NOP all swap chain manipulation calls but keep the m_settings assignment
        // Find and NOP: ResizeTarget, SetFullscreenState, TryChangeToFullscreen calls
        var il = method.Body.GetILProcessor();
        var instructions = method.Body.Instructions;

        // Find the m_settings = settings.Value assignment (stsfld m_settings)
        int settingsStoreIdx = -1;
        for (int i = 0; i < instructions.Count; i++)
        {
            if (instructions[i].OpCode == OpCodes.Stsfld && instructions[i].Operand is FieldReference fr && fr.Name == "m_settings")
            {
                settingsStoreIdx = i;
                break;
            }
        }

        if (settingsStoreIdx < 0) return;

        // Keep everything up to and including "m_settings = settings.Value"
        // Replace everything after with ret
        // The pattern is: ldarga.s, call get_Value, stsfld m_settings, ...rest..., ret
        // We want to keep the store and add ret right after
        var retInstr = il.Create(OpCodes.Ret);
        // NOP all instructions after the stsfld m_settings, before the final ret
        for (int i = settingsStoreIdx + 1; i < instructions.Count; i++)
        {
            if (instructions[i].OpCode == OpCodes.Ret)
            {
                break;
            }
            NopInstr(instructions[i]);
        }

        Console.WriteLine("[LinuxCompat] Patched ApplySettings: NOP'd swap chain operations");
    }

    private static void PatchFixSettings(TypeDefinition type)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == "FixSettings");
        if (method?.HasBody != true) return;

        // Find the adapter.Outputs.Length == 0 check and NOP the branch
        // This prevents "Fullscreen is not acceptable (no output)" error on Linux
        var instructions = method.Body.Instructions;
        var il = method.Body.GetILProcessor();

        for (int i = 0; i < instructions.Count; i++)
        {
            if (instructions[i].OpCode == OpCodes.Callvirt && instructions[i].Operand is MethodReference mr &&
                mr.Name == "get_Outputs" && mr.DeclaringType.Name == "Adapter")
            {
                // Find the surrounding branch logic and NOP the fullscreen check
                // The pattern is: ldarg adapter, callvirt get_Outputs, ldlen, brfalse/brtrue
                // NOP from the ldarg to the branch target condition
                for (int j = i + 1; j < instructions.Count && j < i + 5; j++)
                {
                    if (instructions[j].OpCode == OpCodes.Ldlen)
                    {
                        // NOP the entire block from the comparison that loads adapter to the branch
                        // Find the start (usually ldarg or ldloc for adapter)
                        int start = i - 1;
                        while (start >= 0 && instructions[start].OpCode != OpCodes.Ldarg_1 &&
                               instructions[start].OpCode != OpCodes.Ldarg_S &&
                               !(instructions[start].OpCode == OpCodes.Ldloc || instructions[start].OpCode == OpCodes.Ldloc_S ||
                                 instructions[start].OpCode == OpCodes.Ldloc_0 || instructions[start].OpCode == OpCodes.Ldloc_1))
                        {
                            start--;
                        }

                        // Find the branch instruction after ldlen
                        for (int k = j + 1; k < instructions.Count && k < j + 5; k++)
                        {
                            if (instructions[k].OpCode == OpCodes.Brtrue || instructions[k].OpCode == OpCodes.Brtrue_S ||
                                instructions[k].OpCode == OpCodes.Brfalse || instructions[k].OpCode == OpCodes.Brfalse_S ||
                                instructions[k].OpCode == OpCodes.Bne_Un || instructions[k].OpCode == OpCodes.Bne_Un_S ||
                                instructions[k].OpCode == OpCodes.Beq || instructions[k].OpCode == OpCodes.Beq_S)
                            {
                                // Make the branch unconditional to skip the fullscreen check
                                // NOP from start to the branch, and change branch to unconditional
                                for (int n = start; n <= k; n++)
                                    NopInstr(instructions[n]);
                                Console.WriteLine("[LinuxCompat] Patched FixSettings: skipped adapter.Outputs check");
                                return;
                            }
                        }
                        break;
                    }
                }
                break;
            }
        }
    }

    private static void NopGameWindowOnModeChanged(TypeDefinition type, string methodName)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == methodName);
        if (method?.HasBody != true) return;

        var instructions = method.Body.Instructions;

        for (int i = instructions.Count - 1; i >= 0; i--)
        {
            if ((instructions[i].OpCode != OpCodes.Call && instructions[i].OpCode != OpCodes.Callvirt) ||
                !(instructions[i].Operand is MethodReference mr) ||
                mr.Name != "OnModeChanged")
                continue;

            int callIdx = i;

            // Walk backwards to find ldfld m_windows (start of the expression)
            int startIdx = -1;
            for (int j = callIdx - 1; j >= 0; j--)
            {
                if (instructions[j].OpCode == OpCodes.Ldfld &&
                    instructions[j].Operand is FieldReference fr &&
                    fr.Name == "m_windows")
                {
                    startIdx = j > 0 && instructions[j - 1].OpCode == OpCodes.Ldarg_0 ? j - 1 : j;
                    break;
                }
            }

            if (startIdx < 0) continue;

            for (int j = startIdx; j <= callIdx; j++)
                NopInstr(instructions[j]);

            Console.WriteLine($"[LinuxCompat] NOP'd GameWindow.OnModeChanged in {type.Name}.{methodName}");
        }
    }

    private static void NopInstr(Instruction instr)
    {
        instr.OpCode = OpCodes.Nop;
        instr.Operand = null;
    }

    private static void SetInstr(Instruction instr, OpCode opCode, object operand = null)
    {
        instr.OpCode = opCode;
        instr.Operand = operand;
    }

    private static void NopMethodBody(TypeDefinition type, string methodName)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == methodName);
        if (method == null) return;

        var il = method.Body.GetILProcessor();
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
    }

    private static void ReplaceWithBoolReturn(TypeDefinition type, string methodName, bool value)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == methodName);
        if (method == null) return;

        var il = method.Body.GetILProcessor();
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        il.Append(il.Create(value ? Mono.Cecil.Cil.OpCodes.Ldc_I4_1 : Mono.Cecil.Cil.OpCodes.Ldc_I4_0));
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
    }

    private static void ReplaceWithConstant(TypeDefinition type, string methodName, float value)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == methodName);
        if (method == null) return;

        var il = method.Body.GetILProcessor();
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_R4, value));
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
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
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4, (int)value));
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
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
            il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_0));
        }
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
    }

    private static void ReplacePInvokeWithConstant(TypeDefinition type, string methodName)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == methodName && m.IsPInvokeImpl);
        if (method == null)
        {
            Console.WriteLine($"[LinuxCompat] ReplacePInvokeWithConstant: {type.Name}.{methodName} not found (candidates: {string.Join(", ", type.Methods.Where(m => m.Name == methodName).Select(m => $"IsPInvoke={m.IsPInvokeImpl}"))})");
            return;
        }
        Console.WriteLine($"[LinuxCompat] ReplacePInvokeWithConstant: {type.Name}.{methodName}");

        method.IsPInvokeImpl = false;
        method.IsPreserveSig = false;
        method.PInvokeInfo = null;
        method.ImplAttributes = Mono.Cecil.MethodImplAttributes.IL | Mono.Cecil.MethodImplAttributes.Managed;
        method.Body = new Mono.Cecil.Cil.MethodBody(method);

        var il = method.Body.GetILProcessor();
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_0));
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
    }

    private static void ReplaceProcessPrivateMemory(TypeDefinition type)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == "get_ProcessPrivateMemory");
        if (method == null) return;

        var module = type.Module;
        var processType = module.ImportReference(typeof(System.Diagnostics.Process));
        var getCurrentProcess = module.ImportReference(typeof(System.Diagnostics.Process).GetMethod("GetCurrentProcess"));
        var privateMemSize = module.ImportReference(typeof(System.Diagnostics.Process).GetProperty("PrivateMemorySize64")!.GetGetMethod()!);

        var il = method.Body.GetILProcessor();
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Call, getCurrentProcess));
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Callvirt, privateMemSize));
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
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
        // alias table (DXVK, EOS, Steamworks, SDL3, the PE-loader wrapper
        // libs) live in Pulsar's NativeLibraryPreloader, which runs at the
        // top of Pulsar.Legacy.Program.Main before any plugin loads. All
        // that remains here is handing the wrapper libraries the absolute
        // paths to the Windows DLLs they need to PE-load.
        InitNativeWrappers();

        // Bring up the dedicated SDL render thread before any SDL3 use. It must
        // exist by the time MyCommonProgramStartup.InitSplashScreen fires our
        // ShowSplashScreenPatch, which dispatches MySdlSplashScreen.Show via
        // SdlRenderThread.Invoke. InitSplashScreen runs inside MyProgram.Main
        // — before Pulsar's plugin manager calls Plugin.Init — so starting the
        // thread here in Preloader.Finish (which runs prior to MyProgram.Main)
        // is the only place early enough. Plugin.Init's call to Start() is
        // kept as a no-op safety net (Start is idempotent).
        ClientPlugin.Compatibility.SdlRenderThread.Start();

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
        
        var harmony = new Harmony("LinuxCompat");
        try
        {
            harmony.PatchCategory("Finish");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[LinuxCompat] PatchCategory(\"Finish\") threw: {e}");
            try { VRage.Utils.MyLog.Default.WriteLineAndConsole($"[LinuxCompat] PatchCategory(\"Finish\") threw: {e}"); } catch { }
            throw;
        }
        Console.WriteLine($"[LinuxCompat] PatchCategory(\"Finish\") applied {harmony.GetPatchedMethods().Count()} methods");
        try { VRage.Utils.MyLog.Default.WriteLineAndConsole($"[LinuxCompat] PatchCategory(\"Finish\") applied {harmony.GetPatchedMethods().Count()} methods"); } catch { }
    }

    private static bool s_nativeWrappersInitialized;

    // Hand the wrapper libraries the absolute paths to the Windows DLLs they
    // PE-load (the SE-shipped d3dcompiler_47.dll, Havok.dll, RecastDetour.dll,
    // VRage.Native.dll). The wrapper .so files themselves are already in the
    // process via Pulsar's NativeLibraryPreloader, so the [DllImport] calls
    // these Init() methods make resolve against the preloaded handles.
    //
    // Guarded against duplicate invocation: Preloader.Finish() has been seen
    // to run twice (stale LinuxCompat.dll alongside a fresh one, or another
    // entry point also calling into the wrappers). Loading the same DLL twice
    // through the PE loader doubles every export entry and overflows the
    // 4096-slot table mid-Havok. The C-side Init() functions are also guarded
    // (see Havok.cpp / D3DCompiler.cpp / RecastDetour.cpp / VRageNative.cpp);
    // this flag is the belt to their suspenders and only helps when both
    // duplicate calls share this assembly's static state.
    private static void InitNativeWrappers()
    {
        if (s_nativeWrappersInitialized)
        {
            throw new Exception("[LinuxCompat] InitNativeWrappers: already initialized. This is the second attempt.");
        }
        s_nativeWrappersInitialized = true;

        var gameRoot = Environment.GetEnvironmentVariable("SPACE_ENGINEERS_ROOT");
        if (string.IsNullOrEmpty(gameRoot))
        {
            Console.WriteLine("[LinuxCompat] WARNING: SPACE_ENGINEERS_ROOT not set, cannot initialize native wrappers");
            return;
        }

        var bin64 = Path.Combine(gameRoot, "Bin64");
        InitWrapper("D3DCompiler",  bin64, "d3dcompiler_47.dll", D3DCompilerLinux.Init);
        InitWrapper("Havok",        bin64, "Havok.dll",          HavokLinux.Init);
        InitWrapper("RecastDetour", bin64, "RecastDetour.dll",   RecastDetourLinux.Init);
        InitWrapper("VRageNative",  bin64, "VRage.Native.dll",   VRageNativeLinux.Init);
    }

    private static void InitWrapper(string name, string binDir, string dllName, Action<string> initFunc)
    {
        var dllPath = Path.Combine(binDir, dllName);
        if (!File.Exists(dllPath))
        {
            Console.WriteLine($"[LinuxCompat] WARNING: {dllName} not found at {dllPath}");
            return;
        }

        initFunc(dllPath);
        Console.WriteLine($"[LinuxCompat] {name} initialized: {dllPath}");
    }
}
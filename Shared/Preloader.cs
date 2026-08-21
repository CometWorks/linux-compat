// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Pulsar and Magnetar discover Preloader in the global namespace.

// ReSharper disable once UnusedType.Global
public static class Preloader
{
    static Preloader()
    {
        // Loader-randomized identities must resolve to this in-memory assembly.
        var selfAssembly = typeof(Preloader).Assembly;
        var selfName = selfAssembly.GetName().Name;
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var name = new AssemblyName(args.Name).Name;
            if (name == null)
                return null;
            return name == "LinuxCompat" || name == "LinuxCompatServer" || name == selfName
                ? selfAssembly
                : null;
        };
    }

    // ReSharper disable once UnusedMember.Global
    public static void Initialize() => ClientPlugin.Compatibility.NativeLibraries.Initialize();

    // ReSharper disable once UnusedMember.Global
    public static IEnumerable<string> TargetDLLs { get; } =
    [
#if MAGNETAR
        "Sandbox.Game.dll",
        "SpaceEngineers.Game.dll",
        "VRage.dll",
        "VRage.Dedicated.dll",
        "VRage.Game.dll",
        "VRage.Library.dll",
        "VRage.Platform.Windows.dll",
        "VRage.Scripting.dll",
        "VRage.Steam.dll",
#else
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
        "SharpDX.dll",
        "SharpDX.DXGI.dll",
        "SixLabors.ImageSharp.dll",
#endif
    ];

    // ReSharper disable once UnusedMember.Global
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    public static void Patch(AssemblyDefinition asmDef)
    {
        var asmName = asmDef.Name.Name;
        Console.WriteLine($"[LinuxCompat] Preloader.Patch: {asmName}");
        switch (asmName)
        {
            case "VRage.Platform.Windows":
                PatchVRagePlatformWindows(asmDef);
                break;
#if !MAGNETAR
            case "VRage.Audio":
                PatchVRageAudio(asmDef);
                break;
#endif
            case "VRage.Steam":
                PatchVRageSteam(asmDef);
                break;
#if !MAGNETAR
            case "SharpDX":
                PatchSharpDX(asmDef);
                break;
#endif
            case "VRage.Game":
                ClientPlugin.Patches.PathHandling.MyModContextPrepatch.Prepatch(asmDef);
                break;
            case "VRage.Library":
                ClientPlugin.Patches.PathHandling.MyFileSystemOpenPrepatch.Prepatch(asmDef);
                break;
            case "SpaceEngineers.Game":
                PatchSpaceEngineersGame(asmDef);
                break;
#if MAGNETAR
            case "VRage.Dedicated":
                ServerPlugin.Patches.PlatformGuards.AttachConsolePrepatch.Prepatch(asmDef);
                ServerPlugin.Patches.PlatformGuards.IsVcRedist2019InstalledPrepatch.Prepatch(
                    asmDef
                );
                break;
#endif
        }
    }

    // Linux distributions expose Opus through its versioned SONAME.
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
            Console.WriteLine(
                $"[LinuxCompat] Preloader: rewrote {renamed} ModuleReference(s) Opus.dll -> libopus.so.0 in SpaceEngineers.Game"
            );
        else
            Console.WriteLine(
                "[LinuxCompat] Preloader: no Opus.dll ModuleReference in SpaceEngineers.Game (already patched or upstream changed P/Invoke names?)"
            );
    }

    private static void PatchVRagePlatformWindows(AssemblyDefinition asmDef)
    {
#if !MAGNETAR
        // Resolve XAudio2 and X3DAudio references to this assembly's shims.
        RedirectAssemblyRef(asmDef, "SharpDX.XAudio2", "LinuxCompat");
#endif

        var myWindowsSystem = asmDef.MainModule.GetType(
            "VRage.Platform.Windows.Sys.MyWindowsSystem"
        );
        if (myWindowsSystem == null)
            return;

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

        var myWindowsWindows = asmDef.MainModule.GetType(
            "VRage.Platform.Windows.Forms.MyWindowsWindows"
        );
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

#if !MAGNETAR
        var myPlatformRender = asmDef.MainModule.GetType(
            "VRage.Platform.Windows.Render.MyPlatformRender"
        );
        if (myPlatformRender != null)
        {
            PatchCreateRenderDevice(myPlatformRender, asmDef.MainModule);
            PatchCreateSwapChain(myPlatformRender, asmDef.MainModule);
            PatchApplySettings(myPlatformRender);
            PatchFixSettings(myPlatformRender);
        }

        var myWindowsRender = asmDef.MainModule.GetType(
            "VRage.Platform.Windows.Render.MyWindowsRender"
        );
        if (myWindowsRender != null)
        {
            // The WinForms GameWindow is null on Linux; Harmony postfixes route mode changes to SDL.
            NopGameWindowOnModeChanged(myWindowsRender, "CreateRenderDevice");
            NopGameWindowOnModeChanged(myWindowsRender, "ApplyRenderSettings");
        }
#endif
    }

#if !MAGNETAR
    private static void PatchSharpDX(AssemblyDefinition asmDef)
    {
        var module = asmDef.MainModule;
        var resultDescriptor = module.GetType("SharpDX.ResultDescriptor");
        if (resultDescriptor == null)
            return;

        var method = resultDescriptor.Methods.FirstOrDefault(m =>
            m.Name == "GetDescriptionFromResultCode"
        );
        if (method == null)
            return;

        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        var il = method.Body.GetILProcessor();

        il.Append(il.Create(OpCodes.Ldstr, "HRESULT 0x"));
        il.Append(il.Create(OpCodes.Ldarga_S, method.Parameters[0]));
        il.Append(il.Create(OpCodes.Ldstr, "X8"));
        var int32ToString = module.ImportReference(
            typeof(int).GetMethod("ToString", [typeof(string)])
        );
        il.Append(il.Create(OpCodes.Call, int32ToString));
        var stringConcat = module.ImportReference(
            typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])
        );
        il.Append(il.Create(OpCodes.Call, stringConcat));
        il.Append(il.Create(OpCodes.Ret));

        Console.WriteLine(
            "[LinuxCompat] Patched SharpDX.ResultDescriptor.GetDescriptionFromResultCode to avoid kernel32.dll"
        );
    }

    private static void PatchVRageAudio(AssemblyDefinition asmDef)
    {
        // Resolve SharpDX.XAudio2 types to this assembly's shims.
        RedirectAssemblyRef(asmDef, "SharpDX.XAudio2", "LinuxCompat");
    }

    private static readonly Version LinuxCompatVersion = new Version(1, 0, 0, 0);

    private static void RedirectAssemblyRef(
        AssemblyDefinition asmDef,
        string fromName,
        string toName
    )
    {
        var module = asmDef.MainModule;
        var asmRef = module.AssemblyReferences.FirstOrDefault(r => r.Name == fromName);
        if (asmRef == null)
        {
            Console.WriteLine(
                $"[LinuxCompat] RedirectAssemblyRef: {asmDef.Name.Name} has no AssemblyRef '{fromName}' (skipping)"
            );
            return;
        }

        asmRef.Name = toName;
        asmRef.Version = LinuxCompatVersion;
        asmRef.PublicKeyToken = Array.Empty<byte>();
        asmRef.PublicKey = Array.Empty<byte>();
        asmRef.Culture = string.Empty;
        asmRef.HashAlgorithm = Mono.Cecil.AssemblyHashAlgorithm.None;

        Console.WriteLine(
            $"[LinuxCompat] Redirected AssemblyRef in {asmDef.Name.Name}: {fromName} -> {toName} {LinuxCompatVersion}"
        );
    }
#endif

    private static void PatchVRageSteam(AssemblyDefinition asmDef)
    {
        var module = asmDef.MainModule;
        var mySteamService = module.GetType("VRage.Steam.MySteamService");
        if (mySteamService == null)
            return;

        var steamUserIdField = mySteamService.Fields.FirstOrDefault(f => f.Name == "SteamUserId");
        if (steamUserIdField == null)
            return;

        foreach (var method in mySteamService.Methods)
        {
            if (!method.HasBody)
                continue;

            var il = method.Body.GetILProcessor();
            var instructions = method.Body.Instructions;

            for (int i = 0; i < instructions.Count; i++)
            {
                var instr = instructions[i];
                if (
                    instr.OpCode == OpCodes.Call
                    && instr.Operand is MethodReference methodRef
                    && methodRef.Name == "RequestCurrentStats"
                    && methodRef.DeclaringType.Name == "SteamUserStats"
                )
                {
                    var steamUserStatsType = methodRef.DeclaringType;
                    var csteamIdType = steamUserIdField.FieldType;
                    var steamApiCallType = module.ImportReference(
                        new TypeReference(
                            "Steamworks",
                            "SteamAPICall_t",
                            module,
                            methodRef.DeclaringType.Scope,
                            true
                        )
                    );
                    var requestUserStats = new MethodReference(
                        "RequestUserStats",
                        steamApiCallType,
                        steamUserStatsType
                    );
                    requestUserStats.Parameters.Add(new ParameterDefinition(csteamIdType));

                    var loadThis = il.Create(OpCodes.Ldarg_0);
                    var loadField = il.Create(OpCodes.Ldfld, steamUserIdField);
                    il.InsertBefore(instr, loadThis);
                    il.InsertBefore(instr, loadField);

                    instr.Operand = requestUserStats;

                    Console.WriteLine(
                        $"[LinuxCompat] Replaced RequestCurrentStats with RequestUserStats in {method.Name}"
                    );
                    i += 2;
                }
            }
        }

        var getAuthTicket = mySteamService.Methods.FirstOrDefault(m =>
            m.Name == "GetAuthSessionTicket"
        );
        if (getAuthTicket?.HasBody == true)
        {
            PatchGetAuthSessionTicket(getAuthTicket, module);
        }

        // Supply the bool parameters required by the Steamworks.NET overloads.
        var mySteamUgcClient = module.GetType("VRage.Steam.Steamworks.MySteamUgcClient");
        if (mySteamUgcClient != null)
        {
            AppendDefaultFalseToSteamUgcCall(
                mySteamUgcClient,
                "SetItemTags",
                originalParamCount: 2,
                module
            );
            AppendDefaultFalseToSteamUgcCall(
                mySteamUgcClient,
                "GetNumSubscribedItems",
                originalParamCount: 0,
                module
            );
            AppendDefaultFalseToSteamUgcCall(
                mySteamUgcClient,
                "GetSubscribedItems",
                originalParamCount: 2,
                module
            );
        }
    }

    private static void AppendDefaultFalseToSteamUgcCall(
        TypeDefinition containerType,
        string methodName,
        int originalParamCount,
        ModuleDefinition module
    )
    {
        foreach (var method in containerType.Methods)
        {
            if (!method.HasBody)
                continue;
            var il = method.Body.GetILProcessor();
            var instructions = method.Body.Instructions.ToList();
            foreach (var instr in instructions)
            {
                if (instr.OpCode != OpCodes.Call)
                    continue;
                if (instr.Operand is not MethodReference mr)
                    continue;
                if (mr.Name != methodName)
                    continue;
                if (mr.DeclaringType.Name != "SteamUGC")
                    continue;
                if (mr.Parameters.Count != originalParamCount)
                    continue;

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
                Console.WriteLine(
                    $"[LinuxCompat] Rewrote SteamUGC.{methodName}({originalParamCount}) -> ({originalParamCount + 1}, false) in {method.Name}"
                );
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
            if (
                instr.OpCode == OpCodes.Call
                && instr.Operand is MethodReference methodRef
                && methodRef.Name == "GetAuthSessionTicket"
                && methodRef.DeclaringType.Name == "SteamUser"
            )
            {
                // Cecil must encode SteamNetworkingIdentity as a value type for the JIT.
                var steamNetIdType = new TypeReference(
                    "Steamworks",
                    "SteamNetworkingIdentity",
                    module,
                    methodRef.DeclaringType.Scope
                )
                {
                    IsValueType = true,
                };
                steamNetIdType = module.ImportReference(steamNetIdType);

                var newMethodRef = new MethodReference(
                    "GetAuthSessionTicket",
                    methodRef.ReturnType,
                    methodRef.DeclaringType
                );
                newMethodRef.Parameters.Add(
                    new ParameterDefinition(new ArrayType(module.TypeSystem.Byte))
                );
                newMethodRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                newMethodRef.Parameters.Add(
                    new ParameterDefinition(new ByReferenceType(module.TypeSystem.UInt32))
                );
                newMethodRef.Parameters.Add(
                    new ParameterDefinition(new ByReferenceType(steamNetIdType))
                );

                var identityVar = new VariableDefinition(steamNetIdType);
                method.Body.Variables.Add(identityVar);

                var ldloca1 = il.Create(OpCodes.Ldloca_S, identityVar);
                var initobj = il.Create(OpCodes.Initobj, steamNetIdType);
                var ldloca2 = il.Create(OpCodes.Ldloca_S, identityVar);

                il.InsertBefore(instr, ldloca1);
                il.InsertBefore(instr, initobj);
                il.InsertBefore(instr, ldloca2);

                instr.Operand = newMethodRef;

                Console.WriteLine(
                    $"[LinuxCompat] Patched GetAuthSessionTicket with SteamNetworkingIdentity"
                );
                break;
            }
        }
    }

#if !MAGNETAR
    private static void PatchCreateRenderDevice(TypeDefinition type, ModuleDefinition module)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == "CreateRenderDevice");
        if (method?.HasBody != true)
            return;

        var instructions = method.Body.Instructions;

        for (int i = 0; i < instructions.Count; i++)
        {
            var instr = instructions[i];
            if (
                instr.OpCode == OpCodes.Newobj
                && instr.Operand is MethodReference ctor
                && ctor.DeclaringType.Name == "Device"
                && ctor.Parameters.Count >= 3
                && (
                    ctor.Parameters[0].ParameterType.Name == "Adapter"
                    || ctor.Parameters[0].ParameterType.Name == "Adapter1"
                )
            )
            {
                for (int j = i - 1; j >= 0 && j >= i - 15; j--)
                {
                    if (
                        instructions[j].OpCode == OpCodes.Ldloc
                        || instructions[j].OpCode == OpCodes.Ldloc_S
                        || instructions[j].OpCode == OpCodes.Ldloc_0
                        || instructions[j].OpCode == OpCodes.Ldloc_1
                        || instructions[j].OpCode == OpCodes.Ldloc_2
                        || instructions[j].OpCode == OpCodes.Ldloc_3
                    )
                    {
                        VariableDefinition varDef = null;
                        if (instructions[j].Operand is VariableDefinition vd)
                            varDef = vd;
                        else if (instructions[j].Operand is int vi)
                            varDef = method.Body.Variables[vi];
                        else
                        {
                            int idx =
                                instructions[j].OpCode == OpCodes.Ldloc_0 ? 0
                                : instructions[j].OpCode == OpCodes.Ldloc_1 ? 1
                                : instructions[j].OpCode == OpCodes.Ldloc_2 ? 2
                                : 3;
                            varDef = method.Body.Variables[idx];
                        }

                        if (varDef?.VariableType.Name == "Adapter")
                        {
                            SetInstr(instructions[j], OpCodes.Ldc_I4_1);

                            // DriverType shares FeatureLevel's SharpDX scope.
                            var featureLevelScope = (
                                ctor.Parameters[2].ParameterType is ArrayType at
                                    ? at.ElementType
                                    : ctor.Parameters[2].ParameterType
                            ).Scope;
                            var driverTypeRef = module.ImportReference(
                                new TypeReference(
                                    "SharpDX.Direct3D",
                                    "DriverType",
                                    module,
                                    featureLevelScope,
                                    true
                                )
                            );
                            var newCtor = new MethodReference(
                                ".ctor",
                                module.TypeSystem.Void,
                                ctor.DeclaringType
                            );
                            newCtor.HasThis = true;
                            newCtor.Parameters.Add(new ParameterDefinition(driverTypeRef));
                            newCtor.Parameters.Add(
                                new ParameterDefinition(ctor.Parameters[1].ParameterType)
                            );
                            newCtor.Parameters.Add(
                                new ParameterDefinition(ctor.Parameters[2].ParameterType)
                            );

                            instr.Operand = newCtor;
                            Console.WriteLine(
                                "[LinuxCompat] Patched CreateRenderDevice: Device(Adapter) -> Device(DriverType.Hardware)"
                            );
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
        if (method?.HasBody != true)
            return;

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
                if (
                    fr.Name == "SwapEffect"
                    && fr.DeclaringType.Name == "SwapChainDescription"
                    && i > 0
                )
                {
                    SetInstr(instructions[i - 1], OpCodes.Ldc_I4_1);
                    Console.WriteLine(
                        "[LinuxCompat] Patched CreateSwapChain: SwapEffect = Sequential"
                    );
                }
                if (fr.Name == "Usage" && fr.DeclaringType.Name == "SwapChainDescription" && i > 0)
                {
                    SetInstr(instructions[i - 1], OpCodes.Ldc_I4, 0x30);
                    Console.WriteLine(
                        "[LinuxCompat] Patched CreateSwapChain: Usage = ShaderInput | RenderTargetOutput"
                    );
                }
            }
        }

        for (int i = 0; i < instructions.Count; i++)
        {
            if (
                instructions[i].OpCode == OpCodes.Callvirt
                && instructions[i].Operand is MethodReference mr
                && mr.Name == "MakeWindowAssociation"
            )
            {
                for (int j = i; j >= i - 3 && j >= 0; j--)
                    NopInstr(instructions[j]);
                Console.WriteLine(
                    "[LinuxCompat] Patched CreateSwapChain: NOP'd MakeWindowAssociation"
                );
                break;
            }
        }
    }

    private static void PatchApplySettings(TypeDefinition type)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == "ApplySettings");
        if (method?.HasBody != true)
            return;

        var instructions = method.Body.Instructions;

        int settingsStoreIdx = -1;
        for (int i = 0; i < instructions.Count; i++)
        {
            if (
                instructions[i].OpCode == OpCodes.Stsfld
                && instructions[i].Operand is FieldReference fr
                && fr.Name == "m_settings"
            )
            {
                settingsStoreIdx = i;
                break;
            }
        }

        if (settingsStoreIdx < 0)
            return;

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
        if (method?.HasBody != true)
            return;

        // Null-render adapters have no outputs but still accept these settings.
        var instructions = method.Body.Instructions;
        var il = method.Body.GetILProcessor();

        for (int i = 0; i < instructions.Count; i++)
        {
            if (
                instructions[i].OpCode == OpCodes.Callvirt
                && instructions[i].Operand is MethodReference mr
                && mr.Name == "get_Outputs"
                && mr.DeclaringType.Name == "Adapter"
            )
            {
                for (int j = i + 1; j < instructions.Count && j < i + 5; j++)
                {
                    if (instructions[j].OpCode == OpCodes.Ldlen)
                    {
                        int start = i - 1;
                        while (
                            start >= 0
                            && instructions[start].OpCode != OpCodes.Ldarg_1
                            && instructions[start].OpCode != OpCodes.Ldarg_S
                            && !(
                                instructions[start].OpCode == OpCodes.Ldloc
                                || instructions[start].OpCode == OpCodes.Ldloc_S
                                || instructions[start].OpCode == OpCodes.Ldloc_0
                                || instructions[start].OpCode == OpCodes.Ldloc_1
                            )
                        )
                        {
                            start--;
                        }

                        for (int k = j + 1; k < instructions.Count && k < j + 5; k++)
                        {
                            if (
                                instructions[k].OpCode == OpCodes.Brtrue
                                || instructions[k].OpCode == OpCodes.Brtrue_S
                                || instructions[k].OpCode == OpCodes.Brfalse
                                || instructions[k].OpCode == OpCodes.Brfalse_S
                                || instructions[k].OpCode == OpCodes.Bne_Un
                                || instructions[k].OpCode == OpCodes.Bne_Un_S
                                || instructions[k].OpCode == OpCodes.Beq
                                || instructions[k].OpCode == OpCodes.Beq_S
                            )
                            {
                                for (int n = start; n <= k; n++)
                                    NopInstr(instructions[n]);
                                Console.WriteLine(
                                    "[LinuxCompat] Patched FixSettings: skipped adapter.Outputs check"
                                );
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
        if (method?.HasBody != true)
            return;

        var instructions = method.Body.Instructions;

        for (int i = instructions.Count - 1; i >= 0; i--)
        {
            if (
                (
                    instructions[i].OpCode != OpCodes.Call
                    && instructions[i].OpCode != OpCodes.Callvirt
                )
                || !(instructions[i].Operand is MethodReference mr)
                || mr.Name != "OnModeChanged"
            )
                continue;

            int callIdx = i;

            int startIdx = -1;
            for (int j = callIdx - 1; j >= 0; j--)
            {
                if (
                    instructions[j].OpCode == OpCodes.Ldfld
                    && instructions[j].Operand is FieldReference fr
                    && fr.Name == "m_windows"
                )
                {
                    startIdx = j > 0 && instructions[j - 1].OpCode == OpCodes.Ldarg_0 ? j - 1 : j;
                    break;
                }
            }

            if (startIdx < 0)
                continue;

            for (int j = startIdx; j <= callIdx; j++)
                NopInstr(instructions[j]);

            Console.WriteLine(
                $"[LinuxCompat] NOP'd GameWindow.OnModeChanged in {type.Name}.{methodName}"
            );
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
#endif

    private static void NopMethodBody(TypeDefinition type, string methodName)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == methodName);
        if (method == null)
            return;

        var il = method.Body.GetILProcessor();
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
    }

    private static void ReplaceWithBoolReturn(TypeDefinition type, string methodName, bool value)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == methodName);
        if (method == null)
            return;

        var il = method.Body.GetILProcessor();
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        il.Append(
            il.Create(value ? Mono.Cecil.Cil.OpCodes.Ldc_I4_1 : Mono.Cecil.Cil.OpCodes.Ldc_I4_0)
        );
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
    }

    private static void ReplaceWithConstant(TypeDefinition type, string methodName, float value)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == methodName);
        if (method == null)
            return;

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
        if (method == null)
            return;

        method.IsPInvokeImpl = false;
        method.IsPreserveSig = false;
        method.PInvokeInfo = null;
        method.ImplAttributes =
            Mono.Cecil.MethodImplAttributes.IL | Mono.Cecil.MethodImplAttributes.Managed;
        method.Body = new Mono.Cecil.Cil.MethodBody(method);

        var il = method.Body.GetILProcessor();
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4, (int)value));
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
    }

    private static void ReplaceWithDefaultReturn(TypeDefinition type, string methodName)
    {
        var method = type.Methods.FirstOrDefault(m =>
            m.Name == methodName && !m.IsPInvokeImpl && m.HasBody
        );
        if (method == null)
            return;

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

    private static void ReplaceProcessPrivateMemory(TypeDefinition type)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == "get_ProcessPrivateMemory");
        if (method == null)
            return;

        var module = type.Module;
        var getCurrentProcess = module.ImportReference(
            typeof(System.Diagnostics.Process).GetMethod("GetCurrentProcess")
        );
        var privateMemSize = module.ImportReference(
            typeof(System.Diagnostics.Process).GetProperty("PrivateMemorySize64")!.GetGetMethod()!
        );

        var il = method.Body.GetILProcessor();
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Call, getCurrentProcess));
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Callvirt, privateMemSize));
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
    }

    // ReSharper disable once UnusedMember.Global
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    public static void Finish()
    {
        AppContext.SetSwitch(
            "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization",
            true
        );

        Assembly.Load("System.Collections.Immutable");

#if !MAGNETAR
        // Splash creation uses SDL before Plugin.Init.
        if (ClientPlugin.Compatibility.RenderingConfig.AllowRendering)
            ClientPlugin.Compatibility.SdlRenderThread.Start();
#endif

        string[] dlls = ["System.Management"];
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var targetName = new AssemblyName(args.Name).Name;
            return dlls.Contains(targetName) ? Assembly.Load(targetName) : null;
        };

#if DEBUG && HARMONY_DEBUG
        Harmony.DEBUG = true;
#endif
#if MAGNETAR
        const string harmonyId = "LinuxCompatServer";
#else
        const string harmonyId = "LinuxCompat";
#endif
        var harmony = new Harmony(harmonyId);
        try
        {
            harmony.PatchCategory("Finish");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[LinuxCompat] PatchCategory(\"Finish\") threw: {e}");
            try
            {
                VRage.Utils.MyLog.Default.WriteLineAndConsole(
                    $"[LinuxCompat] PatchCategory(\"Finish\") threw: {e}"
                );
            }
            catch { }
            throw;
        }
        Console.WriteLine(
            $"[LinuxCompat] PatchCategory(\"Finish\") applied {harmony.GetPatchedMethods().Count()} methods"
        );
        try
        {
            VRage.Utils.MyLog.Default.WriteLineAndConsole(
                $"[LinuxCompat] PatchCategory(\"Finish\") applied {harmony.GetPatchedMethods().Count()} methods"
            );
        }
        catch { }
#if MAGNETAR
        // Server Plugin.Init runs after the auto-loaded world's mods compile.
        ClientPlugin.Patches.PathHandling.PathTranslation.Init();
        ClientPlugin.Rewriter.RewriterRegistration.Register();
#endif
    }
}

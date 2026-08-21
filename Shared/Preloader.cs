// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Pulsar and Magnetar discover Preloader in the global namespace.

// ReSharper disable once UnusedType.Global
public static class Preloader
{
    // ReSharper disable once UnusedMember.Global
    public static void Initialize() => ClientPlugin.Compatibility.NativeLibraries.Initialize();

    // ReSharper disable once UnusedMember.Global
    public static IEnumerable<string> TargetDLLs { get; } =
    [
#if MAGNETAR
        "SpaceEngineers.Game.dll",
        "VRage.Dedicated.dll",
        "VRage.Game.dll",
        "VRage.Library.dll",
        "VRage.Platform.Windows.dll",
        "VRage.Steam.dll",
#else
        "SpaceEngineers.Game.dll",
        "VRage.Audio.dll",
        "VRage.Game.dll",
        "VRage.Library.dll",
        "VRage.Platform.Windows.dll",
        "VRage.Steam.dll",
        "SharpDX.dll",
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
        RedirectAssemblyRef(asmDef, "SharpDX.XAudio2");
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
        RedirectAssemblyRef(asmDef, "SharpDX.XAudio2");
    }

    private static void RedirectAssemblyRef(AssemblyDefinition asmDef, string fromName)
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

        var runtimeName = typeof(Preloader).Assembly.GetName();
        asmRef.Name = runtimeName.Name;
        asmRef.Version = runtimeName.Version;
        asmRef.PublicKeyToken = runtimeName.GetPublicKeyToken() ?? Array.Empty<byte>();
        asmRef.PublicKey = runtimeName.GetPublicKey() ?? Array.Empty<byte>();
        asmRef.Culture = runtimeName.CultureName ?? string.Empty;
        asmRef.HashAlgorithm = Mono.Cecil.AssemblyHashAlgorithm.None;

        Console.WriteLine(
            $"[LinuxCompat] Redirected AssemblyRef in {asmDef.Name.Name}: {fromName} -> {runtimeName.FullName}"
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
#if !MAGNETAR
        // Splash creation uses SDL before Plugin.Init.
        if (ClientPlugin.Compatibility.RenderingConfig.AllowRendering)
            ClientPlugin.Compatibility.SdlRenderThread.Start();
#endif

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
        ClientPlugin.Rewriter.ShimRegistration.Register();
#endif
    }
}

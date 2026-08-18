using System;
using System.Linq;
using ClientPlugin.Tools;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ClientPlugin.Patches.PathHandling;

// Explicit interface getters expose Windows paths to mods without changing native engine getters.
public static class MyModContextPrepatch
{
    public static void Prepatch(AssemblyDefinition asmDef)
    {
        if (asmDef.Name.Name != "VRage.Game")
            return;

        var module = asmDef.MainModule;

        var type = module.GetType("VRage.Game.MyModContext");
        if (type == null)
            return;

        var iface = module.GetType("VRage.Game.ModAPI.IMyModContext");
        if (iface == null)
            return;

        var toWindowsPath = ImportToWindowsPath(module);

        InjectExplicitGetter(type, iface, module, "ModPath", toWindowsPath);
        InjectExplicitGetter(type, iface, module, "ModPathData", toWindowsPath);
    }

    private static MethodReference ImportToWindowsPath(ModuleDefinition module)
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

        var pathHelpersType = new TypeReference(
            "ClientPlugin.Patches.PathHandling", "PathHelpers", module, linuxCompatRef, false);

        var stringRef = module.TypeSystem.String;
        var method = new MethodReference("ToWindowsPath", stringRef, pathHelpersType)
        {
            HasThis = false,
            ExplicitThis = false,
            CallingConvention = MethodCallingConvention.Default,
        };
        method.Parameters.Add(new ParameterDefinition(stringRef));
        return method;
    }

    private static void InjectExplicitGetter(
        TypeDefinition type, TypeDefinition iface, ModuleDefinition module, string propName,
        MethodReference toWindowsPath)
    {
        var publicGetter = type.Methods.FirstOrDefault(m => m.Name == "get_" + propName);
        var backingField = type.Fields.FirstOrDefault(f => f.Name == $"<{propName}>k__BackingField");
        var ifaceGetter = iface.Methods.FirstOrDefault(m => m.Name == "get_" + propName);
        if (publicGetter?.Body == null || backingField == null || ifaceGetter == null)
            return;

        // Reject game updates that change the assumed auto-property body.
        publicGetter.Body.Instructions.RecordOriginalCode(publicGetter);
        publicGetter.Body.Instructions.VerifyCodeHash(publicGetter, "172f09b2");

        var stringRef = module.TypeSystem.String;

        var mangled = "VRage.Game.ModAPI.IMyModContext.get_" + propName;

        if (type.Methods.Any(m => m.Name == mangled))
            return;

        var attrs = MethodAttributes.Private
                  | MethodAttributes.Final
                  | MethodAttributes.HideBySig
                  | MethodAttributes.NewSlot
                  | MethodAttributes.Virtual
                  | MethodAttributes.SpecialName;

        var newMethod = new MethodDefinition(mangled, attrs, stringRef)
        {
            HasThis = true,
            IsManaged = true,
            ImplAttributes = MethodImplAttributes.IL | MethodImplAttributes.Managed,
        };

        newMethod.Overrides.Add(module.ImportReference(ifaceGetter));

        var body = new MethodBody(newMethod);
        var il = body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, backingField));
        il.Append(il.Create(OpCodes.Call, toWindowsPath));
        il.Append(il.Create(OpCodes.Ret));

        newMethod.Body = body;
        type.Methods.Add(newMethod);

        newMethod.Body.Instructions.RecordCustomCode(newMethod, "injected");
    }
}

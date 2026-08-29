using System.Linq;
using ClientPlugin.Tools;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ClientPlugin.Patches.PathHandling;

/// <summary>
/// Injects an explicit interface getter returning
/// <see cref="PathHelpers.ToWindowsPath"/> of a string backing field, exposing
/// Windows paths to mods while native engine getters stay unchanged.
/// </summary>
public static class ExplicitInterfaceGetterInjector
{
    public static void Inject(
        TypeDefinition type,
        TypeDefinition iface,
        ModuleDefinition module,
        string propName,
        string backingFieldName,
        string expectedHash
    )
    {
        var publicGetter = type.Methods.FirstOrDefault(m => m.Name == "get_" + propName);
        var backingField = type.Fields.FirstOrDefault(f => f.Name == backingFieldName);
        var ifaceGetter = iface.Methods.FirstOrDefault(m => m.Name == "get_" + propName);
        if (publicGetter?.Body == null || backingField == null || ifaceGetter == null)
            return;

        // Reject game updates that change the assumed getter body.
        publicGetter.Body.Instructions.RecordOriginalCode(publicGetter);
        publicGetter.Body.Instructions.VerifyCodeHash(publicGetter, expectedHash);

        var stringRef = module.TypeSystem.String;

        var mangled = iface.FullName.Replace('/', '.') + ".get_" + propName;

        if (type.Methods.Any(m => m.Name == mangled))
            return;

        var attrs =
            MethodAttributes.Private
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

        var toWindowsPath = module.ImportReference(
            typeof(PathHelpers).GetMethod(nameof(PathHelpers.ToWindowsPath), [typeof(string)])
        );

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

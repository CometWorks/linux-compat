using System;
using System.Linq;
using ClientPlugin.Tools;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ClientPlugin.Patches.PathHandling;

// Topic 4.7 (Mod context Windows-path interface split).
//
// Upstream adds two explicit interface implementations on MyModContext so
// that the public ModPath / ModPathData getters keep returning the native
// (Linux) path for internal callers, while IMyModContext.ModPath /
// IMyModContext.ModPathData — the mod-facing surface — return a
// backslash-separated Windows path:
//
//   public  string ModPath { get; private set; }                 // unchanged
//   string IMyModContext.ModPath => PathHelpers.ToWindowsPath(ModPath);
//
//   public  string ModPathData { get; private set; }             // unchanged
//   string IMyModContext.ModPathData => PathHelpers.ToWindowsPath(ModPathData);
//
// A Harmony Postfix on the public getters is NOT equivalent: the postfix
// fires for both caller sets and corrupts internal Path.Combine / cache-
// key sites with mixed separators. See
// memory/feedback_explicit_interface_impl.md for the incident that killed
// the previous attempt. The correct fix has to synthesize a real explicit
// interface method, which is a preloader (Mono.Cecil) job.
//
// The injected getter calls into ClientPlugin.Patches.PathHandling.
// PathHelpers.ToWindowsPath. That helper carries the full mod-facing path
// transform — separator flip, prefix translation (Linux SE roots →
// synthetic Windows roots via PathTranslation), and C: promotion on miss
// — so MyModContext.ModPath/ModPathData go through exactly the same
// translation as every other mod-facing egress (WrappedGamePaths,
// WindowsPath.FromGame, WindowsPath.GetFullPath).
//
// Cross-assembly call into LinuxCompat requires a new AssemblyRef on the
// VRage.Game module; the AssemblyResolve handler installed in Preloader
// answers that bind at runtime, including the Pulsar dev-folder
// randomized assembly identity.
//
// The public getters are left byte-identical. VerifyCodeHash guards the
// baseline so a future game update that changes the getter body will fail
// fast at preloader time instead of silently breaking the interface split.
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

    // Build a MethodReference to LinuxCompat's PathHelpers.ToWindowsPath
    // (static, one string parameter, string return). Adds an AssemblyRef to
    // LinuxCompat on the VRage.Game module if one isn't already present.
    // PublicKeyToken is empty — matches how RedirectAssemblyRef rewires
    // SharpDX.XAudio2 → LinuxCompat, and the AssemblyResolve handler in
    // Preloader.cs answers by simple name regardless of token.
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

        // Record the public getter and pin its IL baseline. If the game's
        // getter body ever deviates from the auto-property pattern, we want
        // preloader to throw rather than silently let a bad MethodImpl ride.
        publicGetter.Body.Instructions.RecordOriginalCode(publicGetter);
        publicGetter.Body.Instructions.VerifyCodeHash(publicGetter, "172f09b2");

        var stringRef = module.TypeSystem.String;

        // Mangled name mirrors the C# compiler's output for an explicit
        // interface implementation.
        var mangled = "VRage.Game.ModAPI.IMyModContext.get_" + propName;

        // Don't double-inject if the prepatch runs twice somehow.
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

        // MethodImpl override: this method satisfies IMyModContext.get_<Prop>().
        newMethod.Overrides.Add(module.ImportReference(ifaceGetter));

        // Body, equivalent C#:
        //   return ClientPlugin.Patches.PathHandling.PathHelpers
        //              .ToWindowsPath(this.<Prop>k__BackingField);
        // ToWindowsPath null-checks internally, so the null branch is gone.
        var body = new MethodBody(newMethod);
        var il = body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, backingField));
        il.Append(il.Create(OpCodes.Call, toWindowsPath));
        il.Append(il.Create(OpCodes.Ret));

        newMethod.Body = body;
        type.Methods.Add(newMethod);

        // Record the injected IL next to this file, with a custom suffix so
        // it doesn't collide with the public getter's `original`.
        newMethod.Body.Instructions.RecordCustomCode(newMethod, "injected");
    }
}

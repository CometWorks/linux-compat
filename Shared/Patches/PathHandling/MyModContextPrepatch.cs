using Mono.Cecil;

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

        ExplicitInterfaceGetterInjector.Inject(
            type,
            iface,
            module,
            "ModPath",
            "<ModPath>k__BackingField",
            "172f09b2"
        );
        ExplicitInterfaceGetterInjector.Inject(
            type,
            iface,
            module,
            "ModPathData",
            "<ModPathData>k__BackingField",
            "172f09b2"
        );
    }
}

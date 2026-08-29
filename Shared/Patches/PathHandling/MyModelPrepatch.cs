using Mono.Cecil;

namespace ClientPlugin.Patches.PathHandling;

// Mods reading IMyModel.AssetName get the Windows shape; engine model loading stays native.
public static class MyModelPrepatch
{
    public static void Prepatch(AssemblyDefinition asmDef)
    {
        if (asmDef.Name.Name != "VRage.Game")
            return;

        var module = asmDef.MainModule;

        var type = module.GetType("VRage.Game.Models.MyModel");
        if (type == null)
            return;

        var iface = module.GetType("VRage.Game.ModAPI.IMyModel");
        if (iface == null)
            return;

        ExplicitInterfaceGetterInjector.Inject(
            type,
            iface,
            module,
            "AssetName",
            "m_assetName",
            "172f09b2"
        );
    }
}

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ClientPlugin.Patches.UIDisplay;

// Append Linux after DotNetCompat's runtime suffix. Resolve its helper by
// reflection because ClientPlugin does not reference DotNetCompat.dll.
[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
static class DotNetCompatAppendFrameworkDescriptionPatch
{
    private const string TypeName = "ClientPlugin.Patches.Miscellaneous.MyGuiScreenMainMenuBasePatch";
    private const string MethodName = "AppendFrameworkDescription";

    static MethodBase TargetMethod()
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a =>
            {
                try { return a.GetType(TypeName, throwOnError: false); }
                catch { return null; }
            })
            .FirstOrDefault(t => t != null);

        return type?.GetMethod(MethodName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(string) },
            modifiers: null);
    }

    static bool Prepare() => TargetMethod() != null;

    static void Postfix(ref string __result)
    {
        if (string.IsNullOrEmpty(__result))
            return;
        __result = __result + " on Linux";
    }
}

using System;
using System.Linq;
using ClientPlugin.Patches.NullSafety;
using ClientPlugin.Tools;
using Mono.Cecil;

namespace ClientPlugin.Patches.Audio;

// Concurrent audio shutdown or wave disposal can invalidate GetVoice state. Treat a
// NullReferenceException as an unavailable voice, matching its entry guard. Written into the
// method body, because a lost Harmony finalizer here crashes the game on a sound (SE #1073).
public static class MyCueBankGetVoicePrepatch
{
    public static void Prepatch(AssemblyDefinition asmDef)
    {
        var module = asmDef.MainModule;
        // GetVoice(MyCueId, out int, MySoundDimensions, int, MyVoicePoolType)
        var method = module
            .GetType("VRage.Audio.MyCueBank")
            ?.Methods.FirstOrDefault(m => m.Name == "GetVoice" && m.Parameters.Count == 5);
        if (method?.Body == null)
        {
            Console.WriteLine(
                "[LinuxCompat] MyCueBankGetVoicePrepatch: MyCueBank.GetVoice not found"
            );
            return;
        }

        method.Body.Instructions.RecordOriginalCode(method);
        var filter = module.ImportReference(
            typeof(NullSafetyPrepatch).GetMethod(nameof(NullSafetyPrepatch.NullReference))
        );
        PreloaderHelpers.WrapInExceptionFilter(method, filter);
        method.Body.Instructions.RecordPatchedCode(method);
        Console.WriteLine("[LinuxCompat] MyCueBankGetVoicePrepatch: guarded MyCueBank.GetVoice");
    }
}

using System;
using System.Linq;
using ClientPlugin.Patches.PathHandling;
using ClientPlugin.Tools;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ClientPlugin.Patches.Audio;

// Cue definitions contain Windows separators and occasional trailing whitespace, so
// FindAudioFile normalizes its fileName first. Written into the method body: as a Harmony
// prefix it was lost on a player's machine during a world load, and every sound went
// missing (SE #1073).
public static class MyWaveBankFindAudioFilePrepatch
{
    public static void Prepatch(AssemblyDefinition asmDef)
    {
        var module = asmDef.MainModule;
        // static bool FindAudioFile(MySoundData cue, string fileName, out string fsPath)
        var method = module
            .GetType("VRage.Audio.MyWaveBank")
            ?.Methods.FirstOrDefault(m =>
                m.Name == "FindAudioFile"
                && m.IsStatic
                && m.Parameters.Count == 3
                && m.Parameters[1].ParameterType.FullName == "System.String"
            );
        if (method?.Body == null)
        {
            Console.WriteLine(
                "[LinuxCompat] MyWaveBankFindAudioFilePrepatch: MyWaveBank.FindAudioFile not found"
            );
            return;
        }

        method.Body.Instructions.RecordOriginalCode(method);
        var normalize = module.ImportReference(
            typeof(PathHelpers).GetMethod(nameof(PathHelpers.Normalize), [typeof(string)])
        );
        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions[0];
        var fileName = method.Parameters[1];
        il.InsertBefore(first, il.Create(OpCodes.Ldarg, fileName));
        il.InsertBefore(first, il.Create(OpCodes.Call, normalize));
        il.InsertBefore(first, il.Create(OpCodes.Starg, fileName));
        method.Body.Instructions.RecordPatchedCode(method);
        Console.WriteLine(
            "[LinuxCompat] MyWaveBankFindAudioFilePrepatch: normalizing the file name in MyWaveBank.FindAudioFile"
        );
    }
}

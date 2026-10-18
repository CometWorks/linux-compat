using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ClientPlugin.Patches.PathHandling;
using ClientPlugin.Tools;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using VRage.Audio;
using VRage.Data.Audio;

namespace ClientPlugin.Patches.Audio;

// Decode Linux audio into the shim's managed AudioBuffer.Data field. Reflection
// targets the runtime type after the SharpDX.XAudio2 AssemblyRef redirect.
[HarmonyPatch(typeof(MyInMemoryWave), nameof(MyInMemoryWave.Dispose))]
[HarmonyPatchCategory("Finish")]
static class MyInMemoryWaveDisposePatch
{
    static bool Prefix(MyInMemoryWave __instance)
    {
        // m_owner is null after the first disposal.
        if (__instance.m_owner == null)
            return false;

        // Preserve field cleanup and streamed-wave removal.
        return true;
    }
}

// Finish-category ordering installs this before MyAudio.LoadData.
[HarmonyPatch(
    typeof(MyInMemoryWave),
    MethodType.Constructor,
    new[] { typeof(MySoundData), typeof(string), typeof(MyWaveBank), typeof(bool), typeof(bool) }
)]
[HarmonyPatchCategory("Finish")]
static class MyInMemoryWaveCtorPatch
{
    // Runtime AudioBuffer fields after AssemblyRef redirection.
    private static Type s_audioBufferType;
    private static FieldInfo s_audioBytesField;
    private static FieldInfo s_flagsField;
    private static FieldInfo s_dataField;
    private static FieldInfo s_loopCountField;

    private static FieldInfo s_bufferField;

    static bool Prefix(
        MyInMemoryWave __instance,
        MySoundData cue,
        string path,
        MyWaveBank owner,
        bool streamed,
        bool cached
    )
    {
        // Direct File.Exists requires Linux separators and on-disk casing.
        path = path.Replace('\\', '/');
        if (Path.IsPathRooted(path))
            path = PathCache.ResolveAbsolute(path);

        byte[] data = MySdlAudioInterop.LoadAudioFile(path, out var waveFormat);

        if (s_audioBufferType == null)
        {
            s_bufferField =
                AccessTools.Field(typeof(MyInMemoryWave), "m_buffer")
                ?? throw new InvalidOperationException("MyInMemoryWave.m_buffer not found");
            s_audioBufferType = s_bufferField.FieldType;
            s_audioBytesField =
                s_audioBufferType.GetField("AudioBytes")
                ?? throw new InvalidOperationException("AudioBuffer.AudioBytes not found");
            s_flagsField =
                s_audioBufferType.GetField("Flags")
                ?? throw new InvalidOperationException("AudioBuffer.Flags not found");
            s_dataField =
                s_audioBufferType.GetField("Data")
                ?? throw new InvalidOperationException(
                    "AudioBuffer.Data not found (shim not active?)"
                );
            s_loopCountField =
                s_audioBufferType.GetField("LoopCount")
                ?? throw new InvalidOperationException("AudioBuffer.LoopCount not found");
        }

        var buffer = Activator.CreateInstance(s_audioBufferType);
        s_audioBytesField.SetValue(buffer, data.Length);
        s_flagsField.SetValue(buffer, 0); // BufferFlags.None
        s_dataField.SetValue(buffer, data);
        if (cue.Loopable)
        {
            s_loopCountField.SetValue(buffer, 255);
        }

        __instance.m_owner = owner;
        __instance.m_path = path;
        __instance.m_waveFormat = waveFormat;
        s_bufferField.SetValue(__instance, buffer);
        __instance.Streamed = streamed;
        return false;
    }
}

// The shim leaves m_stream null and ignores WMA DecodedPacketsInfo, so
// SubmitSourceBuffer must not read wave.Stream.DecodedPacketsInfo. Replace that
// callvirt with `pop; ldnull` in the assembly itself. A Harmony transpiler did this
// before, but players still ended up running the original body, and then every
// sound threw a NullReferenceException (SpaceGT/Pulsar#70).
public static class MySourceVoiceSubmitSourceBufferPrepatch
{
    public static void Prepatch(AssemblyDefinition asmDef)
    {
        var method = asmDef
            .MainModule.GetType("VRage.Audio.MySourceVoice")
            ?.Methods.FirstOrDefault(m =>
                m.Name == "SubmitSourceBuffer"
                && m.Parameters.Count == 1
                && m.Parameters[0].ParameterType.FullName == "VRage.Audio.MyInMemoryWave"
            );
        if (method?.Body == null)
        {
            Console.WriteLine(
                "[LinuxCompat] MySourceVoiceSubmitSourceBufferPrepatch: MySourceVoice.SubmitSourceBuffer(MyInMemoryWave) not found"
            );
            return;
        }

        method.Body.Instructions.RecordOriginalCode(method);

        var il = method.Body.GetILProcessor();
        var replaced = 0;
        foreach (var instr in method.Body.Instructions.ToList())
        {
            if (
                instr.OpCode != OpCodes.Callvirt
                || instr.Operand is not MethodReference { Name: "get_DecodedPacketsInfo" } mr
                || mr.DeclaringType.FullName != "SharpDX.Multimedia.SoundStream"
            )
                continue;

            // Reuse the instruction so branch targets stay attached.
            instr.OpCode = OpCodes.Pop;
            instr.Operand = null;
            il.InsertAfter(instr, il.Create(OpCodes.Ldnull));
            replaced++;
        }

        method.Body.Instructions.RecordPatchedCode(method);

        Console.WriteLine(
            $"[LinuxCompat] MySourceVoiceSubmitSourceBufferPrepatch: replaced {replaced} DecodedPacketsInfo read(s) in MySourceVoice.SubmitSourceBuffer"
        );
    }
}

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

// Decode Linux audio into the shim's managed AudioBuffer.Data field instead of the game's
// SharpDX decoder, and make a second Dispose a no-op. Both are written into the method bodies:
// as Harmony prefixes they can be lost at runtime, and the world goes silent (SE #1073).
public static class MyInMemoryWavePrepatch
{
    public static void Prepatch(AssemblyDefinition asmDef)
    {
        var type = asmDef.MainModule.GetType("VRage.Audio.MyInMemoryWave");
        // MyInMemoryWave(MySoundData cue, string path, MyWaveBank owner, bool streamed, bool cached)
        var ctor = type?.Methods.FirstOrDefault(m =>
            m.IsConstructor && !m.IsStatic && m.Parameters.Count == 5
        );
        var dispose = type?.Methods.FirstOrDefault(m => m.Name == "Dispose" && !m.HasParameters);
        var owner = type?.Fields.FirstOrDefault(f => f.Name == "m_owner");
        if (ctor?.Body == null || dispose?.Body == null || owner == null)
        {
            Console.WriteLine(
                "[LinuxCompat] MyInMemoryWavePrepatch: MyInMemoryWave constructor, Dispose or m_owner not found"
            );
            return;
        }

        // The constructor body becomes MyInMemoryWaveLinux.Init(this, cue, path, owner, streamed, cached).
        ctor.Body.Instructions.RecordOriginalCode(ctor);
        ctor.Body.Instructions.Clear();
        ctor.Body.ExceptionHandlers.Clear();
        ctor.Body.Variables.Clear();
        var il = ctor.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldarg_0));
        foreach (var parameter in ctor.Parameters)
            il.Append(il.Create(OpCodes.Ldarg, parameter));
        il.Append(
            il.Create(
                OpCodes.Call,
                asmDef.MainModule.ImportReference(
                    typeof(MyInMemoryWaveLinux).GetMethod(nameof(MyInMemoryWaveLinux.Init))
                )
            )
        );
        il.Append(il.Create(OpCodes.Ret));
        ctor.Body.Instructions.RecordPatchedCode(ctor);

        // Dispose returns at once when m_owner is null, that is, after the first disposal.
        dispose.Body.Instructions.RecordOriginalCode(dispose);
        il = dispose.Body.GetILProcessor();
        var first = dispose.Body.Instructions[0];
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Ldfld, owner));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
        dispose.Body.Instructions.RecordPatchedCode(dispose);

        Console.WriteLine(
            "[LinuxCompat] MyInMemoryWavePrepatch: replaced the MyInMemoryWave constructor, guarded Dispose"
        );
    }
}

public static class MyInMemoryWaveLinux
{
    // Runtime AudioBuffer fields after AssemblyRef redirection.
    private static Type s_audioBufferType;
    private static FieldInfo s_audioBytesField;
    private static FieldInfo s_flagsField;
    private static FieldInfo s_dataField;
    private static FieldInfo s_loopCountField;

    private static FieldInfo s_bufferField;

    // Called from the constructor body. The parameters take no game types: importing this method
    // into VRage.Audio during the preload must not load the game assemblies.
    public static void Init(
        object instance,
        object cue,
        string path,
        object owner,
        bool streamed,
        bool cached
    )
    {
        var wave = (MyInMemoryWave)instance;

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
        if (((MySoundData)cue).Loopable)
        {
            s_loopCountField.SetValue(buffer, 255);
        }

        wave.m_owner = (MyWaveBank)owner;
        wave.m_path = path;
        wave.m_waveFormat = waveFormat;
        s_bufferField.SetValue(wave, buffer);
        wave.Streamed = streamed;
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

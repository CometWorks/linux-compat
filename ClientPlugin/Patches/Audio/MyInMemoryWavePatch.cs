using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ClientPlugin.Patches.PathHandling;
using ClientPlugin.Tools;
using HarmonyLib;
using SharpDX.Multimedia;
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

// The shim leaves m_stream null and ignores WMA DecodedPacketsInfo. Replace its
// callvirt with `pop; ldnull` to preserve the expected stack signature.
[HarmonyPatch(typeof(MySourceVoice))]
[HarmonyPatchCategory("Finish")]
// ReSharper disable once UnusedType.Global
static class MySourceVoiceSubmitBufferTranspiler
{
    // ReSharper disable once UnusedMember.Local
    [HarmonyTranspiler]
    [HarmonyPatch("SubmitSourceBuffer", new[] { typeof(MyInMemoryWave) })]
    static IEnumerable<CodeInstruction> SubmitSourceBufferTranspiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase patchedMethod
    )
    {
        var il = instructions.ToList();
        il.RecordOriginalCode(patchedMethod);

        var getDecodedPackets = typeof(SoundStream)
            .GetProperty("DecodedPacketsInfo", BindingFlags.Public | BindingFlags.Instance)
            ?.GetGetMethod();

        // Walk backwards so Insert calls don't shift the indexes still to visit.
        for (var i = il.Count - 1; i >= 0 && getDecodedPackets != null; i--)
        {
            var instr = il[i];
            if (instr.opcode != OpCodes.Callvirt)
                continue;
            if (instr.operand is not MethodInfo mi || mi != getDecodedPackets)
                continue;

            // Reuse the instruction so labels and exception blocks stay attached.
            instr.opcode = OpCodes.Pop;
            instr.operand = null;
            il.Insert(i + 1, new CodeInstruction(OpCodes.Ldnull));
        }

        il.RecordPatchedCode(patchedMethod);
        return il;
    }
}

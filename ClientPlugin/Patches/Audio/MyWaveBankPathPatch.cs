using ClientPlugin.Patches.PathHandling;
using HarmonyLib;
using VRage.Audio;

namespace ClientPlugin.Patches.Audio;

// Cue definitions contain Windows separators and occasional trailing whitespace.
[HarmonyPatch(typeof(MyWaveBank), "FindAudioFile")]
[HarmonyPatchCategory("Finish")]
static class MyWaveBankFindAudioFilePathPatch
{
    static void Prefix(ref string fileName)
    {
        if (fileName != null)
            fileName = PathHelpers.Normalize(fileName);
    }
}

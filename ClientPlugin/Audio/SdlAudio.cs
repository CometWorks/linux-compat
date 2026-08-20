using System;
using System.Runtime.InteropServices;
using VRage.Audio;

namespace ClientPlugin.Audio;

/// <summary>
/// Thread-safe SDL3 audio bindings. SDL stream and device functions provide
/// their own locking and may be called without render-thread marshalling.
/// </summary>
internal static class SdlAudio
{
    private const string Lib = "libSDL3.so";

    internal const uint SDL_INIT_AUDIO = 0x10u;

    internal static bool InitSubSystem(uint flags)
    {
        return SDL_InitSubSystem(flags);
    }

    internal static IntPtr GetError()
    {
        return SDL_GetError();
    }

    internal static string GetErrorString()
    {
        IntPtr ptr = SDL_GetError();
        return ptr == IntPtr.Zero ? "unknown error" : Marshal.PtrToStringAnsi(ptr);
    }

    internal static bool LoadWav(string path, ref MySdlAudioInterop.SdlAudioSpec spec, out IntPtr audioBuffer, out uint audioLength)
    {
        return SDL_LoadWAV(path, ref spec, out audioBuffer, out audioLength);
    }

    internal static void Free(IntPtr memory)
    {
        SDL_free(memory);
    }

    internal static uint OpenAudioDevice(uint devid, ref MySdlAudioInterop.SdlAudioSpec spec)
    {
        return SDL_OpenAudioDevice(devid, ref spec);
    }

    internal static void CloseAudioDevice(uint devid)
    {
        SDL_CloseAudioDevice(devid);
    }

    internal static bool PauseAudioDevice(uint devid)
    {
        return SDL_PauseAudioDevice(devid);
    }

    internal static bool GetAudioDeviceFormat(uint devid, out MySdlAudioInterop.SdlAudioSpec spec, out int sampleFrames)
    {
        return SDL_GetAudioDeviceFormat(devid, out spec, out sampleFrames);
    }

    internal static IntPtr CreateAudioStream(ref MySdlAudioInterop.SdlAudioSpec src, ref MySdlAudioInterop.SdlAudioSpec dst)
    {
        return SDL_CreateAudioStream(ref src, ref dst);
    }

    internal static void DestroyAudioStream(IntPtr stream)
    {
        SDL_DestroyAudioStream(stream);
    }

    internal static bool BindAudioStream(uint devid, IntPtr stream)
    {
        return SDL_BindAudioStream(devid, stream);
    }

    internal static void UnbindAudioStream(IntPtr stream)
    {
        SDL_UnbindAudioStream(stream);
    }

    internal static bool PutAudioStreamData(IntPtr stream, byte[] data, int len)
    {
        return SDL_PutAudioStreamData(stream, data, len);
    }

    internal static int GetAudioStreamQueued(IntPtr stream)
    {
        return SDL_GetAudioStreamQueued(stream);
    }

    internal static bool ClearAudioStream(IntPtr stream)
    {
        return SDL_ClearAudioStream(stream);
    }

    internal static bool SetAudioStreamGain(IntPtr stream, float gain)
    {
        return SDL_SetAudioStreamGain(stream, gain);
    }

    internal static bool SetAudioStreamFrequencyRatio(IntPtr stream, float ratio)
    {
        return SDL_SetAudioStreamFrequencyRatio(stream, ratio);
    }

    // SDL3 _Bool is one byte; the CLR's default unmanaged BOOL is four bytes.

    [DllImport(Lib, EntryPoint = "SDL_InitSubSystem")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_InitSubSystem(uint flags);

    [DllImport(Lib, EntryPoint = "SDL_GetError")]
    private static extern IntPtr SDL_GetError();

    [DllImport(Lib, EntryPoint = "SDL_LoadWAV", CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_LoadWAV(string path, ref MySdlAudioInterop.SdlAudioSpec spec, out IntPtr audioBuffer, out uint audioLength);

    [DllImport(Lib, EntryPoint = "SDL_free")]
    private static extern void SDL_free(IntPtr memory);

    [DllImport(Lib, EntryPoint = "SDL_OpenAudioDevice")]
    private static extern uint SDL_OpenAudioDevice(uint devid, ref MySdlAudioInterop.SdlAudioSpec spec);

    [DllImport(Lib, EntryPoint = "SDL_CloseAudioDevice")]
    private static extern void SDL_CloseAudioDevice(uint devid);

    [DllImport(Lib, EntryPoint = "SDL_PauseAudioDevice")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_PauseAudioDevice(uint devid);

    [DllImport(Lib, EntryPoint = "SDL_GetAudioDeviceFormat")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetAudioDeviceFormat(uint devid, out MySdlAudioInterop.SdlAudioSpec spec, out int sampleFrames);

    [DllImport(Lib, EntryPoint = "SDL_CreateAudioStream")]
    private static extern IntPtr SDL_CreateAudioStream(ref MySdlAudioInterop.SdlAudioSpec src, ref MySdlAudioInterop.SdlAudioSpec dst);

    [DllImport(Lib, EntryPoint = "SDL_DestroyAudioStream")]
    private static extern void SDL_DestroyAudioStream(IntPtr stream);

    [DllImport(Lib, EntryPoint = "SDL_BindAudioStream")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_BindAudioStream(uint devid, IntPtr stream);

    [DllImport(Lib, EntryPoint = "SDL_UnbindAudioStream")]
    private static extern void SDL_UnbindAudioStream(IntPtr stream);

    [DllImport(Lib, EntryPoint = "SDL_PutAudioStreamData")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_PutAudioStreamData(IntPtr stream, byte[] data, int len);

    [DllImport(Lib, EntryPoint = "SDL_GetAudioStreamQueued")]
    private static extern int SDL_GetAudioStreamQueued(IntPtr stream);

    [DllImport(Lib, EntryPoint = "SDL_ClearAudioStream")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_ClearAudioStream(IntPtr stream);

    [DllImport(Lib, EntryPoint = "SDL_SetAudioStreamGain")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetAudioStreamGain(IntPtr stream, float gain);

    [DllImport(Lib, EntryPoint = "SDL_SetAudioStreamFrequencyRatio")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetAudioStreamFrequencyRatio(IntPtr stream, float ratio);
}

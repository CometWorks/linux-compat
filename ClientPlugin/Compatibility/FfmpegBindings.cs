using System;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace ClientPlugin.Compatibility;

internal static class FfmpegBindings
{
    static FfmpegBindings()
    {
        ffmpeg.RootPath = string.Empty;
        PinLibraryVersions();

        // FFmpeg.AutoGen 8.1 is tied to the bundled FFmpeg 8.1 ABI. Load that pinned
        // build from the plugin directory rather than whatever FFmpeg the system provides.
        string pluginDirectory = Path.GetDirectoryName(typeof(FfmpegBindings).Assembly.Location)!;
        _ = NativeLibrary.Load(Path.Combine(pluginDirectory, "libavformat.so"));
        _ = NativeLibrary.Load(Path.Combine(pluginDirectory, "libswscale.so"));

        DynamicallyLoadedBindings.Initialize();
        ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
        _ = ffmpeg.avformat_version();
    }

    internal static void EnsureInitialized() { }

    private static void PinLibraryVersions()
    {
        ffmpeg.LibraryVersionMap["avcodec"] = 62;
        ffmpeg.LibraryVersionMap["avformat"] = 62;
        ffmpeg.LibraryVersionMap["avutil"] = 60;
        ffmpeg.LibraryVersionMap["swresample"] = 6;
        ffmpeg.LibraryVersionMap["swscale"] = 9;
    }
}

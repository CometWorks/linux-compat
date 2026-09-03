using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ClientPlugin.Audio;
using ClientPlugin.Compatibility;
using FFmpeg.AutoGen;
using SharpDX.Multimedia;
using VRage.FileSystem;
using VRage.Utils;

namespace VRage.Audio;

internal static unsafe class MySdlAudioInterop
{
    internal const uint SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK = 0xFFFFFFFFu;

    internal const ushort SDL_AUDIO_U8 = 0x0008;

    internal const ushort SDL_AUDIO_S16LE = 0x8010;

    internal const ushort SDL_AUDIO_S32LE = 0x8020;

    internal const ushort SDL_AUDIO_F32LE = 0x8120;

    private static bool m_initialized;

    private static readonly object InitLock = new object();

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdlAudioSpec
    {
        public ushort Format;

        public int Channels;

        public int Frequency;
    }

    public static void EnsureInitialized()
    {
        if (m_initialized)
        {
            return;
        }
        lock (InitLock)
        {
            if (m_initialized)
            {
                return;
            }
            bool ok = SdlAudio.InitSubSystem(SdlAudio.SDL_INIT_AUDIO);
            if (!ok)
            {
                throw new PlatformNotSupportedException(
                    $"SDL3 audio initialization failed: {GetErrorString()}"
                );
            }
            FfmpegBindings.EnsureInitialized();
            m_initialized = true;
        }
    }

    public static byte[] LoadAudioFile(string path, out WaveFormat waveFormat)
    {
        EnsureInitialized();
        if (!File.Exists(path))
        {
            // Mod audio can sit inside a _legacy.bin workshop archive, where the
            // path has no filesystem representation; only the game's virtual
            // filesystem (MyZipFileProvider) can open such members.
            byte[] virtualData = TryReadVirtualFile(path);
            if (virtualData == null)
            {
                throw new FileNotFoundException("Audio file was not found.", path);
            }
            byte[] decoded = MyFfmpegAudioInterop.LoadAudioBytes(
                virtualData,
                path,
                out waveFormat
            );
            MyLog.Default?.WriteLine(
                $"[LinuxCompat] Loaded audio through the virtual file system: '{path}' "
                    + $"({virtualData.Length} bytes -> {decoded.Length} PCM bytes)"
            );
            return decoded;
        }
        string extension = Path.GetExtension(path);
        // Some mods ship xWMA data in files named .wav, which SDL_LoadWAV rejects
        // outright while Windows XAudio2 plays it. Take the SDL fast path only for
        // real RIFF/WAVE containers and let FFmpeg decode the rest, as it already
        // does for every other extension.
        if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) && IsRiffWave(path))
        {
            return LoadWavFile(path, out waveFormat);
        }
        return MyFfmpegAudioInterop.LoadAudioFile(path, out waveFormat);
    }

    private static byte[] TryReadVirtualFile(string path)
    {
        using Stream stream = MyFileSystem.OpenRead(path);
        if (stream == null)
        {
            return null;
        }
        using MemoryStream buffer = new MemoryStream(
            stream.CanSeek ? checked((int)stream.Length) : 0
        );
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static bool IsRiffWave(string path)
    {
        byte[] header = new byte[12];
        try
        {
            using FileStream stream = File.OpenRead(path);
            int read = 0;
            while (read < header.Length)
            {
                int count = stream.Read(header, read, header.Length - read);
                if (count <= 0)
                {
                    return false;
                }
                read += count;
            }
        }
        catch (IOException)
        {
            // Let the decoder below fail with the actionable error instead.
            return false;
        }
        return header[0] == (byte)'R'
            && header[1] == (byte)'I'
            && header[2] == (byte)'F'
            && header[3] == (byte)'F'
            && header[8] == (byte)'W'
            && header[9] == (byte)'A'
            && header[10] == (byte)'V'
            && header[11] == (byte)'E';
    }

    private static byte[] LoadWavFile(string path, out WaveFormat waveFormat)
    {
        SdlAudioSpec spec = default;
        if (!SdlAudio.LoadWav(path, ref spec, out IntPtr audioBuffer, out uint audioLength))
        {
            throw new InvalidOperationException(
                $"SDL3 failed to load wav '{path}': {SdlAudio.GetErrorString()}"
            );
        }
        byte[] data;
        try
        {
            data = new byte[audioLength];
            if (audioLength > 0)
            {
                Marshal.Copy(audioBuffer, data, 0, checked((int)audioLength));
            }
        }
        finally
        {
            SdlAudio.Free(audioBuffer);
        }

        // SDL_LoadWAV expands 24-bit PCM to S32LE, while SharpDX interprets
        // 32-bit WaveFormat samples as float. Convert to S16 before tagging them.
        if (spec.Format == SDL_AUDIO_S32LE)
        {
            data = ConvertS32LeToS16Le(data);
            spec.Format = SDL_AUDIO_S16LE;
        }

        waveFormat = CreateWaveFormat(spec);
        return data;
    }

    private static byte[] ConvertS32LeToS16Le(byte[] s32)
    {
        int sampleCount = s32.Length / 4;
        byte[] s16 = new byte[sampleCount * 2];
        for (int i = 0; i < sampleCount; i++)
        {
            // Keep the high 16 bits; SDL's expanded 24-bit source has a zero low byte.
            int sample =
                s32[i * 4]
                | (s32[i * 4 + 1] << 8)
                | (s32[i * 4 + 2] << 16)
                | (s32[i * 4 + 3] << 24);
            short truncated = (short)(sample >> 16);
            s16[i * 2] = (byte)(truncated & 0xFF);
            s16[i * 2 + 1] = (byte)((truncated >> 8) & 0xFF);
        }
        return s16;
    }

    public static SdlAudioSpec CreateSdlSpec(WaveFormat waveFormat)
    {
        return new SdlAudioSpec
        {
            Format = GetFormat(waveFormat),
            Channels = waveFormat.Channels,
            Frequency = waveFormat.SampleRate,
        };
    }

    public static WaveFormat CreateWaveFormat(SdlAudioSpec spec)
    {
        return spec.Format switch
        {
            SDL_AUDIO_F32LE => WaveFormat.CreateIeeeFloatWaveFormat(spec.Frequency, spec.Channels),
            SDL_AUDIO_S32LE => new WaveFormat(spec.Frequency, 32, spec.Channels),
            SDL_AUDIO_S16LE => new WaveFormat(spec.Frequency, 16, spec.Channels),
            SDL_AUDIO_U8 => new WaveFormat(spec.Frequency, 8, spec.Channels),
            _ => new WaveFormat(spec.Frequency, 16, spec.Channels),
        };
    }

    public static Speakers GetSpeakerMask(int channels)
    {
        return channels switch
        {
            1 => Speakers.FrontCenter,
            2 => Speakers.FrontLeft | Speakers.FrontRight,
            4 => Speakers.FrontLeft | Speakers.FrontRight | Speakers.BackLeft | Speakers.BackRight,
            6 => Speakers.FrontLeft
                | Speakers.FrontRight
                | Speakers.FrontCenter
                | Speakers.LowFrequency
                | Speakers.BackLeft
                | Speakers.BackRight,
            8 => Speakers.FrontLeft
                | Speakers.FrontRight
                | Speakers.FrontCenter
                | Speakers.LowFrequency
                | Speakers.BackLeft
                | Speakers.BackRight
                | Speakers.SideLeft
                | Speakers.SideRight,
            _ => Speakers.FrontLeft | Speakers.FrontRight,
        };
    }

    private static ushort GetFormat(WaveFormat waveFormat)
    {
        if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            return SDL_AUDIO_F32LE;
        }
        return waveFormat.BitsPerSample switch
        {
            8 => SDL_AUDIO_U8,
            32 => SDL_AUDIO_S32LE,
            _ => SDL_AUDIO_S16LE,
        };
    }

    public static string GetErrorString()
    {
        return SdlAudio.GetErrorString();
    }
}

internal static unsafe class MyFfmpegAudioInterop
{
    private static readonly int Eagain = ffmpeg.AVERROR(11);

    private static readonly int Einval = ffmpeg.AVERROR(22);

    private const int AvioBufferSize = 65536;

    // FFmpeg holds raw function pointers to these delegates for the lifetime of
    // the AVIOContext; the static references keep the GC from collecting them.
    private static readonly avio_alloc_context_read_packet ReadPacketCallback = ReadPacket;

    private static readonly avio_alloc_context_seek SeekCallback = Seek;

    private sealed class MemoryReaderState
    {
        public byte[] Data;

        public int Position;
    }

    public static byte[] LoadAudioFile(string path, out WaveFormat waveFormat)
    {
        AVFormatContext* formatContext = null;
        ThrowIfError(
            ffmpeg.avformat_open_input(&formatContext, path, null, null),
            $"open audio '{path}'"
        );
        try
        {
            return DecodeOpenedInput(formatContext, path, out waveFormat);
        }
        finally
        {
            ffmpeg.avformat_close_input(&formatContext);
        }
    }

    public static byte[] LoadAudioBytes(byte[] data, string path, out WaveFormat waveFormat)
    {
        FfmpegBindings.EnsureInitialized();
        var state = new MemoryReaderState { Data = data };
        var stateHandle = GCHandle.Alloc(state);
        AVIOContext* avio = null;
        try
        {
            byte* avioBuffer = (byte*)ffmpeg.av_malloc(AvioBufferSize);
            if (avioBuffer == null)
            {
                throw new InvalidOperationException(
                    $"FFmpeg failed to allocate an I/O buffer for '{path}'."
                );
            }
            avio = ffmpeg.avio_alloc_context(
                avioBuffer,
                AvioBufferSize,
                0,
                (void*)GCHandle.ToIntPtr(stateHandle),
                ReadPacketCallback,
                null,
                SeekCallback
            );
            if (avio == null)
            {
                ffmpeg.av_free(avioBuffer);
                throw new InvalidOperationException(
                    $"FFmpeg failed to allocate an I/O context for '{path}'."
                );
            }
            AVFormatContext* formatContext = ffmpeg.avformat_alloc_context();
            if (formatContext == null)
            {
                throw new InvalidOperationException(
                    $"FFmpeg failed to allocate a format context for '{path}'."
                );
            }
            formatContext->pb = avio;
            // The caller owns a custom AVIOContext; keep close_input off it.
            formatContext->flags |= ffmpeg.AVFMT_FLAG_CUSTOM_IO;
            // On failure avformat_open_input frees the format context itself.
            ThrowIfError(
                ffmpeg.avformat_open_input(&formatContext, path, null, null),
                $"open in-memory audio '{path}'"
            );
            try
            {
                return DecodeOpenedInput(formatContext, path, out waveFormat);
            }
            finally
            {
                ffmpeg.avformat_close_input(&formatContext);
            }
        }
        finally
        {
            if (avio != null)
            {
                // FFmpeg may have replaced the buffer allocated above.
                ffmpeg.av_freep(&avio->buffer);
                ffmpeg.avio_context_free(&avio);
            }
            stateHandle.Free();
        }
    }

    private static int ReadPacket(void* opaque, byte* buffer, int bufferSize)
    {
        try
        {
            var state = (MemoryReaderState)GCHandle.FromIntPtr((IntPtr)opaque).Target;
            int remaining = state.Data.Length - state.Position;
            if (remaining <= 0)
            {
                return ffmpeg.AVERROR_EOF;
            }
            int count = Math.Min(remaining, bufferSize);
            Marshal.Copy(state.Data, state.Position, (IntPtr)buffer, count);
            state.Position += count;
            return count;
        }
        catch
        {
            // Exceptions must not unwind into native FFmpeg frames.
            return Einval;
        }
    }

    private static long Seek(void* opaque, long offset, int whence)
    {
        try
        {
            var state = (MemoryReaderState)GCHandle.FromIntPtr((IntPtr)opaque).Target;
            if (whence == ffmpeg.AVSEEK_SIZE)
            {
                return state.Data.Length;
            }
            long target = (whence & ~ffmpeg.AVSEEK_FORCE) switch
            {
                0 => offset, // SEEK_SET
                1 => state.Position + offset, // SEEK_CUR
                2 => state.Data.Length + offset, // SEEK_END
                _ => -1,
            };
            if (target < 0 || target > state.Data.Length)
            {
                return Einval;
            }
            state.Position = (int)target;
            return target;
        }
        catch
        {
            return Einval;
        }
    }

    private static byte[] DecodeOpenedInput(
        AVFormatContext* formatContext,
        string path,
        out WaveFormat waveFormat
    )
    {
        ThrowIfError(
            ffmpeg.avformat_find_stream_info(formatContext, null),
            $"read stream info for '{path}'"
        );
        AVCodec* codec = null;
        int streamIndex = ffmpeg.av_find_best_stream(
            formatContext,
            AVMediaType.AVMEDIA_TYPE_AUDIO,
            -1,
            -1,
            &codec,
            0
        );
        ThrowIfError(streamIndex, $"find audio stream in '{path}'");
        AVCodecContext* codecContext = ffmpeg.avcodec_alloc_context3(codec);
        if (codecContext == null)
        {
            throw new InvalidOperationException(
                $"FFmpeg failed to allocate codec context for '{path}'."
            );
        }
        try
        {
            ThrowIfError(
                ffmpeg.avcodec_parameters_to_context(
                    codecContext,
                    formatContext->streams[streamIndex]->codecpar
                ),
                $"copy codec parameters for '{path}'"
            );
            ThrowIfError(
                ffmpeg.avcodec_open2(codecContext, codec, null),
                $"open decoder for '{path}'"
            );
            return DecodeAudioStream(path, formatContext, streamIndex, codecContext, out waveFormat);
        }
        finally
        {
            ffmpeg.avcodec_free_context(&codecContext);
        }
    }

    private static byte[] DecodeAudioStream(
        string path,
        AVFormatContext* formatContext,
        int streamIndex,
        AVCodecContext* codecContext,
        out WaveFormat waveFormat
    )
    {
        using MemoryStream output = new MemoryStream();
        AVPacket* packet = ffmpeg.av_packet_alloc();
        AVFrame* frame = ffmpeg.av_frame_alloc();
        if (packet == null || frame == null)
        {
            if (packet != null)
            {
                ffmpeg.av_packet_free(&packet);
            }
            if (frame != null)
            {
                ffmpeg.av_frame_free(&frame);
            }
            throw new InvalidOperationException(
                $"FFmpeg failed to allocate decode buffers for '{path}'."
            );
        }

        SwrContext* resampler = null;
        AVChannelLayout outputLayout = default;
        AVChannelLayout inputResamplerLayout = default;
        bool outputLayoutInitialized = false;
        bool inputResamplerLayoutInitialized = false;
        AVSampleFormat inputResamplerFormat = AVSampleFormat.AV_SAMPLE_FMT_NONE;
        int inputResamplerRate = 0;
        int sampleRate = 0;
        int channels = 0;

        try
        {
            while (true)
            {
                int readResult = ffmpeg.av_read_frame(formatContext, packet);
                if (readResult < 0)
                {
                    break;
                }
                try
                {
                    if (packet->stream_index != streamIndex)
                    {
                        continue;
                    }
                    ThrowIfError(
                        ffmpeg.avcodec_send_packet(codecContext, packet),
                        $"send audio packet for '{path}'"
                    );
                    ReceiveFrames(
                        path,
                        codecContext,
                        frame,
                        ref resampler,
                        ref outputLayout,
                        ref outputLayoutInitialized,
                        ref inputResamplerLayout,
                        ref inputResamplerLayoutInitialized,
                        ref inputResamplerFormat,
                        ref inputResamplerRate,
                        ref sampleRate,
                        ref channels,
                        output
                    );
                }
                finally
                {
                    ffmpeg.av_packet_unref(packet);
                }
            }

            ThrowIfError(
                ffmpeg.avcodec_send_packet(codecContext, null),
                $"flush decoder for '{path}'"
            );
            ReceiveFrames(
                path,
                codecContext,
                frame,
                ref resampler,
                ref outputLayout,
                ref outputLayoutInitialized,
                ref inputResamplerLayout,
                ref inputResamplerLayoutInitialized,
                ref inputResamplerFormat,
                ref inputResamplerRate,
                ref sampleRate,
                ref channels,
                output
            );
        }
        finally
        {
            if (inputResamplerLayoutInitialized)
            {
                ffmpeg.av_channel_layout_uninit(&inputResamplerLayout);
            }
            if (outputLayoutInitialized)
            {
                ffmpeg.av_channel_layout_uninit(&outputLayout);
            }
            if (resampler != null)
            {
                ffmpeg.swr_free(&resampler);
            }
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_packet_free(&packet);
        }

        if (sampleRate <= 0 || channels <= 0 || output.Length == 0)
        {
            throw new InvalidOperationException($"FFmpeg decoded no PCM data from '{path}'.");
        }

        waveFormat = new WaveFormat(sampleRate, 16, channels);
        return output.ToArray();
    }

    private static void ReceiveFrames(
        string path,
        AVCodecContext* codecContext,
        AVFrame* frame,
        ref SwrContext* resampler,
        ref AVChannelLayout outputLayout,
        ref bool outputLayoutInitialized,
        ref AVChannelLayout inputResamplerLayout,
        ref bool inputResamplerLayoutInitialized,
        ref AVSampleFormat inputResamplerFormat,
        ref int inputResamplerRate,
        ref int sampleRate,
        ref int channels,
        MemoryStream output
    )
    {
        while (true)
        {
            int receiveResult = ffmpeg.avcodec_receive_frame(codecContext, frame);
            if (receiveResult == Eagain || receiveResult == ffmpeg.AVERROR_EOF)
            {
                return;
            }
            ThrowIfError(receiveResult, $"receive decoded frame for '{path}'");
            ConvertFrame(
                path,
                codecContext,
                frame,
                ref resampler,
                ref outputLayout,
                ref outputLayoutInitialized,
                ref inputResamplerLayout,
                ref inputResamplerLayoutInitialized,
                ref inputResamplerFormat,
                ref inputResamplerRate,
                ref sampleRate,
                ref channels,
                output
            );
            ffmpeg.av_frame_unref(frame);
        }
    }

    private static void ConvertFrame(
        string path,
        AVCodecContext* codecContext,
        AVFrame* inputFrame,
        ref SwrContext* resampler,
        ref AVChannelLayout outputLayout,
        ref bool outputLayoutInitialized,
        ref AVChannelLayout inputResamplerLayout,
        ref bool inputResamplerLayoutInitialized,
        ref AVSampleFormat inputResamplerFormat,
        ref int inputResamplerRate,
        ref int sampleRate,
        ref int channels,
        MemoryStream output
    )
    {
        AVChannelLayout inputLayout =
            inputFrame->ch_layout.nb_channels > 0 ? inputFrame->ch_layout : codecContext->ch_layout;
        bool inputLayoutInitialized = false;
        if (inputLayout.nb_channels <= 0)
        {
            ffmpeg.av_channel_layout_default(
                &inputLayout,
                codecContext->ch_layout.nb_channels > 0 ? codecContext->ch_layout.nb_channels : 2
            );
            inputLayoutInitialized = true;
        }

        try
        {
            AVSampleFormat inputFormat = (AVSampleFormat)inputFrame->format;
            int inputRate =
                inputFrame->sample_rate > 0 ? inputFrame->sample_rate : codecContext->sample_rate;
            AVChannelLayout currentInputResamplerLayout = inputResamplerLayout;
            bool shouldRecreateResampler =
                resampler == null
                || inputResamplerFormat != inputFormat
                || inputResamplerRate != inputRate
                || !LayoutsEqual(&currentInputResamplerLayout, &inputLayout);
            if (shouldRecreateResampler)
            {
                if (resampler != null)
                {
                    SwrContext* currentResampler = resampler;
                    ffmpeg.swr_free(&currentResampler);
                    resampler = null;
                }
                if (inputResamplerLayoutInitialized)
                {
                    AVChannelLayout layoutToFree = inputResamplerLayout;
                    ffmpeg.av_channel_layout_uninit(&layoutToFree);
                    inputResamplerLayout = default;
                    inputResamplerLayoutInitialized = false;
                }
                if (!outputLayoutInitialized)
                {
                    channels = inputLayout.nb_channels;
                    sampleRate = inputRate;
                    AVChannelLayout copiedOutputLayout = default;
                    ThrowIfError(
                        ffmpeg.av_channel_layout_copy(&copiedOutputLayout, &inputLayout),
                        $"copy channel layout for '{path}'"
                    );
                    outputLayout = copiedOutputLayout;
                    outputLayoutInitialized = true;
                }
                AVChannelLayout copiedInputLayout = default;
                ThrowIfError(
                    ffmpeg.av_channel_layout_copy(&copiedInputLayout, &inputLayout),
                    $"copy input layout for '{path}'"
                );
                inputResamplerLayout = copiedInputLayout;
                inputResamplerLayoutInitialized = true;
                inputResamplerFormat = inputFormat;
                inputResamplerRate = inputRate;
                AVChannelLayout targetOutputLayout = outputLayout;
                SwrContext* configuredResampler = resampler;
                AVChannelLayout targetInputLayout = inputResamplerLayout;
                ThrowIfError(
                    ffmpeg.swr_alloc_set_opts2(
                        &configuredResampler,
                        &targetOutputLayout,
                        AVSampleFormat.AV_SAMPLE_FMT_S16,
                        sampleRate,
                        &targetInputLayout,
                        inputFormat,
                        inputRate,
                        0,
                        null
                    ),
                    $"configure resampler for '{path}'"
                );
                resampler = configuredResampler;
                ThrowIfError(ffmpeg.swr_init(resampler), $"initialize resampler for '{path}'");
            }

            byte** outputData = null;
            int outputLineSize = 0;
            try
            {
                long delay = ffmpeg.swr_get_delay(resampler, inputRate);
                int outputSamples = (int)
                    ffmpeg.av_rescale_rnd(
                        delay + inputFrame->nb_samples,
                        sampleRate,
                        inputRate,
                        AVRounding.AV_ROUND_UP
                    );
                ThrowIfError(
                    ffmpeg.av_samples_alloc_array_and_samples(
                        &outputData,
                        &outputLineSize,
                        channels,
                        outputSamples,
                        AVSampleFormat.AV_SAMPLE_FMT_S16,
                        0
                    ),
                    $"allocate output samples for '{path}'"
                );
                int convertedSamples = ffmpeg.swr_convert(
                    resampler,
                    outputData,
                    outputSamples,
                    inputFrame->extended_data,
                    inputFrame->nb_samples
                );
                ThrowIfError(convertedSamples, $"convert audio frame for '{path}'");
                int bufferSize = ffmpeg.av_samples_get_buffer_size(
                    &outputLineSize,
                    channels,
                    convertedSamples,
                    AVSampleFormat.AV_SAMPLE_FMT_S16,
                    1
                );
                ThrowIfError(bufferSize, $"measure audio buffer for '{path}'");
                byte[] managedBuffer = new byte[bufferSize];
                Marshal.Copy((IntPtr)outputData[0], managedBuffer, 0, bufferSize);
                output.Write(managedBuffer, 0, managedBuffer.Length);
            }
            finally
            {
                if (outputData != null)
                {
                    ffmpeg.av_freep(&outputData[0]);
                    ffmpeg.av_freep(&outputData);
                }
            }
        }
        finally
        {
            if (inputLayoutInitialized)
            {
                ffmpeg.av_channel_layout_uninit(&inputLayout);
            }
        }
    }

    private static bool LayoutsEqual(AVChannelLayout* left, AVChannelLayout* right)
    {
        if (left == null || right == null)
        {
            return false;
        }
        if (left->nb_channels <= 0 || right->nb_channels <= 0)
        {
            return false;
        }
        return ffmpeg.av_channel_layout_compare(left, right) == 0;
    }

    private static void ThrowIfError(int errorCode, string operation)
    {
        if (errorCode >= 0)
        {
            return;
        }
        byte* buffer = stackalloc byte[1024];
        ffmpeg.av_strerror(errorCode, buffer, 1024);
        string message = Marshal.PtrToStringAnsi((IntPtr)buffer) ?? $"FFmpeg error {errorCode}";
        throw new InvalidOperationException($"FFmpeg failed to {operation}: {message}");
    }
}

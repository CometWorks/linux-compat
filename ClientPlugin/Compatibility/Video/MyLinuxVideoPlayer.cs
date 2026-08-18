using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ClientPlugin.Audio;
using FFmpeg.AutoGen;
using SharpDX;
using SharpDX.DXGI;
using SharpDX.Direct3D11;
using VRage;
using VRage.Audio;
using VRage.Collections;
using VRage.Platform.Windows.Render;
using VRage.Utils;

namespace ClientPlugin.Compatibility.Video;

/// <summary>
/// FFmpeg video decoder with SDL audio and a SharpDX texture for VRage's SRV wrapper.
/// </summary>
internal unsafe class MyLinuxVideoPlayer : IVideoPlayer, IDisposable
{
    private static readonly object FfmpegInitLock = new object();
    private static bool s_ffmpegInitialized;
    private static readonly int Eagain = ffmpeg.AVERROR(11);

    private readonly object m_syncRoot = new object();

    private AVFormatContext* m_formatContext;
    private AVCodecContext* m_codecContext;
    private AVPacket* m_packet;
    private AVFrame* m_decodedFrame;
    private AVFrame* m_convertedFrame;
    private SwsContext* m_scaleContext;
    private int m_streamIndex = -1;
    private int m_videoWidth;
    private int m_videoHeight;
    private long m_avgTimePerFrame;

    private VideoState m_currentState = VideoState.Stopped;
    private double m_durationSeconds;
    private double m_currentPositionSeconds;
    private double m_pendingFrameSeconds;
    private double m_nominalFrameDurationSeconds = 1.0 / 30.0;
    private double m_timestampOriginSeconds = double.NaN;
    private long m_playbackStartTicks;
    private long m_fallbackFrameIndex;
    private bool m_hasPendingFrame;
    private bool m_endOfStream;
    private bool m_disposed;

    private MySwapQueue<byte[]> m_videoDataRgba;
    private byte[] m_currentFrame;
    private byte m_alphaTransparency = 255;

    private Texture2D m_texture;
    private ShaderResourceView m_srv;

    private float m_volume = 1f;
    private byte[] m_audioData;
    private IntPtr m_audioStream;
    private uint m_audioDeviceId;
    private int m_audioBytesPerSecond;
    private int m_audioBlockAlign;
    private bool m_audioQueued;

    public int VideoWidth => m_videoWidth;
    public int VideoHeight => m_videoHeight;
    public VideoState CurrentState => m_currentState;
    public IntPtr TextureSrv => m_srv.NativePointer;

    public float Volume
    {
        get => m_volume;
        set
        {
            lock (m_syncRoot)
            {
                m_volume = value;
                if (m_disposed || m_audioStream == IntPtr.Zero)
                    return;
                SdlAudio.SetAudioStreamGain(m_audioStream, value);
            }
        }
    }

    public bool TryGetFrameData(out byte[] frameData)
    {
        frameData = m_currentFrame;
        return frameData != null;
    }

    public void Init(string fileName)
    {
        lock (m_syncRoot)
        {
            try
            {
                if (m_disposed)
                    throw new ObjectDisposedException(nameof(MyLinuxVideoPlayer));

                // Game video paths use Windows separators.
                if (!string.IsNullOrEmpty(fileName))
                    fileName = fileName.Replace('\\', '/');
                EnsureFfmpegInitialized();
                LoadAudio(fileName);

                fixed (AVFormatContext** formatContext = &m_formatContext)
                {
                    ThrowIfError(ffmpeg.avformat_open_input(formatContext, fileName, null, null),
                        $"open video '{fileName}'");
                }
                ThrowIfError(ffmpeg.avformat_find_stream_info(m_formatContext, null),
                    $"read stream info for '{fileName}'");

                AVCodec* codec = null;
                m_streamIndex = ffmpeg.av_find_best_stream(m_formatContext, AVMediaType.AVMEDIA_TYPE_VIDEO,
                    -1, -1, &codec, 0);
                ThrowIfError(m_streamIndex, $"find video stream in '{fileName}'");

                m_codecContext = ffmpeg.avcodec_alloc_context3(codec);
                if (m_codecContext == null)
                    throw new InvalidOperationException($"FFmpeg failed to allocate a video decoder for '{fileName}'.");

                ThrowIfError(
                    ffmpeg.avcodec_parameters_to_context(m_codecContext,
                        m_formatContext->streams[m_streamIndex]->codecpar),
                    $"copy codec parameters for '{fileName}'");
                ThrowIfError(ffmpeg.avcodec_open2(m_codecContext, codec, null),
                    $"open video decoder for '{fileName}'");

                m_videoWidth = m_codecContext->width;
                m_videoHeight = m_codecContext->height;
                m_durationSeconds = GetDurationSeconds();
                m_nominalFrameDurationSeconds = GetNominalFrameDurationSeconds();
                m_avgTimePerFrame = (long)(m_nominalFrameDurationSeconds * 10000000.0);

                m_videoDataRgba = new MySwapQueue<byte[]>(() => new byte[m_videoHeight * m_videoWidth * 4]);

                m_packet = ffmpeg.av_packet_alloc();
                m_decodedFrame = ffmpeg.av_frame_alloc();
                m_convertedFrame = ffmpeg.av_frame_alloc();
                if (m_packet == null || m_decodedFrame == null || m_convertedFrame == null)
                    throw new InvalidOperationException($"FFmpeg failed to allocate video decode buffers for '{fileName}'.");

                m_convertedFrame->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
                m_convertedFrame->width = m_videoWidth;
                m_convertedFrame->height = m_videoHeight;

                MyLog.Default.WriteLineAndConsole(
                    $"[LinuxCompat] VideoPlayer codec={m_codecContext->codec_id} w={m_videoWidth} h={m_videoHeight} pix_fmt={m_codecContext->pix_fmt}");

                ThrowIfError(ffmpeg.av_frame_get_buffer(m_convertedFrame, 0),
                    $"allocate converted video frame for '{fileName}'");

                // WMV3 may not expose pix_fmt until its first decoded frame.
                // Creating SwsContext earlier can assert inside libswscale.

                InitTextures();

                m_currentPositionSeconds = 0.0;
                m_pendingFrameSeconds = 0.0;
                m_timestampOriginSeconds = double.NaN;
                m_fallbackFrameIndex = 0;
                m_hasPendingFrame = false;
                m_endOfStream = false;
                m_currentFrame = null;
                m_audioQueued = false;
            }
            catch (Exception inner)
            {
                MyLog.Default.WriteLineAndConsole(
                    $"[LinuxCompat] VideoPlayer.Init FAILED for '{fileName}': {inner}");
                throw new Exception("Unable to load or play the video file", inner);
            }
        }
    }

    private void InitTextures()
    {
        var description = new Texture2DDescription
        {
            Width = m_videoWidth,
            Height = m_videoHeight,
            Format = Format.B8G8R8A8_UNorm_SRgb,
            ArraySize = 1,
            MipLevels = 1,
            BindFlags = BindFlags.ShaderResource,
            Usage = ResourceUsage.Dynamic,
            CpuAccessFlags = CpuAccessFlags.Write,
            SampleDescription = new SampleDescription(1, 0),
            OptionFlags = ResourceOptionFlags.None
        };
        m_texture = new Texture2D(MyPlatformRender.DeviceInstance, description);
        m_texture.DebugName = "MyLinuxVideoPlayer.Texture";

        var srvDesc = new ShaderResourceViewDescription
        {
            Format = Format.B8G8R8A8_UNorm_SRgb,
            Dimension = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,
        };
        srvDesc.Texture2D.MipLevels = 1;
        srvDesc.Texture2D.MostDetailedMip = 0;

        m_srv = new ShaderResourceView(MyPlatformRender.DeviceInstance, m_texture, srvDesc);
        m_srv.DebugName = "MyLinuxVideoPlayer.Texture";
    }

    public void Play()
    {
        lock (m_syncRoot)
        {
            if (m_disposed || m_currentState == VideoState.Playing)
                return;

            MyLog.Default.WriteLineAndConsole(
                $"[LinuxCompat] VideoPlayer.Play w={m_videoWidth} h={m_videoHeight} dur={m_durationSeconds:F2}s audio={(m_audioStream != IntPtr.Zero)}");
            QueueAudioFromCurrentPosition();
            m_playbackStartTicks = Stopwatch.GetTimestamp()
                - (long)(m_currentPositionSeconds * Stopwatch.Frequency);
            m_currentState = VideoState.Playing;
        }
    }

    public void Stop()
    {
        lock (m_syncRoot)
        {
            if (m_disposed)
                return;

            ClearQueuedAudio();
            SeekToStart();
            m_currentState = VideoState.Stopped;
        }
    }

    public void Update(object context)
    {
        lock (m_syncRoot)
        {
            if (m_disposed || m_videoDataRgba == null)
                return;

            UpdateInternal(context as DeviceContext);
        }
    }

    private void UpdateInternal(DeviceContext context)
    {
        if (m_currentState == VideoState.Playing)
        {
            double targetSeconds = Math.Max(0.0,
                (Stopwatch.GetTimestamp() - m_playbackStartTicks) / (double)Stopwatch.Frequency);

            while (true)
            {
                if (!m_hasPendingFrame && !TryDecodeNextFrame())
                    break;
                if (!m_hasPendingFrame)
                    break;
                if (m_currentPositionSeconds > 0.0 && m_pendingFrameSeconds > targetSeconds)
                    break;

                m_videoDataRgba.CommitWrite();
                m_currentPositionSeconds = m_pendingFrameSeconds;
                m_hasPendingFrame = false;
            }

            if (!m_hasPendingFrame && m_endOfStream
                && (m_durationSeconds <= 0.0 || targetSeconds >= m_durationSeconds))
            {
                ClearQueuedAudio();
                m_audioQueued = false;
                m_currentPositionSeconds =
                    m_durationSeconds > 0.0 ? m_durationSeconds : m_currentPositionSeconds;
                m_currentState = VideoState.Stopped;
            }
        }

        if (m_videoDataRgba.RefreshRead())
        {
            byte[] frame = m_videoDataRgba.Read;
            m_currentFrame = frame;
            if (context != null)
                OnFrame(context, frame);
        }
    }

    private void OnFrame(DeviceContext context, byte[] frameData)
    {
        DataBox box = context.MapSubresource(m_texture, 0, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None);
        if (box.IsEmpty)
            return;
        try
        {
            int rowSize = m_videoWidth * 4;
            IntPtr destination = box.DataPointer;
            int sourceOffset = 0;
            for (int row = 0; row < m_videoHeight; row++)
            {
                Utilities.Write(destination, frameData, sourceOffset, rowSize);
                destination += box.RowPitch;
                sourceOffset += rowSize;
            }
        }
        finally
        {
            context.UnmapSubresource(m_texture, 0);
        }
    }

    private bool TryDecodeNextFrame()
    {
        while (true)
        {
            int receiveResult = ffmpeg.avcodec_receive_frame(m_codecContext, m_decodedFrame);
            if (receiveResult == 0)
            {
                ConvertFrame(m_decodedFrame);
                ffmpeg.av_frame_unref(m_decodedFrame);
                return true;
            }
            if (receiveResult != Eagain && receiveResult != ffmpeg.AVERROR_EOF)
                ThrowIfError(receiveResult, "decode video frame");
            if (m_endOfStream)
                return false;

            int readResult = ffmpeg.av_read_frame(m_formatContext, m_packet);
            if (readResult < 0)
            {
                m_endOfStream = true;
                ThrowIfError(ffmpeg.avcodec_send_packet(m_codecContext, null),
                    "flush video decoder");
                continue;
            }
            try
            {
                if (m_packet->stream_index == m_streamIndex)
                    ThrowIfError(ffmpeg.avcodec_send_packet(m_codecContext, m_packet),
                        "send video packet");
            }
            finally
            {
                ffmpeg.av_packet_unref(m_packet);
            }
        }
    }

    private void ConvertFrame(AVFrame* sourceFrame)
    {
        AVPixelFormat sourceFormat = (AVPixelFormat)sourceFrame->format;
        bool wasNull = m_scaleContext == null;
        m_scaleContext = ffmpeg.sws_getCachedContext(m_scaleContext,
            m_videoWidth, m_videoHeight, sourceFormat,
            m_videoWidth, m_videoHeight, AVPixelFormat.AV_PIX_FMT_BGRA,
            2, null, null, null);
        if (m_scaleContext == null)
            throw new InvalidOperationException(
                $"FFmpeg failed to create a video conversion context (source pix_fmt={sourceFormat}).");
        if (wasNull)
            MyLog.Default.WriteLineAndConsole(
                $"[LinuxCompat] VideoPlayer scaler ready: source pix_fmt={sourceFormat} -> BGRA");

        ThrowIfError(ffmpeg.av_frame_make_writable(m_convertedFrame),
            "prepare converted video frame");
        ffmpeg.sws_scale(m_scaleContext, sourceFrame->data, sourceFrame->linesize,
            0, m_videoHeight, m_convertedFrame->data, m_convertedFrame->linesize);

        byte[] write = m_videoDataRgba.Write;
        int sourceStride = m_convertedFrame->linesize[0];
        int destinationStride = m_videoWidth * 4;
        for (int row = 0; row < m_videoHeight; row++)
        {
            IntPtr source = (IntPtr)(m_convertedFrame->data[0] + row * sourceStride);
            int destinationOffset = row * destinationStride;
            Marshal.Copy(source, write, destinationOffset, destinationStride);
            for (int i = destinationOffset + 3; i < destinationOffset + destinationStride; i += 4)
                write[i] = m_alphaTransparency;
        }
        m_pendingFrameSeconds = GetFrameTimestampSeconds(m_decodedFrame);
        m_hasPendingFrame = true;
    }

    private void SeekToStart()
    {
        if (m_formatContext == null || m_codecContext == null)
        {
            m_currentPositionSeconds = 0.0;
            return;
        }
        ThrowIfError(ffmpeg.av_seek_frame(m_formatContext, m_streamIndex, 0,
            ffmpeg.AVSEEK_FLAG_BACKWARD), "seek video to start");
        ffmpeg.avcodec_flush_buffers(m_codecContext);
        ffmpeg.av_packet_unref(m_packet);
        ffmpeg.av_frame_unref(m_decodedFrame);
        ffmpeg.av_frame_unref(m_convertedFrame);
        m_currentPositionSeconds = 0.0;
        m_pendingFrameSeconds = 0.0;
        m_timestampOriginSeconds = double.NaN;
        m_fallbackFrameIndex = 0;
        m_endOfStream = false;
        m_hasPendingFrame = false;
        m_currentFrame = null;
        m_audioQueued = false;
    }

    public void Dispose()
    {
        lock (m_syncRoot)
        {
            if (m_disposed)
                return;

            m_disposed = true;
            m_currentState = VideoState.Stopped;

            DestroyAudio();

            if (m_scaleContext != null)
            {
                ffmpeg.sws_freeContext(m_scaleContext);
                m_scaleContext = null;
            }
            if (m_convertedFrame != null)
            {
                fixed (AVFrame** p = &m_convertedFrame) ffmpeg.av_frame_free(p);
            }
            if (m_decodedFrame != null)
            {
                fixed (AVFrame** p = &m_decodedFrame) ffmpeg.av_frame_free(p);
            }
            if (m_packet != null)
            {
                fixed (AVPacket** p = &m_packet) ffmpeg.av_packet_free(p);
            }
            if (m_codecContext != null)
            {
                fixed (AVCodecContext** p = &m_codecContext) ffmpeg.avcodec_free_context(p);
            }
            if (m_formatContext != null)
            {
                fixed (AVFormatContext** p = &m_formatContext) ffmpeg.avformat_close_input(p);
            }

            m_videoDataRgba = null;
            m_currentFrame = null;
            m_srv?.Dispose();
            m_srv = null;
            m_texture?.Dispose();
            m_texture = null;
        }
    }

    private double GetDurationSeconds()
    {
        AVStream* stream = m_formatContext->streams[m_streamIndex];
        if (stream->duration > 0)
            return stream->duration * ffmpeg.av_q2d(stream->time_base);
        if (m_formatContext->duration > 0)
            return m_formatContext->duration / (double)ffmpeg.AV_TIME_BASE;
        return 0.0;
    }

    private double GetNominalFrameDurationSeconds()
    {
        AVStream* stream = m_formatContext->streams[m_streamIndex];
        AVRational rate = stream->avg_frame_rate.num != 0 ? stream->avg_frame_rate : stream->r_frame_rate;
        if (rate.num != 0 && rate.den != 0)
            return rate.den / (double)rate.num;
        if (m_codecContext->framerate.num != 0 && m_codecContext->framerate.den != 0)
            return m_codecContext->framerate.den / (double)m_codecContext->framerate.num;
        return 1.0 / 30.0;
    }

    private double GetFrameTimestampSeconds(AVFrame* frame)
    {
        long timestamp = frame->best_effort_timestamp;
        if (timestamp == ffmpeg.AV_NOPTS_VALUE)
            timestamp = frame->pts;
        if (timestamp == ffmpeg.AV_NOPTS_VALUE)
            return m_fallbackFrameIndex++ * m_nominalFrameDurationSeconds;
        double seconds = timestamp * ffmpeg.av_q2d(m_formatContext->streams[m_streamIndex]->time_base);
        if (double.IsNaN(m_timestampOriginSeconds))
            m_timestampOriginSeconds = seconds;
        return Math.Max(0.0, seconds - m_timestampOriginSeconds);
    }

    private static void ThrowIfError(int errorCode, string operation)
    {
        if (errorCode >= 0) return;
        byte* buffer = stackalloc byte[1024];
        ffmpeg.av_strerror(errorCode, buffer, 1024);
        throw new InvalidOperationException(
            $"FFmpeg failed to {operation}: {Marshal.PtrToStringAnsi((IntPtr)buffer)} ({errorCode}).");
    }

    private static void EnsureFfmpegInitialized()
    {
        if (s_ffmpegInitialized) return;
        lock (FfmpegInitLock)
        {
            if (s_ffmpegInitialized) return;
            ffmpeg.RootPath = string.Empty;
            PinFfmpegLibraryVersions();
            Type bindingsType = typeof(ffmpeg).Assembly.GetType("FFmpeg.AutoGen.DynamicallyLoadedBindings");
            MethodInfo initializeMethod = bindingsType?.GetMethod("Initialize",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            initializeMethod?.Invoke(null, null);
            _ = ffmpeg.avformat_version();
            s_ffmpegInitialized = true;
        }
    }

    private static void PinFfmpegLibraryVersions()
    {
        // FFmpeg.AutoGen 8.1.0 requires the FFmpeg 8.1 ABI bundled by Pulsar.
        // FFmpeg 7 has a different AVCodecContext layout after `framerate`.
        // Omit unused libraries to avoid loading a second dependency set.
        ffmpeg.LibraryVersionMap["avcodec"]    = 62;
        ffmpeg.LibraryVersionMap["avformat"]   = 62;
        ffmpeg.LibraryVersionMap["avutil"]     = 60;
        ffmpeg.LibraryVersionMap["swresample"] = 6;
        ffmpeg.LibraryVersionMap["swscale"]    = 9;
    }

    private void LoadAudio(string fileName)
    {
        AVFormatContext* formatContext = null;
        try
        {
            ThrowIfError(ffmpeg.avformat_open_input(&formatContext, fileName, null, null),
                $"open audio '{fileName}'");
            ThrowIfError(ffmpeg.avformat_find_stream_info(formatContext, null),
                $"read audio stream info for '{fileName}'");
            AVCodec* codec = null;
            int streamIndex = ffmpeg.av_find_best_stream(formatContext, AVMediaType.AVMEDIA_TYPE_AUDIO,
                -1, -1, &codec, 0);
            if (streamIndex < 0)
                return;

            AVCodecContext* codecContext = ffmpeg.avcodec_alloc_context3(codec);
            if (codecContext == null)
                throw new InvalidOperationException($"FFmpeg failed to allocate an audio decoder for '{fileName}'.");
            try
            {
                ThrowIfError(ffmpeg.avcodec_parameters_to_context(codecContext,
                    formatContext->streams[streamIndex]->codecpar),
                    $"copy audio codec parameters for '{fileName}'");
                ThrowIfError(ffmpeg.avcodec_open2(codecContext, codec, null),
                    $"open audio decoder for '{fileName}'");
                m_audioData = DecodeAudioStream(fileName, formatContext, streamIndex, codecContext,
                    out var spec, out var bytesPerSecond, out var blockAlign);
                m_audioBytesPerSecond = bytesPerSecond;
                m_audioBlockAlign = blockAlign;
                CreateAudioOutput(spec);
            }
            finally
            {
                ffmpeg.avcodec_free_context(&codecContext);
            }
        }
        catch (Exception ex)
        {
            MyLog.Default.WriteLineAndConsole($"[LinuxCompat] VideoPlayer audio load failed: {ex.Message}");
            m_audioData = null;
            DestroyAudio();
        }
        finally
        {
            if (formatContext != null)
                ffmpeg.avformat_close_input(&formatContext);
        }
    }

    private byte[] DecodeAudioStream(string fileName, AVFormatContext* formatContext, int streamIndex,
        AVCodecContext* codecContext, out MySdlAudioInterop.SdlAudioSpec audioSpec, out int bytesPerSecond, out int blockAlign)
    {
        using var output = new MemoryStream();
        AVPacket* packet = ffmpeg.av_packet_alloc();
        AVFrame* frame = ffmpeg.av_frame_alloc();
        if (packet == null || frame == null)
        {
            if (packet != null) ffmpeg.av_packet_free(&packet);
            if (frame != null) ffmpeg.av_frame_free(&frame);
            throw new InvalidOperationException($"FFmpeg failed to allocate audio decode buffers for '{fileName}'.");
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
                if (readResult < 0) break;
                try
                {
                    if (packet->stream_index != streamIndex) continue;
                    ThrowIfError(ffmpeg.avcodec_send_packet(codecContext, packet),
                        $"send audio packet for '{fileName}'");
                    ReceiveAudioFrames(fileName, codecContext, frame, ref resampler,
                        ref outputLayout, ref outputLayoutInitialized,
                        ref inputResamplerLayout, ref inputResamplerLayoutInitialized,
                        ref inputResamplerFormat, ref inputResamplerRate,
                        ref sampleRate, ref channels, output);
                }
                finally { ffmpeg.av_packet_unref(packet); }
            }
            ThrowIfError(ffmpeg.avcodec_send_packet(codecContext, null),
                $"flush audio decoder for '{fileName}'");
            ReceiveAudioFrames(fileName, codecContext, frame, ref resampler,
                ref outputLayout, ref outputLayoutInitialized,
                ref inputResamplerLayout, ref inputResamplerLayoutInitialized,
                ref inputResamplerFormat, ref inputResamplerRate,
                ref sampleRate, ref channels, output);
        }
        finally
        {
            if (inputResamplerLayoutInitialized)
                ffmpeg.av_channel_layout_uninit(&inputResamplerLayout);
            if (outputLayoutInitialized)
                ffmpeg.av_channel_layout_uninit(&outputLayout);
            if (resampler != null)
                ffmpeg.swr_free(&resampler);
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_packet_free(&packet);
        }
        if (sampleRate <= 0 || channels <= 0 || output.Length == 0)
            throw new InvalidOperationException($"FFmpeg decoded no audio PCM data from '{fileName}'.");

        bytesPerSecond = sampleRate * channels * 2;
        blockAlign = channels * 2;
        audioSpec = new MySdlAudioInterop.SdlAudioSpec
        {
            Format = MySdlAudioInterop.SDL_AUDIO_S16LE,
            Channels = channels,
            Frequency = sampleRate
        };
        return output.ToArray();
    }

    private void ReceiveAudioFrames(string fileName, AVCodecContext* codecContext, AVFrame* frame,
        ref SwrContext* resampler,
        ref AVChannelLayout outputLayout, ref bool outputLayoutInitialized,
        ref AVChannelLayout inputResamplerLayout, ref bool inputResamplerLayoutInitialized,
        ref AVSampleFormat inputResamplerFormat, ref int inputResamplerRate,
        ref int sampleRate, ref int channels, MemoryStream output)
    {
        while (true)
        {
            int receiveResult = ffmpeg.avcodec_receive_frame(codecContext, frame);
            if (receiveResult == Eagain || receiveResult == ffmpeg.AVERROR_EOF)
                return;
            ThrowIfError(receiveResult, $"receive audio frame for '{fileName}'");
            ConvertAudioFrame(fileName, codecContext, frame, ref resampler,
                ref outputLayout, ref outputLayoutInitialized,
                ref inputResamplerLayout, ref inputResamplerLayoutInitialized,
                ref inputResamplerFormat, ref inputResamplerRate,
                ref sampleRate, ref channels, output);
            ffmpeg.av_frame_unref(frame);
        }
    }

    private void ConvertAudioFrame(string fileName, AVCodecContext* codecContext, AVFrame* inputFrame,
        ref SwrContext* resampler,
        ref AVChannelLayout outputLayout, ref bool outputLayoutInitialized,
        ref AVChannelLayout inputResamplerLayout, ref bool inputResamplerLayoutInitialized,
        ref AVSampleFormat inputResamplerFormat, ref int inputResamplerRate,
        ref int sampleRate, ref int channels, MemoryStream output)
    {
        AVChannelLayout inputLayout = inputFrame->ch_layout.nb_channels > 0
            ? inputFrame->ch_layout
            : codecContext->ch_layout;
        bool inputLayoutInitialized = false;
        if (inputLayout.nb_channels <= 0)
        {
            ffmpeg.av_channel_layout_default(&inputLayout,
                codecContext->ch_layout.nb_channels > 0 ? codecContext->ch_layout.nb_channels : 2);
            inputLayoutInitialized = true;
        }
        try
        {
            AVSampleFormat inputFormat = (AVSampleFormat)inputFrame->format;
            int inputRate = inputFrame->sample_rate > 0 ? inputFrame->sample_rate : codecContext->sample_rate;
            AVChannelLayout currentInputResamplerLayout = inputResamplerLayout;
            bool shouldRecreateResampler = resampler == null
                || inputResamplerFormat != inputFormat
                || inputResamplerRate != inputRate
                || !AudioLayoutsEqual(&currentInputResamplerLayout, &inputLayout);
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
                    ThrowIfError(ffmpeg.av_channel_layout_copy(&copiedOutputLayout, &inputLayout),
                        $"copy output channel layout for '{fileName}'");
                    outputLayout = copiedOutputLayout;
                    outputLayoutInitialized = true;
                }
                AVChannelLayout copiedInputLayout = default;
                ThrowIfError(ffmpeg.av_channel_layout_copy(&copiedInputLayout, &inputLayout),
                    $"copy input channel layout for '{fileName}'");
                inputResamplerLayout = copiedInputLayout;
                inputResamplerLayoutInitialized = true;
                inputResamplerFormat = inputFormat;
                inputResamplerRate = inputRate;
                AVChannelLayout targetOutputLayout = outputLayout;
                AVChannelLayout targetInputLayout = inputResamplerLayout;
                SwrContext* configuredResampler = resampler;
                ThrowIfError(
                    ffmpeg.swr_alloc_set_opts2(&configuredResampler,
                        &targetOutputLayout, AVSampleFormat.AV_SAMPLE_FMT_S16, sampleRate,
                        &targetInputLayout, inputFormat, inputRate, 0, null),
                    $"configure audio resampler for '{fileName}'");
                resampler = configuredResampler;
                ThrowIfError(ffmpeg.swr_init(resampler), $"initialize audio resampler for '{fileName}'");
            }
            byte** outputData = null;
            int outputLineSize = 0;
            try
            {
                long delay = ffmpeg.swr_get_delay(resampler, inputRate);
                int outputSamples = (int)ffmpeg.av_rescale_rnd(delay + inputFrame->nb_samples,
                    sampleRate, inputRate, AVRounding.AV_ROUND_UP);
                ThrowIfError(
                    ffmpeg.av_samples_alloc_array_and_samples(&outputData, &outputLineSize,
                        channels, outputSamples, AVSampleFormat.AV_SAMPLE_FMT_S16, 0),
                    $"allocate output audio samples for '{fileName}'");
                int convertedSamples = ffmpeg.swr_convert(resampler, outputData, outputSamples,
                    inputFrame->extended_data, inputFrame->nb_samples);
                ThrowIfError(convertedSamples, $"convert audio frame for '{fileName}'");
                int bufferSize = ffmpeg.av_samples_get_buffer_size(&outputLineSize, channels,
                    convertedSamples, AVSampleFormat.AV_SAMPLE_FMT_S16, 1);
                ThrowIfError(bufferSize, $"measure output audio buffer for '{fileName}'");
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
                ffmpeg.av_channel_layout_uninit(&inputLayout);
        }
    }

    private static bool AudioLayoutsEqual(AVChannelLayout* left, AVChannelLayout* right)
    {
        if (left == null || right == null) return false;
        if (left->nb_channels <= 0 || right->nb_channels <= 0) return false;
        return ffmpeg.av_channel_layout_compare(left, right) == 0;
    }

    private void CreateAudioOutput(MySdlAudioInterop.SdlAudioSpec sourceSpec)
    {
        if (m_audioData == null || m_audioData.Length == 0)
            return;

        var requested = new MySdlAudioInterop.SdlAudioSpec
        {
            Format = MySdlAudioInterop.SDL_AUDIO_F32LE,
            Channels = Math.Max(sourceSpec.Channels, 2),
            Frequency = Math.Max(sourceSpec.Frequency, 48000)
        };
        if (!SdlAudio.InitSubSystem(SdlAudio.SDL_INIT_AUDIO))
            throw new InvalidOperationException($"SDL3 audio initialization failed: {SdlAudio.GetErrorString()}");
        uint deviceId = SdlAudio.OpenAudioDevice(MySdlAudioInterop.SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, ref requested);
        if (deviceId == 0)
            throw new InvalidOperationException($"SDL3 failed to open a video audio device: {SdlAudio.GetErrorString()}");
        if (!SdlAudio.GetAudioDeviceFormat(deviceId, out var outputSpec, out _))
            outputSpec = requested;
        IntPtr stream = SdlAudio.CreateAudioStream(ref sourceSpec, ref outputSpec);
        if (stream == IntPtr.Zero)
        {
            SdlAudio.CloseAudioDevice(deviceId);
            throw new InvalidOperationException($"SDL3 failed to create a video audio stream: {SdlAudio.GetErrorString()}");
        }
        if (!SdlAudio.BindAudioStream(deviceId, stream))
        {
            SdlAudio.DestroyAudioStream(stream);
            SdlAudio.CloseAudioDevice(deviceId);
            throw new InvalidOperationException($"SDL3 failed to bind a video audio stream: {SdlAudio.GetErrorString()}");
        }
        SdlAudio.SetAudioStreamGain(stream, m_volume);
        m_audioDeviceId = deviceId;
        m_audioStream = stream;
    }

    // All callers hold m_syncRoot.
    private void QueueAudioFromCurrentPosition()
    {
        if (m_audioStream == IntPtr.Zero || m_audioData == null || m_audioData.Length == 0 || m_audioQueued)
            return;
        ClearQueuedAudio();
        int offset = 0;
        if (m_audioBytesPerSecond > 0)
        {
            offset = (int)Math.Floor(m_currentPositionSeconds * m_audioBytesPerSecond);
            if (m_audioBlockAlign > 0)
                offset -= offset % m_audioBlockAlign;
        }
        offset = Math.Clamp(offset, 0, m_audioData.Length);
        byte[] audioSlice = m_audioData;
        if (offset > 0)
        {
            int remaining = m_audioData.Length - offset;
            if (remaining <= 0) return;
            audioSlice = new byte[remaining];
            System.Buffer.BlockCopy(m_audioData, offset, audioSlice, 0, remaining);
        }
        if (audioSlice.Length > 0 && !SdlAudio.PutAudioStreamData(m_audioStream, audioSlice, audioSlice.Length))
            throw new InvalidOperationException($"SDL3 failed to queue video audio: {SdlAudio.GetErrorString()}");
        SdlAudio.SetAudioStreamGain(m_audioStream, m_volume);
        m_audioQueued = true;
    }

    private void ClearQueuedAudio()
    {
        if (m_audioStream != IntPtr.Zero)
            SdlAudio.ClearAudioStream(m_audioStream);
        m_audioQueued = false;
    }

    private void DestroyAudio()
    {
        IntPtr stream = m_audioStream;
        uint deviceId = m_audioDeviceId;
        m_audioStream = IntPtr.Zero;
        m_audioDeviceId = 0;
        m_audioQueued = false;
        m_audioData = null;
        if (stream != IntPtr.Zero)
        {
            SdlAudio.ClearAudioStream(stream);
            SdlAudio.DestroyAudioStream(stream);
        }
        if (deviceId != 0)
            SdlAudio.CloseAudioDevice(deviceId);
    }
}

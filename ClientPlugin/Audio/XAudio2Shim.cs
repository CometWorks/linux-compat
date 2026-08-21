using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ClientPlugin.Audio;
using SharpDX;
using SharpDX.Mathematics.Interop;
using SharpDX.Multimedia;
using Silk.NET.OpenAL;
using VRage.Audio;

namespace SharpDX.XAudio2
{
    public enum XAudio2Version
    {
        Default,
    }

    public enum DeviceRole
    {
        DefaultGameDevice,
        DefaultCommunicationsDevice,
    }

    [Flags]
    public enum VoiceFlags
    {
        None = 0,
        UseFilter = 1,
    }

    public enum FilterType
    {
        LowPassFilter = 0,
        BandPassFilter = 1,
        HighPassFilter = 2,
        NotchFilter = 3,
    }

    [Flags]
    public enum BufferFlags
    {
        None = 0,
    }

    public struct FilterParameters
    {
        public FilterType Type;

        public float Frequency;

        public float OneOverQ;
    }

    public struct DeviceDetails
    {
        public string DeviceID;

        public string DisplayName;

        public DeviceRole Role;

        // The field type must match SharpDX so the game's IL can bind it.
        public WaveFormatExtensible OutputFormat;
    }

    // CLR field signatures must match SharpDX.XAudio2.VoiceDetails exactly:
    // VoiceFlags CreationFlags, then three int fields in the order below.
    public struct VoiceDetails
    {
        public VoiceFlags CreationFlags;

        public int ActiveFlags;

        public int InputChannelCount;

        public int InputSampleRate;
    }

    public struct VoiceState
    {
        public int BuffersQueued;
    }

    public class EffectDescriptor
    {
        public EffectDescriptor(SharpDX.XAPO.AudioProcessor effect) { }

        public EffectDescriptor(SharpDX.XAPO.AudioProcessor effect, int _) { }
    }

    [Flags]
    public enum VoiceSendFlags
    {
        None = 0,
    }

    // Fields, not properties: VRage.Audio IL accesses
    // VoiceSendDescriptor.Flags and .OutputVoice via ldfld/stfld.
    public struct VoiceSendDescriptor
    {
        public VoiceSendFlags Flags;

        public Voice OutputVoice;

        public VoiceSendDescriptor(Voice outputVoice)
        {
            Flags = VoiceSendFlags.None;
            OutputVoice = outputVoice;
        }

        public VoiceSendDescriptor(VoiceSendFlags flags, Voice outputVoice)
        {
            Flags = flags;
            OutputVoice = outputVoice;
        }
    }

    public class AudioBuffer
    {
        public DataStream Stream { get; set; }

        public int AudioBytes;

        public BufferFlags Flags;

        public int PlayLength;

        public int LoopCount;

        public int LoopBegin;

        public int LoopLength;

        public byte[] Data;

        // Must be a field with the SharpDX signature; game IL uses `ldfld`.
        // The shim reads Stream/Data, so this pointer remains zero.
        public IntPtr AudioDataPointer;

        public AudioBuffer() { }

        public AudioBuffer(DataStream stream)
        {
            Stream = stream;

            // Voice chat owns the DataStream only during construction. Copy its
            // payload before it can be dequeued, disposed, or reused.
            if (stream != null && stream.Length > 0)
            {
                int length = (int)stream.Length;
                Data = new byte[length];
                Marshal.Copy(stream.DataPointer, Data, 0, length);
                AudioBytes = length;
            }
            else
            {
                Data = Array.Empty<byte>();
            }
        }
    }

    // Game IL calls CppObject.get_NativePointer() to validate voices. Keep the
    // CppObject sentinel nonzero while the managed voice is alive.
    public abstract unsafe class Voice : global::SharpDX.CppObject
    {
        private float m_volume = 1f;

        private Voice m_outputVoice;

        protected Voice()
        {
            _nativePointer = (void*)1;
        }

        public virtual VoiceDetails VoiceDetails { get; protected set; }

        public virtual float EffectiveVolume => m_volume * (m_outputVoice?.EffectiveVolume ?? 1f);

        public event Action VolumeChanged;

        internal Voice OutputVoice
        {
            get { return m_outputVoice; }
            set
            {
                if (m_outputVoice == value)
                {
                    return;
                }
                if (m_outputVoice != null)
                {
                    m_outputVoice.VolumeChanged -= OnParentVolumeChanged;
                }
                m_outputVoice = value;
                if (m_outputVoice != null)
                {
                    m_outputVoice.VolumeChanged += OnParentVolumeChanged;
                }
                OnVolumeGraphChanged();
            }
        }

        public virtual void SetVolume(float volume)
        {
            m_volume = volume;
            OnVolumeGraphChanged();
        }

        public void SetVolume(float volume, int operationSet)
        {
            SetVolume(volume);
        }

        public virtual void GetVolume(out float volume)
        {
            volume = m_volume;
        }

        public virtual void SetEffectChain(EffectDescriptor[] _) { }

        public virtual void DisableEffect(int _) { }

        public virtual void EnableEffect(int _) { }

        public virtual bool IsEffectEnabled(int _)
        {
            return false;
        }

        public virtual void IsEffectEnabled(int _, out RawBool enabled)
        {
            enabled = IsEffectEnabled(0);
        }

        public virtual void SetOutputVoices(VoiceSendDescriptor[] descriptors)
        {
            OutputVoice =
                descriptors != null && descriptors.Length != 0 ? descriptors[0].OutputVoice : null;
        }

        // Must be declared on Voice; game IL calls
        // `callvirt SharpDX.XAudio2.Voice::SetFilterParameters(FilterParameters, int32)`.
        public virtual void SetFilterParameters(FilterParameters _) { }

        public virtual void SetFilterParameters(FilterParameters _, int __) { }

        // Must be declared on Voice to match the game's MethodRef.
        public virtual void SetOutputMatrix(
            Voice destinationVoice,
            int sourceChannels,
            int destinationChannels,
            float[] matrix
        ) { }

        public virtual void GetOutputMatrix(
            Voice destinationVoice,
            int sourceChannels,
            int destinationChannels,
            float[] matrix
        ) { }

        public virtual void GetChannelMask(out int channelMask)
        {
            channelMask = (int)MySdlAudioInterop.GetSpeakerMask(VoiceDetails.InputChannelCount);
        }

        // Must be declared on Voice to match the game's MethodRef.
        public virtual void GetVoiceDetails(out VoiceDetails voiceDetails)
        {
            voiceDetails = VoiceDetails;
        }

        protected override void Dispose(bool disposing)
        {
            _nativePointer = null;
            if (m_outputVoice != null)
            {
                m_outputVoice.VolumeChanged -= OnParentVolumeChanged;
                m_outputVoice = null;
            }
        }

        public virtual void DestroyVoice() { }

        protected virtual void OnVolumeGraphChanged()
        {
            this.VolumeChanged?.Invoke();
        }

        private void OnParentVolumeChanged()
        {
            OnVolumeGraphChanged();
        }
    }

    // Game IL validates the device through CppObject.NativePointer, so the sentinel
    // remains nonzero while alive. OpenAL handles panning, HRTF, and resampling;
    // the game retains attenuation and Doppler control.
    public sealed unsafe class XAudio2 : global::SharpDX.CppObject
    {
        // OpenAL selects its device format. VRage only needs this synthetic spec
        // for mixing-graph channel and sample-rate metadata.
        private readonly MySdlAudioInterop.SdlAudioSpec m_outputSpec;

        private readonly AL m_al;
        private readonly ALContext m_alc;
        private readonly Device* m_device;
        private readonly Context* m_context;
        private readonly object m_alLock = new object();
        private readonly bool m_floatExtensionAvailable;
        private readonly bool m_directChannelsExtensionAvailable;
        private readonly bool m_directChannelsRemixExtensionAvailable;
        private readonly bool m_sourceSpatializeExtensionAvailable;

        internal MySdlAudioInterop.SdlAudioSpec OutputSpec => m_outputSpec;

        internal AL Al => m_al;

        internal object AlLock => m_alLock;

        internal bool FloatExtensionAvailable => m_floatExtensionAvailable;

        internal bool DirectChannelsExtensionAvailable => m_directChannelsExtensionAvailable;

        internal bool DirectChannelsRemixExtensionAvailable =>
            m_directChannelsRemixExtensionAvailable;

        internal bool SourceSpatializeExtensionAvailable => m_sourceSpatializeExtensionAvailable;

        public event EventHandler<ErrorEventArgs> CriticalError;

        public XAudio2(XAudio2Version _)
        {
            _nativePointer = (void*)1;

            // Initialize SDL audio before the engine starts creating voices.
            MySdlAudioInterop.EnsureInitialized();

            m_alc = ALContext.GetApi(soft: true);
            m_al = AL.GetApi(soft: true);
            m_device = m_alc.OpenDevice("");
            if (m_device == null)
            {
                throw new InvalidOperationException(
                    "OpenAL: failed to open default playback device (alcOpenDevice returned NULL)"
                );
            }
            m_context = m_alc.CreateContext(m_device, null);
            if (m_context == null)
            {
                m_alc.CloseDevice(m_device);
                throw new InvalidOperationException(
                    "OpenAL: failed to create audio context (alcCreateContext returned NULL)"
                );
            }
            if (!m_alc.MakeContextCurrent(m_context))
            {
                m_alc.DestroyContext(m_context);
                m_alc.CloseDevice(m_device);
                throw new InvalidOperationException("OpenAL: alcMakeContextCurrent failed");
            }

            // The game owns distance attenuation (custom curves + linear falloff
            // applied to MatrixCoefficients in MyXAudio2.Apply3D) and Doppler (clamped
            // frequency ratio fed to SetFrequencyRatio). Disable both in OpenAL so
            // nothing double-applies.
            m_al.DistanceModel(DistanceModel.None);
            m_al.DopplerFactor(0.0f);

            m_floatExtensionAvailable = m_al.IsExtensionPresent("AL_EXT_float32");
            m_directChannelsExtensionAvailable = m_al.IsExtensionPresent("AL_SOFT_direct_channels");
            m_directChannelsRemixExtensionAvailable = m_al.IsExtensionPresent(
                "AL_SOFT_direct_channels_remix"
            );
            m_sourceSpatializeExtensionAvailable = m_al.IsExtensionPresent(
                "AL_SOFT_source_spatialize"
            );

            // VRage.Audio only reads channel count + sample rate from this spec to
            // fill DeviceDetails / WaveFormatExtensible for the game's graph.
            m_outputSpec = new MySdlAudioInterop.SdlAudioSpec
            {
                Format = MySdlAudioInterop.SDL_AUDIO_F32LE,
                Channels = 2,
                Frequency = 48000,
            };

            // X3DAudio is constructed independently, so publish the AL handle and lock.
            global::SharpDX.X3DAudio.X3DAudio.GlobalAl = m_al;
            global::SharpDX.X3DAudio.X3DAudio.GlobalAlLock = m_alLock;
        }

        public static float SemitonesToFrequencyRatio(float semitones)
        {
            return (float)Math.Pow(2.0, semitones / 12f);
        }

        internal DeviceDetails GetDeviceDetails()
        {
            return new DeviceDetails
            {
                DeviceID = "OpenAL-Default",
                DisplayName = "OpenAL Soft Default Playback",
                Role = DeviceRole.DefaultGameDevice,
                OutputFormat = new WaveFormatExtensible(
                    m_outputSpec.Frequency,
                    16,
                    m_outputSpec.Channels
                )
                {
                    ChannelMask = MySdlAudioInterop.GetSpeakerMask(m_outputSpec.Channels),
                },
            };
        }

        public DeviceDetails GetDeviceDetails(int _)
        {
            return GetDeviceDetails();
        }

        protected override void Dispose(bool disposing)
        {
            _nativePointer = null;

            // Detach X3DAudio before destroying the AL instance.
            global::SharpDX.X3DAudio.X3DAudio.GlobalAl = null;
            global::SharpDX.X3DAudio.X3DAudio.GlobalAlLock = null;

            lock (m_alLock)
            {
                if (m_context != null)
                {
                    m_alc.MakeContextCurrent(null);
                    m_alc.DestroyContext(m_context);
                }
                if (m_device != null)
                {
                    m_alc.CloseDevice(m_device);
                }
            }

            // OpenAL drains its mixing thread during context and device destruction.
            try
            {
                m_al?.Dispose();
            }
            catch { }
            try
            {
                m_alc?.Dispose();
            }
            catch { }
        }

        internal void RaiseCriticalError(Exception exception)
        {
            this.CriticalError?.Invoke(this, new ErrorEventArgs(exception));
        }

        public void CommitChanges(int _) { }

        public void StopEngine()
        {
            // OpenAL owns the mixing thread and stops it during context destruction.
            if (IsDisposed)
                return;
        }
    }

    public sealed class MasteringVoice : Voice
    {
        private readonly XAudio2 m_engine;

        public MasteringVoice(XAudio2 engine, int _, int __, string ___)
        {
            m_engine = engine;
            VoiceDetails = new VoiceDetails
            {
                InputChannelCount = engine.OutputSpec.Channels,
                InputSampleRate = engine.OutputSpec.Frequency,
                ActiveFlags = 0,
            };
        }

        public override void GetChannelMask(out int channelMask)
        {
            channelMask = (int)MySdlAudioInterop.GetSpeakerMask(m_engine.OutputSpec.Channels);
        }
    }

    public sealed class SubmixVoice : Voice
    {
        public SubmixVoice(XAudio2 engine, int channels, int sampleRate)
        {
            VoiceDetails = new VoiceDetails
            {
                InputChannelCount = channels,
                InputSampleRate = sampleRate,
                ActiveFlags = 0,
            };
            OutputVoice = new MasteringVoice(engine, 0, 0, null);
        }
    }

    public sealed class SourceVoice : Voice
    {
        // AL_EXT_float32 buffer-format constants. Silk.NET's BufferFormat enum
        // only covers OpenAL 1.1 (Mono8/16, Stereo8/16); float32 is exposed via
        // AL_EXT_float32 and must be supplied as raw integer constants.
        private const int AL_FORMAT_MONO_FLOAT32 = 0x10010;
        private const int AL_FORMAT_STEREO_FLOAT32 = 0x10011;
        private const int AL_DIRECT_CHANNELS_SOFT = 0x1033;
        private const int AL_SOURCE_SPATIALIZE_SOFT = 0x1214;
        private const int AL_REMIX_UNMATCHED_SOFT = 0x0002;
        private const float MatrixTolerance = 0.0001f;

        private enum RoutingMode
        {
            Direct,
            SpatialMono3D,
            SoftwareMonoToStereoHalf,
        }

        private readonly XAudio2 m_engine;

        private readonly AL m_al;

        private readonly WaveFormat m_sourceFormat;

        // 0 = uninitialised / disposed. AL source IDs are returned by alGenSources.
        private uint m_source;

        // Pre-resolved per-voice buffer format. For 32-bit float input, this is the
        // AL_EXT_float32 raw constant when the extension is present; otherwise the
        // voice falls back to S16 with a per-PutBuffer conversion.
        private readonly int m_alFormatRaw;
        private readonly bool m_convertF32ToS16;
        private readonly int m_alFormatRawStereo;
        private readonly bool m_convertF32ToS16Stereo;

        // AL buffer IDs currently queued on the source, in submission order. The
        // watcher unqueues processed buffers from the head and deletes them.
        private readonly Queue<uint> m_alBufferQueue = new Queue<uint>();

        // Per-AL-buffer flag: true = game-submitted buffer whose completion should
        // fire a BufferEnd callback; false = internally re-fed loop buffer whose
        // completion must be silently consumed. Kept in lock-step with m_alBufferQueue.
        private readonly Queue<bool> m_alBufferTracked = new Queue<bool>();

        // PCM byte counts align tracked AL buffers with BufferEnd callbacks.
        private readonly Queue<int> m_pushedBufferSizes = new Queue<int>();

        // XAudio2 fires BufferEnd once after looping stops, not after each iteration.
        private bool m_loopCallbackDeferred;

        private readonly List<AudioBuffer> m_buffers = new List<AudioBuffer>();

        private readonly object m_sync = new object();

        private CancellationTokenSource m_watchToken;

        private VoiceState m_state;

        private bool m_started;

        private bool m_paused;

        private bool m_stopRequested;

        private bool m_loopReplayEnabled;

        // Streaming voices retain their watcher across underruns so the next packet
        // cannot race a natural-drain shutdown.
        private bool m_streaming;

        private float m_frequencyRatio = 1f;

        private float m_matrixGain = 1f;

        private RoutingMode m_routingMode;
        private float[] m_lastOutputMatrix = Array.Empty<float>();
        private int m_lastOutputMatrixSourceChannels;
        private int m_lastOutputMatrixDestinationChannels;

        public event Action<IntPtr> BufferEnd;

        public int SourceSampleRate { get; set; }

        public VoiceState State => m_state;

        public SourceVoice(
            XAudio2 engine,
            WaveFormat sourceFormat,
            bool enableCallbackEvents = true
        )
            : this(engine, sourceFormat, VoiceFlags.None, 2f, enableCallbackEvents) { }

        public SourceVoice(
            XAudio2 engine,
            WaveFormat sourceFormat,
            VoiceFlags flags,
            float _,
            bool enableCallbackEvents = true
        )
        {
            m_engine = engine;
            m_al = engine.Al;
            m_sourceFormat = sourceFormat;
            SourceSampleRate = sourceFormat.SampleRate;
            VoiceDetails = new VoiceDetails
            {
                InputChannelCount = sourceFormat.Channels,
                InputSampleRate = sourceFormat.SampleRate,
                ActiveFlags = (int)flags,
            };

            ResolveAlFormat(
                sourceFormat,
                engine.FloatExtensionAvailable,
                out m_alFormatRaw,
                out m_convertF32ToS16
            );
            ResolveAlFormat(
                sourceFormat.Encoding,
                sourceFormat.BitsPerSample,
                2,
                engine.FloatExtensionAvailable,
                out m_alFormatRawStereo,
                out m_convertF32ToS16Stereo
            );

            lock (engine.AlLock)
            {
                m_source = m_al.GenSource();
                // Default: listener-relative at origin so non-3D sounds (UI, music)
                // play with no spatial colouration. X3DAudio.Calculate flips this to
                // world-positioned when an emitter is supplied.
                ResetSourceToRelative2DLocked();
                m_al.SetSourceProperty(m_source, SourceFloat.Gain, 1f);
                m_al.SetSourceProperty(m_source, SourceFloat.Pitch, 1f);
                // Looping is owned by our shim (intro/loop/outro pattern), not by AL.
                m_al.SetSourceProperty(m_source, SourceBoolean.Looping, false);
            }
        }

        private static void ResolveAlFormat(
            WaveFormat fmt,
            bool floatExt,
            out int alFormatRaw,
            out bool convertF32ToS16
        )
        {
            ResolveAlFormat(
                fmt.Encoding,
                fmt.BitsPerSample,
                fmt.Channels,
                floatExt,
                out alFormatRaw,
                out convertF32ToS16
            );
        }

        private static void ResolveAlFormat(
            WaveFormatEncoding encoding,
            int bitsPerSample,
            int channels,
            bool floatExt,
            out int alFormatRaw,
            out bool convertF32ToS16
        )
        {
            convertF32ToS16 = false;
            bool mono = channels == 1;
            bool isFloat = encoding == WaveFormatEncoding.IeeeFloat;
            if (isFloat)
            {
                if (floatExt)
                {
                    alFormatRaw = mono ? AL_FORMAT_MONO_FLOAT32 : AL_FORMAT_STEREO_FLOAT32;
                }
                else
                {
                    // Fall back to F32-to-S16 conversion before upload.
                    alFormatRaw = mono ? (int)BufferFormat.Mono16 : (int)BufferFormat.Stereo16;
                    convertF32ToS16 = true;
                }
                return;
            }

            switch (bitsPerSample)
            {
                case 8:
                    alFormatRaw = mono ? (int)BufferFormat.Mono8 : (int)BufferFormat.Stereo8;
                    return;
                case 16:
                default:
                    alFormatRaw = mono ? (int)BufferFormat.Mono16 : (int)BufferFormat.Stereo16;
                    return;
            }
        }

        public override void SetVolume(float volume)
        {
            base.SetVolume(volume);
            ApplyGain();
        }

        protected override void OnVolumeGraphChanged()
        {
            base.OnVolumeGraphChanged();
            ApplyGain();
        }

        public void SubmitSourceBuffer(AudioBuffer buffer, uint[] _)
        {
            if (buffer == null)
            {
                return;
            }
            lock (m_sync)
            {
                // Streaming buffers go directly to AL; m_buffers owns static loop data.
                bool isLooping = GetLoopBufferIndexLocked() >= 0;
                if (m_started && !m_stopRequested && !isLooping)
                {
                    m_streaming = true;
                    m_state.BuffersQueued++;
                    if (m_source != 0)
                    {
                        PutBuffer(buffer);
                        EnsurePlayingLocked();
                    }
                }
                else
                {
                    m_buffers.Add(buffer);
                    m_state.BuffersQueued = m_buffers.Count;
                }
            }
        }

        public void Start()
        {
            bool needPlay = false;
            lock (m_sync)
            {
                if (m_started || m_source == 0 || m_buffers.Count == 0)
                {
                    return;
                }
                m_started = true;
                m_stopRequested = false;
                m_streaming = false;
                m_loopReplayEnabled = GetLoopBufferIndexLocked() >= 0;
                m_loopCallbackDeferred = false;
                m_alBufferTracked.Clear();
                m_pushedBufferSizes.Clear();
                m_watchToken?.Cancel();
                QueueInitialBuffers();
                ApplyGainLocked();
                ApplyFrequencyRatioLocked();
                needPlay = true;
            }
            if (needPlay)
            {
                lock (m_engine.AlLock)
                {
                    if (m_source != 0)
                        m_al.SourcePlay(m_source);
                }
                m_watchToken = new CancellationTokenSource();
                _ = WatchPlaybackAsync(m_watchToken.Token);
            }
        }

        public void Stop()
        {
            m_stopRequested = true;
            m_watchToken?.Cancel();
            lock (m_sync)
            {
                if (m_source != 0)
                {
                    lock (m_engine.AlLock)
                    {
                        m_al.SourceStop(m_source);
                        DrainAllBuffersLocked();
                        ResetSourceToRelative2DLocked();
                    }
                }
            }
        }

        public void FlushSourceBuffers()
        {
            m_stopRequested = true;
            m_watchToken?.Cancel();
            lock (m_sync)
            {
                if (m_source != 0)
                {
                    lock (m_engine.AlLock)
                    {
                        m_al.SourceStop(m_source);
                        DrainAllBuffersLocked();
                        ResetSourceToRelative2DLocked();
                    }
                }
                m_buffers.Clear();
            }
            CompleteBuffers();
        }

        public void SetFrequencyRatio(float ratio)
        {
            m_frequencyRatio = ratio;
            ApplyFrequencyRatio();
        }

        public override void SetOutputMatrix(
            Voice _,
            int sourceChannels,
            int destinationChannels,
            float[] matrix
        )
        {
            // Apply3D calls Calculate then SetOutputMatrix on one thread, allowing
            // emitter data to use [ThreadStatic] storage.
            var emitter = global::SharpDX.X3DAudio.X3DAudio.ConsumeLastEmitter();
            float gainFromMatrix = 1f;
            RoutingMode routingMode = RoutingMode.Direct;
            int cellCount = 0;
            if (
                matrix != null
                && matrix.Length > 0
                && sourceChannels > 0
                && destinationChannels > 0
            )
            {
                cellCount = Math.Min(matrix.Length, sourceChannels * destinationChannels);
                if (
                    emitter.Valid
                    && sourceChannels == 1
                    && TryGetUniformMatrixGain(matrix, cellCount, out gainFromMatrix)
                )
                {
                    routingMode = RoutingMode.SpatialMono3D;
                }
                else if (
                    TryMatchFallbackMonoMatrix(
                        matrix,
                        sourceChannels,
                        destinationChannels,
                        out gainFromMatrix
                    )
                )
                {
                    routingMode = RoutingMode.SoftwareMonoToStereoHalf;
                }
                else if (
                    TryMatchFallbackStereoMatrix(
                        matrix,
                        sourceChannels,
                        destinationChannels,
                        out gainFromMatrix
                    )
                )
                {
                    routingMode = RoutingMode.Direct;
                }
                else if (
                    emitter.Valid && TryGetUniformMatrixGain(matrix, cellCount, out gainFromMatrix)
                )
                {
                    // Conservative path for non-mono 3D sounds: keep distance attenuation,
                    // but do not attempt fake per-channel spatial routing with a scalar.
                    routingMode = RoutingMode.Direct;
                }
            }

            lock (m_sync)
            {
                m_matrixGain = gainFromMatrix;
                m_routingMode = routingMode;
                StoreOutputMatrix(matrix, sourceChannels, destinationChannels, cellCount);

                if (m_source != 0)
                {
                    lock (m_engine.AlLock)
                    {
                        if (routingMode == RoutingMode.SpatialMono3D && emitter.Valid)
                        {
                            ConfigureSpatialMono3DLocked();
                            m_al.SetSourceProperty(m_source, SourceBoolean.SourceRelative, false);
                            m_al.SetSourceProperty(
                                m_source,
                                SourceVector3.Position,
                                emitter.X,
                                emitter.Y,
                                emitter.Z
                            );
                            m_al.SetSourceProperty(
                                m_source,
                                SourceVector3.Velocity,
                                emitter.VX,
                                emitter.VY,
                                emitter.VZ
                            );
                        }
                        else
                        {
                            // Voices are pooled. If a recycled voice last played a 3D cue,
                            // force it back to listener-relative 2D before reuse.
                            ResetSourceToRelative2DLocked();
                            ConfigureDirectPlaybackLocked();
                        }
                    }
                }
            }
            ApplyGain();
        }

        public override void GetOutputMatrix(
            Voice _,
            int sourceChannels,
            int destinationChannels,
            float[] matrix
        )
        {
            if (matrix == null)
            {
                return;
            }
            int requestedCellCount = Math.Min(
                matrix.Length,
                Math.Max(0, sourceChannels * destinationChannels)
            );
            Array.Clear(matrix, 0, requestedCellCount);
            if (requestedCellCount <= 0)
            {
                return;
            }

            if (
                sourceChannels == m_lastOutputMatrixSourceChannels
                && destinationChannels == m_lastOutputMatrixDestinationChannels
                && m_lastOutputMatrix.Length > 0
            )
            {
                Array.Copy(
                    m_lastOutputMatrix,
                    matrix,
                    Math.Min(requestedCellCount, m_lastOutputMatrix.Length)
                );
            }
            else if (matrix.Length > 0)
            {
                matrix[0] = m_matrixGain;
            }
        }

        public override void DestroyVoice()
        {
            FlushSourceBuffers();
        }

        protected override void Dispose(bool disposing)
        {
            m_watchToken?.Cancel();
            lock (m_sync)
            {
                if (m_source != 0)
                {
                    lock (m_engine.AlLock)
                    {
                        m_al.SourceStop(m_source);
                        DrainAllBuffersLocked();
                        ResetSourceToRelative2DLocked();
                        m_al.DeleteSource(m_source);
                    }
                    m_source = 0;
                    m_alBufferQueue.Clear();
                    m_alBufferTracked.Clear();
                    m_loopCallbackDeferred = false;
                }
            }
            base.Dispose(disposing);
        }

        private void QueueInitialBuffers()
        {
            if (m_buffers.Count == 0)
            {
                return;
            }
            int loopBufferIndex = GetLoopBufferIndexLocked();
            if (loopBufferIndex >= 0)
            {
                for (int i = 0; i <= loopBufferIndex; i++)
                {
                    bool isLoopBuffer = i == loopBufferIndex;
                    // Defer the loop buffer's sole BufferEnd until all iterations finish.
                    PutBuffer(m_buffers[i], trackCallback: !isLoopBuffer);
                    if (isLoopBuffer)
                        m_loopCallbackDeferred = true;
                }
            }
            else
            {
                for (int i = 0; i < m_buffers.Count; i++)
                {
                    PutBuffer(m_buffers[i]);
                }
            }
        }

        private async Task WatchPlaybackAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && !IsDisposed)
                {
                    await Task.Delay(20, token).ConfigureAwait(false);
                    if (IsDisposed || token.IsCancellationRequested)
                    {
                        return;
                    }

                    bool shouldExit = false;
                    bool needRestart = false;
                    bool fireDeferredLoopCallback = false;
                    int trackedCallbacksToFire = 0;
                    lock (m_sync)
                    {
                        if (m_source == 0)
                            return;

                        int processed;
                        int queued;
                        int sourceState;
                        lock (m_engine.AlLock)
                        {
                            m_al.GetSourceProperty(
                                m_source,
                                GetSourceInteger.BuffersProcessed,
                                out processed
                            );
                            m_al.GetSourceProperty(
                                m_source,
                                GetSourceInteger.BuffersQueued,
                                out queued
                            );
                            m_al.GetSourceProperty(
                                m_source,
                                GetSourceInteger.SourceState,
                                out sourceState
                            );

                            for (int i = 0; i < processed; i++)
                            {
                                uint[] tmp = new uint[1];
                                m_al.SourceUnqueueBuffers(m_source, tmp);
                                m_al.DeleteBuffer(tmp[0]);
                                if (m_alBufferQueue.Count > 0)
                                    m_alBufferQueue.Dequeue();
                            }
                        }

                        // Only tracked game buffers fire BufferEnd. Loop re-feeds must not
                        // decrement m_activeSourceBuffers or recycle an active voice.
                        // m_alBufferTracked controls callbacks; size data is bookkeeping only.
                        for (int i = 0; i < processed; i++)
                        {
                            bool tracked =
                                m_alBufferTracked.Count > 0 && m_alBufferTracked.Dequeue();
                            if (tracked)
                            {
                                if (m_pushedBufferSizes.Count > 0)
                                    m_pushedBufferSizes.Dequeue();
                                trackedCallbacksToFire++;
                                if (m_state.BuffersQueued > 0)
                                {
                                    m_state.BuffersQueued--;
                                }
                            }
                        }

                        int loopBufferIndex = m_loopReplayEnabled ? GetLoopBufferIndexLocked() : -1;
                        bool isLooping = loopBufferIndex >= 0;

                        int queuedAfter = queued - processed;
                        var stateEnum = (SourceState)sourceState;

                        if (!m_stopRequested && isLooping && queuedAfter <= 1)
                        {
                            // Re-feed the loop before its last queued AL buffer drains.
                            PutBuffer(m_buffers[loopBufferIndex], trackCallback: false);
                            // OpenAL does not auto-resume a stopped source after re-feed.
                            if (stateEnum == SourceState.Stopped)
                            {
                                needRestart = true;
                            }
                        }
                        else if (m_stopRequested && isLooping)
                        {
                            int outroBufferIndex = loopBufferIndex + 1;
                            if (outroBufferIndex < m_buffers.Count)
                            {
                                PutBuffer(m_buffers[outroBufferIndex]);
                                if (stateEnum == SourceState.Stopped)
                                {
                                    needRestart = true;
                                }
                            }
                            m_loopReplayEnabled = false;

                            // Match XAudio2's deferred BufferEnd when looping stops.
                            if (m_loopCallbackDeferred)
                            {
                                m_loopCallbackDeferred = false;
                                fireDeferredLoopCallback = true;
                                if (m_state.BuffersQueued > 0)
                                    m_state.BuffersQueued--;
                            }
                        }

                        if (stateEnum == SourceState.Stopped && !needRestart)
                        {
                            if (m_streaming && queuedAfter > 0)
                            {
                                // Resume after a streaming underrun.
                                lock (m_engine.AlLock)
                                    m_al.SourcePlay(m_source);
                            }
                            else if (queuedAfter == 0 && !m_streaming && !m_loopReplayEnabled)
                            {
                                // A non-looping natural drain ends the watcher.
                                m_started = false;
                                m_buffers.Clear();
                                m_state.BuffersQueued = 0;
                                shouldExit = true;
                            }
                        }

                        if (needRestart)
                        {
                            lock (m_engine.AlLock)
                                m_al.SourcePlay(m_source);
                        }
                    }

                    // Fire BufferEnd outside m_sync because callbacks can re-enter
                    // the shim (e.g. SubmitSourceBuffer), and holding m_sync would
                    // deadlock the lock-on-itself path.
                    if (fireDeferredLoopCallback)
                    {
                        this.BufferEnd?.Invoke(IntPtr.Zero);
                    }
                    for (int i = 0; i < trackedCallbacksToFire; i++)
                    {
                        this.BufferEnd?.Invoke(IntPtr.Zero);
                    }

                    if (shouldExit)
                    {
                        return;
                    }
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[LinuxCompat] WatchPlaybackAsync CRASHED: {ex}");
                Console.Error.Flush();
            }
        }

        // Requires m_sync. AL operations take the engine lock internally.
        // trackCallback: true for game-submitted buffers (fire BufferEnd when
        // consumed), false for internally re-fed loop buffers (silently consumed).
        private unsafe void PutBuffer(AudioBuffer buffer, bool trackCallback = true)
        {
            if (m_source == 0)
                return;

            // Voice chat uses Data; static waves may provide only Stream and AudioBytes.
            byte[] managed = buffer.Data;
            int len;
            byte* unmanaged = null;
            if (managed != null && managed.Length > 0)
            {
                len = managed.Length;
            }
            else if (buffer.Stream != null && buffer.AudioBytes > 0)
            {
                len = buffer.AudioBytes;
                unmanaged = (byte*)buffer.Stream.DataPointer;
            }
            else
            {
                return;
            }
            if (len <= 0)
                return;

            bool monoToStereoHalf = m_routingMode == RoutingMode.SoftwareMonoToStereoHalf;
            bool monoToStereoFull =
                m_routingMode == RoutingMode.Direct && m_sourceFormat.Channels == 1;
            bool softwareMonoToStereo = monoToStereoHalf || monoToStereoFull;
            int originalLen = len;
            if (softwareMonoToStereo)
            {
                managed = EnsureManagedBytes(managed, unmanaged, len);
                managed = monoToStereoHalf
                    ? RouteMonoToStereoHalf(managed)
                    : RouteMonoToStereo(managed);
                unmanaged = null;
                len = managed.Length;
            }

            int alFormatRaw = softwareMonoToStereo ? m_alFormatRawStereo : m_alFormatRaw;
            bool convertF32ToS16 = softwareMonoToStereo
                ? m_convertF32ToS16Stereo
                : m_convertF32ToS16;
            uint alBuf;
            lock (m_engine.AlLock)
            {
                alBuf = m_al.GenBuffer();
                if (convertF32ToS16)
                {
                    // AL_EXT_float32 is unavailable; convert in place. Two
                    // bytes out per four bytes in; round-half-away-from-zero matches
                    // AVX-style F32-to-S16 conversion.
                    int frames = len / 4;
                    short[] s16 = new short[frames];
                    if (managed != null)
                    {
                        for (int i = 0; i < frames; i++)
                        {
                            float f = BitConverter.ToSingle(managed, i * 4);
                            if (f > 1f)
                                f = 1f;
                            else if (f < -1f)
                                f = -1f;
                            s16[i] = (short)Math.Round(f * 32767f, MidpointRounding.AwayFromZero);
                        }
                    }
                    else
                    {
                        float* fp = (float*)unmanaged;
                        for (int i = 0; i < frames; i++)
                        {
                            float f = fp[i];
                            if (f > 1f)
                                f = 1f;
                            else if (f < -1f)
                                f = -1f;
                            s16[i] = (short)Math.Round(f * 32767f, MidpointRounding.AwayFromZero);
                        }
                    }
                    int byteLen = frames * 2;
                    fixed (short* ptr = s16)
                    {
                        m_al.BufferData(
                            alBuf,
                            (BufferFormat)alFormatRaw,
                            ptr,
                            byteLen,
                            m_sourceFormat.SampleRate
                        );
                    }
                }
                else if (managed != null)
                {
                    fixed (byte* ptr = managed)
                    {
                        m_al.BufferData(
                            alBuf,
                            (BufferFormat)alFormatRaw,
                            ptr,
                            len,
                            m_sourceFormat.SampleRate
                        );
                    }
                }
                else
                {
                    m_al.BufferData(
                        alBuf,
                        (BufferFormat)alFormatRaw,
                        unmanaged,
                        len,
                        m_sourceFormat.SampleRate
                    );
                }
                m_al.SourceQueueBuffers(m_source, new[] { alBuf });
            }
            m_alBufferQueue.Enqueue(alBuf);
            m_alBufferTracked.Enqueue(trackCallback);

            if (trackCallback && buffer.Stream != null)
            {
                m_pushedBufferSizes.Enqueue(len);
            }
        }

        // Requires m_sync and m_engine.AlLock.
        private void DrainAllBuffersLocked()
        {
            if (m_source == 0)
                return;
            // Source must be stopped before unqueueing. Caller is responsible for
            // having issued SourceStop already.
            m_al.GetSourceProperty(m_source, GetSourceInteger.BuffersQueued, out int total);
            if (total > 0)
            {
                uint[] bufs = new uint[total];
                m_al.SourceUnqueueBuffers(m_source, bufs);
                m_al.DeleteBuffers(bufs);
            }
            m_alBufferQueue.Clear();
            m_alBufferTracked.Clear();
            m_pushedBufferSizes.Clear();
        }

        // Requires m_engine.AlLock.
        private void ResetSourceToRelative2DLocked()
        {
            if (m_source == 0)
                return;

            m_al.SetSourceProperty(m_source, SourceBoolean.SourceRelative, true);
            m_al.SetSourceProperty(m_source, SourceVector3.Position, 0f, 0f, 0f);
            m_al.SetSourceProperty(m_source, SourceVector3.Velocity, 0f, 0f, 0f);
        }

        // Requires m_engine.AlLock.
        private void ConfigureDirectPlaybackLocked()
        {
            if (m_source == 0)
                return;

            if (m_engine.SourceSpatializeExtensionAvailable)
            {
                m_al.SetSourceProperty(m_source, (SourceInteger)AL_SOURCE_SPATIALIZE_SOFT, 0);
            }

            if (m_engine.DirectChannelsExtensionAvailable)
            {
                int playbackChannels = GetPlaybackChannelCount();
                int value =
                    playbackChannels > 1
                        ? (
                            m_engine.DirectChannelsRemixExtensionAvailable
                                ? AL_REMIX_UNMATCHED_SOFT
                                : 1
                        )
                        : 0;
                m_al.SetSourceProperty(m_source, (SourceInteger)AL_DIRECT_CHANNELS_SOFT, value);
            }
        }

        // Requires m_engine.AlLock.
        private void ConfigureSpatialMono3DLocked()
        {
            if (m_source == 0)
                return;

            if (m_engine.SourceSpatializeExtensionAvailable)
            {
                m_al.SetSourceProperty(m_source, (SourceInteger)AL_SOURCE_SPATIALIZE_SOFT, 1);
            }

            if (m_engine.DirectChannelsExtensionAvailable)
            {
                m_al.SetSourceProperty(m_source, (SourceInteger)AL_DIRECT_CHANNELS_SOFT, 0);
            }
        }

        private int GetPlaybackChannelCount()
        {
            if (m_routingMode == RoutingMode.SoftwareMonoToStereoHalf)
                return 2;
            if (m_routingMode == RoutingMode.Direct && m_sourceFormat.Channels == 1)
                return 2;
            return m_sourceFormat.Channels;
        }

        private void StoreOutputMatrix(
            float[] matrix,
            int sourceChannels,
            int destinationChannels,
            int cellCount
        )
        {
            m_lastOutputMatrixSourceChannels = sourceChannels;
            m_lastOutputMatrixDestinationChannels = destinationChannels;
            if (matrix == null || cellCount <= 0)
            {
                m_lastOutputMatrix = Array.Empty<float>();
                return;
            }

            if (m_lastOutputMatrix.Length != cellCount)
            {
                m_lastOutputMatrix = new float[cellCount];
            }
            Array.Copy(matrix, m_lastOutputMatrix, cellCount);
        }

        private static bool TryGetUniformMatrixGain(float[] matrix, int cellCount, out float gain)
        {
            gain = 1f;
            if (matrix == null || cellCount <= 0)
                return false;

            float first = matrix[0];
            for (int i = 1; i < cellCount; i++)
            {
                if (Math.Abs(matrix[i] - first) > MatrixTolerance)
                    return false;
            }

            gain = Math.Abs(first);
            return true;
        }

        private static bool TryMatchFallbackMonoMatrix(
            float[] matrix,
            int sourceChannels,
            int destinationChannels,
            out float gain
        )
        {
            gain = 1f;
            if (
                matrix == null
                || sourceChannels != 1
                || destinationChannels != 2
                || matrix.Length < 2
            )
                return false;

            float left = matrix[0];
            float right = matrix[1];
            if (left < -MatrixTolerance || right < -MatrixTolerance)
                return false;
            if (Math.Abs(left - right) > MatrixTolerance)
                return false;
            if (left > 0.5001f)
                return false;

            gain = Math.Max(0f, left * 2f);
            return true;
        }

        private static bool TryMatchFallbackStereoMatrix(
            float[] matrix,
            int sourceChannels,
            int destinationChannels,
            out float gain
        )
        {
            gain = 1f;
            if (
                matrix == null
                || sourceChannels != 2
                || destinationChannels != 2
                || matrix.Length < 4
            )
                return false;

            float ll = matrix[0];
            float rl = matrix[1];
            float lr = matrix[2];
            float rr = matrix[3];
            if (ll < -MatrixTolerance || rr < -MatrixTolerance)
                return false;
            if (Math.Abs(rl) > MatrixTolerance || Math.Abs(lr) > MatrixTolerance)
                return false;
            if (Math.Abs(ll - rr) > MatrixTolerance)
                return false;

            gain = Math.Max(0f, ll);
            return true;
        }

        private static unsafe byte[] EnsureManagedBytes(byte[] managed, byte* unmanaged, int len)
        {
            if (managed != null)
                return managed;

            var copy = new byte[len];
            if (len > 0 && unmanaged != null)
            {
                Marshal.Copy((IntPtr)unmanaged, copy, 0, len);
            }
            return copy;
        }

        private byte[] RouteMonoToStereoHalf(byte[] monoData)
        {
            if (monoData == null || monoData.Length == 0)
                return Array.Empty<byte>();

            if (m_sourceFormat.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                int frames = monoData.Length / 4;
                var stereo = new byte[frames * 8];
                for (int i = 0; i < frames; i++)
                {
                    float sample = BitConverter.ToSingle(monoData, i * 4) * 0.5f;
                    var bytes = BitConverter.GetBytes(sample);
                    Buffer.BlockCopy(bytes, 0, stereo, i * 8, 4);
                    Buffer.BlockCopy(bytes, 0, stereo, i * 8 + 4, 4);
                }
                return stereo;
            }

            switch (m_sourceFormat.BitsPerSample)
            {
                case 8:
                {
                    int frames = monoData.Length;
                    var stereo = new byte[frames * 2];
                    for (int i = 0; i < frames; i++)
                    {
                        int centered = monoData[i] - 128;
                        byte scaled = (byte)
                            Math.Clamp((int)Math.Round(centered * 0.5f + 128f), 0, 255);
                        stereo[i * 2] = scaled;
                        stereo[i * 2 + 1] = scaled;
                    }
                    return stereo;
                }
                case 16:
                default:
                {
                    int frames = monoData.Length / 2;
                    var stereo = new byte[frames * 4];
                    for (int i = 0; i < frames; i++)
                    {
                        short sample = BitConverter.ToInt16(monoData, i * 2);
                        short scaled = (short)
                            Math.Clamp(
                                (int)Math.Round(sample * 0.5f),
                                short.MinValue,
                                short.MaxValue
                            );
                        var bytes = BitConverter.GetBytes(scaled);
                        Buffer.BlockCopy(bytes, 0, stereo, i * 4, 2);
                        Buffer.BlockCopy(bytes, 0, stereo, i * 4 + 2, 2);
                    }
                    return stereo;
                }
            }
        }

        // Direct-mode {1,1} mono needs full-amplitude stereo duplication; OpenAL's
        // centered mono mix is otherwise about three times quieter than Windows.
        private byte[] RouteMonoToStereo(byte[] monoData)
        {
            if (monoData == null || monoData.Length == 0)
                return Array.Empty<byte>();

            if (m_sourceFormat.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                int frames = monoData.Length / 4;
                var stereo = new byte[frames * 8];
                for (int i = 0; i < frames; i++)
                {
                    Buffer.BlockCopy(monoData, i * 4, stereo, i * 8, 4);
                    Buffer.BlockCopy(monoData, i * 4, stereo, i * 8 + 4, 4);
                }
                return stereo;
            }

            switch (m_sourceFormat.BitsPerSample)
            {
                case 8:
                {
                    int frames = monoData.Length;
                    var stereo = new byte[frames * 2];
                    for (int i = 0; i < frames; i++)
                    {
                        stereo[i * 2] = monoData[i];
                        stereo[i * 2 + 1] = monoData[i];
                    }
                    return stereo;
                }
                case 16:
                default:
                {
                    int frames = monoData.Length / 2;
                    var stereo = new byte[frames * 4];
                    for (int i = 0; i < frames; i++)
                    {
                        Buffer.BlockCopy(monoData, i * 2, stereo, i * 4, 2);
                        Buffer.BlockCopy(monoData, i * 2, stereo, i * 4 + 2, 2);
                    }
                    return stereo;
                }
            }
        }

        // Requires m_sync.
        private void EnsurePlayingLocked()
        {
            if (m_source == 0)
                return;
            lock (m_engine.AlLock)
            {
                m_al.GetSourceProperty(m_source, GetSourceInteger.SourceState, out int state);
                if ((SourceState)state != SourceState.Playing)
                {
                    m_al.SourcePlay(m_source);
                }
            }
        }

        private void ApplyGain()
        {
            lock (m_sync)
            {
                ApplyGainLocked();
            }
        }

        // Requires m_sync.
        private void ApplyGainLocked()
        {
            if (m_source == 0 || IsDisposed)
            {
                return;
            }
            base.GetVolume(out var ownVolume);
            float outputEff = OutputVoice?.EffectiveVolume ?? 1f;
            float gain = Math.Max(0f, m_paused ? 0f : ownVolume * outputEff * m_matrixGain);
            lock (m_engine.AlLock)
                m_al.SetSourceProperty(m_source, SourceFloat.Gain, gain);
        }

        private void ApplyFrequencyRatio()
        {
            lock (m_sync)
            {
                ApplyFrequencyRatioLocked();
            }
        }

        // Requires m_sync.
        private void ApplyFrequencyRatioLocked()
        {
            if (m_source == 0 || IsDisposed)
            {
                return;
            }
            float ratio = Math.Clamp(m_frequencyRatio, 0.01f, 100f);
            lock (m_engine.AlLock)
                m_al.SetSourceProperty(m_source, SourceFloat.Pitch, ratio);
        }

        private void CompleteBuffers()
        {
            int callbacks;
            lock (m_sync)
            {
                callbacks = m_state.BuffersQueued;
                m_state.BuffersQueued = 0;
                m_started = false;
                m_streaming = false;
                m_loopReplayEnabled = false;
                m_loopCallbackDeferred = false;
                m_alBufferTracked.Clear();
                m_pushedBufferSizes.Clear();
            }
            for (int i = 0; i < callbacks; i++)
            {
                this.BufferEnd?.Invoke(IntPtr.Zero);
            }
        }

        // Requires m_sync.
        private int GetLoopBufferIndexLocked()
        {
            for (int i = 0; i < m_buffers.Count; i++)
            {
                if (m_buffers[i].LoopCount > 0)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}

namespace SharpDX.XAPO
{
    // Stub interface matching real SharpDX.XAPO.AudioProcessor so the IL in
    // VRage.Audio binds EffectDescriptor..ctor(AudioProcessor, int32) and
    // passes Reverb/MasteringLimiter as AudioProcessor.
    public interface AudioProcessor { }

    // The CLR binds `AudioProcessorParamNative<T>.get_Parameter()` from game IL.
    // Inheriting the real CppObject also supplies the Dispose(bool) virtual slot
    // used by `callvirt SharpDX.DisposeBase::Dispose()`.
    public abstract unsafe class AudioProcessorParamNative<T>
        : global::SharpDX.CppObject,
            AudioProcessor
        where T : struct
    {
        private T m_parameter;

        protected AudioProcessorParamNative(global::SharpDX.XAudio2.XAudio2 _)
        {
            _nativePointer = (void*)1;
        }

        public T Parameter
        {
            get { return m_parameter; }
            set { m_parameter = value; }
        }

        protected override void Dispose(bool disposing)
        {
            _nativePointer = null;
        }
    }
}

namespace SharpDX.XAudio2.Fx
{
    // Closes the generic base like the real SharpDX.XAudio2.Fx.ReverbParameters
    // does. Stock VRage.Audio never touches its fields (SetReverbParameters is
    // an empty stub in this game version), so no members are required.
    public struct ReverbParameters { }

    // Preserve the SharpDX hierarchy so DisposeBase.Dispose() dispatches through
    // the expected Dispose(bool) virtual slot.
    public sealed class Reverb : global::SharpDX.XAPO.AudioProcessorParamNative<ReverbParameters>
    {
        public Reverb(global::SharpDX.XAudio2.XAudio2 engine)
            : base(engine) { }
    }
}

namespace SharpDX.XAPO.Fx
{
    public struct MasteringLimiterParameters
    {
        public int Loudness;
    }

    public sealed class MasteringLimiter
        : global::SharpDX.XAPO.AudioProcessorParamNative<MasteringLimiterParameters>
    {
        public MasteringLimiter(global::SharpDX.XAudio2.XAudio2 engine)
            : base(engine) { }
    }
}

namespace SharpDX.XAudio2
{
    public class ErrorEventArgs : EventArgs
    {
        public ErrorEventArgs(Exception error)
        {
            Error = error;
            ErrorCode = new global::SharpDX.Result(-1);
        }

        public Exception Error { get; }

        public global::SharpDX.Result ErrorCode { get; }
    }
}

namespace SharpDX.X3DAudio
{
    using SharpDX.Mathematics.Interop;

    public struct CurvePoint
    {
        public float Distance;

        public float DspSetting;
    }

    // Must be a class so the CLR can resolve Emitter.Cone's exact FieldRef.
    public sealed class Cone { }

    [Flags]
    public enum CalculateFlags
    {
        Matrix = 1,
        Doppler = 2,
        RedirectToLfe = 4,
    }

    public enum X3DAudioVersion
    {
        Default,
    }

    public sealed class Listener
    {
        public RawVector3 Position;

        public RawVector3 Velocity;

        public RawVector3 OrientFront;

        public RawVector3 OrientTop;
    }

    public sealed class Emitter
    {
        public RawVector3 Position;

        public RawVector3 Velocity;

        public RawVector3 OrientFront;

        public RawVector3 OrientTop;

        public int ChannelCount;

        public float CurveDistanceScaler;

        // The field type must match VRage.Audio's Cone FieldRef.
        public Cone Cone;

        public CurvePoint[] VolumeCurve;

        public float InnerRadius;

        public float InnerRadiusAngle;

        public float DopplerScaler;
    }

    // These must be fields because VRage.Audio accesses them with ldfld/stfld.
    public sealed class DspSettings
    {
        public DspSettings(int sourceChannelCount, int destinationChannelCount)
        {
            SourceChannelCount = sourceChannelCount;
            DestinationChannelCount = destinationChannelCount;
            MatrixCoefficients = new float[sourceChannelCount * destinationChannelCount];
            DelayTimes = new float[destinationChannelCount];
            DopplerFactor = 1f;
        }

        public readonly float[] MatrixCoefficients;

        public readonly float[] DelayTimes;

        public readonly int SourceChannelCount;

        public readonly int DestinationChannelCount;

        public float LpfDirectCoefficient;

        public float LpfReverbCoefficient;

        public float ReverbLevel;

        public float DopplerFactor;

        public float EmitterToListenerAngle;

        public float EmitterToListenerDistance;

        public float EmitterVelocityComponent;

        public float ListenerVelocityComponent;
    }

    public sealed unsafe class X3DAudio
    {
        // X3DAudio has no engine reference, so XAudio2 publishes its AL handle and lock.
        internal static AL GlobalAl;
        internal static object GlobalAlLock;

        internal struct EmitterData
        {
            public float X,
                Y,
                Z;
            public float VX,
                VY,
                VZ;
            public bool Valid;
        }

        // Handoff from Calculate to SourceVoice.SetOutputMatrix. The game's
        // MyXAudio2.Apply3D calls them in immediate succession on the same thread,
        // so a [ThreadStatic] field needs no queue.
        [ThreadStatic]
        private static EmitterData s_lastEmitter;

        internal static EmitterData ConsumeLastEmitter()
        {
            var data = s_lastEmitter;
            s_lastEmitter = default;
            return data;
        }

        public X3DAudio(Speakers speakers)
            : this(speakers, X3DAudioVersion.Default) { }

        public X3DAudio(Speakers _, float __) { }

        public X3DAudio(Speakers _, X3DAudioVersion __) { }

        public void Calculate(
            Listener listener,
            Emitter emitter,
            CalculateFlags flags,
            DspSettings dspSettings
        )
        {
            // The game uses this distance to attenuate MatrixCoefficients.
            float dx = emitter.Position.X - listener.Position.X;
            float dy = emitter.Position.Y - listener.Position.Y;
            float dz = emitter.Position.Z - listener.Position.Z;
            float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            dspSettings.EmitterToListenerDistance = distance;

            // OpenAL handles spatial panning. Matrix cells carry only the attenuation
            // scalar consumed by the game's falloff and AL_GAIN path.
            float attenuation = EvaluateCurve(
                emitter.VolumeCurve,
                emitter.CurveDistanceScaler,
                distance
            );
            if (flags.HasFlag(CalculateFlags.Matrix))
            {
                for (int i = 0; i < dspSettings.MatrixCoefficients.Length; i++)
                {
                    dspSettings.MatrixCoefficients[i] = attenuation;
                }
            }

            // The game applies DopplerFactor through SetFrequencyRatio; OpenAL Doppler is disabled.
            if (flags.HasFlag(CalculateFlags.Doppler) && distance > 1e-4f)
            {
                float invDist = 1f / distance;
                float dirX = dx * invDist,
                    dirY = dy * invDist,
                    dirZ = dz * invDist;
                float listenerVel =
                    listener.Velocity.X * dirX
                    + listener.Velocity.Y * dirY
                    + listener.Velocity.Z * dirZ;
                float emitterVel =
                    emitter.Velocity.X * dirX
                    + emitter.Velocity.Y * dirY
                    + emitter.Velocity.Z * dirZ;
                dspSettings.ListenerVelocityComponent = listenerVel;
                dspSettings.EmitterVelocityComponent = emitterVel;
                const float speedOfSound = 343.5f;
                float dopplerScaler = emitter.DopplerScaler;
                float denom = speedOfSound + emitterVel * dopplerScaler;
                if (MathF.Abs(denom) < 0.01f)
                    denom = MathF.CopySign(0.01f, denom);
                dspSettings.DopplerFactor = Math.Clamp(
                    (speedOfSound + listenerVel * dopplerScaler) / denom,
                    0.5f,
                    2.0f
                );
            }
            else
            {
                dspSettings.DopplerFactor = 1f;
            }

            // Apply3D updates the OpenAL listener every audio frame.
            var al = GlobalAl;
            var alLock = GlobalAlLock;
            if (al != null && alLock != null)
            {
                lock (alLock)
                {
                    al.SetListenerProperty(
                        ListenerVector3.Position,
                        listener.Position.X,
                        listener.Position.Y,
                        listener.Position.Z
                    );
                    al.SetListenerProperty(
                        ListenerVector3.Velocity,
                        listener.Velocity.X,
                        listener.Velocity.Y,
                        listener.Velocity.Z
                    );
                    // SE and OpenAL use right-handed -Z forward. The game negates camera
                    // forward for X3DAudio, so negate OrientFront again for OpenAL.
                    float* ori = stackalloc float[6]
                    {
                        -listener.OrientFront.X,
                        -listener.OrientFront.Y,
                        -listener.OrientFront.Z,
                        listener.OrientTop.X,
                        listener.OrientTop.Y,
                        listener.OrientTop.Z,
                    };
                    al.SetListenerProperty(ListenerFloatArray.Orientation, ori);
                }
            }

            // SetOutputMatrix consumes this emitter on the same thread.
            s_lastEmitter = new EmitterData
            {
                X = emitter.Position.X,
                Y = emitter.Position.Y,
                Z = emitter.Position.Z,
                VX = emitter.Velocity.X,
                VY = emitter.Velocity.Y,
                VZ = emitter.Velocity.Z,
                Valid = true,
            };
        }

        private static float EvaluateCurve(CurvePoint[] points, float maxDistance, float distance)
        {
            if (points == null || points.Length == 0 || maxDistance <= 0f)
            {
                return 1f;
            }
            float normalized = Math.Clamp(distance / maxDistance, 0f, 1f);
            CurvePoint previous = points[0];
            for (int i = 1; i < points.Length; i++)
            {
                CurvePoint current = points[i];
                if (normalized <= current.Distance)
                {
                    float range = current.Distance - previous.Distance;
                    if (range <= 0f)
                    {
                        return current.DspSetting;
                    }
                    float t = (normalized - previous.Distance) / range;
                    return previous.DspSetting + (current.DspSetting - previous.DspSetting) * t;
                }
                previous = current;
            }
            return points[^1].DspSetting;
        }
    }
}

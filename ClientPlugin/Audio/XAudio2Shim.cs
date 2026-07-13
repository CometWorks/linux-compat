using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ClientPlugin.Audio;
using Silk.NET.OpenAL;
using SharpDX;
using SharpDX.Mathematics.Interop;
using SharpDX.Multimedia;
using VRage.Audio;

namespace SharpDX.XAudio2
{
public enum XAudio2Version
{
	Default
}

public enum DeviceRole
{
	DefaultGameDevice,
	DefaultCommunicationsDevice
}

[Flags]
public enum VoiceFlags
{
	None = 0,
	UseFilter = 1
}

public enum FilterType
{
	LowPassFilter = 0,
	BandPassFilter = 1,
	HighPassFilter = 2,
	NotchFilter = 3
}

[Flags]
public enum BufferFlags
{
	None = 0
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

	// Must match SharpDX.XAudio2.DeviceDetails.OutputFormat (WaveFormatExtensible)
	// so the game's IL (compiled against real SharpDX.XAudio2) binds the field.
	public WaveFormatExtensible OutputFormat;
}

// Struct layout must match stock SharpDX.XAudio2.VoiceDetails exactly:
// VoiceFlags CreationFlags, int ActiveFlags (NOT VoiceFlags — int!),
// int InputChannelCount, int InputSampleRate. Game IL does ldfld/stfld
// with these exact field signatures.
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
	public EffectDescriptor(SharpDX.XAPO.AudioProcessor effect)
	{
	}

	public EffectDescriptor(SharpDX.XAPO.AudioProcessor effect, int _)
	{
	}
}

[Flags]
public enum VoiceSendFlags
{
	None = 0
}

// Fields (not properties): stock VRage.Audio IL accesses
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

	// Must be a field, not a property: stock VRage.Audio.dll IL in
	// MySourceVoice.SubmitBuffer(byte[]) reads it via `ldfld` inside its
	// catch-block diagnostic log. As a get-only property it threw
	// MissingFieldException on the first received voice-chat packet,
	// crashing the game (see Docs/Fixes.md AudioBuffer.AudioDataPointer
	// entry). Real SharpDX.XAudio2.AudioBuffer declares it as a public
	// field; the shim never needs a non-zero value because our
	// SubmitSourceBuffer reads Stream/Data, not the pointer.
	public IntPtr AudioDataPointer;

	public AudioBuffer()
	{
	}

	public AudioBuffer(DataStream stream)
	{
		Stream = stream;

		// Voice chat path: stock VRage.Audio.MySourceVoice.SubmitBuffer(byte[])
		// creates a DataStream over the byte[] payload and constructs the
		// AudioBuffer via this ctor. Our SourceVoice.PutBuffer reads only
		// the managed `Data` field (SDL_PutAudioStreamData takes a byte[]),
		// so unless we copy the bytes out now they will be silently lost.
		//
		// We copy upfront — not lazily inside PutBuffer — because by the
		// time PutBuffer fires, the originating DataStream may already
		// have been dequeued and disposed by MySourceVoice.OnStopPlayingBuffered
		// (or its underlying pinned byte[] reused for a later packet).
		// During this ctor the stream has just been created in
		// SubmitBuffer and is guaranteed alive.
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

// Must inherit from SharpDX.CppObject (which itself extends DisposeBase):
// stock VRage.Audio.dll IL uses `callvirt SharpDX.CppObject::get_NativePointer()`
// on voice fields in MySourceVoice.IsNativeValid and VoiceExtensions.IsValid.
// That getter reads CppObject._nativePointer directly — if we inherit DisposeBase
// only, the getter silently returns IntPtr.Zero and IsValid becomes permanently
// false, so MySourceVoice.Start early-returns and no sound ever plays.
// We keep the CppObject base "alive" by setting `_nativePointer = (void*)1` in
// the ctor and back to zero in Dispose.
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
		get
		{
			return m_outputVoice;
		}
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

	public virtual void SetEffectChain(EffectDescriptor[] _)
	{
	}

	public virtual void DisableEffect(int _)
	{
	}

	public virtual void EnableEffect(int _)
	{
	}

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
		OutputVoice = descriptors != null && descriptors.Length != 0 ? descriptors[0].OutputVoice : null;
	}

	// Must be on Voice (not SourceVoice) — stock VRage.Audio IL in
	// MyEffectInstance.Update calls
	// `callvirt SharpDX.XAudio2.Voice::SetFilterParameters(FilterParameters, int32)`.
	public virtual void SetFilterParameters(FilterParameters _)
	{
	}

	public virtual void SetFilterParameters(FilterParameters _, int __)
	{
	}

	// Must be on Voice (not SourceVoice) — game IL binds the MethodRef
	// against the declaring type Voice, regardless of runtime type.
	public virtual void SetOutputMatrix(Voice destinationVoice, int sourceChannels, int destinationChannels, float[] matrix)
	{
	}

	public virtual void GetOutputMatrix(Voice destinationVoice, int sourceChannels, int destinationChannels, float[] matrix)
	{
	}

	public virtual void GetChannelMask(out int channelMask)
	{
		channelMask = (int)MySdlAudioInterop.GetSpeakerMask(VoiceDetails.InputChannelCount);
	}

	// Must be on Voice (not MasteringVoice) — game IL binds the MethodRef
	// against the declaring type Voice, regardless of runtime type.
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

	public virtual void DestroyVoice()
	{
	}

	protected virtual void OnVolumeGraphChanged()
	{
		this.VolumeChanged?.Invoke();
	}

	private void OnParentVolumeChanged()
	{
		OnVolumeGraphChanged();
	}
}

// Must inherit from SharpDX.CppObject (which itself extends DisposeBase):
// stock VRage.Audio.dll IL in MySourceVoice.IsNativeValid reads
// `m_device.NativePointer` via `callvirt SharpDX.CppObject::get_NativePointer()`
// and short-circuits playback if the result is IntPtr.Zero. We keep the base
// `_nativePointer` non-zero while the device is alive.
//
// Backed by OpenAL Soft via Silk.NET.OpenAL. OpenAL handles spatial panning,
// HRTF, and resampling natively; the game's custom distance attenuation and
// Doppler are kept (DistanceModel.None / DopplerFactor 0) so the existing
// volume curves and frequency-ratio math drive AL_GAIN / AL_PITCH directly.
public sealed unsafe class XAudio2 : global::SharpDX.CppObject
{
	// FFmpeg/WAV loading still flows through MySdlAudioInterop, which decodes
	// to format-agnostic byte[]s — so OpenAL receives PCM with no SDL coupling.
	// The output spec is synthesised: OpenAL Soft picks its own device format,
	// but VRage.Audio only reads channel count & sample rate to build
	// WaveFormatExtensible for the game's mixing-graph metadata.
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

	internal bool DirectChannelsRemixExtensionAvailable => m_directChannelsRemixExtensionAvailable;

	internal bool SourceSpatializeExtensionAvailable => m_sourceSpatializeExtensionAvailable;

	public event EventHandler<ErrorEventArgs> CriticalError;

	public XAudio2(XAudio2Version _)
	{
		_nativePointer = (void*)1;

		// FFmpeg bindings are still required by MySdlAudioInterop for WAV/audio
		// decoding; SDL3 audio init is no-op for us but harmless if other paths
		// still touch it (video playback owns its own SDL audio device).
		MySdlAudioInterop.EnsureInitialized();

		m_alc = ALContext.GetApi(soft: true);
		m_al = AL.GetApi(soft: true);
		m_device = m_alc.OpenDevice("");
		if (m_device == null)
		{
			throw new InvalidOperationException("OpenAL: failed to open default playback device (alcOpenDevice returned NULL)");
		}
		m_context = m_alc.CreateContext(m_device, null);
		if (m_context == null)
		{
			m_alc.CloseDevice(m_device);
			throw new InvalidOperationException("OpenAL: failed to create audio context (alcCreateContext returned NULL)");
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
		m_directChannelsRemixExtensionAvailable = m_al.IsExtensionPresent("AL_SOFT_direct_channels_remix");
		m_sourceSpatializeExtensionAvailable = m_al.IsExtensionPresent("AL_SOFT_source_spatialize");

		// VRage.Audio only reads channel count + sample rate from this spec to
		// fill DeviceDetails / WaveFormatExtensible for the game's graph.
		m_outputSpec = new MySdlAudioInterop.SdlAudioSpec
		{
			Format = MySdlAudioInterop.SDL_AUDIO_F32LE,
			Channels = 2,
			Frequency = 48000
		};

		// Publish AL handle for X3DAudio.Calculate (listener properties) — the
		// game constructs X3DAudio independently of XAudio2, so it has no
		// reference; a static plug is the simplest hand-off that survives the
		// game's construction order. Fully qualify because in SharpDX.XAudio2
		// the bare `X3DAudio` resolves to the SharpDX.X3DAudio namespace.
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
			OutputFormat = new WaveFormatExtensible(m_outputSpec.Frequency, 16, m_outputSpec.Channels)
			{
				ChannelMask = MySdlAudioInterop.GetSpeakerMask(m_outputSpec.Channels)
			}
		};
	}

	public DeviceDetails GetDeviceDetails(int _)
	{
		return GetDeviceDetails();
	}

	protected override void Dispose(bool disposing)
	{
		_nativePointer = null;

		// Detach the X3DAudio static plug first so a late Calculate call after
		// dispose won't touch a destroyed AL instance.
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

		// Silk.NET API objects are disposable. OpenAL Soft drains its mixing
		// thread inside alcDestroyContext / alcCloseDevice, so by the time we
		// reach here the audio thread has exited cleanly — no SDL-style
		// use-after-free shutdown crash.
		try { m_al?.Dispose(); } catch { }
		try { m_alc?.Dispose(); } catch { }
	}

	internal void RaiseCriticalError(Exception exception)
	{
		this.CriticalError?.Invoke(this, new ErrorEventArgs(exception));
	}

	public void CommitChanges(int _)
	{
	}

	public void StopEngine()
	{
		// OpenAL Soft owns its mixing thread internally and synchronises
		// shutdown via alcDestroyContext; no equivalent of SDL's PipeWire
		// use-after-free issue, so this can stay a near-no-op.
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
			ActiveFlags = 0
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
			ActiveFlags = 0
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
		SoftwareMonoToStereoHalf
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

	// PCM byte-counts in the same order as tracked AL buffers, used to map
	// AL buffer-end events to the game's BufferEnd callback.
	private readonly Queue<int> m_pushedBufferSizes = new Queue<int>();

	// True while the loop buffer's BufferEnd callback is deferred. In real
	// XAudio2 a buffer with LoopCount>0 fires BufferEnd only once, when the
	// loop ends (voice stopped). Our shim emulates looping by re-queuing AL
	// buffers, so we must defer the callback until the loop actually finishes.
	private bool m_loopCallbackDeferred;

	private readonly List<AudioBuffer> m_buffers = new List<AudioBuffer>();

	private readonly object m_sync = new object();

	private CancellationTokenSource m_watchToken;

	private VoiceState m_state;

	private bool m_started;

	private bool m_paused;

	private bool m_stopRequested;

	private bool m_loopReplayEnabled;

	// True once a buffer was submitted *after* Start() (i.e. streaming mode,
	// e.g. voice chat). For static one-shot playback (sound effects, all
	// buffers submitted up-front) we terminate the watcher on natural drain;
	// for streaming we keep the watcher alive until explicit Stop/Flush/Dispose,
	// because terminating-on-drain races against the next voice packet's
	// m_dataStreams.Enqueue and would orphan the buffer.
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

	public SourceVoice(XAudio2 engine, WaveFormat sourceFormat, bool enableCallbackEvents = true)
		: this(engine, sourceFormat, VoiceFlags.None, 2f, enableCallbackEvents)
	{
	}

	public SourceVoice(XAudio2 engine, WaveFormat sourceFormat, VoiceFlags flags, float _, bool enableCallbackEvents = true)
	{
		m_engine = engine;
		m_al = engine.Al;
		m_sourceFormat = sourceFormat;
		SourceSampleRate = sourceFormat.SampleRate;
		VoiceDetails = new VoiceDetails
		{
			InputChannelCount = sourceFormat.Channels,
			InputSampleRate = sourceFormat.SampleRate,
			ActiveFlags = (int)flags
		};

		ResolveAlFormat(sourceFormat, engine.FloatExtensionAvailable, out m_alFormatRaw, out m_convertF32ToS16);
		ResolveAlFormat(sourceFormat.Encoding, sourceFormat.BitsPerSample, 2, engine.FloatExtensionAvailable,
			out m_alFormatRawStereo, out m_convertF32ToS16Stereo);

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

	private static void ResolveAlFormat(WaveFormat fmt, bool floatExt, out int alFormatRaw, out bool convertF32ToS16)
	{
		ResolveAlFormat(fmt.Encoding, fmt.BitsPerSample, fmt.Channels, floatExt, out alFormatRaw, out convertF32ToS16);
	}

	private static void ResolveAlFormat(WaveFormatEncoding encoding, int bitsPerSample, int channels, bool floatExt,
		out int alFormatRaw, out bool convertF32ToS16)
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
				// Fall back: every PutBuffer will down-convert F32 → S16 before upload.
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
			// Streaming case (voice chat): playback already running, not in
			// looping mode. Push directly to the AL queue — don't grow
			// m_buffers, which is reserved for the static set used by
			// QueueInitialBuffers and the loop-replay logic.
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

	public override void SetOutputMatrix(Voice _, int sourceChannels, int destinationChannels, float[] matrix)
	{
		// Consume any emitter data left by X3DAudio.Calculate on this thread.
		// MyXAudio2.Apply3D is a tightly coupled Calculate → SetOutputMatrix
		// pair on a single thread, so [ThreadStatic] storage is sufficient
		// hand-off without a queue.
		var emitter = global::SharpDX.X3DAudio.X3DAudio.ConsumeLastEmitter();
		float gainFromMatrix = 1f;
		RoutingMode routingMode = RoutingMode.Direct;
		int cellCount = 0;
		if (matrix != null && matrix.Length > 0 && sourceChannels > 0 && destinationChannels > 0)
		{
			cellCount = Math.Min(matrix.Length, sourceChannels * destinationChannels);
			if (emitter.Valid && sourceChannels == 1 && TryGetUniformMatrixGain(matrix, cellCount, out gainFromMatrix))
			{
				routingMode = RoutingMode.SpatialMono3D;
			}
			else if (TryMatchFallbackMonoMatrix(matrix, sourceChannels, destinationChannels, out gainFromMatrix))
			{
				routingMode = RoutingMode.SoftwareMonoToStereoHalf;
			}
			else if (TryMatchFallbackStereoMatrix(matrix, sourceChannels, destinationChannels, out gainFromMatrix))
			{
				routingMode = RoutingMode.Direct;
			}
			else if (emitter.Valid && TryGetUniformMatrixGain(matrix, cellCount, out gainFromMatrix))
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
						m_al.SetSourceProperty(m_source, SourceVector3.Position, emitter.X, emitter.Y, emitter.Z);
						m_al.SetSourceProperty(m_source, SourceVector3.Velocity, emitter.VX, emitter.VY, emitter.VZ);
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

	public override void GetOutputMatrix(Voice _, int sourceChannels, int destinationChannels, float[] matrix)
	{
		if (matrix == null)
		{
			return;
		}
		int requestedCellCount = Math.Min(matrix.Length, Math.Max(0, sourceChannels * destinationChannels));
		Array.Clear(matrix, 0, requestedCellCount);
		if (requestedCellCount <= 0)
		{
			return;
		}

		if (sourceChannels == m_lastOutputMatrixSourceChannels && destinationChannels == m_lastOutputMatrixDestinationChannels
			&& m_lastOutputMatrix.Length > 0)
		{
			Array.Copy(m_lastOutputMatrix, matrix, Math.Min(requestedCellCount, m_lastOutputMatrix.Length));
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
				// The loop buffer is queued as "untracked" — its BufferEnd
				// callback is deferred until the loop actually ends (matching
				// real XAudio2 behaviour where LoopCount>0 buffers fire
				// BufferEnd only once, after all iterations complete).
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
						m_al.GetSourceProperty(m_source, GetSourceInteger.BuffersProcessed, out processed);
						m_al.GetSourceProperty(m_source, GetSourceInteger.BuffersQueued, out queued);
						m_al.GetSourceProperty(m_source, GetSourceInteger.SourceState, out sourceState);

						// Unqueue and delete each processed AL buffer.
						for (int i = 0; i < processed; i++)
						{
							uint[] tmp = new uint[1];
							m_al.SourceUnqueueBuffers(m_source, tmp);
							m_al.DeleteBuffer(tmp[0]);
							if (m_alBufferQueue.Count > 0) m_alBufferQueue.Dequeue();
						}
					}

					// Fire BufferEnd callbacks only for game-submitted ("tracked")
					// buffers. Internally re-fed loop buffers are "untracked" and
					// must be silently consumed — firing BufferEnd for each loop
					// iteration would exhaust the game's m_activeSourceBuffers
					// counter and cause premature voice-pool recycling while the
					// AL source is still playing.
					//
					// Callback count is driven by m_alBufferTracked ALONE — not
					// gated on m_pushedBufferSizes. The size queue is bookkeeping
					// only; an empty size queue while a tracked buffer drains
					// must not suppress its BufferEnd, or the game's
					// m_activeSourceBuffers counter never reaches zero and the
					// voice is leaked from m_availableVoices.
					for (int i = 0; i < processed; i++)
					{
						bool tracked = m_alBufferTracked.Count > 0 && m_alBufferTracked.Dequeue();
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
						// Keep the loop fed: when only one buffer remains queued,
						// re-submit the loop body. (AL buffer count is the unit
						// here, not byte fill — OpenAL doesn't expose queued bytes.)
						// Mark as untracked to suppress BufferEnd callback.
						PutBuffer(m_buffers[loopBufferIndex], trackCallback: false);
						// If the source already underran and went Stopped before
						// our refeed, AL will not auto-resume on the freshly queued
						// buffer — we must re-issue SourcePlay. This is the common
						// case for short intro buffers that drain before the 20ms
						// tick: without this restart, looping sounds (jetpack,
						// drills, music with intro+loop+outro) stay silent forever.
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

						// The loop has ended — fire the deferred BufferEnd for the
						// loop buffer. In real XAudio2 this fires when the looping
						// buffer's LoopCount is exhausted or the voice is stopped.
						if (m_loopCallbackDeferred)
						{
							m_loopCallbackDeferred = false;
							fireDeferredLoopCallback = true;
							if (m_state.BuffersQueued > 0)
								m_state.BuffersQueued--;
						}
					}

					// Source can stop naturally if all buffers have been consumed,
					// either at end-of-stream (one-shot) or on streaming under-run.
					if (stateEnum == SourceState.Stopped && !needRestart)
					{
						if (m_streaming && queuedAfter > 0)
						{
							// Under-run during streaming: re-prime playback so newly
							// submitted buffers continue.
							lock (m_engine.AlLock)
								m_al.SourcePlay(m_source);
						}
						else if (queuedAfter == 0 && !m_streaming && !m_loopReplayEnabled)
						{
							// Natural drain on a non-looping one-shot (sound effect
							// or post-outro). Tear down state and exit. Looping
							// voices never reach here while m_loopReplayEnabled is
							// true — they get refed above.
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

				// Fire BufferEnd outside m_sync — game callbacks can re-enter
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
		catch (TaskCanceledException)
		{
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[LinuxCompat] WatchPlaybackAsync CRASHED: {ex}");
			Console.Error.Flush();
		}
	}

	// Caller MUST hold m_sync. AL operations are taken with the engine lock.
	// trackCallback: true for game-submitted buffers (fire BufferEnd when
	// consumed), false for internally re-fed loop buffers (silently consumed).
	private unsafe void PutBuffer(AudioBuffer buffer, bool trackCallback = true)
	{
		if (m_source == 0)
			return;

		// Resolve the audio bytes. Voice-chat AudioBuffers (constructed via
		// AudioBuffer(DataStream)) eagerly copy the unmanaged stream into
		// `Data`, but statically-loaded MyInMemoryWaves on a non-Linux build
		// only set `Stream` + `AudioBytes`. Read straight from the unmanaged
		// pointer in that case so static cues (music, jetpack loop, drills)
		// don't silently get dropped here.
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
		bool monoToStereoFull = m_routingMode == RoutingMode.Direct && m_sourceFormat.Channels == 1;
		bool softwareMonoToStereo = monoToStereoHalf || monoToStereoFull;
		int originalLen = len;
		if (softwareMonoToStereo)
		{
			managed = EnsureManagedBytes(managed, unmanaged, len);
			managed = monoToStereoHalf ? RouteMonoToStereoHalf(managed) : RouteMonoToStereo(managed);
			unmanaged = null;
			len = managed.Length;
		}

		int alFormatRaw = softwareMonoToStereo ? m_alFormatRawStereo : m_alFormatRaw;
		bool convertF32ToS16 = softwareMonoToStereo ? m_convertF32ToS16Stereo : m_convertF32ToS16;
		uint alBuf;
		lock (m_engine.AlLock)
		{
			alBuf = m_al.GenBuffer();
			if (convertF32ToS16)
			{
				// AL_EXT_float32 missing on this device — convert in-place. Two
				// bytes out per four bytes in; round-half-away-from-zero matches
				// AVX-style F32→S16 conversion.
				int frames = len / 4;
				short[] s16 = new short[frames];
				if (managed != null)
				{
					for (int i = 0; i < frames; i++)
					{
						float f = BitConverter.ToSingle(managed, i * 4);
						if (f > 1f) f = 1f; else if (f < -1f) f = -1f;
						s16[i] = (short)Math.Round(f * 32767f, MidpointRounding.AwayFromZero);
					}
				}
				else
				{
					float* fp = (float*)unmanaged;
					for (int i = 0; i < frames; i++)
					{
						float f = fp[i];
						if (f > 1f) f = 1f; else if (f < -1f) f = -1f;
						s16[i] = (short)Math.Round(f * 32767f, MidpointRounding.AwayFromZero);
					}
				}
				int byteLen = frames * 2;
				fixed (short* ptr = s16)
				{
					m_al.BufferData(alBuf, (BufferFormat)alFormatRaw, ptr, byteLen, m_sourceFormat.SampleRate);
				}
			}
			else if (managed != null)
			{
				fixed (byte* ptr = managed)
				{
					m_al.BufferData(alBuf, (BufferFormat)alFormatRaw, ptr, len, m_sourceFormat.SampleRate);
				}
			}
			else
			{
				m_al.BufferData(alBuf, (BufferFormat)alFormatRaw, unmanaged, len, m_sourceFormat.SampleRate);
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

	// Caller MUST hold both m_sync AND m_engine.AlLock.
	private void DrainAllBuffersLocked()
	{
		if (m_source == 0) return;
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

	// Caller MUST hold m_engine.AlLock.
	private void ResetSourceToRelative2DLocked()
	{
		if (m_source == 0)
			return;

		m_al.SetSourceProperty(m_source, SourceBoolean.SourceRelative, true);
		m_al.SetSourceProperty(m_source, SourceVector3.Position, 0f, 0f, 0f);
		m_al.SetSourceProperty(m_source, SourceVector3.Velocity, 0f, 0f, 0f);
	}

	// Caller MUST hold m_engine.AlLock.
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
			int value = playbackChannels > 1
				? (m_engine.DirectChannelsRemixExtensionAvailable ? AL_REMIX_UNMATCHED_SOFT : 1)
				: 0;
			m_al.SetSourceProperty(m_source, (SourceInteger)AL_DIRECT_CHANNELS_SOFT, value);
		}
	}

	// Caller MUST hold m_engine.AlLock.
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

	private void StoreOutputMatrix(float[] matrix, int sourceChannels, int destinationChannels, int cellCount)
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

	private static bool TryMatchFallbackMonoMatrix(float[] matrix, int sourceChannels, int destinationChannels, out float gain)
	{
		gain = 1f;
		if (matrix == null || sourceChannels != 1 || destinationChannels != 2 || matrix.Length < 2)
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

	private static bool TryMatchFallbackStereoMatrix(float[] matrix, int sourceChannels, int destinationChannels, out float gain)
	{
		gain = 1f;
		if (matrix == null || sourceChannels != 2 || destinationChannels != 2 || matrix.Length < 4)
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
					byte scaled = (byte)Math.Clamp((int)Math.Round(centered * 0.5f + 128f), 0, 255);
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
					short scaled = (short)Math.Clamp((int)Math.Round(sample * 0.5f), short.MinValue, short.MaxValue);
					var bytes = BitConverter.GetBytes(scaled);
					Buffer.BlockCopy(bytes, 0, stereo, i * 4, 2);
					Buffer.BlockCopy(bytes, 0, stereo, i * 4 + 2, 2);
				}
				return stereo;
			}
		}
	}

	// Duplicates mono PCM to stereo at full amplitude. Used for Direct-mode
	// mono sources where the game's output matrix is {1,1} (both speakers at
	// full level). Without this, OpenAL renders center mono to stereo with a
	// significant per-channel reduction, causing ~3× quieter output vs Windows.
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

	// Caller MUST hold m_sync.
	private void EnsurePlayingLocked()
	{
		if (m_source == 0) return;
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

	// Caller MUST hold m_sync.
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

	// Caller MUST hold m_sync.
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

	// Caller MUST hold m_sync.
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
public interface AudioProcessor
{
}

// Stub matching real SharpDX.XAPO.AudioProcessorParamNative<T>. Stock
// VRage.Audio.dll IL accesses `masteringLimiter.Parameter` in
// MyXAudio2.StartEngine(), which in real SharpDX is defined on this
// generic base class as `instance !0 get_Parameter()`. The CLR must be
// able to load the generic type definition before JIT can bind the
// callsite, or StartEngine() throws TypeLoadException and the whole
// audio engine is caught-and-disabled.
//
// Must inherit from the real SharpDX.CppObject (SharpDX.dll is NOT
// assembly-redirected, so CppObject : DisposeBase here is the same type
// the game IL binds against). The real hierarchy is
// AudioProcessorParamNative<T> : AudioProcessorNative : ComObject :
// CppObject : DisposeBase, and stock VRage.Audio IL disposes effects via
// `callvirt SharpDX.DisposeBase::Dispose()` (MyCueBank.Dispose on the
// Reverb, compiled against the declaring type). DisposeBase.Dispose()
// virtually dispatches `Dispose(bool)` through vtable slot 5 — when this
// stub derived from System.Object that slot did not exist and the
// dispatch jumped into type metadata: a deterministic SIGSEGV at
// RIP 0x2281 on world load (MyDefinitionManager.LoadSounds →
// MyXAudio2.ReloadData → MyCueBank.Dispose).
public abstract unsafe class AudioProcessorParamNative<T> : global::SharpDX.CppObject, AudioProcessor where T : struct
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
public struct ReverbParameters
{
}

// Must mirror the real hierarchy (Reverb : AudioProcessorParamNative<
// ReverbParameters>, rooted in the real SharpDX.DisposeBase — see the
// comment on AudioProcessorParamNative<T>). MyCueBank stores this instance
// and MyCueBank.Dispose calls `callvirt SharpDX.DisposeBase::Dispose()` on
// it during MyXAudio2.ReloadData; a System.Object-based stub crashed there
// with SIGSEGV at RIP 0x2281 (missing Dispose(bool) vtable slot).
public sealed class Reverb : global::SharpDX.XAPO.AudioProcessorParamNative<ReverbParameters>
{
	public Reverb(global::SharpDX.XAudio2.XAudio2 engine)
		: base(engine)
	{
	}
}
}

namespace SharpDX.XAPO.Fx
{
public struct MasteringLimiterParameters
{
	public int Loudness;
}

public sealed class MasteringLimiter : global::SharpDX.XAPO.AudioProcessorParamNative<MasteringLimiterParameters>
{
	public MasteringLimiter(global::SharpDX.XAudio2.XAudio2 engine)
		: base(engine)
	{
	}
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

// Stub matching real SharpDX.X3DAudio.Cone reference type. VRage.Audio
// only sets `emitter.Cone = null;` in X3DAudioExtensions.SetDefaultValues,
// so members are not required — but the Cone type itself must exist as
// a class so the FieldRef `class SharpDX.X3DAudio.Cone Emitter::Cone`
// resolves when the CLR loads Emitter.
public sealed class Cone
{
}

[Flags]
public enum CalculateFlags
{
	Matrix = 1,
	Doppler = 2,
	RedirectToLfe = 4
}

public enum X3DAudioVersion
{
	Default
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

	// Must be typed as SharpDX.X3DAudio.Cone (not object): stock
	// VRage.Audio.dll IL has FieldRef
	// `field class SharpDX.X3DAudio.Cone Emitter::Cone`, and the CLR
	// matches fields by name AND signature — so `object Cone` looks
	// like a different field and throws MissingFieldException.
	public Cone Cone;

	public CurvePoint[] VolumeCurve;

	public float InnerRadius;

	public float InnerRadiusAngle;

	public float DopplerScaler;
}

// All DspSettings members must be FIELDS, not properties. Stock
// VRage.Audio IL in MyXAudio2.Apply3D does `stfld` / `ldfld` on
// these members — properties produce set_X/get_X methods that
// don't match a `field` MemberRef and throw MissingFieldException.
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
	// AL handle and lock are owned by XAudio2; X3DAudio is constructed
	// independently by the game (`new X3DAudio(speakers)`) so it has no
	// engine reference. XAudio2 publishes these on init / clears them on
	// dispose, and Calculate is no-op until they're set.
	internal static AL GlobalAl;
	internal static object GlobalAlLock;

	internal struct EmitterData
	{
		public float X, Y, Z;
		public float VX, VY, VZ;
		public bool Valid;
	}

	// Hand-off slot from Calculate → SourceVoice.SetOutputMatrix. The game's
	// MyXAudio2.Apply3D calls them in immediate succession on the same thread,
	// so a [ThreadStatic] field is enough — no queue, no GC pressure.
	[ThreadStatic] private static EmitterData s_lastEmitter;

	internal static EmitterData ConsumeLastEmitter()
	{
		var data = s_lastEmitter;
		s_lastEmitter = default;
		return data;
	}

	public X3DAudio(Speakers speakers)
		: this(speakers, X3DAudioVersion.Default)
	{
	}

	public X3DAudio(Speakers _, float __)
	{
	}

	public X3DAudio(Speakers _, X3DAudioVersion __)
	{
	}

	public void Calculate(Listener listener, Emitter emitter, CalculateFlags flags, DspSettings dspSettings)
	{
		// Distance: the game reads this back to apply its own custom linear
		// falloff to MatrixCoefficients before SetOutputMatrix.
		float dx = emitter.Position.X - listener.Position.X;
		float dy = emitter.Position.Y - listener.Position.Y;
		float dz = emitter.Position.Z - listener.Position.Z;
		float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
		dspSettings.EmitterToListenerDistance = distance;

		// Custom volume-curve attenuation — same shape as the legacy SDL
		// implementation. Filled uniformly into MatrixCoefficients because the
		// per-cell layout no longer matters: OpenAL handles spatial panning
		// from the source's world position. The scalar magnitude must still be
		// correct, since the game multiplies the matrix by its linear falloff
		// after this returns and SourceVoice.SetOutputMatrix extracts it as
		// AL_GAIN.
		float attenuation = EvaluateCurve(emitter.VolumeCurve, emitter.CurveDistanceScaler, distance);
		if (flags.HasFlag(CalculateFlags.Matrix))
		{
			for (int i = 0; i < dspSettings.MatrixCoefficients.Length; i++)
			{
				dspSettings.MatrixCoefficients[i] = attenuation;
			}
		}

		// Doppler: the game reads DopplerFactor and feeds it into
		// voice.SetFrequencyRatio. With OpenAL's own Doppler disabled
		// (DopplerFactor(0) on the engine), there's no double-application.
		if (flags.HasFlag(CalculateFlags.Doppler) && distance > 1e-4f)
		{
			float invDist = 1f / distance;
			float dirX = dx * invDist, dirY = dy * invDist, dirZ = dz * invDist;
			float listenerVel = listener.Velocity.X * dirX + listener.Velocity.Y * dirY + listener.Velocity.Z * dirZ;
			float emitterVel = emitter.Velocity.X * dirX + emitter.Velocity.Y * dirY + emitter.Velocity.Z * dirZ;
			dspSettings.ListenerVelocityComponent = listenerVel;
			dspSettings.EmitterVelocityComponent = emitterVel;
			const float speedOfSound = 343.5f;
			float dopplerScaler = emitter.DopplerScaler;
			float denom = speedOfSound + emitterVel * dopplerScaler;
			if (MathF.Abs(denom) < 0.01f) denom = MathF.CopySign(0.01f, denom);
			dspSettings.DopplerFactor = Math.Clamp(
				(speedOfSound + listenerVel * dopplerScaler) / denom, 0.5f, 2.0f);
		}
		else
		{
			dspSettings.DopplerFactor = 1f;
		}

		// Push listener pose to OpenAL. Apply3D fires every audio frame, so
		// the listener tracks the camera continuously.
		var al = GlobalAl;
		var alLock = GlobalAlLock;
		if (al != null && alLock != null)
		{
			lock (alLock)
			{
				al.SetListenerProperty(ListenerVector3.Position,
					listener.Position.X, listener.Position.Y, listener.Position.Z);
				al.SetListenerProperty(ListenerVector3.Velocity,
					listener.Velocity.X, listener.Velocity.Y, listener.Velocity.Z);
				// SE world is right-handed with -Z forward (Vector3D.Forward = (0,0,-1)).
				// MySandboxGame.UpdateSound passes listenerFront = -MainCamera.ForwardVector
				// to flip into X3DAudio's left-handed +Z forward convention. OpenAL is
				// right-handed -Z forward (matches SE world), so we negate OrientFront
				// here to recover the actual camera-look direction. OrientTop, positions
				// and velocities are already in SE coords — no flip needed.
				float* ori = stackalloc float[6]
				{
					-listener.OrientFront.X, -listener.OrientFront.Y, -listener.OrientFront.Z,
					listener.OrientTop.X, listener.OrientTop.Y, listener.OrientTop.Z
				};
				al.SetListenerProperty(ListenerFloatArray.Orientation, ori);
			}
		}

		// Stash emitter data for the matching SetOutputMatrix call. Same
		// thread, immediate handoff — no queue needed.
		s_lastEmitter = new EmitterData
		{
			X = emitter.Position.X, Y = emitter.Position.Y, Z = emitter.Position.Z,
			VX = emitter.Velocity.X, VY = emitter.Velocity.Y, VZ = emitter.Velocity.Z,
			Valid = true
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

using System.Runtime.Versioning;
using WindowsNaturalVoices.Internal;

namespace WindowsNaturalVoices;

/// <summary>
/// An energy-based <see cref="ISpeechActivityDetector"/>: it thresholds the RMS
/// loudness of each fixed-length frame and debounces the result with start/stop
/// hangovers (see <see cref="VoiceActivityOptions"/>). It is a pure-signal
/// endpointer with no model dependency, suitable for turn-taking and barge-in; a
/// native <c>svad</c>-backed detector can be substituted later behind the same
/// interface.
/// </summary>
/// <remarks>Thread-hostile: feed audio from one thread and serialize calls.</remarks>
[SupportedOSPlatform("windows")]
public sealed class EnergyVoiceActivityDetector : ISpeechActivityDetector
{
    private readonly EnergyEndpointer _endpointer;
    private readonly TimeSpan _frameDuration;
    private readonly int _frameSampleCount; // interleaved samples per frame (all channels)
    private readonly List<float> _residual = [];
    private TimeSpan _position;

    /// <summary>
    /// Creates a detector for <paramref name="format"/>, tuned by
    /// <paramref name="options"/> (or <see cref="VoiceActivityOptions.Default"/>).
    /// </summary>
    public EnergyVoiceActivityDetector(AudioFormat format, VoiceActivityOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(format);
        options ??= VoiceActivityOptions.Default;
        options.Validate();

        Format = format;
        Options = options;
        _endpointer = new EnergyEndpointer(options);

        var framesPerChannel = Math.Max(1, (int)Math.Round(options.FrameDuration.TotalSeconds * format.SampleRate));
        _frameSampleCount = framesPerChannel * format.Channels;
        // Time each frame by the audio it actually spans (the sample count is rounded),
        // so hangovers and positions track real audio rather than the requested duration.
        _frameDuration = TimeSpan.FromSeconds((double)framesPerChannel / format.SampleRate);
    }

    /// <inheritdoc />
    public AudioFormat Format { get; }

    /// <summary>The tuning this detector was created with.</summary>
    public VoiceActivityOptions Options { get; }

    /// <inheritdoc />
    public bool IsSpeaking => _endpointer.IsSpeaking;

    /// <inheritdoc />
    public event EventHandler<SpeechActivityEventArgs>? SpeechStarted;

    /// <inheritdoc />
    public event EventHandler<SpeechActivityEventArgs>? SpeechEnded;

    /// <inheritdoc />
    public void Process(AudioBuffer audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (audio.Format.SampleRate != Format.SampleRate || audio.Format.Channels != Format.Channels)
        {
            throw new ArgumentException(
                $"Audio is {audio.Format.SampleRate} Hz/{audio.Format.Channels}ch but the detector " +
                $"expects {Format.SampleRate} Hz/{Format.Channels}ch.", nameof(audio));
        }

        if (audio.IsEmpty) return;
        _residual.AddRange(audio.ToSamples());

        // Collect transitions while state is being mutated, then dispatch events only
        // after the residual is trimmed — so a handler that calls Reset()/Process()
        // (or throws) can't corrupt the frame accounting mid-loop.
        List<(bool Started, SpeechActivityEventArgs Args)>? pending = null;

        var consumed = 0;
        while (_residual.Count - consumed >= _frameSampleCount)
        {
            var frame = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_residual)
                .Slice(consumed, _frameSampleCount);
            var rms = AudioEnergy.Rms(frame);
            consumed += _frameSampleCount;
            _position += _frameDuration;

            switch (_endpointer.Process(rms, _frameDuration))
            {
                case VadTransition.SpeechStarted:
                    (pending ??= []).Add((true, new SpeechActivityEventArgs(_position)));
                    break;
                case VadTransition.SpeechEnded:
                    (pending ??= []).Add((false, new SpeechActivityEventArgs(_position)));
                    break;
            }
        }

        if (consumed > 0) _residual.RemoveRange(0, consumed);

        if (pending is null) return;
        foreach (var (started, args) in pending)
        {
            if (started) SpeechStarted?.Invoke(this, args);
            else SpeechEnded?.Invoke(this, args);
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        _endpointer.Reset();
        _residual.Clear();
        _position = TimeSpan.Zero;
    }

    /// <summary>No unmanaged resources; provided to satisfy the interface.</summary>
    public void Dispose() { }
}

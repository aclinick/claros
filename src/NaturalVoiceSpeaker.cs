using System.Runtime.Versioning;
using Claros.Internal;

namespace Claros;

/// <summary>
/// One-call facade over <see cref="SapiPhonemizer"/>,
/// <see cref="NaturalVoiceEngine"/>, and <see cref="Vocoder"/>. Wraps the
/// three components an app needs to turn text into audio and disposes them
/// together.
///
/// Instances are thread hostile; construct one per voice and serialize calls
/// to <see cref="SynthesizeAsync"/>. Reuse across many phrases: model load times
/// dominate a first-call latency budget, and this facade keeps the sessions
/// warm for the lifetime of the speaker.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NaturalVoiceSpeaker : IDisposable
{
    private readonly NaturalVoiceEngine _engine;
    private readonly Vocoder _vocoder;
    private readonly SapiPhonemizer _phonemizer;
    private readonly string _locale;
    private bool _disposed;

    /// <summary>The Natural Voice this speaker is bound to.</summary>
    public VoiceInfo Voice { get; }

    /// <summary>The underlying acoustic model. Exposed for advanced callers.</summary>
    public NaturalVoiceEngine Engine => _engine;

    /// <summary>The underlying vocoder. Exposed for advanced callers.</summary>
    public Vocoder Vocoder => _vocoder;

    /// <summary>The SAPI phonemizer driving the text preprocessor.</summary>
    public SapiPhonemizer Phonemizer => _phonemizer;

    private NaturalVoiceSpeaker(
        VoiceInfo voice,
        NaturalVoiceEngine engine,
        Vocoder vocoder,
        SapiPhonemizer phonemizer,
        string locale)
    {
        Voice = voice;
        _engine = engine;
        _vocoder = vocoder;
        _phonemizer = phonemizer;
        _locale = locale;
    }

    /// <summary>
    /// Load a Natural Voice and prepare it for text-to-speech synthesis. Uses
    /// <paramref name="sapiVoiceName"/> to drive SAPI's text preprocessor; the
    /// default "Microsoft Zira Desktop" ships with every en-US Windows install.
    /// </summary>
    public static NaturalVoiceSpeaker Load(
        VoiceInfo voice,
        string sapiVoiceName = "Microsoft Zira Desktop",
        NaturalVoiceEngineOptions? engineOptions = null)
    {
        ArgumentNullException.ThrowIfNull(voice);
        DeviceVoiceGuard.RequireOnDevice(voice, nameof(voice));

        var engine = NaturalVoiceEngine.Load(voice, engineOptions);
        Vocoder? vocoder = null;
        SapiPhonemizer? phonemizer = null;
        try
        {
            vocoder = Vocoder.Load(voice, engineOptions);
            phonemizer = SapiPhonemizer.Create(sapiVoiceName);
            return new NaturalVoiceSpeaker(voice, engine, vocoder, phonemizer, voice.Locale);
        }
        catch
        {
            vocoder?.Dispose();
            phonemizer?.Dispose();
            engine.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Convert <paramref name="text"/> to a waveform. Runs SAPI, the acoustic
    /// model, and the vocoder in sequence on the caller's task pool. This produces
    /// audio but does not play it.
    /// </summary>
    public Task<WaveformResult> SynthesizeAsync(
        string text,
        SynthesisOptions? synthesisOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Task.Run(() => SynthesizeCore(text, synthesisOptions, cancellationToken), cancellationToken);
    }

    private WaveformResult SynthesizeCore(
        string text,
        SynthesisOptions? synthesisOptions,
        CancellationToken cancellationToken)
    {
        var ids = _phonemizer.Phonemize(text, _engine.Phonemes, _locale);
        cancellationToken.ThrowIfCancellationRequested();

        // Call the engine synchronously. This method already runs inside a
        // Task.Run, so going through SynthesizeAsync would queue a second pool
        // work item and block this thread waiting on it, occupying two threads
        // per utterance for no benefit.
        var tokens = _engine.Synthesize(ids, synthesisOptions, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return _vocoder.Synthesize(tokens);
    }

    /// <summary>
    /// Releases the phonemizer, acoustic engine, and vocoder this speaker owns.
    /// Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _phonemizer.Dispose();
        }
        finally
        {
            try
            {
                _vocoder.Dispose();
            }
            finally
            {
                _engine.Dispose();
            }
        }
    }
}

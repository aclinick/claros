using System.Runtime.Versioning;
using Claros.Internal;

namespace Claros;

/// <summary>
/// One-call facade over <see cref="SapiPhonemizer"/>,
/// <see cref="NaturalVoiceEngine"/>, and <see cref="Vocoder"/>. Wraps the
/// three components an app needs to turn text into audio and disposes them
/// together.
///
/// Implements <see cref="ISpeechSynthesizer"/>, so it can drive
/// <see cref="TimedNarrator"/>, <see cref="SpeechConversation"/>, and any
/// tier-agnostic code alongside <see cref="EmbeddedSpeechSynthesizer"/>.
/// It is the reconstructed pipeline rather than Microsoft's own front end,
/// so it reports narrower <see cref="Capabilities"/>: no word boundaries,
/// and no SSML or prosody, because SAPI's preprocessor is driven with plain
/// text here.
///
/// Instances are thread hostile; construct one per voice and serialize calls
/// to <see cref="SynthesizeAsync(SpeechSynthesisRequest, CancellationToken)"/>.
/// Reuse across many phrases: model load times dominate a first-call latency
/// budget, and this facade keeps the sessions warm for its lifetime.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NaturalVoiceSynthesizer : ISpeechSynthesizer
{
    private readonly NaturalVoiceEngine _engine;
    private readonly Vocoder _vocoder;
    private readonly SapiPhonemizer _phonemizer;
    private readonly string _locale;
    private readonly SingleFlightGate _gate = new();
    private bool _disposed;

    /// <summary>The Natural Voice this synthesizer is bound to.</summary>
    public VoiceInfo Voice { get; }

    /// <summary>
    /// The audio this pipeline produces: mono 16-bit PCM at the vocoder's
    /// native <see cref="Vocoder.NativeSampleRate"/>. Fixed for the lifetime
    /// of the instance, because the vocoder model itself fixes it.
    /// </summary>
    public AudioFormat OutputFormat { get; } = AudioFormat.Pcm16Mono(Vocoder.NativeSampleRate);

    /// <summary>
    /// On-device, minus word boundaries, SSML, and prosody. The acoustic model
    /// emits codec tokens with no word alignment, so there is nothing to report
    /// per word; and this pipeline drives SAPI's preprocessor with plain text,
    /// so neither markup nor prosody can be honored. All three gaps are
    /// declared rather than discovered: a caller that needs one should check
    /// here instead of passing an <c>onWord</c> callback that would never fire,
    /// or markup that would be refused mid-conversation.
    /// </summary>
    public SynthesizerCapabilities Capabilities { get; } =
        SynthesizerCapabilities.OnDevice with
        {
            WordBoundaries = false,
            RawSsml = false,
            Prosody = false,
        };

    /// <summary>
    /// The SAPI voice whose text preprocessor drives grapheme-to-phoneme
    /// conversion. Exposed as a name rather than as the
    /// <see cref="SapiPhonemizer"/> itself: this instance owns and disposes
    /// the phonemizer, and handing it out invites a caller to dispose it and
    /// leave this synthesizer unusable.
    /// </summary>
    public string SapiVoiceName => _phonemizer.VoiceName;

    /// <summary>
    /// How many <c>Streaming*</c> custom-op nodes were rewritten to stock ONNX
    /// ops when the vocoder loaded. Useful for diagnostics; see
    /// <see cref="Vocoder.RewrittenNodes"/>.
    /// </summary>
    public int RewrittenVocoderNodes => _vocoder.RewrittenNodes;

    private NaturalVoiceSynthesizer(
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
    public static NaturalVoiceSynthesizer Load(
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
            return new NaturalVoiceSynthesizer(voice, engine, vocoder, phonemizer, voice.Locale);
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
    /// Synthesizes <paramref name="request"/> and returns the complete waveform.
    /// Runs SAPI, the acoustic model, and the vocoder in sequence on the
    /// caller's task pool. This produces audio but does not play it.
    /// </summary>
    /// <remarks>
    /// A plain <see cref="string"/> converts implicitly to a text request, so
    /// <c>SynthesizeAsync("hello")</c> is the simple case. Raw SSML and prosody
    /// are rejected rather than silently dropped: this pipeline drives SAPI's
    /// preprocessor with plain text, and quietly ignoring markup would produce
    /// audio that does not match what was asked for. Use
    /// <see cref="EmbeddedSpeechSynthesizer"/> when you need either.
    /// </remarks>
    public Task<WaveformResult> SynthesizeAsync(
        SpeechSynthesisRequest request, CancellationToken cancellationToken = default) =>
        SynthesizeAsync(request, null, cancellationToken);

    /// <summary>
    /// Synthesizes <paramref name="request"/> with pipeline-level overrides —
    /// decoder step cap, stop threshold, and the rest of
    /// <see cref="SynthesisOptions"/> — that the tier-agnostic
    /// <see cref="ISpeechSynthesizer"/> contract has no place for.
    /// </summary>
    public Task<WaveformResult> SynthesizeAsync(
        SpeechSynthesisRequest request,
        SynthesisOptions? synthesisOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        ObjectDisposedException.ThrowIf(_disposed, this);

        SynthesisSupportGuard.RequirePlainText(request, Voice.DisplayName);

        return Task.Run(
            () => SynthesizeCore(request.Content, synthesisOptions, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Synthesizes <paramref name="request"/> in full, then writes the audio to
    /// <paramref name="sink"/> in ~100 ms <see cref="AudioBuffer"/> chunks. The
    /// vocoder returns a complete waveform, so this decouples the consumer
    /// rather than lowering first-audio latency. The sink is not completed; the
    /// caller owns its lifetime, and its format must match
    /// <see cref="OutputFormat"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="onWord"/> is rejected rather than ignored, because this
    /// pipeline produces no word alignment — see <see cref="Capabilities"/>. A
    /// caller highlighting captions would otherwise wait forever for a callback
    /// that cannot come.
    /// </remarks>
    public async Task SynthesizeToSinkAsync(
        SpeechSynthesisRequest request,
        IAudioSink sink,
        Action<SpokenWord>? onWord = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);

        SynthesisSupportGuard.RequireNoWordCallback(onWord, Voice.DisplayName, nameof(onWord));

        var waveform = await SynthesizeAsync(request, cancellationToken).ConfigureAwait(false);
        await SinkWriter.WriteAsync(sink, waveform, nameof(sink), cancellationToken)
            .ConfigureAwait(false);
    }

    private WaveformResult SynthesizeCore(
        string text,
        SynthesisOptions? synthesisOptions,
        CancellationToken cancellationToken)
    {
        // Concurrent use would race three native engines at once. The documented
        // rule is "serialize calls"; this makes breaking it fail loudly.
        _gate.Enter(nameof(NaturalVoiceSynthesizer), "a synthesis request");
        try
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
        finally
        {
            _gate.Exit();
        }
    }

    /// <summary>
    /// Releases the phonemizer, acoustic engine, and vocoder this instance owns.
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

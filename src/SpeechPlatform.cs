using System.Runtime.Versioning;

namespace Claros;

/// <summary>
/// Single entry point that unifies the two halves of the library — offline
/// text-to-speech (synthesis) and offline speech-to-text (recognition) — behind
/// one discoverable, consistent facade. Rather than reaching for
/// <see cref="VoiceCatalog"/>, <see cref="TranscriptionModelCatalog"/>,
/// <see cref="EmbeddedVoiceSpeaker"/>, and <see cref="EmbeddedTranscriber"/>
/// separately, a caller opens one <see cref="SpeechPlatform"/> and discovers
/// installed voices and recognition models, then creates warm speakers and
/// transcribers from it.
/// </summary>
/// <remarks>
/// <para>
/// The platform is a thin, non-owning coordinator over the existing types: voice
/// discovery is delegated to an owned <see cref="VoiceCatalog"/> (whose
/// <see cref="VoiceCatalog.VoicesChanged"/> event is re-raised as
/// <see cref="VoicesChanged"/>), recognition-model discovery to the static
/// <see cref="TranscriptionModelCatalog"/>, and speaker/transcriber creation to
/// the respective <c>Load</c> factories. It adds no new synthesis or recognition
/// behavior; it exists purely to make both halves reachable and named
/// consistently from one place.
/// </para>
/// <para>
/// The speakers and transcribers it creates are themselves thread hostile and
/// must be kept warm and called serially (model load dominates first-call
/// latency). The platform itself is cheap to construct; dispose it to release the
/// owned voice catalog's package-change subscription.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class SpeechPlatform : IDisposable
{
    private readonly VoiceCatalog _voices;
    private bool _disposed;

    /// <summary>
    /// Opens the speech platform, subscribing to installed-voice package changes
    /// so <see cref="VoicesChanged"/> fires when the set of installed voices
    /// changes. Recognition models are queried on demand from the OS package
    /// graph and are never cached.
    /// </summary>
    public SpeechPlatform()
    {
        _voices = new VoiceCatalog();
        _voices.VoicesChanged += OnVoicesChanged;
    }

    /// <summary>
    /// Fires when the OS reports a change (install, update, or uninstall) in the
    /// set of installed Natural Voice packages. Handlers should call
    /// <see cref="ListVoicesAsync"/> again to rebuild their voice list.
    /// </summary>
    public event EventHandler? VoicesChanged;

    /// <summary>
    /// Returns every installed Natural Voice. Never cached; each call queries the
    /// OS. This is the synthesis counterpart of
    /// <see cref="ListRecognitionModels"/>.
    /// </summary>
    public Task<IReadOnlyList<VoiceInfo>> ListVoicesAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _voices.ListVoicesAsync(cancellationToken);
    }

    /// <summary>
    /// Finds the first installed Natural Voice whose locale matches
    /// <paramref name="locale"/> (compared case-insensitively, e.g. <c>en-US</c>),
    /// or <c>null</c> when none is installed for that locale.
    /// </summary>
    public async Task<VoiceInfo?> FindVoiceAsync(
        string locale, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(locale);
        var voices = await ListVoicesAsync(cancellationToken).ConfigureAwait(false);
        return voices.FirstOrDefault(
            v => string.Equals(v.Locale, locale, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds the installed voice with the given <see cref="VoiceInfo.Id"/>, or
    /// <c>null</c> when it is no longer installed. Ids are stable across calls,
    /// so this is the reliable way to re-open the exact voice a user picked
    /// earlier, where a locale lookup could return a different voice.
    /// </summary>
    public async Task<VoiceInfo?> FindVoiceByIdAsync(
        string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        var voices = await ListVoicesAsync(cancellationToken).ConfigureAwait(false);
        return voices.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.Ordinal));
    }

    /// <summary>
    /// Returns every installed on-device speech-recognition model (the Live
    /// Captions packs), one per locale. Never cached; each call queries the OS.
    /// This is the recognition counterpart of <see cref="ListVoicesAsync"/>.
    /// </summary>
    public IReadOnlyList<TranscriptionModelInfo> ListRecognitionModels()
    {
        ThrowIfDisposed();
        return TranscriptionModelCatalog.ListModels();
    }

    /// <summary>
    /// Finds the installed recognition model for <paramref name="locale"/>
    /// (compared case-insensitively, e.g. <c>en-US</c>), or <c>null</c> when none
    /// is installed. Mirrors <see cref="FindVoiceAsync"/> on the recognition side.
    /// </summary>
    public TranscriptionModelInfo? FindRecognitionModel(string locale)
    {
        ArgumentException.ThrowIfNullOrEmpty(locale);
        ThrowIfDisposed();
        return TranscriptionModelCatalog.FindModel(locale);
    }

    /// <summary>
    /// Creates a warm <see cref="EmbeddedVoiceSpeaker"/> bound to
    /// <paramref name="voice"/> for offline synthesis. Delegates to
    /// <see cref="EmbeddedVoiceSpeaker.Load"/>; see it for the license and options
    /// contract. The returned speaker is owned by the caller and must be disposed.
    /// </summary>
    public EmbeddedVoiceSpeaker CreateSpeaker(
        VoiceInfo voice,
        string? license = null,
        EmbeddedVoiceOptions? options = null)
    {
        ThrowIfDisposed();
        return EmbeddedVoiceSpeaker.Load(voice, license, options);
    }

    /// <summary>
    /// Creates a warm <see cref="EmbeddedTranscriber"/> bound to
    /// <paramref name="model"/> for offline recognition. Delegates to
    /// <see cref="EmbeddedTranscriber.Load"/>; see it for the license and options
    /// contract. The returned transcriber is owned by the caller and must be
    /// disposed.
    /// </summary>
    public EmbeddedTranscriber CreateTranscriber(
        TranscriptionModelInfo model,
        string? license = null,
        EmbeddedTranscriberOptions? options = null)
    {
        ThrowIfDisposed();
        return EmbeddedTranscriber.Load(model, license, options);
    }

    /// <summary>
    /// Creates a warm <see cref="EmbeddedVoiceSpeaker"/> bound to
    /// <paramref name="voice"/> and wraps it in a <see cref="TimedNarrator"/> for
    /// subtitle- and cue-timed narration. The narrator <em>owns</em> the speaker
    /// it creates, so disposing the narrator releases it; there is nothing else
    /// for the caller to track.
    /// </summary>
    public TimedNarrator CreateNarrator(
        VoiceInfo voice,
        string? license = null,
        EmbeddedVoiceOptions? options = null)
    {
        ThrowIfDisposed();
        var speaker = EmbeddedVoiceSpeaker.Load(voice, license, options);
        try
        {
            return new TimedNarrator(speaker, owned: speaker);
        }
        catch
        {
            speaker.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Wires an end-to-end, barge-in <see cref="SpeechConversation"/>: it loads a
    /// warm synthesizer for <paramref name="voice"/> and a live recognizer plus an
    /// energy voice-activity detector for <paramref name="model"/>, then connects
    /// them to the supplied <paramref name="microphone"/> and
    /// <paramref name="speaker"/> and the caller's <paramref name="turnHandler"/>.
    /// </summary>
    /// <remarks>
    /// The returned conversation <em>owns</em> every component it created here, so
    /// disposing it releases the synthesizer, transcriber, recognizer, and detector
    /// together — a single <c>using</c> is the whole lifetime. The
    /// <paramref name="microphone"/> and <paramref name="speaker"/> are supplied by
    /// the caller and are never disposed. The recognizer and detector are bound to
    /// <paramref name="microphone"/>'s sample rate, so all three must agree (Live
    /// Captions expects 16 kHz mono). Build the components yourself and use the
    /// <see cref="SpeechConversation"/> constructor when you need to keep a warm
    /// engine alive across several conversations.
    /// </remarks>
    public SpeechConversation CreateConversation(
        VoiceInfo voice,
        TranscriptionModelInfo model,
        IAudioSource microphone,
        IAudioSink speaker,
        ConversationTurnHandler turnHandler,
        VoiceActivityOptions? activityOptions = null,
        string? synthesisLicense = null,
        string? recognitionLicense = null,
        EmbeddedVoiceOptions? synthesisOptions = null,
        EmbeddedTranscriberOptions? recognitionOptions = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(voice);

        var synth = EmbeddedVoiceSpeaker.Load(voice, synthesisLicense, synthesisOptions);
        try
        {
            return CreateConversationCore(
                synth, model, microphone, speaker, turnHandler,
                activityOptions, recognitionLicense, recognitionOptions,
                ownsSynthesizer: true);
        }
        catch
        {
            // The synthesizer was created here, so it is ours to release when the
            // rest of the wiring fails.
            synth.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Wires an end-to-end, barge-in <see cref="SpeechConversation"/> around a
    /// synthesizer the caller already owns, rather than loading an on-device voice
    /// internally. This is the tier-agnostic overload: any
    /// <see cref="ISpeechSynthesizer"/> can drive the loop, so a caller who has
    /// explicitly opted into a different synthesis tier reuses the same recognition
    /// and barge-in wiring instead of reimplementing it.
    /// </summary>
    /// <remarks>
    /// The returned conversation owns the transcriber, recognizer, and detector it
    /// creates, and releases them when disposed. <paramref name="synthesizer"/>
    /// stays owned by the caller and is never disposed — not on success, and not
    /// when this method fails partway. The recognizer and detector are bound to
    /// <paramref name="microphone"/>'s sample rate, so all three must agree (Live
    /// Captions expects 16 kHz mono).
    /// </remarks>
    public SpeechConversation CreateConversation(
        ISpeechSynthesizer synthesizer,
        TranscriptionModelInfo model,
        IAudioSource microphone,
        IAudioSink speaker,
        ConversationTurnHandler turnHandler,
        VoiceActivityOptions? activityOptions = null,
        string? recognitionLicense = null,
        EmbeddedTranscriberOptions? recognitionOptions = null)
    {
        ThrowIfDisposed();
        return CreateConversationCore(
            synthesizer, model, microphone, speaker, turnHandler,
            activityOptions, recognitionLicense, recognitionOptions,
            ownsSynthesizer: false);
    }

    private SpeechConversation CreateConversationCore(
        ISpeechSynthesizer synthesizer,
        TranscriptionModelInfo model,
        IAudioSource microphone,
        IAudioSink speaker,
        ConversationTurnHandler turnHandler,
        VoiceActivityOptions? activityOptions,
        string? recognitionLicense,
        EmbeddedTranscriberOptions? recognitionOptions,
        bool ownsSynthesizer)
    {
        ArgumentNullException.ThrowIfNull(synthesizer);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(microphone);
        ArgumentNullException.ThrowIfNull(speaker);
        ArgumentNullException.ThrowIfNull(turnHandler);

        EmbeddedTranscriber? trans = null;
        StreamingRecognizer? rec = null;
        EnergyVoiceActivityDetector? vad = null;
        try
        {
            trans = EmbeddedTranscriber.Load(model, recognitionLicense, recognitionOptions);
            rec = trans.StartRecognizer();
            vad = new EnergyVoiceActivityDetector(microphone.Format, activityOptions);

            // Held in CREATION order; the conversation disposes in reverse, which
            // is what the parent/child relationship requires: the detector first,
            // then the recognizer, then the transcriber that owns the recognizer's
            // underlying session, and the synthesizer last when we created it.
            var owned = new List<IDisposable> { trans, rec, vad };
            if (ownsSynthesizer) owned.Insert(0, synthesizer);

            return new SpeechConversation(
                microphone, rec, vad, synthesizer, speaker, turnHandler, owned);
        }
        catch
        {
            // Roll back any native resources created before the failure so the
            // caller is not left with unreachable, undisposed engines. The
            // caller-supplied synthesizer is deliberately left alone.
            vad?.Dispose();
            rec?.Dispose();
            trans?.Dispose();
            throw;
        }
    }


    private void OnVoicesChanged(object? sender, EventArgs e) =>
        VoicesChanged?.Invoke(this, EventArgs.Empty);

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>
    /// Releases the owned voice catalog's package-change subscription. Speakers
    /// and transcribers created by this platform are independently owned and are
    /// not disposed here. Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _voices.VoicesChanged -= OnVoicesChanged;
        _voices.Dispose();
    }
}

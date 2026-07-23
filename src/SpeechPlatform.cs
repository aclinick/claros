using System.Runtime.Versioning;

namespace WindowsNaturalVoices;

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
    public Task<IReadOnlyList<VoiceInfo>> ListVoicesAsync()
    {
        ThrowIfDisposed();
        return _voices.ListVoicesAsync();
    }

    /// <summary>
    /// Finds the first installed Natural Voice whose locale matches
    /// <paramref name="locale"/> (compared case-insensitively, e.g. <c>en-US</c>),
    /// or <c>null</c> when none is installed for that locale.
    /// </summary>
    public async Task<VoiceInfo?> FindVoiceAsync(string locale)
    {
        ArgumentException.ThrowIfNullOrEmpty(locale);
        var voices = await ListVoicesAsync().ConfigureAwait(false);
        return voices.FirstOrDefault(
            v => string.Equals(v.Locale, locale, StringComparison.OrdinalIgnoreCase));
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

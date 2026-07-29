namespace Claros;

/// <summary>
/// A request-driven, streamable text-to-speech engine. Turns a
/// <see cref="SpeechSynthesisRequest"/> (plain text, prosody-shaped text, or raw
/// SSML) into audio, either buffered as a <see cref="WaveformResult"/> or streamed
/// chunk by chunk into an <see cref="IAudioSink"/>. This is the abstraction the
/// conversation loop synthesizes through, so any voice engine can be substituted.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are thread hostile: construct one per voice, keep it warm, and
/// serialize calls. The engine is bound to a single <see cref="Voice"/> for its
/// lifetime.
/// </para>
/// <para>
/// The contract itself is deliberately platform-neutral — it describes only
/// "request in, audio out" — so an engine that is not backed by an installed
/// Windows voice package can implement it. The shipped implementation
/// (<see cref="EmbeddedVoiceSpeaker"/>) carries
/// their own Windows platform annotations. Implementations must not silently
/// substitute a different tier than the one their <see cref="Voice"/> declares;
/// see <see cref="VoiceSource"/>.
/// </para>
/// </remarks>
public interface ISpeechSynthesizer : IDisposable
{
    /// <summary>The voice this synthesizer speaks with.</summary>
    VoiceInfo Voice { get; }

    /// <summary>
    /// What this engine can and cannot guarantee. Callers that depend on a
    /// specific behaviour — word boundaries for caption highlighting, or one
    /// stable sample rate for timeline mixing — should check here and refuse up
    /// front rather than discover the gap mid-render.
    /// </summary>
    /// <remarks>
    /// Defaults to the profile implied by <see cref="Voice"/>'s
    /// <see cref="VoiceSource"/>: <see cref="SynthesizerCapabilities.OnDevice"/> for
    /// a device voice, <see cref="SynthesizerCapabilities.Hosted"/> otherwise. That
    /// derivation matters — a hosted engine that forgot to override this must not
    /// be able to claim it is offline and free, which would defeat the negotiation
    /// entirely. Override whenever the engine differs from its tier's profile.
    /// </remarks>
    SynthesizerCapabilities Capabilities =>
        Voice.IsOnDevice ? SynthesizerCapabilities.OnDevice : SynthesizerCapabilities.Hosted;

    /// <summary>
    /// Synthesizes <paramref name="request"/> and returns the complete waveform.
    /// Cancellation stops the in-flight synthesis.
    /// </summary>
    Task<WaveformResult> SynthesizeAsync(
        SpeechSynthesisRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synthesizes <paramref name="request"/> and writes the audio to
    /// <paramref name="sink"/> as a sequence of <see cref="AudioBuffer"/> chunks,
    /// so playback (or capture) can begin and can be cancelled mid-stream. The
    /// sink is not completed — the caller owns its lifetime. When
    /// <paramref name="onWord"/> is supplied it is raised for each word as its
    /// audio is produced (a synthesis-time boundary; see <see cref="SpokenWord"/>).
    /// </summary>
    Task SynthesizeToSinkAsync(
        SpeechSynthesisRequest request,
        IAudioSink sink,
        Action<SpokenWord>? onWord = null,
        CancellationToken cancellationToken = default);
}

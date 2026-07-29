namespace Windows.Speech;

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
/// Windows voice package can implement it. The shipped on-device implementations
/// (<see cref="EmbeddedVoiceSpeaker"/>, <see cref="NaturalVoiceSpeaker"/>) carry
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

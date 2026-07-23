namespace WindowsNaturalVoices;

/// <summary>
/// A destination for streaming audio: a speaker, a file, or an in-memory
/// collector for testing. Producers push <see cref="AudioBuffer"/> chunks with
/// <see cref="WriteAsync"/> and signal end of stream with <see cref="CompleteAsync"/>.
/// </summary>
/// <remarks>
/// Every buffer written must match the sink's declared <see cref="Format"/>.
/// After <see cref="CompleteAsync"/> the sink accepts no further writes.
/// </remarks>
public interface IAudioSink
{
    /// <summary>The format every written buffer must be in.</summary>
    AudioFormat Format { get; }

    /// <summary>
    /// Writes one buffer of audio. The buffer's <see cref="AudioBuffer.Format"/>
    /// must equal this sink's <see cref="Format"/>.
    /// </summary>
    ValueTask WriteAsync(AudioBuffer buffer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signals that no more audio will be written, flushing any pending output.
    /// Safe to call more than once.
    /// </summary>
    ValueTask CompleteAsync(CancellationToken cancellationToken = default);
}

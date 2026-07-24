namespace Windows.Speech;

/// <summary>
/// A pull-based source of streaming audio: a microphone, a decoded file, or an
/// in-memory sequence for testing. Consumers iterate <see cref="ReadAsync"/> and
/// receive <see cref="AudioBuffer"/> chunks until the source ends.
/// </summary>
/// <remarks>
/// Every buffer a source yields must match its declared <see cref="Format"/>. The
/// sequence completes when the underlying source ends (end of file, capture
/// stopped); a live microphone may never complete on its own and is instead
/// stopped by cancelling the enumeration.
/// </remarks>
public interface IAudioSource
{
    /// <summary>The format of every buffer this source yields.</summary>
    AudioFormat Format { get; }

    /// <summary>
    /// Streams audio buffers as they become available. Cancelling
    /// <paramref name="cancellationToken"/> ends the enumeration; for a live
    /// capture source this is the normal way to stop it.
    /// </summary>
    IAsyncEnumerable<AudioBuffer> ReadAsync(CancellationToken cancellationToken = default);
}

namespace Claros;

/// <summary>
/// An <see cref="IAudioSink"/> that keeps every buffer written to it in memory.
/// Useful for capturing synthesized audio without a file or speaker (for example
/// "synthesize this text to a buffer"), and as a test double for anything that
/// writes audio.
/// </summary>
public sealed class BufferedAudioSink : IAudioSink
{
    private readonly List<AudioBuffer> _buffers = new();
    private readonly object _gate = new();
    private bool _completed;

    /// <summary>Creates a collecting sink that accepts audio in <paramref name="format"/>.</summary>
    public BufferedAudioSink(AudioFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        Format = format;
    }

    /// <inheritdoc/>
    public AudioFormat Format { get; }

    /// <summary>Whether <see cref="CompleteAsync"/> has been called.</summary>
    public bool IsCompleted
    {
        get { lock (_gate) return _completed; }
    }

    /// <summary>A snapshot of the buffers written so far, in order.</summary>
    public IReadOnlyList<AudioBuffer> Buffers
    {
        get { lock (_gate) return _buffers.ToArray(); }
    }

    /// <inheritdoc/>
    public ValueTask WriteAsync(AudioBuffer buffer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        cancellationToken.ThrowIfCancellationRequested();
        if (!buffer.Format.Equals(Format))
        {
            throw new ArgumentException(
                "The buffer's format does not match the sink's format.", nameof(buffer));
        }

        lock (_gate)
        {
            if (_completed)
            {
                throw new InvalidOperationException("The sink has been completed and accepts no more writes.");
            }
            if (!buffer.IsEmpty) _buffers.Add(buffer);
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) _completed = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>Concatenates every written buffer's PCM into a single byte array.</summary>
    public byte[] ToPcm()
    {
        lock (_gate)
        {
            var total = 0;
            foreach (var b in _buffers) total += b.Pcm.Length;
            var result = new byte[total];
            var offset = 0;
            foreach (var b in _buffers)
            {
                b.Pcm.Span.CopyTo(result.AsSpan(offset));
                offset += b.Pcm.Length;
            }
            return result;
        }
    }

    /// <summary>
    /// Concatenates all written audio into normalized float samples, interleaved
    /// across channels.
    /// </summary>
    public float[] ToSamples() =>
        AudioBuffer.FromOwned(ToPcm(), Format).ToSamples();

    /// <summary>
    /// Returns the collected mono audio as a <see cref="WaveformResult"/>. Throws
    /// when the sink's format is not single-channel.
    /// </summary>
    public WaveformResult ToWaveform()
    {
        if (Format.Channels != 1)
        {
            throw new InvalidOperationException(
                $"ToWaveform requires mono audio; the sink is {Format.Channels}-channel.");
        }
        return new WaveformResult(ToSamples(), Format.SampleRate);
    }
}

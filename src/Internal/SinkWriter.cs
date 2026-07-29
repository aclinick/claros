namespace Claros.Internal;

/// <summary>
/// Writes a finished waveform into an <see cref="IAudioSink"/> as uniform chunks.
/// Shared by the synthesizers so every engine delivers the same buffer shape and
/// enforces the same sink/voice format agreement.
/// </summary>
internal static class SinkWriter
{
    /// <summary>
    /// Writes <paramref name="waveform"/> to <paramref name="sink"/> in ~100 ms
    /// buffers, checking cancellation between chunks so a consumer can stop
    /// promptly. The sink is not completed; the caller owns its lifetime.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The sink's format does not match the audio the voice produced.
    /// </exception>
    public static async Task WriteAsync(
        IAudioSink sink,
        WaveformResult waveform,
        string sinkParamName,
        CancellationToken cancellationToken)
    {
        var format = AudioFormat.Pcm16Mono(waveform.SampleRate);
        if (!sink.Format.Equals(format))
        {
            throw new ArgumentException(
                $"The sink expects {sink.Format.SampleRate} Hz / {sink.Format.Channels}-channel audio, " +
                $"but this voice produces {format.SampleRate} Hz mono. Match the sink's format to the voice.",
                sinkParamName);
        }

        var samples = waveform.Samples;
        var chunk = Math.Max(1, format.SampleRate / 10); // ~100 ms
        for (var offset = 0; offset < samples.Length; offset += chunk)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = Math.Min(chunk, samples.Length - offset);
            var buffer = AudioBuffer.FromSamples(samples.AsSpan(offset, length), format);
            await sink.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
    }
}

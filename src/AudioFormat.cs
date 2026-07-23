namespace WindowsNaturalVoices;

/// <summary>
/// Describes the layout of an uncompressed PCM audio stream: its sample rate,
/// channel count, and bit depth. This is the common currency the streaming
/// speech interfaces speak, so a synthesizer's output and a recognizer's input
/// can be described (and validated) in one place instead of each type carrying a
/// bare <c>int sampleRate</c>.
/// </summary>
/// <remarks>
/// The library's audio paths are 16-bit PCM throughout (this is what both the
/// on-device synthesis and recognition runtimes exchange), but the format itself
/// is general so callers can describe other layouts when bridging external audio.
/// <see cref="AudioBuffer"/> specifically requires 16-bit formats.
/// </remarks>
public sealed record AudioFormat
{
    /// <summary>
    /// Creates a PCM audio format. All three values must be positive and
    /// <paramref name="bitsPerSample"/> must be a whole number of bytes.
    /// </summary>
    public AudioFormat(int sampleRate, int channels, int bitsPerSample)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bitsPerSample);
        if (bitsPerSample % 8 != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitsPerSample), bitsPerSample,
                "Bit depth must be a whole number of bytes (a multiple of 8).");
        }

        SampleRate = sampleRate;
        Channels = channels;
        BitsPerSample = bitsPerSample;
    }

    /// <summary>Samples per second, per channel (for example 16000 or 24000).</summary>
    public int SampleRate { get; }

    /// <summary>Number of interleaved channels (1 for mono, 2 for stereo).</summary>
    public int Channels { get; }

    /// <summary>Bits in one sample of one channel (16 throughout this library).</summary>
    public int BitsPerSample { get; }

    /// <summary>Bytes in one sample of one channel.</summary>
    public int BytesPerSample => BitsPerSample / 8;

    /// <summary>
    /// Bytes in one interleaved frame (one sample across every channel). PCM data
    /// length is always a whole number of these.
    /// </summary>
    public int BlockAlign => Channels * BytesPerSample;

    /// <summary>Bytes of PCM that represent one second of this audio.</summary>
    public int BytesPerSecond => SampleRate * BlockAlign;

    /// <summary>The duration of <paramref name="byteCount"/> bytes of this audio.</summary>
    public TimeSpan DurationOf(long byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        return TimeSpan.FromSeconds((double)byteCount / BytesPerSecond);
    }

    /// <summary>A single-channel 16-bit PCM format at <paramref name="sampleRate"/>.</summary>
    public static AudioFormat Pcm16Mono(int sampleRate) => new(sampleRate, 1, 16);

    /// <summary>16-bit mono PCM at 16000 Hz — the on-device recognizer's input rate.</summary>
    public static AudioFormat Pcm16Mono16k { get; } = Pcm16Mono(16_000);

    /// <summary>16-bit mono PCM at 24000 Hz — the on-device HD synthesizer's output rate.</summary>
    public static AudioFormat Pcm16Mono24k { get; } = Pcm16Mono(24_000);
}

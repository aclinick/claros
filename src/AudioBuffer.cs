namespace Windows.Speech;

/// <summary>
/// An immutable chunk of 16-bit PCM audio together with the <see cref="AudioFormat"/>
/// that describes it. This is the unit that flows through the streaming speech
/// interfaces (<see cref="IAudioSource"/>, <see cref="IAudioSink"/>): a synthesizer
/// produces buffers, a recognizer consumes them, and the conversation loop moves
/// them between a microphone and a speaker.
/// </summary>
/// <remarks>
/// The buffer owns its bytes as a read-only span of little-endian 16-bit samples.
/// It centralizes the PCM&lt;-&gt;normalized-float conversions that were previously
/// scattered across the WAV reader and the transcriber, so every audio path uses
/// one definition of "sample". Encoding rounds a clamped [-1, 1] float to a signed
/// 16-bit value using the 32767 full-scale factor; decoding divides by 32768. This
/// mirrors the historical conversions and is lossless to within one least
/// significant bit (bit-exact except at the ±full-scale extremes).
/// </remarks>
public sealed class AudioBuffer
{
    private readonly ReadOnlyMemory<byte> _pcm;

    /// <summary>
    /// Wraps <paramref name="pcm"/> (little-endian 16-bit PCM) as a buffer of
    /// <paramref name="format"/>. The format must be 16-bit and the byte length
    /// must be a whole number of interleaved frames. The bytes are copied so the
    /// buffer is genuinely immutable and unaffected by later changes to the
    /// caller's memory.
    /// </summary>
    public AudioBuffer(ReadOnlyMemory<byte> pcm, AudioFormat format)
    {
        Validate(pcm.Length, format);
        // Defensive copy: the public contract promises an immutable buffer, so it
        // cannot alias caller-owned (and thus mutable) memory.
        _pcm = pcm.ToArray();
        Format = format;
    }

    // Trusted factory for arrays this type has just allocated and exclusively
    // owns (for example the output of FromSamples). Skips the defensive copy
    // while still validating, so the hot encode path allocates only once.
    private AudioBuffer(byte[] owned, AudioFormat format, bool _)
    {
        Validate(owned.Length, format);
        _pcm = owned;
        Format = format;
    }

    internal static AudioBuffer FromOwned(byte[] owned, AudioFormat format)
    {
        ArgumentNullException.ThrowIfNull(owned);
        return new AudioBuffer(owned, format, true);
    }

    private static void Validate(int length, AudioFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (format.BitsPerSample != 16)
        {
            throw new ArgumentException(
                $"AudioBuffer holds 16-bit PCM; the format is {format.BitsPerSample}-bit.",
                nameof(format));
        }
        if (length % format.BlockAlign != 0)
        {
            throw new ArgumentException(
                $"PCM length ({length}) is not a whole number of {format.BlockAlign}-byte frames.",
                nameof(format));
        }
    }

    /// <summary>The layout of this buffer's audio.</summary>
    public AudioFormat Format { get; }

    /// <summary>The raw little-endian 16-bit PCM bytes.</summary>
    public ReadOnlyMemory<byte> Pcm => _pcm;

    /// <summary>The number of interleaved frames (samples per channel).</summary>
    public int FrameCount => _pcm.Length / Format.BlockAlign;

    /// <summary>Whether this buffer carries no audio.</summary>
    public bool IsEmpty => _pcm.Length == 0;

    /// <summary>The duration of this buffer's audio.</summary>
    public TimeSpan Duration => Format.DurationOf(_pcm.Length);

    /// <summary>An empty buffer in <paramref name="format"/>.</summary>
    public static AudioBuffer Empty(AudioFormat format) =>
        new(ReadOnlyMemory<byte>.Empty, format);

    /// <summary>
    /// Creates a buffer from existing little-endian 16-bit PCM bytes. Alias for the
    /// constructor that reads clearly at call sites producing raw PCM.
    /// </summary>
    public static AudioBuffer FromPcm16(ReadOnlyMemory<byte> pcm, AudioFormat format) =>
        new(pcm, format);

    /// <summary>
    /// Creates a buffer by encoding normalized float <paramref name="samples"/> (in
    /// the range [-1, 1], interleaved across <paramref name="format"/>'s channels)
    /// to 16-bit PCM. Values outside the range are clamped.
    /// </summary>
    public static AudioBuffer FromSamples(ReadOnlySpan<float> samples, AudioFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (format.BitsPerSample != 16)
        {
            throw new ArgumentException(
                $"AudioBuffer holds 16-bit PCM; the format is {format.BitsPerSample}-bit.",
                nameof(format));
        }
        if (samples.Length % format.Channels != 0)
        {
            throw new ArgumentException(
                $"Sample count ({samples.Length}) is not a whole number of {format.Channels}-channel frames.",
                nameof(samples));
        }

        var bytes = new byte[samples.Length * sizeof(short)];
        var span = bytes.AsSpan();
        for (var i = 0; i < samples.Length; i++)
        {
            var clamped = Math.Clamp(samples[i], -1f, 1f);
            var value = (short)Math.Round(clamped * 32767f);
            span[i * 2] = (byte)(value & 0xFF);
            span[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }
        return AudioBuffer.FromOwned(bytes, format);
    }

    /// <summary>
    /// Decodes this buffer's 16-bit PCM into normalized float samples in the range
    /// [-1, 1), interleaved across channels (dividing by 32768).
    /// </summary>
    public float[] ToSamples()
    {
        var pcm = _pcm.Span;
        var count = pcm.Length / sizeof(short);
        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            var value = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
            samples[i] = value / 32768f;
        }
        return samples;
    }
}

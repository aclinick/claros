using WindowsNaturalVoices;

namespace WindowsNaturalVoices.Tests;

public class AudioFormatTests
{
    [Fact]
    public void DerivedValues_AreComputedFromLayout()
    {
        var format = new AudioFormat(16_000, 1, 16);

        Assert.Equal(2, format.BytesPerSample);
        Assert.Equal(2, format.BlockAlign);
        Assert.Equal(32_000, format.BytesPerSecond);
    }

    [Fact]
    public void BlockAlign_AccountsForChannels()
    {
        var stereo = new AudioFormat(48_000, 2, 16);

        Assert.Equal(4, stereo.BlockAlign);
        Assert.Equal(192_000, stereo.BytesPerSecond);
    }

    [Fact]
    public void DurationOf_ConvertsBytesToTime()
    {
        var format = AudioFormat.Pcm16Mono(16_000);

        // One second is BytesPerSecond bytes.
        Assert.Equal(TimeSpan.FromSeconds(1), format.DurationOf(format.BytesPerSecond));
        Assert.Equal(TimeSpan.Zero, format.DurationOf(0));
    }

    [Theory]
    [InlineData(0, 1, 16)]
    [InlineData(16_000, 0, 16)]
    [InlineData(16_000, 1, 0)]
    [InlineData(-1, 1, 16)]
    public void Constructor_RejectsNonPositiveValues(int rate, int channels, int bits)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioFormat(rate, channels, bits));
    }

    [Fact]
    public void Constructor_RejectsNonByteAlignedBitDepth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioFormat(16_000, 1, 12));
    }

    [Fact]
    public void Presets_HaveExpectedLayout()
    {
        Assert.Equal(new AudioFormat(16_000, 1, 16), AudioFormat.Pcm16Mono16k);
        Assert.Equal(new AudioFormat(24_000, 1, 16), AudioFormat.Pcm16Mono24k);
    }

    [Fact]
    public void Equality_IsByValue()
    {
        Assert.Equal(new AudioFormat(16_000, 1, 16), new AudioFormat(16_000, 1, 16));
        Assert.NotEqual(new AudioFormat(16_000, 1, 16), new AudioFormat(24_000, 1, 16));
    }
}

public class AudioBufferTests
{
    private static readonly AudioFormat Mono16k = AudioFormat.Pcm16Mono(16_000);

    [Fact]
    public void FrameCountAndDuration_ReflectPcmLength()
    {
        // 16000 mono frames = 32000 bytes = 1 second.
        var buffer = new AudioBuffer(new byte[32_000], Mono16k);

        Assert.Equal(16_000, buffer.FrameCount);
        Assert.Equal(TimeSpan.FromSeconds(1), buffer.Duration);
        Assert.False(buffer.IsEmpty);
    }

    [Fact]
    public void Empty_HasNoAudio()
    {
        var buffer = AudioBuffer.Empty(Mono16k);

        Assert.True(buffer.IsEmpty);
        Assert.Equal(0, buffer.FrameCount);
        Assert.Equal(TimeSpan.Zero, buffer.Duration);
    }

    [Fact]
    public void Constructor_RejectsNon16BitFormat()
    {
        Assert.Throws<ArgumentException>(() => new AudioBuffer(new byte[8], new AudioFormat(16_000, 1, 8)));
    }

    [Fact]
    public void Constructor_RejectsUnalignedPcmLength()
    {
        // Odd byte count cannot be a whole number of 2-byte frames.
        Assert.Throws<ArgumentException>(() => new AudioBuffer(new byte[3], Mono16k));
    }

    [Fact]
    public void Decode_KnownPcmToSamples()
    {
        // 0x4000 = 16384 -> 16384/32768 = 0.5 exactly; 0x0000 = 0.
        var pcm = new byte[] { 0x00, 0x00, 0x00, 0x40 };
        var samples = new AudioBuffer(pcm, Mono16k).ToSamples();

        Assert.Equal(2, samples.Length);
        Assert.Equal(0f, samples[0]);
        Assert.Equal(0.5f, samples[1], 5);
    }

    [Fact]
    public void FromSamples_ThenToSamples_RoundTripsWithinOneLsb()
    {
        var original = new[] { 0f, 0.25f, 0.5f, -0.5f, 0.9f, -0.9f };
        var buffer = AudioBuffer.FromSamples(original, Mono16k);
        var decoded = buffer.ToSamples();

        Assert.Equal(original.Length, decoded.Length);
        for (var i = 0; i < original.Length; i++)
        {
            Assert.Equal(original[i], decoded[i], 0.001);
        }
    }

    [Fact]
    public void FromSamples_ClampsOutOfRangeValues()
    {
        var buffer = AudioBuffer.FromSamples(new[] { 2f, -2f }, Mono16k);
        var decoded = buffer.ToSamples();

        Assert.Equal(1f, decoded[0], 0.001);
        Assert.Equal(-1f, decoded[1], 0.001);
    }

    [Fact]
    public void FromSamples_RejectsPartialFrameForMultichannel()
    {
        var stereo = new AudioFormat(48_000, 2, 16);
        Assert.Throws<ArgumentException>(() => AudioBuffer.FromSamples(new[] { 0.1f, 0.2f, 0.3f }, stereo));
    }

    [Fact]
    public void FromPcm16_WrapsBytes()
    {
        var pcm = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var buffer = AudioBuffer.FromPcm16(pcm, Mono16k);

        Assert.Equal(pcm, buffer.Pcm.ToArray());
        Assert.Equal(2, buffer.FrameCount);
    }

    [Fact]
    public void Buffer_IsImmutableAgainstCallerMutation()
    {
        var pcm = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        var buffer = AudioBuffer.FromPcm16(pcm, Mono16k);

        // Mutating the caller's array must not change the buffer's contents.
        pcm[0] = 0xFF;

        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44 }, buffer.Pcm.ToArray());
    }
}

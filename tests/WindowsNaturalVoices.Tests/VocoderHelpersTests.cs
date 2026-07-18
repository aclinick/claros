namespace WindowsNaturalVoices.Tests;

public class VocoderHelpersTests
{
    [Fact]
    public void ToChannelMajor_RearrangesInterleavedStepsToChannelMajor()
    {
        // Interleaved by step: step0(ch0,ch1), step1(ch0,ch1), step2(ch0,ch1)
        var interleaved = new long[] { 10, 20, 11, 21, 12, 22 };

        var result = Vocoder.ToChannelMajor(interleaved);

        // Channel-major: all ch0 then all ch1
        Assert.Equal(new long[] { 10, 11, 12, 20, 21, 22 }, result);
    }

    [Fact]
    public void ToChannelMajor_HandlesSingleStep()
    {
        var result = Vocoder.ToChannelMajor(new long[] { 5, 6 });

        Assert.Equal(new long[] { 5, 6 }, result);
    }

    [Fact]
    public void Normalize_ScalesToRequestedPeak()
    {
        var samples = new[] { 0.5f, -0.25f, 0.1f };

        Vocoder.Normalize(samples, 0.9f);

        // max abs is 0.5, so scale = 0.9 / 0.5 = 1.8
        Assert.Equal(0.9f, samples[0], 5);
        Assert.Equal(-0.45f, samples[1], 5);
        Assert.Equal(0.18f, samples[2], 5);
    }

    [Fact]
    public void Normalize_LeavesAllZeroBufferUnchanged()
    {
        var samples = new[] { 0f, 0f, 0f };

        Vocoder.Normalize(samples, 0.9f);

        Assert.All(samples, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Normalize_UsesAbsolutePeakFromNegativeSample()
    {
        var samples = new[] { 0.2f, -0.8f };

        Vocoder.Normalize(samples, 1.0f);

        // peak abs is 0.8, scale = 1.25
        Assert.Equal(0.25f, samples[0], 5);
        Assert.Equal(-1.0f, samples[1], 5);
    }
}

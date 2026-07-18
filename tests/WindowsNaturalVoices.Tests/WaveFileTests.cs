using System.Text;

namespace WindowsNaturalVoices.Tests;

public class WaveFileTests
{
    private static (int sampleRate, int dataBytes, short[] samples) ReadWav(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(bytes, 12, 4));

        var channels = BitConverter.ToInt16(bytes, 22);
        var sampleRate = BitConverter.ToInt32(bytes, 24);
        var bitsPerSample = BitConverter.ToInt16(bytes, 34);
        Assert.Equal(1, channels);
        Assert.Equal(16, bitsPerSample);

        Assert.Equal("data", Encoding.ASCII.GetString(bytes, 36, 4));
        var dataBytes = BitConverter.ToInt32(bytes, 40);

        var samples = new short[dataBytes / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToInt16(bytes, 44 + i * 2);
        }
        return (sampleRate, dataBytes, samples);
    }

    [Fact]
    public void WriteMono16_WritesHeaderWithGivenSampleRate()
    {
        using var file = TempFile.Create(".wav");
        WaveFile.WriteMono16(file.Path, new[] { 0f, 0.5f, -0.5f }, 26000);

        var (sampleRate, dataBytes, _) = ReadWav(file.Path);

        Assert.Equal(26000, sampleRate);
        Assert.Equal(6, dataBytes); // 3 samples * 2 bytes
    }

    [Fact]
    public void WriteMono16_ConvertsFloatSamplesToInt16()
    {
        using var file = TempFile.Create(".wav");
        WaveFile.WriteMono16(file.Path, new[] { 0f, 1f, -1f }, 24000);

        var (_, _, samples) = ReadWav(file.Path);

        Assert.Equal(0, samples[0]);
        Assert.Equal(32767, samples[1]);
        Assert.Equal(-32767, samples[2]);
    }

    [Fact]
    public void WriteMono16_ClampsOutOfRangeSamples()
    {
        using var file = TempFile.Create(".wav");
        WaveFile.WriteMono16(file.Path, new[] { 2f, -2f }, 24000);

        var (_, _, samples) = ReadWav(file.Path);

        Assert.Equal(32767, samples[0]);
        Assert.Equal(-32768, samples[1]);
    }

    [Fact]
    public void WriteMono16_HonoursRewrappedSampleRate()
    {
        // Writing the same samples with a different header rate is the documented
        // re-pitch trick used by the demo (26 kHz samples wrapped as 24 kHz).
        using var file = TempFile.Create(".wav");
        WaveFile.WriteMono16(file.Path, new[] { 0.1f, 0.2f }, 24000);

        var (sampleRate, _, _) = ReadWav(file.Path);

        Assert.Equal(24000, sampleRate);
    }

    [Fact]
    public void WriteMono16_ThrowsOnNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => WaveFile.WriteMono16(null!, new[] { 0f }, 24000));
        using var file = TempFile.Create(".wav");
        Assert.Throws<ArgumentNullException>(() => WaveFile.WriteMono16(file.Path, null!, 24000));
    }
}

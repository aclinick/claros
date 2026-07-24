using System.Text;

namespace Windows.Speech.Tests;

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

    [Fact]
    public void ReadMono16_RoundTripsWrittenSamples()
    {
        using var file = TempFile.Create(".wav");
        var original = new[] { 0f, 0.5f, -0.5f, 0.25f };
        WaveFile.WriteMono16(file.Path, original, 24000);

        var (samples, sampleRate) = WaveFile.ReadMono16(File.ReadAllBytes(file.Path));

        Assert.Equal(24000, sampleRate);
        Assert.Equal(original.Length, samples.Length);
        for (var i = 0; i < original.Length; i++)
        {
            Assert.True(Math.Abs(original[i] - samples[i]) < 0.001f);
        }
    }

    [Fact]
    public void ReadMono16_WalksExtraChunksBeforeData()
    {
        // The Embedded Speech runtime emits a LIST chunk before the data chunk;
        // the reader must walk chunks rather than assume a fixed offset.
        var wav = BuildWavWithListChunk(24000, new short[] { 100, -100, 200 });

        var (samples, sampleRate) = WaveFile.ReadMono16(wav);

        Assert.Equal(24000, sampleRate);
        Assert.Equal(3, samples.Length);
        Assert.True(Math.Abs(100 / 32768f - samples[0]) < 0.001f);
    }

    [Fact]
    public void ReadMono16_ThrowsOnNonRiff()
    {
        Assert.Throws<ArgumentException>(() => WaveFile.ReadMono16(new byte[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public void ReadMono16_ThrowsOnNonPcmFormat()
    {
        // IEEE float (format tag 3) is 16-bit mono but must not be read as PCM.
        var wav = BuildWav(24000, formatTag: 3, new short[] { 1, 2, 3 });

        Assert.Throws<ArgumentException>(() => WaveFile.ReadMono16(wav));
    }

    [Fact]
    public void ReadMono16_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => WaveFile.ReadMono16(null!));
    }

    private static byte[] BuildWav(int sampleRate, short formatTag, short[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var dataBytes = samples.Length * sizeof(short);

        w.Write("RIFF"u8);
        w.Write(4 + (8 + 16) + (8 + dataBytes));
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write(formatTag); w.Write((short)1);
        w.Write(sampleRate); w.Write(sampleRate * 2);
        w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(dataBytes);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildWavWithListChunk(int sampleRate, short[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var dataBytes = samples.Length * sizeof(short);
        var listBytes = 8; // arbitrary padded content

        w.Write("RIFF"u8);
        w.Write(4 + (8 + 16) + (8 + listBytes) + (8 + dataBytes));
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1); w.Write((short)1);
        w.Write(sampleRate); w.Write(sampleRate * 2);
        w.Write((short)2); w.Write((short)16);
        w.Write("LIST"u8); w.Write(listBytes); w.Write(new byte[listBytes]);
        w.Write("data"u8); w.Write(dataBytes);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }
}

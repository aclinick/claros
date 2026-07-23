namespace WindowsNaturalVoices.Tests;

public class WavFileAudioSourceTests
{
    private static string WriteTempWav(float[] samples, int sampleRate)
    {
        var path = Path.Combine(Path.GetTempPath(), $"wnv-src-{Guid.NewGuid():N}.wav");
        WaveFile.WriteMono16(path, samples, sampleRate);
        return path;
    }

    [Fact]
    public async Task Replays_AllSamples_InChunks_WithWavFormat()
    {
        // 1 second at 16 kHz.
        var samples = new float[16_000];
        for (var i = 0; i < samples.Length; i++) samples[i] = 0.25f;
        var path = WriteTempWav(samples, 16_000);
        try
        {
            var source = new WavFileAudioSource(path, TimeSpan.FromMilliseconds(100), realtime: false);
            Assert.Equal(16_000, source.Format.SampleRate);
            Assert.Equal(1, source.Format.Channels);

            var total = 0;
            var chunks = 0;
            await foreach (var buffer in source.ReadAsync())
            {
                Assert.True(buffer.Format.Equals(source.Format));
                total += buffer.FrameCount;
                chunks++;
            }

            Assert.Equal(16_000, total);   // 100 ms x 10 chunks = 1 s
            Assert.Equal(10, chunks);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LastChunk_IsShorter_WhenNotEvenlyDivisible()
    {
        // 250 ms at 16 kHz = 4000 samples; 100 ms chunks -> 1600,1600,800.
        var samples = new float[4_000];
        var path = WriteTempWav(samples, 16_000);
        try
        {
            var source = new WavFileAudioSource(path, TimeSpan.FromMilliseconds(100), realtime: false);
            var counts = new List<int>();
            await foreach (var buffer in source.ReadAsync())
                counts.Add(buffer.FrameCount);

            Assert.Equal([1600, 1600, 800], counts);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Cancellation_StopsReplay()
    {
        var samples = new float[16_000];
        var path = WriteTempWav(samples, 16_000);
        try
        {
            var source = new WavFileAudioSource(path, TimeSpan.FromMilliseconds(100), realtime: false);
            using var cts = new CancellationTokenSource();
            var seen = 0;
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in source.ReadAsync(cts.Token))
                {
                    if (++seen == 2) cts.Cancel();
                }
            });
            Assert.Equal(2, seen);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsNonPositiveChunk()
    {
        var path = WriteTempWav(new float[16], 16_000);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new WavFileAudioSource(path, TimeSpan.Zero));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

using Claros;

namespace Claros.Tests;

public class BufferedAudioSinkTests
{
    private static readonly AudioFormat Mono16k = AudioFormat.Pcm16Mono(16_000);

    [Fact]
    public async Task Write_CollectsBuffersInOrder()
    {
        var sink = new BufferedAudioSink(Mono16k);
        await sink.WriteAsync(AudioBuffer.FromPcm16(new byte[] { 1, 0 }, Mono16k));
        await sink.WriteAsync(AudioBuffer.FromPcm16(new byte[] { 2, 0 }, Mono16k));

        Assert.Equal(2, sink.Buffers.Count);
        Assert.Equal(new byte[] { 1, 0, 2, 0 }, sink.ToPcm());
    }

    [Fact]
    public async Task Write_IgnoresEmptyBuffers()
    {
        var sink = new BufferedAudioSink(Mono16k);
        await sink.WriteAsync(AudioBuffer.Empty(Mono16k));

        Assert.Empty(sink.Buffers);
    }

    [Fact]
    public async Task Write_RejectsMismatchedFormat()
    {
        var sink = new BufferedAudioSink(Mono16k);
        var wrong = AudioBuffer.FromPcm16(new byte[] { 1, 0 }, AudioFormat.Pcm16Mono(24_000));

        await Assert.ThrowsAsync<ArgumentException>(async () => await sink.WriteAsync(wrong));
    }

    [Fact]
    public async Task Complete_BlocksFurtherWrites()
    {
        var sink = new BufferedAudioSink(Mono16k);
        await sink.CompleteAsync();

        Assert.True(sink.IsCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sink.WriteAsync(AudioBuffer.FromPcm16(new byte[] { 1, 0 }, Mono16k)));
    }

    [Fact]
    public async Task Complete_IsIdempotent()
    {
        var sink = new BufferedAudioSink(Mono16k);
        await sink.CompleteAsync();
        await sink.CompleteAsync();

        Assert.True(sink.IsCompleted);
    }

    [Fact]
    public async Task ToWaveform_ReturnsMonoSamples()
    {
        var sink = new BufferedAudioSink(Mono16k);
        await sink.WriteAsync(AudioBuffer.FromSamples(new[] { 0.5f, -0.5f }, Mono16k));

        var waveform = sink.ToWaveform();

        Assert.Equal(16_000, waveform.SampleRate);
        Assert.Equal(2, waveform.Samples.Length);
        Assert.Equal(0.5f, waveform.Samples[0], 0.001);
    }

    [Fact]
    public async Task ToWaveform_RejectsMultichannel()
    {
        var stereo = new AudioFormat(48_000, 2, 16);
        var sink = new BufferedAudioSink(stereo);
        await sink.WriteAsync(AudioBuffer.FromPcm16(new byte[] { 1, 0, 2, 0 }, stereo));

        Assert.Throws<InvalidOperationException>(() => sink.ToWaveform());
    }
}

public class AudioSourceTests
{
    private static readonly AudioFormat Mono16k = AudioFormat.Pcm16Mono(16_000);

    [Fact]
    public async Task FromBuffers_YieldsBuffersInOrder()
    {
        var buffers = new[]
        {
            AudioBuffer.FromPcm16(new byte[] { 1, 0 }, Mono16k),
            AudioBuffer.FromPcm16(new byte[] { 2, 0 }, Mono16k),
            AudioBuffer.FromPcm16(new byte[] { 3, 0 }, Mono16k),
        };
        var source = AudioSource.FromBuffers(Mono16k, buffers);

        var seen = new List<byte>();
        await foreach (var buffer in source.ReadAsync())
        {
            seen.Add(buffer.Pcm.Span[0]);
        }

        Assert.Equal(new byte[] { 1, 2, 3 }, seen);
        Assert.Equal(Mono16k, source.Format);
    }

    [Fact]
    public async Task ReadAsync_HonorsCancellation()
    {
        var source = AudioSource.FromBuffers(
            Mono16k, new[] { AudioBuffer.FromPcm16(new byte[] { 1, 0 }, Mono16k) });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in source.ReadAsync(cts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task FromBuffers_RejectsMismatchedFormat()
    {
        var source = AudioSource.FromBuffers(
            Mono16k, new[] { AudioBuffer.FromPcm16(new byte[] { 1, 0 }, AudioFormat.Pcm16Mono(24_000)) });

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in source.ReadAsync())
            {
            }
        });
    }
}

using Claros.Internal;

namespace Claros.Tests;

public class TimedNarratorRenderTests
{
    // A deterministic in-memory synthesizer: every request yields a clip of a
    // fixed length filled with 1.0, at a small sample rate so placements are easy
    // to reason about (100 Hz => 1 sample = 10 ms).
    private sealed class FakeSynthesizer : ISpeechSynthesizer
    {
        private readonly int _clipSamples;
        public FakeSynthesizer(int clipSamples = 10) => _clipSamples = clipSamples;

        public int SampleRate { get; init; } = 100;
        public List<string> Requests { get; } = [];

        public VoiceInfo Voice { get; } = new(
            "id", "Fake", "en-US", "Female", "Adult", "Test", "1", "pfn", "pfull", "path");
        public AudioFormat OutputFormat => AudioFormat.Pcm16Mono(SampleRate);

        public Task<WaveformResult> SynthesizeAsync(
            SpeechSynthesisRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.Content);
            var samples = new float[_clipSamples];
            Array.Fill(samples, 1.0f);
            return Task.FromResult(new WaveformResult(samples, SampleRate));
        }

        public Task SynthesizeToSinkAsync(
            SpeechSynthesisRequest request, IAudioSink sink,
            Action<SpokenWord>? onWord = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public bool WasDisposed { get; private set; }

        public void Dispose() { WasDisposed = true; }
    }

    [Fact]
    public void Dispose_BorrowedSynthesizer_IsLeftAlone()
    {
        // The public constructor borrows, so one warm synthesizer can drive
        // several narrators.
        var synth = new FakeSynthesizer();
        var narrator = new TimedNarrator(synth);

        narrator.Dispose();
        narrator.Dispose();

        Assert.False(synth.WasDisposed);
    }

    [Fact]
    public void Dispose_OwnedSynthesizer_IsReleasedExactlyOnce()
    {
        var synth = new FakeSynthesizer();
        var narrator = new TimedNarrator(synth, owned: synth);

        narrator.Dispose();
        narrator.Dispose();

        Assert.True(synth.WasDisposed);
        Assert.Equal(synth.Voice, narrator.Voice);
    }

    // An engine that declares one rate and returns another - the misbehaviour that
    // OutputFormat makes detectable.
    private sealed class UnstableRateSynthesizer : ISpeechSynthesizer
    {
        public int Calls { get; private set; }

        public VoiceInfo Voice { get; } = VoiceInfo.Cloud("hosted", "Hosted", "en-US");
        public AudioFormat OutputFormat => AudioFormat.Pcm16Mono(100);

        public Task<WaveformResult> SynthesizeAsync(
            SpeechSynthesisRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            // Declared 100 Hz above; returns 200 Hz.
            return Task.FromResult(new WaveformResult(new float[10], 200));
        }

        public Task SynthesizeToSinkAsync(
            SpeechSynthesisRequest request, IAudioSink sink,
            Action<SpokenWord>? onWord = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Dispose() { }
    }

    [Fact]
    public async Task RenderAsync_DetectsAnEngineThatBreaksItsDeclaredOutputFormat()
    {
        // The timeline is laid out at the declared rate, so a clip that comes back
        // at a different rate cannot be placed. Previously this was guarded by a
        // StableSampleRate capability flag, which contradicted OutputFormat: an
        // engine cannot both promise a fixed format and reserve the right to vary.
        var synth = new UnstableRateSynthesizer();
        var narrator = new TimedNarrator(synth);
        var cues = new[] { new TimedCue(0, TimeSpan.Zero, TimeSpan.FromSeconds(1), "hello") };

        var ex = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => narrator.RenderAsync(cues));

        Assert.Contains("declares 100 Hz", ex.Message);
        Assert.Contains("returned 200 Hz", ex.Message);
    }

    [Fact]
    public async Task RenderAsync_NoCues_ReturnsEmptyWaveformAtTheDeclaredRate()
    {
        var narrator = new TimedNarrator(new FakeSynthesizer());

        var result = await narrator.RenderAsync([]);

        Assert.Empty(result.Samples);
        // The rate comes from OutputFormat, not from a clip, so even an empty
        // render is a valid, writable waveform rather than a 0 Hz one.
        Assert.Equal(100, result.SampleRate);
    }

    [Fact]
    public async Task RenderAsync_WhitespaceCue_SkipsSynthesisButHonorsEndLength()
    {
        var fake = new FakeSynthesizer();
        var narrator = new TimedNarrator(fake);
        var cues = new[] { new TimedCue(1, TimeSpan.Zero, TimeSpan.FromSeconds(1), "   ") };

        var result = await narrator.RenderAsync(cues, new TimedNarrationOptions { GroupIntoSentences = false });

        Assert.Empty(fake.Requests);        // nothing synthesized
        // Knowing the rate up front means the cue's duration is still reserved:
        // 1 s of silence at 100 Hz. Previously this returned nothing at all,
        // because the rate could only be learned from a clip that never came.
        Assert.Equal(100, result.Samples.Length);
        Assert.All(result.Samples, s => Assert.Equal(0f, s));
        Assert.Equal(100, result.SampleRate);
    }

    [Fact]
    public async Task RenderAsync_PlacesEachClipAtItsStartOffset()
    {
        var fake = new FakeSynthesizer(clipSamples: 10); // 100ms clips at 100 Hz
        var narrator = new TimedNarrator(fake);
        var cues = new[]
        {
            new TimedCue(1, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(0.1), "a."),
            new TimedCue(2, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.1), "b."),
        };
        var options = new TimedNarrationOptions { GroupIntoSentences = false, FadeEdges = false };

        var result = await narrator.RenderAsync(cues, options);

        Assert.Equal(100, result.SampleRate);
        // Clip 1 at sample 0..9, clip 2 at sample 100..109, silence between.
        Assert.Equal(1.0f, result.Samples[0]);
        Assert.Equal(1.0f, result.Samples[9]);
        Assert.Equal(0.0f, result.Samples[10]);
        Assert.Equal(0.0f, result.Samples[99]);
        Assert.Equal(1.0f, result.Samples[100]);
        Assert.Equal(1.0f, result.Samples[109]);
    }

    [Fact]
    public async Task RenderAsync_OverlappingClips_AreAdditivelyMixed()
    {
        var fake = new FakeSynthesizer(clipSamples: 20); // 200ms clips
        var narrator = new TimedNarrator(fake);
        var cues = new[]
        {
            new TimedCue(1, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(0.2), "a."),
            new TimedCue(2, TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.3), "b."),
        };
        var options = new TimedNarrationOptions { GroupIntoSentences = false, FadeEdges = false };

        var result = await narrator.RenderAsync(cues, options);

        // Overlap window samples 10..19 carry both clips => 2.0.
        Assert.Equal(1.0f, result.Samples[0]);
        Assert.Equal(2.0f, result.Samples[10]);
        Assert.Equal(2.0f, result.Samples[19]);
        Assert.Equal(1.0f, result.Samples[20]);
    }

    [Fact]
    public async Task RenderAsync_TimelineRunsAtLeastToLastCueEnd()
    {
        var fake = new FakeSynthesizer(clipSamples: 5); // clip shorter than the cue span
        var narrator = new TimedNarrator(fake);
        var cues = new[] { new TimedCue(1, TimeSpan.Zero, TimeSpan.FromSeconds(2), "a.") };
        var options = new TimedNarrationOptions { GroupIntoSentences = false, FadeEdges = false };

        var result = await narrator.RenderAsync(cues, options);

        Assert.Equal(200, result.Samples.Length); // 2s * 100 Hz, though audio was only 5 samples
    }

    [Fact]
    public async Task RenderAsync_Grouping_MergesFragmentsIntoOneUtterance()
    {
        var fake = new FakeSynthesizer();
        var narrator = new TimedNarrator(fake);
        var cues = new[]
        {
            new TimedCue(1, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1), "Hello there"),
            new TimedCue(2, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "friend."),
        };

        await narrator.RenderAsync(cues); // grouping on by default

        Assert.Equal(["Hello there friend."], fake.Requests);
    }

    [Fact]
    public async Task RenderAsync_FadeEdges_SoftensClipStart()
    {
        var fake = new FakeSynthesizer(clipSamples: 40) { SampleRate = 1000 };
        var narrator = new TimedNarrator(fake);
        var cues = new[] { new TimedCue(1, TimeSpan.Zero, TimeSpan.FromSeconds(0.04), "a.") };
        var options = new TimedNarrationOptions { GroupIntoSentences = false, FadeEdges = true };

        var result = await narrator.RenderAsync(cues, options);

        Assert.Equal(0.0f, result.Samples[0]);        // fade-in starts at zero gain
        Assert.True(result.Samples[1] < 1.0f);        // ramping up
    }

    [Fact]
    public async Task RenderAsync_Cancellation_Throws()
    {
        var narrator = new TimedNarrator(new FakeSynthesizer());
        var cues = new[] { new TimedCue(1, TimeSpan.Zero, TimeSpan.FromSeconds(1), "a.") };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => narrator.RenderAsync(cues, null, cts.Token));
    }

    // A synthesizer whose sample rate changes between calls (should never happen for
    // a real single voice, but the mixer relies on one rate).
    private sealed class VaryingRateSynthesizer : ISpeechSynthesizer
    {
        private int _calls;
        public VoiceInfo Voice { get; } = new(
            "id", "Fake", "en-US", "Female", "Adult", "Test", "1", "pfn", "pfull", "path");

        // Declares one rate but does not honour it - the misbehaviour this fixture
        // exists to provoke.
        public AudioFormat OutputFormat => AudioFormat.Pcm16Mono(100);

        public Task<WaveformResult> SynthesizeAsync(
            SpeechSynthesisRequest request, CancellationToken cancellationToken = default)
        {
            var rate = _calls++ == 0 ? 24000 : 16000;
            return Task.FromResult(new WaveformResult(new float[4], rate));
        }

        public Task SynthesizeToSinkAsync(
            SpeechSynthesisRequest request, IAudioSink sink,
            Action<SpokenWord>? onWord = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Dispose() { }
    }

    [Fact]
    public async Task RenderAsync_InconsistentSampleRates_Throws()
    {
        var narrator = new TimedNarrator(new VaryingRateSynthesizer());
        var cues = new[]
        {
            new TimedCue(1, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1), "one."),
            new TimedCue(2, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "two."),
        };

        await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => narrator.RenderAsync(cues, new TimedNarrationOptions { GroupIntoSentences = false }));
    }
}

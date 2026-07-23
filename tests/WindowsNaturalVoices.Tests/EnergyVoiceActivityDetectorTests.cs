namespace WindowsNaturalVoices.Tests;

public class EnergyVoiceActivityDetectorTests
{
    private static readonly AudioFormat Format = AudioFormat.Pcm16Mono16k; // 16 kHz mono

    // Small, precise tuning: 10ms frames (160 samples), 2-frame start, 3-frame end.
    private static VoiceActivityOptions Options() => new()
    {
        FrameDuration = TimeSpan.FromMilliseconds(10),
        StartThreshold = 0.02,
        EndThreshold = 0.012,
        StartHangover = TimeSpan.FromMilliseconds(20),
        EndHangover = TimeSpan.FromMilliseconds(30),
    };

    private static AudioBuffer Tone(int frames, float amplitude = 0.3f)
    {
        var samples = new float[frames * 160]; // 160 samples per 10ms frame at 16 kHz
        Array.Fill(samples, amplitude);
        return AudioBuffer.FromSamples(samples, Format);
    }

    private static AudioBuffer Silence(int frames) =>
        AudioBuffer.FromSamples(new float[frames * 160], Format);

    [Fact]
    public void Process_SilenceOnly_RaisesNothing()
    {
        var vad = new EnergyVoiceActivityDetector(Format, Options());
        var started = 0; var ended = 0;
        vad.SpeechStarted += (_, _) => started++;
        vad.SpeechEnded += (_, _) => ended++;

        vad.Process(Silence(50));

        Assert.Equal(0, started);
        Assert.Equal(0, ended);
        Assert.False(vad.IsSpeaking);
    }

    [Fact]
    public void Process_ToneThenSilence_RaisesStartThenEnd()
    {
        var vad = new EnergyVoiceActivityDetector(Format, Options());
        SpeechActivityEventArgs? startArgs = null, endArgs = null;
        vad.SpeechStarted += (_, e) => startArgs = e;
        vad.SpeechEnded += (_, e) => endArgs = e;

        vad.Process(Tone(50));    // 500ms of speech
        Assert.True(vad.IsSpeaking);
        vad.Process(Silence(50)); // 500ms of silence

        Assert.NotNull(startArgs);
        Assert.NotNull(endArgs);
        // Start declared after the 20ms (2-frame) hangover.
        Assert.Equal(TimeSpan.FromMilliseconds(20), startArgs!.Position);
        // End declared 30ms into the silence, after 500ms of tone => 530ms.
        Assert.Equal(TimeSpan.FromMilliseconds(530), endArgs!.Position);
        Assert.False(vad.IsSpeaking);
    }

    [Fact]
    public void Process_HandlesFramesSplitAcrossBuffers()
    {
        var vad = new EnergyVoiceActivityDetector(Format, Options());
        var started = 0;
        vad.SpeechStarted += (_, _) => started++;

        // Feed 250 samples then 250 samples: neither is a whole number of 160-sample
        // frames, but together they span 3 full frames of tone (> 20ms hangover).
        var a = new float[250]; Array.Fill(a, 0.3f);
        var b = new float[250]; Array.Fill(b, 0.3f);
        vad.Process(AudioBuffer.FromSamples(a, Format));
        vad.Process(AudioBuffer.FromSamples(b, Format));

        Assert.Equal(1, started);
        Assert.True(vad.IsSpeaking);
    }

    [Fact]
    public void Process_OnlyRaisesStartOnce_ForContinuousSpeech()
    {
        var vad = new EnergyVoiceActivityDetector(Format, Options());
        var started = 0;
        vad.SpeechStarted += (_, _) => started++;

        vad.Process(Tone(10));
        vad.Process(Tone(10));
        vad.Process(Tone(10));

        Assert.Equal(1, started);
    }

    [Fact]
    public void Process_FormatMismatch_Throws()
    {
        var vad = new EnergyVoiceActivityDetector(Format, Options());
        var wrong = AudioBuffer.FromSamples(new float[160], AudioFormat.Pcm16Mono24k);

        Assert.Throws<ArgumentException>(() => vad.Process(wrong));
    }

    [Fact]
    public void Reset_ClearsStateAndPosition()
    {
        var vad = new EnergyVoiceActivityDetector(Format, Options());
        vad.Process(Tone(50));
        Assert.True(vad.IsSpeaking);

        vad.Reset();
        Assert.False(vad.IsSpeaking);

        // After reset the position counter restarts from zero.
        SpeechActivityEventArgs? startArgs = null;
        vad.SpeechStarted += (_, e) => startArgs = e;
        vad.Process(Tone(10));
        Assert.Equal(TimeSpan.FromMilliseconds(20), startArgs!.Position);
    }

    [Fact]
    public void Process_EmptyBuffer_IsNoOp()
    {
        var vad = new EnergyVoiceActivityDetector(Format, Options());
        vad.Process(AudioBuffer.Empty(Format));
        Assert.False(vad.IsSpeaking);
    }

    [Fact]
    public void Process_HandlerCallingReset_DoesNotCorruptState()
    {
        var vad = new EnergyVoiceActivityDetector(Format, Options());
        vad.SpeechStarted += (_, _) => vad.Reset(); // reentrant Reset from the handler

        // Tone long enough to also cross the end hangover would fire both events, but
        // the start handler resets first; the point is no exception / no bad state.
        var ex = Record.Exception(() => vad.Process(Tone(50)));

        Assert.Null(ex);
        Assert.False(vad.IsSpeaking); // reset took effect
    }
}

public class VoiceActivityOptionsValidationTests
{
    [Fact]
    public void Validate_RejectsNaNThresholds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new VoiceActivityOptions { StartThreshold = double.NaN }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new VoiceActivityOptions { EndThreshold = double.NaN, StartThreshold = 0.5 }.Validate());
    }

    [Fact]
    public void Validate_RejectsInfiniteThreshold()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new VoiceActivityOptions { StartThreshold = double.PositiveInfinity }.Validate());
    }

    [Fact]
    public void Validate_RejectsNonPositiveFrameDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new VoiceActivityOptions { FrameDuration = TimeSpan.Zero }.Validate());
    }

    [Fact]
    public void Validate_AcceptsDefault()
    {
        VoiceActivityOptions.Default.Validate(); // does not throw
    }
}

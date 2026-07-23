using WindowsNaturalVoices.Internal;

namespace WindowsNaturalVoices.Tests;

public class EnergyEndpointerTests
{
    private static VoiceActivityOptions Options(int startFrames, int endFrames) => new()
    {
        FrameDuration = TimeSpan.FromMilliseconds(10),
        StartThreshold = 0.5,
        EndThreshold = 0.2,
        StartHangover = TimeSpan.FromMilliseconds(10 * startFrames),
        EndHangover = TimeSpan.FromMilliseconds(10 * endFrames),
    };

    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(10);

    [Fact]
    public void Process_DeclaresStart_AfterStartHangover()
    {
        var ep = new EnergyEndpointer(Options(startFrames: 3, endFrames: 5));

        Assert.Equal(VadTransition.None, ep.Process(1.0, Frame));
        Assert.Equal(VadTransition.None, ep.Process(1.0, Frame));
        Assert.Equal(VadTransition.SpeechStarted, ep.Process(1.0, Frame)); // 3rd loud frame
        Assert.True(ep.IsSpeaking);
    }

    [Fact]
    public void Process_DeclaresEnd_AfterEndHangover()
    {
        var ep = new EnergyEndpointer(Options(startFrames: 1, endFrames: 3));
        ep.Process(1.0, Frame); // starts (1-frame hangover)
        Assert.True(ep.IsSpeaking);

        Assert.Equal(VadTransition.None, ep.Process(0.0, Frame));
        Assert.Equal(VadTransition.None, ep.Process(0.0, Frame));
        Assert.Equal(VadTransition.SpeechEnded, ep.Process(0.0, Frame)); // 3rd silent frame
        Assert.False(ep.IsSpeaking);
    }

    [Fact]
    public void Process_BriefSpike_DoesNotStart()
    {
        var ep = new EnergyEndpointer(Options(startFrames: 3, endFrames: 5));

        ep.Process(1.0, Frame);
        ep.Process(1.0, Frame);       // 2 loud, not yet started
        ep.Process(0.0, Frame);       // silence resets the accumulator
        Assert.Equal(VadTransition.None, ep.Process(1.0, Frame));
        Assert.Equal(VadTransition.None, ep.Process(1.0, Frame));
        Assert.Equal(VadTransition.SpeechStarted, ep.Process(1.0, Frame)); // needs a fresh run of 3
    }

    [Fact]
    public void Process_ShortPauseWithinSpeech_DoesNotEnd()
    {
        var ep = new EnergyEndpointer(Options(startFrames: 1, endFrames: 5));
        ep.Process(1.0, Frame); // started

        ep.Process(0.0, Frame);
        ep.Process(0.0, Frame);       // 2 silent, below the 5-frame end hangover
        ep.Process(1.0, Frame);       // loud again resets the silence accumulator
        ep.Process(0.0, Frame);
        ep.Process(0.0, Frame);
        ep.Process(0.0, Frame);
        Assert.True(ep.IsSpeaking);   // still speaking: never reached 5 consecutive silent frames
    }

    [Fact]
    public void Process_MidLevelEnergy_HoldsCurrentState_ViaHysteresis()
    {
        var ep = new EnergyEndpointer(Options(startFrames: 1, endFrames: 1));

        // 0.3 is between EndThreshold (0.2) and StartThreshold (0.5).
        Assert.Equal(VadTransition.None, ep.Process(0.3, Frame)); // in silence: not loud enough to start
        Assert.False(ep.IsSpeaking);

        ep.Process(1.0, Frame); // now speaking
        Assert.Equal(VadTransition.None, ep.Process(0.3, Frame)); // in speech: not quiet enough to end
        Assert.True(ep.IsSpeaking);
    }

    [Fact]
    public void Reset_ReturnsToSilence()
    {
        var ep = new EnergyEndpointer(Options(startFrames: 1, endFrames: 5));
        ep.Process(1.0, Frame);
        Assert.True(ep.IsSpeaking);

        ep.Reset();

        Assert.False(ep.IsSpeaking);
        // Accumulators cleared; with a 1-frame start hangover one loud frame restarts.
        Assert.Equal(VadTransition.SpeechStarted, ep.Process(1.0, Frame));
        Assert.True(ep.IsSpeaking);
    }

    [Fact]
    public void Constructor_RejectsStartBelowEndThreshold()
    {
        var bad = new VoiceActivityOptions { StartThreshold = 0.1, EndThreshold = 0.5 };
        Assert.Throws<ArgumentException>(() => new EnergyEndpointer(bad));
    }
}

public class AudioEnergyTests
{
    [Fact]
    public void Rms_OfSilence_IsZero()
    {
        Assert.Equal(0.0, AudioEnergy.Rms(new float[100]));
    }

    [Fact]
    public void Rms_OfEmpty_IsZero()
    {
        Assert.Equal(0.0, AudioEnergy.Rms([]));
    }

    [Fact]
    public void Rms_OfConstantAmplitude_EqualsThatAmplitude()
    {
        var samples = new float[64];
        Array.Fill(samples, 0.3f);
        Assert.Equal(0.3, AudioEnergy.Rms(samples), precision: 5);
    }

    [Fact]
    public void Rms_OfFullScaleSquareWave_IsOne()
    {
        var samples = new float[8];
        for (var i = 0; i < samples.Length; i++) samples[i] = i % 2 == 0 ? 1f : -1f;
        Assert.Equal(1.0, AudioEnergy.Rms(samples), precision: 5);
    }
}

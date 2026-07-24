using Microsoft.ML.OnnxRuntime;

namespace Windows.Speech.Tests;

public class OptionsAndRecordsTests
{
    [Fact]
    public void SynthesisOptions_HasReferencePipelineDefaults()
    {
        var options = new SynthesisOptions();

        Assert.Equal(800, options.MaxDecoderSteps);
        Assert.Equal(0.5f, options.StopThreshold);
        Assert.Equal(20, options.WarmupSteps);
    }

    [Fact]
    public void SynthesisOptions_SupportsInitOnlyOverrides()
    {
        var options = new SynthesisOptions { MaxDecoderSteps = 100, StopThreshold = 0.7f, WarmupSteps = 5 };

        Assert.Equal(100, options.MaxDecoderSteps);
        Assert.Equal(0.7f, options.StopThreshold);
        Assert.Equal(5, options.WarmupSteps);
    }

    [Fact]
    public void NaturalVoiceEngineOptions_DefaultsToBasicGraphOptimization()
    {
        var options = new NaturalVoiceEngineOptions();

        Assert.Equal(GraphOptimizationLevel.ORT_ENABLE_BASIC, options.GraphOptimizationLevel);
    }

    [Fact]
    public void WaveformResult_ExposesSamplesAndSampleRate()
    {
        var samples = new[] { 0.1f, 0.2f };
        var result = new WaveformResult(samples, Vocoder.NativeSampleRate);

        Assert.Same(samples, result.Samples);
        Assert.Equal(26000, result.SampleRate);
    }

    [Fact]
    public void CodecTokens_RecordEqualityUsesReferenceArrays()
    {
        var c20 = new long[] { 1, 2 };
        var c40 = new long[] { 3 };
        var a = new CodecTokens(c20, c40, Steps: 1, StoppedByGate: true);
        var b = new CodecTokens(c20, c40, Steps: 1, StoppedByGate: true);

        Assert.Equal(a, b);
        Assert.Equal(1, a.Steps);
        Assert.True(a.StoppedByGate);
    }

    [Fact]
    public void VoiceInfo_StoresAllDescriptorFields()
    {
        var voice = new VoiceInfo(
            Id: "id@fam",
            DisplayName: "Microsoft Ava",
            Locale: "en-US",
            Gender: "Female",
            Age: "Adult",
            Vendor: "Microsoft",
            Version: "1.0",
            PackageFamilyName: "fam",
            PackageFullName: "full",
            InstalledPath: @"C:\voices\ava");

        Assert.Equal("Microsoft Ava", voice.DisplayName);
        Assert.Equal("en-US", voice.Locale);
        Assert.Equal(@"C:\voices\ava", voice.InstalledPath);
    }

    [Fact]
    public void Vocoder_NativeSampleRateIs26kHz()
    {
        Assert.Equal(26000, Vocoder.NativeSampleRate);
    }

    [Fact]
    public void SpokenWord_StoresTextOffsetAndDuration()
    {
        var word = new SpokenWord("hello", TimeSpan.FromSeconds(1.5), TimeSpan.FromMilliseconds(400));

        Assert.Equal("hello", word.Text);
        Assert.Equal(TimeSpan.FromSeconds(1.5), word.Offset);
        Assert.Equal(TimeSpan.FromMilliseconds(400), word.Duration);
    }

    [Fact]
    public void SpokenWord_RecordEqualityComparesByValue()
    {
        var a = new SpokenWord("world", TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(250));
        var b = new SpokenWord("world", TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(250));
        var different = a with { Text = "word" };

        Assert.Equal(a, b);
        Assert.NotEqual(a, different);
    }
}

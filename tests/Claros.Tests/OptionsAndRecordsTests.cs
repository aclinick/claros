using Microsoft.ML.OnnxRuntime;

namespace Claros.Tests;

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
    public void VoiceInfo_DefaultsToOnDevice()
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

        Assert.Equal(VoiceSource.Device, voice.Source);
        Assert.True(voice.IsOnDevice);
    }

    [Fact]
    public void VoiceInfo_Cloud_IsNotOnDeviceAndHasNoPackageIdentity()
    {
        var voice = VoiceInfo.Cloud("mai-voice-2:ava", "Ava (hosted)", "en-US");

        Assert.Equal(VoiceSource.Cloud, voice.Source);
        Assert.False(voice.IsOnDevice);
        Assert.Equal("mai-voice-2:ava", voice.Id);
        Assert.Equal("Ava (hosted)", voice.DisplayName);
        Assert.Equal("en-US", voice.Locale);
        Assert.Empty(voice.PackageFamilyName);
        Assert.Empty(voice.PackageFullName);
        Assert.Empty(voice.InstalledPath);
    }

    [Theory]
    [InlineData("", "name", "en-US")]
    [InlineData("id", "", "en-US")]
    [InlineData("id", "name", "")]
    public void VoiceInfo_Cloud_RejectsMissingIdentity(string id, string displayName, string locale)
    {
        Assert.Throws<ArgumentException>(() => VoiceInfo.Cloud(id, displayName, locale));
    }

    [Fact]
    public void EmbeddedSpeechSynthesizer_Load_RejectsCloudVoiceLoudly()
    {
        var voice = VoiceInfo.Cloud("mai-voice-2:ava", "Ava (hosted)", "en-US");

        // The on-device loader must fail with a clear contract error rather than
        // probing the empty InstalledPath a cloud voice carries.
        var ex = Assert.Throws<ArgumentException>(() => EmbeddedSpeechSynthesizer.Load(voice));
        Assert.Equal("voice", ex.ParamName);
    }

    [Fact]
    public void VoiceInfo_KeepsTenArgumentPositionalShape()
    {
        // Guards the public record surface: the positional constructor and
        // Deconstruct must stay at ten parameters, so Source is an init property
        // rather than an eleventh positional parameter.
        var voice = new VoiceInfo(
            "id", "Ava", "en-US", "Female", "Adult", "Microsoft", "1.0",
            "fam", "full", @"C:\voices\ava");

        var (id, _, locale, _, _, _, _, _, _, path) = voice;

        Assert.Equal("id", id);
        Assert.Equal("en-US", locale);
        Assert.Equal(@"C:\voices\ava", path);
        Assert.Equal(VoiceSource.Device, voice.Source);
    }

    [Fact]
    public void VoiceInfo_SourceParticipatesInEquality()
    {
        var device = new VoiceInfo(
            "id", "Ava", "en-US", "Female", "Adult", "Microsoft", "1.0", "", "", "");
        var cloud = device with { Source = VoiceSource.Cloud };

        Assert.NotEqual(device, cloud);
        Assert.True(device.IsOnDevice);
        Assert.False(cloud.IsOnDevice);
    }

    public static TheoryData<string, Func<VoiceInfo, object>> OnDeviceLoaders() => new()
    {
        { "NaturalVoiceEngine", v => NaturalVoiceEngine.Load(v) },
        { "Vocoder", v => Vocoder.Load(v) },
        { "NaturalVoiceSynthesizer", v => NaturalVoiceSynthesizer.Load(v) },
        { "EmbeddedSpeechSynthesizer", v => EmbeddedSpeechSynthesizer.Load(v) },
    };

    [Theory]
    [MemberData(nameof(OnDeviceLoaders))]
    public void OnDeviceLoaders_RejectCloudVoice(string name, Func<VoiceInfo, object> load)
    {
        var voice = VoiceInfo.Cloud("mai-voice-2:ava", "Ava (hosted)", "en-US");

        var ex = Assert.Throws<ArgumentException>(() => load(voice));
        Assert.Equal("voice", ex.ParamName);
        Assert.Contains("on-device", ex.Message);
        Assert.NotNull(name);
    }

    [Theory]
    [MemberData(nameof(OnDeviceLoaders))]
    public void OnDeviceLoaders_RejectDeviceVoiceWithoutInstalledPath(
        string name, Func<VoiceInfo, object> load)
    {
        // A device voice with no InstalledPath would otherwise Path.Combine("", ...)
        // and silently probe the process working directory.
        var voice = new VoiceInfo(
            "id", "Ava", "en-US", "Female", "Adult", "Microsoft", "1.0", "", "", "");

        var ex = Assert.Throws<ArgumentException>(() => load(voice));
        Assert.Equal("voice", ex.ParamName);
        Assert.Contains("InstalledPath", ex.Message);
        Assert.NotNull(name);
    }

    [Fact]
    public void CodecTokens_RejectsA20HzStreamThatIsNotTwoPerStep()
    {
        // The vocoder reshapes the 20 Hz stream to [1, 2, steps], so a length
        // that is not steps*2 would silently produce a misaligned tensor.
        var ex = Assert.Throws<ArgumentException>(
            () => new CodecTokens(new long[] { 1, 2, 3 }, new long[] { 9 }, 1, false));

        Assert.Contains("two-per-step", ex.Message);
    }

    [Fact]
    public void CodecTokens_AllowsAnIndependent40HzStreamWidth()
    {
        // The 40 Hz stream is consumed as a flat [1, 1, length] tensor, so its
        // width per step is a model detail and must not be constrained.
        var tokens = new CodecTokens(new long[] { 1, 2 }, new long[] { 9 }, 1, true);

        Assert.Equal(1, tokens.Steps);
        Assert.Single(tokens.C40Hz);
    }

    [Fact]
    public void CodecTokens_RejectsNegativeStepsAndNullStreams()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CodecTokens([], [], -1, false));
        Assert.Throws<ArgumentNullException>(
            () => new CodecTokens(null!, [], 0, false));
        Assert.Throws<ArgumentNullException>(
            () => new CodecTokens([], null!, 0, false));
    }

    [Fact]
    public void WithSampleRate_RelabelsWithoutResampling()
    {
        var samples = new[] { 0.1f, 0.2f, 0.3f };
        var native = new WaveformResult(samples, 26000);

        var relabelled = native.WithSampleRate(24000);

        // Same audio data, re-pitched purely by declaring a different rate.
        Assert.Equal(24000, relabelled.SampleRate);
        Assert.Same(samples, relabelled.Samples);
        Assert.Equal(26000, native.SampleRate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void WithSampleRate_RejectsNonPositiveRates(int rate)
    {
        var native = new WaveformResult([0.1f], 26000);

        Assert.Throws<ArgumentOutOfRangeException>(() => native.WithSampleRate(rate));
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

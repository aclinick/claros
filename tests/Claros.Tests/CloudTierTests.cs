namespace Claros.Tests;

public class CloudTierTests
{
    private static CloudVoiceOptions Valid() => new()
    {
        SubscriptionKey = "key",
        Region = "eastus",
        VoiceName = "mai-voice-2-flash-en-us",
    };

    [Fact]
    public void Options_ValidateAcceptsACompleteConfiguration()
    {
        var options = Valid();

        options.Validate();

        Assert.Equal("en-US", options.Locale);
    }

    [Theory]
    [InlineData("SubscriptionKey")]
    [InlineData("Region")]
    [InlineData("VoiceName")]
    [InlineData("Locale")]
    public void Options_ValidateRejectsAMissingSetting(string missing)
    {
        // Fail at construction rather than on the first billed request.
        var options = missing switch
        {
            "SubscriptionKey" => Valid() with { SubscriptionKey = "  " },
            "Region" => Valid() with { Region = "" },
            "VoiceName" => Valid() with { VoiceName = "" },
            _ => Valid() with { Locale = "" },
        };

        var ex = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Equal(missing, ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(22_050)]
    [InlineData(44_100)]
    public void Options_ValidateRejectsAnUnsupportedSampleRate(int rate)
    {
        var options = Valid() with { SampleRate = rate };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
        Assert.Equal("SampleRate", ex.ParamName);
    }

    [Theory]
    [InlineData(16_000)]
    [InlineData(24_000)]
    [InlineData(48_000)]
    public void OutputFormats_AreAlwaysRiffContainers(int rate)
    {
        // Load-bearing: results are parsed with WaveFile.ReadMono16, which expects
        // a RIFF header. A raw-PCM format would be misread as though the first
        // samples were the header, so every supported rate must map to Riff*.
        var format = Claros.Internal.OutputFormats.Resolve(rate);

        Assert.StartsWith("Riff", format.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Options_ToStringNeverRevealsTheKey()
    {
        // A record's generated ToString prints every property, which would put the
        // Speech key into any log line, exception message, or debugger view that
        // rendered the options. PrintMembers is overridden to prevent that.
        var options = Valid() with { SubscriptionKey = "super-secret-key-value" };

        var text = options.ToString();

        Assert.DoesNotContain("super-secret-key-value", text, StringComparison.Ordinal);
        Assert.Contains("(redacted)", text, StringComparison.Ordinal);
        // The non-sensitive settings stay visible, so the type is still debuggable.
        Assert.Contains("eastus", text, StringComparison.Ordinal);
        Assert.Contains("mai-voice-2-flash-en-us", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_ToStringDistinguishesAnUnsetKey()
    {
        var text = (Valid() with { SubscriptionKey = "" }).ToString();

        Assert.Contains("(unset)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OnDeviceProfile_IsFreeOfflineAndFullyCapable()
    {
        var caps = SynthesizerCapabilities.OnDevice;

        Assert.True(caps.Offline);
        Assert.False(caps.Metered);
        Assert.True(caps.WordBoundaries);
        Assert.True(caps.StableSampleRate);
    }

    [Fact]
    public void HostedProfile_IsNetworkedAndBilled()
    {
        var caps = SynthesizerCapabilities.Hosted;

        Assert.False(caps.Offline);
        Assert.True(caps.Metered);
        // Still capable enough for caption highlighting and timeline mixing.
        Assert.True(caps.WordBoundaries);
        Assert.True(caps.StableSampleRate);
    }

    // An implementation written before capabilities existed, which does not
    // override the member.
    private sealed class LegacySynthesizer : ISpeechSynthesizer
    {
        public VoiceInfo Voice { get; } = new(
            "id", "Legacy", "en-US", "Female", "Adult", "Test", "1", "", "", "");

        public Task<WaveformResult> SynthesizeAsync(
            SpeechSynthesisRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SynthesizeToSinkAsync(
            SpeechSynthesisRequest request, IAudioSink sink,
            Action<SpokenWord>? onWord = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Dispose() { }
    }

    // A hosted implementation that forgets to declare its capabilities. It must
    // not be able to inherit "offline and free".
    private sealed class ForgetfulHostedSynthesizer : ISpeechSynthesizer
    {
        public VoiceInfo Voice { get; } = VoiceInfo.Cloud("hosted", "Hosted", "en-US");

        public Task<WaveformResult> SynthesizeAsync(
            SpeechSynthesisRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SynthesizeToSinkAsync(
            SpeechSynthesisRequest request, IAudioSink sink,
            Action<SpokenWord>? onWord = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Dispose() { }
    }

    [Fact]
    public void Capabilities_DefaultToTheOnDeviceProfile()
    {
        // Every synthesizer that existed before a second tier was on-device, so the
        // default describes them rather than forcing every implementer to restate it.
        ISpeechSynthesizer legacy = new LegacySynthesizer();

        Assert.Equal(SynthesizerCapabilities.OnDevice, legacy.Capabilities);
    }

    [Fact]
    public void Capabilities_DefaultCannotLetAHostedVoiceClaimToBeOfflineAndFree()
    {
        // The default is derived from the voice's tier, so omitting the override
        // cannot produce a synthesizer that misreports the cost of using it.
        ISpeechSynthesizer hosted = new ForgetfulHostedSynthesizer();

        Assert.False(hosted.Capabilities.Offline);
        Assert.True(hosted.Capabilities.Metered);
        Assert.Equal(SynthesizerCapabilities.Hosted, hosted.Capabilities);
    }
}

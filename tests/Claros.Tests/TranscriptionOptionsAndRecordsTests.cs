namespace Claros.Tests;

public class TranscriptionOptionsAndRecordsTests
{
    [Fact]
    public void EmbeddedTranscriberOptions_HasStreamingDefaults()
    {
        var options = new EmbeddedTranscriberOptions();

        Assert.True(options.StageNativeRuntime);
        Assert.True(options.EmitPartialResults);
        Assert.False(options.MaskProfanity);
        // High timeout keeps the engine in a continuous streaming state.
        Assert.Equal(100_000, options.SegmentationSilenceTimeoutMs);
        Assert.Equal(16_000, options.SampleRate);
    }

    [Fact]
    public void EmbeddedTranscriberOptions_SupportsInitOverrides()
    {
        var options = new EmbeddedTranscriberOptions
        {
            StageNativeRuntime = false,
            EmitPartialResults = false,
            MaskProfanity = true,
            SegmentationSilenceTimeoutMs = 500,
            SampleRate = 8_000,
        };

        Assert.False(options.StageNativeRuntime);
        Assert.False(options.EmitPartialResults);
        Assert.True(options.MaskProfanity);
        Assert.Equal(500, options.SegmentationSilenceTimeoutMs);
        Assert.Equal(8_000, options.SampleRate);
    }

    [Fact]
    public void TranscriptionModelInfo_StoresAllFields()
    {
        var model = new TranscriptionModelInfo(
            Locale: "en-US",
            ModelName: "Microsoft Speech Recognizer en-US FP Model V11",
            PackageFamilyName: "MicrosoftWindows.Speech.en-US_cw5n1h2txyewy",
            PackageFullName: "MicrosoftWindows.Speech.en-US.1_1.0.29.0_x64__cw5n1h2txyewy",
            InstalledPath: @"C:\Program Files\WindowsApps\speech-en-US");

        Assert.Equal("en-US", model.Locale);
        Assert.Equal("Microsoft Speech Recognizer en-US FP Model V11", model.ModelName);
        Assert.Equal("MicrosoftWindows.Speech.en-US_cw5n1h2txyewy", model.PackageFamilyName);
        Assert.Equal(@"C:\Program Files\WindowsApps\speech-en-US", model.InstalledPath);
    }

    [Fact]
    public void TranscriptionModelInfo_RecordEqualityComparesByValue()
    {
        var a = new TranscriptionModelInfo("en-US", "Model", "fam", "full", @"C:\p");
        var b = new TranscriptionModelInfo("en-US", "Model", "fam", "full", @"C:\p");
        var different = a with { Locale = "fr-FR" };

        Assert.Equal(a, b);
        Assert.NotEqual(a, different);
    }
}

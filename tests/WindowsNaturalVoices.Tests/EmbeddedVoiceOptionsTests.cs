namespace WindowsNaturalVoices.Tests;

public class EmbeddedVoiceOptionsTests
{
    [Fact]
    public void Defaults_ForceHdAt24kHzWithStaging()
    {
        var options = new EmbeddedVoiceOptions();

        Assert.Equal(24_000, options.SampleRate);
        Assert.True(options.ForceHd);
        Assert.Equal(0, options.HdThreshold);
        Assert.True(options.PreferSymlink);
        Assert.True(options.StageNativeRuntime);
        Assert.Null(options.OverlayRoot);
    }

    [Fact]
    public void SupportsInitOnlyOverrides()
    {
        var options = new EmbeddedVoiceOptions
        {
            SampleRate = 48_000,
            ForceHd = false,
            HdThreshold = 5,
            PreferSymlink = false,
            StageNativeRuntime = false,
            OverlayRoot = @"C:\overlays",
        };

        Assert.Equal(48_000, options.SampleRate);
        Assert.False(options.ForceHd);
        Assert.Equal(5, options.HdThreshold);
        Assert.False(options.PreferSymlink);
        Assert.False(options.StageNativeRuntime);
        Assert.Equal(@"C:\overlays", options.OverlayRoot);
    }
}

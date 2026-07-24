using Windows.Speech.Internal;

namespace Windows.Speech.Tests;

public class RecognitionModelConfigTests
{
    [Fact]
    public void Parse_ReadsAllKnownKeys()
    {
        var content =
            "name=Microsoft Speech Recognizer en-US FP Model V11\n" +
            "power-level=full\n" +
            "locale=en-US\n" +
            "version=11\n";

        var config = RecognitionModelConfig.Parse(content);

        Assert.NotNull(config);
        Assert.Equal("Microsoft Speech Recognizer en-US FP Model V11", config!.Name);
        Assert.Equal("en-US", config.Locale);
        Assert.Equal("full", config.PowerLevel);
        Assert.Equal("11", config.Version);
    }

    [Fact]
    public void Parse_IgnoresBlankLinesCommentsAndUnknownKeys()
    {
        var content =
            "# a comment\n" +
            "\n" +
            "name=Recognizer\n" +
            "unknown=whatever\n" +
            "locale=fr-FR\n";

        var config = RecognitionModelConfig.Parse(content);

        Assert.NotNull(config);
        Assert.Equal("Recognizer", config!.Name);
        Assert.Equal("fr-FR", config.Locale);
        Assert.Null(config.PowerLevel);
        Assert.Null(config.Version);
    }

    [Fact]
    public void Parse_TrimsWhitespaceAroundKeysAndValues()
    {
        var content = "  name = Trimmed Name  \n  locale =  en-GB \n";

        var config = RecognitionModelConfig.Parse(content);

        Assert.NotNull(config);
        Assert.Equal("Trimmed Name", config!.Name);
        Assert.Equal("en-GB", config.Locale);
    }

    [Fact]
    public void Parse_KeepsEqualsSignsInsideValue()
    {
        var config = RecognitionModelConfig.Parse("name=a=b=c\nlocale=en-US\n");

        Assert.NotNull(config);
        Assert.Equal("a=b=c", config!.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("power-level=full\nversion=11\n")] // missing name and locale
    [InlineData("name=Recognizer\n")]              // missing locale
    [InlineData("locale=en-US\n")]                 // missing name
    public void Parse_ReturnsNullWhenRequiredKeysMissing(string? content)
    {
        Assert.Null(RecognitionModelConfig.Parse(content));
    }
}

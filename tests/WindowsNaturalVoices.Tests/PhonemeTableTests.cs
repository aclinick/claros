using WindowsNaturalVoices.Internal;

namespace WindowsNaturalVoices.Tests;

public class PhonemeTableTests
{
    private const string Sample =
        "<pad>:0\n" +
        "</s>:1\n" +
        "<bos>:4149\n" +
        "en-us_eh1:646\n" +
        "en-us_iy:200\n" +
        "malformed-line-without-colon\n" +
        "trailing-colon:\n" +
        ":123\n" +
        "\n";

    private static PhonemeTable Load(string content)
    {
        using var file = TempFile.WithText(content, ".txt");
        return PhonemeTable.Load(file.Path);
    }

    [Fact]
    public void Load_ParsesValidLinesAndSkipsMalformed()
    {
        var table = Load(Sample);

        // pad, eos, bos, eh1, iy => 5 valid entries
        Assert.Equal(5, table.Count);
        Assert.True(table.TryGet("en-us_eh1", out var id));
        Assert.Equal(646, id);
    }

    [Fact]
    public void Load_ReadsControlTokens()
    {
        var table = Load(Sample);

        Assert.Equal(0, table.Pad);
        Assert.Equal(1, table.Eos);
        Assert.Equal(4149, table.Bos);
    }

    [Fact]
    public void Constructor_UsesDefaultsWhenControlTokensMissing()
    {
        var table = new PhonemeTable(new Dictionary<string, int> { ["en-us_eh1"] = 646 });

        Assert.Equal(0, table.Pad);
        Assert.Equal(0, table.Bos);
        Assert.Equal(1, table.Eos);
    }

    [Fact]
    public void TryGet_ReturnsFalseForUnknownKey()
    {
        var table = Load(Sample);

        Assert.False(table.TryGet("en-us_nope", out var id));
        Assert.Equal(0, id);
    }

    [Theory]
    [InlineData("en-US", "EH1", 646)]
    [InlineData("EN-us", "eh1", 646)]
    [InlineData("en-us", "Iy", 200)]
    public void TryGetArpabet_IsCaseInsensitiveAndLocalePrefixed(string locale, string arpabet, int expected)
    {
        var table = Load(Sample);

        Assert.True(table.TryGetArpabet(locale, arpabet, out var id));
        Assert.Equal(expected, id);
    }

    [Fact]
    public void Load_StripsByteOrderMark()
    {
        var table = Load("\uFEFFen-us_eh1:646\n");

        Assert.True(table.TryGet("en-us_eh1", out var id));
        Assert.Equal(646, id);
    }
}

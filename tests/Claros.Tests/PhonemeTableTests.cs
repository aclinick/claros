using Claros.Internal;

namespace Claros.Tests;

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
    public void Load_ReportsTheFirstIdAndLineForAThirdConflictingOccurrence()
    {
        // Regression: tracking only the most recent value would blame the first
        // line for an id it never held.
        var ex = Assert.Throws<VoicePackageFormatException>(
            () => Load("<pad>:0\n</s>:1\n<bos>:4149\nx:1\nx:2\nx:3\n"));

        // 'x' is 1 on line 4. Both conflicts must cite that, not the running value.
        Assert.Contains("'x' is 1 on line 4 but 2 on line 5", ex.Message);
        Assert.Contains("'x' is 1 on line 4 but 3 on line 6", ex.Message);
    }

    [Fact]
    public void Load_KeepsTheFirstMappingWhenAKeyRepeatsWithTheSameId()
    {
        var table = Load("<pad>:0\n</s>:1\n<bos>:4149\nen-us_eh1:646\nen-us_eh1:646\n");

        Assert.True(table.TryGet("en-us_eh1", out var id));
        Assert.Equal(646, id);
        // pad, eos, bos, eh1 - the repeat must not add a second entry.
        Assert.Equal(4, table.Count);
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
        var table = Load("\uFEFF<pad>:0\n</s>:1\n<bos>:4149\nen-us_eh1:646\n");

        Assert.True(table.TryGet("en-us_eh1", out var id));
        Assert.Equal(646, id);
    }

    [Fact]
    public void Load_AllowsAKeyRepeatedWithTheSameId()
    {
        var table = Load(Sample + "en-us_eh1:646\n");

        Assert.True(table.TryGet("en-us_eh1", out var id));
        Assert.Equal(646, id);
    }

    [Fact]
    public void Load_RejectsConflictingIdsAndReportsLineNumbers()
    {
        // 'en-us_eh1' is 646 on line 4 of Sample; reassign it further down.
        var ex = Assert.Throws<VoicePackageFormatException>(
            () => Load(Sample + "en-us_eh1:999\n"));

        Assert.Contains("en-us_eh1", ex.Message);
        Assert.Contains("646", ex.Message);
        Assert.Contains("999", ex.Message);
        Assert.Contains("line 4", ex.Message);
    }

    [Theory]
    [InlineData("<pad>")]
    [InlineData("<bos>")]
    [InlineData("</s>")]
    public void Load_RejectsAPackageMissingARequiredControlToken(string missing)
    {
        var lines = new[] { "<pad>:0", "</s>:1", "<bos>:4149", "en-us_eh1:646" }
            .Where(l => !l.StartsWith(missing + ":", StringComparison.Ordinal));

        var ex = Assert.Throws<VoicePackageFormatException>(
            () => Load(string.Join("\n", lines) + "\n"));

        Assert.Contains(missing, ex.Message);
    }

    [Fact]
    public void Load_MissingBosWouldOtherwiseCollideWithPad()
    {
        // The reason a missing <bos> is fatal rather than defaulted: the fallback
        // is 0, which is exactly <pad>, so utterances would silently be prefixed
        // with padding instead of a begin-of-sequence marker.
        var lenient = new PhonemeTable(new Dictionary<string, int> { ["<pad>"] = 0 });
        Assert.Equal(lenient.Pad, lenient.Bos);

        Assert.Throws<VoicePackageFormatException>(() => Load("<pad>:0\n</s>:1\nen-us_eh1:646\n"));
    }
}

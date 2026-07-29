using Claros.Internal;

namespace Claros.Tests;

public class TokensXmlParserTests
{
    private const string ValidTokens = """
        <?xml version="1.0" encoding="utf-8"?>
        <SPTokenList>
          <Token name="MSTTS_V110_enUS_ZiraM">
            <String value="Microsoft Zira" />
            <Attribute name="Gender" value="Female" />
            <Attribute name="Age" value="Adult" />
            <Attribute name="Vendor" value="Microsoft" />
            <Attribute name="Version" value="11.0" />
            <Attribute name="Name" value="Fallback Name" />
          </Token>
        </SPTokenList>
        """;

    [Fact]
    public void TryParse_ExtractsAllMetadataFields()
    {
        using var file = TempFile.WithText(ValidTokens, ".xml");

        var meta = TokensXmlParser.TryParse(file.Path);

        Assert.NotNull(meta);
        Assert.Equal("Microsoft Zira", meta!.DisplayName);
        Assert.Equal("Female", meta.Gender);
        Assert.Equal("Adult", meta.Age);
        Assert.Equal("Microsoft", meta.Vendor);
        Assert.Equal("11.0", meta.Version);
    }

    [Fact]
    public void TryParse_FallsBackToNameAttributeWhenNoStringElement()
    {
        const string xml = """
            <SPTokenList>
              <Token>
                <Attribute name="Name" value="Fallback Name" />
                <Attribute name="Gender" value="Male" />
              </Token>
            </SPTokenList>
            """;
        using var file = TempFile.WithText(xml, ".xml");

        var meta = TokensXmlParser.TryParse(file.Path);

        Assert.NotNull(meta);
        Assert.Equal("Fallback Name", meta!.DisplayName);
        Assert.Equal("Male", meta.Gender);
    }

    [Fact]
    public void TryParse_ReturnsNullWhenFileMissing()
    {
        Assert.Null(TokensXmlParser.TryParse(Path.Combine(Path.GetTempPath(), "does_not_exist_" + Guid.NewGuid().ToString("N") + ".xml")));
    }

    [Fact]
    public void TryParse_ReturnsNullForMalformedXml()
    {
        using var file = TempFile.WithText("<Token><not closed", ".xml");

        Assert.Null(TokensXmlParser.TryParse(file.Path));
    }

    [Fact]
    public void TryParse_ReturnsNullWhenNoTokenElement()
    {
        using var file = TempFile.WithText("<SPTokenList></SPTokenList>", ".xml");

        Assert.Null(TokensXmlParser.TryParse(file.Path));
    }

    [Fact]
    public void TryParse_UsesEmptyStringsForMissingAttributes()
    {
        const string xml = """
            <SPTokenList>
              <Token>
                <String value="Only Display" />
              </Token>
            </SPTokenList>
            """;
        using var file = TempFile.WithText(xml, ".xml");

        var meta = TokensXmlParser.TryParse(file.Path);

        Assert.NotNull(meta);
        Assert.Equal("Only Display", meta!.DisplayName);
        Assert.Equal(string.Empty, meta.Gender);
        Assert.Equal(string.Empty, meta.Vendor);
    }
}

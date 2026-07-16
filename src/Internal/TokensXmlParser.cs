using System.Xml.Linq;

namespace WindowsNaturalVoices.Internal;

/// <summary>
/// Parses the <c>Tokens.xml</c> file that every Natural Voice package
/// ships alongside its model binaries. Extracts the voice metadata that
/// backs a public <see cref="VoiceInfo"/> record.
/// </summary>
internal static class TokensXmlParser
{
    public record TokenMetadata(
        string DisplayName,
        string Gender,
        string Age,
        string Vendor,
        string Version);

    public static TokenMetadata? TryParse(string tokensXmlPath)
    {
        if (!File.Exists(tokensXmlPath)) return null;

        XDocument doc;
        try { doc = XDocument.Load(tokensXmlPath); }
        catch { return null; }

        var token = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Token");
        if (token is null) return null;

        string Attr(string name) =>
            token.Elements()
                .Where(e => e.Name.LocalName == "Attribute" &&
                            (string?)e.Attribute("name") == name)
                .Select(e => (string?)e.Attribute("value") ?? string.Empty)
                .FirstOrDefault() ?? string.Empty;

        var display = token.Elements()
            .Where(e => e.Name.LocalName == "String" && string.IsNullOrEmpty((string?)e.Attribute("name")))
            .Select(e => (string?)e.Attribute("value") ?? string.Empty)
            .FirstOrDefault() ?? Attr("Name");

        return new TokenMetadata(
            DisplayName: display,
            Gender: Attr("Gender"),
            Age: Attr("Age"),
            Vendor: Attr("Vendor"),
            Version: Attr("Version"));
    }
}

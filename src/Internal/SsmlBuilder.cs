using System.Text;
using System.Xml;

namespace Windows.Speech.Internal;

/// <summary>
/// Builds a minimal, valid SSML document that wraps plain text in a bound voice
/// and optional <see cref="SpeechProsody"/>. Used by the embedded synthesizer to
/// render prosody-shaped text (the on-device runtime applies rate/pitch/volume
/// only through SSML). An <see cref="XmlWriter"/> does the escaping so arbitrary
/// text, voice names, and locales cannot break out of the markup.
/// </summary>
internal static class SsmlBuilder
{
    private const string SynthesisNamespace = "http://www.w3.org/2001/10/synthesis";

    /// <summary>
    /// Wraps <paramref name="text"/> spoken by <paramref name="voiceName"/> in
    /// <paramref name="locale"/> (for example <c>en-US</c>), applying
    /// <paramref name="prosody"/> when it sets any dimension. The result is a
    /// complete <c>&lt;speak&gt;</c> document.
    /// </summary>
    public static string BuildTextSsml(
        string text, SpeechProsody? prosody, string voiceName, string locale)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(voiceName);
        ArgumentException.ThrowIfNullOrEmpty(locale);

        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = false,
            ConformanceLevel = ConformanceLevel.Document,
        };

        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, settings))
        {
            writer.WriteStartElement("speak", SynthesisNamespace);
            writer.WriteAttributeString("version", "1.0");
            writer.WriteAttributeString("xml", "lang", XmlReservedNamespace, locale);

            writer.WriteStartElement("voice", SynthesisNamespace);
            writer.WriteAttributeString("name", voiceName);

            var hasProsody = prosody is { IsEmpty: false };
            if (hasProsody)
            {
                writer.WriteStartElement("prosody", SynthesisNamespace);
                if (prosody!.Rate is not null) writer.WriteAttributeString("rate", prosody.Rate);
                if (prosody.Pitch is not null) writer.WriteAttributeString("pitch", prosody.Pitch);
                if (prosody.Volume is not null) writer.WriteAttributeString("volume", prosody.Volume);
            }

            writer.WriteString(text);

            if (hasProsody) writer.WriteEndElement(); // prosody
            writer.WriteEndElement(); // voice
            writer.WriteEndElement(); // speak
        }

        return sb.ToString();
    }

    private const string XmlReservedNamespace = "http://www.w3.org/XML/1998/namespace";
}

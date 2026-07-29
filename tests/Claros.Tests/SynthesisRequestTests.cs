using System.Xml;
using System.Xml.Linq;
using Claros;
using Claros.Internal;

namespace Claros.Tests;

public class SpeechSynthesisRequestTests
{
    [Fact]
    public void ForText_IsPlainTextRequest()
    {
        var request = SpeechSynthesisRequest.ForText("hello");

        Assert.Equal("hello", request.Content);
        Assert.False(request.IsSsml);
        Assert.False(request.RequiresSsml);
    }

    [Fact]
    public void ForText_WithProsody_RequiresSsml()
    {
        var request = SpeechSynthesisRequest.ForText("hi", new SpeechProsody { Rate = "slow" });

        Assert.True(request.RequiresSsml);
        Assert.False(request.IsSsml);
    }

    [Fact]
    public void ForText_WithEmptyProsody_DoesNotRequireSsml()
    {
        var request = SpeechSynthesisRequest.ForText("hi", new SpeechProsody());

        Assert.False(request.RequiresSsml);
    }

    [Fact]
    public void ForSsml_IsSsmlRequest()
    {
        var request = SpeechSynthesisRequest.ForSsml("<speak>hi</speak>");

        Assert.True(request.IsSsml);
        Assert.True(request.RequiresSsml);
    }

    [Fact]
    public void ImplicitFromString_IsPlainText()
    {
        SpeechSynthesisRequest request = "howdy";

        Assert.Equal("howdy", request.Content);
        Assert.False(request.IsSsml);
    }

    [Fact]
    public void ForText_RejectsEmpty()
    {
        Assert.Throws<ArgumentException>(() => SpeechSynthesisRequest.ForText(""));
    }

    [Fact]
    public void ImplicitFromString_RejectsEmpty()
    {
        // Deliberate: passing a bare "" to SynthesizeAsync is a caller bug, and the
        // conversion is how that reaches every speaker uniformly. This is the one
        // behaviour that changed when the redundant SpeakAsync(string) overload,
        // which forwarded empty text to the runtime, was removed.
        Assert.Throws<ArgumentException>(() =>
        {
            SpeechSynthesisRequest _ = "";
        });
    }

    [Fact]
    public void Validate_RejectsSsmlCombinedWithProsody()
    {
        var request = new SpeechSynthesisRequest
        {
            Content = "<speak>hi</speak>",
            IsSsml = true,
            Prosody = new SpeechProsody { Rate = "fast" },
        };

        Assert.Throws<ArgumentException>(request.Validate);
    }

    [Fact]
    public void Validate_AllowsSsmlWithEmptyProsody()
    {
        var request = new SpeechSynthesisRequest
        {
            Content = "<speak>hi</speak>",
            IsSsml = true,
            Prosody = new SpeechProsody(),
        };

        request.Validate(); // does not throw
    }
}

public class SsmlBuilderTests
{
    [Fact]
    public void Build_ProducesValidSpeakDocumentWithVoiceAndLang()
    {
        var ssml = SsmlBuilder.BuildTextSsml("hello", null, "Ava", "en-US");

        var doc = XDocument.Parse(ssml); // throws if malformed
        XNamespace ns = "http://www.w3.org/2001/10/synthesis";
        var speak = doc.Root!;
        Assert.Equal(ns + "speak", speak.Name);
        Assert.Equal("en-US", speak.Attribute(XNamespace.Xml + "lang")!.Value);

        var voice = speak.Element(ns + "voice")!;
        Assert.Equal("Ava", voice.Attribute("name")!.Value);
        Assert.Equal("hello", voice.Value);
    }

    [Fact]
    public void Build_EmitsOnlyTheProsodyAttributesThatAreSet()
    {
        var prosody = new SpeechProsody { Rate = "slow", Volume = "+6dB" };
        var ssml = SsmlBuilder.BuildTextSsml("hi", prosody, "Ava", "en-US");

        XNamespace ns = "http://www.w3.org/2001/10/synthesis";
        var prosodyEl = XDocument.Parse(ssml).Descendants(ns + "prosody").Single();

        Assert.Equal("slow", prosodyEl.Attribute("rate")!.Value);
        Assert.Equal("+6dB", prosodyEl.Attribute("volume")!.Value);
        Assert.Null(prosodyEl.Attribute("pitch"));
        Assert.Equal("hi", prosodyEl.Value);
    }

    [Fact]
    public void Build_OmitsProsodyElementWhenEmpty()
    {
        var ssml = SsmlBuilder.BuildTextSsml("hi", new SpeechProsody(), "Ava", "en-US");

        XNamespace ns = "http://www.w3.org/2001/10/synthesis";
        Assert.Empty(XDocument.Parse(ssml).Descendants(ns + "prosody"));
    }

    [Fact]
    public void Build_EscapesTextSoMarkupCannotBreakOut()
    {
        var ssml = SsmlBuilder.BuildTextSsml("a < b & \"c\" </speak>", null, "Ava", "en-US");

        // Must still be well-formed and round-trip the literal text.
        var doc = XDocument.Parse(ssml);
        XNamespace ns = "http://www.w3.org/2001/10/synthesis";
        Assert.Equal("a < b & \"c\" </speak>", doc.Descendants(ns + "voice").Single().Value);
    }

    [Fact]
    public void Build_EscapesVoiceNameAttribute()
    {
        var ssml = SsmlBuilder.BuildTextSsml("hi", null, "A\"&<Voice", "en-US");

        var doc = XDocument.Parse(ssml); // malformed if the attribute wasn't escaped
        XNamespace ns = "http://www.w3.org/2001/10/synthesis";
        Assert.Equal("A\"&<Voice", doc.Descendants(ns + "voice").Single().Attribute("name")!.Value);
    }

    [Fact]
    public void Build_RejectsEmptyVoiceOrLocale()
    {
        Assert.Throws<ArgumentException>(() => SsmlBuilder.BuildTextSsml("hi", null, "", "en-US"));
        Assert.Throws<ArgumentException>(() => SsmlBuilder.BuildTextSsml("hi", null, "Ava", ""));
    }
}

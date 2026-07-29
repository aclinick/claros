using Claros;
using Claros.Internal;

namespace Claros.Tests;

/// <summary>
/// The reconstructed NaturalVoiceSynthesizer pipeline implements
/// ISpeechSynthesizer but cannot honor every part of that contract. These pin
/// the refusals, because the alternative — accepting the request and silently
/// dropping what cannot be done — produces audio that does not match what was
/// asked for, with nothing to tell the caller.
/// </summary>
public class SynthesisSupportGuardTests
{
    [Fact]
    public void PlainTextRequest_IsAllowedThrough()
    {
        var request = SpeechSynthesisRequest.ForText("hello");

        SynthesisSupportGuard.RequirePlainText(request, "Ava");
    }

    [Fact]
    public void EmptyProsody_IsNotTreatedAsMarkup()
    {
        // An all-default SpeechProsody asks for nothing, so there is nothing to
        // drop and no reason to refuse. Refusing here would break callers who
        // pass a prosody object around without setting anything on it.
        var request = SpeechSynthesisRequest.ForText("hello", new SpeechProsody());

        Assert.True(request.Prosody!.IsEmpty);
        SynthesisSupportGuard.RequirePlainText(request, "Ava");
    }

    [Fact]
    public void RawSsml_IsRefusedAndNamesTheEngineThatSupportsIt()
    {
        var request = SpeechSynthesisRequest.ForSsml("<speak>hi</speak>");

        var ex = Assert.Throws<NotSupportedException>(
            () => SynthesisSupportGuard.RequirePlainText(request, "Ava"));

        Assert.Contains("Ava", ex.Message);
        Assert.Contains(nameof(SynthesizerCapabilities.RawSsml), ex.Message);
        Assert.Contains(nameof(EmbeddedSpeechSynthesizer), ex.Message);
    }

    [Fact]
    public void ProsodyShapedText_IsRefusedRatherThanSpokenFlat()
    {
        var request = SpeechSynthesisRequest.ForText(
            "hello", new SpeechProsody { Rate = "slow" });

        var ex = Assert.Throws<NotSupportedException>(
            () => SynthesisSupportGuard.RequirePlainText(request, "Ava"));

        // Named separately from RawSsml: an engine can support one without the
        // other, so the message must point at the flag that actually refused.
        Assert.Contains(nameof(SynthesizerCapabilities.Prosody), ex.Message);
        Assert.DoesNotContain(nameof(SynthesizerCapabilities.RawSsml), ex.Message);
    }

    [Fact]
    public void NoWordCallback_IsAllowedThrough()
    {
        SynthesisSupportGuard.RequireNoWordCallback(null, "Ava", "onWord");
    }

    [Fact]
    public void WordCallback_IsRefusedRatherThanNeverRaised()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => SynthesisSupportGuard.RequireNoWordCallback(_ => { }, "Ava", "onWord"));

        Assert.Contains("onWord", ex.Message);
        Assert.Contains(nameof(SynthesizerCapabilities.WordBoundaries), ex.Message);
    }
}

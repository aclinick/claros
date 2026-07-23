namespace WindowsNaturalVoices;

/// <summary>
/// What to synthesize: either plain text (optionally shaped by
/// <see cref="Prosody"/>) or a complete SSML document. This is the single input
/// to <see cref="ISpeechSynthesizer.SynthesizeAsync"/>, replacing a bare
/// <c>string text</c> so the request can grow (prosody now, more later) without
/// changing the method surface.
/// </summary>
/// <remarks>
/// A plain <see cref="string"/> converts implicitly to a text request, so simple
/// calls stay terse (<c>SynthesizeAsync("hello")</c>). Use <see cref="ForText"/>
/// to attach prosody and <see cref="ForSsml"/> to pass markup you built yourself.
/// Prosody and raw SSML are mutually exclusive.
/// </remarks>
public sealed record SpeechSynthesisRequest
{
    /// <summary>The text to speak, or the SSML document when <see cref="IsSsml"/> is set.</summary>
    public required string Content { get; init; }

    /// <summary>Whether <see cref="Content"/> is a complete SSML document rather than plain text.</summary>
    public bool IsSsml { get; init; }

    /// <summary>Optional prosody for a text request; ignored (and disallowed) for SSML.</summary>
    public SpeechProsody? Prosody { get; init; }

    /// <summary>Whether this request must be rendered through SSML (raw SSML or prosody-shaped text).</summary>
    public bool RequiresSsml => IsSsml || (Prosody is { IsEmpty: false });

    /// <summary>Creates a text request, optionally shaped by <paramref name="prosody"/>.</summary>
    public static SpeechSynthesisRequest ForText(string text, SpeechProsody? prosody = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        return new SpeechSynthesisRequest { Content = text, IsSsml = false, Prosody = prosody };
    }

    /// <summary>Creates a request from a complete SSML document.</summary>
    public static SpeechSynthesisRequest ForSsml(string ssml)
    {
        ArgumentException.ThrowIfNullOrEmpty(ssml);
        return new SpeechSynthesisRequest { Content = ssml, IsSsml = true };
    }

    /// <summary>Treats a bare string as a plain-text request.</summary>
    public static implicit operator SpeechSynthesisRequest(string text) => ForText(text);

    /// <summary>
    /// Validates the request's internal consistency, throwing when raw SSML is
    /// combined with prosody or the content is empty.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrEmpty(Content))
        {
            throw new ArgumentException("The synthesis request has no content.", nameof(Content));
        }
        if (IsSsml && Prosody is { IsEmpty: false })
        {
            throw new ArgumentException(
                "Prosody cannot be applied to a request that already carries raw SSML; " +
                "set prosody inside your SSML instead.", nameof(Prosody));
        }
    }
}

namespace WindowsNaturalVoices;

/// <summary>
/// Optional prosody adjustments applied to a plain-text
/// <see cref="SpeechSynthesisRequest"/>: how fast, how high, and how loud the
/// text is spoken. Each value is an SSML <c>prosody</c> attribute value, so it
/// accepts the forms that markup allows — a named level (<c>slow</c>, <c>high</c>,
/// <c>loud</c>), a relative change (<c>+10%</c>, <c>-2st</c>, <c>+6dB</c>), or an
/// absolute value — and is emitted verbatim (XML-escaped) into the generated SSML.
/// </summary>
/// <remarks>
/// Prosody applies only to text requests; when a request already carries raw SSML
/// the caller controls prosody within that markup, so combining the two is
/// rejected. A <c>null</c> field leaves that dimension at the voice's default.
/// </remarks>
public sealed record SpeechProsody
{
    /// <summary>Speaking rate, e.g. <c>x-slow</c>, <c>slow</c>, <c>+10%</c>, <c>0.9</c>.</summary>
    public string? Rate { get; init; }

    /// <summary>Pitch, e.g. <c>high</c>, <c>+2st</c>, <c>-5%</c>.</summary>
    public string? Pitch { get; init; }

    /// <summary>Volume, e.g. <c>loud</c>, <c>+6dB</c>, <c>80</c>.</summary>
    public string? Volume { get; init; }

    /// <summary>Whether any dimension is set (otherwise this has no effect).</summary>
    public bool IsEmpty => Rate is null && Pitch is null && Volume is null;
}

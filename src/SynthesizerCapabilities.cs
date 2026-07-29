namespace Claros;

/// <summary>
/// What an <see cref="ISpeechSynthesizer"/> can and cannot do, so callers can
/// negotiate rather than assume. This exists because the library deliberately
/// supports more than one synthesis tier (see <see cref="VoiceSource"/>), and the
/// tiers differ in ways that matter to real consumers.
/// </summary>
/// <remarks>
/// The point is to fail loudly and early rather than degrade quietly. A caller
/// that needs a guarantee should check for it up front and refuse, instead of
/// discovering the gap halfway through a render — which on a metered tier means
/// having already paid for the work.
/// </remarks>
public sealed record SynthesizerCapabilities
{
    /// <summary>
    /// Whether <see cref="ISpeechSynthesizer.SynthesizeToSinkAsync"/> raises the
    /// <c>onWord</c> callback. Caption highlighting needs this; timeline-aligned
    /// narration does not, because it places audio by cue timestamps.
    /// </summary>
    public required bool WordBoundaries { get; init; }

    /// <summary>
    /// Whether the engine accepts a complete SSML document
    /// (<see cref="SpeechSynthesisRequest.ForSsml"/>).
    /// </summary>
    /// <remarks>
    /// <c>false</c> means such a request is refused, not spoken as literal text
    /// or stripped to its content: silently dropping markup yields audio that
    /// does not match what was asked for, with nothing to tell the caller. A
    /// consumer that emits requests it does not author —
    /// <see cref="SpeechConversation"/> hands a turn handler's reply straight to
    /// the synthesizer — should check this before choosing to emit markup.
    /// </remarks>
    public required bool RawSsml { get; init; }

    /// <summary>
    /// Whether the engine honors <see cref="SpeechProsody"/> shaping on plain
    /// text. Tracked separately from <see cref="RawSsml"/> because the two are
    /// only coupled by implementation, not by contract: the shipped
    /// <see cref="EmbeddedSpeechSynthesizer"/> applies prosody by generating
    /// SSML, but an engine with native rate and pitch controls could honor
    /// prosody while refusing arbitrary markup.
    /// </summary>
    public required bool Prosody { get; init; }

    /// <summary>
    /// Whether synthesis completes with no network access. <c>false</c> means the
    /// caller has explicitly opted into a hosted tier and inherits its latency,
    /// availability, and privacy characteristics.
    /// </summary>
    public required bool Offline { get; init; }

    /// <summary>
    /// Whether requests are billed. Cancelling a metered request mid-flight does
    /// not necessarily avoid the charge, so retry loops deserve more care than
    /// they do against a local engine.
    /// </summary>
    public required bool Metered { get; init; }

    /// <summary>
    /// The profile of an engine running entirely on this machine from installed
    /// models: word boundaries, SSML and prosody, no network, and no cost. This is what every
    /// synthesizer in the library was before a second tier existed, and it is the
    /// default an implementation inherits if it does not declare otherwise.
    /// </summary>
    public static SynthesizerCapabilities OnDevice { get; } = new()
    {
        WordBoundaries = true,
        RawSsml = true,
        Prosody = true,
        Offline = true,
        Metered = false,
    };

    /// <summary>
    /// The profile of a hosted engine the caller explicitly opted into: still
    /// word-boundary, SSML, and prosody capable, but networked and billed.
    /// </summary>
    public static SynthesizerCapabilities Hosted { get; } = new()
    {
        WordBoundaries = true,
        RawSsml = true,
        Prosody = true,
        Offline = false,
        Metered = true,
    };
}

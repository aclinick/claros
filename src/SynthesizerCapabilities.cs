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
    /// models: word boundaries, no network, and no cost. This is what every
    /// synthesizer in the library was before a second tier existed, and it is the
    /// default an implementation inherits if it does not declare otherwise.
    /// </summary>
    public static SynthesizerCapabilities OnDevice { get; } = new()
    {
        WordBoundaries = true,
        Offline = true,
        Metered = false,
    };

    /// <summary>
    /// The profile of a hosted engine the caller explicitly opted into: still
    /// word-boundary capable, but networked and billed.
    /// </summary>
    public static SynthesizerCapabilities Hosted { get; } = new()
    {
        WordBoundaries = true,
        Offline = false,
        Metered = true,
    };
}

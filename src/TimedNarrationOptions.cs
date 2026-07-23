namespace WindowsNaturalVoices;

/// <summary>
/// Tuning for <see cref="TimedNarrator"/>.
/// </summary>
public sealed record TimedNarrationOptions
{
    /// <summary>
    /// Whether to merge consecutive cues into whole sentences before narrating
    /// (see <see cref="CueSentenceGrouper"/>). On by default so the talkover
    /// flows as sentences rather than clipped fragments. Turn off when cues are
    /// already whole utterances (for example live ticker events).
    /// </summary>
    public bool GroupIntoSentences { get; init; } = true;

    /// <summary>The silent gap that forces a sentence break when grouping.</summary>
    public TimeSpan MaxGap { get; init; } = CueSentenceGrouper.DefaultMaxGap;

    /// <summary>
    /// How far ahead of a cue's start the live scheduler begins speaking it, to
    /// hide synthesis latency so the voice lands on time. Only used by
    /// <see cref="TimedNarrator.NarrateAsync"/>.
    /// </summary>
    public TimeSpan Lead { get; init; } = TimeSpan.FromMilliseconds(350);

    /// <summary>
    /// How far past a cue's end the live scheduler still speaks it. Beyond this
    /// the cue is considered passed (for example after a forward seek) and is
    /// skipped rather than spoken late. Only used by
    /// <see cref="TimedNarrator.NarrateAsync"/>.
    /// </summary>
    public TimeSpan StaleGrace { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether <see cref="TimedNarrator.RenderAsync"/> applies a short linear fade
    /// to each clip's edges so overlapping clips don't click. On by default.
    /// </summary>
    public bool FadeEdges { get; init; } = true;

    /// <summary>The default options.</summary>
    public static TimedNarrationOptions Default { get; } = new();
}

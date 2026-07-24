namespace Windows.Speech;

/// <summary>
/// A single timed text cue: some text anchored to a start/end time on a timeline.
/// It is the common currency of <see cref="TimedNarrator"/> — a subtitle line, a
/// caption, or a live event (for example a stock-ticker update) to be spoken when
/// a clock reaches <see cref="Start"/>.
/// </summary>
/// <param name="Index">
/// A 1-based ordinal identifying the cue within its source (subtitle number).
/// Zero when the cue has no natural ordinal (for example a live event).
/// </param>
/// <param name="Start">When the cue begins on its timeline (from time zero).</param>
/// <param name="End">
/// When the cue ends. May be <see cref="Start"/> (a zero-length instant) for a
/// point-in-time event that has no intrinsic duration.
/// </param>
/// <param name="Text">The cue's text, already stripped of markup.</param>
public sealed record TimedCue(int Index, TimeSpan Start, TimeSpan End, string Text)
{
    /// <summary>A cue for a point-in-time event with no intrinsic duration.</summary>
    public static TimedCue At(TimeSpan when, string text, int index = 0) =>
        new(index, when, when, text);
}

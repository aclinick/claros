namespace WindowsNaturalVoices.Internal;

/// <summary>
/// Pure scheduling core for live, playhead-synced narration. Given a monotonic
/// playhead and a set of cues ordered by start time, decides which cue to speak
/// next when the narrator is idle: it speaks a cue once the playhead reaches its
/// start (minus a lead), and skips cues the playhead has already passed (for
/// example after a forward seek) so narration never runs behind. It carries no
/// timing, audio, or synthesis dependency so it is unit-testable.
/// </summary>
internal sealed class NarrationScheduler
{
    private readonly IReadOnlyList<TimedCue> _cues; // ordered by Start
    private int _cursor;

    public NarrationScheduler(IReadOnlyList<TimedCue> cues)
    {
        ArgumentNullException.ThrowIfNull(cues);
        _cues = cues;
    }

    /// <summary>Whether every cue has been taken or skipped.</summary>
    public bool IsExhausted => _cursor >= _cues.Count;

    /// <summary>
    /// Returns the next cue to speak at <paramref name="playhead"/>, or
    /// <see langword="null"/> when the frontier cue is not yet due (or all cues are
    /// consumed). A cue is due when <c>playhead &gt;= Start - lead</c>; if the
    /// playhead is already more than <paramref name="staleGrace"/> past a due cue's
    /// end it is skipped and the next cue is considered. Advances the cursor for
    /// each cue taken or skipped; call only when idle (one utterance at a time).
    /// </summary>
    public TimedCue? TakeDue(TimeSpan playhead, TimeSpan lead, TimeSpan staleGrace)
    {
        while (_cursor < _cues.Count)
        {
            var cue = _cues[_cursor];
            if (playhead < cue.Start - lead)
            {
                return null; // frontier cue not due yet; nothing earlier remains
            }

            _cursor++; // consume this cue whether we speak or skip it
            if (playhead <= cue.End + staleGrace)
            {
                return cue; // due and not stale — speak it
            }
            // Playhead has moved past this cue (seek/overrun): skip and look ahead.
        }

        return null;
    }
}

namespace WindowsNaturalVoices.Internal;

/// <summary>
/// Turns the ever-growing streaming hypothesis of a live recognition session
/// into clean, whole-sentence chat lines. On each call it returns only the
/// sentences whose boundary has been <em>confirmed</em>, and it withholds the
/// trailing sentence until a later hypothesis begins a new sentence after it
/// (or until a final flush). This yields a Whisper-style one-bubble-per-utterance
/// experience without relying on the engine's native end-of-utterance finals.
/// </summary>
/// <remarks>
/// The streaming recognizer emits transient terminating punctuation mid-utterance
/// (for example "You have six." moments before it revises the same fragment into
/// "You have $610,000 in stocks."). Emitting a sentence the instant a period
/// appears therefore truncates content, because the fragment is still being
/// revised in place. To avoid that, the trailing sentence is <b>always</b> held
/// back (even when it already ends with a terminator): only once the recognizer
/// has moved on and started a genuinely new sentence do we treat the previous
/// one as stable and surface it. A flush releases whatever remains at end of audio.
///
/// Known limitation: this tracks how many sentences have been surfaced, not their
/// exact text. Streaming recognizers revise only the trailing region in practice,
/// so a confirmed (non-trailing) sentence is effectively stable. If a later
/// hypothesis were to re-segment already-surfaced history (merging or splitting
/// sentences before the trailing one), a sentence could be dropped or duplicated.
/// Fully guarding against that would require text-prefix reconciliation with
/// explicit correction semantics, which the finals-only chat contract does not
/// support; it is accepted as an intentional tradeoff.
/// </remarks>
internal sealed class SentenceCommitter
{
    private int _emitted;

    /// <summary>
    /// Given the current full hypothesis <paramref name="text"/>, returns the
    /// sentences whose boundary is now confirmed (i.e. a later sentence has
    /// started after them). When <paramref name="flush"/> is <c>true</c>, the
    /// still-in-progress trailing sentence is also returned, for use at end of
    /// audio.
    /// </summary>
    public IReadOnlyList<string> Take(string? text, bool flush = false)
    {
        var sentences = TranscriptSegmenter.Split(text).Segments;

        // Without a flush, always hold back the trailing sentence. It is the one
        // still being revised, and streaming recognizers routinely emit a
        // transient terminator inside it that a later hypothesis rewrites. The
        // boundary is only trustworthy once a subsequent sentence has begun.
        var usable = sentences.Count;
        if (!flush && usable > 0)
        {
            usable--;
        }

        if (usable <= _emitted) return Array.Empty<string>();

        var result = new List<string>(usable - _emitted);
        for (var i = _emitted; i < usable; i++)
        {
            result.Add(sentences[i].Text);
        }
        _emitted = usable;
        return result;
    }
}

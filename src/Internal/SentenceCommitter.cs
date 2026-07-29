namespace Claros.Internal;

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
/// Reconciliation: rather than counting how many sentences have been surfaced,
/// this remembers the <em>exact text</em> of the stable sentences it last saw and
/// diffs the new stable segmentation against it by longest common prefix. Anything
/// past that common prefix is surfaced. This makes the committer robust to the
/// real failure mode of streaming recognizers: a transient terminator can briefly
/// split the trailing region into an extra short sentence (advancing the position),
/// after which the recognizer revises that region into the correct, longer
/// sentence. A count-based tracker would treat the corrected sentence as "already
/// past" and silently drop it (this is how the 14-second "$610,000 ... resets."
/// sentence was lost). Prefix reconciliation instead detects that the text at that
/// position changed and re-surfaces the corrected sentence, while identical
/// re-observations produce no output (no duplicates). Streaming recognizers revise
/// only the trailing region in practice, so the already-emitted prefix stays
/// stable; a mid-history insertion (not observed in practice) is the one case this
/// finals-only contract still cannot retract.
/// </remarks>
internal sealed class SentenceCommitter
{
    private readonly List<string> _emitted = new();

    /// <summary>
    /// Given the current full hypothesis <paramref name="text"/>, returns the
    /// sentences whose boundary is now confirmed (i.e. a later sentence has
    /// started after them, or the text at a previously-seen position was revised).
    /// When <paramref name="flush"/> is <c>true</c>, the still-in-progress trailing
    /// sentence is also returned, for use at end of audio.
    /// </summary>
    public IReadOnlyList<string> Take(string? text, bool flush = false)
    {
        var sentences = TranscriptSegmenter.Split(text).Segments;

        // Without a flush, always hold back the trailing sentence. It is the one
        // still being revised, and streaming recognizers routinely emit a
        // transient terminator inside it that a later hypothesis rewrites. The
        // boundary is only trustworthy once a subsequent sentence has begun.
        var stableCount = sentences.Count;
        if (!flush && stableCount > 0)
        {
            stableCount--;
        }

        // Diff the new stable segmentation against what we last surfaced by longest
        // common prefix. Everything beyond the shared prefix is new content or an
        // in-place revision of a position we thought was done; either way it must
        // be surfaced. Identical re-observations share the whole prefix and emit
        // nothing.
        var shared = 0;
        while (shared < _emitted.Count &&
               shared < stableCount &&
               string.Equals(_emitted[shared], sentences[shared].Text, StringComparison.Ordinal))
        {
            shared++;
        }

        var result = new List<string>(stableCount - shared);
        for (var i = shared; i < stableCount; i++)
        {
            result.Add(sentences[i].Text);
        }

        // Remember the exact stable segmentation for the next diff.
        _emitted.RemoveRange(shared, _emitted.Count - shared);
        for (var i = shared; i < stableCount; i++)
        {
            _emitted.Add(sentences[i].Text);
        }

        return result.Count == 0 ? Array.Empty<string>() : result;
    }
}

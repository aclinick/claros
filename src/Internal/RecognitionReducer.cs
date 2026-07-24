namespace Windows.Speech.Internal;

/// <summary>
/// Turns the ever-growing streaming hypothesis of a live recognition session into
/// an ordered stream of <see cref="RecognitionEvent"/>s. It is the event-model
/// counterpart of <see cref="SentenceCommitter"/>: it applies the same
/// hold-back-the-trailing-sentence and longest-common-prefix reconciliation, but
/// classifies each surfaced sentence as a brand-new <see cref="RecognitionEventKind.Final"/>
/// or a <see cref="RecognitionEventKind.Correction"/> of a position it had already
/// finalized, and additionally emits the still-in-progress trailing sentence as a
/// <see cref="RecognitionEventKind.Partial"/>.
/// </summary>
/// <remarks>
/// See <see cref="SentenceCommitter"/> for why the trailing sentence is always
/// withheld until a later sentence begins (transient mid-utterance terminators)
/// and why reconciliation diffs the exact stable text rather than counting
/// sentences (so an in-place revision at an already-surfaced position re-emits the
/// corrected sentence instead of silently dropping it). The only added behavior
/// here is the Final/Correction distinction — a re-emission at a position that was
/// previously surfaced becomes a <see cref="RecognitionEventKind.Correction"/> —
/// and the trailing <see cref="RecognitionEventKind.Partial"/>. Ordering within one
/// observation is: corrections and finals in ascending sentence index, then the
/// partial (if any). This type is not thread safe; serialize calls.
///
/// <para><b>Finals-only limitation.</b> Because this contract only ever adds or
/// revises a sentence in place (never retracts one), it cannot cleanly represent a
/// confirmed sentence <em>boundary being removed</em> — for example two already-final
/// sentences later merging into one. In that case the merged text surfaces as a
/// <see cref="RecognitionEventKind.Correction"/> at the earlier index while the
/// now-absorbed later index is left as a stale final until (and unless) a future
/// hypothesis reuses that position and corrects it. This mirrors the accepted
/// limitation of <see cref="SentenceCommitter"/>; in practice the streaming
/// recognizer only flickers the terminator of the still-trailing (withheld)
/// sentence, so confirmed boundaries do not merge.</para>
/// </remarks>
internal sealed class RecognitionReducer
{
    private readonly List<string> _emitted = new();
    private string _lastPartial = string.Empty;

    /// <summary>
    /// Folds a new full hypothesis <paramref name="text"/> into recognition events.
    /// When <paramref name="flush"/> is <c>true</c> (end of audio) the trailing
    /// sentence is finalized too and no partial is emitted.
    /// </summary>
    public IReadOnlyList<RecognitionEvent> Observe(string? text, bool flush = false)
    {
        var sentences = TranscriptSegmenter.Split(text).Segments;

        // Hold back the trailing sentence unless flushing: it is still being
        // revised and its boundary is only trustworthy once a later sentence has
        // begun (see SentenceCommitter).
        var stableCount = sentences.Count;
        if (!flush && stableCount > 0) stableCount--;

        // Longest common prefix with what we last finalized. Everything past the
        // shared prefix is new or an in-place revision that must be surfaced.
        var shared = 0;
        while (shared < _emitted.Count &&
               shared < stableCount &&
               string.Equals(_emitted[shared], sentences[shared].Text, StringComparison.Ordinal))
        {
            shared++;
        }

        var events = new List<RecognitionEvent>();

        // Surface everything past the shared prefix. A position that was already
        // finalized but now differs is a Correction; a position beyond what we ever
        // finalized is a new Final. Crucially, _emitted is NEVER truncated when
        // stableCount shrinks: streaming recognizers routinely flicker a
        // terminating period (it appears, is revised away as the next word attaches,
        // then reappears), which momentarily drops the stable sentence count. If we
        // forgot the finalized sentence on that dip, it would re-finalize — emitting
        // a duplicate Final — when the period returned. Keeping _emitted monotonic
        // means the returning sentence matches the shared prefix and stays silent.
        for (var i = shared; i < stableCount; i++)
        {
            var sentence = sentences[i].Text;
            if (i < _emitted.Count)
            {
                // The prefix walk stops at the first divergence, so indices after it
                // may still be unchanged; only surface a genuine revision.
                if (string.Equals(_emitted[i], sentence, StringComparison.Ordinal)) continue;
                _emitted[i] = sentence;
                events.Add(RecognitionEvent.Correction(sentence, i));
            }
            else
            {
                _emitted.Add(sentence); // i == _emitted.Count here
                events.Add(RecognitionEvent.Final(sentence, i));
            }
        }

        // The trailing, not-yet-finalized sentence is the live partial. Suppress a
        // verbatim repeat (unchanged trailing hypothesis across polls), but always
        // emit when a sentence just finalized this turn: the trailing text now
        // belongs to a NEW sentence and must surface even if it happens to match the
        // previous partial's text.
        if (!flush && sentences.Count > stableCount)
        {
            var advanced = events.Count > 0;
            var partial = sentences[stableCount].Text;
            if (partial.Length > 0 &&
                (advanced || !string.Equals(partial, _lastPartial, StringComparison.Ordinal)))
            {
                _lastPartial = partial;
                events.Add(RecognitionEvent.Partial(partial));
            }
        }
        else if (flush)
        {
            _lastPartial = string.Empty;
        }

        return events.Count == 0 ? Array.Empty<RecognitionEvent>() : events;
    }
}

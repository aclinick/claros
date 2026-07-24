namespace Windows.Speech.Internal;

/// <summary>
/// Pure-logic core of a single call leg's transcript: converts newly completed
/// sentence segments into speaker-labeled <see cref="TranscriptChunk"/>s,
/// accumulates them in order, and supports clearing. This is the testable heart
/// of <see cref="CallLegTranscriber"/>, kept free of any native recognizer so it
/// can be unit tested with synthetic segments.
///
/// It mirrors the reference Mac worker's per-source accumulation and "finals
/// only" emission (<c>AudioService.emitTranscript</c> / <c>getTranscript</c> /
/// <c>clearTranscript</c>): blank sentences are skipped, and each surviving
/// sentence becomes exactly one chunk attributed to this leg.
/// </summary>
internal sealed class CallLegTranscript
{
    private readonly string _sourceId;
    private readonly string _sourceLabel;
    private readonly List<TranscriptChunk> _chunks = new();

    public CallLegTranscript(string sourceId, string sourceLabel)
    {
        _sourceId = sourceId;
        _sourceLabel = sourceLabel;
    }

    /// <summary>The finalized chunks accumulated so far, in order.</summary>
    public IReadOnlyList<TranscriptChunk> Chunks => _chunks;

    /// <summary>
    /// Converts <paramref name="sentences"/> to chunks (stamped with
    /// <paramref name="timestamp"/>), appends them, and returns just the chunks
    /// that were added, so the caller can raise one event per new line. Blank or
    /// whitespace-only sentences are skipped.
    /// </summary>
    public IReadOnlyList<TranscriptChunk> Append(
        IReadOnlyList<TranscriptionSegment> sentences,
        DateTimeOffset timestamp)
    {
        if (sentences.Count == 0) return Array.Empty<TranscriptChunk>();

        List<TranscriptChunk>? added = null;
        foreach (var sentence in sentences)
        {
            var text = sentence.Text.Trim();
            if (text.Length == 0) continue;
            var chunk = new TranscriptChunk(text, timestamp, _sourceLabel, _sourceId, IsFinal: true);
            _chunks.Add(chunk);
            (added ??= new List<TranscriptChunk>(sentences.Count)).Add(chunk);
        }
        return (IReadOnlyList<TranscriptChunk>?)added ?? Array.Empty<TranscriptChunk>();
    }

    /// <summary>Drops all accumulated chunks.</summary>
    public void Clear() => _chunks.Clear();
}

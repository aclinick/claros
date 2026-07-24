namespace Windows.Speech;

/// <summary>
/// One finalized transcript line from a single call leg: a completed, punctuated
/// sentence attributed to one speaker. This mirrors the <c>transcript_chunk</c>
/// payload emitted by the Contoso-Finance listener workers (the Mac
/// <c>AudioService</c> and the .NET <c>AudioWorker</c>), so a Windows leg built on
/// <see cref="CallLegTranscriber"/> is a drop-in functional match: one recognizer
/// per speaker, "finals only" output, merged time-ordered across legs.
/// </summary>
/// <param name="Content">
/// The finalized sentence text, already punctuated and capitalized by the
/// on-device model (with inverse text normalization applied).
/// </param>
/// <param name="Timestamp">Wall-clock time the sentence was finalized.</param>
/// <param name="Speaker">Human-readable speaker label for the leg (e.g. "Anna").</param>
/// <param name="SpeakerType">Stable source identifier for the leg (e.g. "advisor").</param>
/// <param name="IsFinal">
/// Always <c>true</c>: this listener emits only finalized sentences, never
/// volatile partials. The field is kept for wire-shape parity with the reference
/// workers.
/// </param>
public sealed record TranscriptChunk(
    string Content,
    DateTimeOffset Timestamp,
    string Speaker,
    string SpeakerType,
    bool IsFinal = true);

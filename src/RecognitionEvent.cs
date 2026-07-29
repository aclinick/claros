namespace Claros;

/// <summary>
/// What a <see cref="RecognitionEvent"/> represents in the streaming recognition
/// life cycle of one spoken turn.
/// </summary>
public enum RecognitionEventKind
{
    /// <summary>
    /// A volatile, in-progress hypothesis for the sentence currently being spoken.
    /// Its text may still be revised (or withdrawn) by later events; do not treat
    /// it as committed. Exactly the trailing, not-yet-finalized sentence.
    /// </summary>
    Partial,

    /// <summary>
    /// A newly finalized sentence whose boundary the recognizer has confirmed.
    /// This is a fresh sentence at <see cref="RecognitionEvent.SentenceIndex"/>
    /// that has not been surfaced before.
    /// </summary>
    Final,

    /// <summary>
    /// A revision of a sentence that was previously surfaced as
    /// <see cref="Final"/>: the recognizer changed the text at
    /// <see cref="RecognitionEvent.SentenceIndex"/> after the fact (for example it
    /// attached a later word or applied inverse text normalization). The new
    /// <see cref="RecognitionEvent.Text"/> replaces the earlier value at that
    /// index. Still a final, not a partial.
    /// </summary>
    Correction,
}

/// <summary>
/// One event from a live recognition stream. Unlike <see cref="TranscriptChunk"/>
/// (whose <c>IsFinal</c> is always <c>true</c> for wire-shape parity), this models
/// the real streaming life cycle: a volatile <see cref="RecognitionEventKind.Partial"/>
/// hypothesis, a confirmed <see cref="RecognitionEventKind.Final"/> sentence, or a
/// <see cref="RecognitionEventKind.Correction"/> that retracts and replaces a
/// sentence previously surfaced as final. Finalized sentences carry a stable
/// <see cref="SentenceIndex"/> within the session so a consumer can reconcile a
/// correction against the line it already showed.
/// </summary>
/// <param name="Kind">The role this event plays in the recognition life cycle.</param>
/// <param name="Text">
/// The event's text: the trailing in-progress hypothesis for a
/// <see cref="RecognitionEventKind.Partial"/>, or the sentence text for a
/// <see cref="RecognitionEventKind.Final"/> or
/// <see cref="RecognitionEventKind.Correction"/>. Already punctuated and
/// capitalized (with inverse text normalization) by the on-device model.
/// </param>
/// <param name="SentenceIndex">
/// The zero-based, session-stable index of the sentence for a
/// <see cref="RecognitionEventKind.Final"/> or
/// <see cref="RecognitionEventKind.Correction"/>; <c>-1</c> for a
/// <see cref="RecognitionEventKind.Partial"/>, which has no committed position.
/// </param>
public sealed record RecognitionEvent(RecognitionEventKind Kind, string Text, int SentenceIndex)
{
    /// <summary>
    /// Whether this event is a finalized sentence (<see cref="RecognitionEventKind.Final"/>
    /// or <see cref="RecognitionEventKind.Correction"/>) rather than a volatile
    /// <see cref="RecognitionEventKind.Partial"/>.
    /// </summary>
    public bool IsFinal => Kind != RecognitionEventKind.Partial;

    /// <summary>A volatile partial hypothesis (no committed sentence index).</summary>
    public static RecognitionEvent Partial(string text) =>
        new(RecognitionEventKind.Partial, text, -1);

    /// <summary>A newly confirmed sentence at <paramref name="sentenceIndex"/>.</summary>
    public static RecognitionEvent Final(string text, int sentenceIndex) =>
        new(RecognitionEventKind.Final, text, sentenceIndex);

    /// <summary>A revision of the previously-final sentence at <paramref name="sentenceIndex"/>.</summary>
    public static RecognitionEvent Correction(string text, int sentenceIndex) =>
        new(RecognitionEventKind.Correction, text, sentenceIndex);
}

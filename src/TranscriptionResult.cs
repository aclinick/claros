namespace WindowsNaturalVoices;

/// <summary>
/// One recognized span of a transcript: the text of a phrase together with its
/// position in the source audio.
/// </summary>
/// <param name="Text">
/// The recognized text, already punctuated and capitalized by the on-device
/// model (with inverse text normalization applied, so numbers, times, and the
/// like appear in written form).
/// </param>
/// <param name="Offset">Start time of the phrase, measured from the beginning of the audio.</param>
/// <param name="Duration">Duration of the phrase.</param>
public sealed record TranscriptionSegment(string Text, TimeSpan Offset, TimeSpan Duration);

/// <summary>
/// The result of transcribing an audio source: the full text plus the
/// individual recognized <see cref="Segments"/> with their timings.
/// </summary>
/// <param name="Text">
/// The complete transcript: every segment's text joined with single spaces.
/// </param>
/// <param name="Segments">The recognized phrases in order, each with a timing.</param>
public sealed record TranscriptionResult(string Text, IReadOnlyList<TranscriptionSegment> Segments)
{
    /// <summary>An empty result (no speech recognized).</summary>
    public static TranscriptionResult Empty { get; } =
        new(string.Empty, Array.Empty<TranscriptionSegment>());

    /// <summary>
    /// Build a <see cref="TranscriptionResult"/> from recognized
    /// <paramref name="segments"/>, joining their text into <see cref="Text"/>.
    /// Segments are taken in the order supplied.
    /// </summary>
    public static TranscriptionResult FromSegments(IReadOnlyList<TranscriptionSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0) return Empty;
        var text = string.Join(" ", segments.Select(s => s.Text.Trim()).Where(t => t.Length > 0));
        return new TranscriptionResult(text, segments);
    }
}

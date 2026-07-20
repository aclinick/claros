namespace WindowsNaturalVoices.Internal;

/// <summary>
/// Splits a continuous recognized transcript into sentence-level
/// <see cref="TranscriptionSegment"/>s. The on-device recognizer already
/// punctuates and capitalizes its output, so a sentence boundary is a
/// terminating <c>.</c>, <c>?</c>, or <c>!</c> that is either the end of the
/// text or followed by a space.
/// </summary>
internal static class TranscriptSegmenter
{
    /// <summary>
    /// Splits <paramref name="text"/> into ordered sentence segments (with zero
    /// timings) and returns them together with the trimmed full text. Returns
    /// <see cref="TranscriptionResult.Empty"/> when the text has no content.
    /// </summary>
    public static TranscriptionResult Split(string? text)
    {
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0) return TranscriptionResult.Empty;

        var segments = new List<TranscriptionSegment>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if ((c == '.' || c == '?' || c == '!') &&
                (i + 1 >= text.Length || text[i + 1] == ' '))
            {
                AddSentence(segments, text, start, i + 1);
                start = i + 1;
            }
        }
        AddSentence(segments, text, start, text.Length);

        return new TranscriptionResult(text, segments);
    }

    private static void AddSentence(List<TranscriptionSegment> segments, string text, int start, int end)
    {
        if (end <= start) return;
        var sentence = text[start..end].Trim();
        if (sentence.Length > 0)
        {
            segments.Add(new TranscriptionSegment(sentence, TimeSpan.Zero, TimeSpan.Zero));
        }
    }
}

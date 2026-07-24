using System.Text;

namespace Windows.Speech;

/// <summary>
/// Merges consecutive <see cref="TimedCue"/>s into sentence-sized cues. A group
/// closes when the accumulated text ends a sentence (<c>. ! ? …</c>) or when a
/// long silent gap to the next cue implies a deliberate pause. Narrating whole
/// sentences keeps a synthesizer's intonation continuous, instead of the flat,
/// clipped delivery you get when each cue fragment is spoken (and ended) on its
/// own. Each merged cue keeps the first source cue's <see cref="TimedCue.Index"/>
/// and <see cref="TimedCue.Start"/>, and the last cue's <see cref="TimedCue.End"/>.
/// </summary>
public static class CueSentenceGrouper
{
    /// <summary>The default silent gap that forces a sentence break.</summary>
    public static readonly TimeSpan DefaultMaxGap = TimeSpan.FromSeconds(1.2);

    /// <summary>
    /// Groups <paramref name="cues"/> (assumed ordered by start) into sentence
    /// cues, breaking whenever the gap to the next cue exceeds
    /// <paramref name="maxGap"/>. Returns an empty list for empty input.
    /// </summary>
    public static IReadOnlyList<TimedCue> GroupIntoSentences(
        IReadOnlyList<TimedCue> cues, TimeSpan? maxGap = null)
    {
        ArgumentNullException.ThrowIfNull(cues);
        var gap = maxGap ?? DefaultMaxGap;

        var groups = new List<TimedCue>();
        int? firstIndex = null;
        TimeSpan start = default;
        var text = new StringBuilder();

        for (var i = 0; i < cues.Count; i++)
        {
            var cue = cues[i];
            if (firstIndex is null)
            {
                firstIndex = cue.Index;
                start = cue.Start;
                text.Clear();
                text.Append(cue.Text);
            }
            else
            {
                text.Append(' ').Append(cue.Text);
            }

            var closesSentence = EndsSentence(text);
            var gapBreak = i + 1 < cues.Count && cues[i + 1].Start - cue.End > gap;
            if (closesSentence || gapBreak || i == cues.Count - 1)
            {
                groups.Add(new TimedCue(firstIndex.Value, start, cue.End, text.ToString()));
                firstIndex = null;
            }
        }

        return groups;
    }

    // Look past trailing quotes/brackets/whitespace for sentence-ending punctuation.
    private static bool EndsSentence(StringBuilder sb)
    {
        for (var i = sb.Length - 1; i >= 0; i--)
        {
            var c = sb[i];
            if (c is '"' or '\'' or ')' or ']' or '”' or '’' or '»' or ' ') continue;
            return c is '.' or '!' or '?' or '…';
        }
        return false;
    }
}

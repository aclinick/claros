namespace Claros.Internal;

/// <summary>
/// Computes the newly committed text of a live transcript. The on-device
/// recognizer produces an ever-growing hypothesis that occasionally revises
/// earlier words (punctuation, capitalization). Given what was already committed
/// and the current full hypothesis, this returns only the text past their common
/// prefix, so a late revision re-emits just the changed tail rather than
/// replaying the whole transcript.
/// </summary>
internal static class TranscriptDelta
{
    /// <summary>
    /// Returns the trimmed text of <paramref name="current"/> beyond its common
    /// prefix with <paramref name="committed"/>. Empty when nothing new has
    /// accumulated.
    /// </summary>
    public static string Compute(string committed, string current)
    {
        committed ??= string.Empty;
        current ??= string.Empty;
        var prefix = CommonPrefixLength(committed, current);
        return current[prefix..].Trim();
    }

    /// <summary>Length of the shared leading run of characters of two strings.</summary>
    public static int CommonPrefixLength(string a, string b)
    {
        var max = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < max && a[i] == b[i]) i++;
        return i;
    }
}

using Windows.Speech.Internal;

namespace Windows.Speech.Tests;

public class TranscriptDeltaTests
{
    [Fact]
    public void Compute_GrowingText_ReturnsOnlyNewTail()
    {
        var delta = TranscriptDelta.Compute("Hello world", "Hello world today");

        Assert.Equal("today", delta);
    }

    [Fact]
    public void Compute_FirstCommit_ReturnsWholeText()
    {
        var delta = TranscriptDelta.Compute("", "Hello there");

        Assert.Equal("Hello there", delta);
    }

    [Fact]
    public void Compute_NoNewText_ReturnsEmpty()
    {
        Assert.Equal("", TranscriptDelta.Compute("Same text", "Same text"));
    }

    [Fact]
    public void Compute_LateRevision_ReemitsOnlyFromDivergence_NotWholeTranscript()
    {
        // The engine revised "resets understandable" into "resets. Understandable".
        var committed = "when it resets understandable";
        var current = "when it resets. Understandable if you paid";

        var delta = TranscriptDelta.Compute(committed, current);

        // Only the changed tail from the divergence point is re-emitted, not the
        // entire transcript from the beginning.
        Assert.Equal(". Understandable if you paid", delta);
        Assert.DoesNotContain("when it resets", delta);
    }

    [Fact]
    public void Compute_TrimsWhitespace()
    {
        Assert.Equal("world", TranscriptDelta.Compute("Hello", "Hello   world  "));
    }

    [Fact]
    public void Compute_HandlesNulls()
    {
        Assert.Equal("", TranscriptDelta.Compute(null!, null!));
        Assert.Equal("hi", TranscriptDelta.Compute(null!, "hi"));
        Assert.Equal("", TranscriptDelta.Compute("hi", null!));
    }

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("abc", "abd", 2)]
    [InlineData("abc", "abc", 3)]
    [InlineData("abc", "ab", 2)]
    [InlineData("xyz", "abc", 0)]
    public void CommonPrefixLength_IsCorrect(string a, string b, int expected)
    {
        Assert.Equal(expected, TranscriptDelta.CommonPrefixLength(a, b));
    }
}

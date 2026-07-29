using Claros.Internal;

namespace Claros.Tests;

public class TranscriptSegmenterTests
{
    [Fact]
    public void Split_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Same(TranscriptionResult.Empty, TranscriptSegmenter.Split(null));
        Assert.Same(TranscriptionResult.Empty, TranscriptSegmenter.Split(""));
        Assert.Same(TranscriptionResult.Empty, TranscriptSegmenter.Split("   "));
    }

    [Fact]
    public void Split_SplitsOnSentenceTerminators()
    {
        var result = TranscriptSegmenter.Split(
            "The quick brown fox jumps over the lazy dog. I have three cats! Is it half past four?");

        Assert.Equal(3, result.Segments.Count);
        Assert.Equal("The quick brown fox jumps over the lazy dog.", result.Segments[0].Text);
        Assert.Equal("I have three cats!", result.Segments[1].Text);
        Assert.Equal("Is it half past four?", result.Segments[2].Text);
    }

    [Fact]
    public void Split_KeepsUnterminatedTailAsFinalSegment()
    {
        var result = TranscriptSegmenter.Split("First sentence. And a trailing thought");

        Assert.Equal(2, result.Segments.Count);
        Assert.Equal("First sentence.", result.Segments[0].Text);
        Assert.Equal("And a trailing thought", result.Segments[1].Text);
    }

    [Fact]
    public void Split_DoesNotBreakDecimalNumbers()
    {
        // A period not followed by a space (as in "6.2") is not a boundary.
        var result = TranscriptSegmenter.Split("Returns are 6.2 percent this year.");

        Assert.Single(result.Segments);
        Assert.Equal("Returns are 6.2 percent this year.", result.Segments[0].Text);
    }

    [Fact]
    public void Split_FullTextIsTrimmedInput()
    {
        var result = TranscriptSegmenter.Split("  Hello world.  ");

        Assert.Equal("Hello world.", result.Text);
        Assert.Single(result.Segments);
        Assert.Equal("Hello world.", result.Segments[0].Text);
    }

    [Fact]
    public void Split_SingleSentenceWithoutTerminator()
    {
        var result = TranscriptSegmenter.Split("just a phrase");

        Assert.Single(result.Segments);
        Assert.Equal("just a phrase", result.Segments[0].Text);
        Assert.Equal("just a phrase", result.Text);
    }

    [Fact]
    public void Split_SegmentsHaveZeroTimings()
    {
        var result = TranscriptSegmenter.Split("One. Two.");

        Assert.All(result.Segments, s =>
        {
            Assert.Equal(TimeSpan.Zero, s.Offset);
            Assert.Equal(TimeSpan.Zero, s.Duration);
        });
    }
}

namespace Claros.Tests;

public class TranscriptionResultTests
{
    [Fact]
    public void Empty_HasNoTextOrSegments()
    {
        Assert.Equal(string.Empty, TranscriptionResult.Empty.Text);
        Assert.Empty(TranscriptionResult.Empty.Segments);
    }

    [Fact]
    public void FromSegments_JoinsTextWithSingleSpaces()
    {
        var segments = new[]
        {
            new TranscriptionSegment("Hello world.", TimeSpan.Zero, TimeSpan.Zero),
            new TranscriptionSegment("How are you?", TimeSpan.FromSeconds(2), TimeSpan.Zero),
        };

        var result = TranscriptionResult.FromSegments(segments);

        Assert.Equal("Hello world. How are you?", result.Text);
        Assert.Equal(2, result.Segments.Count);
    }

    [Fact]
    public void FromSegments_SkipsBlankSegmentTextWhenJoining()
    {
        var segments = new[]
        {
            new TranscriptionSegment("Alpha", TimeSpan.Zero, TimeSpan.Zero),
            new TranscriptionSegment("   ", TimeSpan.Zero, TimeSpan.Zero),
            new TranscriptionSegment("Beta", TimeSpan.Zero, TimeSpan.Zero),
        };

        var result = TranscriptionResult.FromSegments(segments);

        Assert.Equal("Alpha Beta", result.Text);
        // Blank segment is still preserved in the list; only the joined text skips it.
        Assert.Equal(3, result.Segments.Count);
    }

    [Fact]
    public void FromSegments_EmptyList_ReturnsEmpty()
    {
        Assert.Same(TranscriptionResult.Empty, TranscriptionResult.FromSegments(Array.Empty<TranscriptionSegment>()));
    }

    [Fact]
    public void FromSegments_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => TranscriptionResult.FromSegments(null!));
    }

    [Fact]
    public void TranscriptionSegment_StoresFields()
    {
        var segment = new TranscriptionSegment("text", TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(500));

        Assert.Equal("text", segment.Text);
        Assert.Equal(TimeSpan.FromSeconds(3), segment.Offset);
        Assert.Equal(TimeSpan.FromMilliseconds(500), segment.Duration);
    }
}
